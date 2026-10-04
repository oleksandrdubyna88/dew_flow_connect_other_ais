/**
 * The Security lane tab's part of the Settings page script (todo/PLAN_the_security_tab_reads_at_a_glance.md, epic 3, C).
 *
 * <p><b>Why page-local state.</b> Any write repaints the page, and a repaint REPLACES the document: a fold the person
 * opened would snap shut, and the control they just used would lose focus — the change handler releases focus to the
 * host, so the repaint carries none. The only store that survives a replaced document is the webview's own
 * `setState` (the precedent is `selectSearch.ts`), and it is synchronous. So: a toggle records the open folds there; a
 * change, or a press of a button that removes itself, records where focus goes; and the new document restores both.
 * The markup is always drawn closed, so the paint key never moves on a toggle.</p>
 *
 * <p><b>A focus note is honoured only while it is fresh</b> ({@link SECURITY_FOCUS_FRESH_MS}), as `selectSearch.ts`
 * honours its own: a press that writes nothing — a cancelled modal, a refused command — brings no repaint, and an old
 * note must not pull the focus somewhere the next, unrelated repaint. And the focus is put back one microtask LATER,
 * after the shared script has wired its `focusin` listeners, so the host hears it as it hears a click.</p>
 *
 * <p>Stored ids are only ever COMPARED with each element's dataset — never put into a selector — so a prompt id that is
 * odd, or hostile, cannot change what the page selects.</p>
 */
export const SECURITY_FOCUS_FRESH_MS = 4000;

export function securityLaneScript(): string {
  return `
  {
    const keptState = () => vscode.getState() || {};
    const keep = (patch) => vscode.setState(Object.assign({}, keptState(), patch));
${foldScript()}${focusScript()}  }
`;
}

/** Folds reopen from the page-local state their toggles write. */
function foldScript(): string {
  return `
    const opened = new Set(keptState().seclaneOpen || []);
    for (const fold of document.querySelectorAll('[data-seclane-open]')) {
      if (opened.has(fold.dataset.seclaneOpen)) {
        fold.open = true;
      }
      fold.addEventListener('toggle', () => {
        const now = new Set(keptState().seclaneOpen || []);
        if (fold.open) {
          now.add(fold.dataset.seclaneOpen);
        } else {
          now.delete(fold.dataset.seclaneOpen);
        }
        keep({ seclaneOpen: Array.from(now) });
      });
    }
`;
}

/** Focus goes back where the last change or self-removing press said, while that note is fresh. */
function focusScript(): string {
  return `
    const back = keptState().seclaneFocus;
    if (back) {
      keep({ seclaneFocus: null });
      if (Date.now() - (back.at || 0) <= ${SECURITY_FOCUS_FRESH_MS}) {
        queueMicrotask(() => focusBack(back));
      }
    }
    function focusBack(target) {
      const wanted = (el) => target.field !== undefined
        ? el.dataset.securityField === target.field
        : el.dataset.command === target.command && (el.dataset.id || '') === target.id;
      for (const el of document.querySelectorAll(target.field !== undefined ? '[data-setting]' : '[data-command]')) {
        if (wanted(el)) {
          el.focus();
          return;
        }
      }
    }
    for (const el of document.querySelectorAll('[data-setting]')) {
      if (el.dataset.securityField !== undefined) {
        el.addEventListener('change', () => keep({ seclaneFocus: { field: el.dataset.securityField, at: Date.now() } }));
      }
    }
    for (const el of document.querySelectorAll('[data-command]')) {
      if (el.dataset.seclaneThen !== undefined) {
        el.addEventListener('click', () => keep({ seclaneFocus: { command: el.dataset.seclaneThen, id: el.dataset.seclaneThenId || '', at: Date.now() } }));
      }
    }
`;
}
