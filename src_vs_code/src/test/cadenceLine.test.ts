import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CadenceAnswer, cadenceLinesHtml, cadenceSaid, cadenceWanted, parseCadence } from '../cadenceLine';
import type { SessionFile } from '../rounds';

/**
 * The line the sidebar draws for a plan's consultation cadence (todo/PLAN_consult_on_a_cadence.md, epic 4
 * story 4.2): the operator's "epics closed 4/14 · consultation for triple 4-6: due".
 *
 * <p>Everything here is read from the answer `coai-mcp --cadence` gives — the extension does not work
 * the cadence out itself (the risk consultation for story 4.2, point 4).</p>
 */

const answer = (over: Partial<CadenceAnswer> = {}): CadenceAnswer => ({
  plan: 'todo/PLAN_x.md',
  mode: 'remind',
  epics: 14,
  epicsClosed: [1, 2, 3, 4],
  groups: [
    { range: '1-3', consulted: true },
    { range: '4-6', consulted: false },
    { range: '7-9', consulted: false },
    { range: '10-12', consulted: false },
    { range: '13-14', consulted: false },
  ],
  risk: [],
  riskAnswered: false,
  unreadable: '',
  ...over,
});

test('the line says how many epics are closed and whether the current group has its consultation', () => {
  assert.equal(cadenceSaid(answer()), 'PLAN_x.md · epics closed 4/14 · consultation for epics 4-6: due');
});

test('a consulted current group says so', () => {
  const groups = answer().groups.map((g) => (g.range === '4-6' ? { ...g, consulted: true } : g));

  assert.equal(cadenceSaid(answer({ groups })), 'PLAN_x.md · epics closed 4/14 · consultation for epics 4-6: taken');
});

test('in require, a due consultation says the code round waits for it', () => {
  assert.match(cadenceSaid(answer({ mode: 'require' })), /epics 4-6: due — the code round waits for it$/);
});

test('the current group is the one holding the first epic not yet closed, whatever order they closed in', () => {
  assert.match(cadenceSaid(answer({ epicsClosed: [1, 2, 4, 5] })), /epics closed 4\/14 · consultation for epics 1-3: taken$/);
});

test('a group of one epic is named as one', () => {
  assert.match(cadenceSaid(answer({ epics: 7, epicsClosed: [1, 2, 3, 4, 5, 6], groups: [{ range: '1-3', consulted: true }, { range: '4-6', consulted: true }, { range: '7', consulted: false }] })),
    /consultation for epic 7: due$/);
});

test('a plan that continues from epic seven counts from seven', () => {
  const said = cadenceSaid(answer({ epics: 3, epicsClosed: [7], groups: [{ range: '7-9', consulted: true }] }));

  assert.equal(said, 'PLAN_x.md · epics closed 1/3 · consultation for epics 7-9: taken');
});

test('a plan whose every epic is closed says that and nothing about a group', () => {
  assert.equal(
    cadenceSaid(answer({ epics: 3, epicsClosed: [1, 2, 3], groups: [{ range: '1-3', consulted: true }] })),
    'PLAN_x.md · every epic closed (3/3)');
});

test('the risky pieces are counted when any were named', () => {
  const risk = [{ key: '7', reason: 'payments', consulted: true }, { key: '9/9.2', reason: 'migration', consulted: false }];

  assert.match(cadenceSaid(answer({ risk })), / · risky pieces consulted 1\/2$/);
});

test('an unreadable record is said, never drawn as nothing done', () => {
  const said = cadenceSaid(answer({ epicsClosed: [], unreadable: 'the cadence record … could not be read (torn)' }));

  assert.equal(said, 'PLAN_x.md · the cadence record could not be read: the cadence record … could not be read (torn)');
});

test('a plan with no epics counted yet says so rather than 0/0', () => {
  assert.equal(cadenceSaid(answer({ epics: 0, epicsClosed: [], groups: [] })), 'PLAN_x.md · no epics counted yet');
});

test('the lines are escaped, carry where they are from, and a due one is marked', () => {
  const html = cadenceLinesHtml([{ repoPath: 'D:/rsd/<repo>', branch: 'feat/x', answer: answer({ plan: 'todo/<b>.md' }) }]);

  assert.ok(!html.includes('<b>'), 'a plan name came off a disk');
  assert.ok(!html.includes('<repo>'));
  assert.match(html, /class="stale"/);
  assert.match(html, /feat\/x/);
});

test('the branch is set apart from the sentence, labelled, not run on as its last word', () => {
  const html = cadenceLinesHtml([{ repoPath: 'D:/repo', branch: 'epic-1', answer: answer() }]);
  const text = html.replace(/<[^>]*>/g, '').trim();

  assert.ok(text.endsWith(`${cadenceSaid(answer())} · branch epic-1`), text);
});

test('no lines draw nothing at all', () => {
  assert.equal(cadenceLinesHtml([]), '');
});

test('the answer is parsed from what the server prints, and `null` means nothing to draw', () => {
  const parsed = parseCadence(JSON.stringify(answer()));

  assert.equal(parsed?.epics, 14);
  assert.deepEqual(parsed?.epicsClosed, [1, 2, 3, 4]);
  assert.equal(parseCadence('null'), null);
});

test('a body that is not an answer is not one — and an older answer without `unreadable` still is', () => {
  assert.equal(parseCadence('{ torn'), undefined);
  assert.equal(parseCadence('{"plan":7}'), undefined);
  const { unreadable: _, ...older } = answer();
  assert.equal(parseCadence(JSON.stringify(older))?.unreadable, '');
});

test('a group or a risk item of the wrong shape is dropped rather than drawn as something', () => {
  const parsed = parseCadence(JSON.stringify({ ...answer(), groups: [{ range: 4 }, { range: '4-6', consulted: true }], risk: ['x'] }));

  assert.deepEqual(parsed?.groups, [{ range: '4-6', consulted: true }]);
  assert.deepEqual(parsed?.risk, []);
});

const session = (over: Partial<SessionFile> = {}): SessionFile => ({
  state: { sessionId: 's', repoPath: 'D:/repo', branch: 'feat/x', stage: 'CodeReview', awaitingResolve: false },
  rounds: [{ stage: 'PlanReview', number: 1, verdict: 'proceed', gatingCount: 0, reviewers: '', completedUtc: '2026-09-25T10:00:00Z' }],
  plan: 'todo/PLAN_x.md',
  ...over,
});

const NOW = Date.parse('2026-09-25T12:00:00Z');

test('only a session touched in the last day, holding a plan, on a named repo and branch is probed', () => {
  assert.equal(cadenceWanted(session(), NOW), true);
  assert.equal(cadenceWanted(session({ plan: '' }), NOW), false, 'no plan, nothing to count');
  const { plan: _, ...older } = session();
  assert.equal(cadenceWanted(older, NOW), false, 'a file from before the field');
  assert.equal(cadenceWanted(session({ rounds: [] }), NOW), false, 'nothing ever ran');
  assert.equal(
    cadenceWanted(session({ rounds: [{ stage: 'PlanReview', number: 1, verdict: 'proceed', gatingCount: 0, reviewers: '', completedUtc: '2026-09-24T11:00:00Z' }] }), NOW),
    false,
    'older than a day');
  assert.equal(cadenceWanted(session({ state: { ...session().state, branch: '' } }), NOW), false);
  assert.equal(cadenceWanted(session({ state: { ...session().state, repoPath: '' } }), NOW), false);
});

test('a round still running counts by when it started', () => {
  const running = session({ rounds: [{ stage: 'CodeReview', number: 1, verdict: 'running', gatingCount: 0, reviewers: '', completedUtc: '', startedUtc: '2026-09-25T11:59:00Z' }] });

  assert.equal(cadenceWanted(running, NOW), true);
});

// The code round (codex): a round dated in the future made a session eligible for ever; a round a
// minute ahead is a skewed clock between two sides of one machine, and still counts.
test('a session dated in the future is not probed for ever — a clock a few minutes out still counts', () => {
  const at = (instant: string): SessionFile => session({ rounds: [{ stage: 'PlanReview', number: 1, verdict: 'proceed', gatingCount: 0, reviewers: '', completedUtc: instant }] });

  assert.equal(cadenceWanted(at('2026-09-27T12:00:00Z'), NOW), false, 'two days ahead is a broken stamp, not a recent session');
  assert.equal(cadenceWanted(at('2026-09-25T12:03:00Z'), NOW), true, 'three minutes ahead is a clock between two sides');
});

// The code round (codex): the latest instant was found by spreading every one into Math.max, which
// throws past the engine's argument limit on a long enough history.
test('a session with a very long history is still judged, not thrown on', () => {
  const rounds = Array.from({ length: 300_000 }, () => ({ stage: 'CodeReview', number: 1, verdict: 'proceed', gatingCount: 0, reviewers: '', completedUtc: '2026-09-25T11:00:00Z' }));

  assert.equal(cadenceWanted(session({ rounds }), NOW), true);
});
