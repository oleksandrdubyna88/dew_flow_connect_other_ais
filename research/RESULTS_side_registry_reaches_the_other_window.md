# RESULTS — a `globalState` stamp from one side reaches an already-open window of the other side, live

> 2026-10-10, measured by the operator on their machine for E0 of
> [PLAN_paths_per_side.md](../todo/PLAN_paths_per_side.md) (decides how soon "both sides known" — and so the two path
> fields — appears). VS Code 1.141 on Windows 11 with a WSL (Ubuntu) remote window; one profile.

## The question

The side registry of the plan (design (a), option A) stamps `coai.sideSeen@<sideKey>` into the extension's `globalState`
from every window. `globalState` is the client's store, shared by local and remote windows of a profile (measured
earlier, `module_extension.md`, *A Team-server sign-in belongs to a SIDE*). What was not measured: does a stamp written
by a WSL window reach a Windows window that is **already open**, through `Memento.keys()` / `get`, and when — at once,
after a while, only after a reload?

## The probe

A throwaway extension (not shipped, not in the repository) with two commands, installed in both windows: **Stamp this
side** writes `sideProbe@<remoteName>:<distro>:<platform>` = `{at, platform, remote}` into `context.globalState`;
**Show** lists every `sideProbe@…` key `context.globalState.keys()` returns, with its value and the time asked. It runs
as a workspace extension, so in the WSL window it runs in the WSL extension host — exactly where coai runs.

## What was observed (in this order, both windows open throughout, no reload)

| Step | Window | Time (UTC) | Result |
|---|---|---|---|
| 1 | Windows (`local:win32`, probe activated 14:02:25.491) | 14:02:25.494 | `Memento.keys()` available: true · no stamps |
| 2 | WSL (`wsl:Ubuntu:linux`) | 14:02:48.065 | stamped `sideProbe@wsl:Ubuntu:linux` |
| 3 | Windows — the same, already-open window | 14:03:11.171 | **1 stamp visible: the WSL one** |
| 4 | Windows | 14:03:38.841 | stamped `sideProbe@local:win32` |
| 5 | WSL — already open (activated 14:02:48.057) | 14:03:58.344 | **2 stamps visible: its own and the Windows one** |

## What it means for the plan

- **"Both known" flips live**, in both directions, without a reload: the first `Show` after the other side's stamp saw
  it. The latency was not measured below the 23 s and 20 s between the stamp and the first look — it is an upper bound,
  not the lag.
- **`Memento.keys()` exists** in both extension hosts (and in `@types/vscode` ^1.85, the manifest's floor) — the
  registry can be one key per side, enumerated, as design (a) proposes (E0.3).
- The fallback of E0.2 (switching `coai.pathFieldsPerSide` to `always` from a path spelled for the other OS) is **not
  needed** as a detection signal; it stays a person's override only. E2 still proves the signal with a `test:host`
  scenario (one window, a seeded record), since the cross-window half cannot run in CI.
- Not measured: a second WSL distro, an SSH remote, Settings Sync across machines (which does not carry `globalState`
  without `setKeysForSync`), and how a page that is already open learns of the new side — E2 re-reads the registry on
  every render, so the next repaint shows it.
