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

test('a stop reaches the thread it names, and only while that thread is running', () => {
  // The guarantee the plan states as "a double press cannot stop the NEXT turn". The session refuses
  // a stop that names nothing, but the host must refuse one that names the WRONG thing: a bridge
  // message can be late, and by the time it lands the next turn may already be in flight.
  const text = read(join('src', 'chatCommand.ts'));

  assert.match(
    text,
    /onStop:[\s\S]{0,600}?panels\.entryOf\(id\)/,
    'a stop is routed by the page id, so it can reach a thread that is not the one that sent it',
  );
  assert.match(
    text,
    /thread\??\.running/,
    'a stop is applied without checking that the thread has a turn to stop',
  );
  assert.match(
    text,
    /turn !== thread\.turn/,
    'a stop naming a turn other than the running one is not refused',
  );
});

test('a stopped turn is written into the transcript before the conversation is carried', () => {
  // The finding that ordering is load-bearing. The question is appended BEFORE the turn is sent, so a
  // turn that ends without an answer leaves the transcript ending on a dangling question. Carrying
  // that into a fresh process hands the next model a question nobody answered and no sign that it was
  // abandoned — and the turn after it appends a second `you` on top of the first. (gemini, the plan
  // round, Blocking.)
  const text = read(join('src', 'chatCommand.ts'));

  assert.match(
    text,
    /result\.stopped[\s\S]{0,500}?thread\.messages = \[\.\.\.thread\.messages,/,
    'a stopped turn leaves the transcript ending on a question nobody answered',
  );
  assert.match(
    text,
    /result\.stopped === true[\s\S]{0,900}?thread\.carry = \[\.\.\.thread\.messages\]/,
    'the carry is taken BEFORE the stop is recorded, so it carries the dangling question',
  );
});

/**
 * The third thing no unit test can see: a read that goes AROUND the one accessor.
 *
 * <p>`panelProvider` has said since the per-side switch shipped that "a read that goes around it is
 * a setting that silently stays shared" — and its accessor was private, so three reads went around
 * it. Both files below import `vscode` and cannot be exercised here, which is exactly the shape the
 * `argvFor` guard above was written for: the fact is in the source, so the source is what is
 * checked.</p>
 *
 * <p>It matters most for `vendors`, because that row carries `executablePath`. On a machine with the
 * switch on, a shared read hands a WSL window the Windows npm shim.</p>
 */
const DIRECT_VENDORS = /\.get(?:<[^>]*>)?\(\s*'vendors'\s*\)/g;

test('the chat reads vendors only through the per-side reader, never straight off the shared configuration', () => {
  const text = read(join('src', 'chatCommand.ts'));

  assert.deepStrictEqual(
    [...text.matchAll(DIRECT_VENDORS)].map((hit) => hit[0]),
    [],
    'a direct read of vendors bypasses this side’s overlay — go through the per-side reader',
  );
  assert.match(
    text,
    /sideConfigReader/,
    'nothing in the chat builds a per-side reader, so every setting it reads is the shared one',
  );
});

test('the orphan sweep asks this side rather than giving up because it is not Windows', () => {
  // A vendor CLI orphaned by a force-kill is a signed-in process with nobody to stop it. The sweep
  // answered 'unknown' for every non-Windows host, and 'unknown' is the one outcome that does NOT
  // settle a row — so under WSL the record was kept and re-asked at every activation, for ever.
  const text = read(join('src', 'chatOrphans.ts'));

  assert.doesNotMatch(
    text,
    /process\.platform !== 'win32'/,
    'the sweep still refuses every host that is not Windows, so a WSL orphan is never ended',
  );
  assert.match(text, /endIfOursPosix/, 'nothing asks /proc, so there is no answer for a Linux child');
});

test('the server settings file is fed this side’s settings, not only the shared ones', () => {
  // The same bypass with a wider blast radius: this file is what coai-mcp reads, so a shared read
  // here runs the GATE's reviewers off another side's vendor list, not only the chat.
  const text = read(join('src', 'extension.ts'));

  assert.deepStrictEqual(
    [...text.matchAll(DIRECT_VENDORS)].map((hit) => hit[0]),
    [],
    'the server is handed the shared vendors, whatever this side has configured',
  );
  assert.match(text, /sideConfigReader/, 'nothing here builds a per-side reader');
});
