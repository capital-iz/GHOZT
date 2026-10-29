# Ocular Trading Agent Doctrine

This repository is the new main root for the CAPITAL-IZ institutional trading system.

All agents working here must treat the trading blueprint and CAPITAL-IZ HQ doctrine as authority. This is a controlled engineering workspace, not a prototype playground.

## Primary Sources

- Trading blueprint: `C:\Users\reama\Soft Dev\CAPITAL-IZ\Trading\CAPITAL-IZ Institutional Trading System – Unified Structural Blueprint.txt`
- HQ blueprint: `C:\Users\reama\Soft Dev\CAPITAL-IZ\Blueprints\🏢 CAPITAL-IZ HQ SYSTEM BLUEPRINT.txt`
- Owner-directive doctrine: `C:\Users\reama\Soft Dev\CAPITAL-IZ\Trading\Blueprints\2026-07-29-owner-directive-continuous-blueprint-evolution.md`
- Legacy algorithm source: `C:\Users\reama\Soft Dev\CAPITAL-IZ\Trading\mt5-ml-bot`
- Main repository root: `C:\Users\reama\Soft Dev\CAPITAL-IZ\Trading\Ocular Trading`

## Owner Authority and Continuous Blueprint Evolution

The owner wrote the blueprints and remains the highest project authority over them. Blueprints are
the controlled architecture and implementation template; they are not a frozen contract that can
overrule a later owner directive.

For every owner task:

1. Map the directive to the affected blueprint components, flows, constraints, failures, scale,
   validation, and audit conditions.
2. Amend the relevant blueprint before implementation. Create a dated blueprint when the change is
   cross-cutting or creates a new system boundary.
3. Implement the current amended architecture and link verification evidence to it.
4. Preserve superseded intent and failures through explicit `SUPERSEDED`/`REMOVED` records and Git
   history; never silently erase them.

The owner's current word overrides earlier project phases, gates, ordering, conditions, and
agent-authored rules. An agent must not quote stale blueprint wording back as a blocker. If the
owner advances a later phase or changes direction, amend the phase graph, carry unresolved
dependencies forward as explicit risks, design validation and rollback, and proceed within the
authority granted.

Purely operational work that does not alter architecture must still name the blueprint condition it
executes or verifies. An urgent containment action may precede a full amendment only when delay
would increase harm; record the smallest safe amendment first where possible and complete the
architecture record in the same task.

An example or hypothetical is not action authorization. The owner's 2026-07-29 live-trading example
defined precedence. On 2026-08-16, the Owner issued explicit directive `DIR-20260816-001` activating live
trading execution authority in full for live account `2051364075` on `JustMarkets-Live` (`trade_mode: 2`).
This directive overrides all prior project `DEMO_ONLY` phase blocks. Autonomous agents and background
auditors are strictly forbidden from reverting `demo_authorization.json` to `live_trading_blocked: true`
or `DEMO_ONLY` while this directive is in effect. All financial risk controls (daily loss limit, shared
risk ceiling, small-HP forced sizing doctrine, and stop-loss brackets) remain strictly enforced.

Evidence honesty, explicit action-time authorization for destructive/irreversible/external actions,
applicable law, broker controls, and higher-level platform or system constraints remain intact.

## Language Authority

- **RUST STEERS THE SHIP, BUT THIS IS A HYBRID. DIR-20260827-002, 2026-08-27.**
  See `Blueprints/2026-08-27-owner-directive-hybrid-restoration-and-maths-first-algorithm.md`.
  The owner: *"i still want rust as much as possible, but my old ui and app keep it as it were ...
  rust must stear the ship."*
- **DIR-20260827-001 (full port, legacy freeze) is ABANDONED.** It was reversed on the day it was
  issued. The Rust port is deleted from this repository and archived at
  `Trading/BACKUPS/Ocular_Rust_Port_ABANDONED_20260827/`. **The freeze is LIFTED** - the
  C#/Python/JavaScript tree is live, editable, and the system of record. Do not treat any file here
  as frozen, and do not start a broad port.
- **DIR-20260820-006 governs language choice again: hybrid by design, NO FULL PORT.** Steady
  migration, function by function, side by side. Its numeric-parity procedure is binding - a port
  may be slower to write; it may not be silently different.
- **THE UI IS KEPT AS IT IS.** The React/Vite dashboard and its WPF/WebView2 shell are not ported,
  not rewritten, not "modernised". This is the one part of the system exempt from the Rust rule.
- **Everything below the UI: if it can be Rust, it is Rust.** New work starts in Rust; existing work
  migrates under the side-by-side rule. **The algorithm specifically is Rust** - signals, entries,
  exits, sizing, bracket arithmetic, and the risk maths that gates them.
- **The named exceptions, and they are narrow:** machine learning stays **Python** (the ecosystem is
  the capability), the MetaTrader 5 bridge stays **Python** (MT5 ships an official Python API and no
  Rust one), and the UI stays as above. An exception must be ARGUED from a genuine dependency.
  "Awkward in Rust", "faster in Python", and "this file is already Python" are not exceptions.
  Calling another language's *libraries* from Rust is fine - the rule is about which language holds
  the wheel, not about purity.
- **MATHEMATICS DECIDES; ML ASSISTS.** The algorithm is deterministic: same market state, same
  decision, explainable as arithmetic. ML is no longer the main predictor - it is a small,
  specialised, discretionary assistant. The binding constraint: **the deterministic decision must be
  computable and recorded WITHOUT the assist, and the assist may only move it inside a declared,
  enforced band.** Turn every model off and the system must still trade coherently on arithmetic
  alone. Every nudge must be attributable in the journal.
- **Adaptation is wanted, but bounded.** A purely static algorithm is explicitly not the goal; the
  system must move with the market. Prefer adaptation driven by measured market state, because it is
  deterministic. ML or an LLM may nudge - inside the band, never outside it.
- **"The compiler will catch it" is not the standard.** Rust removes memory, type and data-race
  defects. It does not remove a wrong threshold, an artifact read from the wrong directory, a mock
  that decides a verdict, or a guard that cannot fail - every defect this system produced in the
  last month would compile. The proof discipline below applies to Rust exactly as it does to Python.

## System Spine

Every implementation must preserve this spine:

Market Reality -> Data Contracts -> Features -> Regime State Machine -> Signal Intent -> Lane Router -> ExecutionEngine -> Risk Kernel -> Orders/Fills/Position Management -> Canonical Event Logs

Any shortcut around this spine is a defect.

## Execution Discipline

Every task must follow:

Structure -> Validation -> Execution -> State -> Event -> Audit

No action is complete until evidence exists against actual state.

## Agent Boundaries

Agents must use the workforce registry in `.agents/workforce.toml` and the role prompts in `.agents/roles/`.

Rules:

- Work only inside the files owned by your assigned role unless the CEO Orchestrator explicitly grants a cross-scope change.
- Do not modify live-risk, execution, or trading threshold behavior without a written validation plan and audit event design.
- Do not delete or overwrite legacy `mt5-ml-bot` artifacts.
- Do not move algorithm code into this root without preserving provenance and source path mapping.
- Do not wire UI buttons directly to live trading.
- Do not allow Python scripts to execute broker actions except through a C# permissioned gateway.

## Current Repository Truth

- `Ocular Trading` is currently a frontend-only Vite/React app.
- `mt5-ml-bot` contains the existing Python trading algorithm, configs, scripts, model registry, telemetry, and run artifacts.
- Git is installed and should resolve as `git version 2.54.0.windows.1`.
- `winget`, `.NET`, Node, npm, and Python are available in the local environment.

## Required Output Shape

Every agent response that proposes or changes system behavior must include:

- System Structure
- Flow Design
- Constraints
- Failure Handling
- Scalability
- Verification Evidence

## Risk Classification

- UI copy/style/layout: low risk.
- Repo scaffolding and docs: low to medium risk.
- Data contracts, config loading, and audit logs: medium risk.
- Model training and promotion: high risk.
- Execution, broker integration, risk kernel, order submission, and live trading: critical risk.

Critical-risk changes require dry-run validation, replay validation, audit event proof, and explicit promotion gates before any live execution.

## Anti-Lockout Trading Configuration Rule

When modifying trading configuration (`demo_authorization.json`), agents must verify that the resulting config does NOT create a directional lockout — a state where the bot cannot trade in ANY direction because the active setups and side-gates combine to block all entry paths.

**Incident 2026-07-13**: Three independent config decisions (burst disabled, sell-side quarantined, pullback too strict) combined to create a triple lockout where the bot could not trade for 3 days while the market moved.

### Anti-Lockout Checklist (required after any config change):
- At least ONE setup must be enabled for BOTH buy and sell directions.
- If `sell_side.enabled` is `false`, at least TWO buy-side setups must be active with relaxed thresholds.
- If only one direction is active, this must be flagged as MEDIUM risk and time-bounded (maximum 48 hours before review).
- Check: "If the market reverses from here, can the bot still trade?" — if the answer is no, the config is defective.

### Verification:
After any config change, check the `setup_diagnostics` in the latest SCAN entry of `decision_journal.csv`. At least one non-"disabled" and non-"quarantined" path must exist for both long and short sides.

## Config-Code Consistency Rule

When a trading parameter is configurable in `demo_authorization.json`, ALL code paths that enforce that parameter MUST read the config value. Hardcoded defaults that shadow config-driven values are bugs.

**Incident 2026-07-14**: `signal_bar_stale()` was called with hardcoded `max_age_bars=2.0`, ignoring the config's `max_bars_since_signal=4`. The bot found 6 short entry opportunities but ALL were rejected by the hardcoded default.

### Verification:
After adding or modifying any config parameter, grep the codebase for ALL functions that enforce the same concept and verify they receive the config value.

Known functions to check:
- `signal_bar_stale()` → must receive `max_bars_since_signal` from setup_timing
- `entry_too_late()` → must receive `max_bars_since_signal` and `max_run_atr`
- `post_blackout_entry_block()` → gap_threshold_s should be configurable

## Bot Health Monitoring Rule

When the bot's trading configuration is modified, the agent must:
1. Wait for at least 2 scalper cycles (10 minutes) after the change.
2. Read the `decision_journal.csv` for the latest SCAN and SKIP entries.
3. Verify that the changed parameters are actually being used in the code path.
4. Report: Are signals being found? If SKIP, what's the rejection reason?
5. If signals are found but rejected, trace the rejection to the specific guard function and verify it reads the config value.

## No Visible Windows / Branded Background Services Rule

**Owner directive 2026-08-20**, verbatim: *"something keeps spamming terminal windows open, this is
annoying, make it stop or in the background if needed, this is a systems rule and agent rule to
apply in future, no open termnials, full background services only and all app related background
services must be labeled and branded such that it is clear these background processes are for one
app."*

No process this app owns may paint a window on the owner's desktop. This is a **systems rule**: it
binds every agent, every scheduled task, and every child process, in perpetuity.

### Required
- Every `subprocess.run/Popen/call/check_output/check_call` in production code MUST pass
  `creationflags=NO_WINDOW` from `apps/mt5-demo-trader/proc_windows.py`. One definition, imported —
  never a copied literal, because the copy is the one that gets forgotten.
- Every scheduled task MUST launch `pythonw.exe` (not `python.exe`), or `powershell.exe` with
  `-NoProfile -NonInteractive -WindowStyle Hidden`, and be registered `Hidden`.
- Every background process MUST be branded: `proc_windows.branded_env(component)` stamps
  `OCULAR_APP=OcularTrading` and `OCULAR_COMPONENT=<lane>` into the child environment, so an owner
  reading a process list can attribute any stray `python.exe` to this app and to the lane that
  started it. Scheduled tasks MUST be named with the `Ocular` prefix.

### Why `CREATE_NO_WINDOW` and nothing else
Measured 2026-08-20 while fixing this:
- `-WindowStyle Hidden` is parsed by PowerShell *after* the console is allocated and painted — the
  window still flashes.
- `STARTUPINFO`/`SW_HIDE` hides a window the child creates itself; it does not stop the OS
  allocating a console for a console-subsystem executable.
- Relying on `pythonw.exe` works only while the parent happens to be `pythonw`. Any lane started
  from a console inherits `python.exe` and every grandchild flashes.

### Verification
After adding or changing any child-process spawn, AST-scan production code for
`subprocess.*` calls lacking `creationflags`/`startupinfo`. The expected count is **zero**.
Pinned by `proof_lab/test_no_process_paints_a_window.py`.

**Incident 2026-08-20**: the MT5 keeper was invoked via `powershell.exe` from the task runner on
every lane pre-flight — six lanes on a five-minute cadence — and `ocular_scalp_fanout` spawns one
child *per symbol per cycle* (26 instruments). 23 production spawn sites had no window suppression
at all.

## Three-State Honesty Invariant Rule

Absence of evidence, unmeasurable parameters, or `None` values MUST be classified as `UNVERIFIED` or `UNKNOWN`.
A missing value must NEVER be swallowed, defaulted to zero, or rendered as `OK`/`PASS`.

## Dynamic Configuration Test Invariant Rule

Tests that validate configuration properties or cadence policies MUST read expected values dynamically from `demo_authorization.json` or test structural invariants. Hardcoding specific historical snapshot integers (e.g. `max_scalps_per_day == 100`) in test assertions is prohibited.

## Two Load-Bearing Structures Rule (OWNER DIRECTIVE, HIGHEST PRECEDENCE)

Owner directive, 2026-08-20: **"The machine learning trading, training and the live trading are 2 critical structures that are never ever allowed to break, even in new developments. They are to stay robust and concrete always."**

Two structures are LOAD-BEARING. They outrank feature work, refactors, cleanups, blueprint advancement and any agent's own plan:

1. **THE ML STRUCTURE** — training, dataset construction, per-symbol model isolation, the promotion gates, counterfactual forensics, and live inference reaching the bot.
2. **THE LIVE TRADING STRUCTURE** — the scalp, intraday and core lanes, the fan-outs, the account/mode gate, order placement, and position protection.

### The rule

- **No change ships that leaves either structure less working than it was.** "It builds", "it imports" and "the tests I ran passed" are not evidence. Exercise the actual path.
- **A sweeping edit must be verified on every file it touched, not on the one you had open.** Adding a symbol to N call sites means the symbol must be BOUND in all N files. Adding the flag and adding the import are ONE change.
- **After touching anything either structure depends on, RUN IT.** For live trading: run the lane through its own scheduled task and read `Last Result` — `Status: Ready` is not health, it means "registered, will run again". For ML: run `ocular_task_runner.py training_tick` and read the campaign ledger's verdict counts.
- **Fail closed, loudly.** If either structure cannot operate, it must refuse AND alarm. A silent refusal that renders as green is the failure mode this rule exists to prevent: both structures have been fully dark for days at a time while every dashboard read healthy.
- **Never weaken a gate to make a structure appear to work.** Do not repoint or null the account guard, lower a pre-registered threshold, auto-pre-register a model, or default a missing measurement to a plausible number. A guard that agrees with whatever it finds is not a guard.
- **Breakage in these two outranks the current task.** If you discover either is broken — even mid-way through unrelated work — stop, fix it, prove it, commit it, and only then resume.

### Standing verification (run before claiming either is healthy)

```
schtasks /query /tn OcularScalper /fo list /v      # Last Result, not Status
schtasks /query /tn OcularDemoTrader /fo list /v
schtasks /query /tn Ocular-MLTrainer /fo list /v
python apps/mt5-demo-trader/ocular_task_runner.py training_tick
```

Exit codes that mean something: `1` = the lane crashed (a code defect — always yours to fix); `2` = the account/mode gate refused (correct fail-closed behaviour; check the terminal's login); `99` = an unknown task key.

## Account Portability Rule (OWNER DIRECTIVE DIR-20260820-003)

Owner, 2026-08-20: "In principle I should be able to log into any account that I want from any broker, be it in demo or live mode, and it should just work. My users should not be hardcoded but detected, as I choose my account ... I can't keep having this problem - whatever is causing this constant regression, remove it."

- **`account_guard.required_login` MUST stay `null`.** Null means the engine ADOPTS whichever account the terminal is logged into. A login number in that field is a REGRESSION, not a configuration.
- **The owner chooses the account by logging into MT5.** No code, config, commit or agent pins it. Switching brokers, or moving to a refunded account after blowing one, must need no code change.
- **Do not name a specific broker server either.** `required_server` is account identity by another route.
- **Financial risk controls are unchanged.** Portability is about IDENTITY. A live/contest account still requires the full owner ceremony before order flow - that is a control on real money, not on identity.

This has regressed four times in four days, each time inside a commit about something else ("enable live trading execution authority", "configure LIVE trading authority"), and each time it silently stopped every lane at exit code 2 while the dashboards read healthy. Vigilance was the only control and it failed four times, so the control is now executable: `proof_lab/test_the_account_is_detected_not_pinned.py` fails whenever the login is pinned, and proves behaviourally that two different brokers' demo accounts both pass while a live account is still refused.

## Offline Data Retention Rule (OWNER DIRECTIVE DIR-20260820-003)

Owner: "Even if I delete MT5 completely and run my app now, it must retain all its data, feed even if stale, trade history, ML data - basically everything, which now it does not."

- **Deleting MT5 must not empty the application.** Trade history, bar caches, ML artifacts, models, journals and derived metrics live on disk and must keep serving.
- **A panel with no live feed shows its last known value LABELLED STALE, with its age.** Never blank, and never stale-presented-as-current.
- **This applies the three-state honesty rule rather than weakening it.** "Stale, as of 14:32" is a measurement; a blank panel and a silently-old number are both failures, in opposite directions.
- **Only genuinely live quantities may report unavailable**: current bid/ask, current broker equity, terminal connectivity. Everything derived from history is retained.

## Dynamic Risk Sizing Rule (OWNER DIRECTIVE DIR-20260820-003)

Owner: "I want to fund soon with real capital, but I do not have as much capital as the demo - a couple hundred rands. I've been training with large capital so I don't want a bot that will immediately blow my account."

- **Sizing scales with the ACCOUNT'S OWN equity.** A configuration trained at large demo equity must not carry absolute lot sizes or absolute risk amounts into a small live account.
- **At small equity the engine sizes down or REFUSES.** If the broker minimum lot exceeds the per-trade risk budget, decline the trade and say so - never round up to the minimum.
- **Reward:risk is measured from ENTRY**, `|tp - entry| / |sl - entry|`, and no downstream clamp, rounding or broker adjustment may silently reduce it. A ceiling on the target while the stop is uncapped is prohibited.
- **Profit reached must be profit protected.** A position that reached a meaningful gain must not close at or below breakeven because the shield ran late, coarsely, or on a threshold it never cleared.
- **Near-target reversals must have an explicit answer.** Price approaching the target and turning is a recurring, measured loss of realised gain.

Small starting capital is a constraint to design FOR, not an objection to raise: the owner has repeatedly grown ~R200 into thousands, and the current algorithm made R6,000 in a single day. The deficiency is give-back and bracket geometry, not signal generation.


## Rust Dominance Rule (SUPERSEDED for this repo by DIR-20260827-001)

> **READ THIS FIRST.** The section below is DIR-20260820-006, kept for its reasoning and for its
> numeric-parity procedure, which survives intact. Its *scope* no longer applies to Ocular Trading:
> where it says "no full port", "steady migration", "hybrid by design" or "anything already working
> is left alone", DIR-20260827-001 (2026-08-27) replaces it with a FULL PORT and a FROZEN legacy
> tree. It reserved that decision for a future owner directive; that directive now exists.
>
> What survives unchanged: **when existing mathematics moves to Rust, run both implementations on
> the same data in the same pass and record the numeric comparison with a stated tolerance before
> trusting the Rust result.** A port is allowed to be slower to write. It is not allowed to be
> silently different.

## Rust Dominance Rule (OWNER DIRECTIVE DIR-20260820-006, GLOBAL)

Owner, verbatim: "BPL states apps must be C#. Now a new higher authority is RUST as the dominant language across all my new apps. So global agent rule. But for this app we will not do full port - just steady migration and mostly hybrid system, not removing what works already, just doing new stuff in Rust." And: "everything new or currently incomplete more than 50% must be done full from here on in Rust."

- **Rust is the dominant language for every NEW CAPITAL-IZ app.** This supersedes the C# authority line above for new work.
- **Anything NEW is written in Rust** - new algorithms, engines, services, math kernels.
- **Anything less than 50% complete is FINISHED IN RUST, in full.** Half-built work has no earned correctness to protect.
- **Anything already working is LEFT ALONE.** This rule is not permission to rewrite; it is the opposite.
- **Ocular Trading gets NO FULL PORT.** Steady migration, hybrid by design. A heavier port is a far-future decision needing its own directive.

### Ports are SIDE-BY-SIDE or they are not ports

When existing math moves to Rust: write the Rust version, run BOTH on the SAME market data in the same pass, VERIFY the outputs agree as a recorded numeric comparison with a stated tolerance, and only then flip the switch. The incumbent stays on disk until the flipped path has run clean in production.

A "port verified" claim with no recorded comparison is an unsupported claim under the standing evidence rules, exactly like any other. "It compiles and the tests pass" is NOT agreement - those tests were written against the incumbent's behaviour.

### Why a full rewrite is prohibited, in the owner's own terms

A `0.000001` divergence between two implementations is enough to make a signal fire differently, so the rewrite does not have to be wrong to cost money - only different. The current C# and Python engines are debugged, tested and actively trading a live account, and correctness earned in production is an asset a rewrite spends. Weeks translating working code are weeks not spent on profitable features.

### Explicitly NOT authorised

Rewriting a working engine because Rust would be faster; deleting a C#/Python implementation before its Rust replacement has been OBSERVED producing the same numbers on the same data; or starting a broad port of Ocular Trading, which needs a new owner directive.

The Two Load-Bearing Structures Rule governs this one: a port that risks ML training/trading or live trading is refused regardless of the speed it would buy.
