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
                label.Text.SetStrokeEnable(true, Text.UpdateType.DontUpdateMesh);
                label.Text.SetStrokeColor(new Color(0.12f, 0.08f, 0.18f), Text.UpdateType.DontUpdateMesh);
                label.Text.SetStrokeSize(0.08f, Text.UpdateType.DontUpdateMesh);
            }
        }
        catch { Dispose(); throw; }
    }

    /// <summary>共同适配徽章后对齐两段文字的下边界，不用不同字号下的固定 Y 偏移。</summary>
    internal void Set(string value, Vector2 center, float diameter)
    {
        int point = value.IndexOf('.');
        bool changed = whole!.Set(point < 0 ? value : value[..(point + 1)], Color.white);
        changed |= fraction!.Set(point < 0 ? "" : value[(point + 1)..], Color.white);
        if (!changed && displayedCenter == center && displayedDiameter == diameter) return;
        NativePotentialPage.PlaceLabel(whole, center, diameter, 1f);
        NativePotentialPage.PlaceLabel(fraction, center, diameter, 24f / 34f);
        var largeSize = whole.Text.GetComponent<Constrained2D>()._mrA();
        var smallSize = fraction.Text.GetComponent<Constrained2D>()._mrA();
        float width = largeSize.x + smallSize.x;
        float height = Math.Max(largeSize.y, smallSize.y);
        if (width <= 0f || height <= 0f) return;
        // UserCell 的两段数字总宽 65、高 34，徽章为 85；三位小数共同缩放，保持字号比例。
        float scale = Math.Min(diameter * (65f / 85f) / width, diameter * (34f / 85f) / height);
        float bottom = center.y - height * scale / 2f;
        NativePotentialPage.PlaceLabel(whole,
            new Vector2(center.x - smallSize.x * scale / 2f, bottom + largeSize.y * scale / 2f), diameter, scale);
        NativePotentialPage.PlaceLabel(fraction,
            new Vector2(center.x + largeSize.x * scale / 2f, bottom + smallSize.y * scale / 2f), diameter, scale * 24f / 34f);
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
