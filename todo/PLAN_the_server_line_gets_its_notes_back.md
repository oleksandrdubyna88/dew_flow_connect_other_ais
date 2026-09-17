# PLAN — The Server line gets its notes back, and cannot lose them again

> Status: **plan only, nothing implemented yet, 2026-09-17.** Scope: `src_vs_code/CHANGELOG.md`
> (thirteen backfilled `## Server` entries), a new guard script under `.github/scripts/`, its tests,
> and one step in the `mcp-draft` job of `.github/workflows/release.yml`. No product code.

## The symptom, measured — and it is not the one I first reported

Yesterday I told the operator that the `## Server` notes "have fallen behind". That was wrong twice,
and the measurement is the plan:

| Line | Tags | Entries in the changelog | Missing |
|---|---|---|---|
| `mcp-v*` (the MCP binary, called **Server** in this file) | 67 | 10 | **57** |
| `extension-v*` | 90 | 39 | 51 |
| `server-v*` (the Team server) | 6 | 1 | 5 |

114 headings against 163 tags. **The changelog has never been a per-release record in any line** — it
documents a release when there is something to tell a person, and that is a legitimate convention
nobody wrote down. `Extension 0.43.0` is absent while `0.43.1` is present; that is the ordinary shape
of the gaps in that line.

**The MCP line is different in kind, and that is the actual defect.** Nine of its ten entries are one
dense run between 0.17.2 and 0.18.14 (2026-09-05 to 09-09); the tenth is 0.28.0, written yesterday.
Between them sit **thirteen releases, 172 commits in `src_mcp`, 148 of them `feat` or `fix`** — and
what shipped in that gap is not maintenance:

- a person's own review roles, composed onto the shipped ones (`COAI_ROLES`);
- `review_document` — a whole gate;
- `consult` — the whole consultant, eight stories;
- the round knowing which AI called it, and which model it declared;
- `coai-bugs` and the defect collector;
- the data directory partitioned per side.

A reader of this changelog is told nothing about any of it. The GitHub release bodies do not help:
every `mcp-v*` release carries the same fixed sentence, *"Native AOT builds of the ConnectOtherAIs MCP
server."* The information exists only in the commit log.

## What this plan does, and what it deliberately does not

1. **Backfills thirteen entries**, 0.18.15 through 0.27.1 — from the last existing entry forward, so
   the record becomes continuous from a defensible point rather than arbitrarily.
2. **Says out loud that they are a backfill.** One banner above the block: written on 2026-09-17 from
   the commit record, not at release time. A reconstructed note presented as contemporaneous is a
   confidently wrong record, which is worse than a silent one.
3. **Adds a guard so the line cannot lose them again** — a release that has no entry does not ship.
4. **Leaves the 0.1.0–0.18.14 gap alone**, recorded as accepted: those releases pre-date anyone
   installing this component deliberately, the notes began at 0.17.2, and reconstructing fifty of them
   from months-old commits would produce prose nobody asked for and nobody can check.
5. **Leaves `extension-v*` and `server-v*` alone.** Their gaps are real but different in kind, and
   putting a guard on them is a policy change for release lines this task was not asked about. The
   guard below is one argument away from covering them when somebody decides it should.

## The guard

`.github/scripts/changelog-names-the-release.mjs` — given a tag, it finds the heading that tag
requires and refuses when the changelog does not carry it.

**Wired as the FIRST step of `mcp-draft`**, which is the first job an `mcp-v*` tag runs. A refusal
there costs nothing: no draft exists yet, no asset has been uploaded, and the tag can be deleted and
re-pushed after the entry is written.

**It is inherently forward-looking**, which is why it needs no baseline list: it only ever examines
the tag being released. History cannot redden it.

**Written in Node rather than bash, unlike its three siblings in that folder, and the reason is the
one `promote-release.mjs` gives in the conventions repository:** a gate whose refusals cannot be
exercised by a test is a gate nobody has seen close. The siblings are bash because they drive `gh`;
this one is a pure text check over a file, so it can be imported and run by the extension suite —
both the refusal and the pass.

## Test plan

| What | Where | Teeth |
|---|---|---|
| a tag whose version has no `## Server` heading is REFUSED, naming the version and the file | new cases in `src_vs_code/src/test/` | the refusal is the point; assert the message names the version |
| a tag whose version HAS one passes | same | so the guard can be seen to open as well as close |
| a heading for a different line does not satisfy it — `## Team server 0.28.0` must not pass an `mcp-v0.28.0` | same | the three lines share one file and one number can exist in two of them |
| a version that is a PREFIX of another does not satisfy it — `0.2.0` must not be matched by `## Server 0.2.0.1` or `0.28.0` | same | the trap a naive `includes` walks into |
| the `mcp-draft` job actually calls it | the existing workflow-reading assertions in `install.test.ts` | a script nothing invokes guards nothing |

The RED for each is observed before the script exists, and reported.

## Build order

1. The thirteen entries, oldest first, each derived from that release's own commits and dated from its
   tag. `0.27.1` is a re-release — its only change is `fix(release): the release line never checked
   out the rules it tests against` — and the entry says so rather than inventing behaviour.
2. The banner naming the block as a backfill.
3. RED: the guard's test cases, against a script that does not exist yet.
4. The script, then the cases green.
5. The workflow step, and the assertion that the job calls it.
6. `npm test`, then the family checks, then the MTP executable — nothing here touches `src_mcp`, so
   its count must not move.
7. Commit by path; PR; the gate's code round.

## Definition of Done

- [ ] Thirteen `## Server` entries, 0.18.15 → 0.27.1, each from its own commits, none inventing a
      behaviour the commits do not support.
- [ ] The backfill says it is a backfill, with its date and its source.
- [ ] The guard refuses a tag with no entry and is proved to refuse, not only to pass.
- [ ] It is wired into `mcp-draft` before anything is created, and a test asserts the wiring.
- [ ] Prefix and wrong-line collisions are covered.
- [ ] The accepted 0.1.0–0.18.14 gap is recorded with its reason.
- [ ] `npm test` green; family checks green; the MTP count unchanged.
