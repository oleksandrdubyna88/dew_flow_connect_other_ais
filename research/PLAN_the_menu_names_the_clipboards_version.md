# PLAN — The ⋯ menu names the version of what it will actually give you

> Status: **IMPLEMENTED, 2026-09-14.** Scope: `src_vs_code/src/claudeSnippet.ts`, the command title in
> `src_vs_code/package.json`, the snippet tests, and the drifted section of
> `research/module_extension.md`. No server change, no change to any rule TEXT.
>
> Related docs: [module_extension.md](module_extension.md),
> [PLAN_a_mounting_repository_is_told_to_paste_the_gate_again.md](../todo/PLAN_a_mounting_repository_is_told_to_paste_the_gate_again.md)
> (the open tail — the boundary is named in both).

## The symptom

The ⋯ menu said **“Copy the CLAUDE.md snippet (v5)”**. It had said v5 since the label was introduced,
and the artefact that lands on the clipboard had changed **three times** since: the document half, the
caller half, and — on 2026-09-14 — the consultant half. A person looking at that menu had no way to
learn that the text they pasted a week ago was not what the menu would give them now, which is the
exact defect the label was added to fix. The test that pins the label says so in its own header
(`src_vs_code/src/test/snippetVersionIsVisible.test.ts:11`):

> *A version only the source code knows is a version nobody has. Reported by the operator, looking at
> the ⋯ menu: the snippet had been at v5 for a release and the menu said “Copy the CLAUDE.md snippet” —
> so a person with v4 pasted in their repository had no way to learn there was anything newer, and no
> reason to click.*

Reported again by the operator on 2026-09-14, in those terms: *if the snippet changed, the version
should have gone up — v6.*

### Why it stalled, and why it was not a bump of `SNIPPET_VERSION`

`SNIPPET_VERSION` is **not the version of the clipboard artefact**. It is the marker written inside
`common/coai-review-gate.md`, one of the 24 rule bodies the conventions repository hashes against its
migration baseline — so that file cannot be edited and its number cannot be raised. This is deliberate
and correct, and the SHA guard says it in as many words: *“A change that arrives as a WHOLE NEW half
brings its own marker and raises neither.”*

Detection had already moved to **per-half markers**, and that half worked: a paste missing
`<!-- coai-consultant v1 -->` was already reported `older` by its absence. What was never moved is the
**number a person is shown**, which was still the frozen one, in three places: the menu title
(`package.json`), the panel's stale-copy note and the message after the click.

**And the note was self-contradicting.** For the six family repositories that mount the gate rule,
`snippetStatus` returned `{ kind: 'older', found: 5, current: 5 }` — the gate marker current, three
halves missing — so the panel printed *“The CLAUDE.md snippet in this workspace is v5; v5 is
current.”* Two tests pinned exactly that.

**Raising `SNIPPET_VERSION` to 6 was the one fix that could not work.** The constant must equal the
marker in the text it describes: `claudeSnippet()` carries `coai-snippet v5` from the frozen gate body,
so a constant of 6 makes `snippetStatus(claudeSnippet())` answer `older` — the panel would tell a
repository with a perfectly current paste to replace it.

## What shipped

1. **`ARTEFACT_VERSION = 6`** in `claudeSnippet.ts` — an ordinal for the composed paste, sequential
   after the 5 the menu had been showing for the whole artefact. It numbers a different thing from
   `SNIPPET_VERSION`, which stays 5 and stays the gate rule's own marker; the docblock says so, at
   length, because two numbers differing by one invite a future reader to “tidy” them together.
2. **The manifest title is `Copy the CLAUDE.md snippet (v6)`**, pinned to `ARTEFACT_VERSION` by
   `snippetVersionIsVisible.test.ts`, whose failure now names the value to type.
3. **The SHA guard forces it up.** `snippetVersion.test.ts`'s hash test fails on any change to the
   artefact text — a new half included — and its message names `ARTEFACT_VERSION` and the static
   `package.json` title alongside `SNIPPET_BODY_SHA`.
4. **No sentence prints a number for a paste any more.** `SnippetStatus` lost `found` and gained
   `behind` / `newer`: the note and the notification name the halves that are missing, behind or
   newer, and the only number they give is the one the ⋯ menu hands out.
5. **One `HALVES` table** replaced four marker regexes and four near-identical readers; the marker is
   built from the id. The four exported `*VersionIn` readers stayed as thin wrappers, so no caller
   changed.
6. **`halvesIn` + `HALF_IDS`**, so a test can compare the halves the artefact carries with the halves
   the reader knows — a fifth half added without a row is now a red build.

## Deviations from the plan as written

- **The number was going to be DERIVED, by summing the four half markers (5+1+1+1 = 8).** All three
  reviewers of the plan round refused it, on three different grounds, and they were right: duplicate
  blocks in one `CLAUDE.md` sum together, so a person who pastes without deleting the old copy would
  have been told their v12 was behind our v8; the sum collides for a paste newer in one half and
  missing another, which is precisely the case the design claimed to handle; and the guard proposed for
  it (`artefactVersionIn(claudeSnippet()) === ARTEFACT_VERSION`) compared the constant with the
  expression it was assigned from. A declared ordinal plus the existing SHA guard does the job the
  derivation was reaching for, and the operator's own “v6” turned out to be the right number.
- **`found` was removed from `SnippetStatus` entirely**, which the plan did not ask for. Once the sum
  was gone there was no true artefact number to read out of a paste at all, and printing the gate
  half's frozen 5 was the original lie. Two reviewers had asked for per-half reporting in the diverged
  states; that is what replaced it.
- **The `HALVES` table was not in the plan.** It is what makes the half-list guard possible — the
  reader had no list of halves to compare anything against — and it removed four copies of one regex.
- **Two Blocking findings were rejected**, both asking for the `package.json` title to be generated so
  the bump could not be manual. A VS Code command title is static JSON the editor reads before any of
  our code runs, and the `%nls%` indirection only moves the literal to another static file; generating
  it in `prepare-gate.mjs` would put a second implementation of the version decision in the build
  script. The third finding on the same point — *acknowledge the bump is manual and document the guard
  that enforces it* — was accepted instead, and that acknowledgment is in the docblock.
- **The “half at v0” finding was taken in its cheap form**: a test asserting every half's marker is
  ≥ 1, rather than the proposed restructuring of the version into a tuple.
- **`POST_DEPLOY.md` was not touched, deliberately.** Item 11 already requires the installed extension
  to report a current snippet as current and an older copy as older, and the file is at its twelve-item
  cap.

## What the CODE round changed (verdict `proceed`, all 12 reviewers)

The gate passed, and four of its findings were real enough to reopen the code:

- **A half this build has never heard of was ignored.** A repository pasted from a NEWER extension
  carries our four halves plus its own; comparing only the halves we know about answered `current`,
  the panel said nothing, and a copy would have deleted a rule the person's newer build put there.
  Unknown `coai-*` ids are reported as newer now, by their id, because a build that cannot name a rule
  cannot tell anybody to paste over it. (Raised by one vendor in three roles.)
- **A second block was invisible in one direction and blessed in the other.** The gate's reviewer
  showed that taking the first match hides a newer block appended below a current one; my own
  reviewer showed that taking the last match blesses a stale block sitting ABOVE a fresh paste — the
  one the AI in that repository reads first. The two findings pull opposite ways, so a half is now
  compared at its LOWEST version for *behind* and its HIGHEST for *newer*, and both are true of the
  same file. Never a sum: two copies of v1 are still v1.
- **`SnippetStatus` carried English prose.** `behind`/`newer` held display names, so a consumer had to
  match on sentences and a copy edit was a contract change. They carry half ids; the two message
  functions map ids to names, and an unknown id is named by its id.
- **The precedence had nothing pinning it, and neither did first-match.** Swapping the two filters
  left the whole suite green, because every `ahead` fixture was otherwise complete; and the
  duplicate-block test compared two IDENTICAL copies, which first, last and max all satisfy. Both now
  have fixtures that distinguish them.

Two more, from the conventions review: the hash guard **retyped the four markers** it strips (it
derives them now) and its instruction named two of the three raisable halves — `CONSULTANT_VERSION`
was missing — so that sentence is built from `KNOWN_HALVES.filter(h => !h.frozen)`. `frozen` is the
gate half's own field rather than prose in four docblocks. `claudeSnippet()` composes from the table
as well, because the docblock claimed to be the only place that knew there were four while the
composer named them again one screen below; and `documentVersionIn` / `consultantVersionIn`, which had
no callers at all, are gone.

**And a bug of my own, caught by the suite rather than by review:** writing *behind* as a `Math.min`
over the versions present made an ABSENT half answer `Infinity`, so a paste carrying only the gate
rule read as `current` — the exact case this change exists for. The named `isBehind` predicate says
absent-counts-as-behind in one place, with the incident in its docblock.

## The open tail

[PLAN_a_mounting_repository_is_told_to_paste_the_gate_again.md](../todo/PLAN_a_mounting_repository_is_told_to_paste_the_gate_again.md)
stays open, and the boundary is written into it as a table. The sentence a mounting repository sees is
now accurate about WHAT it is missing and still wrong about what to DO: it is told to *replace the old
block* when there is no block, and the clipboard still carries the gate half its submodule provides.
This change took no decision about mount awareness in either direction.

## Evidence

- **RED, the label guard:** *the menu says "Copy the CLAUDE.md snippet (v5)" while the artefact it
  copies is v6 — a version in the code and not in the menu is one nobody can act on.*
- **RED, the note:** *the note invents a version for a paste that carries no artefact number: "The
  CLAUDE.md snippet in this workspace is v5; v5 is current. Copy it again from the ⋯ menu and replace
  the old block…"*
- **RED, the two the code round found:** *actual: 'current', expected: 'ahead'* — twice: once for a
  paste carrying a half this build has never heard of, once for a newer half in a second block.
- **GREEN:** the whole extension suite, 2678 passed, 0 failed, 1 skipped (plus the 7 pre-compile
  tests), after rebasing onto `origin/main` at `4fe3cb02`.
- **Teeth:** putting `(v5)` back in the manifest turns the label guard red naming v6; restoring it is
  green again.
