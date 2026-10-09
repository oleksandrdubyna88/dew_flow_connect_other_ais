import type { PromptPreset } from './chatPresets';
import { escapeHtml } from './webviewHtml';

/**
 * One saved prompt preset as a block — the piece Chat draws.
 *
 * <p><b>Why this is not in `chatPresetsPage.ts` any more.</b> The Chat presets tab drew it until E5.1 deleted the tab, and so does Chat on the new
 * Settings page (`chatTabEmbed.ts`, research/PLAN_one_model_catalog.md E4.6b), which draws the tab's own block rather than a
 * copy, under attribute names of its own ({@link PresetAttrs}). Epic 5 deleted the tab (E5.1), so the block was moved
 * out first — prerequisite (b) of that epic — exactly as it was.</p>
 *
 * <p><b>Everything in it is text the person in front of it wrote</b> — their own name for their own prompt, which they
 * can perfectly well paste a `<script>` into to see what happens — so every value is escaped.</p>
 *
 * <p><b>The edges point one way.</b> The tab and the embed import this module, and it imports neither: `chatTabEmbed.ts`
 * imports `panelView.ts`, so nothing `panelView.ts` reaches may import it back, and this module is kept that way by
 * importing nothing that draws a page.</p>
 */

/**
 * The rows the prompt editor opens at. Twelve is about a screenful of a real instruction.
 *
 * <p>The box is large because that was asked for in as many words: a prompt is often a paragraph and sometimes
 * several, and a person editing one in a three-line box is reading it three lines at a time.</p>
 */
const PROMPT_ROWS = 14;

/**
 * The attribute names a prompt block carries. The tab's own are the defaults; the Settings page's Chat draws the
 * same block with names of its own (research/PLAN_one_model_catalog.md E4.6b), because that page also draws the roles,
 * whose wiring reads `data-field` and `data-remove` — the commands' arrangement (`commandsBlocks.CommandAttrs`).
 */
export interface PresetAttrs {
  readonly row: string;
  readonly list: string;
  readonly field: string;
  readonly remove: string;
}

const TAB_ATTRS: PresetAttrs = { row: 'data-id', list: 'data-list', field: 'data-field', remove: 'data-remove' };

/** One saved prompt: its name, the main tick, Remove, and its words in a large box. */
export function promptBlock(preset: PromptPreset, attrs: PresetAttrs = TAB_ATTRS): string {
  const id = escapeHtml(preset.id);

  return `<div class="preset" ${attrs.row}="${id}">
  <div class="head">
    <input type="text" ${attrs.list}="prompt" ${attrs.field}="name" value="${escapeHtml(preset.name)}" placeholder="A name for this prompt">
    <label class="main"><input type="checkbox" ${attrs.list}="prompt" ${attrs.field}="main"${preset.main ? ' checked' : ''}> main</label>
    <button type="button" class="remove" ${attrs.remove}="prompt" ${attrs.row}="${id}">Remove</button>
  </div>
  <textarea ${attrs.list}="prompt" ${attrs.field}="text" rows="${PROMPT_ROWS}" placeholder="What the captured passage travels with">${escapeHtml(preset.text)}</textarea>
</div>`;
}
