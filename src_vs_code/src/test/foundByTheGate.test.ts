import { SNIPPET_VERSION } from '../claudeSnippet';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { discoverEngine, LocalEngine, noEngine } from '../localEngines';
import { panelHtml, PanelState, staticKey } from '../panelView';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS } from '../vendors';

/**
 * The defects this product's own review gate found in it, one test each.
 *
 * <p>Five models reviewed commit `2b7d3ab` under the operator's settings on 2026-09-02 and named
 * eight real defects nobody knew about — `research/RESULTS_findings_that_are_worth_something.md`.
 * Four of them are on this side of the wire. Each test below was watched fail against the unfixed
 * code, and the failure message is recorded beside it.</p>
 */

const engine = (over: Partial<LocalEngine> = {}): LocalEngine => ({
  kind: 'ollama',
  probeUrl: 'http://127.0.0.1:11434',
  apiBaseUrl: 'http://127.0.0.1:11434/v1',
  reachable: true,
  status: '0.14.2',
  models: [{ id: 'qwen3.5:35b', detail: '' }],
  ...over,
});

const state = (over: Partial<PanelState> = {}): PanelState => ({
  settings: DEFAULTS,
  vendors: DEFAULT_VENDORS,
  codexModels: [], agyModels: [],
  localEngines: {},
  server: { kind: 'known', version: '0.12.0', remembered: false, updateOffered: false },
  side: '',
  latestServerVersion: '0.12.0',
  questions: [],
  openSections: ['reviewers'],
  sessions: [],
  usage: [],
  usageWindow: 'week',
  cliStatus: {},
  modelPrices: {},
  snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
  ...over,
});

// ---------------------------------------------------------------- flash, alone

test('a reprobe that finds different models repaints the panel', () => {
  // `staticKey` decides repaint-or-patch, and anything missing from it is a control that can never
  // change. The local model list was missing: pressing ⟳ probed the engine, got a new list, and
  // the picker kept showing the old one for as long as the panel stayed open. This is the exact
  // defect class `liveRepaint.test.ts` was written for, in the one field added after it.
  //
  // RED: Expected the key to change when the model list did, but both were identical.
  const before = staticKey(state({ localEngines: {} }));
  const after = staticKey(state({ localEngines: { local: engine() } }));

  assert.notEqual(before, after, 'a new model list must repaint, or the picker is frozen');
});

test('an engine that went away also repaints', () => {
  const up = staticKey(state({ localEngines: { local: engine() } }));
  const down = staticKey(state({ localEngines: { local: noEngine('connection refused') } }));

  assert.notEqual(up, down);
});

// ---------------------------------------------------------------- luna, alone

test('discovery says what actually happened, not always "connection refused"', async () => {
  // Every candidate's reason is computed and then thrown away by the final return, so a firewall
  // that swallows the connection, an engine wedged mid-answer and a port with nothing on it were
  // all reported to a person as "connection refused" — three different actions, one sentence.
  //
  // RED: Expected the reason to name the ports it tried, but found "connection refused".
  const nothingListens = ['http://127.0.0.1:9', 'http://127.0.0.1:7'];

  const found = await discoverEngine(nothingListens, 400);

  assert.equal(found.reachable, false);
  assert.match(found.status, /9/, 'the first candidate is not named');
  assert.match(found.status, /7/, 'the second candidate is not named');
});

// ---------------------------------------------------------------- gemma, alone

test('a vendor somebody named themselves can be given the endpoint it exists for', () => {
  // The "Another OpenAI-compatible endpoint" preset ships with an empty `baseUrl`, and the field
  // was hidden while `baseUrl` was empty — so the one preset whose entire purpose is to be given a
  // base URL was the one that could not be given one. A field that appears only after it is filled
  // cannot be filled.
  //
  // RED: Expected the HTML to contain a baseUrl input for "mycompany", found none.
  const html = panelHtml(
    state({
      vendors: [{ id: 'mycompany', runtime: 'codex', model: '', enabled: true, baseUrl: '',
                  executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 }],
    }),
    'n0nce',
  );

  assert.ok(html.includes('data-setting="baseUrl" data-vendor="mycompany"'), 'no endpoint field');
});

test('the shipped vendors that need no endpoint are not asked for one', () => {
  // The other half of the rule: codex, claude, gemini and antigravity know where they go, and a
  // blank URL box under each of them is four invitations to break a working reviewer.
  const html = panelHtml(state(), 'n0nce');

  for (const id of ['codex', 'claude']) {
    assert.ok(
      !html.includes(`data-setting="baseUrl" data-vendor="${id}"`),
      `${id} ships knowing its endpoint and must not be asked for one`,
    );
  }
});

// ---------------------------------------------------------------- sonnet, alone

test('two local reviewers on two engines show two different model lists', () => {
  // One `state.localEngine` was probed from `vendors.find(v => v.runtime === 'local')` and handed
  // to EVERY card, so a second local reviewer on another port displayed the first one's models —
  // and picking one of them sent a model the second engine does not have.
  //
  // RED: Expected the card for "local-b" to offer gpt-oss:120b, found qwen3.5:35b.
  const html = panelHtml(
    state({
      vendors: [
        { id: 'local-a', runtime: 'local', model: '', enabled: true, baseUrl: 'http://127.0.0.1:11434/v1',
          executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 },
        { id: 'local-b', runtime: 'local', model: '', enabled: true, baseUrl: 'http://127.0.0.1:8000/v1',
          executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 },
      ],
      localEngines: {
        'local-a': engine(),
        'local-b': engine({ kind: 'vllm', models: [{ id: 'gpt-oss:120b', detail: '' }] }),
      },
    }),
    'n0nce',
  );

  const cardB = html.slice(html.indexOf('local-b'));
  assert.ok(cardB.includes('gpt-oss:120b'), 'the second engine’s own models are not offered');
  assert.ok(!cardB.includes('qwen3.5:35b'), 'the first engine’s models leaked into the second card');
});
