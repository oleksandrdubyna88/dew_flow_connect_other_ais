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

### VM `ai1-dev3` — OPEN

The before values are recorded: 14 claude/node pids, logged in, a settings sha, 3 MCP names, the `agy` size, and
5 backups. The after read goes through the creds MCP. Each call needs the owner's consent in the editor. The night
call timed out with no one to answer. The next call, at 05:29 UTC, was refused with "Another request holds this
window's dialog place": the night dialog still stands. It was not worked around. The row runs when the owner
closes that dialog.

## R10 — the places that lack `jq`

| Place | `jq` | Package manager | root, or `sudo` with no password |
|---|---|---|---|
| WSL `Ubuntu`, the default user | missing | `apt-get` | `sudo` asks for a password |
| WSL `Ubuntu`, root | missing (the same file system) | `apt-get` | is root |
| VM `ai1-dev3` | open: the same creds block as R7 | — | — |

The tab shows the fixed command `sudo apt-get install -y jq` for the Ubuntu rows. Whether the next round reads the
accounts after the owner runs it is open: the command has not been run yet. This table is the input of E6.

## R12 — two windows — OPEN

The owner's night word was "R12 not by hand". The automated tests cover parts of it on fakes and in one editor
process. They do not cover the gate as the plan writes it. The gate
needs two real windows and presses by hand.
