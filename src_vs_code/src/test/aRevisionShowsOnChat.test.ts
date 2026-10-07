import assert from 'node:assert/strict';
import { test } from 'node:test';
import { catalogHtml } from '../catalogPage';
import type { ModelPreset } from '../chatPresets';
import { chatMove } from '../chatPresetMove';
import { revisionStoresReading, revisionWrites } from '../chatPresetRevision';
import { presetEdit, type RevisionChoice } from '../chatPresetsMessages';
import { chatSettingsFrom } from '../chatSettings';
import type { PanelState } from '../panelView';
import { DEFAULT_VENDORS } from '../vendors';
import { panelState } from './panelPageHarness';
import { bubbled, pageTree, selectorsOf, type PageNode } from './pageTree';
import { runPageHtml } from './pageScriptHarness';
import { sourceOf } from './sourceReading';

/**
 * Epic 5 prerequisite (a) of todo/PLAN_one_model_catalog.md, R7, on the page: a chat preset an older build edited after
 * the move is raised on Chat — the Settings page's Chat tab, beside the stranded pick — with the row as it is, the edited
 * values, and two choices, each of which posts exactly its own message into the presets' editing core. The page is
 * RUN against the DOM shim, drawn from a settings file through the same reader the panel uses, and the message it posts
 * is carried back through the host's decision to the file, read back as a reload reads it.
 */

/** The settings file, as far as the Chat tab and a choice read and write it. */
type SettingsFile = Readonly<Record<string, unknown>>;

const preset = (extra: Partial<ModelPreset> = {}): ModelPreset => ({
  id: 'p-1', name: 'Deep', runtime: 'claude', model: 'opus', main: false, executablePath: 'C:\\coai\\claude.exe', baseUrl: '',
  startingPrompt: 'You review APIs.', ...extra,
});

/** The edit an older build made after the move: another CLI. */
const EDITED = preset({ executablePath: 'D:\\tools\\claude.exe' });

/** This build moved `preset()`; the presets now say `presets`, as an older build left them. */
function fileWith(presets: readonly ModelPreset[]): SettingsFile {
  const moved = chatMove({ presets: [preset()], rows: DEFAULT_VENDORS.map((row) => ({ ...row })), record: [] });

  return { vendors: moved.rows, chatPresetsMoved: moved.record, chatModelPresets: presets };
}

const readerOf = (file: SettingsFile) => (key: string): unknown => file[key];

function stateOf(file: SettingsFile): PanelState {
  return {
    ...panelState('reviewers'),
    chat: chatSettingsFrom(readerOf(file)),
    commands: { rows: [], texts: {}, serverVersion: '', perSide: false },
    roles: { rows: [], texts: {}, serverVersion: '', perSide: false, stranded: [] },
  };
}

const AT_LOAD = ['[data-setting]', '[data-prompt]', '[data-command]', '[data-tab]', '[data-pane]'];

/** The new page on Chat, drawn from the file and running. */
function chatPage(file: SettingsFile) {
  const html = catalogHtml(stateOf(file), 'test-nonce', 'chat');
  const tree = pageTree(html);
  const page = runPageHtml(html, selectorsOf(tree, AT_LOAD), undefined, { value: undefined });

  return { page, pane: tree.one((node) => node.dataset.pane === 'chat', 'Chat pane') };
}

const conflictsIn = (pane: PageNode): readonly PageNode[] => pane.find((node) => node.dataset.chpConflict !== undefined);

/**
 * The presets the drawn conflict blocks name — compared as strings: a node holds its parent, and a failed deep compare
 * of the nodes themselves spends minutes printing the whole page instead of failing.
 */
const conflictIds = (pane: PageNode): readonly string[] => conflictsIn(pane).map((node) => node.dataset.chpConflict ?? '');

const choiceIn = (pane: PageNode, choice: RevisionChoice): PageNode =>
  pane.one((node) => node.dataset.chpRevision === choice && node.tagName === 'BUTTON', `the ${choice} button`);

/** Every message a press can post that changes something — what "no other row's action" is checked against. */
const ACTIONS = ['chatPresets', 'roles', 'commands', 'command', 'setting', 'prompt'];

test('the message is read like its neighbours: a preset id and one of the two choices, or nothing', () => {
  assert.deepEqual(presetEdit({ type: 'revision', id: 'p-1', choice: 'use' }), { kind: 'revision', presetId: 'p-1', choice: 'use' });
  assert.deepEqual(presetEdit({ type: 'revision', id: 'p-1', choice: 'keep' }), { kind: 'revision', presetId: 'p-1', choice: 'keep' });
  for (const junk of [{ type: 'revision', id: 'p-1', choice: 'drop' }, { type: 'revision', id: '', choice: 'use' }, { type: 'revision', choice: 'keep' },
    { type: 'revision', id: 'p-1', choice: '__proto__' }, { type: 'revision', id: 7, choice: 'use' }]) {
    assert.deepEqual(presetEdit(junk), { kind: 'ignore' }, JSON.stringify(junk));
  }
});

test('the conflict is drawn on Chat: the row as it is, the edited values, and the two choices', () => {
  const { pane } = chatPage(fileWith([EDITED]));
  const [block] = conflictsIn(pane);

  assert.ok(block !== undefined, 'no conflict block on Chat');
  assert.equal(block.dataset.chpConflict, 'p-1');
  assert.match(block.text(), /Deep/u);
  assert.ok(block.text().includes('C:\\coai\\claude.exe'), 'the row as it is is not shown');
  assert.ok(block.text().includes('D:\\tools\\claude.exe'), 'the edited value is not shown');
  assert.deepEqual([choiceIn(block, 'use').text().trim(), choiceIn(block, 'keep').text().trim()], ['Use the edited values', 'Keep the row']);
});

test('an unedited preset raises nothing on Chat', () => {
  assert.deepEqual(conflictIds(chatPage(fileWith([preset()])).pane), []);
});

for (const choice of ['use', 'keep'] as const) {
  test(`pressing ${choice} posts exactly its own message, numbered — no other row's action`, () => {
    const { page, pane } = chatPage(fileWith([EDITED]));

    bubbled(page, 'click', choiceIn(pane, choice));

    const sent = page.posted.filter((one) => ACTIONS.includes(String(one['type'])));
    assert.deepEqual(sent.map(({ type, edit }) => ({ type, edit })), [{ type: 'chatPresets', edit: { type: 'revision', id: 'p-1', choice } }]);
    assert.ok(sent[0]?.['seq'] !== undefined, 'the press carries no busy mark');
  });
}

/** The page's own message, through the host's decision, written and read back as a reload reads it. */
function afterPress(file: SettingsFile, choice: RevisionChoice): SettingsFile {
  const { page, pane } = chatPage(file);
  bubbled(page, 'click', choiceIn(pane, choice));
  const command = presetEdit(page.posted.find((one) => one['type'] === 'chatPresets')?.['edit']);
  assert.equal(command.kind, 'revision', `the host does not read the page's message: ${JSON.stringify(command)}`);
  const writes = command.kind === 'revision'
    ? revisionWrites(command.choice, command.presetId, revisionStoresReading(file['chatModelPresets'], readerOf(file)))
    : [];

  return JSON.parse(JSON.stringify(writes.reduce<SettingsFile>((acc, one) => ({ ...acc, [one.key]: one.value }), file))) as SettingsFile;
}

const rowOf = (file: SettingsFile): Readonly<Record<string, unknown>> | undefined =>
  revisionStoresReading([], readerOf(file)).rows.find((row) => row['id'] === 'chat-p-1');

test('Use the edited values: the row takes them, the record takes the preset, and Chat is clear after a reload', () => {
  const saved = afterPress(fileWith([EDITED]), 'use');

  assert.equal(rowOf(saved)?.['executablePath'], 'D:\\tools\\claude.exe');
  assert.equal(revisionStoresReading([], readerOf(saved)).record[0]?.copied?.executablePath, 'D:\\tools\\claude.exe');
  assert.deepEqual(conflictIds(chatPage(saved).pane), []);
});

test('Keep the row: the row unchanged, the record takes the preset, and Chat is clear after a reload', () => {
  const file = fileWith([EDITED]);
  const saved = afterPress(file, 'keep');

  assert.deepEqual(rowOf(saved), rowOf(file));
  assert.equal(revisionStoresReading([], readerOf(saved)).record[0]?.copied?.executablePath, 'D:\\tools\\claude.exe');
  assert.deepEqual(conflictIds(chatPage(saved).pane), []);
  // And a later, different edit is raised again.
  assert.deepEqual(conflictIds(chatPage({ ...saved, chatModelPresets: [preset({ executablePath: 'E:\\claude.exe' })] }).pane), ['p-1']);
});

test('the host routes the choice to that decision, in the catalog\'s turn, through this side, and never writes the presets', () => {
  const host = sourceOf('chatPresetsHost.ts');

  assert.match(host, /command\.kind === 'revision'/u, 'the host never reads a revision');
  assert.match(host, /inCatalogTurn\(async \(\) => \{\s*const writes = revisionWrites\(command\.choice, command\.presetId, revisionStoresReading\(userLayer\(config\(\)\)\(MODELS_KEY\), chatRead\(config\(\)\)\)\);/u);
  assert.match(host, /await saveSetting\(side, config\(\), one\.key, one\.value\);/u);
});
