using System.Security.Cryptography;
using System.Text.Json;
using ifapp.Game.Common;
using ifapp.Game.Data;
using UnityEngine;

namespace InFalsusMod.Potential;

/// <summary>一项 v6 结果的归属；等级或难度改变后不沿用旧值。</summary>
internal sealed record MengLeiValue(int Level, byte Difficulty, decimal Constant);

/// <summary>内置结果先行，曲库就绪后以托管快照后台计算缺项；主线程统一采用结果。</summary>
internal sealed class MengLeiConstants : IDisposable
{
    private sealed record ChartFile(string Id, byte Difficulty, int Level, string? Path, long Length);
    private readonly string cachePath, bundledHash;
    private readonly byte[] modelBytes;
    private readonly CancellationTokenSource cancellation = new();
    private Dictionary<string, MengLeiValue> values;
    private Task<Dictionary<string, MengLeiValue>?>? update;
    private bool consumed, disposed;
    internal int Revision { get; private set; }
    internal bool UpdateReady => !consumed && !disposed && update?.IsCompleted == true;

    internal MengLeiConstants(string configDirectory)
    {
        cachePath = Path.Combine(configDirectory, "PotentialSystem", "menglei-v6-cache.json");
        byte[] table = Resource("MengLeiConstants");
        modelBytes = Resource("MengLeiModel");
        bundledHash = Convert.ToHexString(SHA256.HashData(table.Concat(modelBytes).ToArray()));
        using var json = JsonDocument.Parse(table);
        values = Parse(json.RootElement);
        try
        {
            using var stream = File.OpenRead(cachePath);
            if (stream.Length > 2 * 1024 * 1024) throw new InvalidDataException("萌泪酱缓存体积超限。");
            using var cached = JsonDocument.Parse(stream);
            if (cached.RootElement.GetProperty("bundledHash").GetString() == bundledHash)
            {
                var additions = Parse(cached.RootElement.GetProperty("constants"));
                foreach (var pair in additions) values[pair.Key] = pair.Value;
            }
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch (Exception ex) { Plugin.Logger.LogWarning($"萌泪酱缓存读取失败，使用内置定数：{ex.Message}"); }
    }

    internal decimal Resolve(string id, byte difficulty, int level) => values.TryGetValue(id, out var value)
        && value.Level == level && value.Difficulty == difficulty ? value.Constant : level;

    /// <summary>只能在主线程调用；复制原生数据后，后台不持有任何 Unity/IL2CPP 对象。</summary>
    internal void Start(DataAccess access)
    {
        if (disposed || update != null || access?.SongData?.allSongInfo == null) return;
        try
        {
            var missing = new List<ChartFile>();
            foreach (var song in access.SongData.allSongInfo)
                if (song != null && song.ChartInfos != null)
                    foreach (var chart in song.ChartInfos)
                    {
                        if (chart == null || !chart.Available) continue;
                        string id = chart.Id;
                        byte difficulty = (byte)chart.Difficulty;
                        int level = chart.Rating;
                        if (string.IsNullOrWhiteSpace(id) || difficulty is not (1 or 2 or 4 or 8) || level is < 0 or > 99)
                            throw new InvalidDataException("曲库包含不受支持的谱面身份、难度或等级。");
                        if (!values.TryGetValue(id, out var value) || value.Level != level || value.Difficulty != difficulty)
                            missing.Add(new ChartFile(id, difficulty, level, null, 0));
                    }
            if (missing.Count == 0)
            {
                update = Task.FromResult<Dictionary<string, MengLeiValue>?>(null);
                return;
            }
            var files = new Dictionary<string, (string Path, long Length)>(StringComparer.Ordinal);
            var needed = missing.Select(c => c.Id + ".spc").ToHashSet(StringComparer.Ordinal);
            string gameDirectory = Directory.GetParent(Application.dataPath)!.FullName;
            if (access.StreamingAssetsMapping?.Entries != null)
                foreach (var entry in access.StreamingAssetsMapping.Entries)
                    Add(entry, Path.Combine(Application.streamingAssetsPath, "sam"));
            if (access.DlcRuntimeStartupConfiguration?.Entries != null)
                foreach (var pack in access.DlcRuntimeStartupConfiguration.Entries)
                    if (pack?.StreamingAssets != null)
                    {
                        if (!SafeName(pack.DlcId)) throw new InvalidDataException("DLC 文件夹身份无效。");
                        foreach (var entry in pack.StreamingAssets)
                            Add(entry, Path.Combine(gameDirectory, "dlc", pack.DlcId, "sam"));
                    }
            ChartFile[] charts = missing.Select(c => files.TryGetValue(c.Id + ".spc", out var file)
                ? c with { Path = file.Path, Length = file.Length } : c).ToArray();
            var baseline = new Dictionary<string, MengLeiValue>(values, StringComparer.Ordinal);
            update = Task.Run(() => ComputeAsync(charts, baseline, cancellation.Token));
            Plugin.Logger.LogInfo($"萌泪酱 v6 开始后台计算 {charts.Length} 个未收录或等级变化的谱面。");

            void Add(StreamingAssetsMapping.Entry entry, string directory)
            {
                string lookup = entry.FullLookupPath;
                if (!needed.Contains(lookup)) return;
                if (!SafeName(entry.Guid)) throw new InvalidDataException("谱面映射文件名无效。");
                if (!files.TryAdd(lookup, (Path.Combine(directory, entry.Guid), entry.FileLength)))
                    throw new InvalidDataException($"谱面映射重复：{lookup}。");
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"萌泪酱曲库快照读取失败，本次保留本地定数：{ex}");
            update = Task.FromResult<Dictionary<string, MengLeiValue>?>(null);
        }
    }

    internal bool ApplyUpdate()
    {
        if (!UpdateReady) return false;
        consumed = true;
        var next = update!.GetAwaiter().GetResult();
        if (next == null || (next.Count == values.Count && next.All(p => values.TryGetValue(p.Key, out var old) && old == p.Value))) return false;
        values = next;
        Revision++;
        return true;
    }

    private async Task<Dictionary<string, MengLeiValue>?> ComputeAsync(ChartFile[] charts,
        Dictionary<string, MengLeiValue> next, CancellationToken token)
    {
        int completed = 0;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            var model = new MengLeiModel(modelBytes);
            foreach (var chart in charts)
            {
                if (timeout.IsCancellationRequested) break;
                try
                {
                    if (chart.Path == null) throw new FileNotFoundException("缺少谱面文件映射。");
                    using var input = new FileStream(chart.Path, FileMode.Open, FileAccess.Read, FileShare.Read,
                        8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    if (input.Length != chart.Length || input.Length is < 20 or > 16 * 1024 * 1024)
                        throw new InvalidDataException("谱面文件长度与映射不符或超限。");
                    var bytes = new byte[checked((int)input.Length)];
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int length = await input.ReadAsync(bytes.AsMemory(offset), timeout.Token).ConfigureAwait(false);
                        if (length == 0) throw new EndOfStreamException("谱面文件不完整。");
                        offset += length;
                    }
                    var notes = ChartDecoder.Decode(bytes, chart.Id + ".spc", timeout.Token);
                    var features = ChartFeatures.Extract(notes, chart.Difficulty, timeout.Token);
                    decimal constant = model.Predict(chart.Difficulty, chart.Level, features);
                    timeout.Token.ThrowIfCancellationRequested();
                    next[chart.Id] = new MengLeiValue(chart.Level, chart.Difficulty, constant);
                    completed++;
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Plugin.Logger.LogWarning($"萌泪酱谱面 {chart.Id} 计算失败，保留官方等级：{ex.Message}"); }
            }
            if (token.IsCancellationRequested) return null;
            if (timeout.IsCancellationRequested) Plugin.Logger.LogWarning("萌泪酱后台计算达到 2 分钟上限，未完成谱面下次启动重试。");
            if (completed > 0)
            {
                try { SaveCache(next); }
                catch (Exception ex) { Plugin.Logger.LogWarning($"萌泪酱计算完成，但缓存保存失败：{ex.Message}"); }
            }
            Plugin.Logger.LogInfo($"萌泪酱 v6 后台计算结束：成功 {completed}/{charts.Length}。");
            return completed > 0 ? next : null;
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested) Plugin.Logger.LogWarning($"萌泪酱后台计算失败，保留本地定数：{ex}");
            return null;
        }
    }

    private static bool SafeName(string value) => !string.IsNullOrWhiteSpace(value) && value is not ("." or "..")
        && value.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_');

    private static byte[] Resource(string name)
    {
        using var input = typeof(MengLeiConstants).Assembly.GetManifestResourceStream($"PotentialSystem.{name}.json")
            ?? throw new InvalidDataException($"缺少萌泪酱资源：{name}。");
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    private static Dictionary<string, MengLeiValue> Parse(JsonElement root)
    {
        var result = new Dictionary<string, MengLeiValue>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            var value = property.Value.Deserialize<MengLeiValue>() ?? throw new InvalidDataException("定数缓存含空条目。");
            if (string.IsNullOrWhiteSpace(property.Name) || value.Difficulty is not (1 or 2 or 4 or 8)
                || value.Level is < 0 or > 99 || value.Constant != MengLeiModel.Quantize(value.Level, (double)value.Constant)
                || !result.TryAdd(property.Name, value)) throw new InvalidDataException("定数缓存身份、等级或档位无效。");
            if (result.Count > 10000) throw new InvalidDataException("定数缓存条目过多。");
        }
        if (result.Count == 0) throw new InvalidDataException("萌泪酱定数表为空。");
        return result;
    }

    private void SaveCache(Dictionary<string, MengLeiValue> constants)
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
            if (File.Exists(cachePath)) File.Replace(temporary, cachePath, cachePath + ".bak", ignoreMetadataErrors: false);
            else File.Move(temporary, cachePath);
        }
        finally
        {
            if (File.Exists(temporary))
                try { File.Delete(temporary); }
                catch (Exception ex) { Plugin.Logger.LogWarning($"萌泪酱缓存临时文件清理失败：{ex.Message}"); }
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        cancellation.Cancel();
        if (update == null) cancellation.Dispose();
        else _ = update.ContinueWith(_ => cancellation.Dispose(), TaskScheduler.Default);
    }
}
