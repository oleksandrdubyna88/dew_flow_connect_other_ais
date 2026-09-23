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
  // ALL THREE modules a session is started or replaced in, since the command file was split: the
  // new conversation is still an entry point, a restored one is reopened by the turn, and a switch
  // is the launch. Reading one of them is how this guard stops guarding the omission it exists for.
  const text = ['chatCommand.ts', 'chatTurn.ts', 'chatLaunch.ts']
    .map((one) => read(join('src', one)))
    .join('\n');

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
  // The page hooks moved to `chatHooks.ts` when the command file was split — still ONE object
  // built in ONE place, which is what these assert.
  const text = read(join('src', 'chatHooks.ts'));

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
  // The page hooks moved to `chatHooks.ts` when the command file was split — still ONE object
  // built in ONE place, which is what these assert.
  const text = read(join('src', 'chatHooks.ts'));

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
  // The turn moved to `chatTurn.ts` when the command file was split — a re-ask and a retry still
  // go THROUGH `oneTurn` rather than beside it, which is what these assert.
  const text = read(join('src', 'chatTurn.ts'));

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

test('a retry goes to the model that failed, or it is not offered at all', () => {
  // A retry is the same question to the same model; a re-ask is the same question to a DIFFERENT
  // one. That difference is the whole of what tells the two features apart, so a retry that followed
  // whatever is selected when the button is pressed would be a re-ask wearing the other one's label.
  // Nothing else records which model failed — a failed turn appends no answer, and an answer is the
  // only message carrying the model that produced it. (codex, the second code round.)
  // The page push moved to `chatShow.ts` when the command file was split — it had to come out
  // before the archive, the turn, the launch and the hooks, all of which call it.
  // The `canRetry` arm went with the page push; the failing branch that writes the pair down is
  // still in the command file until the turn comes out.
  const text = read(join('src', 'chatTurn.ts')) + read(join('src', 'chatShow.ts'));

  assert.match(
    text,
    /if \(!result\.ok\) \{[\s\S]{0,400}?thread\.failedWith = pairOf\(thread\)/u,
    'a failed turn no longer records which pair it failed under, so a retry cannot know where to go',
  );
  assert.match(
    text,
    /canRetry:[\s\S]{0,400}?thread\.failedWith === pairOf\(thread\)/u,
    'the retry is offered without checking the model still matches the one that failed',
  );
  // And cleared when a new turn supersedes the failure, or the offer outlives what it was about.
  assert.match(
    text,
    /thread\.running = true;[\s\S]{0,300}?thread\.failedWith = '';/u,
    'a new turn leaves the old failure retryable behind it',
  );
});

test('a retry leaves the carry exactly as the failure left it', () => {
  // The failing branch of `oneTurn` deliberately does NOT clear `thread.carry` — its comment says so
  // in as many words: "the retry — the same question, one keypress later". `oneRetry` is that
  // keypress, so recomputing the carry there would make it the SECOND place deciding what a failed
  // turn carries, and two places deciding one thing is one place disagreeing. (codex, the plan
  // round: the promise had no targeted verification.)
  //
  // Structural, because nothing observable at this level tells "left alone" apart from "recomputed
  // to the same value" — and pinned at BOTH ends so it cannot survive its own break: the function
  // must still be found, and it must still derive its question from the transcript.
  // The turn moved to `chatTurn.ts` when the command file was split — a re-ask and a retry still
  // go THROUGH `oneTurn` rather than beside it, which is what these assert.
  const text = read(join('src', 'chatTurn.ts'));
  const retry = /async function oneRetry\([\s\S]*?\n\}/u.exec(text);

  assert.ok(retry, 'oneRetry was renamed or removed, and this guard stopped guarding anything');
  assert.match(
    retry[0],
    /retryFrom\(thread\.messages\)/u,
    'the retry stopped deriving its question from the transcript, so this guard reads the wrong thing',
  );
  assert.doesNotMatch(
    retry[0],
    /thread\.carry\s*=/u,
    'a retry recomputed the carry that the failure preserved for it',
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
  // EVERY module the command file was split into, because the count below is the assertion and a
  // handover that moved out of this file is a handover this test stopped watching. Named rather than
  // globbed: `chatFresh.ts` and `chatStoreImport.ts` carry the word without handing anything over.
  const text = [
    'chatCommand.ts',
    'chatArchive.ts',
    'chatLaunch.ts',
    'chatTurn.ts',
    'chatHooks.ts',
    'chatConversationRestore.ts',
    'chatLaunch.ts',
    'chatAccess.ts',
  ].map((one) => read(join('src', one))).join('\n');

  // The five places a conversation is handed to a model that has not heard it. Each is named by the
  // event rather than by its line, so this says what broke when it breaks.
  const handovers: Array<readonly [string, RegExp]> = [
    ['a re-ask, which is a switch by another name', /thread\.carry = carriedFrom\(again\.said, thread\.carryFrom\)/],
    ['a Team server, handed the conversation every turn', /thread\.forgetful[\s\S]{0,700}?thread\.carry = carriedFrom\(/],
    ['a vendor that lost the conversation', /contextLost === true[\s\S]{0,200}?thread\.carry = carriedFrom\(/],
    // A model switch and a change of agent mode (issue #289) are one move — a new session with the
    // conversation carried — so both go through `install`, and `install` goes through the mark.
    ['a model switch or a change of agent mode', /export function install\([\s\S]{0,2600}?thread\.carry = carriedFrom\(thread\.messages, thread\.carryFrom\);\s*\n\}/],
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
  // The page hooks moved to `chatHooks.ts` when the command file was split — still ONE object
  // built in ONE place, which is what these assert.
  const text = read(join('src', 'chatHooks.ts'));

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
  // The page hooks moved to `chatHooks.ts` when the command file was split — still ONE object
  // built in ONE place, which is what these assert.
  const text = read(join('src', 'chatHooks.ts'));

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
  // The turn moved to `chatTurn.ts` when the command file was split — a re-ask and a retry still
  // go THROUGH `oneTurn` rather than beside it, which is what these assert.
  const text = read(join('src', 'chatTurn.ts'));

  assert.match(
    text,
    /thread\.messages = thread\.messages\.slice\(0, -2\);[\s\S]{0,400}?thread\.carryFrom = carryMark\(thread\.carryFrom, thread\.messages\.length\);/,
    'a re-ask leaves the mark past the end of the transcript it just shortened',
  );
});

/**
 * A keybinding that acts on OUR OWN picker rather than opening anything.
 *
 * <p>`coai.conversationsPickerOpen` is a context key this extension sets for exactly as long as the
 * conversation picker is on screen, so a binding carrying it can only fire while that list is already
 * open — in front of a person who is choosing a row. Such a command acts ON the list: it forgets the
 * conversation under the cursor, or it will one day rename or pin it. It opens no chat, records no
 * invocation, and counting it as a door would inflate *Opened* on the spending page with presses that
 * opened nothing.</p>
 *
 * <p><b>This is the rule the next person to add one will meet</b>, so it is written here rather than
 * as a name on an exclusion list: the test below does not know about `coai.forgetPickedConversation`
 * and never will. What it knows is that a binding scoped to our picker being open is not a way IN.</p>
 */
const actsOnOurPicker = (row: Contribution): boolean => (row.when ?? '').includes('coai.conversationsPickerOpen');

/** Which commands a person can actually open a chat with, TAKEN FROM THE MANIFEST rather than typed. */
function doorCommands(): readonly string[] {
  const menus = MANIFEST.contributes.menus;
  const fromMenus = [...(menus['webview/context'] ?? []), ...(menus['editor/context'] ?? [])];
  const offered = [...fromMenus, ...MANIFEST.contributes.keybindings.filter((one) => !actsOnOurPicker(one))]
    .map((one) => one.command ?? '')
    .filter((one) => one.length > 0);

  return [...new Set(offered)];
}

test('a keybinding scoped to our own picker is not a door, and nothing that is a door hides behind that scope', () => {
  // The exclusion above is a hole if it can be used to smuggle a real door out of the count, so it is
  // checked from the other side: a command excluded by it must have EVERY one of its bindings scoped
  // that way, and must not also be offered from a menu — where there is no picker for it to act on.
  const excluded = MANIFEST.contributes.keybindings.filter(actsOnOurPicker).map((row) => row.command ?? '');
  const menus = MANIFEST.contributes.menus;
  const inMenus = [...(menus['webview/context'] ?? []), ...(menus['editor/context'] ?? [])].map((row) => row.command);

  assert.ok(excluded.length > 0, 'nothing is scoped to the picker any more — this exclusion is watching a door that moved');
  for (const command of new Set(excluded)) {
    const bindings = MANIFEST.contributes.keybindings.filter((row) => row.command === command);
    assert.ok(bindings.every(actsOnOurPicker),
      command + ' is bound both inside our picker and outside it, so the exclusion hides a real door');
    assert.ok(!inMenus.includes(command),
      command + ' is offered from a right-click menu, where the picker it acts on is not open');
    assert.ok(!read('src/extension.ts').includes("registerCommand('" + command + "', (...args"),
      command + ' takes what VS Code hands a menu item, which is not how a command acting on the picker is invoked');
  }
});

/**
 * Every door records ONE invocation, and records it before anything can refuse.
 *
 * <p>The half a unit test cannot see. Every module under the spending page can be right while one of
 * the commands never writes a line: the parser, the grouping and the rendering would all stay green
 * and the count on the page would simply be wrong. A reviewer asked for this by name on the plan
 * round.</p>
 *
 * <p>It also checks the ORDER, which is the finding that moved this recording up into the command
 * handlers in the first place: a door that cannot resolve a CLI, or that the person dismisses,
 * returns before it delivers anything - and an attempt is exactly what the count is about.</p>
 *
 * <p><b>The list is DERIVED, not typed.</b> A sixth way into a chat is a sixth entry in one of the
 * manifest's menus or keybindings, and the first version of this test would have stayed green while
 * it recorded nothing. (codex, the code round.)</p>
 */
test('each door offered by the manifest records itself, once, before it does anything that can refuse', () => {
  const source = read('src/extension.ts');
  const CALL = "noteChatDoor('";
  const doors = doorCommands();

  assert.ok(doors.length >= 5, 'the manifest offers fewer ways into a chat than this product has');
  for (const command of doors) {
    const at = source.indexOf("registerCommand('" + command + "'");
    assert.notStrictEqual(at, -1, command + ' is offered by the manifest and registered by nothing');
    const next = source.indexOf('registerCommand(', at + 20);
    const handler = source.slice(at, next === -1 ? source.length : next);
    const recorded = handler.split(CALL).slice(1).map((rest) => rest.slice(0, rest.indexOf("'")));

    assert.strictEqual(recorded.length, 1, command + ' does not record exactly one invocation');
    // The work is whatever the handler calls after it; the recorder must come first, and there is
    // exactly one thing before it that is allowed to be there.
    const work = handler.indexOf('(chatPanels');
    assert.ok(work !== -1, command + ' does not reach the chat at all');
    assert.ok(handler.indexOf(CALL) < work,
      command + ' records the invocation after the work that can refuse it, so a refusal goes uncounted');
  }
});

test('nothing records a door the manifest does not offer', () => {
  // The other direction: a recorder left behind on a command that no longer opens a chat would
  // inflate the count on the page and nothing else would notice.
  const source = read('src/extension.ts');

  assert.strictEqual(source.split("noteChatDoor('").length - 1, doorCommands().length,
    'the number of recorders and the number of doors the manifest offers have drifted apart');
});
