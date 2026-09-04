import assert from 'node:assert/strict';
import { test } from 'node:test';
import { panelHtml, PanelState, roundKey, liveRegions } from '../panelView';
import { reviewerLines, RoundRecord, SessionFile } from '../rounds';
import { DEFAULTS } from '../settingsShape';
import { vendorColour } from '../vendorColour';
import { SNIPPET_VERSION } from '../claudeSnippet';

/**
 * A round is closed to a line and opens to its reviewers.
 *
 * <p>Two complaints, one shape: the panel showed the reviewers only while a round RAN, so the view
 * was richest about what you could still watch and poorest about what you came back to understand;
 * and the round's own "11m 2s" says nothing about which of nine reviewers spent the eleven
 * minutes.</p>
 */

function round(over: Partial<RoundRecord> = {}): RoundRecord {
  return {
    stage: 'CodeReview',
    number: 1,
    verdict: 'proceed',
    gatingCount: 5,
    reviewers: '7 of 9 reviewers answered',
    status: 'done',
    startedUtc: '2026-09-03T16:03:24.000Z',
    completedUtc: '2026-09-03T16:14:26.000Z',
    tokensIn: 219667,
    tokensOut: 9422,
    subject: 'SCOPE — the shim probe',
    reviewerStates: [
      { provider: 'codex', role: 'Architecture', status: 'done', findings: 0, note: '', seconds: 38.7, tokensIn: 46293, tokensOut: 1633 },
      { provider: 'local', role: 'SecurityReliability', status: 'failed', note: 'the engine did not finish in time', findings: 0, seconds: 590 },
    ],
    ...over,
  } as RoundRecord;
}

function state(rounds: readonly RoundRecord[], openRounds: readonly string[] = []): PanelState {
  const session: SessionFile = {
    state: { sessionId: 's1', repoPath: 'D:/repo', branch: 'main', stage: 'CodeReview', awaitingResolve: false },
    rounds: [...rounds],
  } as unknown as SessionFile;

  return {
    settings: DEFAULTS,
    vendors: [],
    codexModels: [],
    localEngines: {},
    server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
    side: '',
    latestServerVersion: '',
    questions: [],
    sessions: [session],
    openSections: ['rounds'],
    openRounds,
    usage: [],
    usageWindow: 'week',
    cliStatus: {},
    modelPrices: {},
    snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
  };
}

/** The markup one vendor name renders as, so a test says what it means rather than repeating it. */
function vendorSpan(vendor: string): string {
  return ["<span class=\"who\" style=\"color:", vendorColour(vendor), "\">", vendor, "</span>"].join("");
}

const NOW = Date.parse('2026-09-03T16:20:00.000Z');

test('a finished round keeps its reviewers instead of throwing them away', () => {
  const key = roundKey({ ...round(), branch: 'main' });
  const html = panelHtml(state([round()], [key]), 'n', NOW);

  assert.ok(html.includes('<details class="round"'), 'the card is a disclosure');
  // The vendor's word now carries its own colour, so a row is a span plus the rest of the sentence
  // rather than one string. Same content; the assertion also says where the colour stops.
  assert.ok(
    html.includes(`${vendorSpan('codex')}/Architecture — done`),
    'the reviewers of a FINISHED round are there',
  );
  assert.ok(html.includes(`${vendorSpan('local')}/SecurityReliability — failed`));
});

test('a closed card carries no reviewer rows at all', () => {
  // Not cosmetic: this list is rebuilt every five seconds, and nine rows per round for every round
  // in a 72-hour window is work nobody can see. The provider repaints on the toggle, so the body is
  // there by the time the card is open. (codex, this change's code round.)
  const html = panelHtml(state([round()]), 'n', NOW);

  assert.ok(html.includes('<details class="round"'));
  assert.ok(!html.includes('class="reviewer"'), 'a closed card builds nothing');
});

test('the closed summary is the line it has always been', () => {
  const html = panelHtml(state([round()]), 'n', NOW);
  const card = html.slice(html.indexOf('<details class="round"'));
  const summary = card.slice(card.indexOf('<summary>'), card.indexOf('</summary>'));

  assert.ok(summary.includes('SCOPE — the shim probe'), 'the subject leads');
  assert.ok(summary.includes('code review 1 · main'));
  assert.ok(summary.includes('proceed'));
  assert.ok(summary.includes('5 gating'));
  assert.ok(summary.includes('11m 2s'), 'the round total stays where it was');
  assert.ok(summary.includes('220k in / 9.4k out'));
});

test('each reviewer says how long it took and what it read', () => {
  const lines = reviewerLines(round());

  assert.equal(lines[0], 'codex/Architecture — done (0 findings, 39 s, 46k in / 1.6k out)');
  assert.ok(lines[1]!.includes('9.8 min'), lines[1]);
  assert.ok(lines[1]!.includes('the engine did not finish in time'), 'a failure still says why');
});

test('a duration that is not a number is no duration', () => {
  // A session file is JSON somebody else wrote. "NaN s" reads as a broken panel rather than as a
  // missing measurement.
  const broken = round({
    reviewerStates: [
      { provider: 'codex', role: 'Architecture', status: 'done', findings: 1, note: '', seconds: Number.NaN },
    ],
  });

  assert.equal(reviewerLines(broken)[0], 'codex/Architecture — done (1 finding)');
});

test('a queued reviewer says what it is waiting for', () => {
  // The card is shared with every other window on this machine, so "queued" alone cannot tell ten
  // seconds from ten minutes. The server counts who is on the engine and how long its runs take.
  const waiting = round({
    reviewerStates: [
      { provider: 'local', role: 'Architecture', status: 'queued', findings: 0, note: '2 ahead on this engine, about 4 min' },
    ],
  });

  assert.equal(
    reviewerLines(waiting)[0],
    'local/Architecture — queued (2 ahead on this engine, about 4 min)',
  );
});

test('a round from an older server says nothing about time or tokens', () => {
  // Absent is not zero: a server that never recorded these wrote no number, and printing "0s"
  // would be a measurement nobody made.
  const older = round({
    reviewerStates: [{ provider: 'gemini', role: 'UxDxPerformance', status: 'done', findings: 2, note: '' }],
  });

  assert.equal(reviewerLines(older)[0], 'gemini/UxDxPerformance — done (2 findings)');
});

test('a card the person opened is rendered open, and one they have not is not', () => {
  const key = roundKey({ ...round(), branch: 'main' });
  const closed = panelHtml(state([round()]), 'n', NOW);
  const open = panelHtml(state([round()], [key]), 'n', NOW);

  assert.ok(!closed.includes(`data-round="${key}" open`), 'nothing opens itself');
  assert.ok(open.includes(`data-round="${key}" open`), 'the open set is what decides');
});

test('the open state survives a live patch, which is what the five-second tick does', () => {
  // The failure this prevents: the person opens a round, the tick replaces the list, and the card
  // shuts under them. Raised as Blocking against the plan, so it is asserted on the PATCH itself
  // rather than on the first paint.
  const key = roundKey({ ...round(), branch: 'main' });
  const patched = liveRegions(state([round()], [key]), NOW);

  assert.ok(patched.rounds.includes(`data-round="${key}" open`));
});

test('two rounds of one number on two branches are two cards', () => {
  const mine = roundKey({ ...round(), branch: 'main' });
  const theirs = roundKey({ ...round(), branch: 'feat/other' });
  const rerun = roundKey({ ...round(), branch: 'main', startedUtc: '2026-09-03T18:00:00.000Z' });

  assert.notEqual(mine, theirs, 'a branch is part of what a round IS');
  assert.notEqual(mine, rerun, 'a re-run does not inherit the open state of the round it replaces');
});

test('the rounds list is twice as tall as it was', () => {
  const html = panelHtml(state([round()]), 'n', NOW);

  assert.ok(html.includes('#live-rounds { max-height: 640px'), 'five rounds used to fill a sidebar with room to spare');
});

test('a round that recorded no reviewers still opens into a sentence', () => {
  const bare = round({ reviewerStates: [] });
  const html = panelHtml(state([bare], [roundKey({ ...bare, branch: 'main' })]), 'n', NOW);

  assert.ok(html.includes('recorded no reviewer detail'), 'an empty disclosure would read as broken');
});
