using System.Text;

namespace AfterBuildEvent;

/// <summary>本地模组更新完成后，修正游戏目录的 Doorstop 引导路径以支持 Steam 直启。</summary>
internal static class DoorstopConfigurator
{
    /// <summary>IL2CPP 引导必须具备的配置项；CoreCLR 项只验证目标，不改写其相对路径。</summary>
    private static readonly string[] RequiredKeys =
    {
        "enabled",
        "target_assembly",
        "ignore_disable_switch",
        "coreclr_path",
        "corlib_dir",
    };

    /// <summary>复制 profile 的 Doorstop 入口，将目标程序集指向 profile，保留游戏本地运行时路径。</summary>
    /// <param name="context">已验证且游戏进程已经关闭的游戏和 profile 路径。</param>
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
            switch (key.ToLowerInvariant())
            {
                case "enabled":
                    lines[index] = "enabled = true";
                    break;
                case "ignore_disable_switch":
                    lines[index] = "ignore_disable_switch = false";
                    break;
                case "target_assembly":
                    lines[index] = $"target_assembly = {ResolveRequiredPath(context.ProfileDirectory, value, false, key)}";
                    break;
                case "coreclr_path":
                    ResolveRequiredPath(context.GameDirectory, value, false, key);
                    break;
                case "corlib_dir":
                    ResolveRequiredPath(context.GameDirectory, value, true, key);
                    break;
            }
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

        Console.WriteLine($"Doorstop 路径已修正：目标程序集位于 {context.ProfileDirectory}，CoreCLR 路径保持原配置。");
    }

    /// <summary>按配置项实际使用的根目录解析路径，并确认运行时目标存在。</summary>
    /// <param name="baseDirectory">目标程序集使用 profile，CoreCLR 使用游戏目录。</param>
    /// <param name="configured">Doorstop 配置中的原值。</param>
    /// <param name="directory">目标是否必须为目录。</param>
    /// <param name="key">用于错误信息的配置项名称。</param>
    /// <returns>经过规范化的现有绝对路径。</returns>
    private static string ResolveRequiredPath(string baseDirectory, string configured, bool directory, string key)
    {
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidDataException($"Default profile 的 doorstop_config.ini 未设置 {key}。");

        string path = Path.IsPathFullyQualified(configured)
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(configured, baseDirectory);
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

    /// <summary>确认部署所需的 profile 引导文件存在。</summary>
    /// <param name="path">待检查的文件。</param>
    /// <param name="message">文件缺失时的上下文。</param>
    private static void RequireFile(string path, string message)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(message, path);
    }
}
