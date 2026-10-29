---
name: one-house-many-rooms
description: "Design principle - the whole suite is one ecosystem; mobile must feel exactly like PC and vice versa, each surface a room in one house."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 24ef8bf9-16b3-4393-a279-c618b6b265d1
  modified: 2026-08-29T19:12:16.981Z
---

The CAPITAL-IZ suite is **one ecosystem**, not a set of apps that happen to share a brand. Rea's
words: *"they must all feel like rooms in one house, each room has a bit of a personality of its
own but its main essence is the house."* Mobile must feel exactly like PC and vice versa — a full
adaptation, not a reduced port.

Concretely, when a feature exists on one platform it must be brought to the other **in substance**,
matching the original's feel: the desktop's floating, translucent, animated, borderless surfaces
(the radial wheel, the clipboard popup that appears at the pointer and disengages on click-away)
are the reference the phone is held to, and the phone's affordances feed back the other way. When
adapting a surface, study how it actually behaves on the source platform (animation, transparency,
last-state memory, theme-driven colour) and reproduce that behaviour, not just its layout.

**Why:** the suite is CommanDeck with Mouse n Key inside it — on PC two installable products, on
mobile one app called CommanDeck. A surface that looks or behaves like a lesser version of its
counterpart breaks the sense of one product. Rea has repeatedly rejected mobile work that was
"just basic styles" against the desktop's full complexity.

**How to apply:** before building a mobile surface that has a desktop equivalent (or the reverse),
read the source implementation and match its essence — theme depth (all 59 palettes with gradient,
glass, blur, not flat colour), motion, translucency, and state persistence. Prefer the shared
packages (`capital_iz_theme`, the sync protocol) over forking. Related:
[[commanddeck-suite-architecture]], [[patent-standard-blueprints]].
