# PLAN — a long model list can be searched, and a panel action that takes time says so

> Status: **plan only, nothing implemented yet, 2026-10-02. Plan gate good_enough (2/2 reviewers, 9 findings
> accepted); E1 plan round proceed (2/2); cadence consultation (codex, gpt-6-astra) folded in — §3.4, §3.4a,
> §3.9, §3.14.** Scope: the two pages built by `pageDocument` (the sidebar and the Settings tab) —
> `src_vs_code/src/panelView.ts`, `panelProvider.ts`, `extension.ts` (one `dispose` registration), three new
> page/host modules (`selectSearch.ts`, `busyMark.ts`, `inFlight.ts`), one new `renderCoalescer.ts`, tests,
> `research/module_extension.md`, `research/module_tests.md`. Three epics, three branches, three commits (§5a).
>
> Related docs: [module_extension.md](../research/module_extension.md),
> [PLAN_custom_endpoint_model_list.md](../research/PLAN_custom_endpoint_model_list.md) (the ≡ list this builds on).

## 1. The symptoms

Reported by the operator on 2026-10-02, after extension 0.60.2 shipped the ≡ endpoint list.

1. **A 200-entry dropdown cannot be searched.** ≡ on the `openrouter` card (runtime `codex`, base URL
   `https://openrouter.ai/api/v1`) worked: *"200 models openrouter.ai listed for the key under 'openrouter'"*. The
   dropdown then holds two hundred `vendor/model` ids in the endpoint's own order, and the only way to find
   `anthropic/claude-opus-5.5` is to scroll. Asked for: *"a search field — I type, and it sorts"*.
2. **Changing a setting freezes the panel for seconds with nothing on screen.** Reported on the consultant's model
   picker (*Claude Code asks → Codex (OpenAI) → GPT-6-Astra*). Asked for: *"everywhere an action takes longer than
   0.5 s, show a progress bar or a spinner"*. The operator accepts that the work itself takes time.
3. **An endpoint row offers "the CLI's default".** That entry stores an empty model, and an empty model on a codex
   row means no `-m` at all (`src_mcp/runners/Reviewers/ReviewerRuntime.cs:354`), so the Codex CLI sends its OWN
   default id to OpenRouter, which does not serve it. The label comes from `modelWords`
   (`src_vs_code/src/panelView.ts:1389`), which decides by RUNTIME alone — the same one-site decision
   `asksAnEndpoint` (`endpointModels.ts:48`) fixed for the list itself yesterday.
4. **Vendor keys says nobody needs a key while an OpenRouter row exists.** `keysBody` (`panelView.ts:1627`) counts
   only ENABLED rows with a base URL, so with `openrouter` switched off the tab reads *"Every reviewer you have signs
   in through its own CLI, so none of them needs an API key"* — false about a row that is on the Reviewers tab.

## 2. Why each happens (verified in the code, 2026-10-02, at `1056aed9`)

- **No search:** every model picker is a plain `<select>`; `modelOptions` (`panelView.ts:2739`, comment at `:2735`)
  records why a `<datalist>` was refused — it filters by the value already in the box, so once a model is chosen
  every other one vanishes. Nothing else was put in its place.
- **No busy mark:** the page posts and forgets. `save()` (`panelView.ts:533`) and `bindCommands`
  (`panelView.ts:681`) call `vscode.postMessage` and nothing on the page changes until the host repaints. On the host,
  `receive` (`panelProvider.ts:921`) queues the write (`enqueue`, `:1585` → `WriteQueue.enqueue`, fire-and-forget),
  the configuration change fires a `render()` (`:968`) from `extension.ts:439`'s listener, and `render()` re-reads
  sessions, the usage ledger and the Codex cache before it paints. A changed paint key REPLACES the webview document
  (`surfaceSlot.ts:112`). The only busy marks that exist are per feature: the ≡ button's `disabled`
  (`askingEndpoints`), a Team server's "Signing in…", and `withProgress` notifications for three commands.
- **Renders pile up:** one dropdown change can produce two or three full renders — the configuration listener's,
  the focus release's (`receive`, `:937-940`), and any probe landing — and they run concurrently, each doing every
  read. Nothing coalesces them. `WriteQueue` (`writeQueue.ts:9`) is not the tool: it must run EVERY write; a
  coalescer must merge calls.

## 3. What must be true when it is done

Each item is checkable; §6 names its test and its epic.

1. Every `<select>` on the sidebar and the Settings tab with **15 or more options** gets a search box immediately
   before it. Selects with fewer get none. The threshold is one named constant (`SEARCH_FROM_OPTIONS`).
2. Typing in the box ranks the select's options and HIDES the ones that do not match: an id (value or label) that
   starts with the query first, then one where a segment after `/`, `-`, `.`, `:` or a space starts with it, then any
   that contains it, each group in the list's original order. Several words must ALL match. Matching ignores case.
   An empty query restores the original order with nothing hidden.
3. The sentinel choices — the empty first entry (`the CLI's default` and its siblings), `another model…`
   (`__other__`) and the consultant's custom-endpoint entry (`CUSTOM_ENDPOINT`, `consultSettings.ts:654`) — are
   never hidden and keep their positions, and so does the option currently selected, so the select never displays
   a value it does not list. **Sentinels are not handed to the ranking at all**, so they can never be a match.
4. **Enter** in the box selects the **first option that MATCHES the query under the ranking** — never a preserved
   sentinel, never the preserved non-matching selected option — and commits it by setting the select's value and
   **dispatching the select's own `change` event**, so whatever the select is routed through runs exactly as for a
   mouse pick: `save()` for a `data-setting` select, the prompt handler for a `data-prompt` select
   (`panelView.ts:668`), and the focus release after it. Not `save()` directly — a prompt picker is not a setting
   (consultation, 2026-10-02). **With zero matches Enter is a no-op: nothing saved, nothing thrown.** A **disabled**
   select gets a disabled box and Enter does nothing. **Escape** empties the box and restores the original order.
   **Arrow down** moves focus to the select.
4a. **A query survives a repaint.** The box has its own focus identity (`search|` + the select's identity): focusing
   or typing in it reports a hold through the SAME `reportFocus` path the settings use, so a repaint is withheld while
   someone types; tabbing between a setting and a search box is not a release (the `focusout` guard at
   `panelView.ts:633-637` recognises both). The query is kept in the webview's own state (`vscode.setState`), keyed
   by the select's identity, so when the document IS replaced — a changed list, or the 30-second hold running out —
   the new page re-attaches the box with the same query applied and, if it was focused, the caret back in it.
5. The original option order is recorded ONCE, when the box is attached — an index per option held in the page
   script (a `Map` from option to index), never in markup — and is what Escape and an emptied query restore.
6. The ranking is ONE exported pure function, `rankChoices(query, choices)`, unit-tested in TypeScript AND embedded
   into the page by its source text (`toString()`, as `roundsLog.ts:1588` does), so the function tested is the
   function that runs. `bundledPage.test.ts` guards it in the bundled panel page through `bundleOf`
   (`bundledPage.test.ts:444`).
7. A setting write, a prompt choice or a command posted by either page that has not been **settled** after
   **`BUSY_AFTER_MS` = 500 ms** shows a thin indeterminate progress bar at the top of the page (`role="progressbar"`,
   labelled) and marks the control that started it `aria-busy="true"`. Settled sooner, nothing is shown.
8. **Host-side tracking is a real collection.** `InFlight` (new `inFlight.ts`) is a map keyed by an operation id the
   host mints; each entry remembers the `seq` the page sent, the slot (`SurfaceSlot`) that posted it and its start
   time. An entry is removed in a `finally`. Its size is bounded by what one person clicks while the host is busy;
   `PanelProvider.dispose()` (new — the class has none today, `panelProvider.ts:235`; registered in
   `context.subscriptions`, `extension.ts:421-451`) settles every entry with `ok: false` and clears the map.
9. **Settlement is host-owned and happens in a `finally`.** The mark means *the host finished the work and the
   render pass it caused* — not "the controls changed": a pass may legitimately PATCH instead of repaint while an edit
   hold is active (`surfaceSlot.ts:112-130`), and then the controls are deliberately left as they are. A setting:
   take the coalescer's run mark, `await this.writes.run(() => this.write(…))` (`WriteQueue.run`,
   `writeQueue.ts:25`, hands back the write's own outcome — `enqueue` cannot) and THEN await a render that STARTED
   AFTER that mark (§3.14) — outside the queue, so it is never the self-wait `afterTheWrite` (`refusedWrite.ts:76`)
   exists to avoid. Every render awaits `writes.settled()` before it reads the configuration (`panelProvider.ts:979`),
   so a run that started after the write was queued reads the written value; that is why the configuration
   listener's own render (`extension.ts:439`) can be SHARED rather than followed by a second full one. A prompt: the same with `choosePrompt`. A command: `await this.run(command, id, from)` (its own
   renders are inside it). A failed write, a failed render and a refused command all settle; the `settled` message
   carries `ok: boolean` — `false` when the tracked promise rejected. (A refused box that snapped back is a RESOLVED
   write: its refusal is already said by `reportRefusal`.) The page clears the mark on either value.
10. **The host announces its count.** After every start AND every settle it posts `{ type: 'busy', count,
    oldestMs }` to every slot (`this.slots`), where `oldestMs` is the age of its OLDEST in-flight entry (0 when
    empty). `settled` goes only to the slot that posted the `seq`.
11. **A page painted while work is in flight does not flash and does not lag.** `pageDocument` writes the host's
    snapshot `{ count, oldestMs }` into the painted document (`jsonForScript`, `webviewHtml.ts:163`) — read when the
    page is BUILT, in `pageFor`'s `html` thunk (`panelProvider.ts:952`, as `currentUiScale()` is), never put into
    `staticKey` / `settingsKey`. With `count > 0` the page shows the bar after **`max(0, BUSY_AFTER_MS − oldestMs)`**:
    an action that settles under 500 ms never flashes; one already 400 ms old shows the bar 100 ms later, not 500.
12. **A freshly loaded page says `ready`.** Messages posted while a document is being replaced can be dropped, so
    the shared script's last statement posts `{ type: 'ready' }` and the host answers THAT slot with the current
    `busy` snapshot, which the page applies exactly as the painted one (§3.11), replacing any pending host-busy timer.
13. **A replaced page owns nothing.** Its `seq`s die with it; the host still settles them (nobody listens — fine).
    The new page learns state only from the painted snapshot, the `ready` answer and the broadcasts; a `settled` for
    a `seq` it never posted changes nothing. Its bar clears when the host's count reaches 0 and no local `seq` is
    pending. No control is known for a host-only busy state, so no `aria-busy` is set then — bar only.
14. Concurrent `render()` calls are coalesced: while one runs, any number of further calls share ONE trailing run
    that starts when it ends. No call is dropped — every caller's promise resolves only after a run that started
    after its call. **The arrival boundary is atomic:** a call arriving while a run is in flight — including after
    its work finished but before the coalescer marked it done — joins the trailing run; the running→idle transition
    and the start of the trailing run happen synchronously in the coalescer's own continuation, before any awaiting
    caller resumes. **Runs are numbered.** `mark()` returns the number of the last run started; `runAfter(mark)`
    resolves with a run whose number is greater — the one in flight if it started after the mark, at once if a
    finished run did, otherwise the trailing run it requests. Residual, stated in the module header: if the
    configuration event arrives only after `runAfter` has started its own run, the listener's call becomes a second,
    trailing run — bounded, never concurrent.
15. An endpoint row (`asksAnEndpoint`) labels its empty model choice **"no model yet — press ≡ and pick one this
    endpoint lists"** instead of "the CLI's default"; a plain codex/claude/gemini row keeps "the CLI's default"
    (`panelView.test.ts:185` stays green); `api` and `local` keep their own words (`localWording.test.ts:66,74`).
16. Vendor keys names EVERY endpoint row, enabled or not: enabled ones need a key now (today's sentence), switched-off
    ones are named as needing one once switched on, and "Nothing to fill in yet" appears only when there is no
    endpoint row at all (`panelView.test.ts:379,391,401` keep their meaning).

## 4. Constraints

- **No new dependency.** The page is plain DOM; the host change is TypeScript in the extension. No server change,
  so no `mcp-v*` release.
- **One road in.** The search attaches to every qualifying select from ONE place in the shared page script
  (`pageDocument`, `panelView.ts:503`), not at each select builder; the busy mark wraps the page's seven post sites
  (`:539,548,558,567,670,674,684`) in ONE `send()` that stamps `seq` — `focus` and `section` posts pass through it
  UNTRACKED (they are state, not work); the settle happens in ONE `track()` wrapper in `receive`.
- **No repaint for the mark.** The in-flight snapshot is not part of `staticKey` / `settingsKey` (§3.11).
- **`.looking` stays defined once** (`lookingSpinner.ts`); the bar is its own class. No backticks and no hex in the
  CSS strings (template-literal rule); no `${JSON.stringify(…)}` inside a script — `jsonForScript` only.
- **The embedded ranking function references nothing but its own parameters** and contains no backtick, as
  `roundsLog.ts`'s embedded functions do (`roundsLog.ts:885`).
- **A webview behaviour is tested by RUNNING the page** (`.agents/PROJECT.md:100-108`); no new behavioural assertion
  over page source text. The harness (`src_vs_code/src/test/panelPageHarness.ts`) is widened, stricter than a DOM and
  never more permissive, and each widening has its own tests in `panelPageHarness.test.ts`.
- **Out of scope, recorded:** the other webviews (§8); making `render()` itself cheaper; refusing a round for an
  endpoint row with no model on the server side (`CustomCodexRuntime`), a server release of its own.

## 5. Build order

E1 → E2 → E3 (§5a), in that order; inside an epic, stories in their numbered order; RED before GREEN throughout.

## 5a. Epics

The gate's operator ordered the split: the gate runs once per epic; each epic is its own branch from the previous
epic's commit, lands as ONE commit, and is reviewed by `review_code` over its whole diff. Each epic leaves the suite
green and ships on its own; no epic depends on a later one.

### E1 — the two one-site defects · `feat/model-search-e1` from `1056aed9` (origin/main)

| Story | Goal | Files | Tests | Done when |
|---|---|---|---|---|
| 1.1 | RED tests for symptoms 3 and 4, watched failing on `1056aed9` | `src_vs_code/src/test/endpointModelsCard.test.ts` (one test), `src_vs_code/src/test/vendorKeys.test.ts` NEW | §6 rows E1 | both red with the real symptom in the message (the old label; the "Nothing to fill in yet" sentence) |
| 1.2 | `modelWords(runtime, baseUrl)` decides through `asksAnEndpoint` | `panelView.ts:1389` and its callers | 1.1's label test + `panelView.test.ts:185`, `localWording.test.ts` | GREEN; a plain codex row still reads "the CLI's default" |
| 1.3 | `keysBody` names every endpoint row, enabled or not; docs | `panelView.ts:1627`; `research/module_extension.md` (a paragraph under the ≡ record at `:9845`), `research/module_tests.md` | 1.1's keys tests + `panelView.test.ts:379-401` | GREEN; whole suite, typecheck, lint green; one commit |

### E2 — model search · `feat/model-search-e2` from E1's commit

| Story | Goal | Files | Tests | Done when |
|---|---|---|---|---|
| 2.1 | `rankChoices` pure, with its constant | `src_vs_code/src/selectSearch.ts` NEW (`SEARCH_FROM_OPTIONS`, `rankChoices(query, choices): readonly number[]` — indices in rank order, non-matches absent) | `src_vs_code/src/test/selectSearch.test.ts` NEW | every §3.2 clause has a case; the function body has no backtick and reads no outer name |
| 2.2 | Harness: live options and a real-enough select | `test/panelPageHarness.ts` (`Control.options` become live objects with `hidden`, `selected`, `value`, `text`; `appendChild` MOVES; `createElement`, `insertBefore`, `parentNode`, `querySelector` → `null`; `fire('keydown', { key })`) | `test/panelPageHarness.test.ts` (one test per widening) | each widening proven stricter-than-DOM (an unparseable selector answers nothing; an unknown tag refuses) |
| 2.3 | The box on the page, one road in; bundle guard | `panelView.ts` (`pageDocument` attaches from the shared script; CSS; `var rankChoices = ${rankChoices.toString()}`), `test/selectSearchPage.test.ts` NEW, `test/bundledPage.test.ts` (panel page via `bundleOf('panelView.ts', 'panelHtml')`; `rankChoices` in its EMBEDDED list) | page run: §3.1, §3.2–3.3, §3.4, §3.5; bundled: §3.6 | every §3.1–§3.6 test green with teeth (§6); no select builder edited |
| 2.4 | Docs and help | `research/module_extension.md`, `research/module_tests.md`, `src_vs_code/src/help.ts:98` (`vendorModel` mentions the box) | the help test that already reads `HELP` | docs describe what shipped; one commit |

### E3 — busy marks · `feat/model-search-e3` from E2's commit

| Story | Goal | Files | Tests | Done when |
|---|---|---|---|---|
| 3.1 | Coalesce `render()` | `src_vs_code/src/renderCoalescer.ts` NEW (`Coalescer.run(work)`), `panelProvider.ts:968` (body → `renderNow()`, `render()` runs it through the coalescer) | `test/renderCoalescer.test.ts` NEW — §3.14 incl. the atomic-boundary case | three calls during a run → one trailing run; a rejecting run rejects only its callers; next run still happens |
| 3.2 | Host tracks, settles, announces, answers `ready`, paints its snapshot, disposes clean | `src_vs_code/src/inFlight.ts` NEW, `panelProvider.ts` (`seq` on `PanelMessage` `:218`; `track()` in `receive` `:921`; `ready` branch; `dispose()`), `extension.ts:421-451` (register `dispose`), `panelView.ts:503` (`pageDocument` takes the snapshot) | `test/inFlight.test.ts` NEW (§3.8–3.10); a provider-level test over fake slots: start → `busy` to every slot; settle → `settled` to the poster + `busy` to all; `ready` → answer to that slot only; `dispose` → all `ok:false`, map empty | the paint key is unchanged by the snapshot (asserted); a write settles only after `run()` AND `render()` |
| 3.3 | The page's mark; harness clock | `src_vs_code/src/busyMark.ts` NEW (`BUSY_AFTER_MS`, bar markup, CSS, the script fragment: `send()`, per-`seq` timers, painted-age timer, `ready` post, `settled`/`busy` handling), `panelView.ts` (the seven posts → `send()`), `test/panelPageHarness.ts` (a controllable clock: `setTimeout`/`clearTimeout` run only when the test advances it), `test/busyMarkPage.test.ts` NEW | page run, fake clock: §3.7, §3.11, §3.12, §3.13 incl. the replacement scenario; harness test for the clock | every test green with teeth; no post site gained the mark by being edited — `send()` did |
| 3.4 | Docs | `research/module_extension.md`, `research/module_tests.md`; this plan's §8 pointer | — | docs describe what shipped; whole suite, typecheck, lint, family checks green; one commit; this plan promoted |

## 6. Test plan

| Epic | Item | Test | Kind |
|---|---|---|---|
| E1 | §3.15 label | an OpenRouter card's empty option reads the endpoint words; a plain codex card keeps "the CLI's default" | page run (options as drawn) — RED first |
| E1 | §3.16 keys | a switched-off OpenRouter row is named as needing a key once on; no endpoint row → "Nothing to fill in yet"; an enabled one → today's sentence | rendered section — RED first |
| E2 | §3.2 ranking | prefix > segment prefix > substring; original order inside a group; all words must match; case; empty query → every index in order; label matches count | unit, pure |
| E2 | §3.1 threshold | 15 options → a box before that select, 14 → none, counted from what the page drew | page run |
| E2 | §3.2–3.3 filter | typing `opus` hides non-matches, keeps the three sentinels and the selected option in place, orders the rest | page run |
| E2 | §3.4 Enter | Enter posts the first MATCH through `save()` (same message shape as a mouse pick) when the selected option and a sentinel both sit above it; zero matches → nothing posted, no throw | page run |
| E2 | §3.4 routing | Enter on a 15+ option PROMPT picker posts the `prompt` message a mouse pick posts, not a `setting`; a disabled picker's box is disabled and Enter posts nothing | page run |
| E2 | §3.4a repaint | type a query, focus held (a `focus` editing message names `search|…`); a page run again with the stored state re-applies the query; tabbing setting → search posts no release | page run |
| E2 | §3.4–3.5 Escape | typing then Escape: box empty, nothing hidden, `options` in the recorded original order; ArrowDown focuses the select | page run |
| E2 | §3.6 bundle | the minified panel page still defines `rankChoices` where it calls it | bundled page |
| E3 | §3.14 coalesce | three calls during a run → one trailing run; each promise resolves after a run that started after it; a throwing run rejects its callers and the next run still runs; **a caller arriving as the active run completes gets a run that started after its call** | unit |
| E3 | §3.8 map | start mints an id and records seq/slot/start; settle removes in `finally` even when the work throws; `oldestMs` is the oldest entry's age; dispose empties it | unit |
| E3 | §3.9–3.10 host | a setting settles after `run()` AND `render()`, `ok:false` when either rejects; a command settles when `run()` returns; `busy` to every slot after start and after settle; `settled` only to the poster; `ready` answered to that slot | provider over fake slots |
| E3 | §3.7 mark | a change: nothing at 499 ms, bar + `aria-busy` at 500 ms, both gone on `settled` for that seq (`ok` true or false); settled at 300 ms → never shown | page run, fake clock |
| E3 | §3.11 painted age | painted with `{count:1, oldestMs:400}` → bar at +100 ms, not +500; `{count:1, oldestMs:900}` → bar immediately; `{count:0}` → never | page run, fake clock |
| E3 | §3.12 ready | the page's last post is `ready`; a `busy` answer replaces the painted timer | page run |
| E3 | §3.13 replacement | page A posts seq 1; a second page is run as the replacement with the host's snapshot; `settled` seq 1 to it changes nothing; `busy {count:0}` clears its bar — **ends with no bar and no `aria-busy`** | page run, two pages |
| E2/E3 | harness | `querySelector` answers `null`; an unparseable selector answers nothing; `appendChild` moves rather than copies; `hidden` is per live option; a timer runs only when the clock passes it | harness tests |

Teeth: each page test is checked by deleting the line it guards (the `hidden` assignment, the index map, the
`send()` routing, the `max(0, …)` subtraction, the `finally`) and watching it go red. The E1 tests are watched red
on `1056aed9` before any fix.

## 7. Growth surfaces

- **Page memory:** the `Map` of unsettled `seq` → its control and timer, plus one host-busy timer. Bounded by what
  one person clicks while the host is busy; emptied by `settled`, dropped whole when the document is replaced. A
  page that never hears back (a host crash) keeps its mark — the true state; the next paint starts from the host's
  snapshot, which is then empty.
- **Host memory:** `InFlight` — one entry per tracked operation (`id`, `seq`, slot, start time). Removed in a
  `finally`; bounded by one person's clicks; cleared on `dispose()`. The ranking's index map lives in the page.
- Nothing on disk, nothing in a database.

## 8. Follow-up, not built here

The same mark on the other webviews, each with its own message loop: `chatPanel.ts` (a turn already shows its own
state), `roundsLogPanel.ts`, `bugzReviewPanel.ts` (has its own in-flight map), `rolesPanel.ts`, `phrasesPanel.ts`,
`commandsPanel.ts`, `chatPresetsPanel.ts`, `notificationsPanel.ts`, `helpPanel.ts`, `bugsKeysPanel.ts`,
`chatRestorePanel.ts`. `busyMark.ts` is written to be included by any of them. The server-side refusal of an endpoint
row with no model (§4) belongs with it or on its own.

## 8a. What shipped differently (kept as each epic lands)

- **E1.** The new `keysBody` decision (`asksAnEndpoint`) also stopped asking a `local` row with its engine's address
  for a key — the old `enabled && baseUrl` count did; a local engine needs none. Pinned by a fifth keys test. An older
  test (*"a reviewer that does not run needs nothing"*) was reversed by the operator's decision, not kept.
- **E2.** `FOCUS_ID` (`panelView.ts`), the guard that lets a focus id into the page script, had to gain an optional
  literal `search|` prefix — the plan named the box's identity but not this guard, and the caret test went red until
  it did. A prompt picker's identity is `prompt|role|round|` (four parts) so the same pattern admits it.
  Enter on a PROMPT picker is not page-tested: no shipped role has 15 prompts, so no prompt picker gets a box; its
  routing is the select's own `change`, which the setting case proves is what Enter dispatches. The harness's
  `removeChild` had to model a DOM's selectedness (removing the chosen option moves the choice) — without it the
  teeth check showed the value re-apply could be deleted with every test green. `gateModelPickers.test.ts` had been
  assigning a model no picker offered; the stricter select refused it. Added after review, not in the plan: Enter
  leaves a `searchFocus` note so the next document puts the caret back in the box (own review — the pick's focus
  release meant the caret landed nowhere), bounded by `RETURN_TO_BOX_MS`; and an unchanged answer moves no option (code
  round, local). The round's other performance findings were rejected on a measurement (20–160 µs per keystroke).

## 9. Definition of Done

- [ ] §3.1–§3.16 each have a test that was watched failing first (E1) or written before the code (E2, E3), and was
      proven to have teeth.
- [ ] Three branches, three commits, three `review_code` rounds — each epic's suite, typecheck and lint green from
      exit codes; `plan-lifecycle`, `pin-check`, `adapter-check` and `rules check` clean at the end of each.
- [ ] No select builder, command handler or post site was edited to gain the search or the mark — the one road in
      did it.
- [ ] `research/module_extension.md` and `research/module_tests.md` describe what shipped; this plan promoted.
- [ ] Code gate `proceed` per epic; PRs merged with every thread answered; extension release cut after E3.
