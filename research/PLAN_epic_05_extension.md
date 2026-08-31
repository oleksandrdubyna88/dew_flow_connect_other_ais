# PLAN — epic 05: the ConnectOtherAIs VS Code extension

> Status: **IMPLEMENTED, 2026-08-31.** Story 5.3 shipped separately the same day, as its own plan. Epic 5 of 6 under
> [PLAN_connect_other_ais.md](PLAN_connect_other_ais.md) (its Phases 4–5). Commit df25020;
> 25 `node:test` cases; the `.vsix` was packaged AND installed into VS Code on this machine.
>
> **Deviations from the plan:**
> - **Story 5.3's loopback is NOT built.** Settings travel one way, in the `mcpServers` env block
>   the extension writes to the clipboard, and the rounds view reads the server's own session
>   files directly — so no port, no listener, no protocol between the halves. What that costs is
>   real: `ask_human` could not reach a modal, and the server's static refusal (epic 04) stood in.
>   That work shipped the same day — see [PLAN_escalation_loopback.md](PLAN_escalation_loopback.md).
> - Settings are VS Code `configuration` contributions rather than a webview: the same knobs, in
>   the editor's own settings UI, with no HTML to maintain. A webview buys presentation only.
> - The rounds view is a generated markdown document rather than a tree/webview — reads well,
>   copies into an issue, costs nothing to maintain.
> - `esbuild` 0.25 (0.28 does not exist); otherwise the creds toolchain unchanged.
> - Two snippet assertions were written across line wraps and failed on their first run; the text
>   was reworded and the assertions tightened rather than loosened.

## Goal

The human surface: configure providers, watch rounds, answer escalations, and install the server
with one button — with the same restraint creds shows (config offered on the clipboard, never
written into another program's file; binary into extension storage, never onto `PATH`).

---

## Story 5.1 — Settings that describe reality

*As the operator, I want every knob from the master plan's configuration table in one settings view,
and I want it to show which auth each provider is actually using, so configuration is a statement of
fact rather than hope.*

Work: settings UI for the whole table (providers, models, rounds, threshold, on-exhausted, ladder,
caps, timeouts, diff exclusions, role prompts with restore); provider rows show CLI found/version
and `own auth | vault key | unavailable(reason)` as reported by the server; settings persisted in
extension storage and served to `coai-mcp` over the loopback.

**Test cases**

| # | Test | Expected |
|---|---|---|
| 1 | `SettingsShape_RoundTripsThroughStorage` | serialize → load → identical (pure, `node:test`) |
| 2 | `DefaultsMatchTheMasterPlanTable` | one test per default, so plan drift is a red test |
| 3 | `ThresholdAndRoundsValidate` | 0 rounds, negative threshold, cap < 1 → named validation errors |
| 4 | `ProviderRow_RendersTheThreeAuthStates` | own auth / vault key / unavailable(reason) |
| 5 | `RolePromptRestore_ReturnsTheShippedDefault` | after an edit, restore is byte-exact |

## Story 5.2 — The install button

*As a new user, I want "Install the MCP server…" to download the right release asset into extension
storage and put the config block on my clipboard, so setup is one click and one paste.*

Work: port the `credsInstall.ts` decisions (RID for platform/arch — macOS honestly absent; asset
naming; version remembered because the binary is not asked; storage-not-PATH) for the `mcp-v` tag
line; the clipboard block is `{ "mcpServers": { "coai": { "command": "<full path>", "env": {
"COAI_CREDS_KEY": "…" } } } }` with `env` omitted when no key is configured, path through
`JSON.stringify` so Windows paths survive.

**Test cases**

| # | Test | Expected |
|---|---|---|
| 1 | `RidFor_MatchesTheReleaseMatrix` | win/linux × x64/arm64; darwin → undefined, said plainly |
| 2 | `AssetAndEntryNames_MatchTheWorkflow` | zip/tar.gz split, versioned inner path |
| 3 | `ConfigBlock_UsesTheFullPath_AndValidJsonEscaping` | `C:\Users\…` survives a parse |
| 4 | `EnvOmittedWhenNoKey` | no empty `env: {}` inviting questions |
| 5 | `UpdateOffered_OnlyWhenTheTagIsNewer` | version compare incl. the wrong-tag-prefix case |
| 6 | `InstallNeverTouchesPath_OrAnotherProgramsConfig` | asserted over the effect list of the pure planner |

## Story 5.3 — The loopback: rounds view and the human escalation modal

*As the human in "call a human", I want a live view of rounds and a modal that actually reaches me,
so escalation is a doorbell, not a log line.*

Work: a loopback listener in the extension (the creds broker pattern); the server posts round events
and `ask_human` requests; rounds view renders sessions → rounds → findings → decisions from
`globalStorageUri` artifacts; the modal shows the question + open findings, answer returned to the
blocked tool call; no window listening → the server's plain refusal path (already built in epic 04)
is what fires.

**Test cases**

| # | Test | Expected |
|---|---|---|
| 1 | `RoundEvent_AppendsToTheArtifactStore` | event in → artifact JSON versioned per round |
| 2 | `AskHuman_ResolvesTheBlockedCall_WithTheAnswer` | fake server ↔ real listener over loopback |
| 3 | `NoListener_ServerFallsBackToRefusal` | integration: extension absent → tool returns the named refusal |
| 4 | `RoundsView_RendersAFullSessionFixture` | plan rounds + code rounds + partial failures visible |
| 5 | `ExportCommand_WritesDocsReviews_OnlyOnExplicitCommand` | nothing under the repo without the command |

## Story 5.4 — The CLAUDE.md snippet

*As the operator, I want the instruction text that teaches a target repo's main AI when to call the
`coai` tools, offered as a paste, so adoption is explicit and reviewable.*

Work: a command producing the snippet (when to `open`, the two review gates, `resolve` with reasons,
what a refusal means) with the target repo's name interpolated; clipboard + a preview, never a write
into the user's CLAUDE.md.

**Test cases**

| # | Test | Expected |
|---|---|---|
| 1 | `Snippet_NamesAllSevenTools_UnderTheCoaiNamespace` | `mcp__coai__…` spelling throughout |
| 2 | `Snippet_StatesTheOrderingContract` | plan gate before code gate, in the text |
| 3 | `CommandCopies_NeverWrites` | effect list contains clipboard only |

## Definition of Done

- [ ] A `.vsix` builds in CI; pure-module tests run under `node:test` there.
- [ ] One manual pass on this machine: install button → paste config → `claude mcp list` shows `coai ✔`.
- [ ] The extension holds no secret of its own and opens no port beyond the loopback listener.
