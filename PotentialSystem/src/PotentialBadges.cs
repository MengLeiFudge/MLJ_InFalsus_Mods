using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InFalsusMod.Potential;

/// <summary>自下而上排列的 RGBA32 像素，供 Unity 展示和独立 B50 绘图共用。</summary>
/// <param name="Width">像素宽度。</param>
/// <param name="Height">像素高度。</param>
/// <param name="Rgba">长度严格为宽×高×4 的像素。</param>
internal sealed record PixelImage(int Width, int Height, byte[] Rgba);

/// <summary>拥有嵌入的 Arcaea 徽章及其 Unity 纹理，按固定潜力值门槛选择档位。</summary>
internal sealed class PotentialBadges : IDisposable
{
    /// <summary>八档潜力值徽章的下界；13、13.5、14 分别对应 Arcaea 的一星、二星、三星。</summary>
    private static readonly decimal[] thresholds = { 0m, 4m, 8m, 11m, 12m, 13m, 13.5m, 14m };
    /// <summary>徽章像素与按需创建的纹理；8 为不可用状态，9、10 为账号细框和头像底，11～14 为名片，15、16 为设定按钮和齿轮。</summary>
    private readonly PixelImage?[] pixels = new PixelImage[17];
    private readonly Texture2D?[] textures = new Texture2D[17];
    /// <summary>UserCell 的 icon_back 正常状态细框；上角按游戏边界裁切。</summary>
    internal Texture2D AvatarFrame => Texture(9);
    /// <summary>与角色头像同尺寸的深色菱形底，保证透明区域不透出曲绘。</summary>
    internal Texture2D AvatarBacking => Texture(10);
    /// <summary>APK 自带的阶梯名片横幅，保留斜切轮廓、细边和纹理。</summary>
    internal Texture2D Namecard(int color) => Texture(11 + color);
    /// <summary>按 TopBar.csb 合成的设定按钮，含原始底图、齿轮和文字。</summary>
    internal Texture2D SettingsButton => Texture(15);

    /// <summary>按潜力值选择固定档位，选歌、结算、成绩浏览和 B50 导出共用。</summary>
    /// <param name="potential">当前显示或导出快照的账号潜力值。</param>
    /// <returns>从低到高的徽章编号，范围为 0～7。</returns>
    internal static int Tier(decimal potential)
    {
        int tier = 0;
        for (int i = 1; i < thresholds.Length; i++)
            if (potential >= thresholds[i])
                tier = i;
        return tier;
    }

    /// <summary>读取 DLL 内的原始 PNG，保留透明背景及不同素材的原始尺寸。</summary>
    internal PixelImage Pixels(int tier)
    {
        if (pixels[tier] != null)
            return pixels[tier]!;
        string file = tier switch
        {
            8 => "rating_off.png", 9 => "avatar_frame.png", 10 => "avatar_backing.png",
            15 => "settings_button.png", 16 => "settings_icon.png",
            >= 11 => $"namecard_{tier - 11}.png", _ => $"rating_{tier}.png"
        };
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PotentialSystem.Badges." + file)
            ?? throw new InvalidDataException($"缺少潜力值徽章：{file}。");
        var decoded = PngRgba.Decode(stream);
        var image = new PixelImage(decoded.Width, decoded.Height, decoded.BottomUpPixels);
        // TopBar 的 settingsImage opacity=99；只调整其独立像素缓存，不修改 APK 原件。
        if (tier == 16)
            for (int i = 3; i < image.Rgba.Length; i += 4) image.Rgba[i] = (byte)(image.Rgba[i] * 99 / 255);
        return pixels[tier] = tier == 15 ? DrawSettings(image) : image;
    }

    /// <summary>将 TopBar 的 98×51 按三倍设计单位绘制；文字为 16 字号、2 宽深色描边。</summary>
    private PixelImage DrawSettings(PixelImage background)
    {
        using var black = new WindowsBitmap(294, 153);
        using var white = new WindowsBitmap(294, 153);
        using var font = new BitmapFont("NotoSansCJKsc-Regular.otf", "Noto Sans CJK SC Regular", 48);
        var size = black.Measure("设定", font);
        black.Fill(0, 0, 294, 153, 0);
        white.Fill(0, 0, 294, 153, 0xffffff);
        Draw(black);
        Draw(white);
        return black.ToPixels(white);

        void Draw(WindowsBitmap canvas)
        {
            canvas.Image(background, 0, 0, 294, 153);
            canvas.Image(Pixels(16), 97, 41, 93, 93);
            canvas.OutlinedText("设定", font, 147 - size.Width / 2, 90 - size.Height / 2, 6, 0xffffff, 0x402d3f);
        }
    }

    /// <summary>在 Unity 主线程创建可复用纹理，避免当前 interop 的 LoadImage/Span 限制。</summary>
    internal Texture2D Texture(int tier)
    {
        if (textures[tier] != null)
            return textures[tier]!;
        var image = Pixels(tier);
        var texture = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, false)
        {
            name = "PotentialSystem.Badge." + tier,
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        try
        {
            texture.LoadRawTextureData(new Il2CppStructArray<byte>(image.Rgba));
            texture.Apply(false, true);
            return textures[tier] = texture;
        }
        catch { Object.Destroy(texture); throw; }
    }

    /// <summary>所有视图退出后销毁自有纹理；游戏封面和字体不归本对象所有。</summary>
    public void Dispose()
    {
        foreach (var texture in textures)
            if (texture != null)
                Object.Destroy(texture);
        Array.Clear(textures, 0, textures.Length);
    }
}
