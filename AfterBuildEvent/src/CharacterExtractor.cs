using System.ComponentModel;
using System.Diagnostics;

namespace AfterBuildEvent;

/// <summary>独立的离线立绘提取入口；管理资源脚本的参数、取消和执行期限。</summary>
internal static class CharacterExtractor
{
    /// <summary>解析提取选项，读取游戏路径，并运行随工具分发的 Unity 资源解码脚本。</summary>
    /// <param name="args">extract-characters 之后的参数；支持 --output 输出目录和 --help。</param>
    /// <returns>成功返回零；资源脚本的非零退出码原样返回。</returns>
    internal static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 1 && args[0] == "--help")
        {
            Console.WriteLine("用法：AfterBuildEvent.exe extract-characters [--output <目录>]");
            Console.WriteLine("从 DefaultPath.props 的 GameDir 提取全部角色的默认完整立绘，默认输出至仓库 CharacterPortraits。");
            Console.WriteLine("需要 PATH 中的 uv；首次运行将由 uv 准备 Python 3.12、UnityPy 和 Pillow。");
            return 0;
        }
        if (args.Length != 0 && (args.Length != 2 || args[0] != "--output" || string.IsNullOrWhiteSpace(args[1])))
            throw new ArgumentException("立绘提取参数无效。用法：extract-characters [--output <目录>]");

        var (root, game) = RepositoryContext.LoadGamePaths();
        string output = args.Length == 0 ? Path.Combine(root, "CharacterPortraits") : Path.GetFullPath(args[1]);
        string script = Path.Combine(AppContext.BaseDirectory, "scripts", "extract_characters.py");
        if (!File.Exists(script))
            throw new FileNotFoundException("缺少立绘提取脚本，请重新构建 AfterBuildEvent。", script);

        var start = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "uv.exe" : "uv",
            UseShellExecute = false,
            WorkingDirectory = root,
        };
        foreach (string argument in new[] { "run", "--no-project", "--python", "3.12", "--script", script,
                     "--game-dir", game, "--output", output })
            start.ArgumentList.Add(argument);
        start.Environment["PYTHONUTF8"] = "1";
        Console.WriteLine($"提取角色默认立绘：{game}");
        Console.WriteLine($"输出目录：{output}");

        using var process = new Process { StartInfo = start };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        ConsoleCancelEventHandler cancel = (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancel;
        try
        {
            try
            {
                if (!process.Start())
                    throw new InvalidOperationException("无法启动立绘提取进程。");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 3)
            {
                throw new InvalidOperationException("找不到 uv。请安装 uv 并加入 PATH，例如：winget install --id astral-sh.uv --exact", ex);
            }

            try
            {
                await process.WaitForExitAsync(cancellation.Token);
                if (process.ExitCode != 0)
                    Console.Error.WriteLine($"立绘提取失败，退出码 {process.ExitCode}；已完成的角色图片保留，可根据上方错误修正后重新执行。");
                return process.ExitCode;
            }
            catch (OperationCanceledException)
            {
                throw new OperationCanceledException("立绘提取已取消或超过 10 分钟执行期限；已完成的角色图片保留。");
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    if (!process.WaitForExit(5000))
                        throw new TimeoutException("立绘提取子进程未能在 5 秒内退出。");
                }
            }
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }
}
