import { access } from 'node:fs/promises';
import { delimiter, isAbsolute, join } from 'node:path';
import { needsShell, parseCliVersion, shimCommandLine, unquoted, versionProbeCandidates } from './cliVersions';
import { hostPlatform, Platform } from './hostSide';
import { launch } from './processLauncher';

/**
 * Asking a binary what version it is, for anything on this machine that answers `--version`.
 *
 * <p>It lived inside `panelProvider.ts` and served the vendor CLIs only. It is here because the
 * server binary now needs exactly the same call with exactly the same contract, and a second spawn
 * helper is how two probes drift into behaving differently — one with a timeout, one without.</p>
 *
 * <p><b>The Windows shim is the one case that gets a shell</b>, and everything about that case is
 * narrowed before a shell sees it: the platform is checked, a bare name is resolved to an absolute
 * path so `cmd.exe` cannot find a different file of that name, and the path is refused if it could
 * escape the quoting. The empty working directory that used to be arranged here now belongs to
 * `processLauncher.workingDirectory`, which applies it to every shell launch rather than to this
 * caller only.</p>
 *
 * <p>The spawn and its hardening live in `processLauncher.ts`. What stayed here is what is about
 * VERSIONS: which binary to ask, and what an answer has to look like to count.</p>
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
export async function askVersion(executable: string, platform: Platform = hostPlatform()): Promise<string> {
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

/** The probe itself, with the cap and the "nothing rather than an error" contract. */
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
    const child = launch(target, args, { shell });

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

/**
 * Where a bare executable name actually is, or empty when the PATH does not have it.
 *
 * <p>The name already carries its extension (`versionProbeCandidates` supplies `codex.cmd`), so
 * this is a directory walk and not a PATHEXT search. Empty rather than a guess: handing an
 * unresolved name to a shell is exactly the case this exists to prevent.</p>
 *
 * <p>It lives here rather than in the launcher, and that is a decision rather than an accident: it
 * needs `unquoted` from `cliVersions`, and a low-level process primitive that imports a CLI-domain
 * module has its layers inverted — a circular hazard the moment CLI code wants to launch something.
 * The launcher starts processes; knowing which file a vendor's bare name means is this file's job.
 * (gemini, the code round.)</p>
 */
/**
 * The FILE a vendor's bare name means, or empty when the PATH does not have one.
 *
 * <p>`spawn` does not search PATHEXT: `spawn('codex')` on Windows fails with `ENOENT` even though
 * `codex.cmd` is right there on the PATH and every shell finds it. The version probe has known this
 * since it was written — `versionProbeCandidates` is its list — and the chat needed the same answer
 * for the same reason, so it is exported rather than written twice.</p>
 *
 * <p>Caught by the live check of the three-adapter change: both new vendors are npm shims here, and
 * a chat with either of them would have failed at the first turn with a message about `spawn`.</p>
 *
 * @param executable an absolute path, which is returned as it is, or a bare name to look up
 */
export async function resolvedExecutable(executable: string, platform: Platform = hostPlatform()): Promise<string> {
  const exe = unquoted(executable);
  if (exe.length === 0 || isAbsolute(exe)) {
    return exe;
  }
  for (const candidate of versionProbeCandidates(exe, platform)) {
    const found = await onPath(candidate);
    if (found.length > 0) {
      return found;
    }
  }

  return '';
}

async function onPath(name: string): Promise<string> {
  const path = process.env['PATH'] ?? '';
  const key = `${path}\0${name}`;
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
 * (codex and gemini, an earlier code round.)</p>
 */
const resolvedOnPath = new Map<string, string>();

