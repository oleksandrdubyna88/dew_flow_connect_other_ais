# PLAN — the gate's own findings become a corpus of real defects

> Status: **stories 0–5 shipped; 6 is open.** `rounds.base_ref` went first (PR #268) because every
> round that ran without it lost that half permanently, and the read side followed (PR #272). Story
> 2's package was approved on three conditions — pinned version, grammars pruned at publish, the
> library behind `IAstNormalizer` — and all three are in. Story 3 is the collector (PR #307), story
> 4 the panel section and the run state it needed first (PR #317), story 5 the pairs and the review
> page (PR #320).
> Scope remaining: `coai-bugs`, its keys and its quarantine — the one story that sends anything.
>
> **Two deviations from the plan as written.** The normalizer was planned as a `coai-normalize`
> sidecar and is a MODE of `coai-mcp` instead: the reason for a separate binary was Roslyn under
> `PublishAot`, and tree-sitter reaches its grammars by P/Invoke, which Native AOT carries. A second
> release line would have bought nothing. And the Rust toolchain the plan once carried is gone — the
> binding ships prebuilt natives for every RID this product targets, so there is no third toolchain
> in CI.
>
> Related docs: [module_server.md](../research/module_server.md) (the rounds database),
> [module_extension.md](../research/module_extension.md) (the panel),
> [PLAN_local_db.md](../research/PLAN_local_db.md) (why the projection exists).

## The goal

Every accepted finding is a defect a human confirmed. `coai.db` has been collecting them since
2026-09-05 and nothing reads them back as *material*. The goal is a pipeline that turns them into
anonymous, language-normalised before/after pairs, keeps them in a shared vector store, and — later,
not in this plan — matches a fresh pull request against them.

**This plan covers collection only.** Retrieval, the PR gate and the weekly synthesis are downstream
and deliberately out of scope until there is a corpus to retrieve from.

## What is actually there — measured 2026-09-15

Read-only against a live 25 MB `coai.db`: 257 sessions, 524 rounds, 8 687 findings, 66 repo paths.

| Filter | Left |
|---|---|
| all findings | 8 687 |
| `stage = 'CodeReview'` | 5 705 |
| `resolution = 'accept'` | 2 622 |
| `is_gating` (Blocking/Major) | 1 917 |
| `category IN ('Reliability','Performance','Security')` | 771 |
| `file != '' AND line != 0` | **771** (costs nothing — keep it as a guard) |
| real repo, path still on disk | 497 |
| file is C#/TS/JS | **462** |

Four findings that shape the design, each measured rather than assumed:

1. **`Finding.Line` is reliable.** Of the candidates whose commit is still reachable: 206 locatable,
   1 line past EOF, 2 files absent — **98.6 %**. The prediction that loose line numbers would be the
   main loss was wrong.
2. **`head_sha` is orphaned 55.7 % of the time** — squash-merge plus branch deletion. By repo it
   tracks the workflow exactly: `dew_flow_connect_other_ais` 72 %, `creds_for_devs` 39 %, every
   `_wt/*` worktree 100 %, and `creds_e4` / `creds_e5` / `conventions` **0 %**.
3. **The orphans are not lost.** 99.6 % of their blobs are still readable (1 object of 263 collected).
   So `method_before` survives for ~468 of 472; what orphaning destroys is only the forward walk to
   the fix, i.e. `method_after`.
4. **30 % of raw candidates live in scratch directories** — `…/Temp/claude/…`, plus 104 worktrees
   (25 % already gone) and 14 under `toDelete`. None can be walked. This must be a guard, not a
   discovery at collect time.

Two more worth recording: **`repo_path` is not canonicalised** — 52 distinct strings collapse to 42,
and `dew_flow_connect_other_ais` is stored three ways (`D:\`, `d:\`, `D:/`), which also splits one
repo+branch across several sessions; and clustering is already visible — 462 candidates across 182
files, **97 files carrying more than one**, led by `panelProvider.ts` (16), `chatOrphans.ts` (13),
`JobStore.cs` (12).

## Decisions taken (operator, 2026-09-15)

| Decision | Chosen | Why it was asked |
|---|---|---|
| Ingest server | **A standalone AOT binary**, not an endpoint on `coai-server` | Anyone should be able to contribute, not only people with a Team server. `coai-server` is behind Entra and a domain allow-list, which is load-bearing there and wrong here. |
| Symbol resolution | **Mechanically, from `file:line`** | `Finding` (`src_mcp/core/Findings/Finding.cs:54`) has no symbol field. Resolving it works retroactively on everything already stored and needs no prompt or schema change. |
| Normalizer | **A separate non-AOT .NET sidecar**, parsing with tree-sitter | Roslyn under `PublishAot` with `-warnaserror` is a risk nobody needs to take — and Roslyn reads only C#, which is 189 of the 462 candidates. |
| Languages | **C#, TypeScript, JavaScript** first | Covers 93 % of the measured corpus (`.ts` 234, `.cs` 189, `.mjs` 38, `.cjs` 1). |
| Corpus | **Accepted coai findings only**, filtered hard | Every entry has a human decision behind it. |
| Missing fix commit | **Drop the finding**, with the reason recorded | |
| Who collects | **A human presses a button** | |
| Identity | **Hand-issued API keys. No personal data at all.** | |

### The parser: tree-sitter, inside .NET

Roslyn reads C# and nothing else, and C# is 189 of the 462 candidates. The sidecar has two jobs —
resolve the enclosing symbol at `file:line`, and normalise it — and tree-sitter does both for every
language with one API. What is needed is **syntax, not semantics**: renaming identifiers and
stripping literals needs node kinds, never type resolution, which is the whole reason a parser
without a semantic model is enough.

**It stays a .NET sidecar.** A Rust host was proposed and rejected on 2026-09-15: a third language
and a third release line, in a family that is C# and TypeScript everywhere, is a permanent cost
against a one-time build problem — and the build problem turned out not to exist.

**Spiked before it was written into this plan**, on win-x64 and linux-x64:

- The native grammars are **prebuilt and shipped per RID** by the binding package — `win-x64`,
  `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`, and two more. No C compiler, no
  cross-compilation, nothing to link. `dotnet publish -c Release -r <rid> --self-contained` carried
  them for both targets with **zero errors and zero warnings**.
- Both jobs work. Line 9 of a C# fixture resolves to `<method_declaration>` spanning 7..15 with six
  distinct identifiers; the TypeScript and JavaScript fixtures resolve to `<function_declaration>`
  with ten and nine. A line inside no function at all returns nothing, which is the
  `symbol_not_resolved` path arriving on its own.
- **Payload is the one cost.** The package bundles 28 grammars: 69 MB of the 152 MB self-contained
  publish, where the four we need are 9 MB. Pruning the unused ones is a publish step, and worth
  taking — a sidecar people download should not carry Haskell and Verilog.
- **The per-language table holds a LIBRARY and an ENTRY POINT, not one name.** The binding's
  one-argument constructor derives both from a single string, which cannot express C#: the library is
  `tree-sitter-c-sharp`, the entry point `tree_sitter_c_sharp`. The two-argument constructor does.
  This is exactly the sort of thing that looks like a typo six months later.

Four grammars cover the first three languages: `c-sharp`, `javascript`, `typescript`, `tsx`. Per
language the table says which node kinds are a function and which identifier kinds may be renamed —
data, not code. **`.mjs` and `.cjs` must map to the JavaScript grammar**: they are 39 candidates,
8 % of the corpus, and exactly what a naive `.js` check drops.

**The binding package is APPROVED, 2026-09-15, on three conditions.** `TreeSitter.DotNet` is **MIT**,
205 k downloads, six releases from 2025-05-04 to 2026-01-22 — fresh by the twelve-month bar — but
published by one person (Marius Greuel), which `.agents/conventions/csharp/nuget-packages.md` says is
*always* asked about first, however popular. It was asked and granted. **Option D — Roslyn for `.cs`
and the TypeScript compiler API for `.ts`/`.js` — is declined outright**: splitting the normalising
logic across two engines costs more than the dependency does, and it would mean two property tests
guarding one guarantee.

The three conditions, which are part of the story's Definition of Done:

1. **The version is pinned hard, and never floats.** Central package management is on here, so the
   pin lives in `Directory.Packages.props` and the `.csproj` carries a bare `<PackageReference>` —
   that is where the family's other standing pins are recorded, with the reason beside them. This one
   is an explicit exception to the monthly latest-stable bump: an individual's package moves when we
   decide to move it, having read what changed, not when a tool notices a release.
2. **The publish prunes the grammars it does not use.** Keep `c-sharp`, `typescript` and
   `javascript`; drop the other twenty-five. That is ~60 MB of ballast per RID on something people
   download, and a step in the publish rather than a note for later. *(`tsx` is excluded by that
   list. It is consistent with the measurement — no `.tsx` appeared among the 462 candidates — and it
   is one line to add the day one does.)*
**All three were met, 2026-09-15.** The pin and its reason are in `Directory.Packages.props`; the
publish keeps four native libraries and deletes twenty-seven, checked from both directions by the
release Package step against `shared/kept-grammars.txt`; and `OnlyTheNormalizerNamesTreeSitter`
holds the seam, unchanged even after `coai-mcp` gained a reference to the implementing project.

**And the sidecar was cancelled while they were being met.** The reason for a separate binary was
Roslyn under `PublishAot`; tree-sitter reaches its grammars by P/Invoke, which Native AOT carries
without complaint — measured, zero IL warnings — so `coai-mcp --normalize` costs no second release
line and no second download. The grammars ride beside the binary exactly as `e_sqlite3` does.

3. **The library sits behind `IAstNormalizer`.** Nothing outside the implementing project sees
   `TreeSitter`, a `Language`, a node or a P/Invoke. The interface belongs in `CoaiMcp.Core`, which is
   pure and already knows nothing of IO; the implementation and the whole native dependency live in
   one project behind it. That is also what makes Option D cheap to reach for later if this
   dependency ever has to go — a second implementation of one interface, not a second design.

## The collector contract

```sql
-- findings, appended as one Schema.Steps entry (src_mcp/src/Store/Schema.cs:31 — APPEND, never insert)
collect_state   TEXT NOT NULL DEFAULT ''   -- '' | 'collected' | 'skipped' | 'failed'
collect_run_id  TEXT NOT NULL DEFAULT ''
collect_reason  TEXT NOT NULL DEFAULT ''   -- comma-joined codes, as `providers` already is
fix_sha         TEXT NOT NULL DEFAULT ''

CREATE TABLE IF NOT EXISTS collect_runs (
  id TEXT PRIMARY KEY, started_utc TEXT, finished_utc TEXT, model TEXT,
  candidates INT, picked INT, collected INT, skipped INT, failed INT,
  reasons TEXT                              -- the funnel, per stage
);
```

`collect_state` is **not a boolean**: a boolean means the first run permanently consumes everything
it looked at, and a second run with a better prompt can never see it again. `collect_runs` earns its
place twice — it is the skip telemetry *and* the durable run state the panel needs (persisted before
the work starts, re-read on load, swept at startup if a crash orphans it).

### `skipped` is not `failed`

- **`skipped`** — the data cannot support a case. Expected; measure the rate.
- **`failed`** — our code did not do its job. Should be ≈0. **A zero-knowledge assertion failure is
  `failed`**, never `skipped`: a normalizer that starts leaking identifiers must not show up as a
  slightly worse skip rate that nobody looks at.

### Skip reasons

| Code | When | Stage |
|---|---|---|
| `repo_path_transient` | under a temp root or `toDelete` — 30 % of raw candidates | guard |
| `repo_path_missing` | gone, or not a git repository | guard |
| `head_sha_unreachable` | the object is not in this repository | guard |
| `head_sha_orphaned` | present but reachable from no ref — **squash/rebase, 55.7 %** | guard |
| `file_not_in_commit` | the path did not exist at `head_sha` | locate |
| `language_unsupported` | not C#/TS/JS | locate |
| `symbol_not_resolved` | the line lands inside no function | locate |
| `fix_commit_not_found` | nothing in the interval touched the method | walk |
| `method_unchanged` | the fix touched the file, not the method | walk |

### The guard is reachability, NOT equality

`head_sha` is the commit the reviewers read — the **broken** state. By collect time the fix has
landed and `HEAD` has moved past it, so requiring `HEAD == head_sha` would skip precisely the
findings that were fixed. The check is:

```bash
git cat-file -e <head_sha>^{commit}                 # the object is still here
git merge-base --is-ancestor <head_sha> <branch>    # and it is on this history
```

against `sessions.branch`, falling back to reachable-from-any-ref. The second check failing **is**
the history-rewritten signal; no separate detector is needed. *(This plan's first draft specified
equality. It was measured, and it inverted the feature — 90.7 % false orphans against a branch that
happened to be behind.)*

### Three routes to `method_after`, in cost order

1. **Bounded walk** — a later round in the same session bounds the interval exactly. **37 %** of
   candidates (`head_sha[N] .. head_sha[N+1]`, both already stored).
2. **The symbol on the default branch today** — free, offline, works for orphans. Weaker: includes
   drift unrelated to the fix.
3. **GitHub PR API** — `GET /repos/{o}/{r}/commits/{sha}/pulls` finds the PR for an orphaned commit
   even after the branch is deleted. Precise; needs a token, GitHub only.

Ship 1 + 2, measure how often `before == after`, then decide on 3. **A `method_before`-only entry may
be enough for v1**: retrieval matches the shape of buggy code, and the fix is for the human-readable
snippet. That would make ~468 entries available immediately.

### Anonymity

Two validators, deliberately different:

- **Client-side** knows the original, so it asserts a blacklist: no input identifier, string literal,
  numeric literal outside a whitelist, or path survives. A **property test over real files**, not a
  step in the happy path.
- **Server-side** has never seen the original, so it asserts the **alphabet**: every identifier
  matches `var_\d+|type_\d+|method_\d+`, every string is `""`, every numeric `0`. A whitelist —
  strictly stronger, and it needs no parser.

The key table carries no personal data:

```sql
CREATE TABLE api_keys (
  id TEXT PRIMARY KEY, key_hash TEXT NOT NULL UNIQUE,
  created_utc TEXT NOT NULL, revoked_utc TEXT NOT NULL DEFAULT '',
  note TEXT NOT NULL DEFAULT '',            -- OUR record, not the holder's data
  submissions INTEGER NOT NULL DEFAULT 0
);
```

**`last_seen_utc` is omitted on purpose** — with submission timestamps it is a behavioural trace tied
to a key we know we handed to one person. And **nginx must not log client IPs for this route**, or
the anonymity claim is false one layer below the table. Rate-limit partition on `key_hash`;
idempotency on the client-generated entry id.

**Ingest lands in quarantine, never in the live index.** Promotion is a separate reviewed step. The
alphabet validator stops leaked identifiers; it cannot tell a real skeleton from a crafted one, and
a corpus shown to users as precedent is worth poisoning. The quarantine table goes in the first
schema — retrofitting it after the index is live means re-auditing everything in it.

## Build order

| # | Story | Notes |
|---|---|---|
| 0 | `rounds.base_ref` | **Shipped, PR #268.** |
| 1 | `BugsQuery` + `coai-mcp --bugs-json` | **Shipped, PR #272.** Beside `RoundsQuery`; the panel owns no SQLite. It brought the four `findings` columns of the contract below with it, as migration step 6. |
| 2 | The normalizer — .NET + tree-sitter | **Shipped, PRs #283 and #296.** Symbol resolution, normalisation, and the zero-knowledge property test. It is a MODE of `coai-mcp` rather than the `coai-normalize` sidecar this table first named — see below. |
| 3 | The bounded walk + drop-with-reason | Small, because bounded. |
| 4 | The `Bugz` panel section | **Shipped, PR #317.** `collect_runs` had to come first: a button
needs a state that survives a reload, and story 3 recorded only a run id. |
| 5 | The review page | **Shipped, PR #320.** `collect_pairs` had to come first for the same shape
of reason: the collector computed both skeletons and threw them away, so the corpus held pointers
and nothing to review. NOT the first multi-select — `roundsLog.ts` already had one, which the plan
round corrected. |
| 6 | `coai-bugs` + its release line | Mostly machinery, and the one story that CROSSES the
boundary: the upload sends the two skeletons and the language, never the symbol and never the join
back to repo, commit, file and line. |

**Story 4 reuses, and these are verified:** the model list is `modelsFor` (`src_vs_code/src/models.ts:97`)
with `modelsProvenance` (`:242`) — not consultant-specific. The two-select row shape is
`consultantBody` (`src_vs_code/src/consultantView.ts:112`); the settings shape is `consultSettingsFrom`
(`src_vs_code/src/consultSettings.ts:194`) and the runtime filter beside it (`:118`). A section is one
line at `section()` (`src_vs_code/src/panelView.ts:735`) **plus a `staticKey` field** (`:2626`) or it
can never repaint to reveal the Review button. Story 5's surface is `RoundsLogPanel`
(`src_vs_code/src/roundsLogPanel.ts:61`). The sidecar is spawned through `IProcessLauncher`
(`src_mcp/runners/Processes/ProcessLauncher.cs:129`), file-in/file-out and batched one spawn per run,
as `--ask-local` already does.

**The Bugz model list should be narrower than the Consultant's.** The ranking pass reads
`title/why/fix`, which are full of domain names and are **not** sanitised — the sanitiser runs later,
on code. Pointing that step at a cloud vendor by accident would leak before anything is normalised.

**Ranking, not scoring.** Ask for *"the best 10 of these 200, ranked"*. Unanchored 1–5 scales make a
model cluster at 4s and 5s, so a threshold over them filters nothing. Sort by category in code, from
the returned rank.

### Story 4 — the `Bugz` section, and the run state it needs first

> Revised after the plan round: 16 findings, 12 accepted. What changed is recorded inline — the sweep
> is a heartbeat rather than a timeout, the model restriction moved from the picker to the collection
> boundary, and this section now carries the growth budget the convention requires.

**The surface.** One new panel section, `Bugz`, carrying four controls: a model picker, a **Collect**
button, a **Review bugs** button (story 5's page, present and disabled until a run has collected
something), and the ingest server's address.

#### Who owns what, across stories 3 to 6

| | Owns | Explicitly does NOT |
|---|---|---|
| **3** (shipped) | `findings.collect_state/collect_run_id/collect_reason/fix_sha`; the walk; the run id | any record OF a run; anything a person presses |
| **4** (this) | `collect_runs`; the section; the Collect button's enabled state; the model allowlist | the review page's contents; any outbound call |
| **5** | the review page, the multi-select, the ranking pass | the run records; the allowlist it must USE |
| **6** | `coai-bugs`, its keys, its quarantine | anything in `coai.db` |

#### `collect_runs`, and why story 3 did not build it

Story 3 stamps a run id on every row it claims, so *which run decided this finding* is answerable;
what is stored nowhere is *what run X did* or *is a run in flight right now*. `CollectSummary` is
computed, printed to stderr and lost when the process exits. That was sufficient for a CLI one-shot,
which was story 3's only surface — and it is not sufficient for a button. The durable-status rule
(`.agents/conventions/common/durable-status.md`) requires the state to survive a reload, to be read
back from storage rather than from a local flag, and never to stick in-flight after a crash.

```sql
CREATE TABLE IF NOT EXISTS collect_runs (
  id TEXT PRIMARY KEY, started_utc TEXT, finished_utc TEXT, heartbeat_utc TEXT,
  state TEXT NOT NULL DEFAULT 'running',   -- running | done | failed | interrupted
  model TEXT, candidates INT, picked INT, collected INT, skipped INT, failed INT,
  reasons TEXT                              -- the funnel, per stage
);
```

1. The row is written **before** the first candidate, not once at the end — a single write at the
   finish is the shape that cannot represent *running*.
2. Counts and `heartbeat_utc` are refreshed **as each candidate is decided**, on the write that
   already happens there. Progress was promised and two writes cannot express it; either the writes
   or the promise had to go, and the writes are nearly free because `RecordCollect` is already a
   per-candidate statement. *(Plan round, gemini.)*
3. A terminal state is written in a **`finally`**, with whatever counts were reached. A throw or a
   hang on candidate three otherwise never reaches the completing write, and the button stays
   in-flight forever. *(Plan round, codex.)*

#### The sweep is a heartbeat, and deliberately not a pid

The plan round caught a defect this would have shipped: because the panel reaches SQLite only through
one-shot invocations, a sweep that ran at `--bugs-json` startup and cleared every row with no
`finished_utc` would clear the row of a `--collect-bugs` run that is **alive at that moment**. And
deleting the row orphans every finding already stamped with its id. *(gemini.)*

gemini proposed a pid column and a liveness check. This uses a **heartbeat** instead, reusing
`chatStoreSweep`'s pattern (`HEARTBEAT_EVERY_MS`, `HEARTBEAT_STALE_MS`) rather than inventing a
second answer to the same question — and for a reason the pid proposal does not survive: **the data
directory can be a network share.** The operator runs one on a NAS, so a row may carry a pid from a
different machine, where it is not merely useless but actively wrong — it will eventually name a
live, unrelated process. A timestamp means the same thing on every host.

- A run refreshes `heartbeat_utc` as it decides each candidate.
- A run whose heartbeat is older than the stale window is presumed gone. The window is
  `chatStoreSweep`'s thirty minutes and for its stated reason: wide enough for a sleeping laptop, a
  host paused under a debugger, or a window that has just reloaded.
- It is then marked `interrupted`, **never deleted** — the id is a foreign key in all but name.

#### The model restriction is enforced at the boundary, not at the picker

Two reviewers found this independently. A picker is UI: `--collect-bugs` can be invoked directly, a
persisted setting survives a narrowed list, and a stale webview posts whatever it last rendered. So
**one named allowlist lives in `CoaiMcp.Core`, the collector refuses anything outside it before a
single finding field is read, and the picker is derived FROM that list** rather than agreeing with it
separately. A test asserts the refusal and that no call was made.

**Assumption, stated because it narrows the operator's spec:** the allowlist is **local vendors
only** for now. The ranking pass reads `title`, `why` and `fix`, which are the reviewers' own words
about somebody's code and are not sanitised — the normaliser runs later, on source. The honest
complication is that those words were often *written by* a cloud model during the review round that
produced them, so this is not a categorical leak; it is an uncontrolled one, and local-only is the
defensible default until a person decides otherwise. The question is at the end of this story.

#### The server address is a dialog, not a box

`staticKey` is the state whose change repaints the panel. The Bugz section MUST be in it or the
Collect button can never repaint, and everything above buys nothing. But that same function carries a
warning learned the hard way: a section holding a free-text control must NOT be in `staticKey`,
because the page then rebuilds under a focused box on every keystroke — which is why the chat
section's prompt textarea became a picker. A server URL is free text, so `addTeamServer` and
`customConsultant` settle it: `vscode.window.showInputBox` behind a command button, with a validator
that refuses while the box is still open. No inline text control enters this section.

#### Growth budget

Required by `planning-docs.md` for any plan introducing a growth surface, and this adds one table.

- **Projected size.** One row per Collect press. At the plan's own volume — a person pressing it
  about once a day — that is ~365 rows a year. A row is two timestamps, a heartbeat, a state, a model
  name, five integers and a `reasons` string capped at the nine skip codes with counts: ~400 bytes.
  **~150 KB a year**, beside a `coai.db` that is already megabytes of rounds and findings.
- **Who retires it.** Nobody: **kept forever, projected ~150 KB/year, stored in `coai.db`** beside the
  findings whose `collect_run_id` points at it. Deleting a run would orphan the rows that name it, and
  the whole point of the id is that *which run decided this, under which rules* stays answerable.
- **When it is interrupted.** The heartbeat sweep above, invoked by whichever one-shot mode opens the
  database for writing — the same idempotent `RoundsDb.Open` path the migration already runs through.

#### Compatibility, because the halves ship out of step

`--bugs-json` grows a `lastRun` object rather than gaining a second mode and a second spawn. The two
halves of this product have shipped out of step before, so: `lastRun` is **optional** and its absence
means *no run has ever finished*, never *unavailable*; a new panel reading old output must therefore
behave, and one check runs the **real binary** and parses its output with the panel's own reader.
*(Plan round, codex.)*

#### Test plan

- `collect_runs` against a seeded `RoundsDb` on real SQLite in a temp directory, as `RoundsDbTests`
  does; the migration tested from a **hand-spelled older schema**, never from the current `Schema`
  constant, or it follows the code forward and stops testing.
- A run interrupted between its two writes leaves a `running` row; a fresh heartbeat is left alone and
  a stale one is marked `interrupted` — both asserted, because a sweep that cleared a live run is the
  defect the plan round caught.
- A model outside the allowlist is refused **before any field is read**, and nothing is called.
- `bundledPage.test.ts` drives the assembled page: press Collect, observe running → done, confirm the
  section repaints and that Review flips from disabled to enabled only when a run collected something.
  Static assertions that the markup contains the right text stay green while a click does nothing.
- One end-to-end check over the real binary's output and the panel's reader, both shapes.
- `research/module_server.md`, `module_extension.md` and `module_tests.md` updated; diagrams re-rendered.

#### Definition of done

- [x] `collect_runs` exists; a run writes it before the work, on every candidate, and in a `finally`.
- [x] A stale run is swept to `interrupted`; a live one is untouched; no row is ever deleted.
- [x] The allowlist is one list in the core, enforced before any finding field is read — and it now
      travels on the wire, so the picker follows the server it is talking to rather than a copy.
- [x] `--bugs-json` reports `lastRun`, optional, and old output parses — checked against the REAL
      binary by `bugzLiveContract.test.ts`, not only against hand-written JSON.
- [x] The section renders, repaints, and holds no free-text control.
- [x] `TreeSitter.DotNet` is exempt from the monthly bump by something that FAILS, not by a sentence.
- [x] The three drifted references above are corrected and the six satisfied DoD boxes are ticked.
### Story 5 — the review page, and the pairs it has nothing to show without

> Revised after the plan round: 15 findings, 11 accepted. Two of them corrected statements of mine
> that were simply false — this codebase already HAS a multi-select, and a stored pair is not
> anonymous even though its skeletons are.

**The corpus contains no pairs.** `CollectOutcome` carries `SymbolName`, `SkeletonBefore` and
`SkeletonAfter` — the collector MUST compute both skeletons, because comparing them is how it
decides the method changed at all — and then throws all three away. Only `fix_sha` is persisted.
What shipped is a corpus of POINTERS: `(repo_path, head_sha, fix_sha, file, line)`.

**Operator decision, 2026-09-16: store them when collected.** Recomputing costs a git read, a
locate, a normalise, a second git read and a second normalise per candidate, at review time AND
again at upload time — and the symbol name is not stored either, so each recomputation must
re-locate by line first, exactly as the collector did.

```sql
CREATE TABLE IF NOT EXISTS collect_pairs (
  finding_id      INTEGER PRIMARY KEY REFERENCES findings(id),
  symbol_name     TEXT NOT NULL,
  language        TEXT NOT NULL,
  skeleton_before TEXT NOT NULL,
  skeleton_after  TEXT NOT NULL,
  written_utc     TEXT NOT NULL,
  keep            INTEGER NOT NULL DEFAULT -1   -- -1 undecided, 0 dropped, 1 kept
);
```

#### What is anonymous here, said precisely

The first draft's Definition of Done said *every stored pair passes the zero-knowledge check*, and
that is false. `symbol_name` is a name, and `finding_id` joins straight back to the repository path,
the commit, the file and the line. **The two SKELETONS carry the zero-knowledge guarantee; the row
around them is local metadata.** (Plan round, codex.)

It adds no new exposure where it sits: `coai.db` already holds the repository path, the file, the
line and the reviewers' un-anonymised prose about the code. The boundary that matters is what LEAVES
the machine — and story 6 sends the two skeletons and the language, never the symbol and never the
join. The check to write is therefore *every stored SKELETON is anonymous*, asserted over the
column rather than over the row.

#### One transaction, and `--all` must not forget a decision

**The outcome and the pair are one transaction.** A kill between the two writes leaves a database
where they disagree, which is the one thing the first draft claimed could not happen. Rolled back
together, with an interruption test. (Plan round, codex.)

**`--all` upserts the pair and leaves `keep` alone.** Two reviewers found this independently and it
is the sharpest failure in the story: a person reviews two hundred pairs, runs `--all` to pick up a
repaired walk, and an ordinary upsert takes every decision back to `-1`. Silently.

```sql
INSERT INTO collect_pairs (...) VALUES (...)
ON CONFLICT(finding_id) DO UPDATE SET
  symbol_name = excluded.symbol_name, language = excluded.language,
  skeleton_before = excluded.skeleton_before, skeleton_after = excluded.skeleton_after,
  written_utc = excluded.written_utc        -- and NOT keep
```

Tested across a rerun for a kept row AND a dropped one, because `0` and `1` are both decisions and a
guard written as `WHERE keep = -1` would preserve only one of them.

#### Growth budget

- **Projected size, measured rather than guessed.** 53 real methods from this repository, put
  through the shipped `--normalize` on 2026-09-16: a skeleton is **381 bytes** at the median, 416
  mean, 706 at p90, 1 576 at the largest. A pair is two of them — **762 bytes** at the median — so
  the 462 usable candidates the funnel measured are **344 KB**. (I first wrote ~1.6 KB a pair and
  ~1 MB from intuition; measuring it halved the number, which is the reason the rule asks for one.)
- **Who retires it.** Nobody: kept forever, projected ~344 KB for the measured corpus, stored in
  `coai.db`. It IS the artefact this whole plan exists to produce, so a retention window would
  delete the product; `keep = 0` marks a person's decision rather than destroying the evidence for
  it. A legitimate answer under `planning-docs.md` precisely because it is a decision with a number.
- **When it is interrupted.** Nothing to sweep: the pair is written inside its outcome's
  transaction, so a run that dies leaves the pairs it had already committed, which are true.

#### How the page reaches the pairs

The panel owns no SQLite, and `PROJECT.md:52` sanctions one shape for crossing that gap: a one-shot
CLI mode chosen from `args[0]` before any transport opens, whose stdout is its entire interface.
The first draft described a page reading and writing a table and never said how. (Plan round,
gemini.)

| Mode | Answers |
|---|---|
| `--pairs-json [--limit N]` | the collected pairs with their `keep`, for the page to render |
| `--pairs-keep --in <decisions.json>` | writes a batch of keep/drop decisions, file-in as `--findings-many` does |

**A batch file, not one spawn per decision**, on the precedent of `--findings-many`: a review of two
hundred pairs is two hundred spawns otherwise. And `PROJECT.md:70` — *adding a one-shot mode means
adding it here* — so both go into that list in the same change.

**Neither exits 64.** A binary that KNOWS a mode must never exit 64 whatever is wrong with the
request, because 64 is how the extension detects an OLD binary and falls back; a request fault
wearing that code hides itself behind a successful-looking fallback. A malformed decisions file is
**65 (EX_DATAERR)**, as `--findings-many` answers an unreadable keys file.

#### The page

`RoundsLogPanel` (`src_vs_code/src/roundsLogPanel.ts:63`) is the surface precedent — a webview panel
of its own rather than a panel section, because a person reading pairs is reading, not configuring.

**This is NOT the first multi-select, and the first draft was wrong to say so.** `roundsLog.ts:1121`
already renders `<input type="checkbox" id="pickall">` over a `.pick` column, and `PROJECT.md:75`
records what it cost: *a tick-box and an Export branch each need an early `return` or the control
also opens the row it sits in, and no source assertion can see a missing one.* Building a fresh
pattern would have reintroduced a solved bug. This page adopts that file's propagation handling and
its select-all. (Plan round, gemini.)

**The decision is PERSISTED, not held in the page** — same reason `collect_runs` is a table. A review
of two hundred pairs is not finished in one sitting.

#### The ranking pass

**Ranking, not scoring.** Ask for *the best 10 of these 200, ranked*. Unanchored 1–5 scales make a
model cluster at 4s and 5s, so a threshold over them filters nothing. Sort by category in code, from
the returned rank.

**A model's answer is external unvalidated data**, and the first draft specified the prompt and
nothing about the reply. So: a strict schema; ids the model invented are dropped; ids it omitted keep
their original order rather than vanishing; duplicate ranks break ties by the original order; and a
reply that will not parse is a failure with a visible reason, never a silently shorter list.
(Plan round, gemini and codex.)

**It runs through the shared launcher with a timeout and a tree-kill**, like every other process
here. A local model that hangs would otherwise leave the operation pending for ever — which is the
failure the durable-status rule exists for — and the failure is recorded so a retry costs no
decisions.

**It uses story 4's allowlist and does not own it.** `RankingModels` refuses a non-local model
before a finding field is read. This pass is the first thing in the pipeline that actually SENDS
`title`, `why` and `fix` anywhere: until now that boundary has guarded a step that did not exist.

#### Test plan

- `collect_pairs` against a seeded `RoundsDb`, and the migration from a hand-spelled older schema.
- A collected candidate writes its pair inside the outcome's transaction; a skipped one writes none;
  an interruption between the two leaves neither.
- `--all` rewrites a pair and PRESERVES `keep`, asserted for a kept row and a dropped one.
- Every stored SKELETON passes the zero-knowledge check, asserted over the column.
- The page RUN, not read (`PROJECT.md:78`), and the assertion is that the decision was PERSISTED:
  select two of three, press keep, read the value back after a reload. A message posted to a broken
  handler would pass a test that stopped at the message.
- A ranking reply that invents an id, omits one, or will not parse at all.
- A ranking pass with a non-local model is refused before any finding text is read.

#### Definition of done

- [ ] `collect_pairs` exists; a collected candidate writes one in its outcome's transaction.
- [ ] `--all` preserves `keep` for both a kept and a dropped row.
- [ ] Every stored SKELETON passes the zero-knowledge check — the row is local metadata and the plan
      says which half carries the guarantee.
- [ ] `--pairs-json` and `--pairs-keep` exist, are named in `PROJECT.md`, and neither exits 64.
- [ ] The page lists pairs, multi-selects on `roundsLog.ts`'s pattern, and its decisions survive a
      reload — proved by reading them back, not by watching a message.
- [ ] The ranking pass validates the model's reply and cannot hang.
- [ ] `research/module_extension.md` and `module_server.md` updated.

#### What story 5 did NOT finish

Stated so story 6 does not discover it:

- **The ranking has no transport.** `Ranking.Order` is pure and fully tested — inventions,
  omissions, duplicate ranks, contradictions — and nothing spawns a local model to produce a reply
  for it. The page shows pairs in collection order. `RankingModels` therefore still guards a step
  that does not exist.
- **No paging.** `--pairs-json` takes a limit and the caller passes one; there is no cursor and no
  `hasMore`, so a corpus past the limit is not reviewable from the page.
- **No in-flight feedback** while a decision writes, and no loading state while the panel first
  reads.
- **No live binary-to-panel contract check for the pairs.** `bugzLiveContract.test.ts` does that for
  the corpus read; there is no equivalent here, and no end-to-end scenario test of the review flow.

#### What this story does NOT own

The run records and the allowlist are story 4's. Anything that sends a pair off this machine is
story 6's: this story ends with a person's keep/drop decision written to a column.

### Story 6 — `coai-bugs`, and the only story that sends anything

> Revised after the plan round: 24 findings, 23 accepted. The first draft contradicted itself about
> the entry id, called a lifetime counter a rate limit, and implied a test could prove something no
> application-level test can. All three are corrected below.

Everything so far has stayed on one machine. This is the boundary, and it is the whole risk of the
plan: five stories of care about anonymity are worth exactly what this one upload sends.

#### What crosses, and nothing else

```csharp
public sealed record UploadedPair(string Language, string SkeletonBefore, string SkeletonAfter);
```

**A type of its own.** `StoredPair` carries the finding id, the symbol, the severity, the category
and the title — every one of which must never leave, and a story-5 code round flagged reusing it
here as the obvious way to leak all five at once. Explicit mapping at the boundary is human
discipline, so an **architecture test** asserts this type has exactly three properties: adding one,
or mapping the stored row onto the wire, is a red test rather than a silent widening.

**The id is DERIVED, not sent.** The first draft promised idempotency on a client-generated entry id
AND that only three fields cross — both cannot be true, and two reviewers said so. They can both be
KEPT: the id is a pure function of the payload, `sha256(language \0 before \0 after)`, so the server
computes it and nothing extra crosses. It is stable, carries nothing, deduplicates identical pairs
from different people for free, and there is no mismatch case to handle because there is no second
copy to disagree.

#### The alphabet check is SHAPE validation, not proof of anonymity

The two validators are deliberately different. The **client** knows the original and asserts a
blacklist — nothing of the input survived — as a property test over real files, per grammar. The
**server** has never seen the original, so a blacklist is impossible; it asserts a whitelist: every
identifier matches `var_\d+|type_\d+|method_\d+` or is runtime vocabulary, every string is `""`,
every number is `0`.

**And that is a shape, not a proof.** A source method literally named `method_1` normalises to a
placeholder that may be the same string, and the server cannot tell the two apart. Nothing unsafe
follows — a name that coincides with what we would have generated carries no information — but the
first draft called this check the guarantee, and the guarantee is the client-side property test.
(Plan round, codex.)

**The vocabulary is REFERENCED, not copied.** A plan reviewer asked for a shared file on the premise
that the ingest server is a different binary which cannot reach `CoaiMcp.Core` — and checking it
before building, that premise is wrong: `coai-server` already references `CoaiMcp.Runners`, so a
sibling server can reference the core directly. A project reference is strictly stronger than a file
plus two parity tests, because the compiler enforces it and there is only ever one list. The
reviewer's concern — two lists drifting — is the right concern and this is the better answer to it.

#### Per-item results, or one bad pair strands every good one

A batch that fails as a whole is a batch the client retries unchanged, for ever, with every valid
pair behind the invalid one. So `POST /ingest` answers **200 with a result per item** —
`accepted`, `duplicate`, or `refused` with a reason a person can read — and a refusal never fails
its neighbours. (Plan round, gemini.)

The client marks a pair sent **only on `accepted` or `duplicate`**, never on the batch leaving: a
pair marked before the server answered is a pair lost in silence, and a kill between the two must
leave it to be retried. A `refused` pair is marked refused locally, so it is not retried blindly and
a person can see there is something wrong with OUR normaliser.

#### Quarantine, and how anything leaves it

Ingest lands in quarantine; the live index is a separate table. The alphabet check stops leaked
identifiers; it cannot tell a real skeleton from a crafted one, and a corpus shown to people as
precedent is worth poisoning. It goes in the FIRST migration, because retrofitting it after an index
is live means re-auditing everything already there.

**Promotion is `coai-bugs --promote`**, an admin one-shot, a person reading. Nothing automatic and no
timer: the first draft said 'a separate reviewed step' and named no mechanism, which makes quarantine
a room with no door. An interrupted promotion leaves the rows in quarantine, because the row moves
inside one transaction.

#### Identity is a key, and the key is not in argv

```sql
CREATE TABLE api_keys (
  id TEXT PRIMARY KEY, key_hash TEXT NOT NULL UNIQUE,
  created_utc TEXT NOT NULL, revoked_utc TEXT NOT NULL DEFAULT '',
  note TEXT NOT NULL DEFAULT '',            -- OUR record, not the holder's data
  submissions INTEGER NOT NULL DEFAULT 0
);
```

- **HMAC-SHA256 with a server secret**, not a bare hash: a stolen database must not be a rainbow-table
  exercise. Compared in constant time.
- **`coai-bugs --issue-key --note "..."`** prints a key once and stores only its hash; `--revoke <id>`
  ends it. A fresh deployment otherwise has an empty table and no documented way to fill it.
- **The client reads its key from a file or the environment, never argv** — an argument is in process
  listings and shell history, which this family's secret rule forbids outright.
- **TLS is required** for any deployment that is not loopback. The server refuses to start otherwise
  unless told a proxy terminates it, because a bearer key over plain HTTP is a key an observer keeps.

**`last_seen_utc` is omitted on purpose**: with submission timestamps it is a record of when a person
we handed a key to was working. `submissions` is a counter without a clock — **and it is not a rate
limit**, which the first draft implied. A lifetime counter has no window and no reset; what actually
bounds abuse is the body cap, the batch cap and a per-key daily quota the deployment enforces.

#### The no-IP promise, honestly

The application cannot keep this promise on its own. A reverse proxy writes `remote_addr` before the
request reaches any route, and a test asserting that no logging middleware is registered passes while
nginx logs every upload. Three reviewers said so and they are right.

So it is stated as what it is: **a deployment obligation**, written in the deploy notes with the exact
nginx and Kestrel settings, verified by reading the deployed stack's logs after a real ingest. The
application-level test is kept for what it does prove — that WE did not add one — and is described as
that and nothing more.

#### Growth budget

- **Projected size.** A pair is 762 bytes measured (story 5), plus a 64-character id and a row: call
  it 900 bytes. The whole measured corpus of one contributor is 462 pairs — **~400 KB**. A hundred
  contributors is 40 MB, which is the expected volume.
- **At abuse volume**, the caps are what bound it: **1 MB body, 200 pairs a batch**, so one key's
  daily quota is what decides the ceiling rather than the table.
- **Who retires it.** Quarantine rows that a person REFUSED are deleted on promotion; rows nobody has
  reviewed are kept, because deleting unreviewed submissions silently discards contributions. The
  live index is kept for ever — it is the product.
- **When it is interrupted.** A promotion is one transaction; an interrupted one leaves everything in
  quarantine, which is the state it started in.

#### The shape

| | |
|---|---|
| `POST /ingest` | a batch of `UploadedPair`, keyed, per-item results, idempotent on the derived id |
| `GET /health` | unauthenticated, says nothing about the corpus |
| `coai-bugs --issue-key` / `--revoke` / `--promote` | admin one-shots |
| `coai-mcp --upload-pairs` | the client half — **registered in `.agents/PROJECT.md`**, and never 64 |

A standalone AOT binary, per the operator's decision of 2026-09-15: `coai-server` sits behind Entra
and a domain allow-list, which is load-bearing there and wrong here — anyone should be able to
contribute, not only people with a Team server.

#### Test plan

- The alphabet validator over the skeletons this repository's own corpus produces — every one must
  pass — and over crafted ones carrying a hostname, an identifier and a number, which must not.
- A mixed batch: the valid pairs are accepted and the invalid one refused, in one answer.
- The same pair twice is `duplicate` and stores one row.
- A revoked key and an unknown key are both refused, and the answers are indistinguishable.
- `--upload-pairs` sends `keep = 1` only, sends three fields and no others, marks sent only on an
  acknowledgement, and leaves an unacknowledged pair to be retried after a kill.
- An architecture test: the wire type has exactly three properties.
- The `http/` contract suite gains this server — it is what caught the AOT JSON binding failure that
  made a released `coai-server` answer 500 to everything.
- A scenario over the REAL built CLI and the REAL server: kept rows selected, ingested, refused,
  persisted, retried. Catalogued in `research/module_tests.md`.

#### Definition of done

- [ ] The wire type has three properties and a test says so; nothing maps `StoredPair` onto it.
- [ ] The server derives the id; no id crosses.
- [ ] Per-item results; one refusal never strands its neighbours.
- [ ] Ingest lands in quarantine; `--promote` is the only way out and a person runs it.
- [ ] Keys are HMAC-hashed and compared in constant time; `--issue-key` exists; the client never
      takes a secret in argv; TLS is required off loopback.
- [ ] `last_seen_utc` does not exist, and the no-IP promise is written as a deployment obligation
      rather than implied to be tested.
- [ ] `--upload-pairs` is in `.agents/PROJECT.md` and never exits 64.
- [ ] The growth budget above is real; body and batch caps are enforced.
- [ ] A scenario test drives the real binaries; `module_server.md`, `architecture.md` and
      `module_tests.md` describe the boundary.

#### What this story does NOT own

Retrieval, the PR gate and the weekly synthesis are downstream and out of this plan entirely. The
ranking pass's transport is story 5's unfinished tail and stays there — it is not a prerequisite for
sending a pair somebody already decided to keep.

## Test plan

- `BugsQuery` against a seeded `RoundsDb`, the `RoundsDbTests` pattern: real SQLite, temp directory.
- The migration from a hand-spelled older schema, as `ADatabaseFromBeforeTheseColumns_GainsThem_…`
  does — never from the current `Schema` constant, or it follows the code forward and stops testing.
- The guard, against a **real** repository fixture: a reachable commit, an orphaned one (commit,
  branch, delete), a missing path, a transient path.
- The normalizer's property test over a corpus of real files, per grammar.
- Round-trip: a known bug→fix pair from this repository's own history produces the expected pair.
- The ingest server's contract suite entry under `http/` — that suite is what caught the AOT JSON
  binding failure that made a released `coai-server` answer 500 to everything.

## Definition of Done

- [x] Story 1 reports the count, and the decision to continue is recorded against it.
- [x] `TreeSitter.DotNet` is pinned in `Directory.Packages.props` with its reason, and exempted
      from the monthly bump in writing.
- [x] The publish keeps three grammars and drops the rest, and something FAILS if that step is
      removed — an unchecked pruning step is a step that silently stops running.
- [x] Nothing outside the normalizer's own project names `TreeSitter`, and a test says so.
- [x] The guard tests reachability, never equality, and has a test that would fail on equality.
- [x] `skipped` and `failed` are distinct, and a zero-knowledge failure is `failed`.
- [x] Skip reasons are attributed to the stage that produced them — a funnel, not a flat percentage,
      so `language_unsupported` cannot mask `fix_commit_not_found`.
- [x] The normalizer's property test covers every shipped grammar; `.mjs`/`.cjs` map to JavaScript.
- [ ] The server validates the alphabet, not the absence of names.
- [ ] Ingest lands in quarantine; nothing reaches a live index without a reviewed promotion.
- [ ] No personal data is stored, and the route does not log client IPs.
- [ ] `research/module_server.md` and `module_extension.md` updated; this plan promoted per
      `.agents/conventions/common/planning-docs.md`.
