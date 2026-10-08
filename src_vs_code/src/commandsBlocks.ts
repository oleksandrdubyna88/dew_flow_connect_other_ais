import { COMMAND_MODELS_SINCE } from './commandModels';
import { fileIdOf, hasText, shippedTextOf, type CommandRow, type CommandStageName, type ShippedCommand } from './commands';
import { STAGE_NAMES } from './commandsMessages';
import { compareVersions } from './coaiInstall';
import { escapeHtml } from './webviewHtml';

/**
 * The pieces a commands page is drawn from: a command of yours, a shipped one, and the note for a server too old to
 * read either.
 *
 * <p><b>Why this is not in `commandsPage.ts` any more.</b> Two pages drew these until E5.1 deleted the first: the Gate commands tab, and Reviews ›
 * Commands on the new Settings page (`commandsEmbed.ts`, todo/PLAN_one_model_catalog.md E4.4), which draws the tab's own
 * blocks rather than a copy of them, under attribute names of its own ({@link CommandAttrs}). Epic 5 deleted the tab
 * (E5.1), so the blocks were moved out first — prerequisite (b) of that epic — exactly as they were.</p>
 *
 * <p><b>The edges point one way.</b> The tab and the embed import this module, and it imports neither — nor
 * `panelView.ts`, which the import-cycle ratchet (`importCycles.test.mjs`) would name as a ring.</p>
 */

/**
 * The note for a server too old to read any of this — the texts and the commands arrived in the same
 * release as the per-caller models (`COMMAND_MODELS_SINCE`).
 */
export function commandsSkewNote(serverVersion: string): string {
  return serverVersion.length === 0 || compareVersions(COMMAND_MODELS_SINCE, serverVersion) <= 0
    ? ''
    : `The installed coai-mcp is ${serverVersion}: it ignores these texts and commands and gives the shipped orders. `
      + `Update it to ${COMMAND_MODELS_SINCE} or later.`;
}

/**
 * The attributes a command block carries. The Gate commands tab's own, or — on the new Settings page, which also draws
 * the roles (todo/PLAN_one_model_catalog.md E4.4) — names of its own, so the roles' wiring never reads a command's
 * control.
 */
export interface CommandAttrs {
  readonly row: string;
  readonly field: string;
  readonly text: string;
  readonly remove: string;
  readonly restore: string;
  readonly file: string;
  readonly add: string;
}

export function customBlock(row: CommandRow, texts: Readonly<Record<string, string>>, a: CommandAttrs): string {
  const fileId = fileIdOf(row.id);

  return `<section class="command" ${a.row}="${escapeHtml(row.id)}">
<div class="row">
<input type="text" ${a.field}="title" aria-label="Title" value="${escapeHtml(row.title)}">
<select ${a.field}="stage" aria-label="Rounds">${STAGE_NAMES.map((stage) => stageOption(stage, row.stage)).join('')}</select>
<label><input type="checkbox" ${a.field}="enabled"${row.enabled ? ' checked' : ''}> On</label>
<button type="button" class="remove" ${a.remove}>Remove</button>
</div>
<textarea ${a.text}="${escapeHtml(fileId)}" aria-label="What it tells the AI" rows="3">${escapeHtml(texts[fileId] ?? '')}</textarea>
</section>`;
}

const STAGE_LABEL: Readonly<Record<CommandStageName, string>> = { any: 'Every round', plan: 'Plan rounds', code: 'Code rounds' };

function stageOption(stage: CommandStageName, chosen: CommandStageName): string {
  return `<option value="${stage}"${stage === chosen ? ' selected' : ''}>${STAGE_LABEL[stage]}</option>`;
}

export function shippedBlock(one: ShippedCommand, texts: Readonly<Record<string, string>>, a: CommandAttrs): string {
  // Only an override that SAYS something is one — a blank file is the shipped text, as the server reads it.
  const written = hasText(texts, one.id) ? texts[one.id] ?? '' : '';

  return `<section class="command" ${a.file}="${escapeHtml(one.id)}">
<h3>${escapeHtml(one.title)}${written.length > 0 ? ' <span class="badge">yours</span>' : ''}</h3>
${placeholderNote(one)}${markerLine(one.marker)}
<textarea ${a.text}="${escapeHtml(one.id)}" aria-label="${escapeHtml(one.title)}" rows="4" placeholder="${escapeHtml(shippedTextOf(one.id))}">${escapeHtml(written)}</textarea>
${restoreButton(one.id, written, a.restore)}
</section>`;
}

function markerLine(marker: string): string {
  return marker.length > 0 ? `<p class="marker"><b>${escapeHtml(marker)}</b>…</p>` : '';
}

function restoreButton(fileId: string, written: string, attr: string): string {
  return written.length > 0 ? `<button type="button" ${attr}="${escapeHtml(fileId)}">Restore the shipped text</button>` : '';
}

function placeholderNote(one: ShippedCommand): string {
  return one.placeholders.length === 0
    ? ''
    : `<p class="note">The server fills in ${one.placeholders.map((p) => `<code>${escapeHtml(p)}</code>`).join(', ')}.</p>`;
}
