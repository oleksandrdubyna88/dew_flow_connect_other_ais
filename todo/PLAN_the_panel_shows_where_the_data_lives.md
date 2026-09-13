# PLAN — the panel says where the data lives, and how to move it

> Status: **plan only, nothing implemented yet.** Scope: `src_vs_code/src/panelView.ts`
> (`serverBody`) and its tests.
>
> The tail of [PLAN_the_data_directory_moves_and_each_side_keeps_its_own.md](../research/PLAN_the_data_directory_moves_and_each_side_keeps_its_own.md),
> extracted when that plan shipped on 2026-09-13 rather than left inside a document filed as
> documentation. Issue [#115](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/115)
> is closed by the two together.

## What shipped, and what is missing

The resolution rule is built and tested on both halves: `COAI_DATA_DIR` chooses a location,
`COAI_DATA_SIDE=<name>` partitions it so two installations sharing one NAS keep their own database,
sessions and tokens. That was the half that could be silently wrong, which is why it went first.

What is missing is the half a person touches. Today both variables are set by hand-editing an MCP
client entry, and **the panel says nothing at all about where the data is** — so there is no way to
answer "did my setting take effect?" short of looking for files.

## What must be true when this is done

1. The *MCP server* section shows **the directory in use**, resolved — not the raw variable — and the
   side name when there is one.
2. It produces **the line to paste** for a chosen directory, the way *Install the MCP server…*
   already produces the config block, and says which file it goes in.
3. It speaks **for the side it is running on**. A Windows panel shows a Windows path and a WSL panel
   the WSL one: `\\nas\coai` pasted into a WSL client entry is not a path that exists there, and
   `/mnt/z/coai` in a Windows one is not either. Each panel already runs on its own side; it must not
   pretend to know the other's mount.
4. It names **what to move and what not to**: the database, the sessions, `unparseable/` and
   `empty/`. Not `worktrees/`, which is scratch pruned on every `open`, and not the token files,
   which belong to the side that signed in.
5. It surfaces the server's own notes — a database left loose in a shared root, a side directory
   created for the first time. `PanelSettings` already emits both into `Unrecognised`, which the
   server logs at startup; the panel is where a person would actually see them.
6. Nothing here CHANGES the directory. Copying is deliberately out of scope — it is the half with
   every interesting failure in it (a partial copy, a file in use, a database being written while it
   is read), and it stays out rather than being done badly.

## Test plan (RED first)

| # | Test (`src_vs_code/src/test/panelView.test.ts`) | RED symptom expected |
|---|---|---|
| 1 | the section shows the resolved directory, and the side when one is set | nothing is shown |
| 2 | with no side set it says so plainly — the default has not moved — rather than showing an empty side | it reads as though a side is configured and empty |
| 3 | the line to paste carries the directory this side resolved, and names the file it belongs in | there is no line |
| 4 | the server's notes (a loose database, a new side directory) are rendered where a person sees them | they reach the log only |

Run: `cd src_vs_code && npm test`.

## Definition of Done

- [ ] Every test above written RED first, with its failure message recorded.
- [ ] `npm test` green, the count reported in the pull request.
- [ ] The diff through the `coai` plan and code rounds, every finding resolved.
- [ ] `research/module_extension.md` and `CHANGELOG.md` updated.
- [ ] This plan promoted to `research/` with `IMPLEMENTED` and the date.
