---
name: ghozt-is-the-fifth-product
description: "Ghozt - Dev Tester lives at Apps/Desktop/GHOZT with its own repo; its handoff and rules are in the repo, not in chat."
metadata: 
  node_type: memory
  type: project
  originSessionId: 49f73f88-47aa-4967-b2a3-261a22b8b5f8
  modified: 2026-08-31T23:27:31.918Z
---

**Ghozt - Dev Tester** is a CAPITAL-IZ desktop product, added 2026-08-31/09-01. It is a software
forensics and release-clearance platform: it registers targets, runs governed sweeps, records an
event ledger, hashes evidence, and decides whether a build has earned clearance.

- **Path:** `Apps\Desktop\GHOZT` — this is the **git root**, not `GHOZT\App`.
- **Remote:** `https://github.com/capital-iz/GHOZT`, branch `main`.
- **Stack:** WPF / .NET 8 (`net8.0-windows10.0.19041.0`), matching CommanDeck and Mouse n Key.
- **Ship:** `Windows\iterate.ps1 -Message "..."` — bumps, tests, builds the MSI, commits by explicit
  path, pushes. Artifacts are gitignored (57 MB each; GitHub refuses blobs over 100 MB).

**Why this matters:** `Apps\AGENTS.md` says "four products in four places". Ghozt makes five, and it
does not appear in that table. Do not conclude Ghozt is stray work because it is missing from the
layout — see [[four-products-four-places]] and [[local-is-the-authority]].

**How to resume without re-explaining:** the repo carries its own `AGENTS.md` (the rules, inheriting
the global ones) and `HANDOFF.md` (current state, outstanding items, anti-hallucination protocol for
a fresh agent). Read those two files before anything else; they are written for any agent on any
platform, not just Claude Code. See [[agents-md-is-the-rule-carrier]].

**Open at handoff:** Rea is not satisfied with the UI and has not yet said what specifically to
change — get a brief from screenshots before writing UI code. The MSI has never been installed
(needs an elevated shell). Nothing has been published to the VPS registry yet, which is a standing
rule outstanding — see [[publish-every-release-to-vps-registry]].
