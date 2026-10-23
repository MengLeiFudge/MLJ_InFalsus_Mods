using ifapp.Game.Scenes;
using ifapp.Game.UI.Common;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InFalsusMod.Potential;

/// <summary>选中谱面的最佳表现信息；按 Arcaea 横向成绩条的 433×110 基准分两层展示。</summary>
internal sealed class SelectedChartInfo : IDisposable
{
    private Constrained2D? root;
    private Material? material;
    private readonly List<NativeLabel> labels = new();
    private PotentialSnapshot? shown;
    private ChartKey selected;
    private Str.Strings.Localization language;
    private const float Width = 433f, Height = 220f;

    internal SelectedChartInfo(SongSelectScene scene)
    {
        var node = new GameObject("InFalsus.PotentialSystem.SelectedChart");
        try
        {
            node.SetActive(false);
            node.transform.SetParent(scene.scene.transform, false);
            root = node.AddComponent<Constrained2D>();
            var layout = root.constrained2DTransform;
            layout.transform = node.transform;
            layout.type = Constrained2DType.Container;
            layout.anchor = new Vector2(0.5f, 0.5f);
            layout.parentAlignment = Constrained2DAlignment.BottomLeft;
            // 留出一整行成绩条的导航间隔，面板底边为 220，脱离返回按钮区域。
            layout.offset = new Vector2(Width, Height * 1.5f);
            layout.sizeInPixels = layout.sizeInParentNormalized = Vector2.zero;
            layout.renderableScale = Vector2.one;
            layout.color = Color.white;
            layout.zPosition = -0.2f;
            layout.overrideMaterialRenderQueueOffset = 12;
            root.constrained2DTransform = layout;
            node.SetActive(true);
            root._rQA();
            root._RQA();
            root._PQA(0x3f);
            var background = PotentialView.CloneGraphic(scene.leftSongCardContainer.songCardPrefab.jacket,
                node.transform, "SelectedChart.Backing");
            var backing = background.constrained2DTransform;
            backing.color = new Color(0.06f, 0.045f, 0.09f, 0.94f);
            backing.zPosition = 0f;
            background.constrained2DTransform = backing;
            material = new Material(backing.renderableTargetMaterial)
            {
                name = "PotentialSystem.SelectedChart.Backing", hideFlags = HideFlags.HideAndDontSave,
                mainTexture = Texture2D.whiteTexture, mainTextureScale = Vector2.one, mainTextureOffset = Vector2.zero
            };
            background._irA(material);
            BrowserWidgets.Place(background, Vector2.zero, new Vector2(Width, Height));
            for (int i = 0; i < 6; i++)
                labels.Add(new NativeLabel(scene.topBar.text, node.transform, "SelectedChart.Text" + i,
                    new Vector2(0.5f, 0.5f), Constrained2DAlignment.Center, Vector2.zero, 2.5f));
        }
        catch { if (root == null) Object.Destroy(node); Dispose(); throw; }
    }

    internal void Render(PotentialSnapshot? snapshot, ChartKey key)
    {
        bool available = snapshot != null && snapshot.Catalog.TryGet(key, out _);
        root!.gameObject.SetActive(available);
        if (!available) { shown = null; return; }
        var currentLanguage = NativeLabel.CurrentLanguage();
        if (shown == snapshot && selected == key && language == currentLanguage) return;
        snapshot!.Catalog.TryGet(key, out var chart);
        var play = snapshot.Find(key);
        var accent = PotentialPresentation.PotentialColor(play?.Potential);
        Set(0, $"{PotentialPresentation.Difficulty(key.Difficulty)} · 定数 {PotentialCalculator.FormatConstant(chart.Constant)}",
            88f, 2.5f, PotentialPresentation.Color(PotentialPresentation.DifficultyRgb(key.Difficulty)));
        Set(1, play?.Potential is decimal value ? $"PTT {PotentialCalculator.Format(value)}   #{play.Rank}" : "尚未游玩",
            50f, 4f, accent);
        Set(2, play == null ? "打击分数  --" : $"打击分数  {PotentialPresentation.Score(play.Play.Score)}",
            9f, 4f, Color.white);
        Set(3, play == null ? "完成状态  --" : PotentialPresentation.Completion(play.Play.ResultClear),
            -28f, 2.875f, Color.white);
        string date = play?.Play.AchievedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "未知";
        Set(4, play == null ? "最佳纪录时间  --" : $"最佳纪录时间  {date}", -62f, 2.5f, PotentialPresentation.Color(0xc9c2d4));
        Set(5, $"定数来源：{CommunityConstants.DisplayName(snapshot.Catalog.Source)}", -91f, 2.5f,
            PotentialPresentation.Color(0xc9c2d4));
        shown = snapshot;
        selected = key;
        language = currentLanguage;
    }

    private void Set(int index, string text, float y, float font, Color color)
    {
        labels[index].Set(text, color);
        NativePotentialPage.PlaceLabel(labels[index], new Vector2(0f, y), Width - 40f, font, -1f);
    }

    public void Dispose()
    {
        foreach (var label in labels) label.Dispose();
        labels.Clear();
        if (root != null) Object.Destroy(root.gameObject);
        if (material != null) Object.Destroy(material);
        root = null;
        material = null;
    }
}
