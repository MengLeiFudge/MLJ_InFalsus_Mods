using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace InFalsusMod.Potential;

/// <summary>维护安小璃定数的内置副本、profile 缓存及每次加载的一次后台更新。</summary>
internal sealed class AnXiaoLiConstants : IDisposable
{
    /// <summary>GitHub 官方 Contents API 返回用户指定仓库文件的原文。</summary>
    private const string SourceUrl = "https://api.github.com/repos/Anling20/InFalsusTools/contents/config/levels/experimental.json?ref=master";
    /// <summary>限制外部输入体积；缓存另预留 1 KiB 存放本地元数据。</summary>
    private const int MaximumBytes = 1024 * 1024;
    /// <summary>缓存归属当前 profile，并以内置资源摘要区分发布批次。</summary>
    private readonly string cachePath, bundledHash;
    /// <summary>卸载取消源独立于单次请求超时。</summary>
    private readonly CancellationTokenSource cancellation = new();
    /// <summary>后台只产生新表，不修改主线程正在使用的数据。</summary>
    private readonly Task<IReadOnlyDictionary<ChartKey, decimal>?> update;
    /// <summary>启动载入后只由主线程整体替换。</summary>
    private IReadOnlyDictionary<ChartKey, decimal> values;
    /// <summary>主线程记录结果领取与卸载，避免重复应用或释放。</summary>
    private bool consumed, disposed;

    /// <summary>只在主线程换表时递增，已有曲库据此重算。</summary>
    internal int Revision { get; private set; }
    /// <summary>主线程无需等待，即可领取本次请求结果。</summary>
    internal bool UpdateReady => !consumed && !disposed && update.IsCompleted;

    /// <summary>先读取本地小表，再启动后台网络请求；缓存失效不影响离线使用。</summary>
    internal AnXiaoLiConstants(string configDirectory)
    {
        cachePath = Path.Combine(configDirectory, "PotentialSystem", "anxiaoli-cache.json");
        using var stream = typeof(AnXiaoLiConstants).Assembly.GetManifestResourceStream("PotentialSystem.AnXiaoLiConstants.json")
            ?? throw new InvalidDataException("缺少安小璃内置定数表。");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        bundledHash = Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
        using var json = JsonDocument.Parse(bytes.ToArray());
        values = Parse(json.RootElement);
        LoadCache();
        update = Task.Run(() => FetchAsync(cancellation.Token));
    }

    /// <summary>缺项时使用当前游戏等级，不改写上游社区定数。</summary>
    internal decimal Resolve(ChartKey key, int rating) => values.TryGetValue(key, out var value) ? value : rating;

    /// <summary>仅由 Unity 主线程在安全的选歌时机采用完整表；失败或数值相同不递增版本。</summary>
    internal bool ApplyUpdate()
    {
        if (!UpdateReady)
            return false;
        consumed = true;
        var next = update.GetAwaiter().GetResult();
        if (next == null || (next.Count == values.Count
            && next.All(pair => values.TryGetValue(pair.Key, out var value) && value == pair.Value)))
            return false;
        values = next;
        Revision++;
        return true;
    }

    /// <summary>只使用与本版内置表匹配的缓存，避免升级后被旧缓存覆盖。</summary>
    private void LoadCache()
    {
        try
        {
            using var stream = File.OpenRead(cachePath);
            if (stream.Length > MaximumBytes + 1024)
                throw new InvalidDataException("安小璃缓存体积超限。");
            using var json = JsonDocument.Parse(stream);
            var root = json.RootElement;
            if (root.GetProperty("bundledHash").GetString() == bundledHash)
                values = Parse(root.GetProperty("constants"));
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch (Exception ex) { Plugin.Logger.LogWarning($"安小璃缓存读取失败，使用内置定数：{ex.Message}"); }
    }

    /// <summary>限时下载和校验；网络失败不接触当前表，缓存写入失败仍可使用本次下载。</summary>
    private async Task<IReadOnlyDictionary<ChartKey, decimal>?> FetchAsync(CancellationToken token)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("InFalsus-PotentialSystem/1.0.1");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.raw+json");
            using var response = await client.GetAsync(SourceUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaximumBytes)
                throw new InvalidDataException("安小璃远端定数超出 1 MiB 上限。");
            using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            int length;
            while ((length = await input.ReadAsync(buffer.AsMemory(), timeout.Token).ConfigureAwait(false)) != 0)
            {
                if (bytes.Length + length > MaximumBytes)
                    throw new InvalidDataException("安小璃远端定数超出 1 MiB 上限。");
                bytes.Write(buffer, 0, length);
            }
            timeout.Token.ThrowIfCancellationRequested();
            using var json = JsonDocument.Parse(bytes.ToArray());
            var next = Parse(json.RootElement);
            token.ThrowIfCancellationRequested();
            try { SaveCache(json.RootElement); }
            catch (Exception ex) { Plugin.Logger.LogWarning($"安小璃定数已下载，但缓存保存失败：{ex.Message}"); }
            return next;
        }
        catch (OperationCanceledException)
        {
            if (!token.IsCancellationRequested)
                Plugin.Logger.LogWarning("安小璃定数更新超过 15 秒，继续使用本地定数。");
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
                Plugin.Logger.LogWarning($"安小璃定数更新失败，继续使用本地定数：{ex.Message}");
        }
        return null;
    }

    /// <summary>按 SongId 和四个难度名解析，不依赖歌曲顺序、语言或显示名称。</summary>
    private static IReadOnlyDictionary<ChartKey, decimal> Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("安小璃定数表不是歌曲对象。");
        var result = new Dictionary<ChartKey, decimal>();
        var songs = new HashSet<ushort>();
        foreach (var song in root.EnumerateObject())
        {
            if (!ushort.TryParse(song.Name, NumberStyles.None, CultureInfo.InvariantCulture, out ushort id)
                || id == 0 || !songs.Add(id) || song.Value.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("安小璃定数表含无效或重复歌曲身份。");
            int count = 0;
            foreach (var chart in song.Value.EnumerateObject())
            {
                byte difficulty = chart.Name switch
                {
                    "minimal" => 1, "evolved" => 2, "ultimate" => 4, "forbidden" => 8, _ => 0
                };
                if (difficulty == 0 || chart.Value.ValueKind != JsonValueKind.Number
                    || !chart.Value.TryGetDecimal(out decimal value) || value < 0m || value > 100m
                    || decimal.Truncate(value * 10m) != value * 10m
                    || !result.TryAdd(new ChartKey(id, difficulty), value))
                    throw new InvalidDataException("安小璃定数表含无效难度、重复谱面或非一位小数定数。");
                count++;
            }
            if (count == 0)
                throw new InvalidDataException("安小璃定数表含空歌曲。");
        }
        if (result.Count == 0)
            throw new InvalidDataException("安小璃定数表为空。");
        return result;
    }

    /// <summary>同目录临时文件刷新后原子替换，保留上一版缓存。</summary>
    private void SaveCache(JsonElement constants)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        string temporary = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new { bundledHash, constants });
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(cachePath))
                File.Replace(temporary, cachePath, cachePath + ".bak", ignoreMetadataErrors: false);
            else
                File.Move(temporary, cachePath);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try { File.Delete(temporary); }
                catch (IOException ex) { Plugin.Logger.LogWarning($"安小璃缓存临时文件清理失败：{ex.Message}"); }
                catch (UnauthorizedAccessException ex) { Plugin.Logger.LogWarning($"安小璃缓存临时文件清理失败：{ex.Message}"); }
            }
        }
    }

    /// <summary>卸载时取消请求，不阻塞 Unity 主线程；后台结束后释放取消源。</summary>
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        cancellation.Cancel();
        _ = update.ContinueWith(_ => cancellation.Dispose(), TaskScheduler.Default);
    }
}
