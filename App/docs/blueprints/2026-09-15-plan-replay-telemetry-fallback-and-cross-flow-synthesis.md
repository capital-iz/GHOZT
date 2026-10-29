# Implementation Plan Blueprint: Replay Telemetry Fallback, Non-Visual Payload Canvas, and Cross-Flow Synthesis (GZT-PLN-019)

**Document ID**: GZT-PLN-019  
**Status**: ACTIVE / VERIFIED  
**Date**: 2026-09-15  
**Governing Blueprint**: GZT-BLD-019 (`2026-09-15-replay-telemetry-fallback-and-cross-flow-synthesis.md`)  
**Product**: CAPITAL-IZ Ghozt — Dev Tester  

---

## 1. Objectives & Scope

1. **Replay Fallback**: Upgrade `ReplayWorkspace` so sweeps without screenshot frames (such as API contracts, CLI executions, smoke tests) produce fully interactive, scrubbable timeline steps from structured evidence and ledger events.
2. **Dual-Modality Canvas**: Introduce `NonVisualPayloadCard` in `ReplayWorkspace.xaml` so structured payloads, JSON, traces, and headers display directly in the central inspection canvas with monospace typography.
3. **Regressions Reflow**: Replace rigid horizontal `StackPanel` in `RegressionsWorkspace.xaml` with `WrapPanel` so action buttons reflow without clipping.
4. **Tactile Confirmations**: Add temporary button confirmations ("Exported!", "Analyzed!") in `RegressionsWorkspace.xaml.cs`.
5. **Cross-Flow Sweep Replay**: Provide immediate post-sweep replay navigation via `BtnPostSweepReplay` in `SweepWorkspace`.

---

## 2. Step-by-Step Implementation Sequence

1. **ReplayWorkspace.xaml.cs**:
   - Add `NonVisualPayload` (string) and `HasImage` (bool) to `ReplayStepModel`.
   - Update `LoadSweep`: if `screenshots.Count == 0`, fall back to `allEvidence.OrderBy(e => e.CapturedAt)` or `events.OrderBy(ev => ev.At)`.
   - Update `ShowStep`: toggle `ScreenshotImage` vs `NonVisualPayloadCard` based on `step.HasImage` and presence of `NonVisualPayload`.
   - Update `UpdateComparison`: compute step count from evidence count when screenshot count is 0.

2. **ReplayWorkspace.xaml**:
   - Inside Row 1 canvas `ScrollViewer`, add `NonVisualPayloadCard` border with structured header, kind tag, and monospace `TextBox`.

3. **RegressionsWorkspace.xaml & .cs**:
   - Convert command header button row to `WrapPanel`.
   - Add `ShowTemporaryButtonConfirmation` helper method.
   - Wire confirmation on `OnExportSuite` and `OnAnalyzeChangeImpact`.

4. **SweepWorkspace.xaml & .cs**:
   - Convert `PostSweepPanel` button row to `WrapPanel`.
   - Add `BtnPostSweepReplay` button.
   - Implement `OnPostSweepReplay` handler dispatching `Shell.NavigateToReplay(_lastSweepId)`.

5. **Test Suite Verification**:
   - Add `WorkspacesExperientialPhase6ReplayFallbackAndRegressionsPolishExist` to `RealExecutionTests.cs`.
   - Mutation-test assertion.
   - Run `dotnet test App/tests/Ghozt.Tests/Ghozt.Tests.csproj -c Release`.

6. **Packaging & Forensic Drive**:
   - Run `iterate.ps1` with `-Bump patch`.
   - Run `deep_forensic_drive.py` and inspect screenshots.
