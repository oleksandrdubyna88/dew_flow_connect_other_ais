import { SNIPPET_VERSION } from '../claudeSnippet';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { UsageEntry } from '../usage';
import { roundsLogHtml, usageTabHtml } from '../roundsLog';
import { escapeHtml, panelHtml, PanelState } from '../panelView';
import { DEFAULTS } from '../settingsShape';
import { vendorPalette } from '../vendorColour';
import { DEFAULT_VENDORS } from '../vendors';

/**
 * The palette the page builds for this fixture — from the vendors it configures, which is the same
 * canonical list every view reads. Asking for a colour any other way here would be asserting against
 * a second palette, which is exactly what these tests exist to forbid.
 */
const DEFAULT_COLOUR = vendorPalette(DEFAULT_VENDORS.map((v) => v.id));

const state = (over: Partial<PanelState> = {}): PanelState => ({
  settings: DEFAULTS,
  vendors: DEFAULT_VENDORS,
  agyModels: [],
  codexModels: [
    { id: 'gpt-5.6-sol', label: 'GPT-5.6-Sol' },
    { id: 'gpt-5.4-mini', label: 'GPT-5.4-Mini' },
  ],
  localEngines: {},
  server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
  side: '',
  perSide: false,
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


/**
 * The section is named for one thing and describes that thing.
 *
 * <p>For three releases (0.31.1–0.31.3) it also carried the Team server's address, read-only, and
 * the version answering on it — on the reasoning that "what am I talking to" is one question with
 * two answers. In front of the operator it read as two subjects sharing a box, and the answer was
 * to name the box: a Team server is described where it is managed, under *Team servers*.</p>
 */
test('the MCP server section is titled for coai-mcp and describes nothing else', () => {
  // TWO servers, because a reintroduction that only rendered the second would pass a
  // single-server fixture while every person with two saw it.
  const html = panelHtml(
    state({
      teamServers: [
        { server: { id: 'rs', name: 'RemSoftDev', url: 'https://coai.remsoft.dev' }, email: 'a@remsoft.dev', problem: '', stale: false },
        { server: { id: 'st', name: 'Staging', url: 'https://coai.staging.dev' }, email: 'b@remsoft.dev', problem: '', stale: false },
      ],
      latestServerVersion: '0.18.7',
    }),
    'n0nce',
  );

  assert.ok(html.includes('<summary>MCP server</summary>'), 'the section says what it is about');
  assert.ok(!html.includes('<summary>Server</summary>'), 'and no longer says it vaguely');

  // The SECTION's own markup, not the whole panel: the address and the account are supposed to be
  // elsewhere in this document — under Team servers, which is the point. `rounds` is the section
  // after this one; the bounds are asserted because a slice from a `-1` reads to the end of the
  // document and would pass by accident. (It did: this test bounded on a `usage` section that
  // `panelHtml` does not render, and was green only because Team servers happens to come first.)
  const from = html.indexOf('data-section="server"');
  const to = html.indexOf('data-section="rounds"');
  assert.ok(from > 0 && to > from, 'the MCP server section is bounded by the one after it');
  const mcp = html.slice(from, to);

  // Not just the `ts-here-` id prefix the old block used: a reintroduction under a different id or
  // class would slip past that, and what must not come back is the CONTENT.
  assert.ok(!mcp.includes('ts-here-'), 'no Team-server address block');
  assert.ok(!mcp.includes('coai.remsoft.dev'), 'no Team-server address');
  assert.ok(!mcp.includes('coai.staging.dev'), 'not the second one either');
  assert.ok(!mcp.includes('@remsoft.dev'), 'no Team-server account');
  assert.ok(!mcp.includes('coai-server'), 'no Team-server version — coai-mcp is the subject here');
  assert.ok(!mcp.includes('data-server-url='), 'no read-only address input');

  // And the section still says its own subject, so this is a narrowing rather than an emptying.
  assert.ok(mcp.includes('0.18.7'), 'the coai-mcp lines are still there');
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
    state({ vendors: [{ id: 'codex', runtime: 'codex', model: '', enabled: false, plan: true, code: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 }] }),
    'n0nce',
  );
  // The ROW's checkbox, by its id: the vendor also has two stage boxes now, and asserting on
  // `data-vendor="codex" checked` matched one of those instead of the switch under test.
  assert.doesNotMatch(html, /id="v-codex"[^>]*checked/);
});

test("codex offers the CLI's own cached models; antigravity offers what agy lists", () => {
  const html = panelHtml(state(), 'n0nce');
  assert.ok(html.includes('value="gpt-5.6-sol"'), 'discovered from ~/.codex/models_cache.json');
  assert.ok(html.includes('models the Codex CLI has cached'));
  assert.ok(html.includes('value="gemini-3.7-flash-high"'));
  // This used to assert the sentence "what `agy models` lists for this subscription" while the list
  // was a hand-written constant — and the assertion's own comment claimed the provenance was
  // admitted. The two disagreed, and the constant went a model generation stale behind that.
  assert.ok(
    html.includes('did not answer'),
    'with nothing discovered, the line says the list may be behind rather than claiming the CLI said it');
});

test('the picker is a SELECT with every model visible, never a filtering datalist', () => {
  const html = panelHtml(state(), 'n0nce');
  assert.ok(!html.includes('<datalist'), 'a datalist filters by the current value and reads as empty');
  assert.ok(html.includes('another model…'), 'the list is a convenience, never a limit');
  assert.ok(html.includes("the CLI's default"), 'and empty is a first-class choice');
});

test('a model the person typed stays in its own list', () => {
  const html = panelHtml(
    state({ vendors: [{ id: 'codex', runtime: 'codex', model: 'something-new', enabled: true, plan: true, code: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 }] }),
    'n0nce',
  );
  assert.ok(html.includes('value="something-new"'));
  assert.ok(html.includes('something-new (yours)'), 'a saved value never vanishes from its own dropdown');
});

test('a custom endpoint is editable; a first-party vendor shows no URL field', () => {
  const custom = panelHtml(
    state({ vendors: [{ id: 'mistral', runtime: 'codex', model: '', enabled: true, plan: true, code: true, baseUrl: 'https://api.mistral.ai/v1', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 }] }),
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
        { id: 'codex', runtime: 'codex', model: '', enabled: true, plan: true, code: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 },
        { id: 'deepseek', runtime: 'codex', model: '', enabled: true, plan: true, code: true, baseUrl: 'https://api.deepseek.com/v1', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 },
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
    state({ vendors: [{ id: 'deepseek', runtime: 'codex', model: '', enabled: false, plan: true, code: true, baseUrl: 'https://x/v1', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 }] }),
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
    state({ vendors: [{ id: 'claude', runtime: 'claude', model: 'haiku', enabled: true, plan: true, code: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 }] }),
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
    `<span class="who" style="color:${DEFAULT_COLOUR('codex')}">codex</span>/Architecture — done (2 findings)`));
  assert.ok(html.includes('5.3k in / 260 out'));
  assert.ok(html.includes('no cost reported'));
});

/**
 * A reviewer's card and its name in a round are the same colour, and stay that way.
 *
 * <p>The operator asked for coloured edges on the reviewer cards and pinned the requirement that
 * matters: the colours must be synchronised with the colours in the rounds list. So the assertion is not
 * about a hex — it is that ONE function answers for both places. A second palette would satisfy
 * "the card is coloured" and break the only thing that was asked for.</p>
 */
test('a reviewer card wears the colour that vendor has in the rounds list', () => {
  const html = panelHtml(state(), 'n0nce');

  for (const vendor of DEFAULT_VENDORS) {
    assert.ok(
      html.includes(`<div class="vendor" style="border-left-color:${DEFAULT_COLOUR(vendor.id)}">`),
      `${vendor.id}'s card should carry ${DEFAULT_COLOUR(vendor.id)}`,
    );
  }

  // Per VENDOR, not one colour for the section: two different ids must not paint the same edge, or
  // the loop above would pass against a constant and say nothing.
  const colours = DEFAULT_VENDORS.map((v) => DEFAULT_COLOUR(v.id));
  assert.strictEqual(new Set(colours).size, colours.length, 'each vendor gets its own colour');
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
  // The spending section moved to the rounds log page on 2026-09-05; the rule about the row is the
  // same there. "antigravity—" is what a name butted against the money dash reads as, and it read
  // as a typo. The two are adjacent in the markup on purpose — it is the ROW that holds them apart.
  const usage = usageTabHtml(
    [{ utc: new Date().toISOString(), provider: 'antigravity', model: 'm', role: 'PlanCritique',
       stage: 'PlanReview', outcome: 'Ok', tokensIn: 55_800, tokensOut: 25_500, costUsd: null, seconds: 84 } as UsageEntry],
    'day', [], {});
  const html = roundsLogHtml([], [], 'n0nce', usage);

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
        { id: 'codex', runtime: 'codex', model: '', enabled: true, plan: true, code: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 },
        { id: 'antigravity', runtime: 'antigravity', model: '', enabled: true, plan: true, code: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 },
      ],
      settings: { ...DEFAULTS },
    }),
    'n0nce',
  );

  // Four code roles since Conventions became one of them, and the sentence counts ROLES
  // rather than a literal — which is what let it say "3 roles" under four boxes.
  assert.match(html, /2 vendors × up to 4 roles = 8 reviewers/);
  assert.match(html, /each runs once per round/i, 'the answer to the question that was actually asked');
  assert.match(html, /up to 1 round/);
});

test('a disabled vendor is not counted in the arithmetic', () => {
  const html = panelHtml(
    state({
      vendors: [
        { id: 'codex', runtime: 'codex', model: '', enabled: true, plan: true, code: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 },
        { id: 'antigravity', runtime: 'antigravity', model: '', enabled: false, plan: true, code: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 },
      ],
    }),
    'n0nce',
  );

  assert.match(html, /1 vendor × up to 4 roles = 4 reviewers/, 'a reviewer that will not run is not one');
});

test('the panel names the side it is about to keep settings for', () => {
  // Somebody with a Windows window and two WSL distros is about to keep three sets of settings, and
  // the only way to be sure which one is being edited is to read it off the panel doing the editing.
  const page = panelHtml(state({ side: 'WSL: Ubuntu-24.04', perSide: true }), 'nonce');

  assert.match(page, /Separate settings for each side/);
  assert.match(page, /This side is <b>WSL: Ubuntu-24\.04<\/b>/);
  assert.match(page, /data-setting="perSideSettings"[^>]* checked/);
  assert.match(page, /keeps its own vendors, models, proxies/);
});

test('with the switch off the panel says the settings are shared, and does not tick the box', () => {
  const page = panelHtml(state({ side: '', perSide: false }), 'nonce');

  assert.match(page, /This side is <b>this machine<\/b>/, 'a local window has one side and no word for it');
  assert.match(page, /shares its settings with every other side/);
  assert.doesNotMatch(page, /data-setting="perSideSettings"[^>]* checked/);
});

test('every reviewer row offers the two stages, ticked unless narrowed', () => {
  // The setting the measurement asked for: local was 19 % useful on a plan and 3 % on code, so
  // "on for the plan, off for the code" has to be expressible without editing JSON.
  const html = panelHtml(state({
    vendors: [
      { id: 'codex', runtime: 'codex', model: '', enabled: true, plan: true, code: true, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 },
      { id: 'local', runtime: 'local', model: 'qwen', enabled: true, plan: true, code: false, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 },
    ],
  }), 'n0nce');

  assert.match(html, /data-setting="plan" data-vendor="codex" checked/);
  assert.match(html, /data-setting="code" data-vendor="codex" checked/);
  assert.match(html, /data-setting="plan" data-vendor="local" checked/);
  assert.doesNotMatch(html, /data-setting="code" data-vendor="local"[^>]*checked/,
    'a vendor narrowed to plans must show its code box unticked');
  assert.match(html, /reviews plans/);
  assert.match(html, /reviews code/);
});

test('a vendor that is off leaves its stage boxes readable but inert', () => {
  // The review gate's point: leaving them live while the master switch is off invites somebody to
  // tick one and expect it to mean something. Visible, so the state can be read; disabled, so it
  // cannot be contradicted.
  const html = panelHtml(state({
    vendors: [{ id: 'codex', runtime: 'codex', model: '', enabled: false, plan: true, code: false, baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0 }],
  }), 'n0nce');

  assert.match(html, /data-setting="plan" data-vendor="codex" checked disabled/);
  assert.match(html, /data-setting="code" data-vendor="codex" disabled/);
  assert.match(html, /class="field stages off"/);
});

/**
 * The round limit's note is a line UNDER its row, not a third thing inside it.
 *
 * <p>Issue #118. Every numeric row in Limits is a `.field.inline` flex holding a label and a 64px
 * number input, pushed to opposite ends — which is what puts all five inputs in one column. The
 * round limit's row carried its derived note as a third flex item, so that row alone shared its
 * width three ways: the input sat left of the other four and the note was squeezed in beside it.
 * Every other description in this panel is a sibling `.hint` after its row, which is the shape this
 * takes now.</p>
 *
 * <p>Asserted for all THREE wordings `roundLimitNote` can produce — raised on the plan round by two
 * vendors, because a structural fix verified on two of three states leaves the third free to keep
 * the old shape.</p>
 */
test('the round limit\'s note is a line under its row, in every state it can be in', () => {
  const limits = (over: Partial<PanelState['settings']>): string => {
    const html = panelHtml(state({ settings: { ...DEFAULTS, ...over }, openSections: ['limits'] }), 'n0nce');
    const from = html.indexOf('data-section="limits"');
    const to = html.indexOf('data-section="keys"');
    assert.ok(from > 0 && to > from, 'the limits section is bounded — a -1 would read to the end of the document');
    return html.slice(from, to);
  };

  const states: ReadonlyArray<[string, Partial<PanelState['settings']>, string]> = [
    ['derived', { roundTimeoutMinutes: 0, reviewerTimeoutMinutes: 10, maxConcurrency: 3 }, 'worked out:'],
    ['set by hand', { roundTimeoutMinutes: 90, reviewerTimeoutMinutes: 10 }, 'set by hand'],
    ['cut off', { roundTimeoutMinutes: 5, reviewerTimeoutMinutes: 10 }, "shorter than one reviewer's"],
  ];

  for (const [name, settings, wording] of states) {
    const html = limits(settings);
    // The row is everything from its opening tag to the first </div>: it holds no nested div today,
    // and the whole point of this test is that it must not grow one.
    const row = /<div class="field inline">(?:(?!<\/div>)[\s\S])*id="roundTimeoutMinutes"(?:(?!<\/div>)[\s\S])*<\/div>/.exec(html);
    assert.ok(row, `${name}: the round limit is a .field.inline row`);
    assert.ok(!row[0].includes('hint'), `${name}: the note is not inside the row — ${row[0]}`);

    const after = html.slice(row.index + row[0].length).trimStart();
    assert.ok(after.startsWith('<div class="hint">'), `${name}: a hint follows the row, on its own line — got ${after.slice(0, 60)}`);
    assert.ok(after.slice(0, 400).includes(wording), `${name}: and it carries the note`);
  }
});

test('every limits row holds exactly a label and an input, so none can crowd its column again', () => {
  // The structural property behind the row above, asserted for all five rather than one. Raised on
  // the plan round: a test that names the round limit would not notice the next row to grow a third
  // item, and the alignment is a property of the SET of rows.
  const html = panelHtml(state({ openSections: ['limits'] }), 'n0nce');
  const section = html.slice(html.indexOf('data-section="limits"'), html.indexOf('data-section="keys"'));
  const rows = [...section.matchAll(/<div class="field inline">((?:(?!<\/div>)[\s\S])*)<\/div>/g)].map((m) => m[1]!);

  assert.equal(rows.length, 5, 'five numeric settings');
  for (const row of rows) {
    // The label carries the `?` help span inside itself, so three tags for two flex children.
    const tags = [...row.matchAll(/<(\w+)[\s>]/g)].map((m) => m[1]!);
    assert.deepEqual(tags, ['label', 'span', 'input'], `a row is its label (with the ? help) and its input: ${row}`);
  }
});

/**
 * The round limit shows what it will actually be, beside the box that sets it.
 *
 * <p>Zero means "work it out", and a reviewer on the plan round called that a hidden dependency:
 * raise the reviewer timeout and the round doubles with nothing on screen saying so. The panel does
 * the same arithmetic the server does, in the numbers currently configured.</p>
 */
test('the round limit says what it works out to, and warns when it cannot be met', () => {
  const withSettings = (over: Partial<PanelState['settings']>): string =>
    panelHtml(state({ settings: { ...DEFAULTS, ...over }, openSections: ['limits'] }), 'n0nce');

  // Asserted as a SHAPE rather than against a vendor count: how many vendors ship enabled is a
  // default that moves, and a test that hard-codes it fails for a reason that has nothing to do
  // with the arithmetic under test.
  const derived = withSettings({ roundTimeoutMinutes: 0, reviewerTimeoutMinutes: 10, maxConcurrency: 3 });
  const said = /worked out: at most (\d+) waves? × 10 min = (\d+) min/.exec(derived);
  assert.ok(said, `the derivation is shown; the note read: ${derived.slice(derived.indexOf('worked out') - 20, 200)}`);
  assert.ok(Number(said[1]) >= 1, 'at least one wave');
  assert.equal(Number(said[2]), Number(said[1]) * 10, 'and the total is the waves times the reviewer timeout');

  const tooSmall = withSettings({ roundTimeoutMinutes: 5, reviewerTimeoutMinutes: 10 });
  assert.ok(
    tooSmall.includes("shorter than one reviewer's 10 min"),
    'a limit under one reviewer cannot be met, and the panel says so where it is set',
  );

  // A vendor that reviews plans only is not a code reviewer, and dealing sends each lens to ONE
  // vendor rather than to all of them — two ways the naive "every enabled vendor, every role" count
  // is too big. Both raised on the code round.
  const planOnly = panelHtml(
    state({
      settings: { ...DEFAULTS, roundTimeoutMinutes: 0, reviewerTimeoutMinutes: 10, maxConcurrency: 3 },
      vendors: DEFAULT_VENDORS.map((v) => ({ ...v, enabled: true, code: false })),
      openSections: ['limits'],
    }),
    'n0nce',
  );
  // The EXACT count, not "at least one". A reviewer pointed out that the old enabled-vendor
  // arithmetic also produces a positive number, so a floor assertion cannot tell the regression from
  // the fix. With no code-capable vendor the count floors at one vendor x four code roles = four
  // reviewers, which is two waves at a cap of three — where counting every ENABLED vendor would give
  // two vendors x four = eight, and three waves.
  assert.match(planOnly, /worked out: at most 2 waves × 10 min = 20 min/,
    'no code-capable vendor floors at one, not at every enabled vendor');

  const dealt = withSettings({ roundTimeoutMinutes: 0, reviewerTimeoutMinutes: 10, maxConcurrency: 3, dealCodeLenses: true });
  const dealtWaves = /worked out: at most (\d+) waves?/.exec(dealt);
  const undealtWaves = /worked out: at most (\d+) waves?/.exec(derived);
  assert.ok(dealtWaves && undealtWaves);
  assert.ok(
    Number(dealtWaves[1]) <= Number(undealtWaves[1]),
    'dealing sends each lens to one vendor, so a dealt round is never wider than an undealt one',
  );

  const byHand = withSettings({ roundTimeoutMinutes: 90, reviewerTimeoutMinutes: 10 });
  assert.ok(byHand.includes('set by hand'), 'a workable explicit limit needs no arithmetic');
  // "worked out:" with the colon — the note's own form. The help article on the same page says
  // "worked out rather than guessed" about the setting, which is prose about it, not a claim that
  // THIS value was derived.
  assert.ok(!byHand.includes('worked out:'), 'and is not described as derived');
});

/**
 * A code role can be switched off, and off means it does not take part.
 *
 * <p>Asked for on 2026-09-08 over a screenshot of the four code boxes. The requirement is not "grey
 * it out" — it is that the role takes no part in the round, which is a server behaviour; what the
 * panel owes is a switch that says so, an arithmetic that agrees with it, and a refusal to remove
 * the last one.</p>
 */
test('every code role box carries a switch, and the plan role does not', () => {
  const html = panelHtml(state(), 'n0nce');

  for (const role of ['Conventions', 'Architecture', 'SecurityReliability', 'UxDxPerformance']) {
    assert.ok(
      html.includes(`data-setting="roleEnabled" data-role="${role}"`),
      `${role} has no switch`,
    );
  }
  assert.ok(
    !html.includes('data-setting="roleEnabled" data-role="PlanCritique"'),
    'the plan stage has one role; a switch that turns the whole stage off is a different feature',
  );
});

test('a role switched off dims its box and disables the controls that no longer apply', () => {
  const html = panelHtml(
    state({ settings: { ...DEFAULTS, roleEnabled: { ...DEFAULTS.roleEnabled, Architecture: false } } }),
    'n0nce',
  );

  assert.match(html, /class="role role-arch off"/, 'the box says it is off');
  assert.match(
    html,
    /data-setting="rounds" data-role="Architecture"[^>]*disabled/,
    'a rounds box that cannot be reached by any reviewer is not editable',
  );
  // And the role is still THERE, with its number kept: this is a switch, not a way of clearing it.
  assert.ok(html.includes('id="rounds-Architecture"'));
});

test('the last role standing cannot be unticked', () => {
  const onlyOne = {
    ...DEFAULTS,
    roleEnabled: { Conventions: false, Architecture: true, SecurityReliability: false, UxDxPerformance: false },
  };
  const html = panelHtml(state({ settings: onlyOne }), 'n0nce');

  assert.match(
    html,
    /data-setting="roleEnabled" data-role="Architecture"[^>]*disabled/,
    'removing the last reviewer would start a round nobody answers, which never resolves',
  );
  assert.match(
    html,
    /data-setting="roleEnabled" data-role="Conventions"(?![^>]*disabled)/,
    'the ones already off stay clickable, or there is no way back',
  );
  // A disabled input shows no title tooltip and cannot be focused by keyboard, so the reason has to
  // be on the page rather than under the pointer. Raised on the code round and it was right.
  assert.ok(
    html.includes('The only role still ticked — tick another one before turning this one off.'),
    'a control that refuses without saying why reads as broken',
  );
});

test('the fan-out sentence counts the roles that will actually run', () => {
  const all = panelHtml(state(), 'n0nce');
  const two = panelHtml(
    state({
      settings: {
        ...DEFAULTS,
        roleEnabled: { Conventions: false, Architecture: true, SecurityReliability: true, UxDxPerformance: false },
      },
    }),
    'n0nce',
  );

  // The sentence is a promise about the round that is about to run. Announcing four roles' worth of
  // reviewers while two are switched off describes a different round.
  assert.notEqual(
    all.slice(all.indexOf('Code stage'), all.indexOf('Code stage') + 200),
    two.slice(two.indexOf('Code stage'), two.indexOf('Code stage') + 200),
    'the fan-out sentence did not notice that two roles are off',
  );
});

test('an older server that would run the role anyway is called out', () => {
  // The failure this warns about is backwards: the box says off and the reviewer runs. A person
  // would only discover it by reading the reviewer list of a round they already paid for.
  const off = { ...DEFAULTS, roleEnabled: { ...DEFAULTS.roleEnabled, Architecture: false } };
  const old = panelHtml(
    state({ settings: off, server: { kind: 'known', version: '0.18.12', remembered: true, updateOffered: false } }),
    'n0nce',
  );
  const current = panelHtml(
    state({ settings: off, server: { kind: 'known', version: '0.18.13', remembered: true, updateOffered: false } }),
    'n0nce',
  );
  const nothingOff = panelHtml(
    state({ server: { kind: 'known', version: '0.18.12', remembered: true, updateOffered: false } }),
    'n0nce',
  );

  assert.match(old, /does not know a role can be switched off[\s\S]*Architecture/);
  assert.ok(!current.includes('does not know a role can be switched off'), 'a server that knows is not nagged');
  assert.ok(!nothingOff.includes('does not know a role can be switched off'),
    'and neither is one where nothing is switched off — there is nothing to get wrong');
});
