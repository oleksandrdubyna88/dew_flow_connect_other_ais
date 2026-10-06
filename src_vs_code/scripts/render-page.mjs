#!/usr/bin/env node
/**
 * Render a ConnectOtherAIs page as it looks — its real html, its own script, demo state — in headless
 * Chromium, and save a PNG.
 *
 * <p>Why: the page tests run each page's script in a DOM shim, and a shim has no layout. A column that runs
 * to the window's edge, a note that did not grow, two buttons drawn as bars — only a browser shows them. It
 * is also how the README's screenshots are made, since no running editor can be captured from here
 * (`research/PLAN_every_page_reads_alike.md`, D7). Not part of `npm test`: CI has no browser.</p>
 *
 * <p>The page's styles read VS Code's theme variables, which exist only inside the editor, so a Dark
 * Modern token set is supplied; `acquireVsCodeApi` is stubbed so the page's own script runs (the Settings
 * tab opens its held pane that way).</p>
 *
 *   npm run compile && node scripts/render-page.mjs <page> <out.png> [width] [height] [--size n] [--browser path]
 *
 * <page>: sidebar · sidebar-question · settings[:<tab>] · security · commands · roles · presets
 *
 * Width: headless Chromium lays a page out at no less than about 500px and CROPS a narrower screenshot, so
 * a 340px sidebar comes out cut at the right edge rather than wrapped. Render the sidebar at 500.
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

/**
 * Light Modern, for `--theme light` (todo/PLAN_one_model_catalog.md E3.1): the same names as {@link THEME}, so a page
 * drawn on the editor's own variables can be seen in both before it ships.
 */
const LIGHT = {
  ...THEME,
  foreground: '#3b3b3b',
  descriptionForeground: '#6f6f6f',
  errorForeground: '#c72e0f',
  focusBorder: '#005fb8',
  'editor-background': '#ffffff',
  'editor-foreground': '#3b3b3b',
  'sideBar-background': '#f8f8f8',
  'panel-border': '#e5e5e5',
  'input-background': '#ffffff',
  'input-foreground': '#3b3b3b',
  'input-border': '#cecece',
  'button-background': '#005fb8',
  'button-hoverBackground': '#0258a8',
  'button-secondaryBackground': '#e5e5e5',
  'button-secondaryForeground': '#3b3b3b',
  'textLink-foreground': '#005fb8',
  'textCodeBlock-background': '#f2f2f2',
  'editorWarning-foreground': '#bf8803',
  'list-hoverBackground': '#f2f2f2',
  'widget-border': '#e5e5e5',
  'editorWidget-background': '#f8f8f8',
  'testing-iconPassed': '#388a34',
};

const NONCE = 'render';

/** Something happening, for the sidebar's picture: one round running, one consultation being had, a plan the cadence follows, three phrases. */
function DEMO_SIDEBAR() {
  const minutesAgo = (n) => new Date(Date.now() - n * 60_000).toISOString();
  const round = {
    stage: 'CodeReview', number: 1, verdict: '', gatingCount: 0, reviewers: '3 of 4 reviewers answered', status: 'running',
    startedUtc: minutesAgo(4), completedUtc: '', subject: 'SCOPE — the sidebar keeps what is happening now',
    reviewerStates: [
      { provider: 'codex', role: 'Architecture', status: 'done', findings: 2, note: '', seconds: 71 },
      { provider: 'codex', role: 'Conventions', status: 'done', findings: 0, note: '', seconds: 64 },
      { provider: 'antigravity', role: 'SecurityReliability', status: 'done', findings: 1, note: '', seconds: 88 },
      { provider: 'antigravity', role: 'UxDxPerformance', status: 'running', findings: 0, note: '' },
    ],
  };
  const consultation = {
    id: 'c1', callerKind: 'claude', repoPath: 'D:/work/app', branch: 'feat/settings-page', vendor: 'codex', model: 'gpt-6-luna',
    status: 'asking', startedUtc: minutesAgo(2), updatedUtc: minutesAgo(1), maxTurns: 3, alert: '', reason: 'why the key never moves',
    kind: 'stuck', plan: '', epics: '',
    turns: [{ utc: minutesAgo(2), problem: 'why the key never moves', advice: '', seconds: 40, tokensIn: 12000, tokensOut: 800, costUsd: null }],
  };

  return {
    sessions: [{ state: { sessionId: 's1', repoPath: 'D:/work/app', branch: 'feat/settings-page', stage: 'CodeReview', awaitingResolve: false }, rounds: [round] }],
    consultations: [consultation],
    cadence: [{
      repoPath: 'D:/work/app', branch: 'feat/settings-page',
      answer: {
        plan: 'todo/PLAN_settings_page.md', mode: 'remind', epics: 5, epicsClosed: [1, 2],
        groups: [{ range: '1-3', consulted: true }], risk: [], riskAnswered: true, unreadable: '',
      },
    }],
    phrases: [
      { id: 'p1', name: 'Explain', text: 'Explain, and what do you think about it?' },
      { id: 'p2', name: 'Your thoughts?', text: 'What would you advise here?' },
      { id: 'p3', name: 'Translate', text: 'Translate, as close to the meaning as you can.' },
    ],
  };
}

/** What the stubbed `getState` answers, by page: the page-local state it restores (the Security lane tab's open folds). */
const SAVED = { security: { seclaneOpen: ['redteam-sql'] } };

/** The page's html, with the theme and a stubbed editor API in front of its own script; `saved` is what getState answers. */
function dressed(html, saved, theme) {
  // Escaped for a script element: no `<` reaches the page, so no value can close the script it is written into.
  const state = JSON.stringify(saved).replaceAll('<', '\\u003c');
  const tokens = Object.entries(theme === 'light' ? LIGHT : THEME).map(([name, value]) => `--vscode-${name}: ${value};`).join(' ');
  // On the ROOT, as VS Code paints a webview: the page itself draws its body transparent.
  const head = `<style>:root { ${tokens} background: var(--vscode-editor-background); color-scheme: ${theme === 'light' ? 'light' : 'dark'}; }</style>`
    + `<script nonce="${NONCE}">window.acquireVsCodeApi = () => ({ postMessage() {}, getState() { return ${state}; }, setState() {} });</script>`;

  return html.replace('<head>', `<head>${head}`).replace(/<body(\s|>)/, `<body class="vscode-${theme === 'light' ? 'light' : 'dark'}"$1`);
}

function page(name, size) {
  const [which, tab] = name.split(':');
  const { panelState } = from('test/panelPageHarness.js');
  const text = { uiScale: size, textTone: 0 };
  switch (which) {
    case 'sidebar': {
      const open = ['notifications', 'rounds', 'consultations', 'cadence', 'phrases', 'bugz'];
      return from('panelView.js').panelHtml({ ...panelState(''), ...text, ...DEMO_SIDEBAR(), openSections: open }, NONCE);
    }
    case 'sidebar-question': {
      // The card a round nobody could answer leaves: the operator's own text of 2026-09-29.
      const question = 'The plan review gate needs your decision: no reviewer answered — nothing was reviewed. 0 of 3 reviewers answered; failed: '
        + 'local/PlanCritique: exit 69: [coai-mcp] the local engine at http://127.0.0.1:11434/v1 did not finish in time - it was still working after 290s of the 290s this reviewer was given., '
        + 'gemini/PlanCritique: exit 1: error: Eligibility check failed: UNAUTHENTICATED (code 401): Request had invalid authentication credentials., '
        + 'codex/PlanCritique: rate limited (after 1 attempt): {"type":"error","message":"You’ve hit your usage limit. Try again at Oct 3rd, 2026 7:…. '
        + 'Proceed anyway, or fix the findings and review again?';
      const questions = [{ id: 'q1', sessionId: 's1', repoPath: 'D:/work/app', branch: 'feat/cadence-own-section', question, openFindings: [], askedUtc: new Date().toISOString() }];
      return from('panelView.js').panelHtml({ ...panelState(''), ...text, questions }, NONCE);
    }
    case 'catalog':
      // The new Settings page, on a place: `catalog`, `catalog:setup/team` (todo/PLAN_one_model_catalog.md E3).
      // The shipped roles and commands, as the host reads them (E4.3, E4.4) — one command of your own, nothing rewritten.
      return from('catalogPage.js').catalogHtml({
        ...panelState(''), ...text,
        roles: { rows: [], texts: {}, serverVersion: '', perSide: false, stranded: [] },
        commands: { rows: [{ id: 'cmd-ab12', title: 'Run the linter', stage: 'code', enabled: true }], texts: {}, serverVersion: '', perSide: false },
        // Two chat models that can answer, one that cannot, two prompts (E4.6b).
        chat: {
          prompt: 'Explain', promptChoice: '', language: 'en', autoSend: 'keyboard', model: 'chat-deep', modelName: '',
          prompts: [{ id: 'p1', name: 'Explain', text: 'Explain', main: true }, { id: 'p2', name: 'Review', text: 'Review this for bugs.', main: false }],
          models: [
            { id: 'chat-deep', name: 'Deep', runtime: 'claude', model: 'opus', main: false, executablePath: '', baseUrl: '', startingPrompt: 'You review APIs.' },
            { id: 'chat-fast', name: 'Fast', runtime: 'claude', model: 'sonnet', main: false, executablePath: '', baseUrl: '' },
            { id: 'chat-gpu', name: 'On my GPU', runtime: 'local', model: 'qwen', main: false, executablePath: '', baseUrl: '' },
          ],
        },
      }, NONCE, tab ?? 'models');
    case 'settings':
      return from('panelView.js').settingsHtml({ ...panelState(''), ...text }, NONCE, tab ?? 'reviewers');
    case 'security':
      return securityDemo(text);
    case 'commands':
      return from('commandsPage.js').commandsHtml({ rows: [], texts: {}, serverVersion: '', perSide: false, ...text }, NONCE);
    case 'roles':
      return from('rolesPage.js').rolesHtml({ rows: [], texts: {}, serverVersion: '', perSide: false, ...text }, NONCE);
    case 'presets':
      return from('chatPresetsPage.js').chatPresetsHtml({ prompts: [], models: [], providers: [], unreadable: [], ...text }, NONCE);
    default:
      console.log(`unknown page "${name}" — sidebar, settings[:<tab>], catalog[:<place>], security, commands, roles or presets`);
      process.exit(2);
  }
}

/**
 * The Security lane tab with every card state on it (research/PLAN_the_security_tab_reads_at_a_glance.md, epic 4): an edited
 * preset (override text), a preset whose conditions changed, a custom prompt with no text yet, pairs including general,
 * and one conditions fold opened the way a person's toggle leaves it in the page's own state.
 */
function securityDemo(text) {
  const { panelState } = from('test/panelPageHarness.js');
  const { DEFAULTS } = from('settingsShape.js');
  const { securityLaneFrom } = from('securityLane.js');
  const lane = securityLaneFrom({
    enabled: true,
    prompts: [{ id: 'redteam-sql', triggers: ['sql', 'xss'] }, { id: 'redteam-mine', triggers: [], focus: [] }],
    runs: [{ vendor: 'codex', prompt: 'redteam-general' }, { vendor: 'codex', prompt: 'redteam-authz' }, { vendor: 'antigravity', prompt: 'redteam-mine' }],
  });
  return from('panelView.js').settingsHtml({
    ...panelState(''), ...text, settings: { ...DEFAULTS, securityLane: lane },
    server: { kind: 'known', version: '0.43.0', remembered: false, updateOffered: false },
    securityPromptText: { 'redteam-authz': 'written', 'redteam-mine': 'none' },
    securityPromptDir: 'C:/Users/me/AppData/Roaming/coai/prompts',
  }, NONCE, 'securityLane');
}

const [name, target, width = '1200', height = '900'] = process.argv.slice(2).filter((a, i, all) => !a.startsWith('--') && !all[i - 1]?.startsWith('--'));
if (name === undefined || target === undefined) {
  console.log('usage: node scripts/render-page.mjs <page> <out.png> [width] [height] [--size n] [--theme light|dark] [--browser path]');
  process.exit(2);
}
const sizeAt = process.argv.indexOf('--size');
const size = sizeAt >= 0 ? Number(process.argv[sizeAt + 1]) : 0;
const themeAt = process.argv.indexOf('--theme');
const theme = themeAt >= 0 ? process.argv[themeAt + 1] : 'dark';
const shot = screenshot(browserOrExit(), 'coai-render-', {
  html: dressed(page(name, size), SAVED[name.split(':')[0]] ?? null, theme), width: Number(width), height: Number(height), out: resolve(target),
});
console.log(shot.ok ? `wrote ${resolve(target)}` : `no picture: ${shot.said}`);
process.exit(shot.ok ? 0 : 1);
