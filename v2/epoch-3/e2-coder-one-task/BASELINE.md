# Epic e2-coder-one-task, story s6, task t1 — the baseline: how the three numbers are read, and one task done by the main AI itself

Recorded 2026-11-25. This file holds numbers, times, commit ids and one-line task descriptions only. It quotes no prompt, no transcript text and no file content.

The epic says the coder is better when the main AI uses fewer tokens, spends less time and does not accept more gate findings than it did when it did the same kind of task itself. Section 1 says how each number is read. Section 2 holds the baseline. Section 3 names the task for t3.

## 1. How each number is read

A second person who follows these steps on the same files gets the same numbers.

### (a) Main-AI token count

- **Source.** The Claude Code session transcript of the main AI: `~/.claude/projects/<folder slug>/<session id>.jsonl`. The folder slug is the working folder the session was started in, with `/` written as `-`. Read the top-level file of the session only. A subagent has its own file under `<session id>/subagents/`. It is not the main AI and it is not counted. A line with `isSidechain: true` is not counted.
- **Window.** From the first event of the first turn of the task to the last event of the last turn of the task. A turn starts at a `user` event that is a prompt (its content holds no `tool_result`). A wake-up of a scheduled loop is a prompt too. A turn ends at the last `user` or `assistant` event before the next prompt. Write the first and last event time (UTC, from the `timestamp` field) in the result.
- **What is counted.** One API response is one `message.id`. A response is written as several `assistant` events (one per content block) that share the id; take the `message.usage` of the last event of the id. Ignore a response whose `message.model` is `<synthetic>` (it has no usage). Sum these fields of `message.usage` over all responses of the window: `input_tokens`, `cache_creation_input_tokens`, `cache_read_input_tokens`, `output_tokens`.
- **Headline number.** `fresh tokens` = `input_tokens` + `cache_creation_input_tokens` + `output_tokens`. Cache reads are 97 % of all tokens and they measure how long the context was, not how much work was done, so they are written next to the headline and are not part of it. `all tokens` (the four fields) is written too. A result must write all four fields, so a reader can use another sum.
- **Not counted.** The tokens of the gate reviewers and of the consultants (other vendors, the gate's own cost), and the tokens of the coder in t3. They are written in their own lines. What the main AI reads from them is in its own context, so it is counted.
- **The reader.** `node` on the transcript (no Python):

```js
// node read-main-ai.mjs <session.jsonl> <startIso> <endIso> [idleMinutes=10]   (startIso/endIso like 2026-11-07T16:23:02.000Z)
import fs from 'fs';
const [file, start, end, idle = '10'] = process.argv.slice(2);
const events = fs.readFileSync(file, 'utf8').split('\n').filter(Boolean).map(l => { try { return JSON.parse(l); } catch { return null; } })
  .filter(e => e && e.timestamp && (e.type === 'assistant' || e.type === 'user') && e.timestamp >= start && e.timestamp <= end);
const byId = new Map();
for (const e of events) if (e.type === 'assistant' && e.message?.usage && e.message.model !== '<synthetic>') byId.set(e.message.id, e.message.usage);
const s = { responses: byId.size, input: 0, cacheCreation: 0, cacheRead: 0, output: 0 };
for (const u of byId.values()) { s.input += u.input_tokens || 0; s.cacheCreation += u.cache_creation_input_tokens || 0; s.cacheRead += u.cache_read_input_tokens || 0; s.output += u.output_tokens || 0; }
const t = events.map(e => Date.parse(e.timestamp)).sort((a, b) => a - b);
let active = 0; for (let i = 1; i < t.length; i++) if (t[i] - t[i - 1] < idle * 60000) active += t[i] - t[i - 1];
console.log(JSON.stringify({ ...s, freshTokens: s.input + s.cacheCreation + s.output, first: new Date(t[0]).toISOString(), last: new Date(t.at(-1)).toISOString(),
  elapsedMin: +((t.at(-1) - t[0]) / 60000).toFixed(1), activeSec: Math.round(active / 1000) }));
```

### (b) Wall time

Two numbers, from the same events as (a). The headline is the first.

- **Active time** (headline). The sum of the gaps between two consecutive `user` or `assistant` events of the window, for every gap shorter than 10 minutes. A gap of 10 minutes or more is idle time (the main AI was waiting for CI, a scheduler or a person) and is not counted. It is the time the main AI itself spent: reading, writing, running its own commands and waiting for a gate round. The reader above prints it as `activeSec`.
- **Elapsed time.** From the first event of the window to the end of the task. For a task that ends at a merge, the end is the `mergedAt` time of the pull request (`gh pr view <n> --json mergedAt`). Write both end points.
- A tool call that only waits (`role_status` with `maxSeconds`, a gate round) is inside the active time, because its events are less than 10 minutes apart. t3 must list the time of its `role_status` waits on their own line, so a reader can see how much of the coder's run the main AI waited for.

### (c) Accepted gate findings

- **What counts.** A finding that the main AI, as the caller of the gate, answered with `accept` in a `resolve` call, for a round of the task's own session (the repository and the branch the task was done on). A finding answered with `reject`, and a finding not answered, do not count. A round with no findings counts 0. Plan rounds and code rounds are counted together and also written apart. The coder's own work is never counted as a finding.
- **Where it is read.**
  1. The rounds database of the machine that ran the gate (`coai.db` in the data folder). Per round, `rounds.accepted` and `rounds.rejected` are the counts the caller wrote with `resolve` (`-1` means the caller never answered). The same number is `count(*)` of `findings` with `resolution = 'accept'` over the rounds of the session (`sessions.repo_path` and `sessions.branch`). `coai-mcp --log` prints the `accepted` and `rejected` of each round with its repository, branch, stage and number. `coai-mcp --findings --session <id> --stage <stage> --number <n>` prints one round's findings with their `resolution`.
  2. For a task of a v2 work plan, `coai-mcp --plan-report <feature-dir> --json` gives, per plan folder (feature, epoch, epic, story, task), the gates run, the rounds, the cons and consask counts and the wall time of the calls that named the folder in their `workItem` argument. It holds no accepted count; take that from (1) for the rounds it counts. A call with no `workItem` is not in the report.
  3. When the database is not there, the `mcp__coai__resolve` calls of the main AI's transcript hold the same answers: the `decisions` argument is a JSON text, a list of `{finding, action: accept|reject, reason?}`. Count `action: accept`.
- **The baseline below was read with (3).** No `coai.db` of November 2026 is on this machine (checked: the WSL data folder and the Windows data folder hold no `coai.db`), so (1) and (2) were not run for it. The verdicts and counts of the rounds come from the same transcript, from the answers of `review_plan` and `review_code`.

## 2. The baseline task, done by the main AI itself

| | |
|---|---|
| Task | A test pinned to "the process is elevated" failed on the Windows release run (mcp-v0.65.0, win-x64 and win-arm64) when the suite ran from Git Bash. Fix: the test now reads who owns a new file, and skips with a reason where the process owns it. One CI step reproduces the release's shell. One module-doc section records it. |
| Kind | `code` (a bug fix with a test), the same `kind` as a task of `task.yaml`. |
| Complexity | 4 of 10. This is a judgement, because the work method gives no scale. The reasons: one test file changed by 15 lines, one CI step of 14 lines, one doc section; the cause had to be found from a failed remote run on an operating system this machine does not run; the fix was proved by CI only. Other tasks of this plan, for the same scale: `t4-this-repository-file` 3, `t1-toolchain-schema-reader` 5, `t2-fix-turns` 8. |
| Repository, commit | This repository. Pull request #962, squash-merged on main as `72adc50c` (3 files, +57 -1: `src_mcp/tests/RunTempTests.cs`, `.github/workflows/ci.yml`, `research/module_tests.md`). The five commits of the branch: `7064dfb4` (the red CI step), `7106884d`, `151274ca`, `77a72d8a`, `56ccbfe1`. |
| Who did it | The main AI itself: Claude Code 2.1.319 in the VS Code extension, model `claude-opus-5-5`, effort high. The transcript has 0 sidechain lines and the session used no subagent for this task (it called the Agent tool 0 times in the window). The gate's reviewers and consultants are other vendors' models and are not the main AI. |
| Transcript | The session file whose name starts with `88d40f91` in the project folder of the conventions repository (the folder the session was started in; it worked in a worktree of this repository). |

### 2.1 The three numbers

| Number | Value |
|---|---|
| (a) fresh tokens | **323,072** = input 108 + cache creation 285,546 + output 37,418 |
| (a) cache read | 12,619,098 |
| (a) all four fields | 12,942,170, in 51 model responses |
| (b) active time | **894 s (14.9 min)** |
| (b) elapsed time | 222.2 min: first event 2026-11-07 16:23:02 UTC to `mergedAt` 20:05:15 UTC. The last main-AI event is at 19:31:02 UTC; the last 34 min was the CI wait before the merge. |
| (c) accepted findings | **3**: 1 of 5 in the plan round, 2 of 3 in the first code round, 0 of 0 in the second code round. The other 5 findings were rejected with a reason. |

The window is three turns. A prompt that is a loop wake-up starts each of them. The idle gaps between them are 140.3 min (the first CI run) and 32.8 min (the second); the loop wakes by itself at those times.

| Turn | From (UTC) | To (UTC) | Active | fresh tokens | what the main AI did |
|---|---|---|---|---|---|
| 1 | 16:23:02 | 16:25:16 | 134 s | 32,995 (input 26, cache creation 23,223, output 9,746) | read the failed release run, found the cause, added the red CI step, committed it |
| 2 | 18:45:32 | 18:52:58 | 446 s | 265,189 (input 56, cache creation 246,000, output 19,133) | wrote the test fix, built and ran the suite, ran the plan gate and the first code gate, fixed one finding, committed, pushed |
| 3 | 19:25:48 | 19:31:02 | 314 s | 24,888 (input 26, cache creation 16,323, output 8,539) | fixed the test's skip check, built and ran the suite, ran the second code gate, started the wait for CI and the merge |

Cache reads of the turns: 2,656,877, 6,625,233 and 3,336,988.

### 2.2 The gate rounds of the task

| Round | Verdict | Reviewers | Findings (severity) | Gating / threshold | Accepted / rejected | Gate tokens (in / out) |
|---|---|---|---|---|---|---|
| plan 1 | proceed | 2 of 2 | 5 (1 blocking, 4 major) | 5 / 6 | 1 / 4 | 72,788 / 3,378 |
| code 1 | proceed | 8 of 8 | 3 (2 major, 1 minor) | 2 / 5 | 2 / 1 | 306,363 / 4,250 |
| code 2 (`again`) | proceed | 8 of 8 | 0 | 0 / 5 | 0 / 0 | 298,392 / 8,776 |

For t3, the code gate on the coder branch inherits the feature's plan (epic story s5), so no plan round runs there. t3 compares all accepted findings of the rounds that ran for its task (the baseline's 3) and also writes the code rounds apart (the baseline's 2).

### 2.3 Limits of this baseline, said plainly

- **One task, one run.** The earlier runs of this repository show how large the noise between two identical runs is: two equal arms differed by 11 to 16 % in cost (`RESULTS.md` of the `coai-exp` experiment folder next to this checkout, 2026-10-25, outside this repository). A difference of a few percent between t3 and this baseline is inside the noise.
- **A rare case.** In this repository the main AI nearly always gives code work to a subagent. A scan of every local top-level transcript (all project folders, sessions of 2026-10-25 to 2026-11-25) for bursts of edits by the main AI itself to `src_mcp`, `src_server`, `src_vs_code` or `src_bugs`, with a test file edited, at least one commit, no Agent call and edits less than an hour apart, found five. Two are of one to three edits to one file in one or two minutes. Two are long runs of many commits (2 h and 7 h, nine and six files) and are not small tasks. The fifth is this task. It is the only one of a named kind and complexity 3 to 5 with a commit on main and a clean window.
- **Mixed turns.** The three turns also hold a few commands of other work of the same loop session (status checks). They are not separable by time. The window has 62 tool calls and 0 Agent calls; about 9 of its 39 shell calls name neither the task's worktree, its test file, its CI file nor a run, so they may be that other work. They make the baseline a little higher, so a t3 result below it is the safe direction.
- **The task is a test fix, t3's is a code fix.** Both are `code` tasks of complexity 3 to 5 with a test that is red first. This one touched a CI step and a doc beside the test; t3's task has a smaller diff by design (one file of code, one of tests). A smaller diff makes t3 look better. t3 must say so in its results if a number is below the baseline by less than the difference in size.
- **The model and effort.** `claude-opus-5-5`, effort high. t3's main AI should be the same model and effort; if it is not, t3 writes which.
- **Cost in USD** is not read. The transcript holds no price, and the coder's own usage is in tokens too, so the comparison is in tokens.

## 3. The task for t3

**`WorktreePaths.Same` compares two real paths with the case rule of the file system** — a real, open, small fix of this repository, written down on 2026-11-01 and not done (section 3a of `todo/PLAN_a_delete_is_pinned_to_the_folder_it_judged.md`; `src_mcp/runners/Worktrees/WorktreePaths.cs` still compares with `OrdinalIgnoreCase` on every operating system).

- **Kind and complexity.** `code` (a bug fix with a test), complexity 4: within 1 of the baseline's 4.
- **What is wrong.** On Linux `/x/Repo` and `/x/repo` are two folders, and `Same` says they are one checkout. It matches records only (a worktree that git lists against a spelled path, a consultation's repository against a review's); it gates no read and no delete.
- **What must be true when it is done.** `Same` compares with `Ordinal` on Linux and with `OrdinalIgnoreCase` on Windows and macOS, as `WorktreeManager`'s guard already does. A test that is red first on Linux: `Same("/x/Repo", "/x/repo")` is false for two real folders that differ only by case, and is true for one folder reached by two spellings of the same case. The other tests of `src_mcp/tests` that call `WorktreePaths.Same` stay green.
- **Files.** `src_mcp/runners/Worktrees/WorktreePaths.cs` and a test in `src_mcp/tests/AWorktreePathIsComparedByItsRealPathTests.cs`. Nothing else: the one `todo` line that says the item is done is for the main AI to change after the coder's branch is merged, so the coder's diff stays small.
- **Why it can be checked by `.coai/toolchain.yaml`.** The fix is in `src_mcp` and the test is in `CoaiMcp.Tests`, so the build and the test executable of the toolchain file are what proves it. The other open items of `todo/` that are small are in `src_vs_code` (they need a VS Code host scenario, which the toolchain file does not run).
- **A reader's check before t3 starts.** `git grep -n OrdinalIgnoreCase -- src_mcp/runners/Worktrees/WorktreePaths.cs` still prints the line in `Same`. If a later change has fixed it, t3 takes the next small item and says so in its results.
