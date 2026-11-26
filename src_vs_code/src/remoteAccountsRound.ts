import { credsSession } from './credsSession';
import type { AccountsRead } from './remoteAccountsReply';
import type { AccountsMode } from './remoteAccountsScript';
import type { HostStanding } from './remoteHosts';
import { WslPlace, isWslKey, memoryKeyOf, splitWslKey } from './remotePlaces';
import { WslFacts, WslPort, doorOf, retainedAfter, wslRound } from './remoteWslRound';
import type { PlaceCall, PlaceRun, Visited, WslHooks, WslWalk } from './remoteWslRound';
import type { DoorAnswer } from './remotePermission';
import type { RemoteRun } from './remoteSsh';
import type { LiveTerminal } from './terminalSource';

/**
 * The round of the accounts read and its memory (E1.S3, contract C4 and C5). Pure: every effect is a seam, so a unit test drives a whole
 * round over fakes and a fake clock. The `vscode` seams are `remoteAccountsHost.ts`'s.
 *
 * <p>A round calls every distinct host of the live `Creds:` terminals that the door answers `yes` for, ONE at a time and each awaited. Every
 * host's FIRST call reads no Claude usage; the one host that first answers with an account that has no usage reading in this round is called
 * once more, for it. So an account's usage is read at most once in a round and a host is called at most twice. The round never asks a
 * person: the door (`may`, E2.S1's) has no ask seam. A door that answers `no-permission` or `stopped` ENDS the round; `no`, `damaged` and
 * `waiting` skip that host. The scope host `wsl` is called only when the window gives the round a WSL port (a Windows window, E3.S3): its turn is `wslTurn`, the WSL part of the round, place by place.</p>
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

export type Trigger = 'timer' | 'refresh' | 'open';

export type UsagePolicy = { readonly kind: 'every-round' } | { readonly kind: 'hourly' } | { readonly kind: 'on-press' } | { readonly kind: 'off' };

/**
 * The ONE constant a later build sets from W6 and W7. E1 ships `off` (the re-review's finding 2): no call is `claude-usage`, by a timer or
 * by a press, so no build runs the usage command before W6 and W7 have recorded results.
 */
export const USAGE_POLICY: UsagePolicy = { kind: 'off' };

/**
 * The scope host that is never called, in any window (E2.S4): `Creds: wsl / …` names a login session of a WSL distro, which is not a host that
 * `ssh` reaches. Its block says why (E2S2-T12); the round calls nothing for it.
 */
export const WSL_SCOPE = 'wsl';

export type Reading = Extract<AccountsRead, { kind: 'read' }>;
type Unreadable = Extract<AccountsRead, { kind: 'unreadable' }>;
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
  readonly unreadable?: { readonly why: Unreadable['why']; readonly atMs: number } | undefined;
  readonly failure?: { readonly sentence: string; readonly atMs: number } | undefined;
  /** What the door answered when it was not `read`: the list learns *not asked yet*, *full* and *damaged* from here. */
  readonly admission?: NotRead | undefined;
  readonly claudeAccount?: string | undefined;
  /** When this account's usage was last READ through this host. */
  readonly usageAtMs?: number | undefined;
  /** The Claude Code version at which the usage read went wrong; `''` stands for a version that was not known. */
  readonly usageBlockedAt?: string | undefined;
  /** How long this place's last call took, in ms: the hover of the age says it beside the exact time (R9a). */
  readonly tookMs?: number | undefined;
  /** The scope host `wsl`'s own facts of the last round (E3.S3): the distros, the counts past a cap, a failure, the strikes. Only on the entry `wsl`. */
  readonly wsl?: WslFacts | undefined;
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

/** The hosts a round may call: `accountsHosts`, and the scope host `wsl` only when the window reads WSL (`wslOn`). */
export const callableHosts = (live: readonly LiveTerminal[], wslOn = false): readonly string[] => accountsHosts(live).filter((host) => host !== WSL_SCOPE || wslOn);

/** The scope host of a memory key: a WSL place's key belongs to `wsl`; a host is its own. */
export const scopeOf = (key: string): string => (isWslKey(key) ? WSL_SCOPE : key);

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
  /** The WSL port of a Windows window (E3.S3): its three calls, each asking the door in its one spawn function. Nothing, and `wsl` is never called, in any other. */
  readonly wsl?: WslPort | undefined;
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
  | { readonly kind: 'published'; readonly state: RoundState; readonly read: AccountsRead | undefined }
  | { readonly kind: 'skipped'; readonly state: RoundState }
  | { readonly kind: 'abandoned'; readonly state: RoundState };

const put = (memory: Memory, host: string, next: HostMemory): Memory => new Map<string, HostMemory>([...memory, [host, next]]);

/** The memory of the hosts that are still in the list: a host whose last terminal closed is dropped. */
export const liveOnly = (memory: Memory, hosts: readonly string[]): Memory => new Map([...memory].filter(([key]) => hosts.includes(scopeOf(key))));

const hostLive = (seams: RoundSeams, host: string): boolean => accountsHosts(seams.live()).includes(scopeOf(host));

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

/** A host's memory with how long its last call took. */
const timed = (memory: HostMemory, tookMs: number): HostMemory => ({ ...memory, tookMs });

/** The places that answered with a reading, counting this one when its reply is one. */
const readPlaces = (state: RoundState, host: string, read: AccountsRead | undefined): ReadonlySet<string> => (read?.kind === 'read' ? new Set([...state.read, host]) : state.read);

/** The result of a call, published: a refused run is a failure that keeps the older reading. */
function publishedState(state: RoundState, host: string, run: RemoteRun, read: AccountsRead | undefined, nowMs: number, tookMs: number): RoundState {
  const prev = state.memory.get(host) ?? {};
  const next = timed(read === undefined ? failedMemory(prev, run, nowMs) : readMemory(prev, host, read, nowMs), tookMs);
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

  return afterDoor(context, await context.seams.may(scopeOf(host)));
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

/** The call is back: a closed host is dropped; a result that fails a check is dropped, once logged; else it is published. */
async function settled(context: Context, state: RoundState, host: string, mode: AccountsMode, run: RemoteRun, startedMs: number): Promise<Step> {
  const { seams } = context;
  if (!hostLive(seams, host)) {
    return closedStep(context, state);
  }
  const gate = await checked(context, host);
  if (gate.kind !== 'ok') {
    return droppedStep(seams, state, host, gate);
  }
  const read = run.kind === 'answered' ? seams.parse(run, mode) : undefined;
  const nowMs = seams.now();
  const next = publishedState(state, host, run, read, nowMs, Math.max(0, nowMs - startedMs));
  seams.landed(next.memory, host);

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

/** The door did not say `yes`: the answers that end the round end it; the others skip the host, which keeps the kind for the list. */
function notYes(seams: RoundSeams, state: RoundState, host: string, door: Exclude<DoorAnswer, 'yes'>): Step {
  if (endsTheRound(door)) {
    return { kind: 'abandoned', state };
  }
  const skipped = withAdmission(state, host, HELD_AS[door]);
  seams.landed(skipped.memory, host);

  return { kind: 'skipped', state: skipped };
}

/** What a call is made with: the mode and, for a usage call, the digest of the first reply. An ssh host's call is the round's `call`; a WSL place's is the walk's. */
type Runner = (mode: AccountsMode, digest: string | undefined) => Promise<PlaceRun | RemoteRun>;

/** The door's own answer, when a WSL call was refused by the door inside its spawn (an ssh run has none). */
const doorAnswerOf = (run: PlaceRun | RemoteRun): DoorAnswer | undefined => (run.kind === 'refused' && 'end' in run ? doorOf(run) : undefined);

/** A WSL place whose distro was not running when it was read again: no call was made, and the place keeps nothing. */
const withoutKey = (state: RoundState, key: string): RoundState => ({ ...state, memory: new Map([...state.memory].filter(([one]) => one !== key)) });

/** The call is back: the door's refusal inside it, a place that was not read, or a result to publish. */
async function afterCall(context: Context, state: RoundState, host: string, mode: AccountsMode, got: PlaceRun | RemoteRun, startedMs: number): Promise<Step> {
  const door = doorAnswerOf(got);
  if (door !== undefined && door !== 'yes') {
    return notYes(context.seams, state, host, door);
  }
  if (got.kind === 'left') {
    return { kind: 'skipped', state: withoutKey(state, host) };
  }

  return settled(context, admitted(state, host), host, mode, got, startedMs);
}

/** One call to one place: the Stop, the terminal, the door, the call, then `afterCall`. `host` is the memory key. */
async function hostStep(context: Context, state: RoundState, host: string, call: 'first' | 'usage', run: Runner): Promise<Step> {
  const { seams } = context;
  const early = await beforeCall(context, state, host);
  if (early !== undefined) {
    return early;
  }
  const door = await seams.may(scopeOf(host));
  if (door !== 'yes') {
    return notYes(seams, state, host, door);
  }
  // the door took time: the Stop, the generation and the terminal are looked at again, right before the call
  const late = await beforeCall(context, state, host);
  if (late !== undefined) {
    return late;
  }
  const mode = modeFor(call);
  const startedMs = seams.now();

  return afterCall(context, state, host, mode, await run(mode, undefined), startedMs);
}

/** A host's first call, and the one extra call for the account that reply names, when it is due. */
async function hostRound(context: Context, state: RoundState, host: string): Promise<Step> {
  const run: Runner = (mode) => context.seams.call(host, mode);
  const first = await hostStep(context, state, host, 'first', run);
  if (first.kind !== 'published' || first.read === undefined) {
    return first;
  }
  const { seams, policy, trigger } = context;
  const second = needsSecondCall(first.read, first.state.roundUsage, first.state.memory, host, policy, seams.now(), trigger);

  return second ? hostStep(context, first.state, host, 'usage', run) : first;
}

// ───────────────── the WSL turn (E3.S3) ─────────────────

/** The answer of the door that a kept admission stands for: what the WSL part ended with, when a place's own door question said no. */
const DOOR_OF_KIND: Readonly<Record<NotRead, DoorAnswer>> = { refused: 'no', 'not-asked': 'no-permission', full: 'no', damaged: 'damaged', unwritten: 'waiting' };

/** The scope host's facts are in its entry of the memory. */
const withFacts = (state: RoundState, facts: WslFacts): RoundState =>
  ({ ...state, memory: put(state.memory, WSL_SCOPE, { ...(state.memory.get(WSL_SCOPE) ?? {}), wsl: facts, admission: undefined }) });

/** The places of distros and users that the walk found gone, or not running, keep nothing; a list that was not read keeps all. */
function retired(state: RoundState, walk: WslWalk<RoundState>): RoundState {
  const kept = ([key]: readonly [string, HostMemory]): boolean => {
    const place = splitWslKey(key);

    return place === undefined || retainedAfter(walk.seen, place.distro, place.user);
  };

  return { ...state, memory: new Map([...state.memory].filter(kept)) };
}

/** What ends the WSL part after a place whose own door question said no: the answer that kind stands for. */
const endAfter = (step: Step, key: string): Visited<RoundState>['end'] => (step.kind === 'abandoned' ? 'stopped' : doorEnd(step.kind === 'skipped' ? step.state.memory.get(key) : undefined));

/** The WSL part's end for a place's kept admission, or nothing when it has none. */
function doorEnd(memory: HostMemory | undefined): Visited<RoundState>['end'] {
  const kind = memory?.admission;

  return kind === undefined ? undefined : { door: DOOR_OF_KIND[kind] };
}

/**
 * The scope host `wsl`'s turn: the WSL part of the round, place by place, one at a time and awaited. One class because it is one sequence of awaited
 * steps whose state the walk's hooks and its visits both move; what leaves it is a `Step`, as for a host.
 */
class WslTurn {
  private state: RoundState;

  constructor(private readonly context: Context, state: RoundState, private readonly port: WslPort) {
    this.state = state;
  }

  private hooks(): WslHooks {
    return {
      stillOn: async () => !(await ended(this.context)) && hostLive(this.context.seams, WSL_SCOPE),
      facts: (facts) => { this.landsFacts(facts); },
    };
  }

  private landsFacts(facts: WslFacts): void {
    this.state = withFacts(this.state, facts);
    this.context.seams.landed(this.state.memory, WSL_SCOPE);
  }

  /** One place: the same step as a host's (the Stop, the terminal, the door, the call, the checks, the publication), with the walk's call. */
  private async visit(_state: RoundState, place: WslPlace, call: PlaceCall): Promise<Visited<RoundState>> {
    const key = memoryKeyOf(place);
    const step = await hostStep(this.context, this.state, key, 'first', (mode, digest) => call(mode, digest));
    this.state = step.state;

    return { state: step.state, end: endAfter(step, key) };
  }

  private finish(walk: WslWalk<RoundState>): Step {
    this.state = retired(this.state, walk);
    this.context.seams.landed(this.state.memory, WSL_SCOPE);

    return walk.end === 'stopped' ? { kind: 'abandoned', state: this.state } : { kind: 'published', state: this.state, read: undefined };
  }

  async run(): Promise<Step> {
    const early = await beforeCall(this.context, this.state, WSL_SCOPE);
    if (early !== undefined) {
      return early;
    }
    const door = await this.context.seams.may(WSL_SCOPE);
    if (door !== 'yes') {
      return notYes(this.context.seams, this.state, WSL_SCOPE, door);
    }

    return this.finish(await wslRound(this.port, this.hooks(), this.state, (state, place, call) => this.visit(state, place, call)));
  }
}

/** The turn of one scope host: `wsl` is the WSL walk when the window gives the round a port; every other host is `hostRound`. */
function turnOf(context: Context, state: RoundState, host: string): Promise<Step> {
  const port = host === WSL_SCOPE ? context.seams.wsl : undefined;

  return port === undefined ? hostRound(context, state, host) : new WslTurn(context, state, port).run();
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
  const hosts = roundOrder(callableHosts(seams.live(), seams.wsl !== undefined), round);
  let state: RoundState = { memory: liveOnly(memory, hosts), roundUsage: new Set(), asked: new Set(), read: new Set() };
  for (const host of hosts) {
    const step = await turnOf(context, state, host);
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
  new Map([...memory].filter(([key]) => !refused.has(scopeOf(key)) || key === scopeOf(key)).map(([host, one]): [string, HostMemory] => [host, refused.has(host) ? refusedEntry(one) : one]));

/** A refused host's entry: its reading and its WSL facts go, and the kind the door will answer stays. */
const refusedEntry = (one: HostMemory): HostMemory => ({ ...Object.fromEntries(Object.entries(one).filter(([field]) => field !== 'wsl')), reading: undefined, admission: 'refused' });

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

const hasAnswer = (one: HostMemory | undefined): boolean => [one?.reading, one?.failure, one?.wsl].some((held) => held !== undefined);

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

export { STATE_KEY, accountsStateOf, isStopped, permissionRowOf, startTheRound, stateMerge, stopTheRound, stoppedOf } from './remoteAccountsStateRow';
export type { AccountsState, StateChange, StateCommitted, StopDoor } from './remoteAccountsStateRow';
