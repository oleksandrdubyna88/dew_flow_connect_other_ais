import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { childEnvironment, launch } from '../processLauncher';
import { capture } from '../versionProbe';

/**
 * `withoutEnv` (E3.S1, § W3): names taken OUT of the copy of the inherited environment, compared without case. The one new option of
 * `LaunchOptions` and `CaptureOptions`. With it the child gets a full copy less those keys — never an empty map.
 */

const INHERITED: NodeJS.ProcessEnv = { SystemRoot: 'C:\\Windows', PATH: 'C:\\x', WSLENV: 'A/p', wslenv: 'B', HOME: '/h', Home: '/h2', Keep: '1' };

test('E3S1-T2 (pure): childEnvironment with `without` drops each name in every spelling of its case, and keeps the rest', () => {
  const env = childEnvironment(undefined, INHERITED, ['WSLENV', 'HOME']);
  assert.ok(env !== undefined);
  assert.deepEqual(Object.keys(env).sort(), ['Keep', 'PATH', 'SystemRoot']);
  assert.equal(env['SystemRoot'], 'C:\\Windows');
});

test('E3S1-T2 (pure): `without` with nothing to add is never an empty map, and an empty `without` changes nothing', () => {
  const only = childEnvironment(undefined, { WSLENV: 'x', HOME: 'y' }, ['WSLENV', 'HOME']);
  assert.deepEqual(only, {}, 'the copy less the keys; here the inherited map held nothing else');
  assert.equal(childEnvironment(undefined, INHERITED, []), undefined, 'no addition and no `without`: the spawn takes its own path');
  assert.equal(childEnvironment(undefined, INHERITED), undefined);
});

test('E3S1-T2 (pure): additions are applied, and a name in `without` is out even when an addition names it', () => {
  const env = childEnvironment({ Extra: 'e', home: 'added' }, INHERITED, ['HOME']);
  assert.ok(env !== undefined);
  assert.equal(env['Extra'], 'e');
  assert.equal(Object.keys(env).some((key) => key.toLowerCase() === 'home'), false);
  assert.equal(env['Keep'], '1');
});

test('E3S1-T2 (real child): a launched child does not see the names, and still sees the others', async () => {
  process.env['COAI_WITHOUT_PROBE'] = 'still here';
  process.env['WSLENV'] = 'X/p';
  const script = 'process.stdout.write(JSON.stringify({ w: process.env.WSLENV ?? "", p: process.env.COAI_WITHOUT_PROBE ?? "" }))';
  try {
    const got = await capture(process.execPath, ['-e', script], false, 20_000, undefined, undefined, '', { withoutEnv: ['WSLENV', 'HOME'] });
    assert.equal(got.output, JSON.stringify({ w: '', p: 'still here' }));
    const child = launch(process.execPath, ['-e', script], { withoutEnv: ['wslenv'] });
    const out = await new Promise<string>((resolve) => {
      let text = '';
      child.onStdout((chunk) => { text += chunk; });
      child.onExit(() => { resolve(text); });
    });
    assert.equal(out, JSON.stringify({ w: '', p: 'still here' }));
  } finally {
    delete process.env['COAI_WITHOUT_PROBE'];
    delete process.env['WSLENV'];
  }
});
