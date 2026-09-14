# Implementation Plan: Time Alignment, Deterministic Replay & Reproduction Envelope

**Governing Blueprint:** `App/docs/blueprints/2026-09-14-time-alignment-and-deterministic-replay.md` (GZT-BLD-012)  
**Parent Blueprint:** `Blueprints/GHOZT - PATENT-LEVEL FUNCTIONAL AND SYSTEM ARCHITECTURE BLUEPRINT.docx` (§27, §28, Tables 48, 49, 50, 52, 54)

---

## 1. Proposed Code Changes

### Component: `Ghozt.Core`
1. **`TimeAlignmentEngine.cs` (NEW)**:
   - Data structures: `RawTimelineEvent`, `EventSourceKind`, `AlignedTimelineRecord`, `TimelineAlignmentReport`.
   - Core algorithms:
     - Rendezvous offset calibration.
     - Lamport logical clock topological sorting.
     - Partial order DAG generation with uncertainty intervals.
2. **`DeterministicReplayEngine.cs` (NEW)**:
   - Data structures: `ReplayPackage`, `ReplayAction`, `ReplayOutcome`, `ReplayExecutionRecord`, `ReproductionEnvelope`.
   - Core algorithms:
     - Package compilation from sweep/finding/evidence records.
     - Outcome evaluation matching Patent Table 50 (`ExactReproduction`, `EquivalentReproduction`, `PartialReproduction`, `NonReproduction`, `InvalidReplay`).
     - Weighted reproducibility score (Table 54) with 95% Wilson confidence intervals.
     - Reproduction envelope synthesis (Table 52).
     - BlackBox event logging (`RPL.PACKAGE.CREATED`, `RPL.VERIFIED`, `RPL.OUTCOME`).

### Component: `Ghozt.App`
1. **`ReplayWorkspace.xaml` & `ReplayWorkspace.xaml.cs` (MODIFY)**:
   - Add **"Replay Package"** action button and modal/panel to generate and view the Patent Table 49 specification.
   - Add **"Verify Replay"** button that executes deterministic evaluation, rendering the Table 50 outcome badge and Table 52 envelope metrics.

### Component: `Ghozt.Tests`
1. **`TimeAlignmentAndDeterministicReplayTests.cs` (NEW)**:
   - Unit tests for heterogeneous time alignment, clock uncertainty, ReplayPackage serialization, outcome classification, and reproduction envelope computation.
   - Mutation testing proof (RED -> GREEN).

---

## 2. Verification Plan
- `dotnet build` of `Ghozt.Core` and `Ghozt.App` with zero errors.
- `dotnet test` targeting `TimeAlignmentAndDeterministicReplayTests`.
- Full regression suite execution across all 270+ tests.
- Version packaging via `iterate.ps1`.
