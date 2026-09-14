# Implementation Plan — Ghozt Resilience Recovery Engine and Self-Hosting Autonomous Certification

- **Target Version:** `0.7.0`
- **Governing Blueprint:** `App/docs/blueprints/2026-09-14-resilience-recovery-and-self-test-engine.md`
- **Patent Blueprint Alignment:** Ghozt Patent-Level Functional and System Architecture Blueprint (§39, §40)
- **Author:** Antigravity / Systems Architect

---

## 1. Objectives

1. **Failure Handling, Recovery and Degraded Operation Engine (Patent §39)**:
   - Implement `ResilienceRecoveryEngine.cs` in `App/src/Ghozt.Core/`.
   - Address the 14 failure modes from Patent Table 77:
     * `MissingInputs`: Block affected action, create visible task, preserve plan.
     * `PermissionDenial`: Deny, record actor/action/policy with legitimate request path.
     * `AdapterUnavailable`: Mark capability unavailable, capture diagnostics, use approved fallback only.
     * `RunnerCrash`: Detect lease loss, seal completed events, mark in-flight action, recover/reset.
     * `ControlPlaneRestart`: Restore sweep state from ledger, never infer completion from queue absence.
     * `NetworkPartition`: Queue safe events/actions, freeze misleading live indicators, reconcile on reconnect.
     * `EvidenceStoreFailure`: Stop reportable high-risk execution if evidence cannot be preserved.
     * `StoragePressure`: Apply declared capture degradation and record omitted artefacts.
     * `ClockSyncLoss`: Preserve source order and uncertainty; never fabricate sequence.
     * `AiModelUnavailable`: Continue deterministic capabilities; mark intelligent analysis unavailable.
     * `LowConfidenceAi`: Route to human review, expose missing evidence/assumptions.
     * `TargetCrash`: Capture pre-failure evidence, contain side effects, attempt policy-controlled reset.
     * `RollbackFailure`: Escalate, freeze destructive work, record residual target state.
     * `ReportFailure`: Preserve source data, record export fault, retry without changing findings.
   - Bounded exponential back-off with jitter ($T_r = \min(T_{\max}, T_{\text{base}} \cdot 2^r \pm \text{jitter})$).
   - Idempotency guard: retry only safe, idempotent, or compensatable actions.
   - Retries logged as immutable BlackBox ledger events without overwriting initial failure events.
   - Escalation of intermittent failures to Phantom findings with Wilson score confidence intervals.

2. **Self-Hosting Autonomous Examination & Certification Engine (Patent §40)**:
   - Implement `SelfTestEngine.cs` in `App/src/Ghozt.Core/`.
   - Execute self-examination across all 12 domains from Patent Table 78:
     1. `ThemeEngine`: 12 palettes × contrast validation.
     2. `PermissionEngine`: Allow/deny gates across access classes (ObserveOnly to Destructive).
     3. `EventLedger`: Append-only immutability, zero event loss, crash recovery.
     4. `EvidenceIntegrity`: SHA-256 verification and tamper detection.
     5. `RunOrchestration`: Sweep state machine transitions and budget enforcement.
     6. `Adapters`: Preflight checks and contract conformance across registered adapters.
     7. `StateLattice`: Graph node/edge relationships, cycles, and provenance tags.
     8. `FaultForensics`: Fingerprint normalization and clustering.
     9. `AiGovernance`: Multi-agent supervisor role prohibitions and grounding assays.
     10. `DataOracles`: Mathematical integrity and conservation equations.
     11. `Reports`: SARIF, JUnit, and HTML dossier generation.
     12. `Clearance`: Mandatory gates, anti-gaming SHA-256 validation, and certificate signing.
   - Issue cryptographically sealed `GhoztSelfTestCertificate` signed by `GhoztSelfHostSupervisor@v1` and store as `Evidence`.

3. **UI & Shell Integration**:
   - In `ClearanceWorkspace.xaml`: Add "Self-Test Engine" button.
   - In `ClearanceWorkspace.xaml.cs`: Wire `OnRunSelfTest` to execute `SelfTestEngine`, render domain status cards, and display the Self-Hosting Quality Certificate.

4. **Testing, Sensitivity & Packaging**:
   - Author comprehensive unit tests in `ResilienceAndSelfTestTests.cs`.
   - Mutation-test assertion sensitivity (RED then GREEN).
   - Verify all tests pass cleanly.
   - Bump version to `0.7.0`, package MSI `Ghozt-0.7.0.msi` via `Windows\iterate.ps1`, install, commit, and push.
   - Capture verification screenshots for `screenshots/v070/`.
