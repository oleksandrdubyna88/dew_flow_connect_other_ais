import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';

import { catalogHtml } from '../catalogPage';
import { HELP_ARTICLES, HELP_LANGUAGES, bodyFor } from '../helpContent';
import { type HostKind, hostKind, hostKindOf, hostKindOfPlatform } from '../hostSide';
import { cardContextFor, type PanelState } from '../panelView';
import { ON_LINUX_SINCE, VIBE_ON_LINUX_SINCE, VIBE_RUNTIME_SINCE, runtimeOnLinuxSide, runtimeSkewNote } from '../runtimeSince';
import { vendorInstall, vendorUpdate, vibeNoteFor } from '../vendorTerminal';
import { namesWsl2 } from '../wslNetwork';
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
/** What the server means about a native Linux host: refused for now, measured on WSL2 only (DL1) — said the same everywhere in English. */
const NATIVE_REFUSED = 'On a native Linux host coai-mcp refuses Vibe for now — it is measured on WSL2 only';
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

// ---------------------------------------------------------------- the host kind (DL1): WSL2, WSL1, native Linux

const WSL2_BANNER = 'Linux version 6.18.33.2-microsoft-standard-WSL2 (root@build) (gcc) #1 SMP';
const WSL1_BANNER = 'Linux version 4.4.0-19041-Microsoft (Microsoft@Microsoft.com) (gcc version 5.4.0) #1-Microsoft';
const NATIVE_BANNER = 'Linux version 5.15.0-91-generic (buildd@lcy02) (gcc) #101-Ubuntu SMP';

for (const [banner, wsl2] of [
  [WSL2_BANNER, true],
  ['6.18.33.2-microsoft-standard-WSL2', true],
  ['5.15.90.1-MICROSOFT-STANDARD-WSL2', true],
  ['5.10.16.3-microsoft-standard', true],
  [WSL1_BANNER, false],
  ['4.4.0-19041-Microsoft', false],
  [NATIVE_BANNER, false],
  ['', false],
] as const) {
  test(`namesWsl2 reads "${banner.slice(0, 40)}" as ${wsl2 ? 'WSL2' : 'not WSL2'}`, () => {
    assert.equal(namesWsl2(banner), wsl2, 'the same rule as the server’s NamesWsl2: WSL2 or microsoft-standard, any case');
  });
}

test('the host kind is WSL2, WSL1 or native Linux by the kernel banner, and Windows and macOS are what they are', () => {
  assert.equal(hostKindOf('linux', WSL2_BANNER), 'wsl2');
  assert.equal(hostKindOf('linux', WSL1_BANNER), 'wsl1', 'a WSL1 kernel is not WSL2: it has no real process tree');
  assert.equal(hostKindOf('linux', NATIVE_BANNER), 'linux');
  assert.equal(hostKindOf('linux', ''), 'linux', 'a banner nobody could read is a native host: it only ever refuses');
  assert.equal(hostKindOf('win32', WSL2_BANNER), 'windows', 'a Windows host is Windows whatever a banner would say');
  assert.equal(hostKindOf('darwin', ''), 'darwin');
  assert.equal(hostKindOfPlatform('linux'), 'linux');
});

test('the host kind of THIS host reads the banner only on Linux, and an unreadable banner never rejects', async () => {
  assert.equal(await hostKind('linux', async () => WSL2_BANNER), 'wsl2');
  assert.equal(await hostKind('linux', async () => WSL1_BANNER), 'wsl1');
  assert.equal(await hostKind('linux', async () => NATIVE_BANNER), 'linux');
  assert.equal(await hostKind('linux', async () => { throw new Error('no /proc/version'); }), 'linux');
  assert.equal(await hostKind('win32', async () => WSL2_BANNER), 'windows');
  assert.equal(await hostKind('darwin', async () => WSL2_BANNER), 'darwin');
  assert.equal(await hostKind('freebsd', async () => WSL2_BANNER), 'wsl2', 'an unknown platform narrows to linux, as hostPlatform says');
});

// ---------------------------------------------------------------- the card's note, by host kind

const ANY_SERVER = [BEFORE_LINUX, VIBE_ON_LINUX_SINCE, '0.58.1', '1.0.0', ''] as const;
const UPDATE = /update coai-mcp to run Vibe here/iu;

test('WSL2: a server older than 0.58.0 is told to update, a newer or unknown one is not', () => {
  const note = runtimeSkewNote(BEFORE_LINUX, [VIBE], 'wsl2');

  assert.match(note, UPDATE);
  assert.ok(note.includes(BEFORE_LINUX), 'it names the server the person has');
  assert.ok(note.includes(VIBE_ON_LINUX_SINCE), 'it names the release that runs Vibe here');
  assert.equal(runtimeSkewNote(VIBE_ON_LINUX_SINCE, [VIBE], 'wsl2'), '');
  assert.equal(runtimeSkewNote('0.58.1', [VIBE], 'wsl2'), '');
  assert.equal(runtimeSkewNote('', [VIBE], 'wsl2'), '', 'an unknown server is not called old');
});

test('a native Linux host is told it is refused at EVERY server version — updating coai-mcp would not help', () => {
  for (const version of ANY_SERVER) {
    const note = runtimeSkewNote(version, [VIBE], 'linux');

    assert.doesNotMatch(note, UPDATE, `server "${version}": an update note on a host 0.58.0 still refuses`);
    assert.match(note, /native Linux host/u, `server "${version}"`);
    assert.match(note, /measured on WSL2 only/u, `server "${version}"`);
  }
});

test('WSL1 is told it is refused at every server version, and the cure is wsl --set-version <distro> 2', () => {
  for (const version of ANY_SERVER) {
    const note = runtimeSkewNote(version, [VIBE], 'wsl1');

    assert.doesNotMatch(note, UPDATE, `server "${version}"`);
    assert.match(note, /WSL1/u, `server "${version}"`);
    assert.ok(note.includes('`wsl --set-version <distro> 2`'), `server "${version}": ${note}`);
  }
});

test('Windows, macOS and the old call shape get no note about the host; a server that does not know Vibe keeps its own, once', () => {
  for (const host of ['windows', 'darwin'] as const) {
    for (const version of ANY_SERVER) {
      assert.equal(runtimeSkewNote(version, [VIBE], host), '', `${host} at "${version}"`);
    }
  }

  for (const host of ['wsl2', 'wsl1', 'linux'] as const) {
    const note = runtimeSkewNote('0.52.0', [VIBE], host);
    assert.ok(note.includes(VIBE_RUNTIME_SINCE), `${host}: the runtime gate speaks first`);
    assert.doesNotMatch(note, /run Vibe here|native Linux host|WSL1/u, `${host}: one note for one row`);
  }
});

test('a switched-off row and the other reviewers are untouched on every host', () => {
  for (const host of ['windows', 'wsl2', 'wsl1', 'linux', 'darwin'] as const) {
    assert.equal(runtimeSkewNote(BEFORE_LINUX, [{ ...VIBE, enabled: false }], host), '', `${host}: a switched-off row is not refused at launch`);
    assert.equal(runtimeSkewNote(BEFORE_LINUX, DEFAULT_VENDORS, host), '', `${host}: codex and antigravity are untouched`);
  }
});

function stateWith(overrides: Partial<PanelState>): PanelState {
  return { ...panelState('reviewers'), vendors: [DEFAULT_VENDORS[0]!, VIBE], ...overrides };
}

test('the Vibe card says what its HOST kind says: update on WSL2, a refusal on native Linux and WSL1, nothing on Windows', () => {
  const on = (hostKindOfWindow: HostKind, version = BEFORE_LINUX) => stateWith({ server: KNOWN(version), hostKind: hostKindOfWindow });

  assert.match(cardContextFor(on('wsl2'))(VIBE).runtimeNote, UPDATE);
  assert.equal(cardContextFor(on('windows'))(VIBE).runtimeNote, '');
  assert.equal(cardContextFor(on('wsl2', VIBE_ON_LINUX_SINCE))(VIBE).runtimeNote, '');
  for (const version of [BEFORE_LINUX, VIBE_ON_LINUX_SINCE]) {
    assert.match(cardContextFor(on('linux', version))(VIBE).runtimeNote, /native Linux host/u, `native Linux at ${version}`);
    assert.match(cardContextFor(on('wsl1', version))(VIBE).runtimeNote, /WSL1/u, `WSL1 at ${version}`);
  }

  const wsl2 = on('wsl2');
  const page = runPanel(wsl2, { html: catalogHtml(wsl2, 'test-nonce', 'models') });
  const card = pageTree(page.html).one((node) => node.dataset['modelCard'] === 'vibe', 'the Vibe card');
  assert.match(card.text(), UPDATE, 'the person reads it on the card');

  const native = on('linux', VIBE_ON_LINUX_SINCE);
  const nativePage = runPanel(native, { html: catalogHtml(native, 'test-nonce', 'models') });
  const nativeCard = pageTree(nativePage.html).one((node) => node.dataset['modelCard'] === 'vibe', 'the Vibe card');
  assert.doesNotMatch(nativeCard.text(), UPDATE, 'a native Linux card never tells a person to update');
  assert.match(nativeCard.text(), /native Linux host/u);
});

// ---------------------------------------------------------------- the card hint

test('the Vibe hint names Windows, WSL2 from 0.58.0, the .env sign-in, and that native Linux is measured on WSL2 only', () => {
  const hint = VENDOR_PRESETS.find((one) => one.runtime === 'vibe')?.hint ?? '';

  assert.doesNotMatch(hint, /Windows only/u, 'the Windows-only sentence is what this release lifts');
  assert.match(hint, /WSL2/u);
  assert.ok(hint.includes(VIBE_ON_LINUX_SINCE), `the hint does not name coai-mcp ${VIBE_ON_LINUX_SINCE}: ${hint}`);
  assert.ok(hint.includes(VIBE_RUNTIME_SINCE), 'the runtime gate stays in the hint');
  assert.ok(hint.includes('~/.vibe/.env') && hint.includes('vibe --setup'), 'the sign-in file and the command that makes it');
  assert.ok(hint.includes(NATIVE_REFUSED), `the hint does not say what the server means about a native Linux host: ${hint}`);
  assert.doesNotMatch(hint, /Native Linux: measured/u, 'the garbled form of that sentence');
  // A Windows window must not point the row at wsl.exe: WSL2 runs it only when coai-mcp itself runs there.
  assert.ok(hint.includes('when coai-mcp itself runs there (a VS Code window attached to WSL)'), hint);
});

// ---------------------------------------------------------------- the install note, per host

test('⤓ for Vibe on WSL2 says Vibe runs there from 0.58.0 and where the sign-in is kept', () => {
  const note = vendorInstall(VIBE, 'linux', 'wsl2').note;

  assert.ok(note.includes(VIBE_ON_LINUX_SINCE) && note.includes('WSL2'), note);
  assert.ok(note.includes('~/.vibe/.env') && note.includes('vibe --setup'), note);
  assert.match(note, /links? (?:it|that file) into its own homes and never copies/u, 'the sign-in is linked, never copied');
  assert.match(note, /keyring is invisible to coai/u);
  assert.doesNotMatch(note, /does not run Vibe/u, 'the "not yet" sentence is what this release lifts');
  assert.match(note, /▶/u, 'the note still names the button that opens Vibe’s own terminal');
});

test('⤓ for Vibe on native Linux says coai-mcp refuses it for now: measured on WSL2 only', () => {
  const note = vendorInstall(VIBE, 'linux', 'linux').note;

  assert.ok(note.includes(NATIVE_REFUSED), note);
  assert.doesNotMatch(note, /Native Linux: measured/u, 'the garbled form of that sentence');
  assert.doesNotMatch(note, /Vibe runs on this side/u, 'a native host is not told Vibe runs here');
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

test('⤓ for Vibe on WSL1 says it is WSL1 and names the cure: wsl --set-version <distro> 2', () => {
  const note = vendorInstall(VIBE, 'linux', 'wsl1').note;

  assert.match(note, /WSL1/u);
  assert.ok(note.includes('`wsl --set-version <distro> 2`'), note);
  assert.doesNotMatch(note, /Vibe runs on this side/u, 'a WSL1 host is not told Vibe runs here');
  assert.match(note, /vibe --setup/u, 'the sign-in is still said');
});

const PLATFORM_OF: Readonly<Record<HostKind, 'win32' | 'linux' | 'darwin'>> = {
  windows: 'win32', wsl2: 'linux', wsl1: 'linux', linux: 'linux', darwin: 'darwin',
};

test('the note ⤓ and ⟳ show is vibeNoteFor(host) for every host kind, and the five are five different notes', () => {
  const hosts = Object.keys(PLATFORM_OF) as HostKind[];

  for (const host of hosts) {
    assert.equal(vendorInstall(VIBE, PLATFORM_OF[host], host).note, vibeNoteFor(host), `⤓ on ${host}`);
    assert.equal(vendorUpdate(VIBE, PLATFORM_OF[host], '2.26.1', host).note, vibeNoteFor(host), `⟳ on ${host}`);
  }
  // wsl1 and linux each name their own cure; a table that mapped two hosts to one note would hide a wrong one.
  assert.equal(new Set(hosts.map((host) => vibeNoteFor(host))).size, hosts.length);
});

test('a caller that names no host gets its platform’s own: Linux alone is a native host, never WSL2', () => {
  assert.equal(vendorInstall(VIBE, 'linux').note, vibeNoteFor('linux'));
  assert.notEqual(vendorInstall(VIBE, 'linux').note, vibeNoteFor('wsl2'));
  assert.equal(vendorInstall(VIBE, 'win32').note, vibeNoteFor('windows'));
  assert.equal(vendorInstall(VIBE, 'darwin').note, vibeNoteFor('darwin'));
});

test('every host gets the same command, and only the note differs', () => {
  const commands = new Set((['wsl2', 'wsl1', 'linux'] as const).map((host) => vendorInstall(VIBE, 'linux', host).command));

  assert.equal(commands.size, 1);
});

// ---------------------------------------------------------------- the help, in five languages

/** What each language must say about Vibe on Linux — the same four facts, in its own words. */
const SAID: Record<string, readonly string[]> = {
  en: ['WSL2', VIBE_ON_LINUX_SINCE, '~/.vibe/.env', '`vibe --setup`', 'never copies', 'keyring',
    'when coai-mcp itself runs there (a VS Code window attached to WSL)', NATIVE_REFUSED],
  de: ['WSL2', VIBE_ON_LINUX_SINCE, '~/.vibe/.env', '`vibe --setup`', 'kopiert sie nie', 'Schlüsselbund',
    'wenn coai-mcp selbst dort läuft (ein mit WSL verbundenes VS-Code-Fenster)', 'Auf einem nativen Linux-Host lehnt coai-mcp Vibe vorerst ab — es ist nur unter WSL2 gemessen'],
  es: ['WSL2', VIBE_ON_LINUX_SINCE, '~/.vibe/.env', '`vibe --setup`', 'nunca lo copia', 'llavero',
    'cuando el propio coai-mcp se ejecuta allí (una ventana de VS Code conectada a WSL)', 'En un host Linux nativo coai-mcp rechaza Vibe por ahora — solo está medido en WSL2'],
  ru: ['WSL2', VIBE_ON_LINUX_SINCE, '~/.vibe/.env', '`vibe --setup`', 'никогда не копирует', 'связке ключей',
    'когда сам coai-mcp работает там (окно VS Code, подключённое к WSL)', 'На нативном Linux-хосте coai-mcp пока отклоняет Vibe — он измерен только на WSL2'],
  uk: ['WSL2', VIBE_ON_LINUX_SINCE, '~/.vibe/.env', '`vibe --setup`', 'ніколи не копіює', 'в’язці ключів',
    'коли сам coai-mcp працює там (вікно VS Code, під’єднане до WSL)', 'На нативному Linux-хості coai-mcp поки відхиляє Vibe — він виміряний лише на WSL2'],
};

/** The sentences this release replaced, per language: none may survive anywhere in the help. */
const GONE: Record<string, readonly string[]> = {
  en: ['on Windows only for now, because Linux, WSL and macOS are not measured yet', 'Vibe runs only on Windows for now',
    'Native Linux is measured on WSL2 only for now', '(native Linux and macOS are refused for now)'],
  de: ['vorerst nur unter Windows aus, weil Linux, WSL und macOS noch nicht gemessen sind', 'Vibe läuft vorerst nur unter Windows',
    'Natives Linux ist vorerst nur unter WSL2 gemessen', '(natives Linux und macOS werden vorerst abgelehnt)'],
  es: ['por ahora solo en Windows, porque Linux, WSL y macOS aún no están medidos', 'Por ahora Vibe solo funciona en Windows',
    'Linux nativo está medido por ahora solo en WSL2', '(Linux nativo y macOS se rechazan por ahora)'],
  ru: ['пока запускает его только на Windows, потому что Linux, WSL и macOS ещё не измерены', 'Пока Vibe работает только в Windows',
    'На нативном Linux он пока измерен только на WSL2', '(нативный Linux и macOS пока отклоняются)'],
  uk: ['поки запускає його лише на Windows, бо Linux, WSL і macOS ще не виміряні', 'Поки що Vibe працює лише у Windows',
    'На нативному Linux його поки виміряно лише на WSL2', '(нативний Linux і macOS поки відхиляються)'],
};

/** The question-consultant article's one sentence about the host a Vibe row runs on, per language. */
const QUESTION_ROW: Record<string, readonly string[]> = {
  en: ['on WSL2 from coai-mcp 0.58.0 (on a native Linux host or macOS coai-mcp refuses it for now)'],
  de: ['unter WSL2 ab coai-mcp 0.58.0 (auf einem nativen Linux-Host und unter macOS lehnt coai-mcp es vorerst ab)'],
  es: ['en WSL2, desde coai-mcp 0.58.0 (en un host Linux nativo y en macOS coai-mcp lo rechaza por ahora)'],
  ru: ['в WSL2 — начиная с coai-mcp 0.58.0 (на нативном Linux-хосте и на macOS coai-mcp пока его отклоняет)'],
  uk: ['в WSL2 — починаючи з coai-mcp 0.58.0 (на нативному Linux-хості та на macOS coai-mcp поки його відхиляє)'],
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
    assert.ok(everything.includes('WSL2') && everything.includes(VIBE_ON_LINUX_SINCE), `the ${language} help says nothing about WSL2 and ${VIBE_ON_LINUX_SINCE} for a Vibe row`);
    assert.ok(QUESTION_ROW[language]!.every((phrase) => everything.includes(phrase)), `the ${language} question-consultant help does not say what is refused for a Vibe row: ${QUESTION_ROW[language]}`);
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

test('the Vibe wsl row rests on the L-V9 record: every WSL2 cell 3 of 3, and nothing owed', () => {
  const row = vibeRow('wsl');

  assert.equal(row.standing, 'confined');
  assert.ok(row.measured !== undefined && row.source === undefined, 'a measured row, never both');
  assert.equal(row.measured.cliVersion, 'vibe 2.26.1');
  assert.equal(row.measured.date, '2026-10-16');
  assert.match(row.measured.document, /^research\/RESULTS_vibe_confinement\.md/u);
  const cells = row.measured.cells;
  for (const cell of ['WSL2', 'Section 31', 'L-V1', 'L-V2', 'L-V3', 'L-V4', 'write_file', 'bash', 'run_typescript', '~/.vibe']) {
    assert.ok(cells.includes(cell), `the cells do not name ${cell}: ${cells}`);
  }
  assert.ok((cells.match(/3 of 3/gu) ?? []).length >= 5, `L-V1 (signed-out half), the L-V2 read, L-V3, L-V4 are each 3 of 3: ${cells}`);
  assert.doesNotMatch(cells + row.text, /n = 1|one run per cell|still owed|owed/u, 'the one-run record this replaces');
  assert.match(row.text, /WSL2/u);
  assert.doesNotMatch(row.text, /refuses it in WSL|only on Windows/u, 'the refusal this release lifts');
});

test('the Vibe wsl row says what the Windows row says about what Vibe reads: a copy coai makes of the live tree', () => {
  const copy = /copy coai makes of the live tree, without its \.git, \.vibe\/, skills and AGENTS\.md/u;

  assert.match(vibeRow('windows').text, copy, 'the fixture: the Windows row says it');
  assert.match(vibeRow('wsl').text, copy, 'the snapshot is the same on WSL');
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
