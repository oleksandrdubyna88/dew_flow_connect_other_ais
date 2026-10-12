# PLAN — the Security lane names its reviewers, the server words a stranded pick, and a missing named data folder is never created by the server

> Status: **built on `feat/names-and-a-missing-root` (2026-10-11), not yet merged — the operator promotes it after the
> merge; see "As built" for the deviations.** Scope: `src_vs_code` (`securityLaneView.ts`,
> `securityPromptCard.ts`, `consultantHealthPanel.ts`), `src_mcp` (`QuestionAdmission.cs`, `CapabilityMatrix.cs`,
> `Program.cs` start-up, `ConsultantCheckMode.cs`, the writer modes that open `RoundsDb`, `PanelSettings.cs`).
>
> Related docs: [PLAN_paths_per_side.md](../research/PLAN_paths_per_side.md) (D3 and "E5 as built", item C and the
> "Left" note), [module_extension.md](../research/module_extension.md), [module_server.md](../research/module_server.md).

## The request

The operator, 2026-10-11, after the paths-per-side work: three gaps that work left open, close to their request about
raw ids (PR #748), are to be done too — "да. тоже делай":

1. The Security lane's reviewer select still shows raw catalog ids.
2. The server's sentence for a model pick that is no longer offered is unclear (and can differ from the extension's).
3. A data folder a person NAMED that does not exist is never created by the extension (D3), but coai-mcp still creates it
   in the modes a person starts and when an MCP client starts it.

## The symptom, and the goal

**Today** (cited at `origin/main` 9079918e):

1. `securityLaneView.ts:32` draws every reviewer option as its raw id (`<option …>${esc(id)}</option>`); `:126` feeds it
   `vendors.map(v => v.id)`. A switched-off row has no mark; a pick no longer offered is a plain selected id; a `vendor: ''`
   pair (a hand edit, `securityLane.ts:132` accepts it) draws an empty option and the warning
   `Reviewer  is unavailable; select an enabled reviewer.` (`:148`, two spaces, no name). There is no *Offered:* line.
   The question consultant already says all of this well since #748 (`catalogPicks.ts:42-110`: `rowName`, `rowLabel`,
   `pickedLabel`, `strandedOption`, `strandedNote`, `rowPicks`, `offeredLine`, `optionHtml`).
2. For a question row whose catalog row is gone or no longer ticked (sent as `{vendor:'<id>', runtime:''}`), the server
   blocks it per question with `the row '<id>' names the vendor '<id>', which is neither a reviewer row nor a runtime this
   build can launch a question on (claude, codex, antigravity, local, api) — give the row a runtime of its own, or name a
   reviewer, in …` (`QuestionAdmission.cs:180-185`). The extension says the same case as `<id> is no longer ticked
   question consultant (or was removed) — pick another model` (`qconsultWrite.ts:52-75`). No test pins the server's
   sentence. `CapabilityMatrix.Admit` (`CapabilityMatrix.cs:70-77`) would word an empty runtime as `'' is not a runtime …`.
3. With `COAI_DATA_DIR` naming a folder that does not exist:
   - **serve** (an MCP client's launch, `Startup.Serve`, `Program.cs:1915`): the file logger is built under
     `<data>/logs` first (`Program.cs:1935-1938` → `CoaiLogging.cs:55`), then run markers (`RunMarkers.cs:169`), the
     notices ledger (`JsonlLedger.cs:157,178`), service construction with its start-up sweeps (`Program.cs:1998`), the
     consultants survey (`Program.cs:2061` → `ConsultantsSurveyClaim.cs:123`) — the folder is made at once;
   - **`--check-consultant` / `--check-model`**: `ConsultCheckLock.TryTake` → `ConsultCheckState.cs:428`
     `CreateDirectory(<data>/consultations/health)` (recursive, so the named root is made); the extension launches them
     unguarded (`consultantHealthPanel.ts:162-174`);
   - **`--collect-bugs`, `--pairs-keep`, `--pairs-decide`, `--upload-pairs`** (and the test-only `--requeue-refused`):
     `RoundsDb.Open` → `RoundsDb.cs:66` `CreateDirectory`.
   - `--check-security` never touches the data directory (`CheckSecurityMode.cs:58-132`) — the paths plan's "Left" note
     named it wrongly.

**When this is done**

1. The Security lane's reviewer select reads exactly like the question consultant's: `GPT-6-Astra (chat-preset-…)` with the
   id as the value, `(switched off)`, `(stranded)` with the named sentence, no empty option text, an *Offered:* line with
   **Change on Models**; the warning names its row, and an empty pick says "names no reviewer — pick one". For a pick
   whose row is GONE the sentence says the server drops that pair (it does: `SecurityLaneSetting.cs:281-286`), not that
   the pair "keeps asking for it".
2. The server words the stranded question row as the extension does — `<id> is no longer ticked question consultant (or
   was removed) — pick another model, in <section>` — and no server sentence can show `''`.
3. No coai-mcp mode creates a NAMED data folder that is confirmed absent (an unknown answer — a sleeping share — never
   counts, and the default location is still made lazily, and `<root>/<side>` inside an existing root too):
   - the writer modes stop at once with one stderr sentence and exit 74 (the extension already words 74 as "could not
     use its data directory");
   - **serve** starts a refusal-only server (decided below): the stdio protocol answers, every tool call returns an MCP
     error naming the folder — "restore it, then restart the MCP server" — and nothing is written;
   - the extension does not launch `--check-consultant` / `--check-model` while the root is missing (the
     `consultantsProbe.ts:106-109` guard).

## Decision — how serve refuses (consulted, 2026-10-11)

Options: (A) exit 74 with a stderr sentence — MCP clients often show only "server failed"; (B) start a refusal-only
server; (C) full serve with every writer refusing — large. The question consultant had no active row; the gate's
consultant (`consult`, gpt-6-astra) recommended **B as a separate start-up branch, before the logger is built**: a
minimal stdio transport with the existing tool list and a refusing service source that throws `McpException(reason)`
(the SDK keeps that message in an error tool result; an ordinary exception gets generic text); no normal host, notices,
markers, survey or sweeps — which a "disable the logger and refuse tools" version would still start
(`Program.cs:1998`, `:2061`; a failed `StartingHost` closes the transport at `:2047`). **Chosen: B**, verified by the
test in story 3.2 before anything else.

## Stories

### 1 — the Security lane names its reviewers (extension)

- **1.1** `securityLaneView.ts` builds the Reviewer options from `rowPicks('security', r.vendor, allRows, 'This pair')`
  through `optionHtml`; an empty pick gets a leading disabled "no reviewer — pick one" option (the `qconsultView.ts:192`
  pattern); `reviewerWarning` names the row by `rowLabel` and says "names no reviewer — pick one" for `''`; an
  *Offered:* line (`offeredLine('security')`). The prompt cards' reviewer ticks (`securityPromptCard.ts:43-44`) named by
  `rowLabel`. A GONE row's sentence says the server drops the pair until another is picked (a security-specific wording
  of `strandedNote`, not a change to the question consultant's).
- **Test (RED first), RUN the page** (`runPanel` + `pageTree`, after `aCatalogRowIsCalledByItsName.test.ts:37-75`), new
  `aSecurityPairNamesItsReviewer.test.ts`: a named ticked row → label `GPT-6-Astra (chat-preset-…)`, value the id; a row
  present but not ticked → `… (stranded)` and the named sentence; a gone row → `<id> (stranded)` and "dropped"; `vendor:
  ''` → no option with empty text, no `Reviewer  is`, a "pick one" line; a switched-off row → `(switched off)`; the
  *Offered:* line with `data-models-uses="security"`. Compiling break-its on each deciding line.

### 2 — the server words a stranded pick as the extension does (server)

- **2.1** `QuestionAdmission.cs:180-185`: for a row with no runtime of its own whose vendor is neither a reviewer row nor
  a runtime, the reason becomes `<vendor> is no longer ticked question consultant (or was removed) — pick another model,
  in <section>`; a bare id that is not a catalog id keeps the runtime list as its tail.
- **2.2** `CapabilityMatrix.Admit`: an empty or blank runtime is refused with "this row has no model to run on — pick
  one", never `''`. The decision does not change, so `shared/capability-matrix-vectors.json` stays valid.
- **Tests (RED first)**: `QuestionAdmissionTests` — a row `{vendor:"consult-claude", runtime:""}` with no reviewers is
  blocked with the extension's sentence and no `''`; `CapabilityMatrixTests` — `Admit("", …)` refused without `''`.

### 3 — a missing named data folder is never created by the server (server + extension)

- **3.1** One pure helper in `PanelSettings` — `NamedRootMissing(env, presence)`: `COAI_DATA_DIR` set AND the ROOT
  (`DataRootFor`, before the side) is confirmed `Absent` by `QuestionRoots.PresenceOf(…, wantsDirectory: true)`; it gives
  the extension's sentence (`findingsBoard.ts:197-199`). Checked at the top of every writer mode (`--check-consultant`,
  `--check-model`, `--collect-bugs`, `--pairs-keep`, `--pairs-decide`, `--upload-pairs`, `--requeue-refused`) → stderr
  sentence, exit 74, nothing written.
- **3.2** Serve: before the logger is built, the same check branches into the refusal-only server (decision above).
- **3.3** Extension: `consultantHealthPanel.runCheck` refuses while the root is missing, as `consultantsProbe.asked` does;
  `everyServerModeTheExtensionLaunches.test.ts` reclassifies `--check-consultant` / `--check-model` as "a person acts, held
  by the extension" and `--check-security` as touching no data directory (and it joins
  `AReaderModeNeverMakesTheDataDirectoryTests.ReaderModes`).
- **Tests (RED first)**: C# — `--check-model` through `ConsultantCheckMode.AnswerModelAsync` with a missing named root
  and a launcher that fails every call: the folder stays absent, exit 74 (RED today at `ConsultCheckState.cs:428`);
  `--check-consultant` likewise with a consultant that passes preflight (otherwise the unavailable path writes nothing
  and the test is vacuous); each RoundsDb writer mode; **serve** through the existing `StdioServer` harness
  (`src_mcp/tests/StdioServer.cs:15`): initialize, list tools, wait past a sweep interval, call `providers` — the root is
  absent throughout and the tool result has `isError: true` and the exact path (RED today: the logger). Positive
  controls: `COAI_DATA_DIR` unset → no refusal; a side folder inside an existing root still made; an UNKNOWN presence
  never refuses. TS — `runCheck` with a missing named root calls no run port.

### Added by the plan round (2026-10-11, both accepted)

- **The helper is three-state, and only a confirmed absence refuses** (finding 0). `NamedRootMissing` asks
  `QuestionRoots.PresenceOf(root, File.GetAttributes, wantsDirectory: true)`, which already answers
  `Present` / `Absent` / `Unknown` and maps an inspection error (EACCES, EBUSY, EIO, a timeout) to `Unknown` — never a
  `Directory.Exists`-style boolean, which reports an error as `false`. Test: a named root the process cannot inspect
  (an injected presence that throws `UnauthorizedAccessException` / `IOException`) is NOT refused, in every mode and in serve.
- **The two halves agree, proved against the real binary** (finding 1). A seam leg `scripts/seam-missing-named-root.mjs`
  in `npm run test:seam`: for (a) a named root that does not exist, (b) a named root that exists with a missing side
  folder, (c) no `COAI_DATA_DIR` at all (the default location), the extension's own decision (`dataDirFinding` /
  `absentDataRoot` over the same environment) and the real coai-mcp (`--check-model` and a serve `initialize` + one tool
  call) must agree: (a) both refuse, nothing is created; (b) and (c) neither refuses, and the side folder / default
  location is made lazily as today. A disagreement fails the leg, naming both answers.

## Build order

1 (extension only) → 2 (server wording) → 3.1 → 3.2 → 3.3. One branch, one pull request
(`feat/names-and-a-missing-root`), one coai-mcp release then one extension release.

## Test plan

Every story RED first with its failure message recorded, then GREEN, then a compiling break-it on the deciding line.
Full suites before the PR: `npm run compile`, `npx eslint src`, `npm test`, `npm run test:host`, `npm run test:seam`,
`npm run test:contract`; `dotnet build dew_flow_connect_other_ais.slnx` and `CoaiMcp.Tests.exe` (never `dotnet test`);
the family checks (`plan-lifecycle`, `pin-check`, `gate-snippet-check`).

## Risks

1. **The refusal-only server is a second start-up path** — kept minimal (no host, no sweeps) and pinned by the stdio
   test; its message says "restore the folder, then restart the MCP server", because the server does not watch for the
   folder to come back.
2. **A sleeping share** answers `unknown` — never refused, as in the extension's guard (`absentRoot`).
3. **A client entry whose `COAI_DATA_DIR` equals the default path** is treated as named — the extension does the same
   (paths plan, item E).

## Definition of Done

- [x] The Security lane's select, warning, prompt ticks and *Offered:* line name every row; no empty option text; RUN page tests.
- [x] The server's stranded-row sentence matches the extension's; no server sentence shows `''`; tests pin both.
- [x] No coai-mcp mode creates a confirmed-absent named root; serve answers every tool with the named refusal; the default location and `<root>/<side>` are still made; tests prove each.
- [x] The extension does not launch the health checks under a missing root; the mode census reclassified.
- [ ] Help (five languages) for any new user-visible words; CHANGELOG under Unreleased; `module_extension.md`, `module_server.md`, `module_tests.md`; the paths plan's "Left" note corrected; this plan promoted when done. *(All but the promotion, which the operator does after the merge.)*

## As built (2026-10-11)

Eight commits on `feat/names-and-a-missing-root`, one per story plus the seam leg, the docs and the measurement of the
three person's modes the plan did not list. Every story went RED
first with the real symptom (the rows of [module_tests.md](../research/module_tests.md), "Names and a missing root",
carry each message), then GREEN, then compiling break-its on the deciding lines, restored in `finally`.

**What shipped as planned:** story 1 through `catalogPicks` (`rowPicks` + `optionHtml`, `rowLabel`, `offeredLine`) — no
second naming; story 2's sentence; the three-state `NamedRootMissing` (only `Absent` refuses; `Unknown`, the default
location and `<root>/<side>` never); the writer modes' exit 74; serve's refusal-only branch before the logger (decision
B); the checks held by the extension; the census reclassified; the seam leg in `npm run test:seam`.

**The consultant's advice, verified first.** `TheServerRefusesAMissingNamedRootTests` was RED on today's binary
(`initialize made the missing data folder, holding: consultations, logs, runs, …`), GREEN with the branch, and the break-it
that throws an `InvalidOperationException` instead of an `McpException` reached the client as *"An error occurred
invoking 'providers'."* — so the SDK keeps an `McpException`'s message in the error tool result (`isError: true`) and
hides any other, exactly as advised. Nothing was written past a sweep's beat (12 s at `COAI_SWEEP_SECONDS=10`).

**Deviations:**

| # | Planned | Built | Why |
|---|---|---|---|
| 1 | 2.1: "a bare id that is not a catalog id keeps the runtime list as its tail" | A vendor that is a RUNTIME's own name this build knows (`ReviewerRuntimeSelector.RuntimeNames`, e.g. `gemini`, `remote`) keeps the old sentence with the runtime list; any other id gets the extension's sentence; an empty pick says *"this row picks no model — pick one, in …"* (the extension's own words) | The server cannot tell a catalog id from any other id — it is never sent the catalog's ids for a stranded row. The runtime-name set is a fact it has. |
| 2 | 3.1: the check "at the top of every writer mode" | Once in `Program.RunAsync` for `Program.WriterModes`, AND inside `ConsultantCheckMode.AnswerAsync` / `AnswerModelAsync` (the entry points the plan's test drives) | One list instead of seven copies; the inner guard is the plan's test path. The seam break-it showed the redundancy is real: the dispatch guard alone disabled stays green because the entry guard still refuses. |
| 3 | 3.1: helper `NamedRootMissing(env, presence)` | `NamedRootMissing(env, Func<string, FileAttributes> attributesOf)` + `PanelSettings.NamedDataRoot` / `NamedRootRefusal(attributesOf)` for code holding settings | The attribute read is what `QuestionRoots.PresenceOf` takes, so the three-state mapping stays that one function's; a test injects a read that throws `UnauthorizedAccessException` / `IOException`. |
| 4 | Unknown "not refused, in every mode and in serve" | Proved on the helper, on the dispatch decision (`Program.MissingRootRefusal`) and on serve's (`Program.ServeRefusal`) with an injected read — not by running each mode with one | The modes read `File.GetAttributes` themselves; the decision every one of them takes is the one tested. |
| 5 | 3.2: "console logging to stderr only" | No logger at all in the refusal-only server: the reason is written once to stderr (`Program.Note`), and the end is exit 74 | Nothing else happens in that process worth a log line, and no Serilog sink can then make a file by accident. |
| 6 | 3.3: `consultantHealthPanel.runCheck` refuses | The guard is in `consultantCheckRun.runConsultantCheck`, which `runCheck` calls; a held check is `crashed` with the finding's sentence + *"The check was not started — nothing was sent."* | `runCheck` is `vscode` wiring no unit test can load; the module it calls is. `crashed` is the shape the existing *model removed* hold already uses, so no new result kind (and no new table words). The board is read at once when it has its first answer — `consultantsProbe.asked`'s own one-line expression, repeated rather than wrapped, because an `await` on a wrapper would move the spawn out of the turn the existing tests pin. |
| 7 | 3.3: `--check-security` "touching no data directory" | A census kind of its own (*touches no data directory*), held by the server's reader inventory; the two checks' kind (*a person acts, held by the extension*) is held by a new census test that requires them in the server's writer test | The plan named the reclassification; this is how each new kind is held. |
| 8 | Seam (c): a served `initialize` + tool call with no `COAI_DATA_DIR` | Served only on POSIX (`XDG_DATA_HOME` moves the default into a temp folder); on Windows (c) asks `--check-model` alone, which writes nothing for an unavailable row | On Windows .NET reads `%LOCALAPPDATA%` from the shell, not the environment: a served run would write logs, a run marker and sweeps into the developer's own data. |
| 9 | Help | Two additions in five languages: the `security-lane` article (the marks) and `where-your-data-lives` (the server creates no missing named folder either); both held by `helpCoverage.test.ts` | — |
| 10 | The writer modes named in 3.1 | Plus `--probe-api`: measured to make the root (its `usage.jsonl` line), so it joined `Program.WriterModes` and `ProbeApiMode.RunAsync` refuses before the vault read and before anything is sent | The DoD says NO mode creates a confirmed-absent named root; the three modes the plan did not list were measured (the coordinator's follow-up, same day). |

**The three person's modes the plan did not list, measured.** `--probe-api` made the root (`holding: usage.jsonl`) and
is now refused (exit 74, nothing sent). `--close-consult` makes nothing — it finds no record under a missing root and
refuses with 65 — and `--tree-remove` makes nothing — it works under the review root; both are pinned by
`AReaderModeNeverMakesTheDataDirectoryTests`. The census (`everyServerModeTheExtensionLaunches.test.ts`) has no
unmeasured *a person acts* kind any more: every launch is held by the server's reader test or its writer test.

**The code round (2026-10-12): the gate (codex, 4 reviewers, `proceed`) and this repository's own reviewer.** The gate:
the help in five languages — rejected (the owner's standing ruling); `NamedDataRoot` and `DataDir` as two independent
fields — accepted as a pinned invariant: one construction (`PanelSettings.WithCatalog`) fills both from one environment
and nothing in `src_mcp/src` moves `DataDir` with `with`, so `TheNamedRootAndTheDataDirAreBuiltTogetherTests` pins exactly
that (two compiling break-its red); merging them into one value was rejected as a larger change than the risk, which
has no caller today. The own reviewer found three real defects: **`--consultants` still made a missing named root**
from a terminal or an older extension (its survey file) — RED as recorded in `module_tests.md`, now in
`Program.WriterModes`, the census tightened to leave only `--help` unheld; **the three CHANGELOG paragraphs sat inside
the RELEASED `Extension 0.69.0` section** after the rebase — moved to a new `## Unreleased`, the released sections
byte-identical to main; **the seam leg's case (c) on Windows read the developer's real default folder and could call
the real vault** — every child now gets an empty `COAI_CREDS_KEY` and no inherited side, and (c) on Windows asks the
extension only. Also corrected: `module_server.md` no longer claims the server words an EMPTY pick as *"this row picks no
model"* for a person — a vendor-less row is dropped at parse time with its own complaint; `NoModel`'s sentence is the
defence for a row built in code. Left as noted, not changed: the refusal server ends on SIGTERM/SIGINT or stdin closing
without the full server's `Environment.Exit` backstop (Claude Code's own SIGKILL follows within half a second); the C#
and TS admission checks order a blank runtime and an unknown capability differently (same decision, sentences may
differ by the vectors' own rule); three older tests keep a private `RepoRoot()` while the new `RepoPaths.Root()` is the
shared one (not rewritten unasked).
