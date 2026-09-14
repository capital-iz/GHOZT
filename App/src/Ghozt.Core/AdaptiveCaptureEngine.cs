using System.Text;
using System.Text.Json;

namespace Ghozt.Core;

/// <summary>
/// Operational capture fidelity modes (Patent §23).
/// </summary>
public enum CaptureMode
{
    /// <summary>Baseline mode: captures low-cost structural telemetry and material events.</summary>
    Baseline,

    /// <summary>Anomaly mode: increases sampling rate around anomalous behavior or warnings.</summary>
    Anomaly,

    /// <summary>Forensic mode: full fidelity; serializes rolling ring buffers, stacks, and payloads.</summary>
    Forensic,

    /// <summary>Sensitive mode: redacts credentials, PII and keys while proving redaction occurred.</summary>
    Sensitive,

    /// <summary>Performance mode: focuses on execution durations, GC telemetry, and instrumentation overhead.</summary>
    Performance,

    /// <summary>Storage pressure mode: compresses traces and degrades optional media under disk limits.</summary>
    StoragePressure,
}

/// <summary>
/// A time-correlated execution telemetry frame captured in the pre-failure ring buffer.
/// </summary>
public sealed record ExecutionTelemetryFrame
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string EventCode { get; init; }
    public required string Actor { get; init; }
    public required string Summary { get; init; }
    public string Payload { get; init; } = "";
    public Severity Severity { get; init; } = Severity.Info;
    public bool IsTriggerEvent { get; init; }
}

/// <summary>
/// High-performance circular buffer for rolling pre-failure execution forensics.
/// Holds the last N frames in FIFO order, overwriting oldest on capacity saturation.
/// </summary>
public sealed class PreFailureRingBuffer
{
    private readonly ExecutionTelemetryFrame[] _buffer;
    private readonly object _gate = new();
    private int _head;
    private int _count;

    public PreFailureRingBuffer(int capacity = 50)
    {
        if (capacity < 5) capacity = 5;
        _buffer = new ExecutionTelemetryFrame[capacity];
    }

    public int Capacity => _buffer.Length;
    public int Count { get { lock (_gate) { return _count; } } }

    /// <summary>
    /// Appends a new telemetry frame, rotating out the oldest when capacity is reached.
    /// </summary>
    public void Append(ExecutionTelemetryFrame frame)
    {
        lock (_gate)
        {
            var index = (_head + _count) % _buffer.Length;
            if (_count == _buffer.Length)
            {
                // Overwriting oldest: move head forward
                _buffer[_head] = frame;
                _head = (_head + 1) % _buffer.Length;
            }
            else
            {
                _buffer[index] = frame;
                _count++;
            }
        }
    }

    /// <summary>
    /// Returns a chronological snapshot of the currently buffered telemetry frames.
    /// </summary>
    public IReadOnlyList<ExecutionTelemetryFrame> Snapshot()
    {
        lock (_gate)
        {
            var list = new List<ExecutionTelemetryFrame>(_count);
            for (var i = 0; i < _count; i++)
            {
                var idx = (_head + i) % _buffer.Length;
                list.Add(_buffer[idx]);
            }
            return list;
        }
    }

    /// <summary>
    /// Clears all buffered frames.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _head = 0;
            _count = 0;
            Array.Clear(_buffer, 0, _buffer.Length);
        }
    }
}

/// <summary>
/// Adaptive Capture Engine (Patent §23).
/// Coordinates multi-mode telemetry capture, pre-failure ring buffering,
/// and forensic evidence plane sealing upon deviation.
/// </summary>
public sealed class AdaptiveCaptureEngine
{
    private readonly GhoztStore _store;
    private readonly PreFailureRingBuffer _ringBuffer;
    private CaptureMode _currentMode = CaptureMode.Baseline;
    private readonly object _modeGate = new();

    public AdaptiveCaptureEngine(GhoztStore store, int ringBufferCapacity = 50)
    {
        _store = store;
        _ringBuffer = new PreFailureRingBuffer(ringBufferCapacity);
    }

    public CaptureMode CurrentMode
    {
        get { lock (_modeGate) { return _currentMode; } }
        set { lock (_modeGate) { _currentMode = value; } }
    }

    public PreFailureRingBuffer RingBuffer => _ringBuffer;

    /// <summary>
    /// Records a material execution event into the pre-failure ring buffer.
    /// Automatically escalates capture mode if an anomaly or fault is detected.
    /// </summary>
    public void RecordTelemetry(
        string eventCode,
        string actor,
        string summary,
        string payload = "",
        Severity severity = Severity.Info)
    {
        var frame = new ExecutionTelemetryFrame
        {
            Timestamp = DateTimeOffset.Now,
            EventCode = eventCode,
            Actor = actor,
            Summary = summary,
            Payload = payload,
            Severity = severity,
            IsTriggerEvent = severity >= Severity.High,
        };

        _ringBuffer.Append(frame);

        // Auto-escalation policy (Patent §23)
        lock (_modeGate)
        {
            if (severity >= Severity.Critical && _currentMode < CaptureMode.Forensic)
            {
                _currentMode = CaptureMode.Forensic;
            }
            else if (severity >= Severity.Medium && _currentMode == CaptureMode.Baseline)
            {
                _currentMode = CaptureMode.Anomaly;
            }
        }
    }

    /// <summary>
    /// Extracts the pre-failure ring buffer, applies active capture mode transformations
    /// (e.g. Redaction in Sensitive mode, compression in StoragePressure mode),
    /// generates SHA-256 integrity hash, and seals the package into the Evidence store.
    /// </summary>
    public Evidence SealForensicPackage(
        Target target,
        string triggerReason,
        GhoztId? findingId = null,
        GhoztId? sweepId = null)
    {
        var frames = _ringBuffer.Snapshot();
        var mode = CurrentMode;

        // Transform frames according to capture mode
        var processedFrames = new List<ExecutionTelemetryFrame>(frames.Count);
        foreach (var f in frames)
        {
            var p = f.Payload;

            if (mode == CaptureMode.Sensitive && !string.IsNullOrWhiteSpace(p))
            {
                p = RedactionEngine.Redact(p);
            }

            if (mode == CaptureMode.StoragePressure && !string.IsNullOrWhiteSpace(p) && p.Length > 512)
            {
                // Degrade payload under storage pressure: truncate and note omitted payload bytes
                p = p[..512] + $"... [OMITTED_{p.Length - 512}_BYTES_UNDER_STORAGE_PRESSURE]";
            }

            processedFrames.Add(f with { Payload = p });
        }

        var forensicDossier = new
        {
            TargetId = target.Id.ToString(),
            TargetName = target.Name,
            CapturedAt = DateTimeOffset.Now,
            TriggerReason = triggerReason,
            CaptureMode = mode.ToString(),
            FrameCount = processedFrames.Count,
            PreFailureSequence = processedFrames,
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = mode != CaptureMode.StoragePressure, // Minify JSON under storage pressure
        };

        var jsonPayload = JsonSerializer.Serialize(forensicDossier, options);
        var sha256 = Hashing.Sha256(jsonPayload);
        var sizeBytes = Encoding.UTF8.GetByteCount(jsonPayload);

        var evidenceId = _store.NextId(IdKind.Evidence);
        var evidence = new Evidence
        {
            Id = evidenceId,
            TargetId = target.Id,
            Kind = "forensic-ring-buffer",
            Summary = $"Pre-failure ring buffer ({processedFrames.Count} frames, {mode} mode): {triggerReason}",
            Sha256 = sha256,
            SizeBytes = sizeBytes,
            Payload = jsonPayload,
            SourceAdapter = "AdaptiveCaptureEngine",
            FindingId = findingId,
            SweepId = sweepId,
            CapturedAt = DateTimeOffset.Now,
            Sealed = true,
            SealedBy = "AdaptiveCaptureEngine:AutoSeal",
            Redacted = mode == CaptureMode.Sensitive,
        };

        _store.Save(evidence);

        // Record BlackBox Event
        _store.Append(new LedgerEvent
        {
            Id = _store.NextId(IdKind.Event),
            At = DateTimeOffset.Now,
            Code = "SYS.CAPTURE.RINGBUFFER_SEALED",
            Message = $"Sealed pre-failure forensic package ({processedFrames.Count} frames, mode: {mode}, hash: {sha256[..8]}...)",
            Severity = Severity.Info,
            TargetId = target.Id,
            SweepId = sweepId,
            RelatedId = evidenceId.ToString(),
            Actor = "AdaptiveCaptureEngine",
            Detail = JsonSerializer.Serialize(new
            {
                EvidenceId = evidenceId.ToString(),
                TriggerReason = triggerReason,
                Mode = mode.ToString(),
                FrameCount = processedFrames.Count,
                Sha256 = sha256,
                SizeBytes = sizeBytes,
            }),
        });

        return evidence;
    }
}
