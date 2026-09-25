import assert from 'node:assert/strict';
import { test } from 'node:test';
import { EMPTY_LOG, parseLog } from '../roundsDb';
import type { DbLog } from '../roundsDb';
import { DEFAULT_LIMIT, readLog } from '../roundsDbRead';
import { blindSpotsHtml } from '../roundsLog';
import { logCommandOf } from '../roundsLogMessages';

/**
 * *What it keeps missing*, over a period — the half the SERVER counts (operator, 2026-09-25).
 *
 * <p>The extension sends the period's first instant as `--log --since`; the server counts the blind
 * spots and the defended list over rounds since then and ECHOES the instant. The echo is the only proof:
 * a coai-mcp 0.36.0 given the flag was measured to ignore it and exit 0 with the all-time answer.</p>
 */

function calls(...answers: { code: number; output: string }[]) {
  const seen: string[][] = [];
  let next = 0;

  return {
    seen,
    run: async (args: readonly string[]) => {
      seen.push([...args]);
      return answers[Math.min(next++, answers.length - 1)] ?? { code: 1, output: '' };
    },
  };
}

const ANSWER = (since?: string) => JSON.stringify({
  rounds: [], blindSpots: [{ kind: 'category', name: 'Ux', accepted: 1, total: 1 }], defended: [],
  totals: { rounds: 0, findings: 0, accepted: 0, rejected: 0, gating: 0, tokensIn: 0, tokensOut: 0, costUsd: 0 },
  ...(since === undefined ? {} : { since }),
});

test('a period is sent to the server as --since, and All sends nothing', async () => {
  const withPeriod = calls({ code: 0, output: ANSWER('2026-09-25T00:00:00.0000000Z') });
  const allTime = calls({ code: 0, output: ANSWER('') });

  await readLog('coai-mcp.exe', {}, withPeriod.run, '2026-09-24T22:00:00.000Z');
  await readLog('coai-mcp.exe', {}, allTime.run, '');

  assert.deepEqual(withPeriod.seen, [['--log', '--paged', '--limit', String(DEFAULT_LIMIT), '--since', '2026-09-24T22:00:00.000Z']]);
  assert.deepEqual(allTime.seen, [['--log', '--paged', '--limit', String(DEFAULT_LIMIT)]]);
});

test('the echo is what says the period was applied, and its absence says it was not', () => {
  assert.equal(parseLog(ANSWER('2026-09-25T00:00:00.0000000Z'), true).spotsSince, '2026-09-25T00:00:00.0000000Z');
  assert.equal(parseLog(ANSWER(), true).spotsSince, '', 'an older server sends no echo, which is "not applied"');
  assert.equal(parseLog(JSON.stringify({ since: 7 }), true).spotsSince, '', 'an echo that is not text is no echo');
  assert.equal(EMPTY_LOG.spotsSince, '');
});

test('the spots period is a command the page sends, and nothing else is one', () => {
  const said = (id: unknown) => logCommandOf({ type: 'command', command: 'spotsPeriod', id });

  assert.deepEqual(said('week'), { kind: 'spotsPeriod', period: 'week' });
  assert.deepEqual(said('all'), { kind: 'spotsPeriod', period: 'all' });
  for (const junk of ['forever', 'Week', 7]) {
    assert.notEqual(said(junk).kind, 'spotsPeriod', `${JSON.stringify(junk)} became a period`);
  }
});

const SPOTS: DbLog = {
  ...EMPTY_LOG,
  read: true,
  blindSpots: [{ kind: 'category', name: 'Ux', accepted: 1, total: 1 }],
};

test('the tab draws the period row with the chosen one marked, above what it counted', () => {
  const html = blindSpotsHtml({ ...SPOTS, spotsSince: '2026-09-25T00:00:00.0000000Z' }, 'week');

  assert.ok(html.startsWith('<div class="windows" data-periods="spots">'), 'the period row is not the first thing on the tab');
  assert.match(html, /class="tab on" data-command="spotsPeriod" data-id="week">Week</);
  assert.match(html, /By category/);
  assert.doesNotMatch(html, /update it/, 'a server that applied the period is not told to update');
});

test('a server that did not apply the period says so, and says it is showing all time', () => {
  const html = blindSpotsHtml({ ...SPOTS, spotsSince: '' }, 'day');

  assert.match(html, /counts every decision it holds/);
  assert.match(html, /0\.37\.0/);
  assert.match(html, /all time/);
});

test('All is all time, so an older server has nothing to apologise for there', () => {
  assert.doesNotMatch(blindSpotsHtml({ ...SPOTS, spotsSince: '' }, 'all'), /counts every decision it holds/);
});

test('an empty period still offers the switch, so a person can widen it', () => {
  const html = blindSpotsHtml({ ...EMPTY_LOG, read: true, spotsSince: '2026-09-25T00:00:00.0000000Z' }, 'day');

  assert.match(html, /data-periods="spots"/, 'with nothing in Today there was no way to reach Week');
  assert.match(html, /Nothing decided/);
});

test('a press reaches the server: the page command, the hook, a fresh read over the new period', async () => {
  // The host half imports `vscode`, so what is held is its shape — each link pinned WHOLE, in its own
  // function, because a match anywhere in a file stays green with the link broken somewhere else.
  const { readFileSync } = await import('node:fs');
  const { join } = await import('node:path');
  const read = (name: string) => readFileSync(join(process.cwd(), 'src', name), 'utf8');

  assert.match(read('roundsLogPanel.ts'),
    /if \(command\.kind === 'spotsPeriod'\) \{\s*void this\.hooks\.onSpotsPeriod\(command\.period\);/,
    'the panel no longer hands a spots press to its hook');
  assert.match(read('extension.ts'),
    /onSpotsPeriod: async \(period\) => \{\s*panelRef\.setSpotsPeriod\(period\);\s*await refreshRoundsLog\(roundsLog, watcher, panelRef, true\);/,
    'a spots press no longer stores the period and forces a fresh read');
  const cache = read('roundsLogCache.ts');
  assert.match(cache, /setSpotsPeriod\(period: LogPeriod\): void \{\s*this\.spots = period;\s*this\.forgetRoundsLog\(\);/,
    'a new period is served from the cache counted over the old one');
  assert.match(cache, /readLog\(server\.fsPath, \{ limit: MAX_LIMIT \}, serverRun\(server\.fsPath\), sinceOf\(this\.spots, new Date\(\)\)\)/,
    'the log is no longer read over the chosen period, worked out at read time');
});

// ------------------------------------------------------------------------------------------------
// The contract, LIVE: the real built coai-mcp answers `--since`, and the real reader carries the echo.

function builtServer(): string {
  return `${process.cwd()}/../src_mcp/src/bin/Debug/net10.0/${process.platform === 'win32' ? 'coai-mcp.exe' : 'coai-mcp'}`;
}

async function isBuilt(): Promise<boolean> {
  const { existsSync } = await import('node:fs');

  return existsSync(builtServer());
}

test('the real server echoes the period it counted over, and the real reader carries it', async (t) => {
  if (!(await isBuilt())) {
    t.skip('the server is not built');
    return;
  }
  const { mkdtempSync, rmSync } = await import('node:fs');
  const { tmpdir } = await import('node:os');
  const { join } = await import('node:path');
  const { spawnSync } = await import('node:child_process');
  // A data directory of its own, so the answer never comes from the machine's own database.
  const data = mkdtempSync(join(tmpdir(), 'coai-since-'));
  const run = async (args: readonly string[]) => {
    const ran = spawnSync(builtServer(), [...args], { encoding: 'utf8', env: { ...process.env, COAI_DATA_DIR: data }, timeout: 60_000 });
    return { code: ran.status ?? 1, output: ran.stdout ?? '' };
  };
  try {
    const log = await readLog(builtServer(), {}, run, '2026-09-24T22:00:00.000Z');
    const refused = await run(['--log', '--paged', '--since', 'yesterday']);

    assert.equal(log.spotsSince, '2026-09-24T22:00:00.0000000Z', 'the server did not echo the instant it applied');
    assert.equal(refused.code, 65, 'a malformed period must be refused as data, never read as an older binary (64)');
  } finally {
    rmSync(data, { recursive: true, force: true });
  }
});
