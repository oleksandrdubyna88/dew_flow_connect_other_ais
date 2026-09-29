import assert from 'node:assert/strict';
import { test } from 'node:test';

import { questionHtml } from '../questionLayout';

/**
 * A round nobody could answer reaches the sidebar's "A review is waiting on you" card as one run-on sentence,
 * and the operator asked for it to read better (2026-09-29, `todo/PLAN_the_cadence_has_its_own_section.md`,
 * story 2). The text itself is right and stays as the server wrote it; the card lays it out.
 */

// The operator's own card, as coai-mcp wrote it — the reviewers' notes cut where the server cuts them.
const FAILED_ROUND = 'The plan review gate needs your decision: no reviewer answered — nothing was reviewed. '
  + '0 of 3 reviewers answered; failed: '
  + 'local/PlanCritique: exit 69: [coai-mcp] the local engine at http://127.0.0.1:11434/v1 did not finish in time - it was still working after 290s of the 290s this reviewer was given. The engine is up; it is slower than the deadline, o, '
  + 'gemini/PlanCritique: exit 1: error: Eligibility check failed: UNAUTHENTICATED (code 401): Request had invalid authentication credentials. Expected OAuth 2 access token, login cookie or othe…, '
  + 'codex/PlanCritique: rate limited (after 1 attempt): {"type":"error","message":"You’ve hit your usage limit. Visit https://chatgpt.com/codex/settings/usage to purchase more credits or try again at Oct 3rd, 2026 7:…. '
  + 'Proceed anyway, or fix the findings and review again?';

/** The text of each element with `class`, in order, tags stripped. */
function texts(html: string, cls: string): readonly string[] {
  return [...html.matchAll(new RegExp(`<(\\w+) class="${cls}">([\\s\\S]*?)</\\1>`, 'g'))].map((found) => found[2]!.replace(/<[^>]+>/g, ''));
}

test('a failed round reads as the lead, one line per reviewer that failed, and the question on its own', () => {
  const html = questionHtml(FAILED_ROUND);

  assert.deepEqual(texts(html, 'lead'), ['The plan review gate needs your decision: no reviewer answered — nothing was reviewed. 0 of 3 reviewers answered.']);
  const failed = texts(html, 'failed');
  assert.equal(failed.length, 3, `each reviewer's failure is not a line of its own: ${html}`);
  assert.match(failed[0]!, /^local · PlanCritique — exit 69: \[coai-mcp\] the local engine/);
  assert.match(failed[1]!, /^gemini · PlanCritique — exit 1: error: Eligibility check failed: UNAUTHENTICATED/);
  assert.deepEqual(texts(html, 'ask'), ['Proceed anyway, or fix the findings and review again?']);
});

test('a vendor\'s raw error body is shown as its message, not as JSON', () => {
  const codex = texts(questionHtml(FAILED_ROUND), 'failed')[2]!;

  assert.match(codex, /^codex · PlanCritique — rate limited \(after 1 attempt\): You’ve hit your usage limit\./);
  assert.ok(!codex.includes('{"type"'), `the JSON is still there: ${codex}`);
});

test('any other question is drawn exactly as it was', () => {
  const plain = 'Round 2 of the code review found 3 gating findings. Proceed anyway?';

  assert.equal(questionHtml(plain), plain);
});

test('everything a reviewer said is escaped, in the lines as in the plain text', () => {
  const hostile = FAILED_ROUND.replace('exit 1: error:', 'exit 1: <img src=x onerror=alert(1)> error:');
  const html = questionHtml(hostile);

  assert.ok(!html.includes('<img'), 'a reviewer\'s words reached the card as markup');
  assert.ok(html.includes('&lt;img'));
  assert.ok(!questionHtml('<b>hi</b>').includes('<b>'));
});
