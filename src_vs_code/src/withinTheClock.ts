/**
 * Waiting for something, but not for ever.
 *
 * <p>ONE of these. There were two — `jsonlLedger.ts` had a private copy answering a boolean, and
 * the notifications panel grew a second answering a value — and two implementations of one
 * capability is a defect from the moment it compiles, because they drift and nothing notices. They
 * had already drifted: one returned `false` immediately for a deadline that had passed, the other
 * armed a zero-millisecond timer. (The second S5 code round found the missing ceiling; the
 * duplicate was found while fixing it.)</p>
 *
 * <p>It answers a RESULT rather than a value, because `undefined` is a perfectly good thing for
 * work to resolve to, and a helper that could not tell "it finished, with nothing" from "it never
 * finished" would be a worse bargain than the one it replaces.</p>
 *
 * <p>The losing path does NOT cancel the work — nothing can — it stops WAITING for it. If the
 * directory comes back before the host dies, the read or the append still lands.</p>
 */

/** Either what the work answered, or that the clock ran out first. */
export type InTime<T> = { readonly inTime: true; readonly value: T } | { readonly inTime: false };

/** Whatever `work` answers, if it answers within `msLeft`. */
export async function withinTheClock<T>(work: Promise<T>, msLeft: number): Promise<InTime<T>> {
  if (msLeft <= 0) {
    return { inTime: false };
  }
  let timer: NodeJS.Timeout | undefined;
  const clock = new Promise<{ readonly inTime: false }>((resolve) => {
    timer = setTimeout(() => resolve({ inTime: false }), msLeft);
    // A pending timer is itself a reason a Node process will not exit, and a helper for giving up
    // cleanly that held the process open would be a joke at its own expense. Cleared on the winning
    // path too: a twenty-second timer left armed after a fast read keeps a handle alive for nothing.
    timer.unref?.();
  });
  try {
    return await Promise.race([work.then((value) => ({ inTime: true as const, value })), clock]);
  } finally {
    if (timer !== undefined) {
      clearTimeout(timer);
    }
  }
}

/** The same wait, where only "did it finish" matters. */
export async function finishedInTime(work: Promise<unknown>, msLeft: number): Promise<boolean> {
  return (await withinTheClock(work, msLeft)).inTime;
}
