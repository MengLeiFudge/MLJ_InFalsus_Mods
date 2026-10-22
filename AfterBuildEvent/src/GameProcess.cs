using System.Diagnostics;

namespace AfterBuildEvent;

/// <summary>在替换模组和游戏引导文件前，关闭当前配置的 In Falsus 进程。</summary>
internal static class GameProcess
{
    /// <summary>先请求窗口关闭，再结束超时进程；核对程序路径，避免影响同名程序。</summary>
    /// <param name="context">提供获授权更新的游戏目录。</param>
    internal static void Stop(RepositoryContext context)
    {
        string expected = Path.GetFullPath(Path.Combine(context.GameDirectory, "infalsus.exe"));
        int stopped = 0;
        foreach (Process process in Process.GetProcessesByName("infalsus"))
        {
            using (process)
            {
                if (process.HasExited)
                    continue;
                string actual = process.MainModule?.FileName
                    ?? throw new InvalidOperationException($"无法核对游戏进程 {process.Id} 的程序路径。");
                if (!Path.GetFullPath(actual).Equals(expected, StringComparison.OrdinalIgnoreCase))
                    continue;
                Console.WriteLine($"关闭游戏：{actual}（PID {process.Id}）");
                if (!process.CloseMainWindow() || !process.WaitForExit(5000))
                {
                    if (!process.HasExited)
                        process.Kill();
                    if (!process.WaitForExit(10000))
                        throw new TimeoutException($"游戏进程 {process.Id} 未在期限内退出，停止更新。");
                }
                stopped++;
            }
        }
        Console.WriteLine(stopped == 0 ? "游戏未运行，无需关闭。" : $"游戏已关闭：{stopped} 个进程。");
    }
}
