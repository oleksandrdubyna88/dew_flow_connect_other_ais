/**
 * The place, its key and the decoding of the two WSL lists (E3.S1, spec § W4 and § W7; contract K1). Pure — no process, no file, no `vscode`.
 *
 * <p>A place is where one reading is taken: an ssh host, or one Linux user of one WSL distro. Its key is `ssh:<host>` or `wsl:<distro>/<user>`;
 * a reading, its time and its failure belong to the key, so two users of a distro never share one, and `wsl:Ubuntu/root` is never a user's.</p>
 *
 * <p>Every name that reaches an argv of `wsl.exe` passes a class here first. The decoder reads the list calls in UTF-16LE (as `wsl.exe` prints it)
 * or in UTF-8 (when the person's own environment asks for it) and refuses what it does not understand: a list that does not decode is not read at
 * all, because a half-decoded name could name another distro and a `-d` call starts a stopped distro (J2).</p>
 */

export type Place =
  | { readonly kind: 'ssh'; readonly host: string }
  | { readonly kind: 'wsl'; readonly distro: string; readonly user: string; readonly uid: number };

export type WslPlace = Extract<Place, { readonly kind: 'wsl' }>;

/** A distro's name as `wsl.exe --list` prints it, by its class. */
export const DISTRO_CLASS = /^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$/u;
/** A Linux user's name, by the class the users script checks. */
export const USER_CLASS = /^[a-z_][a-z0-9_-]{0,31}$/u;

/** The caps of a round (§ W12): distros of the list, users of a distro (the script's own), WSL places in all. */
export const MAX_DISTROS = 16;
export const MAX_USERS = 16;
export const MAX_PLACES = 32;

/** `ssh:<host>` or `wsl:<distro>/<user>`. */
export const placeKey = (place: Place): string => (place.kind === 'ssh' ? `ssh:${place.host}` : `wsl:${place.distro}/${place.user}`);

/** What the person reads: the host, or `<distro> · <user>`. */
export const placeLabel = (place: Place): string => (place.kind === 'ssh' ? place.host : `${place.distro} · ${place.user}`);

/**
 * The key the round's memory keeps a place under. An ssh host keeps its bare name, as E2 held it (a host's class has no `:`, so it can never equal
 * a `wsl:` key); a WSL place keeps its `placeKey`.
 */
export const memoryKeyOf = (place: Place): string => (place.kind === 'ssh' ? place.host : placeKey(place));

/** Whether a memory key is a WSL place's. */
export const isWslKey = (key: string): boolean => key.startsWith('wsl:');

/** The distro and the user of a `wsl:<distro>/<user>` key; nothing for any other key. A distro's class has no `/`, so the first one splits. */
export function splitWslKey(key: string): { readonly distro: string; readonly user: string } | undefined {
  const at = key.indexOf('/');

  return isWslKey(key) && at > 4 ? { distro: key.slice(4, at), user: key.slice(at + 1) } : undefined;
}

/** The distro's class and nothing else. */
export const distroOk = (distro: string): boolean => DISTRO_CLASS.test(distro);

/** `root` exactly when the UID is 0; any other user from 1000 to 59 999. The ONE rule, shared with the reader of the users reply. */
export const uidFits = (user: string, uid: number): boolean => (user === 'root' ? uid === 0 : uid >= 1000 && uid <= 59_999);

/**
 * The three rules of a WSL place: the distro fits its class, the user fits its class, and the user is `root` exactly when the UID is 0; a UID that is
 * not 0 is from 1000 to 59 999.
 */
export const wslPlaceOk = (place: WslPlace): boolean =>
  distroOk(place.distro) && USER_CLASS.test(place.user) && Number.isInteger(place.uid) && uidFits(place.user, place.uid);

/** The result of reading a list call's text. */
export type WslNames = { readonly names: readonly string[]; readonly ignored: number } | 'undecodable';

const BOM = '\uFEFF';
const NUL = '\u0000';

/** Every character at an even place is not U+0000 and every one at an odd place is: UTF-16LE of ASCII, read as UTF-8. Built, not written, so no control character stands in a literal. */
const UTF16_ASCII = new RegExp(`^(?:[^${NUL}]${NUL})*$`, 'u');

/**
 * UTF-16LE as the launcher shows it (decoded as UTF-8): the even places are the text and EVERY character at an odd place is U+0000, so the length is even.
 * Anything else that holds a U+0000 is `undefined`. A character outside ASCII breaks the pattern, so such a list is not read (J2).
 */
const utf16Text = (text: string): string | undefined => (UTF16_ASCII.test(text) ? text.replaceAll(NUL, '') : undefined);

/**
 * A call's text as the launcher decoded it (UTF-8), made plain: one leading U+FEFF dropped; a text that holds U+0000 is UTF-16LE (the even places) or
 * `undefined`. This is rules 1 and 2 of the decoder, and also what a `wsl.exe` message goes through before a word of it is looked at.
 */
export function wslDecoded(text: string): string | undefined {
  const unbommed = text.startsWith(BOM) ? text.slice(1) : text;

  return unbommed.includes(NUL) ? utf16Text(unbommed) : unbommed;
}

/**
 * The names of a list call's text: rules 1 and 2 (`wslDecoded`), then split at LF with one CR at a line's end dropped and empty lines dropped; a line that
 * fits the distro class is a name (a second copy is dropped); any other line counts in `ignored` and is never passed on.
 */
export function wslNames(text: string): WslNames {
  const plain = wslDecoded(text);

  return plain === undefined ? 'undecodable' : namesOf(plain);
}

const withoutCr = (line: string): string => (line.endsWith('\r') ? line.slice(0, -1) : line);

function namesOf(plain: string): { readonly names: readonly string[]; readonly ignored: number } {
  const lines = plain.split('\n').map(withoutCr).filter((line) => line !== '');

  return { names: [...new Set(lines.filter(distroOk))], ignored: lines.filter((line) => !distroOk(line)).length };
}
