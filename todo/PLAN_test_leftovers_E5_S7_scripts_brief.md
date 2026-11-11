# Build brief — coai E5 story S7, the second drain PR: the `scripts/` suite's `raw-temp` entries to zero, and the native test's colliding folder names

> Status: **plan only, 2026-11-11, by Opus.** Scope: the 10 `raw-temp` baseline entries of `scripts/`, one read-only
> export of the Node helper, and the 2-random-byte folder names of `scripts/native/native.test.mjs`. A Sonnet coder
> builds from this brief alone.

The plan is [PLAN_test_leftovers_E5.md](PLAN_test_leftovers_E5.md) § *S7 — The drain*; the first drain PR was #977
([its brief](PLAN_test_leftovers_E5_S7_bench_brief.md)). The parent is `dew_flow_conventions ·
todo/PLAN_test_leftovers_rollout.md`, § *Epoch 8*, rows S7-b to S7-e. Rule: `.agents/conventions/common/test-leftovers.md`
§2 (the helper; never at module load) and `common/test-leftovers-baseline.md`. Counts measured at `origin/main`
`e1b63958`, conventions pin `09db328`.

## 1. Goal

1. **The `scripts/` suite drains (10 → 0; the file 770 → 760).** Each site moves to the helper door the suite already
   uses (`wrap.ownedDir(scope, <what>, { parent: realpathSync.native(wrap.tempRoot()) })`, `wrap.tempRoot()`; `wrap` is
   `owned` of `src_vs_code/scripts/owned.mjs`), and its entry leaves the baseline in the same commit. The `scripts/`
   scope stays, with zero entries (as the bench scope did in #977).
2. **The native test's folder names stop colliding.** Found on #977's CI (job 130615928516, 2026-11-11):
   `not ok 451 … G13 …` with `EEXIST: file already exists, mkdir '…/testsToDelete-u1001-coai-native-test-krDJ71/wide-58de'`.
   `scripts/native/native.test.mjs` names 11 kinds of folder inside its one owned `ROOT` with 2 random bytes
   (`randomBytes(2)`, 65,536 values: lines 120, 331, 332, 380, 381, 457, 937, 946, 955, 986, 1000) and creates them with a
   plain `mkdirSync`, so two draws of one stem in one run collide by chance (`pathDirs()` alone runs 15 times). Not a
   flake: a shared-namespace defect (`common/testing.md`, *Passes alone, fails in the suite*). It is in this suite, in
   the same kind of code (test scratch naming), so it is folded here rather than left for a separate PR.

## 2. What must be true when it is done

1. `raw-temp-baseline.json` holds no entry whose `file` starts with `scripts/`; 760 entries remain; no entry added or
   re-keyed; the `scripts/` scope and its proof unchanged.
2. The shipped Node guard with `--baseline` is clean; `--baseline-shrinks --base origin/main` passes; no new
   `testsToDelete-exempt` marker; the inventory (`shared/leftovers-exemptions.json`) unchanged (S7-e).
3. No folder in `scripts/**/*.test.mjs` is made at module load (`transcript-sanitise.test.mjs:22` today).
4. The proof file `scripts/runFolder.test.mjs` still asserts, in `P-T`, that EACH of the three temp variables and the
   process's temp root name the run folder — read through the helper, not spelled.
5. `native.test.mjs` draws every scratch name from ONE function that cannot return the same path twice in a run, and a
   test proves it (RED on the 2-byte draw by pigeonhole, GREEN after).
6. `node scripts/run-lib-tests.mjs` (the CI command, `ci.yml:226`) is green on Linux with the same pass count + the one
   new test, no new skip.

## 3. Constraints

- No change to the run-folder gate, `inRunFolder.cjs`, `beginOwnedRun`, the self-test seam, the proof marker or the
  proof's child runs P-1..P-5 (the baseline rule's last *Never*).
- The ONE helper change allowed: `src_vs_code/scripts/owned.mjs` gains
  `export const TEMP_VARIABLES = Object.freeze(Object.values(MISMATCH));` beside `MISMATCH` (line 61) — the three
  variable names the gate already holds, exported read-only so the proof can iterate them. Its line carries no temp
  spelling, so it adds no guard finding and no exemption.
- No product code changes. Every test keeps its name and its assertions. Names that a test passes to the code under test
  keep their meaning (the old-coai-leftovers CLI scans a root it is given; that root's own name is not read by any
  assertion — check before relying on it, § 4).

## 4. The sites

| # | File:line | Today | After |
|---|---|---|---|
| 1 | `scripts/lib/kimi.test.mjs:105` (`homeWith`) | `const home = mkdtempSync(join(base, \`${name}-\`));` — `base` is already the file's owned folder | `const home = join(base, \`${name}-${++homes}\`); mkdirSync(home);` with one file-level `let homes = 0;` — unique by counter inside the owned folder, created exclusively |
| 2 | `scripts/lib/kimi.test.mjs:287` (`fakeKimiHome`) | the same shape | the same counter and `mkdirSync`; drop `mkdtempSync` from the import if unused |
| 3 | `scripts/lib/old-coai-leftovers.test.mjs:39` (`privateRoot`) | `fs.realpathSync.native(fs.mkdtempSync(path.join(os.tmpdir(), prefix)))` + `t.after(rmSync)` | `fs.realpathSync.native(wrap.ownedDir(t, what, { parent: fs.realpathSync.native(wrap.tempRoot()) }))`, where the parameter becomes `what` (`'old-coai-leftovers'`, `'old-coai-start'`): `ownedDir` registers its own removal on `t`, so the `t.after(rmSync)` goes. Import `owned as wrap` from `../../src_vs_code/scripts/owned.mjs`. Before changing it, read every caller: if any assertion reads the root's NAME (not its contents), stop and report |
| 4 | `scripts/lib/old-coai-leftovers.test.mjs:642` (S28) | `--out path.join(os.tmpdir(), 'never-written-listing.json')` | `const root = privateRoot(t, 'old-coai-start'); const missing = path.join(root, 'no-such-root');` and `--out path.join(root, 'never-written-listing.json')` — the file is never written (the test's own claim), now inside an owned folder either way |
| 5 | `scripts/lib/transcript-sanitise.test.mjs:22` | `const base = mkdtempSync(join(realpathSync.native(tmpdir()), 'transcript-sanitise-test-'));` at MODULE LOAD, `after(rmSync)` | the `kimi.test.mjs:16-20` shape: `let base; const scope = wrap.cleanupScope(); before(() => { base = wrap.ownedDir(scope, 'transcript-sanitise', { parent: realpathSync.native(wrap.tempRoot()) }); }); after(() => scope.close());` — `scratch()` reads `base` lazily, so it keeps working. Drop `mkdtempSync`, `tmpdir`, `rmSync` imports if unused |
| 6 | `scripts/run-lib-tests.test.mjs:46` (`privateRoot`) | `fs.mkdtempSync(path.join(os.tmpdir(), 'run-lib-tests-'))` + `t.after(rmSync)` | `wrap.ownedDir(t, 'run-lib-tests', { parent: fs.realpathSync.native(wrap.tempRoot()) })`. The tests plant `testsToDelete-<owner>-coai-…` folders in this root and point a child run's temp root at it: check that nothing walks UP from the root (a sweep given `--tmp <root>` reads only that root); if one does, stop and report |
| 7–10 | `scripts/runFolder.test.mjs:30-33` (`P-T`, the proof) | `assert.equal(process.env.TEMP, folder); …TMP…; …TMPDIR…; assert.equal(os.tmpdir(), folder);` | `for (const name of TEMP_VARIABLES) assert.equal(process.env[name], folder, \`${name} names the run folder\`);` and `assert.equal(owned.tempRoot(), folder);` — import `TEMP_VARIABLES` from `../src_vs_code/scripts/owned.mjs`; add `assert.equal(TEMP_VARIABLES.length, 3)` so an emptied list cannot pass vacuously (`common/testing.md`, *A structural test that matches nothing*) |
| N | `scripts/native/native.test.mjs` — the 11 `randomBytes(2)` names | `join(ROOT, \`<stem>-${randomBytes(2).toString('hex')}\`)` | ONE function near `ROOT`: `let drawn = 0; function fresh(stem) { drawn += 1; return join(ROOT, \`${stem}-${drawn}\`); }` (line 986 builds the path with a template, not `join`: keep its `${ROOT}/` spelling through `fresh` returning the same string). Line 120 already appends a counter: route it through `fresh` too, keeping its stem text. Every site calls `fresh('<its stem>')`. Drop `randomBytes` from the import if unused |

## 5. Steps — RED, GREEN, RED again

Lowered, one process at a time (`common/local-cpu-budget.md`): `node .agents/conventions/tools/low-priority.mjs node scripts/run-lib-tests.mjs`.

0. **Before.** Run `node scripts/run-lib-tests.mjs`; write down `# tests`, `# pass`, `# fail`, `# skipped`. Run the Node
   guard `node .agents/conventions/tools/leftovers-check.mjs --repo . --baseline --profile node --token coai --identity
   "the multi-model review extension and server"` → clean.
1. **RED (the drain).** Delete only the 10 `scripts/` entries from `raw-temp-baseline.json`; change no site. The Node
   guard must fail (exit 1) with exactly 10 `raw-temp` findings, one per site of rows 1–10. Copy them for the PR.
2. **RED (the collision).** In `native.test.mjs`, first add `fresh(stem)` with TODAY's draw
   (`join(ROOT, \`${stem}-${randomBytes(2).toString('hex')}\`)`) and route the 11 sites through it; add the test
   `fresh scratch names never repeat within a run — 65,537 draws, one more than two random bytes can tell apart`: draw
   `fresh('x')` 65,537 times into a `Set` and assert its size is 65,537 (draw names only; create nothing). It must FAIL
   (pigeonhole: the 2-byte draw cannot give 65,537 distinct values). Copy the failure.
3. **GREEN.** Switch `fresh` to the counter; rewrite rows 1–10. The new test passes; the whole runner is green with step
   0's counts plus one test; the Node guard `clean`, exit 0; `--baseline-shrinks --base origin/main` "only shrinks", exit 0;
   `node .agents/conventions/tools/plan-lifecycle.mjs` clean.
4. **RED again (teeth).** (a) Put the 2-byte draw back in `fresh` → the new test fails; restore. (b) Put
   `transcript-sanitise.test.mjs:22`'s old line back → the Node guard has one `raw-temp` finding there; restore. (c) In
   `runFolder.test.mjs` set `TEMP_VARIABLES` to `[]` locally (a scratch edit) → `P-T` fails on the length check; restore.
   Commit none of the scratch edits.
5. **Docs.** `research/module_tests.md`, the *S7, the drain* paragraph: add the scripts suite (10 → 0; the file 770 →
   760) and one sentence on the native test's `fresh` names (why: the 2-byte draw collided on CI, 2026-11-11). The E5
   status line: S7's second PR. This brief's status and its `todo/README.md` row.

## 6. What this PR does not prove

- The other four scopes (760 entries) are untouched.
- Windows and macOS: `scripts/run-lib-tests.mjs` runs on Linux in CI (`ci.yml:226`, *Probe script tests*); nothing
  here adds a leg. The changed lines are path joins and folder creation through the same helper the suite already uses
  on those platforms' extension legs.
- `fresh` makes names unique within ONE run of the file (one `ROOT`, one counter); two runs have two `ROOT`s, each an
  owned, claimed folder of its own run.

## 7. Definition of Done

- [ ] Step 1's 10 findings, step 2's failing pigeonhole test, and step 4's three REDs are in the PR description; step 3's
      counts equal step 0's plus the one new test.
- [ ] `raw-temp-baseline.json`: 760 entries, only `scripts/` lines removed (`git diff` shows deletions only).
- [ ] No folder is made at module load; no new exemption; the inventory unchanged.
- [ ] `research/module_tests.md`, the E5 status line and `todo/README.md` updated in the same commit as the code.
- [ ] The coai code round ran over the commit and every finding was resolved before the PR.
