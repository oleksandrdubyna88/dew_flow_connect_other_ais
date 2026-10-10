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

import { ask, createSession, fetchCatalog, fetchPeople, fetchUsage, Usage } from '../teamServerApi';
import { personCard } from '../teamPeople';
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
    people: () => undefined, peopleAsking: () => false, price: () => undefined, now: Date.now(),
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
  const { daily: _daily, ...rest } = answer.value;
  const strip = (rows: Usage['vendors']): Usage['vendors'] => rows.map(({ models: _models, ...row }) => row);
  const older: Usage = { ...rest, vendors: strip(rest.vendors), people: (rest.people ?? []).map((one) => ({ ...one, vendors: strip(one.vendors) })) };
  const parts = teamParts(state(true), { usage: older, problem: '', answeredAt: Date.now() }, false, () => 'var(--c)');
  const text = Object.values(parts).map((html) => pageTree(html).text()).join(' ');

  assert.match(text, /needs Team server ≥/);
  assert.doesNotMatch(text, /\$0(\.0+)?\b/);
});

// ---------- epic 3: who is signed in, and per-model and per-day figures, through the real client ----------

/** An identity token with a display name, as an identity provider sends one. */
function namedToken(email: string, name: string): string {
  const part = (o: object) => Buffer.from(JSON.stringify(o)).toString('base64url');
  const head = part({ alg: 'HS256', typ: 'JWT' });
  const body = part({ iss: 'coai-local', email, name, exp: Math.floor(Date.now() / 1000) + 600 });

  return `${head}.${body}.${createHmac('sha256', KEY).update(`${head}.${body}`).digest('base64url')}`;
}

/** Whether the runner wrote spending into this server's ledger (scripts/run-contract.mjs, seedLedger). */
const SEEDED = (process.env['COAI_CONTRACT_SEEDED'] ?? '') === '1';

test('an admin is told who is signed in — three fields a row, its own name among them; a member is refused 403', async () => {
  const signedIn = await createSession(SERVER, namedToken(ADMIN, 'Contract Admin'));
  assert.ok(signedIn.ok, `the admin could not sign in: ${signedIn.ok ? '' : signedIn.message}`);

  const raw = await ask<unknown>(SERVER, 'api/people', { token: signedIn.value.token });
  if (!raw.ok) {
    assert.fail(`the people listing was refused an admin: ${raw.status} — ${raw.message}`);
  }
  assert.ok(Array.isArray(raw.value) && raw.value.length > 0, 'an admin who has just signed in is not listed — the check would be vacuous');
  for (const row of raw.value as Record<string, unknown>[]) {
    assert.deepStrictEqual(Object.keys(row).sort(), ['displayName', 'email', 'lastUsedUtc'], `a row carries more than the three fields: ${JSON.stringify(row)}`);
  }
  const listed = await fetchPeople(SERVER, signedIn.value.token);
  assert.ok(listed.ok);
  const me = listed.value.find((one) => one.email.toLowerCase() === ADMIN.toLowerCase());
  assert.strictEqual(me?.displayName, 'Contract Admin');
  assert.match(pageTree(personCard({ usage: { email: ADMIN, vendors: [] }, name: me.displayName, lastUsedUtc: me.lastUsedUtc },
    { serverId: 'contract', price: () => undefined, palette: () => 'var(--c)', now: Date.now() })).text(), /last seen within the hour/,
  'a session used a moment ago does not read as seen within the hour');

  const member = await fetchPeople(SERVER, await sessionFor(`member@${DOMAIN}`));
  assert.strictEqual(member.ok ? 200 : member.status, 403, 'a member was shown who is signed in');
});

test('the company answer names each model under its vendor, lower-cases vendor ids, and counts 30 dense UTC days', async (t) => {
  if (!SEEDED) {
    t.skip('this server\'s ledger was not seeded by scripts/run-contract.mjs, so there are no models or days to read');
    return;
  }
  const answer = await fetchUsage(SERVER, await sessionFor(ADMIN), 'week', 'company');
  if (!answer.ok) {
    assert.fail(`the company answer was refused: ${answer.status} — ${answer.message}`);
  }
  const usage = answer.value;

  assert.deepStrictEqual(usage.vendors.map((one) => one.vendor).sort(), ['codex', 'gemini'], 'a vendor id written "Codex" was not lower-cased');
  const codex = usage.vendors.find((one) => one.vendor === 'codex');
  assert.deepStrictEqual(codex?.models?.map((one) => [one.model, one.runs]), [['gpt-5.6-sol', 2]]);
  assert.deepStrictEqual(usage.vendors.find((one) => one.vendor === 'gemini')?.models?.map((one) => one.model), [''],
    'a line with no model is grouped under "" — unknown');
  const daily = usage.daily;
  assert.ok(daily !== undefined, 'a newer server sent no daily series');
  assert.strictEqual(daily.days.length, 30);
  assert.ok(daily.days.every((day) => /^\d{4}-\d{2}-\d{2}$/.test(day.day)), 'a day is not a yyyy-MM-dd string');
  assert.ok(daily.days.every((day) => day.vendors.length === 2), 'the series is not dense: a day lacks a vendor');
  const sum = (vendor: string) => daily.days.reduce((s, day) => s + (day.vendors.find((one) => one.vendor === vendor)?.runs ?? 0), 0);
  assert.deepStrictEqual([sum('codex'), sum('gemini')], [2, 1], 'the chart\'s days do not add up to the launches the ledger holds');

  // And the tab draws it: a chart, names, prices at a list rate, and nothing saying a newer server is needed.
  const parts = teamParts(state(true), { usage, problem: '', answeredAt: Date.now() }, false, () => 'var(--c)', {
    price: (model) => (model === 'gpt-5.6-sol' ? { inPerMillion: 2, outPerMillion: 10, source: 'openrouter' } : undefined),
    now: Date.now(),
  });
  assert.ok(pageTree(parts.chart).find((node) => node.tagName === 'RECT').length > 0, 'the real daily series drew no bars');
  assert.doesNotMatch(Object.values(parts).map((html) => pageTree(html).text()).join(' '), /needs Team server/);
});
