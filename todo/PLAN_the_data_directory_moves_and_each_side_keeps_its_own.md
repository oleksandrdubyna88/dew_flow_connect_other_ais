# PLAN — the data directory can move, and two sides keep their own

> Status: **plan only, nothing implemented yet.** Scope: `src_mcp/src/Server/PanelSettings.cs`, the
> panel's *MCP server* section, and the docs.
>
> Issue [#115](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/115): *"we take
> the values from the database here. I want to be able to say where the database lives, the way
> CredsForDevs does, so that it is not lost when Windows is reinstalled — there is important data for
> analysis in there."*
>
> **Decided by the operator, 2026-09-13:**
> - **the default stays exactly as it is** — nothing moves on its own;
> - **moving is offered later, when it is needed**, not forced;
> - **Windows, macOS and Linux**, all three;
> - and, after the merge question was put and answered: **no merge.** Windows and WSL both move to
>   the shared place and each keeps its OWN database under its own name — *"they will simply sit
>   side by side in one folder"*.

## What already exists, and what the issue is really asking for

`COAI_DATA_DIR` already overrides the data directory — `PanelSettings.cs:297`, `Path.GetFullPath`,
no platform assumption. **So "choose the location" works today**, on all three platforms, for anyone
willing to edit an MCP client entry by hand.

What does not exist is a way to set it that is not hand-editing JSON, and an answer to what happens
when two sides are pointed at one place.

## The merge is not built, and that is the decision

The obvious reading of "point both at the NAS" is that two divergent databases must be reconciled:
pick the bigger, remap the smaller one's ids, merge with SQL. **That was put to the operator and
declined.** It is worth recording why it would have been expensive, because the reasons are also why
side-by-side is right:

- `rounds.id` and `findings.id` are `INTEGER PRIMARY KEY AUTOINCREMENT` (`Schema.cs:35, 73`). Two
  databases that have both been written to hold the same ids for different rounds, so a merge cannot
  `INSERT ... SELECT` — it must remap every id and rewrite the foreign keys in `reviewers` and
  `findings`. Get it subtly wrong and one round's findings attach to another round, silently.
- `sessions.id` is TEXT and the same id can legitimately exist on both sides — one repository, one
  branch, two machines — so "same id, different contents" needs a decided answer.

Side by side has neither problem. Each side reads and writes only its own, exactly as it does today;
the only thing that changes is WHERE it sits.

## The directory is not just a database, which is what decides the shape

| Thing | What it is |
|---|---|
| `coai.db` | SQLite: sessions, rounds, reviewers, findings, the FTS index |
| session files | one JSON per session — what the panel's Active rounds reads |
| `worktrees/` | scratch checkouts, pruned on `open` — disposable |
| `unparseable/`, `empty/` | kept vendor answers, referenced by audit lines |
| Team-server token files | `TeamServerAuth.TokenPath(DataDir, …)` |

The operator's words were *"give the databases different names"*. Applied to the database alone that
leaves the session files, the kept answers and the **tokens** sharing one directory — and the tokens
are the case that must not share: `PLAN_team_server_side_and_url.md` keeps a WSL token separate from
a Windows one **on purpose**, because a token belongs to the side that signed in.

**So the side's name goes into the PATH rather than into one filename**: each side gets its own
subdirectory under the shared location. That is what *side by side in one folder* means with the rest
of the directory carried along, and it needs no naming scheme for five different kinds of file.

```
//nas/coai/
    windows/   coai.db  sessions/  unparseable/  empty/  tokens
    wsl/       coai.db  sessions/  unparseable/  empty/  tokens
```

## What must be true when this is done

1. **The default is unchanged.** Someone who does nothing sees what they see today, where it is
   today. No migration runs on its own.
2. The panel **shows** the directory in use and can **produce** the setting for a new one, on
   Windows, macOS and Linux.
3. **The pointer cannot live inside the data directory** — it must be read before the directory is
   known. It goes where the other startup values already go: the MCP client entry, beside
   `COAI_CALLER_SESSION` and the vault key. The panel's job is to produce the exact line and say
   where to paste it, as *Install the MCP server…* already does for the config block.
4. **Two sides pointed at one location never write to each other's files.** Each resolves to its own
   subdirectory, derived from the side the panel already knows (`PanelState.side`, the Windows/WSL
   distinction the per-side token work introduced).
5. **Moving existing data is a deliberate act**, offered and never automatic, and it does not delete
   anything until the copy has been read back.
6. A side that finds its subdirectory empty **starts clean rather than failing** — that is the normal
   first move, not an error.

## Open questions for the plan round

- What names the side in the path? `PanelState.side` exists for the panel; the SERVER needs the same
  answer independently, since it is the process that opens the database. Is there one value both
  halves already agree on, or does this need one?
- macOS and Linux have no Windows/WSL split — is the side then the hostname, or a single directory?
  A NAS shared between two Macs raises the same question the operator raised about WSL.
- Does the panel offer to COPY the existing directory into the new place, or only tell you where to
  put it? Requirement 5 allows either; the cheaper one may be enough.

## Test plan (RED first)

| # | Test | RED symptom expected |
|---|---|---|
| 1 | `src_mcp/tests`: with no override, the resolved directory is exactly today's — asserted against the current default, so the "nothing moves" promise is a test rather than a sentence | (guard; green before and after) |
| 2 | `src_mcp/tests`: two sides given the SAME `COAI_DATA_DIR` resolve to different subdirectories, and neither's database path equals the other's | both resolve to one file and two processes write one database |
| 3 | `src_mcp/tests`: a side whose subdirectory does not exist yet starts clean — the directory is created, the database opens, nothing throws | it throws on a missing path |
| 4 | `src_mcp/tests`: the token path stays inside the side's own subdirectory, so one side's sign-in is never visible to the other | tokens share a directory |
| 5 | `panelView.test.ts`: the panel shows the directory in use and the line to paste, and says plainly that the default has not moved | nothing is shown |

Run: `dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4`, then the MTP executable — never
`dotnet test`. Extension side: `cd src_vs_code && npm test`.

## Definition of Done

- [ ] Every test above written RED first, with its failure message recorded.
- [ ] The three open questions answered IN the plan round, and the answers written into this file.
- [ ] Both suites green, counts reported in the pull request.
- [ ] The diff through the `coai` plan and code rounds, every finding resolved.
- [ ] `research/module_server.md`, `research/module_extension.md`, `CHANGELOG.md` updated.
- [ ] This plan promoted to `research/` with `IMPLEMENTED` and the date.
