import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { bodyOf, sourceOf } from './sourceReading';

/**
 * The terminal door, as the manifest declares it and as the command is written (todo/PLAN_the_chat_opens_from_a_terminal_E1.md, S3).
 *
 * <p>Two halves, and neither runs the door. The manifest half parses `package.json` and asks structural questions of it: it
 * is a data file, so nothing here reads page source. The source half reads `chatCommand.ts`, a host module whose function
 * bodies cannot be called without a window: it cuts out the functions that make up the terminal door and asserts what they
 * must never call. What neither can show is the keystroke itself, the button click and the palette's view of `terminalFocus`;
 * those are the owner's checks on the VSIX (R1, R3, R9), and `research/module_tests.md` says so.</p>
 */

const ROOT = process.cwd();
const read = (relative: string): string => readFileSync(join(ROOT, relative), 'utf8');

interface Row {
  readonly command?: string;
  readonly key?: string;
  readonly when?: string;
  readonly title?: string;
  readonly category?: string;
  readonly icon?: string;
  readonly group?: string;
}

interface Manifest {
  readonly contributes: {
    readonly commands: readonly Row[];
    readonly keybindings: readonly Row[];
    readonly menus: Readonly<Record<string, readonly Row[]>>;
    readonly configurationDefaults?: Readonly<Record<string, unknown>>;
  };
}

const MANIFEST = JSON.parse(read('package.json')) as Manifest;
const DOOR = 'coai.askFromTerminal';
const SKIP_LIST = 'terminal.integrated.commandsToSkipShell';
const CHORD = 'ctrl+alt+a';

/** A `when` clause that needs a terminal to have focus: `terminalFocus` not negated. `terminalFocusInAnyPane` is another context key. */
const requiresTerminalFocus = (when: string | undefined): boolean => /(^|[^!\w.])terminalFocus\b/u.test(when ?? '');

test('the terminal door is declared with its title and its icon', () => {
  const rows = MANIFEST.contributes.commands.filter((row) => row.command === DOOR);

  assert.equal(rows.length, 1, `${DOOR} is declared ${rows.length} times in contributes.commands, not once`);
  assert.equal(rows[0]?.title, 'Ask other AI', 'the title-bar button has the wrong title');
  assert.equal(rows[0]?.category, 'ConnectOtherAIs');
  assert.equal(rows[0]?.icon, '$(comment-discussion)', 'the title-bar button has no icon');
});

test('the button sits in the terminal panel’s title bar', () => {
  const rows = (MANIFEST.contributes.menus['view/title'] ?? []).filter((row) => row.command === DOOR);

  assert.equal(rows.length, 1, `${DOOR} has ${rows.length} view/title rows, not one`);
  assert.equal(rows[0]?.when, 'view == terminal', 'the view/title row is not scoped to the terminal view');
  assert.equal(rows[0]?.group, 'navigation@90', 'the view/title row is not in navigation@90');
});

test('the chord is Ctrl+Alt+A under terminal focus', () => {
  const rows = MANIFEST.contributes.keybindings.filter((row) => row.command === DOOR);

  assert.equal(rows.length, 1, 'the chord does nothing in a terminal: the door has no keybinding');
  assert.equal(rows[0]?.key, CHORD);
  assert.equal(rows[0]?.when, 'terminalFocus');
});

test('every command bound under terminal focus is in the skip-list default, so its chord is not sent to the shell', () => {
  const skipped = MANIFEST.contributes.configurationDefaults?.[SKIP_LIST];
  const bound = MANIFEST.contributes.keybindings.filter((row) => requiresTerminalFocus(row.when));
  const commands = new Set(bound.map((row) => row.command ?? ''));

  // The scan's own proof: it still finds the door, so a reformatted `when` cannot make it match nothing and pass for ever.
  assert.ok(commands.has(DOOR), 'the derivation no longer finds the terminal door among the terminalFocus bindings');
  assert.ok(Array.isArray(skipped), `${SKIP_LIST} has no default in contributes.configurationDefaults`);
  for (const command of commands) {
    assert.ok(
      (skipped as readonly unknown[]).includes(command),
      `${command} is bound in a terminal but not in the skip list — the chord goes to the shell`,
    );
  }
});

test('no older chord can win a chord typed into a terminal', () => {
  const older = MANIFEST.contributes.keybindings.filter((row) => row.key === CHORD && row.command !== DOOR);
  const goTo = MANIFEST.contributes.keybindings.filter((row) => row.command === 'coai.goToConversation');

  // The scan's own proof: both chat chords still exist, so an emptied filter cannot pass for ever.
  assert.ok(older.filter((row) => row.command === 'coai.chatWithOtherAi').length >= 2, 'the derivation no longer finds the two chat chords');
  assert.ok(goTo.length >= 1, 'the derivation no longer finds the go-to chord');
  for (const row of [...older, ...goTo]) {
    assert.ok(
      (row.when ?? '').includes('!terminalFocus'),
      `${row.command} bound to ${row.key} when "${row.when}" can still fire in a terminal, where the terminal door must win`,
    );
  }
});

test('the palette does not offer the file door while a terminal has focus', () => {
  const rows = (MANIFEST.contributes.menus['commandPalette'] ?? []).filter((row) => row.command === 'coai.chatWithOtherAi');

  assert.equal(rows.length, 1, 'coai.chatWithOtherAi has no commandPalette row, so the palette offers the file door in a terminal');
  assert.equal(rows[0]?.when, '!terminalFocus');
});

/** The source of the functions that make up the terminal door: `askFromTerminal` and every `terminalDoor…` helper. */
function terminalDoorSource(): string {
  const text = sourceOf('chatCommand.ts');
  const helpers = [...text.matchAll(/^function (terminalDoor\w*)\(/gmu)].map((one) => bodyOf(text, `function ${one[1]}(`));

  return [bodyOf(text, 'export async function askFromTerminal('), ...helpers].join('\n');
}

test('the terminal door never reads the tabs', () => {
  const door = terminalDoorSource();
  const chat = bodyOf(sourceOf('chatCommand.ts'), 'export async function chatWithOtherAi(');

  // The scans' own proof: the door's code is found and is the terminal's, and the file door still reads the tabs.
  assert.ok(door.includes('fromTheTerminal('), 'the derivation no longer finds the terminal door\u2019s code');
  assert.ok(chat.includes('matchedSource('), 'the file door no longer calls matchedSource, so this scan guards a function that moved');
  for (const call of ['matchedSource(', 'snapshots(', 'sourceSession(']) {
    assert.ok(!door.includes(call), `the terminal door calls ${call}…) — the file-text defect can happen on this door again`);
  }
});

test('a terminal whose name cannot name a conversation is refused BEFORE the clipboard is read', () => {
  // A name that is not an identity (a control character, a line separator, over 500 units, empty) builds the source `none`: the chat
  // would lose its road back and the steal protection, silently. So the door says so and stops. The guard has to come first — the
  // clipboard is read only for a terminal that can be filed — and it has to be the store's own rule, not a second copy of it.
  const text = sourceOf('chatCommand.ts');
  const command = bodyOf(text, 'export async function askFromTerminal(');
  const guard = bodyOf(text, 'function terminalDoorName(');

  assert.ok(guard.includes('isTerminalName('), 'the terminal door never asks whether the terminal\u2019s name is an identity, so such a terminal opens a chat with no source');
  assert.ok(guard.includes("code: 'terminal-name-unusable'") || terminalDoorSource().includes("code: 'terminal-name-unusable'"),
    'the refusal of an unusable name has no notice of its own');
  assert.ok(command.includes('terminalDoorName('), 'the command does not ask the guard');
  // A title can change while the clipboard is read: the name is read ONCE, by the guard, and the command works with that value —
  // a second read after the clipboard was read could judge one name and file the conversation under another.
  assert.ok(!command.includes('terminal.name'), 'the command reads the terminal\u2019s name itself, so it can differ from the one the guard judged');
  assert.ok(command.indexOf('terminalDoorName(') < command.indexOf('fromTheTerminal('), 'the name is checked after the clipboard was read');
});
