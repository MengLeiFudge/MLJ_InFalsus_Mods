extern alias GameData;

using ifapp.Game.Scenes;
using ifapp.Game.UI.Common;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SaveOwner = GameData::_K._NH;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InFalsusMod.Potential;

/// <summary>选歌左下方的透明历史趋势图；只读原生逐局记录，独立于返回按钮。</summary>
internal sealed class SelectedChartInfo : IDisposable
{
    private Constrained2D? root;
    private Material? material;
    private Texture2D? texture;
    private PotentialSnapshot? shown;
    private ChartKey selected;
    private int historyCount = -1;
    private Str.Strings.Localization language;
    private const float Width = 900f, Height = 450f;

    internal SelectedChartInfo(SongSelectScene scene)
    {
        try
        {
            root = PotentialView.CloneGraphic(scene.leftSongCardContainer.songCardPrefab.jacket,
                scene.scene.transform, "SelectedChart.History");
            var layout = root.constrained2DTransform;
            layout.parentAlignment = Constrained2DAlignment.BottomLeft;
            // 底边沿用原成绩区的 220，避免压住返回按钮；左侧保留 100 的导航边距。
            layout.offset = new Vector2(100f + Width / 2f, 220f + Height / 2f);
            layout.sizeInPixels = new Vector2(Width, Height);
            layout.zPosition = -0.2f;
            layout.overrideMaterialRenderQueueOffset = 12;
            root.constrained2DTransform = layout;
            material = new Material(layout.renderableTargetMaterial)
            {
                name = "PotentialSystem.SelectedChart.History", hideFlags = HideFlags.HideAndDontSave,
                mainTexture = Texture2D.whiteTexture, mainTextureScale = Vector2.one, mainTextureOffset = Vector2.zero
            };
            root._irA(material);
            root.gameObject.SetActive(false);
        }
        catch { Dispose(); throw; }
    }

    /// <summary>谱面、成绩、定数、语言或历史条数变化后重绘；其他帧复用纹理。</summary>
    internal void Render(PotentialSnapshot? snapshot, ChartKey key)
    {
        if (root == null) { shown = null; return; }
        bool available = snapshot != null && snapshot.Catalog.TryGet(key, out _);
        root.gameObject.SetActive(available);
        if (!available) { shown = null; return; }
        var results = SaveOwner._CEb?._DEb?._iEb?.GameResults;
        int count = results?.history?.Length ?? -1;
        var currentLanguage = NativeLabel.CurrentLanguage();
        if (shown == snapshot && selected == key && language == currentLanguage && historyCount == count) return;
        IReadOnlyList<ChartPlay> history;
        string error = "";
        try
        {
            if (results == null) throw new InvalidDataException("游戏逐局记录不可用。");
            history = PlayHistory.Read(results, key);
        }
        catch (Exception ex)
        {
            history = Array.Empty<ChartPlay>();
            error = "历史读取失败，请查看日志。";
            Plugin.Logger.LogError($"选歌谱面 {key} 历史读取失败：{ex}");
        }
        var image = PlayHistoryImage.Selection(snapshot!, key, history, error);
        var next = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, false)
        {
            name = "PotentialSystem.SelectedChart.History", hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
        };
        try
        {
            next.LoadRawTextureData(new Il2CppStructArray<byte>(image.Rgba));
            next.Apply(false, true);
            material = BrowserWidgets.SwapMaterial(root, material!, next, Vector2.one, Vector2.zero);
        }
        catch { Object.Destroy(next); throw; }
        if (texture != null) Object.Destroy(texture);
        texture = next;
        BrowserWidgets.Place(root, root.constrained2DTransform.offset, new Vector2(Width, Height));
        shown = snapshot;
        selected = key;
        language = currentLanguage;
        historyCount = count;
    }

    public void Dispose()
    {
        if (root != null) Object.Destroy(root.gameObject);
        if (material != null) Object.Destroy(material);
        if (texture != null) Object.Destroy(texture);
        root = null;
        material = null;
        texture = null;
    }
}
