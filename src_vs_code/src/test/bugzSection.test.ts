import * as assert from 'node:assert';
import { test } from 'node:test';
import { DEFAULTS } from '../settingsShape';
import { PanelState, panelHtml, staticKey } from '../panelView';
import { BugCorpus, CollectRun, EMPTY_CORPUS, parseBugs } from '../roundsDb';
import { bugzBody, collectLabel, lastRunLine, mayRank } from '../bugzView';

/**
 * The Bugz section: what a person sees of a collector run, and what they can press.
 *
 * <p>The state the section shows is PERSISTED — the run writes its own row before it starts and
 * again when it ends — so these tests drive the section from a corpus rather than from a flag, which
 * is the whole point of the durable-status rule this section exists under.</p>
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

/** The panel as it is really assembled, with only the fields this section reads varied. */
function state(over: Partial<PanelState> = {}): PanelState {
  return {
    settings: DEFAULTS,
    vendors: [],
    codexModels: [], agyModels: [],
    localEngines: {},
    server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
    side: '', perSide: false,
    questions: [], sessions: [], openSections: ['bugz'],
    usage: [], usageWindow: 'day', latestServerVersion: '',
    cliStatus: {}, modelPrices: {},
    snippetStatus: { kind: 'absent', version: 0 },
    ...over,
  } as unknown as PanelState;
}

const page = (over: Partial<PanelState> = {}): string => panelHtml(state(over), 'nonce');

test('a collect that is happening says how far it has got', () => {
  assert.match(collectLabel(RUN), /Collecting… 4\/20/);
});

test('a run that never happened offers Collect, and one that did offers Collect again', () => {
  assert.strictEqual(collectLabel(EMPTY_CORPUS.lastRun), 'Collect');
  assert.strictEqual(collectLabel({ ...RUN, state: 'done', finishedUtc: 'x' }), 'Collect again');
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
  assert.ok(!line.includes('failed:'), 'nothing is known about the rest, which is not the same as failing');
});

/**
 * The section holds NO free-text control, and that is load-bearing.
 *
 * <p>It is in `staticKey` so the Collect button can repaint while a run happens; a section in
 * `staticKey` that holds a text box is rebuilt under the caret on every keystroke. The server
 * address is therefore a dialog behind a button, as `addTeamServer` already is. A future edit that
 * adds an `<input>` here breaks that, silently, and this is what says so.</p>
 */
test('the section has no free-text control, because it repaints', () => {
  const html = bugzBody({ corpus: corpus(RUN), models: [], model: '', server: 'https://x/' });

  assert.ok(!/<input/.test(html), 'an input here is rebuilt under the caret on every keystroke');
  assert.ok(!/<textarea/.test(html), 'and so is a textarea');
  assert.match(html, /data-command="setBugsServer"/, 'the address is asked for in a dialog instead');
});

test('Collect is disabled while a run is happening, and Review until something was collected', () => {
  const going = bugzBody({ corpus: corpus(RUN), models: [], model: '', server: '' });
  assert.match(going, /data-command="collectBugs" disabled/);

  const none = bugzBody({
    corpus: corpus({ ...RUN, state: 'done', collected: 0, finishedUtc: 'x' }),
    models: [], model: '', server: '',
  });
  assert.match(none, /data-command="reviewBugs" disabled/, 'there is nothing to review');

  const some = bugzBody({
    corpus: corpus({ ...RUN, state: 'done', collected: 4, finishedUtc: 'x' }),
    models: [], model: '', server: '',
  });
  assert.ok(!/data-command="reviewBugs" disabled/.test(some));
});

/**
 * The picker cannot offer a model the collector will refuse.
 *
 * <p>The allowlist is enforced in `RankingModels`, before a finding field is read. This only keeps
 * the panel from OFFERING what would be refused — a finding's title, why and fix are the reviewers'
 * prose about somebody's code and are not anonymised.</p>
 */
test('only a local model may rank, and a cloud one is never offered', () => {
  assert.ok(mayRank('local/qwen3.5'));
  assert.ok(mayRank(''), 'no model at all is no ranking pass, which is allowed');
  assert.ok(!mayRank('gemini/pro'));
  assert.ok(!mayRank('codex/gpt-5.6'));

  const html = bugzBody({
    corpus: corpus(RUN),
    models: [{ id: 'gemini/pro', label: 'cloud' }, { id: 'local/qwen', label: 'here' }],
    model: '',
    server: '',
  });

  assert.ok(!html.includes('gemini/pro'), 'a picker that offers a refusal is a picker that lies');
  assert.match(html, /local\/qwen/);
});

/**
 * The section reaches the page, and a change to the run repaints it.
 *
 * <p>Without the corpus in {@link staticKey} the section is frozen for the life of the panel while
 * the run underneath it progresses perfectly — the defect two other entries in that list were each
 * added for.</p>
 */
test('the section is on the page and a run changing repaints it', () => {
  assert.match(page({ bugz: corpus(RUN) }), /data-section="bugz"/);

  const before = staticKey(state({ bugz: corpus(RUN) }));
  const after = staticKey(state({ bugz: corpus({ ...RUN, collected: 9 }) }));

  assert.notStrictEqual(before, after, 'a run that moved must reach the screen');
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
  assert.strictEqual(old.lastRun.id, '', 'and it means no run has ever started');
  assert.match(lastRunLine(old), /1 candidate\(s\) waiting/);
});

test('an answer that is not JSON is nothing known, not an empty corpus', () => {
  assert.strictEqual(parseBugs('not json').read, false);
});
