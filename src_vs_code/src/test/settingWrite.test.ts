import assert from 'node:assert/strict';
import { test } from 'node:test';
import { panelHtml } from '../panelView';
import { DEFAULTS, roleRecordUpdate, settingWrite } from '../settingsShape';
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
    codexModels: [],
    serverInstalled: false,
    serverVersion: '',
    latestServerVersion: '',
    questions: [],
    sessions: [],
    openSections: [],
    usage: [],
    usageWindow: 'week',
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
    codexModels: [],
    serverInstalled: false,
    serverVersion: '',
    latestServerVersion: '',
    questions: [],
    sessions: [],
    openSections: [],
    usage: [],
    usageWindow: 'week',
  }, 'nonce');

  const roles = new Set([...html.matchAll(/data-role="([^"]+)"/g)].map((m) => m[1]!));

  assert.deepEqual([...roles].sort(), ['Architecture', 'PlanCritique', 'SecurityReliability', 'UxDxPerformance']);
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
      codexModels: [],
      serverInstalled: false,
      serverVersion: '',
      latestServerVersion: '',
      questions: [],
      sessions: [],
      openSections: [],
      usage: [],
      usageWindow: 'week',
    }, 'nonce');

  const pickers = (page: string, role: string): number =>
    [...page.matchAll(new RegExp(`data-prompt="${role}" data-round="\\d+"`, 'g'))].length;

  const four = html({ ...DEFAULTS.rounds, Architecture: 4 });
  assert.equal(pickers(four, 'Architecture'), 4);
  assert.equal(pickers(four, 'SecurityReliability'), 2, 'one role\u2019s budget is not another\u2019s');

  const one = html({ ...DEFAULTS.rounds, UxDxPerformance: 1 });
  assert.equal(pickers(one, 'UxDxPerformance'), 1, 'a single round shows a single picker');

  // The clamp: six is the input's own maximum, and a stored value beyond it must not render a wall
  // of controls for rounds no stage will reach.
  const absurd = html({ ...DEFAULTS.rounds, Architecture: 99 });
  assert.equal(pickers(absurd, 'Architecture'), 6);
});
