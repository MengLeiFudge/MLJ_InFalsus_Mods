using System.Buffers.Binary;
using System.IO.Compression;

namespace InFalsusMod;

/// <summary>
/// 解码模组自带的 8 位 RGBA 非隔行 PNG。只覆盖嵌入资源实际使用的格式，
/// 用于绕开当前 interop 中不可用的 ImageConversion.LoadImage。
/// </summary>
internal static class PngRgba
{
    /// <summary>PNG 文件签名。</summary>
    private static readonly byte[] Signature = { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A };
    /// <summary>每像素字节数：R、G、B、A 各 8 位。</summary>
    private const int BytesPerPixel = 4;

    /// <summary>读取完整 PNG，并按 Unity 纹理行序（自下而上）输出像素。</summary>
    /// <param name="stream">PNG 数据流。</param>
    /// <returns>尺寸及 RGBA32 像素，长度为宽×高×4。</returns>
    internal static DecodedImage Decode(Stream stream)
    {
        using var file = new MemoryStream();
        stream.CopyTo(file);
        var data = file.GetBuffer().AsSpan(0, (int)file.Length);
        if (data.Length < Signature.Length || !data[..Signature.Length].SequenceEqual(Signature))
            throw new InvalidDataException("特性筛选入口图案不是 PNG。");

        int width = 0, height = 0;
        using var compressed = new MemoryStream();
        for (int offset = Signature.Length; ;)
        {
            if (offset + 12 > data.Length)
                throw new InvalidDataException("特性筛选入口图案数据不完整。");
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data[offset..]));
            var type = data.Slice(offset + 4, 4);
            if (offset + 12 + length > data.Length)
                throw new InvalidDataException("特性筛选入口图案数据不完整。");
            var body = data.Slice(offset + 8, length);
            if (type.SequenceEqual("IHDR"u8))
            {
                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(body));
                height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(body[4..]));
                // 位深 8、颜色类型 6（RGBA）、默认压缩与过滤、非隔行。
                if (width <= 0 || height <= 0 || body[8] != 8 || body[9] != 6
                    || body[10] != 0 || body[11] != 0 || body[12] != 0)
                    throw new InvalidDataException("特性筛选入口图案必须是 8 位 RGBA 非隔行 PNG。");
            }
            else if (type.SequenceEqual("IDAT"u8))
                compressed.Write(body);
            else if (type.SequenceEqual("IEND"u8))
                break;
            offset += 12 + length;
        }
        if (width == 0)
            throw new InvalidDataException("特性筛选入口图案缺少 IHDR。");

        int stride = checked(width * BytesPerPixel);
        var filtered = new byte[checked((stride + 1) * height)];
        compressed.Position = 0;
        // 目标框架为 net6.0，没有 Stream.ReadExactly；逐段读取直到填满。
        using (var inflater = new ZLibStream(compressed, CompressionMode.Decompress))
            for (int read = 0, count; read < filtered.Length; read += count)
                if ((count = inflater.Read(filtered, read, filtered.Length - read)) == 0)
                    throw new InvalidDataException("特性筛选入口图案像素数据不完整。");

        var pixels = new byte[checked(stride * height)];
        var previous = new byte[stride];
        var current = new byte[stride];
        for (int row = 0; row < height; row++)
        {
            int start = row * (stride + 1);
            byte filter = filtered[start];
            filtered.AsSpan(start + 1, stride).CopyTo(current);
            Unfilter(filter, current, previous);
            // PNG 自上而下存储，Unity 纹理第 0 行在底部。
            current.CopyTo(pixels.AsSpan((height - 1 - row) * stride, stride));
            (previous, current) = (current, previous);
        }
        return new DecodedImage(width, height, pixels);
    }

    /// <summary>按 PNG 规范还原一行过滤数据。</summary>
    /// <param name="filter">本行过滤类型 0–4。</param>
    /// <param name="line">本行过滤后的字节，原地还原。</param>
    /// <param name="previous">已还原的上一行；首行为全零。</param>
    private static void Unfilter(byte filter, byte[] line, byte[] previous)
    {
        for (int i = 0; i < line.Length; i++)
        {
            int left = i >= BytesPerPixel ? line[i - BytesPerPixel] : 0;
            int up = previous[i];
            int upLeft = i >= BytesPerPixel ? previous[i - BytesPerPixel] : 0;
            int predictor = filter switch
            {
                0 => 0,
                1 => left,
                2 => up,
                3 => (left + up) / 2,
                4 => Paeth(left, up, upLeft),
                _ => throw new InvalidDataException("特性筛选入口图案使用了未知的 PNG 过滤类型。")
            };
            line[i] = (byte)(line[i] + predictor);
        }
    }

    /// <summary>PNG Paeth 预测：选择与 left+up−upLeft 最接近的相邻值。</summary>
    /// <param name="left">左侧字节。</param>
    /// <param name="up">上方字节。</param>
    /// <param name="upLeft">左上字节。</param>
    /// <returns>预测值。</returns>
    private static int Paeth(int left, int up, int upLeft)
    {
        int estimate = left + up - upLeft;
        int toLeft = Math.Abs(estimate - left), toUp = Math.Abs(estimate - up), toUpLeft = Math.Abs(estimate - upLeft);
        return toLeft <= toUp && toLeft <= toUpLeft ? left : toUp <= toUpLeft ? up : upLeft;
    }
}

/// <summary>解码后的 RGBA32 图像。</summary>
/// <param name="Width">宽度，像素。</param>
/// <param name="Height">高度，像素。</param>
/// <param name="BottomUpPixels">自下而上排列的 RGBA32 像素。</param>
internal readonly record struct DecodedImage(int Width, int Height, byte[] BottomUpPixels);
