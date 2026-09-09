# module_tests — the harness, the flows it drives, and what it does not prove

> The record the family rule `common/scenario-tests.md` requires of every repository, at the fixed
> name it requires, so a reader looking for the tests of an unfamiliar repository has one place to
> look. Enforced in part by `ScenarioCoverageTests` (`src_mcp/tests/ScenarioCoverageTests.cs`), which
> derives the flow list from the tool registry rather than from this document.
>
> [testing.md](../.claude/rules/shared/common/testing.md) governs how a test is written and believed;
> this describes what exists.

## Where the harness is

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
