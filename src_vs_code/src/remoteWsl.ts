import { access } from 'node:fs/promises';
import { win32 } from 'node:path';
import { Platform, hostPlatform } from './hostSide';
import { system32 } from './processLauncher';
import { withoutControls } from './remoteAccountsReply';
import { ACCOUNTS_PROTOCOL, ACCOUNTS_SCRIPT, AccountsMode, isAccountsMode } from './remoteAccountsScript';
import { WslPlace, distroOk, placeKey, placeLabel, wslDecoded, wslPlaceOk } from './remotePlaces';
import type { DoorAnswer } from './remotePermission';
import { RemoteLimits, lastLine, sizeWords } from './remoteSsh';
import { USERS_SCRIPT } from './remoteUsersScript';
import type { WslEnd, WslRun } from './remoteWslRun';
import type { WslPort } from './remoteWslRound';
import { Captured, capture } from './versionProbe';

/**
 * The WSL edge (E3.S1, contract K2): the executable, the three argv builders and the three calls that never reject. Node only, no `vscode`, beside
 * `remoteSsh.ts` and built the same way: one absolute path, an argv, `shell` off, the script on stdin, 30 s, 64 KiB, the tree killed.
 *
 * <p>The three builders — the list, the users, the accounts — are the ONLY makers of a `wsl.exe` argv, and this is the only source file that holds
 * the word that makes `wsl.exe` start a program (`remoteWsl.test.ts` scans for it). No name that reached this file by a terminal or a reply is
 * an argv word before its class has passed: a distro's, a user's and a mode's. No variable is set; `WSLENV` and `HOME` are taken out.</p>
 *
 * <p><b>The door is asked inside the one spawn function</b> (`spawned`), right before `capture`: every call of this file goes through it, so no
 * `wsl.exe` starts without a `yes` asked for THAT spawn, and nothing but the door's own answer is awaited between the door and the spawn. A
 * running list read again before a `-d` call, and that `-d` call, each ask it (§ W5).</p>
 */

/** WSL's ceilings: 30 s and 64 KiB, the accounts call's own (a test holds the two equal). */
export const WSL_LIMITS: RemoteLimits = { ms: 30_000, maxStdoutBytes: 64 * 1024 };

/** Names taken out of the environment of every `wsl.exe` child: `WSLENV` would send variables into a distro; `HOME` is the distro's, never ours (§ W3). */
export const WSL_WITHOUT_ENV: readonly string[] = ['WSLENV', 'HOME'];

/** The words of the two list calls, nothing on stdin. */
export const LIST_ARGV: readonly string[] = ['--list', '--quiet'];
export const RUNNING_ARGV: readonly string[] = ['--list', '--running', '--quiet'];

/** The digest rule of the accounts script's second operand: 64 characters of `0-9a-f` (the S2 spec, § R2.2). */
const DIGEST = /^[0-9a-f]{64}$/u;

const REFUSED_WORD = 'The WSL read was asked in a form its distro, user or operands do not allow.';

/** The only literal user name of this module: the users call runs as root, by name. */
const ROOT = 'root';

/** The word that makes `wsl.exe` start a program with no shell, in the one place it is written. */
const EXEC = '--exec';

// ───────────────── the executable ─────────────────

/**
 * ONE absolute path, never the `PATH`, and the folder it stands in as the child's working folder: `System32` of the `SystemRoot` (or `WINDIR`, or
 * `C:\Windows`) — the function `system32` that the ssh edge uses. Off Windows the answer is the same Windows shape and is never called.
 */
export function wslExecutable(_platform: Platform = hostPlatform(), env: NodeJS.ProcessEnv = process.env): { readonly path: string; readonly cwd: string } {
  const cwd = system32(env);

  return { path: win32.join(cwd, 'wsl.exe'), cwd };
}

// ───────────────── the three builders ─────────────────

/** The two list calls' words. */
export const wslListArgv = (running: boolean): readonly string[] => (running ? RUNNING_ARGV : LIST_ARGV);

/** The users call's words: root, by name, in the distro, in `/`. No operand, so no `--`. THROWS a fixed text when the distro fails its class. */
export function wslUsersArgv(distro: string): readonly string[] {
  if (!distroOk(distro)) {
    throw new Error(REFUSED_WORD);
  }

  return ['-d', distro, '-u', ROOT, '--cd', '/', EXEC, '/bin/sh', '-s'];
}

/**
 * The accounts call's words: `-d <distro> -u <user> --cd / --exec /bin/sh -s -- <mode>` and, for `claude-usage`, the digest. No word is quoted: `--exec`
 * starts `/bin/sh` with these words and no shell reads them. THROWS a fixed text that holds no value when the place fails `wslPlaceOk`, the mode is not
 * one of the two, or the digest rule fails.
 */
export function wslAccountsArgv(place: WslPlace, mode: AccountsMode, digest?: string): readonly string[] {
  if (!accountsAllowed(place, mode, digest)) {
    throw new Error(REFUSED_WORD);
  }

  return ['-d', place.distro, '-u', place.user, '--cd', '/', EXEC, '/bin/sh', '-s', '--', mode, ...(digest === undefined ? [] : [digest])];
}

const accountsAllowed = (place: WslPlace, mode: AccountsMode, digest: string | undefined): boolean =>
  wslPlaceOk(place) && isAccountsMode(mode) && digestFits(mode, digest);

const digestFits = (mode: AccountsMode, digest: string | undefined): boolean =>
  (mode === 'claude-usage' ? digest !== undefined && DIGEST.test(digest) : digest === undefined);

// ───────────────── the seams and the result ─────────────────

/** What a test may replace. S3 binds the real ones; `door` is `sessions.may('round', 'wsl')`. */
export interface WslSeams {
  readonly capture: typeof capture;
  readonly exists: (path: string) => Promise<boolean>;
  /** The ONE log seam, through which the one log line of a failed call is written. */
  readonly log: (line: string) => void;
  readonly platform: Platform;
  readonly env: NodeJS.ProcessEnv;
  /** Asked right before EVERY spawn: only `yes` spawns. */
  readonly door: () => Promise<DoorAnswer>;
}

/** The seams of a real run, but for the door: the caller binds it. */
export function defaultWslSeams(door: WslSeams['door'], log: WslSeams['log']): WslSeams {
  return {
    capture,
    exists: (path) => access(path).then(() => true, () => false),
    log,
    platform: hostPlatform(),
    env: process.env,
    door,
  };
}

export { isStrike } from './remoteWslRun';
export type { WslEnd, WslRun } from './remoteWslRun';

/** The edge's three calls, as the round takes them. */
export const wslPortOf = (seams: WslSeams): WslPort => ({
  list: (running) => runWslList(running, seams),
  users: (distro) => runWslUsers(distro, seams),
  accounts: (place, mode, digest) => runWslAccounts(place, mode, digest, seams),
});

// ───────────────── the words (§ W13) ─────────────────

export const WSL_ELSEWHERE_SAID = 'WSL is read from a Windows window only';
export const missingSaid = (path: string): string => `wsl.exe was not found at “${path}”, so WSL on this PC cannot be read.`;

const refused = (refusal: string, end: WslEnd, door?: DoorAnswer): WslRun => (door === undefined ? { kind: 'refused', refusal, end } : { kind: 'refused', refusal, end, door });

/** What a call is ABOUT: its argv (nothing when a rule refused it), the label of its sentences, the key of its log line and the begin line of its reply. */
interface Ask {
  readonly argv: readonly string[] | undefined;
  readonly label: string;
  readonly distro: string;
  readonly key: string;
  readonly begin: string | undefined;
  readonly script: string;
}

const text = (value: string): string => withoutControls(value);

function endSentence(ask: Ask, end: 'timeout' | 'capped' | 'not-started', said: string): string {
  const label = text(ask.label);
  if (end === 'timeout') {
    return `“${label}” did not answer through WSL within ${WSL_LIMITS.ms / 1000} s.`;
  }

  return end === 'capped' ? `“${label}” sent more than ${sizeWords(WSL_LIMITS.maxStdoutBytes)}, so the read was stopped.` : `wsl.exe could not be started: ${said}`;
}

/** `wsl.exe`'s last line: its stderr as text (UTF-16LE or UTF-8; one that does not decode says nothing), control characters out, 300 characters. */
function saidOf(got: Captured): string {
  const plain = wslDecoded(got.stderrTail ?? '');

  return plain === undefined ? '' : lastLine(plain);
}

const wsaOf = (got: Captured): boolean => [got.output, got.stderrTail ?? ''].some((one) => wslDecoded(one)?.includes('WSAETIMEDOUT') === true);

const hasBegin = (stdout: string, begin: string | undefined): boolean => begin !== undefined && stdout.split('\n').includes(begin);

const exitSentence = (ask: Ask, code: number, said: string): string =>
  `WSL could not read “${text(ask.label)}” (exit ${code})${said === '' ? '' : `: ${said}`}`;

const wsaSentence = (ask: Ask): string => `WSL did not connect to “${text(ask.distro)}” within its own 30 s (WSAETIMEDOUT).`;

/** The three ways a capture ends with no exit code. */
type Ending = 'not-started' | 'timeout' | 'capped';

const isEnding = (end: Captured['ended']): end is Ending => end === 'not-started' || end === 'timeout' || end === 'capped';

/** How a capture ends (the table of § W10). `list` calls answer with any exit code. */
function outcomeOf(ask: Ask, got: Captured, list: boolean, seams: WslSeams): WslRun {
  if (isEnding(got.ended)) {
    return endedRun(ask, got, got.ended, seams);
  }

  return got.code === 0 ? { kind: 'answered', code: 0, stdout: got.output } : nonZero(ask, got, list, seams);
}

function endedRun(ask: Ask, got: Captured, end: Ending, seams: WslSeams): WslRun {
  logRead(seams, ask.key, end, got);

  return refused(endSentence(ask, end, saidOf(got)), end);
}

function nonZero(ask: Ask, got: Captured, list: boolean, seams: WslSeams): WslRun {
  if (list || hasBegin(got.output, ask.begin)) {
    logRead(seams, ask.key, `exit ${got.code}`, got);

    return { kind: 'answered', code: got.code, stdout: got.output };
  }
  if (wsaOf(got)) {
    logRead(seams, ask.key, 'wsaetimedout', got);

    return refused(wsaSentence(ask), 'wsaetimedout');
  }
  logRead(seams, ask.key, `exit ${got.code}`, got);

  return refused(exitSentence(ask, got.code, saidOf(got)), 'exit');
}

// ───────────────── the one spawn ─────────────────

/** What stands before the door: a run that ends the call, or the file and the words. */
type Ready = { readonly run: WslRun } | { readonly exe: { readonly path: string; readonly cwd: string }; readonly argv: readonly string[] };

/** The platform, the words and the file — nothing that starts a process. */
async function preflight(ask: Ask, seams: WslSeams): Promise<Ready> {
  if (seams.platform !== 'win32') {
    return { run: refused(WSL_ELSEWHERE_SAID, 'platform') };
  }
  const exe = wslExecutable(seams.platform, seams.env);
  if (ask.argv === undefined) {
    return { run: refused(REFUSED_WORD, 'argv') };
  }

  return (await seams.exists(exe.path)) ? { exe, argv: ask.argv } : { run: refused(missingSaid(exe.path), 'missing') };
}

/**
 * THE one spawn: the platform, the file, then the door — the last thing awaited — then `capture`. A door's answer that is not `yes` is `refused` with
 * the answer and no spawn. The three runners have no other way to `capture`.
 */
async function spawned(ask: Ask, list: boolean, seams: WslSeams): Promise<WslRun> {
  const ready = await preflight(ask, seams);
  if ('run' in ready) {
    return ready.run;
  }
  const door = await seams.door();
  if (door !== 'yes') {
    return refused(`WSL is not read: the door answered ${door}.`, 'door', door);
  }
  const options = { cwd: ready.exe.cwd, maxStdoutBytes: WSL_LIMITS.maxStdoutBytes, killTree: true, withoutEnv: WSL_WITHOUT_ENV };

  return outcomeOf(ask, await seams.capture(ready.exe.path, ready.argv, false, WSL_LIMITS.ms, undefined, undefined, ask.script, options), list, seams);
}

/** One call. NEVER rejects: every failure is a `refused` with a sentence that holds no stdout, no operand and no caught error. */
async function ran(ask: Ask, list: boolean, seams: WslSeams): Promise<WslRun> {
  try {
    return await spawned(ask, list, seams);
  } catch {
    // A seam that rejected or threw. Nothing of it reaches the person or the log: only that wsl.exe could not be run.
    logged(seams, logLine(ask.key, 'not-started', ''));

    return refused('wsl.exe could not be run, so WSL on this PC cannot be read.', 'not-started');
  }
}

/** The argv of a builder, or nothing when a rule refused it. */
function argvOf(build: () => readonly string[]): readonly string[] | undefined {
  try {
    return build();
  } catch {
    return undefined;
  }
}

// ───────────────── the three calls ─────────────────

/** The list call (`running`: the running list). Nothing on stdin. Any exit code answers; the round reads what it means (§ W4). NEVER rejects. */
export function runWslList(running: boolean, seams: WslSeams): Promise<WslRun> {
  const label = running ? 'the running list' : 'the list';

  return ran({ argv: wslListArgv(running), label, distro: '', key: running ? 'wsl:running' : 'wsl:list', begin: undefined, script: '' }, true, seams);
}

/** The users call, as root, in one distro, with the users script on stdin. NEVER rejects; a distro outside its class is `refused` with no call. */
export function runWslUsers(distro: string, seams: WslSeams): Promise<WslRun> {
  return ran({ argv: argvOf(() => wslUsersArgv(distro)), label: `users of ${distro}`, distro, key: `wsl:${distro}/${ROOT}`, begin: 'COAI-USERS 1', script: USERS_SCRIPT }, false, seams);
}

/** The accounts call for one place: `ACCOUNTS_SCRIPT` ALWAYS on stdin, there is no script parameter. NEVER rejects; a bad place, mode or digest is `refused` with no call. */
export function runWslAccounts(place: WslPlace, mode: AccountsMode, digest: string | undefined, seams: WslSeams): Promise<WslRun> {
  const ask: Ask = {
    argv: argvOf(() => wslAccountsArgv(place, mode, digest)),
    label: placeLabel(place),
    distro: place.distro,
    key: placeKey(place),
    begin: `COAI-ACCOUNTS ${ACCOUNTS_PROTOCOL}`,
    script: ACCOUNTS_SCRIPT,
  };

  return ran(ask, false, seams);
}

// ───────────────── the log ─────────────────

const LOG_TAIL_MAX = 2000;

/** A failing logger must not turn a read into a rejection. */
function logged(seams: WslSeams, line: string): void {
  try {
    seams.log(line);
  } catch {
    // The line is lost; the read's answer is not.
  }
}

/** The ONE call of `logLine`: a call that did not answer with exit 0, written through the log seam. Never stdout, never an operand. */
function logRead(seams: WslSeams, key: string, end: string, got: Captured): void {
  logged(seams, logLine(key, end, wslDecoded(got.stderrTail ?? '') ?? ''));
}

/** The line: the place's key, how it ended and the last 2000 characters of `wsl.exe`'s stderr, each line break as ` | `. */
function logLine(key: string, end: string, stderr: string): string {
  const said = stderr.slice(-LOG_TAIL_MAX).trimEnd().split(/\r?\n/u).map(withoutControls).join(' | ');

  return `ConnectOtherAIs: remote read place=${key} end=${end} stderr=${said}`;
}
