import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';

import { savedCliPath } from '../cliPathSave';
import { pathForThisSide, type ExecutableSide, type FilePresence, type PathFamily } from '../pathFamily';
import { DEFAULT_VENDORS, vendorsFrom, type Vendor } from '../vendors';
import { rowOnTheWire } from '../vendorsWire';

/**
 * Compatibility of the per-side CLI path with extensions that do not know it (todo/PLAN_paths_per_side.md E3.5).
 *
 * <p>VS Code shares `coai.vendors` between a Windows window and a WSL window, and a WSL remote installs its OWN copy of
 * the extension, which can lag a version. So one row is read and written by two extension versions at once. These tests
 * run in CI on this build alone; `scripts/live-paths-per-side-compat.mjs` runs the same three questions against the
 * RELEASED extension and the released coai-mcp.</p>
 *
 * <ol>
 *   <li>A row an older extension wrote (the legacy `executablePath` only) resolves exactly as before — every shared
 *       executable vector, as a stored row, on the wire — and is written back without a per-side member.</li>
 *   <li>Mixed versions, the limitation named by the cadence consultation for epics 4-6 (finding b): an older reader
 *       sends the ONE legacy value, which is what the side that saved last mirrored.</li>
 *   <li>An older extension's save rewrites the list through its own reader, which drops the field it does not know: a
 *       newer reader then resolves that save, for the family it is spelled for.</li>
 * </ol>
 */

const W = 'C:\\tools\\codex.cmd';
const P = '/opt/codex/bin/codex';
const P2 = '/usr/local/bin/codex';

/** A stored row read back the way the extension reads `coai.vendors` — a JSON round trip, then the real reader. */
function stored(row: Readonly<Record<string, unknown>>): Vendor {
  return vendorsFrom(JSON.parse(JSON.stringify([{ ...DEFAULT_VENDORS[0]!, id: 'codex', runtime: 'codex', enabled: true, ...row }])) as unknown)[0]!;
}

/** A side of `family`, its disk answering `presence` for every path it is asked about. */
function sideOf(family: PathFamily, presence: FilePresence = 'absent', systemDrive = 'C:'): ExecutableSide {
  return { family, systemDrive, fileAt: () => presence };
}

/** The CLI path one row carries in `COAI_VENDORS`, as a side of `side` writes the file. */
function wired(row: Vendor, side: ExecutableSide): unknown {
  return rowOnTheWire(row, '', () => undefined, [], side)['executablePath'];
}

/**
 * What an extension that does not know `executablePathBySide` stores when it saves the list: its reader lists the
 * fields it knows (`vendors.ts` `vendorsFrom` at extension-v0.67.0 builds every row from named fields), so the per-side
 * member is not carried — measured against the released build by `live-paths-per-side-compat.mjs`.
 */
function olderSave(row: Vendor, executablePath: string): Vendor {
  const { executablePathBySide: _dropped, ...known } = row;

  return stored({ ...known, executablePath });
}

/** The shared executable vectors, each as the stored legacy row an older extension wrote. */
interface ExecutableVector {
  readonly path: string;
  readonly family: PathFamily;
  readonly systemDrive: string;
  readonly presence: FilePresence;
  readonly here: string;
  readonly why: string;
}

function executableVectors(): readonly ExecutableVector[] {
  const file = path.join(__dirname, '..', '..', '..', 'shared', 'path-family-vectors.json');
  const rows = (JSON.parse(fs.readFileSync(file, 'utf8')) as { executable?: unknown }).executable;
  assert.ok(Array.isArray(rows) && rows.length > 5, 'shared/path-family-vectors.json carries the executable vectors');

  return rows.map((row: Readonly<Record<string, unknown>>) => ({
    path: String(row['path']),
    family: row['windows'] === true ? 'windows' : 'posix',
    systemDrive: String(row['systemDrive']),
    presence: row['existsHere'] === true ? 'here' : row['unknownHere'] === true ? 'unknown' : 'absent',
    here: String(row['here']),
    why: String(row['why']),
  }));
}

test('a row an older extension wrote — the legacy executablePath only — goes on the wire exactly as every executable vector says, on both sides', () => {
  for (const vector of executableVectors()) {
    const row = stored({ executablePath: vector.path });
    const said = `${vector.family}: ${JSON.stringify(vector.path)} — ${vector.why}`;

    assert.equal(wired(row, sideOf(vector.family, vector.presence, vector.systemDrive)), vector.here, said);
    assert.equal(pathForThisSide(row, sideOf(vector.family, vector.presence, vector.systemDrive)).path, vector.here, said);
    assert.equal('executablePathBySide' in row, false, `${said}: the row read back carries no per-side member, so a save writes it back byte for byte`);
  }
});

test('mixed versions: a newer Windows two-field save mirrors W into the legacy value, which is what an older reader sends — and a POSIX side skips it (the named limitation)', () => {
  const windowsSave = stored({ ...savedCliPath(stored({ executablePath: '' }), W, 'windows', 'windows') });
  const both = stored({ ...windowsSave, ...savedCliPath(windowsSave, P, 'posix', 'windows') });
  assert.deepEqual([both.executablePath, both.executablePathBySide], [W, { windows: W, posix: P, mirrored: W }]);

  // An older WSL extension does not know the per-side member: it reads and sends the legacy value — W, the Windows CLI.
  const olderWslReader = stored({ executablePath: both.executablePath });
  assert.equal(olderWslReader.executablePath, W, 'the limitation: one shared legacy value, the last side that saved mirrored it');
  // The rule a POSIX coai-mcp >= 0.46.0 applies to what it was sent (`ExecutablePaths.Here`, the same vectors): skipped.
  assert.equal(pathForThisSide({ executablePath: W }, sideOf('posix')).path, '', 'skipped as the other side\'s — a PATH lookup, never W');
  // A newer reader on each side runs its own value from the same row.
  assert.equal(wired(both, sideOf('posix')), P);
  assert.equal(wired(both, sideOf('windows')), W);
});

test('mixed versions: the older WSL extension then saves P2 — its reader drops the per-side member — and a newer WSL reader resolves P2; the Windows value is lost to that save', () => {
  const both = stored({ executablePath: W, executablePathBySide: { windows: W, posix: P, mirrored: W } });
  const after = olderSave(both, P2);

  assert.equal('executablePathBySide' in after, false, 'the older save carried no per-side member');
  assert.equal(wired(after, sideOf('posix')), P2, 'the newer WSL reader resolves the older extension\'s save');
  assert.equal(wired(after, sideOf('windows')), '', 'the newer Windows reader skips the WSL path — W went with the member the older save dropped (update that side)');
});

test('mixed versions: an older save that leaves the per-side member as it was (a hand edit, a writer that keeps unknown fields) is detected through mirrored', () => {
  const after = stored({ executablePath: P2, executablePathBySide: { windows: W, posix: P, mirrored: W } });

  assert.equal(wired(after, sideOf('posix')), P2, 'legacy ≠ mirrored: the older save wins for the family it is spelled for');
  assert.equal(wired(after, sideOf('windows')), W, 'and the Windows reader keeps its own value');
});
