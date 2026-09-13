import assert from 'node:assert/strict';
import { test } from 'node:test';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * That the picker is actually REACHABLE, and that its widget obeys the rules the pure modules cannot
 * enforce — the half no unit test can see.
 *
 * <p>`conversationPicker.ts` decides the rows and `conversationChoice.ts` decides what choosing one
 * means; both are tested as values. Between them and a person stands a `createQuickPick`, a manifest
 * and five lines of `activate`, and every one of those can be wrong while the suite is green: a
 * command nothing registers, a keybinding scoped to a surface that does not exist, a widget that
 * builds its own rows instead of asking, a forget that closes the picker it was supposed to leave
 * open. So this reads the source, the way `chatWiring.test.ts` and `chatStoreWiring.test.ts` do here
 * for the same reason.</p>
 */

const ROOT = process.cwd();
const read = (relative: string): string => readFileSync(join(ROOT, relative), 'utf8');

interface Contribution {
  readonly command?: string;
  readonly key?: string;
  readonly mac?: string;
  readonly when?: string;
  readonly group?: string;
}

interface Manifest {
  readonly contributes: {
    readonly commands: readonly { readonly command: string; readonly title: string }[];
    readonly keybindings: readonly Contribution[];
    readonly menus: Readonly<Record<string, readonly Contribution[]>>;
  };
}

const MANIFEST = JSON.parse(read('package.json')) as Manifest;
const SWITCH = 'coai.switchConversations';
const FORGET = 'coai.forgetPickedConversation';
const PANEL = 'claudeVSCodePanel';
/** The context key that is true for exactly as long as OUR picker is on screen. */
const KEY = 'coai.conversationsPickerOpen';

const widget = (): string => read(join('src', 'conversationPickerCommand.ts'));

test('the picker has a command, the manifest declares it, and activate registers it', () => {
  const declared = MANIFEST.contributes.commands.find((row) => row.command === SWITCH);

  assert.ok(declared !== undefined, `${SWITCH} is not in contributes.commands`);
  assert.match(declared.title, /switch conversations/iu, 'the palette entry does not say what it opens');
  assert.match(read('src/extension.ts'), /registerCommand\('coai\.switchConversations'/u,
    'the manifest declares a command nothing registers — the palette entry would do nothing');
});

test('the chord is Ctrl+Shift+Alt+G, has a macOS form, and reaches the picker from ANYWHERE', () => {
  // `Ctrl+Alt` is `AltGr` on Windows, which is why this feature's chords end in G rather than in the
  // letters proposed first — `AltGr+E` types a currency symbol on the layouts in use here.
  //
  // AND IT IS DELIBERATELY UNSCOPED, which is the opposite of what this test asserted when it was
  // written. This picker reads NOTHING from the active tab: it is a global switcher over every
  // conversation, so a `when` clause would only take it away from the places a person most needs it
  // — a terminal, the Output pane, a settings tab, the chat tab itself. The operator's own decision
  // 7 says so: from a tab with no conversation behind it, the answer is the list, not silence.
  //
  // The other command is different and stays scoped: `go to` (story C2) acts on the active tab, so
  // it has something to be scoped TO. This one does not.
  const bound = MANIFEST.contributes.keybindings.filter((row) => row.command === SWITCH);

  assert.equal(bound.length, 1, 'the global switcher is bound more than once, or not at all');
  assert.equal(bound[0]?.key, 'ctrl+shift+alt+g');
  assert.equal(bound[0]?.mac, 'cmd+shift+alt+g', 'the chord is unreachable on macOS, where ctrl is not the modifier');
  assert.equal(bound[0]?.when, undefined,
    'the chord was scoped to a tab, so it does nothing in a terminal or the Output pane — where a person most needs a list of their conversations');
});

test('the picker is in both right-click menus, each scoped where the existing chat items are', () => {
  const inPanel = (MANIFEST.contributes.menus['webview/context'] ?? []).filter((row) => row.command === SWITCH);
  const inEditor = (MANIFEST.contributes.menus['editor/context'] ?? []).filter((row) => row.command === SWITCH);

  assert.equal(inPanel.length, 1, 'the item is missing from the assistant panel’s menu, or declared twice');
  assert.equal(inEditor.length, 1, 'the item is missing from the editor’s menu, or declared twice');
  assert.match(inPanel[0]?.when ?? '', new RegExp(`webviewId == '${PANEL}'`), 'an item escaped the panel it belongs to');
  assert.match(inEditor[0]?.when ?? '', /editorTextFocus/u, 'the editor item is offered where there is no editor');
  // Beside the chat items rather than above them: this one does not take the selection anywhere.
  assert.match(inEditor[0]?.group ?? '', /^coai@/u, 'the item left the group the chat items share');
});

test('forgetting is bound to Alt+Delete and scoped to OUR picker being open', () => {
  // `inQuickOpen` alone would take Alt+Delete away from every other QuickPick in the editor. The
  // context key is what narrows it to this one, and it is the reason the key exists at all.
  const bound = MANIFEST.contributes.keybindings.filter((row) => row.command === FORGET);

  assert.equal(bound.length, 1, 'forgetting has no chord, or more than one');
  assert.equal(bound[0]?.key, 'alt+delete');
  assert.match(bound[0]?.when ?? '', /inQuickOpen/u, 'the chord fires outside a quick pick, where there is no row');
  assert.match(bound[0]?.when ?? '', new RegExp(KEY.replace('.', '\\.')), 'the chord fires in every quick pick, not only ours');
  // AND A DIFFERENT CHORD ON macOS. The focus in a QuickPick is always in its filter box, and on
  // macOS Alt+Delete is the system's own delete-word-forward: a person editing what they had typed
  // would have deleted a conversation instead of a word. Cmd+Delete is that platform's move-to-trash,
  // which is now literally what this does. (gemini, the plan round.)
  assert.equal(bound[0]?.mac, 'cmd+delete', 'on macOS the chord is the system word-delete, and editing the filter would forget a conversation');
});

test('forgetting is not offered in the palette, because from there it acts on nothing', () => {
  // It is meaningful only while a row is under the cursor. Offering it in the palette is offering a
  // command that closes the palette and then finds no picker — the same reasoning that hides
  // `coai.answerQuestionWaiting`.
  const hidden = (MANIFEST.contributes.menus['commandPalette'] ?? []).find((row) => row.command === FORGET);

  assert.ok(hidden !== undefined, 'the forget command is offered in the palette, where it has no row to act on');
  assert.equal(hidden.when, 'false');
});

test('the context key is set when the picker is shown and cleared when it hides', () => {
  // A key left true outlives the picker, and Alt+Delete then belongs to us in every quick pick the
  // editor opens afterwards — a delete keybinding pointed at a list that is not ours.
  const text = widget();
  const set = text.indexOf(`'${KEY}', true`);
  const cleared = text.indexOf(`'${KEY}', false`);

  assert.notEqual(set, -1, 'nothing sets the context key, so Alt+Delete is never scoped to this picker');
  assert.notEqual(cleared, -1, 'nothing clears the context key, so Alt+Delete stays ours after the picker has gone');
  assert.match(text, /onDidHide\(/u, 'nothing watches for the picker being dismissed');
});

test('the rows come from pickerRows and the widget decides none of them', () => {
  // The whole reason `conversationPicker.ts` is pure. A widget that assembled even one row of its own
  // — the notice, the separator, anything — would be a rule about what a person sees that no test can
  // reach.
  const text = widget();

  assert.match(text, /pickerRows\(\{/u, 'the widget builds its own items instead of asking for them');
  assert.doesNotMatch(text, /QuickPickItemKind\.Separator[^\n]*\n[\s\S]{0,200}?label: '/u,
    'the widget writes a section heading of its own');
});

test('it is createQuickPick, and the deviation is explained in the module’s own header', () => {
  // Six `showQuickPick` sites in this repository and no `createQuickPick` before this one. Item
  // buttons, a title button and a list that is rebuilt while it stays open need the instance, and a
  // deviation from a house style that is not explained where a reader meets it is one the next person
  // will "fix".
  const text = widget();
  const header = text.slice(0, text.indexOf('export'));
  const code = text.slice(text.indexOf('export'));

  assert.match(text, /vscode\.window\.createQuickPick</u, 'the picker cannot carry buttons or survive a rebuild');
  assert.doesNotMatch(code, /showQuickPick/u, 'the widget is the one this module exists to avoid');
  assert.match(header, /showQuickPick/u, 'the header does not name the house style it departs from');
});

test('choosing a closed conversation re-reads the record before anything is opened', () => {
  // The index is built in the background and a row can be chosen an hour later. Opening the metadata
  // the row was drawn from would put a stale transcript on screen, and opening a record another
  // window forgot would put a blank tab there.
  const text = widget();

  assert.match(text, /await deps\.store\.read\(/u, 'the picker opens what the index remembered rather than what is on disk');
  assert.match(text, /opening\(/u, 'the widget decides for itself what the store’s answer means');
  assert.match(text, /restoreConversation\(/u, 'a closed conversation is reopened by something other than the one path that builds a chat tab');
});

test('a conversation the store no longer has is dropped from the index as well as reported', () => {
  // Otherwise the row is still there on the next keystroke, and the person presses it again.
  const text = widget();
  const gone = text.slice(text.indexOf("case 'gone'"));

  assert.ok(gone.length > 0, 'the gone answer is not handled at all');
  assert.match(gone.slice(0, 600), /deps\.index\.drop\(/u, 'the row stays on the list after the conversation it names has gone');
});

test('an OPEN conversation is revealed, never reopened', () => {
  // Reopening one would build a second tab for a conversation that already has one, and the store
  // would then have two writers for one record — which is the fork the swap exists to catch, caused
  // by us.
  const text = widget();

  assert.match(text, /revealConversation\(panels,/u, 'an open conversation is not revealed by the registry that holds it');
  // THREE states, not two. A tab in ANOTHER window cannot be revealed from this one and must not be
  // reopened either, so the widget has to tell that case from a conversation nobody holds.
  assert.notEqual(text.indexOf('row.where'), -1, 'the widget does not tell an open conversation from a closed one');
  assert.match(text, /row\.where === 'elsewhere'/u, 'the widget reopens a conversation another window has open, giving one record two writers');
  const declines = text.indexOf("row.where === 'elsewhere'");
  assert.ok(declines > text.indexOf('revealConversation(panels,'),
    'the other window is refused before this one is asked — a tab adopted here since the list was drawn would be refused');
});

test('one accept per picker, so two presses cannot open one conversation twice', () => {
  // The widget is hidden by the first accept and VS Code dispatches its events in order, so a second
  // should not arrive — but what a second WOULD do is build a second tab for one record, which is the
  // single accident this command exists to prevent. A latch is cheaper than the argument that it
  // cannot happen. (The plan round.)
  const text = widget();

  assert.match(text, /let chosen = false;/u, 'nothing stops a second accept');
  assert.match(text, /if \(chosen \|\|/u, 'the latch is not read before the row is acted on');
  const accept = text.slice(text.indexOf('onDidAccept'));
  assert.ok(accept.indexOf('chosen = true;') < accept.indexOf('void choose('),
    'the latch is set after the work starts, which is not a latch');
});

test('forgetting rebuilds the list without closing the picker, from both doors', () => {
  // "The picker stays open" is the whole of what B4 adds beyond a delete: a trash press that closed
  // the window would make forgetting three conversations three openings of the picker.
  const text = widget();

  assert.match(text, /keepScrollPosition = true/u, 'the list jumps to the top whenever it is rebuilt');
  assert.match(text, /onDidTriggerItemButton\(/u, 'no row carries a button, so there is no trash');
  assert.match(text, /export function forgetPickedConversation/u, 'the keybinding has nothing to call');
  const body = text.slice(text.indexOf('const forget ='), text.indexOf('const forgetRow ='));
  assert.ok(body.length > 200, 'there is no one place a conversation is forgotten');
  assert.doesNotMatch(body, /pick\.hide\(\)/u, 'forgetting hides the picker it was supposed to leave open');
  assert.equal(body.split('draw()').length - 1, 2,
    'the list is redrawn on one outcome of a forget and not the other — a row that could not be deleted must still be shown');
});

test('the trash button is drawn on closed rows and on nothing else', () => {
  // An open conversation cannot be forgotten — its tab would write the files again — so offering the
  // button on one is offering an action that fails.
  const text = widget();
  const item = text.slice(text.indexOf('const itemFor'), text.indexOf('const itemFor') + 1_200);

  assert.ok(item.length > 0, 'there is no one place a row becomes an item');
  assert.match(item, /row\.where === 'closed' \? \[TRASH\] : \[\]/u, 'the trash button is drawn without asking where the row is — an open tab would write the files again');
});

test('a picker dismissed while something is in flight is not redrawn', () => {
  // A forget is awaited and the refresh is awaited, and Escape is pressed in between often enough to
  // matter. Assigning `items` to a QuickPick that has been disposed throws, inside a handler nothing
  // is above — the same shape as the tab closed during the reload wait (CodeRabbit, PR #223). The
  // guard is in `draw` itself rather than at each of its callers, because there are four of them.
  const text = widget();
  const body = text.slice(text.indexOf('const draw ='), text.indexOf('const choose ='));

  assert.ok(body.length > 200, 'there is no one place the list is rebuilt');
  assert.match(body.slice(0, 400), /if \(!onScreen\)\s*\{[\s\S]{0,400}?return;/u,
    'the list is rebuilt without asking whether it is still on screen');
  assert.match(text, /onScreen = false/u, 'nothing ever records that the picker has gone');
});

test('the scope button toggles and rebuilds, and does not reopen the picker', () => {
  const text = widget();
  const button = text.slice(text.indexOf('onDidTriggerButton('));

  assert.ok(button.length > 0, 'the title carries no button, so there is no way to look in other folders');
  assert.match(button.slice(0, 400), /everywhere = !everywhere/u, 'the button does something other than toggle the scope');
  assert.match(button.slice(0, 400), /draw\(\)/u, 'the scope changes and the list does not');
});

test('the picker opens on what the index already holds, and refreshes behind it', () => {
  // Thousands of files at ninety days: a picker that lists the directory before it draws anything is
  // a picker nobody uses, which is why the index is built at activation. But a window left open for a
  // day has an index a day old, so the refresh happens — behind the rows, with the widget already on
  // screen.
  const text = widget();
  const shown = text.indexOf('pick.show()');
  const refreshed = text.indexOf('deps.index.refresh(');

  assert.notEqual(shown, -1, 'the picker is never shown');
  assert.notEqual(refreshed, -1, 'the picker never brings the index up to date, so a conversation from another window is invisible');
  assert.ok(shown < refreshed, 'the refresh is awaited before the picker is drawn, so it opens as slowly as the disk');
});

test('every detached edge in the widget ends in a catch that says something', () => {
  // A QuickPick handler cannot be awaited by anybody: an event handler returns void, so a rejection
  // inside one is unhandled. Each of them is the outer edge of a detached call, and the reliability
  // rule says such an edge ends in a catch that speaks.
  const text = widget();
  const detached = [...text.matchAll(/void [a-zA-Z]+\(/gu)].length;
  const caught = [...text.matchAll(/\.catch\(/gu)].length;

  assert.ok(detached > 0, 'nothing is detached here — this guard is watching a door that moved');
  assert.ok(caught >= 1, 'a detached call in the widget has no owner for its failure');
  assert.match(text, /console\.(error|warn)\(/u, 'a failure in the picker is swallowed');
});

/**
 * The three things `chatCommand.ts` had to grow for this story, read the same way.
 *
 * <p>They live there because the `Thread` map is private to that file — the registry knows keys and
 * labels, and everything the picker describes a conversation by is on the thread. The file is far
 * over the size rule already, so what went in is three accessors and no decision.</p>
 */

const command = (): string => read(join('src', 'chatCommand.ts'));

test('the picker is told what is open by the same facts a stored row carries', () => {
  // Ids alone is what `heldConversationIds` gives, and a list of ids is not a list a person can
  // choose from. Two open conversations sharing a title are exactly the case the registry exists for,
  // and the model, the turn count and the last line are what tell them apart.
  const text = command();
  const body = text.slice(text.indexOf('export function openConversations'), text.indexOf('export function revealConversation'));

  assert.ok(body.length > 0, 'nothing tells the picker what this window holds open');
  for (const fact of ['title', 'modelId', 'turns', 'lastLine', 'updatedAt']) {
    assert.ok(body.includes(fact), `an open row would carry no ${fact}`);
  }
  // Derived from the record rather than counted here: `metaOf` is what a stored row is built by, and
  // two derivations of "the last line" is two ways for an open row and a closed one to describe the
  // same conversation differently.
  assert.match(body, /metaOf\(/u, 'the open rows are described by a second derivation of their own');
});

test('a conversation last used is stamped when something CHANGED, never on a repaint', () => {
  // `show` runs on every push — a turn starting, a queue position moving, a failure clearing. Stamping
  // above the guard would put a conversation nobody spoke in at the top of the list, which is the same
  // defect A4 fixed for the store's own `updatedAt` when it ruled that a reload is not a use.
  const text = command();
  const guard = text.indexOf('thread.savedMessages = thread.messages;');
  const stamp = text.indexOf('thread.usedAt =');

  assert.notEqual(stamp, -1, 'nothing records when a conversation was last used, so every open row reads “just now”');
  assert.ok(guard !== -1 && stamp > guard, 'the stamp is taken before the guard that decides whether anything changed');
});

test('an open conversation is revealed by its store id, which is the only name the picker has for it', () => {
  const text = command();
  const body = text.slice(text.indexOf('export function revealConversation'), text.indexOf('export function revealConversation') + 900);

  assert.ok(body.length > 0, 'nothing can reveal a conversation the picker names');
  assert.match(body, /\.panel\.reveal\(\)/u, 'the reveal does not reveal');
  assert.match(body, /saveId === id/u, 'the reveal matches a conversation by something other than its store id');
});

test('a conversation can be restored WITHOUT a panel, and the panel is then created rather than assumed', () => {
  // Its only caller was the reload serializer, which is handed one by VS Code. The picker is its first
  // caller without one — and `createChatPanel` has always taken the panel as optional, so what this
  // asserts is that the optionality reaches it rather than being defended against here.
  const text = command();
  const signature = text.slice(text.indexOf('export function restoreConversation'), text.indexOf('export function restoreConversation') + 400);

  assert.match(signature, /panel: vscode\.WebviewPanel \| undefined/u,
    'restoreConversation still demands a panel, so the picker cannot reopen anything');
  const body = text.slice(text.indexOf('export function restoreConversation'));
  assert.doesNotMatch(body.slice(0, 5_500), /vscode\.window\.createWebviewPanel/u,
    'a second place builds a chat panel — the icon, the wiring and the disposal are set up in createChatPanel and nowhere else');
});

test('the picker is not closed until a tab is actually on screen', () => {
  // Every way choosing can fail says something about THIS LIST — "it has been taken off this list",
  // "the row stays", "it is open in another window". A picker hidden before the work made all three
  // false and threw the person back to their editor to read a toast about a list that was no longer
  // there. It waits, busy, and closes only when something opened. (gemini, the code round.)
  const text = widget();
  const accept = text.slice(text.indexOf('pick.onDidAccept'));
  const body = accept.slice(0, accept.indexOf('pick.onDidChangeValue'));

  assert.doesNotMatch(body, /pick\.hide\(\)/u, 'the picker is hidden before it is known whether anything opened');
  assert.match(body, /pick\.busy = true/u, 'nothing says the picker is working while a conversation is opened');
  assert.match(body, /\.then\(settle\)/u, 'the outcome of choosing does not decide what happens to the list');
  // And `settle` is the one place that decides, so the catch cannot forget to.
  const settle = text.slice(text.indexOf('const settle ='), text.indexOf('pick.onDidAccept'));
  assert.match(settle, /if \(opened\)/u, 'settle does not distinguish a conversation that opened from one that did not');
  assert.match(settle, /pick\.hide\(\)/u, 'a conversation that opened leaves the list covering it');
  assert.match(settle, /chosen = false/u, 'a choice that opened nothing leaves the picker latched, so nothing else can be chosen');
  assert.match(settle, /draw\(\)/u, 'a row that has gone is left on the list after it was said to have been taken off');
  assert.match(settle, /if \(!onScreen\)/u, 'settle writes to a picker that may already have been dismissed and disposed');
});

test('choosing answers whether anything opened, rather than leaving the caller to guess', () => {
  // The three failing answers and the two opening ones are five returns, and the widget acts on the
  // boolean rather than re-deriving the outcome it has already decided.
  const text = widget();
  const choose = text.slice(text.indexOf('const choose ='), text.indexOf('pick.onDidTriggerButton'));

  assert.match(choose, /Promise<boolean>/u, 'choosing does not say whether it opened anything');
  assert.equal(choose.split('return true;').length - 1, 2, 'the two ways a tab appears — revealed, and reopened — do not both report it');
  assert.equal(choose.split('return false;').length - 1, 3, 'the three ways it can fail — gone, refused, held elsewhere — do not all report it');
});

test('one forget at a time, so a second press cannot blame another window for this one', () => {
  // The trash and Alt+Delete are advertised as pressable in a row. Two presses on ONE row before the
  // first finishes would have the second meet the lock the first is holding, and be told the
  // conversation "is being changed by another window" — which it is not; it is being changed by this
  // one. (gemini, the code round.)
  const text = widget();
  const body = text.slice(text.indexOf('const forget ='), text.indexOf('const forgetRow ='));

  assert.match(text, /let removing = false;/u, 'nothing stops two forgets of one conversation overlapping');
  assert.match(body, /if \(removing\)/u, 'the guard is not read before the store is asked');
  assert.match(body, /removing = false;/u, 'the guard is never released, so one forget disables the rest of the session');
  assert.ok(body.indexOf('if (removing)') < body.indexOf('deps.store.forget('),
    'the guard is checked after the store has already been asked, which is not a guard');
});

test('typing rebuilds the list only when what is on screen could be missing a match', () => {
  // The hundred-row cut is applied after the filter now, so a query must be able to reach past it —
  // but rebuilding on every keystroke would reassign a directory's worth of rows under somebody's
  // fingers. `mustRedraw` is the rule, and it is decided where a test can reach it.
  const text = widget();

  assert.match(text, /pick\.onDidChangeValue\(/u, 'what is typed never reaches the list, so the cut still hides older conversations');
  assert.match(text, /mustRedraw\(drawn, value, cut\)/u, 'the widget decides for itself when to rebuild, rather than asking the rule');
  const draw = text.slice(text.indexOf('const draw ='), text.indexOf('const choose ='));
  assert.match(draw, /drawn = query;/u, 'nothing records which query the list on screen was built for');
  assert.match(draw, /cut = rows\.some\(/u, 'nothing records whether the list on screen was cut');
});
