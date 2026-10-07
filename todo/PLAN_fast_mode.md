# PLAN — a per-model "fast mode" switch, three states, Off by default

> Status: **plan only, nothing implemented yet (2026-10-07); revised after the coai plan round (session `33d6d6a3`,
> `proceed`, 8 findings: 6 accepted, 2 rejected with evidence) and an own critic — see *What the review changed*.** Scope: one catalog row field, drawn only on the NEW Settings
> page's model card; coai-mcp's codex and claude launches (reviewer, consultant, question row) and the api request for
> the dialects that have a fast tier; the chat's own launches in the extension.
>
> Related docs: [RESULTS_fast_mode_vendors.md](../research/RESULTS_fast_mode_vendors.md) (what each vendor offers, the
> owner's decision), [PLAN_api_streaming.md](PLAN_api_streaming.md) (the row field this follows, end to end),
> [PLAN_one_model_catalog.md](PLAN_one_model_catalog.md) (the catalog and its new page).

## Goal

The owner asked (2026-10-06) for a per-model "fast mode" setting on the new UI — codex and claude have one, others may
too — **off by default**. Asked whether "off" overrides the CLI's own setting, the owner chose three states:

| State | What coai sends |
|---|---|
| **Off** (default) | forces standard |
| **On** | forces fast |
| **As the CLI is set** | nothing — the CLI's own configuration applies |

Why it matters now: this machine's `~/.codex/config.toml` sets `service_tier = "priority"`, so coai's codex reviewers
and consultant very likely run at about 2–2.5× usage today, unasked. The default **Off** stops that the day a binary
that lists `fastMode` is installed.

## What each runtime is told (from the research; each spelling is measured before it ships — see Test plan)

| Runtime | Off | On | As the CLI is set |
|---|---|---|---|
| codex (reviewer, consultant — all three argv branches — question row) | `-c service_tier=default` | `-c service_tier=fast` | nothing |
| claude (reviewer, consultant, question row) | `--settings <file>` holding `{"fastMode":false}` | the same with `true` — Opus 5.5 / 5 / 4.8 only | nothing |
| api, a dialect with a MEASURED tier — `xai` only to start | the dialect's measured standard value | the dialect's measured fast value | nothing |
| antigravity, local, remote; codex on somebody else's endpoint (`CustomCodexRuntime`, `DeepseekRuntime`); api on `openai` (the generic dialect every unnamed endpoint uses), `dashscope` | — the control is not drawn — | | |

Spellings, from the code that already passes overrides: a codex `-c` value goes **unquoted, as its own argument**
(`CodexConsultant.cs:83` `"-c", "sandbox_mode=read-only"` and the comment at `:78-82` — TOML falls back to the raw string,
and a quote is unsafe through the npm `.cmd` shim). Claude's setting goes as a **file path**, not inline JSON: a JSON
argument carries double quotes through the same Windows shim (`ClaudeConsultant.cs:153` already guards line breaks for
that reason). coai-mcp writes two fixed files under its data folder (`fast-on.json`, `fast-off.json`), each holding exactly ONE key —
`--settings` loads *additional* settings, merged over the user's (claude help 2.1.258, :213-214), so nothing else changes —
written atomically and only when the content differs (several processes start). The extension keeps its OWN pair in its
global storage for the chat; neither depends on the other having run. An api row's tier reaches the `--ask-api` shim
on its command line (`--service-tier <value>`, as `--stream on` does, `ApiRuntime.cs:45`, `:112`) and is written into
the body by `ChatRequest.Body`.

**Which claude models:** Opus by FAMILY — `opus`, `claude-opus-5-5`, `claude-opus-5`, `claude-opus-4-8`, with or
without a `[1m]` suffix; an empty model (the CLI's own default) sends nothing in either state. **The restricted claude
consultant:** `--restricted` ignores the user's settings files (help :184-186), so there "As the CLI is set" is the
standard tier, not the person's `/fast` — the card says so for a row ticked Consultant.

## Decisions

1. **One row field, `fast`**: `"off"` (also absent), `"on"`, `"cli"`. Off is the default because the owner chose it; a
   row written before this field existed is Off too — which is the point (it stops the hidden priority tier).
2. **Which runtime/model has a fast tier is DATA**: a `fastMode` block in `shared/feature-availability.json` per runtime
   (codex: all models; claude: the three Opus ids), following its `thinking` block (`FeatureAvailability.cs:21-56`, the
   generator's `SEED_FIELDS`); and a `fastTier` value per dialect in `shared/api-dialects.json` (openai, xai), the file's
   "measured-only" rule kept — a dialect gets the value only from a recorded call.
3. **Capability, not version**: the field crosses `COAI_VENDORS` only to a binary whose `--features` lists `fastMode`
   (the `apiStream` precedent, `vendorsWire.streamOnTheWire`). A binary that does not list it is told nothing — and the
   card says so through the one note road (`modelCard.skewSaid`).
4. **Every launch of the row**: the reviewer (`RosterBuilder.SettingsFor`), the consultation and the question row
   (`ConsultantTurnInputs.Plain`, `QuestionFanOut.SettingsFor`) — through `ReviewerSettings.Fast`, set for EVERY runtime
   (not only inside `WithApi`, which is api-only). A consultant gets it through the catalog row it carries
   (`consultantRow`, C2 of the catalog plan).
5. **The chat** launches claude and codex from the extension; its own argv gets the same rule (a chat row is a catalog row
   ticked Chat).

## Stories

- **0 — measured FIRST** (the plan round: build nothing on an unverified spelling). On the installed CLIs, through the
  product path: codex with `service_tier = "priority"` in its config and `-c service_tier=default` / `=fast` on the
  command line — accepted, and the tier the run reports; the codex version floor that accepts it; claude `--settings
  <file>` with `fastMode` true/false — `fast_mode_state` in the result events. Recorded in
  `research/RESULTS_fast_mode_measured_<date>.md`. A spelling that fails changes this plan before any code.
- **A — the server.** `VendorDto.Fast` (`SettingsJsonContext.cs:66` neighbourhood) → `ProviderSettings.Fast` (parsed in
  `ParseVendors`, `PanelSettings.cs:1210-1265`) → `ReviewerSettings.Fast`; `FeaturesMode.Listed` gains `fastMode`; codex
  reviewer argv after `NoMcpServers.CodexArgs` and before `"-"` (`ReviewerRuntime.cs:438`); codex consultant in all three
  branches (`CodexConsultant.cs:75`, `:77`, `:83`); claude reviewer (`ClaudeRuntime.cs:78-93`) and consultant
  (`ClaudeConsultant.cs:99-138`, through its `Model` helper); the two settings files written once; api body
  tier per dialect through the shim (`ApiRuntime` argv, `AskApiMode` parse, `ChatRequest.Body` member — `xai` only);
  the question row (`QuestionFanOut.SettingsFor`, the plan round's finding); codex sent the flag only at or above the
  measured version floor, and a `VendorDiagnosis` pattern naming a codex config/value refusal.
- **B — the card.** `vendors.ts` field, `catalogFields.ts` (kept only where the runtime/model has a tier),
  `vendorsWire` gate, `binaryFeatures.FEATURES.fastMode`, a three-state select on the model card (new page only),
  `newTags` `model.fast`, help in the help file, the `skewSaid` note; the current page draws nothing.
- **C — the chat.** The chat's claude and codex argv apply the row's state, judged by the CONVERSATION's model
  (`cliChatLaunch.ts:263-264`), with the extension's own settings files; `ChatAdapter.argv` gains the state.
- **D — the person's flow and the docs.** A scenario: the state changed on the card, saved, and a launch of that row
  carrying it (the plan round's finding), with its entry in `research/module_tests.md`; module docs and
  `architecture.md`; promotion.

## Build order

0 (measure) → A (data and server, the test of each argv) → B (the card and the wire) → C (the chat) → D (the flow, docs,
promote).

## Test plan

- Server, per runtime and per state, the exact argv / body (RED first): codex Off → `-c service_tier=default` in every
  branch (reviewer, fresh consultant, resumed consultant, question row); On → `fast`; cli → no `service_tier`; claude Off
  / On → `--settings <path>` whose file holds the value; a non-Opus claude model On → nothing sent and the card says it
  has no fast tier; an api dialect without `fastTier` → no member; antigravity / local → nothing.
- Named by the own critic: `CustomCodexRuntime` and `DeepseekRuntime` argv carry no `service_tier`; the generic `openai`
  dialect sends no tier and the local body stays byte-identical (`LocalRequestBodyIsPinnedTests`); the shim's argv for
  the tier; `checkInputOf` carries `fast` (`--check-model`); one test each for the security lane and the feature
  roster (both through `RosterBuilder.SettingsFor`); a remote row sends nothing; a claude consultant under
  `--restricted` in the cli state and the card's sentence; claude with an empty model or an alias, state On; chat argv
  for codex fresh and resumed and claude agent and plain, by the conversation's model.
- The wire: a binary without `fastMode` is never handed the field; with it, every state crosses (unit + a seam leg).
- The card (run the page): the select is drawn only for a runtime/model with a tier, writes the row, defaults to Off;
  the current page draws nothing; the skew note for a binary without the capability.
- **Measured, not assumed (Story D):** codex with `service_tier = "priority"` in its config and `-c service_tier=default`
  on the command line runs at the standard tier (the run's own tier field or usage, as the CLI reports it); claude's
  result event's `fast_mode_state` for Off and On. Until measured, the help says "sent, not yet confirmed".

## Risks, said

- **Off changes today's behaviour for every codex row** (from priority to standard). That is the owner's choice; the
  release note must say it.
- A CLI too old for the flag refuses it: `-c service_tier` on an old codex, `--settings` on an old claude — the
  reviewer failure path names the refusal (`VendorDiagnosis`), and Story D records the versions measured.
- Claude's fast mode costs about 2× and is paid from usage credits — On is a person's explicit choice per row.

## What the review changed (2026-10-07)

- **Accepted (coai plan round):** an api Off sends the dialect's measured standard value, not nothing; a scenario for
  the person's flow; the chat keeps its own settings files; measurement first (Story 0); the question row named in
  Story A; a model without a tier is sent nothing in BOTH states.
- **Rejected, with evidence:** "`--settings` replaces `.claude/settings.json` and drops its hooks" — the CLI's help says
  it loads ADDITIONAL settings (2.1.258, :213-214); "absent should mean As the CLI is set" — Off for every row is the
  owner's decision, made to stop the hidden priority tier; the risk of an older CLI is met by Story 0, the codex
  version floor and a named refusal instead.
- **The own critic's facts, verified:** the generic `openai` dialect serves every unnamed endpoint, so it gets no tier;
  codex rows on another endpoint inherit `CodexRuntime.Build` and must be excluded; `--settings` predates fast mode
  (2.1.197), so an old claude ignores the key rather than refusing; `--restricted` ignores the user's settings; the
  chat's model is the conversation's; `skewSaid` lands with the catalog branch (PR #688) — until then the note uses
  `catalogShell.skew`; a Team server row is not forwarded the field (its server's contract does not carry it).

## Progress

- **Story 0, 2026-10-07** — measured ([RESULTS_fast_mode_measured_2026-10-07.md](../research/RESULTS_fast_mode_measured_2026-10-07.md)):
  codex reads and checks `-c service_tier`, dropping an unadvertised value with a warning; claude accepts the documented
  `--settings` fastMode, Off reports `off`, On is held off on this account by its own preference (`fast_mode_disabled_reason:
  preference`). Open: the `xai` tier (no key export yet).
- **Story A (server), 2026-10-07** — `FastMode`, `ReviewerSettings.Fast`, the row field and its parse, all three launch
  places, codex (`CodexRuntime.TierArgs`, reviewer and every consultant branch, none on another endpoint), claude
  (`ClaudeFastMode`, a one-key file), `fastMode` in `--features`, and the data block in `shared/feature-availability.json`
  read by both halves. Seven tests that pinned the full argv now carry the Off flag — the intended change. Not yet: the
  api tier (waits for a measured `xai` value).

## Definition of Done

- [ ] Every launch of a row with a fast tier carries its state's flag, tested per runtime, per state, per launch kind.
- [ ] The field crosses only to a binary listing `fastMode`; the card says when it would be ignored.
- [ ] The control exists only on the new page, three states, Off by default; help and "new" tag.
- [ ] The chat applies it.
- [ ] One measured run per runtime recorded in `research/`; module docs and `architecture.md` updated.
- [ ] Full suites green; the coai plan and code gates passed; PR merged; no release unless the owner says so.
