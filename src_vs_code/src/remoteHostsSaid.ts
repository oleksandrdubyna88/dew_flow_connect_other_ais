import { dayOf } from './remoteHosts';
import type { Committed, HostStanding } from './remoteHosts';

/**
 * The words of the hosts store (E1.S1, § S1.8), English, in one table, so a unit test holds them. The CODES of the notices stay
 * string literals at their call sites in `remoteSessionHost.ts`; only text is here. No path and no disk error text is in any of it.
 */

/** Why a refusal was not saved: one of four fixed phrases, never a path or an error's text. */
export type UnkeptWhy = 'busy' | 'disk' | 'lost' | 'elsewhere';

const WHY: Readonly<Record<UnkeptWhy, string>> = {
  busy: 'another window is writing',
  disk: 'the disk refused the write',
  lost: 'the write did not stay',
  elsewhere: 'another window of this side could not save it',
};

const ASKED_DETAIL = '“Read” allows, with no further question for this host: reading the Claude session of its Creds terminals when you press take or add the question; and, every 10 minutes while such a terminal is open, reading which Claude Code, Codex and Antigravity accounts are signed in there, and their usage. For the usage, Claude Code is started on the host and asks Anthropic with the host’s own login; agy is started for its version. “Not this host” stops every ssh call of CoAI to this host.';

const KEY = 'coai.terminalAccountsAllowNewHosts';

const standingSaid = (standing: HostStanding | undefined): string => {
  if (standing === undefined) {
    return '';
  }
  if (standing.answer === 'no') {
    return `Your answer now: Not this host, given ${standing.askedUtc}.`;
  }

  return standing.how === 'auto' ? `This host was allowed automatically on ${dayOf(standing.askedUtc)}; nobody was asked.` : `Your answer now: Read, given ${standing.askedUtc}.`;
};

const forgetSaid = (allowNew: boolean): string => (allowNew
  ? '“Forget” removes the answer: the host then has none, and the next accounts round allows it again by itself.'
  : '“Forget” removes the answer: the host is then not read until you answer the question about it.');

const sideSaid = (side: string): string =>
  `A “Not this host” is kept on the side of this machine where you gave it — ${side}. “Read” or “Forget” in any window of that side lifts it. If the host is still not read a moment later, another window had not caught up yet: choose it once more. On another side (Windows, a WSL distro, a remote machine) choose it there.`;

export const HOSTS_SAID = {
  /** W1: the title of the question and of the change question. */
  title: (host: string): string => `Read the Claude session and the AI accounts on “${host}” over ssh?`,
  /** W2. */
  askedDetail: ASKED_DETAIL,
  /** W3: how the host stands, then W2, then *Forget*, then the side. */
  changeDetail: (standing: HostStanding | undefined, allowNew: boolean, side: string): string =>
    [standingSaid(standing), ASKED_DETAIL, forgetSaid(allowNew), sideSaid(side)].filter((part) => part.length > 0).join(' '),
  /** W4: a row's description in the list of hosts. */
  rowDescription: (standing: HostStanding, allowNew: boolean): string => {
    if (standing.answer === 'no') {
      return 'not read';
    }
    if (standing.how === 'person') {
      return 'the Claude session and the AI accounts are read';
    }

    return allowNew
      ? `allowed automatically on ${dayOf(standing.askedUtc)}: the Claude session and the AI accounts are read`
      : `allowed automatically on ${dayOf(standing.askedUtc)}; not read while ${KEY} is off`;
  },
  /** W4b. */
  rowDetail: (standing: HostStanding): string => (standing.how === 'auto' ? `nobody was asked; written ${standing.askedUtc}` : `asked ${standing.askedUtc}`),
  /** W4c. */
  noAnswerReadable: 'no answer can be read for this host',
  /** W5: the empty list. */
  emptyOn: 'No host is in the list yet. The host of a “Creds: <host> / <session>” terminal is allowed by itself at the first accounts round and is listed here then; choose it here to forbid it.',
  emptyOff: 'No host is in the list yet. The host of a “Creds: <host> / <session>” terminal is listed here once you answer the question about it.',
  emptyDamaged: 'The list of hosts read over ssh cannot be read, and no answer in it can be read. No host is read over ssh while that is so. Open a “Creds: <host> / <session>” terminal and run this again: your answer for its host repairs the list.',
  /** W6: the session door, a list or file that cannot be read, and an answer that could not be kept. */
  cannotRead: 'The list of hosts read over ssh cannot be read, so nothing was read. Run CoAI: hosts read over ssh and answer for a host there: your answer repairs the list.',
  fullOfRefusals: (host: string): string =>
    `The list of hosts read over ssh holds 100 hosts you refused, which is all it keeps, so your answer for “${host}” was not kept and nothing was read. Forget one of them in CoAI: hosts read over ssh, then answer again.`,
  notSaved: (host: string): string => `Your answer for “${host}” could not be saved, so the host is still not read and nothing was read. Answer again in a moment.`,
  /** W7: the change question over a damaged list. */
  damagedList: (readable: number, refusals: number, unreadable: number): string =>
    `The list of hosts read over ssh is damaged, so no host is read by itself until it is repaired. ${readable} answers can still be read, ${refusals} of them “Not this host”. ${unreadable} entries cannot be read, and one of them may have been a “Not this host”. Your answer here repairs the list: the answers that can be read are kept, and the entries that cannot are dropped. A host of a dropped entry has no answer afterwards, and with ${KEY} on it is allowed again by itself. Look through the list afterwards.`,
  /** W7b: the change question over a damaged file. */
  damagedFile: (side: string, kept: number): string =>
    `The file of hosts you refused on this side (${side}) cannot be read, so no host is read from this side until it is repaired. ${kept} “Not this host” can still be read and are kept. A “Not this host” that was kept only in the file may be lost, and so may a stop of the accounts round. Your answer here writes the file again, with the accounts round STOPPED: look through CoAI: hosts read over ssh, then start the round from the list of AI accounts.`,
  /** W9: a refusal or a Stop that the file did not take. */
  unkeptHost: (host: string, why: UnkeptWhy, side: string): string =>
    `Your “Not this host” for “${host}” is not saved yet (${WHY[why]}). No window of this side (${side}) reads a host over ssh until it is. Each window tries again by itself.`,
  unkeptStop: (why: UnkeptWhy, side: string): string =>
    `The stop of the accounts round is not saved yet (${WHY[why]}). No window of this side (${side}) reads a host over ssh until it is. Each window tries again by itself.`,
  cureMarked: '“Save now” writes it at once, also when another window holds the file and does not finish. If it still cannot be saved, check that the extension’s storage folder can be written.',
  cureUnmarked: 'Only THIS window knows your answer: the disk refused the note that tells the other windows. Keep this window open, and check that the extension’s storage folder can be written. Other windows of this side may still read hosts.',
  cureTorn: 'A note that could not be read was removed. It may have held a “Not this host” or a stop, so the accounts round is now stopped: look through CoAI: hosts read over ssh, then start the round.',
  /** W10: the ignored layer. */
  ignoredWorkspace: `This workspace sets \`${KEY}\`. It is ignored: only your own user settings count.`,
  ignoredFolder: `A folder of this workspace sets \`${KEY}\`. It is ignored: only your own user settings count.`,
  /** W11: a host's row over a damaged list or file. */
  rowDamagedList: 'the list of hosts cannot be read, so this host is not read. Choose this row and answer for this host: that repairs the list.',
  rowDamagedFile: 'the file of refused hosts cannot be read, so this host is not read. Choose this row and answer for this host: that repairs the file and leaves the round stopped.',
  /** W12. */
  rowWaiting: 'a “Not this host” or a stop is not saved yet on this side, so no host is read from it. It is tried again before every read. Choose this row to save it now.',
  /** W13. */
  startDamaged: 'The accounts round cannot be started: the file of refused hosts cannot be read. Answer for a host in CoAI: hosts read over ssh first.',
  startUnwritten: 'The start could not be saved, so the accounts round is still stopped. Choose this row again in a moment.',
  stoppedElsewhere: 'The accounts round is stopped. Choose this row to start it.',
} as const;

/**
 * W6: what an answer that the store did not keep says, or nothing. `answer` is what the person chose. The third sentence is for a *Read* or a
 * *Forget* that the file did not take: a "no" that the file did not take stands, and W9 says it is not saved yet.
 */
const NOT_KEPT: Readonly<Partial<Record<Committed['kind'], (host: string, answer: 'yes' | 'no' | 'forget') => string | undefined>>> = {
  full: (host) => HOSTS_SAID.fullOfRefusals(host),
  damaged: () => HOSTS_SAID.cannotRead,
  unwritten: (host, answer) => (answer === 'no' ? undefined : HOSTS_SAID.notSaved(host)),
};

export const notKeptSaid = (host: string, answer: 'yes' | 'no' | 'forget', result: Committed): string | undefined => NOT_KEPT[result.kind]?.(host, answer);
