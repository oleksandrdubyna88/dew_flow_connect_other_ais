import type { ChatProviderList } from './chatModels';
import type { ModelPreset } from './chatPresets';
import { promptBlock, type PresetAttrs } from './chatPresetBlocks';
import type { CopiedField } from './chatPresetMove';
import type { ConflictField, PresetConflict } from './chatPresetRevision';
import type { ChatSettings } from './chatSettings';
import { escapeHtml } from './escapeHtml';
import { DEFAULT_CHAT, chatProviderListFor, chatSendingFields, type PanelState } from './panelView';

/**
 * Chat on the new Settings page (todo/PLAN_one_model_catalog.md E4.6b): which model a chat opens on, what it is sent
 * with and how, and the prompt presets inline. The models are the rows ticked Chat on Models that can answer; their
 * edits — the opening model, a model's opening text, a prompt — are the Chat presets tab's own messages, posted as
 * `chatPresets` into the one editing core (`chatPresetsHost.ts`). The sending fields are the current page's own
 * (`chatSendingFields`), written through `data-setting`.
 *
 * <p>The page also draws the roles, whose wiring reads `data-field` and `data-remove`, and the commands; so a block here
 * carries names of its own ({@link EMBEDDED}), and each wiring reads only its own.</p>
 */

const EMBEDDED: PresetAttrs = { row: 'data-chp-id', list: 'data-chp-list', field: 'data-chp-field', remove: 'data-chp-remove' };

/** Chat, as the new page draws it. */
export function chatTabHtml(state: PanelState): string {
  const chat = state.chat ?? DEFAULT_CHAT;
  const list = chatProviderListFor(chat, state);

  return `<div class="chat-embed">
<p class="lead">Ask another AI about a passage without leaving the editor. Which models can answer is ticked <b>Chat</b> on Models; here is which one a chat opens on, what it is sent with, and how.</p>
<h3>Which model a chat opens on</h3>
${opensOnHtml(chat, list)}${state.perSide ? '\n<p class="hint">Saved for this side of the machine.</p>' : ''}
<h3>Sending</h3>
${chatSendingFields(chat, '')}
<h3>Prompt presets</h3>
<p class="note">Everything here is saved as you type. The one marked <b>main</b> is sent when a capture sends by itself.</p>
${chat.prompts.map((one) => promptBlock(one, EMBEDDED)).join('\n')}
<button type="button" data-chp-add="prompt">Add a prompt</button>
</div>`;
}

/**
 * The models that can answer, a stranded choice, a preset an older build edited after the move, what answers when nothing
 * is chosen, and the ones that cannot.
 */
function opensOnHtml(chat: ChatSettings, list: ChatProviderList): string {
  const pool = chat.models.filter((one) => list.providers.some((provider) => provider.id === one.id));
  const chosen = chosenOf(chat);

  return [
    strandedHtml(chat, chosen, pool),
    ...chat.conflicts.map(conflictBlock),
    ...pool.map((one) => modelBlock(one, chosen)),
    unchosenHint(chosen, pool),
    refusalsHtml(list),
  ].filter((part) => part.length > 0).join('\n');
}

/** What a chat opens on: `coai.chatModel`, else the model ticked main, else nothing. */
function chosenOf(chat: ChatSettings): string {
  return chat.model.length > 0 ? chat.model : (chat.models.find((one) => one.main)?.id ?? '');
}

/** One model that can answer: its radio, and what the composer opens with when it is chosen. */
function modelBlock(one: ModelPreset, chosen: string): string {
  const id = escapeHtml(one.id);

  return `<div class="block" data-chp-id="${id}">
  <label class="inline"><input type="radio" name="chat-opens-on" value="${id}" data-chp-main${one.id === chosen ? ' checked' : ''}> <b>${escapeHtml(one.name)}</b> <span class="hint">${escapeHtml(one.runtime)}${one.model.length > 0 ? ` · ${escapeHtml(one.model)}` : ''}</span></label>
  <label class="field"><span>Opens with</span>
  <textarea rows="2" data-chp-list="model" data-chp-field="startingPrompt" placeholder="What the composer opens with when this model is chosen (optional)">${escapeHtml(one.startingPrompt ?? '')}</textarea></label>
</div>`;
}

/**
 * A saved choice that no longer resolves stays on screen, chosen and disabled: what is configured is what is shown, and
 * the chat refuses it by name rather than sending the passage to somebody else's model (the current page's rule).
 */
function strandedHtml(chat: ChatSettings, chosen: string, pool: readonly ModelPreset[]): string {
  return chosen.length === 0 || pool.some((one) => one.id === chosen) ? '' : strandedBlock(chosen, chat.models.find((one) => one.id === chosen));
}

/**
 * @param chosen the saved choice
 * @param known the chat model it names, when there is one — then it is one the chat cannot speak to
 */
function strandedBlock(chosen: string, known: ModelPreset | undefined): string {
  const name = escapeHtml(known?.name ?? chosen);
  const why = known === undefined ? 'is no longer ticked Chat on Models, or was removed' : 'cannot answer a chat';

  return `<div class="block stranded">
  <label class="inline"><input type="radio" name="chat-opens-on" value="${escapeHtml(chosen)}" data-chp-main checked disabled> <b>${name}</b> <span class="hint">— ${why}</span></label>
  <p class="callout warn">A chat is set to open on ${name}, which ${why}. Until you pick another model here a chat is refused by that name — it is never quietly sent to a different one.</p>
</div>`;
}

/** What each field the move copies is called here — the labels Models uses for the same row fields. */
const FIELD_LABELS: Readonly<Record<CopiedField, string>> = {
  name: 'Name', runtime: 'Runtime', model: 'Model', baseUrl: 'Endpoint', executablePath: 'CLI path', chatStartingPrompt: 'Opens with',
  vaultKeyName: 'Vault key name', teamServerId: 'Team server', remoteVendor: 'Vendor on the Team server',
};

/**
 * A chat preset an older build edited after it moved (todo/PLAN_one_model_catalog.md, epic 5 prerequisite (a), R7): the
 * row as it is, the edited values beside it, and the two choices. Beside the stranded pick because it is the same kind of
 * thing — what is configured differs from what the chat runs, and only the person can say which is right. Nothing is
 * changed until they choose; the row keeps working meanwhile. Each choice is the presets' own message (`revision`), into
 * the one editing core (`chatPresetsHost.ts`), which writes the answer to the record — so it holds across a reload.
 */
function conflictBlock(conflict: PresetConflict): string {
  const name = escapeHtml(conflict.rowName);

  return `<div class="block conflict" data-chp-conflict="${escapeHtml(conflict.presetId)}">
  <p class="callout warn">The chat preset that became <b>${name}</b> was edited in an older version of ConnectOtherAIs after it moved to Models. A chat still runs ${name} as it is on Models until you choose.</p>
  <table class="map"><thead><tr><th scope="col">Field</th><th scope="col">On Models now</th><th scope="col">The edited values</th></tr></thead>
  <tbody>${conflict.fields.map(conflictRow).join('')}</tbody></table>
  <button type="button" data-chp-revision="use">Use the edited values</button> <button type="button" data-chp-revision="keep">Keep the row</button>
</div>`;
}

function conflictRow(one: ConflictField): string {
  return `<tr><td>${FIELD_LABELS[one.field]}</td><td>${conflictValue(one.row)}</td><td>${conflictValue(one.edited)}</td></tr>`;
}

function conflictValue(value: string): string {
  return value.length > 0 ? `<code>${escapeHtml(value)}</code>` : '<i>none</i>';
}

function unchosenHint(chosen: string, pool: readonly ModelPreset[]): string {
  if (chosen.length > 0) {
    return '';
  }
  const first = pool[0];

  return first === undefined
    ? '<p class="hint">No model that can answer is ticked Chat on Models — chat is off.</p>'
    : `<p class="hint">Nothing is chosen, so a chat opens on the first that can answer — ${escapeHtml(first.name)}.</p>`;
}

/** Every model the chat cannot speak to, with the reason — named, never quietly missing. */
function refusalsHtml(list: ChatProviderList): string {
  return list.refused.length === 0 ? '' : `<ul class="tight">${list.refused.map((one) => `<li>${escapeHtml(one.reason)}</li>`).join('')}</ul>`;
}

/**
 * The chat's wiring in the panel's document — block-scoped, on the panel's own `vscode` and `send`. A pick, a tick and a
 * press are numbered (the busy mark); typing posts plainly and settles in the host. Focus is reported, so a repaint never
 * lands under the caret while somebody types.
 */
export function chatTabEmbeddedScript(): string {
  return `
  {${chatFieldsScript()}${chatPressesScript()}
  }`;
}

/** The fields: a typed box, a main tick and the opening model, each posted as the presets tab's own edit. */
function chatFieldsScript(): string {
  return `
    const chpRowOf = (el) => { const row = el.closest('[data-chp-id]'); return row ? row.dataset.chpId : ''; };
    const chpPost = (edit, el, numbered) => (numbered ? send({ type: 'chatPresets', edit: edit }, el) : vscode.postMessage({ type: 'chatPresets', edit: edit }));
    const chpOwn = (t) => !!(t && t.dataset && typeof t.closest === 'function' && (t.dataset.chpField !== undefined || t.dataset.chpMain !== undefined));
    const chpEdit = (t, value) => ({ type: 'edit', list: t.dataset.chpList, id: chpRowOf(t), field: t.dataset.chpField, value: value });
    document.addEventListener('input', (event) => {
      const t = event.target;
      if (!chpOwn(t) || t.type === 'checkbox' || t.type === 'radio') { return; }
      chpPost(chpEdit(t, t.value), t, false);
    });
    document.addEventListener('change', (event) => {
      const t = event.target;
      if (!chpOwn(t)) { return; }
      if (t.dataset.chpMain !== undefined) {
        if (t.checked) { chpPost({ type: 'edit', list: 'model', id: t.value, field: 'main', value: true }, t, true); }
        return;
      }
      if (t.type === 'checkbox') { chpPost(chpEdit(t, t.checked), t, true); }
    });
    const chpFocus = (t, editing) => vscode.postMessage({ type: 'focus', id: 'chatPresets|' + chpRowOf(t) + '|' + (t.dataset.chpField || 'main'), editing: editing,
      start: typeof t.selectionStart === 'number' ? t.selectionStart : 0, end: typeof t.selectionEnd === 'number' ? t.selectionEnd : 0 });
    document.addEventListener('focusin', (event) => { if (chpOwn(event.target)) { chpFocus(event.target, true); } });
    document.addEventListener('focusout', (event) => {
      // Moving from one field to the next is not a moment to rebuild the page.
      if (chpOwn(event.target) && !chpOwn(event.relatedTarget)) { chpFocus(event.target, false); }
    });`;
}

/**
 * The buttons: Add a prompt, Remove, and the two answers to an edited preset (R7) — each posted numbered, so it carries
 * the busy mark. An answer names its preset from the conflict's block, never from a model's row, so it cannot be read as
 * that row's own action; and it disables BOTH answers of that block while it is in flight (R7's code round, finding 9) —
 * a second press could otherwise send the other answer before the first had landed. The redraw every choice ends with
 * (`applyRevisionChoice`) draws the block again, or not at all.
 */
function chatPressesScript(): string {
  return `
    document.addEventListener('click', (event) => {
      const t = event.target;
      if (!t || typeof t.closest !== 'function') { return; }
      const revision = t.closest('[data-chp-revision]');
      if (revision) {
        const conflict = revision.closest('[data-chp-conflict]');
        chpPost({ type: 'revision', id: conflict ? conflict.dataset.chpConflict : '', choice: revision.dataset.chpRevision }, revision, true);
        const answers = conflict ? Array.prototype.slice.call(conflict.children) : [revision];
        answers.forEach((one) => { if (one.dataset && one.dataset.chpRevision !== undefined) { one.disabled = true; } });
        return;
      }
      const add = t.closest('[data-chp-add]');
      if (add) { chpPost({ type: 'add', list: add.dataset.chpAdd }, add, true); return; }
      const remove = t.closest('[data-chp-remove]');
      if (remove) { chpPost({ type: 'remove', list: remove.dataset.chpRemove, id: chpRowOf(remove) }, remove, true); }
    });`;
}
