# Ghozt Build Blueprint: Autonomous Exploration Engine, Behavioural Truth Triangulation & Adaptive Capture Forensics

- **Identifier**: GZT-BLD-010
- **Date**: 2026-09-14
- **Author**: Capital-Iz Engineering Authority
- **Status**: Implemented
- **Patent Cross-Reference**: Patent §14 (Behavioural Truth Triangulation), §15 (Multi-Source Reconciliation), §16 (Autonomous Exploration Engine), §17 (Candidate-Action Scoring), §23 (Adaptive Capture & Ring Buffer Forensics)

---

## 1. Context and Problem Statement

Modern software quality examination faces three fundamental failure modes in automated testing:
1. **The Intent-Implementation Gap (Patent §14 & §15)**: Product documentation (specifications, OpenAPI schemas), implementation source code, and live runtime behavior frequently diverge. Teams either blindly trust stale documentation or accept untested shadow behaviors.
2. **Exploration Inefficiency & Uncontrolled Mutation (Patent §16 & §17)**: Autonomous fuzzers and exploratory crawlers waste test budgets looping in trivial UI cycles or inadvertently trigger destructive mutations on protected environments (e.g. executing state-changing POST/DELETE operations on an `ObserveOnly` target).
3. **Post-Failure Diagnostic Blindness (Patent §23)**: When an unexpected exception or regression occurs, conventional diagnostic logs only record the crash event itself, losing the sequence of preceding micro-actions and state transitions that caused the failure.

Milestone v0.9.0 resolves these challenges by introducing:
- **Autonomous Exploration Engine (`ExplorationEngine.cs`)** with candidate-action scoring $S(a)$, access-class risk boundaries, cycle prevention, and 3 exploration strategies (`Frontier`, `GoalDirected`, `Adversarial`).
- **Behavioural Truth Triangulation Engine (`TruthTriangulationEngine.cs`)** reconciling Declared Intent, Source Reality, and Runtime Observation, categorizing contract drift into `MissingImplementation`, `UndeclaredBehaviour`, `OracleConflict`, and `ObservedDeviation`.
- **Adaptive Capture Engine (`AdaptiveCaptureEngine.cs`)** with 6 operational capture modes (`Baseline`, `Anomaly`, `Forensic`, `Sensitive`, `Performance`, `StoragePressure`), an in-memory rolling pre-failure ring buffer (last 50 events), and automatic forensic plane sealing.

---

## 2. Architecture & Components

### 2.1 Autonomous Exploration Engine (`ExplorationEngine.cs` — Patent §16 & §17)

#### 2.1.1 Candidate Action Scoring Formula
Candidate actions are evaluated against a multi-objective mathematical objective function:

$$S(a) = w_u \cdot U(s') + w_d \cdot D(a) + w_c \cdot C(a) - w_r \cdot R(a) - w_h \cdot H(s, a)$$

Where:
- $U(s')$: **Unvisited Bonus**: $1.0$ if the destination state $s'$ has zero visits; $1.0 / (visits + 1)$ if previously visited.
- $D(a)$: **Depth / Diversity Bonus**: Rewards actions traversing unexplored feature trees or novel node kinds.
- $C(a)$: **Coverage Value**: Rewards transitions towards nodes currently in `untested`, `fault`, or `deviation` states.
- $R(a)$: **Risk Penalty**: Calibrated against the Target's `AccessClass`:
  - `ObserveOnly`: $R(a) = 1.0$ for non-read actions; state-changing or destructive actions are strictly pruned.
  - `Interact`: $R(a) = 0.1$ for standard navigation/clicks, $0.8$ for state modifications, $1.0$ for destructive actions.
  - `StateChanging`: $R(a) = 0.2$ for mutations (with declared rollback/reset), $1.0$ for destructive actions.
  - `Destructive`: Permitted only if explicit authorization flag is set on the sweep.
- $H(s, a)$: **History Penalty / Cycle Suppressor**: Number of times action $a$ has been taken from state $s$. Strongly penalizes state oscillation.

#### 2.1.2 Exploration Strategies
1. **`Frontier`**: Maximizes state boundary discovery ($w_u = 1.5, w_c = 1.0, w_d = 0.8, w_r = 1.2, w_h = 1.5$).
2. **`GoalDirected`**: Prioritizes actions leading towards a designated target state or feature ID based on graph distance heuristics.
3. **`Adversarial`**: Generates stress testing and boundary condition actions (malformed inputs, long strings, rapid sequential actions, SQLi/XSS probes) with adjusted risk thresholds to surface unhandled exceptions.

---

### 2.2 Behavioural Truth Triangulation Engine (`TruthTriangulationEngine.cs` — Patent §14 & §15)

Reconciles software behavior across three independent dimensions:
1. **Declared Intent**: Extracted from API specifications (OpenAPI/Swagger), UI manifests, or specification files via `ManifestEngine`.
2. **Source Reality**: Extracted from repository code structure, controllers, routes, event handlers, and type signatures.
3. **Runtime Observation**: Real lattice nodes, endpoints, and UI controls discovered and exercised by live adapters and drivers.

#### 2.2.1 Conflict Classification
- **`MissingImplementation`**: Declared in Intent, but absent in Source Reality or unreached in Runtime.
- **`UndeclaredBehaviour`**: Present in Source Reality or Runtime Observation, but absent in Declared Intent (shadow routes, undocumented endpoints, uncontracted controls).
- **`OracleConflict`**: Static specification asserts one invariant (e.g. HTTP 200), while code or active test oracles expect another (e.g. HTTP 204 or error).
- **`ObservedDeviation`**: Runtime consistently behaves differently from manifest contract (schema mismatch, unexpected status codes).

---

### 2.3 Adaptive Capture Engine (`AdaptiveCaptureEngine.cs` — Patent §23)

#### 2.3.1 6 Adaptive Capture Modes
1. **`Baseline`**: Low overhead structural telemetry; logs material events without heavy payloads.
2. **`Anomaly`**: Escalated sampling triggered upon warnings or latency spikes.
3. **`Forensic`**: Maximum fidelity capture; serializes the entire rolling ring buffer, stack traces, full DOM snapshots, and HTTP exchanges.
4. **`Sensitive`**: Deterministically masks secrets, tokens, and PII using `RedactionEngine` before persisting evidence.
5. **`Performance`**: High-frequency CPU/memory measurements, thread profiling, and execution duration histograms.
6. **`StoragePressure`**: Gracefully degrades payload size under disk constraints by compressing traces and omitting optional screenshots while preserving mandatory audit chains.

#### 2.3.2 Pre-Failure Rolling Ring Buffer
- Maintains an in-memory buffer of the last $N=50$ execution events, UI snapshots, and HTTP exchanges.
- When an anomaly, deviation, or fault occurs, `CaptureForensicPackage(...)` extracts the pre-failure window, bundles it with the trigger event and post-failure state, computes SHA-256 digests, and seals it into an immutable `Evidence` record in `GhoztStore`.

---

## 3. Rejected Alternatives & Design Decisions

1. **Rejected: Blind Random Fuzzing / Monkey Testing**:
   - *Reason*: Pure random fuzzers generate high noise, hit cycle traps, and violate access-class boundaries (e.g. deleting production or staging database records). Ghozt strictly bounds exploration through candidate-action scoring and access-class validation.
2. **Rejected: Single Authoritative Truth Source**:
   - *Reason*: Treating code as infallible ignores undocumented shadow routes; treating docs as infallible ignores documentation drift. Truth Triangulation treats all three sources as evidence to be triangulated.
3. **Rejected: Continuous Heavy Video/Payload Recording**:
   - *Reason*: Continuous high-fidelity recording exhausts operator storage and adds significant I/O latency. The pre-failure rolling ring buffer keeps high-frequency telemetry in memory and only persists full forensic packages upon deviation.

---

## 4. Verification Evidence & Test Matrix

Cross-referenced in `App/tests/Ghozt.Tests/ExplorationAndTruthTriangulationTests.cs`:
1. `ExplorationEngine_CandidateActionScore_PrioritizesUnvisitedNodesAndPenalizesLoops`: Proves $S(a)$ formula prioritizes unvisited nodes and penalizes repetitive loops.
2. `ExplorationEngine_ObserveOnlyTarget_PrunesStateChangingActions`: Proves strict access-class pruning for `ObserveOnly` targets.
3. `ExplorationEngine_AdversarialStrategy_GeneratesBoundaryAndMaliciousInputs`: Proves adversarial strategy injects boundary fuzzing actions.
4. `TruthTriangulationEngine_Triangulate_DetectsMissingImplementationAndUndeclaredRoutes`: Proves reconciliation correctly flags missing features and undeclared shadow routes.
5. `TruthTriangulationEngine_OracleConflict_FlagsContractContradiction`: Proves detection of contradictions between specifications and test oracles.
6. `AdaptiveCaptureEngine_EscalatesToForensicMode_PreservesPreFailureRingBuffer`: Proves ring buffer capture preserves pre-failure telemetry window upon anomaly.
7. `AdaptiveCaptureEngine_StoragePressure_DegradesMediaGracefully`: Proves graceful payload compression under storage pressure.
