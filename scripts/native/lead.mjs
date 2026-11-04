#!/usr/bin/env node
// lead.mjs <host> <step> … — the lead's ONLY entry to a measurement series on the native Linux host (E5 S4; todo/PLAN_kimi_and_vibe_on_native_linux.md §6, entry 1.1;
// the rollout's epoch 5, item 4). The series is the one bounded exception of the test-leftovers family: `/tmp/native-(vibe|kimi)-<yyyymmdd>-<6 hex>`, no prefix, no
// owner, no claim; made by `mkdir` here, filled by make-series.sh, removed by remove-series.sh, collected by nothing.
//
//   node scripts/native/lead.mjs <host> name <vibe|kimi>                      prints a fresh series path (today's UTC date, six random hex)
//   node scripts/native/lead.mjs <host> mkdir <series>                        ssh /bin/mkdir -m 700 (no -p: an existing folder FAILS, and this step stops with its status)
//   node scripts/native/lead.mjs <host> copy <series> make                    scp make-series.sh into the series
//   node scripts/native/lead.mjs <host> make <series>                         runs make-series.sh out of the series
//   node scripts/native/lead.mjs <host> copy <series> tools                   scp abort.sh and remove-series.sh into <series>/run/
//   node scripts/native/lead.mjs <host> copy <series> phase <phase> <probe> [probe arguments…]   renders run/<phase>.sh and scps it into <series>/run/
//   node scripts/native/lead.mjs <host> copy <series> checkout <local-dir>    scp -r -p of every entry of <local-dir> into <series>/checkout/ (the archive the probes import)
//   node scripts/native/lead.mjs <host> phase <series> <phase>                starts the phase (nohup + setsid + bash, each by absolute path)
//   node scripts/native/lead.mjs <host> done <series> <phase>                 is logs/<phase>.done there? exit 0 yes, 1 not yet (/usr/bin/test -f; runs nothing from the folder)
//   node scripts/native/lead.mjs <host> read <series> <phase>                 prints logs/<phase>.exit and logs/<phase>.log (/bin/cat --, after a check of the root and the series)
//   node scripts/native/lead.mjs <host> abort <series> <phase>                runs run/abort.sh
//   node scripts/native/lead.mjs <host> remove <series>                       runs run/remove-series.sh (the only remover)
//
// WHY THIS IS THE ONLY ENTRY. The host runs what is in a series folder AS ROOT, and a folder planted in a shared /tmp under a valid name can carry its OWN scripts. So every
// step that runs something out of the folder goes through `runChecked` of lib/native-run.mjs: BEFORE it, a `stat` over ssh (LC_ALL=C, the format quoted for the remote shell)
// reads the root, the series, run/ and the script — plain directories, no links, the expected uid, mode 700, the script not group/world-writable, everything on the root's
// device, the root not group/world-writable without the sticky bit — and any mismatch is ONE line, `refused: … — nothing was run`, exit 78 (66 when the series is gone).
// The two `copy` steps are guarded too: a BARE `stat` of the root and the series (and run/) runs before `scp`, so scp never follows a link someone planted there.
// The raw argument builders are not exported by the library, `runChecked` takes no builder of its own and `copyChecked` builds the scp argv itself; there is no other road.
// Every external command has a finite timeout (10 minutes); a hung or missing ssh/scp is REPORTED (its error on stderr, a non-zero status), never lost.
//
// FIRST LIVE USE: verify, from the lead's machine, the mode `scp -p` leaves on a copied script (the stat check wants 700-class files: not group/world-writable, the owner root).
// A refused mode is fixed by a CHECKED chmod step added to this driver, never by relaxing the check.
//
// There is NO root option: the series root is `/tmp`, always. A custom root would put the checked folder under ancestors this script does not check (the stat reads the
// root's own facts, not its parents'); `/tmp` is the one root whose mode and sticky bit the host's own policy fixes. (The tests reach another root through the `options`
// argument of `lead()`, never through the command line.) Exit codes: 0 done; 64 usage, and also a value REFUSED BY A PATTERN (a series, a phase, a probe, a host that does not match
// its shape: nothing is run); 66 the series is gone; 78 refused by the stat check; anything else is the step's own.
import { spawnSync } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { readdirSync, realpathSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { copyChecked, doneArgv, mkdirArgv, renderRunScript, runChecked } from '../lib/native-run.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const USAGE = 'usage: node scripts/native/lead.mjs <host> name <vibe|kimi> | mkdir <series> | copy <series> make|tools | copy <series> phase <phase> <probe> [args…] | make <series> | phase <series> <phase> | abort <series> <phase> | remove <series>';
const SHAPES = new Set(['vibe', 'kimi']);

const TIMEOUT_MS = 10 * 60 * 1000;

/**
 * The default `exec`: the real ssh / scp, argument array and no shell on this side, with a FINITE timeout. A spawn failure or a timeout is reported: the error goes to stderr
 * (it is not lost behind an empty stderr) and the status is never 0.
 */
export const makeRealExec = (timeoutMs = TIMEOUT_MS) => (argv, execOptions = {}) => {
  const r = spawnSync(argv[0], argv.slice(1), { encoding: 'utf8', timeout: timeoutMs, cwd: execOptions.cwd });
  const failed = r.error === undefined ? '' : `${argv[0]}: ${r.error.code ?? ''} ${r.error.message}`.trim();
  return { status: r.error === undefined ? (r.status ?? 1) : (r.status || 1), stdout: r.stdout ?? '', stderr: [r.stderr ?? '', failed].filter((part) => part !== '').join('\n') };
};

const today = () => new Date().toISOString().slice(0, 10).replaceAll('-', '');

/**
 * Runs one step; answers its exit code. `deps`: `exec(argv)` (ssh and scp; a test's stand-in parses the words with a shell), `options` (`{ root, ownerUid }` — for the tests
 * only: the command line never sets them), `out`, `err`, `tempDir` (where a phase script is rendered before scp).
 */
export async function lead(args, { exec: given, timeoutMs, options = {}, out = (line) => console.log(line), err = (line) => console.error(line), tempDir = '.' } = {}) {
  const exec = given ?? makeRealExec(timeoutMs);
  const [host, step, ...rest] = args;
  const usage = (why) => { err(`${why}\n${USAGE}`); return 64; };
  const opts = { ...options, host };
  const report = (result) => {
    if (result.refused !== undefined) { err(result.refused); return result.status; }
    if (result.stdout) out(String(result.stdout).trimEnd());
    if (result.stderr) err(String(result.stderr).trimEnd());
    return result.status;
  };
  const checked = async (kind, series, phase) => report(await runChecked(kind, series, phase, opts, exec));
  const copied = async (kind, series, sources, dest, extra) => report(await copyChecked(kind, series, sources, dest, opts, exec, extra));
  try {
    if (host === undefined || step === undefined) return usage('a host and a step are needed');
    const count = (...n) => n.includes(rest.length);
    switch (step) {
      case 'name':
        if (!count(1) || !SHAPES.has(rest[0])) return usage('name takes vibe or kimi');
        out(`${options.root ?? '/tmp'}/native-${rest[0]}-${today()}-${randomBytes(3).toString('hex')}`);
        return 0;
      case 'mkdir': {
        if (!count(1)) return usage('mkdir takes a series');
        const result = await exec(mkdirArgv(rest[0], opts));
        if (result.status !== 0) { err(`mkdir failed (status ${result.status}): ${String(result.stderr).trim()} — stopping, nothing was copied or run`); return result.status; }
        return report(result);
      }
      case 'make': return count(1) ? await checked('make', rest[0]) : usage('make takes a series');
      case 'read': return count(2) ? await checked('read', rest[0], rest[1]) : usage('read takes a series and a phase');
      case 'done': {
        if (!count(2)) return usage('done takes a series and a phase');
        const result = await exec(doneArgv(rest[0], rest[1], opts));
        if (result.status === 0) out('done');
        else if (result.status === 1) err('not yet');
        else report(result);
        return result.status;
      }
      case 'remove': return count(1) ? await checked('remove', rest[0]) : usage('remove takes a series');
      case 'abort': return count(2) ? await checked('abort', rest[0], rest[1]) : usage('abort takes a series and a phase');
      case 'phase': return count(2) ? await checked('phase', rest[0], rest[1]) : usage('phase takes a series and a phase');
      case 'copy': {
        const [series, what, phase, probe, ...probeArgs] = rest;
        if (series === undefined) return usage('copy takes a series');
        if (what === 'make' && rest.length === 2) return await copied('bare', series, [join(HERE, 'make-series.sh')], 'make-series.sh');
        if (what === 'tools' && rest.length === 2) return await copied('bare-run', series, [join(HERE, 'abort.sh'), join(HERE, 'remove-series.sh')], 'run/');
        if (what === 'checkout' && rest.length === 3) {
          const dir = resolve(phase);
          const entries = readdirSync(dir).map((name) => join(dir, name));
          if (entries.length === 0) return usage(`${dir} is empty: nothing to copy`);
          return await copied('bare-run', series, entries, 'checkout/', { recursive: true });
        }
        if (what === 'phase' && phase !== undefined && probe !== undefined) {
          const text = renderRunScript({ series, phase, probe, args: probeArgs, root: options.root ?? '/tmp' });
          const local = join(tempDir, `${phase}.sh`);
          writeFileSync(local, text, { mode: 0o700 });
          return await copied('bare-run', series, [local], `run/${phase}.sh`);
        }
        return usage('copy takes make, tools, or phase <phase> <probe> [args…]');
      }
      default: return usage(`unknown step '${step}'`);
    }
  } catch (error) {
    // A value that failed its pattern (a series, a phase, a probe, a host) never reaches a command: it is an exception, said as one line.
    err(`refused: ${String(error?.message ?? error).split('\n')[0]}`);
    return 64;
  }
}

if (process.argv[1] !== undefined && resolve(realpathSync(process.argv[1])) === realpathSync(fileURLToPath(import.meta.url))) {
  const { startOwnedRun } = await import('../../src_vs_code/scripts/ownedRun.mjs');
  const run = await startOwnedRun({ what: 'native-lead' });
  const status = await lead(process.argv.slice(2), { tempDir: run.folder });
  run.end();
  process.exit(status);
}
