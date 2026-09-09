# PLAN — a page nobody can search

> Status: **IMPLEMENTED, 2026-09-09.** Kind: **bug**. Shipped in extension 0.31.19, pull request #154.
> Scope as built: THREE webview panels — `src_vs_code/src/chatPanel.ts`, `roundsLogPanel.ts`,
> `helpPanel.ts`. Origin: [BUGS_2026-09-09.md](../todo/BUGS_2026-09-09.md), entry 14.
>
> Related docs: [module_extension.md](module_extension.md) — *Every page can be searched* — and
> [PLAN_rounds_log_view.md](PLAN_rounds_log_view.md).

## What shipped differently

1. **The help page is IN, not optional.** The draft left it out because that page has a search box of
   its own. It is included because the test is a DISCOVERY — it walks `src/` for every
   `createWebviewPanel(` call rather than naming files, the doctrine `theLogRefusesToOpen.test.ts`
   states — and a discovery that carries an exception for one file becomes the hand-written list it
   exists to replace. The find bar is chrome ABOVE the webview, so the help page's own box is not
   displaced.
2. **The draft's test premise was false.** It said `chatPanel.test.ts` already fakes
   `createWebviewPanel`; it does not — that file tests the pure registry `chatPanels.ts` and never
   imports `vscode`. Every panel owner imports `vscode`, so none can be called in this suite; the test
   is a structural source scan, the idiom of `theLogRefusesToOpen.test.ts:134-172`.
3. **The code round rewrote that test, and it was right to.** The first version matched
   `enableFindWidget: true` anywhere in a call, and three reviewers independently pointed out that a
   string literal, a comment, or an object one level down would satisfy it while the call VS Code
   receives had no such option — the guard staying green over exactly the dead Ctrl+F it was written
   for. It now blanks comments, strings and regular expressions first (which is also what makes the
   balanced-parenthesis cut of the arguments safe: a `)` inside a title can no longer close the
   count), requires the option as a TOP-LEVEL property exactly once with a literal `true`, refuses a
   spread among those properties, and walks `.tsx`/`.mts`/`.cts` as well. A second test drives all
   seven evasions and both formattings through the same code.
4. **Watched red twice** — once against the unfixed tree, and again after the rewrite by removing the
   option from `helpPanel.ts` (`helpPanel.ts: this page cannot be searched`).

## The open tail

Manual verification was on Windows. **The macOS shortcut (Cmd+F) and macOS focus behaviour are not
verified**, and were not claimed.

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
