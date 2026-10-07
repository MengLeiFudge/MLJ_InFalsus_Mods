using System.Text;

namespace AfterBuildEvent;

/// <summary>让 Steam 直接启动通过游戏根目录的 Doorstop 加载指定 r2modman profile。</summary>
internal static class DoorstopConfigurator
{
    /// <summary>从 profile 配置中必须解析并重写的 IL2CPP Doorstop 项。</summary>
    private static readonly string[] RequiredKeys =
    {
        "enabled",
        "target_assembly",
        "ignore_disable_switch",
        "coreclr_path",
        "corlib_dir",
    };

    /// <summary>复制 profile 的 Doorstop 入口，并把所有运行时路径改为 profile 的绝对路径。</summary>
    /// <param name="context">已验证的游戏和 Default profile 路径。</param>
    internal static void Configure(RepositoryContext context)
    {
        string sourceProxy = Path.Combine(context.ProfileDirectory, "winhttp.dll");
        string sourceConfig = Path.Combine(context.ProfileDirectory, "doorstop_config.ini");
        RequireFile(sourceProxy, "Default profile 缺少 Doorstop 代理");
        RequireFile(sourceConfig, "Default profile 缺少 Doorstop 配置");

        string[] lines = File.ReadAllLines(sourceConfig);
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < lines.Length; index++)
        {
            int separator = lines[index].IndexOf('=');
            if (separator < 0)
                continue;

            string key = lines[index][..separator].Trim();
            if (!RequiredKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                continue;
            if (!found.Add(key))
                throw new InvalidDataException($"Default profile 的 doorstop_config.ini 重复定义 {key}。");

            string value = lines[index][(separator + 1)..].Trim();
            lines[index] = key.ToLowerInvariant() switch
            {
                "enabled" => "enabled = true",
                "ignore_disable_switch" => "ignore_disable_switch = false",
                "target_assembly" => $"target_assembly = {ResolveRequiredPath(context.ProfileDirectory, value, false, key)}",
                "coreclr_path" => $"coreclr_path = {ResolveRequiredPath(context.ProfileDirectory, value, false, key)}",
                "corlib_dir" => $"corlib_dir = {ResolveRequiredPath(context.ProfileDirectory, value, true, key)}",
                _ => throw new InvalidOperationException($"未处理 Doorstop 配置项：{key}"),
            };
        }

        string[] missing = RequiredKeys.Where(key => !found.Contains(key)).ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException($"Default profile 的 doorstop_config.ini 缺少：{string.Join(", ", missing)}");

        string targetProxy = Path.Combine(context.GameDirectory, "winhttp.dll");
        string targetConfig = Path.Combine(context.GameDirectory, "doorstop_config.ini");
        string proxyTemporary = targetProxy + ".tmp";
        string configTemporary = targetConfig + ".tmp";
        try
        {
            File.Copy(sourceProxy, proxyTemporary, true);
            File.WriteAllLines(configTemporary, lines, new UTF8Encoding(false));
            File.Move(proxyTemporary, targetProxy, true);
            File.Move(configTemporary, targetConfig, true);
        }
        finally
        {
            if (File.Exists(proxyTemporary))
                File.Delete(proxyTemporary);
            if (File.Exists(configTemporary))
                File.Delete(configTemporary);
        }

        Console.WriteLine($"Steam 直启已绑定 r2 profile：{context.ProfileDirectory}");
    }

    /// <summary>把 profile 内的相对路径解析为绝对路径，并确认运行时目标存在。</summary>
    /// <param name="profileDirectory">相对路径的基准 profile。</param>
    /// <param name="configured">Doorstop 配置中的原值。</param>
    /// <param name="directory">目标是否必须为目录。</param>
    /// <param name="key">用于错误信息的配置项名称。</param>
    /// <returns>经过规范化的现有绝对路径。</returns>
    private static string ResolveRequiredPath(string profileDirectory, string configured, bool directory, string key)
    {
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidDataException($"Default profile 的 doorstop_config.ini 未设置 {key}。");

        string path = Path.IsPathFullyQualified(configured)
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(configured, profileDirectory);
        if (directory)
        {
            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException($"Doorstop 配置项 {key} 指向的目录不存在：{path}");
        }
        else if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Doorstop 配置项 {key} 指向的文件不存在。", path);
        }
        return path;
    }

    /// <summary>确认发布所需文件存在。</summary>
    /// <param name="path">待检查的文件。</param>
    /// <param name="message">文件缺失时的上下文。</param>
    private static void RequireFile(string path, string message)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(message, path);
    }
}
