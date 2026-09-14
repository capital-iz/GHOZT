namespace Ghozt.Core;

/// <summary>
/// A time-series telemetry sample of target system resources.
/// </summary>
public sealed record ResourceSample
{
    public required DateTimeOffset Timestamp { get; init; }
    public required double WorkingSetMB { get; init; }
    public required double PrivateBytesMB { get; init; }
    public int ThreadCount { get; init; }
    public int HandleCount { get; init; }
}

/// <summary>
/// Linear regression slope and determination metrics for a resource parameter.
/// </summary>
public sealed record ResourceDriftMetrics
{
    public required string MetricName { get; init; }
    public required double SlopePerSecond { get; init; }
    public required double RSquared { get; init; }
    public required double ProjectedGrowthPerHour { get; init; }
    public required bool IsLeaking { get; init; }
    public required string Diagnostic { get; init; }
}

/// <summary>
/// Report synthesized by the Soak &amp; Resilience Analysis Engine.
/// </summary>
public sealed record SoakAnalysisReport
{
    public required GhoztId TargetId { get; init; }
    public required TimeSpan Duration { get; init; }
    public required int SampleCount { get; init; }
    public required ResourceDriftMetrics WorkingSetDrift { get; init; }
    public required ResourceDriftMetrics PrivateBytesDrift { get; init; }
    public required ResourceDriftMetrics HandleDrift { get; init; }
    public required ResourceDriftMetrics ThreadDrift { get; init; }
    public bool HasCriticalDrift => WorkingSetDrift.IsLeaking || PrivateBytesDrift.IsLeaking || HandleDrift.IsLeaking || ThreadDrift.IsLeaking;
    public Finding? GeneratedFinding { get; init; }
}

/// <summary>
/// Soak and Resilience Performance Drift Engine (Patent §21).
/// Performs ordinary least-squares linear regression over resource time series to detect
/// deterministic memory growth, handle leaks, and thread accumulation.
/// </summary>
public static class SoakAnalysisEngine
{
    public const double MemoryLeakSlopeThresholdMBPerSec = 0.05; // 180 MB / hr
    public const double HandleLeakSlopeThresholdPerSec = 0.10;   // 360 handles / hr
    public const double ThreadLeakSlopeThresholdPerSec = 0.02;   // 72 threads / hr
    public const double RSquaredConfidenceThreshold = 0.85;

    /// <summary>
    /// Computes linear regression metrics (slope, R², projected growth) for a specific resource stream.
    /// </summary>
    public static ResourceDriftMetrics CalculateDrift(
        IReadOnlyList<ResourceSample> samples,
        Func<ResourceSample, double> selector,
        string metricName,
        double leakSlopeThreshold,
        double rSquaredThreshold = RSquaredConfidenceThreshold)
    {
        if (samples.Count < 2)
        {
            return new ResourceDriftMetrics
            {
                MetricName = metricName,
                SlopePerSecond = 0.0,
                RSquared = 0.0,
                ProjectedGrowthPerHour = 0.0,
                IsLeaking = false,
                Diagnostic = "Insufficient sample count (N < 2) to compute linear drift.",
            };
        }

        var t0 = samples[0].Timestamp;
        var n = samples.Count;

        var tValues = new double[n];
        var yValues = new double[n];

        double sumT = 0;
        double sumY = 0;

        for (var i = 0; i < n; i++)
        {
            var t = (samples[i].Timestamp - t0).TotalSeconds;
            var y = selector(samples[i]);
            tValues[i] = t;
            yValues[i] = y;
            sumT += t;
            sumY += y;
        }

        var meanT = sumT / n;
        var meanY = sumY / n;

        double ssTt = 0;
        double ssYy = 0;
        double ssTy = 0;

        for (var i = 0; i < n; i++)
        {
            var dt = tValues[i] - meanT;
            var dy = yValues[i] - meanY;
            ssTt += dt * dt;
            ssYy += dy * dy;
            ssTy += dt * dy;
        }

        if (ssTt <= 1e-9)
        {
            return new ResourceDriftMetrics
            {
                MetricName = metricName,
                SlopePerSecond = 0.0,
                RSquared = 0.0,
                ProjectedGrowthPerHour = 0.0,
                IsLeaking = false,
                Diagnostic = "Zero time variance across telemetry samples.",
            };
        }

        var slope = ssTy / ssTt;
        double rSquared = 0.0;

        if (ssYy > 1e-9)
        {
            rSquared = Math.Clamp((ssTy * ssTy) / (ssTt * ssYy), 0.0, 1.0);
        }

        var projectedGrowthPerHour = slope * 3600.0;
        var isLeaking = slope >= leakSlopeThreshold && rSquared >= rSquaredThreshold;

        var diagnostic = isLeaking
            ? $"Deterministic leak detected: slope={slope:F4}/s ({projectedGrowthPerHour:F2}/hr), R²={rSquared:F3} >= {rSquaredThreshold:F2}"
            : $"Nominal stability: slope={slope:F4}/s ({projectedGrowthPerHour:F2}/hr), R²={rSquared:F3}";

        return new ResourceDriftMetrics
        {
            MetricName = metricName,
            SlopePerSecond = slope,
            RSquared = rSquared,
            ProjectedGrowthPerHour = projectedGrowthPerHour,
            IsLeaking = isLeaking,
            Diagnostic = diagnostic,
        };
    }

    /// <summary>
    /// Analyzes a time-series of resource samples, evaluating memory, handles, and threads for drift.
    /// When critical drift is detected and a GhoztStore is provided, automatically records a Phantom finding.
    /// </summary>
    public static SoakAnalysisReport Analyze(
        Target target,
        IReadOnlyList<ResourceSample> samples,
        GhoztStore? store = null,
        string actor = "SoakAnalysisEngine")
    {
        var duration = samples.Count >= 2
            ? samples[^1].Timestamp - samples[0].Timestamp
            : TimeSpan.Zero;

        var wsDrift = CalculateDrift(samples, s => s.WorkingSetMB, "WorkingSetMB", MemoryLeakSlopeThresholdMBPerSec);
        var pbDrift = CalculateDrift(samples, s => s.PrivateBytesMB, "PrivateBytesMB", MemoryLeakSlopeThresholdMBPerSec);
        var handleDrift = CalculateDrift(samples, s => s.HandleCount, "HandleCount", HandleLeakSlopeThresholdPerSec);
        var threadDrift = CalculateDrift(samples, s => s.ThreadCount, "ThreadCount", ThreadLeakSlopeThresholdPerSec);

        Finding? finding = null;
        var hasCriticalDrift = wsDrift.IsLeaking || pbDrift.IsLeaking || handleDrift.IsLeaking || threadDrift.IsLeaking;

        if (hasCriticalDrift && store is not null)
        {
            var leakingMetrics = new List<string>();
            if (wsDrift.IsLeaking) leakingMetrics.Add($"WorkingSet (+{wsDrift.ProjectedGrowthPerHour:F1} MB/hr, R²={wsDrift.RSquared:F2})");
            if (pbDrift.IsLeaking) leakingMetrics.Add($"PrivateBytes (+{pbDrift.ProjectedGrowthPerHour:F1} MB/hr, R²={pbDrift.RSquared:F2})");
            if (handleDrift.IsLeaking) leakingMetrics.Add($"Handles (+{handleDrift.ProjectedGrowthPerHour:F1} handles/hr, R²={handleDrift.RSquared:F2})");
            if (threadDrift.IsLeaking) leakingMetrics.Add($"Threads (+{threadDrift.ProjectedGrowthPerHour:F1} threads/hr, R²={threadDrift.RSquared:F2})");

            var summary = string.Join("; ", leakingMetrics);
            var fingerprint = Hashing.Sha256($"LEAK_{target.Id}_{summary}")[..16];

            finding = new Finding
            {
                Id = store.NextId(IdKind.Fault),
                TargetId = target.Id,
                Title = $"Resource Drift: Deterministic Leak in {target.Name}",
                Severity = Severity.High,
                Maturity = FindingMaturity.Confirming,
                Actual = $"Linear regression identified sustained resource growth during soak: {summary}",
                Expected = "Resource consumption reaches bounded steady state (slope < threshold; R² uncorrelated)",
                Fingerprint = fingerprint,
                ReproAttempts = 1,
                ReproSuccesses = 1,
                Owner = actor,
                FirstSeenAt = DateTimeOffset.Now,
                LastSeenAt = DateTimeOffset.Now,
            };

            store.Save(finding);

            store.Append(new LedgerEvent
            {
                Id = store.NextId(IdKind.Event),
                At = DateTimeOffset.Now,
                Code = "SOAK.DRIFT.DETECTED",
                Message = $"Deterministic resource drift detected in {target.Name}: {summary}",
                Severity = Severity.High,
                TargetId = target.Id,
                RelatedId = finding.Id.ToString(),
                Actor = actor,
                Detail = $"Duration: {duration.TotalMinutes:F1}m | Samples: {samples.Count} | WS: {wsDrift.Diagnostic} | PB: {pbDrift.Diagnostic}",
            });
        }

        return new SoakAnalysisReport
        {
            TargetId = target.Id,
            Duration = duration,
            SampleCount = samples.Count,
            WorkingSetDrift = wsDrift,
            PrivateBytesDrift = pbDrift,
            HandleDrift = handleDrift,
            ThreadDrift = threadDrift,
            GeneratedFinding = finding,
        };
    }
}
