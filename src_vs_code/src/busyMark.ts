import type { BusySnapshot } from './inFlight';
import { jsonForScript } from './webviewHtml';

/**
 * The busy mark: a thin progress bar at the top of a page, and `aria-busy` on the control that started the work,
 * for anything the host takes longer than {@link BUSY_AFTER_MS} to finish (research/PLAN_model_search_and_busy_marks.md,
 * Epic 3).
 *
 * <p>Asked for on 2026-10-02: a setting change froze the panel for seconds with nothing on screen, and the operator
 * wanted "everywhere an action takes longer than 0.5 s, a progress bar or a spinner". The page numbers every setting,
 * prompt and command it posts (`send`); the host settles each number once its work and the render it causes are done
 * (`inFlight.ts`). A number unsettled after the delay draws the mark; settled sooner, nothing is drawn at all.</p>
 *
 * <p><b>The page's part is optimistic.</b> It dies with the document. The host's part is not: it announces what is
 * running to every page, writes it into every document it paints, and answers a fresh document's `ready` — so a page
 * replaced mid-action shows the bar for what is still running, and an idle one never keeps it.</p>
 *
 * <p>One fragment, for any page built by `pageDocument`; another webview can include it the same way.</p>
 */

/** How long work runs before it is shown as running. Shorter, and every quick write flashes a bar. */
export const BUSY_AFTER_MS = 500;

/** The bar. Hidden until something has run past the delay; labelled, because a bar alone says nothing. */
export const BUSY_BAR = '<div id="busy-bar" class="busy-bar" role="progressbar" aria-label="Working…" hidden></div>';

/**
 * Its rules. No backticks and no hex: this is interpolated into the page's stylesheet template.
 *
 * <p>The moving stripe is decoration; a viewer who asked for less motion gets a still bar. As `lookingSpinner.ts`
 * does, the preference redefines the animation rather than the element, so no class is defined twice.</p>
 */
export const BUSY_CSS = `
  .busy-bar { position: fixed; top: 0; left: 0; right: 0; height: 2px; overflow: hidden; z-index: 10;
    pointer-events: none; }
  .busy-bar::before { content: ''; position: absolute; top: 0; bottom: 0; left: -30%; width: 30%;
    background: var(--vscode-progressBar-background); animation: busy-slide 1.2s ease-in-out infinite; }
  @keyframes busy-slide {
    from { left: -30%; }
    to { left: 100%; }
  }
  @media (prefers-reduced-motion: reduce) {
    @keyframes busy-slide {
      from { left: 0; width: 100%; }
      to { left: 0; width: 100%; }
    }
  }
  [aria-busy="true"] { cursor: progress; }`;

/**
 * The page fragment. Defines `send(message, control)`, which every setting, prompt and command post goes through;
 * any other message passes through it unnumbered. Runs in the shared page script and uses `vscode` alone.
 *
 * @param painted what the host had in flight when this document was built
 */
export function busyMarkScript(painted: BusySnapshot): string {
  return [busyStateScript(painted), busySendScript(), busyWaitScript(), busyHostScript()].join('');
}

/** The constants, the two halves of the mark, and drawing the bar from them. */
function busyStateScript(painted: BusySnapshot): string {
  return `
  // ---- The busy mark (busyMark.ts) ----
  const busyAfter = ${BUSY_AFTER_MS};
  const busyPainted = ${jsonForScript(painted)};
  const busyBar = document.getElementById('busy-bar');
  // The page's own numbered posts not yet settled, by number. Replaced on every change, never mutated in place.
  let busyLocal = new Map();
  let busySeq = 0;
  // Which document numbered its work: every document counts from 1, and the host settles into the slot's CURRENT
  // document, so a predecessor's seq 1 finishing would otherwise clear this one's (own review of E3).
  const busyDoc = Date.now().toString(36) + '-' + Math.random().toString(36).slice(2);
  // The host's part: past the delay or not, and the timer that will put it past.
  let busyHost = { due: false, timer: 0 };
  function drawBusy() {
    const on = busyHost.due || Array.from(busyLocal.values()).some((entry) => entry.due);
    if (busyBar === null) {
      return;
    }
    busyBar.hidden = !on;
    if (on) {
      busyBar.setAttribute('aria-busy', 'true');
    } else {
      busyBar.removeAttribute('aria-busy');
    }
  }
`;
}

/** Numbering a post, marking its control once the delay passes, and clearing both when the host settles it. */
function busySendScript(): string {
  return `
  const busyTracked = ['setting', 'prompt', 'command'];
  function send(message, control) {
    if (busyTracked.indexOf(message.type) < 0) {
      vscode.postMessage(message);
      return;
    }
    busySeq += 1;
    const seq = busySeq;
    busyLocal = new Map(busyLocal).set(seq, { control, due: false, waiting: false, timer: setTimeout(() => { markDue(seq); }, busyAfter) });
    vscode.postMessage({ ...message, seq, doc: busyDoc });
  }
  // Past its delay: the entry is replaced as due, and its control marked. Not for an entry waiting on the person.
  function markDue(seq) {
    const entry = busyLocal.get(seq);
    if (entry === undefined || entry.waiting) {
      return;
    }
    busyLocal = new Map(busyLocal).set(seq, { ...entry, due: true, timer: 0 });
    if (entry.control && typeof entry.control.setAttribute === 'function') {
      entry.control.setAttribute('aria-busy', 'true');
    }
    drawBusy();
  }
  // Another operation on the same control still past its delay keeps the mark (final E3 round, gemini).
  function unmarkUnlessHeld(control) {
    const held = Array.from(busyLocal.values()).some((other) => other.control === control && other.due);
    if (!held && control && typeof control.removeAttribute === 'function') {
      control.removeAttribute('aria-busy');
    }
  }
  function ownBusy(seq, doc) {
    return doc === busyDoc ? busyLocal.get(seq) : undefined;
  }
  function settleBusy(seq, doc) {
    const entry = ownBusy(seq, doc);
    if (entry === undefined) {
      return;
    }
    clearTimeout(entry.timer);
    const next = new Map(busyLocal);
    next.delete(seq);
    busyLocal = next;
    unmarkUnlessHeld(entry.control);
    drawBusy();
  }
`;
}

/**
 * The person has a VS Code prompt of this work open, then answers it (todo/PLAN_busy_mark_pauses_while_you_type.md §3.4).
 * Waiting on the person is not the host working: the entry stops its timer and drops its mark, and on the answer starts
 * again from the time the host says the work has taken — the page keeps no clock of its own for the pause.
 */
function busyWaitScript(): string {
  return `
  function waitBusy(seq, doc) {
    const entry = ownBusy(seq, doc);
    if (entry === undefined) {
      return;
    }
    clearTimeout(entry.timer);
    busyLocal = new Map(busyLocal).set(seq, { ...entry, due: false, waiting: true, timer: 0 });
    unmarkUnlessHeld(entry.control);
    drawBusy();
  }
  function workBusy(seq, doc, spentMs) {
    const entry = ownBusy(seq, doc);
    if (entry === undefined || !entry.waiting) {
      return;
    }
    const left = Math.max(0, busyAfter - spentMs);
    busyLocal = new Map(busyLocal).set(seq, { ...entry, waiting: false, timer: left > 0 ? setTimeout(() => { markDue(seq); }, left) : 0 });
    if (left === 0) {
      markDue(seq);
    }
  }
`;
}

/** The host's count — painted, answered to `ready`, or announced — and what is left of the delay for its oldest. */
function busyHostScript(): string {
  return `
  function hostBusy(count, oldestMs) {
    clearTimeout(busyHost.timer);
    const left = Math.max(0, busyAfter - oldestMs);
    busyHost = { due: count > 0 && left === 0, timer: 0 };
    if (count > 0 && left > 0) {
      busyHost.timer = setTimeout(() => {
        busyHost.due = true;
        drawBusy();
      }, left);
    }
    drawBusy();
  }
  hostBusy(busyPainted.count, busyPainted.oldestMs);
  window.addEventListener('message', (event) => {
    const message = event.data;
    if (message?.type === 'settled') {
      settleBusy(message.seq, message.doc);
    } else if (message?.type === 'busy') {
      hostBusy(Number(message.count) || 0, Number(message.oldestMs) || 0);
    } else if (message?.type === 'waiting') {
      waitBusy(message.seq, message.doc);
    } else if (message?.type === 'working') {
      workBusy(message.seq, message.doc, Number(message.spentMs) || 0);
    }
  });
`;
}
