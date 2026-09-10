# module: runners — spawn, isolate, survive

> `src_mcp/runners/CoaiMcp.Runners` — the impure ring around [module_core.md](module_core.md):
> processes, worktrees, the scheduler. It carries data to and from the core and adds no rules.

## Purpose

Turn "run six reviewers" into something that survives a moving checkout, a killed session, a hung
CLI and a rate limit — with every failure mode named.

## Flow

```mermaid
sequenceDiagram
  participant S as server (epic 04)
  participant W as WorktreeManager
  participant C as ContextAssembler
  participant B as BoundedScheduler
  participant E as ReviewerExecutor
  participant V as vendor CLI
  S->>W: ResolveShaAsync(branch) → AddAsync(sha) [one lease per ROUND]
  S->>C: CollectAsync(base, sha)
  C->>C: merge-base(base, sha) → the COMMIT this round is a diff of
  alt no common ancestor found
    C->>C: is-shallow-repository? — a truncated clone, or genuinely unrelated
    C->>C: rev-parse the base to a commit — pinned, so three git calls read ONE snapshot
  end
  C-->>S: CollectedDiff(files, comparedAgainst, kind) — numstat, per-file diffs, exclusions
  S->>B: RunAllAsync(work[], executor)
  B->>E: per job, under global(3) + per-provider(2) semaphores
  E->>V: launch (read-only, ephemeral); timeout kills the TREE
  V-->>E: -o file (codex) / stdout envelope (gemini) / NDJSON result (antigravity)
  E-->>B: Ok | NonZeroExit | TimedOut | Unparseable | RateLimited
  B-->>S: outcomes → ReviewerSummaryFactory → "N of M answered"
  S->>W: lease.DisposeAsync() — the finally
```

## Core entities

| Type | File | Role |
|---|---|---|
| `IProcessLauncher` / `ProcessLauncher` | `Processes/ProcessLauncher.cs` | the ONE process seam; timeout kills the entire tree; `StdIn` carries every long or multi-line input, BOM-less UTF-8, and a child that exits before reading is not an exception |
| `ExecutableResolver` | `Processes/ExecutableResolver.cs` | npm Windows shims: the PATHEXT resolution `Process.Start` does not do |
| `WorktreeManager`, `WorktreeLease` | `Worktrees/WorktreeManager.cs` | one detached tree per round, `coai-wt-` prefix under OUR storage; prune-on-open; disposal = finally; never touches a human's worktree |
| `SubmodulePopulator` | `Worktrees/SubmodulePopulator.cs` | fills the round tree's submodules from the PARENT checkout, not the remote — git populates none in a linked worktree, and in this family the project's rules ARE one; offline, pinned, never fatal, and refused when the source is reached through a reparse point |
| `GitModules`, `SubmoduleMount` | `Git/GitModules.cs` | `.gitmodules` as (name, path); a file inside the repository under review, so absolute/traversing paths and names that could spell another config key drop the mount |
| `DiffExclusions`, `ContextAssembler`, `CollectedDiff`, `DiffBase` | `Context/ContextAssembler.cs` | numstat → per-file diffs with `:(exclude,glob)` pathspecs; binary sizes via `cat-file -s`; every one of the three taken against the MERGE BASE, which the result names |
| `IReviewerRuntime`: `CodexRuntime`, `DeepseekRuntime`, `GeminiRuntime`, `ClaudeRuntime`, `AntigravityRuntime`, `CustomCodexRuntime` | `Reviewers/ReviewerRuntime.cs`, `ClaudeRuntime.cs`, `CustomRuntime.cs` | THE vendor adapter: `Build` (argv, pure) + `ReadAnswer` + `ReadUsage`, the last two with working defaults. Flags verified against codex 0.147.0 / gemini 0.55.1 / claude 2.1.197 / agy 1.1.22; keys ride env, never argv; DeepSeek = Codex config-shifted |
| `ReviewerRuntimeSelector` | same | unknown provider refuses naming the catalog |
| `ReviewerOutcome` (closed), `ReviewerExecutor`, `RateLimit`, `ReviewerLaunch` | `Reviewers/ReviewerExecutor.cs` | one launch + one repair **inside ONE deadline** (2026-09-08: the repair used to carry a whole second budget, so a reviewer could take twice its setting — measured at 668.8 s against ten minutes, reporting `ok`); SIX named outcomes incl. NotStarted; `Ok` carries the run's `Usage`, both launches counted when repaired. **`LaunchAsync` is the public half**: launch → classify → the vendor's RAW answer + usage, with the parse left to the caller |
| `Usage`, `UsageParser` | `core/Findings/UsageParser.cs` | schema-less scan over any vendor envelope; MAX per key name then sum per category, so a streamed cumulative total is never summed with itself; money only when the vendor priced the run |
| `BoundedScheduler`, `ReviewerWork`, `ReviewerSummaryFactory` | `Reviewers/BoundedScheduler.cs` | global + per-provider semaphores; a rate limit climbs the ladder below |
| `VendorIdentity`, `RuntimeResolution` | `Reviewers/RuntimeResolution.cs` | the ONE answer to "what is this vendor": which runtime it drives, the adapter for it, and how it authenticates — asked by both binaries, after two incidents where a second copy of it was the one that was wrong |
| `VendorHealth`, `VendorProbe` | `Reviewers/VendorProbe.cs` | the `--version` health probe behind `providers` and the Team server's catalog; a retired runtime is answered BEFORE the probe, a local engine instead of it, and a CLI that never answers says so rather than reporting the kill's exit code |
| `RemoteRuntime` | `Reviewers/RemoteRuntime.cs` | the adapter for a vendor whose reviews run on a **Team server**: builds `--ask-remote` against THIS binary (the `LocalRuntime` shim shape), carries no `SharedResource` because the server owns the queue, and holds the cancel-an-abandoned-job half (`Claim` / `ReadClaim` / `CancelAbandonedAsync`) |
| `RemoteAsk`, `RemotePoll`, `RemoteState` | `Reviewers/RemoteAsk.cs` | the PURE half of the shim: the request body, the poll parse, every exit code and every sentence a person will see — so each failure path is tested without a server |
| `TeamServerAuth` | `Reviewers/TeamServerAuth.cs` | one canonical spelling of a server URL, the fingerprint that names its token file, and the owner-only read/write of that file. The same normalised value builds the request URLs, not just the hash |
| `RemoteProbe`, `RemoteCatalog` | `Reviewers/RemoteProbe.cs` | a remote vendor's health over `GET /api/catalog`: the server's own slot counts as the note, 401/403/426/outage told apart, a cache whose FAILURE wait is never shorter than its success wait, and a refusal that says WHICH of a row's two names was tried |
| `UsageLedger`, `UsageEntry`, `UsageKinds`, `LedgerJsonContext` | `Reviewers/UsageLedger.cs` | one JSON line per reviewer run, unindented because the reader is line-based; moved here so the server appends the same shape. Since 2026-09-09 a line also carries its **`kind`** — `review` or `chat` — because "what did the gate cost me" and "what did asking cost me" are two questions one total answers neither of. The field is trailing and defaulted, so every line already on disk stays valid and an absent kind is READ as a review, which is what all of them were. `UsageKinds` holds the two names here rather than only in the server's own `JobKind`, which this library cannot see: the dependency runs server → mcp and not back, and two vocabularies for one field is how a writer and a reader come to disagree. A test in `src_server` asserts the enum spells exactly these |
| `RetryLadder` | `Reviewers/RetryLadder.cs` | the waits and when to stop: four steps, jittered, bounded by the reviewer's own deadline; pure, so the jitter is a table rather than a stopwatch |

## The decisions a reader needs

- **Neutral shared instructions are part of the review sample.** `RuleFiles` includes
  `.agents/PROJECT.md`, nested `.agents/rules` and the common/C#/Rust/TypeScript directories
  of `.agents/conventions`. Mount research, tools and its own PROJECT/ENTRY are excluded.
  Project/local sources precede shared bodies under the existing whole-file budget and
  shuffle policy. A declared neutral mount without canonical rule bodies is reported in
  `MissingMounts`, even if a `.git` marker exists. Claude/Cursor legacy folders remain supported.
  This collector samples review context; applicability belongs to the shared Node resolver.

- **One worktree per round, by SHA** — six read-only reviewers share one tree; six checkouts of a
  moving branch would be six different inputs to one comparison.
- **A worktree's submodules come from the parent checkout, never from their remote.** `git worktree
  add` populates none (and has no `--recurse-submodules` in git 2.55), so the project's rules were
  simply absent from every round in a repository that keeps them in one. The parent's object store
  already holds every commit the reviewed SHA pins, so cloning from it is offline and faster —
  measured 1.49 s against 2.45 s — and the source is refused unless it resolves inside the parent
  with no reparse point on the way, because `protocol.file.allow` is lifted for that call.
- **Populating is never fatal, and never silent.** A parent that never initialised the submodule
  leaves the mount empty and the round runs; `RuleBundle.MissingMounts` is what tells the reviewer,
  in the prompt, that rules it was not shown exist. Failing the round instead would turn an
  infrastructure hiccup into an outage — a code-round finding that was rejected on exactly that
  ground, with the visibility half accepted.
- **The launch and the parse are two things, and the seam between them is public.**
  `LaunchAsync` answers what the PROCESS said — the terminal outcome when there is one, otherwise the
  vendor's raw answer, its usage, and the transcript as evidence when the envelope came back empty.
  `RunOnceAsync` is that plus `ParseAnswer`, which is pure and takes exactly what it reads: the text
  and the provider that stamps each finding's origin. The seam exists because a second binary needs
  the first half without the second — the planned Team server runs the same CLIs through the same
  adapters and hands the raw answer back over HTTP, while parsing, the repair launch and
  de-duplication stay with the client that asked. A copy of this classification over there is how the
  vendor-set drift in this repository happened twice.
  **`Terminal == null` means the process ran and exited zero — never that there is an answer**: an
  adapter whose output file never appeared says so with a null answer, and what that means is the
  caller's judgement. An EMPTY answer takes the same path as a missing one, so an envelope that came
  back blank still leaves its transcript as evidence.
- **"What is this vendor" is answered once, in the library.** `RuntimeResolution.NameOf` / `.For` /
  `.AuthOf` take a `VendorIdentity` — three strings, because three is what the answers read — and
  every caller asks them rather than repeating the arms. The order inside is the load-bearing part:
  `local` before the base-URL arm (a local vendor IS a vendor with a base url), an explicit runtime
  before the id (`my-claude` is a claude, not a codex). Both of those orders are fixes for defects
  that shipped, and the reason this lives here rather than in one binary is the third: the question
  had two copies twice, and the copy that was missed was the one that was wrong — a local reviewer
  silently became a codex one, and then was dropped from every round while the panel showed it as
  configured.
- **Provider cap beside the global cap** — a rate limit is per vendor; a global cap alone puts all
  its slots on one provider.
- **A rate limit gets a LADDER, not one retry** — 5 s, 30 s, 60 s, 120 s (`COAI_RETRY_BACKOFF`),
  each spread by up to a fifth either way. One interval was the wrong single number for the two
  things it had to serve: a plain `429`/`503 high demand` clears in seconds, while a usage window
  does not clear inside a round at all. The jitter is not decoration — a code round launches nine
  reviewers at once, so nine meet the limit in the same instant and a fixed interval would send them
  all back into it in the same instant too.
  Three things end the climb: the ladder is spent, the limit is **hopeless** (a daily allowance —
  still not retried at all, ladder or no ladder), or the next wait would outrun the reviewer's own
  deadline. That last budget counts WALL CLOCK from the first launch, not the sum of the waits: the
  failed launches are most of the time spent, and 5 s + 30 s of waiting fits a 60-second deadline
  that the two attempts producing them have already blown.
  **`COAI_RATE_LIMIT_BACKOFF_SECONDS` still means what it meant** — set alone, it is a one-step
  ladder at that number. A deployment that deliberately chose 45 seconds must not silently become
  four retries, and nothing would have said so; an unreadable `COAI_RETRY_BACKOFF` falls back and is
  reported through `PanelSettings.Unrecognised` rather than half-applied.
  The round's summary carries the attempts it actually took (`RateLimited.Attempts`), because
  "after one retry" was a true sentence with one step and a confident wrong number with four.
  **A retry gets what is LEFT of the deadline**, never the whole of it again: a first launch that
  spends nine minutes of a ten-minute budget and comes back rate limited would otherwise be followed
  by a second carrying ten more, and a reviewer would run for nineteen minutes against a deadline of
  ten. And the wait is **said out loud** — the ladder can hold a reviewer for over three minutes,
  where "running" reads exactly like a model that is thinking, so each wait reports the attempt and
  how long it will be. Both found by codex reviewing this very change.
- **Antigravity (`agy`) is the closest fit to this product's contract**: `--json-schema` puts the
  finding schema straight into `result.response`, the same envelope carries `usage`, and the model
  ids carry their own reasoning effort. Its prompt rides `--input-format stream-json` on stdin as
  one NDJSON line (`{"event":"user","message":{"role":"user","content":"..."}}`) because `--print`
  takes its prompt as a flag VALUE and a review prompt is ~33 KB — past the Windows command line.
  It exists because Google retired Gemini Code Assist for individuals mid-2026-08-31: the Gemini
  CLI now fails in `_doSetupUser` with "migrate to the Antigravity suite", before any model is
  reached, which was mistaken in turn for a quota, a timeout and an untrusted folder.

- **Codex reads from `-o`, Gemini from stdout, Claude from its JSON envelope's `result`** — each
  through its OWN adapter, because where an answer lands is vendor knowledge; exit-0-with-no-output
  is `Unparseable`, never `Ok`.
- **Adding a vendor is one class, not an edit to the executor** — `IReviewerRuntime` carries launch,
  answer and usage; the executor asks the adapter and knows no vendor's name.
- **Tokens come from the vendor, money only when the vendor prices it** — claude reports
  `total_cost_usd`, codex, gemini and antigravity report tokens alone. A price table of our own would be wrong
  within a month, and a wrong number is worse than an absent one, so `costUsd` stays null and the
  UI says "no cost reported" rather than "$0.00".
- **Subset counts are never added** — codex's `cached_input_tokens` and `reasoning_output_tokens`
  are inside the totals beside them (measured: 14149 in / 9984 cached for one call), while claude's
  `cache_creation_input_tokens` and `cache_read_input_tokens` are additional and billed.
- **The fake CLI** (`src_mcp/tests_fakecli`) is the whole vendor surface in tests: emit / emit-to /
  stderr-exit / sleep / busy (start/end ticks for overlap measurement) / flip (first-launch
  failure), with launch counting. CI never touches a vendor.

## What a code round is a diff OF (2026-09-10)

The merge base of the branch and the base ref — not the tip of the base ref. `ContextAssembler`
resolves it once with `git merge-base` and uses it for all three git calls, and `CollectedDiff` carries
it out so the round can say which commit it compared against.

**Why, measured.** On 2026-09-08 a code round produced three Blocking findings from two different
vendors saying the branch had deleted `chatPrompt.ts`, `processLauncher.ts` and `sessionKey.ts` and
reverted the extension a version. None of it was true: the branch had been cut from `origin/main`, and
while the work was in progress another session merged its own pull request. `A..B` diffs two
ENDPOINTS, so the commits the base has and the branch does not arrive INVERTED — as deletions the
branch performed. On that branch at that moment, `origin/main..HEAD` was 17 files and 1616 deletions
where `origin/main...HEAD` was 9 files and 12. It happened three times before it was fixed, and it
gets likelier the busier the repository is: several agents work here at once, so a base that moves
during a review is the normal case rather than the exception. A reviewer cannot tell a phantom
deletion from a real one — the diff says the line was removed — and whether the change matches its
SCOPE is the one question this gate exists to ask.

**Why an explicit merge base rather than `A...B`.** The three-dot form is the same diff, but it leaves
the resolved commit inside git where nothing can name it, and there is no three-dot form of
`cat-file -s`, which is how a BINARY's old side is sized — that third call site would have gone on
reading a blob from somebody else's commit. One resolution answers all three and gives the round its
audit line.

**What happens when there is no merge base.** The two-dot form, as before, and a sentence saying so:
a review taken against the wrong base is worth more than no review, provided nobody has to guess which
one they are reading. Two cases, deliberately told apart — histories that genuinely share no ancestor,
and a checkout that is SHALLOW, where an ancestor exists but has not been fetched. The second is a
clone somebody can deepen; the first is a fact about the commits. `git merge-base` cannot distinguish
them, so `rev-parse --is-shallow-repository` is asked on the failing path only.

**The resolved commit is named to the reviewer too**, in the diff's own header. Two reviewers of the
plan asked for it independently and the reason is theirs: a reviewer that checks the branch against
the tip of `main` sees a diff that does not match, and that discrepancy is indistinguishable from the
phantom deletions this change exists to stop.

## Two delivery rules the first real run wrote

Both cost a whole run each; see [RESULTS_first_real_run.md](RESULTS_first_real_run.md).

- **No argument may ever contain a newline.** On Windows the vendor CLIs are npm `.cmd` shims, so
  cmd.exe parses our argv and truncates an argument at its first newline — silently, so the model
  answers as if it had been handed nothing. The prompt therefore travels on **stdin**: `codex … -`
  (its documented "instructions from stdin"), and a one-line `-p` pointer for gemini, which appends
  `-p` to stdin. A test asserts the rule for all three runtimes.
- **A bare command name is not startable.** `Process.Start` does not read `PATHEXT`, so it finds
  npm's extensionless shell script and fails. `ExecutableResolver` tries the executable extensions
  first and the bare name last, and never rewrites an explicit path.

## External dependencies

`git` on PATH (worktrees, diffs); the vendor CLIs only at real runtime, never in tests.

## Token accounting is per vendor, because a shared rule is wrong for one of them

| Vendor | Cache tokens | Reads |
|---|---|---|
| codex | `cached_input_tokens` is a SUBSET of `input_tokens` — adding it double-bills | the generic scan over `--json` events |
| claude | `cache_creation_input_tokens` / `cache_read_input_tokens` sit BESIDE the input count and are both billed | `modelUsage`, not `usage` — see below |
| antigravity | `thinking_tokens` inside `output_tokens`, `cache_read_tokens` inside `input_tokens`; its own `total_tokens` = in + out proves it | `result.usage` on the stream's result event |

Claude reports the SAME run twice and the two disagree: measured on a real call, `usage` said 10
input / 44 output while `modelUsage` said 532 / 57. `usage` is the last message's usage;
`modelUsage` is the aggregate across every turn, which is what a multi-turn review actually
consumed. Five tests pin all of this to envelopes captured from real calls.

## Evidence is kept, never summarised away

Three failures cost hours because the reason was discarded at the last step:

- a non-zero exit reported as `exit 1` while the executor held the stderr → the reason travels with
  the code now, chosen by CONTENT (the first line that announces an error), because "the last line"
  picks node's version banner on exactly the vendor CLIs this product drives;
- a rate limit reported without saying WHICH limit → a daily quota and a per-minute throttle read
  identically and only one is worth retrying, so the vendor's words travel and a hopeless limit
  skips its retry;
- an unparseable answer whose text was thrown away → kept under `<dataDir>/unparseable/` now, with
  the outcome naming the file. The one replayed by hand afterwards succeeded, which is precisely
  the case where the raw text is the whole story.

### A reviewer that found NOTHING is evidence too (2026-09-08)

The fifth, and the one that hid behind a success. A review that parses to an empty findings array is
an `ok` outcome by every measure a round can take — its tokens counted, its seconds recorded — and
the raw text was dropped, because only a PARSE failure reached `unparseable/`.

**Measured, 16:12 UTC.** Eight remote reviewers answered `{"findings": []}` on a diff the local
reviewer found eleven things in:

| reviewer | input | output | seconds | findings |
|---|---|---|---|---|
| codex × 4 | 33.4k–33.6k | 44–94 | 4.5–24.8 | **0** |
| gemini × 4 | 34k–42k | 38–1033 | 8.7–10.5 | **0** |
| local × 4 | 21.6k | 600–911 | 21–45 | 3 / 2 / 3 / 3 |

Dateable and not the build: across every code round since 2026-09-01 codex had produced an empty
answer **once in ~400 runs**, four of them are in that one round, and seven minutes later the same
process gave it 62.5k input and 2289–2533 output over 51–66 seconds on another branch. Whether those
eight had SEEN the change could only be guessed at by subtracting a rules byte count from a token
total, and why they said nothing could not be asked at all.

So:

- **A zero-finding review keeps its answer**, under `<dataDir>/empty/` — its own directory, because
  "it said nothing" and "it said something I could not read" are different questions and a person
  chasing one must not wade through the other. A review WITH findings keeps nothing; a directory
  that also collected the healthy case would be a directory whose name lies.
- **The outcome carries the path** (`ReviewerOutcome.Ok.Evidence`) and the reviewer's own audit line
  names it. Not the round's reply: every clean round would carry that sentence, and a sentence on
  every clean round is one nobody reads on the round that matters.
- **The round says what it SENT**: `context for review: diff N bytes over M file(s), E elided; plan
  N bytes; rules N bytes` — every number already computed and previously thrown away. `elided`
  earns its place because a partial view is exactly the state in which a reviewer's silence means
  nothing.
- **And what each reviewer RECEIVED.** The opening line carries every reviewer's prompt size, because
  the context is assembled once and composed per reviewer: anything between the two leaves a round
  log confidently naming a diff nobody was sent.
- The file is written to a sibling and renamed. `File.WriteAllText` truncates first and writes
  second, so a killed process leaves a file that exists and holds nothing — which, in THIS
  directory, reads exactly like a vendor that answered with nothing.

Neither directory has a retention policy yet; `unparseable/` has not had one either. That is its own
change, and it should cover both with one rule.

### A progress note is never a reason (2026-09-07)

The fourth of those, and the one that shows the limit of "choose by content". The remote shim writes
its PROGRESS and its VERDICT to one stderr, so a failed Team-server reviewer was reported as

```
exit 70: [coai-mcp] claude: running on the Team server at https://coai.remsoft.dev
```

— described as RUNNING in the sentence announcing that it had stopped. The verdict was directly
underneath, discarded: it announced nothing the vocabulary knew, so the picker fell back to the first
line that was not scaffolding, and a progress note is the first line there is.

**The fix is not a bigger vocabulary.** Teaching it the word `failed` was tried and broken by three
reviewers on the same round: `0 failed, 3 passed` and `3 tests failed` are tallies that announce
nothing, while `Step 3 failed` and `Job 12 failed` are real verdicts with a digit in front — no rule
over that one word can have it both ways, and each variant traded one wrong answer for another.

So the question changed from "does this announce a failure" to "is this one of OUR progress notes".
There are exactly two, `RemoteAsk.IsProgress` owns both sentences beside the verdicts they compete
with (`AskRemote` used to build them inline, which is how the recognition would have drifted), and
`IsScaffolding` excludes them. The announcement vocabulary is untouched.

### The same thing, one shim over — and a reason is never half a line (2026-09-08)

The LOCAL shim had the identical defect, and nobody looked for it there because the sentence it
produced was worse rather than merely wrong:

```
local/Conventions FAILED after 290.0s: exit 69: l Qwen3.5-35B-A3B-Q5_vk128:latest, pid 52068)
```

It begins mid-word. Nothing crashed: the reviewer waited its whole five-minute deadline for the local
engine and never got the card — three sibling local reviewers in the same round answered in 1.9 min,
34 s and 35 s. Exit 69 is `EX_UNAVAILABLE`, which the shim writes for exactly that, and
`LocalAsk.QueuedOutMessage` says what to do about it: more time, fewer local roles per round, or a
second engine. The cure was written, printed, and lost to forty-five characters of an unrelated line.

**Three links, and the third is what made it a fragment.** The shim writes its waiting note to the
same stderr as its verdict; `ReviewerExecutor` kept the last 400 CHARACTERS, so the cut landed
wherever it landed; and `Because` takes the first line that is not scaffolding — which was now half
of a progress note, recognised as neither progress nor a runtime frame.

The local note is `LocalAsk.WaitingMessage` now, beside `LocalAsk.IsProgress`, exactly as the remote
pair are — `Program.cs` composed it inline, which is why there was nothing to name in the excluded
set. And the tail is built out of **whole lines**: `ReviewerExecutor.TailOf` keeps as many complete
trailing lines as the budget holds and never half of one.

**Whole lines by construction, not by a flag.** The plan proposed telling `Because` that the tail had
been truncated so it could drop the first line, and all three plan reviewers found the same two holes
in that from three directions: a 400-character cut can land exactly on a newline, and dropping the
first line then discards a complete diagnostic; and a stderr with no newline inside the budget has no
first line to drop, so dropping it reports a real failure as blank. Trimming where the whole stderr
is in hand costs the picker no new parameter and leaves it no case to get wrong. The one thing it
cannot do is fit a line longer than the entire budget — that line's OPENING is kept, which is the
half `Because` shows anyway. (Measured: `QueuedOutMessage` is 340–359 characters against the 400
budget, so it fits today and an endpoint longer than about 85 characters would not.)

One more fallback moved. `Because` ended at "the last line" when every line was scaffolding, and for
a reviewer killed while it was still queuing that is a progress note again — the same defect wearing
the other shoe. A tail carrying *any* progress note now says *it was still waiting for its engine
when it stopped*; a tail that is nothing but stack frames keeps the old behaviour, because its last
line is at least something a person can search for. (It was "every line is a progress note" first,
which sent a reviewer that waited and then crashed straight back to quoting a stack frame.)

**The tag is a fact about the stream, so it stopped being a private constant.** `ShimNotes` holds the
name this program calls itself, the `[coai-mcp] ` prefix `Note` puts in front of every line it
writes, and the two questions that prefix answers: *did we write this line*, and *what is the
sentence without the tag*. It was `private const string AppName` inside the shim's own `Program`,
which the code that READS the stream cannot reach — and both halves of the code round's real findings
needed it:

- **A progress recogniser must be ANCHORED, not searched.** `LocalAsk.IsProgress` matched the stem
  anywhere in the line, so a wrapper reporting `error: waiting for the local engine at …: connection
  refused` would have been classified as progress and hidden — the picker suppressing the only line
  that said anything. Three findings, two vendors. It is anchored to the tag now.
- **A reason is capped at 160 characters, and that cap is for a VENDOR's line.** Ours are written to
  be read and are already bounded by the 400-character tail. Cutting them cost the whole point of
  one: measured on the code round, a queued-out local reviewer reported
  `…so its question was never asked. One caller uses t.` — 160 characters of a 340-character verdict,
  ending mid-word, with every cure it names past the cut. The plan's own Definition of Done said
  *reports `QueuedOutMessage`, whole*, and until this it did not.

`StdErrTail` stays at 400. Measured, `QueuedOutMessage` is 340–359 characters, so it fits and an
endpoint longer than about 85 characters would not — a capacity question, recorded rather than
solved, because raising the budget puts more vendor noise into every other failure.

Design record:
[PLAN_a_progress_note_is_never_a_reason_the_local_half.md](PLAN_a_progress_note_is_never_a_reason_the_local_half.md).

### The Gemini retirement (2026-09-01)

Google closed Gemini Code Assist for individual accounts. The CLI now fails inside `_doSetupUser`,
BEFORE it reaches a model, so every symptom it produces belongs to something else — three
observers read the same failure as a daily quota, a timeout and an untrusted directory.

`AntigravityRuntime` shipped on 2026-08-31 and **nothing used it for a day**: no preset offered it,
every default still named `gemini`, and a reviewer list saved before the retirement went on naming
the closed door. Supporting a vendor and DEFAULTING to it are different changes, and only the first
had been made. What changed on 2026-09-01:

| Where | Was | Is |
|---|---|---|
| `PanelSettings.Providers` | codex, gemini, deepseek(off) | codex, **antigravity**, deepseek(off) |
| `COAI_PROVIDERS` fallback | `codex, gemini` | `codex, antigravity` |
| `PanelSettings.Translator` | `gemini` / `gemini-flash-latest` | `antigravity`, model unset |
| extension `DEFAULT_VENDORS` | codex, gemini | codex, **antigravity** (`gemini-3.7-flash-high`) |
| extension presets | no Antigravity entry at all | Antigravity first-class; Gemini kept, marked retired |
| a SAVED `runtime: "gemini"` | run as-is | migrated to `antigravity` (id kept — it names the row, the usage history and the vault key) |
| `providers` health | `--version` exits 0 ⇒ "own auth" | `VendorDiagnosis.ForRuntime` answers **before** the probe |

That last row is the one worth remembering: `gemini --version` exits 0 because it prints a version
without ever reaching Google, so a probe built on `--version` is *structurally* incapable of seeing
the retirement. Green health on a dead vendor is worse than no health at all — it is why a round
was still being spent on it a day later.

A vendor with its own `baseUrl` is never migrated: that is not Google's CLI at all.

### WSL, measured (2026-09-01)

Three separate blockers, each of which alone made a WSL round impossible, and none of which the
earlier "WSL works" reports had actually tested:

1. **A vendor's executable path could not be set.** `COAI_EXE_<VENDOR>` was read in ONE branch of the
   settings — the `COAI_PROVIDERS` fallback — and the panel always writes `COAI_VENDORS`. So the
   moment anybody opened the panel, the only way to say WHERE a CLI lives stopped working. In WSL
   that is fatal: `codex` and `gemini` resolve there to the **Windows npm shims** through the interop
   PATH, which run Linux node against a Windows install and die on a missing native dependency. The
   native Linux codex sits in `~/.npm-global/bin` and nothing could point at it. `VendorDto` carries
   `executablePath` now, the env variable answers when the list does not, and the panel shows the
   field for every vendor.
2. **`npm install -g` fails as the ordinary user.** `npm prefix -g` is `/usr`, owned by root. The fix
   is a user prefix (`npm config set prefix ~/.npm-global`), not sudo.
3. **An installed CLI is not a signed-in CLI.** A fresh codex answers a review with five reconnect
   attempts and two 401s, and nothing in that wall says to run its login. Three doors added to
   `VendorDiagnosis`: the missing bearer, the bare 401, and the untrusted-directory refusal — that
   last one matters because a review runs in a FRESH worktree every round, which is a directory
   nobody has ever accepted a dialog for.

**What works on Linux — and on WSL — today:** `claude` is native and answers (exit 0, measured).
`codex` installs from its official npm package and needs one `codex login`. That is two independent
reviewers, which is the minimum the product is built around.

**Antigravity DOES have a Linux CLI, published by Google — and this document said the opposite for
a day.** The claim was built from two true observations: `npm install -g antigravity` is a 404, and
`agy` ships as a Go binary with the Antigravity app. What was never checked is whether Google
publishes an installer of its own, and it does:

```
curl -fsSL https://antigravity.google/cli/install.sh | bash     # Linux and macOS
irm https://antigravity.google/cli/install.ps1 | iex            # Windows
```

Verified 2026-09-01: both URLs serve, `install.sh` branches on Darwin AND Linux, and the resulting
`~/.local/bin/agy` answered nine review rounds of the pre-delivery campaign in WSL. The third-party
`antigravity-cli` snap stays excluded — **official sources only**, an operator decision pinned by a
test over `OFFICIAL_SOURCES`, because a button that installs software gets pressed without reading.

**Two defects came out of believing it.** `VendorDiagnosis.ForRuntime` had a blanket Linux door for
this runtime, and a door there fires BEFORE the probe — so `providers` answered `cliFound: false`,
`auth: unavailable` for a machine whose `agy` was sitting at the path the vendor row named, an hour
after it had reviewed nine rounds. And a test pinned the sentence, which is why it survived: written
from the same wrong belief as the code, it could only ever confirm it. A test is evidence about
behaviour and never about the world.

`ForRuntime` now answers one question — is this runtime CLOSED whatever its binary says, which Gemini
is and Antigravity is not — and a CLI that is merely absent is reported by the probe, with
`VendorDiagnosis.InstallCure` naming the vendor's own install command.

**On WSL, two routes were measured. One works and one does not, and the one that does not is the
one I recommended first.**

*The Windows `agy.exe` as a reviewer's CLI path: NO — and unnecessary now that the Linux CLI installs
from Google's own script.* It launches — `--help` exits 0 through interop,
and a real plan round confirmed the launcher reaches it, which is the `executablePath` fix verified
in anger. Then it runs for 60 seconds and exits 1 with `Error: authentication timed out`. Its
sign-in lives in the Windows user profile and it cannot complete the flow started from a Linux
parent. Reading had predicted a different failure (every path the server hands a reviewer is a Linux
path, and `--json-schema /home/…` means nothing to a Windows process) — the real failure arrives
earlier, at authentication, which is why this was run rather than concluded.

*The Windows `coai-mcp.exe` as the MCP server for a WSL client: YES.* `initialize` and `providers`
both answered over stdio through interop. That makes the already-signed-in Windows CLIs available to
a WSL session with no Linux install at all. Its limit is paths: a Windows server needs Windows paths
for the repository, so the calling AI must pass `D:\rsd\...` rather than `/mnt/d/rsd/...`.

**One unexplained observation, recorded rather than concluded:** in that interop run, the vendor whose
runtime is `antigravity` reported `codex-cli` as its version, while `agy --version` on the same
machine returns 1.1.23 and the same probe on Windows had reported 1.1.23 for it. Either the probe
resolved the wrong executable or the reading was wrong; it has not been reproduced and is not yet a
bug report.

### Reviewers outlive their server unless something collects them (2026-09-02)

`ProcessLauncher` kills an overrunning reviewer with its whole tree — but the kill is performed by
the PARENT, so it cannot happen when the parent is what went away. An MCP client restarting is the
ordinary case, not the rare one, and every reviewer in flight is then orphaned with nothing left to
stop it. Reported from a macOS checkout: an Antigravity child started at 00:03 was still running at
10:00, hours after its round, its vendor removed from the configuration, its server long gone.

Worktrees already had this shape — written on the way in, swept on the next `open` — and processes
did not, although a leaked reviewer costs more than a leaked directory: it holds a vendor's rate
limit, a GPU, or a paid token budget. So `ProcessTracking` writes one small file per reviewer under
`<dataDir>/running/`, and `PanelService` sweeps at startup beside the existing orphaned-round sweep.

**The design is shaped entirely by what must NOT be killed.** The vendor CLIs are programs a person
also runs by hand, so "kill every codex" would be a product that terminates its user's terminal
session. `OrphanSweep` is pure and kills only when all three hold: this product recorded starting the
process, its recorded start time still matches (so the PID cannot have been reused by a stranger),
and the owning server is provably gone. A record whose child has exited is forgotten rather than
acted on; a child of a DIFFERENT live server is left alone, because two servers over one data
directory is ordinary; a child of the sweeping process itself is never touched, since the sweep runs
while rounds are in flight.

Both guards were checked by removing them: without the start-time comparison the sweep kills a
stranger holding a reused PID, and without the sweep the orphan survives — each named by the test
that fails.

### A local model is a direct call, not a CLI (2026-09-02)

`LocalRuntime` is the fifth vendor adapter and the only one whose "CLI" is this binary:
`coai-mcp --ask-local` reads a prompt file, POSTs one completion to an OpenAI-compatible endpoint
with the finding schema, and prints the answer where the executor already looks.

**Why not `CustomCodexRuntime`.** That adapter points the Codex CLI at any OpenAI-compatible base and
it does reach a local Ollama — verified, it answered `LOCAL_OK`. But codex's own system prompt is
**21k tokens** before any review content, so a model with an 8k window is refused outright and a
larger one pays for a prompt that has nothing to do with the review. A direct call pays none of it.

**Why a process at all.** `IReviewerRuntime.Build` returns a `ProcessRequest` and the executor runs
it; letting an adapter answer in-process would reach `BoundedScheduler`, the concurrency accounting,
the usage parser and the failure classification. The process boundary also buys a hard deadline —
and the shim is given one DERIVED from the reviewer timeout and deliberately shorter, so reaching it
produces a sentence rather than the silence of being killed.

**What killing it actually stops, measured.** A long generation was started, the shim killed with
force, and GPU compute was 0% six seconds later. The mechanism is the SOCKET closing on process
death, which Ollama reads as a client disconnect — not process-tree termination, which an earlier
version of this comment claimed and which would not have stopped a daemon outside the tree. Verified
for Ollama; unmeasured for vLLM, where a non-streaming handler may not notice (see
[PLAN_local_trust_and_vllm.md](../todo/PLAN_local_trust_and_vllm.md) §3).

**Routing.** `RuntimeFor` sends any vendor with a base URL to the Codex CLI, and a local vendor IS a
vendor with a base URL — so `runtime == "local"` is checked BEFORE that arm. `providers` answers for
it without a `--version` probe, there being no binary to version. `DefaultExecutable` resolves the
dotnet-host case: `Environment.ProcessPath` is the app in a Native AOT release and `dotnet.exe` when
the same code runs framework-dependent, so the invocation carries the dll ahead of its own flags.

### An unreachable local engine is told what to do about it (2026-09-03)

`VendorDiagnosis.For` now answers for this product's OWN failure sentence, not only for vendor CLIs.
The observation: one machine, coai 12.1 on both sides, settings byte-identical, and from WSL ten
consecutive local rounds of zero seconds —

```
exit 69: [coai-mcp] the local engine at http://127.0.0.1:11434/v1 could not be reached:
 Connection refused (127.0.0.1:11434)
```

— against a Windows Ollama holding fifteen models. Every word true, none of it actionable. The panel
had the cure since 2026-09-02; the ROUND, which is the surface somebody actually watches during a
review, had nothing.

- **The marker is our own sentence**, `the local engine at … could not be reached`, never the word
  "refused" — which appears in every vendor's stack traces. The endpoint is carried through into the
  cure, because `BoundedScheduler.Because` REPLACES the message with what comes back from here and
  the address was the useful half.
- **WSL, not Linux.** `NamesWsl` reads `/proc/version` once. A native Linux box told to edit
  `%USERPROFILE%\.wslconfig` and run `wsl --shutdown` has been handed instructions for a machine it
  is not — raised by Gemini 3.7 Flash on the plan, before the code existed.
- **A timeout is not a refusal.** The shim already distinguishes them, and one cure for both would
  send somebody to reconfigure a network because their model is slow. `UnreachableLocalEngineTests`
  holds all four cases; the two that matter were watched fail with *"Expected cure not to be null"*.

The cure names mirrored networking first (`[wsl2] networkingMode=mirrored`, then `wsl --shutdown`)
and `OLLAMA_HOST=0.0.0.0` second: the first needs no firewall rule and no address that WSL
re-allocates on the next boot. Verified end to end on 2026-09-03 — after mirrored,
`coai-mcp --ask-local` from Ubuntu against the Windows engine returned `exit 0`, 249/142 tokens and
a valid findings object in 31 s. Plan: [PLAN_wsl_local_engine.md](PLAN_wsl_local_engine.md).

### A setting value this build cannot read is said out loud (2026-09-02)

`PanelSettings.Unrecognised` carries a sentence per setting whose VALUE this build does not
understand, written to the log at startup and returned by `providers`.

It exists because of a bug report that was not one: *"I set this and it still keeps asking me —
settings are not applied without a restart again."* They were applied. The file said
`COAI_ON_EXHAUSTED: good_enough`, nothing in the environment overrode it, and the reload watcher
worked — the running server was a build from the day before `good_enough` existed, so it read the
value, did not recognise it, and fell through to `Human`. Three hypotheses and twenty minutes went
into the settings file, the env precedence and the watcher, and one line of output would have ended
it immediately.

The fallback stays, because refusing to start over a value from a future panel would be worse. What
changed is that it is audible, and that the message names the likely cure — the panel and the server
version separately, so "the panel is newer than this server" is the first thing to check.

### The local runtime declares the card it needs (2026-09-03)

`LocalRuntime.Build` sets `ReviewerInvocation.SharedResource` to `EngineKey(endpoint)`, and that is
the whole of this half: the runtime knows which engine it is about to call, and the scheduler knows
how many reviewers one engine may have. `EngineKey` lower-cases the scheme, host and port and trims
the path, so one card cannot end up with two keys through two spellings of its address.

Nothing here opens a socket to decide it. A key is an identity, not a health check — the same
distinction the WSL work drew when it refused to probe for an engine
([PLAN_wsl_local_engine.md](PLAN_wsl_local_engine.md)).

Why it is measured rather than argued: three reviewers on one Ollama, one answered in 30.6 s and two
were cancelled at 590 s. See *One local engine serves one reviewer* in
[module_server.md](module_server.md).

### A Team server is a vendor like any other (2026-09-06)

`remote` is the second runtime that is not a CLI, and it is deliberately built on the shape `local`
already proved: the adapter emits a command line that launches **this binary** in a shim mode
(`--ask-remote`), and the shim does the HTTP. Nothing above the adapter learns that a reviewer ran on
somebody else's machine — the round, the scheduler, the ledger and the panel all see an ordinary
vendor. `LocalRuntime.SelfInvocation` answers the dotnet-host case for both and is reused, not copied.

Four things about it are not obvious from the shape:

- **Two clocks, and they are not the same one.** `--timeout-seconds` is how long the shim waits before
  it cancels and reports; `--vendor-timeout-seconds` is how long the VENDOR may take, which is what the
  server is told. The shim's is deliberately the shorter, so reaching it produces a sentence instead of
  the executor killing the process. Sending the first as the second asked the server for an
  eight-second review and got a `400` naming its allowed range — found by running it against a real
  server, not by a test.
- **A killed shim can still cancel its job — and something actually does it.** The executor kills an
  abandoned child, and a killed process runs no cleanup, so the polite `DELETE` on the way out never
  happens and the review runs to completion on the team's subscription for an answer nobody will
  collect. The shim writes a claim to a job file the moment the server accepts, and the executor calls
  `IReviewerRuntime.AbandonAsync` on **both** kill paths — its own timeout, and cancellation — which
  is where `RemoteRuntime` reads that claim and sends the `DELETE`.

  The seam is on the interface with a `Task.CompletedTask` default, so no CLI adapter changed: a
  vendor process dies with its tree and has nothing to clean up. `ReviewerExecutor.LaunchAsync` is the
  one place every launch passes through, which is the only reason this could be wired once instead of
  in each adapter.

  Three details are load-bearing, and each is a mistake this made first:

  - **The claim is self-contained** — server, job id, and the path to the token that authenticates the
    cancellation. The parent resolves the adapter from a vendor row, which has no data directory in
    it, so a claim that could not authenticate itself would need something its only reader lacks.
  - **The claim is KEPT when the cancellation fails**, and forgotten only on success or a `404`. It
    was first deleted in a `finally`, so a laptop briefly offline at the wrong moment lost the job id
    for ever and the review ran to completion anyway — precisely the outcome the mechanism exists to
    prevent.
  - **The cancellation carries `X-Coai-Contract`.** The server judges that header before it looks at
    the token and answers `426` without it, so the first version would have been refused by every
    server it was ever sent to: wired, and working on nothing.

- **The vendor budget is clamped to what the server accepts.** `POST /api/reviews` refuses anything
  outside 30..1800 seconds, so a person who set an eight-second reviewer timeout would have had every
  remote review answered `400` before it started — a configuration mistake rendered as a server error.
  Clamping beats refusing because the shim's own deadline still honours the shorter setting: the
  review is still abandoned when the person said, and only what the SERVER is told changes.
- **No `SharedResource`.** The server has its own queue and its own per-account exclusion, so a
  client-side semaphore would serialise reviews the server can run at once. The global and per-provider
  caps still apply, because those are about what this machine is willing to have in flight.
- **The long poll never asks for longer than what is left**, and every request is bounded by the
  review's own deadline rather than only by a fixed client timeout. Asking for the full 25 seconds
  with 6 to go meant a 6-second deadline took 26 seconds to notice; and a stalled server held each
  request for the client's 40 seconds, so a 10-second review could sit for 40 — long enough for the
  executor to kill it first, which is the case where the person sees nothing at all. The courtesy
  `DELETE` gets its own short budget for the same reason: it runs while somebody waits for the
  sentence after it.
- **A dropped poll is a blip; three in a row is an outage.** A long poll held open across a proxy or
  a laptop's wifi drops for reasons that have nothing to do with the review, and aborting on the first
  one abandoned reviews that were running perfectly.
- **A status this client cannot read stops the review instead of being waited out.** An unrecognised
  status used to fall through to "queued", so a terminal state from a newer server was read as "still
  waiting" and the shim polled a finished review until its own deadline, then reported it as too slow
  — a wrong sentence about the wrong thing.
- **Giving up has its own exit code.** It used to return the one for "unreachable" while printing
  "did not finish within 90s", so anybody reading the code rather than the text went to check their
  network for a problem whose cure is a longer timeout or more accounts.
- **The queue position is said while it still matters.** The shim used to say nothing at all until it
  finished or gave up, so the position was first mentioned in the sentence announcing the
  cancellation. It is now reported when it CHANGES — a line every second is the same silence with
  more scrolling.

### A failing server must not be polled harder than a healthy one (2026-09-06)

`RemoteProbe` caches a vendor's health for 60 s. The first draft cached a FAILURE for 15 s — which
inverts backoff: a server that is down gets asked four times as often as one that is up, and the
moment it comes under load is the moment every client starts polling it hardest. Raised twice on the
plan round, and the fix is that a failure now waits at least as long as a success and then doubles,
60 s → 10 min, cleared by one good answer.

Two details make the cache honest rather than merely cheap:

- **"Not signed in" is never cached.** It is a fact about a file on this machine, and it changes the
  instant somebody signs in. A cached one would leave the panel telling a person to sign in for a
  minute after they just did.
- **The token is part of the cache key**, as a hash. Otherwise the cure for a `401` appears not to
  work — a person signs in again and the panel keeps showing the rejection for up to ten minutes,
  so they sign in a third time.

The note a person reads comes from the server's own slot counts, which it had already computed for its
queue: *2 of 3 accounts ready* rather than "healthy", and — the distinction that decides who acts —
*all signed out* (the operator must do something) told apart from *all rate-limited* (they come back by
themselves). `401`, `403`, `426` and an outage are four different sentences for the same reason: one is
fixed by signing in again, one cannot be fixed by that person at all, one needs an update, and only the
last means the server is down.

### A token that could not be protected must not report success (2026-09-06)

`TeamServerAuth.WriteToken` sets the file owner-only and then **verifies** it, returning a sentence
when it is not. It first set the mode inside a swallowed `try`, so on a filesystem that ignores modes
— a CIFS mount, a container volume owned by another uid — sign-in reported success over a
world-readable bearer token, and anybody with an account on that machine could sign in as that person.
Three reviewers raised it, two of them as blocking.

Signing in still SUCCEEDS there, and that part is deliberate: refusing would strand anybody whose home
directory is on such a mount, over a machine they already share with the people who could read it.
What changed is that it can no longer happen silently. Windows reports owner-only — the Unix mode API
does not apply, the profile directory is already ACL-protected, and answering "no" would print a
warning on every Windows machine that nobody could act on.

A remote vendor never reaches the `--version` probe, and that arm is load-bearing rather than tidy: the
executable a remote vendor names is `coai-mcp` **itself**, so without it the probe would have run this
binary against itself and reported whatever it printed as a vendor's health.
