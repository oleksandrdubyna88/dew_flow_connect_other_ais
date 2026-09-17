# PLAN — A repository that MOUNTS the gate rule is told to paste it again

> Status: **plan only, nothing implemented.** Scope: `src_vs_code/src/claudeSnippet.ts` and the copy
> command in `src_vs_code/src/extension.ts`. No server change.
>
> Related docs: [module_extension.md](../research/module_extension.md),
> [PLAN_consultant.md](../research/PLAN_consultant.md) (story 5 built and then gave up a fix for
> this), [PLAN_shared_rules_adoption.md](PLAN_shared_rules_adoption.md),
> [PLAN_the_menu_names_the_clipboards_version.md](../research/PLAN_the_menu_names_the_clipboards_version.md)
> (shipped 2026-09-14 — the boundary is below).

## The boundary with the menu-version change (2026-09-14)

Half of the symptom below has already been fixed, and by a different plan, so this one is narrower
than it was. The division:

| Item | Who owns it |
|---|---|
| the NUMBER in the ⋯ menu, the note and the notification | [PLAN_the_menu_names_the_clipboards_version.md](../research/PLAN_the_menu_names_the_clipboards_version.md) — **done**: `ARTEFACT_VERSION`, v6 |
| the self-contradicting *“is v5; v5 is current”* sentence | that plan — **done**: the note names the halves that are missing or behind instead of inventing a number for the paste |
| **the ADVICE** — *“replace the old block”* said to a repository that has no block | **this plan**, still open |
| **what goes on the CLIPBOARD for a mount** — today it carries the gate half the submodule already provides | **this plan**, still open |
| recognising a mount at all (a `mounted` state, provenance, `isOurGateRule`) | **this plan**, still open |

So the sentence a mounting repository sees is now accurate about WHAT it is missing and still wrong
about what to DO. Nothing in the shipped change made this plan harder: it added no mount awareness and
took no decision about one, and the per-half list the note now carries is the raw material a
mount-aware sentence would use.

## The symptom

Six repositories in this family mount `common/coai-review-gate.md` from the `dew_flow_conventions`
submodule. Their gate rule is current by the submodule pin — nobody pasted it and nobody has to.

The panel tells them to paste it anyway.

`readSnippetStatus` searches `SNIPPET_LOCATIONS`, which deliberately includes the two mount paths
(`.agents/conventions/common/coai-review-gate.md`, `.claude/rules/shared/common/coai-review-gate.md`)
so that a mounting repository is not reported as `absent`. The mounted file carries the gate marker
and no other, so `snippetStatus` finds `coai-snippet v5`, looks for the document, caller and
consultant markers, finds none, and answers `older`. The note then says *"Copy it again from the ⋯
menu and replace the old block"* — and the ⋯ menu hands over **all four rules**, gate half included.

Following that advice puts a second copy of the gate rule into a repository whose submodule already
provides it. The two then drift on the next conventions commit, and the pasted one is the one the AI
reads, because `CLAUDE.md` outranks a mounted rule by design.

## Why it is open rather than fixed

Story 5 of [PLAN_consultant.md](../research/PLAN_consultant.md) built a fix: a `mounted` status, a
`fromMount` provenance flag carried on every answer, `isOurGateRule` recognising a mount by the
catalog id in its frontmatter, and `snippetFor` putting only the missing halves on the clipboard. A
gate reviewer raised the underlying problem as Blocking and two CodeRabbit rounds refined the fix.

It was given up on 2026-09-14, by the operator's decision, when epic 3 was rebased onto a `main`
that had since answered the same question differently and twice — a rule file per half, each with
its own marker and version, no mount awareness at all. Keeping story 5's machinery would have left
one half judged by a mount-aware state machine and three by version equality: two answers to one
question in one file, which `common/reuse-first.md` exists to stop.

So the duplication is **accepted for now and written down**, rather than carried as a second model.

## What a fix has to do

1. **Recognise a mount by what the catalog LOADS it by** — the `id:` in the rule file's frontmatter
   (`common.coai-review-gate`) — not by comparing its body with this build's. A repository whose
   submodule is pinned to an earlier commit legitimately carries an older revision; byte equality
   answers "not a mount" for exactly the repositories this is meant to help. (That was story 5's own
   first attempt and the gate caught it.)
2. **Answer a mounting repository differently from a stale paste.** The gate half is current by its
   pin; what is missing are the halves this product owns. "Replace the old block" is wrong twice
   over: there is no block, and pasting one duplicates the rule.
3. **Put only the missing halves on the clipboard** in that state, or the advice and the button
   disagree and somebody cuts a hundred lines by hand.
4. **Cover all four halves, not just the consultant one.** The document and caller rules have the
   same problem today and nobody has noticed, because the mount case was never reported separately.

## Test plan

| File | The guarantee it pins |
|---|---|
| `snippetVersion.test.ts` | a mounted rule with no pasted halves is NOT reported as a stale paste; a mount pinned to an older conventions commit is still a mount; a file at a mount path declaring another catalog id is not |
| `snippetVersion.test.ts` | the clipboard in that state carries the halves this product owns and NOT the gate rule |
| `snippetDiscovery.test.ts` | an instruction-file paste still outranks a current mount — a stale `CLAUDE.md` is what the AI actually reads |

## Definition of Done

- [ ] A mounting repository is never told to paste the gate rule it already mounts.
- [ ] The mount test is the catalog id plus the gate's own sentence, and an older pinned revision
      passes it.
- [ ] The clipboard agrees with the advice in every state the panel can report.
- [ ] `research/module_extension.md` describes the mounted state and why it is not `older`.

## What 2026-09-17 changed about this, and what it did not

[PLAN_a_finding_that_changes_everything_calls_the_consultant.md](../research/PLAN_a_finding_that_changes_everything_calls_the_consultant.md)
measured the delivery gap this plan is about and found it wider than recorded here: across every
checkout on the machine and the user profile there are **zero pasted copies of the artefact**. The
consultant half is mounted nowhere — it is this product's own file, not a conventions rule — and it
cannot be pasted into a family repository at all, because the shared adapter refuses a `CLAUDE.md`
that is anything but `@AGENTS.md`.

That plan worked AROUND the gap rather than closing it: the two rules it shipped also travel in the
`consult`, `review_plan` and `review_code` tool descriptions, which reach every session with no paste,
no mount and no pin cascade.

**This plan is untouched and still open.** The advice a mounting repository is given, what goes on its
clipboard, and recognising a mount at all are still unbuilt. The tool-description channel does not
answer any of them — it means a rule can reach a mounting repository, not that the panel stops telling
it to paste a gate rule its submodule already provides.
