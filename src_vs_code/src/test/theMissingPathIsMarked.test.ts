import assert from 'node:assert/strict';
import { test } from 'node:test';

import { catalogHtml } from '../catalogPage';
import type { DataLocation } from '../dataDir';
import { watchedDirs } from '../escalationDirs';
import type { PanelState } from '../panelView';
import { type PathFinding } from '../pathFindings';
import type { RootPlaces } from '../qconsultWrite';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS, vendorsFrom } from '../vendors';
import { panelState, runPanel } from './panelPageHarness';
import { type PageNode, pageTree } from './pageTree';

/**
 * The bright mark (research/PLAN_paths_per_side.md E5.4): a path of this side the disk cannot find is marked `.path-missing`
 * beside its value — the `.skew` shape in the error colour, a `●`, the word *missing*, `aria-invalid="true"` and an
 * `aria-describedby` sentence — on the Question consultant, on the Models card's CLI path and on Setup › MCP server's
 * watched folders and data directory. An unknown path takes the warning tone and "could not be checked". The page is RUN
 * (`runPanel`) and read back as the tree it drew; given no finding, nothing on the page carries the class.
 */

const WINDOWS_PLACES: RootPlaces = {
  dataDir: 'C:\\Users\\me\\AppData\\Local\\coai-mcp', profile: 'C:\\Users\\me', systemDirs: ['C:\\Windows'], caseless: true, windows: true, existingHere: [], unknownHere: [], systemDrive: 'C:',
};

function finding(key: string, place: string, value: string, state: PathFinding['state'] = 'missing', owner = ''): PathFinding {
  return { key, place, value, family: 'windows', state, owner };
}

/** The page on a place, RUN, as the tree it drew. */
function drawn(state: PanelState, place: string): PageNode {
  const page = runPanel(state, { html: catalogHtml(state, 'test-nonce', place) });

  return pageTree(page.html);
}

const classes = (node: PageNode): readonly string[] => node.className.split(' ');
const marked = (tree: PageNode): readonly PageNode[] => tree.find((node) => classes(node).includes('path-missing') || classes(node).includes('path-unknown'));

/** The sentence an `aria-describedby` points at, as the page drew it. */
function describedBy(tree: PageNode, node: PageNode): string {
  const id = node.attrs['aria-describedby'] ?? '';
  assert.ok(id.length > 0, 'the marked control names no sentence');

  return tree.one((one) => one.id === id, `the sentence ${id}`).text();
}

// ---------- the question consultant's roots ----------

function rootsPage(roots: readonly string[], findings: readonly PathFinding[] | undefined): PageNode {
  return drawn(panelState('questionconsultant', {
    settings: { ...DEFAULTS, qconsult: { ...DEFAULTS.qconsult, roots } },
    qconsultPlaces: WINDOWS_PLACES,
    pathFindings: findings,
  }), 'consultants/qconsult');
}

function rootLine(tree: PageNode, root: string): PageNode {
  return tree.one((node) => classes(node).includes('qconsult-root') && node.find((child) => child.tagName === 'CODE' && child.text() === root).length > 0, `line for the root ${root}`);
}

test('a deleted root of this OS is marked path-missing on its line — the word missing, aria-invalid, and a sentence it is described by; the root beside it is plain', () => {
  const tree = rootsPage(['D:\\old-project', 'D:\\rsd\\work'], [finding('coai.qconsultRoots', 'consultants/qconsult', 'D:\\old-project')]);

  const gone = rootLine(tree, 'D:\\old-project');
  assert.ok(classes(gone).includes('path-missing'), `the deleted root's line is drawn as ${gone.className} — today nothing beside the folder says it is gone`);
  assert.match(gone.text(), /missing/u, 'the word carries the mark where a theme drops the tint');
  assert.equal(gone.attrs['aria-invalid'], 'true');
  assert.match(describedBy(tree, gone), /missing — not found on this machine \(Windows\)/u);
  assert.equal(gone.find((node) => classes(node).includes('stale')).length, 0, 'the mark is not the quiet refusal tone');

  const fine = rootLine(tree, 'D:\\rsd\\work');
  assert.ok(!classes(fine).includes('path-missing'), 'an existing root was marked');
  assert.equal(fine.attrs['aria-invalid'], undefined);
});

test('given no finding, nothing on the page carries the mark', () => {
  assert.equal(marked(rootsPage(['D:\\old-project'], [])).length, 0);
  assert.equal(marked(rootsPage(['D:\\old-project'], undefined)).length, 0, 'a page drawn before the watch has published draws no mark');
});

test('a root the disk could not answer for takes the warning tone and "could not be checked", and is not invalid', () => {
  const tree = rootsPage(['\\\\nas\\git'], [finding('coai.qconsultRoots', 'consultants/qconsult', '\\\\nas\\git', 'unknown')]);

  const line = rootLine(tree, '\\\\nas\\git');
  assert.ok(classes(line).includes('path-unknown'), `drawn as ${line.className}`);
  assert.ok(!classes(line).includes('path-missing'), 'an unknown path was called missing');
  assert.match(line.text(), /could not be checked/u);
  assert.equal(line.attrs['aria-invalid'], undefined, 'nothing is known against an unknown path');
  assert.match(describedBy(tree, line), /could not be checked on this machine/u);
});

// ---------- the Models card's CLI path ----------

function modelsPage(executablePath: string, findings: readonly PathFinding[] | undefined, extra: Readonly<Record<string, unknown>> = {}): PageNode {
  const vendors = vendorsFrom([{ ...DEFAULT_VENDORS[0]!, id: 'my-codex', runtime: 'codex', model: 'gpt-6.1-sol', enabled: true, executablePath, ...extra }]);

  return drawn({ ...panelState('reviewers'), vendors, hostFamily: 'windows', otherSideClis: [], pathFindings: findings }, 'models');
}

test('a Models card whose CLI path is missing marks the box: the field path-missing, the input aria-invalid and described by the sentence', () => {
  const tree = modelsPage('D:\\tools\\codex.exe', [finding('coai.vendors[].executablePath', 'models', 'D:\\tools\\codex.exe', 'missing', 'my-codex')]);

  const box = tree.one((node) => node.tagName === 'INPUT' && node.dataset['setting'] === 'executablePath' && node.dataset['vendor'] === 'my-codex', 'the CLI-path box');
  assert.equal(box.attrs['aria-invalid'], 'true', 'the box is not marked invalid');
  assert.match(describedBy(tree, box), /missing — not found on this machine \(Windows\)/u);
  assert.ok(box.parent !== undefined && classes(box.parent as PageNode).includes('path-missing'), `the field around the box is drawn as ${(box.parent as PageNode | undefined)?.className}`);

  assert.equal(marked(modelsPage('D:\\tools\\codex.exe', [])).length, 0, 'a card with no finding was marked');
});

test('a card whose row keeps a CLI path per side marks THIS side\'s value — the one its box draws and the watch names', () => {
  // Paths per side E3 × E5: the WSL side saved last (legacy `/usr/local/bin/codex`); this Windows side's own value is in
  // its member, and the watch's finding is about that value (`pathWatch.cliValues`).
  const bySide = { executablePathBySide: { windows: 'D:\\mine\\codex.exe', posix: '/usr/local/bin/codex', mirrored: '/usr/local/bin/codex' } };
  const tree = modelsPage('/usr/local/bin/codex', [finding('coai.vendors[].executablePath', 'models', 'D:\\mine\\codex.exe', 'missing', 'my-codex')], bySide);

  const box = tree.one((node) => node.tagName === 'INPUT' && node.dataset['setting'] === 'executablePath' && node.dataset['vendor'] === 'my-codex', 'the CLI-path box');
  assert.equal(box.attrs['value'], 'D:\\mine\\codex.exe', 'the box draws this side\'s value');
  assert.equal(box.attrs['aria-invalid'], 'true', 'the finding about this side\'s value did not mark its box');
});

// ---------- Setup › MCP server: the watched folders and the data directory ----------

const WHERE: DataLocation = {
  directory: 'Z:\\coai\\windows', side: 'windows', ignoredSide: '', refusal: '', notes: [], env: { COAI_DATA_DIR: 'Z:\\coai', COAI_DATA_SIDE: 'windows' }, source: 'this side',
  alsoWatched: watchedDirs('C:\\Own', ['D:\\elsewhere\\coai-mcp', 'D:\\there\\coai-mcp'], 'win32'),
};

function serverPage(findings: readonly PathFinding[] | undefined): PageNode {
  const tree = drawn(panelState('reviewers', { storage: WHERE, pathFindings: findings }), 'setup/mcp');

  return tree.one((node) => node.dataset['pane'] === 'setup/mcp', 'the setup/mcp pane');
}

test('a watched folder of this side that is missing is marked on its line; the one beside it stays plain', () => {
  const pane = serverPage([finding('coai.alsoWatchDataDirectories', 'setup/mcp', 'D:\\elsewhere\\coai-mcp')]);

  const gone = pane.one((node) => node.text().startsWith('D:\\elsewhere\\coai-mcp'), 'the line for the missing watched folder');
  assert.ok(classes(gone).includes('path-missing'), `drawn as ${gone.className}`);
  assert.match(gone.text(), /missing/u);
  assert.equal(gone.attrs['aria-invalid'], 'true');
  const there = pane.one((node) => node.className === 'status' && node.text() === 'D:\\there\\coai-mcp', 'the plain line for the folder that is there');
  assert.equal(there.attrs['aria-invalid'], undefined);
});

test('a NAMED data directory that is missing is marked on the directory line, in the error tone (D3)', () => {
  const pane = serverPage([finding('coai.dataDirectory', 'setup/mcp', 'Z:\\coai')]);

  const line = pane.one((node) => node.text().startsWith('Z:\\coai\\windows'), 'the data directory line');
  assert.ok(classes(line).includes('path-missing'), `drawn as ${line.className} — today a missing named folder is a quiet "is not there yet" note`);
  assert.match(line.text(), /missing/u);
  assert.equal(line.attrs['aria-invalid'], 'true');
  assert.equal(marked(serverPage([])).length, 0, 'the pane marks something with no finding');
});
