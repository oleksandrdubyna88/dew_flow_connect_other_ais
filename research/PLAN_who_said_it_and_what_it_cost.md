# PLAN — who said it, and what it cost

> Status: **IMPLEMENTED, 2026-09-10.** Both halves: the ledger and the log shipped 2026-09-09
> (another lane); the CAPTION shipped in PR #173 and the running total in PR #185.
> Kind: **feature**. Origin: [../todo/BUGS_2026-09-09.md](../todo/BUGS_2026-09-09.md), entry 17 and
> the cost item.
>
> ### What shipped differently
>
> **The caption's colour rules are built from every model the page will SHOW**, not from the picker's
> current list. The plan assumed the two were the same; they are not, and the difference is the case
> this feature exists for — switching models carries the whole thread across, so a conversation
> routinely displays a model the picker has moved on from. Built from the picker alone, every one of
> those answers carried a class with no rule behind it.
>
> **The caption is its own element.** It began as a `who` span inside the `who` row, which meant every
> rule written for the row landed on the label too. Nothing looked wrong; the next person to change
> the row's layout would have moved the text with it.
>
> **The running total lives beside the conversation**, not read back from the ledger. The ledger is a
> file this window shares with every other, and a tab asking it for its own total on every push would
> be reading a growing file to answer a question it already knows.
>
> ### What the total cannot say yet
>
> Only `claude` reports what it charged. `antigravity` omits cache entirely and `codex` reports a
> cumulative maximum, so for two of three the figure is worked out from tokens and wears the tilde —
> see [../todo/PLAN_usage_that_compares.md](../todo/PLAN_usage_that_compares.md), which is the plan
> that would make them comparable.

## The goal

1. **Every answer says which model gave it.** `ChatMessage` is `{ role, text }`; every answer is
   captioned `The other AI`. Switching the model mid-conversation is a shipped feature, so one
   tab routinely holds answers from two models with nothing distinguishing them.
2. **What a conversation costs is visible** — in the tab, live, next to the picker, where the
   decision to ask again is taken; and in the rounds log, as chat rows priced like rounds, beside
   the reviews. The Team server already separates the two on its side (`kind: chat`,
   `research/PLAN_the_server_knows_a_chat_from_a_review.md`); the local log has not caught up.

## Why the cost matters more here than in a review

A chat turn carries the whole conversation, so question five is billed for one through four —
`chatCommand.ts:503` says so out loud. That number is what a person would use to decide between
asking again and starting fresh, and it is invisible at exactly that moment.

## The one honest limitation

The vendors do not report comparable token counts — claude counts cache reads, antigravity omits
cache, codex reports a cumulative maximum ([PLAN_usage_that_compares.md](../todo/PLAN_usage_that_compares.md)).
So a chat's money is an ESTIMATE for at least two of the three, and it is marked as one with the
convention `usage.ts:230` already uses: `~$0.42` is what the tokens work out to, `$0.42` is what a
vendor actually charged. The tab and the log both keep the tilde.

## The shape

- `ChatMessage` gains `model?: { id, vendor, label }` and `cost?: { tokensIn, tokensOut,
  money, estimated }` on `model` messages — recorded from what ACTUALLY answered (the resolved
  row and model from [PLAN_provider_then_model.md](PLAN_provider_then_model.md)), never from what
  is configured now.
- The caption `The other AI` becomes the model's label in its vendor colour — for an ANSWER. What
  the person said is captioned `You`, chosen by role before anything looks at a model; a reviewer
  read an earlier wording as covering both and was right to ask (the edge
  [PLAN_an_answer_reads_like_a_document.md](PLAN_an_answer_reads_like_a_document.md) draws).
- A running total in the picker row: *this conversation: ~$0.13 · 9.2k tokens*, updated per turn.
- The ledger: every chat turn writes a usage entry with `kind: chat`, the model, tokens, money and
  the `estimated` flag; the log page reads them as rows with a *Conversation* kind, priced through
  the same `modelPrice` the rounds use (`panelProvider.ts`), filterable by kind.

## What it costs to keep — the ledger's growth budget

The family rule asks anything that GROWS to name its budget before the first write, and the gate's
code round asked for it by name. One record is **270 bytes** — measured, with a real model name, a
UUID conversation id and a plan filename in it, not estimated:

| turns a day | a year on disk |
|---|---|
| 50 (a busy person) | **4.7 MB** |
| 200 | 18.8 MB |
| 1000 (nobody types this) | 94 MB |

**Nothing retires it. The operator was asked with these numbers in front of them and ruled "keep it
for ever" on 2026-09-10** — so this is settled rather than merely unimplemented, which is the
difference that stops it being raised a fourth time. The file IS the history: `conversationTotal` sums
it, so deleting old lines would silently change what a past conversation is recorded to have cost, and
a ledger that quietly forgets is worse than one that is large. At the sizes above it is not large. It
is a plain file in the person's own data directory, named in the help; deleting it is theirs to do and
costs them only the history.

The two alternatives were put and refused, and are written down so a future reader does not have to
re-derive them:

| instead | what it costs |
|---|---|
| trim by age or by count | the file stops growing, and *"what did I spend last year"* is answered **wrongly** rather than not at all |
| roll trimmed lines up into one summary per conversation | truth and a bound, at the price of a second record type in the format and the code around it |

If a bound is ever wanted, the roll-up is the only version of it that does not lie.

What did need fixing is the cost of HOLDING it. The log page ticks every five seconds while it is
open and re-read and re-parsed the whole file on each one; `PanelProvider.chatLines` now caches the
parse and re-reads only when the file's size or mtime moves (`stat` is one syscall, a parse of a
year's records is not).

**An interrupted write costs its own line and nothing else.** Records are appended whole and
newline-terminated, so a torn tail is dropped by `parseChatUsage` exactly as `usage.ts` drops one from
the server's ledger. A write is not awaited by the turn that made it — a person's answer must not wait
on a disk — and `deactivate` drains the queue, which is the one moment that costs nobody anything.

## Build order

1. RED tests (below).
2. The ledger entry per chat turn (model + tokens + estimated), from each adapter's reported
   usage — one adapter at a time, with the limitation recorded per adapter.
3. `ChatMessage.model` and `.cost`; the caption; the running total.
4. The log rows and the kind filter.
5. GREEN; whole suite; a manual conversation across two models, screenshot recorded.

## Test plan

- `chatLedger.test.ts`: a turn records the model that answered, not the configured one; the
  estimated flag per vendor.
- `chatMessages.test.ts` / `chatPage.test.ts`: caption = the message's model label; a message
  without a model (a restored or legacy one) still renders with the old caption.
- `roundsLog.test.ts`: chat rows appear with kind *Conversation*, priced, tilde where estimated;
  the kind filter works.
- `usage.test.ts`: the total sums per conversation and keeps `estimated` if any turn is.

## Acceptance — one PR per plan, and the gate on both sides of it

This plan ships as **its own branch (`feat/who-said-it-and-what-it-cost`) and its own pull request**, and the PR is accepted
only when the whole ritual has run — not when the code works.

1. **Gate, before the first line of code.** `open` a coai session for this repository and the
   branch; `review_plan` with THIS file as the plan; `resolve` every finding (a rejection carries a
   reason); repeat until the verdict is `proceed`. `review_code` refuses without it.
2. **Tests first.** A RED test per defect, watched failing with the real symptom, then GREEN; every
   new behaviour with its happy path and the failure paths it introduces — `npm test` in
   `src_vs_code`, the whole suite, not the one file.
3. **Gate, after the code.** `review_code` with the scope = the *Definition of Done* below plus the
   goal, and the diff `main...feat/who-said-it-and-what-it-cost` — three dots; the two-dot moving-base trap is recorded in
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

**Specific to this plan:** no new setting or command; the chat help article gains the caption and the cost line, the rounds-log article gains the *Conversation* kind; `module_extension.md` gains the message shape and the ledger entry; CHANGELOG in the person's words with the tilde explained once more; coordinate the companion plan's status line in the same PR.

## Definition of Done

- [ ] Every answer carries the model that gave it, shown in its vendor colour.
- [ ] A running, tilde-marked total sits in the tab; chat rows sit in the log with their kind and cost.
- [ ] The ledger records what answered, never what is configured.
- [ ] The companion plan's steps 2–3 are either done here or their status line says exactly what remains.
- [ ] `npm test` green; the acceptance ritual complete; promoted on merge.

## Parallelism

Two halves with different owners. **The ledger and log half** (`chatLedger.ts`, `usage.ts`,
`roundsLog.ts`) has no conflict with the chat-page lane and can run beside it. **The tab half**
(`ChatMessage`, caption, total) touches `chatPage.ts` and queues behind
[PLAN_an_answer_reads_like_a_document.md](PLAN_an_answer_reads_like_a_document.md). Build the seam
(`ChatMessage.model`) first in the lane that gets there first; the other half reads it.
