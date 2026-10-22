// The input-token budget of a paid probe — every rule about what a launch may spend lives here, so the probe holds none of it.
//
// A launch is refused BEFORE it starts when what was spent plus a launch as large as the largest one so far would pass the cap
// (a launch cannot be stopped midway). The first agy follow-up run (2026-10-21) checked only AFTER each launch and passed
// 400 000 by starting a launch that alone took 357 k. One launch can still exceed the largest seen so far; a record that uses
// this rule says so.
//
// The budget FAILS CLOSED: a number that is not a finite, non-negative number is never read as "nothing spent" (NaN compares
// false with everything, so `NaN > cap` would wave every launch through), and a launch whose report carries no usable
// `input_tokens` is a refusal, not a zero.

const sound = (value) => typeof value === 'number' && Number.isFinite(value) && value >= 0;

/** '' when another launch fits; otherwise the sentence the probe stops with. Any value that is not sound refuses the launch. */
export function capRefusal(spent, launchInputs, cap) {
  if (!sound(spent)) return 'input-token cap: refusing to launch, the spent total is not a finite non-negative number';
  if (!Array.isArray(launchInputs) || !launchInputs.every(sound)) return 'input-token cap: refusing to launch, a launch input is not a finite non-negative number';
  if (!sound(cap)) return 'input-token cap: refusing to launch, the cap is not a finite non-negative number';
  const largest = Math.max(0, ...launchInputs);
  return spent + largest > cap
    ? `input-token cap: ${spent} spent + ${largest} for a launch like the largest so far > ${cap}`
    : '';
}

/** The hard stop after a launch: true when `spent` is past `cap` — and true when either is not sound (fail closed). */
export function capPassed(spent, cap) {
  return !sound(spent) || !sound(cap) || spent > cap;
}

/**
 * `{ tokens, refusal }` for a launch's `usage` block: the finite non-negative `input_tokens` it reports, or `tokens: 0` with
 * a `refusal` sentence (never echoing the value) when there is no usable one — the probe stops on a refusal rather than count
 * the launch as free.
 */
export function inputTokensOf(usage) {
  if (usage === null || typeof usage !== 'object' || Array.isArray(usage)) return { tokens: 0, refusal: 'the launch reported no usage block' };
  const value = usage.input_tokens;
  return sound(value)
    ? { tokens: value, refusal: '' }
    : { tokens: 0, refusal: `the launch's usage has no finite non-negative input_tokens (found ${value === null ? 'null' : typeof value})` };
}
