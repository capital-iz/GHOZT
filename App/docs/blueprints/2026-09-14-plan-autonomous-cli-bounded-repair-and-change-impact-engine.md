# Implementation Plan: Autonomous Headless CLI Runner, Bounded Repair Verification & Change Impact Engine

- **Paired Blueprint**: `2026-09-14-autonomous-cli-bounded-repair-and-change-impact-engine.md` (GZT-BLD-009)
- **Target Release**: v0.8.0
- **Status**: Executing

## 1. Engine Implementation
1. Create `HeadlessRunner.cs` in `App/src/Ghozt.Core/`:
   - Command-line parsing (`--headless`, `--sweep`, `--mode`, `--sarif`, `--junit`, `--clearance-cert`).
   - Autonomous sweep dispatch via `SweepEngine`.
   - Report export via `ReportGenerator`.
   - Clearance certificate generation via `ClearanceEngine`.
   - Standardized exit codes (0, 1, 2, 3).
2. Create `RepairVerifier.cs` in `App/src/Ghozt.Core/`:
   - `RepairVerificationResult` data model.
   - Enforce 7 Patent §30 rejection rules.
   - Execute repro verification, regression guard evaluation, finding status resolution, and event logging.
3. Create `ChangeImpactEngine.cs` in `App/src/Ghozt.Core/`:
   - `ChangeImpactAssessment` model.
   - Map source paths to `LatticeNode` and `RegressionGuard`.
   - Identify affected checks, risk level, and minimal test scope.

## 2. Shell & UI Integration
1. Wire CLI execution in `App.xaml.cs`:
   - Detect `--headless` or `--sweep`.
   - Run `HeadlessRunner.RunAsync` and exit without launching GUI.
2. In `FaultsWorkspace.xaml` & `.xaml.cs`:
   - Wire "Verify Repair" button calling `RepairVerifier`.
3. In `RegressionsWorkspace.xaml` & `.xaml.cs`:
   - Wire "Change Impact Analysis" drawer.

## 3. Testing & Verification
1. Author `RepairVerificationAndChangeImpactTests.cs` (7 test cases).
2. Perform assertion mutation testing (RED -> GREEN).
3. Package `Ghozt-0.8.0.msi`, install to Program Files, verify, and commit.
