import assert from 'node:assert/strict';
import { test } from 'node:test';
import { panelHtml, PanelState } from '../panelView';
import { DEFAULTS } from '../settingsShape';
import { SNIPPET_VERSION } from '../claudeSnippet';
import { RoundRecord, SessionFile } from '../rounds';

/**
 * The class on a coloured vendor word must not be a class the stylesheet draws a BOX with.
 *
 * <p>Reported from a screenshot: every vendor name in the rounds list had lines through it. It was
 * not a strikethrough — the word was wearing the border of a settings card. The colour was hung on
 * `class="vendor"`, and `.vendor` already meant the reviewer's configuration card in this
 * stylesheet: <code>border: 1px solid; border-radius: 3px; padding: 8px; margin: 8px 0</code>.
 * Applied to an inline span inside a tight line, the top and bottom edges read as lines struck
 * through the text.</p>
 *
 * <p>The lesson is not "rename this one". A class name in a single stylesheet is a shared namespace,
 * and reusing a word that already means something is how a change to one part of a page redraws
 * another. This test asks the question generally: whatever class the reviewer row puts on the vendor,
 * the stylesheet must not give it a border, padding, margin or a text decoration.</p>
 */

const BOXY = ['border', 'padding', 'margin', 'display', 'text-decoration'];

function round(): RoundRecord {
  return {
    stage: 'PlanReview',
    number: 1,
    verdict: '',
    gatingCount: 0,
    reviewers: '',
    status: 'running',
    startedUtc: new Date().toISOString(),
    completedUtc: '',
    reviewerStates: [{ provider: 'codex', role: 'PlanCritique', status: 'running', findings: 0, note: '' }],
  } as RoundRecord;
}

function state(): PanelState {
  const session: SessionFile = {
    state: { sessionId: 's1', repoPath: 'D:/repo', branch: 'main', stage: 'PlanReview', awaitingResolve: false },
    rounds: [round()],
  } as unknown as SessionFile;

  return {
    settings: DEFAULTS,
    vendors: [],
    codexModels: [],
    localEngines: {},
    server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
    side: '',
    latestServerVersion: '',
    questions: [],
    sessions: [session],
    openSections: ['rounds'],
    usage: [],
    usageWindow: 'week',
    cliStatus: {},
    modelPrices: {},
    snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
  } as unknown as PanelState;
}

/** Every declaration block whose selector list mentions this class on its own. */
function rulesFor(css: string, className: string): string[] {
  return [...css.matchAll(/([^{}]+)\{([^}]*)\}/g)]
    .filter(([, selector]) => new RegExp(`\\.${className}(?![\\w-])`).test(selector!))
    // Only rules that could style the element ITSELF: `.x .child` styles the child, not `.x`.
    .filter(([, selector]) => selector!
      .split(',')
      .some((one) => new RegExp(`\\.${className}(?![\\w-])\\s*$`).test(one.trim())))
    .map(([, , body]) => body!);
}

test('the vendor word wears no box from anybody else’s class', () => {
  const html = panelHtml(state(), 'n0nce', Date.now());
  const css = html.split('</style>')[0] ?? '';

  const row = /<span class="([\w-]+)" style="color:[^"]*">codex<\/span>/.exec(html);
  assert.ok(row, 'the reviewer row no longer renders a coloured vendor span');
  const className = row[1]!;

  for (const body of rulesFor(css, className)) {
    for (const property of BOXY) {
      assert.ok(
        !new RegExp(`(^|;|\\s)${property}\\s*:`).test(body),
        `.${className} sets "${property}" — a word is not a card. Found: ${body.trim()}`,
      );
    }
  }
});

test('the class it uses is not the reviewer settings card', () => {
  // Named explicitly as well as checked generally, because this is the collision that happened and
  // the general check would pass again if `.vendor` ever lost its border for an unrelated reason.
  const html = panelHtml(state(), 'n0nce', Date.now());

  assert.ok(
    !/<span class="vendor" /.test(html),
    '`vendor` is the configuration card in this stylesheet; the rounds list must not borrow it',
  );
});
