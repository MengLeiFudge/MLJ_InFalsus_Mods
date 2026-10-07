using System.Buffers.Binary;
using System.IO.Compression;

namespace AfterBuildEvent;

/// <summary>把一个已构建模组更新到本地 profile，并生成独立 Thunderstore 包。</summary>
internal static class ModPublisher
{
    private static readonly DateTimeOffset StableZipTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>校验、部署并打包指定模组。</summary>
    /// <param name="context">仓库及本地 profile 路径。</param>
    /// <param name="project">模组发布合同。</param>
    /// <param name="configuration">要读取的构建配置。</param>
    internal static void Publish(RepositoryContext context, ModProject project, string configuration)
    {
        project.ValidateAssets();
        ValidateIcon(project.IconPath, project.PackageId);

        string dllPath = Path.Combine(project.Directory, "bin", configuration, project.DllFileName);
        if (!File.Exists(dllPath))
            throw new FileNotFoundException($"{project.PackageId} 尚未构建 {configuration} 输出。", dllPath);

        if (!string.IsNullOrWhiteSpace(project.LegacyDllName))
            foreach (string pluginsDirectory in context.LegacyPluginsDirectories)
                DisableLegacyDlls(pluginsDirectory, project.LegacyDllName);

        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [project.DllFileName] = dllPath,
            ["manifest.json"] = project.ManifestPath,
            ["icon.png"] = project.IconPath,
            ["README.md"] = project.ReadmePath,
            ["CHANGELOG.md"] = project.ChangeLogPath,
        };

        string localDirectory = Path.Combine(context.LocalPluginsDirectory, $"{project.ThunderstoreNamespace}-{project.PackageId}");
        Directory.CreateDirectory(localDirectory);
        foreach ((string name, string source) in files)
            CopyWithRetry(source, Path.Combine(localDirectory, name));
        Console.WriteLine($"本地更新：{project.PackageId} -> {localDirectory}");

        Directory.CreateDirectory(context.ModZipsDirectory);
        string zipPath = Path.Combine(
            context.ModZipsDirectory,
            $"{project.ThunderstoreNamespace}-{project.PackageId}-{project.Version}.zip");
        CreatePackage(files, zipPath);
        Console.WriteLine($"创建包：{zipPath}");
    }

    private static void ValidateIcon(string path, string packageId)
    {
        byte[] header = new byte[24];
        using (FileStream stream = File.OpenRead(path))
        {
            if (stream.Read(header, 0, header.Length) != header.Length)
                throw new InvalidDataException($"{packageId} 的 icon.png 不是完整 PNG。");
        }

        byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        if (!header.AsSpan(0, 8).SequenceEqual(signature))
            throw new InvalidDataException($"{packageId} 的 icon.png 不是 PNG。 ");
        int width = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4));
        if (width != 256 || height != 256)
            throw new InvalidDataException($"{packageId} 的 icon.png 必须为 256×256，当前为 {width}×{height}。");
    }

    private static void DisableLegacyDlls(string pluginsDirectory, string legacyDllName)
    {
        foreach (string path in Directory.EnumerateFiles(pluginsDirectory, "*.dll", SearchOption.AllDirectories))
        {
            if (!Path.GetFileName(path).Equals(legacyDllName, StringComparison.OrdinalIgnoreCase))
                continue;

            string disabledPath = path + ".old";
            if (File.Exists(disabledPath))
                File.Delete(disabledPath);
            File.Move(path, disabledPath);
            Console.WriteLine($"禁用旧合并模组：{path} -> {disabledPath}");
        }
    }

    private static void CopyWithRetry(string source, string target)
    {
        const int attempts = 10;
        const int delayMilliseconds = 500;
        string temporary = target + ".tmp";
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                File.Copy(source, temporary, true);
                File.Move(temporary, target, true);
                return;
            }
            catch (IOException) when (attempt < attempts)
            {
                Thread.Sleep(delayMilliseconds);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
    }

    private static void CreatePackage(IReadOnlyDictionary<string, string> files, string zipPath)
    {
        string temporary = zipPath + ".tmp";
        if (File.Exists(temporary))
            File.Delete(temporary);
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, false))
            {
                foreach ((string name, string source) in files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                    entry.LastWriteTime = StableZipTime;
                    using Stream input = File.OpenRead(source);
                    using Stream destination = entry.Open();
                    input.CopyTo(destination);
                }
            }
            File.Move(temporary, zipPath, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
