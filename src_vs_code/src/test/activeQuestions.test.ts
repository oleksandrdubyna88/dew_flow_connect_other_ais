import assert from 'node:assert/strict';
import { test } from 'node:test';

import type { Escalation } from '../escalations';
import { isOpenEscalation, parseEscalation } from '../escalations';
import { liveRegions } from '../panelView';
import { type QuestionConsult, type QuestionConsultRow, HEARTBEAT_STALE_MS, isKept, nextStaleAt, parseQuestionConsult, shownAt } from '../questionConsults';
import { panelState, runPanel } from './panelPageHarness';
import { textOf } from './renderedText';

/**
 * **Active questions**, RUN (todo/PLAN_question_consultant.md, S4 acceptance 3): the sidebar page with its own
 * script, the region as it is drawn and as a live push replaces it — a consulting question one line per model,
 * advancing as rows settle; the card with every answer folded under it; an expired card gone; one card per
 * question, never two.
 */

const NOW = Date.parse('2026-10-02T12:10:00.000Z');

function row(rowId: string, overrides: Partial<QuestionConsultRow> = {}): QuestionConsultRow {
  return {
    rowId, vendor: 'claude', model: 'sonnet', runtime: 'claude', promptTitle: 'The best developer\'s opinion', capability: 'none',
    flag: '', status: 'consulting', reason: '', seconds: 0, advice: '', note: '', ...overrides,
  };
}

function record(overrides: Partial<QuestionConsult> = {}): QuestionConsult {
  return {
    id: 'q-1', repoPath: 'D:/repo', branch: 'feat/x', question: 'Which retry shape fits a flaky vendor?', status: 'consulting',
    outcome: '', escalationId: '', productionRisk: false, startedUtc: '2026-10-02T12:09:00.000Z', updatedUtc: '', endedUtc: '', heartbeatUtc: '',
    rows: [row('sonnet-disk'), row('astra-web', { vendor: 'codex', model: 'gpt-6-astra', flag: 'unconfined', promptTitle: 'The internet' })],
    alert: '', ...overrides,
  };
}

function card(overrides: Partial<Escalation> = {}): Escalation {
  return {
    id: 'esc-1', sessionId: 's', repoPath: 'D:/repo', branch: 'feat/x', question: 'Should the migration drop the column now?',
    openFindings: [], askedUtc: '2026-10-02T12:09:30.000Z', ...overrides,
  };
}

/** What a reader sees of escaped text. */
function decoded(text: string): string {
  return text.replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&amp;/g, '&');
}

/** The text of every element with `class`, in order. */
function texts(html: string, cls: string): readonly string[] {
  return [...html.matchAll(new RegExp(`<(\\w+) class="${cls}"[^>]*>([\\s\\S]*?)</\\1>`, 'g'))].map((found) => textOf(found[2]!));
}

test('a consulting question draws one line per model, and a push advances the line as that row settles', () => {
  const page = runPanel(panelState('', { qconsults: [record()] }));
  const before = texts(page.region('live-qconsults'), 'line').filter((line) => line.includes('—'));

  assert.equal(before.length, 2, `one line per model: ${page.region('live-qconsults')}`);
  assert.match(before[0]!, /^consulting claude · sonnet — The best developer's opinion/);
  assert.match(before[1]!, /can read this machine \(unconfined\)/, 'the flag is beside the flagged row');

  const settled = record({ rows: [row('sonnet-disk', { status: 'answered', seconds: 42, advice: 'Use a ladder.' }), record().rows[1]!] });
  page.deliver({ type: 'live', ...liveRegions(panelState('', { qconsults: [settled] }), NOW) });
  const after = texts(page.region('live-qconsults'), 'line').filter((line) => line.includes('—'));

  assert.match(after[0]!, /^answered claude · sonnet/, 'the settled row advanced');
  assert.match(after[0]!, /42 ?s/, 'and says what it took');
  assert.match(after[1]!, /^consulting codex · gpt-6-astra/, 'the one still running did not');
});

test('the card carries every consultant answer folded under it, the production risk and its reason', () => {
  const page = runPanel(panelState('', {
    questions: [card({
      productionRisk: true,
      riskReason: 'the migration drops a column nothing can restore',
      consultantAnswers: [
        { rowId: 'sonnet-disk', vendor: 'claude', model: 'sonnet', promptTitle: 'Projects on this disk', capability: 'disk', flag: '', status: 'answered', reason: '', advice: 'Back the column up first.' },
        { rowId: 'astra-web', vendor: 'codex', model: 'gpt-6-astra', promptTitle: 'The internet', capability: 'web', flag: 'unconfined', status: 'answered', reason: '', advice: 'Use an expand-contract migration.' },
      ],
    })],
  }));
  const region = page.region('live-qconsults');

  assert.deepEqual(texts(region, 'advice'), ['Back the column up first.', 'Use an expand-contract migration.']);
  assert.match(texts(region, 'qanswer')[1]!, /can read this machine \(unconfined\)/, 'the flag is shown on the answer from a flagged row (D13)');
  assert.match(textOf(region), /Production risk — the migration drops a column nothing can restore/);
  assert.ok(page.commands.some((one) => one.dataset['command'] === 'answer' && one.dataset['id'] === 'esc-1'), 'the card has its Answer button');
});

test('a card that followed a consultation folds THAT consultation\'s answers and names it — and the consultation is not drawn twice', () => {
  const followed = record({
    id: 'q-7', status: 'answered',
    rows: [row('sonnet-disk', { status: 'answered', advice: 'A ladder.' }), row('grok', { vendor: 'api', model: 'x-ai/grok-4.7', status: 'timed_out', reason: 'past its five minutes' })],
  });
  const running = record({ id: 'q-8', escalationId: 'esc-1', status: 'consulting' });
  const page = runPanel(panelState('', { questions: [card({ consultId: 'q-7' })], qconsults: [followed, running] }));
  const region = page.region('live-qconsults');

  assert.deepEqual(texts(region, 'advice'), ['A ladder.', 'past its five minutes', 'nothing yet', 'nothing yet'],
    'the followed consultation\'s rows, and the one running beside the card, are folded under it');
  assert.match(textOf(region), /Followed the consultation q-7/);
  assert.equal(texts(region, 'round qconsult').length, 0, 'a consultation bound to a card is drawn once — inside the card');
  assert.equal((region.match(/data-question=/g) ?? []).length, 1, 'one card per question');
});

test('an answer folded under a card says its row\'s note — a disk root nobody watched (S4b item 5), and a risk card\'s too', () => {
  const unwatched = 'root D:/notes is not a git checkout: changes there are not watched';
  const followed = record({ id: 'q-7', status: 'answered', rows: [row('sonnet-disk', { status: 'answered', advice: 'A ladder.', note: unwatched })] });
  const fromTheRecord = runPanel(panelState('', { questions: [card({ consultId: 'q-7' })], qconsults: [followed] })).region('live-qconsults');
  const beside = runPanel(panelState('', {
    questions: [card({
      productionRisk: true, riskReason: 'r',
      consultantAnswers: [{ rowId: 'sonnet-disk', vendor: 'claude', model: 'sonnet', promptTitle: 'Projects on this disk', capability: 'disk', flag: '', status: 'answered', reason: '', advice: 'A ladder.', note: unwatched }],
    })],
  })).region('live-qconsults');

  for (const region of [fromTheRecord, beside]) {
    assert.ok(texts(region, 'qanswer').some((one) => one.includes(unwatched)), `the fold says the root was not watched: ${region}`);
  }
});

test('an expired card leaves Active questions — the watcher keeps only open ones, and a push without it clears the region', () => {
  const expired = parseEscalation(JSON.stringify({ ...card(), status: 'expired', expiredUtc: '2026-10-02T12:25:00Z' }))!;
  assert.equal(isOpenEscalation(expired), false, 'the watcher would keep an expired card');

  const page = runPanel(panelState('', { questions: [card()] }));
  assert.ok(page.region('live-qconsults').includes('data-question="esc-1"'));
  page.deliver({ type: 'live', ...liveRegions(panelState('', { questions: [expired].filter(isOpenEscalation) }), NOW) });

  assert.equal(page.region('live-qconsults'), '', 'the expired card is still drawn');
});

test('every word a model or the server wrote is escaped, never markup', () => {
  const page = runPanel(panelState('', {
    questions: [card({ question: 'Is <b>this</b> fine?', consultantAnswers: [
      { rowId: 'r', vendor: '<img src=x onerror=1>', model: 'm', promptTitle: 'p', capability: 'none', flag: '', status: 'answered', reason: '', advice: '</div><script>alert(1)</script>' },
    ] })],
    qconsults: [record({ id: 'q-9', question: '<script>q</script>' })],
  }));
  const region = page.region('live-qconsults');

  assert.ok(!region.includes('<script>'), 'a model\'s advice reached the page as markup');
  assert.ok(!region.includes('<img'), 'a vendor name reached the page as markup');
  assert.deepEqual(texts(region, 'advice').map(decoded), ['</div><script>alert(1)</script>'], 'and it reads as the words it was');
});

test('nothing active draws nothing — the region is empty, as the one it replaced was', () => {
  assert.equal(runPanel(panelState('', {})).region('live-qconsults'), '');
});

test('the record reader keeps a running question and one finished within the window, and drops an old one', () => {
  const finished = (minutesAgo: number): QuestionConsult => record({ status: 'answered', endedUtc: new Date(NOW - minutesAgo * 60_000).toISOString() });

  assert.equal(isKept(record(), NOW), true);
  assert.equal(isKept(finished(10), NOW), true);
  assert.equal(isKept(finished(90), NOW), false);
  assert.equal(parseQuestionConsult('not json'), undefined);
  assert.equal(parseQuestionConsult(JSON.stringify({ status: 'consulting' })), undefined, 'a file with no id is not a record');
  assert.deepEqual(parseQuestionConsult(JSON.stringify({ id: 'q', rows: 'nope' }))?.rows, [], 'an unreadable row list is no rows');
});

/**
 * S4b item 9: the server's sweep ends a dead server's record, but only a LIVE server sweeps — so a window whose server
 * died showed "consulting" for ever. The record is SHOWN interrupted once its heartbeat has been silent past two
 * minutes (the server's own `HeartbeatStale`), terminal and not spinning, and the watcher looks again at that moment.
 */
test('a consulting question whose heartbeat is older than two minutes is shown interrupted — terminal, not spinning', () => {
  const beat = (msAgo: number): QuestionConsult => record({ heartbeatUtc: new Date(NOW - msAgo).toISOString(), startedUtc: new Date(NOW - msAgo - 60_000).toISOString() });
  const live = beat(HEARTBEAT_STALE_MS - 1_000);
  const silent = beat(HEARTBEAT_STALE_MS + 1_000);

  assert.equal(shownAt(live, NOW).status, 'consulting', 'a beat inside the window is a live question');
  const shown = shownAt(silent, NOW);
  assert.equal(shown.status, 'interrupted');
  assert.deepEqual(shown.rows.map((one) => one.status), ['interrupted', 'interrupted'], 'its rows stop spinning too');
  assert.equal(shownAt(record({ status: 'answered', heartbeatUtc: new Date(NOW - 3_600_000).toISOString() }), NOW).status, 'answered', 'a finished one is what it says');
  assert.equal(nextStaleAt([live]), Date.parse(live.heartbeatUtc) + HEARTBEAT_STALE_MS + 1, 'the watcher looks again at the deadline');
  assert.equal(nextStaleAt([shown]), undefined, 'nothing left to wait for');

  const region = runPanel(panelState('', { qconsults: [shown] })).region('live-qconsults');
  assert.match(textOf(region), /Interrupted/, `the question is drawn, as interrupted: ${region}`);
  assert.ok(!region.includes('badge running'), 'no row spins');
  assert.equal((region.match(/badge stopped/g) ?? []).length, 2, 'each row wears the terminal chip');
});
