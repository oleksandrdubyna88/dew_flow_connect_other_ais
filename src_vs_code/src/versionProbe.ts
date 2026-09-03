import { spawn } from 'node:child_process';
import { access } from 'node:fs/promises';
import { delimiter, isAbsolute, join } from 'node:path';
import { tmpdir } from 'node:os';
import { needsShell, parseCliVersion, shimCommandLine, unquoted } from './cliVersions';
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
 * for each is at the decision it guards.</p>
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

/**
 * The spawn itself, with the cap and the "nothing rather than an error" contract.
 *
 * <p><b>`spawn` can THROW rather than emit `error`</b>, and it does for a real case: node refuses a
 * `.cmd` without a shell with a synchronous `EINVAL` (the 2024 argument-injection fix). Measured
 * here on `codex.cmd`, where the exception left `render` and the panel stopped repainting — which is
 * why the try/catch is around the call and not only in an `error` handler.</p>
 */
function run(target: string, args: readonly string[], shell: boolean): Promise<string> {
  return new Promise((resolve) => {
    let child: ReturnType<typeof spawn>;
    try {
      child = spawn(target, [...args], {
        shell,
        windowsHide: true,
        // Only for the shell branch, and for the same reason the name was resolved above: a working
        // directory with nothing in it is one `cmd.exe` cannot find anything hostile in.
        ...(shell ? { cwd: tmpdir() } : {}),
      });
    } catch {
      resolve('');
      return;
    }

    const timer = setTimeout(() => {
      // `child.kill()` reaches `cmd.exe` and NOT what the shim started under it, so a shell probe
      // that times out would leave the grandchild running — every 8 seconds, for as long as the
      // panel repaints. Both codex's and the local reviewer's rounds named this; the tree is what
      // has to go. (`taskkill` is on every Windows; a failure to find it is not worth reporting.)
      killTree(child.pid, shell);
      resolve('');
    }, CAP_MS);

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

/** Kill what we started — the whole tree when a shell is between us and the real process. */
function killTree(pid: number | undefined, shell: boolean): void {
  if (pid === undefined) {
    return;
  }
  if (!shell || process.platform !== 'win32') {
    process.kill(pid);
    return;
  }
  try {
    spawn('taskkill', ['/pid', String(pid), '/t', '/f'], { windowsHide: true }).unref();
  } catch {
    // Nothing to report: the promise has already answered "could not be read".
  }
}

/**
 * Where a bare executable name actually is, or empty when the PATH does not have it.
 *
 * <p>The name already carries its extension (`versionProbeCandidates` supplies `codex.cmd`), so
 * this is a directory walk and not a PATHEXT search. Empty rather than a guess: handing an
 * unresolved name to a shell is exactly the case this exists to prevent.</p>
 */
async function onPath(name: string): Promise<string> {
  for (const dir of (process.env['PATH'] ?? '').split(delimiter)) {
    if (dir.length === 0) {
      continue;
    }
    const candidate = join(unquoted(dir), name);
    try {
      await access(candidate);
      return candidate;
    } catch {
      // Not here; the next directory is not an error.
    }
  }

  return '';
}

/** The host's platform, narrowed to the three this extension answers for. */
function current(): Platform {
  return process.platform === 'win32' ? 'win32' : process.platform === 'darwin' ? 'darwin' : 'linux';
}
