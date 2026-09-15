# Implementation Plan: Ghozt v1.0.4 — Authorised Security Audit Engine, Compatibility Matrix Engine & Clearance Gates 10-11

**Milestone:** v1.0.4  
**Date:** 2026-09-15  
**Author:** Antigravity  
**Governing Blueprints:**
- `Blueprints/GHOZT - PATENT-LEVEL FUNCTIONAL AND SYSTEM ARCHITECTURE BLUEPRINT.docx` (§22, Table 36, Table 38)
- `Apps/Desktop/GHOZT/AGENTS.md`

---

## 1. Goal Description
Implement and integrate:
1. **Patent Table 36: Authorised Security Audit Engine** evaluating 8 canonical security validation areas (`IdentityAndSession`, `Authorisation`, `InputHandling`, `SecretsAndCredentials`, `TransportAndTrust`, `ClientStorage`, `AbuseAndRateControl`, `SecurityObservability`).
2. **Patent Table 38: Compatibility Matrix Engine** evaluating 5 canonical compatibility axes (`OperatingSystem`, `DisplayAndScaling`, `LocaleAndCulture`, `RuntimeAndDependencies`, `UpgradeAndMigration`).
3. **Multi-Axis Clearance Gates (Gates 11 & 12)** in `ClearanceEngine` ensuring commercial software releases satisfy strict security and compatibility envelopes.
4. **Interactive Clearance Workspace UI** exposing the 5-axis Compatibility Matrix and 8-area Security Audit diagnostics.

---

## 2. Proposed Changes

### Core Models & Contracts
- `App/src/Ghozt.Core/Model.cs`:
  - Enums: `SecurityValidationArea`, `CompatibilityAxis`.
  - Records: `SecurityAuditAreaResult`, `SecurityAuditReport`, `CompatibilityAxisResult`, `CompatibilityMatrixReport`.

### Core Verification Engines
- `App/src/Ghozt.Core/GhoztStore.cs`:
  - Added `VerifyDatabaseIntegrity()` for SQLite `PRAGMA integrity_check;` and `PRAGMA foreign_key_check;`.
- `App/src/Ghozt.Core/SecurityAuditEngine.cs`:
  - Implements 8 Table 36 audit areas, cryptographic report signatures, and automatic finding emission for High/Critical breaches.
- `App/src/Ghozt.Core/CompatibilityMatrixEngine.cs`:
  - Evaluates Windows host environment, DPI scaling bounds, culture/locale numeric invariance, CLR dependencies, and database migration integrity.
- `App/src/Ghozt.Core/ClearanceEngine.cs`:
  - Added Gate 11 (`GATE.COMPATIBILITY_MATRIX`) and Gate 12 (`GATE.SECURITY_AUDIT`).

### UI Layer
- `App/src/Ghozt.App/Workspaces/ClearanceWorkspace.xaml`:
  - Added "Security & Compatibility Assay" button.
- `App/src/Ghozt.App/Workspaces/ClearanceWorkspace.xaml.cs`:
  - Added `OnRunSecurityAndCompatibilityAssay` and `RenderAssayResults` rendering Table 36 and Table 38 cards.

### Tests & Verification
- `App/tests/Ghozt.Tests/SecurityAuditAndCompatibilityMatrixTests.cs`:
  - Unit and property tests verifying all 8 security areas, 5 compatibility axes, culture invariance, and clearance integration.
- Mutation test proof on clearance gate enforcement.

---

## 3. Verification Plan
- `dotnet test App\tests\Ghozt.Tests\Ghozt.Tests.csproj -c Release`
- Mutation test execution (invert condition, verify RED, restore to GREEN).
- Self-contained MSI package build via `.\iterate.ps1`.
