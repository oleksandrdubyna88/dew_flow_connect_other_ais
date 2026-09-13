# PLAN — the gate reviews a document (4 of 5)

> Status: **plan only, nothing implemented yet.** Scope: `src_mcp` (a third stage, the session kind,
> the artifact, `review_document`, the finding wire), `src_vs_code` (the roles page, the panel, the
> snippet, the help), `shared/builtin-roles.json` (two shipped document roles), and the tests for all
> of it. No `src_server` change — that is plan 5.
>
> Related docs: [module_core.md](../research/module_core.md),
> [module_server.md](../research/module_server.md),
> [module_extension.md](../research/module_extension.md),
> [architecture.md](../research/architecture.md);
> plan 1: [PLAN_review_roles_become_data.md](../research/PLAN_review_roles_become_data.md),
> plan 2: [PLAN_review_roles_crud_tab.md](../research/PLAN_review_roles_crud_tab.md),
> plan 3: [PLAN_team_server_accepts_custom_roles.md](../research/PLAN_team_server_accepts_custom_roles.md),
> all three shipped.

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
   decision 7.
3. The AI in their editor calls `review_document`. — **Which does not exist.**
4. Three vendors read the document and answer findings. — **Which are rejected**, because a
   requirements finding's category is not one of the six code categories; see decision 5.
5. They read a summary of what the document says. — **Which nothing can express**; see decision 6.

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

`PanelConfig.RolesOf(Stage)` ([`SessionState.cs:180`](../src_mcp/core/Rounds/SessionState.cs#L180))
maps a stage to a role stage today:

```csharp
Catalog.RolesOf(stage == Stage.CodeReview ? RoleStages.Result : RoleStages.Plan);
```

It becomes a map to a BUCKET: `PlanReview → plan:code`, `CodeReview → result:code`,
`DocumentReview → result:document`. `RoleCatalog.RolesOf` takes a bucket rather than a stage plus a
hard-coded `ProgrammingTask`, and the predicate above stops being a filter and becomes a match.

**`plan:document` is a fourth bucket and this plan does not run it.** `RoleComposition` already
counts it, so a person can store a plan-stage non-programming role today; it will keep being stored
and keep running in nothing. Naming it as still-waiting rather than silently folding it into
`DocumentReview` is the same choice plan 1 made about `result:document`, and for the same reason:
"not built yet" and "does not exist" are different states. The roles page says so, in the same words
it says everything else it refuses.

### 2. A session KIND, and a session key that carries the artifact

A session is per repo+branch and ends at `Done`
([`SessionKey.cs`, `SessionState.cs:327`](../src_mcp/core/Rounds/SessionState.cs#L327)):

```csharp
public static string For(string repoPath, string branch) =>
    $"{repoPath.Replace('\\', '/').TrimEnd('/').ToLowerInvariant()}#{branch.Trim()}";
```

That rule is correct for code — a branch is reviewed once and merged — and **wrong for documents.** A
manager with ten documents does not have ten branches, and would not want them. So:

```csharp
public static string For(string repoPath, string branch, string artifact = "")
```

with `artifact` empty for every code session, which makes **every existing key byte-identical** and
needs no migration, no versioning and no sweep. A document session's artifact segment is the
artifact id from decision 3.

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

### 3. The artifact: the document is STORED, and stored once

A round must review the same text a later round reviews, and the rounds log must be able to say what
was reviewed a week later. Neither is true of a string that only ever lived in a tool argument.

`review_document` takes the document **as text** (`documentText`), or **as a path on this machine**
(`documentPath`) — exactly one of the two, and a refusal naming which when both or neither arrive.
The path form is the one a person actually has, and resolving it here rather than making the caller
paste a file is the difference between a tool a manager's AI uses and one it gets wrong.

What is stored is the TEXT, under the session directory, keyed by a content hash:

- The **artifact id** is the first 16 hex characters of the SHA-256 of the normalised text. Content,
  not filename: the same document reviewed from two folders is one session, and a renamed file does
  not start a new one. It is also what makes decision 2's key work — a stable, filesystem-safe
  segment that is not a path.
- The **display name** is the file's own name when a path was given, else the document's first
  heading, else its first words — which is [`RoundSubject`](../src_mcp/core/Rounds/RoundSubject.cs)
  exactly as it already behaves, including its deliberate refusal to use `Path.GetFileName` (a
  Windows path handed to a server under WSL comes back whole).

**A path is read as UTF-8 text and nothing else.** No `.docx`, no PDF, no conversion: a binary file
is refused by name, with the sentence that the AI calling this can convert it. Guessing an encoding
or shelling out to a converter is a whole product, and putting a bad one inside a review gate means
reviewers confidently reviewing mojibake.

Bounds, because a document has no `DiffShaper` upstream of it: refuse above **1 MB** of text, and say
the size and the bound. The code stage's shaping exists precisely because an unbounded payload is a
per-reviewer cost across every vendor; a document round would otherwise be the one path with no
ceiling at all.

### 4. The tool, and what its scope argument means

```
review_document(repoPath, branch, documentText | documentPath, purposeText)
```

`purposeText` is to a document what `planText` is to a diff: **what this document is for.** The code
gate refuses a bare diff ([`CodeScope.IsSubstantial`](../src_mcp/core/Rounds/CodeScope.cs)) on the
grounds that a reviewer holding only the artefact can say whether it is defensible but not whether it
is what was asked for. A document has the same two questions and the second one is the one that
matters more: a specification can be clear, complete, internally consistent and for the wrong
project.

So `purposeText` is REQUIRED and held to `CodeScope`'s existing substantiality rule — reused, not
re-implemented. *(The name `CodeScope` will then be wrong for one of its two callers. Renaming it is
in story D1; a second copy of the rule is not.)*

`review_document` is refused when every document role is switched off, before anything is built —
the same guard both existing stages have and for the same reason (a round that launches no reviewer
is not an empty round, it is an unresolved one that sits open for ever).

### 5. A finding's CATEGORY has to be able to describe a document

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

### 6. `notes` — the answer to "and producing summaries"

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

The alternative — a "Summary" role whose findings are the summary — was refused. It makes every
summary line a thing a caller must `accept` or `reject`, which is the one duty the protocol enforces
absolutely, and it would put prose through dedup.

### 7. The five-active limit is counted per BUCKET on one side and per STAGE on the other

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

### 8. The extension: three groups, not "plan and everything else"

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

### 9. The snippet and the rule have to teach the third call

[`claudeSnippet.ts`](../src_vs_code/src/claudeSnippet.ts) carries `SNIPPET_VERSION = 5` and a hash
guard that will not let the version be forgotten; the shared rule is
`.agents/conventions/common/coai-review-gate.md`, at the same v5, and `gate-snippet-check` holds
there to be exactly one copy of it. Both gain the document flow and go to **v6** in the same change
as the tool, because a snippet sitting in somebody's `CLAUDE.md` is the text actually being obeyed —
that is the whole reason the version marker exists.

The rule's numbered order stays a contract for code and gains a short parallel branch: a document
review is `open` → `review_document` → `resolve` → repeat, with no plan gate before it and no code
gate after it.

## Build order

Four stories, each on its own branch, each through the gate on its own diff — the shape plans 2 and 3
both used, and the reason is recorded in both: a session is per repo+branch and ends at `Done`.

| Story | Branch | What it lands |
|---|---|---|
| **D1** | `feat/review-document-s1` | The core, no tool: `Stage.DocumentReview`, the bucket map, `RoleCatalog.RolesOf(bucket)`, `SessionState.Kind`, `SessionKey` with its artifact segment, `RoundMachine.BeginDocumentRound`, and `CodeScope` renamed for its two callers. Pure core, pure tests. |
| **D2** | `feat/review-document-s2` | The wire: four categories, `notes` through the schema, `RawReview`, `ReviewParser` and the reply, and the proof that `notes` cannot gate. |
| **D3** | `feat/review-document-s3` | The tool: `review_document`, the artifact store, the path/text refusals and bounds, `ReviewDocumentAsync` in `PanelService`, and the two shipped document roles with their prompt bodies in `shared/builtin-roles.json`. |
| **D4** | `feat/review-document-s4` | The person's side: the bucket grouping on the roles page and in the panel, `activeCount` per bucket, the hint split, the snippet and the shared rule at v6, the help in five languages, and the CHANGELOG. |

D1 and D2 are independent of each other. D3 needs both. D4 needs D1.

## Test plan

Every story is TDD — the RED test first, and its failure message has to describe the real symptom
rather than a setup error.

**D1**
- A `result:document` role is returned by `RolesOf(Stage.DocumentReview)` and by nothing else.
- A `result:code` role is returned by `RolesOf(Stage.CodeReview)` and by nothing else. *(The
  regression guard: the whole change is one predicate, and the way to get it wrong is to widen it.)*
- A `plan:document` role is returned by no stage at all, and that is asserted rather than assumed.
- `SessionKey.For(repo, branch)` is **byte-identical** to today's value. Pinned as a literal, because
  the thing being protected is every session file already on disk.
- A document round does not require `PlanProceeded`; a code round still does.
- `BeginDocumentRound` refuses on a held human gate, an unresolved round, and a `Done` session — three
  cases, three distinct sentences.
- A session with no `Kind` in its file loads as `Code`.

**D2**
- Each of the four new categories parses; an unknown one is still a `RejectedEntry` naming itself.
- The six code categories still parse. *(Same regression shape as D1.)*
- `FindingSchemaTests`' existing rules hold with `notes` added: `additionalProperties: false`, and
  every declared key present in `required`.
- A review whose `notes` is absent, `null` or empty is a normal review with no failure reported.
- **A round whose only content is `notes` has a non-gating verdict.** The load-bearing test of the
  story.

**D3**
- Both arguments, and neither, are refused — two different sentences.
- A path that does not exist, a directory, and a file with a NUL byte in its first bytes are each
  refused by name.
- Over 1 MB is refused, and the message carries the size and the bound.
- The artifact id is the content hash: the same text from two paths is one id; a changed byte is a
  different one.
- `review_document` on a code session is refused, and `review_code` on a document session is refused.
- Every document role switched off refuses before anything is built.
- The two shipped roles load from the seed in both halves — each half asserted against
  `shared/builtin-roles.json` by its own suite, which is the rule plan 1 established.

**D4**
- `activeCount` counts per bucket: five active code roles do NOT block a document role. *(Step 2 of
  the story, as a test.)*
- The roles page draws three groups, and a document role is under the document heading.
- The panel draws a document role with a budget and an enable switch.
- The "still being built" hint is gone for `result:document` and present for `plan:document`.
- `snippetVersion.test.ts` pins v6 to the new body hash; `gate-snippet-check` stays green.
- All five languages change in the same commit as the English. *(The stale-translation trap: coverage
  marks a MISSING translation and rewards a STALE one.)*

## Definition of Done

- [ ] A role with `programmingTask: false` at the result stage takes part in a round, and the round
      returns its findings.
- [ ] `review_document` exists, takes a document as text or as a path, refuses both and neither,
      refuses a binary, and refuses above 1 MB.
- [ ] A document session and a code session are different kinds, refuse each other's stage by name,
      and every session file written before this plan loads as a code session.
- [ ] `SessionKey.For(repo, branch)` is unchanged for every existing session.
- [ ] The four document categories parse, the six code categories still parse, and an unknown
      category is still named rather than dropped.
- [ ] `notes` reaches the caller per reviewer, unmerged, and cannot gate a round.
- [ ] The five-active limit means the same thing in both halves.
- [ ] The roles page and the panel both group by bucket, and a document role has a budget and a switch.
- [ ] The snippet and the shared rule are at v6, in the same change as the tool, in five languages.
- [ ] `research/module_core.md`, `research/module_server.md`, `research/module_extension.md` and
      `research/architecture.md` describe
      the third stage; `todo/README.md` matches the folder.
- [ ] Every story went through `review_plan` → `resolve` → `review_code` → `resolve` on its own branch.
- [ ] `plan-lifecycle`, `pin-check`, `adapter-check`, `gate-snippet-check` and all three suites green.

## Out of scope

- **A document never reaches a Team server.** Plan 5,
  *PLAN_team_server_reviews_documents*. Today a remote reviewer is handed its prompt and nothing
  else; a document round on a remote vendor is excluded by name with the reason, using the
  `SkippedRole` machinery that already exists for exactly this — a decision the gate made, reported
  in words that can never read as a failure.
- **`plan:document` — a plan review for non-programming work.** Stored, composed, counted, and run by
  nothing. Named as waiting, in the page and in the module doc.
- **No conversion of anything.** `.docx`, PDF, HTML: the AI calling the tool converts, or the person
  does. Named in the refusal so nobody has to guess.
- **No change to what a code round does.** Same roles, same diff, same verdict, same refusals. Every
  story carries the regression test that says so.
- **No release.** The extension and `coai-mcp` are versioned and tagged as a separate act, as always.
