import { isAbsolute } from 'node:path';
import { tmpdir } from 'node:os';
import { needsShell, parseCliVersion, shimCommandLine, unquoted } from './cliVersions';
import { launch, onPath } from './processLauncher';
import { Platform } from './vendorTerminal';

/**
 * Asking a binary what version it is, for anything on this machine that answers `--version`.
 *
 * <p>It lived inside `panelProvider.ts` and served the vendor CLIs only. It is here because the
 * server binary now needs exactly the same call with exactly the same contract, and a second spawn
 * helper is how two probes drift into behaving differently — one with a timeout, one without.</p>
 *
 * <p><b>The Windows shim is the one case that gets a shell</b>, and everything about that case is
 * narrowed before a shell sees it: the platform is checked, a bare name is resolved to an absolute
 * path so `cmd.exe` cannot find a different file of that name, the path is refused if it could
 * escape the quoting, and the working directory is one with nothing in it to hijack. The reasoning
 * for each is at the decision it guards — now in `processLauncher.ts`, which owns the spawn and the
 * hardening around it. This file kept the two things that are about VERSIONS: what to ask, and what
 * an answer has to look like to count.</p>
 */

/** How long any one probe may take. A CLI that hangs must not hold up a repaint. */
const CAP_MS = 8000;

/**
 * One `--version` call, answered with the version or with nothing.
 *
 * <p>Nothing throws: an absent binary is an ordinary state of a machine, not an error to show
 * somebody. A timeout, a refusal, an unresolvable name and a non-zero exit all produce the same
 * empty string, which the panel renders as "could not be read".</p>
 *
 * <p><b>Only stdout is read, and only on exit 0.</b> A binary that refuses the argument writes to
 * stderr and exits non-zero — every `coai-mcp` up to 0.12.2 does — and that refusal must not parse
 * as a version. The exit code is part of it because a damaged or substituted executable can print a
 * plausible banner AND fail: taking the banner would let the panel report a version for a binary
 * that does not work, and suppress the update that would replace it.</p>
 */
export async function askVersion(executable: string, platform: Platform = current()): Promise<string> {
  const exe = unquoted(executable);
  if (exe.length === 0) {
    return '';
  }
  if (!needsShell(exe, platform)) {
    return run(exe, ['--version'], false);
  }

  // A BARE `codex.cmd` must never reach the shell: `cmd.exe` searches its working directory before
  // the PATH, so opening a workspace that happens to contain a file of that name would run it on
  // the next repaint. Resolving the name ourselves means the shell is only ever handed a path we
  // found on the PATH. (gemini, this change's round.)
  const path = isAbsolute(exe) ? exe : await onPath(exe);
  const line = path.length === 0 ? '' : shimCommandLine(path);

  return line.length === 0 ? '' : run(line, [], true);
}

/** The spawn itself, with the cap and the "nothing rather than an error" contract. */
function run(target: string, args: readonly string[], shell: boolean): Promise<string> {
  return capture(target, args, shell, CAP_MS).then(({ code, output }) => (code === 0 ? parseCliVersion(output) : ''));
}

/**
 * Everything a binary printed, or nothing at all.
 *
 * <p>Expressed over the one launcher rather than owning a spawn of its own. The contract its three
 * callers rely on is unchanged and is stated here rather than left to be re-derived: a synchronous
 * throw, a spawn error, a timeout and an unreadable exit all answer `code: -1` with empty output,
 * and only a real close carries what the child printed.</p>
 *
 * <p><b>The output is taken on `close`, not `exit`</b> — the launcher's own rule, and the reason is
 * this function: `exit` can arrive with output still buffered, and a truncated banner parses to no
 * version at all.</p>
 */
export function capture(
  target: string,
  args: readonly string[],
  shell: boolean,
  capMs: number,
): Promise<{ code: number; output: string }> {
  return new Promise((resolve) => {
    // Only for the shell branch, and for the same reason the name is resolved above: a working
    // directory with nothing in it is one `cmd.exe` cannot find anything hostile in.
    const child = launch(target, args, { shell, ...(shell ? { cwd: tmpdir() } : {}) });

    let output = '';
    let answered = false;
    const answer = (code: number, text: string): void => {
      if (answered) {
        return;
      }
      answered = true;
      clearTimeout(timer);
      resolve({ code, output: text });
    };

    const timer = setTimeout(() => {
      // `child.kill()` reaches `cmd.exe` and NOT what the shim started under it, so a shell probe
      // that times out would leave the grandchild running — every 8 seconds, for as long as the
      // panel repaints. Both codex's and the local reviewer's rounds named this; the tree is what
      // has to go, and the launcher is what knows how.
      child.kill();
      answer(-1, '');
    }, capMs);

    child.onStdout((chunk) => {
      output += chunk;
    });
    child.onError(() => answer(-1, ''));
    child.onExit((code) => answer(code, output));
  });
}

/** The host's platform, narrowed to the three this extension answers for. */
function current(): Platform {
  return process.platform === 'win32' ? 'win32' : process.platform === 'darwin' ? 'darwin' : 'linux';
}
