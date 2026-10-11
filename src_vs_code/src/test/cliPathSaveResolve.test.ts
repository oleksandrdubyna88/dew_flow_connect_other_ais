import assert from 'node:assert/strict';
import { test } from 'node:test';

import { savedCliPath, vendorFieldWrite } from '../cliPathSave';
import { pathForThisSide, storedForThisSide, type ExecutableBySide, type ExecutableSide, type FilePresence, type PathFamily } from '../pathFamily';
import { DEFAULT_VENDORS, vendorsFrom, type Vendor } from '../vendors';

/**
 * Save → resolve (todo/PLAN_paths_per_side.md E3.2, design (c) as amended by the cadence consultation for epics 4-6).
 *
 * <p>ONE write rule in every mode: every save of a CLI-path field writes `executablePathBySide[<its family>]`; a save of
 * THIS side's field also mirrors the value into the legacy `executablePath` (what every older extension reads) and
 * records it as `mirrored`, so a later save by an extension that does not know the per-side field is DETECTED (legacy ≠
 * mirrored) and wins for the family it is spelled for. What a side resolves is what was last saved for it, whatever the
 * mode (one field or two) was at either moment.</p>
 *
 * <p>Every row goes through the real reader and a JSON round trip, as `coai.vendors` does between a save and a read.</p>
 */

const W = 'C:\\tools\\codex.cmd';
const W2 = 'D:\\other\\codex.exe';
const P = '/opt/codex/bin/codex';
const P2 = '/usr/local/bin/codex';
const SHARE = '//server/share/codex';

/** A stored row read back the way the extension reads `coai.vendors`. */
function row(executablePath: string, executablePathBySide?: ExecutableBySide): Vendor {
  const stored = { ...DEFAULT_VENDORS[0]!, id: 'codex', runtime: 'codex', enabled: true, executablePath, ...(executablePathBySide === undefined ? {} : { executablePathBySide }) };

  return vendorsFrom(JSON.parse(JSON.stringify([stored])) as unknown)[0]!;
}

/** A save of one field, by a window on `here`, then the row read back. */
function saved(before: Vendor, value: string, field: PathFamily, here: PathFamily): Vendor {
  return vendorsFrom(JSON.parse(JSON.stringify([{ ...before, ...savedCliPath(before, value, field, here) }])) as unknown)[0]!;
}

/** What a side of this family runs for the row — the disk answering `disk` (by default "absent" to every other-OS spelling). */
function resolved(stored: Vendor, family: PathFamily, disk: (path: string) => FilePresence = () => 'absent'): string {
  const side: ExecutableSide = { family, systemDrive: 'C:', fileAt: disk };

  return pathForThisSide(stored, side).path;
}

test('a save of THIS side\'s field mirrors into the legacy value an older extension reads, and records what it mirrored', () => {
  const after = saved(row(W, { windows: W, mirrored: W }), W2, 'windows', 'windows');

  assert.equal(resolved(after, 'windows'), W2, 'the newer reader');
  assert.equal(after.executablePath, W2, 'an older reader on this side would be left with the stale value');
  assert.equal(after.executablePathBySide?.mirrored, W2);
});

test('a save of the OTHER side\'s field never touches the legacy value', () => {
  const after = saved(row(W, { windows: W, mirrored: W }), P, 'posix', 'windows');

  assert.equal(after.executablePath, W, 'the legacy value is THIS side\'s, for this side\'s older reader');
  assert.equal(resolved(after, 'posix'), P);
  assert.equal(resolved(after, 'windows'), W);
});

test('two fields: save A → one field → save B resolves B (never the older per-side A)', () => {
  // What a two-field save of A left (the shape the write rule writes), then a one-field save of B.
  const twoFields = row(W, { windows: W, posix: '', mirrored: W });
  const oneField = saved(twoFields, W2, 'windows', 'windows');

  assert.equal(resolved(oneField, 'windows'), W2);
  assert.equal(storedForThisSide(oneField, 'windows'), W2, 'what the field shows');
});

test('one field: save B → two fields → this side\'s field shows and resolves B, and the other side\'s is untouched', () => {
  const after = saved(row(SHARE), W2, 'windows', 'windows');

  assert.equal(storedForThisSide(after, 'windows'), W2);
  assert.equal(resolved(after, 'windows'), W2);
  assert.equal(storedForThisSide(after, 'posix'), SHARE, 'the WSL side ran the share before the save, and runs it still');
  assert.equal(resolved(after, 'posix'), SHARE);
});

test('the other side\'s value P → Forget (or 90 days) → one field again → save B resolves B, and P is still stored', () => {
  const twoFields = saved(saved(row(''), W, 'windows', 'windows'), P, 'posix', 'windows');
  const afterForget = saved(twoFields, W2, 'windows', 'windows');

  assert.equal(resolved(afterForget, 'windows'), W2);
  assert.equal(afterForget.executablePathBySide?.posix, P, 'hiding a field never deletes the other side\'s value');
  assert.equal(resolved(afterForget, 'posix'), P);
});

test('clearing THIS side\'s field is the PATH lookup here — never the legacy share — and the other side keeps what it ran', () => {
  const after = saved(row(SHARE), '', 'windows', 'windows');

  assert.equal(resolved(after, 'windows'), '', 'cleared: PATH');
  assert.equal(after.executablePathBySide?.windows, '', 'cleared is PRESENT and empty, never a deleted key');
  assert.equal(resolved(after, 'posix'), SHARE, 'the other side\'s value untouched');
});

test('clearing the OTHER side\'s field is the PATH lookup there, even with a legacy share stored that neither OS spells', () => {
  const after = saved(row(SHARE), '', 'posix', 'windows');

  assert.equal(after.executablePath, SHARE, 'the legacy value is untouched by the other side\'s field');
  assert.equal(resolved(after, 'posix'), '', 'the emptied field fell through to the share');
  assert.equal(resolved(after, 'windows'), SHARE);
});

test('a save by an extension that does not know the per-side field is detected, and wins for the family it is spelled for', () => {
  // A newer Windows window saved W in two-field mode with the WSL value P; an OLDER WSL extension then saved P2 into the
  // legacy field and left the per-side field as it was (a hand edit, or a writer that carries unknown fields).
  const olderSave = row(P2, { windows: W, posix: P, mirrored: W });

  assert.equal(resolved(olderSave, 'posix'), P2, 'the newer WSL reader resolves the older extension\'s save');
  assert.equal(resolved(olderSave, 'windows'), W, 'the newer Windows reader keeps its own value');
  assert.equal(storedForThisSide(olderSave, 'posix'), P2, 'and the WSL field shows it');

  const olderWindowsSave = row(W2, { windows: W, posix: P, mirrored: W });
  assert.equal(resolved(olderWindowsSave, 'windows'), W2);
  assert.equal(resolved(olderWindowsSave, 'posix'), P);

  const neither = row('codex-beta', { windows: W, posix: P, mirrored: W });
  assert.deepEqual([resolved(neither, 'windows'), resolved(neither, 'posix')], ['codex-beta', 'codex-beta'], 'spelled for neither OS: every side');
});

test('the next newer save keeps what the older save meant for the other side — it is folded, not lost', () => {
  const after = saved(row(P2, { windows: W, posix: P, mirrored: W }), W2, 'windows', 'windows');

  assert.equal(resolved(after, 'windows'), W2);
  assert.equal(resolved(after, 'posix'), P2, 'the older WSL save survives a newer Windows save');
  assert.equal(after.executablePathBySide?.mirrored, W2);
});

/** Stored states a row can be in: legacy only (four spellings), a newer save, an older save after one. */
const STATES: readonly Vendor[] = [
  row(''), row(W), row(P), row(SHARE), row('codex'),
  row(W, { windows: W, mirrored: W }), row(P, { posix: P, mirrored: P }), row(W, { windows: W, posix: P, mirrored: W }),
  row(P2, { windows: W, posix: P, mirrored: W }), row(W2, { windows: W, posix: P, mirrored: W }), row(SHARE, { windows: '', posix: P, mirrored: '' }),
  row(SHARE, { posix: '' }),
];

/** The three answers a disk can give about every other-OS spelling it is asked about (`FilePresence`). */
const DISKS: readonly FilePresence[] = ['absent', 'here', 'unknown'];

test('every save leaves every OTHER family resolving exactly what it did — the only thing a save changes is its own field', () => {
  for (const presence of DISKS) {
    const disk = (): FilePresence => presence;
    for (const before of STATES) {
      for (const [field, here, value] of [['windows', 'windows', W2], ['posix', 'windows', P2], ['posix', 'posix', P2], ['windows', 'posix', W2], ['windows', 'windows', '']] as const) {
        const after = saved(before, value, field, here);
        const other: PathFamily = field === 'windows' ? 'posix' : 'windows';
        const label = `${JSON.stringify(before)} saved ${field}=${value} from ${here}, the disk saying ${presence}`;

        assert.equal(resolved(after, field, disk), value, `${label}: the field saved`);
        assert.equal(resolved(after, other, disk), resolved(before, other, disk), `${label}: the other family moved`);
        assert.equal(after.executablePathBySide?.mirrored, after.executablePath, `${label}: mirrored records the legacy value`);
      }
    }
  }
});

/**
 * The risk consultant's case (2026-10-11, finding c): the save decided by SPELLING whether another family reads the
 * legacy value, while the reader asks the DISK. On Windows `/tools/codex.exe` is the root-relative `C:\tools\codex.exe`
 * — a legal path, run when the file is there (or when the disk cannot tell) — so a WSL save that replaced the legacy
 * value left Windows' working CLI behind.
 */
const ROOT_RELATIVE = '/tools/codex.exe';
const ON_THE_SYSTEM_DRIVE = 'C:\\tools\\codex.exe';

/** A Windows disk on which only `C:\tools\codex.exe` is a file. */
const onlyThatFile = (path: string): FilePresence => (path === ON_THE_SYSTEM_DRIVE ? 'here' : 'absent');

test('a WSL save never takes away the CLI the Windows side found on its own disk under the legacy value', () => {
  const before = row(ROOT_RELATIVE);
  const after = saved(before, '/opt/other/codex', 'posix', 'posix');

  assert.equal(resolved(before, 'windows', onlyThatFile), ON_THE_SYSTEM_DRIVE, 'the premise: Windows runs the file on its system drive');
  assert.equal(resolved(after, 'windows', onlyThatFile), ON_THE_SYSTEM_DRIVE, 'the save broke the Windows side\'s working CLI');
});

test('a WSL save keeps a legacy value the Windows disk could not answer for — kept before, kept after', () => {
  const before = row(ROOT_RELATIVE);
  const after = saved(before, '/opt/other/codex', 'posix', 'posix');

  assert.equal(resolved(after, 'windows', () => 'unknown'), resolved(before, 'windows', () => 'unknown'));
});

test('saving the UNCHANGED path on Windows changes nothing it runs — the per-side value is qualified as the legacy one was', () => {
  const after = saved(row(ROOT_RELATIVE), ROOT_RELATIVE, 'windows', 'windows');

  assert.equal(resolved(after, 'windows', onlyThatFile), ON_THE_SYSTEM_DRIVE, 'the save moved Windows from C:\\tools\\codex.exe to an unqualified path');
});

test('the vendor write turns the card\'s CLI-path box into this side\'s save, and leaves every other key as posted', () => {
  const before = row(W, { windows: W, posix: P, mirrored: W });

  assert.deepEqual(vendorFieldWrite(before, 'executablePath', W2, 'windows'), { executablePath: W2, executablePathBySide: { windows: W2, posix: P, mirrored: W2 } });
  assert.deepEqual(vendorFieldWrite(before, 'executablePath', P2, 'posix'), { executablePath: P2, executablePathBySide: { windows: W, posix: P2, mirrored: P2 } });
  assert.deepEqual(vendorFieldWrite(before, 'model', 'gpt-6', 'windows'), { model: 'gpt-6' });
});
