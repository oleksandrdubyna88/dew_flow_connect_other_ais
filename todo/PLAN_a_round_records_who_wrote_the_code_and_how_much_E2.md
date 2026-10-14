# PLAN — E2 stories: a code round records the size of its change, and each reviewer's effort

> Status: **plan only, nothing implemented yet, 2026-10-14.** Epic E2 of [PLAN_a_round_records_who_wrote_the_code_and_how_much.md](PLAN_a_round_records_who_wrote_the_code_and_how_much.md).
>
> Scope: `src_mcp` only — `runners/Context` (counted numstats, the delta numstat), new pure units in `core/Context`,
> `runners/Context` and `src/Server/Rounds`, `RoundRecord.Size`, schema step 22 with its writer and its `--log` reader in
> new files, call sites in `PanelService.cs` and `RoundEngine.cs`. No extension change (the card line is E5's), no Team server
> change, no new one-shot mode. Cornerstone area of this epic (the parent's `## Epics`, E2): **the coverage × kind
> classification and the delta's definition** — marked on **E2.S1** alone; the parent's ledger line reads
> `Cornerstones: 3 of 3 — E2.S1, E3.S4, E4.S2` (parent §16 D21).

## 1. Goal, before any solution

Every later figure of the parent plan is "findings per 100 changed lines" or a cell of rounds selected by size, so this epic
writes the **denominator**. Today a code round records the range it read (`rounds.base_ref`, `rounds.head_sha`) and no size
at all; the numstat that would give one is parsed and its counts thrown away (`NumstatReader.Read`), and each reviewer's
effort is held in the session file and never projected. Two errors here would be wrong with no symptom anywhere downstream:
a file the reviewers never saw (excluded, or binary) counted as lines, and a "what the fixers changed" figure computed by
subtracting two cumulative totals. The epic ships alone (parent §11 step 1): a size on every new code round, in the session
file and the database, readable through `--log`, and `reviewers.effort` persisted.

## 2. What exists — verified at `97128954`, 2026-10-14

| Fact | Where |
|---|---|
| The code round's diff: one numstat `{against}..{sha}` with the exclusion pathspecs, parsed with `Read` (counts dropped), one `git diff` per text file | `src_mcp/runners/Context/ContextAssembler.cs:287-330` (numstat `:311`, loop `:314`) |
| `CollectedDiff(Files, ComparedAgainst, Kind)` — no counts | `ContextAssembler.cs:119` |
| The exclusions table; `Git(...)` THROWS on a non-zero exit | `ContextAssembler.cs:7-30`; `:488-494` |
| The unfiltered numstat already exists once, only for the "nothing to review" refusal | `ContextAssembler.cs:359` |
| `CollectAsync` has a second caller, the feature stage, which this epic leaves alone (parent §7) | `src_mcp/src/Server/Stages/FeatureStage.cs:241` |
| `CountedChange(Change, Added, Deleted)` and `ReadCounted`; a binary's `-` and any unparseable column read as **0** | `src_mcp/runners/Context/NumstatReader.cs:41`, `:91-119`, `:146-148` |
| Elision is decided AFTER collection: `DiffShaper.Shape` names binaries (never budgeted) and elides whole text files past 192 KiB | `src_mcp/core/Context/DiffShaper.cs:7-15`, `:30-71` (binaries `:36-39`, elision `:41-57`) |
| The call site: collect `:756`, shape `:757`, the resolved base handed to the work `:866-870`; `session` (the rounds BEFORE this one) is in scope | `src_mcp/src/Server/PanelService.cs:754-870` |
| The work carries the resolved base out of the lambda (`init` property precedent) | `src_mcp/src/Server/Rounds/RoundWork.cs:30` |
| `MakeWork` runs before the live round is appended; the finished record is built with `live.Finish(...) with { Sha = … }` | `src_mcp/src/Server/Rounds/RoundEngine.cs:355`, `:483-491` |
| The projection write — the record already carries `Caller`; `RecordRound` reads it from `record` | `RoundEngine.cs:607-624` |
| A nullable historical snapshot on the round, never materialised on load | `src_mcp/src/Server/SessionStore.cs:173-193` (`RoundRecord.Caller`) |
| `RoundRecord.Sha` — empty on a plan round and on a round from before the field | `SessionStore.cs:96-104` |
| **Trap:** a SKIPPED round is written `Status = Done`, `Verdict = skipped`, **with `Sha = sha`** — it reviewed nothing | `src_mcp/src/Server/Rounds/RoundSkips.cs:102-111` (and `:95-99` for a repeated skip) |
| The code stage records its sha; the feature stage does too (out of scope) | `src_mcp/core/Rounds/Stages.cs:78-84`, `:102-109` |
| `ReviewerState.Effort` exists, set from the invocation; only claude, remote and local launches have one | `SessionStore.cs:29-48`; `src_mcp/src/Server/LiveRound.cs:99-101`; `src_mcp/src/Server/Rounds/RosterBuilder.cs:705-712` |
| The session file and `--log` serialise through one source-generated context that **omits nulls** | `src_mcp/src/Server/ServerJsonContext.cs:339-345`, `:360` |
| Schema: 21 append-only steps, last `TheRoundsKey`; step 20 is the reviewers' model | `src_mcp/src/Store/Schema.cs:31-38`, `:597-600`, `:614-616` |
| `RecordRound` = one transaction: session, round row (upsert), reviewers (delete-then-insert), findings | `src_mcp/src/Store/RoundsDb.cs:190-202`; `RecordRoundRow` `:841-921`; `RecordReviewers` `:923-946` |
| `RoundsDb.cs` (1 285 lines) and `RoundsQuery.cs` (991) are past the 800-line ceiling; the seam for a table written elsewhere | `RoundsDb.cs:173-181` (`Command()`), used by `src_mcp/src/Store/QuestionConsultTable.cs` |
| `--log`: `LogJson` → `RoundsQuery.Read` (read-only open) → `Rounds(...)`, schema asked by `HasColumn` before a column is selected | `src_mcp/src/Program.cs:715-745`; `src_mcp/src/Store/RoundsQuery.cs:410-436`, `:470-531`, `:895-903` |
| `LoggedRound` — positional with defaults; **no reviewer rows at all** | `RoundsQuery.cs:27-102` |
| "Code" extensions: the one extension-to-language mapping | `src_mcp/core/Outlining/ISourceOutliner.cs:40-51` (`OutlineLanguages.Of`) |
| The step test that pins the count at 21 | `src_mcp/tests/TheRoundKeyIsStepTwentyOneTests.cs:79` (step 20's was relaxed the same way: `TheReviewersModelIsStepTwentyTests.cs:84`) |
| Test harnesses to mirror: real-git temp repos; a real `PanelService` round through FakeCli; `--log` as a process | `src_mcp/tests/ContextAssemblerTests.cs:10-59`; `ARoundIsMintedAKeyTests.cs`; `LogCliScenarioTests.cs` |
| Where the module doc lists schema steps | `research/module_server.md:3256-3276` |

## 3. Conventions every story follows

- **Tests first, RED on the assertion.** Each story's tests are written against a stub that returns the empty or
  "not recorded" answer and compile; they fail on the ASSERTION with the real symptom, never on a compile error or a missing
  registration. Then the code; then green.
- **Build and run** only as `CLAUDE.md` says, from the worktree root:
  `node .agents/conventions/tools/low-priority.mjs dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4`, then
  `node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*<Class>*"`,
  then the whole executable. **Never `dotnet test`.**
- **Break-its** (where a story lists them): make the named change in PRODUCTION code, run the named test, see it red with
  the real symptom, restore, see it green; both runs go in the story's summary. A break-it never edits a test, never skips
  one, never disables a tripwire.
- **Best-effort, like every projection.** No size computation, size git call or size write may fail, delay past its own
  git calls, or change the outcome of a round. A failure becomes a recorded `unavailable: …` state. Cancellation of the
  round token still propagates (`OperationCanceledException` is never swallowed).
- **No new lines in `RoundsDb.cs`, `RoundsQuery.cs`, `PanelService.cs` or `RoundEngine.cs` beyond call sites and one
  bind** — new tables are written and read from their own files through `RoundsDb.Command()` and a read-only connection.
- **Unknown is never 0.** A binary has no line count (null in the record, NULL in the database, an ABSENT field in JSON); a
  round with no size reads *not recorded*, never a zero-line change.
- **Docs per story** in the same task, where the story says so (`research/module_server.md`).
- Files are LF; tests are xUnit v3 + FluentAssertions in `src_mcp/tests/`, named as the house names them (a sentence:
  `ARoundSizeIsAPartitionTests`).

## 4. Shared contracts — fixed here so S2–S5 can be briefed now

Fable's spec for E2.S1 may rename a word or a member below. If it does, **it edits this section in the same commit as the
spec**, and no dependent story starts before that commit. It may not add a state that means zero.

**C1 — the value types** (`src_mcp/core/Context/RoundSize.cs`, created by E2.S1):

```csharp
public sealed record SizeCell(string Coverage, string Kind, int Files, int? Added, int? Removed);
public sealed record ScopeSize(string State, string From, IReadOnlyList<SizeCell> Cells);
public sealed record RoundSize(ScopeSize Whole, ScopeSize Delta, string Origin = SizeOrigin.Live); // SizeOrigin.Backfill when E7.S3 rebuilt it (parent §16 D12)
```

> Amended 2026-10-14 by E2.S1's spec (Fable), as this section allows: one state word added (`unavailable: numstats
> disagree`), the constant holders named (`ChangeCoverage`, `ChangeKind`, `ScopeState`, `SizeOrigin`), the cell order
> fixed, and `From`'s rule for an `unavailable` scope made exact. No member was renamed; no state means zero.

- `Coverage` ∈ `shown` · `elided` · `excluded` · `binary` — constants on a static `ChangeCoverage`, whose `All` lists them
  in that order; `Kind` ∈ `code` · `test` · `docs` · `config` · `unknown` — constants on `ChangeKind`, whose `All` lists
  them in that order and whose `Of(path)` is the kind function.
- One cell per (coverage, kind) that has at least one file; cells are aggregated — **no path is stored** — and ORDERED by
  coverage (`ChangeCoverage.All`) then kind (`ChangeKind.All`), so two partitions of one range are equal by value.
- `Added`/`Removed` are null **exactly** when `Coverage == binary`.
- `State` ∈ `recorded` · `unavailable: first round` · `unavailable: no predecessor sha` · `unavailable: not an ancestor` ·
  `unavailable: git failed` · `unavailable: numstats disagree` — constants on `ScopeState`. `recorded` with zero cells is an
  observed zero (the delta of two identical heads, or an empty range), and is distinct from every `unavailable`; an
  `unavailable` scope has NO cells. `numstats disagree` is the partition's own refusal (E2.S1 §S1.4): the filtered and the
  unfiltered numstat of ONE range name different files, so no partition of it would be a partition.
- `From`: for `Whole`, the resolved base the diff was taken against (= `RoundWork.BaseRef`); for `Delta`, the predecessor
  round's sha. It is KEPT on an `unavailable` scope whenever a commit was chosen before the failure (`not an ancestor`,
  `git failed`, `numstats disagree`) and empty when none was (`first round`, `no predecessor sha`).
- `Origin` ∈ `live` · `backfill` — constants on `SizeOrigin`.

**C2 — the storage** (schema step 22, E2.S3):

```sql
CREATE TABLE IF NOT EXISTS round_changes (
    round_id INTEGER NOT NULL REFERENCES rounds(id),
    scope    TEXT NOT NULL,      -- 'whole' | 'delta'
    coverage TEXT NOT NULL,
    kind     TEXT NOT NULL,
    files    INTEGER NOT NULL,
    added    INTEGER,            -- NULL for binary: no line count
    removed  INTEGER
);
CREATE INDEX IF NOT EXISTS round_changes_by_round ON round_changes (round_id);
ALTER TABLE rounds    ADD COLUMN size_state  TEXT NOT NULL DEFAULT '';  -- '' = not recorded
ALTER TABLE rounds    ADD COLUMN delta_state TEXT NOT NULL DEFAULT '';
ALTER TABLE rounds    ADD COLUMN delta_from  TEXT NOT NULL DEFAULT '';
ALTER TABLE rounds    ADD COLUMN size_origin TEXT NOT NULL DEFAULT '';  -- 'live' | 'backfill' (parent §16 D12)
ALTER TABLE reviewers ADD COLUMN effort      TEXT NOT NULL DEFAULT 'not recorded';  -- '' = applied none (parent §16 D11)
```

The `rounds` state columns are this document's addition to the parent's step 22 (§10 point 3): without them a delta that
is `unavailable` and one that is an observed zero both read as "no rows". `size_origin` (parent §16 D12) says whether a
size was measured live or reconstructed by E7.S3 with today's exclusion list and diff budget. `reviewers.effort`'s
default marks every row written before step 22 `not recorded`; a reviewer that applied no effort is written `''`
(parent §16 D11) — the two are never merged.

**C3 — the wire.** `RoundRecord.Size` (session file) and `LoggedRound.Size` (`--log`) are both `RoundSize?`, serialised by
`ServerJsonContext`: absent = not recorded; inside a binary cell `added`/`removed` are ABSENT (the context omits nulls,
`ServerJsonContext.cs:344`) and a reader treats absent as "no line count", never 0. E5's card and E6's report read this shape.

## 5. Stories

### E2.S1 — The size of a round, decided: coverage × kind, and the delta's definition (CORNERSTONE)

**Cornerstone — spec by Fable, written 2026-10-14.** Area: the coverage × kind classification and the delta's definition
(parent `## Epics`, E2). The spec is §S1.1–§S1.9 below; the goal, the acceptance criteria and the break-its that were fixed
before it stay as the contract it implements. The coder starts only once this spec is committed and the parent's ledger
names this story (`Cornerstones: 3 of 3 — E2.S1, E3.S4, E4.S2`, parent §16 D21). Every path:line below was verified at
the worktree's head on 2026-10-14. Nothing in the spec is a judgement call left to the coder: where the acceptance
criteria left a question open, §S1.9 closes it with its reason.

- **Goal.** Pure functions, no I/O, that turn the numstat records a round already has into C1's `RoundSize` — every changed
  file in exactly one coverage and one kind, binaries with no line count, excluded files counted apart and never in a
  denominator — and that decide the delta: which earlier round it is measured against, or why it cannot be, so that the
  only delta this codebase can express is a head-to-head numstat between two commits.
- **Acceptance criteria.**
  1. **Pure and placed.** C1's types and `ChangeKind.Of` in `core/Context`; the partition over `CountedChange`
     (`NumstatReader.cs:41`) and `ElidedFile` (`DiffShaper.cs:9`) in `runners/Context`; the predecessor selection over
     `RoundRecord`s in `src/Server/Rounds`. No process, clock, file or database in any of them. Fable may move a unit if the
     project references require it and says so.
  2. **A partition.** Input: the filtered (reviewable) counted records, the UNFILTERED counted records of the same range,
     and the shaper's elided paths. Every unfiltered record lands in exactly one coverage; the sum of `Files` over a scope's
     cells equals the unfiltered record count; no path is counted twice; a reviewable record absent from the unfiltered list
     is a defect the function reports (it cannot happen against one pinned range), never a silent drop.
  3. **Coverage precedence is stated and tested**, including the overlaps the parent leaves open: a binary that is also
     excluded (`**/*.lockb`), a binary elided (impossible by `DiffShaper.cs:36-39` — asserted, not assumed), a rename whose old
     path is excluded and new path is not (`dist/a.js → src/a.js`) and the reverse. "Excluded" is decided by set difference
     against the filtered numstat (what the reviewers were actually given), never by re-matching `DiffExclusions` globs —
     one decider, git's.
  4. **Binary = no line count.** `Added`/`Removed` null exactly for `binary`, whatever `NumstatReader.Lines` returned
     (`NumstatReader.cs:146-148` turns `-` into 0).
  5. **The kind function** follows parent §7's first-match table — `test`, `docs`, `config`, `code`, `unknown` — on the
     record's NEW path, `/`-separated; the spec states case sensitivity, what "a path segment" means, dotfiles, and that
     `code` is `OutlineLanguages.Of(path) != Unsupported` (`ISourceOutliner.cs:40-51`) and never a second extension list.
     A table test covers every row of the parent's table plus this repository's own shapes (`src_mcp/tests/X.cs`,
     `src_vs_code/src/test/x.test.ts`, `research/x.md`, `Directory.Packages.props`, `.gitignore`, `x.ps1`, `x.razor`).
  6. **The predecessor.** The delta is measured from the latest earlier round of this gate session that is a CODE round,
     `Status == done`, **not `Verdict == skipped`** (`RoundSkips.cs:102-108` writes a skip done and with a sha), and with a
     non-empty `Sha`; the spec decides, with its reason, whether a round whose every reviewer failed qualifies. None at all →
     `unavailable: first round`; only rounds without a sha (pre-#490 records) → `unavailable: no predecessor sha`.
  7. **The delta is a range, never arithmetic.** The decision's output is `(from, to)` commits or an `unavailable` state; the
     classification of the delta runs the SAME partition over the delta range's own two numstats. No API in this unit
     accepts two `RoundSize`s or two totals. A `from` that is not an ancestor of `to` → `unavailable: not an ancestor` (the
     git question itself is E2.S2's; the state and its precedence over the others are decided here). `from == to` →
     `recorded`, zero cells.
  8. **Delta coverage.** The spec decides how `shown`/`elided` apply to the delta scope (this document's reading: a delta
     file is `elided` when the round's own shaped diff elided it) and what a delta file ABSENT from the whole diff is (a fix
     that returned a file to the base) — every such file still in exactly one coverage, tested.
  9. **States.** Only C1's words; `recorded` + zero cells is an observed zero; a failure of the unfiltered numstat makes
     the scope `unavailable: git failed` with no cells — never a partition missing its excluded files presented as complete.

**S1.1 — Units and where they live.** Four new files, no existing file edited. Every unit is `static`, takes values and
returns values: no process, no clock, no file, no database, no logger. The complexity ceiling is the repository's
(`src_mcp/CodeMetricsConfig.txt`: `CA1502: 4`), per function, and the shapes below are cut so that it holds without a
suppression.

| File | Unit | Project | Why there |
|---|---|---|---|
| `src_mcp/core/Context/RoundSize.cs` | `ChangeCoverage`, `ScopeState`, `SizeOrigin`, `SizeCell`, `ScopeSize`, `RoundSize` (C1, final) | `CoaiMcp.Core` | the record is serialised into the session file and `--log` (C3); the core is where every other wire record of a round's diff lives (`DiffShaper.cs:7-15`) |
| `src_mcp/core/Context/ChangeKind.cs` | `ChangeKind` — the constants, `All`, and `Of(path)` | `CoaiMcp.Core` | its one dependency, `OutlineLanguages.Of`, is core (`src_mcp/core/Outlining/ISourceOutliner.cs:32-52`) |
| `src_mcp/runners/Context/ChangePartition.cs` | `ChangePartition.Of(...)` → `ScopeSize`; `internal static Disagreement(...)` | `CoaiMcp.Runners` | its inputs are `CountedChange` (`src_mcp/runners/Context/NumstatReader.cs:41`) and the shaper's `ElidedFile` paths (`src_mcp/core/Context/DiffShaper.cs:9`); runners reference core (`runners/CoaiMcp.Runners.csproj:18`), never the reverse |
| `src_mcp/src/Server/Rounds/DeltaPredecessor.cs` | `Predecessor` record; `DeltaPredecessor.Choose(...)` | `CoaiMcp` (`src`) | its input is `RoundRecord` (`src_mcp/src/Server/SessionStore.cs:78-233`), a server type; `internal`, visible to the tests (`src/CoaiMcp.csproj:61`) |

The purity test that already stands (`src_mcp/tests/ArchitectureTests.cs:17-24`) covers the two core files by assembly
reference; the two others are held pure by review and by their signatures, which admit no `IProcessLauncher`, no
`SessionStore` and no `CancellationToken`. `System.IO.Path.GetExtension` inside `OutlineLanguages.Of` is a string function
and is already in the core.

**S1.2 — The types, final (C1).** Exactly this, in `RoundSize.cs`; §4 C1 was amended to match in this commit.

```csharp
namespace CoaiMcp.Core.Context;

/// <summary>Where a changed file stood in the reviewers' view — one of four, every file in exactly one.</summary>
public static class ChangeCoverage
{
    public const string Shown = "shown";       // in the reviewers' diff, whole
    public const string Elided = "elided";     // named to the reviewers, not shown: over the shaper's budget (DiffShaper.cs:41-57)
    public const string Excluded = "excluded"; // never reached the reviewers: hidden by the exclusion pathspecs (ContextAssembler.cs:7-30, :294)
    public const string Binary = "binary";     // no line count exists (NumstatReader.cs:66-67)
    /// <summary>The order cells are written and compared in.</summary>
    public static readonly ImmutableArray<string> All = [Shown, Elided, Excluded, Binary];
}

/// <summary>Why a scope has the cells it has, or none — a closed vocabulary; C2's `size_state`/`delta_state` column.</summary>
public static class ScopeState
{
    public const string Recorded = "recorded";
    public const string FirstRound = "unavailable: first round";
    public const string NoPredecessorSha = "unavailable: no predecessor sha";
    public const string NotAnAncestor = "unavailable: not an ancestor";
    public const string GitFailed = "unavailable: git failed";
    public const string NumstatsDisagree = "unavailable: numstats disagree";
}

/// <summary>Whether a size was measured by the round that ran, or rebuilt later with today's rules (parent §16 D12).</summary>
public static class SizeOrigin
{
    public const string Live = "live";
    public const string Backfill = "backfill";
}

/// <summary>One (coverage, kind) cell of a scope: how many files, and their lines — null lines for a binary, never 0.</summary>
public sealed record SizeCell(string Coverage, string Kind, int Files, int? Added, int? Removed);

/// <summary>One scope — the whole range or the delta — with its state, the commit it starts at, and its cells.</summary>
public sealed record ScopeSize(string State, string From, IReadOnlyList<SizeCell> Cells)
{
    /// <summary>An unavailable scope: the state says why, the cells are none, `from` is the commit chosen before the failure or empty.</summary>
    public static ScopeSize Unavailable(string state, string from = "") => new(state, from, []);

    /// <summary>A measured zero — two identical heads, or an empty range — which is a `recorded` scope with no cells.</summary>
    public static ScopeSize ObservedZero(string from) => new(ScopeState.Recorded, from, []);
}

/// <summary>What a code round changed, by scope, coverage and kind — the denominator of every later figure (parent §7).</summary>
public sealed record RoundSize(ScopeSize Whole, ScopeSize Delta, string Origin = SizeOrigin.Live);
```

Rules of the shape, each held by a test in §S1.7 rather than by a constructor (the session-file reader must accept any
shape a file carries — the `Caller` precedent, `SessionStore.cs:185-192` — so the invariants are the partition's, not the
record's): `Added`/`Removed` are null ⇔ `Coverage == Binary`; `Cells` non-empty ⇒ `State == Recorded`; no two cells share
a (coverage, kind); cells are in `ChangeCoverage.All` then `ChangeKind.All` order; every cell has `Files ≥ 1`. **No
instance computed property** on these three records: the source-generated context serialises every public property, and a
`bool IsRecorded` would land in every session file (`SourceOutline.IsOutlined` needs `[JsonIgnore]` for exactly this,
`ISourceOutliner.cs:116-117`). Registering `RoundSize` in `ServerJsonContext` is E2.S3's (C3).

**S1.3 — The kind function.** `ChangeKind.Of(path)` → one of five words; `ChangeKind.All = [Code, Test, Docs, Config,
Unknown]` (the report's default denominator first).

- **Input.** The path as `git diff --numstat -z` prints it (`NumstatReader.cs:73-79`): `/`-separated, unquoted. The
  function does NOT normalise `\` to `/` — git never prints a backslash as a separator, and on Linux a backslash is a
  character of a name. It never throws: an empty string, or a path whose last component is empty (`a/b/`), is `unknown`.
- **Split.** On `/`. `dirs` = every component but the last; `name` = the last component. A "path segment" in the rules below
  is one of `dirs` compared WHOLE — `contest/` is not `test/`, `testing/` is not `test/`, `Foo.Tests/` is not `tests/` —
  and at any depth. The name rules apply to `name` alone.
- **Case.** Every comparison is `StringComparison.OrdinalIgnoreCase`: `Tests/Foo.cs`, `README.MD` and `X.JSON` are what
  they look like. This is deliberately NOT how `DiffExclusions` matches (ordinal, `ContextAssembler.cs:38`): the exclusions
  mirror a git pathspec and must agree with git byte for byte; the kind is this product's own classification with no git
  twin, and one that put `Tests/` and `tests/` in two kinds would split one convention over the casing of a folder.
- **Rules, first match wins** (parent §7's table, made exact):

| # | Kind | Holds when |
|---|---|---|
| 1 | `test` | a segment ∈ {`test`, `tests`, `__tests__`}; or `name` contains `.test.` or `.spec.`; or `name` ends with `Tests.cs`; or `name` starts with `test_` |
| 2 | `docs` | `name` ends with `.md` or `.txt`; or a segment ∈ {`research`, `todo`, `docs`} |
| 3 | `config` | `name` ends with one of `.json`, `.yml`, `.yaml`, `.toml`, `.props`, `.targets`, `.csproj`, `.slnx`, `.config`; or `name` starts with `.` (a dotfile: `.gitignore`, `.editorconfig`, `.gitattributes`, `.eslintrc.json`) |
| 4 | `code` | `OutlineLanguages.Of(path) != OutlineLanguage.Unsupported` (`ISourceOutliner.cs:40-51`: `.cs`, `.ts/.mts/.cts`, `.tsx`, `.js/.mjs/.cjs/.jsx`, `.rs`, `.php`, `.py/.pyi`) — and NOTHING else: never a second extension list, so a language the outliner learns is a kind this function learns |
| 5 | `unknown` | everything else — in this repository that is `.ps1`, `.sh`, `.razor`, `.html`, `.css`, `.xml`, `.sln`, `.ini`, images, `.lockb`, a submodule's gitlink, a file with no extension. The report prints the word (parent §10 point 7); nothing here guesses |

- **Shape that keeps every function at ≤ 4.** An ordered table `(string Kind, Func<string[] Dirs, string Name, bool>
  Holds)[]` with a final always-true `unknown` row; `Of` = split, then `Rules.First(r => r.Holds(dirs, name)).Kind`. The
  word lists (`TestSegments`, `DocsSegments`, `TestInfixes`, `DocsSuffixes`, `ConfigSuffixes`) are `static readonly
  string[]` matched with `Any`; the name predicates are small private statics (`IsTestName`, `IsConfigName`) so no
  predicate carries more than three `||`.
- **The table test** (`EveryPathInTheTable_HasTheKindTheTableSays`, §S1.7) is the specification's own rows; the coder
  copies them, never invents the expected column:

| Path | Kind | Row |
|---|---|---|
| `src_mcp/tests/X.cs` · `tests/v2.Tests/Foo.cs` · `src/__tests__/a.js` · `Tests/Foo.cs` | `test` | 1, segment (any case) |
| `src_vs_code/src/test/x.test.ts` · `src/Foo.spec.ts` · `src/FooTests.cs` · `scripts/test_runner.py` · `tools/test_helpers.ps1` | `test` | 1, name |
| `tests/fixtures/data.json` · `tests/README.md` | `test` | 1 before 2 and 3 |
| `research/x.md` · `todo/README.md` · `README.md` · `src/notes.TXT` · `CodeMetricsConfig.txt` | `docs` | 2, name |
| `docs/openapi.json` · `src_vs_code/docs/x.png` | `docs` | 2, segment, before 3 |
| `Directory.Packages.props` · `src_mcp/src/CoaiMcp.csproj` · `dew_flow_connect_other_ais.slnx` · `appsettings.json` · `Cargo.toml` · `.github/workflows/ci.yml` · `web.config` | `config` | 3, name |
| `.gitignore` · `.editorconfig` · `src/.eslintrc.json` | `config` | 3, dotfile |
| `src/a.cs` · `b.ts` · `c.tsx` · `d.mjs` · `e.rs` · `f.php` · `g.pyi` · `h.d.ts` · `src/Foo.Tests/Bar.cs` · `src/testing/Helper.cs` · `contest/x.cs` | `code` | 4 (the last three: a segment is a whole name) |
| `latest.cs` (the `.test.` infix needs both dots) | `code` | 4, not 1 |
| `x.ps1` · `x.razor` · `x.sh` · `x.html` · `x.css` · `x.xml` · `x.sln` · `logo.png` · `bun.lockb` · `.agents/conventions` · `src/test` (a bare name) · `""` (empty) · `a/b/` (empty last component) | `unknown` | 5 |

**S1.4 — The partition.** `ChangePartition.Of(filtered, unfiltered, elided, from)` → `ScopeSize`, in
`src_mcp/runners/Context/ChangePartition.cs`.

```csharp
namespace CoaiMcp.Runners.Context;

public static class ChangePartition
{
    /// <param name="filtered">The reviewable numstat of the range — WITH the exclusion pathspecs: what the reviewers were given (ContextAssembler.cs:311; E2.S2's delta twin).</param>
    /// <param name="unfiltered">Every record of the SAME range, no pathspecs (the :359 shape; E2.S2's AllChangesAsync / the delta's unfiltered list).</param>
    /// <param name="elided">The paths the shaper named but did not show — ShapedDiff.Elided[*].Path (DiffShaper.cs:9, :56).</param>
    /// <param name="from">The commit the range starts at — ScopeSize.From.</param>
    public static ScopeSize Of(
        IReadOnlyList<CountedChange> filtered,
        IReadOnlyList<CountedChange> unfiltered,
        IReadOnlyCollection<string> elided,
        string from);

    /// <summary>Why the three inputs cannot be partitioned — one of three fixed sentences, no path in any — or empty.</summary>
    internal static string Disagreement(
        IReadOnlyList<CountedChange> filtered,
        IReadOnlyList<CountedChange> unfiltered,
        IReadOnlyCollection<string> elided);
}
```

*The algorithm*, four steps, each its own private static; complexity per function in brackets.

1. **Match** [3] — every unfiltered record `U` finds at most one filtered record `F`, by the first rule that holds, paths
   compared **ordinal** (git's trees are case-sensitive and two names differing in case are two files; this is the same
   comparison `DiffExclusions` makes, `ContextAssembler.cs:38`):
   - (a) an `F` with `F.Change.Path == U.Change.Path` — the ordinary case, a rename kept whole (both names in both
     numstats), a mode-only change, a deletion;
   - (b) else an `F` with `F.Change.Path == U.Change.RenamedFrom` — the rename's NEW name is excluded, so the filtered
     numstat shows the OLD name as a deletion (`src/a.js → dist/a.js`: `U` = `dist/a.js ← src/a.js`, `F` = `0 N src/a.js`);
     `RenamedFrom` is empty on every non-rename (`NumstatReader.cs:110`), and no filtered record has an empty path
     (`NumstatReader.cs:108,161`), so the rule needs no length test;
   - (c) else an `F` with `F.Change.RenamedFrom == U.Change.Path` — the filtered numstat paired this path into a rename the
     unfiltered one did not (git skips rename detection past `diff.renameLimit`, and the unfiltered range has more files);
     this always collides with another `U`'s match and is caught in step 2 — the rule exists so that case is REPORTED, not
     filed as `excluded`;
   - (d) none → `U` is excluded.
   Implemented as `ByPath(filtered, U.Path) ?? ByPath(filtered, U.RenamedFrom) ?? ByRenamedFrom(filtered, U.Path)`, where
   each helper is a `FirstOrDefault` over `filtered`.
2. **Disagreement** [4] — `Disagreement(...)` runs step 1 over every `U` and returns the FIRST of these sentences that
   holds, else empty; `Of` answers `ScopeSize.Unavailable(ScopeState.NumstatsDisagree, from)` on any of them — never a
   partition missing a file presented as complete (criterion 2, 9):
   - `"a reviewable file is not in the unfiltered numstat"` — an `F` no `U` matched. Impossible against one pinned range;
     reported, never dropped.
   - `"two unfiltered records match one reviewable file"` — an `F` matched twice (the rename-limit case of 1(c); also a
     copy into an excluded folder of a file that was itself modified, under a user's `diff.renames = copies`). The two
     numstats disagree on how many files there are; no count of them is honest.
   - `"an elided path is not a reviewable file"` — an elided path equal to no `F.Change.Path`. The shaper's list is built
     from the filtered records' own paths (`ContextAssembler.cs:314-326` → `DiffShaper.cs:41-57`), so this is a wiring
     defect, and a size that silently ignored it would mis-cover the file.
   These sentences carry no path; E2.S4 may log the sentence at Warning as it stands.
3. **Place** [4] — one `U` with its `F` (or none) lands in one coverage and one kind, by the first rule that holds:

   | # | Rule | Coverage | `Added` / `Removed` |
   |---|---|---|---|
   | 1 | `U.Change.IsBinary` | `binary` | `null` / `null` — whatever `Added`/`Deleted` the reader produced (`NumstatReader.cs:139-148` turns `-` into 0; a 0 here is the defect this epic exists to prevent) |
   | 2 | `F` is none | `excluded` | `U.Added` / `U.Deleted` — the unfiltered record is the only one that saw the file |
   | 3 | `elided` contains `F.Change.Path` — ordinal, through one `HashSet<string>(elided, StringComparer.Ordinal)` built per call | `elided` | `F.Added` / `F.Deleted` |
   | 4 | otherwise | `shown` | `F.Added` / `F.Deleted` |

   The kind is `ChangeKind.Of(U.Change.Path)` — the file's name at the head of the range (for a deletion, its only name),
   the one name every record of the range carries; for a rename across the exclusion boundary that is `dist/a.js`, not
   the `src/a.js` the reviewers saw deleted. Lines of a `shown`/`elided` file come from `F` because coverage describes the
   reviewers' view and the lines of a shown file are the lines they were shown — for `dist/a.js → src/a.js` the filtered
   numstat is a whole-file addition (`+40 −0`) and that is what the per-file diff sent (`ContextAssembler.cs:322-326`
   asks git for `F`'s own pathspecs), while the unfiltered one is the rename's edit (`+2 −1`).
   **Precedence: `binary` beats `excluded` beats `elided` beats `shown`.** A `**/*.lockb` is binary AND excluded; it is
   `binary`, because the null-line invariant (rule 1) must hold on every row and an excluded binary counted under
   `excluded` would have to carry a 0. Nothing is lost that any denominator could use: neither word's lines enter one
   (parent §8). A binary is never `elided` — `DiffShaper.Shape` names binaries outside the budget (`DiffShaper.cs:36-39`)
   — and the test asserts it against the shaper rather than assuming it.
4. **Cells** [2] — group the placed files by (coverage, kind): `Files` = the count; `Added`/`Removed` = the sum, or
   `null` when the coverage is `binary` (every member of such a group is null, by rule 1); order by
   `ChangeCoverage.All.IndexOf` then `ChangeKind.All.IndexOf`. `new ScopeSize(ScopeState.Recorded, from, cells)`. An
   empty `unfiltered` (and therefore, past step 2, an empty `filtered`) yields `recorded` with zero cells — the observed
   zero of criterion 7/9, never an `unavailable`.

Invariants the steps guarantee (each a test): every `U` is placed exactly once, so Σ`Files` over the cells = `unfiltered.Count`
(criterion 2); every `F` is claimed exactly once or the scope is `numstats disagree`; `Added`/`Removed` null ⇔ `binary`.

*Edge cases*, each with its outcome — none is left to the coder:

| Case | Numstat shape (`added removed path [← from]`) | Outcome |
|---|---|---|
| Rename kept whole | `U` = `F` = `2 1 src/new.cs ← src/old.cs` | 1(a); `shown`, +2 −1, kind of `src/new.cs` |
| Rename out of an excluded folder | `U` = `2 1 src/a.js ← dist/a.js`; `F` = `40 0 src/a.js` | 1(a); `shown`, **+40 −0** (`F`'s), `code` |
| Rename into an excluded folder | `U` = `2 1 dist/a.js ← src/a.js`; `F` = `0 40 src/a.js` | 1(b); `shown`, **+0 −40**, kind of `dist/a.js` = `code` |
| Two files swapped | `U1` = `b ← a`, `U2` = `a ← b`; `F` the same | 1(a) resolves each by its new name before 1(b) could cross them |
| Copy (`diff.renames = copies`) | `U` = `3 0 src/b.cs ← src/a.cs`, and `src/a.cs` modified or not | 1(a) on `src/b.cs`; the source matches its own record by 1(a) first, so no collision |
| Mode-only change (`chmod +x`) | `0 0 tools/run.sh` in both | `shown`, +0 −0, `unknown` — a changed file with no changed lines, which is what git said |
| Submodule pointer moved | `1 1 .agents/conventions` in both (git diffs a gitlink as one line) | `shown`, +1 −1, `unknown`; a numstat cannot tell a gitlink from a one-line file, and the function does not try (stated limit) |
| Deleted file | `0 25 src_mcp/tests/OldTests.cs` | `shown`, +0 −25, kind of its only name: `test` |
| Binary, excluded too | `U` = `- - bun.lockb`; no `F` | rule 1 before rule 2: `binary`, null, `unknown` |
| Binary named elided (cannot happen) | `elided` contains a binary `F`'s path | rule 1 first: still `binary`; the shaper test proves `Shape([binary], maxBytes: 0).Elided` is empty |
| Elided text file | `U` = `F` = `900 0 src/big.cs`; `elided` = [`src/big.cs`] | `elided`, +900 −0, `code` |
| Empty range | both lists empty, `elided` empty | `recorded`, zero cells, `From` kept |
| A path with a tab or a non-ASCII name | one record, as `-z` delivers it (`NumstatReader.cs:125-137`) | nothing special: matched by the whole string |
| Filtered record missing from the unfiltered list | `F` = `1 0 src/x.cs`; `unfiltered` empty | `unavailable: numstats disagree`, no cells |
| Rename-limit skip in the unfiltered numstat | `U1` = `0 10 a`, `U2` = `12 0 b`; `F` = `2 0 b ← a` | `U2`→`F` by 1(a), `U1`→`F` by 1(c): claimed twice → `numstats disagree` |
| Two names differing only in case | `U` = `A.cs`; `F` = `a.cs` | ordinal: `F` unclaimed → `numstats disagree` (a lie either way would be a count) |

**S1.5 — The delta: the predecessor, the range, the states.** `src_mcp/src/Server/Rounds/DeltaPredecessor.cs`:

```csharp
namespace CoaiMcp.Server;

/// <summary>The round a delta is measured from — its sha — or the ScopeState word saying why there is none.</summary>
internal sealed record Predecessor(string Sha, string State)
{
    public bool IsChosen => State.Length == 0;
    public static Predecessor Of(string sha) => new(sha, string.Empty);
    public static readonly Predecessor FirstRound = new(string.Empty, ScopeState.FirstRound);
    public static readonly Predecessor NoSha = new(string.Empty, ScopeState.NoPredecessorSha);
}

internal static class DeltaPredecessor
{
    /// <summary>The string a code round's record carries in `Stage` — `Stage.CodeReview.ToString()` (LiveRound.cs:269-271, RoundSkips.cs:102).</summary>
    private const string CodeStage = nameof(Core.Rounds.Stage.CodeReview);

    /// <param name="earlier">The gate session's trail BEFORE this round — `session.Rounds` as the MakeWork lambda receives it (RoundEngine.cs:355); never re-loaded.</param>
    public static Predecessor Choose(IReadOnlyList<RoundRecord> earlier);
}
```

- **Candidate** [3]: `r.Stage == CodeStage && r.Status == RoundRecord.Done && r.Verdict != RoundRecord.Skipped`, every
  comparison ordinal (`Stages.PhraseOf` parses the persisted stage with `ignoreCase: false`, `Stages.cs:131-134`; the
  constants are `SessionStore.cs:87`, `:121`). So: a `running` or `interrupted` round never (`SessionStore.cs:86-88`); a
  plan, document or feature round never — a feature round records a sha (`Stages.cs:102-109`) over a different range and
  is out of this epic's scope (parent §7); a skipped round never, though it is written `Done` WITH a sha
  (`RoundSkips.cs:102-111`, and `:95-99` for the coalesced repeat) — it reviewed nothing (parent §16 D1).
- **Choose** [3]: no candidate → `Predecessor.FirstRound`. Else the LAST candidate in list order whose `Sha is
  { Length: > 0 }` → `Predecessor.Of(that sha)`; candidates exist but none has a sha (records from before #490,
  `SessionStore.cs:96-104`) → `Predecessor.NoSha`. List order, not `CompletedUtc`: the trail is appended in time order and
  a coalesced skip updates its row in place, so position is the record's own ordering and a clock comparison would be a
  second one. "Latest WITH a sha" rather than "latest, which must have a sha": after #490 the newest rounds carry shas and
  the oldest do not, and measuring from the newest commit any round read is the delta's point.
- **A round whose every reviewer failed QUALIFIES.** It completes `Done` with a verdict and its sha
  (`RoundEngine.cs:373-375`, `FeatureStage.cs:46` for the feature twin): the diff was collected and sent, the reviewers
  did not answer. The
  delta measures what changed between two heads the gate was ASKED about, not between two heads that received findings;
  and parent §16 D1 names exactly two exclusions (skipped, interrupted) for the attribution windows — a third one here
  would make the delta and E4's "previous recorded round" disagree about which round came before.
- **The range is `{Predecessor.Sha}..{sha}` and the states have a precedence**, decided here, asked by E2.S2's
  `DeltaAsync` and assembled by E2.S4 — the pure units answer the first two rows and the last two; git answers the middle:

  | Order | Condition | `Delta.State` | `From` | Cells |
  |---|---|---|---|---|
  | 1 | no candidate | `unavailable: first round` | empty | none |
  | 2 | candidates, none with a sha | `unavailable: no predecessor sha` | empty | none |
  | 3 | `from` equals `sha` (`OrdinalIgnoreCase`, as `UncommittedAsync` compares heads, `ContextAssembler.cs:377`) | `recorded` | `from` | **zero** — E2.S2 runs no process; the partition of two empty lists is the same answer |
  | 4 | `from` fails `IsCommit` (`ContextAssembler.cs:408-409`), or `merge-base --is-ancestor` exits other than 0/1, or either numstat exits non-zero | `unavailable: git failed` | `from` | none |
  | 5 | `merge-base --is-ancestor {from} {sha}` exits 1 — a rebase, a reset, an amended head | `unavailable: not an ancestor` | `from` | none; no numstat is run |
  | 6 | the partition's `Disagreement` is non-empty | `unavailable: numstats disagree` | `from` | none |
  | 7 | otherwise | `recorded` | `from` | the partition of the delta's own two numstats |

  Row 4 outranks row 5 only where they cannot both hold (a `from` that is not a commit never reaches the ancestry
  question); when `is-ancestor` itself fails to run, that is row 4. Nothing in this unit or in E2.S2/E2.S4 takes two
  `ScopeSize`s, two `RoundSize`s or two totals (criterion 7) — §S1.7's reflection test holds the API to it.
- **Delta coverage** (criterion 8): the delta scope is `ChangePartition.Of(deltaFiltered, deltaUnfiltered, elided, from)`
  with `elided` = the ROUND's own shaped diff's elided paths (`shaped.Elided`, the same set the whole scope used) and the
  delta's own two numstats deciding `excluded` by the same set difference — one decider, git's, for both scopes. So a
  delta file is `elided` when this round's reviewers were told it was over budget, `excluded` when the delta's pathspecs
  hid it, `binary` when it has no lines, else `shown`. **A delta file absent from the whole diff** — a fix that returned a
  file to the base, so the cumulative `{against}..{sha}` no longer lists it — is `shown`: the delta's coverage answers
  "what hid this file from a reviewer", and nothing did; its disappearance from the whole scope is the fix's effect and
  the whole scope already shows it by the file's absence. For the report (parent §8) those lines are fixer work and belong
  in the corrective cohort's denominator, which `shown` is.

**S1.6 — Worked examples.** Input numstat rows → output cells, in the order the cells are returned. The first is
`ContextAssemblerTests.cs:18-37`'s own fixture, so E2.S2's git test and this table describe one change.

| # | `unfiltered` | `filtered` | `elided` | Cells / state |
|---|---|---|---|---|
| 1 | `1 1 app.cs` · `1 0 package-lock.json` · `1 0 dist/bundle.js` · `- - logo.png` | `1 1 app.cs` · `- - logo.png` | — | `shown/code` 1 +1 −1 · `excluded/code` 1 +1 −0 (`dist/bundle.js`) · `excluded/config` 1 +1 −0 (the lock file) · `binary/unknown` 1 null null — Σ files 4 |
| 2 | `900 0 src/big.cs` · `3 1 src/small.cs` | same | `src/big.cs` | `shown/code` 1 +3 −1 · `elided/code` 1 +900 −0 |
| 3 | `2 1 src/new.cs ← src/old.cs` | same | — | `shown/code` 1 +2 −1 |
| 4 | `2 1 src/a.js ← dist/a.js` | `40 0 src/a.js` | — | `shown/code` 1 +40 −0 |
| 5 | `2 1 dist/a.js ← src/a.js` | `0 40 src/a.js` | — | `shown/code` 1 +0 −40 |
| 6 | `- - bun.lockb` | (none) | — | `binary/unknown` 1 null null |
| 7 | `0 0 tools/run.sh` | same | — | `shown/unknown` 1 +0 −0 |
| 8 | `1 1 .agents/conventions` | same | — | `shown/unknown` 1 +1 −1 |
| 9 | `0 25 src_mcp/tests/OldTests.cs` | same | — | `shown/test` 1 +0 −25 |
| 10 | (none) | (none) | — | `recorded`, no cells, `From` = the argument |
| 11 | (none) | `1 0 src/x.cs` | — | `unavailable: numstats disagree`, no cells, `From` kept |
| 12 (a delta) | `0 4 src/a.cs` · `2 0 src/b.cs` — round N reverted `a.cs` to the base, edited `b.cs` | same | — (`a.cs` is not in the whole diff any more) | `shown/code` **2 +2 −4** — never `−4` or "absent", which is what a subtraction of two wholes would say |
| 13 | `5 0 research/x.md` · `1 1 README.md` · `3 0 Directory.Packages.props` · `2 0 .gitignore` · `7 0 src/Foo.cs` · `4 0 tests/FooTests.cs` | same | — | `shown/code` 1 +7 −0 · `shown/test` 1 +4 −0 · `shown/docs` 2 +6 −1 · `shown/config` 2 +5 −0 |
| 14 | `0 10 a` · `12 0 b` | `2 0 b ← a` | — | `unavailable: numstats disagree` (two records, one reviewable file) |
| 15 | `3 0 src/Big.cs` | `3 0 src/Big.cs` | `src/big.cs` | `unavailable: numstats disagree` (the elided path is no reviewable file — ordinal) |

**S1.7 — Tests.** Three files, xUnit v3 + FluentAssertions, pure (no git, no temp directory). RED first: each file is
written against a stub — `ChangeKind.Of` returning `unknown`, `ChangePartition.Of` returning `ScopeSize.ObservedZero(from)`,
`DeltaPredecessor.Choose` returning `Predecessor.FirstRound` — that COMPILES, so every failure is on the assertion with the
real symptom. The named test is the one the stub turns red; a test the stub already satisfies is marked (stub-green) and
earns its red through a break-it in §S1.8.

`src_mcp/tests/AChangedFileHasOneKindTests.cs`

| Test | Proves | RED under the stub |
|---|---|---|
| `EveryPathInTheTable_HasTheKindTheTableSays` (`[Theory]`, every row of §S1.3's table) | the table | `Expected "unknown" to be "test"` on the first non-unknown row |
| `TheFirstMatchingRowWins_TestBeforeDocsBeforeConfigBeforeCode` | `tests/fixtures/data.json` → test, `docs/openapi.json` → docs, `tests/README.md` → test | same shape |
| `CodeIsExactlyWhatTheOutlinerMaps_NeverASecondList` | for every `OutlineLanguage` ≠ `Unsupported` one path is `code`; `.ps1 .razor .sh .html .css` are `unknown` | `Expected "unknown" to be "code"` |
| `TheComparisonIgnoresCase` | `Tests/Foo.cs`, `README.MD`, `X.JSON`, `SRC/TEST/a.ts` | `Expected "unknown" to be "test"` |
| `ASegmentIsAWholeDirectoryName_AtAnyDepth` | `contest/x.cs` → code, `src/testing/x.cs` → code, `a/b/c/tests/d.cs` → test, `src/test` (bare name) → unknown | `Expected "unknown" to be "code"` |
| `AnEmptyOrTrailingSlashPath_IsUnknownAndNeverThrows` | `""`, `"a/b/"` | stub-green; red under break-it 9 |

`src_mcp/tests/ARoundSizeIsAPartitionTests.cs` (the examples are §S1.6's numbers; a helper builds `CountedChange`s from
`"added removed path [← from]"` strings so a test reads like the table)

| Test | Example | RED under the stub |
|---|---|---|
| `EveryUnfilteredFile_LandsInExactlyOneCell_AndTheFilesSumToTheRecordCount` | 1 | `Expected Cells to contain 4 item(s), but found 0` (the sum assertion fails first) |
| `AnExcludedFileIsCountedApart_NeverAsShown` | 1 — `shown/code` is exactly `+1 −1`, the lock file's `+1` sits in `excluded/config` | cells empty |
| `ABinaryHasNoLineCount_WhateverTheReaderGave` | 1, and a hand-built `CountedChange(new("logo.png", "", IsBinary: true), 7, 7)` | `Expected Added to be <null>` is break-it 2's symptom; under the stub, cells empty |
| `ABinaryThatIsAlsoExcluded_IsBinary` | 6 | cells empty |
| `ABinaryIsNeverElided_AssertedAgainstTheShaper` | `DiffShaper.Shape([new FileDiff("x.png", "", IsBinary: true, BinaryBytes: 9)], maxBytes: 0).Elided` is empty; and the partition handed an `elided` set naming a binary `F` still says `binary` | stub-green on the first half; the second half: cells empty |
| `AnElidedFileIsElided_WithItsLinesCounted` | 2 | cells empty |
| `ARenameKeptWhole_IsOneShownFile` | 3 | cells empty |
| `ARenameOutOfAnExcludedFolder_IsShownWithTheLinesTheReviewersSaw` | 4 — `+40 −0`, not `+2 −1` | cells empty |
| `ARenameIntoAnExcludedFolder_IsShownAsTheDeletionTheReviewersSaw` | 5 — `+0 −40`, kind `code` | cells empty |
| `ASwapOfTwoFiles_MatchesEachByItsNewName` | `U1` = `3 0 b ← a`, `U2` = `0 3 a ← b`; `F` same → `shown/unknown` 2 +3 −3 and NOT `numstats disagree` | state right, cells empty |
| `AModeOnlyChange_IsAFileWithZeroLines` | 7 | cells empty |
| `ASubmodulePointer_IsOneUnknownFile` | 8 | cells empty |
| `ADeletedFile_IsKindedByItsOnlyName` | 9 | cells empty |
| `AnEmptyRange_IsAnObservedZero_NotUnavailable` | 10 | stub-green (the stub IS this answer — which is why the next row exists); red under break-it 11 |
| `AReviewableFileAbsentFromTheUnfilteredRange_IsReportedNeverDropped` | 11 | `Expected State "unavailable: numstats disagree" but found "recorded"` |
| `TwoUnfilteredRecordsClaimingOneReviewableFile_IsADisagreement` | 14 | same |
| `AnElidedPathThatIsNoReviewableFile_IsADisagreement` | 15 | same |
| `TheDisagreementSentencesCarryNoPath` | `Disagreement(...)` over 11, 14, 15: each sentence is one of the three fixed strings and contains none of the paths | `Expected "" to be "a reviewable file …"` |
| `CellsAreOrderedByCoverageThenKind_AndNeverRepeatAPair` | 1 + 13: the order of §S1.6; `Cells.Select(c => (c.Coverage, c.Kind))` has no duplicate | cells empty |
| `TheDeltaOfAFixThatReturnedAFileToTheBase_CountsItShown` | 12 | cells empty |
| `PathsMatchOrdinally_ACaseTwinIsNotTheSameFile` | `U` = `A.cs`, `F` = `a.cs` → `numstats disagree` | state `recorded` |
| `TheFromIsKeptOnEveryAnswer` | 1, 10, 11: `From == "abc1234"` | stub-green on 10; 1 and 11 red on cells/state |

`src_mcp/tests/TheDeltaIsMeasuredFromTheLastReviewedHeadTests.cs` (records built with the positional constructor
`SessionStore.cs:78-85` and `with`; a skipped one built EXACTLY as `RoundSkips.cs:102-111` builds it: `Verdict:
RoundRecord.Skipped`, `Status = Done`, `Sha = sha`, `Note = reason`)

| Test | Trail → expected | RED under the stub (`FirstRound` always) |
|---|---|---|
| `NoEarlierCodeRound_IsTheFirstRound` | `[]`; `[plan 1 done]`; `[plan 1 done, plan 2 done]` → `FirstRound` | stub-green; red under break-it 12 |
| `TheLatestDoneCodeRoundWithASha_IsThePredecessor` | `[code 1 done A, code 2 done B]` → `Of(B)` | `Expected IsChosen to be true` / `Sha "" ≠ "B"` |
| `ASkippedRoundIsNeverThePredecessor_ThoughItIsDoneWithASha` | `[code 1 done A, code 2 skipped done B]` → `Of(A)` | `Sha "" ≠ "A"`; under break-it 4: `Expected Sha "A" but found "B"` |
| `ARunningOrInterruptedRound_IsNeverThePredecessor` | `[code 1 done A, code 2 interrupted B, code 3 running C]` → `Of(A)` | `Sha "" ≠ "A"` |
| `APlanDocumentOrFeatureRound_IsNeverThePredecessor` | `[code 1 done A, FeatureReview 1 done F, DocumentReview 1 done D]` → `Of(A)` | same |
| `ARoundWhoseEveryReviewerFailed_StillQualifies` | `[code 1 done A, verdict call_human, ReviewerStates all Failed]` → `Of(A)` | same |
| `OnlyPreShaRounds_IsNoPredecessorSha` | `[code 1 done ""]`; `[plan 1 done, code 1 done ""]` → `NoSha` | `Expected State "unavailable: no predecessor sha" but found "unavailable: first round"` |
| `TheLatestRoundWithAShaWins_OverANewerOneWithout` | `[code 1 done A, code 2 done ""]` → `Of(A)`; `[code 1 done "", code 2 done B]` → `Of(B)` | `Sha ""` |
| `ListOrderDecides_NotTheClock` | `[code 1 done A (CompletedUtc later), code 2 done B (earlier)]` → `Of(B)` | `Sha ""` |
| `TheStageIsMatchedByItsPersistedName_Ordinally` | `[Stage "codereview" done A]` → `FirstRound`; `[Stage "CodeReview" done A]` → `Of(A)` | second half red |
| `NoSizeUnitTakesTwoSizes` (reflection, the criterion-7 tripwire) | over `typeof(ChangePartition)`, `typeof(DeltaPredecessor)`, `typeof(ScopeSize)`, `typeof(RoundSize)`, `typeof(SizeCell)`: every public or internal method and constructor that is NOT `IsSpecialName` (a record's generated `op_Equality`/`op_Inequality` take two) has at most ONE parameter whose type is `ScopeSize`, `RoundSize`, `SizeCell` or an `IEnumerable<SizeCell>` | stub-green; red under break-it 3 |

**S1.8 — Break-its.** Each is a change to PRODUCTION code, run against the named test, seen red with the symptom, then
restored and seen green; both runs go in the summary. No test is edited, none skipped, no tripwire disabled.

| # | Mutation | Test that goes red | Symptom |
|---|---|---|---|
| 1 | `Place` rule 2: a `U` with no `F` is `shown` with `U`'s lines (the set difference folded away) | `AnExcludedFileIsCountedApart_NeverAsShown` | `Expected shown/code Files 1 … found 2` / the lock file's `+1` inside `shown` |
| 2 | `Place` rule 1: `Added = U.Added` instead of null | `ABinaryHasNoLineCount_WhateverTheReaderGave` | `Expected Added to be <null>, but found 7` (and `0` on example 1) |
| 3 | Add `public static ScopeSize Minus(ScopeSize a, ScopeSize b)` to `ChangePartition` (the subtraction criterion 7 forbids) | `NoSizeUnitTakesTwoSizes` | the member named in the failure. The BEHAVIOURAL twin — a delta computed as whole N minus whole N−1 — is E2.S2's `TheDelta_IsTheDiffBetweenTheTwoHeads` and E2.S4's `ASecondCodeRoundAfterOneCommit_RecordsTheHeadToHeadDelta`; example 12 here is the pure half of it |
| 4 | Drop `&& r.Verdict != RoundRecord.Skipped` from the candidate rule | `ASkippedRoundIsNeverThePredecessor_ThoughItIsDoneWithASha` | `Expected Sha to be "A", but found "B"` |
| 5 | Swap rows 1 and 3 of the kind table (config before test) | `TheFirstMatchingRowWins_TestBeforeDocsBeforeConfigBeforeCode` | `Expected "test", but found "config"` on `tests/fixtures/data.json` |
| 6 | Match paths with `OrdinalIgnoreCase` | `PathsMatchOrdinally_ACaseTwinIsNotTheSameFile` | `Expected State "unavailable: numstats disagree", but found "recorded"` |
| 7 | Drop the unclaimed-`F` check from `Disagreement` | `AReviewableFileAbsentFromTheUnfilteredRange_IsReportedNeverDropped` | `Expected … "numstats disagree", but found "recorded"` |
| 8 | `Choose`: return the last CANDIDATE's sha whether or not it has one | `TheLatestRoundWithAShaWins_OverANewerOneWithout`, `OnlyPreShaRounds_IsNoPredecessorSha` | `Sha "" ≠ "A"`; `IsChosen` true with an empty sha |
| 9 | Let `Of("")` index `name` without the empty guard | `AnEmptyOrTrailingSlashPath_IsUnknownAndNeverThrows` | an `IndexOutOfRangeException` where `unknown` was expected |
| 10 | Order cells by kind then coverage | `CellsAreOrderedByCoverageThenKind_AndNeverRepeatAPair` | the sequence assertion names the first misplaced cell |
| 11 | `Of`: answer `ScopeSize.Unavailable(ScopeState.GitFailed, from)` when `unfiltered` is empty (an empty range read as a failure) | `AnEmptyRange_IsAnObservedZero_NotUnavailable` | `Expected State "recorded", but found "unavailable: git failed"` |
| 12 | `Choose`: answer `Predecessor.NoSha` when there is no candidate (one word for two states) | `NoEarlierCodeRound_IsTheFirstRound` | `Expected State "unavailable: first round", but found "unavailable: no predecessor sha"` |

**S1.9 — Decisions closed by this spec** (each with its reason above; listed so a reviewer can check them as a set):

1. Coverage precedence `binary` > `excluded` > `elided` > `shown`; an excluded binary is `binary` (§S1.4 step 3).
2. Lines of a `shown`/`elided` file are the FILTERED record's (what the reviewers were shown); of an `excluded` file the
   unfiltered record's (§S1.4 step 3).
3. The kind is decided on the unfiltered record's `Path` — the file's name at the head (§S1.4 step 3).
4. Path matching is ordinal (git's); kind matching is ordinal-ignore-case (ours) — two comparisons, two reasons (§S1.3,
   §S1.4).
5. "A path segment" is one whole directory component at any depth; the name rules apply to the last component only; the
   docs folders match at any depth (§S1.3).
6. Matching is three-way (new name, old name, the filtered rename's old name) so a rename across the exclusion boundary is
   classified and a rename-limit skip is reported, never filed as `excluded` (§S1.4 steps 1–2).
7. One state added to C1, `unavailable: numstats disagree`, with three fixed causes and no path in its sentences (§4 C1,
   §S1.4 step 2).
8. The predecessor is chosen by list order; the latest candidate WITH a sha; a round whose reviewers all failed qualifies;
   feature rounds never (§S1.5).
9. The seven delta states have the precedence of §S1.5's table; `From` is kept on an `unavailable` scope once a commit was
   chosen (§4 C1).
10. The delta's `elided` set is the round's own; a delta file absent from the whole diff is `shown` (§S1.5).
11. Cells are ordered coverage-then-kind and `ChangeKind.All` starts with `code` (§S1.2, §4 C1).
12. Criterion 7's "never arithmetic" is held by a reflection test over the units' API, with the behavioural proof in
    E2.S2 and E2.S4 (§S1.7, §S1.8 row 3).
13. The three records carry no computed instance property; `ScopeSize` gets two static factories (§S1.2).

*Stated limits, not decisions to revisit here:* `.sln`, `.xml`, `.ini`, `.cshtml`, `.sql` and every language the outliner
does not map are `unknown` — the parent's table, printed by E6; a .NET test project folder not literally named `tests`
(`src/Foo.Tests/`) is `code` unless a name rule catches the file — every repository in this family keeps tests under a
`tests/` or `test/` segment, so the parent's rule stands until E7's hand-check (parent §11 step 7) says otherwise; a
gitlink reads as a one-line file; a `numstats disagree` makes the whole SCOPE unavailable rather than one file partial,
because a partition that is wrong about one file is not known to be right about the rest.

- **Break-its — the four fixed before the spec**, kept as written; §S1.8 maps each to its test and adds eight:
  1. Fold `excluded` into `shown` (drop the set difference) → the partition test over a lock file + a code file reports the
     lock file's lines as shown.
  2. Write a binary's `Added` as the 0 the reader gave → the binary test sees `0`, not null.
  3. Compute the delta as round N's whole totals minus round N−1's → the fixture where round N reverts round N−1's edit
     (head-to-head: lines changed; subtraction: zero or negative) goes red.
  4. Drop the `Verdict != skipped` filter → the predecessor test picks the skipped round's sha.
- **Done when.** The three test files green with every row of §S1.6 and §S1.3's table present; the twelve break-its of §S1.8
  recorded red-then-green in the summary; `ArchitectureTests` green; `dotnet format --verify-no-changes` clean over the
  four new files; the whole `CoaiMcp.Tests.exe` green. No change to `research/module_server.md` in this story — E2.S4
  writes the size paragraph once the round carries one.
- **Depends on.** Nothing. Every other story of E2 depends on this committed spec (C1 final); E2.S3 and E2.S4 on its code.
- **Size.** ≈ 560 changed lines: ≈ 170 code (four files, none past 120 lines), ≈ 390 tests (three files; the table tests
  are most of it). Fable's cut, replacing the ≈ 380–420 estimate: the test list grew by the edge cases §S1.4 names.
- **Cornerstone — spec by Fable** · **Coder: sonnet** · review: opus.

### E2.S2 — The numstats: counted whole, unfiltered whole, and the delta range from git

- **Goal.** `ContextAssembler` yields the counted records E2.S1 consumes without changing what any reviewer is shown and
  without taxing the feature stage: the round's own numstat keeps its counts, and two new methods answer the unfiltered
  numstat and the delta range — neither ever throws for a git failure.
- **Scope.**
  - `runners/Context/ContextAssembler.cs:311-329` — parse with `NumstatReader.ReadCounted` and build the same `FileDiff`
    list from `c.Change`; `CollectedDiff` (`:119`) gains `public IReadOnlyList<CountedChange> Counted { get; init; } = [];`
    set at `:329`. Files, their order, and every git call `CollectAsync` makes are unchanged (the feature stage at
    `FeatureStage.cs:241` pays nothing).
  - New `public async Task<CountedNumstat> AllChangesAsync(string repoPath, string against, string sha, CancellationToken ct)`
    — the unfiltered numstat of `:359`'s shape (`diff --numstat -z {against}..{sha}`), through `launcher.RunAsync`, NOT
    `Git(...)`: a non-zero exit returns `CountedNumstat.Failed` (an empty list plus a `Failed` flag), never a throw.
  - New `public async Task<DeltaNumstats> DeltaAsync(string repoPath, string from, string sha, IReadOnlyList<string>? exclusions, CancellationToken ct)`:
    `merge-base --is-ancestor {from} {sha}` (exit 0 → ancestor, 1 → `not an ancestor`, anything else → `git failed`;
    a `from` that fails `IsCommit` (`:408-409`) → `git failed` without a process); then the filtered numstat of `{from}..{sha}`
    with the same exclusion pathspecs as `:294`, and the unfiltered one. Returns the two counted lists or the S1 state word.
    `from == sha` (ordinal-ignore-case) → two empty lists, `recorded`, no process.
  - `CountedNumstat` / `DeltaNumstats` as small records beside `CollectedDiff`.
- **Brief.** Follow `ComparisonBase` (`:430-467`) for running git without throwing and `IsCommit` for every value that
  reaches a range. No path is logged at Information. Do not touch `ReviewableAsync`; do not move the exclusion pathspec
  building — reuse the `:294` expression (extract a private static `Excludes(exclusions)` used by all three call sites if it
  removes a copy).
- **Test scenarios** (`src_mcp/tests/TheRoundsNumstatsAreCountedTests.cs`, real git as `ContextAssemblerTests.cs:18-59`;
  RED: the stubbed methods return empty/`git failed` and every assertion below fails on its value):
  - `TheRoundsOwnNumstat_KeepsItsCounts_AndTheFilesAreUnchanged` — `Counted` holds `app.cs` with its real added/deleted;
    `Files` equals what the unchanged collector returned (paths and order).
  - `TheUnfilteredNumstat_NamesWhatTheExclusionsHid` — `package-lock.json`, `dist/bundle.js` present in `AllChangesAsync`,
    absent from `Counted`; `logo.png` binary in both.
  - `ARename_ArrivesOnceUnderItsNewPathWithTheEditsCounts` (the `ContextAssemblerRenameTests` shape).
  - `ARenameAcrossTheExclusionBoundary_IsClassifiedBothWays` — real git, two commits: `src/a.js` renamed to
    `dist/a.js` (INTO an excluded path) and `dist/b.js` renamed to `src/b.js` (OUT of one). The filtered and unfiltered
    numstats fed to E2.S1's partition give `excluded` for the first and `shown` for the second, matched through the
    rename's old name (S1.9 item 6) — never `numstats disagree`.
  - `TheDelta_IsTheDiffBetweenTheTwoHeads` — base → commit A (+10 lines) → commit B (reverts 4 of them, adds 2):
    `DeltaAsync(A, B)` counts B's edit (2 added, 4 removed), not the cumulative diff.
  - `ARebasedBranch_IsNotAnAncestor` — record A's sha, then `reset --hard` the branch to the base and commit a different
    change B: `DeltaAsync(A, B)` reads `unavailable: not an ancestor` and runs no numstat.
  - `AnUnknownCommit_IsGitFailed_NeverAThrow` — a well-formed sha that does not exist; and a value that fails `IsCommit`.
  - `TheSameHeadTwice_IsAnObservedZero` — `recorded`, empty lists.
- **Break-its.** (1) Take the delta range as `{against}..{sha}` instead of `{from}..{sha}` → `TheDelta_IsTheDiffBetweenTheTwoHeads`
  red with the cumulative counts. (2) Skip the ancestry question → `ARebasedBranch_IsNotAnAncestor` red with counts.
- **Done when.** The scenarios and `ContextAssembler*Tests` green, the whole executable green; break-its recorded.
- **Depends on.** E2.S1's committed spec (the state words); not its code.
- **Size.** ≈ 260 changed lines (≈ 80 code, ≈ 180 tests).
- **Coder: sonnet** · scenarios and brief: this document (opus) · review: opus.

### E2.S3 — Schema step 22 and its writer: `round_changes`, the size states, `reviewers.effort`

- **Goal.** A round recorded with a `Size` writes C2's rows and states in `RecordRound`'s own transaction; a round without
  one writes none and reads *not recorded*; every reviewer's effort is persisted; every database shape in the field reaches
  step 22; read-only readers of an older file keep answering.
- **Scope.**
  - `src/Server/SessionStore.cs:193` — `RoundRecord` gains `public Core.Context.RoundSize? Size { get; init; }`, documented
    as `Caller` is (`:173-192`): nullable, NOT normalised on the way in, absent on rounds before the field.
  - `src/Store/Schema.cs` — `internal const string TheRoundsSize` = C2's SQL, appended after `TheRoundsKey` in `Steps`
    (`:31-38`), with a doc comment in the step-20/21 style (`:576-616`).
  - New `src/Store/RoundChangesTable.cs` (the `QuestionConsultTable.cs` shape, through `RoundsDb.Command()`):
    `Record(RoundsDb db, long roundId, RoundSize? size)` = `DELETE FROM round_changes WHERE round_id = $id`, then one insert
    per cell per scope (`added`/`removed` bound `DBNull` when null), then
    `UPDATE rounds SET size_state = $s, delta_state = $d, delta_from = $f, size_origin = $o WHERE id = $id`. When `size` is
    null NOTHING is written — rows and states stay as they are, so a backfilled size survives a live re-record (the
    parent's §16 D14 rule for authors, applied to the size).
  - `src/Store/RoundsDb.cs:196-201` — one call, `RoundChangesTable.Record(this, roundId, round.Size);`, after
    `RecordReviewers` inside the transaction. `:930-943` — `effort` added to the insert and bound from `reviewer.Effort`.
  - `src_mcp/tests/TheRoundKeyIsStepTwentyOneTests.cs:79` — `Should().Be(21)` becomes `Should().BeGreaterThanOrEqualTo(21)`,
    exactly as step 20's test was relaxed when 21 shipped (`TheReviewersModelIsStepTwentyTests.cs:84`). This is the only
    edit to an existing test in this epic; its position assertions (`:78`, `:80`) stay.
  - `research/module_server.md` after `:3276` — a **Schema step 22** paragraph in the step-21 paragraph's shape.
- **Brief.** Mirror `TheRoundKeyIsStepTwentyOneTests.cs` for the migration cases (its `MakeAt`, `Shape`, both preview-fork
  shapes, the read-only reader). `RoundsDb.Bind` is private — give `RoundChangesTable` its own `Bind` as
  `QuestionConsultTable` does. Nothing in `RecordRoundRow`'s SQL changes. Never write `0` for a binary.
- **Test scenarios** (`src_mcp/tests/TheRoundsSizeIsStepTwentyTwoTests.cs`; RED: with the step present but `Record` a no-op
  and the effort unbound, every row/column assertion fails on its value):
  - `TheRoundsSize_IsTheTwentySecondStep_AppendedNeverInserted` — index 21, `Steps[21] == Schema.TheRoundsSize`.
  - `ADatabaseAtStepTwentyOne_GainsTheTableAndColumns_AndItsOlderRoundsReadNotRecorded` — `size_state = ''`, no rows,
    the older reviewer rows' `effort = 'not recorded'` (parent §16 D11).
  - `ARecordedRound_WritesOneRowPerCell_AndABinaryHasNoLineCount` — a `RoundSize` with shown/code, excluded/config,
    binary/unknown and a delta of one cell: exact rows; the binary row's `added IS NULL`.
  - `AnUnavailableDelta_IsAStateNotAnEmptyResult` — `unavailable: first round` and a `recorded` zero-cell delta read back as
    two different states.
  - `TheSameRoundRecordedTwice_HasTheSameRows` — no doubled rows; and a re-record with `Size = null` leaves the earlier
    rows and states untouched (parent §16 D14).
  - `AReviewersEffort_IsPersisted_AndOneWithoutReadsEmpty` — a reviewer that applied none is written `''`, never
    `not recorded`.
  - `AReadOnlyReader_OnAStepTwentyOneFile_StillReads_AndLeavesItAtTwentyOne` (`RoundCounts.Read`, as step 21's test).
  - Both preview-fork shapes reach the head; a migrated file opens again unchanged.
  - `ASessionFileWithoutASize_RoundTripsWithoutOne` — a `PersistedSession` JSON with no `size` loads `Size == null` and is
    re-saved without a `size` member (the `Caller` guarantee, `SessionStore.cs:185-192`); one with a binary cell round-trips
    with `added`/`removed` absent and reads back null.
- **Break-its.** (1) Bind a binary's null as 0 → the binary test red. (2) Drop the `DELETE` → the twice-recorded test red with
  doubled rows. (3) Drop the effort bind → the effort test red.
- **Done when.** The scenarios, `TheRoundKeyIsStepTwentyOneTests`, `TheReviewersModelIsStepTwentyTests`, `RoundsDbTests`,
  `SqliteMigratorTests` and the whole executable green; module doc paragraph written.
- **Depends on.** E2.S1 (C1's types exist).
- **Size.** ≈ 360 changed lines (≈ 120 code + doc, ≈ 240 tests).
- **Coder: sonnet** · scenarios and brief: this document (opus) · review: opus.

### E2.S4 — Every code round measures itself: the size rides from the diff to the record

- **Goal.** A live code round computes its `RoundSize` from the diff it actually built and shaped, carries it out of
  `MakeWork` beside the resolved base, and finishes with it on its record — session file and database — while a size that
  cannot be measured never touches the round.
- **Scope.**
  - New `src/Server/Rounds/RoundSizer.cs` — `MeasureAsync(ContextAssembler context, string repoPath, string sha,
    CollectedDiff collected, ShapedDiff shaped, IReadOnlyList<RoundRecord> earlier, CancellationToken ct)`: whole =
    S1's partition over `collected.Counted`, `AllChangesAsync(collected.ComparedAgainst, sha)` and `shaped.Elided`; the
    predecessor by S1's selection over `earlier`; the delta by `DeltaAsync` + the same partition (with S1's delta-coverage
    rule). Every exception except `OperationCanceledException` → the affected scope `unavailable: git failed`, one Warning
    log line with no path. A non-empty `ChangePartition.Disagreement(...)` is a STATE, not an error: the scope is
    `unavailable: numstats disagree` and its fixed sentence (never a path) is logged once at Warning. Logs one Information line: whole files/lines by coverage, delta state, elapsed ms.
  - `src/Server/Rounds/RoundWork.cs` — `public Core.Context.RoundSize? Size { get; init; }` beside `BaseRef` (`:22-30`).
  - `src/Server/PanelService.cs` — after `:757`, `var size = await RoundSizer.MeasureAsync(_context, repoPath, sha,
    collected, shaped, session.Rounds, roundToken);` and `Size = size,` in the `with` at `:866-870`. Nothing else.
  - `src/Server/Rounds/RoundEngine.cs:483-491` — `Size = roundWork.Size,` in the finished record's `with`. (Not in the
    projection block `:607-624`: `RecordRound` already reads it from `record`, as it reads `Caller`.)
  - Plan, document and feature rounds: untouched; their `RoundWork.Size` stays null.
- **Brief.** `session` inside the `MakeWork` lambda holds the rounds BEFORE this one (`RoundEngine.cs:355`) — use it, never a
  re-load. Do not reorder the existing git calls; the size's calls run after `Shape` and before the rules are read. If the
  delta's extra processes measure over 300 ms p50 on the test repo, say so in the summary rather than optimising.
- **Test scenarios** (`src_mcp/tests/ACodeRoundRecordsItsSizeTests.cs`, the `ARoundIsMintedAKeyTests.cs` harness —
  real `PanelService`, FakeCli, real git; RED: with `MeasureAsync` returning null every assertion fails on `Size == null`):
  - `TheFirstCodeRound_RecordsItsWholeSize_AndNoDelta` — session file and `round_changes` agree: shown/code = the
    feature commit's lines; delta `unavailable: first round`.
  - `ASecondCodeRoundAfterOneCommit_RecordsTheHeadToHeadDelta` — round 2 after a fix commit: `delta_from` = round 1's sha,
    delta cells = the fix commit's numstat; `whole` = the cumulative diff.
  - `ASkippedRoundIsNeverThePredecessor` — a skipped code round between two that ran: the delta is measured from the one that
    ran (S1 criterion 6, end to end).
  - `ALockFileAndAnImage_AreCountedApart` — the excluded and binary cells present; neither inside shown.
  - `AnElidedFile_IsRecordedElided` — a text file past 192 KiB: `elided`, lines counted.
  - `ASizeThatCannotBeMeasured_DoesNotTouchTheRound` — `RoundSizer` driven directly with a launcher whose unfiltered numstat
    exits 128: the scope reads `unavailable: git failed`; and end to end, the round's verdict and findings are identical to
    the same round with sizing working.
  - `APlanRound_HasNoSize`.
  - `ANumstatDisagreement_IsAStateAndAWarning` — `RoundSizer` driven with an unfiltered numstat that lacks a reviewable
    file: the scope reads `unavailable: numstats disagree`, one Warning with the fixed sentence and no path, and the
    round's verdict and findings are unchanged.
- **Break-its.** (1) Remove `Size = roundWork.Size` at `RoundEngine.cs:483-491` → the first scenario red (`Size` null in
  file and table). (2) Pass `session.Rounds` unfiltered as "the last round" (ignore S1's selection) → the skipped-round
  scenario red. (3) Let a size exception propagate → `ASizeThatCannotBeMeasured_DoesNotTouchTheRound` red.
- **Done when.** The scenarios green; `EndToEndTests`, `ARoundIsMintedAKeyTests`, `ARoundsNumberComesFromTheJournalTests`
  and the whole executable green; the measured cost of the size's git calls is in the summary; `research/module_server.md`'s
  code-round flow names the size step in one paragraph.
- **Depends on.** E2.S1, E2.S2, E2.S3.
- **Size.** ≈ 340 changed lines (≈ 110 code, ≈ 230 tests).
- **Coder: sonnet** · scenarios and brief: this document (opus) · review: opus.

### E2.S5 — `--log` names each round's size, and a step-21 file still answers

- **Goal.** `--log` carries C3's `size` on every round that has one and nothing on a round that does not, read in one grouped
  query per page, and a database written before step 22 is read exactly as before.
- **Scope.**
  - `src/Store/RoundsQuery.cs:27-102` — `LoggedRound` gains `public Core.Context.RoundSize? Size { get; init; }` (an init
    property, so the positional constructor and every call site stay; the `LoggedLog.Consultations` precedent, `:206`).
  - New `src/Store/RoundChangesLog.cs` — `Read(SqliteConnection db, IReadOnlyCollection<long> roundIds)` →
    `IReadOnlyDictionary<long, RoundSize>`: when `RoundsQuery.HasColumn(db, "rounds", "size_state")` is false, an empty map
    with no query; otherwise one `SELECT id, size_state, delta_state, delta_from, base_ref FROM rounds WHERE id IN (…)` and one
    `SELECT … FROM round_changes WHERE round_id IN (…)` (ids bound as parameters, never concatenated); a round whose
    `size_state` is `''` is absent from the map. `Whole.From` = `base_ref`.
  - `RoundsQuery.cs:470-531` — `Rounds(...)` attaches `Size` from that map (call site + one `with`).
  - `research/module_server.md` — the `--log` round shape gains the `size` field, with C3's "absent = no line count" rule.
- **Brief.** Follow `FindingsByRound` (`RoundsQuery.cs:476`) for the one-query-per-page shape and `QuestionConsultLog.Read`
  for a reader in its own file on the read-only connection `Read` already opened (`:421`). `Columns.Text`/`Number` read by
  name; `added` NULL → null (`Columns.Number` turns NULL into 0 — do NOT use it for `added`/`removed`).
- **Test scenarios** (`src_mcp/tests/TheLogNamesEachRoundsSizeTests.cs` for the reader; `LogCliScenarioTests.cs` gains one
  process-level case; RED: `Size` never attached, every assertion fails on `size` absent):
  - `ARoundWithASize_IsLoggedWithIt_AndOneWithoutIsNot` — seeded with `RecordRound` (one with a binary cell, one with
    `Size = null`): exact cells; the binary cell's `added`/`removed` are null; the second round has `Size == null`.
  - `TheLogAsAProcess_PrintsTheSize_AndOmitsABinarysLineCount` — `--log` JSON: `size.whole.cells[*]`, the binary cell has
    no `added` member; exit 0.
  - `AStepTwentyOneFile_IsLoggedAsBefore` — a file made at `Steps[..21]` with one round: `--log` answers exit 0, the round has
    no `size`, and the file is still at version 21 afterwards.
  - `EachSizeLandsOnItsOwnRound_AcrossPages` — 30 rounds with distinct sizes read as two pages through the `before`
    cursor: every round carries its own cells and no round of the other page leaks in (the id list is the page's).
- **Break-its.** (1) Remove the `HasColumn` guard → `AStepTwentyOneFile_IsLoggedAsBefore` red (exit 74 / no such column).
  (2) Read `added` with `Columns.Number` → the binary cell reads `0`, red.
- **Done when.** The scenarios, `RoundsQueryTests`, `LogCliScenarioTests` and the whole executable green; the module doc
  updated.
- **Depends on.** E2.S3 (the columns and the writer that seeds the tests). Not on E2.S4.
- **Size.** ≈ 250 changed lines (≈ 90 code + doc, ≈ 160 tests).
- **Coder: sonnet** · scenarios and brief: this document (opus) · review: opus.

## 6. Build order

```
S1 spec (Fable) ──► S1 code ──┬─► S3 schema + writer ──┬─► S5 --log
                              │                          │
S1 spec ──► S2 numstats ──────┴──────────────────────────┴─► S4 the round measures itself
```

S2 starts as soon as S1's spec is committed (it needs the state words, not S1's code). S3 and S2 run in parallel once S1's
code is in; S5 needs only S3; S4 is last. The epic lands as ONE commit on its own branch, with the coai gate once
(parent `## Epics`).

## 7. Test plan — the parent's §12 rows this epic owns

| Parent §12 row | Story | Kind |
|---|---|---|
| **Diff size** on a temp git repo: text edit, binary, rename, lock file, test/docs/config, elided file, a second round after one commit, a second round after a rebase | S1 (pure table), S2 (git), S4 (end to end) | table + real git |
| **Migration**: a step-21 database opens at 22; old rows read *not recorded*; read-only `--log`/`--round-counts` on a step-21 file still answer | S3, S5 | real SQLite + process |
| `reviewers.effort` persisted (DoD) | S3 | real SQLite |
| The same round recorded twice | S3 | real SQLite |
| Break-its: excluded as shown, binary as 0, delta by subtraction, skipped predecessor (area); wrong range, no ancestry (S2); null→0, no delete, no effort bind (S3); size not on the record, unfiltered predecessor, failure propagates (S4); no column guard, NULL→0 (S5) | each story | red → restore |

## 8. Growth budget

| Surface | Projected size | Retired by | Interrupted |
|---|---|---|---|
| `round_changes` | ≤ 2 scopes × 4 coverages × 5 kinds = 40 rows worst case, typically 3–6 × ~60 B; ~700 code rounds per two weeks → **≤ 6 MB a year** (parent §14) | with its round; a re-record deletes and rewrites the round's rows | no in-flight state: written in the round's own transaction or not at all |
| `rounds.size_state`/`delta_state`/`delta_from`, `reviewers.effort` | ≤ ~80 B a round | with the round | same |
| `RoundRecord.Size` in session files | ~200–400 B a code round | with the session file | the record is written at finish; an interrupted round has none |
| Per-round cost | 2–4 more git processes on a code round (unfiltered numstat, is-ancestor, two delta numstats); measured in S4's summary | — | cancelled with the round token |

## 9. Definition of Done — epic E2

- [ ] E2.S1's spec written by Fable and committed into its section before its code; C1 matches it; the parent's ledger line
      reads `Cornerstones: 3 of 3 — E2.S1, E3.S4, E4.S2`.
- [ ] E2.S1–E2.S5 landed, each with its scenarios green and its break-its red-then-green, recorded in the story's summary.
- [ ] A code round records its size by scope, coverage and kind; binaries carry no line count anywhere (record, row, JSON);
      the delta is a head-to-head numstat or a named `unavailable` state; a skipped round is never a predecessor.
- [ ] `reviewers.effort` persisted; a step-21 database opens at 22; a pre-step round reads *not recorded*; read-only
      `--log` and `--round-counts` on a step-21 file answer and leave it at 21.
- [ ] No size failure changed a round's outcome (S4's scenario).
- [ ] `CoaiMcp.Tests.exe` green as a whole; `node .agents/conventions/tools/plan-lifecycle.mjs` clean (this document has its
      own `todo/README.md` row); `research/module_server.md` describes step 22, the size step and the `--log` field.
- [ ] This document is promoted with the parent plan, not before.

## 10. Points this document raises for the parent (not edited there)

> **Settled 2026-10-14** in the parent's §16: 1 → D1; 3 → D12 (plus `size_origin`); 4 → D11 (an explicit
> `not recorded` default); 2 and 5–8 accepted as this document resolves them.

1. **A skipped round is "done with a sha"** (`RoundSkips.cs:102-111`), so the parent's predecessor rule — "the last CODE
   round with status `done` and a `Sha`" (parent §7, and the E2 scope) — would measure a delta from a commit nobody
   reviewed. S1 criterion 6 excludes `Verdict == skipped`.
2. **`CollectAsync` cannot return the whole `ChangeSize`** (parent §7, E2 scope): `elided` exists only after
   `DiffShaper.Shape` (`PanelService.cs:757`), and `CollectAsync` is also the feature stage's (`FeatureStage.cs:241`), which
   would pay the exclusions numstat for nothing. S2 keeps the counts on `CollectedDiff` (free) and puts the extra numstats in
   separate methods; S4 assembles the size after shaping.
3. **The delta's `unavailable: <why>` has nowhere to live** in the parent's `round_changes(...)` row shape, and an observed
   zero (same head) would read like "unavailable" with rows alone. C2 adds `rounds.size_state`, `delta_state`, `delta_from`
   to step 22.
4. **"`--log` shows … the effort"** (parent E2 done-when): `--log` has no reviewer rows at all (`RoundsQuery.cs:27-102`).
   E2 proves the column by a database read; E6's report reads the table directly. Separately, `reviewers.effort` will be
   EMPTY for codex, gemini and agy reviewers — `RosterBuilder.cs:705-712` gives an effort only to claude, remote and local
   launches — so for codex, the only reviewer since 2026-10-11 (parent §1), the reviewer set's effort reads `unknown`.
5. **The record is where the size goes, not the projection call**: the parent's call-site list names
   `RoundEngine.cs:605-625`; the size is set on the finished record at `RoundEngine.cs:483-491` and `RecordRound` reads it
   from there, as it reads `Caller`.
6. `TheRoundKeyIsStepTwentyOneTests.cs:79` pins `Steps.Length == 21`; step 22 relaxes it to `≥ 21` (S3), as step 21 did to
   step 20's test. E5's step 23 will do the same to S3's test.
7. "Code = the repository's tree-sitter languages" has two candidate maps (`OutlineLanguages.Of`, 7 languages;
   `TreeSitterNormalizer.LanguageOf`, 3). S1 takes `OutlineLanguages` ("the one mapping"); `.ps1`, `.sh`, `.razor`, `.html`,
   `.css` are `unknown` and outside the default `code`+`test` denominator of parent §8 — a choice E6 should print.
8. `RoundsDb.cs` and `RoundsQuery.cs` are past the 800-line ceiling; the parent names only `RoundEngine.cs` and
   `PanelService.cs` as call-site-only. S3 and S5 put the writer and the reader in new files.
