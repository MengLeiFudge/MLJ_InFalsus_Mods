using System.Globalization;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using ifapp.Game.UI.Common;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InFalsusMod.Potential;

/// <summary>以 APK 字体和名片配色绘制昵称，独立于已确认的头像与 PTT 图形。</summary>
internal sealed class PotentialNameText : IDisposable
{
    private const int RasterScale = 4, FontSize = 45, Padding = 4 * RasterScale;
    private Constrained2D? graphic;
    private Material? material;
    private Texture2D? texture;
    private string? displayed;
    private int displayedColor = -1;
    private Vector2 imageSize;
    /// <summary>45 字号下的昵称前进宽度，按 APK 账号坐标计算，不含透明留边。</summary>
    internal float NaturalWidth => Math.Max(0f, imageSize.x - 2f * Padding / RasterScale);

    internal PotentialNameText(Constrained2D template, Transform parent)
    {
        try
        {
            graphic = PotentialView.CloneGraphic(template, parent, "Namecard.Name");
            var layout = graphic.constrained2DTransform;
            layout.zPosition = -0.02f;
            graphic.constrained2DTransform = layout;
            material = new Material(layout.renderableTargetMaterial)
            {
                name = "PotentialSystem.Namecard.Name", hideFlags = HideFlags.HideAndDontSave,
                mainTextureScale = Vector2.one, mainTextureOffset = Vector2.zero
            };
            graphic.gameObject.SetActive(false);
        }
        catch { Dispose(); throw; }
    }

    /// <summary>只在昵称或名片款式改变时生成纹理；正常字号固定为 45，长名字才按可用宽度缩小。</summary>
    internal void Set(string value, int color, Vector2 rightCenter, float maxWidth, float scale)
    {
        color = Math.Clamp(color, 0, 3);
        if (string.IsNullOrWhiteSpace(value)) { graphic!.gameObject.SetActive(false); return; }
        if (displayed != value || displayedColor != color)
        {
            // 与编辑框的 40 字限制一致；手工配置过长时仅限制显示，不修改原文。
            var elements = StringInfo.ParseCombiningCharacters(value);
            string text = elements.Length > 40 ? value[..elements[40]] : value;
            var image = Compose(text, color);
            var next = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, false)
            {
                name = "PotentialSystem.Namecard.Name", hideFlags = HideFlags.HideAndDontSave,
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
            imageSize = new Vector2(image.Width, image.Height) / RasterScale;
            displayed = value;
            displayedColor = color;
        }
        graphic!.gameObject.SetActive(true);
        float fit = Math.Min(scale, maxWidth / imageSize.x);
        // 去掉透明留边对右对齐的影响，保留用户名原来的右端及垂直中心。
        BrowserWidgets.Place(graphic, rightCenter + new Vector2(-imageSize.x / 2f + Padding / (float)RasterScale, 0f) * fit,
            imageSize * fit);
    }

    /// <summary>Geosans 不含中文；非 ASCII 名字使用 APK 的 NotoSC Light，并减小描边以保持细字重。</summary>
    private static PixelImage Compose(string value, int color)
    {
        bool latin = value.All(c => c <= 0x7f);
        using var font = new BitmapFont(latin ? "GeosansLight.ttf" : "NotoSansCJKsc-Light.otf",
            latin ? "GeosansLight" : "Noto Sans CJK SC Light", FontSize * RasterScale, latin ? 400 : 300);
        using var measure = new WindowsBitmap(1, 1);
        var size = measure.Measure(value, font);
        int width = checked(size.Width + 2 * Padding), height = checked(size.Height + 2 * Padding);
        using var black = new WindowsBitmap(width, height);
        using var white = new WindowsBitmap(width, height);
        black.Fill(0, 0, width, height, 0);
        white.Fill(0, 0, width, height, 0xffffff);
        // 当前四款背景依次来自 course_banner_4/12/8/2；文字与描边同色，阴影各自固定。
        var (foreground, shadow) = color switch
        {
            0 => (0x344961u, 0xf2f3f5u),
            1 => (0xffffffu, 0xd59f78u),
            2 => (0xffffffu, 0x4b1a79u),
            _ => (0x38505cu, 0xffffffu)
        };
        Draw(black);
        Draw(white);
        return black.ToPixels(white);

        void Draw(WindowsBitmap canvas)
        {
            // APK 的 (1,-2) 转为位图向下的 Y；英文描边 1，中文用 1/4 避免细字被描粗。
            int outline = latin ? RasterScale : 1;
            canvas.OutlinedText(value, font, Padding + RasterScale, Padding + 2 * RasterScale, outline, shadow);
            canvas.OutlinedText(value, font, Padding, Padding, outline, foreground);
        }
    }

    public void Dispose()
    {
        if (graphic != null) Object.Destroy(graphic.gameObject);
        if (material != null) Object.Destroy(material);
        if (texture != null) Object.Destroy(texture);
        graphic = null; material = null; texture = null;
    }
}
