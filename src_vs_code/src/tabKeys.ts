/**
 * The keyboard half of a tab strip: the contract `role="tablist"` promises and nothing here kept.
 *
 * <p>WAI-ARIA's tab pattern attaches an interaction to that role: <b>Left/Right</b> move to the
 * previous/next tab and wrap at the ends, <b>Home/End</b> go to the first and the last, and exactly one
 * tab is in the Tab order (roving `tabindex`). Announcing the role without the keys gives a person who
 * cannot see the page a wrong model of it (`todo/PLAN_the_tabs_announce_themselves.md`, §B, whose
 * step 1 this is — the Settings tab is its first consumer).</p>
 *
 * <p><b>A key does what a click does, through the click.</b> It moves focus to the tab it lands on and
 * clicks it, so selection follows focus (the pattern's automatic activation) and the page's own click
 * handler stays the ONE place a tab is selected — a second selection path would be a second thing to
 * keep in step with it. Keeping `tabindex` in step is that handler's job too: it is the part of
 * selecting a tab that has to happen however the tab was chosen.</p>
 *
 * <p>A page-script fragment in the shape of `zoomScript()` (`zoomControl.ts`): plain text inlined in a
 * page's one script. Keys it does not handle are left alone — no `preventDefault`, so typing, Tab and
 * the editor's own shortcuts keep working. It touches only the strip the focused tab is in, found by
 * `data-tab` and `data-strip` — the attributes `tabStrip.ts` already writes — rather than by role.</p>
 */
export function tabKeysScript(): string {
  return `
  const TAB_KEYS = { ArrowLeft: -1, ArrowRight: 1, Home: 'first', End: 'last' };
  document.addEventListener('keydown', (event) => {
    const target = event.target;
    const tab = target && typeof target.closest === 'function' ? target.closest('[data-tab]') : null;
    if (!tab) { return; }
    const move = TAB_KEYS[event.key];
    if (move === undefined) { return; }
    // The tabs of THIS strip: a page with two strips names each with data-strip, and a page with one
    // has none, in which case every tab on the page is in it.
    const strip = tab.closest('[data-strip]');
    const tabs = Array.prototype.slice.call(document.querySelectorAll('[data-tab]'))
      .filter((each) => each.closest('[data-strip]') === strip);
    const at = tabs.indexOf(tab);
    const next = move === 'first' ? tabs[0]
      : move === 'last' ? tabs[tabs.length - 1]
        : tabs[(at + move + tabs.length) % tabs.length];
    if (!next) { return; }
    event.preventDefault();
    next.focus();
    next.click();
  });`;
}
