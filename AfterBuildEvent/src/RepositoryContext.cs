using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AfterBuildEvent;

/// <summary>发布工具使用的仓库根目录、本地 profile 和压缩包输出目录。</summary>
internal sealed class RepositoryContext
{
    /// <summary>包含解决方案和 Directory.Build.props 的仓库目录。</summary>
    internal string RootDirectory { get; }

    /// <summary>r2modman Default profile 根目录，持有 BepInEx、Doorstop 和 CoreCLR。</summary>
    internal string ProfileDirectory { get; }

    /// <summary>Steam 游戏根目录，包含 Doorstop 引导文件及相对路径引用的 CoreCLR。</summary>
    internal string GameDirectory { get; }

    /// <summary>本次部署写入的 BepInEx plugins 目录。</summary>
    internal string LocalPluginsDirectory { get; }

    /// <summary>需要停用旧合并 DLL 的现有插件目录，包括部署目标和已配置的游戏目录。</summary>
    internal IReadOnlyList<string> LegacyPluginsDirectories { get; }

    /// <summary>用户为本项目配置的群文件接收群；未配置时不得推断其他项目的目标群。</summary>
    internal IReadOnlyList<long> PublishGroupIds { get; }

    /// <summary>QQ 群文件夹 ID；空字符串表示最外层，与群号共同确定上传和去重位置。</summary>
    internal string PublishFolderId { get; private init; } = "";

    /// <summary>独立 Thunderstore ZIP 的输出目录。</summary>
    internal string ModZipsDirectory => Path.Combine(RootDirectory, "ModZips");

    private RepositoryContext(
        string rootDirectory,
        string profileDirectory,
        string gameDirectory,
        string localPluginsDirectory,
        IReadOnlyList<string> legacyPluginsDirectories,
        IReadOnlyList<long> publishGroupIds)
    {
        RootDirectory = rootDirectory;
        ProfileDirectory = profileDirectory;
        GameDirectory = gameDirectory;
        LocalPluginsDirectory = localPluginsDirectory;
        LegacyPluginsDirectories = legacyPluginsDirectories;
        PublishGroupIds = publishGroupIds;
    }

    /// <summary>从工具输出目录向上定位仓库，并解析未提交的本机路径配置。</summary>
    /// <returns>已验证的发布路径。</returns>
    internal static RepositoryContext Load()
    {
        string root = FindRoot();
        string configPath = Path.Combine(root, "DefaultPath.props");
        if (!File.Exists(configPath))
            throw new FileNotFoundException("缺少 DefaultPath.props；请复制 DefaultPath.props.example 并填写本机路径。", configPath);

        var properties = ReadProperties(configPath);
        string folderId = properties.TryGetValue("QqFolderId", out string? folder) ? folder : "";
        if (folderId.Length > 256 || folderId.Any(char.IsControl))
            throw new InvalidDataException("DefaultPath.props 的 QqFolderId 必须为 QQ 返回的文件夹 ID，不能含控制字符。");
        if (folderId == "/")
            folderId = "";
        string profileDirectory = RequireDirectory(properties, "ProfileDir", root, "r2modman profile");
        string gameDirectory = RequireDirectory(properties, "GameDir", root, "游戏");

        string localPlugins;
        if (properties.TryGetValue("LocalPluginsDir", out string? configured) && !string.IsNullOrWhiteSpace(configured))
            localPlugins = ExpandPath(configured, properties, root);
        else
            localPlugins = Path.Combine(profileDirectory, "BepInEx", "plugins");

        Directory.CreateDirectory(localPlugins);
        var legacyPluginsDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            localPlugins,
        };
        string gamePluginsDirectory = Path.Combine(gameDirectory, "BepInEx", "plugins");
        if (Directory.Exists(gamePluginsDirectory))
            legacyPluginsDirectories.Add(gamePluginsDirectory);

        return new RepositoryContext(
            root,
            profileDirectory,
            gameDirectory,
            localPlugins,
            legacyPluginsDirectories.ToArray(),
            ReadGroupIds(properties))
        {
            PublishFolderId = folderId,
        };
    }

    /// <summary>只读取仓库与游戏路径，供不需要部署 profile 的资源提取命令使用。</summary>
    /// <returns>仓库根目录和已验证存在的游戏目录；不创建目录。</returns>
    internal static (string RootDirectory, string GameDirectory) LoadGamePaths()
    {
        string root = FindRoot();
        string configPath = Path.Combine(root, "DefaultPath.props");
        if (!File.Exists(configPath))
            throw new FileNotFoundException("缺少 DefaultPath.props；请复制 DefaultPath.props.example 并填写本机路径。", configPath);
        var properties = ReadProperties(configPath);
        return (root, RequireDirectory(properties, "GameDir", root, "游戏"));
    }

    /// <summary>读取本机配置的正整数群号；空配置留给群发布阶段明确报告。</summary>
    /// <param name="properties">DefaultPath.props 中的本机属性。</param>
    /// <returns>去重后的群号列表。</returns>
    private static IReadOnlyList<long> ReadGroupIds(IReadOnlyDictionary<string, string> properties)
    {
        if (!properties.TryGetValue("QqGroupIds", out string? configured) || string.IsNullOrWhiteSpace(configured))
            return Array.Empty<long>();
        var groups = new HashSet<long>();
        foreach (string value in configured.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!long.TryParse(value, out long group) || group <= 0)
                throw new InvalidDataException("DefaultPath.props 的 QqGroupIds 必须为正整数群号，以逗号或分号分隔。");
            groups.Add(group);
        }
        return groups.OrderBy(group => group).ToArray();
    }

    private static string FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            bool hasSolution = directory.EnumerateFiles("*.sln", SearchOption.TopDirectoryOnly).Any();
            if (hasSolution && File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException($"无法从工具目录向上定位解决方案：{AppContext.BaseDirectory}");
    }

    private static Dictionary<string, string> ReadProperties(string path)
    {
        var document = XDocument.Load(path);
        return document.Descendants("PropertyGroup")
            .Elements()
            .GroupBy(element => element.Name.LocalName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Value.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    private static string RequireDirectory(
        IReadOnlyDictionary<string, string> properties,
        string propertyName,
        string root,
        string description)
    {
        if (!properties.TryGetValue(propertyName, out string? configured) || string.IsNullOrWhiteSpace(configured))
            throw new InvalidDataException($"DefaultPath.props 必须提供 {propertyName}。");
        string path = ExpandPath(configured, properties, root);
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"{description}目录不存在：{path}");
        return path;
    }

    private static string ExpandPath(string value, IReadOnlyDictionary<string, string> properties, string root)
    {
        string expanded = value;
        for (int pass = 0; pass < 8; pass++)
        {
            string next = Regex.Replace(expanded, @"\$\(([^)]+)\)", match =>
            {
                string key = match.Groups[1].Value;
                if (properties.TryGetValue(key, out string? replacement))
                    return replacement;
                return Environment.GetEnvironmentVariable(key) ?? match.Value;
            });
            if (next == expanded)
                break;
            expanded = next;
        }
        expanded = Environment.ExpandEnvironmentVariables(expanded);
        if (expanded.Contains("$(", StringComparison.Ordinal))
            throw new InvalidDataException($"路径包含无法解析的 MSBuild 属性：{value}");
        return Path.IsPathFullyQualified(expanded) ? Path.GetFullPath(expanded) : Path.GetFullPath(expanded, root);
    }
}
