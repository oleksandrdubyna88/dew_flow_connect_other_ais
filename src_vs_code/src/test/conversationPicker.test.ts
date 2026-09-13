import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ConversationMeta } from '../chatStore';
import { IndexState } from '../chatStoreCache';
import {
  MOST_ROWS,
  OPEN_SECTION,
  OpenConversation,
  PickerInput,
  PickerRow,
  RECENT_SECTION,
  folderLabels,
  pickerRows,
  rowAge,
} from '../conversationPicker';

/**
 * What the picker OFFERS, decided without a host.
 *
 * <p>Pure for the reason every decision in this feature is: the rows are the whole of what a person
 * chooses between, and a rule about them buried in a `createQuickPick` call is a rule no test can
 * reach. The `vscode` half is the widget and nothing else.</p>
 */

const AT = Date.UTC(2026, 8, 13, 12, 0, 0);
const WORKSPACE = 'D:\\rsd\\one';
const READY: IndexState = { kind: 'ready', at: AT };
const UNAVAILABLE: IndexState = { kind: 'unavailable', reason: 'the conversation store could not be read (EACCES)', lastGoodAt: AT - 3_600_000 };

const meta = (over: Partial<ConversationMeta> = {}): ConversationMeta => ({
  version: 1,
  id: 'a1',
  rev: 3,
  title: 'Why the lock is fenced',
  modelId: 'gemini-3-pro',
  turns: 4,
  lastLine: 'because a read and a delete are two operations',
  source: { kind: 'none' },
  workspace: WORKSPACE,
  updatedAt: AT - 60_000,
  ...over,
});

const open = (over: Partial<OpenConversation> = {}): OpenConversation => ({
  id: 'a1',
  title: 'Why the lock is fenced',
  modelId: 'gemini-3-pro',
  turns: 4,
  lastLine: 'because a read and a delete are two operations',
  updatedAt: AT - 60_000,
  ...over,
});

/** The input, with everything a test does not care about filled in. */
const input = (over: Partial<PickerInput>): PickerInput => ({
  open: [], stored: [], index: READY, workspace: WORKSPACE, everywhere: false, now: AT, elsewhere: new Set<string>(), query: '', ...over,
});

const shown = (rows: readonly PickerRow[]): readonly string[] => rows.flatMap((row) => (row.kind === 'conversation' ? [row.id] : []));

const described = (rows: readonly PickerRow[], id: string): string => {
  const row = rows.find((one) => one.kind === 'conversation' && one.id === id);

  return row?.kind === 'conversation' ? row.description : '';
};

test('open conversations come first, and are never repeated among the closed ones', () => {
  // The ten-tabs case the whole feature exists for: what is already open is what a person is most
  // likely to be looking for, and offering it twice would make the list read as two conversations.
  const rows = pickerRows(input({ open: [open()], stored: [meta(), meta({ id: 'b2', title: 'The other one' })] }));

  assert.deepEqual(rows.map((row) => row.kind), ['section', 'conversation', 'section', 'conversation']);
  assert.equal(rows[0]?.kind === 'section' ? rows[0].label : '', OPEN_SECTION);
  assert.equal(rows[1]?.kind === 'conversation' ? rows[1].id : '', 'a1');
  assert.equal(rows[2]?.kind === 'section' ? rows[2].label : '', RECENT_SECTION);
  assert.equal(rows[3]?.kind === 'conversation' ? rows[3].id : '', 'b2',
    'a conversation that is open was offered a second time among the closed ones');
});

test('the picker keeps the order the index published, and sorts nothing itself', () => {
  // The index sorts once, when it publishes; a second sort here was a sort of the whole store before
  // the cut to a hundred, on every keystroke. The order given is the order shown. (The code round.)
  const rows = pickerRows(input({
    stored: [
      meta({ id: 'mid', updatedAt: AT - 60_000 }),
      meta({ id: 'old', updatedAt: AT - 900_000 }),
      meta({ id: 'new', updatedAt: AT - 1_000 }),
    ],
  }));

  assert.deepEqual(shown(rows), ['mid', 'old', 'new'], 'the picker re-sorted what the index had already ordered');
});

test('a stored row says what a person needs to tell two conversations apart', () => {
  const rows = pickerRows(input({ stored: [meta({ updatedAt: AT - 7_200_000 })] }));
  const row = rows.find((one) => one.kind === 'conversation');

  assert.equal(row?.kind === 'conversation' ? row.label : '', 'Why the lock is fenced');
  // The model, how much was said, and how long ago — the three things that separate two
  // conversations with the same title, which is the case the whole registry exists to handle.
  assert.match(row?.kind === 'conversation' ? row.description : '', /gemini-3-pro/u);
  assert.match(row?.kind === 'conversation' ? row.description : '', /4 turns/u);
  assert.match(row?.kind === 'conversation' ? row.description : '', /2 hours ago/u);
  assert.equal(row?.kind === 'conversation' ? row.detail : '', 'because a read and a delete are two operations',
    'the last thing said is not on the row, so two conversations with one title are indistinguishable');
});

test('an OPEN row carries the same facts as a stored one — the last line and how long ago included — and says it is open', () => {
  // Two open conversations sharing a title, a model and a turn count are the exact case the registry
  // exists to handle; a row without the last line and the age made them indistinguishable. (gemini.)
  const rows = pickerRows(input({
    open: [
      open({ id: 'a1', lastLine: 'the first answer', updatedAt: AT - 7_200_000 }),
      open({ id: 'b2', lastLine: 'the second answer', updatedAt: AT - 30_000 }),
    ],
  }));
  const row = (id: string): Extract<PickerRow, { kind: 'conversation' }> | undefined => {
    const found = rows.find((one) => one.kind === 'conversation' && one.id === id);

    return found?.kind === 'conversation' ? found : undefined;
  };

  assert.equal(row('a1')?.detail, 'the first answer', 'an open row omits the last line');
  assert.equal(row('b2')?.detail, 'the second answer');
  assert.match(row('a1')?.description ?? '', /2 hours ago/u, 'an open row omits how long ago');
  assert.match(row('b2')?.description ?? '', /just now/u);
  assert.match(row('a1')?.description ?? '', /open now/u, 'an open row does not say it is open');
  assert.equal(row('a1')?.where, 'here');
  assert.notEqual(row('a1')?.description, row('b2')?.description, 'two open conversations with one title, model and turn count are indistinguishable');
});

test('only this workspace, unless the person asked for all of them', () => {
  const stored = [meta({ id: 'here' }), meta({ id: 'there', workspace: 'D:\\rsd\\two' })];
  const mine = pickerRows(input({ stored }));
  const all = pickerRows(input({ stored, everywhere: true }));

  assert.deepEqual(shown(mine), ['here']);
  assert.equal(shown(all).length, 2);
  // And when they are shown, the row says WHERE, or two conversations from two projects look alike.
  assert.match(described(all, 'there'), /two/u, 'a conversation from another workspace does not say which');
});

test('two projects whose folders share a name are told apart by the folder above', () => {
  const rows = pickerRows(input({
    stored: [meta({ id: 'here', workspace: 'D:\\rsd\\coai' }), meta({ id: 'there', workspace: 'E:\\other\\coai' })],
    workspace: 'D:\\rsd\\coai',
    everywhere: true,
  }));

  assert.match(described(rows, 'here'), /rsd[\\/]coai/u, `two projects with one folder name render alike: ${described(rows, 'here')}`);
  assert.match(described(rows, 'there'), /other[\\/]coai/u, `two projects with one folder name render alike: ${described(rows, 'there')}`);
});

test('the folder is shown only when the rows actually span more than one', () => {
  // A folder name on every row of a list that is all one folder is noise — even with "everywhere" on.
  const rows = pickerRows(input({ stored: [meta({ id: 'a1' }), meta({ id: 'b2' })], workspace: 'D:\\rsd\\elsewhere', everywhere: true }));

  for (const row of rows) {
    if (row.kind === 'conversation') {
      assert.doesNotMatch(row.description, /\bone\b/u, `the folder is shown on every row of a list that is all one folder: ${row.description}`);
    }
  }
  assert.equal(shown(rows).length, 2);
});

test('folder labels: nothing for one folder, the name when names differ, as many folders above as it takes when they clash', () => {
  assert.deepEqual([...folderLabels(['D:\\rsd\\coai', 'D:\\rsd\\coai'])], [['D:\\rsd\\coai', '']]);
  assert.deepEqual([...folderLabels(['D:\\rsd\\coai', 'D:\\rsd\\bench'])], [['D:\\rsd\\coai', 'coai'], ['D:\\rsd\\bench', 'bench']]);
  // Two levels of clash: `a/x/coai`, `b/x/coai` and `c/y/coai` — the third is told apart one level up, the first two need two.
  assert.deepEqual(
    [...folderLabels(['/a/x/coai', '/b/x/coai', '/c/y/coai'])],
    [['/a/x/coai', 'a/x/coai'], ['/b/x/coai', 'b/x/coai'], ['/c/y/coai', 'y/coai']],
  );
  // Two spellings of one folder are told apart by their spelling, in each path's own separators.
  assert.deepEqual([...folderLabels(['D:\\rsd\\coai', 'D:/rsd/coai'])], [['D:\\rsd\\coai', 'rsd\\coai'], ['D:/rsd/coai', 'rsd/coai']]);
  // One folder spelled twice with nothing to tell the spellings apart stays alike — that is the truth — and the loop still ends.
  assert.deepEqual([...folderLabels(['D:\\rsd\\coai', 'D:\\rsd\\coai\\'])], [['D:\\rsd\\coai', 'D:\\rsd\\coai'], ['D:\\rsd\\coai\\', 'D:\\rsd\\coai']]);
  assert.deepEqual([...folderLabels(['', 'D:\\rsd\\coai'])], [['', 'no folder'], ['D:\\rsd\\coai', 'coai']]);
});

test('a conversation with no folder behind it is still offered, and says so when it stands beside one that has a folder', () => {
  // A window with no folder open is an ordinary way to work, and the store files those under the
  // empty string. Dropping them would hide a whole way of using the product.
  const alone = pickerRows(input({ stored: [meta({ id: 'loose', workspace: '' })], workspace: '' }));
  assert.equal(shown(alone).length, 1);

  const beside = pickerRows(input({ stored: [meta({ id: 'loose', workspace: '' }), meta({ id: 'a1' })], workspace: '', everywhere: true }));
  assert.match(described(beside, 'loose'), /no folder/u);
});

test('the NEW row is offered first when it is asked for, and never otherwise', () => {
  // `go to` opens the picker with this preselected when the active tab has no conversation. Nothing
  // is created until it is chosen — the operator's own rule: a hotkey that silently makes an empty
  // chat is the trap the two commands exist to avoid.
  const withNew = pickerRows(input({ stored: [meta()], offerNew: 'README.md' }));

  assert.equal(withNew[0]?.kind, 'new');
  assert.match(withNew[0]?.kind === 'new' ? withNew[0].label : '', /README\.md/u, 'the row does not name the tab the conversation would belong to');

  const without = pickerRows(input({ stored: [meta()] }));

  assert.notEqual(without[0]?.kind, 'new');
});

test('a store that would not answer says so, with the reason, instead of looking empty', () => {
  // The distinction A2 built the typed listing for. An empty list and a list that could not be read
  // are opposite facts, and only one of them means "you have no conversations".
  const rows = pickerRows(input({ index: UNAVAILABLE }));

  assert.equal(rows.length, 1);
  assert.equal(rows[0]?.kind === 'notice' ? rows[0].notice : '', 'unreadable');
  assert.match(rows[0]?.kind === 'notice' ? rows[0].label : '', /could not be read/u);
  assert.match(rows[0]?.kind === 'notice' ? rows[0].detail : '', /EACCES/u, 'the notice does not carry the reason the index gave');
});

test('a store that would not answer still lists what is OPEN, and the last good rows the index kept — said to be from before', () => {
  // The index keeps its last good rows when a refresh cannot look, so the picker can show them beside
  // the notice; and what is open comes from the registry, which no disk can take away.
  const rows = pickerRows(input({ open: [open({ id: 'live' })], stored: [meta({ id: 'kept' })], index: UNAVAILABLE }));

  assert.deepEqual(rows.map((row) => row.kind), ['section', 'conversation', 'notice', 'section', 'conversation']);
  assert.deepEqual(shown(rows), ['live', 'kept']);
  assert.match(rows[2]?.kind === 'notice' ? rows[2].detail : '', /as it was last read/u, 'the last good rows are shown without saying they may be behind');
});

test('an unreadable store still offers the NEW row, because that needs no store', () => {
  const rows = pickerRows(input({ index: UNAVAILABLE, offerNew: 'main.ts' }));

  assert.equal(rows[0]?.kind, 'new', 'a disk that would not answer took away the one action that does not need it');
});

test('while the index is still BUILDING the picker says so — never "no conversations" over a full store — and still lists what is open', () => {
  // At activation the index is empty until the sweep and the first refresh finish. A caller that
  // mapped that to an empty list showed "No conversations yet" over hundreds. (The code round.)
  const building = pickerRows(input({ index: { kind: 'building' }, offerNew: 'README.md' }));

  assert.deepEqual(building.map((row) => row.kind), ['new', 'notice']);
  assert.equal(building[1]?.kind === 'notice' ? building[1].notice : '', 'building');
  assert.match(building[1]?.kind === 'notice' ? building[1].label : '', /still being listed/u);
  assert.doesNotMatch(building[1]?.kind === 'notice' ? building[1].label : '', /No conversations/u, 'an index that is still building read as an empty store');

  const withOpen = pickerRows(input({ open: [open()], index: { kind: 'building' } }));
  assert.deepEqual(withOpen.map((row) => row.kind), ['section', 'conversation', 'notice'], 'what is open in a tab was hidden while the index was building');
});

test('nothing at all is one row saying so, not an empty list', () => {
  // An empty QuickPick reads as a broken command. It says what is true instead.
  const rows = pickerRows(input({}));

  assert.equal(rows.length, 1);
  assert.equal(rows[0]?.kind === 'notice' ? rows[0].notice : '', 'empty');
  assert.match(rows[0]?.kind === 'notice' ? rows[0].label : '', /in this folder/u);
  const everywhere = pickerRows(input({ everywhere: true }));
  assert.equal(everywhere[0]?.kind === 'notice' ? everywhere[0].label : '', 'No conversations yet');
});

test('the list is bounded, and says when it was cut', () => {
  // Ninety days at this operator's rate is hundreds of rows; a QuickPick that renders all of them is
  // slow to open for a list nobody scrolls to the end of. The cut is stated rather than silent.
  const many = Array.from({ length: 140 }, (_, at) => meta({ id: `c${at}`, updatedAt: AT - at * 1_000 }));
  const rows = pickerRows(input({ stored: many }));

  assert.equal(shown(rows).length, MOST_ROWS, 'the whole store was rendered into the picker');
  const more = rows.find((row) => row.kind === 'notice' && row.notice === 'more');
  assert.match(more?.kind === 'notice' ? more.label : '', /40 older/u, 'the list was cut and said nothing about it');
});

test('no row but a conversation carries an id — a notice, the cut line and the offer to start one have nothing a widget could try to open', () => {
  // Pressing Enter on any non-separator row of a QuickPick fires accept. A row that is not a
  // conversation must therefore have nothing an accept handler could open — expressed in the row
  // union, so the compiler asks the widget, rather than as a rule story B3 has to remember.
  const many = Array.from({ length: 140 }, (_, at) => meta({ id: `c${at}`, updatedAt: AT - at * 1_000 }));
  const everyKind = [
    ...pickerRows(input({ index: UNAVAILABLE, offerNew: 'main.ts' })),
    ...pickerRows(input({ index: { kind: 'building' } })),
    ...pickerRows(input({})),
    ...pickerRows(input({ stored: many })),
  ];
  const withId = everyKind.filter((row) => 'id' in row);

  assert.ok(withId.length > 0, 'the test built no row at all');
  assert.deepEqual(withId.filter((row) => row.kind !== 'conversation').map((row) => row.kind), [],
    'a row that is not a conversation carries an id the widget could try to open');
  assert.deepEqual([...new Set(everyKind.map((row) => row.kind))].sort(), ['conversation', 'new', 'notice', 'section'], 'the test did not build every kind of row');
});

test('how long ago reads the way a person would say it', () => {
  assert.equal(rowAge(AT, AT - 30_000), 'just now');
  assert.equal(rowAge(AT, AT - 90_000), 'a minute ago');
  assert.equal(rowAge(AT, AT - 20 * 60_000), '20 minutes ago');
  assert.equal(rowAge(AT, AT - 3 * 3_600_000), '3 hours ago');
  assert.equal(rowAge(AT, AT - 26 * 3_600_000), 'yesterday');
  assert.equal(rowAge(AT, AT - 5 * 86_400_000), '5 days ago');
  // And a clock that stepped backwards is not a conversation from the future.
  assert.equal(rowAge(AT, AT + 60_000), 'just now');
});
test('a conversation ANOTHER window holds is drawn as such, and never as one this window can reopen', () => {
  // The defect three reviewers found independently, and the one I had flagged myself: a tab in
  // another window cannot be revealed from this one — VS Code offers no way to raise a window an
  // extension is not running in — so it used to be drawn as closed, and pressing it opened a SECOND
  // tab on one record. Two writers, and the store's compare-and-swap forks the conversation and
  // reports it afterwards: the product causing the exact accident that swap exists to catch.
  const rows = pickerRows(input({
    stored: [meta({ id: 'mine' }), meta({ id: 'theirs', title: 'Held over there' })],
    elsewhere: new Set(['theirs']),
  }));
  const row = (id: string): Extract<PickerRow, { kind: 'conversation' }> | undefined => {
    const found = rows.find((one) => one.kind === 'conversation' && one.id === id);

    return found?.kind === 'conversation' ? found : undefined;
  };

  assert.equal(row('theirs')?.where, 'elsewhere', 'a conversation another window holds is offered as reopenable');
  assert.equal(row('mine')?.where, 'closed', 'a conversation nobody holds is not reopenable');
  // And it is visible BEFORE the press, not only in the sentence after it: a person scanning the
  // list should see which rows this window can actually take them to.
  assert.match(row('theirs')?.description ?? '', /another window/iu, 'the row does not say where it is');
  assert.doesNotMatch(row('mine')?.description ?? '', /another window/iu);
});

test('this window’s OWN open conversations are never called elsewhere, whatever a heartbeat says', () => {
  // A window's own heartbeat names what it holds and is written at most once a minute, so it can
  // still name a conversation whose tab closed seconds ago. `heldElsewhere` leaves this window's own
  // file out for exactly that reason; if a caller passed it in anyway, the open row must still win —
  // it is the row the picker can answer instantly.
  const rows = pickerRows(input({ open: [open()], stored: [meta()], elsewhere: new Set(['a1']) }));
  const drawn = rows.filter((row) => row.kind === 'conversation' && row.id === 'a1');

  assert.equal(drawn.length, 1, 'one conversation was drawn twice');
  assert.equal(drawn[0]?.kind === 'conversation' ? drawn[0].where : '', 'here');
});

test('what is TYPED is matched before the hundred-row cut, or an older conversation cannot be found at all', () => {
  // The hole the cut left, and the one thing this list exists to do. QuickPick filters the items it
  // was handed; the list hands it a hundred; so conversation number a hundred and one could not be
  // found by typing its own title, however exactly it was typed. Filtering first is what makes the
  // search a search. (codex, the code round.)
  const many = Array.from({ length: 140 }, (_, at) => meta({ id: `c${at}`, updatedAt: AT - at * 1_000 }));
  const buried = meta({ id: 'buried', title: 'The lock is fenced with a token', updatedAt: AT - 200_000_000 });

  const unfiltered = pickerRows(input({ stored: [...many, buried] }));
  assert.equal(shown(unfiltered).includes('buried'), false, 'the test did not manage to bury the conversation past the cut');

  const found = pickerRows(input({ stored: [...many, buried], query: 'fenced with a token' }));
  assert.deepEqual(shown(found), ['buried'], 'a conversation past the cut could not be found by typing its own title');
});

test('a query matches the title, the model or the last line — the three the widget filters on', () => {
  const stored = [
    meta({ id: 'byTitle', title: 'Why the lock is fenced', modelId: 'gpt-5.4', lastLine: 'nothing to see' }),
    meta({ id: 'byModel', title: 'nothing to see', modelId: 'gemini-3-pro', lastLine: 'nothing to see' }),
    meta({ id: 'byLine', title: 'nothing to see', modelId: 'gpt-5.4', lastLine: 'because a read and a delete are two' }),
  ];

  assert.deepEqual(shown(pickerRows(input({ stored, query: 'FENCED' }))), ['byTitle'], 'the title is not matched, or not case-blind');
  assert.deepEqual(shown(pickerRows(input({ stored, query: 'gemini' }))), ['byModel'], 'the model is not matched');
  assert.deepEqual(shown(pickerRows(input({ stored, query: 'a delete' }))), ['byLine'], 'the last line is not matched');
  assert.equal(shown(pickerRows(input({ stored, query: '' }))).length, 3, 'an empty query filtered something out');
  assert.deepEqual(shown(pickerRows(input({ stored, query: 'nothing like this' }))), []);
});

test('an OPEN conversation is never filtered away by what is typed — the widget filters those itself', () => {
  // Only the stored rows are cut, so only they need matching before it. Filtering the open ones here
  // as well would mean two filters over one list, and this one is a plain substring while the
  // widget's is fuzzy — a row would vanish that the widget was willing to show.
  const rows = pickerRows(input({ open: [open()], stored: [], query: 'nothing like this' }));

  assert.deepEqual(shown(rows), ['a1']);
});
