# Ghozt Build Blueprint: Commercial Release Clearance Dossier, Merkle Evidence Sealing & Portfolio Rollup Engine

- **Identifier**: GZT-BLD-011
- **Date**: 2026-09-14
- **Author**: Capital-Iz Engineering Authority
- **Status**: Implemented
- **Patent Cross-Reference**: Patent §33 (Earned, Not Asserted Release Clearance), §41 (Estate Governance & Portfolio Use), §45 (Honest Boundaries & Three-State Clearance), Appendix B (Table 10: Example Clearance Digest)

---

## 1. Context and Problem Statement

Release decisions in commercial software development are often opaque, political, or based on misleading summary percentages. Conventional QA systems suffer from three critical defects:
1. **False Completeness (Patent §45)**: Passing 100% of existing tests is routinely equated with "zero defects", hiding that 80% of application state space was never observed or tested.
2. **Untraceable Clearance Claims (Patent §33 & Appendix B)**: Release clearance decisions lack cryptographic bindings to the actual underlying test artefacts, logs, and failure reproducers that support them.
3. **Estate Blindness (Patent §41)**: Enterprise software systems consist of multiple interconnected products (e.g. Capital-Iz Desktop Suite: Ghozt, CommanDeck, Mouse n Key, Trading Terminal). Verifying one product in isolation without cross-product dependency and portfolio rollup leads to integration regressions.

Milestone v1.0.0 addresses these needs by delivering:
- **Release Clearance Dossier Engine (`ClearanceDossierEngine.cs`)** implementing Patent Appendix B (Table 10), Three-State Honesty decomposition (Verified, Failed, Unknown), Merkle evidence roots, and standalone interactive HTML/JSON certificates.
- **Portfolio Quality Rollup Engine (`PortfolioRollupEngine.cs`)** governing multi-target quality across the entire Capital-Iz desktop estate.

---

## 2. Architecture & Components

### 2.1 Release Clearance Dossier Engine (`ClearanceDossierEngine.cs` — Patent §33 & Appendix B)

#### 2.1.1 Clearance Digest Structure (Patent Appendix B, Table 10)
```yaml
clearance_id: CLR-XXXXXX
target/build: TGT-XX / BLD-XXX
policy: Release-Standard@v1.0
decision: CLEARED | CONDITIONALLY_CLEARED | DENIED | BLOCKED
verified_weighted_coverage: 0.XX
unknown_risk: 0.XX
mandatory_gates:
  unit: PASS | FAIL
  integration: PASS | FAIL
  ui_core: PASS | FAIL
  security_auth: PASS | FAIL
  accessibility_core: PASS | FAIL
  performance: PASS | PASS_WITH_CONDITION | FAIL
  regression_invariants: PASS | FAIL
open_findings:
  CRITICAL: N
  HIGH: N
  MEDIUM: N (waived, expiry YYYY-MM-DD)
waiver: WVR-XXXX
evidence_digest: sha256:XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX
report: RPT-XXXXXX
approvers: [ROLE-01, ROLE-02]
valid_until: ISO-8601-TIMESTAMP
```

#### 2.1.2 Merkle Evidence Root & Tamper Protection
- Ghozt aggregates all `Evidence` records stored for the target build.
- Sorts records deterministically by `Id` and combines their individual `Sha256` content digests into an aggregate SHA-256 Merkle root.
- Any subsequent tampering with an evidence payload or SQLite record breaks the digest match, invalidating the release certificate.

#### 2.1.3 Three-State Honesty Invariant (Patent §45)
Clearance evaluates:
$$\text{Total Scope} = \text{Verified Scope} + \text{Failed Scope} + \text{Unknown Risk}$$
Where:
- **Verified Scope**: States and endpoints observed with cryptographic proof ($V$).
- **Failed Scope**: Open un-remediated defects ($F$).
- **Unknown Risk**: Unvisited states, unmeasured telemetry, or unexecuted paths ($U = 1.0 - V - F$).
- The release decision explicitly publishes $U$. It is never zeroed or disguised as a pass.

---

### 2.2 Portfolio Quality Rollup Engine (`PortfolioRollupEngine.cs` — Patent §41 & P511)

- Manages multi-target quality aggregation across the Capital-Iz application portfolio:
  - Discovers all registered targets in `GhoztStore`.
  - Calculates estate-wide metrics:
    - Active target count and reachability status.
    - Latest sweep execution states.
    - Clearance status distribution (`Cleared`, `ConditionallyCleared`, `Denied`, `Blocked`).
    - Open defect inventory across severity tiers.
    - Active regression guard efficacy and mutant-kill status.
  - Produces an executive `PortfolioRollupReport` with cross-product risk indicators.

---

## 3. UI Integrations

1. **Clearance Workspace (`ClearanceWorkspace.xaml` & `.xaml.cs`)**:
   - Added **"Export Release Certificate"** button that generates the interactive HTML certificate, writes it to disk, and opens it in the default browser.
   - Surfaces the cryptographic Merkle digest, Unknown Risk percentage, and Appendix B YAML/JSON digest preview.

2. **Reports Workspace (`ReportsWorkspace.xaml` & `.xaml.cs`)**:
   - Added **"Portfolio Rollup"** action button generating estate-wide executive quality reports.

---

## 4. Verification Evidence & Test Matrix

Cross-referenced in `App/tests/Ghozt.Tests/ClearanceDossierAndPortfolioTests.cs`:
1. `ClearanceDossierEngine_BuildDigest_MatchesPatentAppendixBSchema`: Validates complete compliance with Patent Appendix B Table 10.
2. `ClearanceDossierEngine_ComputesCompositeEvidenceDigest_DetectsTamperedArtefact`: Validates cryptographic Merkle root sensitivity to evidence corruption.
3. `ClearanceDossierEngine_GeneratesHtmlCertificate_ContainsGoldAuthorityAndUnknownRisk`: Validates self-contained HTML rendering, Gold Authority badges, and design tokens.
4. `ClearanceDossierEngine_EnforcesThreeStateHonesty_PreservesUnknownScope`: Proves unverified scope is never swallowed.
5. `PortfolioRollupEngine_AggregatesEstateTargets_ComputesQualityMetricsAndGuardHealth`: Validates multi-target aggregation across the estate.
6. `PortfolioRollupEngine_DetectsUnclearedProductionTargets_FlagsRisk`: Proves detection of uncleared production deployments.
