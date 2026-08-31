---
name: agents-md-is-the-rule-carrier
description: "Global rule - every Capital-Iz repo carries an AGENTS.md holding all standing rules, so any AI agent on any platform picks them up without Rea re-explaining."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 24ef8bf9-16b3-4393-a279-c618b6b265d1
  modified: 2026-08-30T17:52:54.988Z
---

Rea's standing rules must be **sourced and accessible to every AI agent, on every platform**, so
that when he returns to a chat his global rules are already active and he does not have to keep
re-explaining himself. His words: *"regarding agents .md files i want them sourced and accessible to
the ai agents, such that when i return to you - my global rules are active across different
platforms and i dont have to keep re-explaining myself."*

The mechanism: an **`AGENTS.md` at the root of every repo** (CAPITAL-IZ DESKTOP, Mouse n Key,
CAPITAL-IZ MOBILE, capital-iz website, and the `Capital-Iz` root), byte-identical in all of them,
plus a `CLAUDE.md` alias in the desktop repo. `AGENTS.md` is the convention Codex, Cursor, Gemini
CLI, Copilot and Claude Code all read. Alongside it, `docs/HANDOFF-2026-08-30.md` carries programme
state: authoritative vs stale trees, priority strategy, anti-hallucination protocol, commit
discipline, and what is blocked on Rea.

**Why:** the rules kept dying with each chat session. Written into the repos, they travel with the
code and reach whichever agent opens it.

**How to apply:**
- When Rea states a new standing rule, write it to this memory directory **and** add it to
  `AGENTS.md` in every repo. Memory is canonical; `AGENTS.md` is the distribution copy.
- Keep the copies identical — edit the desktop repo's `AGENTS.md`, then copy it out.
- Refresh `docs/HANDOFF-*.md` at the end of any long working session so state does not go stale.

Related: [[patent-standard-blueprints]], [[local-is-the-authority]], [[queue-dont-interrupt]],
[[one-house-many-rooms]], [[installers-carry-every-dependency]],
[[publish-every-release-to-vps-registry]], [[commanddeck-suite-architecture]].
