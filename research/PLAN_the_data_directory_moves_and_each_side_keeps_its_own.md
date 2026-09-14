# PLAN — the data directory can move, and two sides keep their own

> Status: **IMPLEMENTED, 2026-09-13.** `COAI_DATA_SIDE=<name>` partitions a chosen `COAI_DATA_DIR`
> per side, so two installations sharing one location each keep their own database, sessions and
> Team-server tokens. The default is untouched.
>
> **Three deviations, each forced by evidence rather than preference.**
>
> **The partition is OPT-IN.** The plan partitioned every override. Built that way, six of this
> repository's own scenario tests went red — they set `COAI_DATA_DIR` and then read that exact path,
> which is what a script, the bench, and a setting made a year ago also do. Moving their data one
> level down silently is the surprise the plan spends a section refusing.
>
> **The side is a NAME, not derived.** The plan built it from platform + WSL distribution + machine
> name. `src_vs_code/src/dataDir.ts` explains why that is wrong in a docstring older than this
> change: the extension WRITES the Team-server token where the shim READS it, so both halves must
> agree on the path exactly — and deriving means computing one string twice, from
> `Environment.MachineName` and from `os.hostname()`, which differ in case and in whether they carry
> a domain. A chosen name cannot diverge, and it is what the issue asked for.
>
> **A refused side refuses, rather than meaning the root.** The code round returned `revise` on this:
> `COAI_DATA_SIDE=wsl/node1` failed the grammar, was treated as "no side", and put every
> misconfigured installation on the shared root's single database — the corruption the partition
> exists to prevent, reached by a typo. Seven reviewers raised it.
>
> **Not built, and named rather than dropped:** the panel field that shows the directory in use and
> produces the line to paste. The resolution rule is the half that can be silently wrong, and it is
> done; the panel is a sentence and a button, and it is
> [PLAN_the_panel_shows_where_the_data_lives.md](PLAN_the_panel_shows_where_the_data_lives.md).
>
> Related docs: [module_server.md](module_server.md), [module_extension.md](module_extension.md).
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
   subdirectory, from a side the SERVER derives itself — see question 1. Not `PanelState.side`: that
   belongs to the extension, and the process that opens the database must be able to answer without
   asking the panel.
5. **Moving existing data is a deliberate act**, offered and never automatic, and it does not delete
   anything until the copy has been read back.
6. A side that finds its subdirectory empty **starts clean rather than failing** — that is the normal
   first move, not an error.

## The three questions, answered

### 1. What names a side — `<platform>[-<wsl distro>]-<machine>`

The server has **no** side concept today: `grep` for one finds only prose. `PanelState.side` belongs
to the extension, and the server is the process that opens the database, so it needs an answer it can
reach on its own without asking the panel.

**`RuntimeInformation` platform + `Environment.MachineName`**, lower-cased and path-safe:
`windows-desktop01`, `linux-desktop01`, `osx-macbook-a`. Both are available on every platform and
need no configuration.

The machine name **alone** is not enough, and this is the case the operator actually has: WSL takes
its hostname from the Windows host by default, so `MachineName` is the SAME string on both sides of
one box. The platform is what separates them — WSL reports Linux.

**And the platform is not enough either, which the plan round caught**: two WSL distributions on one
host — Ubuntu and Debian, or a work one and a personal one — both report `linux-<hostname>` and would
land in one directory. So `WSL_DISTRO_NAME` joins the name when it is set: `linux-ubuntu-desktop01`.
An empty `MachineName` — possible in a container — falls back to a fixed word rather than producing
a name ending in a dash.

`COAI_DATA_SIDE` overrides the whole thing for anyone who wants to name a side themselves. It is
**validated, not trusted**: no directory separators, no `.`/`..` segments, and the resolved path must
still be under the data root after canonicalisation — otherwise a side called `../shared` escapes the
root it is supposed to partition. It is also what makes the value testable without a second machine.

**A machine rename makes a new side**, and that is worth saying out loud rather than discovering: the
old directory stays where it is, full, and the server starts a new one beside it. The panel shows the
resolved side and path for exactly this reason, and `COAI_DATA_SIDE` is how somebody pins a name that
survives a rename.

### 2. The side subdirectory applies to EVERY override, with no flat-layout fallback

This is what keeps requirement 1 true at one end and requirement 4 true at the other. With no
`COAI_DATA_DIR`, the path is `DefaultDataDir` **exactly as today** — no subdirectory, nothing moves,
nobody has to do anything. The per-side layout is a property of a directory somebody deliberately
chose to share.

**The first draft of this answer had a fallback, and it defeated the whole feature.** It said: if
`<dir>` itself contains `coai.db`, use `<dir>` — so that somebody already overriding the directory
did not find an empty one. Three reviewers, independently, pointed out what that does in the
operator's own scenario: Windows moves its data directory to `//nas/coai/`, so `//nas/coai/coai.db`
exists; WSL is then pointed at `//nas/coai/`, sees the database in the root, adopts the flat layout —
and **both sides write the same SQLite file**, which is the exact thing the plan was written to
prevent, arrived at through the compatibility shim.

So there is no fallback. An override always resolves to `<dir>/<side>`. A database sitting in
`<dir>` itself is **never adopted and never written to**; the server reports it — "there is a
database at `<dir>` in the old flat layout; move it into `<dir>/<side>` to keep its history" — and
carries on with the side directory. Non-destructive, visible, and it cannot produce two sides on one
file. Silence there would make a working configuration look broken, which is the lesson already
written into `PanelSettings.cs` about relative paths.

**A newly created side directory is reported too**, for the same reason: a mistyped NAS path is a
creatable directory, and a person who typo'd it would otherwise quietly accumulate a second history
while believing they were writing to the first.

### 3. The panel tells you where to put it; it does not copy

Requirement 5 allowed either. Telling is enough for the case in the issue — a person who is moving to
a NAS is already moving files — and copying is the half with every interesting failure in it (a
partial copy, a file in use, a database being written to while it is read). It stays out of this
plan rather than being done badly; if it is wanted, it is its own plan with its own verification.

**The panel speaks for the side it is running on, and says so.** A Windows panel shows a Windows path
and a WSL panel shows the WSL one — raised on the plan round, because `\\nas\coai` pasted into a WSL
client entry is not a path that exists there, and `/mnt/z/coai` pasted into a Windows one is not
either. Each side's panel already runs on that side; it produces the line for the side it can see,
labels it with the resolved side name, and does not pretend to know the other one's mount.

And what it tells you to move is named: the database, the session files, `unparseable/` and `empty/`.
Not `worktrees/`, which is scratch pruned on every `open`, and not the token files, which belong to
the side that signed in.

## Test plan (RED first)

| # | Test | RED symptom expected |
|---|---|---|
| 1 | `src_mcp/tests`: with no override the resolved directory is **exactly** `DefaultDataDir` — no side, no subdirectory. The "nothing moves" promise as a test rather than a sentence | (guard; green before and after) |
| 2 | `src_mcp/tests`: two sides given the SAME `COAI_DATA_DIR` resolve to DIFFERENT directories — asserted through the `COAI_DATA_SIDE` override, since one test process cannot be two machines | both resolve to one path, and two processes write one database |
| 3 | `src_mcp/tests`: the side is `<platform>-<machine>` and a machine name shared by two platforms still yields two sides — the WSL case, which a machine-name-only scheme would collapse | there is no side |
| 4 | `src_mcp/tests`: **the scenario the plan round found** — an override whose directory already holds `coai.db` STILL resolves to `<dir>/<side>`, never to `<dir>`. Asserted for two different sides against one root, so the two-sides-one-database case is the test rather than a sentence | the flat database is adopted and both sides share it |
| 5 | `src_mcp/tests`: that flat database is REPORTED — the resolution says it saw one and where to move it — and a newly created side directory is reported too, so a mistyped path is not a silent second history | both are silent |
| 6 | `src_mcp/tests`: `COAI_DATA_SIDE` is validated — a separator, a `..` segment or an empty value is refused, and the resolved path is still under the root after canonicalisation | `../shared` escapes the root it partitions |
| 7 | `src_mcp/tests`: two WSL distributions on one host get two sides — `WSL_DISTRO_NAME` joins the name — and an empty `MachineName` does not produce a name ending in a dash | both distros land in one directory |
| 8 | `src_mcp/tests`: a side whose subdirectory does not exist yet starts clean — created, opened, nothing thrown | it throws on a missing path |
| 9 | `src_mcp/tests`: the Team-server token path lands inside the side's own directory, and the directory it needs exists by the time a token is written | tokens share a directory |
| 10 | `panelView.test.ts`: the panel shows the resolved side and directory, the line to paste for THIS side, what to move and what not to move, and says plainly that the default has not moved | nothing is shown |

Run: `dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4`, then the MTP executable — never
`dotnet test`. Extension side: `cd src_vs_code && npm test`.

## Definition of Done

- [ ] Every test above written RED first, with its failure message recorded.
- [ ] The three open questions answered IN the plan round, and the answers written into this file.
- [ ] Both suites green, counts reported in the pull request.
- [ ] The diff through the `coai` plan and code rounds, every finding resolved.
- [ ] `research/module_server.md`, `research/module_extension.md`, `CHANGELOG.md` updated.
- [ ] This plan promoted to `research/` with `IMPLEMENTED` and the date.
