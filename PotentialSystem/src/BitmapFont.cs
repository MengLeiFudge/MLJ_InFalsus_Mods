using System.ComponentModel;
using System.Runtime.InteropServices;

namespace InFalsusMod.Potential;

/// <summary>临时注册 DLL 中的原字体，仅供当前进程位图绘制；不安装到系统。</summary>
internal sealed class BitmapFont : IDisposable
{
    private IntPtr data, resource;
    internal IntPtr Handle { get; private set; }

    internal BitmapFont(string file, string family, int size)
    {
        try
        {
            using var stream = typeof(BitmapFont).Assembly.GetManifestResourceStream("PotentialSystem.Fonts." + file)
                ?? throw new InvalidDataException("缺少名片字体：" + file);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var bytes = buffer.ToArray();
            data = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, data, bytes.Length);
            uint count = 0;
            resource = AddFontMemResourceEx(data, (uint)bytes.Length, IntPtr.Zero, ref count);
            if (resource == IntPtr.Zero || count == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "加载名片字体失败。");
            Handle = CreateFontW(-size, 0, 0, 0, 400, 0, 0, 0, 1, 4, 0, 4, 0, family);
            if (Handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "建立名片字体失败。");
        }
        catch { Dispose(); throw; }
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero) DeleteObject(Handle);
        if (resource != IntPtr.Zero) RemoveFontMemResourceEx(resource);
        if (data != IntPtr.Zero) Marshal.FreeHGlobal(data);
        Handle = resource = data = IntPtr.Zero;
    }

    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr AddFontMemResourceEx(IntPtr data, uint size, IntPtr reserved, ref uint count);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveFontMemResourceEx(IntPtr resource);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeOut, uint charSet, uint outputPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);
}
