---
name: input-ownership-is-not-ours-to-take
description: "Rea wants Mouse n Key to be the only thing controlling his input; the app must name rivals, never remove them."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 52269a7b-9b45-4fbb-b8ff-3484603e31bb
  modified: 2026-08-31T21:11:36.429Z
---

Rea's requirement, in his words: *"only mnk is allowed to have control on my mouse or keyboard."*
Mouse n Key answers it by **detecting and naming** competing input software in a footer banner, and
never by unloading a driver, stopping a service, or uninstalling anything.

**Why:** the requirement cannot be enforced from user mode at all — see
[[filter-drivers-own-reas-input]] — so the only honest options were to lose silently or to say who
won. And acting on it automatically would be worse than the problem: a filter driver pulled out from
under a running system is a way to lose a keyboard, and software that removes other people's software
behind the user's back is exactly the complaint being made.

**How to apply:** keep `InputStackConflicts` read-only. When a new remapper needs recognising, add a
rule and a remedy sentence, not an action. Anything that would change the machine's driver or service
state goes in `docs/WHAT-I-NEED-FROM-REA.md` as a procedure Rea performs, with the trade-off stated —
turning Norton's keystroke protection off is a security decision and it is his, not ours.
