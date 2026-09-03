import { spawn } from 'node:child_process';
import { parseCliVersion } from './cliVersions';

/**
 * Asking a binary what version it is, for anything on this machine that answers `--version`.
 *
 * <p>It lived inside `panelProvider.ts` and served the vendor CLIs only. It is here because the
 * server binary now needs exactly the same call with exactly the same contract, and a second spawn
 * helper is how two probes drift into behaving differently — one with a timeout, one without.</p>
 */

/**
 * One `--version` call, answered with the version or with nothing.
 *
 * <p>A CLI that hangs must not hold up a repaint, so the wait is short and a timeout produces the
 * same "could not tell" every other failure here does. Nothing throws: an absent binary is an
 * ordinary state of a machine, not an error to show somebody.</p>
 *
 * <p><b>Only stdout is read.</b> A binary that refuses the argument writes to stderr and exits
 * non-zero — every `coai-mcp` up to 0.12.2 does — and that refusal must not parse as a version.</p>
 */
export function askVersion(executable: string): Promise<string> {
  return new Promise((resolve) => {
    const child = spawn(executable, ['--version'], { shell: false });
    const timer = setTimeout(() => {
      child.kill();
      resolve('');
    }, 8000);
    let output = '';
    child.stdout?.on('data', (chunk: Buffer) => {
      output += chunk.toString();
    });
    child.on('error', () => {
      clearTimeout(timer);
      resolve('');
    });
    child.on('close', () => {
      clearTimeout(timer);
      resolve(parseCliVersion(output));
    });
  });
}
