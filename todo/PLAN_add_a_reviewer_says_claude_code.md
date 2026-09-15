# PLAN — Add a reviewer says "Claude Code", and says it twice

> Status: **plan only, nothing implemented yet, 2026-09-15.** Kind: **bug** (two causes, one
> symptom). Scope: `src_vs_code/src/vendors.ts` (the preset label and one new pure function),
> `src_vs_code/src/panelProvider.ts` (`addVendor`'s offering and its quick pick), and tests.
> Origin: [issue #294](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/294)
> — *"add reviewer нет клод кода"*.
>
> Related docs: [module_extension.md](../research/module_extension.md).

## The symptom

Open **Add a reviewer** and Claude Code is not there. The operator's conclusion was that the
catalogue has no entry for it and one must be added.

## The cause — the catalogue has the entry; two separate things hide it

The entry exists, and has since the presets were written. `vendors.ts:172-183`:

```ts
{
  label: 'Claude (a second one)',
  hint: 'A separate claude -p process: it sees the plan and the diff, never the conversation that produced them.',
  id: 'claude',
  runtime: 'claude',
  model: 'haiku',
  ...
}
```

There is exactly one Claude runtime — `RUNTIMES` at `models.ts:33` is
`['codex', 'gemini', 'claude', 'antigravity', 'local', 'remote']`, and `cliChatLaunch.ts:37-41`
maps `claude` to `claudeAdapter`, whose `CLAUDE_ARGS` (`claudeAdapter.ts:22-29`) run `claude -p`.
So "Claude Code" and this preset are the same program; nothing is missing from the catalogue.

Two independent defects hide it, and either one alone produces the report:

1. **The words "Claude Code" appear nowhere in the entry**, and the quick pick cannot search the
   text that is there. `panelProvider.ts:2330-2341` calls `showQuickPick` with only
   `{ title, placeHolder }` — no `matchOnDetail` — so VS Code filters on the **label alone**. Type
   *Claude Code* and the list goes empty, which reads exactly like an absent feature. The product
   calls it "Claude Code" everywhere else: `consultSettings.ts:24` is
   `{ id: 'claude', label: 'Claude Code' }`, and `consultations.ts:137` repeats it. A person who
   read those words in the Consultant section is searching for them here.

2. **A preset already configured is dropped from the list, silently.** `panelProvider.ts:2321`:

   ```ts
   // A preset already in the panel is not offered twice; the blank one (empty id) always is.
   const offered = VENDOR_PRESETS.filter((p) => p.id.length === 0 || !existing.has(p.id));
   ```

   So an installation that already has a `claude` reviewer cannot add a second one at all — and
   nothing says why the entry vanished. A second Claude row is a thing a person wants (one on
   haiku for cheap passes, one on opus for the hard ones), and it is exactly the shape of failure
   this file already has a docblock about (`vendors.ts:108-114`): *"remove gemini and the list it
   came from was the only place it existed, so it could never be added back. A default that
   cannot be restored is a one-way door, and the operator walked through it."* Pinned by
   `vendors.test.ts:5-14`.

   Contrast the Consultant picker, which never silently drops a catalogue entry — it renders a
   `refusal()` line naming the reason (`consultantView.ts:362-364`, rule at
   `consultSettings.ts:598-602`).

**Decision, taken by the operator on 2026-09-15: fix both.** Either cause alone would leave the
report half-answered, and neither fix is large.

## What must be true when it is done

1. A person typing `Claude Code` into **Add a reviewer**'s filter box sees the Claude entry.
2. A person typing words from the entry's hint — `claude -p`, say — also sees it.
3. The entry still says it is a SECOND, separate process, not the session driving the gate. That
   distinction is what `(a second one)` and the hint carry, and it is not traded away for
   searchability.
4. Every catalogue preset is offered **whether or not one of its id is already configured**, and
   picking one that is configured adds a second row under a free id rather than being refused.
5. The entry for an already-configured preset says so, and names the id the new row will take —
   no silent difference between the two cases.
6. Nothing else about `addVendor` changes: the Team-server entries, the blank
   OpenAI-compatible entry, the custom-endpoint flow and `saveVendor`'s duplicate refusal all
   behave as before.

## The design

Two edits and one new pure function.

**`vendors.ts`**

- `vendors.ts:173` — label becomes `'Claude Code (a second one)'`. Point 3 above is why the
  parenthetical stays.
- A new exported pure function, beside `normaliseId` (`vendors.ts:304-310`):

  ```ts
  /** The id a new row of this preset takes: the preset's own, or the next free `<id>-N`. */
  export function freeVendorId(base: string, taken: ReadonlySet<string>): string
  ```

  `base` when free, else `base-2`, `base-3`, … A blank base is returned unchanged — the blank
  preset names itself through `askCustomEndpoint` and must not be given an id here.
- A second exported pure function that makes the whole offering decision testable without a
  VS Code host:

  ```ts
  export interface OfferedPreset {
    readonly preset: Vendor & { label: string; hint: string };
    readonly id: string;      // what the new row will be called
    readonly second: boolean; // one of this preset's id is already configured
  }
  export function presetsOffered(
    presets: readonly (Vendor & { label: string; hint: string })[],
    taken: ReadonlySet<string>,
  ): readonly OfferedPreset[]
  ```

  This is the reuse-first move: the decision leaves the `vscode`-bound host and joins the pure
  module the rest of the panel's decisions already live in, so it is tested by calling it rather
  than by scanning `panelProvider.ts` as source text.

- A third exported pure function, so the quick pick's ITEMS are decided outside the host and can be
  tested by calling them — this is what the gate's round asked for, and it is the difference
  between testing the fix and testing a function beside it:

  ```ts
  export interface ReviewerPickItem {
    readonly label: string;
    readonly detail: string;
    readonly description: string; // '' unless this adds a second row
    readonly offered: OfferedPreset;
  }
  export function reviewerPickItems(offered: readonly OfferedPreset[]): readonly ReviewerPickItem[]
  ```

**`panelProvider.ts`**

- `addVendor` replaces the `filter` at `:2321` with
  `reviewerPickItems(presetsOffered(VENDOR_PRESETS, existing))`, mapped onto quick-pick items that
  carry `offered` (not `preset`) alongside the unchanged Team-server entries.
- The options object at `:2340` gains **both** `matchOnDetail: true` **and**
  `matchOnDescription: true`. VS Code's `matchOnDetail` filters `detail` only; the new row's id
  lives in `description`, so without the second flag typing `claude-2` returns an empty list.
  (gemini, this plan's round.)
- **The blank-preset branch is preserved exactly.** After a pick:
  `let vendor: Vendor = { ...picked.offered.preset, id: picked.offered.id };` and the existing
  `if (vendor.id.length === 0) { … askCustomEndpoint … }` at `panelProvider.ts:2356-2362` still
  fires, because `presetsOffered` returns the blank preset's id unchanged as `''`. The
  Team-server branch at `:2346-2353` is untouched.

  This clause exists because the first draft of this plan wrote `{ ...preset, id: offered.id }`
  where `offered` was the ARRAY — `offered.id` is `undefined`, which `saveVendor` would have
  written into the settings as a row with no id, and the same line would have skipped
  `askCustomEndpoint` for the blank preset. Caught in the plan round by gemini before any code;
  recorded here because the shape of the mistake is easy to make again.
- `saveVendor` (`panelProvider.ts:2428-2438`) keeps its duplicate refusal untouched — it is now
  unreachable for this path, and it stays as the guard for every other one.

### Deliberately NOT in this change

- The custom-endpoint flow, the Team-server entries and `LOCAL_PRESET` handling.
- Any change to `DEFAULT_VENDORS` (`vendors.ts:103-106`). A fresh install still starts with
  `codex` + `antigravity`.
- `vendorColour.ts`. A second Claude row takes a colour from the same allocator as any other
  reviewer; `claude` keeps its anchored hue (`vendorColour.ts:90-96`) and `claude-2` is allocated
  around it, which is what that file is for.

## Growth budget

The only thing this makes bigger is the `coai.vendors` array in VS Code settings, which a person
now grows by hand where before the catalogue refused them.

- **Projected size:** a row is roughly 200 bytes of JSON. The realistic ceiling is a person adding
  a handful of duplicates — under 20 rows, ~4 KB. There is no automatic writer: every row is one
  deliberate quick-pick.
- **Who retires it:** the person, with the existing `remove` button on each card
  (`panelView.ts:995` → `panelProvider.ts:2445-2470`), which already refuses to remove the last
  reviewer.
- **When interrupted:** nothing is in flight. A single `config.update` either lands or does not;
  there is no state to sweep.

## Build order

1. **RED** — the three tests below. Run them and watch them fail with the real symptom.
2. `freeVendorId` and `presetsOffered` in `vendors.ts`.
3. The label at `vendors.ts:173`.
4. `addVendor` in `panelProvider.ts`: the offering, the `description`, `matchOnDetail`, the id.
5. **GREEN** — the three tests, then the whole suite.
6. `research/module_extension.md` — a dated section recording that the catalogue is now offered in
   full and what a second row is called.
7. `CHANGELOG.md` + `src_vs_code/README.md` where the behaviour is user-visible.

## Test plan

Home: `src_vs_code/src/test/vendors.test.ts` (the catalogue's own tests, where the one-way-door
regression already lives at `:5-14`).

1. `'the Claude preset is one a person searching for Claude Code can find'` — select the preset
   **with `id === 'claude'`** and assert its label contains `Claude Code` **and** still carries the
   second-process wording. Picking it by id rather than by "some preset" is the point: an assertion
   that any label contains `Claude Code` stays green while the `claude` entry keeps its old label
   and the search still fails. (codex, this plan's round — and the house rule that a structural
   assertion must pin the whole condition.)
2. `'a preset already configured is still offered, under a free id'` —
   `presetsOffered(VENDOR_PRESETS, new Set(['claude']))` still contains the claude preset, with
   `id === 'claude-2'` and `second === true`. Fails today: the function does not exist, and the
   behaviour it replaces drops the entry.
3. `'a free id is the preset's own when nothing holds it'` — and `claude-3` when both `claude` and
   `claude-2` are held; a blank base comes back blank, so the custom-endpoint flow still fires.
4. **The condition is pinned whole**: test 2 also asserts that a preset whose id is NOT taken comes
   back with `second === false` and its own id — otherwise it would pass against a function that
   marked everything a second row.
5. `'the item for a second row says which id it will take'` — `reviewerPickItems` gives an entry
   with `second: true` a `description` naming the new id, and gives one with `second: false` an
   empty description. This is the assertion that would fail if the description were dropped.
6. **The wiring, which no pure test can see.** `matchOnDetail`/`matchOnDescription` are arguments to
   a VS Code API that this suite has no host for (`research/module_tests.md` names "no extension
   host" as a known gap), so they are covered the way this repository already covers exactly this
   case — a structural assertion over `panelProvider.ts`'s source, the idiom of
   `customEndpointIsOneFlow.test.ts:9-17`. It asserts that the Add-a-reviewer `showQuickPick`
   options carry both flags, and that `addVendor` no longer contains the old
   `VENDOR_PRESETS.filter(` line. Source-text assertion is legitimate here and only here: the rule
   that forbids it (`.agents/PROJECT.md`) is about webview PAGES, which have a program to run;
   this is a call into an API the suite cannot instantiate.
7. Whole suite: `cd src_vs_code && npm test`. Baseline on `origin/main` for this branch is
   **2932 tests, 2931 pass, 1 skipped, 0 fail**; the count may only go up.
8. `research/module_tests.md` gains the Add-a-reviewer flow in its catalogue, including what is
   still NOT covered — that nobody has driven the real quick pick.

The decisions that CAN be pure were made pure precisely so they are tested by calling them; only
the two API flags fall back to a source assertion, and the reason is written above.

## Definition of Done

- [ ] The three tests were written first, run RED, and the failure named the real symptom.
- [ ] `Claude Code` is findable in the quick pick by label, and the hint is searchable via
      `matchOnDetail`.
- [ ] `(a second one)` and the hint survive the rename.
- [ ] Every preset is offered whatever is configured; a second row gets a free id and the entry
      says which.
- [ ] Whole extension suite green, with the observed numbers reported.
- [ ] `research/module_extension.md` updated; `CHANGELOG.md` and `src_vs_code/README.md` carry the
      user-visible sentence.
- [ ] `node .agents/conventions/tools/plan-lifecycle.mjs` and `pin-check.mjs` clean.
- [ ] `todo/README.md` carries this plan's row, in the same commit as this file.

## Acceptance — the gate on both sides

1. `mcp__coai__review_plan` over this document before the first line of code; `resolve` every
   finding; repeat until `proceed`.
2. RED test per defect, then the whole suite — never one file alone.
3. `mcp__coai__review_code` with this document as the scope and a three-dot diff
   `origin/main...fix/add-a-reviewer-says-claude-code`; `resolve` every finding.
4. Pull request only after the code round is resolved, per
   `.agents/conventions/common/pull-requests.md`. At most three open on this repository.
5. About five minutes after opening: read CI and CodeRabbit, verify every comment against the code
   and the rules, fix or answer, resolve the threads, merge by rebase.
6. **The promotion inverts the checklist's order**, per `planning-docs.md` — *The `git mv` goes
   LAST*. Every edit is made while this file is still in `todo/`: the status line becomes
   `IMPLEMENTED, <date>` with its deviations, its own links are rewritten for the DESTINATION
   (`../research/` on links to plans still open; the `../research/` prefix DROPPED on links to
   things already there), every inbound reference is fixed, and `todo/README.md`'s row is removed.
   Only then `git mv`, then `git add` the destination path — `git mv` stages the ORIGINAL bytes —
   and all of it in ONE commit. Done the other way round the rename sits staged for the length of
   the promotion, where any peer's plain `git commit` carries half of it away and the plan ends up
   in both folders at `HEAD`. (local, this plan's round; the procedure it asks for is the one the
   rule already prescribes.)
7. `node .agents/conventions/tools/plan-lifecycle.mjs` after the promotion, as the check that the
   move, the status line, the links and both indexes agree.

Specific to this plan: no new command and no new setting, so `helpCoverage.test.ts` has nothing to
demand — but the Add-a-reviewer help text (`help.ts:95-96`) is checked for whether it still
describes what the pick does.

## What the plan round changed

`review_plan`, 2026-09-15: verdict `good_enough` (7 gating against a threshold of 6, all three
reviewers answered — the plan stage allows one round, so `good_enough` is the pass). Accepted and
folded in above: the `offered.id`/blank-preset design bug, `matchOnDescription`, the label test
pinning `id === 'claude'`, the untested wiring, the pick-item seam, and the promotion ordering.
Declined with reasons: `research/architecture.md` (no seam moved), the `npm test` "assumption" (it
is the convention and what CI runs — the reviewer applied the .NET rule to the Node half), and the
quick-pick theme/truncation check (not observable in a suite with no extension host; its
substantive half is `matchOnDescription`, which was accepted).
