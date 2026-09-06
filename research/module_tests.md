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
| `ask_human` | **no** | It BLOCKS until a person answers in the panel or on their phone, and the wait is the behaviour; a scenario that answered it from a fake surface would be exercising the fake. Its pieces are covered by `EscalationsTests` and `HumanDecisionTests`. |

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
| **Every command, through a real extension host** | **no** | There is no extension-host harness here yet: `@vscode/test-electron` downloads a VS Code build and runs a suite inside it, which no workflow does today. This is the largest single gap in the repository. |

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
