# Context

_Read this first. It is what is true now, not a history._

## What this project is

**Ghozt - Dev Tester** (visible shell brand **Ghozt**, portfolio **CAPITAL-IZ Ghozt**, publisher **CAPITAL-IZ**): an autonomous software forensics, quality certification, and development acceleration platform. It registers targets, executes governed sweeps, records every observation to an immutable SQLite WAL event ledger, captures and hashes evidence, and evaluates release policy to issue signed clearance decisions.

## Current state

- **Repository**: `https://github.com/capital-iz/GHOZT` (`main` branch), git root `C:\Users\reama\Soft Dev\CAPITAL-IZ\Apps\Desktop\GHOZT`.
- **Stack**: C# / WPF / .NET 8 (`net8.0-windows10.0.19041.0`), SQLite WAL (`ghozt.db`), xUnit (`Ghozt.Tests`), WiX 5 self-contained MSI packaging.
- **Projects**:
  - `Ghozt.Theming`: Contrast arithmetic, 12 palettes x 2 modes, mathematical contrast enforcement, strictly generated tokens (`Text.*`, `Surface.*`, `Border.*`, `Accent.*`, `State.*`, `Ghozt.*`, `Authority.*`).
  - `Ghozt.Core`: Domain model, prefix-keyed transactional IDs (`TGT-`, `SWP-`, `FLT-`, `EVD-`, `EVT-`), SQLite WAL plane, SHA-256 fingerprinting, 7-check `SweepEngine` (pass / deviation / blocked).
  - `Ghozt.App`: WPF shell, window chrome, status strip, operations rail, forensics inspector, black-box tray, command palette (Ctrl+K), visual capture engine (`--capture`), 6 implemented workspaces (Targets, Sweep, State Map, Faults, Evidence, Settings), and 5 planned workspaces (Replay, Algorithms, Regressions, Reports, Clearance).
  - `Ghozt.Tests`: 143 passing unit tests.
  - `Windows/Installer`: WiX 5 `Package.wxs`, automated branding generator (`build-brand-assets.py`), automated MSI builder (`build-msi.ps1`), single-command iteration pipeline (`iterate.ps1`).

## Constraints and standing decisions

- **Language Policy (Amended 2026-09-11)**: C#, Rust, and Python are preferred languages. A repo already in a language STAYS in it. This supersedes DIR-20260820-006. Ghozt stays C#/WPF; Rust is permitted where beneficial and authorized.
- **Strict Execution Permission**: Edit only what the owner explicitly authorizes. No unrequested refactors.
- **Verification-First Doctrine**: A passing build or test suite is not a working app. Every UI change requires `Ghozt.exe --capture` and visual inspection of rendered output.
- **Design System (§12 Ghozt adaptation)**:
  - Universal Design System v2.0 (`UNIVERSAL DESIGN SYSTEM - CAPITAL-IZ DOCTRINE.docx` §12) and Menial v.2 command picker specifications (`2026-09-11-command-picker-feature-specification.md`).
  - No literal colors anywhere; resolve via published tokens.
  - Exactly one `Accent.Fill` per screen for primary action.
  - Gold is authority, not emphasis (approvals, clearance, complete evidence seals).
  - Live numerals are tabular. No decorative data.
  - One outer backdrop filter behind the main panel; never per card (Law 15).
- **Packaging Invariant**: Installers carry 100% of dependencies (self-contained publish `win-x64`, 256+ files, ~57 MB MSI).

## Open questions & backlog

- **UI Overhaul**: Rework app UI to align deeply with Universal Design System v2.0 and Menial v.2 command picker principles (dense, elegant floating window styling, micro-labels, crisp typography, consistent hierarchy).
- **Five Placeholder Workspaces**: Replay, Algorithms, Regressions, Reports, Clearance are currently marked planned.
- **Motion Layer**: Token durations exist in `Ghozt.Theming` but are unconsumed in XAML.
- **Redacted Mono Palette Accent**: Accent `#CDA661` converges with authority gold; needs owner decision.
- **Sandboxed Test Isolation**: Capture and test runs currently hit `%LOCALAPPDATA%\CAPITAL-IZ\Ghozt\ghozt.db` (violates doctrine §25.2); needs sandboxed data root.
- **Registry Slug Registration**: `ghozt` slug on `admin.capital-iz.com` pending owner approval before initial VPS publish.
