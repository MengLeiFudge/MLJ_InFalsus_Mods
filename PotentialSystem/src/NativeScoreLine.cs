using ifapp.Game.UI.Common;
using UnityEngine;
using Object = UnityEngine.Object;
using Text = FastText.FastText;

namespace InFalsusMod.Potential;

/// <summary>侧卡上方的单层定数与潜力值文字，随原生歌曲卡片一起移动。</summary>
internal sealed class NativeScoreLine : IDisposable
{
    /// <summary>侧卡信息区域的设计像素宽度。</summary>
    private const float Width = 310f;
    /// <summary>零尺寸容器只提供卡片上方的定位点；文字自行计算尺寸并继承原生缩放。</summary>
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
            layout.sizeInPixels = Vector2.zero;
            layout.sizeInParentNormalized = Vector2.zero;
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
            value = new NativeLabel(scoreTemplate, root.transform, "ChartValue.Text", new Vector2(0.5f, 0.5f),
                Constrained2DAlignment.Center, Vector2.zero, fontSize);
        }
        catch { if (row == null) Object.Destroy(root); Dispose(); throw; }
    }

    /// <summary>按已绑定歌曲行更新文本与显示状态；位置和缩放由原生父布局继承。</summary>
    /// <param name="text">定数或排名、定数与潜力值；空串隐藏本行。</param>
    /// <param name="tint">按单曲潜力值档位得到的辨识色。</param>
    internal void Set(string text, Color tint)
    {
        if (row == null || value == null)
            return;
        row.gameObject.SetActive(text.Length > 0);
        if (text.Length == 0)
            return;
        if (value.Set(text, tint))
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
