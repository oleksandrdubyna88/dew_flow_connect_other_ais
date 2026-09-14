import assert from 'node:assert/strict';
import { test } from 'node:test';
import { settingRefusal } from '../settingRefused';

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
  const refusal = settingRefusal('phrases', STALE_WINDOW);

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
});

test('the setting that would not save is named, so a person knows what they have lost', () => {
  assert.match(settingRefusal('phrases', STALE_WINDOW).text, /coai\.phrases/u);
  assert.match(settingRefusal('consultants', STALE_WINDOW).text, /coai\.consultants/u);
});

test('the target and the key can change and it is still recognised as a stale window', () => {
  // Matched on the product's own phrasing rather than the whole sentence. A workspace-scoped write
  // and a key added next year both read the same to a person, and both have the same cure.
  const refusal = settingRefusal('roles', new Error(
    'Unable to write to Workspace Settings because coai.roles is not a registered configuration.',
  ));

  assert.equal(refusal.reloadCures, true);
});

test("any other refusal keeps VS Code's own reason, word for word", () => {
  const refusal = settingRefusal('phrases', new Error('EPERM: operation not permitted, open settings.json'));

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
  const refusal = settingRefusal('phrases', 'the window went away');

  assert.match(refusal.text, /the window went away/u);
  assert.equal(refusal.reloadCures, false);
});
