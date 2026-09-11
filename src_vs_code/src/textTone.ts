import { escapeHtml } from './webviewHtml';

/**
 * The ± tone of the text, beside the ± size of it.
 *
 * <p>Asked for in these words: *"рядом с приближением добавь аналогичный контрол чтоб регулировать
 * яркость белого шрифта (белее — желтее — серее)"*. The white of the theme is the only white a page
 * has, and on a long answer at night it is too bright — but re-theming the editor to fix one panel
 * is not a trade anybody makes.</p>
 *
 * <p><b>One axis, and zero is the theme.</b> At `0` nothing is emitted at all, so somebody who never
 * touches the control sees exactly what they saw before it existed. Positive steps push the text
 * AWAY from the page's background; negative steps pull it towards a warm grey, which is the same
 * gesture as turning a lamp down: dimmer and warmer at once, "желтее" and "серее" being one
 * direction rather than two.</p>
 *
 * <p><b>Which way is away depends on the theme, and that is not a detail.</b> A fixed ramp towards
 * white erases the text on a light theme — the gate's plan round caught it before a line was
 * written. The host's own `vscode-light` class on the page body flips the target to black, so the
 * positive direction always means "more contrast" and the control means the same thing on both.</p>
 *
 * <p>Pure, like `zoomControl.ts` beside it, and for the same reason: a page module that imports
 * `node:` or `vscode` fails the bundle test, and a decision inside one is a decision no unit test
 * can reach. It does NOT borrow the zoom's clamp or label even though the range matches today —
 * two settings that happen to share a range are not one setting, and a future zoom range has no
 * business moving the tone.</p>
 */

export const TEXT_TONE_MIN = -5;
export const TEXT_TONE_MAX = 5;

/** How much of the target is mixed in per step. Five steps is 40%: a change, never a new colour. */
const MIX_PER_STEP = 8;

/**
 * The colour a NEGATIVE step pulls towards: warm, and darker than any theme's foreground.
 *
 * <p>One target does both halves of "желтее — серее", because a mix towards a warm grey warms and
 * dims at once. Two targets with a knee between them would be a second decision to explain and a
 * second place for the two directions to disagree.</p>
 */
const WARM = '#b9a88a';

/** The setting's value, made safe: clamped to the range, floored to an integer, 0 for junk. */
export function clampTone(value: unknown): number {
  const n = typeof value === 'number' && Number.isFinite(value) ? Math.trunc(value) : 0;

  return Math.min(TEXT_TONE_MAX, Math.max(TEXT_TONE_MIN, n));
}

/** `+3` / `−3`, and empty at the theme's own colour — the number is feedback, not decoration. */
export function toneLabel(offset: number): string {
  const clamped = clampTone(offset);
  if (clamped === 0) {
    return '';
  }

  return clamped > 0 ? `+${clamped}` : `−${-clamped}`;
}

/**
 * The colour for an offset, as a CSS value — or nothing at all at the theme's own colour.
 *
 * <p>`color-mix` resolves its `var()`s where it is USED, which is what lets the same string be put
 * in a stylesheet at render time and assigned to `body.style.color` later by a pushed message: the
 * theme variable and the direction variable are read at paint, not at build.</p>
 */
export function toneColour(offset: number, base = 'var(--vscode-foreground)'): string {
  const clamped = clampTone(offset);
  if (clamped === 0) {
    return '';
  }
  const mixed = Math.abs(clamped) * MIX_PER_STEP;
  const target = clamped > 0 ? 'var(--coai-tone-away)' : 'var(--coai-tone-warm)';

  return `color-mix(in srgb, ${base} ${100 - mixed}%, ${target} ${mixed}%)`;
}

/**
 * The two colours a page paints with, toned: its own chrome, and the text of the conversation.
 *
 * <p>TWO, because the transcript does not inherit the body's colour — `.msg .what` sets
 * `--vscode-editor-foreground` for itself, and a tone applied to `body` alone left the answers
 * exactly as they were. Which is the text somebody dimming their screen at night is reading.
 * (CodeRabbit, PR #206.)</p>
 */
export function toneColours(offset: number): { readonly text: string; readonly read: string } {
  return {
    text: toneColour(offset),
    read: toneColour(offset, 'var(--vscode-editor-foreground)'),
  };
}

/**
 * The CSS fragment, for the inside of the page's own `body {}` rule.
 *
 * <p>Empty at zero, deliberately: an untouched control must leave the rule exactly as it was. And
 * it belongs INSIDE that rule — a fragment above it silently drops the whole rule, which is the
 * trap `chatPage.ts` documents and a test parses for.</p>
 */
export function toneStyle(offset: number): string {
  const { text, read } = toneColours(offset);

  // Two custom properties and one `color`, rather than one `color`: the page's chrome takes it by
  // inheritance and the transcript takes it by name, because the transcript sets a colour of its own
  // and would otherwise ignore the tone entirely.
  return text.length === 0 ? '' : `--coai-text: ${text}; --coai-read: ${read}; color: var(--coai-text);`;
}

/** The header control: minus, the offset, plus. The zoom's shape, its own field. */
export function toneControlHtml(offset: number): string {
  return `<span class="toneCtl" title="Text tone: brighter, or dimmer and warmer (every ConnectOtherAIs page)">
    <button type="button" class="icon" data-tone="-1" aria-label="Dimmer, warmer text">−</button>
    <span id="toneOffset" class="zoomOffset">${escapeHtml(toneLabel(offset))}</span>
    <button type="button" class="icon" data-tone="1" aria-label="Brighter text">+</button>
  </span>`;
}

/**
 * The webview-side wiring, as a script fragment: post the press, apply pushed values live.
 *
 * <p>Its own `data-tone` attribute and its own `toneOffset` id — sharing the zoom's would wire one
 * control's buttons to the other's handler, and the two labels would chase each other.</p>
 */
export function toneScript(): string {
  return `
  for (const toneButton of document.querySelectorAll('button[data-tone]')) {
    toneButton.addEventListener('click', () => {
      vscode.postMessage({ type: 'tone', delta: Number(toneButton.dataset.tone), field: '' });
    });
  }
  window.addEventListener('message', (event) => {
    if (event.data?.type !== 'textTone') { return; }
    // BOTH, for the same reason the stylesheet writes both: the transcript names its own colour and
    // would keep it through every press otherwise.
    document.body.style.setProperty('--coai-text', event.data.color);
    document.body.style.setProperty('--coai-read', event.data.read);
    document.body.style.color = event.data.color.length > 0 ? 'var(--coai-text)' : '';
    const toneLabelNode = document.getElementById('toneOffset');
    if (toneLabelNode) { toneLabelNode.textContent = event.data.label; }
  });`;
}

/**
 * Shared look for the control, and the two direction targets.
 *
 * <p>`--coai-tone-away` is the one the theme decides: white where the background is dark, black
 * where it is light, so "brighter" never means "invisible". VS Code puts `vscode-light` on the body
 * of a webview under a light theme — the page is told which world it is in by the host itself.</p>
 */
export const TONE_CSS = `
  /* The defaults sit on the ROOT so a page's own body rule can override them: a declaration on the
     element beats one it merely inherits, whichever order the two rules are written in. */
  :root { --coai-text: var(--vscode-foreground); --coai-read: var(--vscode-editor-foreground); }
  body { --coai-tone-away: #ffffff; --coai-tone-warm: ${WARM}; }
  body.vscode-light { --coai-tone-away: #000000; }
  .toneCtl { display: inline-flex; align-items: center; gap: 2px; margin-left: 6px; }
  .toneCtl button { min-width: 24px; padding: 2px 6px; }`;
