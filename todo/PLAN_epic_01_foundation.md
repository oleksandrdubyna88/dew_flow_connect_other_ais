# PLAN — epic 01: repository foundation

> Status: **plan only, nothing implemented yet.** Epic 1 of 6 under
> [PLAN_connect_other_ais.md](PLAN_connect_other_ais.md) (its Phase 0). Everything else builds on
> this; nothing here depends on anything else.

## Goal

A repository that is a full member of the `dew_flow_*` family before the first line of product code:
shared rules mounted, logging wired, tests runnable, CI honest. The master plan's own checker run
proved why this cannot wait — `plan-lifecycle.mjs` against this repo today answers *"nothing to
check"*, a green that means blind.

---

## Story 1.1 — Conventions mounted, knowledge base seeded

*As the operator, I want this repo to load the same shared rules and pass the same checks as every
other `dew_flow_*` repo, so a session here behaves like a session anywhere in the family.*

Work: submodule `dew_flow_conventions` at `.claude/rules/shared` (per `ROLLOUT.md`),
`settings/settings.json` copied byte-identical from the reference, `CLAUDE.md`, `todo/README.md`
with the *Currently open* table, `research/README.md` + `research/architecture.md` (skeleton with
the container diagram from the master plan). Adding the consumer row to
`dew_flow_conventions/README.md` is a conventions edit — the pin bump in every consumer happens in
the same task, per the editing discipline.

**Test cases**

| # | Check | Expected |
|---|---|---|
| 1 | `node .claude/rules/shared/tools/plan-lifecycle.mjs` | exit 0, and it **does** check (todo/ + research/ pair exists) — not "nothing to check" |
| 2 | `node .claude/rules/shared/tools/pin-check.mjs` | exit 0 — pin at remote tip |
| 3 | Delete one `PLAN_*` row from `todo/README.md`, rerun checker | exit 1 naming the omitted plan (proves the check has teeth here) |
| 4 | `git diff --no-index settings/settings.json .claude/rules/shared/../settings/settings.json` equivalent byte compare | identical |
| 5 | Fresh clone + `git submodule update --init .claude/rules/shared` | rules visible, checker runnable |

## Story 1.2 — Solution skeleton with logging that obeys the family rule

*As a developer, I want `dotnet build` + a running MTP test exe + a log file per run from day one, so
every later story lands into working scaffolding instead of building it as a side quest.*

Work: `dew_flow_connect_other_ais.slnx`, `global.json`, `Directory.Build.props` /
`Directory.Packages.props` (per the NuGet monthly-latest policy; FluentAssertions pinned 7.2.2),
`src/CoaiMcp` empty AOT-publishable exe, `src/ServiceDefaults` with `AddDewFlowLogging(appName,
consoleToStdErr)` per `logging-serilog.md` (ANSI console sink written by hand, file sink
`logs/{yyyy-MM-dd}/{app}-{HH-mm-ss}-{pid}.log`, UTC everywhere), `tests/CoaiMcp.Tests` (xUnit v3,
MTP — runs as an executable, never `dotnet test`).

**Test cases**

| # | Test | Expected |
|---|---|---|
| 1 | `Run_WritesOneLogFile_UnderUtcDayFolder` | one file matching the path shape; timestamps UTC |
| 2 | `SecondRun_WritesASecondFile` | two files, distinct names — never a rolling append |
| 3 | `StdioMode_SendsConsoleSinkToStderr` | with `consoleToStdErr: true`, stdout stays empty |
| 4 | `RedirectedStdout_StillCarriesAnsiEscapes` | escape bytes present when redirected (the measured Serilog-theme failure is why this is asserted, not assumed) |
| 5 | `./tests/CoaiMcp.Tests/bin/Debug/net10.0/CoaiMcp.Tests.exe` | runs and reports; `dotnet test` is not used anywhere |
| 6 | `dotnet publish -r win-x64` on `CoaiMcp` | AOT publish succeeds with zero trim warnings |

## Story 1.3 — CI that runs what a developer runs

*As the operator, I want every push checked by the same commands I run locally, so a red is a real
red and a green means the family checks actually executed.*

Work: GitHub workflow — checkout, `git submodule update --init --depth 1 .claude/rules/shared`
(that one submodule only, per `ROLLOUT.md`), build, run the test executable, run both `.mjs` checks.

**Test cases**

| # | Check | Expected |
|---|---|---|
| 1 | Push with all green | workflow passes; log shows the test exe ran and reported a count > 0 |
| 2 | Push a deliberately failing unit test on a branch | workflow fails at the test step, not later |
| 3 | Push with a stale conventions pin on a branch | `pin-check.mjs` fails the workflow |
| 4 | Workflow log | only `.claude/rules/shared` fetched — no recursive submodule drag |

## Definition of Done

- [ ] All three stories' checks pass in CI on `master`.
- [ ] The consumers table in `dew_flow_conventions/README.md` lists this repo, pins bumped everywhere in the same task.
- [ ] `plan-lifecycle.mjs` here checks a real todo/ + research/ pair.
