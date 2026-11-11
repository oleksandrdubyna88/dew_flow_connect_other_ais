# PLAN — E2.S2 spec: the remote reader's contract — the ssh argv, the stdin script and the typed result (CORNERSTONE)

> Status: **plan only, nothing implemented yet, 2026-11-11 — written by Fable before its code; reviewed by Opus 2026-11-11
> (gate G6), its findings fixed in this text; change 2.1 decided (§ S2.7).**
> Cornerstone spec of story E2.S2 of [PLAN_the_chat_opens_from_a_terminal_E2.md](PLAN_the_chat_opens_from_a_terminal_E2.md)
> (parent [PLAN_the_chat_opens_from_a_terminal.md](PLAN_the_chat_opens_from_a_terminal.md), § *Stage 2*, § *Security*, ledger
> entry 1.1). It holds the security judgement on the ssh edge (gate G2 + G4, § S2.6), which must stand before any S2 code.
> Its own file, because the stories document goes whole to a review gate and must stay short. It keeps no change ledger: a
> change is an entry in the parent (epoch 2, 1 change: 2.1; the next accepted change is a full stop). It is promoted with
> the E2 document.
>
> Scope: `src_vs_code` only. NEW `src/remoteScript.ts`, `src/remoteFrame.ts`, `src/remoteSsh.ts`; `src/versionProbe.ts`
> (`capture`, one options object); `src/processLauncher.ts` (one option, one exported function); tests;
> `research/module_extension.md`, `research/module_tests.md`, `research/architecture.md`. Coder: Sonnet. Review: Opus.
> Read at `b51dd174` (E2.S1 is committed as `09bd1faf` and `b51dd174`); every file:line of § S2.0 re-checked at
> `9f16332c` by the G6 review (no `src_vs_code` change between). All paths are under `src_vs_code/`, but
> `research/`, `todo/`, `.github/`, `SECURITY.md` and `raw-temp-baseline.json`, which are from the repository root.

## Changes to § 4 (for gate G6)

The caller puts these into § 4 of the stories document and into the S3 and S4 briefs in ONE commit, before S3 or S4 is coded.
The rows are X1…X10 (not G: the stories document uses G for its gates).

| # | Contract | § 4 says | This spec says | Why | Who must follow |
|---|---|---|---|---|---|
| X1 | C2, the file | `readRemote` and `RemoteRead` are in `src/remoteScript.ts` | They are in NEW pure `src/remoteFrame.ts`. `src/remoteScript.ts` holds the script, the operands and the quoting | The script is 293 lines; one file would pass the 800-line limit (`eslint.config.mjs:113`) | S3 imports `readRemote` from `./remoteFrame` |
| X2 | C2, the parser | `readRemote(stdout: string, ...)` | `readRemote(run: { code: number; stdout: string }, call: RemoteCall): RemoteRead` | An exit code that is not 0 is `unreadable`, decided in one place. The parser must know what was asked | S3 passes the `answered` run and the same `call` it built the operands from. S3 never reads `code` |
| X3 | C2, the arms | `fallback` holds "the pane's `cwd` and the folder names, or the title rows" | Two arms: `fallback` (the pane's `cwd` and the folder names) and NEW `titles` (the title rows) | No arm is a field to interpret | S3 `nextStep`: `fallback` → call (ii); `titles` → call (iii), `pick` or a refusal |
| X4 | C2, the fields | — | `found` gains `via: 'registry' \| 'id'` and `folder`; `absent` gains `what: 'session' \| 'transcript'`; `ambiguous` gains `why` and `unnamed`; `unreadable` gains `why` (a closed list, with `UNREADABLE_SAID`) | S3 must know a registry success (it drops the picked id), and must say why a read failed | S3 (`nextStep`, the sentences) |
| X5 | C2a, the operands | "The spec fixes the operands" | `RemoteCall` (three kinds), `operandsOf(call)`, `callOf(operands)`. The forms: `<session>`; `<session> id <uuid>`; `<session> titles <folder>…` (1 to 3) | § S2.2 | S3 builds every call with `operandsOf`. Call (iii) and a press with a picked id are the SAME form (`id`) |
| X6 | C2, scope | "`src/chatSource.ts` (EXPORT `SESSION_ID`, `:68`)" | Not needed. `isSessionId` is exported already (`src/chatSource.ts:79`). `chatSource.ts` leaves S2's scope | Reuse first | — |
| X7 | C3, the runner | `runRemote(host, session, operands, script, limits?)` | One more trailing parameter: `seams?: RemoteSeams` (the capture, the file check, the log, the platform) | § 5's own tests need a fake capture (S2-T6, S2-T7) | S3 does not pass it |
| X8 | C3, the capture | options object `{ cwd, maxStdoutBytes }` | `{ cwd, maxStdoutBytes, killTree }`; `Captured` gains `stderrTail` (only when the object is given); `CaptureEnd` gains `'capped'`. `LaunchOptions` gains `killTree`. `src/processLauncher.ts` joins S2's scope (also `system32()`) | `security.md`: a timeout kills the whole tree. Today a child with no shell gets `child.kill()` only (`processLauncher.ts:412-415`), and ssh with `ProxyJump` has a child | — |
| X9 | C4 (S3 owns), from the judgement | — | TWO requests to S3, § S2.6 J8 and J9: (a) one more seam, the re-check just before each spawn; (b) for a `found` with `via: 'id'`, S3 checks `found.folder` against the folders it expects — so the picked id S3 keeps carries those folders with it | The window between the press and the read; a file of the right id in a folder nobody asked for | S3 `remoteRoad`, `nextStep`, the picked-id map |
| X10 | S4, S2-T10 | "The doors' catches are scanned by S2-T10" | S2 writes the scanner and proves it on samples. S4 adds the companion: the scan FINDS the two terminal registrations and the Asked hook | A scan that matches nothing passes for ever (`testing.md`) | S4 |

The parent's step 1 is also made exact (no contract moves): the stories document's example `tmux list-panes -s -t "=$1"`
is NOT exact. Measured (§ S2.0, M-T1): without the colon, a name that is the START of another session's name reads that
other session (`small` would read `small-tasks`). The script uses `-t "=$1:"`.

C3's "the parent's options, in the parent's order" stays true word for word: the parent's list now holds change 2.1
(below), so `SSH_OPTIONS` is that list.

## Plan change 2.1 — decided (the full text is § S2.7)

The judgement proposed two ssh options that were not in the parent's list. The caller decided, after question consultant
`a81ed863` (two independent causes): **`-o StrictHostKeyChecking=yes` IS taken** as change 2.1 (parent ledger entry 2.1),
after `-o LogLevel=ERROR` and before `--`. **`-o ControlPath=none` is NOT taken**: it would be a second change and a full
stop. Its residual — connection sharing on a Linux or macOS extension host, or in a Remote window — is named in words in
`SECURITY.md` by E2.S4 (§ S2.7 gives the words). Epoch 2 now has one change; any further ssh option is a full stop.

## S2.0 — What was read and what was measured

Read at `b51dd174`, worktree `/home/jinx/git/wt/coai-chat-terminal-e2`.

| # | Fact | Where |
|---|---|---|
| F1 | The one spawn site: `spawn(target, [...args], { shell, windowsHide: true, cwd?, env? })`. `shell` is false unless asked. There is NO `windowsVerbatimArguments`, so on win32 node quotes each word by the C runtime rules: a word with no space, tab or `"` goes as it is | `src/processLauncher.ts:141-160` (`:149-154`) |
| F2 | With no `cwd` and no shell the child gets the extension host's folder | `src/processLauncher.ts:125-131` `workingDirectory` |
| F3 | The kill: without a shell, or off win32, it is `child.kill()` only. `taskkill /t /f` runs only for a shell on win32. `taskkill` is an absolute path built from `SystemRoot`, then `WINDIR`, then `C:\Windows` | `src/processLauncher.ts:410-422` `killTree`; `:443-456`; `:465-469` `TASKKILL` |
| F4 | stdout and stderr are decoded as UTF-8 on the STREAM. The stderr tail keeps the last 8000 characters. A child that did not start puts the reason in the tail | `src/processLauncher.ts:315-316`; `:34`, `:110`, `:322-324`; `:331-338`, `:169-183` |
| F5 | `capture(target, args, shell, capMs, stop?, env?, input = '')`: seven positional parameters. It passes only `shell` and `env` to `launch`. It kills at the cap. It writes `input` and closes stdin. It has no stdout cap and no stderr in its result. `CaptureEnd` is `'timeout' \| 'not-started' \| 'stopped'` | `src/versionProbe.ts:92-168` (`:122`, `:146-153`, `:160`, `:162-166`); `:69`; `:72-76` |
| F6 | `capture` has 10 call sites in 7 files. None passes an eighth argument | `claudeProbeCache.ts:186`; `roundsDbRead.ts:330`, `:352`, `:373`; `apiModelsProbe.ts:28`; `versionProbe.ts:62`; `providersProbe.ts:46`; `panelProvider.ts:579`, `:4708`; `chatOrphans.ts:289` |
| F7 | `resolvedExecutable` joins each `PATH` entry with the name. A relative `PATH` entry gives a relative path, so it is not a guard against the current folder | `src/versionProbe.ts:196-209`, `:211-236` |
| F8 | The session-id shape is private and case-blind. `isSessionId` is exported | `src/chatSource.ts:68`, `:79` |
| F9 | `credsSession(name)`; the host class `[A-Za-z0-9][A-Za-z0-9._-]*`, the session class `[A-Za-z0-9][A-Za-z0-9_-]*` | `src/credsSession.ts:65`, `:31-32` |
| F10 | `projectDirNames(cwd)` gives up to three spellings. A name can hold any letter; the third spelling keeps `.`, `_` and spaces | `src/claudeSessions.ts:72-80` |
| F11 | `namedAmong`; `SCAN_BUDGET` = 250 files, 10 s, 64 MiB for one file | `src/claudeSessions.ts:333`, `:400` |
| F12 | `collectNames`, `titleFrom`, `customTitleFrom`: each parses the line as JSON. A line that is not JSON names nothing. `lastAsked`, `humanSaid` | `src/claudeQuestion.ts:110`, `:356`, `:379`; `:291`, `:327`; `SessionNames` `:78` |
| F13 | `isTerminalName`: a string of 1 to 500 units and one line | `src/chatStore.ts:108` |
| F14 | A test that runs a real `sh` exists: it returns when `run.error` is set ("no POSIX sh on this machine"). No test uses `sh -s` or `sh -c` yet | `src/test/install.test.ts:487-494` |
| F15 | `npm test` of the extension runs in CI on `ubuntu-latest` only (`sh` is `dash` there) | `.github/workflows/ci.yml:575` |
| F16 | Lint: `complexity` 4, 50 lines for a function, 800 lines for a file. `versionProbe.ts` holds suppressions today (complexity 3, function length 1) | `eslint.config.mjs:113-115`; `eslint-suppressions.json:930` |
| F17 | A new temp-API line in a test (`mkdtemp`, `tmpdir()`) is a `raw-temp` finding; the baseline only shrinks. The run folder is `COAI_TEST_RUN_FOLDER` | `raw-temp-baseline.json`; `scripts/runFolderGuard.cjs:21` |
| F18 | `oneAtATime` (S3 uses it; S2 does not) | `src/oneAtATime.ts:32` |

**Measured 2026-11-11 on this machine** (WSL2, Ubuntu 26.04; `dash` 0.5.12, `bash` 5.3, procps-ng 4.0.4, tmux 3.6,
OpenSSH 10.2p1; Windows `C:\Windows\System32\OpenSSH\ssh.exe` = `OpenSSH_for_Windows_9.5p2`; Claude Code 2.1.322). No sshd
is on this machine, so NO measurement here used a real connection.

| # | What was done | Result |
|---|---|---|
| M-T1 | `tmux list-panes -s -t "=<first 15 of 18 characters of a live session name>"` | exit 0, and it listed the panes of the LONGER session. The `=` does not hold without the colon |
| M-T2 | the same with `-t "=<prefix>:"`; with `-t "=<full name>:"` | exit 1 (`can't find …`); exit 0 with the right session |
| M-T3 | `tmux display-message -p -t "=<prefix>:" '#{session_name}'` (the parent's step 1 form) | exit 0 with NO output: a missing session looks like success |
| M-P1 | `ps -o pid=,ppid=,comm=` up from this session's shell | the Claude CLI's `comm` is `claude`; its parent is the pane's `bash`; its children are an MCP server and shells |
| M-R1 | the keys of `~/.claude/sessions/<pid>.json`; the file's size | `pid`, `sessionId`, `cwd`, `startedAt`, `procStart`, `name`, `nameSource`, `status`, `updatedAt` and others; 647 bytes |
| M-R2 | `procStart` against field 22 of `/proc/<pid>/stat` | equal (`41288` and `41288`): `procStart` is the process start time in clock ticks |
| M-G1 | `ssh -F <a config> -G vmx`, where the config's `Host vmx` holds `RemoteCommand`, `RequestTTY force`, a `LocalForward`, a `RemoteForward`, a `DynamicForward`, `ForwardAgent yes`, `ForwardX11 yes`, `ControlMaster auto`, a `ControlPath`, `ControlPersist 10m`, `StrictHostKeyChecking accept-new`, `BatchMode no`, `LogLevel DEBUG` | all of them are in the printed configuration |
| M-G2 | the same with the parent's options, `--` before the host and `sh -s -- 'x'` after it (OpenSSH 10.2p1) | `batchmode yes`, `clearallforwardings yes`, NO forward line, `forwardagent no`, `forwardx11 no`, `requesttty false`, NO `remotecommand` line, `loglevel ERROR`, `connecttimeout 5`. STILL there: `controlmaster auto`, the `controlpath`, `controlpersist 600`, `stricthostkeychecking accept-new` |
| M-G3 | M-G2 plus `-o StrictHostKeyChecking=yes -o ControlPath=none`, on OpenSSH 10.2p1 and on Windows 9.5p2 | `stricthostkeychecking true`; NO `controlpath` line. Windows 9.5p2 printed the same values as M-G2 for the parent's options |
| M-W1 | Windows `ssh.exe -F none -G -o ControlMaster=auto -o ControlPersist=60 -o ControlPath=C:/x/%C example.invalid` | the three options are PARSED and printed. This proves the parser only |
| M-S1 | the script of § S2.3 by `dash -s` and by `bash --posix -s`, a temporary `HOME`, a fake `tmux` and `ps`, `PATH` = one folder | the same output from both shells, byte for byte; 0.06 s |
| M-S2 | M-S1 while the fake tools read ALL of their stdin and the script's stdin stays open 3 s | `COAI-END` after 0.07 s. Every tool call saw `/dev/null` as stdin |
| M-S3 | M-S2 with ` </dev/null` removed from the call line | `COAI-END` after 3.07 s (the tools waited for the script's stdin). 4 of 4 tool calls saw a pipe. Under `bash` the tool ate the last line of the script and `COAI-END` was never printed; under `dash` it was (dash reads the script in blocks) |
| M-S4 | a 12-pane fixture; the reader closed stdout after 20 bytes | 2 `ps` calls of 24, then the script ended — with `SIGPIPE` at its default AND with `SIGPIPE` ignored, in `dash` and in `bash` |
| M-S5 | a 5 MiB transcript of multi-byte text | 4.2 MB of output; the first data line is a part of a line; every other line parses |
| M-S6 | the `HOME` tree (names, sizes, times, modes) before and after five runs of all three forms | identical |
| M-S7 | `grep` removed from the `PATH` (found by accident: the harness's link was broken) | before the tool check was added, the title call answered "no rows" and exit 0 — a failure that looked like an absence. With the check: `COAI-BAD tool` |
| M-Q1 | 25 hostile strings (`$(id)`, `` `id` ``, `;`, `\|`, `&`, `*`, `~`, a space, a tab, `"`, `'`, `\`, `--` …) through the quoting rule of § S2.2 and a real `sh -c "printf '%s\n' …"` in `dash` and `bash` | each printed exactly. With double quotes in place of the rule the command does not even parse |

**Read on the web 2026-11-11** (for § S2.6 J4): the Win32-OpenSSH wiki page *Project Scope*; issue
`PowerShell/Win32-OpenSSH#405`; pull request `PowerShell/openssh-portable#863`; `ssh_config(5)` on man.openbsd.org. They were read through a fetch tool that
answers questions about a page; the quoted words below are as that tool gave them, and the pages themselves are the source.

## S2.1 — Decisions

1. **The script reports facts; the parser decides.** `REMOTE_SCRIPT` prints marker lines and data lines. It matches
   nothing but shapes (digits, the UUID shape, the two alphabets). `readRemote` turns the facts into one `RemoteRead` arm.
   The script takes ONE decision of its own: which file it reads next (§ S2.3, rule 7).
2. **Every data line starts with `:`; every other line of the frame starts with `COAI-`.** So no byte of a transcript,
   a registry file, a folder name or a title row can end a section or look like a marker. This replaces "a header with
   the byte count" of § 5 area 4: stdout arrives decoded (F4), so a byte count cannot cut a string exactly, and a tail
   that starts inside a character changes the count.
3. **`COAI-END` is printed by the LAST line of the script, outside the function.** It proves three things at once: the
   whole script text arrived, the function returned, and no command ate the rest of the script (M-S3).
4. **The whole script is one function, `coai_main`, called with `</dev/null 2>/dev/null`.** Its helpers are defined
   INSIDE it. Nothing runs before the last two lines are read, so a script cut on the way does nothing. Every command
   inside gets `/dev/null` as stdin and as stderr from this ONE line (`security.md`: the measure is on the one road in).
5. **No `trap`.** Without a terminal the remote sshd sends no signal; it closes the pipes. The script ends by itself:
   every step is bounded, and the first `printf` of each step and of each loop pass is `… || return 0`, so a closed stdout
   stops the script at its next step, also when `SIGPIPE` is ignored (M-S4). A `trap` would do nothing here, and a measure that
   does nothing is not kept (`security.md`).
6. **The process walk accepts `comm` = `claude` exactly, and takes the top-most match of each branch** (M-P1). Not
   measured on the VMs (R7). A wrong guess gives the fallback (three calls where one would do), never another process.
7. **Every pane is listed. Several panes or several top-most `claude` are `ambiguous`, never a pick.** A session with
   several panes and NO `claude` is `ambiguous` with no candidates (the fallback uses a pane's folder, and choosing the
   pane would be a pick).
8. **A registry file that does not fit its process is never used.** The script prints field 22 of `/proc/<pid>/stat`;
   the parser compares it with the file's `procStart` (M-R2). Different → `unreadable`. One of the two is not there
   (an older Claude Code, a system with no `/proc`) → not checked, and that is said in § S2.8.
9. **A picked id is one operand form, `<session> id <uuid>`**, for the next press after a pick AND for the fallback's
   third call. The registry path wins only when it is exact (one pane, one `claude`, a registry file that is valid).
   Else the file of that id is read; no file → `gone`.
10. **Operands cross Windows unchanged because of their alphabets, and are single-quoted for the remote shell
    anyway.** The quoting function is total (it also quotes a `'`), and a real `sh -c` test holds it, so a loosened
    alphabet does not become an injection.
11. **The ssh executable is ONE absolute path for each platform, never the `PATH`, never the current folder**; the
    child's folder is the folder of that executable (§ S2.6 J1).
12. **`capture` is widened, not copied**: one options object as its eighth parameter. A call without it answers exactly
    what it answers today.
13. **The log line is built by one function with four parameters** (host, session label, end, stderr tail). No other
    code in the three modules names `console`.
14. **No hash test of the script.** The proof is behaviour (the `sh -s` tests). The spec gives the sha256 of the text
    so the coder and the reviewer can see that the constant IS the text below.
15. **The shell language is POSIX; the tools are those of a Linux VM.** `ps --ppid` (procps), `head -c`, `stat -c %Y`,
    `date +%s` and `/proc/<pid>/stat` are not POSIX. On a system without one of them the answer is the fallback (no
    children found), `unreadable` (`registry-bad`, `tool`) or a time of `-` — never another session (§ S2.8, residual 8).

## S2.2 — The operands (C2a) and their quoting (`src/remoteScript.ts`)

```ts
/** What one ssh call asks. The ONE source of the operands, and what `readRemote` reads the answer against. */
export type RemoteCall =
  | { readonly kind: 'session'; readonly session: string }
  | { readonly kind: 'id'; readonly session: string; readonly id: string }
  | { readonly kind: 'titles'; readonly session: string; readonly folders: readonly string[] };

export function operandsOf(call: RemoteCall): readonly string[];            // throws when callOf(result) is undefined
export function callOf(operands: readonly string[]): RemoteCall | undefined; // THE grammar; sshArgv asks it
export function shQuoted(word: string): string;                             // for the REMOTE login shell
```

| Form | Operands | Used for |
|---|---|---|
| `session` | `<session>` | the registry path; when it finds no live `claude` or no registry file, the fallback's call (i) |
| `id` | `<session>` `id` `<uuid>` | a press with a picked id (§ 5 area 9); the fallback's call (iii) |
| `titles` | `<session>` `titles` `<folder>` … (1 to 3 folders, all different) | the fallback's call (ii) |

| Operand | Rule (checked in `callOf`, AND again in `sh`) | Why |
|---|---|---|
| `<session>` | `credsSession('Creds: h / ' + session).kind === 'session'` — C1's rule, called, not typed again | one rule for a tmux session name |
| `id`, `titles` | the two literal words | — |
| `<uuid>` | `isSessionId(id)` (`chatSource.ts:79`) | one rule for a session id |
| `<folder>` | `/^[A-Za-z0-9_-][A-Za-z0-9._-]{0,254}$/` | it becomes a path part: no `/`, and the first character keeps out `.` and `..`. A real name starts with `-` (`/home/x` → `-home-x`), so `-` is legal first |

- **No alphabet holds a space, a tab, a quote, a control character or a shell character.** So on win32 node passes
  each word to `ssh.exe` as it is (F1), and `ssh` joins the words with spaces.
- **`shQuoted(word)`** = `'` + the word with each `'` written as `'\''` + `'`. A NUL, a CR or an LF THROWS. The result
  holds no `"`, no space and no tab for a legal operand. Every word after `sh -s --` goes through it, the two literal
  words also.
- **A folder name with a letter outside ASCII cannot be an operand.** `projectDirNames` can give one (F10). Such a
  folder is found on the registry path (the script sends its name back as data); only the title fallback cannot ask for
  it. The script counts the folders it left out, so S3 can say so (§ S2.8, residual 4).

## S2.3 — `REMOTE_SCRIPT`, exact (`src/remoteScript.ts`)

`export const REMOTE_SCRIPT: string` is the text below, byte for byte: 293 lines, 7497 bytes, each line ends with LF,
sha256 `1c3b25f9325f79cab47c84513fd126cac79f89105b016163bd75233686ecad07`. This text was run for M-S1…M-S7. The coder may
write the constant in any form (the text holds `${`, `\` and `'`, so a plain template literal needs care); the brief's
step 3 checks the hash. Beside it, exported for the tests and the parser: `REMOTE_PROTOCOL = 1`,
`TAIL_MAX = 4 * 1024 * 1024`, `REGISTRY_MAX = 64 * 1024`, `REMOTE_TOOLS` (the eleven names of rule 12).

```sh
coai_main() {
  LC_ALL=C
  export LC_ALL
  unset IFS
  set -f
  nl='
'
  h='[0-9A-Fa-f]'
  h4="$h$h$h$h"
  uuid="$h4$h4-$h4-$h4-$h4-$h4$h4$h4"

  coai_data() {
    sed 's/^/:/'
  }

  coai_walk() {
    seen=$((seen + 1))
    if [ "$seen" -gt 256 ] || [ "$2" -gt 16 ]; then
      printf 'over\n'
      return 0
    fi
    if [ "$(ps -o comm= -p "$1")" = claude ]; then
      printf '%s\n' "$1"
      return 0
    fi
    for kid in $(ps -o pid= --ppid "$1"); do
      case $kid in
        ''|*[!0-9]*) continue ;;
      esac
      coai_walk "$kid" $(($2 + 1))
    done
    return 0
  }

  coai_claude() {
    start=$(sed 's/^.*) //' <"/proc/$2/stat" | cut -d' ' -f20)
    case $start in
      ''|*[!0-9]*) start=- ;;
    esac
    printf 'COAI-CLAUDE %s %s %s\n' "$1" "$2" "$start" || return 0
    reg="$HOME/.claude/sessions/$2.json"
    regid=''
    if [ ! -f "$reg" ]; then
      regstate=none
    elif [ ! -r "$reg" ]; then
      regstate=denied
    else
      regstate=ok
    fi
    printf 'COAI-REGISTRY %s %s\n' "$2" "$regstate"
    [ "$regstate" = ok ] || return 0
    regtext=$(head -c 65537 <"$reg")
    printf '%s\n' "$regtext" | coai_data
    regid=$(printf '%s\n' "$regtext" | sed -n 's/.*"sessionId"[[:space:]]*:[[:space:]]*"\([0-9A-Fa-f-]*\)".*/\1/p')
    case $regid in
      $uuid) ;;
      *) regid='' ;;
    esac
    return 0
  }

  coai_transcript() {
    set +f
    set -- "$1" "$HOME"/.claude/projects/*/"$1.jsonl"
    set -f
    want=$1
    shift
    found=0
    file=''
    for one do
      [ -f "$one" ] || continue
      found=$((found + 1))
      file=$one
    done
    printf 'COAI-TRANSCRIPT %s %s\n' "$want" "$found" || return 0
    [ "$found" -eq 1 ] || return 0
    dir=${file%/*}
    folder=${dir##*/}
    size=$(wc -c <"$file")
    [ -r "$file" ] || size=''
    case $folder in
      ''|*"$nl"*) size='' ;;
    esac
    case $size in
      ''|*[!0-9\ ]*) printf 'COAI-BAD transcript\n'; return 0 ;;
    esac
    printf 'COAI-FILE %s\n' "$((size + 0))"
    printf '%s\n' "$folder" | coai_data
    printf 'COAI-TAIL\n'
    { tail -c 4194304 <"$file"; printf '\n'; } | coai_data
    size=$(wc -c <"$file")
    case $size in
      ''|*[!0-9\ ]*) size=0 ;;
    esac
    printf 'COAI-FILE-END %s\n' "$((size + 0))"
    return 0
  }

  coai_fallback() {
    cwd=$(tmux display-message -p -t "$1" '#{pane_current_path}')
    case $cwd in
      ''|*"$nl"*) printf 'COAI-BAD cwd\n'; return 0 ;;
    esac
    printf 'COAI-CWD\n' || return 0
    printf '%s\n' "$cwd" | coai_data
    printf 'COAI-FOLDERS\n'
    skipped=0
    listed=0
    more=0
    set +f
    set -- "$HOME"/.claude/projects/*/
    set -f
    for one do
      [ -d "$one" ] || continue
      name=${one%/}
      name=${name##*/}
      case $name in
        ''|.*|*[!A-Za-z0-9._-]*) skipped=$((skipped + 1)); continue ;;
      esac
      if [ "$listed" -ge 2000 ]; then
        more=1
        break
      fi
      listed=$((listed + 1))
      printf ':%s\n' "$name" || return 0
    done
    printf 'COAI-FOLDERS-END %s %s\n' "$skipped" "$more"
    return 0
  }

  coai_titles() {
    shift 2
    for folder do
      case $folder in
        ''|.*|*[!A-Za-z0-9._-]*) printf 'COAI-BAD operands\n'; return 0 ;;
      esac
    done
    began=$(date +%s)
    took=0
    why=all
    for folder do
      dir="$HOME/.claude/projects/$folder"
      if [ ! -d "$dir" ]; then
        printf 'COAI-FOLDER %s none\n' "$folder"
        continue
      fi
      printf 'COAI-FOLDER %s ok\n' "$folder" || return 0
      for name in $(ls -t -- "$dir"); do
        case $name in
          $uuid.jsonl) ;;
          *) continue ;;
        esac
        file="$dir/$name"
        [ -f "$file" ] || continue
        if [ "$took" -ge 250 ]; then
          why=count
          break 2
        fi
        now=$(date +%s)
        if [ "$((now - began))" -ge 8 ]; then
          why=time
          break 2
        fi
        took=$((took + 1))
        size=$(wc -c <"$file")
        [ -r "$file" ] || size=''
        case $size in
          ''|*[!0-9\ ]*) size=-1 ;;
        esac
        mtime=$(stat -c %Y -- "$file")
        case $mtime in
          ''|*[!0-9]*) mtime=- ;;
        esac
        if [ "$((size + 0))" -lt 0 ] || [ "$((size + 0))" -gt 67108864 ]; then
          printf 'COAI-SESSION %s %s %s skipped\n' "${name%.jsonl}" "$mtime" "$((size + 0))" || return 0
          continue
        fi
        printf 'COAI-SESSION %s %s %s rows\n' "${name%.jsonl}" "$mtime" "$((size + 0))" || return 0
        grep -F -e '"custom-title"' -e '"ai-title"' <"$file" | awk '
          1024 < length($0) { toolong++; next }
          { last = $0; rows++ }
          !($0 in seen) { seen[$0] = 1; if (kept < 24) { kept++; print ":" $0 } else more = 1 }
          END { if (rows) print ":" last; printf "COAI-ROWS %d %d\n", toolong, more }'
      done
    done
    printf 'COAI-SCAN %s %s\n' "$took" "$why"
    return 0
  }

  coai_session() {
    session=$1
    mode=${2-}
    picked=${3-}
    for tool in tmux ps; do
      command -v "$tool" >/dev/null || { printf 'COAI-BAD tool\n'; return 0; }
    done
    panes=$(tmux list-panes -s -t "=$session:" -F '#{window_index}.#{pane_index} #{pane_id} #{pane_pid}') || {
      printf 'COAI-NO-SESSION\n'
      return 0
    }
    set -- $panes
    if [ "$#" -eq 0 ] || [ "$(($# % 3))" -ne 0 ]; then
      printf 'COAI-BAD panes\n'
      return 0
    fi
    if [ "$#" -gt 48 ]; then
      printf 'COAI-OVER panes\n'
      return 0
    fi
    npanes=$(($# / 3))
    nclaude=0
    over=0
    onlypane=''
    while [ "$#" -ge 3 ]; do
      case $1 in
        ''|*[!0-9.]*) printf 'COAI-BAD panes\n'; return 0 ;;
      esac
      case $2 in
        %*[!0-9]*|%) printf 'COAI-BAD panes\n'; return 0 ;;
        %*) ;;
        *) printf 'COAI-BAD panes\n'; return 0 ;;
      esac
      case $3 in
        ''|*[!0-9]*) printf 'COAI-BAD panes\n'; return 0 ;;
      esac
      printf 'COAI-PANE %s %s %s\n' "$1" "$2" "$3" || return 0
      onlypane=$2
      seen=0
      for one in $(coai_walk "$3" 0); do
        if [ "$one" = over ]; then
          over=1
          continue
        fi
        nclaude=$((nclaude + 1))
        coai_claude "$1" "$one"
      done
      shift 3
    done
    if [ "$over" -eq 1 ]; then
      printf 'COAI-OVER walk\n'
      return 0
    fi
    exact=0
    if [ "$npanes" -eq 1 ] && [ "$nclaude" -eq 1 ]; then
      case $regstate in
        ok) exact=1 ;;
        denied) return 0 ;;
      esac
    fi
    if [ "$exact" -eq 1 ]; then
      [ -n "$regid" ] && coai_transcript "$regid"
      return 0
    fi
    if [ "$mode" = id ]; then
      coai_transcript "$picked"
      return 0
    fi
    if [ "$npanes" -eq 1 ] && [ "$nclaude" -le 1 ]; then
      coai_fallback "$onlypane"
    fi
    return 0
  }

  printf 'COAI-BEGIN 1\n'
  case ${HOME-} in
    /*) ;;
    *) printf 'COAI-BAD home\n'; return 0 ;;
  esac
  for tool in sed cut head tail wc grep awk ls date; do
    command -v "$tool" >/dev/null || { printf 'COAI-BAD tool\n'; return 0; }
  done
  case ${1-} in
    [A-Za-z0-9]*) ;;
    *) printf 'COAI-BAD operands\n'; return 0 ;;
  esac
  case $1 in
    *[!A-Za-z0-9_-]*) printf 'COAI-BAD operands\n'; return 0 ;;
  esac
  case $#:${2-} in
    1:) coai_session "$1" ;;
    3:id)
      case $3 in
        $uuid) coai_session "$1" id "$3" ;;
        *) printf 'COAI-BAD operands\n' ;;
      esac
      ;;
    3:titles|4:titles|5:titles) coai_titles "$@" ;;
    *) printf 'COAI-BAD operands\n' ;;
  esac
  return 0
}
coai_main "$@" </dev/null 2>/dev/null
printf 'COAI-END\n'
```

**The rules the text keeps** (each has a test in § S2.9):

1. **One function, read whole, then called.** Line 1 is `coai_main() {`. The second-last line is
   `coai_main "$@" </dev/null 2>/dev/null`. The last line is `printf 'COAI-END\n'`. Every other line is inside the
   function.
2. **No command reads the script.** Every command inside has `/dev/null` as stdin, or a pipe, or `<"$file"`.
3. **A data file is opened by redirection** (`<"$reg"`, `<"$file"`, `<"/proc/$2/stat"`), so its name is never an
   argument a tool could read as an option. The two commands that take a path as an argument take it after `--`:
   `ls -t -- "$dir"` and `stat -c %Y -- "$file"`. Both paths start with `/` (rule 11).
4. **`set -f` for the whole function.** The two globs (`…/projects/*/"$1.jsonl"`, `…/projects/*/`) run between `set +f`
   and `set -f`, and every match is tested (`-f`, `-d`) before it is used. An empty glob stays a literal word, fails
   the test and is never given to a command.
5. **The process walk** (`coai_walk`): a pid whose `comm` is `claude` is printed and NOT entered. So a `claude` with a
   `node` child and a `claude` grandchild is one candidate. Bounds: 256 processes for a pane, depth 16; past a bound it
   prints `over`, and the answer is `unreadable`, never a part of a list.
6. **The panes**: `tmux list-panes -s -t "=$session:" -F '#{window_index}.#{pane_index} #{pane_id} #{pane_pid}'`. The
   colon is needed (M-T1). At most 16 panes. Each of the three words is checked before it is used.
7. **Which file is read next** (the script's one decision):
   - one pane, one `claude`, a registry file that could be read → the transcript of the id IN that file (`regid`,
     taken by `sed`, checked against the UUID shape; the parser checks it again against the JSON);
   - a registry file that exists and cannot be opened → nothing more (the parser says `unreadable`);
   - else, with `id <uuid>` → the transcript of that id;
   - else, one pane with at most one `claude` → the fallback listing (the pane's folder by its pane id, the folder
     names);
   - else → nothing more (the parser says `ambiguous`).
8. **A closed stdout ends the script**: the first `printf` of each step and of each loop pass is `… || return 0` (10
   places), so no loop goes on after the reader is gone.
9. **Nothing is matched in `sh`** but shapes. No title, name or folder is compared with anything.
10. **Read-only, and nothing left**: no here-document (`bash` can make a temporary file for one), no `&`, no `>` but
    `>/dev/null`, no `cd`, no `eval`, no backtick, no `trap`, no line that starts with `~`.
11. **`LC_ALL=C`**, and `HOME` must start with `/`; else `COAI-BAD home`.
12. **A tool that is not there is said, never read as "nothing found"** (M-S7): `sed cut head tail wc grep awk ls date`
    are checked first, `tmux ps` before the panes; else `COAI-BAD tool`. `stat` is not in the list: without it the
    file's time is `-`.
13. **The bounds**:

| What | Bound | In the script | The TypeScript constant it must equal (a test holds each) |
|---|---|---|---|
| the registry file | 64 KiB; one byte more is read so "too big" can be seen | `head -c 65537` | `REGISTRY_MAX + 1` |
| the transcript tail | 4 MiB | `tail -c 4194304` | `TAIL_MAX` |
| the process walk | 256 processes and depth 16 for a pane, so at most 512 `ps` calls a pane and 16 panes; a walk that takes longer than the 15 s kill stops at the next pane's `printf` (rule 8) | `-gt 256`; `-gt 16`; `-gt 48` words | — |
| the folder listing | 2000 names | `-ge 2000` | — |
| the title scan | 250 files; 8 s; a file over 64 MiB is `skipped` | `-ge 250`; `-ge 8`; `-gt 67108864` | `SCAN_BUDGET.most`; under `SCAN_BUDGET.withinMs`; `SCAN_BUDGET.mostBytes` |
| the title rows of one file | a row over 1024 bytes is left out and counted; the first 24 different rows, then the last row again | the `awk` program | — |

   The largest answers: a `found` is about 4.3 MiB; a `titles` is at most 250 × 25 × 1025 bytes = 6.4 MiB; a `fallback`
   is at most 0.5 MiB. All are under the 8 MiB cap. A transcript made of empty lines only could pass it; that is the
   capped refusal, not a wrong answer.

**Why the last title row is printed again.** `collectNames` (F12) reads the LAST row as the latest name. The script
keeps the first 24 different rows in first-seen order and then repeats the last row, so `latest` is right and `former`
holds the same SET of names. `namedAmong` reads `former` as a set.

## S2.4 — The frame, and `readRemote` (`src/remoteFrame.ts`, pure)

**The frame.** stdout is split at `\n`. The frame starts at the LAST line that is exactly `COAI-BEGIN 1` and ends at the
FIRST line after it that is exactly `COAI-END`. Lines before the start (a login banner, a `motd`, a banner that holds the
sentinel) and lines after the end are ignored. Inside, a line that starts with `COAI-` is a marker (its words are split
at single spaces); a line that starts with `:` is data of the marker before it (the `:` is taken off); any other line
makes the answer `unreadable`.

| Marker | Words | Data lines | From |
|---|---|---|---|
| `COAI-BAD` | `operands` \| `home` \| `tool` \| `panes` \| `cwd` \| `transcript` | — | any form |
| `COAI-NO-SESSION` | — | — | `session`, `id` |
| `COAI-OVER` | `panes` \| `walk` | — | `session`, `id` |
| `COAI-PANE` | `<window>.<pane>` `%<n>` `<pid>` | — | one for each pane |
| `COAI-CLAUDE` | `<window>.<pane>` `<pid>` `<start ticks \| ->` | — | one for each top-most `claude`, after its pane |
| `COAI-REGISTRY` | `<pid>` `ok` \| `none` \| `denied` | when `ok`: the file's text (at most 65537 bytes) | after each `COAI-CLAUDE` |
| `COAI-TRANSCRIPT` | `<uuid>` `<files of that id>` | — | rule 7 |
| `COAI-FILE` | `<bytes before the read>` | ONE line: the folder's name | only when the count is 1 |
| `COAI-TAIL` | — | the tail's lines | after `COAI-FILE` |
| `COAI-FILE-END` | `<bytes after the read>` | — | after `COAI-TAIL` |
| `COAI-CWD` | — | ONE line: the pane's folder | the fallback |
| `COAI-FOLDERS` | — | one folder name on each line | the fallback |
| `COAI-FOLDERS-END` | `<names left out>` `<0 \| 1: more than 2000>` | — | the fallback |
| `COAI-FOLDER` | `<folder>` `ok` \| `none` | — | `titles`, one for each operand |
| `COAI-SESSION` | `<uuid>` `<mtime \| ->` `<bytes>` `rows` \| `skipped` | when `rows`: the title rows | `titles`, newest first in its folder |
| `COAI-ROWS` | `<rows left out as too long>` `<0 \| 1: more than 24 different>` | — | after the rows of each `rows` session |
| `COAI-SCAN` | `<files read>` `all` \| `count` \| `time` | — | `titles`, last |

```ts
export interface RemoteCandidate {
  readonly pane: string;       // '<window>.<pane>', for the pick list
  readonly pid: number;        // the top-most claude process
  readonly sessionId: string;  // valid by isSessionId — a candidate with no valid registry file is not in the list
  readonly name: string;       // '' when the registry has none
  readonly cwd: string;
}
export interface TitledSession {
  readonly sessionId: string;
  readonly folder: string;
  readonly mtime: number;      // seconds; 0 when the VM has no `stat -c`
  readonly names: SessionNames; // by collectNames() over the rows — the one rule
  readonly skipped: boolean;   // over 64 MiB or not readable: its names were NOT read
}
export type Unreadable =
  | 'exit' | 'no-sentinel' | 'version' | 'cut' | 'frame'
  | 'operands' | 'home' | 'tool' | 'panes' | 'too-many' | 'cwd' | 'transcript'
  | 'registry-denied' | 'registry-big' | 'registry-bad' | 'registry-stale' | 'id-mismatch' | 'folder';

export type RemoteRead =
  | { readonly kind: 'found'; readonly via: 'registry' | 'id'; readonly sessionId: string; readonly cwd: string;
      readonly name: string; readonly folder: string; readonly lines: readonly string[]; readonly cut: boolean }
  | { readonly kind: 'fallback'; readonly paneCwd: string; readonly folders: readonly string[];
      readonly skipped: number; readonly more: boolean }
  | { readonly kind: 'titles'; readonly sessions: readonly TitledSession[];
      readonly scan: { readonly why: 'all' | 'count' | 'time'; readonly tooLong: number; readonly more: boolean } }
  | { readonly kind: 'gone'; readonly sessionId: string }
  | { readonly kind: 'absent'; readonly what: 'session' | 'transcript' }
  | { readonly kind: 'ambiguous'; readonly why: 'panes' | 'processes' | 'transcripts';
      readonly candidates: readonly RemoteCandidate[]; readonly unnamed: number }
  | { readonly kind: 'unreadable'; readonly why: Unreadable };

export function readRemote(run: { readonly code: number; readonly stdout: string }, call: RemoteCall): RemoteRead;
export const UNREADABLE_SAID: Readonly<Record<Unreadable, string>>;
```

There is no `truncated` arm. `found.cut` is a fact; what it means is `answerFrom(found, purpose)` in `remoteRead.ts` (S3).
`readRemote` never throws, and no arm holds an error's message (a V8 `JSON.parse` error quotes its input).

**The registry file of one candidate** is valid when ALL of these hold, checked in this order; the first that fails names
the fault:

| # | Check | Fault |
|---|---|---|
| 1 | its text is at most `REGISTRY_MAX` bytes (`Buffer.byteLength`) | `registry-big` |
| 2 | it parses as a JSON object; `sessionId` passes `isSessionId`; `pid`, when there, equals the candidate's pid; `cwd` starts with `/` and passes `isTerminalName` (one line, at most 500 units) | `registry-bad` |
| 3 | `procStart`, when it is a string of digits AND the marker's start is digits, equals it | `registry-stale` |

`name` is the file's `name` when it passes `isTerminalName`, else `''`.

**The decision, in this order.** `P` = the `COAI-PANE` lines, `C` = the `COAI-CLAUDE` lines. A candidate is *named* when
its registry is `ok` and valid; every other candidate counts in `unnamed`.

| # | Condition | `session` call | `id` call (picked id `X`) |
|---|---|---|---|
| 1 | `run.code !== 0` | `unreadable` `exit` | the same |
| 2 | no `COAI-BEGIN 1` line: another `COAI-BEGIN <n>` → `version`; none → `no-sentinel`. No `COAI-END` after it → `cut`. A line that is not a marker or data, an unknown marker, a marker with wrong words, a part that the rows below need and is not there → `frame` | `unreadable` | the same |
| 3 | a `COAI-BAD <what>` → that `<what>`; a `COAI-OVER` → `too-many` | `unreadable` | the same |
| 4 | `COAI-NO-SESSION` | `absent` `session` | the same |
| 5 | `P` = 1, `C` = 1, registry `denied` or not valid | `unreadable` with the fault | the same |
| 6 | `P` = 1, `C` = 1, registry valid: the `COAI-TRANSCRIPT` id must be the registry's `sessionId` (else `id-mismatch`). Files: 0 → `absent` `transcript`; 2 or more → `ambiguous` `transcripts`, no candidates; 1 → the folder must be one of `projectDirNames(cwd)` (else `unreadable` `folder`), then `found` `via: 'registry'` | as said | the same — `X` is not read (S3 drops it) |
| 7 | `P` = 1 and (`C` = 0, or `C` = 1 with registry `none`) | `fallback` (the `COAI-CWD` line must start with `/`; every folder name must pass the folder rule) | the `COAI-TRANSCRIPT` id must be `X`. Files: 0 → `gone`; 2 or more → `ambiguous` `transcripts`; 1 → `found` `via: 'id'`, `cwd` and `name` `''` |
| 8 | `P` ≥ 2 or `C` ≥ 2 | `ambiguous`, `why` = `panes` when `P` ≥ 2, else `processes`; the named candidates; `unnamed` | when `X` is a named candidate's id, or `unnamed` > 0, or there is no candidate: as row 7 (with that candidate's `cwd` and `name` when it is one). Else (every candidate is named and none is `X`) → `ambiguous` with the candidates: the pick is not one of the live sessions |

For a `found`: `lines` are the `COAI-TAIL` data lines with empty lines left out. `cut` = the larger of the two sizes
(`COAI-FILE`, `COAI-FILE-END`) is over `TAIL_MAX`; when `cut`, the FIRST line is dropped (it is a part of a line, and a
tail that starts inside a UTF-8 character is in that part). Both sizes are read so a file that grew past the limit
during the read is still `cut`.

A **`titles` call**: rows 1–3, then every `COAI-FOLDER` name must be one of `call.folders` (else `frame`). Each
`COAI-SESSION` is a `TitledSession` of the folder marker before it; its `names` come from `collectNames()` fed with its
rows in order. `scan` = the `COAI-SCAN` word, the sum of the too-long counts, and whether any session had more than 24
different rows.

`UNREADABLE_SAID` gives one short clause for each `Unreadable`, all different, for S3's one "unreadable" sentence. None
holds data from the answer:

| `why` | Clause |
|---|---|
| `exit` | the remote command ended with an error |
| `no-sentinel` | the answer did not start as expected |
| `version` | the answer is in a form this version does not know |
| `cut` | the answer stopped before its end |
| `frame` | the answer was not in the expected form |
| `operands` | the remote script refused what it was asked |
| `home` | the remote account has no home folder |
| `tool` | a tool the read needs (tmux, ps, sed, cut, head, tail, wc, grep, awk, ls or date) is not on the PATH of an ssh command there |
| `panes` | tmux answered in a form that was not expected |
| `too-many` | the tmux session has too many panes or processes to look through |
| `cwd` | tmux gave no folder for the pane |
| `transcript` | the session file could not be opened |
| `registry-denied` | the registry file of the session could not be opened |
| `registry-big` | the registry file of the session is larger than 64 KiB |
| `registry-bad` | the registry file of the session is not in the expected form |
| `registry-stale` | the registry file of the session belongs to an older process |
| `id-mismatch` | the session file that was read is not the one the registry names |
| `folder` | the session file is not in the folder that belongs to the session's working folder |

## S2.5 — The ssh edge (`src/remoteSsh.ts`, node only; `src/versionProbe.ts`; `src/processLauncher.ts`)

```ts
/** The parent's options, in the parent's order, with change 2.1 last. A change to this list is a plan change (§ S2.7). */
export const SSH_OPTIONS: readonly string[] = [
  '-T',
  '-o', 'BatchMode=yes', '-o', 'ConnectTimeout=5', '-o', 'RequestTTY=no', '-o', 'RemoteCommand=none',
  '-o', 'ClearAllForwardings=yes', '-o', 'ForwardAgent=no', '-o', 'ForwardX11=no', '-o', 'LogLevel=ERROR',
  '-o', 'StrictHostKeyChecking=yes',
];
export function sshArgv(host: string, operands: readonly string[]): readonly string[];
export function sshExecutable(platform?: Platform, env?: NodeJS.ProcessEnv): { readonly path: string; readonly cwd: string };

export interface RemoteLimits { readonly ms: number; readonly maxStdoutBytes: number }
export const REMOTE_LIMITS: RemoteLimits = { ms: 15_000, maxStdoutBytes: 8 * 1024 * 1024 };
export type RemoteRun =
  | { readonly kind: 'answered'; readonly code: number; readonly stdout: string }
  | { readonly kind: 'refused'; readonly refusal: string };
export interface RemoteSeams {
  readonly capture: typeof capture;
  readonly exists: (path: string) => Promise<boolean>;
  readonly log: (line: string) => void;       // the default is the ONE console call of the three modules
  readonly platform: Platform;
  readonly env: NodeJS.ProcessEnv;
}
export function runRemote(host: string, session: string, operands: readonly string[], script: string,
  limits?: RemoteLimits, seams?: RemoteSeams): Promise<RemoteRun>;
```

**`sshArgv(host, operands)`** = `[...SSH_OPTIONS, '--', host, 'sh', '-s', '--', ...operands.map(shQuoted)]`.

- It THROWS (an `Error` whose message is a fixed word, never the value) when
  `credsSession('Creds: ' + host + ' / s').kind !== 'session'` or `callOf(operands) === undefined`. This is the second
  check of the same rules, at the one road in; the door's check (S3) is the first.
- No `-n` (the remote `sh -s` would read an empty stdin). `--` stands right before the host: the host cannot be read as
  an option, and `ssh` then takes every word after the host as the command, so `-s` is not read as ssh's own `-s`.
- On win32 (G0): every word is one of the fixed words above, the host, or a quoted operand. None holds a space, a tab or
  `"`. The only quote characters are the single quotes of `shQuoted`.

**`sshExecutable(platform, env)`** — pure:

| Platform | `path` | `cwd` |
|---|---|---|
| `win32` | `path.win32.join(system32(env), 'OpenSSH', 'ssh.exe')` | the folder of `path` |
| `linux`, `darwin` | `/usr/bin/ssh` | `/usr/bin` |

`system32(env = process.env)` is NEW in `processLauncher.ts`, exported: `path.win32.join(env.SystemRoot ?? env.WINDIR ??
'C:\\Windows', 'System32')`. `TASKKILL` (`:465-469`) is rebuilt from it, so the rule is written once.

**`runRemote`** never rejects and never throws. In order:

1. `operands[0] !== session` or `sshArgv` throws → `refused`: *"This terminal’s name cannot be given to ssh safely, so
   nothing was read."* The log line for this case holds no value: `ConnectOtherAIs: remote read refused before ssh: a
   host, a session or an operand failed its rule`.
2. `!(await seams.exists(exe.path))` → `refused`: *"ssh was not found at “<path>”, so the Claude session on “<host>”
   cannot be read."*, and on win32 one more sentence: *"Install the Windows optional feature “OpenSSH Client”."* There
   is no second place to look.
3. `seams.capture(exe.path, argv, false, limits.ms, undefined, undefined, script, { cwd: exe.cwd, maxStdoutBytes:
   limits.maxStdoutBytes, killTree: true })`.
4. The result:

| `capture` answered | `RemoteRun` |
|---|---|
| `ended: 'timeout'` | `refused`: *"“<host>” did not answer over ssh within <limits.ms / 1000> s."* |
| `ended: 'capped'` | `refused`: *"“<host>” sent more than <maxStdoutBytes / 1048576> MiB, so the read was stopped."* |
| `ended: 'not-started'` | `refused`: *"ssh could not be started: <the last line of the stderr tail>"* |
| `code === 255` | `refused`: *"ssh could not read from “<host>”: <the last line of the stderr tail>"*; with an empty tail *"ssh could not read from “<host>” (exit 255)."* |
| any other `code` | `answered` with that `code` and the stdout. (`readRemote` makes a code that is not 0 `unreadable`.) |

   "The last line" = the last line that is not empty, control characters taken out, at most 300 characters, `…` after a
   cut. It is ssh's own sentence (`Permission denied (publickey).`, `Host key verification failed.`): the remote
   script's stderr goes to `/dev/null` (§ S2.3 rule 1), so no transcript text can be in it.
5. **The log**: ONE line for each call that did not answer with code 0, through `seams.log`, built by
   `logLine(host, session, end, stderrTail)`: `ConnectOtherAIs: remote read host=<host> session=<session> end=<exit N |
   timeout | capped | not-started | no-ssh> stderr=<the last 2000 characters of the tail, each line break as " | ">`.
   Never stdout, never an operand, never a caught error.

**`capture`** (`versionProbe.ts:92`) gets an EIGHTH parameter, `options?: CaptureOptions`, and no other change of its
signature:

```ts
export type CaptureEnd = 'timeout' | 'not-started' | 'stopped' | 'capped';
export interface CaptureOptions { readonly cwd?: string; readonly maxStdoutBytes?: number; readonly killTree?: boolean }
export interface Captured { readonly code: number; readonly output: string; readonly ended?: CaptureEnd;
  readonly stderrTail?: string }
```

- `launch` gets `cwd` and `killTree` when they are given; `shell` stays the caller's third argument.
- Each stdout chunk adds `Buffer.byteLength(chunk)` to a count. Past `maxStdoutBytes`: `child.kill()`, then the answer
  `{ code: -1, output: '', ended: 'capped', … }`. The stdout read so far is NOT in the answer.
- `stderrTail: child.stderrTail()` is in the answer only when `options` was given. So every one of the 10 call sites of
  today (F6) gets exactly the object it gets today.
- `capture` already holds lint suppressions (F16). The count must not grow: put the new branches in small functions.

**`launch`** (`processLauncher.ts:141`): `LaunchOptions` gains `readonly killTree?: boolean`; the handle is made with
`liveHandle(child, shell || options.killTree === true)`. So on win32 the kill of an ssh child is `taskkill /pid <pid> /t
/f` and then `child.kill()` (F3) — ssh and what it started (a `ProxyJump` ssh, a `ProxyCommand`). Off win32 nothing
changes: `child.kill()` sends `SIGTERM` to ssh.

## S2.6 — The security judgement on the ssh edge (gate G2 + G4)

Decided before any S2 code. Each item says the decision, the evidence, and what was NOT observed. By `security.md`, an
ssh option is a request until its effect was seen: the words below are "we pass", not "it protects", where no effect was
seen.

**J1 — Which `ssh` runs, from which folder.** *(question a)*

- **Decision.** One absolute path for each platform (§ S2.5): on Windows `<SystemRoot>\System32\OpenSSH\ssh.exe`; else
  `/usr/bin/ssh`. `SystemRoot`, then `WINDIR`, then `C:\Windows` — the rule `TASKKILL` has. The file is checked to be
  there before the spawn. Not there → a refusal that names the path. NO search of the `PATH`, NO setting, NO second
  place. The child's working folder is the folder of the executable.
- **Why not the `PATH`.** With `shell: false`, the spawn layer looks for a bare name in the current folder first and
  then on the `PATH` (the stories document's gate text; the same reason `TASKKILL` is an absolute path, F3). With no
  `cwd` the current folder is the extension host's (F2), which can be the opened workspace. `resolvedExecutable` does not close this: a relative `PATH` entry gives a relative path (F7). A user folder
  on the `PATH` is also a place where another program can put an `ssh.exe`.
- **Why that working folder.** Only an administrator can write there. `ssh` itself can start a program by a bare name
  (the `ProxyJump` ssh, a `ProxyCommand`), and a relative `IdentityFile` is read from the working folder. With this
  folder, a bare `ssh` found in the working folder is the real one.
- **The cost.** A person whose terminal uses another ssh (Git for Windows, a newer OpenSSH in `Program Files`) gets the
  system one here. Both read the same `~/.ssh/config` and `known_hosts`. They can use different key agents: then the
  call ends with exit 255 and ssh's own line — a refusal that says why, never a wrong answer. A machine with no
  `/usr/bin/ssh` (NixOS) gets the "not found" refusal.
- **Evidence.** F1, F2, F3, F7. `C:\Windows\System32\OpenSSH\ssh.exe` is there on this machine (9.5p2).
- **Not observed.** How `ssh.exe` starts its `ProxyJump` child (by its own full path or by a bare name). The working
  folder makes both safe.

**J2 — Which host configuration is accepted.** *(question b)*

- **Decision.** The host is the alias in the LIVE terminal name, valid by C1's host class. The person's own
  `~/.ssh/config` is the authority for that alias: `HostName`, `User`, `Port`, `IdentityFile`, `ProxyJump`,
  `ProxyCommand`, `Match`, `Include`. Our options stand first on the command line, and for each setting ssh keeps the
  first value it gets, so they win over the config (M-G2).
- **What the alphabet closes.** A host that starts with `-` (an option), and a host with a shell character that a
  `ProxyCommand … %h` would hand to a local shell. OpenSSH closed that second hole itself in 9.6; the Windows client
  here is 9.5p2, so the class is what closes it on this machine.
- **What `BatchMode=yes` stops (our call).** A password prompt, a keyboard-interactive prompt and a key passphrase
  prompt: the method is skipped, and the call ends with exit 255 (*"Permission denied (…)."*). The question "do you
  trust this host key?" is answered NO, so with the default `StrictHostKeyChecking ask` an unknown key is a refusal.
- **What `BatchMode=yes` does NOT stop.** (1) The person's own `StrictHostKeyChecking accept-new` or `no`: then an
  unknown key is ACCEPTED and written to `known_hosts`, by a call that a terminal's NAME started (M-G2 shows the
  person's value stays). (2) With `no`, a CHANGED key is accepted also. **Change 2.1 closes both**: we pass
  `-o StrictHostKeyChecking=yes`, which stands before the config's value (M-G3 shows `stricthostkeychecking true` over
  `accept-new`); the refusal itself against a real server is M-O3, not observed yet.
- **What our options do NOT reach: the proxy leg.** A `ProxyJump` or a `ProxyCommand` is a second program that ssh
  starts with the person's configuration. It does not get our `-o` options. `ssh -W` (what `ProxyJump` runs) clears
  forwardings and opens no session by its own rule (`ssh(1)`, `-W`). It has no `BatchMode`, so it can try to ask. It
  has no terminal here; the 15 s kill ends it, and on Windows the tree kill (X8) takes it with ssh.
- **Other things the person's config can still do**, all as in the person's own terminal: `LocalCommand` (only with
  `PermitLocalCommand yes`), `SendEnv`/`SetEnv`, `Match exec`, `UpdateHostKeys` (ssh can add more keys of a host it
  already trusts to `known_hosts`). `StdinNull`, `ForkAfterAuthentication` or `SessionType none` in a `Host` block
  break the read in the safe direction: no sentinel (`unreadable`) or the 15 s refusal.
- **Evidence.** M-G1, M-G2; `ssh_config(5)`: `BatchMode` turns off password prompts and host key questions;
  `StrictHostKeyChecking` `yes` never adds a key and refuses a changed one, `accept-new` adds new keys; `ssh(1)`: `-W`
  implies `-N`, `-T` and `ClearAllForwardings` unless the configuration says other.
- **Not observed.** Any of this against a real server (no sshd here). R6 is the first real call.

**J3 — Whose keys.** The person's keys and agent, through their configuration. The extension handles no secret: nothing
is added to the child's environment, no key path is in the argv, and the log holds the host, the session name, the end
and ssh's stderr tail. At `LogLevel=ERROR` that tail holds refusals and can name a key FILE (*"Load key …"*), never key
material.

**J4 — Multiplexing.** *(question c)*

- **Does Win32-OpenSSH support it?** No, not in a released build — sure of this to a high degree:
  - the Win32-OpenSSH wiki, *Project Scope* (read 2026-11-11): *"following features have been scoped out and will not
    work on Windows yet"*, and the list holds *"Client ControlMaster"* and *"Background ssh execution mode"*;
  - issue `PowerShell/Win32-OpenSSH#405` (*"ControlPath in ssh_config fails"*): open since 2016-11-13, on the backlog; a
    contributor, 2016-11-16: *"ControlMaster is not supported on Windows yet."*; a user, 2023-12-11: *"Setting
    ControlPath makes OpenSSH fail, as it is not supported on Windows"*;
  - pull request `PowerShell/openssh-portable#863` (*"Add client-side ControlMaster (connection multiplexing)
    support"*), opened 2026-07-14, still open on 2026-11-11: what a pull request still adds is not in a release.
  - M-W1 shows only that 9.5p2 PARSES the three options.
  **Less sure** (not checked: the source of 9.5p2, a run against a server): what `ssh.exe` DOES with a `ControlPath` in
  the config — fail or go on without sharing. Both are safe for us: a failure is exit 255 with its line.
  So on the owner's side today our call cannot become a master and cannot share the terminal's connection. That
  changes without any change of ours on the day #863 ships in Windows.
- **On a Linux or macOS extension host** (the parent says "not measured, not built for", but nothing stops the
  extension there): yes to both, when the person's config says `ControlMaster auto` (M-G2 shows the three settings
  stay):
  - a master is there (the terminal's ssh, or one kept by `ControlPersist`) → our call goes through IT. This does not
    break the consent (the same alias, the same person, a connection the person opened). But `BatchMode` and
    `ConnectTimeout` then say nothing, and the call runs on a master that was started with other options;
  - no master is there, and `ControlPersist` is set → OUR call becomes the master, goes to the background after it
    logged in, and stays for the `ControlPersist` time (for ever with `yes`), with an open connection to the VM. The
    15 s kill ends only the foreground half. This is *"a multiplexing master … that outlives the read"*, which § 1 of
    the stories document says must not ship.
- **Proposed:** `-o ControlPath=none` (no sharing: never a master, never a client of one). `ControlMaster=no` alone is
  not enough: it stops the master and still lets the call go through a master that is there.
- **Decided (the caller, 2026-11-11, parent ledger entry 2.1): NOT taken.** It is a cause independent of the host-key
  option, so it would be a second change of epoch 2 and a full stop. Connection sharing stays the person's own
  configuration. On the owner's extension host (Windows, Win32-OpenSSH) there is no released multiplexing, so there the
  call cannot become a master or share one. On a Linux or macOS extension host, or in a Remote window (the extension then
  runs on the remote machine and uses its `ssh`), the two cases above can happen. That is a named residual (§ S2.8,
  residual 9), and `SECURITY.md` says it in words (§ S2.7). The stories document's § 1 item *"something left running"*
  then holds for a multiplexing master on Windows only.
- **Evidence.** M-G2 (the three settings stay under the parent's options); M-G3 (the option would remove the
  `controlpath` line on both builds); `ssh_config(5)`: `ControlPath` — *"none"* turns connection sharing off.
- **Not observed.** A master that stays after a read on Linux (M-O2 in § S2.8 measures the residual, so its words can
  be checked).

**J5 — Forwarding, and the parent's option list.** *(question e)*

Nothing in the parent's list is wrong. M-G2 saw each one take effect on the configuration ssh would use, against a
config that sets the opposite:

| Option | What we ask | Seen (M-G2) | Note |
|---|---|---|---|
| `-T`, `RequestTTY=no` | no terminal | `requesttty false` over the config's `force` | the two say the same thing; both stay, the parent's list is the contract. With no terminal the `~` escapes are off, and the script has no line that starts with `~` anyway |
| `BatchMode=yes` | no question | `batchmode yes` over `no` | J2 |
| `ConnectTimeout=5` | a bound on the connect | `connecttimeout 5` | the 15 s kill is the bound on the whole call |
| `RemoteCommand=none` | the config's `RemoteCommand` does not run | no `remotecommand` line | without it ssh refuses a command line beside a `RemoteCommand` |
| `ClearAllForwardings=yes` | no `LocalForward`, `RemoteForward`, `DynamicForward` | `clearallforwardings yes` and no forward line | the owner's terminal already holds its `LocalForward` |
| `ForwardAgent=no`, `ForwardX11=no` | no agent, no X11 | both `no` over `yes` | — |
| `LogLevel=ERROR` | refusals only | `loglevel ERROR` over `DEBUG` | refusals are errors and stay; the server's banner is not printed |
| `--` before the host | the host is never an option; the words after it are the command | accepted by 10.2p1 and by Windows 9.5p2 | — |
| no `-n` | the script must reach `sh -s` | — | S2-T1 |
| `StrictHostKeyChecking=yes` (change 2.1) | no host key is accepted by this call | `stricthostkeychecking true` over `accept-new` (M-G3) | J2; the refusal itself is M-O3 |

Two settings of the person's config were NOT pinned by the parent's first list: `StrictHostKeyChecking` (J2) and
`ControlPath` (J4). Change 2.1 pins the first; the second stays the person's (residual 9). That is § S2.7. `Tunnel` is
not pinned either: it needs root on both sides and is not a forwarding the list names; it is a residual (§ S2.8).

**J6 — Nothing left behind on the VM.** *(question d)*

- **Decision.** The script's own shape, not a signal: (1) nothing runs until the whole text is read (decision 4);
  (2) no command can wait for input — stdin is `/dev/null` from the one call line (M-S2); (3) no background job, no
  here-document, no write, no temporary file (rule 10; M-S6); (4) every step is bounded (rule 13), the title scan by
  8 s; (5) when the reader is gone, the script stops at its next step (rule 8; M-S4). No `trap` (decision 5).
- **What happens when the local ssh is killed.** With no terminal, the remote sshd closes the pipes and sends no
  signal. A command that is reading a file runs to its end or to its next write, and then the script ends as in (5).
  So what can stay is one read of at most 64 MiB for a moment, never a process that waits.
- **One thing this does not bound:** a tmux server that does not answer. The `tmux` call then waits, and stays after
  the kill. The person's terminal is stuck too in that case. `timeout` is not POSIX; it is not used. Residual (§ S2.8).
- **Not observed.** A real sshd (M-S2…M-S4 used pipes). § 8 of the stories document already records with R6 "none of
  the script's commands on the VM" after a killed read.

**J7 — Nothing left behind on this machine.** One child for each call; the kill at 15 s or at 8 MiB takes the tree on
win32 (X8); the stdout is in memory only and is dropped after the parse; nothing is written by the extension. ssh
itself can write `known_hosts` only through `UpdateHostKeys`, for a host it already trusts (J2; change 2.1 stops a new
host). On Windows no master can stay (J4: no multiplexing, no background mode). On a Linux or macOS extension host, or in
a Remote window, a person's `ControlPersist` can keep one (J4, residual 9).

**J8 — The window between the press and the read (a request to S3).** The consent question can stay open for minutes.
So just before EACH spawn, S3's road asks a seam whether the terminal is still open and its name still gives the same
host and session; if not → *"The terminal’s name changed, or it was closed, so nothing was read. Press again."*, no
spawn. `runRemote` cannot do this (it has no `vscode`).

**J9 — What comes back is input from another machine.** The stdout is parsed by the strict frame (§ S2.4) and is never
run, never used as a local path, never logged, and never part of an error's text. Text from it reaches the person only
as a chat passage that is not sent by itself, as *Asked* rows, and as pick-list rows (`name` and `cwd` are one line of at
most 500 units by the parser). A VM that lies can put false text in the chat box; it cannot do more. **Request to S3:**
for a `found` with `via: 'id'`, check `found.folder` — after a title pick it must be one of the folders of call (ii);
after a pick from an ambiguity list it must be one of `projectDirNames(candidate.cwd)` — and refuse otherwise.

## S2.7 — Plan change 2.1, as decided

**The proposal** (this judgement): two more options after `-o LogLevel=ERROR` and before `--`,
`-o StrictHostKeyChecking=yes -o ControlPath=none`.

**The decision** (the caller, 2026-11-11, after question consultant `a81ed863`, codex and antigravity: *two independent
causes*). The parent's ledger entry 2.1 holds it:

```
- 2.1 · 2026-11-11 · agent · contract — the automatic ssh call pins StrictHostKeyChecking=yes, so a person's accept-new or no cannot make it accept a host key
```

| Option | The failure it prevents | Evidence | Effect seen? | Decided |
|---|---|---|---|---|
| `-o StrictHostKeyChecking=yes` | A call that a terminal's NAME started accepts a host key nobody looked at, writes it to `known_hosts`, and then logs in there (the host learns the person's user name and public keys) and reads its answer as the session. It happens when the person's config says `accept-new` or `no` — a usual line under `Host *`. With `no`, a CHANGED key is accepted too. The consent question is then the only guard, and it shows a name | J2; M-G2 (the person's `accept-new` stays under the parent's first options); M-G3 (`stricthostkeychecking true` on 10.2p1 and on Windows 9.5p2); `ssh_config(5)`: with `yes` ssh never adds a key and refuses a host whose key changed | at the configuration level only (M-G3); the refusal is M-O3 | **TAKEN** — change 2.1, in `SSH_OPTIONS` (§ S2.5) |
| `-o ControlPath=none` | On Linux or macOS, or in a Remote window (and on Windows when multiplexing ships there), with `ControlMaster auto` and `ControlPersist` in the person's config, our call becomes a background master that outlives the read, or runs through the terminal's own connection | J4; M-G2 (the three settings stay); M-G3 (no `controlpath` line) | at the configuration level only (M-G3) | **NOT taken** — an independent cause: a second change of epoch 2, so a full stop. Residual 9 (§ S2.8) |

**What `StrictHostKeyChecking=yes` costs.** Nothing for a host the person's own ssh already trusts: a `Creds:` terminal
is open, so its ssh wrote the key (with `ask`, `accept-new` and `no` alike). A host whose keys are never kept
(`UserKnownHostsFile` set to nothing) cannot be read: the refusal ends with ssh's line *"No … host key is known for … and
you have requested strict checking."* (exit 255, the `code === 255` row of § S2.5). R6 shows it on the owner's hosts.

**What this spec does with the decision.**

- `SSH_OPTIONS` (§ S2.5) holds the two words `-o`, `StrictHostKeyChecking=yes` after `-o`, `LogLevel=ERROR`. It holds no
  `ControlPath`, `ControlMaster` or `ControlPersist` word. S2-T1 pins the list, S2-T2 and S2-T21 hold the option, B34 is
  its break-it.
- M-O3 is owed before any document says more than "we pass `StrictHostKeyChecking=yes`".
- **The words for `SECURITY.md`** (E2.S4 writes them in its paragraph on the ssh edge; "we pass" words, as `security.md`
  asks for a delegated option):
  - *"The read passes `-o StrictHostKeyChecking=yes`, before your own configuration. With it, ssh refuses a host whose key
    is not already in your `known_hosts`, and a host whose key changed, whatever your configuration says
    (`accept-new` or `no` included); the read then ends with ssh's own sentence. A host your terminal's ssh already
    trusts is read as usual."*
  - *"The read does not turn off ssh connection sharing. On Windows, the OpenSSH client that comes with Windows has no
    released connection sharing, so a read cannot share a connection or leave one open. When the extension runs on Linux
    or macOS — or on the remote machine of a Remote window — and your ssh configuration shares connections
    (`ControlMaster`, `ControlPersist`), the read can go through a connection that is already open, or open one that
    stays open for the `ControlPersist` time, as your configuration says."*
- The stories document's § 1 item *"something left running"* holds for a multiplexing master on the owner's Windows
  extension host only; elsewhere it is residual 9, said in the words above. Any further ssh option is a full stop, never
  a coder's or a reviewer's edit.

## S2.8 — What S2 does not close

**Not observed — owed, with who can observe it.** Until then the documents say "we pass", not "it protects".

| # | What | Who, where |
|---|---|---|
| M-O1 | A real call from Windows: the argv reaches `ssh.exe` unchanged, the script arrives, the frame comes back with `\n` line ends | the owner, R6 |
| M-O2 | Residual 9, its size: with `ControlMaster auto` and `ControlPersist 60` in a config, a call with our argv leaves a background `ssh` master on Linux (the words of § S2.7 say "can"; this checks them). Not a merge condition | anyone with a Linux machine that runs sshd (the VM itself can call `localhost`) |
| M-O3 | `StrictHostKeyChecking=yes` (change 2.1) with `BatchMode=yes`: a host that is not in `known_hosts` gives exit 255 and nothing is written, also when the config says `accept-new` | the same; or the owner by hand with the argv of S2-T1 to an alias whose key is not kept (R6) |
| M-O4 | After a read killed at the limit: no `ssh` process on Windows, also with `ProxyJump`; none of the script's commands on the VM | the owner, R6 (§ 8 of the stories document) |
| M-O5 | The `comm` of the Claude CLI on the VMs; `ps --ppid` there; the registry file's `procStart` there | the owner, R7 |
| M-O6 | The VMs' tmux: `-t "=name:"` is exact (a session `small` beside `small-tasks`) | the owner, R7 |
| M-O7 | Whether the registry's `cwd` follows a `cd` inside a session. If it does, row 6's folder check refuses a good session | the owner, R7 |

**Residuals, named.**

1. A tmux server that does not answer keeps the remote `tmux` call (J6).
2. `procStart` is checked only when both sides have it (decision 8). Without it, a registry file left by an older
   process with the same pid is read as the session's. The window is the seconds before a new Claude CLI writes its
   own file.
3. The proxy leg runs with the person's configuration only (J2).
4. The title fallback cannot ask for a folder whose name has a letter outside ASCII, a space or another character
   outside the folder rule (§ S2.2). The registry path can read it.
5. More than 24 different title rows in one file: the names after the 24th are not seen, except the last. `scan.more`
   says so.
6. `Tunnel`, `LocalCommand` and `UpdateHostKeys` stay the person's (J2, J5).
7. The time bound of the title scan is 8 s on the VM's clock, checked between files.
8. The tools are a Linux VM's (decision 15). On a VM without procps `ps --ppid` the walk finds no `claude` and the
   answer is the fallback; without `head -c` the registry is `registry-bad`.
9. **Connection sharing is the person's (change 2.1 took only the host-key option).** On a Linux or macOS extension
   host, or in a Remote window, a person's `ControlMaster`/`ControlPersist` can make the call go through an open master,
   or become a master that stays after the read (J4). Win32-OpenSSH, the owner's extension host, has no released
   multiplexing. `SECURITY.md` names it in the words of § S2.7 (E2.S4). M-O2 measures it.
10. tmux answers exit 1 for "no such session", for "no server running" and for a socket it cannot reach; the script
    reads all three as `COAI-NO-SESSION` (`absent` `session`). The second is a true absence; the third (a socket in
    another `TMUX_TMPDIR`, a socket with other rights) is a failure that reads as an absence. Telling them apart would
    need tmux's stderr text matched in `sh` (decision 1 says no). S3's `absent` sentence says "no tmux session … that
    ssh can see", not more.

## S2.9 — Scenarios

Unit tests: `node --test` through `scripts/run-tests.mjs`. Every test file starts with
`import '../../scripts/inRunFolder.cjs';`. **RED-GREEN-RED for every row**: (1) the test is written first and seen RED
for the symptom in the *first RED* column — where the module does not exist yet, add a stub so the file compiles;
(2) GREEN with the code; (3) the break-it of § S2.10 is applied to the PRODUCTION line, the test is seen RED with its
message, the line is restored. All three observations go in the summary. There is no host scenario in S2: no module
here imports `vscode`.

**No new temp-API line** (F17): the `sh` tests make their tree with `mkdirSync(join(process.env.COAI_TEST_RUN_FOLDER,
'remote-' + randomUUID()))` — a plain folder in the owned run folder, removed with it. If the guard still reports the
line, stop and ask; do not add a baseline entry.

### S2.9.1 — `src/test/remoteSsh.test.ts` (new; pure, with a fake `capture`)

`H = 'ai1-dev2'`, `S = 'small-tasks'`. `fake(answer)` is a `RemoteSeams` whose `capture` records its arguments and
answers `answer`, whose `exists` answers true, and whose `log` collects lines.

| # | Name | Input → assertion | First RED |
|---|---|---|---|
| S2-T1 | the argv is the parent's, in its order (change 2.1 included) | `sshArgv(H, [S])` deep-equals `[...SSH_OPTIONS, '--', H, 'sh', '-s', '--', "'small-tasks'"]`, and `SSH_OPTIONS` deep-equals the parent's list typed out ONCE in the test — its last two words are `-o`, `StrictHostKeyChecking=yes`; no word is `-n`; no word starts with `Control`; the word before `H` is `--` | no module |
| S2-T2 | every forwarding is off, no question is asked and no host key is accepted | each of `BatchMode=yes`, `RequestTTY=no`, `RemoteCommand=none`, `ClearAllForwardings=yes`, `ForwardAgent=no`, `ForwardX11=no`, `StrictHostKeyChecking=yes` follows a `-o` and stands before `--`. The test's comment says this is PRESENCE; the effect on the configuration is S2-T21, the refusal is M-O3 | no module |
| S2-T3 | a bad host or operand throws before any spawn | hosts `-oProxyCommand=x`, `h;id`, `h\n`, `h\0`, `a b`, `user@h` and the empty string → `sshArgv` throws; operand lists `["a'b"]`, `['a b']`, `['a\nb']`, `['a\0b']`, `[S, 'id', 'x']`, `[S, 'titles', '..']` → throws; through `runRemote` each gives `refused` with the "cannot be given to ssh safely" sentence and the fake `capture` saw NO call. `runRemote(H, S, ['other-session'], …)` (the operands name another session than the log label) → the same refusal, no call. Companion: `runRemote(H, S, [S], …)` with the same fake DOES call it | no module |
| S2-T5 | on win32 the argv crosses unchanged (G0) | for the three forms with every legal first character and a 255-character folder: no word of `sshArgv(…)` holds a space, a tab or `"`; the words after `sh -s --` are `shQuoted` of the operands; `callOf` refuses a folder with a space, a tab or a `"`; the fake `capture` got `shell === false` | no module |
| S2-T6 | the script travels on stdin and nothing else does | `runRemote(H, S, [S], REMOTE_SCRIPT, …)`: the fake `capture`'s `input` is `REMOTE_SCRIPT`; no argv word holds a line break or is longer than 300 characters | no module |
| S2-T7 | a slow host is refused at the limit | fake answers `{ code: -1, output: '', ended: 'timeout', stderrTail: '' }`, `limits.ms` 15000 → `refused`, *"“ai1-dev2” did not answer over ssh within 15 s."*; the fake got `capMs` 15000 and `killTree: true`. The KILL itself is S2-T22 | no module |
| S2-T9 | ssh's own failure is not "no session" | fake answers `{ code: 255, output: '', stderrTail: 'debug\nPermission denied (publickey).\n' }` → `refused`, and the refusal ends with `Permission denied (publickey).`; an empty tail → the "(exit 255)" sentence; a 5000-character last line is cut at 300 with `…`; a control character is taken out | no module |
| S2-T18 | the ssh executable is one absolute path, and its folder is the child's | `sshExecutable('win32', { SystemRoot: 'D:\\W' })` → `D:\W\System32\OpenSSH\ssh.exe`, `cwd` `D:\W\System32\OpenSSH`; with `WINDIR` only; with neither → `C:\Windows\…`; `'linux'` and `'darwin'` → `/usr/bin/ssh`, `/usr/bin`. Through `runRemote`: the fake `capture` got that `path` as its target and that `cwd`; `exists` false → the "not found at" refusal with the path, on win32 with the "OpenSSH Client" sentence, and NO capture call | no module |
| S2-T19 | one log line for a call that did not answer 0, and nothing of the conversation in it | a 255 → ONE line that holds `host=ai1-dev2`, `session=small-tasks`, `end=exit 255` and the stderr text; timeout, capped, not started, no ssh, exit 3 → one line each with its `end`; exit 0 → NO line. With operands `[S, 'titles', '-home-git-secret']` and stdout `TRANSCRIPT-MARK`: no line holds `-home-git-secret` or `TRANSCRIPT-MARK`. A bad operand → the fixed line, with no value in it | no module |
| S2-T20 | `runRemote` never rejects, and a code that is not 0 or 255 is an answer | a fake `capture` that rejects, and an `exists` that rejects → `refused`, no throw. Exit 3 with stdout `x` → `answered`, code 3; `readRemote` of it → `unreadable` `exit` | no module |
| S2-T21 | the options win over a person's config (the REAL `ssh -G`) | write the config of M-G1 in the run folder. `ssh -F <it> -G vmx` shows the forwards, `forwardagent yes`, `batchmode no`, the `remotecommand` (the fixture IS hostile). `ssh -F <it> -G` + `sshArgv('vmx', [S])` shows `batchmode yes`, `clearallforwardings yes`, no line that starts with `localforward`, `remoteforward` or `dynamicforward`, `forwardagent no`, `forwardx11 no`, `requesttty` `no` or `false`, no `remotecommand` line, `loglevel ERROR`, `connecttimeout 5`, `stricthostkeychecking true` (the fixture says `accept-new`). The test asserts nothing about the `control…` lines: they stay the person's (residual 9), and its comment says so. The ssh is `sshExecutable().path`. No ssh there → skip with that reason; when `process.env.CI === 'true'` on linux the ssh MUST be found — the test fails, it does not skip (the ssh half of S2-T44) | no module |

### S2.9.2 — `src/test/captureWidened.test.ts` (new; REAL children, as `captureStops.test.ts`)

All four rows live in this ONE new file, the `launch` half of S2-T22 and S2-T23 included: commit 1 creates it with those
two, commit 2 adds the rest. No existing test file is edited (`processLauncher.test.ts`, `captureStops.test.ts` and
`roundsDbRead.test.ts` stay byte for byte; S2-T11, checklist 5).

| # | Name | Input → assertion | First RED |
|---|---|---|---|
| S2-T8 | the cap counts bytes | a node child that writes 300 `é` and then waits; `maxStdoutBytes` 400 (300 characters, 600 bytes) → `ended: 'capped'`, `code` -1, `output` `''`; it answers well before the 60 s cap, so the child was killed. Companion: `maxStdoutBytes` 700 and a child that ends → code 0 and the 300 characters | `'capped'` is not a `CaptureEnd` (compile) |
| S2-T11 | `capture` is widened, not changed | `capture.length` is 6 (the parameters before `input`); a call with NO eighth argument deep-equals `{ code: 0, output: 'x' }` — no `stderrTail` key; with `{}` the answer has `stderrTail`; with `{ cwd: <the run folder> }` a child that prints `process.cwd()` prints that folder. The existing `captureStops`, `roundsDbRead` and `processLauncher` tests are green with NO edit | the eighth parameter does not exist |
| S2-T22 | a child past the limit is gone | a node child that never ends, `capMs` 150, `{ killTree: true }` → `ended: 'timeout'`, and the child is not alive after it (it wrote its pid to a file in the run folder first; `process.kill(pid, 0)` throws). The `launch` half (in this file): `launch(NODE, […], { killTree: true })` and `kill()` → the exit event comes | `killTree` is not a `LaunchOptions` key |
| S2-T23 | the system folder is one rule | `system32({ SystemRoot: 'D:\\W' })` → `D:\W\System32`; `WINDIR` only; neither → `C:\Windows\System32` | no export |

### S2.9.3 — `src/test/remoteScript.test.ts` (new; pure, and one real `sh -c`)

| # | Name | Input → assertion | First RED |
|---|---|---|---|
| S2-T4 | operands reach the remote shell as ONE literal word each (a real `sh -c`) | for the operands of the three forms, and for the 25 hostile strings of M-Q1 given to `shQuoted` directly: `spawnSync('sh', ['-c', "printf '%s\\n' " + words.map(shQuoted).join(' ')])` prints exactly the words, one on a line. `shQuoted` of a string with NUL, CR or LF throws. No `sh` → skip with that reason; under `CI` on linux it must run | no module |
| S2-T16 | the operands have one grammar | `operandsOf` of the three kinds → the three forms; `callOf(operandsOf(c))` deep-equals `c`; `callOf` is `undefined` for `[]`, `['-s']`, `['a.b']`, `[S, 'id']`, `[S, 'id', 'nope']`, `[S, 'titles']`, four folders, `[S, 'titles', '..']`, `'.x'`, `'a/b'`, `'a b'`, a 256-character folder, the same folder twice, `[S, 'tail', 'x']`, `[S, 'extra']`; it accepts `-home-git-kb`, `C--Users-x`, `a.b_c-d`, a 255-character folder. `operandsOf` of a bad call throws | no module |
| S2-T17 | the script keeps its own rules (a scan of the TEXT, with a companion for each) | the first line is `coai_main() {`; the last two lines are exactly the call line and `printf 'COAI-END\n'`; every other line starts with two spaces or is `}`; it ends with LF and holds no CR; no `<<`, no backtick, no `eval`, no `trap`, no line that starts with `~` after its spaces; no `&` that is not `&&`; every `>` is `>/dev/null`. Companions: the text DOES hold `</dev/null 2>/dev/null` once; the first `set` of the function is `set -f`, and each of the two `set +f` has a `set -f` two lines after it; `\|\| return 0` is there 10 times. The numbers: `tail -c ${TAIL_MAX}`, `head -c ${REGISTRY_MAX + 1}`, `-ge ${SCAN_BUDGET.most}`, `-gt ${SCAN_BUDGET.mostBytes}`, `COAI-BEGIN ${REMOTE_PROTOCOL}` are in the text; `8 * 1000 < SCAN_BUDGET.withinMs`; each name of `REMOTE_TOOLS` is in one of the two `for tool in` lines and no other name is | no module |

### S2.9.4 — `src/test/remoteFrame.test.ts` (new; pure)

`frame(...lines)` joins lines with `\n` after `COAI-BEGIN 1` and before `COAI-END`. `ok(stdout)` = `{ code: 0, stdout }`.
`REG(over)` builds a registry JSON that IS valid (asserted once: a frame with it gives `found`), so each negative row
changes one thing of a fixture the parser accepts. `ID`, `ID2` are two UUIDs. `CALL = { kind: 'session', session: S }`.

| # | Name | Input → assertion | First RED |
|---|---|---|---|
| S2-T24 | a banner is not data, and no sentinel is not "absent" | a `motd` before the frame → the same arm as without it; a banner that HOLDS the line `COAI-BEGIN 1` → the frame starts at the last one; text after `COAI-END` is ignored. No sentinel → `unreadable` `no-sentinel` (never `absent`); `COAI-BEGIN 2` → `version`; no `COAI-END` → `cut`; a line `hello` inside → `frame`; an unknown marker → `frame`; a whole good frame with `code` 3 → `exit` | no module |
| S2-T25 | a transcript line cannot end the transcript | a tail whose data lines are `:COAI-END`, `:COAI-BEGIN 1`, `::x`, `:COAI-FILE-END 9` → `found`, and `lines` are `COAI-END`, `COAI-BEGIN 1`, `:x`, `COAI-FILE-END 9` | no module |
| S2-T26 | a cut tail drops its first part, and a tail cut inside a character is clean | sizes `TAIL_MAX + 1` and `TAIL_MAX + 1`, first data line `\ufffd\ufffdvet"}` → `cut` true, that line is not in `lines`; sizes `10`/`10` → `cut` false and the first line is kept; sizes `TAIL_MAX`/`TAIL_MAX + 5` (grew during the read) → `cut` true; empty data lines are left out | no module |
| S2-T27 | a registry file that is not right is `unreadable`, each fault by its name | text over `REGISTRY_MAX` bytes and under it in characters (33000 `é`: 66000 bytes, 33000 units) → `registry-big`; `{not json` → `registry-bad`, and `JSON.stringify` of the result does not hold `not json`; `sessionId` `'x'`; `pid` that is another number; no `cwd`; a `cwd` with a line break → `registry-bad`; `procStart` `'41288'` with the marker's start `41289` → `registry-stale`; equal → `found`; no `procStart`, or start `-` → `found`; `COAI-REGISTRY <pid> denied` → `registry-denied`. A `name` with a control character → `found` with `name` `''` | no module |
| S2-T28 | the transcript is found by its id, once, in the session's folder | files 0 → `absent` `transcript`; 2 → `ambiguous` `transcripts` with no candidates; 1 with a folder that is not one of `projectDirNames(cwd)` → `unreadable` `folder`; each name that `projectDirNames('/home/git/knowledge_base')` gives (taken from the call, not typed) → `found`; a `COAI-TRANSCRIPT` id that is not the registry's → `id-mismatch`; registry valid and no `COAI-TRANSCRIPT` → `frame` | no module |
| S2-T29 | a picked id: the six cases | `call = { kind: 'id', session: S, id: ID2 }`. (a) one pane, one `claude`, a valid registry of `ID` → `found` `via: 'registry'` of `ID`; (b) two panes, named candidates `ID` and `ID2`, the transcript of `ID2` → `found` `via: 'id'` with that candidate's `cwd` and `name`; (c) one pane, no `claude` → `found` `via: 'id'`, `cwd` `''`; (d) the file count 0 in (b) and in (c) → `gone` with `ID2` (never `absent`, never `fallback`); (e) two named candidates, none is `ID2` → `ambiguous` with both; (f) one named candidate that is not `ID2` and one `claude` with registry `none` → `found` `via: 'id'`. A `COAI-TRANSCRIPT` of another id → `frame` | no module |
| S2-T30 | the fallback's first call | one pane, no `claude`, `COAI-CWD` `:/home/git/kb`, three folder lines, `COAI-FOLDERS-END 2 0` → `fallback` with that `paneCwd`, the three names, `skipped` 2, `more` false; `… 0 1` → `more` true; a folder line `a b` → `frame`; a `COAI-CWD` line that does not start with `/` → `frame`; one pane, one `claude`, registry `none` → `fallback` | no module |
| S2-T31 | the title rows are read by the one rule | `call` with folders `F1`, `F2`; a `COAI-SESSION ID 1794411443 201 rows` with rows ai-title `A`, custom-title `small-tasks`, ai-title `A` (the last again) → `names.latest` `A`, `names.former` `['small-tasks']`; a `skipped` session → `skipped` true and `NO_NAMES`; mtime `-` → 0; `COAI-SCAN 250 count` → `scan.why` `count`; `COAI-ROWS 3 1` → `tooLong` 3, `more` true; a `COAI-FOLDER` that is not in the call → `frame`. End to end: `namedAmong(sessions, (s) => s.names, 'small-tasks')` over three sessions gives the one | no module |
| S2-T32 | several panes or processes are a list | two panes with one named `claude` each → `ambiguous` `panes`, two candidates with their `pane`, `pid`, `sessionId`, `name`, `cwd`, `unnamed` 0; one pane with two `claude`, one with registry `none` → `ambiguous` `processes`, ONE candidate, `unnamed` 1; two panes and no `claude` → `ambiguous` `panes`, none, `unnamed` 0 — never `found`, never `fallback` | no module |
| S2-T33 | every fault has its own words | `COAI-NO-SESSION` → `absent` `session`; each `COAI-BAD <what>` → `unreadable` with that word; `COAI-OVER panes` and `walk` → `too-many`. For EVERY key of `UNREADABLE_SAID` (taken from the object): the clause is not empty and all clauses differ; and this file reached that `why` in some test (a set filled by the tests, compared at the end) | no module |

### S2.9.5 — `src/test/remoteScriptRuns.test.ts` (new; the REAL script by `sh -s`)

**The harness.** `tree()` makes, in the run folder: `home/.claude/sessions`, `home/.claude/projects`, `bin/`, `fx/`.
`bin/` holds a fake `tmux` and a fake `ps` (below) and a link to the real file of each other name of `REMOTE_TOOLS`,
plus `stat`. `run(shell, operands, opts)` = `spawn(shell, ['-s', '--', ...operands], { env: { HOME: <home>, PATH: <bin>
} })` — the environment holds NOTHING else — with `REMOTE_SCRIPT` written to stdin. `shell` is an ABSOLUTE path, found
once on the test's own `PATH`: node looks a bare command up on the `PATH` of `env`, and that is `bin/`, which holds no
`sh`. Each test runs for every shell that
is there: `sh`, and `bash --posix` (it reads a script byte by byte, `dash` in blocks: M-S3). The operands are given
WITHOUT quotes (no login shell is in the way here). No `sh` → skip with that reason. **S2-T44**: when
`process.env.CI === 'true'` and the platform is `linux`, `sh` MUST be found — the test fails, it does not skip. (The ssh
half is S2-T21's, in commit 6: this file is commit 5 and cannot import `sshExecutable` yet.)

The fakes are `sh` scripts. They set their own `PATH=/usr/bin:/bin` first (the script under test sees only `bin/`). Each
appends one line to `fx/log`: its name, its pid, its arguments, and what its stdin is (`readlink /proc/$$/fd/0`). When `fx/eat`
exists they then read ALL their stdin (`cat >/dev/null`). `tmux list-panes -s -t "=<name>:" -F …` prints `fx/<name>.panes`
or exits 1; **for any target that is not exactly `=<name>:` it exits 2** (the fake holds the form that M-T1 and M-T2
measured). `tmux display-message -p -t %<n> …` prints `fx/<n>.cwd`. `ps -o comm= -p <pid>` and `ps -o pid= --ppid <pid>`
read `fx/ps.txt` (lines `pid ppid comm`); when `fx/slow` exists the fake `ps` first sleeps 0.1 s, so a test that counts
calls after a closed reader does not depend on the machine's speed. Fake pids are over 99000000, so no `/proc/<pid>`
exists for them.

| # | Name | Setup → assertion | First RED |
|---|---|---|---|
| S2-T34 | the registry path, end to end, through the real parser | one pane, `bash` → `claude`, a registry file of `ID`, a transcript of 4 lines in the folder `projectDirNames` gives for the registry's `cwd`; one of the lines is `COAI-BEGIN 1` and the last has no line end → `readRemote({code, stdout}, CALL)` is `found`, `via: 'registry'`, the 4 lines exactly, `cut` false. Exit code 0. `fx/log` holds exactly: 1 `tmux`, 3 `ps` | no module |
| S2-T12 | an empty glob does not eat the script, and no tool reads it | NO `~/.claude/projects` folder at all, `fx/eat` set, one pane with no `claude` → the frame is whole (`COAI-BEGIN 1` … `COAI-END`), the arm is `fallback` with no folders; with `id ID` → `gone`. EVERY line of `fx/log` says `stdin=/dev/null` (Linux; elsewhere this one assertion is left out, and the test says so) | no module |
| S2-T13 | a `claude` with a child is one candidate | one pane; `claude` (pid A) with a `node` child and a `claude` grandchild → ONE `COAI-CLAUDE` line, pid A; `fx/log` has no `ps` call that asks for the children of A. Two sibling `claude` → `ambiguous` `processes` with what could be named. The pane's own process is `claude` → one candidate, that pid | no module |
| S2-T14 | several panes are a list, never a pick (change 1.1 point 2) | two panes in one window; two windows; one pane with two `claude` → `ambiguous` with every candidate that has a registry file; NO `COAI-TRANSCRIPT`, `COAI-CWD` or `COAI-FOLDERS` line is printed. Two panes where ONE runs `claude` with a valid registry and a transcript → `ambiguous` `panes` with that one candidate, and no `COAI-TRANSCRIPT` line (the fixture B19 needs). Two panes with no `claude` → `ambiguous`, none. The companion: the same registry and transcript with ONE pane → `found` | no module |
| S2-T15 | nothing is left behind | `fx/eat` set; the test writes the script and does NOT close stdin → `COAI-END` comes within 5 s (no command waits for input); then stdin is closed → the `sh` process ends within 5 s with code 0, and no fake is alive (each `fx/log` line holds the fake's pid; `process.kill(pid, 0)` throws for each) | no module |
| S2-T35 | every arm comes out of the real script | one fixture for each, through `readRemote`: `fallback` (a pane, no `claude`, three folders and one named `has space` → `skipped` 1); `titles` (two folders; one file with an ai-title, a custom-title, the ai-title again, and one raw line of 3000 bytes that holds `"custom-title"` → the names, `tooLong` 1); `gone`; `absent` `session` (no such tmux session); `absent` `transcript`; `ambiguous` `transcripts` (one id in two folders); `unreadable` `registry-denied` (mode 000; left out when the test runs as root); `unreadable` `too-many` (a chain of 20 processes) | no module |
| S2-T36 | the session name is exact | fixtures `small.panes` and `small-tasks.panes` with different panes → the call for `small` lists `small`'s pane. Every `tmux list-panes` line of `fx/log` has `-t =<name>:` with the colon. NOT covered: a real tmux (M-T1, M-T2 here; M-O6 on the VMs) | no module |
| S2-T37 | the script only reads | a listing of the whole `home` tree (path, size, mtime, mode) before and after one run of each of the three forms is identical; the run folder has no new entry outside `fx/log` | no module |
| S2-T38 | a tool that is not there is said | for EACH name of `REMOTE_TOOLS` (taken from the constant) and each form that runs it: the link is taken out of `bin/` → `unreadable` `tool` — never `absent`, `fallback` or a `titles` with no rows. The nine names of the first `for tool in` line are checked by every form; `tmux` and `ps` by the `session` and `id` forms only (the `titles` form never runs them, so without them it still answers its rows — the test asserts that too). Companion: with all of them there → `found`, `gone` and a `titles` with its rows | no module |
| S2-T39 | a reader that is gone stops the script | a 12-pane fixture, `fx/slow` set; the test reads the first line and destroys its end of stdout → the process ends within 5 s; `fx/log` holds fewer than 6 `ps` lines (a whole run holds 24). The same when the shell was started with `SIGPIPE` ignored (`sh -c "trap '' PIPE; exec <the shell's absolute path> -s -- many"` — absolute, because the `PATH` is `bin/` and holds no `sh`) | no module |
| S2-T40 | a tail over 4 MiB is cut, and says so | a transcript of 5 MiB of JSON lines with multi-byte text → `found`, `cut` true; every line of `lines` parses as JSON; `Buffer.byteLength` of the joined lines is at most `TAIL_MAX`; the last line is the file's last line | no module |
| S2-T41 | a name the script cannot frame is left out, not sent | a project folder whose name holds a line break and one whose name holds a space → both counted in `skipped`, the frame is whole. A transcript in a folder named `-home-git-my proj` (the registry's `cwd` is `/home/git/my proj`) → `found` on the registry path, `folder` exactly that name | no module |
| S2-T42 | the start time in the frame is the process's own (Linux) | the fixture's `claude` pid is the TEST's own pid, and its registry's `procStart` is field 22 of `/proc/self/stat` read by the test → `found`; with `procStart` + 1 → `unreadable` `registry-stale` | no module |
| S2-T43 | the title scan stops at its bounds | 251 session files in one folder → `scan.why` `count`, 250 sessions. A fake `date` in `bin/` that answers `100` and then `109` → `scan.why` `time` after the first file. The 64 MiB bound is NOT run with a real file: S2-T17 holds its number and S2-T31 the parser's `skipped` row, and `module_tests.md` says so | no module |

### S2.9.6 — `src/test/remoteLogScan.test.ts` (new; S2-T10)

| # | Name | Assertion | First RED |
|---|---|---|---|
| S2-T10 | no stdout, no operand and no caught error in any log | **The scanner** `logLeaks(text): readonly string[]` (in the test file) finds, across lines, every `console.<name>(` and `notify(` call and reports one whose argument text holds a banned word: `stdout`, `output`, `lines`, `tail` (the whole word; `stderrTail` is not one), `operands`, `folders`, `folder`, `paneCwd`, `cwd`, `candidates`, `names`, `rows`, `script`, `.message`, `asText(`, `reason`. **On samples** (inline strings): `console.error('x', run.stdout)`, a three-line `notify({ … detail: asText(reason) })`, `console.error(\`${operands}\`)` → one finding each; `seams.log(logLine(host, session, end, stderrTail))` and `console.error('coai.takeFromTerminal failed')` → none. **On the tree**: every file `src/remote*.ts` is scanned whole → no finding. The files are found by that name pattern, so S3's `remoteRead.ts` and `remoteSessionHost.ts` are scanned from the day they exist; the door functions in other files are S4's part (X10). **Companions**: the pattern finds `remoteScript.ts`, `remoteFrame.ts` and `remoteSsh.ts`; `console.` is in the three S2 modules exactly ONCE (the default `log` seam of `remoteSsh.ts`); `logLine(` is called once, and its declaration has exactly the parameters `host`, `session`, `end`, `stderrTail` | no module |

### S2.9.7 — Mapping to the stories document

S2-T1…T15 keep their ids and their meaning. The rows "also, in the spec's own table" of § 5 are: the framing S2-T24,
S2-T25, S2-T26; the registry file S2-T27, S2-T42; the transcript by id S2-T28; the picked id S2-T29 (and S2-T12, S2-T35 for
`gone` from the real script); the fallback's three calls S2-T30, S2-T31, S2-T35; the bounded title row S2-T35, S2-T43.

## S2.10 — Break-its (a PRODUCTION change; each seen RED with its message, then restored)

| # | Change | Goes red |
|---|---|---|
| B1 | `SSH_OPTIONS` gets `-n` | S2-T1 — `the remote sh -s would read an empty stdin` |
| B2 | `ClearAllForwardings=yes` taken out | S2-T2 — `the owner's LocalForward would be opened a second time`; S2-T21 — a `localforward` line |
| B3 | `sshArgv` without `--` before the host | S2-T1 — `the host could be read as an option` |
| B4 | `shQuoted` throws for CR and LF only, not for NUL | S2-T4 — red naming NUL (the alphabets still stop a NUL in `sshArgv`; this is the second line) |
| B5 | `shQuoted` puts the word in double quotes | S2-T4 — red naming `$(id)` (the command does not parse); S2-T5 — a word holds `"` |
| B6 | the folder rule allows a space | S2-T16; S2-T5 — `this operand would be re-quoted on its way to ssh.exe` |
| B7 | the folder rule's first class allows `.` | S2-T16 — `..` was accepted; S2-T3 |
| B8 | `sshArgv` does not ask `callOf`; then `runRemote` does not compare `operands[0]` with `session` | S2-T3 — the fake `capture` WAS called for `[S, 'id', 'x']`; then for `['other-session']` |
| B9 | `runRemote` puts the script in the argv | S2-T6 |
| B10 | `runRemote` maps code 255 to `answered` | S2-T9 — `a refused key read as an empty session` |
| B11 | `runRemote` passes no `killTree`; then `capture` on its timeout answers without `child.kill()` | S2-T7; S2-T22 — `a timed-out ssh was left running` |
| B12 | `capture` counts `chunk.length` | S2-T8 — `8 MiB of characters is not 8 MiB` |
| B13 | `capture` gets `options` as a seventh parameter before `input`; then it puts `stderrTail` in every answer | S2-T11 — red naming the signature; red naming the extra key |
| B14 | `sshExecutable` answers the bare `ssh`; then `cwd` is left out of the capture call | S2-T18 |
| B15 | `logLine` gets the operands; then the stdout | S2-T19 — the folder name is in the line; S2-T10 — a banned word |
| B16 | the call line of the script without ` </dev/null` | S2-T12 — a tool call whose stdin is not `/dev/null` (and under `bash` the frame has no `COAI-END`: `the rest of the script was read as data`); S2-T15 — the test's own timeout, naming `tmux`; S2-T17 |
| B17 | `coai_walk` enters a `claude` (the `return 0` after its `printf` taken out) | S2-T13 — `one Claude CLI was read as two sessions` |
| B18 | the panes line reads `-t "=$session"` (no colon) | S2-T36 — the fake tmux exits 2: `the session name was not exact`. Then `display-message` in place of `list-panes -s`: S2-T14 — `a two-pane session was read without a choice` |
| B19 | `exact` is 1 for any pane count | S2-T14 — a `COAI-TRANSCRIPT` line for the two-pane session with one `claude` |
| B20 | `coai_transcript` without the `-f` test of each match | S2-T12 — `gone` became a failure (`COAI-BAD transcript`) with no projects folder |
| B21 | the first `for tool in …` check taken out | S2-T38 — with no `grep` the `titles` form answers a `titles` with no rows: `a missing tool was read as no session` |
| B22 | `\|\| return 0` taken off the `COAI-PANE` line | S2-T39, the `SIGPIPE`-ignored half — all 24 `ps` calls ran |
| B23 | the script's `tail -c 4194304` becomes `cat` | S2-T40; S2-T17 (the number) |
| B24 | `coai_data` becomes `cat` (no `:`) | S2-T34 — the transcript line `COAI-BEGIN 1` moved the frame; S2-T25 is the parser's half |
| B25 | the parser takes the FIRST `COAI-BEGIN 1` | S2-T24 — a banner that holds the sentinel |
| B26 | the parser answers `absent` when there is no sentinel | S2-T24 — `a broken answer was read as no session` |
| B27 | the parser does not drop the first line of a cut tail; then it reads only the first size | S2-T26 |
| B28 | the parser does not compare `procStart`; then it does not check `pid` | S2-T27, S2-T42 |
| B29 | the parser skips the folder check | S2-T28 — `a file in another project's folder was read as this session` |
| B30 | the parser answers `absent` for a picked id with no file; then it answers `found` for row 8's "else" | S2-T29 (d) — `a vanished pick was reported as no session`; S2-T29 (e) |
| B31 | the parser answers `found` for the first of two candidates | S2-T32, S2-T14 — `a two-pane session was read without a choice` |
| B32 | the parser builds the names with its own JSON read in place of `collectNames` (the `latest` is then the first row) | S2-T31 |
| B33 | `readRemote` ignores `run.code` | S2-T20, S2-T24 |
| B34 | `-o`, `StrictHostKeyChecking=yes` taken out of `SSH_OPTIONS` (change 2.1) | S2-T1 — the list; S2-T2 — `an unknown host key would be accepted by a call a terminal's name started`; S2-T21 — `stricthostkeychecking accept-new` |

## S2.11 — The brief: order, files, commands

One coder, on `feat/chat-from-a-terminal-e2`, on top of `b51dd174` or later. Each commit is green on its own (`npm run
compile` exit 0, the whole suite, lint). Stage by path. **`SSH_OPTIONS` is exactly the list of § S2.5: the parent's
options and change 2.1's two words `-o`, `StrictHostKeyChecking=yes` last. `ControlPath=none` was NOT taken. No other
option, ever, from the coder: a further ssh option is a plan change and a full stop.**

**Sizes (the 800-line limit, `eslint.config.mjs:113`, holds for `src/**` and not for `src/test/**`).** Expected:
`remoteScript.ts` ≈ 420 lines (293 of them the script), `remoteFrame.ts` ≈ 500, `remoteSsh.ts` ≈ 220. If
`remoteFrame.ts` would pass 700 lines, stop and tell the caller; do not add a file the spec does not name.

0. **Step 0, no commit.** Check that the text of § S2.3 is the text you will send: copy the block to a file, and
   `sha256sum` it — it must be `1c3b25f9…ecad07` (293 lines, the last line ends with LF).
1. **`feat(launcher): a caller may ask for the tree kill, and the system folder is one rule`** — `processLauncher.ts`
   (`LaunchOptions.killTree`, `system32`, `TASKKILL` from it); NEW `captureWidened.test.ts` with S2-T23 and the `launch`
   half of S2-T22.
2. **`feat(capture): one options object — the working folder, a byte cap, the stderr tail`** — `versionProbe.ts`;
   `captureWidened.test.ts` gets S2-T8, S2-T11 and the `capture` half of S2-T22.
3. **`feat(remote): the operands and the script that is sent`** — `remoteScript.ts`; `remoteScript.test.ts` (S2-T4,
   S2-T16, S2-T17). Then `node -e` prints the sha256 of the compiled `REMOTE_SCRIPT`: it must be the hash of step 0.
   Put the command and its output in the summary.
4. **`feat(remote): the frame and the typed result`** — `remoteFrame.ts`; `remoteFrame.test.ts` (S2-T24…T33).
5. **`test(remote): the script, run by a real sh`** — `remoteScriptRuns.test.ts` (S2-T12…T15, S2-T34…T44). A red here
   that needs a change of the script text is NOT fixed in silence: say it in the summary with the new hash; the caller
   updates § S2.3.
6. **`feat(remote): the ssh edge`** — `remoteSsh.ts`; `remoteSsh.test.ts` (S2-T1…T3, T5…T7, T9, T18…T21; S2-T21 holds
   the ssh half of S2-T44).
7. **`test(remote): no log line carries the conversation`** — `remoteLogScan.test.ts` (S2-T10).
8. **`docs(research): the remote reader's contract`** — § S2.12.

Commands, from `src_vs_code/` (the stories document's § 3 has the full list):

```bash
npm run compile                                                # read the exit code
node scripts/run-tests.mjs out/test/remoteFrame.test.js        # one file, after compile
node ../.agents/conventions/tools/low-priority.mjs npm test    # the whole suite
npm run lint                                                   # eslint src
node scripts/count-notifications.mjs --write                   # expect NO change: S2 adds no notify site
```

- Lint (F16): every new function has `complexity` at most 4 and at most 50 lines; no file passes 800 lines; the counts
  in `eslint-suppressions.json` for `src/versionProbe.ts` and `src/processLauncher.ts` do not grow. The parser is many
  small functions and tables typed by the marker and by the fault, not one long `switch`.
- No new host module. The three modules import no `vscode`. `remoteSsh.ts` imports `capture`, `system32`,
  `hostPlatform`, `credsSession`, `callOf`, `shQuoted`; `remoteFrame.ts` imports `isSessionId`, `projectDirNames`,
  `collectNames`, `isTerminalName` and the constants of `remoteScript.ts`. None of these rules is typed a second time.
- Do not touch: `credsSession.ts`, `chatStore.ts`, `chatSessionJoin.ts` (S1); the consent, the pick list, `remoteRead.ts`,
  `remoteSessionHost.ts` (S3); the doors, the page, the help, `SECURITY.md` (S4); `chatSource.ts` (X6).
- From the worktree root, before any commit that touches `todo/`: `node .agents/conventions/tools/plan-lifecycle.mjs`.

## S2.12 — Documents (commit 8)

- `research/module_extension.md`, section *The chat reads the remote Claude session (E2)*: the ssh edge — the argv and
  what each option asks for, with the table of J5 and the date and versions of M-G2; the executable and the child's
  folder and why (J1); the Windows argv (no space, tab or `"` in any word); the limits and the tree kill; the log rule.
  The script — its three forms, its rules 1–13, the bounds table. The frame and the decision table of § S2.4. Every
  sentence about an ssh option says "we pass", and names M-O1…M-O7 as not observed yet. Change 2.1
  (`StrictHostKeyChecking=yes`) and residual 9 (connection sharing is the person's; not on Windows) in the words of § S2.7.
- `research/architecture.md`: the new OUTWARD edge — the extension host → the system `ssh` → a VM the person agreed to,
  read-only (the edges section and the cross-cutting security part).
- `research/module_tests.md`, the row *Chat with other AI — THE REMOTE SESSION (E2)*: S2's tests by file. **Not
  covered**, with the reason: a real ssh call and a real sshd (R6; M-O1…M-O4); the real `comm`, `ps` and registry on the
  VMs (R7; M-O5); a real tmux (M-O6); the 64 MiB bound of the title scan by a real file; what `ssh.exe` does with a
  `ControlPath` in a config.
- `SECURITY.md` is S4's. S4 takes § S2.6 and § S2.7 as its source, in "we pass" words — the two paragraphs of § S2.7
  among them.

## S2.13 — What the Opus reviewer checks

1. `REMOTE_SCRIPT` has the sha256 of § S2.3, or each difference is in the summary with its reason and the new hash is in
   this spec. The script was run by `sh -s` in CI: the job's log shows S2-T12…T15 and S2-T34…T44 run, not skipped.
2. The last two lines of the script are the call with `</dev/null 2>/dev/null` and the `COAI-END` line. No command in
   the function names a data file as an argument except the two after `--`.
3. `SSH_OPTIONS` is the parent's list, word for word and in its order, with change 2.1's `-o`,
   `StrictHostKeyChecking=yes` last (parent ledger entry 2.1). No `ControlPath` and no other option anywhere
   (`grep -n "'-o'" src/remoteSsh.ts`).
4. S2 added no `spawn`, `exec` or `execFile` call (`git diff` of `src/` shows none; `processLauncher.ts` keeps its two
   `spawn` sites). `remoteSsh.ts` reaches a process only through `capture`, with `shell` false; the executable is never a
   bare name.
5. `capture`'s first seven parameters did not move. A call with no eighth argument answers the same object as before
   (S2-T11). No existing test file of `capture`, `launch` or `roundsDbRead` was edited.
6. The host rule and the session rule are `credsSession`, called; the id rule is `isSessionId`, called; the folder rule
   is written once, in `callOf`. `sshArgv` throws before any use of a value, and its error text holds no value.
7. `readRemote` never throws (fuzz it in a scratch run with 1000 random frames) and no arm holds an error's message.
   `JSON.parse` is in one function with a `catch` that returns nothing of the error.
8. The decision table of § S2.4 is in the code as it is written here: no arm is reached by a default; `ambiguous` is
   never turned into a `found`; `gone` comes only from an `id` call; `fallback` only from one pane.
9. The numbers in the script equal the constants (S2-T17), and no rule of `projectDirNames`, `namedAmong` or
   `collectNames` was typed again in `sh` or in TypeScript.
10. `console.` is in the three modules once. `logLine` has four parameters. No log, no refusal and no thrown error holds
    stdout, an operand or a caught error (S2-T10, S2-T19).
11. Every refusal sentence of § S2.5 is as written; each names the host where the table says so.
12. The fakes hold what was measured (the exact target; `comm` `claude`), and `module_tests.md` says what a fake cannot
    prove.
13. Every scenario has its three observations in the summary, with the RED messages. Each break-it of § S2.10 was applied
    to the PRODUCTION line named, the script's lines among them (B16…B24).
14. No new temp-API line in a test; `raw-temp-baseline.json` did not change; `eslint-suppressions.json` did not grow;
    `notification-sites.json` did not change.
15. The documents say "we pass" for every ssh option, and list M-O1…M-O7 as owed.
16. Nothing of S3 or S4 was built: no consent, no pick list, no `vscode` import, no door.

## Definition of Done — E2.S2

- [x] The security judgement (§ S2.6) and the proposal of § S2.7 were read by the caller BEFORE any S2 code; 2.1 is
      recorded in the parent (`StrictHostKeyChecking=yes` taken; `ControlPath=none` not taken, residual 9), and this spec
      and its brief say so.
- [ ] This spec was reviewed by Opus (gate G6) and "Changes to § 4" is in the stories document and in the S3 and S4
      briefs, in one commit.
- [ ] S2-T1…T44 green; each first RED and each break-it B1…B34 observed with its message; all in the summary.
- [ ] `npm run compile` exit 0; the whole suite green on CI with the `sh -s`, `sh -c` and `ssh -G` tests RUN (S2-T44);
      `npm run lint` green; no suppression count grew.
- [ ] The sha256 of `REMOTE_SCRIPT` is the one in § S2.3 (or the spec holds the new one, with the reason).
- [ ] `research/module_extension.md`, `research/module_tests.md` and `research/architecture.md` updated; M-O1…M-O7 named
      as not observed; R6 and R7 carry them when the owner runs them.
- [ ] `node .agents/conventions/tools/plan-lifecycle.mjs` clean.
