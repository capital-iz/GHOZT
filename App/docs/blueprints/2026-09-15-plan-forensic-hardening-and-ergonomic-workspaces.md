# Ghozt Implementation Plan — Forensic Hardening, Ergonomic Workspaces & Interactive Developer Laboratories
**Document ID:** GZT-PLN-016  
**Paired Blueprint:** GZT-BLD-016  
**Date:** 2026-09-15  

---

## 1. Scope of Work

1. **State Map Ergonomics**:
   - Add `BtnAutoArrange` button to toolbar.
   - Implement `OnAutoArrange` radial layout recalculation, SQLite persistence, and edge re-anchoring.
   - Expand `OnCanvasMouseDown` to support Right-Click canvas panning.
   - Attach node hover highlighting to incident lines in `BuildNode`.

2. **Faults Workspace Real-Time Filtering & Bug Export**:
   - Add Filter & Search bar: `SearchBox`, `SeverityFilter`, `StateFilter`.
   - Implement `ApplyFilters()` in `FaultsWorkspace.xaml.cs`.
   - Add `[ Export Bug Report ]` button and clipboard serialization logic.
   - Connect `OnReproduce` to in-process execution.

3. **Algorithms Laboratory Interactive Delta-Debugging**:
   - Add custom multiline input editor, presets, and condition picker to Tab 3 in `AlgorithmsWorkspace.xaml`.
   - Implement dynamic token extraction and execution via `AlgorithmHarness.MinimizeInput`.

4. **Evidence Workspace**:
   - Expand `KindFilter` in `EvidenceWorkspace.xaml` with `ui:screenshot/png`, `application/json`, `report:sarif`, `report:junit`.

5. **Automated Verification & Packaging**:
   - Add test coverage in `RealExecutionTests.cs` and perform mutation testing.
   - Run `Windows\iterate.ps1` to produce `Ghozt-1.0.7.msi` and update installation.
