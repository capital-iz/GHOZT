# Build Blueprint: Multi-Target Reactivity, Hotkey Ergonomics, and Empty States Polish (GZT-BLD-020)

**Document ID**: GZT-BLD-020  
**Status**: ACTIVE / VERIFIED  
**Date**: 2026-09-16  
**Product**: Capital-Iz Ghozt — Dev Tester  
**Subsystem**: Shell Infrastructure, Target Management, & Workspace State Isolation  

---

## 1. System Structure

### 1.1 Architecture & Core Components
```
   +-------------------------------------------------------------------------+
   |                        OPERATOR INTERACTION LAYER                       |
   |                                                                         |
   |   [ Command Palette (Ctrl+K/P) ]      [ Targets Workspace (Alt+1) ]    |
   +--------------------+------------------------------------+---------------+
                        |                                    |
                        v                                    v
   +-------------------------------------------------------------------------+
   |                        SHELL VIEW MODEL SOVEREIGNTY                     |
   |                                                                         |
   |  1. Target Setter: Emits TGT.ACTIVE into Black-Box Ledger               |
   |  2. Broadcasts PropertyChanged(Target) to all subscribers               |
   |  3. Resets Events & Findings scopes to current Target ID                |
   +------------------------------------+------------------------------------+
                                        |
     +----------------------------------+----------------------------------+
     |                                  |                                  |
     v                                  v                                  v
+----+--------------------+   +---------+---------------+   +--------------+-----+
|   Clearance Workspace   |   |    Reports Workspace    |   | Algorithms Workspace|
|                         |   |                         |   |                     |
| 1. Discards old decision|   | 1. Discards old report  |   | 1. Dynamically      |
| 2. Re-evaluates target  |   | 2. Re-generates dossier |   |    evaluates        |
| 3. Or renders EmptyState|   | 3. Or renders EmptyState|   |    SealButton.IsEn- |
|    if Target == null    |   |    if Target == null    |   |    abled per target |
+-------------------------+   +-------------------------+   +---------------------+
```

### 1.2 Entities & Event Specifications
- `TGT.ACTIVE`:
  - **Code**: `TGT.ACTIVE`
  - **Severity**: `Severity.Info` (when switching to a valid target) / `Severity.Medium` (when cleared to null).
  - **Message**: `"Active target switched to {target.Name} ({target.Id})."` / `"Active target cleared (no target selected)."`
  - **RelatedId**: Target ID string.
  - **Actor**: `"operator"`.
- `FilterEmptyState` (in `TargetsWorkspace`):
  - Renders when `Shell.Targets.Count > 0 && filtered.Count == 0`.
  - Explains the active query and provides an instant one-click `"Clear filter"` recovery button.
- `NoMatchesNotice` (in `CommandPalette`):
  - Renders when search query returns 0 matching workspaces, targets, or actions.
  - Replaces empty container void with clear feedback.

---

## 2. Flow Design

### 2.1 Multi-Target Switching & Invalidation Flow
1. Operator selects a target in `TargetsWorkspace` or `CommandPalette`.
2. `ShellViewModel.Target` setter executes:
   - Sets backing field `_target`.
   - Fires `OnPropertyChanged(nameof(Target))` and status bar tokens.
   - Logs `TGT.ACTIVE` to SQLite ledger store and prepends to `Events`.
   - Calls `Refresh()`, which resets derived `Events` and `Findings` lists to the new target's ID.
3. Subscribed workspaces react immediately:
   - `ClearanceWorkspace.OnShellChanged`: Clears `_currentDecision`. If `Target == null`, renders `EmptyState` and disables sign/export. If `Target != null`, evaluates release gates for the new target.
   - `ReportsWorkspace.OnShellChanged`: Clears `_currentReport`. If `Target == null`, renders `EmptyState` and disables export buttons. If `Target != null`, compiles forensic report for the new target.
   - `AlgorithmsWorkspace.OnShellChanged`: Checks `SealButton.IsEnabled = Target != null && _lastReport != null`.
   - `FaultsWorkspace`, `StateMapWorkspace`, `SweepWorkspace`, `ReplayWorkspace`, `EvidenceWorkspace`, `RegressionsWorkspace` reload their scoped datasets.
4. No cross-target state bleeding occurs.

### 2.2 Keyboard Accelerators Flow
1. Operator presses `Ctrl+L`:
   - If `Tray == TrayState.Collapsed`, shell expands tray to `TrayState.Peek`.
   - Shell focuses `StreamFilter` text box.
2. Operator presses `Alt+NumPad1` through `Alt+NumPad0`:
   - Shell navigates to corresponding workspace kinds identically to `Alt+D1`..`Alt+D0`.

---

## 3. Constraints & Invariants

1. **Zero Live Pollution (Doctrine §25.2)**:
   - All tests use isolated in-memory or sandboxed temporary databases (`forensic_sandboxed.db`).
2. **Suite Launch Shortcut Guard (Doctrine §2 / AGENTS.md §2 Rule 9)**:
   - `Alt+C` remains strictly unassigned in Ghozt, reserved exclusively for CommanDeck.
3. **No Phantom State**:
   - Workspaces never display cached or stale data from a previously selected target.

---

## 4. Failure Handling

| Failure Scenario | Detection | Recovery Mechanism |
|---|---|---|
| Target removed while actively viewed | `Shell.ReloadTargets()` detects deleted ID | Falls back to first remaining target or null; triggers `TGT.ACTIVE` and resets all workspaces to empty states. |
| Search query filters out all targets | `filtered.Count == 0 && Targets.Count > 0` | Displays `FilterEmptyState` with explanation and `"Clear filter"` reset button. |
| Command palette query returns no results | `entries.Count == 0` | Displays `NoMatchesNotice` ("No matching workspaces, targets, or actions found"). |
| Ctrl+L pressed while black-box tray is hidden | `Tray == TrayState.Collapsed` | Automatically expands tray to `TrayState.Peek` before focusing `StreamFilter`. |

---

## 5. Verification Evidence

- **Automated Tests**:
  - `RealExecutionTests.TargetSwitchReactivityAndFilterEmptyStateInvariants`: Verifies `TGT.ACTIVE` emission and target isolation across clearance and reports engines.
- **Mutation Verification**:
  - Assertion mutated, verified test went RED, restored, verified GREEN.
- **UI Automation (UIA)**:
  - `deep_forensic_drive.py` executed, capturing multi-target switching, command palette search, filter empty states, and tray expansion.
