import { agoWords } from './gaugeWords';
import { ACCOUNTS_SECONDS, ACCOUNTS_UNREADABLE_SAID, plainValue } from './remoteAccountsReply';
import type { ManagerWord } from './remoteAccountsReply';
import { claudeAccountKey } from './remoteAccountsRound';
import type { AccountsState, HostMemory, Memory } from './remoteAccountsRound';
import { dayOf } from './remoteHosts';
import type { HostStanding } from './remoteHosts';
import { HOSTS_SAID } from './remoteHostsSaid';
import { claudeLine, codexLine, weekResets } from './usageWords';
import type { ClaudeWindowLike } from './usageWords';

/**
 * The rows of the accounts list (E1.S4, contract C6). Pure: a view of what the round remembers and of the consent store, and a clock, go in;
 * the rows a QuickPick shows come out. `remoteAccountsHost.ts` draws them and handles a press.
 *
 * <p><b>Text from a VM reaches a row at ONE road, `forList`</b>, which calls `plainValue` (control and format characters out, every `$(` written
 * `$ (`, 200 characters at most) and then cuts at a length. It holds no character class of its own, and a scan of this file
 * (`remoteAccountsRows.test.ts`) holds that every text field of a reply and the runner's failure sentence passes it. Host names are not VM
 * text: they come from the terminal name's own rule.</p>
 *
 * <p><b>A command is only ever a value of `INSTALL_JQ`.</b> No text of a reply is part of one.</p>
 */

/** The ONE road by which text from a VM reaches a row: made plain, then cut at `max` characters with an ellipsis. Doing it twice changes nothing. */
export function forList(text: string, max: number): string {
  const plain = [...plainValue(text)];

  return plain.length > max ? `${plain.slice(0, Math.max(0, max - 1)).join('')}…` : plain.join('');
}

const EMAIL_MAX = 120;
const NAME_MAX = 60;
const SENTENCE_MAX = 200;

/** What to run on a host without `jq`, by the host's own package manager: exactly the plan's table, and the ONLY commands the list shows. */
export const INSTALL_JQ: Readonly<Record<ManagerWord, string | undefined>> = {
  'apt-get': 'sudo apt-get install -y jq',
  dnf: 'sudo dnf install -y jq',
  yum: 'sudo yum install -y jq',
  apk: 'apk add jq',
  none: undefined,
};

export const NO_MANAGER_SAID = 'Install `jq` on this host. CoAI found none of apt-get, dnf, yum and apk there.';

/** What a group says when nobody read its usage, and no host failed to: in this build CoAI does not ask Claude Code for usage (the policy is `off`). */
export const USAGE_OFF_SAID = 'usage not read: CoAI does not ask Claude Code for usage in this build';

const AGY_SAID = 'agy states no usage, and its account is not read';
const NOT_SHOWN_SAID = 'not shown: a value in the answer looked like a credential';
const NO_TERMINAL_SAID = 'No `Creds: <host> / <session>` terminal is open, so there is no host to read.';
const FLOOR_SAID = 'A round ran less than a minute ago.';
const TURNED_OFF_SAID = 'turned off: your answer for this host is “Not this host”';
const NOT_ASKED_SAID = 'not asked yet — choose this row to be asked';
const FULL_SAID = 'the list of hosts is full, so this host is not read';
const HOSTS_LABEL = '── Hosts ──';
const UNKNOWN_VERSION = '(version not known)';

/** What choosing a host row does: the change question, the shared question, or *Save now* for a refusal the file did not take. */
export type HostPress = 'change' | 'ask' | 'save';

export type Row =
  | { readonly kind: 'separator'; readonly label: string }
  | { readonly kind: 'usage'; readonly label: string; readonly description: string; readonly detail: string }
  | { readonly kind: 'host'; readonly host: string; readonly label: string; readonly description: string; readonly press: HostPress; readonly copy?: string }
  | { readonly kind: 'info'; readonly label: string; readonly action?: 'start' };

/** What the list needs to be drawn. The three optional fields are a person's press or a read of the file that the round does not carry. */
export interface AccountsView {
  /** Any `Creds:` terminal is open. */
  readonly live: boolean;
  readonly stopped: boolean;
  readonly busy: boolean;
  readonly memory: Memory;
  readonly standings: readonly HostStanding[];
  /** The hosts of the live `Creds:` terminals, sorted. */
  readonly hosts: readonly string[];
  readonly ignored: 'workspace' | 'folder' | undefined;
  readonly allowNew: boolean;
  /** The refusals file cannot be read: W11 and W13 say so in their second wording. */
  readonly damagedFile?: boolean;
  /** A press of the start row did not save the start. */
  readonly startUnwritten?: boolean;
  /** A press of *Refresh now* came inside the minute: one row says so, and the next redraw takes it away. */
  readonly floor?: boolean;
}

type Reading = NonNullable<HostMemory['reading']>['read'];

/** One host's reading, as the groups use it. `failed`: a later read of this host failed and its older reading stays. */
interface Seen {
  readonly host: string;
  readonly read: Reading;
  readonly atMs: number;
  readonly failed: boolean;
}

const seenOf = (host: string, memory: HostMemory | undefined): readonly Seen[] =>
  (memory?.reading === undefined ? [] : [{ host, read: memory.reading.read, atMs: memory.reading.atMs, failed: memory.failure !== undefined }]);

const newestOf = <T extends Seen>(members: readonly T[]): T | undefined => members.reduce<T | undefined>((best, one) => (best === undefined || one.atMs > best.atMs ? one : best), undefined);

const readAge = (atMs: number, nowMs: number): string => `read ${agoWords(nowMs - atMs)}`;

const separator = (label: string): Row => ({ kind: 'separator', label });

const usageRow = (label: string, description: string, detail: string): Row => ({ kind: 'usage', label, description, detail });

// ───────────────── Claude: one group for each account ─────────────────

type Found = Extract<Reading['claude'], { state: 'found' }>;

type ClaudeSeen = Seen & { readonly found: Found };

const claudeSeenOf = (one: Seen): readonly ClaudeSeen[] => (one.read.claude.state === 'found' ? [{ ...one, found: one.read.claude }] : []);

/** The readings that found a Claude account, grouped by the round's own key; a group keeps the order of the hosts. */
function claudeGroups(seen: readonly Seen[]): readonly (readonly ClaudeSeen[])[] {
  const groups = new Map<string, ClaudeSeen[]>();
  for (const one of seen.flatMap(claudeSeenOf)) {
    const key = claudeAccountKey(one.found, one.host);
    groups.set(key, [...(groups.get(key) ?? []), one]);
  }

  return [...groups.values()];
}

/** `Claude Code · <email> · <org>`; an account that is not known names its host in the account's place. */
function claudeSeparator(first: ClaudeSeen): string {
  const email = forList(first.found.email, EMAIL_MAX);
  const org = forList(first.found.org ?? '', NAME_MAX);
  if (email === '') {
    return `Claude Code · ${first.host}`;
  }

  return org === '' ? `Claude Code · ${email}` : `Claude Code · ${email} · ${org}`;
}

const windowsOf = (one: ClaudeSeen): readonly ClaudeWindowLike[] =>
  (one.found.usage.state === 'read'
    ? one.found.usage.windows.map((window): ClaudeWindowLike => (typeof window.name === 'string' ? window : { ...window, name: { model: forList(window.name.model, NAME_MAX) } }))
    : []);

const hasNumbers = (one: ClaudeSeen): boolean => one.found.usage.state === 'read';

const versionSaid = (found: Found): string => `Claude Code ${forList(found.version ?? UNKNOWN_VERSION, NAME_MAX)}`;

const USAGE_FAILED_SAID: Readonly<Record<'timeout' | 'error' | 'format' | 'model-call', (found: Found) => string>> = {
  format: (found) => `usage not read: ${versionSaid(found)} on this host printed no usage line this version understands`,
  'model-call': (found) => `usage not read: the usage command of ${versionSaid(found)} asked the model, so CoAI stopped asking this host`,
  timeout: () => `usage not read: Claude Code did not answer within ${ACCOUNTS_SECONDS.usage} s`,
  error: (found) => `usage not read: the usage command of ${versionSaid(found)} ended with an error`,
};

/** Why one reading's usage was not read; nothing for a usage another host read (`skipped`) or one that was. */
function usageFailure(one: ClaudeSeen): string | undefined {
  const { usage } = one.found;
  if (usage.state !== 'failed') {
    return undefined;
  }

  return USAGE_FAILED_SAID[usage.why](one.found);
}

/** The label of a group with no numbers: the newest failure of its hosts, or the sentence of a build that asks for none. */
const notReadSaid = (members: readonly ClaudeSeen[]): string => {
  const failed = newestOf(members.filter((one) => usageFailure(one) !== undefined));

  return (failed === undefined ? undefined : usageFailure(failed)) ?? USAGE_OFF_SAID;
};

const laterFailed = (members: readonly ClaudeSeen[]): string => members.filter((one) => one.failed).map((one) => ` · the later read of ${one.host} failed`).join('');

/** The label and the description of a group: the mod's line of the reading that has numbers, or why no host has them. */
function shownOf(members: readonly ClaudeSeen[], source: ClaudeSeen | undefined, nowMs: number): { readonly label: string; readonly description: string } {
  if (source === undefined) {
    return { label: notReadSaid(members), description: '' };
  }
  const windows = windowsOf(source);

  return { label: claudeLine(windows, nowMs), description: weekResets(windows, nowMs) };
}

/** One group: its separator and its one row. The numbers and the age are those of the newest reading that HAS numbers, else of the newest reading. */
function claudeGroupRows(members: readonly ClaudeSeen[], nowMs: number): readonly Row[] {
  const source = newestOf(members.filter(hasNumbers));
  const newest = source ?? newestOf(members) ?? members[0];
  const { label, description } = shownOf(members, source, nowMs);
  const detail = `${members.map((one) => one.host).join(', ')} · ${readAge(newest.atMs, nowMs)}${laterFailed(members)}`;

  return [separator(claudeSeparator(members[0])), usageRow(label, description, detail)];
}

/** A host whose Claude side is not an account: signed out, or unreadable for a reason that has its own sentence. `no-jq` is the host row's. */
const CLAUDE_SIDE_SAID: Readonly<Record<string, string | undefined>> = {
  'signed-out': 'signed out',
  auth: 'the account command of Claude Code printed nothing this version understands',
  timeout: `the account command of Claude Code did not answer within ${ACCOUNTS_SECONDS.account} s`,
  'credential-shape': NOT_SHOWN_SAID,
  'no-jq': undefined,
};

const claudeSideWord = (claude: Reading['claude']): string | undefined =>
  (claude.state === 'signed-out' ? 'signed-out' : claude.state === 'unreadable' ? claude.why : undefined);

const claudeSideSaid = (claude: Reading['claude']): string | undefined => {
  const word = claudeSideWord(claude);

  return word === undefined ? undefined : CLAUDE_SIDE_SAID[word];
};

function claudeSideRows(one: Seen, nowMs: number): readonly Row[] {
  const text = claudeSideSaid(one.read.claude);

  return text === undefined ? [] : [separator(`Claude Code · ${one.host}`), usageRow(text, '', readAge(one.atMs, nowMs))];
}

// ───────────────── Codex and Antigravity: one group for each host ─────────────────

type CodexFound = Extract<Reading['codex'], { state: 'found' }>;

const CODEX_FAILED_SAID: Readonly<Record<'no-sessions' | 'none' | 'error', string>> = {
  'no-sessions': 'no Codex session file is on this host',
  none: 'no usage record is near the end of the newest Codex sessions',
  error: 'the Codex session files could not be read',
};

const planPart = (found: CodexFound): string => {
  const plan = forList(found.plan ?? '', NAME_MAX);

  return plan === '' ? '' : ` · plan ${plan}`;
};

const NO_STAT_SAID = 'this host has no stat, so the time of the Codex session file was not read';

/** `from a Codex session <age> · [no-stat sentence · ]read <age>`: the age of the session is a dash when no time is known. */
function codexDetail(found: CodexFound, one: Seen, nowMs: number): string {
  const { usage } = found;
  if (usage.state !== 'read') {
    return readAge(one.atMs, nowMs);
  }
  const session = `from a Codex session ${usage.asOf === null ? '—' : agoWords(nowMs - usage.asOf.ms)}`;

  return [session, ...(usage.statMissing ? [NO_STAT_SAID] : []), readAge(one.atMs, nowMs)].join(' · ');
}

const codexUnreadableRows = (one: Seen, why: 'no-jq' | 'credential-shape', nowMs: number): readonly Row[] =>
  (why === 'credential-shape' ? [separator(`Codex · ${one.host}`), usageRow(NOT_SHOWN_SAID, '', readAge(one.atMs, nowMs))] : []);

function codexFoundRows(one: Seen, codex: CodexFound, nowMs: number): readonly Row[] {
  const label = codex.usage.state === 'read' ? codexLine(codex.usage.windows, nowMs) : CODEX_FAILED_SAID[codex.usage.why];

  return [separator(`Codex · account name not shown by Codex${planPart(codex)} · ${one.host}`), usageRow(label, '', codexDetail(codex, one, nowMs))];
}

function codexRows(one: Seen, nowMs: number): readonly Row[] {
  const { codex } = one.read;
  if (codex.state === 'absent') {
    return [];
  }

  return codex.state === 'unreadable' ? codexUnreadableRows(one, codex.why, nowMs) : codexFoundRows(one, codex, nowMs);
}

function agyRows(one: Seen, nowMs: number): readonly Row[] {
  const { agy } = one.read;
  if (agy.state === 'absent') {
    return [];
  }
  if (agy.state === 'unreadable') {
    return [separator(`Antigravity · ${one.host}`), usageRow(NOT_SHOWN_SAID, '', readAge(one.atMs, nowMs))];
  }

  return [separator(`Antigravity ${forList(agy.version ?? UNKNOWN_VERSION, NAME_MAX)} · ${one.host}`), usageRow(AGY_SAID, '', readAge(one.atMs, nowMs))];
}

/** The groups, vendor by vendor: Claude's accounts, then Claude's hosts that have none, then Codex, then Antigravity. */
function groupRows(view: AccountsView, nowMs: number): readonly Row[] {
  const seen = view.hosts.flatMap((host) => seenOf(host, view.memory.get(host)));

  return [
    ...claudeGroups(seen).flatMap((members) => claudeGroupRows(members, nowMs)),
    ...seen.flatMap((one) => claudeSideRows(one, nowMs)),
    ...seen.flatMap((one) => codexRows(one, nowMs)),
    ...seen.flatMap((one) => agyRows(one, nowMs)),
  ];
}

// ───────────────── the hosts ─────────────────

/** What a host's row says, and what choosing it does. */
interface Said {
  readonly text: string;
  readonly press: HostPress;
  readonly copy?: string;
}

const said = (text: string, press: HostPress = 'change'): Said => ({ text, press });

const ADMISSION_SAID: Readonly<Record<'refused' | 'not-asked' | 'full' | 'damaged' | 'unwritten', (view: AccountsView) => Said>> = {
  refused: () => said(TURNED_OFF_SAID),
  'not-asked': () => said(NOT_ASKED_SAID, 'ask'),
  full: () => said(FULL_SAID),
  damaged: (view) => said(view.damagedFile === true ? HOSTS_SAID.rowDamagedFile : HOSTS_SAID.rowDamagedList),
  unwritten: () => said(HOSTS_SAID.rowWaiting, 'save'),
};

const isNo = (standing: HostStanding | undefined): boolean => standing?.answer === 'no';

const unasked = (view: AccountsView, standing: HostStanding | undefined): boolean => standing === undefined && !view.allowNew;

type BlockKind = keyof typeof ADMISSION_SAID;

/** Why a host is not read: a "no", what the door answered, or no question asked yet with the setting off. */
const blockedKind = (view: AccountsView, standing: HostStanding | undefined, memory: HostMemory): BlockKind | undefined =>
  (isNo(standing) ? 'refused' : (memory.admission ?? (unasked(view, standing) ? 'not-asked' : undefined)));

function blocked(view: AccountsView, standing: HostStanding | undefined, memory: HostMemory): Said | undefined {
  const kind = blockedKind(view, standing, memory);

  return kind === undefined ? undefined : ADMISSION_SAID[kind](view);
}

const failedSaid = (failure: NonNullable<HostMemory['failure']>, nowMs: number): string => {
  const sentence = forList(failure.sentence, SENTENCE_MAX);
  const age = agoWords(nowMs - failure.atMs);

  return sentence === '' ? `the last read failed ${age}.` : `the last read failed ${age}: ${sentence}`;
};

/** `jq` is missing: the fixed command of the table with a copy of exactly it, or the sentence for a host with no manager. */
function jqSaid(word: ManagerWord | undefined): Said | undefined {
  if (word === undefined) {
    return undefined;
  }
  const command = INSTALL_JQ[word];
  if (command === undefined) {
    return said(NO_MANAGER_SAID);
  }

  return { text: `jq is missing on this host. Run there: ${command}${word === 'apk' ? ' (to be run as root)' : ''}`, press: 'change', copy: command };
}

/** What went wrong with the last answer of a host, in order: an answer that could not be read, a read that failed, `jq` missing. */
function problem(host: string, memory: HostMemory, nowMs: number): Said | undefined {
  if (memory.unreadable !== undefined) {
    return said(`The AI accounts on “${host}” could not be read: ${ACCOUNTS_UNREADABLE_SAID[memory.unreadable.why]}.`);
  }
  if (memory.failure !== undefined) {
    return said(failedSaid(memory.failure, nowMs));
  }

  return jqSaid(memory.reading?.read.jqMissing);
}

const plainSaid = (memory: HostMemory, nowMs: number): Said =>
  said(memory.reading === undefined ? 'never read yet' : readAge(memory.reading.atMs, nowMs));

const autoSaid = (standing: HostStanding | undefined): readonly string[] =>
  (standing?.answer === 'yes' && standing.how === 'auto' ? [`allowed automatically on ${dayOf(standing.askedUtc)}. Choose this row to change that.`] : []);

const rowOf = (host: string, main: Said, more: readonly string[]): Row =>
  ({ kind: 'host', host, label: host, description: [main.text, ...more].join(' · '), press: main.press, ...(main.copy === undefined ? {} : { copy: main.copy }) });

const mainOf = (held: Said | undefined, host: string, memory: HostMemory, nowMs: number): Said => held ?? problem(host, memory, nowMs) ?? plainSaid(memory, nowMs);

function hostRow(view: AccountsView, host: string, nowMs: number): Row {
  const standing = view.standings.find((one) => one.host === host);
  const memory = view.memory.get(host) ?? {};
  const held = blocked(view, standing, memory);

  return rowOf(host, mainOf(held, host, memory, nowMs), held === undefined ? autoSaid(standing) : []);
}

// ───────────────── the list ─────────────────

const ignoredRows = (ignored: AccountsView['ignored']): readonly Row[] =>
  (ignored === undefined ? [] : [{ kind: 'info', label: ignored === 'workspace' ? HOSTS_SAID.ignoredWorkspace : HOSTS_SAID.ignoredFolder }]);

/** The stopped row: the wording follows what a press could not do. It offers to start unless the file cannot be read. */
function startRow(view: AccountsView): Row {
  if (view.damagedFile === true) {
    return { kind: 'info', label: HOSTS_SAID.startDamaged };
  }

  return { kind: 'info', label: view.startUnwritten === true ? HOSTS_SAID.startUnwritten : HOSTS_SAID.stoppedElsewhere, action: 'start' };
}

const floorRows = (view: AccountsView): readonly Row[] => (view.floor === true ? [{ kind: 'info', label: FLOOR_SAID }] : []);

/**
 * The rows, in order: the ignored-layer row; then, when stopped, ONLY the start row; when no `Creds:` terminal is open, ONLY the one
 * sentence; else the groups, the `── Hosts ──` separator and one row for each host.
 */
export function accountsRows(view: AccountsView, nowMs: number): readonly Row[] {
  const head = ignoredRows(view.ignored);
  if (view.stopped) {
    return [...head, startRow(view)];
  }
  if (!view.live) {
    return [...head, { kind: 'info', label: NO_TERMINAL_SAID }];
  }

  return [...head, ...floorRows(view), ...groupRows(view, nowMs), separator(HOSTS_LABEL), ...view.hosts.map((host) => hostRow(view, host, nowMs))];
}

// ───────────────── the notices ─────────────────

const DAY_MS = 86_400_000;

const isAuto = (standing: HostStanding): boolean => standing.answer === 'yes' && standing.how === 'auto';

/** Due again when the last notice is absent or a day old; a time that cannot be read is no notice. */
const dueAgain = (last: string | undefined, nowMs: number): boolean => last === undefined || !(nowMs - Date.parse(last) < DAY_MS);

const autoDue = (state: AccountsState, standings: readonly HostStanding[], nowMs: number): readonly string[] => {
  const last = state.lastAutoNoticeUtc;

  return dueAgain(last, nowMs) ? standings.filter((one) => isAuto(one) && (last === undefined || one.askedUtc > last)).map((one) => one.host) : [];
};

const firstDue = (state: AccountsState, standings: readonly HostStanding[], firstRoundEnded: boolean): readonly string[] =>
  (firstRoundEnded && state.toldUtc === undefined ? standings.filter((one) => one.answer === 'yes' && one.how === 'person').map((one) => one.host) : []);

/**
 * The hosts a notice is due for. `autoHosts`: the automatic "yes" rows allowed after the last such notice, once a day. `first`: the
 * person's own "yes" rows, once, at the end of the first round, while the person was never told. The host commits `{ told }` whether or
 * not `first` is empty (it checks `toldUtc` and the round itself).
 */
export const noticesDue = (state: AccountsState, standings: readonly HostStanding[], firstRoundEnded: boolean, nowMs: number): { readonly autoHosts: readonly string[]; readonly first: readonly string[] } =>
  ({ autoHosts: autoDue(state, standings, nowMs), first: firstDue(state, standings, firstRoundEnded) });
