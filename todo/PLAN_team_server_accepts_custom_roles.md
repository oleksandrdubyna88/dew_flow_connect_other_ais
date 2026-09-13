# PLAN — a Team server runs the roles a person wrote (3 of 5)

> Status: **plan only, nothing implemented yet, 2026-09-13.** Scope: `src_server` (the two role gates,
> `Coai:ExtraRoles`, `Coai:AllowAnyRole`, `CatalogDto.Roles`), `src_mcp` (`CanCarry`, `RemoteProbe`),
> `src_vs_code` (`teamServerApi.ts` and the sentence the panel shows), and the tests for all of it.
>
> Related docs: [module_server.md](../research/module_server.md),
> [module_extension.md](../research/module_extension.md),
> [architecture.md](../research/architecture.md);
> plan 1: [PLAN_review_roles_become_data.md](../research/PLAN_review_roles_become_data.md),
> plan 2: [PLAN_review_roles_crud_tab.md](../research/PLAN_review_roles_crud_tab.md), both shipped.

## The symptom

Plans 1 and 2 made a review role data and gave a person a page to write one. **A Team server still
runs only the five this product was compiled with.**

[`ReviewEndpoints.cs:226`](../src_server/src/Jobs/ReviewEndpoints.cs#L226) refuses anything else with
a 400 naming the five, so `coai-mcp` does not even ask: `CanCarry`
([`PanelService.cs:1465`](../src_mcp/src/Server/PanelService.cs#L1465)) is

```csharp
!Remote(provider) || _settings.Rounds.Catalog.ById(role)?.BuiltIn == true;
```

— a remote vendor carries a built-in role and nothing else. The round then says so, once per
(vendor, role): *"'Requirements' is a role this Team server does not know — it accepts the five this
product ships"*.

That sentence is accurate and it is the wrong answer. A team that shares a Team server is exactly the
team that wants one agreed set of review roles, and today the person who writes one gets it from the
vendors they run themselves and from nobody else's.

| Who | What they get today |
|---|---|
| A person with local CLIs only | Their roles run. Plan 2 finished the job. |
| A person on a Team server | Their roles are excluded, per vendor, with a sentence |
| A team wanting ONE agreed role set | No mechanism at all — the server's list is compiled in |

## What this plan is NOT

- **No `review_document`, no artifact, no document upload.** A role with `programmingTask: false` is
  still stored, still shown, and still runs in no round. That is plan 4 and plan 5.
- **No change to what a round outputs.** Same findings, same categories, same verdict.
- **No role AUTHORING on the server.** The server does not grow a roles page; it grows permission to
  run roles a client names. Where the role's prompt comes from is unchanged — the client composes the
  prompt and sends it, which is already how every review works.
- **No per-caller role permissions.** Roles are a property of the SERVER's configuration, like
  `AllowedDomains`. Who may run which role is a question nobody has asked yet, and inventing an
  answer now would be a second authorisation model beside the domain one.

## The decisions worth arguing with

### 1. `Coai:ExtraRoles` + `Coai:AllowAnyRole`, and the precedent is exact

The server already has this shape, for the question with the same structure — *who may use this
server*:

```csharp
var allowedDomains = SplitCsv(config["Coai:AllowedDomains"]);       // Program.cs:57
var allowAnyDomain = config.GetValue("Coai:AllowAnyDomain", false); // Program.cs:58
```

and [`Startup.cs:91`](../src_server/src/Startup.cs#L91) refuses to BOOT when the list is empty and the
escape hatch is off, naming both keys. Roles take the same pair and the same spelling:
`Coai:ExtraRoles` is a CSV of ids, `Coai:AllowAnyRole` is the deliberate opt-out.

**The default is what happens today** — neither key set means the five shipped roles, exactly as now.
A server nobody reconfigures does not change behaviour, which is the property every one of these
plans has kept.

**It does NOT refuse to boot when `ExtraRoles` is empty**, and that is the one place the precedent is
deliberately not followed. An empty domain list is a server anyone can use; an empty extra-role list
is a server that runs the five it always ran. The first is a hole, the second is the status quo.

### 2. One list, read by both gates — because there are two, and they disagree already

`RoleCatalog.Builtin.Roles` is enumerated in **two** places that answer the same question:

- [`ReviewEndpoints.cs:229`](../src_server/src/Jobs/ReviewEndpoints.cs#L229) — *"'X' is not a review
  role. Allowed: …"*
- [`JobKind.cs:126`](../src_server/src/Jobs/JobKind.cs#L126) — *"a job sent as kind 'review' needs a
  role. Allowed: …"*

Add a configured list and a third copy writes itself. **One `AcceptedRoles` service**, injected, with
`Knows(id)` and `Names` — every gate and every message derives from it.

This is not a tidiness argument. Plan 2 shipped with THREE independent counts of "which code roles
will run" and the one nobody updated promised four reviewers under a section drawing five boxes. The
same shape, caught before it is written.

### 3. A role id is checked for SHAPE, not only for membership

Today an unknown role is refused, so nothing ill-formed reaches storage. With `AllowAnyRole` an
arbitrary string reaches `JobRecord.Role`, the idempotency fingerprint, and the usage ledger.

So `AllowAnyRole` accepts **any id matching `^[A-Za-z][A-Za-z0-9_]*$`** — the rule
`RoleComposition.RoleId` already enforces on the client, for the reason that a role id becomes
`COAI_ROUNDS_<ID>`. "Any" means any ROLE, not any string: a 4 KB role name or one carrying a newline
is a defect wherever it came from, and the ledger is append-only.

### 4. Canonicalisation must cover the configured roles too

```csharp
internal static string CanonicalRole(string? said) =>
    RoleCatalog.Builtin.ById(said ?? string.Empty)?.Id ?? said ?? string.Empty;
```

A built-in is canonicalised; **anything else passes through verbatim**. Its own doc says why that
matters: *"a spelling passed straight through would make one role two rows in the usage view the day
two clients disagree about it"* — which is exactly the state a custom role is in the moment this plan
accepts one. `Requirements` from one machine and `requirements` from another are two rows for one
role, in an append-only ledger.

`ExtraRoles` gives the server a configured spelling to canonicalise against, so it must. Under
`AllowAnyRole` there is no configured spelling and no honest answer — **first spelling seen wins is a
race**, so that case canonicalises to nothing and the plan says out loud that `AllowAnyRole` can
split a ledger row. That is the price of the escape hatch, and it is why `ExtraRoles` is the
documented way.

### 5. The client must ASK, not guess — and an old server must stay readable

`CanCarry` hard-codes `BuiltIn == true` for a remote vendor. It has to learn what the server accepts,
and there is one endpoint for that: `/api/catalog`, already fetched by
[`RemoteProbe.cs:123`](../src_mcp/runners/Reviewers/RemoteProbe.cs#L123) and by
[`teamServerApi.ts:355`](../src_vs_code/src/teamServerApi.ts#L355). `CatalogDto` gains `Roles`.

**The absent-field rule, which this family has paid for twice.** `remoteVendor` was dropped by every
component written before it existed, and the signature was a 400 nobody could read. So:

| `Roles` on the wire | What the client concludes |
|---|---|
| absent (a server older than this plan) | **the five this product ships** — today's behaviour exactly |
| a list | those ids, and only those |
| the marker for "any" | any role the client holds |

Absent must never mean "none" (every round would silently lose its reviewers) and never mean "any"
(every custom role would be sent to a server certain to 400 it). It means what is true today.

### 6. The panel says it BEFORE the round, not during it

A role excluded for a Team server is named in the round's result. By then somebody has waited. The
roles page and the reviewers section both know the server now, so the exclusion is visible where the
role is configured — the same argument plan 2 settled for the version-skew banner.

## Build order

| # | Story | Files |
|---|---|---|
| 1 | `AcceptedRoles`: the shipped five, plus `Coai:ExtraRoles`, or any well-formed id under `Coai:AllowAnyRole`. Shape rule, canonicalisation, `Names` for the messages. Pure, and tested alone. | `src_server/src/Jobs/AcceptedRoles.cs` (new) |
| 2 | Both gates read it — `ReviewEndpoints.Refusal` and `JobKinds.Refusal` — and no third copy of the list exists. A structural test asserts `RoleCatalog.Builtin.Roles` is enumerated nowhere else in `src_server`. | `ReviewEndpoints.cs`, `JobKind.cs` |
| 3 | `CanonicalRole` canonicalises against the configured roles; the ledger keeps one row per role. | `ReviewEndpoints.cs`, `UsageReader` tests |
| 4 | `Startup` reports the accepted roles at boot, the way it reports the domain boundary — an operator who set `ExtraRoles` can see the server read it. | `Startup.cs`, `Program.cs` |
| 5 | `CatalogDto.Roles`, and the absent-field rule proved against a payload with no such field. | `ServerJsonContext.cs`, `CatalogEndpoints.cs` |
| 6 | `CanCarry` reads what the server said; `RemoteProbe` carries it. The exclusion sentence changes from "the five this product ships" to what the server actually accepts. | `PanelService.cs`, `RemoteProbe.cs` |
| 7 | `teamServerApi.ts` reads `roles`; the panel says which of a person's roles this server will run. Help article, four translations, CHANGELOG. | `teamServerApi.ts`, `panelView.ts`, `helpContent.ts` + 4 |

Stories 1–4 are the server and land together; 5 is the wire; 6–7 are the two clients and can run
beside each other. **Story 5 must ship before 6 and 7 can be verified against a real server**, and by
[[coai-halves-ship-out-of-step]] the Team server deploy is manual — so the new field must be measured
against the OLD server before either client is released.

## Test plan

- `AcceptedRoles` alone: default is the five; `ExtraRoles` adds; `AllowAnyRole` accepts a well-formed
  id and REFUSES one that is not; a configured id that is not well-formed is refused **at boot**, not
  at request time.
- Both gates refuse an unknown role with a message naming the accepted set — the SAME set, asserted
  from one place.
- Canonicalisation: `requirements` and `Requirements` reach the ledger as one row when `ExtraRoles`
  names it; the `AllowAnyRole` split is asserted as the documented price rather than left to be
  discovered.
- The wire: a `CatalogDto` payload with no `Roles` field leaves a client running exactly the five.
- `CanCarry`: a custom role is carried by a server that accepts it, excluded with the server's own
  words by one that does not, and — the case worth naming — excluded by a server too OLD to say.
- Whole-suite: `CoaiServer.Tests`, `CoaiMcp.Tests`, the extension suite, and the three family scans.

## Definition of Done

- [ ] A role a person wrote runs on a Team server that lists it in `Coai:ExtraRoles`.
- [ ] A server with neither key set behaves exactly as it does today — same five, same messages.
- [ ] `Coai:AllowAnyRole` accepts any well-formed role id and refuses anything that is not one.
- [ ] One list feeds both gates and every message; a test fails if a third copy appears.
- [ ] A client talking to a server too old to send `Roles` runs the five, silently and correctly.
- [ ] The panel says which of a person's roles a configured Team server will run, before a round.
- [ ] Help article in English plus the four translations, in the same commit.
- [ ] Every suite green, `plan-lifecycle` / `pin-check` / `adapter-check` clean.
- [ ] **Verified against the deployed server**, not only in tests — the deploy is manual, so this is
      a step somebody performs.

## The open question for the operator

**`AllowAnyRole` can split a usage-ledger row** (decision 4): with no configured spelling, two
clients disagreeing about the case of a role id write two rows for one role, in a ledger that is
append-only. `ExtraRoles` has no such problem.

The alternatives are to drop `AllowAnyRole` entirely, or to canonicalise on first-seen spelling —
which is a race, and a worse kind of wrong because it is invisible. **This plan keeps the key, and
documents the price.** Say if it should be dropped instead; nothing else in the plan depends on it.
