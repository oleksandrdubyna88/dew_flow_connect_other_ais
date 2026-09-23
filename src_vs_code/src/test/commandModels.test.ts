import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';

import {
  COMMAND_MODELS_SINCE,
  SHIPPED_COMMAND_MODELS,
  commandModelTarget,
  commandModelsAfter,
  commandModelsEnv,
  commandModelsFrom,
  commandModelsSkewNote,
  kindsOnTheWire,
  resolvedPair,
} from '../commandModels';
import { CALLER_KINDS } from '../consultSettings';
import { DEFAULTS, envBlock, settingWrite } from '../settingsShape';

/**
 * The split order's models per caller kind (issue #117): what is read, what crosses to the server,
 * what one box writes, and when the panel warns about an older server.
 */

const shared = JSON.parse(
  readFileSync(join(__dirname, '..', '..', '..', 'shared', 'command-models.json'), 'utf8'),
) as { shipped: Record<string, { strongest: string; implementation: string }> };

test('the shipped pairs are the ones in the shared file, for exactly the caller kinds that exist', () => {
  // `shared/command-models.json` is owned by neither half; coai-mcp asserts CommandModels.Shipped
  // against it too, so a shipped pair changed on one side goes red on that side.
  assert.deepEqual(SHIPPED_COMMAND_MODELS, shared.shipped);
  assert.deepEqual(Object.keys(SHIPPED_COMMAND_MODELS).sort(), CALLER_KINDS.map(({ id }) => id).sort());
});

test('a pristine panel sends no COAI_COMMAND_MODELS at all', () => {
  assert.equal(envBlock(DEFAULTS)['COAI_COMMAND_MODELS'], undefined);
  assert.deepEqual(kindsOnTheWire(commandModelsFrom(undefined)), []);
});

test('only the kind whose pair differs crosses, and it crosses resolved', () => {
  const models = commandModelsFrom({ codex: { strongest: ' gpt-6-astra ' }, claude: { strongest: 'Fable' } });

  // `claude` typed exactly its shipped name, so it resolves to the shipped pair and stays home.
  assert.deepEqual(JSON.parse(commandModelsEnv(models)['COAI_COMMAND_MODELS']!), {
    codex: { strongest: 'gpt-6-astra', implementation: '' },
  });
});

test('a record holding one kind still answers the shipped pair for every other kind', () => {
  const models = commandModelsFrom({ codex: { strongest: 'gpt-6-astra' } });

  assert.deepEqual(resolvedPair(models, 'claude'), { strongest: 'Fable', implementation: 'Opus' });
  assert.deepEqual(resolvedPair(models, 'gemini'), { strongest: '', implementation: '' });
});

test('a blank field keeps the shipped name for that field only', () => {
  const models = commandModelsFrom({ claude: { implementation: 'sonnet' } });

  assert.deepEqual(resolvedPair(models, 'claude'), { strongest: 'Fable', implementation: 'sonnet' });
});

test('junk of any type reads as nothing chosen, never as a crash', () => {
  for (const raw of [null, 5, 'text', [], { claude: 5 }, { codex: { strongest: 7, implementation: null } }]) {
    assert.deepEqual(kindsOnTheWire(commandModelsFrom(raw)), [], JSON.stringify(raw));
  }
});

test('typing a name stores that one field and keeps everything else in the record', () => {
  const after = commandModelsAfter(
    { claude: { implementation: 'sonnet' }, cursor: { strongest: 'c-max' } }, 'codex', 'strongest', ' gpt-6-astra ');

  assert.deepEqual(after, {
    claude: { implementation: 'sonnet' },
    // A kind this build does not know was written by a newer panel; deleting it would be a write
    // about something this panel cannot see.
    cursor: { strongest: 'c-max' },
    codex: { strongest: 'gpt-6-astra' },
  });
});

test('a kind this build does not know is never written, whatever the message says', () => {
  // The kind arrives in a webview message; `__proto__` indexed into a record would read
  // Object.prototype and write a key nobody asked for — the refusal consultantRecordUpdate makes.
  const current = { claude: { strongest: 'opus' } };

  assert.deepEqual(commandModelsAfter(current, '__proto__', 'strongest', 'x'), current);
  assert.deepEqual(commandModelsAfter(current, 'cursor', 'strongest', 'x'), current);
});

test('choosing the CLI alias of the default is choosing the default', () => {
  // The Claude picker offers `fable`; the shipped name is `Fable`. Sending it would change nothing
  // and put a warning about an older server beside a panel that asked for nothing new.
  const models = commandModelsFrom({ claude: { strongest: 'fable', implementation: 'opus' } });

  assert.deepEqual(kindsOnTheWire(models), []);
  assert.equal(commandModelsSkewNote('0.32.0', models), '');
});

test('a picker id names a known kind and a real slot, or nothing', () => {
  assert.deepEqual(commandModelTarget('codex:strongest'), { kind: 'codex', label: 'Codex', slot: 'strongest' });
  assert.equal(commandModelTarget('__proto__:strongest'), undefined);
  assert.equal(commandModelTarget('codex:nonsense'), undefined);
  assert.equal(commandModelTarget('codex'), undefined);
});

test('clearing a box removes the field, and a kind with no fields left drops out', () => {
  assert.deepEqual(
    commandModelsAfter({ claude: { strongest: 'opus', implementation: 'sonnet' } }, 'claude', 'strongest', '  '),
    { claude: { implementation: 'sonnet' } });
  assert.deepEqual(commandModelsAfter({ claude: { strongest: 'opus' } }, 'claude', 'strongest', ''), {});
});

test('a picker write is routed by its caller kind, and only a real slot is written', () => {
  assert.deepEqual(settingWrite({ key: 'strongest', value: 'gpt-6-astra', commandModel: 'codex' }), {
    kind: 'commandModel', key: 'strongest', value: 'gpt-6-astra', commandModel: 'codex',
  });
  assert.equal(settingWrite({ key: 'nonsense', value: 'x', commandModel: 'codex' }), undefined,
    'a key that is not one of the two slots is a page and a host that disagree');
  // Without the kind it is an ordinary write of whatever key it names — which is why the page must
  // send the kind.
  assert.equal(settingWrite({ key: 'strongest', value: 'x' })?.kind, 'plain');
});

test('the skew note speaks only for a known, older server and a panel that changed something', () => {
  const changed = commandModelsFrom({ codex: { strongest: 'gpt-6-astra' } });
  const pristine = commandModelsFrom(undefined);

  assert.match(commandModelsSkewNote('0.32.0', changed), /names Fable\s+and Opus to every caller/);
  assert.equal(commandModelsSkewNote('', changed), '', 'an unknown server is not called old');
  assert.equal(commandModelsSkewNote(COMMAND_MODELS_SINCE, changed), '');
  assert.equal(commandModelsSkewNote('0.32.0', pristine), '',
    'a pristine panel means Fable and Opus, which is what an older server says anyway');
});
