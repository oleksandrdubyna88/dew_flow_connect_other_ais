# PLAN — the feature and question halves of the snippet are shared rules

> Status: **IMPLEMENTED, 2026-10-09.** Steps 1–3 shipped: the conventions rules (PR #58, promoted to
> `release` at `bb022429`) and this repository's snippet built from six mounted halves (artefact v17).
> **Deviations:** this repository's half landed in THREE pull requests, not one, because two other
> sessions moved first: the pin bump with `CONSULTANT_VERSION` 4 (#726, artefact 15), then the feature
> half from the mount with the `mounted` flag removed (#730, S3.5 of
> [PLAN_feature_review.md](../todo/PLAN_feature_review.md), artefact 16); the pull request of this plan
> was cut down to the question half (artefact 17). The feature rule's verdicts were corrected twice more before merge, by the conventions
> code round and an own review against the server (`proceed`/`good_enough` owe no second round but the
> person may still ask; `continue_anyway` dropped — a feature round never produces it); the question
> rule gained an explicit no-consultant door (`ask_human` without a `consultId`) and a Definition of Done
> matching its exceptions (CodeRabbit); the bounded smoke run of the reads came back **incomplete** (the native trace overflowed the
> harness cap before a final answer), so read evidence is the resolver's `explain`, not the smoke run.
> **Step 4**, the pin cascade: four consumers were bumped to `bb022429` the same day (mcp #43,
> sidecar_rust #51, benchmark #71, creds_for_devs #200). **Open tail:** `dew_flow_rag_qln`, last by
> design (it pins mcp and benchmark too) — a mechanical bump pull request, no plan of its own.
>
> Scope: `dew_flow_conventions` (`common/`), this repository's snippet (`src_vs_code/src/claudeSnippet.ts`,
> `src_vs_code/scripts/prepare-gate.mjs`, their tests, `package.json`), then the `.agents/conventions` /
> `.claude/rules/shared` pin in every consumer.
>
> Related: [PLAN_feature_review.md](../todo/PLAN_feature_review.md) (D11 made the feature half this
> product's own), [PLAN_question_consultant.md](../todo/PLAN_question_consultant.md) (the phase rule
> `ask_human` enforces).

## Symptom

1. **The conventions carry no feature-gate rule.** The pasted snippet has five halves — `coai-snippet`
   v5, `coai-document` v3, `coai-feature` v2, `coai-caller` v2, `coai-consultant` v3 — and four come from
   the mounted conventions. The fifth is `src_vs_code/src/featureRule.md`, product-owned under D11
   (`claudeSnippet.ts:171`, `KNOWN_HALVES` row `claudeSnippet.ts:198`, `mounted: false`;
   `prepare-gate.mjs:59` `FEATURE_SOURCE`). A repository that mounts the rules never sees it. The
   operator ruled on 2026-09-26 that the conventions must carry it once `review_feature` shipped; it has.
2. **The feature half is wrong about rounds.** v2 says `revise` → "call `review_feature` again", and
   "a finished feature review is reopened with `again: true` once HEAD moved". The server ships D23
   (`src_mcp/src/Server/Stages/FeatureSecondRound.cs:101-126`): one round is the budget; a second only for
   a reviewer failure, a `blocking` finding or the person's request; never a third; `good_enough` closes on
   resolve; `again: true` over the same base is refused without a ground
   (`AFeatureIsReviewedEndToEndTests.cs:476-497`).
3. **No rule says "ask the consultants before the person".** The server's phase rule (the
   `ask_human`/`ask_consultants` descriptions, `src_mcp/src/Tools.cs:383`, `:539`) lets planning questions
   and the first two batches after `proceed` go straight to the person. On 2026-10-09 the operator told a
   session that it should have asked the consultants first and asked them only what neither could decide.
4. **Queued, operator-approved 2026-09-26:** trigger 7 of `coai-consultant.md` — "a cadence consultation
   counts only after `close_consult` records `solved`, `not_solved` or `abandoned`" — and its marker to v4.

## Decisions

- **D1 — the feature half becomes a mounted rule**, `common/coai-feature-gate.md`, id
  `common.coai-feature-gate`. Its body starts with `<!-- coai-feature vN -->` + `## Reviewing the whole
  FEATURE before release (ConnectOtherAIs)`, the heading `prepare-gate.mjs` already requires, then the
  `owns:` lines (the order the canonical-marker test pins).
- **D2 — it moves as v3, not verbatim v2.** The verdict section is rewritten to D23 (symptom 2); three
  phrases the ownership check refuses ("the gate's fourth stage", "the gate switched off", "lets the gate
  find") are reworded; `callerModel` and "never a secret in `lessons`" are added; a Definition of Done is
  appended as on the other coai rules. A pasted v2 is then reported OLDER, which is correct: its round
  advice is wrong. Every phrase `featureSnippet.test.ts` pins survives.
- **D3 — a new rule `common/coai-question-consultant.md`** (`coai-question` v1, heading `## Before you ask
  the person, ask the consultants (ConnectOtherAIs)`). Every question for the person goes to
  `ask_consultants` first, in every phase — a bar above the server's floor, never against its
  mechanics: `consultId` passed to `ask_human` (once, same session, 30 min), `ask_in_conversation` for
  own questions, the gate's own question never sent to consultants, `productionRisk` keeps its path. The
  person gets what the answers did not settle plus three kinds always: actions outside the working copy
  (issues/PRs in another owner's repo, publication, release), changes to the person's machine, pure
  preferences — each question carrying what the consultants said and the session's recommendation. When
  no consultant can be had: say so, cut the list anyway. A skill whose step is to ask the person is not
  overridden.
- **D4 — the question rule is a sixth half of the snippet**, by the consultant/feature precedent: a paste
  without it never learns the bar. New `QUESTION_VERSION = 1`, row `coai-question` after `coai-consultant`
  (it is about asking, as the consultant is), `file: 'coai-question-consultant.md'`. The `mounted` flag
  and `MOUNTABLE_HALVES` go: with every half a mounted rule there is one kind of row, and a mount is
  judged on all six — a mount from before the move is `older` on the missing file, i.e. a pin to move.
- **D5 — version dance in this repository.** `SNIPPET_VERSION` stays 5 (frozen). `FEATURE_VERSION` 2→3,
  `CONSULTANT_VERSION` 3→4, `QUESTION_VERSION` new at 1, `SNIPPET_BODY_SHA` recomputed,
  `ARTEFACT_VERSION` 14→15, `package.json` title "Copy the CLAUDE.md snippet (v15)". *(Shipped as 16: a minimal pin bump, PR #726, landed first with `CONSULTANT_VERSION` 4 and artefact 15, so this change took 16.)*
- **D6 — the product copy goes.** `src_vs_code/src/featureRule.md` is deleted; `prepare-gate.mjs` reads
  `FEATURE_SOURCE = '.agents/conventions/common/coai-feature-gate.md'` through `ruleBody` (frontmatter and
  leading `owns:` stripped) and adds `QUESTION_SOURCE`; both emitted like the consultant (own output
  modules). Tests that asserted "product-owned, no mount" are inverted: one source, the mount.
- **D7 — no new tooling, no enforcement.** Conventions hold guidance; nothing counts whether the
  consultants were asked.

## Build order

1. Conventions (worktree from `origin/main`): the two rule files, trigger 7 + v4, two entries in
   `tools/canonical-markers.test.mjs`, two hand entries in `research/rule-bodies.json` then
   `node tools/rule-bodies.mjs --update common.coai-feature-gate common.coai-question-consultant
   common.coai-consultant`, the README `reason` input of `promote-release`, a `research/module_tests.md`
   section. `npm test`, `npm run check`, `ownership-check`, `rule-bodies`, `plan-lifecycle`. Gate (plan,
   code), PR, CI, reviewer threads, merge; close PR #55 as superseded (its v1 text never shipped).
2. Promote: `promote-release` dispatched against `main` with the merged 40-hex sha and `-f reason=…`.
3. This repository (worktree from `origin/main`): pin `.agents/conventions` to the new release; D4–D6;
   tests: `featureSnippet.test.ts` (mounted, v3, artefact 15), `snippetVersion.test.ts` (six mounted
   halves), `snippetDiscovery.test.ts` / `prepareGate.test.mjs` fixtures, a new `questionSnippet.test.ts`
   (row, marker, the load-bearing sentences, mount judgement). `npm ci --ignore-scripts --prefix
   .agents/conventions`, then every extension and mcp test. Gate, PR, CI, threads, merge. The release
   PR release-please opens is NOT merged here: its notes go to the operator.
4. Pin cascade, each consumer its own worktree from `origin/main`, pin-check after the commit:
   mcp, sidecar_rust, benchmark, creds_for_devs (`.claude/rules/shared`), then rag_qln LAST with its two
   code pins in the same PR and `dotnet build` before the commit. Mechanical; no gate round.

## Test plan

- Conventions: `canonical-markers.test.mjs` RED when the marker is displaced (its existing companion
  case), GREEN with both new files; `rules.test.mjs` freeze green after `--update`; ownership-check OK.
- This repository: the hash guard in `snippetVersion.test.ts` names the new SHA and versions; the
  byte-identity test reads six mounted halves; `featureSnippet.test.ts` asserts the mount is the only
  source and the D23 sentences ("never a third", `good_enough`); `questionSnippet.test.ts` asserts the
  marker, `ask_consultants` before the person in every phase, `consultId`, the three kinds, and that a
  mounting repository with all six files is `current` while one missing the question file is `older`
  naming it; `prepareGate.test.mjs` refuses a missing/foreign feature or question source by name.
- **A bounded smoke run of the reads** (accepted from the conventions plan round, codex): the
  conventions' `tools/smoke-rules.mjs` with `--agent claude` and `--file` naming the new rules, read-only
  and budget-capped, its report recorded in the pull request. It proves the rules are selected and READ;
  no harness claims an agent obeys them — consultant-first is observed in the live session doing this
  work, which sends its own questions to `ask_consultants` before the person.

## Definition of Done

- [ ] Both rules on conventions `main`, promoted to `release`.
- [ ] The snippet hands out v16 built from six mounted halves; no product copy of any half remains.
- [ ] All extension and mcp suites green on the PR's head sha; reviewer threads resolved.
- [ ] Every consumer's pin at the new release, `pin-check` green; rag_qln's code pins current, its build run.
- [ ] Release notes drafted for the operator; the release PR left unmerged.
