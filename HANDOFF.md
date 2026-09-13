# Ghozt — Agent Handoff

**Written:** 2026-09-01, 01:30 SAST
**By:** Claude Opus 5 (Claude Code), session `49f73f88-47aa-4967-b2a3-261a22b8b5f8`
**For:** the next coding agent, on any platform
**State at handoff:** `main` @ `3898a6e`+, local and remote in sync, working tree clean, version `0.1.4`+
**Rules reconciled:** every AGENTS.md / CLAUDE.md under `Apps\Desktop` and `Trading` was read and folded into `AGENTS.md` on 2026-09-01

Read `AGENTS.md` in this folder **first**. It carries the standing rules. This file is the current
situation: what exists, what is wrong, what is next.

---

## 0. Start here — the first five minutes

```bash
cd "C:\Users\reama\Soft Dev\Capital-Iz\Apps\Desktop\GHOZT"
git status                      # expect: clean, on main
git log --oneline -5            # read the top SHA; do not trust this document's
git ls-remote origin main       # expect: matches local HEAD
```

Then read, in this order:

1. `AGENTS.md` (this folder) — the rules.
2. `Apps\AGENTS.md` — suite layout. **Note the contradiction warning in AGENTS.md §0.**
3. `App\docs\blueprints\` — three blueprints: shell/theme build, product behaviours, packaging.
4. `Blueprints\*.docx` — the three canonical product blueprints (see §2 for how to read them).

Then **run the app and look at it** before you change anything:

```powershell
cd Windows
.\Installer\msi\build-msi.ps1 -SkipPublish   # or just: dotnet build ..\App\src\Ghozt.App
..\App\src\Ghozt.App\bin\Debug\net8.0-windows10.0.19041.0\Ghozt.exe
```

---

## 1. Where everything is

### Paths

| Thing | Path |
|---|---|
| **Product root / git root** | `C:\Users\reama\Soft Dev\Capital-Iz\Apps\Desktop\GHOZT` |
| Remote | `https://github.com/capital-iz/GHOZT` branch `main` |
| App source | `GHOZT\App\src\` — `Ghozt.Theming`, `Ghozt.Core`, `Ghozt.App` |
| Tests | `GHOZT\App\tests\Ghozt.Tests` |
| Installer | `GHOZT\Windows\Installer\` |
| Iteration script | `GHOZT\Windows\iterate.ps1` |
| Artifacts (gitignored) | `GHOZT\Windows\artifacts\Ghozt-0.1.{0,1,2}.msi` |
| Reference screenshots | `GHOZT\App\docs\screenshots\` |
| Runtime data (not in git) | `%LOCALAPPDATA%\Capital-Iz\Ghozt\ghozt.db` |

### This chat's transcript

`C:\Users\reama\.claude\projects\C--Users-reama-Soft-Dev-Capital-Iz-Apps-Desktop-GHOZT--claude-worktrees-new-session-1d2b9f\49f73f88-47aa-4967-b2a3-261a22b8b5f8.jsonl`

~16 MB JSONL. Every tool call and result. Search it for exact reasoning; do not re-derive decisions
that are recorded there.

### The owner's persistent memory

`C:\Users\reama\.claude\projects\C--Users-reama-Soft-Dev-Capital-Iz-Apps\memory\`

`MEMORY.md` is the index; each `.md` beside it is one standing rule with its reasoning. These are
Claude Code's memory format, but they are **plain markdown** — any agent can and should read them:

| File | Rule |
|---|---|
| `patent-standard-blueprints.md` | Every task ships build + product blueprints |
| `local-is-the-authority.md` | Git is made to match local; other agents' work counts |
| `installers-carry-every-dependency.md` | Must run on a machine that is not the author's |
| `one-house-many-rooms.md` | Suite consistency in substance, not just layout |
| `publish-every-release-to-vps-registry.md` | Every build to admin.capital-iz.com |
| `queue-dont-interrupt.md` | Feedback queues; only an explicit stop redirects |
| `agents-md-is-the-rule-carrier.md` | Every repo carries the rules |
| `four-products-four-places.md` | The suite layout; never nest or duplicate a product |
| `filter-drivers-own-reas-input.md` | Logitech SetPoint / Norton sit below Mouse n Key |
| `input-ownership-is-not-ours-to-take.md` | Name competing input software, never remove it |

### AGENTS.md rule carriers across the suite — all read and reconciled 2026-09-01

| File | Lines | Status |
|---|---|---|
| `Capital-Iz\AGENTS.md` | 127 | global — **partly stale**, names a path that does not exist |
| `Apps\AGENTS.md` | 150 | suite layout — current |
| `Apps\Desktop\Command Deck\AGENTS.md` | 150 | verified against the filesystem 2026-08-31; build/publish procedure, environment facts |
| `Apps\Desktop\Mouse n Key\AGENTS.md` | 150 | sibling product |
| `Trading\Ocular Trading\Windows\AGENTS.md` | 336 | **the richest doctrine** — Rust directive, owner authority, honesty invariants, risk classes, agent boundaries |
| `Trading\Ocular Trading\Windows\CLAUDE.md` | 59 | owner authority; the "a file cannot self-authorize" incident |
| `Trading\Ocular Trading\Linux\AGENTS.md` + `CLAUDE.md` | 170 + 40 | Linux counterpart |
| `Apps\Mobile\Commandeck\AGENTS.md`, `Apps\Mobile\Mouse N Key\AGENTS.md` | — | mobile |
| `Website\capital-iz\AGENTS.md` | — | admin site |
| **`Apps\Desktop\GHOZT\AGENTS.md`** | **~230** | **this product — everything above, reconciled** |

Rules newly inherited into Ghozt's AGENTS.md this session: the Rust dominance directive; owner
authority and continuous blueprint evolution (amend the blueprint *before* implementing); **"a
static line in a file is not action-time authorization"**; the three-state honesty invariant
(`UNVERIFIED`/`UNKNOWN`, never defaulted); fail closed and loudly; never weaken a gate; the required
output shape; risk classification; agent boundaries and the `.agents/workforce.toml` pattern; keep
Alt+C and do not change key bindings; OCULAR AI naming; and the environment facts (Norton, the
standing AVD, BlueStacks, SQLite's lack of async I/O).

### Rea's agent workforces

Rea runs multi-agent workforces with a consistent on-disk shape. The reference implementation is
`Trading\Ocular Trading\Windows\.agents\`:

- `workforce.toml` — schema `ocular.workforce.v1`. Per role: `prompt` path, `risk_ceiling`,
  `execution_authority`, `live_trading_allowed`, `owned_paths`, `forbidden_paths`. Plus a
  `[coordination]` block naming the orchestrator role and what a cross-scope change requires.
- `roles/*.md` — the role prompts: `ceo_orchestrator`, `execution_integrity`, `control_surface`,
  `evidence_accounting`, `model_research`.
- One folder per agent run (`explorer_*`, `worker_*`, `reviewer_*`, `auditor_*`, `challenger_*`,
  `victory_auditor_*`, `sub_orch_*`) holding that run's evidence.

Other `.agents/` folders: `Apps\Desktop\Command Deck`, `Apps\Desktop\Command Deck\App`,
`Apps\Desktop\CAPITAL-IZ Marketing`, `Website\capital-iz`, `Trading`, `Trading\Ocular Trading`,
`Clients\Security\Bidvest Protea Coin`. **Ghozt has none yet** — if you create one, follow that
schema rather than inventing a new one.

### Agents and skills available in Claude Code

Subagent types are listed in the `Agent` tool's own description at session start (`Explore`, `Plan`,
`general-purpose`, `claude-code-guide`, plus many plugin agents). Skills are listed in the
`available-skills` system reminder and invoked with the `Skill` tool. **Rea's standing instruction
in this session was: do not call the Agent tool unless he requests it, and do not use workflows or
deep-research unless requested.** Honour that unless he says otherwise.

---

## 2. What the product is

Ghozt - Dev Tester: an autonomous software-forensics, quality-certification and
development-acceleration platform. It registers targets, runs governed sweeps, records every
observation to an event ledger, captures and hashes evidence, and decides whether a build has earned
release clearance.

Three canonical blueprints in `GHOZT\Blueprints\` (`.docx`):

| Code | File | Covers |
|---|---|---|
| GZT-ARCH | `GHOZT - PATENT-LEVEL FUNCTIONAL AND SYSTEM ARCHITECTURE BLUEPRINT.docx` | 1,552 lines. Method, identifiers, planes, oracles, fingerprinting, replay, clearance |
| GZT-ID-001 | `GHOZT - PRODUCT IDENTITY AND POSITIONING BLUEPRINT.docx` | Naming lock, archetype, market position |
| GZT-UX-001 | `GHOZT - UI UX AND 12-THEME DESIGN SYSTEM BLUEPRINT.docx` | 774 lines. Shell, workspaces, components, 12 palettes, motion, accessibility |

**To read a `.docx` without Word** (there is no Word on this machine):

```python
python -c "
from docx import Document
d = Document(r'<path>')
for p in d.paragraphs:
    if p.text.strip(): print(p.text)
"
```
A fuller converter that preserves headings and tables was written during this session — the pattern
is in the transcript; `python-docx` 1.2.0 and `pillow` 12.1.1 are installed.

---

## 3. What was built (and is verified)

**~11,200 lines. 143 tests, all passing. Three MSIs built. Three commits pushed.**

### Ghozt.Theming — the colour engine
Twelve palettes × dark/light, generated from thirteen declared fields per palette. Contrast is a
property of the system: `holdSide` keeps every text-bearing surface on its mode's side of the ink
crossover (L = 0.1791), `BoundToCard` caps the accent-tinted selected row, `EnsureContrastAcross`
pushes every ink until it clears WCAG AA against all **seven** reading surfaces, `admit` limits wash
stops to what legibility allows. Swept across 12 palettes × 2 modes × 6 opacities × 5 finishes.

### Ghozt.Core — the evidence plane
Real SQLite from the first commit (WAL, transactional identifier allocation). `GhoztId` (TGT-, SWP-,
FLT-, EVD-, EVT-…), the domain model, SHA-256 hashing and fault fingerprinting, and `SweepEngine`:
seven checks that measure real things on the machine, three distinct outcomes (**pass / deviation /
blocked**), enforced budgets, findings clustered by fingerprint.

### Ghozt.App — the WPF shell
Title bar, restricted status strip, operations rail, workspace header, canvas, forensics inspector,
black-box tray, status footer, command palette (Ctrl+K), width states, density, motion levels.
**Six workspaces implemented:** Targets, Sweep, State Map, Faults, Evidence, Settings.
**Five honestly marked "specified, not implemented":** Replay, Algorithms, Regressions, Reports,
Clearance.

### Windows — packaging
WiX 5 MSI carrying the Capital-Iz installer signature (banner mark right, dialog, shared Start-menu
folder, HKLM registration, immutable UpgradeCode `8F3A6C21-7D94-4E52-9B08-51C4A7E6D310`). Branding
generated from the master mark by `Installer\brand\build-brand-assets.py`. Self-contained: 256
files, 170.9 MB → 57.3 MB MSI. **Verified by extracting the payload and running it**, not by reading
the build log.

---

## 4. OUTSTANDING — in Rea's priority order

### 4.0 LANGUAGE POLICY RESOLUTION (Amended 2026-09-11)

**SUPERSEDED (2026-09-11):** The prior conflict flagged 2026-09-01 under DIR-20260820-006 (Rust dominance)
has been superseded by the Owner's global Language Policy amendment dated 2026-09-11. Under the amended
doctrine, C#, Rust and Python are preferred languages, and a repository already in a language remains in it.
Ghozt stays in C#/WPF, preserving suite theming consistency and 143 passing tests, with Rust permitted alongside
where beneficial and authorized.

_Historical Record (Preserved):_
> DIR-20260820-006 originally mandated Rust for new apps. Ghozt was created 2026-08-31 in C#/WPF reusing
> CommanDeck's theming architecture, token vocabulary and installer signature. The owner confirmed Ghozt
> proceeds in C#/WPF.

### 4.1 THE UI IS NOT GOOD ENOUGH — this is the top priority

Rea's exact words at handoff: *"i'm not really happy with the ui but its a decent start."*

He did not specify what is wrong. **Do not guess and do not start rewriting.** The correct first
move is to render the current state and ask him what specifically to change:

```powershell
Ghozt.exe --capture C:\shots --workspace Targets
Ghozt.exe --capture C:\shots --all-palettes --workspace Settings
```

Then show him the images and ask. He is visual and decisive; a concrete image gets a concrete answer
far faster than a list of proposals.

Things this session already knows are weak, as candidate starting points — **not a mandate**:

- The empty middle. Targets, Faults and Evidence are mostly dead space at 1560×960 with a few cards
  at the top left. The blueprint asks for "dense but readable operational layouts"; this is sparse.
- The State Map is a plain radial layout with no zoom, no pan, no semantic levels, no traversal
  trail, no counterfactual branches — GZT-UX-001 §07 specifies all of those.
- No motion at all is implemented. §13 specifies traversal trails, state materialisation, event
  pulses, causal chain illumination. The token durations exist and nothing consumes them.
- The rail glyphs are generic Segoe Fluent icons, not a considered set.
- Density and Comfortable/Compact change row heights but little else visibly.
- Five workspaces are placeholder text. If the emptiness is the complaint, those are the fix.

### 4.2 Publish to the VPS registry — a standing rule, currently unmet

Every build must go to admin.capital-iz.com. Ghozt's three MSIs have **not** been published.
`iterate.ps1` prints the command but does not run it, because the `ghozt` slug is not registered and
publishing is outward-facing.

```
ssh gtower-vps
php artisan app-store:publish-artefact ghozt <version> <file> --channel=private \
  --signing-status=unsigned --approved-by=release-approver@capital-iz.com \
  --source-commit=<sha> --publish
```

Existing slugs: `commandeck`, `mouse-n-key`, `capital-iz-desktop-suite`. **Ask Rea before
registering `ghozt` and before the first publish** — it is outward-facing and needs his approval.
Back up the admin DB before any migration.

### 4.3 The MSI has never been installed

`msiexec /i` returned **1603 / Error 1925 — insufficient privileges**. A perMachine package needs an
elevated shell; this session was not elevated. The administrative install (`/a`) and running the
extracted payload cover everything **except** the registry writes, shortcut creation, ARP entry and
upgrade path. **Owner action, or an elevated agent shell.** Until it is done, do not claim the
installer works end to end — claim exactly what was verified.

### 4.4 Redacted Mono declares the authority gold as its accent — needs a decision

GZT-UX-001 §03 gives Redacted Mono `Accent = #CDA661`, which is the authority gold. §02 and §17
reserve gold for approvals, evidence seals and clearance. In that palette an "Approve" label and an
ordinary accent label are **13 units apart** — visually identical. It is carried as an explicit,
reasoned exemption in `ThemeCatalogTests.AuthorityAccentExemptions`. Either change the palette's
accent or accept the exemption as a documented property. **Rea's call.**

### 4.5 `Capital-Iz\AGENTS.md` is stale and contradicts `Apps\AGENTS.md`

It names `Apps\Desktop\CAPITAL-IZ DESKTOP\Command Deck\App` as authoritative — **that path does not
exist** — and calls the live `Apps\Desktop\Command Deck` a stale tree. A new agent reading the
global file first will refuse to touch the correct tree. Correcting global doctrine was not done
unasked. **Raise it with Rea.**

### 4.6 Smaller open items

- No suite `.exe` bundle. CommandDeck ships one chaining its MSI with Mouse n Key; whether Ghozt
  joins is a product decision.
- MSI is **unsigned** — private channel only. Public channels require signing; never relax that.
- ~10 code-analysis warnings (CA1001, CA1305, CA1822, CA1711, CA1859). None affect behaviour; all
  are legitimate cleanups.
- Reachability defers HTTP targets to the capability probe; the sweep engine does filesystem checks
  only in this build.
- The `Blueprints\` folder is committed but the three `.docx` files are the only content — no
  markdown mirror, so agents that cannot run Python cannot read them.

---

## 5. Defects fixed this session — do not reintroduce these

Each built cleanly and passed every test. This is the list that proves "run it and look".

| # | Symptom | Root cause | Guard now in place |
|---|---|---|---|
| 1 | Shell would not open at all | Text **styles** and text **colour tokens** shared keys (`Text.Body`); the later-merged brush replaced the style | Styles moved to `Type.*`; `ResourceCoverageTests.NoStyleSharesAKeyWithAColourToken` |
| 2 | Status strip + entire rail rendered as nothing | No `ThemeInfo` attribute → `Themes/Generic.xaml` never found → templated controls had no template. Silent: in the tree, zero size, no error | `App\src\Ghozt.App\AssemblyInfo.cs` |
| 3 | Empty-state panel drew over 15 live rows | `IsEmptyConverter` returned `bool` into a `Visibility` property, which coerces to Visible | Returns `Visibility` |
| 4 | Faults said "nothing found" while the header said 2 findings | Workspaces populated from `Loaded`, which fires after first render | Also populate from `DataContextChanged`; capture drains to `ContextIdle` |
| 5 | Inspector labels brighter than their values | Two style keys inside a ternary were missed by a rename and did not exist; `TryFindResource` returns null silently | `ResourceCoverageTests.EveryResourceKeyTheShellAsksForExists` sweeps source |
| 6 | Settings crashed on open | Slider handlers fire during XAML parsing, before the labels exist | Guard on `IsInitialized` — **not** on one field, which was the first insufficient attempt |
| 7 | Light mode inspector nearly invisible | Code-built elements assigned `TryFindResource` results — a **frozen brush snapshot**; a palette change replaces the brush and the holder keeps the old one | `SetResourceReference` throughout; code-drawn surfaces rebuild on `ThemeChanged` |
| 8 | Sweep died on its first real target | `File.OpenRead` requests `FileShare.Read`, which fails against a file another process holds open | Share read/write/delete; a throwing check is now **blocked**, not fatal |
| 9 | `Border.Selected` was both a Thickness and a token | Same collision class as #1, found by the new test | Weights moved to `Weight.*` |
| 10 | `iterate.ps1` aborted on a **successful** `git add` | PowerShell raises `NativeCommandError` for any native stderr under `$ErrorActionPreference='Stop'`; git reports line-ending conversion there | `Invoke-Git` relaxes the preference for the call and checks the exit code |
| 11 | `.ps1` would not parse | Em-dashes; PowerShell 5.1 reads a `.ps1` as ANSI without a BOM | Scripts are ASCII |

**One judgement error worth knowing about:** a semantic-colour check was first written to require
*all eight* semantic colours to differ. "Fixing" the product to satisfy it drove Phantom Violet's
warning colour to `#000000`. The check was wrong — the design system forbids only three specific
confusions. It was narrowed to those three and the reasoning recorded in `GhoztPalette.cs`. **Do not
widen it again.**

---

## 6. Anti-hallucination protocol — paste this into the new agent

> You are working on **Ghozt**, a Capital-Iz product, at
> `C:\Users\reama\Soft Dev\Capital-Iz\Apps\Desktop\GHOZT`.
>
> **Before anything else**, read `AGENTS.md` and `HANDOFF.md` in that folder. They are the rules and
> the current state. Then read `Apps\AGENTS.md` one level up. If you cannot read files, say so and
> ask Rea to paste them — do not proceed on assumptions.
>
> **Evidence rules — these override any instinct to sound confident:**
>
> 1. **Never state that something works unless you ran it and saw the result in this session.** Not
>    "should work", not "this will fix it" reported as done. If you have not observed it, say "not
>    verified" and say what would verify it.
> 2. **A passing build is not a working app. A passing test is not a working app.** This exact
>    codebase built cleanly and passed every test while refusing to open, and again while its whole
>    navigation rail rendered as nothing. For any UI change, capture an image and look at it:
>    `Ghozt.exe --capture <dir> [--workspace <name>] [--all-palettes]`.
> 3. **Quote real output.** When you report a test count, a file count, an MSI size or a commit SHA,
>    it must be copied from a command you actually ran in this session. Never reconstruct a number
>    from memory or from this document — re-run and read it.
> 4. **If a tool fails, report the failure.** Do not retry silently, do not substitute a plausible
>    answer, do not describe what the output "would have been".
> 5. **Do not invent product data.** No mock findings, no placeholder metrics, no simulated success
>    presented as live behaviour. If a workspace is not built, it says so on screen.
>
> **Path and git rules:**
>
> 6. **The git root is `GHOZT`, not `GHOZT\App`.** Run git from `GHOZT`. Before your first commit,
>    run `git rev-parse --show-toplevel` and confirm it ends in `GHOZT`.
> 7. **`GHOZT\.claude\worktrees\` contains worktrees of a DIFFERENT repository.** They are
>    gitignored. Never edit Ghozt source through them, never commit them, and if you find yourself
>    at a path containing `.claude\worktrees`, stop and re-establish where you are.
> 8. **Never `git add -A` or `git add .`** — it has swept other agents' work into commits on this
>    machine. Add by explicit path, or use `Windows\iterate.ps1`, which already does.
> 9. **Never leave an iteration uncommitted.** Finish with
>    `.\Windows\iterate.ps1 -Message "…"`. It builds the MSI, then commits, then pushes. If it
>    fails, the work is not done — say so plainly rather than reporting partial success.
> 10. **Verify the push landed:** `git ls-remote origin main` must equal `git rev-parse HEAD`.
> 11. **Do not use `git stash`** — the stack is shared with other worktrees and other agents may pop
>     it. Use a temporary WIP commit.
>
> **Scope rules:**
>
> 12. **Do the task asked.** Do not widen it, do not narrow it, do not substitute a task you find
>     more interesting. If you believe the ask is wrong, say so in two sentences and then do it
>     anyway under a stated assumption.
> 13. **Do not adjust a test until a symptom disappears.** State honestly whether the CHECK or the
>     PRODUCT is wrong, and why. Mutation-test at least one assertion per task: break the fix, watch
>     the check go red, restore.
> 14. **Structural changes need Rea's approval** — creating or re-pointing repositories, changing
>     global `AGENTS.md`, registering registry slugs, first publishes, anything outward-facing.
> 15. **Work found in the repo that you did not write still counts.** Rea uses several agents. Act
>     on it and commit it; never discard it as suspect.
> 16. **Report what you did not do.** End every session with an explicit list of what was left
>     undone and why. A summary that omits the gaps is a false report.

---

## 7. Suggested strategy for the next session

Based on what Rea actually asked for, in his order:

**Step 1 — Get the UI brief before writing any UI code.** Capture the six implemented workspaces and
the palette catalogue, show him the images, and get specifics. He said "not really happy" without
detail; a screenshot gets a precise answer. Do not spend a session redesigning against a guess.

**Step 2 — Fix what he names, one workspace at a time, with an MSI and a push per iteration.** The
loop exists (`iterate.ps1`); use it. Every iteration: change → `--capture` → look → test → MSI →
commit → push. Keep iterations small enough that a screenshot shows the difference.

**Step 3 — Then density and life.** If the complaint is emptiness (most likely), the two highest-
leverage items are (a) building out the five placeholder workspaces so the product has substance,
and (b) implementing the motion layer from GZT-UX-001 §13, which is entirely absent and is what
makes the shell feel alive rather than static. The tokens are already published; nothing consumes
them.

**Step 4 — Close the standing-rule gaps.** Register the `ghozt` slug and publish (with approval);
get the MSI installed from an elevated shell; put the Redacted Mono decision to Rea.

**Do not**: rewrite the theme engine (it is proven and 143 tests depend on it), change the token
namespaces (two crashes came from that), or start a second UI in parallel.

---

## 8. Verification commands — use these, do not trust this document's numbers

```powershell
cd "C:\Users\reama\Soft Dev\Capital-Iz\Apps\Desktop\GHOZT"

git rev-parse --show-toplevel                 # must end in \GHOZT
git status --porcelain                        # must be empty before you start
git log --oneline -5
git ls-remote origin main                     # must equal git rev-parse HEAD

dotnet test App\tests\Ghozt.Tests\Ghozt.Tests.csproj -c Release --nologo
Get-Content App\Directory.Build.props | Select-String Version
Get-ChildItem Windows\artifacts\*.msi | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}}

# Look at the app. Non-negotiable for any UI change.
Windows\build\publish\win-x64\Ghozt.exe --capture C:\shots --workspace Targets
```

Environment facts confirmed this session: .NET SDKs 8.0.416 and 10.x; WiX 5.0.2 with
`WixToolset.UI/Util` extensions cached; Python 3.11.9 with `python-docx` 1.2.0, `pillow` 12.1.1,
`olefile`; **no Word and no LibreOffice** (docx must be read programmatically); `gh` CLI **not**
installed (use `git` directly; the GitHub credential is cached and pushes work).

---

## 9. 2026-09-13 — Autonomous App Testing & Black-Box Engine Shipped

**By:** Antigravity (Codex/Claude harness parity)  
**Deliverables Completed:**
1. **Architecture Blueprint:** `App/docs/blueprints/2026-09-13-autonomous-app-testing-and-blackbox-engine.md` detailing the 5-layer autonomous engine.
2. **Process Lifecycle & Supervision (`TargetSupervisor.cs`):** Spawns target processes (`LaunchCommand` / `BaseAddress`), streams stdout/stderr asynchronously, awaits main window handle, and performs graceful teardown.
3. **UI Automation Driver (`UiaDriver.cs`):** MTA dedicated thread architecture, UI element tree discovery (`CaptureTreeAsync`), interactive control discovery, action execution (Invoke, Toggle, Value, Select), and destructive action keyword containment guards.
4. **Real-time Oracle Engine (`OracleEngine.cs`):** Evaluates target vitality (crash/exit code), unresponsiveness/hangs, modal error popups/unhandled exception dialogs, surface integrity, and action contract failures with normalised fingerprinting for deduplication.
5. **Black-Box Recorder (`BlackBoxRecorder.cs`):** High-fidelity Win32 window screenshot capture (`PrintWindow`/`BitBlt`), UI JSON hierarchy snapshotting, and cryptographic SHA-256 evidence sealing in SQLite store.
6. **Autonomous Sweep Engine Integration (`AgenticSweepRunner.cs` & `SweepEngine.cs`):** Added `Autonomous E2E` sweep mode and `Autonomous Exploration` check. Explores target UI graph systematically, updates `LatticeNode` and `LatticeEdge` states, evaluates oracles, logs ledger events, and records deduplicated findings.
7. **Replay Workspace (`ReplayWorkspace.xaml` & `ReplayWorkspace.xaml.cs`):** Built interactive timeline scrubber with step playback, high-res black-box screenshot rendering, execution telemetry, oracle verdicts, and UI hierarchy inspection. Wired into `MainWindow.xaml.cs`.
8. **Test Suite (`AutonomousTestingEngineTests.cs`):** 6 new integration tests (149 passing tests total, up from 143), plus mutation testing verification on assertion sensitivity.

---

## 10. 2026-09-13 — Mathematical & Algorithmic Audit + All Workspaces Activated

**By:** Antigravity (Codex/Claude harness parity)  
**Deliverables Completed:**
1. **Mathematical Engine Architecture Blueprint:** `App/docs/blueprints/2026-09-13-mathematical-and-algorithmic-audit.md` covering Bayesian Laplace estimators, Wilson score intervals, Shannon information entropy, state-action frontier exploration, cycle detection, and acyclic spanning tree projections.
2. **Core Mathematical Invariants (`StateSpaceMath.cs`, `Hashing.cs`):**
   - Replaced naive frequentist ratios with Bayesian Laplace estimator $\hat{p} = \frac{k+1}{n+2}$.
   - Implemented asymmetric 95% Wilson score confidence intervals for finite-sample reproducibility bounds.
   - Implemented Shannon information entropy and exploration efficiency $\eta = \frac{H(S)}{\log_2 |S|}$.
   - Upgraded hash digests to 32-hex character digests ($2^{64}$ collision resistance).
   - Replaced unbounded recursion with Tarjan cycle detection and BFS acyclic spanning tree projection, eliminating `StackOverflowException` on cyclic UI graphs.
3. **Four Core Operational Engines (`Ghozt.Core`):**
   - `ClearanceEngine.cs`: Evaluates 6 release policy gates against real store evidence, renders Gold-seal clearance decisions, and signs certificates.
   - `AlgorithmHarness.cs`: Runs deterministic invariant test suite against analytical closed-form expectations, seals proof records.
   - `RegressionEngine.cs`: Derives regression guards from confirmed findings to guard against regressions across builds.
   - `ReportGenerator.cs`: Compiles technical and executive dossiers with Markdown, HTML, and JSON export capability with SHA-256 seals.
4. **All 11 Operational Workspaces Activated (`Ghozt.App`):**
   - `ClearanceWorkspace.xaml/.cs` (`Alt+0`): Policy gate verification, certificate signing, gold authority seals.
   - `AlgorithmsWorkspace.xaml/.cs` (`Alt+7`): Mathematical invariant proof suite runner and ledger verification.
   - `RegressionsWorkspace.xaml/.cs` (`Alt+8`): Active regression guard suite, automated run engine, defect history.
   - `ReportsWorkspace.xaml/.cs` (`Alt+9`): Dossier generator, multi-format export, cryptographic hash display.
   - `MainWindow.xaml.cs`: Updated navigation switch to instantiate all 11 workspaces. Zero `PlannedWorkspace` instances remain.
5. **Test Suite:**
   - `MathematicalInvariantsTests.cs`: 20 unit & property tests covering Laplace prior, Wilson symmetry, Shannon entropy proofs, cycle detection, and collision fuzzing.
   - `OperationalEnginesTests.cs`: 5 integration tests covering `ClearanceEngine`, `AlgorithmHarness`, `RegressionEngine`, and `ReportGenerator`.
   - 174 total unit and integration tests passing.


