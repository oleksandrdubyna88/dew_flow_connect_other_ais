# PLAN — the rounds log names the model, and the one that actually ran

> Status: **plan only, nothing implemented yet.** Scope: `ReviewerState` and the panel's rounds
> log, `ReviewStatusDto` on the Team server, and the client that reads it.
>
> Related docs: [module_server.md](../research/module_server.md),
> [module_extension.md](../research/module_extension.md),
> [module_team_server.md](../research/module_team_server.md).

## The symptom

A round in the log says `remsoftdev-claude / Architecture — 84.8 s`. It does not say which model
answered, and the question people actually ask about a slow or a weak round is *which model was
that*. `ReviewerState` (`src_mcp/src/Server/SessionStore.cs:17-23`) has no field for one:

```csharp
public sealed record ReviewerState(
    string Provider, string Role, string Status,
    int Findings = 0, string Note = "", double Seconds = 0)
```

The spending ledger already records a model per run (`UsageEntry.Model`), so the data exists in the
product — it is simply not on the object the log renders.

## The trap, which is why this is not a one-field change

**The model the panel is configured with is not always the model that ran.** Two paths change it
after the settings are read:

1. **A Team server picks its own.** `ReviewRequestDto` carries a `Model` the client asked for, and a
   server with its own default answers with whatever its account is set to. `ReviewStatusDto`
   (`src_server/src/ServerJsonContext.cs:76-85`) reports `Status`, `Answer`, `Seconds`, `TokensIn`,
   `TokensOut`, `Failure`, `Reason` — **and no model**. The client cannot know.
2. **Escalation.** A retry may run a stronger model than the one the round opened with.

Today `PanelService` records `ModelOf(progress.Provider)` — the CONFIGURED model — into the ledger
(`src_mcp/src/Server/PanelService.cs:551-556`). So the ledger is already answering the question with
the name of a model that may not have run, and copying that into the log would spread the same
inaccuracy to a second surface rather than fix it.

**A log that names the wrong model is worse than one that names none**, because the first is
believed.

## The shape

Three steps, in this order, so that each is useful alone.

### 1. The client records what it asked for, and says that is what it means

- `ReviewerState` gains `Model` (defaulted and trailing, so persisted sessions on disk stay valid).
- `LiveRound` fills it from `ReviewerInvocation.Model`, which it already holds.
- The panel's rounds log renders it beside the role.

### 2. The server reports what it ran

- `ReviewStatusDto` gains `Model` — defaulted, so an older client parsing a newer server is
  unaffected and a newer client reading an older server gets an empty string.
- The job record keeps the model the server RESOLVED for the account it claimed, not the one the
  request asked for.
- `RemoteRuntime` surfaces it, and the round overwrites its own guess with it when it is non-empty.

### 3. The ledger stops guessing too

Once the answer is available, `UsageLedger.Record` takes the resolved model rather than
`ModelOf(provider)`. This is what makes the spending page's per-model breakdown true for remote runs
as well as local ones.

## Version skew, stated rather than discovered

A panel on the new build talking to an older Team server gets no `Model` back. That is the empty
string, and the log shows the CONFIGURED model with nothing claiming it is confirmed. There is no
failure path: an unknown field is ignored by both JSON readers, which the Conventions role's own
skew scare (2026-09-08) is the reason to state explicitly rather than assume.

## Build order

1. RED: a reviewer state carries the model it was launched with; the log renders it.
2. RED: a persisted session written before this field still loads (the defaulted-parameter promise).
3. Implement 1, ship it — the local case is complete and correct at this point.
4. RED, on the server: a finished job reports the model that ran, and it is the account's resolved
   model rather than the request's.
5. RED, on the client: a non-empty model from the server WINS over the configured one; an empty one
   leaves the configured one in place.
6. Implement 2 and 3, then move the ledger onto the same value.

## Test plan

| Test | Where | Asserts |
|---|---|---|
| The state carries the launched model | `src_mcp/tests` | step 1 |
| An old session file without the field loads | `src_mcp/tests` | nothing on disk is invalidated |
| The rounds log shows the model | `src_vs_code/src/test` | the surface the task is about |
| A finished job reports its resolved model | `src_server/tests` | step 2 |
| A server's model overrides the configured one; empty does not | `src_mcp/tests` | the trap above |

## Definition of Done

- [ ] The rounds log names a model for every reviewer.
- [ ] A Team-server run shows the model the SERVER ran, not the one the client asked for.
- [ ] An older server, which reports none, leaves the configured name in place and fails nothing.
- [ ] The usage ledger and the log agree, because they read the same value.
- [ ] Sessions written before the field still load.
- [ ] Module docs describe where the model comes from and why the configured one is not trusted.
