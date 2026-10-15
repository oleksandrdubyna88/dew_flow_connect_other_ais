// One explicit ordinal comparator for every sort in the scripts. A bare .sort() is the same order (UTF-16 code units) but hides the intent
// and trips Sonar S2871; localeCompare would change the order, and with it the synthetic clock's ranks, the agent-file order and every
// fixture byte. Pinned by ordinal.test.mjs.
export const compareOrdinal = (a, b) => {
  if (a < b) return -1;
  return a > b ? 1 : 0;
};
