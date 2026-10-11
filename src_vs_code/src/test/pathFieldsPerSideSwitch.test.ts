import assert from 'node:assert/strict';
import { test } from 'node:test';

import { catalogHtml } from '../catalogPage';
import type { DataLocation } from '../dataDir';
import { watchedDirs } from '../escalationDirs';
import type { PanelState } from '../panelView';
import type { RootPlaces } from '../qconsultWrite';
import { DEFAULTS } from '../settingsShape';
import { type PathFieldsMode, type SeenSide, type SidesView } from '../sidesSeen';
import { DEFAULT_VENDORS, vendorsFrom } from '../vendors';
import { panelState, runPanel } from './panelPageHarness';
import { type PageNode, pageTree } from './pageTree';

/**
 * `coai.pathFieldsPerSide` decides every place at once (todo/PLAN_paths_per_side.md E4.4): *Always one* (`never`)
 * restores ONE field everywhere even with both sides seen; *Always two* (`always`) draws two with a single side seen, the
 * other labelled *(not seen yet)*. The Models card, the Question consultant's folders and Setup › MCP server are each RUN
 * (`runPanel`) and read back as the tree they drew.
 */

const DAY = 24 * 60 * 60 * 1000;
const NOW = Date.UTC(2026, 9, 11, 12, 0, 0);
const WIN_KEY = 'local|C:/Users/me/AppData/Roaming/Code/User/globalStorage/remsoftdev.connect-other-ais';
const WSL_KEY = 'wsl|Ubuntu|/home/me/.vscode-server/data/User/globalStorage/remsoftdev.connect-other-ais';

function seen(side: string, family: SeenSide['family'], label: string, daysAgo: number): SeenSide {
  return { side, family, label, firstSeen: NOW - 60 * DAY, lastSeen: NOW - daysAgo * DAY };
}

const HERE = { key: WIN_KEY, family: 'windows' as const, label: 'Windows' };
const BOTH_SEEN = [seen(WIN_KEY, 'windows', 'Windows', 0), seen(WSL_KEY, 'posix', 'WSL: Ubuntu', 2)];
const ONLY_THIS = [seen(WIN_KEY, 'windows', 'Windows', 0)];

const PLACES: RootPlaces = {
  dataDir: 'C:\\Users\\me\\AppData\\Local\\coai-mcp', profile: 'C:\\Users\\me', systemDirs: ['C:\\Windows'], caseless: true, windows: true,
  existingHere: [], unknownHere: [], systemDrive: 'C:',
};

const WHERE: DataLocation = {
  directory: 'Z:\\coai\\windows', side: 'windows', ignoredSide: '', refusal: '', notes: [], env: {}, source: 'this side',
  alsoWatched: watchedDirs('C:\\Own', ['D:\\elsewhere\\coai-mcp', '/home/me/.local/share/coai-mcp'], 'win32'),
};

function stateOf(mode: PathFieldsMode, seenSides: readonly SeenSide[]): PanelState {
  const sides: SidesView = { here: HERE, seen: seenSides, mode, now: NOW };

  return {
    ...panelState('reviewers'),
    vendors: vendorsFrom([{ ...DEFAULT_VENDORS[0]!, id: 'codex-a', runtime: 'codex', model: 'gpt-6.1-sol', enabled: true, executablePath: 'C:\\tools\\codex.exe' }]),
    settings: { ...DEFAULTS, qconsult: { ...DEFAULTS.qconsult, roots: ['D:\\rsd\\work', '/home/me/git'] } },
    hostFamily: 'windows',
    qconsultPlaces: PLACES,
    storage: WHERE,
    otherSideData: [],
    sides,
  };
}

/** One place of the page, RUN, as the tree it drew. */
function place(state: PanelState, at: string): PageNode {
  const page = runPanel(state, { html: catalogHtml(state, 'test-nonce', at) });
  // The Models tab is one card here; every other place is its pane.
  const pane = at === 'models'
    ? (node: PageNode): boolean => node.tagName === 'ARTICLE' && node.dataset['modelCard'] === 'codex-a'
    : (node: PageNode): boolean => node.dataset['pane'] === at;

  return pageTree(page.html).one(pane, `the ${at} place`);
}

/** Every other side's group the three places draw — their legends, in place order. */
function legends(state: PanelState): readonly string[] {
  return ['models', 'consultants/qconsult', 'setup/mcp'].flatMap((at) => place(state, at)
    .find((node) => node.tagName === 'FIELDSET' && node.className.split(' ').includes('other-side'))
    .map((group) => group.one((node) => node.tagName === 'LEGEND', 'a legend').text()));
}

test('Always one (never) draws ONE field at every place, with both sides seen', () => {
  const state = stateOf('never', BOTH_SEEN);

  assert.deepEqual(legends(state), [], 'a place still draws the other side\'s group under Always one');
  assert.equal(place(state, 'models').find((node) => (node.dataset['setting'] ?? '').startsWith('executablePath')).length, 1, 'the card draws more than one CLI-path box');
  assert.equal(place(state, 'consultants/qconsult').find((node) => node.dataset['group'] !== undefined).length, 0, 'the folders are grouped');
});

test('Always two draws two fields with a single side seen — the other labelled "not seen yet" at every place', () => {
  const state = stateOf('always', ONLY_THIS);
  const notSeen = 'The other side — WSL / Linux (not seen yet)';

  // The card (one), the folders (one), the data directory and the watched folders (two).
  assert.deepEqual(legends(state), [notSeen, notSeen, notSeen, notSeen]);
  assert.equal(place(state, 'models').find((node) => (node.dataset['setting'] ?? '').startsWith('executablePath')).length, 2);
  assert.match(place(state, 'setup/mcp').text(), /No WSL \/ Linux window has been seen yet/u, 'the data directory of a side never seen says so');
});

test('Automatic follows the sides seen: one field with one side, two with both', () => {
  assert.deepEqual(legends(stateOf('auto', ONLY_THIS)), []);
  assert.equal(legends(stateOf('auto', BOTH_SEEN)).length, 4);
});
