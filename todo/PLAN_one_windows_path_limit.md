# PLAN — one named constant for Windows' path limits, used by the Vibe budget and the worktree error text

> Status: **plan only, nothing implemented yet, 2026-10-18.** Approved by the owner 2026-10-18 as a small refactor.
> Scope: `src_mcp/runners/Platform/` (one new file), `src_mcp/runners/Vibe/VibePathBudget.cs`,
> `src_mcp/runners/Worktrees/WorktreeFailure.cs`, their tests, and `research/module_runners.md`.
>
> Related docs: [module_runners.md](../research/module_runners.md),
> [PLAN_review_trees_survive_long_paths.md](../research/PLAN_review_trees_survive_long_paths.md),
> [PLAN_vibe_live_round_fixes.md](../research/PLAN_vibe_live_round_fixes.md).

## 1. Symptom

One platform limit is written down in two places.

| Where (origin/main `2b302c65e`) | What it says |
|---|---|
| `src_mcp/runners/Vibe/VibePathBudget.cs:27` | `public static VibePathBudget Windows { get; } = new(259, 156);` — the Vibe home check. 259 is the Windows file-path limit. |
| `src_mcp/runners/Worktrees/WorktreeFailure.cs:22` | `internal const int WindowsMaxPathUnits = 259;` — the same Windows file-path limit, for the review-tree error text. |
| `src_mcp/runners/Worktrees/WorktreeFailure.cs:29` | `internal const int WindowsMaxFolderUnits = 247;` — the Windows folder-path limit (PR #852). |
| `src_mcp/runners/Worktrees/WorktreeFailure.cs:75` | `public int WindowsLimit => IsFolder ? WindowsMaxFolderUnits : WindowsMaxPathUnits;` — the one reader of the two constants. |

The rule `.agents/conventions/common/platform-limits.md` §1 item 4 says: "One constant is the source of truth, and
everything derives from it … **no call site keeps its own literal**." Today two call sites keep the number 259. If a
later probe changes it (for example `todo/PLAN_vibe_long_paths_measured.md`), one copy can change and the other can stay
wrong. Both numbers were probed: 259 by probes P1/P2 of `research/PLAN_review_trees_survive_long_paths.md` and by
`research/RESULTS_vibe_confinement.md` §27; 247 by git 2.55 on 2026-10-17 and by Python in
`research/PLAN_vibe_live_round_fixes.md`. Both are counted in UTF-16 units (what Windows counts), and both callers
already measure `string.Length`.

## 2. Every literal found, and the decision for each

Searched on `origin/main` (`2b302c65e`) for `259`, `247`, `260`, `248`, `MAX_PATH` and `MaxPath` in `*.cs`, `*.ts`,
`*.mjs`, `*.js`, `*.json`, `*.yml`.

### 2.1 In scope — the same limit, Windows' file path (259) or folder path (247)

| File:line | Kind | Change |
|---|---|---|
| `src_mcp/runners/Vibe/VibePathBudget.cs:27` | code literal `259` | `new(WindowsPathLimits.FileUnits, 156)`. The `156` is Vibe's own depth, not a platform limit; it stays. |
| `src_mcp/runners/Vibe/VibePathBudget.cs:26` | doc of that line | names `WindowsPathLimits.FileUnits` with a `<see cref>`. |
| `src_mcp/runners/Worktrees/WorktreeFailure.cs:17-29` | the two constants and their docs | removed; their docs move to the new type. |
| `src_mcp/runners/Worktrees/WorktreeFailure.cs:75` | reader of the two constants | `IsFolder ? WindowsPathLimits.FolderUnits : WindowsPathLimits.FileUnits`. |
| `src_mcp/runners/Worktrees/TreeGit.cs:8` | doc prose "passes 259 UTF-16 units" | gets a `<see cref="WindowsPathLimits.FileUnits"/>` beside the number. |
| `src_mcp/tests/WorktreeFailureTests.cs:65` | test reads `WorktreeFailure.WindowsMaxPathUnits` | reads `WindowsPathLimits.FileUnits`. |
| `src_mcp/tests/WorktreeFailureTests.cs:97-107` | the two pin tests of the old constants | move to the new `WindowsPathLimitsTests` (section 5). |
| `src_mcp/tests/WorktreeManagerTests.cs:381` | test threshold `BeGreaterThan(259, …)` | `BeGreaterThan(WindowsPathLimits.FileUnits, …)` — it is a threshold, not a pin. |

These stay as they are, on purpose: the literal `259`/`247`/`260` in the MESSAGE assertions —
`WorktreeFailureTests.cs:70,87,88,93,125` and `VibePathBudgetTests.cs:39,47,48,66`. They pin what the user reads. If
they derived from the constant, a wrong constant would make them agree with it and they could not go red.

### 2.2 Out of scope — a different limit, or prose only

| File:line | Why it is not this limit |
|---|---|
| `src_mcp/runners/Processes/ProcessTree.cs:302` `[InlineArray(260)]` | The SIZE of the `szExeFile[MAX_PATH]` field in the Win32 struct `PROCESSENTRY32W` — an ABI layout. It must stay 260 whatever the path limit is. Also `src_mcp/tests/HostProcessListTests.cs:70` (fills that buffer). |
| `src_mcp/core/Rounds/RoundSubject.cs:31` `trimmed.Length < 260` | A guess "is this plan text a path?". It is not a guard against the OS, it runs on every OS, and `CoaiMcp.Core` cannot reference `CoaiMcp.Runners` (Runners references Core). |
| `src_mcp/tests/ACallerTranscriptFixtureCarriesNoTextTests.cs:64,94` | A fixture path budget (160) with 260 in prose. |
| Prose about an incident "over 260 characters": `src_mcp/runners/Consultation/ConsultantHomes.cs:12`, `src_mcp/runners/Vibe/VibeRuntime.cs:230`, `src_mcp/src/Server/PanelSettings.cs:392`, `src_mcp/tests/VibeRuntimeTests.cs:120`, `src_mcp/tests/KimiHomeTests.cs:334` | History in comments and one assertion reason string. No number is enforced. |
| Prose in tests: `src_mcp/tests/DeepCommit.cs:8,20,25`, `src_mcp/tests/AReviewTreeWithALongPathTests.cs:11`, `src_mcp/tests/WorktreeManagerTests.cs:355` | Comments and a skip reason. `DeepCommit.AbsoluteFloor = 300` is a fixture length, not the limit. |
| Prose in `VibePathBudget.cs:9,12` and `WorktreeFailure.cs` remarks | Explanations that quote the probed numbers; the code beside them reads the constant. |
| Extension help text: `src_vs_code/src/help.ts:46`, `helpContent.ts:318`, `helpDe.ts:71`, `helpEs.ts:71`, `helpRu.ts:71`, `helpUk.ts:135`, `src_vs_code/package.json:633` | User-facing prose that says "Windows stops a path at 259". It is the same number, but it is TypeScript/JSON in another product and cannot reference a C# constant. Recorded as the open tail (section 8), not done here. |
| Other `260`/`248` hits (`codeHighlight.ts:437` an rgba colour, `suppression.ts:11` a line number, test token counts `260`, "~1 260 gate rounds") | Not a path limit. |

## 3. Decision

- **One new type:** `src_mcp/runners/Platform/WindowsPathLimits.cs`, namespace `CoaiMcp.Runners.Platform`, beside
  `HostKind.cs` — the folder that already holds "which platform" facts. Both callers live in the `CoaiMcp.Runners`
  project, so both can reference it with no new project reference.
- **Shape:** `internal static class WindowsPathLimits` with two `internal const int` members:
  `FileUnits = 259` and `FolderUnits = 247`. The `Units` suffix keeps the KERNEL's unit in the name
  (platform-limits §1 item 1: UTF-16 units, never bytes). `internal` is enough: both callers are in the same assembly
  and the tests have `InternalsVisibleTo` (`src_mcp/runners/CoaiMcp.Runners.csproj:45`).
  `VibePathBudget` is `public`, but only its value uses the constant, not its signature, so `internal` compiles.
- **Docs move, they are not copied:** the probe history of each number (P1/P2, git 2.55 on 2026-10-17, §27) goes to
  the XML doc of its constant. `WorktreeFailure` loses its two constants.
- **Scope of "one source of truth":** the C# code of `src_mcp`. The extension's help prose (TypeScript/JSON, another
  product) names the same 259 and cannot read a C# constant; it stays the open tail of section 8 (plan round 1,
  codex).
- **No behaviour change:** every number, every sentence and every platform branch stays the same. The platform
  choice stays where it is today (`VibePathBudget.ForThisProcess`, `OperatingSystem.IsWindows()` in
  `WorktreeFailure.Advice`); this type only holds the Windows numbers. No new platform is mapped.
- **Rejected:** keeping `WorktreeFailure.WindowsMaxPathUnits` as an alias of the new constant — that is a second name
  for one fact, which is what this plan removes. Putting the type in `CoaiMcp.Core` — nothing in Core needs it
  (`RoundSubject.cs:31` is a different check, section 2.2).

## 4. Build order

1. Tests first: add `src_mcp/tests/WindowsPathLimitsTests.cs` (section 5). It does not compile yet (RED).
2. Add `src_mcp/runners/Platform/WindowsPathLimits.cs`.
3. `VibePathBudget.cs:27` reads `WindowsPathLimits.FileUnits`; update the doc at :26.
4. `WorktreeFailure.cs`: remove the two constants, `:75` reads the new type.
5. Tests: `WorktreeFailureTests.cs:65` and `WorktreeManagerTests.cs:381` read the new type; delete the two old pin
   tests at `WorktreeFailureTests.cs:97-107` (their asserts move to step 1's class).
6. `TreeGit.cs:8` doc gets the `<see cref>`.
7. `research/module_runners.md`: the `WorktreeFailure` row (:48) and the `VibePathBudget` paragraph (:3495) name
   `WindowsPathLimits`; a new row beside `HostKind` (:87) for `Platform/WindowsPathLimits.cs`.
8. Build, run the touched test classes, the break-it, `dotnet format --verify-no-changes`.
9. Run the WHOLE suite once (`CoaiMcp.Tests.exe` with no filter), so a regression in an untouched class is seen
   before the pull request (plan round 1, vibe).

No growth surface: no table, folder, cache or process is added.

## 5. Test plan

New class `src_mcp/tests/WindowsPathLimitsTests.cs`:

| Test | What it proves |
|---|---|
| `TheFileLimitIsTheProbed259_AndTheFolderLimitTheProbed247` | Pins both numbers: `FileUnits == 259` ("P1 created 259 units, P2 refused 260"), `FolderUnits == 247` ("git made a 247-unit folder and refused 248"). |
| `TheVibeBudget_TakesItsFileLimitFromTheOneType` | `VibePathBudget.Windows.FileLimit == WindowsPathLimits.FileUnits`. |
| `BothCallers_ReadTheOneType` | Census with the existing scanner `ProductionSources.FilesMentioning("WindowsPathLimits.")` (comments and string contents removed): its keys include `src_mcp/runners/Vibe/VibePathBudget.cs` and `src_mcp/runners/Worktrees/WorktreeFailure.cs`. |
| `NoOtherProductionCode_SpellsTheLimit` | Census `ProductionSources.FilesMatching(new Regex(@"(?<![\w.])(?:259|247)(?![\w.])"))` over CODE (comments and string contents removed): the only key is `src_mcp/runners/Platform/WindowsPathLimits.cs`, with 2 hits. Companion assert in the same test: that file is in the result, so a scanner that sees nothing cannot pass. |

The existing message tests stay and must stay green with no edit to their assertions: `WorktreeFailureTests`
(folder → 247, file → 259 on Windows; no number elsewhere) and `VibePathBudgetTests` (102 accepted, 103 refused, the
sentence names 259/260/156). They prove "no behaviour change".

Run, per `.agents/PROJECT.md` (never `dotnet test`):
`dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4`, then
`node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*WindowsPathLimitsTests" --filter-class "*WorktreeFailureTests" --filter-class "*VibePathBudgetTests" --filter-class "*WorktreeManagerTests"`.

**Break-it** (each one is reverted at once, and the green run after it is recorded):

1. Put the literal back: `VibePathBudget.cs:27` → `new(259, 156)`. Expected RED:
   `NoOtherProductionCode_SpellsTheLimit` names `VibePathBudget.cs`, and `BothCallers_ReadTheOneType` misses it.
2. Change the constant: `FileUnits = 260`. Expected RED: the pin test, and `VibePathBudgetTests` /
   `WorktreeFailureTests` message tests (they still expect 259). Expected GREEN:
   `TheVibeBudget_TakesItsFileLimitFromTheOneType` — the caller follows the constant, which is the point.
3. Swap the folder and file constants in `WorktreeFailure.cs:75`. Expected RED:
   `ADirectoryThatIsTooLong_IsHeldAgainstTheFolderLimit_NotTheFileLimit` (Windows only — this machine is Windows).

## 6. Definition of Done

- [ ] `WindowsPathLimits.FileUnits = 259` and `.FolderUnits = 247` exist once, in `src_mcp/runners/Platform/WindowsPathLimits.cs`, with the probe history in their docs.
- [ ] `VibePathBudget.Windows` and `WorktreeFailure` read them; `WorktreeFailure` has no constant of its own left.
- [ ] No production C# code holds `259` or `247` outside that file (the census test).
- [ ] The four new tests are green; the existing `WorktreeFailureTests`, `VibePathBudgetTests`, `WorktreeManagerTests` are green with no changed assertion except `:65` and `:381`.
- [ ] The whole `CoaiMcp.Tests.exe` suite ran once with no filter; any red is named in the PR description with its cause.
- [ ] The three break-its went red as expected and green after the revert; the outputs are in the PR description.
- [ ] `dotnet format --verify-no-changes` is clean on the touched files.
- [ ] `research/module_runners.md` names the new type; the plan is promoted to `research/` with `IMPLEMENTED`; `plan-lifecycle.mjs` is clean.
- [ ] The coai code round reached `proceed`; the PR title is `refactor(...)` (not a releasing type).

## 7. Constraints

- No behaviour change: no sentence, no number, no platform branch changes.
- No new project reference; no public API change.
- The extension (TypeScript) is not touched.

## 8. Open tail (not done here)

The extension's help text names 259 in seven places (section 2.2). It is the same limit, in another product. A check
that the help text and `WindowsPathLimits.FileUnits` agree would be a cross-language census; it is not part of this
refactor and is recorded here so that the next reader can see it was checked, not missed.
