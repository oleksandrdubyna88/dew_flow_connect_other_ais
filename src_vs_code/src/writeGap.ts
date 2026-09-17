/**
 * How much of this run the ledger could not keep, and over what window.
 *
 * <p>Pure, and its own module for the reason the whole funnel is split this way: `notify.ts` imports
 * `vscode`, so no test in this repository can import it, and a state machine nobody can run is a
 * state machine nobody can check. Everything here is arithmetic over three fields, and every rule in
 * it was a way of getting the count wrong.</p>
 *
 * <p>The gap is reported TWICE, on purpose, and the two reports are different things. Live, it is a
 * sentence on the panel — this window, right now, cannot write. Durably, it is a record carried into
 * the ledger by the next append that lands, because a sentence dies with its host and the records it
 * describes are still missing after a reload.</p>
 */

/** What is missing, and between which two instants. */
export interface Gap {
  readonly lost: number;
  /** ISO, the first loss of this hole. Empty when there is no hole. */
  readonly since: string;
  /** ISO, the most recent loss. Empty when there is no hole. */
  readonly until: string;
}

/** No hole at all — what a run starts with, and what a written-down hole leaves behind. */
export const NO_GAP: Gap = { lost: 0, since: '', until: '' };

/**
 * One more record the disk refused.
 *
 * <p>`since` is set once and never moves, because it is the beginning of the hole; `until` follows
 * every loss, because the hole is still opening. A hole with one loss has both at the same
 * instant, which reads correctly: "between 09:12:44 and 09:12:44".</p>
 */
export function widened(gap: Gap, atIso: string): Gap {
  return {
    lost: gap.lost + 1,
    since: gap.lost === 0 ? atIso : gap.since,
    until: atIso,
  };
}

/**
 * What is left after `written` was successfully recorded.
 *
 * <p>SUBTRACTED, never zeroed, and that is the one rule here worth the module. Writing the gap
 * record is an append, an append is awaited, and a notice can be lost WHILE it is being awaited — so
 * a flush that cleared the counter outright would swallow exactly the losses that happened during
 * the reporting of the earlier ones. What remains keeps the newest instant and begins where the
 * written record ended, which is the truthful reading of a hole whose first half is now on disk.</p>
 */
export function settled(gap: Gap, written: Gap): Gap {
  const lost = Math.max(0, gap.lost - written.lost);
  if (lost === 0) {
    return NO_GAP;
  }

  return { lost, since: written.until, until: gap.until };
}
