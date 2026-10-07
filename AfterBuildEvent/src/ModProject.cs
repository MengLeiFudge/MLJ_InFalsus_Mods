using System.Text.Json;
using System.Xml.Linq;

namespace AfterBuildEvent;

/// <summary>从模组 csproj 和发布素材读取的打包合同。</summary>
internal sealed class ModProject
{
    internal string Directory { get; }
    internal string PackageId { get; }
    internal string AssemblyName { get; }
    internal string Version { get; }
    internal string ThunderstoreNamespace { get; }
    internal string LegacyDllName { get; }

    internal string DllFileName => $"{AssemblyName}.dll";
    internal string ManifestPath => Path.Combine(Directory, "Assets", "manifest.json");
    internal string IconPath => Path.Combine(Directory, "Assets", "icon.png");
    internal string ReadmePath => Path.Combine(Directory, "README.md");
    internal string ChangeLogPath => Path.Combine(Directory, "CHANGELOG.md");

    private ModProject(
        string directory,
        string packageId,
        string assemblyName,
        string version,
        string thunderstoreNamespace,
        string legacyDllName)
    {
        Directory = directory;
        PackageId = packageId;
        AssemblyName = assemblyName;
        Version = version;
        ThunderstoreNamespace = thunderstoreNamespace;
        LegacyDllName = legacyDllName;
    }

    /// <summary>发现仓库根目录下一层中显式标记为 IsModProject 的项目。</summary>
    /// <param name="rootDirectory">解决方案根目录。</param>
    /// <returns>按 PackageId 排序的模组项目。</returns>
    internal static IReadOnlyList<ModProject> Discover(string rootDirectory)
    {
        var projects = new List<ModProject>();
        foreach (string directory in System.IO.Directory.EnumerateDirectories(rootDirectory))
        {
            foreach (string projectPath in System.IO.Directory.EnumerateFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly))
            {
                var document = XDocument.Load(projectPath);
                if (!GetProperty(document, "IsModProject").Equals("true", StringComparison.OrdinalIgnoreCase))
                    continue;

                string packageId = RequireProperty(document, "PackageId", projectPath);
                string assemblyName = GetProperty(document, "AssemblyName");
                if (string.IsNullOrWhiteSpace(assemblyName))
                    assemblyName = Path.GetFileNameWithoutExtension(projectPath);
                projects.Add(new ModProject(
                    directory,
                    packageId,
                    assemblyName,
                    RequireProperty(document, "Version", projectPath),
                    RequireProperty(document, "ThunderstoreNamespace", projectPath),
                    GetProperty(document, "LegacyDllName")));
            }
        }

        if (projects.Count == 0)
            throw new InvalidDataException("解决方案中没有 IsModProject=true 的模组项目。");
        return projects.OrderBy(project => project.PackageId, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>核对发布素材与项目元数据，避免生成无法上传或版本错位的包。</summary>
    internal void ValidateAssets()
    {
        foreach (string path in new[] { ManifestPath, IconPath, ReadmePath, ChangeLogPath })
            if (!File.Exists(path))
                throw new FileNotFoundException($"{PackageId} 缺少发布文件：{Path.GetRelativePath(Directory, path)}", path);

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(ManifestPath));
        JsonElement root = manifest.RootElement;
        RequireManifestValue(root, "namespace", ThunderstoreNamespace);
        RequireManifestValue(root, "name", PackageId);
        RequireManifestValue(root, "version_number", Version);
        if (!root.TryGetProperty("dependencies", out JsonElement dependencies)
            || dependencies.ValueKind != JsonValueKind.Array
            || !dependencies.EnumerateArray().Any(value => value.GetString() == "BepInEx-BepInExPack_IL2CPP-6.0.755"))
            throw new InvalidDataException($"{PackageId}/Assets/manifest.json 缺少 BepInEx-BepInExPack_IL2CPP-6.0.755 依赖。");
    }

    private static string GetProperty(XDocument document, string name) => document
        .Descendants("PropertyGroup")
        .Elements(name)
        .Select(element => element.Value.Trim())
        .LastOrDefault() ?? "";

    private static string RequireProperty(XDocument document, string name, string projectPath)
    {
        string value = GetProperty(document, name);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"{projectPath} 缺少 {name}。");
        return value;
    }

    private static void RequireManifestValue(JsonElement root, string name, string expected)
    {
        if (!root.TryGetProperty(name, out JsonElement value) || value.GetString() != expected)
            throw new InvalidDataException($"manifest.json 的 {name} 必须为 {expected}。");
    }
}
