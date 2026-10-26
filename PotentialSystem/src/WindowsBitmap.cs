using System.ComponentModel;
using System.Runtime.InteropServices;

namespace InFalsusMod.Potential;

/// <summary>Windows 内存位图画布，按成绩数据绘图，不读取游戏屏幕或创建外部依赖。</summary>
internal sealed class WindowsBitmap : IDisposable
{
    /// <summary>GDI 画布、位图、之前选入的对象及像素指针。</summary>
    private IntPtr dc, bitmap, previous, bits;
    /// <summary>本画布拥有的字体句柄，销毁前必须从 DC 中移出。</summary>
    private readonly Dictionary<(int Size, bool Bold), IntPtr> fonts = new();
    /// <summary>位图像素尺寸，行序为自上而下。</summary>
    internal int Width { get; }
    internal int Height { get; }

    /// <summary>建立 32 位、无压缩的顶向下 DIB；任何失败都会回收已取得的句柄。</summary>
    internal WindowsBitmap(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        Height = height;
        try
        {
            dc = CreateCompatibleDC(IntPtr.Zero);
            Check(dc != IntPtr.Zero, "建立图像画布");
            var info = new BitmapHeader(width, -height);
            bitmap = CreateDIBSection(dc, ref info, 0, out bits, IntPtr.Zero, 0);
            Check(bitmap != IntPtr.Zero && bits != IntPtr.Zero, "建立图像位图");
            previous = SelectObject(dc, bitmap);
            Check(previous != IntPtr.Zero && previous != new IntPtr(-1), "选择图像位图");
            Check(SetBkMode(dc, 1) != 0, "设置文字背景");
        }
        catch { Dispose(); throw; }
    }

    /// <summary>将普通 RGB 颜色转为 GDI 的 COLORREF。</summary>
    private static uint ColorRef(uint rgb) => (rgb >> 16) | (rgb & 0xff00) | ((rgb & 0xff) << 16);
    /// <summary>Win32 绘图失败必须报告，不能产生伪成功的剪贴板提示。</summary>
    private static void Check(bool success, string action)
    {
        if (!success)
            throw new Win32Exception(Marshal.GetLastWin32Error(), action + "失败。");
    }

    /// <summary>填充指定矩形；临时画刷始终回收。</summary>
    internal void Fill(int x, int y, int width, int height, uint rgb)
    {
        var brush = CreateSolidBrush(ColorRef(rgb));
        Check(brush != IntPtr.Zero, "建立画刷");
        try
        {
            var rectangle = new NativeRect(x, y, width, height);
            Check(FillRect(dc, ref rectangle, brush) != 0, "填充图像");
        }
        finally { DeleteObject(brush); }
    }

    /// <summary>绘制圆角卡片底板；临时画刷从 DC 移出后回收，空画笔只借用系统对象。</summary>
    /// <param name="bounds">左上角位置及卡片像素尺寸。</param>
    /// <param name="radius">圆角的像素半径。</param>
    /// <param name="rgb">填充颜色。</param>
    internal void RoundedFill((int X, int Y, int Width, int Height) bounds, int radius, uint rgb)
    {
        var brush = CreateSolidBrush(ColorRef(rgb));
        Check(brush != IntPtr.Zero, "建立圆角画刷");
        IntPtr oldBrush = IntPtr.Zero, oldPen = IntPtr.Zero;
        try
        {
            oldBrush = SelectObject(dc, brush);
            Check(oldBrush != IntPtr.Zero && oldBrush != new IntPtr(-1), "选择圆角画刷");
            oldPen = SelectObject(dc, GetStockObject(8));
            Check(oldPen != IntPtr.Zero && oldPen != new IntPtr(-1), "选择圆角画笔");
            Check(RoundRect(dc, bounds.X, bounds.Y, bounds.X + bounds.Width, bounds.Y + bounds.Height,
                radius * 2, radius * 2), "绘制圆角卡片");
        }
        finally
        {
            if (oldPen != IntPtr.Zero && oldPen != new IntPtr(-1)) SelectObject(dc, oldPen);
            if (oldBrush != IntPtr.Zero && oldBrush != new IntPtr(-1)) SelectObject(dc, oldBrush);
            DeleteObject(brush);
        }
    }

    /// <summary>绘制连续折线，原生画笔从画布移出后释放。</summary>
    /// <param name="points">自上而下位图坐标。</param>
    /// <param name="rgb">线条颜色。</param>
    /// <param name="width">像素线宽。</param>
    internal void Stroke(IReadOnlyList<(int X, int Y)> points, uint rgb, int width = 2)
    {
        if (points.Count < 2) return;
        var pen = CreatePen(0, width, ColorRef(rgb));
        Check(pen != IntPtr.Zero, "建立曲线画笔");
        IntPtr old = IntPtr.Zero;
        try
        {
            old = SelectObject(dc, pen);
            Check(old != IntPtr.Zero && old != new IntPtr(-1), "选择曲线画笔");
            Check(MoveToEx(dc, points[0].X, points[0].Y, IntPtr.Zero), "定位曲线");
            for (int i = 1; i < points.Count; i++)
                Check(LineTo(dc, points[i].X, points[i].Y), "绘制曲线");
        }
        finally
        {
            if (old != IntPtr.Zero && old != new IntPtr(-1)) SelectObject(dc, old);
            DeleteObject(pen);
        }
    }

    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreatePen(int style, int width, uint color);
    [DllImport("gdi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool MoveToEx(IntPtr dc, int x, int y, IntPtr previous);
    [DllImport("gdi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool LineTo(IntPtr dc, int x, int y);

    /// <summary>使用系统中英文字体绘制单行，过长时在矩形内省略，不解析歌曲名中的 &amp;。</summary>
    internal void Text(string text, int x, int y, int width, int height, int size, uint rgb, bool bold = false, bool right = false)
    {
        if (string.IsNullOrEmpty(text))
            return;
        var key = (size, bold);
        if (!fonts.TryGetValue(key, out var font))
        {
            font = CreateFontW(-size, 0, 0, 0, bold ? 700 : 400, 0, 0, 0, 1, 4, 0, 4, 0, "Microsoft YaHei UI");
            Check(font != IntPtr.Zero, "建立分享图字体");
            fonts.Add(key, font);
        }
        var old = SelectObject(dc, font);
        Check(old != IntPtr.Zero && old != new IntPtr(-1), "选择分享图字体");
        try
        {
            SetTextColor(dc, ColorRef(rgb));
            var rectangle = new NativeRect(x, y, width, height);
            Check(DrawTextW(dc, text.Replace('\n', ' ').Replace('\r', ' '), -1, ref rectangle,
                0x20u | 0x4u | 0x8000u | 0x800u | (right ? 2u : 0u)) != 0, "绘制分享图文字");
        }
        finally { SelectObject(dc, old); }
    }

    /// <summary>使用已注册字体的实际行框度量，不套用选歌模板文字的边界或缩放。</summary>
    internal (int Width, int Height) Measure(string text, BitmapFont font)
    {
        var old = SelectObject(dc, font.Handle);
        Check(old != IntPtr.Zero && old != new IntPtr(-1), "选择名片字体");
        try
        {
            Check(GetTextExtentPoint32W(dc, text, text.Length, out var size), "测量名片文字");
            return (size.Width, size.Height);
        }
        finally { SelectObject(dc, old); }
    }

    /// <summary>按原字体轮廓填字及描边；高分辨率画布缩放后形成抗锯齿，不依赖游戏 SDF 材质。</summary>
    internal void OutlinedText(string text, BitmapFont font, int x, int y, int outline, uint rgb, uint? stroke = null)
    {
        var brush = CreateSolidBrush(ColorRef(rgb));
        Check(brush != IntPtr.Zero, "建立名片文字画刷");
        IntPtr pen = IntPtr.Zero, oldFont = IntPtr.Zero, oldBrush = IntPtr.Zero, oldPen = IntPtr.Zero;
        try
        {
            pen = CreatePen(0, outline * 2, ColorRef(stroke ?? rgb));
            Check(pen != IntPtr.Zero, "建立名片文字描边");
            oldFont = SelectObject(dc, font.Handle);
            Check(oldFont != IntPtr.Zero && oldFont != new IntPtr(-1), "选择名片字体");
            oldBrush = SelectObject(dc, brush);
            Check(oldBrush != IntPtr.Zero && oldBrush != new IntPtr(-1), "选择名片文字画刷");
            oldPen = SelectObject(dc, pen);
            Check(oldPen != IntPtr.Zero && oldPen != new IntPtr(-1), "选择名片文字描边");
            Check(BeginPath(dc), "开始名片文字轮廓");
            Check(TextOutW(dc, x, y, text, text.Length), "生成名片文字轮廓");
            Check(EndPath(dc), "完成名片文字轮廓");
            Check(StrokeAndFillPath(dc), "绘制名片文字");
        }
        finally
        {
            if (oldPen != IntPtr.Zero && oldPen != new IntPtr(-1)) SelectObject(dc, oldPen);
            if (oldBrush != IntPtr.Zero && oldBrush != new IntPtr(-1)) SelectObject(dc, oldBrush);
            if (oldFont != IntPtr.Zero && oldFont != new IntPtr(-1)) SelectObject(dc, oldFont);
            if (pen != IntPtr.Zero) DeleteObject(pen);
            DeleteObject(brush);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { internal int Width, Height; }
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetTextExtentPoint32W(IntPtr dc, string text, int length, out NativeSize size);
    [DllImport("gdi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool BeginPath(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EndPath(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool StrokeAndFillPath(IntPtr dc);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TextOutW(IntPtr dc, int x, int y, string text, int length);

    /// <summary>透明图像转成预乘 BGRA 后合成，保留 Arcaea 徽章的透明边缘。</summary>
    internal void Image(PixelImage image, int x, int y, int width, int height)
    {
        if (image.Rgba.Length != checked(image.Width * image.Height * 4))
            throw new InvalidDataException("分享图素材像素长度无效。");
        using var source = new WindowsBitmap(image.Width, image.Height);
        var bgra = new byte[image.Rgba.Length];
        for (int row = 0; row < image.Height; row++)
            for (int column = 0; column < image.Width; column++)
            {
                int from = ((image.Height - 1 - row) * image.Width + column) * 4;
                int to = (row * image.Width + column) * 4;
                int alpha = image.Rgba[from + 3];
                bgra[to] = (byte)((image.Rgba[from + 2] * alpha + 127) / 255);
                bgra[to + 1] = (byte)((image.Rgba[from + 1] * alpha + 127) / 255);
                bgra[to + 2] = (byte)((image.Rgba[from] * alpha + 127) / 255);
                bgra[to + 3] = (byte)alpha;
            }
        Marshal.Copy(bgra, 0, source.bits, bgra.Length);
        Check(AlphaBlend(dc, x, y, width, height, source.dc, 0, 0, image.Width, image.Height,
            new BlendFunction { SourceConstantAlpha = 255, AlphaFormat = 1 }), "合成分享图素材");
    }

    /// <summary>将 GDI 位图转为底向上 RGBA；提供同内容的白底画布时，从黑白两次绘制恢复透明度及抗锯齿边缘。</summary>
    /// <param name="whiteMatte">同尺寸白底副本；当前画布必须为黑底。为空时输出不透明图像。</param>
    internal PixelImage ToPixels(WindowsBitmap? whiteMatte = null)
    {
        Check(GdiFlush(), "完成图像绘制");
        var bgra = new byte[checked(Width * Height * 4)];
        Marshal.Copy(bits, bgra, 0, bgra.Length);
        byte[]? matte = null;
        if (whiteMatte != null)
        {
            if (whiteMatte.Width != Width || whiteMatte.Height != Height)
                throw new ArgumentException("透明图像的黑白画布尺寸不一致。", nameof(whiteMatte));
            matte = new byte[bgra.Length];
            Marshal.Copy(whiteMatte.bits, matte, 0, matte.Length);
        }
        var rgba = new byte[bgra.Length];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int source = (y * Width + x) * 4;
                int target = ((Height - 1 - y) * Width + x) * 4;
                int alpha = matte == null ? 255 : Math.Clamp(255 - Math.Max(matte[source] - bgra[source],
                    Math.Max(matte[source + 1] - bgra[source + 1], matte[source + 2] - bgra[source + 2])), 0, 255);
                if (alpha == 0) continue;
                rgba[target] = (byte)Math.Min(255, (bgra[source + 2] * 255 + alpha / 2) / alpha);
                rgba[target + 1] = (byte)Math.Min(255, (bgra[source + 1] * 255 + alpha / 2) / alpha);
                rgba[target + 2] = (byte)Math.Min(255, (bgra[source] * 255 + alpha / 2) / alpha);
                rgba[target + 3] = (byte)alpha;
            }
        return new PixelImage(Width, Height, rgba);
    }

    /// <summary>输出广泛兼容的 CF_DIB 数据：40 字节头、正高度、底向上 BGRA 像素。</summary>
    internal byte[] ToClipboardDib()
    {
        Check(GdiFlush(), "完成图像绘制");
        int stride = checked(Width * 4);
        var pixels = new byte[checked(stride * Height)];
        Marshal.Copy(bits, pixels, 0, pixels.Length);
        for (int i = 3; i < pixels.Length; i += 4)
            pixels[i] = 255;
        using var buffer = new MemoryStream(checked(40 + pixels.Length));
        using var writer = new BinaryWriter(buffer);
        writer.Write(40u);
        writer.Write(Width);
        writer.Write(Height);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0u);
        writer.Write((uint)pixels.Length);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0u);
        writer.Write(0u);
        for (int row = Height - 1; row >= 0; row--)
            writer.Write(pixels, row * stride, stride);
        return buffer.ToArray();
    }

    /// <summary>恢复 DC 原对象后释放字体、位图与 DC，允许初始化失败时调用。</summary>
    public void Dispose()
    {
        if (dc != IntPtr.Zero && previous != IntPtr.Zero && previous != new IntPtr(-1))
            SelectObject(dc, previous);
        foreach (var font in fonts.Values)
            DeleteObject(font);
        fonts.Clear();
        if (bitmap != IntPtr.Zero)
            DeleteObject(bitmap);
        if (dc != IntPtr.Zero)
            DeleteDC(dc);
        dc = bitmap = previous = bits = IntPtr.Zero;
    }

    /// <summary>Win32 BITMAPINFOHEADER，无调色板的 32 位 DIB 正好使用这 40 字节。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapHeader
    {
        internal uint Size;
        internal int Width, Height;
        internal ushort Planes, BitCount;
        internal uint Compression, ImageSize;
        internal int XPelsPerMeter, YPelsPerMeter;
        internal uint ColorsUsed, ColorsImportant;
        internal BitmapHeader(int width, int height)
        {
            Size = 40; Width = width; Height = height; Planes = 1; BitCount = 32;
            Compression = 0; ImageSize = checked((uint)(width * Math.Abs(height) * 4));
            XPelsPerMeter = YPelsPerMeter = 0; ColorsUsed = ColorsImportant = 0;
        }
    }
    /// <summary>GDI 使用边界坐标的矩形。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left, Top, Right, Bottom;
        internal NativeRect(int x, int y, int width, int height)
        { Left = x; Top = y; Right = x + width; Bottom = y + height; }
    }
    /// <summary>AlphaBlend 的四字节值参数。</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        internal byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
    }

    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GdiFlush();
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapHeader info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int objectType);
    [DllImport("gdi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RoundRect(IntPtr hdc, int left, int top, int right, int bottom, int width, int height);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern int SetBkMode(IntPtr hdc, int mode);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr hdc, uint color);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeOut, uint charSet, uint outputPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);
    [DllImport("user32.dll", SetLastError = true)] private static extern int FillRect(IntPtr hdc, ref NativeRect rect, IntPtr brush);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int DrawTextW(IntPtr hdc, string text, int length, ref NativeRect rect, uint format);
    [DllImport("msimg32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AlphaBlend(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, int sourceWidth, int sourceHeight, BlendFunction blend);
}
