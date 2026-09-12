import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { join } from 'node:path';
import { test } from 'node:test';

/**
 * Every generated file in this package still is what its generator produces.
 *
 * <p>The catalog's own agreement test compares the GENERATED FILE with the SEED, which catches a
 * file left behind by a seed edit. It cannot catch the other direction: change the generator's
 * mapping and the committed file still matches the seed, every test stays green, and the next person
 * to run the script silently changes the panel's catalog. Same for the help page, one level worse —
 * <code>helpPrompts.test.ts</code> compares each ENTRY against the <code>.md</code> file it came
 * from, so a changed grouping, order or label leaves it green while the page changes.</p>
 *
 * <p>So each generator gained a <code>--check</code> mode: it renders exactly what it would write
 * and compares, writing nothing. Raised by codex and gemini on story C1's plan round, three times
 * between them.</p>
 */

const SCRIPTS = ['generate-builtin-roles.mjs', 'generate-help-prompts.mjs'] as const;

// out/test at run time, so two levels reach the package root.
const scriptsDir = join(__dirname, '..', '..', 'scripts');

for (const script of SCRIPTS) {
  test(`${script} still produces the file that is committed`, () => {
    const run = spawnSync(process.execPath, [join(scriptsDir, script), '--check'], {
      encoding: 'utf8',
    });

    assert.strictEqual(
      run.status,
      0,
      `${script} --check failed:\n${run.stdout}${run.stderr}`,
    );
  });
}

test('--check writes nothing, so running the suite cannot repair the drift it is looking for', () => {
  // A check that quietly fixes the thing it checks is a check that always passes. Asserted by
  // running it twice over a file nobody has regenerated in between: the second run sees exactly what
  // the first one did.
  const before = spawnSync(process.execPath, [join(scriptsDir, SCRIPTS[0]), '--check'], { encoding: 'utf8' });
  const after = spawnSync(process.execPath, [join(scriptsDir, SCRIPTS[0]), '--check'], { encoding: 'utf8' });

  assert.strictEqual(before.stdout, after.stdout);
  assert.strictEqual(after.status, 0);
});
