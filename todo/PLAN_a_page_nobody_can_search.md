# PLAN — a page nobody can search

> Status: **plan only, nothing implemented yet.** Kind: **bug**. Scope: two webview panels —
> `src_vs_code/src/chatPanel.ts`, `roundsLogPanel.ts`. Origin:
> [BUGS_2026-09-09.md](BUGS_2026-09-09.md), entry 14 (accepted for both surfaces).
>
> Related docs: [module_extension.md](../research/module_extension.md),
> [PLAN_rounds_log_view.md](../research/PLAN_rounds_log_view.md).

## The symptom

Ctrl+F in the chat tab does nothing. Ctrl+F in the rounds log does nothing. Both are pages of long
text — one of answers, one a table built to be searched — and neither has a find bar.

## The cause, which is one boolean

`createWebviewPanel` is called with `{ enableScripts, retainContextWhenHidden, localResourceRoots }`
and **no `enableFindWidget`** — `chatPanel.ts:95-100` and `roundsLogPanel.ts:94`. VS Code gives a
webview panel its real find bar for that one option. (`helpPanel.ts:55` has the same omission; the
help page has a search box of its own, so it is optional here — include it if the operator wants
one behaviour everywhere.)

## Build order

1. RED: `chatPanel.test.ts` already fakes `createWebviewPanel` — assert the options object carries
   `enableFindWidget: true`. Same for the rounds log panel. Watch both fail.
2. Add the option in both places. GREEN.
3. Manual: Ctrl+F in each tab shows the find bar; a match in a long answer is highlighted and
   scrolled to.

## Test plan

- `chatPanel.test.ts`, `roundsLog*.test.ts`: the option is present at creation.
- Nothing else can regress: the option changes no markup and no message.

## Acceptance — one PR per plan, and the gate on both sides of it

This plan ships as **its own branch (`fix/a-page-nobody-can-search`) and its own pull request**, and the PR is accepted
only when the whole ritual has run — not when the code works.

1. **Gate, before the first line of code.** `open` a coai session for this repository and the
   branch; `review_plan` with THIS file as the plan; `resolve` every finding (a rejection carries a
   reason); repeat until the verdict is `proceed`. `review_code` refuses without it.
2. **Tests first.** A RED test per defect, watched failing with the real symptom, then GREEN; every
   new behaviour with its happy path and the failure paths it introduces — `npm test` in
   `src_vs_code`, the whole suite, not the one file.
3. **Gate, after the code.** `review_code` with the scope = the *Definition of Done* below plus the
   symptom, and the diff `main...fix/a-page-nobody-can-search` — three dots; the two-dot moving-base trap is recorded in
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

**Specific to this plan:** no manifest, help or README change; one CHANGELOG sentence — *Ctrl+F works in the chat tab and in the rounds log*; `module_extension.md` mentions the find bar where it lists each panel's options.

## Definition of Done

- [ ] Both tests exist, were watched failing, and pass.
- [ ] Ctrl+F opens a find bar in the chat tab and in the rounds log.
- [ ] `npm test` green; the acceptance ritual complete; promoted on merge.

## Parallelism

Owns one line each in `chatPanel.ts` and `roundsLogPanel.ts`. **Tiny — land it first in its lane**,
because [PLAN_a_conversation_survives_a_reload.md](PLAN_a_conversation_survives_a_reload.md) and
[PLAN_the_tab_wears_an_icon.md](PLAN_the_tab_wears_an_icon.md) edit the same `createWebviewPanel`
call afterwards.
