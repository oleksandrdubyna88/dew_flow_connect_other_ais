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

export function snapshot(read: RoundCountsRead, ledger: readonly UsageEntry[] = [usageLine()]): RoundsSnapshot {
  return { read, ledger, atMs: NOW };
}

export const NO_CHAT: readonly LedgerLine[] = [];
