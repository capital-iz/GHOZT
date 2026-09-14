# Implementation Plan — Ghozt Supervised Multi-Agent Architecture, Unified Adapter SDK, and Data Integrity Oracles

- **Target Version:** `0.6.0`
- **Governing Blueprint:** `App/docs/blueprints/2026-09-14-multiagent-adapter-sdk-and-data-oracles.md`
- **Patent Blueprint Alignment:** Ghozt Patent-Level Functional and System Architecture Blueprint (§19, §20, §34, §37)
- **Author:** Antigravity / Systems Architect

---

## 1. Objectives

1. **Supervised Multi-Agent Architecture Engine (Patent §34)**:
   - Implement `MultiAgentSupervisor.cs` in `App/src/Ghozt.Core/`.
   - Formalize specialized agent roles (`Manifest`, `Discovery`, `SweepPlanner`, `Exploration`, `Forensics`, `Repair`, `Regression`, `Report`, `ClearanceEvaluator`).
   - Enforce prohibited authorities map (e.g. no agent may silently approve expected behavior, exceed target authority, or approve its own high-risk plan).
   - Implement the 7-step bounded pipeline: `Observe` -> `Classify` -> `Draft` -> `Recommend` -> `RequestApproval` -> `ExecuteBounded` -> `RecordEvidence`.
   - Grounding Assays: Validate agent claims against manifest and evidence plane (`AGT.GROUNDING.PASS` / `AGT.GROUNDING.FAIL`).
   - Red-Team Privilege Bypass Defense: Detect and block any mutation attempt exceeding the target's granted `AccessClass` (`AGT.PRIVILEGE_BYPASS_BLOCKED`).
   - Step & Resource Budgets: Guardrails preventing infinite loops or budget overrun.

2. **Unified Adapter, Harness & Plugin SDK (Patent §37)**:
   - Implement `AdapterSdk.cs` in `App/src/Ghozt.Core/`.
   - Define `AdapterManifest` contract and `IExaminationAdapter` interface (`PreflightCheckAsync`, `DiscoverAsync`, `ExecuteActionAsync`, `ResetAsync`).
   - Build 4 production adapters:
     - `DesktopUiaAdapter`: UI automation, element hierarchy discovery, UI interaction, visual state capture.
     - `ProcessSupervisorAdapter`: Process telemetry (CPU, memory, handles, threads, responding state, exit codes).
     - `HttpRestAdapter`: HTTP/HTTPS endpoints, JSON response schema validation, latency, status codes.
     - `DatabaseSqliteAdapter`: SQLite schema inspection, table/column metadata, query execution, row counts.
   - Implement `AdapterRegistry` with dynamic resolution by target attributes (`Kind`, `BaseAddress`).

3. **Data, Database & Pipeline Examination Oracles (Patent §19, §20)**:
   - Implement `DataOracleEngine.cs` in `App/src/Ghozt.Core/`.
   - Formulate 6 mathematical data oracle types:
     - `ExactRow`: Matches observed row values against expected schema and content.
     - `SetEquality`: Validates exact set equivalence, subset, or superset invariants across collections.
     - `AggregateChecksum`: Computes counts, sums, averages, and SHA-256 cryptographic digests across datasets.
     - `ReferentialInvariant`: Verifies relational integrity and flags orphaned or dangling foreign records.
     - `ConservationEquation`: Validates mass/energy/financial conservation equations ($\sum \text{In} - \sum \text{Out} = \Delta \text{Balance}$).
     - `TemporalFreshness`: Verifies monotonic timestamp progression ($t_i \le t_{i+1}$), maximum age, and rejects future timestamps.
   - Seal oracle evaluations into cryptographic `Evidence` items (`Kind = "OracleEvaluation"`, `Sha256`, `Sealed = true`) and flag defects as `Finding` (`FLT-`).

4. **UI & Shell Integration**:
   - In `TargetsWorkspace.xaml.cs`: Route probing through `AdapterRegistry.Resolve(target)` for typed preflight and discovery.
   - In `InspectorPanel.xaml.cs`: Add an `"Adapter"` tab to `BuildTargetTabs` exposing resolved adapter identity, capabilities, allowed action classes, and preflight status.

5. **Verification, Testing & Packaging**:
   - Author comprehensive unit test suite in `MultiAgentAndAdapterSdkTests.cs`.
   - Mutation-test assertion sensitivity (RED then GREEN).
   - Ensure all 225+ tests pass cleanly.
   - Bump version to `0.6.0`, package self-contained MSI `Ghozt-0.6.0.msi` via `Windows\iterate.ps1`, install, commit, and push.
