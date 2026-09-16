# PLAN — Edit roles: three tabs, a green frame around what you added, and the menu's own colours

> Status: **plan only, nothing implemented yet.** Scope: `src_vs_code/src/rolesPage.ts`,
> `src_vs_code/src/rolesPanel.ts`, a new `src_vs_code/src/roleTone.ts` extracted out of
> `src_vs_code/src/panelView.ts`.
>
> Related docs: [module_extension.md](../research/module_extension.md),
> [module_tests.md](../research/module_tests.md).
>
> Issue: [#293](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/293).

## The symptom, in the three pictures the issue attaches

1. **The page is one column of everything.** Seven roles, each with up to six prompt boxes ten rows
   high, under three `<h2>` headings on one scroll. The operator's words: *"тут сильно много всего.
   кажду секцию пустить в табы."*
2. **A prompt you add appears with no mark on it.** `Add a prompt` repaints the page and the new
   block lands among the shipped ones, identical to them. *"иначе не понятно куда он добавился, что
   его можно редактировать."*
3. **Every left edge on this page is the same blue.** `rolesPage.ts:387` gives `.role` a fixed
   `border-left: 3px solid var(--vscode-charts-blue)`, while the sidebar has given each role its own
   tone since the settings panel was written — purple for the plan critic, yellow for conventions,
   blue for architecture, orange for security, green for UX/DX (`panelView.ts:1412`). Two views of
   the same seven roles, coloured by two different rules. *"сейчас все синии в едит."*

## What ships

### A. Three tabs

`Plan review` · `Code review` · `Document review` — the three sections `rolesHtml` already computes
at `rolesPage.ts:341-348`, unchanged in content and order. The plan bucket keeps the
`PLAN_DOCUMENT` rows drawn with it, exactly as the comment there says.

**The tab must survive a repaint, and this is the part that is not decoration.** `rolesPanel.ts:252`
(`apply`) returns *whether the page must be redrawn*, and a redraw replaces `panel.webview.html`
wholesale — a new document, with no memory of which tab was open. Every shape-changing action
redraws: `Add a prompt`, `Add a role`, a switch, a stage, a `Remove`, a `Restore`. So a page-local
tab would mean **press "Add a prompt" on an architecture role and be thrown back to the plan tab,
with the prompt you just added on a tab you can no longer see** — which is the very complaint in
picture 2, made worse.

So the tab is remembered by the HOST:

- the page switches instantly on click (no wait for a round trip) **and** posts `{ type: 'tab', id }`;
- `roleEdit` (`rolesPage.ts:91`) decodes it to `{ kind: 'tab', id }`, believed only as one of the
  three known ids — anything else is `IGNORE`, the way every other message on this page is read;
- `rolesPanel` holds it in the module the panel already keeps its state in and passes it into
  `RolesPageState`; `rolesHtml` marks that tab `on` and hides the other two.

`{ kind: 'tab' }` does **not** redraw (`apply` returns `false`) — the page has already done it.

**Adding a role moves you to the Code tab.** A new role always joins the code bucket of the result
stage — that is what `stageIsFull` at `rolesPage.ts:334` is about. Adding one from the plan tab would
otherwise create it somewhere you cannot see. `apply` sets the remembered tab to `code` when it
applies a `kind: 'add'`, and that command already redraws.

An empty section gets a sentence rather than a blank tab.

### B. A green frame around a prompt you added

`promptBlock` (`rolesPage.ts:211`) already computes `shipped = isShippedPrompt(role.id, prompt.id)`
and uses it to choose `Restore` against `Remove`. The frame is that same condition and no new one:
**not shipped means you added it.**

- the whole block, not the left edge: `.prompt.mine { border: 1px solid var(--vscode-charts-green, #b5cea8) }`,
  replacing the 2px grey left edge `.prompt` has today.
- It **stays** green — it is not a highlight of the last thing added, it is what the block *is*. A
  prompt you wrote is the one whose label, purpose and text are all yours to edit; a shipped one has
  two readonly fields and a `Restore`.
- A shipped prompt you have overridden is still shipped: it keeps its grey edge and its `Restore`.
  The green is about who OWNS the block, not about whether it has been typed in.
- A custom prompt inside a built-in role is yours, and is green. That is the case the issue's second
  picture is actually showing.

### C. The sidebar's colours, from one source

`ROLE_TONE` and the `--tone-*` custom properties are private to `panelView.ts` (the map at 1412, the
`:root` block at 2461-2472, the `.role-*` rules at 2490-2495). A second copy in `rolesPage.ts` would
be two palettes that drift, which is the defect this issue reports, one file further on.

Extract them into **`src_vs_code/src/roleTone.ts`**:

```ts
export function roleTone(roleId: string, stage: string): string;  // 'plan' | 'conv' | 'arch' | …
export const ROLE_TONE_CSS: string;   // the :root block + the .role-* border-left rules
```

`roleTone` is exactly the expression at `panelView.ts:1690`, fallback included
(`ROLE_TONE[id] ?? (stage === 'plan' ? 'plan' : 'arch')`), so a custom role is toned the same way in
both views — including the fact that it shares a colour with a built-in one. Matching the menu means
matching it where it is arbitrary too.

`panelView.ts` then imports both and keeps its markup unchanged; `rolesPage.ts` puts `ROLE_TONE_CSS`
in `styles()` and `role-` + the tone on the `<details>`.

## Reuse — what was searched, and what is being widened

- **Tabs**: `roundsLog.ts:1196-1198` (CSS), `:1255` (markup), `:1578-1590` (the click branch). Same
  class names, same `data-tab`/`hidden` shape. The rounds log's own tab is page-local because that
  page never replaces its own document; this one is host-remembered for the reason above. That is the
  only deliberate difference.
- **Colours**: widening move **2, extract the shared half** (`reuse-first.md`). Not a parameter,
  because the caller needs the CSS block as well as the map, and not a move to `roles.ts` — that
  module is the role MODEL and knows nothing about CSS.
- **Message decoding**: `roleEdit` already validates every field of every message; the tab is one
  more `case` with an allow-list, not a new mechanism.

## What the plan round changed

Eleven findings, six gating, verdict `proceed` on the first round; nine accepted, two rejected with
reasons. What they moved:

- **The default tab is named, and an unknown one is normalised.** The plan said the host remembers
  the tab and never said what a page with no choice yet shows. `DEFAULT_ROLE_TAB` is `plan`, the
  first section, and `rolesHtml` normalises anything outside the three known ids to it — so
  **exactly one tab is `on` and exactly two sections are `hidden`, always**, whatever it is handed.
  `roleEdit` still IGNOREs an unknown id rather than storing it: two halves, neither trusting the
  other. (Three findings, from two vendors.)
- **Adding a PROMPT never changes the tab.** Only `add` — a role — moves you, because only a role
  can be created somewhere you are not looking. A prompt is created inside a role you are already
  looking at. The plan implied it; a test now pins it.
- **No source assertion over the host.** `.agents/PROJECT.md:78` is an operator ruling —
  *"A new behavioural assertion over page source text is refused"* — and the test this plan proposed
  for `rolesPanel` was one. So the transition is extracted instead: `nextTab(current, command)`, a
  pure exported function that IS the rule, executed by the test rather than read. `rolesPanel`
  becomes the one line that calls it. (Two findings, two vendors, independently.)
- **The tone is checked across BOTH renderers.** A test of `roleTone.ts` alone stays green while
  `panelView.ts` keeps a private copy — which is exactly today's defect, one file further on. So the
  test renders the same roles through the panel AND through this page and compares the class each
  emits.
- **The green frame is asserted as a RULE, not only as a class.** There is no CSS engine in this
  suite, so `.prompt.mine` being absent from the stylesheet is invisible to a class-name assertion.
  The test asserts the generated CSS carries an all-sides green border for `.prompt.mine`, and that a
  shipped prompt does not get the class.
- **The green is `var(--vscode-charts-green, #b5cea8)`** — with the hex fallback, which is this
  repository's own documented convention for a charts token (`panelView.ts:2458`) and answers the
  "undefined in some themes" objection without leaving the palette the sidebar uses.

**Rejected.** *Persist the tab to `globalState` so it survives an extension reload* — surviving the
REPAINT is the requirement; the redraw that loses the tab happens several times a minute, and a
module variable survives all of them and outlives the panel too. The rounds log page is the
precedent: page-local, resets on a fresh open, never reported. And the suggested test cannot exist
here — this suite has no extension host, so "survives a reload" is unobservable by any means.
*The green frame may conflict with the dynamic sidebar colours* — checked in the code: the tone is a
`border-left` on the `.role` `<details>`, the frame is a `border` on a `.prompt` `<div>` nested
inside it, so neither selector can match the other's element; and no tone is user-settable, since
`ROLE_TONE` is a fixed map keyed by role id.

## Deviations, recorded while building it

- **No empty-section note.** The plan promised one; it is unreachable. `composed` merges the
  shipped catalog into whatever is stored, a shipped role's stage cannot be changed, and the
  catalog populates all three buckets — so no tab can be empty, and a branch nothing can enter is
  worse than an absent one because a test cannot reach it either. Dropped rather than written.
- **The palette moved in `panelView.ts`, so "not one byte" is not quite true.** The `:root` block
  and the `.role-*` rules were separated by other rules (`.role`, `.role-group`, `.consultant-row`)
  and the shared constant carries them as one run, so those rules now sit ABOVE it. The CASCADE is
  unchanged and that is what was actually at stake: `.role` and `.role-arch` have equal specificity,
  so `.role-arch` must come after `.role` — it still does. `:root`'s position does not matter, since
  custom properties resolve at computed-value time. The panel's own tests are the check, and they
  stayed green.
- **`rolesEdit.ts` was touched**, which the plan did not name: two `Exclude<RolesCommand, …>`
  parameters and one guard enumerate the kinds that do not touch a row, and a new kind has to be
  added to them. That enumeration is the point — it is what makes "a tab never edits a role" a
  compile error rather than a convention.
- **One existing test was rewritten, not weakened.** `rolesPage.test.ts`'s "the plan stage and the
  code stage are drawn apart" located the groups by their `<h2>` headings, which are now tabs. It
  asserts the same guarantee against `data-section`, with one more assertion than before.
- **`tabShown` is exported beside `nextTab`.** The plan named one function; normalising on the way
  IN (the transition) and on the way OUT (drawing) is two calls to one rule, and the second needed
  a name of its own.
## Build order

1. `roleTone.ts` — the map, the function, the CSS — plus its tests. `panelView.ts` imports it and
   deletes its private copies. **The panel's rendering must not change by one byte**; its existing
   tests are the check.
2. `rolesPage.ts` — `tab` on `RolesPageState`, the tab strip, the three sections, `roleEdit`'s new
   case, the tone class, the green frame, the CSS for all of it.
3. `rolesPanel.ts` — hold the tab, pass it to `rolesHtml`, set it to `code` on `add`, no redraw for
   `tab`.

## Test plan

Tests first, each watched RED before the code exists (`testing.md`).

New — `src_vs_code/src/test/roleTone.test.ts`:
- every built-in role gets the tone the sidebar gives it today (the five ids, by name);
- a custom plan role falls back to `plan` and a custom result role to `arch`;
- `ROLE_TONE_CSS` defines every tone it names — no `.role-x` rule without a `--tone-x`.

New — `src_vs_code/src/test/editRolesInTabs.test.ts`:
- the three sections are three tabs, and exactly one is `on`;
- the tab named in the state is the one marked `on` and the other two sections are `hidden`;
- **the page's own script switches the tab AND posts it** — run the way `rolesPageScript.test.ts`
  runs this page's script, not read;
- a tab id the page does not know is IGNORED by `roleEdit` (it is a webview message, so it is not
  trusted);
- an empty section says so rather than rendering nothing;
- a prompt you added carries the `mine` class and a shipped one does not — **both halves**, so it
  cannot pass by framing everything;
- a shipped prompt with an override is still not `mine`;
- a role's left edge carries the tone class the sidebar gives that role — asserted for a built-in
  and for a custom one;
- `nextTab` RUN, not read: a `tab` command moves to that tab, `add` moves to `code`, `addPrompt`
  and every other command leave it where it was, and an unknown id normalises to the default;
- the same roles rendered through the panel and through this page emit the same tone class.

Existing suites that must stay green untouched: `rolesPage.test.ts`, `rolesPageScript.test.ts`,
`panelServerPromptAgreement.test.ts`, and every panel-rendering test — step 1 is a pure extraction.

## Definition of Done

- [ ] The three sections are tabs, one visible at a time.
- [ ] The chosen tab survives every repaint, and adding a role lands you on the tab the role appears on.
- [ ] A prompt you added is framed in green, the whole block, always.
- [ ] Every left edge on this page is the colour the sidebar gives that role, from one shared source.
- [ ] `ROLE_TONE` exists in exactly one file.
- [ ] No new behavioural assertion over page source text (`.agents/PROJECT.md:78`).
- [ ] Tests written first and watched red; the full suite green; `plan-lifecycle` and `pin-check` clean.
- [ ] The coai gate: a plan round and a code round, every finding resolved with a reason.
- [ ] `research/module_extension.md` and `research/module_tests.md` updated; this plan promoted.
