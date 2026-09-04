import assert from 'node:assert/strict';
import { test } from 'node:test';
import { panelHtml, PanelState, roundKey, roundsBody } from '../panelView';
import { RoundRecord, SessionFile } from '../rounds';
import { DEFAULTS } from '../settingsShape';
import { SNIPPET_VERSION } from '../claudeSnippet';

/**
 * A card that cannot be opened must not offer to open.
 *
 * <p>Reported looking at the list: some rounds expand and some do not, and both show the hand
 * cursor. The ones that do not are rounds a server older than `reviewerStates` recorded — there is
 * nothing to show and there never will be. Offering a hand over them is the panel promising
 * something it has not got, and clicking one used to open a card containing a sentence apologising
 * for itself.</p>
 */

function round(over: Partial<RoundRecord> = {}): RoundRecord {
  return {
    stage: 'CodeReview',
    number: 1,
    verdict: 'proceed',
    gatingCount: 2,
    reviewers: 'all 3 reviewers answered',
    status: 'done',
    startedUtc: '2026-09-04T16:03:24.000Z',
    completedUtc: '2026-09-04T16:04:26.000Z',
    subject: 'SCOPE — something',
    reviewerStates: [
      { provider: 'codex', role: 'Architecture', status: 'done', findings: 1, note: '' },
    ],
    ...over,
  } as RoundRecord;
}

function session(rounds: readonly RoundRecord[]): SessionFile {
  return {
    state: { sessionId: 's1', repoPath: 'D:/repo', branch: 'main', stage: 'CodeReview', awaitingResolve: false },
    rounds: [...rounds],
  } as unknown as SessionFile;
}

const NOW = Date.parse('2026-09-04T16:20:00.000Z');

test('a round with reviewers is a disclosure', () => {
  const html = roundsBody([session([round()])], [], NOW);

  assert.match(html, /<details class="round"/);
});

test('a round with nothing to show is a LINE, not a disclosure', () => {
  // No `<details>` means no toggle, no marker and — with `.round.flat` — no hand cursor.
  const html = roundsBody([session([round({ reviewerStates: [] })])], [], NOW);

  assert.ok(!html.includes('<details class="round"'), 'nothing to open must not look openable');
  assert.match(html, /<div class="round flat"/);
});

test('the flat card still says everything the summary said', () => {
  const html = roundsBody([session([round({ reviewerStates: [] })])], [], NOW);

  assert.ok(html.includes('SCOPE — something'));
  assert.ok(html.includes('proceed'));
  assert.ok(html.includes('2 gating'));
});

test('only the openable card gets a pointer', () => {
  const css = panelHtml(state([round()]), 'n', NOW).split('</style>')[0] ?? '';

  assert.match(css, /details\.round > summary \{[^}]*cursor: pointer/);
  assert.match(css, /\.round\.flat \{[^}]*cursor: default/);
});

test('an opened card shows something while its reviewers are being read', () => {
  // The body is built by the provider, so there is always a gap between the click and the rows.
  // An empty card during it reads as a card with nothing in it.
  const key = roundKey({ ...round(), branch: 'main' });
  const html = roundsBody([session([round({ reviewerStates: [] , status: 'running' })])], [key], NOW);

  assert.ok(html.includes('Reading this round…') || html.includes('class="reviewer"'));
});

function state(rounds: readonly RoundRecord[]): PanelState {
  return {
    settings: DEFAULTS,
    vendors: [],
    codexModels: [],
    localEngines: {},
    server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
    side: '',
    latestServerVersion: '',
    questions: [],
    sessions: [session(rounds)],
    openSections: ['rounds'],
    openRounds: [],
    usage: [],
    usageWindow: 'week',
    cliStatus: {},
    modelPrices: {},
    snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
  };
}
