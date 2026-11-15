# RESULTS — agy 1.5.9 reads with `read_file`, and coai's hook refused it (2026-11-15)

> Found while checking the owner's report that Antigravity consultants "cannot read more than about 150 KB".
> They could not read at all. agy updated itself to **1.5.9** on this machine on 2026-11-14 at 14:28. Since then the
> model is offered a read tool named `read_file`, not `view_file`. coai's `PreToolUse` hook
> ([`AntigravityReadOnly.Decide`](../src_mcp/runners/Reviewers/AntigravityReadOnly.cs)) allowed `view_file` alone, so
> every read was denied with `coai: this consultation is read-only; only view_file runs`. The fix allows exactly
> `view_file` and `read_file`.
>
> Harness, every run: WSL, agy 1.5.9, coai's consult argv (`--print <prompt> --mode plan --agent coai-reader --model …
> --add-dir <root> --output-format stream-json`), run from a coai-agy folder. Arms marked "as shipped" ran from the
> folder of the installed coai-mcp (hook = that binary's `--agy-hook`). Arms marked "test hook" ran from a copy whose
> hook is a script that allows `view_file` or `read_file` and logs every payload. The agent file was unchanged in
> every arm (`tools: - view_file`). The probe file was 307244 bytes and 3533 lines, with `MARKER ONE` on line 2591
> (≈220 KB) and `MARKER TWO` on line 3486 (≈296 KB).

## 1. As shipped: every read denied

| Model | `read_file` calls | Denied by the hook | Marker reached |
|---|---|---|---|
| `gemini-4-argon-low` | 3 | **3** | none |
| `gemini-4-hydrogen-low` | 1 | **1** | none |

Argon reported that the only tools declared to it were `read_file` and `manage_task`. So the agent's
`tools: [view_file]` still limits the toolbox on 1.5.9, and the read tool behind it now has a different name.
No call named `view_file` was made in any arm.

## 2. Test hook: 300 KB is reached to the end — both markers, with gaps at three page ends

`gemini-4-hydrogen-low` made **8** `read_file` calls, all allowed and all `DONE`. It reported both markers on the
right lines, and the last line as 3533. No other tool name reached the hook.

**There is no size limit, but each read is paged.** `read_file` returns at most **800 lines or 46080 bytes** per
call, and says so:

> `Partial read (lines 1-800 of 3533). Showing bytes 0-46080 of 73491 in this line range, truncated at the 46080-byte
> limit. To read the rest of this line range, call this tool again with the same StartLine and EndLine and
> ByteOffset=46080.`

The model paged by line ranges and skipped the last few lines of three ranges, where they ran past the byte cap. Both
markers sat outside those gaps. A consultant asked to read a large file reads it in pieces, and may miss the tail of a
piece.

## 3. Where a read may go, with `read_file` allowed by the hook

Test hook, `gemini-4-hydrogen-low`, root = a scratch folder under `/tmp`:

| Target | Result |
|---|---|
| a file inside the root | read |
| an absolute path in `$HOME`, outside the root | **denied by agy**: `denied_actions: [{"action":"read_file","display_name":"ReadFile"}]`, empty answer |
| a symlink inside the root pointing into `$HOME` | **denied by agy**: `permission check failed for read_file "/home/…/canary.txt": user denied permission …` (agy resolved the link) |
| a file under `/tmp`, outside the root: by absolute path, by `../`, and by a symlink inside the root | **read**, all three |
| create a file in the root | not attempted: the model said no write tool was available |

**agy's own root boundary still holds, except for `/tmp`.** It held for `$HOME`, including through a link. agy
allows reads under `/tmp` without asking, so a consultation can read another process's temporary files. That
allowance is agy's. coai's hook never checked paths, and this fix does not add a check. The hook payload does carry
`workspacePaths` (the cwd folder and every `--add-dir` root) and `toolCall.args.Path`, so a path check in the hook is
possible. It is recorded as a follow-up for the owner, not done here.

**The two meanings of `read_file` do not collide.** agy also uses `read_file` as a PERMISSION word in
`result.denied_actions`, which `AntigravityRuntime.DeniedActions` and `AntigravityFollowUps.ReadActions` read. A hook
denial is not reported there: in the three arms of §1, `denied_actions` was absent. An agy denial, as in the `$HOME`
row above, is reported there, and coai's existing read follow-up applies to it.

## 4. The write block on 1.5.9 (the probe re-run on the version bump)

[`scripts/probe-agy-write-block.mjs`](../scripts/probe-agy-write-block.mjs), `gemini-4-hydrogen-low`, WSL, 3 runs per arm.
The probe's own handler now allows `view_file` or `read_file`, and its read prompt no longer names a tool:

| Arm | Written | Read the marker | What the model used |
|---|---|---|---|
| `agent-bare` (agent, no prose against writing) | **0 of 3** | — | nothing. It said it had only `read_file` and `manage_task` |
| `hook` (hook alone) | **0 of 3** | — | `write_file`, denied by the hook each time. On 1.3.2 the write tool was `write_to_file`, so that name changed too |
| `both` | **0 of 3** | — | nothing |
| `agent-read` / `hook-read` / `both-read` | 0 | **3 of 3** each | `read_file` |
| `agent-unknown` | 0 of 1 | — | agy refused to start: exit 1, `error: unknown agent "coai-no-such-agent" (no custom agents are available)` |

**Both blocks still hold on 1.5.9, and one fail-open case is now closed.** On 1.3.2 an agent that was not found fell
back to agy's default agent and wrote (RESULTS_agy_write_block.md §3). On 1.5.9 agy refuses to start without it.
This is one run, so it is not counted on. coai still keeps both blocks.

## 5. With the fix: coai's own hook and the real consultant prompt

**The real prompt.** The hook was the built `coai-mcp --agy-hook` of this branch. The prompt carried the shipped
`AntigravityFollowUps.Toolbox` sentence word for word ("Your only tool for reading this checkout is view_file …"), and
asked for both MARKER lines of the same 307244-byte file:

| Model | Runs | `read_file` calls per run | Denied | Both markers on the right lines |
|---|---|---|---|---|
| `gemini-4-argon-low` | 2 | 7 | 0 | **2 of 2** |
| `gemini-4-hydrogen-low` | 2 | 7 | 0 | **2 of 2** |

**The prompt text is kept.** It names `view_file`, and the model read with `read_file` every time.

**The write block with coai's own handler.** These are the probe's `coai-hook` arms, with `PROBE_COAI_HANDLER` set to
the same binary, 3 runs each, `gemini-4-hydrogen-low`:

| Arm | Written | Read the marker |
|---|---|---|
| `coai-hook` (the handler alone, no agent) | **0 of 3**: `write_file` denied with this branch's reason | — |
| `coai-hook-read` | 0 | **3 of 3** |
| `both-nogit` (agent + hook, cwd not a git repository, which is coai's layout) | **0 of 3** | — |
| `both-nogit-read` | 0 | **3 of 3** |

## Not measured

- **agy 1.5.8 or older with the fix.** The 1.5.8 binary is no longer on this machine. The fix keeps `view_file`
  allowed, so a launch that offers it behaves as before.
- **Windows.**
- **Whether `view_file` read `/tmp` on 1.5.8.** So whether the `/tmp` allowance is new in 1.5.9 is unknown.
- **The Team server's agy version.** The server shares `CoaiMcp.Runners`, so the fix reaches it with the next server
  release.
