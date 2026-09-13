# Suite rule carriers — mirrored

Read-only copies of every `AGENTS.md` / `CLAUDE.md` in the Capital-Iz suite that bears on Ghozt,
taken **2026-09-01**. They are here so an agent with only this repository — Codex, Cursor, Copilot,
a web chat, a clone on another machine — can read the same doctrine Rea's local tools see, without
him re-explaining it per tool.

| File | Original |
|---|---|
| `00-Capital-Iz-AGENTS.md` | `Capital-Iz\AGENTS.md` |
| `01-Apps-AGENTS.md` | `Apps\AGENTS.md` |
| `02-CommandDeck-AGENTS.md` | `Apps\Desktop\Command Deck\AGENTS.md` |
| `03-MouseNKey-AGENTS.md` | `Apps\Desktop\Mouse n Key\AGENTS.md` |
| `04-OcularTrading-Windows-AGENTS.md` | `Trading\Ocular Trading\Windows\AGENTS.md` |
| `05-OcularTrading-Windows-CLAUDE.md` | `Trading\Ocular Trading\Windows\CLAUDE.md` |
| `06-OcularTrading-Linux-AGENTS.md` | `Trading\Ocular Trading\Linux\AGENTS.md` |

## These are snapshots, not the authority

The originals win. This folder goes stale the moment one of them is edited, and a stale rule
followed confidently is worse than no rule.

**`..\..\AGENTS.md` (Ghozt's own) is what actually binds you here.** It reconciles everything in this
folder into one current set, and it resolves the conflicts between them — of which there are two
you must know about:

1. `00-Capital-Iz-AGENTS.md` names `Apps\Desktop\CAPITAL-IZ DESKTOP\Command Deck\App` as
   authoritative and calls `Apps\Desktop\Command Deck` stale. **That path does not exist.**
   `01-Apps-AGENTS.md` and `02-CommandDeck-AGENTS.md` are newer and correct.
2. `04-OcularTrading-Windows-AGENTS.md` carries DIR-20260820-006, which made Rust the language
   for every new Capital-Iz app. **SUPERSEDED (2026-09-11):** The Owner amended the Language Policy
   on 2026-09-11: C#, Rust and Python are preferred languages, and existing repos remain in their
   established language. Ghozt stays C#/WPF. See Ghozt's `AGENTS.md` §3.

## Refreshing

```powershell
$g = "C:\Users\reama\Soft Dev\Capital-Iz\Apps\Desktop\GHOZT\docs\suite-rules"
$c = "C:\Users\reama\Soft Dev\Capital-Iz"
Copy-Item "$c\AGENTS.md"                                  "$g\00-Capital-Iz-AGENTS.md" -Force
Copy-Item "$c\Apps\AGENTS.md"                             "$g\01-Apps-AGENTS.md" -Force
Copy-Item "$c\Apps\Desktop\Command Deck\AGENTS.md"        "$g\02-CommandDeck-AGENTS.md" -Force
Copy-Item "$c\Apps\Desktop\Mouse n Key\AGENTS.md"         "$g\03-MouseNKey-AGENTS.md" -Force
Copy-Item "$c\Trading\Ocular Trading\Windows\AGENTS.md"   "$g\04-OcularTrading-Windows-AGENTS.md" -Force
Copy-Item "$c\Trading\Ocular Trading\Windows\CLAUDE.md"   "$g\05-OcularTrading-Windows-CLAUDE.md" -Force
Copy-Item "$c\Trading\Ocular Trading\Linux\AGENTS.md"     "$g\06-OcularTrading-Linux-AGENTS.md" -Force
```

**Do not edit these here.** Writing a rule into a mirror does not change what the tools load; it
produces two versions of a rule and the wrong one gets followed. Change the original, then re-mirror.
