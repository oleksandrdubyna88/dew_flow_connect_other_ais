/**
 * What a real-editor scenario does to put the profile back, and to find it quiet, when the editor can take a write away (pure, over seams).
 *
 * <p>The editor keeps ONE `globalState` value for each extension and sends the WHOLE value on every `update`; when the editor's copy of that value
 * changes, the window REPLACES its own copy with it (`research/PLAN_auto_never_asks_a_person_E2_S2_spec.md`, V1 to V3: "`await update()` proves nothing;
 * only a later read does"). So a write made a moment after another one can be taken away by the older write's own news arriving late, and a second
 * scenario that starts at once reads, and puts back, the state of the first one. The scenarios of `terminalHostsScenario.ts` and its neighbours run
 * one after the other in one editor, a few milliseconds apart; this module is what makes their order not decide them: a store is put back until a read
 * still shows it so for a quiet time, and a store is read as "found" only when two reads a step apart agree.</p>
 */

/** The time a loop waits and for how long. A step is the pause between two reads; quiet is how long the wanted state must stand; max ends the loop. */
export interface SettleLimits {
  readonly stepMs: number;
  readonly quietMs: number;
  readonly maxMs: number;
}

/** 500 ms of quiet is longer than the stall of the editor's process that was seen to take a write away (about 130 ms); 10 s is far above any normal run. */
export const SETTLE: SettleLimits = { stepMs: 100, quietMs: 500, maxMs: 10_000 };

export interface SettleSeams {
  /** One read of everything that is put back, as text, so two reads can be compared. */
  readonly read: () => Promise<string>;
  /** The writes that put it back (and nothing that tells a listener, or the listener would write it again). */
  readonly putBack: () => Promise<void>;
  readonly pause: (ms: number) => Promise<void>;
  readonly now: () => number;
}

/** One read after a step: `true` when the wanted state stands; else it is put back, and `false`. */
async function stands(want: string, seams: SettleSeams, limits: SettleLimits): Promise<boolean> {
  // Polling on purpose — each read must finish before the next one starts (S9382).
  await seams.pause(limits.stepMs); // NOSONAR
  if ((await seams.read()) === want) { // NOSONAR
    return true;
  }
  await seams.putBack(); // NOSONAR

  return false;
}

/**
 * Reads until the wanted state is what a read shows for `quietMs` in a row; a read that shows anything else puts it back and starts the quiet time again.
 * `true` when it held; `false` at `maxMs`, when the state kept changing under the writes (the scenario's own assertion then says what differs).
 */
export async function putBackUntilHeld(want: string, seams: SettleSeams, limits: SettleLimits): Promise<boolean> {
  const deadline = seams.now() + limits.maxMs;
  let since = seams.now();
  while (seams.now() < deadline) {
    since = (await stands(want, seams, limits)) ? since : seams.now(); // NOSONAR
    if (seams.now() - since >= limits.quietMs) {
      return true;
    }
  }

  return false;
}

export interface FoundSeams<T> {
  readonly read: () => Promise<T>;
  /** Whether two reads show the same state. */
  readonly same: (a: T, b: T) => boolean;
  readonly pause: (ms: number) => Promise<void>;
  readonly now: () => number;
}

/**
 * The state as it is FOUND: the first read that the next read, a step later, agrees with. At `maxMs` the last read, quiet or not (a state that never
 * settles is the scenario's own to report, from what it asserts).
 */
export async function foundQuiet<T>(seams: FoundSeams<T>, limits: SettleLimits = SETTLE): Promise<T> {
  const deadline = seams.now() + limits.maxMs;
  let last = await seams.read();
  while (seams.now() < deadline) {
    // Polling on purpose — each read must finish before the next one starts (S9382).
    await seams.pause(limits.stepMs * 2); // NOSONAR
    const next = await seams.read(); // NOSONAR
    if (seams.same(last, next)) {
      return next;
    }
    last = next;
  }

  return last;
}
