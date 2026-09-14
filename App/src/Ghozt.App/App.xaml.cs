using System.IO;
using System.Windows;
using System.Windows.Threading;
using Ghozt.App.Shell;
using Ghozt.App.Theming;
using Ghozt.Core;

namespace Ghozt.App;

public partial class App : Application
{
    private GhoztStore? _store;

    /// <summary>
    /// True while a capture run is in progress.
    /// </summary>
    /// <remarks>
    /// A capture runs unattended. The interactive fault handler puts up a modal box and waits for
    /// someone to click OK, which during a capture means the process hangs forever with no output
    /// and no clue why. In capture mode the fault is written to the ledger and to stderr instead,
    /// and the run is failed rather than blocked.
    /// </remarks>
    private bool _capturing;

    /// <summary>The evidence plane, opened once and shared by every workspace.</summary>
    public static GhoztStore Store { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A styling or storage fault must never take the shell down. The handler records what
        // happened and keeps the window alive, because a control that is the wrong shade is a
        // blemish while a screen that will not open is a broken product.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var args = Environment.GetCommandLineArgs();

        // Autonomous Headless CLI Execution (Patent §38)
        if (args.Contains("--headless") || args.Contains("--sweep") || (args.Contains("--self-test") && !args.Contains("--capture")))
        {
            var options = HeadlessRunner.ParseArgs(args);
            var dbPath = !string.IsNullOrWhiteSpace(options.DatabasePath)
                ? options.DatabasePath
                : GhoztStore.DefaultDatabasePath;

            _store = new GhoztStore(dbPath);
            Store = _store;

            var runner = new HeadlessRunner(_store);
            var exitCode = runner.RunAsync(options).GetAwaiter().GetResult();
            _store.Dispose();
            Environment.Exit(exitCode);
            return;
        }

        _store = new GhoztStore(GhoztStore.DefaultDatabasePath);
        Store = _store;

        var theme = new ThemeManager(_store);
        ThemeManager.Initialise(theme);
        Resources.MergedDictionaries.Add(theme.Dictionary);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        // Visual-regression capture, invoked as: Ghozt.exe --capture <directory> [--all-palettes]
        //
        // A product feature rather than a test scaffold: GZT-UX-001 §20 requires reference
        // screenshots across workspaces, modes and palettes, and this is the surface that produces
        // them from the live shell rather than from an approximation of it.
        // Read from the process command line rather than StartupEventArgs.
        //
        // WPF's generated entry point calls Run() without forwarding the command line, so e.Args is
        // empty in a project that lets the SDK write Main - which is every WPF app that does not
        // hand-roll one. Environment.GetCommandLineArgs() is populated either way, so the capture
        // switch works whether or not that generated entry point ever changes.
        var capture = Array.IndexOf(args, "--capture");
        if (capture >= 0 && capture + 1 < args.Length)
        {
            _capturing = true;

            var workspaceIndex = Array.IndexOf(args, "--workspace");
            RunCapture(
                window,
                args[capture + 1],
                args.Contains("--all-palettes"),
                workspaceIndex >= 0 && workspaceIndex + 1 < args.Length ? args[workspaceIndex + 1] : null);
        }
    }

    /// <summary>
    /// Appends a line to the capture log.
    /// </summary>
    /// <remarks>
    /// A capture runs with no console and no operator. Without this, a failure inside it produces a
    /// process that exits silently having written nothing - which is how a broken capture looks
    /// exactly like a working one that found nothing to do.
    /// </remarks>
    private static void Trace(string message)
    {
        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "ghozt-cap-trace.txt"),
                $"{DateTimeOffset.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }

    private void RunCapture(MainWindow window, string directory, bool allPalettes, string? workspace)
    {
        Trace($"RunCapture entered dir={directory} all={allPalettes}");
        window.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            () =>
            {
                Trace("dispatcher callback running");
                try
                {
                    if (workspace is not null
                        && Enum.TryParse<Shell.WorkspaceKind>(workspace, true, out var kind))
                    {
                        window.ShowWorkspaceForCapture(kind);
                    }

                    if (allPalettes)
                    {
                        var files = Shell.VisualCapture.CaptureCatalogue(window, directory);
                        Console.WriteLine($"captured {files.Count} images to {directory}");
                    }
                    else
                    {
                        var name = Path.Combine(directory,
                            $"{Theming.ThemeManager.Current.Tokens.Digest().Replace('/', '_')}.png");
                        Trace($"saving {name} size={window.ActualWidth}x{window.ActualHeight}");
                        Shell.VisualCapture.Save(window, name);
                        Trace("saved");
                    }
                }
#pragma warning disable CA1031 // A capture run must report ANY failure rather than hang or vanish.
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"capture failed: {ex}");
                    File.WriteAllText(Path.Combine(directory, "capture-error.txt"), ex.ToString());
                }
#pragma warning restore CA1031
                finally
                {
                    Shutdown();
                }
            });
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Trace("UNHANDLED: " + e.Exception);

        // Recorded to the ledger where possible, so an operator can see that something failed rather
        // than discovering a silently missing panel later.
        try
        {
            _store?.Append(new LedgerEvent
            {
                Id = _store.NextId(IdKind.Event),
                At = DateTimeOffset.Now,
                Code = "APP.FAULT",
                Message = "Ghozt recovered from an unexpected error.",
                Severity = Severity.High,
                Actor = "shell",
                Detail = e.Exception.ToString(),
            });
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException or ObjectDisposedException)
        {
            // If the ledger itself is the thing that failed, there is nowhere left to record it.
        }

        if (_capturing)
        {
            Console.Error.WriteLine($"unhandled during capture: {e.Exception}");
            e.Handled = true;
            Shutdown(1);
            return;
        }

        MessageBox.Show(
            "Ghozt hit an unexpected error and recovered. The details were written to the event ledger.",
            "Ghozt",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _store?.Dispose();
        base.OnExit(e);
    }
}
