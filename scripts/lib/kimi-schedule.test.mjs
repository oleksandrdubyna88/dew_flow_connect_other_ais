// node --test scripts/lib/*.test.mjs — S-N18 of todo/PLAN_kimi_and_vibe_on_native_linux.md (DN5 item 4, cadence consult e9a9b20e decision 2): N-K2 must
// see an abrupt shim death AND the very next launch. The pure parts of scripts/probe-kimi-tree-hold.mjs live in lib/kimi-schedule.mjs so they are
// tested here without a shim, a kimi, or a process: the schedule of killed->normal PAIRS, the signal each killed arm sends, the evidence a
// SIGKILL row needs (a survivor marker existing at the kill), and each orphan's NEW parent. Makes one folder under this user's temp folder.
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, realpathSync, rmSync, utimesSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, test } from 'node:test';
import { KILL_ARMS, buildSchedule, isKilledArm, killRecord, killSignal, nextRow, orphanRecord, survivorMarkers } from './kimi-schedule.mjs';

const base = mkdtempSync(join(realpathSync.native(tmpdir()), 'testsToDelete-kimi-schedule-'));
after(() => rmSync(base, { recursive: true, force: true }));

const labels = (schedule) => schedule.map((s) => s.label);

test('S-N18: --arms killed,killed-hard,killed-rg --repeats 3 is NINE pairs, each kill followed at once by its normal launch, repeat by repeat', () => {
  const schedule = buildSchedule(['killed', 'killed-hard', 'killed-rg'], 3);

  assert.deepEqual(labels(schedule).slice(0, 4), ['killed-1', 'killed-1-next', 'killed-hard-1', 'killed-hard-1-next'],
    "the schedule ran killed-1, killed-2 and killed-3 before any normal launch — the next launch's reconcile was never observed right after a kill");
  assert.equal(schedule.length, 18);
  for (let i = 0; i < schedule.length; i += 2) {
    const [kill, next] = [schedule[i], schedule[i + 1]];
    assert.equal(isKilledArm(kill.arm), true, `entry ${i} is not a killed arm`);
    assert.equal(next.label, `${kill.label}-next`, `something runs between ${kill.label} and its next launch`);
    assert.deepEqual([next.arm, next.kind, next.after, next.n], ['normal', 'next', kill.label, kill.n]);
  }
  assert.deepEqual([...new Set(schedule.filter((s) => s.kind === 'arm').map((s) => s.n))], [1, 2, 3], 'repeat-outer: repeat 1 of every arm, then repeat 2');
  assert.deepEqual(labels(schedule).filter((l) => !l.endsWith('-next')).map((l) => l.replace(/-\d$/u, '')),
    ['killed', 'killed-hard', 'killed-rg', 'killed', 'killed-hard', 'killed-rg', 'killed', 'killed-hard', 'killed-rg']);
});

test('S-N18: arms that are not killed run as before, with no pair; they interleave with the pairs repeat by repeat', () => {
  const schedule = buildSchedule(['normal', 'killed', 'normal-tools'], 2);

  assert.deepEqual(labels(schedule), ['normal-1', 'killed-1', 'killed-1-next', 'normal-tools-1', 'normal-2', 'killed-2', 'killed-2-next', 'normal-tools-2']);
  assert.deepEqual(schedule.filter((s) => s.kind === 'next').map((s) => s.arm), ['normal', 'normal']);
});

test('S-N18: killed-hard sends SIGKILL, killed and killed-rg SIGTERM — the shim handles TERM and ends the agent itself, so only KILL is abrupt', () => {
  assert.equal(killSignal('killed-hard'), 'SIGKILL', 'killed-hard does not send SIGKILL — a clean next launch would prove nothing about an abandoned marker');
  assert.equal(killSignal('killed'), 'SIGTERM');
  assert.equal(killSignal('killed-rg'), 'SIGTERM');
  assert.deepEqual(Object.keys(KILL_ARMS), ['killed', 'killed-hard', 'killed-rg']);
  assert.equal(isKilledArm('normal'), false);
});

test('S-N18: a killed-hard row with no marker seen at the kill says "not evidence"; with one it counts; the other arms need none', () => {
  const none = killRecord({ arm: 'killed-hard', markers: [], survivors: [], listing: [] });
  const some = killRecord({ arm: 'killed-hard', markers: [{ name: '<hex>.survivors.json', mtimeMs: 1234 }], survivors: [], listing: [] });
  const term = killRecord({ arm: 'killed', markers: [], survivors: [], listing: [] });

  assert.match(none.evidence, /^not evidence/u);
  assert.match(some.evidence, /^counts/u);
  assert.deepEqual(some.markerAtKill, [{ name: '<hex>.survivors.json', mtimeMs: 1234 }]);
  assert.deepEqual([none.signal, some.signal, term.signal], ['SIGKILL', 'SIGKILL', 'SIGTERM']);
  assert.doesNotMatch(term.evidence, /not evidence/u, 'a TERM row is not judged by a marker');
});

test('S-N18: each survivor is recorded with its NEW parent — pid, comm and exe basename — read from the listing after the kill', () => {
  const listing = [
    { pid: 1, ppid: 0, comm: 'systemd', name: 'systemd' },
    { pid: 900, ppid: 1, comm: '(sd-pam)', name: '(sd-pam)' },
    { pid: 321, ppid: 900, comm: 'MainThread', name: 'kimi' },
    { pid: 322, ppid: 1, comm: 'rg', name: 'rg' },
  ];

  const record = killRecord({ arm: 'killed-hard', markers: [], survivors: [{ pid: 321, comm: 'MainThread', name: 'kimi' }, { pid: 322, comm: 'rg', name: 'rg' }], listing });

  assert.deepEqual(record.orphans, [
    { pid: 321, comm: 'MainThread', exe: 'kimi', newParent: { pid: 900, comm: '(sd-pam)', exe: '(sd-pam)' } },
    { pid: 322, comm: 'rg', exe: 'rg', newParent: { pid: 1, comm: 'systemd', exe: 'systemd' } },
  ]);
});

test('S-N18: a survivor whose new parent is not in the listing keeps the parent pid and says nothing else; one that left the listing is marked gone', () => {
  const listing = [{ pid: 321, ppid: 77, comm: 'MainThread', name: 'kimi' }];

  assert.deepEqual(orphanRecord({ pid: 321, comm: 'MainThread', name: 'kimi' }, listing),
    { pid: 321, comm: 'MainThread', exe: 'kimi', newParent: { pid: 77, comm: '', exe: '' } });
  assert.deepEqual(orphanRecord({ pid: 5, comm: 'x', name: 'x' }, listing), { pid: 5, comm: 'x', exe: 'x', newParent: null, gone: true });
});

test('S-N18: the -next row records the marker before it, the shim\'s report, and the marker after it — and whether the marker it found is gone', () => {
  const marker = [{ name: '<hex>.survivors.json', mtimeMs: 5 }];
  const report = { end: 'answered' };

  assert.deepEqual(nextRow({ markerBefore: marker, report, markerAfter: [] }), { markerBefore: marker, report, markerAfter: [], markerFoundAndGone: true });
  assert.equal(nextRow({ markerBefore: marker, report, markerAfter: marker }).markerFoundAndGone, false, 'a marker still there after the launch is not gone');
  assert.equal(nextRow({ markerBefore: [], report, markerAfter: [] }).markerFoundAndGone, false, 'no marker found: nothing to have cleared');
});

test('S-N18: survivorMarkers lists *.survivors.json by name (hex redacted) and mtime only — never a content, never another file', () => {
  const locks = join(base, 'locks');
  mkdirSync(locks, { recursive: true });
  writeFileSync(join(locks, '0123456789abcdef.survivors.json'), '{"survivors":[{"pid":4242,"note":"CONTENT-WORD"}]}');
  writeFileSync(join(locks, '0123456789abcdef.lock'), '');
  writeFileSync(join(locks, '0123456789abcdef.tree.lock'), '');
  utimesSync(join(locks, '0123456789abcdef.survivors.json'), new Date(1_700_000_000_000), new Date(1_700_000_000_000));

  const found = survivorMarkers(locks);

  assert.deepEqual(found, [{ name: '<hex>.survivors.json', mtimeMs: 1_700_000_000_000 }]);
  assert.equal(JSON.stringify(found).includes('CONTENT-WORD'), false);
  assert.deepEqual(survivorMarkers(join(base, 'no-such-folder')), []);
});
