# Shared agent memory

Every AI harness Rea uses - Claude Code, Codex, Antigravity - reads and writes this folder.
It lives in the project, not in any one harness's config, so all three see the same thing and
a session on any platform starts already knowing where the work stands.

## The contract

**At the start of a session**, read `CONTEXT.md` and the last few entries of `SESSIONS.md`
before asking the owner anything you could have learned here.

**When something is decided or finished**, append to `SESSIONS.md`: the date, which platform
you were, what changed, and what is still open. One short entry; this is a ledger, not a diary.

**When a durable decision is made** - an architecture choice, a constraint, a rule - put it in
`CONTEXT.md`, because that is what the next session reads first. `SESSIONS.md` is what happened;
`CONTEXT.md` is what is true now.

**Never delete another platform's entries.** Append only. If something here is wrong, correct it
and say in `SESSIONS.md` that you did.

## Reading the other platforms' actual chats

This folder is a summary. The raw transcripts are still on disk, and any harness can read all
three with the `cross-platform-memory` skill:

    python <skill>/scripts/agent_chats.py list   --project "<this project path>"
    python <skill>/scripts/agent_chats.py search --project "<this project path>" --query "..."
    python <skill>/scripts/agent_chats.py digest --project "<this project path>" --out .agent-memory/chats-digest.md
