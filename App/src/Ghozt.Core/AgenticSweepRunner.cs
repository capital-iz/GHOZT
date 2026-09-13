using System.Diagnostics;

namespace Ghozt.Core;

/// <summary>
/// Autonomous end-to-end testing engine that exercises target applications,
/// discovers and executes UI actions via UI Automation, evaluates real-time oracles,
/// and preserves black-box forensic telemetry.
/// </summary>
public sealed class AgenticSweepRunner
{
    private readonly GhoztStore _store;
    private readonly IUiaDriver _uiaDriver;
    private readonly BlackBoxRecorder _recorder;

    public AgenticSweepRunner(GhoztStore store, IUiaDriver? uiaDriver = null, BlackBoxRecorder? recorder = null)
    {
        _store = store;
        _uiaDriver = uiaDriver ?? new UiaDriver();
        _recorder = recorder ?? new BlackBoxRecorder(store);
    }

    /// <summary>
    /// Executes an autonomous agentic sweep against the target application.
    /// </summary>
    public async Task<Sweep> RunAgenticSweepAsync(
        Target target,
        Sweep sweep,
        IProgress<SweepProgress>? progress,
        CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var executablePath = ResolveExecutablePath(target);

        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            var blockedSweep = sweep with
            {
                State = RunState.Blocked,
                StartedAt = DateTimeOffset.Now,
                FinishedAt = DateTimeOffset.Now,
                Outcome = $"Target executable not found or unspecified: '{executablePath}'. Configure LaunchCommand or BaseAddress.",
            };
            _store.Save(blockedSweep);
            _store.Append(new LedgerEvent
            {
                Id = _store.NextId(IdKind.Event),
                At = DateTimeOffset.Now,
                Code = "SWP.BLOCKED",
                Message = blockedSweep.Outcome,
                Severity = Severity.Medium,
                TargetId = target.Id,
                SweepId = sweep.Id,
                Actor = "AgenticSweepRunner",
                Detail = $"Configured LaunchCommand: '{target.LaunchCommand}', BaseAddress: '{target.BaseAddress}'",
            });
            return blockedSweep;
        }

        var currentSweep = sweep with
        {
            State = RunState.Running,
            StartedAt = DateTimeOffset.Now,
        };
        _store.Save(currentSweep);

        _store.Append(new LedgerEvent
        {
            Id = _store.NextId(IdKind.Event),
            At = DateTimeOffset.Now,
            Code = "SWP.START",
            Message = $"Autonomous examination started for target '{target.Name}' using '{executablePath}'",
            Severity = Severity.Info,
            TargetId = target.Id,
            SweepId = sweep.Id,
            Actor = "AgenticSweepRunner",
            Detail = $"MaxStates: {sweep.MaxStates}, MaxSeconds: {sweep.MaxSeconds}s, AllowDestructive: {sweep.AllowDestructive}",
        });

        using var supervisor = new TargetSupervisor();
        var visitedStates = 0;
        var deviationsFound = 0;
        var evidenceCaptured = 0;
        var executedActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            progress?.Report(new SweepProgress("Launching", 0, sweep.MaxStates, 0, 0, clock.Elapsed, $"Launching {Path.GetFileName(executablePath)}…"));

            var launched = await supervisor.LaunchAsync(executablePath, "", Path.GetDirectoryName(executablePath) ?? "", cancellationToken).ConfigureAwait(false);
            if (!launched)
            {
                throw new InvalidOperationException($"Failed to spawn target process: {executablePath}");
            }

            var windowHandle = await supervisor.WaitForMainWindowAsync(TimeSpan.FromSeconds(8), cancellationToken).ConfigureAwait(false);

            // Step 0: Initial State Capture
            visitedStates++;
            progress?.Report(new SweepProgress("Initial State", visitedStates, sweep.MaxStates, deviationsFound, evidenceCaptured, clock.Elapsed, "Capturing baseline UI surface…"));

            var initialTree = await _uiaDriver.CaptureTreeAsync(windowHandle, sweep.MaxDepth).ConfigureAwait(false);
            var initialOracles = OracleEngine.Evaluate(supervisor, initialTree);

            var initialStep = _recorder.RecordStep(
                stepIndex: 0,
                target: target,
                sweep: sweep,
                stateId: "state:root",
                actionDescription: "Application launched and baseline window captured",
                windowHandle: windowHandle,
                uiTree: initialTree,
                oracleResults: initialOracles);

            evidenceCaptured += 2; // Screenshot + UI Tree

            _store.Save(new LatticeNode
            {
                Id = "state:root",
                TargetId = target.Id,
                Label = initialTree?.Name ?? target.Name,
                Kind = NodeKind.System,
                State = "Observed",
                Provenance = "Observed",
                ParentId = "",
                VisitCount = 1,
                Detail = $"Process PID: {supervisor.ProcessId}, Window: {windowHandle}",
                EvidenceCount = 2,
                LastSeenAt = DateTimeOffset.Now,
            });

            foreach (var oracle in initialOracles.Where(o => !o.Passed))
            {
                deviationsFound++;
                RecordFinding(target, sweep, oracle, "state:root");
            }

            var currentStateId = "state:root";

            // Autonomous Exploration Loop
            while (!cancellationToken.IsCancellationRequested &&
                   visitedStates < sweep.MaxStates &&
                   clock.Elapsed.TotalSeconds < sweep.MaxSeconds &&
                   supervisor.IsRunning)
            {
                // Capture active UI tree
                var currentTree = await _uiaDriver.CaptureTreeAsync(windowHandle, sweep.MaxDepth).ConfigureAwait(false);
                if (currentTree is null)
                {
                    await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // Discover actions
                var availableActions = await _uiaDriver.DiscoverActionsAsync(currentTree, sweep.AllowDestructive).ConfigureAwait(false);
                var candidateAction = availableActions.FirstOrDefault(a => !executedActions.Contains(GetActionKey(a)));

                if (candidateAction is null)
                {
                    // No unvisited actions on current surface
                    _store.Append(new LedgerEvent
                    {
                        Id = _store.NextId(IdKind.Event),
                        At = DateTimeOffset.Now,
                        Code = "SWP.SURFACE_EXHAUSTED",
                        Message = $"All {availableActions.Count} discoverable UI actions on this surface have been explored.",
                        Severity = Severity.Info,
                        TargetId = target.Id,
                        SweepId = sweep.Id,
                        Actor = "AgenticSweepRunner",
                    });
                    break;
                }

                var actionKey = GetActionKey(candidateAction);
                executedActions.Add(actionKey);

                progress?.Report(new SweepProgress(
                    "Executing Action",
                    visitedStates,
                    sweep.MaxStates,
                    deviationsFound,
                    evidenceCaptured,
                    clock.Elapsed,
                    $"Exercising {candidateAction.Kind} on '{candidateAction.TargetName}'…"));

                // Execute action
                var actionResult = await _uiaDriver.ExecuteActionAsync(windowHandle, candidateAction, sweep.AllowDestructive).ConfigureAwait(false);

                // Wait for UI to settle
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);

                // Capture post-action UI tree and evaluate oracles
                var postTree = await _uiaDriver.CaptureTreeAsync(windowHandle, sweep.MaxDepth).ConfigureAwait(false);
                var postOracles = OracleEngine.Evaluate(supervisor, postTree, candidateAction, actionResult);

                visitedStates++;
                var nextStateId = $"state:{visitedStates:D3}";
                var actionNodeId = $"action:{candidateAction.Kind}:{candidateAction.TargetId}";

                var step = _recorder.RecordStep(
                    stepIndex: visitedStates,
                    target: target,
                    sweep: sweep,
                    stateId: nextStateId,
                    actionDescription: candidateAction.Description,
                    windowHandle: windowHandle,
                    uiTree: postTree,
                    actionIntent: candidateAction,
                    actionResult: actionResult,
                    oracleResults: postOracles);

                evidenceCaptured += (postTree is not null ? 2 : 1);

                // Map into State Lattice
                _store.Save(new LatticeNode
                {
                    Id = actionNodeId,
                    TargetId = target.Id,
                    Label = $"{candidateAction.Kind} {candidateAction.TargetName}",
                    Kind = NodeKind.Action,
                    State = actionResult.Success ? "Verified" : "Deviation",
                    Provenance = "Observed",
                    ParentId = currentStateId,
                    VisitCount = 1,
                    Detail = candidateAction.Description,
                    EvidenceCount = 1,
                    LastSeenAt = DateTimeOffset.Now,
                });

                _store.Save(new LatticeEdge
                {
                    TargetId = target.Id,
                    FromId = currentStateId,
                    ToId = actionNodeId,
                    Label = candidateAction.Kind.ToString(),
                    Relationship = "observed",
                    TraversalCount = 1,
                });

                _store.Save(new LatticeNode
                {
                    Id = nextStateId,
                    TargetId = target.Id,
                    Label = postTree?.Name ?? $"State {visitedStates}",
                    Kind = NodeKind.DataState,
                    State = postOracles.Any(o => !o.Passed) ? "Deviation" : "Verified",
                    Provenance = "Observed",
                    ParentId = actionNodeId,
                    VisitCount = 1,
                    Detail = $"Discovered after {candidateAction.Kind} on '{candidateAction.TargetName}'",
                    EvidenceCount = 1,
                    LastSeenAt = DateTimeOffset.Now,
                });

                _store.Save(new LatticeEdge
                {
                    TargetId = target.Id,
                    FromId = actionNodeId,
                    ToId = nextStateId,
                    Label = "transitions_to",
                    Relationship = "observed",
                    TraversalCount = 1,
                });

                // Record Findings for oracle deviations
                foreach (var oracle in postOracles.Where(o => !o.Passed))
                {
                    deviationsFound++;
                    RecordFinding(target, sweep, oracle, actionNodeId);
                }

                currentStateId = nextStateId;
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation handled cleanly below
        }
        catch (Exception ex)
        {
            _store.Append(new LedgerEvent
            {
                Id = _store.NextId(IdKind.Event),
                At = DateTimeOffset.Now,
                Code = "SWP.ERROR",
                Message = $"Agentic sweep encountered an unhandled execution error: {ex.Message}",
                Severity = Severity.Critical,
                TargetId = target.Id,
                SweepId = sweep.Id,
                Actor = "AgenticSweepRunner",
                Detail = ex.ToString(),
            });
        }
        finally
        {
            // Graceful teardown
            await supervisor.TerminateAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }

        var finalRunState = cancellationToken.IsCancellationRequested
            ? RunState.Cancelled
            : visitedStates >= sweep.MaxStates || clock.Elapsed.TotalSeconds >= sweep.MaxSeconds
                ? RunState.Partial
                : RunState.Completed;

        var outcome = finalRunState switch
        {
            RunState.Cancelled => "Sweep cancelled by operator. All black-box evidence and steps are preserved.",
            RunState.Partial => $"Budget exhausted at {visitedStates}/{sweep.MaxStates} states ({clock.Elapsed.TotalSeconds:F1}s).",
            _ when deviationsFound > 0 => $"Completed with {deviationsFound} deviation{(deviationsFound == 1 ? "" : "s")}. Evidence sealed.",
            _ => "Completed successfully. All exercised functions satisfied oracles with cryptographic evidence.",
        };

        var completedSweep = currentSweep with
        {
            State = finalRunState,
            FinishedAt = DateTimeOffset.Now,
            StatesVisited = visitedStates,
            DeviationsFound = deviationsFound,
            EvidenceCaptured = evidenceCaptured,
            Outcome = outcome,
        };

        _store.Save(completedSweep);
        _store.Save(target with { LastSweepAt = DateTimeOffset.Now });

        _store.Append(new LedgerEvent
        {
            Id = _store.NextId(IdKind.Event),
            At = DateTimeOffset.Now,
            Code = "SWP.END",
            Message = $"Sweep {sweep.Id} ended in state {finalRunState}: {outcome}",
            Severity = deviationsFound > 0 ? Severity.Medium : Severity.Info,
            TargetId = target.Id,
            SweepId = sweep.Id,
            Actor = "AgenticSweepRunner",
            Detail = $"Visited: {visitedStates}, Deviations: {deviationsFound}, Evidence: {evidenceCaptured}, Duration: {clock.Elapsed.TotalSeconds:F1}s",
        });

        progress?.Report(new SweepProgress("Finished", visitedStates, sweep.MaxStates, deviationsFound, evidenceCaptured, clock.Elapsed, outcome));

        return completedSweep;
    }

    private void RecordFinding(Target target, Sweep sweep, OracleResult oracle, string nodeId)
    {
        var existing = _store.FindByFingerprint(target.Id, oracle.Fingerprint);
        if (existing is not null)
        {
            _store.Save(existing with
            {
                ReproAttempts = existing.ReproAttempts + 1,
                ReproSuccesses = existing.ReproSuccesses + 1,
                LastSeenAt = DateTimeOffset.Now,
                EvidenceCount = existing.EvidenceCount + 1,
            });
        }
        else
        {
            _store.Save(new Finding
            {
                Id = _store.NextId(IdKind.Fault),
                TargetId = target.Id,
                SweepId = sweep.Id,
                Title = oracle.Title,
                Severity = oracle.Severity,
                Maturity = FindingMaturity.New,
                Actual = oracle.Actual,
                Expected = oracle.Expected,
                Fingerprint = oracle.Fingerprint,
                ReproAttempts = 1,
                ReproSuccesses = 1,
                NodeId = nodeId,
                Owner = target.Owner,
                FirstSeenAt = DateTimeOffset.Now,
                LastSeenAt = DateTimeOffset.Now,
                EvidenceCount = 1,
            });
        }
    }

    private static string GetActionKey(UiActionIntent intent) =>
        $"{intent.Kind}:{intent.TargetId}:{intent.TargetName}:{intent.InputValue}";

    public static string ResolveExecutablePath(Target target)
    {
        if (!string.IsNullOrWhiteSpace(target.LaunchCommand))
        {
            var parts = target.LaunchCommand.Split(' ', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0 && File.Exists(parts[0]))
            {
                return parts[0];
            }
        }

        if (!string.IsNullOrWhiteSpace(target.BaseAddress))
        {
            if (File.Exists(target.BaseAddress))
            {
                return target.BaseAddress;
            }
        }

        return "";
    }
}
