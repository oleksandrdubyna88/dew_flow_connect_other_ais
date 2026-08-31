# The first real run — 2026-08-31

> The record story 6.2 of [PLAN_epic_06_proof.md](PLAN_epic_06_proof.md) exists to produce:
> one session against the real vendors, on a throwaway repository, from a deliberately flawed plan.
> It found **eight defects that 141 green tests did not** — seven in delivery and one in the
> counting rule itself — and every one of them is now a test.

## What was under review

A scratch repo, `tokenbin`: a paste store with three flaws seeded on purpose —

1. **Security** — the paste token is stored in plaintext beside the body, and the id becomes a
   filename unchecked.
2. **A missing failure path** — `JsonDocument.Parse` on a corrupt file throws; `Save` is neither
   atomic nor crash-safe.
3. **Performance** — `Search` reads every file once per file: O(n²) for a linear question.

The plan was written to match: it describes the three methods and their happy paths, and says
nothing about failure, validation, or scale.

## What the run cost, and what it bought

Six attempts, because each one found something and stopped. In order:

| # | What broke | Where the defect was |
|---|---|---|
| 1 | `An error occurred invoking 'review_plan'` | **ours** — the role prompts shipped as files beside the executable, and the release asset carries only the executable. Tests never saw it: a project reference copies content into the test output. |
| 2 | `codex` could not be started | **ours** — npm's Windows shims are `codex.cmd`; `Process.Start` does not read `PATHEXT`, so it found npm's extensionless *shell script* and failed. |
| 3 | every reviewer failed, verdict `proceed` | **ours, and the worst of them** — the gate failed open. Codex was out of quota, Gemini refused an untrusted folder, nothing was reviewed, and the round passed. The e2e test had asserted that behaviour. |
| 4 | Gemini exit 55 | **ours** — a round's worktree is always a fresh directory, so never a trusted folder; without `--skip-trust` Gemini refuses headless *and* overrides plan mode away. |
| 5 | Codex `400 invalid_json_schema` | **ours** — OpenAI's structured outputs require every object to declare `additionalProperties: false` **and** list every property in `required`; optionality is a nullable type. `file` and `line` were merely absent. |
| 6 | reviewers answered "No implementation plan was provided" | **ours, and the quietest** — on Windows the CLIs are `.cmd` shims, so cmd.exe parses our argv and **truncates an argument at its first newline**. Every reviewer got the first line of its role prompt and nothing else, then answered politely. The round reported *all reviewers answered*. |

Plus one found by a test written along the way: a missing CLI threw through the fan-out and killed
the whole **round** rather than one reviewer (`ReviewerOutcome.NotStarted`).

**The pattern is worth naming.** Five of the six are failures of *delivery* — what reaches the
vendor, in what shape, from what packaging — and not one of them is expressible in the pure core
where most of the testing effort went. A suite of 141 tests over parsing, counting and rounds was
green through every single one.

## What the reviewer found, once it could actually read

Codex, plan stage, one reviewer, 38 seconds:

| Severity | What |
|---|---|
| blocking | paste ids are not constrained to safe filenames under the root |
| major | `Save` has no atomic-write or interrupted-write behaviour |
| major | `Search` is unauthenticated at the store boundary though described as admin-only |
| major | **`Search` matches whole JSON files, so it can match and return stored TOKENS** |
| major | `Read`/`Search` define no behaviour for missing, malformed or concurrently deleted files |
| minor | tokens stored in plaintext with no stated handling policy |
| minor | the plan's build order includes work it declares out of scope |

Two of the three seeded flaws, named precisely — and the token-exposure finding was **not** one of
them: the reviewer noticed that a substring search over the raw JSON reaches the secret, which is a
better finding than the one that was planted. The third seeded flaw is the O(n²) search, invisible
in a plan that never describes the loop; it belongs to the code stage.

## The code stage: all three seeded flaws, and one nobody planted

Codex, three roles, 76 seconds, `all 3 reviewers answered`:

| Severity | Role that found it | What |
|---|---|---|
| blocking | security | unvalidated paste ids allow reads and writes outside the root |
| major | security | `Search` returns each matching record's bearer token along with its body |
| major | architecture | `Search` exposes the persistence format instead of returning bodies |
| major | **performance** | **`Search` rescans and rereads the entire store once for every paste** |
| major | performance | quadratic directory scans and file reads |
| major | reliability | writing over the destination can leave truncated JSON after an interruption |
| major | reliability | a concurrent reader can observe partial JSON |
| minor | performance | **`Read` never disposes its `JsonDocument`** |

All three seeded flaws, and the quadratic scan was named independently by two different roles —
which is the panel working exactly as intended. The undisposed `JsonDocument` was nobody's plant:
it is a real leak that went into the scratch code by accident and came straight back out.

## And a defect in OUR OWN counting rule, found the same way

Two reviewers described the one path-traversal defect at `Store.cs:10` as *"Unvalidated paste IDs
can escape the configured storage root"* and *"Unvalidated paste IDs allow writes and reads outside
the configured root"*. Token similarity: **0.43** — under the 0.5 threshold, so they counted twice.
A gate whose count grows with the number of reviewers is precisely the failure de-duplication
exists to prevent, and it had been invisible while both vendors were scripted to return identical
text.

The fix is a **graduated** threshold: when file, line (±5) and category all match, those
coordinates already say the reviewers are looking at the same code for the same reason, so far less
wording overlap is required (0.25); with no file to anchor them, the strict 0.5 stands.

**Its limit is recorded rather than tuned away.** The two quadratic-scan findings share almost no
vocabulary (0.12), and the threshold that would merge them would also merge two genuinely different
remarks on one line (0.20). So that pair still counts twice, and the honest cure is a semantic
comparison — a change to make deliberately, not by moving a constant until a test passes.

## Observed, and NOT ours

- **Codex's shell cannot start on this Windows machine** — `error 1920` under both `read-only` and
  `workspace-write`, in a worktree and in an ordinary directory alike. So a reviewer here cannot
  explore the checkout. It does not have to: the prompt carries the plan and the shaped diff, which
  is exactly why the design assembles context server-side rather than telling a CLI to go look.
- **Gemini answers `503 UNAVAILABLE` under load** — "This model is currently experiencing high
  demand", transient by its own description. Now among the phrases that earn one retry.
- **Codex says "You've hit your usage limit"** — never "429", never "rate limit". A quota
  exhaustion was therefore misreported as a plain non-zero exit and never retried. Its real words
  are in the table now.

## The verdict on the exercise

The run did its job in the only way it could: by being real. Seven of the eight defects lived in the
seam between our code and someone else's — a release packaging step, a Windows shim, a vendor's
schema validator, a CLI's trust model, a quota message's wording. None of those could have been
found by testing our own logic harder, and all of them would have been found by the first user.

The eighth is the one worth remembering longest: the counting rule was wrong, and it was wrong in a
way that only real reviewers could expose, because a fake vendor returns the text you told it to.
The moment two models described one defect in their own words, the gate double-counted it. Scripted
tests cannot produce disagreement about wording — which is exactly what a panel of different
vendors is for.
