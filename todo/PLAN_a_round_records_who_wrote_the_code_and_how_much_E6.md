# PLAN — E6: the report, `coai-mcp --round-authors`, over live rounds (stories)

> Status: **plan only, nothing implemented yet, 2026-10-14.** Epic E6 of [PLAN_a_round_records_who_wrote_the_code_and_how_much.md](PLAN_a_round_records_who_wrote_the_code_and_how_much.md).
>
> Scope: `src_mcp` only — a read-only one-shot mode beside `--round-counts`, its reader, a pure population unit, a pure
> counting unit, the JSON of sums and `--add`, the table. Nothing is written to any store; nothing on the extension or the
> Team server. Related docs: [module_server.md](../research/module_server.md) (*`--round-counts`*, the shape followed),
> [module_tests.md](../research/module_tests.md).

## 0. What every story stands on

**The parent's §8 is the contract; this document only splits it and closes the gaps §8 leaves.** Read §5–§8 of the parent
before any story. E6 carries no cornerstone AREA (the parent's `## Epics`, *"an arithmetic §8 fixes as exact-cell tests"*), so
no story here is marked; the parent's ledger reads `Cornerstones: 3 of 3 — E2.S1, E3.S4, E4.S2` (parent §16 D21).

**Inputs E6 reads, and from whom.** E6 depends on E5.S2 (step 23) and E2.S3 (step 22), on E4.S4 for contributors and on
E5.S6 for `shared/model-display-vectors.json`. None of their schema steps exists today
(`Schema.cs:31-37` ends at step 21, `TheRoundsKey`, `:614-616`), so the names below are the parent's (§7, §11 step 4) and
**the coder of E6.S1 opens `Schema.cs` steps 22 and 23 as E2 and E5 shipped them and uses THEIR names** — where they differ
from this list, the shipped schema wins and the story's summary says so.

| Read | From | Columns this document assumes |
|---|---|---|
| `round_changes` | E2.S3, step 22 | `round_id, scope (whole\|delta), coverage (shown\|elided\|excluded\|binary), kind (code\|test\|docs\|config\|unknown), files, added, removed`; `delta`'s `unavailable: <why>` as E2 records it |
| `reviewers.effort` | E2.S3, step 22 | `not recorded` = a row from before step 22; `''` = no effort applied (D7, parent §16 D11) |
| `round_authors` | E5.S2, step 23 | `round_id, who (main\|subagent), agent (opaque id, '' for main), requested, model, effort, agent_type, association, window, launches, turns, <4 token terms>, write_calls` |
| `rounds.authors_state`, `authors_session`, `authors_id_source`, `authors_reader`, ambiguous totals | E5.S2, step 23 | state `''` (not recorded) \| `observed` \| `partial: <why>` \| `unobserved: <why>` (§5) |
| `rounds` (existing) | `Schema.cs:52-78` | `id, session_id, stage, number, status, started_utc, accepted` (`-1` = `resolve` never ran, `:71-73`), `gating` (the gate's own count — the `gate_count` numerator, D8) |
| `sessions` (existing) | `Schema.cs:45-50` | `id, repo_path, branch` |
| `reviewers` (existing) | `Schema.cs:80-88,597-600` | `round_id, provider, status, model` |
| `findings` (existing) | `Schema.cs:90-116` | `round_id, is_gating, re_raised, resolution` (`accept`\|`reject`\|`''`, `RoundsDb.cs:777`) |

The architect's concurrent change — the authors also as a nullable snapshot on `RoundRecord` — does not change what E6 reads:
the report reads the projection (`coai.db`), as `--round-counts` does, never the session files. It changes how the TESTS seed a
store: through `RoundsDb.RecordRound` with the record's `Size` and authors snapshot set, i.e. through the real writers, never
hand-written `INSERT`s (`testing.md`, *a fixture the code rejects proves nothing*). Hand SQL is allowed for exactly one thing:
a database stopped at an OLDER step, built as `RoundCountsCliScenarioTests.cs:140-152` builds one (`Schema.Steps[..n]`).

**The shape followed.** `--round-counts`: the mode `src_mcp/src/Server/RoundCountsMode.cs` (88 lines — argument parse, exit
codes 0/65/74, never 64), the reader `src_mcp/src/Store/RoundCounts.cs` (read-only open `:87`, a column asked for before it is
selected `:89,101-104`, a missing file is an empty answer `:81-85`), the registration `Program.cs:297,339,441-444`, the help
`Program.cs:2452-2456`, the JSON context `ServerJsonContext.cs:361`, the one-shot list `.agents/PROJECT.md:53`. The file layout
follows `Store/GateHistory{Query,Rules,Models,Text}.cs` (one unit per job, 112–316 lines each):

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

`Program.cs` (2 514 lines) gains registration lines only. Every new file stays under 400 lines; a pure unit is `static`,
reads no clock and no disk.

### Decisions this document takes where §8 is silent

Each is a rule the stories implement and a test pins. A reviewer who disagrees argues with the line here, not with the code.

| # | Decision | Why |
|---|---|---|
| D1 | **Cohort is decided over ALL done rounds of the gate session's stage, before the window and before any exclusion.** `first` = the lowest-`number` round with status `done`; every later `done` round is `corrective`. An excluded first round does not promote round 2; a first round before `--since` leaves round 2 `corrective`. | otherwise a filter changes what "new code" means |
| D2 | **Contributors of round R** = `round_authors` rows of every round of R's gate session (any stage) whose `started_utc ≤ R.started_utc`; rows with `window = before-open` only under `--with-before-open`. Computed by E4.S4's one implementation over the stored rows (mapped to its input type) — never a second union here (parent §6, §16 D2). | §6 *"the union of the windows up to and including round N"*; §6 does not say whether plan rounds' windows count — D2 says they do (the coding between plan `proceed` and code round 1 sits in a plan-tiled window) |
| D3 | **Authors state of R for the report = the WORST state over its contributing rounds**: not recorded < unobserved < partial < observed. Not recorded or unobserved → excluded; partial → included and counted. | a union with a hole is not an observed union |
| D4 | **Coders** = contributor rows with `who ≠ main` and association `wrote here` (§8 *"families of `wrote here` subagents"*). Family from the OBSERVED `model` (§5) through `shared/model-display-vectors.json` (parent §16 D19, written by E5.S6) — the vectors the card reads too; empty → `unknown`; a model the vectors do not know → `other`. Coder mix: one family → it; several → `mixed`; none and a `main` row `wrote here` → `main only`; none at all → `no write observed`. | §8 lists `sonnet, opus, fable, mixed, main only`; a Bash-only change has no `wrote here` row and needs a name |
| D5 | **Coder effort** = distinct `effort` of the coder rows, `''` → `unknown`, sorted, joined `+` (`high`, `high+unknown`); no coders → `—`. **Main model** = distinct observed `model` of `who = main` contributor rows, sorted, joined `+`; none → `unknown`. | `unknown` stays visible inside a mix instead of being out-voted |
| D6 | **Reviewer set** = the DISTINCT `(provider, model, effort)` of reviewer rows with status `done`, sorted ordinally. Beside it, never in the key: failed rows, stood-down rows (`SessionStore.cs:53,56`), and `partial set` rounds (≥ 1 launched reviewer `failed`). No `done` reviewer → excluded `no reviewer answered`. | §8 *"for reviewers that answered — with failed and partial reviewers counted beside"* |
| D7 | **Reviewer effort**: `not recorded` (step 22's default for every row written before it) renders `not recorded`; `''` renders `none` (applied none — codex, gemini, agy, `RosterBuilder.cs:705-712`). The two are never merged, and a reviewer set keyed on `none` is not the one keyed on `not recorded`. | parent §16 D11: E2.S3 writes the explicit default |
| D8 | **Two numerators, named apart** (parent §16 D10): **`severe`** = `findings.is_gating`, i.e. severity blocking or major (`Finding.cs:84`, written `RoundsDb.cs:996`), BEFORE the gate's discount; **`gate_count`** = Σ `rounds.gating` (`GatingCount`, after it). No column, header or test calls either "gating". The table's column says `blocking+major`, and the header says it is not the gate's own count (`rounds.gating` = `GatingCount`, after the discount, `GateRule.cs:57-82`). | §8 calls `is_gating` the gating numerator and cites the discount; the column is pre-discount |
| D9 | **Coverage per numerator** = the rounds whose value is KNOWN, with their selected lines: all, severe, gate_count, re-raised, unresolved → every round in the cell (an unresolved finding is a real state, `Schema.cs:106-107`); accepted → rounds with `accepted ≠ -1`. Every ratio is over its own covered rounds and lines. | §8 *"each with its total and its coverage"* |
| D10 | **Denominator** (code stage) = Σ (`added + removed`) of `whole` rows with coverage `shown` or `elided` and kind in `--kinds` (default `code,test`). `excluded` and `binary` are reported apart and never in it. Corrective cells ALSO carry Σ `delta` lines and `delta unavailable` per reason — reported, not a denominator. `--stage plan` has no diff: no size exclusion, every per-100 figure is `not applicable`. | §7 coverage table; §8 is silent on which scope a corrective cell divides by — `whole` is what the reviewers read |
| D11 | **Two flags §8's synopsis does not list**: `--kinds <k,…>` (§8 *"flags select others"*) and `--without-elided` (§7 *"so the report can drop rounds with elision"*), which moves a round with any `elided` row to the exclusion `elided`. | named here so they are a decision, not a coder's invention |
| D12 | **Exclusion reasons, first match, one per round**: `not done` (`running`/`interrupted`, `SessionStore.cs:86-88`) → `no reviewer answered` → `authors not recorded` → `authors unobserved` → `size not recorded` → `binary only` → `size not covered` (`uncovered_writes > 0` — writes into a submodule or a nested checkout under `repo_path`, parent §7 / gate finding 0; skipped under `--with-uncovered`, which every per-100-line figure then includes) → `no selected lines` → `elided` (only with D11's flag). The last five are code-stage only; `size not covered` rounds are also counted per cell so the mark is visible, not hidden. | a round counted under two reasons double-counts the exclusions |
| D13 | **Periods**: no `--split` → one period `all`; else `before` (`started_utc < split`) and `after` (`≥ split`). The window is `[since, until)`. Both compared in ONE function, over the column's own `O` stamps, as `RoundCounts.cs:145-146` does. | one edge rule, one place |
| D14 | **Marks**: `small` when a cell has < 10 rounds; `dominated` when its largest branch (`repo_path` + `branch`) has MORE than half its rounds (6 of 10 is, 5 of 10 is not). Computed at render from additive sums, so `--add` recomputes them. | §8 *"under 10 rounds or one branch over 50 %"* |
| D15 | **The confounding line**: with `--split`, per cohort, every dimension (coder mix, coder effort, main model, reviewer set) whose SET of values differs between `before` and `after` is named; when coder mix AND reviewer set are both named, the header says the report cannot separate them. Without `--split`, no period comparison is claimed. Always: *association only*; the reviewer model is the one ASKED for (`SessionStore.cs:17-28`); a write is contribution, not authorship of surviving lines (parent §3); a diff can carry lines nobody observed (parent §13). | §8 *"the header says so"* |
| D16 | **Rounding**: a figure is `Math.Round(x, 2, MidpointRounding.ToEven)` of a `double` ratio, computed ONLY at render (S3) — the JSON never carries one (S4). Test values are chosen off the tie. | one rule; the JSON is sums |

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
  ever selected — and a database from before steps 22/23 is read, not migrated, with its absences as *not recorded*.
- **Scope (verified).** New `RoundAuthorsModels.cs`, `RoundAuthorsQuery.cs`, test seed `RoundAuthorsSeed.cs`. Reused, not
  copied: the read-only open string `RoundCounts.cs:87`; `RoundsQuery.HasColumn` (`RoundsQuery.cs:895-902`) for every column of
  steps 22/23 and a `sqlite_master` lookup for each new table; `Columns.Text/Number/Id` (`Columns.cs:30-40`); the missing-file
  answer `RoundCounts.cs:81-85`. Stage names are `nameof(Stage.CodeReview)` / `nameof(Stage.PlanReview)` as stored
  (`RoundCounts.cs:159-160`).
- **Brief (for the Sonnet coder).**
  1. Records (in `RoundAuthorsModels.cs`):
     `RoundFacts(long RoundId, string SessionId, string RepoPath, string Branch, string Stage, int Number, string Status,
     string StartedUtc, bool InWindow, AuthorsFacts Authors, SizeFacts Size, IReadOnlyList<ReviewerFact> Reviewers,
     FindingFacts Findings)`;
     `AuthorsFacts(AuthorsStateKind State, string Why, IReadOnlyList<AuthorRow> Rows, long AmbiguousLaunches, long AmbiguousTokens, long UncoveredWrites)` (`UncoveredWrites` = the persisted `uncovered_writes`, E5.S2; 0 when the state is not recorded)
     with `AuthorsStateKind { NotRecorded, Unobserved, Partial, Observed }` (the order IS D3's worst-first order);
     `AuthorRow(string Who, string Agent, string Requested, string Model, string Effort, string Association, string Window, int Launches)`;
     `SizeFacts(bool Recorded, IReadOnlyList<ChangeRow> Rows, string DeltaUnavailable)`;
     `ChangeRow(string Scope, string Coverage, string Kind, int Files, long Added, long Removed)`;
     `ReviewerFact(string Provider, string Model, string Effort, bool EffortRecorded, string Status)`;
     `FindingFacts(int All, int Severe, int GateCount, int ReRaised, int Accepted, int Unresolved, bool Resolved)`.
     Records only — no `string?`, no nullable collections (absent = `NotRecorded` / `false` / `[]`).
  2. `RoundAuthorsQuery.Read(string dataDir, string stage, string sinceUtc, string untilUtc) → IReadOnlyList<RoundFacts>`:
     (a) the in-window rounds of the stage (`started_utc >= $since AND started_utc < $until`, D13) → their session ids;
     (b) EVERY round of those sessions, any stage, any time, `InWindow` set from (a) — S2 needs them for D1 and D2, and rounds
     of sessions with no in-window round are never read; (c) per round: reviewers, `rounds.gating` (`gate_count`), finding
     counts in SQL (`COUNT(*)`, `SUM(is_gating)` (`severe`), `SUM(re_raised)`, `SUM(resolution = 'accept')`, `SUM(resolution = '')`), `rounds.accepted <> -1` as
     `Resolved`; (d) `round_changes` and `round_authors` rows when their tables exist, the state columns when they exist.
     Bounded by the touched sessions; read in at most one query per table, joined in memory by `round_id` — no per-round query.
  3. Never SELECT `plan_text`, `agent_log`, `subject`, `note`, `commands`, nor `findings.title/why/fix/reason/file/category`,
     nor any text column E5 might have added beside the authors. Parameters for every value; table/column names are literals.
  4. A database missing `round_authors` → every round `NotRecorded`; missing `round_changes` → `Size.Recorded = false`;
     missing `reviewers.effort` → `EffortRecorded = false`. A missing file → `[]`, and no file is created.
  5. `RoundAuthorsSeed` (tests): `Round(db, session, repo, branch, stage, number, started, status, reviewers, findings,
     size, authors)` calling `RoundsDb.RecordRound` with `RoundRecord.Size` and the authors snapshot as E2/E5 shipped them,
     and the resolution through the shipped resolve write (`RoundsDb.cs:777` path). Plus `OlderFile(dir, stopBeforeStep, sql)`
     in the `RoundCountsCliScenarioTests.cs:140-152` shape. S2–S5 seed only through this.
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
  6. *A step-21 file* (`OlderFile` stopped before step 22): rounds read, authors `NotRecorded`, size not recorded,
     `EffortRecorded = false`; the file's SHA-256 and `PRAGMA user_version` (21) unchanged afterwards.
  7. *A step-22 file* (stopped before 23): size read, authors `NotRecorded`.
  8. *Missing database* → `[]`, the data directory holds no `coai.db` afterwards.
  9. *No text crosses*: a sentinel (`SENTINEL-7f3a`) written into `plan_text`, `agent_log`, `subject`, `note`, and every
     finding's `title/why/fix/reason` → no `RoundFacts.ToString()` of the read contains it; companion (`testing.md`, *a
     structural test that matches nothing*): the same check finds the round's `branch` text, proving the probe sees strings.
- **Break-its (each watched red, then reverted).** Select `v.effort` unconditionally → scenario 6 fails with the SQLite
  `no such column` instead of `EffortRecorded = false`; drop the session filter of (b) → scenario 3 sees B's round; select
  `f.title` into a field → scenario 9; use `<=` for `until` → scenario 4.
- **Done when.** Scenarios 1–9 green, the four break-its observed; the reader is read-only (scenario 6) and text-free
  (scenario 9); suite green.
- **Depends on.** E2.S3 (step 22), E5.S2 (step 23), E4.S4 (contributors), E5.S6 (the vectors file). **Model:** brief Opus → code Sonnet → review Opus → fixes Sonnet.

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
     ambiguous launches and tokens, coder launches per family, coder rows whose requested family ≠ observed family.
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
     observed `claude-opus-x` → family opus, requested-differs 1; no coder and no main write → `no write observed`.
  9. *Effort* (D5): coders at `high` and `''` → `high+unknown`; no coders → `—`.
  10. *Reviewer set* (D6): three `done` codex rows of one model → one entry; plus one `failed` gemini → same set, failed 1,
      partial set; every reviewer failed → excluded `no reviewer answered`.
  11. *Lines* (D10): shown 80 + elided 20 code/test, excluded 500, binary 2 files, docs 40 → selected 100; docs-only round →
      `no selected lines`; binary-only → `binary only`; `--kinds docs` → the docs-only round counted with 40; `--without-elided`
      → the elided round excluded `elided`.
  12. *First match, once* (D12): one round failing every check is excluded ONCE, under `not done`; then, for EVERY value
      of `ExclusionReason` (iterated from the enum, not listed — `testing.md`, *a test that repeats a list*), a round
      failing exactly that check is excluded under it.
  13. *Periods*: a round AT the split instant is `after`.
  14. *Plan stage* (D10): a plan round with no size rows is counted, its lines `not applicable`, no size exclusion.
  15. *Conservation*: |In| + |Out| = the in-window rounds of the stage asked for (cohort filter aside).
- **Break-its.** Cohort computed after the exclusions → scenario 3 shows round 2 as `first`; contributors without the
  `≤ started_utc` bound → scenario 5's round 1 is `mixed`; `excluded` coverage added to the selected lines → scenario 11
  reads 600; D3 taking the round's OWN state → scenario 3's round 2 is counted.
- **Done when.** Scenarios 1–15 green, the four break-its observed; the unit is `static`, takes no clock, opens nothing.
- **Depends on.** S1's records. **Model:** brief Opus → code Sonnet → review Opus → fixes Sonnet.

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
     `branch` → count); selected / elided / excluded lines; binary files; delta lines and delta-unavailable per reason; per
     numerator `NumeratorSums(long Total, int CoveredRounds, long CoveredLines)`; failed and stood-down reviewer rows;
     partial-set rounds; authors-partial rounds; ambiguous launches and tokens; coder launches per family; requested-differs.
     `ExclusionCount(Cohort, Period, Reason, Rounds)`. Cells ordered by key, ordinally — the same input gives the same bytes.
  2. Figures, computed from sums only (D16), never stored back: per round = `Total / CoveredRounds`; per 100 lines =
     `100 · Total / CoveredLines`. `CoveredRounds = 0` or `CoveredLines = 0` → `Unknown`; plan stage per-100 →
     `NotApplicable`. A ratio of SUMS — never the mean of per-round ratios.
  3. Marks per D14, from `Rounds` and the branch map (largest share = max / rounds).
  4. `IReadOnlyList<string> Header(RoundAuthorsSums sums, RoundAuthorsSelection selection, bool effortMarkerKnown)` per D15,
     plus D7's and D8's sentences. Header lines are fixed sentences with values filled in — no text from any store row except
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
     association-only, asked-for-model, contribution-not-authorship and unobserved-lines sentences always present.
  8. *Exclusions conserve*: Σ cell rounds + Σ exclusion rounds = |In| + |Out| per cohort and period.
  9. *Determinism*: the same population counted twice, and in shuffled input order → equal sums.
- **Break-its.** Per 100 as the mean of per-round ratios → scenario 1 reads `5.00`; a zero denominator returning `Of(0)` →
  scenario 3; dominance at `≥ 50 %` → scenario 6's 5 of 10 marked; a `cohort = all` cell → scenario 4.
- **Done when.** Scenarios 1–9 green, the four break-its observed; sums are integers and sets only; figures exist only as
  return values of the figure functions.
- **Depends on.** S2. **Model:** brief Opus → code Sonnet → review Opus → fixes Sonnet.

---

### E6.S4 — The JSON of sums, and `--add` of two stores

- **Goal.** The default output is the sums as JSON — additive, so a WSL run saved to a file plus the Windows run print the
  combined report without opening a store across the drvfs boundary (`roundCountsRead.ts:18-21`); combining two halves equals
  reporting their union.
- **Scope.** New `RoundAuthorsMerge.cs`; `RoundAuthorsAnswer` registered in `ServerJsonContext.cs` beside
  `RoundCountsAnswer` (`:361`) — source-generated, no reflection.
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
- **Scope (verified).** New `RoundAuthorsMode.cs` (the `RoundCountsMode.cs:30-88` shape: `Run(args, env, output, note,
  readFile, clock)`, a pure `Parse` returning `(Ok, Selection, Adds, Table, Why)`), new `RoundAuthorsText.cs` (the table).
  Registration lines only: `Program.Startup` member beside `RoundCounts` (`Program.cs:287-297`), `Classify`
  (`Program.cs:339`), the dispatch (`Program.cs:441-444`), the help paragraph after `--round-counts`'s
  (`Program.cs:2452-2456`); the one-shot list `.agents/PROJECT.md:53`; the reader-mode list
  `AReaderModeNeverMakesTheDataDirectoryTests.cs:46`. Data directory resolved as `--round-counts` resolves it
  (`RoundCountsMode.cs:51`, `PanelSettings.FromEnvironment`). Unreadable store: `Program.Unreadable` /
  `Program.WhyUnreadable` (`Program.cs:1652-1660`). Instants: `Program.TryParseInstant` (`Program.cs:1769`).
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
     per round, per 100 lines, coverage (`k/n rounds`) · failed / stood-down / partial-set · authors partial · ambiguous;
     corrective tables add delta lines and delta unavailable. A figure prints `Unknown` or `n/a`, never `0` and never blank.
     Then the exclusions table (cohort · period · reason · rounds), every reason of the enum listed with its count, zeros
     included — an absent row and a zero are different claims. Fixed column order; no colour; ASCII separators.
  4. Docs in the same story: `research/module_server.md` — a `--round-authors` section after `--round-counts`'s
     (`module_server.md:3177`), with the contract, D1–D16 by reference to this document, and a Mermaid flow (facts →
     population → sums → merge → render); `research/module_tests.md` — a row per test class after `module_tests.md:3181`'s
     section, with each RED message and break-it as that section records them.
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
  7. *An older store is read, not migrated*: a step-21 file → exit 0, its code rounds under `authors not recorded`; SHA-256
     and `user_version` unchanged.
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
- [ ] A step-21 and a step-22 store are read without being migrated (S1, S5).
- [ ] `.agents/PROJECT.md` lists `--round-authors`; `module_server.md` and `module_tests.md` describe it.
- [ ] Every break-it of S1–S5 was watched red and reverted; `CoaiMcp.Tests.exe` and the family checks green.
- [ ] The parent's E6 entry links this document (owed by whoever commits the epic) and `todo/README.md` carries this
      document's row (planning-docs.md, *each stories document is a plan of its own to the checker*).
