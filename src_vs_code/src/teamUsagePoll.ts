/**
 * The Review rounds page's own clock for Team-server figures (todo/PLAN_team_usage_by_person.md, story 1.1).
 *
 * <p>Before this, the figures were refreshed only from the end of the SIDEBAR's render, which returns early when no
 * sidebar is held — so with the sidebar closed the page never refreshed them at all.</p>
 */

/** Something that can call a function every so often, and stop. Real timers in the product, a hand-turned one in a test. */
export interface Ticker {
  readonly every: (fn: () => void, ms: number) => unknown;
  readonly stop: (handle: unknown) => void;
}

export const REAL_TICKER: Ticker = {
  every: (fn, ms) => setInterval(fn, ms),
  stop: (handle) => { clearInterval(handle as ReturnType<typeof setInterval>); },
};

/**
 * Call `tick` every `everyMs` until the returned function is called — the page's clock, started when the page opens
 * and stopped when it closes.
 *
 * <p>One tick at a time: a refresh can take two ten-second requests, and a minute's tick arriving while the last one is
 * still waiting on a slow server would only ask it twice. A tick that fails is SAID, never thrown — nothing is above this
 * frame, so a throw here would be an unhandled rejection in the extension host.</p>
 */
export function pollWhileOpen(
  tick: () => Promise<void>,
  everyMs: number,
  ticker: Ticker = REAL_TICKER,
  warn: (message: string, reason: unknown) => void = console.error,
): () => void {
  let running = false;
  const once = (): void => {
    if (running) {
      return;
    }
    running = true;
    void tick()
      .catch((reason: unknown) => { warn('ConnectOtherAIs: the rounds log could not refresh its Team-server figures', reason); })
      .finally(() => { running = false; });
  };
  const handle = ticker.every(once, everyMs);
  // At once as well: a page opened with the sidebar closed would otherwise ask nothing for its first minute.
  once();

  return () => { ticker.stop(handle); };
}
