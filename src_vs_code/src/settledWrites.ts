/**
 * Storing what a person types, once they have stopped typing it, and one write at a time.
 *
 * <p>Pure — no `vscode` handle — so the two rules it exists for are reachable from a unit test
 * instead of only from a webview nobody can drive in this suite. It was private inside
 * `rolesPanel.ts` until the phrases tab needed exactly it; both hosts call this now, because a
 * second copy of a concurrency rule is the copy that drifts and nothing notices.</p>
 *
 * <p><b>Rule one: one write at a time.</b> `config.update` is asynchronous, and two of them started
 * from two keystrokes read the same rows — whichever resolves last wins, so the earlier keystroke's
 * value overwrites the later one and the field reverts under the person's hands. A single promise
 * chain is the whole fix. It is short because nothing here is slow, and a queue that can only grow
 * while a key is held is bounded by the typing.</p>
 *
 * <p><b>Rule two: a typed field waits to settle.</b> Every keystroke is otherwise a write to
 * `settings.json`, which VS Code then broadcasts as a configuration change to every listener in the
 * window — a forty-character name was forty of them in four seconds. Settling turns that into one
 * write. Keyed by FIELD, so typing in a name and then in a body stores both rather than the last
 * one.</p>
 *
 * <p><b>And nothing settling is ever dropped.</b> A structural command redraws the page and the
 * redraw replaces whatever is half-typed in it, so the settling fields are stored first, in the
 * order they were typed. Closing the tab does the same through {@link SettledWrites.flush} — text
 * somebody typed and then closed the tab on is the one data loss a page like this can cause.</p>
 */

/** How long to wait after the last keystroke before storing a typed field. */
export const SETTLE_MS = 300;

export interface SettledWrites<C> {
  /** One message from the page: a typed field waits to settle, anything else goes straight through. */
  queue(command: C): void;
  /** Drained, and waited for — the tab is closing and nothing else will carry these. */
  flush(): Promise<void>;
}

export interface SettledWritesOptions<C> {
  /** Store the command. Answers whether the page must be redrawn. */
  readonly apply: (command: C) => Promise<boolean>;
  /**
   * Redraw the page. Called only when `apply` asked for it, and AWAITED when it answers with a
   * promise — the roles tab's redraw reads prompt files from disk, and a write that started while
   * it was still reading would be the very interleaving this module exists to prevent.
   */
  readonly render: () => void | Promise<void>;
  /** A write that failed. Shown to the person by the host; never swallowed. */
  readonly report: (error: unknown) => void;
  /**
   * The key a typed field settles under, or nothing for a command that is not typing.
   *
   * <p>A command that is not typing goes straight through: a click has no caret to disturb, and
   * making somebody wait 300 ms to see a row appear would be a delay bought for nothing.</p>
   */
  readonly fieldOf: (command: C) => string | undefined;
  /** Overridable so a test does not have to wait the real interval. */
  readonly settleMs?: number;
  /** Overridable so a test can drive the timers it created. */
  readonly setTimer?: (run: () => void, ms: number) => unknown;
  readonly clearTimer?: (timer: unknown) => void;
}

export function settledWrites<C>(options: SettledWritesOptions<C>): SettledWrites<C> {
  const settleMs = options.settleMs ?? SETTLE_MS;
  const setTimer = options.setTimer ?? ((run, ms): unknown => setTimeout(run, ms));
  const clearTimer = options.clearTimer ?? ((timer): void => { clearTimeout(timer as NodeJS.Timeout); });
  const settling = new Map<string, { readonly command: C; readonly timer: unknown }>();
  let working: Promise<void> = Promise.resolve();

  function run(command: C): void {
    working = working
      .then(() => options.apply(command))
      // AWAITED, by returning it: a host whose redraw reads files (the roles tab does) would
      // otherwise have the next write start while the redraw was still reading.
      .then((again) => (again ? options.render() : undefined))
      .catch((error: unknown) => { options.report(error); });
  }

  function settle(field: string, command: C): void {
    clearTimer(settling.get(field)?.timer);
    settling.set(field, {
      command,
      timer: setTimer(() => {
        settling.delete(field);
        run(command);
      }, settleMs),
    });
  }

  /** Everything still settling, stored now rather than when its timer would have fired. */
  function drain(): void {
    const pending = [...settling.values()];
    settling.clear();
    for (const { command, timer } of pending) {
      clearTimer(timer);
      run(command);
    }
  }

  return {
    queue(command: C): void {
      const field = options.fieldOf(command);
      if (field !== undefined) {
        settle(field, command);

        return;
      }
      drain();
      run(command);
    },
    async flush(): Promise<void> {
      drain();
      await working;
    },
  };
}
