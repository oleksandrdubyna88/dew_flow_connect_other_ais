# PLAN — a document reaches a Team server (5 of 5)

> Status: **plan only, nothing implemented yet.** Scope: `src_server` (a bound on the prompt it
> accepts), `src_mcp` (which vendor switch serves a document round, and a sentence saying where the
> document went), `src_vs_code` (that switch on the vendor card), and the tests and docs for all of
> it.
>
> Related docs: [module_server.md](../research/module_server.md),
> [module_core.md](../research/module_core.md),
> [module_extension.md](../research/module_extension.md),
> [architecture.md](../research/architecture.md);
> plan 1: [PLAN_review_roles_become_data.md](../research/PLAN_review_roles_become_data.md),
> plan 2: [PLAN_review_roles_crud_tab.md](../research/PLAN_review_roles_crud_tab.md),
> plan 3: [PLAN_team_server_accepts_custom_roles.md](../research/PLAN_team_server_accepts_custom_roles.md),
> plan 4: [PLAN_review_document.md](../research/PLAN_review_document.md),
> all four shipped.

## The symptom

Plan 4 shipped `review_document` and wrote this into `architecture.md`:

> **The Team server is unchanged.** A document round on a remote vendor is out of scope until plan 5;
> today the document reaches only local reviewers.
>
> **No `src_server` change, and no new arrow.** A document never leaves this machine.

The first sentence is true today and stops being true **without anybody writing a line of code** —
which is the finding this plan starts from.

`AcceptedRoles.From` seeds itself from the shipped catalog:

```csharp
// src_server/src/Jobs/AcceptedRoles.cs:103
foreach (var role in RoleCatalog.Builtin.Roles)
```

`RoleCatalog.Builtin.Roles` has carried `DocumentReview` and `DocumentSummary` since plan 4, and
`src_server` compiles against `CoaiMcp.Core`. `/api/catalog` serves that same instance's names
(`src_server/src/Vendors/CatalogEndpoints.cs:50`), and this side carries any role a server names:

```csharp
// src_mcp/runners/Reviewers/RemoteRoles.cs:130
private string? Said(string role, string shown) =>
    AllowAny || Names.Contains(role, StringComparer.OrdinalIgnoreCase) ? null : …
```

So **the day the operator redeploys the box, a document round starts going to it** — not because
anything was built for that, but because the document travels inside `prompt` exactly as a plan does
and the server never wanted a checkout in the first place. The deploy is manual
(`workflow_dispatch`), so the change lands on a day nobody associates with a code change.

That is not a defect to fix; it is the right behaviour arriving by the wrong route. What is missing
is the three things that ought to have arrived with it.

**1. The person never decided that the document may leave the machine.** Plan 4's confinement — the
document is read from inside the repository the session was opened for — was a security boundary. A
Team vendor carries the text off this machine to a shared box, where it runs under a company account
rather than the person's own. Today that decision is taken by the vendor's **plan** tick:

```csharp
// src_mcp/src/Server/PanelService.cs:818
NeedsWorktree: false, ServedByPlanSwitch: true, ReadsCheckout: false,
```

```csharp
// src_mcp/src/Server/PanelSettings.cs:50
public bool Serves(bool isPlan) => Enabled && (isPlan ? Plan : Code);
```

Plan 4 chose that deliberately and said so in a comment — *"a person who set a vendor to read
documents rather than diffs ticked that one"* — and for a local reviewer it is a fair reading. It
stops being fair the moment the same tick also decides whether a company document leaves the laptop.
A tick that means "this vendor is good at prose" cannot also mean "this file may go to the shared
server", and nothing on the vendor card says it does.

**2. The server bounds nothing.** `AcceptedRoles`' own remarks say it: *"a client is not a boundary
— anything reaching the endpoints is checked here."* The 256 KB document bound
(`src_mcp/core/Rounds/DocumentRules.cs:31`) is **client-side only**. The server checks the prompt for
emptiness and nothing else:

```csharp
// src_server/src/Jobs/ReviewEndpoints.cs:203
return "a review needs a prompt";
```

Above it sits nginx with `client_max_body_size 4m` (`deploy/nginx/coai:51`) and Kestrel's
un-overridden 30 MB default. So a caller between those two numbers is answered by nginx's 413 HTML
page, which the shim reads as an unparseable vendor failure — a wall with no sentence, on the one
endpoint whose whole design is that a refusal explains itself.

**3. Nothing says the document went.** A round reports who reviewed it; nothing reports that the text
crossed the network. For a diff that is unremarkable — the repository is already the company's. For
a document a manager handed somebody, it is the fact they would most want to have been told.

## What a person does with it

They configure a Team vendor, tick **documents** on its card, call `review_document`, and the round
runs the document roles on the company's shared subscription as well as locally. The reply names the
server the document was sent to. If they leave the box unticked, the document stays on the machine
and the round says which vendors were left out and why — the machinery plan 3 built for exactly this.

## The decisions

### 1. No new wire field, no upload, no artifact on the server

The document travels inside `prompt`, as it already does. No `document` field on
`ReviewRequestDto`, no artifact store on the box, no second endpoint.

Two reasons, and the second is the load-bearing one. The server has never held a checkout — it runs
the vendor CLI on `job.Prompt` in a per-job temp directory it deletes in a `finally`
(`src_server/src/Jobs/ReviewLauncher.cs`) — so a document is already the *easiest* thing it can be
asked to review, not the hardest. And a new wire field is measured against the OLD half by rule: the
deploy is manual, so a client shipping a field the box does not read would be a silent downgrade for
however many weeks pass before somebody presses the button. Nothing here needs one.

### 2. A vendor has a THIRD switch, and an absent one keeps today's behaviour

`ProviderSettings` gains `Document`, and `Serves` stops taking a bool:

```csharp
public bool Serves(Stage stage) => Enabled && stage switch
{
    Stage.PlanReview => Plan,
    Stage.CodeReview => Code,
    Stage.DocumentReview => Document ?? Plan,
    _ => throw …,   // the exhaustive shape PanelConfig.BucketFor already has
};
```

**Nullable, and that is the whole design** — the same argument `JobKind` settled on the server: it is
the only way to tell *a settings file written before documents existed* from *a person who said no*.
Absent means today's behaviour, which is the plan tick; the moment somebody touches the box the two
become independent. A non-nullable `Document = true` would silently start sending documents to every
vendor a person had ticked for code only, which is the exact consent this plan exists to ask for.

`BuildWork` and `StageRun` lose `servedByPlanSwitch` and take the `Stage` instead. Plan 4 split one
flag into two because one flag was answering two questions; a bool cannot answer three, and the
caller already knows which stage it is running. The four call sites are
`PanelService.cs:272` (`ExcludedFrom`), `1647` (the eligible vendors), `2045`
(`ConfiguredReviewers`) and `1165`.

### 3. The server bounds the prompt itself, with a sentence

`ReviewEndpoints.Refusal` gains a bound, checked beside the empty check. **3 MiB**, and the number is
derived rather than chosen: it must be comfortably above a real code round's prompt (the nginx
comment records that 1 MB truncated real reviews) and comfortably **below** nginx's 4 MB, so that
everything between the two meets the server's own sentence rather than an HTML error page. A document
prompt cannot approach it — 256 KB of document plus a role prompt — so this is a bound on the
boundary, not on the feature.

The sentence names the size sent and the limit, as every other refusal on this endpoint does, and it
is a 400.

### 4. The reply says where the document went

One clause, appended to a document round's summary when — and only when — a remote vendor carried
work in it: *"the document was also sent to <server>, where it runs on the team's shared
subscription."*

Only for a **document** round, and only when at least one carrier was remote. A code round's diff
going to a Team server is what a Team server is; saying it on every round is the change nobody asked
for that `ReviewerSummary.Sentence`'s own remarks warn against. The clause is built from the work
that was actually assembled, never from the settings — a vendor that was configured and then excluded
(no credential, role refused) did not receive anything, and a sentence claiming otherwise would be
worse than silence.

### 5. The old box keeps working, and says why in its own words

Until the operator redeploys, the box answers `/api/catalog` with the five it was built with, so
`Said()` refuses a document role with *"'Document review' is not one of the roles this Team server
runs — it runs PlanCritique, Conventions, …"*. That sentence is already correct, already per (vendor,
role), and already reaches the round summary. **No change**, and the plan says so explicitly so that
nobody later reads the absence as an oversight.

The operator's verification is one call, and it belongs in `deploy/README.md` rather than in code:
`GET /api/catalog` must name `DocumentReview` before a document round can reach that box.

### 6. What this plan does NOT do about the confinement

The document still may not be an arbitrary path: `DocumentReader.Canonical` walks root-down and
refuses anything outside the repository, and that is untouched. What changes is only where a document
that was already legal may be *sent*, and only when a person ticks a box.

## Build order — ONE unit

One branch, one pull request, one gate plan round and one code round, on the operator's standing
command that a plan already part of a split is not split again.

1. **`src_server`** — the prompt bound and its refusal, with the tests. It is the only half that can
   ship independently of the others and the only half whose deploy is manual, so it goes first.
2. **`src_mcp`** — `ProviderSettings.Document`, `Serves(Stage)`, `BuildWork`/`StageRun` taking the
   stage, and the four call sites.
3. **`src_mcp`** — the "where it went" clause on a document round's summary.
4. **`src_vs_code`** — the third `stageBox` on the vendor card, its help text, and the settings
   round-trip (`vendors.ts:28`, `panelView.ts:840`, `889`).
5. **Docs** — `module_server.md`, `module_core.md`, `module_extension.md`, `architecture.md` (the
   "Team server is unchanged" paragraph is what this plan rewrites), and `deploy/README.md`.

## Test plan

RED first, every one of them.

**The server accepts a document role — deliberately, not by accident.**
- `AcceptedRolesTests`: `Knows("DocumentReview")` and `Knows("DocumentSummary")` on a default
  instance. Today's tests derive from `RoleCatalog.Builtin.Roles` and would pass with the roles
  removed; these name them, so a seed change is a red test rather than a quiet exclusion.
- `CatalogEndpointTests`: `/api/catalog` names both.
- `ReviewEndpointTests`, through the real HTTP surface with the `TeamServer` harness: a job with
  `role: "DocumentReview"` is accepted, queued, claimed and answered. This is the end-to-end proof
  that the two halves were only ever tested against themselves.

**The prompt bound.**
- A prompt one byte over 3 MiB is refused 400, and the sentence names both numbers.
- A prompt at exactly the bound is accepted.
- A document-sized prompt (256 KB + a role prompt) is nowhere near it — an explicit test, because the
  bound must never be the thing that refuses the feature it was added for.

**The document switch.**
- A vendor with `plan: true` and no `document` set serves a document round — today's behaviour,
  preserved by the nullable.
- A vendor with `document: false, plan: true` does NOT, and is named in `ExcludedFrom`.
- A vendor with `document: true, plan: false` DOES, and is not.
- `Serves` is exhaustive over `Stage`: a new stage throws rather than falling into the code arm.
- Regression: a plan round and a code round route by their own switches, unchanged.
- **Structural**: the document round's `StageRun` is pinned to carry the *stage*, and a guard asserts
  the source no longer contains `servedByPlanSwitch: true` beside a document round — the whole
  condition, not a fragment, per the lesson five source-pinning tests taught in one session.

**The sentence.**
- A document round with a remote carrier names the server; with none, the summary is byte-identical
  to today's.
- A code round with a remote carrier says nothing new.
- A vendor that was configured but excluded is not named as having received the document. Prove it by
  reverting to a settings-derived clause and watching the test name the wrong server.

**The extension.**
- The third box renders, round-trips through settings, and is absent-safe: a settings file with no
  `document` key reads back as undefined rather than false.
- The bucket counting and the roles page are untouched — a regression assertion, not a new one.

## Definition of Done

- [ ] `src_server` refuses an oversized prompt with a sentence naming both numbers, and a
      document-sized prompt is proven to be far below the bound.
- [ ] An end-to-end test through the real HTTP surface runs a `DocumentReview` job on the server.
- [ ] `ProviderSettings.Document` exists, is nullable, and an absent value reproduces today's routing
      exactly.
- [ ] `Serves` takes the `Stage`; `servedByPlanSwitch` is gone from `BuildWork` and `StageRun`.
- [ ] A document round whose work reached a Team server says so; one that did not is unchanged.
- [ ] The vendor card has a third switch with help text that says what ticking it means for a file.
- [ ] Every RED test above was watched failing with the real symptom before its fix, and the two
      revert-proofs were run.
- [ ] `architecture.md`'s "the Team server is unchanged" paragraph is rewritten, and the confinement
      paragraph says precisely what the boundary is now.
- [ ] `deploy/README.md` records the one call that verifies a box will run document roles.
- [ ] Full suites green: CoaiMcp, CoaiServer, the extension, the conventions submodule.
- [ ] `plan-lifecycle`, `pin-check`, `adapter-check`, `gate-snippet-check`, `build-flags-check`.
- [ ] The gate: `open` → `review_plan` → `resolve` → implement → `review_code` → `resolve`.

## Out of scope

- **`plan:document` — a plan review for non-programming work.** Stored, composed, counted, run by
  nothing, and still named as waiting. Untouched by this plan: it is a fourth bucket with no round,
  not a routing question.
- **The conventions rule.** `coai-document-gate.md` teaches the AI how to CALL the gate; where a
  document travels is a decision the person makes on the vendor card, in a panel the AI does not
  read. Adding a paragraph would raise `DOCUMENT_VERSION`, the snippet SHA and a six-repository pin
  cascade for text aimed at the wrong reader.
- **Per-model context validation.** Refused in plan 4 for a reason that has not changed: this product
  holds no reliable context size for every vendor and model a person may configure. The 3 MiB bound
  here is a transport boundary, not a model one, and must not be read as the other.
- **Redeploying the Team server.** Manual, `workflow_dispatch`, and the operator's. This plan makes
  the verification one call; it does not press the button.
- **No release.** The extension and `coai-mcp` are versioned and tagged as a separate act, as always.
