using System.Globalization;
using System.Text.Json;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using ifapp.Game.UI.Common;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InFalsusMod.Potential;

/// <summary>使用 APK 的 Exo 字形图集显示 PTT；字形含真实描边，不依赖选歌文字模板的材质。</summary>
internal sealed class PotentialRatingText : IDisposable
{
    /// <summary>字形坐标为图集左上原点像素；Advance 为同分辨率的字体前进宽度。</summary>
    private sealed record Glyph(int X, int Y, int Width, int Height, int Left, int Top, float Advance);
    private sealed record Face(Dictionary<string, Glyph> Glyphs, Dictionary<string, float> Kerning);
    private sealed record Atlas(int Scale, int Stroke, Dictionary<string, Face> Faces);
    private static readonly Lazy<(Atlas Metrics, PixelImage Image)> atlas = new(LoadAtlas);
    private Constrained2D? graphic;
    private Material? material;
    private Texture2D? texture;
    private string? displayed;
    private Vector2 imageSize, imageCenter;

    /// <summary>复制本页图形模板，仅拥有自己的图形、材质及合成纹理。</summary>
    internal PotentialRatingText(Constrained2D template, Transform parent)
    {
        try
        {
            graphic = PotentialView.CloneGraphic(template, parent, "Rating.Text");
            var layout = graphic.constrained2DTransform;
            layout.zPosition = -0.25f;
            graphic.constrained2DTransform = layout;
            material = new Material(layout.renderableTargetMaterial)
            {
                name = "PotentialSystem.Rating.Text", hideFlags = HideFlags.HideAndDontSave,
                mainTextureScale = Vector2.one, mainTextureOffset = Vector2.zero
            };
            graphic.gameObject.SetActive(false);
        }
        catch { Dispose(); throw; }
    }

    /// <summary>保留 34/24 字号、独立锚点和 3 像素描边；只在数值变化时合成字形。</summary>
    /// <param name="value">内部格式化的 PTT，或 --.---。</param>
    /// <param name="center">徽章中心的游戏设计坐标。</param>
    /// <param name="diameter">徽章在游戏设计坐标中的直径；APK 原始直径为 85。</param>
    internal void Set(string value, Vector2 center, float diameter)
    {
        if (displayed != value)
        {
            var (image, offset) = Compose(value);
            var next = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, false)
            {
                name = "PotentialSystem.Rating.Text", hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            try
            {
                next.LoadRawTextureData(new Il2CppStructArray<byte>(image.Rgba));
                next.Apply(false, true);
                material = BrowserWidgets.SwapMaterial(graphic!, material!, next, Vector2.one, Vector2.zero);
            }
            catch { Object.Destroy(next); throw; }
            if (texture != null) Object.Destroy(texture);
            texture = next;
            imageSize = new Vector2(image.Width, image.Height) / atlas.Value.Metrics.Scale;
            imageCenter = offset;
            displayed = value;
            graphic!.gameObject.SetActive(true);
        }
        float scale = diameter / 85f;
        BrowserWidgets.Place(graphic!, center + imageCenter * scale, imageSize * scale);
    }

    /// <summary>读取构建时从 Exo-SemiBold.ttf 生成的透明字形和字体度量。</summary>
    private static (Atlas, PixelImage) LoadAtlas()
    {
        var assembly = typeof(PotentialRatingText).Assembly;
        using var metadata = assembly.GetManifestResourceStream("PotentialSystem.Fonts.RatingGlyphs.json")
            ?? throw new InvalidDataException("缺少 PTT 字形度量。");
        using var image = assembly.GetManifestResourceStream("PotentialSystem.Fonts.RatingGlyphs.png")
            ?? throw new InvalidDataException("缺少 PTT 字形图集。");
        var metrics = JsonSerializer.Deserialize<Atlas>(metadata)
            ?? throw new InvalidDataException("PTT 字形度量为空。");
        var pixels = PngRgba.Decode(image);
        return (metrics, new PixelImage(pixels.Width, pixels.Height, pixels.BottomUpPixels));
    }

    /// <summary>按 APK 的两段文字锚点、行框和 Exo 下伸部定位，返回相对徽章中心的透明图像。</summary>
    private static (PixelImage Image, Vector2 Center) Compose(string value)
    {
        var (metrics, image) = atlas.Value;
        int point = value.IndexOf('.');
        string large = point < 0 ? value : value[..(point + 1)];
        string small = point < 0 ? "" : value[(point + 1)..];
        var largeFace = metrics.Faces["34"];
        var smallFace = metrics.Faces["24"];
        float largeWidth = Width(largeFace, large) / metrics.Scale + 2f * metrics.Stroke;
        float smallWidth = Width(smallFace, small) / metrics.Scale + 2f * metrics.Stroke;
        float containerX = (largeWidth - smallWidth) / 2f;
        var placements = new List<(Glyph Glyph, int X, int Y)>();
        // UserCell：大字(2,-3.5)、小字(0,0)，容器y=3.5，徽章y=22.5。
        // Exo hhea.descender=-328、unitsPerEm=1000；两段各自的行框下缘换算为基线。
        Add(largeFace, large, containerX + 2f - largeWidth + metrics.Stroke, -22.5f + metrics.Stroke + 34f * 0.328f);
        Add(smallFace, small, containerX + metrics.Stroke, -19f + metrics.Stroke + 24f * 0.328f);
        int left = placements.Min(p => p.X), top = placements.Min(p => p.Y);
        int right = placements.Max(p => p.X + p.Glyph.Width), bottom = placements.Max(p => p.Y + p.Glyph.Height);
        var result = new PixelImage(right - left, bottom - top, new byte[(right - left) * (bottom - top) * 4]);
        bool high = decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var ptt) && ptt >= 13m;
        var stroke = high ? new[] { 104, 9, 52 } : new[] { 81, 72, 92 };
        foreach (var (glyph, x, y) in placements)
            Blit(image, result, glyph, x - left, y - top, stroke);
        return (result, new Vector2((left + right) / 2f, -(top + bottom) / 2f) / metrics.Scale);

        void Add(Face face, string text, float x, float baseline)
        {
            float cursor = x * metrics.Scale;
            for (int i = 0; i < text.Length; i++)
            {
                if (i > 0 && face.Kerning.TryGetValue(text.Substring(i - 1, 2), out float kern)) cursor += kern;
                var glyph = face.Glyphs[text[i].ToString()];
                placements.Add((glyph, (int)MathF.Round(cursor) + glyph.Left,
                    (int)MathF.Round(-baseline * metrics.Scale) + glyph.Top));
                cursor += glyph.Advance;
            }
        }
    }

    /// <summary>前进宽度含字偶距，不把两端描边当成字符间距。</summary>
    private static float Width(Face face, string text)
    {
        float width = 0f;
        for (int i = 0; i < text.Length; i++)
        {
            width += face.Glyphs[text[i].ToString()].Advance;
            if (i > 0 && face.Kerning.TryGetValue(text.Substring(i - 1, 2), out float kern)) width += kern;
        }
        return width;
    }

    /// <summary>按覆盖率为描边着色，再合成到自下而上的 RGBA 图像；交叠字形保留抗锯齿。</summary>
    private static void Blit(PixelImage source, PixelImage target, Glyph glyph, int x, int y, int[] stroke)
    {
        for (int row = 0; row < glyph.Height; row++)
            for (int column = 0; column < glyph.Width; column++)
            {
                int from = ((source.Height - 1 - glyph.Y - row) * source.Width + glyph.X + column) * 4;
                int to = ((target.Height - 1 - y - row) * target.Width + x + column) * 4;
                int alpha = source.Rgba[from + 3];
                if (alpha == 0) continue;
                int behind = target.Rgba[to + 3] * (255 - alpha) / 255;
                int combined = alpha + behind;
                for (int channel = 0; channel < 3; channel++)
                {
                    int color = stroke[channel] + (255 - stroke[channel]) * source.Rgba[from] / 255;
                    target.Rgba[to + channel] = (byte)((color * alpha + target.Rgba[to + channel] * behind) / combined);
                }
                target.Rgba[to + 3] = (byte)combined;
            }
    }

    /// <summary>释放本页合成纹理及图形；进程共享的字形源只有托管像素。</summary>
    public void Dispose()
    {
        if (graphic != null) Object.Destroy(graphic.gameObject);
        if (material != null) Object.Destroy(material);
        if (texture != null) Object.Destroy(texture);
        graphic = null; material = null; texture = null;
    }
}
