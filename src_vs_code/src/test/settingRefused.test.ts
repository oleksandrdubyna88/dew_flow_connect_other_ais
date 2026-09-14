import assert from 'node:assert/strict';
import { test } from 'node:test';
import { declaresSetting, settingRefusal } from '../settingRefused';

/**
 * What a refused setting SAYS.
 *
 * <p>The defect these tests were written for was not a failed write — the write failed for a real
 * reason and was meant to. It was the sentence: the tab discarded VS Code's reason and asserted one
 * of its own, so the person read "your settings file may be read-only or held by another program"
 * about a settings file that was perfectly writable, and went looking at file permissions for a
 * problem whose cure was reloading the window.</p>
 *
 * <p>So every test here is about WORDS, and the strongest of them is negative: the invented cause
 * must not come back. A test that only checked the new sentence was present would still pass if the
 * old one were appended beside it.</p>
 */

/** VS Code's own words, copied from the Extension Host log of the window that reported this. */
const STALE_WINDOW = new Error(
  'Unable to write to User Settings because coai.phrases is not a registered configuration.',
);

test('a window that has not caught up with an update is told that, not that its file is read-only', () => {
  const refusal = settingRefusal('phrases', STALE_WINDOW, 'declared');

  assert.doesNotMatch(
    refusal.text,
    /read-only|held by another program|writable/u,
    'the refusal still blames the settings file — this is the defect: the file was fine, the window was stale',
  );
  assert.match(refusal.text, /updated/u, 'the refusal does not say the extension was updated');
  assert.match(
    refusal.text,
    /Reload Window/u,
    'the refusal does not name the one action that fixes it, so the person has to guess the command',
  );
  assert.equal(refusal.reloadCures, true, 'the caller is not told it can offer the reload');
  assert.match(
    refusal.text,
    /is not a registered configuration/u,
    "VS Code's own reason was discarded and replaced by a diagnosis, which is the habit this module exists to end",
  );
});

test('the cure is offered without pretending it is free', () => {
  // The banner promised "What you typed is still here" and the button under it reloaded the window,
  // which destroys the webview holding exactly that. Three reviewers, two vendors, one round.
  const refusal = settingRefusal('phrases', STALE_WINDOW, 'declared');

  assert.doesNotMatch(
    refusal.text,
    /the change will save/u,
    'it says reloading saves the change: reloading cures the REFUSAL, it does not replay the write',
  );
  assert.match(refusal.text, /copy anything you have typed/u, 'it does not warn that the tab closes with the window');
  assert.match(refusal.text, /make the change again/u, 'it does not say the edit has to be redone afterwards');
});

test('the setting that would not save is named, so a person knows what they have lost', () => {
  assert.match(settingRefusal('phrases', STALE_WINDOW, 'declared').text, /coai\.phrases/u);
  assert.match(settingRefusal('consultants', STALE_WINDOW, 'declared').text, /coai\.consultants/u);
});

test('the target and the key can change and it is still recognised as a stale window', () => {
  // Matched on the product's own phrasing rather than the whole sentence. A workspace-scoped write
  // and a key added next year both read the same to a person, and both have the same cure.
  const refusal = settingRefusal('roles', new Error(
    'Unable to write to Workspace Settings because coai.roles is not a registered configuration.',
  ), 'declared');

  assert.equal(refusal.reloadCures, true);
});

test("any other refusal keeps VS Code's own reason, word for word", () => {
  const refusal = settingRefusal('phrases', new Error('EPERM: operation not permitted, open settings.json'), 'declared');

  assert.match(
    refusal.text,
    /EPERM: operation not permitted, open settings\.json/u,
    'the real reason was replaced by a summary, which is how the original defect started',
  );
  assert.equal(refusal.reloadCures, false, 'reloading does not cure a permission error, and offering it would mislead');
});

test('a thrower that is not an Error still produces a sentence rather than nothing', () => {
  // `config.update` rejects with whatever the workbench threw, and this path has no control over
  // what that is. Saying "[object Object]" is poor; saying nothing at all is worse, because the
  // banner would then be empty and the person would think the save had worked.
  const refusal = settingRefusal('phrases', 'the window went away', 'declared');

  assert.match(refusal.text, /the window went away/u);
  assert.equal(refusal.reloadCures, false);
});

/**
 * The discriminator — the same VS Code sentence, two faults, one cure between them.
 *
 * <p>Asked for by the code gate (codex) against this change's plan: "not a registered configuration"
 * is also what a build that FAILED TO DECLARE the key produces, and this product has shipped that
 * exact fault once already. Telling somebody to reload then sends them round a loop with no exit.</p>
 */

/** The manifest shape VS Code actually hands back, trimmed to what is read. */
const manifest = (...keys: string[]): unknown => ({
  contributes: { configuration: { title: 'ConnectOtherAIs', properties: Object.fromEntries(keys.map((k) => [k, {}])) } },
});

test('a key this build DOES declare means the window is behind, and reloading is the cure', () => {
  assert.equal(declaresSetting(manifest('coai.phrases', 'coai.roles'), 'phrases'), 'declared');
  assert.equal(settingRefusal('phrases', STALE_WINDOW, 'declared').reloadCures, true);
});

test('a key this build does NOT declare is a fault in the extension, and reloading is never offered', () => {
  assert.equal(declaresSetting(manifest('coai.roles'), 'phrases'), 'absent');

  const refusal = settingRefusal('phrases', STALE_WINDOW, 'absent');

  assert.equal(refusal.reloadCures, false, 'a reload cannot register a key the build never shipped');
  assert.doesNotMatch(refusal.text, /Reload Window/u, 'it still tells them to reload, which will not help');
  assert.match(refusal.text, /does not declare it/u, 'it does not say which of the two faults this is');
  assert.match(refusal.text, /is not a registered configuration/u, "VS Code's own words are still passed on");
});

test('a manifest that cannot be read offers no cure and invents no cause', () => {
  // Neither diagnosis is available, so neither is asserted. This is the branch that must never
  // "pick the likelier one" — that is precisely the habit the whole change exists to end.
  for (const unreadable of [undefined, null, 'nonsense', 42, {}, { contributes: {} }, { contributes: { configuration: 7 } }]) {
    assert.equal(declaresSetting(unreadable, 'phrases'), 'unknown', `read too much into ${JSON.stringify(unreadable) ?? 'undefined'}`);
  }

  const refusal = settingRefusal('phrases', STALE_WINDOW, 'unknown');

  assert.equal(refusal.reloadCures, false);
  assert.doesNotMatch(refusal.text, /Reload Window|updated while this window was open|does not declare/u);
  assert.match(refusal.text, /is not a registered configuration/u);
});

test('configuration may be an ARRAY of sections, which the manifest format allows', () => {
  const split = { contributes: { configuration: [
    { title: 'One', properties: { 'coai.roles': {} } },
    { title: 'Two', properties: { 'coai.phrases': {} } },
  ] } };

  assert.equal(declaresSetting(split, 'phrases'), 'declared');
  assert.equal(declaresSetting(split, 'nothingLikeThis'), 'absent');
});

test('a key named like something every object has is not declared by accident', () => {
  // `Object.hasOwn`, not `in`: `properties['toString']` is truthy on any object, and answering
  // "declared" there would offer a reload for a setting that does not exist.
  assert.equal(declaresSetting(manifest('coai.phrases'), 'toString'), 'absent');
  assert.equal(declaresSetting(manifest('coai.phrases'), 'constructor'), 'absent');
});
