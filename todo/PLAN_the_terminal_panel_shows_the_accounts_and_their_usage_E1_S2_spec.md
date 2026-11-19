# PLAN — E1.S2 spec: the accounts script, its reply and its parser (CORNERSTONE)

> Status: **plan only, nothing implemented yet, 2026-11-19.** Written by Fable before any code of the story; the Opus
> review of this text (gate G2) is owed. Cornerstone spec of story E1.S2 of
> [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1.md)
> (parent [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md),
> § 2, § 4, § 5, § *Security*, § *Growth surfaces*; the gate's findings 3, 4 and 5 are folded in there). It holds the
> exact script text, the reply's grammar, the parser's rules, the scenarios and the security judgement on that text
> (§ A2.9), which must stand before any S2 code. Its own file, because the stories document goes whole to a review
> gate and must stay short. It keeps no change ledger: a change is an entry in the parent (epoch 1, 0 changes; the
> next accepted change is 1.1 and the one after it is a full stop). It is promoted with the E1 document.
>
> Scope: `src_vs_code` only, and one step of `.github/workflows/ci.yml`. NEW pure `src/remoteFraming.ts`,
> `src/remoteAccountsScript.ts`, `src/remoteAccountsUsage.ts`, `src/remoteAccountsReply.ts`; `src/remoteFrame.ts` (its
> private frame finder calls the shared one); `src/notifications.ts` (one pattern gets a name and an `export`, its
> text unchanged); the tests. Coder: Sonnet. Review: Opus. Read at `b4aa611b`. All paths are under `src_vs_code/`,
> but `research/`, `todo/`, `.github/` and `.agents/`, which are from the repository root.

## Changes to the parent plan — none

The parent allows one change in epoch 1 before a full stop, and the E1.S1 spec may need it. This spec needs **zero**:

| Kind of change (parent § 5, last paragraph; stories document § 6, G1) | In this spec |
|---|---|
| A new ssh option | None. The script travels on the call the session reader makes: `SSH_OPTIONS`, `--`, the host, `sh -s --`, one operand (S3 builds it) |
| A third kind of ssh call | None. One script, two operands |
| A file with a secret that the parent does not name | None. The script opens ONE kind of file: the Codex session files the parent names. It tests that `~/.codex/auth.json` and agy's sign-in file exist, as the parent says; it opens neither |
| A write by the accounts script | None. No `>` but `>/dev/null`, no temporary file, no here-document; `cd /` is its only change of state (measured: M-A6) |
| Anything installed on a VM | None. A host without `jq` is told the manager word; the script runs no package manager |
| Road (b) | Not in the text |

**Five things are inside the parent's text, but the caller should read them, because "in doubt, it is a change" is the
caller's call and not this spec's.** Each has a way back that costs nothing in the ledger.

| # | What | Why it is inside the text | The way back |
|---|---|---|---|
| R1 | The script runs `stat -c %Y` once, as an OPTIONAL tool, for the time of the newest Codex session file | The parent's reply sends "the file's time and the word `file`" when a record's own time does not parse (§ 4, *The time rule*), and its measured host has `stat` in `/usr/bin` (§ *What step 0 measured*). The parent's tool sentence does not list it. It is read-only metadata of a file the plan already reads; without `stat` the fact is `-` | Take the `stat` line and its `case` out and set `ftime=-`: the time is then the record's own or not known |
| R2 | The reply has more words than the parent's sketch: `COAI-JQ`, `unreadable auth`, `unreadable timeout`, `failed error`, `failed no-sessions` | The parent says "the spec fixes the exact words", and the stories document asks for "every word of the reply" | — |
| R3 | Each vendor command runs under `timeout -k 1 <n>`: a KILL one second after the TERM | The parent's limits are the TERM (2, 3, 8, 4 s). Without `-k` a program that ignores TERM is never ended. The ceiling of 30 s holds (§ A2.3, the time table) | Drop `-k 1` |
| R4 | Codex is `found` when `codex` is on the `PATH` OR the folder `~/.codex` exists | The parent reads session FILES and never starts `codex`. A `codex` installed where a shell that is not a login shell does not look would else read as "absent" while its files are there | Test the `PATH` only |
| R5 | The three line patterns need the whole form, the reset clause included | The parent's patterns, word for word. A line of a window with no reset clause is counted in `:unmatched` and not read. What such an account prints was not measured (the RESULTS file, § 9) | — |

## Changes to § 4 of the stories document (for gate G2)

The caller puts these into § 4 of the stories document and into the briefs of S3 and S4 in ONE commit, before S3 or S4
is coded. The rows are X1…X11 (the stories document uses C for contracts, K for conflicts and G for gates).

| # | Contract | The stories document says | This spec says | Why | Who must follow |
|---|---|---|---|---|---|
| X1 | C2, the files | two modules, `remoteAccountsScript.ts` and `remoteAccountsReply.ts` | Four: those two, NEW `src/remoteAccountsUsage.ts` (the value rules: the Claude lines, the reset, the Codex windows and times) and NEW `src/remoteFraming.ts` (the shared frame finder, K11) | The 800-line limit and `complexity` 4 (`eslint.config.mjs:113-115`); the frame finder must not live in either parser | S3 and S4 import from the two modules C2 names; nothing else moves |
| X2 | C2, the parser | `readAccounts(run, mode)` | `readAccounts(run, mode, nowMs)` | The reset text has no year, so the parse needs this machine's clock; a clock read inside a pure parser makes its tests expire (`testing.md`, *A test that reads the clock expires*) | S3's `parse` seam is `(run, mode) => readAccounts(run, mode, seams.now())` |
| X3 | C2, Claude | `unreadable` (`why`, `no-jq` among them); `failed` (`timeout` \| `format` \| `model-call` \| …) | `unreadable` `why`: `no-jq` \| `auth` \| `timeout` \| `credential-shape`. `failed` `why`: `timeout` \| `error` \| `format` \| `model-call`. `read` gains `unmatched: number` | A killed account command and one that prints nothing readable are different faults; a usage command that ends with an error is not a format this version cannot read | S3 `blockedAfter`: only `format` and `model-call` block a host; `error` and `timeout` are tried again next round. S4: one sentence for each new `why` (proposed in § A2.7) |
| X4 | C2, Codex | `failed` (`none` \| a record too far from the end); `asOfMs`, `asOfFrom` | `failed` `why`: `no-sessions` \| `none` \| `error`. `asOf: { ms; from: 'record' \| 'file' } \| null`. `unreadable` gains `credential-shape` | The script cannot tell "no record" from "a record further back than 1 MiB" without reading the whole file; the parent's sentence (*no usage record is near the end of the newest Codex sessions*) is true of both, so both are `none`. A time that is not there is `null`, not two fields to interpret | S4: the three sentences; `asOf === null` draws the age as `—` |
| X5 | C2, agy | `absent` \| `found` | adds `unreadable` (`credential-shape`); `found` gains `usage: { state: 'no-source' }` | Every value of every vendor passes the same net; "no usage source" is a value, not an absent field (A2-T20) | S4 |
| X6 | C2, `jqMissing` | — | The manager word whenever the VM said `jq` is missing, also on a host where no vendor needs it | The script reports a fact; whether the row is worth showing on a host with no AI CLI is the list's choice, and R10 wants the fact for every host | S4 |
| X7 | C2 and C6, VM text | the parser takes out control and format characters and cuts; the `$(` break is the list's (`forList`) | The parser's ONE value function `plainValue` does all three: characters out, `$(` written `$ (`, cut at 200 characters. `forList` CALLS `plainValue` (it is idempotent) and does not export `plainOf` from `remoteRead.ts` | One road in at the parser, so a new reader of the typed result cannot forget the break; one implementation, not a second copy of the character class (there are two today: `remoteRead.ts:67`, `remoteSsh.ts:216` — § A2.14, N1) | S4 `forList`; A4-T12 and A4-H1 keep their expected texts |
| X8 | C4 (S3 owns), from § A2.8 | `UsagePolicy` = `every-round` \| `hourly` \| `on-press` | adds `{ kind: 'off' }`: `modeFor` answers `no-claude-usage` for every host and every trigger | W7's stated consequence — "the Claude usage read is then off and the list shows the account only" — needs a value of the one constant the round reads, as W6's rows have | S3 `modeFor`, `due`, A3-T17 |
| X9 | C3 (S3 owns), K17 | `ACCOUNTS_LIMITS` = 30 s, 64 KiB | unchanged; A3-T4 gains one assertion: `ACCOUNTS_LIMITS.maxStdoutBytes >= ACCOUNTS_REPLY_BYTES_MAX` (33 858) | The runner's cap is over the whole stdout, a banner included; the reply's own caps are this spec's. The two must not cross in silence | S3 |
| X10 | Scope | S2: new modules and `remoteFrame.ts` | adds `.github/workflows/ci.yml` (one step, K12) and `src/notifications.ts` (one exported name) | § A2.10; § A2.7, the credential net | — (the stories document gives neither file to S1) |
| X11 | C2, exports | — | `ACCOUNTS_PROTOCOL`, `ACCOUNTS_SECONDS`, `ACCOUNTS_LINES_MAX`, `ACCOUNTS_LINE_BYTES_MAX`, `ACCOUNTS_REPLY_BYTES_MAX`, `MANAGER_WORDS`, `ACCOUNTS_UNREADABLE_SAID` | S4's sentence *"Claude Code did not answer within 8 s"* takes its number from `ACCOUNTS_SECONDS.usage`, not from a literal | S4 |

## A2.0 — What was read and what was measured

Read at `b4aa611b`, worktree `coai-accounts-e1`. Every file:line below was opened for this spec.

| # | Fact | Where |
|---|---|---|
| F1 | The session parser's frame finder is PRIVATE and throws that parser's own faults: `framedLines` takes the LAST `COAI-BEGIN 1` and the first `COAI-END` after it, and answers `version`, `no-sentinel` or `cut` through `fail` | `src/remoteFrame.ts:121-130`; `:98-100` `fail`; `:89-96` `Fault`; `:106-117` `readRemote` |
| F2 | Its record builder and its data rule throw the same faults, so they are not shared | `src/remoteFrame.ts:143-155`, `:158-160` |
| F3 | `REMOTE_SCRIPT` is one template literal; its last two lines are the call with `</dev/null 2>/dev/null` AFTER the command, and the end line; its doc says no test holds the hash | `src/remoteScript.ts:137`, `:428-429`, `:126-128` |
| F4 | The operand grammar and the quoting of the session reader | `src/remoteScript.ts:91-95` `callOf`, `:117-123` `shQuoted` |
| F5 | The pinned options and their comment; the session limits; the capped sentence divides by 1 048 576; the one `console.` | `src/remoteSsh.ts:28-33`, `:22`; `:75`; `:168`; `:96` |
| F6 | Two private copies of one rule, "control and format characters out" | `src/remoteRead.ts:67` `plainOf`; `src/remoteSsh.ts:216` `withoutControls` |
| F7 | The real-`sh` harness: a shell found once by an absolute path; `sh` and `bash --posix`; one test for each shell; on CI's Linux a missing `sh` FAILS; the child gets `HOME` and `PATH` and nothing else | `src/test/remoteScriptRuns.test.ts:37-39`, `:48-60`, `:65-74`, `:76-80`, `:193-195` |
| F8 | The log scan: the banned words, the scanner, the files by the name `remote*.ts`, the companions | `src/test/remoteLogScan.test.ts:16`, `:43-50`, `:71`, `:73-79`, `:81-93` |
| F9 | An owned temporary folder for a test | `src/test/ownedTemp.ts:18` |
| F10 | A test may read a file of the repository by a path from its own folder | `src/test/sonarExclusions.test.ts:24-27` |
| F11 | The extension's test job: `ubuntu-latest`; the steps *Install*, *Lint*, *Test* (`npm test`), *Package*. It installs no `jq` | `.github/workflows/ci.yml:524-527`, `:558-560`, `:572-574`, `:576-578` |
| F12 | Another job of this repository runs `jq` on `ubuntu-latest` with no install step, and says "jq is on the runner"; an `apt-get` step exists in a workflow | `.github/workflows/deploy-server.yml:98`, `:233-235`; `.github/workflows/release.yml:262` |
| F13 | The product's redactor: its patterns are private; the vendor key shapes (`sk-`, `ghp_`, `gho_`, `ghu_`, `ghs_`, `github_pat_`, `xox[baprs]-`, then 8 to 512 token characters); `safeText` is exported. The word list judges a parameter's NAME, never a value | `src/notifications.ts:174`, `:182-183`, `:236`; `src/credentialWords.ts:86` |
| F14 | Lint: 800 lines a file, `complexity` 4, 50 lines a function | `eslint.config.mjs:113-115` |
| F15 | The mod's rule for "left": 100 minus the percent used, rounded, held to 0–100; `null` for anything that is not a finite number | `.agents/conventions/mods/agent-models/hooks/limits.tsx:36-40` |
| F16 | The suite runs test FILES side by side, at most 8 | `scripts/run-tests.mjs:84` |

**Measured 2026-11-19 on this machine** (WSL2, Ubuntu 26.04.1; `dash` as `sh`, `bash` 5.3.9, **uutils coreutils 0.10.0**
for `timeout`, `tail`, `ls` and `stat`, GNU `grep` 3.12, GNU `sed` 4.9). **This machine has no `jq`**, and a `jq`
fetched for the measurement was not allowed to run. So the text of § A2.3 was run with a stand-in for `jq` (a script
that reads all its input and prints a fixed text for each of the three programs): that proves the SHELL around the
three `jq` programs, and nothing about the programs. The three programs themselves were run by `gojq`, the evaluator
inside `gh` (`gh api rate_limit --jq …`), with their input given as a literal in place of `[inputs]` and `inputs`.
`gojq` is not `jq`. **What `jq` itself does with these programs and with the flags `-n`, `-R`, `-r` and `--arg` is NOT
observed** (§ A2.14, U1); the code story's first real run on CI is where it is seen.

| # | What was done | Result |
|---|---|---|
| M-A1 | The text of § A2.3 by `dash -s` and by `bash --posix -s`, a temporary `HOME` with a false `claude` and `agy` only in `$HOME/.local/bin`, `PATH` = one folder with links to the five tools and `stat`, the stand-in `jq`, the operand `claude-usage` | The same reply from both shells, byte for byte: 24 lines, 615 bytes. Every started vendor program saw `/` as its folder and `/dev/null` as its stdin. The usage command got `TZ=UTC`; no other command did; no start of the false `claude` saw an `LC_ALL` |
| M-A2 | The same with no `jq` on the `PATH` | `COAI-JQ missing apt-get`, `COAI-VENDOR claude unreadable no-jq`, and agy still read with its version. No `claude` was started |
| M-A3 | Operands `usage`, none, and two words | `COAI-ACCOUNTS 1`, `COAI-BAD operands`, `COAI-ACCOUNTS-END`; no program was started |
| M-A4 | A false `claude` whose `--version` hangs, whose usage starts a child and hangs, and a false `agy` that hangs (`dash`); a plain usage hang (`bash --posix`); a hang of the account command (`dash`) | 14.11 s (2 + 8 + 4), 8.13 s, 3.13 s. Each reply ended with its end line. Afterwards no `sleep` of a fake was alive: this `timeout` ended the command AND its child |
| M-A5 | False credential files with a marker token (`.credentials.json`, `.claude.json`, `auth.json`, agy's sign-in file and log); both modes, both shells | The marker is in no reply, no stderr and no line the fakes logged. stderr was 0 bytes |
| M-A6 | A listing of the whole `HOME` tree (name, size, time, mode) before and after those runs | Identical |
| M-A7 | The access time of the five credential files, set to the year 2001 before a run, and of a probe file the measurement itself read | The five still say 2001; the probe says today. On this file system a read IS seen, and the script made none |
| M-A8 | The script cut after every one of its first 216 lines, and at every byte of its last two lines (71 cuts), both shells | No cut before the call line printed anything or started a program. In 30 of the 71 cuts the call line was whole and the vendor programs ran — each with `/dev/null` as its stdin. No cut started a program with a stdin that is not `/dev/null`. The only cut that printed the end line is the whole text without its last line end |
| M-A9 | M-A8's second half with the call written as in `REMOTE_SCRIPT` (the redirections AFTER the command) — by reasoning, not run | A cut between `"$@"` and ` </dev/null` would run the vendor commands with the script's own pipe as stdin and with stderr open. So this script writes the redirections FIRST (§ A2.1, 4) |
| M-A10 | The script written to the shell's stdin, which then stays open 4 s | The end line within 0.4 s under both shells (0.13 s and 0.16 s in the last run): no command waits for input |
| M-A11 | A reader that reads one line and goes away; then the same with `SIGPIPE` ignored | The shell ended within 0.5 s both times. One vendor program had been started (the account command, before the script's next `printf`); the version command, the usage command and `agy` were not started |
| M-A12 | `tail` taken out of the `PATH`; `HOME` not absolute | `COAI-BAD tool`; `COAI-BAD home` |
| M-A13 | Five session files in three day folders, with file times set by hand, one name with a space and one that is no rollout | The stand-in `jq` was fed exactly the three files changed last in the two newest day folders, oldest first; the third day folder, the fourth file and the two bad names were not read |
| M-A14 | The stand-in's usage text: the three measured lines, a `49.5%` line, a line with no reset clause, a line whose reset text holds `$(id)`, a line that starts `-=read 9 9`, and two activity lines with a marker | Three `:line` lines left; `:unmatched` counted the others that start with `Current `; no marker, no `$(`, and the false status line did nothing |
| M-J1 | The usage program in `gojq`: the measured JSON; exit 124; exit 137; exit 1; `is_error` true; `num_turns` 2; a stream with no result object; a `result` that is no string; no exit record | `=read 0 4` and every line with a `-` before it; `=timeout`; `=timeout`; `=error`; `=error`; `=read 2 1` and NO line; `=error`; `=error`; `=error` |
| M-J2 | The account program in `gojq`: the measured keys with a marker in `orgId`; `loggedIn` false with exit 1; exit 124 and no object; no object; a number as `email`, an `orgName` with a line break and a false marker line in it, a `subscriptionType` of 200 characters | `found` with three keys and no `orgId`; `signed-out`; `unreadable timeout`; `unreadable auth`; no `:email`, ONE `:org` line, `:plan` cut at 120 |
| M-J3 | The Codex program in `gojq`: the measured record after an older one, between a cut line and two transcript lines with a marker; two windows with an older record later; equal times; no record; wrong types | `:plan prolite`, the marker line, `:window 14 10080 1795518850`, `:as-of <the newest time> <file time>`; `33.46` (two decimals) and the newer record; the later line; `COAI-USAGE codex failed none`; `:window - - -` and `:as-of -` |

## A2.1 — Decisions

1. **The script reports facts; the parser decides.** On the VM, `jq` names a field and `grep` keeps a line that fits
   an anchored pattern. Whether a value is a number, in range, an email or a time is decided in TypeScript. The script
   takes three decisions of its own, because they choose what it runs or reads next: the mode, which Codex files it
   reads, and whether an account is signed in (no usage command for an account that is not).
2. **Every data line starts with `:`; every other line of the frame starts with `COAI-`.** So no byte of an email, an
   organization's name or a usage line can end a section or look like a marker. The sentinels are NOT the session
   reader's (`COAI-ACCOUNTS 1`, `COAI-ACCOUNTS-END`): the answer of one script can never be read by the other parser.
3. **The whole script is one function, `coai_accounts`, with its helpers INSIDE it.** Nothing runs before the last two
   lines are read. The end line is printed by the LAST line, outside the function: it proves that the whole text
   arrived, that the function returned, and that no command ate the rest of the script.
4. **The call is written `</dev/null 2>/dev/null coai_accounts "$@"`: the redirections first.** In `sh` they may stand
   before the command. Written after it (as `REMOTE_SCRIPT` has them, F3), a text cut between `"$@"` and the first `<`
   runs the function with the script's own pipe as stdin and with stderr open. Written first, no cut can (M-A8, M-A9).
   The session script reads files; this one starts a vendor program, so here the order matters.
5. **Every vendor command is written out in full in the text.** No helper that takes a command as words: the four
   command lines are what a reviewer reads and what A2-T6 holds word for word.
6. **Every vendor command's stdout goes into a pipe whose reader has a limit of its own.** `timeout` ends the command
   and, by its documentation, the command's process group. A child that left the group can still hold the pipe open;
   the reader's own limit (2 s later) then ends the read, so the script never waits for a program it did not start.
7. **The exit code of a vendor command travels in the JSON stream.** `sh` has no `pipefail`. After the command, the
   script prints one line, `{"coai_exit":<code>}`, into the same pipe, and `jq` reads it as a second value. So a killed
   command (124, or 137 after the KILL) is `timeout`, and the usage JSON is never held in a shell variable.
8. **The usage text never leaves a pipe before it is filtered.** `claude` → `tail -c` (1 MiB) → `jq` → `grep`. `jq`
   puts `-` before every line of the `result` text and `=` before its own status line, so a line of the text can
   never be read as the status. Only `grep`'s output is kept, and it is the status line and lines of the three forms.
9. **No `LC_ALL` for the vendor programs.** The session script exports `LC_ALL=C` for everything. Here that would
   change what `claude` is given, and what it prints was measured without it. `LC_ALL=C` stands before the script's
   own `grep`, `sed` and `ls` only, where a pattern must count bytes.
10. **The script text is ASCII.** The middle dot of the usage lines (U+00B7) is built with `printf '\302\267'`, so the
    pattern holds exactly those two bytes and no source file holds the character.
11. **A missing `jq` is its own marker, `COAI-JQ missing <manager>`, never `COAI-BAD tool`.** The five other tools are
    part of every Linux; a host without one of them is `COAI-BAD tool` and nothing is read. A host without `jq` is the
    case the owner decided about (N5): agy is still read, and Claude and Codex say `unreadable no-jq`.
12. **Both times of a Codex record are sent; the parser chooses.** The record's own `timestamp`, as it is, and the
    time of the newest file read. The parent has the script say which of the two it sent; sending both keeps the
    judgement "does this time parse" in one place.
13. **A window's slot is not sent.** The Codex lines are `:window <used> <minutes> <reset>`. The words `primary` and
    `secondary` are not in the reply, so nothing on this side can name a window by its slot (the RESULTS file, § 6:
    `primary` was the weekly window).
14. **A hash test, this time.** The spec's block, the constant and a literal in the test must be the same text; § A2.3
    has the procedure. The session script has none (its spec's decision 14); this script starts a vendor program.
15. **The shell language is POSIX; the tools are those of a Linux VM.** `timeout -k`, `tail -c`, `ls -tr`, `grep -E`
    with `{n,m}` and `stat -c %Y` are GNU, uutils or busybox forms. On a system without one of them the answer is
    `COAI-BAD tool`, `unreadable`, `failed error` or a time of `-` — never another account's numbers (§ A2.14).

## A2.2 — The operand: one of two fixed words (`src/remoteAccountsScript.ts`)

```ts
export type AccountsMode = 'claude-usage' | 'no-claude-usage';
export const ACCOUNTS_MODES: readonly AccountsMode[] = ['claude-usage', 'no-claude-usage'];
export function isAccountsMode(word: string): word is AccountsMode;   // membership in ACCOUNTS_MODES, nothing else
```

| Operand | What the script runs | Used when (S3 decides) |
|---|---|---|
| `claude-usage` | the account command, the version command and the usage command of Claude Code; the Codex read; `agy --version` | this account has no usage reading in this round, and the policy allows one |
| `no-claude-usage` | the same WITHOUT the usage command. Neither command it still runs for Claude Code writes a file (W3) | the account's usage was read on another host in this round; the host is blocked; the policy says no |

- **Checked twice.** S3's `accountsArgv` asks `isAccountsMode` and quotes the word with `shQuoted` (F4). The script
  checks again: `case $#:${1-} in 1:claude-usage|1:no-claude-usage)`. Any other count or word prints `COAI-BAD
  operands` and starts nothing (M-A3).
- **No value from a terminal's name or from a VM is ever an operand.** The two words hold no space, quote or shell
  character, so on win32 they cross to `ssh.exe` as they are.
- **The parser is told the mode that was sent**, and holds the reply to it (§ A2.7, rule 7).

## A2.3 — `ACCOUNTS_SCRIPT`, exact (`src/remoteAccountsScript.ts`)

`export const ACCOUNTS_SCRIPT: string` is the text below, byte for byte: **218 lines**, 8100 bytes, each line ends
with LF, ASCII only. This text was run for M-A1…M-A14. The coder may write the constant in any form (the text holds
`${` six times, 57 backslashes and no backtick, so a template literal needs every backslash twice and every `${`
escaped); the hash test checks the result.

Pinned sha256: PENDING — the code story writes the value here and in the test, in one commit (the procedure below).

Provenance, not the pin: when M-A1…M-A14 were run, this block hashed to
`eeb2e82975f9cc04004eae2d2d7c3a81363c3ab05268453053cd60625a13efe6`. If step 0 of the brief gets another value, the text changed
after the measurements, and the review says why.

```sh
coai_accounts() {
  unset IFS
  set -f
  nl='
'
  h='[0-9A-Fa-f]'
  h4="$h$h$h$h"
  uuid="$h4$h4-$h4-$h4-$h4-$h4$h4$h4"
  dot=$(printf '\302\267')
  window='[0-9]{1,3}(\.[0-9]{1,2})?% used '"$dot"' resets [A-Za-z0-9,: ]{1,40} \([A-Za-z0-9_/+-]{1,40}\)$'
  version='1s/^[^0-9]*\([0-9][0-9.]\{0,31\}\).*$/\1/p'
  one='def one: tostring | split("\n") | join(" ") | split("\r") | join(" ") | .[0:120];'
  jq_auth="$one"'
    def key(k): if type == "string" and . != "" then ":" + k + " " + one else empty end;
    [inputs] as $all
    | ([$all[] | select(type == "object") | select(has("coai_exit")) | .coai_exit] | .[-1]) as $rc
    | ([$all[] | select(type == "object") | select(has("loggedIn"))] | .[0]) as $a
    | if $a == null then
        (if $rc == 124 or $rc == 137 then "COAI-VENDOR claude unreadable timeout" else "COAI-VENDOR claude unreadable auth" end)
      elif $a.loggedIn != true then "COAI-VENDOR claude signed-out"
      else "COAI-VENDOR claude found",
        ($a.email | key("email")), ($a.orgName | key("org")), ($a.subscriptionType | key("plan"))
      end'
  jq_usage='[inputs] as $all
    | ([$all[] | select(type == "object") | select(has("coai_exit")) | .coai_exit] | .[-1]) as $rc
    | ([$all[] | select(type == "object") | select(.type == "result")] | .[0]) as $r
    | if $rc == 124 or $rc == 137 then "=timeout"
      elif $rc != 0 or $r == null then "=error"
      elif $r.is_error != false or ($r.result | type) != "string" or ($r.num_turns | type) != "number" then "=error"
      else ($r.result | split("\n")) as $l
        | "=read " + ($r.num_turns | floor | tostring) + " " + ([$l[] | select(startswith("Current "))] | length | tostring),
          (if $r.num_turns == 0 then $l[] | "-" + . else empty end)
      end'
  jq_codex="$one"'
    def num: if type == "number" then (((. * 100 + 0.5) | floor) / 100 | tostring) else "-" end;
    def int: if type == "number" then (floor | tostring) else "-" end;
    def win: if type == "object" then ":window " + (.used_percent | num) + " " + (.window_minutes | int) + " " + (.resets_at | int) else empty end;
    reduce (inputs | fromjson? | select(type == "object") | select(.type == "event_msg")
        | select((.payload | type) == "object") | select(.payload.type == "token_count")
        | select((.payload.rate_limits | type) == "object")) as $r
      (null; ($r.timestamp | if type == "string" then . else "" end) as $t
        | if . == null or .t <= $t then {t: $t, rl: $r.payload.rate_limits} else . end)
    | if . == null then "COAI-USAGE codex failed none"
      else (.rl.plan_type | if type == "string" and . != "" then ":plan " + one else empty end),
        "COAI-USAGE codex read",
        (.rl.primary | win), (.rl.secondary | win),
        ":as-of " + (if .t == "" then "-" else (.t | one | .[0:40]) end) + " " + $ft
      end'

  coai_manager() {
    if [ -x /usr/bin/apt-get ]; then
      printf 'apt-get'
    elif [ -x /usr/bin/dnf ]; then
      printf 'dnf'
    elif [ -x /usr/bin/yum ]; then
      printf 'yum'
    elif [ -x /sbin/apk ]; then
      printf 'apk'
    else
      printf 'none'
    fi
  }

  coai_version() {
    case $1 in
      ''|*[!0-9.]*) ;;
      *) printf ':version %s\n' "$1" ;;
    esac
  }

  coai_sign_in() {
    if [ -e "$1" ]; then
      printf ':sign-in-file present\n'
    else
      printf ':sign-in-file absent\n'
    fi
  }

  coai_claude() {
    command -v claude >/dev/null || { printf 'COAI-VENDOR claude absent\n'; return 0; }
    [ "$jq" = found ] || { printf 'COAI-VENDOR claude unreadable no-jq\n'; return 0; }
    auth=$(
      {
        timeout -k 1 3 claude auth status --json
        printf '\n{"coai_exit":%s}\n' "$?"
      } | timeout -k 1 5 tail -c 65536 | timeout -k 1 5 jq -n -r "$jq_auth"
    )
    case $auth in
      'COAI-VENDOR claude found'|'COAI-VENDOR claude found'"$nl":*) ;;
      'COAI-VENDOR claude signed-out'|'COAI-VENDOR claude unreadable timeout') printf '%s\n' "$auth"; return 0 ;;
      *) printf 'COAI-VENDOR claude unreadable auth\n'; return 0 ;;
    esac
    printf '%s\n' "$auth" || return 0
    coai_version "$(timeout -k 1 2 claude --version | LC_ALL=C timeout -k 1 4 sed -n -e "$version" -e 1q)"
    if [ "$mode" != claude-usage ]; then
      printf 'COAI-USAGE claude skipped\n'
      return 0
    fi
    kept=$(
      {
        TZ=UTC timeout -k 1 8 claude -p "/usage" --output-format json --no-session-persistence --strict-mcp-config --setting-sources ""
        printf '\n{"coai_exit":%s}\n' "$?"
      } | timeout -k 1 10 tail -c 1048576 | timeout -k 1 10 jq -n -r "$jq_usage" |
        LC_ALL=C grep -E -e '^=timeout$' -e '^=read [0-9]{1,6} [0-9]{1,6}$' -e "^-Current session: $window" -e "^-Current week \\([A-Za-z0-9][A-Za-z0-9 ._-]{0,39}\\): $window"
    )
    head=${kept%%"$nl"*}
    case $head in
      '=timeout') printf 'COAI-USAGE claude failed timeout\n'; return 0 ;;
      '=read '*) ;;
      *) printf 'COAI-USAGE claude failed error\n'; return 0 ;;
    esac
    set -- $head
    sent=$(printf '%s\n' "$kept" | LC_ALL=C grep -c '^-')
    [ "$sent" -le 8 ] || sent=8
    printf 'COAI-USAGE claude read\n' || return 0
    printf ':turns %s\n' "$2"
    printf '%s\n' "$kept" | LC_ALL=C sed -n -e 's/^-/:line /p' | LC_ALL=C sed -n -e '1,8p'
    printf ':unmatched %s\n' "$(($3 - sent))"
    return 0
  }

  coai_codex() {
    if ! command -v codex >/dev/null && [ ! -d "$HOME/.codex" ]; then
      printf 'COAI-VENDOR codex absent\n'
      return 0
    fi
    [ "$jq" = found ] || { printf 'COAI-VENDOR codex unreadable no-jq\n'; return 0; }
    printf 'COAI-VENDOR codex found\n' || return 0
    coai_sign_in "$HOME/.codex/auth.json"
    set +f
    set -- "$HOME"/.codex/sessions/[0-9][0-9][0-9][0-9]/[0-9][0-9]/[0-9][0-9]/
    set -f
    while [ "$#" -gt 2 ]; do
      shift
    done
    for day do
      shift
      [ -d "$day" ] || continue
      set +f
      set -- "$@" "$day"rollout-*.jsonl
      set -f
    done
    for one do
      shift
      name=${one##*/}
      case $name in
        *[!A-Za-z0-9._-]*) continue ;;
        rollout-*-$uuid.jsonl) ;;
        *) continue ;;
      esac
      [ -f "$one" ] || continue
      set -- "$@" "$one"
    done
    while [ "$#" -gt 64 ]; do
      shift
    done
    if [ "$#" -eq 0 ]; then
      printf 'COAI-USAGE codex failed no-sessions\n'
      return 0
    fi
    three=$(LC_ALL=C ls -tr -- "$@" | tail -n 3)
    ftime=$(stat -c %Y -- "${three##*"$nl"}")
    case $ftime in
      ''|*[!0-9]*) ftime=- ;;
    esac
    record=$(
      printf '%s\n' "$three" | while IFS= read -r one; do
        [ -f "$one" ] || continue
        tail -c 1048576 <"$one"
        printf '\n'
      done | timeout -k 1 10 jq -n -R -r --arg ft "$ftime" "$jq_codex"
    )
    case $record in
      'COAI-USAGE codex failed none'|'COAI-USAGE codex read'"$nl":*|:plan\ *"$nl"'COAI-USAGE codex read'"$nl":*) printf '%s\n' "$record" ;;
      *) printf 'COAI-USAGE codex failed error\n' ;;
    esac
    return 0
  }

  coai_agy() {
    command -v agy >/dev/null || { printf 'COAI-VENDOR agy absent\n'; return 0; }
    printf 'COAI-VENDOR agy found\n' || return 0
    coai_version "$(timeout -k 1 4 agy --version | LC_ALL=C timeout -k 1 6 sed -n -e "$version" -e 1q)"
    coai_sign_in "$HOME/.gemini/antigravity-cli/antigravity-oauth-token"
    printf 'COAI-USAGE agy none\n'
    return 0
  }

  printf 'COAI-ACCOUNTS 1\n' || return 0
  case $#:${1-} in
    1:claude-usage|1:no-claude-usage) ;;
    *) printf 'COAI-BAD operands\n'; return 0 ;;
  esac
  mode=$1
  case ${HOME-} in
    /*) ;;
    *) printf 'COAI-BAD home\n'; return 0 ;;
  esac
  cd / || { printf 'COAI-BAD folder\n'; return 0; }
  PATH="$HOME/.local/bin${PATH:+:$PATH}"
  export PATH
  for tool in timeout tail ls sed grep; do
    command -v "$tool" >/dev/null || { printf 'COAI-BAD tool\n'; return 0; }
  done
  if command -v jq >/dev/null; then
    jq=found
    printf 'COAI-JQ found\n' || return 0
  else
    jq=missing
    printf 'COAI-JQ missing %s\n' "$(coai_manager)" || return 0
  fi
  coai_claude
  coai_codex
  coai_agy
  return 0
}
</dev/null 2>/dev/null coai_accounts "$@"
printf 'COAI-ACCOUNTS-END\n'
```

**The pin — the procedure and the test (A2-T2).**

1. *Step 0, no commit.* Take the block out of this file and hash it. From the repository root:

   ```bash
   node -e 'const fs=require("fs"),c=require("crypto");const t=fs.readFileSync(process.argv[1],"utf8");const a=t.indexOf("```sh\n",t.indexOf("\n## A2.3"))+6;const s=t.slice(a,t.indexOf("\n```",a)+1);console.log(c.createHash("sha256").update(s).digest("hex"),s.split("\n").length-1,Buffer.byteLength(s))' todo/PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1_S2_spec.md
   ```

   It prints the hash, `218` and `8100`. The hash must be the provenance value above.
2. Write the constant. After `npm run compile`, print the hash of the COMPILED constant:

   ```bash
   node -e 'const {ACCOUNTS_SCRIPT:s}=require("./out/remoteAccountsScript.js");console.log(require("crypto").createHash("sha256").update(s).digest("hex"))'
   ```

   It must be the value of step 0. Put both commands and their output in the summary.
3. In ONE commit: the line above becomes ``Pinned sha256: `<64 hex>` ``, and the test file gets the same value as a
   literal, `PINNED`.
4. **The test** (`remoteAccountsScript.test.ts`, A2-T2) holds three things to one another: (a) the sha256 of
   `ACCOUNTS_SCRIPT` is `PINNED`; (b) this spec — read from `todo/`, or from `research/` once it is promoted; found in
   both or in neither is a failure — has a line `Pinned sha256:` with that value; (c) the first `sh` block after the
   heading that starts `## A2.3` IS `ACCOUNTS_SCRIPT`, compared as text, and the failure names the first line that
   differs. So an edit of the constant, of the spec's block or of either hash, without the others, is red. The path is
   built as `sonarExclusions.test.ts:24-27` builds its own (F10).
5. A later change of the text is a change of this spec: the block, the pin and the literal move in one commit, and
   the commit says what changed and why. A change of WHAT LEAVES THE VM (a field, a pattern, a file) goes to the
   caller first; the coder never makes one alone.

**The rules the text keeps** (each has a test in § A2.11):

1. **One function, read whole, then called.** Line 1 is `coai_accounts() {`. The second-last line is
   `</dev/null 2>/dev/null coai_accounts "$@"`. The last line is `printf 'COAI-ACCOUNTS-END\n'`. Every other line is
   inside the function.
2. **No command reads the script.** Every command inside has `/dev/null` as stdin, or a pipe, or `<"$one"` (a Codex
   session file).
3. **No write and nothing left**: every `>` in the text is `>/dev/null`; no `<<`, no `&` but `&&`, no backtick, no
   `eval`, no `trap`, no line that starts with `~`. One `cd`, to `/`. `PATH` is the only variable it exports; `TZ` is
   set for one command.
4. **`set -f` for the whole function.** The two globs (the day folders, the rollout files of a day) run between
   `set +f` and `set -f`, and every match is tested (`-d`, `-f`, the name) before it is used.
5. **The `PATH`**: `$HOME/.local/bin` in front, as the measured VM's `~/.profile` does for a login shell. Nothing else
   of a profile is read. `HOME` must start with `/`, else `COAI-BAD home`. With an empty `PATH` no empty part is
   added (an empty part would mean the current folder).
6. **A tool that is not there is said, never read as "nothing found"**: `timeout tail ls sed grep` are checked first
   (`COAI-BAD tool`); `jq` by its own marker; `stat` is optional (a time of `-`).
7. **The mode decides one thing**: whether the usage command runs. Nothing else differs between the two replies.
8. **`jq` is the only reader of a vendor's JSON and of a Codex session file**, and each of its three programs prints
   lines it builds from named fields. No program prints its input, a whole object or a field it does not name.
9. **A closed stdout ends the script at its next step**: the first `printf` of each step is `… || return 0` (7
   places), so the usage command and `agy` are not started after the reader is gone (M-A11).
10. **A file's name is never an argument a tool could read as an option**: the session files are opened by `<"$one"`;
    the two commands that take a path take it after `--` (`ls -tr --`, `stat -c %Y --`), and every path starts with `/`.

**The commands, each with its limits.** Every command runs from `/`, with `/dev/null` as stdin and as stderr (from
the ONE call line), under `timeout -k 1 <n>`: a TERM after `n` seconds, a KILL one second later.

| # | The command, word for word | Limit | Its output goes to | Its reader's limit | Runs |
|---|---|---|---|---|---|
| 1 | `claude auth status --json` | 3 s | `tail -c 65536`, then `jq` (the account program) | 5 s each | when `claude` is found and `jq` is there |
| 2 | `claude --version` | 2 s | `sed`: of the FIRST line, the first run of digits and dots, at most 32 bytes | 4 s | when the account is signed in |
| 3 | `claude -p "/usage" --output-format json --no-session-persistence --strict-mcp-config --setting-sources ""`, with `TZ=UTC` for this one command | 8 s | `tail -c 1048576`, then `jq` (the usage program), then `grep` (the status line and the three forms) | 10 s each | when the account is signed in AND the mode is `claude-usage` |
| 4 | `agy --version` | 4 s | `sed`, as for command 2 | 6 s | when `agy` is found |
| — | no `codex` command, ever | — | — | — | — |

The slowest run that still answers: 3 + 2 + 8 + 4 = 17 s, or 21 s when every program needs its KILL; with ssh's 5 s
to connect that is under the 30 s ceiling (parent § 2). When a program leaves a child that holds its pipe, the
readers' limits apply instead (5 + 4 + 10 + 6 = 25 s); the runner's 30 s kill is the last bound, and the host then
says it did not answer.

**The Codex read.** No program of Codex is started. The script takes the day folders
`~/.codex/sessions/<4 digits>/<2 digits>/<2 digits>/`, keeps the LAST TWO in name order (the two newest days), and of
their files keeps the names that fit `rollout-*-<uuid>.jsonl` over the alphabet `A-Za-z0-9._-` (the last 64 by name,
if a day holds more). Of those it takes the three changed last (`ls -tr … | tail -n 3`, oldest first), reads the last
1 MiB (1 048 576 bytes) of each with `tail -c`, and gives the lines to `jq` as raw text (`-R`). `jq` parses each line
by itself (`fromjson?`: a cut first line, or any line that is not JSON, gives nothing), keeps the objects whose `type`
is `event_msg`, whose `payload.type` is `token_count` and whose `payload.rate_limits` is an object, and of those
takes the one with the greatest `timestamp` text (the later line on a tie, so the newer file wins). Of that ONE
record it prints `plan_type`, and of `primary` and of `secondary` the three numbers `used_percent` (two decimals at
most), `window_minutes` and `resets_at`. A day folder, a file or a record that is not there is said (`failed
no-sessions`, `failed none`), and a `jq` that fails or is killed is `failed error`.

**The bounds.**

| What | Bound | In the script | The TypeScript constant it must equal (A2-T26 holds each) |
|---|---|---|---|
| the account command, the version, the usage, agy | 3 s, 2 s, 8 s, 4 s | `timeout -k 1 3`, `2`, `8`, `4` | `ACCOUNTS_SECONDS` = `{ account: 3, version: 2, usage: 8, agy: 4 }` |
| the account JSON given to `jq` | 64 KiB | `tail -c 65536` | `ACCOUNT_JSON_MAX` |
| the usage JSON given to `jq` | 1 MiB; a longer output is cut at its start, does not parse, and is `failed error` | `tail -c 1048576` (in the usage pipe) | `USAGE_JSON_MAX` |
| the usage lines sent | 8 | `1,8p`; `-le 8`, `sent=8` | `USAGE_LINES_MAX` |
| a Codex session file | the last 1 MiB | `tail -c 1048576` (in the `while` loop) | `CODEX_TAIL_MAX` |
| the Codex files read | 3, of at most 64 names, in 2 day folders | `tail -n 3`; `-gt 64`; `-gt 2` | `CODEX_FILES_MAX` |
| a text value (`email`, `org`, `plan`) | 120 characters, one line | `.[0:120]` in `one` | — |
| a version | 32 bytes of digits and dots | `\{0,31\}` | — |
| a usage line | at most 169 bytes as a `:line`, by its pattern (a model of 40, a percent of 6, a reset text of 40, a zone of 40, and the fixed words) | the two `-e` patterns of the forms | — |
| the reply | at most 28 lines; the longest line is `:email ` and 120 characters (487 bytes when each is 4 bytes) | — | under `ACCOUNTS_LINES_MAX` = 64 and `ACCOUNTS_LINE_BYTES_MAX` = 512; the parser refuses more |

## A2.4 — What leaves the VM

Everything the script can print, whole. A value is printed because a `jq` program names its field, because a `grep`
pattern keeps its line, or because the script made it from a test of its own.

| Line | From | What it can hold |
|---|---|---|
| `COAI-ACCOUNTS 1`, `COAI-ACCOUNTS-END`, `COAI-BAD <word>`, every `COAI-VENDOR …` and `COAI-USAGE …` | the script's own words | closed words only |
| `COAI-JQ missing <word>` | which of four fixed paths can be run: `/usr/bin/apt-get`, `/usr/bin/dnf`, `/usr/bin/yum`, `/sbin/apk`, in this order | one of five words |
| `:email`, `:org`, `:plan` (Claude) | `email`, `orgName`, `subscriptionType` of `claude auth status --json`, each only when it is a string that is not empty | one line, 120 characters. NOT sent: `orgId`, `authMethod`, `loginExpiresAt` and the seven other keys; `loggedIn` is read and not sent (the RESULTS file, § 3: 14 keys) |
| `:version` (Claude, agy) | the first line of `--version` | digits and dots, at most 32 |
| `:turns` | `num_turns` of the usage JSON — **it is sent back**, so a model call is seen and not assumed | digits |
| `:line` | the `result` text of the usage JSON, only when `num_turns` is 0 | at most 8 lines, each one of the three anchored forms below |
| `:unmatched` | a count | how many lines that start with `Current ` were not sent — never their text |
| `:sign-in-file` | `[ -e <path> ]` | `present` or `absent` |
| `:plan` (Codex), `:window` | the ONE newest `token_count` record: `plan_type`, and three numbers of each window | one line of 120 characters; numbers or `-` |
| `:as-of` | that record's `timestamp` (40 characters), and the file time of the newest file read | a text and digits |

**The three anchored forms, in two `grep` patterns** (`grep -E` under `LC_ALL=C`, each line with the `-` that `jq` put before it; `·` is the
two bytes `0xC2 0xB7`):

```
^-Current session: <tail>
^-Current week \([A-Za-z0-9][A-Za-z0-9 ._-]{0,39}\): <tail>            (it also takes "all models")
<tail> = [0-9]{1,3}(\.[0-9]{1,2})?% used · resets [A-Za-z0-9,: ]{1,40} \([A-Za-z0-9_/+-]{1,40}\)$
```

The parent names three patterns; `Current week (all models)` and `Current week (<model>)` are one `grep` pattern
here, and the parser tells them apart by the exact words. Everything else of the `result` text is dropped ON THE VM —
first of all the section *"What's contributing to your usage?"*.

**What never leaves, and why it cannot:** the Claude credential file and `~/.claude.json` (no line of the script
names them); `~/.codex/auth.json` and agy's sign-in file (one `-e` test each; their bytes are never read); agy's log
(not named); a Codex transcript line (`jq` prints six numbers, a plan type and a time of ONE record, and no other
field); the person's activity (the `grep` forms); a banner or an update notice of a version command (the `sed` form);
stderr of anything (`2>/dev/null` on the one call line).

## A2.5 — The reply: the frame, the markers, the keys, the caps

**The frame.** stdout is split at `\n`. The frame starts at the LAST line that is exactly `COAI-ACCOUNTS 1` and ends at
the FIRST line after it that is exactly `COAI-ACCOUNTS-END`. Lines before the start and after the end are ignored.
Inside: **at most 64 lines, each at most 512 bytes** (UTF-8). A line that starts with `COAI-` is a marker, its words
split at single spaces. A line that starts with `:` is data of the marker before it: the key is the text up to the
first space, the value is the rest (it may be empty). Any other line makes the answer `unreadable`.

**The markers and their words — a closed vocabulary.** `·` separates alternatives.

| Marker | Words | Data lines | Where |
|---|---|---|---|
| `COAI-BAD` | `operands` · `home` · `folder` · `tool` | none | the ONLY record of its frame |
| `COAI-JQ` | `found` · `missing` + one of `apt-get` · `dnf` · `yum` · `apk` · `none` | none | first, exactly once |
| `COAI-VENDOR` | `claude` + `found` · `absent` · `signed-out` · `unreadable` + (`no-jq` · `auth` · `timeout`) | when `found`: `version`, `email`, `org`, `plan`, each at most once | second |
| `COAI-USAGE` | `claude` + `read` · `skipped` · `failed` + (`timeout` · `error`) | when `read`: `turns` (once), `line` (0 to 8), `unmatched` (once) | right after `COAI-VENDOR claude found`, and only then |
| `COAI-VENDOR` | `codex` + `found` · `absent` · `unreadable no-jq` | when `found`: `sign-in-file` (once), `plan` (at most once) | after the Claude part |
| `COAI-USAGE` | `codex` + `read` · `failed` + (`no-sessions` · `none` · `error`) | when `read`: `window` (0 to 2), `as-of` (once) | right after `COAI-VENDOR codex found`, and only then |
| `COAI-VENDOR` | `agy` + `found` · `absent` | when `found`: `version` (at most once), `sign-in-file` (once) | after the Codex part |
| `COAI-USAGE` | `agy none` | none | right after `COAI-VENDOR agy found`, and only then |

**The keys and their values.**

| Key | Value | Closed? |
|---|---|---|
| `version` | text | no — the parser's version rule |
| `email`, `org`, `plan` | text | no — the parser's text rules |
| `turns`, `unmatched` | 1 to 6 digits | yes |
| `line` | one usage line | no — the parser's line rule; a line that fits no form counts as unmatched |
| `sign-in-file` | `present` · `absent` | yes |
| `window` | exactly three words: used, minutes, reset — each digits (used may have a dot) or `-` | the count of words, yes |
| `as-of` | at least two words: the LAST is the file's time in epoch seconds or `-`; the words before it are the record's time, as the record has it, or `-` | at least two words, yes |

The parent's sketch of the reply, with these words (the Claude account of the measured host, made anonymous):

```
COAI-ACCOUNTS 1
COAI-JQ found
COAI-VENDOR claude found
:email someone@example.com
:org Example Org
:plan team
:version 2.1.326
COAI-USAGE claude read
:turns 0
:line Current session: 56% used · resets Nov 19, 3pm (UTC)
:line Current week (all models): 77% used · resets Nov 22, 3am (UTC)
:line Current week (Fable): 49% used · resets Nov 22, 2:59am (UTC)
:unmatched 0
COAI-VENDOR codex found
:sign-in-file present
:plan prolite
COAI-USAGE codex read
:window 14 10080 1795518850
:as-of 2026-11-17T19:32:36.474Z 1794938000
COAI-VENDOR agy found
:version 1.5.12
:sign-in-file present
COAI-USAGE agy none
COAI-ACCOUNTS-END
```

**Two differences from the parent's sketch, both in the order only.** `:version` comes after the account's keys (the
version command runs after the account command, so that a signed-out host starts one program, not two); the keys of a
marker may come in any order. The Codex `:plan` is printed under `COAI-VENDOR codex`, as in the sketch, and comes from
the usage record, so it is there only when a record was found.

## A2.6 — The frame finder, shared (K11) (`src/remoteFraming.ts`, pure)

```ts
/** The three lines that make a frame: the exact begin line, what every begin line of this family starts with, the exact end line. */
export interface Sentinels { readonly begin: string; readonly family: string; readonly end: string }

/** What a stdout holds of a frame. It names no parser's fault: each parser says its own words for the two misses. */
export type Framed =
  | { readonly kind: 'framed'; readonly lines: readonly string[] }
  | { readonly kind: 'no-begin'; readonly otherVersion: boolean }   // otherVersion: a line starts with `family`
  | { readonly kind: 'no-end' };

/** The lines between the LAST begin line and the first end line after it. */
export function framed(stdout: string, sentinels: Sentinels): Framed;
```

- **The decision (K11): one new module of about 30 lines, with a result that is a value and not a throw.** It is the
  body of `framedLines` (F1), moved. It lives in neither parser's file, so the accounts parser does not import the
  session parser (which pulls in the chat modules), and the name `remote*.ts` puts it under the log scan (F8).
- **`src/remoteFrame.ts` keeps its private `framedLines(stdout)`**, with the same signature, as an adapter: it calls
  `framed(stdout, { begin: 'COAI-BEGIN 1', family: 'COAI-BEGIN ', end: 'COAI-END' })` (the begin line built from
  `REMOTE_PROTOCOL`, as today) and maps `no-begin` to `fail(otherVersion ? 'version' : 'no-sentinel')` and `no-end` to
  `fail('cut')`. Nothing else in that file changes. **No test of `remoteFrame.test.ts` or `remoteScriptRuns.test.ts`
  is edited**; both stay green.
- The accounts parser calls `framed(stdout, { begin: 'COAI-ACCOUNTS 1', family: 'COAI-ACCOUNTS ', end:
  'COAI-ACCOUNTS-END' })`. The end line does not start with the family (`COAI-ACCOUNTS-` is not `COAI-ACCOUNTS `), so a
  whole reply is never taken for another version.
- After this story `lastIndexOf(` is in `src/remoteFraming.ts` and not in `src/remoteFrame.ts`: A2-T24 holds that, so
  the extraction cannot be a copy.

## A2.7 — `readAccounts`, the typed result and the value rules (`src/remoteAccountsReply.ts`, `src/remoteAccountsUsage.ts`, pure)

```ts
export type ManagerWord = 'apt-get' | 'dnf' | 'yum' | 'apk' | 'none';
export const MANAGER_WORDS: readonly ManagerWord[];

/** Why a whole answer cannot be used. A closed list; `ACCOUNTS_UNREADABLE_SAID` has one clause for each. */
export type AccountsUnreadable =
  | 'exit' | 'no-sentinel' | 'version' | 'cut' | 'too-many' | 'too-long'
  | 'line' | 'marker' | 'key' | 'value' | 'shape'
  | 'operands' | 'home' | 'folder' | 'tool';

export interface ClaudeWindow {
  readonly name: '5h' | 'week' | { readonly model: string };
  readonly usedPercent: number | null;   // as the VM states it: USED. Never 0 for "not there"
  readonly resetsAtMs: number | null;
}
export type ClaudeUsage =
  | { readonly state: 'read'; readonly windows: readonly ClaudeWindow[]; readonly unmatched: number }
  | { readonly state: 'skipped' }
  | { readonly state: 'failed'; readonly why: 'timeout' | 'error' | 'format' | 'model-call' };
export type ClaudeSide =
  | { readonly state: 'absent' }
  | { readonly state: 'signed-out' }
  | { readonly state: 'unreadable'; readonly why: 'no-jq' | 'auth' | 'timeout' | 'credential-shape' }
  | { readonly state: 'found'; readonly version: string | null; readonly email: string;   // '' = the account is not known
      readonly org: string | null; readonly plan: string | null; readonly usage: ClaudeUsage };

export interface CodexWindow { readonly minutes: number; readonly usedPercent: number | null; readonly resetsAtMs: number | null }
export type CodexUsage =
  | { readonly state: 'read'; readonly windows: readonly CodexWindow[];
      readonly asOf: { readonly ms: number; readonly from: 'record' | 'file' } | null }
  | { readonly state: 'failed'; readonly why: 'no-sessions' | 'none' | 'error' };
export type CodexSide =
  | { readonly state: 'absent' }
  | { readonly state: 'unreadable'; readonly why: 'no-jq' | 'credential-shape' }
  | { readonly state: 'found'; readonly signInFile: boolean; readonly plan: string | null; readonly usage: CodexUsage };

export type AgySide =
  | { readonly state: 'absent' }
  | { readonly state: 'unreadable'; readonly why: 'credential-shape' }
  | { readonly state: 'found'; readonly version: string | null; readonly signInFile: boolean;
      readonly usage: { readonly state: 'no-source' } };

export type AccountsRead =
  | { readonly kind: 'unreadable'; readonly why: AccountsUnreadable }
  | { readonly kind: 'read'; readonly claude: ClaudeSide; readonly codex: CodexSide; readonly agy: AgySide;
      readonly jqMissing: ManagerWord | undefined };

export function readAccounts(run: { readonly code: number; readonly stdout: string }, mode: AccountsMode, nowMs: number): AccountsRead;
export function plainValue(text: string): string;          // THE road by which VM text becomes a value (X7)
export function credentialShaped(value: string): boolean;
export const ACCOUNTS_UNREADABLE_SAID: Readonly<Record<AccountsUnreadable, string>>;
```

`readAccounts` never throws, and no arm holds an error's message or a piece of the reply that the rules below did not
pass. It has no `console`, no `notify`, no file call, no `eval`, no `new Function`, and builds no path.

**The decision, in this order.** The first rule that fails names the fault.

| # | Rule | Fault |
|---|---|---|
| 1 | `run.code` is 0 | `exit` |
| 2 | `framed(stdout, …)` (§ A2.6): no begin line — another `COAI-ACCOUNTS <n>` → `version`, none → `no-sentinel`; no end line after it → `cut` | as said |
| 3 | The frame has at most `ACCOUNTS_LINES_MAX` (64) lines; every line is at most `ACCOUNTS_LINE_BYTES_MAX` (512) bytes (`Buffer.byteLength`) | `too-many`; `too-long` |
| 4 | Every line is a marker (`COAI-…`) or data (`:…`) after a marker | `line` |
| 5 | Every marker is in the table of § A2.5 with words from its closed list. Every key is one its marker takes, no more often than the table says | `marker`; `key` |
| 6 | A `COAI-BAD <word>` is the only record → the answer is `unreadable` with that word. With any other record beside it → `shape` | the word; `shape` |
| 7 | The arrangement: `COAI-JQ`; then for `claude`, `codex`, `agy` in this order one `COAI-VENDOR`, followed by its `COAI-USAGE` exactly when its state is `found`; nothing else. The keys that must be there are there. `COAI-JQ missing` goes only with `absent` or `unreadable no-jq` for Claude and for Codex, and `COAI-JQ found` never with `no-jq`. For the mode `no-claude-usage` a found Claude has `skipped`; for `claude-usage` it never has | `shape` |
| 8 | The values of the closed keys are in their form (§ A2.5) | `value` |
| 9 | **Every value passes `plainValue`, then `credentialShaped`.** A value that looks like a credential is dropped, with every other value of that vendor: the vendor is `unreadable` with `credential-shape`. The two other vendors stand | (the vendor's state) |
| 10 | Each value becomes its typed field by the value rules below. A value that fails its rule is `null` — or `''` for the email — and never 0 | — |

**`plainValue(text)`** — one function, three steps, in this order: every character of the classes `Cc` and `Cf` is
taken out (control characters, bidi marks, a zero-width space — the rule of `plainOf` and `withoutControls`, F6);
every `$(` is written `$ (`, so nothing a VM sent can be drawn as an icon by a list; the text is cut at 200
characters. It is idempotent.

**`credentialShaped(value)`** is true when ONE of these holds. It is a net, not the measure: the measure is that the
script has no line that opens a credential file (§ A2.9, J2).

1. The value holds a run of 40 or more characters of `A-Za-z0-9_-`. (The parent's rule, read at its widest: a run
   anywhere, so the segments of a JWT and a token inside other text are caught. An email address or a name with such a
   run is caught too; that is the safe direction, and § A2.14 names it.)
2. The product's own vendor key shapes match: the pattern of `src/notifications.ts:182-183`, which gets a name and an
   `export` (`VENDOR_KEY_SHAPE`) with its text unchanged, so there is ONE list of key prefixes in this extension.
   **The exported pattern has the flag `g`, and a `g` pattern keeps state between two `test` calls: this module
   builds its own from `VENDOR_KEY_SHAPE.source`, once, and never calls `test` on the shared object.** `safeText`
   itself is NOT the test: its other patterns take `Basic <word>` and `token <word>` for a header, and an
   organization called *Basic Industries* would make its account unreadable.
3. One of three shapes of the vendors this script stands beside: `eyJ` and 16 or more token characters (a JWT, as
   in Codex's `auth.json`), `ya29.` and 16 or more (a Google access token), or `-----BEGIN`.

**The value rules** (`src/remoteAccountsUsage.ts` for the usage lines, the reset and the Codex record):

| Value | Rule | When it fails |
|---|---|---|
| a percent (`:line`, `:window`) | `^\d{1,3}(\.\d{1,2})?$` and at most 100 | `null` — so `101`, `-1`, `1e3`, `NaN` and an empty text are all "not known" |
| a version | two to four numbers of 1 to 5 digits with dots between | `null` ("version not known"); the vendor is still found |
| the email | one `@`, no space, at most 254 characters | `''`: the account is not known, and S4 never groups it |
| `org` | not empty after `trim` | `null` |
| `plan` (Claude, Codex) | `^[A-Za-z0-9][A-Za-z0-9 ._-]{0,39}$` | `null` |
| `turns` | digits; **not 0 → the usage is `failed` `model-call`**, whatever lines came with it | — |
| a Codex window | `minutes`: digits, 1 to 527 040 (366 days) — else the line is no window and is left out. `used`: the percent rule. `reset`: 1 to 11 digits, epoch seconds → `resetsAtMs` | `usedPercent` `null`; `resetsAtMs` `null` |
| `as-of` | the record's time (every word but the last) when it fits `^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(\.\d{1,9})?(Z\|[+-]\d\d:\d\d)$`, `Date.parse` gives a number, and it is between 2020-01-01 and one day after `nowMs` → `from: 'record'`. Else the file's time (the last word), 1 to 11 digits in the same range → `from: 'file'` | `asOf` `null` |

**A Claude usage line** (`:line`) is read by ONE pattern, the twin of the script's:

```
^Current (session|week \(([A-Za-z0-9][A-Za-z0-9 ._-]{0,39})\)): (\d{1,3}(?:\.\d{1,2})?)% used \u00b7 resets ([A-Za-z0-9,: ]{1,40}) \(([A-Za-z0-9_/+-]{1,40})\)$
```

- `Current session` is the window `5h`. `Current week (all models)` is `week`. `Current week (<other words>)` is `{
  model: <those words> }`.
- A `:line` that does not fit, and a second line for a window that is already there, are not read: each adds one to
  `unmatched` (on top of the script's own `:unmatched`).
- **No window at all** (the marker says `read`, `turns` is 0) is `failed` `format`.

**The reset text** — *"resets Nov 19, 3pm (UTC)"*, *"resets Nov 22, 2:59am (UTC)"* — has no year.

1. The zone must be exactly `UTC`. `TZ=UTC` is a request to Claude Code; its effect on a VM in another zone was not
   observed. So any other zone is a dash.
2. The text must fit `^(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec) ([1-9]|[12]\d|3[01]), (1[0-2]|[1-9])(?::([0-5]\d))?(am|pm)$`.
   `12am` is hour 0; `12pm` is hour 12; `3pm` is 15; no minutes is `:00`.
3. The moment is `Date.UTC(year, month, day, hour, minute)` in the UTC year of `nowMs`. A day the month does not have
   (the built date does not give back the same month and day) is no moment.
4. If that moment is more than one hour before `nowMs`, the NEXT year is tried, by the same step.
5. The result counts only when it is between one hour before `nowMs` and eight days after it.
6. Anything else — another zone, no zone, a text that does not fit, a moment outside that range — is `resetsAtMs:
   null`: the list draws `resets in —` and **the percent is still shown**.

**"Left" is 100 minus "used", and it is made in ONE place, which is not this story's.** The VM states percents USED
(`56% used`; Codex `used_percent`). The script sends them as they are. The typed result holds `usedPercent`, as sent,
or `null`. The list makes "left" with the mod's rule (F15): `Math.max(0, Math.min(100, Math.round(100 − used)))`, and
`null` stays `null` and is drawn as a dash — S4's `percentLeft` in `usageWords.ts`, held to the mod's cases (A4-T1).
No function of S2 subtracts from 100, so the two views cannot disagree about rounding, and a missing number has no
path on which it could become 0 or 100. A2-T17 holds that.

**A Codex window is named by its length.** The typed window has `minutes` and no name. `windowLabel` (S4) says `5h`
for 300 and `week` for 10 080, another length in hours. The slot (`primary`, `secondary`) is not in the reply (§ A2.1,
13). Two windows of the same length: the first is kept. No window left: `failed` `none`.

**`ACCOUNTS_UNREADABLE_SAID`** — one short clause for each fault, all different, none with data from the answer, for
ONE sentence of the list (*"The AI accounts on “h” could not be read: <clause>."*):

| `why` | Clause |
|---|---|
| `exit` | the remote command ended with an error |
| `no-sentinel` | the answer did not start as expected |
| `version` | the answer is in a form this version does not know |
| `cut` | the answer stopped before its end |
| `too-many` | the answer has more lines than a reply may have |
| `too-long` | the answer has a line longer than a reply may have |
| `line` | the answer holds a line that is neither a marker nor data |
| `marker` | the answer holds a marker this version does not know |
| `key` | the answer holds a key this version does not know |
| `value` | the answer holds a value that is not in the form of its key |
| `shape` | the parts of the answer do not fit together |
| `operands` | the remote script refused what it was asked |
| `home` | the remote account has no home folder |
| `folder` | the remote script could not go to the folder / |
| `tool` | a tool the read needs (timeout, tail, ls, sed or grep) is not on the PATH of an ssh command there |

**Sentences S4 owes for the new `why` words** (proposed; S4's brief is the stories document, X3–X5): Claude
`unreadable auth` — *the account command of Claude Code printed nothing this version understands*; `unreadable
timeout` — *the account command of Claude Code did not answer within 3 s*; usage `failed error` — *usage not read:
the usage command of Claude Code <version> ended with an error*; Codex `failed no-sessions` — *no Codex session file
is on this host*; `failed none` — *no usage record is near the end of the newest Codex sessions* (the parent's
words); `failed error` — *the Codex session files could not be read*; any vendor `unreadable credential-shape` — *not
shown: a value in the answer looked like a credential*.

## A2.8 — W6 and W7: what the code does until E2 has run

**W7 — "starts no hook, no MCP server and no plugin of the person" is NOT claimed.** Step 0 concluded it from the
flags; W2 saw no child in one sample and one `node` process of unknown origin afterwards. Neither proves it. The
`execve` trace of every program start runs in E2 (parent § 5), on the owner's hosts. Until it has run:

| What | What the code does | What the words say |
|---|---|---|
| The three flags (`--no-session-persistence`, `--strict-mcp-config`, `--setting-sources ""`) | They are passed, word for word, and A2-T6 holds that they are passed | The doc comment of `ACCOUNTS_SCRIPT`, the module documents and `SECURITY.md` say **"we pass"**. No sentence says "it starts nothing" or "no hook runs" — a flag is a request until its effect was seen (`security.md`, *A measure you have not observed working is a comment*) |
| A child that the usage command starts and that stays in its process group | `timeout -k 1 8` ends it with a read that is KILLED at its limit: seen here with a false `claude` and uutils `timeout` 0.10.0 (M-A4); A2-T8 holds it on CI's GNU `timeout` | "A child that stays in the group is ended with a read that ran out of time" |
| A child of a read that ENDS IN TIME, and a child that left the group | Nothing. The script does not look for it and cannot end it | Named as not covered, in § A2.14 and in `SECURITY.md` |
| The one switch | The usage command runs ONLY for the operand `claude-usage`. The round's planner (S3) reads ONE constant, `USAGE_POLICY`, to choose the operand. E1 ships `{ kind: 'every-round' }`, for the pre-release build that runs on the owner's hosts only | — |
| **If W7 fails** | The release build is cut with `USAGE_POLICY = { kind: 'off' }` (X8): every host gets `no-claude-usage`, Claude Code is started for its account and its version only, and the list shows the account with `usage` `skipped`. No line of the script, the reply or the parser changes | the parent's stated consequence, "until the owner decides" |

**W6 — the growth gate's effect on the code is that same constant, and nothing in S2.** A usage read may leave a
backup of `~/.claude.json` behind each time (parent § *Growth surfaces*). Which row of the parent's § 2 table W6
selects becomes the release build's `USAGE_POLICY`: `every-round`, `hourly` (at most once an hour for an account, under
a budget the owner approves) or `on-press`. What S2 guarantees for every one of those rows:

- the operand `no-claude-usage` starts neither the usage command nor any other program that was seen to write (W3:
  the account and version commands wrote no file) — A2-T5 and A2-T6 hold that the false `claude` is never started
  with `-p` in that mode;
- the script itself writes nothing in either mode (A2-T13), so the only growth on a VM is Claude Code's own;
- the parser is told the mode: a reply to `no-claude-usage` that holds a usage reading is `unreadable` (`shape`), so
  a VM cannot make the list show a usage the policy did not ask for.

## A2.9 — The security judgement on the exact text

Decided before any S2 code (Fable; the cornerstone areas (a), (b) and (d) of the parent). Each item says the
decision, the evidence, and what was NOT observed.

**J1 — What the script touches on a VM.** The whole list, from the text of § A2.3:

- *Programs it starts*: `claude` (three fixed command lines), `agy --version`, `jq`, `timeout`, `tail`, `ls`, `sed`,
  `grep`, `stat`. Never `codex`. Every one is found on the `PATH` after `$HOME/.local/bin` was put in front.
- *Files it opens*: at most three Codex session files, each through `tail -c 1048576 <file`.
- *Paths it only tests* (`-e`, `-d`, `-f`, `-x`; no byte is read): `~/.codex`, `~/.codex/auth.json`, the day folders
  and the rollout names, `~/.gemini/antigravity-cli/antigravity-oauth-token`, and the four package-manager paths.
- *What it changes*: its own working folder, to `/`; its own `PATH`. No file, no folder, no other process.
- **Evidence.** M-A1, M-A5, M-A6, M-A7, M-A13. **Not observed:** what the REAL `claude` and `agy` open and write when
  they are started — that is Claude Code's and agy's, measured for Claude Code in step 0 and W1–W5, owed for agy (W8).

**J2 — No secret passes through the script.** No line names a credential file to a program that reads. No token is in
a variable, an argument or the environment. The marker test (A2-T9) holds it for the exact text and is seen RED once
with one added line that prints a file. The access-time half (A2-T7) shows the five files were not opened, on a file
system where a read is seen (M-A7). **A hostile `claude` on the VM** could print the token as its "email": that
program is the VM user's own, who already holds the token; the parser's net (rule 9) is for the case that a vendor's
real output one day carries one.

**J3 — What an attacker on the VM can send back.** Someone who is that Linux user, or root, controls every byte of
stdout: up to 64 KiB within 30 s (the runner's caps, S3). They can send a perfect frame with false contents, a frame
with hostile contents, or noise.

**J4 — What the parser guarantees against that, whatever the bytes are.**

1. **Nothing of the reply is run, and nothing of it is a path, a URL or a command.** `readAccounts` is a pure function
   from a string to a value; it calls no process, file or network function (A2-T21's scan holds the module free of
   `child_process`, `fs`, `eval(` and `new Function`, with a companion).
2. **Only a closed vocabulary is read.** An unknown marker, an unknown key, a line that is neither, a marker in the
   wrong place or twice: the WHOLE answer is `unreadable`, with a word from a closed list. No hostile line can be
   skipped and the rest believed.
3. **It is bounded.** 64 lines of 512 bytes; a value is cut at 200 characters. More is `unreadable`.
4. **A text value is plain**: no control or format character, no `$(`.
5. **A number is a number in its range, or `null`.** No reply can produce `NaN`, a negative percent, a percent over
   100, or a 0 that stands for "not there" (A2-T17).
6. **A reset is a moment between one hour ago and eight days ahead, read only with the zone `UTC`, or a dash.**
7. **No value that looks like a credential reaches the typed result**, and so none can reach a row, a notice or a log
   (rule 9; the marker-token test A2-T16).
8. **The mode holds**: a usage reading in answer to `no-claude-usage` is refused.
9. **The command shown for a host without `jq` is chosen by one of five words**; the word comes from a closed list
   and the strings are S4's table. No text of a reply is part of a command (A4-T10).
10. **Nothing of a reply is in an error's text or a log**: the faults are closed words; the module has no `console`.

**J5 — What it does NOT guarantee: the truth.** That user can make the list show a false email, a false organization,
false percents, a false `turns 0`, or "signed out". The email and the organization are text of their choosing, up to
200 plain characters, drawn in a list on this machine — a sentence meant to mislead a person can stand there. It
cannot be an icon, a link the list follows, or a command. The list groups by the whole account text, so a false
email on one host can put that host into another account's group; S4 shows the hosts of a group by name, and the
group's numbers are those of the newest reading.

**J6 — Another Linux user on the same VM.** They can list processes and read every argument. The arguments of this
script are: the three `jq` programs (fixed text), the `sed` and `grep` patterns (fixed text), the fixed flags of the
vendor commands, the paths of up to 64 rollout files (given to `ls`), one path given to `stat`, and one file time
given to `jq` as `--arg ft`. No secret, no email, no usage number. The account text and the usage lines travel in
pipes and in two shell variables (`auth`, `kept`) that are printed by `printf`, which is a builtin of `dash`, `bash`
and busybox `ash` — so it is no argument of any program there. In a shell whose `printf` is a program they would be
arguments for an instant: an email address and percents, never a token. The script writes no file, so nothing is left
in a shared folder.

**J7 — The `PATH` and the folder.** `$HOME/.local/bin` in front means a file there is run in place of a system tool
of the same name (`jq`, `grep`). Only that Linux user (or root) can write there, and they already control every
answer. `/` holds no project of a person; W1 measured that the usage command leaves nothing there.

**J8 — The limits are requests to `timeout`.** That it ends a command and the command's child was seen here with
uutils 0.10.0 (M-A4) and is held on CI with GNU coreutils (A2-T8). Not seen: GNU `timeout` on a real VM, busybox
`timeout`, a child that left the process group (§ A2.14, U5). The readers' own limits and the runner's 30 s kill are
the bounds that do not depend on it.

**J9 — The frame is a second line of defence against a cut or mixed stream.** A script cut on the way starts nothing
(M-A8); a reply cut on the way has no end line and is `cut`; a banner before the reply is skipped; a line of data can
never be a marker (decision 2).

**The judgement.** *What the script reads and prints:* accepted — one kind of file is opened, and only named fields
and anchored lines leave. *The parser:* accepted — closed, bounded, never run. *Road (a):* as the parent judges it,
with W7 still able to fail it; this spec adds no claim to it. *The `jq` programs:* their LOGIC was checked in `gojq`;
they were **not run by `jq`** here, and the brief's commit 5 is where the leak tests first run against the real one —
until those are green, the judgement on "only named fields leave" stands on the reading of the three programs.

## A2.10 — Where the tests run: the real `jq` (K12), their time, and the caps (K17)

**K12 — the real `jq`.**

- *Does `ubuntu-latest` have `jq`?* Not read from a runner by this spec. What was read: another job of this
  repository runs `jq` on `ubuntu-latest` with no install step and says so in its comment (F12), and the five newest
  runs of that workflow on `main` ended green (the newest on 2026-11-18, read with `gh run list`). That is an
  inference from green runs, not a line of a log that prints a version.
- *The decision: the test job gets `jq` by a step of its own, so the question stops mattering.* In
  `.github/workflows/ci.yml`, job `extension` (`:524-527`), between *Install* (`:558-560`) and *Lint* (`:572-574`):

  ```yaml
      # The accounts script is run for real by `remoteAccountsRuns.test.ts`, with false vendor programs and the REAL jq:
      # its three jq programs are what keeps a credential and the person's activity on the VM. The runner image has
      # carried jq so far; this step makes that a fact of this job, not of the image, and prints the version it tested.
      - name: jq for the accounts script tests
        run: |
          command -v jq >/dev/null || { sudo apt-get update && sudo apt-get install -y --no-install-recommends jq; }
          jq --version
  ```

  It installs on a CI runner, never on a VM. Its shape is the one `release.yml:262` already has.
- *On CI's Linux a missing `sh` or `jq` FAILS* (A2-T23), as `remoteScriptRuns.test.ts:76-80` does for `sh`: when
  `process.env.CI === 'true'` and the platform is `linux`, the test asserts both were found.
- *On this machine* (no `jq`) the cases that need `jq` are skipped with the reason `no jq on this machine`; the cases
  that need NO `jq` run everywhere `sh` is: the frame, the cut script, the operands, the `PATH`, a missing tool, a
  missing `jq` with its manager word, agy, the hang of `agy`, nothing written. `research/module_tests.md` says which
  are which (S5).
- *The real `jq` is linked into the test's `bin/` folder by its absolute path*, as the harness links the other tools
  (F7). The script under test sees `bin/` and `$HOME/.local/bin` and nothing else.

**The time the script's own limits cost a test (point 3 of the stories document).** The limits are part of the text
under test, so a case that hangs a false vendor waits for the real limit. The bound:

- The hang cases are FIVE runs, started together inside ONE test and awaited together (`Promise.all`): under `sh` —
  (i) `claude --version` hangs, 2 s; (ii) the account command hangs, 3 s; (iii) the usage command starts a child and
  hangs, 8 s; (iv) `agy --version` hangs by `exec sleep`, a single process, 4 s; under `bash --posix` — (iii) again,
  8 s. They sleep; they use no CPU. The test ends after the longest, about 8.5 s; one after another they would take
  25 s.
- That test has `{ timeout: 40_000 }`, and every other test of the file `{ timeout: 20_000 }`. An ordinary run of the
  script takes about 0.3 s. The whole file stays under 30 s here and under 60 s on a CI runner; a file that needs
  more is a finding to report, not a limit to raise.
- Each hang case asserts a LOWER bound on its time (the limit minus 0.2 s: the command was not ended early) and that
  the reply came; the upper bound is the test's own timeout. No assertion compares two machine speeds.

**K17 — the caps.** The stories document found that the runner's capped sentence divides by 1 048 576 (F5) and gave
the 64 KiB sentence to S3 (A3-T4). What is S2's:

- The reply's OWN caps are this spec's and are exported: `ACCOUNTS_LINES_MAX = 64`, `ACCOUNTS_LINE_BYTES_MAX = 512`,
  and `ACCOUNTS_REPLY_BYTES_MAX = (64 + 2) × 513 = 33 858` — the largest stdout a legal reply can be, its two
  sentinel lines and its line ends counted. The real reply is at most 28 lines and about 3.5 KiB (R9 asks for at
  most 64 lines and 8 KiB).
- The 64 KiB is the RUNNER's ceiling over the whole stdout, a banner included, and stays S3's `ACCOUNTS_LIMITS`. A
  legal reply is about half of it, so a banner of 30 KiB before the frame still fits.
- A capped run never reaches the parser: the runner refuses it with its own sentence. So "capped" is not a fault of
  `readAccounts`, and the parser's two caps (`too-many`, `too-long`) are the only ones it knows.
- X9: A3-T4 asserts that the runner's cap is at least `ACCOUNTS_REPLY_BYTES_MAX`, so the two numbers cannot cross
  when one of them is changed.

## A2.11 — Scenarios

Unit tests: `node --test` through `scripts/run-tests.mjs`. Every test file starts with
`import '../../scripts/inRunFolder.cjs';`; a temporary folder comes from `ownedTemp` (F9). **RED-GREEN-RED for every
row**: (1) the test is written first and seen RED for the symptom in the *RED today* column — where the module does
not exist yet, add a stub so the file compiles and the RED is the assertion's own; (2) GREEN with the code; (3) the
break-it of § A2.12 is applied to the PRODUCTION line, the test is seen RED with its message, the line is restored.
All three observations go in the summary. There is no host scenario in S2: no module here imports `vscode`.

The ids A2-T1…T23 are the stories document's and keep their meaning; A2-T24…T30 are this spec's own. **No test reads
the clock**: every parser test passes `nowMs` (`NOW = Date.UTC(2026, 10, 19, 12, 0)` unless a row says otherwise), and
no generated input uses `Math.random` (a seeded generator, so a failure can be run again).

### A2.11.1 — `src/test/remoteFraming.test.ts` (new; pure)

| # | Name | Input → expected result | RED today |
|---|---|---|---|
| A2-T14 | the frame is found by ONE function, for both parsers | For the session sentinels AND the accounts sentinels: a banner before the begin line → the same lines as without it; a banner that HOLDS the begin line → the frame starts at the LAST one; two end lines → it ends at the FIRST after the begin; text after the end is ignored; an end line before the begin and none after → `no-end`; no begin line → `no-begin`, `otherVersion` false; only `COAI-ACCOUNTS 2` → `no-begin`, `otherVersion` true; a whole accounts reply is never `otherVersion` | no module `remoteFraming` |
| A2-T24 | the frame finder was MOVED, not copied, and the session parser is unchanged | A scan with its companion: `lastIndexOf(` is in `remoteFraming.ts` once and NOT in `remoteFrame.ts`; `remoteFrame.ts` calls `framed(` once. `remoteFrame.test.ts` and `remoteScriptRuns.test.ts` are green, and `git diff` shows no line of either (said in the summary) | `remoteFrame.ts` holds `lastIndexOf(` |

### A2.11.2 — `src/test/remoteAccountsScript.test.ts` (new; pure)

| # | Name | Input → expected result | RED today |
|---|---|---|---|
| A2-T2 | the script is the text of its spec, and both are pinned | § A2.3, the test: the sha256 of `ACCOUNTS_SCRIPT` is `PINNED`; the spec (in `todo/` or in `research/`, exactly one) has the line `Pinned sha256:` with that value; the spec's `sh` block after `## A2.3` equals the constant, and a difference names its first line. 218 lines; the text ends with LF | no module; with a stub: `the spec's pin line still says PENDING` |
| A2-T3 (the TypeScript half) | the modes are two words, by a closed list | `ACCOUNTS_MODES` deep-equals `['claude-usage', 'no-claude-usage']`; `isAccountsMode` is true for both and false for `''`, `usage`, `Claude-usage`, `claude-usage ` (a space), `claude-usage;id`, `constructor`; the script holds the `case` line that is built from `ACCOUNTS_MODES` — each word with `1:` before it, joined by the `case` separator, then `) ;;` — so the two lists cannot part | no module |
| A2-T25 | the script keeps its own rules (a scan of the TEXT, with a companion for each) | The first line is `coai_accounts() {`; the last two are exactly the call line (redirections first) and the end line; every other line is empty, `}` or starts with two spaces; it ends with LF, holds no CR and no character over 127; no `<<`, no backtick, no `eval`, no `trap`, no line that starts with `~` after its spaces; every `>` is the start of `>/dev/null`; no `&` that is not `&&`; ONE `cd `, and it is `cd / `; ONE `export`, and it is `export PATH`; ONE `TZ=`, on the usage line; every `LC_ALL=C` stands right before `grep`, `sed`, `ls` or `timeout -k 1 <n> sed`. The four vendor command lines of § A2.3 are in the text word for word (typed ONCE in the test, from the parent's § 4); `claude` follows a `timeout -k 1 <n> ` three times and `agy` once, and neither is started anywhere else; the only `codex` in a command is `command -v codex`. `.credentials.json`, `.claude.json` and `cli.log` are NOT in the text; `auth.json` and `antigravity-oauth-token` are in it once each, as the argument of `coai_sign_in`. No `jq` program holds `tojson`, `@json`, `@text`, `keys`, `to_entries`, `env`, `$ENV`, `input_filename` or `debug` (the words that print a whole value, a list of keys or the environment). Companions: `</dev/null 2>/dev/null` is there once; `\|\| return 0` 7 times; the first `set` is `set -f`, and each of the two `set +f` has a `set -f` two lines after it | no module |
| A2-T26 | the numbers in the script are the constants | In the text: `timeout -k 1 ${ACCOUNTS_SECONDS.account} claude auth`, `… ${ACCOUNTS_SECONDS.version} claude --version`, `… ${ACCOUNTS_SECONDS.usage} claude -p`, `… ${ACCOUNTS_SECONDS.agy} agy`; `tail -c ${ACCOUNT_JSON_MAX}` once; `tail -c ${USAGE_JSON_MAX}` and `tail -c ${CODEX_TAIL_MAX}` (the same number: the text holds it exactly twice); `tail -n ${CODEX_FILES_MAX}`; `1,${USAGE_LINES_MAX}p`; `COAI-ACCOUNTS ${ACCOUNTS_PROTOCOL}`. `ACCOUNTS_REPLY_BYTES_MAX === (ACCOUNTS_LINES_MAX + 2) * (ACCOUNTS_LINE_BYTES_MAX + 1)`. The four limits, one KILL second each and ssh's 5 s are under 30 s. Each name of `ACCOUNTS_TOOLS` is in the `for tool in` line and no other name is. `MANAGER_WORDS` are exactly the words `coai_manager` can print (taken from its `printf` lines), in that order, and the four absolute paths are in the text in the order `apt-get`, `dnf`, `yum`, `apk` | no module |

### A2.11.3 — `src/test/remoteAccountsUsage.test.ts` (new; pure)

`line(text)` is a frame of § A2.5 with that one `:line`; `LINES` are the three measured ones.

| # | Name | Input → expected result | RED today |
|---|---|---|---|
| A2-T18 | the Claude usage lines, as a table | **The three measured lines** → `5h` 56 `2026-11-19T15:00Z`; `week` 77 `2026-11-22T03:00Z`; `{ model: 'Fable' }` 49 `2026-11-22T02:59Z`. **The hour**: `3pm` → 15:00; `3am` → 03:00; `2:59am` → 02:59; `12am` → 00:00; `12pm` → 12:00. **The year**: read on `2026-12-30T12:00Z`, `resets Jan 1, 3am (UTC)` → `2027-01-01T03:00Z`; read on `2028-02-27T12:00Z`, `Feb 29, 3am` → `2028-02-29T03:00Z`, and read on `2026-02-27T12:00Z` → a dash. **The range**: `Nov 19, 11:30am` (30 minutes ago) → that moment; `Nov 19, 10am` (2 hours ago; next year is a year ahead) → a dash; `Nov 27, 12pm` (8 days ahead) → that moment; `Nov 28, 3am` (more) → a dash. **The zone**: `(PST)`, `(Europe/Berlin)`, `(utc)` → a dash. **The form**: `Feb 30, 3am`, `Nov 19, 13pm`, `Nov 19, 0am`, `Nov 19, 3:60pm`, `19 Nov, 3pm`, `Nov 19, 2027, 3pm` → a dash. **In every dash row `usedPercent` is still the line's number.** **The percent**: `100` → 100; `0` → 0 (a real zero stays a zero); `49.5` → 49.5; `101` → `null` and the window is kept. **A line that fits no form** — no zone clause, no reset clause, `1000%`, `-1%`, `1e3%`, `NaN%`, a third word after the zone — is no window and adds 1 to `unmatched`. **The windows**: no session line → no `5h` window; two model lines → two model windows in their order; `Current session` twice → the first, `unmatched` + 1. **No matched line**, `:turns 0` → `failed` `format`. **`:turns 1`** with the three good lines → `failed` `model-call`. Companion (the fixture is accepted): the three measured lines with `:turns 0` → `read`, three windows, `unmatched` 0 | no module |
| A2-T19 (the parser's half) | the Codex record, as a table | `:window 14 10080 1795518850` alone (**`primary` weekly, `secondary` null**) → one window, `minutes` 10 080, 14, `resetsAtMs` 1 795 518 850 000. **Both windows** → two, in the order sent, and the same two when the lines are swapped. **A 300-minute window**; **another length** (120) → `minutes` as sent. **A reset that has passed** (`1700000000`) → `resetsAtMs` as sent: the sentence is S4's, at draw time; the parser drops nothing. `- 10080 -` → `usedPercent` `null`, `resetsAtMs` `null`. `101 300 1` → `usedPercent` `null`. `14 - 1`, `14 0 1`, `14 600000 1` → no window; alone → `failed` `none`. Two windows of 300 → the first. **The time**: `2026-11-17T19:32:36.474Z 1794938000` → `asOf` that moment, `from: 'record'`; with an offset (`…T20:32:36+01:00`) → the same moment; `- 1794938000` → `from: 'file'`; `not-a-time 1794938000`, `2026-11-17 19:32 1794938000`, `2026-11-17T19:32:36 1794938000` (no zone), `2099-01-01T00:00:00Z 1794938000` (the future) → `from: 'file'`; `- -` → `asOf` `null`. A reply with the words `primary` or `secondary` anywhere is not in the grammar (`marker` or `key`). Companion: the reply of § A2.5 → `read`, one window, `asOf` from the record | no module |

### A2.11.4 — `src/test/remoteAccountsReply.test.ts` (new; pure)

`frame(...lines)` joins lines with `\n` after `COAI-ACCOUNTS 1` and before `COAI-ACCOUNTS-END`. `ok(stdout)` =
`{ code: 0, stdout }`. `GOOD` is the reply of § A2.5; it is asserted ONCE to give `read` with Claude `found` and three
windows, Codex `read` and agy `found`, so each negative row changes one thing of a reply the parser accepts
(`testing.md`, *A fixture the code rejects proves nothing*). The module's own grammar table is exported for this file
(`ACCOUNTS_GRAMMAR`), and the rows marked *derived* walk it, so a marker or a key added later is covered or red.

| # | Name | Input → expected result | RED today |
|---|---|---|---|
| A2-T15 | every marker and every key, and each fault by its own word | *Derived*: for every marker of the grammar and every word it takes, a frame that holds it in its place is `read`; for every key of every marker, a frame with it is `read`. Then, each from `GOOD`: `code` 3 → `exit`; no begin line → `no-sentinel`; `COAI-ACCOUNTS 2` → `version`; no end line → `cut`; a 65th line (data lines of a legal key repeated) → `too-many`; a line of 513 bytes made of `:org ` and 254 `é` (259 characters, so a count of characters would let it pass) → `too-long`, and the same with 253 `é` is read; a line `hello` → `line`; a data line before any marker → `line`; `COAI-NEW x` → `marker`; `COAI-VENDOR claude maybe` → `marker`; `COAI-VENDOR gemini found` → `marker`; `:token x` under any marker → `key`; `:email` under `COAI-USAGE claude read` → `key`; `:email` twice → `key`; nine `:line` → `key`; `:turns x`, `:turns 1234567`, `:unmatched -1`, `:sign-in-file maybe`, `:window 1 2`, `:as-of 1` → `value`. *Derived*: every key of `ACCOUNTS_UNREADABLE_SAID` was reached by a test of this file (a set the tests fill, compared at the end); every clause is not empty and all differ | no module |
| A2-T16 | no secret-shaped value passes the parser, and the values are plain | **The marker-token test.** Four tokens: `sk-ant-oat01-` + `MARK` 15 times; `M4RK` 10 times (a bare run of 40); `eyJ` + 20 token characters + `.MARK.MARK`; `ya29.` + `MARK` 5 times. *Derived*: for every (marker, key) of the grammar, `GOOD` with that key's value replaced by each token, and for every marker a frame with the token as one of its words. In EVERY one: `JSON.stringify(readAccounts(…))` holds neither `MARK` nor `M4RK`; a `console` spy saw no call; a text key (`email`, `org`, `plan`, `version`, `line`, and `as-of` with the token as the record's time before a file time) → that vendor is `unreadable` `credential-shape` and the two others are as in `GOOD`; a closed key → the answer is `unreadable` `value`; a marker word → `marker`. Companion: the same frames with `someone@example.com` and `Example Org` give those texts back. **Plain values**: an `:org` with a bell, an escape, U+202E and U+200B (built with `String.fromCodePoint`) → none of them in `org`; `:org Team $(zap)` → `Team $ (zap)`; a value of 10 KB → `too-long`; an `:org` of 300 characters under 512 bytes → 200. **Percents** `101`, `-1`, `1e3`, `NaN` in a `:window` → `usedPercent` `null`, the window kept. **A manager word outside the list** (`COAI-JQ missing brew`, `… apt-get;id`, `… APT-GET`) → `marker` | no module |
| A2-T17 | absent is not zero | From `GOOD`, each number taken out one at a time (no `:window`, `-` for used, `-` for reset, no reset zone, no `:as-of` time, no `:version`) → that field is `null` (or the window is not there) and NO field of the result is 0. Then 300 replies from a seeded generator that leaves numbers out at random, each built from a model: the typed result equals the one built from the model, and wherever the model has no number the result has `null`. A scan with its companion: `remoteAccountsReply.ts` and `remoteAccountsUsage.ts` hold no `?? 0`, no `\|\| 0` and no `100 -` | no module |
| A2-T20 | the typed result has one arm for each outcome, and the parser never throws | One frame for each: Claude `absent`, `signed-out`, `unreadable` (`no-jq`, `auth`, `timeout`, `credential-shape`), `found` with usage `read`, `skipped`, `failed` (`timeout`, `error`, `format`, `model-call`); Codex `absent`, `unreadable`, `found` with `read` and `failed` (`no-sessions`, `none`, `error`); agy `absent`, `unreadable`, `found` with `usage.state` `no-source`. All of them differ as JSON; no arm has a key whose value is `undefined`; "no usage source" (agy), "skipped", "failed" and "unreadable" are four different values. **Never throws**: 1000 inputs from the seeded generator — legal lines in a random order, random bytes, a lone surrogate, a line of 1 MiB, 10 000 lines, `code` −1 — each gives a value whose `kind` is `read` or `unreadable`, and an `unreadable` one has a `why` that is a key of `ACCOUNTS_UNREADABLE_SAID`; `JSON.stringify` of it holds no piece of the input longer than 200 characters | no module |
| A2-T27 | the reply is held to the mode and to its own `COAI-JQ` line | `GOOD` read with `no-claude-usage` → `shape`; `GOOD` with `COAI-USAGE claude skipped` read with `claude-usage` → `shape`, and with `no-claude-usage` → `read`, usage `skipped`. `COAI-JQ missing apt-get` with Claude `found` → `shape`; `COAI-JQ found` with `unreadable no-jq` → `shape`; `COAI-JQ missing dnf`, Claude `unreadable no-jq`, Codex `absent`, agy `found` → `read`, `jqMissing` `'dnf'`; `… missing none` → `'none'`; `COAI-JQ found` → `jqMissing` `undefined`. The vendors in another order, one missing, one twice, a `COAI-USAGE` after a state that is not `found`, a `found` with no `COAI-USAGE`, no `COAI-JQ`, two `COAI-JQ` → `shape`. `COAI-BAD tool` alone → `tool` (and `operands`, `home`, `folder` each by its word); `COAI-BAD tool` with a vendor after it → `shape`; `COAI-BAD nope` → `marker` | no module |
| A2-T28 | the credential net knows every key shape the product's redactor knows | *Derived*: the alternatives of `VENDOR_KEY_SHAPE.source` (at least 7 are found — the companion), each with 16 token characters after it: `credentialShaped` is true, AND `safeText` changes the same text (so the sample is a key to the redactor too). The same call twice in a row gives the same answer (the pattern's `g` flag). True: a run of 40; `eyJ` + 16; `ya29.` + 16; `-----BEGIN OPENSSH PRIVATE KEY-----`. False: `someone@example.com`, `Example Org`, `Basic Industries`, `Token Engineering Ltd`, `team`, `prolite`, `2.1.326`, a run of 39, and the three measured lines. `notifications.test.ts` is green with no edit | no export `VENDOR_KEY_SHAPE` |
| A2-T29 | `plainValue` is one rule, and doing it twice changes nothing | For 200 strings from the seeded generator (control and format characters, `$(`, `$$((`, long runs): the result holds no character of `Cc` or `Cf`, no `$(`, at most 200 characters, and `plainValue(plainValue(x)) === plainValue(x)` | no module |

### A2.11.5 — `src/test/remoteAccountsRuns.test.ts` (new; the REAL script by `sh -s`, and the REAL `jq`)

**The harness**, in the shape of `remoteScriptRuns.test.ts` (F7), not a copy of its fakes. `tree()` makes, in an
`ownedTemp` folder: `home/`, `home/.local/bin/` (the false `claude` and `agy` — where the measured VM has them),
`bin/` (the false `codex`; a link to the real file of each name of `ACCOUNTS_TOOLS` and of `stat`; and, when the case
wants it, a link to the real `jq`), `fx/`. `run(shell, t, operands)` = `spawn(shell, ['-s', '--', ...operands], { cwd:
<the tree>, env: { HOME: <home>, PATH: <bin> } })` — the environment holds NOTHING else — with `ACCOUNTS_SCRIPT`
written to stdin, and stdout AND stderr captured. Each test runs for every shell that is there: `sh`, and `bash
--posix`. The whole reply is then read by the real `readAccounts`.

The fakes are `sh` scripts that set their own `PATH=/usr/bin:/bin` first. Each appends ONE line to `fx/log`: its name,
its pid, every argument in brackets (`[auth][status][--json]`, so an empty argument is seen as `[]`), its working
folder, what its stdin is (`readlink /proc/$$/fd/0`) and its `TZ` (or `unset`); and writes its whole environment to
`fx/env.<pid>`. The false `claude` prints `fx/claude.version`, `fx/claude.auth` or `fx/claude.usage` by its first
argument and ends with the code in `fx/claude.<part>.code` (0 when there is none); a file `fx/hang-<part>` makes it
`exec sleep 100` (one process), and `fx/child-<part>` makes it start `sleep 100` as a child and wait. The fixtures
hold what was MEASURED (the RESULTS file, § 3, § 4, § 6): the 14 keys of the account JSON, with `orgId` set to
`ORG-ID-MARK`; the usage JSON with `type`, `subtype`, `is_error`, `num_turns`, `total_cost_usd`, `duration_api_ms` and
a `result` of the measured lines followed by a *"What's contributing to your usage?"* section; a `token_count` line
with the measured `rate_limits`. **What a fake cannot prove** is in § A2.14.

Rows marked **jq** need the real `jq`: skipped here with the reason `no jq on this machine`, and RUN on CI (A2-T23).
The others run wherever `sh` is.

| # | Name | Setup → expected result | RED today |
|---|---|---|---|
| A2-T1 | the script is one function, called last; a script cut on the way does nothing | A whole run: the first line of stdout is `COAI-ACCOUNTS 1`, the last is `COAI-ACCOUNTS-END`, exit code 0. The text cut after EVERY line before the call line (1 to 216): stdout is empty and `fx/log` is empty. The text cut at EVERY byte of its last two lines: no line of `fx/log` has a stdin that is not `/dev/null`; the end line is printed only by the whole text (with or without its last line end); a reply that lost its end line is `cut`. Both shells | no module |
| A2-T3 (the `sh` half) | a wrong or missing operand starts nothing, and says so | Operands none, `usage`, `CLAUDE-USAGE`, `claude-usage;id`, `claude-usage x` (two words), `-x` → the reply is the begin line, `COAI-BAD operands`, the end line; `fx/log` is empty; `readAccounts` → `operands`. Companion: `claude-usage` and `no-claude-usage` DO start the false `agy` | no module |
| A2-T4 | a `claude` and an `agy` only in `$HOME/.local/bin` are found, and no profile is read | The fakes are in `home/.local/bin` only, `PATH` is `bin/` → `COAI-VENDOR agy found` (and, **jq**, `claude found`). A `home/.profile` and a `home/.bashrc` that would create `fx/profile-ran` → the file is not there. Companion: with `home/.local/bin` renamed → both `absent` | no module |
| A2-T5 **jq** | each vendor's states, each usage state, each mode — through the real parser | Claude: the measured fixtures → `found`, usage `read`, three windows; `{"loggedIn":false}` with code 1 → `signed-out`, and NO `--version` and no `-p` start; no fake → `absent`; an account output that is no JSON → `unreadable` `auth`. Usage: `num_turns` 3 → `:turns 3`, no `:line`, `failed` `model-call`; a `result` with no line of the forms → `failed` `format`; code 1, `is_error` true, an output that is no JSON → `failed` `error`. The mode `no-claude-usage` → `skipped`, and `fx/log` holds NO start with `-p`. Codex: session files → `found`, `read`; no `codex` and no `~/.codex` → `absent`; no `codex` on the `PATH` but `~/.codex` there → `found`; no `sessions` folder → `failed` `no-sessions`; files with no record → `failed` `none`. agy: there → `found`; not there → `absent` | no module |
| A2-T6 **jq** | every vendor command is the parent's, word for word, from `/`, with an empty stdin | Mode `claude-usage`: `fx/log` holds exactly three `claude` lines, in this order — `[auth][status][--json]`; `[--version]`; `[-p][/usage][--output-format][json][--no-session-persistence][--strict-mcp-config][--setting-sources][]` (eight arguments, the last the EMPTY word) — and one `agy` line, `[--version]`. Every line: folder `/`, stdin `/dev/null`. `TZ` is `UTC` on the `-p` line and `unset` on every other. No `fx/env.*` holds `LC_ALL`. Mode `no-claude-usage`: the first two `claude` lines only. The expected lists are typed ONCE in the test, from the parent's § 4 | no module |
| A2-T7 | `agy` gets nothing but `--version`; `codex` is never started; no credential file is opened | In both modes, also with Codex session files there: `fx/log` has one `agy` line with the one argument, and NO `codex` line (the false `codex` logs any start, `login status` included). The five false credential files (`.claude/.credentials.json`, `.claude.json`, `.codex/auth.json`, agy's sign-in file and `cli.log`) get an access time in 2001 before the run; after it each still has it. Companion: a probe file treated the same way and then READ by the test has a new access time — on CI's Linux that companion must hold (else the file system does not show a read, and the row proves nothing); elsewhere the access-time half is skipped with that reason | no module |
| A2-T8 **jq** (but (iv)) | a vendor command that hangs, and one that starts a child and hangs, are ended by the command's own limit | The five runs of § A2.10, started together. (i) version hangs → the reply has NO Claude `:version`, the usage is read, at least 1.8 s; (ii) the account command hangs → `COAI-VENDOR claude unreadable timeout`, no `-p` start, at least 2.8 s; (iii) the usage command starts a child and hangs → `COAI-USAGE claude failed timeout`, at least 7.8 s, under both shells; (iv) `agy` hangs (`exec sleep`) → `COAI-VENDOR agy found`, no `:version`, `COAI-USAGE agy none`, at least 3.8 s. EVERY reply ends with its end line and is `read` by the parser; afterwards no pid of `fx/log` and no child pid a fake wrote is alive (`process.kill(pid, 0)` throws) | no module |
| A2-T9 **jq** | no secret leaves — SEEN | The five false credential files hold `{"accessToken":"sk-ant-oat01-SECRET-MARK…"}`. Both modes, both shells: `SECRET-MARK` is not in stdout, not in stderr, not in `fx/log` (the arguments), not in any `fx/env.*` (the environments), not in `JSON.stringify` of the typed result; a `console` spy around `readAccounts` saw no call; stderr is empty. The account fixture's `ORG-ID-MARK` is not in stdout either. **Seen RED once** (B12) | no module |
| A2-T10 **jq** | the person's activity does not leave — SEEN | The usage `result` holds `ACTIVITY-MARK` inside the *"What's contributing to your usage?"* section, in `Current sessions of ACTIVITY-MARK`, and in `Current week (all models): 77% used · resets Nov 22, 3am (UTC) ACTIVITY-MARK` → the reply has the three `:line` lines, `:unmatched 2`, and no `ACTIVITY-MARK`. Codex: the session file holds message lines with `TRANSCRIPT-MARK` and the word `rate_limits` in their text, and a `token_count` record whose `info` holds `TRANSCRIPT-MARK` → the reply has the right `:window` and no `TRANSCRIPT-MARK`. **Seen RED once** (B11) | no module |
| A2-T11 | of a version command only the version leaves | `agy` prints `Antigravity CLI v1.5.12 (stable) VERSION-MARK`, then `Update available: 1.6.0 VERSION-MARK` → `:version 1.5.12`, no mark, no `1.6.0`. A banner LINE first (`Welcome VERSION-MARK`, then `1.5.12`) → no `:version` line; the parser: version `null`, agy `found`. 40 digits → a `:version` of 32 digits; `null`, `found`. `unknown` → no line. `7` → `:version 7`; `null`, `found`. **jq**: the same five for `claude --version` (`2.1.326 (Claude Code)` → `2.1.326`) | no module |
| A2-T12 | a host without `jq` says so, with its manager word; agy is still read | No `jq` link in `bin/` (this row needs no `jq` and runs everywhere). (a) The real text: `COAI-JQ missing <word>`, where the test takes `<word>` from the same four paths with `accessSync(X_OK)` in the same order (on Ubuntu: `apt-get`); Claude `unreadable no-jq` and NO `claude` start; Codex `unreadable no-jq`; agy `found` with its version; `jqMissing` is that word. (b) The five branches: the lines of `coai_manager` are taken out of `ACCOUNTS_SCRIPT` by their first and last line, the two prefixes `/usr/bin/` and `/sbin/` are written as a temporary folder's, and that text is run by `sh` with each one of the four files there and with none → `apt-get`, `dnf`, `yum`, `apk`, `none`; with `apt-get` and `dnf` both → `apt-get`; with `dnf` and `yum` both → `dnf`. (b) runs a text DERIVED from the script; A2-T26 holds that the real text names the four absolute paths in that order | no module |
| A2-T13 | nothing is written | A listing of the whole `home` tree (path, size, time, mode) before and after one run of each mode, with `jq` (where it is) and without: identical. The tree's root has no new entry outside `fx/`. Every line of `fx/log` has the folder `/` | no module |
| A2-T19 (the script's half) **jq** | which Codex files are read, how far, and which record wins | **`primary` weekly, `secondary` null** (the measured record) → `:window 14 10080 1795518850` and one `:plan`; **both windows** → two `:window` lines. **No record** → `failed none`. **A record further from the end than the tail**: a file whose only record is followed by 1.1 MiB of lines → `failed none`. **The newer record wins**: the file changed LAST holds the OLDER `timestamp` → the other file's numbers. **Three files, two days**: a fourth file by change time is not read, and a third day folder is not read — each of them holds a record with used 99 and the greatest `timestamp`, and the reply never says 99. **A name outside the form** — `notes.jsonl`, `rollout-x.jsonl`, `rollout-…-<uuid>.jsonl.bak`, a name with a space — each with a 99 record → never 99. A cut first line and a last line with no line end do no harm. The file's time in `:as-of` is the newest file's `mtime` as the test reads it | no module |
| A2-T22 **jq** | the script bounds its own reply | A `result` of 10 MB → `COAI-USAGE claude failed error`. A `result` of 900 KB made of 20 000 good session lines → 8 `:line` lines and `:unmatched 19992`. An `email` of 10 KB and an `orgName` with a line break and `COAI-USAGE claude read` after it → ONE `:email` line and ONE `:org` line. In all three: at most 64 lines in the frame, each at most 512 bytes, and stdout is at most `ACCOUNTS_REPLY_BYTES_MAX` | no module |
| A2-T23 | on CI's Linux runner the `sh` and `jq` cases RUN | When `process.env.CI === 'true'` and the platform is `linux`: `sh` was found AND `jq` was found — a failure with the reason, not a skip. The test also asserts that this file HAS rows marked `jq` (the count is over 5), so the gate cannot pass on an empty set | no module; here: skipped rows name `no jq on this machine` |
| A2-T30 | no command waits for the script's stdin, and a reader that is gone stops the script | The test writes the script and does NOT close stdin → the end line comes within 5 s; then stdin is closed → the shell ends with code 0. The test reads the first line and destroys its end of stdout → the shell ends within 5 s, and `fx/log` holds no `-p` start and no `agy` start; the same when the shell was started with `SIGPIPE` ignored (`sh -c "trap '' PIPE; exec <the shell's absolute path> -s -- claude-usage"`) | no module |

### A2.11.6 — `src/test/remoteLogScan.test.ts` (extended)

| # | Name | Assertion | RED today |
|---|---|---|---|
| A2-T21 | nothing of a reply reaches a log or an error's text | The tree scan of `:73-79` covers the four new files by their name and finds nothing. A companion: the name pattern FINDS `remoteFraming.ts`, `remoteAccountsScript.ts`, `remoteAccountsUsage.ts` and `remoteAccountsReply.ts`. The four hold no `console.`, no `notify(`, no `throw new Error(` with a template literal, no `import` of `vscode`, `node:child_process` or `node:fs`, no `eval(` and no `new Function` — with a companion that the same scan finds each of those in a sample string. The existing companion of `:81-93` (`console.` once in the three E2 modules) is green with no edit | the companion: `the name pattern does not find remoteAccountsReply.ts` |

### A2.11.7 — Mapping to the stories document

A2-T1…T23 keep their ids. Three are in two files: A2-T3 (TypeScript in § A2.11.2, `sh` in § A2.11.5), A2-T19 (the
parser in § A2.11.3, the script in § A2.11.5), A2-T14 (the frame finder in § A2.11.1; the accounts parser's use of it
is A2-T15's `no-sentinel`, `version` and `cut` rows). The stories document's "the spec's own scenarios" are
A2-T24…T30.

## A2.12 — Break-its (a PRODUCTION change; each seen RED with its message, then restored)

| # | Change | Goes red |
|---|---|---|
| B1 | the call line loses ` </dev/null`; then it is written with the redirections AFTER the command | A2-T6 — a `claude` line whose stdin is not `/dev/null`; A2-T30 — the test's own timeout; then A2-T1 — `a cut text started a program with the script as its stdin`; A2-T25 |
| B2 | the operand `case` is taken out | A2-T3 — `a wrong operand started agy` |
| B3 | the `PATH` line is taken out | A2-T4 — `a claude in ~/.local/bin was read as absent` |
| B4 | `cd /` is taken out | A2-T6, A2-T13 — the fakes saw the test's folder |
| B5 | `TZ=UTC` is taken off the usage line; then `--strict-mcp-config` is | A2-T6 — the `TZ` field; the argument list. A2-T25 |
| B6 | `timeout -k 1 8` is taken off the usage command | A2-T8 — the test's own timeout, naming the usage command |
| B7 | the mode test is inverted | A2-T5, A2-T6 — `the usage command ran for no-claude-usage` |
| B8 | the `[ "$jq" = found ]` line of `coai_claude` is taken out | A2-T12 — `a missing jq was read as an account that cannot be read` (`unreadable auth`), and a `claude` start |
| B9 | the `for tool in` check is taken out | A2-T5's companion with no `tail` in `bin/` — `a missing tool was read as an account fault`, not `COAI-BAD tool` |
| B10 | the two branches of `coai_manager` for `apt-get` and `dnf` change places | A2-T12 (b) — `dnf` where both are there; A2-T26 — the order of the paths |
| B11 | **the `grep` is taken out of the usage pipe** | **A2-T10 — `ACTIVITY-MARK` is in a `:line`** (the RED the stories document asks to be seen once) |
| B12 | **a line `tail -c 200 <"$HOME/.claude/.credentials.json"` is added to `coai_claude`** | **A2-T9 — `SECRET-MARK` is in stdout** (seen once); A2-T7 — the file's access time; A2-T25 — the file's name is in the text; A2-T2 |
| B13 | the account program also prints `.orgId` | A2-T9 — `ORG-ID-MARK`; A2-T15 on the real run — an unknown key |
| B14 | the version `sed` becomes `sed -n 1p` | A2-T11 — `VERSION-MARK` left the VM |
| B15 | the Codex program prints `($r \| tojson)` in place of the named fields | A2-T10 — `TRANSCRIPT-MARK`; A2-T25 — `tojson` |
| B16 | the Codex `tail -c 1048576` becomes a read of the whole file | A2-T19 — a record 1.1 MiB from the end was found; A2-T26 |
| B17 | the name `case` of the rollout files is taken out | A2-T19 — `notes.jsonl` was read (99) |
| B18 | `ls -tr` becomes `ls -t` | A2-T19 — the three files changed FIRST were read |
| B19 | `.t <= $t` becomes `.t < $t` | A2-T19 — on equal times the older file won |
| B20 | `1,8p` is taken out; then `.[0:120]` | A2-T22 — more than 8 `:line`; a line over 512 bytes |
| B21 | `\|\| return 0` is taken off `printf '%s\n' "$auth"` | A2-T30, the `SIGPIPE`-ignored half — `the usage command started after the reader was gone` |
| B22 | the usage program prints the lines of the text also when `num_turns` is not 0 | A2-T5 — `:turns 3` came with `:line` lines: `the answer of a model call left the VM` |
| B23 | one space is added inside `ACCOUNTS_SCRIPT`; then a word of the spec's block is changed | A2-T2 — the hash; `the spec and the constant differ at line n` |
| B24 | the parser takes the FIRST begin line | A2-T14 — a banner that holds the begin line |
| B25 | `remoteFrame.ts` keeps its own `lastIndexOf` beside the shared one | A2-T24 — `two frame finders` |
| B26 | an unknown key is skipped; then an unknown marker | A2-T15 — `key`; `marker` |
| B27 | the line cap is not checked; then `run.code` is not | A2-T15 — `too-long`; `exit` |
| B28 | a percent is read with `Number(…)` and no range | A2-T16, A2-T18 — 101, `1e3` |
| B29 | **rule 9 is taken out** (no credential net) | **A2-T16 — `MARK` is in the typed result** |
| B30 | `plainValue` does not break `$(` | A2-T16, A2-T29 |
| B31 | a missing percent becomes `?? 0` | A2-T17 — the field and the scan |
| B32 | the year is always this year | A2-T18 — 1 January read on 30 December is a dash |
| B33 | the zone is not compared | A2-T18 — `(PST)` was read as UTC |
| B34 | `12am` is hour 12 | A2-T18 |
| B35 | `turns` is not looked at | A2-T18, A2-T5 — `a model call was read as a usage` |
| B36 | the file's time is taken before the record's | A2-T19 — `from` |
| B37 | the window's `minutes` are not checked | A2-T19 — `14 - 1` became a window |
| B38 | the mode is not compared | A2-T27 — a usage reading for `no-claude-usage` |
| B39 | a manager word is passed through as it came | A2-T16 — `brew` |
| B40 | the parser calls `test` on the shared `g` pattern | A2-T28 — the second call answered false |
| B41 | `console.error(run.stdout)` is added to `readAccounts` | A2-T21; A2-T16 — the spy |

## A2.13 — The brief: order, files, commands

One coder, on `feat/terminal-accounts-e1`, on top of `b4aa611b` or later, beside the coder of S1 (the files are
disjoint: S1 has the consent files and the setting). Each commit is green on its own (`npm run compile` exit 0, the
whole suite, lint). Stage by path. **The script text is § A2.3's. The coder adds no ssh option, no file, no write, no
vendor command and no field to what leaves the VM: each of those is a change the caller decides.**

**Sizes** (the 800-line limit holds for `src/**`, not for `src/test/**`): `remoteFraming.ts` ≈ 30 lines,
`remoteAccountsScript.ts` ≈ 300 (218 of them the script), `remoteAccountsUsage.ts` ≈ 200, `remoteAccountsReply.ts` ≈
450. If the reply module would pass 650 lines, stop and tell the caller; do not add a file this spec does not name.

0. **Step 0, no commit.** The procedure of § A2.3, step 1: the block's hash, 218 lines, 8100 bytes.
1. **`ci: jq is there for the accounts script tests`** — `.github/workflows/ci.yml`, the step of § A2.10.
2. **`refactor(remote): one function finds the frame`** — NEW `remoteFraming.ts`; `remoteFrame.ts` (`framedLines`
   calls it); NEW `remoteFraming.test.ts` (A2-T14, A2-T24). No other test file changes.
3. **`feat(remote): the accounts script and its two modes`** — NEW `remoteAccountsScript.ts`; NEW
   `remoteAccountsScript.test.ts` (A2-T2, T3, T25, T26). The pin is written into § A2.3 and into the test in THIS
   commit (§ A2.3, steps 2 and 3); the two commands and their output go in the summary.
4. **`feat(remote): the accounts reply — the typed result and its value rules`** — NEW `remoteAccountsUsage.ts`,
   `remoteAccountsReply.ts`; `notifications.ts` (the pattern of `:183` becomes `export const VENDOR_KEY_SHAPE`, used
   in `SECRETS` as before; no character of the pattern changes); NEW `remoteAccountsUsage.test.ts`,
   `remoteAccountsReply.test.ts` (A2-T15…T20, T27…T29).
5. **`test(remote): the accounts script, run by a real sh and the real jq`** — NEW `remoteAccountsRuns.test.ts`
   (A2-T1, T3…T13, T19, T22, T23, T30). **This is the first time the three `jq` programs are run by `jq`** (§ A2.0).
   The `jq` rows run on CI, so push and read the job's log before going on. A red that needs a change of a `jq`
   program is expected to be possible and is NOT fixed in silence: say it in the summary with the old and the new
   lines; § A2.3's block, the pin and the test literal move in the same commit; and if the change touches WHAT is
   printed (a field, a pattern), the caller is asked first.
6. **`test(remote): the log scan knows the accounts modules`** — `remoteLogScan.test.ts` (A2-T21).

Commands, from `src_vs_code/` (the stories document's § 3 has the full list):

```bash
npm run compile                                                                         # read the exit code
node ../.agents/conventions/tools/low-priority.mjs node scripts/run-tests.mjs out/test/remoteAccountsReply.test.js   # one file
node ../.agents/conventions/tools/low-priority.mjs npm test                             # the whole suite
npm run lint                                                                            # eslint src
node scripts/count-notifications.mjs --write                                            # expect NO change: S2 adds no notice
```

- Lint (F14): every new function has `complexity` at most 4 and at most 50 lines. The parser is many small functions
  and tables typed by the marker and by the key, not one long `switch`; the grammar is ONE exported table that the
  checks AND the tests walk.
- The four modules import no `vscode`, no `node:child_process`, no `node:fs`. `remoteAccountsReply.ts` imports
  `framed`, the constants of `remoteAccountsScript.ts`, the rules of `remoteAccountsUsage.ts` and `VENDOR_KEY_SHAPE`.
- No test reads the clock and none uses `Math.random`. No new temp-API line: every folder comes from `ownedTemp`.
- Do not touch: `remoteRead.ts`, `remoteSessionHost.ts`, `modelKeys.ts`, `package.json` (S1); `remoteSsh.ts` and the
  round (S3); the rows and the words (S4); `SECURITY.md`, the help and the module documents (S5); `REMOTE_SCRIPT` and
  its tests. The duplicate of § A2.14, N1 is named there and is NOT fixed in this story.
- From the worktree root, before any commit that touches `todo/`: `node .agents/conventions/tools/plan-lifecycle.mjs`.

## A2.14 — What S2 does not close

**Not verified — each line says what was checked and what was not.**

| # | What | Checked | NOT checked |
|---|---|---|---|
| U1 | The three `jq` programs | Their logic, in `gojq` inside `gh`, with the input as a literal (M-J1…M-J3); the shell around them, with a stand-in `jq` (M-A1…M-A14) | **The real `jq`**: `-n` with `[inputs]` over a stream of two JSON values, a parse error in that stream (expected: no output, so `unreadable auth` or `failed error`), `-R` with `fromjson?`, `--arg`, how a number is printed (`14.0` → `14` is expected after the arithmetic), any version of `jq`. Commit 5 on CI is the first run |
| U2 | The `jq` on the VMs and on CI | Nothing | Its version. The programs use nothing newer than `jq` 1.5 by the manual (`inputs`, `fromjson?`, `startswith`, `split/1`, `def f(g)`, `--arg`); a `jq` older than that fails to compile them, which reads as `unreadable auth` and `failed error` — safe, and the wrong words. R10 records the versions |
| U3 | Whether `ubuntu-latest` has `jq` | Another job of this repository uses it and is green (F12; five runs) | A runner's own listing. The CI step of § A2.10 makes the answer not matter |
| U4 | `timeout` | uutils 0.10.0 here: `-k 1 <n>` is accepted, and a command and its child were ended (M-A4) | GNU coreutils `timeout` on a real VM (A2-T8 sees it on CI's runner); busybox `timeout` (Alpine: an old one has no `-k`, and then every vendor command fails and is said as `unreadable auth` or `failed error`); a child that left the process group — it is not ended, and it can hold a pipe until the reader's own limit |
| U5 | What `claude` prints under `ssh <host> sh -s` | The lines of step 0 and W1, taken through `creds_exec` | The locale and the environment of the real call; whether the JSON is one line (the script reads either); whether the middle dot comes as the character (`jq` decodes an escape too); the exit code and the JSON of `claude auth status` on a SIGNED-OUT host (the script reads `loggedIn` and not the code) |
| U6 | An account with no 5-hour window; a window with no reset clause | Nothing (the RESULTS file, § 9) | What `/usage` prints there. A line that does not fit is counted in `unmatched` and the window is a dash (R5 at the top of this spec; the parent's R9 looks at such a host) |
| U7 | The Codex file names | `rollout-<ts>-<uuid>.jsonl` (the RESULTS file, § 6) | The exact form of `<ts>`. The script takes any `rollout-*-<uuid>.jsonl` over `A-Za-z0-9._-`, so the form does not matter |
| U8 | A Codex session that started days ago and is still used | Nothing | Its file is in the day folder of its START, which may not be one of the two newest. The parent's rule is kept; the list says how old the record is |
| U9 | agy's sign-in file | Its path, from the RESULTS file, § 7 | Whether other agy versions keep it there. Wrong path: `:sign-in-file absent`, a wrong word and no leak |
| U10 | The access-time proof (A2-T7) | A tmpfs here shows a read (M-A7) | CI's file system. The row's companion decides there, and fails loudly if a read is not shown |
| U11 | The time of a real read from Windows | Nothing | R9 |
| U12 | W6, W7 and W8 | Nothing more than the parent has | E2. This spec claims none of them (§ A2.8) |

**Residuals, named.**

1. **A false positive of the credential net makes a real account unreadable.** An email or an organization's name
   with a run of 40 letters, digits, `_` or `-`, or an email that starts with `sk-` and 8 more token characters. The
   list then says a value looked like a credential. The safe direction; R3 would show it on the owner's accounts.
2. **More than eight usage windows**: the ninth line is not sent, and `:unmatched` counts it. The list can say that
   lines were not read; it cannot show them.
3. **That Linux user can make the list show false text** (§ A2.9, J5).
4. **After the reader is gone, one vendor command may still start** (the account command: it runs before the script's
   next `printf`), and a command that is running runs to its own limit (M-A11).
5. **A vendor program that leaves a child holding its stdout** costs that step its reader's limit, and the child is
   not ours to end.
6. **A shell whose `printf` is not a builtin** would show the account text and the usage lines as arguments for an
   instant (§ A2.9, J6). `dash`, `bash` and busybox `ash` have it built in.
7. **`HOME` is trusted as the VM gives it.** A `HOME` with a `:` adds a `PATH` part; a `HOME` with a line break makes
   the Codex paths fail their tests and nothing is read. Both are that user's own doing.

**Found in the neighbourhood, not changed here (`reuse-first.md`: describe, propose, ask).**

- **N1 — one rule in two private copies.** `plainOf` (`remoteRead.ts:67`) and `withoutControls` (`remoteSsh.ts:216`)
  are the same expression. This story adds the third reader of that rule and does NOT add a third copy: `plainValue`
  is exported, and X7 has S4 call it. *As it should be:* the two older functions call `plainValue`'s first step too
  (one exported `withoutControls` in `remoteFraming.ts`). They are in S1's and S3's files, so it is not done here.
  **Should it be done — by S3, in `remoteSsh.ts`, and by S1 or S4 in `remoteRead.ts`? The caller decides.**
- **N2 — the session script's call line** (`remoteScript.ts:428`) has its redirections after the command; a cut in
  one window of about 25 bytes would run its function with the script as stdin (M-A9, by reasoning). That script
  starts `tmux` and `ps`, not a vendor program, and its hash is in its promoted spec. Not changed; named for the owner
  of that plan.

## A2.15 — What the Opus reviewer checks

1. `ACCOUNTS_SCRIPT` is the block of § A2.3: A2-T2 is green, and the pin line is a hash, not `PENDING`. Each change
   of the text since this spec was written is in the summary with its reason.
2. The script was run by `sh -s` AND by the real `jq` in CI: the job's log shows the rows marked **jq** run, not
   skipped, and prints a `jq` version (the step of § A2.10).
3. The last two lines of the script are the call with the redirections FIRST and the end line. The four vendor
   command lines are the parent's § 4, word for word; there is no fifth; `codex` is in no command but `command -v`.
4. Read the three `jq` programs as a person who wants to leak: each prints only strings it builds from NAMED fields;
   none prints its input, an object, a key list or the environment. Then read the three `grep` patterns: each is
   anchored at both ends and holds no `.` that is not escaped.
5. A2-T9 and A2-T10 were each seen RED (B12, B11), with the RED messages in the summary; and A2-T16's marker-token
   test was seen RED with rule 9 taken out (B29).
6. `readAccounts` never throws (A2-T20) and no arm holds an error's message. The decision table of § A2.7 is in the
   code as written: no rule is reached by a default; an unknown marker or key is never skipped.
7. No number of the typed result can be 0 for "not there" (A2-T17), and no function of S2 computes "left".
8. `framed` is the one frame finder; `remoteFrame.ts` holds no second one; no existing test of the session reader
   was edited.
9. `notifications.ts` changed by one name and one `export`; the pattern is the same characters; the parser never
   calls `test` on that object.
10. The four modules have no `console`, `notify`, `vscode`, process or file import (A2-T21).
11. The documents the coder touched say "we pass" for the three flags and claim nothing of W7 (§ A2.8).
12. The hang test is ONE test of five runs and the file's time is inside the bound of § A2.10.
13. Every scenario has its three observations in the summary; each break-it of § A2.12 was applied to the PRODUCTION
    line named, the script's lines among them (B1…B23).
14. Nothing of S1, S3 or S4 was built: no ssh call, no round, no row, no notice, no setting.

## Definition of Done — E1.S2

- [ ] This spec was read by the caller and reviewed by Opus (gate G2) BEFORE any S2 code; "Changes to § 4" (X1…X11)
      is in the stories document and in the briefs of S3 and S4, in one commit; the caller has read R1…R5 and
      recorded none of them, or the one it reads as a change, in the parent's ledger.
- [ ] A2-T1…T30 green; each first RED and each break-it B1…B41 observed with its message; A2-T9,
      A2-T10 and A2-T16 seen RED once; all in the summary.
- [ ] The pin is written: the sha256 of `ACCOUNTS_SCRIPT` is in § A2.3 and in the test, and A2-T2 holds the spec's
      block, the constant and both values to one another.
- [ ] `npm run compile` exit 0; the whole suite green on CI with the `sh` and `jq` rows RUN (A2-T23, read from the
      job's log); `npm run lint` green; no suppression count grew; `notification-sites.json` did not change.
- [ ] The three `jq` programs were run by the real `jq`, and § A2.14's U1 is rewritten from what was seen.
- [ ] No ssh option, no file, no write and no kind of call was added to the parent's (the table at the top of this
      spec still says none).
- [ ] `node .agents/conventions/tools/plan-lifecycle.mjs` clean.
