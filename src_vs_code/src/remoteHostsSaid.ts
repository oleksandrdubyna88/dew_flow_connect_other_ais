import type { Committed, HostButton, HostStanding } from './remoteHosts';
import type { Allowed } from './remotePermission';

/**
 * The words of the hosts store and of the permission (E1.S1, § S1.8; E2.S1, § P12 of its spec), English, in one table, so a unit test holds them. The
 * CODES of the notices stay string literals at their call sites in `remoteSessionHost.ts`; only text is here. No path and no disk error text is in any of it.
 */

/** Why a refusal or a withdrawal was not saved: one of four fixed phrases, never a path or an error's text. */
export type UnkeptWhy = 'busy' | 'disk' | 'lost' | 'elsewhere';

const WHY: Readonly<Record<UnkeptWhy, string>> = {
  busy: 'another window is writing',
  disk: 'the disk refused the write',
  lost: 'the write did not stay',
  elsewhere: 'another window of this side could not save it',
};

/**
 * The key of the retired setting, which W10's two sentences are built from until W10 is deleted with `ignoredRows` (X7, X13 of the stories document).
 * No code reads the setting; the commit that deletes W10 deletes this line too.
 */
const KEY = 'coai.terminalAccountsAllowNewHosts';

/** The three choices of the change question, by their buttons (§ P10). */
export const BUTTON_SAID: Readonly<Record<HostButton, string>> = {
  'not-this-host': '“Not this host” stops every ssh call of CoAI to this host, from the accounts round and from the chat doors.',
  'read-again': '“Read this host again” removes your “Not this host”: the host is then read under the permission like any other.',
  'forget': '“Forget” removes the stored older answer. It changes nothing about what is read.',
};

/** The ways a host stands for W3: refused, or not, with the permission in force or not. */
export interface StandsAs {
  readonly standing: HostStanding | undefined;
  readonly permitted: boolean;
}

const standingSaid = ({ standing, permitted }: StandsAs): string => {
  if (standing?.answer === 'no') {
    return `Your answer now: Not this host, given ${standing.askedUtc}.`;
  }

  return permitted ? 'This host is read under your permission.' : 'This host is not read: there is no permission to read hosts.';
};

const storedYesSaid = (standing: HostStanding | undefined): string =>
  (standing?.answer === 'yes' ? `An answer “Read” of an earlier version is stored (given ${standing.askedUtc}); it is not used any more.` : '');

const sideSaid = (side: string): string =>
  `A “Not this host” is kept on the side of this machine where you gave it — ${side}. “Read this host again” in any window of that side lifts it. If the host is still not read a moment later, another window had not caught up yet: choose it once more. On another side (Windows, a WSL distro, a remote machine) choose it there.`;

/** The second sentence of W9's titles: every window of the side when a marker tells them, only this window when none could be made. */
const reach = (marked: boolean, side: string): string =>
  (marked ? `No window of this side (${side}) reads a host over ssh until it is.` : 'This window reads no host over ssh until it is.');

/** W16's cure for an *Allow* that was not saved. */
const ALLOW_CURE: Readonly<Record<'busy' | 'disk' | 'lost', string>> = {
  busy: 'If another window hangs, “Save now” takes the file over; then choose “Allow reading” again.',
  disk: 'Check that the extension’s storage folder can be written; “Save now” tries what waits.',
  lost: 'Check that the extension’s storage folder can be written; “Save now” tries what waits.',
};

const CANNOT_READ = 'The list of hosts read over ssh cannot be read, so nothing was read. Run CoAI: hosts read over ssh and choose “Not this host” or “Read this host again” for a host there: your answer repairs the list.';

const WAITING = 'A “Not this host”, a stop or a withdrawal is not saved yet on this side, so nothing was read. Choose “Save now” — the first row of CoAI: hosts read over ssh.';

/** What the first row of the hosts list is, in each state of the permission (W18). */
export interface PermissionRowSaid {
  readonly label: string;
  readonly description: string;
}

export const HOSTS_SAID = {
  /** W1: the title of the change question. */
  title: (host: string): string => `Read the host “${host}” over ssh, or not?`,
  /** W3: how the host stands, an older "yes" row, one sentence for each button that is offered, then the side. */
  changeDetail: (stands: StandsAs, buttons: readonly HostButton[], side: string): string =>
    [standingSaid(stands), storedYesSaid(stands.standing), ...buttons.map((one) => BUTTON_SAID[one]), sideSaid(side)].filter((part) => part.length > 0).join(' '),
  /** W4: a row's description in the list of hosts. */
  rowDescription: (refused: boolean, permitted: boolean): string => (refused ? 'not read: “Not this host”' : permitted ? 'read under the permission' : 'not read: no permission'),
  /** W4b. */
  rowDetail: (standing: HostStanding): string => {
    if (standing.answer === 'no') {
      return `given ${standing.askedUtc}`;
    }

    return standing.how === 'auto' ? 'an automatic entry of a test build is stored and not used' : 'an older “Read” answer is stored and not used';
  },
  /** W4c. */
  noAnswerReadable: 'no answer can be read for this host',
  /** W6: the session door on `damaged`, a list that cannot be read, and an answer that could not be kept. */
  cannotRead: CANNOT_READ,
  fullOfRefusals: (host: string): string =>
    `The list of hosts read over ssh holds 100 hosts you refused, which is all it keeps, so your answer for “${host}” was not kept and nothing was read. Forget one of them in CoAI: hosts read over ssh, then answer again.`,
  notSaved: (host: string): string => `Your answer for “${host}” could not be saved, so the host is still not read and nothing was read. Answer again in a moment.`,
  /** W7: the change question over a damaged list. */
  damagedList: (readable: number, refusals: number, unreadable: number): string =>
    `The list of hosts read over ssh is damaged, so no host is read by itself until it is repaired. ${readable} answers can still be read, ${refusals} of them “Not this host”. ${unreadable} entries cannot be read, and one of them may have been a “Not this host”. Your answer here repairs the list: the answers that can be read are kept, and the entries that cannot are dropped. A host of a dropped entry has no answer afterwards and is read under the permission like any other host. Look through the list afterwards.`,
  /** W7b: the change question over a damaged file. */
  damagedFile: (side: string, kept: number): string =>
    `The file of hosts you refused on this side (${side}) cannot be read, so no host is read from this side until it is repaired. ${kept} “Not this host” can still be read and are kept. A “Not this host” that was kept only in the file may be lost, and so may a stop of the accounts round. Your answer here writes the file again, with the accounts round STOPPED and with no permission to read: look through CoAI: hosts read over ssh, choose “Allow reading…” there, then start the round in AI accounts and usage.`,
  /** W9: a refusal, a Stop or a withdrawal that the file did not take. */
  unkeptHost: (host: string, why: UnkeptWhy, side: string, marked = true): string =>
    `Your “Not this host” for “${host}” is not saved yet (${WHY[why]}). ${reach(marked, side)} Each window tries again by itself.`,
  unkeptStop: (why: UnkeptWhy, side: string, marked = true): string =>
    `The stop of the accounts round is not saved yet (${WHY[why]}). ${reach(marked, side)} Each window tries again by itself.`,
  unkeptWithdrawal: (why: UnkeptWhy): string =>
    `Your withdrawal of the permission is not saved yet (${WHY[why]}). This window reads no host until it is; the other windows stop when the shared state reaches them.`,
  cureMarked: '“Save now” writes it at once, also when another window holds the file and does not finish. If it still cannot be saved, check that the extension’s storage folder can be written.',
  cureUnmarked: 'Only THIS window knows your answer: the disk refused the note that tells the other windows. Keep this window open, and check that the extension’s storage folder can be written. Other windows of this side may still read hosts.',
  cureTorn: 'A note that could not be read was removed. It may have held a “Not this host” or a stop, so the accounts round is now stopped: look through CoAI: hosts read over ssh, then start the round.',
  cureWithdrawal: '“Save now” writes it at once. Until then only this window and the shared state hold it: keep this window open, and check that the extension’s storage folder can be written.',
  cureWithdrawalBusy: '“Save now” takes the hosts file over from a window that hangs and checks your withdrawal. Keep this window open until then.',
  /** W10: the ignored layer (deleted with `ignoredRows`, by the story that deletes its last caller). */
  ignoredWorkspace: `This workspace sets \`${KEY}\`. It is ignored: only your own user settings count.`,
  ignoredFolder: `A folder of this workspace sets \`${KEY}\`. It is ignored: only your own user settings count.`,
  /** W11: a host's row over a damaged list or file. */
  rowDamagedList: 'the list of hosts cannot be read, so this host is not read. Choose “Not this host” or “Read this host again” for it: that repairs the list.',
  rowDamagedFile: 'the file of refused hosts cannot be read, so this host is not read. Choose “Not this host” or “Read this host again” for it: that repairs the file, leaves the round stopped and leaves no permission.',
  /** W12: the door answer `waiting`, at the session door and in the tab. */
  waiting: WAITING,
  rowWaiting: WAITING,
  /** W13. */
  startDamaged: 'The accounts round cannot be started: the file of refused hosts cannot be read. Answer for a host in CoAI: hosts read over ssh first.',
  startUnwritten: 'The start could not be saved, so the accounts round is still stopped. Choose this row again in a moment.',
  stoppedElsewhere: 'The accounts round is stopped. Choose this row to start it.',
  /** W14: the ONE question. */
  permissionTitle: 'Allow CoAI to read the hosts of your Creds terminals?',
  permissionDetail: (side: string): string =>
    `“Allow reading” lets CoAI read every host of an open “Creds: <host> / <session>” terminal with no further question — the ones open now and the ones you open later. It logs in over ssh with your own keys every 10 minutes while one is open. In a Windows window with a “Creds: wsl / …” terminal it also reads every Linux user of each running WSL distro, root too; on a distro with systemd this opens a login session that runs the user’s profile. It reads which of Claude Code, Codex and Antigravity are there, their accounts and usage; claude and agy, as found there, are started (root’s as root), and Claude Code rewrites its cache files. Take or add the question reads that terminal’s Claude session. CoAI itself installs and writes nothing there. To stop: “Not this host”, “Stop the round”, or “Withdraw the permission” for all reads. The permission is for ${side} only; another side asks its own.`,
  /** W15: the session door on `no-permission`. */
  notNow: 'Nothing was read: CoAI has no permission to read hosts. Press again and choose “Allow reading”.',
  permissionGiven: 'The permission is given. Press again to read.',
  withdrawnMeanwhile: 'The permission to read hosts was withdrawn meanwhile, so the rest was not read.',
  /** W17: after *Withdraw the permission* in the hosts list. */
  withdrew: 'The permission is withdrawn: CoAI reads no host. Every side of this machine stops at its next read; “Allow reading” on a side allows that side again.',
  /** W16: an *Allow* that was not kept. */
  allowNotKept: (result: Allowed): string => ALLOW_NOT_KEPT[result.kind](result),
  /** W18: the first row of the hosts list, in each of its four states. */
  permissionRow: (state: 'none' | 'waiting' | 'damaged' | { readonly given: string }): PermissionRowSaid => PERMISSION_ROW[typeof state === 'string' ? state : 'given'](state),
} as const;

const ALLOW_NOT_KEPT: Readonly<Record<Allowed['kind'], (result: Allowed) => string>> = {
  unwritten: (result) => {
    const why = (result as Extract<Allowed, { kind: 'unwritten' }>).why;

    return `The permission could not be saved (${WHY[why]}), so nothing changed and nothing is read. Choose “Allow reading” again in a moment. ${ALLOW_CURE[why]}`;
  },
  withdrawn: () => 'The permission was withdrawn while it was being saved, so nothing is read. Choose “Allow reading” again if you want it.',
  damaged: () => CANNOT_READ,
  given: () => '',
  kept: () => '',
};

const PERMISSION_ROW: Readonly<Record<'none' | 'waiting' | 'damaged' | 'given', (state: unknown) => PermissionRowSaid>> = {
  none: () => ({ label: 'Allow reading…', description: 'no permission yet: nothing is read' }),
  given: (state) => ({
    label: 'Withdraw the permission',
    description: `given ${(state as { readonly given: string }).given}: every host of your Creds terminals is read, but the ones you refused`,
  }),
  waiting: () => ({ label: 'Save now', description: 'a “Not this host”, a stop or a withdrawal is not saved yet on this side, so nothing is read' }),
  damaged: () => ({
    label: 'The list or the file of hosts cannot be read',
    description: 'nothing is read. Choose a host below and answer for it: that repairs it. If no host is listed, open a “Creds: <host> / <session>” terminal and run this again.',
  }),
};

/**
 * W6: what an answer that the store did not keep says, or nothing. `answer` is what the person chose. The third sentence is for a *Read this host again*
 * or a *Forget* that the file did not take: a "no" that the file did not take stands, and W9 says it is not saved yet.
 */
const NOT_KEPT: Readonly<Partial<Record<Committed['kind'], (host: string, answer: 'no' | 'forget') => string | undefined>>> = {
  full: (host) => HOSTS_SAID.fullOfRefusals(host),
  damaged: () => HOSTS_SAID.cannotRead,
  unwritten: (host, answer) => (answer === 'no' ? undefined : HOSTS_SAID.notSaved(host)),
};

export const notKeptSaid = (host: string, answer: 'no' | 'forget', result: Committed): string | undefined => NOT_KEPT[result.kind]?.(host, answer);
