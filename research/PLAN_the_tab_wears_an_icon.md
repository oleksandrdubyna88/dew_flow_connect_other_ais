# PLAN — the tab wears an icon

> Status: **IMPLEMENTED, 2026-09-09.** Kind: **feature**. Shipped in extension 0.31.21, pull request
> #158. Scope as built: `src_vs_code/src/chatPanel.ts`, a new `chatIcon.ts`, the URI threaded through
> `chatCommand.ts` and `extension.ts`, and two files under `src_vs_code/media/`. Origin:
> [BUGS_2026-09-09.md](../todo/BUGS_2026-09-09.md), entry 20.
>
> Related docs: [module_extension.md](module_extension.md) — *The chat tab wears its own glyph*.

## What shipped differently

1. **The line was the small half.** `createChatPanel` had no context and no URI, and neither had
   `newConversation` or `chatWithOtherAi`; `context` exists only in `activate`. The URI is threaded
   through all three — the URI, not the whole `ExtensionContext`, because a function that needs a
   media folder should say so and this one needs neither storage nor subscriptions.
2. **The path lives in its own module, `chatIcon.ts`.** The only failure mode an icon has is a path
   naming a file nobody shipped: nothing type-checks a `Uri`, and a wrong one produces the generic
   icon and no error anywhere. The code round refused the first test's regex over `chatPanel.ts` and
   was right — a regex proves somebody wrote a string down. The RESOLUTION is a pure function,
   `chatTabIcon(join)`, handed `vscode.Uri.joinPath` by the panel and a joiner of its own by the
   test, so both observe the same two paths and the test opens them on disk.
3. **The contrast was measured rather than judged by eye**: 4.30:1 on white, 3.64:1 on a Light+ tab,
   7.61:1 on Dark+, 8.10:1 on Dark Modern — all past the 3:1 WCAG asks of a graphical object.
4. **The packaging guard was green over its own defect.** It asserted that no `.vscodeignore` line
   starts with `media`; adding `**/*.svg` left it passing. Each pattern is expanded as a glob now, in
   one character-by-character pass — a chain of `.replace` calls cannot do it, because the `.*` that
   `**/` expands to contains a `*` the later `*` rule rewrites. Checked against four exclusions.
5. **An open tail became a test, and it earned its keep the same day.** A panel restored by a
   `WebviewPanelSerializer` would come back without an icon; there was no serializer then, so the
   suite gained a scan that fails any file registering one without reaching `createChatPanel`. The
   reload plan's first implementation went red on it.

## The open tail

The side-by-side against the Welcome tab needs a running workbench and a human eye. The geometry
matches the numbers this plan measured, but **"indistinguishable in size" was never verified** and was
not claimed.

## The goal

A bright GREEN icon on the chat tab, from our palette, using the glyph the operator supplied —
and BIG, the way VS Code's own Welcome tab icon is. Today `createWebviewPanel`
(`chatPanel.ts:95-100`) never sets `iconPath` — `grep` finds none in `src_vs_code/src` — so the
tab wears the generic `≡`.

## What already exists

- `media/` ships SVGs and is not excluded by `.vscodeignore`: `panel.svg` (the activity bar) and
  the exact precedent, a themed pair — `help-yellow.svg` and `help-yellow-light.svg`.
- The green is `#5CC46F`, palette slot 5 (`vendorColour.ts:63`) — the one `gemini` wears, chosen
  with the eleven others to stay apart in a light theme as well as a dark one.

## Two constraints that decide how it is built

1. **A tab icon cannot read the theme.** It is workbench chrome, not webview content;
   `var(--vscode-…)` is unavailable, and `iconPath` takes a `Uri` or `{ light, dark }`. The colour
   is baked into the file — which is why `help-yellow*.svg` is a pair. Two files, or one whose
   green works on both grounds.
2. **"Big" is not a size you can ask for.** The workbench draws a tab icon at its own fixed size;
   the Welcome icon looks big because the glyph fills its box edge to edge with a heavy stroke.
   So: a full-bleed `viewBox`, no internal padding, thick strokes — and checked against the
   Welcome tab side by side, not declared done from the file.

## The asset — done 2026-09-09

The glyph the operator pointed at is the activity-bar icon, `media/panel.svg` — three nodes around
a hub, drawn in `currentColor`, which the workbench recolours for the activity bar and which a tab
icon cannot use (no theme reaches it). So the pair was made from it:

| File | Colour | Why |
|---|---|---|
| `media/chat.svg` | `#5CC46F` — palette slot 5 | the dark-theme icon, the green `gemini` wears |
| `media/chat-light.svg` | `#2E8B3E` — the same hue, darker | the light-theme icon, derived the way `help-yellow-light.svg` was from `help-yellow.svg` (`#E3B341` → `#9A6700`): a bright colour on white reads pale |

Both keep the glyph's geometry and change three things for the tab: the viewBox is cut to the
content (`2.7 0.85 18.6 18.6` — the three outer circles touch the edges, centred on the hub's
visual centre at y 10.15, not the box's 12), the line strokes go from 0.95 to 1.7 and the ring
strokes from 1.15 to 1.8, so that at 16 px the icon is a shape rather than a hairline. That is the
"big" — the box is the same box the Welcome tab gets.

## Build order

1. ~~Receive `chat.svg`; recolour; the light variant; full-bleed the viewBox.~~ Done — see *The
   asset* above. If the side-by-side in step 4 says the strokes are still thin, thicken in the
   file, never by scaling.
2. RED: `chatPanel.test.ts` — `TheChatTabHasAnIcon` (iconPath set, light and dark).
3. `panel.iconPath = { light, dark }` from `context.extensionUri` + `media/…` — the panel host
   needs the extension URI handed in where it is created.
4. GREEN; side-by-side screenshot with the Welcome tab, in both themes, recorded in the promotion.

## Test plan

- `chatPanel.test.ts`: iconPath present with both variants; paths under `media/`.
- Manual: both themes, both screenshots.

## Acceptance — one PR per plan, and the gate on both sides of it

This plan ships as **its own branch (`feat/the-tab-wears-an-icon`) and its own pull request**, and the PR is accepted
only when the whole ritual has run — not when the code works.

1. **Gate, before the first line of code.** `open` a coai session for this repository and the
   branch; `review_plan` with THIS file as the plan; `resolve` every finding (a rejection carries a
   reason); repeat until the verdict is `proceed`. `review_code` refuses without it.
2. **Tests first.** A RED test per defect, watched failing with the real symptom, then GREEN; every
   new behaviour with its happy path and the failure paths it introduces — `npm test` in
   `src_vs_code`, the whole suite, not the one file.
3. **Gate, after the code.** `review_code` with the scope = the *Definition of Done* below plus the
   goal, and the diff `main...feat/the-tab-wears-an-icon` — three dots; the two-dot moving-base trap is recorded in
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

**Specific to this plan:** no manifest, help or setting change; a CHANGELOG sentence; `module_extension.md`'s asset list gains the two files; the screenshots go in the promotion.

## Definition of Done

- [ ] The chat tab shows the green glyph, full-bleed, in light and dark, indistinguishable in size from the Welcome tab's.
- [ ] The test exists and passes; the files ship in the `.vsix`.
- [ ] `npm test` green; the acceptance ritual complete; promoted on merge.

## Parallelism

Owns two files in `media/` and one line in `chatPanel.ts`. Sequenced with
[PLAN_a_page_nobody_can_search.md](PLAN_a_page_nobody_can_search.md) and
[PLAN_a_conversation_survives_a_reload.md](PLAN_a_conversation_survives_a_reload.md) on the same
`createWebviewPanel` call; otherwise free. Nothing waits on anybody — the asset is in the tree.
