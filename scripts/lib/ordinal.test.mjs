// node --test scripts/lib/ordinal.test.mjs — the explicit comparator the scripts sort with. It replaces a bare .sort() (Sonar S2871) and
// must keep EXACTLY the order the bare sort had: UTF-16 code units, not the locale. The synthetic clock's ranks, the agent-file order and the
// manifest key order all hang on it, so a locale-aware comparator (localeCompare) would silently renumber every fixture.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { compareOrdinal } from './ordinal.mjs';

const MIXED = ['b', 'B', 'a', 'A', '10', '9', '2', 'é', 'z', 'Z', 'ä', '_', '-', 'agent-9', 'agent-10', 'agent-a', '\u{1F600}', '\uFF21', ''];

test('theOrdinalComparatorOrdersAMixedSetByCodeUnit', () => {
  const expected = ['', '-', '10', '2', '9', 'A', 'B', 'Z', '_', 'a', 'agent-10', 'agent-9', 'agent-a', 'b', 'z', 'ä', 'é', '\u{1F600}', '\uFF21'];
  assert.deepEqual([...MIXED].sort(compareOrdinal), expected);
});

test('theOrdinalComparatorIsTheOrderTheBareSortHadAndNotTheLocalesOrder', () => {
  assert.deepEqual([...MIXED].sort(compareOrdinal), [...MIXED].sort(), 'identical to the default sort, so no fixture is renumbered');
  assert.notDeepEqual([...MIXED].sort(compareOrdinal), [...MIXED].sort((a, b) => a.localeCompare(b)), 'a locale comparator would change the order');
});

test('theOrdinalComparatorIsAConsistentTotalOrder', () => {
  for (const a of MIXED) for (const b of MIXED) {
    assert.equal(compareOrdinal(a, b) + compareOrdinal(b, a), 0);
    assert.equal(compareOrdinal(a, b) === 0, a === b);
  }
});
