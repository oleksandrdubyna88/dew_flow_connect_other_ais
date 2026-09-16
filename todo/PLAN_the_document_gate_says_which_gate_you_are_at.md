# PLAN — The document gate says which gate you are at

> Status: **plan only, nothing implemented yet.** Scope: `common/coai-document-gate.md` in the
> `dew_flow_conventions` submodule, and the `review_document` and `review_plan` tool descriptions in
> `src_mcp/src/Tools.cs`. A conventions commit, so a `promote-release` and the six-consumer pin
> cascade come with it.
>
> Related docs: [module_server.md](../research/module_server.md),
> [module_extension.md](../research/module_extension.md).
> Boundary table below — the version cascade this change contributes to belongs to
> [PLAN_a_finding_that_changes_everything_calls_the_consultant.md](PLAN_a_finding_that_changes_everything_calls_the_consultant.md).

## The symptom

**Sessions keep calling `review_document` on a plan.** Reported by the operator on 2026-09-16 as a
thing that happens repeatedly, to different agents, in different repositories.

The reflex looks like a reading failure and is not one. Both texts already say the right thing:
`Tools.cs:128-131` opens with *"for work whose result is a document rather than a diff… the document
IS the work"*, and `common/coai-document-gate.md:22-24` says *"no plan round before it and no code
round after it"*.

What defeats both sentences is the **list of examples that follows them**:

> Reach for it when what you are producing or checking is a document — a specification, a policy, a
> proposal, a brief, a requirements list — rather than a diff.
> — `common/coai-document-gate.md:22-24`

A plan **is** a proposal. A plan **is** a requirements list. An agent holding
`todo/PLAN_something.md` matches it against that list, matches twice, and calls `review_document`.
The general sentence is correct and the specific list contradicts it — and a list of concrete nouns
beats an abstract contrast every time, which is why the mistake is so consistent.

The cost is not cosmetic. A plan sent to the document gate opens a review keyed by the document
rather than the branch, and it never satisfies the thing that actually gates the work:
`review_code` REFUSES until a **plan** round reached `proceed`. So the agent does a full round, spends
three vendors' reviewers, and arrives at implementation with the gate still shut — and the refusal it
then meets says nothing about the gate it used by mistake.

## The goal

One discriminator, stated in the same words on every surface, that a reader can apply without
judgement:

> **The test is what exists when the task is finished.** If it is code, the document in your hand is
> a plan FOR that code and goes to `review_plan`. If the deliverable is the text itself and nothing
> will be built from it, it goes to `review_document`.

And, because the list is what does the damage, the counter-example is named: **a plan is not one of
these.**

## The change, in full text

### 1. `common/coai-document-gate.md` (the `dew_flow_conventions` submodule)

Immediately after the "Reach for it when…" sentence at `:22-24`, so the correction sits against the
list that needs it rather than three paragraphs away:

```markdown
**A PLAN is not one of these, and that is the mistake this paragraph exists to stop.** A plan is a
proposal and a requirements list, so it matches the words above twice — which is exactly why the
words above are not the test. The test is what exists when the task is FINISHED: if it is code, the
document in your hand is a plan for that code and it goes to `mcp__coai__review_plan`, the only gate
that unlocks `mcp__coai__review_code`. If the deliverable is the text itself and nothing will be
built from it, it belongs here. Sending a plan here costs a full round of every reviewer and leaves
the code gate exactly as shut as it was, because a document round is keyed by the document and the
code gate asks about the branch.
```

The marker `<!-- coai-document v2 -->` becomes `v3` and stays the **first line after the
frontmatter** — `canonical-markers.test.mjs:33-58` exists because a story once put `owns:` comments
above a marker, passed everything in conventions, was promoted to `release`, and broke a consumer's
build with `missing canonical marker`.

`research/rule-bodies.json` is updated through `tools/rule-bodies.mjs` naming this rule's id — the
2026-09-15 arrangement, under which a body that changes without its hash changing is a red suite and
a deliberate change is a two-line diff beside the prose.

### 2. `src_mcp/src/Tools.cs` — `review_document` (`:128-158`)

A second paragraph, straight after the first:

> **A PLAN is not one of these.** The test is what exists when the task is finished: if it is code,
> the document in your hand is a plan FOR that code and goes to `review_plan` — the only gate that
> unlocks `review_code`. If the deliverable is the text itself and nothing will be built from it, it
> belongs here. A plan is a proposal and a requirements list, which is why the words above are not
> the test.

### 3. `src_mcp/src/Tools.cs` — `review_plan` (`:77-84`)

The same discriminator from the other side, one sentence:

> **This is the gate for a document that code will be written FROM, and the only one that unlocks
> `review_code`. A document that is itself the deliverable goes to `review_document`.**

## Boundaries

| Item | Who owns it |
|---|---|
| the wording that tells the two gates apart, in the shared rule and in both tool descriptions | **this plan** |
| `DOCUMENT_VERSION` 2 → 3, `SNIPPET_BODY_SHA`, `ARTEFACT_VERSION` → 9 and the `(v9)` menu title | [PLAN_a_finding_that_changes_everything_calls_the_consultant.md](PLAN_a_finding_that_changes_everything_calls_the_consultant.md) — **there is one artefact and it may move only once**, so both plans' text changes land before a single cascade |
| the sixth consultant trigger, the burden of proof, the untrusted-evidence boundary | that plan |
| what the document gate DOES once you are correctly at it — `purposeText`, the per-document session, `notes` | [PLAN_consultant.md](../research/PLAN_consultant.md) and the shipped rule; untouched here |

**Ordering consequence, and it is the whole reason these two plans share a branch:** editing
`coai-document-gate.md` changes `DOCUMENT_RULE`, which changes the composed artefact, which changes
`SNIPPET_BODY_SHA`. If this change and the consultant change shipped as two cascades the artefact
would go to v9 and then v10, and every pasted copy would be told it is behind twice for what is one
task.

## Build order

1. **In `dew_flow_conventions`**, on its own branch and through its own gate session: the paragraph,
   the marker `v2` → `v3`, `rule-bodies.mjs` for this id. `node tools/rules.test.mjs` and
   `node tools/canonical-markers.test.mjs` green. PR, merge, then `promote-release` — the only
   sanctioned mover of the `release` branch that consumers pin.
2. **The pin cascade**, and it is not uniform. The five consumers that do not generate the pasted
   artefact take a standalone pin bump; `pin-check.mjs` green in each. **This product's own pin bump
   is NOT standalone** — `snippetVersion.test.ts:519-530` compares the marker in the mounted text
   against `KNOWN_HALVES`, so bringing `coai-document v3` in while `DOCUMENT_VERSION` is still 2
   fails with `coai-document's marker and its version disagree`. It rides in the cascade commit of
   the consultant plan's story 2.1. A stale pin blocks every open PR in a repository at once, which
   is why none of this is left for later.
3. **`Tools.cs`**: the two descriptions, with their assertions added to the test class the consultant
   plan creates (`TheGateSaysWhenToConsultTests`) or a sibling — one class per subject, decided when
   the file exists.
4. The artefact cascade is executed by the consultant plan, once, with this change already in the
   mounted rule.

## Test plan

| What | Where | Teeth |
|---|---|---|
| the rule body's hash moved with its text | `dew_flow_conventions` `tools/rules.test.mjs` | red on a body that changed without its hash |
| the marker is still the first line of the body | `tools/canonical-markers.test.mjs:33-58` | red if anything precedes it — the incident it was written for |
| the mounted rule is byte-identical to what the menu hands out | `snippetVersion.test.ts:175-201` | red until `prepare:gate` is re-run against the new pin |
| `review_document` names the plan counter-example | new assertion, `src_mcp/tests` | **both directions** per `TheServerSaysWhatOnlyTheRuleSaidTests.cs:86` |
| `review_plan` says it is the gate that unlocks `review_code` | same | as above |

## Definition of Done

- [ ] The counter-example sits against the list that causes the mistake, not elsewhere in the file.
- [ ] The same discriminator — *what exists when the task is finished* — appears in all three places,
      in the same words.
- [ ] `coai-document v3`, the marker still first after the frontmatter, `rule-bodies.json` updated by
      the tool and not by hand.
- [ ] **Both new tool names declared with `owns:` comments, and no ambiguous noun left behind a
      definite article.** A shared rule may not name a product's tools undeclared, tokens are matched
      exactly, and `ownership-check.mjs` is a CI step that `npm test` does not run — which is how this
      reached CI red with a green local suite.
- [ ] `promote-release` run, and `pin-check` green in all six consumers.
- [ ] Both tool-description assertions hold in both directions.
- [ ] `DOCUMENT_VERSION` 2 → 3 recorded here and EXECUTED by the consultant plan's single cascade.
- [ ] Promoted to `research/` with its deviations; `todo/README.md` updated; `plan-lifecycle.mjs` green.
