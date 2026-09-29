import { TONE_CSS, toneControlHtml, toneScript, toneStyle } from './textTone';
import { ZOOM_CSS, zoomControlHtml, zoomScript, zoomStyle } from './zoomControl';

/**
 * The two text controls every ConnectOtherAIs page carries — the size and the tone — as ONE unit.
 *
 * <p><b>Why it exists.</b> The two controls shipped as four page pieces and two host pieces each, and every
 * page wired all twelve by hand. Three pages got both; three got the size only; five got neither, the
 * Settings tab among them (`todo/PLAN_every_page_reads_alike.md`). A page now takes this unit and cannot
 * take half of it. The pieces it composes are unchanged, so the three pages that were already complete
 * keep their own wiring and render the same controls.</p>
 */

/** The two settings a page is drawn in, for the builders that take positional arguments. */
export interface TextSettings {
  readonly size: number;
  readonly tone: number;
}

/** The theme's own size and colour — what a page is drawn in when nobody chose otherwise. */
export const PLAIN_TEXT: TextSettings = { size: 0, tone: 0 };

/** One press on either control: which one, and a single step in one direction. */
export interface TextControl {
  readonly kind: 'zoom' | 'tone';
  readonly delta: number;
}

/** The two controls, side by side, for a page's header. */
export function textControlsHtml(size: number, tone: number): string {
  return `${zoomControlHtml(size)}${toneControlHtml(tone)}`;
}

/**
 * The declarations a page roots its text in — its size and its tone.
 *
 * <p>They belong INSIDE the page's `body` rule. Loose at the top of a sheet, a browser reads them and the
 * rule after them as one invalid selector and drops both, which is how the Review roles page opened at
 * the theme's size with an unstyled control.</p>
 */
export function textControlsStyle(size: number, tone: number): string {
  return `${zoomStyle(size)} ${toneStyle(tone)}`.trim();
}

/** The look of both controls. */
export const TEXT_CONTROLS_CSS = `${ZOOM_CSS}\n${TONE_CSS}`;

/**
 * Both controls' wiring, as a script fragment.
 *
 * @param handle the name the page gave its `acquireVsCodeApi()` handle — `vscode` everywhere except the
 *   Notifications page, whose handle is `api`. Named rather than assumed: a fragment calling a name the
 *   page never declared throws on the first press.
 */
export function textControlsScript(handle = 'vscode'): string {
  const wiring = `${zoomScript()}\n${toneScript()}`;

  return handle === 'vscode' ? wiring : wiring.replaceAll('vscode.postMessage(', `${handle}.postMessage(`);
}

/**
 * A page's message, read as a press on one of the two controls — or `undefined` when it is not one.
 *
 * <p>The delta is CLAMPED to one step, not merely checked: the controls only ever send ±1, and a larger
 * number from a surface the host does not control would jump the setting to a bound in one message. It is
 * the rule the pages' own parsers already applied to the size (`rolesPage.ts`, `chatMessages.ts`), written
 * once here for both controls of the pages that take this unit.</p>
 */
export function textControlFrom(message: unknown): TextControl | undefined {
  const said = recordOf(message);
  const kind = controlKind(said['type']);
  const delta = oneStep(said['delta']);

  return kind === undefined || delta === undefined ? undefined : { kind, delta };
}

/** A command a page's own parser produced, narrowed to a press on one of the two controls. */
export function isTextControl<C extends { readonly kind: string }>(command: C): command is C & TextControl {
  return controlKind(command.kind) !== undefined;
}

/** The two values a page state carries, each absent meaning the theme's own. */
export function textOf(state: { readonly uiScale?: number; readonly textTone?: number }): TextSettings {
  return { size: state.uiScale ?? 0, tone: state.textTone ?? 0 };
}

function recordOf(message: unknown): Record<string, unknown> {
  return typeof message === 'object' && message !== null ? message as Record<string, unknown> : {};
}

function controlKind(type: unknown): TextControl['kind'] | undefined {
  return type === 'zoom' || type === 'tone' ? type : undefined;
}

/** One step in one direction; anything that is not a finite number is not a press. */
function oneStep(delta: unknown): number | undefined {
  return typeof delta === 'number' && Number.isFinite(delta) ? Math.max(-1, Math.min(1, Math.trunc(delta))) : undefined;
}
