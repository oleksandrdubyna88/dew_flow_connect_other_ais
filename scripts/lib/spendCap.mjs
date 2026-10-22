// The input-token cap of a paid probe, checked BEFORE a launch (a launch cannot be stopped midway).
//
// A launch is refused when what was spent plus a launch as large as the largest one so far would pass the cap. The first
// agy follow-up run (2026-10-21) checked only AFTER each launch and passed 400 000 by starting a launch that alone took
// 357 k. One launch can still exceed the largest seen so far; a record that uses this rule says so.

/** '' when another launch fits; otherwise the sentence the probe stops with. */
export function capRefusal(spent, launchInputs, cap) {
  const largest = Math.max(0, ...launchInputs);
  return spent + largest > cap
    ? `input-token cap: ${spent} spent + ${largest} for a launch like the largest so far > ${cap}`
    : '';
}
