# PLAN — a code role with its box unticked does not take part in the round

> Status: **IMPLEMENTED, 2026-09-08.** Scope: the four code-review role boxes in the panel, the
> setting behind them, and the server that schedules a round.
>
> Related docs: [module_extension.md](module_extension.md), [module_server.md](module_server.md).

## The goal

The operator, 2026-09-08, over a screenshot of the four code-review boxes — Conventions,
Architecture, Security & reliability, Performance & UX-DX:

> add checkboxes here, from the active list. If a section's box is unticked it is skipped

and, asked what "skipped" means and whether it applies to the plan stage:

> it is "the role does not take part in the round at all" if the box is unticked. Code review only.

*(translated from the operator's Russian; this repository's documentation is English.)*

Today a role can only be turned off by lying to a different control — setting its rounds to a number
that keeps it out. That is not the same statement, it is not readable as one on the panel, and it
loses the number the person will want back when they turn the role on again.

## What must be true when it is done

1. Each of the four CODE role boxes carries a checkbox, ticked by default, and unticking it is
   persisted the way every other per-role setting is.
2. `PlanCritique` gets **no** checkbox. The plan stage has one role, and a switch whose only setting
   turns the whole stage off is a different feature nobody asked for.
3. An unticked role launches **no reviewers** in any code round — not a reviewer whose findings are
   ignored afterwards.
4. An unticked role's threshold and round budget stop counting toward the STAGE. Leaving them in
   would keep a stage running rounds that nobody reviews.
5. Unticking every one of the four is refused where it is discovered, with a message naming what to
   re-tick — never a code round that launches nobody and reports "0 reviewers answered", which the
   session state already treats as an unresolved round.
6. The panel's own arithmetic agrees: the fan-out sentence and the round-limit note count the roles
   that will actually run.
7. An unticked role's rounds, threshold and prompt selections survive being turned off and come back
   unchanged when it is turned on.
8. A server that receives no such setting behaves exactly as it does today — every role on.
9. The setting reaches the server by the same route as every other per-role setting, and only when
   it differs from the default, so a pristine configuration still writes no env at all.

## The change

### Server (`src_mcp`)

- **`core/Rounds/SessionState.cs:38`** — `RoleGate(int MaxRounds, int Threshold)` gains
  `bool Enabled = true`. A positional default keeps every existing construction site compiling and
  keeps "absent means on" true by construction.
- **`core/Rounds/SessionState.cs:106`** — `RolesForRound` already filters by budget
  (`For(r).MaxRounds >= round`); it gains `For(r).Enabled`.
- **`core/Rounds/SessionState.cs:96`** — `For(Stage)` takes `roles.Max(...)` over the stage's roles.
  It must consider only enabled ones, and `Max` over an empty sequence throws — so the all-off case
  is decided here rather than crashing here.
- **`src/Server/PanelSettings.cs:540`** — `RoleGates` reads `COAI_ENABLED_<ROLE>`, beside the
  existing `COAI_ROUNDS_<ROLE>` and `COAI_THRESHOLD_<ROLE>`. Absent means on.
- **`src/Server/PanelService.cs:368`** — where a code round is assembled. The skipped roles are
  logged the way `RolesWithRulesInMind` logs the Conventions drop, and a round with no roles left is
  refused with a message that names the panel setting.

### Panel (`src_vs_code`)

- **`src/settingsShape.ts:176`** — a `roleEnabled` record beside `rounds` and `thresholds`, all four
  code roles `true`. `settingWrite` (`settingsShape.ts:142`) already routes any `data-role` message
  into a merged role record, so the write path needs nothing new — this is reuse, not new plumbing.
- **`src/settingsShape.ts:288`** — `envBlock` writes `COAI_ENABLED_<ROLE>=false` for a role that
  differs from the default, exactly as the rounds and threshold loops beside it do.
- **`package.json`** — `contributes.configuration` declares `coai.roleEnabled`. Not optional: VS Code
  will not persist a key it does not know, and this repository has already shipped that bug once —
  the three gate switches lit up, saved nothing, and said nothing.
- **`src/panelView.ts:893`** — the role box's head becomes a checkbox plus its label, the same shape
  the vendor card's head already uses (`panelView.ts:527`), and the role's controls are dimmed and
  disabled when it is off, as the vendor card's `.stages.off` block already does.
- **`src/panelView.ts:798`** — `roundLimitNote` counts code roles to derive the round budget; it must
  count the ENABLED ones. Same for the fan-out sentence.

## Constraints

- **Only the code stage.** `PlanCritique` is not given a switch and its behaviour does not change.
- **Absent means on**, in both the env parser and the panel's reader: an older panel driving a newer
  server, and a newer panel with a stored record that predates the key, must both review everything.
- **Nothing is removed to implement this.** A role that is off keeps its rounds, its threshold and
  its prompts in the settings; the switch is a fourth fact about the role, not a way of clearing the
  other three.
- **No mutate-in-place filtering.** `RolesWithRulesInMind` was written as `roles.Remove(...)` and two
  gate reviewers named it as the shape `coding-style.md` forbids; the new filter is derived the same
  way that one now is.
- The panel modules stay `vscode`-free and pure where they already are.

## Test plan

- **RED first, server:** a `PanelConfig` with Architecture disabled — `RolesForRound(CodeReview, 1)`
  must not contain it. Fails today because the concept does not exist.
- `For(Stage.CodeReview)` reports the widest ENABLED role's budget, and the all-off case is decided
  rather than throwing.
- `COAI_ENABLED_ARCHITECTURE=false` disables exactly that role; an absent variable leaves every role
  on; a malformed value is treated as on rather than as off.
- A code round with every role off is refused with a message naming the setting.
- **Panel:** the four code boxes render a checkbox and `PlanCritique` does not; unticking writes
  `roleEnabled` merged rather than replaced; `envBlock` emits `COAI_ENABLED_*` only for a role that
  differs from the default; the round-limit note and the fan-out sentence drop a disabled role.
- `settingsAreDeclared.test.ts` already asserts every written key is declared in the manifest — the
  new key must pass it rather than need an exception.
- Both suites in full: the xUnit executable for `src_mcp`, `npm test` for `src_vs_code`.

## Definition of Done

- [ ] Four checkboxes, ticked by default, persisted; none on the plan role.
- [ ] An unticked role launches no reviewer and counts toward no stage budget or threshold.
- [ ] All four off is refused with a message naming what to re-tick.
- [ ] The panel's predicted fan-out and round limit match what will run.
- [ ] Absent setting = every role on, in the parser and in the panel.
- [ ] Documentation updated: `module_extension.md`, `module_server.md`, `module_core.md`, this plan
      promoted to `research/` with its deviations, `research/README.md`, CHANGELOG, version bumped.
- [ ] Both test suites green.

## What shipped differently

Three things the plan did not have, all of them from the gate's plan round:

1. **The stage's THRESHOLD, not only its round budget, ignores a disabled role.** The plan named the
   budget and stopped there; a role switched off with a threshold of nine would have held the gate
   open against a number no reviewer could bring down.
2. **The all-off case has a named contract instead of a promise not to throw.** `EnabledRolesOf` is a
   member both callers read, `For(Stage)` answers `(0, 0)`, and `ReviewCodeAsync` refuses before the
   scope check and before any worktree — so the refusal happens before session state exists rather
   than after a round was created.
3. **The env value is read by its own helper, not by the existing flag one.** `NotSwitchedOff`
   disables a role only on the four spellings of false; absent, empty, `no`, a typo and a
   shell-mangled value all leave the reviewer working. A role wrongly ON costs one extra pass, a role
   wrongly OFF is a review nobody performed.

Two more arrived while building it:

4. **The last ticked role cannot be unticked**, refused at the pointer rather than at round time. The
   server's refusal stays as well — a Team server and a hand-written `mcpServers` block have no
   checkbox to look at.
5. **`ROLE_SWITCH_SINCE`**, mirroring `CONVENTIONS_ROLE_SINCE`: while the installed `coai-mcp` is
   older than 0.18.13 it never looks for `COAI_ENABLED_*` and runs the role anyway. That skew fails
   backwards — an unticked box that still reviews tells a person the opposite of what is happening —
   so the panel says so out loud, naming the roles it would run.

One finding was rejected: that the defaults object might not initialise the four keys to `true`,
which is what the plan already specified. The neighbouring findings about a PARTIALLY stored record
named the real gap and were taken instead — the reader falls back per KEY.

## The open tail

- The help article and its four translations describe the switch; no test asserts a translation is
  in step with the English, so a future change to that paragraph can leave them behind.
- Nothing surfaces, in a finished round, that a role was switched off while it ran. The rounds log
  records the reviewers that answered; "and these three were not asked" is not written anywhere.
