// lib/native-run.mjs — the Windows half of the safe remote run (DN11 of todo/PLAN_kimi_and_vibe_on_native_linux.md, E1.S1): the lead
// renders ONE run script per phase from scripts/native/run-template.sh and builds every ssh argument array. Pure — nothing here
// starts a process or touches a file but the template read — so it is tested everywhere (native-run.test.mjs).
//
// Why every value is checked HERE: the rendered script is run by root on the host, and an `ssh` argument list is re-joined by ssh
// into one string the remote shell parses again. So a series, a phase, a probe name, a probe argument and a host all pass a pattern
// first, and a value that fails it never reaches the text or the array — it is an exception, not an escape.
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const TEMPLATE = readFileSync(join(dirname(fileURLToPath(import.meta.url)), '..', 'native', 'run-template.sh'), 'utf8');

/**
 * The shape of a series folder's name (below its root) — ONE literal, also carried by make-series.sh, abort.sh and
 * remove-series.sh (native.test.mjs fails unless all three hold exactly this text).
 *
 * THE SERIES IS THE ONE BOUNDED EXCEPTION of the test-leftovers family (E5 S4; the rollout's epoch 5, item 4): the name carries no test-leftovers prefix,
 * no owner segment and no claim, because the series is a root-run, multi-phase folder that outlives every script that writes it. The LEAD makes it
 * (`mkdirArgv`), `make-series.sh` fills it, `remove-series.sh` removes it; nothing else collects it. `ls -d <root>/native-*` finds a forgotten one.
 */
export const SERIES_SHAPE = 'native-(vibe|kimi)-[0-9]{8}-[0-9a-f]{6}';
const PHASE = /^[a-z0-9-]{1,32}$/u;
const PROBE = /^[A-Za-z0-9][A-Za-z0-9_-]*\.mjs$/u;
/** A probe argument: the lead's literals, but written into a root-run script, so checked like data. */
const ARG = /^[A-Za-z0-9_.,=/:@-]+$/u;
const HOST = /^[A-Za-z0-9][A-Za-z0-9._-]*$/u;
const ROOT = /^\/(?:[A-Za-z0-9_-][A-Za-z0-9_.-]*(?:\/[A-Za-z0-9_-][A-Za-z0-9_.-]*)*)?$/u;

const refuse = (what, value) => {
  throw new Error(`refused: ${what} ${JSON.stringify(String(value))} does not match its pattern — it would be written into a script root runs`);
};
const checked = (pattern, what, value) => (typeof value === 'string' && pattern.test(value) ? value : refuse(what, value));

/** The series folder: `<root>/native-(vibe|kimi)-<yyyymmdd>-<6 hex>`; the root is `/tmp` unless a test names its own. */
function seriesOf(series, root = '/tmp') {
  checked(ROOT, 'the series root', root);
  const prefix = root === '/' ? '/' : `${root}/`;
  const name = typeof series === 'string' && series.startsWith(prefix) ? series.slice(prefix.length) : '';
  return new RegExp(`^${SERIES_SHAPE}$`, 'u').test(name) ? series : refuse('the series', series);
}

/** Names that would land a phase on a file or folder the series itself owns: run/abort.sh and run/remove-series.sh (root runs those later), the five subfolders. */
const RESERVED_PHASES = new Set(['abort', 'remove-series', 'checkout', 'tmp', 'run', 'logs', 'bin']);
const phaseOf = (phase) => (RESERVED_PHASES.has(phase) ? refuse('a phase naming a file or folder the series owns', phase) : checked(PHASE, 'the phase', phase));
const hostOf = (options) => checked(HOST, 'the host', options?.host);
const quoted = (word) => `'${word}'`;

/**
 * The probe command line of a run script: `'scripts/<probe>' '<arg>' …`, every word single-quoted. A word passing its pattern holds
 * no quote, so the quotes cannot be closed from inside.
 */
function probeCommand(probe, args) {
  checked(PROBE, 'the probe', probe);
  if (!Array.isArray(args)) refuse('the probe arguments', args);
  return [quoted(`scripts/${probe}`), ...args.map((arg) => quoted(unreserved(checked(ARG, 'a probe argument', arg))))].join(' ');
}

/**
 * The flags the series itself owns or the host forbids: the run script appends its own `--scratch "$SERIES/$PHASE"` (a caller's would move the
 * probe out of the series folder), and the `--allow-…` flags switch on arms the owner permits on the Windows machine only (J5), or let a probe read the sign-in
 * (`--allow-copied-credentials`) or run on the real home (`--allow-real-home`) — a series on a native host never does either.
 */
const RESERVED_FLAGS = ['--scratch', '--allow-agents-canary', '--allow-live-survivor', '--allow-copied-credentials', '--allow-real-home'];
const isReserved = (arg) => RESERVED_FLAGS.some((flag) => arg === flag || arg.startsWith(`${flag}=`));
const unreserved = (arg) => (isReserved(arg) ? refuse('a probe argument naming a flag the series or the host owns', arg) : arg);

/** The run script of one phase: the template with the series, the phase and the probe command filled in (replacers are functions, so no `$` in a value is ever special). */
export function renderRunScript({ series, phase, probe, args = [], root = '/tmp' }) {
  const marks = { '@SERIES@': seriesOf(series, root), '@PHASE@': phaseOf(phase), '@PROBE_COMMAND@': probeCommand(probe, args) };
  return Object.entries(marks).reduce((text, [mark, value]) => text.replaceAll(mark, () => value), TEMPLATE);
}

const remote = (options, program, ...rest) => ['ssh', hostOf(options), '--', program, ...rest];

/** Starts a phase: nohup + setsid + bash, each by absolute path, the script by its path. */
function launcherArgv(series, phase, options) {
  const dir = seriesOf(series, options?.root);
  return remote(options, '/usr/bin/nohup', '/usr/bin/setsid', '/bin/bash', '--', `${dir}/run/${phaseOf(phase)}.sh`);
}

/** Aborts a phase: abort.sh, copied into run/, by path. */
function abortArgv(series, phase, options) {
  const dir = seriesOf(series, options?.root);
  return remote(options, '/bin/bash', '--', `${dir}/run/abort.sh`, phaseOf(phase));
}

/** Removes a series: remove-series.sh, copied into run/, given the folder it must also read back from SERIES_PATH. */
function removeArgv(series, options) {
  const dir = seriesOf(series, options?.root);
  return remote(options, '/bin/bash', '--', `${dir}/run/remove-series.sh`, dir);
}

/** The bare folder: no `-p`, so an existing one fails. */
export function mkdirArgv(series, options) {
  return remote(options, '/bin/mkdir', '-m', '700', '--', seriesOf(series, options?.root));
}

/** Runs make-series.sh from the folder it was copied into. */
function makeSeriesArgv(series, options) {
  return remote(options, '/bin/bash', '--', `${seriesOf(series, options?.root)}/make-series.sh`);
}

/** The poll: is the phase's `.done` there. */
export function doneArgv(series, phase, options) {
  const dir = seriesOf(series, options?.root);
  return remote(options, '/usr/bin/test', '-f', `${dir}/logs/${phaseOf(phase)}.done`);
}

// ------------------------------------------------------------------------------------------------------------------------------------------------
// The caller's `stat` check (E5 S4, the rollout's epoch 5 item 4, evidence 4): BEFORE the lead runs anything out of a series folder, it asks the host, over
// ssh, with `stat` (never a shell expansion of the folder's content), whether the folder is what it must be. The scripts in the folder run as root, and a
// folder planted in a shared /tmp under a valid name can carry its OWN run/remove-series.sh: a check inside that copy protects nothing by itself.
// ------------------------------------------------------------------------------------------------------------------------------------------------

/** The steps that run something out of a series, and the file each runs; `bare` and `bare-run` run nothing from it (they guard an scp INTO the folder). */
const SCRIPT_OF = { make: () => 'make-series.sh', remove: () => 'run/remove-series.sh', abort: () => 'run/abort.sh', phase: (phase) => `run/${phaseOf(phase)}.sh`, bare: () => null, 'bare-run': () => null };
const BUILDERS = {
  make: (series, _phase, options) => makeSeriesArgv(series, options),
  remove: (series, _phase, options) => removeArgv(series, options),
  abort: (series, phase, options) => abortArgv(series, phase, options),
  phase: (series, phase, options) => launcherArgv(series, phase, options),
};

/**
 * Every path whose facts the check reads, in order: the root, the series, run/ (not for `make` and `bare`, which have none yet) and the script (none for the bare kinds).
 * `bare` is the check before an `scp` INTO the series folder (the root and the series only), `bare-run` the one before an `scp` into `run/`: scp must never follow a planted link.
 */
function pathsOf(kind, series, phase, options) {
  const dir = seriesOf(series, options?.root);
  const script = SCRIPT_OF[kind]?.(phase);
  if (script === undefined) refuse('the kind of step', kind);
  const root = options?.root ?? '/tmp';
  return { root, dir, run: kind === 'make' || kind === 'bare' ? undefined : `${dir}/run`, script: script === null ? undefined : `${dir}/${script}` };
}

/** `%n` name, `%F` kind WITHOUT following links, `%u` owner uid, `%d` device, `%a` octal mode. ONE single-quoted word: ssh joins the words with spaces and the REMOTE SHELL parses them again. */
const STAT_FORMAT = '%n|%F|%u|%d|%a';

/**
 * The ssh argument array that reads the facts: `/usr/bin/env LC_ALL=C /usr/bin/stat -c '<format>' -- <paths>` — the locale is fixed for this one command because `%F` is
 * TRANSLATED under a forwarded locale, and the format is quoted for the remote shell (an unquoted `|` there is a pipe: exit 127 and no facts).
 */
export function statArgv(kind, series, phase, options) {
  const { root, dir, run, script } = pathsOf(kind, series, phase, options);
  return remote(options, '/usr/bin/env', 'LC_ALL=C', '/usr/bin/stat', '-c', quoted(STAT_FORMAT), '--', root, dir, ...(run === undefined ? [] : [run]), ...(script === undefined ? [] : [script]));
}

const factsOf = (text) => new Map(String(text).split('\n').filter((line) => line.includes('|')).map((line) => {
  const [name, kind, uid, device, mode] = line.split('|');
  return [name, { kind, uid: Number(uid), device, mode: Number.parseInt(mode, 8) }];
}));
const modeText = (mode) => mode.toString(8);

/**
 * The refusal line for the facts `stat` printed, or `''` when the series is what it must be. `ownerUid` is the uid the series, its root and its files must belong
 * to (0: root on the host; the integration test names its own). The series folder and `run/` must be plain directories (a link is refused), that uid, mode 700; the
 * script must be a regular file of that uid and neither group- nor world-writable; the root must be a directory of that uid and, when group- OR world-writable, sticky (a group member could otherwise rename the checked folder).
 * A series that is not there at all is its own line ("gone") — the same one a phase prints at the host.
 */
export function statRefusal(kind, series, phase, stdout, options) {
  const { root, dir, run, script } = pathsOf(kind, series, phase, options);
  const uid = options?.ownerUid ?? 0;
  const facts = factsOf(stdout);
  if (facts.size === 0) return `refused: the host answered no facts for ${dir} — nothing was run`;
  if (!facts.has(dir)) return `the series ${dir} is gone; start a new one with make-series.sh`;
  const bad = (what, fact) => `refused: ${what} — nothing was run`;
  const folder = (path, label) => {
    const f = facts.get(path);
    if (f === undefined) return bad(`${label} ${path} is missing`);
    if (f.kind !== 'directory') return bad(`${label} ${path} is a ${f.kind}, not a plain directory`);
    if (f.uid !== uid) return bad(`${label} ${path} is owned by uid ${f.uid}, not ${uid}`);
    if (f.mode !== 0o700) return bad(`${label} ${path} is mode ${modeText(f.mode)}, not 700`);
    return '';
  };
  const rootFacts = facts.get(root);
  if (rootFacts === undefined) return bad(`the series root ${root} cannot be read`);
  if (rootFacts.kind !== 'directory') return bad(`the series root ${root} is a ${rootFacts.kind}, not a plain directory`);
  if (rootFacts.uid !== uid) return bad(`the series root ${root} is owned by uid ${rootFacts.uid}, not ${uid}`);
  if ((rootFacts.mode & 0o022) !== 0 && (rootFacts.mode & 0o1000) === 0) return bad(`the series root ${root} is group- or world-writable (mode ${modeText(rootFacts.mode)}) without the sticky bit`);
  const wrong = folder(dir, 'the series') || (run === undefined ? '' : folder(run, 'its run folder'));
  if (wrong !== '') return wrong;
  // A FUSE mount with allow_other can fake owner, mode and kind of everything below it; it cannot share the root's device.
  const sameDevice = (path, label) => (facts.get(path).device === rootFacts.device ? '' : bad(`${label} ${path} is on another device (${facts.get(path).device}) than the series root (${rootFacts.device})`));
  const elsewhere = sameDevice(dir, 'the series') || (run === undefined ? '' : sameDevice(run, 'its run folder'));
  if (elsewhere !== '') return elsewhere;
  if (script === undefined) return '';
  const file = facts.get(script);
  if (file === undefined) return bad(`the script ${script} is missing`);
  if (file.kind !== 'regular file' && file.kind !== 'regular empty file') return bad(`the script ${script} is a ${file.kind}, not a regular file`);
  if (file.uid !== uid) return bad(`the script ${script} is owned by uid ${file.uid}, not ${uid}`);
  if ((file.mode & 0o022) !== 0) return bad(`the script ${script} is mode ${modeText(file.mode)}: group- or world-writable`);
  return sameDevice(script, 'the script');
}

/**
 * Runs one step out of a series ONLY after its folder passed the `stat` check — and this is the ONLY road: the raw argument builders of the steps that run out of a series
 * are not exported. `exec(argv)` runs an argument array (the lead's ssh, or a test's local stand-in that parses the words with a shell as sshd does) and answers
 * `{ status, stdout }`; `kind` is `make`, `remove`, `abort` or `phase` (the builder is ours), or `bare` / `bare-run` (an scp INTO the folder: `makeArgv` is the caller's).
 * Answers `{ status: 78 | 66, refused }` and runs NOTHING from the folder when the check fails (66: the series is gone); else `exec`'s own answer for the step. Never throws for a refusal.
 */
export async function runChecked(kind, series, phase, options, exec, makeArgv = BUILDERS[kind]) {
  if (typeof makeArgv !== 'function') refuse('a step with no argument builder', kind);
  const probe = await exec(statArgv(kind, series, phase, options));
  const refused = statRefusal(kind, series, phase, probe?.stdout ?? '', options);
  if (refused !== '') return { status: refused.startsWith('the series ') ? 66 : 78, refused };
  return exec(makeArgv(series, phase, options));
}
