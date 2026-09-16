# PLAN — a chat row can be cleared from the spending chart, exactly as a reviewer's can

> Status: **IMPLEMENTED, 2026-09-16.** Kind: **feature** (one button, one
> watermark). Scope: `src_vs_code/src/chatSpendRows.ts` (one pure filter),
> `src_vs_code/src/panelView.ts` (the ✕ on the chat card), `src_vs_code/src/panelProvider.ts` (the
> handler and the watermark), `src_vs_code/src/roundsLogMessages.ts` and
> `src_vs_code/src/roundsLogPanel.ts` (the message), and their tests. Origin:
> [issue #298](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/298) — the
> operator: *"just as in Reviewers, it should be possible to delete on chat items too, with the same
> logic (deleted; if it appears again it continues from a clean slate)."*
>
> Related docs: [module_extension.md](module_extension.md), [module_tests.md](module_tests.md).
>
> ## Deviations — what shipped differently, and why
>
> 1. **The plan's provider resolution was only two thirds of the rule, and the gate caught it.** It
>    said "apply `vendorOf` before the key is compared". The real rule in `chatSpend` is three-way:
>    what the LINE wrote down wins over what the preset list says today, because a preset is a row
>    somebody edits and re-resolving old lines would move a year of history to another vendor in an
>    append-only ledger (CodeRabbit, PR #209). A filter using `vendorOf` alone would have missed
>    every line carrying its own `vendor` — which, since #209, is every recent one. The rule is now
>    ONE exported function, `chatRowProvider`, used by the chart and the filter, so they cannot
>    drift. Proved by reverting it: the chart's own pre-existing test went red too.
> 2. **The page dropped the field, and only running it found that.** The button carries `data-model`,
>    and the rounds-log page's delegated `[data-command]` handler posted only `id` — so every
>    assertion over the rendered markup was green while the control forgot nothing. The handler now
>    sends the model, and `forgetAChatRow.test.ts` captures the listener the page registers and calls
>    it. This is the one place in this series where the gate's standing "run the page" finding was
>    both available and RIGHT: the rounds-log page has a script with a branch in it, unlike the
>    sidebar's static markup, and it shipped a real defect that markup tests could not see.
> 3. **`forgetChat` needed a case in the sidebar's switch although the sidebar never sends it.**
>    `PANEL_COMMANDS` is the shared vocabulary for every `data-command` this product emits, and the
>    provider switches over it with a `never` check — so declaring the command demanded a case even
>    though the rounds-log page is the only surface that posts it. It is an empty case with a comment
>    saying so, which is better than a second vocabulary.
> 4. **`LogPageMessage` gained a `model` field.** The plan did not say where the model would ride;
>    the decoder reads it only for the one command that needs it, and every other command ignores it.
>
> **Checked, and not done:** the modal itself, and whether a mark survives an extension restart.
> Both need a VS Code host, which this suite does not have — named in `module_tests.md` rather than
> implied.

## The symptom

The spending tab draws two ledgers side by side — `panelView.ts:2037` is
`<section class="ledger-half"><h3 class="ledger">Reviewers</h3>` and `:2040` is the same for
**Chat**.

Every card in the **Reviewers** half carries a ✕ (`panelView.ts:1841`, `data-command="forgetUsage"`)
whose own tooltip is almost the operator's sentence: *"Nothing is deleted from the ledger — the row
simply stops counting what is already there, and comes back the next time this vendor runs."*

Every card in the **Chat** half carries nothing. `chatSpendCard` (`panelView.ts:1944-1958`) renders a
head, a bar and figures, and there is no button anywhere in it. So a chat row that has served its
purpose — a model tried once, a preset since renamed — sits in the chart for ever, and the half of
the page beside it has had the answer since the spending tab shipped.

## What "the same logic" already means here

**A watermark, never a rewrite.** `forgetUsage` (`panelProvider.ts:943-961`) writes
`coai.usageForgottenBefore[provider] = <ISO instant>` into `globalState` and `remembered()`
(`panelProvider.ts:971-979`) filters on READ: an entry at or before the mark is not counted,
everything after it is. The docblock says why, and the reason transfers exactly: the server appends
to the ledger while the panel is open, so filtering the file and writing it back would race a round
finishing mid-write — and a spending record is the kind of file that must not lose rows to a UI
action.

**That is also what makes "a clean slate" free.** There is no state to reset; the row returns on its
own the next time something records a later timestamp.

## The one thing that differs: what a row IS

A reviewer row is keyed by **provider**. A chat row is keyed by **provider AND model** —
`ChatSpendRow` is `{ provider, model, … }` (`chatSpendRows.ts:64-67`) and rows are grouped by
`keyOf(provider, model)`, a NUL-joined pair (`chatSpendRows.ts:118-120`).

So the chat watermark is keyed by that pair, not by the vendor. Forgetting *codex · gpt-5.4* must not
take *codex · gpt-5.5* with it — they are two rows on the page and a person pressing ✕ on one of them
is pointing at one of them.

Both ledgers carry what this needs: `ChatTurnRecord` is `{ utc, provider, model, … }`
(`chatUsage.ts:38-44`) and `ChatDoorRecord` is `{ utc, door, provider, model, … }`
(`chatDoors.ts:61-73`).

**The row a line belongs in is decided by a THREE-WAY rule, and the key must use all of it.** The
first draft of this paragraph said only "apply `vendorOf`", and that is two thirds of it — the plan
round caught the missing third, and it is the one that matters most:

1. what the LINE wrote down (`record.vendor`) wins, if it has one;
2. otherwise `vendorOf` resolves the recorded PRESET id to a vendor;
3. and the result is paired with the model.

Step 1 exists because a preset is a row somebody edits: re-resolving old lines through the list as it
is today would move a year of history to another vendor in a ledger that is supposed to be
append-only (CodeRabbit, PR #209). A watermark keyed on `vendorOf(recorded)` alone would therefore
have missed **every line carrying its own `vendor`** — which, since that change, is every recent one,
and the ✕ would have silently forgotten nothing.

So the rule is extracted as `chatRowProvider` and used by BOTH the chart and the filter. One
function, not two copies that agree today.

## What must be true when it is done

1. Every card in the **Chat** half of the spending tab carries a ✕, in the same place and with the
   same shape as the Reviewers half's.
2. Pressing it asks first — modal, as `forgetUsage` does, because the number it clears is the only
   record of what a month cost and it is not reversible from the panel.
3. Confirming stops that row counting what is already recorded, and **nothing on disk changes**.
4. The row **comes back on its own** the next time that vendor-and-model pair records anything.
5. It clears exactly one row: another model of the same vendor, and the same model under another
   vendor, are untouched.
6. The Reviewers half, `usage.jsonl`, and `coai.usageForgottenBefore` are untouched — this is a
   second watermark beside the first, not a change to it.
7. A person who has forgotten nothing sees exactly what they see now.

## The design

**`chatSpendRows.ts`** — one exported pure function, tested by calling it:

```ts
/** The key a chat row is forgotten under: the pair the page groups by. */
export function chatForgetKey(provider: string, model: string): string

/** The two ledgers minus what has been forgotten, applied on READ so no file is touched. */
export function rememberedChat<T extends { utc: string; provider: string; model: string }>(
  records: readonly T[],
  marks: Readonly<Record<string, string>>,
  vendorOf: ChatVendorOf,
): readonly T[]
```

Generic over the record, because turns and doors differ in everything except the three fields this
reads — one filter for both rather than two that can drift.

**`panelView.ts`** — `chatSpendCard` gains the ✕, byte-for-byte the shape of the reviewer one
(`panelView.ts:1841-1843`): `class="link forget"`, a `title` saying what it does and does not do, an
`aria-label`, and the glyph. It carries `data-id` **and** a `data-model`, because one id cannot name
a pair. A row whose provider is empty — the `nothing chosen` row — gets **no** button: there is no
pair to forget, and a ✕ that cannot act is worse than none.

**`roundsLogMessages.ts` / `roundsLogPanel.ts`** — the command decodes to
`{ kind: 'forgetChat', provider, model }`, beside the existing `forgetUsage` → `{ kind: 'forget' }`,
and the panel gains an `onForgetChat` hook, wired in `extension.ts` exactly as `onForget` is.

**`panelProvider.ts`** — `forgetChatUsage(provider, model)`: the same modal, then
`coai.chatUsageForgottenBefore[chatForgetKey(...)] = now`, then `render()`. The ledgers are filtered
where they are assembled (`panelProvider.ts:584-586`), which is the same place `remembered()` is
applied to the reviewers' one.

## Growth budget

One `globalState` record, `coai.chatUsageForgottenBefore`: a key of `<provider>\0<model>` and an ISO
instant.

- **Projected size:** one entry per row a person has pressed ✕ on. The chart shows the rows that
  exist, so the ceiling is the number of vendor-and-model pairs ever used — realistically under 50,
  at ~60 bytes each, **under 3 KB**. It is written only by a deliberate modal confirmation.
- **Who retires it:** nobody, and that is deliberate — the same decision `coai.usageForgottenBefore`
  already made. A mark must outlive the ledger lines it hides or the row would reappear with its old
  figures. It is bounded by the pairs that exist rather than by time, so it cannot grow without a
  person adding models.
- **When interrupted:** a `globalState.update` either lands or does not; there is no in-flight state
  and nothing to sweep.

## What this does NOT do

- It does not touch the ledgers on disk, the Reviewers half, or the existing watermark.
- It does not add a "forget everything" control. One row at a time is what was asked for.
- It does not delete a CONVERSATION. Conversations already have a trash button and a keybinding in
  the conversation picker (`conversationPickerCommand.ts`); this is the spending chart, which is what
  the issue's screenshot shows and what the "comes back from a clean slate" sentence describes.

## Build order

1. **RED** — the tests below, watched failing.
2. `chatForgetKey` and `rememberedChat` in `chatSpendRows.ts`.
3. The ✕ in `chatSpendCard`; the command in `PANEL_COMMANDS`; the decode and the hook.
4. `forgetChatUsage` and the filter at the assembly site.
5. **GREEN** — the same tests, then the whole suite.
6. `research/module_extension.md` and `research/module_tests.md`; `CHANGELOG.md` under 0.47.0.
   All BEFORE the `git mv` of the promotion.
7. `plan-lifecycle.mjs` and `pin-check.mjs`, before the `git mv` and again before the final commit.

## Test plan

1. **`rememberedChat` is tested by calling it**: a record at the mark and one before it are dropped,
   one after it is kept. The boundary is `>` exactly as `remembered()` uses it — a record AT the
   instant is forgotten.
2. `'forgetting one model leaves the vendor's other models alone'` — three records over two models of
   one vendor and one model of another; one mark; exactly the right records survive. This is the
   assertion that fails if the key collapses to the provider.
3. `'the preset a line recorded is resolved before the mark is compared'` — a record whose raw
   provider is a preset id resolving to another vendor is matched by the mark for the RESOLVED pair.
   Without this the filter silently keeps everything.
4. `'a chat card carries the same control the reviewer card does'` — the rendered chat half contains
   `data-command="forgetChat"` with the row's provider AND model, and the reviewers half still
   contains `forgetUsage`. Both, so this cannot pass by having moved the reviewer button.
5. `'the row that has nothing chosen has nothing to forget'` — no button on the empty-provider row.
6. `'the command is one the provider must handle'` — `forgetChat` is in `PANEL_COMMANDS`, and the
   decoder maps it to `{ kind: 'forgetChat', provider, model }`. The exhaustiveness check in the
   provider makes a command with no case a compile error, which is the other half.
7. **Teeth, by reverting**: drop the filter at the assembly site and watch 1–3 fail; restore, drop
   the `data-model` and watch 4 fail.
8. Whole suite: `cd src_vs_code && npm test`. Baseline on this branch's base: 2971 tests, 2970 pass,
   1 skipped, 0 fail.

## Definition of Done

- [x] The behaviour tests were watched RED by reverting each production line separately — collapsing the key, dropping the written-down vendor, and removing the page’s `model` field each reddened a different test.
- [x] Every chat card has the ✕; the `nothing chosen` row does not.
- [x] It asks first, clears exactly one vendor-and-model pair, and touches no file.
- [x] The row returns on its own when that pair records something later — the `>` boundary is asserted at the instant itself.
- [x] The Reviewers half and its watermark are untouched, and a test asserts both.
- [x] Whole suite green: 2972 tests, 2971 pass, 1 skipped, 0 fail.
- [x] `research/module_extension.md`, `research/module_tests.md` and `CHANGELOG.md` updated.
- [x] `plan-lifecycle.mjs` and `pin-check.mjs` clean.
- [x] Indexed — the `research/README.md` row on promotion.

## Acceptance — the gate on both sides

1. `review_plan` over this document before the first line of code; `resolve` every finding.
2. RED test per defect, then the whole suite — never one file alone.
3. **Rebase onto `origin/main` immediately before the code round**, then `review_code` with this
   document as the scope and `baseRef` `origin/main`; `resolve` every finding.
4. Pull request only after the code round is resolved; at most three open on this repository.
5. Five minutes after opening: read CI and the automated reviewer, verify each comment against the
   code and the rules, fix or answer, resolve the threads, merge by rebase.
6. Promotion inverts the checklist's order — every edit while the file is still in `todo/`, the
   `git mv` LAST, `git add` the destination, one commit — with the DoD ticked and the deviations
   recorded.

Specific to this plan: **a new command means `helpCoverage.test.ts` has something to demand.**
`forgetChat` is a panel command, not a manifest command, so the manifest scan does not see it — but
the help catalogue is checked for every `coai.*` setting, and `coai.chatUsageForgottenBefore` is
`globalState`, not configuration, so neither gate fires. Verified rather than assumed, and if either
does fire the article is written in the same commit.
