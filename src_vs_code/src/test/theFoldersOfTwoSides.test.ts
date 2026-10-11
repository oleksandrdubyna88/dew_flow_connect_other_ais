import assert from 'node:assert/strict';
import { test } from 'node:test';

import { catalogHtml } from '../catalogPage';
import type { PanelState } from '../panelView';
import { rootAdded, type RootPlaces } from '../qconsultWrite';
import { DEFAULTS, settingMessageFrom, settingWrite } from '../settingsShape';
import { type SeenSide, type SidesView } from '../sidesSeen';
import { seenDay } from '../sidesSeenView';
import { TYPED_ROOT_KEY, typedRootRefusal } from '../typedRoots';
import { rootBoxKey, withRefusal } from '../typedRefusals';
import { lastWrite, panelState, runPanel, type Page } from './panelPageHarness';
import { type PageNode, pageTree } from './pageTree';

/**
 * The Question consultant's folders drawn in the three groups of `pathGroups.ts` once both sides are known
 * (todo/PLAN_paths_per_side.md E4.2): this side's (with Add a folder…), the other side's in a `fieldset` with its legend
 * and caption, and *both sides / unclear* — nothing hidden. The other side's group takes TYPED text, admitted only when
 * this window would group it as the other side's (so an entry never jumps groups on the next render), and refused
 * BESIDE the box with the reason. The page is RUN (`runPanel`), saved through the host's own decisions
 * (`typedRootRefusal`, `rootAdded`) and RE-RENDERED: the typed entry lands in the group it was typed into.
 */

const DAY = 24 * 60 * 60 * 1000;
const NOW = Date.UTC(2026, 9, 11, 12, 0, 0);
const WIN_KEY = 'local|C:/Users/me/AppData/Roaming/Code/User/globalStorage/remsoftdev.connect-other-ais';
const WSL_KEY = 'wsl|Ubuntu|/home/me/.vscode-server/data/User/globalStorage/remsoftdev.connect-other-ais';

function seen(side: string, family: SeenSide['family'], label: string, daysAgo: number): SeenSide {
  return { side, family, label, firstSeen: NOW - 60 * DAY, lastSeen: NOW - daysAgo * DAY };
}

const BOTH_FROM_WINDOWS: SidesView = {
  here: { key: WIN_KEY, family: 'windows', label: 'Windows' },
  seen: [seen(WIN_KEY, 'windows', 'Windows', 0), seen(WSL_KEY, 'posix', 'WSL: Ubuntu', 3)], mode: 'auto', now: NOW,
};
const BOTH_FROM_WSL: SidesView = {
  here: { key: WSL_KEY, family: 'posix', label: 'WSL: Ubuntu' },
  seen: [seen(WIN_KEY, 'windows', 'Windows', 1), seen(WSL_KEY, 'posix', 'WSL: Ubuntu', 0)], mode: 'auto', now: NOW,
};

const WINDOWS_PLACES: RootPlaces = {
  dataDir: 'C:\\Users\\me\\AppData\\Local\\coai-mcp', profile: 'C:\\Users\\me', systemDirs: ['C:\\Windows'], caseless: true, windows: true,
  existingHere: [], unknownHere: [], systemDrive: 'C:',
};
const WSL_PLACES: RootPlaces = {
  dataDir: '/home/me/.local/share/coai-mcp', profile: '/home/me', systemDirs: ['/usr', '/etc'], caseless: false, windows: false,
  existingHere: [], unknownHere: [], systemDrive: 'C:',
};

/** `/work` is a folder on this Windows machine's system drive, so it is this side's here. */
const STORED = ['D:\\rsd\\work', '/home/me/git', '//nas/share/x', 'work', '/work'];
const WINDOWS_ASKED: RootPlaces = { ...WINDOWS_PLACES, existingHere: ['/work'] };

function stateOf(roots: readonly string[], places: RootPlaces, sides: SidesView | undefined, over: Partial<PanelState> = {}): PanelState {
  return {
    ...panelState('questionconsultant'),
    settings: { ...DEFAULTS, qconsult: { ...DEFAULTS.qconsult, roots } },
    hostFamily: places.windows ? 'windows' : 'posix',
    qconsultPlaces: places,
    ...(sides === undefined ? {} : { sides }),
    ...over,
  };
}

function opened(state: PanelState): { readonly page: Page; readonly pane: PageNode } {
  const html = catalogHtml(state, 'test-nonce', 'consultants/qconsult');

  return { page: runPanel(state, { html }), pane: pageTree(html).one((node) => node.dataset['pane'] === 'consultants/qconsult', 'the question consultant pane') };
}

/** A group as drawn, by its `data-group`. */
function group(pane: PageNode, name: string): PageNode {
  return pane.one((node) => node.dataset['group'] === name && node.dataset['list'] === 'qconsultRoots', `the ${name} group of the folders`);
}

/** The roots a group lists, in order. */
function rootsIn(node: PageNode): readonly string[] {
  return node.find((one) => one.className.split(' ').includes('qconsult-root')).map((line) => line.one((child) => child.tagName === 'CODE', 'the root').text());
}

/** The other side's typed box. */
function typedBox(pane: PageNode): PageNode {
  return pane.one((node) => node.tagName === 'INPUT' && node.dataset['setting'] === TYPED_ROOT_KEY, 'the box that types a folder of the other side');
}

/** What the host does with what was typed: its own decision, then the root added — the settings as the next render reads them. */
function savedBy(page: Page, roots: readonly string[], places: RootPlaces): { readonly roots: readonly string[]; readonly refusal: string } {
  const write = settingWrite(settingMessageFrom(lastWrite(page)));
  assert.ok(write?.kind === 'caller' && write.key === TYPED_ROOT_KEY, `the typed box posted ${JSON.stringify(write)}, not a typed root`);
  const typed = String(write.value);
  const asked = { ...places, existingHere: [...places.existingHere], unknownHere: [...places.unknownHere] };
  const refusal = typedRootRefusal(typed, asked);

  return refusal.length > 0 ? { roots, refusal } : { roots: rootAdded(roots, typed.trim(), asked).roots, refusal };
}

test('with both sides known the folders are drawn in three groups — this side\'s, the other side\'s in a fieldset with its legend, both sides / unclear — and nothing is hidden', () => {
  const { pane } = opened(stateOf(STORED, WINDOWS_ASKED, BOTH_FROM_WINDOWS));

  assert.deepEqual(rootsIn(group(pane, 'thisSide')), ['D:\\rsd\\work', '/work'], 'this side\'s group — /work exists on this machine\'s system drive');
  const theirs = group(pane, 'otherSide');
  assert.equal(theirs.tagName, 'FIELDSET', 'the other side\'s group is not a fieldset');
  assert.equal(theirs.one((node) => node.tagName === 'LEGEND', 'its legend').text(), `The other side — WSL / Linux (last seen ${seenDay(NOW - 3 * DAY)})`);
  assert.deepEqual(rootsIn(theirs), ['/home/me/git']);
  assert.deepEqual(rootsIn(group(pane, 'unclear')), ['//nas/share/x', 'work'], 'a share and a relative name are neither side\'s alone');
  assert.equal(['thisSide', 'otherSide', 'unclear'].flatMap((name) => rootsIn(group(pane, name))).length, STORED.length, 'a stored root is in no group');

  assert.equal(group(pane, 'thisSide').find((node) => node.dataset['command'] === 'qconsultAddRoot').length, 1, 'Add a folder… does not add to this side\'s group');
  assert.equal(theirs.find((node) => node.dataset['command'] === 'qconsultAddRoot').length, 0, 'the other side\'s group offers a folder picker — this host cannot browse that disk');
  assert.equal(theirs.find((node) => node.dataset['command'] === 'qconsultRemoveRoot').length, 1, 'Remove does not work in the other side\'s group');
  const box = typedBox(pane);
  assert.ok(theirs.all().includes(box), 'the typed box is not in the other side\'s group');
  assert.ok(pane.find((node) => node.tagName === 'LABEL' && node.attrs['for'] === box.id && box.id.length > 0).length === 1, 'the typed box has no label');
});

test('with one side known the folders are one list, exactly as before — no groups, no typed box', () => {
  const { pane } = opened(stateOf(STORED, WINDOWS_ASKED, undefined));

  assert.equal(pane.find((node) => node.dataset['group'] !== undefined).length, 0);
  assert.equal(pane.find((node) => node.dataset['setting'] === TYPED_ROOT_KEY).length, 0);
  assert.equal(pane.find((node) => node.className.split(' ').includes('qconsult-root')).length, STORED.length);
});

test('a folder typed into the other side\'s box is added and, re-rendered, lands in the group it was typed into; a //server/share lands in both sides / unclear', () => {
  for (const [typed, landsIn] of [['/home/me/other', 'otherSide'], ['//nas/share/y', 'unclear']] as const) {
    const { page } = opened(stateOf(STORED, WINDOWS_ASKED, BOTH_FROM_WINDOWS));
    const box = page.controls.find((one) => one.dataset['setting'] === TYPED_ROOT_KEY);
    assert.ok(box !== undefined, 'the page draws no box to type a folder of the other side into');
    box.value = typed;
    box.fire('change');

    const saved = savedBy(page, STORED, WINDOWS_ASKED);
    assert.equal(saved.refusal, '', `${typed} was refused`);
    const { pane } = opened(stateOf(saved.roots, WINDOWS_ASKED, BOTH_FROM_WINDOWS));
    assert.ok(rootsIn(group(pane, landsIn)).includes(typed), `${typed} did not land in the ${landsIn} group`);
  }
});

test('the admission rule: only what this window would call the other side\'s goes in, each refusal with its reason', () => {
  const windows = (typed: string, places: RootPlaces = WINDOWS_PLACES): string => typedRootRefusal(typed, places);
  const wsl = (typed: string): string => typedRootRefusal(typed, WSL_PLACES);

  assert.match(windows('D:\\elsewhere'), /'D:\\elsewhere' is spelled for this side — add it above/u, 'a Windows path typed into the WSL / Linux box on Windows');
  assert.match(windows('\\\\server\\share\\x'), /spelled for this side/u, 'a \\\\server\\share typed into the WSL / Linux box on Windows');
  assert.match(windows('work'), /'work' is not an absolute path/u, 'a relative path — rootRefusal\'s own words');
  assert.match(windows('/tmp/project', { ...WINDOWS_PLACES, existingHere: ['/tmp/project'] }), /on this machine that is C:\\tmp\\project, this side's folder/u);
  assert.match(windows('/tmp/project', { ...WINDOWS_PLACES, unknownHere: ['/tmp/project'] }), /could not tell whether C:\\tmp\\project exists/u, 'a folder the disk could not answer for would group as unclear');
  assert.equal(windows('/home/me/project'), '', 'a WSL folder that is not on this machine is the other side\'s');
  assert.equal(windows('//nas/share/project'), '', 'a //server/share is admitted into both sides / unclear');

  assert.match(wsl('/home/me/project'), /spelled for this side/u, 'a POSIX path typed into the Windows box in WSL');
  assert.match(wsl('\\work'), /'\\work' names no drive/u, 'a root-relative \\path in WSL');
  assert.equal(wsl('D:\\rsd\\work'), '', 'a drive-letter path is the Windows side\'s');
  assert.equal(wsl('\\\\server\\share\\x'), '', 'a \\\\server\\share is the Windows side\'s');
  assert.match(wsl('C:\\'), /drive root/u, 'every root\'s own rules still apply on the other side');
});

test('a refused folder is drawn BESIDE the other side\'s box — the typed text kept, aria-invalid, described by the reason', () => {
  const reason = typedRootRefusal('D:\\elsewhere', WINDOWS_PLACES);
  const refusals = withRefusal({}, rootBoxKey('qconsultRoots', 'otherSide'), { typed: 'D:\\elsewhere', reason });
  const { pane } = opened(stateOf(STORED, WINDOWS_ASKED, BOTH_FROM_WINDOWS, { typedRefusals: refusals }));
  const box = typedBox(pane);

  assert.equal(box.value, 'D:\\elsewhere', 'the refused text was not kept');
  assert.equal(box.attrs['aria-invalid'], 'true');
  const said = (box.attrs['aria-describedby'] ?? '').split(' ').map((id) => pane.find((node) => node.id === id)[0]?.text() ?? '');
  assert.ok(said.includes(reason), `the box is not described by the reason: ${JSON.stringify(said)}`);
  assert.ok(said.includes('Used by the server on that side. This window does not check it.'), 'the box is not described by its group\'s caption');
});

test('a WSL window draws the Windows folders as the other side\'s', () => {
  const { pane } = opened(stateOf(['/home/me/git', 'D:\\rsd\\work'], WSL_PLACES, BOTH_FROM_WSL));

  assert.deepEqual(rootsIn(group(pane, 'thisSide')), ['/home/me/git']);
  assert.deepEqual(rootsIn(group(pane, 'otherSide')), ['D:\\rsd\\work']);
  assert.match(group(pane, 'otherSide').one((node) => node.tagName === 'LEGEND', 'its legend').text(), /^The other side — Windows \(last seen /u);
});
