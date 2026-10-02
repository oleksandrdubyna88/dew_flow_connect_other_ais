/**
 * The watchers' debounce and their poll rule (todo/PLAN_question_consultant.md, A5): the sidebar reacts to a
 * FILE CHANGE within 175 ms, and the five-second poll is kept only where change events are not delivered.
 *
 * <p>Pure, with the timer injected, so "five events in 100 ms are one refresh at 175 ms" is a test with a stopped
 * clock rather than a test that sleeps. One road for the three watchers (consultations, escalations, question
 * consults) — the operator's 150–200 ms, so one number.</p>
 */

/** The window every file event of the three watchers is gathered into. */
export const WATCH_DEBOUNCE_MS = 175;

/** The poll, where it is kept at all. */
export const POLL_MS = 5000;

/** The two calls a debounce makes of a clock — `setTimeout` and `clearTimeout`, or a test's. */
export interface Timer {
  readonly set: (fire: () => void, ms: number) => unknown;
  readonly clear: (handle: unknown) => void;
}

export const REAL_TIMER: Timer = {
  set: (fire, ms) => setTimeout(fire, ms),
  clear: (handle) => clearTimeout(handle as ReturnType<typeof setTimeout>),
};

/** A call that may come many times, and the one cancel a disposing watcher needs. */
export interface Debounced {
  (): void;
  readonly cancel: () => void;
}

/**
 * `fire` once, `ms` after the FIRST call of a burst, however many calls follow inside the window — so a burst of
 * writes (a record rewritten per row, a temp file renamed over its target) is one repaint, and the first change
 * is never more than `ms` late. A call after the window has fired opens the next one.
 */
export function debounced(fire: () => void, ms: number, timer: Timer = REAL_TIMER): Debounced {
  let pending: unknown;
  let waiting = false;
  const call = (): void => {
    if (waiting) {
      return;
    }
    waiting = true;
    pending = timer.set(() => {
      waiting = false;
      fire();
    }, ms);
  };

  return Object.assign(call, {
    cancel: (): void => {
      if (waiting) {
        timer.clear(pending);
        waiting = false;
      }
    },
  });
}

/**
 * Whether a watched directory needs the poll: a UNC path — `\\wsl.localhost\…`, `\\wsl$\…`, `\\server\share`,
 * or `//server/share` — is where a change made by another process raises no event. A local path is watched by
 * its events alone.
 */
export function needsPoll(directories: readonly string[]): boolean {
  return directories.some((dir) => /^(\\\\|\/\/)[^\\/]/.test(dir.trim()));
}
