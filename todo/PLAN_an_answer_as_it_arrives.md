# PLAN — an answer as it arrives

> Status: **PHASE 0 IS DONE (measured 2026-09-09) — Phase 1 is not built, and the measurement says
> only one of three vendors could use it.** `agy` streams real deltas on `step_update`; `claude` and
> `codex` emit nothing before their final answer. The window scales with answer length — 6 % of a
> short turn, 44 % of a long one — and the plan's premise that eight silent seconds were recoverable
> is REFUTED: most of that silence is the model thinking, before any vendor has a token to give.
> Phase 1 stays unbuilt on that evidence; the table and the harness are below and committed.
> Kind: **feature**
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

### Phase 0 RESULTS — measured 2026-09-09

Harness: [`src_vs_code/scripts/measure-stream.mjs`](../src_vs_code/scripts/measure-stream.mjs), run
on Windows 11, node v24.18.0, against the real signed-in CLIs. Three measured runs per arm after one
warm-up that is recorded and not counted. The launch spec is **imported** from `launchSpecFor`, not
retyped, so this cannot measure a mode the product does not run. Raw transcripts are written to
`src_vs_code/measurements/` and git-ignored — the table is what belongs here.

The pinned prompt is mechanical and synthetic (*"write the numbers from 1 to N, one per line"*), so
the answer can be checked without judging prose and nothing from this machine reaches a file. `N` is
the second variable and it turned out to be the one that mattered.

**Asking for 40 lines (a 110-character answer):**

| vendor | arm | ok | turn (ms) | deltas | streams? | stream window | delta field | usage event | usage at |
|---|---|---|---|---|---|---|---|---|---|
| `claude` | direct *(shipped)* | 3/3 | 3987 | 1 | no — whole answer, once, early | — | `message.content[].text` | `assistant`, `result.success` | 3366 ms |
| `claude` | `cmd.exe` | 3/3 | 3822 | 1 | no — whole answer, once, early | — | `message.content[].text` | `assistant`, `result.success` | 3186 ms |
| `codex` | direct | **impossible** | — | — | — | — | — | — | node refuses to spawn a `.cmd` without a shell |
| `codex` | `cmd.exe` *(shipped)* | 3/3 | 6537 | 0 | no | — | — | `turn.completed` | 6036 ms |
| `agy` | direct *(shipped)* | 3/3 | 4548 | 6 | **yes** | 253 ms | **`step_update.text_delta`** | `step_update`, `result` | 4113 ms |
| `agy` | `cmd.exe` | 3/3 | 4207 | 5 | **yes** | 178 ms | **`step_update.text_delta`** | `step_update`, `result` | 3882 ms |

**Asking `agy` for 400 lines**, because a 200 ms window on a 4.5 s turn is worth nothing and the
question is whether it GROWS:

| vendor | arm | ok | turn (ms) | deltas | streams? | stream window |
|---|---|---|---|---|---|---|
| `agy` | direct *(shipped)* | 3/3 | 8978 | 82 | **yes** | **3932 ms** |
| `agy` | `cmd.exe` | 3/3 | 11575 | 76 | **yes** | **3730 ms** |

#### What that says, question by question

**1. Does the CLI emit partial answer text at all?**

- **`agy`: YES, and genuinely.** The field is **`step_update.text_delta`** — the vendor's own name for
  it — on `step_update` events with `status: ACTIVE` and `step: agent_response`. It carries fragments
  (`"1\n2\n…8\n"`, then `"9\n10\n…16\n1"`, then `"7\n18\n"`) which tile the final answer exactly, in
  arrival order.

  **How a delta is identified matters more than the result, so it is written down.** The first version
  of this harness scraped every nested string out of every event and accepted any that appeared
  *somewhere* in the answer. All three vendors' reviewers found the same hole independently: for a
  numeric answer, unrelated metadata like `12` and `345` can pass a containment test and
  "reconstruct" an answer nobody streamed. A measurement that can say YES when the truth is NO is
  worse than no measurement. A delta now has to satisfy three conditions at once — it must match at
  an **advancing offset** (exactly what comes next, from a cursor that never goes backwards), the
  pieces must **tile the whole answer** with nothing left over, and they must all come from **one JSON
  path**. That is what produced the field name above, which is the thing Phase 1 actually needs.

  The strict rule then produced a false negative of its own, in the opposite direction, and it is
  worth recording: excluding single-character fragments stalled the cursor the first time `agy` split
  a token across a boundary (`"…16\n1"` then `"7\n18\n"`), and every later fragment failed to match, so
  a streaming vendor was reported as not streaming on the 400-line probe. The minimum length was never
  what made the test safe — the tiling and the single path are.
- **`claude`: NO.** Four events. The `assistant` event carries the COMPLETE message in ONE piece on
  `message.content[].text`, a few milliseconds before `result.success`. The harness counts that as a
  single "delta" covering the whole answer and refuses to call it streaming, which is the distinction
  that matters: it is an early final, and showing it would advance the answer by that handful of
  milliseconds.
- **`codex`: NO.** Four events; the answer exists only in `item.completed.agent_message`.

**2. What event carries it, and does it interleave with what the adapter already parses?**

`agy`'s deltas ride `step_update`, which `agyAdapter.classify` currently returns as `NOTHING` — so
the event is already arriving and already being discarded, and Phase 1 is a new `AdapterEvent` kind
rather than a change to any existing branch. They interleave cleanly: `init`, then `step_update DONE
user_input`, then the ACTIVE deltas, then `step_update DONE agent_response`, then `result`.

**3. On Windows through `cmd.exe`, does buffering delay it until the end anyway?**

**No — refuted.** For both vendors that resolve to a real `.exe` the two arms are within noise of each
other (`claude` 3987 against 3822 ms, `agy` 4548 against 4207 ms with windows of 253 against 178 ms —
the shell arm was FASTER in both, which is how you know you are reading noise), and `agy` streams
identically through the shell, from the same field. The shell is not what makes a turn quiet.

`codex` cannot be compared: it installs as `codex.cmd`, and node has refused to spawn a `.cmd`
without a shell since the fix for CVE-2024-27980. That is a result rather than a gap — the product
has no direct arm available for it either.

#### The finding that decides it: the window SCALES with the answer

At 110 characters `agy`'s deltas occupy the last **253 ms of a 4548 ms turn — 6 %**, which would spare
a person nothing. At roughly 1.5 kB the same vendor streams for **3932 ms of an 8978 ms turn — 44 %**,
across 82 deltas. So the silence is the model THINKING, not output being withheld, and the streaming
half only becomes worth watching once an answer is long — which a real chat answer is, and the
40-line probe is not.

**This also corrects the plan's own premise.** *"Eight of those nine seconds are silent"* is true, but
they are not silent because anything is being held back; the first four to six seconds are spent
before any vendor has a token to give. No implementation can recover those.

### The decision

**Phase 1 is worth building for `agy` only, and it is NOT urgent.** One of three local vendors
streams; for it the gain is real on long answers (44 % of the wait) and nil on short ones; the other
two have nothing to stream and must keep an honest `Thinking…`. A Team server, as expected, answers
"no" — it returns a job over the wire.

Recorded rather than acted on now, because the seam it extends has just changed underneath it (see
*Phase 1* below) and because one vendor at 44 %-on-long-answers is a smaller prize than the plan
assumed when it was written. The evidence is here and the harness is committed, so whoever picks it
up starts from numbers rather than from a guess.

### What Phase 0 also recorded for a companion plan

The harness reports where each vendor puts its token counts, because it was reading the same streams
and [PLAN_who_said_it_and_what_it_cost.md](PLAN_who_said_it_and_what_it_cost.md) needs exactly that:

| vendor | usage arrives on | when |
|---|---|---|
| `claude` | `assistant` **and** `result.success` | 3366 ms of a 3987 ms turn |
| `codex` | `turn.completed` | 6036 ms of a 6537 ms turn |
| `agy` | `step_update` (the `DONE agent_response` one) **and** `result` | 4113 ms of a 4548 ms turn |

Every one of them is currently discarded — `codexAdapter.ts`'s own header comment already said so
about `turn.completed`, and this confirms it for the other two. Note that all three publish usage on
or before the terminal event, so a per-turn ledger needs no extra round trip.

## Phase 1 — only where Phase 0 said yes (so: `agy` only, and not yet)

Six constraints the plan round put on this half before it is written. They are recorded here rather
than solved now, because Phase 1 is not being built:

1. **The seam signature is no longer free.** `stop()` landed on `ChatSession` first
   ([PLAN_a_turn_nobody_can_stop.md](PLAN_a_turn_nobody_can_stop.md)), and a third POSITIONAL
   callback after `onWaiting` is the shape most likely to be got wrong by a caller. Decide between an
   options object and a positional argument **before** the first line, and say which. (codex.)
2. **A partial belongs to a TURN, not to a session.** The stop work already learned this the hard
   way: a callback from a superseded turn must be dropped, not delivered into the next turn's
   message. Reuse the turn identity that exists rather than inventing a second one. (codex.)
3. **Only text that reconstructs the answer may be shown.** `agy` also emits `step_update` events for
   tool activity and status; showing those as the answer would put the model's scratchpad in the
   tab. The harness's rebuild test is the rule to port. (codex.)
4. **The scroll rule must survive progressive rendering** — see the Definition of Done below.
5. **The plain-to-rendered switch moves the geometry.** Partials render as plain text and markdown is
   applied to the final answer only, deliberately, so a half-open fence does not flicker. A reviewer
   asked for an incremental markdown renderer instead; that was declined as a much larger machine
   whose only job is to make that flicker acceptable, and because the container's reserved geometry
   belongs to the renderer plan
   ([PLAN_an_answer_reads_like_a_document.md](PLAN_an_answer_reads_like_a_document.md)), not to two
   designs at once. (gemini, rejected with reasons.)
6. **A failure is not evidence of absence** — if a re-measurement is ever run, a hung or rate-limited
   CLI must be reported as FAILED, never as "does not stream". The harness already enforces this.

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

## Definition of Done — PHASE 0 (this pull request)

- [x] A harness that drives the REAL CLIs, importing the launch spec rather than retyping it.
- [x] A failed, hung or unspawnable run is reported as such and never counted as "does not stream".
- [x] The prompt is pinned, synthetic, and long enough that one chunk cannot hold the answer; the
      answer LENGTH is varied, because it turned out to decide the result.
- [x] Arrival is stamped per stdout CHUNK, before line splitting, so buffering is visible.
- [x] Direct spawn compared against `cmd.exe` wherever the resolved file allows both.
- [x] Answer deltas distinguished from reasoning, tool and status text by reconstruction.
- [x] Three measured runs per arm after a warm-up; raw transcripts kept out of git.
- [x] The table is in this file, with the per-vendor usage events a companion plan needs.
- [x] A decision line saying which adapters stream, and whether Phase 1 is worth building.

## Definition of Done — PHASE 1 (not this pull request)

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
