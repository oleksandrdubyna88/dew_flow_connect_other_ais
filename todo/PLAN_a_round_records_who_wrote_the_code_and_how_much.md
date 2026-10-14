# PLAN — a round records who wrote the code under review, and how big the change was

> Status: **plan only, nothing implemented yet, 2026-10-14.** Revised the same day after the question
> consultant's advice (§10), and again after the seven stories documents (§16). Scope: `src_mcp` — the code round's
> numstat (`runners/Context`), a new text-free caller-transcript reader beside `Store/AgentLog.cs`, attribution in
> `core/Authors/`, appended schema steps, the round's
> record (nullable `Size` and `Authors` snapshots in the session file, text-free) and its projection write, `--log`,
> and two new one-shot modes (`--round-authors`, `--backfill-authors`); `src_vs_code` — one line on the round card.
> Nothing on the Team server.
>
> Related docs: [module_server.md](../research/module_server.md), [module_extension.md](../research/module_extension.md),
> [PLAN_the_round_knows_which_ai_called_it.md](../research/PLAN_the_round_knows_which_ai_called_it.md) (the
> `caller_model` this extends), [PLAN_local_db.md](../research/PLAN_local_db.md) (where `agent_log` is recorded).

## 1. The goal, and why today's store cannot answer it

The owner, 2026-10-14: *questions like "is the code better or worse since coders moved to Sonnet?" are answered
from data.* Since 2026-10-13 the family's policy (`.agents/conventions/common/subagent-models.md`) runs the main
session on Opus, scouts and coders on **Sonnet subagents**, judges on Opus and Fable for epics, security and
cornerstones. The question is about the subagents, and coai records none of them.

**What a round records today** (both `coai.db` stores — Windows `%LOCALAPPDATA%\coai-mcp`, WSL
`~/.local/share/coai-mcp` — read-only, 2026-10-14):

| Recorded | Where | What it cannot say |
|---|---|---|
| `caller_vendor`, `caller_client`, `caller_model` | `Schema.cs:215-220` (`WhoCalled`), from `open`'s `callerModel` | the MAIN session only — "Opus + Sonnet coders" and "Opus + Opus coders" are one row |
| `base_ref`, `head_sha` | `Schema.cs:229-231`, `Schema.cs:69` | the range, but **no size**: findings cannot be normalised per changed line |
| findings: severity, `is_gating`, `re_raised`, resolution | `Schema.cs:90-114` | — |
| `reviewers`: provider, role, status, `model` (step 20) | `Schema.cs:80-88,597-600`, `RoundsDb.cs:923-945` | the reviewer's **effort** — `ReviewerState.Effort` exists (`SessionStore.cs:29-37`) and is not persisted |

**The measurement that motivates this** (CODE rounds; the caller's query of both stores):

| Store | Rounds before (2 weeks) | Rounds after (~1 day) | Findings / round before → after |
|---|---|---|---|
| Windows | 393 | 10 | 4.2 → 7.6 |
| WSL | 416 | 100 | 4.8 → 3.9 |

Read as "Sonnet coders made it worse on Windows and better in WSL", both lines are wrong for reasons the store
cannot even show:

1. **The author is inferred from a DATE, not observed.** Nothing says which rounds' code a Sonnet subagent wrote.
   Effort moved the same week (every model converged to `high` on 2026-10-14, `subagent-models.md` § *Effort*) —
   this session's own transcripts show Sonnet subagents at `medium` before it.
2. **The reviewer set changed on 2026-10-11** (only codex reviewing), inside the "before" window: findings per
   round is a property of the reviewers as much as of the code.
3. **One branch dominates the Windows "after"**: ten rounds, mostly one session — a mean over one task.
4. **No size**, so findings per 100 changed lines — the normalisation that survives a change of task mix — could not
   be computed.
5. **First reviews and corrective rounds are mixed.** Every code round reviews the cumulative base→head diff, so a
   fourth round over the same change is another exposure of old code, not new code.

**Measured while writing this plan** (Windows store and this machine's `~/.claude`, read-only):

- Code rounds since 2026-09-30: **398**, each with a UUID `rounds.caller` (the caller's Claude session id); **21**
  distinct callers. The caller's transcript is on disk for **20 of 21** callers, covering **392 of 398** rounds; **18**
  have `subagents/*.meta.json`. Oldest transcript: 2026-09-25 (`cleanupPeriodDays` is now 90).
- Callers since 2026-09-30: `claude-code` 648 rounds, `coai-call` 21, blank 20, `claude-code-stdio-shim` 17, `codex`
  10. Declared `caller_model`: `claude-opus-5-5` 644, `claude-opus-5.5` 18, blank 32, `claude-fable-5-1` 22 — the
  declaration is spelled two ways; the transcript's `message.model` is not.
- The WSL store and WSL transcripts were **not** measured (the WAL store is not opened across the drvfs boundary —
  `src_vs_code/src/roundCountsRead.ts:18-21`); step 0 measures them from inside WSL.

## 2. What exists, and how much of it is reused

| Piece | Where | Reused as |
|---|---|---|
| Caller identity | `src_mcp/src/Server/CallerSessions.cs:44-53` | NOT the transcript key: its ladder puts the stated `COAI_CALLER_SESSION` first, so the reader has its own (§4, §16 D9). `CallerSessions.cs:11-14` is explicit that the caller's session is NOT the gate's repo+branch session |
| `agent_log` | `src_mcp/src/Store/AgentLog.cs:56-67` | **not reused as a source.** It selects by `cwd` and falls back to the busiest transcript on the machine (`:64-71`), renders text, and caps at 400 entries / 256 KiB (`:30-36`) — as a measurement it would invent attribution, undercount, and break the no-text rule. Only its text-free helpers move to a shared unit: the day-substring gate (`:103-112,157-173`) and the shared-mode, torn-line-tolerant read (`:183-207,227-230`) |
| The round hook | `src_mcp/src/Server/Rounds/RoundEngine.cs:605-625` | where the projection write records the round; the authors are computed just before it |
| Transcript dir override | `src_mcp/src/Server/PanelSettings.cs:386,972` | `COAI_AGENT_LOG_DIR` → `AgentLogDir` |
| The diff | `PanelService.cs:756-757` → `src_mcp/runners/Context/ContextAssembler.cs:311-330` | `git diff --numstat -z {against}..{sha}` minus `DiffExclusions.Default` (`ContextAssembler.cs:7-30`) at the pinned resolved base, read with `NumstatReader.Read`, which **drops the counts** |
| Counts | `src_mcp/runners/Context/NumstatReader.cs:41,91,139-147` | `ReadCounted`; a binary's `-` columns read as 0 (`:146-147`), so binaries are a coverage kind, never "0 lines" |
| Over-budget omission | `src_mcp/core/Context/DiffShaper.cs:9-14,30-56` | `ElidedFile` — whole files the reviewers were NOT shown |
| Resolved base to the record | `PanelService.cs:868` → `RoundWork.cs:30` → `RoundEngine.cs:615-625` → `RoundContext.cs:37` | the path a size rides on |
| A nullable snapshot on the round | `SessionStore.cs:193` (`RoundRecord.Caller`) | the precedent for a historical field: absent on old rounds, never back-filled with today's default |
| The write | `src_mcp/src/Store/Projection.cs:24-38`, `RoundsDb.cs:190,849-886,923-945` | best-effort, synchronous, stable upsert; reviewers delete-then-insert per round |
| Schema steps | `src_mcp/src/Store/Schema.cs:31-37` (21 steps, last `:614-616`) | append-only, one transaction per step |
| The gate's count | `src_mcp/core/Gate/GateRule.cs:9-40` | `GatingCount` is AFTER dedup and AFTER discounting standing rejections — one of several numerators, not "the" finding count |
| A one-shot read mode | `src_mcp/src/Server/RoundCountsMode.cs`, `src_mcp/src/Program.cs:338-340` | exit codes 0 / 65 / 74, never 64; read-only open |
| Round card | `src_vs_code/src/rounds.ts:156`, `src_vs_code/src/roundsLog.ts:519` | renders `caller.model` |
| Evidence vs inference; population vs arithmetic; unknown ≠ zero | `dew_flow_benchmark · src/Bench.Domain/Sessions/SessionTrace.cs`, `Sessions/ToolTaxonomy.cs`, `Gate/GatePopulation.cs`, `Gate/GateReport.cs`, `Gate/Figure.cs` (at `4dc98df`) | the conventions are carried over, not referenced: a tool name the taxonomy does not know is `Unknown`, never guessed into a write (its `CommandClassifier`'s treatment of unknown shell commands is NOT carried over); a figure that could not be captured is `Unknown`, not 0; who is in a cohort is decided apart from how it is counted |

**The transcript, as it is on this machine** (Claude Code, read 2026-10-14 from this session's folder; names and
model fields only):

```
~/.claude/projects/<cwd-slug>/<sessionId>.jsonl                       main session
~/.claude/projects/<cwd-slug>/<sessionId>/subagents/agent-<id>.jsonl  one per subagent, nested ones too
~/.claude/projects/<cwd-slug>/<sessionId>/subagents/agent-<id>.meta.json
~/.claude/sessions/<CLAUDE_PID>.json                                  {pid, sessionId, cwd, startedAt, …}
```

- `meta.json`: `agentType`, `description`, `toolUseId` (joins the main transcript's `Agent` tool_use), `spawnDepth`
  (1, or 2 with `parentAgentId`), `requestShape` (`background` on all 84 here), `model` (the ALIAS asked for:
  `sonnet` 60, `opus` 18, `fable` 6).
- Assistant lines: `message.model` (observed id), `requestedModel`, `message.usage` (four terms), `message.id`,
  `effort`, `isSidechain`, `agentId`, `cwd`, `timestamp`.
- **A message is several lines**: 1 992 assistant lines carried 1 041 distinct `message.id`s.
- 82 `Agent` tool_uses in the main transcript, 84 metas (two launched by subagents) — a free consistency check.
- A subagent's `cwd` is the SESSION's (`d:\rsd\ClaudeRag`) even while it edits `D:/rsd/_wt/…`.
- `--no-session-persistence` writes no transcript at all.

## 3. Out of scope, and the neighbours

- **Codex and Gemini callers**: `unobserved: no reader for <vendor>` (10 + 0 rounds since 09-30). A reader per
  vendor is a later plan behind the same `ObservedSession` record.
- **The Team server** runs reviewers, never callers. What a remote reviewer actually ran is
  [PLAN_the_log_names_the_model.md](PLAN_the_log_names_the_model.md)'s; this plan records the requested model and
  says so (`SessionStore.cs:17-28`).
- **`agent_log` keeps its behaviour.** Moving it onto the exact session is a separate change.
- **Lines that survive.** A write is evidence of contribution, not of authorship of the reviewed lines; mapping
  transcript edits to `git blame` of the reviewed commit is not attempted.

| Item | Which plan builds it | The other's part |
|---|---|---|
| Rounds per vendor row and window; reviewers' tokens and money | [PLAN_every_round_is_counted.md](PLAN_every_round_is_counted.md) | reads `rounds`/`reviewers`; this plan adds caller-side data it never reads, and one `reviewers.effort` column it may |
| What a reviewer token column means per vendor | [PLAN_usage_that_compares.md](PLAN_usage_that_compares.md) | this plan stores the CALLER's Claude usage in Claude's own four terms and compares no vendors |
| Which model a Team server actually ran | [PLAN_the_log_names_the_model.md](PLAN_the_log_names_the_model.md) | reviewer side; this is the caller side |

None of these is a slice taken from the others, so no reciprocal edit is owed; the table is complete.

## 4. Three identities, kept apart

| Identity | Key | Says |
|---|---|---|
| The caller's session | Claude session id, by the READER's own ladder (§16 D9): `~/.claude/sessions/<CLAUDE_PID>.json`'s `sessionId` (that one file, read by pid — the folder is never listed and its `.key` files never touched), else `CLAUDE_CODE_SESSION_ID`. Not `CallerIdentity.From` (`CallerSessions.cs:44-53`), which puts `COAI_CALLER_SESSION` first: a stated id alone is `unobserved: stated id`, and a `repo:<path>` caller (`RoundCommands.cs:34-35`) is no session id | which transcript to read — nothing about the repository |
| The gate session | `sessions.id`, keyed by repo+branch | which rounds form one review history (`RoundRecord`s, predecessor rounds) |
| Repository attribution | evidence in the transcript that an agent wrote under the gate session's `repo_path` | which observed activity belongs to the change |

One caller session can serve several gate sessions (an orchestrator, epics on their own branches) and one gate
session can be served by several caller sessions (a resumed task, a second window). Nothing joins them except the
recorded evidence; no key stands in for another.

## 5. Observation — what the transcript says, text-free

A new reader (`Store/CallerTranscript.cs` → an `ObservedSession` value), keyed by the session id, never by `cwd`:
the transcript is `<projects>/*/<sessionId>.jsonl` (one directory listing per project folder; no slug is computed —
the slug rule is lossy, `claudeSessions.ts`). Which id source was used is recorded (`authors_id_source`: `registry`,
`environment`, or `stored` for a backfill). The reader takes a lower bound `fromUtc` — the earliest window start
attribution needs — and reads only that slice of every file; the day-substring gate makes the bound cheap (§16 D7).

- **Tokens** are counted once per `message.id`: each of the four terms is the MAXIMUM over that id's lines (outputs
  only grow across the lines of one message; inputs are identical), placed at the id's earliest instant (§16 D8).
- **Launches** are counted once per `agentId` — the first line of `agent-<id>.jsonl`. A resumed agent (more lines in
  the same file, later) is the same launch with later activity, never a second launch.
- **Requested vs observed model** are two fields: `requested` (the meta's alias, else `requestedModel`) and `model`
  (`message.model`). The report groups on observed and shows where they differ.
- **Effort** is the line's own `effort`. Absent is `unknown` — never today's default, never the session's.
- **Parent/child**: `toolUseId` joins a meta to the main `Agent` call; `parentAgentId` joins a nested agent to its
  parent.
- **Writes**: `Edit`, `Write`, `MultiEdit`, `NotebookEdit` — and an `Artifact`-style file writer only when its input
  carries a file path into a checkout — are read for that ONE path property, never anything else in the input. Each
  write is kept per CALL: (agent, instant, checkout root, tool), never a boolean, and `write_calls` counts them
  (§16 D4, D18). The checkout root is the nearest one above the path; a path under no checkout (a scratchpad, a temp
  file, the assistant's memory folder) is `outside` (§16 D3). `Bash`, `PowerShell` and every other tool are `Unknown`
  and never a write — no guessing from command text. A write call whose `tool_result` is an error, or that has no
  `tool_result` at all, is NOT a write — nothing changed, or nothing is known to have; both are only counted as
  diagnostics (§16 D23, E3.S4's S4-7). An `Agent` (older: `Task`) call refused with an error result and no meta is a
  refused launch, not a missing child.
- **States, all four distinguishable**: `observed` (complete — and an observed session with no subagents is an
  observed ZERO), `partial: <why>` (an `Agent` call with no transcript, a meta without one, the read budget hit),
  `unobserved: <why>` (no session id, a stated id, no reader for the vendor, transcript not found — persistence off is
  one cause), and empty = not recorded (a round from before the step). Reasons are a closed vocabulary (`ObservationReasons`).
  A torn LAST line of a file is a race with the CLI's append, not a loss: it is counted (`Diagnostics.TornTails`) and is
  NOT `partial`; a torn line in the MIDDLE of a file is `partial: torn line` (§16 D22, E3.S4's S4-1).
- **Tests never read the developer's `~/.claude`**: a test-assembly fixture points the home/profile at a temp
  directory for this reader AND for `AgentLog`, whose FakeCli round tests read the real transcripts today
  (`RoundEngine.cs:897`, §16 D17).
- **Never text**: no prompt, `description`, tool input other than the path test, or message text is stored or
  printed. A sentinel test enforces it.

## 6. Attribution — the highest-risk part, and its own epic

Two different questions, kept apart because the consultant's example is real: a cheap model fixing a strong model's
code in round 3 must not relabel the whole cumulative diff as the cheap model's.

**Activity per round.** The caller's observed activity is tiled into non-overlapping windows that every message falls
into exactly once:

| Window | From | To |
|---|---|---|
| `before-open` (first round of a gate session only) | the caller transcript's first line, or the end of the last window this caller session already gave to ANY gate session on the same `repo_path` | the gate session's `open` (`PersistedSession.OpenedUtc`, `SessionStore.cs:248` — never `sessions.opened_utc`, which is the first PROJECTION's clock, `RoundsDb.cs:837`) |
| `round (stage, N)` | the previous RECORDED round's **start**, of any stage (or `open` for the first) | this round's start |

Windows are half-open `[from, to)` and tiled across ALL stages of one gate session in time order; round numbers are
per stage, so a round is always named `(stage, number)` (§16 D2). Skipped and interrupted rounds open no window and are
never a delta predecessor — a skip is written `done` WITH a sha (`RoundSkips.cs:95-111`), an interrupted round is swept
past the attribution call site (`SessionStore.cs:578-597`) — so their stretch lands in the next recorded round
(§16 D1). **Evidence cutoff**: only transcript lines stamped before round N's start are evidence for round N, live and
backfill alike, so the two agree (§16 D6). A PEER gate session's claim (below) is judged on the peer's own window and
the evidence the peer itself had — which may run past this round's start but always ends before the peer completed,
hence before this round's live read. Instants are compared in UTC; the transcript's clock is the client's, the round's
the server's, the clock source is recorded, and skew is a stated limitation — no tolerance is guessed.

Starting at the previous round's START, not its completion (which is what `agent_log`'s window does,
`RoundEngine.cs:893-902`), is the fix for the gap the consultant named: background work that ran while round N−1
was being reviewed belongs to round N's window instead of to none. `before-open` makes work done before the gate was
opened visible as its own kind, so the report can include or exclude it instead of losing it.

**Association**, per agent in a window — the first that holds:

1. **wrote here** — a write whose checkout root is at or under `repo_path` (a submodule or a worktree under it counts
   as here, §16 D3). For ASSOCIATION only: the SIZE covers just the gate session's own checkout (`repo_path`'s
   toplevel), so a write into any OTHER checkout root under `repo_path` — a registered submodule, or a nested worktree —
   is `wrote here` but its lines are not in this branch's diff, and the round says so (§7, *Only the gate's own
   checkout is sized*).
2. **child of an associated agent** — via `parentAgentId` (and `toolUseId`), and only through a SUBAGENT parent that
   is `wrote here` or `child of` in the same window; `main`'s association never propagates (E4.S2, decision 5).
3. **wrote elsewhere only** — a write into ANOTHER checkout; counted for that repository, not here. A write `outside`
   every checkout is neither evidence for nor against.
4. **no evidence** — `cwd`, launch time and agent type alone associate nothing. If this caller session wrote into
   exactly ONE checkout in the window and it is this one, the agent is associated `same-session`; otherwise it is
   **ambiguous**.

**Never duplicated** (§16 D5, made exact by E4.S2): an agent that wrote into this repository AND another in one window
is `ambiguous` (rung `two repositories`) and gets a `wrote here` ROW carrying its `write_calls` into this repository
with ZERO turns and tokens, so it is a contributor of each repository it wrote into while its messages are owned ONCE —
here, in the ambiguous totals, or by the peer that ranks first. **Ownership across gate sessions is by completion
rank** (E4.S2 decision 3, which REPLACED the brief's earliest-start candidate): every window of this gate and of every
peer served by the same caller is ranked by its round's `CompletedUtc` (then gate id, then `before-open` first); an
item owned here is instead `claimed by peer` when an earlier-ranked peer window containing its instant would own it —
because the store learns of rounds in completion order, which is what lets a live record and a backfill agree. The
association words are therefore six: `wrote here`, `child of`, `same-session`, `ambiguous`, `wrote elsewhere`,
`claimed by peer`. The main session is associated by the same rules
(`who = main`). Attribution lives in `src_mcp/core/Authors/`, where `ArchitectureTests.cs:17-24` enforces purity
(§16 D20).

**Contributors to the change under review** are derived, not stored a second time: the union of the associated
activity of every window of the gate session up to and including round N, of every stage, each tagged with its
`(stage, number)` — plan-stage activity is not hidden; the report groups by the tag (§16 D2). One implementation
(E4.S4); the report calls it over stored rows. A first code round's contributors are its own windows; a corrective
round lists the original authors AND the fixers, with `delta_*` (§7) sizing what the fixers changed.

**Acceptance cases** (each a fixture from step 0, which carries the round data in a `rounds.json` beside the
transcript): two repositories served by one caller session; background work spanning two rounds; a model switch
inside one window; a resumed agent; missing child transcripts; a second gate session of the same caller; work before
`open`; a skipped round between two recorded ones; and a replay — the same round recorded twice, or recorded live and
then backfilled — adding nothing.

## 7. Diff size — from the numstat, never from patch text

`CollectAsync` switches to `ReadCounted` (`ContextAssembler.cs:314`) and keeps the counts on `CollectedDiff`
(`ContextAssembler.cs:119`) — free, and the feature stage (`FeatureStage.cs:241`) pays nothing more. The extra numstats
are separate methods, and the `ChangeSize` is assembled AFTER shaping, because `elided` exists only once
`DiffShaper.Shape` has run (`PanelService.cs:757`); it is set on the finished record (`RoundEngine.cs:483-491`), where
`RecordRound` reads it as it reads `Caller`. Every file lands in exactly one **coverage** and one **kind**:

| Coverage | Meaning | Lines |
|---|---|---|
| `shown` | in the reviewers' diff whole | counted |
| `elided` | over the shaper's budget, named but not shown (`DiffShaper.cs:30-56`) | counted, so the report can drop rounds with elision |
| `excluded` | matched `DiffExclusions.Default` — one more numstat without the exclusions, the call `ReviewableAsync` already makes (`ContextAssembler.cs:359`) | counted, reported apart, never in the denominator |
| `binary` | numstat `-` | **no line count** — files only |

| Kind | Rule (one pure function, first match) |
|---|---|
| `test` | a path segment `test`/`tests`/`__tests__`, or a name `*.test.*`, `*.spec.*`, `*Tests.cs`, `test_*` |
| `docs` | `*.md`, `*.txt`, or under `research/`, `todo/`, `docs/` |
| `config` | `*.json`, `*.yml`, `*.yaml`, `*.toml`, `*.props`, `*.targets`, `*.csproj`, `*.slnx`, `*.config`, dotfiles |
| `code` | any other extension `OutlineLanguages.Of` maps (the one mapping; `.ps1`, `.sh`, `.razor`, `.html`, `.css` are therefore `unknown`, and the report prints that) |
| `unknown` | everything else |

Two scopes, both kept:

- **`whole`** — `{against}..{sha}`, what the round reviewed.
- **`delta`** — `{prevSha}..{sha}`, a diff between the two heads, against the previous applicable round: the gate
  session's last CODE round with status `done`, a `Sha`, and a verdict that is not `skipped` (`SessionStore.cs:96-104`,
  `RoundSkips.cs:102-111`; §16 D1). Never a subtraction of two cumulative totals, which measures nothing.
  `unavailable: first round` / `: no predecessor sha` / `: not an ancestor` (a rebase, checked with
  `merge-base --is-ancestor`) / `: git failed` / `: numstats disagree` are explicit. The last is the partition's own
  refusal (E2.S1): when the filtered and the unfiltered numstat cannot be matched file for file, the whole SCOPE is
  unavailable — a partition wrong about one file is not known to be right about the rest — and the round logs one
  fixed sentence, no path, at Warning.

**Only the gate's own checkout is sized** (plan gate finding 0, accepted 2026-10-14). The numstat is the parent
repository's: a change inside a submodule appears there as ONE gitlink line (`Subproject commit …`), and a nested
worktree that is not a submodule does not appear at all unless it is the same branch. So: the size records changed
gitlinks AS gitlinks — a count, `rounds.size_gitlinks` (E2.S4, from `git diff --raw` entries of mode `160000`; `-1` =
not recorded), never their one line as code; and the authors record two counts of `wrote here` write calls whose
checkout root is NOT `repo_path`'s toplevel — `submodule_writes` (the root is a submodule registered in the checkout's
`.gitmodules`) and `uncovered_writes` (every such call, submodules included). A round with `uncovered_writes > 0` is
marked `size not covered`: the report excludes it from per-100-line figures by default and counts it
(`--with-uncovered` takes it in), and the card says so.

Stored as rows `round_changes(round_id, scope, coverage, kind, files, added, removed)` plus four state columns on
`rounds` — `size_state` (`''` = not recorded), `delta_state`, `delta_from` (the predecessor's sha) and `size_origin`
(`live`, or `backfill` for a size reconstructed later with TODAY's exclusion list and diff budget, not the round's;
§16 D12) — and ALSO as a nullable `Size` on `RoundRecord` (the `Caller` precedent, `SessionStore.cs:193`), because the
database is a projection of the session files (`Schema.cs:7-10`) and a size is small enough to belong in the source.
`reviewers.effort` arrives in the same step with an explicit `not recorded` default for every row written before it,
distinct from `''` = no effort applied (`SessionStore.cs:29-37`; §16 D11).

**The authors follow the same rule** (decided 2026-10-14, after the epic split found the gap): a nullable `Authors`
snapshot on `RoundRecord` — state, the session id read, the reader version, the ambiguous totals, and one entry per
`round_authors` row (§11 step 4's fields: ids, words from a closed set, counts and tokens; no text) — written into the
session file with the round, and `round_authors` is projected FROM it. A projection-only table would be lost on the
rebuild `Schema.cs:7-10` reserves the right to do, and a rebuilt round whose transcript is gone could never be
recovered by backfill. So the read and attribution run before the round is SAVED (`RoundEngine.cs:586`), not merely
before the projection; absent (null) on every round older than the field, which reads *not recorded*; backfill (§9)
writes the snapshot through the session store as well as the rows. Every snapshot write goes under `SessionClaim`; a
live re-record carrying a null snapshot never erases authors already in the database; and the snapshot is STRIPPED
from the `open`/`status` replies (`PanelService.cs:1814-1822` sends `session.Rounds` to the calling AI — several KB of
its context per round otherwise) (§16 D13, D14). Snapshots are taken for `PlanReview` and `CodeReview` rounds the engine
finishes; skipped rounds and other stages read *not recorded*. **At merge: not recorded** —
the last code round's `whole` is the branch over its base; what lands after it is review fixes. Feature rounds
(`FeatureStage.cs:257`) are left for later.

## 8. The report — `coai-mcp --round-authors`, after live attribution, before backfill

```
coai-mcp --round-authors [--since <utc>] [--until <utc>] [--split <utc>] [--stage code|plan]
                         [--cohort first|corrective|all] [--with-before-open] [--add <file.json>] [--table]
```

- **Population is selected apart from the arithmetic** (the benchmark's `GatePopulation`/`GateReport` split): one
  unit decides which rounds are in a cohort and why each excluded one is out; another counts.
- **Cohorts**: `first` (the first code round of each gate session — new code, the default) and `corrective` (later
  rounds — exposures of a cumulative diff), never pooled.
- **Cells** by period, **coder mix** (families of `wrote here` subagents among the contributors: `sonnet`, `opus`,
  `fable`, `mixed`, `main only`, `no write observed`, plus the coders' effort, `unknown` included), the main model, and
  the **reviewer set** as (provider, model, effort) for reviewers that answered — with failed and partial reviewers
  counted beside it. Contributors come from E4.S4's one implementation, never a second union in the report; a model
  id's family and display name come from ONE vectors file, `shared/model-display-vectors.json`, read by both the card
  (TypeScript) and the report (C#) (§16 D19). A reviewer row whose effort is `not recorded` is shown as such, apart
  from `none` (§16 D11).
- **Numerators, named apart, each with its total and its coverage** (§16 D10): all deduplicated findings (the stored
  rows); **`severe`** = findings with `is_gating` (blocking or major, BEFORE the discount, `Finding.cs:84`);
  **`gate_count`** = `rounds.gating`, the gate's own count AFTER dedup and the standing-rejection discount
  (`GateRule.cs:32-37`); re-raised; accepted; unresolved. No column, header or test ever calls both "gating".
- **Denominator**: `shown + elided` lines of `code` and `test` kinds by default (flags select others); rounds with no
  size, a binary-only change, `unobserved` authors, or `size not covered` (writes into a submodule or another checkout
  under `repo_path`, §7; `--with-uncovered` takes them in) are excluded AND counted. Per-100-line figures are ratios of
  sums. A figure with no population is `Unknown`, printed as such, never 0 (the benchmark's `Figure`).
- **Branch dominance and size**: each cell shows rounds, gate sessions, branches and the largest branch's share;
  under 10 rounds or one branch over 50 % is marked, not hidden.
- **What it can claim**: association inside comparable cohorts. When the coder mix and the reviewer set change
  together, the report cannot separate them and the header says so.
- `--table` prints the table; the default JSON holds only sums and counts, so a WSL run saved to a file and
  `coai-mcp --round-authors --add wsl.json` on Windows print the combined report without opening a store across the
  boundary. Exit 0 / 65 (bad flag, unreadable `--add`) / 74 (unreadable store), never 64. Both modes join
  `.agents/PROJECT.md`'s one-shot list.

The round card gets one line (plan gate finding 2): *written with Opus 5.5 (main) · 3 Sonnet coders · +412 −88 in 9
files*. Coders are DISTINCT `wrote here` subagents (by `agent`), grouped by family through
`shared/model-display-vectors.json`: one family → *N Family coder(s)*; two → *2 Sonnet + 1 Opus coders* (larger group
first, ties by family name); more than two → *N coders (mixed)*; none → *main only*. A partial state appends
*· partial — <why>*, `uncovered_writes > 0` appends *· size not covered*, and an unobserved round reads *authors not
observed — <why>*.

## 9. Backfill — last, source-local, rerunnable

`coai-mcp --backfill-authors [--since <utc>]` runs **the same reader and attribution, tagged with their version**
(`authors_reader`), with the same evidence cutoff (§6), over rounds whose `authors_state` is empty OR was written by
an older reader version; a round whose `caller` is not a session id (a stated id, `repo:<path>`, another vendor) is
written `unobserved: <why>`, so every round ends with a state. It reruns the same numstats when `repo_path` still
resolves and both shas exist, and marks that size `size_origin = backfill`. The open time comes from the session file
(§6), never from `sessions.opened_utc`. The live path's 2 s wall clock is not applied (it measures machine load, not
the round); the byte cap is. Each side backfills its own store from its own transcripts. It never lowers a state's
strength, never overwrites an `observed`/`partial` row produced by a newer or equal reader version, and never touches
findings or their resolutions: it NEVER calls `RecordRound`/`RecordRoundRow` (`RoundsDb.cs:190-202,846-868`), which
rewrites the round row, re-inserts reviewers and deletes findings past the count it is handed — a narrow writer
writes only the authors and size columns, the round's `round_authors`/`round_changes` rows, and the session file's
snapshot through the session store under `SessionClaim` (§16 D14). **The session file is written FIRST** (plan gate
finding 3): under the claim, the snapshot is saved and verified by a reload; only then is the projection written FROM
that snapshot. A rerun reconciles: a round whose snapshot exists but whose database authors are missing or of an older
reader is re-projected from the snapshot without reading the transcript again; a run killed between the two writes is
completed by the next one with identical rows. Exit codes: **0** done (nothing to do included), **65** a bad argument,
**74** a store or I/O error, **75** (`EX_TEMPFAIL`) when at least one busy session was deferred to the next run —
never 64. It ships in a RELEASE first; the backfill run and
its RESULTS are a second, docs-and-data commit, and the WSL side needs its own build — NativeAOT does not
cross-compile Windows → Linux (§16 D16). **Acceptance: a second run changes no totals.** What it
cannot recover is recorded per round: a deleted transcript or worktree, a rebased-away commit, a session id the
server inherited stale (the registry read is not possible after the fact). Rounds before 2026-09-25 on Windows are
gone for good. The report does not wait for it.

## 10. The question consultant's advice, checked (2026-10-14)

| Point | Verdict | Evidence |
|---|---|---|
| 1 `agent_log` must not be the source | adopted | `AgentLog.cs:64-71` (cwd, busiest-transcript fallback), `:30-36` (caps), `:246-287` (text) |
| 2 three identities | adopted (§4) | `CallerSessions.cs:11-14` |
| 3 numstat at the pinned base; coverage kinds; head-to-head delta | adopted (§7) | `ContextAssembler.cs:311-330`, `NumstatReader.cs:146-147`, `DiffShaper.cs:30-56`. The cited `ContextAssembler ~:212` is the WORKING-TREE numstat (`:203`), not the code round's — the round's is `:311` |
| 4 observation rules; nullable historical snapshot | adopted (§5, §7) | `SessionStore.cs:193` |
| 5 attribution as its own epic | adopted (§6). **Correction**: the window is `RoundEngine.cs:893-902`, not `PanelService ~:2648` (`PanelService.cs` is 1 849 lines) | — |
| 6 "reviewer model and effort are not persisted" | **half right**: the model IS persisted since step 20 (`RoundsDb.cs:930-944`); the effort is not → `reviewers.effort` joins the first schema step. Requested ≠ ran on a Team server: adopted as a stated limit (`SessionStore.cs:17-28`). Numerators, cohorts, population/arithmetic split: adopted (§8) | `GateRule.cs:32-37` |
| 7 backfill last, versioned, idempotent | adopted (§9) | `RoundsDb.cs:849-886` |
| 8 fixtures from the spike before the schema | adopted (step 0) — for the AUTHORS schema (step 23); step 22 derives from git numstat and may land first | — |

## 11. Build order

0. **Spike: measure, and cut the fixtures.** On Windows and inside WSL: the layout of §2, `meta.json` and `effort`
   presence per Claude Code version on disk, message-id repetition, `/clear` and `--resume` against the registry and
   the inherited variable, transcript coverage of each store's code rounds, WHOLE-session sizes (main + subagents —
   one real session measured 14.8 MB main + 63 MB subagents, §16 D7) and the read time over them. The fixtures gate the
   AUTHORS schema — step 23 (E5) gives no field a meaning before they exist; step 22 (the size, E2) derives from git
   numstat alone and may land before E1 (plan gate finding 4, rejected as a dependency, made explicit). Predictions written
   first (`measurement.md` rule 4) for what is still unknown — WSL coverage, read time; Windows coverage (392 / 398)
   was already observed and is recorded as an observation. The read time is Node's; E5 re-measures the C# reader
   before fixing its budget. Output: a `research/RESULTS_` file AND sanitised fixtures under
   `src_mcp/tests/fixtures/caller-transcripts/` (LF pinned by `.gitattributes`), each with a `rounds.json`: the union of
   §6's acceptance cases and §12's reader cases. "Text-free" means a shape-checked ALLOWLIST: ids, instants, model
   ids, enums and fixture-rooted paths keep their shape, every other string is the sentinel. Write paths are
   re-pointable (under fixture roots) and instants shiftable by one offset, so E5 can run a round end to end. The
   `/clear` and `--resume` probes are attended work by the main session.
1. **Diff size.** `ReadCounted`, `ChangeSize`, the kind function, the exclusions numstat, the delta numstat,
   `RoundRecord.Size`, schema step 22 (`round_changes`, `size_state`/`delta_state`/`delta_from`/`size_origin`,
   `size_gitlinks`, `reviewers.effort` with its `not recorded` default), written through a new table unit (`RoundsDb.cs` and
   `RoundsQuery.cs` are past 800 lines and gain call lines only), `--log` emits the size. Ships alone.
2. **Observation.** `CallerTranscript` → `ObservedSession`, the shared read helpers lifted out of `AgentLog.cs`, the
   reader's own id ladder, the lower bound `fromUtc`, the per-call writes, the home fixture for tests.
3. **Attribution.** `RoundAttribution` in `src_mcp/core/Authors/` over `ObservedSession` + the gate session's rounds:
   windows, association, ambiguous totals, contributors. Pure; every acceptance case of §6 is a test.
4. **Store and show.** Schema step 23 (`round_authors` with `who`, `agent` (the opaque agent id, empty for `main` —
   "3 Sonnet coders" counts distinct agents, and contributors deduplicate on it), `requested`, `model`, `effort`,
   `agent_type`, `association`, `window` (the round's `(stage, number)` is its `round_id`'s), `launches`, `turns`, the four token terms,
   `write_calls`; `rounds.authors_state`, `authors_session`, `authors_id_source`, `authors_reader`,
   `submodule_writes`, `uncovered_writes` (§7), the ambiguous
   totals, and an index on `authors_session` for the peer lookup E4.S2 needs on EVERY round — peers built from their
   session files; `partial: prior windows unknown` when the lookup cannot answer), all projected from the nullable
   `RoundRecord.Authors` snapshot (§7), which is computed before the round is saved (`RoundEngine.cs:586`) — so
   before `Projection.Write` opens the database too — capped at 2 s / 64 MiB over the `fromUtc` slice →
   `partial: read budget`. New code in new files; `RoundEngine.cs` (910 lines) and `PanelService.cs` (1 849) gain call
   sites only. The card line (this round's own windows; the `delta` size, else `whole`).
5. **Report** (§8), on live rounds.
6. **Backfill** (§9), released first, then run once per side (twice, to prove the second run changes nothing).
7. **Validate, then answer.** Hand-read 10 rounds per side against transcripts and diffs (`measurement.md` rule 2);
   then the before/after table into a `RESULTS_` file with reviewer set, effort, cohort and branch share as columns.

## 12. Test plan

| Test | Proves |
|---|---|
| Main session with a model switch mid-window; every message as three lines | per-model rows; tokens once per `message.id` |
| Foreground Sonnet writer into the repo; background Opus reader across a round start; a nested agent; a resumed agent | `wrote here` / `child of`; the straddler's tokens split by timestamp into the two windows, ONE launch; resume is not a launch |
| A subagent writing only into another repository; one with no evidence while the session wrote into two repositories | credited elsewhere; ambiguous totals, no row |
| Background work during round N−1's review | lands in round N's window, not in none |
| Work before `open` | `before-open`, excluded from the report unless asked |
| A meta without a transcript; an `Agent` call with neither; a torn MIDDLE line; a meta with no `model` | `partial` naming each; read on; observed model from the lines |
| A torn LAST line | NOT partial: `Diagnostics.TornTails` counts it, the state is unchanged (§16 D22) |
| A write call whose result is an error, or that has no result | not a write; counted as a diagnostic (§16 D23) |
| Two gate sessions of one caller whose windows overlap | an item is owned once: by the earlier-COMPLETED window, `claimed by peer` in the other; a two-repository agent has a `wrote here` row with its write calls and zero tokens |
| Persistence off; `COAI_CALLER_SESSION` only; a `repo:<path>` caller; a codex caller | `unobserved: transcript not found` / `stated id` / `no session id` / `no reader for codex`; `caller_model` untouched |
| A skipped round between two recorded ones; an interrupted round | neither opens a window nor is a delta predecessor; their stretch lands in the next recorded round |
| Lines stamped after round N's start; a plan round's window before code round 1 | not evidence for N; plan-stage contributors present, tagged `(PlanReview, n)` |
| A write into a submodule under `repo_path`; into the scratchpad; into another checkout | `wrote here` / `outside`, no evidence / `wrote elsewhere` |
| A refused `Agent` call (error result, no meta) | not a launch, not `partial` |
| A test run on a developer machine | no file under the real `~/.claude` is opened (the home fixture) |
| Registry naming a different session than the variable; an unreadable registry | the registry wins and the record says which |
| No `effort` on the lines | `unknown`, not `high` |
| Sentinels in every text field | absent from the session file, `--log`, `--round-authors`, and from the `.db` bytes once `rounds.agent_log` — which keeps transcript TEXT today by design — is blanked; that column is the positive control, seen carrying the sentinel FIRST (§16 D13) |
| `open`/`status` over a session whose rounds carry snapshots | no `authors` key in the reply; the file keeps it |
| The same round recorded twice | identical rows, no doubled totals |
| **Migration**: a step-21 database opens at 23; old rows read *not recorded*; read-only `--log`/`--round-counts` on a step-21 file still answer | the append-only contract |
| **Diff size** on a temp git repo: text edit, binary, rename, lock file, test/docs/config file, an elided file, a second round after one commit, a second round after a rebase | coverage and kind per file; binaries have no lines; `delta` is the head-to-head increment; `unavailable: not an ancestor` |
| **Submodules and nested checkouts**: a write into a registered submodule under `repo_path`, one into a nested non-submodule worktree, and the submodule's gitlink changed in the diff | both writes `wrote here`; `submodule_writes = 1`, `uncovered_writes = 2`; `size_gitlinks = 1` and the gitlink's line in no `code` cell; the round marked `size not covered`, excluded from per-100-line figures by default and counted, included under `--with-uncovered`; the card says *size not covered* |
| **Card line** over coder mixes | one family, two families (*2 Sonnet + 1 Opus coders*), three (*3 coders (mixed)*), none (*main only*); coders counted as distinct agents, a straddler once |
| **Backfill interrupted** between the session-file save and the projection | the next run re-projects from the snapshot without reading the transcript; rows identical to an uninterrupted run |
| **Report** over a seeded store: known mixes, two reviewer sets, failed reviewers, one dominant branch, unknown sizes, first and corrective rounds | exact cells; ratios of sums; `Unknown` not 0; exclusions counted; cohorts never pooled; marks; `--add` of two halves = the union |
| Report exit codes | 0 on an empty store, 65, 74, never 64 |
| Backfill twice, over a store with one `observed` row of a newer reader | the second run changes no totals; the newer row and every resolution survive; reviewers and findings rows are byte-identical before and after |
| A live re-record with a null snapshot over a backfilled round | the database's authors survive |
| The card, run as a page (`bundledPage.test.ts` harness) | the line for observed / partial / unobserved, none for a pre-step round |

All through the MTP executables (`CoaiMcp.Tests.exe`) and the extension's `npm test`.

## 13. Risks

| Risk | Answer |
|---|---|
| The transcript layout, `meta.json` and `effort` are not a documented contract | step 0 records the version; every field optional; the `Agent`-count check turns a silent loss into `partial`; `authors_reader` versions every row |
| Edits through `Bash` | that agent is `no evidence` → `same-session` or ambiguous; never a guessed write |
| Two sessions, or a human, commit to one branch | the diff counts their lines; the authors only what was observed; the report header says so |
| The reader's cost on every round's answer path (`Projection.Write` is synchronous) | read before the database opens, 2 s ceiling, measured in step 0 |
| A Windows client running a WSL server | `unobserved: transcript not found` unless `COAI_AGENT_LOG_DIR` and `WSLENV` carry the path and the id |
| Clock skew between the client's transcript and the server's round start | compared in UTC, clock source recorded, no tolerance guessed; E1 reports the skew it can see |
| `open` and the startup orphan sweep save session files without `SessionClaim` (`PanelService.cs:492-506`, `SessionStore.cs:586-600`) | snapshot writers take the claim; the backfill verifies after writing and heals a lost snapshot on its next run; locking `open` is a separate change |
| Confounders remain: task mix, coincident coder and reviewer changes | cohort, reviewer set, effort and branch share are columns; association only, said in the header |

## 14. Growth

| Surface | Projected size | Who retires it | Interrupted |
|---|---|---|---|
| `round_authors` | ~700 rounds per two weeks on Windows → ~18 000 a year; rows are per AGENT (× model × effort × association × window), so an orchestrator round can hold 10–20; at a typical ≤ 10 rows × ~110 B ≈ 1.1 KB a round → **≤ 20 MB a year per store** (≤ 40 MB if every round were an orchestrator's) beside a 72 MB `coai.db` | kept with its round, like `reviewers`; a re-record deletes and rewrites the round's rows | no in-flight state: written in the round's own projection write, or not at all |
| `round_changes` | ≤ 2 scopes × 4 coverages × 5 kinds, typically 3–6 rows × 60 B → ~6 MB a year | with the round | same |
| `RoundRecord.Size` in session files | ~200 B a round | with the session file | same |
| `RoundRecord.Authors` in session files | ~0.4 KB per indented row × 3–10 rows ≈ 1.2–4 KB a round (~1.5 KB typical) × ~18 000 rounds → **22–75 MB a year per side**, beside today's 1 743 session files in 28 MB on Windows (largest 190 KB); a 20-round session file grows 30–80 KB, parsed on every `Load` — and every progress-tick save of a running round rewrites the WHOLE file, so the write cost grows with it (§16 D15) | nothing prunes session files today: **kept for ever**, a decision — they are the source the database is rebuilt from | written with the round's own save under `SessionClaim`, or not at all |
| Backfill | nothing beyond the rows above | — | rerunnable; an interrupted run is resumed by running it again |
| `<dataDir>/backfill-backups/<utc>/` (E7) | one `coai.db` copy plus the session files per WRITING run → ~150–360 MB on Windows over E7's runs | the operator, by hand, after the RESULTS file is committed; the mode never deletes, and nothing is deleted in WSL by an agent | a crashed run leaves its folder; the next run makes its own |

## 15. Owner questions

1. **A self-report for non-Claude callers?** Decided *no*: it needs a shared rule in every consuming repository for
   ~1.4 % of rounds, and an AI's memory of its own launches does not survive compaction. If codex or gemini become
   regular callers, the answer is a transcript reader for that vendor.

## 16. Decisions after the stories split (2026-10-14)

The seven stories documents (`_E1` … `_E7`, linked under `## Epics`) each listed the defects they found in this plan.
The main session settled them; the text above is aligned with every row. "E<n>/k" names defect or point k of that
document's own defects section (E6 has none; its §0 decisions D1–D16 played that part).

| # | Decision | Reason | Settles |
|---|---|---|---|
| D1 | Skipped and interrupted rounds never open a window and are never a delta predecessor; activity before them lands in the next RECORDED round of the same gate session | a skip is written `done` with a sha (`RoundSkips.cs:95-111`) and reviewed nothing; an interrupted round is swept past the call site (`SessionStore.cs:578-597`) | E2/1, E4/7, E5/8 |
| D2 | Windows tile ALL stages of one gate session in time order; contributors of code round N = the union of every window up to N, each tagged with its stage; the report groups by the tag; rounds are always `(stage, number)` | the coding between plan `proceed` and code round 1 sits in a plan-tiled window; hiding it would lose the main authorship stretch | E4/9, E6 D2 |
| D3 | A write is `here` when the nearest checkout root of its path is at or under `repo_path` (submodules and worktrees under it count); a path under no checkout is `outside` — never a second repository, never evidence; a write into another checkout is `elsewhere`. **Gate finding 0:** `here` is for association only — the size covers `repo_path`'s own checkout, so writes into a submodule or nested checkout are counted (`submodule_writes`, `uncovered_writes`) and mark the round `size not covered`; changed gitlinks are counted as `size_gitlinks`, never sized as code | scratchpad, memory and temp writes are constant; counted as repositories, `same-session` would almost never apply | E3/4, E3/5, E4/2 |
| D4 | Writes are kept per call — (agent, instant, checkout root, tool) — never as a boolean; `write_calls` counts them | association is per window; a session-wide boolean associates an agent with a window where it wrote nothing | E3/3, E4/1, E5/4 |
| D5 | Never duplicated: an agent writing into two repositories, or activity two gate sessions could claim, goes to the ambiguous totals of each candidate as a COUNT of ambiguous items; its tokens are never added twice. **Made exact by E4.S2 (decision 4):** the two-repository agent is `ambiguous` with a `wrote here` ROW holding its write calls into this repository and ZERO turns and tokens; its messages are owned once — here or by the first-ranked peer (`claimed by peer`, E4.S3's `ClaimedByPeer` total) | double counting is the one error with no symptom; the row keeps authorship without adding a token | E4/3, E4/5 |
| D6 | Evidence cutoff: only lines stamped before round N's start are evidence for N, live and backfill alike; compared in UTC, clock source recorded, skew a stated limitation. **Refined by E4.S2 (decision 7):** a PEER's claim is judged on the peer's own window and evidence, which may end after N's start but always before the peer completed | otherwise a live record and a backfill of one round differ, and "a second backfill changes nothing" cannot see it; truncating a peer's evidence at N's start would let one message land in two rows | E4/10, E4/13 |
| D7 | The reader takes a lower bound `fromUtc` and reads only that slice; the 64 MiB / 2 s budget applies to the slice; E1 measures whole-session sizes | one real session is 14.8 MB main + 63 MB subagents; a whole read ends `partial: read budget` on exactly the orchestrator sessions the question is about | E3/1, E3/10 |
| D8 | Tokens per `message.id`: the per-term MAXIMUM across that id's lines | outputs only grow across a message's lines; inputs are identical (162 of ≈ 2 400 ids differ) | E3/2 |
| D9 | The reader's own id ladder: `~/.claude/sessions/<CLAUDE_PID>.json` (read by pid; the folder is never listed, `.key` files never touched) → `CLAUDE_CODE_SESSION_ID`; `COAI_CALLER_SESSION` alone is `unobserved: stated id`; `repo:<path>` is no session id; new column `authors_id_source` | `CallerIdentity.From` puts the stated id first; a stated id names no transcript | E1/6, E1/7, E3/7, E3/8, E5/3, E5/11 |
| D10 | Numerators named apart: `gate_count` = `rounds.gating` (after the standing-rejection discount, `GateRule.cs:32-37`) and `severe` = findings with `is_gating` (blocking/major, before it, `Finding.cs:84`); never one word for both | E6's "gating" column was pre-discount while the gate's own count is post-discount | E6 D8 |
| D11 | `reviewers.effort` gets an explicit `not recorded` default for rows before step 22, distinct from `''` = no effort applied | codex, the only reviewer since 2026-10-11, applies none (`RosterBuilder.cs:705-712`); without a marker the two read alike | E2/4, E6 D7 |
| D12 | `rounds.size_state`, `delta_state`, `delta_from` (E2's C2) are adopted; a backfilled size carries `size_origin = backfill` (today's exclusion list and diff budget, not the round's) | an observed zero must not read like "unavailable"; a reconstruction must say so | E2/3, E7/7 |
| D13 | The privacy test blanks `rounds.agent_log` (which keeps transcript TEXT today) before checking the `.db` bytes, positive control first; the snapshot is stripped from `open`/`status` replies | `agent_log` keeps its behaviour by design (§3); the replies send `session.Rounds` to the calling AI (`PanelService.cs:1814-1822`) | E5/1, E5/7 |
| D14 | Session-file snapshot writes go under `SessionClaim`; a live re-record with a null snapshot never erases existing DB authors (E2.S3 applies the same rule to a null `Size`, so a backfilled size survives too); the backfill writes the snapshot through the session store too — FIRST, saved and verified under the claim, the projection second and FROM the snapshot, so a rerun re-projects a round whose snapshot exists without re-reading the transcript (gate finding 3) — and NEVER through `RecordRound`/`RecordRoundRow` — a narrow writer | `RecordRound` rewrites reviewers and deletes findings (`RoundsDb.cs:925,960`); a projection-only backfill would be lost on a rebuild | E5/9, E7/6, E7/8 |
| D15 | Growth: the snapshot row in §14 (22–75 MB a year a side; progress-tick saves rewrite the whole file — stated) | the decision put authors into the session file | E5/6 |
| D16 | E7: release first (normal flow), then the backfill + RESULTS as a second, docs-and-data commit; WSL needs its own build (no NativeAOT cross-compile); the operation steps say so | the operation needs a binary containing the epic on BOTH sides | E7/1 |
| D17 | Tests never read the developer's `~/.claude`: a test-assembly fixture points home/profile at a temp dir for the new reader AND for `AgentLog` | FakeCli round tests slice the real `~/.claude/projects` today (`RoundEngine.cs:897`) | E5/12 |
| D18 | Write tools: `Edit`, `Write`, `MultiEdit`, `NotebookEdit`, plus `Artifact`-style file writers only when they carry a file path into a checkout; `Bash`/`PowerShell` never count — no guessing from command text | evidence, not inference (the benchmark's taxonomy, minus its unknown-command heuristic) | E4/12 |
| D19 | One shared vectors file, `shared/model-display-vectors.json`, read by both the TS card and the C# report, for model family and display | two family rules in two languages drift (the `shared/path-family-vectors.json` precedent) | E5/14, E6 D4 |
| D20 | Attribution lives in `src_mcp/core/Authors/` | purity is enforced only in the core (`ArchitectureTests.cs:17-24`) | E4/11 |
| D21 | Ledger: `Cornerstones: 3 of 3 — E2.S1, E3.S4, E4.S2` | the three areas Fable named, one story each, Q = ⌈2·7/5⌉ = 3 | `## Epics` |

**The writers' own resolutions, accepted** (no decision above overrides them):

| From | Resolution |
|---|---|
| E1/1–3, 9 | the fixture set is the union of §6's and §12's cases plus a `rounds.json` per fixture; "text-free" is a shape-checked allowlist; fixtures under `src_mcp/tests/fixtures/caller-transcripts/`, LF by `.gitattributes` |
| E1/4, E1/5, E1/8 | Windows coverage is an observation, not a prediction; Node's read time is not the C# reader's — E5 re-measures; the session probes are attended |
| E2/2, E2/5 | counts stay on `CollectedDiff`; the size is assembled after shaping and set on the finished record (`RoundEngine.cs:483-491`) |
| E2/6, E2/7, E2/8 | the step-count test relaxes to `≥`; `code` = `OutlineLanguages.Of`; writers and readers go in new files |
| E3/9 | a refused `Agent`/`Task` call (error result, no meta) is not a launch and not `partial` |
| E3/11 | E3 derives its `partial` cases by mutating E1's fixtures |
| E4/4 | rule 2 propagates through SUBAGENT parents only — KEPT by E4.S2's spec (decision 5) |
| E4/6 | each round reads its OWN caller's transcript; a caller change between rounds is `partial: caller changed` (an empty previous caller `partial: prior caller unknown`) — KEPT by E4.S2's spec (decision 2) |
| E4/8 | `round_authors` carries an opaque `agent` key (rows are per agent) |
| E5 §3 | snapshots for `PlanReview`/`CodeReview` rounds only; the peer lookup (every round, replaced by E4.S2 §S2.8) queries `rounds.authors_session` and answers `partial: prior windows unknown` when it cannot; the card describes this round's windows with the `delta` size, else `whole` |
| E5/10 | no rounds re-projection exists; "rebuild" is tested by deleting `coai.db` and recording from the session file |
| E6 D1, D3–D6, D9, D11–D16 | cohort before exclusion; worst state over contributing rounds; reviewer set = distinct (provider, model, effort) of `done` rows; per-numerator coverage; `--kinds` and `--without-elided`; one exclusion reason per round; periods `[since, until)`; marks; the confounding line; rounding only at render |
| E7/2–5, E7/9 | select empty OR older-reader rows and never lower a state; every non-session caller written `unobserved`; no wall clock in a backfill; the open time from the session file; the hand-check by an independent judge |

**Closed by the cornerstone specs (2026-10-14, Fable):**

| # | Decision | Reason | From |
|---|---|---|---|
| D22 | A torn LAST line is NOT `partial`: counted in `Diagnostics.TornTails`, the state unaffected; any other unparseable line is `partial: torn line`. One named constant, `CallerTranscript.TornTailCountsAsPartial = false`, flipped (with a `ReaderVersion` bump) only if E1.S4 measures tails that are not transient | a torn tail is the CLI's append racing our read, and that line is stamped after the round's start, so under D6 it could never be evidence; marking most live rounds `partial` for it would empty the word | E3.S4 S4-1 (closes E3/6) |
| D23 | A write call whose `tool_result` is an error, or that has no `tool_result`, is not a write; both are counted as diagnostics | a refused or failed `Edit` changed nothing; counting it would make an agent refused every write `wrote here` | E3.S4 S4-7 |
| D24 | Ownership across gate sessions is by COMPLETION rank `(CompletedUtc, gate id, before-open first)`; an item an earlier-ranked peer window would own is `claimed by peer` here. This REPLACES the brief's "earliest-closing by start" candidate | the store learns of rounds in completion order, so only a completion rank is decidable from what the store held at the round's live time — the condition for live = backfill | E4.S2 decision 3 |
| D25 | `unavailable: numstats disagree` is a delta/whole state of its own: the filtered and unfiltered numstats could not be matched file for file, the whole scope is unavailable, one fixed sentence is logged at Warning | a partition wrong about one file is not known to be right about the rest | E2.S1 S1.9 item 7 |

E4.S2's other decisions kept or extended the brief's candidates (its §S2.2 records which); E5.S4 owes E4.S2's §S2.8
hand-over — peers on EVERY round, from the peers' session files, `ReadFrom` before the read, the universe, and
`unobserved: invalid input`.

## Definition of Done

- [ ] Step 0's results file exists with its prediction written first, the WSL side measured, and the fixtures cut
      before the AUTHORS schema (step 23); step 22 may have landed earlier.
- [ ] Writes into a submodule or a nested checkout under `repo_path` are counted and mark the round `size not covered`;
      changed gitlinks are counted, never sized as code.
- [ ] The card line follows §8's coder-mix format (two families, *N coders (mixed)*, *main only*).
- [ ] The backfill saves the snapshot first and re-projects from it on a rerun; its exit codes are 0 / 65 / 74 / 75,
      never 64.
- [ ] A code round records its size by scope, coverage and kind; binaries carry no lines; the delta is a head-to-head
      diff or says why not; a pre-step round reads *not recorded*.
- [ ] `reviewers.effort` is persisted.
- [ ] Every round records `authors_state`; no field is filled by a guess, a default, or `caller_model`.
- [ ] Attribution passes every acceptance case of §6; unallocatable activity is ambiguous, never duplicated.
- [ ] No text is stored or printed — the sentinel test passes, with `agent_log` blanked and seen as the positive control.
- [ ] No test reads the developer's `~/.claude`; the numerators `severe` and `gate_count` are never called by one name.
- [ ] `--round-authors` separates population from arithmetic, cohorts, numerators and coverage, prints `Unknown`
      as unknown, marks small and dominated cells, and two sides combine through `--add`.
- [ ] `--backfill-authors` ran on both sides; a second run changed nothing; unrecoverable rounds are counted per reason.
- [ ] Ten rounds per side were hand-checked before any number was reported; the answer is in a `RESULTS_` file.
- [ ] `.agents/PROJECT.md`'s one-shot list names `--round-authors` and `--backfill-authors`, each in the same commit
      that wires it into `Program.cs` (gate finding 1); `research/module_server.md` and `module_extension.md` describe the reader,
      the attribution, the tables and the card line; the plan is promoted with its deviations.
- [ ] All tests pass through the MTP executable and `npm test`; the family checks are green.

## Epics

Seven epics in §11's order, each on its own branch and ONE commit, the coai gate once per epic; cornerstone quota ⌈2·7/5⌉ = 3.
Stories live in `todo/PLAN_a_round_records_who_wrote_the_code_and_how_much_E<n>.md`, one document per epic, written by Opus
when the epic starts (`subagent-models.md`, pipeline step 3); all seven are written, each linked under its epic below, and §16
records how the defects they found were settled. Fable named the cornerstone AREAS; Opus marks the stories within the quota and keeps the ledger. E1,
E5, E6, E7 carry no area — data, wiring, an arithmetic §8 fixes as exact-cell tests, an upsert with a shipped twin: in doubt, Opus.

*Consultant (gpt-6-astra), 2026-10-14, used for this split:* diff size first, useful on its own; observation (reader + record)
as ONE epic, attribution kept out; attribution its own epic — the highest risk, it decides what the report means; the report
after live attribution, not waiting for backfill; backfill last, rerunnable; the spike's fixtures before the AUTHORS schema
(step 23) gives a field meaning — step 22 (size) is git numstat only and may land first. Riskiest everywhere: numbers wrong without a symptom — the association, tokens once per `message.id`, the
denominator's coverage kinds, the delta's definition; the three areas hold exactly those four. E7 joins §11's steps 6 and 7:
the answer cannot precede the backfill, and the hand-check cannot follow the answer.

Cornerstones: 3 of 3 — E2.S1, E3.S4, E4.S2

### E1 — The spike: measure both sides, cut the fixtures (step 0) · M · depends on nothing

- **Stories.** [PLAN_a_round_records_who_wrote_the_code_and_how_much_E1.md](PLAN_a_round_records_who_wrote_the_code_and_how_much_E1.md)
- **Goal.** The facts the reader is built on, measured before any product code: §2's layout, `meta.json` and `effort` presence
  per Claude Code version, `message.id` repetition, `/clear` and `--resume` against the registry and the inherited variable,
  each store's transcript coverage, the read time over 14 MB — prediction first (`measurement.md` rule 4: ≥ 95 %, ≤ 300 ms p50).
- **Scope.** A `research/RESULTS_` file; sanitised fixtures under `src_mcp/tests/fixtures/caller-transcripts/` (LF by
  `.gitattributes`), the union of §6's and §12's cases, each with a `rounds.json`, every string off a shape-checked allowlist the
  sentinel; whole-session sizes (§16 D7); the `/clear` and `--resume` probes attended; the WSL side from inside WSL (`roundCountsRead.ts:18-21`). No product code, no schema step. Its fixtures gate the AUTHORS schema (step 23, E5) only; E2's step 22 derives from git numstat and does not wait for them (gate finding 4).
- **Done when.** The RESULTS file carries prediction then observation for both sides; one fixture per case; a test asserts
  every string off the allowlist is the sentinel and every allowlisted one has its shape; the measured read time is in the file, for E5's budget.

### E2 — Diff size, shipped alone (step 1) · M · depends on nothing, may run beside E1 or before it

- **Stories.** [PLAN_a_round_records_who_wrote_the_code_and_how_much_E2.md](PLAN_a_round_records_who_wrote_the_code_and_how_much_E2.md)
- **Goal.** Every code round records its change by scope, coverage and kind (§7); `reviewers.effort` is persisted.
- **Scope.** `CollectAsync` → `ReadCounted` (`ContextAssembler.cs:314`) and a `ChangeSize` beside `Files` (`:119`); the
  exclusions numstat (`:359`); the `delta` numstat against the last `done`, not-skipped code round with a `Sha` (`SessionStore.cs:96-104`, §16 D1),
  `merge-base --is-ancestor` for the rebase case; the kind function; `ElidedFile` → `elided` (`DiffShaper.cs:30-56`); nullable
  `RoundRecord.Size` (`SessionStore.cs:193`); schema step 22 — `round_changes`, `size_state`/`delta_state`/`delta_from`/`size_origin`, `reviewers.effort` defaulting to
  `not recorded` (§16 D11, D12) — after `TheRoundsKey`
  (`Schema.cs:31-37,614-616`); `RecordRound` (`RoundsDb.cs:190`) writes both and `RecordReviewers` (`:923-945`) binds
  `ReviewerState.Effort`; `--log` emits them. Call sites only: `PanelService.cs:756-757,868`, `RoundEngine.cs:605-625`.
- **Done when.** §12's diff-size row passes on a temp git repository; binaries carry no lines; `excluded` is reported and never
  in a denominator; a step-21 database opens at 22 and a pre-step round reads *not recorded*; `--log` shows the size and a database read
  shows the effort; `CoaiMcp.Tests.exe` green.
- **Cornerstone AREA.** The coverage × kind classification and the delta's definition — every later figure's denominator. An
  excluded or binary file counted as lines, or a delta taken as two cumulative totals subtracted, is wrong with no symptom.

### E3 — Observation: the text-free reader (step 2) · M · depends on E1, whose fixtures are its test inputs

- **Stories.** [PLAN_a_round_records_who_wrote_the_code_and_how_much_E3.md](PLAN_a_round_records_who_wrote_the_code_and_how_much_E3.md)
- **Goal.** `ObservedSession` from the caller's own transcript, keyed by the session id, never by `cwd`; nothing attributed.
- **Scope.** New `src_mcp/src/Store/CallerTranscript.cs` (§5); the text-free helpers lifted OUT of `AgentLog.cs` — the day gate
  (`AgentLog.cs:103-112,157-173`), the shared-mode torn-line-tolerant read (`:183-207,227-230`) — `AgentLog` calling them, its
  behaviour kept (§3); the transcript as `<projects>/*/<sessionId>.jsonl`, the id by the reader's own ladder — registry
  by pid, else `CLAUDE_CODE_SESSION_ID`, a stated id `unobserved` (§16 D9) — the source recorded; the `fromUtc` slice (§16 D7);
  tokens as the per-term maximum per `message.id` (§16 D8); the home fixture for tests (§16 D17); `COAI_AGENT_LOG_DIR` (`PanelSettings.cs:386,972`);
  requested vs observed model; `effort` or `unknown`; parent links; writes per call — agent, instant, checkout root,
  tool — from the write tools' one path property (§16 D3, D4, D18); the four states; the `Agent`-count check.
- **Done when.** Over E1's fixtures: a three-line message counts once; a resumed agent is one launch; a meta without a transcript,
  an `Agent` call with neither, a torn MIDDLE line, a meta with no `model` → `partial: <why>`, reading on; a torn LAST line →
  not partial, counted in `Diagnostics.TornTails` (§16 D22); a refused or unanswered write → not a write; no `effort` → `unknown`;
  persistence off → `unobserved: transcript not found`; the registry beats the variable, recorded; no sentinel in any rendering.
- **Cornerstone AREA.** The counting rules — tokens once per `message.id`, launches once per `agentId`, resume ≠ launch. Every
  downstream figure sums what this unit counts; a double count has no symptom.

### E4 — Attribution: windows, association, ambiguous totals (step 3) · M · depends on E3

- **Stories.** [PLAN_a_round_records_who_wrote_the_code_and_how_much_E4.md](PLAN_a_round_records_who_wrote_the_code_and_how_much_E4.md)
- **Goal.** `RoundAttribution` — pure, no I/O — turns an `ObservedSession` plus the gate session's rounds into associated
  activity per window and agent, the round's ambiguous totals and its contributors (§6).
- **Scope.** `src_mcp/core/Authors/` (§16 D20); windows tiled across every stage from the previous RECORDED round's START (§16 D1, D2) — not its `CompletedUtc`, as
  `RoundEngine.cs:893-902` does — plus `before-open`; the four-step association; `same-session` only when exactly one
  checkout was written in the window; the evidence cutoff (§16 D6); never duplicated (§16 D5); contributors as the union of windows up to round N, each tagged with its round.
- **Done when.** Every §6 acceptance case is a passing test: two repositories in one caller session; background work across two
  rounds (tokens split by timestamp, ONE launch, in round N's window, not in none); a model switch inside a window; a resumed
  agent; missing child transcripts; a second gate session of the same caller; a replay adding nothing; work before `open` →
  `before-open`; the cheap-model-fixes-round-3 case keeps the original authors and adds the fixer; no duplication anywhere.
- **Cornerstone AREA.** The window tiling and the association ladder — the highest risk by the consultant's reading and by §6:
  this unit decides what the report means.

### E5 — Store and show (step 4) · M · depends on E2 (step 22 before 23; the card reads the size) and E4

- **Stories.** [PLAN_a_round_records_who_wrote_the_code_and_how_much_E5.md](PLAN_a_round_records_who_wrote_the_code_and_how_much_E5.md)
- **Goal.** Every new round's record carries its authors and their state in the session file, and its projection row from it (§7, §11 step 4); one line on the card.
- **Scope.** The nullable, text-free `RoundRecord.Authors` snapshot (§7), saved with the round; schema step 23 — `round_authors`
  (§11 step 4's columns), `rounds.authors_state`, `authors_session`, `authors_id_source`, `authors_reader`, the ambiguous totals, an opaque `agent` per
  row — projected from that snapshot under `SessionClaim`, a null snapshot never erasing stored authors, stripped from
  `open`/`status` (§16 D13, D14); read + attribution BEFORE the round is saved (`RoundEngine.cs:586`), so before `Projection.Write` opens the database (`Projection.cs:24-38`), capped at
  2 s / 64 MB → `partial: read budget`; rows delete-then-insert per round like `RecordReviewers` (`RoundsDb.cs:923-945`); call
  sites only in `RoundEngine.cs:605-625` and `PanelService.cs`; `--log` prints the state; the card line (`rounds.ts:156`,
  `roundsLog.ts:519`): *written with Opus 5.5 (main) · 3 Sonnet coders · +412 −88 in 9 files* or *authors not observed — <why>*.
- **Done when.** A step-21 database opens at 23 and read-only `--log`/`--round-counts` on a step-21 file still answer; a pre-step
  round (no `Authors` in its session file) reads *not recorded*; the same round recorded twice gives identical rows; projecting a
  record that carries the snapshot, with NO transcript directory present, writes the same `round_authors` rows (rounds have no
  startup re-projection today — consultations do, `PanelService.cs:159` — so this is the test that the rows need nothing else); the sentinel is absent from the session
  file, `--log`, and the `.db` bytes once `agent_log` is blanked (positive control first); the budget path records `partial: read budget`; `bundledPage.test.ts` shows the line for observed /
  partial / unobserved and none for a pre-step round; `CoaiMcp.Tests.exe` and `npm test` green.

### E6 — The report: `--round-authors` on live rounds (step 5) · L · depends on E5

- **Stories.** [PLAN_a_round_records_who_wrote_the_code_and_how_much_E6.md](PLAN_a_round_records_who_wrote_the_code_and_how_much_E6.md)
- **Goal.** §8 over the rounds E5 records: cohorts, cells, numerators, denominators — association only, said in the header.
- **Scope.** A mode beside `RoundCountsMode.cs`, registered at `Program.cs:338-340`; a population unit (which rounds are in a
  cohort, why each excluded one is out) apart from a counting unit (the benchmark's `GatePopulation`/`GateReport`/`Figure`
  conventions, carried over); `first`/`corrective` never pooled; cells by period × coder mix (+ effort) × main model × reviewer
  set, failed and partial reviewers beside; six numerators named apart — all, `severe`, `gate_count`, re-raised,
  accepted, unresolved (§16 D10) — each with total and coverage; contributors through E4.S4; families from
  `shared/model-display-vectors.json` (§16 D19); denominator `shown + elided` lines
  of `code`+`test` by default; ratios of sums; `Unknown` never 0; dominance marks; JSON of sums, `--table`, `--add`; exit
  0 / 65 / 74, never 64; `--round-authors` registered in `.agents/PROJECT.md`'s one-shot list IN THE SAME COMMIT that wires
  it into `Program.cs`; rounds marked `size not covered` excluded from per-100-line figures by default and counted.
- **Done when.** §12's report row passes over a seeded store — exact cells, ratios of sums, `Unknown` not 0, exclusions counted,
  cohorts never pooled, marks under 10 rounds or one branch over 50 %, `--add` of two halves = the union; exit 0 on an empty
  store, 65 on a bad flag or an unreadable `--add`, 74 on an unreadable store; the header names coincident changes;
  `.agents/PROJECT.md` names `--round-authors` and `ARequestFaultIsNotAnOldBinaryTests` is green.

### E7 — Backfill, then validate, then answer (steps 6–7) · M · depends on E6, which does not wait for it (§8)

- **Stories.** [PLAN_a_round_records_who_wrote_the_code_and_how_much_E7.md](PLAN_a_round_records_who_wrote_the_code_and_how_much_E7.md)
- **Goal.** `--backfill-authors [--since]` recovers what still exists, per side; ten rounds per side are hand-read before any
  number is reported; the owner's question is answered in a `RESULTS_` file; the plan is promoted.
- **Scope.** A mode beside `RoundCountsMode.cs` running E3, E4 and E2's numstats, versioned by `authors_reader`, over rounds
  with empty or older-reader `authors_state` (a non-session `caller` written `unobserved`); never over an `observed`/`partial`
  row of a newer-or-equal reader; findings and resolutions untouched — a narrow writer, never `RecordRound` (`RoundsDb.cs:190-202`),
  the snapshot through the session store under `SessionClaim` (§16 D14), `size_origin = backfill` (§16 D12); released first,
  the run and RESULTS a second commit, WSL built in WSL (§16 D16); the session file saved FIRST and the projection
  re-derived from it on a rerun (gate finding 3); exit 0 / 65 / 74 / 75, never 64; §9's unrecoverable reasons per round;
  `--backfill-authors` registered in `.agents/PROJECT.md`'s one-shot list IN THE SAME COMMIT that wires it into `Program.cs`. Then rule 2 of `measurement.md` on both sides; the before/after table via `--round-authors --add`
  with §11 step 7's columns; `research/module_server.md`, `module_extension.md`; `/promote-plan` of this plan with its `_E<n>.md`.
- **Done when.** Twice over a store holding one `observed` row of a newer reader: the second run changes no totals, the newer
  row and every resolution survive; a run killed between the session-file save and the projection is completed by the
  next with identical rows; each exit code has its test; `.agents/PROJECT.md` names `--backfill-authors`;
  unrecoverable rounds counted per reason; ran on both sides; the RESULTS file has the
  hand-check first, the table second, association-only in its header; docs updated; `plan-lifecycle.mjs` clean.
