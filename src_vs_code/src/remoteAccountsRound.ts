import { credsSession } from './credsSession';
import { stampOf } from './remotePermission';
import type { AccountsRead } from './remoteAccountsReply';
import type { AccountsMode } from './remoteAccountsScript';
import type { Admission, Committed, HostStanding, Merge } from './remoteHosts';
import type { RemoteRun } from './remoteSsh';
import type { LiveTerminal } from './terminalSource';

/**
 * The round of the accounts read and its memory (E1.S3, contract C4 and C5). Pure: every effect is a seam, so a unit test drives a whole
 * round over fakes and a fake clock. The `vscode` seams are `remoteAccountsHost.ts`'s.
 *
 * <p>A round calls every distinct host of the live `Creds:` terminals that the door admits, ONE at a time and each awaited. Every host's
 * FIRST call reads no Claude usage; the one host that first answers with an account that has no usage reading in this round is called
 * once more, for it. So an account's usage is read at most once in a round and a host is called at most twice. The round never asks a
 * person: the door (`admit`) is S1's, and it has no ask seam.</p>
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
/** The `globalState` key of the state row (C5). */
export const STATE_KEY = 'coai.terminalAccountsState';

export type Trigger = 'timer' | 'refresh' | 'open';

export type UsagePolicy = { readonly kind: 'every-round' } | { readonly kind: 'hourly' } | { readonly kind: 'on-press' } | { readonly kind: 'off' };

/**
 * The ONE constant a later build sets from W6 and W7. E1 ships `off` (the re-review's finding 2): no call is `claude-usage`, by a timer or
 * by a press, so no build runs the usage command before W6 and W7 have recorded results.
 */
export const USAGE_POLICY: UsagePolicy = { kind: 'off' };

/** Whether the accounts round reads any host at all. */
export type AccountsReading = 'off' | 'on';

/**
 * The cut of the accounts plan's stop: in a build from main the round reads no host. With `off` no timer is armed (`timerFor`), no round
 * calls a runner or asks the door (`runRound`), and the list is one row that says so (`accountsRows`). The modules stay: a later build
 * sets this to `on` when it asks the person for its one permission.
 */
export const ACCOUNTS_READING: AccountsReading = 'off';

/** What the list and the notices say while `ACCOUNTS_READING` is `off`. */
export const READING_OFF_SAID = 'The accounts reading is off in this build: it comes back when the next build asks for one permission.';

export type Reading = Extract<AccountsRead, { kind: 'read' }>;
type Unreadable = Extract<AccountsRead, { kind: 'unreadable' }>;
type Found = Extract<Reading['claude'], { state: 'found' }>;
type NotRead = Exclude<Admission['kind'], 'read'>;

/** What the round keeps of one host, in memory only. A field that is not there is `undefined`: nothing here is written to disk. */
export interface HostMemory {
  readonly reading?: { readonly read: Reading; readonly atMs: number } | undefined;
  readonly unreadable?: { readonly why: Unreadable['why']; readonly atMs: number } | undefined;
  readonly failure?: { readonly sentence: string; readonly atMs: number } | undefined;
  /** What the door answered when it was not `read`: the list learns *not asked yet*, *full* and *damaged* from here. */
  readonly admission?: NotRead | undefined;
  readonly claudeAccount?: string | undefined;
  /** When this account's usage was last READ through this host. */
  readonly usageAtMs?: number | undefined;
  /** The Claude Code version at which the usage read went wrong; `''` stands for a version that was not known. */
  readonly usageBlockedAt?: string | undefined;
}

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

/** A call: the real one runs `ssh`; a test replaces it. */
export type AccountsCall = (host: string, mode: AccountsMode) => Promise<RemoteRun>;

/** What the round needs of the world, and no ask: a round never asks anybody. */
export interface RoundSeams {
  readonly live: () => readonly LiveTerminal[];
  /** The Stop, read from the file and the state row before EVERY call. */
  readonly stopped: () => Promise<boolean>;
  /** S1's door, asked before EVERY call and once more before a result is published. */
  readonly admit: (host: string) => Promise<Admission>;
  readonly call: AccountsCall;
  /** Reads the reply against the mode that was SENT; the host binds the clock. */
  readonly parse: (run: { readonly code: number; readonly stdout: string }, mode: AccountsMode) => AccountsRead;
  readonly now: () => number;
  /** After each host whose memory changed, so the list redraws. `memory`: the round's memory with this host's news, for the list to draw. */
  readonly landed: (memory: Memory) => void;
  /** Bumped on a Stop, on a refusal and on a Forget. */
  readonly generation: () => number;
  /** One fixed log line for a result that was dropped. */
  readonly dropped: (host: string) => void;
}

export interface RoundResult {
  readonly memory: Memory;
  /** The hosts whose automatic "yes" this round wrote. */
  readonly allowedNow: readonly string[];
  readonly endedMs: number;
  /** A Stop ended it: the memory of such a round is never stored (`memoryAfter`). */
  readonly abandoned: boolean;
}

/** The memory the host holds after a round: an abandoned round's memory is NOT stored, `stop()` cleared the earlier one. */
export const memoryAfter = (current: Memory, result: RoundResult): Memory => (result.abandoned ? current : result.memory);

interface RoundState {
  readonly memory: Memory;
  /** The account keys whose usage was READ in this round. */
  readonly roundUsage: ReadonlySet<string>;
  readonly allowedNow: readonly string[];
}

interface Context {
  readonly seams: RoundSeams;
  readonly trigger: Trigger;
  readonly policy: UsagePolicy;
  /** The generation when the round started. */
  readonly token: number;
}

type Step =
  | { readonly kind: 'published'; readonly state: RoundState; readonly read: AccountsRead | undefined }
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

/** The door said `read`: the kept kind goes, and a host whose automatic row was written just now is noted. */
const admittedAs = (state: RoundState, host: string, allowedNow: boolean): RoundState => ({
  ...state,
  memory: put(state.memory, host, { ...(state.memory.get(host) ?? {}), admission: undefined }),
  allowedNow: allowedNow && !state.allowedNow.includes(host) ? [...state.allowedNow, host] : state.allowedNow,
});

/** The key whose usage this reply READ, if it did. */
function usageKeyOf(read: AccountsRead | undefined, host: string): string | undefined {
  const found = read === undefined ? undefined : foundOf(read);

  return found !== undefined && found.usage.state === 'read' ? claudeAccountKey(found, host) : undefined;
}

/** A usage time belongs to the account it was read for: a host that changed its account starts with none. */
function usageTimeOf(prev: HostMemory, read: AccountsRead, host: string, nowMs: number): number | undefined {
  const found = foundOf(read);
  if (usageKeyOf(read, host) !== undefined) {
    return nowMs;
  }

  return found !== undefined && claudeAccountKey(found, host) === prev.claudeAccount ? prev.usageAtMs : undefined;
}

/** A block stays only at the version it was made at: a reading with another version unblocks the host. */
function blockOf(prev: HostMemory, read: AccountsRead): string | undefined {
  const found = foundOf(read);

  return blockedAfter(read) ?? (found !== undefined && prev.usageBlockedAt === versionOf(found) ? prev.usageBlockedAt : undefined);
}

/** What a host's memory is after a good or unreadable reply. */
function readMemory(prev: HostMemory, host: string, read: AccountsRead, nowMs: number): HostMemory {
  if (read.kind === 'unreadable') {
    return { ...prev, unreadable: { why: read.why, atMs: nowMs }, failure: undefined };
  }
  const found = foundOf(read);

  return {
    ...prev, reading: { read, atMs: nowMs }, unreadable: undefined, failure: undefined,
    claudeAccount: found === undefined ? undefined : claudeAccountKey(found, host), usageAtMs: usageTimeOf(prev, read, host, nowMs), usageBlockedAt: blockOf(prev, read),
  };
}

/** A refused run is a failure that keeps the older reading. */
const failedMemory = (prev: HostMemory, run: RemoteRun, nowMs: number): HostMemory =>
  ({ ...prev, failure: { sentence: run.kind === 'refused' ? run.refusal : '', atMs: nowMs }, unreadable: undefined });

/** The result of a call, published: a refused run is a failure that keeps the older reading. */
function publishedState(state: RoundState, host: string, run: RemoteRun, read: AccountsRead | undefined, nowMs: number): RoundState {
  const prev = state.memory.get(host) ?? {};
  const next = read === undefined ? failedMemory(prev, run, nowMs) : readMemory(prev, host, read, nowMs);
  const key = usageKeyOf(read, host);

  return { ...state, memory: put(state.memory, host, next), roundUsage: key === undefined ? state.roundUsage : new Set([...state.roundUsage, key]) };
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

  return afterDoor(context, await context.seams.admit(host));
}

/** A Stop, a refusal or a Forget since the round started: the generation is not the round's. */
const moved = (context: Context): boolean => context.seams.generation() !== context.token;

/**
 * The door's answer — unless the generation moved while it was awaited: then nothing is published, whatever the door said. After a `read`
 * the Stop is read again (a Stop saved by another window leaves the generation as it was): a Stop then abandons the round, as (b) does.
 */
const afterDoor = async (context: Context, door: Admission): Promise<Gate> =>
  (moved(context) ? { kind: 'abandon' } : door.kind === 'read' ? readAfterStop(context) : { kind: 'refused', admission: door.kind });

/** A Stop saved by another window while the door was awaited does not move the generation: the Stop is read once more, as failure of (b). */
const readAfterStop = async (context: Context): Promise<Gate> => ((await ended(context)) ? { kind: 'abandon' } : { kind: 'ok' });

/** A result that failed a check is dropped and logged once: a Stop ends the round, a refusal keeps the kind the door answered. */
function droppedStep(seams: RoundSeams, state: RoundState, host: string, gate: Exclude<Gate, { readonly kind: 'ok' }>): Step {
  seams.dropped(host);

  return gate.kind === 'abandon' ? { kind: 'abandoned', state } : { kind: 'skipped', state: withAdmission(state, host, gate.admission) };
}

/** A host whose terminal closed in flight: its result is dropped; a Stop that came meanwhile still ends the round. */
const closedStep = (context: Context, state: RoundState): Step => ({ kind: moved(context) ? 'abandoned' : 'skipped', state });

/** The call is back: a closed host is dropped; a result that fails a check is dropped, once logged; else it is published. */
async function settled(context: Context, state: RoundState, host: string, mode: AccountsMode, run: RemoteRun): Promise<Step> {
  const { seams } = context;
  if (!hostLive(seams, host)) {
    return closedStep(context, state);
  }
  const gate = await checked(context, host);
  if (gate.kind !== 'ok') {
    return droppedStep(seams, state, host, gate);
  }
  const read = run.kind === 'answered' ? seams.parse(run, mode) : undefined;
  const next = publishedState(state, host, run, read, seams.now());
  seams.landed(next.memory);

  return { kind: 'published', state: next, read };
}

/** Whether the round is over: the generation moved, or a Stop is saved (read again, so a Forget that came while it was read is seen too). */
const ended = async (context: Context): Promise<boolean> => moved(context) || (await context.seams.stopped()) || moved(context);

/** What ends a step before its door, and again before its call: a Stop or a moved generation ends the round; a host with no terminal is skipped. */
async function beforeCall(context: Context, state: RoundState, host: string): Promise<Step | undefined> {
  if (await ended(context)) {
    return { kind: 'abandoned', state };
  }

  return hostLive(context.seams, host) ? undefined : { kind: 'skipped', state };
}

/** One call to one host: the Stop, the terminal, the door, the call, then `settled`. */
async function hostStep(context: Context, state: RoundState, host: string, call: 'first' | 'usage'): Promise<Step> {
  const { seams } = context;
  const early = await beforeCall(context, state, host);
  if (early !== undefined) {
    return early;
  }
  const door = await seams.admit(host);
  if (door.kind !== 'read') {
    const skipped = withAdmission(state, host, door.kind);
    seams.landed(skipped.memory);

    return { kind: 'skipped', state: skipped };
  }
  // the door took time: the Stop, the generation and the terminal are looked at again, right before the call
  const late = await beforeCall(context, state, host);
  if (late !== undefined) {
    return late;
  }
  const mode = modeFor(call);

  return settled(context, admittedAs(state, host, door.allowedNow), host, mode, await seams.call(host, mode));
}

/** A host's first call, and the one extra call for the account that reply names, when it is due. */
async function hostRound(context: Context, state: RoundState, host: string): Promise<Step> {
  const first = await hostStep(context, state, host, 'first');
  if (first.kind !== 'published' || first.read === undefined) {
    return first;
  }
  const { seams, policy, trigger } = context;
  const second = needsSecondCall(first.read, first.state.roundUsage, first.state.memory, host, policy, seams.now(), trigger);

  return second ? hostStep(context, first.state, host, 'usage') : first;
}

/** The round's end. A generation that moved by now abandons it: its memory is never stored over what a Stop cleared. A closed host's older entry goes. */
const finished = (context: Context, state: RoundState, abandoned: boolean): RoundResult => ({
  memory: liveOnly(state.memory, accountsHosts(context.seams.live())), allowedNow: state.allowedNow, endedMs: context.seams.now(), abandoned: abandoned || moved(context),
});

/**
 * One round: for each host of the rotated order, in turn and AWAITED, the steps of `hostRound`. `policy` is `USAGE_POLICY` unless a test
 * hands one in: it is a constant of the build, not a setting.
 */
async function readRound(seams: RoundSeams, memory: Memory, round: number, trigger: Trigger, policy: UsagePolicy): Promise<RoundResult> {
  const context: Context = { seams, trigger, policy, token: seams.generation() };
  const hosts = roundOrder(accountsHosts(seams.live()), round);
  let state: RoundState = { memory: liveOnly(memory, hosts), roundUsage: new Set(), allowedNow: [] };
  for (const host of hosts) {
    const step = await hostRound(context, state, host);
    state = step.state;
    if (step.kind === 'abandoned') {
      return finished(context, state, true);
    }
  }

  return finished(context, state, false);
}

/** The cut: no door, no call, no memory. `abandoned` keeps the window's memory as it was and skips every notice. */
const noRound = (seams: RoundSeams): Promise<RoundResult> => Promise.resolve({ memory: new Map(), allowedNow: [], endedMs: seams.now(), abandoned: true });

const ROUND_OF: Readonly<Record<AccountsReading, (seams: RoundSeams, memory: Memory, round: number, trigger: Trigger, policy: UsagePolicy) => Promise<RoundResult>>> =
  { off: noRound, on: readRound };

/** One round, or none while `reading` is `off` (the build's constant unless a test hands one in). */
export const runRound = (
  seams: RoundSeams, memory: Memory, round: number, trigger: Trigger, policy: UsagePolicy = USAGE_POLICY, reading: AccountsReading = ACCOUNTS_READING,
): Promise<RoundResult> => ROUND_OF[reading](seams, memory, round, trigger, policy);

// ───────────────── the timer, as decisions ─────────────────

export type ArmDecision = 'arm-first' | 'clear' | 'keep';

const idle = (input: { readonly armed: boolean; readonly running: boolean }): boolean => !input.armed && !input.running;

/**
 * What the timer does when a terminal opens or closes or the window starts. `eligible`: a live `Creds:` terminal of a host that is not
 * refused. Whether a host is CONTACTED is the door's answer alone.
 */
export function armFor(input: { readonly eligible: boolean; readonly armed: boolean; readonly stopped: boolean; readonly running: boolean }): ArmDecision {
  if (input.stopped || !input.eligible) {
    return 'clear';
  }

  return idle(input) ? 'arm-first' : 'keep';
}

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
 * The first test-mode lock, and the ONE road from a decision to a timer: in a test run, or while the reading is `off`, no timer is ever
 * started, whatever `armFor` or `nextAfterRound` answered. A decision after a round is an absolute time; the wait is counted from `nowMs`.
 */
export function timerFor(decision: ArmDecision | NextRound, test: boolean, nowMs = 0, reading: AccountsReading = ACCOUNTS_READING): TimerAction {
  if (timerLocked(test, reading)) {
    return { kind: 'none' };
  }

  return actionOf(decision, nowMs);
}

const timerLocked = (test: boolean, reading: AccountsReading): boolean => test || reading === 'off';

const actionOf = (decision: ArmDecision | NextRound, nowMs: number): TimerAction => (typeof decision === 'string' ? ARMED[decision] : afterRound(decision, nowMs));

/** After a round: the next one `ROUND_GAP_MS` from the END of this one, or no timer. */
export const nextAfterRound = (input: { readonly stopped: boolean; readonly eligible: boolean; readonly endedMs: number }): NextRound =>
  (input.stopped || !input.eligible ? { kind: 'clear' } : { kind: 'at', atMs: input.endedMs + ROUND_GAP_MS });

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

// ───────────────── the state row and the Stop (C5) ─────────────────

export interface AccountsState {
  readonly toldUtc?: string;
  readonly lastAutoNoticeUtc?: string;
  readonly stoppedUtc?: string;
  /** The copy of the newest withdrawal's stamp (E2.S1, § P5): a stamp, or a value that is not one — then it counts as a withdrawal. */
  readonly withdrawnStamp?: unknown;
  /** When the icon's question was first asked. */
  readonly askedUtc?: string;
  readonly [other: string]: unknown;
}

const isRecord = (value: unknown): value is Readonly<Record<string, unknown>> => typeof value === 'object' && value !== null && !Array.isArray(value);

const FIELDS: readonly string[] = ['toldUtc', 'lastAutoNoticeUtc', 'stoppedUtc', 'askedUtc'];

const fieldsOk = (value: Readonly<Record<string, unknown>>): boolean => FIELDS.every((field) => value[field] === undefined || typeof value[field] === 'string');

/** `undefined` (never written) is the empty state; an object whose three fields are each absent or a string is that state; anything else is damaged. */
export function accountsStateOf(value: unknown): { readonly kind: 'state'; readonly state: AccountsState } | { readonly kind: 'damaged' } {
  if (value === undefined) {
    return { kind: 'state', state: {} };
  }

  return isRecord(value) && fieldsOk(value) ? { kind: 'state', state: value } : { kind: 'damaged' };
}

/** The Stop's copy in the row. A row that cannot be read is STOPPED, not empty: the stop is the way out when a read does harm on a VM. */
export function stoppedOf(value: unknown): boolean {
  const read = accountsStateOf(value);

  return read.kind === 'damaged' || typeof read.state.stoppedUtc === 'string';
}

/**
 * What the permission needs of the row (E2.S1, § P5). `withdrawn`: a stamp, nothing, or a value that is not a stamp (`'unreadable'`, which counts as a
 * withdrawal); a row that cannot be read at all is STOPPED and has no permission. `stopped`: the Stop's copy.
 */
export function permissionRowOf(value: unknown): { readonly withdrawn: number | 'unreadable' | undefined; readonly stopped: boolean; readonly askedUtc: string | undefined } {
  const read = accountsStateOf(value);
  if (read.kind === 'damaged') {
    return { withdrawn: 'unreadable', stopped: true, askedUtc: undefined };
  }
  const copy = read.state.withdrawnStamp;

  return { withdrawn: copy === undefined ? undefined : (stampOf(copy) ?? 'unreadable'), stopped: typeof read.state.stoppedUtc === 'string', askedUtc: read.state.askedUtc };
}

/** A FIELD change of the state row. The first four are E1's; the last three are the permission's (E2.S1). */
export type StateChange =
  | { readonly stop: string }
  | { readonly start: true }
  | { readonly told: string }
  | { readonly autoNoticed: string }
  /** A withdrawal's stamp: the row's copy becomes the higher of the two. `press`: a person's press; `read`: a copy-out, which never replaces a copy that is not a stamp. */
  | { readonly withdrawn: number; readonly utc: string; readonly by: 'press' | 'read' }
  /** An *Allow*: the copy is raised to its floor; a copy that is not a stamp is REPLACED by it. `restart`: the copy is removed. */
  | { readonly floor: number; readonly utc: string; readonly restart?: true }
  /** The icon's question was asked. Written once. */
  | { readonly asked: string };

export type StateCommitted = { readonly kind: 'written' } | { readonly kind: 'kept' };

const without = (state: AccountsState, field: string): AccountsState => Object.fromEntries(Object.entries(state).filter(([key]) => key !== field));

type WithdrawnChange = Extract<StateChange, { readonly withdrawn: number }>;
type FloorChange = Extract<StateChange, { readonly floor: number }>;

/** The row an *Allow* leaves over a row that could not be read: the floor as the copy (when it is one), still STOPPED. */
const damagedFloor = (change: FloorChange): AccountsState => ({ ...(change.floor >= 1 && change.restart !== true ? { withdrawnStamp: change.floor } : {}), stoppedUtc: change.utc });

/** A withdrawal over a row that could not be read: a person's press writes the copy with the stop's time; a copy-out writes nothing. */
const damagedWithdrawn = (change: WithdrawnChange): AccountsState | undefined => (change.by === 'press' ? { withdrawnStamp: change.withdrawn, stoppedUtc: change.utc } : undefined);

/**
 * Over a row that cannot be read only a person's press writes: a stop writes a row with its time, a start an empty row, a withdrawal or an *Allow* the
 * copy with the stop's time (the row stays STOPPED); a notice time, a copy-out and the first asking write nothing.
 */
const DAMAGED_BY: Readonly<Record<string, (change: never) => AccountsState | undefined>> = {
  stop: (change: { readonly stop: string }) => ({ stoppedUtc: change.stop }),
  start: () => ({}),
  withdrawn: damagedWithdrawn,
  floor: damagedFloor,
};

/** The name of the field that says which change it is. */
const fieldOf = (change: StateChange): string | undefined => Object.keys(DAMAGED_BY).find((field) => field in change);

const damagedNext = (change: StateChange): AccountsState | undefined => {
  const field = fieldOf(change);

  return field === undefined ? undefined : (DAMAGED_BY[field] as (change: StateChange) => AccountsState | undefined)(change);
};

const stoppingNext = (state: AccountsState, change: { readonly stop: string } | { readonly start: true }): AccountsState | undefined =>
  ('stop' in change ? { ...state, stoppedUtc: change.stop } : state.stoppedUtc === undefined ? undefined : without(state, 'stoppedUtc'));

const toldNext = (state: AccountsState, utc: string): AccountsState | undefined => (state.toldUtc === undefined ? { ...state, toldUtc: utc } : undefined);

/** The later of the two times is kept. */
const autoNoticedNext = (state: AccountsState, utc: string): AccountsState | undefined =>
  (state.lastAutoNoticeUtc !== undefined && state.lastAutoNoticeUtc >= utc ? undefined : { ...state, lastAutoNoticeUtc: utc });

const noticeNext = (state: AccountsState, change: { readonly told: string } | { readonly autoNoticed: string }): AccountsState | undefined =>
  ('told' in change ? toldNext(state, change.told) : autoNoticedNext(state, change.autoNoticed));

const isCopy = (state: AccountsState): boolean => state.withdrawnStamp !== undefined;

/** The copy raised to `stamp` — only when it is absent or a LOWER stamp. A copy that is not a stamp is never replaced here. */
function copyNext(state: AccountsState, stamp: number): AccountsState | undefined {
  const had = stampOf(state.withdrawnStamp);

  return !isCopy(state) || (had !== undefined && had < stamp) ? { ...state, withdrawnStamp: stamp } : undefined;
}

/** An *Allow* over a copy that is not a stamp: it is replaced by the floor, or removed when there is no floor. */
const replacedCopy = (state: AccountsState, floor: number): AccountsState => (floor >= 1 ? { ...state, withdrawnStamp: floor } : without(state, 'withdrawnStamp'));

/** An *Allow*: a copy that is not a stamp is REPLACED by the floor; the restart removes the copy. Else the copy is raised to the floor when the floor is higher. */
function floorNext(state: AccountsState, change: FloorChange): AccountsState | undefined {
  if (change.restart === true) {
    return isCopy(state) ? without(state, 'withdrawnStamp') : undefined;
  }

  return isBadCopy(state) ? replacedCopy(state, change.floor) : raisedTo(state, change.floor);
}

/** A copy that is there and is not a stamp. */
const isBadCopy = (state: AccountsState): boolean => isCopy(state) && stampOf(state.withdrawnStamp) === undefined;

const raisedTo = (state: AccountsState, floor: number): AccountsState | undefined => (floor >= 1 ? copyNext(state, floor) : undefined);

const askedNext = (state: AccountsState, utc: string): AccountsState | undefined => (state.askedUtc === undefined ? { ...state, askedUtc: utc } : undefined);

/** One merge for each field that says which change it is. */
const READABLE_BY: Readonly<Record<string, (state: AccountsState, change: never) => AccountsState | undefined>> = {
  stop: stoppingNext,
  start: stoppingNext,
  told: noticeNext,
  autoNoticed: noticeNext,
  withdrawn: (state: AccountsState, change: WithdrawnChange) => copyNext(state, change.withdrawn),
  floor: floorNext,
  asked: (state: AccountsState, change: { readonly asked: string }) => askedNext(state, change.asked),
};

const readableNext = (state: AccountsState, change: StateChange): AccountsState | undefined => {
  const field = Object.keys(READABLE_BY).find((one) => one in change);

  return field === undefined ? undefined : (READABLE_BY[field] as (state: AccountsState, change: StateChange) => AccountsState | undefined)(state, change);
};

/**
 * The merge of the state row, given to `sessions.commitKey`: it reads the value in the step of the write. A stale VARIABLE is harmless; a stale
 * WINDOW is not, and what keeps the Stop is the refusals file (S1 X17): `stoppedUtc` is its copy.
 */
export const stateMerge: Merge<StateChange, StateCommitted> = (stored, change) => {
  const read = accountsStateOf(stored);
  const next = read.kind === 'damaged' ? damagedNext(change) : readableNext(read.state, change);

  return next === undefined ? { next: undefined, result: { kind: 'kept' } } : { next: { value: next }, result: { kind: 'written' } };
};

/** What the Stop needs of the road: the file's three answers and the one commit of a `globalState` key. */
export interface StopDoor {
  readonly stopRound: () => Promise<Committed>;
  readonly startRound: () => Promise<Committed>;
  readonly roundStop: () => Promise<'stopped' | 'running' | 'damaged'>;
  readonly commitKey: <C, R>(key: string, change: C, merge: Merge<C, R>) => Promise<R>;
}

/**
 * The round is stopped when the FILE says so (read from the disk) OR the copy in the row does. A `damaged` file is not a stop here: every
 * admission then answers `damaged` and the list says so.
 */
export async function isStopped(door: Pick<StopDoor, 'roundStop'>, readState: () => unknown): Promise<boolean> {
  return (await door.roundStop()) === 'stopped' || stoppedOf(readState());
}

/** The file FIRST, then the copy, whatever the first answered: a Stop that was not saved still stops this window. */
export async function stopTheRound(door: StopDoor, utc: string): Promise<void> {
  await door.stopRound();
  await door.commitKey(STATE_KEY, { stop: utc }, stateMerge);
}

/** The file FIRST; the copy is cleared only when the file took the start (`written` or `kept`). Answers whether it was. */
export async function startTheRound(door: StopDoor): Promise<boolean> {
  const started = await door.startRound();
  if (started.kind !== 'written' && started.kind !== 'kept') {
    return false;
  }
  await door.commitKey(STATE_KEY, { start: true }, stateMerge);

  return true;
}
