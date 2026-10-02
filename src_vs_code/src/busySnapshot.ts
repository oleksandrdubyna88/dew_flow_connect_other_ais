/**
 * What a page is told about the host's work in flight (research/PLAN_model_search_and_busy_marks.md §3.8).
 *
 * <p>Its own module, with no imports, because the PAGES read it: `panelView.ts` paints it and `busyMark.ts` draws it,
 * and both are bundled into webview pages that run with no `require` (`bundledPage.test.ts`). The record that produces
 * it, `inFlight.ts`, runs its work under Node's `AsyncLocalStorage` (research/PLAN_busy_mark_pauses_while_you_type.md), and a
 * page that imported the snapshot from there carried `node:async_hooks` into the bundle with it.</p>
 */

/** What a page needs to draw the host's part of the mark: how much is running, and for how long the oldest has. */
export interface BusySnapshot {
  readonly count: number;
  readonly oldestMs: number;
}

/** Nothing in flight. */
export const IDLE: BusySnapshot = { count: 0, oldestMs: 0 };
