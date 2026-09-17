# Ghozt Implementation Plan: Executive Dossier & Estate Portfolio Rollup Terminal
**Plan Code:** GZT-UX-005-PLAN  
**Governing Blueprint:** GZT-UX-005 (`2026-09-17-executive-dossier-and-portfolio-rollup-terminal.md`)  
**Status:** IMPLEMENTED  
**Date:** 2026-09-17  

---

## 1. Scope & Execution Objectives

1. Refactor `ReportsWorkspace.xaml` Row 1 from a single-column layout (`MaxWidth="900"`) into a responsive two-column high-density layout (`Column 0: Width="*"`, `Column 1: Width="480"`).
2. Implement `ReportOperationsPanel` in `Column 1`:
   - `ReportSealingCard`: Cryptographic Merkle seal, report ID, signatory, and SHA-256 digest.
   - `EstateRollupCard`: Multi-target Estate Readiness Index (`ERI` %), monitored target metrics, critical defect tally, and rollup generation trigger.
   - `ExportHubCard`: One-click export triggers for Markdown, HTML, JSON, SARIF, and JUnit XML with last exported path feedback.
   - `ViewerLaunchpadCard`: In-app quick reader drawer toggle and system external viewer launcher.
3. Synchronize `ReportsWorkspace.xaml.cs` code-behind to update `ReportOperationsPanel` dynamically whenever a report or portfolio rollup is generated or selected.
4. Add comprehensive unit and markup contract tests to `RealExecutionTests.cs`.
5. Execute isolated runtime visual verification and capture high-resolution proof screenshots.
6. Package MSI via `iterate.ps1`, install locally to `C:\Program Files\Capital-Iz\Ghozt\`, commit, and push.

---

## 2. Risk Assessment & Mitigations

- **Risk 1 (WPF Resource Collisions)**: Referencing undeclared styles (e.g. `Type.MicroStyle` or `Button.Secondary`) causes runtime parsing exceptions.
  *Mitigation*: Use verified token styles `Type.MicroLabelStyle`, `Type.HeadingStyle`, `Type.CaptionStyle`, `Type.MonoStyle`, and default `<Button ... />` for secondary controls.
- **Risk 2 (Live Database Contamination)**: Executing diagnostics against live `%LOCALAPPDATA%` could corrupt real target records (Doctrine §25.2).
  *Mitigation*: E2E scripts strictly isolate execution to a temporary test database (`isolated_reports_test.db`).
- **Risk 3 (Empty State Drift)**: When no target is selected, controls must cleanly disable without throwing `NullReferenceException`.
  *Mitigation*: Safe null-checking throughout `UpdateOperationsPanel` and fallback to `PENDING GENERATION` placeholders.
