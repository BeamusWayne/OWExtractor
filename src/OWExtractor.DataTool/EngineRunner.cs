using System.Diagnostics;
using System.Text.RegularExpressions;

namespace OWExtractor.DataTool;

public interface IExecutionGate { void EnsureAllowed(); }

public sealed class LiveAccessGate(Func<bool> isEnabled, Func<bool>? isGameRunning = null) : IExecutionGate
{
    public void EnsureAllowed()
    {
        if (!isEnabled()) throw new InvalidOperationException("当前为离线模式：只读取既有结果，不启动 DataTool。");
        try
        {
            if ((isGameRunning ?? DetectGame)()) throw new InvalidOperationException("检测到守望先锋正在运行，已阻止 DataTool 启动。请退出游戏后再提取。");
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex) { throw new InvalidOperationException("无法确认游戏是否已退出，已阻止引擎启动。", ex); }
    }

    private static bool DetectGame()
    {
        // Only enumerate process names. Never open handles to, suspend, or terminate the game.
        foreach (var name in new[] { "Overwatch", "Overwatch Launcher" })
        {
            var processes = Process.GetProcessesByName(name);
            try { if (processes.Length > 0) return true; }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        return false;
    }
}

public sealed record EngineResult(int ExitCode, bool Cancelled, IReadOnlyList<string> Tail);

public sealed partial class EngineRunner(IExecutionGate gate)
{
    public static int StartedProcessCount { get; private set; }
    public async Task<EngineResult> RunAsync(EngineCommand command, Action<string>? onLine, CancellationToken token)
    {
        gate.EnsureAllowed();
        token.ThrowIfCancellationRequested();
        if (!File.Exists(command.Executable)) throw new FileNotFoundException("未找到 DataTool.exe。", command.Executable);
        using var process = new Process { StartInfo = command.CreateStartInfo() };
        var tail = new Queue<string>();
        var sync = new object();
        void Accept(string raw)
        {
            var line = Ansi().Replace(raw, "");
            if (line.Length > 4096) line = line[..4096];
            lock (sync) { tail.Enqueue(line); while (tail.Count > 300) tail.Dequeue(); }
            onLine?.Invoke(line);
        }
        gate.EnsureAllowed();
        process.Start();
        StartedProcessCount++;
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (System.ComponentModel.Win32Exception) { }
        using var registration = token.Register(() => {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        });
        async Task Drain(StreamReader reader)
        {
            while (await reader.ReadLineAsync() is { } line) Accept(line);
        }
        var stdout = Drain(process.StandardOutput); var stderr = Drain(process.StandardError);
        await process.WaitForExitAsync(CancellationToken.None);
        await Task.WhenAll(stdout, stderr);
        return new(process.ExitCode, token.IsCancellationRequested, tail.ToArray());
    }

    public static string Explain(EngineResult result)
    {
        var text = string.Join('\n', result.Tail);
        if (result.Cancelled) return "任务已取消，已生成的文件保留在任务目录。";
        if (text.Contains("does not support", StringComparison.OrdinalIgnoreCase) || text.Contains("UnsupportedBuild", StringComparison.OrdinalIgnoreCase)) return "DataTool 与当前游戏版本不兼容，请匹配引擎版本。";
        if (text.Contains("encrypted", StringComparison.OrdinalIgnoreCase) || text.Contains("Missing key", StringComparison.OrdinalIgnoreCase)) return "部分资源无法解密，请查看上游版本与日志。";
        if (text.Contains("Error initializing CASC", StringComparison.OrdinalIgnoreCase)) return "无法初始化游戏存储，请检查目录、版本和资源语言。";
        return $"DataTool 退出码：{result.ExitCode}。请查看日志和输出文件。";
    }

    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]")]
    private static partial Regex Ansi();
}
