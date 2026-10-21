// node --test scripts/lib/*.test.mjs — S-N4 of todo/PLAN_kimi_and_vibe_on_native_linux.md: every M-K3 / M-K6 arm that goes through the shim carries an owned
// root (the shim refuses a Kimi turn without --home, --persons-bin and --locks, E4.3), and every `kimi -p` control an owned home with `owned: true`, so
// the built environment (not the operator's own) is what it runs in. The arms are read off the table with fakes for the probe's helpers: nothing is
// launched, no file is written, no credential is opened.
import assert from 'node:assert/strict';
import { join } from 'node:path';
import { test } from 'node:test';
import { rootOfHome } from './kimi.mjs';
import { builtEnv } from './kimi-home.mjs';
import { ownedShimArms } from './kimi-arms.mjs';

const RUN = join('scratch', 'run-1');
/** The probe's helpers as fakes: `write` records nothing, `ownedHome` hands back the product's layout (`<runDir>/kh/home`) and counts its calls. */
function table() {
  const homes = [];
  const ownedHome = (runDir) => {
    const path = join(runDir, 'kh', 'home');
    homes.push(path);
    return { path, notes: 'holds: (junction only)' };
  };
  return { arms: ownedShimArms({ write: () => {}, ownedHome, pySnippet: (p) => `python3 -c "touch ${p}"` }), homes };
}

const specs = (arms) => Object.entries(arms).flatMap(([cell, list]) => list.flatMap((a) => [1, 2, 3].map((n) => ({ cell, a, n, spec: a.setup(join(RUN, `${cell}-${a.arm}-${n}`), n) }))));

test('S-N4: the table holds M-K3 (Write, Edit, Bash and a control each) and M-K6 (two shim arms)', () => {
  const { arms } = table();

  assert.deepEqual(arms['M-K3'].map((a) => a.arm), ['Write', 'control -p: Write', 'Edit', 'control -p: Edit', 'Bash', 'control -p: Bash']);
  assert.deepEqual(arms['M-K6'].map((a) => a.arm), ['Read inside + absolute outside', 'Grep + Glob on an outside path']);
});

test('S-N4: every shim arm of M-K3 and M-K6 has an owned root — the shim refuses a turn without one', () => {
  for (const { cell, a, n, spec } of specs(table().arms).filter((s) => (s.spec.kind ?? 'shim') === 'shim')) {
    assert.notEqual(rootOfHome(spec.home ?? ''), '', `${cell} ${a.arm} has no owned root — the shim refuses it (E4.3), and the control would run on the real home (run ${n})`);
    assert.equal(spec.owned, true, `${cell} ${a.arm}: the owned home is not marked owned — the shim environment and the disposal would be skipped`);
  }
});

test('S-N4: every -p control runs on an owned home with owned: true, in the built environment — never on the person\'s real home', () => {
  const controls = specs(table().arms).filter((s) => s.spec.kind === 'prompt');

  assert.equal(controls.length, 9, 'three controls, three runs each');
  for (const { cell, a, n, spec } of controls) {
    assert.equal(spec.owned, true, `${cell} ${a.arm} (run ${n}) has no owned home — the control would run on the real home`);
    assert.notEqual(rootOfHome(spec.home ?? ''), '', `${cell} ${a.arm} (run ${n}): the home is not <root>/home`);
    // The Linux arm is read with forward slashes whatever host runs this test (a Windows host joins with a backslash).
    const slashed = (p) => p.replaceAll('\\', '/');
    const env = builtEnv({ home: slashed(spec.home), profile: slashed(spec.profile ?? ''), platform: 'linux', parent: { PATH: '/usr/bin', HOME: '/home/op' } });
    assert.equal(env.KIMI_CODE_HOME, slashed(spec.home));
    assert.equal(env.HOME, `${slashed(rootOfHome(spec.home))}/os`, `${cell} ${a.arm}: HOME is the operator's, so the control fills the real OS home`);
    assert.equal(env.XDG_CACHE_HOME, `${slashed(rootOfHome(spec.home))}/cache`);
  }
});

test('S-N4: each run gets its OWN owned home, and the arms\' prompts and canaries are untouched', () => {
  const { arms, homes } = table();

  const all = specs(arms);

  assert.equal(new Set(homes).size, homes.length, 'two runs shared an owned home');
  assert.equal(homes.length, all.length, 'an arm that makes no owned home');
  assert.match(all.find((s) => s.a.arm === 'Bash').spec.prompt, /python3 -c "touch .*canary-bash-outside\.txt"/u);
  assert.deepEqual(Object.keys(all.find((s) => s.a.arm === 'Bash').spec.canaries), ['bashOutside']);
  assert.deepEqual(Object.keys(all.find((s) => s.a.arm === 'Grep + Glob on an outside path').spec.expect), ['grepMarker', 'globName']);
});
