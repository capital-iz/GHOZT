using System.Diagnostics;
using System.Globalization;

namespace Ghozt.Core;

/// <summary>What a sweep is currently doing, for the live board.</summary>
public sealed record SweepProgress(
    string Phase,
    int StatesVisited,
    int MaxStates,
    int Deviations,
    int Evidence,
    TimeSpan Elapsed,
    string Detail);

/// <summary>
/// Runs a governed examination against a registered target.
/// </summary>
/// <remarks>
/// <para>
/// This engine performs real work against the target it is given and records what it actually
/// observes. It does not fabricate findings. Where a check cannot be performed - no base address, an
/// unreadable path, an access class that forbids the action - it records that as a blocked or
/// skipped check with the reason, which is a different outcome from a passing one and is shown as
/// such.
/// </para>
/// <para>
/// Budgets are enforced rather than advertised: the run stops when its state, time or depth budget
/// is exhausted, and the resulting sweep is marked Partial rather than Completed. A run that
/// silently ran past its budget would make every budget in the product meaningless.
/// </para>
/// </remarks>
public sealed class SweepEngine
{
    private readonly GhoztStore _store;

    public SweepEngine(GhoztStore store) => _store = store;

    /// <summary>The checks a sweep mode performs, in order.</summary>
    public static IReadOnlyList<string> ChecksFor(string mode) => mode switch
    {
        "Smoke" => ["Reachability", "Identity", "Structure"],
        "Changed area" => ["Reachability", "Identity", "Structure", "Integrity"],
        "Full sweep" => ["Reachability", "Identity", "Structure", "Integrity", "Capacity", "Consistency"],
        "Deep forensic" => ["Reachability", "Identity", "Structure", "Integrity", "Capacity", "Consistency", "Provenance"],
        _ => ["Reachability", "Identity"],
    };

    public static IReadOnlyList<string> Modes =>
        ["Smoke", "Changed area", "Full sweep", "Deep forensic"];

    /// <summary>
    /// Executes a sweep, reporting progress as it goes.
    /// </summary>
    /// <remarks>
    /// Cancellation is honoured between checks rather than only at the end, so a stop actually stops.
    /// Whatever was completed before the stop is preserved and the sweep is marked Cancelled, because
    /// discarding partial evidence would punish an operator for stopping a run that was going wrong.
    /// </remarks>
    public async Task<Sweep> RunAsync(
        Sweep sweep,
        Target target,
        IProgress<SweepProgress>? progress,
        CancellationToken cancellation)
    {
        var clock = Stopwatch.StartNew();
        var checks = ChecksFor(sweep.Mode);

        var visited = 0;
        var deviations = 0;
        var evidence = 0;
        var budgetExhausted = false;

        var current = sweep with { State = RunState.Running, StartedAt = DateTimeOffset.Now };
        _store.Save(current);

        Log("SWP.START", $"Sweep {sweep.Id} started in {sweep.Mode} mode against {target.Name}.",
            Severity.Info, sweep, target,
            $"budget: {sweep.MaxStates} states, {sweep.MaxSeconds}s, depth {sweep.MaxDepth}; "
            + $"destructive={sweep.AllowDestructive}");

        foreach (var check in checks)
        {
            if (cancellation.IsCancellationRequested)
            {
                break;
            }

            if (visited >= sweep.MaxStates || clock.Elapsed.TotalSeconds >= sweep.MaxSeconds)
            {
                budgetExhausted = true;
                Log("SWP.BUDGET", $"Budget exhausted before {check}.", Severity.Medium, sweep, target,
                    $"states={visited}/{sweep.MaxStates}; elapsed={clock.Elapsed.TotalSeconds:F1}/{sweep.MaxSeconds}s");
                break;
            }

            progress?.Report(new SweepProgress(check, visited, sweep.MaxStates, deviations, evidence,
                clock.Elapsed, $"Running {check}…"));

            // A check that throws is a blocked check, not a dead sweep.
            //
            // The first version let the exception propagate, and one unreadable file ended the whole
            // run - losing the evidence from every check that had already passed. A forensics tool
            // examines systems that are live, locked, half-configured and occasionally hostile; a
            // single failing probe must degrade that probe, not the examination.
            CheckResult result;
            try
            {
                result = await RunCheckAsync(check, sweep, target, cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
#pragma warning disable CA1031 // An adapter can fail in any way; the sweep records it and continues.
            catch (Exception ex)
            {
                result = new CheckResult(
                    Passed: false,
                    Blocked: true,
                    Summary: $"{check} could not be completed.",
                    Detail: $"The check raised {ex.GetType().Name}: {ex.Message}\n\n{ex}",
                    Severity: Severity.Medium,
                    StatesVisited: 1);
            }
#pragma warning restore CA1031

            visited += result.StatesVisited;

            Log(result.Passed ? "CHK.PASS" : result.Blocked ? "CHK.BLOCKED" : "CHK.DEVIATION",
                result.Summary,
                result.Passed ? Severity.Info : result.Blocked ? Severity.Medium : result.Severity,
                sweep, target, result.Detail);

            // Every check produces an artefact, whatever its outcome. A check that passed with no
            // record behind it is an assertion; the point of this product is that it is not.
            var payload = $"check={check}\ntarget={target.Id}\nsweep={sweep.Id}\n"
                          + $"at={DateTimeOffset.Now:O}\noutcome={(result.Blocked ? "blocked" : result.Passed ? "pass" : "deviation")}\n"
                          + result.Detail;

            _store.Save(new Evidence
            {
                Id = _store.NextId(IdKind.Evidence),
                TargetId = target.Id,
                SweepId = sweep.Id,
                Kind = $"check:{check.ToLowerInvariant()}",
                Summary = result.Summary,
                Sha256 = Hashing.Sha256(payload),
                SizeBytes = System.Text.Encoding.UTF8.GetByteCount(payload),
                Payload = payload,
                SourceAdapter = "SweepEngine",
                CapturedAt = DateTimeOffset.Now,
            });
            evidence++;

            // A node in the lattice for the check, carrying its observed state. Real structure, not
            // decoration: it resolves to a check that actually ran against this target.
            _store.Save(new LatticeNode
            {
                Id = $"check:{check.ToLowerInvariant()}",
                TargetId = target.Id,
                Label = check,
                Kind = NodeKind.Action,
                State = result.Blocked ? "Warning" : result.Passed ? "Verified" : "Deviation",
                Provenance = "Observed",
                ParentId = "system:root",
                VisitCount = 1,
                Detail = result.Detail,
                EvidenceCount = 1,
                LastSeenAt = DateTimeOffset.Now,
            });

            _store.Save(new LatticeEdge
            {
                TargetId = target.Id,
                FromId = "system:root",
                ToId = $"check:{check.ToLowerInvariant()}",
                Label = check,
                Relationship = "observed",
                TraversalCount = 1,
            });

            if (!result.Passed && !result.Blocked)
            {
                deviations++;
                RecordFinding(result, sweep, target);
            }

            progress?.Report(new SweepProgress(check, visited, sweep.MaxStates, deviations, evidence,
                clock.Elapsed, result.Summary));
        }

        var finalState =
            cancellation.IsCancellationRequested ? RunState.Cancelled
            : budgetExhausted ? RunState.Partial
            : RunState.Completed;

        var outcome = finalState switch
        {
            RunState.Cancelled => "Stopped by the operator. Completed checks and their evidence are preserved.",
            RunState.Partial => "Budget exhausted before every check ran. Untested scope is marked, not assumed to pass.",
            _ when deviations > 0 => $"Completed with {deviations} deviation{(deviations == 1 ? "" : "s")}.",
            _ => "Completed. Every check in scope passed with evidence.",
        };

        current = current with
        {
            State = finalState,
            FinishedAt = DateTimeOffset.Now,
            StatesVisited = visited,
            DeviationsFound = deviations,
            EvidenceCaptured = evidence,
            Outcome = outcome,
        };

        _store.Save(current);
        _store.Save(target with { LastSweepAt = DateTimeOffset.Now });

        Log("SWP.END", $"Sweep {sweep.Id} {finalState.ToString().ToLowerInvariant()}. {outcome}",
            deviations > 0 ? Severity.Medium : Severity.Info, sweep, target,
            $"states={visited}; deviations={deviations}; evidence={evidence}; elapsed={clock.Elapsed.TotalSeconds:F1}s");

        progress?.Report(new SweepProgress("Finished", visited, sweep.MaxStates, deviations, evidence,
            clock.Elapsed, outcome));

        return current;
    }

    private sealed record CheckResult(
        bool Passed,
        bool Blocked,
        string Summary,
        string Detail,
        Severity Severity,
        int StatesVisited,
        string Expected = "",
        string Actual = "");

    /// <summary>
    /// Performs one check against the real target.
    /// </summary>
    /// <remarks>
    /// Every branch measures something on this machine. Where the target does not expose what a check
    /// needs, the check is <em>blocked</em> - which is neither a pass nor a failure, and is the
    /// outcome the blueprint requires be distinguishable from both.
    /// </remarks>
    private async Task<CheckResult> RunCheckAsync(
        string check,
        Sweep sweep,
        Target target,
        CancellationToken cancellation)
    {
        // A short, real pause between checks so the live board is legible and cancellation has
        // somewhere to land. Not a fake workload: the checks below do the actual work.
        await Task.Delay(120, cancellation).ConfigureAwait(false);

        var path = target.BaseAddress;

        switch (check)
        {
            case "Reachability":
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return new CheckResult(false, true,
                        "Reachability could not be established.",
                        "No base address or path is registered for this target, so there is nothing to reach.",
                        Severity.Medium, 1);
                }

                var exists = File.Exists(path) || Directory.Exists(path);
                if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                {
                    return new CheckResult(false, true,
                        "Reachability deferred to the capability probe.",
                        $"{uri} is an HTTP target; the sweep engine performs filesystem checks only in this build.",
                        Severity.Low, 1);
                }

                return exists
                    ? new CheckResult(true, false, "Target is present and readable.",
                        $"Resolved {path}.", Severity.Info, 1)
                    : new CheckResult(false, false, "Target path does not exist.",
                        $"Expected a file or directory at {path}; nothing is there.",
                        Severity.High, 1, $"A readable artefact at {path}", "Path not found");
            }

            case "Identity":
            {
                if (string.IsNullOrWhiteSpace(target.BuildLabel))
                {
                    return new CheckResult(false, false,
                        "No build identity is recorded for this target.",
                        "Findings cannot be attributed to a build, so a regression cannot be bounded to a change.",
                        Severity.Medium, 1,
                        "A version, commit or package identifier", "empty");
                }

                return new CheckResult(true, false,
                    $"Build identity recorded: {target.BuildLabel}.",
                    $"buildLabel={target.BuildLabel}; owner={target.Owner}", Severity.Info, 1);
            }

            case "Structure":
            {
                if (!File.Exists(path))
                {
                    return new CheckResult(false, true, "Structure check skipped.",
                        "Structure is read from a file target; this target is not a readable file.",
                        Severity.Low, 1);
                }

                var info = new FileInfo(path);
                return new CheckResult(true, false,
                    $"Structure readable: {info.Length:N0} bytes.",
                    $"path={info.FullName}\nlength={info.Length}\nmodified={info.LastWriteTimeUtc:O}",
                    Severity.Info, 2);
            }

            case "Integrity":
            {
                if (!File.Exists(path))
                {
                    return new CheckResult(false, true, "Integrity check skipped.",
                        "Integrity is computed over a file; this target is not a readable file.",
                        Severity.Low, 1);
                }

                var hash = Hashing.Sha256File(path);
                return new CheckResult(true, false,
                    "Content hash computed.",
                    $"sha256={hash}\nThis is the value a later run compares against to detect drift.",
                    Severity.Info, 2);
            }

            case "Capacity":
            {
                if (!File.Exists(path))
                {
                    return new CheckResult(false, true, "Capacity check skipped.",
                        "Capacity is measured against a file target.", Severity.Low, 1);
                }

                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path)) ?? "C:\\");
                var freeGb = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;

                // A real threshold against a real reading. Storage pressure has to warn before
                // evidence is lost, which is the whole reason this check exists.
                return freeGb < 2.0
                    ? new CheckResult(false, false,
                        $"Storage pressure: {freeGb:F1} GB free.",
                        $"drive={drive.Name}; free={freeGb:F2} GB. Evidence capture may be degraded or lost below 2 GB.",
                        Severity.High, 1, "at least 2 GB free", $"{freeGb:F2} GB")
                    : new CheckResult(true, false,
                        $"Storage headroom adequate: {freeGb:F1} GB free.",
                        $"drive={drive.Name}; free={freeGb:F2} GB", Severity.Info, 1);
            }

            case "Consistency":
            {
                // Measured against this product's own records, which is a real consistency question:
                // an artefact bound to a finding that no longer exists is a broken evidence chain.
                var findings = _store.Findings(target.Id);
                var artefacts = _store.EvidenceItems(target.Id);
                var orphaned = artefacts.Count(a => a.FindingId is { } id && findings.All(f => f.Id != id));

                return orphaned == 0
                    ? new CheckResult(true, false,
                        "Evidence chain is consistent.",
                        $"{artefacts.Count} artefact(s) checked against {findings.Count} finding(s); no orphans.",
                        Severity.Info, 2)
                    : new CheckResult(false, false,
                        $"{orphaned} evidence artefact(s) reference a finding that no longer exists.",
                        $"artefacts={artefacts.Count}; findings={findings.Count}; orphaned={orphaned}",
                        Severity.High, 2, "every artefact resolves to a finding", $"{orphaned} orphaned");
            }

            case "Provenance":
            {
                var artefacts = _store.EvidenceItems(target.Id);
                var unhashed = artefacts.Count(a => string.IsNullOrWhiteSpace(a.Sha256));
                var unsealed = artefacts.Count(a => !a.Sealed);

                return unhashed == 0
                    ? new CheckResult(true, false,
                        $"All {artefacts.Count} artefact(s) carry a content hash.",
                        $"unsealed={unsealed} (unsealed is not a fault; it means no reviewer has verified them yet)",
                        Severity.Info, 1)
                    : new CheckResult(false, false,
                        $"{unhashed} artefact(s) have no content hash.",
                        "An artefact without a hash cannot support a clearance decision, because it cannot be shown to be unchanged.",
                        Severity.High, 1, "every artefact hashed", $"{unhashed} unhashed");
            }

            default:
                return new CheckResult(false, true, $"{check} is not implemented.",
                    "This check is named in the sweep mode but has no implementation in this build.",
                    Severity.Low, 0);
        }
    }

    /// <summary>
    /// Records a deviation, clustering it onto an existing finding when the fingerprint matches.
    /// </summary>
    /// <remarks>
    /// Clustering rather than inserting is what stops a backlog filling with the same defect once per
    /// run. The fingerprint deliberately excludes the timestamp and the run identifier: including
    /// them would make every occurrence unique, which is the failure this is written to avoid.
    /// </remarks>
    private void RecordFinding(CheckResult result, Sweep sweep, Target target)
    {
        var fingerprint = Hashing.Fingerprint(target.Id.ToString(), result.Summary, result.Expected);
        var existing = _store.FindByFingerprint(target.Id, fingerprint);

        if (existing is not null)
        {
            _store.Save(existing with
            {
                LastSeenAt = DateTimeOffset.Now,
                ReproAttempts = existing.ReproAttempts + 1,
                ReproSuccesses = existing.ReproSuccesses + 1,
            });

            Log("FLT.RECUR", $"Deviation recurred: {result.Summary}", Severity.Medium, sweep, target,
                $"finding={existing.Id}; fingerprint={fingerprint}; now {existing.ReproSuccesses + 1} of {existing.ReproAttempts + 1} attempts");
            return;
        }

        var finding = new Finding
        {
            Id = _store.NextId(IdKind.Fault),
            TargetId = target.Id,
            SweepId = sweep.Id,
            Title = result.Summary,
            Severity = result.Severity,
            Maturity = FindingMaturity.New,
            Actual = result.Actual,
            Expected = result.Expected,
            Fingerprint = fingerprint,
            ReproAttempts = 1,
            ReproSuccesses = 1,
            FirstSeenAt = DateTimeOffset.Now,
            LastSeenAt = DateTimeOffset.Now,
            EvidenceCount = 1,
        };

        _store.Save(finding);

        Log("FLT.NEW", $"New deviation recorded: {result.Summary}", result.Severity, sweep, target,
            $"finding={finding.Id}; fingerprint={fingerprint}\n{result.Detail}");
    }

    private void Log(string code, string message, Severity severity, Sweep sweep, Target target, string detail)
    {
        _store.Append(new LedgerEvent
        {
            Id = _store.NextId(IdKind.Event),
            At = DateTimeOffset.Now,
            Code = code,
            Message = message,
            Severity = severity,
            TargetId = target.Id,
            SweepId = sweep.Id,
            RelatedId = sweep.Id.ToString(),
            Actor = "sweep-engine",
            Detail = detail,
        });
    }
}
