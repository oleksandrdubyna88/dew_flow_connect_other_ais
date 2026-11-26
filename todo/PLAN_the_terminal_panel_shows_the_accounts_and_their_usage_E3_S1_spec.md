# PLAN — E3.S1 spec: the WSL road — who is called, as whom, and what runs as root (SECURITY SPEC)

> Status: **plan only, nothing implemented yet, 2026-11-26.** Written by Fable (the security row of
> `subagent-models.md`, outside the cornerstone quota) before any code of the story. Security spec of story E3.S1 of
> [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E3.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E3.md)
> (parent [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md):
> § 2, § 4, § 5, *Security*, residuals 12 and 13, decisions 21, 24 and 25, the test plan's row *The WSL road*; read
> on the plan's branch at `f5f3362b`). It stands on W9 (`research/RESULTS_wsl_on_this_pc.md` on the plan's branch;
> its letters (a) to (g) are cited as W9a…W9g). The script that every place runs is the
> [E3.S2 spec](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E3_S2_spec.md)'s; the door is the
> [E2.S1 spec](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E2_S1_spec.md)'s (§ P8). It keeps no
> change ledger. **No plan round and no judge has read the parent's text on root or on the login session (the
> parent's *Re-review for epoch 4*): this spec and E3's plan round are that reading.**
>
> Scope: `src_vs_code` only. NEW pure `src/remotePlaces.ts` and `src/remoteUsersScript.ts`; NEW `src/remoteWsl.ts`
> (the edge, Node only, beside `src/remoteSsh.ts`); ONE option of `src/processLauncher.ts` and
> `src/versionProbe.ts`; the tests. Coder: sonnet. Review: opus. Paths are under `src_vs_code/`.

## W0 — What this stands on

| Fact | Where |
|---|---|
| The list calls print UTF-16LE with no BOM, CR LF after every name, exit 0; with `WSL_UTF8=1` in `wsl.exe`'s own environment, UTF-8. A list call starts nothing | W9a |
| `-d <distro> -u <user> --cd / --exec /bin/sh -s -- <word>` runs `/bin/sh` as that user, in `/`, with that word as `$1`; the script on stdin; the reply has LF line ends | W9b, W9c |
| **`HOME` is the passwd home of the `-u` user whatever the caller sends**; other variables cross when `WSLENV` names them, and none without it | W9b |
| On a distro with systemd the first `-u` call for a user after the distro starts opens a login session of that user, root too; it ran `~/.bash_profile`, made two empty files, and stays until the distro stops. With `systemd=false`: none | W9b, W9e |
| No script changed a file in a home (no vendor tool was installed) | W9c |
| The users script answers the same as root and as the default user | W9d |
| A killed `wsl.exe` leaves no process of its call in the distro | W9e |
| **Any `-d` call starts a stopped distro**, in 3 to 5 s; it stops about 15 s after its last call | W9f |
| A call takes 0.3 to 1.1 s; two calls failed after 30 s with `Wsl/Service/WSAETIMEDOUT` and no output, and none of 52 later ones | W9g, surprise 3 |
| The launcher decodes stdout as UTF-8 and only ADDS variables to the inherited environment | `src/processLauncher.ts:58`, `:81` (`childEnvironment`), `:329` |
| The ssh edge: one absolute path, an argv, `shell` off, the script on stdin, 30 s, 64 KiB, the tree killed | `src/remoteSsh.ts:82` (`sshExecutable`), `:101` (`ACCOUNTS_LIMITS`), `:181` (`ranAsked`) |

**Not measured, and so owed by hand (§ W16):** the lists with no distro and with none running; a Windows
process as the caller; a real UID 0 with a real tool; which stream holds `wsl.exe`'s own error text.

## W1 — Who can try what

| Who | Tries | What holds |
|---|---|---|
| A terminal's name, a file of a workspace | To have WSL read before the "yes", or another program run | The door before every spawn (§ W11). The executable is one absolute path; no word of an argv comes from a name but the scope host `wsl` itself |
| A Linux user of a distro | To have their program run as root or as another user | The `-u` word is the user of the place and nothing else (§ W7). As UID 0 the script starts nothing that another user can write (§ W8) |
| The same user | To reach this side through a reply | Bounded (30 s, 64 KiB), a closed vocabulary, never run, drawn as text |
| Root of a distro | To have a system account or another user read as root | This side takes UID 0 only under the name `root`, a UID from 1000 to 59 999 else, by its own check of the reply (§ W6) |
| A distro's name | To be read as an option or as two names | Its class before any argv; a list that does not decode is not read at all (§ W4) |
| The environment of the window | To send a variable into a distro, or another `wsl.exe` | No `WSLENV` in the child (§ W3); `HOME` cannot cross (W9b); the path is not searched |

Not prevented, named: the login session (§ W9); a user's own program started as that user by our timer, and
interop from it; root's own programs as root.

## W2 — Decisions

| # | Decision | Why |
|---|---|---|
| J1 | `WSL_UTF8` is not set. The list is decoded by a strict rule that takes both forms (§ W4) | `WSL_UTF8=1` from a Windows process was not measured; a rule that reads both needs no variable and still reads the list when the person has set one |
| J2 | A list that does not decode is not read: no `-d` call in that round | A half-decoded name could name another distro, and a `-d` call starts it |
| J3 | The running list is read again before EVERY `-d` call, and the call is spawned at once after it | W9f. What is left is residual 12 |
| J4 | The users script runs as `root`, by name | The parent's § 2. Root is a place in nearly every distro, so its session is opened by the round in any case (§ W15, 6) |
| J5 | The parser of the users reply refuses the WHOLE reply for one line outside the rules | A reply is believed whole or not at all, as the accounts parser does |
| J6 | One ceiling for every `wsl.exe` call: 30 s, 64 KiB | The parent's § 2. A list call that fails ends the WSL part of the round, so the ceiling is paid once |
| J7 | After TWO calls that end at a 30 s limit, the rest of the WSL places are not called in that round | 32 places at 30 s would hold a round for 16 minutes |
| J8 | No call is tried again inside a round | The next round is 10 minutes away; a second try doubles what a broken WSL costs |
| J9 | Text of `wsl.exe`'s stdout is never in a sentence or a log line; only its exit code and the last line of stderr | Stdout of a `-d` call can hold what a script printed |
| J10 | As UID 0 the check covers every program the script starts by name, not only `claude` and `agy`; `id`, `stat` and `readlink` are its base | "Starts nothing that another user can write" is a rule about every start. The base is residual 13 |
| J11 | A read assumes nothing of a login session and never ends one | Decision 25: accepted, named, not managed |
| J12 | `wsl.exe` gets no argument but those of § W7: no `--terminate`, `--shutdown`, `--set-default`, `--install`, `--user` alone | Each would change the PC |

## W3 — The executable, its environment and its limits (`src/remoteWsl.ts`)

- **`wslExecutable(platform, env)`**: `win32.join(system32(env), 'wsl.exe')`, and `cwd` the same `System32`
  folder — the function `system32` that the ssh edge uses. Never a bare name, never the `PATH`. When the file is
  not there (`seams.exists`): the sentence S2 of § W13 and no call.
- **Not on Windows** (`seams.platform !== 'win32'`): the sentence S1 and no call of either runner. On Windows the
  scope host `wsl` is never given to `runAccounts` (ssh): the round's one switch sends it to this module.
- **The spawn** goes through `capture` and so through `launch`, the one spawn site: `shell` false, the script (or
  nothing, for a list call) on stdin, `{ cwd, maxStdoutBytes: 65 536, killTree: true, withoutEnv: ['WSLENV'] }`,
  30 000 ms. `WSL_LIMITS` equals `ACCOUNTS_LIMITS`; a test holds the two equal.
- **`withoutEnv`** is the one new option of `LaunchOptions` and `CaptureOptions`: names taken OUT of the copy of
  the inherited environment, compared without case (Windows keys are not case-sensitive, and a spread of
  `process.env` keeps the case it was written in). `childEnvironment(additions, inherited, without)` stays the
  one pure function. With `without` the child gets a full copy less those keys, never an empty map.
  The WSL edge adds no variable: no `HOME`, no `WSL_UTF8`, no `PATH`.
- What we do here is what was measured: with no `WSLENV` no variable crossed, and `HOME` crossed in no case
  (W9b). We do not say more than that.

## W4 — The two list calls and their decoding (`src/remotePlaces.ts`, pure)

Argv, word for word: `['--list', '--quiet']` and `['--list', '--running', '--quiet']`. Nothing on stdin.

**`wslNames(text): { names: readonly string[]; ignored: number } | 'undecodable'`**, over the text as the
launcher decoded it (UTF-8):

1. One leading U+FEFF is dropped (a UTF-8 BOM; a UTF-16 one does not decode to it and fails rule 2 — W9a saw
   no BOM).
2. **When the text holds U+0000** it is UTF-16LE read as UTF-8: its length must be even, EVERY character at an
   odd place must be U+0000 and NONE at an even place. Then the even places are the text. Any other text with a
   U+0000 is `undecodable`. (A character outside ASCII breaks the pattern, so such a list is not read: J2.)
3. Split at LF; one CR at a line's end is dropped; empty lines are dropped.
4. A line that fits `^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$` is a name; a second copy of a name is dropped. **Any
   other line counts in `ignored` and is never passed on** — a message of `wsl.exe` in any language, a name with
   a space, a name that starts with `-`.

**How the two answers are used:**

| Call | It ended | Then |
|---|---|---|
| The list | exit 0 and decodable | Its names, in its order, are the distros; the first 16 are taken (§ W12) |
| The list | anything else | S3; **no further `wsl.exe` call in this round** |
| The running list | exit 0 and decodable | **The running set is its names that are ALSO names of the list** |
| The running list | another exit code | The running set is empty: every distro says *not running, so not read*. (What "none running" answers was not measured: H2) |
| The running list | `timeout`, `not-started`, `capped`, or `undecodable` | S3; no further `wsl.exe` call in this round |

A distro that is in the list and not in the running set gets **no call of any kind** and the line S4 (Q2).

## W5 — The running list, again, right before every `-d` call

Before the users call of a distro and before each accounts call of each of its users, in this order and with
nothing awaited between the last two steps: the door (§ W11) → `--list --running --quiet` → the distro is in
its names → the `-d` call is spawned.

- The distro is not in it: no call, the line S5, and **no further call or re-read for that distro in this
  round**.
- The re-read itself fails: as the running list of § W4.
- So a round makes, for `wsl`: 2 list calls, and for each `-d` call one list call more (the parent's *Growth
  surfaces*). A list call took about 0.3 s (W9g).
- **What this does not close (residual 12):** a distro that stops between that list call and the spawn is
  started by it, in 3 to 5 s, and stops again about 15 s after. And while a round reads a distro, its own calls
  keep the distro up; it stops about 15 s after the round's last call there.

## W6 — The users script (`src/remoteUsersScript.ts`)

`USERS_SCRIPT` is the text below, byte for byte: **39 lines**, 1088 bytes, each line ends with LF, ASCII only,
no backslash but in `\n`, no backtick.

Pinned sha256: `7dc333a9d82e0e6b1a526e010630fdabf8c09c66523f1865528fc8863a980c1f`

E3S1-T10 holds three things to one another: the sha256 of the constant, the pin line above, and the first `sh`
block after the heading `## W6`. Run 2026-11-26 by `sh` (dash) and by `bash` on this PC's own `/etc/passwd`: the
frame, `root 0` and one user; with an operand, `COAI-BAD operands`. Not run with a made-up passwd file.

```sh
coai_users() {
  unset IFS
  set -f
  LC_ALL=C
  printf 'COAI-USERS 1\n' || return 0
  [ "$#" -eq 0 ] || { printf 'COAI-BAD operands\n'; return 0; }
  [ -r /etc/passwd ] || { printf 'COAI-BAD passwd\n'; return 0; }
  n=0
  while IFS=: read -r name pw uid gid gecos home shell; do
    case $name in
      ''|*[!a-z0-9_-]*|[!a-z_]*) continue ;;
    esac
    [ "${#name}" -le 32 ] || continue
    case $uid in
      ''|*[!0-9]*|0?*) continue ;;
    esac
    [ "${#uid}" -le 5 ] || continue
    if [ "$name" = root ]; then
      [ "$uid" -eq 0 ] || continue
    else
      [ "$uid" -ge 1000 ] || continue
      [ "$uid" -le 59999 ] || continue
    fi
    case $home in
      /*) ;;
      *) continue ;;
    esac
    [ -d "$home" ] || continue
    case $shell in
      nologin|false|*/nologin|*/false) continue ;;
    esac
    n=$((n + 1))
    [ "$n" -le 16 ] || continue
    printf 'COAI-USER %s %s\n' "$name" "$uid" || return 0
  done </etc/passwd
  [ "$n" -le 16 ] || printf 'COAI-MORE %s\n' "$((n - 16))"
  return 0
}
{ </dev/null 2>/dev/null coai_users "$@"; printf 'COAI-USERS-END\n'; }
```

- **It opens `/etc/passwd` and no other file, and starts no program**: every word of it is a shell builtin. So
  nothing of § W8 applies to it, though it runs as root. It has no operand; one is `COAI-BAD operands`.
- **It prints `COAI-USER <name> <uid>` for an entry only when ALL hold:** the name fits
  `[a-z_][a-z0-9_-]{0,31}`; the UID is digits with no leading zero, at most 5; **the name is `root` and the UID
  is 0, or the name is not `root` and the UID is from 1000 to 59 999**; the home is absolute and is a folder;
  the shell is not `nologin` or `false` (the word, or the last part of a path). An empty shell field counts as
  a shell. The first 16 such entries are printed; when there are more, one line `COAI-MORE <n>` says how many.
- **Never printed:** a UID from 1 to 999; UID 0 under another name; the name `root` with another UID.
- **The argv:** `['-d', <distro>, '-u', 'root', '--cd', '/', '--exec', '/bin/sh', '-s']` — no operand, so no
  `--`. The word `root` of this argv is the only literal user name in the module.

**The reply on this side — `usersOf(run)`, pure, never throws:** the frame `COAI-USERS 1` … `COAI-USERS-END`
through `framed`; at most 18 lines of 64 bytes; a line is `COAI-USER <name> <uid>`, or — only as the last —
`COAI-MORE <1 to 5 digits>`, or — alone — `COAI-BAD operands` or `COAI-BAD passwd`. **Every rule of the list
above is checked again here** (the name's class, the UID's form, `root` only with 0 and 0 only with `root`, the
range). One line that fails a rule, a name twice, more than 16 users, or any other line makes the whole reply
`unreadable` with a word of a closed list, and then that distro has no place in this round (S6). The home and
the shell are the distro's to test; this side cannot.

## W7 — The accounts call, and the place (`src/remotePlaces.ts`, `src/remoteWsl.ts`)

- **A place:** `{ kind: 'ssh'; host }` or `{ kind: 'wsl'; distro; user; uid }`. **`placeKey`**: `ssh:<host>`,
  `wsl:<distro>/<user>` — the distro's name as the list printed it, the user's as the reply printed it. Its label
  on screen is `<distro> · <user>`. A reading, its time and its failure are of the key; `Ubuntu/root` is a key
  of its own and never a user's.
- **`wslPlaceOk(place)`**: the distro fits its class, the user fits its class, and `(user === 'root') === (uid
  === 0)`, and a UID that is not 0 is from 1000 to 59 999.
- **The argv, word for word** (`wslAccountsArgv(place, mode, digest?)`):
  `['-d', place.distro, '-u', place.user, '--cd', '/', '--exec', '/bin/sh', '-s', '--', 'no-claude-usage']`, or
  with the last words `'claude-usage', <digest>`. No word is quoted: `--exec` starts `/bin/sh` with these words
  and no shell reads them (W9b).
- **It THROWS a fixed text that holds no value** when `wslPlaceOk` fails, the mode is not one of the two, or the
  digest rule of the S2 spec (§ R2.2) fails. `runWslAccounts` then answers `refused` with no call.
- **The three builders — list, users, accounts — are the only makers of a `wsl.exe` argv**, in this one module;
  a scan holds that `'--exec'` stands in no other source file.
- **Who runs as whom, by construction:** the `-u` word of an accounts call is `place.user`, read from the place
  and from nowhere else. So root runs a mode only for the place (distro, `root`), and a user's read is never
  root's. Nothing of ours names a `HOME`: not an argv word, not a variable. **Never root with another user's
  `HOME`** is held twice — by this rule, and by WSL itself, which takes `HOME` from the passwd entry of the
  `-u` user (W9b). We promise the first; the second is what was seen.
- The script on stdin is `ACCOUNTS_SCRIPT`, always: there is no script parameter.

## W8 — As UID 0: nothing that another user can write is started

**The rule.** When `id -u` prints `0` — the place (distro, `root`), and an ssh host whose login user is root —
the script checks, before the first start of each program, the program's file and every folder above it. One
that fails is not started. The functions are in the text of the S2 spec (§ R2.3); this section is their
contract.

**`coai_closed <path>` — the walk.** First `/`, then every prefix of the path, left to right, each with
`stat -c '%u %a'` on the name itself (a link is not followed by it):

1. The owner must be UID 0.
2. A link (`[ -L ]`): its text is read by `readlink`; the walk starts again on what it points to — its text when
   that is absolute, else the link's folder and its text — with the rest of the path. At most 16 links.
3. A folder or a file: **no write bit for group or for others** — the last two octal digits hold no 2, 3, 6 or 7.
4. A `stat` or `readlink` that fails, or a 17th link, fails the walk.

The first path that fails is the answer (`unsafe`). So the file as found, each link on the way, each link's
target and every folder above each of them, up to `/`, are checked — the parent's rule, as ssh's `StrictModes`
asks of a key file.

**Where it is called:**

| Before | When the walk fails |
|---|---|
| Root's `$HOME/.local/bin` goes on the `PATH` | It does not go on it. `claude` and `agy` are then looked for in the six system folders only; when one is not there and a file of that name is in the folder, its row is `unsafe` with the folder's failed path; else `absent` |
| The first start of `timeout`, `tail`, `ls`, `sed`, `grep`, `sha256sum`, `jq` | `COAI-BAD unsafe` with its `:path`: nothing of that place is read |
| The first start of `claude`, of `agy` | `COAI-VENDOR <name> unsafe` with its `:path`: that tool is not started, and the rest of the place is read |

- A `command -v` answer that is not an absolute path fails as UID 0.
- **On screen** (the S2 spec, § R2.7): *not read: `<path>` is writable by another user* — also when the path is
  another user's own.
- **When the user is not root, nothing is checked**: a user's program runs as that user, which is named, not
  prevented (the parent's *Security*).
- **The base, not checked by itself:** `/bin/sh`, which WSL or sshd starts before any line of ours, and `id`,
  `stat` and `readlink` as the six system folders hold them — root's own folder is not on the `PATH` yet when
  they are first used. This is residual 13.
- A file's ACL was not tested; a write right it gives shows in the group digit, which the rule reads.

**How a run that is not root stands for UID 0 in a test.** The test sends, BEFORE the pinned text, a prelude of
two shell functions that shadow the programs: `id` prints `0`; `stat` answers the real owner and mode with the
test's own UID written as `0`, answers `0 755` for the folders ABOVE the test's root (a runner's temporary
folder is open to all), and leaves the owner as it is under a folder the test names `other`. The pinned text is
unchanged and is what runs. Seen on 2026-11-26 in dash and bash: all closed → the three parts, `claude` and
`agy` started; the home folder group-writable → both `unsafe` with that folder, no start; a link whose target
sits in a folder open to all → `unsafe` with that folder; a file of another owner behind an absolute link →
`unsafe`; a `jq` writable by others → `COAI-BAD unsafe`. **What this does not show is a real UID 0**: row R13
and H1 do.

## W9 — The login session of a read (decision 25): the mechanics, and the words

- **What it is** (W9b, W9e): on a distro with systemd, the first `-u <user>` call after the distro starts makes
  WSL run `/bin/login -f <user>`, `systemd --user` and a login shell, which runs that user's profile — root's as
  root. A first login makes `.motd_shown` and `.cache/motd.legal-displayed` in that home. The session stays
  until the distro stops. With `systemd=false` there is none.
- **What a read assumes of it: nothing.** No line of ours waits for a session, reads what a profile set, or
  ends a session. The script sets its own `PATH`; `HOME` is the passwd one with or without a session; the first
  call can run beside the login (W9b saw that order change) and reads the same.
- **What it is bounded by:** one session for each user a call named — at most 16 in a distro, root among them —
  until the distro stops. We never stop a distro or a session (J12).
- **It is outside § W8**: WSL starts it before any line of ours. Whoever can write a user's profile decides what
  runs as that user at our first read after a distro start, as at any login of that user.
- **The words.** The question (W14 of the E2.S1 spec, § P12) says it already, so E3 asks nothing again. The tab,
  under the heading of `wsl`, one muted line, always: S13 of § W13. The help in five languages and `SECURITY.md`
  say the same three things: that WSL opens the session, that it runs the user's profile — root's as root —
  and that it stays until the distro stops (story E3.S4).

## W10 — How a `-d` call ends: the ceiling, the kill, `WSAETIMEDOUT`

| The capture ended | The result |
|---|---|
| `not-started` | `refused`: *wsl.exe could not be started: `<said>`* |
| `timeout` — our kill at 30 s, the tree (`taskkill /t /f`) | `refused`, S7. **A strike.** No process of the call is left in the distro (W9e); the login session is |
| `capped` | `refused`: *“`<label>`” sent more than 64 KiB, so the read was stopped.* |
| exit 0 | `answered`: the parser judges the frame |
| another exit code, and stdout holds the begin line as a whole line | `answered`: the parser says its fault |
| another exit code, no begin line, and `WSAETIMEDOUT` is in stdout or in the stderr tail (after rule 2 of § W4) | `refused`, S8. **A strike** |
| another exit code, else | `refused`, S9 with the exit code and the last line of stderr (rule 2 of § W4, control characters out, 300 characters) |

- **Two strikes in one round**: every WSL place not yet called says S10 and gets no call (J7). The count starts
  again with each round.
- `wsl.exe`'s own 30 s and our 30 s are the same size (W9, surprise 3), so either row can come first; both are a
  strike and neither is a fault of a script.
- **The log line**, once for a call that did not answer with exit 0, through the edge's one log seam:
  `ConnectOtherAIs: remote read place=<key> end=<how> stderr=<tail>`. Never stdout, never an operand.
- The round's log gate is unchanged: a place logs when its sentence changes and when it reads again.

## W11 — The door, and the order of a round's WSL part

**`sessions.may('round', 'wsl')` is asked before EVERY spawn of `wsl.exe`** — each list call, each re-read, each
users call, each accounts call — and again before each result is published. Only `yes` spawns. `no`,
`no-permission`, `waiting`, `damaged` and `stopped` each end the WSL part with **no call at all, the list calls
among them**. The round's own checks stand beside it as for an ssh host: the generation, the Stop, and a live
`Creds: wsl / …` terminal.

The order, each step awaited:

1. The door → the list. The door → the running list.
2. For each distro of the first 16, in the list's order: not in the running set → S4, next distro.
3. § W5 → the users call. Unreadable or refused → S6, next distro.
4. For each user, while fewer than 32 WSL places were taken in this round: § W5 → the accounts call, first
   mode. When the round's usage rule asks for it (the S2 spec, § R2.8): § W5 → the usage call.

Each place is published when its call lands; the enumeration's own result (the distros, which run, the counts,
a failure) belongs to the scope host `wsl`.

## W12 — The caps

16 distros of the list; 16 users of a distro (the script's own cap, root among them); 32 WSL places in a round.
What is past a cap is counted and said in the heading (S11), never read. A list's `ignored` count is said (S12).

## W13 — The words

| # | When | Words |
|---|---|---|
| S1 | The window does not run on Windows | *WSL is read from a Windows window only* |
| S2 | `wsl.exe` is not at its path | *wsl.exe was not found at “`<path>`”, so WSL on this PC cannot be read.* |
| S3 | A list call failed | *The list of WSL distros could not be read: `<how>`.* — `WSL did not answer within 30 s`, `wsl.exe ended with exit <n>`, `its output could not be decoded`, `wsl.exe could not be started` |
| S4 | A distro is not running | *not running, so not read* |
| S5 | It left the running list after the round began | *stopped since the round began, so not read* |
| S6 | The users call failed | *The Linux users of “`<distro>`” could not be read: `<how>`.* |
| S7 | Our 30 s | *“`<label>`” did not answer through WSL within 30 s.* |
| S8 | `WSAETIMEDOUT` | *WSL did not connect to “`<distro>`” within its own 30 s (WSAETIMEDOUT).* |
| S9 | Another exit | *WSL could not read “`<label>`” (exit `<n>`)*, and *: `<line>`* when stderr had one |
| S10 | Two strikes | *not read in this round: WSL did not answer twice.* |
| S11 | Past a cap | *and `<n>` more distros are not read (the limit is 16)*; *and `<n>` more users are not read (the limit is 16 for a distro, 32 in all)* |
| S12 | Lines not understood | *`<n>` lines of the list were not understood and are not read.* |
| S13 | Under the heading of `wsl`, always | *On a distro with systemd, WSL opens a login session for each user that is read. It runs that user’s profile — root’s as root — and stays until the distro stops.* |

Every `<distro>`, `<label>` and `<line>` goes through `plainValue` and is drawn as text.

## W14 — Scenarios

Pure tests with a false `capture` seam that records every argv, and runs of the REAL scripts by `sh -s`. RED
first. T20 to T26 are tests of the S2 spec's text and land with it.

| # | What must be shown |
|---|---|
| E3S1-T1 | The executable: `<SystemRoot>\System32\wsl.exe` and that folder as `cwd`; with no `SystemRoot`, `C:\Windows`; not there → S2 and no call |
| E3S1-T2 | **The environment**: the map given to the spawn holds no `WSLENV` in any spelling of its case, and still holds `SystemRoot` and `PATH`; the edge adds no variable; `childEnvironment` with `without` is never an empty map |
| E3S1-T3 | **The argv, word for word**: the two list calls, the users call, the accounts call in both modes |
| E3S1-T4 | **A name that fails its class never reaches an argv**: a distro or a user with a leading `-`, a space, 100 characters, a letter outside ASCII, the empty text → the fixed refusal, which holds no part of the name; no spawn |
| E3S1-T5 | **Root**: `-u root` with a mode word only for the place (distro, `root`, 0); `root` with UID 1000, another name with UID 0, UID 999 and UID 60 000 → refused; over 200 made-up places the `-u` word of every accounts argv is that place's user; `'--exec'` stands in one source file |
| E3S1-T6 | The decoder: UTF-16LE as the launcher shows it, and UTF-8; CR LF, also after the last name; a UTF-8 BOM (dropped) and a UTF-16 one (`undecodable`); no output; an odd length, a U+0000 at an even place, a missing U+0000 → `undecodable`; a message line and a name with a space count in `ignored` |
| E3S1-T7 | The running set is the names of BOTH answers; the running call with another exit code → empty; the list call with one → S3 and **no `-d` call** |
| E3S1-T8 | **A distro that is not running gets no call**: no recorded argv holds `-d <its name>`; its line is S4 |
| E3S1-T9 | **The re-read**: in the record, the spawn before every `-d` call is the running list; a distro that leaves the running list after the round began gets no call, S5, and no later re-read. Break-it: the re-read removed |
| E3S1-T10 | The users script's pin: the constant, the pin line and the block of § W6 are one |
| E3S1-T11 | **The users script's rules** (the pinned text with its two `/etc/passwd` words replaced by a file of the test, and the test asserts it replaced exactly two): users of 1000 and 59 999 and `root` with 0 are printed; UID 999, 60 000, `toor` with 0, `root` with 5, a name in capitals, a UID with a leading zero, a home that is missing, `nologin`, `false` are not — for root as for a user; 17 that count → 16 lines and `COAI-MORE 1`; an operand → `COAI-BAD operands` |
| E3S1-T12 | The pinned text as it is, on the runner's own passwd, with a `PATH` that holds no program: framed, with `COAI-USER root 0` |
| E3S1-T13 | `usersOf`: each of — UID 500, `toor 0`, `root 5`, a bad name, 17 users, a name twice, an unknown line, `COAI-BAD passwd` — makes the whole reply unreadable and gives **no place** |
| E3S1-T14 | The caps: 17 distros → 16 and the count; 32 places over three distros and the count of the rest, root counted as a user |
| E3S1-T15 | § W10, row by row, with the words; a marker text in stdout is in no sentence and in no log line |
| E3S1-T16 | Two strikes: the third and later places get no call and say S10; the next round calls again |
| E3S1-T17 | **The door**: for each of `no`, `no-permission`, `waiting`, `damaged`, `stopped` → **zero spawns, the list calls among them**; a "no" saved while a call runs → its result is dropped and no further spawn. Break-it: the door asked once for the round |
| E3S1-T18 | Not on Windows: S1 and zero spawns of either runner; on Windows the ssh runner records no call for `wsl` |
| E3S1-T19 | The key and the label; two users of a distro are two keys; `wslPlaceOk`'s table |
| E3S1-T20 | **As UID 0, all closed** (real script, the prelude of § W8): the three parts, `claude` and `agy` each record a start |
| E3S1-T21 | Root's `~/.local/bin` writable by its group: `claude` and `agy` are `unsafe` with that folder's path, and neither records a start |
| E3S1-T22 | `claude` is a link whose target sits in a folder open to all: `unsafe` with that folder, no start; `agy` beside it is read |
| E3S1-T23 | `agy` is another owner's file behind an absolute link: `unsafe`, no start; `claude` beside it is read |
| E3S1-T24 | A tool (`jq`) writable by others: `COAI-BAD unsafe` with its path, and NO program of that place records a start |
| E3S1-T25 | 17 links in a row, and a link that points to itself: `unsafe`; a `command -v` answer that is not absolute: `unsafe` |
| E3S1-T26 | **Not UID 0**: the folders of T21 and T22 → the tools start and no `:path` line is printed. **And root's `HOME`**: with `HOME` standing for root's and the prelude, a `claude` in ANOTHER home's `.local/bin` and one on the caller's `PATH` record no start |

## W15 — Break-its, and what is not closed

**Break-its** (a PRODUCTION change, seen RED with its message, then restored):

| Break | Must go red |
|---|---|
| `withoutEnv` not passed | E3S1-T2 |
| The class test of the distro removed from the builder | E3S1-T4 |
| The `-u` word taken from a parameter beside the place | E3S1-T5 |
| Rule 2 of the decoder replaced by "drop every U+0000" | E3S1-T6 |
| The running set taken from the running call alone | E3S1-T7 |
| The re-read removed | E3S1-T9 |
| `usersOf` skips a bad line and keeps the rest | E3S1-T13 |
| The door asked once for the round | E3S1-T17 |
| In the script: `coai_closed` always answers 0 | E3S1-T21…T25 |
| In the script: the group digit not tested | E3S1-T21 |

**Not closed, named** (the parent's residuals 12 and 13, in this spec's detail):

1. A distro that stops in the gap between the running list and the spawn is started by the call (§ W5).
2. The base of the check: `/bin/sh`, `id`, `stat`, `readlink` (§ W8). A distro whose system folders are open to
   another user is not safe to read as root, and this check cannot say so.
3. What a started program starts itself, and what it reads, is its own: `claude` and `agy` of root run with full
   rights every round.
4. Between the walk and the start, a chain that is root's and closed can be changed by root only.
5. **The login session** is outside the check and outside the kill (§ W9).
6. On a distro where root is no place (its shell is `nologin`), the users call still runs as root and opens
   root's session there.
7. Root of a distro can make the users script name users that are not there; a call for one fails as a call.
8. Interop: a user's own program, started by our timer, can start Windows programs as the Windows user.
9. A distro whose name does not fit the class is counted and never read.

## W16 — By hand, at the one real-window check

| # | What is run | Recorded |
|---|---|---|
| H1 | Row R13: root signed in to a tool in one distro | Root's row shows root's account; in a distro where root has no tool, no line |
| H2 | In PowerShell, with no distro running: `wsl --list --running --quiet`, then `$LASTEXITCODE` | The text and the exit code; § W4's table is right or is corrected |
| H3 | `wsl -d no-such-distro --exec true`, stdout and stderr apart | Which stream holds the error; S9 shows it or shows the exit code alone |
| H4 | As root in a test distro: `chmod 775 /root/.local/bin`, one round, then `chmod 755` | The rows say *not read: /root/.local/bin is writable by another user*; after the second, they read |
| H5 | The process list of a distro after a round (`ps -eo user,args`) | No process of a call; on systemd the sessions of § W9 |

## W17 — The brief, and what the reviewer checks

**Order.** (1) `src/processLauncher.ts`, `src/versionProbe.ts`: `withoutEnv`, with its test. (2)
`src/remoteUsersScript.ts`: the constant from § W6, the pin test, `usersOf`. (3) `src/remotePlaces.ts`: the
place, the key, the classes, `wslPlaceOk`, `wslNames`, the caps. (4) `src/remoteWsl.ts`: the executable, the
three builders, `runWslList`, `runWslUsers`, `runWslAccounts`, the outcomes of § W10. The enumeration's order
(§ W11) is a pure function over seams (`wslRound`), which E3.S3 puts into the round. The names start with
`remote`, so `remoteLogScan.test.ts` covers each from its first commit.

**The coder must not:** call `wsl.exe` in a test or by hand; add an argument, a variable or a retry; quote or
join argv words; read a distro's name from anything but the list; touch the script texts.

**The Opus reviewer checks:** every spawn has the door before it; the three builders are the only argv makers;
no `HOME`, no `WSLENV`, no `WSL_UTF8` anywhere in the edge; § W4's table and § W10's table row by row against
the code; the two pins; that no sentence or log line can hold stdout; that T20 to T26 run the pinned text and
not a copy.

## Definition of Done — E3.S1

- [ ] E3S1-T1…T19 green; T4, T5, T7, T9, T13 and T17 seen RED by their break-its, with the message.
- [ ] E3S1-T20…T26 green on the text of the S2 spec; T21 seen RED by its break-it.
- [ ] The users script is the block of § W6 and its pin holds.
- [ ] No test and no agent started `wsl.exe`; H1 to H5 are the owner's, at the one check.
- [ ] `SECURITY.md` and the help say § W9 and § W8 (story E3.S4).
