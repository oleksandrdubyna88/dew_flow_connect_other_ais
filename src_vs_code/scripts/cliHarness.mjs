/**
 * Driving a real vendor CLI once, and being honest about how it ended.
 *
 * <p>Extracted from `measure-stream.mjs` when a second probe needed the same machinery, so that the
 * rules that probe's review round established are kept in ONE place rather than re-derived by
 * whoever writes the third one. The rules are not obvious and each was paid for:</p>
 *
 * <ul>
 *   <li><b>A failure is never evidence.</b> Hung, non-zero, unspawnable and over-long are each their
 *       own {@link Ending}, and a caller that treats "did not answer" as "cannot do it" is reading a
 *       broken environment as a property of the vendor.</li>
 *   <li><b>The child is killed as a TREE.</b> With `shell: true` the direct child is `cmd.exe`, and
 *       killing it orphans the vendor CLI — still signed in, still spending a turn.</li>
 *   <li><b>`spawn` can throw synchronously.</b> Node has refused to spawn a `.cmd` without a shell
 *       since the fix for CVE-2024-27980, and `codex` installs as `codex.cmd`. That is a RESULT, not
 *       a crash.</li>
 *   <li><b>stdin can break under you.</b> A CLI that exits at once — a refused sign-in, a rate limit
 *       — turns the write into an unhandled `EPIPE` that takes the harness down instead of recording
 *       the run.</li>
 *   <li><b>Arrival is stamped per CHUNK</b>, before any line splitting, because a stream held back
 *       and released in one write is indistinguishable from a trickling one once you count lines.</li>
 * </ul>
 */
import { spawn } from 'node:child_process';

/**
 * How a run ended. Only `closed` with code 0 is a run whose CONTENT may be believed.
 *
 * @typedef {'closed' | 'timeout' | 'error' | 'stdin' | 'overflow' | 'unspawnable'} Ending
 */

/**
 * Kill a whole process TREE.
 *
 * <p>Windows has no process groups to signal, so `taskkill /T /F` is the way. Without it, killing a
 * shell arm reaps `cmd.exe` and leaves the vendor CLI running against somebody's account.</p>
 */
export function killTree(child) {
  if (child.pid === undefined) {
    return;
  }
  if (process.platform === 'win32') {
    try {
      spawn('taskkill.exe', ['/pid', String(child.pid), '/t', '/f'], { windowsHide: true, stdio: 'ignore' });

      return;
    } catch {
      // Fall through to the ordinary kill; a tree is better than nothing but nothing is not an option.
    }
  }
  child.kill();
}

/**
 * Run one CLI once, write `stdin` to it, and return everything about how it went.
 *
 * @param {{ executable: string, args: string[], cwd: string }} spec from `launchSpecFor`, never retyped
 * @param {{ useShell: boolean, stdin: string, timeoutMs: number, maxBytes: number,
 *           onChunk?: (count: number) => void }} how
 * @returns {Promise<{ chunks: { atMs: number, text: string }[], stderr: string, code: number,
 *                     ending: Ending, failure: string, ms: number }>}
 */
export function runCli(spec, how) {
  return new Promise((resolve) => {
    const started = Date.now();
    const chunks = [];
    let stderr = '';
    let bytes = 0;
    let settled = false;
    const finish = (result) => {
      if (settled) {
        return;
      }
      settled = true;
      resolve({ chunks, stderr, ms: Date.now() - started, ...result });
    };

    let child;
    try {
      child = spawn(spec.executable, spec.args, {
        cwd: spec.cwd,
        shell: how.useShell,
        stdio: ['pipe', 'pipe', 'pipe'],
        windowsHide: true,
      });
    } catch (reason) {
      finish({ code: -1, ending: 'unspawnable', failure: String(reason) });

      return;
    }

    const timer = setTimeout(() => {
      killTree(child);
      finish({ code: -1, ending: 'timeout', failure: `no terminal event within ${how.timeoutMs} ms` });
    }, how.timeoutMs);

    child.stdout.on('data', (data) => {
      const text = data.toString('utf8');
      bytes += text.length;
      if (bytes > how.maxBytes) {
        killTree(child);
        clearTimeout(timer);
        finish({ code: -1, ending: 'overflow', failure: `more than ${how.maxBytes} bytes of stdout` });

        return;
      }
      chunks.push({ atMs: Date.now() - started, text });
      how.onChunk?.(chunks.length);
    });
    child.stderr.on('data', (data) => {
      stderr += data.toString('utf8');
    });
    child.stdin.on('error', () => undefined);
    child.on('error', (reason) => {
      clearTimeout(timer);
      finish({ code: -1, ending: 'error', failure: String(reason) });
    });
    child.on('close', (code) => {
      clearTimeout(timer);
      finish({ code: code ?? -1, ending: 'closed', failure: '' });
    });

    try {
      child.stdin.write(how.stdin);
      child.stdin.end();
    } catch (reason) {
      clearTimeout(timer);
      finish({ code: -1, ending: 'stdin', failure: String(reason) });
    }
  });
}

/** Every complete NDJSON line in arrival order, each carrying the chunk time it completed in. */
export function linesOf(chunks) {
  const lines = [];
  let held = '';
  for (const chunk of chunks) {
    held += chunk.text;
    const parts = held.split(/\r?\n/);
    held = parts[parts.length - 1] ?? '';
    for (const part of parts.slice(0, -1)) {
      if (part.trim().length > 0) {
        lines.push({ atMs: chunk.atMs, text: part });
      }
    }
  }
  if (held.trim().length > 0) {
    lines.push({ atMs: chunks[chunks.length - 1]?.atMs ?? 0, text: held });
  }

  return lines;
}

/** The environment a measurement was taken in. Recorded, because a table without it ages badly. */
export function environment() {
  return {
    node: process.version,
    platform: `${process.platform} ${process.arch}`,
    takenUtc: new Date().toISOString(),
  };
}
