// node --test scripts/lib/*.test.mjs — the Windows half of the native-Linux run scripts (E1.S1 of
// todo/PLAN_kimi_and_vibe_on_native_linux_E1.md, DN11 of the parent plan): the lead renders a root-run script from a template and
// builds every ssh argument array, and NOTHING that reaches a root shell may be unchecked. Pure: no process is started, no file
// is written, so it runs on every platform. The bash scripts these strings are for are tested in scripts/native/native.test.mjs.
import '../../src_vs_code/scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { abortArgv, doneArgv, launcherArgv, makeSeriesArgv, mkdirArgv, removeArgv, renderRunScript, runChecked, statArgv, statRefusal } from './native-run.mjs';

const SERIES = '/tmp/native-vibe-20261021-a1b2c3';
const HOST = 'a-host';

const rendered = (over = {}) => renderRunScript({ series: SERIES, phase: 'n-v2', probe: 'probe-vibe-confinement.mjs', args: ['--cells', 'M-V7', '--repeats=3'], ...over });
const refusal = (fn, why = 'the call was accepted — it must be refused before anything reaches a root shell') => assert.throws(fn, Error, why);

test('a run script is the template with the series, the phase and the probe command filled in — and nothing else', () => {
  const text = rendered();
  assert.ok(text.includes(`SERIES="$(cat '${SERIES}/SERIES_PATH')"`), 'the one SERIES_PATH literal');
  assert.ok(text.includes("PHASE='n-v2'"), 'the phase');
  assert.ok(text.includes(String.raw`"$NODE" 'scripts/probe-vibe-confinement.mjs' '--cells' 'M-V7' '--repeats=3' --scratch "$SERIES/$PHASE" &`), 'node by variable, the probe and its arguments, quoted');
  assert.equal(/@[A-Z_]+@/u.test(text), false, 'no mark is left unfilled');
  assert.equal(text.includes('/usr/bin/env -i'), true);
});

test('a phase or a series that fails its pattern is refused by the renderer — "renderRunScript wrote phase \'n-v2;rm\' into a root-run script"', () => {
  refusal(() => rendered({ phase: 'n-v2;rm' }), "renderRunScript wrote phase 'n-v2;rm' into a root-run script");
  refusal(() => rendered({ phase: '' }));
  refusal(() => rendered({ phase: 'N-V2' }));
  refusal(() => rendered({ phase: 'a'.repeat(33) }));
  refusal(() => rendered({ series: '/tmp/../root' }), "renderRunScript wrote series '/tmp/../root' into a root-run script");
  refusal(() => rendered({ series: `${SERIES}/` }));
  refusal(() => rendered({ series: '/tmp/native-other-20261021-a1b2c3' }), 'a wrong kind');
  refusal(() => rendered({ series: '/var/tmp/native-vibe-20261021-a1b2c3' }), 'outside the root');
  refusal(() => rendered({ series: '/tmp/native-vibe-20261021-A1B2C3' }), 'upper-case hex');
  refusal(() => rendered({ series: '/tmp/native-vibe-2026102-a1b2c3' }), 'a date one digit short');
  refusal(() => rendered({ series: '/tmp/native-vibe-202610211-a1b2c3' }), 'a date one digit long');
  refusal(() => rendered({ series: '/tmp/native-vibe-20261021-a1b2c' }), 'hex one short');
  refusal(() => rendered({ series: '/tmp/native-vibe-20261021-a1b2c3/x' }), 'a separator in the leaf');
  refusal(() => rendered({ series: '/tmp/native-vibe-20261021-a1b2c3\n' }), 'a line break in the leaf');
  // The OLD name (the test-leftovers prefix first) is no series any more: a series started under it is finished with the checkout that made it.
  const OLD_PREFIX = ['testsTo', 'Delete-'].join('');
  refusal(() => rendered({ series: `/tmp/${OLD_PREFIX}native-vibe-20261021-a1b2c3` }), 'the old prefixed name');
});

test('a probe name or a probe argument outside its pattern is refused — they are written into a script root runs', () => {
  for (const arg of ['$(id)', '`id`', 'a b', 'a;b', "a'b", 'a"b', 'a|b', 'a&b', 'a>b', '', '*', 'a\nb', '--x=$HOME']) refusal(() => rendered({ args: ['--ok', arg] }));
  for (const probe of ['x.sh', '../x.mjs', 'a b.mjs', 'x.mjs;id', '', '.mjs', 'dir/x.mjs']) refusal(() => rendered({ probe }));
  assert.doesNotThrow(() => rendered({ args: ['--vibe', '/usr/local/bin/vibe', '--cells', 'M-V6,M-V1', '--user=u@h:1'] }));
});

test('CodeRabbit (native-run.mjs): a phase named like a file or folder the series owns is refused - run/abort.sh and run/remove-series.sh would be overwritten, and --scratch would be run/, logs/, tmp/, checkout/ or bin/', () => {
  for (const phase of ['abort', 'remove-series', 'run', 'logs', 'tmp', 'checkout', 'bin']) {
    refusal(() => rendered({ phase }), `renderRunScript wrote the phase '${phase}' - it names a file or folder the series owns`);
    refusal(() => launcherArgv(SERIES, phase, { host: HOST }), `launcherArgv accepted the phase '${phase}'`);
    refusal(() => abortArgv(SERIES, phase, { host: HOST }), `abortArgv accepted the phase '${phase}'`);
  }
  assert.doesNotThrow(() => rendered({ phase: 'abort-2' }), 'only the exact names are reserved');
  assert.doesNotThrow(() => rendered({ phase: 'n-v2' }));
});

test('a probe argument that would override the series folder or switch on a host-forbidden arm is refused (J5): --scratch, --allow-agents-canary, --allow-live-survivor — bare or with a value', () => {
  // The run script appends its OWN `--scratch "$SERIES/$PHASE"`; a second one before it would move the probe out of the series, and the two
  // --allow flags switch on arms the owner permits on the Windows machine only (the person's real ~/.agents; a live survivor).
  for (const arg of ['--scratch', '--scratch=/tmp/elsewhere', '--allow-agents-canary', '--allow-agents-canary=1', '--allow-live-survivor', '--allow-live-survivor=yes']) {
    refusal(() => rendered({ args: ['--cells', 'M-K1', arg] }), `renderRunScript wrote the argument ${arg} into a root-run script`);
  }
  assert.doesNotThrow(() => rendered({ args: ['--scratch-keep', '--allow-nothing'] }), 'only the three names are refused, not every flag that starts the same');
});

test('the flags that read or write the real home of the person are refused too (re-check of J1 and item 4): --allow-copied-credentials and --allow-real-home, bare or with a value', () => {
  for (const arg of ['--allow-copied-credentials', '--allow-copied-credentials=1', '--allow-real-home', '--allow-real-home=yes']) {
    refusal(() => rendered({ args: ['--cells', 'M-K1', arg] }), `renderRunScript wrote the argument ${arg} into a root-run script`);
  }
});

test('a series root other than /tmp is accepted only when the caller names it, and only in a safe shape', () => {
  const root = '/tmp/native-root-x1';
  const series = `${root}/native-kimi-20261021-00ff00`;
  assert.ok(rendered({ series, root }).includes(`'${series}/SERIES_PATH'`));
  refusal(() => rendered({ series })); // the default root is /tmp
  refusal(() => rendered({ series, root: '/tmp/../x' }));
  refusal(() => rendered({ series, root: '/tmp/x y' }));
  refusal(() => rendered({ series, root: 'relative' }));
});

test('the launcher is an argument array: ssh, the host, then ONLY absolute remote programs — refused for a bad series or phase alike', () => {
  const argv = launcherArgv(SERIES, 'n-v2', { host: HOST });
  assert.deepEqual(argv, ['ssh', HOST, '--', '/usr/bin/nohup', '/usr/bin/setsid', '/bin/bash', '--', `${SERIES}/run/n-v2.sh`]);
  assert.ok([argv[3], argv[4], argv[5], argv[7]].every((p) => p.startsWith('/')), 'nohup, setsid, bash and the script by absolute path');
  refusal(() => launcherArgv(SERIES, 'n-v2;rm', { host: HOST }));
  refusal(() => launcherArgv('/tmp/../root', 'n-v2', { host: HOST }));
  refusal(() => launcherArgv(SERIES, 'n-v2', { host: '-oProxyCommand=x' }));
  refusal(() => launcherArgv(SERIES, 'n-v2', { host: 'a b' }));
  refusal(() => launcherArgv(SERIES, 'n-v2', {}));
});

test('every other remote step is an array of absolute programs and a checked path — abort, remove, mkdir, make-series, done-poll', () => {
  assert.deepEqual(abortArgv(SERIES, 'n-v2', { host: HOST }), ['ssh', HOST, '--', '/bin/bash', '--', `${SERIES}/run/abort.sh`, 'n-v2']);
  assert.deepEqual(removeArgv(SERIES, { host: HOST }), ['ssh', HOST, '--', '/bin/bash', '--', `${SERIES}/run/remove-series.sh`, SERIES]);
  assert.deepEqual(mkdirArgv(SERIES, { host: HOST }), ['ssh', HOST, '--', '/bin/mkdir', '-m', '700', '--', SERIES]);
  assert.deepEqual(makeSeriesArgv(SERIES, { host: HOST }), ['ssh', HOST, '--', '/bin/bash', '--', `${SERIES}/make-series.sh`]);
  assert.deepEqual(doneArgv(SERIES, 'n-v2', { host: HOST }), ['ssh', HOST, '--', '/usr/bin/test', '-f', `${SERIES}/logs/n-v2.done`]);
  for (const make of [(s, p) => abortArgv(s, p, { host: HOST }), (s) => removeArgv(s, { host: HOST }), (s) => mkdirArgv(s, { host: HOST }),
    (s) => makeSeriesArgv(s, { host: HOST }), (s, p) => doneArgv(s, p, { host: HOST })]) {
    refusal(() => make('/tmp/../root', 'n-v2'));
    refusal(() => make(`${SERIES};id`, 'n-v2'));
  }
  refusal(() => abortArgv(SERIES, 'n-v2 && id', { host: HOST }));
  refusal(() => doneArgv(SERIES, '../x', { host: HOST }));
});

// ---------------------------------------------------------------- the caller's stat check (E5 S4, the rollout's epoch 5 item 4, evidence 4), every platform

/** The text `stat -c '%n|%F|%u|%a'` prints for a series that is what it must be (uid 0), one line per path the check reads. */
function goodFacts(kind = 'abort', phase = 'n-v2', over = {}) {
  const script = { make: 'make-series.sh', remove: 'run/remove-series.sh', abort: 'run/abort.sh', phase: `run/${phase}.sh` }[kind];
  const rows = {
    '/tmp': ['directory', 0, 1777],
    [SERIES]: ['directory', 0, 700],
    [`${SERIES}/run`]: ['directory', 0, 700],
    [`${SERIES}/${script}`]: ['regular file', 0, 700],
    ...over,
  };
  return Object.entries(rows).filter(([, row]) => row !== null).map(([name, [type, uid, mode]]) => `${name}|${type}|${uid}|${mode}`).join('\n');
}
const check = (kind, stdout, options = { host: HOST }, phase = 'n-v2') => statRefusal(kind, SERIES, phase, stdout, options);

test('the stat check reads the root, the series, run/ and the script with ONE absolute stat call, never following a link — and make has no run/ yet', () => {
  assert.deepEqual(statArgv('abort', SERIES, 'n-v2', { host: HOST }),
    ['ssh', HOST, '--', '/usr/bin/stat', '-c', '%n|%F|%u|%a', '--', '/tmp', SERIES, `${SERIES}/run`, `${SERIES}/run/abort.sh`]);
  assert.deepEqual(statArgv('make', SERIES, undefined, { host: HOST }),
    ['ssh', HOST, '--', '/usr/bin/stat', '-c', '%n|%F|%u|%a', '--', '/tmp', SERIES, `${SERIES}/make-series.sh`]);
  assert.equal(statArgv('phase', SERIES, 'n-v2', { host: HOST }).at(-1), `${SERIES}/run/n-v2.sh`);
  assert.equal(statArgv('remove', SERIES, undefined, { host: HOST }).at(-1), `${SERIES}/run/remove-series.sh`);
  refusal(() => statArgv('abort', '/tmp/../root', 'n-v2', { host: HOST }));
  refusal(() => statArgv('phase', SERIES, 'abort', { host: HOST }));
  refusal(() => statArgv('other', SERIES, 'n-v2', { host: HOST }));
});

test('the stat check accepts a series that is what it must be, for every step', () => {
  for (const kind of ['make', 'remove', 'abort', 'phase']) assert.equal(check(kind, goodFacts(kind)), '', kind);
});

test('the stat check refuses each unsafe state with one line and "nothing was run": a link, a wrong owner, a wrong mode, a writable script, an unsafe root', () => {
  const cases = [
    ['a symlinked series', 'abort', { [SERIES]: ['symbolic link', 0, 777] }, /is a symbolic link, not a plain directory/],
    ['a symlinked run/', 'abort', { [`${SERIES}/run`]: ['symbolic link', 0, 777] }, /its run folder .* is a symbolic link/],
    ['a series of another owner', 'remove', { [SERIES]: ['directory', 1000, 700] }, /is owned by uid 1000, not 0/],
    ['a series of mode 755', 'abort', { [SERIES]: ['directory', 0, 755] }, /is mode 755, not 700/],
    ['a run/ of mode 770', 'phase', { [`${SERIES}/run`]: ['directory', 0, 770] }, /is mode 770, not 700/],
    ['a group-writable script', 'remove', { [`${SERIES}/run/remove-series.sh`]: ['regular file', 0, 770] }, /group- or world-writable/],
    ['a world-writable script', 'abort', { [`${SERIES}/run/abort.sh`]: ['regular file', 0, 702] }, /group- or world-writable/],
    ['a script of another owner', 'abort', { [`${SERIES}/run/abort.sh`]: ['regular file', 1000, 700] }, /is owned by uid 1000, not 0/],
    ['a script that is a link', 'abort', { [`${SERIES}/run/abort.sh`]: ['symbolic link', 0, 777] }, /is a symbolic link, not a regular file/],
    ['a script that is a directory', 'abort', { [`${SERIES}/run/abort.sh`]: ['directory', 0, 700] }, /is a directory, not a regular file/],
    ['a script that is missing', 'abort', { [`${SERIES}/run/abort.sh`]: null }, /is missing/],
    ['a world-writable root without the sticky bit', 'abort', { '/tmp': ['directory', 0, 777] }, /group- or world-writable \(mode 777\) without the sticky bit/],
    ['a group-writable root (775) without the sticky bit', 'abort', { '/tmp': ['directory', 0, 775] }, /group- or world-writable \(mode 775\) without the sticky bit/],
    ['a group-writable root (770) without the sticky bit', 'abort', { '/tmp': ['directory', 0, 770] }, /group- or world-writable \(mode 770\) without the sticky bit/],
    ['a root of another owner', 'abort', { '/tmp': ['directory', 1000, 1777] }, /series root \/tmp is owned by uid 1000/],
    ['a root that is a link', 'abort', { '/tmp': ['symbolic link', 0, 777] }, /series root \/tmp is a symbolic link/],
  ];
  for (const [name, kind, over, expected] of cases) {
    const line = check(kind, goodFacts(kind, 'n-v2', over));
    assert.match(line, expected, name);
    assert.match(line, /^refused: .* — nothing was run$/u, `${name}: one line`);
    assert.equal(line.includes('\n'), false, name);
  }
  for (const mode of [755, 700, 1777]) assert.equal(check('abort', goodFacts('abort', 'n-v2', { '/tmp': ['directory', 0, mode] })), '', `a root of mode ${mode} is accepted (not group/world-writable, or sticky)`);
  assert.match(check('abort', goodFacts('abort', 'n-v2', { '/tmp': ['directory', 0, 1777] }), { host: HOST, ownerUid: 1000 }), /owned by uid 0, not 1000/, 'the expected uid is a parameter');
  assert.equal(check('abort', goodFacts('abort').replaceAll('|0|', '|1000|'), { host: HOST, ownerUid: 1000 }), '', 'and the same facts pass for their own uid');
});

test('a series that is gone is its own line, status 66 — and nothing is run', async () => {
  const onlyRoot = '/tmp|directory|0|1777';
  assert.equal(check('phase', onlyRoot), `the series ${SERIES} is gone; start a new one with make-series.sh`);
  assert.match(check('phase', ''), /^refused: the host answered no facts/u);
  const seen = [];
  const result = await runChecked('phase', SERIES, 'n-v2', { host: HOST }, async (argv) => { seen.push(argv); return { status: 1, stdout: onlyRoot }; });
  assert.equal(result.status, 66);
  assert.match(result.refused, /is gone; start a new one with make-series\.sh/u);
  assert.equal(seen.length, 1, 'only the stat call was made; the phase launcher never was');
});

test('runChecked runs the step only after the check passed, and runs NOTHING after a refusal', async () => {
  const calls = [];
  const exec = (facts) => async (argv) => { calls.push(argv); return argv[3] === '/usr/bin/stat' ? { status: 0, stdout: facts } : { status: 0, stdout: 'ran' }; };

  const ok = await runChecked('abort', SERIES, 'n-v2', { host: HOST }, exec(goodFacts('abort')));
  assert.deepEqual([ok.status, ok.stdout, calls.length], [0, 'ran', 2]);
  assert.deepEqual(calls[1], abortArgv(SERIES, 'n-v2', { host: HOST }), 'the real step is the argv builder\'s');

  calls.length = 0;
  const bad = await runChecked('remove', SERIES, undefined, { host: HOST }, exec(goodFacts('remove', 'n-v2', { [`${SERIES}/run/remove-series.sh`]: ['regular file', 0, 777] })));
  assert.equal(bad.status, 78);
  assert.equal(calls.length, 1, 'only the stat call ran');
});
