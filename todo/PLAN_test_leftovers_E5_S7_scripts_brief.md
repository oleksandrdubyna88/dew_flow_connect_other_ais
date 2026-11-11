# Build brief — coai E5 story S7, the second drain PR: the `scripts/` suite's `raw-temp` entries to zero

> Status: **plan only, 2026-11-11, by Opus.** Scope: the 10 `raw-temp` baseline entries of `scripts/` and one read-only
> export of the Node helper. A Sonnet coder builds from this brief alone.

The plan is [PLAN_test_leftovers_E5.md](PLAN_test_leftovers_E5.md) § *S7 — The drain*; the first drain PR was #977
([its brief](PLAN_test_leftovers_E5_S7_bench_brief.md)). The parent is `dew_flow_conventions ·
todo/PLAN_test_leftovers_rollout.md`, § *Epoch 8*, rows S7-b to S7-e. Rule: `.agents/conventions/common/test-leftovers.md`
§2 (the helper; never at module load) and `common/test-leftovers-baseline.md`. Counts measured at `origin/main`
`e1b63958`, conventions pin `09db328`.

## 1. Goal

**The `scripts/` suite drains (10 → 0; the file 770 → 760).** Each site moves to the helper door the suite already
   uses (`wrap.ownedDir(scope, <what>, { parent: realpathSync.native(wrap.tempRoot()) })`, `wrap.tempRoot()`; `wrap` is
   `owned` of `src_vs_code/scripts/owned.mjs`), and its entry leaves the baseline in the same commit. The `scripts/`
   scope stays, with zero entries (as the bench scope did in #977).

**Found, and NOT in this PR's scope** (it would be a scope change to S7, which `common/plan-change-control.md` counts — and the
parent's epoch 8 already holds one change): `scripts/native/native.test.mjs` names 11 kinds of scratch folder in its
owned `ROOT` with 2 random bytes (`randomBytes(2)`, 65,536 values) and creates them with a plain `mkdirSync`, so two
draws of one stem in one run can collide — seen once on #977's CI (job 130615928516, 2026-11-11: `EEXIST … mkdir
…/wide-58de`, test G13). It is a defect outside the plan, carried to the owner as a separate fix; nothing below touches
that file.

## 2. What must be true when it is done

1. `raw-temp-baseline.json` holds no entry whose `file` starts with `scripts/`; 760 entries remain; no entry added or
   re-keyed; the `scripts/` scope and its proof unchanged.
2. The shipped Node guard with `--baseline` is clean; `--baseline-shrinks --base origin/main` passes; no new
   `testsToDelete-exempt` marker; the inventory (`shared/leftovers-exemptions.json`) unchanged (S7-e).
3. No folder in `scripts/**/*.test.mjs` is made at module load (`transcript-sanitise.test.mjs:22` today).
4. The proof file `scripts/runFolder.test.mjs` still asserts, in `P-T`, that EACH of the three temp variables and the
   process's temp root name the run folder — read through the helper, not spelled.
5. `node scripts/run-lib-tests.mjs` (the CI command, `ci.yml:226`) is green on Linux with the same pass count, no new
   skip.

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
| 3 | `scripts/lib/old-coai-leftovers.test.mjs:39` (`privateRoot`) | `fs.realpathSync.native(fs.mkdtempSync(path.join(os.tmpdir(), prefix)))` + `t.after(rmSync)` | the signature becomes `function privateRoot(t, what = 'old-coai-leftovers')` returning `fs.realpathSync.native(wrap.ownedDir(t, what, { parent: fs.realpathSync.native(wrap.tempRoot()) }))`; `ownedDir` registers its own removal on `t`, so the `t.after(rmSync)` goes. All 47 calls already pass `t` (checked at `e1b63958`); the 39 one-argument calls stay as they are, and the 8 named ones lose the trailing `-` of their old prefix: `'old-coai-colon'`, `'old-coai-cwd'` ×3, `'old-coai-escape'`, `'old-coai-listing'` ×2, `'old-coai-out'`, `'old-coai-start'`. No test reads the root's own name for its text: `S17` (escape) and the colon test build their special names INSIDE the root, and `S34` uses only `path.basename(root)` / `path.dirname(root)` (still valid for an owned folder inside the run folder). Import `owned as wrap` from `../../src_vs_code/scripts/owned.mjs`. If a caller turns out to read the root's name text after all, stop and report |
| 4 | `scripts/lib/old-coai-leftovers.test.mjs:642` (S28) | `--out path.join(os.tmpdir(), 'never-written-listing.json')` | `const root = privateRoot(t, 'old-coai-start'); const missing = path.join(root, 'no-such-root');` and `--out path.join(root, 'never-written-listing.json')` — the file is never written (the test's own claim), now inside an owned folder either way |
| 5 | `scripts/lib/transcript-sanitise.test.mjs:22` | `const base = mkdtempSync(join(realpathSync.native(tmpdir()), 'transcript-sanitise-test-'));` at MODULE LOAD, `after(rmSync)` | the `kimi.test.mjs:16-20` shape: `let base; const scope = wrap.cleanupScope(); before(() => { base = wrap.ownedDir(scope, 'transcript-sanitise', { parent: realpathSync.native(wrap.tempRoot()) }); }); after(() => scope.close());` — `scratch()` reads `base` lazily, so it keeps working. Drop `mkdtempSync`, `tmpdir`, `rmSync` imports if unused |
| 6 | `scripts/run-lib-tests.test.mjs:46` (`privateRoot`) | `fs.mkdtempSync(path.join(os.tmpdir(), 'run-lib-tests-'))` + `t.after(rmSync)` | `wrap.ownedDir(t, 'run-lib-tests', { parent: fs.realpathSync.native(wrap.tempRoot()) })`. The tests plant `testsToDelete-<owner>-coai-…` folders in this root and point a child run's temp root at it: check that nothing walks UP from the root (a sweep given `--tmp <root>` reads only that root); if one does, stop and report |
| 7–10 | `scripts/runFolder.test.mjs:30-33` (`P-T`, the proof) | `assert.equal(process.env.TEMP, folder); …TMP…; …TMPDIR…; assert.equal(os.tmpdir(), folder);` | `for (const name of TEMP_VARIABLES) assert.equal(process.env[name], folder, \`${name} names the run folder\`);` and `assert.equal(owned.tempRoot(), folder);` — import `TEMP_VARIABLES` from `../src_vs_code/scripts/owned.mjs`; add `assert.equal(TEMP_VARIABLES.length, 3)` so an emptied list cannot pass vacuously (`common/testing.md`, *A structural test that matches nothing*) |

## 5. Steps — RED, GREEN, RED again

Lowered, one process at a time (`common/local-cpu-budget.md`): `node .agents/conventions/tools/low-priority.mjs node scripts/run-lib-tests.mjs`.

0. **Before.** Run `node scripts/run-lib-tests.mjs`; write down `# tests`, `# pass`, `# fail`, `# skipped`. Run the Node
   guard `node .agents/conventions/tools/leftovers-check.mjs --repo . --baseline --profile node --token coai --identity
   "the multi-model review extension and server"` → clean.
1. **RED (the drain).** Delete only the 10 `scripts/` entries from `raw-temp-baseline.json`; change no site. The Node
   guard must fail (exit 1) with exactly 10 `raw-temp` findings, one per site of rows 1–10. Copy them for the PR.
2. **GREEN.** Rewrite rows 1–10. The whole runner is green with step 0's counts; the Node guard `clean`, exit 0; `--baseline-shrinks --base origin/main` "only shrinks", exit 0;
   `node .agents/conventions/tools/plan-lifecycle.mjs` clean.
3. **RED again (teeth).** (a) Put
   `transcript-sanitise.test.mjs:22`'s old line back → the Node guard has one `raw-temp` finding there; restore. (b) In
   `runFolder.test.mjs` set `TEMP_VARIABLES` to `[]` locally (a scratch edit) → `P-T` fails on the length check; restore.
   Commit none of the scratch edits.
4. **Docs.** `research/module_tests.md`, the *S7, the drain* paragraph: add the scripts suite (10 → 0; the file 770 →
   760). The E5
   status line: S7's second PR. This brief's status and its `todo/README.md` row.

## 6. What this PR does not prove

- The other four scopes (760 entries) are untouched.
- Windows and macOS: `scripts/run-lib-tests.mjs` runs on Linux in CI (`ci.yml:226`, *Probe script tests*); nothing
  here adds a leg. The changed lines are path joins and folder creation through the same helper the suite already uses
  on those platforms' extension legs.
- The native test's 2-byte names (§ 1, *Found*) are not fixed here.

## 7. Definition of Done

- [ ] Step 1's 10 findings and step 3's two REDs are in the PR description; step 2's counts equal step 0's.
- [ ] `raw-temp-baseline.json`: 760 entries, only `scripts/` lines removed (`git diff` shows deletions only).
- [ ] No folder is made at module load; no new exemption; the inventory unchanged.
- [ ] `research/module_tests.md`, the E5 status line and `todo/README.md` updated in the same commit as the code.
- [ ] The coai code round ran over the commit and every finding was resolved before the PR.
