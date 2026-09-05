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
  return capture(target, args, shell, CAP_MS).then(({ code, output }) => (code === 0 ? parseCliVersion(output) : ''));
}

/**
 * Everything a binary printed, or nothing at all.
 *
 * <p>The version probe's own spawn, widened rather than copied: the hardening below — the tree kill,
 * the synchronous-throw catch, the empty working directory for the shell branch — was written
 * against real failures, and a second launcher would have to learn each of them again.</p>
 */
export function capture(
  target: string,
  args: readonly string[],
  shell: boolean,
  capMs: number,
): Promise<{ code: number; output: string }> {
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
      resolve({ code: -1, output: '' });
      return;
    }

    const timer = setTimeout(() => {
      // `child.kill()` reaches `cmd.exe` and NOT what the shim started under it, so a shell probe
      // that times out would leave the grandchild running — every 8 seconds, for as long as the
      // panel repaints. Both codex's and the local reviewer's rounds named this; the tree is what
      // has to go.
      killTree(child, shell);
      resolve({ code: -1, output: '' });
    }, capMs);

    let output = '';
    child.stdout?.on('data', (chunk: Buffer) => {
      output += chunk.toString();
    });
    child.on('error', () => {
      clearTimeout(timer);
      resolve({ code: -1, output: '' });
    });
    child.on('close', (code) => {
      clearTimeout(timer);
      resolve({ code: code ?? -1, output });
    });
  });
}

/**
 * Kill what we started — the whole tree when a shell is between us and the real process.
 *
 * <p><b>`child.kill()`, never `process.kill(pid)`</b>: a probe that exits in the same tick the timer
 * fires has a pid that no longer exists, and Windows reuses pids — so killing by number can throw
 * `ESRCH` or, worse, terminate whatever now holds that number. `child.kill()` on an exited child is
 * a no-op. (codex, the code round.)</p>
 *
 * <p><b>`taskkill` by ABSOLUTE path.</b> `CreateProcess` searches the application directory and the
 * working directory before the system one, so a bare `taskkill` could be a file planted in an opened
 * workspace, run with the extension host's privileges. Raised as Blocking in the same round —
 * against the fix for another hole in this file, which is a fair description of why a shell is worth
 * this much care.</p>
 */
function killTree(child: ReturnType<typeof spawn>, shell: boolean): void {
  const pid = child.pid;
  if (!shell || process.platform !== 'win32' || pid === undefined) {
    child.kill();
    return;
  }
  try {
    spawn(TASKKILL, ['/pid', String(pid), '/t', '/f'], { windowsHide: true, cwd: tmpdir() }).unref();
  } catch {
    // Nothing to report: the promise has already answered "could not be read".
  }
  child.kill();
}

/** The system utility, not whatever is called that on the PATH or in a workspace. */
const TASKKILL = join(process.env['SystemRoot'] ?? 'C:\\Windows', 'System32', 'taskkill.exe');

/**
 * Where a bare executable name actually is, or empty when the PATH does not have it.
 *
 * <p>The name already carries its extension (`versionProbeCandidates` supplies `codex.cmd`), so
 * this is a directory walk and not a PATHEXT search. Empty rather than a guess: handing an
 * unresolved name to a shell is exactly the case this exists to prevent.</p>
 */
async function onPath(name: string): Promise<string> {
  const path = process.env['PATH'] ?? '';
  const key = `${path}\u0000${name}`;
  const known = resolvedOnPath.get(key);
  if (known !== undefined) {
    return known;
  }

  let found = '';
  for (const dir of path.split(delimiter)) {
    if (dir.length === 0) {
      continue;
    }
    const candidate = join(unquoted(dir), name);
    try {
      await access(candidate);
      found = candidate;
      break;
    } catch {
      // Not here; the next directory is not an error.
    }
  }
  resolvedOnPath.set(key, found);

  return found;
}

/**
 * Where each bare name resolved to, MISSES INCLUDED, keyed by the PATH it was resolved against.
 *
 * <p>A miss is the expensive case — it walks every directory — and it is also the common one, since
 * the candidate list tries `codex.cmd` on a machine that may only have `codex.exe`. Without this,
 * a long PATH costs its whole length in `access` calls per candidate per probe. Keying on the PATH
 * itself means a machine whose PATH changes re-resolves rather than trusting a stale answer.
 * (codex and gemini, the code round.)</p>
 */
const resolvedOnPath = new Map<string, string>();

/** The host's platform, narrowed to the three this extension answers for. */
function current(): Platform {
  return process.platform === 'win32' ? 'win32' : process.platform === 'darwin' ? 'darwin' : 'linux';
}
