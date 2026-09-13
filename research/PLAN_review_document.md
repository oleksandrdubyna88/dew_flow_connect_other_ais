# PLAN — the gate reviews a document (4 of 5)

> Status: **IMPLEMENTED, 2026-09-13.** Shipped as PR #230, as ONE unit on the operator's standing
> command that a plan already part of a split is not to be split again. `review_document` runs the
> roles a person wrote over a document; a document review is its own session keyed by the document's
> identity; `notes` carries the summary; and the shared rule and the pasted snippet teach the new
> call.
>
> Three gate rounds — the plan round and two code rounds — **77 findings, 49 accepted**. Two of them
> were security holes that a fully green suite could not see, and both were proved by breaking the
> fix and watching the test name the real symptom.
>
> Scope: `src_mcp` (a third stage, the session kind, the artifact, `review_document`, the finding
> wire), `src_vs_code` (the roles page, the panel, the snippet, the help),
> `shared/builtin-roles.json` (two shipped document roles), `.agents/conventions`
> (`common/coai-document-gate.md`, merged as conventions #26), and the tests for all of it. No
> `src_server` change — that is plan 5.
>
> Related docs: [module_core.md](module_core.md),
> [module_server.md](module_server.md),
> [module_extension.md](module_extension.md),
> [architecture.md](architecture.md);
> plan 1: [PLAN_review_roles_become_data.md](PLAN_review_roles_become_data.md),
> plan 2: [PLAN_review_roles_crud_tab.md](PLAN_review_roles_crud_tab.md),
> plan 3: [PLAN_team_server_accepts_custom_roles.md](PLAN_team_server_accepts_custom_roles.md),
> all three shipped.

> **Revised after the plan round.** Eighteen findings, sixteen accepted, and six of them converged on
> one decision: the first draft keyed a document session by the CONTENT HASH of the document. Three
> reviewers found the same fatal consequence independently — editing the document between rounds
> changes the key, so the previous round is orphaned UNRESOLVED and `resolve` → repeat cannot work at
> all. The second draft separates the two ideas the first one had merged: a document's **identity**
> is what a session is keyed by, and its **content hash** is a per-round snapshot. Two reviewers also
> refused the unrestricted `documentPath` on security grounds, which was right and is the second
> largest change here. What was rejected, and why, is at the bottom.

## The symptom

Plans 1, 2 and 3 built a role a person can write, a page to write it on, and a Team server that will
run it. All three of them stop at the same wall, and each one said so in its own out-of-scope
section: **a role marked `programmingTask: false` is stored, shown, composed, given a budget and an
enable switch — and takes part in no round.**

It is not a subtlety. It is one predicate:

```csharp
// src_mcp/core/Rounds/RoleCatalog.cs:134
public IReadOnlyList<string> RolesOf(string stage) =>
    [.. Roles.Where(r => r.Stage == stage && r.ProgrammingTask && r.Active).Select(r => r.Id)];
```

`&& r.ProgrammingTask` is where a document role stops. Everything above it is already built. The
catalog already models the thing this plan needs, one file up:

```csharp
// src_mcp/core/Rounds/RoleCatalog.cs:62
public string Bucket => $"{Stage}:{(ProgrammingTask ? "code" : "document")}";
```

`Bucket` exists today for exactly one purpose — counting the five-active limit
([`RoleComposition.cs:61`](../src_mcp/core/Rounds/RoleComposition.cs#L61)). This plan makes it the
thing a ROUND is selected by, which is what it always described.

**And the person who asked for this is not a programmer.** The originating request was a manager who
needs the gate for *checking documents and producing summaries*. That is the sentence to hold every
decision here against: someone whose work product is a requirements document, a policy, a proposal —
not a diff — wants several vendors' models to read it independently and tell them what is wrong with
it, and wants a readable account of what it says.

## What a person does with it

Today, end to end, for a manager with a specification to check:

1. They write a role on the roles page: **Requirements**, stage *result*, **not** a programming task,
   with a prompt that says what a requirements reviewer looks for. Plan 2 shipped this page.
2. They switch it on. — **And the page refuses**, which is the first defect this plan has to fix; see
   decision 8.
3. The AI in their editor calls `review_document`. — **Which does not exist.**
4. Three vendors read the document and answer findings. — **Which are rejected**, because a
   requirements finding's category is not one of the six code categories; see decision 6.
5. They read a summary of what the document says. — **Which nothing can express**; see decision 7.

Every step after the first is this plan.

## The decisions

### 1. A third stage, not a second meaning for the second one

`Stage` is three values today ([`SessionState.cs:196`](../src_mcp/core/Rounds/SessionState.cs#L196)):
`PlanReview`, `CodeReview`, `Done`. It gains **`DocumentReview`**.

The alternative — `review_document` reusing `Stage.CodeReview` and switching on the role kind — was
considered and refused. A stage is what the session's rounds, budgets, thresholds and verdict trail
are counted against; two different jobs sharing one stage share one round budget and one threshold,
which is the exact mistake `RoleGate` was introduced to undo (the remark at
[`SessionState.cs:48`](../src_mcp/core/Rounds/SessionState.cs#L48) records that history: one number
for both stages, then per stage, then per role, each step the same discovery).

**`Stage` and `RoleDefinition.Stage` are two different things and stay two different things.** Said
outright because the plan round asked for it in those words: a document role's `Stage` remains the
STRING `RoleStages.Result` with `ProgrammingTask: false`, in the seed, in `COAI_ROLES`, in every
session file and everywhere a role is persisted. `Stage.DocumentReview` is an ORCHESTRATION value,
and `PanelConfig.RolesOf(Stage)` ([`SessionState.cs:180`](../src_mcp/core/Rounds/SessionState.cs#L180))
is the single place the two meet:

| `Stage` | bucket |
|---|---|
| `PlanReview` | `plan:code` |
| `CodeReview` | `result:code` |
| `DocumentReview` | `result:document` |

`RoleCatalog.RolesOf` takes a bucket rather than a stage plus a hard-coded `ProgrammingTask`, and the
predicate at the top of this document stops being a filter and becomes a match. Nothing else in the
codebase compares a `RoleDefinition.Stage` against a `Stage`, and a test asserts that the mapping is
the only bridge.

**`plan:document` is a fourth bucket and this plan does not run it.** `RoleComposition` already
counts it, so a person can store a plan-stage non-programming role today; it will keep being stored
and keep running in nothing. Naming it as still-waiting rather than silently folding it into
`DocumentReview` is the same choice plan 1 made about `result:document`, and for the same reason:
"not built yet" and "does not exist" are different states. The roles page says so, in the same words
it says everything else it refuses.

### 2. A session is keyed by the document's IDENTITY, never by its content

*(Rewritten after the plan round. The first draft keyed it by the content hash, and six findings
across all three vendors killed that.)*

A session is per repo+branch and ends at `Done`
([`SessionState.cs:327`](../src_mcp/core/Rounds/SessionState.cs#L327)):

```csharp
public static string For(string repoPath, string branch) =>
    $"{repoPath.Replace('\\', '/').TrimEnd('/').ToLowerInvariant()}#{branch.Trim()}";
```

That rule is correct for code — a branch is reviewed once and merged — and wrong for documents: a
manager with ten documents does not have ten branches. So the key gains a third segment:

```csharp
public static string For(string repoPath, string branch, string document = "")
```

`document` is empty for every code session, which makes **every existing key byte-identical** and
needs no migration, no versioning and no sweep.

**What goes in that segment is the document's IDENTITY**, and the whole point of the revision is that
identity is not content:

- a **`documentPath`** normalises to its repo-relative path, forward slashes, lower-cased. Stable
  across every edit, which is exactly the property the round loop needs.
- **`documentText`** has no identity of its own, so `documentName` is REQUIRED with it and refused
  when absent. This is the one place this plan asks the caller for something, and the plan round is
  why: without it, round 2 of an edited document cannot find round 1, and the tool's own `resolve`
  duty becomes unsatisfiable. `documentName` is held to the same shape rule as a role id
  ([`AcceptedRoles.MaxIdLength`](../src_server/src/Jobs/AcceptedRoles.cs), 48, `\A[A-Za-z][A-Za-z0-9_]*\z`
  — plan 3's rule, not a second one), because it becomes a path segment.

**The content hash survives as the artifact snapshot, per round.** It is what the rounds log names,
what proves a second round read the text a first round did not, and what makes "the document changed
between rounds" observable rather than silent. It is never part of the key.

**Re-reviewing a finished document is explicit, and the refusal carries the door.** After `Done`,
`review_document` on the same identity is refused — and the refusal names `newReview: true` as the
way through, which appends an ordinal to the key (`spec.md`, `spec.md#2`, …). Silently starting a
fresh session would hide a finished review; refusing with no door would make an unchanged policy
permanently unreviewable, which is what the plan round found. `GateHeld`
([`RoundMachine.cs:77`](../src_mcp/core/Rounds/RoundMachine.cs#L77)) is the precedent: a refusal
names every way out, because a refusal with no door is a stall.

**The purpose is stored with the session, and a changed purpose is a new review.** The same document
reviewed for "check the security controls" and later for "check it is complete" is two reviews, not
two rounds of one; `review_document` refuses a changed purpose on an open session, with the same
`newReview: true` door. Found by the plan round, which pointed out that the first draft would have
silently applied round 1's scope to round 2's question.

`SessionState` gains `Kind` (`Code` | `Document`), defaulted to `Code` positionally — the same
absent-field discipline plans 1 and 3 both paid for twice: absent means today's behaviour, never
"none" and never "any". A session file written before this plan loads as a code session, because that
is what it was.

**A document session has no plan gate.** `BeginDocumentRound` does not check `PlanProceeded`
(contrast [`RoundMachine.cs:97`](../src_mcp/core/Rounds/RoundMachine.cs#L97), where
`{ PlanProceeded: false }` refuses a code round). There is no plan before a document — the document
IS the work. It keeps the other three refusals verbatim: a held human gate, an unresolved previous
round, and a finished session.

**A code session refuses `review_document` and a document session refuses `review_code`**, each with
a sentence naming which kind it is and how to open the other. A session that quietly changed kind
under the caller would count one budget against two different jobs.

### 3. The document is confined to the repository

*(New after the plan round: two vendors independently refused the first draft's unrestricted path,
both as a Major security finding, and they were right.)*

`review_document` reads a path and ships its contents to other vendors' APIs. Unrestricted, that is
an exfiltration primitive with a friendly name: an AI that has been prompt-injected — or has simply
misread an instruction — passes `~/.ssh/id_rsa` or a repository's `.env`, and the gate mails it to
three vendors.

So **`documentPath` must resolve inside `repoPath`**, and the containment test is the real one:

- resolve both to full paths and follow symlinks (`Path.GetFullPath` then the link target), because a
  symlink inside the repo pointing outside it is the case a prefix check misses;
- compare at a directory SEPARATOR, never by string prefix — `/repo-secrets` is not inside `/repo`;
- refuse with a sentence naming the resolved path and the root it had to be under.

**What this costs, said plainly:** a manager whose documents live outside any checkout cannot pass a
path. They can pass `documentText` with a `documentName`, which is what their AI does anyway when it
has converted a `.docx`. Widening this later is a decision with an owner; a gate that reads anything
on the disk by default is not.

### 4. The artifact: stored, bounded, and proven to be text

What is stored is the document's TEXT, under the session directory, named by the content hash from
decision 2.

**Written temp-then-rename**, like the session file beside it
([`SessionStore.cs:172`](../src_mcp/src/Server/SessionStore.cs#L172) — "a torn session file would
refuse every later call on that repo+branch") and like the extension's prompt writes. A process
killed mid-write must not leave a session pointing at half a document; an artifact the session
references and cannot read is named as such rather than reviewed as an empty one.

**The display name** is the file's own name when a path was given, else `documentName`, else the
document's first heading — which is [`RoundSubject`](../src_mcp/core/Rounds/RoundSubject.cs) exactly
as it already behaves, including its deliberate refusal to use `Path.GetFileName` (a Windows path
handed to a server under WSL comes back whole).

**A document is proven to be text, not guessed at.** The first draft said "a NUL byte in the first
bytes", and both codex and gemini refused that as too weak — UTF-16 has NULs in alternate positions,
and a binary file with no NUL in the probed prefix decodes to mojibake that three reviewers then
review in earnest. So:

- decode with `UTF8Encoding(false, throwOnInvalidBytes: true)` over the WHOLE payload; anything that
  throws is refused as not-text;
- refuse `.docx`, `.pdf`, `.xlsx`, `.pptx`, `.zip`, `.png`, `.jpg` and friends by EXTENSION as well,
  before reading a byte, because naming the format makes the refusal actionable;
- the refusal is an INSTRUCTION, not a diagnosis: *"`spec.docx` is not text. Convert it and pass the
  result as `documentText` with a `documentName`."* The plan round's point was that an AI told only
  "binary file refused" retries with the same bytes and the session stalls.

**Bounded at 256 KB of text.** The first draft said 1 MB, and codex pointed out that 1 MB is roughly
250 000 tokens — accepted by the tool and then refused by a 128K-context reviewer, which is a failure
the caller cannot see coming. 256 KB is roughly 64 000 tokens and fits every reviewer this product
ships with, prompt and output included. The refusal names the size, the bound, and that a larger
document should be reviewed in parts.

### 5. The tool

```
review_document(repoPath, branch, purposeText,
                documentPath | (documentText + documentName),
                newReview?)
```

`purposeText` is to a document what `planText` is to a diff: **what this document is for.** The code
gate refuses a bare diff ([`CodeScope.IsSubstantial`](../src_mcp/core/Rounds/CodeScope.cs)) on the
grounds that a reviewer holding only the artefact can say whether it is defensible but not whether it
is what was asked for. A document has the same two questions and the second one matters more: a
specification can be clear, complete, internally consistent and for the wrong project.

So `purposeText` is REQUIRED and held to `CodeScope`'s existing substantiality rule — reused, not
re-implemented. *(The name `CodeScope` is then wrong for one of its two callers; it is renamed
`ReviewScope` in this change. A second copy of the rule is not an option.)*

Exactly one of `documentPath` and `documentText` — both and neither are two different refusals,
because "you sent two" and "you sent none" send the caller to different fixes.

`review_document` is refused when every document role is switched off, before anything is built —
the same guard both existing stages have and for the same reason (a round that launches no reviewer
is not an empty round, it is an unresolved one that sits open for ever).

### 6. A finding's CATEGORY has to be able to describe a document

This is the defect that would make a green build ship a broken feature, and it is worth stating
plainly because nothing about it is visible from the roles page.

`ReviewParser.ParseCategory` ([`ReviewParser.cs:102`](../src_mcp/core/Findings/ReviewParser.cs#L102))
answers `null` for anything outside the six code categories, and a `null` category makes the finding
a `RejectedEntry` rather than a `Finding`. The schema every vendor is handed
([`FindingSchema.cs:31`](../src_mcp/core/Findings/FindingSchema.cs#L31)) names the same six. So a
requirements reviewer answering `"category": "completeness"` has its findings **dropped** — and
codex, whose schema is enforced by the API, would be refused outright.

`Category` ([`Finding.cs:14`](../src_mcp/core/Findings/Finding.cs#L14)) gains four, chosen to be the
things that are actually wrong with documents rather than a translation of the code six:

| Category | What it names |
|---|---|
| `Clarity` | It can be read two ways, and the two ways imply different work. |
| `Completeness` | Something a reader must know to act is not in it. |
| `Consistency` | Two parts of the document contradict each other. |
| `Feasibility` | It asks for something that cannot be done as described, or not for the stated cost. |

Four, not fourteen: a category list a reviewer has to think about is a category list reviewers answer
inconsistently, and dedup counts across vendors.

**The six code categories stay valid everywhere.** A document round may answer `security` or
`reliability` — a policy document has both — and a code round answering `clarity` is a legitimate
remark about a comment or a name. One enum, one schema, no per-stage variant: two schemas is two
things to keep in step, and `FindingSchemaTests` exists because that drift is what happened last
time.

### 7. `notes` — the answer to "and producing summaries"

A round returns findings and a verdict. A summary is neither, and the manager asked for one.

The wire gains ONE optional top-level field per reviewer:

```json
{ "findings": [ … ], "notes": "…" }
```

`notes` is prose, **non-gating**, never deduplicated, and attributed to the reviewer that wrote it —
three vendors' accounts of the same document side by side is the useful artefact, and merging them
would destroy the only property that makes it worth reading. It is `null` on every code round,
because no code prompt asks for it and `RawReview` is nullable throughout by construction.

Requirements, in order:

- OpenAI's structured-output rules are not JSON Schema's: `notes` goes in `required` with type
  `["string", "null"]`, never omitted from `required`. The file already carries this rule and the
  scar that produced it (a 400 on every reviewer, 2026-08-31); `FindingSchemaTests` holds it.
- A reviewer that answers no `notes` is not a failure and is never reported as one.
- **`notes` can never gate.** A verdict is computed from gating findings; prose has no severity and
  must not acquire one by accident.
- **Both shipped document prompts ASK for it, in those words, and a test proves a non-empty note
  travels from the vendor's answer to the tool's reply.** Added after the plan round, which found the
  hole precisely: `notes: null` is a valid review, so a shipped prompt that only asks for findings
  satisfies every other check on this page and leaves the manager with no summary at all.

The alternative — a "Summary" role whose findings are the summary — was refused. It makes every
summary line a thing a caller must `accept` or `reject`, which is the one duty the protocol enforces
absolutely, and it would put prose through dedup.

### 8. The five-active limit is counted per BUCKET on one side and per STAGE on the other

The server counts per bucket ([`RoleComposition.cs:61`](../src_mcp/core/Rounds/RoleComposition.cs#L61),
`active[role.Bucket]`). The extension counts per stage:

```ts
// src_vs_code/src/roles.ts:317
export function activeCount(rows: readonly RoleRow[], stage: string): number {
  return composed(rows).filter((r) => stageOf(r) === stage && isActive(r)).length;
}
```

Today that divergence is invisible, because document roles run in nothing. **On the day this plan
ships it becomes step 2 of the story above**: a person with the five shipped code roles switched on
cannot switch on a single document role — the page refuses, and the server it is refusing on behalf
of would have accepted. It is the first thing the manager would hit and it would look like the
feature not working.

`activeCount` takes a bucket. So does `MAX_ACTIVE_PER_STAGE`, which is renamed to say bucket.

### 9. The extension: three groups, not "plan and everything else"

Two places discriminate by *not plan*:

- [`rolesPage.ts:321`](../src_vs_code/src/rolesPage.ts#L321) — `all.filter((r) => stageOf(r) !== PLAN_STAGE)`
  is the "Code review" group, so a document role is drawn under a heading that is wrong about it.
- [`panelView.ts:1339`](../src_vs_code/src/panelView.ts#L1339) — `stageOf(r) !== 'plan' && (r.programmingTask ?? true)`
  drops document roles from the panel entirely, so they have no round budget and no enable switch a
  person can see.

Both become a grouping by bucket: **Plan review**, **Code review**, **Document review**. The roles
page's stage `<select>` ([`rolesPage.ts:228`](../src_vs_code/src/rolesPage.ts#L228)) offers two
options and sets `stage`; the kind is a separate checkbox
([`rolesPage.ts:232`](../src_vs_code/src/rolesPage.ts#L232)) — that stays as it is, because stage and
kind are genuinely two questions, and collapsing them into one three-valued picker would make
`plan:document` unspellable rather than unbuilt.

The hint at [`rolesPage.ts:237`](../src_vs_code/src/rolesPage.ts#L237) — *"takes part in no round yet
— the stage that reviews a document rather than a diff is still being built"* — is deleted for
`result:document` and **kept, reworded, for `plan:document`**, which is still true.

### 10. The snippet and the rule have to teach the third call

[`claudeSnippet.ts`](../src_vs_code/src/claudeSnippet.ts) carries `SNIPPET_VERSION = 5` and a hash
guard that will not let the version be forgotten; the shared rule is
`.agents/conventions/common/coai-review-gate.md`, at the same v5, and `gate-snippet-check` holds
there to be exactly one copy of it. Both gain the document flow and go to **v6** in the same change
as the tool, because a snippet sitting in somebody's `CLAUDE.md` is the text actually being obeyed —
that is the whole reason the version marker exists.

The rule's numbered order stays a contract for code and gains a short parallel branch: a document
review is `open` → `review_document` → `resolve` → repeat, with no plan gate before it and no code
gate after it.

## Build order — ONE unit

The first draft cut this into four stories on four branches. **The operator's standing command on the
plan round forbids that**: this plan is already a piece of a split, and it is to be built as one unit,
reviewed as one diff through this gate, fixed, documented, tested and committed.

It is not too big for one unit, and the reason is that the change is narrow at every point it
touches: one predicate in the catalog, one map in `PanelConfig`, one optional parameter on the key,
one enum value, four category values, one wire field, one tool, and a grouping change in two places
in the extension. Order within the single branch:

1. The core: `Stage.DocumentReview`, the bucket map, `RolesOf(bucket)`, `SessionState.Kind`,
   `SessionKey`'s third segment, `BeginDocumentRound`, `CodeScope` → `ReviewScope`.
2. The wire: four categories, `notes` through the schema, `RawReview`, `ReviewParser`, the reply.
3. The tool: `review_document`, the artifact store, containment, the text proof, the bounds,
   `ReviewDocumentAsync`, and the two shipped document roles with their prompts.
4. The person's side: bucket grouping, `activeCount`, the hint split, the snippet and rule at v6, the
   help in five languages, the module docs, the CHANGELOG.

## Test plan

TDD — the RED test first, and its failure message has to describe the real symptom rather than a
setup error.

**The core**
- A `result:document` role is returned by `RolesOf(Stage.DocumentReview)` and by nothing else.
- A `result:code` role is returned by `RolesOf(Stage.CodeReview)` and by nothing else. *(The
  regression guard: the whole change is one predicate, and the way to get it wrong is to widen it.)*
- A `plan:document` role is returned by no stage at all, asserted rather than assumed.
- `SessionKey.For(repo, branch)` is **byte-identical** to today's value, pinned as a literal, because
  the thing being protected is every session file already on disk.
- A document round does not require `PlanProceeded`; a code round still does.
- `BeginDocumentRound` refuses on a held human gate, an unresolved round, and a `Done` session —
  three cases, three distinct sentences.
- A session with no `Kind` in its file loads as `Code`.

**Identity (the plan round's chief finding, as tests)**
- The same document EDITED between rounds keeps its session: round 2 finds round 1.
- Two different files with identical text are two sessions.
- A `Done` session refuses, and the refusal names `newReview`.
- `newReview: true` opens an ordinal session and leaves the finished one intact.
- A changed `purposeText` on an open session is refused, naming `newReview`.
- `documentText` with no `documentName` is refused; a `documentName` of the wrong shape is refused.

**Confinement and text**
- A path outside `repoPath` is refused, and so is a symlink inside it that resolves outside.
- `/repo-secrets/x.md` is refused against a root of `/repo` — the separator test, not a prefix test.
- Invalid UTF-8 is refused; a `.docx` is refused by extension before a byte is read; the refusal text
  contains the word `documentText`, because it has to tell the caller what to do next.
- Over 256 KB is refused, and the message carries the size and the bound.
- The artifact is written temp-then-rename; a session naming an artifact that is not there is
  reported as such rather than reviewed as empty.

**The wire**
- Each of the four new categories parses; an unknown one is still a `RejectedEntry` naming itself.
- The six code categories still parse. *(Same regression shape as the core.)*
- `FindingSchemaTests`' existing rules hold with `notes` added: `additionalProperties: false`, every
  declared key present in `required`.
- A review whose `notes` is absent, `null` or empty is a normal review, no failure reported.
- A round whose only content is `notes` has a non-gating verdict.

**End to end, through the public tool** *(the plan round's other chief finding: every test above can
be green while `review_document` returns an empty round, because the wiring is what none of them
touch)*
- One active document role, a scripted vendor through `FakeCli`, the shape
  `ACustomRoleReviewsAChangeTests` already established: the round names the role, the finding carries
  it, the stored document text and the purpose are what the reviewer was handed, and **a non-empty
  `notes` comes back in the tool's reply**.
- `review_document` on a code session is refused, and `review_code` on a document session is refused.
- Every document role switched off refuses before anything is built.
- The two shipped roles load from the seed in both halves — each half asserted against
  `shared/builtin-roles.json` by its own suite, which is the rule plan 1 established.

**The person's side**
- `activeCount` counts per bucket: five active code roles do NOT block a document role.
- The roles page draws three groups, and a document role is under the document heading.
- The panel draws a document role with a budget and an enable switch.
- The "still being built" hint is gone for `result:document` and present for `plan:document`.
- `snippetVersion.test.ts` pins v6 to the new body hash; `gate-snippet-check` stays green.
- All five languages change in the same commit as the English. *(The stale-translation trap: coverage
  marks a MISSING translation and rewards a STALE one.)*

## Definition of Done

- [ ] A role with `programmingTask: false` at the result stage takes part in a round, and the round
      returns its findings.
- [ ] `review_document` exists; exactly one of `documentPath` / `documentText` is required, and both
      and neither are two different refusals.
- [ ] A document session is keyed by the document's identity, survives an edit between rounds, and is
      re-reviewable after `Done` only through `newReview`.
- [ ] A changed purpose on an open session is refused by name.
- [ ] A path outside the repository — including through a symlink — is refused.
- [ ] Non-text is refused with an instruction the calling AI can act on; over 256 KB is refused with
      the size and the bound.
- [ ] The artifact is written atomically, and a missing one is named rather than reviewed as empty.
- [ ] A document session and a code session are different kinds, refuse each other's stage by name,
      and every session file written before this plan loads as a code session.
- [ ] `SessionKey.For(repo, branch)` is unchanged for every existing session.
- [ ] The four document categories parse, the six code categories still parse, and an unknown
      category is still named rather than dropped.
- [ ] `notes` reaches the caller per reviewer, unmerged, cannot gate a round, and **both shipped
      document prompts ask for it**, proven end to end.
- [ ] The five-active limit means the same thing in both halves.
- [ ] The roles page and the panel both group by bucket, and a document role has a budget and a switch.
- [ ] The snippet and the shared rule are at v6, in the same change as the tool, in five languages.
- [ ] `research/module_core.md`, `research/module_server.md`, `research/module_extension.md` and
      `research/architecture.md` describe the third stage; `todo/README.md` matches the folder.
- [ ] The diff went through `review_code` → `resolve` on this branch.
- [ ] `plan-lifecycle`, `pin-check`, `adapter-check`, `gate-snippet-check` and all three suites green.

## What the CODE round changed

Forty-nine findings from twelve reviewers, twenty-nine accepted. Three clusters, and the first two
were holes rather than opinions.

**The confinement was not confinement.** Three reviewers found the same escape independently:
`File.ResolveLinkTarget` resolves the FINAL component only, so with `repo/docs` a symlink to
somewhere outside, `repo/docs/secret.md` has a perfectly ordinary last component — the resolver
returned it unchanged, the containment check saw a path plainly inside the repository, and the read
followed the parent link. `DocumentReader.Canonical` walks from the root down now, re-resolving after
every step. Proved by reverting it: the test came back `Ready` instead of `Refused`, which is the
outside file being read and sent to three vendors.

And the normalisation was lower-casing BOTH sides, which two reviewers attacked from opposite ends.
On Linux `/tmp/repo` and `/tmp/Repo` are two directories, so a sibling looked contained; and two
files differing only in case became one session. `DocumentId.Comparison` is the platform's own rule
now, and the identity keeps the path's own case. Proved the same way: the prefix-check revert made
`/repo-secrets/x.md` resolve to `secrets.md` — accepted, *with a plausible identity*.

**A relative path was resolved against the process, not the repository.** `Path.GetFullPath(path)`
uses the working directory, which for an MCP server is wherever the client launched it. A caller
passing `docs/spec.md` — the ordinary thing to pass — named a file nobody meant.

**And `resolve` could not find the session `review_document` had made.** Five findings across two
vendors were one defect: `resolve` and `status` rebuilt the identity by a second route — rooted paths
only, no links, a different case rule. `DocumentReader.IdentityOf` is the one resolution, called from
all three.

The rest, in short:

- A new session was saved with an empty purpose, so a round interrupted before its own save left a
  session whose stored purpose was blank — and the retry was then refused as a DIFFERENT purpose. A
  trap that only fires after something else has already gone wrong.
- `newReview` over a round nobody had resolved orphaned it — the same failure this design was
  rewritten to prevent, one ordinal further along. Refused now, with the door named.
- `#` is an ordinary character in a filename, so `notes#2.md` and the second review of `notes.md`
  shared an identity. Refused by name rather than escaped.
- **`isPlanStage` was a lie.** It answered two questions — which vendor switch serves this round, and
  whether a reviewer gets a checkout — which agreed for as long as there were two stages. A document
  round wants the plan stage's answer to both and is not the plan stage. Two told fields now,
  `ServedByPlanSwitch` and `ReadsCheckout`, which is the discipline that parameter was introduced
  with.
- The deadline read the roster a second time, so the guard and the multiplier could disagree about
  whether it was empty. One read, passed down.
- `DocumentTurn` was two nullable fields where every other outcome in this codebase is a closed
  union, and the C# bucket was a bare string where the extension's is a type — the same primitive
  obsession the extension's seven miscalls had already paid for.
- Snapshots grew for ever. Thirty days, swept on the write path, abandoned `.writing` files included.
- The ordinal walk deserialised whole session files to learn file names; `SessionStore.Exists` stats.
- `research/module_tests.md` is the one module doc the rules name by name, and it was untouched.

## What the code round refused, and why

Twenty of forty-nine, and three of them were factually wrong about the diff: the generated
`gateRule.ts` is git-ignored and regenerated by `precompile`, `status` already takes `document`
(`Tools.cs:174`), and `newReview` on `resolve` would mean recording decisions on a session being
replaced in the same call. Four more applied the plan status-line rule to `architecture.md` and three
`module_*.md` files, which are not plans.

The rest were the shape of the wire: `notes` must be in `required` with a nullable type because that
is what OpenAI's structured outputs enforce, and it is absent rather than empty on a round where
nobody wrote prose because `Commands`, `CommandsPreamble` and `Cost` on the same record already mean
absent that way. One was accepted as a debt without being acted on: `PanelService` is over 2,200
lines, and splitting it inside this diff would bury four hundred new lines in two thousand moved
ones.

## What the plan round refused, and why

Two findings of eighteen were rejected with reasons, recorded because a reasoned rejection is
discounted in later rounds and the reason is the whole of its value.

- **"Backward compatibility relies on an implicit assumption about artifact ID generation"** (local,
  Major). It describes a state that cannot exist: nothing has ever written a three-segment session
  key, because the third parameter is introduced by this plan. Every key on disk has two segments,
  which is exactly what the byte-identical claim is about and what the test pins as a literal. A
  migration for a shape that was never produced is a code path no input can reach.
- **"Unknown finding categories are silently dropped, hiding data loss"** (local, Major). The
  mechanism it asks for is the one that already exists. An unrecognised category does not drop the
  finding: it becomes a `RejectedEntry`, whose own type documentation reads *"an entry a vendor sent
  that could not become a `Finding` — named, never dropped"*, and `rejectedEntries` is carried in
  every reply. The premise — that the manager sees fewer findings with no explanation — is false.

## Out of scope

- **A document never reaches a Team server.** Plan 5,
  *PLAN_team_server_reviews_documents*. Today a remote reviewer is handed its prompt and nothing
  else; a document round on a remote vendor is excluded by name with the reason, using the
  `SkippedRole` machinery that already exists for exactly this — a decision the gate made, reported
  in words that can never read as a failure.
- **`plan:document` — a plan review for non-programming work.** Stored, composed, counted, and run by
  nothing. Named as waiting, in the page and in the module doc.
- **No conversion of anything.** `.docx`, PDF, HTML: the AI calling the tool converts, or the person
  does. Named in the refusal, with the next step, so nobody has to guess.
- **No per-model context validation.** codex asked for the document plus prompt to be checked against
  each selected model's context window. The bound came down to 256 KB instead, which is the part that
  can be enforced honestly: this product does not hold a reliable context size for every vendor and
  every model a person may configure, and a check against a number we are guessing would refuse valid
  rounds while still missing invalid ones. Worth its own plan the day model capabilities are data.
- **No change to what a code round does.** Same roles, same diff, same verdict, same refusals. The
  regression tests above say so.
- **No release.** The extension and `coai-mcp` are versioned and tagged as a separate act, as always.
