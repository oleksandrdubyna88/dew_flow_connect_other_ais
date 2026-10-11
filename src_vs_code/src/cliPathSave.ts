import { familyLabel, spelledForTheOtherOs, storedForThisSide, type ExecutableBySide, type PathFamily, type StoredExecutable } from './pathFamily';

/**
 * What a SAVE of a CLI-path field writes on a catalog row — the one write rule of todo/PLAN_paths_per_side.md design (c),
 * the same in every mode (amended by the cadence consultation for epics 4-6, finding a).
 *
 * <p><b>Why one rule.</b> The first design wrote only the legacy `executablePath` while one field was shown, and the
 * per-side value while two were: after `always → never`, a **Forget**, or the other side's 90 days running out, a save of
 * B still resolved to the older per-side A. Here every save writes `executablePathBySide[<the field's family>]` — in
 * one-field mode the one field IS this side's — so a side always resolves what was last saved for it.</p>
 *
 * <ul>
 *   <li>A save of THIS side's field also mirrors the value into the legacy `executablePath`, which every older extension
 *       reads, so an older reader on this side is never left with a stale value.</li>
 *   <li>A save of the OTHER side's field leaves the legacy value alone: it is this side's, for this side's older
 *       reader.</li>
 *   <li>`mirrored` records the legacy value as this save left it, so a later save by an extension that does not know the
 *       per-side field is DETECTED (legacy ≠ `mirrored`, `olderSaveFor`).</li>
 *   <li>Every OTHER family keeps resolving exactly what it did: its stored value is pinned into its member when the save
 *       would otherwise move it — when it has a member already (an older extension's save may be winning for it, and
 *       `mirrored` is about to catch up), or when it reads the legacy value and this save REPLACES that value. Pinned
 *       whatever its spelling: the reader judges a member by the same disk-aware rule as the legacy value
 *       (`pathForThisSide`), so the pinned value resolves exactly as the legacy one did — `/tools/codex.exe` stays
 *       `C:\tools\codex.exe` on a Windows side where that file is, and stays skipped where it is not. Deciding by the
 *       spelling instead broke a working CLI (the risk consultant, 2026-10-11, finding c): the save cannot ask the other
 *       side's disk, and does not have to.</li>
 *   <li>Clearing writes `""` — present, the PATH lookup — never deletes the member: a deleted member would resurrect the
 *       legacy value, and a `//server/share/codex` is spelled for neither OS, so it would run on both sides.</li>
 * </ul>
 *
 * <p>Pure and `vscode`-free; the panel's one vendor write calls {@link vendorFieldWrite}.</p>
 */

/** What a save of one CLI-path field writes on the row. */
export interface SavedCliPath {
  readonly executablePath: string;
  readonly executablePathBySide: ExecutableBySide;
}

/** The two families a row's per-side field holds. */
const FAMILIES: readonly PathFamily[] = ['windows', 'posix'];

/**
 * The row's CLI-path fields after a save of `value` into the field of `field`, by a window on `here`.
 *
 * @param stored the row as it is stored now
 * @param field the family of the field that was edited — `here` for the one field, the other for E4's second field
 * @param here this window's family
 */
export function savedCliPath(stored: StoredExecutable, value: string, field: PathFamily, here: PathFamily): SavedCliPath {
  const text = value.trim();
  const executablePath = field === here ? text : stored.executablePath.trim();
  const replacesLegacy = executablePath !== stored.executablePath.trim();
  const pinned = FAMILIES.filter((family) => family !== field).map((family) => pinnedFor(stored, family, replacesLegacy));

  return { executablePath, executablePathBySide: Object.assign({}, stored.executablePathBySide, ...pinned, { [field]: text, mirrored: executablePath }) };
}

/**
 * The member a family keeps through a save of another family's field: its current stored value, written out when it has
 * a member already (an older extension's save may be winning for it — folded in before `mirrored` catches up) or when
 * the save replaces the legacy value it reads. No spelling and no disk is asked: the member is resolved by the very rule
 * the legacy value was, so pinning the stored value preserves the resolution on any disk. A family with no member keeps
 * reading an unchanged legacy value and needs none.
 */
function pinnedFor(stored: StoredExecutable, family: PathFamily, replacesLegacy: boolean): ExecutableBySide {
  const keeps = stored.executablePathBySide?.[family] !== undefined || replacesLegacy;

  return keeps ? { [family]: storedForThisSide(stored, family) } : {};
}

/**
 * The fields the panel's one vendor write (`data-setting` + `data-vendor`) puts on a row: a card's CLI-path box is saved
 * by {@link savedCliPath} with the family of the box — `executablePath` is THIS side's, `executablePath.<family>` the
 * OTHER side's box E4 draws beside it (E4's plan round, finding 0: never a second write path) — and every other key as it
 * was posted.
 */
export function vendorFieldWrite(stored: StoredExecutable, key: string, value: unknown, here: PathFamily): Readonly<Record<string, unknown>> {
  const field = cliBoxFamily(key, here);

  return field !== undefined && typeof value === 'string' ? { ...savedCliPath(stored, value, field, here) } : { [key]: value };
}

/** The key the OTHER side's CLI-path box posts — its family named, so the one vendor write saves that side's member. */
export function otherSideCliKey(family: PathFamily): string {
  return `executablePath.${family}`;
}

/** The family whose CLI-path box a posted key names — `executablePath` is this side's — or undefined for any other key. */
export function cliBoxFamily(key: string, here: PathFamily): PathFamily | undefined {
  return key === 'executablePath' ? here : FAMILIES.find((family) => key === otherSideCliKey(family));
}

/**
 * Why a value typed into the OTHER side's CLI-path box is not saved — '' when it may be (design (d): editable as text,
 * refused when spelled for the wrong OS). A path spelled for THIS side's OS belongs in this side's box — a drive letter or
 * a `\` path typed into the WSL / Linux box on Windows, a `/…` path typed into the Windows box in WSL. A bare name, a
 * relative path, a `//server/share` and an empty box (the PATH lookup) are kept: none of them is this side's spelling.
 * Never `stat`ed: the other side's disk is not this window's to ask.
 */
export function cliPathRefusal(key: string, value: unknown, here: PathFamily): string {
  const field = cliBoxFamily(key, here);

  return field === undefined || field === here || typeof value !== 'string' ? '' : spelledHere(value, field, here);
}

/** The refusal of a value typed into `field`'s box when it is spelled for `here`'s OS — '' when it is not. */
function spelledHere(value: string, field: PathFamily, here: PathFamily): string {
  return spelledForTheOtherOs(value, field === 'windows')
    ? `'${value.trim()}' is a ${familyLabel(here)} path — this side's; type it in the box above, or a ${familyLabel(field)} path here`
    : '';
}
