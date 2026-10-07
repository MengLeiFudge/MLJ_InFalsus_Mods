using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Logging;

namespace InFalsusMod;

/// <summary>修正 BepInEx IL2CPP 托管编码器未同步 Windows 控制台输出代码页的问题。</summary>
internal static class WindowsConsoleEncoding
{
    /// <summary>UTF-8 的 Windows 代码页。</summary>
    private const uint Utf8CodePage = 65001;

    /// <summary>在原版 BepInEx 控制台存在时，把其输出代码页校准为托管写入器使用的 UTF-8。</summary>
    /// <param name="logger">设置失败时用于记录英文诊断的插件日志。</param>
    internal static void EnsureUtf8(ManualLogSource logger)
    {
        if (!OperatingSystem.IsWindows() || !ConsoleManager.ConsoleActive || GetConsoleOutputCP() == Utf8CodePage)
            return;

        if (!SetConsoleOutputCP(Utf8CodePage))
        {
            logger.LogError($"Failed to set the BepInEx console output code page to UTF-8 (Win32 error {Marshal.GetLastWin32Error()}).");
            return;
        }

        uint actual = GetConsoleOutputCP();
        if (actual != Utf8CodePage)
            logger.LogError($"BepInEx console output code page remained {actual}; expected {Utf8CodePage}.");
    }

    /// <summary>读取当前进程所附加控制台的输出代码页。</summary>
    /// <returns>Windows 控制台输出代码页；没有控制台时为零。</returns>
    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleOutputCP();

    /// <summary>修改当前进程所附加控制台的输出代码页。</summary>
    /// <param name="codePage">目标 Windows 代码页。</param>
    /// <returns>设置成功时为 <see langword="true"/>。</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleOutputCP(uint codePage);
}
