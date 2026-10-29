# Ghozt Dev Tester — Target Operations Console & State Map Causal Failure Tracing

**Blueprint ID:** GZT-UX-003  
**Date:** 2026-09-17  
**Status:** Approved  
**Author:** CAPITAL-IZ Systems Architecture  

---

## 1. System Structure

### 1.1 Target Operations Mission-Control Console (GZT-UX-001 §05 & §06)
- **Host Component:** TargetsWorkspace.xaml and TargetsWorkspace.xaml.cs.
- **Layout Architecture:** Responsive two-pane master-detail arrangement:
  - **Left Pane:** Filterable, searchable cards list for registered targets.
  - **Right Pane:** TargetOperationsPanel activated when Shell.Target is selected:
    - **Identity & Authority Header:** Formal name, Ghozt ID, Kind, Environment chip, Access level, Authority grant badge.
    - **Runtime & Telemetry Card:** Endpoint/address, reachable latency badge, process binding status, launch command.
    - **Lattice & Exploration Metrics:** Discovered nodes count, state transitions mapped, frontier states remaining, path coverage %.
    - **Defect & Risk Distribution:** Severity pill breakdown (Critical, High, Medium, Low), active finding summary.
    - **Action Launchpad:** Immediate cross-workspace deep-linking:
      - Launch & Attach
      - Run Targeted Fast Sweep (pre-selects target in SweepWorkspace)
      - Explore State Lattice (navigates to StateMapWorkspace)
      - Triage Deviations (navigates to FaultsWorkspace)
      - Verify Evidence Vault (navigates to EvidenceWorkspace)
      - Generate Clearance Dossier (navigates to ReportsWorkspace)

### 1.2 State Map Causal Failure Tracing & Traversal Density Heatmap (Patent §14, §15 & GZT-UX-001 §13)
- **Host Component:** StateMapWorkspace.xaml and StateMapWorkspace.xaml.cs.
- **Causal Failure Path Tracing:**
  - Traversal engine finds causal graph paths from initial state (INIT.START / root) to target fault states (State == 'Fault').
  - Path edges are rendered with animated dash arrays (StrokeDashArray='4,2'), thicker stroke (3.0), and pulsing fault tint (State.Fault).
  - Interactive breadcrumbs display the causal path sequence across the top of the lattice canvas.
- **Traversal Density Heatmap:**
  - Toggleable heatmap visualization scaling edge thickness (1.0 to 4.0) and opacity based on TraversalCount / MaxTraversalCount.
  - Color gradient dynamically reflects hot paths vs unvisited frontiers.

---

## 2. Constraints & Invariants
- **Design System Invariant:** All visual tokens must resolve through the CAPITAL-IZ 12-palette token engine. No hardcoded literal hex colors in XAML.
- **Three-State Honesty Invariant (Doctrine §9):** Absence of evidence or unmeasured metrics must display as UNVERIFIED or UNKNOWN, never defaulted to zero or green.
- **Zero Live Contamination (Doctrine §25.2):** All test and capture suites run against isolated sandbox databases.
- **Performance Budget:** Causal path computation must complete in < 5ms for lattices up to 1,000 nodes.
