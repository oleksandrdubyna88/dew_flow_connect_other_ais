// node --test scripts/lib/*.test.mjs — the Windows half of the native-Linux run scripts (E1.S1 of
// todo/PLAN_kimi_and_vibe_on_native_linux_E1.md, DN11 of the parent plan): the lead renders a root-run script from a template and
// builds every ssh argument array, and NOTHING that reaches a root shell may be unchecked. Pure: no process is started, no file
// is written, so it runs on every platform. The bash scripts these strings are for are tested in scripts/native/native.test.mjs.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { abortArgv, doneArgv, launcherArgv, makeSeriesArgv, mkdirArgv, removeArgv, renderRunScript } from './native-run.mjs';

const SERIES = '/tmp/testsToDelete-native-vibe-20261021-a1b2c3';
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
  refusal(() => rendered({ series: '/tmp/testsToDelete-native-other-20261021-a1b2c3' }));
  refusal(() => rendered({ series: '/var/tmp/testsToDelete-native-vibe-20261021-a1b2c3' }));
  refusal(() => rendered({ series: '/tmp/testsToDelete-native-vibe-20261021-A1B2C3' }));
});

test('a probe name or a probe argument outside its pattern is refused — they are written into a script root runs', () => {
  for (const arg of ['$(id)', '`id`', 'a b', 'a;b', "a'b", 'a"b', 'a|b', 'a&b', 'a>b', '', '*', 'a\nb', '--x=$HOME']) refusal(() => rendered({ args: ['--ok', arg] }));
  for (const probe of ['x.sh', '../x.mjs', 'a b.mjs', 'x.mjs;id', '', '.mjs', 'dir/x.mjs']) refusal(() => rendered({ probe }));
  assert.doesNotThrow(() => rendered({ args: ['--vibe', '/usr/local/bin/vibe', '--cells', 'M-V6,M-V1', '--user=u@h:1'] }));
});

test('a series root other than /tmp is accepted only when the caller names it, and only in a safe shape', () => {
  const root = '/tmp/testsToDelete-native-root-x1';
  const series = `${root}/testsToDelete-native-kimi-20261021-00ff00`;
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
