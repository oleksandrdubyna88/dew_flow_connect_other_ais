import assert from 'node:assert/strict';
import { test } from 'node:test';
import { NO_NOTES, type ProviderHealth } from '../providers';
import { type PanelState } from '../panelView';
import { everyPageHtml, paintKeys } from './panelPages';
import { DEFAULTS } from '../settingsShape';
import type { Vendor } from '../vendors';

/**
 * A probe that lands after the first paint must be able to reach the screen.
 *
 * <p>Several of the panel's answers are started by a render and never awaited — the server's
 * `--providers` verdict, the Claude CLI's model list — and each repaints by calling `render()` again
 * when it lands (`panelProvider.ts`, `refreshProviders`). That second render only repaints when the
 * paint KEY changed. The key used to be a hand-kept list of fields, and a field the page drew but the
 * list did not name was a badge that arrived in the state and never on the screen: the render ran,
 * found the key it had painted last time, and posted only the live regions
 * (`research/PLAN_settings_page.md`, F1).</p>
 *
 * <p>So the guarantee is stated from the person's side, both ways round: whatever the page would draw
 * differently, the key differs for — and whatever only a LIVE region or the page itself changes, the
 * key does not, or a five-second tick would reload the webview under whatever the person was doing.
 * Two of F1's fields are not in the table because their fixtures need a whole admin catalog or a
 * local consultant endpoint; the fix covers them by construction, since the key is now the markup.</p>
 */

const row = (over: Partial<Vendor> & Pick<Vendor, 'id' | 'runtime'>): Vendor => ({
  model: '',
  enabled: true,
  plan: true,
  code: true,
  baseUrl: '',
  executablePath: '',
  pricePerMillionIn: 0,
  pricePerMillionOut: 0,
  ...over,
});

const VENDORS: readonly Vendor[] = [
  row({ id: 'codex', runtime: 'codex', model: 'gpt-5.6' }),
  row({ id: 'antigravity', runtime: 'antigravity', model: 'gemini-3-pro' }),
  row({ id: 'claude', runtime: 'claude', model: 'sonnet' }),
  row({ id: 'remsoftdev-claude', runtime: 'remote', remoteVendor: 'claude', model: 'haiku', baseUrl: 'https://coai.example.com' }),
];

function state(over: Partial<PanelState> = {}): PanelState {
  return {
    settings: DEFAULTS,
    vendors: VENDORS,
    codexModels: [],
    agyModels: [],
    localEngines: {},
    server: { kind: 'known', version: '0.40.2', remembered: false, updateOffered: false },
    side: '',
    perSide: false,
    questions: [],
    sessions: [],
    openSections: [],
    usage: [],
    usageWindow: 'day',
    latestServerVersion: '0.40.2',
    cliStatus: {},
    cardPrices: {},
    snippetStatus: { kind: 'current', current: 14 },
    teamServers: [],
    ...over,
  };
}

const UNAVAILABLE: Record<string, ProviderHealth> = {
  'remsoftdev-claude': {
    provider: 'remsoftdev-claude',
    auth: 'unavailable',
    note: 'not signed in to the Team server at https://coai.example.com',
  },
};

/** One probe's answer arriving, and nothing else changing. */
const DRAWN: ReadonlyArray<readonly [string, Partial<PanelState>]> = [
  ['the server says a reviewer cannot run', { providers: { reported: UNAVAILABLE, asked: true, answered: true, notes: NO_NOTES } }],
  ['a newer vendor CLI is published', { cliStatus: { codex: { installed: '0.150.0', latest: '0.156.1' } } }],
  ['the Claude CLI answers which models it reaches', {
    claudeProbe: { cliVersion: '2.1.0', checkedUtc: '2026-09-28T12:00:00Z', models: [{ asked: 'opus', answered: 'claude-opus-5-5', verified: true }] },
  }],
  ['the Claude probe starts', { askingClaude: true }],
  ['Antigravity answers which models it has', { agyModels: [{ id: 'gemini-3.5-flash', label: 'gemini-3.5-flash' }] }],
  ['the price tables arrive', { cardPrices: { codex: { inPerMillion: 1.25, outPerMillion: 10, source: 'openrouter' } } }],
  ['the pasted snippet turns out to be older', { snippetStatus: { kind: 'older', behind: ['13'], current: 14 } }],
  ['the per-side switch is on', { perSide: true }],
];

for (const [what, over] of DRAWN) {
  test(`the paint key moves when ${what}`, () => {
    const before = state();
    const after = state(over);

    assert.notEqual(everyPageHtml(after, 'n', 0), everyPageHtml(before, 'n', 0), `the fixture must change what is drawn: ${what}`);
    assert.notEqual(paintKeys(after), paintKeys(before), `${what}: it landed in the state and the key did not move, so it never reaches the screen`);
  });
}

/** Changes only a live region, the page itself, or the caret can make — none of them may reload the page. */
const NOT_A_REPAINT: ReadonlyArray<readonly [string, Partial<PanelState>]> = [
  ['a question starts waiting', {
    questions: [{ id: 'q1', sessionId: 's1', repoPath: 'D:/r', branch: 'main', question: 'Proceed?', openFindings: [], askedUtc: '2026-09-28T12:00:00Z' }],
  }],
  // F10: the page opened it itself, so a key that moved with it reloaded the webview to show what was
  // already on screen, a few seconds later, dropping the scroll position and any open dropdown.
  ['the person opens a section', { openSections: ['bugz'] }],
  ['the page reports where the caret is', { focus: { id: 'model|codex||', start: 2, end: 2 } }],
];

for (const [what, over] of NOT_A_REPAINT) {
  test(`the paint key stays put when ${what}`, () => {
    assert.equal(paintKeys(state(over)), paintKeys(state()), `${what}: that would reload the whole webview on the next tick`);
  });
}

test('the same answer built in another order is the same paint key', () => {
  const two: Record<string, ProviderHealth> = {
    ...UNAVAILABLE,
    codex: { provider: 'codex', auth: 'own sign-in', note: '' },
  };
  const reversed: Record<string, ProviderHealth> = Object.fromEntries(Object.entries(two).reverse());

  assert.equal(
    paintKeys(state({ providers: { reported: reversed, asked: true, answered: true, notes: NO_NOTES } })),
    paintKeys(state({ providers: { reported: two, asked: true, answered: true, notes: NO_NOTES } })),
  );
});

test('a section the person opened is still drawn open by a repaint made for another reason', () => {
  assert.match(everyPageHtml(state({ openSections: ['bugz'] }), 'n', 0), /data-section="bugz" open/);
});
