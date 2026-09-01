# Code review through the gate — scope first, diff second (MANDATORY)

> This rule governs how the `coai` review gate is CALLED, in this repository and in every repository
> that adopts it. It is enforced by the server: `review_code` refuses a round with no scope.
> The instruction also travels in the snippet the extension hands to target repos
> (`src_vs_code/src/claudeSnippet.ts`) and in the `review_code` tool description
> (`src_mcp/src/Tools.cs`), so an AI that never reads this file still gets it.

## The rule

**Never hand a reviewer a bare diff.** Every code round carries two things:

1. **The scope** — what this change was supposed to achieve.
2. **The diff** — what actually changed.

## Why

A reviewer holding only a diff can answer one question: *is this code defensible?* It cannot answer
the question a gate exists for: *is this the change that was asked for?*

Those two come apart constantly, and the second is the expensive one. A change can be well written,
well tested, idiomatic, and solve the wrong problem — and a diff-only review will approve it,
because on its own terms there is nothing wrong with it. The same reviewer given the scope says
"this does X, and the scope asked for Y".

It is also the only way a reviewer can notice what is MISSING. A diff shows what is there; the scope
is what makes an absence visible — the case not handled, the test not written, the sentence in the
requirement nobody implemented.

## What a scope must contain

Not a commit subject. Not a ticket title. The three things a reviewer needs to judge against:

- **The symptom or the goal** — what was wrong, or what should become possible. State the observed
  behaviour, not the intended fix.
- **What must be true when it is done** — numbered, checkable statements. This is what the reviewer
  reads the diff against, so each one should be something a diff can satisfy or fail.
- **The constraints** — what must NOT change, what may not be added, which conventions bind. A
  reviewer cannot flag a violated constraint it was never told about.

A Definition of Done checklist at the end is worth its length: it converts the scope into the list
the reviewer walks.

## Reviewing an existing commit

The same shape, and the scope is written from the commit's INTENT rather than from its diff:

```
branch  = the commit (or a branch pointing at it)
baseRef = its parent
planText = what that commit was supposed to do
```

**Write the scope before reading the diff too closely.** A scope reverse-engineered from the diff
says exactly what the diff does, which makes the review circular — the reviewer is then handed the
answer key and asked to mark the paper. Take the intent from the issue, the plan, the commit
message's reasoning, or the conversation that produced it.

## What the reviewer is NOT given

The plan stage runs in an EMPTY directory: a plan reviewer that gets a repository goes exploring, and
eight minutes later it is still reading files instead of judging a document (measured, on a 15 KB
plan). The code stage gets one read-only worktree pinned to the branch's commit, because a diff
cannot be judged without the code around it — but the diff and the scope are both in the prompt, so
the worktree is for checking, not for discovery. The reviewer timeout bounds the rest.

## Never

- Do **not** call `review_code` with an empty or one-line `planText`. The server refuses it, and the
  refusal is not a formality — a round that ran without a scope answered a different question.
- Do **not** paste the diff into `planText`. That is the answer key, not the scope.
- Do **not** re-send the scope by hand in the normal flow: the plan round's text is kept with the
  session and reused. Send one only when you are reviewing something no plan round covered.

## Definition of Done

- [ ] Every code round carried a scope stating the goal, what must be true, and the constraints.
- [ ] A scope for an existing commit was written from its intent, not from its diff.
- [ ] The verdict, the reviewer count and the honest failures were reported — a round that ran with
      four of six reviewers says so.
