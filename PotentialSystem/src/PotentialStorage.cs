using System.Text.Json;

namespace InFalsusMod.Potential;

/// <summary>在原版存档同目录保存独立 JSON，原子替换并保留上一版备份。</summary>
internal sealed class PotentialStorage
{
    /// <summary>读写共享 camelCase 协议；分数字符串由 BestPlay 属性单独约束。</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    /// <summary>每条成绩必须显式提供的字段，避免缺失值默认为合法的零。</summary>
    private static readonly string[] PlayFields =
    {
        "songId", "difficulty", "chartId", "score", "lamp", "resultClear",
        "constant", "potential", "achievedAt", "source"
    };
    /// <summary>只用于退役归档的旧路径，不读取其中的成绩。</summary>
    private readonly string legacyPath;
    /// <summary>当前账号与游戏存档目录下的独立 PTT 文件。</summary>
    internal string FilePath { get; }

    /// <summary>仅设置存储归属，读取或写入均不修改原版 .sav。</summary>
    /// <param name="nativeSavePath">从当前原版存档容器读取的完整保存路径。</param>
    internal PotentialStorage(string nativeSavePath)
    {
        if (!Path.IsPathFullyQualified(nativeSavePath)
            || !string.Equals(Path.GetExtension(nativeSavePath), ".sav", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("原版存档容器未提供完整 .sav 路径。");
        string directory = Path.GetDirectoryName(nativeSavePath)
            ?? throw new InvalidDataException("原版存档路径缺少父目录。");
        FilePath = Path.Combine(directory, "savestate_ptt_v1.json");
        legacyPath = Path.Combine(directory, "PotentialSystem", "integer-v1.json");
    }

    /// <summary>仅文件缺失时返回 null 以创建时间记录；坏文件不能触发重建覆盖。</summary>
    /// <returns>已验证的成绩状态，或不存在的文件；其他错误通过异常报告。</returns>
    internal PotentialProfile? Load()
    {
        try
        {
            using var stream = File.OpenRead(FilePath);
            if (stream.Length > 8 * 1024 * 1024)
                throw new InvalidDataException("潜力值文件超出 8 MiB 上限。");
            using var json = JsonDocument.Parse(stream);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("bestPlays", out var plays)
                || plays.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("潜力值文件缺少成绩数组。");
            foreach (var play in plays.EnumerateArray())
            {
                if (play.ValueKind != JsonValueKind.Object || PlayFields.Any(field => !play.TryGetProperty(field, out _))
                    || play.GetProperty("score").ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("潜力值成绩缺少字段或分数不是十进制字符串。");
            }
            var document = root.Deserialize<PotentialDocument>(JsonOptions)
                ?? throw new InvalidDataException("潜力值文件内容为空。");
            var profile = new PotentialProfile(document);
            // 已落盘但旧文件归档失败时，下次载入仍继续完成退役，不重新导入原生成绩。
            profile.Dirty |= File.Exists(legacyPath) || File.Exists(legacyPath + ".bak");
            return profile;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    /// <summary>写入同目录临时文件并刷新，再原子替换；成功后退役旧文件，最后清除 Dirty。</summary>
    /// <param name="profile">需要保存的当前存档成绩。</param>
    internal void Save(PotentialProfile profile)
    {
        string directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, profile.ToDocument(), JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(FilePath))
                File.Replace(temporary, FilePath, FilePath + ".bak", ignoreMetadataErrors: false);
            else
                File.Move(temporary, FilePath);
            ArchiveLegacy(legacyPath);
            ArchiveLegacy(legacyPath + ".bak");
            profile.Dirty = false;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try { File.Delete(temporary); }
                catch (IOException ex) { Plugin.Logger.LogWarning($"潜力值临时文件清理失败：{ex.Message}"); }
                catch (UnauthorizedAccessException ex) { Plugin.Logger.LogWarning($"潜力值临时文件清理失败：{ex.Message}"); }
            }
        }
    }

    /// <summary>新文档安全落盘后移走旧格式文件，保留独立备份供人工恢复。</summary>
    private void ArchiveLegacy(string path)
    {
        if (!File.Exists(path))
            return;
        string backup = FilePath + ".legacy." + Guid.NewGuid().ToString("N") + ".bak";
        File.Move(path, backup);
        Plugin.Logger.LogInfo($"旧潜力值文件已退役，备份：{backup}");
    }
}
