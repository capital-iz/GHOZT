# Ghozt Supervised Multi-Agent Architecture, Unified Adapter SDK, and Data Integrity Oracles — Build Blueprint

- **Document code:** GZT-BLD-007
- **Version:** `0.6.0`
- **Date:** 2026-09-14
- **Author:** Capital-Iz / Systems Architecture
- **Governing Doctrine:** Ghozt Patent-Level Functional and System Architecture Blueprint (§19, §20, §34, §37)
- **Status:** APPROVED / IMPLEMENTED

---

## 1. Executive Summary & Patent Mapping

This blueprint specifies the design, contracts, mathematical invariants, and verification mechanisms for three foundational subsystems of Ghozt:

1. **Supervised Multi-Agent Architecture Engine (Patent §34)**:
   A governed multi-agent intelligence plane where agents specialize into 9 canonical roles (`ManifestAgent`, `DiscoveryAgent`, `SweepPlanner`, `ExplorationAgent`, `ForensicsAgent`, `RepairAgent`, `RegressionAgent`, `ReportAgent`, `ClearanceEvaluator`). High-risk execution authority is centralized and isolated from model intelligence; agent outputs are validated through grounding assays against the evidence plane; red-team privilege bypass checks prevent unauthorized mutations.

2. **Unified Adapter, Harness & Plugin SDK (Patent §37)**:
   A pluggable examination framework decoupling target technologies from the core examination kernel. Provides typed contracts (`IExaminationAdapter`) for preflight health checks, autonomous surface discovery, permissioned action execution, and deterministic environment reset across Desktop (UI Automation), Process (OS telemetry), HTTP REST (web endpoints), and SQLite (databases).

3. **Data, Database and Pipeline Examination Oracles (Patent §19, §20)**:
   Mathematical state integrity oracles verifying properties beyond visual UI: exact row matching, set equivalence/subsets, aggregate reconciliation and checksums, relational/referential integrity, physical/financial conservation balancing equations ($\sum \text{In} = \sum \text{Out} + \Delta \text{Balance}$), and temporal freshness/monotonicity.

---

## 2. Supervised Multi-Agent Architecture (§34)

### 2.1 Separation of Intelligence vs Execution Authority
In Ghozt, no machine-learning or reasoning model possesses autonomous execution privilege. Reasoning models advise, draft hypotheses, and recommend actions; bounded execution services validate the target, environment, granted access level, budget, and state before executing.

```
+-------------------------------------------------------------------------+
|                         REASONING / AGENT PLANE                         |
|  [Manifest] [Discovery] [SweepPlanner] [Exploration] [Forensics] [...]  |
+-------------------------------------------------------------------------+
                                    │
                         Candidate Action & Rationale
                                    ▼
+-------------------------------------------------------------------------+
|                       MULTI-AGENT SUPERVISOR                            |
|  • Prohibited Authority Filter (Table 66)                              |
|  • Grounding Assay (Evidence & Manifest Reference Validation)           |
|  • Red-Team Privilege Bypass Check (Target AccessClass L0-L5)           |
|  • Step, Token & Latency Budget Enforcement                            |
+-------------------------------------------------------------------------+
                                    │
                          Approved Safe Action
                                    ▼
+-------------------------------------------------------------------------+
|                     BOUNDED EXECUTION SERVICE (SDK)                     |
|  Desktop UIA │ Process Supervisor │ HTTP REST │ Database SQLite         |
+-------------------------------------------------------------------------+
                                    │
                           Telemetry & Evidence
                                    ▼
+-------------------------------------------------------------------------+
|                          EVIDENCE PLANE                                 |
|  Cryptographic Ledger • SHA-256 Hashes • Sealed Audit Records           |
+-------------------------------------------------------------------------+
```

### 2.2 Canonical 7-Step Agent Interaction Pipeline
Every agent cycle executes the bounded 7-step sequence:
1. `Observe`: Ingest current bounded state, target constraints, and prior evidence.
2. `Classify`: Identify target entities, risk level, confidence score, and applicable policy.
3. `Draft`: Formulate hypothesis, plan, or candidate action.
4. `Recommend`: Output proposed action with explicit evidence citations and expected outcome.
5. `RequestApproval`: If risk exceeds threshold or action changes state, route through supervisor gate.
6. `ExecuteBounded`: Dispatch approved action through the verified adapter harness.
7. `RecordEvidence`: Seal inputs, outputs, telemetry deltas, and state transitions into the evidence plane.

### 2.3 Prohibited Authority Matrix (Patent Table 66)
- **Manifest Agent**: Cannot silently approve expected behaviour.
- **Discovery Agent**: Cannot exceed target authority.
- **Sweep Planner**: Cannot approve its own high-risk plan.
- **Exploration Agent**: Cannot invoke tools directly outside the policy gate.
- **Forensics Agent**: Cannot label cause final without required evidence.
- **Repair Agent**: Cannot modify unauthorized files or weaken tests.
- **Regression Agent**: Cannot retire protection without decision.
- **Report Agent**: Cannot invent findings or mark unreviewed claims as verified.
- **Clearance Evaluator**: Cannot sign or waive risk unless assigned authority.

---

## 3. Unified Adapter SDK Architecture (§37)

### 3.1 The `IExaminationAdapter` Contract
Every target type implements a uniform contract:
```csharp
public interface IExaminationAdapter
{
    AdapterManifest Manifest { get; }
    Task<AdapterPreflightResult> PreflightCheckAsync(Target target, CancellationToken ct = default);
    Task<AdapterDiscoveryResult> DiscoverAsync(Target target, CancellationToken ct = default);
    Task<AdapterActionResult> ExecuteActionAsync(Target target, AdapterActionRequest request, CancellationToken ct = default);
    Task<AdapterResetResult> ResetAsync(Target target, CancellationToken ct = default);
}
```

### 3.2 Concrete Built-in Adapters
1. **`DesktopUiaAdapter`** (`adapter:desktop-uia`):
   - Capabilities: `DiscoverUi`, `ExecuteUi`, `CaptureState`, `ResetTarget`.
   - Discovers Windows automation element trees (windows, controls, text, buttons).
   - Simulates user clicks, text input, keyboard navigation, and takes visual snapshots.
2. **`ProcessSupervisorAdapter`** (`adapter:process-supervisor`):
   - Capabilities: `MonitorTelemetry`, `CaptureState`.
   - Samples working set, private memory, thread count, handle count, CPU time, and exit status.
3. **`HttpRestAdapter`** (`adapter:http-rest`):
   - Capabilities: `DiscoverUi`, `ExecuteUi`, `CaptureState`.
   - Discovers HTTP endpoints, executes requests, validates status codes and JSON schemas, measures latency.
4. **`DatabaseSqliteAdapter`** (`adapter:database-sqlite`):
   - Capabilities: `DataOracles`, `CaptureState`.
   - Inspects SQLite schema (`sqlite_master`, `PRAGMA table_info`), executes read queries, validates state invariants.

---

## 4. Data, Database and Pipeline Examination Oracles (§19, §20)

### 4.1 Oracle Formulations
1. **Exact Row Oracle**: Matches observed database row fields against strict relational schemas and value assertions.
2. **Set Equality Oracle**:
   - Equal: $A = B$
   - Subset: $A \subseteq B$
   - Superset: $A \supseteq B$
   Verifies referential coverage, e.g. every active user in memory is indexed in persistent storage.
3. **Aggregate Checksum Oracle**: Reconciles counts, numerical sums, averages, and cryptographic SHA-256 content digests across datasets.
4. **Referential Invariant Oracle**: Checks foreign key integrity:
   $$\forall c \in \text{ChildTable}: c.\text{ForeignKey} \in \text{ParentTable}.\text{PrimaryKey}$$
   Flags orphaned child records with zero parent mapping.
5. **Conservation Equation Oracle**:
   $$\sum_{i} \text{Inflow}_i - \sum_{j} \text{Outflow}_j = \Delta \text{Balance}$$
   Ensures zero-sum or conservation laws hold within numerical tolerance $\epsilon \le 10^{-6}$.
6. **Temporal Freshness Oracle**:
   $$\forall i: t_i \le t_{i+1} \quad \text{and} \quad \text{now} - t_{\text{latest}} \le \Delta t_{\max} \quad \text{and} \quad t_i \le \text{now}$$
   Detects clock skew, timestamp reversals, future timestamps, and stale telemetry.

---

## 5. Verification & Testing

- **MultiAgentSupervisor**: Unit tests verify that role prohibitions are strictly enforced, ungrounded claims fail assays, unauthorized mutations under `ObserveOnly` are blocked, and step/token budgets stop runaway loops.
- **Adapter SDK**: Unit tests verify dynamic resolution for all target classes, preflight checks, discovery, and action execution.
- **Data Oracles**: Unit tests verify all 6 mathematical invariants under both positive and negative (fault-inducing) conditions.
- **Mutation Testing**: Deliberate inversion of an oracle assertion confirms test sensitivity.
