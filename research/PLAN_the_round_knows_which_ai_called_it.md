# PLAN — the round knows which AI called it, and which model

> Status: **IMPLEMENTED, 2026-09-13.** Scope: `src_mcp/src/Server/CallerSessions.cs`,
> `src_mcp/src/Server/CallerDeclaration.cs`, `src_mcp/src/Tools.cs`, the `rounds` table's write and
> read paths, the rounds log page, and one new shared rule in `dew_flow_conventions`.
>
> **What shipped differently from the plan below, and why — the valuable part of the record:**
>
> 1. **The handshake outranks the environment for the VENDOR.** The plan derived the vendor solely
>    from the four session variables. Gemini raised it as Blocking on the plan round and was right:
>    those variables belong to the PROCESS, inherited from whatever launched the server and fixed for
>    its life, while `clientInfo` is negotiated on the connection that is calling. A server whose
>    environment says claude and whose caller is codex would have recorded the launcher for ever. So
>    `clientInfo.name` decides the vendor when it maps to a known one, and the variable is the
>    fallback.
> 2. **`McpServer` injection reads the REQUEST-scoped server**, which the plan assumed but did not
>    check. The SDK's own documentation settles it twice over: an `McpServer` parameter is bound to
>    the instance of this request's `RequestContext` and is excluded from the tool schema, and on the
>    `2026-07-28` protocol revision `ClientInfo` travels per request in `_meta` rather than being
>    fixed at `initialize` — so the root server would have been the wrong object to read.
> 3. **`callerModel` needed an explicit `= null` default, not merely a nullable type.** The first
>    build bound it as required, and a two-argument `open` — what every client predating the argument
>    sends — came back *"An error occurred invoking 'open'"*. Caught by the contract test written for
>    exactly that case, which is the whole reason it was written before the code.
> 4. **The database migration is TRANSACTIONAL.** Codex raised the missing migration as Blocking; the
>    repository already had the mechanism (`Schema.Steps` + `user_version`), so the fix was one
>    appended `ALTER` step. But the step and the version bump were two statements, and a process
>    killed between them would re-run the alter, hit `duplicate column name`, and — because `Open`
>    answers null to any exception — leave a database that never opens again. They are one
>    transaction now.
> 5. **The shared rule is a THIRD file, not a sentence in the gate rule.** `coai-review-gate.md` is
>    one of the 24 bodies `tools/rules.test.mjs` hashes against baseline `5d6984eb`, so step 1 could
>    not grow a sentence. `common/coai-caller-model.md` says where it belongs when the inventory
>    retires, and carries its own `coai-caller` marker so a paste made before it is reported as
>    older rather than silently never sending a model.
> 6. **The plan's step 6 is done here**: the conventions change is
>    [dew_flow_conventions#27](https://github.com/oleksandrdubyna88/dew_flow_conventions/pull/27),
>    merged as `db71a0d`, and the pin cascade rides with it.
>
> **Nothing is outstanding.** Every item of the Definition of Done below is met.
>
> Issue [#174](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/174): *"I used
> to work only with Claude. Now there is Codex Astra as well, and I need to track through the MCP —
> if possible — who is calling, and which model. claude-opus, claude-fable, codex-astra."*
>
> **Decided by the operator, 2026-09-13:** the model is **declared by the calling AI on `open`**, not
> read from an environment variable. Do not reopen that choice; its consequences are written into
> the requirements below.

## Two halves, and only one of them is a design question

### The identity half is already three-quarters built

`CallerIdentity.From` (`src_mcp/src/Server/CallerSessions.cs:20-46`) reads, in order:

```
COAI_CALLER_SESSION, CLAUDE_CODE_SESSION_ID, CODEX_SESSION_ID, GEMINI_CLI_SESSION_ID
```

and returns **only the value** — *the name of the variable that matched is thrown away*. That
discarded name is exactly the vendor half of what the issue asks for.

It is already persisted: `CallerFor(session)` (`PanelService.cs:1234`) feeds `RoundContext.Caller`,
which is bound into the `rounds.caller` column (`Schema.cs:52`, `RoundsDb.cs:234`). And it is
already **never read back**: `RoundsQuery.Rounds` does not select it and `LoggedRound` has no field
for it, so nothing in the extension can show it.

So: keep which variable matched, select the column, put it on the page. No decision needed.

### The model half has no source in the protocol

MCP's `initialize` carries `clientInfo { name, version }` — `claude-code`, `codex`, `gemini-cli` —
and **no model field exists in the protocol at all**. The SDK exposes it as `McpServer.ClientInfo`,
and `McpServerTool.Create(Delegate, McpServerToolCreateOptions)` — the overload `Tools.cs` already
uses — injects `McpServer` into a tool lambda without publishing it in the tool's schema. That gives
the CLIENT for free. It cannot give the model, because the model is not there.

## What must be true when this is done

1. A round records **which vendor** called it, from the variable that matched, and the client's own
   name and version from the handshake. `contract-test` clients and unknown callers are recorded as
   what they are rather than guessed at.
2. A round records **the model the calling AI declared**, on `open`.
3. **A caller that declares nothing is recorded as declaring nothing** — never as a default, never as
   a guess. This is the direct consequence of the operator's choice and the thing most likely to be
   got wrong: the field is optional, so the honest rendering is *"not stated"*, not *"claude"*.
4. The rounds log **shows** all of it: the log is where the question is asked.
5. Sessions and databases already on disk stay readable — every new column and field is nullable or
   trailing-defaulted, as `ReviewerState.Model` was.

## Why the operator chose the declaration over an environment variable

Recorded so the next reader does not re-litigate it. `COAI_CALLER_MODEL` beside the existing
`COAI_CALLER_SESSION` would have been cheaper and needs no protocol change — but it is set when the
client STARTS. A person who switches model mid-session with `/model` gets a log that confidently
names the model they stopped using, and a confidently wrong log is worse than a silent one (the same
argument `PLAN_the_log_names_the_model.md` makes about a Team server's model). The declaration
tracks the switch because it is sent per `open`.

## The cost this choice carries, and it is not in this repository

The AIs have to be TOLD to send it, and the place that tells them is the gate snippet — which is a
**shared rule in `dew_flow_conventions`**, mounted here through `.agents/conventions` and checked for
parity by `gate-snippet-check.mjs` in CI. So this plan's last step is a conventions change plus the
pin cascade across every consumer repository. Budget it; it is the largest single piece of work here
and none of it is C#.

## The change

- `CallerSessions.cs` — `CallerIdentity.From` returns `(Vendor, Id)` rather than the id alone. The
  vendor is derived from the variable that matched (`CLAUDE_CODE_SESSION_ID` → `claude`), with
  `COAI_CALLER_SESSION` meaning "stated by the operator, vendor unknown".
- `Tools.cs` — the `open` lambda takes an injected `McpServer` (no schema change) for
  `ClientInfo.Name` / `.Version`, and a NEW optional `callerModel` argument in the schema.
- `RoundContext` / `Schema.cs` / `RoundsDb.cs` — the caller's vendor, client, client version and
  declared model travel beside the `caller` already stored. One migration, columns nullable.
- `RoundsQuery.Rounds` + `LoggedRound` — select them, so the log page can render them.
- `roundsLogPanel.ts` / `roundsLog.ts` — a column or a line on the expanded row: *"asked by
  claude-code 2.x · claude-opus-5"*, and *"asked by codex · model not stated"* where it was not.
- `research/module_server.md`, `research/module_extension.md`, `CHANGELOG.md`.
- **The conventions repository**: the gate snippet gains one sentence telling the calling AI to send
  its own model id on `open`. Then the pin cascade.

## Test plan (RED first)

| # | Test | RED symptom expected |
|---|---|---|
| 1 | `src_mcp/tests`: `CallerIdentity.From` names the VENDOR for each of the four variables, and `COAI_CALLER_SESSION` yields an explicit "stated" vendor rather than a guessed one | it returns a bare string; there is no vendor to assert |
| 2 | `src_mcp/tests`: a round records the declared model, and a round opened WITHOUT one records an empty declaration that renders as *not stated* — never as a vendor default | there is no field |
| 3 | `McpContractTests`: a real stdio `initialize` carrying `clientInfo {"name":"contract-test"}` reaches the round record — the handshake path asserted end to end, as that suite already does for the tools | the client is not recorded |
| 4 | `src_mcp/tests`: `RoundsQuery` selects the caller columns and `--log` carries them | `LoggedRound` has no such fields |
| 5 | `roundsLog.test.ts`: the page renders the caller and the model, and renders *not stated* for a round that declared none; a round from before the columns reads exactly as it does today | nothing is rendered |
| 6 | `gate-snippet-check.mjs` passes against the amended shared snippet | the local copy and the conventions copy differ |

Run: `dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4` then the MTP executable — never
`dotnet test`. Extension side: `cd src_vs_code && npm test`.

## Definition of Done

- [ ] Every test above written RED first, with its failure message recorded.
- [ ] Both suites green, counts reported in the pull request.
- [ ] The diff through the `coai` plan and code rounds, every finding resolved.
- [ ] `module_server.md`, `module_extension.md`, `CHANGELOG.md` updated.
- [ ] The conventions snippet amended, its own pull request merged, and the pin cascade run across
      the consumer repositories.
- [ ] This plan promoted to `research/` with `IMPLEMENTED` and the date.
