# PLAN — epic 04: coai-mcp — the server that holds the protocol

> Status: **plan only, nothing implemented yet.** Epic 4 of 6 under
> [PLAN_connect_other_ais.md](PLAN_connect_other_ais.md) (its Phase 3). Depends on epics 02 (state
> machine, contract) and 03 (runners). After this epic the whole loop works from any MCP client —
> before the extension exists.

## Goal

A Native-AOT stdio binary exposing `providers / open / review_plan / review_code / resolve / status /
ask_human` under the client id `coai`, built by hand rather than on the SDK host — the measured
reason: the hosted default logs to stdout, and stdout carries JSON-RPC
(`dew_flow_creds_for_devs/src_mcp/src/Program.cs:19-25`).

---

## Story 4.1 — The skeleton: stdio, stderr logging, the read-only tools

*As an MCP client, I want to spawn `coai-mcp` and get a clean handshake, a tool list, and honest
`providers`/`open`/`status` answers, so the surface exists before the expensive tools do.*

Work: hand-built server on the `ModelContextProtocol` package (AOT, `StripSymbols`,
`JsonSerializerIsReflectionEnabledByDefault=false`); `AddDewFlowLogging` with console→**stderr**;
`providers` probes each enabled CLI (found on disk? answers a version call?); `open` creates/returns
the session and prunes worktrees; `status` reads the persisted session store; session state persisted
under the server's data dir so a client restart re-orients instead of forgetting.

**Test cases**

| # | Test | Expected |
|---|---|---|
| 1 | `Initialize_ThenToolsList_NamesTheSevenTools` | against the built binary over real stdio |
| 2 | `Stdout_CarriesOnlyProtocolBytes` | run with verbose logging → stdout parses as pure JSON-RPC |
| 3 | `Providers_WithACliMissing_ReportsItByNameWithTheReason` | rename the fake CLI → named, not omitted |
| 4 | `Open_IsIdempotentPerRepoAndBranch` | two opens → one session id |
| 5 | `Status_SurvivesAServerRestart` | kill, respawn, `status` still knows the rounds |
| 6 | `HelpFlag_PrintsToStdoutAndExitsZero` | the one sanctioned stdout write, as in creds |

## Story 4.2 — The review tools, wired end to end

*As the main AI, I want `review_plan` / `review_code` / `resolve` to run the real fan-out and answer
with normalised findings and a verdict, with the ordering enforced by refusal.*

Work: `review_plan` (N providers × 1), `review_code` (N × 3 roles over the shaped bundle), both
returning findings + verdict + per-reviewer outcomes; `resolve` records decisions-with-reasons and
advances the state machine; every refusal sentence comes from epic 02's types unchanged. Role
prompts ship as files with an override layer (file default → stored override → restore).

**Test cases**

| # | Test | Expected |
|---|---|---|
| 1 | `ReviewCode_BeforePlanProceed_RefusesOverTheWire` | the epic-02 refusal, observed via MCP |
| 2 | `ReviewPlan_FansOutOnePerProvider_AndMergesFindings` | fake CLI, two providers → dedup visible in the answer |
| 3 | `ReviewCode_FansOutThreeRolesPerProvider` | six fake launches recorded, three distinct role prompts each vendor |
| 4 | `Resolve_WithoutAReason_RefusesPerFinding` | a bare reject is not a decision |
| 5 | `VerdictAfterMaxRounds_MatchesConfiguredOutcome` | `continue`/`human`/`escalate` reach the wire |
| 6 | `RolePrompt_OverrideAndRestore_RoundTrips` | edited prompt used; restore returns the shipped file |

## Story 4.3 — Keys from the creds `config` entry

*As the operator, I want vendor keys read once at startup from one vault `config` entry, so an agent
is never in the chain and a missing key is a named condition, not a crash.*

Work: `COAI_CREDS_KEY` env → run `creds config <key>` at startup → parse the JSON → apply per-vendor
env to reviewer children. Missing binary, missing key, 401 (wrong or revoked — indistinguishable by
design, `brokerConfigRoute.ts:44-51`), malformed body: each is a named per-vendor unavailability in
`providers`. No key value ever reaches a log line or argv.

**Test cases**

| # | Test | Expected |
|---|---|---|
| 1 | `NoCredsKeyConfigured_KeylessVendorsStillWork` | codex/gemini unaffected |
| 2 | `Creds401_MarksOnlyKeyedVendorsUnavailable_WithTheReason` | fake `creds` exe answering 401 |
| 3 | `MalformedConfigJson_IsANamedCondition` | never a partial apply |
| 4 | `KeyValue_NeverAppearsInLogsOrArgv` | grep the run's log file and recorded argv for the fixture key |
| 5 | `ConfigIsReadOnce_ProvidersReportsWhen` | rotation takes effect on restart, and `providers` says so |

## Story 4.4 — Published, released, contract-tested

*As the installer (epic 05), I want tagged releases carrying the four RID assets, each proven to
answer a real handshake, so the install button has something true to download.*

Work: release workflow on `mcp-v*` tags — publish `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`
(the creds matrix), archive naming per `credsInstall.ts` conventions; a smoke step runs the published
binary through `initialize`/`tools/list` before the release is created.

**Test cases**

| # | Check | Expected |
|---|---|---|
| 1 | Tag `mcp-v0.1.0` on a branch build | four assets with the exact expected names |
| 2 | Release smoke step | published binary answers the handshake; a broken publish blocks the release |
| 3 | `AssetNameFor`-equivalent unit tests | zip for `win-*`, tar.gz for `linux-*`, version embedded |
| 4 | AOT publish warnings | zero trim/AOT warnings, enforced in the workflow |

## Definition of Done

- [ ] From a plain Claude Code session with only this binary configured, the full loop (plan rounds → code rounds → resolve) runs against the fake CLI.
- [ ] Stdout purity, ordering refusal, and key hygiene each hold in a test against the **published** binary, not only in-process.
