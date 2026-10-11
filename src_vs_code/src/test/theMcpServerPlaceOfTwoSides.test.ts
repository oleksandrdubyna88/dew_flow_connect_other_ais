import assert from 'node:assert/strict';
import { test } from 'node:test';

import { catalogHtml } from '../catalogPage';
import type { DataLocation } from '../dataDir';
import { watchedDirs } from '../escalationDirs';
import type { OtherSideDataDirectory } from '../otherSideValues';
import type { PanelState } from '../panelView';
import { type SeenSide, type SidesView } from '../sidesSeen';
import { seenDay } from '../sidesSeenView';
import { panelState, runPanel } from './panelPageHarness';
import { type PageNode, pageTree } from './pageTree';

/**
 * Setup › MCP server once both sides are known (research/PLAN_paths_per_side.md E4.3): the watched data folders in the groups
 * of `pathGroups.ts`, and the OTHER side's data directory READ-ONLY (decided with the operator, D2) — text in a fieldset
 * with its legend and caption, no input, nothing to save, and *Change it from a window on that side.* One side known: the
 * place is drawn as before. The page is RUN (`runPanel`) and read back as the tree it drew.
 */

const DAY = 24 * 60 * 60 * 1000;
const NOW = Date.UTC(2026, 9, 11, 12, 0, 0);
const WIN_KEY = 'local|C:/Users/me/AppData/Roaming/Code/User/globalStorage/remsoftdev.connect-other-ais';
const WSL_KEY = 'wsl|Ubuntu|/home/me/.vscode-server/data/User/globalStorage/remsoftdev.connect-other-ais';

function seen(side: string, family: SeenSide['family'], label: string, daysAgo: number): SeenSide {
  return { side, family, label, firstSeen: NOW - 60 * DAY, lastSeen: NOW - daysAgo * DAY };
}

const BOTH: SidesView = {
  here: { key: WIN_KEY, family: 'windows', label: 'Windows' },
  seen: [seen(WIN_KEY, 'windows', 'Windows', 0), seen(WSL_KEY, 'posix', 'WSL: Ubuntu', 1)], mode: 'auto', now: NOW,
};

const WHERE: DataLocation = {
  directory: 'Z:\\coai\\windows', side: 'windows', ignoredSide: '', refusal: '', notes: [], env: { COAI_DATA_DIR: 'Z:\\coai', COAI_DATA_SIDE: 'windows' }, source: 'this side',
  alsoWatched: watchedDirs('C:\\Own', ['D:\\elsewhere\\coai-mcp', '/home/me/.local/share/coai-mcp', '//nas/coai'], 'win32'),
};

const UBUNTU: OtherSideDataDirectory = { side: WSL_KEY, label: 'WSL: Ubuntu', family: 'posix', lastSeen: NOW - DAY, directory: '/mnt/z/coai', dataSide: 'wsl' };

function pane(over: Partial<PanelState>): PageNode {
  const state: PanelState = { ...panelState('reviewers', { storage: WHERE }), hostFamily: 'windows', ...over };
  const page = runPanel(state, { html: catalogHtml(state, 'test-nonce', 'setup/mcp') });

  return pageTree(page.html).one((node) => node.dataset['pane'] === 'setup/mcp', 'the setup/mcp pane');
}

/** The other side's data directory group. */
function otherData(tree: PageNode): PageNode {
  return tree.one((node) => node.tagName === 'FIELDSET' && node.dataset['otherSideData'] !== undefined, 'the other side\'s data directory');
}

const said = (node: PageNode): string => node.text().replace(/\s+/gu, ' ');

test('the other side\'s data directory is TEXT in its own group — no input, nothing to save — and says where to change it', () => {
  const tree = pane({ sides: BOTH, otherSideData: [UBUNTU] });
  const theirs = otherData(tree);

  assert.equal(theirs.one((node) => node.tagName === 'LEGEND', 'its legend').text(), `The other side — WSL / Linux (last seen ${seenDay(NOW - DAY)})`);
  assert.ok(said(theirs).includes('WSL: Ubuntu'), 'the side is not named');
  assert.ok(theirs.find((node) => node.tagName === 'CODE' && node.text() === '/mnt/z/coai').length === 1, 'the directory is not shown as text');
  assert.ok(said(theirs).includes('Change it from a window on that side.'), 'nothing says where it is changed');
  assert.ok(said(theirs).includes('Used by the server on that side. This window does not check it.'));
  assert.deepEqual(theirs.find((node) => ['INPUT', 'SELECT', 'TEXTAREA', 'BUTTON'].includes(node.tagName)).map((node) => node.tagName), [],
    'the other side\'s data directory offers a control — it is read-only here (D2)');
  assert.equal(tree.find((node) => node.dataset['setting'] !== undefined && node.value === '/mnt/z/coai').length, 0, 'the other side\'s directory can be saved from here');
});

test('a side of the other family that chose no directory is listed as such, never dropped', () => {
  const theirs = otherData(pane({ sides: BOTH, otherSideData: [{ ...UBUNTU, directory: '', dataSide: '' }] }));

  assert.match(said(theirs), /WSL: Ubuntu: none chosen there/u);
});

test('the watched data folders are drawn in the groups of their spelling — this side\'s, the other side\'s, both sides / unclear', () => {
  const tree = pane({ sides: BOTH, otherSideData: [UBUNTU] });
  const group = (name: string): readonly string[] => tree.one((node) => node.dataset['list'] === 'alsoWatchDataDirectories' && node.dataset['group'] === name, `the ${name} group`)
    .find((node) => node.dataset['watched'] !== undefined).map((node) => node.dataset['watched']!);

  assert.deepEqual([group('thisSide'), group('otherSide'), group('unclear')], [['D:\\elsewhere\\coai-mcp'], ['/home/me/.local/share/coai-mcp'], ['//nas/coai']]);
});

test('with one side known Setup › MCP server is drawn as before — no group, no other side', () => {
  const tree = pane({ otherSideData: [UBUNTU] });

  assert.equal(tree.find((node) => node.tagName === 'FIELDSET' && node.dataset['otherSideData'] !== undefined).length, 0);
  assert.equal(tree.find((node) => node.dataset['group'] !== undefined).length, 0);
  assert.equal(said(tree).includes('/mnt/z/coai'), false, 'the other side\'s directory is drawn with one side known');
});
