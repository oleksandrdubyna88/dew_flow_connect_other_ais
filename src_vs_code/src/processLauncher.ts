import { spawn } from 'node:child_process';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

/**
 * The one place this extension starts a process it controls.
 *
 * <p>It is the version probe's own launcher, WIDENED rather than copied. Every guard below was
 * written against a real failure — a `.cmd` that throws synchronously instead of emitting `error`,
 * a shell probe whose grandchild outlived the kill, a bare `taskkill` that could be a file planted
 * in an opened workspace, a working directory `cmd.exe` searches before the PATH. A second launcher
 * would have had to learn each of them again, and the one already in the tree (`wslNetwork.ts`)
 * shows how that goes: it has a different stdio contract and none of this hardening.</p>
 *
 * <p><b>Why a handle and not a promise.</b> `capture` wants the whole output and then the exit code,
 * which a promise says well. A chat session wants to write a line, read lines back, and keep the
 * process for the next question — which a promise cannot say at all. One launcher answers both.</p>
 *
 * <p><b>The one deliberate exception to "one spawn site".</b> `killTree` starts `taskkill.exe`
 * itself, so this file contains two `spawn` calls rather than one. It is named here because a
 * structural guard that counts spawns will find it and should not treat it as a regression: killing
 * a tree is not launching a child, it takes no handle, produces no output anybody reads, and is
 * unref'd immediately. Raised by codex on the code round of this very change, and answered by saying
 * so rather than by hiding it behind an injected primitive that would have one implementation.</p>
 *
 * <p><b>Two details are load-bearing.</b> The exit event fires on `close`, not `exit`: `exit` can
 * arrive with output still buffered, and a truncated banner parses to no version at all. And every
 * subscription is late-safe — output that arrived before anybody subscribed is replayed to the first
 * subscriber, because a short-lived child can print its whole answer and close before the caller's
 * next line runs.</p>
 */

/** How much stderr is worth keeping. Enough for a stack or a refusal; not a leak. */
const STDERR_TAIL_MAX = 8000;

/** Stop receiving. Returned by every stream subscription — see `ProcessHandle.onLine`. */
export type Unsubscribe = () => void;

export interface LaunchOptions {
  /** Run through the platform shell. Only the Windows shim case sets this — see `needsShell`. */
  readonly shell?: boolean;
  /** The child's working directory. Defaults per `workingDirectory` — a shell never gets the cwd. */
  readonly cwd?: string;
}

export interface ProcessHandle {
  /**
   * The child's process id, or 0 when it never started.
   *
   * <p>For the LEDGER, and for nothing else here: a chat child is written down as it starts so a
   * force-killed editor cannot leave an authenticated CLI running. Killing by this number is only
   * safe against a process whose identity has been re-checked — see `chatLedger.ts`, and the note
   * on `killTree` below for why the launcher itself never does it.</p>
   */
  readonly pid: number;
  /** Write one line (a newline is appended). `false` when the pipe is already gone. */
  writeLine(line: string): boolean;
  /**
   * Write, then CLOSE the input — for a child that reads its whole prompt and then works.
   *
   * <p>`codex exec -` is the case: it reads instructions from stdin and waits for more until the
   * stream ends, so a turn written without this simply never starts. A persistent vendor must never
   * be sent it — closing the pipe there ends the conversation.</p>
   */
  writeAndEnd(text: string): boolean;
  /** Raw stdout as it arrives. Replayed to the first subscriber; unsubscribe when done. */
  onStdout(listener: (chunk: string) => void): Unsubscribe;
  /** Whole stdout lines, split across chunk boundaries, each delivered once. */
  onLine(listener: (line: string) => void): Unsubscribe;
  /** The exit code, once, on `close`. Never fires after `onError`. Late-safe. */
  onExit(listener: (code: number) => void): void;
  /** The child could not be started, or died of its own error. Fires once. Late-safe. */
  onError(listener: (reason: string) => void): void;
  /** The tail of what the child wrote to stderr — or why it never started. */
  stderrTail(): string;
  /** Kill what we started — the whole tree when a shell is in the way. Safe to call twice. */
  kill(): void;
}

/**
 * Where a child runs.
 *
 * <p>Its own function so the rule can be TESTED rather than trusted: a shell that is handed no
 * working directory inherits the extension host's, which in VS Code is the opened workspace — and
 * `cmd.exe` searches the working directory before the PATH. A workspace holding a file named like
 * the tool being probed would then run with the host's privileges. The caller may still choose a
 * directory; what it may not do is leave a shell pointed at somebody's checkout by omission.
 * (gemini, the code round.)</p>
 */
export function workingDirectory(shell: boolean, cwd: string | undefined): string | undefined {
  if (cwd !== undefined) {
    return cwd;
  }

  return shell ? tmpdir() : undefined;
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
  const cwd = workingDirectory(shell, options.cwd);

  let child: ReturnType<typeof spawn> | undefined;
  let failure = '';
  try {
    child = spawn(target, [...args], {
      shell,
      windowsHide: true,
      ...(cwd === undefined ? {} : { cwd }),
    });
  } catch (reason) {
    failure = reason instanceof Error ? reason.message : String(reason);
  }

  return child === undefined ? failedHandle(failure) : liveHandle(child, shell);
}

/**
 * A handle for a child that never started.
 *
 * <p>Its `stderrTail` carries the REASON rather than an empty string. A caller that shows "could not
 * be read" and nothing else leaves somebody guessing between a missing binary, a denied permission
 * and a rejected argument — three different things to do next. (local and gemini, the code round.)</p>
 */
function failedHandle(reason: string): ProcessHandle {
  return {
    pid: 0,
    writeLine: () => false,
    writeAndEnd: () => false,
    onStdout: () => () => undefined,
    onLine: () => () => undefined,
    onExit: () => undefined,
    onError: (listener) => {
      listener(reason);
    },
    stderrTail: () => reason,
    kill: () => undefined,
  };
}

/**
 * A stream of values that holds what it emitted until somebody is listening.
 *
 * <p>The replay is the point. A child can write its whole answer and close before the caller's next
 * statement runs, and an `onLine` that only appends a listener loses that answer for ever — codex
 * and gemini raised it independently. What is held is handed to the FIRST subscriber and then
 * dropped: a second subscriber joining later is joining a conversation in progress, and pretending
 * otherwise would deliver one line twice.</p>
 */
function replayingFan<T>(): { emit(value: T): void; on(listener: (value: T) => void): Unsubscribe } {
  let listeners: readonly ((value: T) => void)[] = [];
  let held: readonly T[] = [];
  let opened = false;

  return {
    emit(value: T): void {
      if (!opened) {
        held = [...held, value];
        return;
      }
      for (const listener of listeners) {
        listener(value);
      }
    },
    on(listener: (value: T) => void): Unsubscribe {
      opened = true;
      listeners = [...listeners, listener];
      const pending = held;
      held = [];
      for (const value of pending) {
        listener(value);
      }

      // Without this, a chat session that subscribes per turn leaves every past turn's callback in
      // the list, and turn 100's answer is delivered to ninety-nine stale handlers. (codex, gemini.)
      return () => {
        listeners = listeners.filter((known) => known !== listener);
      };
    },
  };
}

/** A thing that happens at most once, and is still heard by whoever asks afterwards. */
function onceFan<T>(): { fire(value: T): boolean; on(listener: (value: T) => void): void } {
  let listeners: readonly ((value: T) => void)[] = [];
  let fired: { value: T } | undefined;

  return {
    fire(value: T): boolean {
      if (fired !== undefined) {
        return false;
      }
      fired = { value };
      for (const listener of listeners) {
        listener(value);
      }

      return true;
    },
    on(listener: (value: T) => void): void {
      if (fired !== undefined) {
        listener(fired.value);
        return;
      }
      listeners = [...listeners, listener];
    },
  };
}

/**
 * Whole lines out of a stream that arrives in arbitrary pieces.
 *
 * <p>A line is only whole once its terminator has arrived; whatever follows the last one waits for
 * the next chunk. This is the split a naive `chunk.split('\n')` gets wrong exactly when the pipe is
 * busiest. `flush` exists because a child that prints without a final newline still printed a line.</p>
 */
function lineSplitter(deliver: (line: string) => void): { push(text: string): void; flush(): void } {
  let pending = '';

  return {
    push(text: string): void {
      pending += text;
      const parts = pending.split(/\r?\n/);
      pending = parts.pop() ?? '';
      for (const line of parts) {
        deliver(line);
      }
    },
    flush(): void {
      if (pending.length === 0) {
        return;
      }
      const last = pending;
      pending = '';
      deliver(last);
    },
  };
}

function liveHandle(child: ReturnType<typeof spawn>, shell: boolean): ProcessHandle {
  const stdout = replayingFan<string>();
  const lines = replayingFan<string>();
  const exited = onceFan<number>();
  const errored = onceFan<string>();
  const splitter = lineSplitter((line) => lines.emit(line));
  const stderr = { tail: '' };

  // Decode as text on the STREAM, not per chunk: a Cyrillic character split across a chunk boundary
  // becomes two replacement characters if each half is decoded alone, and the passages this launcher
  // exists to carry are routinely Russian. (gemini, the code round.)
  child.stdout?.setEncoding('utf8');
  child.stderr?.setEncoding('utf8');

  child.stdout?.on('data', (text: string) => {
    stdout.emit(text);
    splitter.push(text);
  });
  child.stderr?.on('data', (text: string) => {
    stderr.tail = `${stderr.tail}${text}`.slice(-STDERR_TAIL_MAX);
  });

  // EPIPE on a pipe whose reader has gone arrives ASYNCHRONOUSLY, so no try/catch around `write` can
  // see it. Unhandled, node makes it a fatal exception and the extension host dies with it — the one
  // finding in this round that took a whole editor down. (gemini, Blocking.)
  child.stdin?.on('error', () => undefined);

  child.on('error', (reason: Error) => {
    // The reason goes into the tail as well as to the listener. A missing binary does NOT throw
    // synchronously on Windows — it arrives here, asynchronously — so a caller that only reads
    // `stderrTail` to explain a failure would have found it empty for the commonest failure there
    // is. Caught by the test written for the failed-handle half of the same finding.
    stderr.tail = `${stderr.tail}[coai] ${reason.message}`.slice(-STDERR_TAIL_MAX);
    errored.fire(reason.message);
  });
  child.on('close', (code) => {
    splitter.flush();
    exited.fire(code ?? -1);
  });

  return {
    pid: child.pid ?? 0,
    writeLine: (line) => writeLine(child, line),
    writeAndEnd: (text) => writeAndEnd(child, text),
    onStdout: stdout.on,
    onLine: lines.on,
    onExit: exited.on,
    onError: errored.on,
    stderrTail: () => stderr.tail,
    kill: () => {
      const failed = killTree(child, shell);
      if (failed.length > 0) {
        // Not swallowed: a tree kill that could not even start is why a vendor process is still
        // running, and that sentence belongs where the caller already looks. (codex, the code round.)
        stderr.tail = `${stderr.tail}\n[coai] tree kill failed: ${failed}`.slice(-STDERR_TAIL_MAX);
      }
    },
  };
}

/** The whole prompt and then EOF, or `false` when the pipe has gone. Never throws. */
function writeAndEnd(child: ReturnType<typeof spawn>, text: string): boolean {
  const stdin = child.stdin;
  if (stdin === null || stdin.destroyed || stdin.writableEnded) {
    return false;
  }
  try {
    stdin.end(text);

    return true;
  } catch {
    return false;
  }
}

/** One line into the child, or `false` when the pipe has gone. Never throws. */
function writeLine(child: ReturnType<typeof spawn>, line: string): boolean {
  const stdin = child.stdin;
  if (stdin === null || stdin.destroyed || stdin.writableEnded) {
    return false;
  }
  try {
    stdin.write(`${line}\n`);

    return true;
  } catch {
    // A pipe that closed between the check and the write is a state, not an exception: the caller
    // learns the turn failed from `onExit`/`onError`, which is where it can say so.
    return false;
  }
}

/**
 * Kill what we started — the whole tree when a shell is between us and the real process.
 *
 * <p><b>`child.kill()`, never `process.kill(pid)`</b>: a probe that exits in the same tick the timer
 * fires has a pid that no longer exists, and Windows reuses pids — so killing by number can throw
 * `ESRCH` or, worse, terminate whatever now holds that number. `child.kill()` on an exited child is
 * a no-op. (codex, an earlier code round.)</p>
 *
 * <p><b>`taskkill` by ABSOLUTE path.</b> `CreateProcess` searches the application directory and the
 * working directory before the system one, so a bare `taskkill` could be a file planted in an opened
 * workspace, run with the extension host's privileges.</p>
 *
 * @returns why the tree kill could not be started, or an empty string when it was
 */
function killTree(child: ReturnType<typeof spawn>, shell: boolean): string {
  const pid = child.pid;
  if (!shell || process.platform !== 'win32' || pid === undefined) {
    child.kill();

    return '';
  }

  const failed = spawnTaskkill(pid);
  child.kill();

  return failed;
}

/**
 * The second `spawn` in this file, in a function named so an audit can find it.
 *
 * <p>A guard that counts spawn calls will see two here and should see two: killing a tree is not
 * launching a child. It takes no handle, produces no output anybody reads, and is unref'd at once.
 * It is a named function rather than a line inside `killTree` for exactly that reason — the
 * exception should be greppable, not buried. (local, the second code round.)</p>
 *
 * <p>Both options are load-bearing, not copied habit. `windowsHide` keeps a console window from
 * flashing over the editor every time a probe times out — eight seconds apart, for as long as the
 * panel repaints. `cwd` is the temp directory for the same reason every shell launch gets one:
 * `CreateProcess` searches the working directory before the system one.</p>
 *
 * <p>`.on('error')` comes BEFORE `.unref()`: a taskkill that fails to spawn emits `error` on a child
 * nobody is listening to, and node turns that into a fatal exception in the extension host.
 * (gemini, the first code round.)</p>
 *
 * @returns why it could not be started, or an empty string
 */
function spawnTaskkill(pid: number): string {
  let failed = '';
  try {
    spawn(TASKKILL, ['/pid', String(pid), '/t', '/f'], { windowsHide: true, cwd: tmpdir() })
      .on('error', (reason: Error) => {
        failed = reason.message;
      })
      .unref();
  } catch (reason) {
    failed = reason instanceof Error ? reason.message : String(reason);
  }

  return failed;
}

/**
 * The system utility, not whatever is called that on the PATH or in a workspace.
 *
 * <p>`WINDIR` before the literal: a Windows installed on `D:` with `SystemRoot` stripped from a
 * custom child environment would otherwise point the tree kill at a path that does not exist, and
 * the failure would be silent apart from a line in the stderr tail. (gemini, the second code round.)</p>
 */
const TASKKILL = join(
  process.env['SystemRoot'] ?? process.env['WINDIR'] ?? 'C:\\Windows',
  'System32',
  'taskkill.exe',
);
