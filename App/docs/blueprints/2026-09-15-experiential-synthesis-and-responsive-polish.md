# GZT-BLD-018: Experiential Synthesis, Responsive Polish & Deep Forensic Hardening

- **Document ID:** GZT-BLD-018
- **Date:** 2026-09-15
- **Author:** Antigravity (Capital-Iz Systems Architect & Execution Engineer)
- **Status:** Approved
- **Governing Phase:** Phase 5 — Full Experiential Synthesis, Cross-Workspace Telemetry & Responsive Visual Polish

---

## 1. Context & Operational Intent

Static code compilation and unit test passes represent baseline verification, but practical usage reveals layout truncations, unrendered states, and workflow dead-ends. Through automated hands-on screenshot scrutiny of Ghozt 1.0.8, several critical experiential defects were identified:
1. **Clearance Gate Latency**: Navigating to `ClearanceWorkspace` required explicit button clicks before evaluating and rendering the 12 policy gates, leaving an empty card on entry.
2. **Horizontal Action Truncations**: Rigid `StackPanel` containers in `FaultsWorkspace`, `ClearanceWorkspace`, and `ReportsWorkspace` caused trailing actions (`Assign to Issue Tracker`, `Export Certificate`, `Portfolio Rollup`) to truncate on standard displays.
3. **Card Title Clipping**: In `TargetsWorkspace`, target names such as `Capital-Iz Ghozt Core` were truncated to `Capital-Iz Ghozt Cor` due to rigid header column constraints.
4. **Viewport Clipping in Algorithms Lab**: Outcome cards for Delta-Debugging minimization lacked vertical scroll containers, clipping pruned noise lists.
5. **Evidence Toolbar Cramming**: `KindFilter` ComboBox width caused text clipping (`All Kin...`) alongside search and completeness ratio indicators.

---

## 2. Architectural Mechanisms

### 2.1 Auto-Evaluation & Lifecycle Hydration (`ClearanceWorkspace`)
- **Mechanism**: The workspace subscribes to the WPF `Loaded` event. When loaded with an active `Shell.Target`, it automatically triggers `OnEvaluate()`, rendering:
  - Top-level Verdict Card (Cleared/Refused badge, target identity, build version, pass ratio).
  - All 12 Gate Cards (`gate.Passed` $\rightarrow$ `State.Verified`, `gate.Mandatory` $\rightarrow$ `Authority.Clearance`, failed $\rightarrow$ `State.Fault`).
  - Dynamic Remediation Action Buttons linking directly to Faults, Evidence, Reports, or Sweep.
- **Responsive Command Bar**: Action buttons are arranged in a wrapping layout or flexible grid, preventing horizontal clipping of `ClearanceSummaryText`.

### 2.2 Responsive Action Toolbars & Un-clipped Labels (`FaultsWorkspace`)
- **Mechanism**: Replace rigid `StackPanel` with `WrapPanel` on top command strips.
- Expand `FORENSIC CROSS-NAVIGATION` button widths and remove rigid cell constraints, rendering full action text (`Replay Trace`, `Locate on Map`, `View Evidence`).
- Widen `SeverityFilter` and `StateFilter` to `130px`, ensuring "All Severities" and "All States" render with zero clipping.

### 2.3 Fluid Header Layout (`TargetsWorkspace`)
- **Mechanism**: Re-architect target card header layout with flexible star-sizing columns and auto-sizing badge containers, ensuring full brand title rendering (`Capital-Iz Ghozt Core`) before status badges.

### 2.4 Laboratory Viewport Scroll Containment (`AlgorithmsWorkspace`)
- **Mechanism**: Tab 3 ("Delta-Debugging Lab") content is enclosed within a dedicated `<ScrollViewer VerticalScrollBarVisibility="Auto">`. Outcome cards expand dynamically without window boundary clipping.

### 2.5 Tactile Ingestion Feedback & Spacing (`EvidenceWorkspace`)
- **Mechanism**: `KindFilter` ComboBox widened to `140px`.
- `OnVerifyHash` and `OnVerifyAll` update button content with inline visual verification (`"Hashes Verified!"`) with auto-revert timer, and record immutable BlackBox audit events.

### 2.6 Auto-Dossier Synthesis (`ReportsWorkspace`)
- **Mechanism**: Subscribes to `Loaded` event; if `Shell.Target` is active, automatically synthesizes an Executive Dossier Summary card (Sweeps count, open findings by severity, cryptographic evidence count, clearance seal) with one-click export links.

---

## 3. Enumerated Invariants & Quality Gates

1. **Zero Live Profile Pollution**: Sandboxed temporary databases exclusively used for all captures and tests.
2. **Zero Uncommitted Changes**: All changes packaged and committed via `iterate.ps1`.
3. **Zero Lingering Background Tasks**: Background processes, crons, and subagents cleanly terminated before turn completion.
4. **Mutation Testing**: Red $\rightarrow$ Green test assertion cycle verified.
5. **Observational UI Automation**: Verified hands-on via automated driving script (`deep_forensic_drive.py`).
