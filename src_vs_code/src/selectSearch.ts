import { jsonForScript } from './webviewHtml';

/**
 * The search box over a long list: one pure ranking, and the page fragment that attaches a box to every select long
 * enough to need one (research/PLAN_model_search_and_busy_marks.md, Epic 2).
 *
 * <p><b>Why a box beside the select, not a datalist.</b> A datalist filters by the value already in its field, so the
 * moment a model was chosen every other one vanished (`modelOptions` in `panelView.ts` records it). The select stays
 * the control; the box only decides which of its options are in it, and in what order.</p>
 *
 * <p><b>One road in.</b> The fragment runs once in the shared page script (`pageDocument`) over every `select` the page
 * drew, so a select added tomorrow gets the box without being touched. The ranking is embedded by its SOURCE TEXT, the
 * way the rounds log embeds `rowMatches`: the function the unit tests run is the function the page runs, and
 * `bundledPage.test.ts` checks the minified bundle still defines it.</p>
 */

/** A list with fewer options than this is short enough to scan by eye, and gets no box. */
export const SEARCH_FROM_OPTIONS = 15;

/**
 * How long after Enter a new document still puts the caret back in the box. The repaint a pick causes arrives within
 * a render, which is seconds at worst; a note older than this is about some earlier pick, and honouring it would let a
 * repaint minutes later take the caret from wherever the person is now.
 */
export const RETURN_TO_BOX_MS = 15_000;

/**
 * Which of `choices` match `query`, as their indices in rank order — non-matches are absent.
 *
 * <p>Every word of the query must occur in the choice's value or in its label (each word wholly in one of them),
 * ignoring case. The FIRST word decides the
 * tier: a choice that starts with it, then one where a segment does (after `/`, `-`, `.`, `:` or a space), then one
 * that merely contains it; inside a tier the list keeps its own order. A blank query is every choice, in order.</p>
 *
 * <p>EMBEDDED IN THE PAGE BY ITS SOURCE TEXT: it may read nothing but its own parameters and must contain no
 * backtick, comments included — the page is a template literal (rounds log, `roundsLog.ts`, the same constraint).</p>
 */
export function rankChoices(query: string, choices: readonly { readonly value: string; readonly text: string }[]): number[] {
  // Anonymous callbacks only, no helper of its own: a NAMED inner function is minified to a short name that the
  // bundle check cannot tell from a stranger (bundledPage.test.ts, the 0.29.10 rule), and a joined string can come out
  // of the minifier as a template literal.
  const words = query.toLowerCase().split(/\s+/).filter((word) => word.length > 0);
  // A blank query has no first word, and every string starts with the empty one: all match, all in tier 0, in order.
  const first = words[0] ?? '';

  return choices
    .map((choice, index) => ({ index, value: choice.value.toLowerCase(), text: choice.text.toLowerCase() }))
    .filter((entry) => words.every((word) => entry.value.includes(word) || entry.text.includes(word)))
    .map((entry) => ({
      index: entry.index,
      tier: entry.value.startsWith(first) || entry.text.startsWith(first)
        ? 0
        : entry.value.split(/[/\-.: ]/).concat(entry.text.split(/[/\-.: ]/)).some((part) => part.startsWith(first)) ? 1 : 2,
    }))
    .sort((a, b) => a.tier - b.tier || a.index - b.index)
    .map((entry) => entry.index);
}

/** The box's own rule. Its look is the shared input rule's (`input[type="search"]` there); this only spaces it. */
export const SELECT_SEARCH_CSS = `
  .select-search { margin: 0 0 3px; }`;

/**
 * The page fragment: attach a box before every select of {@link SEARCH_FROM_OPTIONS} or more options.
 *
 * <p>It runs inside the shared page script and uses two of its names, `vscode` and `idOf` (one control's identity);
 * the focus hold it reports is the same `focus` message the settings send, under an id of its own (`search|` + the
 * select's identity), which the host keeps as an opaque string (`SurfaceSlot.edited`).</p>
 *
 * <ul>
 * <li><b>Sentinels</b> — values such as the empty first entry and "another model…" — are never ranked, never
 * removed, and keep their place at the top or the bottom of the list.</li>
 * <li><b>Non-matches are DETACHED</b>, not hidden: an `option hidden` is not honoured by every native dropdown and is
 * still reachable by the arrow keys (plan round, gemini). The original order is recorded once, when the box is
 * attached, and is what an emptied query and Escape restore. The select's value is re-applied after every pass, so
 * moving options can never change what is chosen; the chosen option stays in the list even when it does not match.</li>
 * <li><b>Enter</b> commits the first MATCH by setting the select's value and dispatching ITS change event, so a setting
 * select saves and a prompt select posts its prompt, exactly as a mouse pick does. Nothing matches, a blank query, or
 * a disabled select: Enter does nothing. <b>Escape</b> empties the box; <b>ArrowDown</b> moves to the select.</li>
 * <li><b>A query survives a replaced document</b>: it is kept in the webview's own state, keyed by the select's
 * identity, and re-applied when the page is drawn again; a stored query whose select no longer has a box is dropped.</li>
 * </ul>
 *
 * <p>Five small named pieces rather than one: each is a page function of its own, and this repository holds new code to
 * fifty lines a function (`eslint.config.mjs`).</p>
 *
 * @param sentinels the option values that are requests rather than choices
 */
export function selectSearchScript(sentinels: readonly string[]): string {
  return [
    searchStateScript(sentinels), searchListScript(), searchBoxScript(), searchKeysScript(), searchAttachScript(),
  ].join('');
}

/** The ranking, the constants, and what a box keeps in the webview's state and says about focus. */
function searchStateScript(sentinels: readonly string[]): string {
  return `
  // ---- The search box over a long list (selectSearch.ts) ----
  var rankChoices = ${rankChoices.toString()};
  const searchFrom = ${SEARCH_FROM_OPTIONS};
  const searchSentinels = ${jsonForScript(sentinels)};
  const searchStored = (vscode.getState() || {}).search || {};
  // Which box Enter was pressed in, and when — read once, spent at the end of the attach below.
  const searchReturn = (vscode.getState() || {}).searchFocus;
  // Replaced, never mutated in place: each change makes a new record of what the boxes hold.
  let searchKept = {};
  const searchBoxes = [];
  function keepSearch(key, query) {
    const next = {};
    for (const name of Object.keys(searchKept)) {
      if (name !== key) {
        next[name] = searchKept[name];
      }
    }
    if (query.length > 0) {
      next[key] = query;
    }
    searchKept = next;
    vscode.setState({ ...(vscode.getState() || {}), search: searchKept });
  }
  // A prompt picker has no setting name; its identity is its role and round, in the four parts a setting's has, so
  // the page's focus pattern (FOCUS_ID in panelView.ts) admits it behind the search prefix.
  function selectKey(select) {
    return select.dataset.setting !== undefined
      ? idOf(select)
      : 'prompt|' + (select.dataset.prompt || '') + '|' + (select.dataset.round || '') + '|';
  }
  function holdSearch(box, editing) {
    vscode.postMessage({
      type: 'focus',
      id: 'search|' + box.dataset.searchFor,
      editing,
      start: typeof box.selectionStart === 'number' ? box.selectionStart : 0,
      end: typeof box.selectionEnd === 'number' ? box.selectionEnd : 0,
    });
  }
`;
}

/** A select's options as drawn, split once into sentinels and choices; and the list rebuilt for one query. */
function searchListScript(): string {
  return `
  function searchList(select) {
    const all = Array.prototype.slice.call(select.options);
    const isSentinel = (option) => searchSentinels.indexOf(option.value) >= 0;
    const firstChoice = all.findIndex((option) => !isSentinel(option));
    const candidates = all.filter((option) => !isSentinel(option));
    return {
      all,
      leading: all.filter((option, index) => isSentinel(option) && index < firstChoice),
      trailing: all.filter((option, index) => isSentinel(option) && index > firstChoice),
      candidates,
      choices: candidates.map((option) => ({ value: option.value, text: option.text })),
    };
  }
  // Answers the options that MATCH, in rank order — what Enter takes the first of. Blank: none, and the drawn order.
  function showSearch(select, list, query) {
    const chosen = select.value;
    const blank = query.trim().length === 0;
    const matches = blank ? [] : rankChoices(query, list.choices).map((index) => list.candidates[index]);
    const stranded = list.candidates.filter((option) => option.value === chosen && matches.indexOf(option) < 0);
    const order = blank ? list.all : list.leading.concat(stranded, matches, list.trailing);
    // The same answer as the list already shows: nothing to move (E2 code round, local).
    const shown = Array.prototype.slice.call(select.options);
    if (shown.length === order.length && order.every((option, index) => shown[index] === option)) {
      return matches;
    }
    for (const option of list.all) {
      if (option.parentNode === select) {
        select.removeChild(option);
      }
    }
    for (const option of order) {
      select.appendChild(option);
    }
    select.value = chosen;
    return matches;
  }
`;
}

/** The box itself, placed before its select. */
function searchBoxScript(): string {
  return `
  function searchBox(select, list, key) {
    const box = document.createElement('input');
    box.type = 'search';
    box.className = 'select-search';
    box.placeholder = 'Search ' + list.candidates.length + ' choices…';
    box.setAttribute('aria-label', 'Search the ' + list.candidates.length + ' choices of the list below');
    box.dataset.searchFor = key;
    box.disabled = select.disabled;
    select.parentNode.insertBefore(box, select);
    return box;
  }
`;
}

/** Enter, Escape and ArrowDown in a box. `search` holds the box's current matches and how to show a query. */
function searchKeysScript(): string {
  return `
  function onSearchKey(event, box, select, search) {
    if (event.key === 'Enter') {
      event.preventDefault();
      const first = search.matches()[0];
      if (!select.disabled && first !== undefined) {
        // The pick releases the hold, as a mouse pick does, so the repaint it causes carries no caret: leave a note
        // for the next document to put it back here.
        vscode.setState({ ...(vscode.getState() || {}), searchFocus: { key: box.dataset.searchFor, at: Date.now() } });
        select.value = first.value;
        select.dispatchEvent(new Event('change'));
      }
      return;
    }
    if (event.key === 'Escape') {
      event.preventDefault();
      box.value = '';
      search.show('');
      return;
    }
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      select.focus();
    }
  }
`;
}

/** One box per long select, its listeners, the query a previous document left, and the state written back whole. */
function searchAttachScript(): string {
  return `
  function attachSearch(select) {
    if (select.options.length < searchFrom) {
      return;
    }
    const key = selectKey(select);
    const list = searchList(select);
    const box = searchBox(select, list, key);
    let matches = [];
    const show = (query) => {
      matches = showSearch(select, list, query);
      keepSearch(key, query);
    };
    box.addEventListener('input', () => {
      show(box.value);
      holdSearch(box, true);
    });
    box.addEventListener('focusin', () => holdSearch(box, true));
    box.addEventListener('focusout', (event) => {
      // Moving between a box and a control is not a moment to rebuild the page, exactly as between two controls.
      const next = event.relatedTarget;
      if (!(next && next.dataset && (next.dataset.setting !== undefined || next.dataset.searchFor !== undefined))) {
        holdSearch(box, false);
      }
    });
    box.addEventListener('keydown', (event) => onSearchKey(event, box, select, { matches: () => matches, show }));
    const stored = typeof searchStored[key] === 'string' ? searchStored[key] : '';
    if (stored.length > 0) {
      box.value = stored;
      show(stored);
    }
    searchBoxes.push(box);
  }
  for (const select of document.querySelectorAll('select')) {
    attachSearch(select);
  }
  // Enter's note, honoured only while fresh and only for a box this document has; spent either way.
  const searchBack = searchReturn && Date.now() - searchReturn.at < ${RETURN_TO_BOX_MS}
    ? searchBoxes.find((box) => box.dataset.searchFor === searchReturn.key)
    : undefined;
  if (searchBack !== undefined) {
    searchBack.focus();
    searchBack.setSelectionRange(searchBack.value.length, searchBack.value.length);
  }
  // Written back whole: a query for a select that no longer has a box is dropped here, and the note is spent.
  const { searchFocus: _spent, ...searchRest } = vscode.getState() || {};
  vscode.setState({ ...searchRest, search: searchKept });
`;
}
