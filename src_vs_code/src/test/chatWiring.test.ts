import assert from 'node:assert/strict';
import { test } from 'node:test';
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * Two things about this feature that no unit test can see, because neither lives in a function.
 *
 * <p><b>The manifest half.</b> Every pure piece of the trigger can be green while the command is
 * unreachable: a typo in `contributes.commands`, a `when` clause naming a webview that does not
 * exist, a keybinding pointing at an id nothing registered. The gate raised exactly that — "a build
 * can pass `npm test` even when the manifest does not invoke the command" — and it was right that
 * nothing here was checking it. The live check stays in the plan's Definition of Done, because a
 * manifest that parses is still not a menu somebody can press; this test takes the half a machine
 * can answer.</p>
 *
 * <p><b>The injection half.</b> A reviewer read the plan and concluded that the selected passage is
 * interpolated into a PowerShell script, where `$(Get-Content secret.txt)` would run. It is not —
 * the passage travels back through the CLIPBOARD, as data, and the script is one constant. That is
 * true today and cheap to keep true: this test fails the moment anything but `COPY_SCRIPT` is handed
 * to `argvFor`.</p>
 */

/** The repository's `src_vs_code`. The suite runs from its root, which is what `npm test` does. */
const ROOT = process.cwd();

const read = (relative: string): string => readFileSync(join(ROOT, relative), 'utf8');

interface Contribution {
  readonly command?: string;
  readonly key?: string;
  readonly when?: string;
}

interface Manifest {
  readonly contributes: {
    readonly commands: readonly Contribution[];
    readonly keybindings: readonly Contribution[];
    readonly menus: Readonly<Record<string, readonly Contribution[]>>;
  };
}

const MANIFEST = JSON.parse(read('package.json')) as Manifest;
const COMMAND = 'coai.chatWithOtherAi';

/** The webview the Claude Code panel registers. Both doors are scoped to it and nothing else. */
const PANEL = 'claudeVSCodePanel';

test('the command the extension registers is the command the manifest declares', () => {
  const declared = MANIFEST.contributes.commands.map((row) => row.command);

  assert.ok(declared.includes(COMMAND), `${COMMAND} is not in contributes.commands`);
  assert.match(
    read('src/extension.ts'),
    new RegExp(`registerCommand\\('${COMMAND.replace('.', '\\.')}'`),
    'the manifest declares a command nothing registers — the palette entry would do nothing',
  );
});

test('the keybinding door exists, is scoped to the assistant panel, and names that command', () => {
  const bound = MANIFEST.contributes.keybindings.filter((row) => row.command === COMMAND);

  assert.strictEqual(bound.length, 1, 'the keybinding is missing or declared twice');
  assert.strictEqual(bound[0]!.key, 'ctrl+alt+a');
  // Unscoped, it would fire over an editor, where there is no selection to copy out of a webview.
  assert.match(bound[0]!.when ?? '', new RegExp(`activeWebviewPanelId == '${PANEL}'`));
});

test('the right-click door exists, and is scoped to the same panel', () => {
  const items = MANIFEST.contributes.menus['webview/context'] ?? [];
  const ours = items.filter((row) => row.command === COMMAND);

  assert.strictEqual(ours.length, 1, 'the context-menu item is missing — the measured path for a copy VS Code will not make for us');
  assert.match(ours[0]!.when ?? '', new RegExp(`webviewId == '${PANEL}'`));
});

test('the passage never reaches PowerShell: the script is a constant, and it is the only thing encoded', () => {
  const sources = readdirSync(join(ROOT, 'src'))
    .filter((name) => name.endsWith('.ts'))
    .map((name) => ({ name, text: read(join('src', name)) }));

  const callers = sources.flatMap(({ name, text }) =>
    // Calls, not the declaration — `export function argvFor(script: string)` is the door itself.
    [...text.matchAll(/(?<!function )argvFor\(([^)]*)\)/g)].map((hit) => ({ name, argument: hit[1]!.trim() })),
  );

  assert.ok(callers.length > 0, 'nothing calls argvFor — this guard is watching a door that moved');
  for (const call of callers) {
    assert.strictEqual(
      call.argument,
      'COPY_SCRIPT',
      `${call.name} builds a PowerShell script out of something else — a passage reaching it would be code, not data`,
    );
  }
});

test('a conversation that changes its model replaces its memory rules with it', () => {
  // A structural guard because the thing it guards has no seam: `switchNow` needs a live extension
  // host, and the defect it had was an OMISSION — three fields updated and a fourth left describing
  // the model that was just thrown away. `memoryOf` is the one rule; what this asserts is that BOTH
  // places that start a session take it from there, so adding a fifth field to it cannot be applied
  // in one place and forgotten in the other. (codex, the code round.)
  const text = read(join('src', 'chatCommand.ts'));

  // SPREAD, in both places, so the object is taken whole. Reading one field out of it would be the
  // same defect wearing a function call: the next field added to `memoryOf` would reach the new
  // conversation and not the switched one.
  assert.match(
    text,
    /\.\.\.memoryOf\(/,
    'a new conversation does not take its memory rules from memoryOf',
  );
  assert.match(
    text,
    /Object\.assign\(thread, memoryOf\(/,
    'a switch does not replace the memory rules with the new model\u2019s',
  );
});

test('a closed tab is forgotten, not merely disposed', () => {
  // The map outlived every tab that had ever been opened, and an answer arriving after a close then
  // posted state into a webview VS Code had already torn down. Forgetting the thread makes both
  // impossible at once, because every reader of it starts by looking it up.
  const text = read(join('src', 'chatCommand.ts'));

  assert.match(
    text,
    /onClosed:[\s\S]{0,900}?threads\.delete\(/,
    'closing a tab leaves its conversation in the registry forever',
  );
});
