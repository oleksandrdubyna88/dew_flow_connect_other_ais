import assert from 'node:assert/strict';
import { test } from 'node:test';
import { escapeHtml, panelHtml, PanelState } from '../panelView';
import { DEFAULTS } from '../settingsShape';

const state = (over: Partial<PanelState> = {}): PanelState => ({
  settings: DEFAULTS,
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

test('every vendor gets a checkbox and a model field', () => {
  const html = panelHtml(state(), 'n0nce');
  for (const provider of ['codex', 'gemini', 'deepseek']) {
    assert.ok(html.includes(`data-setting="provider.${provider}"`), `${provider} can be switched`);
    assert.ok(html.includes(`data-setting="model.${provider}"`), `${provider} takes a model`);
  }
});

test('the enabled vendors are the checked ones', () => {
  const html = panelHtml(state({ settings: { ...DEFAULTS, providers: ['gemini'] } }), 'n0nce');
  const gemini = html.slice(html.indexOf('data-setting="provider.gemini"'));
  assert.ok(gemini.startsWith('data-setting="provider.gemini" checked'));
  const codex = html.slice(html.indexOf('data-setting="provider.codex"'));
  assert.ok(!codex.startsWith('data-setting="provider.codex" checked'));
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

test('the install button says what it will do', () => {
  assert.ok(panelHtml(state(), 'n').includes('Install the MCP server…'));
  assert.ok(
    panelHtml(state({ serverInstalled: true, serverVersion: '0.3.0' }), 'n').includes('Update the MCP server…'),
  );
  assert.ok(panelHtml(state({ serverInstalled: true, serverVersion: '0.3.0' }), 'n').includes('0.3.0'));
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
