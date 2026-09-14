# GZT-BLD-012: Time Alignment, Deterministic Replay & Reproduction Envelope Blueprint

**Author:** Antigravity  
**Date:** 2026-09-14  
**Status:** Approved & Implemented  
**Governing Blueprints:**
- `Blueprints/GHOZT - PATENT-LEVEL FUNCTIONAL AND SYSTEM ARCHITECTURE BLUEPRINT.docx` (§27, §28, Tables 48, 49, 50, 52, 54)
- `Apps/Desktop/GHOZT/AGENTS.md` (Doctrine §§2, 4, 7, 8, 9, 12, 17, 25)

---

## 1. System Structure

### 1.1 Context and Problem Statement
When examining complex distributed software, microservices, and desktop applications, events originate from heterogeneous, unsynchronized clocks:
1. Distributed traces and service spans.
2. UI rendering frames and input dispatch acknowledgements.
3. Structured application log streams.
4. Network request/response pipelines.
5. Database transaction commits and queue delivery receipts.
6. Agent tool-calls and permission gates.
7. Unsynchronized external telemetry.

Naively interleaving these events based solely on local timestamps produces causality inversions (e.g., an HTTP response appearing before the UI button click that requested it). Furthermore, reproducing intermittent, concurrency, or timing-dependent bugs requires a formal **Replay Package** (Patent Table 49) specifying environment manifests, initial checkpoints, seeds, and preconditions, paired with rigorous outcome classification (Patent Table 50: `ExactReproduction`, `EquivalentReproduction`, `PartialReproduction`, `NonReproduction`, `InvalidReplay`) and multi-dimensional **Reproduction Envelope** bounds (Patent Table 52).

### 1.2 Module Architecture

```
                      +------------------------------------------+
                      |         Heterogeneous Log Inputs         |
                      |  (Traces, UI, Network, DB, AppLogs, Agent) |
                      +------------------------------------------+
                                           |
                                           v
                      +------------------------------------------+
                      |           TimeAlignmentEngine            |
                      |   - Lamport Logical Clock Ordering       |
                      |   - Rendezvous Clock Calibration         |
                      |   - Bounded Offset Uncertainty (+/- dt)  |
                      +------------------------------------------+
                                           |
                                           v
                               +-----------------------+
                               | Aligned Timeline DAG  |
                               +-----------------------+
                                           |
                                           v
                      +------------------------------------------+
                      |        DeterministicReplayEngine         |
                      |   - ReplayPackage Builder (Table 49)     |
                      |   - Checkpoint & Precondition Verification|
                      |   - Outcome Classifier (Table 50)        |
                      |   - Reproduction Envelope Model (Table 52)|
                      |   - Wilson-Score Confidence (Table 54)   |
                      +------------------------------------------+
                                           |
                                           v
                      +------------------------------------------+
                      |   ReplayWorkspace & BlackBox Ledger      |
                      |   - Interactive Replay & Outcome Badges  |
                      |   - Sealed RPL-xxxxxx Evidence Records   |
                      +------------------------------------------+
```

---

## 2. Flow Design

### 2.1 Heterogeneous Timeline Alignment (Patent Table 48)
1. **Span & Parentage Linking**: Trace and span IDs establish direct causal ancestry. Parent spans strictly precede child spans.
2. **Rendezvous Drift Calibration**: Identifies cross-boundary rendezvous events (e.g., client sends HTTP request at $t_{ui}$, server receives at $t_{srv}$). Estimates bounded clock offset $\Delta t$ and uncertainty $\delta$.
3. **Lamport Logical Clock Assignment**: Computes topological partial order DAG ensuring no effect precedes its cause:
   $$L(e') \ge L(e) + 1 \quad \text{for any } e \to e'$$
4. **Causality-Consistent Output**: Emits `AlignedTimelineRecord` stream with calculated monotonic sequence indices and bounded uncertainty intervals $[t - \delta, t + \delta]$.

### 2.2 Replay Package Contract (Patent Table 49)
A deterministic replay package (`RPL-xxxxxx` / `RPR-xxxxxx`) encapsulates:
- `ReplayId`: Canonical identifier.
- `TargetBuild`: Target identifier and build commit.
- `EnvironmentManifest`: OS, architecture, display resolution, locale.
- `Checkpoint`: Initial environment / database snapshot hash.
- `Preconditions`: Required initial fixtures, network conditions, and clock seeds.
- `Actions`: Ordered sequence of deterministic interaction steps (`ACT-xxxx`).
- `Inputs`: Parameterized input payload vectors.
- `RandomSeed`: Fixed PRNG seed for deterministic stochastic algorithms.
- `DependencyVersions`: Pinned dependency version map.
- `ExpectedFaultFingerprint`: Target defect fingerprint (`FP-xxxx`).
- `EvidencePlan`: Required telemetry modes (`video`, `trace`, `state_delta`, `response`).

### 2.3 Replay Outcome Classification (Patent Table 50)
Replay executions are evaluated against expected failure signatures:
- **`ExactReproduction`**: Identical fault fingerprint (`FP-xxxx`) and causal mechanism.
- **`EquivalentReproduction`**: Same semantic fault and supported cause with permitted superficial timing or process ID differences.
- **`PartialReproduction`**: Subset of symptoms or enabling conditions observed; fingerprint incomplete.
- **`NonReproduction`**: Defect absent under recorded conditions (contributes to Phantom analysis).
- **`InvalidReplay`**: Target build, checkpoint, or dependency mismatch invalidating execution.

### 2.4 Reproduction Envelope Model (Patent Table 52)
$$\text{Envelope}(\text{FLT}) = \{ \text{Required}, \text{Sufficient}, \text{Suppressing}, \text{Correlated}, \text{Attempts}, \text{Reproductions}, \text{WilsonScore}, \text{TimingRanges} \}$$
Computes the parameter ranges within which the defect manifests, eliminating guesswork for engineering remediation.

---

## 3. Constraints & Invariants

1. **Causality Invariant**: No effect can precede its cause in the aligned timeline.
2. **Deterministic Checkpointing**: A replay attempt where the initial checkpoint hash differs must be classified as `InvalidReplay`, not `NonReproduction`.
3. **Three-State Honesty**: Incomplete replay executions or unverified state space must remain classified as `PartialReproduction` or `UnknownRisk`, never assumed to have passed.
4. **Sandboxed Test Isolation (Doctrine §25.2)**: Replay tests and checkpoints operate in isolated temporary directories without polluting live `%LOCALAPPDATA%`.

---

## 4. Failure Handling & Recovery

- **Missing Clocks**: If an event source lacks absolute timestamps, it is anchored to the nearest bounding rendezvous event with an expanded uncertainty interval.
- **Corrupted Checkpoints**: Emits `RPL.CHK.INVALID` and safely halts execution before mutations occur.
- **Execution Divergence**: Detects the first step where runtime state deviates from recorded state, logs `RPL.DIVERGENCE`, and records causal delta.

---

## 5. Scalability & Performance

- Timeline alignment executes in $O(N \log N)$ time using topological sorting and priority heaps.
- Checkpoint comparison uses stream SHA-256 digests without holding multi-gigabyte disk snapshots in RAM.
- Reproduction envelope synthesis uses online incremental statistics.

---

## 6. Verification Plan

- **Unit Tests**:
  - Timeline alignment ordering and Lamport clock correctness across 7 sources.
  - ReplayPackage YAML serialization matching Patent Table 49.
  - Replay outcome classification across all 5 Patent Table 50 states.
  - Reproduction Envelope synthesis and Wilson score confidence computation.
- **Mutation Proof**:
  - Mutate one key assertion (e.g. invert outcome verification), observe RED failure, restore to GREEN.
