/**
 * The half every layout MEASUREMENT here shares: a Chromium, a page, and what the page wrote back.
 *
 * <p>Extracted when the second measurement arrived (issue #537, the answer's code block) rather than
 * copied from the first (`measure-failure-layout.mjs`, issue #346). A measurement page runs its own
 * probe script and writes a JSON verdict into `<pre id="result">`; this renders it with
 * `--dump-dom` and reads that element back. An empty or unparseable result is `undefined` — a FAILED
 * case for the caller, never a crash.</p>
 *
 * <p>Not part of `npm test`: CI's runners have no browser to measure with, and a check that skips
 * itself there is not one.</p>
 */
import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

const EDGE = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';

/** The Chromium to measure with — `--browser <path>`, else Edge — or the process ends with exit 2. */
export function browserOrExit(argv = process.argv) {
  const at = argv.indexOf('--browser');
  const browser = at >= 0 ? argv[at + 1] : EDGE;
  if (!existsSync(browser)) {
    console.log(`no browser at ${browser} — pass --browser <path to a Chromium>`);
    process.exit(2);
  }

  return browser;
}

/** What the page wrote, or nothing. */
function parsed(text) {
  try {
    return JSON.parse(text.replace(/&quot;/g, '"').replace(/&amp;/g, '&'));
  } catch {
    return undefined;
  }
}

/**
 * Each page rendered at its width, and the verdict it wrote into `<pre id="result">` — or `undefined`
 * with what the browser said, when there is none.
 *
 * @param {string} browser the Chromium to run
 * @param {string} prefix the temporary directory's name, so a leftover says whose it was
 * @param {{ width: number, html: string }[]} pages
 */
export function measured(browser, prefix, pages) {
  const dir = mkdtempSync(join(tmpdir(), prefix));
  try {
    return pages.map(({ width, html }, index) => {
      const file = join(dir, `page${index}-w${width}.html`);
      writeFileSync(file, html, 'utf8');
      const run = spawnSync(browser, [
        '--headless=new', '--disable-gpu', `--window-size=${width + 40},800`, '--dump-dom', pathToFileURL(file).href,
      ], { encoding: 'utf8', timeout: 60_000 });
      const found = /<pre id="result">([^<]*)<\/pre>/.exec(run.stdout ?? '');

      return { verdict: found === null ? undefined : parsed(found[1]), said: (run.stderr ?? '').slice(0, 200) };
    });
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}
