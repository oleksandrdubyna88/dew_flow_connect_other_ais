import assert from 'node:assert/strict';
import { test } from 'node:test';
import { Consultation, consultationsBody, isLive, parseConsultation } from '../consultations';
import { consultationsHtml } from '../roundsLog';
import { EMPTY_LOG, parseLog } from '../roundsDb';

/**
 * What the sidebar says while a consultation is running, and what the log says once it is over.
 *
 * <p>The two are deliberately different surfaces: the sidebar is present tense — the 2026-09-05
 * ruling that took finished rounds out of it — and the log is the history. A consultation that is
 * closed or failed must therefore be absent from one and present in the other.</p>
 */

const NOW = Date.parse('2026-09-13T10:00:00.000Z');

function record(over: Partial<Consultation> = {}): Consultation {
  return {
    id: 'b8f1c2d3e4a5b6c7d8e9f0a1b2c3d4e5',
    callerKind: 'claude',
    repoPath: 'D:/rsd/repo',
    branch: 'feat/x',
    vendor: 'codex',
    model: 'gpt-5.6-luna',
    status: 'asking',
    startedUtc: '2026-09-13T09:58:00.000Z',
    updatedUtc: '2026-09-13T09:58:00.000Z',
    maxTurns: 5,
    turns: [],
    alert: '',
    reason: '',
    ...over,
  };
}

test('a file with an id and a status is a consultation; anything else is not', () => {
  const parsed = parseConsultation(JSON.stringify(record()));

  assert.equal(parsed?.id, 'b8f1c2d3e4a5b6c7d8e9f0a1b2c3d4e5');
  assert.equal(parsed?.vendor, 'codex');
  assert.equal(parseConsultation('{ not json'), undefined);
  assert.equal(parseConsultation('{"id":"x"}'), undefined, 'a record with no status says nothing about what it is doing');
});

test('a record from a server that wrote fewer fields still draws', () => {
  // The two halves update separately, so a card missing a number is better than a sidebar that
  // refuses to paint. The same decision `parseSession` made.
  const parsed = parseConsultation('{"id":"abc","status":"open"}');

  assert.equal(parsed?.vendor, '');
  assert.equal(parsed?.maxTurns, 0);
  assert.deepEqual(parsed?.turns, []);
});

test('only a consultation that is still going is live', () => {
  assert.equal(isLive(record({ status: 'asking' })), true);
  assert.equal(isLive(record({ status: 'open' })), true);
  assert.equal(isLive(record({ status: 'interrupted' })), true);
  assert.equal(isLive(record({ status: 'closed' })), false);
  assert.equal(isLive(record({ status: 'failed' })), false);
});

test('the card says who is asking whom, and which turn it is on', () => {
  const html = consultationsBody([record({ status: 'asking' })], NOW);

  assert.match(html, /Claude Code is asking codex · gpt-5\.6-luna/);
  assert.match(html, /turn 1 of 5/, 'the turn being asked counts, or the card reads as idle while a model runs');
  assert.match(html, /feat\/x/);
});

test('an answered consultation is on the turn it has used, not the one it is asking', () => {
  const html = consultationsBody(
    [record({ status: 'open', turns: [{ utc: '', problem: 'p', advice: 'a', seconds: 24.7, tokensIn: 31_402, tokensOut: 812, costUsd: null }] })],
    NOW,
  );

  assert.match(html, /turn 1 of 5/);
  assert.match(html, /32\.2k tokens/);
});

test('`interrupted` reads as resumable, because that is what it is', () => {
  // The vendor accepted the turn and the process died before the answer was read; the conversation
  // resumes and the turn was not counted. Red would send somebody hunting a fault that is not there.
  const html = consultationsBody([record({ status: 'interrupted' })], NOW);

  assert.match(html, /resumable/);
  assert.ok(!html.includes('failed'));
});

test('the sidebar says where the finished ones are rather than going blank', () => {
  assert.match(consultationsBody([], NOW), /Nobody is consulting anybody/);
  assert.match(consultationsBody([record({ status: 'closed' })], NOW), /Show review rounds/);
});

test('the invariant’s alert is on the card, because it is the one line a person must read', () => {
  const html = consultationsBody([record({ alert: '2 files changed under D:/rsd/repo during this consultation' })], NOW);

  assert.match(html, /2 files changed/);
});

test('a repository path is escaped like every other value that came off a disk', () => {
  const html = consultationsBody([record({ branch: '<script>x</script>' })], NOW);

  assert.ok(!html.includes('<script>x</script>'));
});

// ---------------------------------------------------------------------------------------------
// The log page's side of it

test('the log lists a consultation with what was stuck and what was advised', () => {
  const html = consultationsHtml({
    ...EMPTY_LOG,
    consultations: [{
      id: 'abc', callerKind: 'codex', repoPath: 'D:/rsd/repo', branch: 'main', vendor: 'claude',
      model: 'opus', turns: 2, status: 'closed', reason: 'all 2 of its turns are used',
      startedUtc: '2026-09-13T09:00:00.000Z', endedUtc: '2026-09-13T09:04:00.000Z',
      seconds: 36.8, tokensIn: 40_402, tokensOut: 1_112, costUsd: 0.15,
      problem: 'The parser returns 3 where 4 is expected.', advice: 'Your loop stops one short.',
      alert: '',
    }],
  });

  assert.match(html, /Codex → claude · opus/);
  assert.match(html, /The parser returns 3 where 4 is expected\./);
  assert.match(html, /Your loop stops one short\./);
  assert.match(html, /41\.5k/);
});

test('a consultation that ended normally is not painted as a failure', () => {
  const of = (status: string): string => consultationsHtml({
    ...EMPTY_LOG,
    consultations: [{
      id: 'abc', callerKind: 'claude', repoPath: 'D:/repo', branch: 'main', vendor: 'codex', model: '',
      turns: 1, status, reason: '', startedUtc: '2026-09-13T09:00:00.000Z', endedUtc: '',
      seconds: 1, tokensIn: 0, tokensOut: 0, costUsd: null, problem: 'p', advice: 'a', alert: '',
    }],
  });

  assert.match(of('closed'), /badge done/);
  assert.match(of('failed'), /badge interrupted/, 'the red one is reserved for what actually went wrong');
  assert.match(of('interrupted'), /badge awaiting/);
});

test('an empty list says how a consultation happens at all', () => {
  assert.match(consultationsHtml(EMPTY_LOG), /No consultations yet/);
  assert.match(consultationsHtml(EMPTY_LOG), /Consultant/);
});

test('a server too old to have the table answers a log with no consultations, never an error', () => {
  // Both directions of this seam are ordinary: the two halves of this product update separately.
  const log = parseLog(JSON.stringify({ rounds: [], blindSpots: [], defended: [], totals: {} }), true);

  assert.deepEqual(log.consultations, []);
  assert.equal(log.read, true, 'an absent list is an empty one, not a failed read');
});
