---
name: queue-dont-interrupt
description: "Global rule - new feedback is queued, never allowed to interrupt work in flight; only an explicit stop/change-direction redirects."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 24ef8bf9-16b3-4393-a279-c618b6b265d1
  modified: 2026-08-30T08:42:38.355Z
---

New feedback arriving mid-flight is **queued, not acted on immediately**. Work already running is
carried through to completion first. Rea's words: *"as i give you feedback, make sure you're not
getting interrupted with previous feedback. add the tasks to queue if work is already busy and do
it when done. don't let my interim messages distract you unless i explicitly say stop or change
direction or something clear that your current task should change - else do not change until all
completed, even new incoming ones queued and followed through in every iteration. if incoming
queues come too late - say while you were about to build a new MSI and APK - then do them in the
next version."*

**Why:** a stream of feedback while agents are mid-task caused half-finished work, agents tearing
each other's files, and builds assembled from a tree that was still being edited. Finishing a
batch, shipping it, then starting the next is what produces working packages.

**How to apply:**
- Record every new item in the backlog (`docs/BACKLOG-*.md`) with Rea's own words, immediately, so
  nothing depends on memory.
- Do not re-task or re-brief running agents for new items; let them finish.
- If feedback lands after a build has started, that build ships as-is and the item goes into the
  **next** version. Say so plainly rather than delaying the build.
- Only an explicit stop, a change of direction, or something clearly invalidating the current task
  redirects work in flight.
- Every queued item must be followed through in a later iteration — queued is not dropped.

Related: [[patent-standard-blueprints]], [[local-is-the-authority]],
[[publish-every-release-to-vps-registry]].
