import { SNIPPET_VERSION } from '../claudeSnippet';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { escapeHtml, panelHtml, PanelState } from '../panelView';
import { DEFAULTS } from '../settingsShape';
import { vendorColour } from '../vendorColour';
import { DEFAULT_VENDORS } from '../vendors';

const state = (over: Partial<PanelState> = {}): PanelState => ({
  settings: DEFAULTS,
  vendors: DEFAULT_VENDORS,
  codexModels: [
    { id: 'gpt-5.6-sol', label: 'GPT-5.6-Sol' },
    { id: 'gpt-5.4-mini', label: 'GPT-5.4-Mini' },
  ],
  localEngines: {},
  server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
  side: '',
  questions: [],
  sessions: [],
  openSections: ['reviewers', 'language', 'prompts', 'gate', 'limits', 'keys', 'server', 'usage', 'rounds'],
  usage: [],
  usageWindow: 'week',
  cliStatus: {},
  modelPrices: {},
  snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
  latestServerVersion: '',
  ...over,
});


test('each role shows its own rounds, its own threshold and its own prompts', () => {
  // The Gate and the Prompts sections described one thing between them: how many times this role
  // asks, how much it may still find, and what it asks each time. One box per role now.
  const html = panelHtml(state(), 'n0nce');

  for (const role of ['PlanCritique', 'Architecture', 'SecurityReliability', 'UxDxPerformance']) {
    // `data-role`, not `data-vendor`. These two assertions read `data-vendor` until 2026-09-01 and
    // so passed while neither input could save anything: the provider takes `data-vendor` to mean a
    // VENDOR, hunted for one called `Architecture`, and wrote the vendor list back unchanged. A test
    // that copies the markup can only ever confirm it — the one with teeth is settingWrite.test.ts,
    // which asks where the value LANDS.
    assert.ok(html.includes(`data-setting="rounds" data-role="${role}"`), `${role} has no rounds control`);
    assert.ok(html.includes(`data-setting="thresholds" data-role="${role}"`), `${role} has no threshold control`);
    assert.ok(html.includes(`data-prompt="${role}" data-round="1"`), `${role} has no round-1 prompt`);
  }

  // What is left in The Gate is the one decision that belongs to neither role nor stage.
  assert.ok(html.includes('data-setting="onExhausted"'));
  assert.ok(!html.includes('data-setting="maxRoundsPlan"'), 'the per-stage controls are gone, not hidden');
});

test('both deal switches are offered, and off is the default', () => {
  const html = panelHtml(state(), 'n0nce');

  assert.ok(html.includes('data-setting="dealPlanLenses"'));
  assert.ok(html.includes('data-setting="dealCodeLenses"'));
  assert.ok(!html.includes('data-setting="dealPlanLenses" checked'), 'dealing gives up cross-vendor agreement');
});

test('the code stage offers Fast and Full, and Fast is the lit one', () => {
  // Fast is not a preference. Measured on one commit, taking the checkout away made every hosted
  // model find MORE useful defects — 4→8, 6→10, 6→7 — at a half to a third of the input tokens,
  // and three real defects appeared that no run with a checkout had reached. The switch exists so
  // a review that genuinely needs the surrounding code can still ask for it.
  const html = panelHtml(state(), 'n0nce');

  assert.ok(html.includes('data-setting="codeWorkspace" value="none"'), 'no Fast position');
  assert.ok(html.includes('data-setting="codeWorkspace" value="worktree"'), 'no Full position');
  assert.match(html, /class="on"[^>]*><input type="radio" name="codeWorkspace" data-setting="codeWorkspace" value="none"/,
    'Fast is the default and must be the lit half');
});

test('choosing Full lights the right half, and only that half', () => {
  const html = panelHtml(state({ settings: { ...DEFAULTS, codeWorkspace: 'worktree' } }), 'n0nce');

  assert.match(html, /class="on"[^>]*><input type="radio" name="codeWorkspace" data-setting="codeWorkspace" value="worktree"/);
  assert.match(html, /class=""[^>]*><input type="radio" name="codeWorkspace" data-setting="codeWorkspace" value="none"/,
    'both halves lit reads as neither');
});

test('the questions are English, so there is no language to choose', () => {
  // The escalation is three buttons; there is no prose left to translate, and a subprocess per
  // escalation that can time out or answer in the wrong language earned nothing.
  const html = panelHtml(state(), 'n0nce');

  assert.ok(!html.includes('data-setting="reviewers"'));
  assert.ok(!html.includes('data-setting="translator.provider"'));
});

test('each reviewer gets a switch, a model field and a way out', () => {
  const html = panelHtml(state(), 'n0nce');
  for (const id of ['codex', 'antigravity']) {
    assert.ok(html.includes(`data-setting="enabled" data-vendor="${id}"`), `${id} can be switched off`);
    assert.ok(html.includes(`data-setting="model" data-vendor="${id}"`), `${id} takes a model`);
    assert.ok(html.includes(`data-command="removeVendor" data-id="${id}"`), `${id} can be removed`);
  }
  assert.ok(html.includes('data-command="addVendor"'), 'and the list is not meant to stay at two');
});

test('a disabled reviewer is shown unchecked', () => {
  const html = panelHtml(
    state({ vendors: [{ id: 'codex', runtime: 'codex', model: '', enabled: false, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 }] }),
    'n0nce',
  );
  assert.ok(!html.includes('data-vendor="codex" checked'));
});

test("codex offers the CLI's own cached models; antigravity offers what agy lists", () => {
  const html = panelHtml(state(), 'n0nce');
  assert.ok(html.includes('value="gpt-5.6-sol"'), 'discovered from ~/.codex/models_cache.json');
  assert.ok(html.includes('models the Codex CLI has cached'));
  assert.ok(html.includes('value="gemini-3.7-flash-high"'));
  assert.ok(html.includes('one CLI'), 'the provenance of the list is admitted, never passed off as discovery');
});

test('the picker is a SELECT with every model visible, never a filtering datalist', () => {
  const html = panelHtml(state(), 'n0nce');
  assert.ok(!html.includes('<datalist'), 'a datalist filters by the current value and reads as empty');
  assert.ok(html.includes('another model…'), 'the list is a convenience, never a limit');
  assert.ok(html.includes("the CLI's default"), 'and empty is a first-class choice');
});

test('a model the person typed stays in its own list', () => {
  const html = panelHtml(
    state({ vendors: [{ id: 'codex', runtime: 'codex', model: 'something-new', enabled: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 }] }),
    'n0nce',
  );
  assert.ok(html.includes('value="something-new"'));
  assert.ok(html.includes('something-new (yours)'), 'a saved value never vanishes from its own dropdown');
});

test('a custom endpoint is editable; a first-party vendor shows no URL field', () => {
  const custom = panelHtml(
    state({ vendors: [{ id: 'mistral', runtime: 'codex', model: '', enabled: true, baseUrl: 'https://api.mistral.ai/v1', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 }] }),
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
  // A hex is allowed in exactly one place: the fallback of a theme token, `var(--x, #hex)`. The
  // sibling product does the same, and the reason is that a theme which does not define the token
  // should still get the intended colour rather than the browser's idea of one. A BARE hex is still
  // us choosing a colour for somebody's editor, and stays forbidden.
  const styles = html.split('<script')[0] ?? '';
  const bare = styles.replace(/var\([^)]*\)/g, '');
  assert.ok(!/#[0-9a-f]{6}/i.test(bare), 'no hard-coded hex colours outside a var() fallback');
});

test('the panel says whether the server is installed, and which version', () => {
  assert.ok(panelHtml(state(), 'n').includes('not installed yet'));
  const installed = panelHtml(state({ server: { kind: 'known', version: '0.4.0', remembered: false, updateOffered: false } }), 'n');
  assert.ok(installed.includes('coai-mcp 0.4.0 is installed'));
});

test('every setting appears once, so two controls cannot disagree about it', () => {
  // Reported from the panel: "What a reviewer gets" was rendered TWICE, and the two looked
  // different — one filled, one hollow. That is not a rendering artefact but what a browser does
  // with two radio groups sharing a `name`: it treats them as ONE group, so selecting in the first
  // clears the second. The blocks were byte-identical copy-paste.
  const html = panelHtml(state(), 'n');
  const groups = [...html.matchAll(/role="radiogroup" aria-label="([^"]+)"/g)].map((m) => m[1]!);
  const seen = new Set<string>();
  for (const label of groups) {
    assert.ok(!seen.has(label), `"${label}" is rendered as ${groups.filter((g) => g === label).length} radio groups`);
    seen.add(label);
  }

  const named = [...html.matchAll(/<input type="radio" name="([^"]+)"/g)].map((m) => m[1]!);
  for (const name of new Set(named)) {
    const values = named.filter((n) => n === name).length;
    assert.ok(values <= 2, `radio name "${name}" appears ${values} times — more than one group shares it`);
  }
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

test('the sidebar lists what is running; a finished round belongs to the log', () => {
  assert.ok(panelHtml(state(), 'n').includes('Nothing is running'));
  const html = panelHtml(
    state({
      sessions: [
        {
          state: { sessionId: 's', repoPath: 'r', branch: 'feature/x', stage: 'CodeReview', awaitingResolve: false },
          rounds: [
            { stage: 'PlanReview', number: 1, verdict: 'revise', gatingCount: 3, reviewers: 'all 2', completedUtc: '2026-08-30T00:00:00Z', status: 'done' },
            { stage: 'CodeReview', number: 1, verdict: '', gatingCount: 0, reviewers: '1 of 6', completedUtc: '', status: 'running', startedUtc: '2026-08-30T04:00:00Z' },
          ],
        },
      ],
    }),
    'n',
    Date.parse('2026-08-30T06:00:00Z'),
  );
  assert.ok(html.includes('badge running'), 'the round in flight is shown');
  assert.ok(!html.includes('revise'), 'the finished one is not — the log has it');
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
        { id: 'codex', runtime: 'codex', model: '', enabled: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 },
        { id: 'deepseek', runtime: 'codex', model: '', enabled: true, baseUrl: 'https://api.deepseek.com/v1', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 },
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
    state({ vendors: [{ id: 'deepseek', runtime: 'codex', model: '', enabled: false, baseUrl: 'https://x/v1', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 }] }),
    'n',
  );
  assert.ok(html.includes('Nothing to fill in yet'), 'a reviewer that does not run needs nothing');
});

test('the server line is body text, not a footnote', () => {
  const css = panelHtml(state(), 'n').split('</style>')[0] ?? '';
  assert.ok(!/\.status \{[^}]*font-size/.test(css), 'it states a fact and reads at the same size as one');
});

test('claude is offered as a reviewer preset', () => {
  const claude = panelHtml(
    state({ vendors: [{ id: 'claude', runtime: 'claude', model: 'haiku', enabled: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 }] }),
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
  for (const folded of ['reviewers', 'prompts', 'gate', 'limits', 'keys', 'server', 'rounds']) {
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
  // The vendor's word now carries its own colour, so the row is a span plus the rest of the
  // sentence. Same content, and the assertion now also says where the colour stops.
  assert.ok(html.includes(
    `<span class="who" style="color:${vendorColour('codex')}">codex</span>/Architecture — done (2 findings)`));
  assert.ok(html.includes('5.3k in / 260 out'));
  assert.ok(html.includes('no cost reported'));
});

/**
 * Two sections must not fight over one class name.
 *
 * <p>`.usage` was defined twice — once for the per-round usage line in Recent rounds
 * (`font-size: 11px; opacity: .7`) and once for the spending cards. CSS does not care which was
 * meant: every spending card was dimmed to 70%, and the `.hint` lines inside it to .7 × .65 = 45%,
 * so the whole section read as disabled. Nothing was broken and nothing said anything; it just
 * looked switched off.</p>
 */
test('no two sections define the same class, because the loser is dimmed in silence', () => {
  const html = panelHtml(state(), 'n0nce');
  const css = html.slice(html.indexOf('<style>'), html.indexOf('</style>'));
  const selectors = [...css.matchAll(/(?:^|\n)\s*([.#][^\s{][^{\r\n]*?)\s*\{/g)].map((m) => m[1]!.trim());

  const seen = new Set<string>();
  const twice = selectors.filter((s) => (seen.has(s) ? true : (seen.add(s), false)));
  assert.deepEqual(twice, [], `defined twice: ${twice.join(', ')}`);
});

test('a spending row shows the vendor and its cost apart, not run together', () => {
  const html = panelHtml(
    state({
      usage: [
        { utc: new Date().toISOString(), provider: 'antigravity', model: 'm', role: 'PlanCritique',
          stage: 'PlanReview', outcome: 'Ok', tokensIn: 55_800, tokensOut: 25_500, costUsd: null, seconds: 84 },
      ],
    }),
    'n0nce',
  );

  // "antigravity—" is what a name butted against the money dash reads as, and it read as a typo.
  // The two are adjacent in the markup on purpose — it is the ROW that holds them apart, so the
  // assertion is on the class that gets positioned and on the rule that positions it.
  assert.match(html, /<span class="cost">/, 'the money needs an element the row can push to its far end');
  assert.match(html, /\.spend \.head \{[^}]*space-between/, 'the row puts the name and the cost at opposite ends');
  assert.match(html, /\.spend \.figures \{/, 'the tokens are the answer, so they are not styled as a hint');
});

// ---------- the prompts section: one frame for the plan, one for the three code roles ----------

test('the plan role stands in its own frame, apart from the code roles', () => {
  const html = panelHtml(state(), 'n0nce');
  const groups = html.split('class="role-group"');

  assert.equal(groups.length, 3, 'two frames: the plan stage, then the code stage');
  const [, planFrame, codeFrame] = groups;
  assert.ok(planFrame!.includes('data-prompt="PlanCritique"'), 'the plan role is in the first frame');
  assert.ok(!planFrame!.includes('data-prompt="Architecture"'), 'and the code roles are not');
  for (const role of ['Architecture', 'SecurityReliability', 'UxDxPerformance']) {
    assert.ok(codeFrame!.includes(`data-prompt="${role}"`), `${role} shares the code frame`);
  }
});

test('each code role is wrapped in its own colour, and still says its name', () => {
  const html = panelHtml(state(), 'n0nce');
  for (const [role, tone] of [
    ['Architecture', 'arch'],
    ['SecurityReliability', 'sec'],
    ['UxDxPerformance', 'uxdx'],
  ] as const) {
    assert.match(html, new RegExp(`class="role role-${tone}"[\\s\\S]*?data-prompt="${role}"`),
      `${role} is not wrapped in its own tone`);
  }
  // The colour is never the only signal: a person who cannot tell them apart still reads the name.
  for (const label of ['Architecture', 'Security &amp; reliability', 'Performance &amp; UX-DX']) {
    assert.ok(html.includes(label), `${label} is written out, not left to a colour`);
  }
});

test('the role colours come from the theme with a fallback, never a bare hex', () => {
  const css = panelHtml(state(), 'n0nce').split('</style>')[0] ?? '';
  for (const [tone, fallback] of [
    ['arch', '#569cd6'],
    ['sec', '#ce9178'],
    ['uxdx', '#b5cea8'],
    ['plan', '#c586c0'],
  ] as const) {
    assert.match(css, new RegExp(`--tone-${tone}:\\s*var\\(--vscode-charts-\\w+,\\s*${fallback}\\)`),
      `${tone} must be a charts token with a fallback`);
  }
});


test('the number of rounds each stage shows follows that stage’s own budget', () => {
  const html = panelHtml(
    state({ settings: { ...DEFAULTS, rounds: { ...DEFAULTS.rounds, PlanCritique: 2, Architecture: 4, SecurityReliability: 4, UxDxPerformance: 4 } } }),
    'n0nce',
  );
  assert.ok(html.includes('data-prompt="PlanCritique" data-round="2"'));
  assert.ok(!html.includes('data-prompt="PlanCritique" data-round="3"'), 'a plan round nobody will run needs no picker');
  assert.ok(html.includes('data-prompt="Architecture" data-round="4"'));
});

test('code round 1 is the conventions pass, and says so', () => {
  const html = panelHtml(state(), 'n0nce');
  assert.match(html, /Round 1[\s\S]{0,400}?Conventions/, 'the first code round defaults to the rules check');
  assert.ok(html.includes('written down'), 'and the section says what that pass judges against');
});

test('the code stage states its own arithmetic, in the numbers actually configured', () => {
  // "three reviewers per vendor, every round" made a reader ask whether each reviewer runs six
  // times. It does not: six is the number of REVIEWERS in a round — vendors × roles — each run
  // once. The panel showing 3 roles × 2 round-pickers is what looks like six runs, so the sentence
  // has to do the multiplication out loud.
  const html = panelHtml(
    state({
      vendors: [
        { id: 'codex', runtime: 'codex', model: '', enabled: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 },
        { id: 'antigravity', runtime: 'antigravity', model: '', enabled: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 },
      ],
      settings: { ...DEFAULTS },
    }),
    'n0nce',
  );

  assert.match(html, /2 vendors × 3 roles = 6 reviewers/);
  assert.match(html, /each runs once per round/i, 'the answer to the question that was actually asked');
  assert.match(html, /up to 2 rounds/);
});

test('a disabled vendor is not counted in the arithmetic', () => {
  const html = panelHtml(
    state({
      vendors: [
        { id: 'codex', runtime: 'codex', model: '', enabled: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 },
        { id: 'antigravity', runtime: 'antigravity', model: '', enabled: false, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 },
      ],
    }),
    'n0nce',
  );

  assert.match(html, /1 vendor × 3 roles = 3 reviewers/, 'a reviewer that will not run is not one');
});
