---
name: four-products-four-places
description: Layout rule - the suite is four products in four folders; never nest one product inside another or create a container folder holding copies of them.
metadata: 
  node_type: memory
  type: project
  originSessionId: 24ef8bf9-16b3-4393-a279-c618b6b265d1
  modified: 2026-08-31T12:35:22.463Z
---

The CAPITAL-IZ suite is **four products in four places**, and nothing else is a product:

| Product | Path |
|---|---|
| CommanDeck — PC | `Apps\Desktop\Command Deck\App` (nested repo inside the `Command Deck` repo) |
| Mouse n Key — PC | `Apps\Desktop\Mouse n Key` |
| CommanDeck — mobile | `Apps\Mobile\Commandeck` |
| Mouse n Key — mobile | `Apps\Mobile\Mouse N Key` |

**Never nest one product inside another product's folder, and never create a container folder that
holds copies of the products.** Rea's words: *"i never said put them in \CAPITAL-IZ DESKTOP because
that is its own app!!! ... my apps exist in two places now and i dont know which is the true latest
and stable app, this is wrong."*

**Why:** a `CAPITAL-IZ DESKTOP` folder was created holding second copies of Command Deck and Mouse n
Key, and a `CAPITAL-IZ MOBILE` folder held a second copy of the mobile app. Releases were built from
the wrong copy for three cycles, and Rea could not tell which tree was current — the folder dates
pointed one way and the real source dates the other. Both containers were removed on 2026-08-31 and
the work consolidated back into the four paths.

**How to apply:**
- Confirm your path against the table above before building or editing. Wrong-tree builds are the
  single most expensive mistake made on this programme.
- If you find a duplicate tree, **stop and tell Rea**. Do not pick one and delete the other.
- Keep programme docs (backlog, audit, ledgers, handoff) inside a repo that tracks them —
  `Apps\Desktop\Command Deck\docs\`. They were once kept in an untracked folder and were lost from
  disk when it was removed; they had to be recovered from the GitHub remote.

Related: [[local-is-the-authority]], [[agents-md-is-the-rule-carrier]], [[one-house-many-rooms]].
