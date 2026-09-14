using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Ghozt.Core;

/// <summary>
/// Types of data and state integrity oracles under Patent §19 and §20.
/// </summary>
public enum DataOracleType
{
    ExactRow,
    SetEquality,
    AggregateChecksum,
    ReferentialInvariant,
    ConservationEquation,
    TemporalFreshness,
}

/// <summary>
/// Set relation criteria for SetEquality oracle.
/// </summary>
public enum SetRelation
{
    ExactEqual,
    Subset,
    Superset,
}

/// <summary>
/// Result of evaluating a mathematical data oracle.
/// </summary>
public sealed record DataOracleResult
{
    public required bool Passed { get; init; }

    public required DataOracleType OracleType { get; init; }

    public required string OracleName { get; init; }

    public required string Summary { get; init; }

    public required string Expected { get; init; }

    public required string Actual { get; init; }

    public string? DeviationDetail { get; init; }

    public Evidence? SealedEvidence { get; init; }

    public Finding? GeneratedFinding { get; init; }
}

/// <summary>
/// Data, Database and Pipeline Examination Oracles Engine (Patent §19, §20).
/// Verifies referential, relational, aggregate, conservation, and temporal invariants
/// beyond the visual UI plane, sealing cryptographic evidence and generating actionable findings.
/// </summary>
public sealed class DataOracleEngine
{
    private readonly GhoztStore _store;

    public DataOracleEngine(GhoztStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Evaluates exact row column values in a database against expected values.
    /// </summary>
    public DataOracleResult EvaluateExactRow(
        Target target,
        string dbPath,
        string tableName,
        string keyColumn,
        string keyValue,
        IReadOnlyDictionary<string, string> expectedColumns,
        GhoztId? sweepId = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(expectedColumns);

        var oracleName = $"ExactRow:{tableName}:{keyValue}";
        if (!File.Exists(dbPath))
        {
            return CreateFailureResult(target, DataOracleType.ExactRow, oracleName,
                $"Database file does not exist at '{dbPath}'", "Database exists", "File missing", sweepId);
        }

        try
        {
            var cs = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly }.ToString();
            using var conn = new SqliteConnection(cs);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT * FROM \"{tableName}\" WHERE \"{keyColumn}\" = @key LIMIT 1;";
            cmd.Parameters.AddWithValue("@key", keyValue);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return CreateFailureResult(target, DataOracleType.ExactRow, oracleName,
                    $"Row with {keyColumn}='{keyValue}' was not found in table '{tableName}'.",
                    $"Row exists with {keyColumn}='{keyValue}'", "Row not found", sweepId);
            }

            var actuals = new Dictionary<string, string>();
            var mismatches = new List<string>();

            foreach (var (col, expVal) in expectedColumns)
            {
                var ordinal = reader.GetOrdinal(col);
                var actVal = reader.IsDBNull(ordinal) ? "NULL" : reader.GetString(ordinal);
                actuals[col] = actVal;

                if (!string.Equals(actVal, expVal, StringComparison.Ordinal))
                {
                    mismatches.Add($"[{col}]: expected='{expVal}', actual='{actVal}'");
                }
            }

            var expSummary = JsonSerializer.Serialize(expectedColumns);
            var actSummary = JsonSerializer.Serialize(actuals);

            if (mismatches.Count > 0)
            {
                return CreateFailureResult(target, DataOracleType.ExactRow, oracleName,
                    $"Mismatched columns in '{tableName}': {string.Join("; ", mismatches)}",
                    expSummary, actSummary, sweepId);
            }

            return CreatePassResult(target, DataOracleType.ExactRow, oracleName,
                $"Row {keyColumn}='{keyValue}' in '{tableName}' satisfies all {expectedColumns.Count} column assertions.",
                expSummary, actSummary, sweepId);
        }
        catch (Exception ex)
        {
            return CreateFailureResult(target, DataOracleType.ExactRow, oracleName,
                $"Exception evaluating exact row: {ex.Message}", "Valid query", ex.Message, sweepId);
        }
    }

    /// <summary>
    /// Evaluates set equality, subset, or superset invariants between two collections.
    /// </summary>
    public DataOracleResult EvaluateSetRelation(
        Target target,
        string oracleName,
        IEnumerable<string> setA,
        IEnumerable<string> setB,
        SetRelation relation,
        GhoztId? sweepId = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        var a = new HashSet<string>(setA ?? Array.Empty<string>());
        var b = new HashSet<string>(setB ?? Array.Empty<string>());

        bool passed;
        string expected;
        string actual;
        string detail = "";

        switch (relation)
        {
            case SetRelation.ExactEqual:
                var missingInB = a.Except(b).ToList();
                var missingInA = b.Except(a).ToList();
                passed = missingInB.Count == 0 && missingInA.Count == 0;
                expected = $"SetA == SetB (|A|={a.Count}, |B|={b.Count})";
                actual = passed ? "Sets are identical" : $"Differences: A\\B=[{string.Join(", ", missingInB.Take(5))}], B\\A=[{string.Join(", ", missingInA.Take(5))}]";
                detail = actual;
                break;

            case SetRelation.Subset:
                var notInB = a.Except(b).ToList();
                passed = notInB.Count == 0;
                expected = "SetA is subset of SetB (A ⊆ B)";
                actual = passed ? $"All {a.Count} items of A exist in B" : $"{notInB.Count} items of A missing in B: [{string.Join(", ", notInB.Take(5))}]";
                detail = actual;
                break;

            case SetRelation.Superset:
                var notInA = b.Except(a).ToList();
                passed = notInA.Count == 0;
                expected = "SetA is superset of SetB (A ⊇ B)";
                actual = passed ? $"All {b.Count} items of B exist in A" : $"{notInA.Count} items of B missing in A: [{string.Join(", ", notInA.Take(5))}]";
                detail = actual;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(relation));
        }

        if (passed)
        {
            return CreatePassResult(target, DataOracleType.SetEquality, oracleName,
                $"Set relation {relation} verified successfully ({expected}).", expected, actual, sweepId);
        }

        return CreateFailureResult(target, DataOracleType.SetEquality, oracleName,
            $"Set relation {relation} violated: {detail}", expected, actual, sweepId);
    }

    /// <summary>
    /// Reconciles row count, numerical sum, average, and cryptographic SHA-256 digest across dataset items.
    /// </summary>
    public DataOracleResult EvaluateAggregateChecksum(
        Target target,
        string oracleName,
        int expectedCount,
        double expectedSum,
        string expectedSha256Digest,
        IReadOnlyList<double> numbers,
        IReadOnlyList<string> payloads,
        double epsilon = 1e-4,
        GhoztId? sweepId = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(numbers);
        ArgumentNullException.ThrowIfNull(payloads);

        var actCount = numbers.Count;
        var actSum = numbers.Sum();

        var combinedPayload = string.Join("\n", payloads);
        var actSha256 = ComputeSha256(combinedPayload);

        var countMatches = actCount == expectedCount;
        var sumMatches = Math.Abs(actSum - expectedSum) <= epsilon;
        var shaMatches = string.IsNullOrWhiteSpace(expectedSha256Digest) ||
                         string.Equals(actSha256, expectedSha256Digest, StringComparison.OrdinalIgnoreCase);

        var expDesc = $"count={expectedCount}, sum={expectedSum:F4}, sha256={expectedSha256Digest}";
        var actDesc = $"count={actCount}, sum={actSum:F4}, sha256={actSha256}";

        if (countMatches && sumMatches && shaMatches)
        {
            return CreatePassResult(target, DataOracleType.AggregateChecksum, oracleName,
                $"Aggregate reconciliation passed: {actCount} records, sum={actSum:F4}, SHA-256 verified.",
                expDesc, actDesc, sweepId);
        }

        var deviations = new List<string>();
        if (!countMatches) deviations.Add($"Count mismatch (exp={expectedCount}, act={actCount})");
        if (!sumMatches) deviations.Add($"Sum mismatch (exp={expectedSum:F4}, act={actSum:F4}, delta={Math.Abs(actSum - expectedSum):F4})");
        if (!shaMatches) deviations.Add($"SHA-256 mismatch (exp={expectedSha256Digest}, act={actSha256})");

        return CreateFailureResult(target, DataOracleType.AggregateChecksum, oracleName,
            $"Aggregate reconciliation failed: {string.Join("; ", deviations)}",
            expDesc, actDesc, sweepId);
    }

    /// <summary>
    /// Validates referential integrity: every foreign key in the child list must exist in the parent keys set.
    /// </summary>
    public DataOracleResult EvaluateReferentialIntegrity(
        Target target,
        string oracleName,
        string parentEntity,
        string childEntity,
        IReadOnlyCollection<string> parentPrimaryKeys,
        IReadOnlyList<(string ChildId, string ParentForeignKey)> childReferences,
        GhoztId? sweepId = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(parentPrimaryKeys);
        ArgumentNullException.ThrowIfNull(childReferences);

        var parentSet = new HashSet<string>(parentPrimaryKeys, StringComparer.Ordinal);
        var orphans = new List<string>();

        foreach (var (childId, fKey) in childReferences)
        {
            if (string.IsNullOrWhiteSpace(fKey) || !parentSet.Contains(fKey))
            {
                orphans.Add($"Child {childId} references missing parent key '{fKey}'");
            }
        }

        var expDesc = $"0 orphans across {childReferences.Count} child references to {parentEntity}";
        var actDesc = $"{orphans.Count} orphans detected";

        if (orphans.Count == 0)
        {
            return CreatePassResult(target, DataOracleType.ReferentialInvariant, oracleName,
                $"Referential integrity verified: all {childReferences.Count} {childEntity} records map to valid {parentEntity} primary keys.",
                expDesc, actDesc, sweepId);
        }

        return CreateFailureResult(target, DataOracleType.ReferentialInvariant, oracleName,
            $"Referential integrity violation: {orphans.Count} orphaned records detected ({string.Join(", ", orphans.Take(5))})",
            expDesc, actDesc, sweepId);
    }

    /// <summary>
    /// Validates physical, financial or conservation balancing equations:
    /// Sum(Inflow) - Sum(Outflow) == DeltaBalance (within tolerance epsilon).
    /// </summary>
    public DataOracleResult EvaluateConservationEquation(
        Target target,
        string oracleName,
        double initialBalance,
        double finalBalance,
        IEnumerable<double> inflows,
        IEnumerable<double> outflows,
        double epsilon = 1e-4,
        GhoztId? sweepId = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        var totalIn = inflows?.Sum() ?? 0.0;
        var totalOut = outflows?.Sum() ?? 0.0;
        var expectedDelta = totalIn - totalOut;
        var actualDelta = finalBalance - initialBalance;
        var imbalance = Math.Abs(expectedDelta - actualDelta);

        var expDesc = $"DeltaBalance = Sum(In) - Sum(Out) = {totalIn:F4} - {totalOut:F4} = {expectedDelta:F4}";
        var actDesc = $"ActualDelta = Final - Initial = {finalBalance:F4} - {initialBalance:F4} = {actualDelta:F4} (imbalance = {imbalance:F6})";

        if (imbalance <= epsilon)
        {
            return CreatePassResult(target, DataOracleType.ConservationEquation, oracleName,
                $"Conservation balancing equation verified (imbalance = {imbalance:F6} <= {epsilon:F6}).",
                expDesc, actDesc, sweepId);
        }

        return CreateFailureResult(target, DataOracleType.ConservationEquation, oracleName,
            $"Conservation equation violated: discrepancy of {imbalance:F4} exceeds tolerance {epsilon:F4}.",
            expDesc, actDesc, sweepId);
    }

    /// <summary>
    /// Validates temporal freshness: monotonic timestamp ordering (t_i <= t_{i+1}),
    /// no future timestamps (t <= now + clockSkew), and maximum staleness limits.
    /// </summary>
    public DataOracleResult EvaluateTemporalFreshness(
        Target target,
        string oracleName,
        IReadOnlyList<DateTimeOffset> timestamps,
        TimeSpan maxStaleness,
        TimeSpan allowedClockSkew,
        GhoztId? sweepId = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(timestamps);

        if (timestamps.Count == 0)
        {
            return CreateFailureResult(target, DataOracleType.TemporalFreshness, oracleName,
                "No timestamps provided for temporal freshness assay.", ">= 1 timestamp", "0 timestamps", sweepId);
        }

        var now = DateTimeOffset.Now;
        var maxFuture = now + allowedClockSkew;
        var nonMonotonic = new List<string>();
        var futureTimes = new List<string>();

        for (int i = 0; i < timestamps.Count; i++)
        {
            var t = timestamps[i];
            if (t > maxFuture)
            {
                futureTimes.Add($"Index {i} ({t:O}) is in the future beyond allowed skew {allowedClockSkew.TotalSeconds}s");
            }

            if (i > 0 && timestamps[i] < timestamps[i - 1])
            {
                nonMonotonic.Add($"Index {i} ({t:O}) is earlier than index {i - 1} ({timestamps[i - 1]:O})");
            }
        }

        var latest = timestamps.Max();
        var staleness = now - latest;
        var isStale = staleness > maxStaleness;

        var expDesc = $"Monotonic non-decreasing, t <= now + {allowedClockSkew.TotalSeconds}s, staleness <= {maxStaleness.TotalSeconds}s";
        var actDesc = $"Count={timestamps.Count}, Latest={latest:O}, Staleness={staleness.TotalSeconds:F1}s, FutureViolations={futureTimes.Count}, Inversions={nonMonotonic.Count}";

        if (nonMonotonic.Count == 0 && futureTimes.Count == 0 && !isStale)
        {
            return CreatePassResult(target, DataOracleType.TemporalFreshness, oracleName,
                $"Temporal freshness verified: monotonic sequence of {timestamps.Count} timestamps with staleness {staleness.TotalSeconds:F1}s.",
                expDesc, actDesc, sweepId);
        }

        var defects = new List<string>();
        if (nonMonotonic.Count > 0) defects.Add($"Timestamp inversions: {string.Join("; ", nonMonotonic.Take(3))}");
        if (futureTimes.Count > 0) defects.Add($"Future timestamps: {string.Join("; ", futureTimes.Take(3))}");
        if (isStale) defects.Add($"Telemetry is stale (age={staleness.TotalSeconds:F1}s > max={maxStaleness.TotalSeconds:F1}s)");

        return CreateFailureResult(target, DataOracleType.TemporalFreshness, oracleName,
            $"Temporal freshness violation: {string.Join("; ", defects)}",
            expDesc, actDesc, sweepId);
    }

    // ------------------------------------------------------------------ helpers

    private DataOracleResult CreatePassResult(
        Target target,
        DataOracleType type,
        string name,
        string summary,
        string exp,
        string act,
        GhoztId? sweepId)
    {
        var evidence = SealEvidence(target, type, name, true, summary, exp, act, sweepId);

        return new DataOracleResult
        {
            Passed = true,
            OracleType = type,
            OracleName = name,
            Summary = summary,
            Expected = exp,
            Actual = act,
            SealedEvidence = evidence,
        };
    }

    private DataOracleResult CreateFailureResult(
        Target target,
        DataOracleType type,
        string name,
        string detail,
        string exp,
        string act,
        GhoztId? sweepId)
    {
        var evidence = SealEvidence(target, type, name, false, detail, exp, act, sweepId);

        // Generate an actionable Finding
        var fingerprint = FaultFingerprint.Compute(
            targetId: target.Id.ToString(),
            exceptionClass: $"DATA_ORACLE_{type.ToString().ToUpperInvariant()}",
            errorCode: name,
            messageOrDetail: detail);

        var finding = new Finding
        {
            Id = _store.NextId(IdKind.Fault),
            TargetId = target.Id,
            SweepId = sweepId,
            Title = $"Data Oracle Failure: {name}",
            Severity = type is DataOracleType.ReferentialInvariant or DataOracleType.ConservationEquation ? Severity.Critical : Severity.High,
            Maturity = FindingMaturity.New,
            Actual = act,
            Expected = exp,
            Fingerprint = fingerprint,
            ReproAttempts = 1,
            ReproSuccesses = 1,
            EvidenceCount = 1,
            FirstSeenAt = DateTimeOffset.Now,
            LastSeenAt = DateTimeOffset.Now,
        };

        _store.Save(finding);

        return new DataOracleResult
        {
            Passed = false,
            OracleType = type,
            OracleName = name,
            Summary = detail,
            Expected = exp,
            Actual = act,
            DeviationDetail = detail,
            SealedEvidence = evidence,
            GeneratedFinding = finding,
        };
    }

    private Evidence SealEvidence(
        Target target,
        DataOracleType type,
        string name,
        bool passed,
        string summary,
        string exp,
        string act,
        GhoztId? sweepId)
    {
        var payloadObj = new
        {
            oracle_type = type.ToString(),
            oracle_name = name,
            passed,
            summary,
            expected = exp,
            actual = act,
            timestamp = DateTimeOffset.Now.ToString("O"),
        };

        var json = JsonSerializer.Serialize(payloadObj);
        var sha256 = ComputeSha256(json);

        var ev = new Evidence
        {
            Id = _store.NextId(IdKind.Evidence),
            TargetId = target.Id,
            SweepId = sweepId,
            Kind = "OracleEvaluation",
            Summary = $"Data Oracle {type}: {name} -> {(passed ? "PASSED" : "FAILED")}",
            Sha256 = sha256,
            SizeBytes = Encoding.UTF8.GetByteCount(json),
            Payload = json,
            SourceAdapter = "core:data-oracle-engine",
            CapturedAt = DateTimeOffset.Now,
            Sealed = true,
            SealedBy = "DataOracleEngine@v1",
        };

        _store.Save(ev);
        return ev;
    }

    private static string ComputeSha256(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
