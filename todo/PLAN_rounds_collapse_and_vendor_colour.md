# PLAN — a finished round closes itself, vendors keep one colour, and Fable stops being a reviewer

> Status: **plan only, nothing implemented yet.** Scope:
> `src_mcp/{core/Commands/GateCommands.cs, src/Server/PanelService.cs}`,
> `src_vs_code/src/{panelProvider.ts, panelView.ts, rounds.ts}`, their tests, the help and the docs.
>
> Related docs: [module_extension.md](../research/module_extension.md),
> [module_server.md](../research/module_server.md),
> [PLAN_commands_and_autonomy.md](../research/PLAN_commands_and_autonomy.md).

## Three symptoms, reported by the operator

**1. Fable is gated on a reviewer that will never exist.** `PanelService.FableIsUsable()` asks the
configured REVIEWER providers whether one of them is Fable, and issues the "split with Fable" order
only then. But Fable is not a reviewer — it is a model of the **calling AI**, which already has it.
Nobody configures Fable as a vendor in this panel and nobody should, so the check is false on every
real machine and the switch is inert. Confirmed on this one: `providers` answers codex, gemini,
local. The measurement that said the order works (22/22 named Fable) only ran because the fixtures
forced availability to true.

The reasoning that put the check there was sound for a reviewer — *never name a model this machine
has not got* — and wrong about what Fable is. The operator's box is the whole decision: they know
what their assistant can run.

**2. A finished round stays open.** Rounds open themselves while they run, which is right, and then
stay open forever, which fills the list with expanded cards. What should happen: **running →
expanded; finished → closed again; opened by the person → never touched.** The current wording in
`panelProvider.expanded` states the opposite as a deliberate choice ("when it finishes it stays as
they left it") — that judgement is overruled by the person who uses it.

**3. A vendor's name is the same grey as everything else.** In `codex/PlanCritique — running` the
one word that says WHO is indistinguishable from the row it sits in. It should carry a colour, the
same colour for the same vendor in every place a vendor is named, and only the vendor word — the
rest of the line stays as it is.

## What must be true when this is done

1. Ticking *Split with Fable* issues the Fable order. No reviewer list is consulted, and there is no
   second condition a person cannot see from the panel.
2. A round that finishes closes itself **if it was this panel that opened it**.
3. A round the person opened stays open when it finishes; a running round the person closed stays
   closed; re-opening a card after the panel closed it makes it theirs again.
4. Every vendor name renders in a colour that is stable for that vendor across the round cards, the
   live section and the spending rows — and stable across restarts, since it is derived from the
   name rather than from the order rows arrive in.
5. The colours come from the editor's own chart palette, so they hold in a light theme as well as a
   dark one.
6. Nothing that comes out of a session file reaches the page unescaped.

## Constraints

- One renderer for a reviewer row, not a text one and an HTML one that drift. The text version is
  what the tests read today.
- The open/closed sets are pruned to living rounds, as they are now — neither may grow for the life
  of the extension host.
- `FableAvailable` is REMOVED rather than left as a field nobody sets: a flag with one caller passing
  a constant is a condition that will be misread later.
- No new dependency, and no colour hard-coded in hex: `--vscode-charts-*` adapt to the theme.

## Build order

1. **RED first, three tests**: the Fable order is issued with the switch on and no Fable vendor
   configured; a round that stops running loses its auto-open; a round the person opened does not.
2. **`GateCommands`**: drop `FableAvailable` from `CommandContext`; the switch alone decides.
   `PanelService`: delete `FableIsUsable` and `NamesFable`.
3. **`panelProvider`**: a second set — the rounds THIS panel opened. A person's toggle removes the
   key from it; a round that is no longer running and is still in it is closed and dropped.
4. **`rounds.ts`**: `vendorColour(name)` — a deterministic index into a fixed palette, so a vendor
   nobody has seen yet still gets a stable colour.
5. **`panelView`**: the vendor word wrapped in `<span class="vendor" style="color:…">`, escaped; the
   spending rows use the same function.
6. Docs: `module_extension.md`, `module_server.md`, the help entry for *Split with Fable*, CHANGELOG.

## Test plan

`src_mcp`: the Fable command is issued with `SplitWithFable` on and nothing else set; it is still
absent on a code round, on a plan that did not pass, and for a piece of a split.

`src_vs_code`: a running round is auto-opened; the same round, finished, is not; a round the person
opened stays open across the transition; a running round the person closed stays closed; both sets
are pruned to living rounds. `vendorColour` is stable for one name, differs for two common names,
and every colour it can return is a `--vscode-charts-*` variable. A reviewer row escapes a vendor
name containing `<`.

## Definition of Done

- [ ] The Fable order fires on the checkbox alone, and `FableAvailable` no longer exists.
- [ ] Finished rounds collapse; the person's own choice survives.
- [ ] Vendor names are coloured, stably, in every place they appear.
- [ ] `CoaiMcp.Tests.exe` and `npm test` pass; docs, help and CHANGELOG updated; the plan promoted.
