import { SNIPPET_VERSION } from '../claudeSnippet';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { panelHtml } from '../panelView';
import { ROLES } from '../prompts';
import {
  DEFAULTS,
  OVERLAID_SETTINGS,
  envBlock,
  overlaidReader,
  roleRecordUpdate,
  seedOverlay,
  settingWrite,
  settingsFrom,
} from '../settingsShape';
import { DEFAULT_VENDORS } from '../vendors';

/**
 * A setting reaches the place it is kept.
 *
 * <p>Reported from the panel: typing 3 into Architecture's <b>Rounds</b> did nothing. Switch to
 * another view and back and the old number returned, and the prompt pickers never changed count —
 * two symptoms of one defect. The input carried <code>data-vendor="Architecture"</code>, and the
 * provider reads <code>data-vendor</code> as A VENDOR: it mapped over the vendor list looking for
 * one with that id, found none, and wrote the vendor list back unchanged. <code>coai.rounds</code>
 * was never written at all.</p>
 *
 * <p>The cause is one attribute carrying two different KINDS of key with nothing to tell them
 * apart. A role is not a vendor; the routing is now a pure decision with three named outcomes, and
 * these tests are on the decision rather than on the `vscode` call it leads to.</p>
 */

const VENDOR_IDS = DEFAULT_VENDORS.map((v) => v.id);

test('a role-keyed setting is written into its record, not into the vendor list', () => {
  const write = settingWrite({ key: 'rounds', value: 3, role: 'Architecture' });

  assert.deepEqual(write, { kind: 'role', key: 'rounds', role: 'Architecture', value: 3 });
});

test('a vendor property is still written to the vendor it names', () => {
  const write = settingWrite({ key: 'model', value: 'gpt-5', vendor: 'codex' });

  assert.deepEqual(write, { kind: 'vendor', key: 'model', vendor: 'codex', value: 'gpt-5' });
});

test('a plain setting carries neither', () => {
  const write = settingWrite({ key: 'maxConcurrency', value: 4 });

  assert.deepEqual(write, { kind: 'plain', key: 'maxConcurrency', value: 4 });
});

test('a message with no key writes nothing at all', () => {
  // The webview is HTML: a control someone adds without a `data-setting` must not become a write
  // of `undefined` into the configuration.
  assert.equal(settingWrite({ key: undefined, value: 1 }), undefined);
});

test('no control that writes a role-keyed setting is labelled as a vendor', () => {
  // The regression, stated as the panel's own markup. `rounds` and `thresholds` are records keyed
  // by ROLE; every id in the panel that writes one must arrive as a role, because a role id in the
  // vendor slot silently addresses a vendor that cannot exist.
  const html = panelHtml({
    settings: DEFAULTS,
    vendors: DEFAULT_VENDORS,
    codexModels: [], agyModels: [],
    localEngines: {},
    server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
    side: '',
    perSide: false,
    latestServerVersion: '',
    questions: [],
    sessions: [],
    openSections: [],
    usage: [],
    usageWindow: 'week',
    cliStatus: {},
    modelPrices: {},
    snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
  }, 'nonce');

  const roleKeyed = [...html.matchAll(/data-setting="(rounds|thresholds)"[^>]*/g)].map((m) => m[0]);

  assert.ok(roleKeyed.length >= 8, `expected a rounds and a threshold input per role, found ${roleKeyed.length}`);
  for (const tag of roleKeyed) {
    const vendor = /data-vendor="([^"]*)"/.exec(tag)?.[1];
    assert.equal(
      vendor,
      undefined,
      `a role-keyed setting arrived as data-vendor="${vendor}" — the provider will look for a vendor by that name and write nothing`,
    );
    assert.ok(/data-role="[A-Za-z]+"/.test(tag), `no data-role on: ${tag}`);
  }
});

test('every role id the panel writes to is a role, and no vendor shares the name', () => {
  const html = panelHtml({
    settings: DEFAULTS,
    vendors: DEFAULT_VENDORS,
    codexModels: [], agyModels: [],
    localEngines: {},
    server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
    side: '',
    perSide: false,
    latestServerVersion: '',
    questions: [],
    sessions: [],
    openSections: [],
    usage: [],
    usageWindow: 'week',
    cliStatus: {},
    modelPrices: {},
    snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
  }, 'nonce');

  const roles = new Set([...html.matchAll(/data-role="([^"]+)"/g)].map((m) => m[1]!));

  // Against ROLES rather than a written-out list: this test is about the panel writing to a
  // REAL role and not colliding with a vendor id, and spelling the roles here made it a
  // second assertion about which roles exist — red the day one was added.
  assert.deepEqual([...roles].sort(), ROLES.map((r) => r.id).sort());
  for (const role of roles) {
    assert.ok(!VENDOR_IDS.includes(role), `${role} collides with a vendor id, which is how this defect hid`);
  }
});

test('writing one role keeps the other three', () => {
  const current = { PlanCritique: 3, Architecture: 2, SecurityReliability: 2, UxDxPerformance: 2 };

  assert.deepEqual(roleRecordUpdate(current, 'Architecture', 4), {
    PlanCritique: 3,
    Architecture: 4,
    SecurityReliability: 2,
    UxDxPerformance: 2,
  });
  assert.deepEqual(current.Architecture, 2, 'the stored record is not mutated');
});

test('the number of prompt pickers follows that role\u2019s rounds', () => {
  // The second half of the report: "the rounds count does not change the number of dropdowns". The
  // rendering was always right — it sized the pickers from `settings.rounds[role]` — and it was
  // reading a value nothing could change. Asserted here so it is guarded rather than inferred.
  const html = (rounds: Record<string, number>): string =>
    panelHtml({
      settings: { ...DEFAULTS, rounds },
      vendors: DEFAULT_VENDORS,
      codexModels: [], agyModels: [],
    localEngines: {},
      server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
      side: '',
      perSide: false,
      latestServerVersion: '',
      questions: [],
      sessions: [],
      openSections: [],
      usage: [],
      usageWindow: 'week',
    cliStatus: {},
    modelPrices: {},
    snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
    }, 'nonce');

  const pickers = (page: string, role: string): number =>
    [...page.matchAll(new RegExp(`data-prompt="${role}" data-round="\\d+"`, 'g'))].length;

  const four = html({ ...DEFAULTS.rounds, Architecture: 4 });
  assert.equal(pickers(four, 'Architecture'), 4);
  // Against the DEFAULT rather than a literal: what this line is about is that raising one role's
  // budget leaves the others alone, and writing the number here made it a second assertion about
  // what the default happens to be \u2014 which is how it went red when the defaults changed and
  // nothing about picker sizing did.
  assert.equal(
    pickers(four, 'SecurityReliability'),
    DEFAULTS.rounds['SecurityReliability'],
    'one role\u2019s budget is not another\u2019s',
  );

  const one = html({ ...DEFAULTS.rounds, UxDxPerformance: 1 });
  assert.equal(pickers(one, 'UxDxPerformance'), 1, 'a single round shows a single picker');

  // The clamp: six is the input's own maximum, and a stored value beyond it must not render a wall
  // of controls for rounds no stage will reach.
  const absurd = html({ ...DEFAULTS.rounds, Architecture: 99 });
  assert.equal(pickers(absurd, 'Architecture'), 6);
});

// ---------- a side of its own ----------

test('an overlaid setting comes from the side, and everything else from the shared settings', () => {
  // One machine, two companies: a Windows window on one subscription and a WSL distro on another,
  // needing different proxies, different CLI paths and different logins. VS Code hands the SAME
  // settings.json to both extension hosts, so the separation has to happen here.
  const shared = (section: string) => ({ maxConcurrency: 4, credsKey: 'shared-key' } as Record<string, unknown>)[section];
  const read = overlaidReader(shared, { credsKey: 'wsl-key' });

  assert.equal(read('credsKey'), 'wsl-key');
  assert.equal(read('maxConcurrency'), 4, 'what the side did not override is still shared');
});

test('a setting the overlay sets to a falsy value is still the overlay value', () => {
  // `?? shared(section)` would have read the shared value for `false`, `0` and `''` - which is
  // every switch somebody turns OFF on one side only.
  const shared = () => true;
  const read = overlaidReader(shared, { autonomous: false, credsKey: '' });

  assert.equal(read('autonomous'), false);
  assert.equal(read('credsKey'), '');
});

test('a setting added by a later version falls through instead of turning itself off', () => {
  // An overlay written by an older build does not mention a new setting; reading undefined for it
  // would disable a new feature on exactly the machines that had customised anything.
  const read = overlaidReader((section) => (section === 'splitWithFable' ? true : undefined), { credsKey: 'k' });

  assert.equal(read('splitWithFable'), true);
});

test('turning the switch on seeds the side with what it reads today, so nothing changes', () => {
  const shared = (section: string) =>
    ({ maxConcurrency: 7, credsKey: 'k', vendors: [{ id: 'codex' }] } as Record<string, unknown>)[section];

  const seeded = seedOverlay(shared);

  assert.deepEqual(settingsFrom(overlaidReader(shared, seeded)), settingsFrom(shared));
  assert.deepEqual(seeded['vendors'], [{ id: 'codex' }]);
  assert.ok(!('uiScale' in seeded), 'a text size belongs to the person, not to the company');
});

test('every setting the shape reads is one a side can hold', () => {
  // The seed copies a NAMED set, so a setting this list forgets stays silently shared - and the
  // person who set a different proxy on one side finds out when a review runs against the wrong
  // company's server. This test is the reminder.
  const asked: string[] = [];
  settingsFrom((section) => { asked.push(section); return undefined; });

  const missing = asked.filter((section) => !OVERLAID_SETTINGS.includes(section));
  assert.deepEqual(missing, [], 'these settings are read but cannot be held per side');
});

// ---------- which stages a vendor serves ----------

test('a vendor that reviews both stages says nothing about them in the env block', () => {
  // The block carries only what DIFFERS from the defaults, so a pristine configuration stays
  // readable and returning a box to ticked removes the key rather than pinning it.
  const both = DEFAULT_VENDORS.map((v) => ({ ...v, model: 'x' }));

  const written = envBlock(DEFAULTS, both)['COAI_VENDORS'] ?? '';

  assert.doesNotMatch(written, /"plan"/);
  assert.doesNotMatch(written, /"code"/);
});

test('a vendor narrowed to plans carries exactly that, and the other vendors stay silent', () => {
  const vendors = [
    { ...DEFAULT_VENDORS[0]!, model: 'gpt' },
    { id: 'local', runtime: 'local' as const, model: 'qwen', enabled: true, plan: true, code: false, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 },
  ];

  const written = JSON.parse(envBlock(DEFAULTS, vendors)['COAI_VENDORS'] ?? '[]') as Record<string, unknown>[];

  assert.deepEqual(written.map((v) => v['id']), ['codex', 'local']);
  assert.equal('plan' in written[0]!, false, 'codex reviews both, so it says nothing');
  assert.equal(written[1]!['code'], false);
  assert.equal('plan' in written[1]!, false, 'and it still reviews plans, so that stays silent too');
});
