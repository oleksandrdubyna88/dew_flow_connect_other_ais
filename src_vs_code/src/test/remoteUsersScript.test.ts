import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { delimiter, join } from 'node:path';
import test from 'node:test';
import { USERS_SCRIPT, USERS_UNREADABLE_SAID, UsersRead, usersOf } from '../remoteUsersScript';
import { ownedTemp } from './ownedTemp';

/**
 * The users script and its reader (E3.S1, spec § W6; E3S1-T10 to T13). The pin, the script's own rules run by `sh -s` with the two `/etc/passwd`
 * words replaced by a file of the test, and the reader of the reply.
 */

const SPEC = join(__dirname, '..', '..', '..', 'todo', 'PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E3_S1_spec.md');
const PINNED = 'f3dc140c82526d128f5881def682a17fe074dfb336585230507a7471329e7d30';
const sha256 = (text: string): string => createHash('sha256').update(text).digest('hex');
const SH = (process.env['PATH'] ?? '').split(delimiter).map((dir) => join(dir, 'sh')).find((path) => existsSync(path));
const NO_SH = SH === undefined ? 'no POSIX sh on this machine' : false;

/** The first `sh` block after the heading `## W6`, with its last line end. */
function specBlock(spec: string): string {
  const heading = spec.indexOf('\n## W6');
  assert.ok(heading >= 0, 'the spec has no heading that starts "## W6"');
  const open = spec.indexOf('```sh\n', heading);
  assert.ok(open >= 0, 'no sh block after the heading');
  const start = open + '```sh\n'.length;

  return spec.slice(start, spec.indexOf('\n```', start) + 1);
}

test('E3S1-T10: the sha256 of USERS_SCRIPT is the pin, the pin line of the spec says the same, and the spec\'s block IS the constant', () => {
  assert.equal(sha256(USERS_SCRIPT), PINNED, 'the text of USERS_SCRIPT changed without the pin');
  assert.equal(USERS_SCRIPT.split('\n').length - 1, 39, 'the text is 39 lines');
  assert.equal(Buffer.byteLength(USERS_SCRIPT), 1106);
  assert.ok(USERS_SCRIPT.endsWith('\n'));
  assert.equal([...USERS_SCRIPT].every((char) => char.charCodeAt(0) < 128), true, 'ASCII only');
  assert.equal(USERS_SCRIPT.includes('`'), false, 'no backtick');
  const spec = readFileSync(SPEC, 'utf8');
  const pin = /^Pinned sha256: `([0-9a-f]{64})`/mu.exec(spec);
  assert.equal(pin?.[1], PINNED, 'the spec and the test pin different values');
  assert.equal(specBlock(spec) === USERS_SCRIPT, true, 'the spec\'s block and the constant differ');
  assert.notEqual(sha256(`${USERS_SCRIPT} `), PINNED);
});

interface Entry { readonly name: string; readonly uid: string; readonly home?: string; readonly shell?: string }

/** The pinned text with its two `/etc/passwd` words replaced by `file`; the replacement is asserted to be exactly two. */
function withPasswd(file: string): string {
  assert.equal(USERS_SCRIPT.split('/etc/passwd').length - 1, 2, 'the script names /etc/passwd twice');

  return USERS_SCRIPT.replaceAll('/etc/passwd', file);
}

function run(script: string, operands: readonly string[] = [], env: NodeJS.ProcessEnv = { PATH: '' }): string {
  const got = spawnSync(SH as string, ['-s', '--', ...operands], { input: script, encoding: 'utf8', env, timeout: 20_000 });
  assert.equal(got.status, 0, `sh ended with ${got.status}: ${got.stderr}`);

  return got.stdout;
}

/** A passwd file of the entries (each with a made home folder unless `home` is given as `none`), and the script's answer to it. */
function answerFor(entries: readonly Entry[], operands: readonly string[] = [], finalLf = true): readonly string[] {
  const root = ownedTemp('users');
  const lines = entries.map((one, at) => {
    const home = one.home === 'none' ? join(root, 'missing', String(at)) : (one.home ?? join(root, 'home', String(at)));
    if (one.home === undefined) {
      mkdirSync(home, { recursive: true });
    }

    return `${one.name}:x:${one.uid}:${one.uid}::${home}:${one.shell ?? '/bin/bash'}`;
  });
  const file = join(root, 'passwd');
  writeFileSync(file, `${lines.join('\n')}${finalLf ? '\n' : ''}`);

  return run(withPasswd(file), operands).split('\n').filter((line) => line !== '');
}

const printed = (lines: readonly string[]): readonly string[] => lines.filter((line) => line.startsWith('COAI-USER '));

test('E3S1-T11: the script prints the users that pass every rule, and none that fails one — for root as for a user', { skip: NO_SH }, () => {
  const lines = answerFor([
    { name: 'root', uid: '0' },
    { name: 'low', uid: '1000' },
    { name: 'high', uid: '59999' },
    { name: 'daemon', uid: '999' },
    { name: 'toobig', uid: '60000' },
    { name: 'toor', uid: '0' },
    { name: 'fiver', uid: '5' },
    { name: 'Capital', uid: '1001' },
    { name: 'leading', uid: '01002' },
    { name: 'nohome', uid: '1003', home: 'none' },
    { name: 'nologin1', uid: '1004', shell: '/usr/sbin/nologin' },
    { name: 'nologin2', uid: '1005', shell: 'nologin' },
    { name: 'false1', uid: '1006', shell: '/bin/false' },
    { name: 'false2', uid: '1007', shell: 'false' },
    { name: 'emptyshell', uid: '1008', shell: '' },
  ]);
  assert.equal(lines[0], 'COAI-USERS 1');
  assert.equal(lines.at(-1), 'COAI-USERS-END');
  assert.deepEqual(printed(lines), ['COAI-USER root 0', 'COAI-USER low 1000', 'COAI-USER high 59999', 'COAI-USER emptyshell 1008']);
  // `root` with another UID and UID 0 under another name are never printed, whoever runs the script.
  assert.equal(lines.some((line) => /toor|fiver|daemon|toobig|Capital|leading|nohome|nologin|false/u.test(line)), false);
});

test('E3S1-T11: 17 users that count give 16 lines and COAI-MORE 1; an operand gives COAI-BAD operands', { skip: NO_SH }, () => {
  const many = Array.from({ length: 17 }, (_, at) => ({ name: `u${at}`, uid: String(1000 + at) }));
  const lines = answerFor(many);
  assert.equal(printed(lines).length, 16);
  assert.ok(lines.includes('COAI-MORE 1'));
  assert.deepEqual(answerFor([{ name: 'root', uid: '0' }], ['x']), ['COAI-USERS 1', 'COAI-BAD operands', 'COAI-USERS-END']);
});

test('E3S1-T11: the last entry of a passwd with NO trailing line end is read like the others', { skip: NO_SH }, () => {
  const lines = answerFor([{ name: 'root', uid: '0' }, { name: 'alice', uid: '1000' }], [], false);
  assert.deepEqual(printed(lines), ['COAI-USER root 0', 'COAI-USER alice 1000'], 'the last entry was lost');
  assert.deepEqual(printed(answerFor([{ name: 'root', uid: '0' }], [], false)), ['COAI-USER root 0'], 'a one-entry file with no line end');
  assert.deepEqual(printed(answerFor([{ name: 'root', uid: '0' }, { name: 'daemon', uid: '999' }], [], false)), ['COAI-USER root 0'], 'a last entry that fails a rule is still not printed');
});

test('E3S1-T11: a passwd the script cannot read is COAI-BAD passwd', { skip: NO_SH }, () => {
  const lines = run(withPasswd(join(ownedTemp('nopasswd'), 'absent'))).split('\n').filter((line) => line !== '');
  assert.deepEqual(lines, ['COAI-USERS 1', 'COAI-BAD passwd', 'COAI-USERS-END']);
});

test('E3S1-T12: the pinned text as it is, on the runner\'s own passwd, with a PATH that holds no program: framed, with COAI-USER root 0', { skip: NO_SH || !existsSync('/etc/passwd') || !existsSync('/root') }, () => {
  const lines = run(USERS_SCRIPT).split('\n').filter((line) => line !== '');
  assert.equal(lines[0], 'COAI-USERS 1');
  assert.equal(lines.at(-1), 'COAI-USERS-END');
  assert.ok(lines.includes('COAI-USER root 0'), `no root line in ${JSON.stringify(lines)}`);
});

// ───────────────── usersOf ─────────────────

const frame = (...lines: readonly string[]): { readonly code: number; readonly stdout: string } => ({ code: 0, stdout: `COAI-USERS 1\n${lines.join('\n')}${lines.length === 0 ? '' : '\n'}COAI-USERS-END\n` });
const why = (read: UsersRead): string => (read.kind === 'unreadable' ? read.why : 'read');

test('E3S1-T13: usersOf reads a good reply, and ONE line outside the rules makes the whole reply unreadable and gives no user', () => {
  const good = usersOf(frame('COAI-USER root 0', 'COAI-USER alice 1000'));
  assert.deepEqual(good, { kind: 'users', users: [{ name: 'root', uid: 0 }, { name: 'alice', uid: 1000 }], more: 0 });
  const bad: Readonly<Record<string, readonly string[]>> = {
    'UID 500': ['COAI-USER root 0', 'COAI-USER bob 500'],
    'toor 0': ['COAI-USER toor 0'],
    'root 5': ['COAI-USER root 5'],
    'a bad name': ['COAI-USER Bad 1000'],
    'a name with a leading dash': ['COAI-USER -x 1000'],
    'UID with a leading zero': ['COAI-USER bob 01000'],
    'UID 60000': ['COAI-USER bob 60000'],
    'a name twice': ['COAI-USER bob 1000', 'COAI-USER bob 1001'],
    'an unknown line': ['COAI-USER bob 1000', 'something else'],
    'more with fewer than 16': ['COAI-USER bob 1000', 'COAI-MORE 3'],
    'a user after more': [...Array.from({ length: 16 }, (_, at) => `COAI-USER u${at} ${1000 + at}`), 'COAI-MORE 1', 'COAI-USER late 2000'],
    'passwd': ['COAI-BAD passwd'],
    'operands': ['COAI-BAD operands'],
    'a bad word': ['COAI-BAD other'],
  };
  for (const [name, lines] of Object.entries(bad)) {
    assert.equal(usersOf(frame(...lines)).kind, 'unreadable', name);
  }
  const seventeen = Array.from({ length: 17 }, (_, at) => `COAI-USER u${at} ${1000 + at}`);
  assert.equal(why(usersOf(frame(...seventeen))), 'cap', '17 users');
  assert.equal(why(usersOf(frame('COAI-USER ' + 'x'.repeat(40) + ' 1000'))), 'rule', 'a name past the class');
  const sixteen = Array.from({ length: 16 }, (_, at) => `COAI-USER u${at} ${1000 + at}`);
  assert.deepEqual(usersOf(frame(...sixteen, 'COAI-MORE 4')).kind, 'users');
});

test('E3S1-T13: the frame and the exit code: another exit, no begin line, no end line, another version, a CR line end', () => {
  assert.equal(why(usersOf({ code: 1, stdout: frame('COAI-USER root 0').stdout })), 'exit');
  assert.equal(why(usersOf({ code: 0, stdout: 'hello\n' })), 'no-sentinel');
  assert.equal(why(usersOf({ code: 0, stdout: 'COAI-USERS 1\nCOAI-USER root 0\n' })), 'cut');
  assert.equal(why(usersOf({ code: 0, stdout: 'COAI-USERS 2\nCOAI-USERS-END\n' })), 'version');
  assert.equal(why(usersOf({ code: 0, stdout: 'COAI-USERS 1\r\nCOAI-USER root 0\r\nCOAI-USERS-END\r\n' })), 'version', 'a CR makes the begin line another one');
  assert.equal(why(usersOf({ code: 0, stdout: 'COAI-USERS 1\nCOAI-USER root 0\r\nCOAI-USERS-END\n' })), 'shape');
  assert.equal(why(usersOf(frame(...Array.from({ length: 19 }, () => 'COAI-USER root 0')))), 'shape', 'more than 18 lines');
  assert.equal(why(usersOf(frame('COAI-USER ' + 'y'.repeat(70) + ' 1000'))), 'shape', 'a line of more than 64 bytes');
  for (const word of Object.keys(USERS_UNREADABLE_SAID)) {
    assert.ok((USERS_UNREADABLE_SAID as Record<string, string>)[word]?.length ?? 0 > 0);
  }
});
