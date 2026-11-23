import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { liveRegions } from '../panelView';
import { type QuestionConsult, type QuestionConsultRow } from '../questionConsults';
import { panelState, runPanel } from './panelPageHarness';
import { textOf } from './renderedText';

/**
 * **Active questions**, RUN (todo/PLAN_question_consultant.md, S4 acceptance 3): the sidebar page with its own
 * script, the region as it is drawn and as a live push replaces it — a consulting question one line per model,
 * advancing as rows settle. (The person's card with its Answer button, and every answer folded under it, went with the held gate: v2 E4, story
 * s6-dead-v1-out.)
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

test('every word a model or the server wrote is escaped, never markup', () => {
  const page = runPanel(panelState('', {
    qconsults: [record({ id: 'q-9', question: '<script>q</script>', alert: '<img src=x onerror=1>', rows: [row('r', { vendor: '<img src=x onerror=1>', promptTitle: '</div><script>alert(1)</script>' })] })],
  }));
  const region = page.region('live-qconsults');

  assert.ok(!region.includes('<script>'), 'a question or a prompt title reached the page as markup');
  assert.ok(!region.includes('<img'), 'a vendor name or an alert reached the page as markup');
  assert.ok(decoded(textOf(region)).includes('<script>q</script>'), 'and the question reads as the words it was');
});

test('nothing active draws nothing — the region is empty, as the one it replaced was', () => {
  assert.equal(runPanel(panelState('', {})).region('live-qconsults'), '');
});

/**
 * A question whose server stopped answering is drawn INTERRUPTED — terminal and not spinning (S4b item 9): its own section, one stopped chip per row,
 * and no row that looks as if it is still running.
 */
test('an interrupted question is drawn as interrupted — terminal, not spinning', () => {
  const shown = record({ status: 'interrupted', rows: [row('sonnet-disk', { status: 'interrupted' }), row('astra-web', { status: 'interrupted' })] });
  const region = runPanel(panelState('', { qconsults: [shown] })).region('live-qconsults');

  assert.match(textOf(region), /Interrupted/, `the question is drawn, as interrupted: ${region}`);
  assert.ok(!region.includes('badge running'), 'no row spins');
  assert.equal((region.match(/badge stopped/g) ?? []).length, 2, 'each row wears the terminal chip');
});
