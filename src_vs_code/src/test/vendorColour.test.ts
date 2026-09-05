import assert from 'node:assert/strict';
import { test } from 'node:test';
import { vendorColour, VENDOR_PALETTE } from '../vendorColour';
import { panelHtml, PanelState } from '../panelView';
import { RoundRecord, SessionFile } from '../rounds';
import { DEFAULTS } from '../settingsShape';
import { SNIPPET_VERSION } from '../claudeSnippet';

/**
 * A vendor's name carries one colour, and it is the same colour everywhere.
 *
 * <p>Reported looking at a round card: in `codex/PlanCritique — running` the one word that says WHO
 * is the same grey as the rest of the row. The requirement is not "make it colourful" — it is that
 * `codex` is the SAME colour in every log, so a nine-reviewer list can be scanned by colour before
 * it is read.</p>
 */

test('one vendor keeps one colour', () => {
  assert.equal(vendorColour('codex'), vendorColour('codex'));
});

test('the colour is derived from the name, not from arrival order', () => {
  // The property that matters across views and across restarts: nothing about WHEN a vendor was
  // first seen can change its colour, because nothing but the name is an input.
  const first = vendorColour('gemini');
  vendorColour('codex');
  vendorColour('local');
  assert.equal(vendorColour('gemini'), first);
});

test('case and stray space are the same vendor', () => {
  assert.equal(vendorColour(' Codex '), vendorColour('codex'));
});

test('the three vendors here do not collide', () => {
  const colours = new Set(['codex', 'gemini', 'local'].map(vendorColour));
  assert.equal(colours.size, 3, 'a colour that says nothing apart is worse than none');
});

test('every colour is a theme variable, never a hex value', () => {
  const names = ['codex', 'gemini', 'local', 'deepseek', 'my-claude', 'antigravity', 'qwen', ''];
  for (const name of names) {
    assert.match(vendorColour(name), /^var\(--vscode-[a-z-]+\)$/, `${name} must use a theme colour`);
  }
});

test('a vendor with no name is the ordinary foreground', () => {
  assert.equal(vendorColour('   '), 'var(--vscode-foreground)');
});

test('the palette is what the function can return', () => {
  const returned = new Set(Array.from({ length: 200 }, (_, i) => vendorColour(`vendor-${i}`)));
  for (const colour of returned) {
    assert.ok(VENDOR_PALETTE.includes(colour), `${colour} is outside the declared palette`);
  }
});

// ---------- through the page ----------

function running(): SessionFile {
  const round: RoundRecord = {
    stage: 'PlanReview',
    number: 1,
    verdict: '',
    gatingCount: 0,
    reviewers: '',
    status: 'running',
    startedUtc: new Date().toISOString(),
    completedUtc: '',
    reviewerStates: [
      { provider: 'codex', role: 'PlanCritique', status: 'running', findings: 0, note: '' },
      { provider: '<script>', role: 'PlanCritique', status: 'running', findings: 0, note: '' },
    ],
  };

  return {
    state: { sessionId: 'a1', repoPath: 'C:/repo', branch: 'main', stage: 'PlanReview', awaitingResolve: false },
    rounds: [round],
  };
}

function state(): PanelState {
  return {
    settings: DEFAULTS,
    vendors: [],
    codexModels: [],
    localEngines: {},
    server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
    side: '',
    latestServerVersion: '',
    questions: [],
    sessions: [running()],
    openSections: ['rounds'],
    usage: [],
    usageWindow: 'week',
    cliStatus: {},
    modelPrices: {},
    snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
  };
}
test('the vendor word is coloured and the rest of the row is not', () => {
  const html = panelHtml(state(), 'n0nce', Date.now());

  assert.ok(
    html.includes(`<span class="who" style="color:${vendorColour('codex')}">codex</span>/PlanCritique`),
    'the colour stops at the vendor name',
  );
});

test('a vendor name from a session file is still escaped', () => {
  // The name comes out of JSON somebody else wrote. A colour is no reason to stop escaping it.
  const html = panelHtml(state(), 'n0nce', Date.now());

  assert.ok(!html.includes('<span class="who" style="color:var(--vscode-charts-blue)"><script>'));
  assert.ok(html.includes('&lt;script&gt;</span>'), 'the name is escaped inside the coloured span');
});
