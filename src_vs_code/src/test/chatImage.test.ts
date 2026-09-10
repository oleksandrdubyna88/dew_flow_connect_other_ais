import assert from 'node:assert/strict';
import { test } from 'node:test';
import { IMAGE_TYPES, imageFileName, imageRefusal, imageTurn, pastedImage } from '../chatImage';

/**
 * A picture in the question.
 *
 * <p>Phase 0 measured the mechanism rather than assuming it: `claude` and `agy` both read a number
 * out of a real PNG when the file's PATH was named in the prompt, which means the vendor process
 * opens the file itself — so the temp file's name, location and lifetime are part of the contract
 * and not an implementation detail. Everything here is that contract, decided without a host.</p>
 */

test('an image arrives as a data URL, and anything else is not an image', () => {
  const png = pastedImage('data:image/png;base64,iVBORw0KGgo=');
  assert.strictEqual(png?.type, 'image/png');
  assert.strictEqual(png?.base64, 'iVBORw0KGgo=');

  for (const bad of [
    '',
    'data:text/html;base64,PHNjcmlwdD4=',
    'data:image/svg+xml;base64,PHN2Zz4=',
    'https://example.com/a.png',
    'data:image/png,notbase64',
    'data:image/png;base64,',
    `data:image/png;base64,${'A'.repeat(20_000_001)}`,
  ]) {
    assert.strictEqual(pastedImage(bad), undefined, `${bad.slice(0, 40)} was accepted as an image`);
  }
});

test('svg is refused although it is an image, because it is a document', () => {
  // An SVG is markup with script in it, and the one thing this feature does is hand a file to a
  // process that will open it. The four types accepted are the ones a screenshot actually is.
  assert.ok(!IMAGE_TYPES.includes('image/svg+xml'));
  assert.deepStrictEqual([...IMAGE_TYPES].sort(), ['image/gif', 'image/jpeg', 'image/png', 'image/webp']);
});

test('the file is named by what it is, never by anything a page said', () => {
  // The page sends bytes and a type; the NAME is made here. A name that came from the page would be
  // a path fragment chosen by whatever wrote into the clipboard.
  assert.match(imageFileName('image/png', 1), /^coai-1\.png$/);
  assert.match(imageFileName('image/jpeg', 2), /^coai-2\.jpg$/);
  assert.match(imageFileName('image/webp', 3), /^coai-3\.webp$/);
  assert.match(imageFileName('image/gif', 4), /^coai-4\.gif$/);
});

test('a provider that cannot take a picture is refused BY NAME', () => {
  // The worst outcome is a picture that silently does not arrive: a person pastes a screenshot, asks
  // about it, and is answered about the text alone with nothing saying the image was dropped.
  assert.strictEqual(imageRefusal('claude'), '');
  assert.strictEqual(imageRefusal('antigravity'), '');
  assert.match(imageRefusal('codex'), /codex/, 'the refusal does not name what refused');
  assert.match(imageRefusal('remsoftdev-codex'), /team server|remsoftdev-codex/i);
});

test('the turn names the file where the model will look for it', () => {
  const turn = imageTurn('What is this?', 'C:\\Users\\x\\AppData\\Local\\Temp\\coai-1.png');

  assert.match(turn, /What is this\?/, 'the question was lost');
  assert.match(turn, /coai-1\.png/, 'the model is not told where the image is');
  // The path is LAST, for the same reason the passage is: whatever must survive a long turn goes at
  // the end, and here that is the instruction to look at the file.
  assert.ok(turn.indexOf('coai-1.png') > turn.indexOf('What is this?'));
});

test('a turn with no question still says what to do with the picture', () => {
  // Pasting an image and pressing Enter is a whole question in itself.
  const turn = imageTurn('', '/tmp/coai-1.png');

  assert.ok(turn.trim().length > 0, 'an image with no words became an empty turn');
  assert.match(turn, /coai-1\.png/);
});
