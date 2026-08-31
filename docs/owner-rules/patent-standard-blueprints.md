---
name: patent-standard-blueprints
description: Global rule - every task must ship with professional build and product blueprints at patent standard
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 24ef8bf9-16b3-4393-a279-c618b6b265d1
  modified: 2026-08-28T19:06:59.839Z
---

Every task, on every project, must be accompanied by full professional build and product
blueprints written to the standard of a patent filing. The user declared this a **global agent
rule** on 2026-08-28, not a per-task request.

**Why:** the user builds products, not scripts. A feature that exists only as code, with its
reasoning living in a chat log, is not a deliverable they can own, defend, hand to someone else, or
return to in a year. Patent standard means: the problem stated, the mechanism described precisely
enough to be reimplemented from the document alone, the claims/behaviours enumerated, and the
alternatives considered and rejected with reasons.

**How to apply:** treat the blueprint as part of "done", never as documentation to add later. For
CommandDeck this lives in `docs/BLUEPRINT.md`; other projects get an equivalent. Record shipped,
in-progress and planned work alike, so no requirement survives only in conversation. See
[[commanddeck-suite-architecture]].
