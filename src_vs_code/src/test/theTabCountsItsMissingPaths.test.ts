import assert from 'node:assert/strict';
import { test } from 'node:test';

import { catalogHtml } from '../catalogPage';
import type { PanelState } from '../panelView';
import type { PathFinding } from '../pathFindings';
import { DEFAULTS } from '../settingsShape';
import { tabStrip } from '../tabStrip';
import { panelState, runPanel } from './panelPageHarness';
import { type PageNode, pageTree } from './pageTree';

/**
 * The count on the tab (todo/PLAN_paths_per_side.md E5.5): a tab — and the sub-tab — whose place holds a missing path
 * shows `● N` after its label, in the error colour, with an `aria-label` a screen reader hears, so the mark is seen from
 * any tab without opening the one it is on. Zero findings draw no badge anywhere. The page is RUN and read back as the
 * tree it drew; the order is asserted on the tree, as a DOM gives it.
 */

function finding(key: string, place: string, value: string, state: PathFinding['state'] = 'missing'): PathFinding {
  return { key, place, value, family: 'windows', state, owner: '' };
}

function drawn(findings: readonly PathFinding[] | undefined): PageNode {
  const state: PanelState = panelState('questionconsultant', {
    settings: { ...DEFAULTS, qconsult: { ...DEFAULTS.qconsult, roots: ['D:\\old-project', 'D:\\gone-too'] } },
    pathFindings: findings,
  });
  const page = runPanel(state, { html: catalogHtml(state, 'test-nonce', 'consultants/qconsult') });

  return pageTree(page.html);
}

/** The strip's button for one place. */
function tab(tree: PageNode, key: string): PageNode {
  return tree.one((node) => node.tagName === 'BUTTON' && node.dataset['tab'] === key, `the tab ${key}`);
}

const badge = (button: PageNode): PageNode | undefined => button.find((node) => node.className.split(' ').includes('tab-mark'))[0];

test('the Consultants tab and its Question consultant sub-tab show ● 2 after their labels, with a label a screen reader hears; Models shows nothing', () => {
  const tree = drawn([
    finding('coai.qconsultRoots', 'consultants/qconsult', 'D:\\old-project'),
    finding('coai.qconsultRoots', 'consultants/qconsult', 'D:\\gone-too'),
    finding('coai.qconsultRoots', 'consultants/qconsult', '\\\\nas\\x', 'unknown'),
  ]);

  for (const key of ['consultants', 'consultants/qconsult']) {
    const button = tab(tree, key);
    const mark = badge(button);
    assert.ok(mark !== undefined, `the tab ${key} carries no count — today a missing folder is seen only on the tab that holds it`);
    assert.equal(mark.text().trim(), '● 2', 'two missing roots; the unknown one counts for nothing');
    assert.equal(mark.attrs['aria-label'], '2 missing folders or paths');
    // In DOM order AFTER the label: the button's parts are its label text, then the badge.
    const parts = button.parts.filter((part) => typeof part !== 'string' || part.trim().length > 0);
    assert.equal(typeof parts[0], 'string', `the label does not come first on ${key}`);
    assert.equal(parts[parts.length - 1], mark, `the badge is not the last thing on ${key}`);
  }
  assert.equal(badge(tab(tree, 'models')), undefined, 'a tab with nothing missing under it shows a count');
  assert.equal(badge(tab(tree, 'consultants/consultant')), undefined, 'the sibling sub-tab shows the other\'s count');
});

test('zero findings draw no badge on any tab', () => {
  assert.equal(drawn([]).find((node) => node.className.split(' ').includes('tab-mark')).length, 0);
  assert.equal(drawn(undefined).find((node) => node.className.split(' ').includes('tab-mark')).length, 0);
});

test('one missing path reads as one — and the strip escapes the label it is given', () => {
  const html = tabStrip([{ key: 'setup', label: 'Setup', mark: { count: 1, label: '1 missing folder or path <"&>' } }], '', { tab: 't-', panel: 'p-', label: 'Settings' });
  const button = pageTree(html).one((node) => node.tagName === 'BUTTON', 'the button');
  const mark = badge(button);

  assert.ok(mark !== undefined, 'the strip drew no badge for a tab that has a mark');
  assert.equal(mark.text().trim(), '● 1');
  assert.equal(mark.attrs['aria-label'], '1 missing folder or path <"&>', 'the label is read back decoded, so it was escaped on the way out');
  assert.ok(!html.includes('<"&>'), 'the label reached the markup unescaped');
});
