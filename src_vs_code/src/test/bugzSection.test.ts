import assert from 'node:assert/strict';
import { test } from 'node:test';

import { panelHtml, staticKey, type PanelState } from '../panelView';
import { SNIPPET_VERSION } from '../claudeSnippet';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS } from '../vendors';
import { EMPTY_CORPUS, hasRun, isRunning, parseBugs, type BugCorpus, type CollectRun } from '../roundsDb';
import { RANKING_VENDORS, collectLabel, lastRunLine, mayRank } from '../bugzView';

/**
 * The Bugz section — its pure decisions, and its controls RUN rather than read.
 *
 * <p><b>Why the page is executed here.</b> `.agents/PROJECT.md` refuses a new behavioural assertion
 * over page source text, and this file earned that ruling the hard way: its first version matched
 * regexes against the assembled HTML for `data-command="collectBugs"` and for a `disabled`
 * attribute. Every one of them passed while the picker beside them was wired to nothing — a person
 * could choose a model, press Collect, and the run would record no model at all, because the
 * provider read a private field the webview never wrote to. Three reviewers found it; no assertion
 * over markup could.</p>
 *
 * <p>So the controls are proved by running the panel's own script and reading what it POSTS, and the
 * state the section shows is built from a corpus — because the run's state is persisted and read
 * back, never held in the page.</p>
 */

const RUN: CollectRun = {
  id: '20260916T090000-abc123',
  startedUtc: '2026-09-16T09:00:00Z',
  finishedUtc: '',
  state: 'running',
  model: 'local/qwen',
  candidates: 40,
  picked: 20,
  collected: 3,
  skipped: 1,
  failed: 0,
  reasons: '',
};

const corpus = (lastRun: CollectRun): BugCorpus => ({
  funnel: { all: 100, onCode: 80, accepted: 60, gating: 50, runtime: 40, located: 40, unprocessed: 20 },
  lastRun,
  read: true,
});

/**
 * A whole panel state, typed and with no cast.
 *
 * <p>The first version ended `as unknown as PanelState`, which two reviewers refused by the
 * TypeScript doctrine — an `as` cast is a promise to maintain a shape by hand, and it comes due
 * silently. The proof arrived the same afternoon from a different direction: adding two settings
 * broke `settingsReach.test.ts`, which uses a typed literal, and would have passed straight through
 * a cast.</p>
 */
const state = (over: Partial<PanelState> = {}): PanelState => ({
  settings: DEFAULTS,
  vendors: DEFAULT_VENDORS,
  agyModels: [],
  codexModels: [],
  // A real local engine, because the section renders a picker only when there is something local
  // to offer — the ranking pass reads findings that are not anonymised, so a machine with no local
  // engine is offered nothing at all rather than a cloud fallback.
  localEngines: {
    local: {
      kind: 'ollama',
      probeUrl: 'http://localhost:11434',
      apiBaseUrl: 'http://localhost:11434/v1',
      reachable: true,
      status: '0.1.0',
      models: [{ id: 'qwen3.5', detail: '' }],
    },
  },
  server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
  side: '',
  perSide: false,
  questions: [],
  sessions: [],
  openSections: ['bugz'],
  usage: [],
  usageWindow: 'week',
  cliStatus: {},
  modelPrices: {},
  snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
  latestServerVersion: '',
  ...over,
});

/** What the script posted, in order. */
interface Posted {
  readonly type: string;
  readonly command?: string;
  readonly key?: string;
  readonly value?: unknown;
}

/** Just enough of a control for the script to bind to and read. */
class Control {
  readonly dataset: Record<string, string>;
  value: string;
  readonly tagName = 'SELECT';
  private readonly handlers = new Map<string, (() => void)[]>();

  constructor(dataset: Record<string, string>, value = '') {
    this.dataset = dataset;
    this.value = value;
  }

  addEventListener(kind: string, handler: () => void): void {
    this.handlers.set(kind, [...(this.handlers.get(kind) ?? []), handler]);
  }

  /** What a person doing it would cause. */
  fire(kind: string): void {
    for (const handler of this.handlers.get(kind) ?? []) {
      handler();
    }
  }
}

/** The last `<script>` the panel ships, cut out of its own html. */
function pageScript(html: string): string {
  const open = html.lastIndexOf('<script');
  const start = html.indexOf('>', open) + 1;
  const end = html.indexOf('</script>', start);
  assert.ok(open >= 0 && end > start, 'the panel has no script to run');

  return html.slice(start, end);
}

/**
 * Run the panel's script over the Bugz section's controls and collect what it posts.
 *
 * <p>`new Function` throws on a syntax error, which is what the page would do on every load — the
 * reason this whole style of test exists in this repository.</p>
 */
function run(controls: Readonly<Record<string, readonly Control[]>>, over: Partial<PanelState> = {}): {
  readonly posted: readonly Posted[];
  readonly html: string;
} {
  const posted: Posted[] = [];
  const html = panelHtml(state(over), 'test-nonce');
  const matching = (selector: string): readonly Control[] =>
    Object.entries(controls).find(([key]) => selector.includes(key))?.[1] ?? [];

  const fakeDocument = {
    addEventListener: () => undefined,
    querySelectorAll: matching,
    querySelector: (): null => null,
    getElementById: (): null => null,
    body: { style: { fontSize: '' } },
    documentElement: { style: { setProperty: () => undefined } },
  };

  // eslint-disable-next-line no-new-func -- the shipped script IS the thing under test.
  const body = new Function('acquireVsCodeApi', 'document', 'window', 'setTimeout', 'clearTimeout',
    pageScript(html));
  body(
    () => ({
      postMessage: (m: Posted) => posted.push(m),
      getState: () => undefined,
      setState: () => undefined,
    }),
    fakeDocument,
    { addEventListener: () => undefined },
    (): number => 0,
    (): void => undefined,
  );

  return { posted, html };
}

test('the panel script is a program, not a string that looks like one', () => {
  assert.doesNotThrow(() => run({}, { bugz: corpus(RUN) }));
});

/**
 * Choosing a ranking model actually SENDS it.
 *
 * <p>The defect this replaces a markup assertion for: the select rendered correctly, matched every
 * regex, and posted its value to a setting the provider never read — so the picker was decoration.
 * What matters is not that the control exists but that a change reaches configuration.</p>
 */
test('choosing a ranking model posts it as a setting', () => {
  // The control is built from what the SECTION really renders, not from a hand-written fixture.
  // Handing the script a control the test invented would pass with no picker on the page at all —
  // which is how the first version of this test passed while the picker was wired to nothing.
  const markup = panelHtml(state({ bugz: corpus(RUN) }), 'test-nonce');
  const section = markup.slice(markup.indexOf('data-section="bugz"'));
  const setting = /<select[^>]*\sdata-setting="([^"]+)"/.exec(section.slice(0, section.indexOf('</details>')));

  assert.ok(setting !== null, 'the section renders no picker bound to a setting');
  assert.equal(setting[1], 'bugzModel', 'the picker must write the setting the collector reads');

  const picker = new Control({ setting: setting[1]! }, 'local/qwen3.5');
  const { posted } = run({ 'data-setting': [picker] }, { bugz: corpus(RUN) });

  // The script bound a listener; a person choosing an option is what fires it.
  picker.fire('change');

  const change = posted.find((m) => m.type === 'setting' && m.key === 'bugzModel');
  assert.ok(change !== undefined, 'the picker must reach configuration, not a field nobody assigns');
  assert.equal(change.value, 'local/qwen3.5');
});

// ---------------------------------------------------------------------------------------------
// The section's decisions, as functions. These are not assertions over page text: they are the
// pure logic the section is built from, and they are where the states are pinned.
// ---------------------------------------------------------------------------------------------

test('a collect that is happening says how far it has got', () => {
  assert.match(collectLabel(RUN), /Collecting… 4\/20/);
});

test('a run that never happened offers Collect, and one that did offers Collect again', () => {
  assert.equal(collectLabel(EMPTY_CORPUS.lastRun), 'Collect');
  assert.equal(collectLabel({ ...RUN, state: 'done', finishedUtc: 'x' }), 'Collect again');
});

/**
 * An unread corpus says so, and does NOT say there is no material.
 *
 * <p>Zero candidates and "we have not looked" are different sentences, and rendering the first when
 * the second is true tells somebody their corpus is empty when nothing asked it.</p>
 */
test('not having asked is not the same as there being nothing', () => {
  assert.match(lastRunLine(EMPTY_CORPUS), /has not been read/);
  assert.match(lastRunLine(corpus(EMPTY_CORPUS.lastRun)), /20 candidate\(s\) waiting/);
});

test('an interrupted run is named as interrupted, not as a failure', () => {
  const line = lastRunLine(corpus({ ...RUN, state: 'interrupted', finishedUtc: 'x' }));

  assert.match(line, /was interrupted/);
  assert.ok(!line.includes('failed:'), 'nothing is known about the rest, which is not failing');
});

test('a run is running until it says otherwise, and none at all is not a run', () => {
  assert.ok(isRunning(RUN));
  assert.ok(!isRunning({ ...RUN, state: 'interrupted' }));
  assert.ok(hasRun(RUN));
  assert.ok(!hasRun(EMPTY_CORPUS.lastRun));
});

/**
 * The picker cannot offer a model the collector will refuse.
 *
 * <p>Enforcement is in `RankingModels`, before a finding field is read. This only keeps the panel
 * from OFFERING what would be refused: a finding's title, why and fix are the reviewers' prose about
 * somebody's code and are not anonymised.</p>
 */
test('only a local model may rank', () => {
  assert.ok(mayRank('local/qwen3.5'));
  assert.ok(mayRank(''), 'no model at all is no ranking pass, which is allowed');
  assert.ok(!mayRank('gemini/pro'));
  assert.ok(!mayRank('codex/gpt-5.6'));
});

/**
 * An older server sends no `lastRun`, and that means no run — never "unavailable".
 *
 * <p>These two halves have shipped out of step before, so the panel must read the old shape.</p>
 */
test('a corpus from a server too old to have runs still reads', () => {
  const old = parseBugs(JSON.stringify({
    funnel: { all: 1, onCode: 1, accepted: 1, gating: 1, runtime: 1, located: 1, unprocessed: 1 },
    candidates: [],
  }));

  assert.ok(old.read, 'the answer was understood');
  assert.equal(old.lastRun.id, '', 'and it means no run has ever started');
  assert.match(lastRunLine(old), /1 candidate\(s\) waiting/);
});

test('an answer that is not JSON is nothing known, not an empty corpus', () => {
  assert.equal(parseBugs('not json').read, false);
});

/**
 * A run that moved repaints the section.
 *
 * <p>Structural, not behavioural: without the corpus in `staticKey` the section is frozen for the
 * life of the panel while the run underneath it progresses perfectly — the defect two other entries
 * in that list were each added for.</p>
 */
test('a run that moved changes what the panel repaints on', () => {
  const before = staticKey(state({ bugz: corpus(RUN) }));
  const after = staticKey(state({ bugz: corpus({ ...RUN, collected: 9 }) }));

  assert.notEqual(before, after, 'a run that moved must reach the screen');
});

/**
 * The section holds no free-text control.
 *
 * <p>A STRUCTURAL guard over the section's own template rather than a behavioural claim: the section
 * is in `staticKey` so Collect can repaint while a run happens, and a section in `staticKey` that
 * holds a text box is rebuilt under the caret on every keystroke. A later edit that adds an
 * `<input>` here reintroduces that silently.</p>
 */
test('the section holds no free-text control, because it repaints', () => {
  const { html } = run({}, { bugz: corpus(RUN) });
  const section = html.slice(html.indexOf('data-section="bugz"'));
  const body = section.slice(0, section.indexOf('</details>'));

  assert.ok(!/<input/.test(body), 'an input here is rebuilt under the caret on every keystroke');
  assert.ok(!/<textarea/.test(body), 'and so is a textarea');
});

/**
 * The picker's vendor list and the collector's cannot drift apart in silence.
 *
 * <p>TypeScript cannot import a C# constant, so the plan's word "derived" was not achievable. What
 * is achievable is that adding a vendor on one side without the other is a RED TEST rather than a
 * feature that half works: the collector would accept a model the picker never offers.</p>
 */
test('the allowlists agree', async () => {
  const fs = await import('node:fs/promises');
  const path = await import('node:path');
  const listed = await fs.readFile(
    path.join(process.cwd(), '..', 'shared', 'ranking-vendors.txt'), 'utf8');

  const vendors = listed.split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0 && !line.startsWith('#'));

  assert.ok(vendors.length > 0, 'the shared list is empty — this guard has stopped guarding');
  assert.deepEqual([...RANKING_VENDORS], vendors,
    'the picker and shared/ranking-vendors.txt disagree about who may read a finding');
});
