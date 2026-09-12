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

test('a chat card is the shape of a reviewer card, with what a reviewer has no equivalent of below', () => {
  // Asked for in those words after the first version shipped as a table: *"выглядеть должно так же
  // как ревьюверы, с полосочкой"*. One question about two ledgers, so one layout.
  const html = page();
  const card = /<div class="spend">[\s\S]*?<\/div>\s*<\/div>/.exec(html.slice(html.indexOf('>Chat<')))?.[0] ?? '';

  assert.notStrictEqual(card, '', 'the chat section draws no card at all');
  assert.match(card, /<span class="name"[^>]*>antigravity<\/span>/, 'the card does not name the vendor');
  assert.match(card, /<span class="model">gemini-3\.8-flash<\/span>/, 'the card does not name the model');
  assert.match(card, /<div class="bar"><span style="width:100%">/, 'the card has no bar, which is what was asked for');
  assert.match(card, /<div class="figures">2.0M in · 1.0M out · 1 turn\(s\)<\/div>/, 'the figures line is not the reviewer shape');
  // 2M in at $1 and 1M out at $10 is $12, and nobody billed it, so it is marked as worked out.
  assert.match(card, /<span class="cost">~\$12.00<\/span>/, 'the card does not price the turn from the rate in force');
  assert.match(card, /\$1 in · \$10 out per million · ~\$12.00 all time/, 'the rates and the all-time total are not below');
  assert.match(card, /asked 2 · opened 3/, 'the two counts are not on their own line below');
});

test('a model nobody prices says so, rather than drawing three dashes', () => {
  const html = usageTabHtml([REVIEW], 'day', [], {}, [], 'me', { turns: [TURN], doors: DOORS });

  assert.match(html, /no rate set for this model/, 'a card with no rate said nothing a reader can act on');
});

test('the vendor named is the VENDOR, not the preset the chat recorded', () => {
  // A chat records the id of the model preset in force, which is a generated string nobody
  // recognises - and the section above this one is per vendor, so the page named the same vendors
  // two different ways until this resolved one to the other.
  const html = usageTabHtml([REVIEW], 'day', [], PRICES, [], 'me', {
    turns: [{ ...TURN, provider: 'preset-mtwxbymp-4' }],
    doors: [],
    vendorOf: (provider) => (provider === 'preset-mtwxbymp-4' ? 'antigravity' : provider),
  });

  assert.match(html, /<span class="name"[^>]*>antigravity<\/span>/, 'the preset id was not resolved to its vendor');
  assert.doesNotMatch(html, /preset-mtwxbymp-4/, 'the generated preset id reached the page');
});

test('one line under BOTH ledgers adds them up', () => {
  // "What has this cost me" is one question whichever half of the product spent it - and the bill
  // and the estimate stay apart across the join, as they do inside each half.
  const html = page();

  assert.match(html, /<div class="hint total everything">Reviewers and chat together: 4\.2M tokens · ~\$12.00<\/div>/,
    'the two ledgers are not added up under a rule of their own');
  assert.strictEqual(html.match(/<hr class="ledgers">/g)?.length, 2,
    'there is no second rule, so the joint total reads as part of the chat section');
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
