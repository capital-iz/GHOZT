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
defines precedence but does not activate live trading. A future explicit live-account directive
overrides project `DEMO_ONLY` phase blocks and must be architected as a CRITICAL-risk transition
with account identity, limits, permissions, dry-run/replay evidence, kill switch, rollback,
monitoring, and audit events. Those controls govern execution; they cannot be used to silently
cancel the directive by reasserting an older project phase.

Evidence honesty, explicit action-time authorization for destructive/irreversible/external actions,
applicable law, broker controls, and higher-level platform or system constraints remain intact.

## Language Authority

- C# is the authority language for system execution, desktop packaging, contracts, risk, execution, state, audit, and orchestration.
- Python may be used only as a bounded analytics/model worker for training, research, feature experiments, and model artifact generation.
- React/Vite is currently a control-layer UI prototype. It must never become the trading authority.

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

## Three-State Honesty Invariant Rule

Absence of evidence, unmeasurable parameters, or `None` values MUST be classified as `UNVERIFIED` or `UNKNOWN`.
A missing value must NEVER be swallowed, defaulted to zero, or rendered as `OK`/`PASS`.

## Dynamic Configuration Test Invariant Rule

Tests that validate configuration properties or cadence policies MUST read expected values dynamically from `demo_authorization.json` or test structural invariants. Hardcoding specific historical snapshot integers (e.g. `max_scalps_per_day == 100`) in test assertions is prohibited.

## MT5 Order Filling Mode Rule

When submitting market or limit orders via MetaTrader 5, agents MUST NOT hardcode `ORDER_FILLING_IOC`. Agents must call `broker_filling.get_filling_mode(info)` to dynamically resolve `ORDER_FILLING_FOK` (Fill Or Kill) for Forex instruments and `ORDER_FILLING_IOC` for Metals/Indices.

## Zero-Order State Reconciliation Rule

When `mt5.orders_get()` or `mt5.positions_get()` returns `None`, agents MUST check `mt5.last_error()`. If `last_error()` is `Success` (code 0/1/10009), `None` MUST be interpreted as an empty set `()`, NOT as an unverified broker observation or failure state.


