# Build Blueprint: Replay Telemetry Fallback, Non-Visual Payload Canvas, and Cross-Flow Synthesis (GZT-BLD-019)

**Document ID**: GZT-BLD-019  
**Status**: ACTIVE / VERIFIED  
**Date**: 2026-09-15  
**Product**: CAPITAL-IZ Ghozt — Dev Tester  
**Subsystem**: Replay Workspace & Cross-Flow Ergonomics  

---

## 1. System Structure

### 1.1 Architecture & Core Components
```
   +-------------------------------------------------------------------------+
   |                            GHOZT CORE STORE                             |
   |                                                                         |
   |   [ Sweeps ]          [ Evidence Items ]           [ Ledger Events ]    |
   +--------+-----------------------+---------------------------+------------+
            |                       |                           |
            v                       v                           v
   +-------------------------------------------------------------------------+
   |                        REPLAY TIMELINE SYNTHESIZER                      |
   |                                                                         |
   | 1. Query screenshots (Kind: ui:screenshot/png)                          |
   | 2. FALLBACK 1: Query structured evidence (logs, HTTP, traces, proofs)   |
   | 3. FALLBACK 2: Query ledger events (Kind, Severity, Summary, Detail)    |
   | 4. Produce Unified ReplayStepModel sequence                             |
   +------------------------------------+------------------------------------+
                                        |
                                        v
   +-------------------------------------------------------------------------+
   |                        DUAL-MODALITY REPLAY CANVAS                      |
   |                                                                         |
   | +----------------------------------+----------------------------------+ |
   | | Visual Frame (BitmapImage)       | Monospace Telemetry Card (JSON)  | |
   | | [When PNG frame is present]      | [When non-visual payload exists] | |
   | +----------------------------------+----------------------------------+ |
   +-------------------------------------------------------------------------+
```

### 1.2 Entities & Models
- `ReplayStepModel`:
  - `StepIndex`: Zero-based timeline sequence index.
  - `ActionDescription`: High-level step summary.
  - `Timestamp`: UTC / local event timestamp.
  - `ScreenshotEvidence`: Primary evidence item reference (nullable).
  - `TreeEvidence`: Associated UI element snapshot (nullable).
  - `UiTree`: Parsed UI hierarchy tree (nullable).
  - `Oracles`: Evaluated invariant outcomes.
  - `StateId`: Node or state identifier (`state:001` or target ID).
  - `TargetName`: Attached target identifier.
  - `Kind`: Classification tag (e.g. `ui:screenshot/png`, `telemetry:event`, `probe:reachability`).
  - `Success`: Boolean execution status (derived from error state/severity).
  - `Error`: Error message or trace if execution faulted.
  - `DurationMs`: Recorded step execution latency.
  - `NonVisualPayload`: Raw text, JSON, or formatting payload for non-visual frames.
  - `HasImage`: Flag indicating if a visual bitmap is present.

---

## 2. Flow Design

### 2.1 Timeline Ingestion Flow
1. Operator or navigation service selects a completed Sweep.
2. `LoadSweep(Sweep? sweep)` queries evidence items from `GhoztStore`.
3. If `screenshots.Count > 0`:
   - Timelines are constructed from PNG frames and nearest UI tree snapshots.
   - `HasImage = true`.
4. If `screenshots.Count == 0 && allEvidence.Count > 0`:
   - Timelines are synthesized from non-visual evidence records.
   - `HasImage = false`, `NonVisualPayload = evidence.Payload`.
5. If `screenshots.Count == 0 && allEvidence.Count == 0 && events.Count > 0`:
   - Timelines are synthesized from ledger events.
   - `HasImage = false`, `NonVisualPayload = event.Detail ?? event.Summary`.
6. Center canvas inspects `HasImage`:
   - `true` -> Renders `ScreenshotImage`.
   - `false` -> Renders `NonVisualPayloadCard` with monospace text viewer and Kind chip.

### 2.2 Cross-Workspace Navigation Flow
1. Sweep completes in `SweepWorkspace`.
2. `PostSweepPanel` becomes visible with action buttons wrapped in a responsive `WrapPanel`.
3. Operator clicks `Replay Execution` (`BtnPostSweepReplay`).
4. Dispatch calls `Shell.NavigateToReplay(_lastSweepId)`.
5. Shell switches workspace to `Replay` and passes `RequestedSweepId`.
6. `ReplayWorkspace` attaches, auto-selects the sweep, loads steps, and renders Step 1.

---

## 3. Constraints & Invariants

1. **Zero Silent Collapse**: A completed sweep must never collapse to `No Replay Data Available` if evidence or event telemetry exists.
2. **Token Engine Purity**: All canvas styles, background fills, and borders must strictly resolve from dynamic resources (`Surface.Deep`, `Border.Subtle`, `Font.Mono`).
3. **Responsive Reflow**: All command headers with multiple action buttons (Regressions, Sweep, Reports, Clearance) must utilize `WrapPanel` layout to prevent clipping on constrained displays.
4. **Sandboxed Isolation**: Automated tests and captures must run against temporary SQLite instances, preserving production profile databases intact.

---

## 4. Failure Handling

1. **Corrupt Payload String**: Handled by graceful text fallback without raising unhandled exceptions.
2. **Missing Sweep Reference**: Clears scrubber, resets step counter to `0 / 0`, and displays friendly empty placeholder.
3. **Dispatcher Collision**: Button temporary confirmation timers are bounded to 2.5 seconds and operate cleanly on the WPF UI dispatcher.

---

## 5. Scalability & Verification

- **Step Cap**: Event telemetry querying is bounded to 1,000 recent items, preventing memory bloat on extensive stress runs.
- **Verification Evidence**: Verified via unit tests (`RealExecutionTests.cs`), mutation testing, MSI installation, and automated UI Automation drive.
