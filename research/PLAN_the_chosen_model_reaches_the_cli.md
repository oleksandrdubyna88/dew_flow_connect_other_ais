# PLAN — the chosen model reaches the CLI

> Status: **IMPLEMENTED, 2026-09-11.** Story 4.1. Scope as built:
> `src_vs_code/src/chatAdapter.ts` (`ChatLaunch`), `cliChatLaunch.ts`, `chatProcess.ts`, `chatCommand.ts`
> and the three adapters.
>
> Related docs: [module_extension.md](module_extension.md) — *And the model it asks for is the model the
> CLI is told to use*.

## What shipped differently

1. **Three session starts, not two.** The plan counted the first launch and the model switch; the tab
   that opens a conversation is a third, and it would have carried an empty model.
2. **No flag spelling was invented.** All three are what this repository already verified for its
   REVIEWERS on the same CLIs, and codex's was read back from `codex exec --help`.
3. **codex's `-m` goes BEFORE `CODEX_ARGS`**, because the bare `-` in there is the positional that makes
   it read stdin. An option after a positional is still parsed as one, but a command line is the last
   place to ask a reader for the benefit of the doubt.
4. The tail the plan named — reading back the model a CLI REPORTS rather than the one it was asked for —
   is not built, and is still worth doing.

## The symptom

The chat tab offers a model per provider (`chatModels.ts:251-258`, the picker at
`panelView.ts:498-502`), and a Team-server chat sends the choice (`chatRemote.ts:68`,
`remoteChatSession.ts:219`). A LOCAL chat does not: `launchSpecFor` builds the command line from
`known.adapter.argv(resume)` (`cliChatLaunch.ts:176`), and the adapter contract
(`chatAdapter.ts:121`, `argv(resume: string)`) has no parameter a model could travel in. No adapter
emits a model flag. The audit compiled the launch for all three runtimes and swapped
`selected-model-A` for `selected-model-B`: **byte-identical command lines**.

So the CLI answers with its own default, the tab attributes the answer to the model that was picked
(the label is recorded from the pick, not from the CLI), and the spending line prices it as that
model. The picker is honoured by one of the two arrows the extension gained on 2026-09-09 and
silently ignored by the other. The root is the type: an interface with no place for the model is why
three adapters could all omit it and every test stay green.

## The change

1. **`ChatLaunch`** — `{ readonly resume: string; readonly model: string }` — replaces the bare string;
   `argv(launch: ChatLaunch)`. The compiler then asks every adapter the question the interface never
   asked.
2. **Each adapter emits its own vendor's flag**, spelled from that CLI's `--help` and written down
   beside the adapter: `claude --model`, `codex -m`, and antigravity's own. An empty model sends no
   flag — the CLI's default, exactly as today, which is also what a Team-server row with no model does.
3. **The launch takes the model the CONVERSATION picked**, not the row's default: `chatProcessFor`
   receives it from the session, which holds `thread.modelId` (`chatCommand.ts:757`), and passes it
   into `launchSpecFor`. A switch mid-conversation already starts a new session; it now starts one on
   the new model.
4. A tail, named and not built: where a CLI REPORTS the model it used (claude's envelope carries
   `modelUsage` keyed by model), the label could say what ran rather than what was asked. Worth its
   own change once this one makes the two agree in the ordinary case.

No growth surface.

## Test plan

| # | Test | Holds |
|---|---|---|
| 1 | `cliChatLaunch.test.ts` — *changing the model changes the command line, for every runtime*, the runtimes read from `ADAPTERS` rather than typed | the audit's repro, inverted, over a list the test cannot fall behind. RED today: identical |
| 2 | *an empty model sends no model flag* | the default is untouched |
| 3 | One test per adapter naming the flag it emits and where it sits in the argv | the spelling, per vendor |
| 4 | `chatProcess` / session tests: the model handed to the launch is the thread's, and a switch launches with the new one | step 3 |

## Definition of Done

- [x] Tests 1–4 written, watched fail for the real symptom, passing; a clean `tsc` read before the suite.
- [x] No adapter can be written without deciding what to do with the model — `argv` takes a
      `ChatLaunch`, so the compiler asks.
- [x] `module_extension.md` records the launch context and the tail.
