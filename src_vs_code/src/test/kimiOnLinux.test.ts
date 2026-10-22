import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';

import { compareVersions } from '../coaiInstall';
import { HELP_ARTICLES, HELP_LANGUAGES, bodyFor } from '../helpContent';
import type { HostKind } from '../hostSide';
import { KIMI_ON_LINUX_SINCE, KIMI_RUNTIME_SINCE, ON_LINUX_SINCE, runtimeOnLinuxSide, runtimeSkewNote } from '../runtimeSince';
import { kimiNoteFor, vendorInstall, vendorUpdate } from '../vendorTerminal';
import { VENDOR_PRESETS, type Vendor } from '../vendors';

/**
 * Kimi on a Linux host in the extension — E6 of research/PLAN_kimi_and_vibe_on_linux.md: what the panel says per host, from which
 * coai-mcp, and what the shared limitation rows claim. The install LINE is `kimiLinuxInstall.test.ts`'s; the marker button is
 * `kimiClearMarker.test.ts`'s.
 *
 * <p>Per host kind (E3's union, `hostSide.ts`: windows, wsl2, wsl1, linux, darwin): Windows is as it
 * was; WSL2 runs the row from KIMI_ON_LINUX_SINCE; WSL1 is refused ("measured on WSL2, and this is WSL1"); native Linux is
 * "measured on WSL2 only for now"; macOS is not measured.</p>
 */

const KIMI: Vendor = {
  id: 'kimi', runtime: 'kimi', model: 'kimi-code/k3-256k', enabled: true, plan: true, code: true,
  baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0,
};
const HOSTS: readonly HostKind[] = ['windows', 'wsl2', 'wsl1', 'linux', 'darwin'];
const repo = (...parts: string[]): string => join(__dirname, '..', '..', '..', ...parts);

// ---------------------------------------------------------------- DL14: the table

test('KIMI_ON_LINUX_SINCE is 0.61.0 — Vibe-on-Linux shipped in 0.59.0 and main released 0.60.0 without Kimi (DL14) — and is in the table beside it', () => {
  assert.equal(KIMI_ON_LINUX_SINCE, '0.61.0');
  assert.equal(ON_LINUX_SINCE['kimi'], KIMI_ON_LINUX_SINCE);
  assert.ok(compareVersions('0.58.0', KIMI_ON_LINUX_SINCE) < 0, 'one minor after Vibe’s release');
});

test('a Linux or WSL side whose server is older than KIMI_ON_LINUX_SINCE does not run Kimi; a newer or an unknown server does; Windows is untouched', () => {
  assert.equal(runtimeOnLinuxSide('kimi', '0.58.0'), false);
  assert.equal(runtimeOnLinuxSide('kimi', KIMI_RUNTIME_SINCE), false, 'knowing the runtime is not running it on this host');
  assert.equal(runtimeOnLinuxSide('kimi', '0.59.0'), false, 'the release Vibe-on-Linux ships in does not run Kimi');
  assert.equal(runtimeOnLinuxSide('kimi', KIMI_ON_LINUX_SINCE), true);
  assert.equal(runtimeOnLinuxSide('kimi', '1.0.0'), true);
  assert.equal(runtimeOnLinuxSide('kimi', ''), true, 'an unknown server is not called old');

  const note = runtimeSkewNote('0.58.0', [KIMI], 'wsl2');
  assert.match(note, /update coai-mcp to run Kimi here/iu);
  assert.ok(note.includes('0.58.0') && note.includes(KIMI_ON_LINUX_SINCE), note);
  assert.equal(runtimeSkewNote('0.58.0', [KIMI], 'windows'), '', 'on Windows a 0.58.0 server runs Kimi');
  assert.equal(runtimeSkewNote(KIMI_ON_LINUX_SINCE, [KIMI], 'wsl2'), '', 'a server at KIMI_ON_LINUX_SINCE runs it on WSL2');
  assert.match(runtimeSkewNote(KIMI_ON_LINUX_SINCE, [KIMI], 'linux'), /refuses a kimi row on a native Linux host for now — Kimi is measured on WSL2 only/u, 'native Linux: refused at ANY version, never "update"');
  assert.match(runtimeSkewNote(KIMI_ON_LINUX_SINCE, [KIMI], 'wsl1'), /This is WSL1, and coai-mcp runs Kimi on WSL2 only/u);
  assert.match(runtimeSkewNote('0.56.0', [KIMI], 'wsl2'), new RegExp(KIMI_RUNTIME_SINCE.replaceAll('.', '\\.'), 'u'), 'a server that does not know Kimi at all speaks first, once');
  assert.doesNotMatch(runtimeSkewNote('0.56.0', [KIMI], 'wsl2'), /run Kimi here/iu);
});

// ---------------------------------------------------------------- the card hint

test('the Kimi hint names Windows, WSL2 from KIMI_ON_LINUX_SINCE, native Linux as measured on WSL2 only, and the wait for your own open kimi', () => {
  const hint = VENDOR_PRESETS.find((one) => one.runtime === 'kimi')?.hint ?? '';

  assert.doesNotMatch(hint, /Windows only/u);
  assert.match(hint, /WSL2/u);
  assert.ok(hint.includes(KIMI_ON_LINUX_SINCE), hint);
  assert.ok(hint.includes(KIMI_RUNTIME_SINCE), 'the runtime gate stays in the hint');
  assert.match(hint, /On a native Linux host coai-mcp refuses Kimi for now — it is measured on WSL2 only/u);
  assert.match(hint, /when coai-mcp itself runs there/u);
  assert.match(hint, /your own kimi is open, coai’s Kimi turns wait for it and then answer busy/u, 'Q3');
  assert.match(hint, /kimi login/u);
  assert.match(hint, /one after another/u);
});

// ---------------------------------------------------------------- the install note, per host kind

test('⤓ for Kimi says, per host kind, whether coai-mcp runs the row there', () => {
  const note = (host: HostKind): string => kimiNoteFor(host);

  assert.match(note('wsl2'), new RegExp(`runs on this side, WSL2, from coai-mcp ${KIMI_ON_LINUX_SINCE.replaceAll('.', '\\.')}`, 'u'));
  assert.match(note('wsl2'), /your own `kimi` is open on this side, coai’s Kimi turns wait for it to close and then answer busy/u, 'Q3');
  assert.match(note('linux'), /On a native Linux host coai-mcp refuses Kimi for now — it is measured on WSL2 only/u);
  assert.match(note('wsl1'), /This is WSL1, and coai-mcp runs Kimi on WSL2 only/u);
  assert.match(note('wsl1'), /wsl --set-version <distro> 2/u, 'what helps on WSL1 is moving the distro, not updating coai-mcp');
  assert.match(note('darwin'), /does not run Kimi on macOS/u);
  assert.match(note('windows'), /Git for Windows/u, 'Windows is as it was');
  assert.doesNotMatch(note('linux'), /WSL1/u);
  assert.doesNotMatch(note('wsl2'), /native Linux|This is WSL1|macOS/u, 'WSL2 is told none of the refusals');
  assert.doesNotMatch(note('windows'), /WSL2|apt-get/u);
  assert.equal(new Set(HOSTS.map(note)).size, HOSTS.length, 'five host kinds, five different notes');
});

test('the notes say a FAILED TOOL step stops the line — not that every step does: Moonshot’s piped script has no pipefail (N4)', () => {
  for (const host of ['wsl2', 'wsl1', 'linux'] as const) {
    assert.match(kimiNoteFor(host), /a failed tool step stops the line/u, host);
    assert.doesNotMatch(kimiNoteFor(host), /a step that fails stops the line/u, host);
  }
});

test('every Linux host note says what the button does: sudo, the distribution’s packages, the fdfind link, the generic sentence, the check', () => {
  for (const host of ['wsl2', 'wsl1', 'linux'] as const) {
    const note = kimiNoteFor(host);

    for (const part of ['sudo apt-get', 'sudo dnf', 'your password', 'fdfind', '~/.local/bin/fd', 'install ripgrep and fd with your package manager', 'where `rg` and `fd` resolved', '/login']) {
      assert.ok(note.includes(part), `${host}: the note never says "${part}"`);
    }
  }
});

test('⤓ and ⟳ take the host kind: the note follows it, the line is one for every Linux kind, and no host means the platform’s own answer', () => {
  const platformOf = (host: HostKind) => (host === 'windows' ? 'win32' : host === 'darwin' ? 'darwin' : 'linux');
  for (const host of HOSTS) {
    assert.equal(vendorInstall(KIMI, platformOf(host), host).note, kimiNoteFor(host), host);
  }
  assert.equal(vendorUpdate(KIMI, 'linux', '2.2.0', 'wsl2').note, kimiNoteFor('wsl2'));
  assert.equal(vendorInstall(KIMI, 'linux').note, kimiNoteFor('linux'), 'no host on Linux is native: the safe answer, which only ever shows a refusal');
  assert.equal(vendorInstall(KIMI, 'darwin').note, kimiNoteFor('darwin'));
  const line = vendorInstall(KIMI, 'linux', 'wsl2').command;
  for (const host of ['wsl1', 'linux'] as const) {
    assert.equal(vendorInstall(KIMI, 'linux', host).command, line, host + ': the same line; only the sentence about running the row differs');
  }
});

test('Kimi stays refused on a native Linux host after E2 (E3 measures it), and a host whose banner was not read is told the same refusal', () => {
  const nativeSentence = 'On a native Linux host coai-mcp refuses Kimi for now — it is measured on WSL2 only — so a Kimi row is refused on this side.';

  assert.ok(kimiNoteFor('linux').includes(nativeSentence), 'Kimi’s native note moved with Vibe’s');
  assert.equal(kimiNoteFor('linux-unread'), kimiNoteFor('linux'), 'an unread banner is refused for Kimi with its native sentence: Kimi is refused on native Linux anyway');
  assert.equal(vendorInstall(KIMI, 'linux', 'linux-unread').note, kimiNoteFor('linux-unread'));
  assert.equal(vendorUpdate(KIMI, 'linux', '2.2.0', 'linux-unread').note, kimiNoteFor('linux-unread'));
  assert.equal(vendorInstall(KIMI, 'linux', 'linux-unread').command, vendorInstall(KIMI, 'linux', 'wsl2').command, 'the same line on every Linux kind');
  assert.match(runtimeSkewNote('0.64.0', [KIMI], 'linux-unread'), /could not read .* kernel banner/u, 'the card says why an unread host refuses a kimi row');
  assert.match(runtimeSkewNote('0.64.0', [KIMI], 'linux-unread'), /refuses a kimi row here/u);
});

// ---------------------------------------------------------------- the help, in five languages

/** What each language must say about Kimi on Linux, in its own words. */
const SAID: Record<string, readonly string[]> = {
  en: ['WSL2', KIMI_ON_LINUX_SINCE, 'native Linux host', 'WSL1', '`kimi` is open', '`sudo apt-get`', '`sudo dnf`', '`~/.local/bin/fd`', '`fdfind`', '`rg` and `fd`'],
  de: ['WSL2', KIMI_ON_LINUX_SINCE, 'nativen Linux', 'WSL1', '`kimi` offen', '`sudo apt-get`', '`sudo dnf`', '`~/.local/bin/fd`', '`fdfind`', '`rg` und `fd`'],
  es: ['WSL2', KIMI_ON_LINUX_SINCE, 'Linux nativo', 'WSL1', '`kimi` esté abierto', '`sudo apt-get`', '`sudo dnf`', '`~/.local/bin/fd`', '`fdfind`', '`rg` y `fd`'],
  ru: ['WSL2', KIMI_ON_LINUX_SINCE, 'нативном Linux', 'WSL1', '`kimi`, ходы', '`sudo apt-get`', '`sudo dnf`', '`~/.local/bin/fd`', '`fdfind`', '`rg` и `fd`'],
  uk: ['WSL2', KIMI_ON_LINUX_SINCE, 'нативному Linux', 'WSL1', '`kimi`, ходи', '`sudo apt-get`', '`sudo dnf`', '`~/.local/bin/fd`', '`fdfind`', '`rg` і `fd`'],
};

/** The sentences this release replaced: none may survive in any language. */
const GONE: Record<string, readonly string[]> = {
  en: ['runs it on Windows only for now, it needs coai-mcp 0.57.0', 'Moonshot’s own script on Linux and macOS, and on Windows one line'],
  de: ['führt sie vorerst nur unter Windows aus, sie braucht coai-mcp 0.57.0', 'Moonshots eigenes Skript unter Linux und macOS und unter Windows eine Zeile'],
  es: ['la ejecuta por ahora solo en Windows, necesita coai-mcp 0.57.0', 'script propio de Moonshot en Linux y macOS, y en Windows una sola línea'],
  ru: ['пока запускает его только на Windows, ему нужен coai-mcp 0.57.0', 'скрипт Moonshot на Linux и macOS, а на Windows — одна строка'],
  uk: ['поки що запускає його лише на Windows, йому потрібен coai-mcp 0.57.0', 'скрипт Moonshot на Linux і macOS, а на Windows — один рядок'],
};

test('the five languages say the same about Kimi on Linux: WSL2 from KIMI_ON_LINUX_SINCE, native Linux and WSL1 not, the wait for your own kimi, the install line', () => {
  const article = HELP_ARTICLES.find((one) => one.id === 'choose-reviewers');
  assert.ok(article !== undefined, 'the reviewers article has been renamed, and this test is now asserting nothing');
  assert.deepEqual([...HELP_LANGUAGES].sort(), Object.keys(SAID).sort(), 'a language was added: say what it must say');

  for (const language of HELP_LANGUAGES) {
    const { body, fallback } = bodyFor(article, language);
    const said = `${body.setup} ${body.usage}`;

    assert.equal(fallback, false, `the ${language} article is missing, so a reader gets English`);
    for (const word of SAID[language]!) {
      assert.ok(said.includes(word), `the ${language} article never says ${word} about Kimi on Linux`);
    }
    for (const old of GONE[language]!) {
      assert.ok(!said.includes(old), `the ${language} article still says: ${old}`);
    }
  }
});

// ---------------------------------------------------------------- shared/consultant-limitations.json

interface Row {
  readonly runtime: string; readonly platform: string; readonly standing: string; readonly text: string; readonly source?: string;
  readonly measured?: { readonly date: string; readonly cliVersion: string; readonly cells: string; readonly document: string };
}

// reads-another-program: a positive claim about the shared file the server embeds and the Consultant tab prints — the Kimi rows
// E6 names (`research/PLAN_kimi_and_vibe_on_linux.md`), from research/RESULTS_kimi_confinement.md section 29.
const ROWS = (JSON.parse(readFileSync(repo('shared', 'consultant-limitations.json'), 'utf8')) as { rows: Row[] }).rows;
const kimiRow = (platform: string): Row => {
  const row = ROWS.find((one) => one.runtime === 'kimi' && one.platform === platform);
  assert.ok(row !== undefined, `no kimi row for ${platform}`);

  return row;
};

test('the Kimi wsl row is measured on WSL2 and says what section 29 measured — Grep and Glob are NOT bounded, writes are refused — one run per cell', () => {
  const row = kimiRow('wsl');

  assert.equal(row.standing, 'unconfined', 'Grep of a path outside the working folder was not stopped (section 29, L-K7)');
  assert.ok(row.measured !== undefined && row.source === undefined, 'a measured row, never both');
  assert.equal(row.measured.cliVersion, 'kimi 2.2.0');
  assert.equal(row.measured.date, '2026-10-16');
  assert.match(row.measured.document, /^research\/RESULTS_kimi_confinement\.md/u);
  for (const cell of ['WSL2', 'L-K7', 'L-K2', 'L-K1', 'n = 1', 'Glob', 'Windows section 9', 'assumed']) {
    assert.ok(row.measured.cells.includes(cell), `the cells do not name ${cell}: ${row.measured.cells}`);
  }
  assert.match(row.text, /Grep and Glob search the disk directly/u);
  assert.match(row.text, /every write and shell call was refused/u);
  assert.match(row.text, /WSL2/u);
});

test('the Kimi linux row says measured on WSL2 only and claims nothing about native Linux', () => {
  const row = kimiRow('linux');

  assert.equal(row.standing, 'unmeasured');
  assert.match(row.text, /measured on WSL2 only/u);
  assert.match(row.source ?? '', /WSL2/u);
  assert.ok(row.measured === undefined);
});
