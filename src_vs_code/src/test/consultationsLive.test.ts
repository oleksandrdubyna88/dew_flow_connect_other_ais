import assert from 'node:assert/strict';
import { test } from 'node:test';
import { Consultation, consultationsBody, isLive, outcomeSaid, parseConsultation } from '../consultations';
import { consultationCost } from '../usage';
import { CONSULTATIONS_SHOWN, consultationsHtml } from '../roundsLog';
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

test('a turn without numbers cannot put NaN on the card', () => {
  // A record from a server that changed the shape, or a half-written file that still parses: the
  // card adds these up, and `NaN tokens` is the one number nobody can read.
  const parsed = parseConsultation(JSON.stringify({
    id: 'abc', status: 'open', maxTurns: 5,
    turns: [{ problem: 'p', advice: 'a' }, { problem: 'p', advice: 'a', seconds: 2, tokensIn: 10, tokensOut: 1 }],
  }));

  assert.equal(parsed?.turns.length, 1, 'a turn whose numbers are not numbers is not a turn');
  assert.match(consultationsBody([{ ...record({ status: 'open' }), turns: parsed!.turns }], NOW), /11 tokens/);
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

test('a full page says it is the newest N rather than letting the rest not exist', () => {
  const one = (id: string) => ({
    id, callerKind: 'claude', repoPath: 'D:/repo', branch: 'main', vendor: 'codex', model: '',
    turns: 1, status: 'closed', reason: '', startedUtc: '2026-09-13T09:00:00.000Z', endedUtc: '',
    seconds: 1, tokensIn: 0, tokensOut: 0, costUsd: null, problem: 'p', advice: 'a', alert: '',
  });
  const full = consultationsHtml({
    ...EMPTY_LOG,
    consultations: Array.from({ length: CONSULTATIONS_SHOWN }, (_, i) => one(`c${i}`)),
  });

  assert.match(full, new RegExp(`Showing the newest ${CONSULTATIONS_SHOWN}`));
  assert.ok(!consultationsHtml({ ...EMPTY_LOG, consultations: [one('c0')] }).includes('Showing the newest'));
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

// ---------------------------------------------------------------------------------------------
// How it ended, and what it cost (issue #309)

test('an outcome is shown when one was recorded, and the four are the only ones', () => {
  assert.equal(outcomeSaid('solved'), 'solved');
  assert.equal(outcomeSaid('not_solved'), 'not solved');
  assert.equal(outcomeSaid('abandoned'), 'abandoned');
  assert.equal(outcomeSaid('lapsed'), 'ran out');
});

test('an ABSENT outcome is a dash, and never a verdict', () => {
  // The rule the whole boundary table exists for. A record with no outcome is one nobody said
  // anything about — including every record written before the field existed. Reading it as a
  // verdict would turn a budget running out into "it worked". (codex, the plan round.)
  assert.equal(outcomeSaid(undefined), '—');
  assert.equal(outcomeSaid(''), '—');
  assert.equal(outcomeSaid('something a newer server writes'), '—',
    'a value this build does not know is not a verdict either');
});

test('the cost is the money when the vendor reported money', () => {
  assert.deepEqual(
    consultationCost(0.15, 40_402, 1_112, { in: 1.25, out: 10 }),
    // Four decimals below a dollar, deliberately: `money` rounds that way so a fraction of a cent
    // does not become '$0.00', which reads as free.
    { text: '$0.1500', estimated: false },
    'a real price is never marked as an estimate, even when a rate exists',
  );
});

test('the cost is a MARKED estimate when the vendor reported none but a rate is known', () => {
  // The reported symptom: 245.7k tokens beside a dash, because codex reports no money and the
  // server rightly refuses to invent a price. The panel already prices the reviewers' tokens and
  // the chat's this way; this column was the one that never asked. (issue #309.)
  const said = consultationCost(null, 200_000, 45_700, { in: 1.25, out: 10 });

  assert.equal(said.estimated, true);
  assert.match(said.text, /^~\$/, `an estimate must wear its tilde; it said "${said.text}"`);
});

test('the cost is still a dash when nothing knows a price', () => {
  assert.deepEqual(
    consultationCost(null, 200_000, 45_700, undefined),
    { text: '—', estimated: false },
    'no reported money and no rate is not zero, and it is not free',
  );
  assert.deepEqual(
    consultationCost(null, 0, 0, { in: 1.25, out: 10 }),
    { text: '—', estimated: false },
    'a rate over no tokens is not a price either',
  );
});
