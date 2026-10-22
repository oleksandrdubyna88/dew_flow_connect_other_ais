// node --test scripts/lib/cli-args.test.mjs - the argument parser the Vibe manifest script, the Vibe confinement probe and the native host
// probe share (E2.S0 follow-up). The defect it pins: `vibe-home-manifest.mjs --unstage` with no value fell through to measure() and RAN Vibe
// (a paid turn). A flag that takes a value and gets none - or gets another `--flag` as its value - is a USAGE error naming the flag, decided
// by this pure function before anything is launched. Nothing here starts a script or a CLI.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { FLAG_SPECS, manifestPlan, parseFlags } from './cli-args.mjs';

/** Every value-taking flag of every script, with its arity (1) - the audit, as data. */
const VALUE_FLAGS = Object.entries(FLAG_SPECS).flatMap(([script, spec]) => spec.values.map((flag) => [script, flag]));

test('the audit: each script\'s value-taking flags and booleans are exactly the ones its usage line documents', () => {
  assert.deepEqual(FLAG_SPECS.manifest.values, ['--vibe', '--scratch', '--from', '--unstage']);
  assert.deepEqual(FLAG_SPECS.manifest.booleans, ['--write', '--stage']);
  assert.deepEqual(FLAG_SPECS.vibeProbe.values, ['--vibe', '--scratch', '--repeats', '--cells', '--cli', '--arms', '--reparse']);
  assert.deepEqual(FLAG_SPECS.hostProbe.values, ['--series', '--scratch', '--kimi', '--coai-mcp', '--vibe-python']);
});

for (const [script, flag] of VALUE_FLAGS) {
  test(`${script}: ${flag} with no value is a usage error naming the flag`, () => {
    const r = parseFlags([flag], FLAG_SPECS[script]);
    assert.equal(r.ok, false, `${flag} with no value was accepted`);
    assert.ok(r.error.includes(flag), `the refusal does not name ${flag}: ${r.error}`);
  });
  test(`${script}: ${flag} followed by another --flag, or by an empty value, is a usage error naming the flag`, () => {
    for (const next of ['--write', '--vibe', '']) {
      const r = parseFlags([flag, next], FLAG_SPECS[script]);
      assert.equal(r.ok, false, `${flag} took ${JSON.stringify(next)} as its value`);
      assert.ok(r.error.includes(flag), r.error);
    }
  });
}

test('a value-taking flag with a value is read; booleans are flags; an absent flag is simply absent', () => {
  const r = parseFlags(['--vibe', '/bin/vibe', '--write'], FLAG_SPECS.manifest);
  assert.equal(r.ok, true);
  assert.deepEqual(r.values, { '--vibe': '/bin/vibe' });
  assert.deepEqual(r.flags, ['--write']);
  assert.deepEqual(parseFlags([], FLAG_SPECS.manifest), { ok: true, values: {}, flags: [], error: '' });
});

test('an unknown flag, a stray word and a repeated flag are usage errors (so --help can never start a measurement)', () => {
  for (const argv of [['--help'], ['stray'], ['--vibe', 'a', '--vibe', 'b'], ['--write', '--write']]) {
    const r = parseFlags(argv, FLAG_SPECS.manifest);
    assert.equal(r.ok, false, argv.join(' '));
    assert.ok(r.error.length > 0);
  }
});

test('a value that fails the spec\'s safe pattern is a usage error naming the flag', () => {
  for (const value of ['a b', '$(id)']) {
    const r = parseFlags(['--kimi', value], FLAG_SPECS.hostProbe);
    assert.equal(r.ok, false);
    assert.ok(r.error.includes('--kimi'));
  }
  assert.equal(parseFlags(['--kimi', '/opt/kimi/bin/kimi'], FLAG_SPECS.hostProbe).ok, true);
});

test('manifestPlan: `--unstage` alone is a usage refusal naming the flag - never a measurement', () => {
  const plan = manifestPlan(['--unstage']);
  assert.equal(plan.mode, 'usage', `--unstage with no value planned "${plan.mode}" - that is the fall-through that started Vibe`);
  assert.ok(plan.error.includes('--unstage'));
});

test('manifestPlan: the modes a person asks for', () => {
  assert.equal(manifestPlan([]).mode, 'measure');
  assert.equal(manifestPlan(['--vibe', '/x/vibe', '--write']).mode, 'measure');
  assert.equal(manifestPlan(['--from', '/s']).mode, 'from');
  const unstage = manifestPlan(['--unstage', 'linux']);
  assert.equal(unstage.mode, 'unstage');
  assert.equal(unstage.values['--unstage'], 'linux');
  assert.equal(manifestPlan(['--bogus']).mode, 'usage');
});

// The three scripts run on import (and two of them can start a paid CLI), so they are never imported or spawned here: a text guard pins that each one decides
// its arguments through this parser, exits 65 on a refusal, and has no private `argv.indexOf` lookup left to fall through with.
test('each script takes its arguments from lib/cli-args.mjs, exits 65 on a refusal and keeps no private indexOf lookup', async () => {
  const { readFileSync } = await import('node:fs');
  const { dirname, join } = await import('node:path');
  const { fileURLToPath } = await import('node:url');
  const scripts = join(dirname(fileURLToPath(import.meta.url)), '..');
  const read = (name) => readFileSync(join(scripts, name), 'utf8');
  const manifest = read('vibe-home-manifest.mjs');
  assert.ok(manifest.includes("manifestPlan(process.argv.slice(2))") && /plan\.mode === 'usage'[\s\S]{0,400}process\.exit\(65\)/u.test(manifest), 'the manifest script no longer refuses a usage plan with exit 65');
  const probe = read('probe-vibe-confinement.mjs');
  assert.ok(probe.includes('parseFlags(argv, FLAG_SPECS.vibeProbe)') && /!parsedArgs\.ok[\s\S]{0,500}process\.exit\(65\)/u.test(probe), 'the Vibe probe no longer refuses a bad command line with exit 65');
  const host = read('probe-native-linux-host.mjs');
  assert.ok(host.includes('parseFlags(argv, FLAG_SPECS.hostProbe)') && host.includes('EXIT.badRequest'), 'the host probe no longer parses through the shared parser');
  for (const [name, text] of [['vibe-home-manifest.mjs', manifest], ['probe-vibe-confinement.mjs', probe], ['probe-native-linux-host.mjs', host]]) {
    assert.equal(text.includes('argv.indexOf('), false, `${name} still has a private argv.indexOf flag lookup - the fall-through that started Vibe`);
  }
});
