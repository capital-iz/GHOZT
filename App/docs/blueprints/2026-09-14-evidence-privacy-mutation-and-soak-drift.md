# Ghozt — Evidence Privacy, Guard Mutation, and Soak Drift Engine: Build Blueprint

| Field | Value |
|---|---|
| Document | Build blueprint (mechanism, alternatives, failure paths, contracts, test cross-reference) |
| Product | Ghozt - Dev Tester · visible brand **Ghozt** · portfolio **Capital-Iz Ghozt** |
| Scope | `Apps\Desktop\GHOZT\App` — `Ghozt.Core` & `Ghozt.App` (`RedactionEngine`, `RegressionEngine`, `SoakAnalysisEngine`, `EvidenceWorkspace`, `RegressionsWorkspace`) |
| Date | 2026-09-14 |
| Status | Architecture implemented; 220/220 automated tests verified green |
| Governance | Universal Design System v2.0 §12, Menial v.2 UI Canon, Capital-Iz Execution Doctrine |
| Patent Reference | Ghozt Patent Architecture Blueprint §21 (Soak & Recurrence), §22/§35 (Privacy Validation & Evidence Redaction), §31 (Mutation & False-Confidence Controls) |

---

## 1. Executive Summary & Objective

This blueprint specifies the architecture and implementation of three mission-critical functional engines derived from the Ghozt Patent-Level Architecture Blueprint:

1. **Evidence Privacy & Redaction Engine (`RedactionEngine.cs` — Patent §22, §35)**: Deterministic PII and secret masking (Bearer tokens, JWTs, API keys, client secrets, passwords, connection strings, PEM private keys, emails) with cryptographic lineage, `Redacted = true` indicators, and `EVD.REDACT` ledger events.
2. **Regression Guard Mutation Testing & False-Confidence Assay (`RegressionEngine.cs` — Patent §31)**: Automated fault seeding across active regression guards to identify and flag tautological/ineffective guards (`RGR.MUTATION.KILLED` vs `RGR.MUTATION.SURVIVED`) and compute the mutation efficacy score $\mu$.
3. **Soak & Resilience Performance Drift Engine (`SoakAnalysisEngine.cs` — Patent §21)**: Linear regression modeling ($\beta = \frac{\Delta \text{MB}}{\Delta t}$, $R^2$) over resource time-series telemetry to detect deterministic memory, handle, and thread leaks, automatically generating `PhantomFinding` deviations in the store.

---

## 2. System Architecture & Components

```
┌────────────────────────────────────────────────────────────────────────────────┐
│                             GHOZT CORE ARCHITECTURE                            │
│                                                                                │
│  ┌─────────────────────────┐  ┌───────────────────────┐  ┌──────────────────┐  │
│  │     RedactionEngine     │  │   RegressionEngine    │  │SoakAnalysisEngine│  │
│  │  (Patent §22 & §35)     │  │     (Patent §31)      │  │   (Patent §21)   │  │
│  │                         │  │                       │  │                  │  │
│  │ • Regex Token Masker    │  │ • Guard Generation    │  │ • OLS Regression │  │
│  │ • Secret Category Assays│  │ • Mutation Operators  │  │ • Leak Slopes    │  │
│  │ • SHA-256 Resealer      │  │ • Tautology Assays    │  │ • R² Fit Assay   │  │
│  │ • Lineage Preserver     │  │ • Efficacy Score (μ)  │  │ • Phantom Seeder │  │
│  └────────────┬────────────┘  └───────────┬───────────┘  └────────┬─────────┘  │
│               │                           │                       │            │
├───────────────┼───────────────────────────┼───────────────────────┼────────────┤
│               ▼                           ▼                       ▼            │
│  ┌──────────────────────────────────────────────────────────────────────────┐  │
│  │                      GhoztStore (SQLite WAL Database)                    │  │
│  │                                                                          │  │
│  │  • Evidence Table (Redacted = 1, New Hash, Masked Payload)               │  │
│  │  • Events Table (EVD.REDACT, RGR.MUTATION.*, SOAK.DRIFT.DETECTED)        │  │
│  │  • Findings Table (Deterministic Leaks, Phantom Invariant Flag)          │  │
│  └──────────────────────────────────────────────────────────────────────────┘  │
│                                           ▲                                    │
├───────────────────────────────────────────┼────────────────────────────────────┤
│                                           │                                    │
│  ┌─────────────────────────┐              │              ┌──────────────────┐  │
│  │    EvidenceWorkspace    │──────────────┴──────────────│RegressionsWorksp.│  │
│  │                         │                             │                  │  │
│  │ • "Redact" (Single)     │                             │ • "Run Suite"    │  │
│  │ • "Redact secrets" (All)│                             │ • "Run Mutation" │  │
│  │ • REDACTED Badge Tags   │                             │ • Score Badge (μ)│  │
│  └─────────────────────────┘                             └──────────────────┘  │
└────────────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Mechanism Specifications

### 3.1 Evidence Privacy & Redaction Engine (`RedactionEngine.cs`)
- **Secret Pattern Recognition**:
  - PEM Private Keys: `-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----` $\rightarrow$ `[REDACTED:PRIVATE_KEY]`
  - Bearer / OAuth Tokens: `(?i)\bBearer\s+[a-zA-Z0-9_\-\.]{12,}\b` $\rightarrow$ `Bearer [REDACTED:TOKEN]`
  - JWT Tokens: `\beyJ[a-zA-Z0-9_-]{10,}\.eyJ[a-zA-Z0-9_-]{10,}\.[a-zA-Z0-9_\-\.]+\b` $\rightarrow$ `[REDACTED:JWT]`
  - API Keys: `(?i)\b(?<prefix>(?:api[_-]?key|client[_-]?secret|access[_-]?token|app[_-]?secret|auth[_-]?token)\s*[:=]\s*["']?)(?!\[REDACTED:)[a-zA-Z0-9_\-]{16,}(?<suffix>["']?)` $\rightarrow$ `${prefix}[REDACTED:API_KEY]${suffix}`
  - Connection Strings: `(?i)(?<prefix>(?:Password|Pwd)\s*=\s*)(?!\[REDACTED:)[^;]+(?<suffix>;|$)` $\rightarrow$ `${prefix}[REDACTED:DB_PASSWORD]${suffix}`
  - Passwords: `(?i)\b(?<prefix>(?:password|pwd)\s*[:=]\s*["']?)(?!\[REDACTED:)[^"'\s,;]{4,}(?<suffix>["']?)` $\rightarrow$ `${prefix}[REDACTED:PASSWORD]${suffix}`
  - Emails: `\b[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}\b` $\rightarrow$ `[REDACTED:EMAIL]`
- **Idempotency & Re-Redaction Prevention**: Uses negative lookaheads `(?!\[REDACTED:)` and non-overlapping rule sequences so existing redacted markers are never recursively re-masked.
- **Lineage Preservation**: Updates `Evidence.Redacted = true`, recomputes SHA-256 over sanitized payload, logs `EVD.REDACT` ledger event with previous and new digest for immutable auditability.

### 3.2 Regression Guard Mutation Testing (`RegressionEngine.cs`)
- **Mutation Operators**:
  - `InvertPathExistence`: Mutates reachable target path to non-existent GUID path.
  - `WipeBuildIdentity`: Mutates target build version to empty string.
  - `InvertMaturityToUnresolved`: Mutates resolved finding state back to `FindingMaturity.New`.
- **Efficacy Determination**:
  - If guard fails when presented with mutated defect state $\rightarrow$ Mutant is **KILLED** (`MutationOutcome.Killed`). Guard is active and effective.
  - If guard passes when presented with mutated defect state $\rightarrow$ Mutant **SURVIVED** (`MutationOutcome.Survived`). Guard is flagged as tautological / false-confidence.
  - Mutation Efficacy Score: $\mu = \frac{\text{Mutants Killed}}{\text{Total Mutants}}$.

### 3.3 Soak Drift Analysis (`SoakAnalysisEngine.cs`)
- **Least-Squares Regression**:
  - Computes $\beta = \frac{SS_{ty}}{SS_{tt}}$ and $R^2 = \frac{SS_{ty}^2}{SS_{tt} \cdot SS_{yy}}$ over resource time series.
  - Thresholds: Memory growth $\beta \ge 0.05 \text{ MB/s}$ (~180 MB/hr) with $R^2 \ge 0.85$; Handles $\beta \ge 0.10 \text{ handles/s}$; Threads $\beta \ge 0.02 \text{ threads/s}$.
  - Automatically seeds high-severity `PhantomFinding` in the store upon critical drift detection.

---

## 4. Rejected Alternatives

| Alternative | Reason Rejected |
|---|---|
| Destructive raw payload deletion (`Payload = ""`) | Breaks retrospective verification and prevents forensic re-analysis; replacing sensitive tokens with structured tags preserves telemetry structure. |
| Global string replacement without regex boundary matching | Collides on ordinary strings (e.g. replacing `secret` inside `client_secret` or replacing innocent substrings), destroying non-secret data. |
| Retrying flaky regression guards until green | Violates Patent §31 ("Flaky guards are investigated as findings, not simply retried until green"); masks real race conditions. |
| Simple two-point memory delta ($\Delta M = M_{\text{end}} - M_{\text{start}}$) | Extremely vulnerable to GC cycles, temporary allocation spikes, and false positives; linear regression with $R^2$ fit validates genuine sustained monotonic drift. |

---

## 5. Enumerated Behaviours & Test Cross-Reference

| Behaviour ID | Description | Verifying Test |
|---|---|---|
| BEH-PRV-001 | Detects sensitive credentials, keys, tokens, and PII across unstructured payloads | `PrivacyAndResilienceTests.RedactionEngine_ContainsSensitiveData_DetectsCredentialsAndTokens` |
| BEH-PRV-002 | Deterministically replaces credentials with typed redaction tokens while preserving structure | `PrivacyAndResilienceTests.RedactionEngine_Redact_ReplacesSecretsWithMaskTokens_PreservesStructure` |
| BEH-PRV-003 | Re-seals evidence with updated SHA-256 and records immutable `EVD.REDACT` ledger event | `PrivacyAndResilienceTests.RedactionEngine_RedactEvidence_UpdatesPayloadAndHash_PreservesLineageInLedger` |
| BEH-PRV-004 | Batch scans and redacts all unredacted artefacts containing sensitive data for a target | `PrivacyAndResilienceTests.RedactionEngine_RedactTargetEvidence_ScansAndBatchSanitizes` |
| BEH-MUT-001 | Seeds controlled faults into target invariants, verifies that guards detect them, and computes $\mu$ | `PrivacyAndResilienceTests.RegressionEngine_VerifyGuardEfficacyAsync_KillsMutantsAndDetectsEffectiveGuards` |
| BEH-SOAK-001| Detects deterministic memory growth using OLS slope $\beta$ and $R^2$ coefficient | `PrivacyAndResilienceTests.SoakAnalysisEngine_CalculateDrift_DetectsDeterministicMemoryLeak` |
| BEH-SOAK-002| Rejects flat or fluctuating memory telemetry with low slope as nominal stability | `PrivacyAndResilienceTests.SoakAnalysisEngine_CalculateDrift_RejectsFlatOrFluctuatingTelemetry` |
| BEH-SOAK-003| Automatically generates `PhantomFinding` in the store when sustained leak is observed | `PrivacyAndResilienceTests.SoakAnalysisEngine_Analyze_GeneratesPhantomFinding_WhenCriticalDriftObserved` |

---

## 6. Failure Paths & Containment

| Failure Path | Detection | Mitigation / Recovery |
|---|---|---|
| Re-redacting already redacted payload | RedactionEngine detects existing `[REDACTED:*]` pattern or checks `evidence.Redacted` | No-op return; skips already masked tokens to prevent token nesting. |
| Zero variance in telemetry timestamps ($SS_{tt} \le 10^{-9}$) | SoakAnalysisEngine checks $SS_{tt}$ | Returns nominal stability with 0 slope and 0 $R^2$; prevents division by zero. |
| Missing target in workspaces | Workspaces check `Shell?.Target is null` | Disables action buttons, clears panels, and displays clean empty state prompt. |
