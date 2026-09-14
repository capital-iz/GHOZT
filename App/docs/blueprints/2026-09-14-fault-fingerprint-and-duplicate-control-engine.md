# GZT-BLD-013: Composite Fault Fingerprint, Duplicate Decision & Finding Maturity Lifecycle Blueprint

**Author:** Antigravity  
**Date:** 2026-09-14  
**Status:** Approved & Implemented  
**Governing Blueprints:**
- `Blueprints/GHOZT - PATENT-LEVEL FUNCTIONAL AND SYSTEM ARCHITECTURE BLUEPRINT.docx` (§25, §26, Tables 44, 45, 46, 47)
- `Apps/Desktop/GHOZT/AGENTS.md` (Doctrine §§2, 4, 7, 8, 9, 12, 17, 25)

---

## 1. System Structure

### 1.1 Context and Problem Statement
When autonomous testing engines and sweep runners examine software targets, failures recur across runs, environments, and builds. Naive deduplication based solely on error messages or raw stack traces fails catastrophically:
1. Volatile parameters (dynamic memory addresses, GUIDs, timestamps, process IDs, ephemeral socket ports, line number shifts in compiled assemblies) create false distinct issues for identical bugs.
2. Identical high-level symptoms (e.g., HTTP 500 or UI unresponsiveness) originating from distinct root causes get improperly collapsed into a single issue, concealing critical defects.
3. Teams lack structured lifecycle progression: findings languish in binary "open/closed" states without evidence-backed verification of reproducer rates, causal hypotheses, and regression guards.

To solve this, Ghozt implements:
- **Patent Table 46**: 7-Dimension Composite Fault Fingerprint ($\text{SHA-256}$ digest over normalized signatures).
- **Patent Table 47**: Duplicate Decision Classifier with 4 formal outcomes (`SameFault`, `Variant`, `Related`, `Independent`).
- **Patent Table 45**: 10-State Finding Maturity Lifecycle (`New`, `Phantom`, `Confirming`, `Reproduced`, `CauseSupported`, `FixReady`, `ReTest`, `Verified`, `Waived`, `Rejected`) with strict precondition verification.

### 1.2 Module Architecture

```
                      +------------------------------------------+
                      |         Raw Deviation Observation        |
                      |   (Exception, UI, State, Trace, Env)     |
                      +------------------------------------------+
                                           |
                                           v
                      +------------------------------------------+
                      |       FaultFingerprintEngine (Table 46)  |
                      |   1. Normalise(exception_signature)      |
                      |   2. SemanticDiff(expected, actual)      |
                      |   3. LatticeContext(node, edge, phase)   |
                      |   4. SequenceSketch(minimal_actions)     |
                      |   5. StateDeltaSketch(before, after)     |
                      |   6. DependencyTraceSketch(trace)        |
                      |   7. EnvironmentClass(environment)       |
                      +------------------------------------------+
                                           |
                                           v
                               +-----------------------+
                               | Composite Fingerprint |
                               |      (FP-xxxxxx)      |
                               +-----------------------+
                                           |
                                           v
                      +------------------------------------------+
                      |     DuplicateClassifier (Table 47)       |
                      |   - SameFault (Exact match & repro)      |
                      |   - Variant (Same cause, diff env/input) |
                      |   - Related (Similar symptom/path)       |
                      |   - Independent (Distinct mechanism)     |
                      +------------------------------------------+
                                           |
                                           v
                      +------------------------------------------+
                      |     FindingMaturityEngine (Table 45)     |
                      |   - 10-State Transition Matrix           |
                      |   - Precondition Evidence Validation     |
                      |   - Immutable Ledger Auditing            |
                      +------------------------------------------+
                                           |
                                           v
                      +------------------------------------------+
                      |          FaultsWorkspace UI              |
                      |   - Table 46 7-Feature Decomposition Card|
                      |   - Table 47 Duplicate Decision Badges   |
                      |   - Table 45 10-State Interactive Track  |
                      +------------------------------------------+
```

---

## 2. Flow Design

### 2.1 Composite Fault Fingerprint Computation (Patent Table 46)
$$\text{Fingerprint} = \text{SHA-256}\left(\bigoplus_{i=1}^7 F_i\right)_{[0..32]}$$
where components $F_1 \dots F_7$ are joined with ASCII unit separator `\u001F`:
1. $F_1 = \text{Normalise}(exception\_signature)$: Normalized exception class, top stack frames, stripping volatile tokens (`0x[0-9a-fA-F]+`, GUIDs, ISO timestamps, PIDs, ports, and line numbers).
2. $F_2 = \text{SemanticDiff}(expected, actual)$: Semantic category (`StatusCodeMismatch`, `UiStateCorruption`, `SchemaViolation`, `ContractViolation`, `ResourceDrift`, `CrashFatalExit`, `UnresponsiveWindow`, `AuthenticationDenied`, `InvariantBroken`) paired with difference vector.
3. $F_3 = \text{LatticeContext}(node, edge, workflow\_position)$: Normalized State Lattice node identifier, edge identifier, and workflow breadcrumb.
4. $F_4 = \text{SequenceSketch}(minimal\_actions)$: Canonical hash of minimal ordered action sequence tokens ($[\text{action}:\text{target}]_1^k$).
5. $F_5 = \text{StateDeltaSketch}(before, after)$: Hash of mutated entity keys and modified property flags.
6. $F_6 = \text{DependencyTraceSketch}(trace)$: Normalized service call chain, RPC/HTTP method, and external dependency identity.
7. $F_7 = \text{EnvironmentClass}(environment)$: Target environment tier (`Local`, `Staging`, `Production`), OS family (`Windows`, `Linux`, `macOS`), and runtime architecture (`win-x64`, etc.).

### 2.2 Duplicate Decision Classifier (Patent Table 47)
For an observed deviation $D$ compared against existing findings $\mathcal{F}$ on target $T$:
1. **`SameFault`**: Exact composite fingerprint match ($D.\text{Fingerprint} = F.\text{Fingerprint}$) or matching root causal hypothesis across the compared scope.
   - *Action*: Update existing finding $F$, increment attempts and successes, recalculate Laplace-smoothed probability $\frac{k+1}{n+2}$ and 95% Wilson confidence interval $[p^-, p^+]$. Log `FLT.RECUR`.
2. **`Variant`**: Matches underlying root cause or lattice node ($D.\text{NodeId} = F.\text{NodeId}$ and $D.F_1 = F.F_1$ and $D.F_2 = F.F_2$), but exhibits materially different environment ($D.F_7 \neq F.F_7$) or distinct input parameter/action bounds ($D.F_4 \neq F.F_4$).
   - *Action*: Register as new finding with `VariantOf = F.Id`. Inherits causal hypothesis and links to parent. Log `FLT.VARIANT`.
3. **`Related`**: Matches symptom or dependency path ($D.F_1 = F.F_1$ or $D.F_6 = F.F_6$), but causal equivalence is unproven.
   - *Action*: Register as new finding with `RelatedCandidateId = F.Id`. Renders in UI as clustered candidate. Log `FLT.RELATED`.
4. **`Independent`**: Distinct mechanism ($D$ shares no underlying cause or signature features with known findings).
   - *Action*: Register as new independent finding. Log `FLT.NEW`.

### 2.3 10-State Maturity Lifecycle Engine (Patent Table 45)
A finding $F$ advances through states governed by evidence-backed guards:
1. `New`: Newly detected deviation, confirmation incomplete.
2. `Phantom`: Intermittent recurrence rate ($\text{ReproAttempts} \ge 3$, $\text{Reproducibility} < 0.50$), reproduction envelope incomplete.
3. `Confirming`: Active reproduction attempts being scheduled.
4. `Reproduced`: Formal reproducer passes threshold ($\text{ReproSuccesses} \ge 1$ and $\text{Reproducibility} \ge 0.50$).
5. `CauseSupported`: At least one counterfactual hypothesis supported by empirical intervention ($\text{Confidence} \ge 0.70$).
6. `FixReady`: Bounded Fix Brief approved with defined allowed/forbidden scope.
7. `ReTest`: Repair applied and post-fix validation executing.
8. `Verified`: Repair passes all original and adjacent regression guards, sealed evidence attached.
9. `Waived`: Formally accepted by named authority, reason, and expiration timestamp.
10. `Rejected`: Proven invalid oracle, test harness defect, or intended behavior.

---

## 3. Constraints & Invariants

1. **Deterministic Normalisation Invariant**: Fingerprint calculation must be 100% idempotent. Two identical runs must yield bit-for-bit identical fingerprints.
2. **Anti-Collapsing Invariant**: Never collapse independent root causes because of generic HTTP 500 or UI generic error text. $F_1$ through $F_6$ prevent surface symptom collapse.
3. **Evidence-Gated Transition Invariant**: Maturity cannot be advanced arbitrarily without meeting precondition evidence contracts (e.g. cannot reach `Verified` without passing regression verification).

---

## 4. Failure Handling & Recovery

1. **Missing or Incomplete Telemetry**: If dependency traces or state deltas are unavailable, normalizer inserts `<UNTRACKED>` and flags uncertainty interval rather than failing.
2. **Volatile Token Leakage**: Fallback regex sweeps strip hex strings and digits from unknown exception messages.
3. **Database Concurrency**: Read/write operations to SQLite findings table use safe parameterised queries with transaction isolation.

---

## 5. Scalability

1. **Fingerprint Indexing**: SQLite `Findings` table indices on `Fingerprint` and `TargetId` enable $O(1)$ duplicate lookup.
2. **Fast Hash Computation**: Composite SHA-256 hashes execute in $< 5\,\mu\text{s}$ per deviation.
3. **In-Memory Caching**: Recent target fingerprints cached in memory for sub-millisecond sweep deduplication.

---

## 6. Verification Plan

1. **Decomposition Tests**: Verify all 7 Table 46 features correctly extract, normalize, and produce deterministic `FP-` hashes.
2. **Duplicate Classifier Tests**: Verify exact matches (`SameFault`), environment shifts (`Variant`), symptom overlaps (`Related`), and distinct defects (`Independent`).
3. **Maturity Transition Tests**: Verify all 10 states, verifying valid transitions succeed and unverified transitions are rejected with descriptive messages.
4. **UI Visual Tests**: Verify Faults workspace renders Table 46 feature cards, Table 47 outcome badges, and interactive Table 45 maturity track.
