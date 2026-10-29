# Ghozt — Shell and Theme Engine: Build Blueprint

| Field | Value |
|---|---|
| Document | Build blueprint (mechanism, alternatives, failure paths) |
| Product | Ghozt - Dev Tester |
| Scope | `Apps\Desktop\GHOZT\App` — theme engine, domain core, sweep engine, WPF shell, six workspaces |
| Date | 2026-08-31 |
| Status | Implemented and verified against a running application |
| Sources | GZT-UX-001 (UI/UX & 12-Theme), GZT-ID-001 (Identity), GZT-ARCH (Functional & System Architecture), CAPITAL-IZ Universal Design System v2.0 §12 |

---

## 1. What was built

Four projects under `App/`:

| Project | Target | Responsibility |
|---|---|---|
| `Ghozt.Theming` | `net8.0` | Colour arithmetic, twelve palettes, token generation, contrast guarantee. No UI dependency. |
| `Ghozt.Core` | `net8.0` | Identifiers, domain model, SQLite evidence plane, hashing, sweep engine. No UI dependency. |
| `Ghozt.App` | `net8.0-windows` (WPF) | Shell, component grammar, six implemented workspaces, visual capture. |
| `Ghozt.Tests` | `net8.0-windows` | 137 tests: contrast sweep, catalogue invariants, resource coverage. |

`net8.0-windows10.0.19041.0` matches CommanDeck and Mouse n Key so the suite's installer tooling,
theming discipline and registry pipeline stay one thing rather than two.

## 2. The theme engine

### 2.1 Mechanism

A palette declares thirteen fields. Every token is derived:

```
base/panel/elevated  →  holdSide()   keeps each on its mode's side of the ink crossover (L = 0.1791)
selected             →  BoundToCard() caps the accent tint at 1.5× the card's luminance
reading surfaces     →  seven composited surfaces a text token can land on
text tokens          →  EnsureContrastAcross() pushes toward the winning ink until AA clears
semantic tokens      →  SemanticInk(): the anchor pushed to legibility, hue preserved
accent ink           →  additionally pushed clear of the authority gold
wash stops           →  admit(): strongest alpha that leaves every reading surface on side
```

### 2.2 Rejected alternatives

| Alternative | Why rejected |
|---|---|
| Hand-authored palettes | 12 palettes × 2 modes × opacity range × finish matrix is not maintainable by eye, and the failures are invisible until an operator moves a slider. |
| Map onto WPF `ColorScheme`-style slots | Roughly thirty slots against a hundred-token vocabulary; collapsing loses the card/panel/input distinctions the contrast guarantee is measured against. |
| Separate every semantic colour from every other | Implemented, then removed. Mixing toward the mode's pole destroys hue: Phantom Violet's light mode drove Warning from amber to `#000000`. The design system forbids only three confusions, and those hold by construction. |
| Fix bleached inks by pushing text harder | The cause was a surface, not an ink. Ghostwire's selected row reached luminance 0.051 against a 0.018 card, forcing every ink toward white. Bounding the surface fixed all of them at once. |

### 2.3 Failure paths

| Failure | Behaviour |
|---|---|
| Unknown token name | Debug/test: throws. Release: falls back to `Surface.Deep` — a real, themed, opaque surface — and reports once. A screen never fails to open over a colour. |
| Unknown palette key in settings | Falls back to `spectral_black` rather than throwing. A stale preference must not stop the shell opening. |
| Out-of-range finish dial | Re-clamped on read, so a value written by an older build cannot produce an out-of-range finish. |
| Persisted preference unwritable | The look still applies for the session; the write failure is logged, not surfaced as an error. |

## 3. The evidence plane

SQLite from the first commit, not a stub. WAL journal so a long read does not block a running sweep.
Identifiers are allocated in a transaction under a process-wide lock: two writers claiming one
identifier would silently overwrite an entity, and the overwritten one would be the evidence.

Schema: `Targets`, `Sweeps`, `LatticeNodes`, `LatticeEdges`, `Findings`, `Evidence`, `Events`,
`Sequences`, `Settings`.

## 4. The sweep engine

Seven checks — Reachability, Identity, Structure, Integrity, Capacity, Consistency, Provenance —
selected by mode. Every check measures something real on this machine and produces a hashed artefact
whatever its outcome, including passes.

Three outcomes, deliberately distinct: **pass**, **deviation**, **blocked**. Blocked is neither a
pass nor a failure; conflating it with either is how untested scope gets recorded as proven.

### Failure paths

| Failure | Behaviour |
|---|---|
| A check throws | Recorded as a blocked check carrying the exception, and the sweep continues. The first version let it propagate and one unreadable file destroyed the evidence from every check that had already passed. |
| Budget exhausted | Run ends as `Partial`, not `Completed`. Untested scope is marked, never assumed to pass. |
| Operator stops the run | Ends as `Cancelled`; completed checks and their artefacts are preserved. |
| Target file held open by another process | `Sha256File` shares read/write/delete. A system under examination almost always has its own files open. |
| Duplicate deviation | Clustered onto the existing finding by fingerprint, incrementing reproduction counters. The fingerprint excludes timestamp and run id, which would make every occurrence unique. |

## 5. Defects found by running the application

Every one of these passed the build and would have passed a test that only asserted structure.

| # | Defect | Cause | Fix |
|---|---|---|---|
| 1 | Shell would not open at all | Text **styles** and text **colour tokens** shared keys (`Text.Body` et al). The later-merged brush replaced the style, and WPF was handed a colour where it expected a `Style`. | Styles moved to `Type.*`. `ResourceCoverageTests` now refuses the collision. |
| 2 | Status strip and operations rail rendered as nothing | No `ThemeInfo` attribute, so `Themes/Generic.xaml` was never found and the two templated controls had no template. Silent: present in the tree, zero size, no error. | `AssemblyInfo.cs` declares `ThemeInfo`. |
| 3 | Empty-state panel drew over fifteen live rows | `IsEmptyConverter` returned `bool` into a `Visibility` property, which coerces to Visible whatever the value. | Returns `Visibility`. |
| 4 | Faults showed "nothing found" while the header said 2 findings | Workspaces populated from `Loaded`, which fires after the first render. | Also populate from `DataContextChanged`; capture drains to `ContextIdle`. |
| 5 | Inspector labels brighter than their values | Two style keys inside a ternary were missed by the rename and did not exist. `TryFindResource` returns null silently. | Explicit foregrounds; `ResourceCoverageTests` sweeps every key in source. |
| 6 | Settings crashed on open | Slider handlers fire during XAML parsing, before the labels they write to exist. | Guard on `IsInitialized` — not on one field, which was the first, insufficient attempt. |
| 7 | Light mode inspector values nearly invisible | Code-built elements assigned the result of `TryFindResource`, capturing a **frozen brush**. A palette change replaces the brush; the holder keeps the old one. | `SetResourceReference` throughout; code-drawn surfaces rebuild on `ThemeChanged`. |
| 8 | Sweep died on its first real target | `File.OpenRead` requests `FileShare.Read`, which fails against a file another process holds. | Share read/write/delete, and blocked-check handling above. |

## 6. Verification

- **137 tests pass.** Contrast is swept across 12 palettes × 2 modes × 6 opacities × 5 finishes.
- **Mutation-tested**: `BoundToCard`'s headroom was widened to 99; four forbidden-pair assertions
  went red naming the exact palettes; restored.
- **Rendered and inspected**: 24 catalogue images (12 palettes × 2 modes) plus per-workspace
  captures, produced by `Ghozt.exe --capture <dir> [--all-palettes] [--workspace <name>]` from the
  live visual tree.
- **Run for real**: the sweep engine was driven against two genuine targets — an existing file and a
  path that does not exist — producing 7 passing checks and 2 real deviations.

## 7. Known gaps

- Replay, Algorithms, Regressions, Reports and Clearance render a **specified, not implemented**
  notice listing what they will contain. They show no invented data.
- Reachability defers HTTP targets to the capability probe; the sweep engine performs filesystem
  checks in this build.
- Redacted Mono declares the authority gold as its own accent (GZT-UX-001 §03), contradicting §02
  and §17. Carried as an explicit, reasoned exemption in `ThemeCatalogTests`. **Needs an owner's
  decision.**
- Not yet published to the VPS registry; no installer produced.
