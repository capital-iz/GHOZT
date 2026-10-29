# Ghozt — Shell and Theme Engine: Product Blueprint

| Field | Value |
|---|---|
| Document | Product blueprint (behaviours, enumerated and cross-referenced to tests) |
| Product | Ghozt - Dev Tester · visible brand **Ghozt** · portfolio **CAPITAL-IZ Ghozt** |
| Date | 2026-08-31 |
| Status | Implemented behaviours verified; planned behaviours marked |

---

## 1. Product promise, as implemented

Ghozt does not tell an operator that something failed. It shows the target, build, environment,
authority, sequence, evidence, reproducibility and verification status behind that conclusion — and
where any of those is absent, it says so rather than leaving a gap that reads as a pass.

## 2. The persistent operator frame

| Region | Behaviour | Verified by |
|---|---|---|
| Restricted status strip | Always visible. Carries target, id, build, environment, access class, connection, run mode, recorder. Production and destructive access are boxed and coloured critical. | Rendered capture; `ThemeCatalogTests` for colour stability |
| Operations rail | Eleven workspaces. Active item marked three ways — accent rail, tinted fill, accent ink. Expands to labels at ≥1800 px. | Rendered capture |
| Workspace header | Badge, title, and a scope line stating the count of sweeps, findings and artefacts for the current target. | Rendered capture |
| Forensics inspector | Follows selection; pinnable. Tabs that cannot answer stay visible with the reason. | Rendered capture |
| Black-box tray | Collapsed / peek / half. Virtualised. Time, id, code, severity badge, message, actor. | Rendered capture |
| Status footer | Evidence-store path and the exact theme digest, so a screenshot is reproducible. | Rendered capture |
| Command palette | Ctrl+K. Workspaces, targets and governed actions. Unavailable actions appear disabled **with the reason**, never hidden. | Implemented |

The selected target never resets implicitly when switching workspaces.

## 3. Enumerated behaviours

### 3.1 Legibility

| # | Behaviour | Test |
|---|---|---|
| B1 | Every text token clears WCAG AA against all seven reading surfaces, for 12 palettes × 2 modes × 6 opacities × 5 finishes | `EveryTextTokenClearsAaOnEverySurfaceItCanLandOn` |
| B2 | Every surface carrying text stays on its mode's side of the ink crossover | `EverySurfaceThatCarriesTextStaysOnItsSideOfTheInkCrossover` |
| B3 | Accent, authority, critical and fault fills each carry a paired legible ink | `AccentAndAuthorityFillsCarryTheirOwnInk` |
| B4 | `Text.Disabled` sits **below** AA on purpose and below `Text.Body` | `DisabledTextIsBelowAaOnPurpose` |

### 3.2 Semantic stability

| # | Behaviour | Test |
|---|---|---|
| B5 | A fault never looks verified; a simulation never looks observed; authority never looks like ordinary emphasis | `ForbiddenPairsNeverConverge` |
| B6 | The selected-row surface is never mistakable for the authority seal | `AnAuthorityDecisionNeverLooksLikeOrdinarySelection` |
| B7 | Every state carries a distinct label and glyph as well as a colour | `EveryStateCarriesALabelAndAGlyphAsWellAsAColour` |
| B8 | Semantic inks stay recognisably their anchor's hue in every palette | `SemanticAnchorsAreIdenticalAcrossEveryPaletteAndMode` |
| B9 | Soft semantic forms keep the untouched anchor, so washes cannot converge | `SoftSemanticFormsAreTheUntouchedAnchor` |
| B10 | The focus ring is never the selection colour | `FocusRingIsNotTheSelectionColour` |
| B11 | Exemptions to B5 must still describe a real conflict | `ExemptionsStillDescribeARealConflict` |

### 3.3 Resource integrity

| # | Behaviour | Test |
|---|---|---|
| B12 | Every resource key the shell asks for exists | `EveryResourceKeyTheShellAsksForExists` |
| B13 | No dictionary key collides with a colour token | `NoStyleSharesAKeyWithAColourToken` |
| B14 | The `Text.*` namespace belongs to the generated table alone | `TheTextNamespaceBelongsToTheTokenTable` |

### 3.4 Catalogue

| # | Behaviour | Test |
|---|---|---|
| B15 | Twelve palettes, unique lower-snake keys, each stating intent and use | `TheBlueprintDeclaresTwelvePalettes`, `KeysAreUniqueAndLowerSnakeCase`, `EveryPaletteDeclaresItsIntentAndItsUse` |
| B16 | The dark ladder ascends base → panel → elevated | `TheDarkLadderAscends` |
| B17 | Every palette generates an identical token set in both modes | `EveryPaletteGeneratesTheSameTokenSetInBothModes` |
| B18 | An unknown palette key falls back rather than throwing | `AnUnknownKeyFallsBackRatherThanThrowing` |

### 3.5 Evidence and truth

| # | Behaviour | Status |
|---|---|---|
| B19 | Every check produces a hashed artefact, including passes | Implemented — verified by a real run |
| B20 | Blocked ≠ passed ≠ failed; all three are preserved and shown distinctly | Implemented |
| B21 | A budget-exhausted run is `Partial`; untested scope is never assumed to pass | Implemented |
| B22 | An artefact cannot be sealed until its hash has been verified in this session | Implemented |
| B23 | Gold appears only on an approval, a complete seal or a clearance — an unsealed artefact gets a plain outline, never a dimmed gold | Implemented |
| B24 | Reproducibility is computed from attempts; "not attempted" shows as an absence, never as zero | Implemented |
| B25 | Deviations cluster by fingerprint rather than duplicating | Implemented — verified by a re-run |
| B26 | Findings cannot be closed in bulk; the control does not exist | Implemented by omission |
| B27 | A finding cannot reach Reproduced without a success, or Verified without evidence | Implemented |

### 3.6 Authority and risk

| # | Behaviour | Status |
|---|---|---|
| B28 | Production targets carry a persistent banner | Implemented |
| B29 | Destructive actions against production are denied by policy, and the control says why | Implemented |
| B30 | Destructive requires the Destructive access class; a lower class disables the control **with the reason** | Implemented |
| B31 | Removing a target requires its name typed back, and names the exact effect | Implemented |
| B32 | A target with no authority grant states that in warning colour, not as blank space | Implemented |
| B33 | Pre-flight separates blockers from warnings; only blockers stop a run | Implemented |

### 3.7 Honesty

| # | Behaviour | Status |
|---|---|---|
| B34 | Unimplemented workspaces say so and list what they will contain — no mock data | Implemented |
| B35 | Every lattice node resolves to a check that actually ran | Implemented |
| B36 | "Nothing has been tested" and "no faults in tested scope" are different messages | Implemented |
| B37 | An empty stream distinguishes "no events" from "your filter excluded everything" | Implemented |
| B38 | Observed / computed / inferred / declared / simulated are labelled distinctly in the inspector | Implemented |
| B39 | A counterfactual edge is dotted and violet, never mistakable for observed | Implemented |

### 3.8 Accessibility and control

| # | Behaviour | Status |
|---|---|---|
| B40 | Colour is never the sole carrier of status | Implemented (B7) |
| B41 | Ctrl+K palette, Ctrl+P switcher, Ctrl+, settings, Ctrl+L filter, Alt+1–0 workspaces, F6 region cycle | Implemented |
| B42 | No destructive action on a single unmodified key | Implemented |
| B43 | Motion Full / Reduced / Off; Off collapses every duration to zero with an identical end state | Implemented |
| B44 | The OS reduced-motion setting overrides a stored preference for Full | Implemented |
| B45 | Density Compact / Standard / Comfortable reflows; never removes | Implemented |
| B46 | Width states at 1800 / 1400 / 1100 / below; the status strip never collapses | Implemented |
| B47 | Every control carries an accessible name; the lattice has a synchronised keyboard tree | Implemented |

## 4. Naming lock

Visible shell says **Ghozt** and nothing else. `Ghozt - Dev Tester` appears only in assembly
metadata. No module name displaces the master brand; no ghost imagery or paranormal language is used
anywhere in the interface.

## 5. What is not built

Replay, Algorithms, Regressions, Reports and Clearance are specified and reachable — rail,
shortcut, header, inspector and scope line all work — but their canvases are not implemented and say
so plainly.

## 6. Open decision for the owner

**Redacted Mono declares `#CDA661` — the authority gold — as its accent** (GZT-UX-001 §03). That
contradicts §02 and §17, which reserve gold for evidence seals, approvals and release clearance. In
that palette an "Approve" label and an ordinary accent label are 13 units apart, effectively
identical. It is carried as an explicit exemption with its reasoning, and separated by label, glyph
and the authority rail. Either the palette's accent changes, or the exemption stands as a documented
property of that palette.
