import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ChatDoorRecord } from '../chatDoors';
import { ChatTurnRecord } from '../chatUsage';
import { usageTabHtml } from '../roundsLog';
import { UsageEntry } from '../usage';

/**
 * The spending tab as a page: two ledgers, named, with a rule between them.
 *
 * <p>The arithmetic is tested in `chatSpendRows.test.ts`. This is the other half of the bargain —
 * that the numbers reach the page at all. Every module below could be right while the tab went on
 * rendering reviewers alone, which is exactly the state this change found.</p>
 */

function todayAt(hours: number): string {
  const day = new Date();
  day.setHours(hours, 0, 0, 0);

  return day.toISOString();
}

const REVIEW: UsageEntry = {
  utc: todayAt(9),
  provider: 'codex',
  model: 'gpt-5.6-sol',
  role: 'Architecture',
  stage: 'CodeReview',
  seconds: 23,
  tokensIn: 1_000_000,
  tokensOut: 200_000,
  costUsd: null,
  outcome: 'ok',
};

const TURN: ChatTurnRecord = {
  utc: todayAt(12),
  provider: 'antigravity',
  model: 'gemini-3.8-flash',
  tokensIn: 2_000_000,
  tokensOut: 1_000_000,
  costUsd: null,
  seconds: 4,
  outcome: 'answered',
  conversation: 'c1',
  title: 'a tab',
};

const DOORS: readonly ChatDoorRecord[] = [
  { utc: todayAt(11), door: 'take', provider: 'antigravity', model: 'gemini-3.8-flash' },
  { utc: todayAt(11), door: 'add', provider: 'antigravity', model: 'gemini-3.8-flash' },
  { utc: todayAt(11), door: 'key', provider: 'antigravity', model: 'gemini-3.8-flash' },
];

const PRICES = { 'gemini-3.8-flash': { inPerMillion: 1, outPerMillion: 10, source: 'openrouter' as const } };

function page(): string {
  return usageTabHtml([REVIEW], 'day', [], PRICES, [], 'me', { turns: [TURN], doors: DOORS });
}

test('the tab names both ledgers and draws a line between them', () => {
  // They are written by different programs and answer different questions. Adding them up by eye is
  // wrong often enough that the page says which is which.
  const html = page();

  assert.match(html, /<h3 class="ledger">Reviewers<\/h3>/, 'the reviewer half is not named');
  assert.match(html, /<h3 class="ledger">Chat<\/h3>/, 'the chat half is not named');
  assert.match(html, /<hr class="ledgers">/, 'nothing separates the two ledgers');
  assert.ok(html.indexOf('>Reviewers<') < html.indexOf('<hr class="ledgers">'), 'the rule is above the first heading');
  assert.ok(html.indexOf('<hr class="ledgers">') < html.indexOf('>Chat<'), 'the rule is below the second heading');
});

test('each ledger carries its own total, and they are not one number', () => {
  const html = page();

  assert.match(html, /All vendors: 1\.2M tokens/, 'the reviewer total went missing or changed shape');
  // Three million tokens of chat, and the chat total must not have swallowed the review's 1.2M.
  assert.match(html, /All chats: 3\.0M tokens/, 'the chat section has no total of its own');
  assert.match(html, /All chats: [^<]*asked 2 · opened 3/, 'the two counts are not in the chat total');
});

test('a chat row names the vendor, the model, both rates and what it has cost', () => {
  const html = page();

  assert.match(html, /<td class="name"[^>]*>antigravity<\/td>/, 'the chat row does not name the vendor');
  assert.match(html, /<td>gemini-3\.8-flash<\/td>/, 'the chat row does not name the model that answered');
  // 2M in at $1 and 1M out at $10 is $12, and nobody billed it, so it is marked as worked out.
  assert.match(html, /~\$12/, 'the row does not price the turn from the rate in force');
  assert.match(html, /\$1<\/td>/, 'the input rate is not shown');
  assert.match(html, /\$10<\/td>/, 'the output rate is not shown');
});

test('the counts are in the row as well as in the total', () => {
  const html = page();
  const row = /<td class="name"[^>]*>antigravity<\/td>[\s\S]*?<\/tr>/.exec(html)?.[0] ?? '';

  assert.notStrictEqual(row, '', 'there is no chat row to read');
  // take + add is two; all three doors is three. The last two cells of the row.
  assert.match(row, /<td class="n">2<\/td>\s*<td class="n">3<\/td>\s*<\/tr>/,
    'Asked and Opened are not the last two cells of the row');
});

test('a tab with no conversations still shows the chat section, and says why it is empty', () => {
  const html = usageTabHtml([REVIEW], 'day', [], PRICES, [], 'me', { turns: [], doors: [] });

  assert.match(html, /<h3 class="ledger">Chat<\/h3>/, 'the section vanished rather than saying it was empty');
  assert.match(html, /No conversations in this window/, 'an empty chat ledger says nothing at all');
});

test('the tab still renders when nothing hands it any chat ledgers at all', () => {
  // The parameter is optional because two callers and a dozen tests predate it, and a page that
  // throws for want of a new argument is a page nobody can open.
  const html = usageTabHtml([REVIEW], 'day', [], PRICES);

  assert.match(html, /<h3 class="ledger">Chat<\/h3>/);
  assert.match(html, /All vendors: 1\.2M tokens/);
});
