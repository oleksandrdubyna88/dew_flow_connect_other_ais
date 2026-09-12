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

test('the chord has one binding per door, and neither of them is unscoped', () => {
  // TWO doors since the chat learned to open from a file. Each is scoped to the surface it can
  // actually read: the panel's binding to the panel, the editor's to a focused editor. What must
  // never appear is a binding with no `when` at all — it would fire over a settings page, an image
  // preview and the Output pane, where there is nothing to send.
  const bound = MANIFEST.contributes.keybindings.filter((row) => row.command === COMMAND);

  assert.strictEqual(bound.length, 2, 'a door is missing, or one is declared twice');
  for (const row of bound) {
    assert.strictEqual(row.key, 'ctrl+alt+a');
    assert.ok((row.when ?? '').length > 0, 'a binding with no scope fires wherever the chord is pressed');
  }
  assert.ok(
    bound.some((row) => new RegExp(`activeWebviewPanelId == '${PANEL}'`).test(row.when ?? '')),
    'the assistant panel lost the chord it had',
  );
  assert.ok(
    bound.some((row) => (row.when ?? '').includes('editorTextFocus')),
    'the chord does nothing in an ordinary editor',
  );
});

test('the right-click door offers TWO items, each naming what it will do', () => {
  // One item whose behaviour depends on `coai.chatAutoSend` is an item nobody can predict from its
  // own label — which is what the operator was working around by asking for two. `default` sends at
  // once on the main model; `choose` puts the turn in the composer and waits.
  const items = MANIFEST.contributes.menus['webview/context'] ?? [];
  const now = items.filter((row) => row.command === 'coai.chatNow');
  const choose = items.filter((row) => row.command === 'coai.chatChoose');

  assert.strictEqual(now.length, 1, 'the send-at-once item is missing from the panel menu');
  assert.strictEqual(choose.length, 1, 'the put-it-in-the-composer item is missing from the panel menu');
  for (const row of [...now, ...choose]) {
    assert.match(row.when ?? '', new RegExp(`webviewId == '${PANEL}'`), 'an item escaped the panel it belongs to');
  }
  // And the setting-dependent command keeps the chord and the palette, where its name is not a
  // promise about what happens next.
  assert.strictEqual(items.filter((row) => row.command === COMMAND).length, 0,
    'the menu still carries the item whose behaviour is a setting');
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
    /result\.stopped === true[\s\S]{0,900}?thread\.carry = carriedFrom\(thread\.messages,/,
    'the carry is taken BEFORE the stop is recorded, so it carries the dangling question',
  );
});

/**
 * The third thing no unit test can see: a read that goes AROUND the one accessor.
 *
 * <p><b>THE GUARANTEE CHANGED, and into a stronger one.</b> This used to say that the chat must read
 * `vendors` through the per-side reader rather than straight off the shared configuration — because
 * that row carries `executablePath`, and a shared read hands a WSL window the Windows npm shim. The
 * chat does not read `vendors` AT ALL now: a preset carries its own vendor, model and CLI path, so
 * there is no reviewer row to read from the wrong side of the machine.</p>
 *
 * <p>Asked for by the operator, five times, in the end as plainly as it can be put: *"это полностью
 * независимый функционал этот чат… чат никак не должен трогать ревьюы"*. A test is the only thing
 * that keeps it true — the coupling was convenient, and it will be convenient again.</p>
 */
/** A read of the reviewer rows that goes around this side's overlay. */
const DIRECT_VENDORS = /\.get(?:<[^>]*>)?\(\s*'vendors'\s*\)/g;

const ANY_VENDORS = /\.get(?:<[^>]*>)?\(\s*'vendors'\s*\)|vendorsFrom\(/g;

test('the chat reads no reviewer row, from either side of the machine', () => {
  for (const file of ['chatCommand.ts', 'chatPresetsPanel.ts', 'chatPresets.ts', 'chatPanel.ts']) {
    const text = read(join('src', file));

    assert.deepStrictEqual(
      [...text.matchAll(ANY_VENDORS)].map((hit) => hit[0]),
      [],
      `${file} reads the reviewer rows — the chat is independent of the review gate, and stays so`,
    );
  }
});

test('the side is bound before any command can be invoked', () => {
  // `sideRead` falls back to the shared configuration while nothing is bound, which is exactly the
  // behaviour this branch removed. Four reviewers named that window in one round. It is closed by
  // ORDERING — the binding is the first statement of `activate` — and this is what keeps it closed.
  const text = read(join('src', 'extension.ts'));
  const bound = text.indexOf('chatReadsThisSide(context)');
  const firstCommand = text.indexOf('registerCommand(');

  assert.ok(bound > 0, 'nothing binds this side, so every chat reads the shared settings');
  assert.ok(
    firstCommand > bound,
    'a command is registered before the side is bound — an invocation in that window reads shared settings',
  );
});

test('the per-side reader is built in exactly one place', () => {
  // `sideConfigReader` takes any Side at all. One construction, which derives the side from the
  // context, is what makes "this side" an invariant instead of a habit each caller has to keep.
  const callers = readdirSync(join(ROOT, 'src'))
    .filter((name) => name.endsWith('.ts'))
    .filter((name) => /(?<!function )sideConfigReader\(/.test(read(join('src', name))));

  assert.deepStrictEqual(
    callers,
    ['sideConfig.ts'],
    'the per-side reader is assembled somewhere other than sideConfig.ts, so a caller can pass its own Side',
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
  assert.match(text, /readerFor\(/, 'nothing here builds a per-side reader');
});

test('EVERY handover goes through the mark, and none builds its own slice', () => {
  // The finding that this file exists for, in its newest shape: a slice unit test proves the
  // function is right, and proves nothing about whether the Team server calls it. A consumer left
  // behind re-sends the subject the person marked away from, and is billed for it every turn.
  const text = read(join('src', 'chatCommand.ts'));

  // The five places a conversation is handed to a model that has not heard it. Each is named by the
  // event rather than by its line, so this says what broke when it breaks.
  const handovers: Array<readonly [string, RegExp]> = [
    ['a re-ask, which is a switch by another name', /thread\.carry = carriedFrom\(again\.said, thread\.carryFrom\)/],
    ['a Team server, handed the conversation every turn', /thread\.forgetful[\s\S]{0,700}?thread\.carry = carriedFrom\(/],
    ['a vendor that lost the conversation', /contextLost === true[\s\S]{0,200}?thread\.carry = carriedFrom\(/],
    ['a model switch', /thread\.carry = carriedFrom\(thread\.messages, thread\.carryFrom\);\s*\n\s*show\(entry, false/],
    ['the first turn after a window reload', /carry: carriedFrom\(saved\.messages, carryMark\(/],
  ];
  for (const [what, shape] of handovers) {
    assert.match(text, shape, `the handover for ${what} does not go through the mark`);
  }

  // And NOTHING builds a carry of its own. A sixth site added later, spelled the old way, is the
  // way this regresses — so the count is the assertion, not the list above.
  const built = [...text.matchAll(/\bcarry(?:: | = )/g)].length;
  const marked = [...text.matchAll(/carry(?:: | = )carriedFrom\(/g)].length;
  // An EMPTY carry hands over nothing and needs no mark; the interface's own line declares the
  // field rather than filling it. Neither is a handover, and both would otherwise read as one.
  const empty = [...text.matchAll(/carry(?:: | = )\[\]/g)].length;
  const declared = [...text.matchAll(/carry: readonly /g)].length;

  assert.strictEqual(built - empty - declared, marked,
    `${built - empty - declared - marked} carry site(s) build a conversation without the mark`);
});

test('a mark set AFTER a switch reaches the conversation that switch had already staged', () => {
  // The finding that mattered most of the whole round. A switch, a restore and a lost context all
  // fill `carry` ahead of the next question. Pressing the button after switching and before asking
  // moved the rule, drew the line, and sent the entire conversation anyway — which is the one case
  // this feature exists for. (gemini, the code round.)
  const text = read(join('src', 'chatCommand.ts'));

  assert.match(
    text,
    /onCarryFrom[\s\S]{0,1600}?if \(mine\.carry\.length > 0\) \{\s*\n\s*mine\.carry = carriedFrom\(mine\.messages, mine\.carryFrom\);/,
    'a mark set after a switch leaves the staged conversation whole, and it is sent whole',
  );
});

test('the mark only ever moves FORWARD, however the page asks', () => {
  // The button is offered on the last answer alone, so a real press can never name a position above
  // the mark already set. A lower one is a stale page or a forged message, and taking it would put
  // back a conversation somebody deliberately excluded — on a Team server, at a price. (codex, as a
  // security finding.)
  const text = read(join('src', 'chatCommand.ts'));

  assert.match(
    text,
    /mine\.carryFrom = Math\.max\(\s*\n\s*carryMark\(at, mine\.messages\.length\),\s*\n\s*carryMark\(mine\.carryFrom, mine\.messages\.length\),/,
    'the host takes whatever position the page names, including one above the mark already set',
  );
});

test('a re-ask brings the mark back with the transcript it truncated', () => {
  // A re-ask drops the rejected answer and its question. Clamping at each use keeps that turn
  // honest, but the STORED mark would stay past the end and point at an unrelated message once the
  // conversation grew again. (gemini, the code round.)
  const text = read(join('src', 'chatCommand.ts'));

  assert.match(
    text,
    /thread\.messages = thread\.messages\.slice\(0, -2\);[\s\S]{0,400}?thread\.carryFrom = carryMark\(thread\.carryFrom, thread\.messages\.length\);/,
    'a re-ask leaves the mark past the end of the transcript it just shortened',
  );
});
