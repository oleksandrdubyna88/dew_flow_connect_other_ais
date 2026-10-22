// lib/launch-tree.mjs — start a program with a timeout whose expiry ends its WHOLE process tree, and give back what it printed.
//
// A paid probe (scripts/probe-agy-review-follow-up.mjs) launches a CLI that starts helpers of its own. `spawnSync`'s timeout is
// not a promise about those: it ends the direct child, and a helper that outlives it keeps running (and, if it is the CLI's
// model call, keeps spending). So the launch is async: when the timeout expires the child is still ALIVE, which is what lets
// `killTree` (lib/vibe.mjs: `taskkill /T /F` by pid on Windows, the process GROUP elsewhere — never by image name) reach every
// descendant. On POSIX the child leads its own process group (`detached`), which is what a group kill needs.
import { spawn } from 'node:child_process';
import { killTree } from './vibe.mjs';

/** How long after the child's exit its output pipes may stay open (a helper holding them) before the result is given anyway. */
const PIPE_GRACE_MS = 2_000;
/** How long after the tree kill the result waits for the child to be reaped. */
const REAP_GRACE_MS = 5_000;

/**
 * Runs `file args` from `cwd`, feeds `input` to its stdin, and resolves — never rejects — with
 * `{ status, signal, stdout, stderr, timedOut, error }`: `error` is '' or a code (`ENOENT`, `maxBuffer`, ...). A timeout or output
 * past `maxBuffer` ends the whole tree by the child's pid.
 */
export function runTree(file, args, { cwd, input = '', timeoutMs, maxBuffer }) {
  return new Promise((resolve) => {
    const out = { status: null, signal: null, stdout: '', stderr: '', timedOut: false, error: '' };
    let child;
    try {
      child = spawn(file, args, { cwd, stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true, detached: process.platform !== 'win32' });
    } catch (e) {
      out.error = e.code ?? 'spawn';
      resolve(out);
      return;
    }
    let settled = false;
    let backup;
    const finish = () => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      clearTimeout(backup);
      resolve(out);
    };
    const endTree = () => {
      if (child.pid) killTree(child.pid);
    };
    const timer = setTimeout(() => {
      out.timedOut = true;
      endTree();
      backup = setTimeout(finish, REAP_GRACE_MS);
    }, timeoutMs);
    const take = (field) => (chunk) => {
      out[field] += chunk;
      if (out.stdout.length + out.stderr.length > maxBuffer && !out.error) {
        out.error = 'maxBuffer';
        endTree();
      }
    };
    child.stdout.setEncoding('utf8').on('data', take('stdout'));
    child.stderr.setEncoding('utf8').on('data', take('stderr'));
    child.stdin.on('error', () => { /* the child ended before reading its input */ });
    child.on('error', (e) => {
      out.error ||= e.code ?? 'spawn';
      finish();
    });
    child.on('exit', (status, signal) => {
      out.status = status;
      out.signal = signal;
      clearTimeout(backup);
      backup = setTimeout(finish, PIPE_GRACE_MS);
    });
    child.on('close', finish);
    child.stdin.end(input);
  });
}
