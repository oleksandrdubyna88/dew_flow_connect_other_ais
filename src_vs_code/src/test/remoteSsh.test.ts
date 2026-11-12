import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { existsSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import test from 'node:test';
import { Platform } from '../hostSide';
import { REMOTE_LIMITS, RemoteLimits, RemoteRun, RemoteSeams, SSH_OPTIONS, runRemote, sshArgv, sshExecutable } from '../remoteSsh';
import { REMOTE_SCRIPT, RemoteCall, operandsOf, shQuoted } from '../remoteScript';
import { readRemote } from '../remoteFrame';
import { Captured, CaptureOptions } from '../versionProbe';

/**
 * The ssh edge (E2.S2, C3), with a fake `capture` that records what it was asked and answers what the test says — and, for
 * the one test that needs it (S2-T21), the REAL `ssh -G` against a hostile configuration.
 *
 * <p>Every sentence about an ssh option in these tests says what is PASSED: the presence of an option on the command line is
 * S2-T2, its effect on the configuration ssh would use is S2-T21, and its refusal against a real server is not observed (M-O3).</p>
 */

const H = 'ai1-dev2';
const S = 'small-tasks';
const ID = '0b2e6a52-1f6c-4d0e-8a43-1c9d1d5e7f10';

/** The parent's options, typed out ONCE here, in the parent's order, change 2.1 last. */
const PARENT_OPTIONS: readonly string[] = [
  '-T',
  '-o', 'BatchMode=yes',
  '-o', 'ConnectTimeout=5',
  '-o', 'RequestTTY=no',
  '-o', 'RemoteCommand=none',
  '-o', 'ClearAllForwardings=yes',
  '-o', 'ForwardAgent=no',
  '-o', 'ForwardX11=no',
  '-o', 'LogLevel=ERROR',
  '-o', 'StrictHostKeyChecking=yes',
];

interface CaptureCall {
  readonly target: string;
  readonly args: readonly string[];
  readonly shell: boolean;
  readonly capMs: number;
  readonly input: string;
  readonly options: CaptureOptions | undefined;
}

interface Fake {
  readonly seams: RemoteSeams;
  readonly calls: CaptureCall[];
  readonly logs: string[];
}

/** A `RemoteSeams` whose `capture` records its arguments and answers `answer`; `exists` answers `exists`; `log` collects lines. */
function fake(answer: Captured | (() => Promise<Captured>), over: { readonly exists?: boolean | (() => Promise<boolean>); readonly platform?: Platform; readonly env?: NodeJS.ProcessEnv } = {}): Fake {
  const calls: CaptureCall[] = [];
  const logs: string[] = [];
  const exists = over.exists ?? true;
  const seams: RemoteSeams = {
    capture: (target, args, shell, capMs, _stop, _env, input = '', options) => {
      calls.push({ target, args: [...args], shell, capMs, input, options });

      return typeof answer === 'function' ? answer() : Promise.resolve(answer);
    },
    exists: typeof exists === 'function' ? exists : () => Promise.resolve(exists),
    log: (line) => {
      logs.push(line);
    },
    platform: over.platform ?? 'linux',
    env: over.env ?? {},
  };

  return { seams, calls, logs };
}

const OK: Captured = { code: 0, output: 'x', stderrTail: '' };
const refusalOf = (run: RemoteRun): string => (run.kind === 'refused' ? run.refusal : `not refused: ${JSON.stringify(run)}`);

test('S2-T1: the argv is the parent\'s, in its order (change 2.1 included)', () => {
  const argv = sshArgv(H, [S]);

  // The named faults first, so a red says WHAT was done; then the whole list.
  assert.ok(!argv.includes('-n'), 'the remote sh -s would read an empty stdin');
  assert.ok(!argv.some((word) => word.startsWith('Control')), 'a Control option was added: connection sharing is the person\'s (residual 9)');
  assert.equal(argv[argv.indexOf(H) - 1], '--', 'the host could be read as an option');
  assert.deepEqual([...SSH_OPTIONS], [...PARENT_OPTIONS], 'SSH_OPTIONS is not the parent\'s list: a change to it is a plan change');
  assert.deepEqual(SSH_OPTIONS.slice(-2), ['-o', 'StrictHostKeyChecking=yes'], 'change 2.1 is the last two words');
  assert.deepEqual([...argv], [...SSH_OPTIONS, '--', H, 'sh', '-s', '--', "'small-tasks'"]);
  assert.deepEqual(sshArgv(H, [S, 'id', ID]).slice(-3), [`'${S}'`, "'id'", `'${ID}'`], 'every word after sh -s -- goes through shQuoted, the literal words too');
});

test('S2-T2: every forwarding is off, no question is asked and no host key is accepted', () => {
  // This is PRESENCE on the command line. The effect on the configuration is S2-T21; the refusal against a real server is M-O3.
  const argv = sshArgv(H, [S]);
  const stop = argv.indexOf('--');
  const said: Readonly<Record<string, string>> = {
    'BatchMode=yes': 'a password or passphrase prompt could be asked of nobody',
    'RequestTTY=no': 'a terminal could be requested',
    'RemoteCommand=none': 'the configuration\'s RemoteCommand would run instead of the script',
    'ClearAllForwardings=yes': 'the owner\'s LocalForward would be opened a second time',
    'ForwardAgent=no': 'the agent would be forwarded',
    'ForwardX11=no': 'X11 would be forwarded',
    'StrictHostKeyChecking=yes': 'an unknown host key would be accepted by a call a terminal\'s name started',
  };
  for (const [option, because] of Object.entries(said)) {
    const at = argv.indexOf(option);
    assert.ok(at > 0 && argv[at - 1] === '-o' && at < stop, `${option} is not passed as -o before --: ${because}`);
  }
});

test('S2-T3: a bad host or operand throws before any spawn', async () => {
  for (const host of ['-oProxyCommand=x', 'h;id', 'h\n', 'h\0', 'a b', 'user@h', '']) {
    assert.throws(() => sshArgv(host, [S]), Error, `the host ${JSON.stringify(host)} was given to ssh`);
    const spy = fake(OK);
    assert.match(refusalOf(await runRemote(host, S, [S], REMOTE_SCRIPT, REMOTE_LIMITS, spy.seams)), /cannot be given to ssh safely/u);
    assert.equal(spy.calls.length, 0, `the host ${JSON.stringify(host)} reached a spawn`);
  }
  const operands: ReadonlyArray<readonly string[]> = [["a'b"], ['a b'], ['a\nb'], ['a\0b'], [S, 'id', 'x'], [S, 'titles', '..']];
  for (const bad of operands) {
    assert.throws(() => sshArgv(H, bad), Error, `the operands ${JSON.stringify(bad)} were given to ssh`);
    const spy = fake(OK);
    const run = await runRemote(H, S, bad, REMOTE_SCRIPT, REMOTE_LIMITS, spy.seams);
    assert.equal(refusalOf(run), 'This terminal’s name cannot be given to ssh safely, so nothing was read.');
    assert.equal(spy.calls.length, 0, `the fake capture WAS called for ${JSON.stringify(bad)}`);
  }
  const other = fake(OK);
  const mismatch = await runRemote(H, S, ['other-session'], REMOTE_SCRIPT, REMOTE_LIMITS, other.seams);
  assert.match(refusalOf(mismatch), /cannot be given to ssh safely/u, 'the operands name another session than the log label, and the call was not refused');
  assert.equal(other.calls.length, 0, 'the operands name another session than the log label, and capture was called');

  const good = fake(OK);
  assert.deepEqual(await runRemote(H, S, [S], REMOTE_SCRIPT, REMOTE_LIMITS, good.seams), { kind: 'answered', code: 0, stdout: 'x' });
  assert.equal(good.calls.length, 1, 'the companion: a good call DOES reach capture');
});

test('S2-T5: on win32 the argv crosses unchanged (G0)', async () => {
  const first = [...'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-'];
  const calls: RemoteCall[] = [{ kind: 'session', session: S }, { kind: 'id', session: S, id: ID }];
  for (const letter of first) {
    calls.push({ kind: 'titles', session: S, folders: [`${letter}ome-x`, `${letter}.b_c`] });
  }
  calls.push({ kind: 'titles', session: S, folders: ['a'.repeat(255)] });

  for (const call of calls) {
    const operands = operandsOf(call);
    const spy = fake(OK, { platform: 'win32', env: { SystemRoot: 'D:\\W' } });
    await runRemote(H, S, operands, REMOTE_SCRIPT, REMOTE_LIMITS, spy.seams);
    const words = spy.calls[0]?.args ?? [];

    assert.ok(words.length > 0, `${call.kind}: capture was not called`);
    for (const word of words) {
      assert.ok(!/[ \t"]/u.test(word), `this operand would be re-quoted on its way to ssh.exe: ${JSON.stringify(word)}`);
    }
    assert.deepEqual(words.slice(words.indexOf('sh') + 3), operands.map(shQuoted), 'the words after sh -s -- are shQuoted of the operands');
    assert.equal(spy.calls[0]?.shell, false, 'ssh was started through a shell');
  }
  for (const bad of ['a b', 'a\tb', 'a"b']) {
    const spy = fake(OK, { platform: 'win32', env: { SystemRoot: 'D:\\W' } });
    await runRemote(H, S, [S, 'titles', bad], REMOTE_SCRIPT, REMOTE_LIMITS, spy.seams);
    for (const word of spy.calls[0]?.args ?? []) {
      assert.ok(!/[ \t"]/u.test(word), `this operand would be re-quoted on its way to ssh.exe: ${JSON.stringify(word)}`);
    }
    assert.equal(spy.calls.length, 0, `a folder with a space, a tab or a quote reached ssh: ${JSON.stringify(bad)}`);
    assert.throws(() => sshArgv(H, [S, 'titles', bad]), Error, `a folder with a space, a tab or a quote was accepted: ${JSON.stringify(bad)}`);
  }
});

test('S2-T6: the script travels on stdin and nothing else does', async () => {
  const spy = fake(OK);

  await runRemote(H, S, [S], REMOTE_SCRIPT, REMOTE_LIMITS, spy.seams);

  const call = spy.calls[0];
  assert.equal(call?.input, REMOTE_SCRIPT, 'the script is not what was written to stdin');
  for (const word of call?.args ?? []) {
    assert.ok(!/[\r\n]/u.test(word) && word.length <= 300, `the script (or a part of it) is in the argv: ${word.slice(0, 40)}`);
  }
});

test('S2-T7: a slow host is refused at the limit', async () => {
  const spy = fake({ code: -1, output: '', ended: 'timeout', stderrTail: '' });

  const run = await runRemote(H, S, [S], REMOTE_SCRIPT, { ms: 15_000, maxStdoutBytes: 8 * 1024 * 1024 }, spy.seams);

  assert.equal(refusalOf(run), '“ai1-dev2” did not answer over ssh within 15 s.');
  assert.equal(spy.calls[0]?.capMs, 15_000);
  assert.equal(spy.calls[0]?.options?.killTree, true, 'a timed-out ssh would be killed without its tree');
  assert.equal(spy.calls[0]?.options?.maxStdoutBytes, 8 * 1024 * 1024);
  // The KILL itself is S2-T22 (captureWidened.test.ts).
});

test('S2-T7 companions: a capped, an unstarted and a refused-by-ssh call each say their own sentence', async () => {
  const sentence = async (answer: Captured): Promise<string> => refusalOf(await runRemote(H, S, [S], REMOTE_SCRIPT, REMOTE_LIMITS, fake(answer).seams));
  assert.equal(await sentence({ code: -1, output: '', ended: 'capped', stderrTail: '' }), '“ai1-dev2” sent more than 8 MiB, so the read was stopped.');
  assert.equal(await sentence({ code: -1, output: '', ended: 'not-started', stderrTail: '[coai] spawn ssh ENOENT' }), 'ssh could not be started: [coai] spawn ssh ENOENT');
  const limits: RemoteLimits = { ms: 1000, maxStdoutBytes: 1024 * 1024 };
  assert.equal(refusalOf(await runRemote(H, S, [S], REMOTE_SCRIPT, limits, fake({ code: -1, output: '', ended: 'capped', stderrTail: '' }).seams)), '“ai1-dev2” sent more than 1 MiB, so the read was stopped.', 'the limit in the sentence is the limit given');
});

test('S2-T9: ssh\'s own failure is not "no session"', async () => {
  const run255 = (stderrTail: string): Promise<RemoteRun> => runRemote(H, S, [S], REMOTE_SCRIPT, REMOTE_LIMITS, fake({ code: 255, output: '', stderrTail }).seams);

  const denied = refusalOf(await run255('debug\nPermission denied (publickey).\n'));
  assert.ok(denied.endsWith('Permission denied (publickey).'), `a refused key read as an empty session: ${denied}`);
  assert.equal(denied, 'ssh could not read from “ai1-dev2”: Permission denied (publickey).');
  assert.equal(refusalOf(await run255('')), 'ssh could not read from “ai1-dev2” (exit 255).');
  assert.equal(refusalOf(await run255('\n\n')), 'ssh could not read from “ai1-dev2” (exit 255).', 'an empty last line is no line');

  const long = refusalOf(await run255(`first\n${'x'.repeat(5000)}\n`));
  assert.equal(long, `ssh could not read from “ai1-dev2”: ${'x'.repeat(300)}…`);
  assert.equal(refusalOf(await run255('Perm\u0007ission\u001b[31m denied\n')), 'ssh could not read from “ai1-dev2”: Permission[31m denied');
});

test('S2-T9b: format controls of a VM\'s stderr — a bidi override, a zero-width space — reach neither the refusal nor the log line', async () => {
  // ESC is a control (Cc) and was already dropped; U+202E and U+200B are FORMAT characters (Cf) and still reorder or hide text.
  const spoof = 'Permission \u202Edenied\u200B (publickey).';
  const spy = fake({ code: 255, output: '', stderrTail: `first\n${spoof}\n` });
  const run = await runRemote(H, S, [S], REMOTE_SCRIPT, REMOTE_LIMITS, spy.seams);

  assert.equal(refusalOf(run), 'ssh could not read from “ai1-dev2”: Permission denied (publickey).', 'a format control reached the refusal');
  assert.equal(spy.logs.length, 1);
  assert.equal(spy.logs[0], 'ConnectOtherAIs: remote read host=ai1-dev2 session=small-tasks end=exit 255 stderr=first | Permission denied (publickey).', 'a format control reached the log line');
  assert.ok(!/[\u202E\u200B]/u.test(spy.logs[0] ?? ''), 'a bidi override or a zero-width space is in the log line');
});

test('S2-T18: the ssh executable is one absolute path, and its folder is the child\'s', async () => {
  assert.deepEqual(sshExecutable('win32', { SystemRoot: 'D:\\W' }), { path: 'D:\\W\\System32\\OpenSSH\\ssh.exe', cwd: 'D:\\W\\System32\\OpenSSH' });
  assert.equal(sshExecutable('win32', { WINDIR: 'E:\\Win' }).path, 'E:\\Win\\System32\\OpenSSH\\ssh.exe');
  assert.deepEqual(sshExecutable('win32', {}), { path: 'C:\\Windows\\System32\\OpenSSH\\ssh.exe', cwd: 'C:\\Windows\\System32\\OpenSSH' });
  for (const platform of ['linux', 'darwin'] as const) {
    assert.deepEqual(sshExecutable(platform, {}), { path: '/usr/bin/ssh', cwd: '/usr/bin' }, `${platform}: the ssh is not the one absolute path`);
  }

  const spy = fake(OK, { platform: 'win32', env: { SystemRoot: 'D:\\W' } });
  await runRemote(H, S, [S], REMOTE_SCRIPT, REMOTE_LIMITS, spy.seams);
  assert.equal(spy.calls[0]?.target, 'D:\\W\\System32\\OpenSSH\\ssh.exe', 'ssh is not the absolute path (a bare ssh would be looked up in the working folder)');
  assert.equal(spy.calls[0]?.options?.cwd, 'D:\\W\\System32\\OpenSSH', 'the working folder of the ssh child is not the folder of the executable');

  const linux = fake(OK);
  await runRemote(H, S, [S], REMOTE_SCRIPT, REMOTE_LIMITS, linux.seams);
  assert.equal(linux.calls[0]?.target, '/usr/bin/ssh');
  assert.equal(linux.calls[0]?.options?.cwd, '/usr/bin', 'the working folder of the ssh child is not the folder of the executable');

  const missing = fake(OK, { exists: false, platform: 'win32', env: { SystemRoot: 'D:\\W' } });
  const refusal = refusalOf(await runRemote(H, S, [S], REMOTE_SCRIPT, REMOTE_LIMITS, missing.seams));
  assert.equal(refusal, 'ssh was not found at “D:\\W\\System32\\OpenSSH\\ssh.exe”, so the Claude session on “ai1-dev2” cannot be read. Install the Windows optional feature “OpenSSH Client”.');
  assert.equal(missing.calls.length, 0, 'ssh was started although it is not there');
  const missingLinux = fake(OK, { exists: false });
  assert.equal(refusalOf(await runRemote(H, S, [S], REMOTE_SCRIPT, REMOTE_LIMITS, missingLinux.seams)), 'ssh was not found at “/usr/bin/ssh”, so the Claude session on “ai1-dev2” cannot be read.');
});

test('S2-T19: one log line for a call that did not answer 0, and nothing of the conversation in it', async () => {
  const lines = async (answer: Captured, over: Parameters<typeof fake>[1] = {}): Promise<string[]> => {
    const spy = fake(answer, over);
    await runRemote(H, S, [S], REMOTE_SCRIPT, REMOTE_LIMITS, spy.seams);

    return spy.logs;
  };

  const denied = await lines({ code: 255, output: '', stderrTail: 'debug\nPermission denied (publickey).\n' });
  assert.equal(denied.length, 1);
  assert.equal(denied[0], 'ConnectOtherAIs: remote read host=ai1-dev2 session=small-tasks end=exit 255 stderr=debug | Permission denied (publickey).', 'the log line holds more or less than the host, the session, the end and the stderr tail (an operand or the stdout is in it)');

  const ends: ReadonlyArray<readonly [Captured, string, Parameters<typeof fake>[1]]> = [
    [{ code: -1, output: '', ended: 'timeout', stderrTail: '' }, 'end=timeout', {}],
    [{ code: -1, output: '', ended: 'capped', stderrTail: '' }, 'end=capped', {}],
    [{ code: -1, output: '', ended: 'not-started', stderrTail: 'spawn ssh ENOENT' }, 'end=not-started', {}],
    [OK, 'end=no-ssh', { exists: false }],
    [{ code: 3, output: 'x', stderrTail: '' }, 'end=exit 3', {}],
  ];
  for (const [answer, end, over] of ends) {
    const got = await lines(answer, over);
    assert.equal(got.length, 1, `${end}: not exactly one log line`);
    assert.ok(got[0]?.includes(end) && got[0].includes('host=ai1-dev2') && got[0].includes('session=small-tasks'), `${end}: ${got[0]}`);
  }
  assert.deepEqual(await lines(OK), [], 'a call that answered 0 wrote a log line');

  // Nothing of the conversation: not an operand, not stdout.
  const spy = fake({ code: 3, output: 'TRANSCRIPT-MARK', stderrTail: 'ssh said this' });
  await runRemote(H, S, [S, 'titles', '-home-git-secret'], REMOTE_SCRIPT, REMOTE_LIMITS, spy.seams);
  assert.equal(spy.logs.length, 1);
  assert.ok(!spy.logs[0]?.includes('-home-git-secret'), 'the folder name is in the log line');
  assert.ok(!spy.logs[0]?.includes('TRANSCRIPT-MARK'), 'the stdout is in the log line');

  // A bad operand: the fixed line, with no value in it.
  const bad = fake(OK);
  await runRemote(H, S, ["a'b-SECRET"], REMOTE_SCRIPT, REMOTE_LIMITS, bad.seams);
  assert.deepEqual(bad.logs, ['ConnectOtherAIs: remote read refused before ssh: a host, a session or an operand failed its rule']);
  assert.ok(!bad.logs.join('').includes('SECRET'));
});

test('S2-T19: the tail in the log is the last 2000 characters, line breaks as " | "', async () => {
  const spy = fake({ code: 255, output: '', stderrTail: `${'a'.repeat(3000)}\nb\nc\n` });

  await runRemote(H, S, [S], REMOTE_SCRIPT, REMOTE_LIMITS, spy.seams);

  const said = spy.logs[0]?.split(' stderr=')[1] ?? '';
  assert.ok(said.startsWith('a') && said.endsWith(' | b | c'), 'the line breaks of the tail are not written as " | "');
  assert.ok(said.split(' | ').join('\n').length <= 2000, `the tail is not cut at its last 2000 characters: ${said.length}`);
});

test('S2-T19: control characters of the stderr tail never reach the log line', async () => {
  const spy = fake({ code: 255, output: '', stderrTail: 'Perm\u001b[31mission\u0007 denied\nsecond\u0000 line\n' });

  await runRemote(H, S, [S], REMOTE_SCRIPT, REMOTE_LIMITS, spy.seams);

  assert.equal(spy.logs.length, 1);
  assert.ok(!/\p{Cc}/u.test(spy.logs[0] ?? ''), `a control character of the VM's text is in the log line: ${JSON.stringify(spy.logs[0])}`);
  assert.ok(spy.logs[0]?.endsWith('stderr=Perm[31mission denied | second line'), spy.logs[0]);
});

test('S2-T20: runRemote never rejects, and a code that is not 0 or 255 is an answer', async () => {
  const rejecting = fake(() => Promise.reject(new Error('SECRET-FROM-CAPTURE')));
  const run = await runRemote(H, S, [S], REMOTE_SCRIPT, REMOTE_LIMITS, rejecting.seams);
  assert.equal(refusalOf(run), 'ssh could not be run, so the Claude session on “ai1-dev2” cannot be read.', 'the sentence of a seam that failed');
  assert.ok(!refusalOf(run).includes('SECRET'), 'a caught error reached the person');
  assert.equal(rejecting.logs.length, 1, 'one log line for a seam that failed');
  assert.ok(rejecting.logs[0]?.includes('end=not-started'));
  assert.ok(!rejecting.logs.join('').includes('SECRET'), 'a caught error reached the log');

  const noFile = fake(OK, { exists: () => Promise.reject(new Error('boom')) });
  assert.equal((await runRemote(H, S, [S], REMOTE_SCRIPT, REMOTE_LIMITS, noFile.seams)).kind, 'refused');

  const throwingLog = fake({ code: 255, output: '', stderrTail: 'x' });
  const loud: RemoteSeams = { ...throwingLog.seams, log: () => { throw new Error('the log is broken'); } };
  assert.equal((await runRemote(H, S, [S], REMOTE_SCRIPT, REMOTE_LIMITS, loud)).kind, 'refused', 'a broken logger turned a refusal into a rejection');

  const three = await runRemote(H, S, [S], REMOTE_SCRIPT, REMOTE_LIMITS, fake({ code: 3, output: 'x', stderrTail: '' }).seams);
  assert.deepEqual(three, { kind: 'answered', code: 3, stdout: 'x' });
  if (three.kind === 'answered') {
    assert.deepEqual(readRemote(three, { kind: 'session', session: S }), { kind: 'unreadable', why: 'exit' });
  }
});

/** The configuration of M-G1: every setting the options of this call must win over. */
const HOSTILE_CONFIG = `Host vmx
  RemoteCommand /bin/false
  RequestTTY force
  LocalForward 127.0.0.1:18080 127.0.0.1:80
  RemoteForward 127.0.0.1:18081 127.0.0.1:80
  DynamicForward 127.0.0.1:18082
  ForwardAgent yes
  ForwardX11 yes
  ControlMaster auto
  ControlPath /tmp/coai-test-%C
  ControlPersist 10m
  StrictHostKeyChecking accept-new
  BatchMode no
  LogLevel DEBUG
`;

test('S2-T21: the options win over a person\'s config (the REAL ssh -G)', (context) => {
  const ssh = sshExecutable().path;
  if (!existsSync(ssh)) {
    if (process.env['CI'] === 'true' && process.platform === 'linux') {
      assert.fail(`no ssh at ${ssh}: on the Linux runner of CI the ssh half of S2-T44 must run`);
    }
    context.skip(`no ssh at ${ssh}`);

    return;
  }
  const folder = process.env['COAI_TEST_RUN_FOLDER'];
  assert.ok(folder !== undefined && folder.length > 0, 'the run folder is not set');
  const config = join(folder, `ssh-config-${randomUUID()}`);
  writeFileSync(config, HOSTILE_CONFIG, { mode: 0o600 });
  try {
    const printed = (args: readonly string[]): string => {
      const run = spawnSync(ssh, ['-F', config, '-G', ...args], { encoding: 'utf8' });
      assert.equal(run.status, 0, `ssh -G failed: ${run.stderr}`);

      return run.stdout;
    };

    // The fixture IS hostile: without our options the person's settings are what ssh would use.
    const bare = printed(['vmx']);
    for (const line of ['localforward', 'remoteforward', 'dynamicforward', 'forwardagent yes', 'forwardx11 yes', 'batchmode no', 'remotecommand /bin/false', 'stricthostkeychecking accept-new']) {
      assert.ok(bare.includes(line), `the fixture is not hostile: ${line} is not in ssh -G of the bare configuration`);
    }

    const ours = printed(sshArgv('vmx', [S]));
    const has = (line: string): boolean => ours.split('\n').includes(line);
    const starts = (word: string): boolean => ours.split('\n').some((line) => line.startsWith(word));
    assert.ok(has('batchmode yes'), 'BatchMode=yes does not win over the configuration');
    assert.ok(has('clearallforwardings yes'));
    for (const word of ['localforward', 'remoteforward', 'dynamicforward']) {
      assert.ok(!starts(word), `a ${word} line is still there: ClearAllForwardings=yes did not win`);
    }
    assert.ok(has('forwardagent no') && has('forwardx11 no'));
    assert.ok(has('requesttty false') || has('requesttty no'));
    assert.ok(!starts('remotecommand'), 'the configuration\'s RemoteCommand is still there');
    assert.ok(has('loglevel ERROR') && has('connecttimeout 5'));
    assert.ok(has('stricthostkeychecking true'), `stricthostkeychecking accept-new would accept a host key — ours says: ${ours.split('\n').filter((line) => line.startsWith('stricthostkeychecking')).join()}`);
    // The `control…` lines are NOT asserted: connection sharing stays the person's own (residual 9 of the spec).
  } finally {
    rmSync(config, { force: true });
  }
});
