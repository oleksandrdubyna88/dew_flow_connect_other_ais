# PLAN — E6: the report, `coai-mcp --round-authors`, over live rounds (stories)

> Status: **plan only, nothing implemented yet, 2026-10-14; revised 2026-10-16 against `main` at `2d98f2f8`, and 2026-10-18 against `main` at `9a0a5f2e` with the points E3/E4/E5 owe it (§3).** Epic E6 of [PLAN_a_round_records_who_wrote_the_code_and_how_much.md](PLAN_a_round_records_who_wrote_the_code_and_how_much.md).
>
> Scope: `src_mcp` only — a read-only one-shot mode beside `--round-counts`, its reader, a pure population unit, a pure
> counting unit, the JSON of sums and `--add`, the table. Nothing is written to any store; nothing on the extension or the
> Team server. Related docs: [module_server.md](../research/module_server.md) (*`--round-counts`*, the shape followed),
> [module_tests.md](../research/module_tests.md).

## 0. What every story stands on

**The parent's §8 is the contract; this document only splits it and closes the gaps §8 leaves.** Read §5–§8 of the parent
before any story. E6 carries no cornerstone AREA (the parent's `## Epics`, *"an arithmetic §8 fixes as exact-cell tests"*), so
no story here is marked; the parent's ledger reads `Cornerstones: 3 of 3 — E2.S1, E3.S4, E4.S2` (parent §16 D21).

**Inputs E6 reads, and from whom.** E6 depends on E5.S2 (schema step 24 — array index 23, `user_version` 24; step 22 is the
size, #829, released in mcp 0.56.0; step 23 the plan source, #841, in mcp 0.58.0), on E4.S4 for contributors and on E5.S6 for
`shared/model-display-vectors.json`. Steps 22 and 23 are SHIPPED: `Schema.Steps` holds 23 entries ending in `TheRoundsKey`,
`TheRoundsSize`, `ThePlanSource` (`Schema.cs:31-38`; step 22 `:635-652`, step 23 `:667-669`). Step 24 is PLANNED: its names below
are E5's as of `c1216b42` (E5 doc §5 E5.S2) — **the coder of E6.S1 opens step 24 as E5 shipped it and uses ITS names**; where they
differ from this list, the shipped schema wins and the story's summary says so (§4 lists what may still move).

| Read | From | Columns this document reads |
|---|---|---|
| `round_changes` + `rounds.size_state`, `delta_state`, `delta_from`, `size_origin`, `size_gitlinks`, `base_ref` | **shipped**, step 22 (`Schema.cs:635-652`, writer `RoundChangesTable.cs:27-39`) | read through the shipped reader `RoundChangesLog.Read(db, ids)` (`src_mcp/src/Store/RoundChangesLog.cs:23-34`) → `RoundSize` per round: `Whole`/`Delta` `ScopeSize(State, From, Cells)`, cells `SizeCell(Coverage, Kind, Files, Added?, Removed?)` (`core/Context/RoundSize.cs:39-52`), `Added`/`Removed` null exactly for `binary`; `Whole.From` is `rounds.base_ref`. A round with `size_state = ''` is absent from the map (not recorded). States are `ScopeState`'s constants (`RoundSize.cs:18-29`) |
| `reviewers.effort` | **shipped**, step 22 (`Schema.cs:651`) | `not recorded` = the column default for every row before step 22; `''` = the launch applied none, written by `RecordReviewers` (`RoundsDb.cs:950-952`) (D7, parent §16 D11) |
| `round_authors` | E5.S2, step 24 (planned) | `round_id, ordinal, who (main\|subagent), agent ('' for main — E5 maps E4's `AgentKey.MainId = "main"` at the column), requested, model, effort, agent_type, association, window (before-open\|round — the round's `(stage, number)` is its `round_id`'s), launches, turns, input_tokens, output_tokens, cache_creation_tokens, cache_read_tokens, write_calls` |
| `rounds.authors_state`, `authors_session`, `uncovered_writes`, `ambiguous_agents`, `ambiguous_launches`, `ambiguous_turns`, `ambiguous_{input,output,cache_creation,cache_read}_tokens` | E5.S2, step 24 (planned) | state `''` (not recorded) \| `observed` \| `partial: r1, r2` \| `unobserved: r` — `AuthorsSnapshot.Render()` (E5.S1); a value `AuthorsSnapshot.IsWellFormed` refuses reads as not recorded (D19). Every `ambiguous_*` is `-1` for E4's `ActivityTotals.Unknown` (an unobserved round), never 0 (D17); `uncovered_writes` is `-1` when no attribution ran (E5's `AuthorsSnapshot.Unknown`, or an `unobserved` result of E4) — *unknown*, never *covered* (D12). Only rounds E4 calls SETTLED have a state: a skipped, interrupted, running, feature or document round keeps `''` (E5 §3 *Which rounds*) |
| `rounds.authors_id_source`, `authors_reader`, `authors_attribution`, `submodule_writes` | E5.S2, step 24 (planned) | **not read.** No decision here depends on them: the id source (`registry` \| `environment` \| E7's `stored` \| `healed`) changes nothing in a count — a `healed` round is read exactly like a `stored` or a live one, because its columns and rows ARE the database's own (E5 §10 item 13 (iv)); the two versions are E7's; `submodule_writes` is a subset of `uncovered_writes` (parent §7), so its `-1` needs no rule here |
| `rounds` (existing) | `Schema.cs:52-78` | `id, session_id, stage, number, status, verdict` (`skipped` = a skip, `SessionStore.cs:121`, D1), `started_utc, accepted` (`-1` = `resolve` never ran, `:71-72`), `gating` (the gate's own count — the `gate_count` numerator, D8) |
| `sessions` (existing) | `Schema.cs:45-50` | `id, repo_path, branch` |
| `reviewers` (existing) | `Schema.cs:80-88`, `model` step 20 (`:598`) | `round_id, provider, status, model` |
| `findings` (existing) | `Schema.cs:90-116` | `round_id, is_gating, re_raised, resolution` (`accept`\|`reject`\|`''`, written by `RecordDecisions`, `RoundsDb.cs:763-778`) |

The authors are also a nullable snapshot on `RoundRecord` (parent §7; E5.S1's `AuthorsSnapshot`), but that does not change what
E6 reads: the report reads the projection (`coai.db`), as `--round-counts` does, never the session files. It changes how the TESTS
seed a store: through `RoundsDb.RecordRound` (`RoundsDb.cs:190-202`) with the record's `Size`, `Gitlinks` and `Authors` set, i.e.
through the real writers, never hand-written `INSERT`s (`testing.md`, *a fixture the code rejects proves nothing*). Hand SQL is
allowed for exactly one thing: a database stopped at an OLDER step, built as `RoundCountsCliScenarioTests.cs:139-152` builds one —
the stop index found by content (`Array.IndexOf(Schema.Steps, Schema.TheRoundsSize)` for a step-21 file,
`Schema.ThePlanSource` for step 22, E5's `Schema.TheRoundsAuthors` for step 23), never a literal `n`.

**The shape followed.** `--round-counts`: the mode `src_mcp/src/Server/RoundCountsMode.cs` (88 lines — `Run` `:39`, argument parse,
exit codes 0/65/74 never 64 `:18-25`, data directory `:51`, the unreadable store `:62-68`), the reader `src_mcp/src/Store/RoundCounts.cs`
(a missing file is an empty answer `:94-98`, read-only open `:100`, a column asked for before it is selected `:102`), the
registration `Program.cs:297,339,441-444`, the help `Program.cs:2457-2461`, the JSON context `ServerJsonContext.cs:363`, the
one-shot list `.agents/PROJECT.md:53`. **And the shipped `--log` readers** (`RoundChangesLog.cs`, `RoundPlanSources.cs`, E5.S3's
planned `RoundAuthorsLog.cs`): one `HasColumn` guard per schema step on that step's marker column (`RoundsQuery.HasColumn`,
`RoundsQuery.cs:910-918`), never a `user_version` guess; ids bound as ONE parameter by `PageIds.Bind` (`PageIds.cs:15-16`) and
unpacked by `json_each`, never SQL built from data; every row joined by its OWN `round_id`, never by position (`PageIds.Of`'s rule,
`:23-24`); a malformed stored value read as absent per ROW, never by a catch around the query (`RoundPlanSources.cs:17-21`). The
file layout follows `Store/GateHistory{Query,Rules,Models,Text}.cs` (one unit per job, 112–316 lines each):

| New file | Story | Job |
|---|---|---|
| `src_mcp/src/Store/RoundAuthorsModels.cs` | S1 (S2–S4 add) | records only |
| `src_mcp/src/Store/RoundAuthorsQuery.cs` | S1 | the read-only read → facts |
| `src_mcp/src/Store/RoundAuthorsPopulation.cs` | S2 | WHICH rounds, which cohort, why each excluded one is out — pure |
| `src_mcp/src/Store/RoundAuthorsCells.cs` | S3 | HOW they are counted — sums, figures, marks, header — pure |
| `src_mcp/src/Store/RoundAuthorsMerge.cs` | S4 | the JSON of sums and `--add` |
| `src_mcp/src/Store/RoundAuthorsText.cs` | S5 | the `--table` rendering |
| `src_mcp/src/Server/RoundAuthorsMode.cs` | S5 | the flags and exit codes |
| `src_mcp/tests/RoundAuthorsSeed.cs` | S1 (all reuse) | seeds a store through the real writers |

`Program.cs` (2 519 lines) gains registration lines only. Every new file stays under 400 lines; a pure unit is `static`,
reads no clock and no disk.

### Decisions this document takes where §8 is silent

Each is a rule the stories implement and a test pins. A reviewer who disagrees argues with the line here, not with the code.

| # | Decision | Why |
|---|---|---|
| D1 | **Cohort is decided over ALL reviewed rounds of the gate session's stage, before the window and before any exclusion.** A *reviewed* round has status `done` and a verdict that is not `skipped` — the predicate the shipped delta rule uses (`DeltaPredecessor.IsCandidate`, `src_mcp/src/Server/Rounds/DeltaPredecessor.cs:45-46`; a skip is written `done` WITH a sha and reviewed nothing, parent §16 D1), and, within the two review stages, exactly E4's *settled* (`Windows.IsSettled` = `IsRecorded` ∧ `done`, E4 §S2.3): one predicate for cohorts (here), contributors (D2) and the authors that exist (E5 §3). It needs `Verdict` and `Status` on every fact (S1 reads both). `first` = the lowest-`number` reviewed round; every later reviewed round is `corrective`. A skipped round has no cohort (excluded `skipped`, D12). An excluded first round does not promote round 2; a first round before `--since` leaves round 2 `corrective`. | otherwise a filter changes what "new code" means — and a skipped round 1 would make the first real review "corrective" |
| D2 | **Contributing rounds of round R** = the SETTLED rounds (D1's predicate) of R's gate session, both review stages, whose `started_utc ≤ R.started_utc`; **contributors** = their `round_authors` rows, rows with `window = before-open` only under `--with-before-open`. A skipped, interrupted, running, feature or document round is never a contributing round: it opens no window (E4 §S2.3, case `c16`) and has no state (E5 §3), so counting it would make every round after a skip *not recorded*. Computed by E4.S4's one implementation (`Contributors.Of(upToN, withBeforeOpen)`, E4 doc as of `30362785`, §E4.S4) over the stored rows mapped to its input type by D18 — never a second union here (parent §6, §16 D2). | §6 *"the union of the windows up to and including round N"*; §6 does not say whether plan rounds' windows count — D2 says they do (the coding between plan `proceed` and code round 1 sits in a plan-tiled window) |
| D3 | **Authors state of R for the report = the WORST state over its contributing rounds**: not recorded < unobserved < partial < observed. Not recorded or unobserved → excluded; partial → included and counted. **`partial: peer running` is partial** (E4 G3): ownership was decided while a same-caller peer round was still running; it is PROVISIONAL, E7's backfill finalises it once every overlapping peer round is final (`done` or `interrupted`, E4 §S2.3), and the cell counts such rounds beside (`provisional`). **The stated limit (E4 G9, E5 §10 item 13 (i)):** the state read is each round's ROLL-UP over its `before-open` and `round` windows — per-window states live in the session-file snapshot only and step 24 does not project them — so with `before-open` left out (the default) a round whose before-open window alone is partial or unobserved (e.g. `unobserved: no open time`) is still partial or excluded. The error goes one way: toward exclusion, never toward a claim. A later schema step can project `Windows` from the files with no backfill (§4 item 9); E6 reads no session file. | a union with a hole is not an observed union |
| D4 | **Coders** = contributor rows with `who ≠ main` and association `wrote here` (§8 *"families of `wrote here` subagents"*). Family from the OBSERVED `model` (§5; E4.S3: a row's `Model` is the model that ANSWERED, `Requested` the one asked for) through `shared/model-display-vectors.json` (parent §16 D19, written by E5.S6, every vector `{ model, display, family }`) — the vectors the card reads too; empty or `unknown` → `unknown`; a model the vectors do not know → `other`. **`unknown` is not a family** (E4 G10): E4.S3's launch-only row and its two-repositories row both carry `Model = "unknown"` — write or launch evidence, no model claim. Coder mix over the KNOWN families (`other` included): one → it; several → `mixed`; then `+unknown` appended when any coder row is `unknown` (`sonnet+unknown`, `mixed+unknown`); only `unknown` coders → `unknown`; no coder and a `main` row `wrote here` → `main only`; none at all → `no write observed`. A REQUESTED alias (`sonnet`) maps to a family only when it equals a family word the vectors file names; any other non-empty value goes through the id rule (`other` when it does not parse); `''` → `unknown`. | §8 lists `sonnet, opus, fable, mixed, main only`; a Bash-only change has no `wrote here` row and needs a name; the suffix keeps `unknown` visible as D5 does, without calling a Sonnet round `mixed` |
| D5 | **Coder effort** = distinct `effort` of the coder rows, `''` and E4's `unknown` word both → `unknown`, sorted, joined `+` (`high`, `high+unknown`); no coders → `—`. **Main model** = distinct observed `model` of `who = main` contributor rows, sorted, joined `+`; none → `unknown`. | `unknown` stays visible inside a mix instead of being out-voted |
| D6 | **Reviewer set** = the DISTINCT `(provider, model, effort)` of reviewer rows with status `done`, sorted ordinally. Beside it, never in the key: failed rows, stood-down rows (`SessionStore.cs:53,56`), and `partial set` rounds (≥ 1 launched reviewer `failed`). No `done` reviewer → excluded `no reviewer answered`. | §8 *"for reviewers that answered — with failed and partial reviewers counted beside"* |
| D7 | **Reviewer effort**: `not recorded` (step 22's column default for every row written before it, `Schema.cs:651`) renders `not recorded`, and so does a file older than step 22, which has no column (`EffortRecorded = false`); `''` renders `none` (the launch applied none — every runtime but claude, a Team server row and local, `RosterBuilder.EffortFor`, `RosterBuilder.cs:721-727`; codex's effort is kept and not sent). The two are never merged, and a reviewer set keyed on `none` is not the one keyed on `not recorded`. Shipped: `RecordReviewers` binds `ReviewerState.Effort` (`RoundsDb.cs:950-951`), never `not recorded`. | parent §16 D11: E2.S3 writes the explicit default |
| D8 | **Two numerators, named apart** (parent §16 D10): **`severe`** = `findings.is_gating`, i.e. severity blocking or major (`Finding.cs:84`, written `RoundsDb.cs:1004`), BEFORE the gate's discount; **`gate_count`** = Σ `rounds.gating` (`GatingCount`, after it). No column, header or test calls either "gating". The table's column says `blocking+major`, and the header says it is not the gate's own count (`rounds.gating` = `GatingCount`, after the discount, `GateRule.cs:57-82`). | §8 calls `is_gating` the gating numerator and cites the discount; the column is pre-discount |
| D9 | **Coverage per numerator** = the rounds whose value is KNOWN, with their selected lines: all, severe, gate_count, re-raised, unresolved → every round in the cell (an unresolved finding is a real state, `Schema.cs:106-107`); accepted → rounds with `accepted ≠ -1`. Every ratio is over its own covered rounds and lines; a corrective cell's per-100-DELTA figures (D10) are over the rounds whose delta is `recorded`. | §8 *"each with its total and its coverage"* |
| D10 | **Denominator** (code stage) = Σ (`added + removed`) of `whole` cells with coverage `shown` or `elided` and kind in `--kinds` (default `code,test`) — what the reviewers read, so first and corrective cells stand on one scale. `excluded` and `binary` are reported apart and never in it. **Corrective cells ALSO carry a fixer figure per 100 `delta` lines** (same coverages and kinds — parent §6, *"`delta_*` sizing what the fixers changed"*). **Fallback for an unavailable delta (parent §16 D26):** a corrective round whose `delta_state` is `unavailable: <why>` (every `ScopeState` word but `recorded`: first round, no predecessor sha, not an ancestor, git failed, numstats disagree, base moved, base unknown, no merge base — `RoundSize.cs:20-28`) stays IN the cell and IN the `whole` figures, is OUT of the delta figures' coverage, and is counted per reason in the cell (`delta unavailable: <why> = n`); its `whole` lines are NEVER substituted into the delta denominator — that would credit the original authors' lines to the fixers, the case parent §6 exists to prevent. A `recorded` delta with no cells is an observed zero: in coverage, 0 lines. A cell whose corrective rounds are all unavailable prints its delta figures `Unknown`. **The `unknown` kind is printed**: every cell carries the lines of each kind OUTSIDE `--kinds` as *not counted* (`docs`, `config`, `unknown`), and the header says `unknown` is every extension `OutlineLanguages.Of` does not map — `.ps1`, `.sh`, `.razor`, `.html`, `.css` among them (E2 §10 item 7; `ChangeKind.Of`, `core/Context/ChangeKind.cs:46`). `--stage plan` has no diff: no size exclusion, every per-100 figure is `not applicable`. | §7 coverage table; §8 is silent on which scope a corrective cell divides by — `whole` is what the reviewers read, `delta` what the fixers changed, and D26 asks for the fallback to be stated |
| D11 | **Two flags §8's synopsis does not list**: `--kinds <k,…>` (§8 *"flags select others"*) and `--without-elided` (§7 *"so the report can drop rounds with elision"*), which moves a round with any `elided` row to the exclusion `elided`. | named here so they are a decision, not a coder's invention |
| D12 | **Exclusion reasons, first match, one per round**: `not done` (`running`/`interrupted`, `SessionStore.cs:86-88`) → `skipped` (verdict `skipped`, `SessionStore.cs:121`; D1) → `no reviewer answered` → `authors not recorded` → `authors unobserved` → `size not recorded` (`size_state = ''`) → `size unavailable` (`size_state` is an `unavailable: <why>` word — the WHOLE scope, e.g. `no merge base`; counted per reason beside the exclusion) → `binary only` → `size not covered` (`uncovered_writes > 0` — writes into a submodule or a nested checkout under `repo_path`, parent §7 / gate finding 0; skipped under `--with-uncovered`, which every per-100-line figure then includes) → `size coverage unknown` (`uncovered_writes = -1`: attribution never ran, E5 §10 item 12 — neither covered nor not covered; it reaches this check only on a `partial` round, because an `unobserved` one left at `authors unobserved`; skipped under `--with-uncovered` too, which relaxes the coverage requirement as a whole) → `no selected lines` → `elided` (only with D11's flag). The last seven are code-stage only; `size not covered` and `size coverage unknown` rounds are also counted per cell under `--with-uncovered` so the mark is visible, not hidden. | a round counted under two reasons double-counts the exclusions; `-1 > 0` is false, so without its own reason an unmeasured round would read as covered |
| D13 | **Periods**: no `--split` → one period `all`; else `before` (`started_utc < split`) and `after` (`≥ split`). The window is `[since, until)`. Both compared in ONE function, textually over the column's own `O` UTC stamps — the comparison form `RoundCounts.Inside` uses (`RoundCounts.cs:158-159`), with E6's own half-open edges (`--round-counts`' windows are `(from, to]`, a different rule for a different question). | one edge rule, one place |
| D14 | **Marks**: `small` when a cell has < 10 rounds; `dominated` when its largest branch (`repo_path` + `branch`) has MORE than half its rounds (6 of 10 is, 5 of 10 is not). Computed at render from additive sums, so `--add` recomputes them. | §8 *"under 10 rounds or one branch over 50 %"* |
| D15 | **The confounding line**: with `--split`, per cohort, every dimension (coder mix, coder effort, main model, reviewer set) whose SET of values differs between `before` and `after` is named; when coder mix AND reviewer set are both named, the header says the report cannot separate them. Without `--split`, no period comparison is claimed. Always: *association only*; the reviewer model is the one ASKED for (`SessionStore.cs:17-28`); a write is contribution, not authorship of surviving lines (parent §3); a diff can carry lines nobody observed (parent §13); **the rank allocation, in one sentence** (E4 G11): *activity that two gate sessions of one caller both see is allocated to the gate whose round completed first and labelled `claimed by peer` on the other — an accounting convention, never evidence of a repository or a model*, so no column, mark or confounding line reads the ambiguous totals as either; the roll-up limit of D3; and, when any counted round is provisional, that `peer running` rounds are counted as partial until E7's backfill finalises them. | §8 *"the header says so"* |
| D16 | **Rounding**: a figure is `Math.Round(x, 2, MidpointRounding.ToEven)` of a `double` ratio, computed ONLY at render (S3) — the JSON never carries one (S4). Test values are chosen off the tie. | one rule; the JSON is sums |
| D17 | **An unknown total is not a zero** (added 2026-10-16). E5.S2 writes `-1` in every `ambiguous_*` column for E4's `ActivityTotals.Unknown` (an unobserved round); such a round is excluded anyway (D12), but a contributing round's (D2) unknown term never enters a sum: a cell's ambiguous sums carry `UnknownRounds` beside the known totals, and a figure over them with any unknown round prints as a floor (`≥ n`). | the benchmark's `Figure` convention; E5's `-1` would otherwise subtract |
| D18 | **Stored rows → E4.S4's input** (added 2026-10-16, amended 2026-10-18). One mapping, in S1's reader, from each round's columns and rows to E4.S3's `RoundAuthors`: `CallerSessionId = rounds.authors_session`; `State`/`Reasons` parsed from `authors_state` (the kind before `: `, reasons split on `, ` — exact: E4's `NoReasonContainsTheJoiner` and E5's `E5sReasonsContainNoJoiner`); each row's `Stage`/`Number` from its round; the agent through **`RoundAuthorsTable.AgentKeyOf(column)`**, E5.S2's one inverse of its `AgentColumn` — never a second spelling of `'' → main` (E5 §10 items 7, 13 (iii)); `Ambiguous` from the `ambiguous_*` columns (`-1` → `Unknown`); the members step 24 does not project take `AuthorsSnapshot.Unknown`'s values, as E7's heal does (E5 §10 item 11 (c)): `Elsewhere`, `ClaimedByPeer` = `ActivityTotals.Unknown`, `UnobservedLaunches = -1`, `Windows = []`, `PeerGates = []`, `WritesOutsideTheGateCheckout = []`. `Contributors.Of` reads rows and the state only. | one mapping; a second union in the report would be a second implementation |
| D19 | **The shipped readers' shape** (added 2026-10-16): one `HasColumn` guard per step on its marker column — `rounds.size_state` for step 22 (`round_changes` and `reviewers.effort` arrive in the same step), `rounds.authors_state` for step 24, `reviewers.model` for step 20; ids bound by `PageIds.Bind`; rows joined by their own `round_id`; a stored `authors_state` that `AuthorsSnapshot.IsWellFormed` refuses reads as *not recorded* for THAT round, checked per row, never by a catch around a query (`RoundPlanSources.cs:17-21`). The size is read through `RoundChangesLog.Read`, never by a second `round_changes` query. | a reader that guesses the version, or catches a whole query, hides *no such column* — the defect `RoundsQuery.cs:494-501` records |

## 1. Stories

Order: **S1 → S2 → S3 → S4 → S5**, one branch, one commit for the epic, the coai gate once (parent `## Epics`). S2 may start as
soon as S1's records compile — it is pure over them. Every story's tests run through
`node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*RoundAuthors*"`
after `dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4`, and the whole suite before the story is called done —
never `dotnet test`. RED first: each scenario is written and watched failing for the RIGHT reason (the missing type or the
wrong number, not a setup error) before the code; every *break-it* is applied to the finished code, watched red, and reverted.

**Model, every story:** scenarios + brief — Opus (this document); code + tests — **Sonnet**; review — Opus; fixes — Sonnet.
Not a cornerstone.

---

### E6.S1 — The read: every round of a stage as text-free facts, read-only

- **Goal.** One read-only open of `coai.db` returns, per round, everything S2 needs and nothing it does not — no text column is
  ever selected — and a database at step 21, 22 or 23 is read, not migrated, with its absences as *not recorded*.
- **Scope (verified against `main` `2d98f2f8`).** New `RoundAuthorsModels.cs`, `RoundAuthorsQuery.cs`, test seed
  `RoundAuthorsSeed.cs`. Reused, not copied: the read-only open string `RoundCounts.cs:100`; `RoundsQuery.HasColumn`
  (`RoundsQuery.cs:910-918`) per D19; **the shipped size reader `RoundChangesLog.Read(db, ids)` (`RoundChangesLog.cs:23-34`) — the
  size is never selected a second time**; `PageIds.Bind` (`PageIds.cs:15-16`); `Columns.Text/Number/Id` (`Columns.cs:30-43`); the
  missing-file answer `RoundCounts.cs:94-98`. Stage names are `nameof(Stage.CodeReview)` / `nameof(Stage.PlanReview)` as stored
  (`RoundCounts.cs:172-175`).
- **Brief (for the Sonnet coder).**
  1. Records (in `RoundAuthorsModels.cs`):
     `RoundFacts(long RoundId, string SessionId, string RepoPath, string Branch, string Stage, int Number, string Status,
     string Verdict, string StartedUtc, bool InWindow, AuthorsFacts Authors, SizeFacts Size, IReadOnlyList<ReviewerFact> Reviewers,
     FindingFacts Findings)`;
     `AuthorsFacts(AuthorsStateKind State, IReadOnlyList<string> Reasons, string CallerSessionId, IReadOnlyList<StoredAuthorRow> Rows,
     AmbiguousFacts Ambiguous, int UncoveredWrites)` (`CallerSessionId` = `rounds.authors_session`, the caller D18 hands
     E4.S4 — its contributor identity is (caller session, agent); `UncoveredWrites` = the persisted `uncovered_writes`, E5.S2,
     kept as stored: `-1` = unknown, read by D12, never clamped to 0; 0 when the state is not recorded) with `AuthorsStateKind { NotRecorded, Unobserved, Partial, Observed }` (the order IS D3's worst-first
     order); `AmbiguousFacts(bool Known, int Agents, int Launches, int Turns, long InputTokens, long OutputTokens,
     long CacheCreationTokens, long CacheReadTokens)` (`Known = false` when E5 wrote `-1`, D17);
     `StoredAuthorRow(int Ordinal, string Who, string Agent, string Requested, string Model, string Effort, string AgentType,
     string Association, string Window, int Launches, int Turns, int WriteCalls)` — a store-side name, so it never collides with
     E4.S3's `AuthorRow`, into which D18 maps it;
     `SizeFacts(bool Recorded, RoundSize Size)` — the shipped `Core.Context.RoundSize` as `RoundChangesLog.Read` returns it
     (`Recorded = false` and an empty size when the round is absent from the map), not a re-declared row type;
     `ReviewerFact(string Provider, string Model, string Effort, bool EffortRecorded, string Status)`;
     `FindingFacts(int All, int Severe, int GateCount, int ReRaised, int Accepted, int Unresolved, bool Resolved)`.
     Records only — no `string?`, no nullable collections (absent = `NotRecorded` / `false` / `[]`). `SizeCell.Added`/`Removed`
     are `int?` in the shipped type and stay so: null is a binary's "no line count", never 0.
  2. `RoundAuthorsQuery.Read(string dataDir, string stage, string sinceUtc, string untilUtc) → IReadOnlyList<RoundFacts>`:
     (a) the in-window rounds of the stage (`started_utc >= $since AND started_utc < $until`, D13) → their session ids;
     (b) EVERY round of those sessions, any stage, any time, `InWindow` set from (a) — S2 needs them for D1 and D2, and rounds
     of sessions with no in-window round are never read; (c) per round: reviewers, `rounds.gating` (`gate_count`), finding
     counts in SQL (`COUNT(*)`, `SUM(is_gating)` (`severe`), `SUM(re_raised)`, `SUM(resolution = 'accept')`, `SUM(resolution = '')`), `rounds.accepted <> -1` as
     `Resolved`; (d) the sizes through `RoundChangesLog.Read(db, ids)` (it carries its own step-22 guard), and the `round_authors`
     rows plus the authors columns behind the step-24 guard (D19), with `authors_state` checked per row by
     `AuthorsSnapshot.IsWellFormed`. Every per-table query takes the round ids as ONE parameter through `PageIds.Bind` and
     `json_each` — the touched sessions' rounds can run to thousands, and a bound JSON array reaches no SQLite variable limit.
     Bounded by the touched sessions; at most one query per table, joined in memory by each row's own `round_id` — no
     per-round query.
  3. Never SELECT `plan_text`, `agent_log`, `subject`, `note`, `commands`, `plan_source` (step 23 — its JSON carries a relative
     path, `PlanSource`), nor `findings.title/why/fix/reason/file/category/security_evidence`, nor any text column E5 might add
     beside the authors. Parameters for every value; table/column names are literals.
  4. A database before step 24 (no `rounds.authors_state`) → every round `NotRecorded`; before step 22 (no `rounds.size_state`)
     → `Size.Recorded = false` and `EffortRecorded = false` (both arrive in step 22); before step 20 → reviewer `model` `''`.
     A missing file → `[]`, and no file is created.
  5. `RoundAuthorsSeed` (tests): `Round(db, session, repo, branch, stage, number, started, status, verdict, reviewers, findings,
     size, gitlinks, authors)` calling `RoundsDb.RecordRound` (`RoundsDb.cs:190-202`) with `RoundRecord.Size`, `Gitlinks` and
     `Authors` as E2 and E5 shipped them, and the resolution through `RoundsDb.RecordDecisions` (`RoundsDb.cs:763-778`). Plus
     `OlderFile(dir, stopBefore, sql)` in the `RoundCountsCliScenarioTests.cs:139-152` shape, the stop index found by content
     (§0). S2–S5 seed only through this.
- **Scenarios (RED first).** Class `RoundAuthorsQueryTests`, real SQLite in a temp directory, fixed instants (no clock).
  1. *A seeded round reads back as its facts*: one code round with two `done` codex reviewers + one `failed` gemini, five
     findings (2 blocking/major, 1 re-raised, 2 accepted, 1 rejected, 2 unresolved), `whole` rows for code/test/docs/binary,
     three author rows → the facts equal the expected record field for field (`Resolved = true`).
  2. *A never-resolved round* → `Resolved = false`, `Unresolved = All`.
  3. *Out-of-window rounds of a touched session are read, untouched sessions are not*: session A rounds at −30 d and −1 d,
     session B only at −30 d, window = last 7 d → A's two rounds (`InWindow` false/true), none of B's.
  4. *Window edges* (D13): a round AT `since` is in, AT `until` is out.
  5. *Stage*: plan and code rounds of one session; asking `CodeReview` returns the code rounds `InWindow` and the plan rounds
     only as `InWindow = false` context.
  6. *A step-21 file* (`OlderFile` stopped at `IndexOf(TheRoundsSize)`): rounds read, authors `NotRecorded`, size not
     recorded, `EffortRecorded = false`; the file's SHA-256 and `PRAGMA user_version` (21) unchanged afterwards.
  7. *A step-22 file and a step-23 file* (stopped at `IndexOf(ThePlanSource)` and at `IndexOf(TheRoundsAuthors)`): size read
     with its states, `EffortRecorded = true`, authors `NotRecorded`; SHA-256 and `user_version` (22, 23) unchanged — the step-23
     file is today's released store, the one E6 meets first.
  7a. *A malformed stored state* (`partial` with no reason, `done`): that round reads `NotRecorded`, the others are unaffected —
     per row, never per query (D19).
  7b. *An unknown ambiguous total*: an `unobserved` round seeded through the writer → `Ambiguous.Known = false`, no `-1` in any
     ambiguous field (D17); a `partial` round seeded with an `AuthorsSnapshot.Unknown(...)` snapshot → `UncoveredWrites = -1`
     read back as stored (D12).
  7c. *Main through the one inverse* (D18): a seeded main row (`agent = ''`) and a subagent row map to `AgentKey.Main` and the
     subagent's key, the same values `RoundAuthorsTable.AgentKeyOf` gives; a skipped and an interrupted round of the session
     read state `NotRecorded` and zero rows.
  8. *Missing database* → `[]`, the data directory holds no `coai.db` afterwards.
  9. *No text crosses*: a sentinel (`SENTINEL-7f3a`) written into `plan_text`, `agent_log`, `subject`, `note`, and every
     finding's `title/why/fix/reason` → no `RoundFacts.ToString()` of the read contains it; companion (`testing.md`, *a
     structural test that matches nothing*): the same check finds the round's `branch` text, proving the probe sees strings.
- **Break-its (each watched red, then reverted).** Select `v.effort` unconditionally → scenario 6 fails with the SQLite
  `no such column` instead of `EffortRecorded = false`; drop the step-24 guard → scenario 7's step-23 file fails with `no such
  column: authors_state`; drop the session filter of (b) → scenario 3 sees B's round; select `f.title` into a field →
  scenario 9; use `<=` for `until` → scenario 4; drop the per-row `IsWellFormed` check → scenario 7a reads `done` as a state; clamp
  `uncovered_writes` with `Math.Max(0, …)` → scenario 7b reads `0`.
- **Done when.** Scenarios 1–9 (with 7a–7c) green, the six break-its observed; the reader is read-only (scenarios 6–7) and
  text-free (scenario 9); suite green.
- **Depends on.** E2.S3 (step 22 — shipped, #829), E5.S1 (`AuthorsSnapshot.IsWellFormed`) and E5.S2 (step 24 and
  `RoundAuthorsTable.AgentKeyOf`), E4.S3 (the
  records D18 maps to). **Model:** brief Opus → code Sonnet → review Opus → fixes Sonnet.

---

### E6.S2 — The population: who is in, which cohort, why each excluded one is out

- **Goal.** One pure unit decides membership, apart from any arithmetic (the benchmark's `GatePopulation`, carried over: a
  cohort is decided once, before anything is counted — `dew_flow_benchmark · src/Bench.Domain/Gate/GatePopulation.cs` at
  `4dc98df`, cited as a path, not linked).
- **Scope.** New `RoundAuthorsPopulation.cs`; records added to `RoundAuthorsModels.cs`. Input: S1's facts and a
  `RoundAuthorsSelection(string Stage, string SinceUtc, string UntilUtc, string SplitUtc, string Cohort, IReadOnlyList<string>
  Kinds, bool WithBeforeOpen, bool WithoutElided, bool WithUncovered)` (`WithUncovered` = the CLI's `--with-uncovered`, D12; it reaches `Population.Of`, which skips the `size not covered` exclusion when set) (`SplitUtc = ""` = no split). No I/O, no clock.
- **Brief.**
  1. `Population Of(IReadOnlyList<RoundFacts> facts, RoundAuthorsSelection selection)` →
     `Population(IReadOnlyList<CountedRound> In, IReadOnlyList<ExcludedRound> Out)`. Only `InWindow` rounds of the selected
     stage are in `In` or `Out`; the others are context for D1 and D2 only.
  2. Cohort per D1 — computed per gate session over its stage's rounds sorted by `Number`, BEFORE step 4. `--cohort first`
     keeps `first`, `corrective` keeps `corrective`, `all` keeps both — as two cohorts, never one (`CountedRound.Cohort` is
     `first` or `corrective`, there is no third value). A round dropped by `--cohort` is not an exclusion (it was not asked
     for); a `not done` round has no cohort and is excluded under the cohort `—`.
  3. Period per D13.
  4. Exclusion per D12: `enum ExclusionReason` declared IN D12's order, evaluated in declaration order, the first true one
     wins. `ExcludedRound(RoundId, Cohort, Period, ExclusionReason Reason)`.
  5. Contributors per D2, authors state per D3, coders / coder mix per D4, coder effort and main model per D5, reviewer set
     and the beside-counts per D6 (effort label per D7), lines per D10 and D11.
  6. `CountedRound(RoundFacts Facts, string Cohort, string Period, CellKey Key, RoundSums Sums)` where
     `CellKey(Cohort, Period, CoderMix, CoderEffort, MainModel, ReviewerSet)` and `RoundSums` carries everything S3 adds:
     selected lines, elided lines, excluded lines, binary files, delta lines and the delta-unavailable reason, the five
     numerators with their coverage flags (D9), failed / stood-down reviewer rows, partial-set flag, authors-partial flag,
     provisional flag (`peer running` in any contributing round's reasons, D3), size-coverage flags (`size not covered`,
     `size coverage unknown`, under `--with-uncovered`), ambiguous launches and tokens, coder launches per family, and the
     **requested → answered counts**: coder rows per (requested family, answered family) pair (E4's risk consultation:
     grouping is on the answering model, and the report shows where the request differs). Family grain, because a request is
     usually an alias: a fallback to another family is an off-diagonal pair; a fallback inside one family (Opus 5.5 → Opus
     4.8) stays on the diagonal and is not visible here — the header says so.
  7. Family (D4) is ONE `static` function used for both the requested alias (`sonnet`) and the observed id
     (`claude-sonnet-…`), so the comparison in 6 cannot disagree with the grouping.
- **Scenarios (RED first).** Class `RoundAuthorsPopulationTests`, facts built in memory (records, no database).
  1. *Cohorts* (D1): session rounds 1 done, 2 done, 3 interrupted → 1 `first`, 2 `corrective`, 3 excluded `not done`.
  2. *An interrupted round 1*: rounds 1 interrupted, 2 done → 2 is `first`.
  3. *An excluded first round does not promote*: round 1 size not recorded, round 2 fully recorded → round 1 excluded
     `size not recorded` under `first`, round 2 counted under `corrective`, not `first`. Variant (D3): round 1 authors
     unobserved → round 2, whose contributors include round 1's windows (D2), is excluded `authors unobserved` under
     `corrective`.
  4. *A first round before `--since`*: round 1 at −30 d, round 2 at −1 d, window 7 d → round 2 is `corrective`.
  5. *The cheap model fixes round 3* (parent §6): round 1's window has an Opus `wrote here` subagent, round 3's a Sonnet one
     → round 1's mix `opus`, round 3's `mixed`; round 1 does not see the fixer (the `≤ started_utc` bound).
  6. *Plan-round windows count* (D2): the Sonnet coder sits in the last plan round's window → code round 1's mix `sonnet`.
  7. *before-open* (D2): the only coder is in `before-open` → `main only` (main wrote) without the flag, `sonnet` with it.
  8. *Families and mixes* (D4): `claude-sonnet-x` → sonnet; `''` → unknown; `gpt-6-luna` → other; requested `sonnet`
     observed `claude-opus-x` → family opus, requested → answered pair (sonnet, opus) = 1; requested `''` → pair (unknown,
     opus); no coder and no main write → `no write observed`.
  8a. *`unknown` is not a family* (D4, E4 G10): a Sonnet coder plus a two-repositories row (`Model = "unknown"`, write calls
     2) → mix `sonnet+unknown`, never `mixed`; a Sonnet and an Opus coder plus a launch-only row → `mixed+unknown`; only the
     two-repositories row → `unknown`, not `no write observed`.
  8b. *A skip between rounds* (D2): rounds 1 done, 2 skipped, 3 done, all observed → round 3's contributing rounds are 1 and
     3; round 3 is counted, not excluded `authors not recorded` by the skip's empty state.
  8c. *Provisional* (D3): a contributing round `partial: peer running` → the round is counted, authors-partial and
     provisional both 1.
  9. *Effort* (D5): coders at `high` and `''` → `high+unknown`; no coders → `—`.
  10. *Reviewer set* (D6): three `done` codex rows of one model → one entry; plus one `failed` gemini → same set, failed 1,
      partial set; every reviewer failed → excluded `no reviewer answered`.
  11. *Lines* (D10): shown 80 + elided 20 code/test, excluded 500, binary 2 files, docs 40 → selected 100; docs-only round →
      `no selected lines`; binary-only → `binary only`; `--kinds docs` → the docs-only round counted with 40; `--without-elided`
      → the elided round excluded `elided`.
  11a. *Corrective delta* (D10): two corrective rounds, one with `delta_state = recorded` and 30 delta lines, one with
      `unavailable: base moved` → both in the cell's `whole` figures; delta coverage 1 round / 30 lines; `delta unavailable:
      base moved = 1`; the unavailable round's whole lines are NOT in the delta sum. A `recorded` delta with no cells → in
      coverage with 0 lines.
  11b. *Skipped* (D1, D12): rounds 1 skipped (`done`, verdict `skipped`), 2 done → 1 excluded `skipped` with no cohort, 2 is
      `first`.
  11c. *Unknown kind printed* (D10): a round with 12 lines of `unknown` kind (`.ps1`) beside 50 of `code` → selected 50, *not
      counted* `unknown` 12.
  11d. *Coverage unknown* (D12): a `partial` round with `uncovered_writes = -1` → excluded `size coverage unknown`, not
      counted as covered; with `WithUncovered` → counted, its size-coverage-unknown flag set; a round with `2` → `size not
      covered`, the two never merged.
  12. *First match, once* (D12): one round failing every check is excluded ONCE, under `not done`; then, for EVERY value
      of `ExclusionReason` (iterated from the enum, not listed — `testing.md`, *a test that repeats a list*), a round
      failing exactly that check is excluded under it.
  13. *Periods*: a round AT the split instant is `after`.
  14. *Plan stage* (D10): a plan round with no size rows is counted, its lines `not applicable`, no size exclusion.
  15. *Conservation*: |In| + |Out| = the in-window rounds of the stage asked for (cohort filter aside).
- **Break-its.** Cohort computed after the exclusions → scenario 3 shows round 2 as `first`; contributors without the
  `≤ started_utc` bound → scenario 5's round 1 is `mixed`; `excluded` coverage added to the selected lines → scenario 11
  reads 600; D3 taking the round's OWN state → scenario 3's round 2 is counted; the unavailable delta falling back to `whole`
  lines → scenario 11a's delta coverage reads 2 rounds; the skipped predicate removed from D1 → scenario 11b's round 2 is
  `corrective`; contributing rounds without the settled predicate → scenario 8b's round 3 is excluded `authors not
  recorded`; `unknown` counted as a family → scenario 8a reads `mixed`; `uncovered_writes > 0` alone → scenario 11d's `-1`
  round is counted as covered.
- **Done when.** Scenarios 1–15 (with 8a–8c, 11a–11d) green, the nine break-its observed; the unit is `static`, takes no clock, opens
  nothing.
- **Depends on.** S1's records; E4.S4 (`Contributors.Of`, D2/D18); E5.S6 (`shared/model-display-vectors.json`, D4).
  **Model:** brief Opus → code Sonnet → review Opus → fixes Sonnet.

---

### E6.S3 — The counting: cells, ratios of sums, `Unknown` never 0, marks, the header

- **Goal.** One pure unit turns a `Population` into additive cell sums and, at render time only, into figures — the
  benchmark's `GateReport`/`Figure` conventions carried over (`dew_flow_benchmark · src/Bench.Domain/Gate/GateReport.cs`,
  `Figure.cs` at `4dc98df`): a figure with no population is `Unknown`, never 0.
- **Scope.** New `RoundAuthorsCells.cs`; records added to `RoundAuthorsModels.cs`. No Figure type exists in `src_mcp`
  (searched 2026-10-14) — a small `RoundFigure(FigureState State, double Value)` with `Known`, `Unknown`, `NotApplicable`
  is written here, not referenced across repositories.
- **Brief.**
  1. `RoundAuthorsSums Count(Population population)` →
     `RoundAuthorsSums(IReadOnlyList<CellSums> Cells, IReadOnlyList<ExclusionCount> Exclusions)`; `CellSums` keyed by
     `CellKey` holding ONLY integers and sets: rounds; session ids (a sorted set); rounds per branch (`repo_path` + `\n` +
     `branch` → count); selected / elided / excluded lines; *not counted* lines per kind (D10); binary files; delta lines,
     delta-covered rounds and delta-unavailable per reason (D10); size-unavailable per reason (D12); per
     numerator `NumeratorSums(long Total, int CoveredRounds, long CoveredLines, int DeltaCoveredRounds, long DeltaCoveredLines)`; failed and stood-down reviewer rows;
     partial-set rounds; authors-partial rounds and, of them, provisional rounds (D3); size-not-covered and
     size-coverage-unknown rounds (D12, under `--with-uncovered`); ambiguous agents, launches, turns and the four token terms
     over known rounds plus `UnknownRounds` (D17); coder launches per family; requested → answered coder rows per
     (requested family, answered family) pair (`requested` + `\n` + `answered` → count, summed per pair like the branch map).
     `ExclusionCount(Cohort, Period, Reason, Rounds)`. Cells ordered by key, ordinally — the same input gives the same bytes.
  2. Figures, computed from sums only (D16), never stored back: per round = `Total / CoveredRounds`; per 100 lines =
     `100 · Total / CoveredLines`; a corrective cell's per 100 delta lines = `100 · Total(delta-covered rounds) /
     DeltaCoveredLines` (D10 — never `whole` lines as a fallback). `CoveredRounds = 0` or `CoveredLines = 0` (and the delta
     twins) → `Unknown`; plan stage per-100 → `NotApplicable`; an ambiguous total with `UnknownRounds > 0` → a floor `≥ n` (D17).
     A ratio of SUMS — never the mean of per-round ratios.
  3. Marks per D14, from `Rounds` and the branch map (largest share = max / rounds).
  4. `IReadOnlyList<string> Header(RoundAuthorsSums sums, RoundAuthorsSelection selection, bool effortMarkerKnown)` per D15,
     plus D7's, D8's and D10's sentences (the denominator, the `delta` fallback, what `unknown` kind covers). Header lines are fixed sentences with values filled in — no text from any store row except
     dimension values (models, providers, effort words, family names).
- **Scenarios (RED first).** Class `RoundAuthorsCellsTests`, populations built through S2 from in-memory facts (so a cell
  is asserted exactly, from rounds a reader can see).
  1. *Ratio of sums*: round A 10 findings over 100 lines, round B 0 over 900, one cell → per 100 = `1.00`, not `5.00`;
     per round = `5.00`.
  2. *Coverage-specific ratios* (D9): two rounds of 100 lines, 4 accepted in the resolved one, the other never resolved →
     accepted covered rounds 1, lines 100, per 100 = `4.00` (not `2.00`).
  3. *Unknown is not 0*: a cell where no round was resolved → accepted per 100 `Unknown` and per round `Unknown`, while
     unresolved is a known number; a plan-stage cell → per 100 `NotApplicable`.
  4. *Cohorts never pooled*: one key in `first` and in `corrective` → two cells; no `CellSums` has a cohort other than
     `first`/`corrective` (asserted over every cell).
  5. *Exact cells*: the seeded mix — two coder mixes × two reviewer sets × two periods, one failed reviewer, one dominant
     branch — every cell's sums asserted field for field (written out once, in the test, as the expected records).
  6. *Marks* (D14): 9 rounds → `small`, 10 → none; 6 of 10 on one branch → `dominated`, 5 of 10 → none.
  7. *Header* (D15): a split where coder mix AND reviewer set both change → both named and the cannot-separate sentence
     present; only coder effort changes → it alone named, no cannot-separate sentence; no split → no period claim; the
     association-only, asked-for-model, contribution-not-authorship, unobserved-lines, rank-allocation and roll-up-limit
     sentences always present; the provisional sentence present exactly when a counted round is provisional.
  8. *Exclusions conserve*: Σ cell rounds + Σ exclusion rounds = |In| + |Out| per cohort and period.
  9. *Determinism*: the same population counted twice, and in shuffled input order → equal sums.
  10. *Delta figures* (D10): a corrective cell of two rounds, 3 findings over 100 whole / 20 delta lines and 1 finding over
      100 whole lines with `unavailable: no merge base` → per 100 whole `2.00`, per 100 delta `15.00` over 1 covered round, a
      cell whose every delta is unavailable → per 100 delta `Unknown`.
- **Break-its.** Per 100 as the mean of per-round ratios → scenario 1 reads `5.00`; a zero denominator returning `Of(0)` →
  scenario 3; dominance at `≥ 50 %` → scenario 6's 5 of 10 marked; a `cohort = all` cell → scenario 4.
- **Done when.** Scenarios 1–10 green, the four break-its observed; sums are integers and sets only; figures exist only as
  return values of the figure functions.
- **Depends on.** S2. **Model:** brief Opus → code Sonnet → review Opus → fixes Sonnet.

---

### E6.S4 — The JSON of sums, and `--add` of two stores

- **Goal.** The default output is the sums as JSON — additive, so a WSL run saved to a file plus the Windows run print the
  combined report without opening a store across the drvfs boundary (`roundCountsRead.ts:18-21`); combining two halves equals
  reporting their union.
- **Scope.** New `RoundAuthorsMerge.cs`; `RoundAuthorsAnswer` registered in `ServerJsonContext.cs` beside
  `RoundCountsAnswer` (`:363`) — source-generated, no reflection; the context omits nulls (`:344`), so every member of the
  answer is non-nullable and a zero is WRITTEN.
- **Brief.**
  1. `RoundAuthorsAnswer(int Format, RoundAuthorsSelection Selection, IReadOnlyList<RoundAuthorsSource> Sources,
     IReadOnlyList<CellSums> Cells, IReadOnlyList<ExclusionCount> Exclusions, bool EffortMarkerKnown)`; `Format = 1`;
     `RoundAuthorsSource(string SourceId, string Platform, string ReadUtc)` — `SourceId` is an OPAQUE id (lower-case hex of the first 16 bytes of SHA-256 over the normalised data-directory path), never the path: the JSON answer carries no absolute path, so it exposes no user name or workspace layout; the merge check compares ids, a test rejects any absolute path in the serialised answer; the platform from
     `RuntimeInformation.OSDescription`'s family, the instant handed in (no clock inside the unit). **Only sums, counts, sets
     and the selection** — no figure, no mark, no header: those are S3's render-time functions over the merged sums.
  2. `Outcome Add(RoundAuthorsAnswer mine, RoundAuthorsAnswer theirs)` → the merged answer or a refusal sentence:
     `Format` differs → refuse naming both; any `Selection` field differs → refuse naming the FIELD and both values (two
     selections are two populations); a `Source` present in both → refuse (a store added twice doubles every count);
     `EffortMarkerKnown` differs → keep `false` (the weaker claim). Merge: cells by key — integers summed, session-id sets
     UNIONED, branch maps summed per branch; exclusions summed by (cohort, period, reason); sources concatenated; cells
     re-sorted as S3 sorts them.
  3. `Parse(string text)` through the same JSON context; unknown properties, a missing required property, or a wrong type →
     a refusal sentence (S5 turns every refusal of this unit into exit 65).
- **Scenarios (RED first).** Class `RoundAuthorsMergeTests`.
  1. *Two halves = the union*: seed store A with rounds X and store B with rounds Y (disjoint sessions) and store C with
     X ∪ Y (S1's seed, same records); `Add(Read(A), Read(B))` equals `Read(C)` in `Cells` and `Exclusions` exactly
     (`Sources` aside).
  2. *Commutative*: `Add(a, b)` and `Add(b, a)` serialise to the same bytes after `Sources` are set aside.
  3. *Marks are recomputed, not added*: two halves of 6 rounds each in one cell → the merged cell renders no `small`; a branch
     with 4 of 6 in one half and 0 of 6 in the other → merged 4 of 12, not `dominated`.
  4. *Sets union*: two constructed answers that share a session id in one cell → sessions counted once.
  5. *Refusals*: selections differing in `--split` → the sentence names `split` and both instants; `Format = 2` → refused;
     one source added twice → refused; text that is not JSON, and JSON missing `cells` → refused.
  6. *Round trip*: serialise → parse → equal; and no property anywhere in the document is named like a figure — asserted
     by walking the JSON and failing on any number that is not an integer (a ratio cannot hide as a sum).
- **Break-its.** Session ids summed as counts → scenario 4 counts 2; marks stored in the JSON and added → scenario 3 shows
  `small`; selection ignored → scenario 5 merges two splits silently.
- **Done when.** Scenarios 1–6 green, the three break-its observed; the JSON contract is written into the mode's XML doc as
  `RoundCountsMode.cs:6-29` writes its own.
- **Depends on.** S3. **Model:** brief Opus → code Sonnet → review Opus → fixes Sonnet.

---

### E6.S5 — The mode: flags, exit codes, the table, registration, docs

- **Goal.** `coai-mcp --round-authors [--since <utc>] [--until <utc>] [--split <utc>] [--stage code|plan]
  [--cohort first|corrective|all] [--with-before-open] [--kinds <k,…>] [--without-elided] [--with-uncovered] [--add <file.json>]… [--table]`
  answers from the store read-only, exits 0 / 65 / 74 and never 64, and is registered everywhere a one-shot mode must be.
- **Scope (verified against `main` `2d98f2f8`).** New `RoundAuthorsMode.cs` (the `RoundCountsMode.cs:39-88` shape — today
  `Run(args, env, output, note, clock)`; this mode adds `readFile` for `--add`; a pure `Parse` returning `(Ok, Selection, Adds,
  Table, Why)`), new `RoundAuthorsText.cs` (the table). Registration lines only: `Program.Startup` member beside `RoundCounts`
  (`Program.cs:287-297`), `Classify` (`Program.cs:323-339`), the dispatch (`Program.cs:441-444`), the help paragraph after
  `--round-counts`'s (`Program.cs:2457-2461`); the one-shot list `.agents/PROJECT.md:53`; the reader-mode list
  `AReaderModeNeverMakesTheDataDirectoryTests.cs:46` — which is also what classifies the new `Startup` value for
  `EveryModeIsClassifiedForAMissingNamedRootTests` (`:45-58`: a reader the test runs; it is NOT added to `Program.WriterModes`,
  `Program.cs:310-314`). Data directory resolved as `--round-counts` resolves it (`RoundCountsMode.cs:51`,
  `PanelSettings.FromEnvironment`). Unreadable store: `Program.Unreadable` / `Program.WhyUnreadable` (`Program.cs:1652-1660`).
  Instants: `Program.TryParseInstant` (`Program.cs:1769`).
- **Brief.**
  1. Parse: every flag at most once except `--add` (repeatable); instants through `TryParseInstant`; `--since` default
     the epoch, `--until` default the clock; `--until ≤ --since` refused; `--split` outside `[since, until)` refused;
     `--stage` ∈ {code, plan} (default code); `--cohort` ∈ {first, corrective, all} (default first); `--kinds` a
     non-empty subset of {code, test, docs, config, unknown} (default `code,test`); `--stage plan` with `--kinds` or
     `--without-elided` refused (no diff). Every refusal: the sentence on stderr naming the mode, the flag and the refused
     value; exit **65**; nothing on stdout. An unknown flag is a refusal, never ignored.
  2. Answer: S1 → S2 → S3 → S4's answer for the local store; each `--add` file read and parsed (unreadable or refused →
     **65** with S4's sentence); merged in argument order; then JSON (default, one document, S4's contract) or, with
     `--table`, `RoundAuthorsText.Render(answer)`. A missing database is an empty local answer and exit **0** (and `--add`
     still applies). A store that will not read → **74** with `WhyUnreadable`. **Never 64.**
  3. `RoundAuthorsText.Render`: the header (S3) first; per cohort, per period, one table — coder mix · coder effort · main
     model · reviewer set · rounds · sessions · branches · top branch % · marks · selected lines · then per numerator total,
     per round, per 100 lines, coverage (`k/n rounds`) · failed / stood-down / partial-set · authors partial (provisional) ·
     ambiguous · requested → answered (the off-diagonal pairs, `sonnet→opus 2`; `—` when none);
     · *not counted* lines per kind (`unknown` included, D10); corrective tables add delta lines, per 100 delta lines with its
     delta coverage, and delta unavailable per reason. A figure prints `Unknown` or `n/a`, never `0` and never blank.
     Then the exclusions table (cohort · period · reason · rounds), every reason of the enum listed with its count, zeros
     included — an absent row and a zero are different claims. Fixed column order; no colour; ASCII separators.
  4. Docs in the same story: `research/module_server.md` — a `--round-authors` section after `--round-counts`'s
     (`module_server.md:3350`), with the contract, D1–D19 by reference to this document, and a Mermaid flow (facts →
     population → sums → merge → render); `research/module_tests.md` — a row per test class after the `--round-counts` section
     (`module_tests.md:3190`, its rows `:3205-3206`), with each RED message and break-it as that section records them.
- **Scenarios (RED first).** Class `RoundAuthorsCliScenarioTests` — the built binary through `OneShotProcess.Run`
  (`OneShotProcess.cs:26-35`, `COAI_DATA_DIR` set), as `RoundCountsCliScenarioTests` runs it; fixed instants via `--until`.
  1. *JSON on stdout, exit 0*: a seeded store → parses through the JSON context; one cell asserted exactly.
  2. *The table*: `--table` → the first line is the association-only sentence; a never-resolved cell prints `Unknown` in its
     accepted columns; a 3-round cell carries `small`; the exclusions table lists every reason, zeros included.
  3. *Empty*: no `coai.db` → exit 0, empty cells, every exclusion zero, and no `coai.db` created.
  4. *Bad requests are 65, never 64*: an unknown flag; `--since` twice; `--since yesterday`; `--cohort both`;
     `--kinds code,bogus`; `--stage plan --kinds code`; `--until` before `--since` → each 65, stdout empty, stderr naming the
     flag and quoting the value; control: `--round-authorz` → 64.
  5. *`--add` faults are 65*: a path that does not exist; a file of `not json`; a valid answer with another `--split`.
  6. *A store that will not read is 74*: `coai.db` holding random bytes.
  7. *An older store is read, not migrated*: a step-21 file → exit 0, its code rounds under `authors not recorded`, reviewer
     effort `not recorded`; a step-23 file (today's released store) → exit 0, sizes read, authors `not recorded`; SHA-256 and
     `user_version` unchanged for both.
  8. *No text leaves*: `SENTINEL-7f3a` in `plan_text`, `agent_log`, `subject`, `note` and every finding's
     `title/why/fix/reason` → absent from stdout (JSON and `--table`) and from stderr; companion: the seeded branch name IS
     present in `--table` (the probe sees strings).
  9. *Two processes add up*: store B reported to a file; store A run with `--add` of it → stdout equals the report over
     store C = A ∪ B (`sources` aside).
  10. *Registered*: `Program.Classify(["--round-authors"])` is the new member, not `Startup.Usage`; the help text names it;
      `.agents/PROJECT.md`'s one-shot list names it; `AReaderModeNeverMakesTheDataDirectoryTests` runs it.
- **Break-its.** A refusal answering 64 → scenario 4's `Expected code to be 65 … but found 64`; the missing-file check
  removed → scenario 3 finds 74; `subject` printed in the table → scenario 8; `--add` read failure as 74 → scenario 5;
  the read opened read-write → scenario 7's SHA-256 differs.
- **Done when.** Scenarios 1–10 green and the five break-its observed; the full `CoaiMcp.Tests.exe` green; `module_server.md`
  and `module_tests.md` describe the mode; `--round-authors` is in `.agents/PROJECT.md`'s one-shot list IN THE SAME COMMIT
  that wires it into `Program.cs`, and `ARequestFaultIsNotAnOldBinaryTests` (`TheSanctionedModesAreTheOnesTheDocumentNames`)
  is green; a seeded round with `uncovered_writes > 0` is excluded as `size not covered` and taken in under
  `--with-uncovered`; `node .agents/conventions/tools/plan-lifecycle.mjs` clean.
- **Depends on.** S4. **Model:** brief Opus → code Sonnet → review Opus → fixes Sonnet.

## 2. Definition of Done (epic E6)

- [ ] §12's report row passes over seeded stores: exact cells, ratios of sums, `Unknown` not 0, exclusions counted once per
      round, cohorts never pooled, `small`/`dominated` marks, `--add` of two halves = the union (S2–S5).
- [ ] Exit 0 on an empty or missing store, 65 on a bad flag or an unreadable/mismatched `--add`, 74 on an unreadable store,
      never 64 (S5).
- [ ] The header names coincident changes and says *association only* (S3, S5).
- [ ] No text column is read (S1) or printed (S5) — the sentinel tests pass with their companions.
- [ ] A step-21, a step-22 and a step-23 store are read without being migrated (S1, S5).
- [ ] A corrective round with an unavailable delta stays in the `whole` figures, leaves the delta figures' coverage and is
      counted per reason — never given `whole` lines as a delta denominator (S2, S3; parent §16 D26). Skipped rounds have no
      cohort and are never contributing rounds (S2).
- [ ] `uncovered_writes = -1` is excluded as `size coverage unknown`, never read as covered; `unknown` coders never make a
      mix `mixed`; `peer running` rounds count as partial and provisional; the header carries the rank-allocation and
      roll-up-limit sentences (S2, S3).
- [ ] `.agents/PROJECT.md` lists `--round-authors`; `module_server.md` and `module_tests.md` describe it.
- [ ] Every break-it of S1–S5 was watched red and reverted; `CoaiMcp.Tests.exe` and the family checks green.
- [ ] The parent's E6 entry links this document (owed by whoever commits the epic) and `todo/README.md` carries this
      document's row (planning-docs.md, *each stories document is a plan of its own to the checker*).

## 3. Revised 2026-10-16 against `main`

Verified at `2d98f2f8` (E1 #828, E2 #829 in mcp 0.56.0, the plan-gate E1 #837 and E2 #841 in mcp 0.58.0; extension 0.78.0).
Every `path:line` in this document was re-read there; E3/E4/E5 were then cited as of `e3a94e0c` / `36560698` / `fe4f878f` (§3a moves them on).

- **Steps 22 and 23 are shipped, step 24 is E5's.** §0 reads `round_changes`, the size states and `reviewers.effort` as they
  shipped (`Schema.cs:635-652`) and `round_authors` as E5.S2 names it — `ordinal`, the four token columns by name, the seven
  `ambiguous_*` columns with `-1` for unknown, `agent = ''` for main, `window` only `before-open`/`round`. The old line "none
  of their schema steps exists today" is gone.
- **The size is read through the shipped reader.** `RoundChangesLog.Read` returns `RoundSize` per round, so S1 no longer
  declares `ChangeRow` or selects `round_changes` itself; `SizeFacts` wraps the shipped type (reuse-first, step 1).
- **The shipped readers' shape is followed (D19):** a `HasColumn` guard per step's marker column, `PageIds.Bind` +
  `json_each`, rows joined by their own id, a malformed state refused per row. S1 gains scenarios 7a–7b and a break-it on the
  step-24 guard; scenario 7 now covers a step-22 AND a step-23 file (today's released store), stop indices found by content.
- **Corrective denominator and its fallback (parent §16 D26) — D10 rewritten.** `whole` stays the shared denominator; a
  corrective cell adds per-100-DELTA figures; an `unavailable` delta leaves only the delta figures' coverage, is counted per
  reason, and is never given `whole` lines. E7 reports the share of corrective rounds that end unavailable from these
  per-reason counts. S2 scenario 11a, S3 scenario 10, a new DoD row.
- **The `unknown` kind is printed (D10)** as *not counted* lines per kind, with the header naming what it covers (E2 §10
  item 7). S2 scenario 11c.
- **Skipped rounds (D1, D12).** A skip is `done` with a sha; cohorts now use the shipped `DeltaPredecessor.IsCandidate`
  predicate, and `skipped` is an exclusion. Also `size unavailable` (a whole scope `unavailable: …`) is an exclusion of its
  own, distinct from `size not recorded`.
- **New decisions D17–D19:** unknown totals are floors, never `-1` summed; one mapping from stored rows to E4.S4's input;
  the readers' shape.
- **`reviewers.effort` (D7) verified:** `RecordReviewers` binds `''` for none (`RoundsDb.cs:950-951`); `not recorded` is only the
  column default; `RosterBuilder.cs:705-712` → `EffortFor`, `:721-727`.
- **Registration (S5):** the reader test is what classifies the new `Startup` value for
  `EveryModeIsClassifiedForAMissingNamedRootTests`; `--round-authors` is NOT a writer mode.
- **Drifted anchors updated:** `Program.cs` 2 514 → 2 519 lines, help `:2452-2456` → `:2457-2461`; `ServerJsonContext.cs:361`
  → `:363`; `RoundCounts.cs` `:87` → `:100`, `:89,101-104` → `:102`, `:81-85` → `:94-98`, `:145-146` → `:158-159`,
  `:159-160` → `:172-175`; `RoundsQuery.cs:895-902` → `:910-918`; `Columns.cs:30-40` → `:30-43`; `RoundsDb.cs:777` →
  `RecordDecisions` `:763-778`, `:996` → `:1004`; `RoundCountsMode.cs:30-88` → `:39-88`; `RoundCountsCliScenarioTests.cs:140-152`
  → `:139-152`; `module_server.md:3177` → `:3350`.

## 3a. Revised 2026-10-18 — what E3, E4 and E5 owe E6

Rebased on `main` `9a0a5f2e` (mcp 0.59.0): no file this document cites in `src_mcp` changed since `2d98f2f8`; only the
`module_tests.md` anchors moved (`:3181` → `:3190`, rows → `:3205-3206`). E3 read as of `f4f80ce7`, E4 `30362785`, E5 `c1216b42`.
Every point was checked against those texts and the code before it was applied; none was rejected.

| Owed point | Source | Applied as |
|---|---|---|
| Cohorts and contributors use `done` ∧ `IsRecorded`; facts need `Verdict` | E4 gate; E4 §S2.3 | D1 names E4's *settled* as the one predicate; D2 counts only settled rounds as contributing (a skip's empty state no longer excludes every later round); S2 scenario 8b + break-it |
| The caller session in `AuthorsFacts` | E4 gate; E4.S4 identity | already a member; S1 says why (D18 → (caller session, agent)) |
| Requested → answered counts | E4 §10 consultation | `RoundSums`/`CellSums` carry (requested family, answered family) pairs; the table prints the off-diagonal ones; the within-family limit is stated |
| The rank allocation in one sentence, never evidence | E4 G11 | D15's header sentence; S3 scenario 7 |
| D2/D3 and per-window `Windows` | E4 G9; E5 §10 item 13 (i) | the limit is stated (D3, the header, §4 item 9) — the projection has the roll-up only, and E6 reads no session file |
| `peer running` counts as partial, finalised by E7 | E4 G3; E5 §10 item 13 (ii) | D3; a `provisional` count per cell; S2 scenario 8c |
| The two-repositories row's `unknown` model is not a model | E4 G10 | D4: `unknown` is not a family, a `+unknown` suffix; S2 scenario 8a + break-it |
| `-1` = unknown in `uncovered_writes` / `submodule_writes` | E5 §10 item 12 | D12's new reason `size coverage unknown`; S1 7b, S2 11d + break-its; `submodule_writes` is not read (§0) |
| D18 calls `AgentKeyOf` | E5 §10 items 7, 13 (iii) | D18; S1 7c; S1 depends on it |
| `healed` is treated like `stored` | judge round 2026-10-17; E5 §10 item 13 (iv) | §0: the id source is not read; a healed round counts as any other |
| Unprojected members take `Unknown`'s values | E5 §3, item 11 (c) | D18: `UnobservedLaunches = -1`, `Windows`/`PeerGates = []` |
| `prior caller unknown` / `peer running` reach the state text | judge round; E4 §S2.3 | §4 item 6 lists E4's whole closed set |

The design choices in D4 (suffix, alias rule) and D12 (own reason, same flag) and the D3 limit were put to the question
consultants on 2026-10-18 (`c5adccb5…`); the one answering row agreed with each, and each was checked against E4 G10, E5 §10
item 12 and E5.S6's vectors rule before it was written.

## 4. Open with E3–E5

What this document assumes of contracts that are still being amended. Each is the named epic's to confirm, or E6's to change
when that epic ships; none blocks S2–S4, which are pure over S1's records. Items 2, 3 and 5 were closed by E4/E5 on
2026-10-16/17 and are kept as one line each for the record.

1. **E5 — step-24 names** (E5 as of `c1216b42`, §5 E5.S2). §0's `round_authors` and `rounds.authors_*` / `ambiguous_*` columns
   are E5's list; a rename there renames S1's literals. The shipped schema wins (§0).
2. **E5 — `-1` for unknown totals. CLOSED:** E4.S3 kept `-1` (`ActivityTotals.Unknown`, final); no `Captured`-style type
   ships (E5 §10 item 2). D17 maps `-1` as written; D12 reads `uncovered_writes = -1` the same way (E5 §10 item 12).
3. **E5 — the state string. CLOSED:** no reason can contain `", "` — E4.S3's `NoReasonContainsTheJoiner`, E5.S1's
   `E5sReasonsContainNoJoiner`, and `IsWellFormed` refusing a counted or empty reason (E5 §10 item 4). D18's split is exact.
4. **E4 — `AgentKey` and the input E6 builds.** `Contributors.Of` takes E4.S3's `RoundAuthors`; D18 fills the members the
   store does not project with `AuthorsSnapshot.Unknown`'s values. If E4.S4 starts reading `ClaimedByPeer` (it may, for a
   pointer to the peer, §E4.S4) the report would need a step to project it, or a session-file read E6 does not do today. The
   `""` ↔ `"main"` mapping stays ONE pair: E5.S2's `AgentColumn` and its inverse `AgentKeyOf`, which D18 calls.
5. **E5.S6 — the family column. CLOSED:** every vector is `{ model, display, family }` — `other` for an id the rule does not
   parse, `unknown` for `""`/`unknown` (E5 §10 item 6). D4 adds only the alias rule and that `unknown` is not a family.
6. **E3/E4/E5 — reason vocabulary.** D12 excludes `authors unobserved` whatever the reason; E3's closed `ObservationReasons`
   (E3 §3.2), E4's closed `AttributionReasons` (`no round start`, `no open time`, `prior windows unknown`, `caller changed`,
   `prior caller unknown`, `peer running`), and E5's `invalid input` / `reader failed` are counted per reason only through
   the `authors_state` text. If a reason is renamed, old rows keep the old word — the report prints both, never merges them.
7. **E4 — blind rounds.** E4.S2 decision 1 makes a round with no `StartedUtc` AND the next recorded round `unobserved: no round
   start`. D3's worst-state rule then excludes every later round of that gate session whose contributors include it — a
   cascade the report must count (it is, under `authors unobserved`), and a reason to check how many such rounds a real store
   holds before O4 of E7.
8. **Older servers strip what they do not know.** No `JsonExtensionData` is declared on `PersistedSession`
   (`ServerJsonContext.cs:339-345`), so an older `coai-mcp` (one per VS Code window) that loads and saves a session file drops
   its `authors` (and, before 0.56.0, its `size`). E6 reads the projection only, so this does not change the report — but it is
   the reason E7's operation checks the running servers' versions; E5 §3 now states it (the database is the durable copy).
9. **E5 / a later step — per-window states.** `Windows` (E4 G9) is in the session-file snapshot only. D3's roll-up limit stands
   until a schema step projects the round window's state and totals (E5 §10 item 13 (i): possible from the files with no
   backfill). When it ships, D3 reads the windows it keeps — the `round` window alone without `--with-before-open` — and the
   header's limit sentence goes. Not E6's to build; an owner decision whether it is wanted.
