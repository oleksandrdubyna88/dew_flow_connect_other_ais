import { spawn } from 'node:child_process';
import { needsShell, parseCliVersion, shimCommandLine } from './cliVersions';

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
 * <p><b>Only stdout is read, and only on exit 0.</b> A binary that refuses the argument writes to
 * stderr and exits non-zero — every `coai-mcp` up to 0.12.2 does — and that refusal must not parse
 * as a version. The exit code is part of it because a damaged or substituted executable can print a
 * plausible banner AND fail: taking the banner would let the panel report a version for a binary
 * that does not work, and suppress the update that would replace it. (codex, this change's gate.)</p>
 */
export function askVersion(executable: string): Promise<string> {
  return new Promise((resolve) => {
    // A `.cmd` / `.bat` shim is launched THROUGH a shell, because node refuses to launch one
    // without (the 2024 argument-injection fix), with a synchronous EINVAL rather than an `error`
    // event — measured here on `codex.cmd`, where it took the panel's whole repaint down with it.
    // Everything else keeps `shell: false`: a shim is the exception, not the rule.
    const line = needsShell(executable) ? shimCommandLine(executable) : '';
    if (needsShell(executable) && line.length === 0) {
      resolve(''); // a path a shell must not be given is a version we cannot read
      return;
    }

    // `spawn` can still throw rather than emit `error`, and the docstring above promises this
    // function never does: an exception out of a repaint is not "a version that could not be read",
    // it is a panel that stops repainting.
    let child: ReturnType<typeof spawn>;
    try {
      child = line.length > 0
        ? spawn(line, [], { shell: true, windowsHide: true })
        : spawn(executable, ['--version'], { shell: false });
    } catch {
      resolve('');
      return;
    }
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
    child.on('close', (code) => {
      clearTimeout(timer);
      resolve(code === 0 ? parseCliVersion(output) : '');
    });
  });
}
