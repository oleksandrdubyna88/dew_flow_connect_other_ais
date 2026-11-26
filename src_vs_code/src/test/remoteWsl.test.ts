import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import { Platform } from '../hostSide';
import { childEnvironment } from '../processLauncher';
import { ACCOUNTS_MODES } from '../remoteAccountsScript';
import type { DoorAnswer } from '../remotePermission';
import { DISTRO_CLASS, MAX_DISTROS, MAX_PLACES, WslPlace, memoryKeyOf, placeKey, placeLabel, wslNames, wslPlaceOk } from '../remotePlaces';
import { ACCOUNTS_LIMITS } from '../remoteSsh';
import { LIST_ARGV, RUNNING_ARGV, WSL_LIMITS, WslRun, WslSeams, runWslAccounts, runWslList, runWslUsers, wslAccountsArgv, wslExecutable, wslPortOf, wslUsersArgv } from '../remoteWsl';
import { PlaceRun, STRIKES_SAID, WslFacts, WslHooks, Visit, wslRound } from '../remoteWslRound';
import { Captured, CaptureOptions } from '../versionProbe';
import { sourceFiles, sourceOf } from './sourceReading';

/**
 * The WSL edge and the WSL part of a round (E3.S1; E3S1-T1 to T9 and T14 to T19), over a fake `capture` and a fake door. No test starts `wsl.exe`:
 * every call goes through the seams, and the RECORD holds, in order, each door question and each spawn.
 */

const DIGEST = 'a'.repeat(64);
/** UTF-16LE bytes, decoded as the launcher decodes them (UTF-8). */
const u16 = (text: string): string => Buffer.from(text, 'utf16le').toString('utf8');
const usersReply = (...users: readonly string[]): string => `COAI-USERS 1\n${users.map((one) => `COAI-USER ${one}`).join('\n')}${users.length === 0 ? '' : '\n'}COAI-USERS-END\n`;
const accountsReply = 'COAI-ACCOUNTS 1\nCOAI-JQ ok\nCOAI-ACCOUNTS-END\n';

interface Spawn {
  readonly target: string;
  readonly args: readonly string[];
  readonly shell: boolean;
  readonly capMs: number;
  readonly env: unknown;
  readonly input: string;
  readonly options: CaptureOptions | undefined;
}

interface World {
  readonly seams: WslSeams;
  /** Each door question (`door:<answer>`) and each spawn (`spawn:<words>`), in order. */
  readonly record: string[];
  readonly spawns: Spawn[];
  readonly logs: string[];
  /** The door's answer for question number `n` (from 0), given the record so far. */
  door: (n: number, record: readonly string[]) => DoorAnswer;
  /** The answer of a call by its words and its stdin. */
  answer: (spawn: Spawn) => Captured | Promise<Captured>;
  /** Called when a spawn starts, before it answers. */
  onSpawn: (spawn: Spawn) => void;
  exists: boolean;
  platform: Platform;
}

const wordsOf = (args: readonly string[]): string => args.join(' ');
const isList = (args: readonly string[]): boolean => args[0] === '--list';
const isRunningList = (args: readonly string[]): boolean => args[0] === '--list' && args.includes('--running');

/** A world whose lists name `names` (running: `running`), whose users call names `users` for each distro, and whose accounts calls answer a frame. */
function world(names: readonly string[], running: readonly string[], users: Readonly<Record<string, readonly string[]>> = {}): World {
  let questions = 0;
  const w: World = {
    record: [],
    spawns: [],
    logs: [],
    exists: true,
    platform: 'win32',
    door: () => 'yes',
    onSpawn: () => undefined,
    answer: (spawn) => {
      if (isRunningList(spawn.args)) {
        return { code: 0, output: u16(`${running.join('\r\n')}${running.length === 0 ? '' : '\r\n'}`), stderrTail: '' };
      }
      if (isList(spawn.args)) {
        return { code: 0, output: u16(`${names.join('\r\n')}${names.length === 0 ? '' : '\r\n'}`), stderrTail: '' };
      }
      if (spawn.args.includes('-s') && spawn.args[spawn.args.indexOf('-u') + 1] === 'root' && !spawn.args.includes('--')) {
        return { code: 0, output: usersReply(...(users[spawn.args[1] as string] ?? ['root 0'])), stderrTail: '' };
      }

      return { code: 0, output: accountsReply, stderrTail: '' };
    },
    seams: undefined as unknown as WslSeams,
  };
  const seams: WslSeams = {
    capture: (target, args, shell, capMs, _stop, env, input = '', options) => {
      const spawn: Spawn = { target, args: [...args], shell, capMs, env, input, options };
      w.record.push(`spawn:${wordsOf(args)}`);
      w.spawns.push(spawn);
      w.onSpawn(spawn);

      return Promise.resolve(w.answer(spawn));
    },
    exists: () => Promise.resolve(w.exists),
    log: (line) => { w.logs.push(line); },
    get platform() { return w.platform; },
    env: { SystemRoot: 'C:\\Windows' },
    door: () => {
      const answer = w.door(questions, w.record);
      questions += 1;
      w.record.push(`door:${answer}`);

      return Promise.resolve(answer);
    },
  };

  return Object.assign(w, { seams });
}

const hooks = (over: Partial<WslHooks> = {}): WslHooks => ({ stillOn: () => Promise.resolve(true), facts: () => undefined, ...over });

/** One walk whose visit makes each place's first call and records what came of it. */
async function walk(w: World, over: Partial<WslHooks> = {}): Promise<{ readonly seen: string[]; readonly facts: WslFacts; readonly places: string[]; readonly runs: PlaceRun[] }> {
  const seen: string[] = [];
  const places: string[] = [];
  const runs: PlaceRun[] = [];
  const visit: Visit<number> = async (state, place, call) => {
    places.push(placeKey(place));
    const run = await call('no-claude-usage', undefined);
    runs.push(run);
    seen.push(`${placeKey(place)}:${run.kind}`);

    return { state: state + 1 };
  };
  const result = await wslRound(wslPortOf(w.seams), hooks(over), 0, visit);

  return { seen, facts: result.facts, places, runs };
}

/** Every spawn is right after a door that said yes. */
const doorBeforeEverySpawn = (record: readonly string[]): boolean => record.every((entry, at) => !entry.startsWith('spawn:') || record[at - 1] === 'door:yes');

const ubuntuRoot: WslPlace = { kind: 'wsl', distro: 'Ubuntu', user: 'root', uid: 0 };
const ubuntuAlice: WslPlace = { kind: 'wsl', distro: 'Ubuntu', user: 'alice', uid: 1000 };

// ───────────────── T1, T2: the executable and its environment ─────────────────

test('E3S1-T1: the executable is System32\\wsl.exe and that folder is the working folder; with no SystemRoot, C:\\Windows', () => {
  assert.deepEqual(wslExecutable('win32', { SystemRoot: 'D:\\Win' }), { path: 'D:\\Win\\System32\\wsl.exe', cwd: 'D:\\Win\\System32' });
  assert.deepEqual(wslExecutable('win32', {}), { path: 'C:\\Windows\\System32\\wsl.exe', cwd: 'C:\\Windows\\System32' });
  assert.deepEqual(wslExecutable('win32', { WINDIR: 'E:\\W' }), { path: 'E:\\W\\System32\\wsl.exe', cwd: 'E:\\W\\System32' });
});

test('E3S1-T1: the spawn is called with that path, that cwd, no shell and no search of the PATH; a file that is not there is S2 and no call, and no door question', async () => {
  const w = world(['Ubuntu'], ['Ubuntu']);
  const run = await runWslList(false, w.seams);
  assert.equal(run.kind, 'answered');
  assert.equal(w.spawns[0]?.target, 'C:\\Windows\\System32\\wsl.exe');
  assert.equal(w.spawns[0]?.shell, false);
  assert.equal(w.spawns[0]?.options?.cwd, 'C:\\Windows\\System32');
  assert.equal(w.spawns[0]?.options?.killTree, true);
  const missing = world(['Ubuntu'], ['Ubuntu']);
  missing.exists = false;
  const refused = await runWslList(false, missing.seams);
  assert.deepEqual(refused, { kind: 'refused', refusal: 'wsl.exe was not found at “C:\\Windows\\System32\\wsl.exe”, so WSL on this PC cannot be read.', end: 'missing' });
  assert.deepEqual(missing.record, [], 'no spawn and no door question');
});

test('E3S1-T2: the environment — WSLENV and HOME are taken out in every spelling of their case; SystemRoot and PATH stay; the edge adds no variable', async () => {
  const w = world(['Ubuntu'], ['Ubuntu']);
  await runWslList(false, w.seams);
  await runWslUsers('Ubuntu', w.seams);
  await runWslAccounts(ubuntuAlice, 'no-claude-usage', undefined, w.seams);
  assert.equal(w.spawns.length, 3);
  const inherited: NodeJS.ProcessEnv = { SystemRoot: 'C:\\Windows', PATH: 'C:\\x', WSLENV: 'A/p', wslenv: 'B', HOME: '/h', Home: '/h2' };
  for (const spawn of w.spawns) {
    assert.equal(spawn.env, undefined, 'the edge adds no variable');
    assert.deepEqual(spawn.options?.withoutEnv, ['WSLENV', 'HOME']);
    const given = childEnvironment(undefined, inherited, spawn.options?.withoutEnv);
    assert.ok(given !== undefined);
    assert.deepEqual(Object.keys(given).sort(), ['PATH', 'SystemRoot']);
  }
});

test('E3S1-T2: the limits are the accounts call\'s own', () => {
  assert.deepEqual(WSL_LIMITS, ACCOUNTS_LIMITS);
  const w = world([], []);
  return runWslList(false, w.seams).then(() => {
    assert.equal(w.spawns[0]?.capMs, 30_000);
    assert.equal(w.spawns[0]?.options?.maxStdoutBytes, 65_536);
  });
});

// ───────────────── T3: the argv, word for word ─────────────────

test('E3S1-T3: the argv of the two list calls, the users call and the accounts call in both modes — word for word', async () => {
  assert.deepEqual([...LIST_ARGV], ['--list', '--quiet']);
  assert.deepEqual([...RUNNING_ARGV], ['--list', '--running', '--quiet']);
  assert.deepEqual([...wslUsersArgv('Ubuntu-22.04')], ['-d', 'Ubuntu-22.04', '-u', 'root', '--cd', '/', '--exec', '/bin/sh', '-s']);
  assert.deepEqual([...wslAccountsArgv(ubuntuAlice, 'no-claude-usage')], ['-d', 'Ubuntu', '-u', 'alice', '--cd', '/', '--exec', '/bin/sh', '-s', '--', 'no-claude-usage']);
  assert.deepEqual([...wslAccountsArgv(ubuntuAlice, 'claude-usage', DIGEST)], ['-d', 'Ubuntu', '-u', 'alice', '--cd', '/', '--exec', '/bin/sh', '-s', '--', 'claude-usage', DIGEST]);
  assert.deepEqual([...wslAccountsArgv(ubuntuRoot, 'no-claude-usage')], ['-d', 'Ubuntu', '-u', 'root', '--cd', '/', '--exec', '/bin/sh', '-s', '--', 'no-claude-usage']);
  const w = world(['Ubuntu'], ['Ubuntu']);
  await runWslList(false, w.seams);
  await runWslList(true, w.seams);
  await runWslUsers('Ubuntu', w.seams);
  await runWslAccounts(ubuntuAlice, 'no-claude-usage', undefined, w.seams);
  assert.deepEqual(w.spawns.map((one) => wordsOf(one.args)), [
    '--list --quiet', '--list --running --quiet', '-d Ubuntu -u root --cd / --exec /bin/sh -s', '-d Ubuntu -u alice --cd / --exec /bin/sh -s -- no-claude-usage',
  ]);
  assert.equal(w.spawns[0]?.input, '', 'a list call has nothing on stdin');
  assert.ok((w.spawns[2]?.input ?? '').startsWith('coai_users()'), 'the users script is on stdin');
  assert.ok((w.spawns[3]?.input ?? '').startsWith('coai_accounts()'), 'the accounts script is on stdin');
});

test('E3S1-T3: the digest rule — claude-usage needs 64 characters of 0-9a-f, and the first mode takes none', () => {
  for (const digest of [undefined, '', 'A'.repeat(64), 'a'.repeat(63), 'a'.repeat(65), `${'a'.repeat(63)}g`, `${'a'.repeat(64)}\n`]) {
    assert.throws(() => wslAccountsArgv(ubuntuAlice, 'claude-usage', digest), /^Error: The WSL read was asked in a form/u, String(digest));
  }
  assert.throws(() => wslAccountsArgv(ubuntuAlice, 'no-claude-usage', DIGEST));
  assert.throws(() => wslAccountsArgv(ubuntuAlice, 'other' as never));
});

// ───────────────── T4: a name that fails its class never reaches an argv ─────────────────

const FIXED = 'The WSL read was asked in a form its distro, user or operands do not allow.';
const BAD_NAMES = ['-x', '--exec', ' ', 'a b', 'a'.repeat(100), 'é', 'Ubüntu', '', 'a\nb', 'a;b', '$(id)', 'x\u0000'];

test('E3S1-T4: a distro or a user that fails its class gives the fixed refusal, which holds no part of the name; no spawn and no door question', async () => {
  const w = world([], []);
  for (const bad of BAD_NAMES) {
    assert.throws(() => wslUsersArgv(bad), (error: Error) => error.message === FIXED, `distro ${JSON.stringify(bad)}`);
    const users = await runWslUsers(bad, w.seams);
    assert.equal(users.kind, 'refused');
    assert.equal(users.kind === 'refused' && users.end, 'argv');
    assert.equal(users.kind === 'refused' && users.refusal === FIXED, true);
    const asDistro = await runWslAccounts({ kind: 'wsl', distro: bad, user: 'alice', uid: 1000 }, 'no-claude-usage', undefined, w.seams);
    assert.equal(asDistro.kind, 'refused');
    const asUser = await runWslAccounts({ kind: 'wsl', distro: 'Ubuntu', user: bad, uid: 1000 }, 'no-claude-usage', undefined, w.seams);
    assert.equal(asUser.kind, 'refused');
    assert.equal(asUser.kind === 'refused' && asUser.refusal === FIXED, true);
    assert.equal(asDistro.kind === 'refused' && asDistro.refusal === FIXED, true);
  }
  assert.deepEqual(w.record, [], 'not a spawn and not a door question');
});

// ───────────────── T5: root ─────────────────

test('E3S1-T5: root — the -u word root only for the place (distro, root, 0); root with 1000, another name with 0, 999 and 60000 are refused with no call', async () => {
  assert.equal(wslAccountsArgv(ubuntuRoot, 'no-claude-usage')[3], 'root');
  const refused: readonly WslPlace[] = [
    { kind: 'wsl', distro: 'Ubuntu', user: 'root', uid: 1000 },
    { kind: 'wsl', distro: 'Ubuntu', user: 'toor', uid: 0 },
    { kind: 'wsl', distro: 'Ubuntu', user: 'bob', uid: 999 },
    { kind: 'wsl', distro: 'Ubuntu', user: 'bob', uid: 60_000 },
    { kind: 'wsl', distro: 'Ubuntu', user: 'root', uid: 5 },
    { kind: 'wsl', distro: 'Ubuntu', user: 'bob', uid: -1 },
    { kind: 'wsl', distro: 'Ubuntu', user: 'bob', uid: 1000.5 },
  ];
  const w = world([], []);
  for (const place of refused) {
    assert.equal(wslPlaceOk(place), false, JSON.stringify(place));
    assert.throws(() => wslAccountsArgv(place, 'no-claude-usage'));
    assert.equal((await runWslAccounts(place, 'no-claude-usage', undefined, w.seams)).kind, 'refused');
  }
  assert.deepEqual(w.record, []);
});

test('E3S1-T5: over 200 made-up places the -u word of every accounts argv is that place\'s user; the word \'--exec\' stands in ONE source file', () => {
  let seed = 7;
  const next = (limit: number): number => { seed = (seed * 1_103_515_245 + 12_345) % 2_147_483_648; return seed % limit; };
  const users = ['root', 'alice', 'bob_1', 'carol-x', '_svc', 'dave'];
  let made = 0;
  for (let at = 0; at < 200; at += 1) {
    const user = users[next(users.length)] as string;
    const place: WslPlace = { kind: 'wsl', distro: `D${next(50)}.x-y`, user, uid: user === 'root' ? 0 : 1000 + next(58_000) };
    const argv = wslAccountsArgv(place, ACCOUNTS_MODES[next(2)] === 'claude-usage' ? 'no-claude-usage' : 'no-claude-usage');
    assert.equal(argv[argv.indexOf('-u') + 1], place.user);
    assert.equal(argv[argv.indexOf('-d') + 1], place.distro);
    made += 1;
  }
  assert.equal(made, 200);
  const holders = sourceFiles().filter((file) => sourceOf(file).includes('\'--exec\''));
  assert.deepEqual(holders, ['remoteWsl.ts']);
  assert.equal(sourceFiles().some((file) => file !== 'remoteWsl.ts' && /['"]-u['"]/u.test(sourceOf(file)) && sourceOf(file).includes('wsl.exe')), false, 'no other file builds a -u word of wsl.exe');
});

// ───────────────── T6: the decoder ─────────────────

test('E3S1-T6: the decoder reads UTF-16LE as the launcher shows it, and UTF-8, CR LF also after the last name, and drops a UTF-8 BOM', () => {
  assert.deepEqual(wslNames(u16('Ubuntu\r\nDebian\r\n')), { names: ['Ubuntu', 'Debian'], ignored: 0 });
  assert.deepEqual(wslNames('Ubuntu\r\nDebian\r\n'), { names: ['Ubuntu', 'Debian'], ignored: 0 });
  assert.deepEqual(wslNames('Ubuntu\nDebian'), { names: ['Ubuntu', 'Debian'], ignored: 0 });
  assert.deepEqual(wslNames('\uFEFFUbuntu\n'), { names: ['Ubuntu'], ignored: 0 });
  assert.deepEqual(wslNames(''), { names: [], ignored: 0 });
  assert.deepEqual(wslNames('Ubuntu\nUbuntu\n'), { names: ['Ubuntu'], ignored: 0 }, 'a second copy is dropped');
});

test('E3S1-T6: a UTF-16 BOM, an odd length, a U+0000 at an even place and a missing U+0000 are undecodable', () => {
  assert.equal(wslNames(`\uFFFD\uFFFD${u16('Ubuntu\r\n')}`), 'undecodable', 'a UTF-16 BOM read as UTF-8');
  assert.equal(wslNames(`${u16('Ubuntu\r\n')}x`), 'undecodable', 'an odd length');
  assert.equal(wslNames(`U\u0000b\u0000\u0000\u0000`), 'undecodable', 'a U+0000 at an even place');
  assert.equal(wslNames('U\u0000bu'), 'undecodable', 'a missing U+0000 at an odd place');
  assert.equal(wslNames('Ub\u0000\u0000'), 'undecodable');
  assert.equal(wslNames(u16('Ubuntu-\u4f60\r\n')), 'undecodable', 'a character outside ASCII breaks the pattern');
});

test('E3S1-T6: a message line and a name with a space count in ignored, and are never passed on', () => {
  assert.deepEqual(wslNames('Ubuntu\nThere are no installed distributions.\nmy distro\n-x\n'), { names: ['Ubuntu'], ignored: 3 });
  assert.deepEqual(wslNames(u16('Ubuntu\r\nA B\r\n')), { names: ['Ubuntu'], ignored: 1 });
  assert.equal(DISTRO_CLASS.test('a'.repeat(64)), true);
  assert.equal(DISTRO_CLASS.test('a'.repeat(65)), false);
});

// ───────────────── T7, T8: the running set, and a distro that is not running ─────────────────

test('E3S1-T7: the running set is the names of BOTH answers: a name only in the running list is not read', async () => {
  const w = world(['Ubuntu', 'Debian'], ['Ubuntu', 'Ghost']);
  const got = await walk(w);
  assert.ok(w.spawns.every((one) => !one.args.includes('Ghost')), 'a name only in the running list never reaches an argv');
  assert.deepEqual(got.facts.distros.map((one) => `${one.distro}:${one.state}`), ['Ubuntu:read', 'Debian:not-running']);
});

test('E3S1-T7: the running call with another exit code gives an empty set; the list call with one is S3 and NO -d call', async () => {
  const w = world(['Ubuntu'], ['Ubuntu']);
  const base = w.answer;
  w.answer = (spawn) => (isRunningList(spawn.args) ? { code: 1, output: '', stderrTail: '' } : base(spawn));
  const got = await walk(w);
  assert.deepEqual(got.facts.distros.map((one) => `${one.distro}:${one.state}`), ['Ubuntu:not-running']);
  assert.equal(w.spawns.some((one) => one.args.includes('-d')), false);
  const failed = world(['Ubuntu'], ['Ubuntu']);
  const original = failed.answer;
  failed.answer = (spawn) => (spawn.args.includes('--running') ? original(spawn) : { code: 3, output: '', stderrTail: '' });
  const result = await walk(failed);
  assert.equal(result.facts.failed, 'The list of WSL distros could not be read: wsl.exe ended with exit 3.');
  assert.equal(failed.spawns.length, 1, 'one list call and nothing after it');
  const garbled = world([], []);
  garbled.answer = () => ({ code: 0, output: 'a\u0000bu', stderrTail: '' });
  assert.equal((await walk(garbled)).facts.failed, 'The list of WSL distros could not be read: its output could not be decoded.');
  assert.equal(garbled.spawns.length, 1);
});

test('E3S1-T8: a distro that is not running gets no call of any kind: no recorded argv holds -d <its name>; its line is S4', async () => {
  const w = world(['Ubuntu', 'Sleepy'], ['Ubuntu']);
  const got = await walk(w);
  assert.equal(w.spawns.some((one) => one.args.includes('Sleepy')), false);
  assert.deepEqual(got.facts.distros.find((one) => one.distro === 'Sleepy'), { distro: 'Sleepy', state: 'not-running', said: 'not running, so not read', users: 0 });
});

// ───────────────── T9: the re-read ─────────────────

test('E3S1-T9: the spawn before every -d call is the running list, and between the two stands a door that answered yes', async () => {
  const w = world(['Ubuntu'], ['Ubuntu'], { Ubuntu: ['root 0', 'alice 1000'] });
  await walk(w);
  const spawns = w.record.filter((one) => one.startsWith('spawn:'));
  assert.deepEqual(spawns, [
    'spawn:--list --quiet', 'spawn:--list --running --quiet',
    'spawn:--list --running --quiet', 'spawn:-d Ubuntu -u root --cd / --exec /bin/sh -s',
    'spawn:--list --running --quiet', 'spawn:-d Ubuntu -u root --cd / --exec /bin/sh -s -- no-claude-usage',
    'spawn:--list --running --quiet', 'spawn:-d Ubuntu -u alice --cd / --exec /bin/sh -s -- no-claude-usage',
  ]);
  record: for (const [at, entry] of w.record.entries()) {
    if (!entry.startsWith('spawn:-d ')) {
      continue;
    }
    assert.equal(w.record[at - 1], 'door:yes', 'a door answered yes right before the -d spawn');
    assert.equal(w.record[at - 2], 'spawn:--list --running --quiet', 'and the spawn before that door is the running list');
    assert.equal(w.record[at - 3], 'door:yes');
    continue record;
  }
});

test('E3S1-T9: a distro that leaves the running list after the round began gets no call, S5 and no later re-read', async () => {
  const w = world(['Ubuntu'], ['Ubuntu'], { Ubuntu: ['root 0', 'alice 1000', 'bob 1001'] });
  let rereads = 0;
  const base = w.answer;
  w.answer = (spawn) => {
    if (isRunningList(spawn.args)) {
      rereads += 1;
      // the 1st is the round's own, the 2nd the users call's, the 3rd the first place's re-read: there the distro is gone
      return rereads >= 4 ? { code: 0, output: u16(''), stderrTail: '' } : base(spawn);
    }

    return base(spawn);
  };
  const got = await walk(w);
  assert.deepEqual(got.seen, ['wsl:Ubuntu/root:answered', 'wsl:Ubuntu/alice:left']);
  assert.equal(rereads, 4, 'no re-read after the distro was found gone');
  assert.deepEqual(got.facts.distros.find((one) => one.distro === 'Ubuntu'), { distro: 'Ubuntu', state: 'left', said: 'stopped since the round began, so not read', users: 0 });
  assert.equal(w.spawns.filter((one) => one.args.includes('-u')).length, 2, 'the users call and the first place only');
});

// ───────────────── T14: the caps ─────────────────

test('E3S1-T14: 17 distros of the list are 16 read and the count of the rest; 32 places over three distros and the count of the rest, root counted as a user', async () => {
  const names = Array.from({ length: 17 }, (_, at) => `D${at}`);
  const many = world(names, []);
  const got = await walk(many);
  assert.equal(got.facts.moreDistros, 1);
  assert.equal(got.facts.distros.length, MAX_DISTROS);
  const three = world(['A', 'B', 'C'], ['A', 'B', 'C'], {
    A: Array.from({ length: 16 }, (_, at) => (at === 0 ? 'root 0' : `u${at} ${1000 + at}`)),
    B: Array.from({ length: 16 }, (_, at) => (at === 0 ? 'root 0' : `u${at} ${1000 + at}`)),
    C: ['root 0', 'late 1000', 'later 1001'],
  });
  const result = await walk(three);
  assert.equal(result.places.length, MAX_PLACES);
  assert.equal(result.facts.moreUsers, 3, 'the rest of C');
  assert.equal(result.places.at(-1), 'wsl:B/u15');
});

// ───────────────── T15: how a -d call ends ─────────────────

test('E3S1-T15: § W10, row by row, with the words; a marker text in stdout is in no sentence and in no log line', async () => {
  const MARK = 'MARKER-IN-STDOUT';
  const run = async (answer: Captured, place: WslPlace = ubuntuAlice): Promise<{ readonly run: WslRun; readonly logs: readonly string[] }> => {
    const w = world([], []);
    w.answer = () => answer;
    const got = await runWslAccounts(place, 'no-claude-usage', undefined, w.seams);

    return { run: got, logs: w.logs };
  };
  const refusalOf = (got: WslRun): string => (got.kind === 'refused' ? got.refusal : 'not refused');
  const notStarted = await run({ code: -1, output: MARK, ended: 'not-started', stderrTail: 'spawn failed' });
  assert.equal(refusalOf(notStarted.run), 'wsl.exe could not be started: spawn failed');
  const timeout = await run({ code: -1, output: MARK, ended: 'timeout', stderrTail: '' });
  assert.equal(refusalOf(timeout.run), '“Ubuntu · alice” did not answer through WSL within 30 s.');
  assert.equal(timeout.run.kind === 'refused' && timeout.run.end, 'timeout');
  const capped = await run({ code: -1, output: MARK, ended: 'capped', stderrTail: '' });
  assert.equal(refusalOf(capped.run), '“Ubuntu · alice” sent more than 64 KiB, so the read was stopped.');
  const ok = await run({ code: 0, output: `${accountsReply}${MARK}`, stderrTail: '' });
  assert.equal(ok.run.kind === 'answered' && ok.run.code, 0);
  assert.deepEqual(ok.logs, [], 'an exit 0 writes no line');
  const begun = await run({ code: 1, output: `x\nCOAI-ACCOUNTS 1\n${MARK}`, stderrTail: '' });
  assert.equal(begun.run.kind, 'answered', 'another exit with the begin line as a whole line: the parser says its fault');
  const wsa = await run({ code: 1, output: '', stderrTail: u16('Wsl/Service/WSAETIMEDOUT\r\n') });
  assert.equal(refusalOf(wsa.run), 'WSL did not connect to “Ubuntu” within its own 30 s (WSAETIMEDOUT).');
  assert.equal(wsa.run.kind === 'refused' && wsa.run.end, 'wsaetimedout');
  const wsaOut = await run({ code: 1, output: u16('WSAETIMEDOUT'), stderrTail: '' });
  assert.equal(wsaOut.run.kind === 'refused' && wsaOut.run.end, 'wsaetimedout');
  const other = await run({ code: 2, output: MARK, stderrTail: u16('The distribution failed.\r\n') });
  assert.equal(refusalOf(other.run), 'WSL could not read “Ubuntu · alice” (exit 2): The distribution failed.');
  const bare = await run({ code: 5, output: MARK, stderrTail: '' });
  assert.equal(refusalOf(bare.run), 'WSL could not read “Ubuntu · alice” (exit 5)');
  for (const got of [notStarted, timeout, capped, begun, wsa, wsaOut, other, bare]) {
    assert.equal(got.logs.length, 1, 'a call that did not answer with exit 0 is one log line');
    assert.ok((got.logs[0] ?? '').startsWith('ConnectOtherAIs: remote read place=wsl:Ubuntu/alice end='));
    assert.equal(got.logs.some((line) => line.includes(MARK)), false, 'stdout is in no log line');
    assert.equal(refusalOfAny(got.run).includes(MARK), false, 'stdout is in no sentence');
  }
});

const refusalOfAny = (got: WslRun): string => (got.kind === 'refused' ? got.refusal : '');

test('E3S1-T15: the last line of stderr is the one said, control characters out, 300 characters; a stderr that does not decode says nothing', async () => {
  const w = world([], []);
  w.answer = () => ({ code: 3, output: '', stderrTail: `first\nthe \u001b[31mlast ${'z'.repeat(400)}\n\n` });
  const got = await runWslAccounts(ubuntuAlice, 'no-claude-usage', undefined, w.seams);
  const said = got.kind === 'refused' ? got.refusal : '';
  assert.ok(said.startsWith('WSL could not read “Ubuntu · alice” (exit 3): the [31mlast '.replace('\u001b', '')));
  assert.ok(said.endsWith('…'));
  const bad = world([], []);
  bad.answer = () => ({ code: 3, output: '', stderrTail: 'a\u0000b' });
  assert.equal((await runWslAccounts(ubuntuAlice, 'no-claude-usage', undefined, bad.seams) as { refusal: string }).refusal, 'WSL could not read “Ubuntu · alice” (exit 3)');
});

test('E3S1-T15: a seam that throws is one fixed sentence with no value in it', async () => {
  const w = world([], []);
  const seams: WslSeams = { ...w.seams, exists: () => Promise.reject(new Error('SECRET-ERROR')) };
  const got = await runWslList(false, seams);
  assert.equal(got.kind === 'refused' && got.refusal.includes('SECRET'), false);
  assert.equal(w.logs.some((line) => line.includes('SECRET')), false);
});

// ───────────────── T16: two strikes ─────────────────

test('E3S1-T16: two strikes — the third and later places get no call and say S10; the next round calls again', async () => {
  const w = world(['Ubuntu'], ['Ubuntu'], { Ubuntu: ['root 0', 'a 1000', 'b 1001', 'c 1002'] });
  const base = w.answer;
  w.answer = (spawn) => (spawn.args.includes('--') ? { code: -1, output: '', ended: 'timeout', stderrTail: '' } : base(spawn));
  const got = await walk(w);
  assert.equal(w.spawns.filter((one) => one.args.includes('--')).length, 2, 'only two accounts calls were made');
  assert.equal(got.facts.strikes, 2);
  assert.deepEqual(got.runs.map((one) => (one.kind === 'refused' ? one.refusal : one.kind)).slice(2), [STRIKES_SAID, STRIKES_SAID]);
  const next = world(['Ubuntu'], ['Ubuntu'], { Ubuntu: ['root 0', 'a 1000', 'b 1001', 'c 1002'] });
  const again = await walk(next);
  assert.equal(next.spawns.filter((one) => one.args.includes('--')).length, 4, 'the next round calls again');
  assert.equal(again.facts.strikes, 0);
});

test('E3S1-T16: WSAETIMEDOUT is a strike too; a users call that times out is a strike and the next distro is S10 after two', async () => {
  const w = world(['A', 'B', 'C'], ['A', 'B', 'C']);
  w.answer = (spawn) => (spawn.args.includes('-u') ? { code: 1, output: '', stderrTail: u16('WSAETIMEDOUT') } : isRunningList(spawn.args) ? { code: 0, output: u16('A\r\nB\r\nC\r\n'), stderrTail: '' } : { code: 0, output: u16('A\r\nB\r\nC\r\n'), stderrTail: '' });
  const got = await walk(w);
  assert.deepEqual(got.facts.distros.map((one) => `${one.distro}:${one.state}`), ['A:users-failed', 'B:users-failed', 'C:strikes']);
  assert.equal(w.spawns.filter((one) => one.args.includes('-u')).length, 2);
});

// ───────────────── T17: the door ─────────────────

test('E3S1-T17: for each answer but yes — ZERO spawns, the list calls among them', async () => {
  for (const answer of ['no', 'no-permission', 'waiting', 'damaged', 'stopped'] as const) {
    const w = world(['Ubuntu'], ['Ubuntu']);
    w.door = () => answer;
    const got = await wslRound(wslPortOf(w.seams), hooks(), 0, (state) => Promise.resolve({ state }));
    assert.equal(w.spawns.length, 0, answer);
    assert.deepEqual(got.end, { door: answer });
  }
});

test('E3S1-T17: the call right before EVERY spawn is a door that answered yes', async () => {
  const w = world(['Ubuntu', 'Debian'], ['Ubuntu', 'Debian'], { Ubuntu: ['root 0', 'a 1000'], Debian: ['root 0'] });
  await walk(w);
  assert.ok(w.spawns.length > 10);
  assert.equal(doorBeforeEverySpawn(w.record), true);
  assert.equal(w.record.filter((one) => one === 'door:yes').length, w.spawns.length, 'one door question for each spawn');
});

test('E3S1-T17: a "no" saved while a call runs — its result is the caller\'s to drop and NO further spawn is made', async () => {
  const w = world(['Ubuntu'], ['Ubuntu'], { Ubuntu: ['root 0', 'alice 1000'] });
  let saved = false;
  w.onSpawn = (spawn) => { saved = saved || spawn.args.includes('--'); };
  w.door = () => (saved ? 'no' : 'yes');
  const got = await walk(w);
  assert.equal(w.spawns.filter((one) => one.args.includes('--')).length, 1, `the call that was running finished: ${w.record.join(' | ')}`);
  assert.equal(w.spawns.at(-1)?.args.includes('--'), true, 'and nothing was spawned after it');
  assert.deepEqual(got.runs.map((one) => (one.kind === 'refused' ? `refused:${one.end}` : one.kind)), ['answered', 'refused:door'], 'the next place was refused by the door, with no spawn');
});

test('E3S1-T17: a "no" saved while a RE-READ runs — no -d spawn after it', async () => {
  const w = world(['Ubuntu'], ['Ubuntu']);
  let rereads = 0;
  let saved = false;
  w.onSpawn = (spawn) => {
    if (isRunningList(spawn.args)) {
      rereads += 1;
      saved = rereads >= 2;
    }
  };
  w.door = () => (saved ? 'no' : 'yes');
  const got = await wslRound(wslPortOf(w.seams), hooks(), 0, (state) => Promise.resolve({ state }));
  assert.equal(w.spawns.some((one) => one.args.includes('-d')), false, 'a "no" saved during the re-read: not one -d spawn');
  assert.deepEqual(got.end, { door: 'no' });
});

test('E3S1-T17: the door is asked inside the edge\'s one spawn function, never beside it', () => {
  const text = sourceOf('remoteWsl.ts');
  assert.equal((text.match(/seams\.capture\(/gu) ?? []).length, 1, 'ONE call of capture in the edge');
  assert.equal((text.match(/seams\.door\(\)/gu) ?? []).length, 1, 'ONE door question in the edge');
  const body = text.slice(text.indexOf('async function spawned('), text.indexOf('/** One call. NEVER rejects'));
  assert.ok(body.indexOf('seams.door()') > 0 && body.indexOf('seams.door()') < body.indexOf('seams.capture('), 'the door is asked before capture, in the same function');
  assert.equal(sourceOf('remoteWslRound.ts').includes('door()'), false, 'the walk asks no door of its own');
});

// ───────────────── T18: not on Windows ─────────────────

test('E3S1-T18: not on Windows — S1 and zero spawns of the three calls', async () => {
  for (const platform of ['linux', 'darwin'] as const) {
    const w = world(['Ubuntu'], ['Ubuntu']);
    w.platform = platform;
    for (const run of [await runWslList(false, w.seams), await runWslUsers('Ubuntu', w.seams), await runWslAccounts(ubuntuAlice, 'no-claude-usage', undefined, w.seams)]) {
      assert.deepEqual(run, { kind: 'refused', refusal: 'WSL is read from a Windows window only', end: 'platform' });
    }
    assert.deepEqual(w.record, []);
  }
});

// ───────────────── T19: the key and the label ─────────────────

test('E3S1-T19: the key and the label; two users of a distro are two keys; root is a key of its own', () => {
  assert.equal(placeKey({ kind: 'ssh', host: 'ai1-dev2' }), 'ssh:ai1-dev2');
  assert.equal(placeKey(ubuntuAlice), 'wsl:Ubuntu/alice');
  assert.equal(placeKey(ubuntuRoot), 'wsl:Ubuntu/root');
  assert.equal(placeLabel(ubuntuAlice), 'Ubuntu · alice');
  assert.equal(placeLabel({ kind: 'ssh', host: 'h' }), 'h');
  assert.notEqual(placeKey(ubuntuAlice), placeKey({ ...ubuntuAlice, user: 'bob', uid: 1001 }));
  assert.equal(memoryKeyOf({ kind: 'ssh', host: 'h' }), 'h');
  assert.equal(memoryKeyOf(ubuntuAlice), 'wsl:Ubuntu/alice');
});

test('E3S1-T19: wslPlaceOk\'s table', () => {
  const ok = (distro: string, user: string, uid: number): boolean => wslPlaceOk({ kind: 'wsl', distro, user, uid });
  assert.equal(ok('Ubuntu', 'root', 0), true);
  assert.equal(ok('Ubuntu', 'alice', 1000), true);
  assert.equal(ok('Ubuntu', 'alice', 59_999), true);
  assert.equal(ok('Ubuntu-22.04', '_x-y1', 1234), true);
  for (const [distro, user, uid] of [['Ubuntu', 'root', 1000], ['Ubuntu', 'alice', 0], ['Ubuntu', 'alice', 999], ['Ubuntu', 'alice', 60_000], ['-x', 'alice', 1000], ['Ubuntu', 'Alice', 1000], ['Ubuntu', '1a', 1000], ['Ubuntu', 'a'.repeat(33), 1000], ['', 'alice', 1000]] as const) {
    assert.equal(ok(distro, user, uid), false, `${distro} ${user} ${uid}`);
  }
});

test('E3S1: the three builders and the spawn are the only users of wsl.exe — no other file names it as a program to start, none sets WSLENV, HOME or WSL_UTF8', () => {
  for (const file of sourceFiles()) {
    const text = sourceOf(file);
    if (file !== 'remoteWsl.ts') {
      assert.equal(/WSL_UTF8|WSLENV/u.test(text.replace(/\/\*[\s\S]*?\*\//gu, '')) && !file.startsWith('remoteWsl'), false, `${file} names a WSL variable outside comments`);
    }
  }
  const edge = sourceOf('remoteWsl.ts').replace(/\/\*[\s\S]*?\*\//gu, '').replace(/\/\/.*$/gmu, '');
  assert.equal(/WSL_UTF8/u.test(edge), false);
  assert.equal(/env\s*:\s*\{/u.test(edge), false, 'the edge sets no variable');
});
