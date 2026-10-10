import assert from 'node:assert/strict';
import { test } from 'node:test';
import { statSync as statSyncForProbe } from 'node:fs';
import { fileAt, useFileProbe } from '../pathFamily';
import fs from 'node:fs';
import path from 'node:path';

import { chatModelPresetsFrom, chatRunSpec } from '../chatPresets';
import { claudeExecutableFor } from '../claudeCli';
import { CALLER_KINDS, DEFAULT_CONSULT, resolveConsultant, type ConsultSettings } from '../consultSettings';
import { hostFamily } from '../hostSide';
import { type FilePresence } from '../pathFamily';
import { DEFAULT_VENDORS, vendorsFrom, type Vendor } from '../vendors';
import { rowOnTheWire } from '../vendorsWire';
import { executableFor, executableForRuntime } from '../vendorTerminal';

// The host's disk, as activation installs it (paths per side, E1.2) — the rule asks it about other-OS spellings.
useFileProbe(fileAt(statSyncForProbe));

/**
 * A CLI path spelled for the OTHER operating system is skipped — the runtime's CLI is looked up on PATH — never launched
 * and never sent to the server (todo/PLAN_paths_per_side.md E1.2). VS Code's settings are shared by a WSL window and a
 * Windows window, so a reviewer row's `C:\…\codex.cmd` reaches the WSL side, whose server probed it as a file, found
 * nothing, and the card said "cannot review" for a row that is right on the side that wrote it.
 */

const row = (executablePath: string, extra: Record<string, unknown> = {}): Vendor =>
  vendorsFrom([{ ...DEFAULT_VENDORS[0]!, id: 'codex', runtime: 'codex', enabled: true, executablePath, ...extra }])[0]!;

/** The wire as a side with this family writes it, the disk answering `presence` for every path it is asked about. */
const wired = (vendor: Vendor, family: 'windows' | 'posix', presence: FilePresence = 'absent'): unknown =>
  rowOnTheWire(vendor, '', () => undefined, [], { family, systemDrive: 'C:', fileAt: () => presence })['executablePath'];

/** A CLI path spelled for the OS this host does NOT run — the other side's, wherever the suite runs. */
const otherSides = hostFamily() === 'windows' ? '/usr/local/bin/claude' : 'C:\\Users\\jinx\\AppData\\Roaming\\npm\\claude.cmd';
const thisSides = hostFamily() === 'windows' ? 'C:\\tools\\claude.exe' : '/opt/claude/bin/claude';

test('a Windows-side writer given a POSIX CLI path sends the server nothing — the PATH lookup — and keeps its own paths', () => {
  assert.equal(wired(row('/usr/local/bin/codex'), 'windows'), '', 'the WSL CLI is the other side\'s on Windows');
  assert.equal(wired(row('C:\\Users\\jinx\\AppData\\Roaming\\npm\\codex.cmd'), 'posix'), '', 'the Windows shim is the other side\'s in WSL');
  assert.equal(wired(row('/usr/local/bin/codex'), 'posix'), '/usr/local/bin/codex', 'kept on the side it was written for');
  assert.equal(wired(row('C:\\tools\\codex.exe'), 'windows'), 'C:\\tools\\codex.exe');
  assert.equal(wired(row(''), 'windows'), '', 'nothing set stays the PATH lookup');
});

test('an EXISTING root-relative CLI on Windows crosses qualified with the system drive — this side\'s, never a PATH install', () => {
  // The cadence consultant (E1): `/Program Files/nodejs/node.exe` is a legal Windows path; skipping it lexically ran another installation.
  assert.equal(wired(row('/Program Files/nodejs/node.exe'), 'windows', 'here'), 'C:\\Program Files\\nodejs\\node.exe');
  assert.equal(wired(row('/Program Files/nodejs/node.exe'), 'windows', 'unknown'), 'C:\\Program Files\\nodejs\\node.exe', 'skipped on a guess');
});

test('a reviewer row\'s other-side path launches the runtime\'s own name; this side\'s path launches as written', () => {
  assert.equal(executableFor(row(otherSides, { runtime: 'claude' })), 'claude');
  assert.equal(executableFor(row(thisSides, { runtime: 'claude' })), thisSides);
  assert.equal(executableForRuntime('claude', [row(otherSides, { runtime: 'claude' })]), 'claude');
});

function consulting(executablePath: string): ConsultSettings {
  return {
    ...DEFAULT_CONSULT,
    byCaller: {
      ...Object.fromEntries(CALLER_KINDS.map(({ id }) => [id, resolveConsultant({ vendor: 'nothing-here', runtime: '', model: '', baseUrl: '', executablePath: '' }, [])])),
      claude: resolveConsultant({ vendor: 'claude', runtime: 'claude', model: '', baseUrl: '', executablePath }, []),
    },
  };
}

test('the Claude probe skips an other-side path on a row AND on a consultant, and asks the next one that is this side\'s', () => {
  assert.equal(claudeExecutableFor([row(otherSides, { runtime: 'claude' })], consulting('')), 'claude', 'the row\'s path is the other side\'s');
  assert.equal(claudeExecutableFor([row(otherSides, { runtime: 'claude' })], consulting(thisSides)), thisSides, 'the consultant\'s own path is this side\'s');
  assert.equal(claudeExecutableFor([], consulting(otherSides)), 'claude', 'the consultant\'s path is the other side\'s too');
});

test('a chat model saved with an other-side CLI path runs the runtime from PATH here', () => {
  const [preset] = chatModelPresetsFrom([{ id: 'p1', name: 'mine', runtime: 'claude', model: '', executablePath: otherSides }]);
  assert.ok(preset !== undefined && preset.executablePath === otherSides, 'the fixture is a preset the reader keeps, with its path');

  assert.equal(chatRunSpec(preset).executablePath, '');
  assert.equal(chatRunSpec({ ...preset, executablePath: thisSides }).executablePath, thisSides);
});

/**
 * Every READ of a stored `executablePath` in the shipped source either goes through the side rule (`executableHere`,
 * `pathForThisSide`, or a local `here(…)` over it) or is one of the counted reads that only COPY or SHOW the stored value
 * — a copy is resolved where it is launched or written. A new raw read changes a count and goes red, so a launch site
 * cannot be added that runs the other side's CLI.
 */
const UNRESOLVED_READS: Readonly<Record<string, { readonly count: number; readonly why: string }>> = {
  'catalogEdit.ts': { count: 2, why: 'copies the stored launch fields between a row and a definition' },
  'catalogLaunch.ts': { count: 3, why: 'compares a row\'s launch identity, and copies it — the stored value, never run' },
  'catalogMigration.ts': { count: 2, why: 'moves a stored definition into a catalog row' },
  'chatCatalogModels.ts': { count: 1, why: 'copies a catalog row into a chat model, which chatRunSpec resolves' },
  'chatModelEdits.ts': { count: 1, why: 'an edit of the stored value' },
  'chatPresetMove.ts': { count: 1, why: 'moves a stored preset into the catalog' },
  'chatPresets.ts': { count: 1, why: 'turns a reviewer row into a stored preset' },
  'consultSettings.ts': { count: 3, why: 'resolves a stored definition and matches it to a row — the stored value' },
  'qconsultSettings.ts': { count: 1, why: 'copies a catalog row into a question row, which rowsOnTheWire resolves' },
  'panelView.ts': { count: 1, why: 'draws the stored value in the card\'s CLI-path box' },
  'setupTab.ts': { count: 4, why: 'draws the stored value in the vendor-keys table' },
  'pathFamily.ts': { count: 1, why: 'the side rule itself' },
};

const READ = /\.executablePath\b/g;
const RESOLVED = /(?:executableHere|pathForThisSide|here)\([^()]*\.executablePath\b/g;

function rawReads(file: string): number {
  const text = fs.readFileSync(file, 'utf8').split('\n').filter((line) => !/^\s*(?:\*|\/\/|\/\*\*)/.test(line)).join('\n');

  return (text.match(READ) ?? []).length - (text.match(RESOLVED) ?? []).length;
}

function shippedSources(): readonly string[] {
  const dir = path.join(__dirname, '..', '..', 'src');

  return fs.readdirSync(dir).filter((name) => name.endsWith('.ts') && !name.endsWith('.generated.ts') && name !== 'pathSettings.ts').sort();
}

test('every read of a stored CLI path goes through the side rule, or is a counted copy or display', () => {
  const dir = path.join(__dirname, '..', '..', 'src');
  const found = Object.fromEntries(shippedSources().map((name) => [name, rawReads(path.join(dir, name))] as const).filter(([, count]) => count > 0));
  const expected = Object.fromEntries(Object.entries(UNRESOLVED_READS).map(([name, one]) => [name, one.count]));

  assert.deepEqual(found, expected, 'a read of executablePath that is not resolved for this side: route it through executableHere, or count it here with why');
});

test('the scan still sees a resolved read and an unresolved one — a pattern that matched nothing would pass forever', () => {
  const wire = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'vendorsWire.ts'), 'utf8');
  assert.ok((wire.match(RESOLVED) ?? []).length > 0, 'vendorsWire.ts resolves its read, and the scan sees it');
  assert.ok(rawReads(path.join(__dirname, '..', '..', 'src', 'panelView.ts')) > 0, 'the card\'s box is a raw read the scan sees');
});
