# PLAN — the native test's PATH folders never collide on a random name

> Status: **IMPLEMENTED, 2026-11-15** (PR #996, squash 5e25da77). Written as the coai plan round's text and recorded here
> after the merge; it never sat in `todo/` because the whole change was built and merged on the day it was planned.
> Tests: [module_tests.md](module_tests.md), *The PATH folders are owned folders (2026-11-15)*.

## Symptom

CI job "build · test · family checks" failed once on PR #989 (a docs-only change, commit a8760d13, run 43696858528):
`not ok 446 - [Linux only …] G4: an EMPTY entry is named "empty (= the current directory)" …` with
`EEXIST: file already exists, mkdir '/tmp/testsToDelete-u1001-coai-run-ZfBioH/testsToDelete-u1001-coai-native-test-tKzSby/wide-5144'`.
A re-run of the job passed.

Cause: `scripts/native/native.test.mjs`, `pathDirs()`, made two folders in the file's one shared ROOT (made once per file
with `wrap.ownedDir(scope, 'native-test', …)`) as `clean-<2 random bytes>` and `wide-<2 random bytes>` with a bare
`mkdirSync` and no retry. 2 random bytes are 65 536 names; `pathDirs()` runs about 18 times per file run, and every folder
lives until the suite's cleanup — a collision in roughly 0.2 % (`wide-`) to 0.5 % (either prefix) of runs, failing
whichever test called `pathDirs()` for a reason unrelated to that test.

## Change

- `scripts/native/native.test.mjs:381` — `pathDirs()` makes both folders with
  `wrap.ownedDir(scope, 'clean' | 'wide', { parent: ROOT })`. The helper (`.agents/conventions/tools/lib/owned.mjs`,
  `dirFor`) claims a name exclusively, draws again when it is taken, writes its claim as `<dir>.owner` BESIDE the folder
  and registers removal on the same `scope`. The modes are set by `chmodSync` as before: 0755 `clean`, 0777 `wide`.
- `scripts/native/native.test.mjs:392` — a regression case: 1000 `pathDirs()` calls in one ROOT give 2000 distinct
  folders, `clean` 0755 holding only `node`, `wide` 0777.
- `research/module_tests.md` — a paragraph and a row for the change.
- `shared/leftovers-exemptions.json:23` — the `legacy-name` row of the file moved from line 507 to 523.

Not changed, deliberately: the other `randomBytes(2)` names in the file (`bareSeries()` must match `SERIES_SHAPE` in
`scripts/lib/native-run.mjs`; the rest name links, markers or deliberately missing paths).

## What had to be true

1. `pathDirs()` cannot fail with EEXIST because of a name it drew itself.
2. Both folders keep modes 0755 / 0777, and `clean` holds only the `node` link.
3. The same scope removes them; no folder or claim is left.
4. The file's tests and the whole scripts suite pass; the CI leftovers check passes.

## Deviations — what shipped differently from the plan

1. **A committed regression test.** The plan round's finding 0 (force a collision) was rejected: the retry is the
   helper's, tested in `owned.test.mjs` test 16, and forcing it in this file would need a test-only seam. The code round
   overturned that with the testing rule ("every bug fix starts with a RED test") and the operator's red-green-red order.
   The shape found then needs no seam: 1000 calls make a collision of the OLD helper all but certain (passing chance about
   3 in ten million). Checked: with the old helper restored under the new case, EEXIST in 3 runs of 3.
2. **The module document.** The plan said "no other file"; the code round's finding 1 (the knowledge-base rule) added
   the `module_tests.md` paragraph and row.
3. **The exemptions inventory.** Not foreseen: `shared/leftovers-exemptions.json` keys each marker by LINE, and the new
   case's 16 lines moved the `legacy-name` site, so N3 (`src_vs_code/src/test/testLeftovers.test.mjs`) failed on the
   macOS, Windows and extension jobs. The row was moved (the site's sha256 unchanged); the whole extension suite then
   ran 7118 tests, 0 fail. The local run before the push had covered `scripts/` only — the extension's leftovers set was
   NOT run then, which is how the miss reached CI.

## Observed

- Before: `native.test.mjs` 65/65. Red by hand (names forced to a constant, not committed): 48 pass, 17 fail with EEXIST.
- After: `native.test.mjs` 66/66 (the new case 213 ms), nothing left in the temp root; the scripts suite 477 tests, 473
  pass, 0 fail, 4 skipped; the extension suite 7118 tests, 7076 pass, 0 fail, 42 skipped.
- coai gate: plan round `proceed` (2 of 2 reviewers, gating 1 of 6); code round `proceed` (8 of 8 reviewers, gating 2 of
  5, both accepted and fixed). The third commit (the inventory row, one number) did not go through a code round: the
  session had closed at `Done`.

## Definition of Done

- [x] `pathDirs()` uses `wrap.ownedDir` for both folders, modes unchanged.
- [x] A retained case that fails with the old helper and passes with the new one.
- [x] The file's tests, the scripts suite and the extension suite pass; no leftovers.
- [x] Code round through the coai gate; PR #996 merged on green checks (owner allowed it over the PR cap, 2026-11-15).
