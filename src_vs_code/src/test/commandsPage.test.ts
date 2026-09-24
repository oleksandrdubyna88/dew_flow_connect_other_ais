import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { CommandRow } from '../commands';
import { commandEdit, commandsHtml, commandsSkewNote, type CommandsPageState } from '../commandsPage';
import { Node, runPageHtml } from './rolesPageHarness';

/**
 * The Edit commands page, RUN — issue #467, Epic B. Its script is executed against the roles page's DOM
 * shim, and every message it posts is read back through the host's own parser, so the page and the host
 * cannot drift apart without a test saying so. (gemini, the plan round.)
 */

const docs: CommandRow = { id: 'custom-1', title: 'Docs', enabled: false, stage: 'any' };

const state = (over: Partial<CommandsPageState> = {}): CommandsPageState =>
  ({ rows: [docs], texts: {}, serverVersion: '0.33.0', perSide: false, ...over });

const run = (over: Partial<CommandsPageState> = {}) => runPageHtml(commandsHtml(state(over), 'test-nonce'));

/** Every posted message, as the host reads it. */
const parsed = (posted: readonly Record<string, unknown>[]) => posted.map(commandEdit);

test('typing an override of a shipped text posts that text, and the host reads it as one', () => {
  const page = run();
  const box = new Node({ text: 'command-autonomy' }, 'TEXTAREA');
  box.value = 'Work without asking.';

  page.fire('input', box);

  assert.deepEqual(parsed(page.posted), [{ kind: 'text', fileId: 'command-autonomy', value: 'Work without asking.' }]);
});

test('restoring, adding and removing post what the host acts on', () => {
  const page = run({ texts: { 'command-autonomy': 'mine' } });
  const row = new Node({ id: 'custom-1' }, 'SECTION');

  page.fire('click', new Node({ restore: 'command-autonomy' }, 'BUTTON'));
  page.fire('click', new Node({ add: '' }, 'BUTTON'));
  page.fire('click', new Node({ remove: '' }, 'BUTTON').under(row));

  assert.deepEqual(parsed(page.posted), [
    { kind: 'restore', fileId: 'command-autonomy' },
    { kind: 'add', token: '' },
    { kind: 'remove', id: 'custom-1' },
  ]);
});

test('a tick and a stage post ONCE each, on change — never again on input', () => {
  const page = run();
  const row = new Node({ id: 'custom-1' }, 'SECTION');
  const box = new Node({ field: 'enabled' }).under(row);
  box.type = 'checkbox';
  box.checked = true;
  const stage = new Node({ field: 'stage' }, 'SELECT').under(row);
  stage.value = 'code';

  page.fire('input', box);
  page.fire('change', box);
  page.fire('input', stage);
  page.fire('change', stage);

  assert.deepEqual(parsed(page.posted), [
    { kind: 'switch', id: 'custom-1', value: true },
    { kind: 'restage', id: 'custom-1', value: 'code' },
  ]);
});

test('retitling posts the title, and the command’s own text box posts its file', () => {
  const page = run();
  const row = new Node({ id: 'custom-1' }, 'SECTION');
  const title = new Node({ field: 'title' }).under(row);
  title.value = 'Module docs';
  const body = new Node({ text: 'command-custom-1' }, 'TEXTAREA').under(row);
  body.value = 'Update the module docs.';

  page.fire('input', title);
  page.fire('input', body);

  assert.deepEqual(parsed(page.posted), [
    { kind: 'retitle', id: 'custom-1', value: 'Module docs' },
    { kind: 'text', fileId: 'command-custom-1', value: 'Update the module docs.' },
  ]);
});

test('the parser ignores what the page never sends', () => {
  for (const message of [null, 'add', {}, { type: 'nope' }, { type: 'switch', id: 'x', value: 'yes' }, { type: 'restage', id: 'x', value: 'weekly' }]) {
    assert.deepEqual(commandEdit(message), { kind: 'ignore' }, JSON.stringify(message));
  }
});

test('each shipped text shows its marker, its shipped words faintly, and Restore only when overridden', () => {
  const plain = commandsHtml(state(), 'n');
  const mine = commandsHtml(state({ texts: { 'command-autonomy': 'Mine.', 'command-preamble': '  \n' } }), 'n');

  assert.match(plain, /<b>Work AUTONOMOUSLY\. <\/b>/);
  assert.match(plain, /placeholder="Say that you are working autonomously/);
  assert.match(plain, /The server fills in <code>\{scope\}<\/code>/);
  assert.doesNotMatch(plain, /data-restore=/);
  assert.match(mine, /data-restore="command-autonomy"/);
  assert.doesNotMatch(mine, /data-restore="command-preamble"/, 'a blank file is no override, as the server reads it');
});

test('a title or a text holding markup stays text', () => {
  // A text can hold `</textarea><script>` — it is the person's own words, pasted from anywhere. (codex, the plan round.)
  const html = commandsHtml(state({
    rows: [{ ...docs, title: '"><script>alert(1)</script>' }],
    texts: { 'command-custom-1': '</textarea><script>alert(2)</script>', 'command-autonomy': '</textarea><img onerror=x>' },
  }), 'n');

  assert.equal((html.match(/<script/g) ?? []).length, 1, 'only the page’s own script');
  assert.doesNotMatch(html, /<img/);
  assert.match(html, /&lt;\/textarea&gt;&lt;script&gt;alert\(2\)/);
});

test('an older server is told it ignores all of this; a new one and an unknown one say nothing', () => {
  assert.match(commandsSkewNote('0.32.0'), /ignores these texts and commands.*0\.33\.0/);
  assert.equal(commandsSkewNote('0.33.0'), '');
  assert.equal(commandsSkewNote(''), '');
  assert.match(commandsHtml(state({ serverVersion: '0.32.0' }), 'n'), /class="stale"/);
});
