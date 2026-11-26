import { ACCOUNTS_UNREADABLE_SAID, plainValue } from './remoteAccountsReply';
import type { ManagerWord } from './remoteAccountsReply';

/**
 * What the accounts tab's model keeps of the cut's rows (E2.S2). The rows of the QuickPick, their press and their notices are gone with the
 * QuickPick (E2.S3); the model is `remoteAccountsTable.ts`.
 *
 * <p><b>Text from a VM reaches the model at ONE road, `forList`</b>, which calls `plainValue` (control and format characters out, every `$(` written
 * `$ (`, 200 characters at most) and then cuts at a length. It holds no character class of its own.</p>
 *
 * <p><b>A command is only ever a value of `INSTALL_JQ`.</b> No text of a reply is part of one.</p>
 */

/** The ONE road by which text from a VM reaches the model: made plain, then cut at `max` characters with an ellipsis. Doing it twice changes nothing. */
export function forList(text: string, max: number): string {
  const plain = [...plainValue(text)];

  return plain.length > max ? `${plain.slice(0, Math.max(0, max - 1)).join('')}…` : plain.join('');
}

/** What to run on a host without `jq`, by the host's own package manager: exactly the plan's table, and the ONLY commands the tab copies. */
export const INSTALL_JQ: Readonly<Record<ManagerWord, string | undefined>> = {
  'apt-get': 'sudo apt-get install -y jq',
  dnf: 'sudo dnf install -y jq',
  yum: 'sudo yum install -y jq',
  apk: 'apk add jq',
  none: undefined,
};

export const NO_MANAGER_SAID = 'Install `jq` on this host. CoAI found none of apt-get, dnf, yum and apk there.';

const SENTENCE_MAX = 200;

/** The clause that says why a whole answer could not be read: the closed sentence of its word, and for `unsafe` the path that failed the check, when it is shown. */
export const unreadableClause = (unreadable: { readonly why: keyof typeof ACCOUNTS_UNREADABLE_SAID; readonly path?: string | null }): string =>
  (unreadable.why === 'unsafe' && typeof unreadable.path === 'string' ? `${forList(unreadable.path, SENTENCE_MAX)} is writable by another user` : ACCOUNTS_UNREADABLE_SAID[unreadable.why]);

/** What a group says when nobody read its usage, and no host failed to: the round did not ask for it (a block, or an account that another place read). */
export const USAGE_NOT_ASKED_SAID = 'usage not read: it was not asked for in this round';

/** The sentence of a program that another user can write, refused before its start as UID 0 (E3.S2, § R2.6, § R2.7). */
export const unsafeSaid = (path: string | null): string => (path === null ? 'not read: a program there is writable by another user' : `not read: ${forList(path, SENTENCE_MAX)} is writable by another user`);
