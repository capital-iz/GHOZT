# Build Blueprint — Performance Soak Sweep, Executive Dossier Dashboard, and Anti-Gaming Clearance Certification

- **Target Version:** `0.5.0`
- **Identifier:** `GZT-BLD-007`
- **Status:** APPROVED / IMPLEMENTATION
- **Date:** 2026-09-14
- **Author:** Antigravity / Systems Architect
- **Governing Doctrines:**
  - Ghozt Patent-Level Functional and System Architecture Blueprint (§21, §32, §33)
  - Universal Design System — CAPITAL-IZ Doctrine v2.0 (§12 Ghozt Adaptation)
  - Universal Security & Operational Doctrine §9
  - Verification-First Execution Doctrine §12

---

## 1. System Structure

```
+-------------------------------------------------------------------------+
|                              Ghozt Shell                                |
|  [Targets]  [State Map]  [Sweep]  [Faults]  [Replay]  [Clearance]  ...  |
+-------------------------------------------------------------------------+
                                     |
               +---------------------+---------------------+
               |                                           |
               v                                           v
+-----------------------------+             +-----------------------------+
|    SweepEngine (Soak Mode)   |             |       ClearanceEngine       |
| - Performance Soak pipeline |             | - GATE.PERFORMANCE_SOAK     |
| - Telemetry time series     |             | - GATE.ANTI_GAMING          |
| - OLS Linear Regression     |             | - Tamper-proof audit seal   |
| - ResourceDriftMetrics      |             +-----------------------------+
+-----------------------------+                            |
               |                                           v
               v                            +-----------------------------+
+-----------------------------+             |      ReportGenerator        |
|     SoakAnalysisEngine      |             | - Executive HTML Dossier    |
| - Slope beta (MB/s)         |             | - Inline SVG Gauges & Charts|
| - Determination coeff R^2   |             | - SARIF v2.1.0 / JUnit XML  |
| - Deterministic leak assays |             | - Dynamic Version Injection |
+-----------------------------+             +-----------------------------+
```

### 1.1 Components & Modules

1. **`SweepEngine` (Soak Integration — Patent §21)**:
   - Registers `"Performance Soak"` in `SweepEngine.Modes`.
   - Defines check sequence: `["Reachability", "Identity", "Soak and Resilience Analysis", "Capacity", "Integrity"]`.
   - Executes periodic telemetry sampling against target processes or HTTP endpoints over the configured budget window.
   - Evaluates ordinary least-squares regression via `SoakAnalysisEngine`.
   - Records sealed `performance:soak` evidence and flags confirmed leaks as High-severity findings.

2. **`ReportGenerator` (Executive & Technical HTML Dashboard — Patent §32)**:
   - Compiles technical, executive, and machine-readable dossiers.
   - Embeds CAPITAL-IZ Design System CSS tokens (`#0b1118`, `#121c26`, `#233444`, `#d4af37`, `#32d74b`, `#ff453a`).
   - Renders pure SVG inline interactive data visualizations (zero external JavaScript or CDN dependencies):
     - Release Clearance Gates Status Ring (Donut chart).
     - Deviation Severity Distribution Bar.
   - Injects dynamic assembly version metadata into all export streams (HTML, SARIF, JUnit, Markdown, JSON).

3. **`ClearanceEngine` & `ClearanceWorkspace` (Anti-Gaming Release Certification — Patent §33)**:
   - Introduces `GATE.PERFORMANCE_SOAK` to prevent clearance when targets suffer resource leaks.
   - Introduces `GATE.ANTI_GAMING` to enforce evidence integrity (no fabricated runs, zero empty states, valid regression guard mutation efficacy $\mu \ge 0.50$).
   - Provides certificate tamper-detection verification in `ClearanceWorkspace.xaml.cs`.

---

## 2. Flow Design

### 2.1 Performance Soak Flow

```
[Start Sweep (Performance Soak)]
             |
             v
[Check Reachability & Identity] -> OK
             |
             v
[Soak and Resilience Analysis]
  * Sample t = 0 (baseline memory, handles, threads)
  * Loop sampling intervals over duration
  * Calculate delta t, y
  * Ordinary Least Squares: beta = SS_ty / SS_tt, R^2 = (SS_ty^2) / (SS_tt * SS_yy)
  * Threshold Comparison:
    - WorkingSet: beta >= 0.05 MB/s & R^2 >= 0.85
    - Handles: beta >= 0.10 /s & R^2 >= 0.85
    - Threads: beta >= 0.02 /s & R^2 >= 0.85
             |
   +---------+---------+
   |                   |
[Leak Detected]     [Stable]
   |                   |
   v                   v
[Generate Finding   [Record Pass]
 & High Severity]      |
   |                   |
   +---------+---------+
             |
             v
[Seal Evidence 'performance:soak']
             |
             v
[Check Capacity & Integrity] -> [Complete Sweep]
```

### 2.2 Clearance Anti-Gaming Audit Flow

```
[Evaluate Target Clearance]
             |
             v
[Evaluate Gates 1-8]
             |
             v
[Gate 9: Performance Soak Stability]
  * Queries evidence for 'performance:soak'
  * If critical drift finding exists -> FAIL
             |
             v
[Gate 10: Anti-Gaming Verification]
  * Verify sweeps visited > 0 states (not unrun/empty)
  * Verify 100% of evidence hashes match payload contents
  * Verify regression guard mutation efficacy score mu >= 0.50
             |
             v
[Compute Clearance Outcome] -> [Sign Decision & Export Sealed Certificate]
```

---

## 3. Constraints & Invariants

1. **Air-Gapped Self-Contained Constraint**: HTML dossiers must never load external CDNs (fonts, scripts, frameworks). All typography and chart visualisations are pure inline CSS and SVG.
2. **Three-State Honesty Invariant**: Missing telemetry or unmeasurable endpoints must result in `UNVERIFIED` / `BLOCKED`, never defaulted to 0.0 or marked as `PASS`.
3. **Deterministic OLS Thresholds**:
   - Memory slope $\beta \ge 0.05 \text{ MB/s}$ (~180 MB/hr) with $R^2 \ge 0.85$.
   - Handle slope $\beta \ge 0.10 \text{ handles/s}$ (~360 handles/hr) with $R^2 \ge 0.85$.
   - Thread slope $\beta \ge 0.02 \text{ threads/s}$ (~72 threads/hr) with $R^2 \ge 0.85$.

---

## 4. Failure Handling

| Failure Path | Detection Mechanism | Recovery & Mitigation |
|---|---|---|
| Target process exits mid-soak | Process handle polling returns `HasExited` | Clamps sample series, analyzes observed window, records `SWP.PROCESS_EXITED` in ledger |
| Telemetry time variance is zero ($SS_{tt} \le 10^{-9}$) | `SoakAnalysisEngine` denominator check | Returns zero slope, non-leaking diagnostic with explanation |
| Evidence payload modified on disk | Certificate verification calculates SHA-256 mismatch | Displays tamper warning in Clearance workspace, refuses certificate verification |
| HTML export failure | Try-catch around file I/O | Logs `RPT.EXPORT.FAIL` in ledger, preserves previous export state |

---

## 5. Rejected Alternatives

1. **Third-Party Charting Library (Chart.js / D3) in HTML Reports**:
   - *Rejected*: Violates the self-contained air-gapped requirement. Corporate networks and secure testing labs forbid external script execution and CDN calls. Pure inline SVG ensures 100% offline rendering fidelity.
2. **Real-time 24-hour Soak Requirement for Basic Runs**:
   - *Rejected*: Inflexible for local developer workflows. Performance Soak scales dynamically according to the operator's declared time and state budget (from 10 seconds to several hours).
3. **Simple Endpoint Averaging Instead of OLS Regression**:
   - *Rejected*: Simple delta comparisons ($\text{End} - \text{Start}$) fail to distinguish between cache warmup (which plateaus) and true continuous memory leaks. Linear regression determination coefficient $R^2 \ge 0.85$ eliminates false positives.

---

## 6. Verification Cross-Reference

- Unit tests in `App/tests/Ghozt.Tests/PerformanceSoakAndClearanceTests.cs`.
- Verification via `dotnet test App\tests\Ghozt.Tests\Ghozt.Tests.csproj -c Release`.
- Mutation testing on assertion sensitivity.
- MSI packaging and visual UI verification.
