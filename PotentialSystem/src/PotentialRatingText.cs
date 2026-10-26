using ifapp.Game.UI.Common;
using UnityEngine;
using Text = FastText.FastText;

namespace InFalsusMod.Potential;

/// <summary>使用 Exo-SemiBold 的 34:24 大小字比例，按实际文字边界底部对齐并保留深色描边。</summary>
internal sealed class PotentialRatingText : IDisposable
{
    private NativeLabel? whole, fraction;
    private Vector2 displayedCenter;
    private float displayedDiameter;
    private bool laidOut;

    internal PotentialRatingText(Text template, Transform parent)
    {
        try
        {
            whole = new NativeLabel(template, parent, "Rating.Whole", new Vector2(0.5f, 0.5f),
                Constrained2DAlignment.Center, Vector2.zero, 1f);
            fraction = new NativeLabel(template, parent, "Rating.Fraction", new Vector2(0.5f, 0.5f),
                Constrained2DAlignment.Center, Vector2.zero, 24f / 34f);
            foreach (var label in new[] { whole, fraction })
            {
                PotentialFonts.ApplyDigits(label.Text);
                label.SetOutline(new Color(0.12f, 0.08f, 0.18f), 0.08f);
            }
        }
        catch { Dispose(); throw; }
    }

    /// <summary>保留 34:24 字号比例与共同底边；三位小数允许略宽于徽章，不按两位小数强行压缩。</summary>
    internal void Set(string value, Vector2 center, float diameter)
    {
        int point = value.IndexOf('.');
        bool changed = whole!.Set(point < 0 ? value : value[..(point + 1)], Color.white);
        changed |= fraction!.Set(point < 0 ? "" : value[(point + 1)..], Color.white);
        if (changed) laidOut = false;
        if (laidOut && displayedCenter == center && displayedDiameter == diameter) return;
        var large = whole.Measure(1f);
        var small = fraction.Measure(24f / 34f);
        float width = large.width + small.width;
        float height = Math.Max(large.height, small.height);
        if (width <= 0f || height <= 0f) return;
        // Exo-SemiBold 34 号数字的可见高度约 25；16:9 参考的三位小数略跨出 85 宽徽章。
        float scale = Math.Min(diameter * 1.1f / width, diameter * (25f / 85f) / height);
        large = whole.Measure(scale);
        small = fraction.Measure(scale * 24f / 34f);
        if (large.height <= 0f || (point >= 0 && small.height <= 0f)) return;
        var bottomLeft = center - new Vector2(large.width + small.width, Math.Max(large.height, small.height)) / 2f;
        whole.PlaceBounds(bottomLeft, Vector2.zero, scale);
        fraction.PlaceBounds(bottomLeft + new Vector2(large.width, 0f), Vector2.zero, scale * 24f / 34f);
        laidOut = true;
        displayedCenter = center;
        displayedDiameter = diameter;
    }

    public void Dispose()
    {
        whole?.Dispose();
        fraction?.Dispose();
        whole = fraction = null;
    }
}
