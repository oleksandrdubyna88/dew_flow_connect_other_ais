// node --test scripts/lib/*.test.mjs — S-N4 of todo/PLAN_kimi_and_vibe_on_native_linux.md, for the cells of the Kimi 2.2.0 window (M-K3, M-K4, M-K6, M-K12):
// every arm — through the shim, in `-p`, or through the small client — carries an owned root (the shim refuses a Kimi turn without --home, --persons-bin
// and --locks, E4.3; nothing may run on the person's real KIMI_CODE_HOME), and the launch REFUSES an arm that lost it. The arms are read off the table with
// fakes for the probe's helpers: nothing is launched, no file is written, no credential is opened.
import assert from 'node:assert/strict';
import { join } from 'node:path';
import { test } from 'node:test';
import { ownedRefusal, rootOfHome, shimRefusal } from './kimi.mjs';
import { builtEnv } from './kimi-home.mjs';
import { ownedShimArms } from './kimi-arms.mjs';

const RUN = join('scratch', 'run-1');
const CANARY = { skillName: 'coai-probe-canary-ab12cd', word: 'GANNET-7' };
/** The probe's helpers as fakes: `write` records nothing, `ownedHome` hands back the product's layout (`<runDir>/kh/home`) and counts its calls. */
function table() {
  const homes = [];
  const ownedHome = (runDir) => {
    const path = join(runDir, 'kh', 'home');
    homes.push(path);
    return { path, notes: 'holds: (junction only)' };
  };
  return { arms: ownedShimArms({ write: () => {}, ownedHome, pySnippet: (p) => `python3 -c "touch ${p}"`, agentsCanary: CANARY }), homes };
}

const specs = (arms) => Object.entries(arms).flatMap(([cell, list]) => list.flatMap((a) => [1, 2, 3].map((n) => ({ cell, a, n, spec: a.setup(join(RUN, `${cell}-${a.arm}-${n}`), n) }))));
const slashed = (p) => p.replaceAll('\\', '/');

test('S-N4: the table holds M-K3, M-K4, M-K6 and M-K12 — the arms of the window', () => {
  const { arms } = table();

  assert.deepEqual(Object.keys(arms).sort(), ['M-K12', 'M-K3', 'M-K4', 'M-K6']);
  assert.deepEqual(arms['M-K3'].map((a) => a.arm), ['Write', 'control -p: Write', 'Edit', 'control -p: Edit', 'Bash', 'control -p: Bash']);
  assert.deepEqual(arms['M-K4'].map((a) => a.arm), ['Bash run_in_background', 'Agent sub-agent', 'control -p: Agent sub-agent', 'tool inventory']);
  assert.deepEqual(arms['M-K6'].map((a) => a.arm), ['Read inside + absolute outside', 'Grep + Glob on an outside path']);
  assert.equal(arms['M-K12'].length, 3);
});

test('S-N4: EVERY arm of every cell has an owned root and owned: true, whatever its kind', () => {
  for (const { cell, a, n, spec } of specs(table().arms)) {
    assert.notEqual(rootOfHome(spec.home ?? ''), '', `${cell} ${a.arm} has no owned root — the shim refuses it (E4.3), and the control would run on the real home (run ${n})`);
    assert.equal(spec.owned, true, `${cell} ${a.arm}: the home is not marked owned — the built environment and the disposal would be skipped`);
  }
});

test('S-N4: the launch REFUSES an arm that has no owned root — by name, before anything starts', () => {
  for (const { cell, a, n, spec } of specs(table().arms)) {
    assert.equal(ownedRefusal(spec), '', `${cell} ${a.arm}: an owned arm was refused`);
    for (const stripped of [{ ...spec, home: undefined }, { ...spec, home: join('some', 'h1') }, { ...spec, owned: false }]) {
      assert.match(ownedRefusal(stripped), /no owned root/u, `${cell} ${a.arm} (run ${n}) was not refused without an owned root`);
    }
  }
  assert.equal(ownedRefusal({ workdir: 'w', prompt: 'p' }), '', 'an arm that never asked for an owned root (a legacy cell) is not judged here');
});

test('S-N4: no shim arm is one the shim refuses by name (the OS-home redirect and the pin are the shim\'s own)', () => {
  const shimArms = specs(table().arms).filter((s) => (s.spec.kind ?? 'shim') === 'shim');

  assert.ok(shimArms.length > 12, 'the loop would check nothing');
  for (const { cell, a, spec } of shimArms) assert.equal(shimRefusal(spec), '', `${cell} ${a.arm} cannot be run through the shim`);
});

test('S-N4: every -p control, and every small-client arm, runs in the built environment of its owned home', () => {
  const direct = specs(table().arms).filter((s) => s.spec.kind === 'prompt' || s.spec.kind === 'acp');

  assert.ok(direct.length >= 15, 'M-K3 (3 controls), M-K4 (1) and M-K12 (2) at three runs each');
  for (const { cell, a, n, spec } of direct) {
    const env = builtEnv({ home: slashed(spec.home), profile: slashed(spec.profile ?? ''), platform: 'linux', parent: { PATH: '/usr/bin', HOME: '/home/op' } });
    assert.equal(env.KIMI_CODE_HOME, slashed(spec.home), `${cell} ${a.arm} (run ${n})`);
    const root = slashed(rootOfHome(spec.home));
    if (spec.profile) {
      assert.equal(env.HOME, `${root}/os`, `${cell} ${a.arm}: HOME is the operator's, so the control fills the real OS home`);
      assert.equal(env.XDG_CACHE_HOME, `${root}/cache`);
    } else {
      assert.equal(env.HOME, '/home/op', `${cell} ${a.arm}: the real OS home is the thing M-K12 (a) measures`);
    }
  }
});

test('S-N4: M-K12 keeps its question — (a) and the control read the REAL OS home, (b) the shim\'s redirected one — and names the canary it expects', () => {
  const arms = table().arms['M-K12'];
  const [a, b, control] = arms.map((arm) => arm.setup(join(RUN, arm.arm), 1));

  assert.deepEqual([a.kind, a.profile], ['acp', '']);
  assert.equal(b.kind ?? 'shim', 'shim');
  assert.equal(slashed(b.profile ?? ''), `${slashed(rootOfHome(b.home ?? ''))}/os`);
  assert.deepEqual([control.kind, control.profile, arms[2].control], ['prompt', '', true]);
  for (const spec of [a, b, control]) assert.deepEqual(spec.expect, { skill: CANARY.skillName, agentsMd: CANARY.word });
});

test('J2/G24: M-K12 (a) and its -p control keep the REAL OS home but name the cache of the owned root, so the native files of Kimi never land in the real ~/.cache', () => {
  const arms = table().arms['M-K12'];
  const direct = [arms[0], arms[2]].map((arm) => arm.setup(join(RUN, arm.arm), 1));

  for (const spec of direct) {
    const env = builtEnv({ home: slashed(spec.home), profile: slashed(spec.profile ?? ''), platform: 'linux', parent: { PATH: '/usr/bin', HOME: '/home/op' } });
    assert.equal(env.HOME, '/home/op', 'HOME must stay real — the canary measurement needs it');
    assert.equal(env.XDG_CACHE_HOME, `${slashed(rootOfHome(spec.home))}/cache`, 'the arm has no owned XDG_CACHE_HOME — Kimi extracts into the real ~/.cache');
  }
});

test('S-N4: each run gets its OWN owned home, and the arms\' prompts and canaries are untouched', () => {
  const { arms, homes } = table();

  const all = specs(arms);

  assert.equal(new Set(homes).size, homes.length, 'two runs shared an owned home');
  assert.equal(homes.length, all.length, 'an arm that makes no owned home');
  assert.match(all.find((s) => s.cell === 'M-K3' && s.a.arm === 'Bash').spec.prompt, /python3 -c "touch .*canary-bash-outside\.txt"/u);
  assert.deepEqual(Object.keys(all.find((s) => s.cell === 'M-K3' && s.a.arm === 'Bash').spec.canaries), ['bashOutside']);
  assert.deepEqual(Object.keys(all.find((s) => s.a.arm === 'Bash run_in_background').spec.canaries), ['background']);
  assert.equal(all.find((s) => s.a.arm === 'Agent sub-agent').spec.deadline, 420);
  assert.deepEqual(Object.keys(all.find((s) => s.a.arm === 'Grep + Glob on an outside path').spec.expect), ['grepMarker', 'globName']);
});
