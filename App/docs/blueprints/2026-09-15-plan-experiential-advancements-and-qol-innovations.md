# Implementation Plan: Experiential Advancements & QoL Innovations

**Date**: 2026-09-15  
**Product**: CAPITAL-IZ Ghozt — Dev Tester  
**Author**: Antigravity  
**Status**: Approved & In Execution  

---

## 1. Executive Summary

This plan addresses practical experiential completion across the Ghozt Dev Tester desktop application:
- Smart Target Deduplication & Canary Pruning
- Active Target Visual Glow & Status Indication
- Live Sweep Cockpit with Observable Check Transitions & Post-Sweep Navigation
- State Lattice In-Place Discovery & Forensic Node Inspector
- In-App Report & Dossier Viewer
- Sandboxed Database Startup Flag (`--db <path>`) for complete test isolation

---

## 2. Affected Components & File Manifest

1. `App/src/Ghozt.App/App.xaml.cs`: Support `--db <path>` in GUI/Interactive mode.
2. `App/src/Ghozt.App/Workspaces/TargetsWorkspace.xaml` & `.xaml.cs`:
   - Idempotent `OnRegisterLocalHost`.
   - "Prune Canaries" toolbar button (`OnPruneCanaries`).
   - Active Target border glow and `ACTIVE` chip badge.
   - Quick action buttons on target card.
3. `App/src/Ghozt.App/Workspaces/SweepWorkspace.xaml` & `.xaml.cs`:
   - Observable `PlanRow` with live check states: `Pending`, `Running`, `Passed`, `Blocked`, `Deviation`.
   - Post-sweep action bar: `[Explore in State Lattice]`, `[Triage Findings]`, `[Evaluate Clearance]`.
4. `App/src/Ghozt.App/Workspaces/StateMapWorkspace.xaml` & `.xaml.cs`:
   - In-place `[Run Discovery Sweep]` button in empty state.
   - Right-docked `NodeInspector` drawer rendering full node metadata, details, and transitions.
5. `App/src/Ghozt.App/Workspaces/ReportsWorkspace.xaml` & `.xaml.cs`:
   - Embedded `DocumentPreviewDrawer` with styled reader and close action.
6. `App/tests/Ghozt.Tests/`: Unit and integration test coverage for target pruning and check state transitions.

---

## 3. Verification & Acceptance Criteria

- All 292+ tests pass in `Ghozt.Tests.dll`.
- Mutation testing assertion verified (RED $\to$ GREEN).
- Hands-on runtime verification via UI Automation (`run-desktop-e2e`) with visual captures.
- Clean MSI package build and installation.
- Zero uncommitted files, zero dangling background tasks.
