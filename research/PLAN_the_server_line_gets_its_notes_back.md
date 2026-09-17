# PLAN — The Server line gets its notes back, and cannot lose them again

> Status: **IMPLEMENTED, 2026-09-17.** Scope: `src_vs_code/CHANGELOG.md` (eight backfilled
> `## Server` entries), `.github/scripts/changelog-names-the-release.mjs`,
> `.github/changelog-baseline.json`, `src_vs_code/src/test/changelogNamesTheRelease.test.ts`, and one
> step in the `mcp-draft` job of `.github/workflows/release.yml`. No product code.
>
> Related docs: [module_tests.md](module_tests.md).

## The symptom, measured — and the measurement was wrong twice before it was right

**First telling (wrong).** I told the operator the `## Server` notes "had fallen behind". They had
never been complete: 10 entries against 65 tags is not a line that slipped, it is a line that was
never kept.

**Second telling (also wrong, and it cost real work).** The plan counted `^## Server` headings and
reported 67 tags against 10 entries, 57 missing. That counts ONE of the three shapes this changelog
uses, over a stale local tag list. The first implementation pass then backfilled thirteen releases on
the strength of it, and five of those thirteen were defects — see *Deviations*.

**The measurement that holds**, across every shape, with the tags fetched rather than whatever the
local checkout happened to hold:

| | |
|---|---|
| `mcp-v*` tags | **65** |
| documented somewhere | **24** |
| — as `## Server 0.18.14 — <date>` | 10 |
| — as `## Extension 0.32.3 · Server 0.18.17 — <date>` | 3 |
| — as `## 0.31.0 — <date> (server 0.18.3)` | 15 |
| with no note at all | **41** |

(The three rows overlap: 28 headings naming 24 distinct versions.)

**The defect is the shape of the gap, not its size.** Of the 41, thirty-three are below 0.18.15 and
predate anyone installing this component on purpose. The other **eight are consecutive and recent** —
0.19.0 through 0.25.0, and 0.27.1 — and what shipped in them is not maintenance:

- a person's own review roles, composed onto the shipped ones (`COAI_ROLES`);
- `review_document` — a whole gate;
- `consult` — the whole consultant, eight stories;
- the round knowing which AI called it, and which model it declared;
- `coai-bugs` and the defect collector;
- the data directory partitioned per side.

A reader of this changelog was told nothing about any of it. The GitHub release bodies do not help:
every `mcp-v*` release carries the same fixed sentence, *"Native AOT builds of the ConnectOtherAIs MCP
server."* The information existed only in the commit log.

## What shipped

1. **Eight entries**, 0.19.0 → 0.27.1, each written from that release's own commits and dated from its
   tag.
2. **Each says it is a backfill** — reconstructed on 2026-09-17 from the commit record, *"not what
   somebody wrote while shipping it"*. A reconstruction presented as contemporaneous is a confidently
   wrong record, which is worse than a silent one.
3. **A guard**, `.github/scripts/changelog-names-the-release.mjs`, wired into `mcp-draft` after the
   checkout and before the draft.
4. **A baseline**, `.github/changelog-baseline.json` — 32 versions, every one of which has both a note
   and a tag. The guard reads it at release time, so the line cannot lose a note it already has.
5. **The 0.1.0–0.18.14 gap is accepted and recorded**, with its reason.
6. **`extension-v*` and `server-v*` stay unguarded**, deliberately, as a `guarded: false` row each.

## Deviations from the plan as written

**Thirteen entries became eight, and the five that went were defects of three different kinds.** This
is the most valuable part of the record, because every one of them survived a plan round, an
implementation and a twelve-reviewer code round without being noticed:

- **0.18.15, 0.18.16 and 0.18.17 were already documented** — contemporaneously, and better — by the
  joint `## Extension N · Server N` headings of the extension releases that carried them. My
  reconstructions said the same thing in thinner words. They were duplicates, and the only reason no
  test saw them is that the tests counted the same single shape the plan did.
- **0.26.0 and 0.27.0 were never released.** The versions exist in the manifest; `mcp-v0.26.0` and
  `mcp-v0.27.0` do not exist as tags. I wrote release notes for two releases nobody can install —
  which is worse than a gap, because it reads as installable. This is the trap already recorded as
  *a release is a TAG, not a package.json bump*, walked into while building a guard against it.
- **0.27.1's entry was wrong about its own content.** It said *"a re-release, and nothing in the
  server changed"*. `mcp-v0.27.1` carries **29 `src_mcp` commits** — the whole of `coai-bugs`, the
  collector, the review page, the Bugz panel section — precisely because 0.26.0 and 0.27.0 were never
  tagged. It is rewritten to say so.

**The guard gained a baseline, which the plan argued against.** The plan claimed the guard "needs no
baseline list: it only ever examines the tag being released. History cannot redden it." That is true,
and it is also the hole: a release that deletes an older heading passes such a guard. Two reviewers
said so independently. The ratchet closes behind each release — shipping a version means writing its
entry *and* adding it to the baseline, and the refusal says both.

**The matcher had to learn all three heading shapes**, for the same reason the measurement was wrong.
A guard that only knows `## Server X` would call fifteen documented releases undocumented.

**Version matching was wrong in a way the plan's own test table did not cover.** The plan specified
that a version must not be satisfied by a prefix (`0.2.0` by `0.28.0` or by `0.2.0.1`), and the first
implementation used `(?![\d.])`, which does exactly that — and accepts `0.28.0rc1`, `0.28.0-beta.1`
and `0.28.0x`, because a letter is neither a digit nor a dot. The condition is `(?=\s|$)`.

**One registry instead of two lists.** The first implementation kept `LINES` and a separate
`UNGUARDED` array consulted *first*, while its own header told the next person that guarding another
line was "one row in `LINES`" — which would have been silently dead. `guarded` is a field now, and a
test reads the registry back through `--lines`.

**The tag reaches the guard through the step environment**, not `"${{ github.ref_name }}"` inside the
command. A `${{ }}` expression is pasted into the shell source before bash parses it, and a git ref
may legitimately contain `"`, `$`, `;` and a backtick — none of them on git's forbidden list.

## Test plan, as built

| What | Where | Teeth |
|---|---|---|
| a tag with no entry is REFUSED, naming the version and the heading | `changelogNamesTheRelease.test.ts` | asserts exit **1** exactly, not "non-zero" |
| a tag with an entry passes | same | the guard is seen to open as well as to close |
| `## Team server 0.28.0` does not satisfy `mcp-v0.28.0` | same | three lines share one file |
| `0.2.0` is satisfied by neither `0.28.0` nor `0.2.0.1` | same | the trap a naive `includes` walks into |
| `0.28.0rc1`, `0.28.0-beta.1`, `0.28.0x` do not satisfy `0.28.0` | same | what `(?![\d.])` let through |
| the joint and parenthesised forms DO satisfy it | same | the misreading that produced three duplicate entries |
| a release that deletes an older note is refused | same | the promise the plan made and the first guard could not keep |
| a release absent from the baseline is refused | same | the ratchet has to close behind each release |
| every line is declared once, guarded or not | same, via `--lines` | a line cannot be guarded and bypassed at once |
| every baseline version still has its note | same, against the REAL changelog | this is what a deletion reddens |
| no baseline version was never tagged | same, against `git tag` | the 0.26.0 / 0.27.0 defect, made loud |
| a malformed tag, and an unreadable changelog | same | assert exit **2**; a guard that cannot check must not pass |
| the `mcp-draft` job calls it, after checkout and before the draft | same | a script nothing invokes guards nothing |
| the tag arrives by env, never interpolated | same | script injection on the release runner |

RED observed before the script existed: **15 failing, 4 passing**. GREEN after: **19 / 19**.

## Definition of Done

- [x] Eight `## Server` entries, 0.19.0 → 0.27.1, each from its own commits, none inventing a
      behaviour the commits do not support, none duplicating an entry that already existed, and none
      for a version that was never tagged.
- [x] The backfill says it is a backfill, with its date and its source.
- [x] The guard refuses a tag with no entry and is proved to refuse, not only to pass.
- [x] It is wired into `mcp-draft` before anything is created, and a test asserts the wiring.
- [x] Prefix, suffix and wrong-line collisions are covered.
- [x] A release cannot delete a note the line already has.
- [x] The accepted 0.1.0–0.18.14 gap is recorded with its reason.
- [x] `npm test` green; family checks green; the MTP count unchanged.

## The open tail

- **`extension-v*` and `server-v*` are unguarded**, with gaps of 51 and 5. Guarding them is one
  `guarded: true` and a baseline each — but it is a policy decision about those lines, and their gaps
  are the ordinary kind (a patch release beside a documented one) rather than this one's.
- **Thirty-three releases below 0.18.15 have no note and will not get one.** Reconstructing them from
  months-old commits would produce prose nobody can check. A note written for one of them is welcome;
  the baseline simply does not require it.
