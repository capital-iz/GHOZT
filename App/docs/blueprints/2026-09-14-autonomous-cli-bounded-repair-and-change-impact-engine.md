# Ghozt Build Blueprint: Autonomous Headless CLI Runner, Bounded Repair Verification & Change Impact Engine

- **Identifier**: GZT-BLD-009
- **Date**: 2026-09-14
- **Author**: CAPITAL-IZ Engineering Authority
- **Status**: Implemented
- **Patent Cross-Reference**: Patent §30 (Fix Brief & Bounded Repair Execution), §31 (Regression Memory & Change Impact), §38 (Deployment Topology & Headless Execution)

---

## 1. Context and Problem Statement

Ghozt is an autonomous software quality examination and verification platform. While its WPF desktop shell provides a rich 12-palette visual environment for operators, production engineering and CI/CD pipelines require unattended, headless execution that can:
1. Sweep targets autonomously from terminal invocations or CI pipelines, outputting standardized SARIF v2.1.0 and JUnit XML reports with deterministic exit codes.
2. Formally verify that code changes made by AI agents (Claude Code, Antigravity, Codex) actually fix identified defects without violating Fix Brief bounds or triggering regressions (Patent §30).
3. Analyze change impact: when source files change, determine exactly which State Lattice nodes and Regression Guards are impacted, avoiding wasteful full sweeps while ensuring zero false confidence (Patent §31).

---

## 2. Architecture & Components

### 2.1 Headless Autonomous CLI Runner (`HeadlessRunner.cs` — Patent §38)
- Invoked via `Ghozt.exe --headless [options]` or `Ghozt.exe --sweep <target>`.
- Parses target identifiers (by ID or name), sweep modes (`smoke`, `deep`, `soak`), and export paths (`--sarif`, `--junit`, `--clearance-cert`).
- Suppresses GUI initialization; writes high-contrast structured status to console stdout.
- Computes target clearance, writes sealed evidence to SQLite, exports artifacts, and returns:
  - `0`: Qualified / Cleared with zero Critical/High deviations.
  - `1`: Clearance Refused (defects detected).
  - `2`: Target Unreachable or missing.
  - `3`: Invalid CLI arguments.

### 2.2 Bounded Repair Verification Pipeline (`RepairVerifier.cs` — Patent §30)
- Converts a `FixBrief` into an active verification run against the updated target:
  - Re-executes the recorded minimal reproduction sequence.
  - Evaluates whether the fault fingerprint recurs.
  - Evaluates all active regression guards on the target.
  - Enforces the 7 mandatory rejection conditions from Patent §30:
    1. Scope violation outside `AllowedScope`.
    2. Symptom masking where underlying root mechanism persists.
    3. Missing verification evidence.
    4. Weakened oracles or suppressed errors.
    5. Test data corruption or disabled features.
    6. Newly introduced regressions.
    7. Unverified claims without cryptographic proof.
- Transitions `FixBriefStatus` to `Verified` or `Rejected`.
- Transitions `Finding.Maturity` to `Resolved` upon successful verification.
- Appends `SYS.REPAIR.VERIFIED` or `SYS.REPAIR.REJECTED` to the immutable BlackBox ledger.

### 2.3 Change Impact Analysis Engine (`ChangeImpactEngine.cs` — Patent §31)
- Accepts a list of modified files, git diff hunks, or endpoint signatures.
- Maps modified paths to State Lattice nodes (`LatticeNode`) using path heuristics and endpoint mappings.
- Identifies active `RegressionGuard` instances associated with the affected nodes.
- Calculates the minimal prioritized sweep subset required to guarantee regression protection.
- Computes an overall change risk rating (`Low`, `Medium`, `High`, `Critical`).

---

## 3. UI Integrations

1. **Faults Workspace**:
   - "Verify Repair" button in action bar.
   - Evaluates repair status via `RepairVerifier`, marks finding resolved, and surfaces audit proof.
2. **Regressions Workspace**:
   - "Change Impact Analysis" drawer allowing operators to input changed paths or inspect git diffs.

---

## 4. Verification & Invariants

- **Zero False Confidence**: An unverified claim or missing run must never be marked as resolved or passed (Doctrine §9).
- **Three-State Honesty**: If reproduction cannot run, report `Unverified`, never assume repaired.
- **Headless Isolation**: CLI execution runs with isolated temporary or default SQLite stores without polluting user profiles or leaking unhandled exceptions.
