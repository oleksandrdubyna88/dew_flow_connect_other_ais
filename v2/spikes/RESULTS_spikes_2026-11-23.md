# coai v2 spikes - results, 2026-11-23

Facts only. No product code. Scratch files: `/tmp/claude-1000/-home-jinx-git-dew-flow-conventions/4c91ec34-3816-4f0d-83a8-d95afa710fba/scratchpad/spikes/` (s1 = hook spike, s2 = systemd spike). No real config was edited. The CLI was started in a scratch project folder with a project-level `.claude/settings.json`. No session env variable was unset (`CLAUDE_CODE_CHILD_SESSION=1` stayed set).

Environment: `claude --version` = `2.1.330 (Claude Code)`; node v26.12.1; WSL2 kernel 6.18.58.1; tmux 3.6; systemd 259.

## SPIKE 1 - AskUserQuestion answered by a PreToolUse hook

Setup: `s1/.claude/settings.json` = PreToolUse hook, matcher `AskUserQuestion`, command `node .../s1/hook.mjs`. The script reads stdin JSON, appends it to `hook.log`, prints
`{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"allow","updatedInput":{"questions":[...original...],"answers":{"<question text>":"<first option label>"}}}}`.

### 1a. Non-interactive (`-p`) - AskUserQuestion NOT available
```
claude -p "Use the AskUserQuestion tool to ask me which color I prefer, red or blue, then tell me what I answered." --output-format stream-json --verbose
claude -p "<same>" --tools AskUserQuestion --output-format stream-json --verbose </dev/null
```
- Run 1: the model tried `ToolSearch select:AskUserQuestion` -> "No matching deferred tools found". The init `tools` list had no AskUserQuestion. The model said the tool is not available and the session is non-interactive.
- Run 2 (`--tools AskUserQuestion`): the init list still held only MCP tools (198), no built-in tool, no AskUserQuestion. Same answer from the model.
- The hook never fired in `-p` mode (no `hook.log`). The hook cannot answer a tool that is not offered to the model.
- No refusal because of `CLAUDE_CODE_CHILD_SESSION`: `-p` runs started and finished (rc=0).
- Note: `-p` with no stdin prints "no stdin data received in 3s"; use `</dev/null`.

### 1b. Interactive in tmux (works)
`tmux new-session -d -x 200 -y 50 -c <s1 folder> claude`. No child-session refusal. The only dialog: the one-time "trust this folder / this folder configures hooks" prompt (answered "Yes" by key press; it is about the scratch folder only). Then prompt sent with `send-keys -l ... ; Enter`.

Result, full answer (hook1.log, `/.../s1/hook1.log`):
- The hook FIRED. Stdin JSON keys: `session_id, transcript_path, cwd, scratchpad_dir, prompt_id, permission_mode ("auto"), effort, hook_event_name:"PreToolUse", tool_name:"AskUserQuestion", tool_input, tool_use_id`.
- Exact `tool_input` shape:
```
{"questions":[{"question":"Which color do you prefer, red or blue?","header":"Color",
  "options":[{"label":"Red","description":"You prefer the color red."},{"label":"Blue","description":"You prefer the color blue."}],
  "multiSelect":false}]}
```
  (`header` and `description` are present; `multiSelect` is a boolean per question.)
- Screen after the run: `Answered Claude's questions: - Which color do you prefer, red or blue? -> Red`. No question widget was seen. The tool finished in under 5 s, and the pane was read after it; a flash within that time could not be ruled out by screen reading, but the run had no input from me.
- Transcript (`~/.claude/projects/<scratch>/d17fe4be-...jsonl`, read only): the model-side `tool_use.input` has the ORIGINAL `questions` only (no `answers`). The `tool_result` is a NORMAL result (no `is_error`):
  `Your questions have been answered: "Which color do you prefer, red or blue?"="Red". You can now continue with these answers in mind.`
  The transcript `toolUseResult` = `{"questions":[...],"answers":{"Which color do you prefer, red or blue?":"Red"}}`.
- The model said: "You answered Red."

### 1c. Partial answers (2 questions, answer only the first)
Hook set `COAI_SPIKE_PARTIAL=1` (answers for question 1 only). Two questions in one call (color, animal).
- Hook fired once with both questions in `tool_input.questions`.
- No widget for the second question; no error. The tool_result was normal and held only the answered one:
  `Your questions have been answered: "Which color do you prefer?"="Red". You can now continue with these answers in mind.`
  `toolUseResult.answers` = `{"Which color do you prefer?":"Red"}`.
- The model noticed: "I did not get an answer to this question [animal]". So a partial answer is silent for the user (no prompt for the rest); the model must ask again.

### 1d. Empty answers (`"answers":{}`) - extra check
Hook set `COAI_SPIKE_NONE=1`.
- tool_result (not an error): `The user did not answer the questions.` `toolUseResult.answers` = `{}`. No widget was seen (pane checked at 6, 12, 18 s: idle prompt). The model said it got no answer.

### Not measured / limits
- Only `permission_mode: auto` was tested. Not tested: `default`/`plan` modes, `multiSelect:true` answer format (the documented format for several labels), a free-text "Other" answer value that is not an option label, a hook that returns a label not in the options, and hook timeout/failure behaviour (hook exit non-zero, no JSON).
- A widget flash in-flight could not be seen (polling the pane only after the fact, in 1b/1c).
- `-p` mode: not possible to test the hook because the tool is not offered. This is a fact of version 2.1.330 with this account and MCP set; other flags (`--input-format stream-json`, `--permission-prompts`) were not tried.
- Model used in the interactive runs: Opus 5.5 (the scratch CLI default), not controlled.

## SPIKE 2 - `systemd-run --user` on this WSL distro

- `systemctl --user is-system-running` -> `running`. PID 1 = systemd. `loginctl show-user 1000` -> `Linger=yes State=active`.
- cgroup v2 user controllers available: `cpu memory pids` (`user@1000.service/cgroup.controllers`).
- Basic unit:
```
systemd-run --user --collect --unit coai-spike-$$ -p MemoryMax=200M /bin/sleep 5
-> Running as unit: coai-spike-2049764.service
systemctl --user status "coai-spike-*"
-> Active: active (running); Memory: 1.7M (max: 200M, available: 198.2M); CGroup: /user.slice/user-1000.slice/user@1000.service/app.slice/coai-spike-2049764.service
systemctl --user show <unit> -p MemoryMax -> MemoryMax=209715200
```
  After 6 s the unit was gone (`--collect`): `Unit ... could not be found`. Note: `status` of a finished `--collect` unit gives not-found, not an exit status; use `--wait` or no `--collect` to read the result.
- MemoryMax enforcement: `alloc.mjs` allocates 40 x 10 MB buffers (400 MB), logging each step.
  - `systemd-run --user --collect --wait --unit coai-spike-mem-$$ -p MemoryMax=200M -p MemorySwapMax=0 node alloc.mjs`
    -> KILLED at about 180 MB allocated (rss 229 MB): `Finished with result: oom-kill`, `Main processes terminated with: code=killed, status=9/KILL`, `Memory peak: 200M (swap: 0B)`, journal: "The kernel OOM killer killed some processes in this unit."
  - Same with `MemoryMax=200M` only (no MemorySwapMax): NOT killed. `Finished with result: success`, `Memory peak: 200M (swap: 215.2M)`. The WSL swap (4 GB) absorbed the excess. So MemoryMax alone is a soft limit here; add `-p MemorySwapMax=0` to make it a hard kill.
- Survival after the launching shell exits:
  `bash -c "systemd-run --user --collect --unit coai-spike-surv-$$ -p MemoryMax=200M /bin/sh -c 'sleep 40; echo survived > .../surv.txt'"` -> the launcher shell exited at once; the unit was `active` (main PID parent = 385 = the user systemd manager, not the shell); after 40 s `surv.txt` held `survived`. The unit runs in its own cgroup under `app.slice`, so it does not die with the launching shell.
- Cleanup: all units finished by themselves (`--collect`); `systemctl --user list-units 'coai-spike-*' --all` -> 0 units. The scratch tmux session `coaispike` was killed.

### Not measured / limits
- Not tested: survival when the whole Claude Code session/process tree is killed (only shell exit was tested); survival across a WSL shutdown/`wsl --terminate` (Linger=yes is set, but a WSL restart stops the distro and its units); CPUQuota/TasksMax enforcement; `systemd-run --user` from a non-login/cron context; the OOM behaviour with the real WSL memory pressure from other processes (about 6 GB used, 9.8 GB available at test time).
