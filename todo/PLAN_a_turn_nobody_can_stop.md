# PLAN — a turn nobody can stop

> Status: **the SEAM half is implemented (2026-09-09, branch `fix/a-turn-nobody-can-stop-seam`); the
> PAGE half — the Stop button in `chatStatusHtml` — is not yet built.** `ChatSession.stop()` exists on
> the seam and in both implementations, the three session shapes behave as this plan describes, and
> `chatCommand.ts` routes a `stop` message to the right thread and refuses one naming a turn that is
> no longer running. What remains is the button that posts it, which belongs to the chat-page lane
> because that lane owns `chatPage.ts`. Kind: **bug** (accepted 2026-09-09). Scope: the
> chat session seam and the page — `src_vs_code/src/chatSession.ts`, `cliChatSession.ts`,
> `remoteChatSession.ts`, `chatCommand.ts`, `chatPage.ts` (the thinking line only). Origin:
> [BUGS_2026-09-09.md](BUGS_2026-09-09.md), entry 15.
>
> Related docs: [module_extension.md](../research/module_extension.md),
> [PLAN_three_chat_adapters.md](../research/PLAN_three_chat_adapters.md),
> [PLAN_the_server_knows_a_chat_from_a_review.md](../research/PLAN_the_server_knows_a_chat_from_a_review.md).

## The symptom

The composer is locked while a turn runs — deliberately, and the file argues for it
(`chatPage.ts:25-27`): two turns down one NDJSON pipe would interleave. But there is **no way to
cancel**. `ChatSession` offers `send` and `dispose` (`chatSession.ts:28-41`); the page has no stop
control. A measured turn is 9.4 s and a long one is much more, so a question sent by accident, or
one you see is wrong the moment it leaves, is simply waited out — and billed.

## What already exists

- `dispose()` on a CLI session (`cliChatSession.ts:148`) kills the process; `chatOrphans.ts` exists
  so that nothing survives a force-kill. The kill is built. What is missing is a button and a rule
  for what the conversation looks like afterwards.
- `TurnResult` already has the failure shape `{ ok: false, failure }` and the page renders it in
  `#failure`. A stopped turn is a turn that ends `ok: false` with *you stopped it*.
- The model-switch handover (`carriedTurn`, `chatPrompt.ts:172`) already re-opens a conversation
  in a fresh process with the transcript carried. **This matters because of the trap below.**

## The trap — for two of the three CLIs, stopping the turn stops the conversation

`claude` and `agy` hold the conversation IN the process (`chatModels.ts:15-21`,
`captionOf`: *keeps the conversation*). Killing the process to stop a turn kills the memory with
it; the next question would go to a process that never heard anything. `codex` resumes a stored
thread by id, so for it a kill is cheap. And a Team server holds no memory at all — there, "stop"
is "stop polling and tell the server" (the server already drops a job nobody polls, on two clocks;
a stop is the client saying so at once rather than after three minutes).

So `stop` is not `dispose` — it is: end this turn now, and make sure the NEXT turn still works.

## The shape

- `ChatSession` gains `stop(): void` — ends the in-flight turn; `send` resolves `ok: false`,
  `failure: "stopped"`. Per implementation:
  - `cliChatSession`: kill the process (the existing dispose path) **and mark the thread as
    needing a carry** — the next `send` opens a fresh process and hands it the transcript through
    `carriedTurn`, exactly as a model switch does today. The person sees one sentence:
    *stopped; the next question re-sends the conversation*.
  - `codex`: kill; the next turn resumes by thread id as it already does.
  - `remoteChatSession`: stop polling; call the server's cancel if it has one (check
    `teamServerApi.ts`; if not, the drop-on-no-poll rule covers it and the plan says so).
- The page: a **Stop** button beside `Thinking…` (`chatStatusHtml`, `chatPage.ts:159`), posting
  `{ type: 'command', command: 'stop' }` over the existing bridge. Disabled the instant it is
  pressed, so a double press cannot stop the NEXT turn.
- The stopped turn is rendered under the entry-23 scroll rule like any other outcome.

## Build order

1. RED tests (below).
2. `stop()` on the seam and the three implementations; the carry mark on the CLI thread.
3. The button and the bridge message; `chatCommand.ts` routes it to the thread's session.
4. GREEN; whole suite; a live check (`npm run test:live` is the precedent) — stop a real turn on
   each local CLI and ask again; the answer must know what was said before the stop.

## Test plan

- `cliChatSession.test.ts`: `StopDuringATurn_ResolvesTheTurnAsStopped`;
  `TheTurnAfterAStop_CarriesTheTranscript` (the fake process is given the carried turn).
- `chatPage.test.ts`: the Stop button exists exactly while `running`; it is absent otherwise.
- `chatWiring.test.ts`: a `stop` command reaches the right thread and only that thread.
- Remote: `StopStopsPolling_AndDoesNotResurrectTheThinkingLine` (the guard at
  `chatCommand.ts:228-231` already handles a poll finishing late — reuse it).

## Acceptance — one PR per plan, and the gate on both sides of it

This plan ships as **its own branch (`fix/a-turn-nobody-can-stop`) and its own pull request**, and the PR is accepted
only when the whole ritual has run — not when the code works.

1. **Gate, before the first line of code.** `open` a coai session for this repository and the
   branch; `review_plan` with THIS file as the plan; `resolve` every finding (a rejection carries a
   reason); repeat until the verdict is `proceed`. `review_code` refuses without it.
2. **Tests first.** A RED test per defect, watched failing with the real symptom, then GREEN; every
   new behaviour with its happy path and the failure paths it introduces — `npm test` in
   `src_vs_code`, the whole suite, not the one file.
3. **Gate, after the code.** `review_code` with the scope = the *Definition of Done* below plus the
   symptom, and the diff `main...fix/a-turn-nobody-can-stop` — three dots; the two-dot moving-base trap is recorded in
   `research/PLAN_the_gate_diffs_from_a_moving_base.md`. `resolve`; repeat until `proceed`.
4. **Documentation.** `research/module_extension.md` says what the code now does;
   `research/architecture.md` if a cross-module seam moved.
5. **Help.** Every new command and setting has an article in `helpContent.ts` —
   `helpCoverage.test.ts` fails the build otherwise; a lagging translation is marked as such.
6. **README and CHANGELOG.** `src_vs_code/README.md` if what a person sees changed;
   `src_vs_code/CHANGELOG.md` in the prose the file already uses — the sentence a person reads,
   not the commit subject.
7. **Manifest.** `package.json` contributions (settings, commands, keybindings, menus), and the
   version the release line expects (see the `chore(release)` history).
8. **Family checks.** `node .claude/rules/shared/tools/plan-lifecycle.mjs` and `pin-check.mjs`
   clean.
9. **Promote.** On merge, `/promote-plan` this file to `research/` with `IMPLEMENTED <date>` and
   every deviation recorded — what shipped differently is the most valuable line of the record.

**Specific to this plan:** a help article line under the chat article (the Stop button and what a stop costs a CLI that keeps the conversation); `module_extension.md`'s session seam gains `stop`; CHANGELOG in the person's words; no manifest change unless a keybinding for Stop is added (Escape is the natural one — if added, it is a `keybindings` contribution and the help names it).

## Definition of Done

- [ ] `ChatSession.stop()` exists on the seam and in all implementations; a stopped turn ends `ok: false` with a sentence.
- [ ] After a stop, the next question to `claude`/`agy` still knows the conversation (carried), and the person was told it would be re-sent.
- [ ] The Stop button appears only while a turn runs and cannot stop the following turn.
- [ ] Live check recorded: each local CLI stopped and asked again.
- [ ] `npm test` green; the acceptance ritual complete; promoted on merge.

## Parallelism

Owns the session files (`chatSession.ts`, `cliChatSession.ts`, `remoteChatSession.ts`) and the
`stop` route in `chatCommand.ts`; **touches `chatPage.ts` in one function (`chatStatusHtml`)**, so
it queues behind [PLAN_the_composer_stays_put.md](../research/PLAN_the_composer_stays_put.md) in the chat-page
lane — or lands its seam half first and its page half after. Conflicts with
[PLAN_an_answer_as_it_arrives.md](PLAN_an_answer_as_it_arrives.md) on `send`'s signature: this one
goes first, streaming extends it.
