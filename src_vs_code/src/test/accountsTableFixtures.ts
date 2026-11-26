import type { AccountsRead, ClaudeUsage, CodexUsage } from '../remoteAccountsReply';
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
  ({ state: 'read', windows: [{ minutes: 300, usedPercent: 10, resetsAtMs: NOW + min(65) }, { minutes: 10_080, usedPercent: 14, resetsAtMs: NOW + min(6_000) }], asOf: { ms: NOW - hour(48), from: 'file' }, statMissing: false, ...over });

export const codexOf = (usage: CodexUsage = codexUsage()): Reading['codex'] => ({ state: 'found', signInFile: true, plan: 'prolite', usage });

export const agyOf = (): Reading['agy'] => ({ state: 'found', version: '1.5.12', signInFile: true, usage: { state: 'no-source' } });

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
