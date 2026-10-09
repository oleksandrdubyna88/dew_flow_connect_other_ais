# PLAN — who on the team is spending it: the Team server tab

> Status: **plan only, nothing implemented yet, 2026-10-09.** Scope: a new admin-only **Team server** tab on the
> extension's Review rounds page (`src_vs_code`), two additive answers from the Team server (`src_server`), and the
> removal of the dead *Company* toggle from the spending tab.
>
> Rewritten 2026-10-09 from the earlier "Company view in the spending tab" plan (extracted from story 3.3 of
> [PLAN_team_server.md](../research/PLAN_team_server.md)): the operator approved a mockup of a dedicated tab in the new
> Settings design and asked for it to be built. The old plan's decisions that still hold are kept below and say so.
>
> Related docs: [module_team_server.md](../research/module_team_server.md),
> [module_extension.md](../research/module_extension.md),
> [RESULTS_what_spends_the_subscription_2026-10-09.md](../research/RESULTS_what_spends_the_subscription_2026-10-09.md).

## The symptom

An admin of a Team server cannot see **who** spent the company's subscriptions. The operator, 2026-10-09: *"for admins,
add a Team server tab with statistics — who (we know who signed in) spent how much. Like 'What each AI has used', but
for the company."* Three things stand in the way today:

1. **The company view is unreachable.** The spending tab's *Company* toggle (`usageScopeControl`,
   `src_vs_code/src/teamServerView.ts:161-169`) emits `data-command="teamUsageScope"` with no id. The Review rounds page
   sends it as `{command:'teamUsageScope', id:null}`; `logCommandOf` drops an empty id (`roundsLogMessages.ts:151`) and
   has no case for it anyway (`:155-174`). Only the sidebar's switch knows it (`panelProvider.ts:2485-2494`), and
   nothing posts there any more — so `usageScope` stays `'me'` and `scope=company` is never asked
   (`panelProvider.ts:3747`). The help (`team-servers`, `helpContent.ts:~281`) promises "Show the whole company".
2. **Team-server figures on the page go stale.** They are fetched only from the end of the sidebar's render
   (`panelProvider.ts:1331`), which returns early when no surface is held; a window press on the page
   (`setUsageWindow`, `:697-701`) does not reset the 60 s freshness the sidebar's own press does (`:2325-2332`). With
   the sidebar closed, the page never refreshes them.
3. **The server answers less than the tab needs.** `GET /api/usage?scope=company` (`src_server/src/Usage/UsageEndpoints.cs:23-59`)
   answers `vendors[]` and `people[{email, vendors[]}]` sorted by email (`UsageTotals.cs:136-141`) — no display name,
   no model per row (the ledger line HAS one, `UsageReader.cs:16-28`, folded away at `UsageTotals.cs:113`), no per-day
   series. Sessions (`SessionRecord(Email, Name, CreatedUtc, ExpiresUtc, LastUsedUtc)`, `src_server/src/Sessions.cs:17`)
   are listed by no endpoint, so "last seen" and "signed in but spent nothing" cannot be shown.

## The approved design

The operator approved the mockup on 2026-10-09 ("мне нравится. делай"): a private page,
<https://claude.ai/artifact/HndMh2uszn9nfFq37uGNTP>, with a standalone copy kept outside the repository. It is the
specification for the screen; what it cannot show is fixed here.

- A **Team server** tab after *What it keeps missing*, marked `admin`, present only when at least one configured
  Team server's catalog says this caller is an admin (`Catalog.isAdmin`, `teamServerApi.ts:107`).
- A toolbar: the server (when more than one is admin), window chips **Today · Week · Month · Year** (the server's own
  windows, trailing and UTC — labelled so, since the local spending tab's *Today* is local midnight), *Read <time>* and
  **Refresh**.
- Badges: the server's version and health, *you are an admin*, and any count the server reports as unreadable.
- A summary strip: **Launches**, **Tokens** (in · out), **~ List price** (an estimate, never a bill), **People**
  (active, plus those signed in with no runs).
- Two columns from 1100 px, one below: **People** (search, sort by ~$ / launches / tokens / last seen; a card per
  person — launches, tokens, ~$, last seen, a per-vendor bar; expanded: a per-vendor table and the failures) and
  **Vendors** (a card per vendor with its models, launches, tokens, ~$), then a **30-day launches-per-day** chart.
- A collapsed *Signed in, no recorded runs in this window* list.
- Notes: what *~$* is, and that *launches* are not rounds.

**What the mockup shows that this plan does not build**, each with the plan that does: rounds, retries, repairs,
cancelled and budget refusals, cached share, the 5 h / 24 h windows and the vendor quota gauge are
[PLAN_every_round_is_counted.md](PLAN_every_round_is_counted.md)'s (its epics 3, 5 and 6) — this tab is where they
appear when that plan's fields arrive. Until then the tab counts **launches** and says so: the ledger has no round id,
and grouping launches by day or person yields launches, not rounds (the question consultant, 2026-10-09).

## Decisions

| # | Question | Decision | From |
|---|---|---|---|
| D1 | A non-admin | sees no tab at all; the server's `403` on `scope=company` is the decision, the catalog flag only hides the tab | kept from the earlier plan |
| D2 | Two casings of one email | one person — the server already groups case-insensitively (`UsageTotals.cs:136-141`); the client never regroups on the raw string | kept |
| D3 | A person with no runs in the window | not a card; listed in the collapsed *signed in, no recorded runs* group (the operator approved the mockup that shows it — this replaces the earlier "absent") | changed |
| D4 | Per-person ~$ | the server adds a per-MODEL breakdown under each vendor total; the client prices each model at its public list price (the existing price book), shows a dash for a model with no price, and marks a partly priced total as a floor. Never the vendor's *current* model — that is not what ran | consultant, verified against `priceOfLine` (`usage.ts:266-297`) |
| D5 | Who signed in | a new admin-only `GET /api/people` projecting unexpired sessions to **email, display name, last used** — never tokens, session ids, creation or expiry. Read from the files directly (`Validate` would write and delete). A caller on a raw identity-provider token has no session file, so the list is "people with an unexpired session", said in the UI | consultant; the projection shape follows the listing in the sibling credentials server |
| D6 | The *Company* toggle | removed from the spending tab, with its state and the sidebar case; the new tab owns the company view; help aligned | consultant, verified (`roundsLogMessages.ts:151`) |
| D7 | The chart's data | an additive `daily` object on the company answer — its own UTC range (last 30 days, whatever the chosen window), ordered day buckets per vendor, from the same single ledger scan; absent on an older server = no chart | consultant; the precedent is `kinds`, added the same way |
| D8 | An older server (no `models`, `daily`, `/api/people`) | every missing piece says *needs Team server ≥ <version>* in its place; never a zero | the rule of the two halves shipping apart |

## Boundaries with the open plans

| Item | Built by | The other plan's part |
|---|---|---|
| Rounds, retries, repairs, cancelled, budget refusals, cached share, 5 h / 24 h windows, vendor gauges | [PLAN_every_round_is_counted.md](PLAN_every_round_is_counted.md) (epics 3, 5, 6) | this plan builds the tab they render into; its 5.2 targets this tab, not the old Team-server block |
| What a token column means per vendor | [PLAN_usage_that_compares.md](PLAN_usage_that_compares.md) | this plan prices per model from the server's breakdown and compares nothing across vendors |
| The tab strip's ARIA | [PLAN_the_tabs_announce_themselves.md](PLAN_the_tabs_announce_themselves.md) | the new tab button follows the strip as that plan leaves it; neither waits for the other |

## Epics and stories

Three epics, each on its own branch from the previous epic's commit and ONE commit; the coai gate once per epic.
Every surface story carries its docs and its help text in all five languages (`HELP_LANGUAGES`, `helpContent.ts:32`;
`test/helpCoverage.test.ts` enforces them).

### E1 — the tab, on today's server (extension only) · `feat/team-tab-e1`

- **1.1 One owner fetches, the page's visibility asks** (Opus) — the sidebar's `PanelProvider` stays the ONE owner of
  Team-server requests (catalog and sign-in refresh stay shared), but demand no longer comes only from the sidebar's
  render (`panelProvider.ts:1331`): the Review rounds page being open asks too, every 60 s and at once on a window
  press. Usage is cached per **(server, scope, window)** — today one `usage` per server (`panelProvider.ts:393-399`)
  lets a `me` answer and a `company` answer overwrite each other — each key with its own sequence number, checked
  BEFORE the cache is written; a changed selection schedules its request even while another is running (today the
  `refreshing` early return at `:3788` lets the old request stamp everything fresh for 60 s). RED first:
  `ThePageRefreshesTeamUsage_WithTheSidebarClosed`, `AWindowPress_ReasksTheServer`, `AnAnswerForAnOldWindow_IsDropped`
  (out of order), `MeAndCompany_KeepTheirOwnTotals` (a delayed `me` after `company`), `TodayThenYear_DuringARefresh`.
- **1.2 The company answer, typed** (Opus) — `Usage.people?: PersonUsage[]` and `unreadableLines?`
  (`teamServerApi.ts:160-165`); for a server whose catalog says admin, the tab asks `scope=company` and the spending
  tab keeps asking `scope=me`; the dead *Company* toggle (`usageScopeControl`, its state and the sidebar case) goes
  (D6). Tests over a body the real server produced; a `403` (admin list changed) shows *no longer an admin here*.
- **1.3 The tab** (Opus) — a new module `teamServerTab.ts` (pure renderers: toolbar, summary, people, vendors, idle,
  notes) styled with the new Settings tokens — `TOKENS` and the chip / card rules become an exported shared sheet
  (`catalogCss.ts:10-23`, `:125-140`) instead of a copy — and vendor colours from `vendorPalette`
  (`vendorColour.ts:164`). Registered on the page: the tab button (`roundsLog.ts:1528`), its section beside the others
  (`:1558-1561`), `WAITING` (`:1590`) and the 15 s never-received list (`:2294`), a `Region` (`pushLedger.ts:23`,
  `roundsLogPanel.ts:262-282`), and its commands — window, refresh, search, sort, server — in `LogCommand` / `WORK`
  (`roundsLogMessages.ts:50-69`, `:216-225`; the mapped type makes a missing one a compile error). Search and sort
  stay on the page (no round trip), and so does every piece of interaction state: the search text and focus, the sort,
  the selected server and each expanded card (keyed by server plus normalised email) survive a push — the region is
  replaced the way `replaceKeepingFolds` already does for the consultations tabs (`roundsLog.ts:2203`), and the
  toolbar is not replaced at all. The Settings sheet is shared by EXTRACTION: its tokens and chip rules move to a module
  both import, inserted in the same order, and a test asserts `CATALOG_CSS` is byte-identical before and after; the
  Settings cards' four-row subgrid stays the Settings page's — this tab's cards have their own layout, scoped to its
  section.
- **1.4 Proven on the page** (Opus) — a bundled-page test on `roundsLogPageHarness.ts` that RUNS the page: an admin
  sees the tab and its people; a non-admin page has no tab button and no section; a `people` push renders cards;
  search narrows; a push keeps the query, the order, the focused search box and an expanded card (switching servers
  with the same email included); *needs Team server ≥ …* shows where `models` / `daily` / people listing are absent
  and no zero does. Help: the `the-rounds-log` article (it lists the tabs by name) and `team-servers` (the toggle sentence) in all
  five languages. Docs: `module_extension.md` (the rounds-log page and the spending tab paragraphs).
- **1.5 End to end, over a real server** (Opus) — a `*.contract.js` case in `npm run test:contract`, which starts the
  built `coai-server` on a loopback port: an admin's company fetch through the real client, a window change re-asking,
  a non-admin's `403`; an older-server body for the fallback. (The `/api/people`, `models` and `daily` assertions belong
  to E2, story 2.3, where those exist.) The flow gets its row in `research/module_tests.md`'s catalogue with what it does NOT prove (a host cannot
  read a webview, so the page itself is the bundled harness's).

### E2 — the server says who and with what · `feat/team-tab-e2`

- **2.1 `GET /api/people`** (Fable) — admin-only (`RequireCaller` plus the inline admin check and `403`, as
  `UsageEndpoints.cs:46`); reads `sessions/*.json` directly like `Sweep` (`Sessions.cs:121-142`) without writing;
  one row per case-insensitive email: `email`, `displayName` (latest non-empty), `lastUsedUtc` (max over unexpired
  sessions); sorted by email; DTO registered in `ServerJsonContext.cs:199-218`. RED: a non-admin gets `403`; an
  expired session is not listed; two sessions of one person are one row; no field beyond the three is on the wire.
- **2.2 Models and days on the company answer** (Opus) — additive, company scope only: `VendorTotal` gains
  `models[{model, runs, failed, tokensIn, tokensOut}]` (from `UsageLine.Model`); the answer gains
  `daily{fromUtc, toUtc, days[{day, vendors[{vendor, runs}]}]}` for the last **30 UTC calendar days including today**
  (day buckets, half-open). Today the reader keeps only the SELECTED window (`UsageEndpoints.cs:56` →
  `UsageReader.cs:96`), so a chart built from it on *Today* would show one day: the read covers the UNION of the two
  ranges in one scan, and the summary and the chart are aggregated from it separately. RED: company/Today with one
  launch yesterday and one today — the summary counts one, the chart two; company/Year with a 60-day-old launch — the
  summary includes it, the chart does not. `me` scope unchanged. `http/usage/usage.http` asserts both; an old line with no model groups under `""` (unknown).
- **2.3 Contract, release and deploy** (Opus) — the contract suite asserts `/api/people` (admin 200 with exactly three
  fields, non-admin `403`) and the `models` / `daily` fields over the real server; server release; the manual
  `deploy-server.yml` dispatch with the owner's approval; live: `/api/people` answers an admin and refuses a non-admin.
  `module_team_server.md` (routes table, story 2.4 section, *What is on disk*).

### E3 — the tab uses it · `feat/team-tab-e3`

- **3.1 Who and when** (Opus) — names and *last seen* on the cards from `/api/people`; the collapsed
  *signed in, no recorded runs* list (D3, D5); people joined by case-insensitive email, every usage person kept.
- **3.2 ~$ per model** (Opus) — each person's and vendor's ~$ from `models[]` through the existing price book
  (`PRICE_BOOK.priceOf`, `priceBook.ts`), cached rate when the server reports cached tokens (it does not yet — the
  every-round plan's 3.1), a dash for an unpriced model, a floor mark when part is unpriced (D4).
- **3.3 The chart** (Opus) — a 30-day launches-per-day stacked bar chart from `daily`, drawn to scale with theme
  tokens, its axis labelled *launches*.
- **3.4 Proven and released** (Opus) — bundled-page tests for each against a new-server body AND an old-server body
  (D8); help and docs; the extension release; live, against the deployed server: an admin's tab shows names, last seen
  and the chart.

## Build order

E1 → E2 → E3. E1 ships alone and is useful against today's 0.10.0 server (people, vendors, launches, tokens). E2 is
server-only and additive, so E1's client meets it without change. E3 needs E2 deployed to be seen, and degrades to
E1 without it.

## Risk (for the gate's `riskItems`)

1. **E2 · 2.1** — a new endpoint that lists people. A wrong admin check exposes every colleague's email and activity
   to any caller. Guards: the same inline admin check as `/api/usage`, a `403` test, a test that pins the three-field
   wire shape.
2. **E1 · 1.2** — removing the *Company* toggle touches the sidebar's command list and the spending tab every
   person uses. Guards: the compile-time exhaustiveness of `PANEL_COMMANDS` and `WORK`, the spending tab's own tests
   unchanged.

## Growth

| Surface | Size | Retired by | Interrupted |
|---|---|---|---|
| `/api/usage` company answer, `models` + `daily` | `daily` is 30 days × vendors (≤ 3 today) ≈ 90 small objects; `models` ≤ vendors × models (≤ 6) | per request | a read; nothing stored |
| `/api/people` | one row per person with an unexpired session (≈ 5 today) | sessions expire (7 days) and `Sweep` removes them | a read of files that may be swept mid-read: a vanished file is skipped |
| The tab's state on the page | the last answer per server | the page | refreshed on open and every 60 s |

Nothing new is written anywhere.

## Test plan

RED first for the two defects (1.1, 1.2). Pure renderers in `test/teamServerTab.test.ts`; the page itself through the
bundled harness (`roundsLogPageHarness.ts`, as `bundledPage.test.ts:1185-1214`); server tests in
`src_server/tests/UsageTests.cs` and a new `PeopleTests.cs`; the `http/` contract suite for both endpoints. Then every
suite: `npm test`, the `src_server` and `src_mcp` test executables, `npm run test:contract`.

Cases that must exist: an admin sees the tab, a non-admin does not; `Alice@Example.com` and `alice@example.com` are one
person; a `403` after the admin list changed; an older server shows *needs Team server ≥ …*, never 0; search by name
and by email; a model with no list price is a dash and its person's total a floor; the chart's bars sum to the day's
launches; `/api/people` returns exactly three fields and refuses a non-admin.

## What the plan round changed (2026-10-09)

Codex, `proceed`, three findings: stale window answers must be dropped (accepted — 1.1); no end-to-end flow was
required (accepted — 1.5); the boundaries were said to be one-sided (rejected — they were written into all three
neighbouring plans in the same commit, which the reviewer, reading the plan text alone, could not see).

The cadence consultation for epics 1–3 (codex), each point checked in code: one usage per server lets `me` and
`company` overwrite each other and a selection changed mid-refresh is lost (1.1 now keys the cache by server, scope
and window, with one owner); a 30-day chart cannot come from a read that keeps only the selected window (2.2 reads the
union); a push would erase an expanded card and the search (1.3 keeps interaction state on the page); two live checks
ran ahead of what they need (moved to 2.3 and 3.4).

## Definition of Done

- [ ] An admin sees the Team server tab with people, vendors and launches against today's server; a non-admin sees no tab.
- [ ] The page's Team-server data refreshes with the sidebar closed, and a window press re-asks.
- [ ] The dead *Company* toggle is gone and the help says where the company view is.
- [ ] `/api/people` and the `models` / `daily` fields are live, admin-only where they must be, and contract-tested.
- [ ] Names, last seen, the idle list, ~$ per model and the 30-day chart appear against the new server, and each says
      what is missing against an old one.
- [ ] Every RED test went red with the real symptom first; all suites green; the contract case runs the flow end to end
      and `research/module_tests.md` catalogues it.
- [ ] `module_extension.md`, `module_team_server.md`, the five help languages and the README rows match; this plan is
      promoted with its deviations.
