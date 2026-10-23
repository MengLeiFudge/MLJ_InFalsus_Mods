using System.Buffers.Binary;
using System.Numerics;

namespace InFalsusMod.Potential;

/// <summary>解码后的托管音符；时间为毫秒，横坐标和宽度为屏宽比例。</summary>
internal sealed record ChartNote(int Type, int Track, int Time, int End, double X, double EndX,
    double Width, double EndWidth, uint Flags)
{
    internal bool Ground => Type is 1 or 2 && Track != 4;
}

/// <summary>SAM/ICP1 v1 的只读解码；密钥流改编自 RedDragon，许可证见 README。</summary>
internal sealed class ChartDecoder
{
    private ulong s0, s1, s2, s3;
    private uint counter;

    internal static ChartNote[] Decode(byte[] data, string seed, CancellationToken token)
    {
        byte[] key = { 0xf0, 0x16, 0x28, 0x4b, 0x7d, 0x9e, 0xc3, 0xa5 };
        for (int i = 0; i < data.Length; i++)
            data[i] ^= key[i % key.Length];
        if (data.Length < 20 || data[0] != 'I' || data[1] != 'C' || data[2] != 'P' || data[3] != '1')
            throw new InvalidDataException("谱面不是 ICP1 格式。");
        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4));
        ushort header = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(6));
        ushort record = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(8));
        ushort eventSize = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(10));
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12));
        uint events = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(16));
        if (version != 1 || header < 20 || record != 80 || eventSize != 32 || count is 0 or > 100000
            || header + (long)count * record + (long)events * eventSize != data.Length)
            throw new InvalidDataException("谱面版本、长度或记录数量不受支持。");
        var decoder = new ChartDecoder
        {
            s0 = unchecked(0xC4CEB9FE1A85EC53UL + count),
            s1 = ((ulong)count << 32) ^ 0x9E3779B185EBCA87UL,
            s2 = 0xD1B54A32D192ED04UL,
            s3 = 1UL ^ 0x94D049BB133111EBUL
        };
        for (int i = 0; i < seed.Length; i++)
        {
            ulong a = ((ulong)i << 32) | seed[i];
            decoder.Advance(a, unchecked(a + count), 1UL ^ a);
        }
        decoder.Advance(count, 1, (ulong)seed.Length);
        var notes = new ChartNote[count];
        var fields = new ulong[10];
        var scales = new ulong[4];
        var values = new double[4];
        for (uint i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            int offset = checked(header + (int)i * record);
            for (int k = 0; k < fields.Length; k++)
            {
                ulong cipher = k < 2 ? BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset + k * 8))
                    : BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 16 + (k - 2) * 4));
                ulong value = unchecked(cipher - decoder.Generate(i, (uint)k));
                fields[k] = k < 2 ? value : (uint)value;
            }
            for (int k = 0; k < 4; k++)
            {
                long a = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + 48 + k * 8));
                long b = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + 52 + k * 8));
                long divisor = Gcd(Math.Abs(a), Math.Abs(b));
                int factor = unchecked((int)fields[6 + k]);
                uint num = unchecked((uint)(a / divisor * factor));
                uint den = unchecked((uint)(b / divisor * factor));
                scales[k] = ((ulong)den << 32) | num;
                values[k] = unchecked((int)num) / (double)unchecked((int)den);
            }
            decoder.Advance(fields[0], fields[1], (fields[3] << 32) | fields[2]);
            decoder.Advance(fields[4], fields[5], scales[0]);
            decoder.Advance(scales[1], scales[2], scales[3]);
            int type = (int)(fields[4] >> 8), track = (int)(fields[4] & 255);
            if (fields[0] != i || type is not (1 or 2 or 4 or 5) || track is < 1 or > 4
                || fields[2] > fields[3] || fields[3] >= 3600000 || values.Any(v => !double.IsFinite(v)))
                throw new InvalidDataException($"谱面记录 {i} 不符合已知音符结构。");
            notes[i] = new ChartNote(type, track, (int)fields[2], (int)fields[3], values[0], values[1],
                values[2], values[3], (uint)fields[5]);
        }
        return notes;
    }

    private static long Gcd(long a, long b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return Math.Max(1, a);
    }

    private ulong Generate(uint index, uint k)
    {
        unchecked
        {
            ulong pair = ((ulong)index << 32) | k;
            return (k & 3) switch
            {
                3 => BitOperations.RotateLeft(s2 + s1 + pair, 37) ^ s3,
                2 => (BitOperations.RotateLeft(pair ^ s0, 29) + s2) ^ s1,
                1 => (s1 - BitOperations.RotateLeft(s3 + pair, 17)) ^ s0,
                _ => (BitOperations.RotateLeft(pair ^ s2, 11) + s0) ^ s3
            };
        }
    }

    private void Advance(ulong a, ulong b, ulong c)
    {
        unchecked
        {
            switch (counter++ & 3)
            {
                case 0:
                    s0 += BitOperations.RotateLeft(s3, 7) + a;
                    s1 ^= BitOperations.RotateLeft(s0 + b, 13);
                    s2 -= c ^ s1;
                    s3 = BitOperations.RotateLeft(s2 + s3, 27) ^ s0;
                    break;
                case 1:
                    s1 += BitOperations.RotateLeft(s0, 9) + c;
                    s2 ^= BitOperations.RotateLeft(s1 + a, 19);
                    s3 -= b ^ s2;
                    s0 = BitOperations.RotateLeft(s3 + s0, 31) ^ s1;
                    break;
                case 2:
                    s2 += BitOperations.RotateLeft(s1, 15) + b;
                    s3 ^= BitOperations.RotateLeft(s2 + c, 23);
                    s0 -= a ^ s3;
                    s1 = BitOperations.RotateLeft(s1 + s0, 39) ^ s2;
                    break;
                default:
                    ulong keep = s2;
                    s3 += BitOperations.RotateLeft(s2, 21) + a;
                    s0 ^= BitOperations.RotateLeft(s3 + b, 29);
                    s1 -= s0 ^ c;
                    s2 = BitOperations.RotateLeft(keep + s1, 43) ^ s3;
                    break;
            }
        }
    }
}
