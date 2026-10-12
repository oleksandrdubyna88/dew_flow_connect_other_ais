/**
 * The usage-counts leg of the seam (story 5.1/5.2 of todo/PLAN_every_round_is_counted.md — E5's plan round decision).
 *
 * <p>The Team server WRITES the round counts into `/api/usage` and the extension READS them through its own parser
 * (`serverRounds` in `teamServerApi.ts`); each side has its own suite, and each suite compares its side against its own
 * idea of the names. A field the server serialises under a name the extension does not read would leave both green and
 * show every admin <i>count unavailable</i> after the deploy. So this starts the REAL server built from this tree over a
 * seeded ledger, asks it the way the tab does — `?windows=5h,24h,week`, company and personal — and reads the answer
 * through the extension's OWN client and parser, asserting every count BY VALUE.</p>
 *
 * <p>The ledger is the shape the server's writer leaves (`JobRunner.Ledger`): lines with a round, a retry whose job met
 * a refusing account first (two lines, one request), a cancelled repair, the server's own follow-up beside a client's,
 * a budget refusal, a deploy canary, and lines from an older client with no round — placed in each window and not the
 * narrower one, so a window that read another's range is a wrong number, not a lucky one.</p>
 *
 * <p>Nothing is launched and nothing is billed: the server only reads its ledger.</p>
 */
import { createHmac } from 'node:crypto';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

import { startTeamServer } from './team-server-child.mjs';

const { ask, createSession, fetchUsage, serverRounds } = await import('../out/teamServerApi.js');

const DOMAIN = 'seam.test';
const ADMIN = `admin@${DOMAIN}`;
const DEV = `dev@${DOMAIN}`;
const OTHER = `other@${DOMAIN}`;
const HOUR = 3_600_000;

const A = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa';
const B = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb';
const C = 'cccccccccccccccccccccccccccccccc';
const D = 'dddddddddddddddddddddddddddddddd';

/** One ledger line as `JobRunner.Ledger` writes it, `hoursAgo` back from now. */
function line(hoursAgo, email, vendor, extra = {}) {
  return JSON.stringify({
    utc: new Date(Date.now() - hoursAgo * HOUR).toISOString(),
    provider: vendor, model: 'm', role: 'Architecture', stage: 'TeamServer', seconds: 3,
    tokensIn: 100, tokensOut: 10, costUsd: null, outcome: 'ok', email, kind: 'review', attempt: 'first',
    ...extra,
  });
}

/** A request's lines: its job id is its turn, as on the server. */
const job = (id, round, clientAttempt, extra = {}) => ({ job: id, turn: id, round, clientAttempt, ...extra });

const LEDGER = [
  // The last five hours: dev's round A through every client word, the server's own follow-up, an old client, a canary.
  line(1, DEV, 'codex', job('a1', A, 'first')),
  line(1, DEV, 'codex', job('a2', A, 'retry', { attempt: 'rotated', outcome: 'RateLimited' })),
  line(1, DEV, 'codex', job('a2', A, 'retry', { attempt: 'rotated' })),
  line(1, DEV, 'codex', job('a3', A, 'repair', { outcome: 'Cancelled', endCause: 'withdrawn' })),
  line(1, DEV, 'codex', job('a4', A, 'first')),
  line(1, DEV, 'codex', job('a4', A, 'first', { attempt: 'followUp' })),
  line(1, DEV, 'codex'),
  line(1, DEV, 'codex', { kind: 'canary', job: 'k1', turn: 'k1' }),
  // Ten hours ago — the 24 h window and not the 5 h one: other's round B, a client follow-up and a budget refusal.
  line(10, OTHER, 'codex', job('b1', B, 'first')),
  line(10, OTHER, 'codex', job('b2', B, 'followUp')),
  line(10, OTHER, 'codex', job('b3', B, 'first', { outcome: 'NoBudget', tokensIn: 0, tokensOut: 0, seconds: 0 })),
  // Three days ago — the week only: a second vendor, and another round.
  line(72, DEV, 'gemini', job('c1', C, 'first')),
  line(72, DEV, 'gemini'),
  line(72, OTHER, 'codex', job('d1', D, 'first')),
];

/** What the extension's parser must read, per window, per vendor — [vendor, rounds, runsWithoutRound, retries, repairs, followUps, cancelled, noBudget, launchesByKind]. */
const COMPANY = {
  '5h': [['codex', 1, 1, 1, 1, 0, 1, 0, { canary: 1, review: 7 }]],
  '24h': [['codex', 2, 1, 1, 1, 1, 1, 1, { canary: 1, review: 9 }]],
  week: [['codex', 3, 1, 1, 1, 1, 1, 1, { canary: 1, review: 10 }], ['gemini', 1, 1, 0, 0, 0, 0, 0, { review: 2 }]],
};

/** Each person's rows in the week. */
const PEOPLE_WEEK = [
  [DEV, [['codex', 1, 1, 1, 1, 0, 1, 0, { canary: 1, review: 7 }], ['gemini', 1, 1, 0, 0, 0, 0, 0, { review: 2 }]]],
  [OTHER, [['codex', 2, 0, 0, 0, 1, 0, 1, { review: 3 }]]],
];

/** What dev reads of their own, per window. */
const MINE = {
  '5h': [['codex', 1, 1, 1, 1, 0, 1, 0, { canary: 1, review: 7 }]],
  '24h': [['codex', 1, 1, 1, 1, 0, 1, 0, { canary: 1, review: 7 }]],
  week: [['codex', 1, 1, 1, 1, 0, 1, 0, { canary: 1, review: 7 }], ['gemini', 1, 1, 0, 0, 0, 0, 0, { review: 2 }]],
};

const row = (one) => [one.vendor, one.rounds, one.runsWithoutRound, one.retries, one.repairs, one.followUps, one.cancelled, one.noBudget, one.launchesByKind];

/** An identity token the server's local scheme accepts — the minting teamUsage.contract.ts uses. */
function localToken(key, email) {
  const part = (o) => Buffer.from(JSON.stringify(o)).toString('base64url');
  const head = part({ alg: 'HS256', typ: 'JWT' });
  const body = part({ iss: 'coai-local', email, exp: Math.floor(Date.now() / 1000) + 600 });

  return `${head}.${body}.${createHmac('sha256', key).update(`${head}.${body}`).digest('base64url')}`;
}

async function sessionFor(server, email) {
  const made = await createSession(server.url, localToken(server.key, email));
  if (!made.ok) {
    throw new Error(`${email} was refused a session: ${made.status} — ${made.message}`);
  }

  return made.value.token;
}

/**
 * The counts the extension's own client and parser read. The company answer is asked exactly as the Team server tab
 * asks it (`fetchUsage`, which adds the round windows); nothing in the extension asks a PERSONAL answer for windows, so
 * that one goes through the same client's `ask` with the route spelled out — the parser is the same either way.
 */
async function countsFor(server, email, scope) {
  const token = await sessionFor(server, email);
  const answer = scope === 'company'
    ? await fetchUsage(server.url, token, 'today', 'company')
    : await ask(server.url, 'api/usage?windows=5h,24h,week', { token });
  if (!answer.ok) {
    throw new Error(`the ${scope} answer was refused: ${answer.status} — ${answer.message}`);
  }
  const read = serverRounds(answer.value);
  if (read.kind !== 'counted') {
    throw new Error(`the extension's parser could not read the server's ${scope} counts (${read.reason}); the answer was ${JSON.stringify(answer.value).slice(0, 600)}`);
  }

  return read.windows;
}

function same(actual, expected, what) {
  if (JSON.stringify(actual) !== JSON.stringify(expected)) {
    throw new Error(`${what}\n  expected ${JSON.stringify(expected)}\n  read     ${JSON.stringify(actual)}`);
  }
}

/** The company answer: every window's vendors, and each person's in the week. */
function checkCompany(windows) {
  for (const window of windows) {
    same(window.vendors.map(row), COMPANY[window.window], `company ${window.window}: the counts the extension read`);
  }
  const week = windows.find((one) => one.window === 'week');
  same(week.people.map((person) => [person.email, person.vendors.map(row)]), PEOPLE_WEEK, 'company week: each person\'s rows');
}

function checkMine(windows) {
  for (const window of windows) {
    same(window.vendors.map(row), MINE[window.window], `dev's own ${window.window}: the counts the extension read`);
    same(window.people, [], `dev's own ${window.window}: a personal answer lists no colleagues`);
  }
}

/** The data directory, retried while the stopping server still holds its log file; a leftover is not worth a red run. */
function removeData(dataDir) {
  try {
    rmSync(dataDir, { recursive: true, force: true, maxRetries: 20, retryDelay: 250 });
  } catch {
    // Not worth failing for.
  }
}

/** @returns {Promise<{ windows: string[], vendorsRead: number }>} */
export async function usageCountsSeam({ fail, extensionRoot = resolve(import.meta.dirname, '..') }) {
  const dataDir = mkdtempSync(join(tmpdir(), 'coai-seam-usage-'));
  writeFileSync(join(dataDir, 'usage.jsonl'), `${LEDGER.join('\n')}\n`, 'utf8');
  let server;
  let outcome;
  try {
    server = await startTeamServer({ extensionRoot, dataDir, domain: DOMAIN, admins: ADMIN, stdio: 'ignore' });
    const company = await countsFor(server, ADMIN, 'company');
    checkCompany(company);
    checkMine(await countsFor(server, DEV, 'me'));
    outcome = { windows: company.map((one) => one.window), vendorsRead: company.reduce((n, one) => n + one.vendors.length, 0) };
  } catch (error) {
    outcome = error instanceof Error ? error.message : String(error);
  } finally {
    // Before any `fail`, which exits the process and would skip a `finally` that ran after it.
    server?.stop();
    removeData(dataDir);
  }
  if (typeof outcome === 'string') {
    fail(`the usage counts did not cross from the server to the extension: ${outcome}`);
  }

  return outcome;
}
