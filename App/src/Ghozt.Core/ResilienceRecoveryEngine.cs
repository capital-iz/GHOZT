using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ghozt.Core;

/// <summary>
/// The 14 system failure classes defined in Patent §39 (Table 77).
/// </summary>
public enum SystemFailureMode
{
    MissingInputs,
    PermissionDenial,
    AdapterUnavailable,
    RunnerCrash,
    ControlPlaneRestart,
    NetworkPartition,
    EvidenceStoreFailure,
    StoragePressure,
    ClockSyncLoss,
    AiModelUnavailable,
    LowConfidenceAi,
    TargetCrash,
    RollbackFailure,
    ReportFailure,
}

/// <summary>
/// Prescribed response action for a system failure under Patent Table 77.
/// </summary>
public sealed record ResilienceDirective
{
    public required SystemFailureMode FailureMode { get; init; }

    public required string RequiredResponse { get; init; }

    public required bool CanRetry { get; init; }

    public required bool RequiresHumanReview { get; init; }

    public required bool HaltsHighRiskExecution { get; init; }

    public string DegradedOperationNote { get; init; } = "";
}

/// <summary>
/// Execution policy governing retries, exponential back-off, and idempotency.
/// </summary>
public sealed record RetryPolicy
{
    public int MaxAttempts { get; init; } = 3;

    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromMilliseconds(200);

    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(5);

    public double JitterFactor { get; init; } = 0.20;

    public bool IsIdempotent { get; init; } = true;
}

/// <summary>
/// Directive emitted when storage pressure is detected.
/// </summary>
public sealed record StorageDegradationDirective
{
    public required bool DegradeActive { get; init; }

    public required long AvailableBytes { get; init; }

    public required long ThresholdBytes { get; init; }

    public required string PolicyMessage { get; init; }

    public bool OmitHighResolutionMedia { get; init; }

    public bool SpoolPayloadsByReference { get; init; }
}

/// <summary>
/// Failure Handling, Recovery and Degraded Operation Engine (Patent §39).
/// Enforces bounded exponential backoff with jitter, idempotency checks, immutable
/// retry audit logging, and graceful degradation during system stress.
/// </summary>
public sealed class ResilienceRecoveryEngine
{
    private readonly GhoztStore _store;

    public ResilienceRecoveryEngine(GhoztStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Classifies a system failure mode according to Patent Table 77.
    /// </summary>
    public static ResilienceDirective ClassifyFailure(SystemFailureMode mode)
    {
        return mode switch
        {
            SystemFailureMode.MissingInputs => new ResilienceDirective
            {
                FailureMode = mode,
                RequiredResponse = "Block affected action, create visible task and preserve plan.",
                CanRetry = false,
                RequiresHumanReview = true,
                HaltsHighRiskExecution = false,
                DegradedOperationNote = "Action queued until operator supplies missing parameters.",
            },

            SystemFailureMode.PermissionDenial => new ResilienceDirective
            {
                FailureMode = mode,
                RequiredResponse = "Deny, record actor/action/policy and offer legitimate request path.",
                CanRetry = false,
                RequiresHumanReview = true,
                HaltsHighRiskExecution = true,
                DegradedOperationNote = "Access class escalation blocked; policy denial audit recorded.",
            },

            SystemFailureMode.AdapterUnavailable => new ResilienceDirective
            {
                FailureMode = mode,
                RequiredResponse = "Mark capability unavailable, capture diagnostics and use approved fallback only.",
                CanRetry = true,
                RequiresHumanReview = false,
                HaltsHighRiskExecution = false,
                DegradedOperationNote = "Capability flagged degraded; falling back to basic process supervision.",
            },

            SystemFailureMode.RunnerCrash => new ResilienceDirective
            {
                FailureMode = mode,
                RequiredResponse = "Detect lease loss, seal completed events, mark uncertain in-flight action and recover/reset before retry.",
                CanRetry = true,
                RequiresHumanReview = false,
                HaltsHighRiskExecution = true,
                DegradedOperationNote = "Runner lease expired; in-flight action sealed with UNCERTAIN state.",
            },

            SystemFailureMode.ControlPlaneRestart => new ResilienceDirective
            {
                FailureMode = mode,
                RequiredResponse = "Restore Sweep state from ledger; reconcile runners; never infer completion from queue absence.",
                CanRetry = true,
                RequiresHumanReview = false,
                HaltsHighRiskExecution = false,
                DegradedOperationNote = "Reconstructed state machine from immutable event ledger.",
            },

            SystemFailureMode.NetworkPartition => new ResilienceDirective
            {
                FailureMode = mode,
                RequiredResponse = "Queue safe events/actions, freeze misleading live indicators and reconcile when connection returns.",
                CanRetry = true,
                RequiresHumanReview = false,
                HaltsHighRiskExecution = false,
                DegradedOperationNote = "Events spooled locally in WAL; live telemetry indicators frozen.",
            },

            SystemFailureMode.EvidenceStoreFailure => new ResilienceDirective
            {
                FailureMode = mode,
                RequiredResponse = "Stop reportable high-risk execution if required evidence cannot be preserved; spool safely where permitted.",
                CanRetry = false,
                RequiresHumanReview = true,
                HaltsHighRiskExecution = true,
                DegradedOperationNote = "HALT: Cannot execute clearance or destructive operations without verified evidence persistence.",
            },

            SystemFailureMode.StoragePressure => new ResilienceDirective
            {
                FailureMode = mode,
                RequiredResponse = "Apply declared capture degradation and record omitted artefacts.",
                CanRetry = true,
                RequiresHumanReview = false,
                HaltsHighRiskExecution = false,
                DegradedOperationNote = "Payload capture degraded to metadata only; large media captures omitted.",
            },

            SystemFailureMode.ClockSyncLoss => new ResilienceDirective
            {
                FailureMode = mode,
                RequiredResponse = "Preserve source order and uncertainty; do not fabricate exact sequence.",
                CanRetry = false,
                RequiresHumanReview = false,
                HaltsHighRiskExecution = false,
                DegradedOperationNote = "Timestamps marked UNCERTAIN; relative monotonic ordering preserved.",
            },

            SystemFailureMode.AiModelUnavailable => new ResilienceDirective
            {
                FailureMode = mode,
                RequiredResponse = "Continue deterministic capabilities; mark intelligent analysis unavailable.",
                CanRetry = false,
                RequiresHumanReview = false,
                HaltsHighRiskExecution = false,
                DegradedOperationNote = "Intelligence plane disabled; deterministic state lattice and oracles active.",
            },

            SystemFailureMode.LowConfidenceAi => new ResilienceDirective
            {
                FailureMode = mode,
                RequiredResponse = "Route to human review and show missing evidence/assumptions.",
                CanRetry = false,
                RequiresHumanReview = true,
                HaltsHighRiskExecution = false,
                DegradedOperationNote = "Recommendation routed to review queue due to confidence below threshold.",
            },

            SystemFailureMode.TargetCrash => new ResilienceDirective
            {
                FailureMode = mode,
                RequiredResponse = "Capture crash and pre-failure evidence, contain side effects, attempt policy-controlled reset.",
                CanRetry = true,
                RequiresHumanReview = false,
                HaltsHighRiskExecution = true,
                DegradedOperationNote = "Target process crash recorded with memory dump; reset routine initiated.",
            },

            SystemFailureMode.RollbackFailure => new ResilienceDirective
            {
                FailureMode = mode,
                RequiredResponse = "Escalate, freeze further destructive work and record residual target state.",
                CanRetry = false,
                RequiresHumanReview = true,
                HaltsHighRiskExecution = true,
                DegradedOperationNote = "CRITICAL: State rollback failed. Target locked to prevent compound damage.",
            },

            SystemFailureMode.ReportFailure => new ResilienceDirective
            {
                FailureMode = mode,
                RequiredResponse = "Preserve source data, record export fault and retry without changing findings.",
                CanRetry = true,
                RequiresHumanReview = false,
                HaltsHighRiskExecution = false,
                DegradedOperationNote = "Report formatting fault isolated; underlying evidence and findings intact.",
            },

            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }

    /// <summary>
    /// Calculates exponential back-off delay with random jitter (Patent §39.2).
    /// delay = min(maxDelay, baseDelay * 2^(attempt - 1)) +/- jitter
    /// </summary>
    public static TimeSpan CalculateBackoff(int attempt, TimeSpan baseDelay, TimeSpan maxDelay, double jitterFactor = 0.20)
    {
        if (attempt <= 1) return baseDelay;

        var expMultiplier = Math.Pow(2.0, Math.Min(attempt - 1, 10));
        var rawMs = baseDelay.TotalMilliseconds * expMultiplier;
        var cappedMs = Math.Min(rawMs, maxDelay.TotalMilliseconds);

        // Apply pseudo-random jitter
        var jitterRange = cappedMs * Math.Clamp(jitterFactor, 0.0, 0.50);
        var jitterDelta = (Random.Shared.NextDouble() * 2.0 - 1.0) * jitterRange;
        var finalMs = Math.Max(1.0, cappedMs + jitterDelta);

        return TimeSpan.FromMilliseconds(finalMs);
    }

    /// <summary>
    /// Evaluates storage headroom and applies capture degradation if capacity is constrained.
    /// </summary>
    public StorageDegradationDirective EvaluateStorageHeadroom(
        long availableBytes,
        long thresholdBytes = 500L * 1024 * 1024) // 500 MB default threshold
    {
        var isConstrained = availableBytes < thresholdBytes;

        return new StorageDegradationDirective
        {
            DegradeActive = isConstrained,
            AvailableBytes = availableBytes,
            ThresholdBytes = thresholdBytes,
            PolicyMessage = isConstrained
                ? $"Storage pressure ({availableBytes / (1024 * 1024)} MB available < {thresholdBytes / (1024 * 1024)} MB threshold). Degraded capture active."
                : $"Storage headroom normal ({availableBytes / (1024 * 1024)} MB available).",
            OmitHighResolutionMedia = isConstrained,
            SpoolPayloadsByReference = isConstrained,
        };
    }

    /// <summary>
    /// Executes an asynchronous operation under the resilience retry policy without returning a value.
    /// </summary>
    public async Task ExecuteWithResilienceAsync(
        string operationName,
        Func<int, Task> operation,
        RetryPolicy policy,
        Target target,
        GhoztId? sweepId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await ExecuteWithResilienceAsync<bool>(
            operationName,
            async attempt =>
            {
                await operation(attempt).ConfigureAwait(false);
                return true;
            },
            policy,
            target,
            sweepId,
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes an asynchronous operation under the resilience retry policy.
    /// Retries only safe, idempotent actions. Logs every retry to the immutable event ledger.
    /// </summary>
    public async Task<T> ExecuteWithResilienceAsync<T>(
        string operationName,
        Func<int, Task<T>> operation,
        RetryPolicy policy,
        Target target,
        GhoztId? sweepId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(target);

        int attempt = 0;
        Exception? lastException = null;

        while (attempt < policy.MaxAttempts)
        {
            attempt++;
            try
            {
                var result = await operation(attempt).ConfigureAwait(false);

                // If succeeded on retry, log recovery
                if (attempt > 1)
                {
                    _store.Append(new LedgerEvent
                    {
                        Id = _store.NextId(IdKind.Event),
                        At = DateTimeOffset.Now,
                        Code = "SYS.RESILIENCE.RECOVERED",
                        Message = $"Operation '{operationName}' recovered on attempt {attempt}/{policy.MaxAttempts}.",
                        Severity = Severity.Info,
                        TargetId = target.Id,
                        SweepId = sweepId,
                        Actor = "ResilienceRecoveryEngine@v1",
                        Detail = $"Previous failure resolved: {lastException?.Message}",
                    });
                }

                return result;
            }
            catch (Exception ex)
            {
                lastException = ex;

                // Record initial fault without overwriting
                _store.Append(new LedgerEvent
                {
                    Id = _store.NextId(IdKind.Event),
                    At = DateTimeOffset.Now,
                    Code = "SYS.RESILIENCE.FAULT",
                    Message = $"Operation '{operationName}' failed on attempt {attempt}/{policy.MaxAttempts}: {ex.Message}",
                    Severity = attempt == policy.MaxAttempts ? Severity.High : Severity.Medium,
                    TargetId = target.Id,
                    SweepId = sweepId,
                    Actor = "ResilienceRecoveryEngine@v1",
                    Detail = ex.GetType().FullName ?? "UnknownException",
                });

                // Idempotency check: Cannot retry non-idempotent mutations
                if (!policy.IsIdempotent)
                {
                    _store.Append(new LedgerEvent
                    {
                        Id = _store.NextId(IdKind.Event),
                        At = DateTimeOffset.Now,
                        Code = "SYS.RESILIENCE.RETRY_REFUSED",
                        Message = $"Non-idempotent operation '{operationName}' cannot be retried automatically.",
                        Severity = Severity.High,
                        TargetId = target.Id,
                        SweepId = sweepId,
                        Actor = "ResilienceRecoveryEngine@v1",
                        Detail = "Patent §39: Retry only when action is safe, idempotent or protected by reset.",
                    });

                    throw new InvalidOperationException($"Operation '{operationName}' is non-idempotent and cannot be retried.", ex);
                }

                if (attempt >= policy.MaxAttempts)
                {
                    break;
                }

                var delay = CalculateBackoff(attempt, policy.BaseDelay, policy.MaxDelay, policy.JitterFactor);

                _store.Append(new LedgerEvent
                {
                    Id = _store.NextId(IdKind.Event),
                    At = DateTimeOffset.Now,
                    Code = "SYS.RESILIENCE.RETRY",
                    Message = $"Scheduling retry {attempt + 1}/{policy.MaxAttempts} for '{operationName}' after {delay.TotalMilliseconds:N0}ms backoff.",
                    Severity = Severity.Info,
                    TargetId = target.Id,
                    SweepId = sweepId,
                    Actor = "ResilienceRecoveryEngine@v1",
                    Detail = $"Backoff attempt={attempt}; delay={delay.TotalMilliseconds:N0}ms",
                });

                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }

        throw new TimeoutException(
            $"Operation '{operationName}' exhausted all {policy.MaxAttempts} resilience retry attempts. Last error: {lastException?.Message}",
            lastException);
    }
}
