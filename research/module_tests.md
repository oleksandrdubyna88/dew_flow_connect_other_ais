# module_tests — the harness, the flows it drives, and what it does not prove

> The record the family rule `common/scenario-tests.md` requires of every repository, at the fixed
> name it requires, so a reader looking for the tests of an unfamiliar repository has one place to
> look. Enforced in part by `ScenarioCoverageTests` (`src_mcp/tests/ScenarioCoverageTests.cs`), which
> derives the flow list from the tool registry rather than from this document.
>
> [testing.md](../.agents/conventions/common/testing.md) governs how a test is written and believed;
> this describes what exists.

## Where the harness is

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

## When it runs

- **The two test suites: in CI, on every push and pull request**, plus the family checks (pin,
  plan lifecycle) and an AOT publish. A release is not cut without them.
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
| `resolve` | yes | `EndToEndTests`, `RoundAuditTests` — a decision is recorded for every finding |
| `status` | yes | `McpContractTests`, `CallerSessionsTests` |
| The remote reviewer (a Team server vendor) | yes | `RemoteShimScenarioTests` — the REAL `coai-mcp --ask-remote` binary, in its own process, against a real `HttpListener` on a real socket. It is not an MCP tool, so it is not in the registry this table is derived from; it is here because the wiring between an adapter's argv and a separate process is exactly what in-process tests cannot see, and this repository has shipped that break twice |
| A round that ANSWERED NOTHING | yes | `EndToEndTests.ARoundThatFoundNothing_SaysWhatItSent_AndKeepsWhatItWasTold` — a whole plan round and code round in which every reviewer returns an empty findings array, asserting the two `context for review:` lines, each reviewer's prompt SIZE in the opening line, and the eight raw answers on disk under `empty/`. Not an MCP tool, so it is not in the registry this table is derived from; it is here because the three facts live in three classes (`PanelService` assembles, `RoundAudit` writes, `ReviewerExecutor` keeps) and the 2026-09-08 defect was that nothing joined them |
| A role NOBODY COMPILED IN, reviewing a change | yes | `ACustomRoleReviewsAChangeTests` — a repository, a branch, a plan gate, a code gate and a vendor answering through the fake CLI, with the four shipped code roles switched off so the round is the custom role alone: the round reports one reviewer, the finding carries that role's name, and the prompt the adapter was handed is the text from `<dataDir>/prompts/<id>.md`. Not an MCP tool, so it is not in the registry this table is derived from; it is here because every unit test of this feature would have stayed green with the enum replaced by a string and the round still asking a compiled-in list — the question "does a role a person defined actually review anything" is answered by no class alone. It also carries the plan stage's half — a plan-stage role a person added, asked for by the round rather than by a hardcoded array, read back from the round's own `reviewerStates` because two reviewers reporting the same thing merge into one finding under one role. Its unit-level siblings are `ACustomRoleReachesAReviewerTests` (each resolution path, including the plan stage's dealt lenses and the stale-prompt fallback), `TheRoundSaysWhatItCouldNotAskTests` (the edges of the two guards: a prompt file that exists and says nothing, a shipped role whose prompt somebody edited, a role that fails both guards at once, and one role refused once however many lenses it would have been dealt) and `RoleCompositionTests` (what a row is refused for) |
| A stubbed `fetch`'s response | yes | `responseFixture.ts` — the runtime's own `Response` constructor, not an object asserted into one. `responseFixture.test.ts` guards it: no `.ts` under `src/test` may contain `as Response`, checked against the SOURCE with comments stripped, because the compiled output has already erased the cast and a check reading `out/` passes against any number of offenders |
| The client half of the HTTP contract | yes | `teamServerApi.test.ts` — `ask()` against a stubbed `fetch` that carries real `Headers`: the number is read on the success arm and on a 426, an absent header is `0`, an unreadable one (`''`, `' '`, `0x10`, `1e2`, `v2`) is `undefined`, a body that throws still keeps what the server said, and no response at all records nothing. `teamServerView.test.ts` covers the sentence it produces. Not an MCP tool, so it is not in the registry this table is derived from; it is here because the two halves of the contract live in two languages and the server's half has been correct and unread since it shipped |
| `ask_human` | **no** | It BLOCKS until a person answers in the panel or on their phone, and the wait is the behaviour; a scenario that answered it from a fake surface would be exercising the fake. Its pieces are covered by `EscalationsTests` and `HumanDecisionTests`. |

A note on why the remote row exists at all. The catalogue is derived from the TOOL registry, and a
vendor runtime is not a tool — so nothing would have failed had the remote flow gone uncovered. What
makes it worth a row is the shape of its failures: the adapter builds a command line, a different
process reads it, and the two agree only until somebody edits one of them. The same shape produced
this repository's two most expensive misses — a released server binary that could not serve a single
request under `PublishAot`, and a page whose minified binding was renamed — and in both cases every
unit test was green. `RemoteShimScenarioTests` runs the real binary end to end, including the two
cancellation paths: the shim reaching its own deadline (it cancels and says so), and the shim being
KILLED (it leaves a claim the parent acts on).

### The extension's flows

The VS Code commands in `src_vs_code/package.json` (`contributes.commands`) are the extension's
flow list. They are **not** driven by a scenario that launches an extension host: what is covered is
each command's decision — the pure function it routes to — plus the page as it ships. The gap is
real and named here rather than implied:

| Flow | Covered | By |
|---|---|---|
| The chat tab's page | yes, as it ships | `bundledPage.test.ts` runs the bundled, minified page and drives its **Send** button through to the one posted turn, asserting the composer locks on the way — the flow the pinned composer added. `chatPage.test.ts` runs the same script from source through `runChatPage()`, which answers only the ids the page actually renders, so a control that stops being rendered stops being testable rather than silently passing |
| The rounds log page | yes, as it ships | `bundledPage.test.ts` bundles and minifies the real module and runs the page script — the only test that catches a minifier-renamed binding, which shipped twice |
| Install / update the server | decisions only | `install.test.ts` (RID choice, asset names, companion policy, the release workflow's own guarantees) |
| Copy the config block / the CLAUDE.md snippet | decisions only | `install.test.ts`, `panelServerPromptAgreement.test.ts` |
| Panel settings writes | decisions only | `settingWrite.test.ts` — the routing decision, not the `vscode` call it leads to |
| Add / sign in to / sign out of / remove a Team server | decisions only | `teamServers.test.ts`, `teamServerAuth.test.ts`, `teamServerApi.test.ts`, `teamServerView.test.ts` — the canonical URL and token path (against the shared cross-language fixture), the scope trust boundary, the sign-in compensation, the renewal flag, every request's failure path with a stubbed `fetch`, and the section's markup. The `vscode` calls those decisions lead to are NOT covered, for the reason in the row below |
| Add a reviewer FROM a Team server | decisions only | `remoteVendorRow.test.ts`, `remoteVendorCard.test.ts` — the row shape, the catalog-fed model list, the canonical server match, and the fields a remote row must not show. The quick-pick itself needs an extension host |
| A reviewer the server cannot run, badged on its card | decisions only | `providersBadge.test.ts` — the three states and that only `unavailable` draws anything, the parser against every shape a different build could hand it, and a row the server did not mention reading as UNKNOWN rather than as fine. The spawn of `--providers` itself is the row below |
| Team-server spending, and *Company* | decisions only | `teamUsage.test.ts` — the per-server block, the admin gate, and what a server that has not answered renders as |
| Mirroring the settings to the file the server reads | decisions, the CONTENTION, and the SEAM | `settingsSync.test.ts`, `settingsLock.test.ts`. Covered: that a change reaches the file with no panel open; that an older build stands down from a newer stamp and says so once per version; that a file which cannot be READ is not a file that is not there; that the read, the comparison and the write happen in that order inside one section; that two syncs over one lock and one file interleave to exactly one writer and one whole payload; and when a lock left by a dead window may be broken, including a clock that went backwards. NOT covered: the lock itself — `fs.open(…, 'wx')`, the owner token, the stale break and the atomic rename all need a real filesystem and two real extension hosts, which is the row below. **The seam IS covered live**: `npm run test:seam` (`scripts/run-seam.mjs`) writes the file with the extension's own `serverSettingsJson`, signs a throwaway data directory in at the token path both sides derive independently, serves a catalog from a loopback HTTP server, and runs the REAL `coai-mcp --providers` against it — asserting the Team-server row resolves under the name its SERVER knows. Watched failing with `remoteVendor` removed from `vendorsEnv`, which is the defect of 2026-09-07 reproduced by the two implementations rather than described by a fixture |
| Chat with other AI — the trigger | decisions only | `chatSettings.test.ts`, `chatCapture.test.ts`, `chatModels.test.ts`, `chatWiring.test.ts` — every settings fallback; both doors and all three `chatAutoSend` values; the clipboard borrow including the case that must NOT restore, the unreadable clipboard that must not be written to, and the marker collision; which models may be asked and which are refused by name; and the manifest wiring itself — that the command, the keybinding and the `webview/context` item all name the id `extension.ts` registers. NOT covered: the synthetic `Ctrl+C`, which needs a real webview with a real selection — it was MEASURED with a probe extension instead (638 characters, 1725 ms) and the numbers are in `module_extension.md` |
| Chat with other AI — the conversation | decisions only | `cliChatSession.test.ts`, `chatPage.test.ts`, `chatPanel.test.ts`, `chatMessages.test.ts`, `chatPrompt.test.ts` — the four ways a long-lived child ends and what each says; the page as it renders; the host boundary that refuses a model the conversation was not offered; and the two turns a model is ever sent: the opening one with its fenced passage, and the CARRIED one that hands a whole conversation to a model that never heard it (questions and answers both, attributed, fenced with a per-turn id, bounded at 60 000 characters with the loss stated). NOT covered: the switch itself — replacing a live session under an open panel is `vscode` state, and it belongs to the row below |
| Chat with other AI — what a turn COST | decisions, and the file | `chatUsage.test.ts`, `chatUsageFile.test.ts`, `chatRows.test.ts` — that a cumulative vendor's numbers are differenced against what it last said and that the rule is keyed on the RUNTIME rather than on a vendor row a person can rename; that money is differenced by the same rule so the two cannot drift apart; that a turn which was stopped or failed is still written down; that a turn nobody reported numbers for reads as UNKNOWN and never as free; and, against a real temp directory, that the ledger appends rather than truncates, that queued writes land in the order they were asked, that a half-written tail costs itself alone, and that a ledger which cannot be written costs the record and never the person's answer. On the log side: the Kind column, the Kind facet, the merge of the two ledgers by time, and that a chat row leaves the columns it has no answer for empty. NOT covered here: several PROCESSES appending at once, which a unit test cannot fork and mean anything by — measured instead, see the row in *What this does NOT prove* |
| Chat with other AI — WHAT IT COST ON THE SPENDING PAGE, and HOW OFTEN IT WAS REACHED FOR | decisions, the wiring, and the page | `chatDoors.test.ts`, `chatDoorsFile.test.ts`, `chatSpendRows.test.ts`, `chatSpendSection.test.ts`, `chatWiring.test.ts`, `chatPresetsPage.test.ts`. Two counts that no turn ledger can answer — *Asked* (`take the question` + `add the question`) and *Opened* (every door) — because a turn is written when a turn FINISHES and `add the question` finishes none. Covered: the door record round trips and a torn line costs itself alone; **that a door line would be read as a TURN by the other ledger’s parser**, which is why it lives in a file of its own and is asserted so that moving it back breaks a test rather than a page; that a door this build has never heard of still counts as an opening but not as a question; the arithmetic — per vendor AND model, the window bounding tokens and both counts but never the all-time column, a bill and an estimate never summed into one number, a row that is part billed and part not keeping BOTH, a cost nobody could have charged treated as no cost, a record whose instant cannot be read belonging to no window, a door that resolved nothing landing in its own named row, and the section total counted from the ledger rather than from the rows that survived the filter; that the whole ledger is read ONCE rather than once per row, counted rather than timed; and the rendered tab — both headings, the rule between them, a total each, and the counts in the row. **The command wiring is asserted against the SOURCE of `extension.ts`, with the door list DERIVED from the manifest’s menus and keybindings**: each door records exactly one invocation, before the work that can refuse it, and nothing records a door the manifest does not offer — so a sixth way into a chat fails this rather than quietly counting zero. Against a REAL directory, like the turn ledger beside it: that the line reaches the file and comes back, that a second invocation is appended rather than written over the first, that queued writes land in the order they were asked for, that a directory nobody has written to yet is made rather than refused, that a half-written tail costs itself alone, and that a ledger nobody has written is no invocations rather than a failure. **And that the vendor is the one WRITTEN DOWN**: a preset is a row somebody edits, so resolving an old line through the list as it stands today would move a year of history to another vendor - the recorded value wins, and the resolver answers only for lines written before the field existed. NOT covered: the commands through a real extension host, which is the row below |
| Chat with other AI — WHERE A HANDOVER STARTS | decisions, the wiring, and the page as it ships | `chatCarry.test.ts`, `chatWiring.test.ts`, `chatPage.test.ts`, `bundledPage.test.ts`. A conversation is handed whole to a model that never heard it in five places — a Team server every turn, a model switch, a re-ask, a vendor that lost the thread, and the first turn after a reload — and **Carry nothing above** moves where that handover begins. Covered: the position itself (past the end carries nothing; a negative one never reaches `slice`, where it would be an offset from the END and carry the last message instead of the suffix; nothing but a finite non-negative integer is a position at all, from the page and from the store); that ALL FIVE consumers go through the one function, counted structurally rather than listed, so a sixth added later without the mark fails; that a mark set AFTER a switch reaches the conversation that switch had already staged, which is the case the feature exists for; that the mark only moves forward, since the button is offered on the last answer alone and a lower position is a stale page or a forged message; that a re-ask brings the mark back with the transcript it truncated; and, in the SHIPPED bundle, that the button posts the index it names through the delegated listener — with a `closest` stub that honours the real selector, because one that did not made this very test pass against a button that could not have been pressed. NOT covered: an actual Team-server turn or model switch end to end, which needs the extension host in the row below; what is asserted instead is that both build their carry through the one function |
| **Every command, through a real extension host** | **no** | There is no extension-host harness here yet: `@vscode/test-electron` downloads a VS Code build and runs a suite inside it, which no workflow does today. This is the largest single gap in the repository. **It is what stands between the settings lock's unit tests and its guarantee**: two hosts writing one file is the failure that started epic 2 of `PLAN_team_server_reviewer_never_called`, and the interleaving test above simulates it rather than reproducing it. |

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
