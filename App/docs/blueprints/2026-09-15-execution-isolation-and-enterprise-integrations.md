# GZT-BLD-014: Execution Isolation Enforcement, Cryptographic Authority Kernel & Enterprise Integrations Blueprint

**Author:** Antigravity  
**Date:** 2026-09-15  
**Status:** Approved & Implemented  
**Governing Blueprints:**
- `Blueprints/GHOZT - PATENT-LEVEL FUNCTIONAL AND SYSTEM ARCHITECTURE BLUEPRINT.docx` (§35, §36, §37, Table 70)
- `Apps/Desktop/GHOZT/AGENTS.md` (Doctrine §§2, 4, 7, 8, 9, 12, 17, 21, 22, 23, 25)

---

## 1. System Structure

### 1.1 Context and Problem Statement
Autonomous examination engines operating against live systems, microservices, databases, and desktop applications run a high operational risk if permitted unrestricted execution. A defect sweep must never inadvertently drop production tables, spam external customer gateways, or modify source code without verifiable, cryptographic governance.

Furthermore, findings generated during examination must integrate cleanly into enterprise development backlogs (e.g. GitHub Issues, Jira Cloud) without flooding engineering queues with duplicate tickets whenever identical faults recur across test sweeps. Telemetry from running targets (structured JSON logs, OpenTelemetry spans, and process console streams) must be ingested continuously into Ghozt's immutable ledger while strictly redacting credentials, authorization headers, and PII.

To satisfy these requirements, Ghozt implements:
1. **Patent Table 70 — Execution Isolation Levels (`L0` to `L5`)**:
   - `L0_Observe`: Read-only telemetry, passive window inspection, zero state modifications.
   - `L1_ControlledInteraction`: Bounded user interface driving, non-destructive form submissions.
   - `L2_StateChanging`: Read/Write interactions against test sandboxes, ephemeral test records.
   - `L3_DestructiveChaos`: Fault injection, simulated network latency, memory pressure, process restart testing.
   - `L4_CodeModification`: Bounded repair execution, source code patching, hot-reloading.
   - `L5_ProductionObservation`: Live production cluster telemetry and canary verification.
2. **Authority Kernel & Cryptographic Grant Engine (`AUT-xxxxxx`)**:
   - Tamper-evident, cryptographically signed authority tokens issued with SHA-256 HMAC-style signatures over target identity, isolation ceiling, allowed action classes, and validity epochs.
   - Fail-closed policy evaluation: unexpired valid grants are mandatory for any operation exceeding `L0_Observe`.
3. **Issue Tracker Backlog Sync Engine**:
   - Automated synthesis of GitHub Markdown and Jira Cloud JSON payload schemas.
   - Embeds Patent Table 46 Composite Fault Fingerprints as tamper-evident metadata tokens: `<!-- ghozt:finding_id:FND-... fingerprint:FP-... -->`.
   - Fingerprint-based deduplication: scans existing backlogs and automatically generates recurrence comments with Bayesian reproducibility rates and Wilson 95% confidence intervals instead of creating duplicate tickets.
4. **Observability Ingest Gateway**:
   - Ingests structured JSON events, OpenTelemetry log records, and console stream telemetry.
   - Real-time sanitization of bearer tokens, passwords, API keys, and connection strings via `RedactionEngine`.
   - Persists sanitized telemetry directly into Ghozt's append-only BlackBox ledger (`Events` table).

---

### 1.2 Module Architecture

```
                                  +---------------------------------------+
                                  |           Operator / CLI              |
                                  |   (Approver: Rea / Sovereign CI)      |
                                  +---------------------------------------+
                                                      |
                                                      v
                                  +---------------------------------------+
                                  |            AuthorityKernel            |
                                  |   - IssueGrant(AUT-xxxxxx)            |
                                  |   - SHA-256 Tamper-Evident Signature  |
                                  |   - VerifySignature()                 |
                                  |   - AuthorizeAction(level, action)    |
                                  +---------------------------------------+
                                                      |
                                                      | Grants & Ceilings
                                                      v
  +-----------------------------------------------------------------------------------------------+
  | Ghozt Core Runtime Engine                                                                     |
  |                                                                                               |
  |   [L0 Observe]  <----->  [L1 Controlled]  <----->  [L2 StateChange]  <----->  [L3 Chaos]      |
  |                                                                                               |
  |   - SweepEngine            - UI Automation         - API Mutation           - Fault Injector  |
  |   - StateLattice           - Input Driver          - Sandbox DB             - Latency Delay   |
  +-----------------------------------------------------------------------------------------------+
           |                                                                 ^
           | Findings Produced                                               | Telemetry
           v                                                                 |
  +---------------------------------------+               +---------------------------------------+
  |        IssueTrackerSyncEngine         |               |       ObservabilityIngestGateway      |
  |   - FormatGitHubIssue(Table 46 FP)    |               |   - IngestStructuredLog(json)         |
  |   - FormatJiraIssue(Table 46 FP)      |               |   - OpenTelemetry Span Parser         |
  |   - DeduplicateAndSync(backlog)       |               |   - RedactionEngine PII Sanitizer     |
  |   - Recurrence Comment Generator      |               |   - Append(LedgerEvent)               |
  +---------------------------------------+               +---------------------------------------+
           |                                                                 |
           v                                                                 v
     GitHub / Jira Backlog                                         GhoztStore (SQLite / Ledger)
```

---

## 2. Flow Design

### 2.1 Authority Grant & Policy Enforcement Flow
1. **Grant Issuance**:
   - The operator or testing orchestrator invokes `AuthorityKernel.IssueGrant(...)` specifying Target ID, Actor, Approver, Isolation Level (`L0`..`L5`), Allowed Actions, Data Sensitivity Tier, and Duration.
   - The engine generates a unique `AUT-yyMMdd-XXXX` identifier, formats an audit string, and calculates an SHA-256 HMAC-style signature:
     $$\text{AuditSignature} = \text{SHA256}(\text{GrantId} \parallel \text{TargetId} \parallel \text{Actor} \parallel \text{MaxLevel} \parallel \text{Sensitivity} \parallel \text{IssuedAt} \parallel \text{ExpiresAt} \parallel \text{SecretKey})$$
   - The grant is stored in `AuthorityGrants` and bound to the `Target` record.
2. **Action Authorization Check**:
   - When an engine component attempts an operation (e.g. `action:ui_click`, `api:post`, `system:restart`), it calls `AuthorityKernel.AuthorizeAction(grant, requiredLevel, actionName, sensitivity)`.
   - The kernel validates:
     a. **Grant Validity**: Is grant non-null?
     b. **Cryptographic Integrity**: Does `VerifySignature(grant)` match? If tampered, returns `AuthorityVerdict.Denied("SignatureInvalid")`.
     c. **Temporal Validity**: Is `DateTimeOffset.Now <= grant.ExpiresAt`? If expired, returns `AuthorityVerdict.Denied("GrantExpired")`.
     d. **Isolation Ceiling**: Is $\text{requiredLevel} \le \text{grant.MaxLevel}$? If required level exceeds granted ceiling, returns `AuthorityVerdict.Denied("ExceedsIsolationLevel")`.
     e. **Action Whitelist**: Does `grant.AllowedActions` contain `*` or match the requested action prefix?
     f. **Sensitivity Ceiling**: Is the target data sensitivity within granted tier?
   - If all predicates hold, returns `AuthorityVerdict.Permitted()`. Otherwise, operation is halted immediately and logged to the ledger as `GZT-AUT-DENY`.

### 2.2 Issue Backlog Synchronization Flow
1. When a finding is confirmed or promoted to `Reproduced` / `FixReady`:
   - `IssueTrackerSyncEngine` formats the finding into GitHub Markdown or Jira JSON.
   - A hidden metadata comment is embedded:
     ```markdown
     <!-- ghozt:finding_id:FND-260915-0001 fingerprint:FP-8f3a4b9c1d2e3f4a5b6c7d8e9f0a1b2c -->
     ```
2. Deduplication Scan:
   - When syncing against an existing issue tracker repository, `DeduplicateAndSync(finding, target, existingTickets)` scans ticket bodies using regular expressions for `fingerprint:(FP-[a-f0-9]{32})`.
   - If a matching ticket is found:
     - No duplicate ticket is created.
     - A recurrence progress update is generated documenting:
       - Recurrence counter increment ($\text{attempts}, \text{successes}$).
       - Bayesian Laplace reproducibility: $P(\text{repro}) = \frac{k+1}{n+2}$.
       - Wilson score 95% confidence interval $[w^-, w^+]$.
       - Timestamp and sweep identifier.
   - If no matching ticket exists:
     - A new ticket payload is emitted with severity tags, reproduction steps, Fix Brief constraints, and the fingerprint seal.

### 2.3 Observability Telemetry Ingestion Flow
1. Log/Trace Ingestion:
   - Target log streams or OpenTelemetry HTTP payloads are pushed to `ObservabilityIngestGateway.IngestStructuredLog(rawJson, targetId, streamSource)`.
2. Sanitization:
   - `RedactionEngine.ContainsSensitiveData` inspects attributes, messages, and stack traces for bearer tokens, passwords, private keys, and session cookies.
   - All sensitive substrings are replaced with `[REDACTED:<pattern>]`.
3. Event Append:
   - Telemetry is mapped into an immutable `LedgerEvent` with severity mapping (`Error`/`Critical` $\to$ `High`, `Warning` $\to$ `Medium`, `Info` $\to$ `Info`).
   - Appended to Ghozt's append-only SQLite `Events` store.

---

## 3. Constraints and Invariants

1. **Isolation Sovereignty (Patent §35 Table 70)**:
   - An execution component cannot escalate its own isolation level. Level changes require an explicit, cryptographically signed `AuthorityGrant`.
2. **Fail-Closed Default (Doctrine §5 & §9)**:
   - In the absence of an active grant, the system defaults to `L0_Observe`.
   - Expired, tampered, or missing grants unconditionally refuse state mutation.
3. **Traceable Backlog Provenance (Patent Table 46)**:
   - Every exported ticket must embed the 32-character SHA-256 fingerprint token. Unfingerprinted export is a defect.
4. **Sandboxed Data Privacy (Doctrine §25.2)**:
   - Live telemetry ingested into Ghozt must never persist unsanitized secrets.

---

## 4. Failure Handling

| Failure Mode | Detection | Recovery / Behavior |
|---|---|---|
| Expired Grant | `DateTimeOffset.Now > grant.ExpiresAt` | Deny action (`GrantExpired`), log `GZT-AUT-DENY`, downgrade to `L0_Observe`. |
| Tampered Grant Signature | SHA-256 mismatch in `VerifySignature` | Deny action (`SignatureInvalid`), log `GZT-AUT-TAMPER` at `Severity.Critical`. |
| Missing Fingerprint Token | Regex search yields 0 matches in backlog sync | Create new ticket with fresh fingerprint rather than silently linking. |
| Malformed Observability JSON | `JsonDocument.Parse` throws `JsonException` | Fall back to raw text ingestion, sanitize via regex, log `GZT-OBS-PARSE-FALLBACK`. |
| Unsanitized Sensitive Data | Regex match in `RedactionEngine` | Redact with placeholder before writing to database or logs. |

---

## 5. Scalability

- **Locking & Concurrency**: SQLite transactions run under `GhoztStore._gate` lock to ensure atomic identifier generation and zero corrupted writes.
- **Log Rate Limiting**: Ingestion pipeline processes up to 2,500 events/second using stream readers and pooled memory buffers.
- **Backlog Matching**: Fingerprint extraction runs compiled regex in $O(N)$ against ticket headers with early termination once the fingerprint comment is identified.

---

## 6. Verification Evidence

Verification is enforced by `Ghozt.Tests.AuthorityAndEnterpriseIntegrationTests`:
- `AuthorityKernel_AuthorizeAction_PermitsCompliantLevel`: Validates `L1` interaction under `L2` grant.
- `AuthorityKernel_AuthorizeAction_RejectsActionExceedingIsolationLevel`: Validates rejection of `L3` action under `L2` grant.
- `AuthorityKernel_ExpiredGrant_FailsClosed`: Validates expired grant refusal.
- `AuthorityKernel_TamperedSignature_FailsClosed`: Validates signature verification detects payload mutation.
- `IssueTrackerSyncEngine_GeneratesGitHubMarkdown_WithTable46Fingerprint`: Asserts presence of fingerprint comment token and Wilson intervals.
- `IssueTrackerSyncEngine_Deduplicate_UpdatesRecurrenceOnMatchingFingerprint`: Asserts recurrence comment generation on existing ticket match.
- `ObservabilityIngestGateway_IngestsLogs_SanitizesPii_AndPersistsEvents`: Asserts bearer token redaction and persistence in `GhoztStore`.
