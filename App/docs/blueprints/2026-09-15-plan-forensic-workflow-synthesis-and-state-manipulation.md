# Plan: Forensic Workflow Synthesis & Interactive State Manipulation

**Date**: 2026-09-15  
**Product**: Capital-Iz Ghozt — Dev Tester  
**Author**: Antigravity  
**Status**: Execution Phase  

---

## 1. Execution Steps

1. **Shell Navigation Handoff Architecture (`ShellViewModel.cs`)**:
   - Introduce `RequestedSweepId`, `RequestedFindingId`, `RequestedNodeId`, `RequestedEvidenceId`.
   - Implement `NavigateToReplay`, `NavigateToStateMap`, `NavigateToFaults`, `NavigateToEvidence`, and `NavigateToSweep`.

2. **State Map Workspace Enhancements (`StateMapWorkspace.xaml`, `.xaml.cs`)**:
   - Implement interactive mouse dragging for `LatticeNode` elements.
   - Dynamically update attached `Line` coordinates in real-time.
   - Persist node coordinates `(X, Y)` to `LatticeNodes` table in SQLite upon drag completion.
   - Implement `SelectAndFocusNode(string nodeId)` and listen to `RequestedNodeId`.
   - Add `[ View Linked Faults ]` inside `NodeInspectorDrawer`.

3. **Faults Workspace Interconnectivity (`FaultsWorkspace.xaml`, `.xaml.cs`)**:
   - Add `[ Replay Fault ]`, `[ Locate on State Map ]`, and `[ View Evidence ]` buttons.
   - Route `finding.SweepId` to `ReplayWorkspace`.
   - Route `finding.NodeId` to `StateMapWorkspace`.
   - Route `finding.Id` to `EvidenceWorkspace`.
   - Respond to `RequestedFindingId` when entered.

4. **In-App Evidence Preview & Media Drawer (`EvidenceWorkspace.xaml`, `.xaml.cs`)**:
   - Add docked `EvidencePreviewDrawer` in `EvidenceWorkspace.xaml`.
   - Implement formatted JSON / log text rendering.
   - Implement decoded PNG/JPEG image rendering for UI screenshot artifacts.
   - Provide copy, export, seal, and redact actions directly in drawer.
   - Link to associated finding in `FaultsWorkspace`.

5. **Replay Workspace Handoffs (`ReplayWorkspace.xaml`, `.xaml.cs`)**:
   - Respond to `RequestedSweepId` and `RequestedFindingId` to automatically load sweep and step.
   - Add `[ Triage in Faults ]` and `[ Inspect in State Map ]` buttons to step details.

6. **Sweep Workspace Live Stream & History Replay (`SweepWorkspace.xaml`, `.xaml.cs`)**:
   - Embed real-time probe console in the execution card.
   - Add inline `[ Replay ]` action button to history list items.

7. **Clearance Workspace Remediation Routing (`ClearanceWorkspace.xaml`, `.xaml.cs`)**:
   - Add action buttons on failed/warning gate cards directly routing to responsible workspaces.

8. **Verification & Packaging**:
   - Run unit/integration tests with `dotnet test`.
   - Run mutation test per AGENTS.md §5.
   - Package self-contained MSI and install locally via `iterate.ps1`.
