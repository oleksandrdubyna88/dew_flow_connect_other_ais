import * as assert from 'node:assert';
import { test } from 'node:test';
import { TeamServerState, anyAdmin, teamUsageBlock, usageScopeControl } from '../teamServerView';

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
  // Both render the same sentence today, which is honest: the panel genuinely does not know. What
  // it must never do is show a zero as though it were a measurement.
  const html = teamUsageBlock(state(), short);

  assert.ok(!html.includes('0 run(s)'));
});

test('a vendor name with markup in it cannot become markup', () => {
  const html = teamUsageBlock(state({
    usage: { window: 'today', vendors: [{ vendor: '<script>x</script>', tokensIn: 1, tokensOut: 1, runs: 1, failed: 0, seconds: 1 }] },
  }), short);

  assert.ok(!html.includes('<script>'));
});

test('Company is offered only to somebody the server calls an admin', () => {
  // The SERVER refuses scope=company for everybody else, so rendering the control for them would be
  // offering a button that answers 403.
  const notAdmin = state({ catalog: { serverVersion: '1', isAdmin: false, vendors: [], error: '' } });
  const admin = state({ catalog: { serverVersion: '1', isAdmin: true, vendors: [], error: '' } });

  assert.strictEqual(anyAdmin([notAdmin]), false);
  assert.strictEqual(usageScopeControl([notAdmin], 'me'), '');
  assert.ok(usageScopeControl([admin], 'me').includes('teamUsageScope'));
});

test('the Company control says which way it will move', () => {
  const admin = state({ catalog: { serverVersion: '1', isAdmin: true, vendors: [], error: '' } });

  assert.ok(usageScopeControl([admin], 'me').includes('Show the whole company'));
  assert.ok(usageScopeControl([admin], 'company').includes('show just me'));
});
