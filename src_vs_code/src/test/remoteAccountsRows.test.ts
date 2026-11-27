import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import { MANAGER_WORDS } from '../remoteAccountsReply';
import { INSTALL_JQ, forList } from '../remoteAccountsRows';

/**
 * What is left of the cut's rows (E2.S3, *Removed*): the one road of VM text and the one table of commands. Their other cases moved to the model's
 * tests (`remoteAccountsTable.test.ts`, E2S2-T11 and the rest).
 */

test('A4-T9: INSTALL_JQ is exactly the plan\'s table', () => {
  assert.deepEqual({ ...INSTALL_JQ }, {
    'apt-get': 'sudo apt-get install -y jq', dnf: 'sudo dnf install -y jq', yum: 'sudo yum install -y jq', apk: 'apk add jq', none: undefined,
  });
  assert.deepEqual(Object.keys(INSTALL_JQ).sort(), [...MANAGER_WORDS].sort(), 'a manager word has no row in the table');
});

test('A4-T12: forList takes out the bidi override, the zero-width space and the bell, breaks $( and cuts at max with an ellipsis', () => {
  const bidi = String.fromCodePoint(0x202e);
  const zero = String.fromCodePoint(0x200b);
  const bell = String.fromCodePoint(7);
  const text = `a${bidi}b${zero}c${bell}d $(zap) ${'x'.repeat(10_000)}`;
  const out = forList(text, 40);

  assert.ok(![bidi, zero, bell].some((one) => out.includes(one)), 'a control or format character survived');
  assert.ok(out.endsWith('…') && [...out].length <= 40, `the cut: ${out}`);
  assert.equal(forList('$(zap)', 40), '$ (zap)');
  assert.equal(forList(forList(text, 40), 40), out, 'doing it twice changed it');
  assert.equal(forList('short', 40), 'short', 'a short text was changed');
});
