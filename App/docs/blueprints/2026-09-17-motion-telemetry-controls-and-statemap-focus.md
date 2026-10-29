# Ghozt Dev Tester — Motion Layer, Telemetry Stream Controls & State Map Search-to-Focus

**Blueprint ID:** GZT-UX-002  
**Date:** 2026-09-17  
**Status:** Approved  
**Author:** CAPITAL-IZ Systems Architecture  

---

## 1. System Structure

### 1.1 Motion Layer & Fluid Workspace Transitions (GZT-UX-001 §13)
- **Canvas Transition Host:** `MainWindow.xaml` and `MainWindow.xaml.cs`.
- **Mechanism:** DoubleAnimation on `Opacity` (0.0 to 1.0, 180ms) and `TranslateTransform.Y` (6.0 to 0.0, 180ms with `CubicEase` EaseOut).
- **Rail Button Hover Feedback:** `Generic.xaml` `RailButton` template updated with soft border glow (`Accent.BorderHover`) and subtle surface wash.

### 1.2 Black-Box Stream Telemetry Controls
- **Severity Ladder Filtering:** `GhoztStore.RecentEvents(limit, targetId, codePrefix, minSeverity)` with parameterized SQL filtering (`Severity IN (...)`).
- **ViewModel Integration:** `ShellViewModel.StreamSeverityFilter` (`All Severities`, `Critical Only`, `High & Critical`, `Warnings & Faults`, `Info Only`).
- **Auto-Scroll Behavior:** `ShellViewModel.AutoScrollEnabled` with automatic `ListBox.ScrollIntoView` on new items when enabled, preserving operator position when paused.
- **Export Formats:** Stream export supporting formatted JSON arrays and NDJSON (newline-delimited JSON), generating `STREAM.EXPORT` audit events.

### 1.3 State Map Search-to-Focus & Node Zoom Animation
- **Kinematic Pan:** In `StateMapWorkspace.xaml.cs`, `FocusNode` executes animated camera translations on `LatticeTranslate.X` and `LatticeTranslate.Y` over 240ms using `CubicEase` (EasingMode.EaseOut).
- **Search Reactivity:** `NodeSearchBox` filters rendered nodes, dims non-matching entities to 25% opacity, and smoothly auto-pans to the first matching candidate.

---

## 2. Constraints & Invariants
- **Design System Invariant:** All visual tokens must resolve through the CAPITAL-IZ 12-palette token engine. No hardcoded literal hex colors in XAML.
- **Backward Compatibility:** `GhoztStore.RecentEvents` retains default parameters for existing calls.
- **Performance Budget:** Transitions must run within 250ms and consume 0 GPU overhead when idle.
- **Audit Traceability:** Export actions must log canonical events into the tamper-evident ledger.
