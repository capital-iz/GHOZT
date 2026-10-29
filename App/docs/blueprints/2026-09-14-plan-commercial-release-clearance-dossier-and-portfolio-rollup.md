# Ghozt Implementation Plan: Commercial Release Clearance Dossier, Merkle Evidence Sealing & Portfolio Rollup Engine

- **Identifier**: GZT-PLN-011
- **Paired Blueprint**: `2026-09-14-commercial-release-clearance-dossier-and-portfolio-rollup.md` (GZT-BLD-011)
- **Target Release**: Milestone v1.0.0 (Commercial Release Completion)
- **Date**: 2026-09-14
- **Status**: Active

---

## 1. Objectives

1. Implement `ClearanceDossierEngine.cs` (Patent §33, §45, Appendix B Table 10) producing the formal Clearance Digest, aggregate SHA-256 Merkle root, and standalone interactive HTML certificate.
2. Implement `PortfolioRollupEngine.cs` (Patent §41, P511) aggregating multi-target quality, clearance outcomes, and guard health across the CAPITAL-IZ application estate.
3. Wire UI controls into `ClearanceWorkspace` and `ReportsWorkspace`.
4. Author unit test suite `ClearanceDossierAndPortfolioTests.cs` (6 tests, mutation testing proof).
5. Bump version to `1.0.0` in `App/Directory.Build.props`, run `iterate.ps1`, package `Ghozt-1.0.0.msi`, and commit.

---

## 2. Step-by-Step Task Breakdown

### Step 1: Core Engine Implementation (`App/src/Ghozt.Core/`)
- Write `ClearanceDossierEngine.cs`:
  - `ClearanceDigest`: `ClearanceId`, `TargetId`, `BuildLabel`, `Policy`, `Decision`, `VerifiedWeightedCoverage`, `UnknownRisk`, `MandatoryGates`, `OpenFindings`, `Waivers`, `EvidenceDigest`, `ReportId`, `Approvers`, `ValidUntil`.
  - Merkle digest computation: Sort target evidence records, hash concatenated SHA-256 digests.
  - HTML Certificate generator: Embeds CAPITAL-IZ brand tokens, Gold Authority seals, SVG status gauges, and gate breakdowns.
- Write `PortfolioRollupEngine.cs`:
  - `TargetQualitySummary`, `PortfolioRollupReport`.
  - Aggregates targets, sweep statuses, findings by severity, reproducibility rates, and regression guard health.
  - Computes estate-wide Release Readiness Index.

### Step 2: UI Workspace Integration (`App/src/Ghozt.App/`)
- `ClearanceWorkspace.xaml` & `.xaml.cs`:
  - Add "Export Clearance Dossier" button.
  - Display Merkle digest and Unknown Risk gauge.
- `ReportsWorkspace.xaml` & `.xaml.cs`:
  - Add "Portfolio Rollup" button to export estate overview.

### Step 3: Test Verification (`App/tests/Ghozt.Tests/`)
- Author `ClearanceDossierAndPortfolioTests.cs`:
  - Test Appendix B digest mapping.
  - Test composite evidence Merkle root and tamper detection.
  - Test HTML certificate generation and Gold Authority tokens.
  - Test three-state honesty invariant preservation.
  - Test portfolio target aggregation.
  - Test uncleared production target detection.
- Run test suite and perform mutation testing (RED to GREEN).

### Step 4: Packaging and Release v1.0.0
- Bump `Version` to `1.0.0` in `App/Directory.Build.props`.
- Package self-contained `Ghozt-1.0.0.msi` via `Windows/iterate.ps1`.
- Verify clean commit and git push.
