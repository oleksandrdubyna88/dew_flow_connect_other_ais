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
    codexModels: [], agyModels: [],
    localEngines: {},
    server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
    side: '',
    perSide: false,
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

// ---------- the totals line says what its time IS (#116) ----------

/**
 * The spending tab ended with a bare duration: `All vendors: 1.2M tokens · $4.05 · 2.0 h`.
 *
 * <p>That 2.0 h is the sum of every reviewer run in the window across every vendor, and nothing said
 * so — read beside the per-vendor cards, each ending `38 s total · 12 s average`, it looks like it
 * could be elapsed wall-clock time for the window. It is not: three reviewers running in parallel
 * for ten minutes contribute thirty minutes to it. Issue #116 asked for the three things that make
 * it unambiguous — say it is a sum, give the average, name the vendor with the most.</p>
 */
/**
 * Today's local NOON, not "now".
 *
 * <p>The page opens on the `day` window, which is since local midnight — so a fixture stamped with
 * the ambient clock is a test that depends on what time it is run, and a run that crosses midnight
 * between building the entry and rendering the page drops it. Noon is inside today whenever the
 * suite runs. `bundledPage.test.ts` pins its own fixture the same way and for the same reason;
 * raised on the code round against the UTC rule.</p>
 */
function todayAtNoon(): string {
  const noon = new Date();
  noon.setHours(12, 0, 0, 0);
  return noon.toISOString();
}

function spent(provider: string, seconds: number, runs: number): UsageEntry[] {
  return Array.from({ length: runs }, (): UsageEntry => ({
    utc: todayAtNoon(), provider, model: 'm', role: 'PlanCritique', stage: 'PlanReview',
    seconds: seconds / runs, tokensIn: 1000, tokensOut: 100, costUsd: null, outcome: 'ok',
  }));
}

function totalsLine(entries: readonly UsageEntry[]): string {
  const page = usageTabHtml(entries, 'day', [], {});
  const at = page.indexOf('class="hint total"');
  assert.ok(at > 0, `the totals line is rendered: ${page.slice(0, 200)}`);
  return page.slice(at, page.indexOf('</div>', at));
}

test('the totals line says its time is a sum across vendors, and gives an average per run', () => {
  // codex 90 s over 3 runs, local 30 s over 1 → 120 s summed, 4 runs, 30 s average per run.
  const line = totalsLine([...spent('codex', 90, 3), ...spent('local', 30, 1)]);

  assert.match(line, /2\.0 min summed across 2 vendors/, `the sum says what it is: ${line}`);
  assert.match(line, /30 s average per run/, `and the average is per run: ${line}`);
});

test('the totals line names the vendor with the most time, and only that one', () => {
  const line = totalsLine([...spent('codex', 90, 3), ...spent('local', 30, 1)]);

  assert.match(line, /codex longest at 1\.5 min/, `the busiest vendor is named: ${line}`);
  assert.ok(!line.includes('local longest'), 'and the other one is not');
});

test('one vendor is never "the longest"', () => {
  // Proved with teeth rather than assumed: this test was watched going red with the clause made
  // unconditional. Raised on the plan round — an expected symptom of "green before and after"
  // cannot catch the regression it names.
  const line = totalsLine(spent('codex', 90, 3));

  assert.match(line, /summed across 1 vendor\b/, `still says what the time is: ${line}`);
  assert.ok(!line.includes('longest'), `naming the only vendor as the longest is noise: ${line}`);
});

test('two vendors tied on time name the same one every time', () => {
  // `>` keeps the FIRST row of equal value and the row order is the page's own (busiest by tokens),
  // so the same data always names the same vendor. Not a policy anybody would guess from the line.
  const entries = [...spent('codex', 60, 2), ...spent('local', 60, 2)];
  const once = totalsLine(entries);

  assert.equal(once, totalsLine(entries), 'the same data renders the same line');
  assert.match(once, /(codex|local) longest at 1\.0 min/, `one of them is named: ${once}`);
});

test('the totals line is built from the rows the cards are built from', () => {
  // The guarantee behind "same window, same forget-marks": the marks are applied by panelProvider
  // BEFORE this function is called, so a unit test cannot set one — what it can pin is that there is
  // one source. If the cards and the total ever read different rows, these numbers stop agreeing.
  const page = usageTabHtml([...spent('codex', 90, 3), ...spent('local', 30, 1)], 'day', [], {});
  const perCard = [...page.matchAll(/<div class="hint">([\d.]+ (?:s|min|h)) total/g)].map((m) => m[1]);

  assert.deepEqual(perCard, ['1.5 min', '30 s'], 'the cards show their own durations');
  assert.match(totalsLine([...spent('codex', 90, 3), ...spent('local', 30, 1)]), /2\.0 min summed/,
    'and the total is their sum, from the same rows');
});

test('forgetting is a command the provider must handle', () => {
  assert.ok(PANEL_COMMANDS.includes('forgetUsage'));
});

test('a vendor with nothing recorded has nothing to forget', () => {
  // The button belongs to a ROW, and a vendor with no runs has no row.
  const page = usageTabHtml([usage('codex', '2026-09-01T10:00:00Z')], 'year', [], {});

  assert.doesNotMatch(page, /data-command="forgetUsage" data-id="gemini"/);
});
