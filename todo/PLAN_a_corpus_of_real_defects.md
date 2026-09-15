# PLAN — the gate's own findings become a corpus of real defects

> Status: **plan only, nothing implemented yet.** One piece has shipped ahead of it:
> `rounds.base_ref` (PR #268), because every round that ran without it lost that half permanently.
> Scope: a new `Bugz` panel section, a `coai-normalize` sidecar, a `coai-bugs` ingest server, and
> four columns on `findings`.
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
| Normalizer | **A separate non-AOT sidecar** | Roslyn under `PublishAot` with `-warnaserror` is a risk nobody needs to take. |
| Languages | **C#, TypeScript, JavaScript** first | Covers 93 % of the measured corpus (`.ts` 234, `.cs` 189, `.mjs` 38, `.cjs` 1). |
| Corpus | **Accepted coai findings only**, filtered hard | Every entry has a human decision behind it. |
| Missing fix commit | **Drop the finding**, with the reason recorded | |
| Who collects | **A human presses a button** | |
| Identity | **Hand-issued API keys. No personal data at all.** | |

### Revision this forces: tree-sitter, not Roslyn

"Any language later" and Roslyn are incompatible, and the sidecar has two jobs — resolve the
enclosing symbol at `file:line`, and normalise it — which tree-sitter does for both with one API.
What is needed is **syntax, not semantics**: renaming identifiers and stripping literals needs node
kinds, never type resolution.

That makes the sidecar **Rust + tree-sitter**. `dew_flow_sidecar_rust` is the house precedent for its
shape and CI, though not for distribution (it is `publish = false`, built on the machine that runs
it). Four grammars for the first three languages: `c-sharp`, `javascript`, `typescript`, `tsx`. Per
language it needs a config table — which node kinds are a function, which identifier kinds may be
renamed — not per-language code. **`.mjs` and `.cjs` must map to the JavaScript grammar**: they are
39 candidates, 8 % of the corpus, and exactly what a naive `.js` check drops.

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
| 1 | `BugsQuery` + `coai-mcp --bugs-json` | Beside `RoundsQuery`; the panel owns no SQLite. **Deliverable is the count.** |
| 2 | `coai-normalize` — Rust + tree-sitter | Symbol resolution *and* normalisation. The property test is the deliverable. |
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

- [ ] Story 1 reports the count, and the decision to continue is recorded against it.
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
