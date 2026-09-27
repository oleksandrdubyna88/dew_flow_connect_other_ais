<!-- coai-feature v1 -->
## Reviewing the whole FEATURE before release (ConnectOtherAIs)

`mcp__coai__review_feature` is the gate's fourth stage. The plan round reviews a plan before it is
built and each code round reviews one epic's diff; by the time the last epic lands, every reviewer has
only ever seen a slice. This round sends reviewers the WHOLE feature — the plan, the epics, what you
learned the hard way, the gate's own history of this work and an outline of every file the plan
changed — so they can judge whether what shipped is what the plan asked for and whether the seams
between epics hold.

**When — once, at the very end, and only for a plan of THREE or more epics.** Call it after every epic
is built — their pull requests may already be merged — and before the release. Not per epic, and not
for a plan of one or two epics: that work is covered by `mcp__coai__review_code`, and the server
records a smaller plan as `skipped` rather than reviewing it.

**What to pass.** It needs no `open` — it keeps its own session, keyed by the plan.

- `repoPath` — this checkout's own top level (`git rev-parse --show-toplevel`).
- `planPath` — the plan document's path, relative to the repository. It is the session's identity:
  pass the same path every time for the same plan.
- `baseRef` — the commit BEFORE the first epic, so the range covers all of the work and nothing older.
- `head` — the branch or commit that holds all of it, `origin/main` included when the epics are merged.
- `epics` — a JSON list, one entry per epic: `title`, `summary`, and its `branch` and `pr` when you have
  them. The `branch` is what lets the gate find the history of an epic that was squash-merged.
- `lessons` — a JSON object with `pitfalls`, `blockers` and `findings`, each non-empty: what went wrong
  or nearly wrong and where, what blocked and how it resolved (or that it is still open), and what a
  reviewer of the whole feature must know — a seam between epics, a workaround, something deliberately
  left undone, the rejected gate finding you are least sure of. When an array genuinely has nothing,
  write "none" with the reason, never `[]` — the server refuses an empty one.

**What the verdicts mean.**

- `skipped` does NOT block the release. Nobody could review it — no vendor ticked for features, the
  gate switched off, or a plan under three epics. Tell the person the feature review did not run, and
  the reason the reply gives, then carry on.
- `revise` — resolve every finding, then land each fix as NEW pull requests, never by rewriting merged
  epics, and call `mcp__coai__review_feature` again with the new `head`.
- `proceed`, `good_enough` or `continue_anyway` — the feature gate is done; your summary says what you
  took and what you declined.
- `call_human` stops the release. Surface the open findings and call `mcp__coai__ask_human`; only the
  person's answer moves it on.
- A finished feature review is reopened only with `again: true` and a `head` that has moved.

**Resolving and re-orienting.** `mcp__coai__resolve` takes a decision for EVERY finding exactly as at
the other gates, and — because this session is keyed by the plan rather than the branch — it needs
`feature: <planPath>`; so do `mcp__coai__status` and `mcp__coai__ask_human`. Everything the review-gate
rule says about this being additional to your own review, about the `commands` a reply may carry and
about `call_human` applies here unchanged.
