import assert from 'node:assert/strict';
import { test } from 'node:test';

import { BUSY_AFTER_MS } from '../busyMark';
import { LogRow } from '../roundsLog';
import { PageClock } from './panelPageHarness';
import { open, type Page, TOTALS } from './roundsLogPageHarness';

/**
 * The rounds log shows that it is working (todo/PLAN_busy_marks_on_every_webview.md, E1).
 *
 * <p>Every action on this page reads the rounds database through `coai-mcp`, measured at 694–930 ms a read on
 * 2026-10-03 — so every press crossed the half second the operator asked to see marked, with nothing on screen.
 * The page RUN on a clock the test moves: what would a person see if the mark were deleted?</p>
 */

function row(over: Partial<LogRow> = {}): LogRow {
  return {
    key: 'k1', kind: 'review',
    startedUtc: '2026-09-05T07:41:00.000Z', completedUtc: '2026-09-05T07:43:10.000Z',
    repoPath: 'D:/repo', repoName: 'repo', branch: 'main', stage: 'code review', number: 1,
    subject: 'SCOPE — the thing', status: 'done', note: '', decided: null, verdict: 'proceed', gating: 1,
    findings: 1, seconds: 130, decideSeconds: null, tokensIn: null, tokensOut: null, costUsd: null, costInUsd: null,
    costOutUsd: null, costTotalUsd: null, costIsEstimate: false, costPartial: false,
    answered: 'all 3 reviewers answered', vendors: ['codex'], reviewers: ['codex/Architecture — done'],
    calledBy: 'codex 0.9 · model not stated',
    reviewerColours: ['#fff'], found: [], foundCount: 0, foundState: 'unasked', origin: 'db',
    dbKey: { sessionId: 's1', stage: 'CodeReview', number: 1 },
    ...over,
  };
}

/** A command button as the page's click handler finds it, keeping the attributes the mark sets on it. */
function commandButton(command: string, id: string): { readonly target: unknown; readonly busy: () => string | null } {
  let attributes: Readonly<Record<string, string>> = { 'data-command': command, 'data-id': id };
  const button = {
    getAttribute: (name: string) => attributes[name] ?? null,
    setAttribute: (name: string, value: string) => { attributes = { ...attributes, [name]: value }; },
    removeAttribute: (name: string) => {
      attributes = Object.fromEntries(Object.entries(attributes).filter(([key]) => key !== name));
    },
  };

  return {
    target: { closest: (asked: string) => (asked === '[data-command]' ? button : null) },
    busy: () => button.getAttribute('aria-busy'),
  };
}

function running(): { readonly page: Page; readonly clock: PageClock } {
  const clock = new PageClock();

  return { page: open([row()], TOTALS, '', clock), clock };
}

/** The last numbered command the page posted. */
function lastCommand(page: Page): Record<string, unknown> {
  const sent = page.posted.filter((one) => (one as Record<string, unknown>)['type'] === 'command').at(-1);
  assert.ok(sent !== undefined, 'the page posted a command');

  return sent as Record<string, unknown>;
}

const shown = (page: Page): boolean => !page.at('busy-bar').hidden;

test('a pressed command is numbered, and shows nothing before the delay and the bar from it on', () => {
  const { page, clock } = running();
  const button = commandButton('usageWindow', 'week');
  assert.equal(shown(page), false, 'drawn hidden');
  page.click(button.target);

  const sent = lastCommand(page);
  assert.equal(sent['command'], 'usageWindow');
  assert.equal(typeof sent['seq'], 'number', 'numbered, so the host can settle it');
  assert.equal(typeof sent['doc'], 'string');
  clock.advance(BUSY_AFTER_MS - 1);
  assert.equal(shown(page), false, 'a read that answers in time shows nothing');
  clock.advance(1);
  assert.equal(shown(page), true, `the bar at ${BUSY_AFTER_MS} ms`);
  assert.equal(button.busy(), 'true', 'the button pressed is marked');
});

test('the host settling it takes the bar and the mark away', () => {
  const { page, clock } = running();
  const button = commandButton('usageWindow', 'month');
  page.click(button.target);
  clock.advance(BUSY_AFTER_MS);
  const sent = lastCommand(page);

  page.deliver({ type: 'settled', seq: sent['seq'], doc: sent['doc'], ok: true });
  assert.equal(shown(page), false);
  assert.equal(button.busy(), null);
});

test('the page says it is ready as its last post, and the host’s count draws after what is LEFT of the delay', () => {
  const { page, clock } = running();
  assert.deepEqual(page.posted.at(-1), { type: 'ready' }, 'ready is the last thing the page says');

  page.deliver({ type: 'busy', count: 1, oldestMs: 200 });
  clock.advance(BUSY_AFTER_MS - 200 - 1);
  assert.equal(shown(page), false);
  clock.advance(1);
  assert.equal(shown(page), true, 'work already running is not given a fresh delay');
  page.deliver({ type: 'busy', count: 0, oldestMs: 0 });
  assert.equal(shown(page), false);
});
