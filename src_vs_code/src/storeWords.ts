import { SNAPSHOT_FILE } from './foreignLedgers';
import type { RoundStore, StoreNotIncluded } from './roundCounts';

/**
 * How a person names a store (E6): *this machine*, *WSL: Ubuntu*, *Windows*, *Linux*, *macOS*, and how a block says when a store
 * was last written and why one is not counted. A leaf with pure functions — the Rounds block, the stated-reset line and the money
 * notes all say a store, and say it one way.
 *
 * <p>A store is a LOCATION, not an account: coai cannot tell whether two stores sign in to one vendor account, so the words
 * name where the numbers came from and never whose they are.</p>
 */

type Named = Pick<RoundStore, 'live' | 'host' | 'wslDistro' | 'platform' | 'directory'>;

const PLATFORM_WORDS: Readonly<Record<string, string>> = { windows: 'Windows', linux: 'Linux', macos: 'macOS' };

/** One store in words: this side is *this machine*, a WSL distribution *WSL: <distro>*, any other its operating system. */
export function storeName(store: Named): string {
  return store.live ? 'this machine' : otherName(store);
}

function otherName(store: Named): string {
  return store.host === 'wsl' ? wslName(store.wslDistro) : (PLATFORM_WORDS[store.platform] ?? directoryName(store.directory));
}

function directoryName(directory: string): string {
  return directory.length > 0 ? directory : 'another store';
}

function wslName(distro: string): string {
  return distro.length > 0 ? `WSL: ${distro}` : 'WSL';
}

/**
 * The words for a store id among the stores a merged answer named. An id no answer named — a directory standing for its own
 * store when the merged call did not answer — is its own words: the directory.
 */
export function storeNameOf(stores: readonly RoundStore[], id: string): string {
  const named = stores.find((one) => one.id === id);

  return named === undefined ? id : storeName(named);
}

/**
 * A store as the Rounds block's sentence names it: its operating system, then in brackets what else a person needs — the WSL
 * distribution, *this machine* for the side the window is on, and whatever the caller adds (an age, *0 rounds as of …*).
 * `Windows (this machine)`, `WSL (Ubuntu, as of 3 min ago)`, `Linux`.
 */
export function storeLabel(store: Named, extra = ''): string {
  const distro = store.host === 'wsl' ? store.wslDistro : '';
  const details = [distro, store.live ? 'this machine' : '', extra].filter((one) => one.length > 0);
  const system = store.host === 'wsl' ? 'WSL' : (PLATFORM_WORDS[store.platform] ?? directoryName(store.directory));

  return details.length === 0 ? system : `${system} (${details.join(', ')})`;
}

const MINUTE_MS = 60_000;
const HOUR_MS = 60 * MINUTE_MS;
const DAY_MS = 24 * HOUR_MS;

/** A span as the block writes it: minutes under an hour, hours under two days, days beyond — `3 min`, `6 h`, `9 d`. */
function spanWords(ms: number): string {
  if (ms < HOUR_MS) {
    return `${Math.floor(ms / MINUTE_MS)} min`;
  }

  return ms < 2 * DAY_MS ? `${Math.floor(ms / HOUR_MS)} h` : `${Math.floor(ms / DAY_MS)} d`;
}

/**
 * When a store's counts are as of, against the clock that counted: `as of 3 min ago`, `as of just now`, and — a WSL clock ahead
 * of Windows after a sleep is a fact worth showing, so never clamped to now — `as of 40 min ahead of this clock`. An instant
 * nobody can read is `as of an unknown time`.
 */
export function asOfWords(asOf: string, nowMs: number): string {
  const at = Date.parse(asOf);
  if (!Number.isFinite(at)) {
    return 'as of an unknown time';
  }
  const behind = nowMs - at;
  if (behind < -MINUTE_MS) {
    return `as of ${spanWords(-behind)} ahead of this clock`;
  }

  return behind < MINUTE_MS ? 'as of just now' : `as of ${spanWords(behind)} ago`;
}

/** *1 round appears* / *2 rounds appear* in two stores with different contents, and which store's copy was counted. */
export function conflictWords(rounds: number, owner: string): string {
  const these = rounds === 1 ? '1 round appears' : `${rounds} rounds appear`;

  return `${these} in two stores with different contents — counted from ${owner}.`;
}

/** The cure for each reason a snapshot could not be counted; a reason this build does not know is kept as text with its detail. */
const WHY: Readonly<Record<string, (detail: string) => string>> = {
  missing: () => 'no snapshot yet — update coai-mcp on that side; the snapshot is written after the next round there',
  unreadable: (detail) => `its snapshot could not be read${bracketed(detail)}`,
  'newer-schema': (detail) => `its snapshot was written by a newer coai-mcp${bracketed(detail)}`,
  timeout: () => 'it did not answer in time',
};

function bracketed(detail: string): string {
  return detail.length > 0 ? ` (${detail})` : '';
}

function whyNot(item: StoreNotIncluded): string {
  const known = WHY[item.reason];

  return known === undefined ? `${item.reason.length > 0 ? item.reason : 'not counted'}${bracketed(item.detail)}` : known(item.detail);
}

/** The directory a snapshot path stands for — the path without the snapshot file's name. */
export function snapshotDirectory(path: string): string {
  const cut = path.length - SNAPSHOT_FILE.length - 1;

  return path.endsWith(SNAPSHOT_FILE) && /[\\/]/u.test(path.charAt(cut)) ? path.slice(0, cut) : path;
}

/** A store the merged call could not count, in the sentence the block always had for a store it names but does not count — with the reason. */
export function notIncludedWords(item: StoreNotIncluded): string {
  return `${snapshotDirectory(item.path)} holds another installation's store; its rounds are not included: ${whyNot(item)}.`;
}
