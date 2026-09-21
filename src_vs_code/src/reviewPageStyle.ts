import { HIGHLIGHT_CSS } from './codeHighlight';
import { tabCss } from './tabStrip';
import { TONE_CSS, toneStyle } from './textTone';
import { ZOOM_CSS, zoomStyle } from './zoomControl';

/**
 * The review page's stylesheet.
 *
 * <p>Extracted when story 3.3 needed a dozen lines in `bugzReviewPage.ts` and the file was at 791 of
 * the 800-line cap. Ninety-four lines of CSS that no logic reads are the cheapest thing to move and
 * the one that leaves the page more readable rather than merely shorter — `notificationsPageStyle.ts`
 * is the same move on the same grounds, which is why this has its shape.</p>
 *
 * <p>Nothing here is interpolated: it is one constant, so a reader of the page sees one name where
 * ninety-four lines were, and a reader of the style sees only style.</p>
 */

/**
 * Every rule the review page draws itself with.
 *
 * <p>A FUNCTION rather than a constant, because the sheet composes four other modules' CSS and
 * two of them take a value — the zoom and the tone a person set. Extracting it as a constant
 * was the first attempt and the compiler caught it at once: the page stopped using four
 * imports it still needed.</p>
 */
export function reviewPageCss(uiScale: number, textTone: number): string {
  return `  *, *::before, *::after { box-sizing: border-box; }
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
  /* The two ways to the code sit on one line: two quiet buttons, a dot between, and beside each the
     sentence that says why it is not offered or what the last press learned. */
  .open button.quiet { padding: 0; }
  .open .why { opacity: .7; font-style: italic; margin-left: 6px; }
  .open .sep { opacity: .5; margin: 0 8px; }
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
`;
}
