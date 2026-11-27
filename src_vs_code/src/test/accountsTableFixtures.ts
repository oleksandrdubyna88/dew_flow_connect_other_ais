import type { AccountsRead, AgyUsage, ClaudeUsage, CodexUsage } from '../remoteAccountsReply';
import type { HostMemory, Memory } from '../remoteAccountsRound';
import type { HostStanding } from '../remoteHosts';
import type { TableInput } from '../remoteAccountsTable';

/** What the tests of the accounts table (the model, the groupings, the page) share: made-up readings, hosts and a clock. Every address is made up. */

export const NOW = Date.UTC(2026, 10, 26, 14, 0, 0);
export const min = (n: number): number => n * 60_000;
export const hour = (n: number): number => n * 3_600_000;

export type Reading = Extract<AccountsRead, { kind: 'read' }>;
export type Claude = Reading['claude'];
export type ClaudeFound = Extract<Claude, { state: 'found' }>;

export const ANN = 'ann@example.test';
export const ORG = 'Example Org';

export const claude = (over: Partial<ClaudeFound> = {}): ClaudeFound =>
  ({ state: 'found', version: '2.1.5', email: ANN, org: ORG, plan: 'team', usage: { state: 'skipped' }, ...over });

export const reading = (over: Partial<Reading> = {}): Reading => ({ kind: 'read', claude: { state: 'absent' }, codex: { state: 'absent' }, agy: { state: 'absent' }, jqMissing: undefined, ...over });

export const usageOf = (five: number | null, week: number | null, fable: number | null, nowMs = NOW): ClaudeUsage => ({
  state: 'read',
  unmatched: 0,
  windows: [
    { name: '5h', usedPercent: five, resetsAtMs: nowMs + min(130) },
    { name: 'week', usedPercent: week, resetsAtMs: nowMs + min(3_800) },
    { name: { model: 'Fable' }, usedPercent: fable, resetsAtMs: null },
  ],
});

export const codexUsage = (over: Partial<Extract<CodexUsage, { state: 'read' }>> = {}): CodexUsage =>
  ({ state: 'read', windows: [{ minutes: 300, usedPercent: 10, resetsAtMs: NOW + min(65) }, { minutes: 10_080, usedPercent: 14, resetsAtMs: NOW + min(6_000) }], ...over });

export const COX = 'cox@example.test';
export const AGY_MAIL = 'agy@example.test';

export const codexOf = (usage: CodexUsage = codexUsage(), email = COX): Reading['codex'] => ({ state: 'found', signInFile: true, email, plan: 'prolite', usage });

/** Antigravity's quota as the parser makes it: the Gemini group part-used with a reset, the third-party group full with none. */
export const agyUsage = (): AgyUsage => ({
  state: 'read',
  windows: [
    { group: 'gemini', window: 'weekly', leftPercent: 60, resetsAtMs: NOW + hour(60) },
    { group: 'gemini', window: '5h', leftPercent: 92, resetsAtMs: NOW + min(95) },
    { group: 'third-party', window: 'weekly', leftPercent: 100, resetsAtMs: null },
    { group: 'third-party', window: '5h', leftPercent: 100, resetsAtMs: null },
  ],
});

export const agyOf = (usage: AgyUsage = agyUsage(), email = AGY_MAIL): Reading['agy'] => ({ state: 'found', version: '1.5.12', signInFile: true, email, usage });

export const seen = (read: Reading, ageMin: number, over: Partial<HostMemory> = {}): HostMemory => ({ reading: { read, atMs: NOW - min(ageMin) }, ...over });

export const memoryOf = (...entries: readonly (readonly [string, HostMemory])[]): Memory => new Map(entries);

export const refused = (host: string): HostStanding => ({ host, answer: 'no', how: 'person', askedUtc: '2026-11-20T10:00:00.000Z' });

export const inputOf = (over: Partial<TableInput> = {}): TableInput => ({
  hosts: [],
  memory: new Map(),
  standings: [],
  stopped: false,
  permission: { given: '2026-11-26T12:00:00.000Z' },
  round: { kind: 'idle' },
  windows: true,
  ...over,
});

/** The tab of the plan's pictures: two places of one account, a failed one, another account, a refused, a `jq`-less, wsl, an unknown account, signed out. */
export function bigInput(): TableInput {
  const memory = memoryOf(
    ['host-a', seen(reading({ claude: claude({ usage: usageOf(56, 77, 49) }), codex: codexOf(), agy: agyOf() }), 3)],
    ['host-e', seen(reading({ claude: claude({ usage: { state: 'skipped' } }) }), 4)],
    ['host-b', { failure: { sentence: '“host-b” did not answer over ssh within 30 s.', atMs: NOW - min(3) }, ...seen(reading({ claude: claude({ usage: usageOf(56, 77, 49) }) }), 25) }],
    ['host-f', seen(reading({ claude: claude({ email: 'other@example.test', org: 'Other Org', plan: 'max', usage: { state: 'skipped' } }) }), 3)],
    ['host-d', seen(reading({ jqMissing: 'apt-get' }), 3)],
    ['host-g', seen(reading({ claude: claude({ email: '', org: null, usage: usageOf(10, 20, 30) }), codex: codexOf() }), 6)],
    ['host-h', seen(reading({ claude: { state: 'signed-out' } }), 7)],
    ['host-i', seen(reading({ codex: codexOf(codexUsage({ windows: [{ minutes: 300, usedPercent: 40, resetsAtMs: null }] })) }), 8)],
  );

  return inputOf({ hosts: ['host-a', 'host-b', 'host-c', 'host-d', 'host-e', 'host-f', 'host-g', 'host-h', 'host-i', 'wsl'], memory, standings: [refused('host-c')] });
}
