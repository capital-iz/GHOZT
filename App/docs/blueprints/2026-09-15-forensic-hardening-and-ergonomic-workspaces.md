# Ghozt Build Blueprint — Forensic Hardening, Ergonomic Workspaces & Interactive Developer Laboratories
**Document ID:** GZT-BLD-016  
**Date:** 2026-09-15  
**Author:** Capital-Iz Systems Engineering  
**Classification:** Patent-Standard Architecture Specification  

---

## 1. Context & Motivation

Static syntax checks and predefined blueprint templates are necessary structural foundations, but practical software utility requires experiential excellence, seamless workflow synthesis, and deep forensic interactive capabilities. 

When operators triage complex defect states, analyze state transitions, or minimize large failure vectors, they encounter distinct human-computer interaction friction:
1. **State Canvas Disorientation**: During deep exploration sweeps, dozens of state nodes are discovered. While interactive dragging was introduced in GZT-BLD-014, complex topologies become cluttered without an automated mechanism to reset or synthesize an optimal force-directed / radial layout. Middle-click-only panning is cumbersome on trackpads and non-standard mice.
2. **Backlog Cognitive Load**: As sweeps generate dozens of findings across various severities and lifecycle states, scrolling through a flat queue without multi-predicate text, severity, or maturity filtering slows down triage.
3. **Defect Export Friction**: Triaged faults cannot easily leave the tool for engineering issue trackers (e.g., GitHub Issues, Jira, Linear) without manual re-transcription of environment, stack traces, and evidence hashes.
4. **Algorithmic Laboratory Usability**: While `AlgorithmHarness.MinimizeInput` (`ddmin`) is analytically proven, operators had no interactive interface to input their own arbitrary failing token vectors, restricting the lab to canned demonstrations.

---

## 2. Mathematical & Architectural Design

### 2.1 State Lattice Ergonomics & Force-Directed Radial Layout
Let $G = (V, E)$ be the directed state lattice where $V$ is the set of discovered nodes and $E$ is the set of observed transitions. When an operator triggers `Auto Arrange`, the system recomputes planar coordinates:
$$X_i = X_0 + R(k_i) \cdot \cos\left(\frac{2\pi \cdot j}{N_k}\right)$$
$$Y_i = Y_0 + R(k_i) \cdot \sin\left(\frac{2\pi \cdot j}{N_k}\right)$$
where $k_i$ denotes the topological layer (System = 0, Feature = 1, Action/Endpoint/Data = 2), $R(k)$ is the layer radius, and $N_k$ is the node count in layer $k$.

Coordinates are immediately persisted to SQLite `LatticeNodes` table (`X`, `Y`) to eliminate positional drift across restarts.

Canvas navigation is expanded to support dual-channel panning:
$$\text{PanTrigger} \iff e.\text{ChangedButton} \in \{\text{Middle}, \text{Right}\}$$

### 2.2 Multi-Predicate Fault Filtering & Markdown Bug Serialization
The fault triage pipeline consumes a continuous filter predicate:
$$P(f) = \text{MatchQuery}(f, q) \land \text{MatchSeverity}(f, s) \land \text{MatchState}(f, m)$$
where $q$ is freeform text matched case-insensitively against $\{f.\text{Title}, f.\text{Id}, f.\text{Detail}, f.\text{Fingerprint}\}$, $s \in \{\text{All}, \text{Blocker}, \text{Critical}, \text{Major}, \text{Minor}\}$, and $m \in \{\text{All}, \text{New}, \text{Phantom}, \text{Confirming}, \text{Reproduced}, \dots\}$.

The Bug Report Serializer generates a structured Markdown contract:
```markdown
# [DEFECT] <Title> (<Severity>)
- **ID:** <Id>
- **Fingerprint:** <Fingerprint>
- **Lifecycle State:** <State>
- **Affected Node:** <NodeId>
- **Target:** <TargetName> (<TargetId>)
- **Environment:** <Environment> · <Access>
- **Reported:** <CreatedAt>

## Observed Exception / Signature
```
<Detail / StackTrace>
```

## Reproduction Steps
1. Target launched under Ghozt supervision.
2. Traversed State Lattice to node `<NodeId>`.
3. Executed check generating deviation.

## Cryptographic Evidence Hashes
- **Evidence Reference:** <EvidenceId>
- **SHA-256 Provenance:** `<Sha256>`
```

### 2.3 Interactive Delta-Debugging Laboratory (ddmin)
Given an arbitrary input vector $C = [c_1, c_2, \dots, c_n]$ supplied by the operator via the multiline editor and an active failure criterion $\Pi(c) \in \{\text{True}, \text{False}\}$, the interactive harness executes Zeller's 1-minimal delta debugging algorithm:
1. Partition $C$ into $n$ subsets $\Delta_1, \dots, \Delta_n$.
2. Test each complement $\nabla_i = C \setminus \Delta_i$.
3. If $\Pi(\nabla_i) = \text{True}$, recurse on $\nabla_i$ with $\max(n-1, 2)$ subsets.
4. Test each subset $\Delta_i$. If $\Pi(\Delta_i) = \text{True}$, recurse on $\Delta_i$ with 2 subsets.
5. If no reduction succeeds, increase granularity $n \leftarrow 2n$.
6. Terminate when $n > |C|$, yielding the 1-minimal reproducing subset $C_{min}$ where removing any single token causes the failure to vanish.

---

## 3. Failure Handling & Invariants

1. **Sandboxed Database Isolation (Doctrine §25.2)**: All interactive testing, automated test suites, and visual captures must target ephemeral or sandboxed SQLite databases, never mutating `%LOCALAPPDATA%\Capital-Iz\Ghozt\ghozt.db`.
2. **Three-State Honesty**: If a finding lacks an evidence artifact or stack trace, the bug report generator explicitly outputs `[UNVERIFIED - No Stack Trace Available]`, never fabricating data.
3. **Empty Collection Safety**: Filtering with empty results renders a clean `EmptyFilterNotice` rather than crashing the UI with null reference exceptions.

---

## 4. Verification Methodology

1. Unit tests in `RealExecutionTests.cs` validating:
   - Coordinate layout math and persistence without floating-point drift.
   - Filter predicate correctness across compound conditions.
   - Bug report Markdown generation contract and required fields.
   - Dynamic input parsing and delta-debugging reduction.
2. Mutation testing: Intentionally mutate one test assertion to observe Red failure, then restore to Green.
3. Physical Runtime UI Automation: Boot installed Ghozt, execute interactive commands via UIA, verify memory telemetry, and capture screenshots across all workspaces.
