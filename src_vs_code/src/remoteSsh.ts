import { access } from 'node:fs/promises';
import { win32 } from 'node:path';
import { credsSession } from './credsSession';
import { Platform, hostPlatform } from './hostSide';
import { system32 } from './processLauncher';
import { callOf, shQuoted } from './remoteScript';
import { Captured, capture } from './versionProbe';

/**
 * The ssh edge (E2.S2, contract C3): the argv, the one executable, and the call that never rejects.
 *
 * <p>Node only, no `vscode`. The script goes on stdin and nothing else does; the operands are single-quoted for the remote
 * login shell and checked twice (here, at the one road in, and by the door that built them). Every sentence about an ssh
 * option in this file says what we PASS, not what it protects: the effect on the configuration was seen (S2-T21), the effect
 * against a real server was not (M-O1…M-O4 of the spec).</p>
 *
 * <p>The ssh child goes through `capture`, which goes through `launch` — the one spawn site — with `shell` false and the tree
 * kill asked for. This module starts no process of its own.</p>
 */

/**
 * The parent's options, in the parent's order, with change 2.1 last. A change to this list is a PLAN change, never an edit.
 *
 * <p>`-T` and `RequestTTY=no` say the same thing; the parent's list is the contract, so both stay. `StrictHostKeyChecking=yes`
 * (change 2.1) stands before the person's configuration, so an `accept-new` or `no` there cannot make this call accept a host
 * key. Connection sharing (`ControlPath`) is NOT pinned: it stays the person's own (the spec's residual 9).</p>
 */
export const SSH_OPTIONS: readonly string[] = [
  '-T',
  '-o', 'BatchMode=yes', '-o', 'ConnectTimeout=5', '-o', 'RequestTTY=no', '-o', 'RemoteCommand=none',
  '-o', 'ClearAllForwardings=yes', '-o', 'ForwardAgent=no', '-o', 'ForwardX11=no', '-o', 'LogLevel=ERROR',
  '-o', 'StrictHostKeyChecking=yes',
];

const REFUSED_WORD = 'The remote read was asked in a form its host or operands do not allow.';

/**
 * The words of the ssh call. `--` stands right before the host, so the host is never read as an option and every word after it
 * is the remote command (`-s` is then not ssh's own `-s`). No `-n`: the remote `sh -s` would read an empty stdin.
 *
 * <p>THROWS, with a fixed text that holds no value, when the host fails C1's host class or the operands fail their grammar:
 * this is the second check of the same rules, at the one road in.</p>
 */
export function sshArgv(host: string, operands: readonly string[]): readonly string[] {
  if (credsSession(`Creds: ${host} / s`).kind !== 'session' || callOf(operands) === undefined) {
    throw new Error(REFUSED_WORD);
  }

  return [...SSH_OPTIONS, '--', host, 'sh', '-s', '--', ...operands.map(shQuoted)];
}

/**
 * ONE absolute path for each platform, never the `PATH`, and the child's working folder: the folder of that executable.
 *
 * <p>With `shell: false` the spawn layer looks for a bare name in the current folder first — the extension host's, which can be
 * the opened workspace — and then on the `PATH`. Only an administrator can write to the system folder, so a bare `ssh` that
 * `ssh` itself starts (a `ProxyJump`, a `ProxyCommand`) is found there and nowhere else.</p>
 */
export function sshExecutable(platform: Platform = hostPlatform(), env: NodeJS.ProcessEnv = process.env): { readonly path: string; readonly cwd: string } {
  if (platform === 'win32') {
    const cwd = win32.join(system32(env), 'OpenSSH');

    return { path: win32.join(cwd, 'ssh.exe'), cwd };
  }

  return { path: '/usr/bin/ssh', cwd: '/usr/bin' };
}

/** The time and the stdout a call may take. A byte count, not a character count: the stream is decoded text. */
export interface RemoteLimits {
  readonly ms: number;
  readonly maxStdoutBytes: number;
}

export const REMOTE_LIMITS: RemoteLimits = { ms: 15_000, maxStdoutBytes: 8 * 1024 * 1024 };

/** What a call came to: the VM answered (any exit code but 255 and a failed start), or the person is told why not. */
export type RemoteRun =
  | { readonly kind: 'answered'; readonly code: number; readonly stdout: string }
  | { readonly kind: 'refused'; readonly refusal: string };

/** What a test may replace. S3 does not pass it. */
export interface RemoteSeams {
  readonly capture: typeof capture;
  readonly exists: (path: string) => Promise<boolean>;
  /** The default is the ONE write to the process's error output in the three remote modules. */
  readonly log: (line: string) => void;
  readonly platform: Platform;
  readonly env: NodeJS.ProcessEnv;
}

function defaultSeams(): RemoteSeams {
  return {
    capture,
    exists: (path) => access(path).then(() => true, () => false),
    log: (line) => console.error(line),
    platform: hostPlatform(),
    env: process.env,
  };
}

const BAD_OPERAND_REFUSAL = 'This terminal’s name cannot be given to ssh safely, so nothing was read.';
const BAD_OPERAND_LOG = 'ConnectOtherAIs: remote read refused before ssh: a host, a session or an operand failed its rule';
const ERROR_LINE_MAX = 300;
const LOG_TAIL_MAX = 2000;

const refused = (refusal: string): RemoteRun => ({ kind: 'refused', refusal });

/**
 * One call. NEVER rejects and never throws: every failure is a `refused` with a sentence that holds no stdout, no operand
 * and no caught error. The log line, once, for a call that did not answer with code 0, is written here and nowhere else.
 */
export async function runRemote(
  host: string,
  session: string,
  operands: readonly string[],
  script: string,
  limits: RemoteLimits = REMOTE_LIMITS,
  seams: RemoteSeams = defaultSeams(),
): Promise<RemoteRun> {
  try {
    return await ranRemote(host, session, operands, script, limits, seams);
  } catch {
    // A seam that rejected or threw. Nothing of it reaches the person or the log: only that ssh could not be run.
    logRead(seams, host, session, 'not-started', '');

    return refused(`ssh could not be run, so the Claude session on “${host}” cannot be read.`);
  }
}

async function ranRemote(host: string, session: string, operands: readonly string[], script: string, limits: RemoteLimits, seams: RemoteSeams): Promise<RemoteRun> {
  const argv = argvOf(host, session, operands);
  if (argv === undefined) {
    logged(seams, BAD_OPERAND_LOG);

    return refused(BAD_OPERAND_REFUSAL);
  }
  const exe = sshExecutable(seams.platform, seams.env);
  if (!(await seams.exists(exe.path))) {
    logRead(seams, host, session, 'no-ssh', '');

    return refused(missingSentence(host, exe.path, seams.platform));
  }
  const options = { cwd: exe.cwd, maxStdoutBytes: limits.maxStdoutBytes, killTree: true };
  const got = await seams.capture(exe.path, argv, false, limits.ms, undefined, undefined, script, options);

  return outcomeOf(host, session, limits, got, seams);
}

/** The argv, or nothing when the session is not the first operand or a rule refuses. */
function argvOf(host: string, session: string, operands: readonly string[]): readonly string[] | undefined {
  try {
    return operands[0] === session ? sshArgv(host, operands) : undefined;
  } catch {
    return undefined;
  }
}

function missingSentence(host: string, path: string, platform: Platform): string {
  const base = `ssh was not found at “${path}”, so the Claude session on “${host}” cannot be read.`;

  return platform === 'win32' ? `${base} Install the Windows optional feature “OpenSSH Client”.` : base;
}

/** The refusal sentence for each way a capture ends without an exit code. */
const END_SENTENCES: ReadonlyMap<string, (host: string, limits: RemoteLimits, said: string) => string> = new Map([
  ['timeout', (host: string, limits: RemoteLimits) => `“${host}” did not answer over ssh within ${limits.ms / 1000} s.`],
  ['capped', (host: string, limits: RemoteLimits) => `“${host}” sent more than ${limits.maxStdoutBytes / 1_048_576} MiB, so the read was stopped.`],
  ['not-started', (_host: string, _limits: RemoteLimits, said: string) => `ssh could not be started: ${said}`],
]);

const tailOf = (got: Captured): string => got.stderrTail ?? '';

/** ssh's own exit 255: it could not connect, authenticate or verify the host — never an answer. */
const sshSentence = (host: string, said: string): string =>
  (said.length === 0 ? `ssh could not read from “${host}” (exit 255).` : `ssh could not read from “${host}”: ${said}`);

/** The sentence when the call did not answer, or nothing when it did. */
function refusalOf(host: string, limits: RemoteLimits, got: Captured): string | undefined {
  const said = lastLine(tailOf(got));
  const sentence = got.ended === undefined ? undefined : END_SENTENCES.get(got.ended);
  if (sentence !== undefined) {
    return sentence(host, limits, said);
  }

  return got.code === 255 ? sshSentence(host, said) : undefined;
}

/** How the call ended, for the log: the end of a capture with no exit code, else the exit code. */
const endOf = (got: Captured): string => (got.ended !== undefined && got.code === -1 ? got.ended : `exit ${got.code}`);

function outcomeOf(host: string, session: string, limits: RemoteLimits, got: Captured, seams: RemoteSeams): RemoteRun {
  if (got.code !== 0) {
    logRead(seams, host, session, endOf(got), tailOf(got));
  }
  const refusal = refusalOf(host, limits, got);

  return refusal === undefined ? { kind: 'answered', code: got.code, stdout: got.output } : refused(refusal);
}

/** The ONE call of `logLine`: a read that did not answer with code 0, written through the log seam. */
function logRead(seams: RemoteSeams, host: string, session: string, end: string, stderrTail: string): void {
  logged(seams, logLine(host, session, end, stderrTail));
}

/** A failing logger must not turn a read into a rejection. */
function logged(seams: RemoteSeams, line: string): void {
  try {
    seams.log(line);
  } catch {
    // The line is lost; the read's answer is not.
  }
}

/** A line of ssh's stderr is text from the VM: its control characters (an escape sequence, a bell) never reach the person or the log. */
const withoutControls = (line: string): string => line.replace(/\p{Cc}/gu, '');

/**
 * ssh's last line: the last one that is not empty, control characters taken out, at most 300 characters with `…` after a cut.
 * It is ssh's own sentence (`Permission denied (publickey).`): the remote script's stderr goes to `/dev/null`, so no transcript
 * text can be in it.
 */
function lastLine(tail: string): string {
  const lines = tail.split('\n').map(withoutControls);
  const last = [...lines].reverse().find((line) => line.trim().length > 0) ?? '';

  return last.length > ERROR_LINE_MAX ? `${last.slice(0, ERROR_LINE_MAX)}…` : last;
}

/**
 * The ONE log line of a call: host, session label, how it ended and the last 2000 characters of ssh's stderr, each line
 * break as ` | `. Never stdout, never an operand, never a caught error.
 */
function logLine(host: string, session: string, end: string, stderrTail: string): string {
  const said = stderrTail.slice(-LOG_TAIL_MAX).trimEnd().split(/\r?\n/u).map(withoutControls).join(' | ');

  return `ConnectOtherAIs: remote read host=${host} session=${session} end=${end} stderr=${said}`;
}
