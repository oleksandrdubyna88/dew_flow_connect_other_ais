# PLAN — E1 stories: the spike measures both sides and cuts the fixtures

> Status: **plan only, nothing implemented yet, 2026-10-14.** Epic E1 of [PLAN_a_round_records_who_wrote_the_code_and_how_much.md](PLAN_a_round_records_who_wrote_the_code_and_how_much.md).
>
> Scope: no product code and no schema step. A results file under `research/`, two measuring scripts under `scripts/`
> with their `node --test` tests, sanitised fixtures under `src_mcp/tests/fixtures/caller-transcripts/`, one C# guard
> test, one `.gitattributes` line. Cornerstones marked here: **none** — the parent's `## Epics` names no cornerstone AREA
> for E1; the parent's ledger reads `Cornerstones: 3 of 3 — E2.S1, E3.S4, E4.S2` (parent §16 D21).

## Why this epic exists, in one paragraph

Everything E3–E7 count is read from a format nobody documents: Claude Code's transcript tree. The parent plan's §2 was
measured on ONE machine side, in ONE session's folder. Before any schema gives a field meaning (parent §11 step 0, §10
point 8), this epic measures the format on Windows AND inside WSL, writes down what it predicted first
(`.agents/conventions/common/measurement.md` rule 4), and leaves behind fixtures that later epics test against —
sanitised so that no prompt, message or tool input ever lands in a public repository. Every later "no text is stored"
test (E3, E5) relies on the sentinel this epic plants.

## Facts the stories rest on (verified 2026-10-14 at `97128954`)

| Fact | Where |
|---|---|
| A code round is `rounds.stage = 'CodeReview'` | `src_mcp/src/Store/BugsQuery.cs:178` |
| `rounds.started_utc`, `rounds.caller` (the caller's session id), `sessions.repo_path` | `src_mcp/src/Store/Schema.cs:61,70,46-47` |
| Read-only store access precedent: Python 3 `sqlite3`, `file:<dataDir>/coai.db?mode=ro` | `research/RESULTS_what_the_gate_costs_a_task.md:98` |
| The other side's store is never opened across `\\wsl.localhost` (WAL) | `src_vs_code/src/roundCountsRead.ts:18-20` |
| The caller ladder: `COAI_CALLER_SESSION` → `CLAUDE_CODE_SESSION_ID` → `CODEX_SESSION_ID` → `GEMINI_CLI_SESSION_ID` | `src_mcp/src/Server/CallerSessions.cs:44-53` |
| Default transcript root `~/.claude/projects`; `COAI_AGENT_LOG_DIR` overrides it | `src_mcp/src/Store/AgentLog.cs:39-40`, `src_mcp/src/Server/PanelSettings.cs:386,972` |
| `agent_log` already walks `*.jsonl` recursively — subagent files included | `src_mcp/src/Store/AgentLog.cs:123` |
| Day-substring gate and the shared-mode line read (the read-time variants to measure) | `src_mcp/src/Store/AgentLog.cs:103-112,157-173,183-207` |
| Fixtures under `src_mcp/tests/fixtures/**` reach the test output | `src_mcp/tests/CoaiMcp.Tests.csproj:36` |
| Measurement scripts live in `scripts/`, their tests in `scripts/lib/*.test.mjs`, run by CI on Linux | `.github/workflows/ci.yml:192-197`, `scripts/lib/kimi.test.mjs:1-5`, header style `scripts/measure-idle-server.mjs:1-14` |
| `core.autocrlf=true` in this checkout; `.gitattributes` pins only `*.sh` and one JSON to LF | `.gitattributes:1-8` |
| Node: Windows `v24.18.0` (has `node:sqlite`), WSL `v20.20.2` (does NOT); Python 3 on both (3.14.6 / 3.12.3) | measured while writing this |

## Order

```
S1 (prediction) ──► S2 (measuring harness) ─┐
                    S3 (sanitiser + cutter) ─┼──► S4 (run both sides, attended) ──► S5 (cut the fixtures, guard test)
```

S2 and S3 may run in parallel after S1. Nothing in S2/S3 may run against the operator's transcripts before S1 is
written — the prediction precedes every observation, including an exploratory one.

---

## E1.S1 — The prediction, written before anything is read

- **Goal.** `research/RESULTS_caller_transcripts_on_both_sides.md` exists with its header and a predictions table, and
  nothing else, before any harness touches a transcript or a store.
- **Scope.** One new file. Header: subject (`dew_flow_connect_other_ais` sha at the time of writing), the harnesses by
  path (`scripts/measure-caller-transcripts.mjs`, `scripts/export-code-rounds.py`), date, machine sides, the Claude
  Code versions to be found on disk (unknown yet — say so). Then a table `Q · question · predicted · why`, the rows below.
  Observations are S4's.
- **Brief.** The questions, each predicted with its reason:

  | Q | Question | Starting point for the prediction |
  |---|---|---|
  | Q1 | Layout of parent §2 lines 85-90 holds on both sides (main file, `subagents/agent-<id>.jsonl`, `.meta.json`) | parent §2, measured on Windows only |
  | Q2 | Share of assistant lines carrying `effort`, and of metas carrying `model`, per Claude Code `version` | parent line 41: Sonnet at `medium` before 2026-10-14 — so `effort` exists on recent versions; older unknown |
  | Q3 | Lines per distinct `message.id`; whether the four `usage` terms are IDENTICAL across a message's lines, and if not which line carries the final figures | parent line 97: 1 992 lines / 1 041 ids ≈ 1.9 |
  | Q4 | `/clear`, `--resume`, `--no-session-persistence` against the registry `sessionId`, the child's inherited `CLAUDE_CODE_SESSION_ID`, and the transcript file | the 2026-09-12 measurement: every child inherits `CLAUDE_CODE_SESSION_ID` and `CLAUDE_PID` |
  | Q5 | Coverage: share of each store's code rounds since 2026-09-30 whose `caller` has a transcript that reaches the round's `started_utc` | Windows: parent line 54 already observed 392/398 — **that is an observation, not a prediction**; write it as such and predict only what is unknown (WSL; the "reaches the round" refinement) |
  | Q6 | WHOLE-session sizes per side (a main file plus its `subagents/` folder — parent §16 D7), and read time over the largest session: full parse p50, and day-gated parse p50 | parent §11 step 0: ≤ 300 ms p50; one real session measured 14.8 MB main + 63 MB subagents |
  | Q7 | `Agent` tool_uses in a main file vs metas vs agent files — how often they disagree, and why | parent line 98: 82 vs 84, explained by nested launches |
  | Q8 | Write-tool `file_path` forms: absolute always? separators and drive-letter case as written; a subagent's `cwd` vs where it writes | parent line 99 |
  | Q9 | Which top-level and `message.content[]` keys exist per version (the sanitiser's allowlist input) | unknown — predict "a stable core plus version-specific extras" |

- **Done when.** The file has the header and nine predictions with reasons; no observed number except the one Q5
  names as already observed; `git diff --stat` shows ≤ 50 lines in that one file.
- **Tests.** None — a document with no behaviour (say so in the summary).
- **Depends on.** Nothing.
- **Model.** Main session (a prediction is a judgement; one file, bounded to ≤ 50 lines before starting — the
  small-edit exception).
- **Size.** ≈ 45 lines.

---

## E1.S2 — The measuring harness: counts only, never a value

- **Goal.** One command per side turns a store and a transcript root into ONE JSON object of counts that answers Q1–Q3
  and Q5–Q9. It prints no field value that could carry text — only counts, booleans, durations, version strings, key
  NAMES and model ids.
- **Scope (new files).**
  - `scripts/export-code-rounds.py` — stdlib only. `sqlite3.connect('file:<db>?mode=ro', uri=True)`; one query:
    `SELECT r.session_id, r.number, r.started_utc, r.completed_utc, r.status, r.caller, r.caller_vendor, s.repo_path
    FROM rounds r JOIN sessions s ON s.id = r.session_id WHERE r.stage = 'CodeReview' AND r.started_utc >= ?`;
    writes JSON to a path argument with `newline='\n'`, `ensure_ascii=False`, UTF-8. No logic beyond the query — it
    exists because WSL's Node 20 has no `node:sqlite`, and ONE exporter for both sides beats two.
  - `scripts/lib/caller-transcripts.mjs` — pure functions over parsed lines and directory listings.
  - `scripts/measure-caller-transcripts.mjs` — CLI:
    `node scripts/measure-caller-transcripts.mjs --rounds <rounds.json> --projects <dir> [--largest <n>] [--runs <n>]`;
    exit 0 ok, 65 bad arguments / unreadable input (never 64 — the family reading of 64). Prints one JSON line.
  - `scripts/lib/caller-transcripts.test.mjs`.
- **Brief.**
  - **Finding a transcript**: `<projects>/*/<caller>.jsonl` — list each project folder once; never compute a slug
    (parent line 136-137). A caller that is not a UUID is counted under `notASessionId` (codex / blank / stated ids).
  - **Q5 coverage** per round: `found` (file exists AND its last parseable timestamp ≥ the round's `started_utc`),
    `foundButEndsBefore`, `notFound`, `notASessionId`; also per caller vendor (`caller_vendor`) and per distinct caller.
    Report rounds AND distinct callers — parent line 53-55 reports both.
  - **Q3**: group assistant lines by `message.id`; report lines/ids, the count of ids whose four usage terms
    (`input_tokens`, `output_tokens`, `cache_creation_input_tokens`, `cache_read_input_tokens`) differ across lines,
    and for those whether the LAST line (by timestamp, then file order) holds the per-term maximum. This decides E3's
    counting rule; the harness answers it, it does not choose it.
  - **Q2/Q9**: per `version` field: assistant lines, lines with `effort`, effort values (`low|medium|high|xhigh|…` —
    value set is printed, it is an enum not text), metas with/without `model`; the SET of top-level keys and of
    `message.content[].type` values (names only).
  - **Q7**: per main file: `Agent` tool_use count, metas, agent files, metas without a file, files without a meta,
    metas whose `toolUseId` matches no `Agent` tool_use in the main file, metas with `parentAgentId`.
  - **Q8**: for `Edit|Write|MultiEdit|NotebookEdit` inputs: counts of absolute vs relative paths, `/` vs `\`,
    lower vs upper drive letter, and whether the path is under the agent line's `cwd` — booleans, never the path.
  - **Q6**: the distribution of whole-session sizes under `--projects` (a main file plus its `subagents/` folder,
    parent §16 D7), then over the `--largest n` (default 1) biggest SESSIONS: bytes and lines, then
    `--runs` (default 7) timed passes each of (a) parse every line, (b) day-gated: skip lines lacking the day substring
    of a one-day window before parsing (the `AgentLog.Days`/`Mentions` approach, `AgentLog.cs:103-112,157-173`); report
    the first (cold) run apart and p50 of the rest. Record `process.version` and the platform: this is Node's time, not
    the C# reader's, and the results file must say so (measurement rule 3).
  - **Torn last line**: a line that does not parse is counted (`unparseable`) and skipped, never fatal.
  - Read files shared (`fs.openSync` read-only); never write under `--projects`; never open anything under
    `~/.claude/sessions` (S4 reads the registry by hand, one file).
- **Test scenarios** (`node --test scripts/lib/caller-transcripts.test.mjs`; synthetic trees under `os.tmpdir()`, removed
  in `after`; nothing depends on the operator's home — CI runs it on Linux):
  - `aMessageWrittenAsThreeLinesIsOneId` — three lines, one `message.id`, identical usage → ids 1, lines 3, differing 0.
  - `aMessageWhoseUsageGrowsReportsWhichLineHoldsTheMaximum` — growing `output_tokens` → differing 1, lastHoldsMax 1.
  - `aRoundWhoseTranscriptEndsBeforeItStartedIsNotCovered` — file exists, last stamp before `started_utc` →
    `foundButEndsBefore`, not `found`.
  - `aCallerThatIsNotAUuidIsCountedApart` — `"codex-abc"` and `""` → `notASessionId` 2, never `notFound`.
  - `aMetaWithoutItsTranscriptAndAnAgentCallWithoutAMetaAreBothCounted`.
  - `aTornLastLineIsCountedAndSkipped`.
  - `noFieldValueReachesTheReport` — every string in the synthetic tree (prompt, `description`, `Bash` command,
    `file_path`, `cwd`, `gitBranch`) carries a marker `ZZ-MARK-<n>`; the printed JSON contains no `ZZ-MARK`. Positive
    twin in the same file: the same tree produces non-zero counts in every section (testing.md, *a file full of
    negative assertions needs one positive*).
  - CLI: missing `--rounds` → exit 65 with a usage line on stderr; a valid run → exit 0, one JSON line on stdout.
- **Break-its** (mutate the lib only; restore after): drop the grouping by `message.id` → the three-line test goes red;
  print a `file_path` in the Q8 section → `noFieldValueReachesTheReport` goes red.
- **Done when.** Tests green under `node --test "scripts/**/*.test.mjs"` on Windows and inside WSL (Node 20 — no API
  newer than 20 in the `.mjs` files); the exporter answers a step-21 `coai.db` copy in a temp dir; LF endings.
- **Depends on.** E1.S1 written. Nothing from other epics.
- **Model.** Coder: sonnet.
- **Size.** ≈ 380 lines (lib ~170, CLI ~60, exporter ~30, tests ~120).

---

## E1.S3 — The sanitiser and the fixture cutter: shape kept, text gone

- **Goal.** A deterministic tool that copies a WINDOW of one real caller session — main file, its subagent files and
  metas — into a fixture directory where every string not on an allowlist is the sentinel, every id is remapped
  consistently, and every path is rewritten under a fixture root. Same input → same bytes.
- **Scope (new files).**
  - `src_mcp/tests/fixtures/caller-transcripts/allowlist.json` — the ONE list of structural fields, read by the cutter
    AND by S5's C# test (a contract written once — testing.md, *two implementations*). Shape:
    `{ "sentinel": "COAI-FIXTURE-SENTINEL", "fields": [ { "path": "message.usage.input_tokens", "shape": "int" }, … ] }`.
    Paths are dotted with `[]` for array elements, e.g. `message.content[].input.file_path`. Shapes: `uuid`, `hexid`,
    `msgid`, `iso-utc`, `model-id`, `alias`, `enum:<a|b|…>`, `tool-name`, `version`, `fixture-path`, `int`, `bool`.
  - `scripts/lib/transcript-sanitise.mjs`, `scripts/cut-transcript-fixtures.mjs`,
    `scripts/lib/transcript-sanitise.test.mjs`.
- **Brief.**
  - **Allowlist, starting from parent §2 lines 92-96 and S4's Q9 key sets**: line `type`, `timestamp`, `sessionId`,
    `agentId`, `uuid`, `parentUuid`, `isSidechain`, `version`, `cwd` (rewritten), `effort`, `requestedModel`;
    `message.id`, `message.model`, `message.role`, `message.usage.*` (the four terms; other usage keys are numbers and
    may stay); `message.content[].type`, `message.content[].id`, `message.content[].name`,
    `message.content[].tool_use_id`, `message.content[].input.file_path` / `.notebook_path` (rewritten),
    `message.content[].input.subagent_type`, `message.content[].input.model`, `message.content[].input.run_in_background`;
    meta `agentType`, `toolUseId`, `spawnDepth`, `parentAgentId`, `requestShape`, `model`. NOT allowed, explicitly:
    `description`, `prompt`, `command`, `content` as a string, `thinking`, `signature`, `toolUseResult`, `gitBranch`,
    `name` of a registry entry.
  - **Everything else**: a string leaf → the sentinel; numbers, booleans and nulls kept; object KEYS kept, except a key
    containing `/`, `\` or `:` → replaced by `k<n>` (a filename used as a key must not survive — `snapshot.trackedFileBackups`
    is the known case: its keys are real file paths). Unknown keys are not an
    error — they are sentinelled and their names counted on stderr, so a new Claude Code field is visible.
  - **Ids**: every `uuid`/`hexid`/`msgid`-shaped allowlisted value is remapped through ONE map per fixture
    (deterministic: `sha256(salt + original)` formatted back into the same shape; the salt is per fixture and
    committed). The SAME map renames files (`<sessionId>.jsonl`, `<sessionId>/subagents/agent-<agentId>.jsonl|.meta.json`),
    so `toolUseId` ↔ `tool_use.id`, `parentAgentId` ↔ `agentId`, `tool_use_id` ↔ `id` still join.
  - **Paths** (`cwd`, `file_path`, `notebook_path`): a `--repo <real-prefix>=<fixture-root>` mapping per repository
    (e.g. `D:/rsd/_wt/x=D:/fixture/repo-a`, `/home/<u>/src/y=/fixture/repo-b`); match is case-insensitive on Windows-style
    prefixes and keeps the remainder's separators and case as written (Q8 is what E3 must cope with). A path matching no
    mapping → `<fixture-root-other>/p<n>` — never the original. A relative path stays relative, its segments replaced by
    `p<n>` segments.
  - **Window**: `--from <utc> --to <utc>` keeps main-file lines in the window and every subagent file whose launching
    `Agent` call or first line falls in it, whole (a resumed agent keeps its later lines). `--drop-agent-file <id>` and
    `--drop-meta <id>` exist to derive the "missing child transcript" cases from a real cut.
  - **Timestamps** kept as written (round windows in `rounds.json` are in the same clock). Nothing in a fixture may
    encode an instant or a path that one offset and the fixture roots cannot move: E5.S5 shifts every instant of a case
    by ONE offset and re-points its roots at a temp repository to run a round end to end (E5's defect 13, accepted in
    the parent's §16).
  - Output: LF, keys in input order, one line per input line, `JSON.stringify` without spaces — as Claude Code writes.
  - The cutter NEVER reads `~/.claude/sessions` and never copies any file it was not named; it refuses (exit 65) an
    output directory outside `src_mcp/tests/fixtures/caller-transcripts/` unless `--out-anywhere` (tests use temp dirs).
- **Test scenarios** (`node --test`, synthetic inputs in temp dirs):
  - `everyStringOffTheAllowlistBecomesTheSentinel` — markers in prompt, `thinking`, `signature`, `toolUseResult`,
    `Bash.command`, `description`, `gitBranch` → none survive; the line count and the key order are unchanged.
  - `anAllowlistedFieldKeepsItsShapeAndOnlyItsShape` — a `message.model` holding a sentence (shape `model-id` fails)
    → sentinelled, not kept.
  - `idsAreRemappedConsistentlyAndStillJoin` — after the cut, every meta's `toolUseId` equals an `Agent` `tool_use.id`
    in the main file and every `parentAgentId` names an agent file; no original id appears in any output byte.
  - `aPathIsRewrittenUnderItsRepositoryRootKeepingItsSeparators` — `D:\rsd\_wt\x\src\A.cs` and `d:/rsd/_wt/x/B.cs` both
    land under `D:/fixture/repo-a` with their own separator style; an unmapped path lands under the `other` root.
  - `aKeyThatLooksLikeAPathIsRenamed`.
  - `theSameInputCutsToTheSameBytes` — two runs, byte-identical output.
  - `aResumedAgentKeepsItsLaterLines` and `aDroppedAgentFileLeavesItsMeta`.
- **Break-its** (mutate the lib only): let `signature` through → the first test goes red; remap `toolUseId` through a
  second map → the join test goes red. Never weaken the cutter's output-directory refusal to make a test pass.
- **Done when.** Tests green on Windows and in WSL (Node 20 APIs only); `allowlist.json` committed; LF.
- **Depends on.** E1.S1 written. Q9's observed key sets (S4) may extend the allowlist afterwards — any extension is
  made in `allowlist.json` only, with the test above re-run.
- **Model.** Coder: sonnet.
- **Size.** ≈ 390 lines (lib ~190, CLI ~60, tests ~120, allowlist ~20).

---

## E1.S4 — Run it on both sides, and the three session probes

- **Goal.** The results file carries, under each prediction, the observation from Windows AND from inside WSL, with
  its conditions; the session probes of Q4 are observed live; the cases S5 will cut are chosen.
- **Scope.** `research/RESULTS_caller_transcripts_on_both_sides.md` (the S1 file, extended); its row in
  `research/README.md`'s table. Raw harness outputs and the case selection stay OUTSIDE the repository (the session's
  scratch directory): they carry the operator's session ids and real paths.
- **Brief.**
  - **Per side**: `python3 scripts/export-code-rounds.py <dataDir>/coai.db <scratch>/rounds-<side>.json 2026-09-30`
    (Windows `%LOCALAPPDATA%\coai-mcp`, WSL `~/.local/share/coai-mcp`); then
    `node scripts/measure-caller-transcripts.mjs --rounds … --projects ~/.claude/projects --largest 1 --runs 7`. The WSL
    run happens INSIDE WSL (`wsl.exe -e bash -lc …`), reading WSL's store and WSL's transcripts — never across the drvfs
    boundary. Run under `node .agents/conventions/tools/low-priority.mjs` on a machine not otherwise loaded, or say what
    else ran.
  - **Q4 probes** (live, cheap, each in a scratch folder whose project directory is the only one listed before/after):
    - *Inheritance*: in a fresh session, a Bash child prints whether `CLAUDE_CODE_SESSION_ID` and `CLAUDE_PID` are set,
      and whether `~/.claude/sessions/$CLAUDE_PID.json`'s `sessionId` equals the variable. Read ONLY that one `.json`;
      never list `~/.claude/sessions`, never open a `.key` beside it.
    - *`/clear`*: after it — the registry `sessionId`, the variable seen by a NEW Bash child, the variable held by the
      ALREADY-RUNNING MCP server child (WSL: `/proc/<pid>/environ` of the stdio child whose parent is `$CLAUDE_PID`,
      found by parent pid, never by image name; Windows: the `caller` the coai server records on a cheap `open` +
      `status`, or "not observable on Windows" stated), and which `.jsonl` the post-clear turns land in.
    - *`--resume`*: same file appended or a new one; registry and variable after it.
    - *`--no-session-persistence`* (`claude -p --no-session-persistence "reply ok"`): no new `.jsonl` in that project
      folder.
    - Record the Claude Code version of each probe; do not paste ids into the results file — say "equal / different".
  - **The folder facts E3.S4's spec rests on**, checked on both sides and written down (counts, never content): the
    main file's first line is a `queue-operation` line with a `timestamp` and no message; `is_error` ABSENT on a
    `tool_result` means not an error; a `tool-results/` folder of `.txt` files sits beside `subagents/` (never read by
    the reader, never cut into a fixture); `snapshot.trackedFileBackups` objects have real file paths as KEYS (S3's
    key-renaming rule must cover them — a fixture with such a key un-renamed is a leak); and how often a live read
    sees a torn LAST line (sample each live main file several times while its session runs) — the frequency that
    keeps or flips `TornTailCountsAsPartial` (parent §16 D22).
  - **Write per Q**: predicted · observed (Windows) · observed (WSL) · conditions · what it does NOT settle. Q3's answer
    is stated as the rule E3 must implement ("the last line of a `message.id` holds the final usage" or whatever was
    observed), with the counts behind it. Q6 says plainly it is Node's time on this hardware and that E5 re-measures the
    C# reader against the same file before fixing its 2 s budget.
  - **Case selection** for S5 (scratch only): per required case, a real session and window that exhibits it, or
    "derived from <case> by <edit>" when none exists on disk. Name in the results file which cases are cut and which
    derived — provenance, not ids.
  - Name the subject sha, the harness paths, the date, both Claude Code version sets (measurement rule 6).
- **Tests.** None of its own; S2's and S3's suites are re-run on the commit used, and the run is named.
- **Done when.** Every Q has both sides or a stated reason one is missing; Q4's four probes observed; the README row
  exists; the case selection covers every case listed in S5; a hand-read of three rounds per side confirms the
  harness's coverage verdicts (measurement rule 2 — a metric is checked before it is reported).
- **Depends on.** E1.S1, E1.S2. (S3 is not needed to measure.)
- **Model.** Main session (attended) — live Claude Code sessions, `/clear` and `--resume` typed by hand, the operator's
  own transcripts.
- **Size.** ≈ 150 lines of results.

---

## E1.S5 — Cut the fixtures, and the test that keeps them text-free

- **Goal.** One fixture directory per required case under `src_mcp/tests/fixtures/caller-transcripts/`, each cut by
  S3's tool from S4's selection (or derived and labelled so), and a C# test that fails the build the moment any fixture
  carries a non-sentinel string, a path outside a fixture root, a broken join, a CR byte, or a case goes missing.
- **Scope.**
  - `src_mcp/tests/fixtures/caller-transcripts/<case>/` — `projects/<slug>/<sessionId>.jsonl`,
    `projects/<slug>/<sessionId>/subagents/agent-<id>.jsonl|.meta.json`, optional `sessions/<pid>.json` (a registry
    entry WRITTEN by hand from allowlisted fields only — never cut from the real registry), and `rounds.json`.
  - `src_mcp/tests/fixtures/caller-transcripts/manifest.json` — per case: `name`, `serves` (the plan section and row it
    is for), `provenance` (`cut` · `derived: <from case>, <edit>`), `side` (`windows`|`wsl`), `pathStyle`, `version`
    (Claude Code), and hand-checked `counts` {`assistantLines`, `distinctMessageIds`, `agentFiles`, `metas`,
    `agentToolUses`}.
  - `rounds.json` shape, consumed by E4: `{ "callerSessionId", "gateSessions": [ { "id", "repoPath", "openedUtc",
    "rounds": [ { "number", "startedUtc", "completedUtc", "status", "headSha" } ] } ] }` — times in the transcript's
    clock, `repoPath` a fixture root.
  - `.gitattributes`: `src_mcp/tests/fixtures/caller-transcripts/** text eol=lf` (this checkout has
    `core.autocrlf=true`; Claude Code writes LF, and E3 must read what Claude Code writes).
  - `src_mcp/tests/ACallerTranscriptFixtureCarriesNoTextTests.cs`.
- **The required cases** (the union of parent §6 line 192-194, §11 step 0 line 292-294 and §12 lines 315-323 — they
  differ, see *Plan defects* below):
  `model-switch-in-window`, `message-as-several-lines`, `resumed-agent`, `background-agent-across-round-start`,
  `nested-agents`, `two-repositories-one-session`, `missing-child-transcripts` (a meta without its file AND an `Agent`
  call with neither), `second-gate-session-same-caller`, `replay-same-round-twice`, `persistence-off` (a registry and a
  `rounds.json`, no transcript), `work-before-open`, `meta-without-model`, `no-effort-lines`,
  `registry-and-variable-disagree`, `writes-only-elsewhere`, `no-evidence-two-repositories`, and — added by the
  parent's §16 — `skipped-round-between-recorded` (D1), `write-into-submodule-and-scratchpad` (D3) and
  `refused-agent-call` (E3/9 accepted). Keep each fixture to the
  smallest window that still shows its case (target ≤ 60 lines per `.jsonl`); never commit a whole real session.
  **No `torn-last-line` fixture**: a torn line cannot pass this story's own JSON guard, so that case is DERIVED in
  E3.S4's tests by `CallerTranscriptFixture`'s `TruncateLastLine` mutation (parent §16 D22).
- **Brief (the test).** Reads `allowlist.json` and `manifest.json` from `AppContext.BaseDirectory/fixtures/caller-transcripts`
  (copied by `CoaiMcp.Tests.csproj:36`). `System.Text.Json` walk; for every string leaf: if its path is allowlisted,
  it must satisfy its `shape` (and a `fixture-path` must start with a manifest fixture root); otherwise it must equal
  the sentinel. Object keys must not contain `/`, `\` or `:`. No file contains `\r`. Every meta's `toolUseId` joins
  an `Agent` `tool_use.id` in its main file and every `parentAgentId` names an agent file — EXCEPT where the manifest's
  `serves` declares the break (`missing-child-transcripts`). The manifest's `counts` equal what the files contain.
  The case list above is held by the test and compared BOTH ways with the manifest: a missing case and an unlisted
  case each fail. No regex over emails or user names is hard-coded — the shape rule already refuses them, and a
  real user name in a public test file would itself be the leak.
- **Test scenarios** (`CoaiMcp.Tests.exe --filter-class "*ACallerTranscriptFixtureCarriesNoTextTests"`, never
  `dotnet test`):
  - `EveryStringOffTheAllowlistIsTheSentinel` · `EveryAllowlistedStringHasItsShape` ·
    `EveryPathLivesUnderAFixtureRoot` · `NoFixtureCarriesACarriageReturn` ·
    `EveryChildJoinsItsParentUnlessTheCaseIsTheBreak` · `TheManifestCountsWhatTheFilesHold` ·
    `EveryRequiredCaseHasAFixtureAndNoFixtureIsUnlisted`.
  - **Positive twin**: `TheSentinelIsActuallyPresent` — the sentinel occurs in every case that has a main file (a
    sanitiser that dropped every field would otherwise pass the negative tests).
- **Break-its** (on a COPY of one fixture in the test output folder, or the committed file restored at once; never by
  loosening the test, the allowlist or the cutter's refusals): put a word into a `description` → red; put
  `C:/Users/someone/x.cs` into a `file_path` → red (shape); add a CRLF line → red; delete one case directory → red; add
  an unlisted case → red. Report each red message.
- **Done when.** The class is green through the MTP executable after
  `dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4`; the whole `CoaiMcp.Tests.exe` run is green; the
  five break-its were seen red for the right reason; every fixture file was opened and read by the Opus reviewer before
  the commit (this is a public repository — a fixture is published data); `git diff --stat` attributes the bulk to
  fixture data, not code.
- **Depends on.** E1.S3 (the cutter and `allowlist.json`), E1.S4 (the selection; Q9's key sets).
- **Model.** Coder: sonnet.
- **Size.** ≈ 220 lines of code (test ~200, `.gitattributes` 1, manifest ~20 structural) plus fixture data (≈ 20 cases
  × ≤ 80 lines, not code).

---

## What the other epics take from this one

| Consumer | Takes |
|---|---|
| E3.S2 (Q4), E3.S3 (Q8), E3.S4 (Q2, Q3, Q7; the fixtures as test inputs), E3.S5 (the sentinel) | the fixtures as test inputs; Q3's usage rule (which line of a `message.id` counts); Q2's `effort` presence; Q4's registry/variable/`/clear` behaviour; Q7's mismatch kinds for `partial: <why>`; Q8's path forms; the sentinel in `allowlist.json` |
| E4.S2 (its acceptance cases), E4.S5 (the fixtures end to end) | `rounds.json` per fixture; the background, resumed, nested, two-repository, second-gate-session and replay cases |
| E5.S4 (Q6, before its budget is fixed), E5.S5 (the sentinel, the re-pointable fixtures) | Q6's read time, before its 2 s / 64 MB budget is fixed; the sentinel for the `.db`-bytes and `--log` checks |
| E7.O1 (Q5 per side), E7.S1 (Q4's stale inherited id as an unrecoverable reason) | Q5's coverage per side — what backfill can recover at most; Q4's stale-inherited-id behaviour as an unrecoverable reason |
| E2 (diff size) | nothing — it may run beside this epic |

## Definition of Done

- [ ] S1: the predictions were committed in the same epic commit but WRITTEN before any harness read a transcript; Q5's
      Windows figure is marked as already observed.
- [ ] S2, S3: `node --test "scripts/**/*.test.mjs"` green on Windows and in WSL; break-its seen red.
- [ ] S4: both sides observed; Q4's probes observed live; three rounds per side hand-checked against the harness; the
      results file names sha, harnesses, date, versions, and what it does not settle; its `research/README.md` row exists.
- [ ] S5: every required case has a fixture with provenance; `ACallerTranscriptFixtureCarriesNoTextTests` green through
      `CoaiMcp.Tests.exe`; its five break-its seen red; the fixtures are LF by `.gitattributes`.
- [ ] No product code, no schema step; E1 marks no cornerstone (the parent's ledger: `3 of 3 — E2.S1, E3.S4, E4.S2`).
- [ ] `node .agents/conventions/tools/plan-lifecycle.mjs` clean (this document gets its own `todo/README.md` row — written
      by whoever commits the epic, not by this document's author).

## Plan defects found while splitting (parent line numbers; not fixed here)

> **Settled 2026-10-14** in the parent's §16: 6 and 7 → D9 (the reader's own ladder; only `<pid>.json` is read, the
> folder never listed); 1–5, 8 and 9 accepted as this document resolves them (§16, *the writers' own resolutions*).

1. **Lines 192-194 vs 292-294 vs 401.** E1's scope says "one per §6 acceptance case (§11 step 0 lists them)", but §11
   step 0 lists a DIFFERENT set (repeated message records, background agent completing after a round starts, nested
   agents, persistence off) from §6 (missing child transcripts, a second gate session, a replay). S5 takes the union
   plus §12's reader cases.
2. **Lines 194, 403.** "A replay" and "a second gate session of the same caller" are properties of ROUNDS, not of a
   transcript; the plan never says a fixture carries round data. S5 adds `rounds.json`.
3. **Line 403.** "Every text field of every fixture is the sentinel" cannot hold literally: model ids, timestamps, ids
   and write paths are text and E3 needs them. S3/S5 define a shape-checked allowlist instead.
4. **Lines 53-55 vs 291.** The Windows coverage "prediction" (≥ 95 %) was already observed (392/398) before it was
   written — measurement rule 4 is not met for that figure; only WSL is a real prediction.
5. **Lines 291, 400, 404.** The read time E5's budget rests on would be measured in Node (or Python), not by the C#
   reader that E5 runs; the plan does not say which runtime. S2/S4 record the condition; E5 must re-measure.
6. **Lines 89, 125.** The registry directory `~/.claude/sessions` also holds `<pid>.<hash>.key` files (measured
   2026-09-12); the plan should say only `<pid>.json` is ever read and the directory is never listed.
7. **Lines 125 and 321.** §4 gives the ladder as "registry, else `CLAUDE_CODE_SESSION_ID` (`CallerSessions.cs:47`)", but
   `CallerSessions.cs:46` puts `COAI_CALLER_SESSION` first, and §12 expects `COAI_CALLER_SESSION` alone to read as
   `no session id` while `CallerIdentity` treats it as an id (vendor `stated`). For E3 to settle; E1's Q4 only measures.
8. **Line 288 / E1 heading line 396.** `/clear` and `--resume` need live, typed sessions; the epic does not say part of
   it is attended work. S4 is.
9. **Line 401.** Fixtures "under `src_mcp/tests/`" — the existing convention is `src_mcp/tests/fixtures/`
   (`CoaiMcp.Tests.csproj:36`), and with `core.autocrlf=true` a `.jsonl` fixture checks out CRLF unless
   `.gitattributes` pins it; the plan says neither.
