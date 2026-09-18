import { HIGHLIGHT_CSS, highlight } from './codeHighlight';
import { cyclomatic } from './cyclomatic';
import { pairDiff } from './lineDiff';
import { RealRead, realView } from './realMethodView';
import { commit, none, text } from './reviewText';
import { slugOf, Tab, tabCss, tabStrip } from './tabStrip';
import { TONE_CSS, toneControlHtml, toneScript, toneStyle } from './textTone';
import { escapeHtml, jsonForScript } from './webviewHtml';
import { ZOOM_CSS, zoomControlHtml, zoomScript, zoomStyle } from './zoomControl';

/**
 * The review page: every collected pair, and what a person decided about it.
 *
 * <p>Markup and script as text, like every other page here, so the script can be RUN by a test
 * rather than read — `.agents/PROJECT.md` refuses a new behavioural assertion over page source, and
 * story 4 earned that ruling the hard way when a picker matched every regex written about it while
 * being wired to nothing.</p>
 *
 * <p><b>The tick-boxes follow `roundsLog.ts` rather than inventing a pattern.</b> That file already
 * has a `.pick` column and a `pickall` box, and PROJECT.md records what they cost: a tick-box branch
 * needs an early `return` or the control also opens the row it sits in, and no source assertion can
 * see a missing one. The plan's first draft called this the first multi-select in the codebase and a
 * plan reviewer corrected it.</p>
 *
 * <p><b>Every row is collapsed when the page opens</b> (`PLAN_the_review_page_can_be_read.md`,
 * story 1.1). Two hundred pairs each showing two skeletons is a page nobody scrolls; what a person
 * needs to see at once is the LIST, and the code when they ask for it. Which rows are open is held
 * by the panel and handed back in {@link ReviewView.expanded}, because a redraw replaces the whole
 * document — `rolesPanel.ts` holds its tab for the same reason and says so at length.</p>
 *
 * <p><b>A row says where it was and what the reviewers said</b> (story 2.1): the finding's path
 * and line, the short hash of the commit the reviewers read, the cause and the proposed fix as
 * recorded — "none recorded" when a finding has none, never an invented one — and the cyclomatic
 * complexity of each skeleton, labelled with the revision it came from. All of it is local metadata
 * read back out of a person's own database; none of it reaches the send, which projects
 * `StoredPair` and never sees this type. The hash is TEXT: a link promises "open at revision", and
 * whether that promise can be kept is epic 3's revision rule, not this story's.</p>
 *
 * <p><b>The methods can be shown un-anonymised, and that is a VIEW and never a payload</b> (story
 * 2.3). A toggle in the bar asks the server for the real text of each OPEN row — `--real-method`,
 * one process per row, cached by the panel for its lifetime — and both texts then live on the row:
 * the skeleton in one container, the real method in the other, and the toggle only decides which is
 * visible. A fetch never writes to what is on screen; it fills the hidden half and asks the same
 * render a toggle flip runs. What a send transmits is unchanged by any of it, by construction: the
 * page supplies decision IDs and the send projects the stored pair, and the server's own test holds
 * the serialised upload byte-identical with the view on and off.</p>
 *
 * <p>Pure, and free of `node:` and `vscode` imports, like `zoomControl.ts` and `textTone.ts` beside
 * it: a page module that reaches for either fails the bundle test, and a decision inside one is a
 * decision no unit test can run.</p>
 */

/**
 * One pair as the server hands it over.
 *
 * <p>The seven fields after `title` arrived with story 2.1. They are OPTIONAL on the wire —
 * `roundsDbRead.pairOf` fills each from a server too old to send it, with empty text and a line of
 * 0 — and REQUIRED here, so that the page cannot forget to decide what an empty one looks like.</p>
 */
export interface ReviewPair {
  readonly findingId: number;
  readonly symbolName: string;
  readonly language: string;
  readonly skeletonBefore: string;
  readonly skeletonAfter: string;
  /** -1 nobody has looked, 0 dropped, 1 kept. */
  readonly keep: number;
  readonly severity: string;
  readonly category: string;
  readonly title: string;
  /** The checkout the round reviewed, as the session recorded it. Empty when the server did not say. */
  readonly repoPath: string;
  /** The commit the reviewers read — the BEFORE skeleton is the method at this commit. */
  readonly headSha: string;
  /** The commit the fix was found in — the AFTER skeleton is the method at this one. */
  readonly fixSha: string;
  /** The finding's path at `headSha`, relative to `repoPath`. */
  readonly file: string;
  /** The finding's line at `headSha`; 0 when none was recorded. */
  readonly line: number;
  /** The reviewers' cause, verbatim — a model's prose about somebody's code, escaped on the way in. */
  readonly why: string;
  /** The reviewers' proposed fix, verbatim. */
  readonly fix: string;
}

/**
 * Everything the page is drawn from, as one argument.
 *
 * <p>`renderHelpHtml`'s shape, for the same reason it has it: this went from three positional
 * parameters to six in one story, and the fourth one along would have been a boolean nobody could
 * read at a call site. The two offsets are the SETTINGS' values, read by the host at paint —
 * the page never computes its own.</p>
 */
export interface ReviewView {
  readonly pairs: readonly ReviewPair[];
  readonly nonce: string;
  /** What the server said when it could not answer. Empty is not the same as an empty corpus. */
  readonly trouble?: string;
  /** The `findingId`s whose code is showing. Keyed by id, never by position — see {@link row}. */
  readonly expanded?: ReadonlySet<number>;
  readonly uiScale?: number;
  readonly textTone?: number;

  /**
   * The project strip, and the language strip within the chosen project.
   *
   * <p>Built by `reviewTabs`, which also decides {@link ReviewView.pairs} — the page is handed the
   * pairs to DRAW, so the waiting count and the tick-boxes are about what is on screen and nothing
   * here has to know it was filtered. A strip is empty when there was only one value to offer, and
   * an empty strip renders as nothing at all.</p>
   */
  readonly projects?: readonly Tab[];
  readonly languages?: readonly Tab[];

  /** Which tab is open in each strip, as `reviewTabs` resolved it — never as it was merely held. */
  readonly project?: string;
  readonly language?: string;

  /**
   * The tab just activated, so the keyboard gets back what it was on.
   *
   * <p>Every repaint replaces the document, so a person who tabbed to a project and pressed Enter
   * lost the focus to the top of a new page — and had to navigate the whole thing again to reach
   * the language strip beside it. Found on the code round. Absent on a draw nobody pressed, which
   * is every draw the server drives: moving somebody's focus because a poll came back would be the
   * same rudeness in the other direction.</p>
   */
  readonly focus?: FilterPress;

  /**
   * Whether the methods are shown un-anonymised.
   *
   * <p>A VIEW, and only ever a view: nothing about it reaches a decision or a send. Held by the
   * panel for the same reason {@link expanded} is — every paint replaces the document — and, like
   * it, forgotten when the window closes. Off by default: the page a person opens shows what leaves
   * the machine, and asks for the rest.</p>
   */
  readonly realText?: boolean;

  /**
   * The real methods already fetched, by `findingId` — what the panel has cached for its lifetime.
   *
   * <p>A row whose method is here is drawn with BOTH texts and the toggle picks; a row whose method
   * is not, and is open with the view on, asks for it when the page loads. Nothing is fetched at
   * paint: 200 pairs would be 400 git reads on a path that already costs 468 ms.</p>
   */
  readonly real?: ReadonlyMap<number, RealRead>;

  /**
   * Which paint this is.
   *
   * <p>Every fetch the page asks for carries a generation composed of this and a counter, and the
   * answer is applied only if that exact generation is still wanted — so an answer to a request the
   * PREVIOUS document made cannot be painted into this one. Two reviewers of the plan round named
   * the race from two angles; this is the half of the answer that survives a redraw.</p>
   */
  readonly draw?: number;
}

/** Which strip was pressed, and which tab in it. */
export interface FilterPress {
  readonly strip: string;
  readonly key: string;
}

export const UNDECIDED = -1;
export const DROPPED = 0;
export const KEPT = 1;

/** What a person has decided, as a word. */
export function decision(keep: number): string {
  if (keep === KEPT) {
    return 'kept';
  }

  return keep === DROPPED ? 'dropped' : 'undecided';
}

/**
 * How many are still waiting on somebody.
 *
 * <p>The number a person comes back to the page for. A review of two hundred pairs happens over
 * days, which is the whole reason `keep` is a column and not a flag in the page.</p>
 */
export const undecided = (pairs: readonly ReviewPair[]): number =>
  pairs.filter((p) => p.keep === UNDECIDED).length;

/** An ARIA boolean is a lowercase STRING, which `${true}` also spells but by accident. */
const ariaBoolean = (yes: boolean): string => (yes ? 'true' : 'false');

/**
 * A `findingId` on its way into an attribute or a selector.
 *
 * <p>It is typed `number` and `roundsDbRead.pairOf` refuses a pair whose id is not one — but this
 * module is pure and its caller's promise is not a property of the page. The id is written into
 * `data-row`, `data-toggle`, `data-detail` and an element id, and the page's own script then builds
 * `[data-detail="' + id + '"]` from what it reads back, so a value carrying a quote would break out
 * of both the attribute and the selector. Escaping costs one call and makes the page safe by
 * construction rather than by what the reader upstream happens to check today.</p>
 */
const key = (findingId: number): string => escapeHtml(String(findingId));

/**
 * Reviewer prose, escaped on the way into the page.
 *
 * <p>`why` and `fix` are what a model wrote about somebody's code — external data, arriving through
 * a JSON document the server printed — and this page is where a person decides what leaves the
 * machine. An `<img onerror>` in a finding's "fix" renders as the text it is.</p>
 */
const prose = (value: string): string => {
  const said = text(value);

  return said.length > 0 ? escapeHtml(said) : none('none recorded');
};

/** Where the finding was: its path and line at the commit the reviewers read, and in which checkout. */
function where(pair: ReviewPair): string {
  const file = text(pair.file);
  const line = Number.isInteger(pair.line) && pair.line > 0 ? `:${pair.line}` : '';
  const place = file.length > 0
    ? `<code class="path">${escapeHtml(file)}${line}</code>`
    : none('no file recorded');
  const repo = text(pair.repoPath);
  const checkout = repo.length > 0 ? ` in <span class="repo">${escapeHtml(repo)}</span>` : '';

  return `${place} at ${commit(pair.headSha)}${checkout}`;
}

/**
 * The two counts, each labelled with the revision its skeleton came from.
 *
 * <p>"Of the method at `aaaa111`", never "of the method": the before side is the method as the
 * reviewers read it, the after side is the method at the commit the fix was found in — two
 * different commits — and both may have changed since. A number without its revision is confidently
 * wrong about today's file. A language the count does not read gets the count's own sentence, not a
 * zero.</p>
 */
function complexity(pair: ReviewPair): string {
  const before = cyclomatic(pair.skeletonBefore, pair.language);
  const after = cyclomatic(pair.skeletonAfter, pair.language);
  if (!before.known) {
    return none(before.why);
  }
  if (!after.known) {
    return none(after.why);
  }

  return `${before.value} at ${commit(pair.headSha)} → ${after.value} at ${commit(pair.fixSha)}`;
}

/** What a row says about itself, above its code. */
function about(pair: ReviewPair): string {
  return `<dl class="about">
      <dt>Where</dt><dd>${where(pair)}</dd>
      <dt>Why</dt><dd>${prose(pair.why)}</dd>
      <dt>Fix</dt><dd>${prose(pair.fix)}</dd>
      <dt title="cyclomatic complexity of the method as it was at that commit, counted from the skeleton — not of the file today">Complexity</dt><dd>${complexity(pair)}</dd>
    </dl>`;
}

/**
 * One pair: the line you always see, and the code you asked for.
 *
 * <p><b>Two `<tr>`s, not one row with hidden cells.</b> A collapsed pair must take the height of a
 * line, and cells sized for skeletons leave a collapsed table as a column of empty space wider than
 * the text beside it. The detail row carries the id it belongs to so a redraw can find it, and
 * `hidden` rather than a class so the browser's own default hides it before any stylesheet loads.</p>
 *
 * <p><b>The key is `findingId` everywhere</b> — `data-row`, `data-pick`, `data-toggle`,
 * `data-detail` and the element id. A position would pass every test written in one order and lose
 * the open row the moment a redraw reorders or removes anything, which is what the three tests named
 * in the plan exist to catch.</p>
 */
/**
 * The two halves of a row's code: the skeleton, and the real method — both on the row, one visible.
 *
 * <p>The toggle only ever flips `hidden` on these two; a fetch only ever fills the second. That is
 * what makes "what is on screen" a function of state the row already holds, and a response that
 * arrives late a thing that fills a container rather than a thing that paints. The skeleton steps
 * aside only when the real half carries TEXT (`shown`); a reason, or "fetching", sits above it.</p>
 */
function halves(pair: ReviewPair, id: string, showReal: boolean, read: RealRead | undefined): string {
  const differs = pairDiff(pair.skeletonBefore, pair.skeletonAfter);
  const fetched = read === undefined ? undefined : realView(pair, read);
  const skeletonHidden = showReal && fetched?.shown === true;

  return `<div class="skel" data-skel="${id}"${skeletonHidden ? ' hidden' : ''}>
    <div class="sides">
      <div class="side">
        <div class="sideName">Before</div>${highlight(pair.skeletonBefore, pair.language, differs.before)}
      </div>
      <div class="side">
        <div class="sideName">After</div>${highlight(pair.skeletonAfter, pair.language, differs.after)}
      </div>
    </div>
    </div>
    <div class="real" data-real="${id}"${showReal && fetched !== undefined ? '' : ' hidden'}>${fetched?.html ?? ''}</div>`;
}

function row(pair: ReviewPair, open: boolean, showReal: boolean, read: RealRead | undefined): string {
  const id = key(pair.findingId);
  const said = `${escapeHtml(pair.severity)} · ${escapeHtml(pair.category)} — ${escapeHtml(pair.title)}`;

  // The three lines are INSIDE the button, so the whole summary is the target. The first version
  // wrapped only the chevron and the symbol, and left the severity, the title and the state word
  // outside it — pressing any of them did nothing at all, while the stylesheet beside it claimed
  // the whole line was the button. A code reviewer (codex, UX) found it. They are spans rather
  // than divs because a `button` takes phrasing content: a `div` in there is invalid markup that
  // browsers merely tolerate.
  return `<tr class="pair" data-row="${id}">
  <td class="pick"><input type="checkbox" data-pick="${id}"></td>
  <td class="what">
    <button type="button" class="twist" data-toggle="${id}"
            aria-expanded="${ariaBoolean(open)}" aria-controls="detail-${id}">
      <span class="chev" aria-hidden="true">▸</span>
      <span class="lines">
        <span class="sym">${escapeHtml(pair.symbolName)}</span>
        <span class="said">${said}</span>
        <span class="state ${decision(pair.keep)}">${decision(pair.keep)}</span>
      </span>
    </button>
  </td>
</tr>
<tr class="detail" id="detail-${id}" data-detail="${id}"${open ? '' : ' hidden'}>
  <td class="pick"></td>
  <td>
    ${about(pair)}
    ${halves(pair, id, showReal, read)}
  </td>
</tr>`;
}

/**
 * Which cached rows carry TEXT and which carry a NOTE, for the page's script to start from.
 *
 * <p>Keyed by id as a string, because that is how the script reads ids back off the attributes.
 * Only the pairs being drawn: the cache may hold rows a filter has hidden, and the page has no
 * container for those.</p>
 */
function realKinds(pairs: readonly ReviewPair[], real: ReadonlyMap<number, RealRead>): Record<string, string> {
  return Object.fromEntries(pairs.flatMap((pair) => {
    const read = real.get(pair.findingId);

    return read === undefined ? [] : [[String(pair.findingId), realView(pair, read).shown ? 'text' : 'note']];
  }));
}


/**
 * The two filter strips, or nothing when neither offers a choice.
 *
 * <p>`onePanel` because both narrow the SAME table: a project tab and a language tab do not each
 * reveal a region of their own, so every one of them points at `#pairs`. `data-strip` is how the
 * one click handler tells a project press from a language press.</p>
 */
/**
 * The id of the button to focus, or empty.
 *
 * <p>Composed here rather than handed over by the panel, because the SLUG is the page's own
 * arithmetic — and because a key composed into a selector on the page would be a path from the
 * database reaching `querySelector`. This looks the key up among the tabs instead, and what crosses
 * into the script is an id this module built.</p>
 */
function focusId(view: ReviewView): string {
  const want = view.focus;
  if (want === undefined) return '';

  const tabs = want.strip === 'language' ? view.languages ?? [] : view.projects ?? [];
  const one = tabs.find((tab) => tab.key === want.key);

  return one === undefined ? '' : `${want.strip}-tab-${slugOf(one)}`;
}

function filterStrips(view: ReviewView): string {
  const projects = tabStrip(view.projects ?? [], view.project ?? '', {
    tab: 'project-tab-', panel: 'pairs', onePanel: true, label: 'Which project', strip: 'project',
  });
  const languages = tabStrip(view.languages ?? [], view.language ?? '', {
    tab: 'language-tab-', panel: 'pairs', onePanel: true, label: 'Which language', strip: 'language',
  });

  return `${projects}${languages}`;
}

/**
 * The three things this page can be showing, as one decision rather than a nested ternary.
 *
 * <p>They are genuinely three: a read that FAILED, a corpus that is empty, and rows. The first two
 * send a person to different places — press Collect, or find out why the server would not answer —
 * and collapsing them is the defect four reviewers found in the first version.</p>
 */
function body(pairs: readonly ReviewPair[], rows: string, trouble: string): string {
  if (trouble.length > 0) {
    return `<p class="empty" id="trouble">The pairs could not be read: ${escapeHtml(trouble)}</p>`;
  }

  if (pairs.length === 0) {
    return '<p class="empty" id="nothing">Nothing has been collected yet.'
      + ' Press Collect in the Bugz section of the panel.</p>';
  }

  return `<table id="pairs">
<thead><tr>
  <th class="pick"><input type="checkbox" id="pickall" title="Select every pair"></th>
  <th>Method</th>
</tr></thead>
<tbody>
${rows}
</tbody>
</table>`;
}

/**
 * The page, whole.
 *
 * <p>`trouble` is what the server said when it could not answer, and it is NOT the same page as an
 * empty corpus: "nothing has been collected yet" sends a person to press Collect, while a read that
 * timed out sends them somewhere else entirely. Four reviewers of the code round found the first
 * version saying the former for both.</p>
 */
export function reviewPageHtml(view: ReviewView): string {
  const { pairs, nonce, trouble = '', uiScale = 0, textTone = 0 } = view;
  const strips = filterStrips(view);
  const expanded = view.expanded ?? new Set<number>();
  const showReal = view.realText === true;
  const real = view.real ?? new Map<number, RealRead>();
  const rows = pairs
    .map((pair) => row(pair, expanded.has(pair.findingId), showReal, real.get(pair.findingId)))
    .join('\n');
  const waiting = undecided(pairs);

  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<style>
  *, *::before, *::after { box-sizing: border-box; }
  :root { color-scheme: light dark; }
  body {
    font-family: var(--vscode-font-family);
    color: var(--vscode-foreground); background: var(--vscode-editor-background);
    margin: 0; padding: 12px 16px;
    /* Both INSIDE the rule. A tone fragment written above it silently drops the whole body rule —
       the trap chatPage.ts documents and a test parses for. */
    ${zoomStyle(uiScale)} ${toneStyle(textTone)}
  }
${ZOOM_CSS}
${TONE_CSS}
${HIGHLIGHT_CSS}
${tabCss('4px 0 10px')}
  h1 { font-size: 1.15em; margin: 0 0 4px; }
  .hint { opacity: .7; font-size: .92em; margin: 0 0 12px; }
  .bar { display: flex; gap: 8px; align-items: center; margin: 0 0 10px; flex-wrap: wrap; }
  /* Pushes the view controls to the far end: what a person presses often and what they set once
     should not sit in one undifferentiated row of buttons. */
  .bar .spacer { flex: 1 1 auto; }
  button {
    background: var(--vscode-button-background); color: var(--vscode-button-foreground);
    border: none; border-radius: 2px; padding: 5px 12px; cursor: pointer;
    font-family: inherit; font-size: inherit;
  }
  button:disabled { opacity: .5; cursor: default; }
  button.quiet { background: none; color: var(--vscode-textLink-foreground); text-decoration: underline; }
  table { width: 100%; border-collapse: collapse; }
  th { text-align: left; font-size: .85em; text-transform: uppercase; letter-spacing: .06em;
       opacity: .7; padding: 4px 8px; border-bottom: 1px solid var(--vscode-panel-border); }
  td { vertical-align: top; padding: 8px; }
  /* The border belongs to the PAIR, and a pair is two rows: drawn under the summary when it is
     closed and under the detail when it is open, so an expanded pair reads as one block rather
     than as two rows that happen to be adjacent. */
  tr.pair > td { border-bottom: 1px solid var(--vscode-panel-border); }
  tr.pair:has(+ tr.detail:not([hidden])) > td { border-bottom: none; }
  tr.detail > td { border-bottom: 1px solid var(--vscode-panel-border); padding-top: 0; }
  /* Explicit rather than trusting the UA sheet to outrank a display we might add later. */
  tr[hidden] { display: none; }
  /* The tick column is as narrow as a box and must not take the click target of the row with it. */
  th.pick, td.pick { width: 1%; padding-right: 0; }
  th.pick input, td.pick input { cursor: pointer; margin: 0; }
  /* The whole summary line is the button — every one of the three lines is inside it — so the
     target is the line and not a glyph. It keeps the page's own text colour: a button coloured as
     a button would make every method look pressable in the accent colour and drown the state
     words underneath. */
  button.twist {
    background: none; color: inherit; padding: 0; text-align: left; width: 100%;
    display: flex; align-items: flex-start; gap: 6px; font: inherit;
  }
  .lines { display: flex; flex-direction: column; align-items: flex-start; min-width: 0; }
  .chev { display: inline-block; opacity: .6; transition: transform .1s; font-size: .9em;
          line-height: 1.4; }
  button.twist[aria-expanded="true"] .chev { transform: rotate(90deg); }
  .sym { font-weight: 600; }
  .said { opacity: .7; font-size: .85em; margin-top: 2px; }
  .state { font-size: .85em; margin-top: 4px; text-transform: uppercase; letter-spacing: .05em; }
  .state.kept { color: var(--vscode-charts-green); }
  .state.dropped { color: var(--vscode-charts-red); }
  .state.undecided { opacity: .5; }
  /* Without min-width: 0, a long unbroken token makes a flex child refuse to shrink and the table
     grows a horizontal scrollbar instead of wrapping. */
  .sides { display: flex; gap: 16px; align-items: flex-start; }
  .side { flex: 1 1 0; min-width: 0; }
  .sideName { font-size: .8em; text-transform: uppercase; letter-spacing: .06em; opacity: .55;
              margin-bottom: 3px; }
  /* What a row says about itself, above its code: a two-column list with the labels quiet. */
  .about { display: grid; grid-template-columns: max-content 1fr; gap: 3px 12px;
           margin: 0 0 10px; font-size: .92em; }
  .about dt { opacity: .6; font-size: .8em; text-transform: uppercase; letter-spacing: .06em;
              line-height: 1.7; }
  /* pre-wrap keeps the reviewers' own line breaks; anywhere keeps a long path from widening
     the table, which is the same reason .side has min-width: 0. */
  .about dd { margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; }
  .about code { font-family: var(--vscode-editor-font-family); font-size: .95em; }
  .none { opacity: .55; font-style: italic; }
  .repo { opacity: .7; }
  .empty { opacity: .7; padding: 24px 0; }
  /* The two halves of a row's code. Explicit, as tr[hidden] is, so a later display rule cannot
     outrank the attribute the script flips. */
  .skel[hidden], .real[hidden] { display: none; }
  .realNote { opacity: .7; font-style: italic; margin: 0 0 8px; }
  .who { margin: 0 0 8px; font-size: .92em; }
  .who .cls { font-weight: 600; }
  /* The toggle reads as pressed when the view is on — a person glancing at the bar must be able to
     tell which text the page is showing without reading a line of code. */
  button.quiet[aria-pressed="true"] { text-decoration: none; font-weight: 600; }
</style>
</head>
<body>
<h1>Review bugs</h1>
${strips}
<p class="hint" id="waiting">${waiting} of ${pairs.length} still waiting on you.</p>
<div class="bar">
  <button type="button" id="keep" disabled>Keep selected</button>
  <button type="button" id="drop" disabled>Drop selected</button>
  <button type="button" class="quiet" id="clear">Clear selection</button>
  <span class="hint" id="picked"></span>
  <span class="spacer"></span>
  <button type="button" class="quiet" id="expandAll">Expand all</button>
  <button type="button" class="quiet" id="collapseAll">Collapse all</button>
  <button type="button" class="quiet" id="realText" aria-pressed="${ariaBoolean(showReal)}"
          title="Show the methods as they really are, un-anonymised. A view only: what a send transmits is the skeleton, whatever this shows.">Real code</button>
  ${zoomControlHtml(uiScale)}${toneControlHtml(textTone)}
</div>
${body(pairs, rows, trouble)}
<script nonce="${nonce}">
(function () {
  var vscode = acquireVsCodeApi();
  var selected = {};

  function count() { return Object.keys(selected).length; }

  function paint() {
    var n = count();
    document.getElementById('keep').disabled = n === 0;
    document.getElementById('drop').disabled = n === 0;
    document.getElementById('picked').textContent = n === 0 ? '' : n + ' selected';
    var boxes = document.querySelectorAll('[data-pick]');
    for (var i = 0; i < boxes.length; i++) {
      boxes[i].checked = selected[boxes[i].getAttribute('data-pick')] === true;
    }
    // The select-all box too. The browser ticks it natively on click while the handler decides
    // from the COUNT, so the two drift apart the moment anything else changes the selection: after
    // a decision clears it the box stayed ticked over nothing, and unticking one row left it ticked
    // over a partial selection. It then showed the opposite of what the next click would do.
    var all = document.getElementById('pickall');
    if (all) { all.checked = boxes.length > 0 && n === boxes.length; }
  }

  function decide(keep) {
    var ids = Object.keys(selected).map(Number);
    if (ids.length === 0) { return; }
    // The page does NOT paint the new state itself. The decision is written by the extension and
    // the page is redrawn from what the database then says — a page that congratulated itself and
    // was wrong is exactly the failure this whole column exists to prevent.
    vscode.postMessage({ type: 'decide', keep: keep, ids: ids });
    selected = {};
    paint();
  }

  // Opening a row is painted HERE and the panel is merely told. A redraw runs the server — one
  // process per click is what a round trip would cost — so the page owns the gesture and the panel
  // owns what survives the next redraw. Nothing about the corpus changes either way.
  // The un-anonymised view. Both texts live on the row - the skeleton in one container, the real
  // method in the other - and this only decides which is visible. A fetch fills the second
  // container and then asks the same render a toggle flip runs; nothing here paints a response
  // directly, and nothing awaits anything.
  var realOn = ${showReal ? 'true' : 'false'};
  // Which cached rows carry text and which carry a note, from the paint that drew them.
  var have = ${jsonForScript(realKinds(pairs, real))};
  // What has been asked for and not yet answered: id -> the generation it was asked with.
  var pending = {};
  var seq = 0;
  var draw = ${jsonForScript(String(view.draw ?? 0))};

  function half(id, which) { return document.querySelector('[data-' + which + '="' + id + '"]'); }

  function wantReal(id, real) {
    if (pending[id] !== undefined) { return; }
    seq += 1;
    var generation = draw + '/' + seq;
    pending[id] = generation;
    real.innerHTML = '<p class="realNote">Fetching the real method…</p>';
    vscode.postMessage({ type: 'fetchReal', id: Number(id), generation: generation });
  }

  // The one render of a row's code: reads the CURRENT toggle and what the row already holds.
  function renderReal(id) {
    var skel = half(id, 'skel');
    var real = half(id, 'real');
    if (!skel || !real) { return; }
    if (!realOn) { skel.hidden = false; real.hidden = true; return; }
    var kind = have[id];
    skel.hidden = kind === 'text';
    real.hidden = false;
    if (kind === undefined) { wantReal(id, real); }
  }

  function renderOpenRows() {
    var details = document.querySelectorAll('[data-detail]');
    for (var i = 0; i < details.length; i++) {
      if (!details[i].hidden) { renderReal(details[i].getAttribute('data-detail')); }
    }
  }

  window.addEventListener('message', function (event) {
    var m = event.data;
    if (!m || m.type !== 'real') { return; }
    var id = String(m.id);
    // Applied only to the request that is still WANTED. The toggle flipped, the row collapsed or
    // the page was redrawn since this was asked for, and a late answer is discarded rather than
    // painted over whatever the person is looking at now.
    if (pending[id] !== m.generation) { return; }
    delete pending[id];
    var real = half(id, 'real');
    if (!real) { return; }
    real.innerHTML = m.html;
    have[id] = m.shown ? 'text' : 'note';
    renderReal(id);
  });

  function showRow(id, open) {
    // By the data attribute rather than by the element id, so opening one row and opening all of
    // them look the same thing up the same way. The element id exists for aria-controls, which
    // needs a real target; a second lookup path would be a second place for the two to disagree.
    var detail = document.querySelector('[data-detail="' + id + '"]');
    if (detail) { detail.hidden = !open; }
    var twist = document.querySelector('[data-toggle="' + id + '"]');
    if (twist) { twist.setAttribute('aria-expanded', open ? 'true' : 'false'); }
    // A row that opens with the view on asks for its method; a row that closes forgets it asked.
    if (open) { renderReal(id); } else { delete pending[id]; }
  }

  function showAll(open) {
    var details = document.querySelectorAll('[data-detail]');
    var ids = [];
    for (var i = 0; i < details.length; i++) {
      details[i].hidden = !open;
      var rowKey = details[i].getAttribute('data-detail');
      ids.push(Number(rowKey));
      if (open) { renderReal(rowKey); } else { delete pending[rowKey]; }
    }
    var twists = document.querySelectorAll('[data-toggle]');
    for (var j = 0; j < twists.length; j++) {
      twists[j].setAttribute('aria-expanded', open ? 'true' : 'false');
    }
    vscode.postMessage({ type: 'expandAll', ids: ids, open: open });
  }

  document.addEventListener('click', function (event) {
    var target = event.target;
    // ABOVE anything that acts on the row, and it RETURNS: a tick-box that falls through would
    // also trigger the row's own behaviour. roundsLog.ts has the same two lines for the same
    // reason, and PROJECT.md records what it cost to learn.
    var picking = target.closest ? target.closest('[data-pick]') : null;
    if (picking) {
      var id = picking.getAttribute('data-pick');
      if (selected[id]) { delete selected[id]; } else { selected[id] = true; }
      paint();
      return;
    }
    // closest() and not an id test, because the press can land on the chevron inside the button.
    // The selector is deliberately the BUTTON and not the row: matching the row would take the
    // whole line, tick-box included, and would read the expanded state off an element that does
    // not carry it — so a second press could never close what the first one opened. The suite
    // goes red on exactly that mutation.
    var twisting = target.closest ? target.closest('[data-toggle]') : null;
    if (twisting) {
      var rowId = twisting.getAttribute('data-toggle');
      var opening = twisting.getAttribute('aria-expanded') !== 'true';
      showRow(rowId, opening);
      vscode.postMessage({ type: 'expand', id: Number(rowId), open: opening });
      return;
    }
    // A filter press, and this page does NOT paint it: the host holds the choice, because the
    // document is replaced wholesale on every decision and a selection living here would die on the
    // first one. The data-strip attribute on the wrapper says which of the two strips it came
    // one attribute instead of two selectors that would drift apart.
    var tabbed = target.closest ? target.closest('[data-tab]') : null;
    if (tabbed) {
      var whichStrip = tabbed.closest ? tabbed.closest('[data-strip]') : null;
      vscode.postMessage({
        type: 'tab',
        strip: whichStrip ? whichStrip.getAttribute('data-strip') : '',
        key: tabbed.getAttribute('data-tab')
      });
      return;
    }
    if (target.id === 'pickall') {
      var boxes = document.querySelectorAll('[data-pick]');
      var everyOne = boxes.length > 0 && count() === boxes.length;
      selected = {};
      if (!everyOne) {
        for (var i = 0; i < boxes.length; i++) { selected[boxes[i].getAttribute('data-pick')] = true; }
      }
      paint();
      return;
    }
    if (target.id === 'expandAll' || target.id === 'collapseAll') {
      showAll(target.id === 'expandAll');
      return;
    }
    if (target.id === 'realText') {
      // Synchronous, from what the rows already hold: every open row is re-rendered from the new
      // toggle before this handler returns. Everything asked for so far is forgotten - an answer
      // to the old view must not land in the new one - and the rows that now need a fetch ask
      // again through the same render. The host is told so the next paint draws the same view.
      realOn = !realOn;
      target.setAttribute('aria-pressed', realOn ? 'true' : 'false');
      pending = {};
      renderOpenRows();
      vscode.postMessage({ type: 'realText', on: realOn });
      return;
    }
    if (target.id === 'keep') { decide(1); return; }
    if (target.id === 'drop') { decide(0); return; }
    if (target.id === 'clear') { selected = {}; paint(); return; }
  });
${zoomScript()}
${toneScript()}

  paint();
  // A page drawn with the view on and rows open asks for whatever those rows do not yet hold.
  renderOpenRows();

  // The keyboard gets back the tab it activated. An id this module composed, never a key from the
  // database: the host says WHICH press, the page works out which element that is.
  var giveFocusBack = ${JSON.stringify(focusId(view))};
  if (giveFocusBack) {
    var wanted = document.getElementById(giveFocusBack);
    if (wanted && wanted.focus) { wanted.focus(); }
  }

  vscode.postMessage({ type: 'ready' });
}());
</script>
</body>
</html>`;
}
