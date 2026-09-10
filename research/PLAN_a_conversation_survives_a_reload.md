# PLAN — a conversation survives a window reload

> Status: **IMPLEMENTED, 2026-09-09.** Kind: **bug** (accepted 2026-09-09: *"не критично; если это
> легко — делай"*). Shipped in pull request #164. Scope as built: a new `chatTabs.ts`,
> `chatPanel.ts`, `chatCommand.ts`, `extension.ts`, one line in `chatPage.ts`, one `workspaceState`
> key. Origin: [BUGS_2026-09-09.md](../todo/BUGS_2026-09-09.md), entry 16.
>
> Related docs: [module_extension.md](module_extension.md) — *A conversation survives a window
> reload* — and [PLAN_chat_with_other_ais.md](PLAN_chat_with_other_ais.md).

## What shipped differently — and the one thing the operator had to decide

**The plan forbade touching `chatPage.ts`, and that constraint was the whole difficulty.** With
nothing calling `setState`, the only identity VS Code preserves is the tab's TITLE — and this
repository already knows what a title-keyed registry does, because `chatPanels.ts` exists to avoid
it. The plan round put six findings on that design and every one was right: two namesake tabs would
overwrite each other **on the way in**, so the collision could never be detected on the way out; a
`deserializeWebviewPanel` call cannot know whether another panel of the same name is still coming;
and `workspaceState` is shared by two windows on one folder.

So the cost was brought back rather than paid, which is what this plan's stop condition asks for —
and the operator chose the one-line touch. The conversation carries its **own id**: minted in
`newConversation`, rendered into the page, handed back through `vscode.setState`, returned to the
serializer. Four Blocking findings dissolved with it, and the store became a straightforward
per-conversation record with no namesake rule and no manifest.

**Nothing is deleted when a tab closes.** This document says to drop the state on close; the code
round showed that a disposal cannot tell a person closing a tab from a reload tearing one down, and
VS Code disposes every panel on reload — so that deletion would have erased the transcript at exactly
the moment it is needed, making the whole feature a no-op. A closed tab is simply never restored,
because only open panels are deserialized; the store is bounded by age and count instead.

**Nothing is started when a tab comes back.** `reopened` opens the session inside the FIRST turn, and
the transcript rides across in `carry` through `carriedTurn` — the handover a model switch already
ships. No vendor thread is resumed and no adapter learns anything new.

**`workspaceState`, and it is this extension's first use of it.** Everything else here uses
`globalState` deliberately, because a setting belongs to the person and is shared by every window of
the profile. A chat tab is not.

**What the code round then found in the implementation**: `show` runs on every state push, so the
first version rewrote up to twenty whole transcripts into one key each time a queue position moved;
and a question a restored conversation refuses was thrown away rather than returned to the composer,
leaving the person to retype it to try the fix they had just been told to make.

## The open tails

1. A panel whose record was pruned is disposed silently rather than saying the conversation expired.
2. `chatCommand.ts` reached 920 lines against the 800 the coding-style rule allows. The extraction is
   named in `module_extension.md` and left as its own change: this diff had already been through its
   code round, and two other plans are editing the same file.
3. A real reload with two tabs open was not verified here — it needs a running workbench.

## The symptom

There is **no `registerWebviewPanelSerializer` anywhere** in `src_vs_code/src`.
`retainContextWhenHidden: true` (`chatPanel.ts:99`) keeps a tab alive while HIDDEN — a different
thing. Reload the window, or let an extension update restart the host, and every open chat tab
comes back empty or not at all: questions, answers and the passage are gone.

## The stop condition — read before building

The operator's acceptance was conditional: **if it is easy, do it.** Easy means:

- a `WebviewPanelSerializer` registered in `activate`, and
- the page state (`ChatPageState`: title, passage, messages, modelId, capped) written to
  `workspaceState` under the panel's id whenever it changes — it is small, it is already a plain
  object, and it is what `chatPageHtml` renders from.

**Not easy, and therefore out of this plan:** re-opening the vendor threads. The process behind a
`claude`/`agy` tab is dead after a reload and cannot be resumed; a `codex` thread could be, a
Team server never had one. If restoring the text turns into a per-adapter resume design, **stop
and bring the cost back** rather than building it.

## The shape (the easy version)

- On restore, the tab is rendered from the saved state as a CLOSED conversation: the transcript
  is there, the composer says *this conversation was closed by a reload — ask again to continue
  with <model>*, and the first new question re-opens a fresh session with the transcript CARRIED
  (`carriedTurn`, `chatPrompt.ts:172` — the same handover a model switch and a stop use). Nothing
  silently continues a thread that no longer exists.
- The saved state is dropped when the tab is closed by the person, and pruned for tabs older than
  a week on activation, so `workspaceState` does not accumulate every conversation ever had.
- The orphan ledger (`chatOrphans.ts`) already handles the dead PROCESS; this plan handles the
  dead PAGE and does not touch the ledger.

## Build order

1. RED: `chatPanel.test.ts` — `AReloadedTab_ShowsItsTranscriptClosed`; `chatWiring.test.ts` —
   `TheFirstQuestionAfterARestore_CarriesTheTranscript`.
2. Persist `ChatPageState` on every push to the page (one place: where `chatPageHtml`'s state is
   produced).
3. The serializer; the closed-conversation rendering; the carry on the first new question.
4. Pruning; GREEN; whole suite; a manual reload with two tabs open, recorded.

## Test plan

- State round-trips through the storage fake unchanged (messages, passage, model).
- A restored tab is `capped`-like: composer enabled, running false, a sentence shown.
- The first `send` after restore goes through `carriedTurn` with the saved messages.
- A tab closed by the person leaves no saved state.

## Acceptance — one PR per plan, and the gate on both sides of it

This plan ships as **its own branch (`fix/a-conversation-survives-a-reload`) and its own pull request**, and the PR is accepted
only when the whole ritual has run — not when the code works.

1. **Gate, before the first line of code.** `open` a coai session for this repository and the
   branch; `review_plan` with THIS file as the plan; `resolve` every finding (a rejection carries a
   reason); repeat until the verdict is `proceed`. `review_code` refuses without it.
2. **Tests first.** A RED test per defect, watched failing with the real symptom, then GREEN; every
   new behaviour with its happy path and the failure paths it introduces — `npm test` in
   `src_vs_code`, the whole suite, not the one file.
3. **Gate, after the code.** `review_code` with the scope = the *Definition of Done* below plus the
   symptom, and the diff `main...fix/a-conversation-survives-a-reload` — three dots; the two-dot moving-base trap is recorded in
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

**Specific to this plan:** a help sentence under the chat article (what a reload keeps and what it does not); `module_extension.md` gains the serializer and the storage key; CHANGELOG in the person's words; no manifest change.

## Definition of Done

- [ ] A window reload brings every open chat tab back with its transcript, marked closed.
- [ ] The first question afterwards carries the transcript; nothing pretends the old process is alive.
- [ ] Closed tabs and week-old states are not kept.
- [ ] If the stop condition was hit, this file says so and what it would cost — instead of code.
- [ ] `npm test` green; the acceptance ritual complete; promoted on merge.

## Parallelism

Owns `chatPanel.ts` (the creation call and a serializer), `chatPanels.ts`, `extension.ts`
(registration). **Does not touch `chatPage.ts`.** Queue it after
[PLAN_a_page_nobody_can_search.md](PLAN_a_page_nobody_can_search.md) and before or after
[PLAN_the_tab_wears_an_icon.md](PLAN_the_tab_wears_an_icon.md) — all three edit the same
`createWebviewPanel` call, so they are sequenced in one lane, not parallel.
