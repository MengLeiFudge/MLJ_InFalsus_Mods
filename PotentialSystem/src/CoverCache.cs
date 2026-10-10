using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;

namespace InFalsusMod.Potential;

/// <summary>浏览会话独立持有的封面加载引用；不改动游戏 AssetReference 自身的加载状态。</summary>
internal sealed class CoverCache : IDisposable
{
    /// <summary>单项封面句柄、展示纹理和可导出的像素。</summary>
    private sealed class Entry
    {
        internal AsyncOperationHandle<Material>? Handle;
        internal Texture2D? Texture;
        internal PixelImage? Pixels;
        internal string? Error;
        internal bool Finished;
    }

    /// <summary>原生 RuntimeKey 到本会话素材的映射，同一歌曲不同难度复用封面。</summary>
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);

    /// <summary>申请一项封面；缺失引用也有明确的完成失败状态。</summary>
    private Entry Request(ChartDefinition? chart)
    {
        string key = chart?.Jacket?.RuntimeKey?.ToString() ?? "missing:" + chart?.Id;
        if (entries.TryGetValue(key, out var existing))
            return existing;
        var entry = new Entry();
        entries.Add(key, entry);
        try
        {
            if (chart?.Jacket?.RuntimeKey == null)
                throw new InvalidDataException("谱面没有可读取的封面引用。");
            // 对 RuntimeKey 单独加载，避免调用已经被游戏使用的 AssetReference.LoadAssetAsync。
            entry.Handle = Addressables.LoadAssetAsync<Material>(chart.Jacket.RuntimeKey);
        }
        catch (Exception ex)
        {
            entry.Error = ex.Message;
            entry.Finished = true;
            Plugin.Logger.LogWarning($"封面加载失败：{chart?.Id}，{ex}");
        }
        return entry;
    }

    /// <summary>异步结果就绪后在主线程读取纹理，不执行阻塞的 WaitForCompletion。</summary>
    private static void Poll(Entry entry)
    {
        if (entry.Finished || entry.Handle == null || !entry.Handle.IsDone)
            return;
        entry.Finished = true;
        try
        {
            if (entry.Handle.Status != AsyncOperationStatus.Succeeded || entry.Handle.Result?.mainTexture == null)
                throw new InvalidOperationException("游戏封面加载失败：" + entry.Handle.OperationException?.Message);
            (entry.Texture, entry.Pixels) = Capture(entry.Handle.Result.mainTexture);
        }
        catch (Exception ex)
        {
            entry.Error = ex.Message;
            Plugin.Logger.LogWarning($"封面读取失败：{ex}");
        }
    }

    /// <summary>把可能不可读的游戏纹理复制到自有 RGBA32 纹理，恢复原渲染目标。</summary>
    private static (Texture2D Texture, PixelImage Pixels) Capture(Texture source)
    {
        const int size = 256;
        var previous = RenderTexture.active;
        var target = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
        Texture2D? texture = null;
        try
        {
            Graphics.Blit(source, target);
            RenderTexture.active = target;
            texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "PotentialSystem.Cover", hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            texture.ReadPixels(new Rect(0f, 0f, size, size), 0, 0, false);
            texture.Apply(false, false);
            Il2CppStructArray<byte> raw = texture.GetRawTextureData();
            var pixels = raw.ToArray();
            if (pixels.Length != size * size * 4)
                throw new InvalidDataException("封面像素长度无效。");
            return (texture, new PixelImage(size, size, pixels));
        }
        catch
        {
            if (texture != null)
                Object.Destroy(texture);
            throw;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
        }
    }

    /// <summary>浏览页按需请求封面；未就绪时返回空纹理和可显示的加载状态。</summary>
    internal Texture2D? GetTexture(ChartDefinition? chart, out string? error)
    {
        var entry = Request(chart);
        Poll(entry);
        error = entry.Error;
        return entry.Texture;
    }

    /// <summary>为导出申请并收集封面；全部完成或明确失败后才能执行剪贴板写入。</summary>
    internal bool Prepare(IReadOnlyList<RankedPlay> plays, out Dictionary<ChartKey, PixelImage> images, out string? error)
    {
        images = new Dictionary<ChartKey, PixelImage>();
        error = null;
        bool ready = true;
        foreach (var play in plays)
        {
            var entry = Request(play.Chart);
            Poll(entry);
            if (entry.Error != null)
                error ??= $"{play.Chart?.Title}：{entry.Error}";
            if (!entry.Finished)
                ready = false;
            if (entry.Pixels != null)
                images.Add(play.Play.Key, entry.Pixels);
        }
        return ready;
    }

    /// <summary>释放自有纹理及所有 Addressables 引用；不销毁原生封面材质。</summary>
    public void Dispose()
    {
        foreach (var entry in entries.Values)
        {
            try
            {
                if (entry.Texture != null)
                    Object.Destroy(entry.Texture);
            }
            finally
            {
                try
                {
                    if (entry.Handle != null && entry.Handle.IsValid())
                        Addressables.Release(entry.Handle);
                }
                catch (Exception ex) { Plugin.Logger.LogError($"释放浏览页封面引用失败：{ex}"); }
            }
            entry.Handle = null;
            entry.Texture = null;
        }
        entries.Clear();
    }
}
