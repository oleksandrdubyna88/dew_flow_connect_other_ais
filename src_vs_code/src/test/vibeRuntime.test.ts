import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';

import { catalogHtml } from '../catalogPage';
import { rowWriteRefusal } from '../catalogWriteRules';
import { CHAT_RUNTIMES } from '../cliChatLaunch';
import { registryUrlFor, versionFrom, versionSourceFor } from '../cliVersions';
import { CONSULTING_RUNTIMES } from '../consultSettings';
import { HELP_ARTICLES, HELP_LANGUAGES, bodyFor } from '../helpContent';
import { CLI_RUNTIMES, RUNTIMES, VIBE_MODELS, modelsFor, modelsProvenance } from '../models';
import { cardContextFor, type PanelState } from '../panelView';
import { VIBE_RUNTIME_SINCE, runtimeOnServer, runtimeSkewNote } from '../runtimeSince';
import { serverSettingsJson } from '../serverSettingsFile';
import { ServerSettingsSync } from '../serverSettingsSync';
import { DEFAULTS } from '../settingsShape';
import { cliGroups } from '../setupTab';
import { OFFICIAL_SOURCES, vendorInstall, vendorTerminal, vendorUpdate } from '../vendorTerminal';
import { DEFAULT_VENDORS, VENDOR_PRESETS, type Vendor, vendorsFrom } from '../vendors';
import { vendorsEnv } from '../vendorsWire';
import { panelState, runPanel } from './panelPageHarness';
import { pageTree } from './pageTree';

/**
 * The `vibe` runtime on the panel's side — S2 of todo/PLAN_vibe_and_kimi_runtimes.md: Mistral's own CLI on the person's
 * own subscription, signed in as itself.
 *
 * <p>Two ways it goes wrong silently, and both are older than this runtime: a runtime the parser does not know comes
 * back as `codex` under its own name (a Mistral row reviewing through the Codex CLI), and an older `coai-mcp` that does
 * not know it turns it into `codex` the same way on its side (D13). Everything below is one of those two, or a button
 * that would install or run the wrong software.</p>
 */

const VIBE: Vendor = {
  id: 'vibe', runtime: 'vibe', model: 'mistral-medium-3.5', enabled: true, plan: true, code: true,
  baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0,
};

/**
 * The newest `coai-mcp` released WITHOUT the runtime — mcp 0.52.0, published 2026-10-13 from main while this branch was
 * open (0.51.0, #785, shipped the Vibe ground and not the runtime either). It would read a vibe row as codex.
 */
const OLDER = '0.52.0';
const KNOWN = (version: string) => ({ kind: 'known' as const, version, remembered: false, updateOffered: false });

const repo = (...parts: string[]): string => join(__dirname, '..', '..', '..', ...parts);

function rowsOf(json: string): readonly { id: string; runtime: string }[] {
  return JSON.parse(json) as { id: string; runtime: string }[];
}

// ---------------------------------------------------------------- the runtime survives being read

test('a saved vibe row comes back as vibe, never as a codex row under its own name', () => {
  assert.ok((RUNTIMES as readonly string[]).includes('vibe'), 'RUNTIMES has no vibe — the parser would rewrite it to codex');

  const [vendor] = vendorsFrom([{ id: 'vibe', runtime: 'vibe', model: 'glm-5-3', enabled: true }]);

  assert.equal(vendor!.runtime, 'vibe');
  assert.equal(vendor!.model, 'glm-5-3');
});

test('vibe is a CLI on this machine, and neither a chat partner nor a consultant yet (S3 is not built)', () => {
  assert.ok(CLI_RUNTIMES.includes('vibe'));
  assert.ok(!(CHAT_RUNTIMES as readonly string[]).includes('vibe'), 'chat is not this plan’s (§8)');
  assert.ok(!(CONSULTING_RUNTIMES as readonly string[]).includes('vibe'), 'the consultant is S3, and coai-mcp would refuse it by name');
});

// ---------------------------------------------------------------- the preset (D11)

test('Add a model offers Mistral Vibe on the curated default, and a fresh install does not review with it', () => {
  const preset = VENDOR_PRESETS.find((one) => one.runtime === 'vibe');

  assert.ok(preset !== undefined, 'no vibe preset — nobody could add the row from the panel');
  assert.equal(preset.id, 'vibe');
  assert.equal(preset.model, VIBE_MODELS[0]!.id, 'the preset starts on the first curated model');
  assert.equal(preset.baseUrl, '', 'Vibe signs in as itself: no endpoint, no key');
  assert.match(preset.label, /Vibe/);
  assert.ok(!DEFAULT_VENDORS.some((one) => one.runtime === 'vibe'), 'D11: presets only — never a default reviewer');

  const [back] = vendorsFrom([{ ...preset }]);
  assert.equal(back!.runtime, 'vibe', 'the preset survives its own round trip through the parser');
});

// ---------------------------------------------------------------- the models (D10)

test('the model picker offers the curated Vibe list, never the Gemini one, and keeps a typed alias', () => {
  assert.deepEqual(VIBE_MODELS.map((one) => one.id), ['mistral-medium-3.5', 'glm-5-3']);

  const offered = modelsFor('vibe', [], '');
  assert.deepEqual(offered.map((one) => one.id), VIBE_MODELS.map((one) => one.id));
  assert.ok(!offered.some((one) => one.id.startsWith('gemini')), 'an unknown runtime used to fall to the Gemini list');

  const typed = modelsFor('vibe', [], 'devstral-3');
  assert.equal(typed[0]!.id, 'devstral-3', 'a saved choice never vanishes from its own dropdown');
});

test('the caption says the list is curated, which Vibe version it was seen on, and that coai-mcp refuses another alias', () => {
  const caption = modelsProvenance('vibe', []);

  assert.match(caption, /curated/u);
  assert.match(caption, /2\.26\.1/u, 'D10: the list is version-labelled');
  assert.match(caption, /refuse/u, 'an alias outside the list is refused before the launch — the caption must not promise it runs');
  assert.doesNotMatch(caption, /Codex CLI|Gemini/u);
});

test('the curated list is the one coai-mcp checks a launch against', () => {
  // reads-another-program: each id the panel OFFERS is asserted positively to be one coai-mcp's
  // VibeModels.Curated holds — a renamed or dropped alias on the server fails here loudly instead of
  // the panel offering a model every round would refuse. It derives nothing from that source.
  const server = readFileSync(repo('src_mcp', 'runners', 'Vibe', 'VibeLaunch.cs'), 'utf8');

  assert.ok(VIBE_MODELS.length > 0, 'an empty list would pass the loop below having checked nothing');
  for (const { id } of VIBE_MODELS) {
    assert.ok(server.includes(`"${id}"`), `coai-mcp does not know '${id}' — the panel offers a model the server refuses`);
  }
  assert.match(server, /public const string SeenOn = "2\.26\.1";/u, 'the caption names the Vibe version the server list was seen on');
});

// ---------------------------------------------------------------- the ▶ terminal

test('▶ on a Vibe card runs plain vibe, on the row’s model, on the Python CLI coai measured — never codex', () => {
  const term = vendorTerminal(VIBE);

  assert.equal(term.command, 'vibe', 'interactive Vibe with no flags — it takes its model from the environment (D10)');
  assert.deepEqual(term.env, { VIBE_ACTIVE_MODEL: 'mistral-medium-3.5', VIBE_CLI: 'python' });
  assert.equal(term.usageCommand, '/status');
  assert.equal(term.note, '');
});

test('▶ on a Vibe row that names no model leaves the model to Vibe', () => {
  assert.deepEqual(vendorTerminal({ ...VIBE, model: '' }).env, { VIBE_CLI: 'python' });
});

test('every other runtime’s terminal carries no environment of its own', () => {
  for (const runtime of ['codex', 'claude', 'antigravity', 'gemini'] as const) {
    assert.deepEqual(vendorTerminal({ ...VIBE, id: runtime, runtime, model: 'm' }).env, {}, runtime);
  }
});

// ---------------------------------------------------------------- install and update, per OS (D16, §6)

test('⤓ installs Vibe with Mistral’s own script on Linux and macOS, and with uv on Windows', () => {
  const on = (platform: 'win32' | 'linux' | 'darwin') => vendorInstall(VIBE, platform);

  assert.equal(on('linux').command, 'curl -LsSf https://mistral.ai/vibe/install.sh | bash');
  assert.equal(on('darwin').command, on('linux').command, 'Mistral’s README gives one script for Linux and macOS');
  assert.equal(on('linux').prerequisite, '', 'the script installs uv itself when it is missing');
  assert.equal(on('win32').command, 'uv tool install mistral-vibe');
  assert.equal(on('win32').prerequisite, 'powershell -ExecutionPolicy ByPass -c "irm https://astral.sh/uv/install.ps1 | iex"',
    'the README’s Windows route: uv first, from Astral');
});

test('⤓ for Vibe says that a sign-in follows and that only the person can do it', () => {
  for (const platform of ['win32', 'linux', 'darwin'] as const) {
    const note = vendorInstall(VIBE, platform).note;
    assert.match(note, /sign.?in/iu, platform);
    assert.match(note, /▶/u, `${platform}: the note names the button that opens Vibe’s own terminal for it`);
    assert.match(note, /vibe --setup/u, platform);
  }
});

test('⟳ updates Vibe the way Mistral’s own installer does: the script again on Linux and macOS, uv tool upgrade on Windows', () => {
  for (const installed of ['', '2.26.1']) {
    assert.equal(vendorUpdate(VIBE, 'linux', installed).command, 'curl -LsSf https://mistral.ai/vibe/install.sh | bash');
    assert.equal(vendorUpdate(VIBE, 'darwin', installed).command, 'curl -LsSf https://mistral.ai/vibe/install.sh | bash');
    assert.equal(vendorUpdate(VIBE, 'win32', installed).command, 'uv tool upgrade mistral-vibe',
      '`uv tool install` on an installed tool changes nothing');
  }
});

test('the Vibe install and update commands come only from sources Mistral publishes', () => {
  for (const platform of ['win32', 'linux', 'darwin'] as const) {
    for (const command of [vendorInstall(VIBE, platform).command, vendorUpdate(VIBE, platform).command]) {
      assert.ok(OFFICIAL_SOURCES.some((prefix) => command.startsWith(prefix)), `${platform}: "${command}" is not an official source`);
      assert.match(command, /mistral/u, `${platform}: "${command}" installs something that is not Mistral’s`);
    }
  }
});

test('the panel’s install commands are the ones coai-mcp names when it finds no Vibe', () => {
  // reads-another-program: a positive claim about VendorDiagnosis.InstallCure — each command the
  // button types must appear verbatim in the server's own cure sentence, so the panel and a failed
  // round never tell a person two different installs. A changed cure fails here loudly.
  const source = readFileSync(repo('src_mcp', 'runners', 'Reviewers', 'VendorDiagnosis.cs'), 'utf8');
  // The vibe arms alone: the whole file also holds codex's npm line, which a codex fallback here would match.
  const linux = source.indexOf('("vibe", true)');
  const windows = source.indexOf('("vibe", false)');
  assert.ok(linux > 0 && windows > linux, 'coai-mcp’s InstallCure has no vibe arms where this test looks for them');
  const linuxArm = source.slice(linux, windows);
  // As the C# literal spells it, its escaped quotes read back as the quotes they stand for.
  const windowsArm = source.slice(windows, source.indexOf('\n', source.indexOf('"', windows + 20) + 1)).replaceAll('\\"', '"');

  assert.ok(linuxArm.includes(vendorInstall(VIBE, 'linux').command), 'the Linux script differs from coai-mcp’s cure');
  assert.ok(windowsArm.includes(vendorInstall(VIBE, 'win32').command), 'the uv command differs from coai-mcp’s cure');
  assert.ok(windowsArm.includes(vendorInstall(VIBE, 'win32').prerequisite), 'the uv installer differs from coai-mcp’s cure');
});

// ---------------------------------------------------------------- the update light

test('the update light reads Vibe’s newest version from PyPI, the index uv tool install takes it from', () => {
  for (const platform of ['win32', 'linux', 'darwin'] as const) {
    const source = versionSourceFor('vibe', platform, 'x64');
    assert.deepEqual(source, { kind: 'pypi', package: 'mistral-vibe' });
    assert.equal(registryUrlFor(source!), 'https://pypi.org/pypi/mistral-vibe/json');
  }
});

test('a PyPI answer gives its version from info, and anything else gives none', () => {
  const pypi = { kind: 'pypi' as const, package: 'mistral-vibe' };

  assert.equal(versionFrom(pypi, { info: { name: 'mistral-vibe', version: '2.26.1' } }), '2.26.1');
  assert.equal(versionFrom(pypi, { version: '9.9.9' }), '', 'PyPI keeps the version under info; a top-level one is not its answer');
  assert.equal(versionFrom(pypi, { info: {} }), '');
  assert.equal(versionFrom(pypi, null), '');
  assert.equal(versionFrom({ kind: 'npm', package: '@openai/codex' }, { version: '0.160.0' }), '0.160.0', 'npm is read as before');
});

// ---------------------------------------------------------------- an older server (D13)

test('a vibe row is kept OUT of the settings file of a server older than the release that knows it', () => {
  assert.equal(VIBE_RUNTIME_SINCE, '0.53.0', 'mcp 0.52.0 is RELEASED without the runtime; the next minor is the first that can carry it');

  const older = rowsOf(vendorsEnv([...DEFAULT_VENDORS, VIBE], OLDER));
  assert.deepEqual(older.map((row) => row.id), ['codex', 'antigravity'],
    'an older server would run the vibe row as codex; every other row still crosses');

  for (const version of [VIBE_RUNTIME_SINCE, '0.53.1', '1.0.0', '']) {
    const vibe = rowsOf(vendorsEnv([...DEFAULT_VENDORS, VIBE], version)).find((row) => row.id === 'vibe');
    assert.equal(vibe?.runtime, 'vibe', `the vibe row must reach a server at ${version || 'an unknown version'}`);
  }
});

test('the written settings file holds the vibe row back from an older server too', () => {
  const written = JSON.parse(serverSettingsJson(DEFAULTS, [...DEFAULT_VENDORS, VIBE], '0.72.0', OLDER)) as Record<string, string>;
  const current = JSON.parse(serverSettingsJson(DEFAULTS, [...DEFAULT_VENDORS, VIBE], '0.72.0', VIBE_RUNTIME_SINCE)) as Record<string, string>;

  assert.ok(!written['COAI_VENDORS']!.includes('"vibe"'));
  assert.ok(current['COAI_VENDORS']!.includes('"vibe"'));
});

test('the version gate knows each runtime by its own release, and an unknown server is not called old', () => {
  assert.equal(runtimeOnServer('vibe', OLDER), false);
  assert.equal(runtimeOnServer('vibe', '0.51.0'), false, 'mcp 0.51.0 shipped the Vibe ground without the runtime');
  assert.equal(runtimeOnServer('vibe', VIBE_RUNTIME_SINCE), true);
  assert.equal(runtimeOnServer('vibe', ''), true);
  assert.equal(runtimeOnServer('api', '0.36.0'), false, 'the api gate is unchanged');
  assert.equal(runtimeOnServer('api', OLDER), true);
  assert.equal(runtimeOnServer('codex', '0.1.0'), true, 'a runtime every server knows is never held back');
});

test('the card’s note names the row and the release, and is silent when the row will run', () => {
  assert.match(runtimeSkewNote(OLDER, [VIBE]), /vibe/u);
  assert.ok(runtimeSkewNote(OLDER, [VIBE]).includes(VIBE_RUNTIME_SINCE));
  assert.ok(runtimeSkewNote(OLDER, [VIBE]).includes(OLDER));
  assert.equal(runtimeSkewNote(VIBE_RUNTIME_SINCE, [VIBE]), '');
  assert.equal(runtimeSkewNote('', [VIBE]), '', 'an unknown server is not called old');
  assert.equal(runtimeSkewNote(OLDER, [{ ...VIBE, enabled: false }]), '', 'a switched-off row does not cross anyway');
  assert.equal(runtimeSkewNote(OLDER, DEFAULT_VENDORS), '');
});

test('an older build stands down before a settings file a newer build wrote with a vibe row in it', async () => {
  // D13 path 2. An older extension window reads `runtime: "vibe"` as codex (its RUNTIMES lacks it), so the rows it
  // would write are the newer file's rows with the vibe one rewritten — the guard is the file's own stamp.
  const newer = serverSettingsJson(DEFAULTS, [...DEFAULT_VENDORS, VIBE], '0.72.0', VIBE_RUNTIME_SINCE);
  const writes: string[] = [];
  const asAnOlderBuildReadsIt: Vendor = { ...VIBE, runtime: 'codex' };
  const sync = new ServerSettingsSync(
    () => ({ settings: DEFAULTS, vendors: [...DEFAULT_VENDORS, asAnOlderBuildReadsIt] }),
    async (json: string) => {
      writes.push(json);
    },
    '0.71.0',
    async () => ({ kind: 'contents', text: newer }),
  );

  assert.equal(await sync.sync(), 'stood-down');
  assert.deepEqual(writes, [], 'the older build overwrote the vibe row with a codex one');
});

// ---------------------------------------------------------------- the card, RUN (new UI)

function stateWith(vendors: readonly Vendor[], overrides: Partial<PanelState> = {}): PanelState {
  return { ...panelState('reviewers'), vendors, ...overrides };
}

function card(state: PanelState) {
  const page = runPanel(state, { html: catalogHtml(state, 'test-nonce', 'models') });

  return { page, card: pageTree(page.html).one((node) => node.dataset['modelCard'] === 'vibe', 'the Vibe card') };
}

test('a Vibe card has its CLI’s buttons and badge, asks for no endpoint, and offers the curated models', () => {
  const { card: vibe } = card(stateWith([DEFAULT_VENDORS[0]!, VIBE], { server: KNOWN(VIBE_RUNTIME_SINCE) }));
  const commands = vibe.find((node) => node.dataset['command'] !== undefined).map((node) => node.dataset['command']);

  assert.ok(commands.includes('runVendor') && commands.includes('installVendorCli') && commands.includes('updateVendorCli'),
    `▶ ⤓ ⟳ are missing from the Vibe card: ${commands.join(', ')}`);
  assert.equal(vibe.find((node) => node.dataset['setting'] === 'baseUrl').length, 0, 'Vibe knows its own endpoint — no base-URL box');
  assert.match(vibe.text(), /vibe CLI: version not read|vibe \d/u, 'the card names the CLI it runs');
  for (const { id } of VIBE_MODELS) {
    assert.ok(vibe.find((node) => node.tagName === 'OPTION' && node.attrs['value'] === id).length > 0, `${id} is not offered`);
  }
});

test('against a server older than the runtime, the Vibe card says why it is switched off', () => {
  const state = stateWith([DEFAULT_VENDORS[0]!, VIBE], { server: KNOWN(OLDER) });
  const { card: vibe } = card(state);
  const stages = vibe.find((node) => node.tagName === 'INPUT' && (node.dataset['setting'] === 'plan' || node.dataset['setting'] === 'code'));

  assert.ok(cardContextFor(state)(VIBE).runtimeNote.length > 0, 'the fixture: the context has a note for this row');
  assert.match(vibe.text(), new RegExp(`update it to ${VIBE_RUNTIME_SINCE.replaceAll('.', '\\.')}`, 'u'));
  assert.ok(stages.length === 2 && stages.every((node) => node.disabled), 'the stage boxes stay live for a row the server will not run');
});

test('an effort on a Vibe row is refused as unmeasured rather than stored and ignored', () => {
  assert.match(rowWriteRefusal([VIBE], 'vibe', 'effort', 'high'), /has not been measured/u);
});

test('the Setup tab’s CLI table lists the Vibe CLI', () => {
  assert.deepEqual(cliGroups([VIBE], 'windows').map((group) => group.runtime), ['vibe']);
});

// ---------------------------------------------------------------- the help, in five languages

test('every language’s choose-reviewers article names Mistral Vibe and that it is Windows-only for now', () => {
  const article = HELP_ARTICLES.find((one) => one.id === 'choose-reviewers');
  assert.ok(article !== undefined, 'the reviewers article has been renamed, and this test is now asserting nothing');

  for (const language of HELP_LANGUAGES) {
    const { body, fallback } = bodyFor(article, language);
    assert.equal(fallback, false, `the ${language} article is missing`);
    assert.ok(body.setup.includes('Mistral Vibe'), `the ${language} article does not name the Vibe preset`);
    assert.ok(body.setup.includes('vibe --setup'), `the ${language} article does not say how Vibe is signed in`);
    assert.ok(body.usage.includes('uv tool upgrade mistral-vibe'), `the ${language} article does not say how Vibe is updated`);
  }
});
