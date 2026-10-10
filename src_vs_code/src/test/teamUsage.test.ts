import * as assert from 'node:assert';
import { test } from 'node:test';
import { TeamServerState, anyAdmin, teamUsageBlock } from '../teamServerView';
import { usageTabHtml } from '../roundsLog';

const SERVER = { id: 'remsoft-dev', name: 'RemSoft Dev', url: 'https://coai.example.com' };

function state(over: Partial<TeamServerState> = {}): TeamServerState {
  return { server: SERVER, email: 'a@b.c', problem: '', stale: false, ...over };
}

const short = (n: number): string => String(n);

test('a server’s own totals are rendered, vendor by vendor', () => {
  // The server keeps that ledger — a review that ran there left no line in this machine's — so what
  // it says is shown rather than recomputed.
  const html = teamUsageBlock(state({
    usage: {
      window: 'today',
      vendors: [
        { vendor: 'codex', tokensIn: 100, tokensOut: 50, runs: 3, failed: 0, seconds: 12 },
        { vendor: 'claude', tokensIn: 10, tokensOut: 5, runs: 1, failed: 1, seconds: 4 },
      ],
    },
  }), short);

  assert.ok(html.includes('RemSoft Dev'));
  assert.ok(html.includes('codex'));
  assert.ok(html.includes('3 run(s)'));
});

test('a failed run still counts as spending, and says so', () => {
  // A review that burned ninety seconds and answered nothing spent the same as one that answered.
  const html = teamUsageBlock(state({
    usage: { window: 'today', vendors: [{ vendor: 'codex', tokensIn: 1, tokensOut: 1, runs: 2, failed: 1, seconds: 9 }] },
  }), short);

  assert.ok(html.includes('1 failed'));
});

test('a server with nothing recorded says that, rather than showing an empty chart', () => {
  const html = teamUsageBlock(state({ usage: { window: 'today', vendors: [] } }), short);

  assert.ok(html.includes('Nothing recorded on this server'));
});

test('a server that has not answered yet is not mistaken for one that spent nothing', () => {
  // The figures are cached per window now (teamUsageCache.ts), so a new window has NO answer until the server is
  // asked — for a moment after every window press. Saying "nothing recorded" then would be a measurement nobody took.
  const html = teamUsageBlock(state(), short);

  assert.ok(!html.includes('0 run(s)'));
  assert.ok(!html.includes('Nothing recorded'), 'an unanswered question rendered as an empty answer');
  assert.ok(html.includes('Asking RemSoft Dev…'), `it does not say it is asking: ${html}`);
});

test('a server that could not be asked says why, rather than that it is still asking', () => {
  const html = teamUsageBlock(state({ problem: 'it did not answer within 10s' }), short);

  assert.ok(html.includes('it did not answer within 10s'));
  assert.ok(!html.includes('Asking'), 'a failed request reads as one still on its way');
});

test('a vendor name with markup in it cannot become markup', () => {
  const html = teamUsageBlock(state({
    usage: { window: 'today', vendors: [{ vendor: '<script>x</script>', tokensIn: 1, tokensOut: 1, runs: 1, failed: 0, seconds: 1 }] },
  }), short);

  assert.ok(!html.includes('<script>'));
});

test('only somebody the server calls an admin counts as one', () => {
  // The SERVER refuses scope=company for everybody else; the flag only decides whether the Team server tab is shown.
  const notAdmin = state({ catalog: { serverVersion: '1', isAdmin: false, vendors: [], error: '' } });
  const admin = state({ catalog: { serverVersion: '1', isAdmin: true, vendors: [], error: '' } });

  assert.strictEqual(anyAdmin([notAdmin]), false);
  assert.strictEqual(anyAdmin([notAdmin, admin]), true);
});

test('the spending tab draws no Company control, for an admin either — the company view is the Team server tab (D6)', () => {
  // The toggle sent an id-less command the page's decoder drops (roundsLogMessages.ts), so it was a button wired to
  // nothing. The spending tab is this account's own spending; whoever is an admin reads the company on its own tab.
  const admin = state({
    catalog: { serverVersion: '1', isAdmin: true, vendors: [], error: '' },
    usage: { window: 'today', vendors: [{ vendor: 'codex', tokensIn: 1, tokensOut: 1, runs: 1, failed: 0, seconds: 1 }] },
  });
  const html = usageTabHtml([], 'day', [], {}, { teamServers: [admin] });

  assert.match(html, /RemSoft Dev/, 'the server block is still drawn — this fixture reached the Team-server branch');
  assert.ok(!html.includes('teamUsageScope'), 'the dead toggle is back');
  assert.ok(!/whole company/i.test(html), 'the spending tab still offers the company view');
});
