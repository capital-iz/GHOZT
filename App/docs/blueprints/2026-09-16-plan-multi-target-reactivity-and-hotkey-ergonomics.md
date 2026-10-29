# Implementation Plan Blueprint: Multi-Target Reactivity, Hotkey Ergonomics, and Empty States Polish (GZT-PLN-020)

**Document ID**: GZT-PLN-020  
**Status**: ACTIVE / VERIFIED  
**Date**: 2026-09-16  
**Product**: CAPITAL-IZ Ghozt — Dev Tester  
**Paired Build Blueprint**: GZT-BLD-020  

---

## 1. Objectives & Scope

1. Eliminate cross-target state retention across `ClearanceWorkspace` and `ReportsWorkspace` upon active target switching.
2. Provide interactive feedback (`FilterEmptyState` + `"Clear filter"`) when search/kind filters in `TargetsWorkspace` exclude all targets.
3. Provide feedback (`NoMatchesNotice`) in `CommandPalette` when a search query yields no matching entries.
4. Auto-expand Black-Box Stream tray when `Ctrl+L` is pressed while collapsed.
5. Add `Alt+NumPad1` through `Alt+NumPad0` workspace navigation shortcuts.
6. Emit `TGT.ACTIVE` audit events to the black-box ledger whenever the active target is switched or cleared.

---

## 2. Implementation Execution Plan

### Step 1: Shell ViewModel & Accelerators
- In `ShellViewModel.cs`, add `Log("TGT.ACTIVE", ...)` within `Target` setter.
- In `MainWindow.xaml.cs`, add `NumPad1`..`NumPad0` keybindings and expand tray on `Ctrl+L`.
- In `CommandPalette.xaml` & `CommandPalette.xaml.cs`, add `NoMatchesNotice` textblock.

### Step 2: Workspaces Target Invalidation
- In `ClearanceWorkspace.xaml.cs`, subscribe to `Shell.PropertyChanged` for `Target`; invalidate decision and re-evaluate or show `EmptyState`.
- In `ReportsWorkspace.xaml.cs`, subscribe to `Shell.PropertyChanged` for `Target`; invalidate report and re-generate or show `EmptyState`.
- In `AlgorithmsWorkspace.xaml.cs`, subscribe to `Shell.PropertyChanged` for `Target`; dynamically update `SealButton.IsEnabled`.
- In `TargetsWorkspace.xaml` & `TargetsWorkspace.xaml.cs`, implement `FilterEmptyState` and `OnClearFilter`.

### Step 3: Automated Invariant Tests & Mutation
- Add `TargetSwitchReactivityAndFilterEmptyStateInvariants` in `RealExecutionTests.cs`.
- Perform mutation testing (break assertion, verify test failure, restore).
- Run full test suite with `dotnet test`.

### Step 4: UI Automation & Visual Screenshot Capture
- Update `deep_forensic_drive.py` to drive multi-target switching, command palette search, filter empty states, and tray focus.
- Capture screenshots into artifacts.

---

## 3. Rollback Plan
- All edits are atomic and version-controlled. Reverting Git commit restores previous state without database schema migration side effects.
