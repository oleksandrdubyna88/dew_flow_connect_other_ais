import assert from 'node:assert/strict';
import { test } from 'node:test';
import { securityEnv, securityLaneFrom } from '../securityLane';

/**
 * A signal's words (`signals`) and a card's own words (a prompt's `words`) cross to coai-mcp only when the installed
 * binary lists `securityWords` (research/PLAN_one_model_catalog.md, epic 2, story 4).
 *
 * <p>Not a nicety: an older binary refuses an unknown member of the lane's root — the WHOLE lane goes off — and an unknown
 * member of a prompt refuses that prompt. Held back, the older binary runs the lane on its shipped words, as it always did.</p>
 */

const LANE = securityLaneFrom({
  enabled: true,
  signals: { secrets: ['vaultread('] },
  prompts: [{ id: 'redteam-billing', triggers: [], focus: [], words: ['acme.charge('] }],
  runs: [{ vendor: 'codex', prompt: 'redteam-billing' }],
});

const sent = (features: readonly string[]): Record<string, unknown> =>
  JSON.parse(securityEnv(LANE, '', features)['COAI_SECURITY_LANE'] ?? '{}') as Record<string, unknown>;

const promptWords = (lane: Record<string, unknown>): unknown =>
  (lane['prompts'] as Record<string, unknown>[] | undefined)?.find((p) => p['id'] === 'redteam-billing')?.['words'];

test('a binary that lists securityWords is sent the words; one that does not keeps its lane on the shipped words', () => {
  const taking = sent(['securityWords']);
  assert.deepEqual(taking['signals'], { secrets: ['vaultread('] });
  assert.deepEqual(promptWords(taking), ['acme.charge(']);

  const older = sent([]);
  assert.equal(older['signals'], undefined, 'an unknown root member turns an older lane off');
  assert.equal(promptWords(older), undefined, 'an unknown prompt member refuses that prompt there');
  assert.equal(older['enabled'], true, 'and the lane itself still crosses');
});
