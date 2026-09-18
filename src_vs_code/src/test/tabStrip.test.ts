import assert from 'node:assert/strict';
import test from 'node:test';

import { openedFrom, tabStrip } from '../tabStrip';

/**
 * The one tab strip, and what it does that the three private copies did not.
 *
 * <p><b>Why this module exists.</b> `rolesPage`, `notificationsRows` and `roundsLog` each render
 * their own strip, and they have already drifted exactly as the reuse rule predicts: `rolesPage`
 * has `role="tablist"`, `aria-selected` and `aria-controls`; `notificationsRows` has only the
 * first; `roundsLog` has none of them, so a screen reader cannot navigate its tabs as tabs at all.
 * The review page needs TWO strips, which would have made a fourth copy the moment it was typed —
 * a reviewer said so on the plan round and was right, so `rolesPage` is converted in the same
 * change and is this module's first caller.</p>
 *
 * <p><b>What is NOT asserted here, deliberately.</b> There is no test comparing this markup to a
 * string copied out of `rolesPage`. A reviewer called that a source-text assertion and that is what
 * it would be: it passes when both are wrong together and goes red on a whitespace change.
 * `rolesPage`'s own eighteen tab tests are the proof the extraction changed nothing — they pin
 * `class="tab on" data-tab="documents"` and the `aria-selected` triple, and they were written
 * against the private copy.</p>
 *
 * <p><b>The one capability the copies never needed.</b> All three render a fixed handful of
 * hardcoded names. This strip's labels are repository directory names and language names out of
 * the database, so everything that reaches an attribute is escaped — which is why the module is a
 * widening of `rolesPage`'s copy rather than a move of it.</p>
 */

const NAMES = { tab: 'tab-', panel: 'section-', label: 'Which roles to edit' } as const;

test('the wrapper is a real tablist and every button is a real tab', () => {
  const html = tabStrip([{ key: 'plan', label: 'Plan review' }, { key: 'code', label: 'Code review' }], 'code', NAMES);

  assert.match(html, /^<div class="tabs" role="tablist" aria-label="Which roles to edit">/u);
  assert.match(html, /<\/div>$/u);
  assert.equal([...html.matchAll(/role="tab"/gu)].length, 2);
  assert.match(html, /id="tab-plan" aria-controls="section-plan"/u,
    'a tab that controls nothing is the defect roundsLog has');
});

test('exactly one tab is selected, and it is the open one', () => {
  const tabs = [{ key: 'plan', label: 'Plan' }, { key: 'code', label: 'Code' }, { key: 'documents', label: 'Docs' }];

  const html = tabStrip(tabs, 'code', NAMES);

  assert.deepEqual([...html.matchAll(/aria-selected="(\w+)"/gu)].map((m) => m[1]), ['false', 'true', 'false']);
  assert.match(html, /class="tab on" data-tab="code"/u, 'the open tab carries the class the pages style');
  assert.equal([...html.matchAll(/class="tab" data-tab=/gu)].length, 2);
});

test('a label out of the database cannot close an attribute or open a tag', () => {
  // The whole reason this is a widening and not a move: these labels are repository directory names
  // and language names read from SQLite, and the copies this replaces never escaped anything.
  const html = tabStrip(
    [{ key: 'c#', label: '<script>&"x"', title: 'd:/rsd/"odd"' }],
    'c#',
    { tab: 't-', panel: 'p-', label: 'Which project' },
  );

  assert.ok(!html.includes('<script>'), 'a label built a tag');
  assert.match(html, /&lt;script&gt;&amp;&quot;x&quot;/u);
  assert.match(html, /title="d:\/rsd\/&quot;odd&quot;"/u);
  assert.ok(!/data-tab="c#"[^>]*\slang/u.test(html));
});

test('a key that is unusable as a DOM id is carried by data-tab while the slug wires the aria', () => {
  // A project key is a path — `d:/rsd/x` in an id attribute breaks every CSS selector that would
  // reach it. So the caller supplies a slug for the wiring and the true key stays the posted value.
  const html = tabStrip(
    [{ key: 'd:/rsd/dew_flow_connect_other_ais', label: 'dew_flow_connect_other_ais', slug: '0' }],
    'd:/rsd/dew_flow_connect_other_ais',
    { tab: 'project-tab-', panel: 'project-panel-', label: 'Which project' },
  );

  assert.match(html, /id="project-tab-0" aria-controls="project-panel-0"/u);
  assert.match(html, /data-tab="d:\/rsd\/dew_flow_connect_other_ais"/u);
});

test('the slug defaults to the key, which is what keeps the converted pages identical', () => {
  const html = tabStrip([{ key: 'plan', label: 'Plan' }], 'plan', NAMES);

  assert.match(html, /id="tab-plan" aria-controls="section-plan"/u);
});

test('a filter strip points every tab at the ONE region it narrows', () => {
  // The review page's project and language tabs both narrow the same table. Appending a slug would
  // point `aria-controls` at a `pairs-0` that does not exist, and a screen reader follows it.
  const html = tabStrip(
    [{ key: 'a', label: 'A', slug: '0' }, { key: 'b', label: 'B', slug: '1' }],
    'a',
    { tab: 'lang-tab-', panel: 'pairs', onePanel: true, label: 'Which language' },
  );

  assert.equal([...html.matchAll(/aria-controls="pairs"/gu)].length, 2);
  assert.match(html, /id="lang-tab-0"/u, 'the buttons still have ids of their own');
});

test('a page with two strips can tell which one was pressed', () => {
  const html = tabStrip([{ key: 'a', label: 'A' }], 'a', { ...NAMES, strip: 'language' });

  assert.match(html, /<div class="tabs" role="tablist" aria-label="Which roles to edit" data-strip="language">/u);
  assert.ok(!tabStrip([{ key: 'a', label: 'A' }], 'a', NAMES).includes('data-strip'),
    'a page with one strip emits nothing extra — which is what keeps the converted pages identical');
});

test('no tabs is no strip at all', () => {
  // An empty tablist announces a control a person cannot use. The page says why there is nothing
  // instead; that is `trouble`, not a strip with no tabs in it.
  assert.equal(tabStrip([], '', NAMES), '');
});

test('a title is emitted only when there is one', () => {
  const without = tabStrip([{ key: 'plan', label: 'Plan' }], 'plan', NAMES);
  const with_ = tabStrip([{ key: 'plan', label: 'Plan', title: 'the whole path' }], 'plan', NAMES);

  assert.ok(!without.includes('title='), 'an empty tooltip is a tooltip a screen reader still reads');
  assert.match(with_, /data-tab="plan" title="the whole path"/u);
});

test('the open tab falls back to the first when what was held is not available any more', () => {
  // Findings 1 and 2 of the plan round, reported by two reviewers independently: C# is selected in
  // project A, the person switches to project B which has only TypeScript, and the language filter
  // then matches nothing — a blank table with no way back except reopening the panel.
  const inB = [{ key: 'typescript', label: 'TypeScript' }];

  assert.equal(openedFrom(inB, 'csharp'), 'typescript', 'a selection that selects nothing is not a selection');
  assert.equal(openedFrom(inB, 'typescript'), 'typescript', 'a still-valid choice is kept');
  assert.equal(openedFrom(inB, ''), 'typescript', 'nothing held yet opens the first');
});

test('the fallback keeps a held choice across a change that did not remove it', () => {
  // The companion to the test above: a rule that always answered the first tab would pass it, and
  // would throw away the person's choice on every unrelated redraw.
  const many = [{ key: 'a', label: 'A' }, { key: 'b', label: 'B' }, { key: 'c', label: 'C' }];

  assert.equal(openedFrom(many, 'c'), 'c');
});

test('no tabs opens nothing rather than inventing a key', () => {
  assert.equal(openedFrom([], 'csharp'), '');
});
