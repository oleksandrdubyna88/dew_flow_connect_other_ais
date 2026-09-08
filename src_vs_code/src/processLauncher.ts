import { spawn } from 'node:child_process';
import { access } from 'node:fs/promises';
import { delimiter, join } from 'node:path';
import { tmpdir } from 'node:os';
import { unquoted } from './cliVersions';

/**
 * The one place this extension starts a process.
 *
 * <p>It is the version probe's own spawn, WIDENED rather than copied. Every guard below was written
 * against a real failure — a `.cmd` that throws synchronously instead of emitting `error`, a shell
 * probe whose grandchild outlived the kill, a bare `taskkill` that could be a file planted in an
 * opened workspace, a working directory `cmd.exe` searches before the PATH. A second launcher would
 * have had to learn each of them again, and the one already in the tree (`wslNetwork.ts`) shows how
 * that goes: it has a different stdio contract and none of this hardening.</p>
 *
 * <p><b>Why a handle and not a promise.</b> `capture` wants the whole output and then the exit code,
 * which a promise says well. A chat session wants to write a line, read lines back, and keep the
 * process for the next question — which a promise cannot say at all. One launcher answers both by
 * returning the child as an object; `capture` is re-expressed over it in `versionProbe.ts` and its
 * three callers cannot tell.</p>
 *
 * <p><b>`close`, not `exit`.</b> The exit event here fires on `close`, when the child's stdio has
 * also ended. `exit` can arrive with output still buffered, and the version probe would then parse a
 * truncated banner into "no version" — the kind of defect that reproduces once a fortnight on a slow
 * machine and never on the one it was written on.</p>
 */

/** How much stderr is worth keeping. Enough for a stack or a refusal; not a leak. */
const STDERR_TAIL_MAX = 8000;

export interface LaunchOptions {
  /** Run through the platform shell. Only the Windows shim case sets this — see `needsShell`. */
  readonly shell?: boolean;
  /** The child's working directory. The shell branch must pass an empty one. */
  readonly cwd?: string;
}

/**
 * A started child, or one that could not be started.
 *
 * <p>Every subscription is late-safe: a listener registered after the event already happened is
 * called anyway. Without that, `launch` on a name that does not exist would be a silent handle —
 * the caller subscribes on the next line and the `error` has already gone.</p>
 */
export interface ProcessHandle {
  /** Write one line (a newline is appended). `false` when the pipe is already gone. */
  writeLine(line: string): boolean;
  /** Raw stdout, as it arrives. For a caller that wants the text exactly as printed. */
  onStdout(listener: (chunk: string) => void): void;
  /** Whole stdout lines, split across chunk boundaries, each delivered once. */
  onLine(listener: (line: string) => void): void;
  /** The exit code, once, on `close`. Never fires after `onError`. */
  onExit(listener: (code: number) => void): void;
  /** The child could not be started, or died of its own error. Fires once. */
  onError(listener: (reason: string) => void): void;
  /** The tail of what the child wrote to stderr, capped. */
  stderrTail(): string;
  /** Kill what we started — the whole tree when a shell is in the way. Safe to call twice. */
  kill(): void;
}

/**
 * Start a process, or answer with a handle that is already in the error state.
 *
 * <p><b>`spawn` can THROW rather than emit `error`</b>, and it does for a real case: node refuses a
 * `.cmd` without a shell with a synchronous `EINVAL` (the 2024 argument-injection fix). Measured on
 * `codex.cmd`, where the exception escaped `render` and the panel stopped repainting — which is why
 * the try/catch is around the call itself and not only in an `error` handler.</p>
 */
export function launch(target: string, args: readonly string[], options: LaunchOptions = {}): ProcessHandle {
  const shell = options.shell === true;

  let child: ReturnType<typeof spawn> | undefined;
  let failure = '';
  try {
    child = spawn(target, [...args], {
      shell,
      windowsHide: true,
      ...(options.cwd === undefined ? {} : { cwd: options.cwd }),
    });
  } catch (reason) {
    failure = reason instanceof Error ? reason.message : String(reason);
  }

  return child === undefined ? failedHandle(failure) : liveHandle(child, shell);
}

/** A handle for a child that never started. Late-safe like any other, and `kill` is a no-op. */
function failedHandle(reason: string): ProcessHandle {
  return {
    writeLine: () => false,
    onStdout: () => undefined,
    onLine: () => undefined,
    onExit: () => undefined,
    onError: (listener) => {
      listener(reason);
    },
    stderrTail: () => '',
    kill: () => undefined,
  };
}

function liveHandle(child: ReturnType<typeof spawn>, shell: boolean): ProcessHandle {
  const stdoutListeners: ((chunk: string) => void)[] = [];
  const lineListeners: ((line: string) => void)[] = [];
  const exitListeners: ((code: number) => void)[] = [];
  const errorListeners: ((reason: string) => void)[] = [];

  let pending = '';
  let stderr = '';
  let exited: number | undefined;
  let errored: string | undefined;

  const deliverLines = (chunk: string): void => {
    pending += chunk;
    // A line is only whole once its terminator has arrived; whatever follows the last one stays in
    // `pending` for the next chunk. This is the split that a naive `chunk.split('\n')` gets wrong
    // exactly when the pipe is busiest.
    const parts = pending.split(/\r?\n/);
    pending = parts.pop() ?? '';
    for (const line of parts) {
      for (const listener of lineListeners) {
        listener(line);
      }
    }
  };

  child.stdout?.on('data', (chunk: Buffer) => {
    const text = chunk.toString();
    for (const listener of stdoutListeners) {
      listener(text);
    }
    deliverLines(text);
  });

  child.stderr?.on('data', (chunk: Buffer) => {
    stderr = `${stderr}${chunk.toString()}`.slice(-STDERR_TAIL_MAX);
  });

  child.on('error', (reason: Error) => {
    if (errored === undefined && exited === undefined) {
      errored = reason.message;
      for (const listener of errorListeners) {
        listener(errored);
      }
    }
  });

  child.on('close', (code) => {
    if (errored !== undefined || exited !== undefined) {
      return;
    }
    // Whatever the child printed without a final newline is still a line somebody wrote.
    if (pending.length > 0) {
      const last = pending;
      pending = '';
      for (const listener of lineListeners) {
        listener(last);
      }
    }
    exited = code ?? -1;
    for (const listener of exitListeners) {
      listener(exited);
    }
  });

  return {
    writeLine: (line) => {
      const stdin = child.stdin;
      if (stdin === null || stdin.destroyed || stdin.writableEnded) {
        return false;
      }
      try {
        stdin.write(`${line}\n`);
        return true;
      } catch {
        // A pipe that closed between the check and the write is a state, not an exception: the
        // caller learns the turn failed from `onExit`/`onError`, which is where it can say so.
        return false;
      }
    },
    onStdout: (listener) => {
      stdoutListeners.push(listener);
    },
    onLine: (listener) => {
      lineListeners.push(listener);
    },
    onExit: (listener) => {
      if (exited !== undefined) {
        listener(exited);
        return;
      }
      exitListeners.push(listener);
    },
    onError: (listener) => {
      if (errored !== undefined) {
        listener(errored);
        return;
      }
      errorListeners.push(listener);
    },
    stderrTail: () => stderr,
    kill: () => killTree(child, shell),
  };
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
    // Nothing to report: the caller has already been told, or is about to be.
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
export async function onPath(name: string): Promise<string> {
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
