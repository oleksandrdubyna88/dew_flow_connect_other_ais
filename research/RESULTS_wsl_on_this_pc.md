# W9 — WSL on this PC, measured in a disposable distro

> Status: **record, 2026-11-26.** Measurement W9 of
> [../todo/PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md](../todo/PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md),
> § 5: the gate for its epic E3, WSL on this PC. What the plan took from it, and the one item it left open for the
> owner (the login session of surprise 1), are in that § 5. The parts (a) to (g) as the plan set them are in the
> plan's appendix, § K.7. This file is public: a Windows user name is written `<user>`.

Date: 2026-11-26 (owner's go the same day). Run from inside the WSL distro `Ubuntu`, calling `/mnt/c/Windows/System32/wsl.exe`
(written `W` below). No Python was started. The disposable distro `coai-w9` was created for the check and is gone.

```
WSL version: 3.0.5.0   Kernel version: 6.18.58.1-1   WSLg 1.0.79   Windows version: 10.0.26300.9974
```

Setup, as run:

```
W --install Ubuntu-24.04 --name coai-w9 --location 'C:\Users\<user>\wsl\coai-w9' --no-launch   # 44 s, rc 0 (download + install)
W -d coai-w9 -u root --exec /bin/sh -c '<useradd -m -s /bin/bash wa; same for wb; echo "echo PROFILE_RAN_<file> >> /tmp/w9-profile-<user>" appended to ~/.profile, ~/.bashrc, ~/.bash_profile of wa, wb and root>'
```

Result: `wa` uid 1000, `wb` uid 1001, `root` uid 0; `/etc/wsl.conf` of the Ubuntu-24.04 image holds `[boot] systemd=true`.
The default user of a `--no-launch` install is root.

All calls below were made from a working folder on the Windows drive (`/mnt/c/...`). From a Linux-only working folder `W` prints
`wsl: Failed to translate '\\wsl.localhost\Ubuntu\...'` on stderr (harmless, the call still runs); `.wslconfig` of this PC also
makes `W` print two config warnings on stderr during install (a duplicated `wsl2.vmIdleTimeout` key and the sparse-VHD notice).

## (a) The list calls

| Call | stdout bytes | Exit |
| --- | --- | --- |
| `W --list --quiet` | UTF-16LE, **no BOM**, `U\0b\0u\0n\0t\0u\0\r\0\n\0c\0o\0a\0i\0-\0w\0 9\0\r\0\n\0` (names, CRLF after **every** name, including the last) | 0 |
| `W --list --running --quiet` | same encoding, only the running names | 0 |
| `WSL_UTF8=1 W --list --quiet` (variable set on the Linux side only) | **still UTF-16LE** — not honoured | 0 |
| `WSL_UTF8=1 WSLENV=WSL_UTF8 W --list --quiet` | **UTF-8**, no BOM, `Ubuntu\r\ncoai-w9\r\n` | 0 |

- Both with `coai-w9` stopped and running: the stopped one is in `--list --quiet` and not in `--list --running --quiet`
  (the default distro comes first).
- Decode: `iconv -f UTF-16LE -t UTF-8` works; `tr -d '\0'` also works for ASCII names. Either is needed unless `WSL_UTF8=1` is
  carried across (from Linux that needs `WSLENV=WSL_UTF8`; from a Windows process, setting the variable in its own environment is
  the normal case and was not separately measured). Line end is CRLF in both encodings. A reader must strip `\r`.
- **A list call starts nothing**: `coai-w9` was Stopped, then `--list --quiet` and `--list --running --quiet` were called, and
  `--list --verbose` still said Stopped (repeated twice).
- `W --terminate coai-w9` prints `The operation completed successfully. ` (UTF-16LE too), rc 0.
- stderr of the list calls was empty.

Verdict (a): measured. UTF-16LE without BOM by default; UTF-8 only with `WSL_UTF8=1` in the environment of `wsl.exe` itself;
CRLF; exit 0; a list call starts no distro.

## (b) The identity of a call, as each user and as root

Argv as in § 2, script on stdin: `printf '%s\n' '<probe>' | W -d coai-w9 -u <user> --cd / --exec /bin/sh -s -- probe`.

| user | `id -u` | `HOME` | `pwd` | `$0` / argc / `$1` | `SHELL` `LOGNAME` `USER` |
| --- | --- | --- | --- | --- | --- |
| wa | 1000 | `/home/wa` | `/` | `/bin/sh` / 1 / `probe` | `/bin/bash` `wa` `wa` |
| wb | 1001 | `/home/wb` | `/` | same | `/bin/bash` `wb` `wb` |
| root | 0 | `/root` | `/` | same | `/bin/bash` `root` `root` |

- `PATH` as given: 1463 bytes, starting `/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:/usr/games:...:/usr/lib/wsl/lib:`
  and then the **Windows** `PATH` appended as `/mnt/c/...` entries (appendWindowsPath is on). `~/.local/bin` is not in it
  (the accounts script prepends `$HOME/.local/bin` itself).
- Without `--cd /` the working folder is the caller's (`/mnt/c/Users/<user>` here).
- **Profiles — the finding.** The `--exec` process itself reads no profile. But with `systemd=true` (this image's default)
  WSL starts, on the **first call for a user after the distro starts**, a login session for that user:
  `/bin/login -f <user>` -> `systemd --user` + `(sd-pam)` + `-bash`. That login shell **did run `~/.bash_profile`**
  (marker `PROFILE_RAN_.bash_profile`, exactly one line per user per distro start; `.profile` and `.bashrc` did not run,
  because `.bash_profile` exists and a login bash reads only it). It also happened for **root** (`/bin/login -f` for root).
  The marker could be absent in the very first probe of the first call (race: the login runs beside the command) and present
  from the next call on. A later call for the same user starts no new login (marker count stayed 1).
  The three login sessions stay alive until the distro stops (see (e)/(f)). `/tmp` is emptied at each distro start.
- With `[boot] systemd=false` in `/etc/wsl.conf` (tested after a restart): **no login, no marker for wa, wb or root, no
  `login`/`-bash`/`systemd` process** — the plan's "no profile is read" holds exactly there.
- A login session also created, at first login, in each home: `.motd_shown` and `.cache/motd.legal-displayed` (empty files). They
  were already present before every listing of (c), so they are in the baseline and not caused by any script.
- **`HOME` cannot be given from outside.** root, `-u root`, all with `/usr/local/bin/w9env.sh` printing `$HOME`:
  - Linux side: `HOME=/tmp/x W ...` -> `/root`; with `WSLENV=HOME/u`, `WSLENV=HOME`, `WSLENV=HOME/w`: `/root` (a control variable
    `FOO` crossed with `WSLENV=FOO`, not with `/u` or `/w` from the Linux side, so the test was valid).
  - Windows side (through `cmd.exe /c "set WSLENV=...& set HOME=...& C:\Windows\System32\wsl.exe -d coai-w9 -u root --exec ..."`):
    no `WSLENV`: `/root`; `WSLENV=HOME:FOO` with `HOME=C:\x`: `/root` while `FOO=bar` crossed; `WSLENV=HOME/u:FOO/u` with
    `HOME=/tmp/x`: `/root`, `FOO=bar` crossed; `WSLENV=HOME/p`: `/root`; `HOME=/home/wa` with `WSLENV=HOME/u` and `-u root`: `/root`;
    `-u wa` with `HOME=/tmp/x`: `/home/wa`.
  - So `wsl.exe` always sets `HOME` from the passwd entry of the `-u` user, whatever the caller sends; ordinary variables do cross
    when `WSLENV` names them. **Root cannot be given another user's `HOME` through `wsl.exe`** (the road the plan wants closed is
    closed by WSL itself for `HOME`; other variables, e.g. `PATH`-like ones, are the reason to keep `WSLENV` empty, as the plan says).

Verdict (b): measured. Effective user, `HOME`, `pwd` as the argv says; `HOME` not overridable; the `--exec` process runs no profile,
**but a systemd distro runs the user's `~/.bash_profile` once in a login session started by the first call** (and leaves that
session running).

## (c) Before/after listings

Listing: `find /home/wa /home/wb /root -printf '%p %s %T@\n' | sort`, taken as root before and after each step (24 lines;
two listings with nothing between them were identical, so the listing itself changes nothing). Distro running with all
three logins already open.

| Step | Output | Diff of the three homes |
| --- | --- | --- |
| users script as root (the `awk` of the task + home-exists + shell not `nologin`/`false`) | `root:0:/root:/bin/bash`, `wa:1000:/home/wa:/bin/bash`, `wb:1001:/home/wb:/bin/bash` (LF line ends) | **none** |
| accounts script as `wa`, `no-claude-usage` | `COAI-ACCOUNTS 1`, `COAI-JQ missing apt-get`, `COAI-VENDOR claude absent`, `codex absent`, `agy absent`, `COAI-ACCOUNTS-END` | **none** |
| accounts script as `root`, `no-claude-usage` | same six lines | **none** |
| accounts script as `wa`, `claude-usage` (claude absent) | same six lines | **none** |
| cold start (terminate, then accounts script as wa, then listing) | same | **none** (the first-login files already existed) |

The accounts script text is the constant `ACCOUNTS_SCRIPT` of `src_vs_code/src/remoteAccountsScript.ts`, evaluated as the template
literal it is (so `\\` and `\${` become `\` and `${`): 222 lines, 8311 bytes, sha256
`e42bae875254ba509d2be0ed0c1cc079999e08d1be0f82bfb2edb35ac55d8ebd`, `sh -n` clean. Sent as
`W -d coai-w9 -u <user> --cd / --exec /bin/sh -s -- <mode>` with the text on stdin (the `{ </dev/null ...; }` last line keeps
the script from reading its own stdin). The output reached the caller with LF line ends, no CR.

The users script as root, as `wa` and as the default user (`wa`, set through `/etc/wsl.conf [user] default=wa`) printed the
same three lines; `/root` (mode 700) tests true for `-d` as `wa`, so even a non-root run sees root's home exist.

**Not measured:** the "read with a real Claude Code" row of (c): Claude Code is not installed in `coai-w9` and the task forbids
installing it. So the promise "read-only" is shown here only for the script's own path with **no vendor installed**: it
touched nothing in either home or root's. What a real `claude`/`codex`/`agy` writes stays open (W7 on a real host).

Verdict (c): measured for the scripts with no vendor tool; no change anywhere in wa's, wb's or root's tree after any run.

## (d) Users script: root versus the default user

- `W -d coai-w9 --exec id -un` on the fresh install: `root`.
- Output of the users script as root, as default user root, as `wa`, and as default user `wa` (after `[user] default=wa`): byte-identical.
- Because `/etc/passwd` is world-readable and a `-d` test on `/root` and `/home/*` is true for any user, the users script does not need
  root for its answer here. A distro whose `/home` or `/root` parents are unreadable was not made.

Verdict (d): answers the same as the default user and as root; root is not needed to run it on this image.

## (e) `wsl.exe` killed at a ceiling

Started `W -d coai-w9 -u wa --cd / --exec /bin/sh -c 'sleep 60'` in the background, killed after 3 s, then listed processes
inside `coai-w9` as root (`ps -eo pid,user,etimes,args`).

- Kill 1: **TERM to the Linux-side interop process** (`/init /mnt/c/Windows/System32/wsl.exe ...`, parent is the shell that started it).
  It died (exit 143); two Windows `wsl.exe` processes that had been started for the call (an outer one and its child) were
  **gone** a second later; **no `sleep`, no `sh -c` left** in `coai-w9`.
- Kill 2: **`Stop-Process -Force` of the outer Windows `wsl.exe`** only (what Node's `child.kill()` does on Windows): its child
  `wsl.exe` was gone, the Linux-side stub was gone, `sleep 61` was **not** in `coai-w9` 2 s and 7 s later.
- Kill 3: `sh -c 'sleep 62 & sleep 63'` (a background grandchild), TERM to the interop process: neither sleep left.
- Kill 4: SIGKILL of the interop process, plain `sleep 64`: nothing left. Kill 5: script on stdin (`sleep 65` via `sh -s`): nothing left.
- What **is** left after any call on a systemd distro: the login sessions of (b) (`login -f`, `systemd --user`, `(sd-pam)`, `-bash`
  per user), until the distro stops. With `systemd=false` nothing is left.
- One of the calls in this group failed with `UtilAcceptVsock:281: accept4 failed 110` (the known interop hiccup, retried).

Verdict (e): no process of the killed call survives (tested plain, with a background child, and with a stdin script); only the
user's login session survives, and only on a systemd distro.

## (f) A stopped distro

- `W --terminate coai-w9`, then `--list --verbose`: `Stopped`.
- **One `--exec` call starts the distro**: yes — `Stopped` -> `Running`; the first call takes 3.1–4.8 s (root: 3.1 s, wa: 4.2–4.8 s
  over 5 cold starts), a later call 0.3–1.1 s.
- It stays `Running` only **about 15 s** after the last call returns: two 1-s polls saw it running until 15.0 s and 14.3 s after
  the call; a 5-s poll of a third run saw it gone at about 20 s. The owner's `.wslconfig` has `vmIdleTimeout=-1` (a different,
  VM-level timer); the 15 s here is WSL's per-distro idle stop. Polling `--list --running` itself does not keep it up and does not
  start it.

Verdict (f): a call starts a stopped distro, and it stops again about 15 s after the last call. So Q2 "a stopped distro is listed and not started" holds only
if the round makes no call to it; any `-d <distro>` call, even `--exec true`, starts it.

## (g) Timing

Distro running, 10 calls each, wall time from the shell (includes the interop start of `wsl.exe`):

| Call | ms each | min / median / max |
| --- | --- | --- |
| probe as `wa` (stdin `sh -s -- probe`) | 343 346 388 397 394 372 305 353 335 329 | 305 / 350 / 397 |
| accounts script as `wa` (`no-claude-usage`, no vendor tools) | 922 918 826 907 911 850 908 882 897 1112 | 826 / 908 / 1112 |

Cold start: 3.1–4.8 s for the first call of a stopped distro.

## Surprises, for the plan

1. **A systemd distro runs the user's `~/.bash_profile`** (login session started by the first `-u` call after the distro starts), for
   root too — so "`--exec` starts no shell of the user, so no profile is read" is true for the process we start, not for the distro.
   The session stays alive. With `systemd=false` the statement is exactly true. Whatever a user's `.bash_profile` does, it runs
   as that user (root's as root) when we merely call.
2. **`HOME` is always the passwd home** of the `-u` user, even with `WSLENV=HOME` from the Windows side; other variables do cross when `WSLENV` names them.
3. **Two calls hit a 30-second `Wsl/Service/WSAETIMEDOUT`** ("connection attempt failed because the connected party did not
   properly respond...", the exit code was not captured): the first `-u root` call after a cold `wa`+`wb` sequence, and a second call right after a call as the default
   user. Neither repeated in 4 more cold sequences of wa/wb/root/wa (12 calls, all fine) or in the 40 later calls. This is the same size as the plan's 30 s ceiling: expect a call
   to die at the ceiling with that text and no output; it is not a script fault. One `UtilAcceptVsock ... accept4 failed 110` also occurred (the known hiccup).
4. `W --list ...` output is UTF-16LE with CRLF and no BOM; `WSL_UTF8=1` works only when it is in the environment of `wsl.exe`
   (on this PC from Linux it needs `WSLENV=WSL_UTF8`); other messages (`--terminate`, errors) are UTF-16LE as well, and `.wslconfig`
   warnings can appear on stderr.
5. The user's `PATH` inside a call contains the whole Windows `PATH` as `/mnt/c/...`; the accounts script prepends `$HOME/.local/bin`
   to that — a `claude` installed on the Windows side (e.g. `/mnt/c/Users/<user>/.local/bin`) could be found by `command -v` inside a distro.
   Not measured with a real tool.

## Not measured, and why

- "No distro" and "none running" outputs and exit codes: cannot be made from inside WSL without touching `Ubuntu`.
- A real Claude Code (or Codex, agy) read, and the listing around it: not installed in `coai-w9` and installing is forbidden here.
- `WSL_UTF8=1` set in a real Windows process environment (only through `cmd.exe /c` for `WSLENV`/`HOME`, and through the Linux side for `WSL_UTF8`).
- The exit code of the 30-second `WSAETIMEDOUT` failure.
- A distro whose default user is not root from the start (set through `/etc/wsl.conf` after the install instead), another distro family (only Ubuntu-24.04), `systemd=false` timing.

## Clean-up, as run

```
W --terminate coai-w9            # The operation completed successfully.
W --list --verbose               # before the unregister: Ubuntu Running, coai-w9 Stopped
W --unregister coai-w9           # rc 0; it also removed the install folder
rmdir C:\Users\<user>\wsl        # the empty parent the install created
W --list --verbose
  NAME      STATE           VERSION
* Ubuntu    Running         2
```

`coai-w9` is unregistered, `C:\Users\<user>\wsl\coai-w9` and its parent `wsl` are gone, `Ubuntu` is still listed and was never
terminated, exported, imported or made default.
