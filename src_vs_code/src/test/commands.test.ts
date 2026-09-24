import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { SHIPPED_COMMAND_TEXTS } from '../commandTexts.generated';
import { SHIPPED_COMMANDS, commandsFrom, fileIdOf, hasText, whyNotGivable } from '../commands';

/** The gate's commands as the Edit commands page reads them — issue #467, Epic B. */

const shared = join(__dirname, '..', '..', '..', 'shared');

test('the generated texts are the shared folder, file for file and word for word', () => {
  const dir = join(shared, 'commands');
  const onDisk = Object.fromEntries(readdirSync(dir).filter((name) => name.endsWith('.md'))
    .map((name) => [name.slice(0, -3), readFileSync(join(dir, name), 'utf8').replace(/[\r\n]+$/, '')]));

  assert.deepEqual(SHIPPED_COMMAND_TEXTS, onDisk);
});

test('the page lists every shipped text once, and no text the build does not ship', () => {
  assert.deepEqual(SHIPPED_COMMANDS.map((one) => one.id).sort(), Object.keys(SHIPPED_COMMAND_TEXTS).sort());
  assert.equal(new Set(SHIPPED_COMMANDS.map((one) => one.id)).size, SHIPPED_COMMANDS.length);
});

test('the markers the page shows are the ones the server keeps', () => {
  // The server's copies are held to shared/command-models.json by its own suite; these are held to it here.
  const words = JSON.parse(readFileSync(join(shared, 'command-models.json'), 'utf8')) as { orderOpensWith: string; splitOrderCarries: string };
  const marker = (id: string): string => SHIPPED_COMMANDS.find((one) => one.id === id)?.marker ?? '';

  assert.equal(marker('command-model'), words.orderOpensWith);
  assert.equal(marker('command-cadence-epic').trim(), words.splitOrderCarries);
  assert.match(marker('command-autonomy'), /^Work AUTONOMOUSLY/);
  assert.match(marker('command-already-split-epic'), /already under way$/);
});

test('a row is read with the server’s defaults, and a field this build does not know is carried', () => {
  assert.deepEqual(commandsFrom([{ id: 'docs', future: 7 }]), [{ id: 'docs', title: 'docs', enabled: false, stage: 'any', future: 7 }]);
  assert.deepEqual(commandsFrom([{ id: 'lint', title: 'Lint', enabled: true, stage: 'code' }]),
    [{ id: 'lint', title: 'Lint', enabled: true, stage: 'code' }]);
});

test('a row the server would refuse is not a row here either', () => {
  const rows = commandsFrom([{ id: 'Bad Name' }, { id: '../x' }, { id: '' }, null, 'x', { id: 'ok' }, { id: 'ok', title: 'second' },
    { id: 'odd', stage: 'sometimes' }]);

  assert.deepEqual(rows.map((one) => [one.id, one.title]), [['ok', 'ok'], ['odd', 'odd']]);
  assert.deepEqual(commandsFrom('not a list'), []);
});

test('a row named after a shipped text is not a command — it would be a second editor for that file', () => {
  // `{"id":"preamble"}` is `command-preamble`, the shipped Preamble's override; the server refuses the
  // name, and a row the page drew would have written — and on Remove deleted — that override. (our own
  // reviewer, the code round.)
  assert.deepEqual(commandsFrom([{ id: 'preamble' }, { id: 'autonomy' }, { id: 'mine' }]).map((one) => one.id), ['mine']);
});

test('a row is read as the server reads it: the id trimmed, the stage in any case', () => {
  assert.deepEqual(commandsFrom([{ id: ' docs ', stage: 'Plan' }, { id: 'lint', stage: 'CODE' }]).map((one) => [one.id, one.stage]),
    [['docs', 'plan'], ['lint', 'code']]);
});

test('a stage the page cannot read is kept as written, never turned into every round', () => {
  // The server refuses the row and says so on the panel; the page turning it into `any` would make it
  // a live command in every round instead. (our own reviewer, the code round.)
  const [row] = commandsFrom([{ id: 'odd', stage: 'later' }]);

  assert.equal(JSON.parse(JSON.stringify(row)).stage, 'later');
});

test('a custom command’s text is the file the server reads — the prefix added once', () => {
  assert.equal(fileIdOf('custom-1'), 'command-custom-1');
});

test('switching on asks for text, and a blank file is no text', () => {
  const row = { id: 'custom-1', title: 'Docs', enabled: false, stage: 'any' } as const;

  assert.match(whyNotGivable(row, {}), /"Docs" has no text yet/);
  assert.match(whyNotGivable(row, { 'command-custom-1': '  \n' }), /no text yet/);
  assert.equal(whyNotGivable(row, { 'command-custom-1': 'Update the docs.' }), '');
  const own: Record<string, string> = { toString: 'x' };
  assert.equal(hasText(own, 'toString'), true, 'an own key is a key; an inherited one is not — the next line');
  assert.equal(hasText({}, 'toString'), false);
});
