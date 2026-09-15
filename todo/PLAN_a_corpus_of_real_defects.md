# PLAN — the gate's own findings become a corpus of real defects

> Status: **stories 0–3 shipped; 4–6 are open.** `rounds.base_ref` went first (PR #268) because
> every round that ran without it lost that half permanently, and the read side followed (PR #272).
> Story 2's package was approved on three conditions — pinned version, grammars pruned at publish,
> the library behind `IAstNormalizer` — and all three are in. Story 3 is the collector.
> Scope: a new `Bugz` panel section, a `coai-bugs` ingest server, and four columns on `findings`.
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
| 4 | The `Bugz` panel section | See below. |
| 5 | The review page | First multi-select in this codebase. |
| 6 | `coai-bugs` + its release line | Mostly machinery. |

**Story 4 reuses, and these are verified:** the model list is `modelsFor` (`src_vs_code/src/models.ts:97`)
with `modelsProvenance` (`:242`) — not consultant-specific. The two-select row shape is
`consultantBody` (`src_vs_code/src/consultantView.ts:30`); the settings shape is `consultSettingsFrom`
(`src_vs_code/src/consultSettings.ts:194`) and the runtime filter beside it (`:118`). A section is one
line at `section()` (`src_vs_code/src/panelView.ts:708`) **plus a `staticKey` field** (`:2450`) or it
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

**The surface.** One new panel section, `Bugz`, carrying four controls: a model picker, a **Collect**
button, a **Review bugs** button (story 5's page, present and disabled until a run has collected
something), and the ingest server's address.

**`collect_runs` comes BEFORE any markup, and story 3 did not build it.** Story 3 stamps a run id on
every row it claims, so *which run decided this finding* is answerable; what is not stored anywhere is
*what run X did* or *is a run in flight right now*. `CollectSummary` is computed, printed to stderr
and lost when the process exits. That was sufficient for a CLI one-shot, which was story 3's only
surface — and it is not sufficient for a button.

The durable-status rule (`.agents/conventions/common/durable-status.md`) is explicit: an action that
starts a process must reflect real state across a reload, the source of truth is persisted
server-side and re-read on load, and a crash must never leave the UI stuck in-flight. With nothing
persisted about a run, a Collect button is exactly the *clicked → reloaded → state lost* case the rule
exists to forbid. So:

1. `collect_runs` as one appended `Schema.Steps` entry, per the contract above.
2. `CollectRun` writes the row **before** the first candidate, and completes it at the end — not one
   write at the finish, which is the shape that cannot represent *running*.
3. A **startup sweep** for a run with no `finished_utc`, mirroring the `InProgress` sweeps the rest of
   this codebase already runs. A VS Code window that closed mid-run killed its child, and that run
   must not read as in-flight forever.
4. The panel reads it through the one-shot mode, because **the panel owns no SQLite** — `--bugs-json`
   grows a `lastRun` object rather than gaining a second mode and a second spawn.

**The server address is a dialog, not a box — and this is a constraint, not a preference.**
`staticKey` in `panelView.ts` is the list of state whose change repaints the panel. The Bugz section
MUST be in it, or the Collect button can never repaint to show a run's progress and item 1 above buys
nothing. But that same function carries a warning learned the hard way: a section holding a free-text
control must NOT be in `staticKey`, because the page then rebuilds under a focused box on every
keystroke — which is why the chat section's prompt textarea became a picker. A server URL is free
text, so the two requirements collide, and the codebase has already settled it twice:
`addTeamServer` and `customConsultant` collect a URL through `vscode.window.showInputBox` behind a
command button, with a validator that can refuse while the box is still open. Bugz follows them. No
inline text control enters this section.

**The model picker is the Consultant's row with a narrower vendor list.** `modelsFor` and
`modelsProvenance` are not consultant-specific and the two-select shape is `consultantBody`'s. The
list must be narrower for a reason that is not stylistic: the ranking pass reads `title`, `why` and
`fix`, which are full of domain names and are **not** sanitised — the normaliser runs later, on code.
Pointing that step at a cloud vendor by accident leaks before anything has been anonymised.

**Line references drift under this file.** `panelView.ts` moved twice in one day while story 3 was in
review (`staticKey` 2450 → 2556 → 2626, `section()` 708 → 730 → 735, `consultantBody` 30 → 112) because
a parallel session works it. Every reference here is re-verified at the moment it is used, never
trusted from this document.

**Not in this story:** the review page itself (story 5) and anything that sends a pair anywhere
(story 6). Collect writes to the local database and nothing leaves the machine.

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
- [ ] `TreeSitter.DotNet` is pinned in `Directory.Packages.props` with its reason, and exempted
      from the monthly bump in writing.
- [ ] The publish keeps three grammars and drops the rest, and something FAILS if that step is
      removed — an unchecked pruning step is a step that silently stops running.
- [ ] Nothing outside the normalizer's own project names `TreeSitter`, and a test says so.
- [ ] The guard tests reachability, never equality, and has a test that would fail on equality.
- [ ] `skipped` and `failed` are distinct, and a zero-knowledge failure is `failed`.
- [ ] Skip reasons are attributed to the stage that produced them — a funnel, not a flat percentage,
      so `language_unsupported` cannot mask `fix_commit_not_found`.
- [ ] The normalizer's property test covers every shipped grammar; `.mjs`/`.cjs` map to JavaScript.
- [ ] The server validates the alphabet, not the absence of names.
- [ ] Ingest lands in quarantine; nothing reaches a live index without a reviewed promotion.
- [ ] No personal data is stored, and the route does not log client IPs.
- [ ] `research/module_server.md` and `module_extension.md` updated; this plan promoted per
      `.agents/conventions/common/planning-docs.md`.
