import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { test } from 'node:test';
import { pathToFileURL } from 'node:url';

/**
 * `.github/scripts/lib/resolved.mjs` — where `git` and `gh` are, for every script in that folder.
 *
 * <p>A module of its own since 2026-09-26: it lived inside `branch-protection.mjs`, and the release
 * scripts imported a PATH resolver from a branch-protection script. Tested here on its own, on a PATH
 * this test controls, so the module holds even if `branch-protection.mjs` is rewritten or removed.
 * (The gate's plan round.) Run in a child process because the module is ESM and this suite compiles to
 * CommonJS.</p>
 */

const repoRoot = path.resolve(__dirname, '..', '..', '..');
const moduleUrl = pathToFileURL(path.join(repoRoot, '.github', 'scripts', 'lib', 'resolved.mjs')).href;

function resolveOn(pathVariable: string, name: string): { code: number; said: string } {
  const program = `import(${JSON.stringify(moduleUrl)}).then((m) => {
    try { console.log(m.resolved(${JSON.stringify(name)})); } catch (e) { console.error(e.message); process.exit(1); }
  });`;
  const ran = spawnSync(process.execPath, ['--input-type=module', '-e', program],
    { encoding: 'utf8', timeout: 30_000, killSignal: 'SIGKILL', env: { ...process.env, PATH: pathVariable } });
  assert.equal(ran.error, undefined, `could not run: ${ran.error?.message}`);

  return { code: ran.status as number, said: `${ran.stdout}${ran.stderr}`.trim() };
}

test('it answers the absolute path of a program on PATH', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'resolver-'));
  try {
    const program = path.join(dir, 'coai-probe' + (process.platform === 'win32' ? '.exe' : ''));
    fs.writeFileSync(program, '');
    fs.chmodSync(program, 0o755);

    const { code, said } = resolveOn(dir, 'coai-probe');
    assert.equal(code, 0, said);
    assert.equal(said, program, 'the answer is not the program on PATH, absolutely');
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('a program that is not on PATH is refused by name, never spawned', () => {
  // The SAME fixture holds a real program, so the refusal is proved to come from looking in it rather
  // than from a PATH that could not be read at all. (The gate's code round.)
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'resolver-'));
  try {
    const present = path.join(dir, 'coai-probe' + (process.platform === 'win32' ? '.exe' : ''));
    fs.writeFileSync(present, '');
    fs.chmodSync(present, 0o755);
    assert.equal(resolveOn(dir, 'coai-probe').said, present, 'the fixture itself could not be looked in');

    const { code, said } = resolveOn(dir, 'no-such-program-4f2b9c');
    assert.equal(code, 1);
    assert.equal(said, 'no-such-program-4f2b9c is not on PATH, so nothing that needs it can run');
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});
