# Implementation Plan — Target Operations Console & State Map Causal Failure Tracing

**Plan ID:** PLAN-2026-09-17-002  
**Date:** 2026-09-17  
**Paired Blueprint:** GZT-UX-003  

---

## 1. TargetsWorkspace Two-Pane Layout
- In `TargetsWorkspace.xaml`:
  - Split Row 1 into 2 columns: Column 0 (`Width="*"`) for targets card list; Column 1 (`Width="480"`) for `TargetOperationsPanel`.
  - Wire `TargetOperationsPanel` visibility to `HasTarget`.
  - Add summary cards: Runtime Telemetry, Lattice Metrics, Defect Breakdown, and Action Launchpad.
- In `TargetsWorkspace.xaml.cs`:
  - Wire `Refresh()` to populate `TargetOperationsPanel` metrics from `GhoztStore` (e.g. node count, edge count, findings count, sweep count).
  - Wire buttons: Fast Sweep, Explore Lattice, Triage Deviations, View Evidence, Generate Clearance Dossier.

## 2. StateMap Failure Path Tracing & Traversal Density
- In `StateMapWorkspace.xaml`:
  - Add `BtnTraceFailure` button in the toolbar: "Trace Failure Chain".
  - Add `ToggleHeatmap` button in the toolbar: "Traversal Density".
- In `StateMapWorkspace.xaml.cs`:
  - In `OnTraceFailureClick`: find path from root to first fault node via BFS/Dijkstra.
  - Animate camera to focus on fault node; highlight path edges with dashed stroke and fault color.
  - In `OnToggleHeatmapClick`: toggle traversal density coloring on edges.

## 3. Automated Tests & Verification
- Add integration tests in `RealExecutionTests.cs` validating:
  - `TargetsWorkspace` markup contracts: `TargetOperationsPanel`, launchpad buttons, metrics bindings.
  - `StateMapWorkspace` markup contracts: `BtnTraceFailure`, `ToggleHeatmap`.
  - `StateSpaceMath` causal failure path resolution.
- Run `dotnet test` (ensure 100% pass).
- Execute sandboxed visual capture proofs and inspect artifacts.
