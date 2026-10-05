import { tabKeysScript } from './tabKeys';
import { textControlsScript } from './textControls';
import { jsonForScript } from './webviewHtml';

/**
 * The new Settings page's own script (todo/PLAN_one_model_catalog.md, E3.1), appended to the shared page script that
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
${tabKeysScript()}
${textControlsScript()}
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
  });
  window.addEventListener('message', (event) => {
    if (event.data && event.data.type === 'showTab') { showPlace(String(event.data.id || ''), false); }
  });
  showPlace(heldPlace, false);`;
}

/** The one confirm dialog: opened by a button that asks, sending its command only on the action button. */
function confirmScript(): string {
  return `
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
      if (asked) { send({ type: 'command', command: asked.dataset.asks, id: asked.dataset.id }, asked); }
    });
  }
  const confirmKeep = document.getElementById('confirm-keep');
  if (confirmKeep) { confirmKeep.addEventListener('click', closeConfirm); }
  const confirmDialog = document.getElementById('confirm-dialog');
  if (confirmDialog) { confirmDialog.addEventListener('cancel', () => { asking = null; }); }`;
}
