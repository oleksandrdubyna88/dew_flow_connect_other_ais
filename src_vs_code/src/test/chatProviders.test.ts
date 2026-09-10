import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ChatCatalog, chatProvidersFrom, legacyPick, openingModel, resolveChatPick } from '../chatModels';
import { Vendor } from '../vendors';
import { TeamServerState } from '../teamServerView';
import { requestBody } from '../remoteAsk';
import { serverVendorOf } from '../teamServers';

/**
 * Provider first, then model — and the provider is a ROW.
 *
 * <p><b>That is the whole design, and it was not the plan's.</b> The plan recommended resolving a
 * chosen runtime to "the first enabled row of that runtime". Three vendors' reviewers rejected that
 * independently on one round: two `codex` rows with different executables, prices or base URLs are
 * two different backends, and picking whichever comes first is a coin toss that bills the wrong one
 * with nothing on screen saying which. A fourth finding extended it to Team servers, where one
 * server hosts several vendor rows (`<server>-codex`, `<server>-claude`) and a server id alone
 * cannot say which vendor is to answer.</p>
 *
 * <p>So a provider IS a configured vendor row, local and remote alike, and resolution is a lookup by
 * row id rather than a search by runtime. Nothing here is ambiguous because nothing here searches.</p>
 */

function vendor(over: Partial<Vendor> = {}): Vendor {
  return {
    id: 'antigravity',
    runtime: 'antigravity',
    model: 'gemini-3.7-flash-high',
    enabled: true,
    plan: true,
    code: true,
    baseUrl: '',
    executablePath: '',
    pricePerMillionIn: 0,
    pricePerMillionOut: 0,
    ...over,
  };
}

/**
 * A Team server with a catalog, built without a cast.
 *
 * <p>`doctrine.md` §3 forbids the `as TeamServerState` that would make this three lines shorter: a
 * cast is a promise to keep a shape by hand, and it comes due silently the day the type gains a
 * required field and every fixture in the suite goes on compiling against a shape it no longer has.</p>
 */
function serverOffering(models: readonly string[]): TeamServerState {
  return {
    server: { id: 'srv1', name: 'Company', url: 'https://coai.example.com' },
    email: 'somebody@example.com',
    problem: '',
    stale: false,
    catalog: {
      serverVersion: '0.5.6',
      isAdmin: false,
      error: '',
      vendors: [
        { id: 'codex', runtime: 'codex', models, slots: { total: 1, ready: 1, coolingDown: 0, needsSignIn: 0 } },
      ],
    },
  };
}

const CATALOG: ChatCatalog = {
  discoveredCodex: [{ id: 'gpt-5.2', label: 'gpt-5.2' }, { id: 'gpt-5.2-mini', label: 'gpt-5.2-mini' }],
  discoveredAgy: [{ id: 'gemini-3.7-flash-high', label: 'gemini-3.7-flash-high' }],
  teamServers: [],
};

test('two rows on one runtime are two providers, told apart by their row id', () => {
  // The finding the whole design turns on. These are two different backends — a different executable
  // and a different price — and collapsing them into one `codex` provider would let a pick land on
  // either. (codex, gemini and the local reviewer, the plan round, independently.)
  const list = chatProvidersFrom(
    [
      vendor({ id: 'codex-cheap', runtime: 'codex', model: 'gpt-5.2-mini', pricePerMillionIn: 1 }),
      vendor({ id: 'codex-paid', runtime: 'codex', model: 'gpt-5.2', pricePerMillionIn: 20 }),
    ],
    CATALOG,
  );

  assert.deepStrictEqual(
    list.providers.map((provider) => provider.id),
    ['codex-cheap', 'codex-paid'],
    'two configured rows must be two providers, never one collapsed runtime',
  );
});

test('a provider carries the models its runtime can actually run', () => {
  const list = chatProvidersFrom([vendor({ id: 'codex', runtime: 'codex', model: 'gpt-5.2' })], CATALOG);

  assert.deepStrictEqual(
    list.providers[0]?.models.map((model) => model.id),
    ['gpt-5.2', 'gpt-5.2-mini'],
    'the model list is what `modelsFor` discovered for that runtime',
  );
});

test('a Team server row offers only what that server allows, under its own vendor name', () => {
  const remote = vendor({
    id: 'srv1-codex',
    runtime: 'remote',
    model: 'gpt-5.2',
    remoteVendor: 'codex',
    teamServerId: 'srv1',
    baseUrl: 'https://coai.example.com',
  });
  const list = chatProvidersFrom([remote], {
    ...CATALOG,
    teamServers: [serverOffering(['gpt-5.2', 'gpt-5.3'])],
  });

  assert.deepStrictEqual(
    list.providers[0]?.models.map((model) => model.id),
    ['gpt-5.2', 'gpt-5.3'],
    'a Team server provider offers its allowlist, looked up by the row\u2019s remoteVendor',
  );
});

test('a model the server has withdrawn is kept and marked, never silently dropped', () => {
  const remote = vendor({
    id: 'srv1-codex',
    runtime: 'remote',
    model: 'gpt-4.9',
    remoteVendor: 'codex',
    teamServerId: 'srv1',
  });
  const list = chatProvidersFrom([remote], {
    ...CATALOG,
    teamServers: [serverOffering(['gpt-5.2'])],
  });

  const withdrawn = list.providers[0]?.models.find((model) => model.id === 'gpt-4.9');
  assert.notStrictEqual(withdrawn, undefined, 'the saved model must still be there');
  assert.match(
    withdrawn?.label ?? '',
    /does not offer it any more/,
    'a withdrawn model is MARKED — silently swapping it would send a round nobody chose',
  );
});

test('a row on a runtime with no adapter is refused by name, not hidden', () => {
  const list = chatProvidersFrom([vendor({ id: 'ollama', runtime: 'local' })], CATALOG);

  assert.deepStrictEqual(list.providers, [], 'a runtime with no chat adapter offers nothing');
  assert.strictEqual(list.refused[0]?.id, 'ollama');
  assert.match(list.refused[0]?.reason ?? '', /ollama/, 'the refusal must name the row');
});

test('a disabled row is neither offered nor refused — it is not configured', () => {
  const list = chatProvidersFrom([vendor({ id: 'off', enabled: false })], CATALOG);

  assert.deepStrictEqual(list.providers, []);
  assert.deepStrictEqual(list.refused, []);
});

test('resolving a pair returns the row that says HOW to run it', () => {
  const paid = vendor({ id: 'codex-paid', runtime: 'codex', model: 'gpt-5.2', executablePath: 'C:/paid/codex.cmd' });
  const vendors = [vendor({ id: 'codex-cheap', runtime: 'codex', model: 'gpt-5.2-mini' }), paid];
  const list = chatProvidersFrom(vendors, CATALOG);

  const picked = resolveChatPick(vendors, list, 'codex-paid', 'gpt-5.2-mini');

  assert.strictEqual(picked.ok, true);
  assert.strictEqual(picked.ok === true ? picked.row.id : '', 'codex-paid', 'the ROW is the one named');
  assert.strictEqual(
    picked.ok === true ? picked.row.executablePath : '',
    'C:/paid/codex.cmd',
    'the row carries the executable, the price and the base URL — that is what resolution is for',
  );
  assert.strictEqual(
    picked.ok === true ? picked.model : '',
    'gpt-5.2-mini',
    'the chosen model, not the row\u2019s configured one',
  );
});

test('a resolved Team-server pair carries the vendor name that server knows', () => {
  // The field this family lost once for three releases. The row id is `<server>-<vendor>`; what goes
  // on the wire is the vendor's own name, and a resolution that dropped it would reproduce a 400
  // reading "'srv1-codex' is not a vendor here", in seconds, for no tokens.
  const remote = vendor({
    id: 'srv1-codex',
    runtime: 'remote',
    model: 'gpt-5.2',
    remoteVendor: 'codex',
    teamServerId: 'srv1',
  });
  const list = chatProvidersFrom([remote], {
    ...CATALOG,
    teamServers: [serverOffering(['gpt-5.2'])],
  });

  const picked = resolveChatPick([remote], list, 'srv1-codex', 'gpt-5.2');

  assert.strictEqual(picked.ok, true);
  assert.strictEqual(picked.ok === true ? picked.row.remoteVendor : '', 'codex');
});

test('a model valid under ANOTHER provider is refused, because the pair is checked as a pair', () => {
  // The security half. A stale or tampered choice of a Claude model against the `agy` row would
  // otherwise pass a model-only membership check and route a Claude model through the wrong CLI,
  // which `vendor-routing.md` forbids by name. (codex, the plan round.)
  const vendors = [
    vendor({ id: 'claude', runtime: 'claude', model: 'sonnet' }),
    vendor({ id: 'agy', runtime: 'antigravity', model: 'gemini-3.7-flash-high' }),
  ];
  const list = chatProvidersFrom(vendors, CATALOG);

  const picked = resolveChatPick(vendors, list, 'agy', 'sonnet');

  assert.strictEqual(picked.ok, false, 'a model this provider does not offer must not resolve');
  assert.match(
    picked.ok === false ? picked.refusal : '',
    /sonnet/,
    'the refusal names the model that was asked for',
  );
});

test('a pick naming a provider that is not configured is refused by that name', () => {
  const vendors = [vendor({ id: 'claude', runtime: 'claude', model: 'sonnet' })];
  const list = chatProvidersFrom(vendors, CATALOG);

  const picked = resolveChatPick(vendors, list, 'codex-gone', 'gpt-5.2');

  assert.strictEqual(picked.ok, false);
  assert.match(picked.ok === false ? picked.refusal : '', /codex-gone/);
});

test('a legacy chatModel naming a ROW resolves to that row and its configured model', () => {
  // What every existing installation has in `settings.json` today: one string, the id of a reviewer
  // row. It must go on working without anybody re-picking anything.
  const vendors = [vendor({ id: 'codex', runtime: 'codex', model: 'gpt-5.2' })];
  const list = chatProvidersFrom(vendors, CATALOG);

  assert.deepStrictEqual(legacyPick(list, vendors, 'codex'), { providerId: 'codex', modelId: 'gpt-5.2', candidates: [] });
});

test('a legacy value naming a MODEL resolves only when exactly one provider offers it', () => {
  // Ambiguity is refused rather than guessed: `sonnet` offered by a local Claude row AND by a Team
  // server is not a choice this code may make on somebody's behalf. (codex, the plan round.)
  const only = [vendor({ id: 'codex', runtime: 'codex', model: 'gpt-5.2' })];
  const onlyList = chatProvidersFrom(only, CATALOG);
  assert.deepStrictEqual(
    legacyPick(onlyList, only, 'gpt-5.2-mini'),
    { providerId: 'codex', modelId: 'gpt-5.2-mini', candidates: [] },
    'one provider offers it, so there is nothing to guess',
  );

  const both = [
    vendor({ id: 'codex-a', runtime: 'codex', model: 'gpt-5.2' }),
    vendor({ id: 'codex-b', runtime: 'codex', model: 'gpt-5.2' }),
  ];
  const bothList = chatProvidersFrom(both, CATALOG);
  assert.deepStrictEqual(
    legacyPick(bothList, both, 'gpt-5.2-mini'),
    { providerId: '', modelId: 'gpt-5.2-mini', candidates: ['codex-a', 'codex-b'] },
    'two providers offer it, so the person must say which — the model is kept, stranded',
  );
});

test('an empty setting IS the first provider that can answer, which is what the picker promises', () => {
  // THE GUARANTEE CHANGED, because the old one was never kept. This test used to assert the SHAPE
  // `{providerId: '', ...}` under the title "so the first provider can answer as it always did" — and
  // nothing downstream made that true: `resolveChatPick(vendors, list, '', '')` finds no provider
  // with an empty id and refuses with `" is not a model this conversation can be sent to any more"`,
  // a sentence with a leading space and no name in it. `coai.chatModel` is EMPTY by default and the
  // panel labels that option "The first one that can answer", so a fresh installation pressing the
  // keybinding got that refusal. Found by the code round (gemini) while it was asking a narrower
  // question about `chatModelName`.
  const vendors = [
    vendor({ id: 'codex', runtime: 'codex', model: 'gpt-5.2' }),
    vendor({ id: 'agy', runtime: 'antigravity', model: 'gemini-3.7-flash-high' }),
  ];
  const list = chatProvidersFrom(vendors, CATALOG);

  assert.deepStrictEqual(legacyPick(list, vendors, ''), { providerId: 'codex', modelId: 'gpt-5.2', candidates: [] });
  assert.strictEqual(resolveChatPick(vendors, list, 'codex', 'gpt-5.2').ok, true, 'the resolved pair is still refused');
});

test('with nothing configured at all there is no first provider to invent', () => {
  assert.deepStrictEqual(legacyPick({ providers: [], refused: [] }, [], ''), { providerId: '', modelId: '', candidates: [] });
});

test('a model of ANOTHER vendor is not offered by this provider — vendor-routing.md is MANDATORY', () => {
  // The antigravity subscription bundles Gemini, Claude and GPT-OSS behind one CLI, so
  // `claude-sonnet-4-6` is selectable there — and `vendor-routing.md` forbids selecting it there by
  // name: the same model sits on an unlimited Claude subscription while every agy call is drawn
  // against a quota that is neither unlimited nor cheap. That rule cost three cells of the
  // 2026-09-01 comparison campaign to learn, and the waste was invisible in the results.
  const vendors = [vendor({ id: 'agy', runtime: 'antigravity', model: 'gemini-3.7-flash-high' })];
  const list = chatProvidersFrom(vendors, {
    ...CATALOG,
    discoveredAgy: [
      { id: 'gemini-3.8-flash-low', label: 'Gemini 3.8 Flash (Low)' },
      { id: 'claude-sonnet-4-6', label: 'Claude Sonnet 4.6 (Thinking)' },
    ],
  });
  const offered = list.providers[0]!.models.map((one) => one.id);

  assert.ok(offered.includes('gemini-3.8-flash-low'), 'the row lost the models it is actually for');
  assert.ok(!offered.includes('claude-sonnet-4-6'), 'a Claude model is offered through agy, which the routing rule forbids');
});

test('the rule does not disarm the CLI a model belongs to', () => {
  const vendors = [vendor({ id: 'claude', runtime: 'claude', model: 'sonnet' })];
  const list = chatProvidersFrom(vendors, CATALOG);

  assert.ok(list.providers[0]!.models.some((one) => one.id === 'sonnet'), 'the claude row lost its own models');
});

/**
 * What actually goes on the wire for a resolved Team-server pick.
 *
 * <p>The gate refused a contract case that only asserts the server ACCEPTS the request, and it was
 * right: a request can be accepted while the field is missing, empty, or carrying the prefixed row
 * id, and a stricter server would then reject it later for a reason nobody could trace. So the VALUE
 * is asserted, at the point where the row becomes the payload, for both ways the value can be
 * arrived at. (codex, gemini and the local reviewer, the plan round.)</p>
 *
 * <p>This is the field this family lost once for three releases, with both suites green.</p>
 */

test('a resolved remote pick puts the server\u2019s own vendor name on the wire, when the row records one', () => {
  const row = vendor({ id: 'srv1-codex', runtime: 'remote', model: 'gpt-5.2', remoteVendor: 'codex' });

  const body = requestBody(serverVendorOf(row, 'srv1'), 'gpt-5.3', 'explain this', 60);

  assert.strictEqual(body['vendor'], 'codex', 'the wire carries the vendor name, never the row id');
  assert.strictEqual(body['model'], 'gpt-5.3', 'and the CHOSEN model, not the row\u2019s configured one');
});

test('a remote row with no recorded vendor falls back to its id with the server prefix stripped', () => {
  // The rows written before `remoteVendor` existed. They must keep working, and the fallback is the
  // half a test that only covered the recorded case would never have exercised.
  const row = vendor({ id: 'srv1-claude', runtime: 'remote', model: 'sonnet' });

  const body = requestBody(serverVendorOf(row, 'srv1'), 'sonnet', 'explain this', 60);

  assert.strictEqual(body['vendor'], 'claude');
});

test('the wire never carries the prefixed row id, which is the 400 this family has already paid for', () => {
  const row = vendor({ id: 'srv1-codex', runtime: 'remote', model: 'gpt-5.2', remoteVendor: 'codex' });

  const body = requestBody(serverVendorOf(row, 'srv1'), 'gpt-5.2', 'explain this', 60);

  assert.notStrictEqual(
    body['vendor'],
    'srv1-codex',
    'sending the row id is the measured failure: a 400 saying it is not a vendor here, for no tokens',
  );
});

test('a provider that is switched off says so, instead of reading as one that is gone', () => {
  // Two states a person can act on in different ways, and `chatProvidersFrom` filters both out of
  // its lists — correctly, since a disabled row is configured for nothing. Without this they arrive
  // at the refusal indistinguishable. (gemini, the code round.)
  const off = vendor({ id: 'codex', runtime: 'codex', model: 'gpt-5.2', enabled: false });
  const list = chatProvidersFrom([off], CATALOG);

  const picked = resolveChatPick([off], list, 'codex', 'gpt-5.2');

  assert.strictEqual(picked.ok, false);
  assert.match(
    picked.ok === false ? picked.refusal : '',
    /switched off/,
    'a row that is merely off must not be reported as one that no longer exists',
  );
});

test('a provider that never existed still reads as gone, not as switched off', () => {
  const list = chatProvidersFrom([vendor({ id: 'codex', runtime: 'codex' })], CATALOG);

  const picked = resolveChatPick([], list, 'never-was', 'gpt-5.2');

  assert.strictEqual(picked.ok, false);
  assert.doesNotMatch(picked.ok === false ? picked.refusal : '', /switched off/);
});

test('an ambiguous legacy model names the providers that offer it', () => {
  // So the caller can ask "which of these two?" rather than "pick something". (gemini.)
  const both = [
    vendor({ id: 'codex-a', runtime: 'codex', model: 'gpt-5.2' }),
    vendor({ id: 'codex-b', runtime: 'codex', model: 'gpt-5.2' }),
  ];
  const list = chatProvidersFrom(both, CATALOG);

  assert.deepStrictEqual(legacyPick(list, both, 'gpt-5.2-mini').candidates, ['codex-a', 'codex-b']);
});

test('a legacy value nobody offers strands with no candidates to suggest', () => {
  const vendors = [vendor({ id: 'codex', runtime: 'codex', model: 'gpt-5.2' })];
  const list = chatProvidersFrom(vendors, CATALOG);

  assert.deepStrictEqual(
    legacyPick(list, vendors, 'a-model-that-went-away'),
    { providerId: '', modelId: 'a-model-that-went-away', candidates: [] },
  );
});

/**
 * Which model a NEW tab opens with, when the panel has named one beside the provider.
 *
 * <p>The tab has held a pair since PR #175; the panel held only the row, so `coai.chatModelName` is
 * the second half of the panel's own choice. It is a lookup with a fallback rather than a second
 * source of truth: a name the chosen provider does not offer is not a pick, and the row's own model
 * answers instead — the same rule `resolveChatPick` applies one step later, applied here so a stale
 * settings value opens a working conversation rather than a refusal.</p>
 */
test('a model named beside the provider is what a new tab opens with', () => {
  const vendors = [vendor({ id: 'codex-paid', runtime: 'codex', model: 'gpt-5.2' })];
  const list = chatProvidersFrom(vendors, CATALOG);
  const saved = legacyPick(list, vendors, 'codex-paid');

  assert.strictEqual(openingModel(list, saved, 'gpt-5.2-mini'), 'gpt-5.2-mini');
});

test('a named model the provider does not offer falls back to the row’s own, not to a refusal', () => {
  // `settings.json` is edited by hand and a Team server withdraws models; either way the value can
  // name something this provider has never offered. The conversation still opens.
  const vendors = [vendor({ id: 'codex-paid', runtime: 'codex', model: 'gpt-5.2' })];
  const list = chatProvidersFrom(vendors, CATALOG);
  const saved = legacyPick(list, vendors, 'codex-paid');

  assert.strictEqual(openingModel(list, saved, 'sonnet'), 'gpt-5.2');
  assert.strictEqual(openingModel(list, saved, ''), 'gpt-5.2');
});

test('a name cannot reach a provider other than the one that was chosen', () => {
  // The pair is checked as a pair everywhere else in this feature, and `vendor-routing.md` forbids a
  // Claude model going through `agy` by name. A name offered by SOMEBODY is not a name offered here.
  const vendors = [
    vendor({ id: 'agy-row', runtime: 'antigravity', model: 'gemini-3.7-flash-high' }),
    vendor({ id: 'claude-row', runtime: 'claude', model: 'sonnet' }),
  ];
  const list = chatProvidersFrom(vendors, CATALOG);
  const saved = legacyPick(list, vendors, 'agy-row');

  assert.strictEqual(openingModel(list, saved, 'sonnet'), 'gemini-3.7-flash-high');
});
