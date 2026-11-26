/**
 * The permission to read hosts over ssh (E2.S1, § P3 to § P10 of its spec): ONE pure module for the stamp rule, the grant record, the
 * withdrawal file's name and parser, the door's answers and the one decision of the question. Nothing here reads a disk or a window.
 *
 * <p>The permission is in force only when a well-made grant has a stamp ABOVE the stamp of every withdrawal; anything that cannot be
 * read on the withdrawal's side counts against the grant. `inForce` is the one comparison, and `grantOf` and `withdrawalOf` are the
 * one reader of the grant and of a withdrawal file: no other module reads either.</p>
 */

/** The highest stamp. A stamp is a whole number from 1 to this. */
export const STAMP_MAX = 1_000_000_000_000;

/** The most bytes of one withdrawal file that are read: a larger file is unreadable. This build writes under 100. */
export const WITHDRAWN_BYTES_MAX = 256;

/** The listing reads at most this many withdrawal files; the names of the others count as one unreadable withdrawal. */
export const WITHDRAWN_READ_MAX = 16;

/** The withdrawal files a withdrawal leaves in the folder: past it the lowest readable stamp goes first. */
export const WITHDRAWN_KEEP_MAX = 8;

/** The most names one *Allow* removes in one press. */
export const ALLOW_REMOVALS_MAX = 1024;

/** The permission of one side: when the person gave it, and its stamp. */
export interface Grant {
  readonly utc: string;
  readonly stamp: number;
}

const isWhole = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value);

/** The value as a stamp, or nothing: a safe whole number from 1 to `STAMP_MAX`. */
export function stampOf(value: unknown): number | undefined {
  return isWhole(value) && value >= 1 && value <= STAMP_MAX ? value : undefined;
}

const recordOf = (value: unknown): Readonly<Record<string, unknown>> | undefined =>
  (typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Readonly<Record<string, unknown>>) : undefined);

/** The ONE reader of the grant. A malformed `grant` is no grant, and the file is not damaged by it. */
export function grantOf(file: { readonly grant?: unknown }): Grant | undefined {
  const one = recordOf(file.grant) ?? {};
  const stamp = stampOf(one['stamp']);
  const utc = one['utc'];

  return typeof utc === 'string' && stamp !== undefined ? { utc, stamp } : undefined;
}

// ─────────────────────────────── The withdrawal file ───────────────────────────────

/** A withdrawal file of the folder. `unreadable` is a name with the prefix that is not a whole withdrawal: it counts as the highest stamp. */
export type Withdrawal =
  | { readonly name: string; readonly kind: 'stamp'; readonly stamp: number; readonly utc: string }
  | { readonly name: string; readonly kind: 'unreadable' };

/** `prefix` is handed in (`DiskNames.withdrawnPrefix`): this module never spells it. */
export const withdrawalName = (prefix: string, stamp: number): string => `${prefix}${stamp}.json`;

/** The text of a withdrawal file: under 100 bytes. */
export const withdrawalText = (stamp: number, utc: string): string => JSON.stringify({ v: 1, stamp, utc });

const DIGITS = /^[1-9][0-9]{0,12}$/;

/** The stamp a name spells, or nothing when the name is not `<prefix><stamp>.json` of the grammar. */
export function nameStamp(prefix: string, name: string): number | undefined {
  if (!name.startsWith(prefix) || !name.endsWith('.json')) {
    return undefined;
  }
  const digits = name.slice(prefix.length, name.length - '.json'.length);

  return DIGITS.test(digits) ? stampOf(Number(digits)) : undefined;
}

const wholeText = (one: Readonly<Record<string, unknown>>, stamp: number): boolean =>
  one['v'] === 1 && one['stamp'] === stamp && typeof one['utc'] === 'string';

function textRecord(text: string | undefined): Readonly<Record<string, unknown>> | undefined {
  if (text === undefined || Buffer.byteLength(text, 'utf8') > WITHDRAWN_BYTES_MAX) {
    return undefined;
  }
  try {
    return recordOf(JSON.parse(text));
  } catch {
    return undefined;
  }
}

/** The text of a withdrawal file, when its name fits the grammar and its text is whole. */
function wholeOf(prefix: string, name: string, text: string | undefined): { readonly stamp: number; readonly utc: string } | undefined {
  const stamp = nameStamp(prefix, name);

  return stamp === undefined ? undefined : wholeAt(stamp, textRecord(text));
}

const wholeAt = (stamp: number, one: Readonly<Record<string, unknown>> | undefined): { readonly stamp: number; readonly utc: string } | undefined =>
  (one !== undefined && wholeText(one, stamp) ? { stamp, utc: one['utc'] as string } : undefined);

/** The ONE parser of a withdrawal file. `text` is `undefined` when the file could not be read. */
export function withdrawalOf(prefix: string, name: string, text: string | undefined): Withdrawal {
  const whole = wholeOf(prefix, name, text);

  return whole === undefined ? { name, kind: 'unreadable' } : { name, kind: 'stamp', ...whole };
}

/** The readable stamps of a listing. */
export const stampsOf = (files: readonly Withdrawal[]): readonly number[] => files.flatMap((one) => (one.kind === 'stamp' ? [one.stamp] : []));

const highest = (numbers: readonly number[]): number => numbers.reduce((top, one) => Math.max(top, one), 0);

// ─────────────────────────────── The stamp rule ───────────────────────────────

export interface Against {
  readonly files: readonly Withdrawal[];
  /** More withdrawal names are in the folder than were read. */
  readonly crowded: boolean;
  /** The copy in the state row: a stamp, nothing, or a value that is not a stamp. */
  readonly copy: number | 'unreadable' | undefined;
  /** This window holds a withdrawal that is not saved. */
  readonly held: boolean;
}

const filesBelow = (files: readonly Withdrawal[], stamp: number): boolean => files.every((one) => one.kind === 'stamp' && one.stamp < stamp);

const copyBelow = (copy: Against['copy'], stamp: number): boolean => copy === undefined || (copy !== 'unreadable' && copy < stamp);

const quiet = (against: Against): boolean => !against.crowded && !against.held;

/** The permission is in force: a well-made grant whose stamp is ABOVE every withdrawal. The ONE comparison; every doubt is "not in force". */
export function inForce(grant: Grant | undefined, against: Against): boolean {
  return grant !== undefined && quiet(against) && filesBelow(against.files, grant.stamp) && copyBelow(against.copy, grant.stamp);
}

/** What a grant is taken above: the readable withdrawals, the copy when it is a stamp, and the file's own grant. */
export function floorOf(files: readonly Withdrawal[], copy: Against['copy'], grant: Grant | undefined): number {
  return highest([...stampsOf(files), typeof copy === 'number' ? copy : 0, grant?.stamp ?? 0]);
}

/** The stamp of a withdrawal: one above the highest it sees, and never above `STAMP_MAX`. */
export function withdrawalStamp(seen: readonly number[]): number {
  return Math.min(STAMP_MAX, highest(seen) + 1);
}

// ─────────────────────────────── The door ───────────────────────────────

export type Asker = 'round' | 'session';

/** The one door's answers. `stopped` is given to the round only. A host is contacted on `yes` and on nothing else. */
export type DoorAnswer = 'yes' | 'no' | 'no-permission' | 'waiting' | 'damaged' | 'stopped';
export type SessionAnswer = Exclude<DoorAnswer, 'stopped'>;

/** What the side shows of the permission, for the tab and the hosts list. */
export type Permission = { readonly given: string } | 'none' | 'waiting';

/** What an *Allow* came to, as the store tells it. */
export type Allowed =
  | { readonly kind: 'given'; readonly grant: Grant; readonly floor: number; readonly restarted: boolean }
  | { readonly kind: 'kept'; readonly grant: Grant }
  /** A withdrawal appeared after the *Allow* began, or stands above the grant at the read-back, or the read-back could not be made. `unsaved`: step 10 could not make its file. */
  | { readonly kind: 'withdrawn'; readonly unsaved?: true }
  | { readonly kind: 'damaged' }
  | { readonly kind: 'unwritten'; readonly why: 'busy' | 'disk' | 'lost' };

/** What a *Withdraw* came to, as the store tells it. */
export type Withdrawn =
  | { readonly kind: 'withdrawn'; readonly stamp: number }
  /** `busy`: the file is made, and a claim was still held at the last read-back. `disk`: no file could be made. `lost`: an *Allow* kept writing above it. */
  | { readonly kind: 'unwritten'; readonly why: 'busy' | 'disk' | 'lost' };

/** What the keeper answers to a person's *Withdraw*. */
export type Withdrew = 'withdrawn' | { readonly kind: 'unwritten'; readonly why: 'busy' | 'disk' | 'lost' };

// ─────────────────────────────── The question ───────────────────────────────

export type PermissionAsked = 'given' | 'not-now' | { readonly kind: 'not-kept'; readonly said: string };

export interface AskSeams {
  /** The modal of W14. `true`: *Allow reading*. `false`: *Not now*, or Escape. */
  readonly raise: () => PromiseLike<boolean>;
  readonly permission: () => PromiseLike<Permission>;
  /** The keeper's `allow()`. */
  readonly allow: () => PromiseLike<Allowed>;
  /** The row's `askedUtc` as it reads now, and its one write. */
  readonly askedBefore: () => boolean;
  readonly noteAsked: () => PromiseLike<void>;
  /** W16 for a result that is not `given` or `kept`; for `unwritten` it also raises the notice with *Save now*. */
  readonly notKept: (result: Allowed) => string;
}

export interface PermissionAsker {
  /** Any press that asks: the tab's *Allow reading…*, the hosts list's first row, a chat door. */
  readonly ask: () => Promise<PermissionAsked>;
  /** The icon's press: asks only the FIRST time. */
  readonly askFirst: () => Promise<PermissionAsked | 'asked-before'>;
}

const inForceNow = async (seams: AskSeams): Promise<boolean> => {
  const now = await seams.permission();

  return typeof now === 'object';
};

/** The modal, and what the answer does. Nothing is written for *Not now*. */
async function modal(seams: AskSeams): Promise<PermissionAsked> {
  if (!(await seams.raise())) {
    return 'not-now';
  }
  const result = await seams.allow();

  return result.kind === 'given' || result.kind === 'kept' ? 'given' : { kind: 'not-kept', said: seams.notKept(result) };
}

/** The ONE decision of the question: who is asked and when. One modal at a time in a window: a second `ask` while one is open gets the first one's answer. */
export function permissionAsker(seams: AskSeams): PermissionAsker {
  let open: Promise<PermissionAsked> | undefined;
  const once = (work: () => Promise<PermissionAsked>): Promise<PermissionAsked> => {
    open ??= work().finally(() => { open = undefined; });

    return open;
  };
  const ask = async (): Promise<PermissionAsked> => ((await inForceNow(seams)) ? 'given' : once(() => modal(seams)));
  const askFirst = async (): Promise<PermissionAsked | 'asked-before'> => {
    if (await inForceNow(seams)) {
      return 'given';
    }
    if (seams.askedBefore()) {
      return 'asked-before';
    }

    return once(async () => { await seams.noteAsked(); return modal(seams); });
  };

  return { ask, askFirst };
}
