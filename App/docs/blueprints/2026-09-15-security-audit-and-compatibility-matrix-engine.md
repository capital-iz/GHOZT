# GZT-BLD-015: Authorised Security Audit Engine, Compatibility Matrix Engine & Multi-Axis Clearance Gates Blueprint

**Author:** Antigravity  
**Date:** 2026-09-15  
**Status:** Approved & Implemented  
**Governing Blueprints:**
- `Blueprints/GHOZT - PATENT-LEVEL FUNCTIONAL AND SYSTEM ARCHITECTURE BLUEPRINT.docx` (§22, Table 36, Table 38)
- `Apps/Desktop/GHOZT/AGENTS.md` (Doctrine §§2, 4, 7, 8, 9, 12, 17, 21, 22, 23, 25)

---

## 1. System Structure

### 1.1 Context and Problem Statement
A high-integrity software certification platform cannot limit its verification to functional correctness and performance soak tests alone. Prior to commercial release clearance, two fundamental cross-cutting risk envelopes must be deterministically assayed:
1. **Security Vulnerability Envelope (Patent §22, Table 36)**: Whether target application endpoints, configuration metadata, process launch strings, and authorization boundaries are vulnerable to credential leakage, command injection, plaintext transport, privilege escalation, or lack of auditable observability.
2. **Compatibility and Environmental Envelope (Patent §22, Table 38)**: Whether target software behaves identically and reliably across heterogeneous operating system architectures, display scaling ratios (100%..200%), internationalization locales and numeric formatting cultures, host CLR runtimes, and database schema migrations.

To address these imperatives without introducing destabilizing, destructive sweeps, Ghozt v1.0.4 introduces the **Authorised Security Audit Engine** and the **Compatibility Matrix Engine**, coupled directly into Ghozt's release policy gates as **Gate 11 (`GATE.COMPATIBILITY_MATRIX`)** and **Gate 12 (`GATE.SECURITY_AUDIT`)**.

---

### 1.2 Module Architecture

```
                                    +-----------------------------------------+
                                    |              Target Under Test          |
                                    |  - BaseAddress, Environment, Access     |
                                    |  - LaunchCommand, Metadata, Notes       |
                                    +-----------------------------------------+
                                                         |
                            +----------------------------+----------------------------+
                            |                                                         |
                            v                                                         v
         +--------------------------------------+                  +--------------------------------------+
         |         SecurityAuditEngine          |                  |       CompatibilityMatrixEngine      |
         |         (Patent §22, Table 36)       |                  |         (Patent §22, Table 38)       |
         +--------------------------------------+                  +--------------------------------------+
         | 1. Identity & Session Security       |                  | 1. Operating System & Architecture   |
         | 2. Privilege & Authority Bounds      |                  | 2. Display DPI & Responsive Bounds   |
         | 3. Input Handling & Injection        |                  | 3. Locale & Cultural Invariance      |
         | 4. Secrets & Credential Hygiene      |                  | 4. CLR Runtime & Storage             |
         | 5. Transport Security (TLS)          |                  | 5. Schema Evolution & Migration      |
         | 6. Client Storage & Sandbox          |                  +--------------------------------------+
         | 7. Execution Quota & Rate Control    |                                     |
         | 8. Security Observability            |                                     |
         +--------------------------------------+                                     |
                            |                                                         |
                            v                                                         v
         +--------------------------------------+                  +--------------------------------------+
         |         SecurityAuditReport          |                  |       CompatibilityMatrixReport      |
         |   - SHA-256 Audit Signature          |                  |   - SHA-256 Matrix Digest            |
         |   - 8 Area Validation Results        |                  |   - 5 Axis Diagnostic Vectors        |
         +--------------------------------------+                  +--------------------------------------+
                            \                                                         /
                             \                                                       /
                              v                                                     v
                            +---------------------------------------------------------+
                            |                     ClearanceEngine                     |
                            |  Gate 11: GATE.COMPATIBILITY_MATRIX                     |
                            |  Gate 12: GATE.SECURITY_AUDIT                           |
                            +---------------------------------------------------------+
                                                         |
                                                         v
                            +---------------------------------------------------------+
                            |                   ClearanceDecision                     |
                            |  (Cryptographically sealed release certificate)        |
                            +---------------------------------------------------------+
```

---

## 2. Flow Design

### 2.1 Table 36: Security Audit Verification Areas
The Security Audit Engine executes sequentially across the 8 canonical validation areas:
1. **Identity & Session Security (`IdentityAndSession`)**: Evaluates validity and isolation of base address endpoints and session configurations.
2. **Authorisation & Privilege Bounds (`Authorisation`)**: Verifies presence of unexpired, cryptographically signed `AuthorityGrant` tokens (`AUT-xxxxxx`) for state-changing targets (`ExecutionIsolationLevel > L0_Observe`).
3. **Input Handling & Command Injection (`InputHandling`)**: Assays target launch commands and path inputs for shell injection delimiters (`;`, `&&`, `||`, `` ` ``, `$()`).
4. **Secrets & Credential Hygiene (`SecretsAndCredentials`)**: Employs `RedactionEngine` heuristics to inspect target configurations and notes for exposed API keys, bearer tokens, or plaintext credentials.
5. **Transport Security & Protocol Trust (`TransportAndTrust`)**: Mandates TLS 1.2+ (`https://`) for targets configured in `Production` or `Staging` environments; flags plaintext `http://` as High severity violations.
6. **Client Storage & Sandbox Isolation (`ClientStorage`)**: Verifies database and artefact storage resides strictly within user application data sandboxes (`%LOCALAPPDATA%` or ephemeral testing temp folders).
7. **Abuse & Rate Control (`AbuseAndRateControl`)**: Asserts that automated sweeps maintain finite, bounded resource budgets (`MaxSeconds <= 3600`, `MaxStates <= 10000`).
8. **Security Observability & Audit Trail (`SecurityObservability`)**: Verifies that the tamper-evident event ledger is active, operational, and recording recent system actions.

### 2.2 Table 38: Compatibility Matrix Axes
The Compatibility Matrix Engine assays 5 environmental axes:
1. **Operating System (`OperatingSystem`)**: Assays host platform against Windows 10/11 x64/Arm64 baseline.
2. **Display & Scaling (`DisplayAndScaling`)**: Verifies display DPI awareness scaling envelopes (100%..200%), minimum responsive viewport constraints ($1024 \times 700$ DIPs), and contrast ratio compliance (> 4.5:1 WCAG AA across 12 standard palettes).
3. **Locale & Culture (`LocaleAndCulture`)**: Evaluates numerical formatting invariance across period (`.`) and comma (`,`) decimal separators in Invariant, en-US, de-DE, and fr-FR cultures; verifies ISO 8601 millisecond-precision roundtripping; evaluates a +30% text expansion tolerance buffer.
4. **Runtime & Dependencies (`RuntimeAndDependencies`)**: Verifies .NET CLR runtime major version >= 8, native SQLite binary linkage, and filesystem write access.
5. **Upgrade & Migration (`UpgradeAndMigration`)**: Executes SQLite `PRAGMA integrity_check;` and `PRAGMA foreign_key_check;` through `GhoztStore.VerifyDatabaseIntegrity()`.

---

## 3. Constraints & Invariants

1. **Non-Destructive Invariant**: Security audits never exploit or inject destructive payloads without an explicit signed authority grant.
2. **Fail-Closed Gate Invariant**: Gates 11 and 12 are `Mandatory = true`. If any compatibility axis fails or any High/Critical security breach is detected, `ClearanceOutcome` is evaluated as `Denied`.
3. **Cryptographic Sealing**: All security audit reports emit a SHA-256 `AuditSignature` and all compatibility matrix reports emit a SHA-256 `MatrixDigest`, both stored in the tamper-evident ledger (`LedgerEvent`).
4. **Three-State Honesty**: Inaccessible or unmeasurable dimensions are recorded as failed or unverified, never defaulted to zero or assumed passing.

---

## 4. Failure Handling & Recovery

| Failure Mode | Detection Mechanism | Mitigation / Recovery |
|---|---|---|
| Plaintext HTTP transport on production target | `AuditTransportAndTrust` detects `http://` prefix | High severity finding recorded; Gate 12 blocks clearance; remediation suggests upgrading to `https://`. |
| Malicious delimiter in launch command | `AuditInputHandling` scans for `;`, `&&`, `||`, etc. | Critical severity finding recorded; Gate 12 immediately blocks release. |
| Database corruption or FK constraint break | `VerifyDatabaseIntegrity()` executes PRAGMA checks | Gate 11 blocks clearance; details emitted with offending row count. |
| Numeric decimal confusion in international culture | `EvaluateLocaleAndCulture` parses formatted string | Gate 11 fails; requires code to use `CultureInfo.InvariantCulture`. |

---

## 5. Verification Evidence

### Automated Test Coverage (`SecurityAuditAndCompatibilityMatrixTests.cs`)
- `SecurityAuditEngine_All8Areas_EvaluatesAndRecordsFindings`: Verifies all 8 Table 36 areas evaluate cleanly on valid target.
- `SecurityAuditEngine_InsecureHttpEndpoint_EmitsTransportFinding`: Verifies `http://` in production target emits High severity defect.
- `SecurityAuditEngine_UnsanitizedSecret_FlagsCredentialFinding`: Verifies unredacted API key in metadata is detected.
- `CompatibilityMatrixEngine_All5Axes_PassOnStandardHost`: Verifies all 5 Table 38 axes pass on valid host.
- `CompatibilityMatrixEngine_LocaleInvariance_SurvivesMultipleCultures`: Verifies numeric and timestamp parsing across Invariant, US, German, and French cultures.
- `ClearanceEngine_IncludesGates11And12_AndEnforcesZeroViolations`: Verifies that Gates 11 and 12 are evaluated and block clearance upon violations.
