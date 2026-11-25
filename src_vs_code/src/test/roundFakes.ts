import type { AccountsRead } from '../remoteAccountsReply';
import type { ClaudeUsage } from '../remoteAccountsUsage';
import type { AccountsMode } from '../remoteAccountsScript';
import type { HostMemory, Memory, RoundResult, RoundSeams, Trigger, UsagePolicy } from '../remoteAccountsRound';
import { claudeAccountKey, runRound } from '../remoteAccountsRound';
import type { Admission } from '../remoteHosts';
import type { LiveTerminal } from '../terminalSource';

/**
 * The fakes of the round's tests (E1.S3): a world of live terminals and a fake accounts call whose answers the test chooses, a fake clock
 * and the controls of a Stop or a refusal that arrives while a call is held open. The fake call answers a run that only NAMES its host, its
 * mode and how many calls the host had; the fake `parse` turns that into the typed reading the test chose.
 */

export const SKIPPED: ClaudeUsage = { state: 'skipped' };
export const USAGE_READ: ClaudeUsage = { state: 'read', windows: [], unmatched: 0 };
export const EVERY_ROUND: UsagePolicy = { kind: 'every-round' };
export const HOURLY: UsagePolicy = { kind: 'hourly' };
export const ON_PRESS: UsagePolicy = { kind: 'on-press' };
export const OFF: UsagePolicy = { kind: 'off' };
export const READ_OK: Admission = { kind: 'read', how: 'auto', allowedNow: false };

/** A reading with a Claude account found; the other two vendors are absent. */
type Reading = Extract<AccountsRead, { kind: 'read' }>;

export function claudeFound(email: string, over: { readonly org?: string | null | undefined; readonly version?: string | null | undefined; readonly usage?: ClaudeUsage | undefined } = {}): Reading {
  return {
    kind: 'read',
    claude: { state: 'found', version: over.version === undefined ? '2.1.5' : over.version, email, org: over.org ?? null, plan: null, usage: over.usage ?? SKIPPED },
    codex: { state: 'absent' },
    agy: { state: 'absent' },
    jqMissing: undefined,
  };
}

/** The found side of a reading the test built itself. */
export function foundOf(read: Reading): Extract<Reading['claude'], { state: 'found' }> {
  if (read.claude.state === 'found') {
    return read.claude;
  }
  throw new Error('the test built a reading with no Claude account');
}

/** What a remembered host holds: a reading of that account, and its key. */
export function remembered(host: string, email: string, over: Partial<HostMemory> = {}): HostMemory {
  const read = claudeFound(email);

  return { reading: { read, atMs: 0 }, claudeAccount: claudeAccountKey(foundOf(read), host), ...over };
}

export const terminal = (host: string, session = 's1', over: Partial<LiveTerminal> = {}): LiveTerminal => ({ key: {}, name: `Creds: ${host} / ${session}`, exited: false, ...over });

export interface Call {
  readonly host: string;
  readonly mode: AccountsMode;
}

export interface Options {
  readonly hosts?: readonly string[];
  /** What the host answers: a reading, or `down` for an ssh that did not answer. `nth`: how many calls this host had before. */
  readonly reply?: (host: string, mode: AccountsMode, nth: number) => AccountsRead | 'down';
  /** The door's answer; `asked` is how many times it was asked about this host before. */
  readonly door?: (host: string, asked: number, world: World) => Admission;
  /** Awaited inside the call, before it answers. */
  readonly hold?: (host: string, mode: AccountsMode, world: World) => Promise<void> | undefined;
  readonly stopped?: (world: World) => Promise<boolean>;
}

export interface World {
  readonly seams: RoundSeams;
  readonly calls: Call[];
  readonly events: string[];
  readonly admits: string[];
  readonly dropped: string[];
  readonly parsed: { readonly mode: AccountsMode; readonly stdout: string }[];
  /** `memories`: what each call of `landed` was handed. */
  readonly landed: { count: number; readonly memories: (Memory | undefined)[] };
  readonly control: { generation: number; stopped: boolean; now: number; terminals: readonly LiveTerminal[]; active: number; maxActive: number };
}

/** The same account on every host named, as `reply` for `Options`. */
export const accounts = (emails: Readonly<Record<string, string>>, usage: ClaudeUsage = USAGE_READ): NonNullable<Options['reply']> =>
  (host, mode) => claudeFound(emails[host] ?? '', { usage: mode === 'claude-usage' ? usage : SKIPPED });

export function world(options: Options = {}): World {
  const control = { generation: 0, stopped: false, now: 1_000_000, terminals: (options.hosts ?? []).map((host) => terminal(host)), active: 0, maxActive: 0 };
  const calls: Call[] = [];
  const events: string[] = [];
  const admits: string[] = [];
  const dropped: string[] = [];
  const parsed: { mode: AccountsMode; stdout: string }[] = [];
  const landed: { count: number; memories: (Memory | undefined)[] } = { count: 0, memories: [] };
  const asked = new Map<string, number>();
  const nths = new Map<string, number>();
  const reply = options.reply ?? ((): AccountsRead => claudeFound('x@example.test'));
  const self: { current?: World } = {};

  const seams: RoundSeams = {
    live: () => control.terminals,
    stopped: () => (options.stopped === undefined ? Promise.resolve(control.stopped) : options.stopped(self.current as World)),
    admit: (host) => {
      admits.push(host);
      const before = asked.get(host) ?? 0;
      asked.set(host, before + 1);

      return Promise.resolve(options.door === undefined ? READ_OK : options.door(host, before, self.current as World));
    },
    call: async (host, mode) => {
      const nth = nths.get(host) ?? 0;
      nths.set(host, nth + 1);
      calls.push({ host, mode });
      events.push(`start ${host}`);
      control.active += 1;
      control.maxActive = Math.max(control.maxActive, control.active);
      await options.hold?.(host, mode, self.current as World);
      control.active -= 1;
      events.push(`end ${host}`);

      return reply(host, mode, nth) === 'down'
        ? { kind: 'refused', refusal: `“${host}” did not answer over ssh within 30 s.` }
        : { kind: 'answered', code: 0, stdout: JSON.stringify({ host, nth }) };
    },
    parse: (run, mode) => {
      parsed.push({ mode, stdout: run.stdout });
      const named = JSON.parse(run.stdout) as { host: string; nth: number };
      const read = reply(named.host, mode, named.nth);

      return read === 'down' ? { kind: 'unreadable', why: 'exit' } : read;
    },
    now: () => control.now,
    landed: (memory) => { landed.count += 1; landed.memories.push(memory); },
    generation: () => control.generation,
    dropped: (host) => { dropped.push(host); },
  };
  const made: World = { seams, calls, events, admits, dropped, parsed, landed, control };
  self.current = made;

  return made;
}

/** A round over a world, with the policy the test gives. */
export const roundOf = (w: World, memory: Memory = new Map(), round = 0, trigger: Trigger = 'timer', policy: UsagePolicy = EVERY_ROUND): Promise<RoundResult> =>
  runRound(w.seams, memory, round, trigger, policy);

/** Each call as `a N` (no usage) or `a U` (usage), so a red shows the order. */
export const callsOf = (w: World): readonly string[] => w.calls.map((call) => `${call.host} ${call.mode === 'claude-usage' ? 'U' : 'N'}`);

export const usageCalls = (w: World): number => w.calls.filter((call) => call.mode === 'claude-usage').length;

export const memoryOf = (...entries: readonly (readonly [string, HostMemory])[]): Memory => new Map(entries);
