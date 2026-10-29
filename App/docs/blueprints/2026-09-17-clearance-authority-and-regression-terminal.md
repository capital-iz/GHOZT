# Ghozt Dev Tester — Clearance Authority Panel & Regression Guard Mutation Terminal

**Blueprint ID:** GZT-UX-004  
**Date:** 2026-09-17  
**Status:** Approved  
**Author:** CAPITAL-IZ Systems Architecture  

---

## 1. System Structure

### 1.1 Clearance Workspace — Cryptographic Authority Clearance Panel (GZT-CLR-001 through GZT-CLR-012)
- **Host Component:** `ClearanceWorkspace.xaml` and `ClearanceWorkspace.xaml.cs`.
- **Layout Architecture:** Responsive two-column master-detail layout:
  - **Left Pane (`Width="*"`):**
    - Scrollable view of the 12 Release Policy Gates table with status badges (`PASS`, `FAIL`, `WARN`), requirement criteria, evidence notes, and direct remediation routing buttons (`Triage Open Faults →`, `Review & Seal Evidence →`, `Inspect Regressions →`, `Execute Target Sweep →`).
    - Alternate views for Autonomous Self-Test Certificate (12 domains) and Security & Compatibility Assays.
  - **Right Pane (`Width="480"`):** `ClearanceAuthorityPanel`
    - **Cryptographic Authority Seal Card:** Gold authority card (`Authority.Clearance`, `Authority.Soft`, `Authority.Fill`) rendering official clearance status (`RELEASE AUTHORIZED` / `RELEASE BLOCKED` / `UNEVALUATED`), target ID, build label, evaluation timestamp, and signatory credentials (`release-approver@capital-iz.com`).
    - **Evidence Plane Tamper Verification Assay:** Dynamic cryptographic validation of all target evidence payloads against SHA-256 checksums (`0 TAMPERED / 100% INTACT`), total evidence count, and active Merkle root digest.
    - **Risk Envelope & Gate Metrics:** Pass ratio (`X/12`), mandatory gate clearance status, active target access class (`ObserveOnly`, `Interact`, `StateChanging`, `Destructive`), and unmitigated defect counters.
    - **Authority Action Launchpad:** Quick-action triggers for `Sign & Seal Decision`, `Export Signed Certificate`, and `Verify Tamper Integrity`.

### 1.2 Regressions Workspace — Guard Operations & Mutation Assay Terminal (Patent §31)
- **Host Component:** `RegressionsWorkspace.xaml` and `RegressionsWorkspace.xaml.cs`.
- **Layout Architecture:** Responsive two-column layout:
  - **Left Pane (`Width="*"`):**
    - Filterable, selectable cards list of all active regression guards derived from confirmed findings.
    - Card selection highlights the active guard and synchronizes the right inspector.
  - **Right Pane (`Width="480"`):** `GuardOperationsPanel` / `RegressionGuardInspector`
    - **Selected Guard Invariant Dossier:** Guard ID, title, severity, invariant statement (`ExpectedBehavior`) vs observed failure (`Actual`), first seen and last evaluated timestamps.
    - **Mutation Assay & Efficacy Matrix:** Mutation score metric ($\mu = X\%$), killed vs survived mutant counts, and false-confidence detection (`HIGH FIDELITY / MUTANT SENSITIVE` vs `INEFFECTIVE / FALSE-CONFIDENCE RISK`).
    - **Change Impact & Source Trace:** Correlation with State Lattice nodes, affected source code files, and risk level indicator (`Low`, `Medium`, `High`, `Critical`).
    - **Operational Controls:** `Execute Selected Guard`, `Run Mutation Assays`, `Analyze Change Impact`, `Export Suite (.json)`.

---

## 2. Constraints & Invariants
- **Gold Authority Invariant (Law 21 & GZT-ID-001):** Gold is reserved strictly for Authority and Release Clearance Seals. Ordinary UI controls or metrics must not use gold.
- **Three-State Honesty Invariant (Doctrine §9):** Absence of evidence, unmeasured metrics, or null values must display as `UNVERIFIED` or `UNKNOWN`, never defaulted to zero or passed.
- **Zero Live Contamination (Doctrine §25.2):** All test and capture suites run against isolated sandbox databases.
- **Design System Invariant:** All visual tokens must resolve through the CAPITAL-IZ 12-palette token engine (`Authority.*`, `State.*`, `Surface.*`, `Border.*`, `Text.*`).
