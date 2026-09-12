# PLAN — the log names EVERY model, and the effort it ran at

> Status: **plan only, nothing implemented yet.** Scope: the four hosted adapters in
> `src_mcp/runners/Reviewers/`, `ReviewerInvocation`, `ReviewerState`, and the extension's reviewer
> line.
>
> Issue [#129](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/129): *"record
> the model name and the effort in the logs. We already did this, I think. Check whether it got lost
> or was never done. Fix it."*
>
> This is the check the issue asks for, and the answer is **both**: the model half was done for two
> vendors out of six and has been silently blank for the other four ever since; the effort half was
> never recorded anywhere. Companion to
> [PLAN_the_log_names_the_model.md](PLAN_the_log_names_the_model.md), whose step 1 this repairs —
> its steps 2 and 3 (what a Team server ACTUALLY ran) stay open there and are **not** in scope here.

## What the check found

**The model is blank for four of the six adapters.** `ReviewerInvocation.Model` is a trailing
parameter defaulted to `""` (`ReviewerRuntime.cs:88-96`), and only two adapters pass it:

| Adapter | Builds the invocation at | Sets `Model`? |
|---|---|---|
| `LocalRuntime` | `LocalRuntime.cs:131` | **yes** (`Model: settings.Model`, line 169) |
| `RemoteRuntime` | `RemoteRuntime.cs:69` | **yes** |
| `CodexRuntime` (and DeepSeek, and any custom OpenAI endpoint) | `ReviewerRuntime.cs:237` | **no** |
| `GeminiRuntime` | `ReviewerRuntime.cs:304` | **no** |
| `ClaudeRuntime` | `ClaudeRuntime.cs:91` | **no** |
| `AntigravityRuntime` | `AntigravityRuntime.cs:66` | **no** |

All four pass the model to the CLI on the command line — `-m <model>` at `ReviewerRuntime.cs:240` —
so the value is in hand at the call site; it is simply not put on the record. `LiveRound` copies
`w.Invocation.Model` into `ReviewerState.Model` faithfully, so the empty string travels all the way
to the panel and the log, where `modelOf` correctly renders nothing.

**The test that covers step 1 cannot see this.** `LiveRoundTests` builds the invocation by hand —
`Work("codex", …).Invocation with { Model = "gpt-5-codex" }` — and its docstring asserts *"the
invocation has carried the model since the adapters were written"*, which is false for four of them.
A test that constructs the value it is checking for can only ever confirm itself.

**The effort is recorded nowhere.** `ReviewerSettings.ReasoningEffort` (`ReviewerRuntime.cs:21`) is
filled from `PanelSettings.LocalReasoningEffort` at `PanelService.cs:1305` and read in exactly one
place: `LocalRuntime` puts `--reasoning-effort <effort>` on the argv (`LocalRuntime.cs:152-153`). It
is on no record, in no ledger, in no log line. The only trace is a Debug-level argv dump.

## What must be true when this is done

1. **Every adapter that launches a model records which one**, on the invocation, so
   `ReviewerState.Model` is populated for codex, gemini, claude and antigravity as it already is for
   local and remote.
2. **An adapter records the effort it actually APPLIED** — not "local records one and hosted does
   not". The plan round was right that hosted APIs have reasoning controls of their own; what makes
   the difference here is that no hosted adapter in this repository puts one on its CLI's command
   line, so recording an effort for one would claim something that did not happen. Stated as a rule
   about truth rather than about vendor class, it stays correct the day a hosted adapter gains the
   flag: whoever adds `--reasoning-effort` to an argv adds `Effort:` to the same `Build`.
   `AntigravityRuntime` applies none — its effort is inside the model id
   (`gemini-3.7-flash-high`), which `modelPrices.test.ts` pins as *"a reasoning effort is not a
   different model"* — so there is no path that renders `gemini-3.7-flash-high (effort: high)`.
3. **The reviewer line shows it** — `local/Architecture · qwen3 (effort: high) — done (…)` — and a
   reviewer with no effort reads exactly as it does today.
4. **The test has teeth this time**: it goes through each adapter's own `Build`, not through a
   hand-made invocation. That is the defect above, and a test that cannot reproduce it is what let
   this ship.
5. Sessions already on disk stay readable: every new field is trailing and defaulted.

## The change

- `ReviewerInvocation` gains `Effort` (trailing, defaulted) beside the `Model` it already has.
- `LocalRuntime.Build` passes `Effort: settings.ReasoningEffort`.
- The four hosted adapters pass `Model: settings.Model` — four one-argument additions. They pass no
  effort, deliberately, per requirement 2.
- `ReviewerState` gains `Effort` (trailing, defaulted); `LiveRound` copies it as it copies `Model`.
- `rounds.ts` — `restOf` renders ` (effort: X)` after the model when there is one, normalised the way
  `modelOf` normalises the model (a session file is JSON somebody else wrote).
- `RoundAudit` — the round's own `Opening` line already receives every `ReviewerWork`, so the
  descriptor gains the model **and the effort**: the audit log is the stated fallback when the panel
  is closed, and without the effort two local runs of one model at different efforts write the same
  line (raised on the plan round). Both parts are **conditional**, so an empty model cannot produce
  `provider/Role[, promptId, N bytes]` — a descriptor with a hole in it is what a later parser
  misreads.
- Docs: `research/module_runners.md` (the adapter contract), `research/module_server.md`
  (`ReviewerState`), `research/module_extension.md` (the reviewer line), `CHANGELOG.md`.
- This plan promoted to `research/`; the companion plan's status line updated to say step 1 was
  repaired here and by what.

## Test plan (RED first)

| # | Test | RED symptom expected |
|---|---|---|
| 1 | `src_mcp/tests`: *every adapter that launches a model puts it on the invocation* — drive each of the six adapters' own `Build` with a settings object naming a model, and assert `Invocation.Model` equals it. **Through `Build`, never a hand-made invocation** | four of six return `Model = ""` |
| 2 | `src_mcp/tests`: *a reviewer records the effort it actually applied* — `LocalRuntime.Build` with `ReasoningEffort = "high"` carries it, and **every** hosted adapter given the same settings carries `""`. Table-driven across codex, gemini, claude and antigravity, because a copy-paste into one of them would otherwise show an invented effort with the suite still green (raised on the plan round) | `Effort` does not exist |
| 2b | `src_mcp/tests`: *the audit line names the model and the effort, and has no hole when either is absent* — a local run at an effort, a hosted run with a model and no effort, and a run with neither, each produce a descriptor with no empty bracket section | the descriptor carries neither, and the naive form yields `[, promptId, …]` |
| 3 | `rounds.test.ts`: *a reviewer line names the effort when there is one* — `local/Architecture · qwen3 (effort: high) — done (0 findings)`; and with no effort the line is exactly what it is today | no effort is rendered |
| 4 | `rounds.test.ts`: an effort that is not a usable string — absent, blank, whitespace, a number — renders as none, the way a model does | `.trim()` on a number throws while the log is built |
| 5 | the existing `LiveRoundTests` model assertions stay green, and the one whose docstring claims the adapters always carried the model has that sentence corrected | green, with an honest comment |

Run: `dotnet build dew_flow_connect_other_ais.slnx -c Debug` then
`./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*ReviewerRuntime*"` for the
fast loop, and the whole executable before the commit — **never `dotnet test`**, which has no VSTest
host here and aborts. Extension side: `cd src_vs_code && npm test`.

## Definition of Done

- [ ] Tests 1 and 2 written first and watched fail, naming the four adapters; then green; then red
      again with the adapter changes reverted.
- [ ] Both suites green: the MTP executable and `npm test`, counts reported in the pull request.
- [ ] The diff through the `coai` code round, every finding resolved.
- [ ] `module_runners.md`, `module_server.md`, `module_extension.md` and `CHANGELOG.md` updated.
- [ ] This plan promoted to `research/`; `PLAN_the_log_names_the_model.md` says its step 1 is whole.
