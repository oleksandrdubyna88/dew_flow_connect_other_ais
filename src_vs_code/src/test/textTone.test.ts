import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import {
  TEXT_TONE_MAX,
  TEXT_TONE_MIN,
  TONE_CSS,
  clampTone,
  toneColour,
  toneControlHtml,
  toneLabel,
  toneScript,
  toneStyle,
} from '../textTone';
import { chatCommandOf } from '../chatMessages';

/**
 * The ± tone of the text, beside the ± size of it.
 *
 * <p>Asked for as *"регулировать яркость белого шрифта (белее — желтее — серее)"*. What is asserted
 * here is that zero changes nothing, that the direction is decided by the THEME rather than fixed
 * towards white, and that the two steppers cannot reach each other's buttons.</p>
 */

test('zero is the theme, and it emits nothing at all', () => {
  // Somebody who never touches this control must see exactly the page they saw before it existed —
  // which is a stronger promise than "the same colour": no declaration, nothing to cascade over.
  assert.strictEqual(toneColour(0), '');
  assert.strictEqual(toneStyle(0), '');
  assert.strictEqual(toneLabel(0), '');
});

test('a step is one step, and the ends hold', () => {
  assert.strictEqual(clampTone(3), 3);
  assert.strictEqual(clampTone(TEXT_TONE_MAX + 9), TEXT_TONE_MAX);
  assert.strictEqual(clampTone(TEXT_TONE_MIN - 9), TEXT_TONE_MIN);
  // Junk from a hand-edited settings file is the theme's colour, never an exception.
  for (const junk of [undefined, null, 'lots', Number.NaN, Number.POSITIVE_INFINITY, {}]) {
    assert.strictEqual(clampTone(junk), 0, `${String(junk)} moved the tone`);
  }
});

test('up is away from the background and down is warm, and each step mixes the same amount', () => {
  assert.match(toneColour(1), /var\(--coai-tone-away\) 8%/, 'a step up does not move towards contrast');
  assert.match(toneColour(-1), /var\(--coai-tone-warm\) 8%/, 'a step down does not move towards warmth');
  assert.match(toneColour(5), /var\(--vscode-foreground\) 60%/, 'five steps is not five steps');
  assert.match(toneColour(-5), /var\(--coai-tone-warm\) 40%/, 'five steps down is not five steps');
});

test('WHICH WAY is away is the theme\'s to say, not this file\'s', () => {
  // A fixed ramp towards white erases the text on a light theme — the gate said so on the plan
  // round, before a line of this was written. VS Code puts `vscode-light` on the body of a webview
  // under a light theme, which is how the page is told which world it is in.
  assert.match(TONE_CSS, /body \{[^}]*--coai-tone-away: #ffffff/, 'the dark default is not white');
  assert.match(TONE_CSS, /body\.vscode-light \{[^}]*--coai-tone-away: #000000/, 'a light theme still ramps to white');
});

test('the two steppers cannot reach each other', () => {
  const tone = toneControlHtml(2);

  assert.match(tone, /data-tone="-1"/, 'the tone control has no down button of its own');
  assert.match(tone, /id="toneOffset"/, 'the tone label shares the zoom label id');
  assert.doesNotMatch(tone, /data-zoom/, 'the tone control posts zoom presses');
  assert.match(tone, /\+2/, 'the offset is not shown beside the buttons');
  assert.match(toneScript(), /button\[data-tone\]/, 'the script wires the zoom buttons');
  assert.doesNotMatch(toneScript(), /zoomOffset/, 'the tone script writes the zoom label');
});

test('a label is escaped like every other thing a page prints', () => {
  // It is a number today. It is printed through the same escape as everything else, so it is still
  // a number the day somebody makes the label say something a person typed.
  assert.doesNotMatch(toneControlHtml(1), /<[^>]*<|&(?!amp;|lt;|gt;|quot;|#)/, 'the control emits unescaped markup');
});

test('a press crosses the seam as a DIRECTION, and anything else as nothing', () => {
  assert.deepStrictEqual(chatCommandOf({ type: 'tone', delta: 1 }), { kind: 'tone', delta: 1 });
  assert.deepStrictEqual(chatCommandOf({ type: 'tone', delta: -1 }), { kind: 'tone', delta: -1 });
  // The control only ever sends ±1, so a bigger number is not a press — it is a jump to a bound
  // from a surface the host does not control, which is the rule the zoom already keeps.
  assert.deepStrictEqual(chatCommandOf({ type: 'tone', delta: 99 }), { kind: 'tone', delta: 1 });
  // Built as the WIRE shape rather than cast through it: `chatCommandOf` takes a message whose
  // fields are already `unknown`, so a junk delta needs no cast at all — and `as never` is
  // forbidden here. (codex, the code round.)
  for (const junk of ['lots', Number.NaN, undefined]) {
    assert.deepStrictEqual(chatCommandOf({ type: 'tone', delta: junk }), { kind: 'tone', delta: 0 });
  }
});

/** The source of a file in `src`, for the wiring this cannot reach without a host. */
function source(file: string): string {
  return readFileSync(join(__dirname, '..', '..', 'src', file), 'utf8');
}

test('both pages that carry the zoom are pushed the tone, and both let it go', () => {
  // There is no host in a unit test, so the fan-out is asserted where it is written. A panel that
  // pushes and never disposes leaks a configuration listener per tab; one that disposes and never
  // pushes opens at the theme colour however the setting stands.
  for (const file of ['chatPanel.ts', 'helpPanel.ts']) {
    const text = source(file);

    assert.match(text, /pushTextToneTo\(panel\.webview\)/, `${file} never pushes the tone`);
    assert.match(text, /tone(Hook)?\.dispose\(\)/, `${file} never releases its tone listener`);
    assert.match(text, /applyToneDelta/, `${file} takes no tone press`);
  }
});
