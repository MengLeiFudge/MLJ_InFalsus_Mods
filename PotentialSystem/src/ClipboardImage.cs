using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace InFalsusMod.Potential;

/// <summary>仅响应玩家导出请求，把已生成的图片作为 CF_DIB 写入 Windows 剪贴板。</summary>
internal static class ClipboardImage
{
    /// <summary>全部字节准备成功后再打开剪贴板；成功移交内存后由系统负责回收。</summary>
    internal static void Copy(byte[] dib)
    {
        if (dib.Length < 40)
            throw new InvalidDataException("生成的分享图不完整。");
        IntPtr window = GetActiveWindow();
        if (window == IntPtr.Zero)
            window = Process.GetCurrentProcess().MainWindowHandle;
        if (window == IntPtr.Zero)
            throw new InvalidOperationException("无法取得游戏窗口，图片尚未写入剪贴板。");
        IntPtr memory = GlobalAlloc(0x2u, (UIntPtr)(uint)dib.Length);
        if (memory == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法分配剪贴板图片内存。");
        bool opened = false;
        try
        {
            IntPtr pointer = GlobalLock(memory);
            if (pointer == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法锁定剪贴板图片内存。");
            try { Marshal.Copy(dib, 0, pointer, dib.Length); }
            finally { GlobalUnlock(memory); }
            if (!OpenClipboard(window))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "剪贴板正被占用，请稍后再次导出。");
            opened = true;
            if (!EmptyClipboard() || SetClipboardData(8u, memory) == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法把分享图写入剪贴板。");
            memory = IntPtr.Zero;
        }
        finally
        {
            if (opened)
                CloseClipboard();
            if (memory != IntPtr.Zero)
                GlobalFree(memory);
        }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint format, IntPtr memory);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseClipboard();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr memory);
}
