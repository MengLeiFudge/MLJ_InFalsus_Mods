namespace InFalsusMod.Potential;

/// <summary>曲绘的印象色用于深色卡片及边框，亮度受限以保证浅色成绩文字的对比度。</summary>
/// <param name="Background">保留曲绘色相的深色背景。</param>
/// <param name="Border">比背景稍亮的同色边框。</param>
internal readonly record struct CoverPalette(uint Background, uint Border)
{
    /// <summary>封面尚未载入或没有可见像素时使用的中性色。</summary>
    internal static CoverPalette Neutral => new(0x232b37, 0x465364);

    /// <summary>分箱统计非极暗、非极亮像素，按色彩强度加权后取最主要的色群。</summary>
    /// <param name="image">自有 RGBA 曲绘像素；行方向不影响颜色统计。</param>
    /// <returns>浏览卡片与导出图片共用的背景及边框颜色。</returns>
    internal static CoverPalette From(PixelImage image)
    {
        if (image.Rgba.Length != checked(image.Width * image.Height * 4))
            throw new InvalidDataException("曲绘颜色统计的像素长度无效。");
        var weights = new int[512];
        var red = new long[512];
        var green = new long[512];
        var blue = new long[512];
        long totalRed = 0, totalGreen = 0, totalBlue = 0;
        int visible = 0;
        // 每四个像素采样一次；统计只在封面读取成功后进行，不放进逐帧绘制路径。
        for (int i = 0; i < image.Rgba.Length; i += 16)
        {
            if (image.Rgba[i + 3] < 128)
                continue;
            int r = image.Rgba[i], g = image.Rgba[i + 1], b = image.Rgba[i + 2];
            totalRed += r; totalGreen += g; totalBlue += b;
            visible++;
            int luminance = (r * 54 + g * 183 + b * 19) / 256;
            if (luminance < 24 || luminance > 226)
                continue;
            int weight = 32 + Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
            int bin = ((r >> 5) << 6) | ((g >> 5) << 3) | (b >> 5);
            weights[bin] += weight;
            red[bin] += (long)r * weight;
            green[bin] += (long)g * weight;
            blue[bin] += (long)b * weight;
        }
        if (visible == 0)
            return Neutral;
        int dominant = 0;
        for (int i = 1; i < weights.Length; i++)
            if (weights[i] > weights[dominant])
                dominant = i;
        int count = weights[dominant];
        var rgb = count == 0
            ? (Red: (int)(totalRed / visible), Green: (int)(totalGreen / visible), Blue: (int)(totalBlue / visible))
            : (Red: (int)(red[dominant] / count), Green: (int)(green[dominant] / count), Blue: (int)(blue[dominant] / count));
        return new CoverPalette(Shade(rgb, 0.3f), Shade(rgb, 0.48f));
    }

    /// <summary>与固定深灰底混合，避免白色或明亮曲绘使卡片上的文字失去对比度。</summary>
    private static uint Shade((int Red, int Green, int Blue) rgb, float amount)
    {
        int r = (int)(18 + (rgb.Red - 18) * amount);
        int g = (int)(25 + (rgb.Green - 25) * amount);
        int b = (int)(35 + (rgb.Blue - 35) * amount);
        return (uint)((r << 16) | (g << 8) | b);
    }
}
