import { LedgerLine } from '../roundMoney';
import { RoundCountsRead, parseRoundCounts } from '../roundCounts';
import { RoundsSnapshot } from '../roundCountsCache';
import { UsageEntry } from '../usage';

/**
 * One `--round-counts` answer in the mode's own JSON shape (`src_mcp/src/Server/RoundCountsMode.cs`), parsed through the
 * REAL parser — a fixture the parser refused would leave every test below asserting the unavailable path.
 */

export const NOW = Date.parse('2026-10-10T12:00:00Z');

const at = (hoursAgo: number): string => new Date(NOW - hoursAgo * 3_600_000).toISOString();

/** codex: 2 rounds in 5 h, 5 in 24 h, 14 in 7 d, on two models (the step of 2026-10-02 shape); gemini: 7 d only; a Team-server row. */
export const ANSWER_JSON = JSON.stringify({
  nowUtc: at(0),
  windows: [
    { name: '5h', fromUtc: at(5), toUtc: at(0), rows: [
      { provider: 'codex', rounds: { plan: 1, code: 1, document: 0, feature: 0 }, reviewers: 5, models: [{ model: 'gpt-6-luna', rounds: 2 }] },
    ] },
    { name: '24h', fromUtc: at(24), toUtc: at(0), rows: [
      { provider: 'codex', rounds: { plan: 2, code: 3, document: 0, feature: 0 }, reviewers: 14, models: [{ model: 'gpt-6-luna', rounds: 5 }] },
      { provider: 'work-codex', rounds: { plan: 0, code: 2, document: 0, feature: 0 }, reviewers: 8, models: [] },
    ] },
    { name: '7d', fromUtc: at(168), toUtc: at(0), rows: [
      { provider: 'codex', rounds: { plan: 5, code: 8, document: 1, feature: 0 }, reviewers: 42,
        models: [{ model: 'gpt-6-luna', rounds: 9 }, { model: 'gpt-6-terra', rounds: 5 }] },
      { provider: 'gemini', rounds: { plan: 3, code: 0, document: 0, feature: 0 }, reviewers: 3, models: [] },
      { provider: 'work-codex', rounds: { plan: 0, code: 2, document: 0, feature: 0 }, reviewers: 8, models: [] },
    ] },
  ],
  modelsKnownSince: at(200),
});

/** The ids of the merged fixture's two stores and its two round keys (the 4.4 document of todo/PLAN_the_round_counter_tail_E6.md). */
export const THIS_STORE = 'a1b2c3d4e5f60718293a4b5c6d7e8f90';
export const WSL_STORE = '3f9c2b7e1a4d5c6f8e9a0b1c2d3e4f50';
export const KEY_A = '9b1f6e2d4c8a7b5e3f1d0c9a8b7e6d5c';
export const KEY_B = '0c7a1e5d9b3f2a8c6e4d1b0f9a8c7e6d';
export const WSL_SNAPSHOT = '\\\\wsl.localhost\\Ubuntu\\home\\me\\.local\\share\\coai-mcp\\round-counts-snapshot.json';
export const WSL_SNAPSHOT_OLD_SPELLING = '\\\\wsl$\\Ubuntu\\home\\me\\.local\\share\\coai-mcp\\round-counts-snapshot.json';

const MERGED_ROW = {
  provider: 'codex',
  rounds: { plan: 1, code: 2, document: 0, feature: 0 },
  reviewers: 7,
  models: [{ model: 'gpt-6-luna', rounds: 3 }],
  roundKeys: [KEY_A, KEY_B],
  keyStore: { [KEY_A]: THIS_STORE, [KEY_B]: WSL_STORE },
  roundsWithoutKey: 1,
  parts: [
    { store: THIS_STORE, rounds: { plan: 1, code: 1, document: 0, feature: 0 }, reviewers: 4 },
    { store: WSL_STORE, rounds: { plan: 0, code: 1, document: 0, feature: 0 }, reviewers: 3 },
  ],
};

/** A `--round-counts-merged` answer in the 4.4 document's shape: two stores (this side live, WSL from a snapshot), three paths not included. */
export const MERGED_JSON = JSON.stringify({
  nowUtc: at(0),
  windows: [
    { name: '5h', fromUtc: at(5), toUtc: at(0), rows: [MERGED_ROW] },
    { name: '24h', fromUtc: at(24), toUtc: at(0), rows: [MERGED_ROW] },
    { name: '7d', fromUtc: at(168), toUtc: at(0), rows: [MERGED_ROW] },
  ],
  modelsKnownSince: at(200),
  gauge: { ratios: [], unavailable: 'local-capture-deferred' },
  stores: [
    { id: THIS_STORE, live: true, platform: 'windows', host: 'windows', wslDistro: '', side: '', directory: 'C:\\Users\\me\\AppData\\Local\\coai-mcp',
      paths: [], asOf: at(0), quiet: false, truncated: false, coaiMcpVersion: '0.63.0' },
    { id: WSL_STORE, live: false, platform: 'linux', host: 'wsl', wslDistro: 'Ubuntu', side: '', directory: '/home/me/.local/share/coai-mcp',
      paths: [WSL_SNAPSHOT, WSL_SNAPSHOT_OLD_SPELLING], asOf: at(0.05), quiet: false, truncated: false, coaiMcpVersion: '0.63.0' },
  ],
  storesNotIncluded: [
    { path: 'D:\\coai-nas\\round-counts-snapshot.json', reason: 'missing', detail: 'D:\\coai-nas\\round-counts-snapshot.json' },
    { path: '\\\\wsl.localhost\\Debian\\home\\me\\.local\\share\\coai-mcp\\round-counts-snapshot.json', reason: 'newer-schema',
      detail: 'schema 2 — this coai-mcp reads schema 1; update coai-mcp on this side' },
    { path: 'E:\\x\\round-counts-snapshot.json', reason: 'a-word-from-the-future', detail: 'kept as text' },
  ],
});

export function counted(json: string = ANSWER_JSON): RoundCountsRead {
  const answer = parseRoundCounts(json);
  if (answer === undefined) {
    throw new Error('the fixture is not the contract — the parser refused it');
  }

  return { state: 'counted', answer };
}

export const UNAVAILABLE: RoundCountsRead = { state: 'unavailable', why: 'this coai-mcp is older than --round-counts' };

export function usageLine(over: Partial<UsageEntry> = {}): UsageEntry {
  return {
    utc: at(1), provider: 'codex', model: 'gpt-6-luna', role: 'Architecture', stage: 'CodeReview', seconds: 30,
    tokensIn: 1_000_000, tokensOut: 2_000, costUsd: null, outcome: 'ok', tokensCached: 0, tokensReasoning: 0, kind: 'review', ...over,
  };
}

export function hoursAgo(hours: number): string {
  return at(hours);
}

export function snapshot(read: RoundCountsRead, ledger: readonly UsageEntry[] = [usageLine()], ledgerFailure = ''): RoundsSnapshot {
  return { read, ledger, ledgerFailure, atMs: NOW };
}

export const NO_CHAT: readonly LedgerLine[] = [];
