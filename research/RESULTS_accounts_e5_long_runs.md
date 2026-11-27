# E5 — the long runs on extension 2.1.0, as far as they ran

Plan: `todo/PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md`, epic E5. The build is the released
extension 2.1.0 (tag on `9060cb87`). Before the tag, the owner's VS Code ran VSIX 2.0.904 and then 2.0.905 from the
same code. This file holds what has run. Rows that did not run are marked open, with the reason.

## R7 — the live sessions are not touched

Before values: 2026-11-26 22:47:54 UTC. After values: 2026-11-27 05:29:50 UTC. In between, the tab ran its 10-minute
round all night.

### WSL distro `Ubuntu`, the default user — PASS

| What | Before | After |
|---|---|---|
| Claude Code processes | 6, all started 2026-11-25 19:20 | the same 6 pids, the same start times |
| `~/.claude/settings.json` sha256 (first 16) | `1ec5c123daf753fa` | `1ec5c123daf753fa` |
| MCP servers in `~/.claude.json` | 3 names | the same 3 names |
| `agy` binary | 280593096 bytes, mtime 2026-11-26 08:42:19 +0100 | the same |

### VM `ai1-dev3` — PASS

After values: 2026-11-27 11:00:17 UTC, read through the creds MCP with the owner's consent. Two earlier calls did not
run: the night call timed out with no one to answer, and the call at 05:29 UTC was refused with "Another request
holds this window's dialog place". Neither was worked around. A restart of the owner's PC freed the dialog.

| What | Before (2026-11-26 22:47:54 UTC) | After (2026-11-27 11:00:17 UTC) |
|---|---|---|
| claude/node processes | 14 pids, started from 2026-11-05 to 2026-11-26 | the same 14 pids, the same start times |
| `~/.claude/settings.json` sha256 (first 16) | `4ceed143825dda57` | `4ceed143825dda57` |
| MCP servers in `~/.claude.json` | 3 names | the same 3 names |
| `~/.local/bin/agy` | 280593096 bytes, mtime 2026-11-26 11:36:18 UTC | the same |
| backups in `~/.claude/backups` | 5 | 5 |

A first after-read of `agy` showed 4096 bytes: `command -v agy` found nothing in a non-login ssh shell, so the
read measured the home folder. The read by the full path above is the one that counts.

The logged-in state was not read again; the 14 sessions ran on through the whole night.

## R10 — the places that lack `jq`

| Place | `jq` | Package manager | root, or `sudo` with no password |
|---|---|---|---|
| WSL `Ubuntu`, the default user | missing | `apt-get` | `sudo` asks for a password |
| WSL `Ubuntu`, root | missing (the same file system) | `apt-get` | is root |
| VM `ai1-dev3`, the ssh user | present (`/usr/bin/jq`) | `apt-get` | `sudo` asks for a password |

The tab showed the fixed command `sudo apt-get install -y jq` for the Ubuntu rows. The owner ran it on
2026-11-27 at 07:39 UTC. `dpkg` recorded three packages: `jq`, `libjq1` and `libonig5`. Both the default user and
root find `/usr/bin/jq` now. Whether the next round reads the accounts in the tab is not yet recorded. This table
is the input of E6: of the places measured, none lacks `jq` now, and none has `sudo` without a password.

## R12 — two windows — OPEN

The owner's night word was "R12 not by hand". The automated tests cover parts of it on fakes and in one editor
process. They do not cover the gate as the plan writes it. The gate
needs two real windows and presses by hand.
