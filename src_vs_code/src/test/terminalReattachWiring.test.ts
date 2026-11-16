import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { bodyOf, sourceFiles, sourceOf } from './sourceReading';

/**
 * The re-attach of a terminal's conversation, as it is wired (todo/PLAN_the_chat_opens_from_a_terminal_E3.md, S1).
 *
 * <p>Host code, where no seam reaches: `terminalHost.ts`, `extension.ts`, `chatRestorePanel.ts` and `chatCommand.ts` import
 * `vscode`, so their function bodies cannot be called here. These tests read them. The decisions themselves are
 * `terminalReattach.ts`'s and are tested by RUNNING them (`terminalReattach.test.ts`); what no unit test shows is the real
 * editor, which is `terminalReattachScenario.ts` in CI and the owner's R8.</p>
 */

/** The one place the window's terminals become `LiveTerminal`s: the `exited` flag is read from the process's `exitStatus`. */
const EXIT_MAP = /exitStatus\s*!==\s*undefined/u;

test('the window’s terminals are read as LiveTerminal in ONE place', () => {
  const holders = sourceFiles().filter((file) => EXIT_MAP.test(sourceOf(file)));

  // The scan's own proof: it still finds the one map, so a reformatted line cannot make it match nothing and pass for ever.
  assert.deepEqual(holders, ['terminalHost.ts'], `the exitStatus map of LiveTerminal is in ${holders.join(', ')}, not only in terminalHost.ts`);
  for (const file of ['chatReturnCommand.ts', 'remoteSessionHost.ts']) {
    assert.ok(sourceOf(file).includes('liveTerminals('), `${file} does not read the window's terminals through liveTerminals(`);
  }
});

/** Where `needle` first appears in `text`, with the scan's own proof: it is there at all, so a renamed anchor cannot make an order check pass for ever. */
function at(text: string, needle: string, where: string): number {
  const found = text.indexOf(needle);
  assert.ok(found >= 0, `${where} no longer holds ${needle} — the scan guards something that moved`);

  return found;
}

test('S1-T11: a terminal that opens re-attaches the conversation of its name', () => {
  assert.equal([...sourceOf('extension.ts').matchAll(/bindReattach\(context, chatPanels\)/gu)].length, 1, 'extension.ts does not bind the re-attach trigger exactly once');
  const bind = bodyOf(sourceOf('terminalHost.ts'), 'export function bindReattach(');

  assert.ok(bind.includes('onDidOpenTerminal('), 'bindReattach does not listen for terminals that open');
  assert.ok(bind.includes('reattachByName(panels, opened.name)'), 'bindReattach does not re-attach by the name of the terminal that opened');
});

test('S1-T11: a restored terminal conversation re-attaches after the serializer restores it', () => {
  const text = sourceOf('chatRestorePanel.ts');
  const restore = bodyOf(text, 'export async function restoreChatTab(');
  const helper = bodyOf(text, 'function reattachRestored(');

  assert.ok(at(restore, 'reattachRestored(', 'restoreChatTab') > at(restore, 'restoreConversation(', 'restoreChatTab'), 'the restore does not re-attach AFTER restoreConversation(');
  assert.ok(helper.includes("source.kind === 'terminal'"), 'the restore trigger does not look at the record’s source kind');
  assert.ok(helper.includes('reattachByName(panels, source.name)'), 'the restore trigger does not re-attach by the record’s terminal name');
});

/** The door’s helper: the one re-attach, match and confirmation that the door, take and add share. */
const helperOf = (): string => bodyOf(sourceOf('chatCommand.ts'), 'function terminalDoorMatch(');

test('S1-T11: the door, take and add re-attach right before the match, with no await between, and confirm a use', () => {
  const helper = helperOf();
  const reattach = at(helper, 'reattachByName(panels, door.name)', 'terminalDoorMatch');
  const match = at(helper, 'const match = terminalMatch(', 'terminalDoorMatch');
  const confirm = at(helper, 'confirmReattach(panels, door.terminal)', 'terminalDoorMatch');

  assert.ok(reattach < match, 're-attach does not run BEFORE the match');
  assert.ok(match < confirm, 'a use is not confirmed AFTER the match');
  assert.ok(!helper.includes('await'), 'an await sits between the re-attach and the match');
  assert.ok(helper.includes("match.kind === 'existing'"), 'the confirmation is not tied to the match answering existing');

  const text = sourceOf('chatCommand.ts');
  const door = bodyOf(text, 'export async function askFromTerminal(');
  const take = bodyOf(text, 'export async function takeFromTerminal(');

  assert.ok(at(door, 'terminalDoorMatch(panels, door)', 'askFromTerminal') > at(door, 'await fromTheTerminal(', 'askFromTerminal'), 'the door re-attaches before the clipboard is read');
  assert.ok(at(take, 'terminalDoorMatch(panels, door)', 'takeFromTerminal') > at(take, 'await readRemoteSession(', 'takeFromTerminal'), 'take re-attaches before the remote read');
  for (const [name, body] of [['askFromTerminal', door], ['takeFromTerminal', take]] as const) {
    const call = at(body, 'const match = terminalDoorMatch(', name);
    const delivered = at(body, 'await deliverPassage(', name);

    assert.ok(call < delivered, `${name} matches after it delivers`);
    assert.ok(!body.slice(call, delivered).includes('await'), `an await sits between the match and the delivery in ${name}`);
    assert.ok(!body.includes('terminalMatch('), `${name} matches by itself, without the re-attach`);
  }
});
