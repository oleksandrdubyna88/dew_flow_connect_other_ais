import assert from 'node:assert/strict';
import { test } from 'node:test';
import { chatCommandOf, isInside } from '../chatMessages';

/**
 * What the page said, read without a host.
 *
 * <p>This file exists because of one finding on the plan round: every other test in epic 2 exercised
 * the pure registry and the pure page, while the only module that turned a webview message into an
 * action was the one no unit test could reach. A wrong message type would then ship with all of them
 * green — a person's send quietly ignored, or delivered somewhere else. So the reading moved into a
 * function, and this is that function under test.</p>
 */

test('a send carries its text, trimmed', () => {
  assert.deepStrictEqual(
    chatCommandOf({ type: 'command', command: 'send', text: '  why is it pinned?  ' }),
    { kind: 'send', text: 'why is it pinned?' },
  );
});

test('an empty send is ignored rather than spending a vendor turn on a stray Enter', () => {
  assert.deepStrictEqual(chatCommandOf({ type: 'command', command: 'send', text: '   ' }), { kind: 'ignore' });
  assert.deepStrictEqual(chatCommandOf({ type: 'command', command: 'send' }), { kind: 'ignore' });
});

test('a pick carries its model id, and a nameless one is ignored', () => {
  assert.deepStrictEqual(
    chatCommandOf({ type: 'command', command: 'pick', id: 'remsoftdev-codex' }),
    { kind: 'pick', id: 'remsoftdev-codex' },
  );
  assert.deepStrictEqual(chatCommandOf({ type: 'command', command: 'pick', id: '' }), { kind: 'ignore' });
});

test('the zoom control posts its own shape, not a command', () => {
  // It is the shared control's message, and it has been that shape since the help page shipped.
  assert.deepStrictEqual(chatCommandOf({ type: 'zoom', delta: -1 }), { kind: 'zoom', delta: -1 });
});

test('a zoom with nonsense in it moves nothing rather than throwing', () => {
  assert.deepStrictEqual(chatCommandOf({ type: 'zoom', delta: 'lots' }), { kind: 'zoom', delta: 0 });
  assert.deepStrictEqual(chatCommandOf({ type: 'zoom', delta: Number.NaN }), { kind: 'zoom', delta: 0 });
});

test('the two capped actions are read', () => {
  assert.deepStrictEqual(chatCommandOf({ type: 'command', command: 'restart' }), { kind: 'restart' });
  assert.deepStrictEqual(chatCommandOf({ type: 'command', command: 'useLocal' }), { kind: 'useLocal' });
});

test('a page that trapped an error tells the host what it was', () => {
  assert.deepStrictEqual(
    chatCommandOf({ type: 'pageError', message: 'x is not defined' }),
    { kind: 'pageError', message: 'x is not defined' },
  );
  // Nothing to report is not a report.
  assert.deepStrictEqual(chatCommandOf({ type: 'pageError', message: '' }), { kind: 'ignore' });
});

test('a word this host does not know is ignored, not thrown on', () => {
  // A webview retained across a reload can be older than the extension talking to it - or newer.
  assert.deepStrictEqual(chatCommandOf({ type: 'command', command: 'summarise' }), { kind: 'ignore' });
  assert.deepStrictEqual(chatCommandOf({ type: 'somethingElse' }), { kind: 'ignore' });
});

test('nothing at all is ignored', () => {
  assert.deepStrictEqual(chatCommandOf(undefined), { kind: 'ignore' });
  assert.deepStrictEqual(chatCommandOf({}), { kind: 'ignore' });
});

test('a message whose fields are the wrong types cannot become a command', () => {
  // The bridge is untyped: everything here arrived as JSON from a page.
  assert.deepStrictEqual(chatCommandOf({ type: 'command', command: 'send', text: 42 }), { kind: 'ignore' });
  assert.deepStrictEqual(chatCommandOf({ type: 'command', command: 'pick', id: { id: 'x' } }), { kind: 'ignore' });
  assert.deepStrictEqual(chatCommandOf({ type: 42, command: 'send', text: 'hi' }), { kind: 'ignore' });
});

test('a stop carries the turn it means to stop', () => {
  // The number is what keeps a late stop from ending the turn AFTER the one it was pressed for: the
  // page renders the control against a turn, and the host refuses a number that is no longer running.
  assert.deepStrictEqual(
    chatCommandOf({ type: 'command', command: 'stop', turn: 3 }),
    { kind: 'stop', turn: 3 },
  );
});

test('a stop that names no turn is not a stop', () => {
  // There is no wildcard. The first version let a missing number mean "whichever is running", which
  // hands back the exact defect the number exists to prevent — and no such page can exist, because
  // `stop` and its turn number ship in the same release. (codex and gemini, the code round.)
  assert.deepStrictEqual(chatCommandOf({ type: 'command', command: 'stop' }), { kind: 'ignore' });
});

test('a stop naming something that is not a turn is refused, not applied to whatever is running', () => {
  // The boundary rule: a value from a surface the host does not control is validated before it
  // reaches anything that acts on it. Every one of these used to become "stop whatever is running".
  for (const turn of [-1, 0, 1.5, Number.NaN, Number.POSITIVE_INFINITY, Number.MAX_SAFE_INTEGER + 2, 'two', null, {}]) {
    assert.deepStrictEqual(
      chatCommandOf({ type: 'command', command: 'stop', turn }),
      { kind: 'ignore' },
      `a turn of ${String(turn)} should have been refused outright`,
    );
  }
});

test('a link from an answer is opened only when it is http or https', () => {
  assert.deepStrictEqual(
    chatCommandOf({ type: 'command', command: 'openLink', url: 'https://example.com/a?b=1' }),
    { kind: 'openLink', url: 'https://example.com/a?b=1' },
  );
  // The page emits no href at all, so this is the boundary — and the boundary is where a scheme is
  // refused rather than where it is hoped nobody wrote one.
  for (const url of ['javascript:alert(1)', 'data:text/html,<script>x</script>', 'vbscript:x',
    'file:///etc/passwd', 'ftp://example.com', 'HTTPS:/broken', 'https://', '']) {
    assert.deepStrictEqual(chatCommandOf({ type: 'command', command: 'openLink', url }),
      { kind: 'ignore' }, `${url} was accepted as a link to open`);
  }
});

test('a file reference from an answer is confined before anything opens it', () => {
  assert.deepStrictEqual(
    chatCommandOf({ type: 'command', command: 'openLink', file: 'src/a.ts', line: 12 }),
    { kind: 'openFile', path: 'src/a.ts', line: 12 },
  );
  assert.deepStrictEqual(
    chatCommandOf({ type: 'command', command: 'openLink', file: 'a.ts' }),
    { kind: 'openFile', path: 'a.ts', line: 0 },
  );

  // A model writing `.git/config` or `../../.ssh/id_rsa` is not a hypothetical. The renderer refuses
  // these too; a page is a surface, not a boundary, and anything can post to it.
  for (const file of ['../a.ts', 'a/../b.ts', '/etc/passwd', 'C:/x/a.ts', 'C:\\x\\a.ts',
    'src\\a.ts', 'file:///a.ts', 'https://e.com/a.ts', '']) {
    assert.deepStrictEqual(chatCommandOf({ type: 'command', command: 'openLink', file }),
      { kind: 'ignore' }, `${file} was accepted as a workspace file`);
  }

  // A line that is not a line is the top of the file, never a negative or a fraction.
  for (const line of [-3, 1.5, Number.NaN, '12', undefined]) {
    const command = chatCommandOf({ type: 'command', command: 'openLink', file: 'a.ts', line });
    assert.deepStrictEqual(command, { kind: 'openFile', path: 'a.ts', line: 0 }, `line ${String(line)}`);
  }
});

test('a copy names an answer by an index that is really an index', () => {
  assert.deepStrictEqual(chatCommandOf({ type: 'command', command: 'copyAnswer', index: 3 }),
    { kind: 'copyAnswer', index: 3 });
  assert.deepStrictEqual(chatCommandOf({ type: 'command', command: 'copyAnswer', index: 0 }),
    { kind: 'copyAnswer', index: 0 });
  for (const index of [-1, 1.5, '2', undefined, Number.NaN]) {
    assert.deepStrictEqual(chatCommandOf({ type: 'command', command: 'copyAnswer', index }),
      { kind: 'ignore' }, `index ${String(index)} was accepted`);
  }
});


test('a sibling folder that merely starts with the workspace root is not inside it', () => {
  // A string prefix is not containment. With a root of `/w/app`, the path `/w/app-secret/x` starts
  // with it and is a different project — the one belonging to somebody who did not open this tab.
  assert.strictEqual(isInside('/w/app', '/w/app/src/a.ts'), true);
  assert.strictEqual(isInside('/w/app', '/w/app'), true);
  assert.strictEqual(isInside('/w/app', '/w/app-secret/config.json'), false,
    'a sibling sharing the root\'s first letters was treated as inside it');
  assert.strictEqual(isInside('/w/app/', '/w/app-secret/config.json'), false);
  assert.strictEqual(isInside('/w/app', '/w/other/a.ts'), false);
  assert.strictEqual(isInside('/w/app', '/w/App/a.ts'), false,
    'containment was decided case-insensitively, which is a guess about the filesystem');
});

test('an extensionless file a model names is still a file', () => {
  // Dockerfile, LICENSE, Makefile. The renderer used to demand a dot before it would offer a link,
  // while the host's own check did not — so the two disagreed about what a path IS, and the reader
  // simply never got a link to any of them. (gemini, the code round.)
  for (const named of ['Dockerfile', 'LICENSE', 'src/routes/index']) {
    assert.deepStrictEqual(
      chatCommandOf({ type: 'command', command: 'openLink', file: named }),
      { kind: 'openFile', path: named, line: 0 },
      `${named} was refused as a workspace file`,
    );
  }
});
