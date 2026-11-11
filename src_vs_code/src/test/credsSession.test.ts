import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { bodyOf, sourceOf } from './sourceReading';
import { NO_LOCAL_SESSION, credsSession, walksLocalHome } from '../credsSession';
import { sourceOfFile, sourceOfSession, sourceOfTerminal } from '../chatStore';

/**
 * Which terminal reads a Claude session (E2.S1): the pure rule, and the two places that must not walk the LOCAL home for
 * a terminal. The pattern is the plan's own; what these tests pin is that nothing wider than it is ever read as a host or
 * a session, because both go to `ssh` and to a remote shell in a later story.
 */

const SHAPE = 'This terminal’s name is not `Creds: <host> / <session>`, so its Claude session cannot be read. Only the text you copied can be sent.';
const hostSentence = (part: string): string =>
  `The host “${part}” in this terminal’s name is not a name ssh can be given safely (letters, digits, \`.\`, \`_\` and \`-\`, not first), so its Claude session is not read.`;
const sessionSentence = (part: string): string =>
  `The tmux session “${part}” in this terminal’s name cannot be read: only letters, digits, \`_\` and \`-\` are allowed (tmux changes a \`.\`).`;

test('a Creds name gives its host and session', () => {
  assert.deepEqual(credsSession('Creds: ai1-dev2 / access-slack-integration'), {
    kind: 'session',
    host: 'ai1-dev2',
    session: 'access-slack-integration',
  });
  assert.deepEqual(credsSession('Creds: a.b-c_d / s_1-2'), { kind: 'session', host: 'a.b-c_d', session: 's_1-2' }, 'a host with a dot was refused');
});

test('other terminals are other, never malformed', () => {
  for (const name of ['bash', 'pwsh', 'Creds run: x', 'Creds VPN: x', 'creds: h / s', '']) {
    assert.deepEqual(credsSession(name), { kind: 'other' }, `${name} was read as a creds terminal`);
  }
});

test('a bad Creds name says which part failed', () => {
  const shape = ['Creds: before alice', 'Creds: h / s / t', 'Creds: '];
  for (const name of shape) {
    assert.deepEqual(credsSession(name), { kind: 'malformed', refusal: SHAPE }, `the refusal does not name the part that failed: ${name}`);
  }
  const host: ReadonlyArray<readonly [string, string]> = [
    ['Creds: -h / s', '-h'],
    ['Creds: h;id / s', 'h;id'],
    ['Creds: h$(x) / s', 'h$(x)'],
    ['Creds: h s / t', 'h s'],
  ];
  for (const [name, part] of host) {
    assert.deepEqual(credsSession(name), { kind: 'malformed', refusal: hostSentence(part) }, `the refusal does not name the part that failed: ${name}`);
  }
  const session: ReadonlyArray<readonly [string, string]> = [
    ['Creds: h / s.x', 's.x'],
    ['Creds: h / s ', 's '],
    ['Creds: h / ', ''],
  ];
  for (const [name, part] of session) {
    assert.deepEqual(credsSession(name), { kind: 'malformed', refusal: sessionSentence(part) }, `the refusal does not name the part that failed: ${name}`);
  }
});

test('a long bad part is quoted at 80 characters and marked as cut', () => {
  const long = 'h'.repeat(300);
  const said = credsSession(`Creds: ${long} / s`);

  assert.equal(said.kind, 'session', 'the fixture is a legal name');
  const bad = credsSession(`Creds: -${long} / s`);
  assert.equal(bad.kind, 'malformed');
  assert.equal(bad.kind === 'malformed' ? bad.refusal : '', hostSentence(`-${'h'.repeat(79)}…`), 'the quoted part is not cut at 80 characters');
});

test('the hostile names never pass', () => {
  const hostile = [';', '`', '$(x)', '\'', '"', '\\', '|', '&', '>', '*', ' ', '\t', '\u0000', '\n', '\u00e9', '.'];
  const names: string[] = [];
  for (const bit of hostile) {
    names.push(`Creds: a${bit}b / s`);
    names.push(`Creds: h / a${bit}b`);
  }
  // Leading, an option for ssh or for tmux: only a LEADING `-` is dangerous, and the pattern refuses it.
  for (const lead of ['-o', '--', '-']) {
    names.push(`Creds: ${lead}x / s`, `Creds: h / ${lead}x`);
  }
  names.push(`Creds: ${'h'.repeat(300)}\n / s`, 'Creds: -o ProxyCommand=x / s', 'Creds: h / ' + 's'.repeat(300) + '!');
  // The one legal control: a `.` inside a HOST is allowed (and not inside a session), which proves the generator is not making every name bad.
  const passed = names.filter((name) => credsSession(name).kind !== 'malformed');

  assert.deepEqual(passed, ['Creds: a.b / s'], `a hostile name was read as a session, or the legal control was refused: ${JSON.stringify(passed)}`);
  assert.ok(names.length >= 30, `only ${names.length} names were tried`);
});

test('a Creds terminal conversation never walks the local home, through the seam', () => {
  assert.equal(walksLocalHome(sourceOfTerminal('Creds: h / s'), true), false, 'a terminal conversation walked the local home');
  assert.equal(walksLocalHome(sourceOfTerminal('bash'), true), false, 'a terminal conversation walked the local home');
  assert.equal(walksLocalHome({ kind: 'none' }, true), true);
  assert.equal(walksLocalHome(sourceOfSession('9f1c-uuid'), true), true);
  assert.equal(walksLocalHome(sourceOfFile('file:///a/b.md'), true), true);
  assert.equal(walksLocalHome({ kind: 'none' }, false), false);
  assert.equal(walksLocalHome(sourceOfSession('9f1c-uuid'), false), false);

  // The companion, the only source check: the FIRST statement of the pin asks the seam, before any walk.
  const pin = bodyOf(sourceOf('chatSessionJoin.ts'), 'export function pinSession(');
  assert.match(
    pin,
    /^export function pinSession\([^)]*\): void \{\n(?: {2}\/\/[^\n]*\n)* {2}if \(!walksLocalHome\(source, fromSession\) \|\| /u,
    'the first statement of pinSession is not the walksLocalHome guard',
  );
  assert.ok(pin.indexOf('walksLocalHome(') < pin.indexOf('findSessionIn('), 'the guard comes after the walk');
});

test('the Asked fallback never walks for a terminal, and says why', () => {
  assert.equal(
    NO_LOCAL_SESSION,
    'This chat came from a terminal: its Claude session is not on this computer, so it is not looked for here.',
    'the person is not told why Asked found nothing',
  );
  const resolve = bodyOf(sourceOf('chatSessionJoin.ts'), 'export async function resolveAndPin(');
  const guard = resolve.indexOf('if (!walksLocalHome(mine.source, true))');

  assert.ok(guard > 0, 'resolveAndPin does not ask the seam for the source');
  assert.match(resolve, /return \{ kind: 'none', refusal: NO_LOCAL_SESSION \};/u, 'the person is not told why Asked found nothing');
  assert.ok(guard < resolve.indexOf('await findSessionById('), 'the id walk comes before the check');
  assert.ok(guard < resolve.indexOf('await findSessionIn('), 'the name walk comes before the check');
  assert.ok(resolve.indexOf('refusal: NO_LOCAL_SESSION') < resolve.indexOf('await findSessionById('), 'the refusal comes after a walk');
});
