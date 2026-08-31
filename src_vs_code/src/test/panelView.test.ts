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
  openSections: ['reviewers', 'language', 'gate', 'limits', 'keys', 'server', 'rounds'],
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

test('the picker is a SELECT with every model visible, never a filtering datalist', () => {
  const html = panelHtml(state(), 'n0nce');
  assert.ok(!html.includes('<datalist'), 'a datalist filters by the current value and reads as empty');
  assert.ok(html.includes('another model…'), 'the list is a convenience, never a limit');
  assert.ok(html.includes("the CLI's default"), 'and empty is a first-class choice');
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

test('every setting carries a "?" that explains it', () => {
  const html = panelHtml(state(), 'n');
  const markers = html.match(/class="help"/g) ?? [];
  assert.ok(markers.length >= 10, `every labelled setting explains itself, found ${markers.length}`);
  // "Per vendor" is the one that provoked this: the label alone says nothing.
  assert.ok(html.includes('Rate limits are per vendor'), 'and the explanation says WHY, not just what');
});

test('the keys section answers "do I need this?" before showing the field', () => {
  const noKeys = panelHtml(state(), 'n');
  assert.ok(noKeys.includes('Nothing to fill in yet'), 'codex and gemini sign in through their own CLIs');
  assert.ok(noKeys.includes('not needed yet'));

  const needsKeys = panelHtml(
    state({
      vendors: [
        { id: 'codex', runtime: 'codex', model: '', enabled: true, baseUrl: '' },
        { id: 'deepseek', runtime: 'codex', model: '', enabled: true, baseUrl: 'https://api.deepseek.com/v1' },
      ],
    }),
    'n',
  );
  assert.ok(!needsKeys.includes('Nothing to fill in yet'));
  assert.ok(needsKeys.includes('deepseek'), 'and it names who needs one');
  assert.ok(needsKeys.includes('Enable Code Access'), 'and how to mint it');
});

test('a disabled vendor with an endpoint does not demand a key', () => {
  const html = panelHtml(
    state({ vendors: [{ id: 'deepseek', runtime: 'codex', model: '', enabled: false, baseUrl: 'https://x/v1' }] }),
    'n',
  );
  assert.ok(html.includes('Nothing to fill in yet'), 'a reviewer that does not run needs nothing');
});

test('the server line is body text, not a footnote', () => {
  const css = panelHtml(state(), 'n').split('</style>')[0] ?? '';
  assert.ok(!/\.status \{[^}]*font-size/.test(css), 'it states a fact and reads at the same size as one');
});

test('claude is offered as a translator and as a reviewer preset', () => {
  assert.ok(panelHtml(state(), 'n').includes('Claude, a small model'));
  const claude = panelHtml(
    state({ vendors: [{ id: 'claude', runtime: 'claude', model: 'haiku', enabled: true, baseUrl: '' }] }),
    'n',
  );
  assert.ok(claude.includes('value="haiku"'));
  assert.ok(claude.includes('aliases the Claude CLI resolves'));
});

test('what changes is open; what is set once is folded away', () => {
  // The fixture opens everything, so this asks the renderer for the real defaults.
  const html = panelHtml(state({ openSections: [] }), 'n');
  const openSections = [...html.matchAll(/data-section="([a-z]+)" open/g)].map((m) => m[1]);
  assert.deepEqual(openSections, [], "the panel opens as a list of headings, not a wall");
  for (const folded of ['reviewers', 'language', 'gate', 'limits', 'keys', 'server', 'rounds']) {
    assert.ok(html.includes(`data-section="${folded}"`), `${folded} is present`);
    assert.ok(!html.includes(`data-section="${folded}" open`), `${folded} starts folded`);
  }
});

test('a section the person opened stays open through a repaint', () => {
  const html = panelHtml(state({ openSections: ['limits'] }), 'n');
  assert.ok(html.includes('data-section="limits" open'));
  assert.ok(!html.includes('data-section="rounds" open'), 'their choice is the whole set');
});

test('a waiting question is never collapsible', () => {
  const html = panelHtml(
    state({
      questions: [
        { id: 'q1', sessionId: 's', repoPath: 'r', branch: 'b', question: 'Ship?', openFindings: [], askedUtc: 'now' },
      ],
    }),
    'n',
  );
  const heading = html.indexOf('waiting on you');
  assert.ok(heading >= 0);
  assert.ok(
    heading < html.indexOf('<details'),
    'it stands before every collapsible section — a blocked round is not tidied away behind an arrow',
  );
});

test('the accordion reports its own toggles, so the open set survives', () => {
  assert.ok(panelHtml(state(), 'n').includes("type: 'section'"));
});

test('nothing sits against the edge of the view', () => {
  const css = panelHtml(state(), 'n').split('</style>')[0] ?? '';
  const body = css.match(/body \{[^}]*\}/)?.[0] ?? '';
  assert.ok(/padding: 4px 14px 20px 12px/.test(body), 'air down both sides, wider on the scrollbar side');
});

test('the disclosure arrow is a drawn chevron, not a punctuation mark', () => {
  const css = panelHtml(state(), 'n').split('</style>')[0] ?? '';
  assert.ok(css.includes('border-right: 1.5px solid currentColor'), 'drawn, so it scales with the text');
  assert.ok(!css.includes('203A'), 'a glyph rendered a third of the size nobody can hit');
  assert.ok(css.includes('rotate(45deg)'), 'and it turns when the section opens');
});

test('every vendor has a green run button next to remove', () => {
  // The operator asked for a play triangle between the name and remove: it opens that vendor's
  // own CLI, which is where an account is checked and a signed-out CLI is signed in.
  const html = panelHtml(state(), 'n');
  assert.ok(html.includes('data-command="runVendor" data-id="codex"'));
  assert.ok(html.includes('▶'));
  assert.ok(html.includes('var(--vscode-charts-green)'), 'green from the theme, not a hex of ours');
  assert.ok(
    html.indexOf('data-command="runVendor" data-id="codex"') <
      html.indexOf('data-command="removeVendor" data-id="codex"'),
    'it sits between the name and remove',
  );
});

test('the live regions are addressable, so an update need not reload the panel', () => {
  // The dropdowns closing after two seconds was a full webview reload on every watcher tick.
  // Patching these two containers is what replaced it.
  const html = panelHtml(state(), 'n');
  assert.ok(html.includes('id="live-questions"'));
  assert.ok(html.includes('id="live-rounds"'));
});

test('a running round shows its status, its reviewers and what it has cost', () => {
  const html = panelHtml(
    state({
      sessions: [
        {
          state: {
            sessionId: 's1',
            repoPath: 'D:/repo',
            branch: 'feature/x',
            stage: 'CodeReview',
            awaitingResolve: false,
          },
          rounds: [
            {
              stage: 'CodeReview',
              number: 1,
              verdict: 'running',
              gatingCount: 0,
              reviewers: '1 of 2 answered, 1 running',
              completedUtc: '2026-08-31T12:00:00Z',
              status: 'running',
              startedUtc: '2026-08-31T12:00:00Z',
              reviewerStates: [
                { provider: 'codex', role: 'Architecture', status: 'done', findings: 2, note: '' },
                { provider: 'claude', role: 'Architecture', status: 'running', findings: 0, note: '' },
              ],
              tokensIn: 5300,
              tokensOut: 260,
              costUsd: null,
            },
          ],
        },
      ],
    }),
    'n',
  );

  assert.ok(html.includes('badge running'));
  assert.ok(html.includes('codex/Architecture — done (2 findings)'));
  assert.ok(html.includes('5.3k in / 260 out'));
  assert.ok(html.includes('no cost reported'));
});
