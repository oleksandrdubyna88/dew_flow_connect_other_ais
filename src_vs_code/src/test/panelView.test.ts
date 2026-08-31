import assert from 'node:assert/strict';
import { test } from 'node:test';
import { escapeHtml, panelHtml, PanelState } from '../panelView';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS } from '../vendors';

const state = (over: Partial<PanelState> = {}): PanelState => ({
  settings: DEFAULTS,
  vendors: DEFAULT_VENDORS,
  codexModels: [
    { id: 'gpt-5.6-sol', label: 'GPT-5.6-Sol' },
    { id: 'gpt-5.4-mini', label: 'GPT-5.4-Mini' },
  ],
  serverInstalled: false,
  serverVersion: '',
  questions: [],
  sessions: [],
  ...over,
});

test('every language and translator is offered', () => {
  const html = panelHtml(state(), 'n0nce');
  for (const label of ['English', 'Español', 'Deutsch', 'Русский', 'Українська']) {
    assert.ok(html.includes(label), `offers ${label}`);
  }
  assert.ok(html.includes('Gemini Flash'));
  assert.ok(html.includes('always show the original'));
});

test('each reviewer gets a switch, a model field and a way out', () => {
  const html = panelHtml(state(), 'n0nce');
  for (const id of ['codex', 'gemini']) {
    assert.ok(html.includes(`data-setting="enabled" data-vendor="${id}"`), `${id} can be switched off`);
    assert.ok(html.includes(`data-setting="model" data-vendor="${id}"`), `${id} takes a model`);
    assert.ok(html.includes(`data-command="removeVendor" data-id="${id}"`), `${id} can be removed`);
  }
  assert.ok(html.includes('data-command="addVendor"'), 'and the list is not meant to stay at two');
});

test('a disabled reviewer is shown unchecked', () => {
  const html = panelHtml(
    state({ vendors: [{ id: 'codex', runtime: 'codex', model: '', enabled: false, baseUrl: '' }] }),
    'n0nce',
  );
  assert.ok(!html.includes('data-vendor="codex" checked'));
});

test("codex offers the CLI's own cached models; gemini offers a curated list", () => {
  const html = panelHtml(state(), 'n0nce');
  assert.ok(html.includes('value="gpt-5.6-sol"'), 'discovered from ~/.codex/models_cache.json');
  assert.ok(html.includes('models the Codex CLI has cached'));
  assert.ok(html.includes('value="gemini-flash-latest"'));
  assert.ok(html.includes('a curated list'), 'curation is admitted, never passed off as discovery');
});

test('a model the person typed stays in its own list', () => {
  const html = panelHtml(
    state({ vendors: [{ id: 'codex', runtime: 'codex', model: 'something-new', enabled: true, baseUrl: '' }] }),
    'n0nce',
  );
  assert.ok(html.includes('value="something-new"'));
  assert.ok(html.includes('something-new (yours)'), 'a saved value never vanishes from its own dropdown');
});

test('a custom endpoint is editable; a first-party vendor shows no URL field', () => {
  const custom = panelHtml(
    state({ vendors: [{ id: 'mistral', runtime: 'codex', model: '', enabled: true, baseUrl: 'https://api.mistral.ai/v1' }] }),
    'n0nce',
  );
  assert.ok(custom.includes('data-setting="baseUrl" data-vendor="mistral"'));
  assert.ok(!panelHtml(state(), 'n0nce').includes('data-setting="baseUrl"'));
});

test('nothing can force the view to scroll sideways', () => {
  const css = panelHtml(state(), 'n0nce').split('</style>')[0] ?? '';
  assert.ok(css.includes('box-sizing: border-box'), 'a 100% field plus padding is wider than its parent');
  assert.ok(css.includes('overflow-x: hidden'));
  assert.ok(!css.includes('white-space: nowrap;\n    width'), 'no fixed widths that a narrow sidebar cannot honour');
});

test('the server actions moved to the title menu, and the panel says so', () => {
  const html = panelHtml(state(), 'n0nce');
  assert.ok(!html.includes('data-command="install"'), 'commands belong in the view menu, not as buttons');
  assert.ok(html.includes('⋯ menu'), 'and the panel points at where they went');
});

test('the script runs only under the given nonce', () => {
  const html = panelHtml(state(), 'abc123');
  assert.ok(html.includes("script-src 'nonce-abc123'"));
  assert.ok(html.includes('<script nonce="abc123">'));
  assert.ok(!html.includes('<script>'), 'a bare script tag would be blocked, and hides the mistake');
});

test('colours come from the theme, never from us', () => {
  const html = panelHtml(state(), 'n0nce');
  assert.ok(html.includes('var(--vscode-foreground)'));
  assert.ok(html.includes('var(--vscode-button-background)'));
  assert.ok(!/#[0-9a-f]{6}/i.test(html.split('<script')[0] ?? ''), 'no hard-coded hex colours');
});

test('the panel says whether the server is installed, and which version', () => {
  assert.ok(panelHtml(state(), 'n').includes('not installed yet'));
  const installed = panelHtml(state({ serverInstalled: true, serverVersion: '0.4.0' }), 'n');
  assert.ok(installed.includes('coai-mcp 0.4.0 is installed'));
});

test('with no question waiting there is no waiting section at all', () => {
  assert.ok(!panelHtml(state(), 'n').includes('waiting on you'));
});

test('an open question is shown with its findings and an answer button', () => {
  const html = panelHtml(
    state({
      questions: [
        {
          id: 'q1',
          sessionId: 's',
          repoPath: 'D:/repo',
          branch: 'feature/x',
          question: 'Ship anyway?',
          openFindings: [
            { severity: 'blocking', category: 'security', file: 'a.cs', line: 1, title: 'token compared with ==' },
          ],
          askedUtc: '2026-08-31T15:00:00Z',
        },
      ],
    }),
    'n',
  );
  assert.ok(html.includes('waiting on you'));
  assert.ok(html.includes('Ship anyway?'));
  assert.ok(html.includes('token compared with =='));
  assert.ok(html.includes('data-command="answer" data-id="q1"'));
});

test('an untranslated question says why, rather than pretending', () => {
  const html = panelHtml(
    state({
      questions: [
        {
          id: 'q1',
          sessionId: 's',
          repoPath: 'r',
          branch: 'b',
          question: 'Ship anyway?',
          openFindings: [],
          askedUtc: 'now',
          translationNote: 'the gemini CLI timed out',
        },
      ],
    }),
    'n',
  );
  assert.ok(html.includes('shown untranslated: the gemini CLI timed out'));
});

test('a question written by someone else cannot inject markup', () => {
  const html = panelHtml(
    state({
      questions: [
        {
          id: 'q1',
          sessionId: 's',
          repoPath: 'r',
          branch: 'b',
          question: '<img src=x onerror="alert(1)">',
          openFindings: [],
          askedUtc: 'now',
        },
      ],
    }),
    'n',
  );
  assert.ok(!html.includes('<img src=x'), 'the question is data, never markup');
  assert.ok(html.includes('&lt;img src=x'));
});

test('rounds are newest first, and an empty history says so', () => {
  assert.ok(panelHtml(state(), 'n').includes('No rounds yet'));
  const html = panelHtml(
    state({
      sessions: [
        {
          state: { sessionId: 's', repoPath: 'r', branch: 'feature/x', stage: 'CodeReview', awaitingResolve: false },
          rounds: [
            { stage: 'PlanReview', number: 1, verdict: 'revise', gatingCount: 3, reviewers: 'all 2', completedUtc: '2026-08-01T00:00:00Z' },
            { stage: 'CodeReview', number: 1, verdict: 'proceed', gatingCount: 0, reviewers: 'all 6', completedUtc: '2026-08-30T00:00:00Z' },
          ],
        },
      ],
    }),
    'n',
  );
  assert.ok(html.indexOf('proceed') < html.indexOf('revise'), 'the newest round leads');
});

test('escapeHtml handles the four characters that matter', () => {
  assert.equal(escapeHtml('<a href="x">&</a>'), '&lt;a href=&quot;x&quot;&gt;&amp;&lt;/a&gt;');
});
