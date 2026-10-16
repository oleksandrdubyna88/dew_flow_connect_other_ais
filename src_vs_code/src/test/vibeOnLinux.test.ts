import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';

import { catalogHtml } from '../catalogPage';
import { HELP_ARTICLES, HELP_LANGUAGES, bodyFor } from '../helpContent';
import { hostIsWsl, isLinuxSide } from '../hostSide';
import { cardContextFor, type PanelState } from '../panelView';
import { ON_LINUX_SINCE, VIBE_ON_LINUX_SINCE, VIBE_RUNTIME_SINCE, runtimeOnLinuxSide, runtimeSkewNote } from '../runtimeSince';
import { vendorInstall, vendorUpdate } from '../vendorTerminal';
import { DEFAULT_VENDORS, VENDOR_PRESETS, type Vendor } from '../vendors';
import { pageTree } from './pageTree';
import { panelState, runPanel } from './panelPageHarness';

/**
 * Vibe on a Linux host — E3 of todo/PLAN_kimi_and_vibe_on_linux.md: what the panel says on the WSL side, once coai-mcp
 * 0.58.0 runs a Vibe row there.
 *
 * <p>The facts every text below must agree on (DL1, DL2, DL14): WSL2 runs Vibe from 0.58.0; the sign-in coai uses is
 * `~/.vibe/.env`, made by `vibe --setup`, which coai links into its own homes and never copies; a sign-in kept in a
 * keyring is invisible to coai; a native Linux host is refused — "measured on WSL2 only for now"; Windows is as it was.
 * Kimi's texts are NOT in this file: Kimi on Linux is a later release.</p>
 */

const VIBE: Vendor = {
  id: 'vibe', runtime: 'vibe', model: 'mistral-medium-3.5', enabled: true, plan: true, code: true,
  baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0,
};

/** The newest coai-mcp released without Vibe on Linux (`.release-please-manifest.json` says 0.57.0 when this was written). */
const BEFORE_LINUX = '0.57.0';
const KNOWN = (version: string) => ({ kind: 'known' as const, version, remembered: false, updateOffered: false });
const repo = (...parts: string[]): string => join(__dirname, '..', '..', '..', ...parts);

// ---------------------------------------------------------------- DL14: the second table

test('the Vibe-on-Linux release is its own table, beside the one that says which server knows the runtime', () => {
  assert.equal(VIBE_ON_LINUX_SINCE, '0.58.0');
  assert.deepEqual(ON_LINUX_SINCE, { vibe: '0.58.0' }, 'Kimi on Linux is a later release: its entry belongs to that release');
});

test('a Linux or WSL side whose server is older than 0.58.0 does not run Vibe; a newer or an unknown server does', () => {
  assert.equal(runtimeOnLinuxSide('vibe', BEFORE_LINUX), false);
  assert.equal(runtimeOnLinuxSide('vibe', '0.53.0'), false, 'knowing the runtime is not running it on this host');
  assert.equal(runtimeOnLinuxSide('vibe', VIBE_ON_LINUX_SINCE), true);
  assert.equal(runtimeOnLinuxSide('vibe', '0.58.1'), true);
  assert.equal(runtimeOnLinuxSide('vibe', '1.0.0'), true);
  assert.equal(runtimeOnLinuxSide('vibe', ''), true, 'an unknown server is not called old');
  assert.equal(runtimeOnLinuxSide('codex', '0.1.0'), true, 'a runtime with no entry runs on every host');
  assert.equal(runtimeOnLinuxSide('kimi', '0.57.0'), true, 'Kimi has no Linux entry yet — this release says nothing about it');
});

test('the card on a Linux side says "update coai-mcp to run Vibe here" for an older server, and only there', () => {
  const note = runtimeSkewNote(BEFORE_LINUX, [VIBE], true);

  assert.match(note, /update coai-mcp to run Vibe here/iu);
  assert.ok(note.includes(BEFORE_LINUX), 'it names the server the person has');
  assert.ok(note.includes(VIBE_ON_LINUX_SINCE), 'it names the release that runs Vibe here');
  assert.equal(runtimeSkewNote(BEFORE_LINUX, [VIBE], false), '', 'on Windows an 0.57.0 server runs Vibe: no note');
  assert.equal(runtimeSkewNote(BEFORE_LINUX, [VIBE]), '', 'the old call shape is the Windows one');
  assert.equal(runtimeSkewNote(VIBE_ON_LINUX_SINCE, [VIBE], true), '');
  assert.equal(runtimeSkewNote('', [VIBE], true), '', 'an unknown server is not called old');
  assert.equal(runtimeSkewNote(BEFORE_LINUX, [{ ...VIBE, enabled: false }], true), '', 'a switched-off row is not refused at launch');
  assert.equal(runtimeSkewNote(BEFORE_LINUX, DEFAULT_VENDORS, true), '', 'codex and antigravity are untouched');
});

test('a server that does not know Vibe at all keeps its own sentence on a Linux side — one note, not two', () => {
  const note = runtimeSkewNote('0.52.0', [VIBE], true);

  assert.ok(note.includes(VIBE_RUNTIME_SINCE), 'the runtime gate speaks first');
  assert.doesNotMatch(note, /run Vibe here/iu);
});

test('only a Linux or WSL host is a Linux side: Windows and macOS are not', () => {
  assert.equal(isLinuxSide('posix', 'linux'), true);
  assert.equal(isLinuxSide('windows', 'win32'), false);
  assert.equal(isLinuxSide('posix', 'darwin'), false, 'macOS stays refused whatever the server — updating coai-mcp would not help');
});

test('whether this host is WSL is asked only on Linux, and an unanswered question is "no"', async () => {
  assert.equal(await hostIsWsl('linux', async () => true), true);
  assert.equal(await hostIsWsl('linux', async () => false), false);
  assert.equal(await hostIsWsl('linux', async () => { throw new Error('no /proc/version'); }), false);
  assert.equal(await hostIsWsl('win32', async () => true), false, 'a Windows host is not a WSL one whatever /proc/version would say');
  assert.equal(await hostIsWsl('darwin', async () => true), false);
});

function stateWith(overrides: Partial<PanelState>): PanelState {
  return { ...panelState('reviewers'), vendors: [DEFAULT_VENDORS[0]!, VIBE], ...overrides };
}

test('the Vibe card on a WSL side shows the update sentence for a 0.57.0 server, and not on a Windows side', () => {
  const wsl = stateWith({ server: KNOWN(BEFORE_LINUX), hostFamily: 'posix' });
  const windows = stateWith({ server: KNOWN(BEFORE_LINUX), hostFamily: 'windows' });

  assert.match(cardContextFor(wsl)(VIBE).runtimeNote, /update coai-mcp to run Vibe here/iu);
  assert.equal(cardContextFor(windows)(VIBE).runtimeNote, '');

  const page = runPanel(wsl, { html: catalogHtml(wsl, 'test-nonce', 'models') });
  const card = pageTree(page.html).one((node) => node.dataset['modelCard'] === 'vibe', 'the Vibe card');
  assert.match(card.text(), /update coai-mcp to run Vibe here/iu, 'the person reads it on the card');
});

test('the Vibe card on a WSL side is quiet once the server is 0.58.0', () => {
  const state = stateWith({ server: KNOWN(VIBE_ON_LINUX_SINCE), hostFamily: 'posix' });

  assert.equal(cardContextFor(state)(VIBE).runtimeNote, '');
});

// ---------------------------------------------------------------- the card hint

test('the Vibe hint names Windows, WSL2 from 0.58.0, the .env sign-in, and that native Linux is measured on WSL2 only', () => {
  const hint = VENDOR_PRESETS.find((one) => one.runtime === 'vibe')?.hint ?? '';

  assert.doesNotMatch(hint, /Windows only/u, 'the Windows-only sentence is what this release lifts');
  assert.match(hint, /WSL2/u);
  assert.ok(hint.includes(VIBE_ON_LINUX_SINCE), `the hint does not name coai-mcp ${VIBE_ON_LINUX_SINCE}: ${hint}`);
  assert.ok(hint.includes(VIBE_RUNTIME_SINCE), 'the runtime gate stays in the hint');
  assert.ok(hint.includes('~/.vibe/.env') && hint.includes('vibe --setup'), 'the sign-in file and the command that makes it');
  assert.match(hint, /Native Linux: measured on WSL2 only for now/u);
});

// ---------------------------------------------------------------- the install note, per host

test('⤓ for Vibe on WSL2 says Vibe runs there from 0.58.0 and where the sign-in is kept', () => {
  const note = vendorInstall(VIBE, 'linux', true).note;

  assert.ok(note.includes(VIBE_ON_LINUX_SINCE) && note.includes('WSL2'), note);
  assert.ok(note.includes('~/.vibe/.env') && note.includes('vibe --setup'), note);
  assert.match(note, /links? (?:it|that file) into its own homes and never copies/u, 'the sign-in is linked, never copied');
  assert.match(note, /keyring is invisible to coai/u);
  assert.doesNotMatch(note, /does not run Vibe/u, 'the "not yet" sentence is what this release lifts');
  assert.match(note, /▶/u, 'the note still names the button that opens Vibe’s own terminal');
});

test('⤓ for Vibe on native Linux says it is measured on WSL2 only for now', () => {
  const note = vendorInstall(VIBE, 'linux', false).note;

  assert.match(note, /measured on WSL2 only for now/u);
  assert.match(note, /keyring is invisible to coai/u);
  assert.match(note, /vibe --setup/u);
  assert.doesNotMatch(note, /operating system’s keyring, which every coai launch/u, 'that sentence is Windows’s truth only');
});

test('⤓ for Vibe on macOS says nothing is measured there, and on Windows is unchanged', () => {
  assert.match(vendorInstall(VIBE, 'darwin').note, /macOS/u);
  assert.doesNotMatch(vendorInstall(VIBE, 'darwin').note, /WSL2/u);

  const windows = vendorInstall(VIBE, 'win32').note;
  assert.ok(windows.includes('The sign-in is kept in the operating system’s keyring, which every coai launch of Vibe reads.'));
  assert.doesNotMatch(windows, /WSL2|\.env/u);
});

test('WSL2 and native Linux get the same command, and ⟳ carries the same note as ⤓', () => {
  assert.equal(vendorInstall(VIBE, 'linux', true).command, vendorInstall(VIBE, 'linux', false).command);
  assert.equal(vendorUpdate(VIBE, 'linux', '2.26.1', true).note, vendorInstall(VIBE, 'linux', true).note);
  assert.equal(vendorUpdate(VIBE, 'linux', '2.26.1', false).note, vendorInstall(VIBE, 'linux', false).note);
});

// ---------------------------------------------------------------- the help, in five languages

/** What each language must say about Vibe on Linux — the same four facts, in its own words. */
const SAID: Record<string, readonly string[]> = {
  en: ['WSL2', '0.58.0', '~/.vibe/.env', '`vibe --setup`', 'never copies', 'keyring', 'Native Linux'],
  de: ['WSL2', '0.58.0', '~/.vibe/.env', '`vibe --setup`', 'kopiert sie nie', 'Schlüsselbund', 'Natives Linux'],
  es: ['WSL2', '0.58.0', '~/.vibe/.env', '`vibe --setup`', 'nunca lo copia', 'llavero', 'Linux nativo'],
  ru: ['WSL2', '0.58.0', '~/.vibe/.env', '`vibe --setup`', 'никогда не копирует', 'связке ключей', 'нативном Linux'],
  uk: ['WSL2', '0.58.0', '~/.vibe/.env', '`vibe --setup`', 'ніколи не копіює', 'в’язці ключів', 'нативному Linux'],
};

/** The sentences this release replaced, per language: none may survive anywhere in the help. */
const GONE: Record<string, readonly string[]> = {
  en: ['on Windows only for now, because Linux, WSL and macOS are not measured yet', 'Vibe runs only on Windows for now'],
  de: ['vorerst nur unter Windows aus, weil Linux, WSL und macOS noch nicht gemessen sind', 'Vibe läuft vorerst nur unter Windows'],
  es: ['por ahora solo en Windows, porque Linux, WSL y macOS aún no están medidos', 'Por ahora Vibe solo funciona en Windows'],
  ru: ['пока запускает его только на Windows, потому что Linux, WSL и macOS ещё не измерены', 'Пока Vibe работает только в Windows'],
  uk: ['поки запускає його лише на Windows, бо Linux, WSL і macOS ще не виміряні', 'Поки що Vibe працює лише у Windows'],
};

test('the five languages say the same about Vibe on Linux: WSL2 from 0.58.0, the .env sign-in, never copied, native Linux not yet', () => {
  const article = HELP_ARTICLES.find((one) => one.id === 'choose-reviewers');
  assert.ok(article !== undefined, 'the reviewers article has been renamed, and this test is now asserting nothing');
  assert.deepEqual([...HELP_LANGUAGES].sort(), Object.keys(SAID).sort(), 'a language was added: say what it must say');

  for (const language of HELP_LANGUAGES) {
    const { body, fallback } = bodyFor(article, language);
    assert.equal(fallback, false, `the ${language} article is missing, so a reader gets English`);
    for (const word of SAID[language]!) {
      assert.ok(body.setup.includes(word), `the ${language} article never says ${word} about Vibe on Linux`);
    }
  }
});

test('the question-consultant help names Vibe on WSL2 from 0.58.0 in every language, and the Windows-only sentence is gone everywhere', () => {
  const article = HELP_ARTICLES.find((one) => one.id === 'the-question-consultant');
  assert.ok(article !== undefined, 'the question consultant article has been renamed, and this test is now asserting nothing');

  for (const language of HELP_LANGUAGES) {
    const everything = [
      ...Object.values(bodyFor(article, language).body),
      ...Object.values(bodyFor(HELP_ARTICLES.find((one) => one.id === 'choose-reviewers')!, language).body),
    ].join(' ');
    assert.ok(everything.includes('WSL2') && everything.includes('0.58.0'), `the ${language} help says nothing about WSL2 and 0.58.0 for a Vibe row`);
    for (const old of GONE[language]!) {
      assert.ok(!everything.includes(old), `the ${language} help still says: ${old}`);
    }
  }
});

// ---------------------------------------------------------------- shared/consultant-limitations.json

interface Row {
  readonly runtime: string; readonly platform: string; readonly standing: string; readonly text: string; readonly source?: string;
  readonly measured?: { readonly date: string; readonly cliVersion: string; readonly cells: string; readonly document: string };
}

// reads-another-program: a positive claim about the shared file the server embeds and the Consultant tab prints — the
// rows the E3 plan names (`todo/PLAN_kimi_and_vibe_on_linux.md`). The C# loader's own tests hold the file's shape.
const ROWS = (JSON.parse(readFileSync(repo('shared', 'consultant-limitations.json'), 'utf8')) as { rows: Row[] }).rows;
const vibeRow = (platform: string): Row => {
  const row = ROWS.find((one) => one.runtime === 'vibe' && one.platform === platform);
  assert.ok(row !== undefined, `no vibe row for ${platform}`);

  return row;
};

test('the Vibe wsl row names the WSL2 cells it rests on, and says how many runs they are', () => {
  const row = vibeRow('wsl');

  assert.equal(row.standing, 'confined');
  assert.ok(row.measured !== undefined && row.source === undefined, 'a measured row, never both');
  assert.equal(row.measured.cliVersion, 'vibe 2.26.1');
  assert.equal(row.measured.date, '2026-10-16');
  assert.match(row.measured.document, /^research\/RESULTS_vibe_confinement\.md/u);
  for (const cell of ['WSL2', 'L-V1', 'L-V2', 'L-V3', 'L-V4', 'n = 1']) {
    assert.ok(row.measured.cells.includes(cell), `the cells do not name ${cell}: ${row.measured.cells}`);
  }
  assert.match(row.text, /WSL2/u);
  assert.doesNotMatch(row.text, /refuses it in WSL|only on Windows/u, 'the refusal this release lifts');
});

test('the Vibe linux row says measured on WSL2 only, and claims nothing about native Linux', () => {
  const row = vibeRow('linux');

  assert.equal(row.standing, 'unmeasured');
  assert.match(row.text, /measured on WSL2 only/u);
  assert.match(row.source ?? '', /WSL2/u);
  assert.doesNotMatch(row.text, /only on Windows/u);
});

test('the Vibe windows and macos rows are as they were', () => {
  assert.equal(vibeRow('windows').standing, 'confined');
  assert.equal(vibeRow('windows').measured?.date, '2026-10-12');
  assert.equal(vibeRow('macos').standing, 'unmeasured');
  assert.match(vibeRow('macos').text, /refuses it on macOS/u);
  assert.ok(!ROWS.some((one) => one.runtime === 'kimi'), 'Kimi rows are another branch’s');
});
