# PLAN — a Team server runs the roles a person wrote (3 of 5)

> Status: **plan only, nothing implemented yet, 2026-09-13.** Scope: `src_server` (the two role gates,
> `Coai:ExtraRoles`, `Coai:AllowAnyRole`, `CatalogDto`), `src_mcp` (`CanCarry`, `RemoteProbe`),
> `src_vs_code` (`teamServerApi.ts` and the sentence the panel shows), and the tests for all of it.
>
> Related docs: [module_server.md](../research/module_server.md),
> [module_extension.md](../research/module_extension.md),
> [architecture.md](../research/architecture.md);
> plan 1: [PLAN_review_roles_become_data.md](../research/PLAN_review_roles_become_data.md),
> plan 2: [PLAN_review_roles_crud_tab.md](../research/PLAN_review_roles_crud_tab.md), both shipped.
>
> **Revised after the plan round.** Seventeen findings, and they converged on one thing: the first
> draft specified `Coai:ExtraRoles` carefully and `Coai:AllowAnyRole` loosely. It had no wire
> representation, no length bound, no shape check at the boundary, and it *documented* a usage-ledger
> split as an acceptable price. Three reviewers independently refused that last part, and one of them
> named the answer the draft had dismissed: **case-fold the id**. The open question the draft put to
> the operator is therefore withdrawn — it is answered below, and `AllowAnyRole` stays.

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

Add a configured list and a third copy writes itself. **One `AcceptedRoles` service**, injected —
every gate, every message and the catalog endpoint derive from it.

This is not a tidiness argument. Plan 2 shipped with THREE independent counts of "which code roles
will run" and the one nobody updated promised four reviewers under a section drawing five boxes. The
same shape, caught before it is written.

### 3. An id is checked for SHAPE and for LENGTH, at the boundary — and `ExtraRoles` at BOOT

Today an unknown role is refused, so nothing ill-formed reaches storage. The moment a configured or
arbitrary id is accepted, one must be:

- **Shape**: `^[A-Za-z][A-Za-z0-9_]*$`, the rule `RoleComposition.RoleId` already enforces on the
  client because a role id becomes `COAI_ROUNDS_<ID>`.
- **Length**: at most **48 characters**, the same bound `roles.ts` `MAX_ROLE_ID_LENGTH` applies for
  the same reason. The regex alone accepts an arbitrarily long identifier, and under `AllowAnyRole` a
  4 KB alphanumeric name would reach `JobRecord`, the idempotency fingerprint and an append-only
  ledger. *(codex: "the proposed regex accepts an arbitrarily long identifier".)*

**`Coai:ExtraRoles` is validated at BOOT, not at request time.** An operator who writes `My-Role` or
`123Role` learns at startup, in a message naming the entry and the rule — the way
`Coai:AllowedDomains` and `Coai:SessionTtlDays` already fail. A configured id that would be refused on
every request is a server that is misconfigured, and a misconfigured server should not start quietly.
*(local, Blocking.)*

**This is enforced at the SERVER boundary, not relied upon from the client.** The client applies the
same rule so it never offers an id the server will refuse, but the server is the boundary that holds.

### 4. `AllowAnyRole` CASE-FOLDS, and the ledger split is solved rather than accepted

```csharp
internal static string CanonicalRole(string? said) =>
    RoleCatalog.Builtin.ById(said ?? string.Empty)?.Id ?? said ?? string.Empty;
```

A built-in is canonicalised; **anything else passes through verbatim**. Its own doc says why that
matters: *"a spelling passed straight through would make one role two rows in the usage view the day
two clients disagree about it"* — exactly the state a custom role is in the moment this plan accepts
one.

- With **`ExtraRoles`**, the server has a configured spelling, so it canonicalises to that. Matching
  is case-insensitive; the recorded id is the one the OPERATOR configured.
- With **`AllowAnyRole`** there is no configured spelling. The first draft concluded there was no
  honest answer and wrote the split down as a price. **That was wrong, and three reviewers said so.**
  There is a deterministic answer that needs no configuration and no race: **fold the id to a
  canonical casing** and record that. `Requirements` and `requirements` become one row, on every
  server, without an operator having listed anything.

The author's own casing is what the panel shows, from the client's own catalog; the server's recorded
id is a KEY, and a key's job is to be the same key. **The open question the first draft raised is
withdrawn.**

### 5. Canonicalisation happens ONCE, at a shared ingress, before the fingerprint

*(codex, Major.)* Normalising inside `ReviewEndpoints.Accepted` is too late and too narrow: the
idempotency fingerprint is computed from the role, and `JobKinds.Refusal` reads the role on another
path. Two requests differing only in casing would be two jobs whatever a later ledger view merges.

So the role is canonicalised at the **request boundary**, once, before `Idempotency.Fingerprint`,
before `JobRecord`, and before either gate reads it. One normalisation point, and a test that sends
the same review twice in two casings and asserts **one** job.

### 6. The wire says both things explicitly — no sentinel

*(gemini Blocking, codex Major: the "any" marker had no defined representation.)* `CatalogDto` gains
**two** fields rather than one overloaded one:

```jsonc
{ "serverVersion": "...", "isAdmin": false, "vendors": [ ... ],
  "roles": ["PlanCritique", "Conventions", "Architecture", "SecurityReliability",
            "UxDxPerformance", "Requirements"],
  "allowAnyRole": false }
```

A sentinel inside `roles` (`["*"]`) was considered and refused: `roles.includes(role)` is the obvious
client code and it silently answers `false` for every real role. A separate boolean cannot be read
wrongly by accident.

| What the client sees | What it concludes |
|---|---|
| no `roles` field at all | **the five this product ships** — today's behaviour exactly |
| `roles` present, `allowAnyRole` false | those ids, matched case-insensitively, and only those |
| `allowAnyRole` true | any id that passes the shape and length rule |
| `roles` present but EMPTY | **the five** — a server that has the field always accepts at least five, so an empty list is a bug, not an instruction *(local, Major)* |

Absent must never mean "none" (every round silently loses its reviewers) and never mean "any" (every
custom role is sent to a server certain to 400 it). It means what is true today. This is the rule
`remoteVendor` was lost for want of, twice.

**A failed catalog fetch is not an old server.** *(codex, Major.)* A timeout, a 401 or a connection
error leaves the client with no answer at all — which is a third state, not the fallback. Unknown
excludes a custom role **with a visible reason naming the fetch failure**, never silently, and never
by pretending the server said "the five".

### 7. Mixed versions: the fallback is a MINIMUM, and it is named

*(codex, Major.)* "Absent means the five this product ships" assumes the old server's five are this
client's five. A client that later adds or renames a built-in would carry it to a server whose
catalog omits `roles` and get a 400 despite the promised silent correctness.

The fallback set is therefore **the built-ins as of the contract version the server reports**, not
"whatever this client ships today" — `ContractVersion` already exists for exactly this, and
`Startup.Version` is already on `CatalogDto`. Adding a sixth built-in is a contract change, and this
plan writes that down so the next person adding one finds the rule rather than the bug.

### 8. The panel says it BEFORE the round, not during it

A role excluded for a Team server is named in the round's result. By then somebody has waited. The
panel knows the catalog, so the exclusion belongs where the role is configured — the same argument
plan 2 settled for the version-skew banner. **With a test that loads a catalog accepting role A but
not role B and asserts the page says so before anything is submitted** *(codex, Major)*, including
the loading and unavailable states.

### 9. A refusal names the rule it broke, not a list

*(gemini Minor, local Major.)* Under `AllowAnyRole`, an id failing the shape check would have been
refused with *"Allowed: PlanCritique, Conventions, …"* — telling an operator the server only accepts
five when it accepts anything. Shape failures and membership failures are different refusals:

- shape/length → *"'123-role' is not a role id: a role id is latin, starts with a letter, carries no
  hyphen, and is at most 48 characters — it becomes `COAI_ROUNDS_<ID>`."*
- membership → *"'X' is not a review role here. Accepted: …"*

## Build order — four stories

| # | Story | Files |
|---|---|---|
| **1** | **`AcceptedRoles`, the whole rule in one testable place.** The shipped five, plus `Coai:ExtraRoles`, or anything well-formed under `Coai:AllowAnyRole`. Shape + 48-char length; case-insensitive membership; canonicalisation (configured spelling, or case-folded under any-role); `Names` for the messages; the two distinct refusals. Boot-time validation of `ExtraRoles` wired into `Startup`, which already refuses to start on a bad `AllowedDomains`. | `src_server/src/Jobs/AcceptedRoles.cs` (new), `Startup.cs`, `Program.cs` |
| **2** | **Both gates and the ingress read it.** `ReviewEndpoints.Refusal` and `JobKinds.Refusal` lose their own lists; the role is canonicalised ONCE at the request boundary, before `Idempotency.Fingerprint` and `JobRecord`. A structural test asserts `RoleCatalog.Builtin.Roles` is enumerated nowhere else in `src_server`. | `ReviewEndpoints.cs`, `JobKind.cs` |
| **3** | **The wire.** `CatalogDto` gains `roles` and `allowAnyRole`, populated FROM `AcceptedRoles` — with endpoint tests for default, configured-list and allow-any, because a DTO property that nothing fills is the failure mode this story exists to prevent *(codex, Major)*. | `ServerJsonContext.cs`, `CatalogEndpoints.cs` |
| **4** | **Both clients.** `CanCarry` asks instead of guessing, case-insensitively; `RemoteProbe` carries the two fields and distinguishes old / unknown / answered. `teamServerApi.ts` the same, and the panel says which roles this server will run, before a round. Help article, four translations, CHANGELOG. | `PanelService.cs`, `RemoteProbe.cs`, `teamServerApi.ts`, `panelView.ts`, `helpContent.ts` + 4 |

Each story is reviewed by `review_code` on its own diff, resolved, documented, tested and committed
before the next begins. Story 3 must land before story 4 can be verified against a real server, and
the Team server deploy is MANUAL (`workflow_dispatch`) — so the new fields are measured against the
OLD deployed server before either client is released.

## Test plan

- **`AcceptedRoles` alone**: default is the five; `ExtraRoles` adds; membership is case-insensitive;
  `AllowAnyRole` accepts a well-formed id, refuses one that is not, and refuses one over 48
  characters — boundary and over-limit both.
- **Boot**: a malformed `Coai:ExtraRoles` entry fails startup naming the entry and the rule.
- **One normalisation**: the same review sent twice in two casings produces ONE job — asserted on the
  fingerprint, not only on a ledger view.
- **Ledger**: `requirements` and `Requirements` are one row under `ExtraRoles` AND under
  `AllowAnyRole`.
- **Both gates** refuse with the SAME accepted set, asserted from one place; a shape failure and a
  membership failure produce different messages.
- **The wire**: default, configured and allow-any payloads; and a `CatalogDto` payload with no
  `roles` field leaves a client running exactly the five.
- **The client**: a custom role carried by a server that accepts it; excluded with the server's own
  words by one that does not; excluded by a server too OLD to say; and excluded with a *fetch failed*
  reason when the catalog could not be read at all.
- **The panel**: role A runnable and role B excluded, shown before submission, plus loading and
  unavailable states.
- Whole-suite: `CoaiServer.Tests`, `CoaiMcp.Tests`, the extension suite, and the three family scans.

## Definition of Done

- [ ] A role a person wrote runs on a Team server that lists it in `Coai:ExtraRoles`.
- [ ] A server with neither key set behaves exactly as it does today — same five, same messages.
- [ ] `Coai:AllowAnyRole` accepts any well-formed role id, refuses anything that is not one, and
      records it case-folded so one role is one ledger row.
- [ ] A malformed `Coai:ExtraRoles` entry stops the server at boot, naming it.
- [ ] One list feeds both gates, every message and the catalog; a test fails if a third copy appears.
- [ ] The role is canonicalised once, before the idempotency fingerprint.
- [ ] `CatalogDto` carries `roles` and `allowAnyRole`, populated from `AcceptedRoles`, with endpoint
      tests for all three shapes.
- [ ] A client talking to a server too old to send `roles` runs the five, silently and correctly; a
      client that could not READ the catalog says so instead of guessing.
- [ ] The panel says which of a person's roles a configured Team server will run, before a round.
- [ ] Help article in English plus the four translations, in the same commit.
- [ ] Every suite green, `plan-lifecycle` / `pin-check` / `adapter-check` clean.
- [ ] **Verified against the deployed server**, not only in tests — the deploy is manual, so this is
      a step somebody performs.

## The open tail

- **A ledger written before this plan** may already hold rows for a custom role in more than one
  casing, from any client that reached a server with `AllowAnyRole`. Nothing does today, because
  nothing accepts a custom role — so there is no data to migrate on the day this ships, and a
  migration written now would have nothing to run against. *(local suggested one; it is recorded here
  rather than built, because the ledger cannot contain such a row until this plan ships.)*
- **Per-caller role permissions** are still nobody's question. If one is ever asked, it is a second
  authorisation model and it belongs beside the domain one, not inside `AcceptedRoles`.
- **`RoleComposition.RoleId` on the client has the trailing-newline hole.** Story 1's own test caught
  it on the server: in .NET, `$` matches at the end of the string OR immediately before a trailing
  newline, so `^[A-Za-z][A-Za-z0-9_]*$` ACCEPTS `"Requirements\n"`. The server anchors `\A…\z` now.
  `RoleComposition` (`src_mcp/core/Rounds/RoleComposition.cs`) still uses `^…$`, and so does
  `roles.ts`'s `ROLE_ID` — though JavaScript's `$` does not have this behaviour without the `m` flag,
  so only the C# copy is affected. Not fixed here because it is not this story's file and a role id
  reaching it has already passed the page's own generator; it is a one-character change whenever
  `RoleComposition` is next opened.
