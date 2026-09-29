#!/usr/bin/env node
/**
 * Render a ConnectOtherAIs page as it looks — its real html, its own script, demo state — in headless
 * Chromium, and save a PNG.
 *
 * <p>Why: the page tests run each page's script in a DOM shim, and a shim has no layout. A column that runs
 * to the window's edge, a note that did not grow, two buttons drawn as bars — only a browser shows them. It
 * is also how the README's screenshots are made, since no running editor can be captured from here
 * (`todo/PLAN_every_page_reads_alike.md`, D7). Not part of `npm test`: CI has no browser.</p>
 *
 * <p>The page's styles read VS Code's theme variables, which exist only inside the editor, so a Dark
 * Modern token set is supplied; `acquireVsCodeApi` is stubbed so the page's own script runs (the Settings
 * tab opens its held pane that way).</p>
 *
 *   npm run compile && node scripts/render-page.mjs <page> <out.png> [width] [height] [--size n] [--browser path]
 *
 * <page>: sidebar · settings[:<tab>] · commands · roles · presets
 */
import { createRequire } from 'node:module';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import { browserOrExit, screenshot } from './browserLayout.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const out = join(here, '..', 'out');
const require = createRequire(import.meta.url);
const from = (module) => require(join(out, module));

/** VS Code's Dark Modern theme, as much of it as these pages read. */
const THEME = {
  'font-family': '-apple-system, BlinkMacSystemFont, "Segoe WPC", "Segoe UI", sans-serif',
  'font-size': '13px',
  'editor-font-family': 'Consolas, "Courier New", monospace',
  foreground: '#cccccc',
  descriptionForeground: '#9d9d9d',
  errorForeground: '#f85149',
  focusBorder: '#0078d4',
  'editor-background': '#1f1f1f',
  'editor-foreground': '#cccccc',
  'sideBar-background': '#181818',
  'panel-border': '#2b2b2b',
  'input-background': '#313131',
  'input-foreground': '#cccccc',
  'input-border': '#3c3c3c',
  'button-background': '#0078d4',
  'button-foreground': '#ffffff',
  'button-hoverBackground': '#026ec1',
  'button-secondaryBackground': '#313131',
  'button-secondaryForeground': '#cccccc',
  'textLink-foreground': '#4daafc',
  'textCodeBlock-background': '#2b2b2b',
  'badge-background': '#616161',
  'badge-foreground': '#f8f8f8',
  'editorWarning-foreground': '#cca700',
  'inputValidation-errorBorder': '#be1100',
  'inputValidation-warningBorder': '#b89500',
  'charts-green': '#89d185',
  'charts-blue': '#3794ff',
  'charts-yellow': '#cca700',
  'charts-orange': '#d18616',
  'charts-purple': '#b180d7',
  'charts-red': '#f14c4c',
  'list-hoverBackground': '#2a2d2e',
  'widget-border': '#313131',
};

const NONCE = 'render';

/** The page's html, with the theme and a stubbed editor API in front of its own script. */
function dressed(html) {
  const tokens = Object.entries(THEME).map(([name, value]) => `--vscode-${name}: ${value};`).join(' ');
  const head = `<style>:root { ${tokens} } body { background: var(--vscode-editor-background); }</style>`
    + `<script nonce="${NONCE}">window.acquireVsCodeApi = () => ({ postMessage() {}, getState() {}, setState() {} });</script>`;

  return html.replace('<head>', `<head>${head}`).replace(/<body(\s|>)/, '<body class="vscode-dark"$1');
}

function page(name, size) {
  const [which, tab] = name.split(':');
  const { panelState } = from('test/panelPageHarness.js');
  const text = { uiScale: size, textTone: 0 };
  switch (which) {
    case 'sidebar': {
      const open = ['notifications', 'rounds', 'consultations', 'phrases', 'bugz'];
      return from('panelView.js').panelHtml({ ...panelState(''), ...text, openSections: open }, NONCE);
    }
    case 'settings':
      return from('panelView.js').settingsHtml({ ...panelState(''), ...text }, NONCE, tab ?? 'reviewers');
    case 'commands':
      return from('commandsPage.js').commandsHtml({ rows: [], texts: {}, serverVersion: '', perSide: false, ...text }, NONCE);
    case 'roles':
      return from('rolesPage.js').rolesHtml({ rows: [], texts: {}, serverVersion: '', perSide: false, ...text }, NONCE);
    case 'presets':
      return from('chatPresetsPage.js').chatPresetsHtml({ prompts: [], models: [], providers: [], unreadable: [], ...text }, NONCE);
    default:
      console.log(`unknown page "${name}" — sidebar, settings[:<tab>], commands, roles or presets`);
      process.exit(2);
  }
}

const [name, target, width = '1200', height = '900'] = process.argv.slice(2).filter((a, i, all) => !a.startsWith('--') && !all[i - 1]?.startsWith('--'));
if (name === undefined || target === undefined) {
  console.log('usage: node scripts/render-page.mjs <page> <out.png> [width] [height] [--size n] [--browser path]');
  process.exit(2);
}
const sizeAt = process.argv.indexOf('--size');
const size = sizeAt >= 0 ? Number(process.argv[sizeAt + 1]) : 0;
const shot = screenshot(browserOrExit(), 'coai-render-', {
  html: dressed(page(name, size)), width: Number(width), height: Number(height), out: resolve(target),
});
console.log(shot.ok ? `wrote ${resolve(target)}` : `no picture: ${shot.said}`);
process.exit(shot.ok ? 0 : 1);
