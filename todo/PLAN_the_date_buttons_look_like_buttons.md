# PLAN — the date buttons on the rounds log look like buttons

> Status: **plan only, nothing implemented yet.** Scope: `src_vs_code/src/roundsLog.ts` (the
> toolbar markup and the page's stylesheet) and `src_vs_code/src/test/roundsLogPage.test.ts`.
>
> Issue [#126](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/126): *"Today ·
> All dates · Clear — these are buttons. Make them blue like everywhere else, so it is visible that
> they are buttons; I pressed one by accident and only then understood."*

## The symptom

The rounds log page (`Show review rounds`) has a toolbar with three buttons — **Today**, **All
dates**, **Clear** — beside the date pickers and the facet selects. All three are real
`<button type="button">` elements (`src_vs_code/src/roundsLog.ts:1101-1104`), but each carries
`class="secondary"`, and the page's stylesheet (`roundsLog.ts:1009`) gives that class VS Code's
*secondary* button colours: `--vscode-button-secondaryBackground` over
`--vscode-button-secondaryForeground`. In a dark theme those are a mid-grey on a dark grey — the
same tones as the toolbar's inputs and the page's text — so the three read as labels.

Every other control that *does* something on this page is the primary blue: **Answer…** on an open
question takes the bare `button` rule (`roundsLog.ts:1008`, `--vscode-button-background`), and the
sidebar's buttons are blue through the same token. The three toolbar buttons are actions — each one
changes what the table shows — painted not to look like one.

The other two users of the class are the pager's **◀ Newer** / **Older ▶** (`roundsLog.ts:1115-1116`).
They stay as they are: they are navigation between pages, like the tab strip above the table
(`.tabs .tab`, flat by design), not actions on the current view — and the issue names three
buttons, not five. So the `button.secondary` rule stays, for them.

## What must be true when this is done

1. **Today**, **All dates** and **Clear** are painted with the primary button colours —
   `--vscode-button-background` / `--vscode-button-foreground` — exactly as **Answer…** is.
2. The pager pair and the tab strip are unchanged.
3. Nothing about behaviour changes: `setToday`, the `#alldates` and `#clear` handlers are
   untouched — the page script is not in the diff at all.

## The change

- `roundsLog.ts:1101, 1102, 1104`: drop `class="secondary"` from the three toolbar buttons.
- `src/test/roundsLogPage.test.ts`: the two tests below.
- `research/module_extension.md`: one sentence in the rounds-log paragraph — the toolbar's three
  actions are primary buttons, the pager pair secondary, and why.
- `src_vs_code/CHANGELOG.md`: one paragraph under `## Unreleased`. The version is not chosen here:
  this is one of a batch of issues landing before a single extension release, and the release commit
  renames the heading to the version it cuts.
- This file: `git mv` to `research/`, status rewritten, its row in `todo/README.md` moved to the
  promotion log — in the same branch, the plan's last commit.

Three attribute deletions in the source. No CSS change, no script change, no host change, no new
setting. The rest is the record.

## Test plan (RED first)

| # | Test (`src/test/roundsLogPage.test.ts`) | RED symptom expected |
|---|---|---|
| 1 | *the three date buttons are painted as buttons, not as secondary text*: each of `#today`, `#alldates`, `#clear` is a `<button` element carrying no `secondary` class | `class="secondary" id="today"` is found |
| 2 | *what the three are painted with is the page's one primary rule*: the bare `button {` rule names `--vscode-button-background` and `cursor: pointer`; the stylesheet carries **no** rule scoped to `.toolbar button` or to any of the three ids (so nothing later in the cascade can repaint them); and the pager's two buttons still carry `secondary` | (guards the fix against a later restyle in either direction; green before and after — raised on the plan round, codex) |

Run, from a checkout with the extension's dependencies installed (`npm ci` in `src_vs_code`, Node 22
as CI uses): `cd src_vs_code && npm test` — the whole suite, not one file, because
`bundledPage.test.ts` executes the page script against a stub DOM and would catch a markup slip the
string test cannot.

## Definition of Done

- [ ] Test 1 written first and watched fail with the symptom above; then green.
- [ ] `npm test` green in the worktree; the count reported in the pull request.
- [ ] The diff through the `coai` code round, every finding resolved.
- [ ] `research/module_extension.md` — the rounds-log paragraph mentions the toolbar buttons are
      primary; `CHANGELOG.md` gets its line under the next extension version.
- [ ] This plan promoted to `research/` with `IMPLEMENTED` and the date.
