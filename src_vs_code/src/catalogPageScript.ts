import { rolesEmbeddedScript } from './rolesEmbed';
import { commandsEmbeddedScript } from './commandsEmbed';
import { chatTabEmbeddedScript } from './chatTabEmbed';
import { securityLaneScript } from './securityLaneScript';
import { tabKeysScript } from './tabKeys';
import { textControlsScript } from './textControls';
import { jsonForScript } from './webviewHtml';

/**
 * The Settings page's own script (research/PLAN_one_model_catalog.md, E3.1), appended to the shared page script that
 * `pageDocument` already runs — the settings writes, the commands, focus restore, the busy marks.
 *
 * <p><b>Two levels of tabs, one place.</b> A top tab's key is its id (`reviews`); a sub-tab's key is the whole place
 * (`reviews/gate`), so one click handler tells the levels apart and the host holds ONE string. Pressing a top tab
 * opens the sub-tab last open under it in this webview (`getState`, which a repaint's new document is handed), else
 * its first. The host's `showTab` selects without posting back, as on the old page.</p>
 *
 * <p><b>The one confirm.</b> A button that asks first carries `data-asks` (never `data-command`, which the shared
 * wiring sends on the first click); the dialog is filled from its attributes, and only the action button sends the
 * command — through the shared `send`, so it is numbered and marked busy like every other press. Keep it, Escape or a
 * click outside sends nothing.</p>
 *
 * @param place the place the host holds — a script literal, never the markup, which is the paint key (D6)
 */
export function catalogScript(place: string): string {
  return `
  const heldPlace = ${jsonForScript(place)};
${placesScript()}
${confirmScript()}
${modelsScript()}
${tabKeysScript()}
${textControlsScript()}
${securityLaneScript()}
${rolesEmbeddedScript()}
${commandsEmbeddedScript()}
${chatTabEmbeddedScript()}
  window.addEventListener('message', (event) => {
    if (event.data && event.data.type === 'uiScale') { document.documentElement.style.fontSize = event.data.px + 'px'; }
  });`;
}

/** The two levels of tabs: which place is shown, remembered per top tab, and told to the host. */
function placesScript(): string {
  return `
  const catalogSaved = vscode.getState() || {};
  const openSubs = Object.assign({}, catalogSaved.subs || {});
  function catalogAll(selector) { return Array.prototype.slice.call(document.querySelectorAll(selector)); }
  function topOf(place) { return String(place).split('/')[0]; }
  function markTab(tab, on) {
    tab.className = on ? 'tab on' : 'tab';
    tab.setAttribute('aria-selected', on ? 'true' : 'false');
    tab.setAttribute('tabindex', on ? '0' : '-1');
  }
  // The place a top tab opens on: the sub-tab last open under it, else its first, else the tab itself.
  function placeUnder(top) {
    const subs = catalogAll('[data-tab]').map((tab) => tab.dataset.tab).filter((key) => topOf(key) === top && key !== top);
    return subs.indexOf(openSubs[top]) >= 0 ? openSubs[top] : (subs[0] || top);
  }
  function isShown(key, place) {
    return key.indexOf('/') < 0 ? key === topOf(place) : key === (topOf(key) === topOf(place) ? place : openSubs[topOf(key)]);
  }
  function showPlace(asked, tell) {
    const tabs = catalogAll('[data-tab]');
    const known = tabs.some((tab) => tab.dataset.tab === asked);
    const place = known ? (asked.indexOf('/') < 0 ? placeUnder(asked) : asked) : placeUnder(tabs.length > 0 ? tabs[0].dataset.tab : '');
    if (place.indexOf('/') >= 0) {
      openSubs[topOf(place)] = place;
      vscode.setState(Object.assign({}, vscode.getState() || {}, { subs: openSubs }));
    }
    for (const tab of tabs) { markTab(tab, isShown(tab.dataset.tab, place)); }
    for (const pane of catalogAll('[data-pane]')) { pane.hidden = !isShown(pane.dataset.pane, place); }
    if (tell) { vscode.postMessage({ type: 'tab', id: place }); }
  }
  document.addEventListener('click', (event) => {
    const pressed = event.target;
    if (!pressed || typeof pressed.closest !== 'function') { return; }
    const tab = pressed.closest('[data-tab]');
    if (tab) { showPlace(tab.dataset.tab, true); }
    // A jump to another place of this page (E5.1): what the old pages' "Edit commands…" and "Edit roles…" opened as a
    // page of their own is a place here, so the press opens it and tells the host as a tab press does. Not a
    // [data-tab]: a jump drawn inside a pane is no tab of a strip, and the strip's marking must never reach it.
    const jump = pressed.closest('[data-goto]');
    if (jump) { showPlace(jump.dataset.goto, true); }
  });
  window.addEventListener('message', (event) => {
    if (event.data && event.data.type === 'showTab') { showPlace(String(event.data.id || ''), false); }
  });
  showPlace(heldPlace, false);`;
}

/** The one confirm dialog: opened by a button that asks, sending its command only on the action button. */
function confirmScript(): string {
  return `
  // Where the caret goes after a confirmed action removed the control it was on — once, then forgotten.
  const focusAfter = (vscode.getState() || {}).focusAfter;
  if (focusAfter) {
    vscode.setState(Object.assign({}, vscode.getState() || {}, { focusAfter: undefined }));
    const target = document.getElementById(focusAfter);
    if (target) { target.focus(); }
  }
  // The one confirm dialog: filled from the button that asked, sending only on its action button.
  let asking = null;
  function closeConfirm() {
    const dialog = document.getElementById('confirm-dialog');
    if (dialog && dialog.open) { dialog.close(); }
    asking = null;
  }
  document.addEventListener('click', (event) => {
    const pressed = event.target;
    if (!pressed || typeof pressed.closest !== 'function') { return; }
    const asks = pressed.closest('[data-asks]');
    const dialog = document.getElementById('confirm-dialog');
    if (!asks || asks.disabled || !dialog) { return; }
    asking = asks;
    document.getElementById('confirm-title').textContent = asks.dataset.askTitle || '';
    document.getElementById('confirm-body').textContent = asks.dataset.askBody || '';
    const go = document.getElementById('confirm-go');
    go.textContent = asks.dataset.askAction || 'OK';
    go.className = asks.dataset.askDanger === 'true' ? 'primary danger' : 'primary';
    dialog.showModal();
  });
  const confirmGo = document.getElementById('confirm-go');
  if (confirmGo) {
    confirmGo.addEventListener('click', () => {
      const asked = asking;
      closeConfirm();
      if (asked && asked.dataset.asks === 'removeModel') {
        // The card goes with the row, and the caret with it: the next document puts it in the search box (PR #687).
        vscode.setState(Object.assign({}, vscode.getState() || {}, { focusAfter: 'model-search' }));
      }
      if (asked) { send({ type: 'command', command: asked.dataset.asks, id: asked.dataset.id }, asked); }
    });
  }
  const confirmKeep = document.getElementById('confirm-keep');
  if (confirmKeep) { confirmKeep.addEventListener('click', closeConfirm); }
  const confirmDialog = document.getElementById('confirm-dialog');
  if (confirmDialog) { confirmDialog.addEventListener('cancel', () => { asking = null; }); }`;
}

/**
 * The Models tab in the page: the filters (search, where it runs, switched-off, the three chip rows) narrow the cards by
 * their `data-*` and are kept in this webview's state — narrowing changes nothing stored, so it costs no round trip and
 * no repaint — and each system prompt counts its UTF-8 bytes as it is typed, against the 8 KiB the host enforces.
 */
function modelsScript(): string {
  return `${modelFiltersScript()}${modelEventsScript()}`;
}

/** Which cards the filters show — the rules, read off each card's `data-*`. */
function modelFiltersScript(): string {
  return `
  const modelFilters = Object.assign({ q: '', access: '', off: false, uses: '', effort: '', runtime: '' }, (vscode.getState() || {}).models || {});
  function saveModelFilters() { vscode.setState(Object.assign({}, vscode.getState() || {}, { models: modelFilters })); }
  function listHas(list, word) { return (' ' + list + ' ').indexOf(' ' + word + ' ') >= 0; }
  function cardShown(card) {
    const d = card.dataset;
    return (modelFilters.off || d.enabled === 'true')
      && (!modelFilters.q || d.search.indexOf(modelFilters.q.toLowerCase()) >= 0)
      && (!modelFilters.access || d.access === modelFilters.access)
      && (!modelFilters.uses || listHas(d.uses, modelFilters.uses))
      && (!modelFilters.effort || d.effort === modelFilters.effort)
      && (!modelFilters.runtime || d.runtime === modelFilters.runtime);
  }
  function applyModelFilters() {
    const cards = catalogAll('[data-model-card]');
    let shown = 0;
    for (const card of cards) { card.hidden = !cardShown(card); shown += card.hidden ? 0 : 1; }
    for (const chip of catalogAll('[data-chip]')) {
      chip.setAttribute('aria-pressed', modelFilters[chip.dataset.chip] === chip.dataset.value ? 'true' : 'false');
    }
    const empty = document.querySelector('[data-models-empty]');
    if (empty) { empty.hidden = shown > 0 || cards.length === 0; }
  }
`;
}

/** What moves the filters — the chips, the search box, the two pickers — and the prompt's byte count. */
function modelEventsScript(): string {
  return `
  function setModelFilter(key, value) { modelFilters[key] = value; saveModelFilters(); applyModelFilters(); }
  document.addEventListener('click', (event) => {
    const pressed = event.target;
    if (!pressed || typeof pressed.closest !== 'function') { return; }
    const chip = pressed.closest('[data-chip]');
    if (chip) { setModelFilter(chip.dataset.chip, modelFilters[chip.dataset.chip] === chip.dataset.value ? '' : chip.dataset.value); }
    if (pressed.id === 'model-clear') {
      Object.assign(modelFilters, { q: '', access: '', uses: '', effort: '', runtime: '' });
      saveModelFilters();
      applyModelFilters();
    }
    // A feature tab's "Change on Models" (E4.1): Models, narrowed to the rows ticked for that feature.
    const narrow = pressed.closest('[data-models-uses]');
    if (narrow) {
      setModelFilter('uses', narrow.dataset.modelsUses || '');
      showPlace('models', true);
    }
    // Security lane's Try it (E4.2): the sample is the command's id — the host puts it to coai-mcp on stdin.
    const tryIt = pressed.closest('[data-security-try]');
    if (tryIt) {
      const sample = document.getElementById('security-sample');
      send({ type: 'command', command: 'trySecurity', id: sample ? String(sample.value || '') : '' }, tryIt);
    }
  });
  document.addEventListener('input', (event) => {
    const typed = event.target;
    if (!typed) { return; }
    if (typed.id === 'model-search') { setModelFilter('q', typed.value || ''); }
    if (typed.dataset && typed.dataset.setting === 'systemPrompt') {
      const count = document.querySelector('[data-bytes-for="' + typed.id + '"]');
      if (count) { count.textContent = String(new TextEncoder().encode(typed.value || '').length); }
    }
  });
  document.addEventListener('change', (event) => {
    const changed = event.target;
    if (changed && changed.id === 'model-access') { setModelFilter('access', changed.value || ''); }
    if (changed && changed.id === 'model-show-off') { setModelFilter('off', changed.checked === true); }
  });
  const modelSearchBox = document.getElementById('model-search');
  if (modelSearchBox) { modelSearchBox.value = modelFilters.q; }
  const modelAccessPick = document.getElementById('model-access');
  if (modelAccessPick) { modelAccessPick.value = modelFilters.access; }
  const modelShowOff = document.getElementById('model-show-off');
  if (modelShowOff) { modelShowOff.checked = modelFilters.off === true; }
  applyModelFilters();`;
}
