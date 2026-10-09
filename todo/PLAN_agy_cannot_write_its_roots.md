# PLAN — an antigravity launch cannot write inside the roots it is given

> Status: **plan only, nothing implemented yet, 2026-10-08; plan round passed 2026-10-09 with its six findings folded
> in.** Scope: every place coai builds an agy argv — the question rows' fragments
> (`src_mcp/core/QuestionConsult/ConfinementPlanner.cs:161-163`), the consultant
> (`src_mcp/runners/Consultation/AntigravityConsultant.cs:173-176`, `Build`) and the reviewer
> (`src_mcp/runners/Reviewers/AntigravityRuntime.cs:56`) — plus a measurement script and tests.
>
> Related docs: [RESULTS_agy_searches_through_coai.md](../research/RESULTS_agy_searches_through_coai.md) (the write check
> that found this), [PLAN_agy_searches_through_coai.md](../research/PLAN_agy_searches_through_coai.md),
> [module_runners.md](../research/module_runners.md), [module_server.md](../research/module_server.md).

## 1. The symptom

coai launches every agy turn with `--mode plan` and treats that as read-only — the reviewer's argv even says so in a
comment (`AntigravityRuntime.cs:54`, "Read-only. The reviewer must not be able to edit the tree it is judging."). It is
not. On 2026-10-08, agy 1.3.1 with coai's exact flags (`--print= --input-format stream-json --output-format stream-json
--mode plan --model gemini-3.8-flash-low --add-dir <tree>`), cwd the tree, asked to create `SENTINEL.txt`: it called
`write_to_file` and the file was there afterwards — **Windows 1 of 1, WSL 1 of 1**, nothing in `denied_actions`
(`RESULTS_agy_searches_through_coai.md` §3).

What is exposed today:

| Launch | Roots agy is given | What notices a write |
|---|---|---|
| Question row, `question-disk` | the operator's roots — on this machine `D:/rsd` and `/home/jinx/git`, the ORIGINAL clones | nothing: a root that is not a git checkout is not watched (`root … is not a git checkout: changes there are not watched`) |
| Stuck consultant (`consult`) | the caller's checkout | the consultation's tree invariant: a change is a breach AFTER the fact; the write is not undone |
| Reviewer | a disposable worktree pinned to one SHA | nothing needed for the operator's files; a review of a tree the reviewer edited is still wrong |

A write needs the model to choose it — asked by nobody in coai's prompts, but a prompt injection inside a file the model
reads, or a model "helping", is enough.

## 2. The operator's decision (2026-10-08)

Asked with the finding: the installed settings stay as they are (no agy row switched off); the write block is measured
**with `--agent` first**; the lookup branch merges without a release.

## 3. The design — to be measured before it is built

**First candidate: a custom agent with read tools only.** `agy --help` (1.3.1) lists `--agent` ("Agent for the current CLI
session") and `agy agents` lists them. If an agent definition can name its tools, coai launches every agy turn with an
agent that has `view_file` and nothing that writes, and the write check passes. Unknown before the measurement: where an
agent is defined (a file in agy's config, per user), whether its tool list restricts BUILT-IN tools or only adds, and
whether `--mode plan` and `--agent` combine.

**What was read before any run (2026-10-08, no model call).** `agy agent` (the listing subcommand) prints nothing on this
machine, and agy's own customization guide (`~/.gemini/antigravity-cli/builtin/skills/agy-customizations/`) documents
rules, skills, plugins, hooks and MCP servers — **no agent definition with a tool list**. So `--agent` may not be a way to
restrict tools at all; M1 settles it. The same guide documents **lifecycle hooks** (`hooks.md`): a `PreToolUse` handler
receives the tool call on stdin and may answer `{"decision": "deny", "reason": …}` — "Hard block the execution
immediately". `hooks.json` is discovered in every `.agents/` folder between the cwd and the project root, or globally in
`~/.gemini/config/`; the handler runs with the hooks file's folder as its cwd (`cmd /c` on Windows, `sh -c` elsewhere).
That makes a second candidate, to be measured right after `--agent`: an ALLOWLIST hook (allow `view_file`, deny every
other tool), placed where coai controls it — a launch cwd coai owns, or a global hook that acts only when coai's own
environment marker is set (so the operator's interactive agy is untouched). Unmeasured: whether a print-mode (`--print=`)
launch runs hooks, whether a hook in a scratch cwd outside any repository is discovered, and whether a deny ends the
turn or lets the model answer in prose.

**If neither holds**, coai takes agy off disk work — a `question-disk` row and a `consult` on antigravity are refused
with a sentence naming this defect, and the operator is asked before that ships. A `permissions.deny` in agy's GLOBAL
settings is NOT a fallback: it changes the operator's own interactive agy, against the decision in §2 (plan round, codex).

**Where every agy argv is built (2026-10-09, read from the code).** Three places, not one — the plan's first draft named
`AntigravityStream.StreamingFlags`, which nothing uses:

| Launch | Built in | cwd today |
|---|---|---|
| question row, `question-disk` / `question-none` | `ConfinementPlanner.cs:161-163` (fragments) through `AntigravityConsultant.Build` | the operator's ROOT (`CwdKind.Root`) / a scratch dir |
| stuck consultant (`consult`) | `AntigravityConsultant.cs:173-176` | the caller's checkout |
| reviewer | `AntigravityRuntime.cs:56` | the pinned worktree |

The block goes into all three through ONE helper both runners call, and a test enumerates the three (plan round, gemini).

**Constraints on a hook, if M1 chooses it (plan round, gemini + codex).**
- coai never writes into the operator's agy configuration (`~/.gemini/config/`) or into a root: the hook lives in a
  per-launch folder coai owns under its data directory, which becomes the launch's cwd (the roots stay reachable through
  `--add-dir`); a question-disk row therefore moves off `CwdKind.Root`. Folders a killed launch left behind are swept at
  server start, and by age, like `runs/`.
- The handler is coai's own binary in a hook mode (`coai-mcp --agy-hook`), one implementation on every side — no `cmd`
  or `sh` script to keep in step; it ALLOWS `view_file` and DENIES every other tool, an allowlist.
- **Fail closed.** M1 also measures what agy does when the hook file is missing, not discovered, or the handler fails or
  times out. A launch whose block cannot be confirmed in place does not start: the turn fails with a named reason.

## 4. Build order

1. **M1 — measure** (`scripts/probe-agy-write-block.mjs`): the write check as in RESULTS §3, three runs per side per
   candidate, and the same runs with the lookup prompt, to show reading still works. Record in `research/`.
2. **M2 — build** what M1 proves, RED first, through one helper all three launch builders call:
   - if `--agent` holds: the agent definition coai owns, and the flag on every argv;
   - if the hook holds: the per-launch hook folder (cwd), the `--agy-hook` handler mode, the startup sweep, and the
     fail-closed check before launch;
   - if neither: the refusal of agy disk work, after asking the operator.
3. **M3 — the comment**: `AntigravityRuntime.cs:54` says what is true.
4. Docs, the whole suite, a code round, the live write check on the branch build, then the PR. A release only with the
   operator's OK.

## 5. Test plan

- Unit: the block is on every agy launch — a question-disk row, a question-none row, a consult first turn, its
  continuation (`AntigravityStream.Continue`), the reviewer — one test that enumerates the builders.
- Unit (hook track): the handler's decision for every tool name in agy's recorded streams (allow `view_file`, deny the
  rest, deny an unknown name), and the handler run as a real child process on a recorded `PreToolUse` payload.
- Fail-closed: a launch whose hook folder cannot be written does not start.
- What CI cannot prove: that agy itself honours the block — CI has no agy account. That is the live probe
  (`scripts/probe-agy-write-block.mjs`), recorded in `research/` now and re-run on every agy version bump (the version is
  part of the record); not hidden behind a green unit test (plan round, gemini).
- Live, before the PR: the write check 3 of 3 PASSED per side on the branch build, and three `ask_consultants` per side
  still answering with lookups (the block must not cost the reads).

## 6. Definition of Done

- [ ] M1 recorded in `research/` with ≥ 3 runs per side per candidate.
- [ ] Every agy launch carries the proven block; RED → GREEN with a teeth check.
- [ ] The live write check passes 3 of 3 per side; lookups still answer.
- [ ] `module_runners.md`, `module_server.md`, `module_tests.md` updated; this plan promoted.
- [ ] Whole suites green; gate rounds resolved; PR merged.
