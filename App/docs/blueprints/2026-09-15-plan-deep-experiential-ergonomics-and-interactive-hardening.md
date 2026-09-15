# GZT-PLN-017: Implementation Plan — Deep Experiential Ergonomics & Interactive Forensic Hardening

- **Governing Blueprint:** GZT-BLD-017
- **Date:** 2026-09-15
- **Branch:** `main`

---

## 1. Work Breakdown Structure

### Phase 1: State Map Node Search & Viewport Centering
- Edit `App/src/Ghozt.App/Workspaces/StateMapWorkspace.xaml`:
  - Add `NodeSearchBox` (`TextBox`) with `OnNodeSearchChanged` on the zoom/filter toolbar.
- Edit `App/src/Ghozt.App/Workspaces/StateMapWorkspace.xaml.cs`:
  - Implement `OnNodeSearchChanged(object sender, TextChangedEventArgs e)`.
  - Dim non-matching nodes, keep matches bright, auto-focus first match via `FocusNode()`.

### Phase 2: Fluid Default Selection & Non-Blocking Export in Faults
- Edit `App/src/Ghozt.App/Workspaces/FaultsWorkspace.xaml.cs`:
  - In `ApplyFilters()`, if `Queue.SelectedItem is null || !filtered.Contains((Finding)Queue.SelectedItem)`, set `Queue.SelectedIndex = 0`.
  - In `OnExportBugReport()`, eliminate `MessageBox.Show()`, set `ExportBugReportButton.Content = "Copied to Clipboard!"`, log to BlackBox ledger, and restore button text asynchronously.

### Phase 3: Fluid Default Selection in Evidence Vault
- Edit `App/src/Ghozt.App/Workspaces/EvidenceWorkspace.xaml.cs`:
  - In `Refresh()`, if `Items.SelectedItem is null || !shown.Contains((EvidenceItem)Items.SelectedItem)`, set `Items.SelectedIndex = 0`.

### Phase 4: Delta-Debugging Visual Token Triage
- Edit `App/src/Ghozt.App/Workspaces/AlgorithmsWorkspace.xaml.cs`:
  - In `OnRunMinimisation()`, build structured results with two columns:
    1. Isolated 1-minimal trigger subset.
    2. Pruned observational noise tokens.

### Phase 5: Test Verification & Mutation Testing
- Add unit tests in `App/tests/Ghozt.Tests/RealExecutionTests.cs`:
  - Test fluid default selection logic in `Faults` and `Evidence`.
  - Test node search matching and focusing.
- Perform mutation testing (Red $\rightarrow$ Green).

### Phase 6: Packaging, Installation & Live Forensic UI Automation
- Execute `Windows\iterate.ps1` to produce `Ghozt-1.0.8.msi`, install to Program Files, commit, and push.
- Execute `deep_forensic_drive.py` against sandboxed database, verifying end-to-end interactive drive without exceptions or modal locks.
- Capture all interactive screenshots.
