# module_tests — the harness, the flows it drives, and what it does not prove

> The record the family rule `common/scenario-tests.md` requires of every repository, at the fixed
> name it requires, so a reader looking for the tests of an unfamiliar repository has one place to
> look. Enforced in part by `ScenarioCoverageTests` (`src_mcp/tests/ScenarioCoverageTests.cs`), which
> derives the flow list from the tool registry rather than from this document.
>
> [testing.md](../.agents/conventions/common/testing.md) governs how a test is written and believed;
> this describes what exists.

## Where the harness is

Security lane scenarios are in `SecurityLaneRoundTests` (real Git, engine and history; paid reviewers
doubled), with settings/evidence/coverage cases in the other `Security*Tests`.
`SecurityEvidenceOnAnOlderDatabaseTests` drops `findings.security_evidence` from a written history and
reads it through every read-only finding query, and reads a damaged evidence projection as an
explicit `Unreadable` marker rather than as none.
`ASecurityPreviewDatabaseKeepsBothStepsTests` opens a database the security preview stamped 17
(evidence column, no question tables) and a database main stamped 17, and both reach the head of
the schema. `AConsultantPreviewDatabaseKeepsEveryStepTests` is its sibling for the consultant preview
(`consultations.failure_kind` stamped 17, no question tables): that file at 17, the same file after a main build
moved it to 18, and main's own database at 18 all open and reach step 19; it also pins 17/18/19 by position. `SecurityLaneBudgetTests` and
`SecurityLaneBoundaryTests` cover the lane's own round budget (it never widens the ordinary roles'),
the empty lane-only round, blank overrides, broken pairings, patch-only slices and the schema's
per-field limit.
The extension's `securityLane.test.ts` and `securityLaneMalformed.test.ts` run the Settings page —
the older-server switch, malformed pairs, and stored values that must stay inside their controls.
The eighth leg of `npm run test:seam` (`scripts/seam-security.mjs`) sends the extension-serialized
lane to the built server: a valid lane draws no complaint, an unknown trigger and a malformed lane
each do. `securityEvidenceLog.test.ts` parses evidence and runs the rounds-log page (through
`roundsLogPagingHarness.ts`) to check its disclosure appears only on security findings, as text.
`SecuritySessionCompatibilityTests` exercises the real session store and source-generated JSON
serializer with pre-lane pending/rejected findings, both omitted and null cap reasons, and a
nonempty new-evidence control. It verifies save/reload without losing findings or rejection
reasons, and the ordinary finding's empty SQLite evidence projection. It does not invoke a paid
reviewer or establish model accuracy.
`SecurityLaneCalibrationTests` is an explicit Windows/Ollama test, excluded from ordinary CI.
It runs three code rounds with the operator's embedded prompts and retains requests, responses,
hashes and session history after each cell. The command and current measurement status are in
[RESULTS_security_lane_qwen_windows.md](RESULTS_security_lane_qwen_windows.md). Run it before a
security-lane release or when changing these prompts, the model or its runtime; the implementing
agent/operator owns that run. Deterministic tests do not establish model quality or prove that
Ollama consumed the full input; the live run does not execute reproductions.
`SecurityLaneAuditTests` separately runs every catalogue prompt (thirteen since `redteam-general` shipped on 2026-10-04; `COAI_SECURITY_AUDIT_PROMPTS` narrows them) against an explicitly selected committed
feature, one at a time, preserving every request, response and failure for consultant triage. Its
ordinary reviewer is a clean test double; the campaign does not replace the COAI code/feature gates.
Both hardware harnesses accept `COAI_SECURITY_CALIBRATION_MODEL` to select the installed Ollama
model; if unset they retain the original Qwen default. `COAI_SECURITY_CALIBRATION_ENDPOINT` names
the OpenAI-compatible endpoint (default `http://localhost:11434/v1`). The audit also reads
`COAI_SECURITY_AUDIT_SCOPE` (else the design plan in `todo/`, then `research/`) and
`COAI_SECURITY_AUDIT_MODEL_CONTEXT_TOKENS` (default 131072), which its manifest records as
configured by the operator, not measured. Their manifests record the selected model and endpoint,
and the reviewer vendor ID is `local-security` independently of the model family.
The calibration fixture includes written repository rules so its Conventions reviewer participates.
`SecurityLaneEffortMeasurementTests` (explicit, 2026-10-13, epic 2 of
[PLAN_local_effort_reaches_every_local_launch.md](PLAN_local_effort_reaches_every_local_launch.md)) runs ONE
effort arm through the same `RunCell` — `COAI_SECURITY_CALIBRATION_EFFORT` (`none`/`low`/`medium`/`high`, default
`none`), `COAI_SECURITY_CALIBRATION_MAX_TOKENS` (default 8192), `COAI_SECURITY_CALIBRATION_REPEATS` (1–10, default 3)
and `COAI_SECURITY_CALIBRATION_FIXTURE` (`unsafe`, the default, or `safe`: the safe controller with `ORDER BY Id` added,
whose correct answer is empty) — into `<out>/<effort>-<ceiling>-<fixture>-<utc>/`. Unlike the qualification test, a
reviewer cut at its ceiling does not fail it (`RunCell(…, strict: false)`): the cut is the observation. It still fails on
an error reply, a prompt that never reached the shim, or any cell whose recorded argv (`turn-N.arguments.json`, written by the
recording launcher for every `--ask-local`) did not carry the arm's effort and ceiling. Results:
[RESULTS_security_lane_effort_2026-10-13.md](RESULTS_security_lane_effort_2026-10-13.md). Its pure half is in CI:
`SecurityLaneEffortArmTests` (18) — the defaults reproduce today's product, every input read, each out-of-range input
refused naming what is legal, a cell that did not differ named, no cell at all a mismatch, the safe twin keeping the
guard and the parameters and changing only the ordering. Green on first run; four mutations built and run at once —
the ceiling check disabled, an empty cell list passing, the safe twin equal to its base, `0` accepted as a count — turned
five tests red, each its own. **The code round added** (RED first against stubs that passed everything): only a cut at
the ceiling is an observation — an unreachable engine, a timeout or an empty answer fails the cell
(`UnexpectedFailures`; RED: *found empty collection*), and the round's own "A of N reviewers answered" line is read
(`NotAnswered`; RED: *found 0*) so the non-strict `RunCell` also fails unless every reviewer that did not answer is one
of those cuts — the four ordinary reviewers answered — and unless the line names exactly the reviewers configured
(CodeRabbit on #800: "all 5 reviewers answered" of six configured is -1; RED first: *found 0*). Both applied to the recorded pilot cells: 5 answered, 5 cut, 0
other, and not-answered equals cuts in every arm.
Passing these hardware tests establishes usable replies and wiring; semantic accuracy of each
finding and reproduction is inspected separately in the [Windows results](RESULTS_security_lane_qwen_windows.md).
`SecurityPresetTests` also covers the historical-PR routing regressions: prose-only changes do not
activate application audits, UI words and DOM/command APIs do not count as SQL, and real query calls,
removed controls, SQL scripts and configuration still participate. Source files under `research/`
remain eligible. These tests check routing behavior; they do not establish semantic model accuracy.

Shared-rule adoption adds real filesystem scenarios: `RuleFilesTests` exercises neutral
PROJECT/local/shared discovery, ordering and missing mount bodies while retaining legacy cases.
`snippetDiscovery.test.ts` calls the same reader the panel uses, against temporary files:
a neutral shared rule is current, and older root/project/local copies take priority.
`src/test/prepareGate.test.mjs` checks frontmatter boundaries and a real Git submodule's
clean/dirty/wrong-pin transitions. A failed build preparation leaves no old generated policy.
These run through `npm test`; no paid model calls occur in that suite. The independent
version/hash test remains, and compares generated delivery with the canonical rule body.

Historical local run on 2026-09-10 at `906ab9c`: neutral C# scenarios first failed with zero discovered files
and no missing mount; all 22 RuleFiles cases then passed. Panel discovery first returned
absent and passed after the location update. Extension suite: 1313 tests, 1312 passed and
one skipped, plus all three build-preparation scenarios. These are local adoption results;
native Codex acceptance and consumer publication remain open in the adoption plan.

Three suites, all in the repository, all in the language of the half they drive.

| Suite | Path | What it drives |
|---|---|---|
| Server scenarios | `src_mcp/tests` | The MCP server: real process, real stdio, real JSON-RPC (`McpContractTests`), and whole rounds end to end (`EndToEndTests`, `LiveRoundTests`) |
| Extension suite | `src_vs_code/src/test` | The extension's pure halves, and the page as it SHIPS — `bundledPage.test.ts` bundles and minifies the real module and runs the page script against a stub DOM |
| Campaign harness | `src_bench` (`coai-bench`) | The whole product from outside: arms of vendors × cases × repeats, driving the real server over the real transport, with a judgement pass over the findings it produced |

The campaign harness is in this repository rather than a repository of its own because it measures
**this** product and nothing else; the rule's separate-repository shape (`dew_flow_benchmark`) is for
a harness that must measure several things. It is C#, like the server it drives, so a renamed field
is a compile error rather than a scenario that quietly stops matching.

## How it is run

Exactly what CI runs — [`.github/workflows/ci.yml`](../.github/workflows/ci.yml):

```bash
# server + core, as an executable (xUnit v3 on Microsoft Testing Platform — never `dotnet test`)
dotnet build src_mcp/tests/CoaiMcp.Tests.csproj
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe

# extension
npm --prefix src_vs_code test

# the campaign harness: build, then one campaign, then the judgement over what it produced
dotnet build src_bench/CoaiBench/CoaiBench.csproj -c Release
./src_bench/CoaiBench/bin/Release/net10.0/coai-bench.exe run  --out artifacts/bench/<name> …
./src_bench/CoaiBench/bin/Release/net10.0/coai-bench.exe judge --runs artifacts/bench/<name>/runs.json …
```

Locally, the same commands run **capped and lowered** (`common.local-cpu-budget`, 2026-10-11):

- `npm test` and `test:contract` start `node --test` through `nodeTestArgs()` in `scripts/run-tests.mjs`, which caps
  it at `min(8, CPUs - 1)` workers (a cap, never a raise: a 4-vCPU CI runner keeps node's default of 3), and lower themselves first through `scripts/lower-priority.mjs`, the family template copied
  with its MOUNT line set. That means BelowNormal on Windows and nice 19 elsewhere; under `CI` nothing is lowered.
- The four xUnit projects read `maxParallelThreads: 8` from an `xunit.runner.json` copied to their output. Measured on
  xunit.v3 4.0.1: an uncopied file is never read, and the runner then takes 24 threads. The copy is conditioned on
  `'$(CI)' != 'true'`, so CI keeps its own default: an exact 8 would raise a 4-vCPU runner.
- Executables and builds run through `node .agents/conventions/tools/low-priority.mjs …`.
- `src_vs_code/src/test/localCpuBudget.test.mjs` pins the cap, the preload against its template, the preload's effect
  on a real child process, every test project's config and Content item, and the documented commands. Break-it:
  dropping the cap and dropping one Content item each redden exactly their own case.

## When it runs

- **The two test suites: in CI, on every push and pull request**, plus the family checks (pin,
  plan lifecycle) and an AOT publish. A release is not cut without them.
- **The server, Team server and bench suites again on macOS, on every pull request** (the `macos`
  job, since 2026-09-23). Every other pull-request job is Linux, and `mcp-v0.31.0` found a macOS-only
  defect — a review tree behind a link, #470 — at the tag, because the release matrix was the first
  thing to run the suite there. The ingest server's suite is not in it: that server ships for Linux.
- **The campaign harness: by hand, and not in CI** — it spends real vendor subscriptions and needs the
  local GPU for the `local` arm. Run by the operator before a release that changes reviewing
  behaviour, and whenever a question about vendors needs an answer with a number. Its results are
  committed under `research/data/<date>/` and written up as `research/RESULTS_*.md`, which is what
  makes a campaign a fact rather than a memory.

## The flow catalogue

Derived from the tool registry (`src_mcp/src/Tools.cs`) by `ScenarioCoverageTests`. Adding a tool
fails that test until the tool is covered here or declared uncovered with a reason.

| Flow | Covered | By |
|---|---|---|
| `providers` | yes | `McpContractTests` — over real stdio against a real `coai-mcp` process |
| `open` | yes | `EndToEndTests` — the whole story, a flawed plan through to a verdict |
| `review_plan` | yes | `EndToEndTests`, `LiveRoundTests` |
| `review_code` | yes | `EndToEndTests`, `StageGateTests` (refused before a plan reaches `proceed`) |
| `review_document` | yes | `ADocumentIsReviewedEndToEndTests` — a document role nobody compiled in, with every shipped role switched off, reading a real document through the public tool: the round reports one reviewer, the finding carries that role and a DOCUMENT category (`completeness`, which the parser dropped as unknown before this plan), the reviewer's prose comes back in `notes` under its provider and role, the branch's own session is untouched, the snapshot is on disk under its content hash, and `resolve` reaches the document's session when it is given the document and says so when it is not. Asked for BY the plan round in those words: every other test of this feature can be green while `review_document` returns an empty round, because the wiring is the one thing none of them touches |
| `review_feature` | yes | `AFeatureIsReviewedEndToEndTests` — a real repository and the fake CLI as the feature reviewer: the pack that reaches the reviewer's stdin (plan, epics, lessons, history, rules, range, `*` marks, the changed member's hunk, the feature schema, the outline truth — and no unchanged member's body, no credential file's content), the source request recorded on the reviewer's note, `resolve(feature)` and `status(feature)`, the D17 and no-reviewer skips (non-blocking, nobody launched), and D23's rounds (S3.4): a non-blocking round closes on `resolve` and `again` over the same base is refused naming the grounds; a blocking finding admits round 2 and a blocking round 2 calls a person; a partly failed round retries only the reviewers that failed and carries the answered ones' decided findings; every reviewer failing buys a retry of everyone, then `call_human`; the person's answer file admits round 2 where no argument of the caller's does; a third round is refused. Its refusals — every one before the engine — by `TheFeatureStageRefusesBeforeItBuildsTests`; the pack's credential handling by `ThePackWithholdsCredentialsTests` |
| `resolve` | yes | `EndToEndTests`, `RoundAuditTests` — a decision is recorded for every finding; its `feature` argument by `AFeatureIsReviewedEndToEndTests` |
| `status` | yes | `McpContractTests`, `CallerSessionsTests`; its `document` argument by `ADocumentIsReviewedEndToEndTests`, its `feature` argument by `AFeatureIsReviewedEndToEndTests` |
| `consult` | yes | `ConsultScenarioTests` — a real git repository with real uncommitted work, the fake CLI standing in for codex: a conversation opened, resumed by the vendor's own thread id, closed by the cap, and every refusal read back as a sentence. It carries the two things no unit test can reach — that the SERVER's own collection is what the consultant is handed (the argv recording asserts the modified tracked file, the untracked file and the problem's position in the prompt), and that a consultant which writes in the tree has its advice withheld while the file is left where it is, driven by `FAKECLI_SIDE_EFFECT`, a stand-in behaviour added for exactly this. Its unit-level siblings: `ConsultantArgvTests` (every flag, the absence of `--ephemeral`, no line break in any value), `ConsultantHandleTests` (a handle read off a TRUNCATED stream, and refused when malformed), `FilesystemInvariantTests` (a real repository, a linked worktree's common hooks, and a rewrite that preserves size and mtime), `WorkingTreeDiffTests`, `ConsultationStoreTests` (the sweep's outcomes — dead pid to interrupted/failed, idle-close, retention, and an `asking` record past its turn deadline swept although its server is alive), `ConsultationDeadlineTests` (the turn deadline is longer than the launch and its grace never below the launcher's drain), `ConsultCallCounterTests` (the cap holding with no file to write it in), `ConsultantPromptTests`, `ConsultBudgetAndFenceTests`, `ConsultGateFixesTests` and `DiffSplitterTests` |
| An antigravity consultation whose permission was denied (2026-10-02, epic 1 of `research/PLAN_the_consultant_works_on_every_vendor.md`) | yes | `ConsultAntigravityDenialScenarioTests` — on `ConsultScenarioBase`'s harness over the fake CLI, fed `tests/fixtures/antigravity/consult-denied.ndjson` + `.stderr.txt` — the REAL 2026-10-02 denied transcript — and `consult-advice.ndjson`, a real follow-up that answered on the WSL side of the E1.1 measurement, RE-KEYED to the same conversation; both with machine paths redacted and the real conversation id replaced by a synthetic one. Covered: a denied command is continued once in the same conversation and the advice arrives, with the toolbox in the first prompt and exactly one `--conversation` on the second launch; the turn is recorded `FollowedUp` and billed by the field-wise maximum; a failed LATER turn is billed only its own share; denied again → exactly two launches, record failed, handle kept; a `read_file` denial gets its own wording; a first launch that writes in the tree is a breach and is NOT followed up; a hung follow-up after a first launch that spent most of the budget is killed by the launcher within the budget plus a 3 s margin (the cap's own check), the record left resumable with the first launch's handle and tokens, not a `deadline`. Beneath it: `AntigravityStreamContinueTests` (`Continue` replaces or appends one flag and refuses a malformed id — `--x`, `a\r\nb` — or an argv with a line break; `DeniedActions` reads spaced JSON, drops non-words, ignores `auto-denied` in stdout and reads it in stderr; a `write_file` / `mcp` denial is offered no follow-up; the measured follow-up text byte for byte; the toolbox names only `view_file`), `ConsultantTurnTests` (never a third launch, the follow-up's timeout STRICTLY under the first's, the tree not asked when no follow-up is coming, no time left → no follow-up, a tree check that spends the budget → one launch, an answer beside a denial or a launch that failed on its own → one launch, a denial outside the allowlist → one launch, a breach carries its changes, a killed second launch keeps the first's handle and tokens, `OfTwoLaunches` max / sum / `None` / not-captured) and `ConsultationBillingTests` (a cumulative vendor's second turn reports only its own cost share). **RED first**: the duplicate flag (`found 2`) against the reviewer's `FollowUp` on a resumed argv, and the scenario's "answered nothing … sharper problem statement"; **revert check**: the wiring line replaced by one `LaunchAsync` turned five of six scenarios red. The review fixes (2026-10-02) were each observed red first and reverted again: the follow-up cap (`found 1m`; the hung scenario at 13.6 s), the post-snapshot remainder (`found` two launches), the reply's cost (`found 0.45`), the handle guard (`--x`), the line-break guard, the allowlist (`write_file`, `mcp`). **NOT covered**: a real agy answering through the product path — that is the live acceptance at the end of the plan |
| A failed consultation turn, classified (2026-10-02, epic 2 of `research/PLAN_the_consultant_works_on_every_vendor.md`) | yes | `ConsultFailureScenarioTests` (codex over the fake CLI, the real launcher) and `ConsultAntigravityDenialScenarioTests` (agy, the real streams). Covered: an unknown option (claude 2.1.197's `--restricted` refusal) is `vendor-refused` with "update the CLI"; a vendor failure gives the call back and says so, a tree the consultant changed keeps it; a turn ended by its OWN deadline with the real launcher is `deadline`, not "could not be recorded" (the snapshot now throws a cancellation); the last failure and the last answer land in `consultations/health/<kind>.*.json`; the failed row reaches the database with its kind and cure; orphaned health temporaries (> 1 h) and consultation evidence (> 30 days) are swept, reviewers' evidence is not; agy denied twice is `command-denied`, tells the caller not to retry, gives the call back, and a second consultation then runs on a cap of one; the agy quota line is `quota`; an interrupted turn then resumed — and an answered, an interrupted and an answered turn — are billed ONCE in total. Beneath it: `ConsultFailureTests` (the vocabulary is the cases, the constants and `shared/consult-failure-kinds.json` one for one, by reflection; the give-back column agrees; the classification table; `VendorDiagnosis.For` unchanged; the four builders and the clearing answer), `ConsultHealthAndGiveBackTests` (give-back of exactly its take, refused after a rollover, bounded at three, never across stores, an old three-field counter file; `ConsultHealth.Current`; newer-only health writes; `health/` invisible to the store; bounded evidence; a share carrying every `Usage` field; an old record billed what its turns say) and `ConsultationFailureColumnsTests` (projection round-trip, the upsert clearing, every older query shape, step 17 last). **RED first** for every behaviour (twelve scenario failures with their real symptoms, the cancelled snapshot that threw nothing, the reply cost) and a revert check per fix. The review fixes added: a refunded failure whose record write fails is refunded once and a breach whose write fails gives nothing back (`ATurnThatNeverReturnsTests`); a cancellation neither token raised is `record-failed`, not `deadline`; an exit with a conversation, and a transient failure resting only on the record's older handle, end the consultation; vendor text is redacted in the answer, the record, the health file and the evidence; an unreadable retention directory never stops the sweep ending a stranded record; the counter holds the caller at its cap for an existing file that is empty, torn or unreadable, refuses a refund without a file at once, and leaves one line; Classify reads stderr when there is no transcript, never names a non-exit `exit 0`, and `DoNotRetry` is the review's eight kinds — each observed red first and reverted again. **NOT covered**: a live vendor failing through the product path — the live acceptance at the end of the plan |
| A claude consultation launched with the flags its INSTALLED CLI knows, and the consultant limitations (2026-10-03, epic 3 of `research/PLAN_the_consultant_works_on_every_vendor.md`) | yes | `ConsultOnClaudeScenarioTests` — the whole `consult` flow through the real service and the real launcher on `ConsultScenarioBase`'s harness, the claude CLI's own processes answered by the test with the REAL `--help` of claude 2.1.258 (Windows) and 2.1.197 (WSL), captured 2026-10-03 into `tests/fixtures/claude/`. Covered: a help that declares `--restricted` puts it in the turn's argv and one that does not leaves it out, `--tools Read,Glob,Grep` either way; the CLI is asked before EVERY turn, so an upgrade between two turns of one consultation is launched with the new flags and the resumed turn keeps its `--resume`; what the turn was SENT (`restricted` / `no-restricted`) lands on the record, the turn and the caller kind's health file, and the capability with its reason is logged; a help that never answers (exit 1) is asked twice, then the turn is REFUSED — `vendor-refused` with "will not launch it unconfined", nothing launched, no confinement recorded, a Warning logged; a claude that refuses `--restricted` anyway is `vendor-refused` with "update the CLI"; a record written before the confinement field reads it as empty, not null. Beneath it: `ClaudeCapabilityTests` (the two real helps, and both COLOURED with ANSI escapes on the option names; every long name on a declaration line; a `--restricted <mode>` / `=<mode>` that takes a required value is not the flag we send, an optional `[mode]` is; a mention inside another option's description and prose at an option's indentation are not declarations, an option header whose description wraps below is; `--restricted-mode`; CRLF; empty. The probe's THREE answers: a help that lists it is `Declared` asked once with `--help` alone under ≤ 10 s, one that came back without it is `NotDeclared` asked once, a non-zero exit, a timeout, an empty help and a CLI that cannot be started are `Unknown` after exactly two asks with the reason named, an answer on the second ask is believed; two calls ask twice — no cache; the default is `Unprobed`), `ClaudeConsultantArgvTests` (golden argvs that OWN the whole argv, supported/unsupported × fresh/resumed/model/both — documented as asserting what is SENT, the evidence of effect being `scripts/probe-claude-consultant-confinement.mjs` and `research/RESULTS_claude_consultant_confinement.md` — and `Build` refusing an `Unprobed` or an `Unknown` capability with an `ArgumentException`; the earlier piecemeal argv tests, `ConsultantsTests.ClaudeIsReadOnly_AndCanSTILLReadTheTree` and `ConsultStoryTwoGateTests.AConsultantMayREADTheTree_AndMayNotRunAShellInIt`, were folded into these goldens in the code round), `ConsultantLimitationsTests` (`shared/consultant-limitations.json` parses through the source-generated context and the embedded resource IS that file; every row measured or sourced, neither/both/half a measurement refused; an unknown platform or standing refused naming what is legal; every runtime of `ConsultantResolution.Consulting` has exactly one row per platform — claude one per capability — derived from the code, not retyped; no platform answered with another's row; `HostKind.Other` and an unknown runtime `unmeasured`; a claude without `--restricted` on Windows gets the not-confined row, which is a `source` reasoning from 2.1.258 run without the flag; the confined claude row is Windows-only and says "measured on claude 2.1.258 with its default model"; the lookup takes the qualifier word — codex needs none, an empty or unknown claude capability selects no claude row; the allow-rule snippet only on the Linux/WSL antigravity rows, parsing as JSON, with the not-read-only warning; agy's settings path per side; every cited document on this branch exists), `HostKindTests` (the four facts → the host and its word, `other` included), `ExecutableResolverTests.OnAPosixPath_…` (the first PATH directory holding the command, nothing for none, a null PATH or empty entries) and `AntigravityOnLinuxTests` (an `agy` on the PATH wins over `$HOME/.local/bin/agy`, which is found on Linux and WSL only when the PATH has none, bare `agy` when neither, Windows's own path unchanged and the home bin never asked there, nothing guessed on macOS). `AReviewerStartsNoMcpServersTests` and two `ConsultantsTests` route tests now hand the claude consultant a prepared capability, since `Build` refuses an unprepared one; `ClaudeRuntimeTests` unchanged. **RED first** (epic 3): the goldens against the deny-list argv, the parse and the probe against a stub that always said no, the scenarios against a service that never prepared the launch (`HelpsAsked … found 0`), the limitation tests against a missing resource and then a lookup keyed on the runtime alone (`found HostKind.Windows`, `found Confined`), the evidence tests against a loader that accepted anything, the agy path against the Windows-only original. **RED first** (the code round): against the shipped two-state probe — `Unknown` found `NotDeclared` four ways, the second ask never made, the coloured help, the alias-first line, the value-taking flag and the prose line each read wrongly, `Build` building an unprepared launch, the refusal scenario launching `--tools Read,Glob,Grep` with no `--restricted`, the recorded confinement `""`, the PATH's agy losing to the home bin, `OnPosixPath` finding nothing, the two JSON rows. **Revert checks**: the `PrepareAsync` line in `RunTurnAsync` (three scenarios red), the `--restricted` arm of `Build` (four goldens), the indentation rule of the parse (the prose test), the capability key of `Lookup` (the Windows not-confined test), the platform key (two); and in the code round, each observed red: the exit arm of `Read` mapped back to `NotDeclared` (the Unknown theory and the refusal scenario), the retry removed (six), the ANSI strip, the column-format rule, the required-value rule, the every-long-name rule, `Build`'s refusal of `Unprobed`/`Unknown` (two), the confinement on the `asking` record (two scenarios), the PATH preference (two), the record's `?? ""` (the old-record test — the turn's own accessor-level `?? ""` was NOT load-bearing, the deserializer passes the parameter's declared default, so it was removed). **NOT covered**: `--restricted` on Linux/WSL/macOS (unmeasured, and the rows say so); a live claude through the product path — E5.5's live acceptance |
| The consultant's health, read and checked: `--consultants` and `--check-consultant` (2026-10-03, epic 4 of `research/PLAN_the_consultant_works_on_every_vendor.md`) | yes | `ConsultantModesCliScenarioTests` runs both as the PROCESSES the panel runs, each child with its own data and temp directory and the fake CLI's steering in ITS environment: `--consultants` answers the four caller kinds, probes a definition through its own CLI (the fake's `--version`), reports `authSource` and never "signed in", a claude whose help lacks `--restricted` gets the unconfined row, agy's settings path is there, a legacy reference to nobody is data with its reason and nothing probed, and `consultations/health/consultants.json` is the answer with its `utc`; a data directory that is a file is **74** and an argument is **65**; the stdio server writes the same file in the background and says nothing on stdout; a check under an empty HOME with no git identity still commits, answers with the marker read, deletes its scratch and writes no consultation record; no `--caller` is 65; two checks at once launch exactly ONE turn and the other answers `already-checking`; a holder killed mid-turn is `abandoned` to the next `--consultants` and a new check then runs. `ConsultantCheckTests` (in process, real launcher and git, fake CLI) covers answered + marker read + canary `not-attempted`, a leak (`read`), a claude refusal recorded in `permission_denials` (`denied-by-cli`, confinement `restricted`), a claude whose help never answers (`vendor-refused`, nothing launched), a 2-second budget killing a sleeping child (`timeout`), the counter and `consultations/*.json` untouched, one `consult-check` ledger row, unavailable taking no lock, a held lock answering `already-checking`, and the heartbeat. Beneath them: `ConsultantsReportTests` (snippet on linux/wsl only, by an injected host; the settings path per side; a claude row's limitation by its probed capability; a current versus a superseded failure; an unresolved row), `ConsultCheckStateTests` (`Settled`, the canary's three readings, the budget cap, the held lock across takers and readers, the sweep rewriting only an abandoned state under the lock), `ConsultCheckSweepTests` (`coai-check-` in `neverSwept`, stale scratch removed with read-only files, the temp walk paced), `ClaudeConsultantDeniedActionsTests`, and the limitation loader refusing a snippet without its warning or off linux/wsl. **RED first** where the code did not yet exist (the denials read as an empty list, the missing warning, `coai-check-` absent from `neverSwept`); everything else had its teeth proven by reverting the line that matters — 17 mutations in two batches plus the background write, each red with its own symptom, then restored. **The code round of epic 4 added**, each observed RED first against the shipped code (the stubbed signatures kept the old behaviour until the run): the canary read from any launch's stream, a denial attributed by its input (`denied-by-cli` only when it names the canary, `denied-by-cli-unattributed` for agy — an agy check driven end to end through the fake CLI's denied stream and its follow-up), the promised deadline including the teardown, a lock nobody can open answering 74, a reader that cannot open the lock judging by the heartbeat, a free lock read again, an unreadable state said as such, the sweeper's first walk waiting a pace, a local consultant probed with its model, a shutdown survey not written, a canary the consultant changed being `tree-changed`, a cancellation during the scratch being `cancelled`, a fresh other-side `checking` refusing a second launch, a missing adapter being `unavailable`, and the prompt carrying neither word; the heartbeat ordering (`BeatingWhileAsync`) and the other-side rule with a lock held here proved their teeth by reverting the line. **NOT covered**: a live paid check against a real vendor (epic 5's acceptance), the 9P boundary itself, the sweep's zero-retry write, the snapshot-before-prepare order and the warn on a failed scratch removal (no observable seam; described in the code) |
| The whole-branch review of the consultant plan, server half (2026-10-03, `research/PLAN_the_consultant_works_on_every_vendor.md`, *Whole-branch review — deviations*) | yes | Every behavioural fix observed **RED first** against the unfixed code, then GREEN. `ConsultHealthReachesTheTabTests` (scenario, fake CLI): a failed turn rewrites ITS row of a handcrafted `consultants.json` — `lastFailure`, `failureCurrent` — while the probe's CLI facts, the survey's `utc`, an unknown field and another kind's row stay as they were (RED: `lastFailure` null); a later answer clears `failureCurrent`; no file → none written; a failure's notice subject is `consult:vendor-refused`, not `Applied` (RED: `{"Applied"}`). `ConsultantsSurveyTruthTests` (the survey in process over a launcher that answers versions): the consultant's auth source is never `vault key` (RED with a vault key present: "vault key"; teeth by probing as if a key were sent); a torn `claude.failure.json` / `claude.answer.json` reaches the row as `healthUnreadable` (RED: the field missing); a survey whose probes ran while a failure was recorded writes that failure (`ConsultantsFile.Written` re-reads; teeth by removing the re-read: `failureCurrent` False). `ConsultantsReportTests`: another consultant's failure is not the row's, another consultant's answer does not hide it (both RED). `TheConsultLedgerKeepsEveryUsageFieldTests`: a never-reported usage is `usage not captured`, a check row keeps cached/reasoning/no-price (RED: `""` / 0). `ConsultOnClaudeScenarioTests`: a refusal before the launch on turn 3 leaves the consultation `open` and turn 3 is then accepted; one before the first launch writes no record (both RED: `failed` / a record). `ClaudeConsultantDeniedActionsTests`: an empty claude turn whose envelope refused `Read` is `read-denied` (RED: `Empty`). `ClaudeCapabilityTests`: a truncated help is `Unknown` (RED: `NotDeclared`); a claude that cannot be started is refused `cli-not-found` with the install cure (RED: `VendorRefused`). `ConsultFailureTests`: `command-denied` and `empty` say a NEW consult is the move, never "do not retry"; quota / cli-not-found / vendor-refused / exit still say it; the timeout cure names the Limits tab (RED on the text). `StartupTests`: the `--check-consultant` help names all four readings and 74 (RED). `TheCheckWordsAreSharedTests`: the check states and canary readings are `shared/consult-check-words.json`, by reflection (teeth by deleting a reading from the file). **NOT covered**: the residual windows named in the code — a refresh racing a survey or another kind's refresh inside the milliseconds between the read and the move, and a recovery hidden behind another consultant's later answer |
| A consultation turn that NEVER returns (2026-09-26) | yes | `ATurnThatNeverReturnsTests` — the live incident reproduced on `ConsultScenarioBase`'s harness, over the fake CLI, with ONE seam: a decorator of the real launcher that acts at the moment the record has just been marked `asking`. Covered: the record write failing after the consultant answered is a SENTENCE not a protocol exception, the record stays `asking` while the write is blocked, and the sweep then ends it once its lock is free and its deadline passed; git refusing the SECOND snapshot after the consultant ran leaves a resumable `interrupted` record (its vendor handle read off the stream) rather than one stuck at `asking`, and the next call picks the conversation up; a launch that never ends is ended by the turn's own deadline with the record settled `failed`; the sweep ends an `asking` record this alive server left behind once its deadline has passed; and a pin that the CALLER's own cancellation still surfaces as a cancellation with the record settled. **RED first** — six went red against the unfixed code with the real symptoms (the record write's `UnauthorizedAccessException` up the stdio stack, the record left `asking`, git-refusal leaving `asking`, the 30 s hang, and the pid-alive sweep keeping the record); **revert check** confirmed the same six red with the fix reverted. The record blocker is deny-by-default cross-platform (a held handle on Windows, a read-only directory on POSIX, proven to bite before it is trusted) |
| `consult` FOR something — a group of epics or a risky piece the cadence ordered (2026-09-25, `research/PLAN_consult_on_a_cadence.md` epic 2) | yes | `ConsultKindsScenarioTests`, on the same harness as `ConsultScenarioTests` — extracted into `ConsultScenarioBase` rather than copied, since that file was past the 800-line ceiling — over the fake CLI in the `fakecli-env` collection. Covered: that a `cadence` or `risk` consultation spends none of the stuck call budget while a stuck one still does (a cap of one, three ordered consultations, then the stuck one allowed and the next refused); that a bad aim — no epics, a range that runs backwards, an unknown kind — is refused before anything is recorded (the consultations directory never appears); that the record carries the kind, the plan with forward slashes, the CANONICAL range (`04-06` → `4-6`) and the repository's common dir; that a group already closed with a verdict is not consulted on again, even named through `research/` instead of `todo/`; that an open one is named for a follow-up instead of duplicated; that a lapsed one does not block a new one; that each kind reaches the consultant with ITS prompt (argv recording); that a follow-up keeps the kind it was opened with; and that a record written before kinds existed reads as `stuck`. Beneath it, without a process: `ConsultAimTests` (the canonical path, repo-relative plans only), `CadenceStoreTests` (two REAL worktrees of one repository share an identity; twelve simultaneous closes all land; a held turn makes an update throw; an unreadable record fails closed naming the file; a key delimiter cannot merge two identities), `CadenceGateTests` (only a verdict counts; a promoted plan's consultation still counts; a group consulted weeks ago survives the sweep; an unavailable consultant stands the gate down with the tool's own sentence) and `ConsultPreflightTests` (each preflight reason is byte for byte the refusal `consult` answers). **NOT covered here:** the gate refusing a real code round — that is the next row |
| The consultation cadence REFUSING and ORDERING real rounds (2026-09-25, `research/PLAN_consult_on_a_cadence.md` epic 3) | yes | `CadenceGateScenarioTests` — `FakeCliRoundTests`' throwaway repository with a real plan file committed, real plan and code rounds over the fake CLI, consultations written straight into the store as the evidence the gate reads (the consult flow itself is the row above). Covered: `require` refuses the first code round of an unconsulted group WITH the literal call, and any epic of it, not only its first; a consultation closed with an outcome lets it through; `remind` orders and never refuses; `off` orders nothing and refuses nothing whatever is declared; the plan round forecasts what a plan that names its epics owes; a checkpoint `again` of the same epic is not asked twice while the next group's epic is; an unavailable consultant stands the refusal down and the round says so; a declared count the plan does not have, a plan of fifteen epics, and epics in the plan with no `epic` declared are refused; a continuing plan (epics 5–14) is counted by its own epics; every bad risk answer is refused naming what is wrong, an empty one with a reason stops the question, a risky epic is refused until its own consultation; a passed code round closes the epic and `status` says so, from another branch too; a cadence write that fails does not fail `resolve` and is reconciled by the next call. **Break-it check:** with `BeforeTheCode` made to always let through (by COMPILING code), seven of these went red. Beside it, `CadenceSettingsTests` (defaults, the mode read without case, an unknown mode named, nothing below one taken). **NOT covered:** a real vendor answering a cadence consultation (the consult row's fake CLI and the plan's live check) |
| What the person sees of the consultation cadence (2026-09-25, `research/PLAN_consult_on_a_cadence.md` epic 4) | yes | Extension: `cadenceSettings.test.ts` (every default read out of `CadenceRule.cs` / `PanelSettings.cs`, the modes out of the enum, a pristine panel writes no key, each changed value crosses under its own key, zero / negative / fractional / too-big counts fall back as the server's `IntVar` does, an unknown mode reads `remind` and writes nothing, per side, declared with its defaults), `cadenceLine.test.ts` (the wording in every state, the current group by the first unclosed epic, an unreadable record said, escaping, the parse believed only by shape, which sessions are wanted), `cadenceProbe.test.ts` (an injected clock and spawn: no wait on a render, the TTL, a lapsed consultation picked up by the TTL, `forget`, one probe at a time, no repaint on an unchanged answer, a 65 kept and logged once, a 64 ends probing, a failure keeps the last answer, `null` draws nothing), `activeRounds.test.ts` (the line in the first paint AND the live push), `consultationsLive.test.ts` (the *For* column, `kind` defaulting to `stuck`, the alert spanning every column). Server: `CadenceGateScenarioTests` drives `--cadence` after real rounds (it answers exactly what `status` answers, a consultation closed with no round after it flips the line, an unreadable record says so, off and no-plan answer `null`, no session and malformed requests are 65), `ConsultationProjectionTests` (a cadence consultation reads back with its kind; a database from before the kinds lists `stuck`). **Break-it checks**, all by COMPILING code: eight in the sidebar code, four in the settings, four in the mode, two in the log — each went red with its own symptom. **NOT covered:** the line in a real VS Code window (no harness case yet) |
| The consultant half read from the MOUNT (2026-09-25, `research/PLAN_consult_on_a_cadence.md` story 5.2) | yes | `snippetVersion.test.ts` — the four mounted bodies are byte-identical to the menu's paste, and no local `consultantRule.md` is left beside them; the seventh trigger (the cadence: the order's name, its `kind`/`plan`/`epics`, `close_consult`) survives into the paste. `snippetDiscovery.test.ts` — the REAL mounted rules with no paste are current; a paste carrying consultant v2, or none, is older and wins over the mount; a half is never filled from another mount. `prepareGate.test.mjs` — `consultantBody` refuses a rule without its marker, with and without frontmatter; a clean pinned mount that lacks the rule fails naming the mounted path. **RED first**, each with the real symptom (the old generator named the local file); **break-it** by COMPILING code: siblings read from the wrong mount, and never read — both went red. |
| The feature half read from the MOUNT (2026-10-09, S3.5 of `todo/PLAN_feature_review.md`) | yes | `featureSnippet.test.ts` — the row is v3, names `coai-feature-gate.md`, and its text is the mounted rule's body byte for byte; `featureRule.md` is gone; the verdict section is the server's (one round, three grounds, a different `baseRef` starts fresh) and v2's reopen promise is absent; `readSnippetStatus` over the real mounted files — five files current, the same mount without `coai-feature-gate.md` older naming the feature gate (the case a pin from before conventions #58 meets), the legacy `.claude/rules/shared` mount current. `snippetVersion.test.ts` — all five bodies byte-identical to the paste, no local `consultantRule.md`/`featureRule.md`. `snippetDiscovery.test.ts` — the mount fixtures carry five files. `prepareGate.test.mjs` — `featureBody` strips frontmatter, keeps an `owns:` line after the heading, refuses a file without metadata, marker or heading; a clean pinned mount without the feature rule fails naming the path and the pin. **RED first**: ten failures on the old code with the real symptoms (v2 not v3, the product file, a mount judged on four halves, a missing `featureRule.md`). **Not covered**: the panel shell around `readSnippetStatus` (no host scenario; the call site did not change). |
| The remote reviewer (a Team server vendor) | yes | `RemoteShimScenarioTests` — the REAL `coai-mcp --ask-remote` binary, in its own process, against a real `HttpListener` on a real socket. It is not an MCP tool, so it is not in the registry this table is derived from; it is here because the wiring between an adapter's argv and a separate process is exactly what in-process tests cannot see, and this repository has shipped that break twice |
| The api reviewer (a hosted OpenAI-compatible endpoint, PLAN_feature_review S1.2) | yes | `ApiShimScenarioTests` — the REAL `coai-mcp --ask-api` binary, launched through the real `ProcessLauncher` with the argv AND the environment `ApiRuntime.Build` produced, against a real `HttpListener`: the bearer the child sent is the key the adapter put in `COAI_API_KEY`, the answer lands in the named file, the usage line is read back, a 401 is 77 — and the key is on neither stream and in no argument. In-process: `AskApiModeTests` (the exit-code contract, the 429/503 sentence `RateLimit.Hit` reads off the TEXT, the 401 body never quoted, an echoed `Authorization:` redacted), `ProbeApiModeTests` (the vault read exactly as every mode does, through `KeyVault.ReadFromConfigurationAsync` — the key from the settings file the panel writes, the environment outranking it — `GET /models`, the S0.5 matrix, the output allowlist, a per-call timeout that lets the matrix run after a hung `/models`), `ApiDialectsTests` (the embedded table IS `shared/api-dialects.json`), `LocalRequestBodyIsPinnedTests` (the local body byte for byte, golden captured BEFORE the refactor). Since S3.7, `AnApiReviewerIsPricedAndKeyedTests` drives two more turns through the real binary — a priced row's `costUsd` reaching the ledger, an unpriced row's `costNote: "no price set"` — and holds a round's roster to the key the row NAMES (S3.6); `AskApiModeTests` holds xAI's 400 wrong-key answer to exit 77 (§9.11). NOT covered: a real vendor — the vault was not configured on the build machine, so `--probe-api` has not run against xAI or DashScope |
| The ACP shim for Kimi Code (`coai-mcp --ask-acp`, S4 of `todo/PLAN_vibe_and_kimi_runtimes.md`, D15) | yes | `AskAcpShimScenarioTests` — the REAL `coai-mcp --ask-acp` binary through the real `ProcessLauncher`, the prompt on stdin and the launcher's close read as "complete", the Kimi profile's own `acp` argv reaching the fake CLI's scripted ACP agent (`acp-agent`, `src_mcp/tests_fakecli/AcpAgent.cs`) through `--executable`; the Kimi exclusions refusing `AGENTS.md` through the real binary; a bad request 65, never 64. In-process: `AcpClientTests` (the wire read off the agent's LOG — `initialize` offers read and never write or terminal, the model set before the prompt, `session/new` with the cwd and no MCP server; every permission refused whatever the options and whichever tool asked; a read inside served, outside and an excluded name refused and never quoted; any other client request `-32601`; usage from the last `usage_update`, unknown without one; tool calls traced with their last status; a stray line ignored; an agent error, a stop reason other than `end_turn`, an executable that is not there; and the teardown on EVERY path checked by pid for the agent AND a heartbeat grandchild — the deadline, the agent's own early exit with the pipe held by the grandchild, the caller's cancellation, a normal end), `AcpReadBoundaryTests` (every refused spelling as a row — UNC, device, drive-relative, root-relative, ADS, trailing dot or space — the exclusions at any depth case-folded per platform, `.agents/skills` only as the pair, a real junction on the way, the ceiling, and — the risk consultations' finding F — a link swapped in between the check and the open refused and never quoted, a file grown past the ceiling after its size was checked refused), `KimiAcpProfileTests` (the installer's path on Windows else bare `kimi`, nothing guessed elsewhere; the subscription `401` named before the sign-in sentence it begins with), `AskAcpModeTests` (0/65/69/70/75/76/77, none 64, the stdout object's shape). The 2026-10-12 security review's eight findings each have their red-first test: a duplicate `optionId` ending `cancelled` (theory rows and `ADuplicateOptionId_IsCancelled_NeverSelected_AndTheTraceSaysCancelled`), the 8.3 alias of `.vibe` read through `GetShortPathName` and refused (`AnEightDotThreeSpellingOfAnExcludedName_IsRefused`, skipped where the volume makes no aliases), a 40 000-character read path and a boundary that throws each answered with an error while the turn goes on, a deadline of 2 000 000 000 s refused before any start (client: `NotStarted`; shim: 65), a 16 Mi+1-character line ending the turn as a protocol failure, 1005 tool calls kept as 1000 plus `DroppedTraceEntries` 5 — and, from the code round of 2026-10-12, the trace's SIZE: a read path of a million characters retained as 512 with an ellipsis (red: *Expected read.Path.Length to be less than or equal to 512 … but found 1048646*), a thousand permission requests with 64 Ki-character titles retained under the 1 Mi budget with every request still answered (red: *… to be less than or equal to 1048576, but found 65551000*), a thousand such refusals plus a thousand tool calls with 8 Ki ids and titles dropping entries past the shared budget and counting them (red: *… but found 24608890*; the fixture's first draft put the index at the END of the id, past the 512th character, so the thousand ids bound to one and became one entry's updates — the index now leads), and a normal short trace retained whole with both counts 0 (green before and after) — `session/new` without a `sessionId` sending nothing more, and an answered turn's grandchild gone (`AnAnsweredTurnsGrandchild_IsGoneToo`) with the cancel reaching an agent still listening (`TheCancelReachesAnAgentThatIsStillListening`). NOT covered by a scripted test: the real `kimi acp` — one live smoke through the shim is recorded in the S4 report; `KimiRuntime` above the shim is the next step |
| A Mistral Vibe reviewer row in a round (S2 of `todo/PLAN_vibe_and_kimi_runtimes.md`, 2026-10-13) | yes (Windows) | `AVibeReviewerRunsThroughAFakeVibeTests` — a REAL code round with a `vibe` row and `COAI_CODE_WORKSPACE=worktree`, the fake CLI standing in for `vibe` through the real launcher with the minimal environment: the reviewer starts in the round's sanitised snapshot (its recorded `--workdir` a `snapshot-*` folder), its answer is counted, and the snapshot and the round's scratch are gone when the round ends; the same class drives one launch end to end (the built environment as the child saw it, the export read and deleted, a home the run dirtied refused before the repair). In-process: `VibeLaunchControlTests`, `AVibeRowReviewsInTheRoundsSnapshotTests` and the pure suites of the section *The Vibe reviewer row*. NOT covered: the real `vibe` (the live rounds are owed), any host but Windows (refused, D18) |
| A round that ANSWERED NOTHING | yes | `EndToEndTests.ARoundThatFoundNothing_SaysWhatItSent_AndKeepsWhatItWasTold` — a whole plan round and code round in which every reviewer returns an empty findings array, asserting the two `context for review:` lines, each reviewer's prompt SIZE in the opening line, and the eight raw answers on disk under `empty/`. Not an MCP tool, so it is not in the registry this table is derived from; it is here because the three facts live in three classes (`PanelService` assembles, `RoundAudit` writes, `ReviewerExecutor` keeps) and the 2026-09-08 defect was that nothing joined them |
| A role NOBODY COMPILED IN, reviewing a change | yes | `ACustomRoleReviewsAChangeTests` — a repository, a branch, a plan gate, a code gate and a vendor answering through the fake CLI, with the four shipped code roles switched off so the round is the custom role alone: the round reports one reviewer, the finding carries that role's name, and the prompt the adapter was handed is the text from `<dataDir>/prompts/<id>.md`. Not an MCP tool, so it is not in the registry this table is derived from; it is here because every unit test of this feature would have stayed green with the enum replaced by a string and the round still asking a compiled-in list — the question "does a role a person defined actually review anything" is answered by no class alone. It also carries the plan stage's half — a plan-stage role a person added, asked for by the round rather than by a hardcoded array, read back from the round's own `reviewerStates` because two reviewers reporting the same thing merge into one finding under one role. Its unit-level siblings are `ACustomRoleReachesAReviewerTests` (each resolution path, including the plan stage's dealt lenses and the stale-prompt fallback), `TheRoundSaysWhatItCouldNotAskTests` (the edges of the two guards: a prompt file that exists and says nothing, a shipped role whose prompt somebody edited, a role that fails both guards at once, and one role refused once however many lenses it would have been dealt) and `RoleCompositionTests` (what a row is refused for) |
| A stubbed `fetch`'s response | yes | `responseFixture.ts` — the runtime's own `Response` constructor, not an object asserted into one. `responseFixture.test.ts` guards it: no `.ts` under `src/test` may contain `as Response`, checked against the SOURCE with comments stripped, because the compiled output has already erased the cast and a check reading `out/` passes against any number of offenders |
| The client half of the HTTP contract | yes | `teamServerApi.test.ts` — `ask()` against a stubbed `fetch` that carries real `Headers`: the number is read on the success arm and on a 426, an absent header is `0`, an unreadable one (`''`, `' '`, `0x10`, `1e2`, `v2`) is `undefined`, a body that throws still keeps what the server said, and no response at all records nothing. `teamServerView.test.ts` covers the sentence it produces. Not an MCP tool, so it is not in the registry this table is derived from; it is here because the two halves of the contract live in two languages and the server's half has been correct and unread since it shipped |
| A REAL refusal, carrying a secret, written by the server and read by the extension | **yes, since 2026-09-22** | `npm run test:seam`, the SIXTH leg, in its own module `scripts/seam-refusal.mjs` — handed the runner's session, resolver and failure road rather than carrying copies — story 2.4 of `PLAN_the_server_says_what_it_did.md`, owed since 1.1. The real `coai-mcp` is driven over stdio and asked to `open` a path that is not a directory and carries a `ghp_` token shape, assembled at run time so no token-shaped literal sits in the source; `OpenAsync` refuses with the path QUOTED. Asserted in order: **(1)** the caller's answer CONTAINS the secret — the teeth, because without it every later check would pass on a server never handed anything sensitive; **(2)** the session ends by EOF and the process exits within the deadline, and is killed and reported if not; **(3)** the secret is absent from stderr and from EVERY file the run left under its data root, the log file included, with a companion asserting a log WAS left so the check is not vacuous; **(4)** the notices file is where the extension's `coaiDataDir` + `serverNoticesPath` say, and holds a refused line, on the raw bytes before any parser; **(5)** the extension's own `readServerNotices` returns it with `code`, `class`, `source` and `subject: OpenAsync`, and its title still says what was refused; **(6)** `notificationLine(parseNotificationLine(line))` gives the line back byte for byte. **Proved by breaking the SERVER** and rebuilding, with the changed ASSEMBLY's timestamp checked rather than the build's error count: the title bypassing `SafeText` turns it red with *"the secret reached the disk in clear, in: \\seam\\server-notices.jsonl"*, and the refusal dropping its quote turns it red on *"the refusal did not quote the path it refused"*. **NOT covered, and it was claimed to be:** a missing drain — planted, it stayed green, because one refusal is written long before stdin closes; the drain is 2.3.1's and its own tests hold it. **And one of those plants found a defect in the leg**: a failure before the clean close removed the directory while the child still held its files, `rmSync` threw `EPERM` on Windows, and a stack was printed where the leg's sentence belonged. `session.killAndWait()` now waits for the process, and a removal that still fails is said rather than thrown. The session reads a child's end at `close` rather than `exit`, because Node documents that stdio may still be open at `exit` and claim (3) needs the WHOLE stderr — fixed on that contract, not on an observed failure (0 of 25 runs reproduced it here). **The harness has tests of its own now**, `seamLegs.test.mjs`, for the two defects CodeRabbit found on #465 — each reproduced RED against the real function before it was fixed. A server that dies mid-request must reject the request rather than end the runner: with no listener on the child's STDIN, a write in flight to a dying child was an unhandled `'error'` event (`Error: write EOF` on Windows, EPIPE elsewhere) that killed the whole run before any cleanup. And a malformed line that merely MENTIONS a refusal must not reach the parser: the leg judged the first such line, the parser answers `undefined` for it, and `notificationLine(undefined)` threw past the leg's cleanup. To make the first testable at all, the session moved into `scripts/seam-session.mjs` behind a factory that takes its command — `run-seam.mjs` starts servers when it is imported, so nothing inside it could be loaded by a test. |
| A server KILLED, and the next start recording it — once | **yes, since 2026-09-23** | `ARunThatDiesIsRecordedTests`, against the REAL `coai-mcp` over stdio — epic 3 of `PLAN_the_server_says_what_it_did.md`. Run A starts, writes `runs/<run>.json`, writes a real refusal of its own (`open` on a folder that is no repository), and is killed with its whole tree; the test backdates the marker's heartbeat by an hour rather than waiting thirty minutes, because the window is the planner's rule and `TheRunsAreAccountedForTests` holds it. Run B then records exactly one `unclean-exit` whose `subject`, `run` and `pid` are A's — and whose `run` equals the one A stamped on its OWN refusal — and on its clean exit (stdin closed) leaves no marker behind; run C records nothing more. RED before the host was wired, on *"a serving run writes runs/<run>.json at start"*. **The join was not asserted at first, and a plant found it**: naming the marker with one run id and stamping the notices with another stayed GREEN until the scenario made A write a refusal of its own. The planner, the disk order and the race (two sweepers × 50 deaths, both writing their own marker first) are `TheRunsAreAccountedForTests`, 26 tests — two of them a beat that finishes after the clear and one whose replace was under way when it landed, neither of which may bring the marker back (CodeRabbit, each RED first), one a stop arriving mid-sweep, which records the death being written and leaves the rest for the next start (RED first: three recorded after the stop). **NOT covered**: a death seen from ANOTHER machine on a share, and a real thirty-minute silence. |
| A start answers `initialize` before the vault read and the startup sweeps (2026-10-06, `todo/PLAN_an_idle_server_is_idle_and_starts_at_once.md`, D2) | **yes** | `TheServerAnswersInitializeAtOnceTests` — the REAL server build over real stdio, with the fake CLI copied in as `creds` on the server's PATH and named as the codex executable, both sleeping 20 s: `initialize` must answer inside 10 s, and a `providers` call afterwards answers once the vault read has ended (RED before the fix: no answer in 10 s). `StartingHostTests` cover what the real binary cannot be made to do without a test hook: a FAILED start is its own `ServerStartFailed` (an `IOException` start is not read as a closed connection), a start cancelled by its stop is an ending, the sweeper (`ConsultationSweeper.RunAsync(source, …)`) beats only after a good start; a start cancelled without its stop stays a failure after the ending cancels the stop; and, through the real binary, a client that leaves during the vault read ends the start without a false `starting: … timed out` line, and a start that FAILS (a session file with a null state) ends with exit 70 and `ServerStartFailed` recorded. The failing start through the real binary is covered too, with a session file whose state is null (`AStartThatFails_EndsAsARecordedCrash_NeverAsAClosedConnection`) |
| An idle server's beat reads nothing, and one consultants survey per window (2026-10-06, same plan, D1) | **yes** | `AnIdleServerReadsNothingTests` — `PanelService.SweepEscalations` over a data directory of sessions and cards, counting the session files parsed (`SessionStore.HeldQuestionParses`): ten idle beats parse none, a held card past its seven days costs one parse of each session and is kept, one session that changes is the only one parsed again, a torn session keeps the set incomplete without being parsed every beat, a cached session is parsed again after `SessionHolds.Recheck` even when its stamp did not move, a sessions path that is there and cannot be listed keeps the set incomplete while a missing one holds nothing, a released hold lets its card go. `PanelServiceHostTests` — a reload that fails is tried again on the next call. `EscalationRetentionTests` pin the order (judge first, ask the held set once per sweep, keep every due card when the set is incomplete — RED before the fix: asked 400 times in ten beats). `ConsultantsSurveyClaimTests` — sixteen parallel takes, one winner; a new slot, build, side or settings surveys again; release is fenced; no secret or per-run variable in the identity; `WriteInBackground` twice on one data directory surveys once. `ServerPaceTests` — the two settings' defaults and clamps. Measured, not asserted: the idle CPU itself (`research/RESULTS_idle_cpu_and_slow_start.md`) |
| An exception nothing else caught | **yes, since 2026-09-23** | `ACrashIsWrittenDownTests` — story 3.2. Through the REAL binary: an unusable `COAI_DATA_SIDE` carrying a vendor-key shape throws before the logger exists, and the process exits **70** with the key absent from stderr and stdout empty — RED first on the runtime's own `Unhandled exception.` with the key in clear. In process: the log line is a redacted STRING and `LogEvent.Exception` is null (planting `log.Error(crash, …)` turned it red with the key in the message); the crash notice on disk is redacted and named by its type; a crash that did not land LEAVES the marker (planting an unconditional clear turned it red); the redactor failing falls back to the TYPE; a wedged append and a throwing flush cannot keep a dying process or replace the crash; and a beat that meets an exception `Write` does not name is said and survived rather than ending the loop in silence (RED first: nothing was logged at all). The ORDER of the end — stop, drain, clear over a landed crash, flush — is pinned as a whole by `TheHostDrainsOnEveryRoadOut`. **NOT covered through the real binary**: a crash INSIDE `ServeAsync`, because no input reaches one from outside; `Program.EndedAsync` is driven directly instead. |
| The CONSULTANT's settings and its log, across the seam | yes | `npm run test:seam` (`scripts/run-seam.mjs`), legs two and three. **Two.** The extension's own `serverSettingsJson` writes a consultant map naming a vendor nobody configured, and the REAL binary — one live process, driven over stdio — is asked to consult: the refusal NAMES that vendor, which it can only do if the map crossed. Then `COAI_CONSULT_ENABLED=false` is written under the SAME process and the next call refuses by name, which is what proves the live reload as well as the key, and `tools/list` still carries `consult` because a caller that cannot see a tool cannot be told why it is not there. Watched failing with the writer's consultant key neutralised: it answers about `codex`, the shipped default. **Three.** The binary runs a consultation that ANSWERS, against the stand-in CLI the server's own tests use (no model, no money), and a SECOND process answers `--log` for the extension's own `parseLog` — the consultation comes back with its advice verbatim. Watched failing with the projection neutralised: *"the consultation never reached the log. It holds: []"*. **Four** (story B4, 2026-09-15). The DEFINITION crosses: a consultant defined under an id with NO reviewer row — its own runtime and CLI path in the entry — is asked to consult, and it ANSWERS through the stand-in CLI the entry itself names; the advice text is this leg's own, so the third leg's answer cannot satisfy it. Watched failing against the released `mcp-v0.22.0` binary (`COAI_MCP_DLL` pointed at a throwaway build of the tag — legs one to three pass on it, leg four does not): *"the definition did not cross the seam — the server could not consult through a consultant with no reviewer row. It answered: {"error":"the consultant for a 'other' caller is the vendor 'a-consultant-with-no-reviewer-row', which is not configured — …"}"*, which is the same sentence the B4 measurement recorded for that server. What it does NOT prove: that a real vendor CLI answers (that is `ConsultScenarioTests`' fake-CLI scenario and the live runs recorded in the plan). About a genuinely OLDER binary it says one thing now — leg four fails on 0.22.0 as above — and the rest of that skew is measured rather than tested: `scripts/measure-consultant-skew.mjs` drives an older build with the product's own bytes and records the replies (`module_server.md`, *The wire carries the definition*), and the log-table direction is pinned by `a server too old to have the table answers a log with no consultations` and `ADatabaseWithoutTheTable_ReadsAsNoConsultations` |
| The `consult` PROMPT (`/mcp__coai__consult`) | yes | `McpContractTests` — a real process over real stdio: `prompts/list` names it, `prompts/get` returns the instruction with the person's words in it, the argument is accepted BOTH omitted and empty (the plan round caught a required parameter making the ordinary use a protocol error), and the INITIALIZE response carries the instructions that are the fallback for a client listing no prompts. Its unit-level sibling is `ConsultPromptTextTests` — the words travel unrewritten including their whitespace, nothing at all asks the assistant to state the problem itself, and a build log is cut at the server's own 16 KB bound with a sentence saying so. Not a tool, so it is not in the registry this table is derived from; it is here because a prompt collection is registered in `ServeAsync` and nothing in-process would notice it being added to an options object the transport never sees |
| `ask_consultants` (2026-10-01, `todo/PLAN_question_consultant.md` S2) | yes | `QuestionConsultScenarioTests` — the fake CLI as a codex row, a REAL child in the `fakecli-env` collection: the planner's argv (`exec -s read-only --ephemeral`, no `--search`, no `-C`) reaches a process with the prompt on its stdin, its `-o` file is read back fenced `advisory_only` with D13's `unconfined` flag beside it, the record is `answered` on disk and the ledger line is kind `question`; a web question carrying a path is refused by class before any launch (`none_available`, nothing recorded in the argv directory); `document` and `feature` together refused as `ask_human` refuses them. **The child runs on the MINIMAL environment** (S1 acceptance 6), so the fake CLI sees no `FAKECLI_*` variable — the first run found exactly that (`exit 64: fake-cli: unknown verb [exec …]`) — and now also reads its steering from `fakecli-minimal.json` in the temp directory, which IS on the minimal list; the test writes and deletes that file. Beneath it, over `ScriptedLauncher` (the real launcher for git, a script for the vendor launches — the one shape that can make ONE row of six slow or read one row's prompt apart from its siblings'): `QuestionFanOutTests` — a row past its deadline → `partial`, the row `timed_out`, the others answered (A1); a blocked pair never launched; a disabled row never launched; a web row given the sanitised question alone (no context, no outline, no checkout name) and a refused one naming its class and cure with nothing launched; a `none` api row given the context and the outline of HEAD with the vault key in its environment, a disk row the context and its roots standing in the first; a context carrying a secret refusing the none row by class without quoting it while the web row still runs; six rows started within one tick with no lock directory; the record `consulting` with the pid before any launch and each row persisted as it settles; the invariant taken ONCE per fan-out (two `git status` on one root for two disk rows) and a breach withholding the disk rows' advice alone, nothing deleted. `QuestionConsultServiceTests` — an empty context refused by name, 4 KB / 8 KB counted in UTF-8 bytes, `off`, `none_available` written down, the eleventh question `quota_spent` under the `q:` key the stuck consultant's cap never touches, every answer fenced with its own vendor and model and never merged, the ledger kind. `QuestionConsultStoreTests` — a round trip, null-normalised reads, the id guard, D14 (d) three ways (stale AND dead → interrupted; stale with a live pid kept; dead with a fresh beat kept) and the rule itself, seven-day retention, the 500 cap sparing a consulting record, the answer files on the record's clock and only ours. `QuestionConsultProjectionTests` — step 17 appended last and a database at the step before gaining the two tables on open, one row per question that advances, advice cut at 16 KB in the table and whole in the file, the startup reprojection rebuilding the tables from the files. `QuestionConsultSettingsTests` — every key, the defaults as PanelSettings carries them, D14 (c)'s six refusals by name. `QuestionRowsTests`, `QuestionPromptSetTests` (the embedded catalog IS the shared file; override and restore through `RolePrompts`), `QuestionPromptTests` (A2 as a contract: a web input with a context, outline or root throws). `QuestionOutlineTimingTests` records the outline's cost on THIS checkout (6.2 s, 1,997 files). **RED first**: 52 compile errors (26 distinct, CS0246) across the six new suites against a tree without S2's production code, then green; **break-it checks** by compiling code: D14 (d) with `&&` made `||` turned three store tests red, a web row handed the context turned its test red, rows launched one after another turned the six-rows test red (`found 3s 83ms`). **NOT covered:** a real vendor answering a question row (the plan's live check, S5). The heartbeat REWRITE while rows run was the other gap — every scripted row settled in under a second — and S3 closed it through a seam on the interval (`TheHeartbeatIsRewritten_WhileARowRuns_OnTheConfiguredInterval`, below) |
| A silent antigravity question row, continued once (2026-10-07, `research/PLAN_a_question_row_on_agy_is_continued_once.md`) | yes | `QuestionRowOnAgyScenarioTests` — `AskConsultantsAsync` with one antigravity `question-disk` row on the fake CLI as a REAL child on the minimal environment (the fake CLI now takes agy's `--print=` argv as a vendor shape, so it reads `fakecli-minimal.json`): the first launch prints the real 2026-10-02 denied stream and its stderr, a follow-up — told apart by `AntigravityFollowUps.StaysDenied` on stdin — the real answering stream. Covered: the row `answered` with the follow-up's advice after exactly two launches, the second carrying `--conversation <id>`, and ONE ledger line at the larger cumulative report; a follow-up denied again ends `failed` after two launches (never three) on the command-denied sentence; a watched root changed by the first launch stops the follow-up (one launch) and the reason names the change instead of claiming a follow-up; a caller who gives up while the follow-up runs leaves ONE line with the first launch's tokens (the launcher reports the killed child as a timeout), and no recorded launch is still running when the call returns — read by pid off the record's name (2026-10-07: with the launcher made to stop waiting on cancel WITHOUT the kill, red naming the live pid); and — at `QuestionRowLaunch`'s own level, the fan-out's tree check as the seam — a caller who gives up WHILE the tree is checked leaves one line with the first launch's tokens written `interrupted`, never `ok` (code round, codex). **RED first** against the old launch: the first three failed on today's reason, "the consultant exited cleanly but answered nothing", after one launch; the last one failed on `outcome` "ok". **Break-it checks**: the fan-out's `ChangesSoFar` left out → the tree test red on the second `--conversation` launch; a ledger line per launch → two tests red |
| The fake CLI's launch record (`FAKECLI_RECORD_DIR`) is whole or absent — test infrastructure every recorded scenario reads (2026-10-07) | yes | `ALaunchRecordIsWholeOrAbsentTests` — a REAL fake CLI child held by `FAKECLI_RECORD_GATE` with its record written and still open: listing and reading `*.argv` then neither throws nor finds it, and once released the record is there whole (the argv, stdin last). The recorder writes `<pid>-<guid>.recording`, closes it, then renames it to `.argv`. Why: tag `mcp-v0.44.1` lost its `coai-mcp (win-x64)` job twice to `QuestionRowOnAgyScenarioTests`' cut-short test, which polls the records while a launch runs — `File.WriteAllText` (write, readers allowed) against `File.ReadAllText` (readers only) is a sharing violation on Windows. `ConsultantModesCliScenarioTests`' `TurnsLaunched` polls the same way and is covered by the same fix. The other half is the READER: with the recorder fixed, 1 of 40 loaded runs still failed on a record that was already renamed (the rename's own handle, or a scanner, still held it), so every record read goes through `LaunchRecords.Read`, which lets every other holder in (`FileShare.ReadWrite | FileShare.Delete`) — a whole record that another handle holds is read anyway. A scan over the suite's statements refuses a record read that does not go through it, with its companion that the scan still finds a known one; its first run found two sites the hand sweep had missed. **RED first**: with the old open mode and the gate, 5 of 5 runs failed on `IOException … being used by another process` — observed while the test still read with `File.ReadAllText`, before the reader half existed; against the committed test (records read through `LaunchRecords.Read`) the old writer fails on the other half, `Expected reading to be empty … but found at least one item {{"exec", "-", "the prompt…`. The reader test failed with the same IOException against `File.ReadAllText`. **NOT covered**: a `.recording` file a killed child leaves behind is not counted as a launch, by design; the scan cannot follow a record path through a variable into a later statement (two such reads were routed by hand) |
| An antigravity consultant searches through coai — question rows and the stuck consultant (2026-10-08, `research/PLAN_agy_searches_through_coai.md`) | yes | `QuestionRowLookupScenarioTests` (an agy `question-disk` row on the fake CLI as a REAL child; turns told apart by `AntigravityFollowUps.LookupTurnMarker` on stdin) — a block is served by coai and the SAME conversation continued (`--conversation <id>`), the row answering with the second turn's prose, billed once at the latest cumulative report; a lookup outside the root is refused by name in the next turn and nothing outside is read; a last turn still asking ends on its prose with the capped note naming what was not served; a last turn with nothing but a block fails saying the lookups were capped; and from code round 2, each observed red first: a continuation whose stream names no conversation keeps the one turn 1 named, so a second lookup is served (red: two launches, not three); a watched root the first turn changed stops the row before its continuation, naming the file (red: a second launch). `ConsultLookupScenarioTests` (on `ConsultScenarioBase`) — inside ONE `consult` call the lookup continues the same conversation, coai's search reads the WORKING tree (an uncommitted comment), the record keeps ONE turn and the ledger ONE line at 2500 tokens (the larger cumulative report, not the sum); a consultant that keeps asking is capped after 4 launches and the advice is its prose plus "lookups capped at 3 turns", never the block; a tree the first launch changed stops the continuation (one launch) and the refusal names the file. Beneath it: `WorkspaceLookupTests` (one-level listing with folders marked; `.git`, dependency folders, credential names and binaries never listed or searched; a search is a literal, case-insensitive; redaction; `..`, an absolute path in no root, and a link inside the root leading out refused by name with nothing outside read; an unreadable folder skipped and SAID; several roots need an absolute path; every cap and the turn budget said; a missing folder said; and from the code round, each observed red first: a path with a NUL refused by name while the next request is served — it was an `ArgumentException`; a lock file named directly not searched — it was served; a block past its time limit serving the first request and naming the rest; a turn budget that cannot hold a result resolving nothing more — a counting link-follower saw 29 resolutions before the fix; a folder with more entries than a walk reads stops the walk and names it), `LookupRequestsTests` (the block, its draft prose, the line cap, refused lines named, two blocks under one cap, an unclosed block still a request, an empty block, a fence glued to the prose as the real model wrote it) and `AntigravityStreamContinueTests` (a denied command with a reader attached keeps the measured text and adds the block; without one it does not). **RED first**: each scenario against the code before its story (the consult advice was the block itself); **break-it checks**: the consult loop's tree check disabled → the changed-tree test red on a second launch; `Settle` reading only the final answer → the cap test red; the cumulative share replaced by the sum → the row billing test red; the containment check removed → the containment tests red. **NOT covered**: the real model writing a block — that is [RESULTS_agy_searches_through_coai.md](RESULTS_agy_searches_through_coai.md) |
| An agy launch cannot write inside its roots (2026-10-09, `research/PLAN_agy_cannot_write_its_roots.md`) | decisions, the argv of every builder, and the hook handler as a real child process | `AntigravityReadOnlyTests` — the hook's decision: `view_file` allowed, every other tool name (and a different case, an empty name, an unreadable or empty payload) denied, and a `write_to_file` whose ARGUMENTS contain the allowed name still denied; the hook mode reads stdin and writes the decision; `--agy-hook` classifies to `Startup.AgyHook`; the BUILT `coai-mcp.dll` started as a child with `--agy-hook` answers deny / allow; the prepared folder holds the `coai-reader` agent with `tools: [view_file]`, the hook (`matcher: "*"`, the per-OS script command) and both scripts naming the handler; preparing again is the same folder and rewrites a tampered agent; a folder that cannot be written is `Failed`, never a folder without its block; a handler path with a quote, line break, `$` or `%` is `Failed`; two handlers get two folders; EVERY builder — reviewer, consult, its continuation, a resumed consult, a question row on `disk` and on `none` — carries `--agent coai-reader` and runs from the prepared folder; a disk row still reaches its root through `--add-dir`; an agy reviewer given a checkout is told its path; a consult whose folder cannot be prepared is REFUSED before the launch. Beside it: `ConfinementPlannerTests` (agy's argv changed on purpose, pinned again byte for byte), `ConsultantPromptTests` (the checkout named by its path), `ConsultantCheckTests` (the agy check reads `CHECK.md` through its `--add-dir` — the fake CLI's new `{{adddir-file:NAME}}`). **RED first**: the builders' test against the old argv (no `--agent`), the reviewer's path note, the agy consultant check (`markerRead` false once agy no longer ran from the repository); **teeth checks**, each fix broken and its test seen red: the prompt without the path (the checkout test), the refusal turned off (`Ready` instead of `Refused`), the hook allowing every tool (`allow` for `write_to_file`). **NOT covered**: that agy honours the agent and the hook — CI has no agy; that is the live probe, `scripts/probe-agy-write-block.mjs` ([RESULTS_agy_write_block.md](RESULTS_agy_write_block.md)), re-run on every agy version bump |
| A question-consultant root of the other operating system is skipped, not refused (2026-10-09, operator: a WSL root `/home/jinx/git` in the shared VS Code settings was a failure toast on every start of the Windows side) | decisions, the reader over a real environment, admission, the startup log, and the Settings page RUN | `QuestionConsultSettingsTests` — a WSL root (or, on Linux, a Windows one) next to this machine's root and the other keys: no complaint, `OtherSideRoots` holds it, this machine's root, the mode and the row still apply, and `PanelSettings.Unrecognised` is empty; both directions on one machine through `SystemPlaces.Windows` (`/home/…`, `/mnt/c/…` on Windows; `C:\…`, `d:/…`, `\\wsl.localhost\…` elsewhere); a MISSING root of this OS still refused by name; `QuestionRoots.OtherSide` against `shared/path-family-vectors.json`; on Windows an EXISTING `/work` accepted as this side's and only the missing `/home/jinx/git` skipped (disk injected), and `OtherSideHere` against the file's `existence` vectors (plan round, same day: RED `/work … but found True`, `the collection is empty`; break-it: existence ignored in `Validate`, then in `OtherSideHere` — each red). `QuestionAdmissionTests` — a disk row with only other-side roots is `disabled` with "inactive on this side … add a folder of this machine"; with no root at all it is still `blocked` asking for one; a non-disk row is admitted whatever the roots. `TheStartupNotesAreWrittenDownTests` — an other-side root is one Information line and NO notice. Extension: `pathFamily.test.ts` (the same vectors, the `existence` vectors, and `existingHere` asking the disk about the other-OS spellings alone, a failing stat not claimed as this side's), `qconsultSection.test.ts` (the page run with Windows places: the WSL root's line says *the other side's folder* and carries no `stale` hint, this machine's root beside it plain; with Linux places and only a Windows root, the block says a disk row is not asked on this side; `/work` in `existingHere` is never called the other side's). Code round, same day: the root-relative root is judged and KEPT on the system drive (`Q:` injected, so a current-drive answer cannot pass: RED `{"Q:\work", "Q:\tools"} … {"D:\work", "D:\tools"}`), `Qualified`/`qualified` against the `resolution` vectors, and `RootExistence` (two renders stat once, a changed list, drive or forget stats again, one stat at a time); break-it: the kept root unqualified, `Qualified` disabled, the existence asked unqualified, the cache key ignored — each red. Cadence consultant, same day: the page and Add a folder judge the qualified path (`qconsultSection.test.ts` runs the page with `/Windows`, `/Users/me` and the data folder: RED `the system folder spelled from the root of the drive is not refused on the page`; `qconsultWrite.test.ts`: refusals and the `/work` = `C:\work` duplicate); the compatibility change pinned (`/work` on the system drive, never the server's current drive — break-it RED `{"D:\work"}` for `{"C:\work"}`); a missing `/home/…` skipped while an explicit `\\wsl.localhost\…` share is judged, kept, or refused by name. **The live check across the seam** (second code round): `scripts/seam-qconsult-roots.mjs`, a leg of `npm run test:seam` — the extension's own writer puts one root of THIS OS (a temp folder, written root-relative on Windows when it is on the system drive) and one root spelled for the OTHER OS (chosen by `process.platform`, absent here) into the settings file; the REAL coai-mcp is asked through what it already exposes, no new flag: `--providers`' `unrecognised`, read with the extension's own `parseProviderNotes` (the other root named in neither spelling; the control — a missing root of this OS — must be named), and `ask_consultants` over a live MCP session, one root at a time with one `question-disk` row on the fake CLI (skipped → the row `disabled`, "inactive on this side"; kept → launched, its note naming the kept path, qualified). The page's `otherSideHere` over the same roots and the same real `stat` must agree with the server on every root. Break-it: the server judging the other root instead of skipping it → the leg red (`the other side's root reached the "server did not understand a setting" list: ["COAI_QCONSULT_ROOTS: 'C:\home\coai-seam-nobody\git' is not a directory …"]` — the leg's first version, matching the written spelling only, stayed GREEN against that break, and now matches both spellings); the page never calling a root the other side's → red (`the page and the server disagree about /home/coai-seam-nobody/git`). Also (same round): a `stat` that fails with anything but ENOENT/ENOTDIR is UNKNOWN (`directoryAt`, `Existence.unknown`, `RootPlaces.unknownHere`) — never called the other side's, said on the page, never cached (`pathFamily.test.ts`, `qconsultSection.test.ts`); each other-side root is probed ONCE (`EachRootOfTheOtherSidesSpelling_IsProbedOnce`: RED `found 4`). Third code round: the server's probe is tri-state too — `OnWindows_ARootTheDiskCannotAnswerFor_IsNotTheOtherSides_ItIsJudgedHereAndRefusedByName` (injected `Unknown`; RED `Expected verdict.OtherSide to be empty … but found {"/work"}`), `ThePresenceProbe_TellsAFolderAFileAndNothingApart` (a real folder, file, missing path and a path under a file), the `existence` vectors' `unknownHere` rows read by both halves; every vector row is read CHECKED (`SharedVectors` in C#, `rowOf` in `pathFamily.test.ts` — a `null` row fails naming `existence[0]`, never a TypeError). `PresenceOf`'s mapping is tested over an injected attribute reader (`PresenceOf(full, attributesOf)`; production passes `File.GetAttributes`) that THROWS the real types — `UnauthorizedAccessException` and a sharing-violation `IOException` → Unknown, `FileNotFoundException` / `DirectoryNotFoundException` → Absent, a file's attributes → Absent, a directory's → Present (`ThePresenceProbe_MapsWhatTheDiskReaderSaid_ThrowingTheRealExceptionTypes`; RED against a stub answering Absent; break-it: access-denied/IO mapped to Absent → both Unknown cases red). Fourth code round: an UNSPELLABLE root is Absent on both REAL probes (`AnUnspellableRoot_IsAbsent_OnTheRealProbe_AsTheExtensionsProbeAnswersIt`, `pathFamily.test.ts` — the `unspellable` existence vector plus a `<` name; RED: C# `a<b` … `found RootPresence.Unknown`, TS `ERR_INVALID_ARG_VALUE` thrown as unknown); the page's answers live `ROOT_ANSWER_LIFETIME_MS` (injected clock: one stat within, a fresh stat after; RED: `[]` where `['/work']` was expected). The forget HOOKS (`extension.ts`: settings change, settled `--features`, `coai.installServer`) are vscode wiring — read, not run; `RootExistence.forget` itself is tested. **RED first**, the API stubbed so the tests compiled and failed for the real reason: `COAI_QCONSULT_ROOTS: '/home/jinx/git' is not a directory on this machine - a disk row needs a folder that exists` (the operator's sentence), status `blocked` where `disabled` was expected, the vectors' `/home/jinx/git` answered `False`; the page drew `'C:\Users\jinx\git Remove'` with no note. **Break-it**, each line restored afterwards: the skip filter removed from `Validate` → the settings tests red with the same sentence; `InactiveHere` forced false → the admission test red (`"blocked"`); the log call removed → the startup test red (`the collection is empty`); the page's other-side branch and the all-other-side hint each disabled → their page tests red. **NOT covered**: a real WSL + Windows pair of windows sharing one `settings.json` — the decision is lexical and both directions run on one machine |
| Every path setting, one rule — a path spelled for the other OS is skipped, not refused (2026-10-10, `research/PLAN_paths_per_side.md` E1) | decisions, the registry census, the writers and launchers, the server's vendor read and its log, the Settings page RUN, and the seam | `pathSettings.test.ts` — the census over `contributes.configuration` (RED with only the roots registered: `path fields with no entry in PATH_SETTINGS: coai.alsoWatchDataDirectories, coai.consultants.*.executablePath, coai.dataDirectory, coai.vendors[].executablePath`; break-it: the default-row walk removed → the companion `the census no longer finds coai.vendors[].executablePath`). `pathFamily.test.ts` + `ACliPathOfTheOtherSideIsSkippedTests` — the `executable` vectors, read checked by both halves (RED `windows: /usr/local/bin/codex … actual /usr/local/bin/codex, expected ""`). `aCliPathOfTheOtherSideIsSkipped.test.ts` — the COAI_VENDORS row, `executableFor` / `executableForRuntime`, the Claude probe (row and consultant), a chat preset, and the scan of raw `executablePath` reads (`UNRESOLVED_READS`, with its companion); break-it: the wire line reverted → the wire test and the scan red. C#: `ParseVendors` through `FromEnvironment` (RED `Expected provider.ExecutablePath to be empty … but found /usr/local/bin/codex`) and the Information line with no notice (break-it: the log call removed → `the collection is empty`). `escalationDirs.test.ts` + `setupOnTheNewPage.test.ts` (Setup › MCP server RUN: the WSL folder drawn `hint watched-other-side`, nothing `stale`; RED with the old semantics: `actual [stale]`). `theDirectoryAPersonChose.test.ts` — the shared `Z:\coai` in WSL and `/mnt/z/coai` on Windows fall to the default (RED `actual [shared setting, Z:\coai, ""]`), `whereData` names it (break-it: `no note names the skipped value: []`). `aCliPathOfTheOtherSideOnTheCard.test.ts` — the Models card RUN on a Windows and a WSL page: the note in the hint tone, no `cannot-run`, no `stale` (red with the note off, and drawn `stale`). `configTransfer.test.ts` — `qconsultRoots` never exported, an older export's roots refused by name (RED: `the roots were exported`). **The live check across the seam**: `scripts/seam-cli-path-other-side.mjs`, a leg of `npm run test:seam` — a row `seam-cli` with a CLI path of the other OS and `COAI_EXE_SEAM_CLI` = the fake CLI: the extension writes `executablePath: ""`; the REAL coai-mcp, given an OLDER extension's file carrying the raw path, probes the fake CLI (`cliFound`, its version) instead of the path; the control — a missing path of THIS OS — is probed as written and not found. Break-it: the server's skip reverted → red (`cliFound:false … was not found on this machine`). The gate's cadence consultant (before E1's code round): a CLI path of the other OS's spelling that EXISTS here is this side's — the `executable` vectors' `existsHere` / `unknownHere` rows and the consultant's case in `pathFamily.test.ts` and `ACliPathOfTheOtherSideIsSkippedTests` (RED: `actual ''`, expected the qualified `C:\Program Files\nodejs\node.exe`), the wire test (`skipped on a guess` when an unknown path is skipped), `fileAt` / `FilePresenceOf` on the real disk; the seam leg's case (d) on a Windows host — a copy of the fake CLI on the system drive spelled without its drive letter, the row's fallback pointed at a missing file — must answer with the fake's version (break-it: the server rule made lexical → `cliFound:false … not-the-fallback`); on another host the case says it did not run, and why. **NOT covered**: a real WSL + Windows pair of windows; the per-side second field (E3/E4) |
| A missing path of this OS: a notice at every start and a bright mark (2026-10-10, `research/PLAN_paths_per_side.md` E5) | decisions, the bounded probes, the storage-write policy and the writers' inventory, the server's answer and its contract, the notice and its dedupe, the Settings page RUN, the tab count, the mirror's gate, the seam and the host | `pathFindings.test.ts` + `pathProbes.test.ts` — the mapping (RED `actual: []` where `[{ … value: 'D:\old-project', state: 'missing' }]` was expected; `actual: undefined, expected: 'unknown'`), a never-settling share beside a missing local folder — one outstanding probe per path however many renders and expiries pass (break-it: every read starting a probe → `the share was asked more than once while its first probe was still running`). `findingsBoard.test.ts`, `everyWriterOfTheDataDirectory.test.ts`, `notificationsFile.test.ts`, `atomicFile.test.ts`, `dataDirFinding.test.ts` — the policy (RED `the notice's own write created the folder it reports missing — and hid the condition`, `the chat ledger created the missing root`, `a named Z:\coai that does not exist yields no finding today`, `a retyped path is one held notice per setting key, never one per retype`; the inventory derived from every `mkdir` / `mkdirSync` / `createDirectory` site in the source, each guarded or with a stated reason; break-it: the board never refusing → 4 red, the ledger's guard removed → the ledger and the inventory red). The host's second launch then showed the chat store's heartbeat creating the root — written at activation beside the probe, through a guard that read the board before the disk had answered: `atomicFile.test.ts` (RED `the write that started before the disk answered created the missing root — the guard read the board before the probe had answered`; break-it: the waiting guard made not to wait → red), the inventory's rule that every async writer awaits `refuseUnderMissingRootOnceSettled` (RED `atomicFile.ts asks the board without waiting for the first data-root answer`), and `consultantsProbe.test.ts` — the survey, a writer by proxy, is not spawned while the root is missing (RED `the survey was spawned while the data folder it writes into is missing — the server creates it`; break-it: the check bypassed → red). C# `TheServerSaysWhichSettingAndValueTests` (RED `Expected complaint.Kind to be "missing-folder" … but "" has a length of 0`) and `providersAnswer.contract.ts` under `npm run test:contract` — the REAL binary over a settings file the extension's writer produced with one existing and one missing root, the four-field shape read back through the parser (its first fixture put the roots INSIDE the data directory and the server refused them by place — a fixture the code rejects; the roots moved beside it, with a control on `unrecognised`). `pathWatch.test.ts` + `theGenericToastDropsWhatTheProblemsCover.test.ts` + `heldNoticesOnThePage.test.ts` — said once when a path APPEARS missing and resolved when it returns, `withoutCovered`, `agreed`, the held notice on the page (RED `today the operator's case produces the generic … toast and no notice of its own`, `the operator's folder would be said twice`, `the server's answer did not replace the window's own for the roots`); the census at 164 with the new door (RED `no call site raises setting-path-missing; the census knows 153 codes`); the storm the first host run showed — a said path resolved on the pending gap after every settings change and said again when the answer landed (RED `three settings changes re-said the still-missing paths: said 8 times, resolved 6`; `an answer the disk could not give resolved or re-said the notice`; break-it: every answer settling → the same 2 red). `theMissingPathIsMarked.test.ts` — the page RUN through `runPanel`: the root line, the CLI-path box and the watched-folder line carry `path-missing`, the word *missing* and `aria-invalid="true"`, nothing with no finding (RED `drawn as field qconsult-root — today nothing beside the folder says it is gone`; break-it: drawn with `.stale` → 4 red ON THE CLASS, never on the text). `theTabCountsItsMissingPaths.test.ts` — the tree through `pageTree`: `● N` after the label on Consultants and on Question consultant, none at zero (RED `the tab consultants carries no count`; break-it: drawn before the label → `the label does not come first on consultants`). `theMirrorWaitsForTheDataRoot.test.ts` — `no-data-root` writes nothing and enters no critical section, `MirrorSchedule` treats it as terminal (RED `activation with a missing named root creates it today: the write that hides the condition`; break-it: the gate neutralised → red), and `whereData` keeps its quiet note only inside an existing root (RED `today a missing named root is a quiet note beside the bright mark`). Every break-it COMPILES: four first breaks that did not (an unused import, an unused parameter, an unused field, a narrowed `&& false`) left stale output and a false pass, and were replaced. **Across the seam**: `scripts/seam-path-missing.mjs`, a leg of `npm run test:seam` — the REAL coai-mcp over a settings file with one existing and one deleted root, its `settingProblems` against the page's own probes as the same `(key, value, kind)` set, and the covered sentence dropped from the generic toast (break-it: `withoutCovered` keeping every sentence → red; the server's kind emptied → red). **In a real editor** (`test:host`, `missingDataRootScenario.ts`, `missingPathSaidOnceScenario.ts`): the ordinary launch's ledger holds no `setting-path-missing` after a first start with an existing named directory (the FIRST scenario, before any other names a path that is not there — the revision scenario's `D:\old\claude.exe` is rightly said, and a read after it blamed the first start); a CLI path of this side that is not there, set in the real settings, is said once however many settings changes follow (the last scenario — red in the host with the settled rule broken: `a path still missing was said 4 times after 3 settings changes`); and a SECOND launch (`run-host.mjs` runs BOTH launches and throws their failures together, so a clipboard scenario failing in the first — which happens on some machines — no longer hides the second's answer) whose `COAI_DATA_DIR` names a folder that is not there finds it still absent after activation, after the notice has settled and after a settings change — and `settings.json` landing within seconds once the folder is made. The three inventories the targeted runs had not covered went red on the full `npm test` and were brought up to date: `UNRESOLVED_READS` (panelView's finding by the stored value, pathFindings' words, pathWatch's subject), the funnel's four netted doors (`notifyOnceThen`), `sonar.coverage.exclusions` (`pathWatchHost.ts`). **The gate's risk consultation on 5.7 (2026-10-10)**, each claim verified first: `aRefreshKeepsTheMissingRootRefused.test.ts` — a settings change (`forget`) with the replacement probe held, or timed out, keeps the guard refusing (RED `a settings change dropped the disk's answer, the guard read "no finding", and a writer created the missing named root`, `a timed-out probe made the mirror believe the named root was there`, `an unknown answer lifted the refusal`; break-it: `presence !== 'absent'` lifts the kept absence → the same red); `aWriterNeverRecreatesTheNamedRoot.test.ts` — the root removed after the board last saw it is not made again by the ledger, the atomic write or its sync form, nor through `<root>/<side>`, and provenance decides (all layers unset → the default is made; an explicit value equal to the default path → refused; a side folder in an existing root → made) (RED `a writer's recursive mkdir made a named root the board had no finding against — the race between the guard and the write`, `a root a person NAMED was created because its spelling matched the default`); `aFolderMadeAgainIsSeenWithoutASettingsEdit.test.ts` — the real disk, the folder made again and nothing else: the recheck confirms it, the notice resolves, the held notice goes, `onDataRootBack` fires, the ledger writes (break-its: `recheck` a no-op → `the folder exists and the guard still refuses it`; the back listener not fired → `the settings mirror, which stood aside, was never told the root is back`); `everyWriterLeavesAMissingRootAbsent.test.ts` RUNS each writer the inventory (`dataDirWriters.ts`, now counting the maker and VS Code's parent-making writes) classifies as writing under the data directory, the board knowing nothing — every run red with the maker broken; `theChoiceMakesNoFolder.test.ts` pins that the choice flow holds no folder-making call and the move holds them all (break-it: a `createDirectory` in `saveChoice` → red); `everyServerModeTheExtensionLaunches.test.ts` classifies every `coai-mcp` mode the extension launches and holds each reader to C# `AReaderModeNeverMakesTheDataDirectoryTests` (RED, six modes: `` `coai-mcp --bugs-json` exited 0 and left the missing data directory behind, holding: coai.db, coai.db-shm, coai.db-wal ``, `--providers left the missing data directory behind, holding: schemas, …`; break-it: `IsThere` always true with `OpenExisting` creating, and the schema eager again → the same six red). `bugzLiveContract.test.ts` seeds its database through a writer mode (`--requeue-refused`) now that `--bugs-json` makes none. In the host, the second launch's recovery makes the folder and changes NO setting. **NOT covered**: the toast itself and its once-per-start in a real editor (a held notice is written to no ledger, and the bundle's module state is out of a scenario's reach — the once-per-run and the button are unit values); a real WSL + Windows pair of windows |
| The sides this profile has been used on — stamped, listed, forgotten, and the path-fields switch (2026-10-10, `research/PLAN_paths_per_side.md` E2) | decisions, the store, the help in five languages, the Settings page RUN, and a real editor | `sidesSeen.test.ts` — the registry as values: both families known within 90 days (RED with a stubbed module: `actual: [], expected: [posix, windows]`), a 91-day-old side pruned and one exactly 90 days old kept (break-it: the window widened by two days → `only the 91-day-old record is pruned` red), at most one stamp a day keeping `firstSeen` (break-it: the guard removed → `stamped twice within one day`), every non-record value under the prefix stamped afresh and pruned, keys outside the registry untouched, newest first; a time no Date can hold (1e300) or more than a day ahead is no record — not listed, pruned, stamped over, and the page still opens (the code round's finding; RED `lastSeen 1e+300 was listed` and the page's `RangeError: Invalid time value`; break-it: the plausibility check made always-true → both red); `twoPathFields` / `pathFieldsModeOf` (the switch, this window counting as seen). `sidesSeenStore.test.ts` — the store half against a map behind the three `Memento` members: activation's stamp is ONE write and a second start the same day writes nothing (RED: `actual 0, expected 1`; break-it: the write guarded by `now < 0` → four red), pruning, Forget refusing this window's own, `sideHereOf` naming a local Windows window **Windows**. A store whose `update` rejects: the stamp does not reject, the refusal reaches the reporter (activation logs it with `console.error`), and the next stamp on a working store records the side (RED: `Got unwanted rejection: a refused stamp rejected`; break-it: the reporter not called → `the refusal was swallowed, not reported`). `configTransfer.test.ts` — `pathFieldsPerSide` never exported (RED: `the path-fields switch was exported`). `helpCoverage.test.ts` — the setting's alias, and every one of the five languages naming **Path fields per side**, **Forget**, the three choices and the 90 days (RED `the en settings article never says **Path fields per side**`; break-it: the Russian label un-bolded → red). `sidesSeenOnThisSide.test.ts` — Setup › This side RUN on the new page: two seeded records draw two rows (name, family, local day), this window first and marked, Forget only on the other row and posting its key (RED: `actual: []`; break-it: `data-id` from the label → red; Forget drawn on this window too → red), a local window named **Windows** and never "this machine" (RED: `the side is not named`), the switch posting a plain `pathFieldsPerSide` write, the sentence for each signal, and a newly stamped side appearing at the next render. **In a real editor**: `src/test/host/sideSeenScenario.ts` (`npm run test:host`) — the shipped extension activated, its own `globalState` read back through `CoaiExports.sidesSeen`: a record for this window's side, with its family, its name and a stamp no older than a day (RED with the stamp unwired: `activation left no coai.sideSeen@ record for this window's side (Windows); the registry holds []`). **NOT covered**: two real windows on two sides (measured by hand once, E0 — it cannot run in CI); the editor's user-data directory outlives a run, so a same-day stamp from an earlier run also satisfies the scenario; the second path field itself (E3/E4) |
| Two values, stored and resolved — a CLI path per side, one write rule, three folder groups, the other side's data directory, and compatibility with older halves (2026-10-11, `research/PLAN_paths_per_side.md` E3) | decisions, the help in five languages, and the RELEASED extension and servers (by hand) | `cliPathsPerSide.test.ts` (E3.1) — a present per-side value wins on its side and is what the wire, the terminal, the Claude probe, the chat launches and a copy into a definition run; a present EMPTY one is the PATH lookup; the reader keeps the member only when said; the wire carries one string per row. Teeth (2026-10-11): the wire reading the legacy value alone → `the WSL side runs its own per-side value`, `cleared on Windows: PATH` red. `cliPathSaveResolve.test.ts` (E3.2) — save → resolve across modes (two fields → one → save B resolves B; one → two; Forget / 90 days; clearing over a legacy `//server/share/codex`), an older save detected through `mirrored`, and every save leaving every other family's resolution unmoved. Teeth: a save that stops mirroring → `an older reader on this side would be left with the stale value` (three red). `pathGroups.test.ts` (E3.3) — `/work` existing on Windows is this side's, `/home/jinx/git` the other side's, `//nas/share/x` and `work` both / unclear on both platforms, an `unknown` stat unclear. Teeth: the spelled-for-neither guard disabled → `//nas/share/x`, `work` land in `thisSide`. `otherSideValues.test.ts` (E3.4) — a Windows window reads the WSL side's `/mnt/z/coai` from that side's overlay; every side of the other family listed newest first, one that chose nothing listed empty; a side past 90 days dropped, a non-string or `null` overlay read as no choice; the store handed in refuses writes. Teeth: filtering by side key instead of family → `Debian is the same family as Ubuntu`; reading this side's overlay → `Z:\coai` for the WSL side; the null guard dropped → `TypeError: Cannot read properties of null (reading 'dataDirectory')`. `pathsPerSideCompat.test.ts` (E3.5) — every `executable` vector as a legacy-only stored ROW goes on the wire as the vector says and gains no member; a two-field save mirrors W, which an older reader sends and a POSIX side skips; an older save that drops the member resolves for its own family and leaves the other the PATH lookup; a legacy edit that keeps the member is detected through `mirrored`. Teeth: the reader seeding the legacy value into the host family's member → `windows: "/usr/local/bin/codex" … actual '/usr/local/bin/codex', expected ''`; no mirror → legacy `''` instead of W; the older-save check ignoring the spelling → `and the Windows reader keeps its own value` red. **With E5's watch** (merged 2026-10-11): `pathWatch.test.ts` — a row whose WSL side saved last is watched by THIS side's member value (RED before the merge fix: `actual: []`; break-it: the value taken from the legacy field alone → `actual: []`); `theMissingPathIsMarked.test.ts` — the card's box draws this side's value and the finding about it marks it (break-it: keyed on `v.executablePath` → `the finding about this side's value did not mark its box`). `helpCoverage.test.ts` — all five languages say `C:\tools\codex.cmd`, `/usr/local/bin/codex` and PATH (RED `the en settings article never says …codex.cmd…`; break-it: the Russian example path changed → red). **Against the released halves**: `scripts/live-paths-per-side-compat.mjs` (`npm run test:paths-per-side-compat`, needs `gh`, the built server and FakeCli; not in CI) — (a) a legacy row reads and crosses as the release sends it, (c) the file this build writes carries one string per row, no member, and the released server runs this side's value from it (the row's fallback points at nothing), (b) the release never runs this side's per-side value, a server ≥ 0.46.0 skips the other side's path, and the release's own vendor write DROPS the member. Run 2026-10-11 on Windows: extension-v0.67.0 + mcp-v0.46.0, extension-v0.65.0 + mcp-v0.45.1, mcp-v0.40.5 — exit 0 each. Teeth: the wire reading the legacy value → three (c) FAILs; the wire also carrying `executablePathBySide` → `the settings file this build writes carries no per-side member` FAIL. **After E3's consultation (2026-10-11).** `cliPathSaveResolve.test.ts` — the every-state property now runs over three disks (absent, here, unknown), plus the risk consultant's case: a legacy `/tools/codex.exe` with `C:\tools\codex.exe` on the Windows disk survives a WSL save (RED `the save broke the Windows side's working CLI`: `''` vs `C:\tools\codex.exe`; with an unknown disk `C:\opt\other\codex`), and saving it UNCHANGED on Windows still runs it qualified (RED `'/tools/codex.exe'`). Teeth: the spelling condition back in `pinnedFor` → three red; the raw member back in the reader → four red (both compile). `cliPathKeep.test.ts` — a member an older save dropped is said ONCE per run and again only after it came back and went again; the notice speaks of *a previously saved value*, never of who removed it, and names an empty value as empty; no settings write without a click, the copy kept; **Restore** writes the copy back and leaves the older save's legacy value (Windows runs W, WSL runs P′); an empty kept value restored as empty; a stale click refused and writing nothing (a later save, a later legacy edit, a removed row — never recreated); a reused id with another runtime or model never inherits and its copy is deleted; a side that keeps its own settings is left alone; a copy is per layer. RED first against stubs (`the drop is said once, not on every look` `0 !== 1`; the wording; `the copy is still there to restore`; the restore's `0 !== 1`; `saved for it since` unmatched); the never-guarantees passed vacuously against a stub and were proved by seven compiling break-its instead (said-once guard removed; an automatic write; `savedCliPath` used to restore; each staleness check disabled; identity by id alone; the layer check removed) — each turned its named test red. `helpCoverage.test.ts` — all five languages name **Restore** in the settings article (RED `the en settings article never says **Restore**`; break-it: the Ukrainian label un-bolded → `the uk settings article never says **Restore**`). **The code round's own review (2026-10-11).** `catalogEdit.test.ts` — a question-row switch flip and a consultant model edit written by a Windows window (`editThroughCatalog(…, 'windows')`, so the same on every host) leave the WSL side running its own path (RED: `a Windows edit of a switch took the WSL side's CLI path away` and `… of the model …`, both `actual { windows: '', posix: '' }`); a Windows path, and a bare `codex.cmd`, given through a definition are Windows' own and move nothing on WSL; `sameLaunch` matches a row to a definition resolved on either side and never to the other side's path. Break-its (each compiles): `sameLaunch` on the legacy value → `the Windows-resolved definition did not match its own row`; the rewrite writing the legacy value alone → `a bare name typed on Windows took the WSL side's CLI path away` (`posix: 'codex.cmd'`); the rewrite always saving → the member lost. The gate's fixture finding: rows widened through `isRecord` (`withRowFields`), no cast. `cliPathKeep.test.ts` — Restore after an older save of `""` or `codex.cmd` (spelled for neither OS) gives both sides their kept values back and leaves the legacy value as the older save wrote it (RED `Restore after an older save of "" gave no side its value back`: `actual { windows: '', posix: '' }`; break-it: the neither-OS case disabled → the same red). **NOT covered**: a real Windows + WSL pair of windows with two extension versions; a POSIX host running the script (it is symmetric — H and O are this host's family and the other); the page drawing any of it (E4); the keeper's host wiring (`cliPathKeepHost.ts`, `extension.ts` — it imports `vscode`; its decisions are the two modules above) and a real editor showing the notice |
| The page draws two fields — the Models card's two CLI paths and Restore, the question consultant's three groups and typed box, Setup › MCP server by side, the switch at every place (2026-10-11, `research/PLAN_paths_per_side.md` E4) | the Settings page RUN, the keeper, the help in five languages, and a real editor | `twoCliPathFieldsOnTheCard.test.ts` (E4.1) — with both sides seen the card holds two boxes, this side's first and labelled *This side — Windows* / *This side — WSL / Linux (WSL: Ubuntu)*, the other side's in a fieldset with its legend and caption and no button; one side: one box, no label, no fieldset; typing into the other box posts `executablePath.posix`, the host's `vendorFieldWrite` saves only that member, and RE-RENDERED the other box shows what was typed, this side's box is unchanged and each family resolves what its box shows; a path of this side's OS is refused beside the box (text kept, `aria-invalid`, described by the reason); Restore is drawn while a value is dropped and posts the kept key. RED: `the card draws 1 CLI-path box(es) with a Windows and a WSL side both seen`, `the card draws 0 Restore buttons`. Teeth: the other field never drawn, the other box saved as this side's (`the other side's save touched the legacy value an older reader on this side runs`), the admission inverted, Restore never drawn. `cliPathKeep.test.ts` (E4.1) — the keeper lists what its last look found dropped and tells its listeners only when the list changes (RED against stubs: `the dropped member is not listed for the card`; teeth: the listeners never told → `the panel was not told the list changed`). `theFoldersOfTwoSides.test.ts` (E4.2) — the three groups with every stored root in one, Add a folder… in this side's, Remove in the other's, the typed box labelled and inside the fieldset; a typed `/home/me/other` and `//nas/share/y` saved through `typedRootRefusal` + `rootAdded` land, re-rendered, in the other side's and the unclear group; every admission and refusal of the plan's 4.2 list on both platforms; the refusal drawn beside the box. RED: `the page draws no the thisSide group of the folders`. Teeth: grouped for the other family, this OS's spelling admitted, an existing `/path` admitted, never grouped, `//server/share` refused. `theMcpServerPlaceOfTwoSides.test.ts` (E4.3) — the other side's data directory as text in its fieldset, no input/select/textarea/button, *Change it from a window on that side.*; a side that chose none listed as such; the watched folders in the groups of their spelling; one side: as before. Teeth: never drawn, drawn as an input (`offers a control — it is read-only here (D2)`), grouped for the other family, never grouped. `pathFieldsPerSideSwitch.test.ts` (E4.4) — *Always one* draws one field at all three places with both sides seen, *Always two* two with one side seen and the legend *(not seen yet)* at every place; written after the behaviour landed, teeth by reverting each deciding line (the switch ignored, the not-seen legend). `helpCoverage.test.ts` — every language says **This side — Windows**, the caption, *Both sides / unclear* and Setup › MCP server, and no longer promises the field for a later release (RED on `en`; teeth: the Ukrainian label un-bolded). `test:host` `twoPathFieldsScenario.ts` (E4.5) — *Always two*, Settings › Models opened by the gear, a row saved through `vendorFieldWrite` box by box, and the `settings.json` the real mirror writes names THIS side's value while the settings hold the other side's in its member (RED with the pre-E4 write: `the settings do not hold … the other side's in its member`; teeth on the bundle's reader → the file names `''`). A host cannot type into a webview, read its DOM or write the extension's `globalState`, so the seeded WSL record and the two drawn boxes are the page tests above. **E4's code round (2026-10-11).** The gate's finding that the host scenario bypasses the panel's save path: `cliBoxWiring.test.ts` — a wiring scan of the provider's `vendor` case (the page test runs the box's message, the host scenario runs the mirror; the case in between imports `vscode`): the CLI-path refusal comes before the save and returns, and every field is saved through `vendorFieldWrite` with this host's family. Teeth (both compile): the refusal not returning → `a refused box falls through to the save`; the save going around `vendorFieldWrite` → `the vendor case saves a field around the one vendor write — the other side's box would reach the legacy value`. **This repository's own review.** (1) While this side keeps its own settings: `twoCliPathFieldsOnTheCard.test.ts` and `theFoldersOfTwoSides.test.ts` — with `perSide: true` and both sides seen the card draws ONE box and the other side's group holds the sentence *This side keeps its own settings — set the other side's value from a window on that side.* and no control; the folders draw no typed box, the sentence in its place, the stored roots and Remove kept; a refusal is drawn beside the sentence; one side known: as before; `cliPathRefusal(…, ownSettings: true)` refuses every write of the other side's box (clearing included) with what was typed, never this side's box; `keptPerSide` by the switch. `ownSettingsWiring.test.ts` — the vendor case hands `writesTheOverlay(config, 'vendors')` to the refusal; `QconsultHost.typedRoot` asks `hooks.keptHere('qconsultRoots')` before the disk; the provider's hooks answer through `writesTheOverlay`. RED: `with coai.perSideSettings on the card draws 2 CLI-path boxes — the other side's saves into THIS side's own settings, which that side never reads, and the page shows it saved`, `… the other side's group draws 1 typed box …`, the pure refusal `actual: ''`, and both wiring scans. Teeth (seven, each compiles): the card / the folders ignoring the switch, the pure refusal ignoring it, the provider / `QconsultHost` / the page asking the wrong key, `keptPerSide` ignoring the switch. (2) A shared value of this side's spelling: `twoCliPathFieldsOnTheCard.test.ts` — a legacy-only `C:\tools\codex.exe` in a Windows window draws the WSL / Linux box empty, described by *The shared value C:\tools\codex.exe is this side's CLI; that side skips it and looks the CLI up on PATH.*, and the mirror from WSL (`/usr/bin/codex`, the Windows box empty); a member of its own, a legacy value of that side's spelling, a bare name and a `//server/share` are shown as stored, with no hint. RED: `the WSL / Linux box shows 'C:\tools\codex.exe' — the Windows side's path, which the WSL side skips (a PATH lookup) and which the box itself would refuse if edited`, `the Windows box of a WSL window shows '/usr/bin/codex'`. Teeth (four, each compiles): the member ignored → `re-rendered, the WSL box lost what was typed`; the spelling judged for the wrong family → `the Windows box does not show the value the Windows side runs`; the value shown anyway; the hint on the wrong boxes. `aCliPathOfTheOtherSideIsSkipped.test.ts` counts the two new display reads. `helpCoverage.test.ts` — every language carries the sentence (RED on `en`; break-it: the Ukrainian sentence un-italicised → `the uk settings article never says why …`). NOT covered: the real webview drawing the two boxes (page tests run the page's own script over its own markup); a real WSL window beside a Windows one (E0 measured the registry by hand). |
| `ask_human` | **yes, since 2026-10-02** | `AskHumanScenarioTests` — the REAL server build over real stdio (S3 of `todo/PLAN_question_consultant.md`): a seeded session whose plan proceeded, two free batches answered at once with `ask_in_conversation` (since 2026-10-03 the AI's own questions write no card — `research/PLAN_ask_human_is_for_the_gate.md`), the third refused naming `ask_consultants`, the consultants answering through the fake CLI as a codex row, and `ask_human` with that `consultId` allowed — the record `person_asked_in_conversation`. The card and its wait over the same wire: `McpContractTests` on a held gate. It was **no** until S3: it BLOCKS until a person answers, the wait is the behaviour, and a scenario answering it from a fake surface would have exercised the fake; the gate changed the shape — the refusal does not wait at all, and since 2026-10-03 neither does the AI's own allowed question (`ask_in_conversation`); the card's wait and its expiry are asserted over the wire by `McpContractTests.AskHuman_WithNobodyListening_WaitsThenSaysToAskInTheChat`, through the file the person's surface reads. Beneath it: `AskHumanServiceTests` (every row of the gate's table against the real service — *The door to the person* below), `AskGateTests`, `QuestionPhaseStoreTests`, `StageReleaseTests`, `EscalationRetentionTests`, `EscalationsTests` and `HumanDecisionTests`; which SESSION a question is filed under — the document's when `document` is passed, the branch's otherwise — by `ADocumentIsReviewedEndToEndTests` (2026-09-25, §9.3 of the feature-review plan); the feature session's, when `feature` is passed, by `AFeatureIsReviewedEndToEndTests` (2026-09-26, S2.2b). |

A note on why the remote row exists at all. The catalogue is derived from the TOOL registry, and a
vendor runtime is not a tool — so nothing would have failed had the remote flow gone uncovered. What
makes it worth a row is the shape of its failures: the adapter builds a command line, a different
process reads it, and the two agree only until somebody edits one of them. The same shape produced
this repository's two most expensive misses — a released server binary that could not serve a single
request under `PublishAot`, and a page whose minified binding was renamed — and in both cases every
unit test was green. `RemoteShimScenarioTests` runs the real binary end to end, including the two
cancellation paths: the shim reaching its own deadline (it cancels and says so), and the shim being
KILLED (it leaves a claim the parent acts on).

**The socket the stub sits on is part of that harness, and it is the reason a correct release did not
ship.** The fixture asked the OS for a free port and then let go of it, which is a race nothing owns:
on 2026-09-15 the `linux-x64` leg lost it, the fixture threw out of `InitializeAsync`, xUnit blamed
whichever test was first, and `mcp-v0.25.0` stayed a DRAFT carrying ten assets instead of twelve.
`LoopbackStub` takes the next candidate instead, bounded at ten, and `AStubSurvivesALostPortTests`
covers: a lost port is retried (the one RED test — verified by setting `Attempts` to 1, the shipped
behaviour, and watching it fail on the real symptom); the bound holds; every code the classifier
claims is honoured, the cases DERIVED from the classifier so the two cannot drift; a failure that is
NOT a taken port arrives as itself, unretried; an exhausted run keeps the platform's own exception;
and — the only one that can fail on a platform nobody has measured —
`TheCodeThisPlatformActuallyReports_IsOneTheStubRetries`, which provokes a REAL collision both ways
on whatever leg it runs and asserts the classifier knows the code that came back.

What this does NOT prove: the collision in every other case is CONSTRUCTED, by holding the port or by
handing the stub a binder that throws. Nothing here reproduces the ambient contention of a loaded
runner, and nothing here can — so the retry is proved and the frequency it was written for is not.
The error codes were measured on two of the six release platforms, each with the port held both ways
(Windows 32 by a socket and 183 by a listener; Linux 98 and 400) — 98 being the one that failed CI.
macOS's 48 and Winsock's 10048 are carried without a measurement, licensed by the Linux result: the
managed listener handed back the raw errno, so a platform on that path reports its own. A leg whose
code is outside the set does not degrade quietly; it reddens the test above and names the code.

### The extension's flows

The VS Code commands in `src_vs_code/package.json` (`contributes.commands`) are the extension's
flow list. They are **not** driven by a scenario that launches an extension host: what is covered is
each command's decision — the pure function it routes to — plus the page as it ships. The gap is
real and named here rather than implied:

| Flow | Covered | By |
|---|---|---|
| **The settings mirror: a changed setting reaching the file the server reads** | yes, in a REAL editor | `src/test/host/scenarios.ts`, second scenario (`npm run test:host`, the `extension · a real editor` check). The launcher gives the host a data directory of its own through `COAI_DATA_DIR`, and the scenario activates the extension, waits for `settings.json` to appear at all, turns `coai.dealPlanLenses` on and waits for `COAI_DEAL_PLAN` to reach the file — then turns it off and waits for it to LEAVE, because a mirror that only ever adds is one that pins the old value. That is the whole chain no value can answer: the configuration listener, the schedule, the lock and the rename. Everything else about the mirror IS a value — `mirrorSchedule.test.ts` runs the three attempts, the supersede, an attempt still in flight when a change arrives, the two conditions, the stand-down that is not a write and the sync that throws, all against a clock whose timers fire only when a test ticks; `serverSettingsFile` and `settingsShape` hold the payload. **NOT covered**: the failure path in a real host. Making a write genuinely fail inside the editor means a read-only directory, whose behaviour differs on every platform this runs on, and the scenario would then have to sit through the eight-second ladder; the exhaustion report, its two codes and the recovery that follows it are asserted as values instead. Nor does anything here prove the SERVER re-reads the file it was handed — that is `module_server.md`. **And the scenario had to learn to read a file that is being replaced** (2026-09-18): every read was a bare `readFileSync`, while the thing under test is a writer whose whole job is to rename over that path — `vscode.workspace.fs.rename(…, { overwrite: true })` is not documented as atomic, and `extension.ts` says beside the lock that the disk provider checks for existence and then renames. CI caught it as `ENOENT` in place of an assertion, which is how this job's twenty-run promotion streak keeps getting reset. The reads go through `settingsUntil` now, where an absent file is NEVER ready in either direction — read as empty text it would have satisfied the final wait, that the marker is GONE, by the file simply not being there. |
| **The Question consultant tab, a row reaching the server, and the log page** (2026-10-02, `todo/PLAN_question_consultant.md` S4) | yes, in a REAL editor | `src/test/host/scenarios.ts`, the scenario after the gear's: `coai.openSettings` on `questionconsultant` opens the one Settings tab, a `coai.qconsultRows` row set in the host reaches `COAI_QCONSULT_ROWS` in the host's own `settings.json` and leaves it when removed, and *Show review rounds* opens the page holding the Questions tab. What the tab and the sidebar DRAW is the page tests' — `qconsultSection.test.ts`, `activeQuestions.test.ts`, the bundled Questions tab — because a host cannot read a webview. **NOT covered**: a person pressing a control inside the webview in a real editor. |
| **Deleting a role, and what happens when the mirror does not carry it** | yes, as VALUES | `roleDeletion.test.ts` runs the coordinator against an in-memory store and a configuration that is a set of live ids, so `gone` is a real question rather than a stub. Ten cases, and the three that carry the design: the LISTENER owns the sync and the deletion is told `busy`, and the cleanup still completes — a build that calls `sync()` itself passes every other case here; a sync landing BETWEEN two of step 2's writes does not finish it, which pins the condition as *the role is absent from the configuration now* rather than *a write landed*; and a worker whose tombstone was cleared and whose id came back deletes NOTHING, which is the nonce. Interruption is three cases, one per state a dead host can leave — the first draft named four and set up the same state for three of them, a bigger number proving the same thing once. `roleDeletionStore.test.ts` is the half only a filesystem can answer: an id that may not become a path is REFUSED rather than sanitised, one unreadable entry strands one deletion and not the other four, and the data directory is read per call because it can MOVE. `rolesPage.test.ts` holds the surface: the cost sentence is asserted verbatim, and *Reload Window* is offered for a stand-down and for nothing else — both directions, because the cure for one condition offered for another is worse than no cure. **Teeth**: planting the four reverted behaviours turns six of the ten coordinator cases red, each naming the symptom — *the prose was deleted before the server had lost the role*, *a stale worker deleted the text of the role that took the freed id*. **A real editor drives it end to end** (`src/test/host/scenarios.ts`, third scenario): a tombstone and a prompt file are planted in the host’s own data directory, the role is put into `coai.roles` and the scenario waits for it to reach `settings.json` — asserting the text is STILL there, because the server has the role — then removes the row and waits for the prompt file and the tombstone to go. That is the configuration listener, the schedule, the terminal callback, the payload check, the claim on a real filesystem and the unlink. **NOT covered**: the webview press, the FAILURE path in a real host (a stood-down mirror there needs a second build’s stamp in the settings file), and two real windows racing on one data directory, where what is driven is two coordinators in one process. |
| **The Settings tab: the gear opens ONE tab per window, and opens it again once closed** | yes, in a REAL editor | `src/test/host/scenarios.ts` (*the gear opens ONE Settings tab, however often it is pressed, and opens it again once closed*): two presses at once, one of them with a tab id, leave exactly one `coaiSettings` webview tab; closing it and pressing again opens one. What the tab SHOWS is not visible to a host — a webview's DOM is not — so that half is the page tests': `settingsPage.test.ts` RUNS the page (the held tab opens alone, a press shows exactly its pane and posts `{type:'tab'}`, `showTab` selects without an echo, the arrow keys wrap, Home/End, an unowned key untouched, the ARIA wiring both ways — two script mutations turned six of nine red), `bundledPage.test.ts` runs it MINIFIED, `theGearSitsBesideHelp.test.ts` holds the title-bar order in the manifest. Not covered: a setting written FROM the Settings surface in a real host — a host cannot post into a webview; `bothPagesWriteThroughOneQueue.test.ts` reads that both pages are heard by the one dispatcher and queue their writes through the one path, whose behaviour `settingWrite.test.ts`, `snapBackQueue.test.ts` and `aQueuedWriteNeverWaitsOnItself.test.ts` run |
| The chat tab's page | yes, as it ships | `bundledPage.test.ts` runs the bundled, minified page and drives its **Send** button through to the one posted turn, asserting the composer locks on the way — the flow the pinned composer added. `chatPage.test.ts` runs the same script from source through `runChatPage()`, which answers only the ids the page actually renders, so a control that stops being rendered stops being testable rather than silently passing |
| A limit that waiting cannot clear | yes, over a real process | `GateReportingTests.cs` holds the vocabulary: the measured codex answer is hopeless, two NEGATIVE cases are not (a throttle carrying a sales footer, a throttle naming how long to wait), and both halves of the line choice are pinned — a terminal line wins over an earlier marked one, and an ordinary throttle is still reported when no line is terminal. **The guard that matters most is `EveryHopelessPhrase_HasAnObservedAnswerThatSurvivesTheLineFilter`**: `Reason` reads MARKED lines only, which is safe solely because every hopeless phrase happens to be carried by a line `Phrases` also matches — a property of the strings, not of the lists, and nothing enforced it until this table did. Add a phrase with no vendor answer behind it, or one whose answer is not marked, and it fails. `BoundedSchedulerTests.cs` drives the real scheduler against a real process: one launch, counted in a file, AND no progress note announcing a wait, because one launch with a long wait is still what was complained about. **NOT covered**: no stated-wait parsing — *retry after 20 seconds* against *retry after 3 hours* — because no sample of one has been observed from a vendor this product runs; and `ScenarioCoverageTests` is keyed by TOOL, so this flow adds no row there and the catalogue does not move. |
| The rounds log's pager and its two views | yes, the page is run | `roundsLogPaging.test.ts` — the pager's disabled state is asserted against the PARSED stylesheet and a modelled element, not its text: a rule keyed on something no button carries satisfies every substring search and paints nothing. The hover half is the one that matters, because a browser matches `:hover` on a disabled element, so the test asks whether any hover rule still REACHES a dead control and names the ones that do. The two views are asserted across a PAGE BOUNDARY every time — the order of the view filter against the slice is invisible on one page — and a search matching both kinds is asked in each view. `roundsLogPage.test.ts` keeps the inverted guard that allows exactly the known button selectors and reddens on a new one. **Every rule and filter was proved by deleting it**, which is how the *back to page one* test was found passing with `firstPage()` removed: `render`'s clamp rescued it while the other view held one page. **S6, 2026-09-17** adds four to the same harness: that a tab drives every section the page MARKED — asserted by recording that the page ASKED for `[data-section]`, because the harness keys a section stub by its own id and the three literal ids would otherwise satisfy it; that the TABLE survives the conversations tab, which watches the ordering of the derived loop against the table's own line rather than trusting it; that every tab section carries the marker, so one added without it cannot go quietly invisible; and that a search finds a phrase appearing only in the Reviewers cell, with a control that a real miss still misses. The harness gained one selector and now keys sections by their id, so `tab-rounds` is ONE stub rather than two. NOT covered: no layout engine, so the declarations and the cascade are asserted and the rendering is not |
| **The Team server tab: an admin reads who spent what on a Team server** (2026-10-09, `todo/PLAN_team_usage_by_person.md` epic 1) | yes — over a REAL server, and the page as it ships | **End to end:** `src/test/teamUsage.contract.ts` in `npm run test:contract` — the built `coai-server` on a loopback port, which `scripts/run-contract.mjs` now starts with one admin (`Coai__Admins`, the `COAI_CONTRACT_ADMIN` persona; set it yourself when pointing at a server you run). Through the REAL client and the REAL `TeamUsageCache`: the catalog says the admin is one and a member is not (the flag the tab is shown on, D1); an admin's `scope=company` answer is kept with its people and vendors lists, and a window change asks the server again (Year's range starts before Week's); a member asking for company is refused `403`, the cache keeps nothing, and the tab says *no longer an admin here*; the real body with what a newer server adds removed says *needs Team server ≥ …* and never a zero. Removing the admin persona turned three of the four red with the server's own refusal. **Epic 3 (2026-10-10):** the runner now seeds the started server's `usage.jsonl` (three lines: two people, one unnamed model, one vendor id written `Codex`) and sets `COAI_CONTRACT_SEEDED`; two more cases — `GET /api/people` through the real `fetchPeople`: an admin who has just signed in with a display name is listed under it, every row has exactly the three fields, the card built from it says *last seen within the hour*, a member is refused `403`; and the company answer: vendor ids lower-cased, `models[]` under each vendor with `""` for the unnamed one, `daily` thirty dense `yyyy-MM-dd` days whose per-vendor sums equal the seeded launches, and the tab drawing bars from it with no *needs* sentence. Not seeding the ledger turned that case red; against a server it did not seed, it skips and says why. **The page:** `teamServerTabPage.test.ts` runs the bundled, minified page on `pageDom.ts` — a document built from the page's own markup, where an absent id is null and an unknown selector throws (its own tests: `pageDom.test.ts`) — fed pushes the host's real renderer built: a non-admin has the tab and section hidden, an admin's push reveals it, search narrows by email, the sort moves the cards, a hostile email is text, a push keeps the query, its focus, the order and an opened card (and the same email on another server is another card), the chips/Refresh/picker/tab each post their command with the server id, a demoted admin is taken back to Rounds with the tab EMPTIED (no email left in the document), a sign-out's push leaves the page holding no email, and the watchdog leaves a pushed tab alone. Each of seven page lines was deleted in turn and its test went red. **Epic 3's pure parts:** `teamPeople.test.ts` (the join by case-insensitive email keeping every spender, the idle list, last seen at the hour, a hostile display name as text, `peopleFrom`), `teamCost.test.ts` (each model at its list price, no long-context tier on a sum, a reported cost winning, an unpriced model a dash and its total a floor), `teamChart.test.ts` (bars summing to each day's launches at one scale, the axis label, the days as written, the legend, the table, no hex colour, hostile strings as text), `teamUsagePeople.test.ts` (the listing through the cache: admin-only, a refusal of it or of company forgetting both); after the own review of E3, RED first for: a listing still on its way said as asking and not as an older server, `sortWhy`, an older server's reported vendor cost kept, the chart's numbers fold keeping open across a push, the axis label start-anchored, an older server's cards without *last seen*; and `listPriceFrom` held against a vendor row's typed rate (a guard: the factory takes no row, so its red was shown only by breaking the list half) — RED first against stubs for the cache, pricing and chart; the join, the page's new-server behaviour and the help were proved by reverting each rule once. The bundled page against a NEW-server body (names, last seen, ~$, the idle list, the chart, search by name, the ~$ and last-seen sorts) and an OLD one after it (the sorts disabled again, saying why; the search back to email; no zero). **The cache:** `teamUsageCache.test.ts` (per-key answers, sequence and freshness from the request's start, evictions that tell the page, a refusal clearing every company window — RED first against the extracted unchanged cache, and again for the own review's three findings) and `teamUsagePoll.test.ts` (one tick at once, then one a minute). **Which keys are asked as the page opens, shows its tab, goes behind and closes:** `teamTabHost.test.ts` — the real `TeamTabHost` driving the real cache over a hand-turned clock and no sidebar: an opened page asks at once; company only in front with the tab shown, not behind, and again when back in front; a closed page's clock stops and nothing it leaves wants company; a reopened page starts on Rounds. (The first commit's `ThePageRefreshesTeamUsage_WithTheSidebarClosed` drove the poll module alone and could not fail for the wiring's reason; that claim is withdrawn and this file replaces it.) **NOT proven:** the webview itself in a real editor — a host cannot read a webview, so the page is the bundled harness's; a server with spending in its ledger — the seeded ledger is three lines, so the figures are checked for their shape and their sums, not at volume; the real price book (both public lists, fetched from the network) is never consulted — every test hands `teamCost` its prices; and the host wiring (`extension.ts` hooks, `PanelProvider.refreshTeamUsage`, `RoundsLogPanel.whileOpen`, its view-state listener and its disposal resetting the team push), which imports `vscode` — its decisions are the modules above (`teamTabHost.ts` among them), its plumbing is unexercised |
| A copy control that says it landed | decisions and the rendered page | `chatPage.test.ts` — the tick beside a *Copy block* / *Copy answer* control. Four things pinned TO EACH OTHER: the acknowledgement is delivered and the mark lands on that control and no other; a press ALONE marks nothing, which is the whole point, since a control that confirmed on the press would confirm just as confidently while the clipboard was held by something else; the mark survives a state push rebuilding `#messages` under it; and two presses half a second apart leave the second one's second intact rather than the first timer cutting it short. The CSS half is parsed with `cssRules.ts` and the selector is matched against the control the page actually renders — a source-text assertion passes on `.never[data-copied="1"]`, which matches nothing. The host's own half is `acknowledgement` in `answerCopy.ts`, which is a function precisely so a test can reach it: nothing in `chatCommand.ts` can be imported here. **Every guard was proved by breaking it** — the rule removed, the condition inverted, and the harness reverted to answering `[]`. NOT covered: no layout engine, so the declarations and the cascade are asserted and the rendering is not |
| A failed turn's **Try again** | yes, as it ships | `bundledPage.test.ts` renders the shipped page with a failure the host says is retryable, asserts the control is in the MARKUP a person is handed — the markup alone, because the page's own script carries the `[data-retry]` selector as a string and a whole-document match would be true whatever the state is — and presses it through the delegated region listener to the one posted `retry`, which names the transcript length it was drawn for. A companion case asserts no control at all when the host says there is nothing behind the failure. What it does NOT prove: a second press is refused by a flag written on the pressed element, and this stub fabricates a fresh element per click, so that guard lives in `chatPage.test.ts` where the same element can be handed over twice. The host half is `chatPresets.test.ts` (`retryFrom` reads the trailing message and nothing else), `chatMessages.test.ts` (a retry naming no whole number is ignored, so there is no wildcard press) and a structural guard in `chatWiring.test.ts` (a retry does not recompute the carry the failure preserved for it) |
| Folding a long question | yes, as it ships | `bundledPage.test.ts` renders the shipped page with a question over the character bound, asserts it came back folded and carrying a key, and presses the control through the delegated region listener — then reads the rule out of the page's own `#folds` stylesheet, which is where the open set lives. `chatPage.test.ts` holds the rest: the boundary of `isLong` on both arms, that `foldKey` is stable and content-derived rather than positional, that the control names how much is hidden in the unit the question actually has, that an answer is never folded, that an opened question survives the transcript being replaced AND closes again on a second press, and that a key which is not the host's own base-36 hash is never written into a stylesheet |
| The rounds log page | yes, as it ships | `bundledPage.test.ts` bundles and minifies the real module and runs the page script — the only test that catches a minifier-renamed binding, which shipped twice |
| Exporting a round, and a selection of them, to CSV | decisions, and the cross-process SEAM | `roundsCsv.test.ts`, `roundsExport.test.ts`, `roundsDbMany.test.ts` — the column list; that an absent measurement stays an EMPTY cell and never a zero, because zero is a number somebody measured; the formula-injection guard including a payload hiding behind a leading space, which a naive guard passes and a spreadsheet does not; the instant written twice, stored-UTC and local-with-offset; one line per finding carrying the round's own columns and the decision in the page's own words; and a round that genuinely found nothing still getting its line. On the selection: that filtering never unmakes a selection while a round scrolled out of the loaded window does leave it, the hidden count on the button, and the confirm above five hundred. On the SEAM: that `--findings-many` is asked with a keys file and every answer is paired to its round **by the key the server echoes back, never by position**; that exit **64 and only 64** means an older server and falls back to `readFindings` four at a time; that 65/69/74 fail the rounds rather than being retried a different way; that a cancel reaches the child process rather than only stopping the wait; and that a round whose findings could not be read is written `not recorded` or `failed` rather than as a round that found nothing. NOT covered: the save dialog itself, which is `vscode` state |
| A question that waits its turn | decisions, the page RUN, and the seam | `chatQueue.test.ts` drives the queue's own rules — order, the two ceilings, that two identical questions are two rows that do not share a fate, and that a withdrawn question stops being WANTED, which is what stops it running. `aQuestionCanWait.test.ts` renders the page: Send live while a turn runs, locked while the conversation is FULL, the waiting rows in order with their crosses, and an `onerror` payload coming back as text. `aQuestionCrossesTheSeam.test.ts` is the one that joins them — it RUNS the page script, presses Send twice, puts what the page posted through the real parser and applies the decoded commands to the real queue, because a page suite and a host suite can both be green while the command name or payload between them is wrong (`forgetAChatRow.test.ts` is the scar). `bundledPage.test.ts` covers it minified, as it ships. The code round sharpened two of these: the seam file`s harness could only deliver a click whose target matched nothing, so it never once pressed a waiting row while claiming to cover that crossing — it presses one now, and unwiring the delegated handler turns it red — and the composer`s LOCK is asserted by pushing host state into the running page rather than only by reading the rendered markup, because a page whose markup is right can still disable the box the moment the host says a turn began. **NOT covered**: `chatCommand.ts` itself, which needs a `vscode` host — the enqueue/dequeue wiring is checked by the compiler and by the pure module, not executed. |
| Phrases — the list, the tab, and the copy | decisions, plus one real-editor scenario for the default list | `phrases.test.ts`, `phrasesEdit.test.ts`, `phrasesPage.test.ts`, `phrasesSection.test.ts`, `phraseCopy.test.ts`, `panelPhrasesScript.test.ts` — that a mistyped row is dropped on its own rather than taking every other phrase with it and that nothing in the settings file makes the extension throw; that a row with words and no name keeps its words and is named from its first line that HAS words, cut to sixty characters with an ellipsis that says so while the phrase itself is never shortened; that the phrase is kept byte-exact, indentation and trailing newline included; that *Copied* is posted only after the clipboard accepted the text, and that two presses land in press order rather than racing. `panelPhrasesScript.test.ts` runs the generated page script, which is what caught an unterminated regex the source-matching tests passed straight over. **The default phrases (2026-10-08)**: `defaultPhrases.test.ts` reads the manifest's declared default through `phrasesFrom` — the seven phrases word for word, ids unique and `phrase-default-…`, names within `NAME_LIMIT` — RED first on the old `[]` (`0 !== 7`); and removing every default through `rowsAfter` leaves an empty list to save, never "unchanged". The editor scenario *the default phrases appear with nothing saved, and an emptied or own list is left alone* (`host/defaultPhrasesScenario.ts`) does the round trip through VS Code's real configuration layers: nothing saved → seven, the emptied list stored and read back empty, a person's own list read back as theirs |
| The catalog migration waits for the window's settings (2026-10-09, `research/PLAN_catalog_migration_waits_for_its_settings.md`) | decisions, plus one real-editor scenario for the registry signal | `catalogMigrationRun.test.ts` drives one layer's run with fake layers and reports: VS Code's own refusal (`coai.migratedFrom is not a registered configuration`) is a wait, never a stop — RED first, the refusal reached `stopped`; a layer whose `unknownKeys` names a key is written NOTHING — RED first, all four keys were written; any other refusal still stops; a known layer is written in the plan's order, the backup first; a refused FIRST write reports nothing written (RED first, the run said it wrote); a refusal in German is still a wait when the registry, asked again, does not know the key (RED first, it stopped). `migrationWait.test.ts`: one retry armed on a settings change AND a 10 s timer, each firing it once and taking the other down; a wait while armed is the same wait (RED first, it warned); a wait after the retry warns once and arms nothing; `settled()` gives a later wait its own retry (RED first); dispose leaves nothing armed. Break-it: each of the six halves removed turns its own test red. The editor scenario *every key the catalog migration writes is known to the real settings registry, and an undeclared one is not* (`host/migrationKeysScenario.ts`) holds `unknownKeysOf` over `inspect().defaultValue` to VS Code 1.141 — including the three keys declared with no default. NOT covered: the update in place itself (the editor under test installs the extension before it starts) |
| Ending a consultation, and what it cost | decisions, a real process, and the release itself | `ConsultationClosingTests` is the table — who may write which word, that `lapsed` is the server's own and not a verdict, that a verdict is immutable while an absent one may still be filled, and that a FAILED consultation takes none. `ConsultScenarioTests` drives six of them over a real consultation through the fake CLI; `CloseConsultCliScenarioTests` drives six more through the PROCESS the panel runs, because the panel reaches the server through `--close-consult` and nothing else — the exit codes (0 / 64 / 65) are what it reads and nothing else can check them. On the extension side `consultationsLive.test.ts` calls `outcomeSaid` and `consultationCost` directly, per the operator ruling that refuses a new behavioural assertion over page source. **The two scenarios earned their keep immediately**: they found a record from an older build reading back with NULL strings (the deserializer skips initialisers for absent members) and `ProcessIsAlive` taking the whole binary down on a pid nobody may open — neither reachable from a unit test. **The boundary is checked against the RELEASE**: `npm run test:close-compat` downloads the previous published server and runs both on one data directory, asserting the old one refuses the new mode with a sentence and still reads a database this build has migrated. Run by hand, not in CI — it needs the network and a published artefact. Last run 2026-09-17 against coai-mcp **0.27.1** — the release BEFORE the newest, or once this ships the check compares the build with itself: the boundary holds, and the old binary's refusal is **exit 64**, which is what makes the panel's "your server is too old" branch a fact rather than an assumption. The code round added three more: `closeAConsultation.test.ts` RUNS the log page and clicks the control, because a markup assertion cannot see a message that leaves without its id; `ConsultationOutcomeSourceTests` covers the author of a verdict and a database written by the previous release, whose missing column threw the WHOLE log page away; and both halves assert their own catalogue against `shared/consultation-outcomes.json`, a file neither owns — `TheOutcomeCatalogueIsOneListTests` here and `closeAConsultation.test.ts` there. **NOT covered**: the QuickPick itself, which is `vscode` state. |
| Which Claude models this machine reaches | decisions, and the CLI contract by hand | `claudeModels.test.ts`, `claudeProbe.test.ts`, `claudeProbeFile.test.ts`, `claudeCli.test.ts` — the `modelUsage` parse; the asked-vs-answered comparison, which is the whole design, since an unknown alias exits **0** and the default answers; the week-and-version freshness rule; the file round trip with the verdict RE-DERIVED from the answer rather than read; that one candidate which could not be started costs its own candidate and not the three after it; that a partial run MERGES into what was held and never subtracts; and which CLI is asked, from reviewer rows **and** consultant definitions. **The probe itself is never run here** — each candidate is a billed request — so the CLI's own JSON shape is checked by `npm run test:claude-models`, run by hand against the installed CLI: it asserts `modelUsage` is keyed by exactly one model, that a known family answers as itself, and that an unknown name still exits 0. Last run 2026-09-16 against CLI 2.1.258: the contract holds. **That script found a defect no unit test could**: `capture` left the child's stdin open, so the CLI waited three seconds for input on every call — twelve per probe — now closed with an EOF and pinned by `processLauncher.test.ts`. **NOT covered**: nothing asserts that today's CLI still answers in that shape without running that script, and a change to it would leave this suite green with every candidate quietly unverified |
| Install / update the server | decisions only | `install.test.ts` (RID choice, asset names, companion policy, the release workflow's own guarantees) |
| Copy the config block / the CLAUDE.md snippet | decisions only | `install.test.ts`, `panelServerPromptAgreement.test.ts` |
| Panel settings writes | decisions only | `settingWrite.test.ts` — the routing decision, not the `vscode` call it leads to |
| Add / sign in to / sign out of / remove a Team server | decisions only | `teamServers.test.ts`, `teamServerAuth.test.ts`, `teamServerApi.test.ts`, `teamServerView.test.ts` — the canonical URL and token path (against the shared cross-language fixture), the scope trust boundary, the sign-in compensation, the renewal flag, every request's failure path with a stubbed `fetch`, and the section's markup. The `vscode` calls those decisions lead to are NOT covered, for the reason in the row below |
| A reviewer's status mark in Active rounds | the decision, by calling it | `activeRounds.test.ts` — `statusMark` is EXPORTED and tested by calling it, one case per status, asserting the exact class AND the exact glyph, because a substring assertion over the generated page cannot tell a branch that returned the wrong glyph from one that returned the right one. The set of four is additionally asserted distinct, which the per-status loop cannot see on its own. An unrecognised status gets a span carrying `class="mark"` with no modifier and no glyph — both halves asserted, so neither "invented a meaning" nor "dropped the column" can pass. `panelView.test.ts` pins the whole line as it is assembled. The CSS is asserted as a MAPPING — done/green, running/blue, queued/yellow, failed/red, each naming its own `--vscode-charts-` variable — because "names some charts variable" is green when done is red. NOT covered: that the glyph renders in the sidebar's font on a machine this has not run on; every glyph used was already shipping elsewhere in this product, which is the argument standing in for that check |
| The Consultant section's frames and colours | the decision by calling it, plus ONE integration assertion | `consultant.test.ts` — `callerColour` is exported and tested by calling it, per caller id, **against the allocator rather than a hard-coded hex** so the test cannot drift from the palette; `other` is asserted to be the neutral token AND not what the palette would give it, because asserting only the first would pass if the palette happened to return that token. `panelView.test.ts` carries the one that actually guards the feature: the rendered page must show the SAME colour for `codex` on its reviewer card and on its caller row. Verified by deletion — removing the palette from the `consultantBody` call reddened **only** that test while every `callerColour` test stayed green, which is what proved the unit tests alone were insufficient. A companion test pins that the frame was not bought by rewriting the controls: every `data-setting`, `data-caller` and `id` hook survives, and the section's hints are compared whole against an unframed render. NOT covered: that the frame is visible. Nothing here has a layout engine — see *What this does NOT prove* — so the declaration and the inline colour are what is checkable |
| The Consultant tab's health block, Check and Copy (2026-10-03, epic 5 of `research/PLAN_the_consultant_works_on_every_vendor.md`) | decisions, the page RUN, the host on ports, and the REAL binary | `consultantHealth.test.ts` — the server's answers parsed (64 and a body without `consultants` are too old, `[]` is answered, junk reads defaults, a file over the byte cap is refused unparsed), `checkStateOf` this side as given and the other side by its OWN published staleness (fresh, stale, a shorter margin, no field → "cannot be told from here", an unparseable beat → abandoned), every failure word of `shared/consult-failure-kinds.json` labelled as the catalogue labels it, and only `denied-by-cli` worded as observed confinement. `consultantSides.test.ts`, `consultantHealthRead.test.ts` (a failed read answers nothing so the last snapshot stays; the signature ignores the heartbeat and moves with the clock on the other side). `consultantHealthState.test.ts` — what each row shows, as a value: a failure of another vendor/model hidden, too-old offers no Check, the settled `checking` disables the button with no local flag (durable status), the newest check record wins with the server's word on a tie, the other side read-only with its time, its evidence path labelled and a Remote-WSL pointer. `consultantHealthPage.test.ts` runs the Settings page (`runPanel`): one Check per row with its id, the click posts it, disabled while checking, none in another side's block, a failure for another vendor absent, values escaped, the snippet's Copy and `copied` per command with the old `copyPhrase` path intact. `consultantCheckRun.test.ts` (0/64/65/crash; the static cap tightened only from a state file newer than the spawn, never extended; the poll stops on an early exit), `consultantHealthHost.test.ts` (a refused confirmation runs nothing, a second press while confirming or running runs once, a throwing run clears the flag, an unknown id runs nothing, a copy with no snippet touches no clipboard), `consultantsProbe.test.ts` (never awaited, one spawn, a repaint only on change, `forget`), `consultantHealthWatcher.test.ts` (a new `consultants.json` on THIS side does not re-ask the server — the endless loop epic 5's review found; start is idempotent; pause keeps the snapshots and stops the poll). The review added to `consultantHealthState.test.ts`: a check of the consultant a row used to name is labelled, a failed check names its consultant, a `checking` left on disk after this window's run ended or after a later server answer does not hold the row busy, with no server answer it is judged by its heartbeat, and an `already-checking` press never outranks the next state; and to the page test, positive companions — the row's own failure, the limitation and the rule's `<pre>` ARE on the page. **The whole-branch review added** (each RED first unless said): `consultantHealthWatcher.test.ts` — a new failure or answer file on THIS side re-asks the server (A2; RED `1 !== 2`), the first read does not (N3; RED `1 !== 0`), a WSL clock five minutes behind with a moving heartbeat is `checking` and one five minutes ahead with a still heartbeat is `abandoned` after sixty seconds of THIS clock (O; RED `abandoned` / `checking`), and `consultantTabShowing` (N2; teeth by `&&` → `||`); `consultantsProbe.test.ts` — an answer differing only in `utc` repaints nothing and is still kept (N1; RED `2 !== 1`); `consultantHealthState.test.ts` — the server's `failureCurrent` is shown without a second vendor comparison (A3; RED `undefined`), an unreadable health file is a notice on both sides (A4; RED); `consultantHealthPage.test.ts` — every side block opens with an `h4`, and no health block carries `aria-live` / `role="status"` (P; both RED); `consultantHealthHost.test.ts` — a landed check is announced once, a refused confirmation announces nothing, and every landing has its sentence (teeth by removing the announce); `consultantHealth.test.ts` — the other side by its sighting and the published margin, this side's own disk by its stamp, and every state and canary reading of `shared/consult-check-words.json` worded (J; teeth by deleting a state's words); `consultantHealthRead.test.ts` — a heartbeat keeps its first sighting until it moves (teeth by always-now), and only this side reads its outcome files. `consultantsLiveContract.test.ts` runs the BUILT `coai-mcp`: `--consultants` answers the four caller kinds with `heartbeatStaleAfterSeconds`, a bad `--check-consultant` request is 65 → refused, an unknown mode is 64 → too old (skips when the server is not built). The harness learned two things for it, each with its own test: a button's drawn text (`textContent`) and the `[data-command="name"]` selector. NOT covered: an OLD binary answering 64 to `--check-consultant` (there is none here — the two runnable halves of that rule are), the `vscode` composition (`consultantHealthPanel.ts`, wiring only, in the Sonar coverage exclusions), and the live paid check (E5.5, the operator's) |
| A phrase's colour, in both places at once | the decision by calling it, plus a CROSS-SURFACE assertion | `phrases.test.ts` — `phraseColours` is tested by calling it: three ids get three colours AND a second allocator over the same list answers the same, because "all different" alone passes for an allocator that is unstable between builds. Two properties are pinned that the plan round predicted the opposite of: **twelve phrases still get twelve colours** (the anchored vendor slots are offered last, not withheld, so the first repeat is the thirteenth) and that **a NON-COLLIDING addition moves nobody** (a name's slot comes from a hash of the name itself). The collision case is pinned too, and it is real: adding `phrase-11` to two phrases moves `phrase-2`. So the allocator is not subset-stable, the cross-surface promise rests on both surfaces reading one saved list, and the test says so at the smallest list that shows it. `phrasesSection.test.ts` carries the one that guards the feature: the same phrase's colour is the same STRING in the editor page and in the panel — and a companion renders the two surfaces **one phrase apart**, because they are separate webviews on their own clocks and a shared phrase must not change colour because of a phrase it is not. `phrasesPage.test.ts` asserts every row in an eight-phrase list wears a distinct colour (not merely "a colour", which a page painting all eight the fallback would satisfy), that `.phrase` still carries its border and 3px edge — without which the inline hue paints nothing — and that each label's `for` names a control `id` that exists, which is what makes it a label rather than a caption. NOT covered: that any of it is visible. No layout engine — see *What this does NOT prove* |
| Clearing a chat row from the spending chart | decisions, the page's own CLICK, and the wiring structurally | `chatSpendRows.test.ts` — `rememberedChat` by calling it: a record AT the mark is forgotten and a later one kept (the `>` the reviewers' `remembered()` uses); forgetting one model leaves the vendor's other models and the same model under another vendor alone, which is what fails if the key collapses to the provider; and **what a line wrote down decides its row, not the preset list as it is today**, which is what fails if the filter resolves through `vendorOf` alone. Both key rules were proved by reverting: collapsing the key reddened the second, and dropping the written-down vendor reddened the third **and a pre-existing chart test**, which is how the extraction was shown not to have changed the chart. `forgetAChatRow.test.ts` **runs the page**: it captures the listener the rounds-log page registers on `document` and calls it with an event whose target answers `closest`, asserting the press posts the vendor AND the model — this caught a real defect, the page's handler posting only `id` while the markup carried `data-model`, and reverting that field reddens exactly this test. The same file holds the markup pair (chat control present, reviewers' control still present), the unnamed row having no control, a model id containing a quote being escaped rather than forging an attribute, and — as a source assertion, because `panelProvider.ts` needs a host this suite has none of — that BOTH ledgers pass through the filter and that a machine which has never forgotten anything reads an empty map. NOT covered: the modal itself, and that a mark survives an extension restart |
| Add a reviewer from the CATALOGUE | decisions, plus the two API flags structurally | `vendors.test.ts` — that the entry a person searching for *Claude Code* filters on carries those words **selected by `id === 'claude'`**, because an assertion that some label contains them stays green while that entry keeps its old name; that `presetsOffered` returns the catalogue WHOLE whatever is already configured, giving a taken id the next free `<id>-2` and marking it a second row, with the un-taken case asserted in the same test so it cannot pass against a function that marks everything; that a blank base stays blank, which is what keeps the custom-endpoint flow asking for a name; and that `reviewerPickItems` puts the new id in the `description` and leaves it empty otherwise. `addAReviewerFilters.test.ts` covers what no pure test can reach — `matchOnDetail` **and** `matchOnDescription` on the pick's options object, matched as a whole object rather than as two loose substrings, and the old `VENDOR_PRESETS.filter(` asserted GONE rather than the new call asserted present. NOT covered: the quick pick itself. Nobody has driven the real list, so that a person typing into VS Code's filter box sees the entry is inferred from the flags, not observed — an extension host is the standing gap named at the end of this file |
| Add a reviewer FROM the vault (`!name`, S3.6) | decisions only | `apiKeyVendors.test.ts` — only NAMES survive the providers parse (a map of names to values keeps nothing), the `!` rows are exactly the vault's names, a key already used still listed with the id the second row takes, an unreadable vault or a server too old to name its keys is ONE row saying why, choosing `!qwen` writes `runtime: 'api'` with `vaultKey: 'qwen'` that survives `vendorsFrom` and crosses as `key`, the two presets, and the probe's `GET /models` ids read or a reason given. NOT covered: the quick pick itself and the `--probe-api` spawn behind it — an extension host is the standing gap |
| An api reviewer's own settings on its card (thinking, effort, review limit, S3.8) | decisions, the card RUN, the SEAM, and the released halves | `apiSettings.test.ts` — the providers report parsed defensively, the three fields stored only when said, the wire (api rows only, held back for a server known older than `API_SETTINGS_SINCE`), a value equal to the reported default stored as nothing, refusals, the reset id; `apiSettingsCard.test.ts` — the panel RUN from reports the binary printed: the effort options are exactly the module's levels, the default marked, the switch present or replaced by a sentence, the minutes box, the writes the page posts, a reset click, the refusal and set-aside note, both hidden states; `apiSettingsHelp.test.ts`; `npm run test:seam`'s seventh leg (the row's values come back from the real binary as `effective`); `scripts/live-api-settings-compat.mjs` against the released halves (run by hand, not in CI). NOT covered: the host's `writeApiSetting` / `resetApiSetting` — an extension host is the standing gap |
| A Mistral Vibe row on the panel — preset, models, ▶ ⤓ ⟳ per OS, the older-server gate (S2 of `todo/PLAN_vibe_and_kimi_runtimes.md`, 2026-10-13) | decisions, the card RUN, and the RELEASED server (by hand) | `vibeRuntime.test.ts` — the round trip, the preset (never a default), the curated aliases and their caption, ▶ as plain `vibe` with `VIBE_ACTIVE_MODEL` and `VIBE_CLI=python` in the terminal's environment, ⤓ and ⟳ for win32, linux and darwin and their official sources, positive agreement with coai-mcp's `InstallCure` and `VibeModels.Curated`, the PyPI version source, the row withheld from a server older than `VIBE_RUNTIME_SINCE` and the card's note, an older build standing down before a newer settings file, the Models card RUN against a new and an older server, the effort refusal, the Setup CLI table, the help in five languages. RED first against stubs: 23 of 26 failed with the real symptom (`codex -m mistral-medium-3.5`, `npm install -g @openai/codex`, the vibe row reaching 0.52.0, the Gemini list), 26 of 26 green after. `scripts/live-vibe-runtime-compat.mjs` (`npm run test:vibe-runtime-compat`, needs `gh`; not in CI) against the released mcp-v0.52.0 and this checkout's Debug server — run 2026-10-13 on Windows, exit 0; teeth: the gate forced open in the compiled output → three FAILs. `vibeWindowsInstall.test.ts` (7) — the Windows ⤓/⟳ as ONE PowerShell line, held to PowerShell's own parser (it runs nothing): `Get-Command uv` first, Astral's installer only when missing, uv called by the path the installer used in its own folder order, `& $uv tool install mistral-vibe` then `& $uv tool update-shell` and the session PATH; `fromOfficialSources` checking every URL and package of a multi-step line; the admitted sources pinned. RED first 6 of 6 against the two-step line; the matcher's every-package-every-step test (the epic 2 risk consultation's cases: a second package, `@npm:` / `@file:` aliases, `--from`/`--with`/`--index-url`, a path or wheel, a second step through pip or `npm i`) RED first with six of them wrongly admitted. `vibeWindowsInstallRuns.test.ts` (Windows only, 8) — the Windows line RUN in Windows PowerShell 5.1 and pwsh 7 against harmless substitutes (a temp folder with spaces, a `powershell` function standing in for Astral's installer behind a guard, a fake `uv.exe` compiled from C# that logs its arguments): (a) spaces in the home and in `UV_INSTALL_DIR` work and the session PATH gets the tool folder; (b) an installer failure and a failing `tool install` / `tool upgrade` each STOP the line; (c) a uv already on PATH is used and Astral's step does not run. RED first: the three (b) scenarios against the line without its stops. NOT covered: a real install by the buttons on any side (nothing was installed — the line runs only against substitutes); a `uv.ps1`/`uv.cmd` found on PATH first; the ▶ terminal's environment reaching a real terminal (`panelProvider.runVendor` is host wiring; its decision is `vendorTerminal`); an extension host |
| A row coai-mcp runs WITH a warning — `reviewerStates[].warning` on the round line, `providers[].warning` on the card (2026-10-13, the owner's warn-not-refuse for Vibe) | decisions, the sidebar and the card RUN | `aWarnedReviewerIsShown.test.ts` — a DONE reviewer's line ends in `warned: …`; blank, whitespace and non-string warnings read as before; the warning read from a session file reaches the rounds-log line; the sidebar's running round shows it under its own reviewer only; `parseProviders` keeps a non-blank `warning` and adds no field otherwise; the Models card of an AVAILABLE row draws **Runs with a warning:** with no "cannot review"; a row without one draws nothing; the help in five languages. RED first 5 of 7. NOT covered: a real server's warning reaching a real editor (the server's half is `module_server.md`'s) |
| The Security lane's settings, across the seam | decisions, the page RUN, and the SEAM | `securityLane.test.ts`, `securityLaneMalformed.test.ts` run the Settings page: an older server cannot switch the lane on, malformed pairs leave it off and the page rendering, stored values stay inside their controls. The eighth leg of `npm run test:seam` (`scripts/seam-security.mjs`) hands the extension-serialized lane to the built server and asserts its `--providers` complaints: none for a valid lane, one for an unknown trigger, one for a malformed lane. Not covered: a paid reviewer answering a lane prompt — that is `SecurityLaneCalibrationTests`, explicit and hardware-bound. |
| Add a reviewer FROM a Team server | decisions only | `remoteVendorRow.test.ts`, `remoteVendorCard.test.ts` — the row shape, the catalog-fed model list, the canonical server match, and the fields a remote row must not show. The quick-pick itself needs an extension host |
| A reviewer the server cannot run, badged on its card | decisions only | `providersBadge.test.ts` — the three states and that only `unavailable` draws anything, the parser against every shape a different build could hand it, and a row the server did not mention reading as UNKNOWN rather than as fine. The spawn of `--providers` itself is the row below |
| Team-server spending, and *Company* | decisions only | `teamUsage.test.ts` — the per-server block, the admin gate, and what a server that has not answered renders as |
| The spending tab's money LINE BY LINE, and tokens and money per vendor row × model × kind (story 2.5 of `todo/PLAN_every_round_is_counted.md`, 2026-10-10) | decisions, the card's text, and the help | `usagePricing.test.ts` — `TheParser_KeepsCachedReasoningAndKind` (an old line: 0, 0, `review`), `APastLine_IsPricedAtItsOwnModel` (and the row's typed rate still winning), `CachedInput_IsPricedAtTheCachedRate` (and a list with no cached rate pricing it at the input rate), `ALongContextTier_AppliesPerLine`, `AReportedCost_IsNeverReEstimated` (`$0.4200 + ~$3.00` through `spend`), `AnUnpricedLine_MakesTheTotalAFloor` (an unpriced and an uncaptured line each `≥`, a whole row not, nothing priced a dash). Each went red on the unfixed code with the real symptom (*expected 1.1, got 2*; *expected 0.56, got 2*; *expected 2.7, got 1.8*; *expected 3, got null*; *an unpriced line was counted as free*; *the cached subset was dropped*), and each was re-reddened by reverting its one production line in the built `usage.js`, `priceOfLine`'s line-model lookup included. `roundMoney.test.ts` — grouping and the window (a Team-server row apart), the four token columns, `AnUncapturedLine_MakesATokenTotalAFloor_NeverZero`, `ARepairedTurn_IsOneRecordedTurn_NotOneLaunch`, the money's three states, `APerRoundFigure_CountsEveryRound_AndCarriesTheFloor`, `AModelLine_DividesByTheRoundsThatUsedIt`, `AConsultationOnlyWindow_ShowsNoPerRoundFigure`, chat turns as kind `chat`, and a parsed real-shape line; each guarantee re-reddened by removing it from the built module. `helpCoverage.test.ts` asks all five languages' spending article for `**≥**` and its example. NOT covered: the provider handing the tab every model the ledger names (`modelPrices(vendors, ledger)` needs an extension host), and the Rounds block, card line and `readRoundCounts` that will consume `roundMoney.ts` (stories 2.2–2.4) |
| Mirroring the settings to the file the server reads | decisions, the CONTENTION, and the SEAM | `settingsSync.test.ts`, `settingsLock.test.ts`. Covered: that a change reaches the file with no panel open; that an older build stands down from a newer stamp and says so once per version; that a file which cannot be READ is not a file that is not there; that the read, the comparison and the write happen in that order inside one section; that two syncs over one lock and one file interleave to exactly one writer and one whole payload; and when a lock left by a dead window may be broken, including a clock that went backwards. NOT covered: the lock itself — `fs.open(…, 'wx')`, the owner token, the stale break and the atomic rename all need a real filesystem and two real extension hosts, which is the row below. **The seam IS covered live**: `npm run test:seam` (`scripts/run-seam.mjs`) writes the file with the extension's own `serverSettingsJson`, signs a throwaway data directory in at the token path both sides derive independently, serves a catalog from a loopback HTTP server, and runs the REAL `coai-mcp --providers` against it — asserting the Team-server row resolves under the name its SERVER knows. Watched failing with `remoteVendor` removed from `vendorsEnv`, which is the defect of 2026-09-07 reproduced by the two implementations rather than described by a fixture |
| A SIDE's settings file and log root, across the seam | decisions, the shared vectors, and the SEAM | `TheSettingsFileKnowsItsSideTests.cs` + `TheSideAdoptsTheRootSettingsTests.cs` (server), `dataDirAgreesWithTheServer.test.ts` (extension), `DataSideVectorTests.cs` over the shared `shared/data-side-vectors.json`, and **leg five of `npm run test:seam`** (`scripts/run-seam.mjs`). The defect, 2026-09-18: `COAI_DATA_SIDE` partitioned `coai.db`, `sessions/` and the tokens while `settings.json` and `logs/` stayed in the root, because `SettingsFile.DataDirFrom` was a SECOND resolver with no side and no trim — two installations sharing one NAS overwrote each other's configuration in silence. Covered as decisions: that the two resolvers agree for every shape of input INCLUDING refusing alike (a property over dir × side, not a list of answers); that the log root moves through the same call and no rule of its own; and the whole adoption protocol — the root file surviving, a kill before publication leaving adoption still possible, a root file that does not parse being named and left, and the RACE, in which the other window publishes in the one instant between this side's check and its rename (a hook, because the first version of that test created the destination first and so never reached `File.Move(overwrite: false)` at all — it passed with the guard deleted). Watched failing with `overwrite: true` planted: *the loser of the race overwrote the winner's file*. **And that the sentences reach a caller**: `Layer` takes a required sink, because the first build adopted and dropped what it returned while the comment beside it claimed startup logged it — six reviewers across three vendors found it; planting the discard turns two tests red with *the collection is empty*. The `settingsPath`/`logsPath` of every vector are asserted by BOTH suites, refused cases included, since the DIRECTORY alone could not see this defect — the directory was already right. **Leg five is the live half**: the extension's own `coaiDataDir()` chooses the path, the REAL binary is started with the same two variables, and it must find the row — agreement by CONSEQUENCE rather than by comparing two strings, which is what a fixture cannot do. It also proves the root adoption against the real binary and that the root file survives it. **NOT covered**: two machines actually mounting one NAS |
| **The two notice serialisers, over one corpus** | decisions, and the two implementations against EACH OTHER | `npm run test:parity` (`scripts/run-parity.mjs`), driving `NoticeTool` — a test-only executable built by the solution and never published, the FakeCli pattern. It exists because `server-notices.jsonl` is written by C# and read by TypeScript and BOTH halves redact: two suites that each compare themselves against their own expectations can be wrong in the same direction, and here that is a secret written by one half that the other would have removed. `src/test/noticeShapes.ts` generates the corpus — every secret shape crossed with eleven wraps including Russian and German prose, quotes, an emoji and a tab; control characters by CODE POINT; each length boundary; a surrogate pair straddling the cut; and values longer than a pattern's own bound. **346 shapes at both limits, compared as UTF-16 code units**, plus three whole records and the fourteen codes. **What it compares and what it cannot**: `safeText` byte for byte; the LINE through the extension's own parser, because `JSON.stringify` writes properties in the caller's insertion order and C# has a fixed list — the first version demanded byte equality and reported 4408 identical units in a different order. So the line is asserted to parse, to agree field by field, and to be a FIXED POINT of the parser. **Watched failing twice, both times by this harness rather than by reasoning**: the transport's first version sent strings through `UnsafeRelaxedJsonEscaping` and turned the lone surrogate `slice` leaves at a cut into U+FFFD (the port was right, the transport was lying); and replacing the redactor's ASCII lookbehind with .NET's Unicode `\b` left `token abcdefghijklmnop` and `basic YWxhZGRpbjpvcGVuc2VzYW1l` unredacted after Cyrillic text while the extension removed them. **NOT covered**: that either half is RIGHT — it proves they are the same, and `notifications.test.ts` is what says the contract is what it should be |
| The Consultant section — how the SERVER resolves what was stored | yes | `ConsultScenarioTests` (the five B3 scenarios, against the real service and the stand-in CLI) with `ConsultantResolverTests` beneath it. Covered: that a consultant DEFINED with no reviewer row at all still resolves, and `settings.Providers` is never read for it; that a definition on a runtime no consultant may run on — `remote` above all — is refused BY NAME **before any `ProviderSettings` exists**, which is the invariant that a working tree is never routed at a Team server; that a LEGACY reference still works with no rewrite, through a row matched case-insensitively and borrowed from **enabled or disabled** (the "switched off" refusal is gone, and that reviewer flag is a fact about reviews), else through an id that is itself a consulting runtime, else refused by name; that rule (a) is asked BEFORE rule (b), proved on `codex`, the id that is both; that a runtime is recognised without case on BOTH halves and travels in the allowlist's spelling; and three guarantees about a RESUMED consultation — it keeps the vendor, model and runtime frozen on its record, it is refused when its own frozen runtime is one no consultant may run on (whatever today's settings say), and the definition that lends it an endpoint is the one under its OWN caller kind rather than whichever the map iterates first. Each of those three was watched failing with its guard removed; the caller-kind test uses a SORTED map on purpose, because with a plain dictionary the absence of the guard shows up only some of the time. **NOT covered:** a real vendor CLI answering — that is the fake-CLI scenario and the live runs in the plan. The definition CROSSING the seam was this row's other gap until story B4 (2026-09-15): it is now the seam's leg four (the row above), with its measurement against the released server half in `scripts/measure-consultant-skew.mjs` and `module_server.md` |
| The Consultant section — what an edit STORES | decisions only | `consultant.test.ts`, `panelServerDefaultsAgreement.test.ts`. Covered: that a legacy `{vendor, model}` entry resolves into a definition on READ and writes nothing (rule (a) a reviewer row enabled or disabled, (b) an id that is itself a consulting runtime, (c) unavailable, preserved raw with its reason); that the first EDIT stores that definition, so the consultant stops following the reviewer row; that a vendor change clears the model and lands the one the new vendor will really use, while re-sending the vendor already chosen changes nothing; that a blank id, an unknown setting key and an unknown caller kind each write nothing; that an unplaceable entry gains no invented runtime and refuses an endpoint; that a field or a caller kind this build cannot name survives an edit beside it; and that the shipped four pairs agree across all THREE copies — the C# `ConsultantRouting.Shipped`, `DEFAULT_CONSULT`, and the manifest's `coai.consultants.default`, which nothing was reading until 2026-09-15. **NOT covered: that `panelProvider` hands the write the SIDE-AWARE reviewer rows.** The unit tests prove the function materialises from the rows it is HANDED — two different row sets give two different results — and cannot see which rows the one line above them read; observing that needs a running extension host, as it does for the chat rows below. The controls for base URL and CLI path do not exist yet (story C5 of [PLAN_the_consultant_has_its_own_vendors.md](PLAN_the_consultant_has_its_own_vendors.md)); the write path accepts their keys already |
| Chat with other AI — the trigger | decisions only | `chatSettings.test.ts`, `chatCapture.test.ts`, `chatModels.test.ts`, `chatWiring.test.ts` — every settings fallback; both doors and all three `chatAutoSend` values; the clipboard borrow including the case that must NOT restore, the unreadable clipboard that must not be written to, and the marker collision; which models may be asked and which are refused by name; and the manifest wiring itself — that the command, the keybinding and the `webview/context` item all name the id `extension.ts` registers. NOT covered: the synthetic `Ctrl+C`, which needs a real webview with a real selection — it was MEASURED with a probe extension instead (638 characters, 1725 ms) and the numbers are in `module_extension.md` |
| Chat with other AI — the conversation | decisions only | `cliChatSession.test.ts`, `chatPage.test.ts`, `chatPanel.test.ts`, `chatMessages.test.ts`, `chatPrompt.test.ts` — the four ways a long-lived child ends and what each says; the page as it renders; the host boundary that refuses a model the conversation was not offered; and the two turns a model is ever sent: the opening one with its fenced passage, and the CARRIED one that hands a whole conversation to a model that never heard it (questions and answers both, attributed, fenced with a per-turn id, bounded at 60 000 characters with the loss stated). NOT covered: the switch itself — replacing a live session under an open panel is `vscode` state, and it belongs to the row below |
| Chat with other AI — what a turn COST | decisions, and the file | `chatUsage.test.ts`, `chatUsageFile.test.ts`, `chatRows.test.ts` — that a cumulative vendor's numbers are differenced against what it last said and that the rule is keyed on the RUNTIME rather than on a vendor row a person can rename; that money is differenced by the same rule so the two cannot drift apart; that a turn which was stopped or failed is still written down; that a turn nobody reported numbers for reads as UNKNOWN and never as free; and, against a real temp directory, that the ledger appends rather than truncates, that queued writes land in the order they were asked, that a half-written tail costs itself alone, and that a ledger which cannot be written costs the record and never the person's answer. On the log side: the Kind column, the Kind facet, the merge of the two ledgers by time, and that a chat row leaves the columns it has no answer for empty. NOT covered here: several PROCESSES appending at once, which a unit test cannot fork and mean anything by — measured instead, see the row in *What this does NOT prove* |
| Chat with other AI — WHAT IT COST ON THE SPENDING PAGE, and HOW OFTEN IT WAS REACHED FOR | decisions, the wiring, and the page | `chatDoors.test.ts`, `chatDoorsFile.test.ts`, `chatSpendRows.test.ts`, `chatSpendSection.test.ts`, `chatWiring.test.ts`, `chatPresetsPage.test.ts`. Two counts that no turn ledger can answer — *Asked* (`take the question` + `add the question`) and *Opened* (every door) — because a turn is written when a turn FINISHES and `add the question` finishes none. Covered: the door record round trips and a torn line costs itself alone; **that a door line would be read as a TURN by the other ledger’s parser**, which is why it lives in a file of its own and is asserted so that moving it back breaks a test rather than a page; that a door this build has never heard of still counts as an opening but not as a question; the arithmetic — per vendor AND model, the window bounding tokens and both counts but never the all-time column, a bill and an estimate never summed into one number, a row that is part billed and part not keeping BOTH, a cost nobody could have charged treated as no cost, a record whose instant cannot be read belonging to no window, a door that resolved nothing landing in its own named row, and the section total counted from the ledger rather than from the rows that survived the filter; that the whole ledger is read ONCE rather than once per row, counted rather than timed; and the rendered tab — both headings, the rule between them, a total each, and the counts in the row. **The command wiring is asserted against the SOURCE of `extension.ts`, with the door list DERIVED from the manifest’s menus and keybindings**: each door records exactly one invocation, before the work that can refuse it, and nothing records a door the manifest does not offer — so a sixth way into a chat fails this rather than quietly counting zero. Against a REAL directory, like the turn ledger beside it: that the line reaches the file and comes back, that a second invocation is appended rather than written over the first, that queued writes land in the order they were asked for, that a directory nobody has written to yet is made rather than refused, that a half-written tail costs itself alone, and that a ledger nobody has written is no invocations rather than a failure. **And that the vendor is the one WRITTEN DOWN**: a preset is a row somebody edits, so resolving an old line through the list as it stands today would move a year of history to another vendor - the recorded value wins, and the resolver answers only for lines written before the field existed. NOT covered: the commands through a real extension host, which is the row below |
| Chat with other AI — WHERE A HANDOVER STARTS | decisions, the wiring, and the page as it ships | `chatCarry.test.ts`, `chatWiring.test.ts`, `chatPage.test.ts`, `bundledPage.test.ts`. A conversation is handed whole to a model that never heard it in five places — a Team server every turn, a model switch, a re-ask, a vendor that lost the thread, and the first turn after a reload — and **Carry nothing above** moves where that handover begins. Covered: the position itself (past the end carries nothing; a negative one never reaches `slice`, where it would be an offset from the END and carry the last message instead of the suffix; nothing but a finite non-negative integer is a position at all, from the page and from the store); that ALL FIVE consumers go through the one function, counted structurally rather than listed, so a sixth added later without the mark fails; that a mark set AFTER a switch reaches the conversation that switch had already staged, which is the case the feature exists for; that the mark only moves forward, since the button is offered on the last answer alone and a lower position is a stale page or a forged message; that a re-ask brings the mark back with the transcript it truncated; and, in the SHIPPED bundle, that the button posts the index it names through the delegated listener — with a `closest` stub that honours the real selector, because one that did not made this very test pass against a button that could not have been pressed. NOT covered: an actual Team-server turn or model switch end to end, which needs the extension host in the row below; what is asserted instead is that both build their carry through the one function |
| Chat with other AI — FINDING A CONVERSATION AGAIN, and forgetting one | decisions, the wiring, and the rows | `conversationPicker.test.ts`, `conversationChoice.test.ts`, `conversationPickerWiring.test.ts`, `chatStoreCache.test.ts`, `chatStoreSweep.test.ts`, `chatStoreFile.test.ts`. **CoAI: switch conversations…** lists what is open here, what another window holds, and what is only on disk. Covered: the rows and their order, with Open first and an open conversation never repeated among the closed ones; that only a conversation carries an id, so a notice or the cut line is not something an accept could open; that a conversation another window announces is drawn as `elsewhere` and is neither reopened nor forgotten, while this window’s OWN heartbeat is never read as somebody else’s and a window that has gone quiet stops holding its conversations; that what is TYPED is matched BEFORE the hundred-row cut, without which the hundred-and-first conversation could not be found by typing its own title; the folder label shown only when folders differ, widened until two projects of one name are told apart; every sentence a failed choice produces, with `gone` taking the row away and `unreadable` keeping it; and, against a REAL directory, that forgetting removes the index entry and RENAMES the transcript into the quarantine under a dated name the sweep already retires — and says so distinctly when there was no transcript to archive. **The widget itself is asserted against the SOURCE of `conversationPickerCommand.ts`**: that it uses `createQuickPick` and never `showQuickPick`, that the list is rebuilt rather than reopened by a forget, that the picker is not closed until a tab is actually on screen, that one accept and one forget run at a time, and that the manifest’s chord carries a macOS form. NOT covered: the QuickPick itself — item buttons, the title button and `Alt+Delete` inside it are `vscode` state, which is the row below |
| Chat with other AI — WHICH CONVERSATION BELONGS TO THIS TAB | decisions, the store operation, and the wiring | `chatSource.test.ts`, `chatStoreRefile.test.ts`, `chatSourceWiring.test.ts`. A conversation gets a durable identity — a file’s uri or a Claude session’s id — and the workspace root it is filed under, captured when that identity is. Covered: which root a path belongs to (longest wins, because roots nest; segment boundaries, so `coai-old` is not inside `coai`; blind to case and separator, since paths arrive from two programs); what a rename moves, including a renamed FOLDER reported as one entry and the closest of two overlapping moves; a session id read off either platform’s separator and refused for anything that is not one of those files; and, against a REAL directory, that a moved file takes its conversation with it — source and root in ONE revision, nothing else rewritten — that a record whose source has moved on is left alone, and **that the store refuses to WRITE a record it could not read back** (a contradictory origin pair saved happily and was then unopenable for ever). **The wiring is asserted against the SOURCE** of `chatCommand.ts`, `chatCapture.ts`, `chatPersist.ts`, `chatSessionJoin.ts`, `chatFollow.ts` and `extension.ts` — the command file was split into fifteen modules on 2026-09-17 and each assertion followed the function it watches, proved by pointing it back at the old file and watching it name its own symptom: that a record is written with the conversation’s own source rather than a placeholder, that the pin captures the folder the session was found in and writes explicitly (the page’s dedupe guard would never have carried it), that all three writers share one queue, that the uri comes from the MATCHED tab rather than whatever is focused, that a restored conversation which lost its source pins again, and that `onDidSaveTextDocument` is NOT listened to — the editor reports no previous uri for a saved untitled buffer, so a listener would attach a conversation to the wrong file. NOT covered: the rename event, the tab registry and the panel end to end, which need the extension host in the row below |
| Chat with other AI — WHAT THIS TAB ASKED, and WHICH SESSION IT IS | decisions only | `claudeAsked.test.ts`, `claudeQuestion.test.ts`. Which rows of a Claude Code session file count as the person speaking, and which session a tab belongs to. **The names**: a session wears more than one — `ai-title` from the model and `custom-title` from a person renaming the tab by hand (and from Claude Code deriving one off the first prompt) — and until 2026-09-16 only the first was read, so a renamed conversation was reported as not existing while it sat in the editor group beside the refusal. Covered: a conversation found under the name it now wears and under every name it used to; the rename that Claude Code PUTS BACK minutes later, found under both; a session with no `ai-title` at all; that a session wearing a name TODAY beats one that wore it once, and that two sessions which both merely wore it are still the refusal; that the pin keeps answering whatever the conversation is called; and that *Take the question* answers the same fixture the Asked button does, because the two share `namedAmong` and one `collectNames()` rather than each keeping a copy of the rule. Also: the shortened tab title read as a prefix, a prefix that fits two sessions refused, every named refusal saying which directory it looked in, and a window with no folder open looking in the home directory. NOT covered: the button through a real extension host, which is the row below |
| Chat with other AI — AGENT MODE (issue #289) | decisions, the page RUN, the seam, and a live script | `chatAgentMode.test.ts`, `aQuestionCrossesTheSeam.test.ts`, plus the structural guards it moved (`chatWiring.test.ts`, `chatFresh.test.ts`, `aConversationSurvivesAReload.test.ts`). Covered: text mode's argv byte-identical to before; each vendor's agent argv (claude `bypassPermissions`, codex's sandbox bypass on a first AND a resumed turn, agy out of plan mode with slash commands still off); the working directory by mode; the refusals — no workspace, a deleted folder, a path that is a FILE, a remote row — in the same words the box uses; `NoDefaultCurrentDirectoryInExePath=1` on every agent launch and nothing added to a text one; the 20-minute budget; the pure rules (`chatAccessRules`); the record (absent = text, `agent` round-trips, anything else is a record this build cannot trust); the command (a real boolean or nothing); the box's markup; and, with the page script RUN, that a tick posts a command the parser reads, that the page NEVER ticks its own box (the host de-duplicates pushes, so a cancelled confirmation would otherwise leave it ticked — found by our own code review, red before the fix), and that the host's push ticks, disables and hides it. Every new guard was proved by breaking it. **Live:** `npm run test:live-agent` (`scripts/live-agent.mjs`) — create, translate, read OUTSIDE the workspace, run `git --version`, per vendor, checked on disk or in the answer; agy, claude and codex all four on 2026-09-23. NOT covered: `switchAccess` itself (`chatAccess.ts`), the modal and the relaunch — they need a `vscode` host, so the compiler and the pure rules carry them |
| Chat with other AI — BACK TO WHERE IT CAME FROM (issue #314) | decisions, the page RUN, the wiring, and a real editor | `chatReturn.test.ts` — every arm of `backTo` (the live tab winning even over a pinned session, a Claude source by its session, a file by its file, `none` going nowhere), the eight fixed focus commands and nothing past them, a tab at index -1 not attempted, every refusal naming its subject. `chatPage.test.ts` — the `<body>` carries the conversation's id in the shape the command reads, and *New chat* and a fork rewrite it (the harness's body learned `setAttribute`; removing the rewrite turns the reset case red). `chatWiring.test.ts` — the command is allow-listed as acting INSIDE a chat, checked both ways (a chat-opening command scoped to our page would be caught, and was, by planting one). `chatReturn.test.ts` also pins `recoveredTab` (a file by its uri, a no-session Claude chat by its ONE same-named Claude tab, never two, never a document tab by name, never a recorded session by name) and `bufferGone`. `test:host` — four scenarios: the editor's recipe really brings the source tab forward (an off-by-one index turns it red); a tab MOVED to another group is still found (its `Tab` object is replaced — measured — and the scenario was red before `recoveredTab`, opening the file twice); a stale right-click refuses while the chord acts on the active chat (red with the old fallback); and a chat with only a recorded file opens that file. NOT covered: the session route through `claude-vscode.editor.open`, because the harness has no Claude Code installed, and a right-click in the live webview |
| The roles page — A ROLE IS NOT SWITCHED ON WITHOUT A QUESTION (issue #338) | decisions, the markup, and the server's sentence | `aRoleNeedsAQuestion.test.ts` — `whyNotAskable` over a shipped role, one's own with text, blank text, text on a later prompt only, no prompt, no name; a new role created switched off; switching on refused by NAME without text and stored with it; switching off never refused for text; a shipped role switched on without text of its own; the page's mark on an active unaskable role, and its absence for one with text or switched off. `rolesEdit.test.ts` — the old guarantee (switched on when there is room) rewritten to the new one. Server: `TheRoundSaysWhatItCouldNotAskTests.ARoleThatCannotBeAsked_IsNamedByItsName`. Every guard proved by breaking it. NOT covered: the host's `textsFor` read (it needs `vscode`), and no scenario runs a real round with a role a person added — each half is tested, and the settings mirror between them has its own tests |
| The real binary's review trees, isolated from the machine's (issue #544) | the root, and the live contract | `TheReviewTreeRootCanBePointedTests` (src_mcp) — `COAI_REVIEW_ROOT` absolute is the root, trimmed; relative or blank is ignored and said, naming the root in use; unset is the machine-local default and says nothing (red first). `bugzLiveContract.test.ts` — `runIn` points `COAI_REVIEW_ROOT` inside the test's own temp data directory, so *"the real --trees answer on a machine holding none is a list"* no longer reads this machine's trees: it was red on a machine holding a live `dew_flow_conventions` tree, red again with the variable set but ignored by the server, and green after. NOT covered: nothing — the test ran before only where `src_mcp` was built, and now holds there too |
| Review rounds — A PERIOD SWITCH ON CONVERSATIONS, CONSULTATIONS AND WHAT IT KEEPS MISSING (2026-09-25) | the periods, the reader, the live server, the page script run | `logPeriod.test.ts` — the spending tab's four then All, the same starts, the instant sent in UTC, only a real period is one, client buttons filter the page and the spots buttons ask the host. `spotsPeriod.test.ts` — `--since` is sent and All sends nothing; the ECHO says it was applied and its absence that it was not (red first); `spotsPeriod` is a command and junk is not; the row with its mark, the older-server sentence, none for All, the row on an empty tab too (red first); the host links pinned whole — panel → hook → store + forced read, the cache forgetting and reading over the chosen period; and LIVE against the built coai-mcp: the echo arrives through the real binary and reader on an empty data directory, and a malformed period exits 65 (the query itself is `ALogSplitByPeriodTests`'); an empty period says so; the cache drops a read overtaken by a press and never serves another period (pinned). `roundsLogPaging.test.ts` (its strict harness taught the period selectors) — a Conversations press sets `#from`/`#to` and marks itself, a hand-typed date unmarks it; Consultations hides rows outside the period on every push and press, All shows the unreadable one too, an empty period says so; each row sits where its filter reaches; every consultation row and its alert row carry `data-started`. a spots press marks itself at once; an empty period hides the table too. Plants, all compiling and all red: the hide disabled, the post-push filter removed, the Conversations range left empty, `markPeriod` reading `data-period` alone, the table left shown. Server: `ALogSplitByPeriodTests` (`module_server.md`). NOT covered: the layout itself, checked by a headless-Edge screenshot |
| Review rounds — THE CONSULTATIONS TAB FOLDS ITS LONG FIELDS (2026-09-25) | the markup, and the page script run | `consultationFold.test.ts` — a long field is one closed `<details data-fold="<id>:<field>">` with the whole text inside; the summary is the first line capped at 160 with `…`, also when that first line is longer than the cap; a short one-liner (160 exactly included) stays plain, an empty one is empty; a short field with a line break folds — `\r`, `\r\n`, U+2028 and U+2029 too — and one whose only break is at the end does not; the summary skips a blank first line; the text and the key are escaped on both roads; the table folds each long field under its own key. The page script RUN against a DOM stub: an opened fold (under an id with quotes and markup) stays open across a `consultations` push, a closed one stays closed; a click on a summary posts nothing, walked up a REAL chain (summary › details › td › tr) by a selector-honouring `closest`. Nine red first; four compiling plants red — the re-open set to `false`, the length threshold dropped, the trailing trim dropped, the blank-line skip dropped. NOT covered: the layout itself, checked by a headless-Edge screenshot closed and open |
| Chat with other AI — A PROMPT BUTTON ADOPTS PASTED TEXT (issue #538) | decisions, and the wiring | `chatPrompt.test.ts` — `instructedBox`: pasted text gets the question on top and survives whole as the material (red first: *"the box holding pasted text was left alone, which is issue #538"*); a second press swaps only the question; pasted text that QUOTES our lines survives two presses; a model button (`'leave'`) still leaves it alone; an empty, whitespace or still-ours box is rebuilt around the captured passage for both; a hand-edited instruction is still swapped at the service lines; with `'adopt'` no box shape is left alone; a paste that merely BEGINS with the current question ("Explain why…" while *Explain* is in force) is adopted, not swapped into "Summarize why…" — `reinstructed` now needs our instruction to end its line (red first, red again with the bare `startsWith` planted; our own code reviewer). `chatWiring.test.ts` — the whole `writeInstruction(…, 'adopt')` call inside `onUsePrompt` and `(…, 'leave')` inside `chooseModel` (red first). Two plants, both compiling: `'leave'` in `onUsePrompt` reddened the wiring test; an adopt branch that matches nothing reddened four decision tests. `notificationSites.test.mjs` — `PLACES_THIS_SPEAKS` 141 → 140, with the reason beside it (the prompt button's left-alone notice went with its branch); `eslint-suppressions.json` — `chatHooks.ts` complexity 9 → 7, pruned. NOT covered: a real editor press — `chatHooks.ts` imports `vscode`, and `setChatDraft` is unchanged |
| Chat with other AI — A CODE BLOCK IN AN ANSWER WRAPS (issue #537) | the cascade, the copy, and a measurement | `chatPage.test.ts` — `a code block in an answer wraps its lines…`: the parsed stylesheet's CASCADE (`cssRules.ts` `winning`) gives a `pre` inside `.msg .what` `white-space: pre-wrap` and `overflow-wrap: anywhere`, with no declaring rule left unread (red first: `actual 'pre'`; red again with `white-space: pre` planted back); `a table in an answer still scrolls in its own box…` — `overflow-wrap: normal`, `display: block`, `overflow-x: auto` by the cascade (a guard, green before and after); `the cascade helper sees an override through ANY ancestor…` — `#messages .what pre`, `main#scroll pre`, `body .msg .what pre` planted each turn the answer to `pre` (red with the two-element chain the first version used: our own code reviewer). `cssRules.test.ts` — `winning` reads the LAST declaration in a rule, counts an alias or shorthand (`word-wrap`, `overflow`), matches a name whole, and reports `!important` as unreadable (three red first, one red again with `.at(0)` planted). `answerCopy.test.ts` — a long no-space line is copied whole, byte for byte (a guard: the copy reads the markdown, not the page). `scripts/measure-answer-code-layout.mjs` (not in `npm test`: CI has no browser) — headless Edge at 320/700/1000 px: code block fits its box, the conversation does not overflow, the table scrolls in its own box; all three failed on main, all held after, and failed again with the plant. NOT covered in CI: the layout itself, which only the script measures |
| Chat with other AI — THE FAILURE LINE'S LAYOUT (issue #346) | structure, parsed declarations, the shipped bundle, and a measurement | `chatPage.test.ts` — *Try again* is the red box's sibling to its right, never inside it; with nothing to retry the row holds the box alone; the parsed stylesheet (`cssRules.ts`) gives `.failure` `overflow-wrap: anywhere` and `white-space: pre-wrap` and `.failureRow` a flex row with a gap. `bundledPage.test.ts` — the same structure in the markup the minified bundle renders, and the press still sends one retry. Every assertion red before the change. `scripts/measure-failure-layout.mjs` (not in `npm test`: CI has no browser) — headless Edge, three widths, three failures, `getBoundingClientRect`: all held, and all failed against main's markup. NOT covered in CI: the layout itself, which only the script measures |
| The Edit commands page (issue #467, Epic B) | pure modules, the page RUN, and the generated copy | `commands.test.ts` — the generated texts equal `shared/commands/` word for word; the page lists every shipped text once; its markers are `shared/command-models.json`'s; rows read with the server's defaults and as the server reads them (id trimmed, stage in any case, a shipped name refused, an unreadable stage kept as written — each red first), unknown fields carried, refused rows dropped; switching on asks for text and a blank file is none. `commandsEdit.test.ts` — add with the host's token, an id skipping a row AND an orphaned file, random rather than counted (two sides), a removed command never forgetting a shipped file, remove forgetting its file, retitle, restage, switch on refused without text (proved by three compiling plants). `commandsPage.test.ts` — the page's script RUN on the roles page's shim (`runPageHtml`), every posted message read back through `commandEdit`: an override typed, restore/add/remove, a tick and a stage posting once, a retitle and a custom text; the parser ignoring what the page never sends; markers, faint shipped words and Restore only for a real override; hostile markup inert (proved by removing one escape); the server note. NOT covered: the host's file and setting writes, which are the roles page's own mechanics |
| The gate's orders as data (issue #467, Epic A) | a recording, the pure core, the settings read, and a real round | `TheOrdersAreWhatTheyWereTests` — all 640 combinations of switches, scopes, plan sizes and model pairs against the recording the pre-change code wrote (proved by changing one word of a shipped file: red, naming the combination). `CommandTextsTests` — every listed id has an embedded text and every `shared/commands/*.md` is listed; an override wins and a blank one does not; each of the four markers survives any override of the text after it; placeholders filled; the preamble rewordable (proved by ignoring overrides: six red). `CustomCommandsTests` — enabled commands in listed order; disabled and other-stage ones not given, a document round given only `any`; one with no text left out and named with its file. `CommandsSettingTests` — defaults and the prefix; not-a-list refused with one sentence; bad, shipped, duplicate, stage-less and id-less rows dropped by name; the settings read carries the commands and their complaints. `ThePersonsCommandsReachTheCallerTests` (on the `FakeCliRoundTests` base extracted from `SplitOrderTests`) — an override file rewords the reply's order and its preamble, deleting it restores the shipped one; an enabled custom command reaches the reply after the built-in orders AND the rounds database; one with no text is in `commandsSkipped`; an override held by another process costs the round nothing and the shipped order is given (red first: the round failed). `RoleCompositionTests.APromptIdInTheCommandsNamespace_IsDroppedAndNamed` (red first). A numeric stage refused; an `any` command given in every kind of round. All red first except the move itself, which the recording guards. NOT covered: the extension's page (Epic B) |
| The consultation's filesystem invariant — A SIBLING'S GIT BOOKKEEPING (issue #376) | real temp repositories and linked worktrees | `FilesystemInvariantTests`: in a linked worktree, the main checkout switching branch and a sibling setting an upstream, a merge base and GitLens keys are NOT a breach (both red before); the editor's merge base in an ordinary repository is not either (red before); a shared `core.fsmonitor`, a `pushRemote` that is a URL, a `remote` naming a remote this file does not define, an include and a branch description ARE still seen; the same settings in another order are the same config (red before); a malformed config fails the snapshot; a change under `<git>/common/` is said to be shared. Every guard proved by breaking it. NOT covered: a consultant's own `commit --allow-empty`, which neither snapshot sees (the recorded open tail) |
| Export config / Import config (issue #467, Epic C) | decisions, over the real manifest | `configTransfer.test.ts` — a default is not exported and a changed one is; an object equal to its default in another key order is still the default; `credsKey` and `dataDirectory` never exported whatever they hold; `executablePath` removed from vendors and consultants with the rest kept; a Team-server URL exported; a round trip gives back the same settings and prompts; not-JSON, another format and another version refused with a sentence; an unknown key, a never-transferred key and bad prompt names (`../escape`, `con`) refused by name with their reasons while the rest applies; `withLocalPaths` over a list by id, a map by key and a plain value; the modal's counts and reasons; and the CLASSIFICATION — every manifest key named like a key, token, secret or password is set and an export run, and none comes out, with a guard that the walk found `credsKey` at all. `configApply.test.ts` — applied without touching what the file does not name; a vendor keeps its local path; a failing write puts every setting and prompt back, including ones the import had added; a failing restore is named. From the code round: an imported `executablePath` never applied and listed as refused; a value of the wrong kind refused by name (number, array, integer, a `['string','null']` union admitting null); a value differing from its default only by a local path not exported; inherited keys (`toString`, `constructor`) unknown to this build; a present-but-not-object section refused with the sentence. `configPromptFiles.test.ts` (real temp folders) — only regular `.md` files with valid names are prompts, a folder named `folder.md` is not; no folder is none; a symbolic link is not read (skipped where the machine refuses to make one — Windows without developer mode); a missing prompt is absent and one that cannot be read (a folder in its place) throws. Every guard proved by breaking it (the path strip, the `credsKey` entry, the rollback, a folder counted as a prompt, every read error taken as absent, the import path strip). NOT covered: the dialogs, the progress notification and `config.update`, which are `vscode` state |
| Chat with other AI — WHICH CONVERSATION IS THIS TAB’S | decisions only | `chatGoto.test.ts`, `conversationChoice.test.ts`. The decision *go to* takes, over the whole matrix: tab kind × what this window already holds × how many saved conversations carry this tab’s source × whether its own Claude session is in doubt × what state the index is in. Covered: that what is open is revealed without reading anything, and on the TAB rather than a source; that exactly one match reopens and two are a picker rather than the newer of them; that a conversation filed under another root of the same window is OFFERED rather than silently dropped, and is not bound to this tab; that one root spelled two ways is one root, under the filesystem’s own case rule; that a source of `none` matches nothing however full the store is of sourceless records; that an ambiguous Claude tab is offered its root’s conversations — the picker was empty until four reviewers found it — while a DOCUMENT tab never produces a session picker whatever the flag says; that an index still BUILDING is not an empty store and one that could not be READ never reopens; that the act-time check verifies source AND workspace, so a record re-filed into another project between the decision and the press cannot bind; and every narrowed title, each reason saying something different. NOT covered: the command and its six arms, which are the row below | **Since 2026-09-16 the three ambiguities are three sentences**: `ambiguous` folds a session walk that FAILED together with one that answered and matched nothing, so every non-namesake case was reported as *the folder did not answer* — over a folder that had answered perfectly well. `walkFailed` carries the failure separately, `Narrowing` has a fifth member, and the covered guarantees are: a folder that answered and matched nothing is `unmatched`, a walk that really failed keeps `unreadable`, each of the four titles says something different, and none of the other three borrows the word the failure owns.
| Chat with other AI — GOING TO THE CONVERSATION ABOUT THIS TAB | the wiring, against the source | `chatGotoWiring.test.ts`. The command that carries C2’s decision out — the half `chatGoto.test.ts` cannot see, because a build where every arm revealed the wrong thing, bound a cross-root record or started a duplicate would pass every decision test. Asserted against the MANIFEST and the SOURCE of `chatGotoCommand.ts` and `chatCommand.ts`: that the command is declared, chorded on both platforms and offered in both right-click menus; that it records its door BEFORE the work, like the five doors before it; that each of the six answers calls the thing it names and **a seventh would be a compile error**; that `start` opens the picker with the offer under the cursor and creates NOTHING; and that a conversation already live under another key is REBOUND rather than merely revealed, while the reload serializer binds nothing. **Binding is asserted as a question asked AT THE PRESS** — `bindTo` is a function, handed to every arm that binds (counted, because one arm holding a precomputed handle is the whole defect back), re-checking source AND the record’s OWN workspace, that the tab is still open, and that it still holds nothing; the record itself is re-read from DISK, and the one-press latch is set inside the `try` so a throw cannot wedge the command for the life of the window. Also: that a record that moved warns AND opens the list rather than stopping; that a session walk which FAILED is reported as `unsure` and said on the console rather than being read as “no sessions” — which would offer to start a duplicate — and that the walk runs under a progress notification. **And the three the second round found**: that the record is re-checked against THE TAB’S root through the very function the decision files by — it was being compared against its own workspace, a root against itself, which passed for every record in the store and undid the cross-root rule at the last step; that the offer to start one can actually be CHOSEN, is drawn only where a caller has said what choosing it does, hands over to the ordinary door on the tab the row names, and refuses when that tab is no longer the one in front; and that a conversation already live is rebound from the PICKER’s accept too, not only from the reopen arm, through one shared helper — with the ordering asserted, because the claim that a second panel was built is refuted by the registry being walked by the store id before anything is read back. Every one of those seventeen guards was proved by breaking it and watching the test name the symptom. NOT covered: the command through a real extension host, which is the row below |
| Chat with other AI — TAKING ONE BLOCK of an answer | decisions, the wire, and the page as it ships | `renderAnswer.test.ts`. An answer's only copy control copies the whole of it, so every fenced block and every blockquote the renderer draws now carries a copy row of its own, and one reserved fence tag — ```` ```reply ```` — makes that row read *Copy the reply prompt*. Covered: that the first block is numbered **0** and not 1, which is the `push`-returns-length trap a plan reviewer caught before it was written; that a fence and a quote each get a row, and so does a fence nested inside a list item — the shape a top-level scan of the markdown cannot see, and the one that already differs from what the renderer emits on 1 of 39 real stored answers; that a quote CONTAINING a fence gives two overlapping controls, the inner numbered first because a quote is recorded on the way out, so the numbers run in the order the rows appear; that a block past `MAX_DEPTH` is drawn as text and therefore earns no row and consumes no ordinal, asserted as the **exact** ordinal list rather than as two lists agreeing — both sides come from one implementation, so a length comparison would have stayed green while the guarantee was gone; that a `reply` fence differs from a `text` fence by its class and its label and by nothing else; that a renderer not told which message it is drawing emits **no** controls at all, which is what keeps this story from shipping a button nothing can act on; and that `button` — the twenty-sixth tag the renderer may emit and the only one that is ours rather than a model's — only ever appears in one exact shape, with a companion assertion proving that scan still finds one. Teeth proved by breaking the code twice: returning `push()`'s value fails 5 tests, suppressing the quote's row fails 3. **And the whole way across** (`answerCopy.test.ts`): the markup `chatMessagesHtml` produces, scraped for every control's coordinate as DATA, each one turned into the message the page's listener would post, run through `chatCommandOf`, resolved by the real decision and written to a fake clipboard — asserted against **hand-written** expectations, never against the same array indexed twice, which would pass under any consistent-but-wrong numbering. It stops at a fake clipboard rather than at `answerBlocks` because the Definition of Done promises what reaches the CLIPBOARD, and a test ending at the enumerator would stay green while the hook failed to forward or the write never happened — raised by two vendors independently on the plan round. Covered there too: that a control drawn for an answer since REWRITTEN to a different text of the same block count refuses rather than copying the new one, which is the case no range check can see; that an ordinal past the end refuses; that a rejected clipboard write is said out loud on the block path **and** on the whole-answer path, whose bare `void` had been silent since it shipped; and that two presses land in the order they were made rather than in the order they resolve. Teeth: removing the signature check goes red naming *the stale control copied the new text*, and numbering from `push()` goes red naming the wrong TEXT — and, on the reply block, an empty clipboard, which is the shipped symptom that defect would have had. In `bundledPage.test.ts`, the SHIPPED minified page: a press posts `copyBlock` with the block, the message and the signature as a STRING (coerced to a number it would be `NaN` for every signature that is not all digits, and every press would be refused), and carries no text; red when `[data-block]` is dropped from the selector. In `chatMessages.test.ts`: the coordinate is accepted only whole — index, block and a non-empty signature at or under the 64-character cap, the boundary itself asserted — and in `chatPage.test.ts`: that no two adjacent controls share a label, now that the message-level one reads *Copy answer*. **And the JOIN, added after CodeRabbit read #273**: those two halves were each exercised and never met — one proved the shipped listener forwards a dataset, the other that a coordinate resolves to the right text, and a parser or listener that dropped a field would have passed both. `bundledPage.test.ts` now renders a real answer, takes the REAL attributes of its second control, presses it through the shipped minified listener, and carries the posted message through `chatCommandOf` into the host decision and a fake clipboard. Watched red by making the parser answer block 0 for every message: the reply control then copies the FIRST block, which is exactly the shape neither half could see. **Also from that review**: a corrective write is itself a write, and a press can land while one is in flight — the first version started a correction and forgot it, so it could settle last and restore text already copied past. Red first, naming the clipboard holding `second` where the person last copied `third`. **And the GUARD in front of both decisions, 2026-09-18** — *is the message at this index still the answer this control was pressed on?* It had stood inline in `chatHooks.ts`, which imports `vscode` and so runs under nothing here, meaning the one rule a code round had added for a real race was asserted by no test in a file that had none. Extracted to `stillAnswering` in `answerCopy.ts` and covered four ways: the answer still there is the one copied; a message that is GONE is refused; a message that is still present but is now the person's own turn is refused, which is the race `undefined` does not cover; and the copy callback is not run at all for a refusal, since `blockToCopy` walking a moved message is how a stale block reaches a clipboard by a second road. Teeth: removing the guard turns three of the four red, naming the missing refusal sentence; the fourth is the happy path and correctly stays green. **And the two MOMENTS, four cases more**: the controls resolve the message at deliberately different times — the whole-answer one at the PRESS, because it sends no signature and would otherwise copy whatever the queue found later; the block one INSIDE the queued job, because its signature can only be checked against the message as it stands at the write. That difference used to be carried by the shape of two expressions and by nothing else, which is a guarantee no test can reach. `theAnswerControlCopies` and `theBlockControlCopies` are those two moments as functions, so a test holds the message list and CHANGES it between building the decision and invoking it: the press-time one still copies what was there; the queued one is proved late by the staleness check actually FIRING — it hands `blockToCopy` the rewritten answer, whose signature no longer matches the one the control carries, and is refused, where a control that had captured at the press would have handed over the text it was drawn from and copied it happily. Paired with a case where nothing moved and the block IS copied, since refusing everything would satisfy the first just as well. Also: a message that went away in between is refused rather than copied, and both controls refuse in the same words — asserted behaviourally, since counting a string literal in the source would pass just as happily on two copies that agree today. Teeth: making the block control capture eagerly — the exact defect a plan reviewer named — turns two of them red, naming *the queued press resolved against a message it had already captured* and *the queued press copied an answer that had gone*. **And the SWAP is a compile error now**, which is the code round's correction to a first version whose two moments were interchangeable: the entries take different arguments (a message by value against a thunk, plus the coordinates only the block control has), so handing each hook the other's entry point fails with `TS2554: Expected 3 arguments, but got 1` and `Expected 1 arguments, but got 3` — measured by performing the swap. **And the WIRING, in a real editor, 2026-09-18** — that the hook bodies call those entries at all, which is the one thing nothing outside a host can reach. Three scenarios in `src/test/host/scenarios.ts`: inside the host `vscode` is real, so `conversationHooks` is simply CALLED and the clipboard it writes to is the system's rather than a fake. A press on the answer control leaves the whole answer on it; a press on block **1** leaves `echo second` and not the whole answer (block 0 is what a handler ignoring the ordinal would serve, so asking for the second is the only version that can fail); a press on an answer the conversation has moved past leaves it untouched — **and that third one is RETRIED, because the clipboard is one per MACHINE**. A sibling session recorded these as *“failing on this machine regardless of the branch”*; the cause is narrower and fixable. Measured 2026-09-18: one run passes, two pass, THREE concurrent `test:host` runs reproduce it, and the only scenario that breaks is the one asserting an ABSENCE — the other two wait for a change they can recognise, this one waited for a change that must not come from anywhere, so any write by any program inside its window failed it. It takes three attempts now and tells the three outcomes apart: the sentinel still there is a pass; the person's own turn on the clipboard is the real defect and fails on sight; anything else is interference and is retried. Three spoiled windows in a row FAIL as *“could not be measured here … Run it alone”* rather than passing, because a scenario that cannot observe anything must not report success. **Proved three ways by forcing the interference**: one spoiled window is carried (5/5), all three spoiled fail with the could-not-measure sentence, and a guard made to leak still fails with *the guard is not in the path* — so the retry does not hide the defect it exists to catch. **Teeth, and they are the argument for the harness itself**: make `onCopyBlock` serve block 0 whatever ordinal the page sent — a wiring error that compiles — and `answerCopy.test.ts` reports 23 pass 0 fail, `tsc` exits 0, and the host scenarios go 3 of 4 naming *the block control copied something other than the block it was pressed on*. The refusal scenario runs FIRST, which is part of the test: `copyText.ts` re-asserts the newest text after a write it was overtaken during, so a copier from an earlier scenario can write after a later one has laid its sentinel — it did, failing that scenario on a break that cannot reach it. A refused press writes nothing, so with nothing copied before it there is no corrective write to race; and since an absence cannot be proved by waiting for a change, it waits a fixed moment and reads once. **NOT covered even so**: a press driven through the rendered webview's own DOM — these call the host's hooks directly with the coordinates the page would post, which `bundledPage.test.ts` separately proves the shipped listener really sends. **NOT covered**: a real press in a real editor, which needs the extension host — the same gap every row here has |
| Chat with other AI — NEW CHAT, the clean slate | decisions and the wiring | `chatFresh.test.ts`, `chatFreshWiring.test.ts`. Covered as values: the slate a reset installs, field by field — and, in both directions, every field it must NOT touch, because a reset that clears too little carries the old conversation into the new one while a reset that clears too much throws away the model somebody chose or the tab’s own identity, and both are silent; that the three marks saying what the disk already holds are DELETED rather than emptied, since the push that writes a conversation down reads “never written” from their absence and would otherwise never write the new record at all; that the clock is an argument; and that the two sentences say what happened, the failure naming its reason and saying the conversation still works. **The ORDER is asserted against the SOURCE**, which is where this story could go wrong invisibly: the generation is bumped BEFORE anything is stopped, so a question queued behind the running answer never begins; the ending is stop → wait for the turn chain → drain the disk queue → dispose → release, each position answering a specific way of losing or mis-filing an answer; the old record is archived only once the ending succeeded, and BEFORE the new id is published; the slate is applied as ONE value so a field added to it cannot be forgotten; a reset that FAILED archives nothing and leaves the conversation not merely live but usable; one reset runs at a time, with the latch released in a `finally`; a turn carries the generation it was QUEUED under and refuses to run under another, handing the words back to the composer; and what a turn writes at the END is guarded on the save id instead, because between a reset beginning and the slate being wiped the turn in flight is still writing into the old conversation. Plus the page seam: its own message type, the quotation cleared, the id merged into the held state, and the line recorded in `lastWritten` so it cannot go stale. **And what the code round added**: that archiving is part of the all-or-nothing, a store refusal going through the same recovery an ending failure does rather than leaving the old record open under a page that says it was archived; that the new record is WRITTEN before its id is published, since the page hands that id to the serializer after a reload; that the dead session stub is installed on the SUCCESS path too, because between the reset and the next question a stop or a switch still reaches the session it has just disposed; that the disposal and the release run whatever failed before them, so a session that would not stop is not left unreferenced and still running; that a question typed DURING a reset goes back to the composer instead of being queued into a conversation being torn down; and that the archived sentence travels with the state push, because the region it is written in belongs to that push and a line written straight into it was wiped on the following frame. **And what the final round added**: that EVERY field of a thread is classified — the reset's projection is exhaustive over `Thread` by a compile-time partition, so a per-conversation field added later and forgotten stops the build and names itself rather than silently surviving into the new conversation; that a window with nowhere to keep conversations REFUSES the reset instead of wiping one that has nothing to be archived into, with the nothing-said case checked first because it needs no store; that the new id is published only once the DISK has the record, since the write chain cannot reject and a drained queue is not a saved record; and that the one-reset-at-a-time latch lives on the thread rather than in a module-level set of object identities that outlives what is in it — that last one reads `chatThread.ts` now, and the reset feature itself `chatArchive.ts`, since the command file was split on 2026-09-17; the latch must not come back as a module-level set in EITHER file, so both are asked. Every one of those guards was proved by breaking it — thirty in all across the three rounds: twenty-seven went red naming their own symptom and three would not compile, which is its own guard. Three assertions passed their break the first time and were strengthened, each having matched a fragment rather than the whole condition. **And the button that makes it reachable** (`chatPage.test.ts`, `bundledPage.test.ts`, `helpCoverage.test.ts`): that *New chat* is in the header of BOTH kinds of tab — a file tab has no *Asked* button and must still have this one — that it carries its own id rather than the capped notice’s, since two elements cannot share one and the notice’s button would stop working; that exactly ONE header control pushes itself right, because an auto left margin on two flex siblings splits the free space between them and pushes the pair apart; that it posts the SAME command the notice’s button posts, once; that it says what it will do, since the label alone reads like “open another tab”; and, in the SHIPPED minified bundle, that pressing it asks for exactly one reset. **Plus the translation freshness check the coverage test cannot make**: `bodyFor` marks an article that is MISSING and cannot mark one that is a release out of date, so all five languages are asserted to name the control — its label is English in every one of them, as every other control name in this catalogue is. Seven breaks, seven red. Covered too, but NOT in `npm test`: the reset through a REAL vendor process, by `scripts/live-fresh.mjs` — plant a number, recall it (the CONTROL, without which a model that never remembered would “forget” for reasons that have nothing to do with the reset), dispose the session and its directory as `ended` does, open a new one carrying nothing as `reopened` does, and recall again. A control on BOTH sides, which the first draft had on one: the recall before proves the model could remember, and a liveness question after proves the new session is answering at all — without it a crash, a timeout or a rate limit produces an answer with no number in it and reads as forgetting. Either control failing is NO VERDICT and a non-zero exit. Run 2026-09-14 on all three adapters: claude, codex and agy each remembered 7431 before, answered the liveness question, and said NONE after — across both `persistent` and `per-turn` shapes. **It proves the RECIPE and not `freshStart`**, which is behind `vscode`: a reset that failed to call `ended` or reused the directory would pass this every time, and what pins that half is `chatFreshWiring.test.ts` above. It is a script rather than a test because no vendor is real in CI and every run spends money on a signed-in account; what CI can check is `liveScripts.test.ts`, below |
| The two scripts that touch a REAL vendor | the call shapes, against the interface | `liveScripts.test.ts`. `scripts/live-chat.mjs` and `scripts/live-fresh.mjs` are `.mjs`, load the compiled output through `await import()` — so every symbol they touch is `any` — and `tsc` never sees them. They rot silently, and they had: `launchSpecFor`’s third parameter became a `ChatLaunch` when the model picker started deciding which model the CLI is told to use (72a6e80a, 2026-09-11) and both scripts kept passing the bare resume STRING. A string is not `undefined`, so the parameter default never applied and `modelRefusal` read `.length` of `undefined` before any process started: every turn of every vendor failed with “the model’s process could not be started”, for three days, spending nothing and proving nothing. This asserts the launch argument is an object carrying every field `ChatLaunch` declares — READ from the interface, so adding a fourth field fails here and names the scripts — that both scripts are reachable as npm scripts, and that neither can report a pass for a run that asked nothing or whose control turn failed. All three proved by breaking them, the first with the exact regression that shipped |
| The coverage gate’s one BY-NAME exclusion | yes | `sonarExclusions.test.ts`. Thirty-three of the hundred and ninety-six modules in `src` import `vscode`, so no test here can execute a line of them and they reach SonarCloud as 0 % of whatever they contributed to new code — a number about the analysis rather than about the change, which is the same reason the extension’s lcov is produced at all. They are excluded from coverage BY NAME, because no glob can express “imports `vscode`”. A hand-kept list drifts silently in both directions: a new host module left out fails a pull request for a reason nobody can act on, and a module that sheds the import and stays in is production code nobody measures any more. This asserts the list is EXACTLY that set, naming the offending module and the direction; and that the host half stays a small minority, because the day it is not would mean the decisions had moved into it and the answer would be to move them back out rather than to widen the list. Both directions proved by breaking them. The whole entry comes out the day the row below exists |
| Reaching the code from the review page — the file at its revision, and the CURRENT file (story 3.1) | decisions, the page RUN, the wiring, and the live seam | `openAtRevision.test.ts` (the memory that probes once per repository, and the current-file guard over real directories), `bugzReviewPage.test.ts` (the two openers pressed, the host's `revisions` answer landing per row), `bugzReviewWiring.test.ts` (the presses routed, the answer posted and fanned out, the guard before the open, the provider's four hooks), `roundsDbRead.test.ts` and `bugzLiveContract.test.ts` (`--file-at` through the real reader, against the real binary over a real repository), `TheFileAtRevisionTests` (the mode, its lexical guard, its reasons). plus **two extension-host scenarios** (`src/test/host/scenarios.ts`), which is where the content provider and the two editor calls are answered: the revision document opens under this product's own scheme with the commit in its tab and the cursor on the finding's line, and **an edit is refused** — asserted by trying `applyEdit`, not by trusting the docblock — while the CURRENT file opens from the checkout at its recorded line. **Still not covered**: the press-to-tab path as a person performs it, from the webview button through the panel to the editor; the two halves are each driven directly |
| A Bugz key typed while this side names no server (research/PLAN_bugz_keys_per_server.md) | the decision, the stored outcome, and the wiring | `bugzKeysPerServer.test.ts` (a setter with no server stores nothing and answers `no-server`; a key set for one server is never read for another — red on the fixed name), `bugzKeyChoices.test.ts` (the refusal sentence), `bugsSendWiring.test.ts` (the sidebar delegates to the one `askForContributorKey`, which stores in secret storage behind a password box). **Not covered by a scenario**: the box not opening, as a person sees it — the refusal is decided before `showInputBox` in a vscode host function, and the host harness has no driver for an input box |
| Old Bugz keys adopted or discarded (research/PLAN_bugz_keys_per_server.md) | the decisions and the key store | `bugzKeyChoices.test.ts` (the choice with and without a server, what it says each answer costs, every outcome's sentence, a revoke refused for a record naming no issuer), `bugzKeysPerServer.test.ts` (adoption writes, reads back THE VALUE, and only then deletes; a newer real key wins; an empty one does not; no server keeps the old keys; discard files nothing; the old names appear in the key module alone). **Not covered by a scenario**: the toast → command → modal path as a person clicks it — `notifyThen`/`notifyAndAsk` modals have no driver in the host harness; the decisions they show are the values above |
| **Every command, through a real extension host** | **one scenario, since 2026-09-17** | `scripts/run-host.mjs` downloads a real VS Code with `@vscode/test-electron`, launches it with the extension loaded, and runs `src/test/host/scenarios.ts` inside it. The first scenario is the row's own subject: the extension ACTIVATES, and every one of the 22 commands its manifest declares is really registered — which no test outside a host can see, because the manifest parses and the source contains a `registerCommand` and the two can still name different things. **Proved by breaking it**: a phantom command added to the manifest fails the run with *“declared in the manifest and never registered, so the menu item does nothing”* and a non-zero exit. **`@vscode/test-cli` was NOT used** — it brings mocha, and this repository runs one runner. **The launch trap, measured:** a run started from a terminal INSIDE VS Code inherits `ELECTRON_RUN_AS_NODE=1`, which makes the child `Code.exe` behave as plain Node and reject the launch arguments with Node's own wording; the launcher strips it and nine `VSCODE_*` siblings. **NOT required to merge**, and promoted only after twenty consecutive green runs of `main` — **and since 2026-09-18 a machine counts them.** `scripts/host-job-streak.mjs` asks the Actions API for this job's conclusion in each run of `main`, newest first, and a final step of the job prints the streak into its own run summary, `if: always()`, because the streak is most worth reading on the run that just broke one. The rule had been written in the workflow since the day the job landed and nobody counted, so the plan that promoted this work had to record the promotion as pending with no figure; the first measured answer was **13 of 20**. **Nineteen** cases in `hostJobStreak.test.mjs` cover it. The counting is pure and is imported rather than spawned — a script exercised by spawning reports zero coverage and would fail the new-code gate — and so is the RETRY the counter gained on 2026-09-22, with the request and the pause injected. The load-bearing ones: a red NEWEST run is a streak of zero however long the history behind it, since promoting on that would break the very next merge; a run still IN FLIGHT ends the streak rather than being skipped, so it can never read twenty while the newest run is failing; and an empty history reads as zero rather than as “nothing has failed, so promote”, which is the glob-matching-nothing shape this file warns about elsewhere. It REPORTS and does not gate: promotion means editing branch protection, which is the operator's. **And since 2026-09-22 it no longer fails the job when it cannot ASK.** A single `502` from the Actions API, while reading the jobs of one previous run, turned this check red on pull request #461 with every editor test in that job green — the counter exits non-zero when it cannot find out, and a step that could not reach an API has learned nothing about the code under review. Transient failures (`5xx`, `429`, no answer at all) are now asked again four times with a backoff, each retry saying so in the log; a `401` or a `404` is an ANSWER and is not retried, because a runner's token is minted per job and does not expire between the calls of one step. If the asking still fails, the counter prints `UNKNOWN` with the reason and exits 0 — the doctrine it was written with is untouched, because it still never reports a figure it did not measure. **Two of the nineteen cases SPAWN the real script** against a local server answering 502, once per class of request it makes, and assert the exit code: an exit code is not something a unit test of a helper can observe, and a wiring mistake would otherwise leave every other case green and the job still red (codex and gemini, on the plan round). Those two are additional to the imports, so coverage is unaffected. The first draft of that harness used `spawnSync` and the suite hung for good, because a blocked event loop cannot serve the child it is waiting for. **Still NOT covered:** the other eleven rows below, and the settings lock — two hosts writing one file remains simulated rather than reproduced. One scenario chosen because a story needed it is evidence; twelve written before any had caught anything would be twelve guesses. |

## A scenario that drives a real child process (2026-09-09)

Two tests here launch the actual `coai-mcp --ask-remote` binary and kill it at a chosen moment. Three
rules, each of them written after the corresponding failure.

**A prerequisite is not the thing under test, and it gets its own budget.** The wait for the child to
reach the interesting moment was a flat `TimeSpan.FromSeconds(30)`, chosen once against the machine
the test was written on. On 2026-09-08 the `win-arm64` leg of the `mcp-v0.18.13` release matrix
failed at 30 s 359 ms while the other five legs passed — a cold .NET start, a sign-in, a loopback
HTTP round trip and a first write, immediately after a Release build of the whole solution, on the
slowest machine of the six. It cost the release a platform: no `coai-mcp-0.18.13-win-arm64.zip` was
ever published, and every Windows ARM install of that version answers 404. The bound is
`PrerequisiteWait`, 120 s, and it costs a fast runner nothing — the wait returns the instant the
condition holds.

**A prerequisite deadline must say what it was waiting for, and what the child was doing.** All the
evidence that failure left was `the condition was still false after 30s`, which cannot tell a slow
machine from a child that died on the way — different failures with different cures. `WaitForAsync`
takes the description and the child as REQUIRED parameters, gives up early when the child has already
exited (a dead child cannot satisfy the condition; waiting out the rest only delays the report by two
minutes), and its message names the wait, the elapsed time, the child's state as an OBSERVATION with
when it was taken, and everything the child said on stderr.

**A redirected stream must have a reader.** Both scenarios set `RedirectStandardError` and neither
drained the pipe. A child that fills a redirected pipe nobody is reading blocks on its next write —
for ever, since the parent's next act is to wait for it — and the symptom of that is a prerequisite
wait running out on one machine and not another. Whether it caused this particular failure cannot be
proved after the fact; a redirected stream with no reader is a latent hang either way. `StartShim`
drains it, which is also where the diagnostic above gets the child's own words. Its `Dispose` kills
the tree, because a wait that throws runs no `finally` of its own and a leaked `coai-mcp` holding a
claim file is how one failing test makes the next three fail for unrelated reasons.

Design record:
[PLAN_the_shim_scenario_waits_too_briefly.md](PLAN_the_shim_scenario_waits_too_briefly.md).

## The document gate's own suites (2026-09-13)

Four classes, and the split between them is the point: three prove the rules a unit at a time and the
fourth proves they are wired to each other.

| Suite | What it holds |
|---|---|
| `ADocumentRoleHasARoundTests` | The bucket selection, every case paired with its regression guard — the whole change is ONE predicate and the way to get it wrong is to widen it. Also that `plan:document` is in the catalog and in NO stage's round, asserted rather than assumed |
| `ADocumentSessionIsKeptApartTests` | Identity. The two-argument session key is pinned as a LITERAL, because what it protects is every session file already on disk. An edited document keeps its session; two files differing only in case keep their own; a case-different SIBLING is not inside where the filesystem says it is not; each gate refuses the other kind by name; an identity that looks like an ordinal is refused |
| `ADocumentIsProvenBeforeItIsReviewedTests` | Every refusal, with the resolver injected so the symlink rules are unit tests rather than a privilege the CI runner may not have. A link on a PARENT component, a sibling with the same prefix, a relative path resolved against the repository rather than the process, invalid UTF-8, a `.docx` refused before a byte is read, the size bound with both numbers in the sentence |
| `ADocumentIsReviewedEndToEndTests` | All of it at once, through the public tool, with a scripted vendor |

**Two of these rules were proved to have teeth by breaking the code and watching the failure name the
real symptom**, which is the discipline this repository holds itself to for a regression test written
after its fix. Reverting the containment check to a string prefix made `/repo-secrets/x.md` resolve to
`secrets.md` — an outside path accepted WITH a plausible identity. Reverting the path walk to resolve
only the last component made a parent-directory symlink come back `Ready` rather than `Refused`,
which is the file being read and sent to three vendors.

## The temp directory is a shared resource (2026-09-13)

Three things, and the third is the one that cost an afternoon.

**`TempDir` is the RAII helper.** `using var work = TempDir.For("coai-thing-");` and the directory is
gone when the scope ends, however the test ended. Eleven classes created one and deleted it nowhere
at all; the rest write the same delete out by hand in `Dispose`. It clears read-only attributes first,
because git marks its object files read-only and `Directory.Delete(recursive: true)` refuses one —
and it never throws on the way out, because a delete that loses a race with a held-open file is not a
failing test.

**A `using` on a value that ESCAPES its scope deletes what the caller was given.** The mechanical
conversion did exactly that to `VendorStagesTests.Repository()`, which builds a git repository and
returns its path: the round then found nothing there and answered `{ }`. A factory hands ownership
back — `private static async Task<TempDir> Repository()` — and the caller writes `using var`.

**And the suite's sweep window came down from a day to two hours**, because the cost of leftovers is
not disk. `RosterBuilder.BuildWork` walks the temp directory looking for its own scratch, and that walk
costs the number of directories in it. Measured 2026-09-13 after a day of runs: **81,986** `coai-*`
directories, and `SubmissionOrderTests` — a hundred `BuildWork` calls in a loop — went from 8 seconds
to over four minutes, which from outside is indistinguishable from a deadlock in whatever had just
been changed. The product now walks at most once every ten minutes per process rather than twice per
reviewer. CI never sees any of this: a runner starts with an empty temp, so a green CI beside a
stalled local suite is evidence FOR this cause.

**Both runners sweep BEFORE they start, on one shared rule (2026-09-23).** The operator's ruling of
2026-09-18: tests must clean up, not after each test but before the run, everything older than ten
minutes — a cleanup hung off each test never runs for the runs that leave the most behind (Ctrl-C, a
killed runner, a crashed host). `shared/temp-sweep.json` holds the rule: prefix `coai-`, ten
minutes, and the product's own working directories (`coai-answers-`, `coai-repair-`,
`coai-noworkspace-`, `coai-plan-`, `coai-server-job-`, `coai-chat-`) which are NEVER a test's to take —
a live chat's directory can sit for an hour with nothing written into it, and a round waiting on a slow
reviewer writes nothing to its answers directory until the answer lands. The extension's
`scripts/run-tests.mjs` calls `sweepTemp.mjs` first (a pure decision over names and times, a thin
unlinker that counts a failure and steps over it) and prints how many it took; the server suite's
`TempDirsAreSwept` reads the same file and passes the exception list to `PruneOldScratchDirs`. The
two-hour window it replaces never excepted anything, so it could take a live chat's directory.
**The trade said out loud:** a test that makes a product-prefixed directory through `PanelService`
(about a thousand per server-suite run) is now left to the product's own six-hour sweep, because by
name it cannot be told from a live round's. Measured on 2026-09-23: a full extension run on a
swept temp added 19 directories. Each rule in `sweepTemp.mjs` and the C# exception was broken on
purpose and the test that owns it went red (`sweepTemp.test.mjs`, `TempDirsAreSwept`). **And the list cannot fall behind the product:** each program scans its OWN source for the temp directories it makes (`CreateTempSubdirectory("coai-…")` in coai-mcp and the Team server, `mkdtemp(path.join(os.tmpdir(), 'coai-…'))` in the extension) and requires every one in `neverSwept` — and requires the scan to find the prefixes it knows are there, so a reformat is red rather than an empty list passing (plan round, local). Removing `coai-plan-`, `coai-server-job-` and `coai-chat-` from the file in turn reddened exactly the owning program's check.

## The collector's suites (2026-09-16)

Four, and each answers a question the others cannot.

| Suite | Drives | Catches |
|---|---|---|
| `CollectorTests` | **real git** — a real squash-merge, a real deleted branch, a real orphan whose absence the fixture asserts | a walk that attributes the wrong commit; a sha that reaches git when it should not |
| `CollectRunTests` | the shipped path: `BugsQuery` out, `Collector` through, `RecordCollect` in, rows read back | a classifier that never persists — the plan round's own words |
| `TheRunsThemselvesTests` | real SQLite over a temp directory, with a clock the test moves | a sweep that ends a LIVE run; a migration that never arrives |
| `bugzSection.test.ts` | the assembled page | a section that renders but never repaints; a text box that would flicker under the caret |

**Why real git rather than a fake.** Every failure this guards against is git's — a commit no ref
reaches, a file that moved, a history rewritten under the finding — and a fake would assert what we
*believe* about those. Measurement kept correcting the belief: the guard was written as equality
once and inverted the whole feature, and the first orphan rate came back 90.7 % against a branch
that happened to be 488 commits behind.

**Why a clock the test moves.** The heartbeat sweep is about elapsed time, and the alternative is a
test that sleeps for thirty-one minutes, which is a test nobody runs. `RoundsDb.Open` takes a
`TimeProvider` for exactly this: a clock a test cannot control is a column a test cannot assert.

**What each of them would still miss, and what covers it.** A database test and a markup test can
both pass while the built CLI, the migration and the webview wiring disagree — so the JSON contract
between the halves is exercised against the **real binary's** output, and `parseBugs` is asserted
against an OLD server's answer (no `lastRun` key) as well as a new one. That pairing is the lesson
of every wire field this product has shipped out of step.

## The review page's suites (2026-09-16)

| Suite | Drives | Catches |
|---|---|---|
| `ThePairsThemselvesTests` | real SQLite | an upsert that forgets a decision; a pair written for a claim that lost; a pair dropped from the page because its session row went; a page-only field (`why`, `repoPath`, …) creeping onto the record the send reads |
| `WhatIsStoredIsAnonymousTests` | the real normaliser, then the COLUMN | a skeleton computed correctly and stored wrong |
| `ThePairModesTests` | both one-shot modes | a request fault answered 64 instead of 65; a malformed document reported as success; a pairs document that does not say where each pair was, read by property NAME as the extension reads it; since story 4.2 `--pairs-decide` held to the same bad requests, a comment the rule refuses (by code point, naming the pair, quoting no text, writing nothing), the limit at 1 000 and 1 001, a changed comment on a SENT pair, 74 for a database that will not open, and 64 kept for an unknown mode alone |
| `UploadRunTests` (story 4.2) | `UploadRun` alone, through a stubbed `HttpMessageHandler` | a commented batch sent to `/ingest` or a comment-free one to `/ingest/commented`; an empty comment sent as `""` rather than omitted; a 401, 403, 429, 5xx or timeout called an OLD server; a 404 on the commented route NOT called one; a commented batch marked on an answer without `contract >= 2`; a lost-comment `Why` not written or not said; words edited while their batch was in the air marked sent; a run stopped before the request, or one whose answer was lost, marking anything; **the comment's text in any progress line or summary**, on all four endings. Four plants each reddened their own case |
| `ThePairsThemselvesTests` (story 4.2) | real SQLite | keep and comment not written together; `Sendable`'s ordinal reader answering the wrong column for the comment (asserted BY VALUE); a sent pair's words changed through the store, or the batch half-written when that is refused; `comment_lost` not kept; an edit during a send recorded as sent; an edit during a send ERASING the server's own reason when both happened (code round 1 of 4.2 — red first on the edit sentence alone); a database from before step 13 — made by dropping the two columns and stamping the file back to twelve — not gaining both, or losing a decision |
| `TheRealMethodTests` (story 2.3) | `--real-method` end to end: **real git** (a temp repository through `TempGitRepo`, the four helpers `CollectorTests` had), real SQLite, the real normaliser, the mode's own stdout | **the privacy regression**: the database file or the serialised `UploadRequest` differing by one byte after a read, or the wire carrying a real name; the un-anonymised text or the class not read at either commit; the three guards not reached (a lambda shown as a method, an overload set guessed between, a renamed method matched to its nearest); "commit absent" collapsed into "file absent"; an orphaned head refused although its object survives; the after side taken down with the before side; a checkout gone, a language not read or a pair not in the database answered as an exit code; a bad `--id` answered 64; a fix that renamed the file answered as anything but `file_not_in_commit` (the row the collector cannot store); the real text reachable from `StoredPair`, `UploadedPair` or `UploadRun.Wire` |
| `AstNormalizerTests` (the class walk, story 2.3) | `EnclosingType` over the real grammars | a method's class not named; a NESTED class answering the outer type; a class invented for a top-level function or for a language the normaliser does not read; a TypeScript or JavaScript class method not naming its class |
| `ARankingIsNotTrustedTests` | the pure ordering | a model that invents, omits, duplicates or contradicts |
| `bugzReviewPage.test.ts` | the page RUN against a DOM shim; the `about` block of a rendered row, sliced per pair; **the two halves of each row's code (`data-skel`, `data-real`) and the host's `real` message, pushed through the shim's `window`** (story 2.3) | a tick-box that renders and selects nothing; reviewer prose becoming markup; a cause invented for a finding that has none; a full sha where an abbreviation belongs; the after-side complexity labelled with the before-side commit; a count rendered as 0 for a language the page does not read; **the real-code toggle asking for a closed row, or asking twice; a fetched method not appearing in the row that asked, or the skeleton not stepping aside; turning the view OFF not restoring every open row's skeleton, or costing a process; a stale answer applied — after a flip, after a collapse, after collapse-all, or from another draw; a redraw with the view on and a cached method asking again, or with nothing cached not asking for the open rows; a side that cannot be shown not saying which fact stopped it; the too-old sentence and the reader's disagreeing; real text or a class name becoming markup; a decision posted while real text shows carrying anything but ids and keep** |
| `bugzReviewWiring.test.ts` | the panel's SOURCE, comments stripped | the page opening a row and the panel never recording it — the seam no page test can see; **(story 2.3) the toggle not recorded; a fetch answered by painting rather than posting, or posted without its generation; the cache consulted after the server, keyed on the id alone, or keeping a failed process; the view and the cache not handed to every paint; a closed window keeping the view or the cache; the provider not wiring `readReal` to the real reader** |
| `roundsDbRead.test.ts` (the real method) | `readRealMethod` against a stubbed `Run` | a document read as a promise rather than checked; **64 not told apart from every other failure** (the one code that means "update the server"); a domain reason read as a failed read; a document with one side read as an older server; a line span that is not a whole number reaching the page |
| `bugzLiveContract.test.ts` (the real method) | the **real binary**, through the **real** `readRealMethod` | an empty corpus's `pair_not_found` refused by the reader, or answered as an exit code; a missing `--id` answered 64 and read as "update the server" |
| `TheFileAtRevisionTests` (story 3.1) | `--file-at` end to end: **real git** through `TempGitRepo`, real SQLite through `PairSeed`, the mode's own stdout through `Stdout`; a RECORDING launcher only where the assertion is that no process starts | the whole file not read at the recorded commit, or read from the fix commit (the seed's fix sha is deliberately one nothing can read); a file deleted since refused; an orphaned commit refused although its object survives; `docs/notes;v2.md` or `src/[legacy].cs` refused; a traversal, an absolute, a drive-qualified, a UNC or a NUL path reaching git — through the reader OR through `GitHistory.FileAtAsync` itself; a malformed sha reaching git; "commit absent" collapsed into "file absent"; a checkout gone or a pair not in the database answered as an exit code; `--repo`/`--sha`/`--file` on argv redirecting the read to another repository; a read that writes; a bad `--id` answered 64; the file reachable from `StoredPair` or `UploadedPair` |
| `openAtRevision.test.ts` (story 3.1) | the panel's memory and the current-file guard, as values — the guard over REAL temp directories | a checkout that is gone not reaching its sibling rows, or reaching another repository's; a successful read not held for its row, or still held once the row names another commit or path; a commit that is gone taken as a fact about the repository; `git_failed` or a failed process answered from the cache; a server too old not stopping every row; a current-file refusal not remembered per row; a checkout outside every workspace folder, a file leading outside its checkout, or a file no longer there being opened; a checkout that IS the workspace folder refused; a note or a path becoming markup; a revision document not named by its short sha, or losing its extension |
| `bugzReviewPage.test.ts` (story 3.1) | the page RUN: the `data-revision` container of each row and the two openers inside it, found by `closest`; the host's `revisions` message through the shim's `window` | a row offering no way to its revision or to the current file; a press posting the wrong row or closing it; a probe at paint; the current file not labelled CURRENT / may differ, or either label carrying the other's word; the unprobed row not saying it will check; a host answer landing in the wrong row, or a gone checkout not reaching every row of that repository while another repository's row is touched; a redraw with what the panel remembers asking again or losing the reasons; a moved file not saying which path, or not offering the current file; a current-file refusal removing the button; a recorded path or checkout becoming markup |
| `bugzReviewWiring.test.ts` (story 3.1) | the panel's and the provider's SOURCE, comments stripped | either press not routed; an answer painted instead of posted, not remembered, or not fanned out per repository; the server asked before what was held; the current file opened before `currentFileIn` judged it, or from a server process; a closed window keeping the memory; a paint not handed the memory; the provider not wiring `readFileAt`, `showRevision`, `showCurrent` or `folders` to the real reader and the real editor |
| `AReviewTreeTests` (story 3.2a) | the durable worktree end to end: **real git** in temp repositories, the gate's own `WorktreeManager` beside it, a RECORDING launcher only where the assertion is that no process starts | a review tree removed by `PruneOursAsync` — asserted TWICE, once for the root and once for the prefix, each also proving the gate's own tree WAS removed so the test cannot pass on a prune that did nothing; a locked tree taken by `remove --force` or by `prune`; an orphaned commit refused a tree, or gc taking the commit a tree holds; two spellings of one checkout, or a linked worktree of it, making two trees; two clones sharing one; a reuse costing a checkout or a population; the cap evicting instead of refusing, or refusing without naming what is held; a half-made tree reused, or a half-made tree somebody edited being deleted; submodules missing from the tree; a sha that is not one reaching git; a gone checkout or an absent commit answered as anything but its reason |
| `bugzLiveContract.test.ts` — the reason union (story 3.2a) | the C# reason constants read for their VALUES, following the alias chain `ReviewTreeReason` → `RealMethodReason` → `SkipReason` → literal, against `TREE_REASONS` | the server answering a reason word the panel does not know, or the panel knowing one the server cannot answer — the server-to-extension contract the plan round said nothing exercised |
| `roundsDbRead.test.ts` (the file at its revision) | `readFileAt` against a stubbed `Run` | 64 not told apart from every other failure; a domain reason read as a failed read; a document about another finding, or without the sha and path it will be named by, accepted; an empty file read as a failure |
| `bugzReviewPage.test.ts` (story 4.2) | the page RUN: a comment box, counter and notice per detail row, built from the rendered markup; `input` and `change` dispatched; the page's pauses HELD by the shim and run by `pause()`, since a real timer would be slow or racy | a keystroke WRITING, or not handing the panel its words as a `draft` (code round 1 of 4.2); a pause or a blur not posting, or posting twice; a draft lost on a redraw; a box without the leaves-the-machine sentence; a sent pair's box editable, or typing into it posting; a lost comment shown as sent; `</textarea><img onerror>` becoming an element; a press in the box toggling or deciding; the counter not warning from 900; a decision posting anything but `type`, `ids` and `keep`. Four plants in `reviewComment.ts` each reddened their own case |
| `reviewComment.test.ts` (story 4.2) | the pure draft helpers; `shared/comment-limit.json`, which `TheCommentRuleTests.TheLimitIsTheOneBothHalvesAgreeOn` holds `CommentRule.MostChars` against too | the box's `maxlength` and the server's limit drifting apart — compared with both the export and the rendered attribute. (The first draft read the number out of `CommentRule.cs`; `NothingReadsAnotherProgramsSourceTests` refused it, rightly); a keep press writing an empty comment over a stored one; a keep invented for a pair the panel lacks — by a comment OR by a press (code round 1 of 4.2); a newer draft let go by an older write; **a draft let go by a PARTIAL write** (`settledBy`); **a closing panel not writing every draft the store lacks — or including a sent pair's** (`unwritten`); **a fallback failing part-way reported as nothing written** (`roundsDbRead.test.ts`); and, in `bugzReviewWiring.test.ts`, **a queued write capturing its decisions at the press rather than when it runs, and a close flushing every draft as one batch that one refusal sinks** (CodeRabbit, the pull request). Each of the code round's cases went red first against a stub that reproduced the old behaviour and compiled |
| `roundsDbRead.test.ts` (story 4.2) | `writeKeep` (its FIRST tests), `writeDecide`, `writeDecisions`, through a recorded `Run` and an in-memory decisions file | a batch on the wrong mode or with the wrong document; a failure reported as zero decisions; 64 not read as `tooOld`, or 65 read as it; an old binary not given a wordless batch keep by keep — or GIVEN a batch with words, which it would keep and lose. The last was proven by a plant that compiled: the first attempt left an unused variable, `tsc` refused it, and no test ran — a green that meant nothing |
| `bugzLiveContract.test.ts` (a decision with words) | the **real** writer, the **real binary**, the **real** reader | the argv, the decisions file and the document the binary reads drifting apart with both halves green (plan round of 4.2, codex); the words not coming back LF and trimmed; a refused comment answered as a binary too old for comments; **the page's limit and the RUNNING binary's disagreeing** — 1 000 characters taken and 1 001 refused, the one live check of the number both halves also hold against `shared/comment-limit.json` (code round 1 of 4.2, codex). The field-by-field seeded test now seeds VALUES for `comment`, `sentUtc` and `commentLost`, because an empty field that comes back empty proves nothing about its name |
| `bugzReviewWiring.test.ts`, `noticesDoNotBlock.test.ts` (story 4.2) | the panel's source, comments stripped | a decision reaching the queue without the drafts; a comment written before it is held as a draft; a draft let go on a FAILED write. And the panel still speaking in three places through the door that waits for the disk — counted across the two files the notices now live in, since two moved to `reviewWrites.ts` |
| `bugzLiveContract.test.ts` (the file at its revision) | the **real binary**, through the **real** `readFileAt`, over a **real repository** | an empty corpus's `pair_not_found` refused or answered as an exit code; a missing `--id` answered 64 and read as "update the server"; the TEXT not being the file as it was at the commit — the working tree is changed after the commit, so a read of the checkout would differ; `commit_unreachable` and `file_not_in_commit` not coming off the real binary as data |
| `codeHighlight.test.ts` | the real Shiki, all three grammars | a skeleton becoming MARKUP; a language rendered by guesswork; colours baked in past the theme |
| `lineDiff.test.ts` | the pure diff, nine cases | anonymisation's renumbering read as real change; a rewrite shown as an unrelated removal and addition |
| `cyclomatic.test.ts` | the pure count, over skeletons including one the normaliser's own fixture produces — **and, since story 2.3, over the ORIGINAL method that skeleton came from** | a keyword inside a string, a comment, a verbatim or a raw string counted as a decision; a `//` inside a string swallowing the code after it; a nullable `?`, a `?.` or a TypeScript `?:` counted as a branch; a count for a language the page does not read; **the count moving between a method and its skeleton, which is the number shown beside the un-anonymised text** |
| `corpusLanguage.test.ts` | the one language table, against `SourceLanguage` through `sourceLanguages.ts` | a fourth collector language nobody told the page about; a name inherited from `Object.prototype` answered as a language |
| `projectIdentity.test.ts` | the pure rule, every case taken from the LIVE table (106 distinct `repo_path` values, 2026-09-18) | one repository split by its spelling; a worktree filed as a project of its own; **two products merged because their worktrees share a parent directory** (22 of them under `d:/rsd/_wt`); **a submodule inside a worktree filed under the worktree's repository** (dew_flow_conventions under connect_other_ais); a vanished path merged with a neighbour; a filesystem call per row instead of per distinct path |
| `tabStrip.test.ts` | the one strip, as markup | a tab that controls nothing (`roundsLog`'s own defect); a repository name out of SQLite closing an attribute; two strips on one page sharing an id or indistinguishable to the click handler; a held choice kept when it selects nothing |
| `reviewTabs.test.ts` | project then language, over the pairs | **a language held from another project blanking the table** (findings 1 and 2, reported independently); a person's still-valid choice thrown away on a redraw; a strip drawn with one tab in it; an empty language key colliding with the everything tab |
| `roundsDbRead.test.ts` (the pairs) | `readPairs` against a stubbed `Run`, in both ages | a nine-field document from an older server read as malformed, or a field invented for it; a `line` that is not a whole non-negative number reaching the page |

**Why the page is run rather than read.** `PROJECT.md` refuses a new behavioural assertion over page
source text, and story 4 earned that ruling: a model picker matched every regex written about it
while being wired to nothing. The boxes the test drives are built from the ids the page ACTUALLY
rendered — a hand-written fixture handed to the shim would pass with no checkbox on the page at all,
which is the mistake its first version made.

**The shim answers `closest` about ANCESTORS, not only about itself** (2026-09-17, story 1.1). Its
elements used to each answer only for their own attribute, and a shim shaped that way is green
through the one mutation this page is most likely to grow: a branch widened from the control to the
row it sits in. Each pair's controls are now built inside a `Row` object, so `closest` walks the way
it does in a browser.

**Which mutations the collapse tests are actually red for — measured, not assumed.** PROJECT.md says
to ask what an assertion would see if the behaviour were deleted, and asking produced a surprise
worth recording: **removing the disclosure branch's early `return` changes nothing** (24 of 24 green
both ways), because every branch below it compares `target.id` and neither the twist button nor the
checkbox has one. The `roundsLog.ts` lesson does not transfer to this page unaltered, and the
`return` stays as house style rather than as something a test defends. What the suite IS red for,
both verified by mutation: rendering rows expanded (ten tests) and widening `closest('[data-toggle]')`
to `closest('[data-row]')` — the latter caught by *pressing it again closes it*, since the row cannot
report the state the button carries. Keying the open set by position instead of `findingId` is caught
by the reorder test.

**The shim decides a containment question rather than asserting about it.** Whether a summary line
opens its pair depends on whether the severity/title spans are INSIDE the disclosure button, and no
regex over markup is a behavioural assertion about that. So `run()` slices the button's own markup
and parents each `Line` to the button or to the row accordingly — the fixture is derived from what
the page rendered, exactly as the boxes are, and moving those spans back out turns *pressing the
severity and title opens the pair* red with "the part of the row that says what the defect is is
dead". Verified by mutation.

**`bugzReviewWiring.test.ts` covers the one seam the page tests structurally cannot.** The page tests
inject `expanded` straight into `reviewPageHtml`; if `received()` stopped recording an `expand`, or
`draw()` stopped passing the panel's set, every one of them would stay green while the open row
collapsed after the next decision. `bugzReviewPanel.ts` imports `vscode`, so no test here can import
it — the constraint `noticesDoNotBlock.test.ts` documents for the same file — and what is left is
reading the source with its comments stripped. TypeScript's own `noUnusedLocals` already catches the
crudest break (dropping the argument leaves `keptOpen` unused); this file catches passing the WRONG
thing, verified by making `draw()` intersect against an empty list. What it does not prove is that a
row reopens after a decision: that needs an extension host, which this suite still does not have.

**The escaping is asserted as a PROPERTY, never as an entity spelling** (2026-09-17, story 1.2). The
two paths escape differently — Shiki writes `&#x3C;` for `<` and leaves the apostrophe alone,
`escapeHtml` writes `&amp;` and `&#39;` — so a test looking for `&lt;` would have gone red on
entirely correct behaviour, and one looking for "no raw `<`" would go red on Shiki's own markup. What
is asserted instead is that the dangerous *sequences* (`</script>`, `<style`, `<img`, `<!--`) cannot
appear in the code element **and that the text is still all there**, because an escaper that simply
dropped the payload would pass every one of the first four. The same property is asserted twice, at
the module and at the PAGE, because the page is where the decision was made to stop calling
`escapeHtml` on skeletons: the call moved into the highlighter, and a later edit rendering a skeleton
anywhere else would reintroduce what was removed. Both verified by mutation.

**`Object.is` on two strings is not a cache test** (2026-09-17, story 1.2's code round). The first
version of the memoisation test asserted that `highlight()` returned "the same string back" and
stayed green with the cache deleted: strings in JavaScript are primitives, so `Object.is` compares
their VALUE and there is no reference identity to observe. The map's SIZE cannot tell them apart
either — re-setting an existing key leaves it unchanged. How often Shiki was ASKED is the only
externally visible difference between a cache that works and one that does not, so `codeHighlight.ts`
exports `timesTokenised()` and the test counts. Verified by mutation: with the cache read deleted it
goes red, where the identity version did not.

**Two things this module's tests deliberately do not cover, and say so in place.** `highlight()`'s
`failed` branch cannot be entered from outside — no input makes a loaded Shiki grammar throw on
demand — so `plainBlock` is exported and the two fallback shapes are asserted directly; what remains
unproven is that a real grammar failure routes there. And the table of languages is pinned against
`SourceLanguage` in `src_mcp` by reading the enum's declaration, because a table checked only against
its own unit tests drifts silently the day the collector learns a fourth language.

**The diff's own measurement is a test, not a comment** (2026-09-18, story 1.3). The number that
shaped `lineDiff.ts` — one added line read as seven changed ones, because the normaliser renumbers
placeholders in order of declaration — is asserted as a case, with the count, so the day a change
brings the wall of false differences back it goes red with the figure in the message rather than
looking merely noisier to whoever next opens the page.

**That measurement was taken on a CONSTRUCTED pair.** No real corpus was present on the machine —
the data directory was empty — so the shape is representative and the exact ratio is not a
measurement of live data. Worth re-running against a real database before the ratio is quoted
anywhere as a property of the corpus.

**The producer scan has a known blind spot, written down rather than left to be discovered.** The
placeholder-kind test reads `Placeholders.cs` and extracts the words it RETURNS as a kind, then
demands the set equal what `lineDiff.ts` masks — so a fourth kind arrives as a red test naming it.
Two reviewers pointed out the way it can still go quiet: a refactor that returns a new kind through
a named constant rather than a literal leaves the extraction finding the same three, and the
assertion passes over a set that is no longer complete. A companion assertion now demands the scan
still finds a KNOWN instance, which catches the extraction matching NOTHING; it cannot catch it
matching too little. The real fix is a generated contract shared by both sides, which is work in
its own right.

**What a row says about itself is asserted on rendered VALUES, inside the ruling's own carve-out**
(2026-09-18, story 2.1). The `about` block — where, why, fix, complexity — is static markup with no
program to run, and `PROJECT.md` names "a value appearing escaped" as the legitimate source
assertion. `aboutOf()` slices the block out of the detail row rendered for THAT `findingId`, so a
block rendered for the wrong pair is a failure rather than a match elsewhere; `readableHtml.ts` is
the tag scanner and entity decoder `codeHighlight.test.ts` had privately, moved out so both files
assert the same property the same way: the dangerous sequence cannot appear AND the text is still
all there.

**Verified by mutation, seven at once, each aimed at a different test — and the first compile of
that batch was not evidence.** Mutation (h) left a helper unused, `tsc` exited 1 under
`noUnusedLocals`, `noEmitOnError` kept the previous `out/`, and 71 of 71 passed against the OLD
build — the TypeScript doctrine's fourth trap, met while checking the tests for teeth. With the
compile read first: prose un-escaped and the "none recorded" branch removed → *no cause or fix
recorded says so* and *reviewer prose full of markup renders as text* red; the full sha rendered →
*the path and the short commit* red; the after side labelled with `headSha` → *labelled with the
revision its skeleton came from* red; the language table made an object literal → *a name
inherited from Object.prototype* red; literals not blanked → three `cyclomatic` cases red; `line`
read unchecked → *a server too old…* and *a line that is not a whole number* red. Ten red of
seventy-one, all attributable, nothing else moved. A second batch on its own, because the first
would have masked it: `//` comments blanked by a whole-text regex before the scanner's walk turns
*a comment marker inside a string does not hide the code after it* red — the obvious first draft
of that scanner, and why the test's docblock names the mutation rather than "the order of the
checks", which cannot break it.

**The server side, the same way.** `PairsJsonSaysWhereEachPairWas_AndWhatTheReviewersSaid` was
written against the nine-field answer and failed first for the real symptom — `KeyNotFoundException`
on `repoPath`, not a compile error — because it reads the JSON by property NAME, as the extension
does. After the record existed: the session's `LEFT JOIN` made a plain `JOIN` turned
`APairWhoseSessionIsGone…` red with *the collection is empty*; `FixSha` read from `head_sha` turned
both the typed and the JSON test red with `"aaaa111"` where `"bbbb222"` belonged. That fixture also
corrected a belief: Microsoft.Data.Sqlite turns foreign keys ON for every connection it opens, so
the product cannot orphan a round — the `sqlite3` shell can, with foreign keys OFF by default, and
the test deletes the session the way that shell would. The wire guard was unmodified by THAT story
and green (it was `OnlyThreeFieldsLeaveTests` then; the comment story later widened and renamed it
to `OnlyFourFieldsLeaveTests`); `TheWhereAndTheWhyAreOnThePagesRecord_AndNeverOnTheSends` pins on the types that the
seven page fields exist on `ReviewPair` and on nothing `Sendable()` returns.

**Counts on 2026-09-18, on this branch.** Extension: `tsc` exit 0, then 3 791 tests — 3 790 passed,
1 skipped, 0 failed (the compiled batch 3 746, the seven source-test batches 45), `npm test` exit
0. Server: 2 214 total, 2 211 passed, 2 skipped, 1 failed — `StageRulesTests.TheRotatedTail_
CurrentlyFitsAtMostOneRule`, whose own remark says the leftover straddles the smallest tail rule
between CRLF and LF; it reads the conventions mount, which this story does not touch, and it fails
on a Windows checkout before and after the change.

**Counts on 2026-09-18, story 2.3, on this branch.** Extension, on a clean `out/`: `tsc` exit 0,
then 3 933 tests — 3 932 passed, 1 skipped, 0 failed, nine batches, `npm test` exit 0; `eslint src`
exit 0 once the view moved into `realMethodView.ts` (the page had reached 949 lines against the
800 `max-lines` allows). Server: 2 244 total, 2 242 passed, 2 skipped, 0 failed; solution build 0
warnings, 0 errors. `plan-lifecycle`, `pin-check` and `adapter-check` clean. The one red run on the
way was `importCycles.test.mjs` naming `bugzReviewPage ↔ realMethodView` as a ninth cycle — a
type-only import back into the page — fixed by giving the view its own four-field `RealMethodRow`.

**The un-anonymised view, verified by mutation on both sides** (2026-09-18, story 2.3). Every
assertion was asked what it would SEE if the behaviour were deleted, and the answer was measured.

Server, two rounds against `TheRealMethodTests` and `AstNormalizerTests`: the `CountNamed > 1`
guard removed → *TwoFunctionsOfOneNameAtTheFix* red with `Expected real.After.Reason to be
"symbol_ambiguous" … but "" has a length of 0`; the class walk made to answer the OUTERMOST type →
*AMethodInANestedClass* red with `Expected … to be "Inner", but "Outer" differs`; a bad `--id`
answered 64 → all five *ABadIdIs65_Never64* rows red `Expected code to be 65 … but found 64`; the
`symbol.Name.Length == 0` guard removed → *ALineInsideANamelessFunction* red with
`Expected … "symbol_not_resolved" … but "" has a length of 0`; "commit absent" collapsed into
`file_not_in_commit` → *AHeadCommitTheRepositoryNeverHad* red; **and the mode made to write one
decision → *AReadWritesNothing_AndTheSendStillTransmitsTheSkeleton* red with the database file
`differs at index 101998`** — the privacy test bites on a write of one row. 90 of 90 green with
every mutation reverted.

Extension, two rounds against the page and the reader: `pending = {}` removed from the toggle →
*a response to a request that is no longer wanted is discarded* red; `delete pending[id]` removed
from a collapse → *a row collapsed while its method is on the way* red; the `draw/` prefix removed
from the generation → *a response from another draw is discarded* red; the toggle ignored in
`renderReal` → *turning the toggle off restores the skeleton*, *a redraw with the view off* and
*asks for every open row once* red (the last because the rows were then asked for twice); the
generation check removed from the message handler → four tests red (stale, collapsed, collapse-all,
another draw); 64 answered as an ordinary failure in `readRealMethod` → *a server too old for the
view is told apart* red. **One mutation stayed green and it is acceptable:** rendering the skeleton
half `hidden` in the MARKUP whenever the view is on (`skeletonHidden = showReal`) changes nothing a
test can see, because `renderOpenRows()` runs on load and re-derives every open row's visibility
from the state the row holds — the wrong initial attribute is corrected before anyone sees it, and a
closed row's skeleton is invisible until `renderReal` runs on open. That is the design working, not
a gap: the attribute is a hint the script overrides, and the script is what the suite runs.

**What the story deliberately does not test, said in place.** No test drives `BugzReviewPanel`
itself — it imports `vscode`, the constraint `bugzReviewWiring.test.ts` documents — so the cache's
sha check, the in-flight dedupe and the generation echo are pinned on the SOURCE with the whole
condition each time, and the behaviour end to end (a row re-drawn after a decision showing the same
real text without a spawn) needs an extension host this suite still does not have. And the
populated `--real-method` round trip through the real binary AND the real reader is not exercised:
the server's suite can make a git repository and the extension's cannot, so the server checks the
populated answer by property NAME (`TheAnswerNamesItsFieldsAsTheExtensionReadsThem`) and the
extension checks the envelope on an empty corpus; the field names are the seam between them.

**The review page's import graph is now GUARDED, and it was not before** (2026-09-18, story 2.1's
plan round). Three docblocks in epic 1 said a review-page module importing `node:` or `vscode`
"fails the bundle test". It would not have: `bundledPage.test.ts` bundled the rounds log and the
chat page only, and `theBundleLoads.test.mjs` loads the whole extension bundle, where `node:` is
available and a page module importing it proves nothing. Purity was held by comments CLAIMING a
guard that did not exist — worse than no comment, because the next reader stops checking. The file
now bundles `bugzReviewPage.ts` as a third entry point and asserts no `require("node:` and no
`require("vscode")` survive, with a companion that the bundle really renders a page.

**And the first mutation for it was too weak to prove anything.** Adding an unused `node:crypto`
import left the test green: esbuild tree-shakes an import nothing uses, so the bundle was clean
and correctly so. The mutation that counts USES `randomBytes`, and that one is red. Worth knowing
before trusting this guard: it catches a real host DEPENDENCY, not a stray import line.

**The `--pairs-json` contract is exercised end to end** (story 2.1's plan round, two providers
independently). `ReviewPair` is written twice — a C# record and a TypeScript interface — and every
other test of it feeds one side a fixture the other never produced, so `headSha` against
`head_sha` would leave both suites green while the page rendered four honest-looking absences,
indistinguishable from an older server. `bugzLiveContract.test.ts` now runs the real binary and
parses with the real `readPairs`. It uses an EMPTY corpus deliberately: what only it can see is
that both halves agree on the envelope, and the field names are checked against a populated
database by the server's own `ThePairsThemselvesTests`, which can write rows this side cannot.
Verified by mutation — renaming what the reader expects turns it red.

**The interpolation fix was a REGRESS before it was a fix, and the code round caught it four
times** (2026-09-18, story 2.1). Keeping whatever sat inside braces so that `${x ? 1 : 0}` would
count applied that rule to every literal AND to comments: `// { if }` counted a branch,
`"{ if for while }"` counted three, a block comment counted two. Measured at 2, 4 and 3 where 1
belonged. It was the worse trade — an understatement in a rare case swapped for an OVERSTATEMENT
in common ones, on text that survives `Normalise` verbatim — and a complexity that moves when only
a comment moves invents a change the control flow does not have.

Only three literal forms interpolate (a template literal, C#'s `$"…"` and its verbatim pairings);
everything else, comments included, is blanked end to end. The property is now asserted as
itself — *changing only the prose does not change the number* — which fails for a comment form
nobody thought to enumerate, rather than for the three that were.

**And the fix obscured a pre-existing hole in the scanner.** `$"…"` without the `@` was never
recognised as one literal: `$@"…"` and `@$"…"` were both handled and the plain form fell through,
so the `$` passed as code and the quote opened a bare string that ended at the first quote INSIDE
the interpolation. It answered correctly while every literal was blanked wholesale and stopped the
moment holes began to be scanned.

**`Columns` lost a guard on the way out of `BugsQuery`, and `TheColumnReaderTests` is the answer.**
The private helper it was extracted from read `IsDBNull(at) ? 0 : Convert.ToInt32(GetValue(at), …)`;
the extracted copy called `GetInt32` straight, which throws *"The data is NULL at ordinal 0"*. Two
reviewers were right about the principle and wrong about the consequence: every column the review
page's projection reads is `NOT NULL DEFAULT`, so no caller today can reach it — but a shared
reader with a hole in it is still that. Tested against a table with genuinely nullable columns,
which is the only way to reach the branch, with a companion proving a present value still reads as
itself and a 64-bit id is not truncated.

**The `--pairs-json` field names are compared at their SOURCES.** `ReviewPair` is written twice, so
`fixSha` against `fix_sha` would leave every suite green while the page rendered honest-looking
absences — indistinguishable from an older server and so unnoticeable by looking. The live check
runs the real binary through the real reader but against an EMPTY corpus: nothing here can seed a
pair from outside the collector, and adding a CLI mode to make a test possible would be changing
the product to suit its tests. So a third structural read across projects (after `SourceLanguage`
and `Placeholders.cs`) camelCases the record's properties and compares them to the interface's
fields. Verified by renaming one field on the server side.

**And the populated round trip is now proven too — the conclusion was wrong, not the reasoning.**
"Nothing here can seed a pair from outside the collector" was true of the product and false of the
runtime: Node 22.5 brought `node:sqlite` in, so a test can write the rows itself. The product is
untouched, no CLI mode was added to suit a test, and the gap is closed rather than recorded.

The shape matters. The BINARY makes the schema — a `--bugs-json` call first, so every table and
every migration is its own work; a test writing its own `CREATE TABLE` would assert against a schema
it invented, which is the failure this file exists to avoid. Then four rows across the three tables
`Pairs()` joins, the real binary prints, the real `readPairs` parses, and all sixteen fields are
compared BY VALUE. The two shas are deliberately different, because the page labels each side of the
complexity with its own and only distinct values can catch one read where the other belonged.

Two mutations nothing could catch before: reading `fix_sha` from `r.head_sha` (the projection crosses
the two commits) and the reader asking for `fix_sha` where the server prints `fixSha` (the exact
drift two reviewers named). Both red.

**What these still do not prove.** Nothing spawns the built binary and drives the review flow end to
end; `bugzLiveContract.test.ts` does that for the corpus read and there is no equivalent for the
pairs — the two-age compatibility of `--pairs-json` is proven with a stubbed `Run` on the read side
and with the mode's own JSON on the write side, never with an old binary against a new extension or
the reverse. The complexity count is checked against hand-counted skeletons and one the normaliser's
fixture produces, not against an analyser's number over the live corpus. And the ranking has no
transport, so `Ranking.Order` is exercised and nothing produces a real reply for it to order.

## The ingest server's suites (2026-09-16)

| Suite | Drives | Catches |
|---|---|---|
| `TheAlphabetTests` | the real normaliser over this repository's OWN files | a whitelist too narrow (it found two) or too wide |
| `TheKeywordsAreOneListTests` | the grammars | a keyword list gone stale after a grammar bump |
| `TheIngestTests` | the pure decisions | a refusal that strands its neighbours; a leak that is stored |
| `TheCommentTests` | a contributor's own words | a comment scrubbed, scanned, lost at promotion, or read on the route that must ignore it; the IDENTICAL words retried after a lost answer called somebody else's (story 4.2) |
| `TheRouteTests` | the REAL server in-process | the AOT JSON binding, the bearer header, the 401, the caps, the two ingest routes really differing |
| `OnlyFourFieldsLeaveTests` | the mapping and the serialiser | the symbol, the id or the finding's prose crossing; a comment-less wire that stopped being byte-identical |
| `BothHalvesTests` | the REAL client against the REAL server | the two halves disagreeing about an id, a word, or a document; since story 4.2 a comment that does not cross on its own route, a retry of an unacknowledged send reported as a lost comment (red on the old `Attach`), and words that lost to an earlier comment recorded as sent without saying so (red on a client that drops `comment_lost`) |
| `TheBuiltBinariesTests` | two real PROCESSES over a real socket | a publish, trimming or embedded-resource defect; an exit code; the field's file not migrated; a 429 without its number; a revoke that stops nothing; two servers on one directory; since 2026-10-02 a port somebody else holds ending in a crash instead of exit 75 and one line (`APortSomebodyHolds_…`, red on the unhandled `IOException`; its Team-server twin is `src_server/tests/ATakenPortTests`, the shared predicate `BindFailureTests` in `src_mcp/tests`) |
| `TheBuiltBinariesTests.OldServer` | the PREVIOUS RELEASE, `bugs-v0.2.0`, pinned by tag and by SHA-256 per architecture and verified before it is executed | the belief that an old binary has no route for a comment; **the only test here that is not two halves of one checkout agreeing with each other.** CI fetches the archive through `.github/actions/fetch-old-bugs`; unset, it skips locally and FAILS on CI, because a silently skipped check is a silently deleted one |
| `TheEdgeIsWatchedTests` | the running server, reading what it LOGGED | a misconfigured edge going unnoticed; the address reaching a log |
| `WhatTheArgumentsMeanTests` | the argument rules, in-process | 64 answered for a mode this binary has, or withheld for one it does not |
| `TheKeywordListIsCheckedTests` | the startup guard | a binary that starts, passes the smoke, and refuses every pair |
| `TheArchiveCheckTests` | the RELEASE's own shell, executed | an archive shipped without the executable, or without the library it dlopens |
| `TheSchemaIsFrozenTests` | step 1 of `CorpusSchema` against a fixture copied from the RELEASED tree | step 1 edited after it shipped — two shapes under one version number |
| `TheMigrationTests` | a file written by step 1 ONLY, at `user_version = 0` — the shape the host has | a column `IF NOT EXISTS` never adds; a second open re-running an `ALTER`; a concurrent opener failing instead of waiting |
| `SqliteMigratorTests` (in `src_mcp/tests`) | the shared runner over a fresh and a part-way file | a step run twice; WAL or the busy timeout not set |
| `TheLimitIsASettingTests` | `RatePerMinute.Parse` over every shape of the value | a clamp where a refusal is owed; a default or the off switch lost |
| `TheRateLimiterTests` | the limiter on a frozen clock, and THREADS released by a barrier | a fixed bucket; an over-admit under a race; a stamp evicted from under an admit |
| `TheLastSeenMonthTests` | the real server on a frozen clock, either side of a UTC month boundary | a day or an hour stored; the month moved by a 401, a 429, a 400 or an admin call; an increment lost to a race |
| `TheRevokedKeyTests` | the real `--revoke` one-shot against the running server | the operator's stop button stopping nothing; a revoked key reaching the limiter |
| `TheQuarantineHoldsNoClockTests` | every table that carries a `key_id`, read from the SCHEMA | a clock time stored beside a contributor's key — the promise made false by a column |
| `TheGateComesBeforeTheBodyTests` | the order of the middleware, over real requests | a body parsed before the key is checked, so a flood is deserialized before it is refused |
| `TheAuditTests` | `admin_audit` through `Issue`/`Revoke`, and the sweep over a small and the REAL bound | a note, a key or a hash in the audit; a sweep keeping the wrong rows; a mutation committed without its audit row |
| `TheServeLockTests` | the exclusive open, and a one-shot beside it | two servers on one data directory; a one-shot locked out |

**Why an HTTP suite when the decisions are already unit-tested.** `Ingest.Take` is pure and covered;
the route is not part of it. The one that matters most is the AOT JSON binding — this repository has
a contract suite because such a failure once made a released `coai-server` answer 500 to everything,
and a serializer context compiles perfectly while binding nothing.

**Why the alphabet is tested over real files.** A fixture proves what its author imagined. Run over
this repository's own methods it found two defects in the validator — adjacent empty strings read as
a leak, and a C# range operator read as a numeric literal — both of which refused good work.

**The live cross-implementation check.** `BothHalvesTests` seeds a real `coai.db`, hosts the real
`coai-bugs` in-process, and runs `UploadRun.RunAsync` against it. It was the loudest finding of the
code round and the convention is explicit: *a contract with two implementations has ONE live check
that exercises them against each other — two suites agreeing with the same file is not that check.*
The wire types are shared now, which removes most of the drift it was written to catch; what it
still catches is what a shared record cannot promise — that the client derives the id the server
derives, that it reads the words the server writes, that both AOT serializers bind the other's
documents, and that a queue past the 200-pair cap arrives in full rather than reporting success
about its first batch. Before it, `UploadRun.RunAsync` had no test at all; only its private mapping
did, and the catalogue said otherwise.

**And one level below that: two real processes.** `BothHalvesTests` hosts the server's ASSEMBLIES, which cannot see a publish-layout, trimming or embedded-resource defect — and this story's whole reason for existing is a keyword file read from a directory no release has. `TheBuiltBinariesTests` starts the built `coai-bugs` on a real port, mints a key through the real `--issue-key`, uploads through the real `coai-mcp --upload-pairs`, and reads the result back through the real `--waiting`. It is on `COAI_CONTRACT_EXE`, the seam the release workflow already sets, so the same scenario is a fast check here and the release smoke there.

**The admin API's suites, and why each is at the layer it is at.**

`TheAdminKeysAreBase64Tests` is the WIRE: the variable arrives base64 and anything else is refused.
It exists because the two shapes overlap — a hex key is inside the base64 alphabet and base64
decoding ignores whitespace — so "it decoded" proves nothing, and a server that fell back to the raw
list would start with administrators nobody holds. Each of the three rules that separate them has a
test (no whitespace in the encoded value, strict UTF-8, no control characters), and two of them were
proved by deletion: removing the whitespace rule makes wrapped base64 pass, and removing the control
character rule accepts a payload that decodes to nonsense. The red that started it was measured
against the unfixed server: a base64 list of two keys configured **one** administrator whose key was
the whole blob.

**Both harnesses now feed the encoded form**, through one `Delivery.AdminKeys` — the in-process
`BugsServer` and the real-binary scenario. A fixture that set the raw text would test a shape no host
produces, and every admin test would pass while the deployed server refused to start.

`TheDeliveryAgreesWithTheServerTests` is the contract between the shell and the server: it runs the
REAL `deploy/bugs/first-key.sh` through `sh`, over a table of lists an operator might plausibly write
— an indented comment, a key with a trailing space, a tab, CRLF — and asserts that whatever the host
picks is a credential `AdminKeys` admits. It exists because a code round found the two parsers
disagreeing (the shell dropped comments before trimming, so `  # alice` became the "key" and the
deploy failed on a list the server was happy with) AND found that nothing would have noticed: every
other test here builds the environment in C#, so the shell could say anything and the suite would
stay green. Its teeth were proved by putting the old filter order back and watching five of its cases
go red. The harness is `TheArchiveCheckTests`'s — on CI a missing `sh` fails the job, on a Windows
checkout without git's `sh` it skips.

`TheHostsHelperIsTheOneThisDeployNeedsTests` guards the other half of that shell-to-server seam: not
which line is a key, but whether the host's helper can carry a key list at all. `install-env.sh` is
COPIED to `/usr/local/sbin` at provisioning and is the one file a deploy never refreshes, so it can
fall behind the checkout that talks to it — and on 2026-09-22 it had, by ten hours and one release.
The older body read stdin as `head -1`, so it took the secret, dropped the administrator list without
a word and reported success; the deploy was green, the server started, and every `/admin` call
answered 401 until `admin-check` said so a whole deploy later. The test runs the REAL
`deploy/bugs/helper-protocol.sh` over four helpers: the one this checkout ships (accepted — which is
what makes the marker single-sourced, since a change to either end that does not change the other
fails here), the pre-administrator body that was actually on the host (refused, and the refusal must
carry the `install -m 0755` repair rather than sending somebody to this document for it), four that
are not this protocol, and a path that does not exist (refused, naming the path). Those four are the
part a code round had to add: the first draft compared with `grep -F` and asserted only `protocol 1`,
which is refused either way — so the test agreed with the bug. `protocol 20` CONTAINS `protocol 2`,
and a containment check admitted it, which is precisely the helper the guard exists to stop. The
check now demands the whole declaration LINE, and the theory covers the earlier protocol, the later
one, a mention in a comment and the string inside some other statement. Its teeth were proved twice —
by moving the shipped helper to protocol 3 and watching the drift test name that exact string, and by
making the check accept everything and watching both refusal tests go red. The harness is
`TheDeliveryAgreesWithTheServerTests`'s, for its reason.

`TheRootHelperFramesTwoRecordsTests` (2026-09-23) covers what the protocol test could not: that
`install-env.sh` FRAMES the two records correctly. It runs the real, unmodified script — which runs as
root and writes under `/etc` — with stand-ins for `install`, `chown` and `mv` first on PATH; the
`mv` stand-in insists the destination is `/etc/coai-bugs/env` and puts the file where the test reads
it. No override for the destination was added to the script: a root-owned writer that took its path
from the environment would be a privilege escalation in a checkout the deploy account can write. Ten
cases: both records become the three lines the server reads and neither value is printed; an empty
second record is no administrators; an absent one is refused and nothing is written; an unterminated
one is still complete; a third record, terminated or not, is refused (a wrapped base64 list would
decode to a PREFIX); carriage returns are stripped; no secret is refused; a list over 64 KiB is refused
rather than truncated; and a refusal on the first line with a megabyte left unread is still read as a
refusal. That last one is the HARNESS's guarantee and went red first (`IOException: The pipe is being
closed`): it wrote stdin before it started reading the child's streams, so a script that stopped reading
broke the test instead of answering it — it now reads both streams first, treats a closed pipe as the
script's answer, and kills the tree on its 30-second timeout (code round, gemini). Teeth: reading an absent record as empty, throwing a third line away, and
keeping the carriage return on the list each turned exactly their own case red. The harness is
`ShellScript` — renamed from `ReleaseScript` in the same change, since it now runs deploy scripts
too — widened with `Fed` (a stdin and a PATH) rather than copied.

`ThePromiseMatchesTheSchemaTests` guards the WORDING against the schema: while `api_keys` has
`last_seen_month` on it, no live surface may still say `submissions` is a counter with no clock. It
is deliberately conditional rather than a banned word — drop the column and the old sentence becomes
true again — and it scans the server, its tests, the edge vhost and the module docs while leaving
`research/PLAN_*.md` alone, because a plan that was right in July is a record and not a claim.

`TheAdminKeysTests` is the credential store: what the key list parses to (comments, blank
lines, `\r\n`, surrounding spaces), that the LAST configured key matches — which an early return
would break while every first-key test passed — and that the same key under a different secret is
nobody, which is what proves the lines are hashed rather than compared as text. One test reads the
private field through reflection and asserts every entry is 64 hex characters and none is the key,
because "we hash it" is the kind of claim a refactor keeps in the comment and drops from the field.
The no-early-return guarantee is **structural**: the property is about TIME, and a test that measured
it would be measuring the machine, so it reads the source and pins the whole condition — the loop
exists, it compares with `FixedTimeEquals`, and its body holds no `return`, `break` or `continue`.
Its teeth were proved by adding an early return and watching it name that symptom.

`TheAdminRoutesTests` is the five routes over real HTTP, including the six new `BugsJson` shapes
actually binding. It reads every answer through record shapes declared in the test file rather than
through the server's own wire types — a test that deserialised with `AdminWire` would agree with the
server about a field it had renamed. The issued key is used to ingest, so "the response carries a
key" cannot pass for a stub; the newest-first order that makes a lost issuance recoverable is
asserted as the contract it is; and one test walks the bytes of every route asserting that none but
the issuance carries a key or a hash.

`TheAdminPagingTests` is the arithmetic. `StabilityAcrossAnInsert` is the sequence an `OFFSET`
implementation cannot pass — page one, then an insert at the top, then page two — and its teeth were
proved by changing the cursor's `<` to `<=` and watching it report the boundary row served twice.
`limit=0`, a limit past the cap, a non-numeric value and a cursor past the end are each asserted as a
400 naming what was legal or an empty page, never a clamp; `total` is asserted present on the keys
page and **absent** on the audit, on the raw bytes.

**The route catalogue is derived, not retyped.** `BugsServer.AdminRoutes()` reads the host's own
`EndpointDataSource` and returns every `/admin` route with its real method. Written by hand, the
gate's shared assertions named the three GET routes and silently left both POSTs out — the issuance
among them — which is the testing rule's own warning that a list repeated in a test will not notice
the third entry. Derived, a sixth admin route is covered by those assertions the day it is added.

`TheAdminGateTests` is the disclosure rule, which is the one thing here that cannot be checked by
looking at one server. It captures what a wrong credential is told by a server WITH administrators,
disposes it, starts one with the variable absent, and compares status, body and `WWW-Authenticate`
byte for byte. The two servers run **sequentially**, not side by side: the harness configures the
server through process environment variables, so two live instances share a data directory and a
serve lock and the second never builds a host — a failure that says nothing about admin keys. Its
other half asserts the right credential IS accepted, because a gate that refused everything would
satisfy an indistinguishability perfectly.

`TheAdminUploadTests` covers the administrator's own ingest path, and its load-bearing test is
`AccceptRefusesWhatAcceptAdminStores`: `Corpus.Accept` refuses the very id `Corpus.AcceptAdmin`
stores under, which is the design argument made checkable rather than written down.

**Two tests assert WORK rather than a duration, because the property is about time.**
`WithNoAdministratorsTheComparisonStillHappens` compares the number and length of the entries an
unconfigured server walks against a server with one administrator: matching used to return early
when nothing was configured, so it never hashed the presented key at all, and identical response
bytes were worth nothing against a clock. A wall clock on a shared machine measures the machine,
which is why the assertion is on the comparison list and not on a stopwatch.
`TheComparisonWalksEveryHashAndLeavesOnlyAtTheEnd` reads the source and pins the whole condition —
and it **strips comment lines first**, because the loop's own comment says it never *returns* from
inside and the word failed the assertion. The same trap as the `FrozenSet` check, which reads as a
defect in a file whose remarks explain at length why it is not one.

**`TheDocblocksAreAttachedTests` guards a mistake that happened four times in one story.** Inserting
a method between a doc comment and the member it belonged to leaves two complete blocks in a row:
the new member gets the old summary and the old member gets none. It compiles, the analysers are
happy, and the only symptom is a reader being told something untrue. `Corpus.AuditTrail`,
`RateLimiter.StampsOf`, `Corpus.Revoke` and `Corpus.KeysPage` each lost or gained one; three were
found by eye. Its first version matched only a closing tag alone on its line and therefore missed
the very case it was written for — this codebase writes most summaries on one line — which the
break-it step caught: the defect was re-created and the test stayed green.

**Two guarantees had their teeth proved by breaking the production code**, per the testing rule's
"when the fix already landed before the test": an early `return` added to the credential loop made
the structural test name that symptom, and changing the keys cursor's `<` to `<=` made
`StabilityAcrossAnInsert` report the boundary row served twice. A third — the startup refusal of a
credential that is both an administrator and a contributor key — was written RED first and observed
failing by the server starting and serving, which is the defect itself.

**And the admin routes are in the real-binary scenario in the same story, not a later one.** Every
one of them can pass in-process while the deployed surface answers nothing — the administrators come
from an environment variable parsed at startup, the gate is wired by hand rather than by routing, and
the wire shapes are bound by a serializer that compiles whether or not it works.
`TheBuiltBinariesTests` therefore issues, lists, audits, reads `/admin/active` and revokes over a real
socket against the built binary, confirms the revoked key really stops ingesting, and checks the two
configurations an operator actually hits: an out-of-range admin limit exiting 78, and the variable
ABSENT — where the server must still boot, still collect, and refuse every admin route.

**A port that was free a moment ago is not a port you hold.** `TheBuiltBinariesTests` asks the OS
for a free port, releases it, and hands the NUMBER to a server process that binds it a moment later
— and anything on the machine may take it in between: another test in this suite, another suite on a
shared runner, an ephemeral outbound connection. Nine call sites took that number straight to a
server with no second chance, so it was nine opportunities per run for a green suite to go red about
something no change had touched. `Serving` now walks candidates until one is really held, bounded at
ten so a machine out of ports is a failure rather than a hang.

Two things make that more than a retry loop. The server's output is **kept** rather than drained into
nothing, so a startup failure arrives with the server's own sentence instead of fifteen seconds of
polling and "must reach the point of listening" — and `Listening` stops the moment the process has
exited, which is what makes a lost port cost tens of milliseconds instead of the full timeout. And
the classifier is **narrow on purpose**: retrying on any early exit would turn a missing keyword list
into ten attempts and a sentence blaming ports, so anything unrecognised fails at once, carrying what
the server said.

It is asserted by provocation rather than by luck. `APortTakenBeforeTheBind_CostsTheCandidateAndNotTheRun`
holds a port with an ordinary socket and offers it as the first candidate; `ARealCollisionIsRecognisedOnThisPlatform`
starts the real server on a held port and asserts that what THIS platform prints is something the
classifier recognises — a platform whose wording is missing reddens there and names the text, which
is how that list is meant to grow. Both borrow their shape from `src_mcp/tests/LoopbackStub`, which
closed the same race for an in-process listener; the code is deliberately not shared, because that
one catches an exception carrying an errno and this one can only observe that a separate process
exited and what it printed.

**The Users tab is RUN by its test, and the assertions are named rather than implied.**
`bugsKeysPage.test.ts` executes the shipped script against a DOM shim and asserts on what it POSTS.
Its load-bearing case renders **two** keys, clicks each row's own revoke control and asserts the
posted ids are `[first, second]` — a page that always posts the first row's id, or always the last,
passes every assertion anybody could write about the markup and fails this one. Teeth proved by
wiring every button to the first row's id and watching it name that defect.

The rest of the file covers what the plan round added: a revoked row offers no control at all, a
stray click posts nothing, Back and Next render disabled when the extension says there is nowhere to
go, a pending key offers copy AND discard and says discarding revokes, the rejected face carries the
server's words while **refusing to name a cause**, an unreachable server says it is not the key, and
a note that is markup is escaped.

`bugsSend.test.ts` holds the decisions a send is made of, with no editor in sight: what may start and
why not, the summary read field by field, and a sentence for every exit the CLI can answer. Its
liveliest test is one a fixture could not have produced — `bugzLiveContract.test.ts` runs the REAL
`coai-mcp --upload-pairs` against a fresh data directory and parses what it actually prints, and that
caught the reader on its first run: the server writes its summary with indentation on, so the real
output is six lines and the last one is `}`. Every unit test had passed, because every fixture was
written on one line. Both sides agreed with each other and disagreed with the binary, which is the
failure that whole style of test exists for. The same file asserts that an unknown mode really does
exit 64, because "update your server" is a sentence the panel only earns if that is what the binary
answers.

`bugsSendWiring.test.ts` pins what `vscode` puts out of reach: that `mayStart` runs BEFORE
`uploadRun` — a plan reviewer named the failure precisely, a stored key and an `http://` address
meaning the credential reaches a process pointed at an address it must not cross — that the key is
neither an argument nor a file, and that `COAI_BUGS_KEY` appears in exactly one place in this
extension. Its teeth were proved by moving the spawn above the preflight and watching it name that.

`TheSendsThemselvesTests` is the durable half, in the server: a send is running before anything has
been sent, a REOPENED database still says so, a beat moves the heartbeat, a kill is swept to
`interrupted` rather than left running for ever, and a live run survives the sweep. Breaking it by
recording a send as already over turns three of them red.

`bugsKeysTurns.test.ts` drives the coordinator that serialises the tab's actions — that a second
action waits, that the page is told at the START of one and told again at the END, and that neither a
failed action, a failed repaint nor a failed reporter can poison the chain and leave a panel silently
answering nothing. `bugsKeysWiring.test.ts` pins the half no unit test can reach: `bugsKeysPanel.ts`
imports `vscode`, so what is asserted is its SOURCE — that every door goes through the coordinator,
that the coordinator is built with both repaints, that setting the key does not reset the trail and
that discarding asks first. That is the house pattern of `chatFreshWiring.test.ts`, and it strips
comments before matching, because a structural assertion that reads prose goes red on a sentence
explaining why the code is the way it is.

`bugsAdminWire.test.ts` is the boundary: every success body read field by field, because the cast
it replaced let a `201` without a `key` through — and that key is the one thing this product cannot
ask for twice. It also holds `mayCarryAKey`, which decides the addresses a credential may cross:
https anywhere, plain http only to loopback, and everything else refused BEFORE the request rather
than after the answer.

`bugsKeysFlow.test.ts` holds the decisions themselves, with no webview in sight: that Back is a
remembered cursor rather than a computed one, that a `changed:false` revoke reports the ORIGINAL
time, that a 404 means a stale row, and that a failed issuance says a key MAY exist and points at the
newest row instead of inviting a retry.

**Three suites that exist because the code round moved the code out from under them.**

`TheEdgeIsWatchedTests` drives the real server and asserts on **what it logged**, because the edge
warning has no other observable: it changes no status, no body and no row, deliberately — refusing
the request would break a deployment over a header the contributor never sent. Its first version
called the helper directly and a reviewer applied this repository's own rule to it: delete the
production line a user would notice and watch THAT go red. Deleting the whole warning left every
one of those tests green. Deleting it now turns **7 of 11** red. It also reads the header list
**from** `Program.Forwarding` rather than retyping it, and asserts the shipped vhost clears every
name in it — the boundary has two halves in two languages and nothing else holds them together.

`WhatTheArgumentsMeanTests` covers what `TheBuiltBinariesTests` proves but a coverage run cannot
see: the exit-code rule lives inside a spawned PROCESS, so it was exercised and measured as
untouched. Both earn their place — one proves the code really reaches the shell, the other can be
run against every shape cheaply. It also caught a hidden dependency: `Unknown()` treated every
admin mode as unknown, which was safe **only** because `Main` checked `Admin.Knows` first, so
swapping two lines would have made `--issue-key` exit 64 and tell every caller this binary was too
old for a mode it has.

`TheKeywordListIsCheckedTests` covers the guard that decides whether the server starts at all. It
was covered, and then it was not: the code round replaced `Checked` (which threw) with
`WhyUnusable` (which answers a value, as the doctrine requires), and the tests stayed with the old
name. SonarCloud reported it as a coverage gap; what it was, was the startup guard having no test.
Its last assertion runs the check over the list **this binary actually carries**, so a release
whose embedded resource parsed to nothing is red before it reaches a host.

`TheArchiveCheckTests` is the fourth, and it exists for a class of code the other three cannot
reach: **shell that only runs during a release.** The archive check lived inline in `release.yml`,
where nothing executes it until the day it matters, and it was wrong —
`grep "$NAME/[^/]*coai-bugs"` matches `coai-bugs.dbg`, so an archive carrying the debug symbols and
not the executable passed the one check whose job is to prove the executable is there. It is
`.github/scripts/archive-carries.sh` now, and this suite runs **the real script** against archives
built to be wrong on purpose. It needs a POSIX shell: CI runs on `ubuntu-latest` where that is
always true, so a missing `sh` fails there and skips on a Windows checkout — the shape of
`StageRulesTests.RequireTheMount`, and for its reason. The same principle applies to any check that
moves into a workflow: a condition nothing executes is a condition nobody has read.

**`changelogNamesTheRelease.test.ts` is the fifth, and it belongs to the extension suite rather than
this one** — same class of code, other language. `.github/scripts/changelog-names-the-release.mjs`
refuses an `mcp-v*` tag whose release has no changelog entry, and it runs in `mcp-draft` after the
checkout and before the draft, where a refusal costs nothing. Its cases **spawn the script and assert
the EXACT exit code**: 0 documented, 1 missing, 2 cannot check. `notEqual(code, 0)` was the first
shape and it proves nothing — a crash, a bad argument and a script that is not there all satisfy
it. Two of the cases are about the guard's own blind spots rather than its contract: one runs every
version in `.github/changelog-baseline.json` against the real changelog, because a check that only
examines the tag being released cannot notice a release DELETING an older note; the other runs the
baseline against `git tag`, because entries were once written for `0.26.0` and `0.27.0`, versions
that live in the manifest and were never tagged at all.

**Three more joined it, and together they cover the whole release path.**
`changelogSection.test.ts` runs `.github/scripts/changelog-section.mjs`, which turns a tag into the
section somebody wrote — the answer to a measurement that made the guard half useless: every
release body was a LITERAL in the workflow, so 65 `mcp-v*` releases carried byte-identical text and
the entry the guard forces was written for nobody. `draftReleaseNotes.test.ts` runs the real
`draft-release.sh` with a stub `gh` first on PATH, and proves two things: the body reaches `gh`
through a FILE, because a release note is multiline markdown that can begin a line with `-`; and a
RE-RUN over an existing draft REWRITES its notes, because that script deliberately reuses a draft it
already made and would otherwise publish the body of a failed first attempt for ever. It needs a
POSIX shell, on the same terms as `TheArchiveCheckTests`. `baselineOnlyGrows.test.ts` covers the
other direction of the ratchet: the baseline lives in the repository it guards, so one commit
deleting a note AND its baseline row would pass everything.

**What the release path still does not prove.** None of these runs on GitHub's runners, so the shape
of a release is asserted by reading `release.yml` — that each drafting job extracts its notes,
writes them to a file, passes the path, and declares the Node it runs on. The first real proof of
any of it is the next release, and that is worth saying rather than implying.

**It found two defects the first time it ran.** `coai-bugs --rotate-the-moon` started Kestrel and listened for ever instead of exiting 64 — the binary had no unknown-mode branch at all, which is the half of the exit-code rule that lets a caller detect an old binary. The test noticed after four minutes and fifty-seven seconds, which is how long it takes to see that a process nobody asked to start is still running.

**The story-1 suites (2026-09-17): a schema that ships, a limit that is a setting, and a promise
that changed shape.**

`TheSchemaIsFrozenTests` exists because a freeze that compares a constant to itself is not a freeze.
Step 1 of `CorpusSchema` is what `bugs-v0.1.0` (`f7e5170b`) shipped and what the first host's
database contains at `user_version = 0`; the fixture under `fixtures/` is that SQL copied once, and
the test holds the constant to it with line endings normalised and nothing else. A third test runs
the fixture and asserts it produces the released tables and not step 2's, so the migration tests
cannot be migrating an empty file.

`TheMigrationTests` migrates the file the FIELD has, because a fresh file proves nothing: the
fixture's tables, one key the old build issued, no version stamp. It lands on the current version
with `last_seen_month` and `admin_audit`, the old key reads as never used and can be revoked, and a
second open runs no second `ALTER`. Its last test holds a write lock on the file for two and a half
seconds from a second connection and asserts the corpus's own write WAITS behind it rather than
failing with `SQLITE_BUSY` — the wall-clock price of observing a wait at all, and the one test here
that takes longer than a blink. `SqliteMigratorTests`, in the mcp suite, covers the shared runner
itself: it was `RoundsDb.Migrate` with one test of its own (the step that fails part-way, still
there), and it gained its contract when it became shared.

`TheQuarantineHoldsNoClockTests` is the strong form of the privacy promise, and it exists because the
promise was **false when it was first written**. The code round found `quarantine.received_utc` — an
exact ISO-8601 instant — sitting beside `key_id`, so for any pair awaiting review the database could
be asked precisely which day and hour a contributor worked, while the document said "no date, no
clock time, no history". The tempting fix was to reword the promise to carve quarantine out; the plan
records an earlier draft arguing "a date is materially different from a timestamp" and a reviewer
refusing that as evasion, and carving out the hole is the same move. So step 3 writes
`received_month` instead and this test asserts the general rule rather than the one column: it reads
the SCHEMA, takes every table carrying a `key_id`, and fails if any of them carries a clock. A fourth
such table added later is covered without anybody remembering to.

`TheGateComesBeforeTheBodyTests` pins an ORDER, which is the kind of thing no unit test sees. Binding
`UploadRequest?` from the body makes ASP.NET deserialize before the handler runs, so a hundred
one-megabyte bodies at a limit of ten were all parsed before ninety of them were refused, and a
malformed body got a framework 400 before the intended 401. Authentication and rate admission are
middleware ahead of binding now, and this suite drives real requests to prove it — an assertion about
"which runs first" that reads the source would survive the wiring being changed back.

`TheRateLimiterTests` races THREADS released by a barrier, not tasks: a race a scheduler may
serialise proves nothing, and the finding it answers was eleven simultaneous requests all reading one
window and all passing a limit of ten. Sixty-four against ten, twenty-five rounds, exactly ten
admitted each round; and thirty-two admits against a sweep that wants to evict their idle window,
every stamp kept. The rest is the sliding window on a frozen clock — a fixed bucket admits twice the
limit across its boundary, and one test says so in as many words.

`TheLastSeenMonthTests` asserts the STORED value against `UtcMonth.Shape()` and against the
month the frozen clock said, one second before midnight on the 30th and at midnight on the 1st. The
month is a promise about what the server records, so each way of not being an accepted ingest has a
test: a 401, a 429 (thirty seconds into October, still inside September's minute), a malformed body,
and the one-shots. Sixteen concurrent ingests count sixteen, because the counter is
`submissions = submissions + 1` in SQL and the whole batch is one commit.

`TheRevokedKeyTests` presses the real stop button — `Admin.Run(["--revoke", …])`, in-process,
against the database the server holds — and asserts the ingest that worked before does not work
after, wrote nothing and counted nothing; and that five requests on a revoked key are five 401s and
never a 429, with the limiter tracking nobody, because the refusal happens before it.

`TheAuditTests` pins the privacy boundary of the one table with a clock: `action` is a verb derived
from the enum (never retyped), `target` is a key id, and no row carries the note, the key or its
hash. The bound is crossed twice — over a small `keep` through the seam, where the surviving ids and
targets are named, and over the REAL 50 000 from a seeded table, where two real issuances push the
two oldest rows out. Dropping the audit table underneath `Issue` rolls the key back, which is the
one-transaction claim made observable.

`TheBuiltBinariesTests` gained the scenario this story needed most: unit tests can all pass while the
deployed `/ingest` never invokes the limiter, because the middleware is wired wrong, and no in-process
host can show a second PROCESS opening the database while the server holds it. So the database is
written first exactly as the host has it, the real binary migrates it on start (asserted by reading
`user_version` off the file), `--issue-key` and `--revoke` run as real concurrent openers, the month
is read back through a third, the third request inside a minute at a limit of two is a 429 carrying
`Retry-After` and the limit in its body, the revoked key is a 401, a second server on the same
directory exits 78, and a rate of 1 001 exits 78.

**Teeth, observed on 2026-09-17**, with the production side broken and then restored, six guards at
once: step 1 with a column appended — *differ on line 31 and column 44*; the limiter's
compare-and-swap replaced by a plain write — *Expected admitted to be 10 … but found 64*; the
sweep's cutoff off by one — *found 6* over the small bound and *found 50001* over the real one; the
revoked-key filter removed from `KeyFor` — *Expected … Unauthorized {value: 401} … but found
HttpStatusCode.OK {value: 200}*; the busy budget cut to one millisecond — *SQLite Error 5:
'database is locked'*. All six green again after the restore, and every failure named the defect
rather than a setup error.

**And one guard in the mcp suite caught this story's own documentation.**
`StageRulesTests.TheRotatedTail_CurrentlyFitsAtMostOneRule` collects the REAL repository's
instruction files and tier rules under the gate's 80 000-byte budget, which they fill to within
1 145 bytes on a CRLF checkout. A kilobyte added to `.agents/PROJECT.md` for the new variable pushed
a TIER rule out of every round's bundle, and the canary went red in the whole-suite run that
started after that edit. The paragraph became one clause (+247 bytes) and the canary is green;
the headroom itself is worth knowing about.

**What these still do not prove.** The no-client-IP promise is a deployment fact — a reverse proxy
writes `remote_addr` before the request reaches any route — and no test in this process can reach
it. It is verified by reading the deployed stack's logs after a real ingest. The unit's `UMask=0027`
is likewise a request to systemd, read back with `stat` on the host rather than asserted here.

## The notification ledger's suites (2026-09-17)

| Suite | Drives | Catches |
|---|---|---|
| `notifications.test.ts` | the real serialiser and parser | a secret written verbatim; a torn line taking a year of history with it; a class a newer build knows and this one drops |
| `notice.test.ts` | the pure half of the funnel | a context field that never reaches the record; an answer written over the asking instead of beside it |
| `suppression.test.ts` | the bounds, as the LEDGER they produce | a sampled design where 10 occurrences and 99 look identical; a question asked and obeyed with neither side recorded; a repeat that never reaches its storm |
| `notificationsFile.test.ts` | two real files and the real append chains | a multi-byte record split across a read window; a drain that waits on a disk until the host is killed |
| `symlinkEscape.test.ts` | a real workspace on disk, with real links | a link inside the workspace leading out of it and opening the file outside; a containment check that refuses EVERYTHING and passes the escape case by breaking the feature; a sibling directory (`ws-secret` beside `ws`) read as inside; a path that resolves nowhere accepted rather than refused; and the CANONICAL path being silently replaced by the path as written, which is how the refactor that introduced `insideReally` nearly changed an older caller's answer. Proved by removing ONLY the canonical comparison: exactly one case goes red, naming the escape, and the other four stay green — so they are not all riding on one condition. **Not covered**: that `openWorkspaceFile` actually calls it, which needs an extension host |
| `credentialWords.test.ts` | the one shared word list | `?author=octocat` refused as a credential; `client_secret` written to a file |
| `notificationSites.test.mjs` | the site counter, against the real tree and against fixtures | a call site added outside the funnel; a `.tsx` file the scan cannot see; a number in a comment counted as a call |
| `notificationsSeen.test.ts` | the watermark, and two windows acknowledging at once | a watermark that moves backwards; one ledger's offsets applied to the other; a count that parses records |
| `notificationsRead.test.ts` | the grouping | one code under two classes becoming one row that moves between tabs; a rate measured over 40 ms; a row marked read because its OLDER occurrences were |
| `notificationsPage.test.ts` | the page RUN against a shim built from its own markup | a table that renders and does not filter; a search that cannot find the sentence on screen; a filtered view acknowledging three thousand records |
| `pageTables.test.ts` | the comparator and the wall-clock bound, characterised | a genericisation that sorts almost the same |
| `notificationsGlance.test.ts` | the cheap look over real files, and the seen-ledger cache | two caps of 3000 reporting 6000 new; a rotated ledger counted as "Nothing new"; a restored acknowledgement file read from a stale offset; a cache that re-reads everything while claiming not to |
| `notificationsSnapshot.test.ts` | the decisions one draw makes, extracted out of the host to be run | a draw that could not read claiming its window anyway; a rotated ledger's range marking the replacement read; "older" missed when the window starts past the beginning of the file |
| `writeGap.test.ts` | the arithmetic of the ledger's own hole | a flush that ZEROES the counter and swallows the notices lost while it was reporting the earlier ones |
| `theGapReachesTheLedger.test.ts` | the two wiring rules, against the source | a gap record written after a FAILED append, which counts its own failure and grows the number it reports; a gap sent through the funnel write, which recurses |
| `liveRegionsRebind.test.ts` | the panel's script RUN over its own live regions | a handler re-bound on every five-second tick — measured: after six ticks one press posted six messages |

**One of those reads SOURCE rather than running anything, and says so in its own header.**
`theGapReachesTheLedger.test.ts` pins two wiring rules in `notify.ts`, which imports `vscode` and
can therefore not be imported by any test here; what it asserts is a specific pairing — this call
inside that branch — never the presence of a word, because a structural assertion matching a
fragment survives its own break. Everything worth running was MOVED somewhere it can be:
`writeGap.test.ts` exists because the arithmetic came out of `notify.ts`, and
`notificationsSnapshot.test.ts` because the decisions came out of `notificationsPanel.ts`.

**`liveRegionsRebind.test.ts` used to be a third, and that was wrong twice.** Its own header claimed
nothing here could run the panel's script — `panelStorageScript.test.ts` already did — and
`.agents/PROJECT.md` refuses new behavioural assertions over page source text outright. The
structural version could not have seen the defect it was written for: two listeners on one node
leave every statement exactly where it was. Running it shows the number instead — six ticks, one
press, six messages. (CodeRabbit, on the S5 pull request.)

**The page test builds its shim FROM the rendered markup and throws on a selector it does not
understand.** Both halves of that are earned: a hand-written fixture handed to a shim passes with no
control on the page at all, and a shim that answers "the first element" to an unsupported selector
has tests binding to the wrong node and passing — which is worse than no test, because it reads like
one.

**What `pageTables.test.ts` is for, precisely.** Breaking the comparator the way an optimiser would —
`return x < y ? -1 : 1`, which throws away 0-for-equal — turns two of its tests red and leaves all 41
of `roundsLog.test.ts` GREEN. The rounds log never asserted stability, so its own suite would have
shipped the regression the extraction could have caused.

The inventory that counter writes is read by a test in the other suite: every `code` literal the
product can emit is in it, and `notifications.test.ts` asserts that redaction rewrites none of them.
That is what a durable key needs, and it is there instead of an exemption that would have put a hole
in the redaction invariant.

**Why the counter has tests of its own.** It is the enforcement of the whole completeness promise,
so a defect in it is a promise that goes on being made while being false. Two were found this way:
the scan read only `.ts`, and it counted API names written in PROSE — this subsystem's files being
unusually full of prose about exactly those names.

**What these still do not prove.** **No test here runs the real VS Code host**, and none can: every
module that imports `vscode` is unimportable by this suite, which is why the funnel is split into a
pure half that is tested and a thin host half that is not. So nothing proves a toast actually
appears, that its buttons are the ones passed, or that a modal blocks what a modal blocks. What the
funnel DOES to a record before showing it is tested exhaustively; that it shows it at all is
asserted structurally, by a scan that counts the call sites and a ratchet that only falls. The three
UX defects found on the code round — a redraw waiting behind an error toast, a modal covering a
reload offer — were all found by READING, and none of them could have been caught here.

## What this does NOT prove

The most valuable section, and the first one people drop.

- **No vendor is real in CI.** The server scenarios drive a **fake CLI** (`CommandFixtures`,
  `FAKECLI_*`) scripted per round. So the composition is proven — dedup across vendors, a standing
  rejection surviving a round boundary, a partial round still producing a verdict — and the vendors'
  actual behaviour is not. That is what the campaign harness is for, and it is why campaigns are run
  by hand against real subscriptions.
- **No model quality is asserted anywhere.** A green suite says the gate works, never that a review
  was good. Quality is measured, not tested: `research/RESULTS_*.md`.
- **No GPU in CI.** The `local` arm never runs there; an Ollama or vLLM endpoint is exercised only in
  a hand-run campaign.
- **No extension host in CI**, per the table above — so a defect that needs the real `vscode` API to
  appear (a command wired to the wrong handler, a webview CSP change) is caught by installing the
  build and using it, which is exactly how the 0.18.1 SQLite defect was found.
- **No LAYOUT anywhere, so no CSS behaviour is ever observed.** The page harness executes a page's
  SCRIPT against a hand-written DOM shim; the shim has no layout engine, so `scrollWidth`,
  `clientWidth`, `getBoundingClientRect` and computed styles are whatever the test set them to. What
  a CSS change can therefore be tested for is that the DECLARATION is in the stylesheet — which
  `chatPage.test.ts`'s `rules()`/`ruleFor()` parser reads as data — and that a rule which must NOT
  inherit something says so. That a long unbreakable run actually wraps, or that the conversation has
  no horizontal scrollbar, is **not** proven here and cannot be without a real browser. The wrap fix
  of 2026-09-15 (issue #299) is asserted exactly this far and no further: `.msg .what` and `.passage`
  carry `overflow-wrap: anywhere`, `.msg .what pre, .msg .what table` carry `overflow-wrap: normal`,
  and the `pre` keeps `overflow-x: auto` without gaining `white-space: pre-wrap`. A test that
  inserted a long string and asserted "no overflow" against the shim would assert something the shim
  decided, and would look like evidence while proving less than the declaration does.
- **The Claude model probe is never driven against the real CLI.** Each candidate costs a billed
  request against somebody's subscription, so a suite that ran it would fail on an account whose
  allowance is spent and would spend one that is not. What is tested is every decision around it,
  against **recorded** answers taken by hand on 2026-09-16: the `modelUsage` parse, the
  asked-vs-answered comparison, the week-and-version freshness rule, the file round trip, and the
  rule that a run which learned nothing keeps the previous answer. What is therefore NOT proven is
  that today's CLI still answers in that shape — if Anthropic changes `--output-format json`, this
  suite stays green and every candidate quietly becomes unverified. The failure mode is the safe one
  by construction (unverified never subtracts from the curated list), which is why this limit is
  written down rather than closed.
- **One platform per run.** The suites run on the CI matrix; the AOT publish covers six RIDs and the
  release smoke exercises the binary on every RID whose machine can execute it — but `osx-x64` is
  cross-built on an arm64 runner and is not executed there.
- **No concurrency between two people.** Two sessions reviewing the same branch on one machine is a
  real case (it happens daily here) and there is no scenario for it; the pieces are covered by
  `SessionStoreConcurrencyTests` and `SessionSaveSurvivesReadersTests`.
- **No two extension hosts appending to one ledger.** Every VS Code window is its own extension
  host and they share the coai data directory, so `chat-usage.jsonl` has as many writers as the
  person has windows open. A unit test cannot fork four writers and mean anything by the result, so
  this one was MEASURED rather than tested: `npm run measure:append` (`scripts/measure-append.mjs`)
  forks real processes that append records of deliberately awkward sizes — the paddings straddle a
  pipe buffer, a page and a filesystem block — and checks every line back, verifying each record's
  padding LENGTH so that two halves of two records cannot pass merely by parsing. Eight processes ×
  1000 records, 116 MB including 60 KB lines: **8000 whole records of 8000, zero torn**, on
  Windows 11 / NTFS / node 22, 2026-09-10; four × 500 was clean first. It exits non-zero if the file
  ever tears, so the claim can be re-checked on another filesystem rather than believed.

Adoption build checks, before reconciliation with main: Debug and Release solution builds
completed with zero warnings/errors. Each configuration's MTP runners passed: MCP 1136/1137
(one explicit Windows directory-link privilege skip), bench 113/113, Team server 226/226.
The generator invalidation guard was removed temporarily: the missing-source test failed
because the old output remained; restoring the guard made it pass. This validates the
failure behavior, not merely that a build hook was declared.

After reconciliation with main, commit `63b40fc`: both configurations built with zero
warnings/errors and passed MCP 1141/1142 (the same platform skip), bench 113/113 and Team
server 226/226. Extension: 1340/1341 plus three generator scenarios. A fresh WSL clone
initialized the committed neutral pin, resolved it and read six complete TypeScript-scope
sources from a nested directory. A separate worktree restored the complete pre-adoption
commit and initialized its legacy mount with a clean tree. Native Claude's TypeScript cell
exceeded the bounded trace size and remains incomplete; details and measured scope are in
[shared-rules-adoption-smoke.json](shared-rules-adoption-smoke.json).


The first completed code review read 14 rule files (77,577 bytes), with 15 omissions and no
missing mount; all 12 reviewers answered. The old installed collector had shown only the
root adapters. Review follow-ups reproduce a stale temporary file blocking the next build
(EEXIST), an older local `review-gate.md` incorrectly hidden by the current shared body,
and serialized candidate reads. Each regression was observed red, green with its fix, then
red with the production behavior removed. Missing-mount detection now reuses the collected
paths; this is an enumeration refactor verified by the existing RuleFiles suite, not a
claimed latency measurement. Detailed decisions are in `shared-rules-adoption-review.json`.


Review corrections on the pinned S2 source (2026-09-10): extension 1342 total / 1341 passed /
1 skipped, plus 3 generator cases; client/server contract 3 passed; settings seam passed.
Debug and Release MCP: 1142 total / 1141 passed / 1 Windows directory-link privilege skip; bench 113 and
Team server 226 passed in both configurations. A Release build first found an all-zero
intermediate core reference DLL (CS0009); rebuilding that project restored its managed
metadata, and the solution then built with zero warnings/errors. This was a local build
artifact failure, not a source-code fix. Package 0.32.0 contains the canonical v5 marker and
neutral/local discovery paths; installation remains separate from this artifact observation.


After reconciling main through 897c7fa, the extension suite reports 1349 total / 1348 passed /
1 skipped. Windows and WSL each pass all three generator scenarios. The WSL run first
exposed a fixture error: a POSIX node_modules symlink is not ignored by the source's
node_modules/ directory pattern and made a clean fixture look dirty. The fixture now copies
the two locked packages into a real directory; pin validation remains unchanged.


The pre-PR-feedback local artifact was extension 0.32.1 (0.32.0 was an uninstalled validation package
before the last main reconciliation). Its packaging suite reports 1348 passed / 1 skipped.
The Windows Native AOT MCP artifact is 0.18.16-sharedrules.20260910: it opened a real SQLite
database and passed all five stdio contract cases through COAI_CONTRACT_EXE. The first
publish failed because vswhere.exe was missing from PATH; the already-installed Visual
Studio Installer directory was added only to the publishing process's environment. No SDK
installation, persistent host setting, or Team-server deployment was performed.


PR #192 follow-up (2026-09-10): the discovery fixture includes Rust and a legacy-only
shared mount. Generator scenarios live under `src/test/prepareGate.test.mjs` and still run
through npm test. The canonical-body comparison uses this checkout's fixed test layout,
so it cannot borrow a parent repository's mount when its own is missing.
The full suite exposed an unrelated timing assumption: the stdin echo test killed its real
child after 300 ms, before a response arrived (`[]` instead of `echo:hello`). It now waits
for an actual reply before killing the child, with the existing ten-second completion
ceiling and cleanup. All 18 process-launcher scenarios then passed.

Local package 0.32.2 contains exactly the 5,934 UTF-8 bytes of the canonical gate body.
Verification read `extension/dist/extension.js` from the VSIX, decoded its single gate
literal with the installed TypeScript scanner (`typescript/unstable/ast/scanner`), and
compared it to the pinned rule after LF normalization and frontmatter removal. Package
hashes are in `shared-rules-adoption-smoke.json`; no model-behavior claim follows from this.
The subsequent echo-test correction changes test source only, which is excluded from the
VSIX; the already-built package's runtime bundle is unchanged.

After the echo synchronization correction, the full normal npm test run reports
1,349 total / 1,348 passed / 1 skipped, plus all three generator scenarios. The updated
Release RuleFiles executable reports 22 passed. Family pin, lifecycle and checklist-shape
checks pass. These are local checks; PR CI and installed-host behavior are separate.

Final main reconciliation through `dea70a1` preserved its Windows/WSL chat changes and
resolved its legacy gitlink update by retaining the same SHA at the neutral mount. The
normal packaging run passed 1,397 extension tests / 1 skip, plus all three generator cases.
Package 0.32.3 was independently checked for all 5,934 canonical body bytes, as above;
its immutable hashes replace the earlier uninstalled validation package in the evidence JSON.

Local installation on 2026-09-10: VS Code installed extension 0.32.3; its actual installed
bundle matches the VSIX SHA. MCP was atomically replaced with the verified Native AOT
artifact, preserving the previous binary; SQLite already matched and was not rewritten.
All five McpContractTests then passed with COAI_CONTRACT_EXE pointing to the installed
globalStorage executable. Existing editor/agent processes were not restarted, so the
manual panel scenario and an already-running host reloading remain unverified.

## The roles page, RUN — and its harness (2026-09-16)

`rolesPageHarness.ts` is the DOM shim and the runner, extracted out of `rolesPageScript.test.ts`
the moment `editRolesInTabs.test.ts` needed the same thing: a second shim is two shims that drift,
and the one thing a shim must be is the same for everybody asserting against it. It takes the
nodes `querySelectorAll` should answer with, keyed by selector — a test that presses a tab needs
the page to FIND the other tabs and the sections, and a shim answering every selector with nothing
would let a broken switch look exactly like a working one.

Since 2026-10-07 the shim itself (`Node`, `runPageHtml`, `presses`, `Page`) is `pageScriptHarness.ts`, and every page
test imports it from there; `rolesPageHarness.ts` keeps `runRolesPage` alone, so E5.1 can delete it with the roles
page (research/PLAN_one_model_catalog.md, E5 prerequisite (b)).

`editRolesInTabs.test.ts` covers issue #293 and executes what it can: the page's own click handler
for the tab switch, and `nextTab` for the transition the host applies. The two assertions that are
not executions are deliberate and say so — there is no CSS engine here, so `.prompt.mine` being
absent from the stylesheet is invisible to a class-name assertion, and the test reads the generated
rule as well as the class. `roleTone.test.ts` is about what the palette SAYS; the check that there
is only ONE palette renders the same roles through `panelHtml` and `rolesHtml` and compares them,
because a test of the module alone stays green while either renderer keeps a private copy.

## What the paste has to keep saying (2026-09-16)

`snippetVersion.test.ts` gained four assertions with the sixth consultant trigger, and the test it
extends was renamed: it had been *the six triggers a stuck AI is told to watch for*, which the new
entry contradicts on its face — that trigger is explicitly NOT a moment of being stuck. It is now
*the six reasons to call a consultant survive into the paste*, raised on the code round.

The four are the sentences a later edit could quietly drop while every version number still moved
correctly: the trigger itself, the boundary the caller keeps around a quoted finding, the placeholder
that goes in before a secret a reviewer quoted leaves this machine, and the sentence that stops
`problem` being read as "paraphrase it in your own words". The last three came out of the code round
and each was proved RED by deleting the sentence from `consultantRule.md`, regenerating through
`prepare:gate` and watching the assertion fail with its own message — *the paste lets a quoted
finding forge its own delimiter*, *the paste forwards a secret a reviewer quoted, verbatim, to
another vendor*, *the paste still lets a caller paraphrase the finding instead of quoting it*.

**There is no scenario test for the flow, and that is not an omission.** A reviewer asked for one.
What this story adds is INSTRUCTION — text a model reads and decides on — not a code path: nothing
here calls `consult`, no branch was added, and the only executable part is the composition of the
paste, which these assertions run end to end through the real generator. A scenario harness would
have to drive another vendor's model to observe anything at all, which is what `PLAN_consultant`'s
phase 0 is for and is measured by hand on purpose.

## What a tool description promises (2026-09-17)

`TheGateSaysWhenToConsultTests` reads `Tools.cs` as TEXT, the way `TheServerSaysWhatOnlyTheRuleSaidTests`
does and for the same reason: the registry needs a live host to enumerate, and this is a check about
text. Seven cases — the consultant trigger, what makes an answer usable, every verdict the machine can
emit, the pointer in BOTH gate rounds, the plan counter-example, that no description offers a severity
the parser rejects, and that the scan looking for it still finds one.

**Every case is scoped to ONE tool's description, and that is the part worth copying.** A file-wide
`Contain` passes when a pointer lands in `review_document` while `review_code` keeps its old text —
exactly the accident this story could have shipped, since its parent plan had already widened its
scope and updated only one of two. Raised on the plan round and accepted.

Three things the code round changed, each of which had left a case weaker than it reads:

- **The verdict list is derived, not retyped.** Six literals in a test are a second copy of a list the
  code holds, and they stay green the day a seventh verdict is composed and this description omits it
  — which is the defect being fixed here, one verdict earlier. The expected set now comes from the
  nested records of `RoundVerdict`, snake-cased into the wire spelling `PanelService` composes.
- **The shared pointer is pinned WHOLE.** A fragment was pinned first, and two descriptions carrying
  one policy drift when a later change expands the trigger in only one of them — both still contain
  the fragment, so both stay green. Proved: inserting two words into `review_code`'s copy is now red.
- **The forbidden severity is scanned per DESCRIPTION, with a companion.** The plan round argued the
  scope both ways and the code round settled it — a raw-file scan also reads comments, so a remark
  that merely NAMES the rejected severity would fail the suite, while four descriptions would let a
  fifth acquire the word silently. Every description, no comments. And the prohibition now has the
  companion the house rule requires: a scan with no case proving it still detects a known instance
  passes forever the day its pattern stops matching, exactly as loudly as when it enforced something.

Phrases are matched against a whitespace-flattened copy. These literals are prose hard-wrapped at
about a hundred columns and a raw string keeps every later line's indentation, so a phrase worth
pinning will wrap eventually — and an assertion that fails on a reflow is complaining about layout,
which is a test nobody trusts. The same fix was made one story earlier in the extension's half.

RED observed before the descriptions moved: **5 of 6 failed**, each quoting only the description it was
scoped to, which is itself the evidence that the slicing works. Teeth then proved on all three of the
code round's additions by breaking them one at a time against the real runner.

**A note on how one of those teeth was nearly mis-read.** The first attempt to prove them drove the
test executable from a Python harness that passed a list argv with `shell=True`; on Windows the
executable never ran, every break reported "nothing failed", and the honest reading of that output is
that three good assertions had no teeth at all. Run by hand, all three were red on the first try. A
harness that cannot start the thing it measures reports silence, and silence looks exactly like a
pass — which is the same shape as a `pin-check` answering about the wrong directory.

## The prompt a consultant actually reads (2026-09-17)

`TheConsultantIsHeldToTheSameStandardTests` asserts the consultant-side half of the "agreement is
earned" rule: that the shipped prompt asks for evidence rather than assertion, and that it names
quoted material as evidence rather than instruction. Five cases, all against
`RolePrompts.ShippedDefaultFor("consult")` — the text compiled into the binary, never the source path
and never the override-first instance accessor, which would let a local `<dataDir>` override decide
whether the suite passes.

**One case runs the composer.** Asserting the resource is embedded proves it exists, never that
anything sends it: a build can carry both new bullets and pass every other case here while the consult
path composes a prompt without them. `ConsultantPrompt.Compose` is the one place that assembles what
the model reads, so the case builds a real `ConsultantPromptInput` and asserts the instruction is in
the result. Raised on the plan round and accepted.

**RED, and the demonstration inside it.** Three of the five failed before the bullets existed. The two
that passed on arrival — the five pre-existing rules, and that the prompt offers no severity the
parser rejects — were then broken deliberately, and the sequence is worth recording because it proves
the design rather than only the teeth:

| Step | Result |
|---|---|
| delete *"Keep it short"* from `consult.md`, **run without rebuilding** | still **green** — the case reads the binary, not the file |
| rebuild, run again | **red**, naming the lost rule |
| put `critical` into the prompt, rebuild | **red** on the severity guard |
| restore, rebuild | green |

That first row is the whole argument for reading the embedded resource. A source-reading test would
have gone red at step one and told you nothing about what shipped; this one goes red only when the
ARTEFACT changes, which is the question a release actually asks.

Whole executable after: 2140 tests, 2138 passed, 0 failed, 2 skipped.

## Who calls this (2026-09-21, story 3.3)

| Suite | What it covers |
|---|---|
| `callHierarchy.test.ts` (21) | the decisions as values: the column found on the line and never assumed; a name that is only part of another word rejected; exactly ONE match accepted so an overload pair is refused rather than guessed at, and WHICH one it was — a symbol and its handle
being ONE value, so no filtering can pair the wrong two; every state a sentence that prints no token; zero told from silence; distinct methods rather than call sites; an answer applied only for the ATTEMPT its row awaits. And the flow with the editor as a parameter: both directions independent, the symbol proved before either is asked, the provider asked about the
item that MATCHED whatever its position, a missing file `gone`, an empty preparation `no-provider`, a provider that never answers bounded and `failed` |
| `callsPanel.test.ts` (12) | what one page REMEMBERS, run rather than read: the asking state on screen before the provider is reached; a press that throws leaving the row pressable; a superseded attempt refused; a collapse dropping both the press and the answer; a collapse REPAINTING the row rather than leaving its markup in the page, and saying nothing about a
row the page cannot draw; a closed window forgetting; an end opened by index out of what this side
holds, with a row that has no answer, a direction with no ends and an index past the end each opening nothing |
| `callsBlock.test.ts` (6) | the markup: each phase saying one thing and only an idle row pressable; the list stopping at 50 with the count untruncated and a tail that says how many more; a list that fits shown whole; a name a provider invented escaped; a row that could not be asked showing a reason rather than a zero; `calledOut` reading back one entry and refusing nine malformed ones |
| `openAtRevision.test.ts` (+4) | `folderHolding`: the folder that HOLDS a file rather than the first one tried; a file under no open folder belonging to none; the most specific of two nested roots winning; and `alphabet` not being inside `alpha`, because the comparison is a path boundary and not a prefix |
| `bugzReviewPage.test.ts` | the control pressed, by RUNNING the page: nothing asked at paint, the press naming its row. And the live patch itself, DRIVEN through `Host.push` — an answer painted into its own container and no other, and an identical patch skipped so a focused button inside it survives. The harness could already deliver a host message; only the shim's `querySelector` had to learn `[data-calls-for]`, and `Note` had to count its writes |
| `bugzReviewWiring.test.ts` | the seams a value test cannot reach: `case 'calls'` reaching the one method, and the collapse path telling the panel to forget — pinned as ONE condition rather than two fragments; an opened end going by INDEX rather than by a path the page carried; the block handed to every paint; a closed window forgetting; the provider wired to the real editor, and opening only through story 3.1's workspace guard rather than a second one |

**The source-text assertions about `callsPanel.ts` became tests that RUN it**, on a code-round
reviewer's finding, and it was right: the module imports no `vscode` and reaches the world through two
functions it is handed, so a regex over `async ask(...)` was matching prose about behaviour that could
be exercised instead. A regex goes green on a body rewritten to do the opposite; `callsPanel.test.ts`
does not. What stays a reading is the one seam inside `bugzReviewPanel.ts`, which does import `vscode`.

**Teeth, measured.** Disabling the column search and the single-match rule turns EIGHT tests red,
including the two that name them. The code round's three fixes were planted back one at a time and
restored from a saved copy afterwards:

| planted | round | red | what the failure said |
|---|---|---|---|
| a plural subject with a singular verb | 2b | 2 | `2 methods call this` expected where the code said `calls` |
| an out-of-range line reported as a missing FILE | 2b | 1 | the out-of-range answer and the absent-file answer must not be the same value |
| a held answer shown under whatever the row is now | 2b | 2 | a row whose id now names a DIFFERENT method still showed `1 method calls this` |
| a live patch that repaints identical markup | 2b | 1 | the same markup a second time must not touch the DOM |
| `theRightSymbol` returns 0 rather than the matching index | 1 | 2 | the handle must be the one beside the item that matched |
| `closed()` drops the press but keeps the answer | 1 | 1 | a collapsed row still showed `1 method calls this` |
| the list rendered unbounded | 1 | 1 | 120 buttons where 50 and a tail were expected |
| `closed()` forgets the answer but does not repaint | 2 | 1 | collapsing REPAINTS the row, because the markup it was holding is still in the page |
| `folderHolding` takes the first folder rather than the holder | 2 | 1 | a nested root wins over the one containing it |
| the folder test is a prefix comparison | 2 | 1 | a folder name that merely STARTS with another is not inside it |

A fourth was planted and the COMPILER refused it: removing `tell` from `closed` leaves its `rows`
parameter unused, which `noUnusedParameters` rejects. Recorded because a plant that will not build is
not evidence either way — the defect had to be re-shaped into one that compiles (`tell([], rows)`)
before the test could be asked about it.

**And one place where there are none — said rather than implied.** The test that presses the control
and asserts the row is undisturbed stays GREEN when the branch's `return` is removed: the control lives in the detail row rather than inside the summary row's toggle, so there is
nothing for the event to bubble into. Measured by taking the `return` out. It stays because it is the
house pattern and because the day the control moves is the day it matters — the test documents the
contract rather than guarding it.

## Story 3.3's remaining gate facts (2026-09-21)

A second measurement scenario, `a call hierarchy needs the symbol COLUMN, and answers for a file
nobody opened`, added because story 3.3's plan round raised two questions a measurement could settle.
It settled both and produced a third:

| asked | answered |
|---|---|
| `prepareCallHierarchy` at column 0 of an INDENTED method | **1 item — the enclosing class**, not the method |
| the same call at the symbol's own column | 1 item, the method |
| a file in the workspace that nobody opened | prepared, and its caller in another file found |
| a file that does not exist | **throws**, rather than answering empty |

The first is the one worth carrying: a reviewer predicted column 0 would answer nothing and read as
`unavailable`; it answers the CLASS, which would have put a class's caller count under a method's
name. The name check the plan already required is what catches it, and the two guards were designed
independently — which is the argument for keeping both.

## Story 3.3's gate, measured in a real editor (2026-09-21)

A tenth host scenario, and it is not a guarantee about shipped behaviour — nothing of story 3.3 is
built. It is the MEASUREMENT the plan makes a precondition, kept as a test because the property it
measures belongs to VS Code rather than to us, and the day it changes is the day a feature built on
it starts lying.

`a call hierarchy can tell NO PROVIDER from NO CALLERS, which is what story 3.3 rests on` writes a
TypeScript file (one function called twice, one called never) and a C# file into the run's own
throwaway workspace, and asks `vscode.prepareCallHierarchy` / `vscode.provideIncomingCalls` about
each. It asserts the four facts that decide the design and REPORTS the timings rather than asserting
them, because a threshold invented in a test is a preference dressed as a measurement:

```
[3.3] typescript: prepared=1 cold=803ms warm=37ms calledTwice=2 calledNever=0
[3.3] no-provider language: prepared=0 incoming=-1
```

**Known about this machine, not about the change:** three clipboard scenarios fail here every run,
and the test says why itself — *"something outside this run wrote to the clipboard in all three
attempts. Another test:host, or a clipboard manager. Run it alone."* A clipboard manager is
installed on this machine. CI has none and runs them green.

## Giving a review tree back (2026-09-21, story 3.2b)

| Suite | What it covers |
|---|---|
| `AReviewRootBehindALinkTests` (5) | a review-tree root reached through a link the test makes itself — a junction on Windows (no privilege needed), a symbolic link elsewhere | a live tree behind a link listed as `unregistered`, and a deleted one that cannot be made again (`git_failed`), both because git records the REAL path and the product compared spellings; `WorktreePaths` resolving a missing tail only as far as it exists, and calling two different places the same. Both integration cases went red first on Windows with exactly the symptoms the `mcp-v0.31.0` release build showed on `osx-arm64` |
| `AReviewTreeIsGivenBackTests` (13) | real git, temp repositories: the four refusals - a modified tracked file, an untracked file, and both of those INSIDE a populated submodule - each asserting afterwards that the tree, its lock, its registration AND its record are all intact; a clean tree with a populated submodule removed (the case a plain `worktree remove` cannot do); ignored files refused until asked for explicitly; a round tree and a person's own worktree neither listed nor removable; a name that is a path refused before any process (recording launcher); a vanished tree forgotten while an unrelated unreachable worktree stays registered; the list telling ready from incomplete from vanished from unregistered; a status that could not run never read as clean |
| `reviewTrees.test.ts` (16) | the readers and the whole picker flow as VALUES: what an answer must echo, 64 as the only too-old code, `--with-ignored` sent only when asked, every word becoming a sentence that never prints the word, and the three paths where walking away must remove nothing |
| `bugzLiveContract.test.ts` | both vocabularies against the C# constants (a word added on one side only goes red naming it), and the real `--trees` and `--tree-remove` through the real reader |

**Teeth, shown rather than claimed.** Three mutations were planted across the two rounds and each was
caught by the right test with the right message. Disabling the per-mount submodule inspection — the
code round's central fix — turns FOUR tests red at once, including
*Expected first.Reason to be the same string because a parent status cannot see this at all*. Two mutations were planted and both were caught by the right
test with the right message: stopping the descent into a flagged submodule turned the refusal into
*Expected said.InTheWay {"mods/sub"} to contain "mods/sub/s.txt"*, and removing the ignored-file gate
turned the first ask into a removal. A third, adding a reason word to the C# side alone, went red
naming `invented`.

**One test outside this story failed once in three whole-suite runs, and its name was captured this
time.** `RemoteShimScenarioTests.AShimKilledMidClaim_LeavesEitherNothingOrAWholeClaim_NeverHalf`
timed out waiting for a claim file: its child process could not reach a local stub server within the
**40-second HttpClient timeout**, on a machine this story's suite keeps busy with git spawns. It is
not a flake and it is not in this diff - it is a load-sensitive test with a hard timeout, recorded
here for whoever owns it rather than fixed inside an unrelated story.

**`TheLocalReviewerStandsDownTests` (issue #485)** — a local launch held on the card finishes while the queued
local rows stand down after a cloud of one remark; a stood-down row is not asked, never failed; two remarks,
a failed cloud reviewer, the switch off and a round with no cloud reviewer each keep every local row running
(red first; a plant that is never quiet turns the first two red). Extension: `stopLocalWhenQuiet.test.ts` (the
default, the env key, the box RUN, the skew note) and `rounds.test.ts` (the reason beside the status).
2026-09-26: the box says *"from the next round"*; `snapsBackWhenRefused` is true for a boolean alone; and
the plain write is RUN: `writePlain` against fake steps — a refused tick saves, repaints and stops; a saved
tick and refused text go on to what follows; an invalidated key is cleared first (all red first against a
stub; red again with the early `return` planted out). Read from the host is only the wiring: the plain case
calls `writePlain`, and `save` returns false in its catch. The panel harness now reads a box's `checked` from the markup, as it
already read `disabled`: every box used to start unticked there, so no test could tell a page drawn from the stored value
from one that was not; *the panel draws the box from what is STORED* holds that (red with `checked` planted out of the box).
Then the dropdowns (`refusedSelect.test.ts`): the page RUN says `control: 'select'` for a changed select and
nothing for a number or a box; `settingMessageFrom` believes only the literal, and `settingWrite` carries it
on all five kinds; `saveOrSnapBack` repaints a refused select and a refused box, never refused text, and
never a save that stood; a refused plain select stops in `writePlain`; and the harness now reads a select's
value from the option the page marked `selected`, so *the panel draws a dropdown on what is STORED* can see
it (all seven red against stubs; red again with the page's spread planted out, and with the rule ignoring the
control). Four existing page tests now expect `control: 'select'` in a select's write — the shape change.
`snapBackQueue.test.ts` runs the REAL `WriteQueue`: a refused dropdown's repaint comes after its write and
the next write still runs (red with a timeout, *a write is waiting on itself*, against a repaint awaited
inside the write — which is what 0.56.1 shipped; green once `afterTheWrite` starts it instead); a write
appended while the queue was being waited for is waited for too; a failed write does not stop the rest. The
last two replace regexes over the old in-class queue in `thePromptBoxRemembers.test.ts`, which now pins only
that `render` waits on `this.writes` and the host enqueues there. `refusedSelect.test.ts` pins each wiring
link whole: `saveWrite` passes `write.control` and a started repaint, `snapBack` clears the paint key, and
`choosePrompt` reports and snaps back. Code round two: a repaint that fails after its write is said, never
an unhandled rejection (red without the catch); and a prompt pick goes through the write queue (red while it
was `void this.choosePrompt(…)`).

**The Re-ask label follows the box (2026-09-26, `chatPage.test.ts`).** With a re-ask on offer, the
running page's button reads *Re-ask · model* over an empty box, *Send* once a question is typed, and
Re-ask again after ✕. A host push while the box holds text keeps it *Send*, and names the pushed model
once the box is emptied. Both cases were red first, the second with the real symptom (the push painted
Re-ask over a typed question), and red again with the call from `paintBackdrop()` planted out.

**Release anchors and the Markdown rule (2026-09-26, `research/PLAN_the_release_guards_contradict.md`).**
`changelogNamesTheRelease.test.ts` now passes a release whose version the baseline does not name YET, as
long as its note exists. That was red first, naming the missing row. It still refuses a release with
neither row nor note, and the ratchet and phantom cases are unchanged. `releaseAnchors.test.ts` runs
`release-anchors.mjs` on a throwaway git repository:
- a tag on the release commit (only the package's `version.txt`) passes;
- a tag moved onto a `.github`-only commit is refused, printing the release commit, "identical", and
  the `PATCH` of the ref;
- a tag on a rebase merge's trailing commit is refused;
- an uncut tag passes;
- an unreadable configuration is exit 2;
- a git that fails is exit 2, never "not cut yet" (code round; red while `tagCommit` swallowed the error);
- `release-please.yml` runs the check before the action, over a checkout with `fetch-depth: 0` and tags.

The check was red with `touches()` planted to always answer yes. Run against this repository before its
tags were refreshed, it named `mcp-v0.38.0` on `ad06b589` — the real 2026-09-26 break — and printed the
exact repair that had been applied by hand.

`docsOnlyTitle.test.ts` runs `docs-only-title.mjs` over facts files:
- every releasing type over `src_mcp/README.md` is refused, and `docs:` passes;
- code beside Markdown passes;
- Markdown in one package riding on code for another is refused;
- Markdown outside every package passes;
- a Markdown-only `fix:` commit under a `docs:` title is refused;
- malformed facts are exit 2;
- `pr-title.yml` runs it with the title passed as data.

In `--pr` mode it asks for files only for commits of a releasing type, not one request per commit.

Both scripts, and `branch-protection.mjs`, find `git` and `gh` through `.github/scripts/lib/resolved.mjs`,
which the resolver moved into on 2026-09-26 from `branch-protection.mjs` unchanged. Its edge cases are still
pinned by `branch-protection.mjs --selftest` (26/26), and both scripts' suites run through it.

Since the operator's word the same day, pictures (`.png`, `.jpg`, `.jpeg`, `.gif`, `.svg`, `.webp`) count as
documentation too: Markdown and a screenshot alone are refused, a picture alone is refused, a picture beside
code passes (red first on `assets/shot.png`). It was red with `releases()` planted to always answer no. Run live with `--pr` against #568 (code)
and #571 (`docs:` over CHANGELOG), both passed.

The release order is now:
1. release-please opens the release pull request;
2. the CHANGELOG section goes onto it;
3. SQUASH merge, then dispatch;
4. the tag lands on the release commit and `mcp-draft` passes;
5. the baseline row follows in its own pull request.

**`ADeniedCommandIsAskedAgainTests` (issue #504)** — the follow-up of an auto-denied agy reviewer carries
`--conversation <id>`, keeps `--mode plan` and the schema, and says commands are unavailable; no follow-up
without a denial or without a conversation id; the executor runs it INSTEAD of the fresh repair (red first:
Unparseable, the #504 symptom; red again with the follow-up ignored) and a follow-up that is denied again
is still Unparseable with its reason. The real CLI was measured by hand (agy 1.2.10) — CI has no agy login.

**Issue #462 took it up (2026-09-24), and it has two readings, of which one is now fixed.** Six whole-suite
runs on this machine did not reproduce it, so the work is by elimination and says so:

- *The stub dies when a killed client abandons an answer* — **refuted**: a test holding the first answer,
  cancelling its client and asking again was green against the unchanged stub, and was not kept (a test
  that never went red is decoration).
- *The assertion reads a claim that names nothing* — **a real defect, fixed**: `ReadClaim` answered a
  transient refusal to open the file (a scanner or indexer, the same instant `Replace` retries on the
  write side) with `None`. `ClaimIsWrittenAtomicallyTests.AClaimHeldForAnInstant_IsStillRead` holds the
  claim with `FileShare.None` for 50 ms, released from a dedicated thread so a starved pool cannot outlast
  the reader's budget, and went red first (in its first form, a 150 ms hold released from the pool) —
  *Expected claim.JobId to be "job-77" … but "" has a length of 0* — the killed-shim test's failure
  exactly; red again with the retry disabled. `AClaimHeldForGood_IsGivenUpOnWithinASecondOrTwo` pins the
  bound.
- *The child times out reaching the stub* (the 40-second reading above) — **not explained**. It was
  already diagnosable: a prerequisite wait that runs out names what the child said on stderr.
  The ASSERTION was not, so it now carries the attempt number and what the file held, or why it could
  not be read.

## The review tree's decisions are tested as VALUES (2026-09-21)

`reviewTree.test.ts` — twenty tests, and the reason it exists is worth more than the tests are.

Story 3.2a shipped its seams well covered and its DECISIONS not covered at all: the wiring tests read
source, the page test runs the page, the live contract test runs the binary — and between them
**85 of `reviewTreeRead.ts`'s 142 new lines were never executed by anything**. Nothing said so. The
suite was green, every review round passed over it, and the only thing that noticed was SonarCloud's
coverage condition on the pull request: `new_coverage = 70.4` against a threshold of 80.

That is the useful half: a story can be reviewed twice by twelve reviewers, ship with 4 006 green
tests, and still have its central decision function unexecuted. Story 3.1 did not have this gap
because `openAtRevision.ts` was written as decisions and tested as values from the start; 3.2a put
its decisions in a reader and tested everything AROUND it.

What is covered now, all without spawning anything (the runner is a parameter):

| | |
|---|---|
| what an answer must MATCH before it is believed | another row, another commit, another repository — the last one is the finding a reviewer raised, and the mutation that removes it goes red with *another repository must not be believed* |
| what is believed although it echoes nothing | a reason-only answer has no coordinates, and refusing it would turn a true answer into silence |
| the path guard | relative, empty and `./` refused; POSIX, drive-with-slash and drive-with-backslash accepted |
| the exit codes | 64 and only 64 is "too old"; 65 and 74 carry the server's own sentence |
| output that is not an answer | empty, not-JSON, `[]`, `null`, a bare string |
| the tree list | shaped item by item, with a null and a number inside it dropped rather than trusted |
| what a person is TOLD | every reason word becomes a sentence, and none of them leaks an underscore; the cap refusal names the checkouts using it up, with their days and paths |
| the press | the working state said before the first await, the button gone while it runs and back when it ends, a refusal opening nothing, an editor's rejection caught, a second press starting nothing |

## Story 3.2a's counts, and an unresolved suite failure (2026-09-21)

Whole executable after the review tree and its code round: **2 420 tests, 0 failed**. Extension:
**4 006 tests, 0 failed, 1 skipped**; `tsc` exit 0 on a clean `out/`, `eslint src` exit 0.

**One whole-suite run reported a single failure and it is NOT explained.** It is recorded here
because the rule is explicit that "passes alone, fails in the suite" is a shared-state defect and
never a flake — and because the first account of it written here was wrong, which is the more useful
half of the record:

- the run printed `ConnectOtherAIs: a chat tab could not be saved for a reload Error: settings.json
  is not writable`, and that was taken for the failure. It is not. That sentence is
  **deliberate fixture output from a test that PASSES** — `a storage failure does not stop every
  write after it` injects exactly that error to prove the write queue survives one, and
  `ChatTabMemory.write` prints it through `console.error` on purpose. `settled()` returns an
  already-caught promise and cannot reject.
- **the failing test's name was never captured**, and a second whole-suite run was clean. So what is
  known is: one failure, identity unknown, not reproduced.

Nothing here is called a flake and nothing is called fixed. The next person to see a single failure
in this suite should capture the test NAME before anything else, and should not spend time on the
`settings.json` line — it is noise by design.

## The one append, and the test that had to be given teeth (2026-09-21, S8 story 1.4)

`AppendOnlyFileTests`, `JsonlLedgerTests`, `ServerNoticesAppendTests`, `ResolvedDataDirTests`,
`TheOneAppendTests` and `TheWordListIsLoadedBeforeTheTransportTests` — 31 cases for one three-line
writer, because everything they are about is a record that is silently not written or one that
silently destroys the next.

**The first two tests of `AppendOnlyFileTests` passed against the broken implementation**, and that
is worth writing down. They append sequentially and assert the file's length, and `FileMode.Append`
— which had just been measured losing 2488 of 8000 records to eight processes — passes both, because
the class opens and writes in one breath and a sequential test never opens the window the defect
lives in. A third test holds that window open through an `afterOpen` seam threaded into both platform
writers: a hundred bytes arrive between our open and our write, and the record must land *after*
them. Against the managed append it fails with `Expected bytes.Length to be 101 ... but found 100`,
which is the defect stated in one line. The seam is a PARAMETER and not a static hook, because xUnit
runs classes in parallel and a hook left set would fire inside somebody else's append.

**What no unit test here can do is fork a process.** `npm run measure:append --dotnet=N` does: it
runs `JsonlLedger.AppendLine` from `NoticeTool append` beside node's writer at one file, and it is
the only reason the defect was found at all. Its four legs — a single-writer control, the torn-tail
pair, and the mixed run — are described in the script's own header with every result and its
conditions.

**The census is an allowlist, not a name count** (`TheOneAppendTests`). The first proposal counted
`ServerNotices.Name` in one production file; a reviewer was right that a second caller could reach
`JsonlLedger.AppendLine` with a computed path and leave every test green. So every production call
site is enumerated and compared with a list of two, the file name is held to one spelling, and the
minting sites of `ResolvedDataDir` are asked of the ASSEMBLY as well as of the source — a
target-typed `new(` hides from a text scan and not from reflection. Each scan has a companion
asserting it still finds its known instances, so a reformat cannot turn a guard into a pass.

## The seams, and two gates that had stopped meaning anything (2026-09-21)

Five findings from story 3.3's code rounds were right about the code and wrong about the SCOPE — each
named a defect that lives in a sibling too. Rejected in the round with that reason, carried out as
questions, answered by the operator, and built here as one unit.

| Suite | What it covers |
|---|---|
| `bugzReviewPage.test.ts` (+4) | the live patch, DRIVEN through `Host.push`: a revision answer landing in its own container and no other; an identical revision patch skipped, as `showCalls` already was; and for BOTH channels, that the container the generator EMITS is the container the page script QUERIES, message type included |
| `suppressionsOnlyShrink.test.ts` (8) | the lint ratchet, by SPAWNING it: an unchanged or shrunken file passes; a new file/rule pair is refused; a grown count is refused; a second rule on an already-listed file is refused; **the case a total would pass** — one fixed, one added, total unchanged; an absent base is a pass rather than a crash; six shapes of valid JSON that are not suppressions files are refused rather than read as a verdict; and the repository's own file is valid and carries nothing from `src/test/` |
| `workflowGuards.test.ts` (4) | the submodule is in the ANALYSIS exclusions; the actionlint fetch has no flat `--retry-delay`, has `--retry 5` and `--retry-max-time 120`; those same arguments **run against a local server that answers 504 three times and then 200**; every job in every workflow declares `timeout-minutes` |

**Teeth, measured.** Seven plants, each restored from a saved copy:

| planted | red | what the failure said |
|---|---|---|
| `showRevisionActions` repaints identical markup | 1 | the same markup a second time must not touch the DOM |
| the page script hardcodes its own query again | 7 | every test that drives a revision patch, plus both channel tests |
| the generator hardcodes the container attribute | — | **the COMPILER refused it**: the import it replaces goes unused. A stronger outcome than a red test, and the reason this seam cannot quietly come apart |
| Sonar analyses the submodule again | 1 | the submodule must be in the ANALYSIS exclusions |
| the flat retry delay comes back | 1 | a fixed `--retry-delay` replaces the exponential backoff |
| one job loses its timeout | 1 | these jobs would run for GitHub's default 360 minutes if they hung |
| one file suppressed that was not before | 1 | run against the REAL file: the total was unchanged at 615, which a count check would pass, and the ratchet named `src/brandNew.ts` |

**Two tests found their own harness first, and both are recorded because the lesson belongs to the
test.** The retry scenario used `spawnSync`, which blocks the thread the local server runs on — so the
server could never answer, curl retried an unanswerable request for the full 120 s, and the test
measured its harness rather than the flags. It is `execFile` now. And the ratchet's own
`--base-absent` test failed on its first run against a script that compared an absent base to `{}`,
which would have declared everything new and refused the very commit that introduces the file.

**`ReviewPair` moved with no test of its own**, said here rather than skipped silently: it is a move
with no behaviour to assert. The check is the compiler, and it is a real one only because the
declaration was DELETED from `bugzReviewPage.ts` rather than re-exported — it named all ten importers
by file and line on the first build.

**The code round took five more and rejected twenty-eight, and two of the five were about these
tests themselves.** The channel test had asserted the generator and the page script agree by
READING the generated text; two reviewers cited the operator’s 2026-09-14 ruling against it and
were right — a page is tested by running it, and the executed test beside it already proved the
same thing. It is gone. And the timeout scan could have passed by matching nothing, which is the
house rule about structural tests: it counts what it found and refuses fewer than twenty jobs.

**Twenty-eight rejections, and ten of them were one claim repeated**: that jobs lack
`timeout-minutes`, citing ci.yml lines 471, 870, 914 and 953 — in a file of 420 lines. Measured
over all eight workflows, no job lacks one. Also rejected on measurement: that ESLint’s
suppressions can be fabricated (a suppression with no matching violation exits **2** with
“there are suppressions left that do not occur anymore”), that `ReviewPair` is still declared in
the renderer (one declaration, in `reviewPair.ts`), that the retry test still uses `spawnSync`
(it uses `execFile`; `spawnSync` survives only in the comment saying why), and that the page
script interpolates `items[i].html` at build time (it is plain text inside the script — a
property access the browser evaluates on a message the host posted).

**And SonarCloud found one the gate did not**, on the pull request: the ratchet checked its
arguments with `currentPath === undefined`, which Sonar reads as always-false because a
destructured short array is not `undefined` in its type view. It was wrong about the runtime and
right about the expression, and looking at why found the real hole behind it — given THREE paths
the script took the first two and compared a pair nobody asked about. Counting the list says what
is meant in every reading. The red test hands it three files that all EXIST, so the refusal cannot
come from a missing one; a first draft passed for exactly that wrong reason.

## The startup notes reach the page, each under a subject (2026-09-22, S8 story 2.3.3)

`TheStartupNotesAreWrittenDownTests` — twenty cases, and most of them are about the SUBJECT, because
that is where this story could have been useless while looking finished: two malformed settings are
two subjects; two refused rows of one key share one (the accepted cost, asserted); two loose
databases under different roots are two subjects; one path spelled two ways is one.

**The taxonomy is asserted, not intended** (codex): a new directory emitted as `stand-down`, or a
loose database as `outcome`, would pass every other case here while the extension shows the wrong
severity.

**The code round added eight, and four of those are a REAL host.** The settings reload is the case
a unit test cannot reach: `Hosting()` builds a genuine `PanelServiceHost` over a temp directory,
writes the settings file the way the panel does, and reads `Current`. That covers the mismatch
reaching the page without a restart, the same mismatch reaching the LOG, the host's first build
staying silent (so a start does not report everything twice), and `Current` read three times over an
unchanged file writing nothing more — the last one is what keeps the ledger's volume bounded by
settings EDITS rather than by tool calls.

**Teeth, seven times.** Making the storage subject the kind alone turns the two-databases case red;
making every storage note an `outcome` turns the taxonomy case red; emptying the unrecognised
subject turns the two-settings case red. From the code round: dropping a kind from
`StartupNotices.ClassByKind` turns the census red naming the kind (*"{new-directory} do(es) not
match"*); restoring the `? :` default turns the unknown-kind case red (*"Expected _written to be
empty ... but found at least one item"*); rewording one sentence's variable turns the key-drift case
red; and starting the host with `_startupBuild = false` turns the double-report assertion red.

**One of those plants proved the test, not the code.** The reload case first wrote its bad value
AFTER constructing the host, so its "the first build says nothing" assertion passed over an empty
settings file whatever the code did — the plant that should have reddened it stayed green. The
value is written BEFORE the host exists now, which is the only ordering in which that assertion
means anything.

**The log half stopped being structural.** It began as a scan of `Program.cs` for the log call and
the notice call side by side — which proves the two paths exist and nothing about whether a given
diagnostic travels both. One call writes both now, so the guard is behavioural: every title written
must appear in some log line, and deleting the `log.Warning` from the loop reddens it. The census of
CALLERS stays, because "two and no more" is still a fact worth pinning.

**A second plant proved a test worthless rather than the code wrong.** The shared-cut assertion was
first hung off `AMegabyteOfStderr`, where the stderr tail goes through `BoundedScheduler.Quote` and
comes back already ellipsised at `ReasonLength` — so it passed on Quote's mark and would have passed
with the shared cut deleted. `AReasonNoQuoteBounds_IsCutAndSaysSo` drives `not started: {Reason}`
instead, which no Quote bounds, and the plain-cut plant reddens it by name.

## Every reviewer failure is written down, once (2026-09-22, S8 story 2.3.2)

`TheReviewerFailuresAreWrittenDownTests` — sixteen cases. A theory over all five endings (the right
code, the `provider/role` subject, the title equal to `Describe`); `Ok` and a progress line with no
outcome write nothing; the same reviewer reported twice is ONE line; two reviewers are two subjects;
a secret in a stderr tail does not reach the line; a megabyte of stderr does not sit in the queue.

**Three guards, each proved by breaking it.** Removing `_noticed` turns the twice-reported case red;
deleting one entry from `ReviewerNotices.ByType` turns both the reflection census AND that ending's
theory row red; and dropping `_noticing` from `PanelServiceHost.Build()` turns the rebuild case red —
which is gemini's finding, and the one that would otherwise have made this story write nowhere in the
case people actually hit.

**The wiring is asserted on IDENTITY, not on behaviour.** A first version drove a real host through a
real round with a vendor that could not start; it went red for a reason that had nothing to do with
the wiring (`no session for this repo+branch`), and chasing that further would have been testing the
round rather than the thread. What the test asserts instead is that the `Noticing` the host was GIVEN
is the one its service holds — by reflection, because the question is an identity and no public
surface should have to answer it — and that it is still the same one after the settings file moves
and `Build()` runs again. The live leg over stdio remains story 2.4's.

**And one defect the tests found in the boundary itself:** `Noticing.Offered` logged TWICE when the
offer threw — the exception, and then "not accepted" — which reads in a log as two different losses.
One sentence per lost notice.

**The theory is DERIVED from the production map** (codex, on the code round): listing the five
endings again beside `ReviewerNotices.ByType` is a list that gives a sixth no row, so the cases come
from `ByType.Keys` and a factory supplies the constructor arguments reflection cannot invent. An
ending that is mapped but which the factory cannot build fails loudly there — the same demand in a
different place.

## A notice offered on the way out still lands (2026-09-22, S8 story 2.3.1)

`TheWriterDrainsBeforeTheProcessLeavesTests` — seven cases for one method, because what `Drain` has
to be right about is all edges: everything queued is on disk afterwards; a writer that HANGS does not
hold the process (measured against a generous ceiling rather than against the bound, since a test
that asserts "returned within 200 ms" of a 200 ms bound fails on a loaded machine and proves nothing
on a fast one); an empty queue pays nothing; a late offer is refused with the CLOSING sentence rather
than the full-queue one; an offer racing the drain is either written or refused but never accepted
and dropped; and a burst past the depth refuses with a sentence naming the depth.

**Teeth.** Deleting the `finally` from `ServeAsync` turns the structural case red; putting the
full-queue sentence back on the closing road turns the message case red. Both restored from saved
copies.

**And one thing these tests CANNOT prove, said rather than implied.** Swapping `Drain`'s wait from
the writer task back to the in-flight count leaves all seven GREEN — measured, by doing it. With
today's `Draining` loop the count reaching zero and the task completing are the same moment, and
`Wrote` catches everything so the task cannot fault. The task wait is kept because it is strictly
stronger and stays correct when somebody adds work after the loop; the docstring says so instead of
the suite pretending otherwise.

**What is structural and why.** `TheHostDrainsOnEveryRoadOut` asserts the `finally` by scanning
`Program.cs` — a `finally` attached to the wrong `try`, or missing from one of the two `return 0`
roads, would pass every behavioural test here while a last-second refusal is lost from a real
session (codex, on the plan round). It is TWO tests, as the convention asks: the known instance and,
separately, `NoSecondProductionCallerDrainsTheWriter`, because a scan that finds nothing passes a
prohibition and proves nothing. The live half — a real refusal over stdio against the published
binary — is story 2.4, which the parent plan owes by name.

**And Sonar found the rest of the same lesson.** New-code coverage came back at 50 %, and every
uncovered line was a helper no test could reach: the `catch` inside `Drain`'s wait, and the two
`Program` helpers that only `ServeAsync`'s `finally` runs. The answer was not to accept them — it was
to make them reachable. `Waited(Task, TimeSpan)` is a static function of its arguments now, so a test
hands it `Task.FromException` and proves the "never throws" contract against a faulted writer the
running code cannot produce; `Draining` and `Unresolved` are `internal`, so the sentence the host
prints — and the rule that it prints nothing when there is nothing outstanding — are asserted rather
than read. A helper a test cannot call is a guarantee nobody has checked, which is the same finding
story 2.2 took from Sonar about an unreachable `catch`.

**Two of these tests could have passed on a fixture the code REJECTS, and the code round caught
both.** The racing test would have gone green on zero accepted and zero written — and measured, the
drain DOES win every race, because the offering task has not been scheduled when the main thread
reaches `Drain` — so one offer is accepted before the race and the race then only has to show that
what it accepts is drained and what it refuses is refused, with one sentence per refusal. The
full-queue test would have gone green against an implementation that refused EVERY offer, so it
asserts that offers were accepted before the overflow began.

## A refusal that cannot be written must not cost the refusal (2026-09-21, S8 story 2.2)

`TheRefusalsAreWrittenDownTests` — thirteen cases, and the interesting half is not that a line lands
but that FOUR of them break the write on purpose: a resolver that throws, a writer that throws, a
writer that answers `false`, and a writer that never returns. The plan round put five findings on one
sentence of the plan, from all three providers, and every one was right about the same gap — the
pseudocode resolved the data directory outside the failure boundary, so a misconfigured
`COAI_DATA_DIR` would have thrown past the return and the calling AI would have received nothing.

**Teeth, measured.** Narrowing the boundary's `catch` from `Exception` to `DivideByZeroException`
turned two of them red with the exception escaping as an `AggregateException`; removing the time budget
made the fourth **hang** — `0 completed, 0 failed | active: AWriterThatNeverReturns…` — which is the
symptom codex described, not a proxy for it. One plant did NOT work and is worth recording: an
`if (true) … else if (false)` version failed to COMPILE, the build stopped, and the suite then ran the
previous executable and reported thirteen green. A plant that does not build is a plant that proves
nothing, and the green run looks identical to a real one.

**And the code round replaced what those tests were testing.** Twelve findings said the 2 s budget was
the wrong shape — a wait only stops waiting, the pool worker stays blocked — so the timeout test became
`AWriterThatNeverReturns_DoesNotDelayTheRefusalAtAll`: fifty refusals against a writer that never
returns, asserted under two seconds. Two more arrived with the queue: one that a full queue answers
`false` rather than blocking (the first channel used `DropWrite`, which discards the notice and answers
TRUE — the silent loss the whole design is against, and the test caught it: *"Expected offered to be
less than 512 … but found 512"*), and one that a file at its ceiling whose archive cannot be written
REFUSES to grow. That last one uses a DIRECTORY in the archive's place rather than a locked file,
because only Windows refuses to rename over an open handle and the suite runs on ubuntu.

**The seams are parameters, not hooks** — the directory and the writer are arguments, for the reason
story 1.4 recorded: xUnit runs test classes in parallel and a static hook fires inside somebody
else's call. `COAI_DATA_DIR` is process-global, so a test that set it would be that hook wearing a
different hat — which is also why the `[CallerMemberName]` forwarding is asserted on the SIGNATURES
of every hop (`Error`, `Refused`, `RunStageAsync`, `Answer`) rather than by driving the service: the
overload that fills the subject resolves its directory from that variable. Removing the attribute
from `Refused` turns that theory red naming it. The live leg is story 2.4.

**And the census caught the new road on its first run.** `TheOneAppendTests` asks the ASSEMBLY which
members answer `ResolvedDataDir`; `Refusal.Where` made three, and the test went red naming it. It is
a pass-through to the one resolver, like `SettingsFile.DataDirFrom` beside it — what that census
exists to catch is a second IMPLEMENTATION — so the list grew by one with the reason written down,
which is the difference between a decision and a drift.

## One scanner, two censuses (2026-09-21, S8 story 2.1)

`ProductionSources` is the source scanner both censuses share — `TheOneAppendTests` (one append) and
`TheRefusalRoadsAreCountedTests` (one refusal boundary). It was built for the first and extracted
when the second wanted it, because every rule in it was earned by a bypass somebody found and a
second copy would have been the version that still has them. The roots are DISCOVERED (`src_*`)
rather than listed, so a project added tomorrow is scanned; the file is read once per run and both
views cached; and `RepositoryRoot()` lives on the scanner rather than on whichever test class needed
it first, because a shared utility that cannot find its own root without a particular test class
existing has an owner its name does not admit to.

**Comments and string contents are removed by a LEXER, not by a line rule.** The first version
skipped a line whose trimmed text began with `//`, which let a `/* … */` block holding a
commented-out call count as a call, a trailing `// new ErrorAnswer(` count as one, and a sentence
inside a string literal count as one. A census that counts prose is the defect it exists to end, in a
new place. One pass handles line and block comments, ordinary strings with their escapes, verbatim
strings with their doubled quotes, raw strings and character literals.

**An interpolation hole is CODE and stays** — the second code round's finding, and the last way past
every census here. A string's contents are prose; `$"{new ErrorAnswer(why)}"` is a construction that
runs, and the first lexer deleted it with the text around it, so the boundary rule story 2.2 rests on
would have gone green over it. The holes are walked as code and spliced back between the fences,
`{{` is a literal brace and not a hole, and a hole may itself hold a literal — which is also how
`$"{map["k"]}"` stopped ending at the quote before `k`. Written RED first: the failure was
`Expected "var m = "";" to contain "new ErrorAnswer("`, the whole hole gone. Then proved on a real
file: a `Hidden(string why) => $"{new ErrorAnswer(why)}"` planted in `Escalations.cs` fails the
boundary census naming that file, and the plant compiles.

**And CodeRabbit found the same hole one syntax along**, on the pull request: a RAW literal was
skipped whole, so `$$"""{{new ErrorAnswer(why)}}"""` named the type nowhere either. The number of `$`
prefixes is the brace count now — one `$` opens a hole with `{`, two with `{{`, and a LITERAL brace is
written twice that many — and `\` is an ordinary character in a raw string, so the escape rule is off
there. That rewrite introduced a defect of its own and **its own companion assertion caught it**: `""`
is the empty string, not a fence of two, and reading it as a fence sent the lexer hunting for the next
two quotes in a row. The append census lost `UsageLedger.cs` — *"Expected … 2 item(s), but found 1"* —
which is exactly what "every census has a companion" is for. It is a test of its own now.

**The lexer's methods are small because the rule says four.** `.coderabbit.yaml` sets a cyclomatic
ceiling of 4 for `src_mcp/**/*.cs`, and the one-pass walk had grown past it. It is a `Fence` record
(delimiter, run, verbatim, braces) and one small method per decision — `Closes`, `Advance`, `Brace`,
`Hole`, `Take` — which is also what made the raw-interpolation case a few lines rather than a rewrite.

**There are two views, and the difference is the whole contract.** `CodeOf` drops what is inside a
literal — that is what stops a census of CALLS counting a sentence that mentions one. `SpellingOf`
keeps literals and drops only comments, because a census of a NAME is a census of a literal:
`"server-notices.jsonl"` is a string, not an identifier. Turning the lexer on took that name away
from story 1.4's two guards and they went RED — which is exactly what their companion "the scan still
finds its known instances" assertions are for, and the reason every census here has one.

**Lines are joined so that a wrapped MEMBER glues and a wrapped WORD does not.** The first version
joined with nothing at all: `Type⏎.Method(` became `Type.Method(`, which is what it was for, and
`using⏎static` became `usingstatic`, which is what story 2.1's import guard was then written to search
for. A line beginning with `.` glues to the one before it; everything else is separated by one space.

**`UnqualifiedCalls` is the precision the plan round asked for.** A whole-file search for `Error(`
counts every `_log.Error(` in the file, and this codebase logs constantly. The rule is an occurrence
whose preceding character is neither a dot nor part of an identifier — and the declaration is
SUBTRACTED as `Error\(\s*string\b`, not matched on its return type: the first version looked for
`string ` *before* the name, so changing the helper to return `Task<string>` would have turned its
declaration into a call, and the second version matched the exact characters `(string `, so
`Error(  string sentence)` would have done the same. Both move a number the documents quote.

**The scanner has its own tests** (`ProductionSourcesTests`), because every guarantee either census
makes is really a claim about that one file — and its rules were being asserted inside whichever
census happened to earn them.

**The inventory writes only when asked, and that run FAILS.** `COAI_RECORD_REFUSAL_SITES=1`
regenerates `shared/refusal-sites.json` and then fails on purpose, naming the file and saying to
re-run without the variable; without it the suite compares read-only. A test that silently rewrites
its own expectation cannot fail, one that writes into a read-only CI checkout cannot run, and one
that goes green after rewriting its own source of truth is worse than both — the first two were named
on the plan round, the third on the code round.

## A poll waits out a busy file (2026-09-24, issue #512)

`ARunThatDiesIsRecorded` failed the mcp-v0.33.0 release on win-x64, and once locally under the full suite:
its poll read `server-notices.jsonl` with `File.ReadAllLines` while the child server held it open to append.
`ReadAllLines` opens with `FileShare.Read`, a reader that forbids writing, so Windows refused it, and the poll
let the `IOException` escape. One unlucky poll failed the whole test.

- **`Polls.Until(condition, patience)`** (tests) is the poll, replacing that class's private `Eventually`. An
  `IOException` or `UnauthorizedAccessException` from the condition is "not yet" — `SharedRead`'s own contract
  for every product reader of the data directory — and at the deadline it is a plain `false`, so the caller's
  assertion fails with its own sentence (and a poll still refused at its deadline writes that exception to the
  test output, so "never came true" and "was never readable" can be told apart).
- **`NoticeLines.Of(file, code)`** (tests) reads through the product's `SharedRead.Text`
  (`FileShare.ReadWrite | Delete`, the share the server's `AppendOnlyFile` writer grants), and counts only
  TERMINATED lines: an unterminated tail is an append in flight, "not yet"; a terminated line that is not JSON —
  malformed or empty — still throws, because hiding it would hide a writer defect.
- **`APollWaitsOutABusyFileTests`** pins each half without a server: a file held with `FileShare.None` and
  released after 300 ms is found by a later poll; `UnauthorizedAccessException` is "not yet" too; a condition
  that never holds still answers `false`; a half-written tail is not a notice; a malformed or empty complete line
  still throws; and a notices file a writer holds open to append is read without a wait (red before the fix with the
  exact #512 message). Five were red against the old poll and reader; the malformed-line guard was proved by a
  plant that skipped unparsable lines.

The sweep for the same shape (a poll over a file a live process writes, letting a busy read escape) found no
other site: `RemoteShimScenarioTests.RawText` already catches both exceptions, and the remaining reads are
one-shot reads after the server has answered.

## CoAI: choose on a bug (2026-09-24, issue #487)

- `reviewChoose.test.ts` — the seven labelled parts in the issue's order with their values; a draft beats
  the saved comment (and another row's draft does not); a skeleton holding ```` ``` ```` stays inside one
  fence, and its language cannot end it; nothing recorded is said as such, never a blank or a zero; one bug
  is one key, the same finding in another checkout another; with a REAL `ChatPanels`, a bug's conversation is
  found again after `rekey` moved it onto a file tab (red against the key lookup), a closed one is gone, and a
  refused press remembers nothing. Two compiling plants (drafts ignored; a fixed three-backtick fence) each turned
  their test red.
- `bugzReviewMessages.test.ts` — the moved guard, RUN: `choose` needs a whole non-negative id, and EVERY kind
  the page sends parses to its shape with its junk dropped (`constructor`/`toString`/`__proto__` answer nothing) —
  written green against the old switch before it was rewritten as a `Map`, so the rewrite is proved
  behaviour-preserving.
- `bugzReviewPage.test.ts` — a `Chooser` element built from the rendered markup (its `closest` finds the ROW,
  as a browser would): one button per row, on the summary line, outside the disclosure button, with a title;
  a press posts `{type:'choose', id}` once and neither opens nor ticks the row.
- `bugzReviewWiring.test.ts` (read, not run — every link imports `vscode`) — `received()` hands the hook the
  pair AND `this.drafts`; the provider's hook reaches the extension's callback; `ChatPanels` exists before the
  sidebar; `chooseFromBug` reveals an existing conversation before anything else and delivers a first one
  with `send = false`.
- `notificationSites.test.mjs` — `PLACES_THIS_SPEAKS` 141 → 141: the three identical "not ready" refusals
  became one helper, and the caught failure of a press is a new place (pinned, with its console line, by
  `bugzReviewWiring.test.ts`, red before the catch existed).

## The diffs are numbered, and have no gaps (2026-09-24, issue #488)

- `codeHighlight.test.ts` — no rule makes a `.line` a block, and the newline between lines stays (both
  paths); every line carries `data-ln` from `firstLine` (1 by default, 41 when asked), the code's readable
  text is untouched, the number leads the spoken mark, `--coai-ln-digits` is on the block; the first line is
  part of the cache key. Red first.
- `bugzReviewPage.test.ts` — a skeleton is numbered from 1, every line of both sides; the real method is
  numbered from each side's own `startLine` (red first: it counted from 1).
- `bundledPage.test.ts` — through the bundle the webview loads, an open row's lines are numbered and the
  shipped stylesheet draws them from the attribute.
- **Not asserted:** the layout itself (the absence of a blank row, the gutter's alignment, where the choose
  button sits) needs a browser; it was checked in headless Chrome over the page's real markup and CSS.

## A counted retry counts only what it was told to lose (2026-09-24, issue #520)

`AStubSurvivesALostPortTests.EveryCodeTheStubClaims_CostsTheCandidateAndNotTheRun` pinned exactly two binds
(the injected loss, then the real one), which is true only while nothing else binds a port between
`LoopbackStub.FreePorts()` listing a candidate and the stub binding it — and the suite runs in parallel: a CI leg
found 3. The product kept its promise (one retry per LOSS); the test assumed a still machine. `CountedBind`
injects the loss on the first call, binds for real after that and writes down every real attempt that lost its
port to somebody else; the count stays EXACT — `2 + lost for real`, the losses named in the message — and a real
failure that is not a taken port still escapes. `ACandidateAnotherProcessTakes_IsCountedAsALossOfItsOwn` makes
the race deterministic with a held socket as the second candidate: red against the old `== 2` with #520's own
message ("found 3"), green with the count.

## The chat state carries spend and reask (2026-09-24, issue #492)

`aQuestionCrossesTheSeam.test.ts` drives the REAL send (`sendChatState` with a recording tab) and pushes what
it posted into the running chat page: a new cost moves `#spend`; a re-ask target renames Send to
*Re-ask · Opus* and an empty-box Send re-asks, and a later empty target puts Send back and re-asks nothing; an
unchanged state is not sent twice while a changed spend is. All three were red before the fields were added —
the third because, without `spend` in the message, a new cost serialised identically and the de-duplication
swallowed it.

## A signal ends the server; reviewers start no MCP servers (2026-09-25, issue #514)

- `ARunThatDiesIsRecordedTests.AServerStoppedByASignal_ClearsItsMarker_AndNoLaterStartCallsItADeath` — a real
  serving child, `kill -TERM/-INT/-HUP`, then no marker, "stopped by a signal" on its stderr, and no unclean
  exit from the next start. POSIX only (skipped on Windows); run on Linux through WSL Ubuntu (.NET 10). Red
  first with "Expected File.Exists(marker) to be False … but found True" for all three signals; then red AGAIN
  once the stderr check was added (our own reviewer: the deadline also clears the marker) — the child had left
  by the 3 s deadline, "the shutdown was still running", because a blocked stdin read ignores the token; green
  after the stop also closes the transport.
- `ASignalEndsTheServerCleanlyTests` — the decision as values: the first signal cancels the runtime's exit and
  stops serving without touching the marker; a second clears it and lets the default exit through; a shutdown
  still running at its deadline is cut with the marker cleared; one that finishes is not.
- `AReviewerStartsNoMcpServersTests` — Claude reviewer and consultant carry `--strict-mcp-config` and no
  `--mcp-config`; Codex reviewer and consultant (first turn and resume) carry one `enabled=false` per server,
  each a valid TOML key (red first for all three argv tests); the `config.toml` reader's forms, and what it must
  NOT report (a plain value under `[mcp_servers]`, a key after `[[…]]`, a dotted key outside the root — red
  first); a name with `&`/`|`/`%` is not passed (red first); the config is read through the launch's `HOME`
  (red first); the gemini-family JSON reader.
- `src_server` `ReviewLauncherTests.ACodexJob_SwitchesOffTheSlotsOwnMcpServers` — a Team-server codex job reads
  the SLOT's `config.toml` (red first: the server's own was read).
- `TheWriterDrainsBeforeTheProcessLeavesTests.TheHostDrainsOnEveryRoadOut` now pins the whole `finally`
  (`EndedAsync`, then `stop.Ended();`).

**Issue #515 — a Team-server agy reviewer whose command was auto-denied is asked again (2026-09-25).**
`src_server` `ReviewLauncherTests`, against the REAL `ReviewLauncher`, `ReviewerExecutor`, `AntigravityRuntime`
and `Confined`, with a scripted process (`Scripted`) that answers each launch in order and keeps every request:
- `ADeniedCommand_IsAskedAgainInTheSameConversation_AndItsAnswerIsTheJobs` — two launches, the second with
  `--conversation conv-7`, `--mode plan` and the "not available" stdin; the answer is the job's, both launches
  billed (red first: one launch).
- `TheFollowUp_RunsAsTheSameAccountInTheSameConfinement` — same executable, working directory, environment and
  `InheritsEnvironment = false`; the work directory and the schema file exist at BOTH launches (red first).
- `AFollowUpThatSaysNothing_IsStillUnparseable_SaysSo_AndBothLaunchesAreBilled`, `AFollowUpThatFailed_IsThatFailure`,
  `AFollowUpCancelled_Propagates_AndTheDirectoryIsStillDeleted` (red first: one launch, and no cancellation
  reached the launcher). That the directory survives BETWEEN the launches is the confinement test's
  `WorkDirectoryExistedAtEveryLaunch`, not this one's.
- `AFollowUp_GetsOnlyWhatIsLeftOfTheJobsBudget` — the second launch's timeout is at most the budget less the
  fake's KNOWN first-launch delay (a second stopwatch in the fake made the margin microseconds; our own code
  reviewer) — red with the timeout left whole; `ADeniedCommandWithTooLittleBudgetLeft_IsNotAskedAgain` — one
  launch, and the `Unparseable` says "not asked again" (red with the `LeastFollowUp` floor removed, and red on
  the reason before the code round's fix); `AnEmptyAnswerThatWasNotADenial_IsStillOneLaunch` (a guard: agy with no
  denial, and claude, stay one launch).
- Each guard was planted out with COMPILING code (the transcript handed to `FollowUp` emptied; the floor
  removed; the timeout left whole) and the named tests went red. **NOT covered:** the real agy on the box —
  the conversation resolving under the confined slot environment was measured locally on #504 (agy 1.2.10)
  and is observable on the Team server only after a deploy.

## The shim tests' stub survives a bad request and says what it saw (2026-09-25, issue #462)

`RemoteShimScenarioTests`' stub served one request at a time and caught nothing inside its loop: an answer that
threw, or a response it could not write because the child had been killed mid-answer — which
`AShimKilledMidClaim` does six times a run — escaped the loop, faulted the unobserved `Task.Run`, and left every
later request to the child's 40 s `HttpClient.Timeout`: the words both Windows legs of mcp-v0.34.0 recorded.
Reproduced DETERMINISTICALLY by two tests, red before the change with exactly that timeout:
`AnAnswerThatThrows_DoesNotSilenceTheStub` and `AnAnswerWhoseClientWentAwayMidWrite_DoesNotSilenceTheStub` — a
raw request, a 16 MB answer the client never reads, the socket reset under the write (a genuinely failed write;
our own reviewer caught that the first version aborted the response inside the answer, which failed at the first
property instead). A plant that lets a failed write end the loop again turns it red with the same timeout. Load itself was ruled out the same day: two full suites at once (0
failures) and a thread-pool starvation experiment (384 pool threads blocked; an outside `curl` still answered in
0.5 s).

Now each request is `AnsweredAsync`: a failure is written down and the response aborted, and the loop goes on;
any failed accept is written down and ends the loop only when the listener has stopped (with a 50 ms pause, never a hot loop). The stub keeps a JOURNAL (time since start, method
and path, status or exception, the loop's end) under its own lock; a prerequisite wait's timeout message
carries it (`AFailedWait_CarriesWhatTheStubSaw`), and every test in the class writes it to its output on
disposal, so any failure there shows whether the stub ever saw the request. #462 stays open until the release
legs have been quiet long enough to call it.

## The feature stage, before its tool (2026-09-26, S2.1 of the feature-review plan)

Four classes in `src_mcp/tests`, one in `src_server/tests`, and the extension's own — every one
written RED first, and the observation recorded because "tests pass" is not evidence.

| Suite | What it holds | Seen red as |
|---|---|---|
| `AFeatureSessionIsKeptApartTests` | Every existing session key pinned by LITERAL; the feature key `repo#:feature#feature:<plan>` collides with no branch or document key; `BeginFeatureRound`'s four refusals are four different sentences; `again` reopens a finished review with a fresh budget and is simply begun on an open one; a round of another kind on a feature session is refused by name; `FeatureBases` — the same base, no recorded base and `again` pass, a different base is refused naming both SHAs; the store, the claim and the orphan sweep keep a feature session under its own file | `"/repo#:feature" has a length of 14` where the plan segment was expected; `Moved` where `Refused` was; `Stage.Done` where `FeatureReview` was; the claim file equal to the branch's |
| `TheFeatureStageIsServedTests` | `Serves(FeatureReview)` — ticked yes, absent no, a Team server never, disabled never; the tick parsed from `COAI_VENDORS` absent-is-false; the stage's own budget keys, the code keys not reaching it, the legacy pair still filling in; the role's switch IS the gate; `COAI_FEATURE_MIN_EPICS` | `ArgumentOutOfRangeException` from the exhaustive switch; `MaxRounds … to be 3, but found 1`; `FeatureMinEpics to be 5, but found 3` |
| `AFeatureRoundIsSkippedNotRefusedTests` | The §4.4 truth table through `RoundEngine.RunStageAsync` with a hand-shaped `StageRun` — the role off, no vendor ticked, a ticked vendor that cannot run, each `skipped` with its reason; the skip recorded with the head and the session state untouched; ten identical skips one row `×10`; a changed reason a new number; **all failed → a person, and un-ticking everyone is refused with the human-gate sentence, not skipped**; two concurrent first calls create one session; `MustExist` still refuses a missing session; `Refuse` is still a refusal | `no session for this repo+branch — call open first` where a skip was expected (the session was not created under the claim); then, with the feature begin's gate arm deleted, a `skipped` verdict where the human-gate error was expected |
| `ASkippedRoundIsWrittenDownTests` + `LogCliScenarioTests.ASkippedRoundsReason_IsOnTheList` | Step 16 is the note; a skipped round's reason round-trips through `RecordRound` and `RoundsQuery.Read` and through the real `--log` process; a coalesced skip carries `×N`; a round that ran has an empty note; a database from before step 16 lists its rounds with an empty note; a step-15 file migrates forward | `Expected adds to be greater than 0 … but found -1` (no step added the column); with the reader's ladder pointed at a column that does not exist, the note read `""` where the reason was expected |
| `AcceptedRolesTests` (Team server) | The built-ins seeded by STAGE; the feature role not known, not advertised, refused naming its stage; `AllowAny = true` still refuses it; naming it in `Coai:ExtraRoles` stops the server | `Default.Knows(FeatureReview) to be False, but found True`; `Names` one item too many |
| `StagesTests`, `BuiltinRoleCatalogTests`, `RoleCompositionTests` | The fourth row, pinned by literal; eight roles and thirty prompts; `RoleStages.All` read by both the seed loader and composition, the refusal naming all three; the `Defaults` dictionary handing the feature role `FeatureDefault` by reference | eleven `StagesTests` red on the enum member alone; the catalog `contains 1 item(s) too many`; with composition's pair spelled by hand again, a feature row `Dropped` |
| extension: `roles.test.ts`, `settingsShape.test.ts`, `builtinRoleCatalog.test.ts`, `panelServerDefaultsAgreement.test.ts`, `helpPrompts.test.ts` | `FEATURE_CODE`/`FEATURE_DOCUMENT` as buckets, `bucketOf` by name with an unknown stage landing in the result bucket rather than a bucket nothing draws; `enabledCodeRoles` as a bucket filter; the panel's word `feature`; the defaults read from `FeatureDefault` | `the shipped feature role reads an outline in its own round` (it was counted as a code role); `FeatureReview: the panel shows undefined rounds`; `the roles page drew no block for FeatureReview` |

**Teeth, proved by reverting the production line each behaviour depends on** (eight checks, run
once from a throwaway script and recorded here rather than kept): `FeatureBases.WhyNot` answering
nothing → the different-base test red; the `--log` ladder pointed at a column that is never there →
the four note tests red with `""` (the `--log` process one included); composition spelling the stage
pair by hand → the feature row dropped; the `Defaults` dictionary asking "is it the plan role" →
`FeatureDefault` not the same reference; the feature begin's held-gate arm deleted → the unresolved
sentence where `call_human` was owed, and `Moved` where `Refused` was; the skip saving the begun
state → `AwaitingResolve` true where untouched was owed; the coalescing condition off → ten rows
where one was owed; and, in the extension, `bucketOf` put back as the template-string cast → a row
of stage `sometime` bucketed `sometime:code` where `result:code` was owed. Each restored and watched
green.

**The released half is a script, not a suite** — `src_vs_code/scripts/live-feature-schema-compat.mjs`
(`npm run test:feature-compat`), the shape of `live-close-consult-compat.mjs`: it downloads the newest
PUBLISHED `mcp-v*` release (drafts excluded — 0.38.0 was one, carrying three platforms) and runs both
binaries over three data directories. NOT in CI: it needs the network and a released artefact, and a
check that silently skips reports success for having done nothing. Its three legs and the 2026-09-26
outcome against 0.37.0 are in `module_server.md` and the plan's §4.13.

**What this does NOT prove.** No tool reaches the stage yet: the engine is driven through its
internal seam with a `StageRun` shaped by hand, and `ScenarioCoverageTests` derives its flow list from
the tool registry, which is unchanged. `review_feature` is S2.2's, with its end-to-end scenario (S2.2b — the section below).

## `review_feature`, through the tool (2026-09-26, S2.2b of the feature-review plan)

The stage S2.1 built, now reached by the eleventh tool — and the pack S2.2a built, now handed to a
reviewer. Every class written RED first where it covers new behaviour; the observations are here
because "tests pass" is not evidence.

| Suite | What it holds | Seen red as |
|---|---|---|
| `AFeatureIsReviewedEndToEndTests` (scenario) | A REAL repository — the plan, a class, a credential-shaped `.ts` file at base; one method body changed, a file added, the credential file changed at head — and the fake CLI as the one feature reviewer (`FAKECLI_RECORD_DIR` keeps its stdin). **Proceed**: the recorded prompt carries the plan, the epics and lessons fenced as claims, the history slot, the rules, the range, `*` on the changed method, that method's hunk, the feature schema and the outline truth — and NOT the body of an unchanged method; the reviewer's source request comes back on its note; `resolve(feature)` closes the review and `status(feature)` reads `Done`. **Credential file**: named as withheld, its secret absent. **Again**: refused without `again`, refused over an unmoved head, run over a moved one, both rounds numbered 1 and 2. **D17**: two epics skipped, does not block, nobody launched, the base never asked; the threshold following `COAI_FEATURE_MIN_EPICS`. **No vendor ticked**: skipped, saying so. **All failed**: `call_human`, and `ask_human(feature)` files the question under the feature session's id | the proceed test first failed on the recorded stdin's encoding (the fake CLI decodes stdin in the console code page, so its assertions stay ASCII); with the note wiring reverted, the source request missing from `notes` |
| `TheFeatureStageRefusesBeforeItBuildsTests` | Every refusal before the engine, each its own sentence: `lessons` empty/absent/a bare "none" — with all four questions; `epics` out of bounds; a plan outside the repository; a missing plan in the tool's own words; base = HEAD; a base that does not resolve; an option-looking base; a base that is not an ancestor; a range that changed only a lock file (named); a subdirectory as `repoPath`; `document` and `feature` together; a different base without `again` naming both SHAs. One positive: a good call passes every guard (a D1 skip, nobody ticked) | a missing plan threw `FileNotFoundException` from `DocumentReader.FollowLink` — the D12 defect below |
| `ThePackWithholdsCredentialsTests` | On a real repository with the real outliner: a credential-shaped file in a supported language named as withheld and absent from the pack; a secret in an outlined default argument and in a changed hunk line redacted, the rest of the hunk kept; an ordinary file named for a credential WORD (`Auth.cs`) outlined with its hunk; every `CredentialFiles.Patterns` shape (derived from the table) withheld by `ReadPlan`, and its companion — an ordinary file read by the same plan | `Expected pack … not to contain "hunter2-the-staging-password"`, `"ghp_…"`, `"abcdefghijklmnopqrstuvwxyz0123456789"`; `Expected plan.Read to be empty … but found at least one item` |
| `OutlineComposerTests` (the reserve) + `FeatureBudgetTests` | A notices-shaped feature (220 files whose outline alone is ~2.5× the budget, one small changed member each) shows ≥ 20 hunks filling the 56 KB reserve, still under 168 KB, files dropped by name; an unused reserve flows back to the outline; the budget property over 200 seeds now also asserts the hunks get their reserve (≥ min(reserve − overhead, all hunks) − one unit) and that EVERY unit, a dropped file's included, is shown or named; the reserve is a third of the budget and scales | `Expected shown to be greater than or equal to 20 … but found 0` — S2.2a's measured "notices get no hunks", reproduced; with the reserve line reverted, the property red at seed 2 (400 bytes of hunks where 4 070 were owed) |
| `TheFeatureRoundAnswersInItsOwnSchemaTests` | What the roster BUILDS: a feature reviewer is quoted `sourceRequests` and handed `finding-schema-feature.json`; a code reviewer never is; every stage row's `Answers`/`Reads`, derived from the enum; `SourceRequestNote` keeps notes as they were when nothing was asked | with `SchemaFile.Ensure` put back to the finding shape, the feature launch handed the plain schema file |
| `ThePromptSaysWhatTheReviewerHasTests` | The three `ReaderMaterial` truths differ; the outline truth names the hunks, `*`, the truncation marker (read back from `MemberHunks`' own output, not retyped), "What this context left out", `sourceRequests`, and RECORDED-not-answered; the source-read guard now holds the material and schema coming from the stage's row | — (the source-read assertions moved with the rename) |
| `McpContractTests`, `ScenarioCoverageTests`, `TheRefusalRoadsAreCountedTests` | Eleven tools over real stdio; `review_feature` covered by the scenario above; `shared/refusal-sites.json` re-recorded (`FeatureStage.cs` a new caller of `Refusal.Answer`) | the tool list and the census, red until updated |

**A defect found on the way (D12).** `DocumentReader.FollowLink` threw `FileNotFoundException` for a
path with nothing at it, so `status`/`resolve`/`ask_human` with a `document` nobody reviewed answered an
SDK-level error. RED first — `ADocumentIsReviewedEndToEndTests.AskingAboutADocumentThatIsNotThere_IsASentence_NotAnException`
failed with `System.IO.FileNotFoundException : Could not find file '…\docs'` — then green. And a
test-side one: `OutlineComposerTests`' property built its units with `SelectMany(MemberHunks.Units)`,
which binds the element's INDEX into `Units`' optional `cap`; every unit it checked was truncated to its
index in bytes. Harmless while it compared headings, it made byte counts meaningless, and it is a
lambda now.

**Teeth.** The reserve line reverted → the notices test, the flows-back test and the property red; the
credential arm and the redaction removed → the four credential tests red with the secrets in the pack;
the note wiring reverted → the proceed scenario red; the schema shape reverted → the schema test red.
Each restored and watched green.

**What this does NOT prove.** The vendor is the fake CLI on the codex adapter; no real model, no `api`
runtime and no live round on this repository ran (the plan's live acceptance waits on the vault, S1.2).
Source requests were recorded, never served, until S3.2 — the section *A reviewer that asks for source is
asked again* below drives the second turn. The released-half check for the new tool is not run: an old
extension never calls `review_feature`, and the tool's arguments are all new.

## The feature gate after its code review (2026-09-26)

Seven findings of epic 2's code review, each fixed RED first — the test written against the shipped
commit and watched fail with the real symptom, the fix, GREEN, and then every fix planted back (the old
behaviour behind the new APIs, in one build) and watched go red again. The suites are the existing ones;
the rows below are what was added to them.

| Suite | What was added | Seen red as |
|---|---|---|
| `FeatureOutlineBuilderTests` | A changed line that quotes the launcher's cut sentence keeps its file's `*` marks and its hunk; EVERY recorded git process runs under `GitDeadline` — the merge-base included, and the shallow-history fallback on an orphan base; every cut member hunk is named by its file AND its outline span (`CutMembersOf`, the text after `path: ` on the cut-hunks line) | `Expected outline.Section … to contain "*   public int Sell(int n)"` — the whole piece was dropped for containing the sentence; `Expected _launcher.Requests to contain only items matching (r.Timeout == GitDeadline)` — the `merge-base` at ten minutes, on both roads; the span assertion held on the shipped code, and with the renderer printing the path alone it read `"1 member(s)" to contain "[1-3] +3/-0"` at every budget |
| `AFeatureIsReviewedEndToEndTests` | A skipped first call pins no base, and a first call whose pack could not be built (`WatchedLauncher` answering `cat-file --batch-check` with exit 128) pins none — the later call naming another base runs, and the round that runs records it; `again` over an unmoved head is refused in an OPEN review too; `again` against a new base is a fresh review from an open review (the same finding gates again: `revise`, round 1 of a fresh budget, no rejections) and from a finished one; `again` against a new base still waits for `resolve` and for a person; with nobody ticked no `cat-file` and no `-U0`/`-U3` diff is asked for — the one diff is the refs' numstat | `this plan's feature review was opened against base X, and this call names base Y … Pass again: true` where a skip, then a `proceed`, was owed; `Expected error to be True because the head has not moved … but found False` — the open review ran its round again; `Expected verdict to be "revise" … but "proceed"` — the old rejection discounted the new review's finding; `Expected Rejections to be empty … found at least one item`; `{"cat-file", "--batch-check"}, {"cat-file", "--batch"}` in the recorded requests. The two guards were green on the shipped code and red with the machine's `HumanGate`/`AwaitingResolve` arms deleted |
| `AFeatureRoundIsSkippedNotRefusedTests` | Rows 1–3 with a `MakeWork` that throws: the skip is recorded without building | `the round failed: the pack was built for a round nobody can review` |
| `TheFeatureStageRefusesBeforeItBuildsTests` | `planPath: ".env.production"` refused by its name — the file written first, reading like a plan; D14 held in stage `FeatureReview` exactly as in `Done` | `a refusal was expected: {}` — the credential file was read as the plan and the call became a skip; `WhyNotThisRound … "" to contain "the head has not moved"` in stage `FeatureReview` |
| `FeatureContextTests` | The plan inside `ConsultationFence.Material` under `FeatureContext.PlanMaterial`, between its heading and the epics; a vendor key in the plan, a bearer and a password assignment in the history, a URL credential in an epic and a password in a lesson never reach the context. The cut test now measures the plan's bytes inside its fence | `Expected opened to be greater than 0 … but found -1`; `Expected text … not to contain "ghp_…"` |
| `TheRefusalRoadsAreCountedTests` | Unchanged and green: `FeaturePlan`'s new refusal is a `FeatureInput.Refused` that leaves by `FeatureStage.Refused` → `Refusal.Answer`, the road the census already counts, so `shared/refusal-sites.json` did not move | — |

Two fixtures were corrected on the way, both of the kind [testing](../.agents/conventions/common/testing.md)
warns of — a fixture the code rejects: a finding of `category: "correctness"` is outside the schema's
enum and was silently dropped (so a threshold of one still passed), and a role at EXACTLY its threshold
passes, so a `revise` needs `RoleGate(2, 0)`.

**What this does NOT prove.** The plant was one build with every old behaviour at once; a test that
would have gone red only for a second, uncombined reason was not separated out. The redaction's
fail-closed line ("could not be redacted in time") in the context has no test that makes a pass time out.

## The feature gate on the macOS job, and two Sonar findings (2026-09-26)

The PR of epic 2 went red on `macos-latest` (23 tests) because the top-level check compared git's
answer — the REAL path, `/private/var/…` — with the caller's spelling, `/var/…`. The link is now made by
the tests themselves (`DirectoryLink`, a junction on Windows with no privilege needed, a symbolic link
elsewhere — extracted from `AReviewRootBehindALinkTests`, which now uses it too), so the defect is red on
every platform.

| Suite | What was added | Seen red as |
|---|---|---|
| `TheFeatureStageRefusesBeforeItBuildsTests` | A repository reached through a directory link is its own top level (a good call is a D1 skip), and through the link a missing plan gets the missing-plan sentence | `'…\coai-feature-refusals-link-…\repo' is inside a repository whose top level is 'C:/…/coai-feature-refusals-…'` — on Windows, through a junction; the same sentence again with the old spelling comparison planted back |
| `GateHistoryQueryTests` | A round and a consultation recorded under the real path belong to the repository asked through a link; a database stopped before `outcome`, and one stopped before `kind`, still yields its consultation with each missing column's meaning (the S2077 fix is three constant query texts) | `Expected history.Rounds to contain a single item … but the collection is empty`; with a wrong ladder planted, `Expected read.Outcome to be "solved" … but ""` |
| `ACredentialLookingNameIsRefusedTests` | Every exclusion matcher carries a match ceiling (derived from `DiffExclusions.Matchers`, checked equal to the glob table); a match that times out WITHHOLDS the path and names its glob, through the `FirstExcluding` seam | with the ceiling removed, `Expected DiffExclusions.Matchers to contain only items matching (… MatchTimeout != Regex.InfiniteMatchTimeout)` |

**What this does NOT prove.** No macOS machine ran these here; the Windows junction is the same class
(git resolves it as macOS resolves `/var`) and went red with the same sentence. The fail-closed test was
written with its fix, and its revert (the `catch` answering `false`) and the gate-history link revert were
not run — the session's permission policy refused both temporary plants; the link test's RED was
observed before the fix instead. No input is shown actually reaching the 1000 ms ceiling.

## A reviewer that asks for source is asked again — the turn loop (2026-09-26, S3.2 of the feature-review plan)

The loop of plan §4.9 (one reviewer, one conversation, one terminal outcome), driven through the REAL
scheduler, the real codex adapter's argv, a real repository the source is read from, and the fake CLI
answering by TURN — which it reads off the prompt's tail heading (`FAKECLI_TURN_MARKER`, a text with
`{n}`; `FAKECLI_REPAIR_MARKER`; `FAKECLI_TURN<n>_STDOUT|OUTFILE_TEXT|EXIT|STDERR|SLEEP_MS`, and the
`_REPAIR_` family), never off a counter, because two reviewers of one round interleave on any counter.
The fake now decodes stdin as UTF-8, as the launcher writes it and every real vendor reads it, so an
assertion over a served tail no longer has to stay ASCII.

| Suite | What it holds | Seen red as |
|---|---|---|
| `AReviewerThatAsksForSourceIsAskedAgainTests` (runners, `[Collection("fakecli-env")]`) | Turn 2's prompt is turn 1's byte for byte with the tail appended — `## Turn 2 of 4`, the request, the symbol fenced with path, lines and sha, its body, "only this turn's answer counts"; a reviewer that asks for nothing is not asked again (note empty, as before); the follow-up cap ends the conversation and the last turn was told FINAL; a spent allowance (a 400-line slice past 128 KB) makes the NEXT turn final whatever the cap says; a malformed turn 2 is repaired against turn 2's prompt (the repair's recorded stdin starts with turn 2's and ends with `RepairInstruction.Text`); a failed turn 2 keeps turn 1's cost on the outcome's `EarlierTurns` and as its own ledger line; a cancellation during turn 2 keeps it too, with ONE terminal outcome; a failed later turn is a failed reviewer (`0 of 1`), never turn 1's answer; a turn that times out is one terminal `TimedOut` with every turn's usage kept, and the reviewer queued behind the slot runs after it; a continuation that never stops is ended by the cap (`timeout × (1 + follow-ups)`) as one `TimedOut`; three two-turn reviewers of one vendor never raise the per-provider peak (green before and after — a guard); a `git show` slower than the resolver's deadline is `not served: path — timed out` while the other request of the turn is served | before the loop existed: `Expected prompts to contain 2 item(s) … but found 1`, `Expected type to be … TimedOut … but found … Ok`, `… NonZeroExit … but found … Ok`, `Expected type not to be … Ok`, `Index was out of range` (no second prompt); with the reviewer-cap flag planted back to `false`: `contain 2 item(s) … but found 3` (no FINAL, a third turn); with the ledger's per-turn loop planted out: `contain 2 item(s) … but found 1` |
| `TurnTailTests` (core, pure) | The plan's order, asserted by POSITION: heading, compact findings, requests, the served heading, the body, `not served`, the instruction, `AskAgain`; FINAL on the last turn and on a spent budget, and `AskAgain` never beside it; earlier turns' source before this turn's; `nothing`/`none` said rather than empty sections; a compact finding names its place only when it has one and never breaks its line; a rejected request named with its reason | with `Final` planted out: `TheLastTurn_IsToldFinal…` and `ASpentBudget_IsFinalBeforeTheCap` red on the missing sentence |
| `TheFeatureStageIsServedTests` (the switch) | Absent → 3; `0`..`3` (and ` 1 `) taken as they are with no note; `4`, `-1`, `many`, `1.5` → 3 and ONE `UnrecognisedSetting` keyed `COAI_FEATURE_SOURCE_FOLLOWUPS` naming the value and `0 to 3` | with the parse planted to the constant and the note dropped: `Expected FeatureSourceFollowUps to be 2, but found 3`; `… to contain a single item matching (one.Key == "COAI_FEATURE_SOURCE_FOLLOWUPS"), but the collection is empty` |
| `ThePromptSaysWhatTheReviewerHasTests` | The outline truth with follow-ups says ANSWERED, `up to N follow-up turn(s)`, the resolver's own caps, "Only your LAST answer counts", and never the recorded sentence; with 0 it says RECORDED and never ANSWERED; the two share their outline half; the source-read guards follow the extraction (`{WhatYouHave(material, followUps)}`, `ComposePrompt(…, followUps)`, `RepairInstruction.Text` used by the roster and never copied) | with the truth ignoring the switch: `Expected said … to contain "ANSWERED"`; on the extraction, the two guards red until re-pointed |
| `LiveRoundTests` | `Finish` adds every turn of a conversation on an answered AND on a timed-out reviewer alike | `Expected record.TokensIn to be 3200L … but found 1200L` — turn 1 of the reviewer that timed out on turn 2 read as free |
| `LedgerAndEvidenceTests` | One line per turn: the earlier turns each with their tokens, cached count and seconds, then the terminal line with the last turn's own seconds (the reviewer's less the earlier turns') | `Expected lines to contain 3 item(s) … but found 1` |
| `UsageParserTests` | The cached subset read on its own — codex's `cached_input_tokens`, OpenAI's `prompt_tokens_details.cached_tokens` — and never added to the input; `Usage.Add` sums it | `Expected codex.TokensCached to be 7808L, but found 0L` |
| `ASourceRequestIsServedOrRefusedTests` | A launcher that throws on one file's `show` refuses THAT request (`git could not read it just now; ask again next turn`) and serves the rest; a read past the resolver's deadline is `timed out…`, never "served, empty", and is forgotten so the next turn asks git again (two `show`s) | `System.IO.IOException : the object store is locked` escaping `ServeAsync`; `Expected first.Served to be empty … but found at least one item` (the read completed after the delay, unbounded) |
| `AFeatureIsReviewedEndToEndTests` (scenario, extended) | Turn 1 asks for `Shop.Sell` with a gating finding and turn 2 withdraws it → `proceed` with no findings, two prompts, the second the first byte for byte plus `## Turn 2 of 4`, the compact finding, the served member's BODY (the one line D3 keeps out of the pack), the reviewer's note `2 turns; source: turn 2: served src/Shop.cs Sell (…)`, two ledger lines; a credential file asked for in turn 2 is `not served … looks like a credential file (.env*)` and its secret never arrives while the other request is served; a failed turn 2 still costs turn 1's 1000 tokens in the round's `cost` and as a ledger line, and is `call_human` (every reviewer failed); with `FeatureSourceFollowUps = 0` one prompt saying RECORDED and the request on the note under `SourceRequestNote.Heading`; four turns of 0.7 s under a 2 s reviewer timeout `proceed` because the round's deadline is `reviewerTimeout × (1 + follow-ups)` | with the roster's wiring planted to `None`: `Expected Prompts() to contain 4 item(s) … but found 1`, `Index was out of range`, `Expected … "proceed" … but "revise"` (turn 1's finding stood); with the scaling planted out: `call_human` — `codex/FeatureReview: timeout; the round reached its 0 minute limit and the reviewers still running were cancelled` |

**Teeth.** Two plants, each one build with independent behaviours planted at once and each behaviour
red on its own test: A — the deadline scaling, the switch's parse and note, the truth's switch, the
cached subset, FINAL; B — the ledger's per-turn loop, the reviewer-cap flag, the roster's wiring. The
loop itself, `LiveRound.Finish` and the resolver's two refusals were seen red BEFORE they were built,
with the messages in the table. Each restored and watched green.

**What this does NOT prove.** No real vendor answered a second turn, so whether any vendor's prompt
cache reuses the resent prefix is unmeasured — the number is recorded per turn (`tokensCached` on the
ledger line, `(N cached)` on the audit line) for the first live run to read. The launcher reports a
caller's cancellation mid-launch as a killed tree (`TimedOut` with `Cancelled`), which the executor names
`TimedOut`; the scheduler's "cancelled while it was running" outcome is reached only when the cancellation
lands between two turns — the cancellation test asserts the cost is kept, not which of the two names it
gets. The `api` runtime's `tokensCached` is read from the shim's line, and the shim's line from the
endpoint's `prompt_tokens_details`, without a live endpoint.

## The feature gate on the person's side, and its released halves (2026-09-26, S3.3 of the feature-review plan)

The extension's half of the fourth gate (S3.3a) is covered by the extension suite, each test written RED
first; what each one saw red is recorded beside the change in [module_extension.md](module_extension.md).

| Suite | What it holds |
|---|---|
| `featureGate.test.ts` | `feature` absent is no and stored only when true; a Team-server row forced off with its sentence; the wire carrying the tick only for a server not known to be older than `FEATURE_SINCE`, while the row always crosses; the defaults comparison; the vendor card RUN with its fourth switch |
| `shippedVendorsDiffer.test.ts` | A change to one of the two shipped rows — a feature tick, an unticked plan or code box, a said document box, a CLI path — reaches `COAI_VENDORS` (D12, each case found RED) |
| `featureStageNames.test.ts` | `stageName` for `DocumentReview` (§9.5, red on the shipped code) and `FeatureReview`, an unknown stage raw |
| `bundledPage.test.ts` (two tests) | The rounds-log page bundled, minified and RUN: a skipped round is `skipped — did not block`, never `done`; opened, it shows the server's reason with `×N`, not "written by an older server", and asks for no findings |
| `featureSnippet.test.ts`, `snippetVersion.test.ts`, `prepareGate.test.mjs` | The fifth half, its marker and version, the artefact version and body hash; the emitted module executed; a foreign or missing source refused; since S3.5 (2026-10-09) the half is the mounted shared rule and a mount is judged on all five halves |
| `featureHelp.test.ts`, `featureStageOnRolesPage.test.ts` | Help in five languages; the roles page's Stage select offering `feature` |

**The released halves are a script, not a suite** — `src_vs_code/scripts/live-feature-vendor-compat.mjs`
(`npm run test:feature-vendor-compat`), beside S2.1's `live-feature-schema-compat.mjs`; both now take the
download, the build and the one-shot runner from `scripts/releasedHalves.mjs`. It downloads the newest
PUBLISHED `mcp-v*` and builds the newest published `extension-v*` from its tag, and drives four seams: this
extension's settings file, env block and the `providers` tool against the released server; the released
extension's `vendorsFrom` over the rows this build stored; `review_feature` on this server over the
released extension's settings (a real fixture repository, a plan of three epics, over MCP); and the released
extension's session parser, `--log` parser and rounds-log page — bundled and run — over what this server
wrote, then the snippet both ways. NOT in CI, for the reason its sibling gives. **Run 2026-09-26 against
coai-mcp 0.38.0 and extension 0.56.3: 29 assertions, all green** — the outcome, seam by seam, is the table in
module_extension.md and the plan's §6. S2.1's script was re-run the same day against 0.38.0: 19 of 19.

**Teeth.** The assertion that the released server read the whole vendor list is only worth something if a
refused list would look different: a `settings.json` whose `COAI_VENDORS` is malformed was handed to the
released 0.38.0 by hand, and it listed `codex, antigravity` — its defaults — which the script's
`codex, antigravity, codex-feature` check fails on. Every leg names the build it ran in its output, and the
script exits 2, never 0, when a released half cannot be obtained.

**What this does NOT prove.** The released extension is BUILT from its tag rather than installed from its
`.vsix`: the same source and compiler, but not the minified bundle, except for the rounds-log page, which is
bundled again. No VS Code ran it — the page runs against a stub DOM, and the vendor card of the released
build was not drawn. No reviewer ran: the only feature round in the script is a skip, so the released
extension never saw a feature round with findings. A Team server was not involved (it runs no feature
review). The case of a server the panel KNOWS is older is the unit suite's (`featureGate.test.ts`); the
script measures the one the panel cannot see.

## The second round, only when it is needed (2026-09-26, S3.4 of the feature-review plan — D23)

Every rule of D23 was written RED first against the shipped code, with the new fields and functions
present as inert seams (an enum, a state field, a `LatestAnswerFor` answering null, an
`ApplyPersonsRequest` returning its input) so that each failure named the real symptom rather than a
missing symbol. The observations are here because "tests pass" is not evidence.

| Suite | What it holds | Seen red as |
|---|---|---|
| `TheSecondFeatureRoundTests` (core, pure) | The shipped feature budget is two rounds; a gating round with nothing blocking closes on `resolve` however many findings gate (`good_enough`, `AdvanceOnResolve`), and one under the threshold still `proceed`s; after it, `again` over the same base is refused naming the three grounds and `ask_human`; a blocking finding admits round 2 (`revise`, one left, `SecondRound = BlockingFinding`) and a blocking round 2 calls a person; a clean round 2 closes and a third is refused; a partly failed round admits a retry even over a passed gate; everybody failing in round 1 is a retry, not a person, and failing twice is a person — the gate never fails open; a blocking finding outranks a failure; a budget of one runs no retry; a fresh review starts its ground over; the caller cannot claim the person's request with `humanDecision: proceed`, and `again` alone never admits round 2; the person's `Continue`/`Fix` admits round 2 AS round two (count kept, rejections kept), `Discuss`/`None` change nothing, and nothing is recorded after two rounds, on a held gate, or on a branch session; a person's fresh set starts the ground over; the human override still cannot close a round that admitted a second; the plan stage keeps its own rule | `Expected PanelConfig.FeatureDefault.MaxRounds to be 2 … but found 1`; `Expected type to be RoundVerdict+GoodEnough … but found RoundVerdict+Revise`; `Expected type to be Transition+Refused, but found Transition+Moved` (again after a non-blocking round; a round 2 with no ground; a third round); `Expected type to be RoundVerdict+Revise because a passed gate with a failed reviewer is a retry, not a proceed, but found RoundVerdict+Proceed`; `Expected type to be RoundVerdict+CallHuman, but found RoundVerdict+Proceed` (a blocking or failed round 2); `Expected asked.SecondRound to be PersonAsked … but found None`; `Expected held.HumanGate to be True, but found False` |
| `ThePersonAsksForASecondFeatureRoundTests` (server) | `PersonsRequest.Apply` against a real `Escalations` directory: a `continue`/`fix` answer newer than the last feature round is the request; an older one was about something else; `discuss` and a typed sentence are not; no answer and an unreadable `answeredUtc` are not | `Expected asked.State.SecondRound to be PersonAsked … but found None` |
| `HumanDecisionTests` | `LatestAnswerFor` returns the newest answer whole — decision and `answeredUtc` — and null for a session nobody answered (the method went 2026-10-03 with its last reader; the test now reads the answer file through `ReadAnswer`) | `Expected answer not to be <null>` |
| `AFeatureIsReviewedEndToEndTests` (scenario, the real service and the fake CLI) | **Close path:** one major over a threshold of zero → `good_enough`, the instruction says the review closes on resolve; `resolve` → `Done`; a moved head and `again: true` → refused naming "reviewer failure", "blocking" and "person"; one round on the trail. **The caller cannot claim the request:** after that close, `again: true`, `resolve(humanDecision: proceed)` and a self-asked `ask_human` (unanswered) are each refused; the answer file written as the panel writes it (`continue`, now) admits round 2 — `proceed`, rounds `[1, 2]`, `AdmittedBy = PersonAsked`, the base unchanged — and after its resolve a second answer is refused with "two rounds". **Blocking:** `revise` with an instruction naming `blocking` and `again: true`; `resolve` keeps the review open; a fix and `again: true` → round 2 still blocking → `call_human`, `AdmittedBy = BlockingFinding`, the gate held, and un-ticking every vendor meets the human-gate sentence. **Partial failure:** two vendors on the fake CLI, the second's launch answered with exit 1 by a `WatchedLauncher` keyed on its model — `revise`, "1 of 2 reviewers answered", one finding to decide, the instruction naming "only the reviewers that failed", one launch recorded; after `resolve(reject)`, round 2 launches exactly one more process and it is the failed vendor's, the answered vendor is "not asked … round 1", `carried` holds the rejected finding with its reason, `AdmittedBy = ReviewerFailure`, one reviewer on the record. **All failed:** `revise` naming a retry and "no new head", no held gate; after `resolve([])` the retry fails → `call_human`, more launches than round 1 and every one the same reviewer, `AdmittedBy = ReviewerFailure`, and `ask_human(feature)` still files under the feature session. Reshaped: a failed second turn is a `revise` (retry) rather than `call_human`; `again` over an unmoved head is refused in an OPEN review (now a blocking one); a fresh review against a new base from an open review, and one that still waits for `resolve`, use a blocking finding to be open; one that still waits for a person fails twice first | `Expected … to be "good_enough" … but "revise"`; `Expected … to be "revise" … but "proceed"` (a blocking finding passed the threshold-five gate); `Expected … to be "revise" … but "call_human"` (every reviewer failed); `Expected refused.TryGetProperty("error") to be True … but found False` — the moved head ran a second round; then, with the D23 arms in, `Expected third.TryGetProperty("error") to be True because a third round is refused … {"verdict": "proceed" …}` — the D12 defect below |
| `AFeatureRoundIsSkippedNotRefusedTests` | The un-tick guard holds on the SECOND failure's held gate, through the engine with a hand-shaped run and `resolve` between the two | `Expected … to be "revise" because the first failure buys a retry … but "call_human"` |
| `AFeatureSessionIsKeptApartTests` | `again` reopens a finished review for its second round with the count KEPT (on the person's ground); a finished review of two rounds is refused; an open review with a ground is simply begun | `Expected reopened.RoundsRunThisStage to be 1 … but found 0`; `Expected type to be Transition+Refused, but found Transition+Moved` |
| `TheFeatureStageRefusesBeforeItBuildsTests` | `WhyNotThisRound` with `again` over an unmoved head is empty for a `ReviewerFailure` and a `PersonAsked` ground, and still "the head has not moved" for a `BlockingFinding` | `Expected … to be empty because a ReviewerFailure second round needs no new head, but found "the head has not moved …"` |
| `humanDecision.test.ts` (extension) | `decisionChoices(':feature')` keeps the same three decisions and gives the first a detail that says "second and last round" and "at most two"; a branch review's detail is unchanged, and so are the other two choices | the call did not compile before the parameter existed — the extension's only RED here is the signature, and the behavioural assertion (a branch review unchanged) was green throughout |
| `panelServerDefaultsAgreement.test.ts` (extension) | The panel's `FeatureReview` round default is the number `SessionState.cs` names — read from the C#, never transcribed | `FeatureReview: the panel shows 1 rounds, the server would run 2`, the day the C# moved first |

**A defect found on the way (D12).** `PanelService.WithHumanDecision` applied the session's newest
answered notice on EVERY `resolve`, gate held or not: the person's one "keep going" reset the round count
for the life of the session, so round 2 resolved to a count of zero and `again` ran a THIRD round with a
`proceed` where "two rounds" was owed. The scenario is the red test — the plant was the shipped line — and
the guard is the engine's own (`HumanGate` first).

**A second one, fixed on its own commit (D12): an answer to an earlier hold reopened a later one.**
Both held-gate readers took the newest ANSWERED question, which walks past an unanswered notice. RED
first, each seen failing for the real reason: `TheAnswerBelongsToTheCurrentHoldTests` against an inert
`CurrentAnswer` that returned the newest answer whenever it was given — `Expected … to be
HumanDecision.None because a 'keep going' given on the earlier hold has been spent … but found
HumanDecision.Continue`, and the no-round and unreadable-time cases likewise; and the scenario
`AFeatureIsReviewedEndToEndTests.AnAnswerToAnEarlierHold_DoesNotReopenALaterOne_AndTheNewAnswerDoes`
against the shipped readers — two holds raised by four failed rounds, hold one answered, hold two's
notice open — red at the first reader it meets, `Expected Session().State.RoundsRunThisStage to be 2
because resolve on hold two must not spend hold one's answer on the count, but found 0`. GREEN with
`CurrentAnswer` routed through all three readers. Revert check: the engine's reader alone put back to
`DecisionFor` (the `resolve` reader kept) — the scenario red with the engine's own symptom, `Expected
answer.TryGetProperty("error") to be True because hold one's answer must not open hold two … but found
False` (a round ran where `GateHeld` was owed); restored, green.

| Suite | What it holds | Seen red as |
|---|---|---|
| `TheAnswerBelongsToTheCurrentHoldTests` (server, a real `Escalations` directory) | An answer given after the hold's round completed is the hold's; an answer to an earlier hold is not, though it is the newest answered and the current hold's notice is open; a skipped row after the hold is not the round the answer is measured against; no round means no hold to answer; an unreadable `answeredUtc` is no answer | `Expected … to be None … but found Continue`; `Expected … to be <null> … but found EscalationAnswer` |
| `AFeatureIsReviewedEndToEndTests` | The two-hold scenario above: hold one's answer neither spends hold two's count on `resolve` nor opens hold two before a round (`GateHeld`, the gate still held); the answer to hold two does | `Expected RoundsRunThisStage to be 2 … but found 0`; with the engine's reader planted back, `Expected error to be True … but found False` |

**Teeth, by reverting the line the behaviour depends on.** Three plants, one build each, restored and
watched green after: the feature branch of `CompleteRound` removed (the machine's rule) — 20 of 47 red,
12 in `TheSecondFeatureRoundTests` and 8 in the scenario, the rest green because they are about the
admission and the road; the two D23 arms of `BeginFeatureRound` removed (the admission) — 8 of 76 red,
every "refused" test and nothing else; `PersonsRequest.Apply` returning its input (the provenance road)
— 3 of 54 red, the two newer-answer tests and the scenario's "the person's answer admits round 2", with
every "the caller cannot claim it" assertion still green, which is what proves the road is the only way
in.

### Three defects from the epic 3 risk consultation (159f0397, 2026-09-26)

Each RED first with the real symptom, GREEN, then the behaviour line planted back and watched red again.

**A failed repair loses the billed usage of the malformed attempt** (§9.13). RED before any behaviour
changed, through the surfaces that existed — the ledger line and the round total, since the outcome
members that now carry it did not exist yet: `Expected lines[0].TokensIn to be 1000L because the first
launch completed and reported what it consumed; only its answer was malformed, but found 0L` (a repair
that timed out, and one that exited 3), the ladder case `Expected … 3000L … but found 0L`, and through the
turn loop the terminal ledger line `"tokensIn":0,… "outcome":"timeout"` where 700 was owed. GREEN with
`EarlierLaunches`/`LastTurnUsage`/`TotalUsage` on the base, `AfterTheRepair`, the ledger and the round
total on the one member, the ladder carrying forward — and the outcome-level assertions added then.
Revert checks: the failure arm's `with { EarlierLaunches = … }` planted back to `return failed` — both
theory cases `Expected outcome.EarlierLaunches.TokensIn to be 1000L … but found 0L`, the ladder case
`… 3000L … but found 0L`, the turn-loop test `Expected timedOut.LastTurnUsage.TokensIn to be 700L … but
found 0L`; then the ladder's carry alone planted back to `outcome = next` — the ladder case only,
`Expected outcome.TotalUsage.TokensIn to be 3000L … but found 1000L` (the last step's own attempt kept,
the two earlier ones lost), the theory green. Restored, green.

**The D23 retry silently drops a failed reviewer that is no longer on the roster** (§9.14). RED:
`Expected sentence "all 1 reviewers answered" to contain "grok/FeatureReview was not asked: failed in
round 1 and is no longer enabled"`. GREEN with the gone reviewers named. Revert check: the `gone`
filter planted to match nothing — `Expected … retry.NotAsked to contain a single item matching (r.Role ==
"grok/FeatureReview") … but the collection is empty`, `… a collection with 2 item(s) … but found an empty
collection`, and the sentence assertion; the "still enabled" and "not a retry" guards green throughout.

**A person's answer is bound to its hold by time, not identity** (§9.15). The data-only field
`SessionState.HoldQuestions` was added first so a test could name a hold's notice; RED then, against the
shipped `CurrentAnswer`: `Expected CurrentAnswer.DecisionFor(heldTwo, Escalations()) to be
HumanDecision.None because q1 was asked for hold one; answering it after hold two began is not a decision
on hold two, but found HumanDecision.Continue`; the machine's release `Expected … HoldQuestions to be
empty … but found at least one item {"n2"}`; and end to end `Expected answer.TryGetProperty("error") to
be True because a question asked for hold one cannot release hold two, whenever it is answered … but found
False` — the round RAN. GREEN with the identity rule, the engine's save, `ask_human`'s recording and the
machine's release. Revert check: `CurrentAnswer.For` planted back to the clock alone — the unit test red
with the same `None … but found Continue`, the scenario red with the same `error … True … but found
False`; restored, green.

| Suite | What it holds | Seen red as |
|---|---|---|
| `AFailedRepairStillCountsTheMalformedAttemptTests` (runners + server, `[Collection("fakecli-env")]`) | A repair that times out or exits non-zero after a malformed first attempt: the failure carries the attempt's usage on `EarlierLaunches`, `LastTurnUsage`/`TotalUsage` say 1000 in / 50 out, the ledger writes ONE line with it (outcome not `ok`), `LiveRound.Finish` counts it once; a repair that is rate limited on every step of a two-step ladder: three attempts, three malformed launches, `TotalUsage` 3000, one ledger line | `Expected lines[0].TokensIn to be 1000L … but found 0L`; `Expected outcome.TotalUsage.TokensIn to be 3000L … but found 1000L` (the ladder's carry alone planted) |
| `AReviewerThatAsksForSourceIsAskedAgainTests` (the new method) | A turn whose repair times out is one terminal `TimedOut` with turn 1 on `EarlierTurns` (1000), the malformed attempt as `LastTurnUsage` (700), `TotalUsage` 1700, one terminal progress report, the reviewer queued behind the slot runs, and the ledger writes turn 1's line and the timeout's with 700 — two lines, never a third | `Expected lines[1] "{…"tokensIn":0,…"outcome":"timeout"…}" to contain ""tokensIn":700"` |
| `ARetryNamesTheFailedReviewerThatIsGoneTests` (server, pure) | With codex and grok failed in round 1 and grok switched off, the retry asks codex and names `grok/FeatureReview` as not asked with *failed in round 1 and is no longer enabled, so it was not retried*; the reviewer sentence (the reply's `reviewers`, the audit line) carries it; with no failed reviewer left, everyone is asked and both gone are named; with every failed reviewer still enabled nobody is named gone; a round that is not a retry is untouched | `Expected sentence "all 1 reviewers answered" to contain "grok/FeatureReview was not asked: …"`; `… to contain a single item matching (r.Role == "grok/FeatureReview") … but the collection is empty` |
| `TheAnswerBelongsToTheCurrentHoldTests` (five more) | An old question answered after a later hold began does not release it, and the hold's own question does; an `ask_human` question recorded for the hold releases it too; the newest of the hold's own answers wins; a hold recorded without its questions (an older build's) is released by the clock as before; the questions are released with the hold (`ApplyHumanDecision` Continue, `Resolve` with `humanSaysProceed`) and kept on `Discuss` | `Expected … to be HumanDecision.None … but found HumanDecision.Continue`; `Expected … HoldQuestions to be empty … but found at least one item {"n2"}` |
| `AFeatureIsReviewedEndToEndTests` (the new scenario) | Hold one raised, `ask_human` under it left open; hold one's notice answered; hold two raised — its notice recorded on the session; the OLD question answered now: `GateHeld`, the gate still held; `ask_human` under hold two records a second question; its answer opens the hold (`proceed`) and the list is empty after | `Expected answer.TryGetProperty("error") to be True because a question asked for hold one cannot release hold two … but found False` |
| `ASessionFromAnOlderBuildStillRunsTests` (one more) | A session file without `holdQuestions` reads back empty, never null | a guard: green from the first run, by the same normaliser `Document` and `Feature` carry |

**Numbers.** `CoaiMcp.Tests.exe`: 5906 total, 5900 succeeded, 6 skipped (the platform skips), 0
failed, 7 m 40 s. `CoaiServer.Tests.exe`: 334 of 334. `CoaiBugs.Tests.exe`: 319 total, 317 succeeded,
2 skipped, 0 failed. Extension: `npm run lint` clean; `npm test` 4584 tests across its batches, 4582
passed, 2 skipped, 0 failed; `generate-help-prompts.mjs --check` up to date; `plan-lifecycle.mjs`
clean; `pin-check.mjs` OK.

**What this does NOT prove.** The vendor is the fake CLI on the codex adapter, twice over for the partial
failure; no real model ran a second round, and whether a real vendor's cached prefix survives a retry over
the same head is unmeasured (D25's question, still open). The phone's Ask Inbox was not exercised — the
answer file is written by the test as `escalationAnswer.ts` writes it, and the panel's own quick pick is
`vscode` wiring no unit test reaches. The log page does not show `AdmittedBy` (the session file carries it;
the database does not). A round whose failed reviewer was un-ticked between the rounds asks everyone, by
the code's own rule, and no scenario drives that edited-roster case end to end.

## Seven findings from epic 3's code round (2026-09-27, §9.16–22 of the feature-review plan)

Each RED first with the real symptom, GREEN, then every fix's load-bearing line reverted in ONE build
(ten reverts, one cycle) and the targeted suites watched red again — 27 of 137 — then restored and green.
One finding is a guideline with no behaviour to observe (§9.22); its RED is a structural scan.

| Suite | What it holds | Seen red as |
|---|---|---|
| `APrivateKeyBlockIsNeverServedTests` (core + runners + the real service, `[Collection("fakecli-env")]`) | **§9.16, security.** A service-account JSON loses its escaped key body, keeps its other lines and its line count; a PEM in a C# fixture loses every body line (nine placeholders, indentation kept) and keeps the code around it; an escaped one-line JSON collapses to one placeholder before its footer; every variant (plain, RSA, EC, DSA, OPENSSH, ENCRYPTED, `PGP … BLOCK`) header to footer; an encrypted PEM's `Proc-Type`/`DEK-Info` lines are inside the block and its blank line stays blank; a block cut short of its footer still loses the body it has, through `SafeSource` and `SafeText`; a header literal alone in code is untouched, and a header-and-footer literal pair redacts only what stands between them; a notice quoting a key on one line loses the body; ten thousand footerless headers and a run past the bound are linear; a three-line hunk in the MIDDLE of a nine-line body — no marker in view — is withheld from the pack; and end to end a service account asked for in turn 2 is served without its key, and turn 3's "served in earlier turns" tail never repeats it | `Did not expect safe "{ … "private_key": "[redacted] PRIVATE KEY-----\nMIIEvQKEYBODYLINE01…" to contain "KEYBODYLINE"` (fifteen of the seventeen, each naming its road: the file, the escaped line, the pack, the prompt, the notice) |
| `privateKeyBlocks.test.ts` (extension, 16) | The same shapes against the TypeScript port, and `safeText` running it first | the port did not exist; its first run was green, and the parity harness is what holds it to the C# side — 423 cases byte for byte with seven key shapes crossed into the corpus |
| `AReviewerThatAsksForSourceIsAskedAgainTests` (one more) | **§9.17.** Turn 1 = a billed launch with no answer file, its repair refused with a 429, the ladder's retry answering and asking for source; turn 2 clean: four launches, turn 1's `EarlierTurns` entry says 1700, `TotalUsage` 1710, the ledger's two lines say 1700 and 10, `LiveRound.Finish` says 1710 | `Expected first.Usage.TokensIn to be 1700L because turn 1 is its malformed launch (700) AND the retry that answered (1000), but found 1000L` |
| `TheFeatureStageRefusesBeforeItBuildsTests` (reshaped) | **§9.18.** `WhyNotThisRound` over an unmoved head is empty for a retry and the person's request with and without `again`, "the head has not moved" for a blocking ground with and without `again`, and empty for a blocking ground over a moved head either way | `Expected … (again: False) "" to contain "the head has not moved"` |
| `AFeatureIsReviewedEndToEndTests` (four more) | **§9.18** a blocking round 1, `resolve`, a PLAIN call over the unmoved head refused, one round on the trail; the fix landed, a plain call runs round 2 (`proceed`, `AdmittedBy = BlockingFinding`). **§9.19** a round-1 failure, `resolve`, every reviewer unticked: an error naming "second round", `ask_human` and the skip's own reason, no `verdict`; two epics on round 2: the same door; one round on the trail, the ground and the count untouched; re-ticked, the retry runs. **§9.20** the review skipped once (the session exists), `ask_human` before round 1; round 1 `good_enough`, `resolve`, a fix; the OLD question answered "continue": `again` refused naming the grounds, no round 2; `ask_human` after round 1 answered: round 2 runs, `AdmittedBy = PersonAsked`. **§9.21** a hold raised, its recorded question cleared as an older build left it, the old notice answered: `GateHeld`, the gate held, a SECOND notice filed under the session and recorded as the hold's question; that answer runs the round and empties the list | `Expected unmoved.TryGetProperty("error") to be True … but found False` (round 2 ran); `Expected unticked.TryGetProperty("verdict") to be False because not a skip: skipped, but found True`; `Expected refused.TryGetProperty("error") to be True because a question asked before round 1 was not asked for round 2 … but found False` (round 2 ran); `Expected answer.TryGetProperty("error") to be True because a time-matched answer to a question the hold does not record must not release it … but found False` |
| `ThePersonAsksForASecondFeatureRoundTests` (reshaped, five more) | **§9.20.** An answer to a question NOT recorded for round 2 is not the request however new it is; an answer to a recorded one is, and spends the list; the newest among the session's own questions is the one read; the clock's fail-safe still refuses an answer older than the round and an unreadable time, and measures against the last round that RAN past a skipped row; `RecordQuestion` puts a question on the hold when the gate is held, on the request list on a feature session with a round run, nowhere before round 1 or on a code session; the list is reset by `FreshFeatureReview` and a person's fresh set | `Expected PersonsRequest.Apply(closed, …) to refer to closed` — it applied the request |
| `TheAnswerBelongsToTheCurrentHoldTests` (reshaped) | **§9.21.** A hold recorded without its questions is released by NO answer (`None`, `For` null); the hold's own notice's answer is the hold's; a skipped row after the hold changes nothing about whose answer it is | `Expected CurrentAnswer.DecisionFor(Held(HoldTwo), …) to be HumanDecision.None … but found HumanDecision.Continue` |
| `TheSecondFeatureRoundTests` (one more) | **§9.22.** Every string-valued method of `FeatureSecondRound` has a non-nullable return, by `NullabilityInfoContext`; the scan asserts it found some | `Expected … ReadState to be NullabilityState.NotNull … but found NullabilityState.Nullable` |
| `ASessionFromAnOlderBuildStillRunsTests` (widened) | A session file without `requestQuestions` reads back empty, never null | a guard, green from the first run |
| `TheRefusalRoadsAreCountedTests` | The census counted `RoundEngine`'s new `Error(…)` site — `SkippedOrOwed` — and the inventory was regenerated with `COAI_RECORD_REFUSAL_SITES=1` | `Expected property recorded.Callers[2].Refusals to be 11 … but found 10` |

**Numbers.** `CoaiMcp.Tests.exe`: 6047 total, 6040 succeeded, 6 skipped, 1 failed — the census above,
before the inventory was regenerated — 7 m 43 s; then the whole suite again over the final tree: 6048
total, 6042 succeeded, 6 skipped, 0 failed, 8 m 15 s. `CoaiServer.Tests.exe`: 334 of 334.
`CoaiBugs.Tests.exe`: 319 total, 317 succeeded, 2 skipped. Extension: `npm run lint` clean; `npm test`
4645 tests, 4642 passed, 2 skipped, 1 failed — the new linear-time guard's own expectation (headers
separated by a SPACE are a footerless header's body, so they are taken; a `;` separates them), fixed and
the whole suite again 4645 tests, 4643 passed, 2 skipped, 0 failed; `npm run test:parity` ok, 423 cases
at each limit byte for byte; `plan-lifecycle.mjs` clean; `pin-check.mjs` OK; `dotnet format
--verify-no-changes` clean.

**What this does NOT prove.** The key shapes are assembled fixtures, not a real vendor's file; no real
key was committed anywhere, so what a real service-account file from a cloud console looks like in git
(BOM, CRLF, a `\r\n` escape) is unmeasured. The re-issued hold notice reaches the panel by the same file
the round's notice does, and the panel was not run. The admitted-round-2 refusal is a sentence, not a
`call_human`: nothing is written to the trail, by design, and the log page will not show that the round
was owed until a reviewer runs it or a person decides.

## An endpoint row's empty model and Vendor keys (2026-10-02, PLAN_model_search_and_busy_marks E1)

| Suite | What it holds | Seen red as |
|---|---|---|
| `endpointModelsCard.test.ts` (one more) | The reviewer card RUN through `panelPageHarness`: an OpenRouter row's empty choice reads *"no model yet — press ≡ and pick one this endpoint lists"*, a plain codex row's *"the CLI's default"* | `actual: "the CLI's default", expected: 'no model yet — press ≡ and pick one this endpoint lists'` |
| `vendorKeys.test.ts` (new, 6 — with a row filed under another key name told THAT name) | The Settings tab's Vendor keys pane, narrowed to that pane: a switched-off OpenRouter row is named as needing a key once switched on; an enabled one keeps its sentence; no endpoint row → *Nothing to fill in yet* and the *not needed yet* placeholder; a switched-off `api` row is named; a `local` row with its engine's address is not asked for a key (the old count asked it) | `a row that cannot run without a key is not "nobody needs one"` — the pane read *Nothing to fill in yet* |

**What this does NOT prove.** Nothing here runs a round: that an endpoint row with no model is refused by the
endpoint is read from `ReviewerRuntime.cs` (no `-m` without a model), not observed in this task.

## A long list's search box (2026-10-02, PLAN_model_search_and_busy_marks E2)

| Suite | What it holds | Seen red as |
|---|---|---|
| `selectSearch.test.ts` (new, 11) | `rankChoices` as a value: prefix > segment start (`/ - . :` space) > contains; list order inside a tier; every word must match, the first decides the tier; case; the label counts; blank query → all, in order; no match → `[]`; regex characters are text; `SEARCH_FROM_OPTIONS` is 15; the function run from its own source text in an empty scope | written before the module existed |
| `selectSearchPage.test.ts` (new, 17 — five after review: no move for an unchanged answer, Escape kept empty across documents, select→box no release, the caret back after Enter, an old note ignored) | The reviewer card RUN through `panelPageHarness`: the 15/14 threshold; typing `opus` leaves `''`, the prefix match, the segment matches, `__other__`; the chosen model kept and still chosen; Enter posts exactly the setting write and focus release a mouse pick posts; no match / blank / disabled card post nothing; Escape restores the drawn order; ArrowDown focuses the select; the hold's id is `search|model|openrouter|…` and box→select is no release; a second page from the saved state re-applies the query and the caret; an orphaned stored query is dropped | `the caret is back in the box` — `FOCUS_ID` dropped every `search|` id; `one search box before the select, found 0` — a fixture keyed to the wrong row |
| `bundledPage.test.ts` (one more) | The minified Settings page embeds `rankChoices` calling nothing the bundle renamed, and the minified text ranks six queries exactly as the source | — |
| `panelPageHarness.test.ts` (eight more) | The widened fake against a DOM's contract: a select refuses an unoffered value; `appendChild` moves; `removeChild` refuses a stranger and re-runs selectedness for the chosen option; `dispatchEvent`; `createElement` only for `input`; no parent outside a running page; prompt pickers apart from settings; the saved state | the teeth check: with the value re-apply deleted the chosen-model test stayed GREEN until `removeChild` modelled selectedness |
| `gateModelPickers.test.ts` (changed) | Picks an option the picker drew (the `other` kind offers only its default) | `the other strongest picker offers at least one model` — it had been assigning a model no picker offered |

**What this does NOT prove.** No real webview runs here: what a native dropdown shows while a box detaches options is
Chromium's, and is not observed. Enter on a prompt picker is not page-tested — no shipped role has 15 prompts.

## The busy mark (2026-10-02, PLAN_model_search_and_busy_marks E3)

| Suite | What it holds | Seen red as |
|---|---|---|
| `renderTracker.test.ts` (new, 12) | Renders numbered and run side by side: a slow render never holds up the next; each told its number; `runAfter` sharing / answering at once; a stalled post-mark render does not hold a write a LATER render carried; a render begun before the mark not counted; a grace period shares the listener's render and starts one when none comes; a failed render rejects a write only when nothing else after its mark runs; `superseded`; a synchronous throw never wedges it | `a slow render never holds up the next one` and a 3 s hang on the synchronous throw — both against the withdrawn serialising `RenderCoalescer`; teeth: five decisions deleted → 1, 2, 1, 1, 1 red |
| `inFlight.test.ts` (new, 9 — with a page that refuses its post not stopping the others) | `InFlight` and `tracked()`: the count and the oldest age; `busy` to every slot at start and end; `settled {seq, doc, ok}` to the poster only; a throwing work settles `ok: false` and is reported; dispose settles all; a double finish never below zero; `settleAfterWrite`'s order; `askOf` | teeth: the settle post, the final announcement, `finish`, `ok = true` → 2, 2, 2, 1 red; either await of the settle order → 1 |
| `busyMarkWiring.test.ts` (new, 5, structural — with the superseded-paint skip and the tracker's grace) | `PanelProvider` cannot be constructed here: each write is followed by its settle in the same turn, a command tracked, `ready` answered, the snapshot in `withCaret`, `dispose` registered | each of three lines deleted from the SOURCE → 1 red |
| `busyMarkPage.test.ts` (new, 12 — with a control's mark kept while a second operation on it runs) | The page RUN on `PageClock`: 499 ms nothing / 500 ms bar + `aria-busy`; settled clears both; settled at 300 ms never shows; commands numbered, focus not; painted age; host-only marks no control; `ready` last and its answer; a replaced page; a new document's seq 1 not cleared by its predecessor's; two writes; the paint key untouched by the snapshot | `a numbered post names the document that sent it` before `doc` existed; teeth: eight lines of the fragment → 6, 1, 2, 3, 2, 2, 1, 4 red, the document check → 1 |
| `panelPageHarness.ts` (widened) | `PageClock` — `setTimeout` runs only when a test advances past it; `hidden`/`removeAttribute`; the busy bar read off the markup; `withoutSeq` and `work(page)` check the number and compare the rest | — |
| changed to compare through `withoutSeq` | `apiSettingsCard`, `consultantSectionScript`, `endpointModelsCard`, `gateModelPickers`, `panelPageHarness`, `thePromptBoxRemembers`, `liveRegionsRebind`, `panelStorageScript` (the last three with fakes of their own) | `+ seq: 1` on every exact message comparison, and `{ type: 'ready' }` in exact posted lists |

**What this does NOT prove.** No real webview runs: the bar's look and the animation are not observed, and VS Code
dropping a message posted while a document loads is reasoned (hence `ready`), not reproduced. The host's tracking is
tested as values over fake slots; `PanelProvider.receive` itself is the standing extension-host gap.

## The busy mark stops while the person answers VS Code (2026-10-02, PLAN_busy_mark_pauses_while_you_type)

| Suite | What it holds | Seen red as |
|---|---|---|
| `personWait.test.ts` (new, 4) | `askPerson` outside an operation is a plain prompt; three awaits down it pauses its own operation before the box shows and resumes it after; a refused box still resumes; two operations side by side each pause only themselves | RED against a stub that ran the prompt plainly → 3 |
| `inFlight.test.ts` (+6, now 15) | A waiting entry leaves the count and its wait leaves its age; two prompts at once pause once; two in turn each skip their wait; a finished operation stays finished; `tracked` posts `waiting` then `working {spentMs: 150}` after ten seconds of typing; a refused prompt resumes before it settles | RED against stubbed `pause`/`resume` → 5 |
| `busyMarkPage.test.ts` (+6, now 18) | Ten seconds in a box show nothing; once answered the bar comes at 500 ms of WORKING time; a bar already up goes while the box is open and returns at once; another document's `waiting` changes nothing; one waiting and one due action on a control keep the mark, and the waiting one alone marks nothing; a late `working` revives nothing | RED before the page knew the messages → 4; teeth: the page ignoring `spentMs` → 2 |
| `promptsWaitForThePerson.test.ts` (new, 6, structural over comment-blanked source) | Every input box, quick pick and file dialog in `src` is `askPerson(() => vscode.…`; fifteen in `panelProvider.ts`; no detached `askPerson`/`notifyAndAsk`; `notifyAndAsk` waits through `askPerson` and is the only wrapper in `notify.ts`; the one `createQuickPick` is the conversation picker; a companion shows the scan an unwrapped prompt, a wrapped one and a comment (plan round, gemini) | RED with 32 sites unwrapped → 2; teeth: one panel prompt unwrapped → 1 |

**What this does NOT prove.** That `AsyncLocalStorage` carries the operation through `vscode.commands.executeCommand`
into a command the panel delegates to (the data-directory moves) is reasoned — the store survives any awaited promise
in one isolate — not run in a real extension host. If it did not, that prompt would only fall back to how 0.61.0
behaved: the bar over the typing.

## ⟳ updates Codex with `codex update` (2026-10-03, PLAN_codex_updates_itself)

| Suite | What it holds | Seen red as |
|---|---|---|
| `vendorUpdate.test.ts` (12, +5 replacing one) | Codex 0.156.1 and 0.126.0 → `codex update`; a pinned path → `"<path>" update`; 0.125.0, an empty version and none at all → npm's `@latest`; `codex-cli 0.156.1`, an error, `0.156`, `v0.156.1` and a prerelease → npm's `@latest`; `updateFor` picks per row from the ⟳ status, a row not yet probed gets the installer, and claude is unchanged; the ⟳ paragraph of the reviewers article names `claude update`, `agy update`, `codex update` and 0.126.0 in all five languages | RED against the old table: 4 (codex installed again; the English help named no self-update). Teeth on the compiled module: no version gate → 2, an unanchored version pattern → 1, codex out of `SELF_UPDATE` → 2 |

**What this does NOT prove.** The one argument `updateFor(this.cliStatus)` in `PanelProvider.updateVendorCli` is
not run: `PanelProvider` cannot be constructed here, and no source-text assertion was added for it (plan round,
gemini). That `codex update` succeeds on Windows npm installs is the vendor's claim plus one Linux run on the Team
server host, not a test here.

## The door to the person (2026-10-02, S3 of `todo/PLAN_question_consultant.md`)

The phase-aware gate in front of `ask_human`, the fifteen-minute wait, the retention of `escalations/`, D13 on
the server, the heartbeat's writing, and the autonomy order's stage sequence. RED first, in two batches: the
tests that compile against the S2 tree and fail at RUN time (7 C# failures naming the old autonomy text, the
old description, `Admitted` where `Refused` was expected, and the schema missing `consultId`; one TS failure,
`Module '"../escalations"' has no exported member 'isOpenEscalation'`), then the tests that name the new types
and fail at COMPILE time (15 distinct CS0246/CS0103 — `AskGateInput`, `AskDecision`, `QuestionMode`,
`QuestionPhaseStore`, `QuestionPhaseKey`, `EscalationRetention`). GREEN on the first full pass but for one real
defect the after-the-release test found: `QuestionPhaseStore.Observe` stamped the release before the directory
existed (`DirectoryNotFoundException … qphase\qphase-….json.tmp`); the directory is created in `Write` now.

| Suite | What it proves | RED observed |
|---|---|---|
| `AskGateTests` (core, pure) | The whole table (phase × mode × productionRisk × proof × availability × readability): the plan stage and the release silent and uncounted in every mode; the first two batches counted and told their number; the third refused under `require` naming `ask_consultants`, the count and `productionRisk`, allowed with the note under `remind`, silent under `off`; a verified `consultId` opens the door and its note travels (the stand-down sentence when the record ended `failed`/`quota_spent`/`none_available`); a rejected one is as if none, with why, in refusal and note alike; `productionRisk` without a reason refused in every mode; with one, asked at once and the consultants beside — or the note saying none ran; the stand-down for every reason; an unreadable record allowing; nothing refused outside Building; 432 inputs never say "critical" | `CS0246: 'AskGateInput' could not be found` (the compile-time RED); **break-it**: `<` → `<=` turned four cases red (`Expected type to be … Refused, but found … Allowed`); the stand-down removed: `Expected type to be … Allowed, but found … Refused`; `off` made to speak: `Expected allowed.Note to be empty … but found "reminder — …"` |
| `QuestionPhaseStoreTests` | One escalation id counts once; two branches of one plan share one record (the branch is not in the key); the caller key is its own record; an unreadable record reads `Readable: false` with the file named and zero batches; a missing one is zero and readable; a key seen released then un-released starts over; the thirty-day sweep by last write | `CS0246: 'QuestionPhaseStore'` |
| `StageReleaseTests` | Every epic closed → released; one open → not, the last open → not; a plan continuing another (5–7) released on its own closes; nothing closed → not; without epics `Done` → released, `CodeReview` → not; a plan declared without an epic follows the stage; no session → not; an unreadable cadence record → not; a `Done` session of one epic with the plan's others open → not | **break-it**: the range check replaced by the last epic alone — `Expected … to be False because epic 2 has not been through its code gate … but found True` |
| `EscalationRetentionTests` | An answered pair goes seven days after the ANSWER, both files, and the question's age is not the clock; an expired question seven days after its expiry; an orphan answer and a stray `.tmp` by their write time; an open question nobody holds goes, a held one is kept; a young open one is kept; a torn file is litter by write time; no directory is nothing | `CS0246: 'EscalationRetention'` |
| `EscalationsTests` (three more) | `Expire` marks the file and keeps it, false for an absent id; `Attach` folds the answers under the question without changing its status; a file written before the status existed reads open with nothing attached | (named the new members; red at compile) |
| `AskHumanServiceTests` (the real service, scripted launcher, real git) | The plan stage: the card written, nothing counted, no note; after proceed: `free batch 1 of 2`, `2 of 2`, the third refused naming `ask_consultants` and no card written; a verified consultId (made by the real `ask_consultants`) opens the door, the record is `person_asked` bound to the card, the card names the consultId, the same id again refused "already"; a fabricated id refused with its id and "not a question this caller"; another caller's, another repository's, a still-consulting and a 31-minute-old record each refused; `remind` allows the third with the note and counts it; `off` says nothing and counts nothing; no row on, and the switch off, stand down with the note; after the release (two closed epics) no note and nothing counted; one open epic counts under the PLAN key, not the caller; an unreadable phase record allows and says so; `productionRisk` without a reason refused and nothing written; a production risk's card exists BEFORE the scripted consultant launches, the answers arrive under it within ten seconds, the record is `production_risk` bound to the card; a 300 ms budget ends `no_answer_yet` with "ask the person directly" and "expired", the file `status: expired` and kept; an answered question is never expired | `CS0246: 'QuestionPhaseStore' could not be found` (the harness names it); then the one real defect above |
| `QuestionAdmissionTests` (D13 on the server) | An unconfined pair without the tick refused `blocked` naming the acknowledgement and "can read this machine"; acknowledged, admitted and still flagged; a default-deny pair the same; a confined pair needs no tick; a pair the runtime cannot do at all is blocked by A3's sentence, not D13's | `Expected type to be … RowAdmission+Refused, but found … RowAdmission+Admitted` (twice) |
| `QuestionFanOutTests` (one more, slow) | The heartbeat is REWRITTEN while a row runs: a 200 ms seam, a row waiting 1.5 s, at least three distinct beats seen in the file while the record says consulting | the seam did not exist: `CS1729` on the eleventh constructor argument |
| `TheOrderTextAgreesWithThePolicyTests` | The order's number is `QuestionPolicy.FreeBatches` and the shipped text carries `{freeBatches}`; the settings default is the constant; `ask_human`'s description's number is the constant; the order names `ask_consultants`, `ask_human`, `consultId`, `productionRisk`, `riskReason`, the release and "refuses" under `require`; under `remind` it reminds and does not threaten; never "critical"; the id is on `ShippedIds` | `CS0246: 'QuestionMode'` |
| `GateCommandsTests` (three more) · `CadenceOrdersTests` (the sweep widened) | The question order follows the autonomy order, only when the mode is not off, and moves nothing before it (split, model, autonomy equal before and after); without the autonomy switch no order; with the mode off the reply is what it always was; the "never critical" sweep now covers the autonomy order and the question order in both modes | `CS0246: 'QuestionConsultFacts'` |
| `AutonomyIsAnInstructionTests` (rewritten for A6) | The pull request opened, CodeRabbit's comments fixed, merged; the deploy verified on the server, no degradation, the logs read; the five steps in that ORDER, as one order; the old (5) wording gone | `Expected TheOrder() "Work AUTONOMOUSLY. …" to contain "CodeRabbit"`; `… to contain "on the server"`; `Expected found.index to be greater than -1 because the order names 'create the pull request', but found -1` |
| `TheGateSaysWhenToConsultTests` (two more) | The phase rule, WHOLE, in both `ask_human` and `ask_consultants`; the old ending "stays open in VS Code either way" gone from the one and "`ask_human` takes them." no longer the end of the other; `ask_human` names `consultId`, `productionRisk`, `riskReason`, "15 minutes by default" and "expired", never "30 minutes" | `Expected askHuman "…(30 minutes by default)…" to contain "`consultId`"`; `… to contain "The phase rule, which the server enforces: …"` |
| `McpContractTests` (one more, real stdio) | `ask_human`'s schema declares `consultId`, `productionRisk` and `riskReason`, none required | `Expected properties.TryGetProperty(argument, out _) to be True because `consultId` is read by the gate … but found False` |
| `AskHumanScenarioTests` (the REAL binary over real stdio, the fake CLI as a codex row) | A seeded session with the plan proceeded: two `ask_human` calls each `no_answer_yet` after their one-second budget with `free batch 1 of 2` / `2 of 2`; the third refused naming `ask_consultants`; `ask_consultants` answers `complete` through a real child; `ask_human` with that `consultId` is `no_answer_yet` saying "expired"; three cards on disk, every one `expired`, the refused question wrote none; the record `person_asked` with the card's id | (named `StdioServer`, extracted from `McpContractTests` — red at compile) |
| `TheOrdersAreWhatTheyWereTests` (re-recorded) | Every combination still byte for byte — against a fixture re-recorded through `COAI_RECORD_GATE_COMMANDS=1`, whose diff touched the two autonomy texts (with and without the split's scope) and nothing else | before the re-recording: `the orders for a1 s0 f0 p1 r1 Epic AsItIs claude must not have changed` |
| `ScenarioCoverageTests` | `ask_human` moved from `NotCovered` to `Covered`; the "every gap carries a reason" assertion rewritten as "no gap with a short reason" because `OnlyContain` refuses the now-empty list | `Expected collection to contain only items matching … but the collection is empty` |
| `ThePersonsCommandsReachTheCallerTests` (three expectations re-derived) | Through a real `review_plan`, the autonomy switch hands back the autonomy order AND the question-consult order under the shipped mode; a reworded autonomy order is followed by the question order; deleting the override restores both; a person's command comes after BOTH built-in orders. The expected context is now derived from the settings the service is handed (`FactsOf`), as the engine derives it, rather than built bare | the full suite's only three failures on the first run: `Expected CommandsOf(answer) to be equal to {"Work AUTONOMOUSLY. …"} … but {…, "ASK THE CONSULTANTS FIRST. …"} contains 1 item(s) too many`; `Expected commands to contain 2 item(s), but found 3` |
| `escalations.test.ts` (three more) | An expired question parses, keeps `status` and `expiredUtc`, and is not open; no status or an empty one is open; the folded answers are carried as written | `error TS2305: Module '"../escalations"' has no exported member 'isOpenEscalation'` and four `TS2339` on the new fields |
| `theInventoryIsComplete.test.ts` (unchanged, and it fired) | `qphase/` was composed under the data directory and the inventory had never heard of it | `+ [ 'qphase', 'src_mcp\\src\\Server\\QuestionConsult\\QuestionPhaseStore.cs' ]` — fixed in `shared/data-inventory.json` and `DATA_TO_MOVE` |

**Two timing assertions, one lesson.** The second full run of the day turned exactly two tests red, both on time
and neither on a shared store: `AProductionRisk_…` asserted that a vendor had LAUNCHED the instant `ask_human`
returned — a promise about the two-second budget, not about the code, since the beside run is detached and does
git and the outline first — and now asserts the guarantee (the card before any launch, the answers under it)
after the wait; and S2's `SixRowsStartWithinOneTick…` missed its 1.8 s bound by 138 ms (`found 1s, 938ms`),
green in the first run and alone — a pre-existing load-sensitive bound, left for its owner to re-tune.

**What it does NOT prove.** The watcher's filter (`escalationWatcher.ts` reading `isOpenEscalation`) is a
one-line wiring in a `vscode`-bound class; the predicate is tested, the wiring is read. A real vendor answering
beside a production risk is the plan's live check (S5). The release inference's limit — the lowest closed
epic standing in for the plan's first — is stated, not measured against a plan whose first epic was skipped.

## The question consultant as the person sees it — S4's suites (2026-10-02)

The extension half of `todo/PLAN_question_consultant.md` S4, and the four server follow-ups that rode with it.
Every page behaviour is RUN (`.agents/PROJECT.md`): the panel harness over the page the panel renders, or the
modules bundled and minified as they ship. The section was written before its tests, so each was given teeth by
deleting the production line it guards, COMPILED (tsc exit 0 checked), and watched go red — a mutation that did
not compile reuses the last output and proves nothing, which is how one of them first passed for the wrong
reason.

| Suite | What it asserts | RED / teeth observed |
|---|---|---|
| `qconsultSettings.test.ts` | Every default read out of `QuestionConsultSettings.cs` / `QuestionRow.cs`; the three modes; the server's key list derived from `QuestionConsultKeys` and each key changed alone reaching the file under it and nothing else; a pristine panel writes none; a server older than `QCONSULT_SINCE` is sent none and named, an unknown or current one is not; the reader as the server reads; every key declared with its default; the keys overlaid per side | Fields added, env not wired: `changing qconsult produced an identical env block — the setting would never reach the server` and eight `+ [] - […]` |
| `qconsultSection.test.ts` (panel harness) | A seventh row's switch disabled while six are on, freed when one is off; the switch posts its row-routed write; a row without a prompt cannot be switched on and is offered no empty choice; every refused vector pair is a DISABLED option with the table's reason and every admitted one is not (A3, from `shared/capability-matrix-vectors.json`); a codex row disabled until its tick, the tick posting `acknowledged: true`; a confined pair asks for none; Restore default posts its command and the box then shows the shipped words read from the seed; the banner for an older server only; the buttons; the picker offering the api presets and not a retired runtime | Six mutations, each compiled: the switch never disabled, the cap removed (`>= MAX + 100`), the acknowledgement gate removed, a refused prompt not disabled, the override not shown, no banner — each red |
| `qconsultWrite.test.ts` (pure) | The shipped prompts ARE the seed; the seventh row refused on and switching off always allowed; never stored without a prompt or with agy + web / api + disk; the tick written and taking it away switching the row off (D13); a vendor or prompt change dropping the acknowledgement; a new row off with an admitted prompt and a free id; an emptied or unchanged shipped box removing the override; new prompts refused by name; a prompt a row runs not removable; D14 (c)'s refusals and two spellings of one root | The data-folder check removed; the untick keeping the row on — each red |
| `panelPageHarness.test.ts` (one more) | The fake reads an option's `disabled` from the boolean attribute and its `title` — and an ordinary option as neither | — (the harness's own contract) |
| `activeQuestions.test.ts` (panel harness) | One line per model and a live push advancing the settled one; the card with every answer folded, the flag on a flagged row's answer, the production risk and its reason, Answer; a card that followed a consultation folding THAT consultation and naming it, the consultation not drawn again, one card per question; an expired card leaving on the push; model text escaped; nothing active drawing nothing; the record reader's keep window | Four mutations, compiled: a bound record drawn twice, advice unescaped, the flag never shown, followed answers not folded — each red |
| `answerChoices.test.ts` | A question with no findings offers the free-text box first and the decisions after; a gate verdict keeps exactly its three | RED first: `'continue' !== ''` |
| `debounced.test.ts` (a stopped clock) | Five events in 100 ms → one refresh at 175 ms; an event after it opens the next; a cancel fires nothing; the window inside 150–200 ms; the poll for UNC and `\\wsl.localhost` only | The coalescing guard removed → red |
| `bundledPage.test.ts` (three more, bundled) | The Questions tab from a `parseLog` fixture parsed into rows and cells — one per question, per-model rows, cost and time, the outcome, the flag; an older server an empty tab; the shipped page opening the tab on a press and a push replacing its body | Four mutations: the push handler, the section's tab name, the parser's field, the flag — each red |
| `liveRegionsRebind`, `panelView`, `theQuestionCardReadsAsAList` (moved) | The same guarantees, addressed to `live-qconsults` — the region that replaced `live-questions` | — |
| `QuestionConsultProjectionTests` (four more, C#) | `--log` lists each question once with its rows and its outcome; advice cut at 4 000; a database without the tables reads as none and the rest answers; the wire key `questionConsults` | `Expected … to contain a single item … but the collection is empty`; the no-tables guard disarmed → `SQLite Error 1: 'no such table: question_consults'` |
| `AskHumanServiceTests` (one more, C#) | A production-risk question past 4 KB still runs the consultants beside the card, the person asked the whole question, the record's question cut and ending `[…truncated]`, `truncated` saying what was cut | `Expected card not to be <null> because the consultants ran beside the card although the question was past 4 KB` |
| `QuestionFanOutTests.SixRowsStartWithinOneTick…` (rewritten, C#) | All six launches STARTED before any was let through — a barrier, not a wall clock | `LaunchAllAsync` made sequential: `Expected barrier.StartedBeforeTheFirstFinished to be 6 … but found 1` (22 s) |

**What it does NOT prove.** The watchers' wiring of the debounce and the poll is a few lines in `vscode`-bound
classes, read and not run; the rule beneath it is a test. A person pressing a control inside a real editor's
webview is not driven — the host scenario stops at what a host can observe. A real vendor answering a row is the
plan's live check (S5). The three clipboard scenarios of `npm run test:host` fail on this machine (something else
writes to the clipboard during the run) as they did before S4.

## The code review's fixes — S4b's suites (2026-10-02)

`todo/PLAN_question_consultant.md` S4b, items 1–15. Each fix's test was watched RED against the code without the fix
— for the items a previous session had already fixed, by reverting the production file to the branch's head (C#)
or compiling a mutation that removes the fix (TS), and restoring it byte for byte (files re-touched so the next
build saw them).

| Item | Suite · test | RED observed |
|---|---|---|
| 1 | `QuestionFanOutTests.AQuestionCarryingASecret_IsRefusedOnEveryRow…`, `AskHumanServiceTests.AProductionRiskQuestionCarryingASecret…` | `Expected row.Status to be "refused" … but "answered"` |
| 2 | `QuestionConsultSettingsTests` (contains a place, credential directories ×7, outside the profile, through a junction/symlink); `qconsultWrite.test.ts` (three, and the credential list read out of the C#) | `verdict.Accepted … found …\to-profile`, `…\home\me\.ssh`; three TS tests red with the checks mutated off |
| 3 | `QuestionFanOutTests.ARowsAdviceAndReason_AreRedacted_BeforeTheRecord` | `Did not expect leaky.Advice … to contain "sk-live-…"` |
| 4 | `WebQuestionSanitiserTests` (schemes, citable links, invisible characters, compatibility spellings, the thirteen classes), `SecretCheckTests` (four hidden secrets), `TextAsReadTests` | `Expected type … Refused, but found … Clean` per case; one SecretCheck case had no teeth (the split fell after a key long enough to match raw) and was moved earlier |
| 5 | `QuestionFanOutTests.ADiskRootThatIsNoGitCheckout…`, `QuestionConsultServiceTests.ADiskRowOverARoot…`, `AskHumanServiceTests.ARiskCardsDiskAnswer…`, `activeQuestions.test.ts`, `bundledPage.test.ts` | `Expected disk.Note "" to contain "root … is not a git checkout…"`; the two TS draws red when mutated off |
| 6 | `settingsShape.test.ts` — the default read out of `PanelSettings.cs` | `30 !== 15` |
| 7 | `EscalationRetentionTests.AHeldQuestion_IsKept_WhateverItsStatusOrAge…` | `Expected Retention().Sweep(Now) to be 0 … but found 4` |
| 8 | `AskHumanServiceTests.TwoAskHumanCallsInFlightWithOneConsultId_OnlyOneGoesThrough`; `…WhoseCardCouldNotBePosted_IsGivenBack…` | `Expected … to be 1 … but found 2` (both went through); break-it: without the give-back the record still names the lost card |
| 9 | `QuestionConsultStoreTests.AConsultingRecordPastTwiceItsDeadline…`; `activeQuestions.test.ts` (heartbeat older than two minutes) | `Expected _store.Sweep(…) to be 1 … but found 0`; TS red with `shownAt` mutated to identity |
| 10 | `jsonDirectory.test.ts` — a fake file system whose first listing resolves after the second | `a superseded refresh tells nobody` with the guard mutated off |
| 11 | `QuestionFanOutTests.WithNoApiNoneRow_TheOutlineIsNeverBuilt`, `TheOtherRowsLaunch_WhileTheApiRowsOutlineIsStillBuilding`, `TheOutline_IsBuiltOncePerRepositoryAndHead…`; `QuestionOutlineCacheTests` | `Expected held.Built to be 0 … but found 1`; `Expected sonnetLaunched to be True … but found False` (15 s) |
| 12, 13, 14 | `jsonDirectory.test.ts` (the snapshot's keep/drop rules, `unreadable: 'none'`), `RecordFilesTests`; the stores' existing suites unchanged and green | refactors: no behaviour to watch fail; the existing suites are the guard |
| 15 | `QuestionPromptSetTests` (`con`, `nul`); `qconsultWrite.test.ts` (a title making `con`) | `Expected custom.Prompts to be empty, but found at least one item`; TS red before the device-name check |

**What it does NOT prove.** The junction test makes real links (`mklink /J` on Windows, a symlink elsewhere) but no
link on a network share. The watcher's recheck timer and file events are `vscode`-bound and read, not run; the
snapshot beneath them is the test. The atomic spend is proved for two calls of ONE process; two server processes
share it through the turn, which `SessionTurn`'s own measurement covers, not this suite.

## The busy mark on the other webviews (2026-10-03, PLAN_busy_marks_on_every_webview)

| Suite | What it holds | Seen red as |
|---|---|---|
| `busyHost.test.ts` (new, 6) | A numbered post is announced, run, settled to its page and the count falls; an unnumbered one runs and marks nothing; a throw settles `ok: false` and is said; `ready` is answered with the count and the age, and nothing else is consumed; a prompt pauses it; dispose settles all | RED against a stub that only ran the work → 5. Teeth: `tracked` not called → 5, the `ready` answer removed → 1 |
| `roundsLogBusyMark.test.ts` (new, 3) | The rounds log RUN on `PageClock`: a pressed command is numbered, nothing at 499 ms, the bar and the button's `aria-busy` at 500; settled clears both; `ready` is the last post, and the host's count draws after what is LEFT of the delay | RED: the page had no bar. Teeth: the command posted through `vscode.postMessage` → 1 |
| `roundsLogWork.test.ts` (new, 2) | Each of the eight commands maps to its own hook, and the work returned is what is awaited; `ready` and `ignore` ask for none | RED against a stub returning nothing → 1. Teeth: the same → 1 |
| `roundsLogPagingHarness.ts` (new, extracted) | The runner from `roundsLogPaging.test.ts`, moved unchanged apart from an optional `PageClock` and a busy bar that keeps its attributes — six private rounds-log runners already existed, and this one is now shared | — |
| changed for the numbered posts | `roundsLogPaging`, `closeAConsultation` and `forgetAChatRow` compare the posts through `withoutSeq`, which checks the number first. The stub elements in `bundledPage`, `closeAConsultation`, `consultationFold`, `forgetAChatRow` and `theLogLosesItsFirstPush` gained `removeAttribute`, which a real element has and the bar calls on load | — |
| `bugsKeysPage.test.ts` (+3) | A server action is numbered: nothing at 499 ms, the bar at 500, gone on `settled`; `copy` and `dismiss` are not numbered; a page painted mid-action draws after what is LEFT of the delay and posts `ready` last | RED: no bar. Teeth: `post()` bypassing `send` → 1, `copy`/`dismiss` numbered → 1, the painted snapshot ignored → 1 |
| `bugzReviewPage.test.ts` (+5) | A decision is numbered and the bar follows the clock until settled; `choose`, `openAt` and `calls` are numbered; a typed comment and a real-method read are never numbered; a repaint mid-decision draws after what is left | RED: no bar, `choose` unnumbered. Teeth: decide bypassing `send` → 3, `choose` untracked → 2, a comment through `send` → 3, a real-method read through `send` → 1, the painted snapshot ignored → 1 |
| runners widened for E2 | The key tab's runner and the bugz review's take an optional `PageClock` and answer the bar's id with the panel harness's `busyBarOf` — exported and now shared by every page runner that meets the bar (the rounds-log harness dropped its own copy). Six bugz tests compare numbered presses through `withoutSeq`; `bugzReviewWiring.test.ts`'s host patterns follow `slow(raw, …)` and `void this.queue(…)` | — |
| `settledWrites.test.ts` (+4) | A structural command answers after its apply AND its redraw; a failure still answers, and is reported; a typed field answers when the write carrying it lands, keystrokes it replaced included; a structural command drains the typing first and answers after its own write | RED against a stub answering at once → 4. Teeth: `run()` answering at once → 3, a replaced keystroke dropped → 1 |
| `rolesBusyMark.test.ts` (new, 4) | Adding a role is numbered and the bar follows the clock until settled; switching a role on is numbered; typing is not; a repaint mid-change draws after what is left and posts `ready` last | RED: no bar, the switch unnumbered. Teeth: a pick unnumbered → 1, typing numbered → 1, add unnumbered → 1 |
| `commandsBusyMark.test.ts` (new, 4) | Switching a command on is numbered and the bar follows the clock; add and remove are numbered; typing a text or a title is not; a repaint mid-change draws after what is left | RED: no bar. Teeth: `post()` bypassing `send` → 2, typing numbered → 1 |
| `rolesPageHarness.ts` (widened) | `runPageHtml(html, inDocument, clock?)`: given a clock, the page's timers run on it and its bar is answered (the shared `busyBarOf`); `presses(page)` gives what the person's presses said, without the load-time `ready` and the numbers. `rolesPageScript`, `featureStageOnRolesPage` and `commandsPage` read through it | — |
| `rolesFieldOf.test.ts` (new, 3) | Typing in a role or prompt field settles under its own key; a `stage` pick and a switch are not settled | RED: a stage pick settled as `R/stage` |

## D13 revised — a flagged row needs no tick (2026-10-03, the operator, before the 0.41.0 / 0.62.0 release)

The operator dropped the acknowledgement: a Codex row (and agy on disk) is shown as able to read this machine, with a
tick that is ON and cannot be taken off, because Codex has no setting that limits what it reads. The field was never
released, so it is removed rather than kept; a row written with it by the pre-release build is read past.

| Suite | What it proves | RED observed |
|---|---|---|
| `QuestionAdmissionTests` (rewritten) | codex × web / disk / none ADMITTED with no tick and still `Unconfined`; agy on disk admitted and `DefaultDeny`; a confined pair unflagged; agy + web still blocked by A3's sentence, which names no tick | 4 of 6 failed against the S3 rule: `Expected type to be RowAdmission+Admitted … but found RowAdmission+Refused` (three codex prompts, agy disk) |
| `QuestionRowsTests` | the operator's three rows, one of them carrying `"acknowledged": true`, parse with no complaint | — (kept green through the field's removal) |
| `qconsultWrite.test.ts` | codex × each prompt and agy × disk switch ON with nothing to tick; a vendor or prompt change still switches the row off | `codex × question-web refused to switch on` |
| `qconsultSection.test.ts` (panel harness) | a codex row's switch is enabled; its tick is drawn `checked disabled`, carries no `data-setting`, and the reason "Codex has no setting that limits what it reads" is beside it | `the row shows no tick for what it can read` |

**What it does NOT prove.** That a disabled checkbox cannot be changed is the webview's DOM, not this harness's; the
tick posts nothing because it carries no `data-setting`, which the harness does observe.

### `ask_human` cards only the gate's question (2026-10-03, `research/PLAN_ask_human_is_for_the_gate.md`, epics 1 and 2)

The operator's symptom: an AI's own A-or-B question became a card under *A review is waiting on you*, answered with
the gate's three buttons, and `status` then reported the press as `humanDecision: "fix"`. RED was observed against
`origin/main` 4dcc9a55 with the production change stashed — 19 tests failed, each for the real symptom:

| test | what it pins | observed RED |
|---|---|---|
| `AskHumanServiceTests.AnAiQuestionOutsideTheGate_WritesNoCard_AndIsAskedInTheConversation` (+ every phase/mode/proof row moved onto the conversation path) | outside a held gate: no card, no wait, `ask_in_conversation`, the instruction naming `document`/`feature` | `Expected … "ask_in_conversation" … but "no_answer_yet"` — a card was written and waited for |
| `AskHumanServiceTests.AGateQuestionPastTheFreeBatches_IsNotSentToTheConsultants` | G7: a held gate's question is carded, not counted, never refused | the reply was an `error` naming `ask_consultants` (no `status` property) |
| `AskHumanServiceTests.AProductionRiskOutsideTheGate_AsksTheConsultantsFirst_AndReturnsTheirAnswers`, `…CarryingASecret_ReachesNoConsultant`, `…WithTheConsultantOff_SaysNobodyCouldBeAsked` | G3: consultants FIRST, fenced answers in the reply, outcome `production_risk_consulted_first`; a secret still reaches nobody | `no_answer_yet` — the card at once, the consultants detached |
| `TheStatusReportsOnlyTheGatesAnswerTests.StatusDoesNotReportAnAnswerToAQuestionTheGateNeverAsked` (the REAL `PanelService.StatusAsync`) | G5: only the hold's or the request's answer is reported | `Expected … humanDecision … to be empty … but found "fix"` |
| `McpContractTests.AskHuman_ForTheAisOwnQuestion_AnswersAtOnceToAskInTheConversation` (real stdio) | the conversation reply arrives without waiting | `TimeoutException` after 20 s — the old server waited its 60 s budget |
| `AskHumanScenarioTests` (real stdio), `ADocumentIsReviewedEndToEndTests.AskHumanWithNoDocument_…`, `AFeatureIsReviewedEndToEndTests.AQuestionAskedBeforeRoundOne_…` | the conversation path end to end; a held document review asked without `document` is told to name it | `no_answer_yet` where `ask_in_conversation` was expected |

Added GREEN with the change (new behaviour, no RED to observe): `AQuestionIsTheGatesTests` (the `AsksForTheGate` table,
`RecordQuestion` recording exactly where it is true, the door table of `AskGate`), `ATypedAnswerToAHoldQuestion_ReachesTheAsker_AndReleasesNothing`,
the `kind` assertions on a question card and on a round's notice, `TheHoldsOwnAnswer_IsStillReported`.

Moved onto a HELD session because the card path they describe still exists only there: the give-back on a failed post,
the production risk's card with answers folded under it, the 4 KB cut, the secret refusal under the card, the disk-root
note, the wait and its expiry. `TwoAskHumanCallsInFlightWithOneConsultId_OnlyOneGoesThrough` now stages the race on the
conversation path — a held gate's question needs no proof since G7, and a production risk needs none either, so the
race runs on the AI's own question, where exactly one of two calls gets through whichever interleaving happens. `ThePersonAsksForASecondFeatureRoundTests` lost "recorded whatever the ground"
to `AQuestionAskedWhenAGroundIsAlreadySet_IsNotRecorded_…` (the narrowing of G1). `HumanDecisionTests` and three
`EscalationsTests` moved off the removed session-wide readers onto `Escalations.DecisionOf` / `ReadAnswer` / `Read`.

Added after the gate's cadence consultation and the three own reviewers (folded into the epic's commit):
`AWordOnlyAnswerGivenAfterADecision_DoesNotHideTheDecision` (RED: `Expected … HumanDecision.Continue … but found
HumanDecision.None` — `CurrentAnswer` now prefers a decided answer), `ACancelledProductionRiskOutsideTheGate_SpendsNothing_AndCountsNothing`
(teeth checked: with the give-back removed, `Expected … EscalationId to be empty … but found "aa34751d3a3c"`),
`AskGateTests.AProductionRiskOnAHeldGatesCard_…`, `…OnTheAisOwnQuestion_…`, `…WithAnUnreadablePhaseRecord_StillRunsItsConsultants_OnEitherDoor`,
the wording scan over both doors (864 decisions), the secret case asserting "none answered", and
`TheStatusReportsOnlyTheGatesAnswerTests` answering the foreign card LAST (so the old newest-card read would fail) plus
`OnAFeatureReview_TheRequestsAnswerIsTheGates`.

**Epic 2 (the extension).** `answerChoices.test.ts`: a `question` card offers the box first — with findings gating, and on a feature review with
that stage's decisions — and its box says words decide nothing for the review (RED before the kind was read: `'continue' !== ''`, the gate's buttons first);
a `notice` offers the three decisions only, even with no findings; an empty kind and an UNKNOWN kind (a newer server) fall back to the
findings rule, never to the decisions alone. `TheLogNamesEveryQuestionOutcomeTests` (C#) enumerates `QuestionOutcomes` by reflection
against `qconsultLog.ts`'s `OUTCOMES` keys (comments stripped, quoted or digit-bearing keys read) — RED with the two new outcomes
unlabelled — with a companion asserting the scan still finds `person_asked`.

**What it does NOT prove.** That an AI actually asks in its chat — `ask_in_conversation` is an instruction, and only a
live session shows it being followed. The two-in-flight race is not forced — the atomic spend is the guard, pinned by the
store's own tests. Cancellation is driven at the service; that the MCP SDK hands the lambda a live token is the SDK's
contract, not observed here.

## An api row's stream, through the person's flow (2026-10-06, Story D of `research/PLAN_api_streaming.md`)

The flow: switch "Stream" on for an api row on the new Settings page's model card → save → press ✓ Check → the badge
says "checked: it answered · streamed". Two halves, joined by the check's durable record:

- **The page half** — `anApiRowCanStream.test.ts` (extension): the switch is drawn only on an api row's card and only on
  the new page, writes the row's `stream`, crosses `COAI_VENDORS` only to a binary whose `--features` lists `apiStream`;
  and the card's badge and the landed-check notice read the record's `streamed` (`streamed` → ok, `not-streamed` →
  warn, absent → nothing about streams).
- **The real child** — `AStreamedRowIsCheckedTests` (C#): the row as the card hands it, on stdin to `--check-model`,
  launches the REAL `coai-mcp --ask-api --stream on` (`ServerBinary.Path`) against `ApiEndpointStub` serving SSE, with
  the key from a `VaultKeys` of the test's own. Three cases: streamed → `"streamed":"streamed"` and the vendor's request
  carried `"stream":true`; switched on but answered with one JSON → `not-streamed`; switched off → no verdict and
  `"stream":false`. Teeth: with the shim's `streamed` flag removed, the first case goes red on the missing verdict.
- **The shim alone** — `AStreamedAskTests`: the stream's outcomes (usage first; an error inside a 200; a cut stream;
  the JSON fallback) and `ConsultantCheck.StreamVerdict` as a value. The seam leg `apiStreamSeam` (`npm run test:seam`)
  holds the wire between the two halves.
- **One reader for both** (2026-10-07) — `AStreamedGoldenIsReadLikeItsRecordingTests`: every recorded golden in
  `fixtures/api-goldens` read through each module that answers in its shape (xai, qwen, deepseek, glm, openai), once
  whole and once re-told as a SYNTHETIC stream; same exit, same usage line plus `"streamed":true`, same answer file.
- **Never 0** (2026-10-07) — `AStreamsUsageIsNeverZeroTests`: a cut stream, an error inside the stream (with and
  without a usage), the answer cap and a mid-stream deadline (served by a `StubHandler` over the shared `Trickle` body),
  each read by `ApiRuntime.ReadUsage` on a PRICED row: counted or unknown, never `Usage.None`. Both classes cover code
  that had shipped, so their teeth were proven by breaking it (research/RESULTS_api_streaming_measurements_2026-10-06.md §5).

```bash
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*AStreamedRowIsCheckedTests"   # build src + tests first
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*AStreamedAskTests"
cd src_vs_code && npm run compile && node --test out/test/anApiRowCanStream.test.js
```

**What it does NOT prove.** That a real vendor streams as the stub does — that is the plan's live call (build step 5),
kept as a fixture when it has run. That a webview click reaches the check: the page and the child meet at the durable
record, and each half is tested against it, not driven through one editor.

## A row's fast mode, through the person's flow (2026-10-07, Story D of `research/PLAN_fast_mode.md`)

The flow: set "Fast mode" on a row's card on the new Settings page → save → every launch of that row carries the tier.
Two halves, joined by the settings file:

- **The page half** — `aRowHasAFastModeSwitch.test.ts` (extension): the select is drawn only on a card whose row has a
  tier, Off by default; a change writes the row's `fast`; it crosses `COAI_VENDORS` only to a binary listing `fastMode`;
  the skew note for a chosen state; the current page draws no control. `fastModeIsData.test.ts`: which runtime and model
  has a tier, from the generated rows of `shared/feature-availability.json`.
- **The real child** — `AFastModeRowIsCheckedTests` (C#): a codex row handed to `--check-model` as the card hands it,
  the fake CLI recording the argv it was really started with: On → `-c service_tier=fast`, never set → `default`, As the
  CLI is set → no tier. Teeth: with the consultant's tier dropped, two of the three go red.
- **The wire** — the seam leg reads `--providers`' `fast` back from the real binary (`on`, or `off` when held back).
- **Two implementations, one answer** — which rows have a tier is decided twice, by the extension's `rowHasFastTier` and
  the server's `RowHasFastTier`. A second seam leg (`fastTierSeam`) asks both the same eleven rows (alias, `[1m]`, a
  case difference, Sonnet, an empty model, codex with and without another endpoint, runtimes with no tier) and fails on
  any row where they differ. Teeth: with the extension's lower-casing removed, it names `claude-case`.
- **The chat** — `aChatRunsOnItsRowsFastMode.test.ts`: the tier per runtime and state, the file in a private folder of
  its own, and a settings path with a space reaching claude whole through `cmd.exe`. Teeth: both new tests went red
  with the real symptom (a shared path; a path split in two) under a mutation that compiled.
- **Per launch** — `AFastModeReachesEveryCodexLaunchTests`, `AFastModeReachesEveryClaudeLaunchTests`,
  `ARowsFastModeReachesItsLaunchesTests`, `FastModeIsDataTests`.

**What it does NOT prove.** That the vendor actually serves the fast tier: codex reports no tier in its output, and
claude needs usage credits on this account (research/RESULTS_fast_mode_measured_2026-10-07.md).

### The codex floor, through every launch path (2026-10-07, `research/PLAN_codex_tier_floor.md`)

The flow: a row left at Off (the default) on a codex 0.110–0.130 — which refuse `service_tier=default` at config load
(research/RESULTS_codex_service_tier_versions_2026-10-07.md) — is launched with no tier on every path, and a release
outside that range is told `default` exactly as before. Each path is driven against the fake CLI answering
`codex-cli 0.120.0` and `0.160.0` to `--version`, the real argv it was started with recorded:

| Path | Scenario test | Covered |
|---|---|---|
| Review rounds — plan, code (with its security lane), document, feature | `EveryReviewRoundAsksTheInstalledCodexTests` | yes |
| A consultation's first and resumed turn | `ACodexConsultationAsksTheInstalledCodexTests` | yes |
| The consultant check (`--check-consultant` / `--check-model`) | `ConsultantCheckTests.ACheckOfACodexConsultant_TellsItOnlyWhatItsReleaseTakes` | yes |
| **A codex question row** (`QuestionRowLaunch.TierProbedAsync`) | `QuestionConsultScenarioTests.ACodexQuestionRow_IsToldOnlyWhatItsReleaseTakes` | yes |
| `--providers`' note for a row whose release cannot be told the standard tier | `ACodexThatRefusesTheStandardTierTests` | yes |
| The probe itself — the range, two releases in one answer, a CLI that does not start, exits non-zero or hangs (killed) | `CodexTierSupportTests` | yes |
| The refusal met anyway, named with its cure | `ConsultFailureTests.ACodexThatRefusesTheStandardTier_IsNamedWithItsCure` | yes |

```bash
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*EveryReviewRoundAsksTheInstalledCodexTests" --filter-class "*ACodexConsultationAsksTheInstalledCodexTests" --filter-class "*ACodexThatRefusesTheStandardTierTests" --filter-class "*CodexTierSupportTests"
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-method "*ACheckOfACodexConsultant_TellsItOnlyWhatItsReleaseTakes*" --filter-method "*ACodexQuestionRow_IsToldOnlyWhatItsReleaseTakes*"
```

**What it does NOT prove.** A real codex 0.110–0.130 completing a review: none can use this account's model with a
ChatGPT login (the measurement's every run ended in a 400 after config load), so the fake CLI stands in for the release.

## An older build's edit after the move is a revision (2026-10-07, R7 of `research/PLAN_one_model_catalog.md` epic 5)

The flow: a chat preset moves into the catalog → an older build edits the preset → this build reads it → Chat (the
new Settings page) shows the row as it is and the edited values → the person presses *Use the edited values* or *Keep
the row* → the choice is in the record and holds across a reload. Two files, pure parts and the page RUN:

- **The move and the conflict** — `anOlderBuildsEditIsARevision.test.ts`: the field list DERIVED — every row field a
  preset's own fields reach, observed by moving variants — equals `COPIED_FIELDS`; a new entry carries the snapshot; an
  edit of EACH copied field (vaultKeyName aside: it is the preset id, the match key) makes no second row, leaves the row
  untouched and raises a conflict naming that field; each choice (the row written before the record for *use*), the
  conflict gone after the file is read back as JSON, and a later different edit raised again; a 5-field entry
  snapshotted from its row (an on-disk edit raised, an unedited preset not, the run writing it with no new row, and an
  owed remap written in the same run); a deleted row or preset raising nothing and throwing nothing; epic 4's two-entry
  record read NEWEST by the move, the conflict and `movedTo`; the chat never listing the edited preset beside its row;
  a revision `unchanged` in the migration and the restore still clearing the record.
- **The page and the host** — `aRevisionShowsOnChat.test.ts`: the message read like its neighbours (junk → `ignore`);
  the assembled Settings page drawn from a settings file through `chatSettingsFrom`, run against the DOM shim, the
  block drawn with both values and both buttons; each button posting exactly its own numbered `chatPresets` message and
  nothing else; that message carried through `presetEdit` → `revisionWrites` → the file → a reload, the block gone.
- Changed with it: `chatPresetMove.test.ts` (a same-id edit is no longer a second row — the old "positional id that
  shifted" test), `catalogChatStep.test.ts` (epic 4's two-entry record written by hand, since no build writes one now).
- **Teeth** (each mutation compiled, went red, was restored): a field added to `COPIED_FIELDS` that the move does not
  copy; a choice that writes without a conflict; the record dropped from `RESTORE_ORDER`; a snapshot reported as
  changed on every run; every preset reported as edited.

- **The code round's findings** (2026-10-07, all red first): `anOlderBuildsEditIsARevision.test.ts` counts the record's
  index reads through a Proxy — one `entryOf` lookup reads ≤ 2 entries (red: 1000, the record copied), a 300-preset
  conflict pass and a 300-entry snapshot pass each ≤ 3000 (red: 90600 / 90601); a source scan that no reader reverses
  the record and `recordedEntryOf` calls `entryOf`; `chatModelName` written (between the row and the record) only when
  the chat opens on that row and its model changed; `applyRevisionChoice` over in-memory ports answering "redraw" for a
  settled conflict and stopping at a refused row write; `chatSettingsFrom` taking the move's presets, so a workspace
  value raises nothing, and `userChatPresets` reading the user layer. `catalogChatStep.test.ts`: a side inheriting the
  chat model finds its own row by id though the fingerprints differ. `aRevisionShowsOnChat.test.ts`: a press disables
  both buttons of the block; the page's message is carried out by `applyRevisionChoice` itself; source pins on the
  host's ports, the panel's and the migration's reader. Fixtures read back from JSON through a type guard
  (`test/settingsFileFixture.ts`), never a cast.
- **In a real editor** — `test/host/revisionScenario.ts`, the last of `npm run test:host`'s scenarios: the SHIPPED
  extension's migration moves a preset (snapshot written) and, after an older build's edit, makes no second row; a
  workspace-layer value of the presets raises nothing; *Keep the row* and then — after a later, different edit on the
  row the chat opens on — *Use the edited values*, each the page's message read by `presetEdit` and carried out by
  `applyRevisionChoice` with the window's real reads (`userChatPresets`, `chatRead`) and Global `update`s; the record,
  the row and `chatModelName` read back from the real settings; the migration the row write triggers still makes no
  second row and raises nothing.

**What it does NOT prove.** The webview click in a real editor (a host cannot reach a webview's DOM — the press is the
DOM-shim test's); `chatPresetsHost.revisionPorts` itself — it needs the bundled extension's own `ExtensionContext`, so
the scenario hands the same reads and a Global `update` (what `saveSetting` does on a window without per-side
settings) and its catalog turn is the compiled copy's, not the bundle's (it waits for the bundle's migration to go quiet
instead); a per-side overlay write; the refusal notice's wording; and a real older build writing the file.

## The switch-over, steps 1–3 (2026-10-08, E5.1 of `research/PLAN_one_model_catalog.md`)

The flows and the tests holding them, as shipped (steps 4–5 stopped — `research/module_extension.md` says why):

- **The jumps** — `theNewPageJumpsToItsPlaces.test.ts`, the page RUN on the DOM shim (`pageScriptHarness`), every node
  pressed taken from the drawn tree by its label: on the new page the gate's "Edit commands…" opens Reviews › Commands
  and the stages' "Edit roles…" opens Roles & prompts, the pane shown, the old one hidden, the host told
  `{ type: 'tab', id }` and nothing else (red: "Commands was not opened" / "Roles & prompts was not opened"); on the
  current page, through `runPanel` over its own html, each still posts its page command (red, after step 1 had made
  them jumps on both pages: 'the current page draws no "Edit commands…" that posts a command').
- **The Bugz picker** — `bugzPicksFromTheCatalog.test.ts`: the sidebar drawn (`panelHtml`) and read as a tree — only the
  rows ticked Bugz offered, as `row/model` (red: every engine model offered); nothing ticked and no pick says so and
  names Models (red: three engine models offered); a never-ticked install's pick of a row and one of its engine's
  models offered and selected; a pick of an unticked row drawn selected + disabled with the stranded sentence (red: "a
  stranded pick cannot be chosen again"), the same for a removed row (red: nothing selected); `bugzCollectRefusal`
  over a stranded pick, no pick, a holding pick and the never-ticked pick (red with the module stubbed: '' for all);
  T7, `envBlock` writes no `COAI_BUGZ_MODEL` (red: `'local/qwen3.5' !== undefined`). Changed with it:
  `bugzSection.test.ts` (its state ticks a local row Bugz), `settingsReach.test.ts` (the walk skips `bugzModel`, with
  why), `catalogMigration.test.ts` (no env key for the moved model), `foundByTheGate.test.ts` (the model-list repaint is
  asserted with a local row on the page, since the sidebar no longer lists every engine model), `notificationSites`
  (161 → 162, `no-ranking-model`).
- **The harness** — `panelPages.ts`/`panelPageHarness.ts` draw the new page for the Settings surface; 21 test files were
  repointed or pinned (the commit of step 3 lists which read what). `paneIn` and `sectionHtml` throw for a place no page
  draws, and find only start tags.

**The code round's findings** (E5.1a, all red first):
- `bugzCollect.test.ts` — `collectWithPick` RUN with in-memory ports: a stranded pick and no pick are refused by their
  sentence and start nothing (red with it stubbed to start: "a collect started with a pick nobody holds", "a collect
  started with no model"); a holding pick starts `--collect-bugs --model … --runtime local`; a ticked row the ranking
  allowlist refuses is refused (red: "a collect started on a row the ranking allowlist refuses"); ONE state built as the
  render builds it draws a catalog-only `bugz-local` chosen AND collects it through `bugzInputsOf` (red, reader stubbed to
  the reviewer list: "the collect refused the pick the sidebar shows chosen"); and source pins on the provider — its
  `collectBugs` goes through `collectWithPick` with the `no-ranking-model` notice and builds no arguments itself, its
  reads go through `bugzInputsOf` with every catalog row.
- `bugzPicksFromTheCatalog.test.ts` — the allowlist-refused row's pick drawn stranded; the two no-pick sentences ("Pick a
  ranking model in the Bugz section before collecting." when a model is offered; red: "a model IS ticked — the sentence
  sends the person to Models anyway"); a stranded pick beside nothing drawn with no `<select>` and no "pick one here",
  nor in the refusal (red: "a picker drawn with nothing in it to choose"). `bugzSection.test.ts`'s allowlist tests ask
  `bugzPickOf` with rows and a rule now.
- `panelPageHarness.test.ts` — a fixture opening `models`, `reviews/commands` or `setup/team` runs the Settings page on
  that place (red: "models ran the sidebar, not the Settings page"); no existing fixture named a place by its own id.
- **In a real editor** — `test/host/bugzCollectScenario.ts`: `coai.bugzModel` written to the real user settings
  (stranded, then empty), read back by the provider's readers into `bugzInputsOf`, refused by `collectWithPick` with
  nothing started. Teeth: planted to start first, it failed 'a collect started with the pick "gone/qwen3.5" read from
  the real settings'.
- **CodeRabbit on #709.** `bugzPicksFromTheCatalog.test.ts`: a ticked row the allowlist refuses is never called
  "no longer ticked" — not in the stranded line, its option, nor the collect's refusal — and the line names what does
  rank (red: "a ticked row is called unticked"). `panelPageHarness.test.ts`: the opened place is asserted by RUNNING the
  page over its drawn tree and reading which panes it shows, not by reading `heldPlace` out of the source (teeth: the
  script made to ignore the held place failed "reviews/commands is not the place the page shows").

**Not covered.** The provider's own ports — the notice actually shown, no process spawned — because the provider is
built only by the bundled extension's `ExtensionContext`: they are pinned by source, and the decision they feed is run.
A collect that does start (it spawns coai-mcp against the window's database). The `data-goto` press in a real editor (a
host cannot reach a webview's DOM). Step 5's prerequisite — every place of the new page held by a test that runs the page
and works one of its controls — was mapped (the plan's progress line) but not completed; it is E5.1b/E5.1c's.

## E5.1b — the health block on the new page, one ON rule, the owed Setup tests (2026-10-08, `research/PLAN_one_model_catalog.md`)

- **The consultant health block, on both pages** — `consultantHealthPage.test.ts` runs EVERY case from one table
  (`PAGES`: the current page's `settingsHtml(…, 'consultant')`, the new page's `catalogHtml(…, 'consultants/consultant')`),
  each registered once per page: one Check per caller with its id, the click posts it, disabled while checking and
  after a reload by the server's settled state, none on a too-old server, none in another side's read-only block, a
  failure of another consultant absent (now guarded: the page must have drawn a block) and the row's own present, the
  confinement line and the rule's `<pre>`, escaping, the snippet's Copy, a `copied` relabelling only its button, a
  non-copy `copied` changing nothing (guarded: the page must have drawn the four Checks), an `h4` per side block, no
  live region. RED before the block was drawn: all 16 new-page cases, e.g. "the page has no checkConsultant button for
  gemini". **The identity case**, new: a pick that is a catalog row (`{ vendor: 'deep-high' }`, the row's model
  `gpt-deep-1`) and a server report naming that pair — the row's own failure is shown, with no "which is not what this
  row names now" notice (RED on the new page: "the picked row's own failure was filtered away as another
  consultant's"; teeth: `callerHealth` matched by the STORED entry turned it red on both pages).
- **One ON rule for roles** — `rolesOnTheNewPage.test.ts`, one table over the plan, code, documents and feature
  buckets: on the new page (`catalogHtml(…, 'reviews/roles')`, run on the DOM shim) the last role ON — the other role
  active in the catalog but off by `roleEnabled` (plan: off in the catalog, its roles have no second switch) — has its
  switch disabled with the "only role still active" hint, and with another role ON it does not; at the host
  (`rowsAfter(…, roleEnabled)`) switching it off is refused, and with another ON it is stored. RED (code, documents,
  feature; plan was already guarded): "Architecture could be switched off while every other code role is off, so the
  code stage would run no role", "the host stored Architecture switched off, leaving the code stage with no role ON",
  and the same for DocumentSummary and FeatureReview. Source pins: `PanelProvider.roleEdited` queues with
  `() => this.roleSwitches()`, and `rolesHost`'s `store` and `removeConfirmed` hand `roleEnabled()` to `rowsAfter`
  (teeth: `queueRoleEdit(command)` alone failed "the new page queues its role edits without the panel's switches").
  `rolesPage`, `rolesEdit`, `settingsShape` and `panelView` tests stayed green over the shared count.
- **Setup › Team servers and Setup › MCP server, run** — `setupOnTheNewPage.test.ts`, one table: add, sign in, sign out,
  remove (signed in and out); install when an update is offered, check again, change and move the data folder, delete
  the old one after a verified move — each button found inside its own place's pane and pressed through the page's own
  script, the one posted `[type, command, id]` asserted; and no Install when up to date, no delete after an unverified
  move. `panelPageHarness.pressCommand` presses the one button of a command that has no `data-id`. Teeth, one compiling
  break at a time, each red and restored: a renamed sign-in / remove / install command, sign out and delete losing their
  id, add and install gaining one, Check again unwired, move posting change, the page script posting the command as
  the id (all ten red), setup/team drawn without its servers, Install offered when up to date, delete offered after an
  unverified move.
- **The code round's two findings** (both red first). Finding 0 — `rolesOnTheNewPage.test.ts`, one table over the four
  buckets with a role of one's OWN as the last ON (only such a role has a stage picker and a Remove): the page offers
  only its own stage (`OPTION`s not `disabled`), draws Remove `disabled` and a click on it posts nothing; with another
  role ON it offers all three stages and Remove posts. Red: "MyPlan, the last plan role ON, could be moved to another
  stage or removed on the page" (and MyCode, MyDoc, MyFeature); teeth: the script's disabled check removed turned all
  four red. Finding 1 — the same file: a page of 1000 roles of one's own (half on) drawn block by block from ONE
  `roleBlockOptions`, the role list read through a counting proxy (R7's pattern), at most 10 reads per role. Red, against
  a `roleBlockOptions` seam that carried no counts: "1008 blocks read the role list 3052224 times — every block
  rescanned it".

**Not covered.** The provider's composition of the new page's role edits beyond the source pins (a host runs only in an
editor); the queue reading the switches at APPLY time rather than queue time is by construction, not by a run; a
browser's refusal to click a disabled option or button (the shim models the page's own refusal); the paid Check
itself, as before (E5.5).

## E5.1c step 4 — the three tabs' tests, asked of their places (2026-10-08, PLAN_one_model_catalog.md E5.1)

The Review roles, Gate commands and Chat presets tabs are deleted; each behaviour their tests held is asked of the place
of the Settings page that draws the same blocks, through the page's own script (`pageTree.ts` + `pageScriptHarness.ts`).

| Was | Now | What it holds |
|---|---|---|
| `rolesPageHarness.ts` | `rolesPlaceHarness.ts` | Roles & prompts drawn and RUN: `runRolesPlace`, `rolesPlaceHtml`, `roleBlockIn`, `roleEditsOf` |
| `rolesPage.test.ts` | `rolesPlace.test.ts` | what the roles' editor draws and refuses to offer; the pure parser |
| `rolesPageScript.test.ts` | `rolesPlaceScript.test.ts` | every press fired at the control the place DREW |
| `editRolesInTabs.test.ts` | `rolesPlaceColours.test.ts` | the green frame and the sidebar's palette, in the whole document |
| `rolesBusyMark.test.ts`, `commandsBusyMark.test.ts` | `rolesPlaceBusyMark.test.ts`, `commandsPlaceBusyMark.test.ts` | numbered presses and the bar on the place |
| `featureStageOnRolesPage.test.ts` | `featureStageOnTheRolesPlace.test.ts` | the Stage select offers `feature`, and choosing it moves the role under *Feature stage* |
| `chatPresetsPage.test.ts` | `chatPresetsMessages.test.ts` (the parser, repaint and settle rules) + `chatOnTheNewPage.test.ts` (a large prompt box, one main tick, escaping, the empty list, unreadable models) | |
| `commandsPage.test.ts` | `commandsMessages.test.ts` (the parser, the skew note) + `commandsOnTheNewPage.test.ts` (Remove, a tick and a stage once on change, markers and Restore, escaping, the stale note) | |
| `formPagesShareOneColumn.test.ts`, `theRolesPageKeepsItsTextSize.test.ts` | — | the tabs' OWN stylesheets, gone with them; the Settings page's column and text size are `theTextControlsHoldUpEverywhere.test.ts` and `everyPageHasBothTextControls.test.ts` (the census is eight pages now) |

New: `editorRedirects.test.ts` — one row per redirected command naming its own place (a crossed wire is red: swapping
two places fails both rows); `theSettingsTabKeepsWhatWasTyped.test.ts` — closing the Settings tab flushes the roles',
commands' and presets' queues, opening it prunes the dead model presets (red with either line removed); `chatSettings.test.ts` reads
`unreadable` from the presets the models come from (red when the reader is emptied).

### E5.1c step 5 — the tests that read the current page

| Was | Now |
|---|---|
| `settingsPage.test.ts` (the old strip) | `catalogPage.test.ts`: opens on the held place, an unknown place opens the first tab, a press tells the host, arrows in ONE strip — plus, moved: a key the strip does not own is left alone, and a repaint puts the caret back in its hidden pane |
| `consultantSectionScript.test.ts` (each caller's own definition) | gone with the definitions: a caller picks a catalog row (`consultantPicks.test.ts`) |
| `catalogTabsWrite.test.ts` (old page vs new, control by control) | every setting control of the Settings page writes ITS OWN key |
| `bundledPage.test.ts` (`settingsHtml` bundled) | `catalogHtml` bundled: no `vscode`, switches a tab, embeds `rankChoices` whole |
| `consultantHealthPage.test.ts` (both pages) | the Settings page — still a table, one row |
| `panelView.test.ts` / `chatSection.test.ts` / `apiRuntimeGate.test.ts` / `selectSearchPage.test.ts` / `qconsultSection.test.ts` cases of the old reviewer card, per-role tick, caller colours, chat model selects, inert api card, per-row vendor picker | removed with what they drew; the Models card, Roles & prompts, Chat and the question rows are `modelsTab`, `rolesOnTheNewPage`, `chatOnTheNewPage`, `qconsultRowPicks` |
| `textControls`, `theSettingsTabFollowsTheTextSize`, `busyMarkPage`, `everyPageHasBothTextControls` (census 7) | the same claims of `catalogHtml` / `catalogKey` |
| `epicThreeCodeRound` "each page keeps its own place", `catalogPlaces` `oldIdOf` | gone: one page, one held place |

New: `catalogPage.test.ts` "the Settings page is the only one" (no `settingsPreview` command, no preview badge);
`consultantHealthWatcher.test.ts` asks the places and `placeOf('consultant')`; `panelSections.test.ts` holds that no
section is declared for the settings surface.

## E5.3 — the tests that held the old page's builders (2026-10-09, PLAN_one_model_catalog.md E5.3)

| Was | Now |
|---|---|
| `consultant.test.ts`: the catalogue picker per caller (offered entries, labels, refusals, kept model, endpoint of your own, caller colours, local engines, Claude probe, codex cache) and the composite definition write (vendor/model/endpoint/CLI path, materialise, keep unknown fields) | gone with the caller definitions: a caller picks a catalog row (`consultantPicks.test.ts`); what a row is and how it answers is its Models card (`modelsTab.test.ts`) |
| `consultant.test.ts` "a caller pointed at its own vendor is told what to think" | `consultantPicks.test.ts` "a Gemini CLI caller pointed at an Antigravity row is told it is its own vendor" |
| `consultant.test.ts` "an entry the rule cannot place carries its REASON", "a vendor id is escaped" | `consultantPicks.test.ts` "a caller whose consultant cannot be placed says why under its pick, and its id is shown as text" |
| `consultant.test.ts` "no Team server can be offered as a consultant", "a catalogue entry that cannot hold a conversation is NAMED" | `modelsTab.test.ts` "a row that cannot hold a consultation — a Team server — cannot be ticked consultant, and its card names the runtimes that can" |
| `consultant.test.ts` / `consultantHelp.test.ts` "every caller kind is named in words a person reads" | `consultantHelp.test.ts` "each caller's pick is named in words a person reads, and carries the \"?\"" |
| `customEndpointIsOneFlow.test.ts` "both flows go through it", "the consultant flow writes the CALLER it was given" | one flow now (Add a model); the consultant custom endpoint went with the definitions |
| `claudeModels.test.ts` "a Claude dropdown says it is being asked", `panelPhrasesScript.test.ts` "the looking mark is one rule" | gone with `claudeNote` and `LOOKING`, which only the old rows drew |
| `qconsultWrite.test.ts` "choosing another vendor or prompt takes the row off" | "choosing another prompt takes the row off" — a row picks a catalog row; there is no vendor control |
| `refusedSelect.test.ts`: a `'consultants'` composite write snaps back | a consultant PICK (`savePick`) snaps back |
| `catalogRow.test.ts`, `theOldPagePricesWhatItShows.test.ts` (since 2026-10-09 `theCardsArePricedFromEveryRow.test.ts`, pinning the per-row prices instead) name `shownOnTheOldPage` | `isReviewerRow` |

## The local round counter — `--round-counts` and schema step 20 (2026-10-10, story 2.1 of `todo/PLAN_every_round_is_counted.md`)

Three suites, real SQLite over temp directories, and the mode run as the PROCESS the extension will run
(`OneShotProcess`, the one-shot runner written once for this scenario; on 2026-10-11 `LogCliScenarioTests`,
`CloseConsultCliScenarioTests` and `ConsultantModesCliScenarioTests` lost their private copies of it and run through it
too — the first two as they are, the third shaping the fake CLI's environment on `OneShotProcess.StartInfo` and running
it with its own three-minute deadline through `OneShotProcess.Run(info, deadline)`; its stdio-server and killed-holder
tests start a long-lived child and keep only the start info, since a deadline that kills them is not their contract;
a refactor with no behaviour change, guarded by the four classes' 33 tests). The clock is a constant (`2026-10-10T12:00Z`) handed to the reader, never
read off the machine, so no window expires overnight. Every behaviour was observed RED first — against stubs that kept
today's behaviour (the records and an empty `Read`), and against today's binary for the process — then GREEN, then red
again with ONE production line reverted per build (ten builds, each file restored byte for byte with a fresh mtime).

| Suite | What it holds | Seen red as |
|---|---|---|
| `RoundCountsTests` (10) | `ModelsKnownSince_WalksTheRoundsByTime_AndStopsAtTheFirst_NeverAggregatingEveryReviewer` (the code round's finding: the once-a-minute read must not cost more as history grows — the query walks `rounds_by_time` oldest first, looks reviewers up by `reviewers_by_round`, sorts nothing and stops at `LIMIT 1`; the plan text reads the same for the first form's `MIN` over the same indexes, so the order and the limit are what is pinned; RED against the `MIN` form: `Expected RoundCounts.ModelsKnownSinceSql … to contain "LIMIT 1"`). One fixture of twelve rounds placed on and around every edge: every stage counted under its own name per vendor row per window; a round of three codex reviewers is ONE round and three reviewer rows; a Team-server row (`team-codex`) is its own provider and its model stays on it; the windows are `(from, to]` — a round exactly 5 h / 24 h / 7 d old is in the next window up and not this one, one exactly at `now` is in every window, one in the future in none; per-model rounds come only from reviewer rows that recorded a model, and `modelsKnownSince` is the oldest such round over the WHOLE table; a window nothing ran in has no rows and is still answered; a missing database is an empty answer and the file is not created; a step-19 database counts its rounds, knows no models and is left at step 19; the read leaves a CURRENT file byte for byte (SHA-256, mtime, `user_version`) — a guard against a data write, and its own remark says the migration guard is the step-19 pair, because on a file already at the head a migrating open changed nothing | RED against the stub: `Expected … Rows.Select(r => r.Provider) to be equal to {"codex", "gemini", "team-codex"}, but found empty collection`; `Expected stopBefore to be greater than 0 … but found -1` (no step added the column). Break-it: the window start made inclusive → `Expected Row(answer, "5h", Codex).Rounds.Code to be 3 because the round exactly 5 h ago is not in the last 5 h, but found 4`; reviewer rows counted instead of distinct rounds → `… to be 3 because round b ran three codex reviewers and is ONE round, but found 5` and `RoundsOf(… Luna) to be 7 … but found 9`; the writer binding an empty model → `Sequence contains no matching element` (no model line to find); `modelsKnownSince` read over the window → `Expected answer.ModelsKnownSince to be the same string … but they differ at index 9`; the missing-file check disarmed → `SQLite Error 14: 'unable to open database file'`; the read opening for writing and migrating → `Expected Version() to be 19L because a read-only reader cannot migrate, and must not try, but found 20L`; the model column selected whether or not the file has it → `SQLite Error 1: 'no such column: v.model'` |
| `RoundCountsCliScenarioTests` (6) | The real binary over `COAI_DATA_DIR`: the JSON contract with `--now` (`nowUtc` echoed, three windows, `codex` with one code round, two reviewers, `gpt-6-luna` once, `modelsKnownSince`); a bare `--round-counts` on the real clock sees a round recorded a minute ago in every window; a bad argument — `--now yesterday`, an unknown flag, `--now` twice, a dangling `--now` — is **65** with nothing on stdout, the mode named on stderr and the refused value quoted back, while `--round-countz` is **64** as the control; a missing database is exit 0, three empty windows, `"modelsKnownSince": null` WRITTEN, no file created; a step-19 file is read without being migrated (SHA-256, mtime, `user_version` 19) and its rounds counted; the mode is known to `Classify`, in the help and in `.agents/PROJECT.md`'s one-shot list | RED against today's binary: `Expected code to be 0, but found 64`; `Expected run.Code to be 65 because an unparseable --now is a bad REQUEST to a mode this binary has; 64 would read as an old binary, but found 64`; `Expected Program.Classify(["--round-counts"]) not to be Startup.Usage … but it is`. Break-it: a bad argument answering 64 → the same 65-against-64 message; the null omitted instead of written → the `"modelsKnownSince": null` text assertion; the missing-file check disarmed → `Expected code to be 0 … but found 74`; the migrating open → the SHA-256 `differs at index 0`; `v.model` selected on a step-19 file → `found 74` |
| `TheReviewersModelIsStepTwentyTests` (6) | Step 20 by POSITION (`Steps[19]` is `ALTER TABLE reviewers ADD COLUMN model` plus `CREATE INDEX … reviewers_by_round`, `Steps.Length == 20`); a step-19 database holding a reviewer row gains the column and the index and that row reads an empty model; `RecordRound` writes each reviewer's model and a stood-down reviewer writes none; the consultant-preview shape (17, failure columns) and the security-preview shape (17, evidence) are repaired and reach step 20; a migrated database opens again at the head and re-runs nothing | RED: `Expected ModelStep to be 19 … but found -1`; `SQLite Error 1: 'no such column: model'` (the writer); each preview shape `Expected Shape() to be equal to …` the head with the column and the index, found the head at 19 without them. Break-it: the index statement removed → `Expected Schema.Steps[19] … to contain "CREATE INDEX …"` and every `Shape()` with its index false; the writer binding an empty model → `{"codex=", "gemini=", "local="} differs at index 0` |

**NOT covered:** a WAL database read across the Windows/WSL boundary (2.2 names the other side's store instead of
opening it); the 74 for a database that will not read, reached only through the shared `Program.Unreadable` path that
`--log --paged` already exercises; a stage name this build does not know (counted under no stage — no such row can be
written by this build). The extension's side is the section below.

## The extension reads the counter, and the Spending tab's Rounds block (2026-10-10, stories 2.2–2.4 of `todo/PLAN_every_round_is_counted.md`)

`vscode`-free throughout: the spawn is a `Run` handed in, the clock is a function, the ledger is a temp file, and the
`--round-counts` answer is one fixture (`roundCountsFixtures.ts`, `ANSWER_JSON`) parsed through the REAL parser — a
fixture the parser refused would leave every test asserting the unavailable path, so `counted()` throws on it. The
page run goes through `bundledLogModule`, widened to export more modules from the same minified bundle. The work landed
before this session observed it, so every behaviour was re-reddened by reverting the production line it depends on
(one build per batch) and restored; the messages are in the table.

| Suite | What it holds | Seen red as |
|---|---|---|
| `roundCounts.test.ts` (5) | The contract fixture is a count, every window and stage read, a row absent from a window is absent (not a fake zero row); rows `[]` is a count of nought; 64, 65, 74, -1, `not json`, a window named `3h`, exit 3 are each `unavailable` with the reason named; a row the parser cannot read refuses the whole answer; `readRoundCounts` asks exactly `['--round-counts']` through the run it is handed, and no binary is no count | `exit 64 with "" read as a count` (64 let through as a count) |
| `roundCountsCache.test.ts` (6) | `TheCardLine_DoesNotSpawnPerRender` (twenty renders in a minute, one spawn; nothing drawn before the first read); `AConsultationOnlySession_RefreshesTheCount` (`forget()` brings the read forward, once); `AnIdleWindow_FallsOnAFakeClock` (a round thirty seconds inside 5 h leaves it after an idle minute); a round ending reads again, the first sighting only remembers; a throwing load is a stamped unavailable snapshot, read once, its `ledgerFailure` saying the read failed (RED before the fix: `a thrown load left the money looking like nothing spent`); the card counts consult turns for its own row and an unavailable count stays unavailable | `due()` always true → `a render spawned coai-mcp 21 times inside one minute`; the age bound removed and `forget()` a no-op → `an idle minute did not read again, so the window could never fall` and `the ending did not make the count due` |
| `ledgerTail.test.ts` (5) | `TheLedgerRead_StopsAtTheOldestWindow` — a year of lines before a 7 d window of 200 costs ≤ the window's bytes + one 16 KiB chunk, oldest first; a chunk smaller than a line joins every torn line; a missing file is no lines AND no failure, a torn last line costs itself; `AnUnreadableLedger_IsAFailureThatSaysWhy_NeverNoLines` (a directory in the file's place: no records and a `failure`); the loader hands that failure to the snapshot as `ledgerFailure` | the stop removed → `read 4 263 040 bytes of a 4 263 041-byte ledger for a 34 600-byte window — the read did not stop`; before the fix (every failure swallowed as no lines) → `a ledger that would not read came back as an empty one — the money would read as nothing spent` and `the snapshot lost the reason the ledger did not read`; break-it, a directory let through to the read (on Windows it opens with size 0 and never fails) → the same two |
| `chatSpendSection.test.ts` (+1) | an unpriced chat turn makes the joint *Reviewers and chat together* total a floor (`≥`), not only an unpriced reviewer line (CodeRabbit on #750) | RED: `an unpriced chat turn left the joint total looking complete` — the line read `~$3.00` |
| `roundsBlock.test.ts` (13) | the card counts only the consult turns inside the block's own 7 days — one line eight days old is not counted (CodeRabbit on #750; RED: `a consult turn eight days old was counted in the card's 7 days`). `AnUnreadableLedger_IsUnavailableSpend_NeverAZero` — a snapshot whose ledger did not read says *Tokens and money: unavailable — the usage ledger could not be read: …* once, *unavailable* in the row's beside / tokens / money / per-round cells and the model line's median / tokens / money / per-round cells, the counts still shown, no `in 0 ·` or `$0`; the card line says *consult turns unavailable* (RED before the fix: the warning missing, and `7 days: 14 rounds · 0 consult turns`). A vendor row's 5 h / 24 h / 7 d, its 7 d stages and reviewers per round; a model change inside the window is two model lines with their own rounds and per-round figures; an unavailable read is *count unavailable* in every count cell with no `<td>0</td>` and the ledger's tokens still shown; before the first read *Counting…* and no cells; consult / question / chat as recorded turns, priced per recorded turn; `AConsultationOnlyWindow_ShowsNoPerRoundFigure`; a Team-server row is its own row *ran on* its server; `TheOtherSidesStore_IsNamedNotOpened` — `otherStores` asks only `coai.db`'s existence and the sentence names the existing store and not the empty directory; the Models card carries the line from the cached snapshot, nothing before a read, *count unavailable* after a failed one; and the shipped Review rounds page, run from the minified bundle, draws the block FIRST on the spending tab with the counts, and with an unavailable read the words and no zero | an unavailable cell drawn as `0` → `an unavailable count was drawn as a zero`; the card line printing rounds for an unavailable read → `an unavailable count is never drawn as rounds beside a consult count`; `ranOn` emptied → the `ran on work` match; the model line taking the row's rounds → `the model the row moved away from vanished, or took the other's rounds`; `otherStores` naming every directory → `a directory with no store was named as one`; the card's `roundsLine` not passed → the `rounds-line` match; `usageTabHtml` dropping `extras.rounds` → `the page did not draw the block the host rendered` |
| `helpCoverage.test.ts` (+2) | Every language's spending article names **Rounds**, *count unavailable*, **5 h**, **7 d** and `--round-counts`; no language still says the product *ships no price table* (its own stale sentence per language) and every one names OpenRouter as the list the default price comes from | RED first: `the en spending article never says “**Rounds**” — stale rather than missing`; `the en spending article still says “ships no price table”, which has been untrue since the list prices` |

**NOT covered:** `PanelProvider.roundsBlockHtml` / `roundCountsOf` and the two `forgetRoundCounts` calls in
`extension.ts` (thin `vscode` wiring over the tested cache — `forget()` itself is tested); the CSS of the block and the
card line (no behaviour to assert).

## The deployed ledger's own lines as fixtures (2026-10-10, story 1.2 of `todo/PLAN_every_round_is_counted.md`, E1)

Three lines of the Team server's live `usage.jsonl`, copied read-only on 2026-10-10 through the operator's credential
vault — the last `ok`, the last `TimedOut` under ten seconds, the last `RateLimited` — with the person's email replaced
by `person@example.invalid` before the bytes left the host, under `src_server/tests/fixtures/usage/` and copied to the
output by the csproj. They are what the deployed server WRITES, as bytes: the twelve keys `utc … email, kind` and none of
`tokensCached`, `tokensReasoning`, `usageNote`, `costNote` (the deployed binary predates them), and the 8.4 s withdrawal
filed as the vendor's `TimedOut` with a free 0/0 — the defects E3 fixes, visible in the fixture rather than composed by
a test. The measurement they belong to is [RESULTS_vendor_quota_gauges.md](RESULTS_vendor_quota_gauges.md) §8.

| Suite | What it proves | RED observed |
|---|---|---|
| `UsageLedgerFixtureTests` (new, 27) — the three shapes | the folder holds exactly `ok.jsonl`, `rate-limited.jsonl`, `timed-out-withdrawal.jsonl`: the companion of every scan below, so a copy rule that forgot the folder cannot pass the theories over nothing | a planted fourth file → `Expected … a collection with 3 item(s), but {"ok.jsonl", "planted.jsonl", …}` |
| — the four redaction guards, one theory each over every file | only the ledger's own keys (an allow-list of sixteen); no `@` but the placeholder's, and the placeholder present; no IPv4 and no host-name shape (`label.tld`; a model id's `5.6` is digits after the dot and does not match); **no token-like value in ANY string field** — `Bearer` / `Basic` + token, the `sk-` / `ghp_` / `github_pat_` prefixes, a JWT (`eyJ….….…`), a 32+ run of hex / base64 characters, the last exempt only in the `model` field and only for a dashed or dotted id of short lowercase segments | a planted un-redacted line in the OUTPUT folder → 4 red with the real symptoms (`must not carry an IP address … found True`; `to contain "person@example.invalid"`; four fixtures not three; `no outcome is named for fixture 'planted.jsonl'`); a foreign `host` key → `items {"host"} are not part of the superset`. **The hole the E1 code round named**: a `Bearer abc…` in the allow-listed `usageNote` passed all 13 of the first three guards' cases; with the fourth guard it is red — `'usageNote' must not hold a credential, but found "an HTTP credential (Bearer / Basic followed by a token)"` — and a JWT planted in `costNote` is red with `"a JWT (three base64url parts, the first starting eyJ)"`; plant removed → 27 of 27 |
| — `APlantedSecret_InAnyStringField_IsRefusedByName` (9 rows) and `ALongDashedModelId_IsNotASecret_ButTheSameTextInANoteIs` | the pure `TokenLike(field, value)` — the same function the theory applies — refuses each planted shape in memory and names its rule; a 43-character dashed model id passes in `model` and is refused as a 32+ run anywhere else | — (new behaviour, the shapes are the plant) |
| — `EveryLine_ReadsThroughTheServersOwnReader` | each fixture copied into a temp data directory as `usage.jsonl` reads through the REAL `UsageReader` as one line, zero unreadable, with the placeholder email, kind `Review` and the outcome its file name promises (`OutcomeOf` throws on a name it does not know, so a mislabelled fixture is caught) | — |
| — `TheDeployedWithdrawal_WasWrittenTimedOut_AndThisServerWritesItWithdrawn` (until story 3.2: `TheWithdrawal_IsWrittenTimedOutWithNothingCounted_WhichIsTheDefectE3Names`) | the deployed server's shape of a withdrawal, asserted as it WAS: `TimedOut`, under 10 s (the fixture holds 8.4 s), 0/0, no `usageNote` — and, since story 3.2 (changed on purpose, as promised), the same event through this server beside it: `Cancelled`, `endCause: withdrawn`, *usage not captured* | — (the story's own section below has its reds) |

Run as `./src_server/tests/bin/Debug/net10.0/CoaiServer.Tests.exe`; the whole server suite was 424 of 424 after the merge
of `origin/main` (411 before it, 14 of them this class's first form).

**What it does NOT prove.** Three lines of one day on one deployed version are not the ledger's vocabulary: no
`UnparseableByVendor`, `NonZeroExit` or `NotStarted` line, no `chat` kind, no line carrying the newer fields, no local
(email-less) line. The guards are SHAPES: a secret that is none of them — a short password, a prose-shaped token — passes,
a host written as a bare word passes, and the model-field exemption would pass a dashed credential shaped like a
catalogue id. The reader test proves the deployed server's bytes parse through today's `UsageReader`, not that E3's
widened reader will — that is `AnOldLedgerLine_StillReads`, owed by E3, and these fixtures are its input. Nothing here
reaches the live ledger beyond the three lines that were copied.

## The server reads a stated duration (2026-10-11, story 5.3a of `todo/PLAN_every_round_is_counted.md`)

The scenario reaches as deep as the Team server's harness goes without a product seam: the real `JobRunner`,
`ReviewLauncher`, antigravity adapter and `SlotRegistry` on disk, with a fake `IProcessLauncher` standing in for the
paid agy process. Run: `./src_server/tests/bin/Debug/net10.0/CoaiServer.Tests.exe --filter-class "*CooldownParserTests"
--filter-class "*AStatedResetParksTheSlotTests"`.

| Suite | What it holds | Seen red as |
|---|---|---|
| `CooldownParserTests` (+8) | `AStatedWaitInWords_ErrsLong_AndWeeklyStillWinsWhenLonger` — `Resets in one hour` errs long to `MaxGuess`, and a line that also says "weekly" takes the longer day (CodeRabbit on #754; RED: `… but <… 12:30:00 +0h>`, the 30-minute retry). `ASpacedDuration_IsReadWhole_NeverAsItsFirstPart` — `2h 30m` and `1h 2m 3s.` read whole, `2h 30 minutes` errs long to `MaxGuess` (CodeRabbit on #754; RED: `2h 30m` read as two hours, `… but <… 14:00:00 +0h>`; break-it: the trailing `(?!\s*\d)` removed → `2h 30 minutes` read as two hours again); and `2h, 30m` / `2h; 30m` err long while `2h, then try again` is two hours (CodeRabbit on #754 again; RED: `Expected CooldownParser.Until("Resets in 2h, 30m", Now) … <… 17:00:00 +0h> … but <… 14:00:00 +0h> does not` — the cooldown would end 30 minutes before the stated reset; the trailing check widened to `(?![\s,;]*\d)`). `ADurationWithTextGluedToItsUnit_IsNoDuration` — `Resets in 2seconds` and `1hgarbage` err long to `MaxGuess`, not a short wait; a full stop is a boundary (the code round; RED: `… but <2026-09-06 12:00:02 +0h> does not`). `CooldownParser_ReadsResetsIn88h49m9s` and `…33m52s` on the two verbatim agy lines (RESULTS §3.1, the fixture); a duration of one part (`5h.`, `45s`, `12m`); `ADurationNoVendorWindowCouldMean_IsTheFallback_NeverAThrow` — `999999999999999999h` and `9999h` err long to `MaxGuess` (a wait was stated), `336h` is exactly the 14-day ceiling; `ADurationIsNotMistakenForAClockTime` — `Resets in 9h30m` is nine and a half hours, `resets 9:30pm (UTC)` still a clock time | RED: every duration read as the 30-minute guess (`… but <2026-09-06 12:30:00 +0h> does not`). Break-it: the duration branch disabled in `NamedInstant` → the same five red |
| `AStatedResetParksTheSlotTests` (2) | the verbatim refusal through the real runner parks the slot for 88h49m9s, READ BACK FROM DISK, and a second queued job launches nothing; with `Resets in 2s` the next job waits, then launches once the two seconds pass — the positive control the cadence consultation asked for, so a slot parked for good cannot pass | RED: `parked!.Value to be within 30s from <… +88h49m9s> … but <… +30 min> was off by 3d, 16h…`; `Expected launched to be True because after the stated two seconds the account is used again — not parked on a 30-minute guess, but found False`. Break-it: the same plant → both red |

**NOT covered:** the HTTP host (`Program.cs` builds the launcher inline — a vendor double there needs a product seam);
a real agy refusal on the deployed box — checked live at the next deploy.

## The server ledger tells the truth — the whole usage, and the canary's own kind (2026-10-10, stories 3.1 and 3.5 of `todo/PLAN_every_round_is_counted.md`)

The seam rule of the plan's test plan: a defect BELOW `IReviewLauncher` is tested through the real `ReviewLauncher` with
a fake `IProcessLauncher` (`TheWholeUsageTravelsTests.Codex` writes the `-o` answer file and prints a codex event
stream), because a fake launcher above the defect cannot fail for the right reason.

| Suite | What it holds | Seen red as |
|---|---|---|
| `TheWholeUsageTravelsTests` (4) | `TheServerLedger_KeepsCachedAndReasoningTokens` — a codex `turn.completed` with 12 544 cached tokens reaches the job and the server ledger line; `AReasoningCount_ReachesTheServerLedger`; `ANonZeroExit_KeepsItsUsage` — a codex launch that exits 1 after reporting 14 508 / 12 544 is written with them; `TheServersReader_ReadsCachedReasoningAndTheUsageNote` — the reader keeps all three fields of a line | RED before the fix: `Expected job.Usage.TokensCached to be 12544L … but found 0L`; `… TokensReasoning to be 41L, but found 0L`; `Expected job.Usage.TokensIn to be 14508L because a vendor that ran and exited non-zero was billed for what it ran, but found 0L`; `Expected line.TokensCached to be 12544L because the reader dropped the cached count the line holds, but found 0L`. Break-it: the success taking only in/out → the cached and reasoning tests red; the runner's failure arm keeping usage for `Unparseable` alone → `ANonZeroExit_KeepsItsUsage` red with the same 0L |
| `ReviewLauncherTests` (+1) | `AContinuationThatEndsBadly_KeepsTheFirstLaunchsUsage` — an agy denial (100/5) continued into a launch that exits 1 (40/20): the attempt is that `NonZeroExit`, carrying 140/25 | RED: `Expected failed.Usage to match (u.TokensIn == 140) AndAlso (u.TokensOut == 25) … but found` the empty usage. Break-it: the terminal arm reading the outcome alone → the same |
| `TheCanaryIsACanaryTests` (2) | `TheCanary_IsAReviewTheDeploySends_SaidAsItsOwnKind` — `canary` reads as a kind, wires as `canary`, is accepted with a role and refused without one, and the ledger knows the word; `TheCanary_IsReadBackAsItsOwnKind_AndReportedApart` — a canary line reads back as `canary` and `UsageTotals.ByKind` reports it apart from the reviews | RED: `Expected JobKinds.TryRead("canary", out var kind) to be True because the release script says it, but found False`; `Expected JobKinds.Wire(lines[0].Kind) to be "canary" … but "review" differs`. Break-it: the table's review row without `Canary` → `Expected JobKinds.Refusal("canary", null, Shipped) <null> to contain "needs a role"` |
| `install.test.ts` (+1) | the deploy canary names `"kind":"canary"` and recognises an older server's *is not a kind of job* before it starts polling | RED: `the canary submit names its kind` |
| `teamServerView.test.ts` (+1) | a `canary` kind is named *deploy canaries* beside the reviews | RED: `the wire word shown bare reads as somebody's kind of review` |

The script's fallback was also run against stubbed `curl` and `python3` (a harness outside the suite): an old server
answered the kind with its 400 and got a second, plain submit; a new one got one submit carrying the kind.

**Covered since epic 3's code round:** the script's fallback as an executed behaviour — `TheCanaryFallbackRunsTests`
(below, *Epic 3's code round*) runs the real `canary` against a stub curl in the ordinary suite; until then the suite
asserted only the script's TEXT and the stub run above was the behaviour, recorded once.

## Every paid launch is written, and written whole (2026-10-10, story 3.3 of `todo/PLAN_every_round_is_counted.md`)

The launcher is faked IN PROCESS for the local half (the defect sits above `IProcessLauncher`, in the executor, the
ladder and the ledger, so the real ones run); the server half runs the real `ReviewLauncher` and `JobRunner` with a fake
`IProcessLauncher` for the follow-up, and a fake `IReviewLauncher` for the rotation (a runner rule). Every new test was
RED for its real symptom first, then GREEN, then RED again with the fix's own line planted back.

| Suite | What it holds | Seen red as |
|---|---|---|
| `EveryPaidLaunchIsWrittenTests` (12, mcp) | `ARepair_WritesTwoLinesSharingATurn`, `AFollowUp_WritesTwoLinesSharingATurn` (agy's denied launch and its continuation), `ARetryAfterARateLimit_WritesItsOwnLine` — one line per launch, attempts named, one shared non-empty turn, and the lines' tokens summing to EXACTLY the folded line's; `ARepairThatExitsNonZero_IsBilledOnce`; `ASingleLaunch_IsOneLine_NamedFirst_WithATurn`; `PartsThatDoNotAddUpToTheTurn_AreNeverWritten_TheTurnIsOneLineAsItWas`; `AFeatureTurnsLaunches_AreSplitPerTurn_EachTurnItsOwnId`; `AVendorReason_IsNotClipped` (agy's verbatim refusal in the ledger AND the audit log; the summary still `…`); `ALongExitReason_IsNotClipped`; `AReasonIsKeptWhole_UpToTheSafetyCap_AndNoFurther`; `AWholeReason_IsStillRedacted` | RED: `Expected lines to contain 2 item(s) … but found 1` (repair, follow-up, retry); `Expected outcome.LastTurnUsage.TokensIn to be 1200L … but found 1400L`; `Expected line.Attempt to be "first" … but "" has a length of 0`; `… contain "AGY_ERROR: {…"retryable":true…}"` against `…Resets in 88h49m9s…`; the 615-character exit reason cut at 160. Break-it: the executor's parts dropped → the repair and follow-up tests; the ladder's parts dropped → the retry test; `EarlierLaunches = first.Usage.Add(second.Usage)` → 1400L; the add-up guard removed → `Expected … a single item because a split that does not add up to the turn is not trusted`; `Turn`/`Attempt` not written → the single-launch and per-turn tests; `RoundAudit` back to `Describe` → the log half of `AVendorReason`; `TailOf` back to its 400-character opening → the long-reason and cap tests; the redactor removed → `Did not expect said "… Bearer abcdef…" to contain "abcdef…"` |
| `AFailedRepairStillCountsTheMalformedAttemptTests`, `AReviewerThatAsksForSourceIsAskedAgainTests` (updated) | the folded single line those tests pinned is now one line per launch — 2 for a failed repair, 6 for a three-step ladder, 3 and 4 for the two conversation cases — with the same sums | RED after the change, as the old contract: `Expected lines to contain a single item because one reviewer, one turn, one line …`; `… 2 item(s) because one ledger line per turn, but found 4`. Break-it on the updated conversation test: the turn loop's `Launches` dropped → `Expected … {"first", "repair", "retry", "first"}, but {"first", "first"}` |
| `SecurityAnswerLimitCharacterizationTests` (+1) | `A_refusal_keeps_the_answers_launches` | RED: `Expected refused.Launches to refer to …`; break-it: the `Launches` initializer removed → the same |
| `ProbeApiModeTests` (+2) | `AProbe_WritesALine` — one `probe` line per completion sent with the real key (the wrong-key one bills nobody), the variant as role, 900/40/640 for an answered one, `HTTP 400 …` with 0/0 for the refused one, no key in the file; `AProbeWithoutAModel_SpendsNothing_AndWritesNothing` | RED: `Expected File.Exists(ledger) to be True because ten completions against a metered key were spent and nothing was written down`; break-it: the paid callback never called → the same |
| `QuestionFanOutTests` (+1), `ConsultScenarioTests` (+1) | `AFailedRowsLedgerLine_KeepsTheReasonWhole_AndRedacted`, `AFailedTurnsLedgerRow_KeepsTheReasonWhole_AndRedacted` — a CLI's `Authorization: Bearer …` stderr reaches the ledger redacted and whole | RED: `Did not expect line "…outcome":"exit 1: error: Authorization: Bearer abcdefghijklmnopqrstuvwxyz0123456789 was refused, … why it would not t…" to contain …` — the token AND the cut. Break-it: each site back to `Describe`, and the redactor removed → the same |
| `EveryServerLaunchIsWrittenTests` (4, server) | `ARequeuedAttempt_WritesALine` (slot a refuses, slot b answers: `RateLimited`/`rotated` 0/0, then `ok`/`rotated`, both turned by the job id); `AFollowUp_WritesTwoLinesSharingATurn` (`first` `UnparseableByVendor` 100, `followUp` `ok` 40, summing to the job's usage); `AJobOfOneLaunch_IsOneLine_NamedFirst_WithTheJobAsItsTurn`; `AnOldLedgerLine_StillReads_AndANewOneKeepsItsTurnAndAttempt` | RED: `Expected lines to contain 2 item(s) … but found 1` (requeue, follow-up); `Expected Text(line, "attempt") to be "first" … but ""`; the reader's tuple empty. Break-it: the requeue's `Ledger` call removed; the launcher's `Launches` dropped; the runner not passing `turn`/`attempt`; the reader not mapping them → each red the same way |
| `usagePricing.test.ts` (+1), `roundsBlock.test.ts` (+1), `helpCoverage.test.ts` (+1) | `TheParser_KeepsTheTurnAndTheAttempt` (an old line has neither); `AProbe_IsItsOwnKind_NeverARound` (`probe 1 recorded turns` beside, its own kind line, no model line); every language says `**probe**`, `--probe-api --model` and `` `turn` `` | RED: `actual: undefined, expected: '3f9a0c1b2d4e'`; `actual: '—', expected: 'probe 1 recorded turns'`; `the en spending article never says "**probe**"`. Break-it: `launchFields` not spread; `probe` taken out of `BESIDE`; the Ukrainian `**probe**` unbolded → `the uk spending article never says "**probe**"` |

**NOT covered:** the extension's own Claude model probe still writes nothing (no sanctioned writer fits — the plan's
deviations); a consultation's and a question row's continued turns still fold per turn; a cancellation during a repair
(story 3.2's); the cumulative difference of an agy follow-up (`todo/PLAN_the_reviewer_follow_up_is_billed_once.md`).

## Each line says what it ran with (2026-10-11, story 3.4 of `todo/PLAN_every_round_is_counted.md`)

The comparison is with what the launcher RECEIVED — the argv and the environment — never with the row's configured
values: the local round runs the real `PanelService` with the fake CLI behind a recording `IProcessLauncher`, the server
runs the real `JobRunner` and `ReviewLauncher` with a fake `IProcessLauncher` that keeps every request. Every new test was
RED for its real symptom first (the line said nothing of what its launch ran with), then GREEN, then RED again with a
line of the fix planted back.

| Suite | What it holds | Seen red as |
|---|---|---|
| `EachLineSaysWhatItRanWithTests` (4, mcp, collection `fakecli-env` — it steers `CODEX_HOME`) | **Changed on the consultation of 2026-10-11** (two facts, two fields): `ACodexRowsEffort_IsRecordedAsIgnored` (a codex row's `high`, never sent: `effortNotSent: ignored`, and `effort` is what RAN — the config.toml's `xhigh`, `cli-config`; RED before the change: `Expected Text(line, "effortNotSent") to be "ignored" … but "<absent>"`); `TheSameConfig_ReadsAsTheSameEffort_WhetherOrNotTheRowAskedForOne` (the consultant's paired fixture: one config, the row's effort empty against `low` — both lines `xhigh`/`cli-config`, only the second `effortNotSent: ignored`; RED: the asked row's effort read empty); `AnEmptyValue_IsLabelledFromCliConfig_NeverChanged` (an "As the CLI is set" row: the argv with and without a config.toml naming `xhigh`/`priority` is identical but for its temp paths, and the line says `xhigh`/`priority` `cli-config` — with no config, nothing); `TheRecordedLaunch_IsWhatWasSent` (a real plan round: an Off codex row → `tier: default` `row` read off the argv's `service_tier`, effort `ignored`, `cliVersion` = the tier probe's `codex-cli 0.160.0`; a `cli` codex row → effort and tier read from the config.toml the request's environment points at, `cli-config`; a claude row → `--effort high` and the fast-on file → `high`/`fast` `row`) | RED: `Expected Text(line, "effortFrom") to be "ignored" … but ""`; `Expected (… "effort", … "effortFrom") to be ("xhigh", "cli-config") … but found ("", "")`; `Expected (… "tier", … "tierFrom") to be ("priority", "cli-config") … but found ("", "")`. Break-it: the ledger not writing `RanWith` → all three the same; the `ignored` label removed → the codex row relabelled `xhigh`/`cli-config` (the mislabel the test forbids); the configuration not read → `("", "")`; a "label" that ALSO put the config's effort on the argv → `Expected Setting(argv, "model_reasoning_effort") to be empty, but found "xhigh"` and `… contains 2 item(s) too many` |
| `EachServerLineSaysWhatItRanWithTests` (5, server) | `TheRecordedLaunch_IsWhatWasSent` (a codex job: `tier: default` `server` off the argv, not the slot's `flex`; effort `low` `cli-config` from the config.toml the request's `CODEX_HOME` names — the slot's; `cliVersion` from `versionOf` with no `--version` spawned; `job`, `slot: a`); `TheSlot_IsItsName_AndNoFieldButTheCallersCarriesAnEmail`; `ADroppedRemoteEffort_IsRecordedAsDropped` (through the real `POST /api/reviews`: the job carries `dropped`, and the line says `dropped` with no effort though the slot's config names one); `AClampedEffort_IsRecordedAsTheServers_AndATakenOneAsTheRows` (`max` past `Coai:MaxEffort=medium` → `--effort medium`, `server`; a taken `high` → `row`); `AnOldLedgerLine_StillReads_WhatItRanWith` | RED: `Expected (… "tier", … "tierFrom") to be ("default", "server") … but found ("", "")`; `Expected Text(line, "slot") to be "a" … but ""`; `Expected job.EffortFrom to be "dropped" … but ""`; the reader's `RanWith` all empty. Break-it: the runner not passing `ranWith`/`job`/`slot` → the launch and slot tests; `ClientOptions` not setting `EffortFrom` → the dropped and clamped tests; the reader not mapping the fields → the old-line test; the tier labelled `row` → `… found ("default", "row")` |
| `usagePricing.test.ts` (+1) | `TheParser_KeepsWhatTheLaunchRanWith` — a server line keeps all eight fields, an empty `effort` is absent, a local line keeps `xhigh`/`cli-config`, an old line has none | RED: `what the launch ran with was dropped on the way in` (seven `undefined`). Break-it: `ranWithFields` keeping nothing → the same |

**NOT covered:** a consultation's, a question row's, a probe's and a chat turn's lines are not labelled (their fields are
empty — "not recorded"); claude's own settings are not read for a label; the client's line for a Team-server row records
the effort it SENT (`row`) — what the server applied is the server's line's.

## How a job ended is said, not guessed (2026-10-11, story 3.2 of `todo/PLAN_every_round_is_counted.md`)

The seam rule again: the shared half runs the real `ReviewerExecutor` over a scripted `IProcessLauncher`
(`ScriptedResults`, extracted from `EveryPaidLaunchIsWrittenTests` rather than copied a fourth time); the Team server's
half runs the real `ReviewLauncher`, `JobRunner` and `JobStore` over a child that WAITS for its token and reports the kill
as the real launcher does (`TimedOut: true, Cancelled: true`), ended by the store's cancel, the sweep or the host's token
(`WithdrawnJob`, shared with the fixture suite). The race and the thrown-at-once shape are runner rules, so their fakes
sit at `IReviewLauncher`. The shared half was RED → GREEN → RED in two plant rounds; the server half's fix landed before
its tests ran, so its RED came from three plant rounds, each test red under its own plant, then GREEN with every plant
restored and the exe REBUILT (a stale exe showed a restored plant as a failure once in this session).

| Suite | What it holds | Seen red as |
|---|---|---|
| `TheReviewerFailuresAreWrittenDownTests.EveryEndingHasACode_AskedOfTheAssembly` (existing) | the reflection census — red on the day `ReviewerOutcome.Cancelled` was added, before any wiring, as it was built to be | `Expected ReviewerNotices.ByType.Keys… to be a collection with 8 item(s) … but {7 types}` |
| `HowAJobEndedIsSaidTests` (7, mcp) | `AWithdrawnLaunch_IsItsOwnEnding_NotTheVendorsTimeout` (both flags → `Cancelled`, launch usage and ending both `NotCaptured`); `ARealDeadline_IsStillATimeout` (one flag → `TimedOut`); `AWithdrawnRemoteReviewer_StillSendsTheDelete` (the spy adapter's `AbandonAsync` called once on the `Cancelled` branch); `ACancelledEnding_HasItsOwnSentence_AndItsOwnNotice` (`cancelled`, `reviewer-cancelled`); `AConsultationLaunchKilledByItsCaller_IsStillWhatTheCallerDecided` (`ConsultFailures.Classify` hands back the `killedAs` the tokens decided — `Cancelled` and `Deadline` alike); `ACancelledTurn_IsOneLedgerLine_SayingCancelled_WithUsageNotCaptured` (`endCause` empty locally); `ACancelledRepair_KeepsTheFirstLaunchsLine` (two lines, `first` 1000 tokens + `repair` `cancelled` not captured, one turn, the sum a floor) | RED: `Expected type to be …Cancelled, but found …TimedOut` (three tests); `Expected line.Outcome to be the same string … differ at index 0` (`unknown`); `Expected …Describe(new Cancelled()) … differ at index 0`; `Expected type to be ConsultFailure+Cancelled … but found ConsultFailure+Empty`. Break-it round A (the branch unreachable for a real cancel, the sentence arm and the consultation arm removed) → the six above; round B (`AbandonAsync` dropped from the branch, the branch keyed on `TimedOut`, the `ByType` row removed, `Cancelled.Usage` defaulted to `None`) → `Expected spy.Abandoned to be 1 … but found 0`, `ARealDeadline` found `Cancelled`, `UsageNote … "" has a length of 0`, `NotCaptured to be True … found False` (twice), `ByType … to contain key Cancelled`, the census back to 7 |
| `HowAJobEndedIsSaidTests` (8, server) | `AWithdrawnLaunch_IsRecordedAsCancelled_WithUsageNotCaptured` (real launcher, `Cancelled`/`withdrawn`/*usage not captured*, under 10 s); `ARealDeadline_IsStillATimeout`; `AWithdrawalAbandonmentBackstopAndHostStop_EachCarryTheirCause` (four jobs, one kind, four causes on the record AND the line); `AJobThatExpiredInTheQueue_CarriesItsCause_AndWritesNoLine` (abandonment first; `queueExpired` from `Expiry` for a polled job whose window ran out; no file); `AThrownCancellation_WritesExactlyOneLine` (`hostStopped`); `AThrownCancellationOfAFollowUp_KeepsTheFirstLaunchsUsage` (two lines, `first` 100 `UnparseableByVendor` + `followUp` `Cancelled`/`withdrawn`, the job's usage 100 and a floor); `ACancelThatLandsWhileFinishRuns_KeepsTheStoresCause_AndTheAttemptsUsage` (the DELETE inside the launcher, the answer's 300 tokens on a `Cancelled`/`withdrawn` line, the store's record `Failed`); `AnOldLedgerLine_StillReads_AndANewOneKeepsItsEndCause` | Break-it S-A (the executor's old read, the follow-up catch skipped, the reader dropping the field): `Expected … "usageNote" to be "usage not captured" … but ""` (the deployed line's free 0/0, twice), `hostStopped.Job.EndCause … but ""`, `{"", ""} differs at index 1`, `TokensIn to be 100L … but found 0L`. S-B (`Cancel`/`Expire` without a cause, `Failed` not routing `Cancelled`, `Describe` not passing it): `EndCause to be "withdrawn" … but ""` (five), `expired[0].EndCause to be "abandoned" … but ""`, `Failure to be Cancelled … but found NotStarted`. S-C (the runner's own record written): `Text(line, "outcome") … differ at index 0` (the `ok` over the cancel), the four cause lines `{"hostStopped" ×4}` |
| `UsageLedgerFixtureTests.TheDeployedWithdrawal_WasWrittenTimedOut_AndThisServerWritesItWithdrawn` (changed on purpose; was `TheWithdrawal_IsWrittenTimedOutWithNothingCounted_WhichIsTheDefectE3Names`) | the deployed bytes still asserted as history (`TimedOut`, 0/0, no note, 8.4 s) beside the same event through this server (`Cancelled`, `withdrawn`, *usage not captured*, the deployed keys a subset of the new line's) | S-A: the new line's `usageNote` ""; S-B: `job.EndCause to be "withdrawn" … but ""`; S-C: `endCause … differ at index 0` |
| `ReviewEndpointTests.CancellingAReviewAnswersNoContentAndThenTheJobIsFailed` (+1 assertion), `http/reviews/happy.http` (+2 assertions) | the status says `endCause: withdrawn` beside the unchanged `failure: cancelled`; the queued shape carries an empty `endCause` | S-B: `after.EndCause to be "withdrawn" … but ""` |
| `usagePricing.test.ts` (+1) | `TheParser_KeepsTheEndCause` — a cancelled server line keeps `withdrawn`, an empty cause and an old line are absent | RED: `why the store killed it was dropped on the way in — actual: undefined, expected: 'withdrawn'`. Break-it: the spread fed `undefined` → the same |

**NOT covered:** a question row killed by the CALLER's cancellation (now `failed` / `cancelled` rather than
`timed_out`) — the fan-out harness cannot reach it without a launcher that reports the kill; a row past its own budget
stays covered by `OneRowPastItsDeadline_…`; the local executor's cause (none is written); a job ended by an
infrastructure throw (no line, as before); `TimedOut`'s launch usage on the server's attempt (still `None`).

## The extension's Claude model probe writes what it spent (2026-10-11, story 3.3 of `todo/PLAN_every_round_is_counted.md`)

| Suite | What it holds | Seen red as |
|---|---|---|
| `probeUsageFile.test.ts` (4) | an answered probe is one `probe` line with Claude's own usage (cache reads and writes in the input) and its billed cost; a request that did not finish (`capture`'s -1) is *usage not captured* with no cost, never 0/0; `recordingRun` writes one line per candidate it asked — a failed one included — and hands the answer back unchanged; the Rounds block's loader reads `probe-usage.jsonl` beside a missing `usage.jsonl` without calling that a failure | RED (a stub recording nothing): every assertion, `Expected values to be strictly equal`. Break-it: the wrapper appending only answered requests → `the recording run writes one line per candidate …` red |
| `theInventoryIsComplete.test.ts` | `probe-usage.jsonl` in `DATA_TO_MOVE` and in `shared/data-inventory.json` (moves, clashes) | — the existing guard, now with one more row |

**NOT covered:** `ClaudeProbeCache` and `PanelProvider.readUsage` passing the probe ledger (thin `vscode` wiring over the
tested wrapper and loader).

## Epic 3's code round (2026-10-11, `todo/PLAN_every_round_is_counted.md`)

Three accepted findings of the gate's code round for epic 3/6; two were rejected with reasons (the plan's deviation
table says which).

| Suite | What it holds | Seen red as |
|---|---|---|
| `EveryPaidLaunchIsWrittenTests.ACancelDuringTheLaddersWait_KeepsTheLaunchesAlreadyPaid` (mcp) | a first launch that billed 1000 and a rate-limited repair, then the round cancelled while the ladder WAITS (cancelled from the progress note that announces the wait — no timing): the turn keeps 1000, two lines `first`/`repair`, the repair's line *rate limited*, the lines summing to the turn | RED before the fix: `Expected outcome.LastTurnUsage.TokensIn to be 1000L … but found 0L` (the cancellation threw out of `Task.Delay` and the paid launches went with the ladder's locals) |
| `TheCanaryFallbackRunsTests` (3, server) | the REAL `canary` function of `deploy/systemd-release.sh`, SOURCED (the script now stops after its functions when sourced), against a stub `curl` first on PATH and the real Python 3: an old server refusing the kind is asked twice, the second time without `"kind"`; a new server once; any other 400 once, and the canary fails. Needs bash and a Python 3 (`ShellScripts.Python` skips the Store's `WindowsApps` alias); skipped where either is missing | Break-it, one plant at a time from a byte copy, restored and hash-checked: the retry condition never matching → `Expected code to be 0 because … HTTP 400`; every 400 retried → `Expected posts to contain a single item … but found {…canary…, …plain…}`; the sourcing guard removed → all three red, `release: usage: …` (sourcing ran the release) |
| `TheImageChecksEachInstallerTests` (behaviour unchanged) | its bash, path and repo-root helpers moved into `ShellScripts`, shared with the canary suite rather than copied | — a refactor; the four tests pass as before |

`UsageLedger.RecordJob` lost its `LaunchOptions? ranWith = null` for two overloads (the short one for a consultation,
a question row and a probe; the full one for the Team server with a non-null `LaunchOptions`) — no behaviour changed,
so no test was added; every existing caller compiles against the overload it meant.

**A hang the first CI run found (PR #761).** On Linux and macOS `ShellScripts.Python()` answered the bare name
`python3`, and the suite's `python3` shim — first on PATH — ran `exec 'python3'`, which found the shim itself: an
endless exec, and `TheCanaryFallbackRunsTests` held both CI legs to their 30-minute limit (the log shows the old-server
case active for the whole half hour). Reproduced here in Git bash (a shim that execs its own name: `timeout 5` → exit
124). Fixed two ways: `Python()` now answers an ABSOLUTE path on every OS (a PATH search for `python3`, or a real
`python.exe` on Windows), and the helper asserts that before anything runs and reads both streams without blocking, with
a 60 s bound that kills the tree. Break-it: `Python()` planted to return `python3` → all three red in 1 s, `Expected
Path.IsPathRooted(python) to be True … but found False` — a failure, no longer a hang.

## The server reads a stated duration (2026-10-11, story 5.3a of `todo/PLAN_every_round_is_counted.md`)

The scenario reaches as deep as the Team server's harness goes without a product seam: the real `JobRunner`,
`ReviewLauncher`, antigravity adapter and `SlotRegistry` on disk, with a fake `IProcessLauncher` standing in for the
paid agy process. Run: `./src_server/tests/bin/Debug/net10.0/CoaiServer.Tests.exe --filter-class "*AStatedResetParksTheSlotTests"`;
`CooldownParserTests` moved with the parser to the mcp suite in story 5.3b
(`./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*CooldownParserTests"`, section below).

| Suite | What it holds | Seen red as |
|---|---|---|
| `CooldownParserTests` (+8) | `AStatedWaitInWords_ErrsLong_AndWeeklyStillWinsWhenLonger` — `Resets in one hour` errs long to `MaxGuess`, and a line that also says "weekly" takes the longer day (CodeRabbit on #754; RED: `… but <… 12:30:00 +0h>`, the 30-minute retry). `ASpacedDuration_IsReadWhole_NeverAsItsFirstPart` — `2h 30m` and `1h 2m 3s.` read whole, `2h 30 minutes` errs long to `MaxGuess` (CodeRabbit on #754; RED: `2h 30m` read as two hours, `… but <… 14:00:00 +0h>`; break-it: the trailing `(?!\s*\d)` removed → `2h 30 minutes` read as two hours again). `ADurationWithTextGluedToItsUnit_IsNoDuration` — `Resets in 2seconds` and `1hgarbage` err long to `MaxGuess`, not a short wait; a full stop is a boundary (the code round; RED: `… but <2026-09-06 12:00:02 +0h> does not`). `CooldownParser_ReadsResetsIn88h49m9s` and `…33m52s` on the two verbatim agy lines (RESULTS §3.1, the fixture); a duration of one part (`5h.`, `45s`, `12m`); `ADurationNoVendorWindowCouldMean_IsTheFallback_NeverAThrow` — `999999999999999999h` and `9999h` err long to `MaxGuess` (a wait was stated), `336h` is exactly the 14-day ceiling; `ADurationIsNotMistakenForAClockTime` — `Resets in 9h30m` is nine and a half hours, `resets 9:30pm (UTC)` still a clock time | RED: every duration read as the 30-minute guess (`… but <2026-09-06 12:30:00 +0h> does not`). Break-it: the duration branch disabled in `NamedInstant` → the same five red |
| `AStatedResetParksTheSlotTests` (2) | the verbatim refusal through the real runner parks the slot for 88h49m9s, READ BACK FROM DISK, and a second queued job launches nothing; with `Resets in 2s` the next job waits, then launches once the two seconds pass — the positive control the cadence consultation asked for, so a slot parked for good cannot pass | RED: `parked!.Value to be within 30s from <… +88h49m9s> … but <… +30 min> was off by 3d, 16h…`; `Expected launched to be True because after the stated two seconds the account is used again — not parked on a 30-minute guess, but found False`. Break-it: the same plant → both red |

**NOT covered:** the HTTP host (`Program.cs` builds the launcher inline — a vendor double there needs a product seam);
a real agy refusal on the deployed box — checked live at the next deploy.

## The server records the round (2026-10-11, story 4.2 of `todo/PLAN_every_round_is_counted.md`)

The request half goes through the REAL `POST /api/reviews` with RAW JSON bodies (a typed client cannot send `{}` where a
string goes); the line half runs the job the endpoint STORED through the real `JobRunner` and `UsageLedger`, with
`EveryServerLaunchIsWrittenTests.BySlot` at `IReviewLauncher` (made `internal` to be shared rather than copied). The first
red run was against a NAIVE binding — `string? Round`, `string? Attempt`, `int? RemainingSeconds` on the request, the
shape amendment 3 warns of — so the matrix saw the real symptom, not a missing member. Run:
`./src_server/tests/bin/Debug/net10.0/CoaiServer.Tests.exe --filter-class "*TheServerRecordsTheRoundTests"`.

| Suite | What it holds | Seen red as |
|---|---|---|
| `TheServerRecordsTheRoundTests` (52, server) | `AMalformedCountingField_IsAcceptedAndDroppedWithANote_NeverRefused` (32 rows: per field `0`, `-1`, `"oops"`, `{}`, `1.5`, an oversized value, and the near misses — for `round` `[]`, `true`, `""`, 31/33 characters, a non-hex `g`, a 4 KiB string, a 33-digit number; for `attempt` `""`, `"rotated"`, `"First"`, a 4 KiB string; for `remainingSeconds` `"60"`, `60.0`, `2147483648`, `99999999999999999999`, `1e400` — each 202, ONE note naming the field, under 300 characters, and the job's three fields empty); `AnAbsentOrNullField_IsAnOldClient_StoredEmpty_WithNoNote` (6); `AWellFormedValue_IsTaken_WithNoNote` (9 — an upper-case round kept in lower case; the four client words; `1`, `50`, `int.MaxValue`); `ARepeatedSubmit_WithLessTimeLeft_IsTheSameJob`; `AMalformedRound_IsDroppedWithANote_AndTheReviewRuns` (the plan's name: `round: "oops"` beside a valid `attempt: repair` — one note, the job `Done`, its line `round: ""`, `clientAttempt: repair`, `attempt: first`); `AValidRound_ReachesEveryLineOfTheJob_BesideTheServersOwnAttempt` (slot a rate-limits, b answers: both lines carry the round and `clientAttempt: retry`, both `attempt: rotated`); `AnOldClient_IsStoredEmpty_AndItsLineSaysNoRound`; `AnOldLedgerLine_StillReads_AndANewOneKeepsItsRoundAndClientAttempt` | RED against the naive binding (45 of 52): eighteen matrix rows `Expected response.StatusCode to be HttpStatusCode.Accepted {value: 202} because a counting field never refuses a review — the server answered {"error":"internal error"}, but found HttpStatusCode.InternalServerError {value: 500}` (every non-string for `round` and `attempt` — `0`, `-1`, `{}`, `[]`, `1.5`, `true`, the 33-digit number; for `remainingSeconds` `"oops"`, `{}`, `1.5`, `60.0`, `2147483648`, `99999999999999999999`, `1e400`) and the rest `Expected accepted.Dropped to contain a single item … but found <null>`; the well-formed rows `Expected (job.Round, job.ClientAttempt, job.RemainingSeconds) to be equal to …`; the line `… {"<absent>", "<absent>"} do(es) not match`; the reader `Expected (scan.Lines[1].Round, …) to be equal to …`. The absent/null rows and the repeat passed from the start (guards). Break-it round 1, all from byte copies at once (the notes not merged into `dropped`; the remaining seconds put into the idempotency fingerprint; the runner writing an empty round; the reader dropping `round`; the round kept in its sent case; a JSON `null` read as sent) → 37 red: every malformed row `… Dropped … but found <null>`, `ARepeatedSubmit` `… the idempotency key 'round-turn-1' was already used for a different request`, the line `{"", ""} do(es) not match`, the upper-case row, the reader — and **the null plant observed nothing**: `JsonElement?` receives JSON `null` as a C# `null`, so the `Null`-kind arm is a guard for a non-nullable binding, not a tested path. Round 2 (the client's word written OVER the server's `attempt`) → `… attempt == "rotated" … but {"retry", "retry"}` and `Expected Text(line, "attempt") to be "first" … but "repair"`. Restored, hash-checked, touched (a `Copy-Item` restore keeps the backup's timestamp and MSBuild skipped the compile — the first "green" after restoring still showed round 2's two failures), REBUILT → 52 green |
| `http/reviews/happy.http` (+4 requests) | `a_review_carrying_its_round_is_accepted_with_nothing_dropped` (`dropped: null`) and `malformed_counting_fields_are_dropped_with_a_note_never_refused` (`round: "oops"`, `attempt: {}`, `remainingSeconds: 1.5` → 202, notes for exactly those three fields), each then withdrawn — through the source-generated binding (`JsonSerializerIsReflectionEnabledByDefault=false`) the in-process host does not use | Break-it: the server built with the notes not merged → `TypeError: Cannot read properties of null (reading 'map')`. The runner reports a failed `test()` block as httpyac exit 20 → its exit **4** ("misconfigured"), not 1 — a pre-existing quirk of `run-contracts.mjs`, not changed here |
| `usagePricing.test.ts` (+1) | `TheParser_KeepsTheRoundAndTheClientAttempt` — a server line keeps `round` and `clientAttempt` beside `attempt: rotated`; an empty round and an old line read both absent | RED: `the round the line holds was dropped on the way in — actual: undefined, expected: '0123456789abcdef0123456789abcdef'`. Break-it: `roundFields` mapping the round to nothing → the same |

`EveryServerLaunchIsWrittenTests.BySlot` went from `private` to `internal` (behaviour unchanged).

**NOT covered:** the client side (story 4.1); any use of `remainingSeconds` (story 4.3); the status DTO does not echo the
fields, by decision, so nothing asserts their absence there.

## The server image checks each installer before it runs it (2026-10-11)

| Suite | What it holds | Seen red as |
|---|---|---|
| `TheImageChecksEachInstallerTests` (4, server) | `NoInstallerIsPipedIntoAShell_EachGoesThroughTheCheckedScript` — no `curl … \| sh\|bash` left in `src_server/Dockerfile`, the codex and claude installer URLs run through `install-cli.sh`, which the image COPYs, and agy through `install-agy.sh` (COPYed, followed by `agy --version`, no `https://antigravity.google` left — updated 2026-10-11, RED against the old Dockerfile: `Expected dockerfile … to contain "COPY src_server/install-agy.sh"`); `APageIsRefused_TriedAgain_AndNamed_NeverRun` — the REAL script with bash against a `file://` HTML page: three attempts, each naming what arrived, exit non-zero, nothing run; `AScriptIsRun_WithTheShellNamed` (and each fetch is announced BEFORE it starts — the code round: a stalled fetch is silent for up to five minutes; RED: `Expected stderr "" to contain "fetching"`); `AFetchThatFails_IsTriedAgain_AndThenFailsTheBuild`. Bash on CI's Linux and macOS legs, Git for Windows' bash here; `INSTALL_CLI_PAUSE=0` removes the pause | RED: the script missing (`No such file or directory`) and the Dockerfile still piping. Break-it: the shebang check replaced by `true` → the page was RUN — `syntax error near unexpected token`, the 2026-10-10 failure reproduced |

**NOT covered:** a real vendor URL (the suite does not reach the network); the image build itself (no daemon here —
the release workflow's image legs build it, and since 2026-10-11 `.github/workflows/server-image.yml` builds it on a pull
request that touches the Dockerfile or either installer script).

### agy installs from its release manifest, checked (2026-10-11)

`antigravity.google/cli/install.sh` answered HTML on GitHub's runners (release run 38075423539), so agy left
`install-cli.sh` for `src_server/install-agy.sh`, which reads Google's manifest (`{version, url, sha512}`) directly.

| Suite | What it holds | Seen red as |
|---|---|---|
| `TheImageInstallsAgyFromItsManifestTests` (12, server) | The REAL `install-agy.sh` with bash, `HOME` in a temp dir, `AGY_MANIFEST_BASE` at `file://` fixtures, `INSTALL_CLI_PAUSE=0`, stub `uname`/`ldd` first on PATH (so the machine is the test's choice); the tarball (one `antigravity`, mode 0755) and its SHA-512 are built in C# (`TarWriter` + `GZipStream`), so the test needs no `tar`/`sha512sum` of its own. `AManifestWhoseHashMatches_InstallsAgy_AndItRuns` — installed at `~/.local/bin/agy`, executable (unix mode checked off Windows), `agy --version` answers, each step announced; `APayloadWhoseHashDiffers_IsRefused_AndNothingIsInstalled`; `APageInPlaceOfTheManifest_IsRefused_Shown_AndTriedAgain` — three refusals, `<!doctype html>` shown; `AManifestWithoutAChecksum_IsRefused`; `AMissingManifest_IsTriedAgain_AndThenFails`; `AMissingPayload_IsTriedAgain_AndNothingIsInstalled`; `ARawBinaryPayload_IsInstalledAsItIs` (Google's non-`.tar.gz` branch); `EachMachineFetchesItsOwnManifest` ×4 — x86_64/aarch64 × glibc/musl → `linux_amd64`, `linux_arm64`, `linux_amd64_musl`, `linux_arm64_musl` (only that manifest exists); `AnUnsupportedMachine_IsRefusedByName_BeforeAnyFetch` (`armv7l`) | RED: all 12 against the missing script — `bash: …/install-agy.sh: No such file or directory`, exit 127 (`Expected code to be 0 … but found 127`; the refusal cases `Expected RefusalLines(…) to be 3, but found 0`). Break-it from a byte copy: the SHA-512 check replaced by `if false` → `APayloadWhoseHashDiffers…` red (`Did not expect code to be 0`); the manifest check reduced to `true` → `APageInPlaceOfTheManifest…` and `AManifestWithoutAChecksum…` red (the page was taken as a manifest); the `ldd … musl` detection removed → both `_musl` cases red. Restored, green 16/16 with the neighbour |

**NOT covered here:** the real manifest and tarball (no network in the suite — `server-image.yml` installs the real one on
both architectures); the script under `dash` (the image's `sh`) is syntax-checked only (`dash -n`), the suite runs it
with bash.

### The image job runs the image and uses the server (2026-10-12)

`server-image.yml` used to stop after the build (CodeRabbit on #775: a delivery check must USE the capability). Each leg
now loads the image and runs `src_server/smoke-image.sh`: the contract runner's Local sign-in configuration, a seeded data
directory, `/api/health` bounded, a signed-in `GET /api/usage` asserted, each CLI's `--version` inside the container.

| Suite | What it holds | Seen red as |
|---|---|---|
| `TheImageSmokeUsesTheServerTests` (4, server, `ServerCollection`) | The script SOURCED for its functions, or run whole with a `docker` stub first on PATH that records every call and the key it saw in its environment. `TheTokenItMints_IsAcceptedByTheRealServer_AndTheSeededLedgerIsWhatItChecks` — `new_key` → `TeamServer` with that key, `seed` into its data dir, `mint` for `dev@example.com` → the in-process server answers `GET /api/usage?window=year` 200, the script's `check_usage` passes that body and fails `{"scope":"me","vendors":[]}`; `EachRunHasItsOwnKey_LongEnoughForTheServer` — two keys differ, each ≥ 32 bytes; `WithoutAnImage_ItRefuses_AndDockerIsNeverAsked`; `AStartThatNeverAnswers_ShowsTheContainersLog_StopsIt_AndNeverPutsTheKeyOnAnArgumentList` — `SMOKE_PORT=1`, `SMOKE_WAIT_SECONDS=2`: non-zero, the stub container's log printed, `run … --rm`, `stop … <that id>`, `-e Auth__Local__SigningKey` by name, the key in docker's environment but in no argument and in no output (`GITHUB_ACTIONS=false`, so the `::add-mask::` line is not printed) | RED: all 4 against the missing script — `bash: …/smoke-image.sh: No such file or directory` (`new_key: command not found`; `Expected output … to contain "usage"`; `… to contain "the stub server said boom"`). Break-it from a byte copy: the HMAC keyed with `"$1x"` → the real server answered 401 (`Expected … to be 200 … but found 401`); the key passed as `-e Auth__Local__SigningKey="$key"` → the argument-list case red; the log dump removed → `… to contain "the stub server said boom"` red. Restored, green |

**NOT covered here:** a real container (no docker daemon in the suite) — the `server-image` job's two legs run it on
every pull request that touches the image's files or `smoke-image.sh`.

## A round has an identity on the wire (2026-10-11, story 4.1 of `todo/PLAN_every_round_is_counted.md`)

The client half of epic 4. The seam: the real `BoundedScheduler`, `ReviewerExecutor` and `RemoteRuntime` over a
recording `IProcessLauncher` for what the parent decides; the real `--ask-remote` code (`AskRemote.RunAsync` over
`Program.Flags`, in-process) against `ApiEndpointStub` (widened with `Origin`) for what reaches the wire; the real binary
once; the real `PanelService` and engine for the minting. Every behaviour was RED for its real symptom on inert stubs
(the new members present, nothing wired), then GREEN, then RED again under four plant rounds from byte copies — each
file hash-checked on restore and the exe rebuilt before the final green (43 of 43 across the five classes). Run:
`./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*ARoundHasAnIdOnTheWireTests" --filter-class "*ARoundIsMintedAKeyTests" --filter-class "*TheRoundKeyIsStepTwentyOneTests"`;
the probe: `cd src_vs_code && npm run test:contract` (starts the server this tree built).

| Suite | What it holds | Seen red as |
|---|---|---|
| `ARoundHasAnIdOnTheWireTests` (9, mcp) | `TheBody_CarriesRoundAttemptAndRemainingSeconds_EachLeftOutWhenEmpty_AndTheContractStaysTwo` (the bare body byte for byte what contract 2 sent; 0 and −5 never serialised; `ContractVersion` 2); `RemainingSeconds_IsTheWholeSecondsLeft_AndNeverBelowNought`; `ARetryARepairAndAFollowUp_ShareTheRoundsId` (a rate limit → the ladder's retry → an unparseable answer → the repair: three recorded launches all `--round <key>`, attempts `first`/`retry`/`repair`; agy's denied-command follow-up keeps `Round`; a remote launch stamped `followUp` says so); `AShortenedLaunch_AdvertisesItsEffectiveBudget_NotTheOneItWasBuiltWith` (600 s built → `Lesser(40 s)` → `--timeout-seconds 30`, `--vendor-timeout-seconds 40`, the POST's `remainingSeconds` 25–30, `timeoutSeconds` 40, `attempt` repair); `TheShimsOwnWait_FollowsTheShortenedDeadline_NotTheOneItWasBuiltWith` (600 → 16 s against a server that never finishes: exit 78 in 3–30 s, the DELETE sent); `AFreshThirtySecondReviewer_AFreshSixtySecondReviewer_AndARepairWithFortySecondsLeft_EachSubmitAPositiveBudget` (the plan's MUST LAUNCH set: 15–20, 45–50, 25–30); `AnExhaustedBudget_IsNeverSubmitted_AndNeverSerialisedAsNought` (`--timeout-seconds 0` by hand: exit 78, the sentence, the stub saw nothing); `WithFlag_ReplacesAValueInPlace_OrAppendsThePair` | RED: `--round` `{"<absent>", "<absent>", "<absent>"}`; the body `… to contain "round":…`; `--timeout-seconds` `"590"` where `"30"` and `"6"` were expected; `remainingSeconds` `-1`; the exhausted shim `Expected exit to be 78 … but found 69` (it POSTed with a spent budget and the request was cancelled). Plant A (`ForLaunch` returning the invocation unchanged) → the same five, the MUST LAUNCH repair row advertising **589**; plant E (the refusal skipped) → exit 69 again; plant F (the ladder's and the executor's attempt stamps removed) → `{"first", "first", "first"}`; plant G (the body serialising nulls) → the body test and `remainingSeconds -1` in both shim tests |
| `ARoundIsMintedAKeyTests` (3, mcp, collection `fakecli-env`) | `ARound_IsWrittenWithItsKey_InTheSessionFileAndTheDatabase` (a real plan round: `status`'s `rounds[].key` well-formed and equal to `round_key`); `EveryLaunchOfTheRound_IsStampedWithTheRecordsKey_ASecondTurnIncluded` (the engine through a hand-built `StageRun` with a spy adapter whose `ForLaunch` records what it was handed, and a continuation asking one more turn built WITHOUT a key: both launches carry the record's key); `Again_GetsANewId` (§9.6's sequence — round 1, resolve, a new commit, `again`: two `CodeReview` rows with two well-formed, distinct keys, the session file naming the same) | RED: `IsWellFormed(inFile) … got ''`; `no such column: round_key`. Plant B (the engine not calling `InRound`) → `{"", ""} differs at index 0`; plant C (the turn loop handing the next turn as built) → `{"<key>", ""} differs at index 1`; plant K (`LiveRound` not writing `Key`) → `{"", ""} do(es) not match` and `IsWellFormed … False` |
| `TheRoundKeyIsStepTwentyOneTests` (8, mcp) | step 21 by position (index 20, `Steps.Length` 21); a step-20 file gains the column and its older rounds read `""`; a recorded round carries its key and one without reads empty; a key is 32 lower-case hex and two mints differ (upper case and a `g` refused); `--round-counts` (`RoundCounts.Read`) on a step-20 file reads and leaves it at twenty; both preview forks reach the head; a second open re-runs nothing | RED: `KeyStep to be 20 … but found -1`; `no such column: round_key`; the forks' `Shape()` short of the head. Plant D (the step out of `Steps`) → the same |
| `RemoteShimScenarioTests.TheRoundTheAttemptAndTheSecondsLeft_GoOut_WithTheRealBinary` (+1) | the REAL `coai-mcp --ask-remote` over the argv `ForLaunch` built for a repair of a 600 s reviewer with 40 s left: `--timeout-seconds 30`, the POST carrying `round`, `attempt: repair`, `remainingSeconds` 20–30 (a cold .NET start inside), `timeoutSeconds` 40; `RunAsync` gained an optional `shaped` step | Plant A → `… {…"--timeout-seconds", "590", …} to contain items {"--timeout-seconds", "30"} in order` |
| `TheReviewersModelIsStepTwentyTests.TheModelColumnAndTheIndex_AreTheTwentiethStep_AppendedNeverInserted` (changed on purpose) | `Steps.Length` ≥ 20 — the head is step 21's own test to pin | RED before the change: `Expected Schema.Steps.Length to be 20, but found 21` |
| `ThePairsThemselvesTests.ADatabaseFromBeforeComments_GainsThem_AndEveryPairSaysNothing` (changed on purpose) | the rewind to step 12 drops `rounds.round_key` too, as it drops every column a later step added, so the reopen can re-run step 21 | RED in the full suite (8703 total, this one failed) and alone: `System.NullReferenceException` on `db.Pairs` — the reopen's step 21 threw on a column still there, and `RoundsDb.Open` answers null to a migration that throws, by design. The sweep for the shape (`DROP COLUMN` across the tests) found one other rewind, `SecurityEvidenceOnAnOlderDatabaseTests`, which stops at step 17 and passed |
| `reviewBody.contract.ts` (2, extension, `npm run test:contract`) | `TheNewBody_IsAcceptedByTheLiveServer` — the new body plus `memberNoServerKnows` with `"kind":"probe-no-such-kind"` against the REAL server the runner started: 400 naming *'probe-no-such-kind' is not a kind of job* — bound past the new members, nothing queued; the control with `timeoutSeconds: "x"`: a 400 that does NOT say *not a kind of job* (the kind is judged after binding) | — both pass on the first run: the server at HEAD skips unknown members by construction (`ServerJsonContext.cs:252`), which is the measurement the client ships on; a server that refused unknown members would fail the first by its message. Not yet run against the DEPLOYED server (`COAI_CONTRACT_URL` + its key + `COAI_CONTRACT_ALLOW_REMOTE=1`) |

**NOT covered:** a `PanelService` round with a REMOTE row through a stub Team server (the engine-level test proves the
stamp through a spy adapter, the wire through the real executor and the real shim — the two are not joined in one run);
the local ledger line (carries no round yet); a feature-stage follow-up turn through the real stage (`OneMoreTurn`
stands in for `SourceConversation`); the server's reading of the fields and its refusal (stories 4.2 / 4.3).

## No start without budget (2026-10-11, story 4.3 of `todo/PLAN_every_round_is_counted.md`)

The rule is the store's (`JobStore.TryClaim`) and the line is the runner's, so the fakes sit at `IReviewLauncher` — a
launcher that COUNTS, because the guarantee is that nothing launched. The wire half goes through the REAL client code:
`RemoteRuntime.Build` → `ForLaunch` → `AskRemote.RunAsync` over `Program.Flags` in-process (the server tests now reference
`CoaiMcp.csproj`, with `InternalsVisibleTo`) → its POST captured by `ShimCapture`, a loopback `HttpListener` in the server
tests → the captured body replayed to the real `POST /api/reviews` → `TryClaim` at once. Capture-and-replay rather than a
socket on the in-process host: the in-process host has none, and "`RemoteRuntime` → captured POST → immediate claim" is
E4's plan round's own wording. Every behaviour was RED on the inert shapes first (the enum value, the policy, the
deadline field and the claim result present, nothing wired), then GREEN, then RED again under three plant rounds from
byte copies, each file hash-checked and TOUCHED on restore (a `Copy-Item` keeps the backup's timestamp) and the binaries
rebuilt before the final green. Run:
`./src_server/tests/bin/Debug/net10.0/CoaiServer.Tests.exe --filter-class "*NoStartWithoutBudgetTests" --filter-class "*TheCallerCanStillWaitTests"`
and `./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*TheServerRefusesForBudgetByNameTests"`.

| Suite | What it holds | Seen red as |
|---|---|---|
| `NoStartWithoutBudgetTests` (10, server) | `AJobWithoutBudget_IsEndedWithoutStartingTheVendor` (12 s stated, 5 off, 7 left, 10 needed: the pump returns false, the counting launcher saw nothing, `Failed`/`NoBudget`, the reason names *at least 10s*, a long poll started before the pump completes within 2 s, and exactly ONE ledger line — `NoBudget`, 0/0, no `usageNote`, 0 s, `kind: review`, the round and `clientAttempt: repair` kept, `turn` the job id, `attempt: first`, empty `slot` and `endCause`); `TheClaimMovesOnToTheNextJob` (a spent job and a fine one: one pump launches the second only, two lines `NoBudget`/`ok`); `AnOldClient_IsNeverRefusedForBudget` (an hour's allowance, `remainingSeconds` 0: no deadline, launched); `ADeleteBeforeTheClaim_LaunchesNothing` (amendment 5, through the counting launcher: the cancelled job is never claimed, no file); `TheRunBudget_IsCappedAtWhatIsLeft` (600 s asked, 100 stated, claimed 20 s later → 75 s, `RunDeadlineUtc` from it); `AStatedBudgetLongerThanTheJobsOwn_LeavesTheRunBudgetAlone`; `AHugeRemainingSeconds_IsCappedAtTheJobsOwnLifetime` (`int.MaxValue` → 60 s + 10 min − 5 s); `AJobAtTheEdge_IsRefused_AndOneJustAboveItLaunches` (14 s refused, 15 s launched, fixed clock); `TheNoBudgetReason_MatchesNoRateLimitPhrase` (the store's own reason inside the new shim's sentence at exit 79 and the old shim's at exit 70: `RateLimit.Hit` false, `Hopeless` false); `TheSettings_AreRefusedAtBoot_WhenTheyCouldNeverBeRight` (`From(-1, 10)`, `From(5, 0)` throw naming the key; `From(0, 1)` is a decision; the default is 5/10) | RED on the inert shapes: `Expected (runner.PumpAsync(…)) to be False because nothing could be started, but found True`; `Expected launcher.Launches to be equal to {<the fine job>} … but {<the spent job>} differs at index 0`; `Expected 1m and 15s … but found 10m`; `Expected job!.ClientDeadlineUtc to represent the same point in time as <2026-10-11 12:10:55 +0h> … but found a <null> DateTimeOffset`; `Expected refused.Job to be <null>, but found CoaiServer.JobRecord`; the phrase test `System.InvalidOperationException : Sequence contains no elements` (no refusal to read). The old-client, the DELETE, the longer-budget and the settings tests passed from the start (guards) |
| `TheCallerCanStillWaitTests` (3, server, collection `coai-server`) | `AClientClockTenMinutesOff_GetsTheSameDeadline` (two real POSTs, the second carrying `submittedAtUtc`, `clientNowUtc`, `deadlineUtc` and `utc` ten minutes off: both deadlines inside `[before, after] + 50 s − 5 s` of the server's clock); `AFreshThirtySecondReviewer_AFreshSixtySecondReviewer_AndARepairWithFortySecondsLeft_EachLaunch` (the MUST LAUNCH set: three real shim runs, three captured bodies, each submitted to the real endpoint and claimed at once — `Refused` empty, the job claimed, a positive run budget); `TheConfiguredAllowance_IsWhatTheClaimReads` (the host with `Coai__MinStartSeconds=3600`: a 50 s budget is refused, the reason names *at least 3600s*, `GET /api/reviews/{id}` says `failed` / `no_budget` / empty `endCause`) | RED: `Expected plain.ClientDeadlineUtc to have a value because a stated budget gets a deadline, but found <null>`; `Expected claim.Job to be <null>, but found CoaiServer.JobRecord`. The MUST LAUNCH set passed from the start — its teeth are plant S-B's |
| `TheServerRefusesForBudgetByNameTests` (4, mcp) | `TheShim_ReadsNoBudgetByName_AndExitsItsOwnCode` (the stub answers `failed` / `no_budget`: exit 79, the sentence, no DELETE); `TheShim_ReadsTheName_NotTheSentence` (the server's sentence under `timed_out` → 70; the name under any sentence → 79); `AnOlderShim_RendersTheNameInItsSentence_AndNothingThrows` (`ReadPoll` → `Failed`, `Decide` → `Finish`, `FailedMessage` carries *(no_budget)*); `NeitherShimsSentence_IsReadAsARateLimit` | RED: `Expected exit to be 79 … but found 70 (difference of -9)` (twice). Plant M-A (the name test made unreachable) → the same two |
| `JobStoreTests`, `AbandonedJobTests`, `IdempotencyTests` (changed on purpose) | `TryClaim` answers a `ClaimOutcome` — `.Job` / `.Token` / `.Refused` — where a nullable tuple stood; ten call sites rewritten, no behaviour changed | — |
| `http/reviews/happy.http` | `@uncovered` for `failure: "no_budget"`: a claim needs a signed-in account and the suite's stack has none, by design; the in-process host test above proves the name on the wire | — |

**Break-it, from byte copies.** Round S-A (`CanStart` → `true`; the lifetime cap removed; the run budget not capped) →
7 red: the five refusal tests as on the inert shapes, `AHugeRemainingSeconds` `… but <2094-10-29 15:14:02 +0h> does
not` (sixty-eight years of `int.MaxValue`), `TheRunBudget` `found 10m`. Round S-B (`DefaultMinStartSeconds` 60 — the
plan's refuted floor; the pass returning after its first refusal; `RefusedForBudget` writing nothing; the margin not
subtracted; *(quota)* appended to the reason) → 9 red: the MUST LAUNCH set `Expected claim.Refused to be empty because a
fresh 30 s reviewer MUST LAUNCH; it advertised 19 s and was refused, but found at least one item` — the consultation's
prediction, measured; `TheClaimMovesOnToTheNextJob` `… to be True because the second job could start, in the same pass,
but found False`; `AJobWithoutBudget` `FileNotFoundException : Could not find file '…\usage.jsonl'`; the clock test `…
to be on or before <… 16:37:58> … but it was <… 16:38:03>` and `AHuge` `<… 12:11:00>` (five seconds, the margin);
`TheRunBudget` `found 1m and 20s`; `TheNoBudgetReason` `Expected RateLimit.Hit(…) to be False … but found True`; the
edge and the settings tests. Round M-A (the shim's name branch made unreachable) → the two shim tests. Every file
restored, hash-checked against its copy, touched, both test projects rebuilt → 13/13 and 4/4.

**NOT covered:** the real `coai-mcp` BINARY against the real server (the shim runs in-process, as 4.1's tests run it;
the binary's own cold start — up to ten seconds under load on this machine — is what makes a fresh 30 s reviewer's
advertised budget vary between runs, and the rule's margin is sized for the in-process number); the host's OWN pump
claiming a job (no slot is signed in on the host's registry in any server test — the claim is the test's); a sweep
over `ClientDeadlineUtc` (none exists, by decision); the extension's `remoteAsk.ts` on the new name (remote CHAT, not
reviews — read: it renders any `failure` string inside a sentence; no test added, `src_vs_code` untouched); the
deployed server (the rule ships with E5's deploy).

## Epic 4's code round (2026-10-11, `todo/PLAN_every_round_is_counted.md`)

One gating finding, accepted: `JobRecord.RemainingSeconds = 0` meant both "the request sent no budget" and "it sent a
malformed one that was dropped" — two facts as one value. `StatedBudget` (`src_server/src/Jobs/StatedBudget.cs`) is now
`Absent`, `Stated(seconds)` or `Dropped(reason)`, the reason being the request's own note; only a STATED budget makes
`ClientDeadlineUtc`, so behaviour is unchanged.

| Suite | What it holds | Seen red as |
|---|---|---|
| `TheServerRecordsTheRoundTests` (changed on purpose) | the malformed matrix also asserts `StatedBudget.State` `Dropped` (for `remainingSeconds`) with the note's reason, `Absent` otherwise; absent/null asserts `StatedBudget.Absent`; a well-formed value asserts `Stated` with its seconds | RED on the inert shape (the type wired through, `RoundFields` always answering `Absent`): `Expected job.StatedBudget.State to be StatedBudgetState.Dropped {value: 2} … but found StatedBudgetState.Absent {value: 0}` for every malformed `remainingSeconds` row; GREEN 65/65 with the budget tests, server suite 530/530 |
| `NoStartWithoutBudgetTests` (helper only) | its job builder states a budget through `StatedBudget.Of`, `Absent` for none | — unchanged behaviour |

## The server counts rounds, and the Team server tab shows them (2026-10-11, stories 5.1 and 5.2 of `todo/PLAN_every_round_is_counted.md`)

Every behaviour was RED on an inert shape first (the fields present and always 0, `UsageWindow.Several` answering no
windows, `5h`/`24h` not yet names; `serverRounds` answering `unavailable`, `teamRoundsBlock` drawing nothing, the
company route unchanged), then GREEN, then RED again under plants from byte copies, each restored and TOUCHED and the
binaries rebuilt (`dotnet build src_server/tests`, `npx tsc -p .`) before the final green. Run:
`./src_server/tests/bin/Debug/net10.0/CoaiServer.Tests.exe --filter-class "*RoundCountsTests" --filter-class "*SeveralWindowsTests" --filter-class "*RoundCountsEndpointTests" --filter-class "*TheWindowAnswerKeepsItsBytesTests"`,
`node --test out/test/teamRounds.test.js out/test/teamServerApi.test.js out/test/teamServerTabPage.test.js` in
`src_vs_code` after `npm run compile`, `node http/run-contracts.mjs`, and `npm run test:seam` (the usage-counts leg).

| Suite | What it holds | Seen red as |
|---|---|---|
| `RoundCountsTests` (10, server) | `FourRolesOfOneRound_CountAsOneRound`; `ARetryAndARepair_AddNoRound_AndAreCountedOncePerRequest` (a retry job of two lines — `RateLimited`/`rotated` then `rotated` — is ONE retry); `AClientFollowUp_IsCounted_AndTheServersOwnFollowUpAndRotationAreNot`; `LinesWithoutARound_AreCountedApart_NeverFoldedIntoAGuess` (a chat is not one); `TheCanary_IsNotARound_AndNotARunWithoutARound` (`launchesByKind` canary 2); `CancelledAndBudgetRefusedLines_AreCountedByTheirOutcome`; `LaunchesPerKind_LeaveOutWhatNeverLaunched` (a `NoBudget` line); `ALineFromBeforeTheRoundFields_IsARunWithoutARound`; `AClientWordWithNoJob_IsOneRequestPerLine`; `EachPersonsVendors_CarryTheSameCounts` | RED on the inert shape: `Expected codex.Rounds to be 1 because one round launches four reviewers on a code stage, and it is still one round, but found 0`; `Expected codex.FollowUps to be 1 … but found 0`; `Expected codex.RunsWithoutRound to be 2, but found 0`; `Expected codex.Cancelled to be 1, but found 0`; `Expected codex.Retries to be 2, but found 0`; `Expected codex.LaunchesByKind to be equal to {["chat"] = 1, ["review"] = 1} … but could not find keys {"chat", "review"}` |
| `SeveralWindowsTests` (9, server) | `TheHourWindows_AreTrailing` (5h, 24h: the span, now inside, `now − span` outside, one second later inside); `TheFiveHourWindow_IsTrailing_NotSinceMidnight`; `SeveralNames_AreReadInTheOrderAsked_EachOnce` (`"5h, 24h,week,5H,"`); `AnUnknownName_AmongSeveral_IsRefusedByName`; `NoNames_IsNoWindows`; `SeveralWindows_OneScan` (a COUNTING read: one scan, rounds 1/2/3 in 5h/24h/week, the people's rows too); `APersonalAnswer_WithSeveralWindows_IsTheCallersOwn`; `APersonalAnswer_ReadsAsFarBackAsItsWidestWindow` (added at plant 4, when the scan's union was seen to be masked by the company chart) | RED: `Expected asked to be equal to {"5h", "24h", "week"} … but found empty collection`; `Expected type to be CoaiServer.WindowsAsked+Refused, but found CoaiServer.WindowsAsked+Read`; the hour windows failed on `Range("5h")` returning null |
| `RoundCountsEndpointTests` (5, server, collection `coai-server`) | through the real host: `SeveralWindows_AreAnsweredByName_EachWithItsRangeAndCounts`; `AWindowAnswer_StaysTodaysShape_WithNoSeveralWindowBlock`; `AnUnknownWindowAmongSeveral_IsRefusedNamingTheLegalOnes`; `AnAdminsWindows_CarryEachPersonsVendorsWithTheCounts`; `AnEmptyWindowsParameter_IsNoWindows` | RED: `Expected refused.StatusCode to be HttpStatusCode.BadRequest {value: 400}, but found HttpStatusCode.OK {value: 200}`; `Expected ((int)answer["vendors"]![0]!["rounds"]!) to be 1 because the counts are added to today's vendor rows too, but found 0` |
| `TheWindowAnswerKeepsItsBytesTests` (2, server) | the `?window=` answer, less the keys 5.1 added, equals text captured from the server BEFORE the change (compared after one relaxed-escaping round trip — the editing tool unescaped `+` in the captured literal); no `windows` key unless asked | a characterization — green on the old code by construction; its teeth: plant 3 below for the omitted key |
| `http/usage/usage.http` (+5 requests, 1 changed) | `usage_over_several_windows_counts_rounds_and_requests_by_value` (every count of the seeded codex row by value in all three windows: runs 8, rounds 2, runsWithoutRound 1, retries 1, repairs 1, followUps 1, cancelled 1, noBudget 1, `launchesByKind {canary 1, review 6}`), `usage_over_a_window_alone_stays_todays_shape_with_the_counts_added` (`@prod`), `usage_over_several_windows_has_the_shape_a_client_reads` (`@prod`), `an_unknown_window_among_several_is_refused_by_name`, `company_scope_over_several_windows_counts_each_persons_rounds`; the unknown-window refusal now names `5h` and `24h` too. `http/run-contracts.mjs` seeds seven round lines a minute old | plant 1 below: `72 requests processed (70 succeeded, 2 failed)` — the two by-value requests |
| `teamRounds.test.ts` (7, extension) | the parser by value (every window, the people's rows); `AnOlderServer_ShowsUnavailable_NotZero` (a 200 with today's range and no counts → `unavailable`, the block says *count unavailable* and the reason, no `0`); a missing window, a row without counts, a string count, `undefined` → unavailable; an empty counted window is a real `5 h 0`; the block's line; a hostile vendor id is text | RED on the inert shape: `Expected values to be strictly equal` ('unavailable' for 'counted'); `The input did not match the regular expression /count unavailable/`; `… /5 h 0 · 24 h 12 · 7 d 40/`; `… /codex/`; `… /<img src=x/` |
| `teamServerApi.test.ts` (+1) | `a company request asks for the round windows beside its own, and a personal one does not` | RED: `AssertionError: the tab's rounds come from the same request as its figures` |
| `teamServerTabPage.test.ts` (+2, the bundled page RUN) | a NEW server's counts on the page (`5 h 2 · 24 h 9 · 7 d 31`, `2 runs without a round`, `3 retries · 1 repair · 0 follow-ups · 4 cancelled · 5 budget refusals`); `AnOlderServer_ShowsUnavailable_NotZero` on the page (`Rounds: count unavailable`, no `5 h 0`/`7 d 0`/`0 runs without a round`) | RED: `The input did not match the regular expression /Rounds/`; `… /Rounds: count unavailable/` |
| `helpCoverage.test.ts` (+1) | every language's `the-rounds-log` article names `**Rounds**`, `**5 h**`, `**24 h**`, `**7 d**`, `**runs without a round**`, `**budget refusals**`, `*count unavailable*` | RED: `the en rounds-log article never says “**5 h**” — stale rather than missing` |
| `teamUsage.contract.ts` (changed on purpose) | the "older server" body also drops `windows` and the count fields, and asserts `Rounds: count unavailable` | — |
| **the seam's usage-counts leg** (`scripts/seam-usage-counts.mjs`, in `npm run test:seam`) | E5's plan round decision: the REAL server built from this tree (`team-server-child.mjs`, extracted from `run-contract.mjs`, which now uses it) over a seeded ledger — rounds, no round, canary, `NoBudget`, `Cancelled`, a client retry whose job rotated, a repair, a client follow-up and the server's own follow-up, placed at 1 h, 10 h and 3 days — asked as the tab asks (`fetchUsage`, company) and for a person's own windows (`ask`, the same client), read through `serverRounds`, every count of every window, and each person's week rows, asserted BY VALUE | seam plant A (the server serialising `runsWithoutRound` as `runsWithNoRound`): `the extension's parser could not read the server's company counts (this Team server does not count rounds yet …)`; seam plant B (retries per line): `company 5h: … expected [["codex",1,1,1,1,0,1,0,…]] read [["codex",1,1,2,1,0,1,0,…]]` |

**Break-it, from byte copies** (server): 1 — retries counted per line (`Distinct` removed): `Expected codex.Retries to be 1
because one retry REQUEST, however many accounts its job met, but found 2`, and the http suite's two by-value requests;
2 — rounds over every kind: `Expected codex.Rounds to be 1 because the deploy's check is not a person's round, but found
2` and `RunsWithoutRound … but found 3`; 3 — an empty window list serialised as `[]`: `Expected
answer.AsObject().ContainsKey("windows") to be False because an old client asking ?window= reads today's shape, but
found True` (and the empty-parameter test); 4 — the scan ignoring the windows: green until
`APersonalAnswer_ReadsAsFarBackAsItsWidestWindow` was added (the company chart's thirty days masked it), then red; 5 —
a `NoBudget` line counted as a launch: `… {["chat"] = 1, ["review"] = 2} differs at key "review"`; 6 — the personal
answer reading the whole scan: `Expected answer.Vendors to be empty because and the top of the answer is still today's
window alone`. (Extension): a partial set of windows believed → the missing-window test; a row without counts believed →
the row test; `unavailable` drawn as zeroes → both `AnOlderServer_ShowsUnavailable_NotZero`; the company request without
`windows` → the request test. Every file restored, touched, rebuilt; green again.

**NOT covered:** the DEPLOYED server (it serves none of this until E5's deploy — the live round count is story 5.4); a
personal answer's windows have no surface in the extension (only the seam leg asks for them); the http runner maps a
failed httpyac run to exit 4 *configuration* rather than 1 (`httpyac exited 20`, seen at plant 1 — a pre-existing
mismatch between the pinned httpyac and `run-contracts.mjs`, not changed here).

## A stated reset is read locally, and never waited out (2026-10-11, stories 5.3b and 5.3c of `todo/PLAN_every_round_is_counted.md`)

Every new behaviour was seen RED first against an INERT shape (the new member present and answering nothing), then
GREEN, then re-reddened from a byte copy of the fixed file (restored, LastWriteTime touched, rebuilt green). The mcp
tests run the REAL executor, ladder and ledger over `ScriptedResults` (the launcher is the only fake); the server tests
go through real HTTP with slots parked on disk by the real `SlotRegistry`, and no account is ever ready, so no vendor
process starts. Run: `./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*CooldownParserTests"
--filter-class "*AStatedReset*" --filter-class "*AQueuedReviewHearsWhenItCanRunTests"`;
`./src_server/tests/bin/Debug/net10.0/CoaiServer.Tests.exe --filter-class "*AParkedSlotIsHeardTests"`;
`node --test out/test/statedReset.test.js out/test/teamServerView.test.js` in `src_vs_code` after `npm run compile`.

| Suite | What it holds | Seen red as |
|---|---|---|
| `CooldownParserTests` (moved, mcp; 16 → 18) | the 16 server tests, moved verbatim with the type (`src_server/tests/SlotTests.cs` keeps `SlotEnvironmentTests` and `SlotSelectorTests`); `AStatedReset_IsTheInstantTheVendorNamed` (the agy 88h line, `33m52s`, a zoned clock time, a date); `NoStatedReset_IsNone_NeverTheGuess` (`rate limit exceeded`, *weekly*, a wait in words, `9999h`, empty → null); #754's carried fix in `ASpacedDuration_IsReadWhole_NeverAsItsFirstPart` — `2h, 30m` and `2h; 30m` err long, `2h, then try again` reads 2 h | RED (stub `=> null`): `Expected CooldownParser.StatedReset(Agy, Now) to represent … <2026-09-10 04:49:09 +0h> … but found a <null> DateTimeOffset`. #754's assertions RED before the regex: `Expected CooldownParser.Until("Resets in 2h, 30m", Now) … <… 17:00:00 +0h> … but <… 14:00:00 +0h> does not`. Break-it: `StatedReset` answering `Until` → `Did not expect … StatedReset("rate limit exceeded", Now) to have a value … but found <2026-09-06 12:30:00 +0h>` |
| `AStatedResetIsNeverWaitedOutTests` (9, mcp — 5.3c) | `AgysResetDaysAway_IsNotRetried_OneLaunchAndTheRefusal` (10-minute budget, ladder of three 1 ms steps: ONE launch, `Attempts` 1, the reason keeps `Resets in 88h49m9s`); `AShortStatedReset_InsideTheBudget_IsStillWaitedOut` (`Resets in 2s`, a minute: two launches, `Ok` — the positive control); `ARefusalThatStatesNoReset_ClimbsTheLadderAsBefore` (`429`: four launches); `AResetBeyondWhatIsLeft_OutlastsTheBudget` (6 rows: 88h/600 s true, 2 s/60 s false, 61 s/60 s true, 59 s/60 s false, no reset/0 s false, a clock time at a pinned now true) | RED on today's ladder (`ResetOutlasts` a stub answering false): `Expected launcher.Launched to be 1 because the vendor said the quota comes back in 88h49m9s, and the reviewer has ten minutes, but found 4.` and the three `true` rows. Break-it: the ladder's `Hopeless` back to `RateLimit.Hopeless` alone → `… but found 4` again |
| `AStatedResetIsWrittenLocallyTests` (5, mcp — 5.3b) | the agy refusal through the real executor and ledger: the one line's `resetsUtc` ends in `Z` and is within a minute of now + 88h49m9s; a `429` writes none; an answer writes none; a junk answer then a refused repair — only the LAST line carries it; an old line without the field reads as none | RED (field present, writer inert): `Did not expect line.ResetsUtc to be empty because agy stated when its quota comes back, and the ledger is where the local half keeps it.` and `Did not expect lines[1].ResetsUtc to be empty because the refused repair is what the turn last saw.` |
| `AQueuedReviewHearsWhenItCanRunTests` (3, mcp — the shim) | `ReadPoll` keeps the server's `waiting` (empty from an older server); the queued progress line carries it after the position and is still `IsProgress`; an older server's line is byte-for-byte `QueuedMessage(server, 2)` | RED (field on `RemotePoll`, not read): `Expected poll.Waiting to be "every account is rate-limited; …" … but "" has a length of 0`; and before the `AskRemote` wiring: `Expected line "gemini: queued on the Team server at https://coai.example.com, 0 ahead of it" to contain "every account is rate-limited; …"`. Break-it: `QueuedMessage` never appending → the line test red |
| `AParkedSlotIsHeardTests` (5, server, real HTTP) | the catalog's `slots.resetsUtc` is the EARLIEST parked account (2 h of 2 h and 88 h) and the body names neither slot and carries no `@`; empty with nothing parked; a queued job behind a parked account hears `rate-limited … comes back at` in `waiting` with `reason` empty; behind a signed-out one, `signed out`; a cancelled job's `waiting` is empty | RED (DTO fields defaulted, never set): `Expected status.Waiting "" to contain "rate-limited".`, `… Waiting "" to contain "signed out".`, `Did not expect vendor.Slots.ResetsUtc to be empty because the catalog says when the vendor can serve again`. Break-it: `Max` for `Min` → `… but <+3d 14h 49m 9s> was off by 3d, 14h, 49m and 9s`; the queued-only gate removed → `Expected status.Waiting to be empty because a cancelled job waits for nothing, but found "every account is rate-limited; …"` |
| `statedReset.test.ts` (9, extension) | the parser keeps `resetsUtc` (absent when empty or old); `resetLine` — `quota out — resets in 89 h (Wed 04:49)` for 88h49m left (rounded UP), `34 min (Sat 12:33)` under an hour, nothing once past, nothing when the row's NEWEST line by `utc` answered (whatever the array order), nothing for another row; the Rounds block's row hint on gemini and not on codex; the model card's `reset-line` and nothing before a read; the SHIPPED Review rounds page (bundled, run on the DOM shim) draws it | RED (stubs answering `''`, the parser not reading the field): `actual: undefined, expected: '2026-10-14T04:49:09.000Z'`, `actual: '', expected: 'quota out — resets in 89 h (Wed 04:49)'`, and the block/card/page regexes unmatched. Break-it (three plants at once): parser drop → the parser test; `Math.floor` → `actual: '… 88 h …'`; newest-by-position → `ALaterAnswer_HidesAnEarlierReset` `actual: 'quota out — …', expected: ''`. **+1 (CodeRabbit on #773):** `ALineWhoseUtcIsNoDate_NeverHidesTheNewestRefusal` — a torn line (`utc: 'not a date'`) before or after the refusal decides nothing; RED before the fix `actual: '', expected: 'quota out — resets in 89 h (Wed 04:49)'` (NaN compared false, so a first such line stayed "newest"), GREEN once `newestOf` skips lines whose `utc` is no date |
| `teamServerView.test.ts` (+1) | `AParkedVendor_SaysWhenItsFirstAccountComesBack` — `… they come back by themselves — the first in 89 h (Wed 04:49)`; an older server (no field) and a reset already past say what they always said | RED (parameters accepted, unused): `actual: 'codex: all 2 rate-limited, they come back by themselves', expected: '… — the first in 89 h (Wed 04:49)'`. Break-it: the clock pinned to 0 → `the first in 497765 h` |
| `importCycles.test.mjs` (unchanged — it caught a defect of this story) | the frozen cycle count | RED on the first full `npm test`: `a NEW import cycle appeared … 'models ↔ statedReset ↔ teamServerView ↔ usage ↔ vendors'` (and two longer ones) — `untilWords` lived in `statedReset.ts`, which reaches `usage.ts`; moved to the leaf `resetWords.ts`, GREEN, full suite 6150 pass / 0 fail |
| `teamServerTab.test.ts` (re-pinned on purpose) | the Settings sheet's hash, re-taken for the one `.catalog .reset-line` rule; without that comment and rule the sheet still hashes to the previous pin | the old pin went red on the change: `actual: '456ad98a…', expected: 'becbdd07…'` |
| `http/` contract suite | `catalog.http`: `slots.resetsUtc` is `''` with nothing parked; `reviews/happy.http`: a queued job's `waiting` says *signed out* (the suite's account is never signed in) and `reason` stays `''` | 67/67 green against the server built from this tree |

**NOT covered:** a reset older than the 7-day ledger tail is not shown; the shim's queued line is
tested through `PollTracker`, not through a running `--ask-remote` against a real server holding a parked slot.

## Epic 5's code round (2026-10-11, `todo/PLAN_every_round_is_counted.md`)

Three findings accepted. One changed behaviour and was seen RED first; two are quality changes guarded by the tests that
already existed.

| Suite | What it holds | Seen red as |
|---|---|---|
| `ConsultAntigravityDenialScenarioTests` (+1) | `AConsultationRefusedWithAStatedReset_WritesTheResetOnItsLedgerRow` — the REAL consultation path over the fake CLI as agy (exit 3, agy's verbatim `Resets in 88h49m9s` line on stderr): the one `consult` ledger row's `resetsUtc` is within a minute of now + 88h49m9s | RED (the gating finding): `Did not expect resets to be empty because the vendor stated when its quota comes back, and the ledger is where the local half keeps it.` Break-it: `ConsultantTurnBooks.StatedResetOf` answering empty for a refusal → the same message |
| `QuestionConsultServiceTests` (+2) | `AQuestionRowRefusedWithAStatedReset_WritesTheResetOnItsLedgerLine` — the real `QuestionConsultService` and row launch over `ScriptedLauncher`, one claude row refused with agy's verbatim line (exit 3; the classification reads the words): the `question` line's outcome starts `rate limited` and its `resetsUtc` is now + 88h49m9s; `AQuestionRowThatAnswered_WritesNoReset` — answered rows write none | RED: `Did not expect line.ResetsUtc to be empty because the vendor stated when its quota comes back …`. Break-it (the same plant, which also wrote `x` for a launch with no terminal): both red, the answered one included |
| `TheWindowAnswerKeepsItsBytesTests` (test-only change) | `WithoutTheCounts` builds a filtered COPY of the parsed answer instead of removing keys from it in place (the repository's no-mutation rule) | no RED: a test helper's shape, not behaviour — its two tests stay green against the same golden text |
| `RoundCountsTests`, `SeveralWindowsTests`, `RoundCountsEndpointTests` (unchanged — the guard) | `UsageRounds` counts retries, repairs and follow-ups in ONE pass (`RequestsByWord`) instead of one pass per word | no RED: a refactor that must not change a number — the 24 tests and the seam leg are the guard, green before and after |

## Names and a missing root (2026-10-11, `research/PLAN_names_and_a_missing_root.md`)

Every break-it below was a compiling one-line plant, applied from a `.mjs` that restored the file in `finally` and
rebuilt; TS plants were held to `tsc` exiting 0, C# plants to a build that succeeded.

| Suite | What it holds | Seen red as |
|---|---|---|
| `aSecurityPairNamesItsReviewer.test.ts` (6, the page RUN) | a ticked row is `GPT-6-Astra (chat-preset-sec-1)` with the id as the value; a switched-off row `… (switched off)`; a row on Models but not ticked `Old Luna (chat-preset-old) (stranded)` with its sentence; a gone row `sec-gone (stranded)` and *"The server drops this pair until you pick another reviewer."*, never "keeps asking"; `vendor: ''` draws no option without text, a selected *no reviewer — pick one*, no `Reviewer  is`, and *"This pair names no reviewer — pick one."*; a switched-off reviewer named in its warning; the *Offered:* line with `data-models-uses="security"`; the prompt card's tick labelled `GPT-6-Astra (chat-preset-sec-1)` | RED: all six — `offered: [["chat-preset-sec-1","chat-preset-sec-1"],…]`, `an option with no text: [["",""],…]`, `Reviewer chat-preset-sec-2 is unavailable; select an enabled reviewer.`, `the page draws no the Offered line`. Break-its (7, each red): options by raw id; no leading none-option (`Expected values to be strictly deep-equal`); the empty pick not named; the gone row through `rowPicks` (`… keeps asking`); the warning by id; no Offered line; the tick by id |
| `helpCoverage.test.ts` — security lane | every language's `security-lane` article carries `*(switched off)*`, `*(stranded)*`, `*no reviewer — pick one*`, `**Change on Models**` | RED: `the en security lane article never says *(switched off)*`. Break-it: the Ukrainian `*(stranded)*` unmarked → `the uk security lane article never says *(stranded)*` |
| `QuestionAdmissionTests` (+3, mcp) | `{vendor:"consult-claude", runtime:""}` with no reviewers is blocked with exactly the extension's sentence and no `''`; an empty pick *"this row picks no model — pick one, in …"*; `gemini` (a runtime's own name) keeps the runtime list | RED: `Expected refused.Reason to be the same string … "the row 'r1' names the." / "consult-claude is no."`. Break-its: the runtime-name test inverted (both red); the empty-vendor check never matching (`" is no longer ticked."`) |
| `CapabilityMatrixTests` (+2, mcp) | `Admit("")` / `Admit("   ")` refused with *"this row has no model to run on — pick one"*, through the Capability overload and the words overload (with an unparseable capability) | RED: `"'' is not a runtime the." / "this row has no model to."`. Break-its: each overload's blank check removed → red (`"'shell' is not a."` for the words overload) |
| `AWriterModeRefusesAMissingNamedRootTests` (12, mcp) | `--collect-bugs`, `--pairs-keep`, `--pairs-decide`, `--upload-pairs`, `--requeue-refused` through `Program.RunAsync`, and `--check-model` / `--check-consultant` through their entry points (a launcher that fails every call; a consultant that PASSES preflight), each: exit 74, the folder named on stderr, the root absent; the helper names the folder; Unknown (`UnauthorizedAccessException`, `IOException("busy")`) never refused, also through the dispatch; the default location never (a disk where nothing is there); `<root>/<side>` inside an existing root made by `--requeue-refused`; a reader and `--check-security` not refused. Its WRITER MODES block and the two checks' `Classify` lines are what the extension's census reads | RED: all seven writers — `` `coai-mcp --collect-bugs` exited 0 and made the missing data folder, holding: coai.db ``; `--check-model exited 0 and made the missing data folder, holding: consultations, consultations\health, …model-codex-2.check.json, …check.lock`. Break-its (6): Unknown refused (`found "The data folder …"`); the check on `<root>/<side>` instead of the root; the default counted as named; `--collect-bugs` out of `WriterModes`; the dispatch guard never firing; the check entry points unguarded |
| `TheServerRefusesAMissingNamedRootTests` (2, mcp, the real binary over stdio) | initialize answered, `tools/list` the real tools, 12 s past a sweep's beat (`COAI_SWEEP_SECONDS=10`, its floor), then `providers` → `isError: true` with the exact path and *"restore it, then restart the MCP server"*; the root absent after each step and after the kill; `ServeRefusal` only on a confirmed absence | RED: `initialize made the missing data folder, holding: consultations, logs, runs, consultations\health, logs\2026-10-11, runs\7263123c74a8.json, …consultants.survey…claim, logs\…\coai-mcp-….log`. Break-its: an `InvalidOperationException` instead of `McpException` → `Expected said "An error occurred invoking 'providers'." to contain "<path>"` (the consultant's advice, measured); serve never branching (the RED above again); `ServeRefusal` ignoring the injected read → the Unknown case refused |
| `AReaderModeNeverMakesTheDataDirectoryTests` | `--check-security` joins `READER MODES` (it reads an empty stdin here) | — held by the census below |
| `consultantCheckRun.test.ts` (+2) | under a missing named root on the board, `--check-consultant` and `--check-model` spawn nothing and answer `crashed` with the finding's sentence and *"The check was not started — nothing was sent."*; a board that never heard of an absence spawns as before | RED: `a check was spawned under a data root that is not there — the server's lock makes it`. Break-its: the board not read before the spawn; a missing root not holding the check — both that same message |
| `everyServerModeTheExtensionLaunches.test.ts` (+1) | `--check-consultant` / `--check-model` are *a person acts, held by the extension* and must appear in the server's writer test; `--check-security` *touches no data directory* and must appear in the server's reader inventory | RED: `--check-security is launched to read, and AReaderModeNeverMakesTheDataDirectoryTests.cs never runs it…`; `--check-model is held by the extension, and AWriterModeRefusesAMissingNamedRootTests.cs never runs it…`. Break-it: `--check-security` removed from `READER MODES` → the first message |
| `helpCoverage.test.ts` — data article | every language's `where-your-data-lives` says `**Check**` and `` `COAI_DATA_DIR` `` in the sentence that the MCP server creates no missing named folder either | RED: `the en data article never says **Check**`. Break-it: the Russian `**Check**` unmarked → `the ru data article never says **Check**` |
| `scripts/seam-missing-named-root.mjs` (`npm run test:seam`) | (a) a missing named root: the extension's `dataDirFinding` says missing, the real `--check-model` exits 74 naming it, a served `providers` is `isError` naming it, and the folder is not made; (b) an existing root with a missing side: neither refuses, and the served run makes the side folder; (c) no `COAI_DATA_DIR`: neither refuses — served only on POSIX (`XDG_DATA_HOME` moves the default into a temp folder; on Windows .NET reads `%LOCALAPPDATA%` from the shell, and a served run would write into the developer's own data) | Break-its against the server, rebuilt: serve not branching → `seam: a missing named data folder: (a) a named root that does not exist: the halves disagree — the extension refuses; --check-model exit 74: …; serve answered (isError false)`; the named root never found missing → red. The writer dispatch alone disabled stays GREEN — `--check-model`'s own entry guard still refuses — a second guard, on purpose |

**The person's other acts, measured (the follow-up of the same day).** Every mode the extension launches on a person's
act is now held by one of the two server tests:

| Suite | What it holds | Seen red as |
|---|---|---|
| `AWriterModeRefusesAMissingNamedRootTests.ProbeApi_StopsAt74_BeforeItSpends_AndLeavesTheMissingNamedRootAbsent` (mcp) | `--probe-api` through `ProbeApiMode.RunAsync` with a vault that answers a key and an endpoint stub that answers: exit 74, the folder named, the root absent, and NO request reached the endpoint; `--probe-api` is in `Program.WriterModes` | RED: `--probe-api exited 0 and made the missing data folder, holding: usage.jsonl` (the probe's usage line). Break-its: the entry-point guard never firing → that RED again; `ProbeApi` out of `WriterModes` → `Expected Program.WriterModes {…} to contain Startup.ProbeApi` |
| `AReaderModeNeverMakesTheDataDirectoryTests.CloseConsultLeavesAMissingNamedDataDirectoryAbsent` (mcp) | `--close-consult --repo … --id c-1 --outcome solved` under a missing root: exit 65 (no record to close — records live in the data directory), the root absent | GREEN on first run: measured to make nothing, so pinned rather than changed |
| `AReaderModeNeverMakesTheDataDirectoryTests.TreeRemoveLeavesAMissingNamedDataDirectoryAbsent` (mcp) | `--tree-remove --tree t-1` with `COAI_REVIEW_ROOT` in the test's own folder: exit 0, the root absent | GREEN on first run: the review root is machine-local, never the data directory |
| `AWriterModeRefusesAMissingNamedRootTests.ARoundsDatabaseWriter_…(args: ["--consultants"])` (mcp, the code round's own review) | `--consultants` through the dispatch under a missing named root: exit 74, the folder named, the root absent | RED: `` `coai-mcp --consultants` exited 0 and made the missing data folder, holding: consultations, consultations\health, consultations\health\consultants.json `` (its survey file). GREEN with `Startup.Consultants` in `Program.WriterModes` |
| `everyServerModeTheExtensionLaunches.test.ts` (changed) | the survey kind is held by the server's writer test too; only `--help` is held by neither | Break-it: `--consultants` taken out of the C# writer block → `--consultants writes on a person's act, and AWriterModeRefusesAMissingNamedRootTests.cs never runs it against a missing data directory` |
| `EveryModeIsClassifiedForAMissingNamedRootTests` (2, mcp, the code gate's third round) | every `Program.Startup` is a writer in `WriterModes`, a mode the reader test runs against a missing root (its flags read from that test's source), serve, or named with why in `NeverReachesTheDataDirectory` (the `--ask-*` shims, the agy hook, `--normalize`, `--outline`, help, an unknown argument — none touches the data directory in its code); no writer is also a reader. `--security-prompt-text` joined the reader test (measured: reads prompts, makes nothing) | GREEN on first run; break-its that build: `Normalize` taken out of the named list → `found at least one item {Startup.Normalize}`; `Consultants` taken out of `WriterModes` → `found at least one item {Startup.Consultants}` |
| `TheNamedRootAndTheDataDirAreBuiltTogetherTests` (2, mcp, the code gate's finding) | over `src_mcp/src`: `new PanelSettings` only in `Server/PanelSettings.cs`; no `with { … DataDir/NamedDataRoot = … }` anywhere | GREEN on first run (today's code obeys it); break-its, each a new file that builds: a `with { DataDir = "elsewhere" }` → `Expected movers to be empty … found {"Server\ZzPlantedMover.cs"}`; a second `new PanelSettings()` → `Expected builders to be equal to {"Server\PanelSettings.cs"} … contains 1 item(s) too many` |
| `RepoPathsTests` (2, mcp, 2026-10-12, the owner's request) | `RepoPaths.RootAbove` finds the folder holding both `src_mcp` and `src_vs_code` from deep below it, and THROWS naming where it began when no folder holds both (a tree with one half only) — the one helper the source-reading tests use; five private `RepoRoot()` copies (three markers, two silent fallbacks to the test binary's folder) were moved onto it | GREEN on first run (new helper); break-it that builds: the throw replaced by the old copies' silent `?? start` → `Expected a <System.InvalidOperationException> to be thrown, but no exception was thrown.` |
| `scripts/seam-missing-named-root.mjs` (changed, the code round's own review) | every case runs its child with an empty `COAI_CREDS_KEY` (no vault) and no inherited `COAI_DATA_SIDE`; case (c) on Windows asks the extension only — the default folder there is the developer's own | — (isolation, no behaviour asserted anew; the C# tests `TheDefaultLocation_IsNeverRefused` and `ServeRefuses_OnlyAConfirmedAbsence` hold (c)'s server decision) |
| `everyServerModeTheExtensionLaunches.test.ts` (+1) | the kind *a person acts* is gone: the RoundsDb writers and `--probe-api` are *a person acts, refused by the server* (in the writer test), `--close-consult` / `--tree-remove` *a person acts, makes no missing root* (in the reader test); a new test fails when any launch is held by neither server test (only `--consultants` and `--help` are) | Break-its: `--close-consult`'s run renamed in the reader test → `--close-consult must make no missing named root, and AReaderModeNeverMakesTheDataDirectoryTests.cs never runs it…`; `--probe-api` reclassified out of both → `a mode is held by neither server test — measure it against a missing root` |

`ProbeApiModeTests.VaultAnswers` became `internal` so the writer test reuses it rather than a third copy of a fake vault.

**NOT covered:** `consultantHealthPanel.runCheck` itself (`vscode` wiring; it calls `runConsultantCheck`, which holds the
guard); serve (c) on Windows (above); `--probe-api` through the dispatch with the real vault (its entry point is tested;
the dispatch's set membership is asserted).

## The sanitised snapshot (2026-10-12, S1 of `todo/PLAN_vibe_and_kimi_runtimes.md`)

| Suite | What it holds | Seen red as |
|---|---|---|
| `ReviewSnapshotTests` | the copy contracts of D6 against **real git** in temp repositories (`TempGitRepo`), the snapshot made into a `coai-answers-` scratch directory: the six excluded names absent at every depth and named in the manifest, `.agents/PROJECT.md` kept; the exclusions matched by the destination's PROBED case rule; the profile a parameter; a committed symlink (a `120000` entry written by hand, so the rule runs on a machine whose git cannot make a link), a file symlink on disk (skipped with the reason where no privilege), a directory link (a junction on Windows, a symlink elsewhere, with a file behind it) each refusing with the path and leaving nothing under the scratch directory; two spellings of one name refusing on a case-folding destination and both copied on a case-sensitive one; an escaping, absolute or doubled path refused by the planner before a directory exists; the file, byte and time ceilings each refusing with the number and leaving no partial copy; a populated submodule copied and an unpopulated one (a clone without `--recurse-submodules`) listed unavailable; an LFS pointer listed unavailable and not copied; `export-ignore` / `export-subst` bytes kept; the live tree copying untracked-not-ignored, current bytes, no deleted and no ignored path, the pinned tree copying what git lists and no untracked; a file changed between its stamp and its copy retried once with the retry's bytes kept, and one that keeps changing refused after exactly one retry; disposal deleting the folder; the partial name never published | 2026-10-12: the whole namespace missing (CS0234/CS0246 ×70); then, against the first build, the hook changing a file BEFORE its stamp (*a snapshot was made where a refusal was expected*, twice) — the seam moved between the stamp and the copy; and `git add -A` in the fixture's commit dropping the hand-written index entries (exit 1, *nothing to commit*) — the two tests commit the index as it is |
| `ReviewSnapshotResolveTests` | `MapBack` mapping an absolute path inside the snapshot and refusing the checkout's, a sibling root, a missing entry, a relative path; `Resolve` serving a manifest entry by its stored spelling through `.` components and the alternate separator; a case variant and an 8.3 short name refused (ordinal); every form refused — empty, relative, drive-relative, root-relative, `\?\`, `\.\`, UNC in both spellings, an alternate data stream, a trailing dot or space on a directory or a file, a `..` that leaves the root, the checkout's path, a sibling root, a missing entry — and a `..` that stays inside served; `Read` serving the bytes and refusing what `Resolve` refuses; a link swapped in under the root AFTER the snapshot not followed (the original bytes or a refusal, never the outside marker) | — new unit, red as the namespace |
| `ReviewSnapshotRefreshTests` | D6 as amended: a refresh writing new and changed files, removing deleted ones, keeping the root path and the manifest replaced; still excluding and still mapping back; a REFUSED refresh (a directory link in the live tree, a ceiling, a tree that keeps changing) leaving the old copy readable, its manifest the same object and nothing beside the root; the budget counting the refreshed size, not old plus new, and a ceiling that admits the refreshed tree but not old + new admitting it; a disposed snapshot refusing | 2026-10-12: an EMPTY junction in the live tree refreshed instead of refusing — git lists nothing under it, so nothing is copied through it; the test puts a file behind the link, and the rule is written down |
| `SnapshotBudgetTests` | a second snapshot past the ceiling refused with both numbers and holding nothing; the free-space floor refusing against a fake volume reader; a reader that throws refusing rather than guessing; a finished snapshot giving its bytes back and the next one fitting; a double dispose releasing once; the defaults the plan names; the real reader answering for the temp directory | — new unit |
| `OnlyARuntimeThatAsksGetsASanitisedSnapshotTests` (S1's `NoRuntimeAsksForASanitisedSnapshotYetTests`, renamed in S2) | of the shipped reviewer runtimes exactly ONE answers `NeedsASanitisedSnapshot` true, and it is `VibeRuntime`; every shipped consultant still false (the Vibe consultant is S3's) | S1: the member missing (CS1061); S2, the capability reverted: *Expected runtimes.Where(runtime => runtime.NeedsASanitisedSnapshot) to contain a single item, but the collection is empty* |
| `AScratchSweepRunsOnTheConsultationBeatTests` | the real `PanelServiceHost`, no vendor, no round: an old `coai-answers-*` directory in the REAL temp root holding an undisposed snapshot is gone after one beat of `ConsultationSweeper` | 2026-10-12: *Expected Directory.Exists(scratch) to be False … but found True* before `Swept` called `SweepScratch` |
| `ReviewSnapshotHardeningTests` (the S1 review, 2026-10-12) | a planner theory of ten names Windows would open as another name — trailing dot or space (`.vibe./config.toml`, `AGENTS.md `, `.agents./skills/x`) and device names (`nul`, `CON.txt`, `com1`, `LPT9.log`, `docs/aux/readme.md`) — each refused with the path before a directory exists, in LIVE mode deliberately (in pinned mode the missing normalised file was refused as "not there": green for the wrong reason on the first run); the bypass end to end with **real git**: `.vibe/config.toml` on disk, `.vibe./config.toml` in the index through `core.protectNTFS=false`, the pinned snapshot refused and nothing published; a directory turned into a junction (a symlink elsewhere) by the seam BETWEEN the plan and the copy, its target a same-length same-time file, refused with the path and no outside byte landed; an untracked nested repository (`git init` inside the live tree) listed unavailable, nothing under it copied, the snapshot made; the manifest holding ordinally and comparing by value | *a snapshot was made where a refusal was expected* (the bypass copied `.vibe/config.toml` into the snapshot on Windows; the swapped-in junction copied `OUTSIDE!`); *nested/ would leave the snapshot root* (the whole snapshot refused for a nested repository); *Expected object to be …* (reference equality) |
| `ReviewSnapshotResolveTests` (added by the review) | `Resolve` refusing a path under a link swapped in after the snapshot, as `Read` does; `SnapshotLinks.FirstLinkOnTheWay` naming a component that does not exist — the walk fails closed | *Expected type to be SnapshotPath+Refused, but found SnapshotPath+Inside*; *Expected … "…\missing" … but "" has a length of 0* |
| `ReviewSnapshotRefreshTests` (added by the review) | a dispose started while a refresh copies (a slow seam): nothing escapes either side, the budget holds nothing, no directory is left under the scratch directory | `ObjectDisposedException: … 'System.Threading.SemaphoreSlim'` out of the refresh's `finally` — the dispose had disposed the turn the refresh held |
| `ReviewSnapshotInventoryTests` (the plan gate's finding, codex, 2026-10-12) | the whole inventory checked after the last file, for `MakeAsync` (live) and `RefreshAsync` alike, the seam firing on the LAST tracked file so the early files' per-file checks have already passed: a file changed after its own copy (`a.txt` appended on every attempt), a file added during the copy (`added-<attempt>.txt`, a different one per attempt so the retry cannot settle), a file removed during the copy (`a.txt` on the first attempt, `b.txt` on the retry) — each retried exactly once (the seam saw attempts 1 and 2) and then refused naming the path; a make publishes nothing, a refresh keeps the old copy's bytes and the same manifest object, nothing beside the root | 2026-10-12, all six: the three makes *a snapshot was made where a refusal was expected* (a snapshot of mixed times published), the three refreshes *Expected type to be SnapshotRefresh+Refused … but found SnapshotRefresh+Refreshed* (the mix swapped under the stable path) |

| `ReviewSnapshotInventoryTests` (finding A of the risk consultations, 2026-10-12) | a later file grown from an EARLIER file's copy seam (between the plan and its own copy): on every attempt → retried once, refused naming it, the budget holding nothing; on the first attempt only → retried, made, and the manifest's and the budget's bytes equal what is on disk, never the plan's size | *a snapshot was made where a refusal was expected*; *Expected snapshot.Manifest.Bytes to be 85L … but found 45L (difference of -40)* — the budget understated by the growth |
| `ReviewSnapshotHardeningTests` (finding B, Windows only) | a 1-byte tracked file carrying a 1 000-byte alternate data stream under a 100-byte ceiling: the snapshot is made with 1 byte counted, and the copied file carries NO `:payload` stream (opening `file:stream` fails) | *Expected HasStream(copied, "payload") to be False … but found True* — `File.Copy` had carried the stream past the ceiling |
| `ReviewSnapshotResolveTests` (findings D and F) | the root renamed aside and a junction to an outside folder put at the root path: `Resolve` and `Read` refuse; a link swapped in BETWEEN the walk and the open (the `BeforeOpen` seam) not followed by `Read`; a file grown past the 4 MiB cap at the seam refused | *Expected type to be SnapshotPath+Refused because the root itself is a link now, but found SnapshotPath+Inside*; *… SnapshotRead+Refused … but found SnapshotRead+Served* (the outside canary served; the 4 MiB+1 file served) |
| `AcpReadBoundaryTests` (finding F) | the same two through the ACP boundary: a link swapped in at the `BeforeOpen` seam refused and the canary never quoted; a file grown past the ceiling after its size was checked refused with "ceiling" | *Expected answer.Served to be False … but found True*, twice |
| `AcpClientTests` (finding G) | `APrematureEofIsAProtocolEnd_AndTheGrandchildIsGoneToo` strengthened: the parent's exit observed before cleanup, the grandchild's pid recorded at spawn and asserted distinct from the agent's, both gone; the reason may name either side of that exit — "exited with code 0" (Windows) or "closed its output" (Linux, macOS, where the EOF arrives first: CI 2026-10-12); the grandchild is asserted gone on Windows only — on Linux and macOS CI showed the known limit (*Expected Gone(grandchild) to be True … but found False*), so there the test ends the grandchild by its recorded pid and reports itself SKIPPED naming the limit and the plan's §4 condition | no red is possible on Windows (the exited parent's retained handle still names its children); the order is fixed by reasoning and the Linux limit is written down |
| `ReviewSnapshotHardeningTests` (CI on Linux and macOS, 2026-10-12) | `ALinkListedAsOneEntry_IsRefusedBeforeItsExistenceIsAsked` (live and pinned): a junction handed to the planner as the ONE entry git lists on Unix for a symlink to a directory — `FileInfo.Exists` false, as there — refused as a link naming the path; `ADanglingLink_IsRefusedNotSkipped` (live and pinned): a junction or symlink to nowhere, refused as a link | with the existence check first: live → *Expected type to be …Refused … but found …Planned* (the link dropped silently, the CI symptom of `ADirectoryLinkInTheSource_…` and `ARefusedRefresh_…`); pinned → *"jdir is listed in the pinned tree but is not there" to contain … "link"* |
| `AcpReadBoundaryTests`, `ReviewSnapshotResolveTests` (the cadence consultation of epics 4-5, 2026-10-12) | `ARootSwappedForALink_BetweenTheCheckAndTheOpen_IsRefused_AndNeverQuoted` / `ARootSwappedForALink_BetweenTheWalkAndTheOpen_IsNotFollowedByRead`: the root renamed aside and a link to an outside folder holding the same relative file put in its place by the `BeforeOpen` seam — refused, the canary never quoted; `ALinkSwappedInForTheOpen_AndRemovedBeforeTheCheck_IsRefused`: a directory swapped for a link for the open and put back by the new `AfterOpen` seam before the check — refused (on Windows and Linux by the handle's name; on macOS by `MacFileIdentity`'s device and inode) | before the fix, on Windows: *Expected answer.Served to be False … but found True* and *Expected type to be …SnapshotRead+Refused … but found …Served* (the root's canonical path was asked for after the open); the restore test cannot go red on Windows — it is macOS's, and only the macOS CI job runs it there |
| `OnlyTheLauncherStartsAProcessTests` (the code gate of epic 4, 2026-10-12) | no `.cs` under `src_mcp/runners` or `src_mcp/src` but `ProcessLauncher.cs` builds a `ProcessStartInfo`, a `new Process { … }` or calls `Process.Start(` (comment lines skipped) | *Expected offenders to be empty … found … AcpTransport.cs:85: var info = new ProcessStartInfo(…)* — the ACP transport's own launch, without the environment rule |
| `AcpSessionWindowTests` (the code gate of epic 4, 2026-10-12) | sixteen `line`/`limit` cases (before the first line, past the last, a trailing newline, CRLF, an empty file, negative and zero values) agree with the first version's split-and-join; a two-line window of a 2 000 000-line text allocates under 64 KiB | *Expected allocated to be less than 65536L … but found 80778176L* — the whole file split for two lines |
| `AcpSessionTraceTests` (CodeRabbit on #785, 2026-10-12) | the fake agent sends 900 tool calls while the test walks `session.ToolCalls` with a `foreach` (an enumerator checks the list's version; `Select(...).Count()` over a list goes by index and did NOT go red) — no exception, and all 900 kept | *Did not expect any exception … but found System.InvalidOperationException: Collection was modified* (3 of 3) — the outcome held the session's live lists |
| `ReviewSnapshotTests` (CodeRabbit on #785, 2026-10-12) | `AScratchDirectoryThatCannotBeMade_IsARefusal_NotAnException`: a file where the scratch directory goes → `SnapshotOutcome.Refused` naming it; `AFileHeldOpenForWritingByAnother_IsCopied_NotRefused` (Windows): a file held open for writing with `FileShare.ReadWrite` is copied | *Did not expect any exception … found System.IO.IOException: Cannot create '…not-a-directory'*; *the snapshot … was refused: app.log could not be copied (… being used by another process)* |
| `KimiAcpProfileTests` (CI on Linux and macOS, 2026-10-12) | `OnWindows_TheInstallersPathIsTakenWhenItIsThere_ElseTheBareName` now holds on every OS: the Windows path is spelled with backslashes whatever OS runs the code | on Linux and macOS CI: *Expected … "C:\Users\someone\.ki…" but "kimi"* (`Path.Combine` used the running OS's separator) |

**NOT covered:** a Kimi launch, a Vibe consultation and a Vibe question row started in a snapshot (S3–S5 wire those
seams; a Vibe REVIEWER started in one is covered — the section below); a symbolic link on disk
on a machine without the privilege to make one (skipped, with the reason); the swap's failure paths (a root held open
by another process during a refresh) — exercised only by code reading; the macOS fallback of `HandleRead` (a walk after
the open plus the device-and-inode check of `MacFileIdentity`) — no macOS run here, the macOS CI job is its only run; a hard link in a live tree (a known limit, not a test); the ACP teardown
order losing a grandchild — not observable on Windows, where the test would pass under either order.

## Every local launch is told the panel's effort, and a cut answer is named (2026-10-13, epic 1 of `research/PLAN_local_effort_reaches_every_local_launch.md`)

Every RED below was observed against the unfixed code; every break-it reverted the one line the behaviour depends on,
rebuilt (C# build 0 errors, `tsc` exit 0), saw red, and restored.

| Suite | What it holds | Seen red as |
|---|---|---|
| `ALocalLaunchCarriesThePanelsEffortTests` (2, mcp) | a ✓ Check of a `local` row (`ConsultantCheckMode.AnswerModelAsync`, the row on stdin, a spy launcher for `--ask-local` and the real one for git) is launched with the panel's NON-default `--reasoning-effort medium` and `--max-tokens 4096`; a row with its own `effort: low` keeps `low` | RED: `Expected argv {"--ask-local", …, "--max-tokens", "8192"} to contain items {"--reasoning-effort", "medium"}`. Break-it: `EffortFor(row, string.Empty)` restored → red |
| `QuestionFanOutTests.ALocalQuestionRow_…` (1, mcp) | a `local` question-consultant row's argv carries the panel's `medium` and `4096` (the harness's `FanOut` gained a panel transform) | RED: the same message, `--timeout-seconds 20 --max-tokens 8192` and no effort. Break-it: `EffortFor(provider, string.Empty)` restored → red |
| `AConsultantRunsWithItsRowsOptionsTests.ALocalConsultant_…` (1, mcp) | a consultation turn's `--ask-local` argv, built through `ConsultantResolution.For` and `ConsultantTurnInputs.Settings(…, panel, …)`, carries `medium` and `4096` | written against the new signature; break-it: the effort line reverted → red |
| *(removed in the code round)* `NoLocalLaunchIsToldAnEmptyEffortTests` | a source scan for `EffortFor(…, "")`. It went RED naming both sites (`ConsultantTurnInputs.cs:64`, `QuestionFanOut.cs:322`), and was then REPLACED: `EffortFor` takes the `PanelSettings`, and all three composers go through `RosterBuilder.WithLocalEngine`, so an empty default cannot be written — the reviewers found the scan blind to a named argument, a call-shaped first argument and the ceiling | the three argv tests above hold each composer's behaviour |
| `EpicTwoCodeRoundTests`, `ATeamServerRowSendsContractTwoTests` (changed) | `EffortFor` called with a `PanelSettings` carrying the level the test used to pass as a string; assertions unchanged | — |
| `ALocalAnswerCutAtItsCeilingIsNamedTests` (6, mcp, `fakecli-env`) | `Program.AskLocalAsync` against `ApiEndpointStub` answering recorded Ollama bodies: content `""` + `finish_reason: length` + 8 192 tokens → exit 70, no `--out` file, stderr `cut at the token limit (max_tokens 8192)`, `8192 tokens generated`, `0 characters of content arrived`, `sent no reasoning_effort`, stdout `"tokensOut":8192`; a schema-whole `{"status":"SECURE","findings":[]}` cut with effort `low` → exit 70, no file, `reasoning_effort was low`; a `stop` answer written as before; `--reasoning-effort engine` said as `sent no reasoning_effort` (RED first: `reasoning_effort was engine` — the code round's correctness review); `CutSentence` equal to the api shim's former inline sentence to the byte; the sentence neither `RateLimit.Hit` nor `VendorDiagnosis.Classify` | RED: `Expected code to be 70, but found 0` (the cut fragment kept) and `Expected stderr "[coai-mcp] the local engine returned no message content: {…"finish_reason":"length"…` to contain "cut at the token limit (max_tokens 8192)"`. Break-it: the `CutRefusal` block removed → both red again |
| `modelCheck.test.ts` (+1, extension) | a failed check record (through the real `parseCheckDocument`) with `failureWhat` and no `reason` reads `checked: no answer — <failureWhat>`; one with a `reason` keeps the reason | RED: `actual: 'checked: no answer'`. Break-it: `reasonOf` returning only the reason → red |

The five call sites of `ConsultantTurnInputs.Settings` in tests now pass a `PanelSettings` (`AConsultantIsHandedItsRowsKeyTests`,
`AnApiConsultantRunsWithItsModulesSettingsTests`, `AnApiRowIsSettableTests`, `ARowsFastModeReachesItsLaunchesTests`,
`AConsultantRunsWithItsRowsOptionsTests`); their assertions are unchanged.

**NOT covered:** a real thinking model answering a check at `none` (epic 2's hardware run measures the lane, and one live
✓ Check after the release is the owner's to press); the lost repair launch of a cut fragment is pinned only as "exit 70,
nothing written" — the executor's no-repair-on-exit is its existing behaviour, not re-tested here.

## The Vibe reviewer row (2026-10-13, S2 of `todo/PLAN_vibe_and_kimi_runtimes.md`)

How each was seen red: written after the code, then the production line each behaviour depends on was REVERTED
(testing.md, *if the fix already landed*) with a scratch script and the classes run against it, then restored and run
green. Three revert runs: (1) the runtime's own lines, the launch control, the home check, the manifest validation, the
executor's three seams, the registrations and the diagnosis rows together; (2) the version parse, the glob match, the
home's name, the installed-path rule, the default model, the `--workdir` value and the scratch's delete; (3) the
engine's disposal of the round's scratch alone. The messages below are those runs' own.

| Suite | What it holds | Seen red as |
|---|---|---|
| `VibeRuntimeTests` | the argv: both read-only flags, never `--trust`/`--add-dir`, the prompt only on stdin (D2, D3, D7); `--workdir` is the folder the roster gave; the BUILT environment — not inherited, `ProcessEnvironment.Minimal`, exactly `VIBE_HOME`/`VIBE_CLI=python`/`VIBE_ACTIVE_MODEL` (D17) — and a `VIBE_MCP_SERVERS` set in the parent never in the request; the curated default for a row with no model; one short home per reviewer in the round's scratch, else in the launch's folder; `--time-limit` 10 s under the timeout, at least 1; a fresh export folder and the time limit per launch (`ForLaunch`); the answer = the last assistant message; usage from the export with no cost, `Usage.Unknown` when null or missing (§18); a deadline said from the export's `outcome`; the after-check `Disowned` (another model, an empty `active_model`, no export); `NeedsASanitisedSnapshot`; the registrations (`RuntimeNames`, `MachineOnlyRuntimes`, `Default`, `Named`, `RuntimeResolution.For`); the installed path under `.local\bin` on Windows only | *Expected args {"-p", "--output", "json", "--enabled-tools", …} to …* (no `--agent plan`); *Expected request.InheritsEnvironment to be False because §22 A …, but found True*; *Expected After(request.Arguments, "--workdir") to be the same string*; *Expected …ModelVariable to be "mistral-medium-3.5" … but "" *; *Expected VibeRuntime.HomeFor("x", "vibe", "Architecture") not to be "x\vh-f84370aca6"*; *Expected VibeRuntime.TimeLimit(…) to be 590, but found 600*; *Expected After(retry…, "--output-dir") not to be …vx-fffb5684ce*; *Expected string to be the same string, but they differ at index 0* (the raw array returned); *Expected object to be CoaiMcp.Core.Findings.Usage …*; *Expected string "" to contain "deadline"*; *Expected runtime.Disowned(…) "" to contain "not on the row's 'glm-5-3'"*; *Expected boolean to be True because D6 …, but found False*; *Expected ReviewerRuntimeSelector.RuntimeNames {"codex", …, "api"} to contain "vibe"*; *Expected VibeRuntime.InstalledExecutableFor(HostKind.Linux, …) to be the same string because nothing has run Vibe on Linux (D18)* |
| `VibeLaunchRulesTests` | the pure halves of the launch control: every host but Windows refused naming D18, and a WSL bridge (`wsl`, `wsl.exe`, `\\wsl.localhost\…`, `\\wsl$\…`) on Windows too; a Windows `vibe` not; the built environment passes and an inherited one, a non-minimal passthrough, `VIBE_CLI=rust` and a fourth `VIBE_*` are refused; an alias outside the curated list refused with the list; the version read off `python.exe …\vibe 2.26.1` | *Expected VibeLaunch.HostRefusal(host, "vibe") "" to contain "D18"*; *Expected VibeLaunch.EnvironmentRefusal(built with { InheritsEnvironment = true }) "" to contain "D17"*; *Expected VibeLaunch.ModelRefusal("nope") "" to contain "'nope'"*; *Expected VibeLaunch.VersionIn(printed) to be "2.26.1" … but "" * |
| `VibeHomeTests` | the owned home against the SHIPPED manifest's own rules with one pinned file this test controls: an empty home and §24's inventory admitted (the positive companions); `hooks.toml`, any `config.toml` (one declaring an MCP server, one declaring only a theme), `agents/plan.toml`, `tools/…`, `.env`, a loose file where Vibe keeps session folders refused naming the file or the first folder Vibe never writes; empty `agents/`, `tools/`, `skills/` folders refused; a trust list with an entry in either list refused; a plugin file with other bytes, one the manifest does not name, a plugin folder it does not name refused; the shape check admitting a plugin file by place but still refusing `hooks.toml`; a link inside the home and a home that is itself a link refused; the varying globs (`*` one segment, a final `**` one or more) | *Expected Refusal() "" to contain "holds hooks.toml,"* (and the same for every refused name); *Expected Refusal() "" to contain "linked"*; *Expected VibeHome.Matches(…) to be False, but found True* |
| `VibeHomeManifestTests` | `shared/vibe-home-manifest.json` is embedded and qualifies `2.26.1` with its 12 pinned plugin files under `logs/session/plugins/`, the dotted lock name among the varying names, `2.27.0` unqualified; a document missing its prefix, its versions or a 64-hex hash is `Broken`, never a manifest that admits nothing | *Expected type to be …VibeManifestLoad+Broken, but found …+Loaded*; *Expected var entry = manifest.For("2.26.1") to contain a single item, but the collection is empty*. Stays green under the revert: the `not json` row, which tests the parse's own catch and not the validation |
| `VibeLaunchControlTests` | through the REAL executor with a scripted launcher that answers `--version` as an installed Vibe and a review as Vibe does (array, export, `vibehistory`): a fresh home on a qualified Vibe admitted, the version asked in the launch's own environment; the export folder gone and the home's turn given back after the read; a model outside the list, a Linux and a WSL host refused before ANYTHING starts; a `hooks.toml` refused before even the version is asked; an UNMEASURED Vibe (2.27.0) run on a fresh and on a resumed home — its plugin files admitted by name — with the outcome carrying the warning (the owner's decision of 2026-10-13; S2 first shipped a refusal here); a measured one carrying none; a measured one with a tampered plugin file still refused; a `hooks.toml` still refused on an unmeasured one; a repair refused because the first launch left a `hooks.toml`; two turns on one home never at once; an answer on another model a failure keeping its usage; a stop at the time limit a failure with unknown usage | *Expected type to be …NotStarted, but found …Unparseable* (the control not asked — the model, host, hook, version and repair rows); *Expected Directory.Exists(ExportOf(vibe.Reviews[0])) to be False because D7 …, but found True*; *Expected vibe.MostAtOnce to be 1 because D17 …, but found 2*; *Expected failed.FailureReason "" to contain "deadline"*. A first version of the scripted Vibe wrote `vibehistory` into a home the reverted control never made and failed with `DirectoryNotFoundException` — a setup error, not the symptom; the fake now makes the home as Vibe does, and the rows went red for the reason above |
| `AVibeRowReviewsInTheRoundsSnapshotTests` | the round's launch directory against a REAL git repository committing `.vibe/hooks.toml` and `AGENTS.md`, through the real roster and snapshot: with `COAI_CODE_WORKSPACE=worktree` a Vibe reviewer starts in the round's snapshot, which holds `src/app.cs` and neither configuration file, inside the round's scratch with the reviewer's home; a codex row keeps the checkout and its argv but for the answer file; a citation under the snapshot maps back to `src/app.cs` and an excluded one stays absolute; no snapshot → the Vibe row left out naming the snapshot; a refused snapshot → left out with that refusal; a plan stage and the `none` workspace make no snapshot and no folder; disposing the scratch deletes snapshot and homes | *Expected type to be CoaiMcp.Server.RoundTree+Made, but found …+NotMade*; *Expected work.Excluded to contain a single item matching (e.Provider == "vibe"), but the collection is empty*; *Sequence contains no matching element* (no Vibe reviewer: the row was not registered); *Expected Directory.Exists(scratch.Directory) to be False, but found True* |
| `AVibeReviewerRunsThroughAFakeVibeTests` | a REAL child: the fake CLI as `vibe` through the real launcher, steered by the temp-directory file because the child gets the minimal environment — the child's own environment record holds `VIBE_CLI=python`, the model, the home under the round's scratch, no `VIBE_MCP_SERVERS` though the parent had one, and nothing but the minimal names besides; the review read and its usage the export's (the export echoes the model the child was given, so the after-check passed for real); the export folder gone; a `hooks.toml` the first launch left refusing the repair. And a REAL code round (Windows; skipped elsewhere, where D18 refuses Vibe) with a Vibe row and `COAI_CODE_WORKSPACE=worktree`: the reviewer's recorded `--workdir` is a `snapshot-*` folder, not the repository, and it — and the round's scratch around it — is gone when the round ends | *Expected type to be …Ok, but found …Unparseable* (the raw array); *Expected Directory.GetDirectories(_dir, "vx-*") to be empty …*; the round with the registration reverted: *nothing could review the PlanReview stage …*; with ONLY the engine's `using var scratchOfTheRound` removed: *Expected Directory.Exists(codeLaunch) to be False because the snapshot went with the round, but found True* |
| `VibeIsDiagnosedInItsOwnWordsTests` | the not-signed-in stderr of §11 arm a2 → `Refused`, cured by `vibe --setup` and naming the keyring; the Rust CLI's clap error of §3 → `UnknownOption`, naming the unmeasured CLI and the pin; the install cure per OS from Mistral's README | *Expected diagnosis not to be <null>* (twice); *Expected VendorDiagnosis.InstallCure("vibe", linux: false) <null> to contain "uv tool install mistral-vibe"* |
| `EveryAdapterRecordsWhatItLaunchedTests` (+ a `vibe` row) | Vibe records the model it launched and no effort | — (an added row of an existing theory, green with the code) |
| `ApiRuntimeTests.TheSelectorKnowsTheName_AndTheMachineOnlySetHoldsIt` (changed) | `MachineOnlyRuntimes` CONTAINS `local` and `api` — it asserted the whole set, a second copy that went red the day `vibe` joined it | *Expected ReviewerRuntimeSelector.MachineOnlyRuntimes to be a collection with 2 item(s), but {"local", "api", "vibe"} contains 1 item(s) more than {"local", "api"}* — the reason for the change |
| `AnUnmeasuredVibeIsWarnedAboutNotRefusedTests` (the owner's decision of 2026-10-13: warn, not refuse) | the card: `providers` probes an unmeasured Vibe (2.27.0) as healthy with `VendorHealth.Warning` naming 2.27.0, 2.26.1, `vibe-home-manifest.mjs` and `probe-vibe-confinement.mjs`; the measured 2.26.1 carries none; the wire carries `warning` on the vibe row alone (absent on codex). And in `VibeLaunchControlTests` / `AVibeReviewerRunsThroughAFakeVibeTests`: the unmeasured version launches and the outcome, the round's reviewer line (`; warned — vibe/PlanCritique: …`) and `reviewerStates[].warning` carry it | RED FIRST, with the members in place but empty: *Expected health.Warning "" to contain "2.27.0"*; *Expected rows["vibe"].TryGetProperty("warning", …) to be True … but found False*; *Expected type to be …Ok because resumed=False: an unmeasured version launches, but found …NotStarted*; the real plan round: *reviewers: "0 of 1 reviewers answered; failed: vibe/PlanCritique: not started: Vibe 2.27.0 is not one this coai-mcp has qualified …"*. Green from the start, as positive companions: the measured version carries no warning, the tampered plugin and the `hooks.toml` still refuse |
| `TheVibeProbeLeavesThePersonsHomeAloneTests` (the coordinator's follow-up, 2026-10-13) | the `providers` probe of a `vibe` row: ONE `--version` request, not inheriting the environment, passing the launch control's own `EnvironmentRefusal` (the built environment), on a home that is not the person's `~/.vibe`, that exists while the probe runs and is gone after it; a codex row is probed as before (inherited environment, nothing added) | RED FIRST, before the fix: *Expected asked.InheritsEnvironment to be False because an inherited environment gives Vibe the person's own home, but found True*; *KeyNotFoundException : The given key 'VIBE_HOME' was not present in the dictionary* (the probe named no home, so Vibe used the person's own); the codex row green before and after |
| The risk consultation of epic 2 (gpt-6-astra, 6b4726a6, 2026-10-13) — `VibeLaunchControlTests` + `VibeHomeTests` | **(1)** the `--version` process runs on the home, so the shape is checked AGAIN after it on both branches: a scripted `--version` that writes `hooks.toml` and answers 2.27.0 (unmeasured) or 2.26.1 (measured) — the review never starts (`AHooksFileTheVersionProbeLeaves_RefusesTheReview_OnEitherBranch`). **(2)** a cached plugin package that would START something is refused on the shape check, which is all that guards an unmeasured Vibe: `mcp.json` (also spelled `MCP.json`), `.mcp.json`, `.claude-plugin`/`.codex-plugin`/`.kimi-plugin` `plugin.json` and `kimi.plugin.json` with a non-empty `mcpServers`, `hooks`, `lspServers` or `sessionStart` (a path string counts), and a declaration that is not JSON; `ai.mistral.vibe/hooks.toml`, `hooks/hooks.json`, `hooks.json` whatever they hold; admitted: an `mcp.json` with `mcpServers: {}`, a foreign manifest whose fields are `null`/`[]`, a native `plugin.json`, a skill that merely mentions `mcpServers`, and a declaring file a measured version pins with exactly those bytes (other bytes refused). Through the executor: a resumed 2.27.0 home whose `packages/zz/zz-digest/mcp.json` declares a server is `NotStarted` and nothing of Vibe's starts | RED FIRST, 15 failures before the fix: *Expected type to be …ReviewerOutcome+NotStarted because Vibe 2.27.0: the home is checked again after `--version`, but found …ReviewerOutcome+Ok* (the 2.26.1 row green before the fix — the measured branch already re-checked everything); *Expected type to be …NotStarted, but found …Ok* (the cached-package launch); *Expected VibeHome.Refusal(_home, Manifest) "" to contain "holds logs/session/plugins/packages/zz/zz-digest/mcp.json,"* (and the same for each declaration and hook file); *Expected VibeHome.Refusal(_home, pinned) "" to contain "holds …/mcp.json," because the pin admits those bytes only*. Green before and after, as positive companions: the four admitted rows |
| `TheProbeScriptsMinimalEnvironmentIsTheProductsTests` (epic 2's code round, codex, conventions + architecture) | `MINIMAL_WINDOWS` in `scripts/lib/vibe.mjs` — the base of every probe launch's built environment, imported by `scripts/lib/kimi.mjs` and every probe, retyped nowhere else — parsed out of the script (a `reads-another-program:` read with its reason) and held set-equal to `ProcessEnvironment.MinimalFor(isWindows: true)`, no duplicates; the parse fails LOUDLY on a missing declaration or an empty list (a doctored-text fact proves both, and that a dropped name is seen) | RED FIRST with `'OS'` dropped from a backed-up `vibe.mjs`, then restored: *Expected names to be a collection with 23 item(s) because the probes measure the launch the product makes - change scripts/lib/vibe.mjs and ProcessEnvironment.cs together, but {…} contains 1 item(s) less …* |
| `scripts/lib/kimi.test.mjs` (`node --test`, not in CI; epic 2's code round, codex, security) | the Kimi probe's `--scratch` must be private before the person's refresh token is copied into it (`scratchRefusal`): a fresh folder under the user's own temp folder admitted; a folder outside the profile refused naming it; a folder reached through a junction refused even when its target is private, a link anywhere on the way too, a junction pointing out of the profile refused, a dangling link refused rather than taken as an absent folder; on POSIX a copied home made `0700` and an existing folder others can read refused (skipped on Windows, which has no mode bits — the profile's ACL is what it relies on). The probe itself: `--scratch C:\coai-kimi-not-private` exits 65 with the sentence and creates nothing | RED FIRST against a stub that refused nothing, 4 of 5 Windows tests: *The input did not match the regular expression /refusing --scratch/u. Input: ''*; *… /link/u. Input: ''* (twice); *Expected "actual" to be strictly unequal to: ''* (the dangling link). Green after: 5 pass, 2 skipped on Windows |
| `VendorCatalogTests.EachBadEntryIsRefusedByName` (Team server, + a `vibe` row) | a `vibe` vendor in the Team server's catalog is refused by name (D12) | — (green with the code: `KnownRuntimes` is derived from `MachineOnlyRuntimes`) |

**The flow:** a Vibe row in a real code round is the `AVibeReviewerRunsThroughAFakeVibeTests` round test above (fake CLI,
Windows only). **NOT covered:** the real `vibe` through the product (a live plan and code round are S2's open tail, owed
before the plan's DoD); Vibe on a Linux or WSL host (refused, D18 — the refusal is covered, a run is not); a second Vibe
version's manifest (the requalification path is the script); concurrent Vibe processes on ONE home (serialised by
design, so never exercised).

**Taken to complexity 4 (pure refactor, 2026-10-13):** `RosterBuilder.BuildWork` (25, its `Add` local function among it) became
`FrameFor` / `CatalogIds` / `AddReviewer` / `ChoiceOf` / `ApiViewOf` / `PlaceOf` / `ReviewerFor` with `RosterFrame` and
`RosterCollector` (`Rounds/RosterAssembly.cs`); `ReviewerExecutor.LaunchTakenAsync` (10) became `StartAsync` /
`KilledAsync` / `Classified`; `VendorProbe.AskItsVersionAsync` (6) became `Answered` / `Refused` / `NotFound`;
`VibeStdout.PartText` (5) reads through `VibeExport.Text`. Measured with CA1502 at the repository's threshold
(`src_mcp/CodeMetricsConfig.txt`, `CA1502: 4`), switched on in `.editorconfig` for every file this story changed and
switched off again; held by the classes behind them (`*Executor*`, `*Roster*`, `*Workspace*`, `*Submission*`,
`*Repair*`, `*Custom*`, `*Launch*`, `*Turn*`, `*Round*`, `*Feature*`, `*Document*`, `*SecurityRoster*`,
`*SecurityLane*`, `*Usage*`, `*Ledger*`), green before and after. Still above 4 in those files and NOT touched by
this story: `RoundEngine.RunStageAsync` (38), `PanelService.ResolveUnderClaim` (12), `Finish` (10), `ReviewCodeAsync`
(10), `OpenDocumentSession`, `PruneOldScratchDirs`, `StatusAsync` (6), the constructor (5),
`ReviewerExecutor.Complaint` and `Longer` (6), `CodexRuntime.ReasonIn` (9) and `Said` (5), `VendorDiagnosis.Classify`,
`RosterBuilder.NamedAs` and `WhyNotCarried`, `RoundEngine.ApplyAnyHumanDecision` and `WhatTheCallerWasDoing` (5).
