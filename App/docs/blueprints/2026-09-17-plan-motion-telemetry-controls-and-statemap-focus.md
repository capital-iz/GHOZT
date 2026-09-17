# Implementation Plan — Motion Layer, Telemetry Stream Controls & State Map Search-to-Focus

**Plan ID:** PLAN-2026-09-17-001  
**Governing Blueprint:** `2026-09-17-motion-telemetry-controls-and-statemap-focus.md`  

---

## 1. Planned Tasks

1. **GhoztStore.cs**:
   - Add `Severity? minSeverity = null` to `RecentEvents`.
   - Add SQL condition for severity threshold matching.

2. **ShellViewModel.cs**:
   - Add `StreamSeverityFilter` and `AutoScrollEnabled` properties.
   - Add `ExportStreamJsonCommand` and `ExportStreamNdjsonCommand`.
   - Pass severity filter to `Refresh()`.

3. **MainWindow.xaml & MainWindow.xaml.cs**:
   - Add Tray header controls for Severity Filter ComboBox and AutoScroll toggle.
   - Implement fluid entry animation in `ShowWorkspace(WorkspaceKind kind)`.

4. **BlackBoxStream.xaml & BlackBoxStream.xaml.cs**:
   - Implement context menu export actions.
   - Implement auto-scrolling on collection change.

5. **Generic.xaml**:
   - Enhance `RailButton` hover state styling with soft glow and subtle border highlight.

6. **StateMapWorkspace.xaml.cs**:
   - Add smooth `DoubleAnimation` on `LatticeTranslate.X` and `Y` in `FocusNode`.

7. **Tests & Packaging**:
   - Add unit tests in `RealExecutionTests.cs`.
   - Run tests, execute UI automation verification, and package MSI installer.
