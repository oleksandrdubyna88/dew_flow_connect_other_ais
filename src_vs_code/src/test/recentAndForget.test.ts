import { SNIPPET_VERSION } from '../claudeSnippet';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { usageTabHtml } from '../roundsLog';
import { CliStatus } from '../cliVersions';
import { panelHtml, PANEL_COMMANDS } from '../panelView';
import { RoundRecord, SessionFile } from '../rounds';
import { DEFAULTS } from '../settingsShape';
import { UsageEntry } from '../usage';
import { DEFAULT_VENDORS } from '../vendors';

/**
 * Two things the panel was asked for from in front of it.
 *
 * <p><b>The rounds section shows what is RUNNING.</b> It was a top-six, then a 72-hour window of
 * finished rounds with disclosures — and the disclosures are where the flicker lived. Ruled on
 * 2026-09-05: the sidebar answers "what is happening now", the history is a log with a table
 * (`PLAN_rounds_log_view.md`). What this file keeps from the window era is the one rule that still
 * holds — a round in flight is shown whatever its age — and the scroll container.</p>
 *
 * <p><b>A vendor's spending row can be forgotten.</b> `gemini` sits in the ledger with two failed
 * runs and nothing else, because it was retired mid-flight; there was no way to clear it. Forgetting
 * a vendor drops its recorded runs and the row goes; the next run it makes brings the row back,
 * because the ledger is what the section reads and forgetting is about the PAST, not about hiding a
 * vendor.</p>
 */

const NOW = Date.parse('2026-09-01T20:00:00Z');

function round(over: Partial<RoundRecord> = {}): RoundRecord {
  return {
    stage: 'PlanReview',
    number: 1,
    verdict: 'proceed',
    gatingCount: 0,
    reviewers: 'all 2 reviewers answered',
    completedUtc: '2026-09-01T19:00:00Z',
    status: 'done',
    ...over,
  };
}

function session(rounds: readonly RoundRecord[]): SessionFile {
  return {
    state: { repoPath: 'D:/repo', branch: 'main', stage: 'PlanReview', sessionId: 'a', awaitingResolve: false },
    rounds,
  } as SessionFile;
}

function usage(provider: string, atUtc: string): UsageEntry {
  return {
    utc: atUtc,
    provider,
    role: 'PlanCritique',
    tokensIn: 1000,
    tokensOut: 100,
    costUsd: null,
    seconds: 10,
    outcome: 'ok',
  } as UsageEntry;
}

function html(over: {
  sessions?: readonly SessionFile[];
  usage?: readonly UsageEntry[];
  cliStatus?: Record<string, CliStatus>;
} = {}): string {
  return panelHtml({
    settings: DEFAULTS,
    vendors: DEFAULT_VENDORS,
    codexModels: [],
    localEngines: {},
    server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
    side: '',
    latestServerVersion: '',
    questions: [],
    sessions: over.sessions ?? [],
    openSections: [],
    usage: over.usage ?? [],
    usageWindow: 'week',
    cliStatus: over.cliStatus ?? {},
    modelPrices: {},
    snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
  }, 'nonce', NOW);
}

test('finished rounds are not in the sidebar, however recent — the log has them', () => {
  const rounds = Array.from({ length: 8 }, (_, i) =>
    round({ number: i + 1, subject: `round-${i + 1}`, completedUtc: `2026-09-01T${String(10 + i).padStart(2, '0')}:00:00Z` }));

  const page = html({ sessions: [session(rounds)] });

  for (let i = 1; i <= 8; i += 1) {
    assert.doesNotMatch(page, new RegExp(`round-${i}\b`), `round-${i} finished an hour ago and is not the sidebar's business`);
  }
  assert.match(page, /Nothing is running/);
});

test('a round still running is shown whatever its age', () => {
  // It has no completion time, and it is the one row somebody is actually waiting on.
  const page = html({ sessions: [session([
    round({ subject: 'in-flight', status: 'running', verdict: 'running', completedUtc: '' }),
  ])] });

  assert.match(page, /in-flight/);
});

test('the list scrolls instead of pushing the rest of the panel away', () => {
  const page = html({ sessions: [session([round({ subject: 'one' })])] });

  assert.match(page, /#live-rounds\s*\{[^}]*overflow-y: auto/, 'no scroll container');
  assert.match(page, /#live-rounds\s*\{[^}]*max-height/, 'nothing caps its height');
});

test('the empty state says where the rounds went, not just "nothing"', () => {
  const page = html({ sessions: [session([round({ completedUtc: '2026-08-01T10:00:00Z' })])] });

  assert.match(page, /Show review rounds/, 'a person with a month of history must not read that their work is gone');
});

test('every vendor with recorded spending offers to forget it', () => {
  // On the rounds log page's spending tab since 2026-09-05 — the sidebar section is gone.
  const page = usageTabHtml([usage('codex', '2026-09-01T10:00:00Z'), usage('gemini', '2026-09-01T11:00:00Z')], 'year', [], {});

  for (const id of ['codex', 'gemini']) {
    assert.match(
      page,
      new RegExp(`data-command="forgetUsage" data-id="${id}"`),
      `${id} has no way to clear its counters`,
    );
  }
});

test('forgetting is a command the provider must handle', () => {
  assert.ok(PANEL_COMMANDS.includes('forgetUsage'));
});

test('a vendor with nothing recorded has nothing to forget', () => {
  // The button belongs to a ROW, and a vendor with no runs has no row.
  const page = usageTabHtml([usage('codex', '2026-09-01T10:00:00Z')], 'year', [], {});

  assert.doesNotMatch(page, /data-command="forgetUsage" data-id="gemini"/);
});
