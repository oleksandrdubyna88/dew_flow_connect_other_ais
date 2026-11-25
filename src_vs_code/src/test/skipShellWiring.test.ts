import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { bodyOf, sourceFiles, sourceOf } from './sourceReading';

/**
 * The write of the skip list, as the host code is written (research/PLAN_the_chat_opens_from_a_terminal_E3.md, S3-T10).
 *
 * <p>`skipShellHost.ts` imports `vscode`, so no test can call it: the one write of E3 is held by reading it. The pure road is
 * covered by `skipShell.test.ts` over fake seams; what no seam shows is WHICH layer the real `update` targets, and that nothing
 * else in the extension writes the setting. Every scan has its companion: it finds the line it guards.</p>
 */

const HOST = sourceOf('skipShellHost.ts');

/** Every `.update(` call of the host with the text up to the end of its statement. */
const updates = (text: string): readonly string[] => [...text.matchAll(/\.update\([^;]*;/gu)].map((one) => one[0]);

test('the one write of the skip list targets the user settings and nothing else', () => {
  const found = updates(HOST).filter((call) => call.includes('SETTING'));

  assert.equal(found.length, 1, `the scan expected ONE update of the setting in skipShellHost.ts, found ${found.length}`);
  assert.ok(found[0]?.includes('vscode.ConfigurationTarget.Global'), `the write does not target ConfigurationTarget.Global: ${found[0]}`);
  assert.ok(!/ConfigurationTarget\.(Workspace|WorkspaceFolder)\b/u.test(HOST), 'skipShellHost.ts names a workspace or folder target');
  // The scan's own proof: the constants it relies on still name the setting.
  assert.ok(/const SECTION = 'terminal\.integrated';/u.test(HOST) && /const SETTING = 'commandsToSkipShell';/u.test(HOST), 'the host no longer names terminal.integrated.commandsToSkipShell through SECTION and SETTING');
});

test('no other file in src writes the setting', () => {
  const others = sourceFiles().filter((file) => file !== 'skipShellHost.ts');
  const writers = others.filter((file) => {
    const text = sourceOf(file);

    return /update\(\s*(['"`])[^'"`]*commandsToSkipShell/u.test(text)
      || (/getConfiguration\(\s*['"]terminal(\.integrated)?['"]\s*\)/u.test(text) && /\.update\(/u.test(text));
  });

  assert.deepEqual(writers, [], `${writers.join(', ')} writes terminal.integrated.commandsToSkipShell beside skipShellHost.ts`);
  // The scan's own proof: it does see a write of that shape.
  assert.ok(/getConfiguration\(\s*['"]terminal(\.integrated)?['"]\s*\)/u.test('x.getConfiguration(\'terminal.integrated\').update(a, b, c);') , 'the scan no longer recognises a write of the setting');
  assert.ok(sourceFiles().includes('skipShellHost.ts'), 'the host module is not where the scan expects it');
});

test('the host only supplies seams: the check, its texts and its memory key are the pure module\'s and one literal', () => {
  const bound = bodyOf(HOST, 'export function bindSkipShell(');

  assert.ok(bound.includes('latchedCheck('), 'the host does not run the check through the one latch');
  assert.ok(/onDidChangeConfiguration\(/u.test(bound) && bound.includes('${SECTION}.${SETTING}'), 'the check does not run again when the setting changes');
  assert.ok(/const MEMORY_KEY = 'coai\.skipShellAnswers';/u.test(HOST), 'the memory key is not coai.skipShellAnswers');
  assert.equal(sourceFiles().filter((file) => sourceOf(file).includes('coai.skipShellAnswers')).length, 1, 'the memory key is written in more than one file');
});

test('the skip-shell test door is built only behind the test-mode check', () => {
  // Built by the chat module since v2 E4 (`index.ts`); `CoaiExports`, the type other code reads, is still `extension.ts`'s.
  const text = sourceOf('../shell/modules/chat/index.ts');
  const built = [...text.matchAll(/skipShell\s*:\s*registered\.skipShell/gu)];

  assert.equal(built.length, 1, `the scan expected ONE place that hands out the door, found ${built.length}`);
  const before = text.slice(Math.max(0, (built[0]?.index ?? 0) - 400), built[0]?.index ?? 0);
  assert.match(before, /extensionMode\s*===\s*vscode\.ExtensionMode\.Test\s*\?\s*\{[^}]*$/u, 'the skip-shell test door is reachable outside test mode');
  assert.match(sourceOf('extension.ts'), /readonly skipShell\?:\s*SkipShellTestDoor/u, 'the field of CoaiExports is not optional, so production would have to supply it');
  assert.equal([...sourceOf('extension.ts').matchAll(/bindSkipShell\(/gu)].length, 0, 'the old activation binds the check again, beside the chat module');
  assert.equal([...sourceOf('../shell/modules/chat/register.ts').matchAll(/bindSkipShell\(/gu)].length, 1, 'the check is not bound exactly once');
});

test('the skip-shell door identifier appears only at its declaration, its binding and inside the test-mode ternary', () => {
  // The door's life since v2 E4: declared and bound in the chat registration (`register.ts`: the field, the binding, the shorthand it returns), and handed
  // out in one place, the test-mode ternary of the chat module (`index.ts`). The old activation must not name it at all, except in the `CoaiExports` type.
  // A use is allowed on a line that is plumbing: the field, the binding, the destructuring of what `bindThisSide` made, the returns that carry it to
  // `index.ts`. `index.ts` hands it out in one place, `skipShell: registered.skipShell`, behind the test-mode ternary (the test above).
  const plumbing = /readonly skipShell: SkipShellTestDoor|const skipShell = bindSkipShell\(|const \{[^}]*\bskipShell\b[^}]*\} = bindThisSide\(|return \{[^}]*\bskipShell\b[^}]*\};|\): ChatRegistration \{|function bindThisSide\(|skipShell: registered\.skipShell/u;
  for (const [file, atLeast] of [['../shell/modules/chat/register.ts', 3], ['../shell/modules/chat/index.ts', 1]] as const) {
    const text = sourceOf(file);
    const lines = text.split('\n').filter((line) => /\bskipShell\b/u.test(line) && !/^\s*(\/\/|\*|\/\*)/u.test(line));
    const stray = lines.filter((line) => !plumbing.test(line));

    // The scan's own proof: it finds the plumbing, so a reformat cannot empty it and pass for ever.
    assert.ok(lines.length >= atLeast, `the scan found only ${lines.length} uses of skipShell in ${file}`);
    assert.deepEqual(stray, [], `skipShell is used outside its declaration, its binding and the test-mode ternary in ${file}: the door can leave test mode`);
  }
  const old = sourceOf('extension.ts');
  assert.deepEqual([...old.matchAll(/\bskipShell\b/gu)].map((one) => old.slice(Math.max(0, (one.index ?? 0) - 10), one.index)), [' readonly '], 'the old activation names skipShell outside the type of its exports');
});

test('the check runs only in a focused window: activation and the setting listener both go through the focus gate', () => {
  const bound = bodyOf(HOST, 'export function bindSkipShell(');
  const gate = bound.indexOf('gatedByFocus(');

  assert.ok(gate >= 0 && bound.includes('vscode.window.state.focused'), 'the host does not gate the check on the focus of the window');
  assert.ok(/onDidChangeWindowState\(/u.test(bound) && /listener\.dispose\(\)/u.test(bound), 'the host does not wait for the first focus and then drop its listener');
  // Every trigger is `run()`, which calls the gated check; nothing calls the bare latched `check()` here, which would ask in the background.
  const run = bound.slice(bound.indexOf('const run = '), bound.indexOf('};', bound.indexOf('const run = ')));
  assert.ok(run.includes('gated()'), 'run() does not call the gated check');
  assert.equal([...bound.matchAll(/(?<![\w.])check\(\)/gu)].length, 0, 'a trigger calls the latched check directly, so a window in the background would ask');
  assert.equal([...bound.matchAll(/\brun\(\);/gu)].length, 2, 'the activation and the setting listener are not both a run()');
});
