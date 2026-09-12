import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
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
  // A check that quietly fixes the thing it checks is a check that always passes — and identical
  // stdout across two runs does not prove it, because a run that REPAIRED a stale file would print
  // the same happy line the second one does. So the file's own bytes are read before and after, over
  // a copy that is deliberately stale. (codex, this story's code round.)
  const dir = mkdtempSync(join(tmpdir(), 'coai-generated-'));
  try {
    const target = join(dir, 'builtinRoles.generated.ts');
    writeFileSync(target, '// nothing like the real thing\n', 'utf8');

    const run = spawnSync(
      process.execPath,
      [join(scriptsDir, SCRIPTS[0]), '--check', `--out=${target}`],
      { encoding: 'utf8' },
    );

    assert.strictEqual(run.status, 1, 'a stale file is a failure');
    assert.match(run.stderr, /run: node scripts\/generate-builtin-roles\.mjs/);
    assert.strictEqual(
      readFileSync(target, 'utf8'),
      '// nothing like the real thing\n',
      '--check repaired the file it was asked to check',
    );
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('--check on a file that is not there says so, rather than a stack trace', () => {
  const run = spawnSync(
    process.execPath,
    [join(scriptsDir, SCRIPTS[0]), '--check', `--out=${join(tmpdir(), 'coai-no-such-file-9e1a.ts')}`],
    { encoding: 'utf8' },
  );

  assert.strictEqual(run.status, 1);
  assert.match(run.stderr, /does not exist — run: node scripts\/generate-builtin-roles\.mjs/);
});

/**
 * A seed this panel cannot draw is refused where it is read, not rendered into a file that
 * typechecks.
 *
 * <p>The stage one is the load-bearing case: <code>prompts.ts</code> maps anything that is not
 * <code>plan</code> onto the panel's <code>code</code>, which is right for the two stages that exist
 * and silently wrong for the document stage a later plan adds — the role would be drawn among the
 * code roles with nothing saying so. (codex and gemini, this story's code round.)</p>
 */
for (const [what, role, expected] of [
  ['a stage this panel has no mapping for', { id: 'R', name: 'R', stage: 'deploy', programmingTask: true }, /no mapping for/],
  ['a role that does not say whether it is a programming task', { id: 'R', name: 'R', stage: 'result' }, /programming task/],
  ['a role with no name', { id: 'R', stage: 'result', programmingTask: true }, /has no name/],
  ['a role with no prompts at all', { id: 'R', name: 'R', stage: 'result', programmingTask: true, prompts: [] }, /no prompts/],
] as const) {
  test(`the generator refuses ${what}`, () => {
    const dir = mkdtempSync(join(tmpdir(), 'coai-seed-'));
    try {
      const seed = join(dir, 'builtin-roles.json');
      writeFileSync(
        seed,
        JSON.stringify({ roles: [{ prompts: [{ id: 'p', label: 'L', purpose: 'P' }], ...role }] }),
        'utf8',
      );

      const run = spawnSync(
        process.execPath,
        [join(scriptsDir, SCRIPTS[0]), `--seed=${seed}`, `--out=${join(dir, 'written.ts')}`],
        { encoding: 'utf8' },
      );

      assert.notStrictEqual(run.status, 0, `it generated anyway:\n${run.stdout}`);
      assert.match(run.stderr, expected);
      assert.ok(!existsSync(join(dir, 'written.ts')), 'a refused seed must leave no file behind');
    } finally {
      rmSync(dir, { recursive: true, force: true });
    }
  });
}
