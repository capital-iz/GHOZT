---
name: filter-drivers-own-reas-input
description: "Two kernel input filter drivers on Rea's machine sit below Mouse n Key and explain most \"the app is messing up my keys\" reports."
metadata: 
  node_type: memory
  type: project
  originSessionId: 52269a7b-9b45-4fbb-b8ff-3484603e31bb
  modified: 2026-08-31T21:11:20.878Z
---

Rea's machine runs three kernel input filter drivers, confirmed 2026-08-31 by their own file
version information rather than by inference:

- `LHidFilt.Sys` and `LHidEqd.Sys` — Logitech, Inc., product "Logitech SetPoint(TM)". `HKLM\...\Run`
  also starts `C:\Program Files\Logitech\SetPointP\SetPoint.exe /launchGaming`.
- `nllKbd.sys` — Gen Digital Inc., "Gen Keyboard Filter Driver" (Norton).

**No AutoHotkey process is running.** The old compiled script Mouse n Key replaced is gone; when Rea
says "dirty assignments", that is not what it is.

**Why:** a filter driver sees a press before Windows tells any application, and can swallow it,
change it, or send something else. Nothing in user mode out-ranks one. So SetPoint's thumb-button
mapping means Windows is handed `Win+Tab` and never sees the button — Mouse n Key's binding is not
losing a race, it is waiting for an event that no longer exists. And Norton's driver consumes and
re-injects keystrokes, so a re-injected key arrives from the driver rather than the keyboard, which
is why a real key can be attributed to the mouse.

**How to apply:** when Rea reports that Mouse n Key "cancels out", keeps a removed assignment
working, or mis-reads which device a key came from, check this first and say so rather than hunting
in the app. Mouse n Key detects and names them (`InputStackConflicts` / `InputStackScanner`) and must
never unload or uninstall them — see [[input-ownership-is-not-ours-to-take]]. Until Rea removes
SetPoint, an assignment on a button SetPoint owns cannot work, whatever the app does.
