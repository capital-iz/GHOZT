using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ghozt.Core;

public sealed record BlackBoxStep
{
    public required int StepIndex { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string StateId { get; init; }
    public required string ActionDescription { get; init; }
    public UiActionIntent? ActionIntent { get; init; }
    public UiActionResult? ActionResult { get; init; }
    public UiElementSnapshot? UiTree { get; init; }
    public GhoztId? ScreenshotEvidenceId { get; init; }
    public GhoztId? UiTreeEvidenceId { get; init; }
    public byte[]? ScreenshotBytes { get; init; }
    public string ScreenshotBase64 { get; init; } = "";
    public IReadOnlyList<OracleResult> OracleResults { get; init; } = [];
}

/// <summary>
/// Autonomous Black-Box Recorder that captures window screenshots, UI tree snapshots,
/// and forensic event trails with cryptographic SHA-256 evidence seals.
/// </summary>
public sealed class BlackBoxRecorder
{
    private readonly GhoztStore _store;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public BlackBoxRecorder(GhoztStore store)
    {
        _store = store;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    /// <summary>
    /// Captures a physical window screenshot via Win32 PrintWindow / GDI into a PNG byte array.
    /// </summary>
    public static byte[] CaptureWindowPng(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return GenerateDiagnosticFrame("No window handle available");
        }

        try
        {
            if (!GetWindowRect(windowHandle, out var rect))
            {
                return GenerateDiagnosticFrame("Failed to query window rectangle");
            }

            var width = Math.Max(1, rect.Right - rect.Left);
            var height = Math.Max(1, rect.Bottom - rect.Top);

            var hdcSrc = GetWindowDC(windowHandle);
            if (hdcSrc == IntPtr.Zero)
            {
                return GenerateDiagnosticFrame("Failed to get window DC");
            }

            var hdcDest = CreateCompatibleDC(hdcSrc);
            var hBitmap = CreateCompatibleBitmap(hdcSrc, width, height);
            var hOld = SelectObject(hdcDest, hBitmap);

            try
            {
                var printed = PrintWindow(windowHandle, hdcDest, PW_RENDERFULLCONTENT);
                if (!printed)
                {
                    // Fallback without PW_RENDERFULLCONTENT
                    PrintWindow(windowHandle, hdcDest, 0);
                }

                var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                bitmapSource.Freeze();

                using var ms = new MemoryStream();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                encoder.Save(ms);
                return ms.ToArray();
            }
            finally
            {
                SelectObject(hdcDest, hOld);
                DeleteObject(hBitmap);
                DeleteDC(hdcDest);
                _ = ReleaseDC(windowHandle, hdcSrc);
            }
        }
        catch (Exception ex)
        {
            return GenerateDiagnosticFrame($"Capture error: {ex.Message}");
        }
    }

    /// <summary>
    /// Generates a synthetic diagnostic frame when physical window capture cannot be performed.
    /// </summary>
    public static byte[] GenerateDiagnosticFrame(string reason)
    {
        const int width = 320;
        const int height = 180;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));
            
            var text = new FormattedText(
                $"GHOZT BLACK-BOX RECORDER\n{reason}\n{DateTimeOffset.Now:O}",
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                11,
                Brushes.DarkGray,
                1.0);

            dc.DrawText(text, new Point(12, 12));
        }

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();

        using var ms = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        encoder.Save(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Records a black-box step, saving screenshot and UI tree artefacts with SHA-256 evidence seals.
    /// </summary>
    public BlackBoxStep RecordStep(
        int stepIndex,
        Target target,
        Sweep sweep,
        string stateId,
        string actionDescription,
        IntPtr windowHandle,
        UiElementSnapshot? uiTree,
        UiActionIntent? actionIntent = null,
        UiActionResult? actionResult = null,
        IReadOnlyList<OracleResult>? oracleResults = null)
    {
        var timestamp = DateTimeOffset.Now;
        var oracles = oracleResults ?? [];

        // 1. Capture and Seal Screenshot Evidence
        byte[] pngBytes;
        if (windowHandle != IntPtr.Zero)
        {
            pngBytes = CaptureWindowPng(windowHandle);
        }
        else
        {
            pngBytes = GenerateDiagnosticFrame("Target has no active window handle");
        }

        var base64Screenshot = Convert.ToBase64String(pngBytes);
        var screenshotSha = Hashing.Sha256(pngBytes);
        var screenshotEvidenceId = _store.NextId(IdKind.Evidence);

        var screenshotEvidence = new Evidence
        {
            Id = screenshotEvidenceId,
            TargetId = target.Id,
            SweepId = sweep.Id,
            Kind = "ui:screenshot/png",
            Summary = $"Step {stepIndex:D3}: {actionDescription}",
            Sha256 = screenshotSha,
            SizeBytes = pngBytes.Length,
            Payload = base64Screenshot,
            SourceAdapter = "BlackBoxRecorder",
            CapturedAt = timestamp,
            Sealed = true,
            SealedBy = "Ghozt.AutonomousEngine",
        };
        _store.Save(screenshotEvidence);

        // 2. Capture and Seal UI Tree Evidence
        GhoztId? uiTreeEvidenceId = null;
        if (uiTree is not null)
        {
            var json = JsonSerializer.Serialize(uiTree, JsonOptions);
            var treeSha = Hashing.Sha256(json);
            uiTreeEvidenceId = _store.NextId(IdKind.Evidence);

            var treeEvidence = new Evidence
            {
                Id = uiTreeEvidenceId.Value,
                TargetId = target.Id,
                SweepId = sweep.Id,
                Kind = "ui:snapshot/json",
                Summary = $"Step {stepIndex:D3} UI tree: {uiTree.Name} ({uiTree.ControlType})",
                Sha256 = treeSha,
                SizeBytes = Encoding.UTF8.GetByteCount(json),
                Payload = json,
                SourceAdapter = "BlackBoxRecorder",
                CapturedAt = timestamp,
                Sealed = true,
                SealedBy = "Ghozt.AutonomousEngine",
            };
            _store.Save(treeEvidence);
        }

        // 3. Append Ledger Event
        var eventSeverity = oracles.Any(o => !o.Passed && o.Severity == Severity.Critical)
            ? Severity.Critical
            : oracles.Any(o => !o.Passed && o.Severity == Severity.High)
                ? Severity.High
                : oracles.Any(o => !o.Passed)
                    ? Severity.Medium
                    : Severity.Info;

        var eventCode = actionIntent is not null ? "UI.ACTION" : "UI.STATE";

        _store.Append(new LedgerEvent
        {
            Id = _store.NextId(IdKind.Event),
            At = timestamp,
            Code = eventCode,
            Message = $"Step {stepIndex:D3}: {actionDescription}",
            Severity = eventSeverity,
            TargetId = target.Id,
            SweepId = sweep.Id,
            RelatedId = screenshotEvidenceId.ToString(),
            Actor = "BlackBoxRecorder",
            Detail = $"State: {stateId}\nAction: {actionDescription}\nOracles evaluated: {oracles.Count}\n" +
                     $"Deviations: {oracles.Count(o => !o.Passed)}\nScreenshot: {screenshotSha[..12]}",
        });

        return new BlackBoxStep
        {
            StepIndex = stepIndex,
            Timestamp = timestamp,
            StateId = stateId,
            ActionDescription = actionDescription,
            ActionIntent = actionIntent,
            ActionResult = actionResult,
            UiTree = uiTree,
            ScreenshotEvidenceId = screenshotEvidenceId,
            UiTreeEvidenceId = uiTreeEvidenceId,
            ScreenshotBytes = pngBytes,
            ScreenshotBase64 = base64Screenshot,
            OracleResults = oracles,
        };
    }
}
