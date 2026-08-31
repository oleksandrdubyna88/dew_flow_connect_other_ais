# The first real run — 2026-08-31

> The record story 6.2 of [PLAN_epic_06_proof.md](PLAN_epic_06_proof.md) exists to produce:
> one session against the real vendors, on a throwaway repository, from a deliberately flawed plan.
> It found **ten defects that 141 green tests did not** — seven in delivery, one in the
> counting rule itself, and two in the tests — and every one of them is now a test.

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


## Two more, found by CI rather than by a vendor

The win-x64 release job reported a concurrency overlap of **five** against a cap of three — a
number a semaphore of three cannot produce. So the measurement was wrong, not the scheduler: it
counted overlapping start/end ticks that child processes wrote into files, which on a loaded
two-core runner reports on clocks and process lifetimes rather than on slots held. The cap is now
recorded inside the scheduler where the slot is taken, and the test asserts that. Worth keeping
beside the rest: a test that measures the environment will eventually accuse the code.

And a tenth, from the same job: the tests steer the fake vendor through **process-wide**
environment variables, while xUnit runs collections in PARALLEL. Two classes that launched the fake
with argv verbs found `FAKECLI_MODE=vendor` set underneath them by a class running beside them, so
the fake answered something nobody asked for and a full-loop test got an error where a verdict
belonged. It passed locally every single time — the interleaving needs the timing of a loaded
two-core runner. Every class that launches the fake now shares one non-parallel collection, and the
fake runs as its apphost rather than through a second `dotnet` host, which is what the job had been
listing as orphaned processes.

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

## The second sitting — 2026-08-31, evening, with the operator at the keyboard

Driven through a real Claude Code session against the marketplace-published build, on a branch
carrying a deliberately flawed `TokenGate`. Three more defects, each of a different kind.

**Eleventh — the plan stage was handing agentic CLIs a checkout.** A plan round sat at ten minutes;
the evidence was two live node processes and the round's worktree on disk. Given a tree and a plan
that mentions files, codex goes and READS them — the role is to judge the document. The plan stage
now runs each reviewer in an empty scratch directory, and a test holds that it creates no worktree
and points nobody at one. Cost, stated: a plan reviewer can no longer verify a `file.cs:line`
reference is real.

**Twelfth — gemini never answered, and the timeout was hiding WHY.** Every gemini reviewer in every
round failed as "timeout" at exactly the reviewer budget. Isolated by running the CLI by hand: with
`--skip-trust` it hangs on ANY headless call on this machine, because `~/.gemini/settings.json`
selects `gemini-api-key` auth and `GEMINI_API_KEY` is not in the environment — it waits on
authentication before the model is ever reached. The operator's evidence ("flash chews 2000-line
files in under a minute in the web") was correct and decisive: the model was never the slow part.
Fix on the machine: sign the CLI in interactively, or put the key in the vault entry. Fix owed in
the product: `providers` should probe with a tiny generation, not `--version`, which needs no
auth and so reports a hung vendor as healthy.

**Thirteenth — the protocol had no way to hear the human say "proceed".** Rounds exhausted, verdict
`call_human`, the person decides to go — and `review_code` kept refusing forever, because
nothing could ever set `PlanProceeded`. Found by actually sitting at the gate, not by any of the
195 tests. `resolve` now takes `humanDecision: "proceed"`, honoured ONLY after exhaustion with a
refusal before it (a model must not skip the loop by claiming permission it was never given), and
the tool description says: never pass it on your own judgement.

**And what the round itself was worth:** codex, three times over the same fictional plan, produced
findings a human reviewer would be proud of — "a response merely containing the word valid is not
an answer", "the 15-minute lease contradicts the revocation promise", "the fake-client tests cannot
verify the HTTP behaviours they claim to". Each revision's findings were sharper than the last,
which is the loop doing exactly what it was built to do.

## The third sitting — 2026-08-31, night: the WSL run, and four defects it alone could find

The operator asked for two things — tokens and money per round, and a rounds view that does not ask
to be saved — and then for the whole cycle to be proved **inside WSL**, tests and a real review
both. Running it on a second platform was not ceremony: three of the four defects below are
invisible on Windows.

**Fourteenth — every prompt this product has ever sent began with a byte-order mark.**
`StandardInputEncoding = Encoding.UTF8` carries a preamble, and .NET flushes it into the child from
*inside* `Process.Start()`. Two consequences: three stray bytes in front of every review prompt, and
— when the child had already exited, which git does constantly — a `Broken pipe` thrown from Start
itself, taking the whole launch down instead of returning a result. On Windows it was a one-in-many
flake with no name; in WSL it failed **five tests at once, all in `Process.Start`**, which is what
made it findable. Measured before the fix, from the child's raw stdin:
`EF BB BF 23 23 20 54 68 65 …`. The fix is a BOM-less `UTF8Encoding`, and the test now records
undecoded bytes — its first version read the prompt back through the fake CLI's `Console.In`, which
strips a BOM while decoding, so it passed against the broken launcher and proved nothing. A decoder
cannot be the witness to a question about bytes.

**Fifteenth — a child that exits before reading its input crashed the launcher.** Related but
separate: even with no preamble, writing a megabyte-long prompt to a process that has already
exited throws. That exception left `ProcessLauncher` instead of a `ProcessResult`, so one CLI
exiting early failed the whole ROUND rather than one reviewer with a named outcome. Guarded now,
and nothing is hidden — exit code, stdout and stderr still come back.

**Sixteenth — `resolve` did not work without the human override.** The ordinary call of every single
round — record decisions, no override — came back as `An error occurred invoking 'resolve'`, because
`humanDecision` had no default value and the SDK therefore published it as REQUIRED. The second
sitting's live run had missed it by always passing `humanDecision: "proceed"`, which is the one call
that does not need to work. A contract test over real stdio now holds it.

**Seventeenth — the human override was checked against the wrong thing.** Reported by codex in the
code gate it was itself the subject of: the guard asked whether rounds remained, and an exhausted
*Escalate* stage has none either — so the flag could skip a configured ladder. It is now a
`HumanGate` flag set only by a `call_human` verdict and cleared by the resolve that used it. The
refusal also became narrower on purpose: a redundant override, where the gate had already decided to
advance, is ignored rather than refused, because refusing would have discarded a legitimate round's
decisions.

**What the WSL run proved, positively.** 215 tests green on Windows, 214 green in WSL across three
consecutive runs; and a real plan round driven over stdio against a linux `coai-mcp` with a live
`claude` reviewer: `call_human`, 4 gating findings of 7, **34 146 in / 7 425 out tokens, $0.3033** —
the money read out of the vendor's own envelope, not a price table of ours — with the round visible
on disk as `running` and `0 of 1 answered, 1 running` for the ninety seconds it took. The reviewer's
own best line, about a plan for caching a probe: *"Plan never confirms the cache's host process is
long-lived, and the specified test can't detect the case where it isn't."*
