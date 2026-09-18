import assert from 'node:assert/strict';
import { test } from 'node:test';
import { pictureDir } from '../pictureStore';

/**
 * One directory per conversation — which it was not, for every conversation at once.
 *
 * <p><b>The defect this exists for.</b> The caller was `pictureDir(entry.id.toString())`.
 * `ChatEntry.id` is typed `object` — the opaque identity a `WeakMap` is keyed on — so `toString()`
 * gave `"[object Object]"` every time, sanitising to `objectObject`. Every conversation's pictures
 * went into ONE directory, and the file inside is named from the image type and the turn number, so
 * two conversations attaching a picture on the same turn wrote the same path and one replaced the
 * other's silently.</p>
 *
 * <p>SonarCloud reported it as a latent risk — <i>"stringifies as `[object Object]` if anything ever
 * puts it in a template"</i> — and the plan carried it that way. It was not latent. Reading the type
 * of `ChatEntry.id` is what turned a cosmetic finding into a real one, which is worth more than the
 * fix.</p>
 *
 * <p>The parameter is the conversation rather than a string precisely so the call that caused this
 * no longer compiles.</p>
 */

const ROOT = '/data';

test('two conversations get two directories', () => {
  // The whole defect, at its narrowest. This passed before the fix as well — the caller was what was
  // broken — which is why the parameter type changed and not only the body.
  const one = pictureDir(ROOT, { saveId: '11111111-2222-3333-4444-555555555555' });
  const two = pictureDir(ROOT, { saveId: '99999999-8888-7777-6666-555555555555' });

  assert.notEqual(one, two, 'two conversations share one picture directory, so their images collide');
});

test('the id becomes a path segment and nothing else', () => {
  // It is spliced into a path, so anything that could leave the directory has to go. `..` reduced to
  // nothing would be a traversal; here it simply is not a usable id.
  const dir = pictureDir(ROOT, { saveId: 'ab/../cd' });

  assert.ok(!dir.includes('..'), `a conversation id walked out of the pictures folder: ${dir}`);
  assert.ok(dir.endsWith('abcd'), dir);
});

test('an id with nothing usable in it is refused rather than guessed', () => {
  // Refusing is what stops the shared directory coming back by another road: an id that sanitises to
  // nothing would otherwise land every window's pictures in `pictures/` itself.
  assert.throws(() => pictureDir(ROOT, { saveId: '///' }), /no usable id/u,
    'a conversation with no usable id was given a directory anyway');
});

test('the conversation is what is asked for, not a string', () => {
  // The type is the fix. `pictureDir(entry.id.toString())` was the call that caused this, and the
  // reason it was possible is that the parameter took any string at all. This case exists so that a
  // future change back to a string has to delete a test that says why it must not.
  const conversation: { readonly saveId: string } = { saveId: 'abc-123' };

  assert.ok(pictureDir(ROOT, conversation).endsWith('abc-123'),
    'the directory is not named after the conversation it belongs to');
});
