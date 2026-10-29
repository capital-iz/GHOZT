# Ghozt — Autonomous App Testing and Black-Box Engine: Build Blueprint

| Field | Value |
|---|---|
| Document | Build blueprint (mechanism, alternatives, failure paths, contracts) |
| Product | Ghozt - Dev Tester · visible brand **Ghozt** · portfolio **CAPITAL-IZ Ghozt** |
| Scope | `Apps\Desktop\GHOZT\App` — `Ghozt.Core` & `Ghozt.App` (Process supervision, UIA driver, exploration loop, oracle engine, black-box recorder, Replay workspace) |
| Date | 2026-09-13 |
| Status | Architecture finalized; implementation in progress under /goal |
| Governance | Universal Design System v2.0 §12, Menial v.2 UI Canon, CAPITAL-IZ Execution Doctrine |

---

## 1. Executive Summary & Objective

Transform Ghozt from a static filesystem/integrity scanner into an autonomous, agentic end-to-end testing and forensics platform. Ghozt launches or attaches to target Windows desktop applications, drives them via Windows UI Automation (UIA) on a dedicated background MTA thread, navigates their features, evaluates real-time experience against oracles/contracts, fingerprints deviations, streams black-box telemetry (frames, actions, UI trees) into an immutable SQLite WAL ledger, and provides timeline scrubbing in the Replay workspace.

---

## 2. System Architecture & Components

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              GHOZT APPLICATION                              │
│                                                                             │
│  ┌────────────────────────┐  ┌───────────────────────────────────────────┐  │
│  │   Sweep / Controller   │  │             Replay Workspace              │  │
│  │  (Interactive Runner)  │  │  (Timeline scrubber, frame/tree viewer)   │  │
│  └───────────┬────────────┘  └─────────────────────▲─────────────────────┘  │
│              │                                     │                        │
├──────────────┼─────────────────────────────────────┼────────────────────────┤
│              ▼                                     │                        │
│  ┌─────────────────────────────────────────────────┴─────────────────────┐  │
│  │                    GHOZT.CORE FORENSICS ENGINE                        │  │
│  │                                                                       │  │
│  │  ┌──────────────────────┐  ┌────────────────┐  ┌───────────────────┐  │  │
│  │  │  TargetSupervisor    │  │ UiaDriver      │  │ OracleEngine      │  │  │
│  │  │  (Process lifecycle, │  │ (MTA worker,   │  │ (Crash, modal,    │  │  │
│  │  │   stdout/err watch)  │  │  UIA tree,     │  │  contract checks) │  │  │
│  │  └──────────┬───────────┘  │  safe inputs)  │  └─────────┬─────────┘  │  │
│  │             │              └───────┬────────┘            │            │  │
│  │             │                      │                     │            │  │
│  │             ▼                      ▼                     ▼            │  │
│  │  ┌─────────────────────────────────────────────────────────────────┐  │  │
│  │  │            AgenticExplorer (State Lattice Generator)            │  │  │
│  │  └─────────────────────────────────┬───────────────────────────────┘  │  │
│  │                                    │                                  │  │
│  │                                    ▼                                  │  │
│  │  ┌─────────────────────────────────────────────────────────────────┐  │  │
│  │  │         BlackBoxRecorder (Evidence & LedgerEvent Stream)        │  │  │
│  │  └─────────────────────────────────┬───────────────────────────────┘  │  │
│  │                                    ▼                                  │  │
│  │                       SQLite WAL Database Store                       │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
└───────────────────────────────────────┬─────────────────────────────────────┘
                                        │ (UIA / Win32 / Process)
                                        ▼
                       ┌─────────────────────────────────┐
                       │    TARGET WINDOWS APPLICATION   │
                       └─────────────────────────────────┘
```

### 2.1 Component Responsibilities

1. **`TargetSupervisor`**: Manages process creation via `Process.Start` with redirected I/O and window handle tracking. Monitors process health, memory footprint, exit codes, and sudden terminations.
2. **`UiaDriver`**: Runs on an isolated MTA background thread to prevent COM automation deadlocks with WPF's STA thread. Enumerates the target's visual UI tree (Window, Button, Edit, Menu, List), maps element bounding rectangles and automation IDs, and safely executes inputs (Invoke, SetText, Select) while enforcing a strict target window boundary box.
3. **`OracleEngine`**: Evaluates runtime behavior against expectations across three tiers:
   - *Tier 1: Fatal Oracles* — Target crash, unhandled exception, process exit, unresponsive window (hung message pump).
   - *Tier 2: Invariant Oracles* — Modal error popups, warning toasts, broken visual bindings, missing required elements.
   - *Tier 3: Contract Oracles* — Expected state transition rules, output text match, status code match.
4. **`AgenticExplorer`**: Drives systematic exploration through unvisited UI paths up to the user-defined `MaxStates` and `MaxDepth` budget. Generates `LatticeNode` and `LatticeEdge` transitions in real time.
5. **`BlackBoxRecorder`**: Captures compressed UI tree snapshots, action telemetry, and keyframe bitmaps into SQLite `LedgerEvent` and `Evidence` tables, indexed with SHA-256 hashes.
6. **`ReplayWorkspace`**: Replaces the placeholder with a synchronized forensic timeline player (frame scrubber, action inspector, element tree viewer, and defect markers).

---

## 3. Rejected Alternatives

| Alternative | Reason Rejected |
|---|---|
| Direct Win32 SendInput coordinate clicking | Brittle across display DPI scaling and window moves; lacks semantic element knowledge; risks clicking unintended background windows. |
| In-process DLL injection / Detours | Highly invasive, triggers antivirus heuristics (e.g. Norton), risks corrupting target memory, cannot test third-party or packaged apps cleanly. |
| Naive 60 FPS full-screen raster recording | Saturates SQLite WAL and disk I/O in seconds; wastes gigabytes on static screens. Replaced with action-triggered keyframes and structural UI tree diffs. |
| Single-threaded UIA in WPF dispatcher | Causes instantaneous shell deadlocks when target processes block their UI queues. Replaced with dedicated MTA worker thread and timeout cancellation. |

---

## 4. Failure Handling & Containment

| Failure | Detection | Recovery / Mitigation |
|---|---|---|
| Target app freezes / hangs | `SendMessageTimeout` / UIA response timeout (3s) | Recorded as an unresponsiveness deviation; logs thread state; halts run gracefully. |
| Target app crashes unexpectedly | `Process.Exited` event with non-zero exit code | Captured as Critical severity finding; dumps stderr and last 10 black-box actions. |
| Target window minimized / lost focus | Window state inspection in `UiaDriver` | Restores window focus; if unable, halts interaction to avoid stray input leakage. |
| Destructive action detected | Text/AutomationId matches destructive blacklist (e.g. "Delete", "Format", "Purge") | Blocked unless target has `AccessClass.Destructive` and sweep has `AllowDestructive=true`. |
| Excessive state space explosion | Visited states exceed `MaxStates` budget | Sweep concludes cleanly as `Partial`; all traversed states and findings preserved. |

---

## 5. Verification Plan

1. **Unit & Harness Tests**:
   - Process supervisor launch and termination tracking.
   - UIA tree parsing and element abstraction.
   - Oracle deviation detection (crash, error modal, contract failure).
   - Black-box recorder event hashing and persistence.
2. **End-to-End Integration Verification**:
   - Drive a real target application, record the full interaction trace into SQLite, and verify Replay workspace renders the timeline.
3. **Packaging**:
   - Clean MSI packaging via `iterate.ps1` with 0 test failures and zero uncommitted changes.
