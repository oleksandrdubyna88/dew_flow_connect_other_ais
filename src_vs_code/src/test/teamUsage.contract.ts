/**
 * The Team server tab's flow against a REAL Team server (todo/PLAN_team_usage_by_person.md, story 1.5).
 *
 * <p>The unit suite stubs `fetch`, so it can prove what the cache asks and how the tab renders an answer, never that
 * the server answers the question the client puts. This file puts it through the real client — `fetchCatalog`,
 * `fetchUsage` and the real `TeamUsageCache` — at the server `scripts/run-contract.mjs` started, which gains an admin
 * through `Coai__Admins` for exactly this.</p>
 *
 * <p>What it does NOT prove, and cannot: the page itself. A host cannot read a webview, so what the tab DOES with these
 * answers is the bundled page harness's (`teamServerTabPage.test.ts`). Nor does a fresh server have spending to show —
 * its ledger is empty, so the people and vendor lists are checked for their shape, not their figures.</p>
 */
import * as assert from 'node:assert';
import { createHmac } from 'node:crypto';
import { test } from 'node:test';

import { createSession, fetchCatalog, fetchUsage, Usage } from '../teamServerApi';
import { teamParts, teamTabPush } from '../teamServerTab';
import { TeamServerState } from '../teamServerView';
import { NO_SELECTION, withShown } from '../teamTabSelection';
import { TeamUsageCache, UsageScope } from '../teamUsageCache';
import { pageTree } from './pageTree';

function required(name: string): string {
  const value = process.env[name] ?? '';
  if (value.length === 0) {
    throw new Error(`${name} is not set. Run this through "npm run test:contract", which starts a server with an admin `
      + 'and fills it in, or set it to an email in the Coai:Admins of the server you point it at.');
  }

  return value;
}

const SERVER = required('COAI_CONTRACT_URL');
const KEY = required('COAI_CONTRACT_KEY');
const DOMAIN = required('COAI_CONTRACT_DOMAIN');
const ADMIN = required('COAI_CONTRACT_ADMIN');

/** An identity token the server's local scheme accepts — the same minting as teamServerSession.contract.ts. */
function localToken(email: string): string {
  const part = (o: object) => Buffer.from(JSON.stringify(o)).toString('base64url');
  const head = part({ alg: 'HS256', typ: 'JWT' });
  const body = part({ iss: 'coai-local', email, exp: Math.floor(Date.now() / 1000) + 600 });

  return `${head}.${body}.${createHmac('sha256', KEY).update(`${head}.${body}`).digest('base64url')}`;
}

async function sessionFor(email: string): Promise<string> {
  const result = await createSession(SERVER, localToken(email));
  if (!result.ok) {
    assert.fail(`${email} was refused a session: ${result.status} — ${result.message}`);
  }

  return result.value.token;
}

/** The real cache over the real client, counting what it asked. */
function realCache(): { cache: TeamUsageCache; asked: string[] } {
  const asked: string[] = [];
  const cache = new TeamUsageCache({
    now: () => Date.now(),
    fetchUsage: (url, token, window, scope) => {
      asked.push(`${scope}/${window}`);

      return fetchUsage(url, token, window, scope);
    },
    changed: () => undefined,
  });

  return { cache, asked };
}

function target(token: string, scope: UsageScope, window: string, admin: boolean) {
  return {
    server: { id: 'contract', url: SERVER },
    wants: [{ scope, window }],
    prepare: async () => ({ token, admin, account: 'whoever' }),
  };
}

function state(admin: boolean): TeamServerState {
  return {
    server: { id: 'contract', name: 'Contract', url: SERVER },
    email: ADMIN,
    problem: '',
    stale: false,
    catalog: { serverVersion: 'contract', isAdmin: admin, vendors: [], error: '' },
  };
}

test('the catalog says an admin is an admin — the flag the tab is shown on (D1)', async () => {
  const catalog = await fetchCatalog(SERVER, await sessionFor(ADMIN));
  const member = await fetchCatalog(SERVER, await sessionFor(`member@${DOMAIN}`));

  assert.ok(catalog.ok && member.ok, 'a catalog was refused');
  assert.strictEqual(catalog.value.isAdmin, true, `${ADMIN} is in Coai:Admins and the catalog does not say so`);
  assert.strictEqual(member.value.isAdmin, false);
});

test('an admin\'s company figures come through the real client, and a window change asks again', async () => {
  const token = await sessionFor(ADMIN);
  const { cache, asked } = realCache();

  await cache.refresh([target(token, 'company', 'week', true)]);
  await cache.refresh([target(token, 'company', 'year', true)]);

  assert.deepStrictEqual(asked, ['company/week', 'company/year'], 'a window change did not ask the server again');
  const week = cache.cell('contract', 'company', 'week')?.usage;
  const year = cache.cell('contract', 'company', 'year')?.usage;
  assert.ok(week !== undefined && year !== undefined, `an admin's company answer was not kept: ${cache.cell('contract', 'company', 'week')?.problem}`);
  assert.ok(Array.isArray(week.people) && Array.isArray(week.vendors), 'a company answer without people and vendors lists');
  assert.ok(typeof week.fromUtc === 'string' && typeof year.fromUtc === 'string');
  assert.ok(Date.parse(year.fromUtc!) < Date.parse(week.fromUtc!), 'Year answered the same range as Week');

  const pushed = teamTabPush({
    states: [state(true)], selection: withShown(NO_SELECTION, true),
    cell: (_server, window) => cache.cell('contract', 'company', window), asking: () => false, palette: () => 'var(--c)',
  });
  assert.strictEqual(pushed.admin, true);
  assert.ok(pushed.readUtc.length > 0, 'the tab cannot say when it read the answer');
});

test('a member asking for company is refused 403, and the cache keeps nothing and says why', async () => {
  // The catalog said admin a minute ago; the server's list changed since. The server's refusal is the decision.
  const token = await sessionFor(`member@${DOMAIN}`);
  const { cache } = realCache();

  await cache.refresh([target(token, 'company', 'week', true)]);

  const cell = cache.cell('contract', 'company', 'week');
  assert.strictEqual(cell?.refused, true, `a member's company request was not refused: ${JSON.stringify(cell)}`);
  assert.strictEqual(cell.usage, undefined);
  const status = teamParts(state(true), cell, false, () => 'var(--c)').status;
  assert.match(pageTree(status).text(), /no longer an admin here/);
});

test('the same body without what a newer server adds says what it needs — never a zero (D8)', async () => {
  const answer = await fetchUsage(SERVER, await sessionFor(ADMIN), 'week', 'company');
  if (!answer.ok) {
    assert.fail(`the company answer was refused: ${answer.status} — ${answer.message}`);
  }
  // As today's server answers, whatever this one answers: the fields a newer server adds, removed.
  const { daily: _daily, ...older } = answer.value;
  const parts = teamParts(state(true), { usage: older as Usage, problem: '', answeredAt: Date.now() }, false, () => 'var(--c)');
  const text = Object.values(parts).map((html) => pageTree(html).text()).join(' ');

  assert.match(text, /needs Team server ≥/);
  assert.doesNotMatch(text, /\$0(\.0+)?\b/);
});
