# AGENTS.md — Ghozt (Capital-Iz)

**Read this before touching anything.** These are the owner's standing rules. They are not
negotiable and they are not per-task. Rea has had to repeat several of them many times; do not make
him repeat them again.

This file exists so the rules travel with the repository. Any agent on any platform — Claude Code,
Codex, Cursor, Copilot, Antigravity, a web chat with the repo attached — reads this file first and
is then bound by it, without Rea re-explaining.

---

## 0. Rule sources, in precedence order

| Rank | Source | Path |
|---|---|---|
| 1 | **This file** | `Apps\Desktop\GHOZT\AGENTS.md` |
| 2 | Suite-level rules | `Apps\AGENTS.md` |
| 3 | Global doctrine | `Capital-Iz\AGENTS.md` |
| 4 | Owner's persistent memory | `C:\Users\reama\.claude\projects\C--Users-reama-Soft-Dev-Capital-Iz-Apps\memory\` |
| 4b | …mirrored into this repo, for agents that cannot reach that path | `docs\owner-rules\` |

`docs\owner-rules\` is a **snapshot**, not the authority. If it disagrees with the path above, the
path above is correct. Refresh it rather than editing it — see `docs\owner-rules\README.md`.

Where they disagree, the **nearer** file wins, because it was written later and about this product.

> **Known contradiction — do not be misled.** `Capital-Iz\AGENTS.md` §1 names
> `Apps\Desktop\CAPITAL-IZ DESKTOP\Command Deck\App` as authoritative and calls
> `Apps\Desktop\Command Deck` a stale tree. **That path does not exist.** `Apps\AGENTS.md` is newer
> and correct: `Apps\Desktop\Command Deck\App` is the live CommanDeck, and
> `Apps\Desktop\Command Deck\Windows` is its live installer. The root file has not been corrected;
> raise it with Rea rather than editing global doctrine unasked.

If you cannot read files (a web chat), ask Rea to paste this file and `Apps\AGENTS.md`. Do not
proceed on assumptions about the layout.

---

## 1. Which tree is real

| Thing | AUTHORITATIVE path | Git |
|---|---|---|
| **Ghozt product root** | `Apps\Desktop\GHOZT` | own repo → `https://github.com/capital-iz/GHOZT`, branch `main` |
| Application source | `Apps\Desktop\GHOZT\App` | inside the same repo (not a nested repo) |
| Installer & packaging | `Apps\Desktop\GHOZT\Windows` | same repo |
| Product blueprints | `Apps\Desktop\GHOZT\Blueprints` | same repo |
| Build blueprints | `Apps\Desktop\GHOZT\App\docs\blueprints` | same repo |

**The git repository root is `GHOZT`, not `GHOZT\App`.** Run every git command from `GHOZT`.

`Apps\Desktop\GHOZT\.claude\worktrees\` holds worktrees **of a different repository** (the `Apps`
repo) that happen to live inside this folder. They are gitignored. Never commit them, never treat a
path under them as the Ghozt source, and never assume a file you edited there reached this repo.

`Apps` repo `.gitignore` excludes `Desktop/`, so Ghozt is invisible to it by design. Each desktop
product has its own repository. That is the pattern, not an accident.

---

## 2. Global rules (inherited — these bind here too)

1. **Patent-standard blueprints.** Every task ships build + product blueprints in `docs/blueprints/`
   — mechanism, rejected alternatives with reasons, enumerated behaviours cross-referenced to tests,
   failure paths. Written to be sufficient to reimplement from.
2. **Local is the authority.** If local and git disagree, local wins and git is made to match. Work
   found in this repo that you did not write **still counts** — act on it and commit it, never
   discard it. Rea uses several coding agents; unfamiliar work is not suspect work.
3. **Installers carry 100% of their dependencies.** The MSI must run on a machine that is NOT the
   author's. Self-contained publish, runtimes bundled.
4. **One house, many rooms.** Ghozt is part of the Capital-Iz suite. It carries the family's
   installer signature and design doctrine, with its own product identity on top.
5. **Publish every release to the VPS registry.** Every build goes to admin.capital-iz.com; the
   in-app updater serves from it. **Not yet wired for Ghozt — see HANDOFF.md.**
6. **Queue, don't interrupt.** Feedback arriving mid-flight goes in the backlog and is done after
   current work finishes. Only an explicit stop redirects work already running.
7. **Never expose internal infrastructure in user-visible UI.** Admin host, VPS IP, registry paths,
   channel keys — configuration only, never rendered.
8. **Naming is locked.** See §4.

---

## 3. How not to waste the owner's time

Every line here is a real failure from this programme.

- **A passing test is not a working app. Run it and look.** This shell built cleanly and passed
  every test while refusing to open at all, and later while its entire navigation rail rendered as
  nothing. Both were invisible to the build and to the tests. Use
  `Ghozt.exe --capture <dir> [--all-palettes] [--workspace <name>]` and *look at the image*.
- **Check the path before you build.** Confirm you are in `Apps\Desktop\GHOZT`, not a worktree
  under it. This is the single most expensive mistake made across this programme.
- **Stale binaries lie.** Rebuild the exact configuration you are about to run, and stop any running
  instance first — it locks its own executable and the build silently reuses the old one.
- **Never `git add -A`.** It has swept other agents' in-flight edits and embedded repositories into
  commits on this machine. Add by explicit path. `Windows\iterate.ps1` already does.
- **Do not adjust a test until a symptom disappears.** Decide honestly: is the CHECK wrong or is the
  PRODUCT wrong, and say which and why. In this session a semantic-colour check was written too
  broadly, and "fixing" the product to satisfy it drove a warning colour to pure black. The check
  was wrong; it was narrowed, and the reasoning recorded.
- **No vacuous checks.** Assert the observable property — what renders, what arrives, what the
  operator can press.
- **Mutation-test at least one assertion per task.** Break the fix, watch the check go red, restore.
- **Do not delegate a task you were given**, and never report work you did not verify.
- **WiX needs a short TEMP.** `build-msi.ps1` sets `C:\cizt` itself; do not remove that.
- **PowerShell scripts here are ASCII.** PowerShell 5.1 reads a `.ps1` as ANSI without a BOM, and an
  em-dash becomes a parse error.

---

## 4. Naming lock (GZT-ID-001 §02)

| Context | Exact form |
|---|---|
| Visible application brand | **Ghozt** — window title, shell, shortcuts, notifications |
| Formal product name | **Ghozt - Dev Tester** — installer, About, metadata, contracts |
| Portfolio identity | **Capital-Iz Ghozt** — collateral, provenance, cross-product references |
| Publisher | **Capital-Iz** |

Never silently correct "Ghozt" to "Ghost". Never use GHOST, Ghozt DevTester, Ghozt QA. A module may
be "State Map"; the shell remains Ghozt. No ghost imagery, paranormal language or puns anywhere in
the product — they cost technical credibility, which is the whole proposition.

---

## 5. Build, test, package, ship

Everything is one command, from `Apps\Desktop\GHOZT\Windows`:

```powershell
.\iterate.ps1 -Message "what changed" [-Bump patch|minor|major|none] [-NoPush]
```

It bumps the version, regenerates brand assets, runs the tests, publishes self-contained, packages
the MSI, commits by explicit path, and pushes. **A failing test stops it and produces no installer.**

The MSI is built **before** the commit deliberately: a commit claiming to be an iteration whose code
does not package leaves history nobody can trust.

Individual steps, if you need them:

```powershell
dotnet test App\tests\Ghozt.Tests\Ghozt.Tests.csproj -c Release
.\Installer\msi\build-msi.ps1              # brand -> test -> publish -> MSI
python Installer\brand\build-brand-assets.py
```

**Artifacts are gitignored.** A self-contained MSI is ~57 MB and GitHub refuses blobs over 100 MB,
so a repo carrying every iteration's installer stops accepting pushes within a few builds. Source in
git, binaries to the registry.

---

## 6. The design system is not optional

Ghozt's look is governed by two documents, and both are binding:

- `C:\Users\reama\Soft Dev\Capital-Iz\Blueprints\🎨 UNIVERSAL DESIGN SYSTEM — CAPITAL-IZ DOCTRINE.docx`
  — the suite doctrine, 23 numbered laws. **§12 is Ghozt's adaptation.**
- `Apps\Desktop\GHOZT\Blueprints\GHOZT - UI UX AND 12-THEME DESIGN SYSTEM BLUEPRINT.docx` (GZT-UX-001)

Non-negotiables that the tests enforce:

1. **No literal colour anywhere in a view.** Every colour resolves through a published token.
2. **`Text.*`, `Surface.*`, `Border.*`, `Accent.*`, `State.*`, `Ghozt.*`, `Authority.*` are the
   generated token namespace.** A style or Thickness sharing a key with a token is resolved by
   whichever dictionary merged last — that collision stopped the shell opening once already.
   `ResourceCoverageTests` refuses it.
3. **Colour is never the sole carrier of status.** Label + glyph + colour, always.
4. **Gold is authority, not emphasis.** Approvals, complete evidence seals, release clearance,
   Capital-Iz provenance. Nothing else.
5. **A fault must never look verified; a simulation never observed; authority never like ordinary
   emphasis.** `ForbiddenPairsNeverConverge` proves it for every palette, mode and opacity.
6. **No decorative data.** Every node, metric and stream line resolves to something real. No mock,
   placeholder or simulated success may be presented as live product behaviour. Unimplemented
   workspaces say so.
7. **Code-built visuals bind by `SetResourceReference`, never by assigning `TryFindResource`.** The
   published brushes are frozen and replaced on a palette change; anything holding one keeps the old
   colour. This shipped a light mode with near-invisible text before it was caught.
