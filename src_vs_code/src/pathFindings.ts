import { placeLabel } from './catalogPlaces';
import { type FilePresence, type PathFamily, isWindowsAbsolute, spelledForTheOtherOs } from './pathFamily';
import { PATH_SETTINGS, type PathKind, type PathSetting } from './pathSettings';

/**
 * A path of THIS side that is not where the settings say it is — the one model behind the notice at every start and the
 * bright mark on the Settings page (todo/PLAN_paths_per_side.md, design (e); E5.1).
 *
 * <p><b>Why one model.</b> The operator's case (2026-10-09): a Windows question-consultant root `D:\old-project` that was
 * deleted was a generic "the server could not understand a setting" toast on every start, and nothing on the page beside
 * the folder. The disk is asked ONCE about each stored value of this side — a folder, a CLI, a watched data folder, the
 * named data directory — and what it said is a finding: `missing` when the disk SAID there is nothing there (ENOENT,
 * ENOTDIR, a file where a folder should be), `unknown` when it could not tell (EACCES, EBUSY, EIO, a probe that ran past
 * its deadline). An unknown path is never counted as missing and never raises the notice (the gate's cadence consultant);
 * it is marked in the warning tone as "could not be checked".</p>
 *
 * <p><b>What is deliberately NOT a finding here.</b> A value spelled for the OTHER operating system is the other side's —
 * decided once, by E1 (`otherSideHere` and `RootExistence` for the roots, `otherSideClis` for a CLI path,
 * `WatchedDir.otherSide`, `ChosenStorage.skippedShared`) and drawn as its quiet note. The plan's model named a third state
 * `otherSide`; producing it here would be a second, lexical copy of a decision that asks the disk (`reuse-first.md`, *a
 * decision applied at SOME of its sites*), so this module skips those values and leaves them to the one rule that owns
 * them. A value that is not an absolute path of this side (a bare CLI name looked up on PATH, a relative root the server
 * refuses by name) is not asked either.</p>
 *
 * <p>Pure and `vscode`-free: the disk is handed in as what it last SAID (`Presence`), the host asks it
 * (`pathProbes.ts`), the page and the notices read the findings, and `shared`-free tests decide every row.</p>
 */

/** What the disk said about a path of this side, when it said anything but "there". */
export type FindingState = 'missing' | 'unknown';

/** One missing or unanswerable path of this side. */
export interface PathFinding {
  /** The registry id — `coai.qconsultRoots`, `coai.vendors[].executablePath`, … (`pathSettings.ts`). */
  readonly key: string;
  /** The place of the Settings page that draws the value (`catalogPlaces.ts`). */
  readonly place: string;
  /** The stored value, trimmed, as the page holds it — the half of the notice's subject. */
  readonly value: string;
  readonly family: PathFamily;
  readonly state: FindingState;
  /** Whose value it is when a setting holds many — a model's id; empty otherwise. */
  readonly owner: string;
}

/** One stored value of one path setting, and the path this side asks the disk about for it. */
export interface PathValue {
  readonly setting: PathSetting;
  /** The stored value, trimmed. */
  readonly value: string;
  /** What to ask the disk about — the value, or its qualified form (`pathForThisSide` for a CLI). */
  readonly path: string;
  readonly owner?: string;
}

/** What the disk has said about a path so far: its three answers, or none yet. */
export type Presence = FilePresence | 'pending';

/** What the host knows about a path right now, without waiting — a probe it has not answered is `pending`. */
export type PresenceOf = (path: string, kind: ProbeKind) => Presence;

/** What a probe asks: a folder, or a file — a list of folders asks about each folder. */
export type ProbeKind = 'folder' | 'file';

/** The probe a setting's values take. */
export function probeKindOf(kind: PathKind): ProbeKind {
  return kind === 'file' ? 'file' : 'folder';
}

/**
 * Whether a stored value is a path of THIS side the disk should be asked about: an absolute path spelled for this
 * family — never one spelled for the other side (E1's), never a bare name (a PATH lookup) or a relative path (refused by
 * name where it is read).
 */
export function probeable(value: string, family: PathFamily): boolean {
  const text = value.trim();
  if (text.length === 0 || spelledForTheOtherOs(text, family === 'windows')) {
    return false;
  }

  return family === 'windows' ? isWindowsAbsolute(text) : text.startsWith('/');
}

/** The finding one value and what the disk said amount to — nothing for a path that is there or not yet answered. */
export function findingOf(one: PathValue, family: PathFamily, presence: Presence): PathFinding | undefined {
  const state = STATE_OF[presence];

  return state === undefined
    ? undefined
    : { key: one.setting.id, place: one.setting.place, value: one.value, family, state, owner: one.owner ?? '' };
}

/** The disk's answer as a finding's state — a path that is there, or not asked yet, is no finding. */
const STATE_OF: Readonly<Record<Presence, FindingState | undefined>> = {
  here: undefined,
  pending: undefined,
  absent: 'missing',
  unknown: 'unknown',
};

/** Every finding over the stored values of this side, from what the disk has said so far. */
export function pathFindings(values: readonly PathValue[], family: PathFamily, presenceOf: PresenceOf): readonly PathFinding[] {
  return values
    .filter((one) => probeable(one.value, family))
    .flatMap((one) => {
      const finding = findingOf(one, family, presenceOf(one.path, probeKindOf(one.setting.kind)));

      return finding === undefined ? [] : [finding];
    });
}

/** The finding about one stored value of one setting, or nothing. */
export function findingFor(findings: readonly PathFinding[], key: string, value: string): PathFinding | undefined {
  return findings.find((one) => one.key === key && one.value === value.trim());
}

/** How many paths are MISSING under one place of the Settings page — what its tab counts; an unknown path counts for nothing. */
export function missingAt(findings: readonly PathFinding[], place: string): number {
  return findings.filter((one) => one.state === 'missing' && (one.place === place || one.place.startsWith(`${place}/`))).length;
}

/** The notice's subject: one counter per path, so fixing one clears one (`notifyOnce` keys on `(code, subject)`). */
export function findingSubject(finding: Pick<PathFinding, 'key' | 'value'>): string {
  return `${finding.key}|${finding.value}`;
}

/** How a family is named to a person (decided with the operator, D1). */
export function sideName(family: PathFamily): string {
  return family === 'windows' ? 'Windows' : 'WSL / Linux';
}

/** The words beside a marked value: what the disk said, never "deleted" — an unmounted drive reports absence too. */
export function markSentence(finding: PathFinding): string {
  return finding.state === 'missing'
    ? `missing — not found on this machine (${sideName(finding.family)}) just now`
    : `could not be checked on this machine (${sideName(finding.family)}) — the disk did not answer`;
}

/** The sentence of the notice for a missing path — one per setting, its words from the registry. */
export function missingTitle(finding: PathFinding): string {
  const setting = settingOf(finding.key);
  const whose = finding.owner.length > 0 ? ` of the model ${finding.owner}` : '';
  const where = `was not found on this machine (${sideName(finding.family)}) just now`;

  return `${OWNERS[finding.key] ?? `The ${setting.noun}`}${whose} ${finding.value} ${where} — ${CONSEQUENCES[finding.key] ?? 'it is skipped here.'}`;
}

/** What to do about a missing path, per setting — the notice's cure. */
export function missingCure(finding: PathFinding): string {
  return CURES[finding.key] ?? `Create it, or change it in Settings › ${placeLabel(finding.place)}.`;
}

/** Who a value belongs to, as the title opens. */
const OWNERS: Readonly<Record<string, string>> = {
  'coai.qconsultRoots': "The question consultant's folder",
  'coai.vendors[].executablePath': 'The CLI path',
  'coai.alsoWatchDataDirectories': "Another installation's data folder",
  'coai.dataDirectory': "This window's data folder",
};

/** What this side does without the value — the title's second half. */
const CONSEQUENCES: Readonly<Record<string, string>> = {
  'coai.qconsultRoots': 'a disk row skips it.',
  'coai.vendors[].executablePath': 'that model cannot run here until it is.',
  'coai.alsoWatchDataDirectories': 'its questions are not answered here until it is.',
  'coai.dataDirectory': 'nothing is written there until it exists.',
};

const CURES: Readonly<Record<string, string>> = {
  'coai.qconsultRoots': 'Create the folder, or remove it from the list in Settings › Consultants › Question consultant.',
  'coai.vendors[].executablePath': 'Install the CLI there, correct the path on its Models card, or clear it to look the CLI up on PATH.',
  'coai.alsoWatchDataDirectories': 'Connect the machine or share holding it, or remove it from the list in Settings › Setup › MCP server.',
  'coai.dataDirectory': 'Connect the drive, create the folder, or choose another in Settings › Setup › MCP server.',
};

/** The registry entry a finding is about — every finding is made from one, so a miss is a programming error. */
export function settingOf(key: string): PathSetting {
  const found = PATH_SETTINGS.find((one) => one.id === key);
  if (found === undefined) {
    throw new Error(`${key} is no path setting of the registry (pathSettings.ts)`);
  }

  return found;
}
