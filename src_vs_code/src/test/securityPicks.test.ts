import assert from 'node:assert/strict';
import { test } from 'node:test';
import { catalogBody, catalogHtml } from '../catalogPage';
import { rowsFor, securityRowsOffered } from '../catalogPicks';
import { FEATURES } from '../binaryFeatures';
import { DEFAULT_SECURITY, type SecurityLane } from '../securityLane';
import { SECURITY_SAMPLE_MAX, securityTryAnswer, securityTryRefusal, securityTryRequest } from '../securityTry';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS, type Vendor } from '../vendors';
import { type PanelState } from '../panelView';
import { panelState } from './panelPageHarness';
import { Node, runPageHtml } from './rolesPageHarness';

/**
 * E4.2 of todo/PLAN_one_model_catalog.md, the security half: on the new page the lane's pairs are made from the rows
 * ticked Security lane, a pair whose row is not ticked is kept and named (D3), and "Try it" asks the installed binary —
 * `coai-mcp --check-security`, on stdin — what the lane makes of a sample: never a JavaScript copy of the matcher.
 */

const guard: Vendor = { ...DEFAULT_VENDORS[1]!, id: 'sec-guard', plan: false, code: false, uses: ['security'] };
const ROWS: readonly Vendor[] = [...DEFAULT_VENDORS, guard];
const reviewer = DEFAULT_VENDORS[0]!.id;

function stateWith(lane: SecurityLane): PanelState {
  return { ...panelState('securityLane', { settings: { ...DEFAULTS, securityLane: lane } }), catalogRows: ROWS };
}

/** The security pane of the new page, cut at the next tab panel. */
function securityPane(state: PanelState): string {
  const body = catalogBody(state);
  const start = body.indexOf('id="cpane-security"');
  const next = body.indexOf('role="tabpanel"', body.indexOf('>', start));

  return body.slice(start, next < 0 ? body.length : next);
}

test('a pair\'s reviewer is picked from the rows ticked Security lane, on the new page', () => {
  const pane = securityPane(stateWith({ ...DEFAULT_SECURITY, runs: [{ vendor: 'sec-guard', prompt: 'redteam-authz' }] }));
  const reviewers = /<select data-setting="securityLane" data-security-field="run:0:vendor">(.*?)<\/select>/s.exec(pane)?.[1] ?? '';

  assert.match(reviewers, /value="sec-guard"/);
  assert.doesNotMatch(reviewers, new RegExp(`value="${reviewer}"`), 'a row not ticked Security lane is offered');
});

test('a pair whose row is not ticked is kept and named — never cleared (D3)', () => {
  const pane = securityPane(stateWith({ ...DEFAULT_SECURITY, runs: [{ vendor: reviewer, prompt: 'redteam-authz' }] }));

  assert.match(pane, new RegExp(`data-security-field="run:0:vendor">.*value="${reviewer}" selected`, 's'));
  assert.match(pane, new RegExp(`${reviewer} is not ticked for the security lane on Models`));
});

test('the note about an ordinary reviewer reads every row, not only the ticked ones', () => {
  assert.doesNotMatch(securityPane(stateWith(DEFAULT_SECURITY)), /Enable an ordinary code or feature reviewer too/);
});

test('the host offers the same rows the page does', () => {
  assert.deepEqual(rowsFor('security', ROWS).map((row) => row.id), ['sec-guard']);
  assert.deepEqual(securityRowsOffered(ROWS, true).map((row) => row.id), ['sec-guard'], 'a write from the new page names a row it never offered');
  assert.deepEqual(securityRowsOffered(ROWS, false), ROWS, 'the current page offers every row, as it always did');
});

test('"Try it" sends the sample and the lane a round would send, on stdin', () => {
  const lane: SecurityLane = { ...DEFAULT_SECURITY, prompts: [{ id: 'redteam-authz', triggers: [], focus: [], words: ['tenant'] }] } as unknown as SecurityLane;

  const sent = JSON.parse(securityTryRequest('SELECT * FROM t', lane, [FEATURES.checkSecurity])) as { text: string; lane: { prompts: { words?: unknown }[] } };
  assert.equal(sent.text, 'SELECT * FROM t');
  assert.equal(sent.lane.prompts[0]!.words, undefined, 'words went to a binary that does not list securityWords');

  const withWords = JSON.parse(securityTryRequest('x', lane, [FEATURES.checkSecurity, FEATURES.securityWords])) as { lane: { prompts: { words?: unknown }[] } };
  assert.deepEqual(withWords.lane.prompts[0]!.words, ['tenant']);
});

test('"Try it" is refused before a spawn when the binary cannot answer it, or the sample is too long', () => {
  assert.match(securityTryRefusal('x', []), /update coai-mcp/i);
  assert.match(securityTryRefusal('x'.repeat(SECURITY_SAMPLE_MAX + 1), [FEATURES.checkSecurity]), /too long/);
  assert.equal(securityTryRefusal('x', [FEATURES.checkSecurity]), '');
});

test('what the binary answered is read as it said it, and a failure is named', () => {
  const answer = securityTryAnswer('s', 0, '{"signals":["sql"],"cards":["redteam-injection"],"refused":[{"signal":"own:x","pattern":"/(?=a)/","why":"lookaround"}],"complaints":["c"],"detectionIncomplete":true}');
  assert.deepEqual(answer, {
    sample: 's', signals: ['sql'], cards: ['redteam-injection'], refused: [{ signal: 'own:x', pattern: '/(?=a)/', why: 'lookaround' }],
    complaints: ['c'], incomplete: true, failure: '',
  });
  assert.match(securityTryAnswer('s', 65, '').failure, /refused/);
  assert.match(securityTryAnswer('s', -1, '').failure, /did not answer/);
  assert.match(securityTryAnswer('s', 0, 'not json').failure, /could not be read/);
});

test('the answer is drawn on the security tab — every part of it', () => {
  const state: PanelState = {
    ...stateWith(DEFAULT_SECURITY),
    securityTry: { sample: 'SELECT 1', signals: ['sql'], cards: ['redteam-injection'], refused: [{ signal: 'own:x', pattern: '/(?=a)/', why: 'lookaround' }], complaints: ['a complaint'], incomplete: true, failure: '' },
  };
  const pane = securityPane(state);

  assert.match(pane, /<textarea id="security-sample"[^>]*>SELECT 1<\/textarea>/);
  for (const part of ['sql', 'redteam-injection', 'lookaround', 'a complaint', 'did not finish']) {
    assert.ok(pane.includes(part), `the answer's ${part} is not drawn`);
  }
});

test('pressing Try it posts the sample from the box', () => {
  const box = new Node({}, 'TEXTAREA');
  box.value = 'password = "hunter2"';
  const press = new Node({ securityTry: '' }, 'BUTTON');
  const page = runPageHtml(catalogHtml(stateWith(DEFAULT_SECURITY), 'test-nonce', 'security'), { '#security-sample': [box], '[data-security-try]': [press] });

  page.fire('click', press);

  const posted = page.posted.filter((one) => one['type'] === 'command').map(({ command, id }) => ({ command, id }));
  assert.deepEqual(posted, [{ command: 'trySecurity', id: 'password = "hunter2"' }]);
});
