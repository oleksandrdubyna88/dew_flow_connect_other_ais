/**
 * The failure line MEASURED in a real layout engine — issue #346.
 *
 * <p>No test in this repository has a layout engine: the unit tests assert the failure's structure (Try
 * again outside the red box) and its parsed declarations (the box wraps anywhere). Whether that is what a
 * person SEES is a layout question, so this renders the page's own stylesheet and the markup
 * `chatFailureHtml` ships in headless Microsoft Edge, at several widths, with the failures that broke it —
 * a long path, a long URL, a multi-line vendor error — and reads `getBoundingClientRect` back:</p>
 * <ul>
 *   <li>the text stays inside its red box (its right edge is not past the box's);</li>
 *   <li>the button sits to the right of the box and does not overlap it.</li>
 * </ul>
 *
 * <p>Run it after `npm run compile`: `node scripts/measure-failure-layout.mjs`. Exit code 0 when every case
 * at every width holds. It needs Edge (or pass `--browser <path>` to any Chromium). Not part of `npm test`:
 * CI's runners have no browser to measure with, and a check that skips itself there is not one.</p>
 */
import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

// From the script's own place, so it runs from any directory. (Our own code review.)
const OUT = new URL('../out/', import.meta.url).href;
const { chatFailureHtml, chatPageHtml } = await import(`${OUT}chatPage.js`);

const at = process.argv.indexOf('--browser');
const BROWSER = at >= 0 ? process.argv[at + 1] : 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
if (!existsSync(BROWSER)) {
  console.log(`no browser at ${BROWSER} — pass --browser <path to a Chromium>`);
  process.exit(2);
}

const FAILURES = {
  'a long path': 'The model could not read C:\\Users\\somebody\\AppData\\Local\\coai-mcp\\prompts\\a-very-long-role-name-general-prompt-file-that-never-ends.md',
  'a long url': 'UNAVAILABLE (code 503): https://generativelanguage.googleapis.com/v1beta/models/gemini-3-pro-preview-0925:streamGenerateContent?alt=sse&key=REDACTED-BUT-LONG',
  'several lines': 'The model stopped.\nstderr: error: unexpected argument\n  --this-is-a-flag-nobody-knows\nUsage: codex exec [OPTIONS]',
};
const WIDTHS = [320, 600, 1000];

/** The page's own stylesheet: the same `<style>` the chat tab ships, cut out of a whole page. */
const pageCss = chatPageHtml({
  id: 'x', title: 't', passage: '', messages: [], models: [], providers: [], reask: '', canRetry: false,
  attached: '', spend: '', promptPresets: [], modelPresets: [], providerId: '', chosenModelId: '', fromSession: false,
  asked: [], carryFrom: 0, modelId: '', running: false, capped: false, turn: 0, waiting: [], failure: 'x', draft: '',
  marks: { role: '', task: '', service: [] }, uiScale: 0, textTone: 0,
}, 'n').split('<style>')[1].split('</style>')[0];

function pageFor(width, failure) {
  return `<!doctype html><html><head><meta charset="utf-8"><style>${pageCss}
body { width: ${width}px; margin: 0; }</style></head><body>
<div id="failure">${chatFailureHtml(failure, true, 1)}</div>
<pre id="result"></pre>
<script>
  const result = document.getElementById('result');
  try {
    const boxEl = document.querySelector('.failure');
    const box = boxEl.getBoundingClientRect();
    const style = getComputedStyle(boxEl);
    // The CONTENT edge, not the border: text that ran into the padding is text out of its box.
    const inner = box.right - parseFloat(style.paddingRight) - parseFloat(style.borderRightWidth);
    const said = document.querySelector('.said').getBoundingClientRect();
    const button = document.querySelector('.retry').getBoundingClientRect();
    const within = said.right <= inner + 0.5 && said.left >= box.left - 0.5;
    // Beside the box AND still on the page: a button pushed past the edge is not beside anything.
    const beside = button.left >= box.right - 0.5 && button.right <= ${width} + 0.5;
    result.textContent = JSON.stringify({ within, beside,
    box: [Math.round(box.left), Math.round(box.right)], said: [Math.round(said.left), Math.round(said.right)],
    button: [Math.round(button.left), Math.round(button.right)] });
  } catch (reason) {
    // Markup the measurement no longer recognises is a FAILED case with its reason, not a crash.
    result.textContent = JSON.stringify({ within: false, beside: false, error: String(reason) });
  }
</script></body></html>`;
}

/** What the page wrote, or nothing — an empty or broken result is a failed case, never a crash. */
function parsed(text) {
  try {
    return JSON.parse(text.replace(/&quot;/g, '"'));
  } catch {
    return undefined;
  }
}

const dir = mkdtempSync(join(tmpdir(), 'coai-failure-layout-'));
let ok = true;
try {
  for (const width of WIDTHS) {
    for (const [name, failure] of Object.entries(FAILURES)) {
      const file = join(dir, `w${width}.html`);
      writeFileSync(file, pageFor(width, failure), 'utf8');
      const run = spawnSync(BROWSER, [
        '--headless=new', '--disable-gpu', `--window-size=${width + 40},800`, '--dump-dom', pathToFileURL(file).href,
      ], { encoding: 'utf8', timeout: 60_000 });
      const found = /<pre id="result">([^<]*)<\/pre>/.exec(run.stdout ?? '');
      const measured = found === null ? undefined : parsed(found[1]);
      const held = measured !== undefined && measured.within && measured.beside;
      ok = ok && held;
      console.log(`${String(width).padStart(5)}px  ${name.padEnd(14)} ${held ? 'HELD  ' : 'FAILED'} ${measured === undefined ? (run.stderr ?? '').slice(0, 200) : JSON.stringify(measured)}`);
    }
  }
} finally {
  rmSync(dir, { recursive: true, force: true });
}
console.log(ok ? 'THE TEXT STAYS IN ITS BOX AND THE BUTTON BESIDE IT, AT EVERY WIDTH' : 'SOMETHING OVERFLOWED');
process.exit(ok ? 0 : 1);
