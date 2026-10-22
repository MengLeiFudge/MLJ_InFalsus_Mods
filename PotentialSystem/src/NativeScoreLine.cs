using ifapp.Game.UI.Common;
using UnityEngine;
using Object = UnityEngine.Object;
using Text = FastText.FastText;

namespace InFalsusMod.Potential;

/// <summary>侧卡上方的单层定数与潜力值文字，随原生歌曲卡片一起移动。</summary>
internal sealed class NativeScoreLine : IDisposable
{
    /// <summary>独立布局及文字副本；宽度沿用侧卡信息区域的设计像素。</summary>
    private const float Width = 310f;
    private Constrained2D? row;
    private NativeLabel? value;
    private readonly float fontSize;

    /// <summary>池化卡片失效后可重新建立本行。</summary>
    internal bool Alive => row != null;

    /// <summary>在侧卡上方留出文字行，不覆盖曲绘和原生分数。</summary>
    /// <param name="parent">原生侧卡容器。</param>
    /// <param name="scoreTemplate">侧卡的原生文字模板。</param>
    internal NativeScoreLine(Transform parent, Text scoreTemplate)
    {
        fontSize = scoreTemplate.FontSize;
        var root = new GameObject("InFalsus.PotentialSystem.ChartValue");
        try
        {
            root.SetActive(false);
            root.transform.SetParent(parent, false);
            row = root.AddComponent<Constrained2D>();
            var layout = row.constrained2DTransform;
            layout.transform = root.transform;
            layout.type = Constrained2DType.Container;
            layout.anchor = new Vector2(0.5f, 0.5f);
            layout.parentAlignment = Constrained2DAlignment.Top;
            layout.offset = new Vector2(0f, 40f);
            layout.renderableScale = Vector2.one;
            layout.color = Color.white;
            layout.zPosition = -0.2f;
            layout.overrideMaterialRenderQueueOffset = 12;
            row.constrained2DTransform = layout;
            root.SetActive(true);
            row._rQA();
            row._RQA();
            row._PQA(0x3f);
            BrowserWidgets.Place(row, layout.offset, new Vector2(Width, 54f));
            value = new NativeLabel(scoreTemplate, root.transform, "ChartValue.Text", new Vector2(0.5f, 0.5f),
                Constrained2DAlignment.Center, Vector2.zero, fontSize);
        }
        catch { if (row == null) Object.Destroy(root); Dispose(); throw; }
    }

    /// <summary>按已绑定歌曲行更新文本，并适配当前卡片缩放后的父布局。</summary>
    /// <param name="text">定数或排名、定数与潜力值；空串隐藏本行。</param>
    internal void Set(string text)
    {
        if (row == null || value == null)
            return;
        row.gameObject.SetActive(text.Length > 0);
        if (text.Length == 0)
            return;
        BrowserWidgets.Place(row, new Vector2(0f, 40f), new Vector2(Width, 54f));
        if (value.Set(text, Color.white))
            NativePotentialPage.PlaceLabel(value, Vector2.zero, Width - 30f, fontSize);
    }

    /// <summary>只释放自有行和文字副本。</summary>
    public void Dispose()
    {
        value?.Dispose();
        value = null;
        if (row != null) Object.Destroy(row.gameObject);
        row = null;
    }
}
