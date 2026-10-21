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
 */
export const SERIES_SHAPE = 'testsToDelete-native-(vibe|kimi)-[0-9]{8}-[0-9a-f]{6}';
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

/** The series folder: `<root>/testsToDelete-native-(vibe|kimi)-<yyyymmdd>-<6 hex>`; the root is `/tmp` unless a test names its own. */
function seriesOf(series, root = '/tmp') {
  checked(ROOT, 'the series root', root);
  const prefix = root === '/' ? '/' : `${root}/`;
  const name = typeof series === 'string' && series.startsWith(prefix) ? series.slice(prefix.length) : '';
  return new RegExp(`^${SERIES_SHAPE}$`, 'u').test(name) ? series : refuse('the series', series);
}

const phaseOf = (phase) => checked(PHASE, 'the phase', phase);
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
export function launcherArgv(series, phase, options) {
  const dir = seriesOf(series, options?.root);
  return remote(options, '/usr/bin/nohup', '/usr/bin/setsid', '/bin/bash', '--', `${dir}/run/${phaseOf(phase)}.sh`);
}

/** Aborts a phase: abort.sh, copied into run/, by path. */
export function abortArgv(series, phase, options) {
  const dir = seriesOf(series, options?.root);
  return remote(options, '/bin/bash', '--', `${dir}/run/abort.sh`, phaseOf(phase));
}

/** Removes a series: remove-series.sh, copied into run/, given the folder it must also read back from SERIES_PATH. */
export function removeArgv(series, options) {
  const dir = seriesOf(series, options?.root);
  return remote(options, '/bin/bash', '--', `${dir}/run/remove-series.sh`, dir);
}

/** The bare folder: no `-p`, so an existing one fails. */
export function mkdirArgv(series, options) {
  return remote(options, '/bin/mkdir', '-m', '700', '--', seriesOf(series, options?.root));
}

/** Runs make-series.sh from the folder it was copied into. */
export function makeSeriesArgv(series, options) {
  return remote(options, '/bin/bash', '--', `${seriesOf(series, options?.root)}/make-series.sh`);
}

/** The poll: is the phase's `.done` there. */
export function doneArgv(series, phase, options) {
  const dir = seriesOf(series, options?.root);
  return remote(options, '/usr/bin/test', '-f', `${dir}/logs/${phaseOf(phase)}.done`);
}
