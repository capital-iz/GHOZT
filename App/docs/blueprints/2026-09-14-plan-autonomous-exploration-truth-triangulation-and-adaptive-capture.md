# Ghozt Implementation Plan: Autonomous Exploration Engine, Behavioural Truth Triangulation & Adaptive Capture Forensics

- **Identifier**: GZT-PLN-010
- **Paired Blueprint**: `2026-09-14-autonomous-exploration-truth-triangulation-and-adaptive-capture.md` (GZT-BLD-010)
- **Target Release**: Milestone v0.9.0
- **Date**: 2026-09-14
- **Status**: Active

---

## 1. Objectives

1. Implement `ExplorationEngine.cs` (Patent §16, §17) with candidate-action scoring formula $S(a)$, access-class enforcement, cycle suppression, and strategies (`Frontier`, `GoalDirected`, `Adversarial`).
2. Implement `TruthTriangulationEngine.cs` (Patent §14, §15) reconciling Declared Intent, Source Reality, and Runtime Observation with structured drift taxonomy.
3. Implement `AdaptiveCaptureEngine.cs` (Patent §23) with 6 capture modes, pre-failure ring buffer preservation, and cryptographic evidence sealing.
4. Wire visual interactions into `StateMapWorkspace` (Exploration and Truth Triangulation commands) and `EvidenceWorkspace` (Capture Mode selector and Ring Buffer seals).
5. Add unit test suite `ExplorationAndTruthTriangulationTests.cs` (7 tests, mutation testing proof).
6. Build and verify `Ghozt-0.9.0.msi`.

---

## 2. Step-by-Step Task Breakdown

### Step 1: Core Engine Implementation (`App/src/Ghozt.Core/`)
- Write `ExplorationEngine.cs`:
  - `CandidateAction`, `ExplorationStrategy`, `ExplorationWeights`, `ExplorationStepResult`.
  - Scoring method $S(a) = w_u \cdot U(s') + w_d \cdot D(a) + w_c \cdot C(a) - w_r \cdot R(a) - w_h \cdot H(s, a)$.
  - Pruning based on Target `AccessClass` (`ObserveOnly`, `Interact`, `StateChanging`, `Destructive`).
  - Action generation (frontier discovery, outbound transitions, adversarial payloads).
  - Batch exploration runner recording state lattice updates and BlackBox events.
- Write `TruthTriangulationEngine.cs`:
  - `TruthSourceKind`, `TruthDriftCategory`, `TruthDriftItem`, `TruthTriangulationReport`.
  - Multi-source comparator matching manifest features, source paths, and observed lattice nodes.
  - Conflict detection (`MissingImplementation`, `UndeclaredBehaviour`, `OracleConflict`, `ObservedDeviation`).
- Write `AdaptiveCaptureEngine.cs`:
  - `CaptureMode` enum (`Baseline`, `Anomaly`, `Forensic`, `Sensitive`, `Performance`, `StoragePressure`).
  - `PreFailureRingBuffer<T>` circular buffer preserving the last $N$ entries.
  - Telemetry item model and `CaptureForensicPackage(...)` creating sealed `Evidence`.
  - Integration with `RedactionEngine` under `Sensitive` mode.

### Step 2: UI Workspace Integration (`App/src/Ghozt.App/`)
- `StateMapWorkspace.xaml` & `.xaml.cs`:
  - Action buttons: "Explore Frontier" and "Triangulate Truth".
  - Status indicators displaying drift count and exploration step counts.
- `EvidenceWorkspace.xaml` & `.xaml.cs`:
  - Capture mode selector ComboBox.
  - "Capture Ring Buffer" action button.
  - Ring buffer indicator badge on evidence items.

### Step 3: Test Verification (`App/tests/Ghozt.Tests/`)
- Author `ExplorationAndTruthTriangulationTests.cs`:
  - Test candidate action scoring unvisited bonus and loop penalties.
  - Test access class restriction on `ObserveOnly` targets.
  - Test adversarial fuzzing action generation.
  - Test truth triangulation drift detection (missing implementation vs undeclared route).
  - Test oracle conflict detection.
  - Test adaptive capture pre-failure ring buffer preservation.
  - Test storage pressure payload compression.
- Run full test suite with release configuration.
- Perform mutation testing (RED to GREEN).

### Step 4: Packaging and Release
- Bump version to `0.9.0` in `App/Directory.Build.props`.
- Package self-contained executable and `Ghozt-0.9.0.msi` via `Windows/iterate.ps1`.
- Verify audit evidence and zero uncommitted files.
