import { credsSession } from './credsSession';
import type { AccountsRead, AccountsUnreadable } from './remoteAccountsReply';
import { accountsDigest } from './remoteAccountsScript';
import type { AccountsMode } from './remoteAccountsScript';
import type { HostStanding } from './remoteHosts';
import type { DoorAnswer } from './remotePermission';
import type { RemoteRun } from './remoteSsh';
import type { LiveTerminal } from './terminalSource';

/**
 * The round of the accounts read and its memory (E1.S3, contract C4 and C5). Pure: every effect is a seam, so a unit test drives a whole
 * round over fakes and a fake clock. The `vscode` seams are `remoteAccountsHost.ts`'s.
 *
 * <p>A round calls every distinct host of the live `Creds:` terminals that the door answers `yes` for, ONE at a time and each awaited. Every
 * host's FIRST call reads no Claude usage; the one host that first answers with an account that has no usage reading in this round is called
 * once more, for it, with the digest of the account lines of THAT reply (E3.S2). So an account's usage is read at most once in a round and a host is called at most twice;
 * the usage reply is JOINED to the first reading (`joined`), never put in its place. The round never asks a
 * person: the door (`may`, E2.S1's) has no ask seam. A door that answers `no-permission` or `stopped` ENDS the round; `no`, `damaged` and
 * `waiting` skip that host. The scope host `wsl` is never called.</p>
 *
 * <p>A reading never comes back after a Stop, a refusal or a Forget that arrived while its call was in flight: before a result is
 * published the round checks the generation, the Stop and the door again, in that order.</p>
 */

/** From the END of a round to the start of the next (O4). */
export const ROUND_GAP_MS = 600_000;
/** After activation, or after the first eligible terminal opened: the window restores its terminals first. */
export const FIRST_DELAY_MS = 30_000;
/** *Refresh now* and opening the list start no round less than this after the last one ended. */
export const REFRESH_FLOOR_MS = 60_000;
export const HOURLY_MS = 3_600_000;

export * from './remoteAccountsStateRow';

export type Trigger = 'timer' | 'refresh' | 'open';

export type UsagePolicy = { readonly kind: 'every-round' } | { readonly kind: 'hourly' } | { readonly kind: 'on-press' } | { readonly kind: 'off' };

/**
 * The ONE constant of the policy (E3.S5, spec § R2.9): the usage of each account is read in every round, by the one second call of the first place that names it.
 * Set from the recorded facts: W7 passed (a usage read started nothing that is not Claude Code's own) and W6 selected the row "Claude Code keeps a fixed number of
 * backups → every round" (five backups, the older ones seen deleted). E1 shipped `off` until W6 and W7 were recorded.
 */
export const USAGE_POLICY: UsagePolicy = { kind: 'every-round' };

/**
 * The scope host that is never called, in any window (E2.S4): `Creds: wsl / …` names a login session of a WSL distro, which is not a host that
 * `ssh` reaches. Its block says why (E2S2-T12); the round calls nothing for it.
 */
export const WSL_SCOPE = 'wsl';

export type Reading = Extract<AccountsRead, { kind: 'read' }>;
type Unreadable = Extract<AccountsRead, { kind: 'unreadable' }>;
/** What a first call can come to: the parser answers `claude-only` for the usage mode alone. */
export type FirstRead = Reading | Unreadable;
type Found = Extract<Reading['claude'], { state: 'found' }>;
/** What the list learns from a door that did not say `yes`: the kinds of its rows. `full` is only the old list's, no door answers it. */
export type NotRead = 'refused' | 'not-asked' | 'full' | 'damaged' | 'unwritten';

/** The door's answers that are not `yes`, as the kind the memory keeps. */
const HELD_AS: Readonly<Record<Exclude<DoorAnswer, 'yes'>, NotRead>> = {
  no: 'refused', damaged: 'damaged', waiting: 'unwritten', 'no-permission': 'not-asked', stopped: 'unwritten',
};

/** What the round keeps of one host, in memory only. A field that is not there is `undefined`: nothing here is written to disk. */
export interface HostMemory {
  readonly reading?: { readonly read: Reading; readonly atMs: number } | undefined;
  /** `path`: only for `unsafe` (§ R2.6): the path that failed the check as UID 0, or `null` when it is not shown. */
  readonly unreadable?: { readonly why: Unreadable['why']; readonly path?: string | null; readonly atMs: number } | undefined;
  readonly failure?: { readonly sentence: string; readonly atMs: number } | undefined;
  /** What the door answered when it was not `read`: the list learns *not asked yet*, *full* and *damaged* from here. */
  readonly admission?: NotRead | undefined;
  readonly claudeAccount?: string | undefined;
  /** When this account's usage was last READ through this host. */
  readonly usageAtMs?: number | undefined;
  /** The Claude Code version at which the usage read went wrong; `''` stands for a version that was not known. */
  readonly usageBlockedAt?: string | undefined;
  /** Why this place's last usage call did not give figures; cleared by the place's next first reply (E3.S2, § R2.8). */
  readonly usageMiss?: UsageMiss | undefined;
}

/** What the table says in the usage cells when the usage call of the round did not come to figures for the place. */
export type UsageMiss =
  | { readonly why: 'call-failed'; readonly sentence: string }
  | { readonly why: 'unreadable'; readonly word: AccountsUnreadable }
  | { readonly why: 'account-changed' }
  /** The first reply named no account lines, so there was no digest and no usage call. */
  | { readonly why: 'no-account-lines' };

/** Immutable, by host: every update returns a new map. */
export type Memory = ReadonlyMap<string, HostMemory>;

// ───────────────── which hosts, in which order ─────────────────

/** The distinct hosts of the live terminals whose name gives a session, sorted by name (code-unit order). */
export function accountsHosts(live: readonly LiveTerminal[]): readonly string[] {
  const hosts = live.flatMap((one) => {
    const name = credsSession(one.name);

    return !one.exited && name.kind === 'session' ? [name.host] : [];
  });

  return [...new Set(hosts)].sort((one, other) => (one < other ? -1 : one > other ? 1 : 0));
}

/** The hosts a round may call: `accountsHosts` without the scope host `wsl`. */
export const callableHosts = (live: readonly LiveTerminal[]): readonly string[] => accountsHosts(live).filter((host) => host !== WSL_SCOPE);

/** The sorted hosts, rotated to start at `round % hosts.length`: no host is always last. */
export function roundOrder(hosts: readonly string[], round: number): readonly string[] {
  if (hosts.length === 0) {
    return [];
  }
  const start = ((round % hosts.length) + hosts.length) % hosts.length;

  return [...hosts.slice(start), ...hosts.slice(0, start)];
}

// ───────────────── the account, the mode and the usage policy ─────────────────

const SEPARATOR = '\u0000';

/**
 * The key of a Claude account. A KNOWN account (`email` not empty) is `claude`, `account`, its email and its organization
 * (`null` as the empty text): two hosts share a key only when the whole account text is equal. An account that is NOT known is `claude`,
 * `host` and the host's name, a key of that host alone, so two unknown accounts never merge.
 */
export function claudeAccountKey(found: Found, host: string): string {
  return found.email === '' ? ['claude', 'host', host].join(SEPARATOR) : ['claude', 'account', found.email, found.org ?? ''].join(SEPARATOR);
}

/** The mode of a call. NOTHING else chooses it: no memory and no host, so no remembered account can be wrong. */
export const modeFor = (call: 'first' | 'usage'): AccountsMode => (call === 'first' ? 'no-claude-usage' : 'claude-usage');

const DUE: Readonly<Record<UsagePolicy['kind'], (lastUsageAtMs: number | undefined, now: number, trigger: Trigger) => boolean>> = {
  'every-round': () => true,
  hourly: (last, now) => last === undefined || now - last >= HOURLY_MS,
  'on-press': (_last, _now, trigger) => trigger === 'refresh',
  off: () => false,
};

/** Whether the policy allows this account's usage to be read now. `off` never does, for any trigger. */
export const due = (policy: UsagePolicy, lastUsageAtMs: number | undefined, now: number, trigger: Trigger): boolean => DUE[policy.kind](lastUsageAtMs, now, trigger);

const foundOf = (read: AccountsRead): Found | undefined => (read.kind === 'read' && read.claude.state === 'found' ? read.claude : undefined);

const versionOf = (found: Found): string => found.version ?? '';

/** When this account's usage was last read, through any host that holds it. */
function lastUsageOf(memory: Memory, key: string): number | undefined {
  const times = [...memory.values()].flatMap((one) => (one.claudeAccount === key && one.usageAtMs !== undefined ? [one.usageAtMs] : []));

  return times.length === 0 ? undefined : Math.max(...times);
}

/**
 * Asked after a host's FIRST call, never after its second. True only when the reply is `found` with an account whose usage was not READ in
 * this round, the policy says it is due and the host is not blocked at the version of THIS reply. With `off` it is never true.
 */
export function needsSecondCall(read: AccountsRead, roundUsage: ReadonlySet<string>, memory: Memory, host: string, policy: UsagePolicy, now: number, trigger: Trigger): boolean {
  const found = foundOf(read);
  if (found === undefined) {
    return false;
  }
  const key = claudeAccountKey(found, host);

  return !roundUsage.has(key) && due(policy, lastUsageOf(memory, key), now, trigger) && !blockedAt(memory, host, found);
}

/** The host is blocked at the Claude Code version of THIS reply. */
const blockedAt = (memory: Memory, host: string, found: Found): boolean => memory.get(host)?.usageBlockedAt === versionOf(found);

const blocks = (usage: Found['usage']): boolean => usage.state === 'failed' && (usage.why === 'model-call' || usage.why === 'format');

/** The version at which a reply says the usage command went wrong (it made a model call, or printed no known line); else nothing. */
export function blockedAfter(read: AccountsRead): string | undefined {
  const found = foundOf(read);

  return found !== undefined && blocks(found.usage) ? versionOf(found) : undefined;
}

// ───────────────── the round ─────────────────

/** A call: the real one runs `ssh`; a test replaces it. The usage mode holds the digest of the first reply's account lines (§ R2.2); the first mode holds none. */
export type AccountsCall = (host: string, mode: AccountsMode, digest?: string) => Promise<RemoteRun>;

/** What the round needs of the world, and no ask: a round never asks anybody. */
export interface RoundSeams {
  readonly live: () => readonly LiveTerminal[];
  /** The Stop, read from the file and the state row before EVERY call. */
  readonly stopped: () => Promise<boolean>;
  /** C2's door, `sessions.may('round', host)`: asked before EVERY call and once more before a result is published. */
  readonly may: (host: string) => Promise<DoorAnswer>;
  readonly call: AccountsCall;
  /** Reads the reply against the mode that was SENT; the host binds the clock. */
  readonly parse: (run: { readonly code: number; readonly stdout: string }, mode: AccountsMode) => AccountsRead;
  readonly now: () => number;
  /** After each host whose memory changed, so the page posts that block. `memory`: the round's memory with this host's news; `host`: the place. */
  readonly landed: (memory: Memory, host: string) => void;
  /** Bumped on a Stop, on a refusal and on a Forget. */
  readonly generation: () => number;
  /** One fixed log line for a result that was dropped. */
  readonly dropped: (host: string) => void;
}

export interface RoundResult {
  readonly memory: Memory;
  readonly endedMs: number;
  /** The places this round called, and the ones among them that answered with a reading. */
  readonly asked: number;
  readonly read: number;
  /** A Stop ended it: the memory of such a round is never stored (`memoryAfter`). */
  readonly abandoned: boolean;
}

/** The memory the host holds after a round: an abandoned round's memory is NOT stored, `stop()` cleared the earlier one. */
export const memoryAfter = (current: Memory, result: RoundResult): Memory => (result.abandoned ? current : result.memory);

interface RoundState {
  readonly memory: Memory;
  /** The account keys whose usage was READ in this round. */
  readonly roundUsage: ReadonlySet<string>;
  /** The places called so far, and the ones that answered with a reading. */
  readonly asked: ReadonlySet<string>;
  readonly read: ReadonlySet<string>;
}

interface Context {
  readonly seams: RoundSeams;
  readonly trigger: Trigger;
  readonly policy: UsagePolicy;
  /** The generation when the round started. */
  readonly token: number;
}

type Step =
  /** `digest`: of the account lines of THIS reply (a first call), held for the one step that makes the usage call and kept nowhere else. */
  | { readonly kind: 'published'; readonly state: RoundState; readonly read: AccountsRead | undefined; readonly digest?: string | undefined }
  | { readonly kind: 'skipped'; readonly state: RoundState }
  | { readonly kind: 'abandoned'; readonly state: RoundState };

const put = (memory: Memory, host: string, next: HostMemory): Memory => new Map<string, HostMemory>([...memory, [host, next]]);

/** The memory of the hosts that are still in the list: a host whose last terminal closed is dropped. */
export const liveOnly = (memory: Memory, hosts: readonly string[]): Memory => new Map([...memory].filter(([host]) => hosts.includes(host)));

const hostLive = (seams: RoundSeams, host: string): boolean => accountsHosts(seams.live()).includes(host);

/** A refusal drops the host's reading; the other kinds keep it. The kind is what the list reads. */
const withAdmission = (state: RoundState, host: string, kind: NotRead): RoundState => {
  const prev = state.memory.get(host) ?? {};

  return { ...state, memory: put(state.memory, host, { ...prev, admission: kind, reading: kind === 'refused' ? undefined : prev.reading }) };
};

/** The door said `yes`: the kept kind goes, and the place counts as asked. */
const admitted = (state: RoundState, host: string): RoundState => ({
  ...state,
  memory: put(state.memory, host, { ...(state.memory.get(host) ?? {}), admission: undefined }),
  asked: new Set([...state.asked, host]),
});

/** The key whose usage this reply READ, if it did. */
function usageKeyOf(read: FirstRead | undefined, host: string): string | undefined {
  const found = read === undefined ? undefined : foundOf(read);

  return found !== undefined && found.usage.state === 'read' ? claudeAccountKey(found, host) : undefined;
}

/** A usage time belongs to the account it was read for: a host that changed its account starts with none. */
function usageTimeOf(prev: HostMemory, read: FirstRead, host: string, nowMs: number): number | undefined {
  const found = foundOf(read);
  if (usageKeyOf(read, host) !== undefined) {
    return nowMs;
  }

  return found !== undefined && claudeAccountKey(found, host) === prev.claudeAccount ? prev.usageAtMs : undefined;
}

/** A block stays only at the version it was made at: a reading with another version unblocks the host. */
function blockOf(prev: HostMemory, read: FirstRead): string | undefined {
  const found = foundOf(read);

  return blockedAfter(read) ?? (found !== undefined && prev.usageBlockedAt === versionOf(found) ? prev.usageBlockedAt : undefined);
}

/** What a host's memory is after a good or unreadable reply. */
function readMemory(prev: HostMemory, host: string, read: FirstRead, nowMs: number): HostMemory {
  if (read.kind === 'unreadable') {
    return { ...prev, unreadable: { why: read.why, ...('path' in read ? { path: read.path } : {}), atMs: nowMs }, failure: undefined, usageMiss: undefined };
  }
  const found = foundOf(read);

  return {
    ...prev, reading: { read, atMs: nowMs }, unreadable: undefined, failure: undefined, usageMiss: undefined,
    claudeAccount: found === undefined ? undefined : claudeAccountKey(found, host), usageAtMs: usageTimeOf(prev, read, host, nowMs), usageBlockedAt: blockOf(prev, read),
  };
}

/** A refused run is a failure that keeps the older reading. */
const failedMemory = (prev: HostMemory, run: RemoteRun, nowMs: number): HostMemory =>
  ({ ...prev, failure: { sentence: run.kind === 'refused' ? run.refusal : '', atMs: nowMs }, unreadable: undefined });

/** The places that answered with a reading, counting this one when its reply is one. */
const readPlaces = (state: RoundState, host: string, read: FirstRead | undefined): ReadonlySet<string> => (read?.kind === 'read' ? new Set([...state.read, host]) : state.read);

/** The result of a call, published: a refused run is a failure that keeps the older reading. */
function publishedState(state: RoundState, host: string, run: RemoteRun, read: FirstRead | undefined, nowMs: number): RoundState {
  const prev = state.memory.get(host) ?? {};
  const next = read === undefined ? failedMemory(prev, run, nowMs) : readMemory(prev, host, read, nowMs);
  const key = usageKeyOf(read, host);

  return { ...state, memory: put(state.memory, host, next), roundUsage: key === undefined ? state.roundUsage : new Set([...state.roundUsage, key]), read: readPlaces(state, host, read) };
}

type Gate = { readonly kind: 'ok' } | { readonly kind: 'abandon' } | { readonly kind: 'refused'; readonly admission: NotRead };

/**
 * Before a result is published, IN THIS ORDER: (a) the generation is the round's; (b) the Stop, read again; (c) the door again. A host that was
 * forgotten in flight fails (a) first, so (c) never runs for it and cannot write an automatic row where a person just took one away.
 */
async function checked(context: Context, host: string): Promise<Gate> {
  if (moved(context) || (await context.seams.stopped()) || moved(context)) {
    return { kind: 'abandon' };
  }

  return afterDoor(context, await context.seams.may(host));
}

/** A Stop, a refusal or a Forget since the round started: the generation is not the round's. */
const moved = (context: Context): boolean => context.seams.generation() !== context.token;

/** The answers that END a round: the permission is gone, or the round is stopped. The other answers that are not `yes` skip the host. */
const endsTheRound = (door: DoorAnswer): door is 'no-permission' | 'stopped' => door === 'no-permission' || door === 'stopped';

/**
 * The door's answer — unless the generation moved while it was awaited: then nothing is published, whatever the door said. After a `yes`
 * the Stop is read again (a Stop saved by another window leaves the generation as it was): a Stop then abandons the round, as (b) does.
 * A withdrawal that no event told this window of reaches the round here, as `no-permission`: it abandons the round too.
 */
const afterDoor = async (context: Context, door: DoorAnswer): Promise<Gate> => {
  if (moved(context) || endsTheRound(door)) {
    return { kind: 'abandon' };
  }

  return door === 'yes' ? readAfterStop(context) : { kind: 'refused', admission: HELD_AS[door] };
};

/** A Stop saved by another window while the door was awaited does not move the generation: the Stop is read once more, as failure of (b). */
const readAfterStop = async (context: Context): Promise<Gate> => ((await ended(context)) ? { kind: 'abandon' } : { kind: 'ok' });

/** A result that failed a check is dropped and logged once: a Stop ends the round, a refusal keeps the kind the door answered. */
function droppedStep(seams: RoundSeams, state: RoundState, host: string, gate: Exclude<Gate, { readonly kind: 'ok' }>): Step {
  seams.dropped(host);

  return gate.kind === 'abandon' ? { kind: 'abandoned', state } : { kind: 'skipped', state: withAdmission(state, host, gate.admission) };
}

/** A host whose terminal closed in flight: its result is dropped; a Stop that came meanwhile still ends the round. */
const closedStep = (context: Context, state: RoundState): Step => ({ kind: moved(context) ? 'abandoned' : 'skipped', state });

/** What the usage call came to, for `joined`: the runner's sentence, or what the parser made of the reply. */
export type UsageCame = { readonly kind: 'refused'; readonly sentence: string } | { readonly kind: 'parsed'; readonly read: AccountsRead };

/** The place's reading after the usage call, its miss, and the account key whose usage was READ (only the row that says yes). */
export interface Joined {
  readonly reading: Reading;
  /** `false`: the reading is the first one, whole. */
  readonly changed: boolean;
  readonly usageMiss: UsageMiss | undefined;
  readonly counted: string | undefined;
}

const whole = (first: Reading, usageMiss: UsageMiss): Joined => ({ reading: first, changed: false, usageMiss, counted: undefined });

/** The Claude part of the second reply with its usage set aside: the account is the second's, its figures are nobody's. */
const withoutFigures = (found: Found): Found => ({ ...found, usage: { state: 'skipped' } });

/** The second reply named a Claude that is not `found`: the first reading takes that state, and nothing is counted. */
const takenState = (first: Reading, claude: Reading['claude']): Joined => ({ reading: { ...first, claude }, changed: true, usageMiss: undefined, counted: undefined });

/** The second reply names an account: the same key with the usage the script read counts; the script's own `skipped` or another key counts nothing and shows no figures. */
function joinedAccount(first: Reading, firstKey: string, claude: Found, place: string): Joined {
  const sameKey = claudeAccountKey(claude, place) === firstKey;
  if (!sameKey || claude.usage.state === 'skipped') {
    return { reading: { ...first, claude: withoutFigures(claude) }, changed: true, usageMiss: { why: 'account-changed' }, counted: undefined };
  }

  return { reading: { ...first, claude }, changed: true, usageMiss: undefined, counted: claude.usage.state === 'read' ? firstKey : undefined };
}

/** What the parser made of the usage reply, as a join: only `claude-only` joins; an unreadable reply or the arm `read` leaves the first reading whole. */
function joinedParsed(first: Reading, firstKey: string, read: AccountsRead, place: string): Joined {
  if (read.kind === 'unreadable') {
    return whole(first, { why: 'unreadable', word: read.why });
  }
  if (read.kind === 'read') {
    return whole(first, { why: 'unreadable', word: 'shape' });
  }

  return read.claude.state === 'found' ? joinedAccount(first, firstKey, read.claude, place) : takenState(first, read.claude);
}

/**
 * THE join (§ R2.8, D10): the usage reply is joined to the first reading and never replaces it. Usage counts for an account only when the second reply names the SAME
 * account key as the first (`firstKey`), and then only for the figures it READ. A call that failed or could not be read leaves the first reading whole. Another account,
 * or the script's own `skipped account-changed`, takes the second's account with its usage `skipped` and counts nothing: the account the call was for is read by its next place.
 * Pure; the place's name only makes the key of an account that is not known.
 */
export function joined(first: Reading, firstKey: string, second: UsageCame, place: string): Joined {
  return second.kind === 'refused' ? whole(first, { why: 'call-failed', sentence: second.sentence }) : joinedParsed(first, firstKey, second.read, place);
}

/** What a first reply is, for the memory: a usage-mode arm in a first reply is a fault of the parser's arrangement, so it is said as `shape`. */
const firstOf = (read: AccountsRead): FirstRead => (read.kind === 'claude-only' ? { kind: 'unreadable', why: 'shape' } : read);

/** The memory and the usage set after a usage call: the reading is the joined one, the miss is kept for the cell, the key is counted when it was READ. */
function joinedState(state: RoundState, host: string, took: Joined, nowMs: number): RoundState {
  const prev = state.memory.get(host) ?? {};
  const base = took.changed ? readMemory(prev, host, took.reading, nowMs) : prev;
  const next: HostMemory = { ...base, usageMiss: took.usageMiss };

  return { ...state, memory: put(state.memory, host, next), roundUsage: took.counted === undefined ? state.roundUsage : new Set([...state.roundUsage, took.counted]) };
}

/** The usage call is back: the closed host and the checks are those of any result; then the reply is joined to the first reading. */
async function settledUsage(context: Context, state: RoundState, host: string, first: Reading, run: RemoteRun): Promise<Step> {
  const { seams } = context;
  if (!hostLive(seams, host)) {
    return closedStep(context, state);
  }
  const gate = await checked(context, host);
  if (gate.kind !== 'ok') {
    return droppedStep(seams, state, host, gate);
  }
  const came: UsageCame = run.kind === 'answered' ? { kind: 'parsed', read: seams.parse(run, 'claude-usage') } : { kind: 'refused', sentence: run.refusal };
  const next = joinedState(state, host, joined(first, claudeKeyOf(first, host), came, host), seams.now());
  seams.landed(next.memory, host);

  return { kind: 'published', state: next, read: undefined };
}

/** The key of the account a first reading names; the caller has shown it is `found`. */
const claudeKeyOf = (first: Reading, host: string): string => claudeAccountKey(foundOf(first) as Found, host);

/** The call is back: a closed host is dropped; a result that fails a check is dropped, once logged; else it is published. */
async function settled(context: Context, state: RoundState, host: string, run: RemoteRun): Promise<Step> {
  const { seams } = context;
  if (!hostLive(seams, host)) {
    return closedStep(context, state);
  }
  const gate = await checked(context, host);
  if (gate.kind !== 'ok') {
    return droppedStep(seams, state, host, gate);
  }
  const read = answeredFirst(seams, run);
  const next = publishedState(state, host, run, read, seams.now());
  seams.landed(next.memory, host);

  return { kind: 'published', state: next, read, digest: digestOfRun(run) };
}

/** A first call's reply, parsed in the first mode; nothing for a call that did not answer. */
const answeredFirst = (seams: RoundSeams, run: RemoteRun): FirstRead | undefined => (run.kind === 'answered' ? firstOf(seams.parse(run, 'no-claude-usage')) : undefined);

/** The digest of the account lines of a first reply's stdout, held for the one step that makes the usage call. */
const digestOfRun = (run: RemoteRun): string | undefined => (run.kind === 'answered' ? accountsDigest(run.stdout) : undefined);

/** Whether the round is over: the generation moved, or a Stop is saved (read again, so a Forget that came while it was read is seen too). */
const ended = async (context: Context): Promise<boolean> => moved(context) || (await context.seams.stopped()) || moved(context);

/** What ends a step before its door, and again before its call: a Stop or a moved generation ends the round; a host with no terminal is skipped. */
async function beforeCall(context: Context, state: RoundState, host: string): Promise<Step | undefined> {
  if (await ended(context)) {
    return { kind: 'abandoned', state };
  }

  return hostLive(context.seams, host) ? undefined : { kind: 'skipped', state };
}

/** The door did not say `yes`: the answers that end the round end it; the others skip the host, which keeps the kind for the list. */
function notYes(seams: RoundSeams, state: RoundState, host: string, door: Exclude<DoorAnswer, 'yes'>): Step {
  if (endsTheRound(door)) {
    return { kind: 'abandoned', state };
  }
  const skipped = withAdmission(state, host, HELD_AS[door]);
  seams.landed(skipped.memory, host);

  return { kind: 'skipped', state: skipped };
}

/** What a call needs before it is made: the Stop, the terminal, the door, and the Stop, the generation and the terminal again right before the call. */
async function beforeSpawn(context: Context, state: RoundState, host: string): Promise<Step | undefined> {
  const { seams } = context;
  const early = await beforeCall(context, state, host);
  if (early !== undefined) {
    return early;
  }
  const door = await seams.may(host);
  if (door !== 'yes') {
    return notYes(seams, state, host, door);
  }

  // the door took time: the Stop, the generation and the terminal are looked at again, right before the call
  return beforeCall(context, state, host);
}

/** A host's first call: the checks, the call, then `settled`. */
async function firstStep(context: Context, state: RoundState, host: string): Promise<Step> {
  const early = await beforeSpawn(context, state, host);
  if (early !== undefined) {
    return early;
  }
  const mode = modeFor('first');

  return settled(context, admitted(state, host), host, await context.seams.call(host, mode));
}

/** A host's usage call, with the digest of THAT host's first reply: the same checks, the call, then the join. */
async function usageStep(context: Context, state: RoundState, host: string, first: Reading, digest: string): Promise<Step> {
  const early = await beforeSpawn(context, state, host);
  if (early !== undefined) {
    return early;
  }
  const mode = modeFor('usage');

  return settledUsage(context, admitted(state, host), host, first, await context.seams.call(host, mode, digest));
}

/** The first reading of a place with no account lines in its reply: nothing can be asked for, and the cell says so. */
const withoutLines = (state: RoundState, host: string): RoundState => ({ ...state, memory: put(state.memory, host, { ...(state.memory.get(host) ?? {}), usageMiss: { why: 'no-account-lines' } }) });

/** The second call, when the first reading asks for one: at most ONE usage call for a place in a round, and never a third call. */
async function secondStep(context: Context, first: Extract<Step, { kind: 'published' }>, reading: Reading, host: string): Promise<Step> {
  const { seams, policy, trigger } = context;
  if (!needsSecondCall(reading, first.state.roundUsage, first.state.memory, host, policy, seams.now(), trigger)) {
    return first;
  }

  return first.digest === undefined ? { ...first, state: withoutLines(first.state, host) } : usageStep(context, first.state, host, reading, first.digest);
}

/** A host's first call, and the one extra call for the account that reply names, when it is due. */
async function hostRound(context: Context, state: RoundState, host: string): Promise<Step> {
  const first = await firstStep(context, state, host);

  return first.kind === 'published' && first.read?.kind === 'read' ? secondStep(context, first, first.read, host) : first;
}

/** The round's end. A generation that moved by now abandons it: its memory is never stored over what a Stop cleared. A closed host's older entry goes. */
const finished = (context: Context, state: RoundState, abandoned: boolean): RoundResult => ({
  memory: liveOnly(state.memory, accountsHosts(context.seams.live())), endedMs: context.seams.now(), asked: state.asked.size, read: state.read.size,
  abandoned: abandoned || moved(context),
});

/**
 * One round: for each host of the rotated order, in turn and AWAITED, the steps of `hostRound`. `policy` is `USAGE_POLICY` unless a test
 * hands one in: it is a constant of the build, not a setting.
 */
export async function runRound(seams: RoundSeams, memory: Memory, round: number, trigger: Trigger, policy: UsagePolicy = USAGE_POLICY): Promise<RoundResult> {
  const context: Context = { seams, trigger, policy, token: seams.generation() };
  const hosts = roundOrder(callableHosts(seams.live()), round);
  let state: RoundState = { memory: liveOnly(memory, hosts), roundUsage: new Set(), asked: new Set(), read: new Set() };
  for (const host of hosts) {
    const step = await hostRound(context, state, host);
    state = step.state;
    if (step.kind === 'abandoned') {
      return finished(context, state, true);
    }
  }

  return finished(context, state, false);
}

// ───────────────── the timer, as decisions ─────────────────

export type ArmDecision = 'arm-first' | 'clear' | 'keep';

const idle = (input: { readonly armed: boolean; readonly running: boolean }): boolean => !input.armed && !input.running;

/**
 * What the timer does when a terminal opens or closes or the window starts. `eligible`: a live `Creds:` terminal of a host that is not
 * refused and is called (never `wsl`). `permitted`: the permission is in force; with none, no timer is armed. Whether a host is CONTACTED is the
 * door's answer alone.
 */
export function armFor(input: { readonly eligible: boolean; readonly permitted: boolean; readonly armed: boolean; readonly stopped: boolean; readonly running: boolean }): ArmDecision {
  if (!armable(input)) {
    return 'clear';
  }

  return idle(input) ? 'arm-first' : 'keep';
}

/** A timer may run only for an eligible terminal, with the permission in force and the round not stopped. */
const armable = (input: { readonly eligible: boolean; readonly permitted: boolean; readonly stopped: boolean }): boolean => input.eligible && input.permitted && !input.stopped;

const refusedIn = (standings: readonly HostStanding[], host: string): boolean => standings.some((one) => one.host === host && one.answer === 'no');

/**
 * A host that was newly refused keeps an entry that says so — its older reading goes, the kind the door will answer is kept — instead of being
 * deleted: the entry is what the list reads, and a round that ends later cannot bring the older reading back.
 */
export const withRefusals = (memory: Memory, refused: ReadonlySet<string>): Memory =>
  new Map([...memory].map(([host, one]): [string, HostMemory] => [host, refused.has(host) ? { ...one, reading: undefined, admission: 'refused' } : one]));

/**
 * One round, and what follows it, in a window. A round that failed — a seam rejected — is logged once through `failed`, and the timer is still set
 * (`schedule` is given nothing): rounds never stop silently. A `schedule` that fails too falls back to `fallback`, which sets the plain gap.
 */
export async function settleRound<T>(work: () => Promise<T>, schedule: (done: T | undefined) => Promise<void>, fallback: () => void, failed: () => void): Promise<T | undefined> {
  let done: T | undefined;
  try {
    done = await work();
  } catch {
    failed();
  }
  try {
    await schedule(done);
  } catch {
    failed();
    fallback();
  }

  return done;
}

/** The hosts the list says "no" to. */
export const refusedHosts = (standings: readonly HostStanding[]): ReadonlySet<string> => new Set(standings.filter((one) => one.answer === 'no').map((one) => one.host));

/**
 * Whether the list's change signal shows a host that is NEWLY refused, or a standing that is gone (a Forget): a result in flight is then to be dropped,
 * so the host bumps its generation. A new row — the automatic "yes" this very round wrote — bumps nothing.
 */
export function standingsBump(known: ReadonlySet<string>, refused: ReadonlySet<string>, standings: readonly HostStanding[]): boolean {
  const forgotten = [...known].some((host) => !standings.some((one) => one.host === host));

  return forgotten || standings.some((one) => one.answer === 'no' && !refused.has(one.host));
}

/** The hosts of live terminals whose standing is not a "no". A host with no row is eligible: the round allows it by itself. */
export const eligibleOf = (hosts: readonly string[], standings: readonly HostStanding[]): readonly string[] => hosts.filter((host) => !refusedIn(standings, host));

export type NextRound = { readonly kind: 'at'; readonly atMs: number } | { readonly kind: 'clear' };

export type TimerAction = { readonly kind: 'none' } | { readonly kind: 'clear' } | { readonly kind: 'start'; readonly ms: number };

const afterRound = (next: NextRound, nowMs: number): TimerAction => (next.kind === 'at' ? { kind: 'start', ms: Math.max(0, next.atMs - nowMs) } : { kind: 'clear' });

const ARMED: Readonly<Record<ArmDecision, TimerAction>> = { 'arm-first': { kind: 'start', ms: FIRST_DELAY_MS }, clear: { kind: 'clear' }, keep: { kind: 'none' } };

/**
 * The first test-mode lock, and the ONE road from a decision to a timer: in a test run no timer is ever started, whatever `armFor` or
 * `nextAfterRound` answered. A decision after a round is an absolute time; the wait is counted from `nowMs`.
 */
export function timerFor(decision: ArmDecision | NextRound, test: boolean, nowMs = 0): TimerAction {
  return test ? { kind: 'none' } : actionOf(decision, nowMs);
}

/** What a read of the permission means for the window, given what it saw before. The first read of a window (`undefined`) is no edge. */
export type PermissionEdge = 'allowed' | 'withdrawn' | 'same';

export function permissionEdge(was: boolean | undefined, now: boolean): PermissionEdge {
  if (was === undefined || was === now) {
    return 'same';
  }

  return now ? 'allowed' : 'withdrawn';
}

/** An *Allow* starts a round at once — unless one runs, the round is stopped, or no terminal is eligible. */
export const startsAfterAllow = (input: { readonly running: boolean; readonly stopped: boolean; readonly eligible: boolean }): boolean =>
  !input.running && !input.stopped && input.eligible;

const actionOf = (decision: ArmDecision | NextRound, nowMs: number): TimerAction => (typeof decision === 'string' ? ARMED[decision] : afterRound(decision, nowMs));

/** After a round: the next one `ROUND_GAP_MS` from the END of this one, or no timer (a Stop, no eligible terminal, or no permission). */
export const nextAfterRound = (input: { readonly stopped: boolean; readonly eligible: boolean; readonly permitted: boolean; readonly endedMs: number }): NextRound =>
  (input.stopped || !input.eligible || !input.permitted ? { kind: 'clear' } : { kind: 'at', atMs: input.endedMs + ROUND_GAP_MS });

const insideFloor = (input: { readonly lastEndMs: number | undefined; readonly now: number }): boolean => input.lastEndMs !== undefined && input.now - input.lastEndMs < REFRESH_FLOOR_MS;

/** *Refresh now*: a round at once, unless one is running or the last one ended less than a minute ago. */
export function refreshAnswer(input: { readonly running: boolean; readonly lastEndMs: number | undefined; readonly now: number }): 'start' | 'busy' | 'floor' {
  if (input.running) {
    return 'busy';
  }

  return insideFloor(input) ? 'floor' : 'start';
}

/** Opening the list starts a round only when a host that is not refused has nothing yet, and not inside the floor. */
export const openStarts = (input: { readonly running: boolean; readonly lastEndMs: number | undefined; readonly now: number; readonly unreadHost: boolean }): boolean =>
  input.unreadHost && refreshAnswer(input) === 'start';

const hasAnswer = (one: HostMemory | undefined): boolean => [one?.reading, one?.failure].some((held) => held !== undefined);

/** A host that is not refused and has neither a reading nor a failure. */
export const unreadHost = (hosts: readonly string[], memory: Memory, standings: readonly HostStanding[]): boolean =>
  eligibleOf(hosts, standings).some((host) => !hasAnswer(memory.get(host)));

// ───────────────── the log gate ─────────────────

/** The one fixed line when a host answers again after a failure was written. */
export const answeredLine = (host: string): string => `ConnectOtherAIs: accounts read host=${host} answered again`;

/** The one fixed line for a result that was dropped. */
export const droppedLine = (host: string): string => `ConnectOtherAIs: accounts read host=${host} dropped: stopped, refused or forgotten while it ran`;

/**
 * A host that is down for a day is two lines, not 144: a host's line is written only when it differs from the last one written for it,
 * and a host that answers again after a written line says so once.
 */
export function logGate(write: (line: string) => void): { readonly seamFor: (host: string) => (line: string) => void; readonly answered: (host: string) => void } {
  const last = new Map<string, string>();

  return {
    seamFor: (host) => (line) => {
      if (last.get(host) !== line) {
        last.set(host, line);
        write(line);
      }
    },
    answered: (host) => {
      if (last.delete(host)) {
        write(answeredLine(host));
      }
    },
  };
}

// ───────────────── the test-mode lock of the call ─────────────────

export const STUB_SENTENCE = 'The accounts call is not replaced in this test run.';

/** Answers `refused` and calls nothing: the default call of a test run. */
export const STUB_CALL: AccountsCall = () => Promise.resolve({ kind: 'refused', refusal: STUB_SENTENCE });

/** The second test-mode lock: in a test run the call is the stub until a scenario replaces it. */
export const callFor = (test: boolean, real: AccountsCall, replaced: AccountsCall | undefined): AccountsCall => replaced ?? (test ? STUB_CALL : real);
