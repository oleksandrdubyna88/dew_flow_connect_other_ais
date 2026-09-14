# PLAN — the panel says where the data lives, and how to move it

> Status: **IMPLEMENTED, 2026-09-14.** Scope: `src_vs_code/src/panelView.ts` (`storageBlock`),
> `dataDir.ts` (`whereData`), `panelProvider.ts`, `shared/data-side-vectors.json` and the two suites
> that assert it. **Issue #115 is closed by this and its predecessor together.**
>
> **What shipped differently from the plan below, and why:**
>
> 1. **It says "where THIS WINDOW keeps its data", not "the directory in use".** Raised as Blocking
>    by gemini on the plan round, and it reframed the feature. The extension host has its own
>    environment; the MCP server's comes from the client entry that spawns it, so a `COAI_DATA_DIR`
>    in a `.mcp.json` reaches the server and never reaches this process. A panel reporting its own
>    environment as the server's would be confidently wrong exactly when somebody came to check.
>    `helpContent.ts` already documented the divergence in five languages as something to deduce
>    from an empty rounds list; this makes both halves visible instead.
> 2. **That also answered requirement 2's open question.** "The line to paste for a CHOSEN
>    directory" needed no picker: the directory offered is the one this window already resolved, and
>    the line is what makes a client's server agree with it.
> 3. **A refused side is rendered, never thrown.** `coaiDataDir()` throws on an unusable
>    `COAI_DATA_SIDE` — right for every other caller, wrong here. `whereData` returns it as a state
>    and the provider catches everything else, so the page cannot lose the sentence that explains
>    itself. (codex, who also asked for the provider-to-view wiring to be covered.)
> 4. **The pasted block reuses `mcpServerBlock`.** codex asked for platform-aware escaping; the
>    install flow's own serializer already does it, and reusing it is what makes a UNC path and
>    `C:\Users\…` survive being pasted.
> 5. **The two halves now assert SHARED vectors.** `shared/data-side-vectors.json`, read by
>    `dataDirAgreesWithTheServer.test.ts` and by `DataSideVectorTests` in C#. Each side's own tests
>    are self-consistent and blind to a divergence; codex and gemini raised it independently.
> 6. **The move/do-not-move inventory is asserted**, not left to prose — two reviewers asked for it
>    by name, because both wrong guesses (`worktrees/`, the token files) are silent.
>
> **Nothing is outstanding.** Every item of the Definition of Done below is met.
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

- [x] Every test above written RED first, with its failure message recorded. Two worth naming:
      dropping the loose-database note fails the shared vector that expects it, and removing the
      `worktrees/` warning fails the inventory assertion on the rendered page.
- [x] `npm test` green, the count reported in the pull request.
- [x] The diff through the `coai` plan and code rounds, every finding resolved — one plan round
      (`good_enough`, 14 findings, 11 accepted) and the code round below it.
- [x] `research/module_extension.md` and `CHANGELOG.md` updated.
- [x] This plan promoted to `research/` with `IMPLEMENTED` and the date, and issue #115 closed.
