import assert from 'node:assert/strict';
import { test } from 'node:test';

import { EMPTY_LOG, parseFindings, parseLog } from '../roundsDb';
import { blindSpotsHtml } from '../roundsLog';
import { LOG, SESSION, clickInRow, firstRenderedKey, runningPage } from './roundsLogPageHarness';

/**
 * A security finding's evidence, from the server's `--log` / `--findings` answer to the Review rounds
 * page. The key is `securityEvidence` because the server writes `LoggedFinding.SecurityEvidence`
 * through `ServerJsonContext`, which is camelCase and omits a null — so an ordinary finding arrives
 * without the key at all.
 *
 * <p>The evidence is text a reviewing MODEL wrote about an attack, so it is the most hostile input
 * this page renders: it is shown as data, never as markup.</p>
 */

/** What a model may write into a reproduction: markup that would run if it were not escaped. */
const HOSTILE = '</pre><script>alert(1)</script><img src=x onerror=alert(2)>';

const EVIDENCE = {
  reproduction: { preconditions: 'a signed-in viewer', steps: HOSTILE, expected: 'refused', actual: 'shown' },
  capReason: '',
  alsoSeenBy: [],
  attackEvidence: { trigger: 'GET /orders/7', mechanism: 'no owner check', consequence: 'reads another tenant' },
};

const finding = (ordinal: number, title: string, extra: Record<string, unknown> = {}) => ({
  ordinal, severity: 'Major', category: 'Security', file: 'src/Orders.cs', line: 12,
  title, why: 'because', fix: '', role: 'Security', isGating: true,
  providers: 'codex', resolution: 'accept', reason: '', reRaised: false, ...extra,
});

const SECURE = finding(1, 'the security finding', { securityEvidence: EVIDENCE });
const ORDINARY = finding(2, 'the ordinary finding');

test('a findings answer keeps a security finding\'s evidence, and an ordinary finding has none', () => {
  const parsed = parseFindings(JSON.stringify({ findings: [SECURE, ORDINARY] }));
  assert.ok(parsed, 'the answer was not read at all');

  assert.deepEqual(parsed[0]?.securityEvidence, EVIDENCE);
  assert.equal('securityEvidence' in parsed[1]!, false, 'an ordinary finding acquired an evidence key');
});

test('evidence that is not an object is not evidence, and the finding is still read', () => {
  const parsed = parseFindings(JSON.stringify({ findings: [finding(3, 'odd', { securityEvidence: 'trust me' })] }));

  assert.equal(parsed?.length, 1);
  assert.equal('securityEvidence' in parsed[0]!, false);
});

test('a log answer carries evidence on a round\'s findings and on a defended finding', () => {
  const log = parseLog(JSON.stringify({
    rounds: [{ ...LOG.rounds[0], findings: [SECURE, ORDINARY] }],
    defended: [SECURE, ORDINARY],
  }));

  assert.deepEqual(log.rounds[0]?.findings?.[0]?.securityEvidence, EVIDENCE);
  assert.equal('securityEvidence' in log.rounds[0]!.findings![1]!, false);
  assert.deepEqual(log.defended[0]?.securityEvidence, EVIDENCE);
  assert.equal('securityEvidence' in log.defended[1]!, false);
});

/** Each finding's own markup, keyed by its title: the slice an assertion about ONE finding may look at. */
function findingsByTitle(html: string): Map<string, string> {
  return new Map(html.split('<div class="finding ').slice(1)
    .map((block): [string, string] => [/<b>([^<]*)<\/b>/.exec(block)?.[1] ?? '', block]));
}

function assertEvidenceDisclosed(html: string): void {
  const blocks = findingsByTitle(html);
  const secure = blocks.get('the security finding');
  const ordinary = blocks.get('the ordinary finding');
  assert.ok(secure !== undefined && ordinary !== undefined, `the page did not draw both findings: ${[...blocks.keys()].join(', ')}`);

  assert.match(secure, /<details><summary>Security evidence \(unverified\)<\/summary><pre>/,
    'the security finding has no evidence disclosure');
  assert.match(secure, /no owner check/, 'the disclosure does not hold the evidence');
  assert.ok(!ordinary.includes('<details>'), 'an ordinary finding grew an evidence disclosure');

  // What the model wrote stayed text: no element the evidence named exists in the markup.
  assert.ok(!html.includes('<script>') && !html.includes('<img') && !html.includes('</pre><script'),
    'the evidence became markup');
  assert.ok(secure.includes('&lt;script&gt;alert(1)&lt;/script&gt;'), 'the evidence was dropped rather than shown escaped');
}

test('opening a round on the shipped page discloses a security finding\'s evidence as text, and nothing for an ordinary one', () => {
  const page = runningPage([SESSION], { ...LOG, rounds: [{ ...LOG.rounds[0], findings: [SECURE, ORDINARY], foundCount: 2 }] });
  const key = firstRenderedKey(page.seen['rows']?.innerHTML ?? '');

  page.click(clickInRow('tr[data-key]', 'data-key', key));

  const rows = page.seen['rows']?.innerHTML ?? '';
  assert.match(rows, /class="detail"/, 'the row did not open');
  assertEvidenceDisclosed(rows);
});

test('a defended finding on the blind-spots tab discloses its evidence as text, and nothing for an ordinary one', () => {
  const log = parseLog(JSON.stringify({ ...EMPTY_LOG, defended: [SECURE, ORDINARY] }));

  assertEvidenceDisclosed(blindSpotsHtml(log));
});
