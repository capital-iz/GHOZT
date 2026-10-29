# AGENTS.md — CAPITAL-IZ suite

**Read this before touching anything.** These are the owner's standing rules. They are not
negotiable and they are not per-task. Rea has had to repeat several of them many times; do not make
him repeat them again.

Layout section last verified against the filesystem: **2026-08-31 14:30**.

---

## 1. Which tree is real

There are **four products in four places**. That is the whole layout. Nothing else is a product.

| Product | AUTHORITATIVE path | Git |
|---|---|---|
| **CommanDeck — PC** | `Apps\Desktop\Command Deck\App` | own repo, branch `main` |
| **Mouse n Key — PC** | `Apps\Desktop\Mouse n Key` | own repo, branch `main` |
| **CommanDeck — mobile** | `Apps\Mobile\Commandeck` | see the warning below |
| **Mouse n Key — mobile** | `Apps\Mobile\Mouse N Key` | see the warning below |

> **The mobile checkouts currently have no local git of their own.** Their home repository is the
> remote `capital-iz/CAPITAL-IZ-MOBILE` (branch `main`, last seen at `dfb05fb`), but the local
> `.git` that pointed at it was destroyed when `Apps\Mobile\CAPITAL-IZ MOBILE` was emptied. The
> `Apps` repo's `.gitignore` excludes `Mobile/`, so most mobile files are untracked there — only a
> partial set of 89 files remains tracked from an earlier mistaken commit. **Mobile work is
> therefore not being version-controlled.** Re-establishing the mobile checkout against its remote
> is a structural change: ask Rea before doing it, and do not resolve it by guessing.

Supporting, not products: `Apps\Mobile\packages` (shared Flutter packages — `capital_iz_theme`,
`capital_iz_shared`, `capital_iz_updates`), `Apps\Desktop\Command Deck\Windows` (installer and build
output), and the admin site at `Website\capital-iz\capital-iz-admin` (branch `capital-iz-site`).

`Apps\Desktop\Command Deck\App` is a **nested repo inside** the `Apps\Desktop\Command Deck` repo (a
gitlink). Commit inside `App` first, then commit the updated pointer in the outer repo. Commit only
one of the two and the outer repo points at the wrong revision.

**Do not recreate the duplication that was just cleaned up.** A `CAPITAL-IZ DESKTOP` folder once
held second copies of Command Deck and Mouse n Key, and a `CAPITAL-IZ MOBILE` folder held a second
copy of the mobile app. Rea never asked for either. Releases got built from the wrong copy, and it
cost three release cycles. **Never nest one product inside another product's folder, and never
create a container folder holding copies of the products.** If you find a duplicate tree, stop and
tell Rea — do not pick one and delete the other on your own judgement.

`Apps\Mobile\CAPITAL-IZ MOBILE` is a leftover empty husk from that cleanup. It holds no source.

## 2. Global rules

1. **Patent-standard blueprints.** Every task ships build + product blueprints in `docs/blueprints/`
   — mechanism, rejected alternatives with reasons, enumerated behaviours cross-referenced to tests,
   failure paths. Written to be sufficient to reimplement from.
2. **Local is the authority.** If local and git disagree, local wins and git is made to match. Work
   found in a repo that you did not write **still counts** — act on it and commit it, never discard
   it. Rea uses other coding agents; unfamiliar work is not suspect work.
3. **Installers carry 100% of their dependencies.** Every MSI/bundle/APK/AAB must run on a machine
   that is NOT the author's. Publish self-contained; bundle runtimes, codecs, drivers. Virtual
   drivers are explicitly authorised where a feature needs one. Known exception, documented: Store-
   gated Windows image extensions (HEIC/AVIF/JPEG XL/raw) have no redistributable form.
4. **One house, many rooms.** The suite is one ecosystem. Mobile must feel exactly like PC and vice
   versa — in substance, not layout. Match the source platform's motion, translucency, depth and
   state persistence, not just its shape.
5. **Publish every release to the VPS registry.** Every build goes to admin.capital-iz.com; the
   in-app updater serves from it. Procedure in section 4.
6. **Queue, don't interrupt.** New feedback arriving mid-flight is recorded in the backlog and done
   after current work finishes. Do not re-task running agents. If feedback lands after a build
   starts, that build ships and the item goes in the next version. Only an explicit stop redirects.
7. **Never expose internal infrastructure in client UI.** The admin host, VPS IP, registry paths and
   channel keys must never be rendered to a user or shipped in user-visible text. Configuration only.
8. **Naming.** The business is **CAPITAL-IZ**. The product is **CommanDeck** (that exact casing).
   **Mouse n Key** is written in full, never abbreviated to MNK in conversation. The AI subsystem is
   **OCULAR AI** — label everything AI, especially ours, accordingly.
9. **Do not change key bindings**, and **keep Alt+C**.

## 3. How to not waste the owner's time

These are real failures from this programme. Each cost hours or days.

- **A passing test is not a working app.** Tests have repeatedly passed while the app was broken —
  an app that could not boot passed 926 tests; a smoke check asserted "nothing left held" while only
  ever pressing F24; a reachability sweep passed while a toolbar buried every button. **Run the app
  and look.** For UI, render it or screenshot it. For a fix, reproduce the fault first.
- **Check the path before you build.** Confirm the tree you are in is the one in the table above.
  This is the single most expensive mistake made here.
- **Stale binaries lie.** Two agents and the coordinator burned rounds on them. If you `dotnet build`
  without `-c Release` and then run the Release exe, you are testing old code. Rebuild the exact
  configuration you are about to run, and stop any running instance first (it locks the output).
- **Never `git add -A`.** It has swept other agents' in-flight edits and embedded repos into commits.
  Commit only your own files by explicit path.
- **Do not adjust a test until a symptom disappears.** A pairing fixture was built already holding a
  permission "because otherwise the phone faults" — it was documenting the real bug and hiding it.
  If a check fails, decide honestly: is the CHECK wrong, or is the PRODUCT wrong? Say which and why.
- **No vacuous checks.** "The panel has children", "a style exists", "a receiver is registered" — all
  shipped defects. Assert the observable property: what renders, what arrives, what the user can press.
- **Mutation-test at least one assertion per task**: break the fix, watch the check go red, restore.
- **Do not delegate a task you were given.** One agent reported "I have launched an agent" and wrote
  nothing.
- **WiX needs a short TEMP.** The MSI fails its CAB step unless TEMP/TMP is short (`C:\cizt` works).

## 4. Build & publish

Desktop build and packaging live in `Apps\Desktop\Command Deck\Windows` and the repo `scripts`
folder. Set a short TEMP first:

```
set TEMP=C:\cizt & set TMP=C:\cizt
```

Publish each artefact to the registry — a standing rule, not an on-request step:

```
ssh gtower-vps    # admin.capital-iz.com, app at /home/capital-iz/apps/capital-iz/capital-iz-admin
php artisan app-store:publish-artefact <slug> <version> <file> --channel=private \
  --signing-status=unsigned --approved-by=release-approver@capital-iz.com \
  --source-commit=<sha> --publish
```

Slugs: `commandeck`, `mouse-n-key`, `capital-iz-desktop-suite`. `--source-commit` is required (the
registry refuses a build it cannot trace). Public channels still require signing — never relax that.
Back up the admin DB before any migration.

Mobile: `cd "Apps\Mobile\Commandeck" && flutter build apk --debug`. A signed release needs
`android/key.properties` (keystores exist at `%USERPROFILE%\.capital-iz-signing\`).

## 5. Environment facts

- **Norton** intercepts TLS and blocks the emulator's network. It breaks Gradle fetches, blocks
  `androidx.test` (so Android instrumentation tests cannot run here), and blocks the emulator's
  internet. A firewall exception for `qemu-system-x86_64.exe` is owner-action, still outstanding.
- **Emulator:** always the standing AVD `Medium_Phone_API_36.1` — never `-wipe-data` or
  `-no-snapshot-load` (the owner keeps sign-in state).
- **BlueStacks** at `127.0.0.1:5558` claims to be a Samsung SM-S908E but carries **zero** Samsung
  packages — an adversarial test for code that trusts the manufacturer string, useless for proving
  Samsung features. Real Samsung features (AI Select, Edge panels) need the owner's Galaxy Z Flip 5.
- SQLite has no true async I/O, so Dapper's `*Async` runs on the caller. Database work must be
  pushed off the UI thread explicitly; this was the cause of the CommanDeck freezes.

## 6. Where the work lives

All programme docs are tracked in the **Command Deck** repo, under `Apps\Desktop\Command Deck\docs\`:

- **Outstanding work:** `BACKLOG-2026-08-29-B.md` (items C1–C61, mostly in the owner's own words;
  C54–C61 are flagged reconstructions).
- **Programme state and strategy:** `HANDOFF-2026-08-31.md`.
- **Truth audit of earlier claims:** `AUDIT-2026-08-29.md`, `OVERNIGHT-LEDGER-2026-08-30.md`.

Keep them there. They were previously kept in a folder no repo tracked, and were lost from disk when
that folder was removed.
- **Blueprints:** `docs/blueprints/` in each repo.
- **Owner's memory (canonical for the rules above):**
  `C:\Users\reama\.claude\projects\C--Users-reama-Soft-Dev-CAPITAL-IZ-Apps\memory\`
