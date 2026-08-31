# Owner's standing rules — mirrored

These files are a **read-only mirror** of Rea's persistent memory at:

```
C:\Users\reama\.claude\projects\C--Users-reama-Soft-Dev-Capital-Iz-Apps\memory\
```

They are mirrored here so that an agent on **any** platform — Codex, Cursor, Copilot, Antigravity, a
web chat with only this repository attached — can read the same standing rules that Claude Code
loads automatically. Rea should not have to re-explain his rules per tool.

`MEMORY.md` is the index; each file beside it is one rule with its reasoning.

## Which copy wins

**The path above is the authority.** This folder is a snapshot, and a snapshot goes stale. If the two
disagree, the file under `.claude\projects\...\memory\` is correct.

Refresh the mirror with:

```powershell
Copy-Item "$env:USERPROFILE\.claude\projects\C--Users-reama-Soft-Dev-Capital-Iz-Apps\memory\*.md" `
          "Apps\Desktop\GHOZT\docs\owner-rules\" -Force
```

## Do not edit these here

Writing a rule into this mirror does not change what Claude Code loads, so it produces two versions
of a rule and the wrong one gets followed. Change the authority, then re-mirror.
