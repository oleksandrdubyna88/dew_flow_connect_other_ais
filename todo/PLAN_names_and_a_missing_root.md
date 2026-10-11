# PLAN — the Security lane names its reviewers, the server words a stranded pick, and a missing named data folder is never created by the server

> Status: **plan only, nothing implemented yet (2026-10-11).** Scope: `src_vs_code` (`securityLaneView.ts`,
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

- [ ] The Security lane's select, warning, prompt ticks and *Offered:* line name every row; no empty option text; RUN page tests.
- [ ] The server's stranded-row sentence matches the extension's; no server sentence shows `''`; tests pin both.
- [ ] No coai-mcp mode creates a confirmed-absent named root; serve answers every tool with the named refusal; the default location and `<root>/<side>` are still made; tests prove each.
- [ ] The extension does not launch the health checks under a missing root; the mode census reclassified.
- [ ] Help (five languages) for any new user-visible words; CHANGELOG under Unreleased; `module_extension.md`, `module_server.md`, `module_tests.md`; the paths plan's "Left" note corrected; this plan promoted when done.
