import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  CLOSE_CHOICES,
  Consultation,
  consultationsBody,
  isLive,
  outcomeSaid,
  parseConsultation,
} from '../consultations';
import { consultationCost } from '../usage';
import { CONSULTATIONS_SHOWN, consultationsHtml } from '../roundsLog';
import { EMPTY_LOG, parseLog } from '../roundsDb';
import type { DbLog } from '../roundsDb';
import type { PriceLookup } from '../usage';
import type { Vendor } from '../vendors';

/**
 * The table as the PRODUCT builds it, with nothing that knows a price.
 *
 * <p>A helper rather than three bare arguments at every call: what these cases are about is the
 * rows, and the rates only matter to the two that say so below.</p>
 */
const priced = (log: DbLog, vendors: readonly Vendor[] = [], listed: PriceLookup = () => undefined): string =>
  consultationsHtml(log, vendors, listed);

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
    kind: 'stuck',
    plan: '',
    epics: '',
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
  const html = priced({
    ...EMPTY_LOG,
    consultations: [{
      id: 'abc', callerKind: 'codex', repoPath: 'D:/rsd/repo', branch: 'main', vendor: 'claude',
      model: 'opus', turns: 2, status: 'closed', reason: 'all 2 of its turns are used',
      startedUtc: '2026-09-13T09:00:00.000Z', endedUtc: '2026-09-13T09:04:00.000Z',
      seconds: 36.8, tokensIn: 40_402, tokensOut: 1_112, costUsd: 0.15,
      problem: 'The parser returns 3 where 4 is expected.', advice: 'Your loop stops one short.',
      alert: '', kind: 'stuck', plan: '', epics: '',
    }],
  });

  assert.match(html, /Codex → claude · opus/);
  assert.match(html, /The parser returns 3 where 4 is expected\./);
  assert.match(html, /Your loop stops one short\./);
  assert.match(html, /41\.5k/);
});

test('a consultation that ended normally is not painted as a failure', () => {
  const of = (status: string): string => priced({
    ...EMPTY_LOG,
    consultations: [{
      id: 'abc', callerKind: 'claude', repoPath: 'D:/repo', branch: 'main', vendor: 'codex', model: '',
      turns: 1, status, reason: '', startedUtc: '2026-09-13T09:00:00.000Z', endedUtc: '',
      seconds: 1, tokensIn: 0, tokensOut: 0, costUsd: null, problem: 'p', advice: 'a', alert: '',
      kind: 'stuck', plan: '', epics: '',
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
    kind: 'stuck', plan: '', epics: '',
  });
  const full = priced({
    ...EMPTY_LOG,
    consultations: Array.from({ length: CONSULTATIONS_SHOWN }, (_, i) => one(`c${i}`)),
  });

  assert.match(full, new RegExp(`Showing the newest ${CONSULTATIONS_SHOWN}`));
  assert.ok(!priced({ ...EMPTY_LOG, consultations: [one('c0')] }).includes('Showing the newest'));
});

test('an empty list says how a consultation happens at all', () => {
  assert.match(priced(EMPTY_LOG), /No consultations yet/);
  assert.match(priced(EMPTY_LOG), /Consultant/);
});

test('a server too old to have the table answers a log with no consultations, never an error', () => {
  // Both directions of this seam are ordinary: the two halves of this product update separately.
  const log = parseLog(JSON.stringify({ rounds: [], blindSpots: [], defended: [], totals: {} }), true);

  assert.deepEqual(log.consultations, []);
  assert.equal(log.read, true, 'an absent list is an empty one, not a failed read');
});

test('a consultation the server wrote with NO price draws a dash, not a page that stops updating', () => {
  // THE SHAPE ON THE WIRE, not the one the type declares. The server writes `--log` through
  // `ServerJsonContext`, whose `WhenWritingNull` OMITS a null `CostUsd` — so a consultation nobody
  // priced (a local model, a subscription CLI) arrives with no `costUsd` key at all, and `undefined`
  // slipped past `costUsd !== null` into `toFixed`. That throw sat in `refreshRoundsLog` before the
  // push, so from the first unpriced consultation on, the date switch and *What it keeps missing*
  // stopped answering. Every case above builds the row by hand with `costUsd: null`, which is why
  // none of them saw it. (2026-09-23, 64 TypeErrors in one day's extension-host log.)
  const wire = JSON.stringify({
    rounds: [], blindSpots: [], defended: [], totals: {},
    consultations: [{
      id: 'abc', callerKind: 'claude', repoPath: 'D:/repo', branch: 'main', vendor: 'local',
      model: 'qwen3', turns: 1, status: 'closed', reason: '', outcome: '', outcomeBy: '',
      startedUtc: '2026-09-23T09:00:00.000Z', endedUtc: '2026-09-23T09:01:00.000Z', seconds: 60,
      tokensIn: 1_200, tokensOut: 300, problem: 'p', advice: 'a', alert: '',
      kind: 'stuck', plan: '', epics: '',
    }],
  });
  const log = parseLog(wire, true);
  // Drawn FIRST, because the throw is the symptom; the field check below is the cause.
  const html = priced(log);

  assert.match(html, /<td class="num cost">—<\/td>/, 'an unpriced consultation shows the dash');
  assert.equal(log.consultations[0]?.costUsd, null, 'an absent price is no price, the same as null');
});

test('a consultation missing fields an older server never wrote still draws', () => {
  // The same boundary rule `round()` already applies to rounds: believed only as far as its shape.
  // A number that is not a number must not reach `shortNumber` or `toFixed` either.
  const log = parseLog(JSON.stringify({ consultations: [{ id: 'abc', tokensIn: 'many', costUsd: 'free' }] }), true);
  const one = log.consultations[0];

  assert.equal(one?.tokensIn, 0);
  assert.equal(one?.costUsd, null);
  assert.equal(one?.vendor, '');
  assert.doesNotThrow(() => priced(log));
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

// ---------------------------------------------------------------------------------------------
// Closing one by hand (issue #309)

test('the person is offered the three verdicts, and never the server\'s own word', () => {
  // `lapsed` says the clock ran out. Offering it to a person would be asking them to state somebody
  // else's fact, and the server refuses it from this door anyway.
  assert.deepEqual(CLOSE_CHOICES.map((one) => one.outcome), ['solved', 'not_solved', 'abandoned']);
  assert.ok(CLOSE_CHOICES.every((one) => one.label.length > 0 && one.detail.length > 0),
    'a picker of bare words leaves a person choosing between them with nothing to choose ON');
});

test('the SIDEBAR carries no close control — it is present tense', () => {
  // Built on the card first, and moved. Two reasons, and the second is the deciding one: a control
  // about how something ENDED does not belong in a section that shows only what is happening now
  // (the 2026-09-05 ruling), and the card shows only LIVE consultations — so the moment one lapses
  // it leaves the sidebar and takes the control with it, which is exactly the state the issue
  // describes. The log lists every consultation, so that is where it went. (Operator, 2026-09-17.)
  assert.doesNotMatch(consultationsBody([record({ status: 'open' })], NOW), /closeConsultation/);
  assert.doesNotMatch(consultationsBody([record({ status: 'asking' })], NOW), /closeConsultation/);
});

test('the LOG offers it for a consultation nobody decided, and withholds it once somebody has', () => {
  const row = {
    id: 'abc', callerKind: 'codex', repoPath: 'D:/rsd/repo', branch: 'main', vendor: 'codex',
    model: 'gpt-6', turns: 1, status: 'closed', reason: 'idle', outcome: '',
    startedUtc: '2026-09-15T22:30:59.000Z', endedUtc: '2026-09-15T22:40:00.000Z',
    seconds: 36.8, tokensIn: 100, tokensOut: 20, costUsd: null,
    problem: 'stuck', advice: 'try this', alert: '',
    kind: 'stuck', plan: '', epics: '',
  };
  const table = (outcome: string, status = 'closed'): string =>
    priced({ ...EMPTY_LOG, consultations: [{ ...row, outcome, status }] });

  assert.match(table(''), /data-command="closeConsultation"/, 'a record nobody spoke about');
  assert.match(table('lapsed'), /data-command="closeConsultation"/,
    'and one the CLOCK ended — `lapsed` is not a verdict, so it can still be answered');
  assert.match(table(''), /data-id="abc"/, 'a control that does not name its consultation would close another');

  assert.doesNotMatch(table('solved'), /closeConsultation/, 'a verdict is not rewritten');
  assert.doesNotMatch(table('', 'failed'), /closeConsultation/,
    'a failed consultation produced no advice, so there is nothing to have a verdict about');
});

test('the TABLE prices a consultation whose vendor reported no money', () => {
  // The assertion the first version of this change did not have, and its absence is why the product
  // went on showing a dash while every unit test passed: `consultationCost` was right and nothing
  // called it with a rate. This goes through `consultationsHtml` exactly as the panel does.
  const row = {
    id: 'abc', callerKind: 'codex', repoPath: 'D:/rsd/repo', branch: 'main', vendor: 'codex',
    model: 'gpt-6-astra', turns: 1, status: 'open', reason: '', outcome: '',
    startedUtc: '2026-09-15T22:30:59.000Z', endedUtc: '',
    seconds: 36.8, tokensIn: 200_000, tokensOut: 45_700, costUsd: null,
    problem: 'which is better, C# or Java?', advice: 'it depends on the team.', alert: '',
    kind: 'stuck', plan: '', epics: '',
  };
  const vendors = [{
    id: 'codex', runtime: 'codex' as const, model: 'gpt-6-astra', baseUrl: '', executablePath: '',
    enabled: true, plan: true, code: true, pricePerMillionIn: 1.25, pricePerMillionOut: 10,
  }];

  const withRates = priced({ ...EMPTY_LOG, consultations: [row] }, vendors);
  assert.match(withRates, /~\$/, 'the reported symptom: 245.7k tokens beside a dash');
  assert.match(withRates, /class="num cost est"/, 'and an estimate wears the class that says so');

  // With nothing that knows a price it is still a dash, which is the honest answer rather than zero.
  assert.doesNotMatch(priced({ ...EMPTY_LOG, consultations: [row] }), /~\$/);
});

// ---------------------------------------------------------------------------------------------
// What a consultation was FOR (research/PLAN_consult_on_a_cadence.md, epic 4 story 4.3)
// ---------------------------------------------------------------------------------------------

const asked = {
  id: 'abc', callerKind: 'claude', repoPath: 'D:/rsd/repo', branch: 'main', vendor: 'codex',
  model: '', turns: 1, status: 'closed', reason: '', startedUtc: '2026-09-25T09:00:00.000Z',
  endedUtc: '', seconds: 1, tokensIn: 1, tokensOut: 1, costUsd: null, outcome: 'solved',
  problem: 'p', advice: 'a', alert: '', kind: 'stuck', plan: '', epics: '',
};

test('the kind has a column of its own, and For says what an ordered one covered', () => {
  const html = priced({
    ...EMPTY_LOG,
    consultations: [
      { ...asked, id: 's' },
      { ...asked, id: 'c', kind: 'cadence', plan: 'todo/PLAN_x.md', epics: '4-6' },
      { ...asked, id: 'r', kind: 'risk', plan: 'todo/PLAN_x.md', epics: '7/7.2' },
      { ...asked, id: 'e', kind: 'risk', plan: 'todo/PLAN_x.md', epics: '7' },
    ],
  });
  const plan = '<br><span class="decided" title="todo/PLAN_x.md">PLAN_x.md</span>';

  assert.match(html, /<th>Kind<\/th><th>For<\/th>/);
  assert.ok(html.includes('<td>stuck</td><td>—</td>'), 'a stuck consultation covered nothing but the problem it names');
  assert.ok(html.includes(`<td>cadence</td><td>epics 4-6${plan}</td>`), html);
  assert.ok(html.includes(`<td>risk</td><td>story 7.2${plan}</td>`), html);
  assert.ok(html.includes(`<td>risk</td><td>epic 7${plan}</td>`), html);
});

test('the sidebar card says the kind of a running consultation, and a record from before the kinds reads stuck', () => {
  const cadence = parseConsultation(JSON.stringify({ ...record(), kind: 'cadence', plan: 'todo/PLAN_x.md', epics: '1-3' }))!;
  const risk = parseConsultation(JSON.stringify({ ...record({ id: 'r1' }), kind: 'risk', plan: 'todo/PLAN_x.md', epics: '5/5.1' }))!;
  const stuck = parseConsultation(JSON.stringify({ ...record({ id: 's1' }), kind: 'stuck' }))!;
  // Written by a server from before the kinds: no kind, plan or epics on the file at all.
  const written = JSON.parse(JSON.stringify(record({ id: 'l1' }))) as Record<string, unknown>;
  for (const field of ['kind', 'plan', 'epics']) {
    delete written[field];
  }
  const legacy = parseConsultation(JSON.stringify(written))!;

  assert.match(consultationsBody([cadence], NOW), /cadence · epics 1-3 · PLAN_x\.md/);
  assert.match(consultationsBody([risk], NOW), /risk · story 5\.1 · PLAN_x\.md/);
  assert.match(consultationsBody([stuck], NOW), /<div class="line kind">stuck<\/div>/);
  assert.match(consultationsBody([legacy], NOW), /<div class="line kind">stuck<\/div>/);
});

test('a consultation from a server that predates the kinds reads as stuck, because that is all there was', () => {
  const one = parseLog(JSON.stringify({ consultations: [{ id: 'abc' }] }), true).consultations[0];

  assert.equal(one?.kind, 'stuck');
  assert.equal(one?.plan, '');
  assert.equal(one?.epics, '');
});

test('a kind this build does not know is shown as written, not dressed up as one it does', () => {
  const html = priced({ ...EMPTY_LOG, consultations: [{ ...asked, kind: 'a-newer-kind', epics: '<b>' }] });

  assert.match(html, />a-newer-kind</);
  assert.ok(!html.includes('<b>'), 'the epics came off a disk and are escaped like every other value');
});

test('an alert under a consultation spans every column the table has', () => {
  const html = priced({ ...EMPTY_LOG, consultations: [{ ...asked, alert: 'two records claim one id' }] });
  const columns = (html.match(/<th[ >]/g) ?? []).length;

  assert.match(html, new RegExp(`colspan="${columns}"`));
});

// PR #556 (CodeRabbit): the test above sends an UNKNOWN kind, which returns before the epics are ever
// formatted — so it could not see an unescaped epics value. Through a kind this build formats:
test('the epics a known kind formats come off a disk and are escaped', () => {
  for (const kind of ['cadence', 'risk']) {
    // No `/` in the payload: the risk formatter splits the epics on it, and a closing tag would be cut.
    const html = priced({ ...EMPTY_LOG, consultations: [{ ...asked, kind, epics: '<img src=x onerror=alert(1)>', plan: 'todo/<i>x.md' }] });

    assert.ok(!html.includes('<img src=x'), `${kind}: the epics reached the table unescaped`);
    assert.ok(html.includes('&lt;img src=x onerror=alert(1)&gt;'), `${kind}: the epics are not shown at all`);
    assert.ok(!html.includes('<i>x.md'), `${kind}: the plan reached the table unescaped`);
  }
});
