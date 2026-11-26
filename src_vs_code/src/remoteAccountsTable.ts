import { byCodeUnit } from './codeUnitOrder';
import { ACCOUNTS_SECONDS, ACCOUNTS_UNREADABLE_SAID, plainValue } from './remoteAccountsReply';
import type { AccountsRead, ManagerWord } from './remoteAccountsReply';
import { NO_MANAGER_SAID, USAGE_OFF_SAID, forList } from './remoteAccountsRows';
import type { HostMemory, Memory } from './remoteAccountsRound';
import type { HostStanding } from './remoteHosts';
import { HOSTS_SAID } from './remoteHostsSaid';
import type { Permission } from './remotePermission';
import { percentLeft, windowLabel } from './usageWords';

/**
 * The model of the accounts tab (E2.S2, contract C4). PURE: what the window holds goes in, and ONE JSON-serialisable value comes out, so the
 * page only draws. Every time in it is an ABSOLUTE instant (ms since the epoch), never an age: the page writes the ages from its own clock.
 *
 * <p><b>A missing number is never a zero.</b> A window's `left` is made by `percentLeft` in ONE place (`cellOf`) and is `null` for anything that
 * is not a number. <b>Every text that came from a VM goes through `forList` once</b>, and a command is never part of the model: a host without
 * `jq` carries its closed manager word, and the page and the panel look the command up in `INSTALL_JQ`.</p>
 *
 * <p>The groupings and the sort are NOT here: `remoteAccountsGroups.ts` derives them, in the page, from this one model.</p>
 */

const EMAIL_MAX = 254;
const NAME_MAX = 60;
const SENTENCE_MAX = 200;
const FIVE_HOURS = 300;
const WEEK = 10_080;

export type ToolName = 'claude-code' | 'codex' | 'agy';

/** A window as a cell: the percent LEFT or `null`, and the reset as an instant or `null`. */
export interface Cell {
  readonly left: number | null;
  readonly resetsAt: number | null;
}

/** A window named in its cell: Claude's single-model weekly windows, and a Codex window that is neither 5-hour nor weekly. */
export interface NamedCell extends Cell {
  readonly name: string;
}

export interface Account {
  /** The account as it is said: email and organization, or the sentence that stands for it. */
  readonly text: string;
  readonly plan: string | null;
}

/** One tool's row at one place. */
export interface ToolRow {
  readonly place: string;
  readonly tool: ToolName;
  /** The version under the tool: its text, `not known`, or `null` for a tool that states none (Codex). */
  readonly version: string | null;
  readonly account: Account;
  /** Vendor and whole account text, or `null` when the account is not known (Claude Code only). */
  readonly accountKey: string | null;
  /** How many places of this window answered with this known account; `null` when it is one or not known. */
  readonly sharedAcross: number | null;
  readonly five: Cell;
  readonly week: Cell;
  /** Claude's single-model weekly windows. */
  readonly models: readonly NamedCell[];
  /** A Codex window of another length. */
  readonly others: readonly NamedCell[];
  /** Why the two window cells are empty: spans both, with its own sentence. `null` when the numbers are shown. */
  readonly span: string | null;
  /** Codex: the instant of its session file or record. */
  readonly session: number | null;
  /** A Codex session whose time was not read because the host has no `stat`. */
  readonly note: string | null;
  /** The tool was found and its account is the row's: `false` for a row that only says why it could not be read (signed out, a credential-shaped value). */
  readonly found: boolean;
  /** The instant of the read that this row comes from: the older read for a place whose last read failed. */
  readonly readAt: number;
}

export type HeadKind = 'read' | 'never' | 'not-allowed' | 'failed' | 'jq-missing' | 'wsl';

/** A place's heading: its kind, its instant and the whole sentence (the words of it are the model's, the time is the page's). */
export interface Head {
  readonly kind: HeadKind;
  /** The read's instant (`read`, `jq-missing`), the failure's (`failed`), else `null`. */
  readonly at: number | null;
  /** The sentence; `''` for `read`, whose words are the time only. */
  readonly said: string;
  /** `jq-missing`: the closed manager word. */
  readonly manager: ManagerWord | null;
}

export interface Block {
  /** The scope host as the terminal named it, WHOLE (its alphabet is closed): what a message names and what groups. E2 has no other kind of place. */
  readonly host: string;
  /** The host as it is drawn: cut at the sentence length through `forList`. Never a key and never posted. */
  readonly shown: string;
  readonly head: Head;
  readonly rows: readonly ToolRow[];
  /** The instant of the older read whose rows these are, after a failure; else `null`. */
  readonly olderAt: number | null;
  /** A read place with no tool: one line. */
  readonly line: string | null;
  /** Why this place has no Claude account, as a sentence. */
  readonly noClaude: string;
  /** *Not this host* is on the heading. */
  readonly canRefuse: boolean;
  /** *Read this host again* is on the heading. */
  readonly canReadAgain: boolean;
}

export type RoundPart =
  | { readonly kind: 'idle' }
  | { readonly kind: 'running'; readonly host: string; readonly index: number; readonly total: number }
  | { readonly kind: 'ended'; readonly at: number; readonly read: number; readonly asked: number };

export interface Toolbar {
  readonly stopped: boolean;
  readonly round: RoundPart;
}

export type PermissionPart =
  | { readonly kind: 'none' | 'waiting'; readonly said: string }
  | { readonly kind: 'given'; readonly at: number | null; readonly utc: string };

export interface TableModel {
  readonly v: 1;
  readonly toolbar: Toolbar;
  readonly permission: PermissionPart;
  readonly blocks: readonly Block[];
  /** The one sentence when no `Creds:` terminal is open; else `null`. */
  readonly empty: string | null;
}

/** What the round is doing, as the window knows it. */
export type RoundNow =
  | { readonly kind: 'idle' }
  | { readonly kind: 'running'; readonly host: string; readonly index: number; readonly total: number }
  | { readonly kind: 'ended'; readonly endedMs: number; readonly read: number; readonly asked: number };

export interface TableInput {
  /** The scope hosts of the live `Creds:` terminals, in any order. */
  readonly hosts: readonly string[];
  readonly memory: Memory;
  readonly standings: readonly HostStanding[];
  readonly stopped: boolean;
  readonly permission: Permission;
  /** The refusals list or file cannot be read: every host's door says `damaged` (W11). */
  readonly damaged?: 'list' | 'file' | undefined;
  readonly round: RoundNow;
  /** The window runs on Windows (the `wsl` sentence). */
  readonly windows: boolean;
}

// ───────────────── the sentences the model owns ─────────────────

export const NO_TERMINAL_SAID = 'No `Creds: <host> / <session>` terminal is open, so there is no host to read.';
export const WSL_WINDOWS_SAID = 'WSL on this PC is not read by this build';
export const WSL_ELSEWHERE_SAID = 'WSL is read from a Windows window only';
const NEVER_SAID = 'never read yet';
const REFUSED_SAID = 'not allowed: your answer for this host is “Not this host”';
const NO_PERMISSION_SAID = 'not read: there is no permission to read hosts';
const JQ_SAID = 'jq is missing on this host, so nothing can be read there.';
const NO_TOOL_SAID = 'no Claude Code, Codex or Antigravity found';
const NO_CLAUDE_SAID = 'no Claude Code found';
const NOT_SHOWN_SAID = 'not shown: a value in the answer looked like a credential';
const NOT_KNOWN = 'not known';
const ACCOUNT_NOT_KNOWN = 'account not known';
const CODEX_ACCOUNT_SAID = 'account name not shown by Codex';
const AGY_ACCOUNT_SAID = 'its account is not read';
const AGY_USAGE_SAID = 'agy states no usage';
const NO_FIGURE_SAID = 'no figure was read for this tool';
const NO_STAT_SAID = 'this host has no stat, so the time of the Codex session file was not read';
export const PERMISSION_NONE_SAID = 'CoAI reads nothing yet. With your permission it logs in over ssh, with your own keys, to every host of an open “Creds: <host> / <session>” terminal every 10 minutes, and reads which of Claude Code, Codex and Antigravity are there, with their accounts and usage.';
export const PERMISSION_WAITING_SAID = HOSTS_SAID.waiting;

const CLAUDE_SIDE_SAID: Readonly<Record<string, string>> = {
  'signed-out': 'signed out',
  auth: 'the account command of Claude Code printed nothing this version understands',
  timeout: `the account command of Claude Code did not answer within ${ACCOUNTS_SECONDS.account} s`,
  'credential-shape': NOT_SHOWN_SAID,
};

const CODEX_FAILED_SAID: Readonly<Record<'no-sessions' | 'none' | 'error', string>> = {
  'no-sessions': 'no Codex session file is on this host',
  none: 'no usage record is near the end of the newest Codex sessions',
  error: 'the Codex session files could not be read',
};

type Reading = Extract<AccountsRead, { kind: 'read' }>;
type Found = Extract<Reading['claude'], { state: 'found' }>;
type CodexFound = Extract<Reading['codex'], { state: 'found' }>;
type Figures = Pick<ToolRow, 'five' | 'week' | 'models'>;

// ───────────────── cells ─────────────────

const instantOf = (ms: number | null | undefined): number | null => (typeof ms === 'number' && Number.isFinite(ms) ? ms : null);

/** THE one place that makes a number "left": `percentLeft` answers `null` for anything that is not a finite number. */
const cellOf = (window: { readonly usedPercent: number | null; readonly resetsAtMs: number | null } | undefined): Cell =>
  ({ left: percentLeft(window?.usedPercent), resetsAt: instantOf(window?.resetsAtMs) });

const NO_CELL: Cell = { left: null, resetsAt: null };

const versionText = (version: string | null): string => forList(version ?? NOT_KNOWN, NAME_MAX);

const planOf = (plan: string | null): string | null => (plan === null ? null : forList(plan, NAME_MAX));

const usageFailedSaid = (found: Found): string => {
  const { usage } = found;
  if (usage.state !== 'failed') {
    return USAGE_OFF_SAID.charAt(0).toUpperCase() + USAGE_OFF_SAID.slice(1);
  }
  const version = `Claude Code ${versionText(found.version)}`;
  const said: Readonly<Record<typeof usage.why, string>> = {
    format: `${version} printed no usage line this version understands`,
    'model-call': `the usage command of ${version} asked the model, so CoAI stopped asking this host`,
    timeout: `Claude Code did not answer within ${ACCOUNTS_SECONDS.usage} s`,
    error: `the usage command of ${version} ended with an error`,
  };

  return `Usage unavailable: ${said[usage.why]}`;
};

type Shell = Omit<ToolRow, 'version' | 'account' | 'span'>;

const base = (place: string, tool: ToolName, readAt: number): Shell => ({
  place, tool, found: true, accountKey: null, sharedAcross: null, five: NO_CELL, week: NO_CELL, models: [], others: [], session: null, note: null, readAt,
});

/** A row that only says why its tool could not be read. */
const sentenceRow = (shell: Shell, said: string): ToolRow => ({ ...shell, found: false, version: null, account: { text: said, plan: null }, span: said });

// ───────────────── Claude ─────────────────

/** The key of a KNOWN Claude account: the vendor and the whole account text. `null` when the account is not known. */
export const accountKeyOf = (found: Found): string | null => (found.email === '' ? null : JSON.stringify(['claude', 'account', plainValue(found.email), plainValue(found.org ?? '')]));

function claudeAccountText(found: Found): string {
  const email = forList(found.email, EMAIL_MAX);
  const org = forList(found.org ?? '', NAME_MAX);

  return email === '' ? ACCOUNT_NOT_KNOWN : org === '' ? email : `${email} · ${org}`;
}

const claudeFoundRow = (shell: Shell, found: Found): ToolRow =>
  ({ ...shell, version: versionText(found.version), account: { text: claudeAccountText(found), plan: planOf(found.plan) }, accountKey: accountKeyOf(found), span: null });

/** `no-jq` is the heading's: the host says `jq` is missing, and this tool has no row of its own. */
function claudeSideRow(shell: Shell, claude: Exclude<Reading['claude'], { state: 'absent' | 'found' }>): ToolRow | undefined {
  const said = CLAUDE_SIDE_SAID[claude.state === 'signed-out' ? 'signed-out' : claude.why];

  return said === undefined ? undefined : sentenceRow(shell, said);
}

function claudeRow(place: string, claude: Reading['claude'], readAt: number): ToolRow | undefined {
  if (claude.state === 'absent') {
    return undefined;
  }
  const shell = base(place, 'claude-code', readAt);

  return claude.state === 'found' ? claudeFoundRow(shell, claude) : claudeSideRow(shell, claude);
}

/** The figures of a Claude row from a reading that has them. */
function claudeFigures(found: Found): Figures | undefined {
  const { usage } = found;
  if (usage.state !== 'read') {
    return undefined;
  }
  const named = (name: '5h' | 'week'): Cell => cellOf(usage.windows.find((one) => one.name === name));

  return {
    five: named('5h'),
    week: named('week'),
    models: usage.windows.flatMap((one) => (typeof one.name === 'string' ? [] : [{ name: forList(one.name.model, NAME_MAX), ...cellOf(one) }])),
  };
}

interface Seen {
  readonly host: string;
  readonly read: Reading;
  readonly at: number;
}

/** The figures of one known account: the newest reading, of any place, that HAS them; else the sentence why not. */
interface Shared {
  readonly places: readonly string[];
  readonly figures: Figures | undefined;
  readonly span: string;
}

interface Member {
  readonly host: string;
  readonly found: Found;
  readonly at: number;
}

const newestFirst = (members: readonly Member[]): readonly Member[] => [...members].sort((one, other) => other.at - one.at);

function sharedAccount(members: readonly Member[]): Shared {
  const [withNumbers] = newestFirst(members.filter((one) => one.found.usage.state === 'read'));
  const [failed] = newestFirst(members.filter((one) => one.found.usage.state === 'failed'));
  const source = withNumbers ?? failed ?? members[0];

  return {
    places: [...new Set(members.map((one) => one.host))],
    figures: withNumbers === undefined ? undefined : claudeFigures(withNumbers.found),
    span: usageFailedSaid(source.found),
  };
}

function sharedOf(seen: readonly Seen[]): ReadonlyMap<string, Shared> {
  const members = seen.flatMap((one) => (one.read.claude.state === 'found' ? [{ host: one.host, found: one.read.claude, at: one.at }] : []));
  const keys = new Set(members.flatMap((one) => accountKeyOf(one.found) ?? []));

  return new Map([...keys].map((key) => [key, sharedAccount(members.filter((one) => accountKeyOf(one.found) === key))]));
}

/** An account that is not known has only its own place's reading. */
const withOwn = (row: ToolRow, found: Found): ToolRow => {
  const figures = claudeFigures(found);

  return { ...row, ...figures, span: figures === undefined ? usageFailedSaid(found) : null };
};

function withShared(row: ToolRow, found: Found, shared: Shared | undefined): ToolRow {
  if (shared === undefined) {
    return withOwn(row, found);
  }

  return { ...row, ...shared.figures, span: shared.figures === undefined ? shared.span : null, sharedAcross: shared.places.length > 1 ? shared.places.length : null };
}

// ───────────────── Codex and Antigravity ─────────────────

/** A Codex window is placed by its LENGTH: 300 is the 5-hour cell, 10 080 the weekly one, another is named in its cell. */
function codexWindows(row: ToolRow, codex: CodexFound): ToolRow {
  const { usage } = codex;
  if (usage.state === 'failed') {
    return { ...row, span: CODEX_FAILED_SAID[usage.why] };
  }
  const others = usage.windows.filter((one) => one.minutes !== FIVE_HOURS && one.minutes !== WEEK).map((one) => ({ name: windowLabel({ minutes: one.minutes }), ...cellOf(one) }));

  return {
    ...row,
    five: cellOf(usage.windows.find((one) => one.minutes === FIVE_HOURS)),
    week: cellOf(usage.windows.find((one) => one.minutes === WEEK)),
    others,
    session: usage.asOf === null ? null : usage.asOf.ms,
    note: usage.statMissing ? NO_STAT_SAID : null,
  };
}

const codexFoundRow = (shell: Shell, codex: CodexFound): ToolRow =>
  codexWindows({ ...shell, version: null, account: { text: CODEX_ACCOUNT_SAID, plan: planOf(codex.plan) }, span: null }, codex);

function codexRow(place: string, codex: Reading['codex'], readAt: number): ToolRow | undefined {
  if (codex.state === 'absent') {
    return undefined;
  }
  const shell = base(place, 'codex', readAt);
  if (codex.state === 'found') {
    return codexFoundRow(shell, codex);
  }

  return codex.why === 'credential-shape' ? sentenceRow(shell, NOT_SHOWN_SAID) : undefined;
}

function agyRow(place: string, agy: Reading['agy'], readAt: number): ToolRow | undefined {
  if (agy.state === 'absent') {
    return undefined;
  }
  const shell = base(place, 'agy', readAt);

  return agy.state === 'unreadable' ? sentenceRow(shell, NOT_SHOWN_SAID) : { ...shell, version: versionText(agy.version), account: { text: AGY_ACCOUNT_SAID, plan: null }, span: AGY_USAGE_SAID };
}

// ───────────────── a place ─────────────────

const bare = (cell: Cell): boolean => cell.left === null && cell.resetsAt === null;

const noFigure = (row: ToolRow): boolean => bare(row.five) && bare(row.week) && row.models.length === 0 && row.others.length === 0;

/** A row whose cells are empty always says why: a tool row with no figure and no reason does not leave this module. */
const reasoned = (row: ToolRow): ToolRow => (noFigure(row) && row.span === null ? { ...row, span: NO_FIGURE_SAID } : row);

function rowsOf(shown: string, seen: Seen, shared: ReadonlyMap<string, Shared>): readonly ToolRow[] {
  const { read, at } = seen;
  const claude = claudeRow(shown, read.claude, at);
  const withFigures = claude === undefined || read.claude.state !== 'found' ? claude : withShared(claude, read.claude, shared.get(claude.accountKey ?? ''));

  return [withFigures, codexRow(shown, read.codex, at), agyRow(shown, read.agy, at)].flatMap((one) => (one === undefined ? [] : [reasoned(one)]));
}

/** Claude's own sentence when it could not be read as an account. */
function claudeSaid(rows: readonly ToolRow[]): string | undefined {
  const claude = rows.find((one) => one.tool === 'claude-code');

  return claude !== undefined && !claude.found ? (claude.span ?? claude.account.text) : undefined;
}

/** The sentence why a place has no Claude account: Claude's own row, else its heading's when it was not read, else that there is none. */
const noClaudeOf = (head: Head, rows: readonly ToolRow[], olderAt: number | null): string =>
  claudeSaid(rows) ?? (head.kind === 'read' || olderAt !== null ? NO_CLAUDE_SAID : head.said);

const headOf = (kind: HeadKind, at: number | null, said: string, manager: ManagerWord | null = null): Head => ({ kind, at, said, manager });

const damagedSaid = (damaged: TableInput['damaged']): string | undefined =>
  (damaged === undefined ? undefined : damaged === 'file' ? HOSTS_SAID.rowDamagedFile : HOSTS_SAID.rowDamagedList);

const permissionSaid = (permission: Permission): string | undefined =>
  (permission === 'waiting' ? HOSTS_SAID.rowWaiting : permission === 'none' ? NO_PERMISSION_SAID : undefined);

/** Why a place is not read at all, by the door's own order: a refusal, damage, an unsaved change, no permission. `undefined`: it is read. */
function notReadSaid(input: TableInput, standing: HostStanding | undefined): string | undefined {
  if (standing?.answer === 'no') {
    return REFUSED_SAID;
  }

  return damagedSaid(input.damaged) ?? permissionSaid(input.permission);
}

const failureSaid = (failure: NonNullable<HostMemory['failure']>): string => {
  const sentence = forList(failure.sentence, SENTENCE_MAX);

  return sentence === '' ? 'the last read failed' : `the last read failed: ${sentence}`;
};

const unreadableHead = (host: string, unreadable: NonNullable<HostMemory['unreadable']>): Head =>
  headOf('failed', unreadable.atMs, `The AI accounts on “${forList(host, SENTENCE_MAX)}” could not be read: ${ACCOUNTS_UNREADABLE_SAID[unreadable.why]}.`);

function readHead(reading: NonNullable<HostMemory['reading']>): Head {
  const word = reading.read.jqMissing;

  return word === undefined ? headOf('read', reading.atMs, '') : headOf('jq-missing', reading.atMs, word === 'none' ? NO_MANAGER_SAID : JQ_SAID, word);
}

/** A place's heading from its memory. */
function memoryHead(host: string, memory: HostMemory): Head {
  if (memory.unreadable !== undefined) {
    return unreadableHead(host, memory.unreadable);
  }
  if (memory.failure !== undefined) {
    return headOf('failed', memory.failure.atMs, failureSaid(memory.failure));
  }

  return memory.reading === undefined ? headOf('never', null, NEVER_SAID) : readHead(memory.reading);
}

function wslBlock(input: TableInput): Block {
  const said = input.windows ? WSL_WINDOWS_SAID : WSL_ELSEWHERE_SAID;

  return { host: 'wsl', shown: 'wsl', head: headOf('wsl', null, said), rows: [], olderAt: null, line: null, noClaude: said, canRefuse: false, canReadAgain: false };
}

function closedBlock(host: string, shown: string, refused: boolean, said: string): Block {
  return { host, shown, head: headOf('not-allowed', null, said), rows: [], olderAt: null, line: null, noClaude: said, canRefuse: !refused, canReadAgain: refused };
}

/** The instant of the older read whose rows a failed place keeps. */
const olderAtOf = (head: Head, reading: HostMemory['reading']): number | null => (head.kind === 'failed' && reading !== undefined ? reading.atMs : null);

function readBlock(host: string, shown: string, memory: HostMemory, head: Head, shared: ReadonlyMap<string, Shared>): Block {
  const { reading } = memory;
  const rows = reading === undefined ? [] : rowsOf(host, { host, read: reading.read, at: reading.atMs }, shared);
  const olderAt = olderAtOf(head, reading);

  return { host, shown, head, rows, olderAt, line: head.kind === 'read' && rows.length === 0 ? NO_TOOL_SAID : null, noClaude: noClaudeOf(head, rows, olderAt), canRefuse: true, canReadAgain: false };
}

function readBlockFor(input: TableInput, host: string, shown: string, shared: ReadonlyMap<string, Shared>): Block {
  const memory = input.memory.get(host) ?? {};

  return readBlock(host, shown, memory, memoryHead(host, memory), shared);
}

function blockFor(input: TableInput, host: string, shared: ReadonlyMap<string, Shared>): Block {
  if (host === 'wsl') {
    return wslBlock(input);
  }
  const shown = forList(host, SENTENCE_MAX);
  const standing = input.standings.find((one) => one.host === host);
  const closed = notReadSaid(input, standing);

  return closed === undefined ? readBlockFor(input, host, shown, shared) : closedBlock(host, shown, standing?.answer === 'no', closed);
}

const roundPart = (round: RoundNow): RoundPart =>
  (round.kind === 'running'
    ? { kind: 'running', host: forList(round.host, SENTENCE_MAX), index: round.index, total: round.total }
    : round.kind === 'ended' ? { kind: 'ended', at: round.endedMs, read: round.read, asked: round.asked } : { kind: 'idle' });

const PERMISSION_OF = (permission: Permission): PermissionPart => {
  if (typeof permission === 'object') {
    return { kind: 'given', at: instantOf(Date.parse(permission.given)), utc: forList(permission.given, SENTENCE_MAX) };
  }

  return permission === 'none' ? { kind: 'none', said: PERMISSION_NONE_SAID } : { kind: 'waiting', said: PERMISSION_WAITING_SAID };
};

/** The model, from what the window holds. Deterministic: the same input gives the same model. */
export function tableModel(input: TableInput): TableModel {
  const hosts = [...new Set(input.hosts)].sort(byCodeUnit);
  const seen = hosts.flatMap((host) => {
    const reading = input.memory.get(host)?.reading;

    return reading === undefined ? [] : [{ host, read: reading.read, at: reading.atMs }];
  });
  const shared = sharedOf(seen);

  return {
    v: 1,
    toolbar: { stopped: input.stopped, round: roundPart(input.round) },
    permission: PERMISSION_OF(input.permission),
    blocks: hosts.map((host) => blockFor(input, host, shared)),
    empty: hosts.length === 0 ? NO_TERMINAL_SAID : null,
  };
}

/** One scope host's part of the model, or `undefined` for a host the model does not hold. */
export const blockOf = (model: TableModel, scopeHost: string): Block | undefined => model.blocks.find((one) => one.host === scopeHost);

/** The blocks of `next` that differ from `prev` (or are new): what a landed place may have changed, the shared figures of other places included. */
export function changedBlocks(prev: TableModel | undefined, next: TableModel): readonly Block[] {
  return next.blocks.filter((block) => {
    const before = prev === undefined ? undefined : blockOf(prev, block.host);

    return before === undefined || JSON.stringify(before) !== JSON.stringify(block);
  });
}
