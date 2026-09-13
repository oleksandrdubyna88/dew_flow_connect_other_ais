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
  assert.match(command, /case 'pick':[\s\S]{0,400}switchConversations\(panels, picker, \{/u, 'an ambiguity does not reach the picker');
  assert.match(command, /case 'start':[\s\S]{0,400}startOnNew: true/u, 'nothing offers to start a conversation for a tab that has none');
  assert.match(command, /case 'everything':[\s\S]{0,200}switchConversations\(panels, picker\);/u, 'a terminal gets something other than the whole list');
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

test('binding is guarded three ways, each for a race the plan round named', () => {
  const command = source('chatGotoCommand.ts');
  const bind = command.slice(command.indexOf('async function bind('), command.indexOf('/** A tab a conversation may be bound to. */'));

  // The record can have gone or moved since the decision.
  assert.match(bind, /await store\.read\(answer\.meta\.id\)/u, 'the record is bound from the index rather than from disk');
  assert.match(bind, /bindable\(record, asked\.source, answer\.meta\.workspace, asked\.caseBlind\)/u,
    'the re-read record is not checked against the tab it is about to be bound to');
  // The tab can have gone, or have gained a conversation of its own.
  const tab = command.slice(command.indexOf('function bindableTab('));
  assert.match(tab, /!tabStillOpen\(key\)/u, 'a conversation can be bound to a tab that has been closed');
  assert.match(tab, /panels\.get\(key\) !== undefined/u, 'a conversation can be bound to a tab that already holds one');
  // And two presses cannot both get through.
  assert.match(command, /let running = false;/u, 'nothing stops a second press while the first is reading');
  assert.match(command, /if \(running\) \{/u, 'the latch is not read');
});

test('a conversation the picker or a reload opened without a tab is REBOUND, not merely revealed', () => {
  // The gap epic B recorded and named this story as the place to close: such a conversation lands
  // under a key of its own, so the tab it belongs to stayed unbound for ever and the picker and this
  // command disagreed about what that tab owns. (Two vendors, the plan round.)
  const command = source('chatGotoCommand.ts');
  const bind = command.slice(command.indexOf('async function bind('));

  assert.match(bind, /whereConversationSits\(panels, record\.id\)/u, 'nothing looks for the conversation under another key');
  assert.match(bind, /panels\.rekey\(where, tab\.key\)/u, 'an unbound conversation is revealed but never bound to its tab');
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
