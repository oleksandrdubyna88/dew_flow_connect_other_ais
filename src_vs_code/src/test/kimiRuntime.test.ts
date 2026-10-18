import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';

import { catalogHtml } from '../catalogPage';
import { rowWriteRefusal } from '../catalogWriteRules';
import { CHAT_RUNTIMES } from '../cliChatLaunch';
import { versionFrom, versionSourceFor } from '../cliVersions';
import { CONSULTING_RUNTIMES } from '../consultSettings';
import { CLI_RUNTIMES, KIMI_MODELS, KIMI_MODELS_SEEN_ON, RUNTIMES, modelsFor, modelsProvenance } from '../models';
import { cardContextFor, type PanelState } from '../panelView';
import { NO_NOTES, type ProviderHealth } from '../providers';
import { KIMI_RUNTIME_SINCE, runtimeOnServer, runtimeSkewNote } from '../runtimeSince';
import { serverSettingsJson } from '../serverSettingsFile';
import { DEFAULTS } from '../settingsShape';
import { cliGroups } from '../setupTab';
import { vendorPalette, VENDOR_PALETTE } from '../vendorColour';
import { vendorTerminal } from '../vendorTerminal';
import { DEFAULT_VENDORS, VENDOR_PRESETS, type Vendor, vendorsFrom } from '../vendors';
import { vendorsEnv } from '../vendorsWire';
import { panelState, runPanel } from './panelPageHarness';
import { pageTree } from './pageTree';

/**
 * The `kimi` runtime on the panel's side — E4.5 of todo/PLAN_vibe_and_kimi_runtimes_E4.md: Moonshot's own CLI on the
 * person's own Kimi Code plan, signed in as itself, run by coai-mcp's `KimiRuntime` over ACP (`module_runners.md`).
 *
 * <p>The same two ways it goes wrong silently as Vibe's (`vibeRuntime.test.ts`): a runtime the parser does not know comes
 * back as `codex` under its own name, and an older `coai-mcp` that does not know it never reviews with it. Everything
 * below is one of those two, or a button that would install or run the wrong software.</p>
 */

const KIMI: Vendor = {
  id: 'kimi', runtime: 'kimi', model: 'kimi-code/k3-256k', enabled: true, plan: true, code: true,
  baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0,
};

/**
 * A `coai-mcp` that is RELEASED without the runtime and is the newest the gate must hold the row back from: mcp 0.56.0, the release `main` will cut next
 * (it already holds two `feat` commits after `mcp-v0.55.0`) — Kimi lands in the one after, `KIMI_RUNTIME_SINCE`. 0.55.0, the newest tag, is below it too.
 */
const OLDER = '0.56.0';
const KNOWN = (version: string) => ({ kind: 'known' as const, version, remembered: false, updateOffered: false });

const repo = (...parts: string[]): string => join(__dirname, '..', '..', '..', ...parts);

function rowsOf(json: string): readonly { id: string; runtime: string }[] {
  return JSON.parse(json) as { id: string; runtime: string }[];
}

// ---------------------------------------------------------------- S5.1 the runtime survives being read

test('aKimiRowSurvivesParsing: a saved kimi row comes back as kimi, never as a codex row under its own name', () => {
  assert.ok((RUNTIMES as readonly string[]).includes('kimi'), 'RUNTIMES has no kimi — the parser would rewrite it to codex');

  const [vendor] = vendorsFrom([{ id: 'kimi', runtime: 'kimi', model: 'kimi-code/k3', enabled: true }]);

  assert.equal(vendor!.runtime, 'kimi');
  assert.equal(vendor!.model, 'kimi-code/k3');
});

test('kimi is a CLI on this machine, and neither a consultant (E4.4 seed) nor a chat partner', () => {
  assert.ok(CLI_RUNTIMES.includes('kimi'), 'CLI_RUNTIMES has no kimi — no ▶ ⤓ ⟳ and no CLI badge');
  assert.ok(!(CHAT_RUNTIMES as readonly string[]).includes('kimi'));
  assert.ok(!(CONSULTING_RUNTIMES as readonly string[]).includes('kimi'), 'the seed names kimi as no consultant (E4.4) — S5 adds it');
});

// ---------------------------------------------------------------- S5.2 / S5.3 the models (D10)

test('kimiOffersItsCuratedListAndNeverGeminis: the four ids in order, a typed id kept', () => {
  assert.deepEqual(KIMI_MODELS.map((one) => one.id),
    ['kimi-code/k3-256k', 'kimi-code/k3', 'kimi-code/kimi-for-coding', 'kimi-code/kimi-for-coding-highspeed']);

  const offered = modelsFor('kimi', [], '');
  assert.deepEqual(offered.map((one) => one.id), KIMI_MODELS.map((one) => one.id));
  assert.ok(!offered.some((one) => one.id.startsWith('gemini')), 'an unknown runtime used to fall to the Gemini list');

  const typed = modelsFor('kimi', [], 'kimi-code/future');
  assert.equal(typed[0]!.id, 'kimi-code/future', 'a saved choice never vanishes from its own dropdown');
});

test('the labels say what each model is known for — the probe, the preview, and the plan that may refuse the fast one', () => {
  const label = (id: string): string => KIMI_MODELS.find((one) => one.id === id)?.label ?? '';

  assert.match(label('kimi-code/k3-256k'), /K3-256k.*64 paid turns/u);
  assert.match(label('kimi-code/k3'), /^K3/u);
  assert.match(label('kimi-code/kimi-for-coding'), /K2\.8 Preview/u);
  assert.match(label('kimi-code/kimi-for-coding-highspeed'), /401/u, 'the plan may answer it 401 — the label must not promise it runs');
});

test('the caption says the list is curated, which Kimi it was seen on, that other ids are refused, and why', () => {
  const caption = modelsProvenance('kimi', []);

  assert.match(caption, /curated/u);
  assert.ok(caption.includes(KIMI_MODELS_SEEN_ON) && KIMI_MODELS_SEEN_ON.length > 0, 'D10: the list is version-labelled');
  assert.match(caption, /kimi-code\//u, 'the rule coai-mcp refuses by is named');
  assert.match(caption, /refuse/u, 'an id outside the list is refused before the launch — the caption must not promise it runs');
  assert.match(caption, /API key/u, 'a moonshot-ai/* id would bill the person’s own key — that is why');
  assert.doesNotMatch(caption, /Codex CLI|Gemini/u);
});

test('theCuratedListMatchesTheServers: the same ids, in the same order, seen on the same Kimi', () => {
  // reads-another-program: the server's list IS shared/kimi-model-blocks.json (KimiModels.Curated is its ids, in order —
  // KimiModels.cs), so a renamed, dropped or reordered id there fails here loudly instead of the panel offering a model
  // every round would refuse. It derives nothing from the file but the claim it checks.
  const table = JSON.parse(readFileSync(repo('shared', 'kimi-model-blocks.json'), 'utf8')) as { seenOn: string; blocks: { id: string }[] };
  const server = readFileSync(repo('src_mcp', 'runners', 'Kimi', 'KimiModels.cs'), 'utf8');

  assert.ok(table.blocks.length > 0, 'an empty table would pass the comparison having checked nothing');
  assert.deepEqual(KIMI_MODELS.map((one) => one.id), table.blocks.map((block) => block.id));
  assert.equal(KIMI_MODELS_SEEN_ON, table.seenOn, 'the caption names the Kimi version the server list was seen on');
  assert.ok(server.includes(`Default = "${KIMI_MODELS[0]!.id}"`), 'the first curated model is what coai-mcp launches a row that names none on');
});

// ---------------------------------------------------------------- S5.4 the preset (D11)

test('thePresetRoundTrips: Add a model offers Kimi Code on its first curated model, never as a default reviewer', () => {
  const preset = VENDOR_PRESETS.find((one) => one.runtime === 'kimi');

  assert.ok(preset !== undefined, 'no kimi preset — nobody could add the row from the panel');
  assert.equal(preset.id, 'kimi');
  assert.equal(preset.label, 'Kimi Code');
  assert.equal(preset.model, KIMI_MODELS[0]!.id, 'the preset starts on the first curated model');
  assert.equal(preset.baseUrl, '', 'Kimi signs in as itself: no endpoint, no key');
  assert.ok(preset.plan && preset.code);
  assert.ok(!DEFAULT_VENDORS.some((one) => one.runtime === 'kimi'), 'D11: presets only — never a default reviewer');

  const [back] = vendorsFrom([{ ...preset }]);
  assert.equal(back!.runtime, 'kimi', 'the preset survives its own round trip through the parser');
  assert.equal(back!.id, 'kimi');
  assert.equal(back!.model, preset.model);
  assert.equal(back!.plan, true);
  assert.equal(back!.code, true);
});

test('the preset’s hint names the release, the sign-in, Windows and WSL2, and the reviewers that run one after another', () => {
  const hint = VENDOR_PRESETS.find((one) => one.runtime === 'kimi')?.hint ?? '';

  assert.ok(KIMI_RUNTIME_SINCE.length > 0 && hint.includes(KIMI_RUNTIME_SINCE), `the hint does not name coai-mcp ${KIMI_RUNTIME_SINCE}: ${hint}`);
  assert.match(hint, /kimi login/u);
  assert.doesNotMatch(hint, /Windows only/u, 'the Windows-only sentence is what E6 lifts for WSL2');
  assert.match(hint, /one after another/u, 'D19: a Kimi row’s three reviewers are serialised');
});

// ---------------------------------------------------------------- S5.5 the colour

test('kimiHasItsOwnColour: slot 11, the slate, never shared with an anchored runtime', () => {
  const anchored = ['codex', 'gemini', 'local', 'claude', 'antigravity', 'vibe', 'kimi'];
  const colour = vendorPalette(anchored);

  assert.equal(colour('kimi'), VENDOR_PALETTE[11], 'kimi is not on the palette’s twelfth slot');
  assert.equal(vendorPalette(['kimi'])('kimi'), VENDOR_PALETTE[11], 'it moved when it was alone');
  // Without an anchor the slate is only where kimi's NAME happens to hash: seven strangers pushed it elsewhere.
  assert.equal(vendorPalette(['aa', 'bb', 'cc', 'dd', 'ee', 'ff', 'kimi'])('kimi'), VENDOR_PALETTE[11], 'it moved among strangers');
  assert.equal(new Set(anchored.map(colour)).size, anchored.length, 'two anchored runtimes share a colour');
});

// ---------------------------------------------------------------- S5.6 the ▶ terminal

test('playOpensPlainKimi: the CLI, no acp, no model flag, no home of coai’s — the person’s own Kimi in their own home', () => {
  const term = vendorTerminal(KIMI);

  assert.equal(term.command, 'kimi', 'interactive Kimi with no flags — a model flag nobody measured is not passed');
  assert.deepEqual(term.env, {}, 'no KIMI_CODE_HOME: this is the person’s own home, where `kimi login` signs coai’s launches in');
  assert.equal(term.usageCommand, '/usage', 'Kimi’s own command, answered without a model call (RESULTS §5)');
  assert.equal(term.note, '');
  assert.doesNotMatch(term.command, /acp|-m |--model/u);
});

test('▶ on a Kimi row with a CLI path of this side runs that path, quoted when it has a space', () => {
  const term = vendorTerminal({ ...KIMI, executablePath: 'C:\\Program Files\\Kimi Code\\bin\\kimi.exe' });

  assert.equal(term.command, '"C:\\Program Files\\Kimi Code\\bin\\kimi.exe"');
});

// ---------------------------------------------------------------- S5.11 the update light

test('theUpdateLightReadsNpm: the newest Kimi is read from the package that is the same release line as the script’s binaries', () => {
  for (const platform of ['win32', 'linux', 'darwin'] as const) {
    assert.deepEqual(versionSourceFor('kimi', platform, 'x64'), { kind: 'npm', package: '@moonshot-ai/kimi-code' });
  }
  assert.equal(versionFrom({ kind: 'npm', package: '@moonshot-ai/kimi-code' }, { version: '2.2.0' }), '2.2.0');
});

// ---------------------------------------------------------------- S5.12 an older server (D13)

test('anOlderServerNeverGetsAKimiRow: held out of the file below the release that knows it, crossing at it and on an unknown server', () => {
  assert.match(KIMI_RUNTIME_SINCE, /^\d+\.\d+\.\d+$/u, 'KIMI_RUNTIME_SINCE is not a version');
  assert.equal(runtimeOnServer('kimi', '0.55.0'), false, 'the newest TAG is released without the runtime');
  assert.equal(runtimeOnServer('kimi', OLDER), false, 'mcp 0.56.0 is released without the runtime (0.55.0, the newest tag, too)');

  const older = rowsOf(vendorsEnv([...DEFAULT_VENDORS, KIMI], OLDER));
  assert.deepEqual(older.map((row) => row.id), ['codex', 'antigravity'],
    'an older server cannot review with the kimi row; every other row still crosses');

  for (const version of [KIMI_RUNTIME_SINCE, '99.0.0', '']) {
    const kimi = rowsOf(vendorsEnv([...DEFAULT_VENDORS, KIMI], version)).find((row) => row.id === 'kimi');
    assert.equal(kimi?.runtime, 'kimi', `the kimi row must reach a server at ${version || 'an unknown version'}`);
  }
});

test('the written settings file holds the kimi row back from an older server too', () => {
  const written = JSON.parse(serverSettingsJson(DEFAULTS, [...DEFAULT_VENDORS, KIMI], '0.72.0', OLDER)) as Record<string, string>;
  const current = JSON.parse(serverSettingsJson(DEFAULTS, [...DEFAULT_VENDORS, KIMI], '0.72.0', KIMI_RUNTIME_SINCE)) as Record<string, string>;

  assert.ok(!written['COAI_VENDORS']!.includes('"kimi"'));
  assert.ok(current['COAI_VENDORS']!.includes('"kimi"'));
});

test('the card’s note names the row and the release, and is silent when the row will run', () => {
  const note = runtimeSkewNote(OLDER, [KIMI], 'windows');

  assert.match(note, /kimi/u);
  assert.ok(note.includes(KIMI_RUNTIME_SINCE), note);
  assert.ok(note.includes(OLDER), note);
  assert.equal(runtimeSkewNote(KIMI_RUNTIME_SINCE, [KIMI], 'windows'), '');
  assert.equal(runtimeSkewNote('', [KIMI], 'windows'), '', 'an unknown server is not called old');
  assert.equal(runtimeSkewNote(OLDER, [{ ...KIMI, enabled: false }], 'windows'), '', 'a switched-off row does not cross anyway');
});

// ---------------------------------------------------------------- the card, RUN (new UI)

function stateWith(vendors: readonly Vendor[], overrides: Partial<PanelState> = {}): PanelState {
  return { ...panelState('reviewers'), vendors, ...overrides };
}

function card(state: PanelState) {
  const page = runPanel(state, { html: catalogHtml(state, 'test-nonce', 'models') });

  return pageTree(page.html).one((node) => node.dataset['modelCard'] === 'kimi', 'the Kimi card');
}

test('a Kimi card has its CLI’s buttons and badge, asks for no endpoint, and offers the curated models', () => {
  const kimi = card(stateWith([DEFAULT_VENDORS[0]!, KIMI], { server: KNOWN(KIMI_RUNTIME_SINCE) }));
  const commands = kimi.find((node) => node.dataset['command'] !== undefined).map((node) => node.dataset['command']);

  assert.ok(commands.includes('runVendor') && commands.includes('installVendorCli') && commands.includes('updateVendorCli'),
    `▶ ⤓ ⟳ are missing from the Kimi card: ${commands.join(', ')}`);
  assert.equal(kimi.find((node) => node.dataset['setting'] === 'baseUrl').length, 0, 'Kimi knows its own endpoint — no base-URL box');
  assert.match(kimi.text(), /kimi CLI: version not read|kimi \d/u, 'the card names the CLI it runs');
  for (const { id } of KIMI_MODELS) {
    assert.ok(kimi.find((node) => node.tagName === 'OPTION' && node.attrs['value'] === id).length > 0, `${id} is not offered`);
  }
});

test('against a server older than the runtime, the Kimi card says why it is switched off', () => {
  const state = stateWith([DEFAULT_VENDORS[0]!, KIMI], { server: KNOWN(OLDER) });
  const kimi = card(state);
  const stages = kimi.find((node) => node.tagName === 'INPUT' && (node.dataset['setting'] === 'plan' || node.dataset['setting'] === 'code'));

  assert.ok(cardContextFor(state)(KIMI).runtimeNote.length > 0, 'the fixture: the context has a note for this row');
  assert.match(kimi.text(), new RegExp(`update it to ${KIMI_RUNTIME_SINCE.replaceAll('.', '\\.')}`, 'u'));
  assert.ok(stages.length === 2 && stages.every((node) => node.disabled), 'the stage boxes stay live for a row the server will not run');
});

// ---------------------------------------------------------------- S5.13 the version warning

const WARNING = 'Kimi 2.2.0 is not the measured 2.1.1: the confinement measurements are 2.1.1’s — re-run scripts/probe-kimi-confinement.mjs.';

function cardWith(reported: Readonly<Record<string, ProviderHealth>>) {
  return card(stateWith([KIMI], { server: KNOWN(KIMI_RUNTIME_SINCE), providers: { reported, asked: true, answered: true, notes: NO_NOTES } }));
}

test('theCardShowsKimisVersionWarning: an AVAILABLE kimi row the server warned about says so in the warning tone, beside its note', () => {
  const kimi = cardWith({ kimi: { provider: 'kimi', auth: 'own auth', note: 'the CLI’s own sign-in is used', warning: WARNING } });
  const said = kimi.find((node) => node.className.split(' ').includes('warned'));

  assert.equal(said.length, 1, 'the card draws no warning for a kimi row — the card is runtime-neutral, so a kimi row must show it');
  assert.ok(said[0]!.text().includes(WARNING), said[0]!.text());
});

test('a kimi row the server did not warn about draws no warning', () => {
  const kimi = cardWith({ kimi: { provider: 'kimi', auth: 'own auth', note: '' } });

  assert.equal(kimi.find((node) => node.className.split(' ').includes('warned')).length, 0);
});

// ---------------------------------------------------------------- the rest of the panel's lists

test('an effort on a Kimi row is refused as unmeasured rather than stored and ignored', () => {
  assert.match(rowWriteRefusal([KIMI], 'kimi', 'effort', 'high'), /has not been measured/u);
});

test('the Setup tab’s CLI table lists the Kimi CLI', () => {
  assert.deepEqual(cliGroups([KIMI], 'windows').map((group) => group.runtime), ['kimi']);
});
