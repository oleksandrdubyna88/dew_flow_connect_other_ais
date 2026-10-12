# RESULTS — first probe of Mistral Vibe and Kimi Code as coai runtimes (2026-10-11)

> Before any code, for [PLAN_vibe_and_kimi_runtimes.md](../todo/PLAN_vibe_and_kimi_runtimes.md). Windows 11 only — **no WSL
> side was measured**, and every cell below is ONE run, not the three per side the plan's S1 repeats. Harness: PowerShell
> by hand from a scratch folder in the session's scratchpad (never a repository checkout); `vibe` **2.26.1** (installed with
> `uv tool install mistral-vibe`, signed in on a Mistral **Pro** subscription, no API key), `kimi` (Kimi Code) **0.41.0**
> (native `~/.kimi-code/bin/kimi.exe`). Nothing outside the scratchpad was written or deleted. Total spend: about 12 short
> Vibe turns on the subscription; one Kimi call that the vendor refused.

## 1. Vibe — the headless contract

| What | Measured | Means for coai |
|---|---|---|
| Programmatic mode | `vibe -p` with no text reads the prompt from **stdin** (a 260-character prompt; a 33 KB one was not tried) | the prompt goes on stdin, as for every other CLI runtime |
| Output | `--output json` prints ONE JSON array at the end: `message/user`, `reasoning`, `effect` (tool calls, each with `state.status` and, when denied, `error.code = tool_denied`), `message/assistant`. Every element carries `sessionId` | the answer is the LAST `role: assistant` message's `content[].text`; `ReadHandle` can read `sessionId` from stdout alone |
| Exit codes | `--help`: 0 finished, 1 usage or config error, 2 infrastructure failure, 3 stopped by a limit (`--time-limit`, `--max-turns`, `--max-price`, `--max-tokens`). Observed: 0 on every finished run | exit 3 is a failure even when an assistant message exists |
| Usage | only in `export.json`, written by `--output-dir DIR` on every exit: `outcome`, `stop_reason`, `error`, `session_id`, `usage {input_tokens, output_tokens, cached_input_tokens, total_tokens}`, `cost_usd`, `steps`, `limits`, `exit_code`, the effective `config`. `--output-dir` also copies the session journal (`session/`, ~90 KB for a two-word answer) | usage needs `--output-dir`; the folder is a TREE, not a file |
| Cost | `cost_usd` 0.0119 for 7 789 tokens — Vibe's list price (`input_price`/`output_price` per model in its config), reported **also on a subscription** | an estimate, not a bill |
| Models on this plan | `mistral-medium-3.5` (alias of `mistral-vibe-cli-latest`, the active default, thinking `off`/`high`) and `glm-5-3` (`zai-glm-5-3`, "GLM-5.3 (Mistral Hosted)", thinking `off`…`max`) | no `--model` flag; `VIBE_ACTIVE_MODEL=glm-5-3` in the child's environment **took effect** (export: `active_model: glm-5-3`) |
| Resume | `vibe -p --resume <session_id>` headless: the model answered from the earlier turn (`PELICAN-42`), the SAME session id came back | a consultant can use "the vendor remembers"; whether the resumed export's usage is cumulative was NOT measured |
| Duration | 11–19 s for a short turn | — |

## 2. Vibe — what stops a write

Each arm asked the model to create a file in the working folder (and to try a shell command / `run_typescript`). A write is
blocked when the file does not exist afterwards.

| Arm | Flags | Written? |
|---|---|---|
| A | `--enabled-tools read_file` (default agent `accept-edits`) | **no** — `write_file`, `edit`, `bash` and the `file_system.*` / `process.*` connector functions answered `tool_denied` |
| B | `--agent plan` alone | **yes, into Vibe's private scratchpad** — `write_file`, `bash`, `run_typescript`, `unified_harness_scratchpad` all offered; plan mode is NOT read-only |
| C | `--agent plan --enabled-tools read_file` | **no** — 39 tool calls asked for a write through `write_file` (×7), `bash` (×10), `edit`, `process.start` and `run_typescript` → `tools.file_system.write_file` / `tools.process.start`; every one `tool_denied` (`approvalType: never`); `read_file` still read |

`read_file` reads by path; the unified harness offers **no list or search tool** in arm C.

## 3. Vibe — project hooks run shell commands when the folder is trusted

Vibe's documented hooks (`docs.mistral.ai/vibe/code/cli/hooks`): `./.vibe/hooks.toml` in the working directory loads
**only when the folder is trusted**, `~/.vibe/hooks.toml` always; a hook's `command` is a shell command. A scratch folder held
`.vibe/hooks.toml` with a `post_agent` hook running `python -c "open('<canary>','w').write('x')"`; prompt `Reply with exactly: OK`.

| Arm | Flags | Canary written? |
|---|---|---|
| control | `--trust` only | **yes** |
| full | `--agent plan --enabled-tools read_file --trust` | **yes** — the tool allow-list does not cover hooks |
| (a) | `--agent plan --enabled-tools read_file`, **no `--trust`**, working folder = the hooked folder | **no**; headless still ran and `read_file` still read the folder (`ALBATROSS-7`) |
| (b) | `--trust --workdir <empty scratch> --add-dir <hooked folder>` | **yes** — `--add-dir` trusts the folder (`--help`: "same semantics as --trust") and its project hooks load |

A first canary written as `echo hooked > '<path>'` did not appear under the full arm; with the shell-neutral `python -c`
command it did, so the first miss was the command, not a block.

Not measured: a folder trusted persistently in `~/.vibe/trusted_folders.toml` (empty on this machine), a trusted PARENT,
a junction, the user-level `~/.vibe/hooks.toml` (documented as always loaded), project MCP servers starting, project
`AGENTS.md` / `.vibe/agents` / skills loading in an untrusted folder.

## 4. Vibe — a deep `VIBE_HOME` fails

`VIBE_HOME=<scratchpad>\vibe-m4\home` (a fresh folder): exit 2, `[Errno 2] No such file or directory` on
`…\home\logs\session\unified\<uuid>\chunks\.<64 hex>.json.<16 hex>.tmp` — about 290 characters, past Windows `MAX_PATH`.
The not-signed-in wording this arm was meant to capture was NOT captured.

## 5. Kimi Code — what was learned

| What | Measured / read |
|---|---|
| Flags (`kimi --help`, 0.41.0) | `-p, --prompt <prompt>` (an ARGUMENT), `-m, --model <alias>`, `--output-format text\|stream-json`, `--plan`, `--agent <name>`, `--agent-file <path>`, `--add-dir`, `-S, --session [id]`, `-c`, `--skills-dir`, commands `login` (device code), `provider`, `session`, `export`, `upgrade` |
| `--plan` with `-p` | **refused**: `error: Cannot combine --prompt with --plan.` (exit 1). The command reference: `--prompt` cannot be used with `--yolo`, `--auto` or `--plan`, because prompt mode "uses `auto` permission by default"; "In `-p` mode, no human approval is requested … static deny rules remain in effect" |
| Configured account | `~/.kimi-code/config.toml`: provider `moonshot-ai`, `base_url https://api.moonshot.ai/v1` (an API-key provider), models `moonshot-ai/kimi-k2.7-code` (default) and `moonshot-ai/kimi-k2.6`. The command reference names `kimi-code/kimi-for-coding` as the `-m` example for the signed-in service |
| A call | `"…" \| kimi -p - --output-format stream-json`: first line `{"role":"meta","type":"system.version","version":"0.41.0"}`, then exit 1 with `provider.api_error: 429 Your account … is suspended due to insufficient balance, please recharge your account or check your plan and billing details` |
| Not settled | whether `-p -` read stdin or sent a literal `-`; the stream's answer / usage / session fields; the agent-file tool list; trust, hooks and project config; exit codes in prompt mode |

The 429 above is a **billing** failure. coai's `RateLimit.Hit` (`src_mcp/runners/Reviewers/ReviewerExecutor.cs:284`) matches
a real 429 and would retry it on the ladder as a rate limit; `RateLimit.Spent` (`:304`) does not name it, so it is not
treated as hopeless either.

## 6. Where these results go

Every decision they feed is in [PLAN_vibe_and_kimi_runtimes.md](../todo/PLAN_vibe_and_kimi_runtimes.md) §2. Every number
here is n=1 on Windows; S1 and S3 of the plan repeat the confinement cells 3 of 3 per side with a script before any claim
of read-only is written into the product.
