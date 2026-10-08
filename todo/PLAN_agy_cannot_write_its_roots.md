# PLAN — an antigravity launch cannot write inside the roots it is given

> Status: **plan only, nothing implemented yet, 2026-10-08.** Scope: how coai launches agy — the consultant
> (`src_mcp/runners/Consultation/AntigravityConsultant.cs:152` `Build`), the question rows that use it, and the reviewer
> (`src_mcp/runners/Reviewers/AntigravityRuntime.cs:48`) — plus a measurement script and tests.
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

**If neither holds**, in order: a `permissions.deny` for the write permission in agy's global settings (the
global file is read — `RESULTS_agy_allow_rule.md` — but it also blocks the operator's own interactive agy); then taking
agy off disk work (codex/claude rows, whose confinement is measured).

## 4. Build order

1. **M1 — measure** (`scripts/probe-agy-write-block.mjs`): the write check as in RESULTS §3, three runs per side per
   candidate, and the same runs with the lookup prompt, to show reading still works. Record in `research/`.
2. **M2 — build** what M1 proves: the argv change in ONE place (`AntigravityStream.StreamingFlags`, which both the
   consultant and the reviewer share), the agent file written by coai if one is needed, RED first.
3. **M3 — the comment**: `AntigravityRuntime.cs:54` says what is true.
4. Docs, the whole suite, a code round, the live write check on the branch build, then the PR. A release only with the
   operator's OK.

## 5. Test plan

- Unit: the argv carries the block on every agy launch — first turn, continuation (`AntigravityStream.Continue`), reviewer.
- Live, before the PR: the write check 3 of 3 PASSED per side on the branch build, and three `ask_consultants` per side
  still answering with lookups (the block must not cost the reads).

## 6. Definition of Done

- [ ] M1 recorded in `research/` with ≥ 3 runs per side per candidate.
- [ ] Every agy launch carries the proven block; RED → GREEN with a teeth check.
- [ ] The live write check passes 3 of 3 per side; lookups still answer.
- [ ] `module_runners.md`, `module_server.md`, `module_tests.md` updated; this plan promoted.
- [ ] Whole suites green; gate rounds resolved; PR merged.
