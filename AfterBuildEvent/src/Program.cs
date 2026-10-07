using System.Text;

namespace AfterBuildEvent;

/// <summary>解析发布范围并串行执行本地部署与 Thunderstore 打包。</summary>
internal static class Program
{
#if DEBUG
    private const string DefaultConfiguration = "Debug";
#else
    private const string DefaultConfiguration = "Release";
#endif

    /// <summary>发布工具入口；返回非零表示至少一个必需输入或输出失败。</summary>
    /// <param name="args">可选的模式 1、构建配置及一个或多个 PackageId。</param>
    /// <returns>进程退出码。</returns>
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            var invocation = PublishInvocation.Parse(args, DefaultConfiguration);
            var context = RepositoryContext.Load();
            var allProjects = ModProject.Discover(context.RootDirectory);
            var projects = invocation.SelectProjects(allProjects);

            Console.WriteLine($"构建配置：{invocation.Configuration}");
            Console.WriteLine($"本地模组目录：{context.LocalPluginsDirectory}");
            foreach (var project in projects)
                ModPublisher.Publish(context, project, invocation.Configuration);
            DoorstopConfigurator.Configure(context);

            Console.WriteLine($"完成：已部署并打包 {projects.Count} 个模组，Steam 直启已绑定 Default profile。");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"发布失败：{ex.Message}");
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

    private PublishInvocation(string configuration, HashSet<string> packageIds)
    {
        Configuration = configuration;
        PackageIds = packageIds;
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
        for (; index < args.Count; index++)
            foreach (string value in args[index].Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                packageIds.Add(value.Trim());
        return new PublishInvocation(configuration, packageIds);
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
