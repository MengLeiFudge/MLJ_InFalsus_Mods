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
    /// <summary>八档徽章的潜力值下界；13、13.5、14 分别对应 Arcaea 的一星、二星、三星。</summary>
    private static readonly decimal[] thresholds = { 0m, 4m, 8m, 11m, 12m, 13m, 13.5m, 14m };
    /// <summary>徽章像素与按需创建的纹理，编号 8 为不可用状态。</summary>
    private readonly PixelImage?[] pixels = new PixelImage[9];
    private readonly Texture2D?[] textures = new Texture2D[9];

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
        string file = tier == 8 ? "rating_off.png" : $"rating_{tier}.png";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PotentialSystem.Badges." + file)
            ?? throw new InvalidDataException($"缺少潜力值徽章：{file}。");
        var decoded = PngRgba.Decode(stream);
        return pixels[tier] = new PixelImage(decoded.Width, decoded.Height, decoded.BottomUpPixels);
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
