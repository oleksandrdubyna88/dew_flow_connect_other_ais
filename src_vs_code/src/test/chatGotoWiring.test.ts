import assert from 'node:assert/strict';
import { test } from 'node:test';
import * as fs from 'node:fs';
import * as path from 'node:path';

/**
 * That *go to* is WIRED, and that each of its six answers does the right thing — the half no unit
 * test here can reach.
 *
 * <p>`chatGoto.test.ts` drives the decision over its whole matrix without a host. What it cannot see
 * is whether the command carries any of those answers out: a build where every arm revealed the
 * wrong thing, bound a cross-root record, or started a duplicate when the re-read refused would pass
 * every one of those tests. This repository has no extension-host harness — `research/module_tests.md`
 * records that as its largest gap — so what can be pinned is which function each arm calls, and that
 * the dispatch is exhaustive. (codex, the plan round, asked for exactly this.)</p>
 */

/** Where one function begins and the next one does, so an assertion cannot match a neighbour. */
const between = (text: string, from: string, to: string): string =>
  text.slice(text.indexOf(from), text.indexOf(to));

const source = (file: string): string =>
  fs.readFileSync(path.join(__dirname, '..', '..', 'src', file), 'utf8');

const MANIFEST = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8')) as {
  contributes: {
    commands: { command: string; title: string }[];
    keybindings: { command: string; key: string; mac?: string; when?: string }[];
    menus: Record<string, { command: string; when?: string; group?: string }[]>;
  };
};

const GOTO = 'coai.goToConversation';

test('the command exists, is bound, and is offered in both menus a person already uses for chat', () => {
  const named = MANIFEST.contributes.commands.find((row) => row.command === GOTO);
  assert.ok(named !== undefined, 'the command is not declared, so the palette does not know it');
  assert.match(named.title, /go to conversation/iu);

  const bound = MANIFEST.contributes.keybindings.filter((row) => row.command === GOTO);
  assert.equal(bound.length, 1, 'go to has no chord, or more than one');
  assert.equal(bound[0]?.key, 'ctrl+alt+g');
  assert.equal(bound[0]?.mac, 'cmd+alt+g', 'the chord is unreachable on macOS, where ctrl is not the modifier');
  // SCOPED, unlike the switch chord — and that difference is the point. *Go to* acts on the active
  // tab, so it has something to be scoped to; the list does not, which is why its chord is global.
  assert.match(bound[0]?.when ?? '', /activeWebviewPanelId|editorTextFocus/u,
    'the chord fires from anywhere, although this command acts on the tab in front of you');

  const editor = (MANIFEST.contributes.menus['editor/context'] ?? []).find((row) => row.command === GOTO);
  const webview = (MANIFEST.contributes.menus['webview/context'] ?? []).find((row) => row.command === GOTO);
  assert.ok(editor !== undefined, 'the item is missing from the editor menu');
  assert.ok(webview !== undefined, 'the item is missing from Claude’s panel menu');
  assert.match(editor.group ?? '', /^coai@/u, 'the item left the group the other chat items share');
  // NOT the tab-header menu: that one hands over the uri of the tab that was CLICKED, which need not
  // be the active one, and this command reads the active tab. (gemini, the plan round.)
  const titles = MANIFEST.contributes.menus['editor/title/context'] ?? [];
  assert.equal(titles.some((row) => row.command === GOTO), false,
    'the item is on the tab-header menu, where the clicked tab and the active tab are not the same tab');
});

test('the command is registered, records its door BEFORE the work, and catches its own edge', () => {
  const wiring = source('extension.ts');
  const at = wiring.indexOf(`registerCommand('${GOTO}'`);
  assert.notEqual(at, -1, 'nothing registers the command, so the manifest names one that does not exist');
  const body = wiring.slice(at, at + 900);

  assert.match(body, /noteChatDoor\('goto'\)/u, 'the opening is not recorded, so the spending page cannot count it');
  assert.ok(body.indexOf("noteChatDoor('goto')") < body.indexOf('goToConversation('),
    'the door is recorded after the work that can refuse it, which is not what the other five doors do');
  assert.match(body, /\.catch\(\(reason: unknown\)/u, 'an awaited command with no catch is an unhandled rejection');
});

test('each of the six answers is carried out, and a seventh would be a compile error', () => {
  const command = source('chatGotoCommand.ts');

  // reveal: the cheapest answer, and no read.
  assert.match(command, /case 'reveal':[\s\S]{0,200}revealConversation\(panels, answer\.id\)/u, 'what is open is not revealed');
  // reopen: through the bind, which re-reads.
  assert.match(command, /case 'reopen':[\s\S]{0,200}await bind\(/u, 'a saved conversation is bound without re-reading it');
  // pick / start / everything: all three are the picker, and only one of them preselects the offer.
  assert.match(command, /case 'pick':[\s\S]{0,400}show\(\{/u, 'an ambiguity does not reach the picker');
  assert.match(command, /case 'start':[\s\S]{0,400}startOnNew: true/u, 'nothing offers to start a conversation for a tab that has none');
  assert.match(command, /case 'everything':[\s\S]{0,300}show\(\);/u, 'a terminal gets something other than the whole list');
  assert.match(command, /case 'building':[\s\S]{0,400}stillListing\(/u, 'an unbuilt list says nothing');
  // And the dispatch is closed.
  assert.match(command, /const unhandled: never = answer;/u, 'a seventh answer would be a press that does nothing');
});

test('START creates nothing — it opens the picker with the offer, and waits', () => {
  // Pressing this chord by accident on a tab with no conversation must not resolve a CLI and launch
  // a vendor process for something nobody has typed into. (Two vendors, the plan round — and it is
  // what the plan itself said before my brief for the story drifted from it.)
  const command = source('chatGotoCommand.ts');
  const start = command.slice(command.indexOf("case 'start':"), command.indexOf("case 'everything':"));

  assert.doesNotMatch(start, /newConversation\(/u, 'go to starts a conversation nobody asked for');
  assert.match(start, /rows: \[\]/u, 'the offer is buried among rows');
  assert.match(start, /startOnNew: true/u, 'the offer is not under the cursor, so it is not one keystroke away');
});

test('binding is decided AT THE PRESS, and guarded three ways', () => {
  // Three reviewers found the same defect in the first draft: the target tab was worked out before a
  // picker somebody may browse for a minute, so a row chosen at the end could be bound to a tab that
  // had closed or since gained a conversation. Nothing is decided in advance now.
  const command = source('chatGotoCommand.ts');
  const decide = command.slice(command.indexOf('function bindingTo('));

  // It is a FUNCTION handed forward, called with the record the store has just answered with — and
  // in EVERY arm that binds. One arm holding a precomputed handle is the whole defect back again, so
  // this counts them rather than asking whether the shape appears anywhere.
  assert.equal((command.match(/bindTo: bindingTo\(panels, asked, key\)/gu) ?? []).length, 2,
    'an arm that binds is handed a target worked out in advance rather than a question asked at the press');
  assert.match(decide, /return \(record\) =>/u, 'the target is computed once rather than at the press');
  // The record must still be this tab's — source AND the TAB'S root, never the record's own.
  assert.match(decide, /bindable\(record, asked\.source, rootOfTab\(asked\), asked\.caseBlind\)/u,
    'the record is not re-checked against the tab it is about to be bound to');
  // The tab must still be there, and still hold nothing.
  assert.match(decide, /!tabStillOpen\(key\)/u, 'a conversation can be bound to a tab that has been closed');
  assert.match(decide, /panels\.get\(key\) !== undefined/u, 'a conversation can be bound to a tab that already holds one');
  // The record is read from DISK at the press, never from the index the decision was made on.
  const bind = command.slice(command.indexOf('async function bind('), command.indexOf('function bindingTo('));
  assert.match(bind, /await store\.read\(answer\.meta\.id\)/u, 'the record is bound from the index rather than from disk');
  // And two presses cannot both get through — with the latch set INSIDE the try, so a synchronous
  // throw cannot wedge the command for the life of the window. (gemini, the code round.)
  assert.match(command, /let running = false;/u, 'nothing stops a second press while the first is reading');
  assert.match(command, /try \{\s*\n\s*running = true;/u, 'the latch is set outside the try, so a throw leaves the command dead');
});

test('the record is re-checked against THE TAB’S root, never against its own', () => {
  // The workspace half of the guard was a tautology: `bindable(record, …, record.workspace, …)`
  // compares a record's root against itself and passes for every record in the store, so a
  // conversation another window re-filed into a different project would bind to this tab anyway —
  // the cross-root rule undone at the last step, which is the one place it has to hold. The tab's
  // own root is a value `chatGoto.ts` computes, and both halves now ask the same function for it.
  // (gemini, the second code round.)
  const command = source('chatGotoCommand.ts');
  const decide = command.slice(command.indexOf('function bindingTo('));

  assert.match(decide, /bindable\(record, asked\.source, rootOfTab\(asked\), asked\.caseBlind\)/u,
    'the record’s workspace is compared against something other than the tab’s own root');
  assert.doesNotMatch(decide, /record\.workspace/u,
    'the record’s own workspace is still being used as the thing to compare it against');
});

test('the offer to start one can be CHOSEN, and is not shown where it cannot be', () => {
  // `start` opens the picker with *New conversation for <tab>* under the cursor and the row itself
  // says "Nothing is created until you choose this" — and choosing it did nothing at all. The
  // accept handler is total by kind and simply returned. (gemini, the second code round; the
  // mechanism it named was wrong — the handler DOES check the kind — but the row was inert.)
  const picker = source('conversationPickerCommand.ts');
  const command = source('chatGotoCommand.ts');

  // The WHOLE guard, so an arm disabled in place is as red as an arm deleted.
  assert.match(picker, /if \(row\.kind === 'new' && narrowed\?\.onNew !== undefined\) \{/u,
    'the accept handler has no arm for the offer row');
  assert.match(picker, /narrowed\.onNew\(\)/u, 'choosing the offer calls nothing');
  // A row nothing can complete is never drawn: the offer appears only when a caller said what
  // choosing it does.
  assert.match(picker, /narrowed\?\.onNew === undefined \? \{\} : \{ offerNew: narrowed\.offer \}/u,
    'the offer is drawn even when no caller can complete it');
  // And the completion is the ordinary door, on the tab the row names — refused when that tab is
  // no longer the one in front, because starting a conversation for the wrong tab is the guess this
  // whole answer exists to avoid.
  assert.match(command, /onNew: startingFor\(panels, extensionUri, asked, key\)/u, 'the picker is given no way to start one');
  const starting = between(command, 'function startingFor(', 'function bindingTo(');
  assert.match(starting, /!activeTabIs\(asked\.tab\.label\)/u,
    'a conversation can be started for whatever tab happens to be in front when the row is pressed');
  assert.match(starting, /chatWithOtherAi\(panels, extensionUri, \[\], false\)/u, 'the offer does not open a conversation');
});

test('a conversation already live is REBOUND from the picker too, not only from the reopen arm', () => {
  // The reopen arm moves a conversation the picker or a reload opened without a tab onto the tab it
  // belongs to. The picker path revealed it and left the tab unbound for ever, so the next press
  // asked the same question again. Both go through one helper now. (Two vendors, the second code
  // round — their claim that a SECOND panel was built is refuted below.)
  const picker = source('conversationPickerCommand.ts');
  const command = source('chatGotoCommand.ts');

  assert.match(picker, /whereConversationSits\(panels, row\.id\)/u, 'the picker cannot tell where a live conversation sits');
  assert.match(picker, /revealBound\(panels, live, narrowed\?\.bindTo === undefined \? undefined : await boundFor\(row\.id, narrowed\.bindTo\)\)/u,
    'a live conversation is revealed without being bound');
  assert.match(command, /revealBound\(panels, where, tab\?\.key\)/u, 'the reopen arm keeps its own copy of the rekey rule');
  // ONE panel per saved conversation was never at risk, and that is why this is a rebind rather than
  // a fix: the registry is walked by the store id BEFORE anything is restored, so a conversation
  // that went live while the picker was open is found and revealed rather than built again.
  const chooses = picker.slice(picker.indexOf('const choose = async ('));
  assert.ok(
    chooses.indexOf('whereConversationSits') < chooses.indexOf('deps.store.read'),
    'a conversation is read back off disk before the registry is asked whether this window already holds it',
  );
});

test('the offer is refused by NAME, never by a captured tab object', () => {
  // The bug the operator found in 0.40.0: this compared a `vscode.Tab` captured when the chord was
  // pressed against the one in front when the row was chosen, and VS Code hands out a NEW Tab object
  // whenever a tab changes — a Claude Code tab renames itself as the assistant refines the session
  // title, which is the behaviour `rekeysByLabel` already exists to work around. So identity was
  // false for the tab still sitting in front, and the offer refused EVERY press.
  const command = source('chatGotoCommand.ts');
  const starting = between(command, 'function startingFor(', 'function bindingTo(');

  assert.match(starting, /!activeTabIs\(asked\.tab\.label\)/u, 'the refusal is not decided by the tab’s name');
  // THE CONDITION, not the prose around it: the comment above it names the call it replaced, and an
  // assertion that reads comments is one a comment can satisfy.
  assert.doesNotMatch(starting, /if \([^)]*(?:tabStillOpen|activeTabIs)\(key\)/u,
    'the refusal still compares a captured tab object, which goes stale the moment the tab is renamed');
  // And the host side asks the PURE predicate, which is what makes this bug class testable at all:
  // the old shape read correctly and was wrong at runtime, and no source assertion could see that.
  const host = source('chatCommand.ts');
  assert.match(host, /export function activeTabIs\(label: string\): boolean \{/u, 'activeTabIs no longer takes a label');
  assert.match(host, /return frontIs\(label, active\?\.label \?\? '', all\.filter\(\(one\) => one\.label === label\)\.length\);/u,
    'the host decides this itself instead of asking the predicate the tests can reach');
  // And it counts the tabs of that name, because a name two tabs share identifies neither.
  assert.match(source('chatGoto.ts'), /export const frontIs = \(label: string, front: string, namesakes: number\): boolean =>/u,
    'there is no pure predicate for "is the tab in front the one this name belongs to"');
});

test('a record that moved says so AND opens the list, rather than stopping', () => {
  // Somebody who asked to go to a conversation and is told it moved needs somewhere to go. My own
  // plan said this and my first implementation quietly dropped it. (Two vendors, the code round.)
  const command = source('chatGotoCommand.ts');
  const bind = command.slice(command.indexOf('async function bind('), command.indexOf('function bindingTo('));
  const missing = bind.slice(bind.indexOf('if (record === undefined)'));

  assert.match(missing.slice(0, 400), /showWarningMessage\(/u, 'a conversation that moved says nothing');
  assert.match(missing.slice(0, 400), /show\(\);/u, 'a conversation that moved leaves the person with nowhere to go');
});

test('a walk of the session directory that FAILED is not read as “no sessions”', () => {
  // "No session is called that" leads to offering a new conversation — so an unreadable directory
  // would have produced a duplicate of a conversation that already existed. (Two vendors.)
  const command = source('chatCommand.ts');
  const walk = command.slice(command.indexOf('async function sessionsNamed('), command.indexOf('async function sessionsNamed(') + 1_400);

  assert.match(walk, /unsure: true/u, 'a failed walk is indistinguishable from an empty one');
  assert.match(walk, /console\.warn\(/u, 'a failed walk is swallowed without a word');
  // And not knowing is a reason to ASK, never to offer to start a second conversation.
  assert.match(command, /walked\.unsure \|\|/u, 'a walk that could not be done still leads to offering a new conversation');
});

test('the session walk shows progress, because it reads a directory and can take seconds', () => {
  const command = source('chatCommand.ts');

  assert.match(command, /withProgress\(\s*\n?\s*\{ location: vscode\.ProgressLocation\.Window, title: 'Finding this conversation…' \}/u,
    'a person who presses the chord sees nothing at all while the sessions are read');
});

test('a conversation the picker or a reload opened without a tab is REBOUND, not merely revealed', () => {
  // The gap epic B recorded and named this story as the place to close: such a conversation lands
  // under a key of its own, so the tab it belongs to stayed unbound for ever and the picker and this
  // command disagreed about what that tab owns. (Two vendors, the plan round.)
  const command = source('chatGotoCommand.ts');
  const bind = command.slice(command.indexOf('async function bind('));

  assert.match(bind, /whereConversationSits\(panels, record\.id\)/u, 'nothing looks for the conversation under another key');
  // Through the shared helper, because the picker's own accept reaches a live conversation too and
  // the two were one rule kept in two places — with only one of them keeping it.
  assert.match(bind, /revealBound\(panels, where, tab\?\.key\)/u, 'an unbound conversation is revealed but never bound to its tab');
  assert.match(source('chatCommand.ts'), /export function revealBound[\s\S]{0,400}panels\.rekey\(where, onto\)/u,
    'the helper both callers go through does not actually move the registration');
});

test('restoring binds only when a caller names a tab, and the reload serializer never does', () => {
  const command = source('chatCommand.ts');
  const restore = command.slice(command.indexOf('export function restoreConversation'));

  // Checked BEFORE anything is built: `panels.open` would reveal what is there without calling the
  // factory, but the panel would already exist — a webview for a tab that turned out to have one.
  assert.match(restore.slice(0, 2_000), /const already = bindTo === undefined \? undefined : panels\.get\(bindTo\.key\)/u,
    'a panel is built before anyone asks whether the tab already has one');
  assert.match(restore, /panels\.open\(bindTo\?\.key \?\? \{\}, bindTo\?\.label \?\? saved\.title/u,
    'a restored conversation cannot be bound to a tab, or is always bound to one');

  // The serializer passes none, so a reload keeps giving every conversation its own key.
  const wiring = source('extension.ts');
  const serializer = wiring.slice(wiring.indexOf('registerWebviewPanelSerializer'));
  assert.doesNotMatch(serializer.slice(0, 1_500), /restoreConversation\([^)]*bindTo/u,
    'the reload serializer binds restored conversations to tabs, which it has no way to identify');
});
