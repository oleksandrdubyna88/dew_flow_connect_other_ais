/**
 * A code block in a chat answer, MEASURED in a real layout engine — issue #537.
 *
 * <p>The unit tests ask the parsed stylesheet's cascade which `white-space` and `overflow-wrap` win on
 * a `pre` inside an answer. Whether that is what a person SEES — no line cut at the edge, nothing to
 * scroll sideways — is a layout question, so this renders the chat page's own stylesheet around the
 * markup an answer really sits in, with one answer from `renderAnswer`, at several widths, and reads the
 * boxes back:</p>
 * <ul>
 *   <li>the code block does not overflow its own box (`scrollWidth ≤ clientWidth`);</li>
 *   <li>the conversation does not overflow sideways either;</li>
 *   <li>a wide TABLE still scrolls inside its own box — the half of the old decision #537 kept.</li>
 * </ul>
 *
 * <p>Run it after `npm run compile`: `node scripts/measure-answer-code-layout.mjs`. Exit code 0 when
 * every width holds. It needs Edge (or pass `--browser <path>` to any Chromium). Not part of
 * `npm test`: CI's runners have no browser to measure with.</p>
 */
import { browserOrExit, measured } from './browserLayout.mjs';

// From the script's own place, so it runs from any directory.
const OUT = new URL('../out/', import.meta.url).href;
const { chatPageHtml } = await import(`${OUT}chatPage.js`);
const { renderAnswer } = await import(`${OUT}renderAnswer.js`);

const BROWSER = browserOrExit();
const WIDTHS = [320, 700, 1000];

/** The answer from the issue's screenshot, plus the two shapes that defeat a wrap at spaces. */
const ANSWER = [
  '## Промпт для ответа',
  '',
  '```',
  '1. §11: Да, переводи 4xx в WARNING (или INFO для 404), оставь ERROR/CRITICAL исключительно для 5xx.',
  '2. §10 (Terraform): Настрой ignore_changes в манифестах Terraform на удаляемые ревизии, остальное не трогай.',
  '3. C:\\Users\\somebody\\AppData\\Local\\coai-mcp\\prompts\\a-very-long-role-name-general-prompt-file-that-never-ends.md',
  '4. https://generativelanguage.googleapis.com/v1beta/models/gemini-3-pro-preview-0925:streamGenerateContent?alt=sse',
  '```',
  '',
  // Wider than the widest page measured, and with no spaces for a cell to wrap at.
  '| a_very_long_column_heading_without_spaces | another_very_long_column_heading_without_spaces | '
    + 'a_third_very_long_column_heading_without_spaces | and_a_fourth_one_so_it_is_wider_than_a_thousand_pixels |',
  '|---|---|---|---|',
  '| cell | cell | cell | cell |',
].join('\n');

const STATE = {
  id: 'x', title: 't', passage: '', messages: [], models: [], providers: [], reask: '', canRetry: false,
  attached: '', spend: '', promptPresets: [], modelPresets: [], providerId: '', chosenModelId: '', fromSession: false,
  asked: [], carryFrom: 0, modelId: '', running: false, capped: false, turn: 0, waiting: [], failure: '', draft: '',
  marks: { role: '', task: '', service: [] }, uiScale: 0, textTone: 0,
};

const PROBE = `<pre id="result"></pre><script>
  const result = document.getElementById('result');
  try {
    const code = document.querySelector('.msg .what pre');
    const table = document.querySelector('.msg .what table');
    const scroll = document.getElementById('scroll');
    // + 1: scrollWidth and clientWidth are rounded separately, so a box that fits can read one pixel over.
    result.textContent = JSON.stringify({
      codeFits: code.scrollWidth <= code.clientWidth + 1,
      pageFits: scroll.scrollWidth <= scroll.clientWidth + 1,
      tableScrollsInItsBox: table.scrollWidth > table.clientWidth,
      code: [code.clientWidth, code.scrollWidth], page: [scroll.clientWidth, scroll.scrollWidth],
    });
  } catch (reason) {
    // Markup the measurement no longer recognises is a FAILED case with its reason, not a crash.
    result.textContent = JSON.stringify({ codeFits: false, pageFits: false, error: String(reason) });
  }
</script>`;

/** The page's own stylesheet: the same `<style>` the chat tab ships, cut out of a whole page. */
const PAGE_CSS = chatPageHtml(STATE, 'n').split('<style>')[1].split('</style>')[0];

/**
 * The chat tab's stylesheet around the chain an answer really sits in — `main#scroll > div#messages >
 * div.msg.model > div.what` — with one answer from `renderAnswer` and the probe.
 *
 * <p>Built rather than cut out of the whole page, the way `measure-failure-layout.mjs` builds its own:
 * stripping the page's script and CSP with regular expressions is HTML filtering CodeQL rightly refuses
 * (PR #539), and the page's script cannot run outside VS Code anyway.</p>
 */
function pageFor(width) {
  return `<!doctype html><html lang="en"><head><meta charset="utf-8"><style>${PAGE_CSS}
body { width: ${width}px; margin: 0; }</style></head><body>
<main id="scroll"><div id="messages"><div class="msg model"><div class="what">${renderAnswer(ANSWER)}</div></div></div></main>
${PROBE}</body></html>`;
}

const results = measured(BROWSER, 'coai-answer-code-layout-', WIDTHS.map((width) => ({ width, html: pageFor(width) })));
let ok = true;
WIDTHS.forEach((width, index) => {
  const { verdict, said } = results[index];
  const held = verdict !== undefined && verdict.codeFits && verdict.pageFits && verdict.tableScrollsInItsBox;
  ok = ok && held;
  console.log(`${String(width).padStart(5)}px  ${held ? 'HELD  ' : 'FAILED'} ${verdict === undefined ? said : JSON.stringify(verdict)}`);
});
console.log(ok ? 'THE CODE BLOCK WRAPS AND THE TABLE SCROLLS IN ITS OWN BOX, AT EVERY WIDTH' : 'SOMETHING OVERFLOWED');
process.exit(ok ? 0 : 1);
