import assert from 'node:assert/strict';
import { test } from 'node:test';

import { catalogHtml } from '../catalogPage';
import { keptKey, type DroppedValue } from '../cliPathKeep';
import { cliPathRefusal, vendorFieldWrite } from '../cliPathSave';
import { type PanelState } from '../panelView';
import { pathForThisSide, type ExecutableSide, type PathFamily } from '../pathFamily';
import { keptPerSide, settingMessageFrom, settingWrite } from '../settingsShape';
import { type SeenSide, type SidesView } from '../sidesSeen';
import { seenDay } from '../sidesSeenView';
import { cliBoxKey, withRefusal } from '../typedRefusals';
import { DEFAULT_VENDORS, vendorsFrom, type Vendor } from '../vendors';
import { click, lastWrite, panelState, runPanel, work, type Page } from './panelPageHarness';
import { type PageNode, pageTree } from './pageTree';

/**
 * The Models card draws TWO CLI-path fields once both sides are known (todo/PLAN_paths_per_side.md E4.1): this side's
 * first, labelled *This side — Windows*, then the other side's in a `fieldset` whose `legend` names it and the day it was
 * last seen, with the caption *Used by the server on that side. This window does not check it.* — editable as text, no
 * Browse. One field otherwise, exactly as before. The other side's box saves through the panel's ONE vendor write with
 * THAT side's family (`vendorFieldWrite` → `savedCliPath`), so the page is saved and RE-RENDERED here: the other box
 * shows what was typed, this side's box is unchanged, and each family resolves what its box shows. A path of this side's
 * OS typed into the other box is refused BESIDE the box. **Restore** sits beside the field while a previously saved value
 * is missing. The page is RUN (`runPanel`) and read back as the tree it drew.
 */

const DAY = 24 * 60 * 60 * 1000;
const NOW = Date.UTC(2026, 9, 11, 12, 0, 0);
const WIN_KEY = 'local|C:/Users/me/AppData/Roaming/Code/User/globalStorage/remsoftdev.connect-other-ais';
const WSL_KEY = 'wsl|Ubuntu|/home/me/.vscode-server/data/User/globalStorage/remsoftdev.connect-other-ais';

function seen(side: string, family: PathFamily, label: string, daysAgo: number): SeenSide {
  return { side, family, label, firstSeen: NOW - 60 * DAY, lastSeen: NOW - daysAgo * DAY };
}

const WINDOWS_HERE = { key: WIN_KEY, family: 'windows' as const, label: 'Windows' };
const WSL_HERE = { key: WSL_KEY, family: 'posix' as const, label: 'WSL: Ubuntu' };

/** A Windows window that saw a WSL window two days ago — both families known. */
const BOTH_FROM_WINDOWS: SidesView = { here: WINDOWS_HERE, seen: [seen(WIN_KEY, 'windows', 'Windows', 0), seen(WSL_KEY, 'posix', 'WSL: Ubuntu', 2)], mode: 'auto', now: NOW };
const ONLY_WINDOWS: SidesView = { ...BOTH_FROM_WINDOWS, seen: [seen(WIN_KEY, 'windows', 'Windows', 0)] };
const BOTH_FROM_WSL: SidesView = { here: WSL_HERE, seen: [seen(WIN_KEY, 'windows', 'Windows', 5), seen(WSL_KEY, 'posix', 'WSL: Ubuntu', 0)], mode: 'auto', now: NOW };

const row = (over: Partial<Vendor> = {}): Vendor =>
  vendorsFrom([{ ...DEFAULT_VENDORS[0]!, id: 'codex-a', runtime: 'codex', model: 'gpt-6.1-sol', enabled: true, executablePath: 'C:\\tools\\codex.exe', ...over }])[0]!;

function stateOf(vendors: readonly Vendor[], family: PathFamily, sides: SidesView | undefined, over: Partial<PanelState> = {}): PanelState {
  return { ...panelState('reviewers'), vendors, hostFamily: family, ...(sides === undefined ? {} : { sides }), ...over };
}

function opened(state: PanelState): { readonly page: Page; readonly tree: PageNode } {
  const page = runPanel(state, { html: catalogHtml(state, 'test-nonce', 'models') });

  return { page, tree: pageTree(page.html) };
}

/** Every CLI-path box of a row, in the order the card drew them. */
function boxesOf(tree: PageNode, id: string): readonly PageNode[] {
  return tree.find((node) => node.tagName === 'INPUT' && node.dataset['vendor'] === id && (node.dataset['setting'] ?? '').startsWith('executablePath'));
}

/** The card of a row. */
function cardOf(tree: PageNode, id: string): PageNode {
  return tree.one((node) => node.tagName === 'ARTICLE' && node.dataset['modelCard'] === id, `the card of ${id}`);
}

/** The text of the `<label for>` that names a box — every field has one in two-field mode. */
function labelOf(tree: PageNode, box: PageNode): string {
  const label = tree.one((node) => node.tagName === 'LABEL' && node.attrs['for'] === box.id && box.id.length > 0, `a label for ${box.dataset['setting']}`);

  // The words of the label — a help mark beside them (`?`, its own element) is not what the field is called.
  return label.parts.map((part) => (typeof part === 'string' ? part : (part.className.split(' ').includes('help') ? '' : part.text()))).join('').trim();
}

/** The fieldset a node sits in, or undefined. */
function fieldsetOf(tree: PageNode, box: PageNode): PageNode | undefined {
  return tree.find((node) => node.tagName === 'FIELDSET' && node.all().includes(box))[0];
}

/** The text of every element a box's `aria-describedby` names. */
function described(tree: PageNode, box: PageNode): readonly string[] {
  return (box.attrs['aria-describedby'] ?? '').split(' ').filter((id) => id.length > 0)
    .map((id) => tree.one((node) => node.id === id, `the sentence ${id}`).text().trim());
}

const side = (family: PathFamily): ExecutableSide => ({ family, systemDrive: 'C:', fileAt: () => 'unknown' });

test('with both sides known the card holds two CLI-path boxes, this side\'s first, each labelled by its side; the other side\'s in a fieldset with its legend and caption, and no Browse', () => {
  const { tree } = opened(stateOf([row()], 'windows', BOTH_FROM_WINDOWS));
  const boxes = boxesOf(tree, 'codex-a');

  assert.equal(boxes.length, 2, `the card draws ${boxes.length} CLI-path box(es) with a Windows and a WSL side both seen — today it draws one`);
  const [mine, theirs] = boxes as [PageNode, PageNode];
  assert.equal(mine.dataset['setting'], 'executablePath', 'this side\'s box is not first, or no longer posts the one CLI-path key');
  assert.equal(labelOf(tree, mine), 'This side — Windows');
  assert.equal(fieldsetOf(tree, mine), undefined, 'this side\'s box sits in the other side\'s group');

  const group = fieldsetOf(tree, theirs);
  assert.ok(group !== undefined, 'the other side\'s box is not in a fieldset');
  assert.equal(group.one((node) => node.tagName === 'LEGEND', 'a legend').text(), `The other side — WSL / Linux (last seen ${seenDay(NOW - 2 * DAY)})`);
  assert.match(labelOf(tree, theirs), /WSL \/ Linux/u);
  assert.ok(described(tree, theirs).includes('Used by the server on that side. This window does not check it.'), 'the other side\'s box is not described by its caption');
  assert.equal(group.find((node) => node.tagName === 'BUTTON').length, 0, 'the other side\'s group offers a button — no Browse: this host cannot browse that disk');
});

test('a WSL window names itself with its distro, and the Windows box is the other side\'s', () => {
  const { tree } = opened(stateOf([row()], 'posix', BOTH_FROM_WSL));
  const [mine, theirs] = boxesOf(tree, 'codex-a') as [PageNode, PageNode];

  assert.equal(labelOf(tree, mine), 'This side — WSL / Linux (WSL: Ubuntu)');
  assert.equal(theirs.dataset['setting'], 'executablePath.windows');
  assert.equal(theirs.value, 'C:\\tools\\codex.exe', 'the Windows box does not show the value the Windows side runs');
});

test('with one side known the card draws ONE box, exactly as before — no label, no fieldset', () => {
  for (const sides of [ONLY_WINDOWS, undefined]) {
    const { tree } = opened(stateOf([row()], 'windows', sides));
    const boxes = boxesOf(tree, 'codex-a');

    assert.equal(boxes.length, 1);
    assert.equal(boxes[0]!.dataset['setting'], 'executablePath');
    assert.equal(cardOf(tree, 'codex-a').find((node) => node.tagName === 'FIELDSET' || node.tagName === 'LABEL' && node.attrs['for'] === boxes[0]!.id && boxes[0]!.id.length > 0).length, 0);
  }
});

test('typing into the other side\'s box saves ONLY that side\'s value; re-rendered, it shows what was typed, this side\'s box is unchanged, and each side resolves what its box shows', () => {
  const before = row({ executablePath: 'C:\\tools\\codex.exe' });
  const { page } = opened(stateOf([before], 'windows', BOTH_FROM_WINDOWS));
  const theirs = page.controls.find((one) => one.dataset['setting'] === 'executablePath.posix' && one.dataset['vendor'] === 'codex-a');
  assert.ok(theirs !== undefined, 'the page draws no WSL / Linux box to type into');
  theirs.value = '/home/me/.local/bin/codex';
  theirs.fire('change');

  // The host's write, as the panel routes it: the one vendor write, with this window's family.
  const write = settingWrite(settingMessageFrom(lastWrite(page)));
  assert.ok(write?.kind === 'vendor', 'the box posted no vendor write');
  assert.equal(cliPathRefusal(write.key, write.value, 'windows'), '', 'a WSL path typed into the WSL box was refused');
  const after = vendorsFrom([{ ...before, ...vendorFieldWrite(before, write.key, write.value, 'windows') }])[0]!;
  assert.equal(after.executablePath, 'C:\\tools\\codex.exe', 'the other side\'s save touched the legacy value an older reader on this side runs');
  assert.equal(after.executablePathBySide?.mirrored, before.executablePathBySide?.mirrored ?? 'C:\\tools\\codex.exe');

  const [mine, again] = boxesOf(opened(stateOf([after], 'windows', BOTH_FROM_WINDOWS)).tree, 'codex-a') as [PageNode, PageNode];
  assert.equal(again.value, '/home/me/.local/bin/codex', 're-rendered, the WSL box lost what was typed');
  assert.equal(mine.value, 'C:\\tools\\codex.exe', 're-rendered, this side\'s box changed');
  assert.equal(pathForThisSide(after, side('posix')).path, again.value, 'the WSL side does not run what its box shows');
  assert.equal(pathForThisSide(after, side('windows')).path, mine.value, 'the Windows side does not run what its box shows');
});

test('a path of this side\'s OS typed into the other side\'s box is refused BESIDE the box — the text kept, aria-invalid, described by the reason', () => {
  const reason = cliPathRefusal('executablePath.posix', 'D:\\bin\\codex.exe', 'windows');
  assert.match(reason, /'D:\\bin\\codex\.exe' is a Windows path — this side's/u, 'a Windows path typed into the WSL / Linux box on Windows is not refused');
  assert.equal(cliPathRefusal('executablePath.windows', '/usr/bin/codex', 'posix').length > 0, true, 'a POSIX path typed into the Windows box in WSL is not refused');
  assert.equal(cliPathRefusal('executablePath', 'D:\\bin\\codex.exe', 'windows'), '', 'this side\'s own box was judged by the other side\'s rule');
  assert.equal(cliPathRefusal('executablePath.posix', 'codex', 'windows'), '', 'a bare name — a PATH lookup on that side — was refused');

  const refusals = withRefusal({}, cliBoxKey('codex-a', 'posix'), { typed: 'D:\\bin\\codex.exe', reason });
  const { tree } = opened(stateOf([row()], 'windows', BOTH_FROM_WINDOWS, { typedRefusals: refusals }));
  const theirs = boxesOf(tree, 'codex-a')[1]!;

  assert.equal(theirs.value, 'D:\\bin\\codex.exe', 'the refused text was not kept in the box');
  assert.equal(theirs.attrs['aria-invalid'], 'true');
  assert.ok(described(tree, theirs).includes(reason), 'the box is not described by the reason');
  assert.equal(boxesOf(tree, 'codex-a')[0]!.attrs['aria-invalid'], undefined, 'this side\'s box was marked by the other box\'s refusal');
});

test('a previously saved value that is missing draws Restore beside the field, which posts the kept copy\'s key; none when nothing is missing', () => {
  const plain = row();
  const dropped: DroppedValue = {
    key: keptKey('user', plain), id: plain.id, runtime: plain.runtime, model: plain.model,
    kept: { windows: 'C:\\tools\\codex.exe', posix: '/home/me/.local/bin/codex', mirrored: 'C:\\tools\\codex.exe' }, legacy: 'C:\\tools\\codex.exe',
  };
  for (const sides of [BOTH_FROM_WINDOWS, ONLY_WINDOWS]) {
    const { page, tree } = opened(stateOf([plain], 'windows', sides, { droppedCliPaths: [dropped] }));
    const restore = tree.find((node) => node.tagName === 'BUTTON' && node.dataset['command'] === 'restoreCliPath');

    assert.equal(restore.length, 1, `the card draws ${restore.length} Restore buttons beside its CLI path while a saved value is missing`);
    assert.equal(restore[0]!.attrs['type'], 'button');
    assert.match(described(tree, restore[0]!).join(' '), /WSL \/ Linux: \/home\/me\/\.local\/bin\/codex/u, 'Restore does not say what it puts back');
    click(page, 'restoreCliPath', dropped.key);
    assert.deepEqual(work(page).map((sent) => [sent['command'], sent['id']]), [['restoreCliPath', dropped.key]]);
  }
  assert.equal(opened(stateOf([plain], 'windows', BOTH_FROM_WINDOWS)).tree.find((node) => node.dataset['command'] === 'restoreCliPath').length, 0, 'Restore drawn with nothing missing');
});

const KEEPS_ITS_OWN = 'This side keeps its own settings — set the other side\'s value from a window on that side.';

test('while this side keeps its own settings the other side\'s CLI box is not drawn: a read-only sentence stands in its place, in the other side\'s group', () => {
  const { tree } = opened(stateOf([row()], 'windows', BOTH_FROM_WINDOWS, { perSide: true }));
  const boxes = boxesOf(tree, 'codex-a');

  assert.deepEqual(boxes.map((box) => box.dataset['setting']), ['executablePath'],
    `with coai.perSideSettings on the card draws ${boxes.length} CLI-path boxes — the other side's saves into THIS side's own settings, which that side never reads, and the page shows it saved`);
  assert.equal(labelOf(tree, boxes[0]!), 'This side — Windows', 'this side\'s box lost its side');
  const group = cardOf(tree, 'codex-a').one((node) => node.tagName === 'FIELDSET', 'the other side\'s group');
  assert.ok(group.text().includes(KEEPS_ITS_OWN), 'no read-only sentence stands where the other side\'s box was');
  assert.equal(group.find((node) => node.tagName === 'INPUT' || node.tagName === 'BUTTON').length, 0, 'the other side\'s group still offers a control');
});

test('a CLI path refused while this side keeps its own settings is drawn beside the sentence', () => {
  const reason = '\'/home/me/.local/bin/codex\' was not saved — this side keeps its own settings; set the other side\'s value from a window on that side.';
  const refusals = withRefusal({}, cliBoxKey('codex-a', 'posix'), { typed: '/home/me/.local/bin/codex', reason });
  const { tree } = opened(stateOf([row()], 'windows', BOTH_FROM_WINDOWS, { perSide: true, typedRefusals: refusals }));
  const alert = cardOf(tree, 'codex-a').find((node) => node.attrs['role'] === 'alert');

  assert.equal(alert.length, 1, 'a refusal of the other side\'s box is not drawn beside where the box was');
  assert.equal(alert[0]!.text(), reason, 'the refusal beside the sentence is not the host\'s reason');
});

test('with this side keeping its own settings and ONE side known the card draws one box, exactly as before', () => {
  const { tree } = opened(stateOf([row()], 'windows', ONLY_WINDOWS, { perSide: true }));

  assert.equal(boxesOf(tree, 'codex-a').length, 1);
  assert.ok(!cardOf(tree, 'codex-a').text().includes(KEEPS_ITS_OWN), 'one-field mode says the two-field sentence');
});

test('the host refuses the other side\'s box while this side keeps its own settings, whatever was typed; this side\'s box and shared settings are not touched', () => {
  const refuse = cliPathRefusal;
  const refused = /^'\/home\/me\/\.local\/bin\/codex' was not saved — this side keeps its own settings; set the other side's value from a window on that side\.$/u;

  assert.match(refuse('executablePath.posix', '/home/me/.local/bin/codex', 'windows', true), refused,
    'a WSL path typed into the WSL / Linux box of a Windows window that keeps its own settings would be saved into this side\'s own settings');
  assert.match(refuse('executablePath.windows', 'C:\\tools\\codex.exe', 'posix', true), /^'C:\\tools\\codex\.exe' was not saved — this side keeps its own settings/u, 'the mirror, from a WSL window');
  assert.match(refuse('executablePath.posix', '  ', 'windows', true), /^Clearing the box was not saved — this side keeps its own settings/u, 'clearing the other side\'s box is a write too');
  assert.equal(refuse('executablePath', 'C:\\tools\\codex.exe', 'windows', true), '', 'this side\'s own box was refused');
  assert.equal(refuse('model', 'gpt', 'windows', true), '', 'a field that is no CLI path was refused');
  assert.equal(refuse('executablePath.posix', '/home/me/.local/bin/codex', 'windows', false), '', 'shared settings refuse a WSL path in the WSL box');

  // What the provider hands in — `writesTheOverlay(config, 'vendors')` — by the switch.
  assert.equal(keptPerSide(true, 'vendors'), true, 'with coai.perSideSettings on, vendors is not called this side\'s own');
  assert.equal(keptPerSide(false, 'vendors'), false, 'with coai.perSideSettings off, vendors is called this side\'s own');
});

/** The other side's box of a row, by its key. */
function otherBox(tree: PageNode, id: string, family: PathFamily): PageNode {
  return tree.one((node) => node.tagName === 'INPUT' && node.dataset['vendor'] === id && node.dataset['setting'] === `executablePath.${family}`, `the ${family} box of ${id}`);
}

test('a legacy-only value spelled for THIS window\'s OS is not shown in the other side\'s box: empty, with a quiet hint whose it is', () => {
  const windows = opened(stateOf([row({ executablePath: 'C:\\tools\\codex.exe' })], 'windows', BOTH_FROM_WINDOWS)).tree;
  const wslBox = otherBox(windows, 'codex-a', 'posix');
  assert.equal(wslBox.value, '', `the WSL / Linux box shows '${wslBox.value}' — the Windows side's path, which the WSL side skips (a PATH lookup) and which the box itself would refuse if edited`);
  assert.ok(described(windows, wslBox).some((said) => said === 'The shared value C:\\tools\\codex.exe is this side\'s CLI; that side skips it and looks the CLI up on PATH.'),
    `the WSL / Linux box is not described by whose the shared value is: ${JSON.stringify(described(windows, wslBox))}`);
});

test('the mirror: a legacy-only POSIX value is not shown in the Windows box of a WSL window', () => {
  const wsl = opened(stateOf([row({ executablePath: '/usr/bin/codex' })], 'posix', BOTH_FROM_WSL)).tree;
  const windowsBox = otherBox(wsl, 'codex-a', 'windows');
  assert.equal(windowsBox.value, '', `the Windows box of a WSL window shows '${windowsBox.value}' — the WSL side's path`);
  assert.ok(described(wsl, windowsBox).some((said) => said.startsWith('The shared value /usr/bin/codex is this side\'s CLI')), 'the Windows box is not described by whose the shared value is');
});

test('the other side\'s box shows what is stored when it is that side\'s: a member of its own, a legacy value of that side\'s spelling, or one of neither', () => {
  const cases: readonly (readonly [Vendor, string])[] = [
    [row({ executablePath: 'C:\\tools\\codex.exe', executablePathBySide: { posix: '/opt/codex', mirrored: 'C:\\tools\\codex.exe' } }), '/opt/codex'],
    [row({ executablePath: '/usr/bin/codex' }), '/usr/bin/codex'],
    [row({ executablePath: 'codex' }), 'codex'],
    [row({ executablePath: '//nas/share/codex' }), '//nas/share/codex'],
  ];
  for (const [vendor, shown] of cases) {
    const tree = opened(stateOf([vendor], 'windows', BOTH_FROM_WINDOWS)).tree;
    const box = otherBox(tree, 'codex-a', 'posix');

    assert.equal(box.value, shown, `the WSL / Linux box of ${JSON.stringify(vendor.executablePath)} shows '${box.value}'`);
    assert.ok(!described(tree, box).some((said) => said.startsWith('The shared value')), `the box of ${shown} says the value is this side's`);
  }
});
