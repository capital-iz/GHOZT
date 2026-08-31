# AGENTS.md — Capital-Iz suite

**Read this before touching anything.** These are the owner's standing rules. They are not
negotiable and they are not per-task. Rea has had to repeat several of them many times; do not make
him repeat them again.

---

## 1. Which tree is real (this has cost days — read it twice)

| Product | AUTHORITATIVE path | Status |
|---|---|---|
| CommanDeck (desktop) | `Apps\Desktop\CAPITAL-IZ DESKTOP\Command Deck\App` | build & edit here |
| Mouse n Key (desktop) | `Apps\Desktop\Mouse n Key` | build & edit here; mirrored into the monorepo by subtree |
| CommanDeck / Mouse n Key (mobile) | `Apps\Mobile\CAPITAL-IZ MOBILE` | build & edit here |
| Admin site / app registry | `Website\capital-iz\capital-iz-admin` | branch `capital-iz-site` |

**STALE TREES — NEVER EDIT OR BUILD THESE:**
- `Apps\Desktop\Command Deck` — frozen 2026-08-06. The packaging script used to build CommanDeck
  from here; every "fix" shipped from it was August code with a new version number painted on. Three
  release cycles were lost to this. `scripts\verify-build-commit.ps1` now fails the release if the
  built exe's embedded commit is not repo HEAD. **Do not defeat that guard.**
- `Apps\Mobile\Commandeck` and `Apps\Mobile\Mouse N Key` — stale mirrors, no unique work.

Mouse n Key flows one way: standalone repo → monorepo subtree. After committing there, resync and
run `scripts\verify-suite-consolidation.cmd` (it must PASS; it also checks both trees are
byte-identical).

## 2. Global rules

1. **Patent-standard blueprints.** Every task ships build + product blueprints in `docs/blueprints/`
   — mechanism, rejected alternatives with reasons, enumerated behaviours cross-referenced to tests,
   failure paths. Written to be sufficient to reimplement from.
2. **Local is the authority.** If local and git disagree, local wins and git is made to match. Work
   found in a repo that you did not write **still counts** — act on it and commit it, never discard it.
   Rea uses other coding agents; unfamiliar work is not suspect work.
3. **Installers carry 100% of their dependencies.** Every MSI/bundle/APK/AAB must run on a machine
   that is NOT the author's. Publish self-contained; bundle runtimes, codecs, drivers. Virtual
   drivers are explicitly authorised where a feature needs one. Known exception, documented: Store-
   gated Windows image extensions (HEIC/AVIF/JPEG XL/raw) have no redistributable form.
4. **One house, many rooms.** The suite is one ecosystem. Mobile must feel exactly like PC and vice
   versa — in substance, not layout. Match the source platform's motion, translucency, depth and
   state persistence, not just its shape.
5. **Publish every release to the VPS registry.** Every build goes to admin.capital-iz.com; the
   in-app updater serves from it. Procedure in §4.
6. **Queue, don't interrupt.** New feedback arriving mid-flight is recorded in the backlog and done
   after current work finishes. Do not re-task running agents. If feedback lands after a build
   starts, that build ships and the item goes in the next version. Only an explicit stop redirects.
7. **Never expose internal infrastructure in client UI.** The admin host, VPS IP, registry paths and
   channel keys must never be rendered to a user or shipped in user-visible text. Configuration only.
8. **Naming.** The business is **Capital-Iz**. The product is **CommanDeck** (that exact casing).
   **Mouse n Key** is written in full, never abbreviated to MNK in conversation. On mobile there is
   ONE app and it is CommanDeck; Mouse n Key's mobile features live inside it. The AI
   subsystem/workspace is **OCULAR AI** — label everything AI, especially ours, accordingly.

## 3. How to not waste the owner's time

These are real failures from this programme. Each cost hours or days.

- **A passing test is not a working app.** Tests have repeatedly passed while the app was broken —
  an app that could not boot passed 926 tests; a smoke check asserted "nothing left held" while only
  ever pressing F24; a reachability sweep passed while a toolbar buried every button. **Run the app
  and look.** For UI, render it or screenshot it. For a fix, reproduce the fault first.
- **Stale binaries lie.** Two agents and the coordinator burned rounds on them. If you `dotnet build`
  without `-c Release` and then run the Release exe, you are testing old code. Rebuild the exact
  configuration you are about to run, and stop any running instance first (it locks the output).
- **Never `git add -A`.** It has swept other agents' in-flight work and embedded repos into commits.
  Commit only your own files by explicit path.
- **Do not adjust a test until a symptom disappears.** A pairing fixture was built already holding a
  permission "because otherwise the phone faults" — it was documenting the real bug and hiding it.
  If a check fails, decide honestly: is the CHECK wrong (assuming a state the machine cannot be in),
  or is the PRODUCT wrong? Say which and why.
- **No vacuous checks.** "The panel has children", "a style exists", "a receiver is registered" — all
  shipped defects. Assert the observable property: what renders, what arrives, what the user can press.
- **Mutation-test at least one assertion per task**: break the fix, watch the check go red, restore.
- **Do not delegate a task you were given.** One agent reported "I have launched an agent" and wrote
  nothing.
- **WiX needs a short TEMP.** The Mouse n Key MSI fails its CAB step unless TEMP/TMP is short
  (`C:\cizt` works). Not yet fixed in the packaging script.

## 4. Build & publish

```
cd "Apps\Desktop\CAPITAL-IZ DESKTOP"
set TEMP=C:\cizt & set TMP=C:\cizt
scripts\package-release.cmd <suite> --desktop-only --allow-untagged ^
  --commanddeck-version <v> --mousenkey-version <v>
```
Then verify provenance, then publish each artefact to the registry:
```
scripts\verify-build-commit.ps1 -Exe "Command Deck\App\artifacts\publish\win-x64\CapitalIz.CommandDeck.App.exe" -RepoRoot .

ssh gtower-vps    # admin.capital-iz.com, app at /home/capital-iz/apps/capital-iz/capital-iz-admin
php artisan app-store:publish-artefact <slug> <version> <file> --channel=private \
  --signing-status=unsigned --approved-by=release-approver@capital-iz.com \
  --source-commit=<sha> --publish
```
Slugs: `commandeck`, `mouse-n-key`, `capital-iz-desktop-suite`. `--source-commit` is required (the
registry refuses a build it cannot trace). Public channels still require signing — never relax that.
Back up the admin DB before any migration.

Mobile: `cd "Apps\Mobile\CAPITAL-IZ MOBILE\Commandeck" && flutter build apk --debug`. A signed
release needs `android/key.properties` (the keystores exist at `%USERPROFILE%\.capital-iz-signing\`).

## 5. Environment facts

- **Norton** intercepts TLS and blocks the emulator's network. It breaks Gradle fetches, blocks
  `androidx.test` (so Android instrumentation tests cannot run here), and blocks the emulator's
  internet. A firewall exception for `qemu-system-x86_64.exe` is owner-action, still outstanding.
- **Emulator:** always the standing AVD `Medium_Phone_API_36.1` — never `-wipe-data` or
  `-no-snapshot-load` (the owner keeps sign-in state). Launch command in
  `Commandeck/docs/blueprints/avd-and-network.md`.
- **BlueStacks** at `127.0.0.1:5558` claims to be a Samsung SM-S908E but carries **zero** Samsung
  packages — useful as an adversarial test for code that trusts the manufacturer string, useless for
  proving Samsung features.
- Real Samsung features (AI Select, Edge panels) need the owner's actual Z Flip 5.
- SQLite has no true async I/O, so Dapper's `*Async` runs on the caller. The deck's operations are
  effectively synchronous on the UI thread — documented as the next freeze waiting to happen.

## 6. Where the work lives

- **Outstanding work:** `docs/BACKLOG-2026-08-29-B.md` (items C1–C61, in the owner's own words).
- **Truth audit of earlier claims:** `docs/AUDIT-2026-08-29.md`.
- **Overnight ledger:** `docs/OVERNIGHT-LEDGER-2026-08-30.md`.
- **Blueprints:** `docs/blueprints/` in each repo.
- **Owner's memory (rules above, canonical):**
  `C:\Users\reama\.claude\projects\C--Users-reama-Soft-Dev-Capital-Iz-Apps\memory\`
