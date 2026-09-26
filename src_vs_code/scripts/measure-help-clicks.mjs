/**
 * A "?" clicked in a real browser flips nothing — research/PLAN_consult_limits_kinds_and_help.md.
 *
 * <p>A click anywhere inside a `<label>` activates the control it labels, so a "?" inside a checkbox's
 * label turned the setting on or off when somebody only wanted to read what it does. Whether a click
 * DOES that is a browser's behaviour, which the DOM shim the page tests run on does not model — so this
 * renders the whole panel from `panelHtml`, clicks every "?" on it in Chromium, and reads back which
 * checkboxes changed. A new behavioural assertion over the page's text is refused (`.agents/PROJECT.md`);
 * this is the page run.</p>
 *
 * <p>It proves it can see the defect before it trusts a clean answer: the page also carries one
 * deliberately wrong control — a "?" inside its checkbox's label — and the run fails unless THAT one
 * flips. The ask of `.agents/PROJECT.md`: what would the assertion see if the behaviour were deleted.</p>
 *
 * <p>Run it after `npm run compile`: `node scripts/measure-help-clicks.mjs`. Exit code 0 when no real
 * checkbox flips and the self-test does. It needs Edge (or pass `--browser <path>` to any Chromium). Not
 * part of `npm test`: CI's runners have no browser to measure with.</p>
 */
import { browserOrExit, measured } from './browserLayout.mjs';

const OUT = new URL('../out/', import.meta.url).href;
const { panelHtml } = await import(`${OUT}panelView.js`);
const { DEFAULTS } = await import(`${OUT}settingsShape.js`);
const { DEFAULT_VENDORS } = await import(`${OUT}vendors.js`);
const { SNIPPET_VERSION } = await import(`${OUT}claudeSnippet.js`);

const BROWSER = browserOrExit();
const NONCE = 'n';

/** Every section open, so every "?" the panel can show is on the page. */
const STATE = {
  settings: DEFAULTS, vendors: DEFAULT_VENDORS, agyModels: [], codexModels: [], localEngines: {},
  server: { kind: 'absent', version: '', remembered: false, updateOffered: false }, side: 'Windows', perSide: false,
  questions: [], sessions: [],
  openSections: ['reviewers', 'language', 'prompts', 'gate', 'limits', 'keys', 'server', 'usage', 'rounds', 'consultant', 'side'],
  usage: [], usageWindow: 'week', cliStatus: {}, modelPrices: {},
  snippetStatus: { kind: 'current', current: SNIPPET_VERSION }, latestServerVersion: '',
};

/** The wrong shape on purpose: a "?" INSIDE its checkbox's label. This one must flip. */
const CONTROL = '<div id="control"><label class="check"><input type="checkbox" data-setting="selfTest"> The self-test'
  + '<span class="help" title="x">?</span></label></div>';

/** The webview's API, stubbed so the panel's own script can run; then the clicks. Carries the page's nonce. */
const STUB = `<script nonce="${NONCE}">window.acquireVsCodeApi = () => ({ postMessage() {}, getState() {}, setState() {} });</script>`;
const PROBE = `<pre id="result"></pre><script nonce="${NONCE}">
  const result = document.getElementById('result');
  try {
    const boxes = [...document.querySelectorAll('input[type="checkbox"]')];
    const before = boxes.map((box) => box.checked);
    const helps = [...document.querySelectorAll('.help')];
    for (const help of helps) help.click();
    const flipped = boxes.filter((box, i) => box.checked !== before[i]).map((box) => box.dataset.setting || box.id || '?');
    result.textContent = JSON.stringify({
      helps: helps.length,
      flipped: flipped.filter((name) => name !== 'selfTest'),
      selfTestFlipped: flipped.includes('selfTest'),
    });
  } catch (reason) {
    result.textContent = JSON.stringify({ helps: 0, flipped: [], selfTestFlipped: false, error: String(reason) });
  }
</script>`;

const panel = panelHtml(STATE, NONCE);
const html = panel.replace('<body>', `<body>${STUB}`).replace('</body>', `${CONTROL}${PROBE}</body>`);
const [page] = measured(BROWSER, 'coai-help-clicks-', [{ width: 420, html }]);

const { verdict } = page;
console.log(JSON.stringify(verdict ?? page, null, 2));
const ok = verdict !== undefined && verdict.selfTestFlipped === true && verdict.helps >= 20 && verdict.flipped.length === 0;
if (verdict !== undefined && verdict.selfTestFlipped !== true) {
  console.log('the self-test did not flip: this run cannot see the defect, so a clean answer proves nothing');
}
process.exitCode = ok ? 0 : 1;
