import assert from 'node:assert/strict';
import { test } from 'node:test';
import { parseUsageLine } from '../../out/usage.js';
import { disagreements, keysWritten, TRACKED, verdictOn } from '../../scripts/seam-ledger-fields.mjs';

/**
 * The ledger-fields leg of the seam, held to what it claims (E3 of todo/PLAN_the_round_counter_tail_E3.md).
 *
 * <p>The leg runs the real `coai-mcp`; these run its VERDICT over lines written the way the real writer writes them (every
 * key present, an unlabelled one spelled as an empty string), so a leg that went blind — comparing only what is present, or
 * counting an empty string as written — is a red test here and not a green seam that checks nothing.</p>
 */

const BASE = {
  utc: '2026-10-21T10:45:08.874Z', provider: 'codex', model: 'gpt-5.6-luna', role: 'consult', stage: 'Consultation', seconds: 0.2,
  tokensIn: 0, tokensOut: 0, costUsd: null, outcome: 'ok', kind: 'consult', cli: 'codex', cliVersion: 'codex-cli 0.131.0',
  effort: 'high', effortFrom: 'row', tier: 'default', endCause: '', consultation: 'c1',
};

const CUT = { ...BASE, role: 'PlanCritique', kind: 'review', outcome: 'the reviewer was cancelled', endCause: 'roundClock', consultation: '' };

const lined = (...rows) => rows.map((raw) => ({ raw, parsed: parseUsageLine(JSON.stringify(raw)) }));

test('a ledger the writer left whole crosses: every tracked key written, every one read back', () => {
  const lines = lined(BASE, CUT);

  assert.deepEqual(disagreements(lines), []);
  assert.deepEqual(keysWritten(lines), TRACKED);
  assert.equal(verdictOn(lines, 'c1'), '');
});

test('a key the reader drops is named — the writer wrote it and parseUsageLine did not give it back', () => {
  const lines = lined(BASE, CUT).map(({ raw, parsed }) => ({ raw, parsed: { ...parsed, effortFrom: undefined } }));

  assert.match(verdictOn(lines, 'c1'), /effortFrom is "row" on disk and undefined through parseUsageLine/u);
});

test('a key the writer never writes is named — a renamed C# property leaves nothing to compare, and that is the failure', () => {
  const renamed = (row) => Object.fromEntries(Object.entries(row).filter(([key]) => key !== 'consultation'));
  const lines = lined(renamed(BASE), renamed(CUT));

  assert.match(verdictOn(lines, 'c1'), /never wrote consultation/u);
});

test('an empty string is not a written key — the writer spells "not recorded" that way', () => {
  const lines = lined({ ...BASE, tier: '' }, { ...CUT, tier: '' });

  assert.deepEqual(keysWritten(lines).filter((key) => key === 'tier'), []);
  assert.match(verdictOn(lines, 'c1'), /never wrote tier/u);
});

test('a round that was cut by nobody the extension can name fails the leg, whatever else crossed', () => {
  const lines = lined(BASE, { ...CUT, endCause: 'unknown' });

  assert.match(verdictOn(lines, 'c1'), /no line reads roundClock/u);
});
