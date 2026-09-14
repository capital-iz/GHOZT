# Implementation Plan: Composite Fault Fingerprint, Duplicate Decision & Finding Maturity Lifecycle

**Governing Blueprint:** `App/docs/blueprints/2026-09-14-fault-fingerprint-and-duplicate-control-engine.md` (GZT-BLD-013)  
**Target Version:** 1.0.2  

---

## 1. Scope and Technical Objectives

Implement **Patent §25 & §26: Composite Fault Fingerprinting, Duplicate Control Engine & 10-State Maturity Lifecycle**:
1. `FaultFingerprintEngine.cs`:
   - 7-feature decomposition (Patent Table 46):
     - Exception signature normalisation (volatile token removal)
     - Semantic difference category and value vector
     - State Lattice context (node, edge, workflow breadcrumb)
     - Action sequence sketch (ordered action tokens)
     - State delta sketch (mutated keys/properties)
     - Dependency trace sketch (service call chain & method)
     - Environment class (tier, OS, architecture)
   - Composite SHA-256 fingerprint generation (`FP-xxxxxx`).
2. `DuplicateClassifier.cs` (or integrated in `FaultFingerprintEngine.cs`):
   - 4 duplicate decision outcomes (Patent Table 47): `SameFault`, `Variant`, `Related`, `Independent`.
   - Cluster resolution: clustering into existing finding, variant lineage tracking (`VariantOf`), related candidate flagging (`RelatedTo`), or new independent finding.
   - BlackBox event auditing (`FLT.NEW`, `FLT.RECUR`, `FLT.VARIANT`, `FLT.RELATED`).
3. `FindingMaturityEngine.cs`:
   - 10-state lifecycle (Patent Table 45): `New`, `Phantom`, `Confirming`, `Reproduced`, `CauseSupported`, `FixReady`, `ReTest`, `Verified`, `Waived`, `Rejected`.
   - Precondition evaluation guards (e.g. valid replays, counterfactual confidence, approved Fix Brief, passing regression verifier, waiving authority).
   - Immutable audit logging (`FLT.MATURITY.ADVANCE`, `FLT.WAIVED`).
4. UI Enhancements in `FaultsWorkspace.xaml` and `.xaml.cs`:
   - Table 46 Feature Breakdown visual card for selected finding.
   - Table 47 Duplicate Decision badges and cluster relationship indicator.
   - Table 45 10-State Interactive Maturity Progress Track with precondition validation feedback.
5. Unit Tests in `FaultFingerprintAndDuplicateTests.cs`:
   - Full coverage across 7 Table 46 features, 4 Table 47 outcomes, and 10 Table 45 states with mutation testing proof.

---

## 2. File Modification & Creation Order

1. **Model & Core Contracts**:
   - `App/src/Ghozt.Core/Model.cs`: Add `Phantom` to `FindingMaturity`. Add `DuplicateOutcome` enum (`SameFault`, `Variant`, `Related`, `Independent`). Add `SemanticCategory` enum (`StatusCodeMismatch`, `UiStateCorruption`, `SchemaViolation`, `ContractViolation`, `ResourceDrift`, `CrashFatalExit`, `UnresponsiveWindow`, `AuthenticationDenied`, `InvariantBroken`, `Other`). Add `VariantOf` and `RelatedCandidateId` properties to `Finding`.
2. **Core Engines**:
   - `App/src/Ghozt.Core/FaultFingerprintEngine.cs`: Implement 7-feature decomposition, composite fingerprinting, duplicate classification, and 10-state maturity transition logic.
3. **UI Integration**:
   - `App/src/Ghozt.App/Workspaces/FaultsWorkspace.xaml`: Add Fault Fingerprint card, Duplicate Decision badges, and 10-state maturity progress bar.
   - `App/src/Ghozt.App/Workspaces/FaultsWorkspace.xaml.cs`: Wire handlers for re-evaluating duplicate classifier, advancing maturity with precondition validation, and viewing feature breakdown.
4. **Unit Tests**:
   - `App/tests/Ghozt.Tests/FaultFingerprintAndDuplicateTests.cs`: 6+ comprehensive tests covering Table 46, Table 47, Table 45, and mutation testing.
5. **Release & Packaging**:
   - Run `iterate.ps1` to build `Ghozt-1.0.2.msi`, verify all tests, commit, and push.
