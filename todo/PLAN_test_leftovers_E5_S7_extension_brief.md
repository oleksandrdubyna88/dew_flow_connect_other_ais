# Build brief — coai E5 story S7, the fifth drain PR: the extension suite's `raw-temp` entries to zero

> Status: **plan only, 2026-11-11, by Opus.** Scope: the 219 `raw-temp` baseline entries of `src_vs_code/src/test/` (71
> files). Tests only, plus one small test-support adapter. A Sonnet coder builds from this brief alone.

The plan is [PLAN_test_leftovers_E5.md](PLAN_test_leftovers_E5.md) § *S7 — The drain* ("`fs.mkdtempSync(path.join(os.tmpdir(),
'<x>-'))` in a compiled extension test ⇒ … the site moves to the wrap"); the earlier drain PRs are #977, #979, #980, #981
(briefs `PLAN_test_leftovers_E5_S7_{bench,scripts,bugs,server}_brief.md`). The parent is `dew_flow_conventions ·
todo/PLAN_test_leftovers_rollout.md`, § *Epoch 8*, rows S7-b to S7-e. Measured at `origin/main` `9c9f0929`, conventions
pin `09db328`.

## 1. Goal

The extension suite drains (219 → 0; the file 677 → 458). Shapes, measured by the guard's own reading of each entry's
line: 173 `mkdtempSync(…tmpdir()…)`, 19 other `tmpdir()` reads, 7 `process.env.TEMP|TMP|TMPDIR`, and 20 whose `line` hint is
stale (the baseline keys a site by file and line HASH; the hint is not a key). **The authoritative site list is step 1's RED
output** — the guard names every real file:line.

Every folder a site makes moves to the wrap the suite already uses (`scripts/owned.mjs`'s `owned.ownedDir`, as
`roundCountsRead.test.ts:117-125` does); every read of the temp root that makes nothing uses the S6 helper
`src/test/tempRoot.ts` (`.ts`) or `owned.tempRoot()` (`.mjs`). The entries leave the baseline in the same commit; the
`src_vs_code/src/test/` scope stays, with zero entries.

## 2. What must be true when it is done

1. `raw-temp-baseline.json` holds no `src_vs_code/` entry; 458 remain (all `src_mcp/tests/`); nothing added or re-keyed;
   the scope and its proof unchanged.
2. The shipped Node guard with `--baseline` is clean (it judges this `node` scope); `--baseline-shrinks --base origin/main`
   passes; the C# guard class stays green; no new `testsToDelete-exempt` marker; `shared/leftovers-exemptions.json`
   unchanged — it is keyed by LINE. Measured at `9c9f0929`: ONE row lies in this scope, `src_vs_code/src/test/leftoversSweep.test.mjs:58`
   (`legacy-name`). If that file has a site, every edit above line 58 keeps the exact line count (no import removed or added
   above it unless another line is added or removed to match, as #979 did in `old-coai-leftovers.test.mjs`); re-list the
   rows at step 0 in case `main` moved (`node -e` over the file's `rows`), and run `testLeftovers.test.mjs` after the edits.
3. No folder is made at module load. Every test keeps its name and assertions; the extension suite's counts are unchanged.
4. No raw temp call is MOVED into a file the guard does not read: `ownedTemp.ts` (below) makes folders only through
   `owned.ownedDir` (owner form, claimed first) and reads the root only through `owned.tempRoot()`.

## 3. Constraints

- No change to `scripts/owned.mjs`, `inRunFolder.cjs`, the gate, `tempRoot.ts`, the proof marker or its child runs, or
  product code. Prefixes keep their `what` (`'coai-asked-'` → `what` `'coai-asked'`: the prefix without its trailing `-`;
  `what` is at most 31 characters).
- **No Python, anywhere** (the owner's rule: Python only in a sandbox). Edit with the Edit tool or a Node script.
- A test's own `rmSync(dir, …)` in a `finally` STAYS: it removes the folder early; the scope's close then removes the claim
  (`removeQuietly` uses `rmSync(…, { force: true })`, so a folder already gone is fine — `tools/lib/owned.mjs:607-615`).

## 4. The adapter and the shapes

**The adapter — `src_vs_code/src/test/ownedTemp.ts` (new, not a `*.test.*` file).** One typed door to the wrap for the `.ts`
tests, the shape `roundCountsRead.test.ts:117-122` already declares inline:

```ts
import { realpathSync } from 'node:fs';
import { after } from 'node:test';

interface OwnedScope { after(step: () => void): void; close(): void }
interface OwnedWrap {
  cleanupScope(): OwnedScope;
  ownedDir(scope: OwnedScope, what: string, options?: { parent?: string }): string;
  tempRoot(): string;
}
// eslint-disable-next-line @typescript-eslint/no-require-imports -- the wrap is an .mjs the compiled tests reach as roundCountsRead.test.ts does
const { owned } = require('../../scripts/owned.mjs') as { owned: OwnedWrap };

/** The file's ONE scope: every folder ownedTemp makes is claimed first and removed, with its claim, when this test file ends (each file runs in its own process: scripts/run-tests.mjs). */
const scope = owned.cleanupScope();
after(() => scope.close());

/** A fresh owned folder of the wrap inside the run folder — never a raw temp folder (E5 S7). Call it inside a test or a hook, never at module load. */
export function ownedTemp(what: string): string {
  return owned.ownedDir(scope, what, { parent: realpathSync.native(owned.tempRoot()) });
}
```

Adjust only what the compiler or the extension's lint config requires (the `require` form, the path from `out/test/` to
`scripts/owned.mjs` — check how `roundCountsRead.test.ts` resolves it after compilation and copy exactly that). The
top-level `after(...)` registration is the precedent of `scripts/lib/kimi.test.mjs:18-20`; nothing is CREATED at load.

**M — `mkdtempSync(join(tmpdir(), '<prefix>'))`** (and its spelled variants: `path.join(os.tmpdir(), …)`,
`realpathSync(…)` around it): → `ownedTemp('<prefix without its trailing ->')` in a `.ts` test; in a `.mjs` test,
`owned.ownedDir(t, '<what>', { parent: fs.realpathSync.native(owned.tempRoot()) })` when the site is inside a test with its
context `t`, else the file-level-scope pattern of `scripts/lib/kimi.test.mjs:16-20`. A site at MODULE LOAD (top level of a
file) moves into a `before` hook, as `scripts/lib/transcript-sanitise.test.mjs` did in #979. If the original wrapped the
call in `realpathSync`, keep the `realpathSync` around the result. Remove imports that become unused.

**R — a read of the root that makes nothing** (a path compared against it, a name built under it that must not exist, an
argument the code under test only reads): → `tempRoot()` from `./tempRoot` in `.ts`, `owned.tempRoot()` in `.mjs`. If the
test WRITES a file directly in the root, it is shape M instead (make a folder, write inside it).

**E — `process.env.TEMP`/`TMP`/`TMPDIR`**: in the proof file `src_vs_code/src/test/runFolder.test.mjs`, iterate
`TEMP_VARIABLES` from `scripts/owned.mjs` (exported in #979) with a length-3 check, and read `owned.tempRoot()`, exactly as
`scripts/runFolder.test.mjs` does since #979. Elsewhere: a site that SETS a child's temp variables to a folder → build the
child env through the same `TEMP_VARIABLES` list (`for (const name of TEMP_VARIABLES) env[name] = folder`); a site that
only reads one → `owned.tempRoot()`.

**S — a line that only SPELLS a temp API or path as data** (a regex, a fixture string, a planted line): derive it from the
source it describes if that makes a stronger assertion (#981's Dockerfile `COPY`), else the `PlantedLines` precedent
(`src_mcp/tests/Leftovers/LeftoversBaselineTests.cs:256-260`: the name written with a `~` removed at run time) with a
one-line comment saying it is data, not a call. List every S site in the report — the judge reviews each.

If a site fits none of these, or a change would alter what a test asserts, stop and report it.

## 5. Steps — RED, GREEN, RED again

Lowered: `cd src_vs_code && node ../.agents/conventions/tools/low-priority.mjs npm test` (it compiles, then runs
`scripts/run-tests.mjs` — the CI command of *extension · typecheck · test · package*). Also run `npm run lint` if the
package has it, and `npx tsc --noEmit -p .` if `npm test` does not type-check.

0. **Before.** `npm test`: record total/pass/fail/skipped. The Node guard `--baseline` clean.
1. **RED.** Delete only the 219 `src_vs_code/` entries; change no site. The Node guard fails (exit 1) with exactly 219
   `raw-temp` findings under `src_vs_code/src/test/` — this list, with the real lines, is the work list.
2. **GREEN.** Add the adapter; rewrite every site. `npm test` equals step 0's counts PLUS the one new contract test of step 3 (`ownedTemp.test.ts`, written in this step so it can be seen red in step 3); the Node guard clean; `--baseline-shrinks
   --base origin/main` only shrinks; `node .agents/conventions/tools/plan-lifecycle.mjs` clean; `testLeftovers.test.mjs`
   green (the inventory's line keys held).
3. **RED again.** (a) Put one shape-M line back → exactly one finding; restore. (b) In `ownedTemp.ts`, replace the
   `ownedDir` call by `mkdtempSync(join(tmpdir(), what))` → the guard does NOT see it (the file is not a test file) — so
   instead assert the adapter's contract in a new test `src_vs_code/src/test/ownedTemp.test.ts`: two calls give two
   different existing folders, each named in the owner form (`owned.classifyName(basename(dir)).form === 'owner'`, token
   `coai`), each with a `<dir>.owner` claim beside it. With the `mkdtempSync` stand-in that test fails on the owner form;
   restore, green. This is the control that the guard cannot be for a non-test file (§ 2.4).
4. **Docs.** `research/module_tests.md`, the *S7, the drain* paragraph: the extension suite fifth (219 → 0; 677 → 458),
   the adapter `ownedTemp.ts` and its contract test, and every S site in one sentence. The E5 status line's S7 sentence:
   the fifth PR. This brief's status and its `todo/README.md` row. If `src_vs_code/README.md` or a module doc lists the
   test-support files, add `ownedTemp.ts`.

## 6. What this PR does not prove

- `src_mcp/tests/` (458 entries) is untouched; it follows, likely in more than one PR.
- The extension's Windows and macOS behaviour is exercised by the CI legs that run this suite today; none is added.

## 7. Definition of Done

- [ ] Step 1's 219 findings (count and the per-file totals) and step 3's two REDs are in the PR description; step 2's
      counts equal step 0's plus the one new contract test.
- [ ] `raw-temp-baseline.json`: 458 entries, only `src_vs_code/` lines removed.
- [ ] No new marker; the inventory unchanged; `testLeftovers.test.mjs` green.
- [ ] Every S site listed with its treatment.
- [ ] Docs updated in the same commit as the code; the coai code round ran and every finding was resolved before the PR.
