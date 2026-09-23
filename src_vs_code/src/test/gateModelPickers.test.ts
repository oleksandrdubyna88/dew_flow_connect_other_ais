import assert from 'node:assert/strict';
import { test } from 'node:test';

import { CALLER_KINDS } from '../consultSettings';
import { commandModelsFrom } from '../commandModels';
import { DEFAULTS, settingMessageFrom, settingWrite } from '../settingsShape';
import { type Control, type Page, lastWrite, panelState, runPanel } from './panelPageHarness';

/**
 * The gate section's split-order model pickers (issue #117), RUN — the panel's own script over the
 * pickers the panel really renders. Every assertion answers "what would I see if the behaviour were
 * deleted": no picker for that kind, a write with no kind on it, a list of the wrong vendor's models.
 */

const codexList = [{ id: 'gpt-6-astra', label: 'GPT-6 Astra' }, { id: 'gpt-6-luna', label: 'GPT-6 Luna' }];

function gate(overrides: Parameters<typeof panelState>[1] = {}): Page {
  return runPanel(panelState('gate', { codexModels: codexList, ...overrides }));
}

function picker(page: Page, kind: string, slot: string): Control {
  const found = page.controls.filter((one) => one.dataset['commandModel'] === kind && one.dataset['setting'] === slot);
  assert.equal(found.length, 1, `${kind} has no ${slot} picker of its own — ${found.length} matched`);

  return found[0]!;
}

/** The options one picker's markup carries, by its id. */
function optionsOf(page: Page, kind: string, slot: string): readonly string[] {
  const open = page.html.indexOf(`id="commandModel-${kind}-${slot}"`);
  assert.ok(open >= 0, `the ${kind} ${slot} picker is not on the page`);
  const markup = page.html.slice(open, page.html.indexOf('</select>', open));

  return [...markup.matchAll(/<option value="([^"]*)"/g)].map((m) => m[1]!);
}

test('every caller kind has both pickers, and each writes for its own kind', () => {
  const page = gate();

  for (const { id } of CALLER_KINDS) {
    for (const slot of ['strongest', 'implementation']) {
      const control = picker(page, id, slot);
      control.value = `${id}-${slot}-model`;
      control.fire('change');

      assert.deepEqual(lastWrite(page), {
        type: 'setting', key: slot, value: `${id}-${slot}-model`,
        vendor: undefined, role: undefined, caller: undefined, commandModel: id,
      }, 'eight pickers share two slot names, so a write with no kind lands in whichever the host guesses');
    }
  }
});

test('what a picker sends reaches the host as a commandModel write, not as a setting of its own', () => {
  // The page and `settingWrite` were each right, and the line between them rebuilt the message from
  // a hand-written list of four fields: every choice in every picker became a write to an
  // undeclared `coai.strongest`, refused, and the picker snapped back. Found in #117's code review.
  const page = gate();
  const control = picker(page, 'codex', 'strongest');
  control.value = 'gpt-6-astra';
  control.fire('change');

  assert.deepEqual(settingWrite(settingMessageFrom(lastWrite(page))), {
    kind: 'commandModel', key: 'strongest', value: 'gpt-6-astra', commandModel: 'codex',
  });
});

test('a repaint puts focus back on the picker that had it, not on another kind sharing its slot name', () => {
  // The picker's focus id has a fifth segment, its kind; the pattern the host holds a focus id to
  // accepted four, so every picker lost its focus on a repaint. (CodeRabbit, PR #481.)
  const page = runPanel(panelState('gate', { codexModels: codexList }, { id: 'strongest||||codex', start: 0, end: 0 }));

  assert.deepEqual(
    page.controls.filter((one) => one.focused).map((one) => `${one.dataset['setting']}/${one.dataset['commandModel']}`),
    ['strongest/codex'],
    'the repaint put focus somewhere else, or nowhere',
  );
});

test('"another model…" asks the host for a name for THAT kind and slot, and writes nothing yet', () => {
  const page = gate();
  const control = picker(page, 'gemini', 'implementation');

  control.value = '__other__';
  control.fire('change');

  assert.deepEqual(page.posted.filter((one) => one['type'] === 'command'), [
    { type: 'command', command: 'customCommandModel', id: 'gemini:implementation' },
  ]);
  assert.equal(page.posted.filter((one) => one['type'] === 'setting').length, 0);
});

test('each kind is offered its OWN vendor’s models, never another’s', () => {
  const page = gate();

  assert.ok(optionsOf(page, 'codex', 'strongest').includes('gpt-6-astra'));
  assert.ok(optionsOf(page, 'claude', 'strongest').includes('fable'));
  assert.ok(!optionsOf(page, 'claude', 'strongest').includes('gpt-6-astra'), 'a Codex model offered to Claude Code');
  assert.ok(!optionsOf(page, 'codex', 'strongest').includes('fable'), 'a Claude model offered to Codex');
  // The default first, and a way to type any name last.
  assert.equal(optionsOf(page, 'other', 'strongest')[0], '');
  assert.equal(optionsOf(page, 'other', 'strongest').at(-1), '__other__');
});

test('a saved name no list knows is still an option, and is the selected one', () => {
  const page = gate({ settings: { ...DEFAULTS, commandModels: commandModelsFrom({ codex: { strongest: 'my-own-model' } }) } });
  const open = page.html.indexOf('id="commandModel-codex-strongest"');
  const markup = page.html.slice(open, page.html.indexOf('</select>', open));

  assert.match(markup, /<option value="my-own-model" selected>/,
    'a value that vanished from its own dropdown would read as reset while the order still names it');
});

test('an older installed server is named beside the pickers once a kind was changed', () => {
  const changed = { ...DEFAULTS, commandModels: commandModelsFrom({ codex: { strongest: 'gpt-6-astra' } }) };
  const older = gate({ settings: changed, server: { kind: 'known', version: '0.32.0', remembered: false, updateOffered: false } });
  const current = gate({ settings: changed, server: { kind: 'known', version: '0.33.0', remembered: false, updateOffered: false } });

  assert.match(older.html, /does not read these models/);
  assert.doesNotMatch(current.html, /does not read these models/);
});
