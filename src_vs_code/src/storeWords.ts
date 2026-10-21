import type { RoundStore } from './roundCounts';

/**
 * How a person names a store (E6): *this machine*, *WSL: Ubuntu*, *Windows*, *Linux*, *macOS*. A leaf with no imports of its
 * own beyond a type — the Rounds block, the stated-reset line and the money notes all say a store, and say it one way.
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
