import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CHAT_AUTO_SEND, DEFAULT_AUTO_SEND, chatSettingsFrom, sendsImmediately } from '../chatSettings';
import { fromMenuArgs, triggerPlan } from '../chatTrigger';

/**
 * The four settings, and the decision they exist to make.
 *
 * <p>Three of these reach a MODEL, so a junk value here does not draw a wrong pixel — it asks a
 * wrong question and bills for the answer. `settings.json` is a file a person edits by hand, so
 * every fallback below is a case that will happen rather than one that might.</p>
 */

/** A settings reader over a plain object, which is what the host's `get` amounts to. */
const reading = (values: Record<string, unknown>) => (key: string) => values[key];

test('an untouched installation asks for an explanation in English, and waits on the menu path', () => {
  const settings = chatSettingsFrom(reading({}));

  assert.deepStrictEqual(settings, { prompt: 'Explain', language: 'en', autoSend: 'keyboard', model: '' });
});

test('a prompt somebody wrote is used exactly as written', () => {
  const prompt = 'Explain it to someone who knows the language but not this codebase.\nSay what worries the author.';

  assert.strictEqual(chatSettingsFrom(reading({ chatPrompt: prompt })).prompt, prompt);
});

test('a cleared prompt box is a cleared field, not a request for an empty question', () => {
  assert.strictEqual(chatSettingsFrom(reading({ chatPrompt: '   ' })).prompt, 'Explain');
  assert.strictEqual(chatSettingsFrom(reading({ chatPrompt: '' })).prompt, 'Explain');
});

test('a language the catalog does not know falls back rather than reaching the model', () => {
  assert.strictEqual(chatSettingsFrom(reading({ chatLanguage: 'ru' })).language, 'ru');
  assert.strictEqual(chatSettingsFrom(reading({ chatLanguage: 'kl' })).language, 'en');
  assert.strictEqual(chatSettingsFrom(reading({ chatLanguage: 42 })).language, 'en');
});

test('the language is its own setting, and does not borrow the help page’s', () => {
  // The owner's help page is set to English; borrowing it would have delivered English explanations,
  // which is exactly what the feature exists to avoid. One setting cannot answer two questions.
  const settings = chatSettingsFrom(reading({ helpLanguage: 'en', chatLanguage: 'ru' }));

  assert.strictEqual(settings.language, 'ru');
});

test('a junk auto-send value becomes the defensible default', () => {
  for (const value of CHAT_AUTO_SEND) {
    assert.strictEqual(chatSettingsFrom(reading({ chatAutoSend: value })).autoSend, value);
  }
  assert.strictEqual(chatSettingsFrom(reading({ chatAutoSend: 'sometimes' })).autoSend, DEFAULT_AUTO_SEND);
  assert.strictEqual(DEFAULT_AUTO_SEND, 'keyboard');
});

test('the default spends a vendor turn only where the passage is certain', () => {
  // The keybinding copied the selection itself, one moment ago. The menu took whatever was on the
  // clipboard and cannot know how old it is.
  assert.strictEqual(sendsImmediately('keyboard', false), true, 'the keybinding did not send');
  assert.strictEqual(sendsImmediately('keyboard', true), false, 'the menu sent a possibly stale passage');
});

test('always sends from both doors, never from neither', () => {
  assert.strictEqual(sendsImmediately('always', true), true);
  assert.strictEqual(sendsImmediately('always', false), true);
  assert.strictEqual(sendsImmediately('never', true), false);
  assert.strictEqual(sendsImmediately('never', false), false);
});

test('VS Code tells the two doors apart by what it hands the command', () => {
  // Measured with the phase-0 probe: a menu item is handed the webview, a keybinding nothing at all.
  assert.strictEqual(fromMenuArgs([{ webview: 'claudeVSCodePanel' }]), true);
  assert.strictEqual(fromMenuArgs([]), false);
});

test('an unrecognised caller is treated as the keyboard, not as the menu', () => {
  // Guessing "menu" would silently stop capturing and look like a broken key; guessing "keyboard"
  // captures, which is the recoverable mistake.
  assert.strictEqual(fromMenuArgs([undefined]), false);
  assert.strictEqual(fromMenuArgs(['something']), false);
  assert.strictEqual(fromMenuArgs([{ other: 1 }]), false);
});

test('the plan puts the door and the setting together', () => {
  assert.deepStrictEqual(triggerPlan([], 'keyboard'), { path: 'keyboard', send: true });
  assert.deepStrictEqual(triggerPlan([{ webview: 'x' }], 'keyboard'), { path: 'menu', send: false });
  assert.deepStrictEqual(triggerPlan([{ webview: 'x' }], 'always'), { path: 'menu', send: true });
  assert.deepStrictEqual(triggerPlan([], 'never'), { path: 'keyboard', send: false });
});
