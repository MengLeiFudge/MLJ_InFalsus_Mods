using ifapp.Game.UI.Common;
using UnityEngine;
using Text = FastText.FastText;

namespace InFalsusMod.Potential;

/// <summary>Arcaea 式整数与小数组合；两段文字共同适配徽章，保持 34:24 字号比例。</summary>
internal sealed class PotentialRatingText : IDisposable
{
    private NativeLabel? whole, fraction;

    internal PotentialRatingText(Text template, Transform parent)
    {
        try
        {
            whole = new NativeLabel(template, parent, "Rating.Whole", new Vector2(0.5f, 0.5f),
                Constrained2DAlignment.Center, Vector2.zero, 1f);
            fraction = new NativeLabel(template, parent, "Rating.Fraction", new Vector2(0.5f, 0.5f),
                Constrained2DAlignment.Center, Vector2.zero, 24f / 34f);
            PotentialFonts.ApplyDigits(whole.Text);
            PotentialFonts.ApplyDigits(fraction.Text);
        }
        catch { Dispose(); throw; }
    }

    /// <summary>用实际字形宽度合排三位小数；数字变化或切换语言时才重新排版。</summary>
    internal void Set(string value, Vector2 center, float diameter)
    {
        int point = value.IndexOf('.');
        bool changed = whole!.Set(point < 0 ? value : value[..(point + 1)], Color.white);
        changed |= fraction!.Set(point < 0 ? "" : value[(point + 1)..], Color.white);
        if (!changed) return;
        NativePotentialPage.PlaceLabel(whole, center, diameter, 1f);
        NativePotentialPage.PlaceLabel(fraction, center, diameter, 24f / 34f);
        var largeSize = whole.Text.GetComponent<Constrained2D>()._mrA();
        var smallSize = fraction.Text.GetComponent<Constrained2D>()._mrA();
        float width = largeSize.x + smallSize.x;
        float height = Math.Max(largeSize.y, smallSize.y);
        if (width <= 0f || height <= 0f) return;
        // UserCell：85 像素徽章，文字两段宽 33+34、重叠 2；字号 34/24。
        float scale = Math.Min(diameter * (65f / 85f) / width, diameter * (34f / 85f) / height);
        NativePotentialPage.PlaceLabel(whole, center + new Vector2(-smallSize.x * scale / 2f, 0f),
            diameter, scale);
        NativePotentialPage.PlaceLabel(fraction, center + new Vector2(largeSize.x * scale / 2f, -3f * diameter / 85f),
            diameter, scale * 24f / 34f);
    }

    public void Dispose()
    {
        whole?.Dispose();
        fraction?.Dispose();
        whole = fraction = null;
    }
}
