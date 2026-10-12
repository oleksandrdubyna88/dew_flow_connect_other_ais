import assert from 'node:assert/strict';
import { test } from 'node:test';

import { bySideOf, pathForThisSide, type ExecutableBySide, type ExecutableSide, type PathFamily } from '../pathFamily';
import { DEFAULT_VENDORS, vendorsFrom, type Vendor } from '../vendors';
import { rowOnTheWire } from '../vendorsWire';
import { chatModelsOf } from '../chatCatalogModels';
import { claudeExecutableFor } from '../claudeCli';
import { DEFAULT_CONSULT, resolveConsultant } from '../consultSettings';
import { hostFamily } from '../hostSide';
import { qconsultSettingsFrom } from '../qconsultSettings';
import { cliGroups } from '../setupTab';
import { executableFor, executableForRuntime } from '../vendorTerminal';

/**
 * A reviewer row stores a CLI path PER SIDE (research/PLAN_paths_per_side.md E3.1, design (b) O2 and (c) rule 1): VS Code
 * shares `coai.vendors` between a Windows window and a WSL window, and one `executablePath` cannot hold both a
 * `C:\…\codex.cmd` and a `/usr/local/bin/codex`. `executablePathBySide: { windows?, posix?, mirrored? }` is additive
 * on the row — an older extension ignores it, coai-mcp never sees it — and a PRESENT per-side value wins over the
 * legacy one, an empty one included (it is the PATH lookup, never a fall-through to the legacy value).
 */

const WIN = 'C:\\tools\\codex.cmd';
const POSIX = '/opt/codex/bin/codex';

/** A stored row read through the real reader, so what the tests hold is what a save writes back. */
function row(executablePath: string, executablePathBySide?: ExecutableBySide): Vendor {
  const stored: Record<string, unknown> = { ...DEFAULT_VENDORS[0]!, id: 'codex', runtime: 'codex', enabled: true, executablePath, executablePathBySide };

  return vendorsFrom([stored])[0]!;
}

/** The side the row is judged on; the disk answers absent for every path, so a legacy value of the other OS is skipped. */
function side(family: PathFamily): ExecutableSide {
  return { family, systemDrive: 'C:', fileAt: () => 'absent' };
}

/** The `executablePath` this side's writer puts into COAI_VENDORS for the row. */
function wired(vendor: Vendor, family: PathFamily): unknown {
  return rowOnTheWire(vendor, '', () => undefined, [], side(family))['executablePath'];
}

test('a row whose per-side value for THIS side is present sends that value — not the legacy value of the other side it skipped today', () => {
  const both = row(WIN, { posix: POSIX });

  assert.equal(wired(both, 'posix'), POSIX, 'the WSL side runs its own per-side value');
  assert.equal(wired(both, 'windows'), WIN, 'the Windows side, with no per-side value of its own, still runs the legacy one');
  assert.deepEqual(pathForThisSide(both, side('posix')), { path: POSIX, otherSide: '' }, 'nothing was skipped: the per-side value is this side\'s');
});

test('a present but EMPTY per-side value is the PATH lookup — it never resurrects the legacy value', () => {
  // The cadence consultation for epics 4-6 (finding a): an emptied field that fell through to a legacy `//server/share/codex`
  // — spelled for neither OS — would have run that share on both sides.
  const cleared = row('//server/share/codex', { windows: '', posix: POSIX });

  assert.equal(wired(cleared, 'windows'), '', 'cleared on Windows: PATH');
  assert.equal(wired(cleared, 'posix'), POSIX);
  assert.deepEqual(pathForThisSide(cleared, side('windows')), { path: '', otherSide: '' }, 'nothing was skipped either: the field is empty on purpose');
});

test('the reader keeps the per-side field — members trimmed, an empty member kept — and a row without it is written back without the key', () => {
  const kept = row(WIN, { windows: `  ${WIN}  `, posix: '', mirrored: ` ${WIN} ` });
  assert.deepEqual(kept.executablePathBySide, { windows: WIN, posix: '', mirrored: WIN });

  const legacyOnly = row(WIN);
  assert.equal(Object.hasOwn(legacyOnly, 'executablePathBySide'), false, 'an older extension\'s row stays byte-identical on the way back');
  assert.equal(Object.hasOwn(row(WIN, {}), 'executablePathBySide'), false, 'an empty object says nothing and is not kept');
});

test('bySideOf reads only string members, by name — a hand edit of any other shape reads as nothing', () => {
  assert.deepEqual(bySideOf({ windows: WIN, posix: 3, other: 'x' }), { windows: WIN });
  assert.equal(bySideOf({ posix: null }), undefined);
  assert.equal(bySideOf('not a record'), undefined);
  assert.equal(bySideOf(['/x']), undefined);
  assert.deepEqual(bySideOf({ mirrored: '' }), { mirrored: '' }, 'an empty mirrored copy is a record of a cleared save');
});

test('the wire carries ONE string per row: no per-side member reaches any coai-mcp, old or new', () => {
  // Design (b): the extension resolves this side's value before it writes the server's settings file, so every server
  // version keeps reading one `executablePath`.
  const crossed = rowOnTheWire(row(WIN, { windows: WIN, posix: POSIX, mirrored: WIN }), '', () => undefined, [], side('windows'));

  assert.equal(Object.hasOwn(crossed, 'executablePathBySide'), false, JSON.stringify(crossed));
  assert.equal(crossed['executablePath'], WIN);
});

/** This host's family, its own spelling and the other side's — the launch sites read the host's side. */
const HERE = hostFamily();
const MINE = HERE === 'windows' ? WIN : POSIX;
const THEIRS = HERE === 'windows' ? POSIX : WIN;

/** A row whose legacy value is the OTHER side's, and whose per-side value for this host is its own. */
function splitRow(runtime: string, uses?: readonly string[]): Vendor {
  const stored = { ...DEFAULT_VENDORS[0]!, id: 'codex', runtime, enabled: true, executablePath: THEIRS, executablePathBySide: { [HERE]: MINE }, ...(uses === undefined ? {} : { uses }) };

  return vendorsFrom([stored])[0]!;
}

test('every launch of a row runs its per-side value on this side — not the legacy value it would skip', () => {
  assert.equal(executableFor(splitRow('codex')), MINE, 'the vendor terminal');
  assert.equal(executableForRuntime('codex', [splitRow('codex')]), MINE, 'the runtime\'s CLI');
  assert.equal(claudeExecutableFor([splitRow('claude')], DEFAULT_CONSULT), MINE, 'the Claude probe');
});

test('a copy of a row into a definition takes this side\'s stored value, so the copy runs what the row would', () => {
  const consultant = resolveConsultant({ vendor: 'codex', runtime: '', model: '', baseUrl: '', executablePath: '' }, [splitRow('codex')]);
  assert.equal(consultant.kind === 'definition' ? consultant.executablePath : 'not a definition', MINE, 'a consultant through the row');

  const [chat] = chatModelsOf([], [splitRow('claude', ['chat'])], []);
  assert.equal(chat?.executablePath, MINE, 'a chat model from the row');

  const read = (key: string): unknown => ({
    vendors: [{ ...splitRow('codex', ['qconsult']) }],
    qconsultRows: [{ id: 'q1', vendor: 'codex', runtime: '', model: '', baseUrl: '', executablePath: '', key: '', prompt: 'p', enabled: true }],
  } as Record<string, unknown>)[key];
  assert.equal(qconsultSettingsFrom(read).rows[0]?.executablePath, MINE, 'a question row through the row');
});

test('the CLI table shows THIS side\'s stored value (the card\'s box: aCliPathOfTheOtherSideOnTheCard.test.ts)', () => {
  assert.equal(cliGroups([splitRow('codex')], HERE)[0]?.executablePath, MINE);
});
