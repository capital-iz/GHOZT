using System.Diagnostics;

namespace Ghozt.Core;

public sealed record ProcessOutput(DateTimeOffset Timestamp, bool IsError, string Text);

public sealed class TargetSupervisor : IDisposable
{
    private Process? _process;
    private readonly List<ProcessOutput> _outputLog = [];
    private readonly object _lock = new();

    public int? ProcessId => _process?.Id;
    public IntPtr MainWindowHandle => _process?.MainWindowHandle ?? IntPtr.Zero;
    public bool IsRunning => _process is { HasExited: false };
    public bool HasExited => _process is null or { HasExited: true };
    public bool IsResponding => _process is not null && !_process.HasExited && _process.Responding;
    public int? ExitCode => _process is { HasExited: true } ? _process.ExitCode : null;
    public IReadOnlyList<ProcessOutput> OutputLog { get { lock (_lock) { return _outputLog.ToArray(); } } }
    public string StandardOutput { get { lock (_lock) { return string.Join("\n", _outputLog.Where(o => !o.IsError).Select(o => o.Text)); } } }
    public string StandardError { get { lock (_lock) { return string.Join("\n", _outputLog.Where(o => o.IsError).Select(o => o.Text)); } } }

    public event Action<ProcessOutput>? OutputReceived;
    public event Action<int>? Exited;

    public async Task<bool> LaunchAsync(string executablePath, string arguments = "", string workingDirectory = "", CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("Executable path must not be empty.", nameof(executablePath));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) 
                ? (Path.GetDirectoryName(executablePath) ?? "") 
                : workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = false,
        };

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            var outItem = new ProcessOutput(DateTimeOffset.Now, false, e.Data);
            lock (_lock) { _outputLog.Add(outItem); }
            OutputReceived?.Invoke(outItem);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            var errItem = new ProcessOutput(DateTimeOffset.Now, true, e.Data);
            lock (_lock) { _outputLog.Add(errItem); }
            OutputReceived?.Invoke(errItem);
        };

        process.Exited += (_, _) =>
        {
            Exited?.Invoke(process.ExitCode);
        };

        if (!process.Start())
        {
            return false;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _process = process;

        // Wait up to 5 seconds for main window handle to appear if GUI
        var timeout = DateTimeOffset.Now.AddSeconds(5);
        while (DateTimeOffset.Now < timeout && !cancellationToken.IsCancellationRequested)
        {
            if (process.HasExited) break;
            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero) break;
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    public async Task<IntPtr> WaitForMainWindowAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (_process is null || _process.HasExited) return IntPtr.Zero;
        var deadline = DateTimeOffset.Now + timeout;
        while (DateTimeOffset.Now < deadline && !cancellationToken.IsCancellationRequested)
        {
            if (_process.HasExited) break;
            _process.Refresh();
            if (_process.MainWindowHandle != IntPtr.Zero) return _process.MainWindowHandle;
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
        return _process?.MainWindowHandle ?? IntPtr.Zero;
    }

    public async Task TerminateAsync(TimeSpan timeout) => await StopAsync((int)timeout.TotalMilliseconds).ConfigureAwait(false);

    public async Task StopAsync(int timeoutMs = 5000)
    {
        if (_process is null || _process.HasExited) return;

        try
        {
            if (_process.MainWindowHandle != IntPtr.Zero)
            {
                _process.CloseMainWindow();
                var waitTask = Task.Run(() => _process.WaitForExit(timeoutMs));
                if (!await waitTask.ConfigureAwait(false))
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            else
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // Process may already be dead
        }
    }

    public void Dispose()
    {
        _process?.Dispose();
        _process = null;
    }
}
