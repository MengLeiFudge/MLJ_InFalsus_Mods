using System.Text;

namespace AfterBuildEvent;

/// <summary>提供本地模组交付和独立的角色立绘提取命令。</summary>
/// <remarks>交付流程串行关闭游戏、更新模组、修正引导路径并推送 QQ 群文件；不发布 Thunderstore。</remarks>
internal static class Program
{
#if DEBUG
    private const string DefaultConfiguration = "Debug";
#else
    private const string DefaultConfiguration = "Release";
#endif

    /// <summary>工具入口；返回非零表示至少一个必需输入或输出失败。</summary>
    /// <param name="args">extract-characters 提取选项，或模式 1、构建配置、PackageId 和 --message-file 发布说明。</param>
    /// <returns>进程退出码。</returns>
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            if (args.Length > 0 && args[0].Equals("extract-characters", StringComparison.OrdinalIgnoreCase))
                return await CharacterExtractor.RunAsync(args[1..]);

            var invocation = PublishInvocation.Parse(args, DefaultConfiguration);
            var context = RepositoryContext.Load();
            var allProjects = ModProject.Discover(context.RootDirectory);
            var projects = invocation.SelectProjects(allProjects);

            Console.WriteLine($"构建配置：{invocation.Configuration}");
            Console.WriteLine($"本地模组目录：{context.LocalPluginsDirectory}");
            GameProcess.Stop(context);
            var packages = new List<ModPackage>();
            foreach (var project in projects)
                packages.Add(ModPublisher.Publish(context, project, invocation.Configuration));
            DoorstopConfigurator.Configure(context);
            await QqPublisher.PublishAsync(context, packages, invocation.MessageFile);

            Console.WriteLine($"完成：已更新并打包 {projects.Count} 个本地模组，修正 Doorstop 路径并完成群文件推送或去重跳过；未发布 Thunderstore。");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"执行中断：{ex.Message}");
            return 1;
        }
    }
}

/// <summary>一次发布命令选择的构建配置和模组范围。</summary>
internal sealed class PublishInvocation
{
    /// <summary>Debug 或 Release 输出目录名。</summary>
    internal string Configuration { get; }

    /// <summary>为空时处理所有模组，否则只处理这些 PackageId。</summary>
    private HashSet<string> PackageIds { get; }

    /// <summary>可选的发布说明文件；省略时由工作区状态决定使用提交正文或自动生成构建说明。</summary>
    internal string? MessageFile { get; }

    private PublishInvocation(string configuration, HashSet<string> packageIds, string? messageFile)
    {
        Configuration = configuration;
        PackageIds = packageIds;
        MessageFile = messageFile;
    }

    /// <summary>兼容参考仓库的模式 1，并读取可选配置与项目筛选。</summary>
    /// <param name="args">命令行参数。</param>
    /// <param name="defaultConfiguration">当前工具自身的构建配置。</param>
    /// <returns>已规范化的调用参数。</returns>
    internal static PublishInvocation Parse(IReadOnlyList<string> args, string defaultConfiguration)
    {
        int index = 0;
        if (args.Count > 0 && args[0] == "1")
            index++;
        else if (args.Count > 0 && int.TryParse(args[0], out _))
            throw new ArgumentException("当前只支持模式 1：本地部署并打包模组。");

        string configuration = defaultConfiguration;
        if (index < args.Count && (args[index].Equals("Debug", StringComparison.OrdinalIgnoreCase)
                                   || args[index].Equals("Release", StringComparison.OrdinalIgnoreCase)))
        {
            configuration = char.ToUpperInvariant(args[index][0]) + args[index][1..].ToLowerInvariant();
            index++;
        }

        var packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? messageFile = null;
        for (; index < args.Count; index++)
        {
            if (args[index] == "--message-file")
            {
                if (messageFile != null || ++index >= args.Count || string.IsNullOrWhiteSpace(args[index]))
                    throw new ArgumentException("--message-file 必须且只能提供一个 UTF-8 文件路径。");
                messageFile = args[index];
                continue;
            }
            if (args[index].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"未知选项：{args[index]}");
            foreach (string value in args[index].Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                packageIds.Add(value.Trim());
        }
        return new PublishInvocation(configuration, packageIds, messageFile);
    }

    /// <summary>按 PackageId 选择项目，并拒绝静默忽略拼错的名称。</summary>
    /// <param name="projects">仓库内全部模组项目。</param>
    /// <returns>稳定排序后的发布项目。</returns>
    internal IReadOnlyList<ModProject> SelectProjects(IReadOnlyList<ModProject> projects)
    {
        if (PackageIds.Count == 0)
            return projects;

        var selected = projects.Where(project => PackageIds.Contains(project.PackageId)).ToArray();
        var found = selected.Select(project => project.PackageId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] missing = PackageIds.Where(id => !found.Contains(id)).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
        if (missing.Length > 0)
            throw new ArgumentException($"未找到模组项目：{string.Join(", ", missing)}");
        return selected;
    }
}
