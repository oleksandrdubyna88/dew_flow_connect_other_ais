# PLAN — an answer as it arrives

> Status: **plan only, nothing implemented yet — CONDITIONAL on a measurement.** Kind: **feature**
> (accepted 2026-09-09: *"если можно стримить — стримь; если нет — и так пойдёт"*). Scope: the
> session seam and its four implementations — `src_vs_code/src/chatSession.ts`,
> `claudeAdapter.ts`, `codexAdapter.ts`, `agyAdapter.ts`, `cliChatSession.ts`,
> `remoteChatSession.ts` — and the page's message region. Origin:
> [BUGS_2026-09-09.md](BUGS_2026-09-09.md), entry 19.
>
> Related docs: [module_extension.md](../research/module_extension.md),
> [PLAN_three_chat_adapters.md](../research/PLAN_three_chat_adapters.md),
> [PLAN_the_server_knows_a_chat_from_a_review.md](../research/PLAN_the_server_knows_a_chat_from_a_review.md).

## The goal — and the condition

A turn is 9.4 s measured, *"and eight of those seconds are silent"* (`chatPage.ts:25-27`). Where a
vendor CLI emits partial output, show it as it arrives. **Where it does not, `Thinking…` stays and
nothing is faked** — a spinner pretending to be progress is worse than an honest wait. The
measurement decides; it is not a preliminary, it is the plan's first deliverable.

## Phase 0 — measure, per adapter, before any design

For each of `claude`, `codex`, `agy`, in the exact mode `cliChatLaunch.ts` runs them (the flags,
the NDJSON/JSON output shape), record:

1. Does the CLI emit partial answer text at all — per token, per block, or only a final result?
2. What event or line carries it, and does it interleave with the events the adapter already
   parses (`init`, `result`, usage)?
3. On Windows through `cmd.exe`, does buffering delay it until the end anyway?

The three-adapter plan recorded that a written-down assumption about these CLIs was already half
wrong once and measurement corrected it — the same discipline here. Results go in this file as a
table. **A Team server is expected to answer "no"**: it returns a job over the wire, and streaming
there is a server change deployed by hand; that is an acceptable split and it is written down,
not worked around.

## Phase 1 — only where Phase 0 said yes

- `ChatSession.send(text, onWaiting?, onPartial?)` — a third optional callback; `Promise<TurnResult>`
  still resolves once with the whole answer (the final text is authoritative; partials are
  display only).
- The adapter parses the partial event and calls `onPartial(textSoFar)`.
- The page appends into a live `model` message under the entry-23 scroll rule (follow only if at
  the bottom); the Stop button ([PLAN_a_turn_nobody_can_stop.md](PLAN_a_turn_nobody_can_stop.md))
  stays available throughout; markdown is re-rendered on the final text only, partials show as
  plain text (a half-open fence must not flicker the layout).
- Where Phase 0 said no, the adapter simply never calls `onPartial` — no branch in the page.

## Build order

1. Phase 0 with a script beside `scripts/live-chat.mjs`; the table recorded here.
2. Decision line in this file: which adapters stream. If none — promote this plan as a record and
   stop.
3. RED tests; the seam; one adapter; the page; the others.
4. GREEN; whole suite; live check per streaming adapter, numbers recorded (time to first
   partial vs. time to result).

## Test plan

- `cliChatSession.test.ts`: a fake process emitting partial events → `onPartial` called with
  accumulating text, `send` resolves once with the final; a process emitting none → `onPartial`
  never called and the turn still completes.
- `chatPage.test.ts`: a partial push renders plain text into the live message; the final push
  replaces it with the rendered answer.
- `chatWiring.test.ts`: a partial arriving after a stop is ignored.

## Acceptance — one PR per plan, and the gate on both sides of it

This plan ships as **its own branch (`feat/an-answer-as-it-arrives`) and its own pull request**, and the PR is accepted
only when the whole ritual has run — not when the code works.

1. **Gate, before the first line of code.** `open` a coai session for this repository and the
   branch; `review_plan` with THIS file as the plan; `resolve` every finding (a rejection carries a
   reason); repeat until the verdict is `proceed`. `review_code` refuses without it.
2. **Tests first.** A RED test per defect, watched failing with the real symptom, then GREEN; every
   new behaviour with its happy path and the failure paths it introduces — `npm test` in
   `src_vs_code`, the whole suite, not the one file.
3. **Gate, after the code.** `review_code` with the scope = the *Definition of Done* below plus the
   goal, and the diff `main...feat/an-answer-as-it-arrives` — three dots; the two-dot moving-base trap is recorded in
   `todo/PLAN_the_gate_diffs_from_a_moving_base.md`. `resolve`; repeat until `proceed`.
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

**Specific to this plan:** the Phase 0 table is part of the PR whatever it says; help sentence under the chat article naming which models stream and that a Team server does not; `module_extension.md` gains `onPartial`; CHANGELOG in the person's words; no manifest change.

## Definition of Done

- [ ] Phase 0's table is in this file with numbers per adapter.
- [ ] Where streaming is possible, partial text appears as it arrives and the final answer replaces it rendered.
- [ ] Where it is not, nothing changed and the help says so.
- [ ] Stop works mid-stream; partials after a stop are ignored.
- [ ] **The scroll rule survives progressive rendering.** Inherited from the composer plan's story-2
      plan round (gemini, Major), where it was rejected as not-yet-reachable: today the page renders
      plain text with no image, embed or async highlighter, so one deferred scroll per push lands on
      the final height. A streamed answer breaks that — the height keeps growing after the follow has
      run, leaving a reader who WAS at the bottom short of it. Each partial must go through the same
      `scheduleFollow()` the pushes use (`src_vs_code/src/chatPage.ts:304`), never a scrollTop of its own, and a test
      must prove a reader at the bottom is still at the bottom when the last token lands.
- [ ] `npm test` green; the acceptance ritual complete; promoted on merge.

## Parallelism

**Phase 0 has no conflicts at all** — a script and a table — and can run in any lane at any time.
Phase 1 conflicts with [PLAN_a_turn_nobody_can_stop.md](PLAN_a_turn_nobody_can_stop.md) on `send`'s
signature (stop goes first) and with the chat-page lane on the message region (queues behind
[PLAN_an_answer_reads_like_a_document.md](PLAN_an_answer_reads_like_a_document.md)).
