---
name: local-is-the-authority
description: "When local and git disagree, local wins and git must be made to match; work by other agents in the repo counts and must be acted on."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 24ef8bf9-16b3-4393-a279-c618b6b265d1
  modified: 2026-08-29T12:54:37.062Z
---

If the local working tree and the git remote disagree, **local is the authority**. Git must
always be brought to match local, never the reverse. Rea considers it unlikely that the remote
is ever ahead of local, so a divergence should be read as "local has work git has not received"
rather than "local is stale".

Rea uses coding agents other than Claude on these repositories. So Claude will routinely find
work in a repo that it did not write and has no memory of. **That work still counts.** Do not
discount it, do not treat it as suspect, and do not discard or revert it because its provenance
is unknown — if it is in the repo, act on it and commit it.

**Why:** work from other agents was at risk of being ignored or overwritten precisely because
Claude had no record of doing it. Treating unfamiliar work as authoritative is what keeps a
multi-agent repository from losing days of effort. This has already happened once on this
programme — see [[commanddeck-suite-architecture]] for the mirror-versus-authoritative-tree
trap that cost real work.

**How to apply:** before any merge, subtree resync, checkout or stash, run `git status` and
confirm nothing is mid-edit by someone else; commit only your own files by explicit path, never
`git add -A`. When a push is rejected because the remote is ahead, merge the remote work in and
redeploy so the end state is the union — never force-push local over it. Related:
[[patent-standard-blueprints]].
