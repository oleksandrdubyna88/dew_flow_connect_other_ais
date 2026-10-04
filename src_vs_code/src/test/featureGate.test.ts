import assert from 'node:assert/strict';
import { test } from 'node:test';

import { FEATURE_SINCE, featureNote, featureOnServer, reviewsFeatures } from '../featureGate';
import { serverSettingsJson } from '../serverSettingsFile';
import { DEFAULTS, envBlock, settingMessageFrom, settingWrite } from '../settingsShape';
import { DEFAULT_VENDORS, Vendor, vendorsFrom } from '../vendors';
import { vendorsEnv } from '../vendorsWire';
import { lastWrite, panelState, runPanel } from './panelPageHarness';

/**
 * The person's side of the feature gate: which vendors review a whole FEATURE (story S3.3 of
 * `todo/PLAN_feature_review.md`, §4.14).
 *
 * <p><b>Absent is NO</b>, unlike the plan and code ticks, because the server reads it that way
 * (`ProviderSettings.Feature`, S2.1: only a written `true` is yes) — a settings file written before the
 * stage existed must not start sending whole features to every vendor. <b>A Team server row is never
 * ticked</b> (D10): `ProviderSettings.Serves(FeatureReview)` is `Feature && !IsRemote`, so a tick there
 * would be a box the round ignores.</p>
 */

const TEAM: Vendor = {
  id: 'work-codex', runtime: 'remote', model: 'gpt-5.2', enabled: true, plan: true, code: true,
  baseUrl: 'https://coai.example.com', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0,
  remoteVendor: 'codex', teamServerId: 'work',
};
const OLDER = '0.38.0';
const KNOWN = (version: string) => ({ kind: 'known' as const, version, remembered: false, updateOffered: false });

function rowsOf(json: string): readonly { id: string; feature?: unknown }[] {
  return JSON.parse(json) as { id: string; feature?: unknown }[];
}

// ---------------------------------------------------------------- the stored row

test('a row that never said anything about features does not review them', () => {
  const [codex] = vendorsFrom([{ id: 'codex', runtime: 'codex', model: '' }]);

  assert.equal(reviewsFeatures(codex!), false);
  assert.ok(!('feature' in (codex as object)), 'coai.vendors is JSON a person reads; an absent tick stays absent');
});

test('a ticked row reviews features, and an unticked one is stored as absent rather than false', () => {
  const [ticked, unticked] = vendorsFrom([
    { id: 'codex', runtime: 'codex', model: '', feature: true },
    { id: 'antigravity', runtime: 'antigravity', model: '', feature: false },
  ]);

  assert.equal(ticked!.feature, true);
  assert.equal(reviewsFeatures(ticked!), true);
  assert.ok(!('feature' in (unticked as object)));
  assert.equal(reviewsFeatures(unticked!), false);
});

test('a Team server row is forced to false, whatever was stored', () => {
  const [team] = vendorsFrom([{ ...TEAM, feature: true }]);

  assert.equal(reviewsFeatures(team!), false);
  assert.ok(!('feature' in (team as object)), 'a tick the round would ignore is not kept');
  assert.equal(featureNote(team!, ''), 'Team servers do not run feature reviews yet.');
});

// ---------------------------------------------------------------- the file

test('the tick crosses to the server only when it is TRUE', () => {
  const vendors: Vendor[] = [{ ...DEFAULT_VENDORS[0]!, feature: true }, DEFAULT_VENDORS[1]!];
  const rows = rowsOf(vendorsEnv(vendors, FEATURE_SINCE));

  assert.equal(rows.find((row) => row.id === 'codex')?.feature, true);
  assert.ok(!('feature' in rows.find((row) => row.id === 'antigravity')!), 'absent is the server’s NO; false would be noise');
});

test('a Team server row carries no tick to the file even if one was forced into it', () => {
  const rows = rowsOf(vendorsEnv([{ ...TEAM, feature: true }], FEATURE_SINCE));

  assert.ok(!('feature' in rows[0]!));
});

test('against a server older than the stage the tick is kept out of the file, and the row still crosses', () => {
  const vendors: Vendor[] = [{ ...DEFAULT_VENDORS[0]!, feature: true }, DEFAULT_VENDORS[1]!];
  const rows = rowsOf(vendorsEnv(vendors, OLDER));

  assert.deepEqual(rows.map((row) => row.id), ['codex', 'antigravity'], 'the vendor keeps reviewing plans and code');
  assert.ok(!('feature' in rows[0]!), `coai-mcp ${OLDER} has no feature stage to be ticked for`);
  // Unknown is not old — every skew gate here stays silent on a server it cannot version.
  assert.equal(rowsOf(vendorsEnv(vendors, ''))[0]!.feature, true);
});

test('ticking features on the shipped reviewers is written to the file — it is not "the defaults"', () => {
  // `envBlock` writes COAI_VENDORS only when the list differs from the shipped one. Two default rows
  // with one tick added are NOT the defaults: dropping them would leave the server reading absent,
  // which is NO, while the card shows the box ticked.
  const vendors: Vendor[] = [{ ...DEFAULT_VENDORS[0]!, feature: true }, DEFAULT_VENDORS[1]!];
  const env = envBlock(DEFAULTS, vendors, FEATURE_SINCE);

  assert.ok(env['COAI_VENDORS'] !== undefined, 'the tick never reached the settings file');
  assert.equal(rowsOf(env['COAI_VENDORS']).find((row) => row.id === 'codex')?.feature, true);
  // And the settings file the sync writes carries it too.
  const written = JSON.parse(serverSettingsJson(DEFAULTS, vendors, '0.56.1', FEATURE_SINCE)) as Record<string, string>;
  assert.ok(written['COAI_VENDORS']!.includes('"feature":true'));
  // The pristine list still writes nothing.
  assert.equal(envBlock(DEFAULTS, DEFAULT_VENDORS, FEATURE_SINCE)['COAI_VENDORS'], undefined);
});

// ---------------------------------------------------------------- the version and the sentence

test('the stage ships in 0.39.0: 0.38.0 was released without it, and unknown is not old', () => {
  assert.equal(FEATURE_SINCE, '0.39.0');
  assert.equal(featureOnServer(OLDER), false, 'mcp-v0.38.0 shipped without the feature stage');
  assert.equal(featureOnServer(FEATURE_SINCE), true);
  assert.equal(featureOnServer('1.0.0'), true);
  assert.equal(featureOnServer(''), true);
});

test('the note names the installed version and the release — and says nothing when there is nothing to say', () => {
  const codex: Vendor = { ...DEFAULT_VENDORS[0]!, feature: true };

  assert.ok(featureNote(codex, OLDER).includes(OLDER));
  assert.ok(featureNote(codex, OLDER).includes(FEATURE_SINCE));
  assert.equal(featureNote(codex, FEATURE_SINCE), '');
  assert.equal(featureNote(codex, ''), '');
});

// ---------------------------------------------------------------- the card, RUN

test('every card carries a fourth switch, "reviews features", unticked unless the row said yes', () => {
  const vendors: Vendor[] = [{ ...DEFAULT_VENDORS[0]!, feature: true }, DEFAULT_VENDORS[1]!];
  const page = runPanel(panelState('reviewers', { vendors, server: KNOWN(FEATURE_SINCE) }));
  const boxes = page.controls.filter((one) => one.dataset['setting'] === 'feature');

  assert.deepEqual(boxes.map((one) => one.dataset['vendor']), ['codex', 'antigravity']);
  assert.deepEqual(boxes.map((one) => one.checked), [true, false]);
  assert.ok(boxes.every((one) => !one.disabled));
  assert.match(page.html, /reviews features/u);
});

test('changing the switch writes coai.vendors for that row', () => {
  const page = runPanel(panelState('reviewers', { server: KNOWN(FEATURE_SINCE) }));
  const box = page.controls.find((one) => one.dataset['setting'] === 'feature' && one.dataset['vendor'] === 'antigravity');
  assert.ok(box !== undefined);

  box.checked = true;
  box.fire('change');

  assert.deepEqual(settingWrite(settingMessageFrom(lastWrite(page))), { kind: 'vendor', key: 'feature', vendor: 'antigravity', value: true });
});

test('a Team server card has the switch switched off, and says why', () => {
  const page = runPanel(panelState('reviewers', { vendors: [...DEFAULT_VENDORS, TEAM], server: KNOWN(FEATURE_SINCE) }));
  const team = page.controls.find((one) => one.dataset['setting'] === 'feature' && one.dataset['vendor'] === 'work-codex');
  const codex = page.controls.find((one) => one.dataset['setting'] === 'feature' && one.dataset['vendor'] === 'codex');

  assert.ok(team !== undefined, 'the Team server card has no feature switch at all');
  assert.equal(team.disabled, true);
  assert.equal(team.checked, false);
  assert.equal(codex?.disabled, false, 'a local vendor’s switch is untouched by the Team server rule');
  assert.match(page.html, /Team servers do not run feature reviews yet/u);
});

test('against an older installed server the switch is off and the card names the release', () => {
  const page = runPanel(panelState('reviewers', { server: KNOWN(OLDER) }));
  const boxes = page.controls.filter((one) => one.dataset['setting'] === 'feature');

  assert.ok(boxes.length === 2 && boxes.every((one) => one.disabled));
  assert.ok(page.html.includes(FEATURE_SINCE), 'the card does not say which release runs feature reviews');
  // The other three stages are not the feature gate's business.
  assert.ok(page.controls.filter((one) => one.dataset['setting'] === 'plan').every((one) => !one.disabled));
});
