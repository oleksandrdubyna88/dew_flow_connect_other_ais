import assert from 'node:assert/strict';
import { test } from 'node:test';

import { type CheckRecord, parseCheckDocument, parseConsultantsAnswer } from '../consultantHealth';
import type { ConsultantHealthState, OtherSideHealth, ProbeShown, ThisSideHealth } from '../consultantHealthState';
import { type Control, type Page, click, panelState, runPanel, work } from './panelPageHarness';

/**
 * The Consultant tab's health block, RUN — the Settings page's own script over the markup the panel really renders
 * (E5.2/E5.3 of PLAN_the_consultant_works_on_every_vendor.md).
 *
 * <p>`.agents/PROJECT.md` refuses a new behavioural assertion over page source text: a template literal contains every
 * button it was meant to contain, wired or not. So the buttons below are PARSED out of the page and clicked through the
 * page's own `bindCommands`, and the `copied` acknowledgement is delivered to the page's own message listener. Ask of
 * each assertion what it would see if the behaviour were deleted: no Check button for that id, no post, a button that
 * is not disabled, a button where there must be none.</p>
 */

/** The shipped consultant map (`SHIPPED_PAIRS`): claude, gemini and other ask codex; codex asks claude. */
const SHIPPED: Readonly<Record<string, { readonly vendor: string; readonly model: string; readonly runtime: string }>> = {
  claude: { vendor: 'codex', model: '', runtime: 'codex' },
  codex: { vendor: 'claude', model: '', runtime: 'claude' },
  gemini: { vendor: 'codex', model: '', runtime: 'codex' },
  other: { vendor: 'codex', model: '', runtime: 'codex' },
};

function row(kind: string, over: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    callerKind: kind,
    available: true,
    ...SHIPPED[kind],
    cli: { probed: true, found: true, version: '1.0.0', authSource: 'own auth', note: '' },
    agy: null,
    ...over,
  };
}

function answered(rows: readonly Record<string, unknown>[], side = 'windows'): ProbeShown {
  const answer = parseConsultantsAnswer(0, JSON.stringify({ utc: '2026-10-03T10:00:00.0000000Z', side, consultants: rows }));
  assert.ok(answer.kind === 'answered', 'the fixture report does not parse');

  return answer;
}

const ALL = Object.keys(SHIPPED);

function thisSide(over: Partial<ThisSideHealth> = {}): ThisSideHealth {
  return { label: 'Windows', probe: answered(ALL.map((kind) => row(kind))), files: { report: undefined, checks: {} }, checking: [], runs: {}, ...over };
}

function run(over: Partial<ThisSideHealth> = {}, otherSides: readonly OtherSideHealth[] = []): Page {
  const health: ConsultantHealthState = { thisSide: thisSide(over), otherSides, nowMs: Date.parse('2026-10-03T10:01:00.0000000Z') };

  return runPanel(panelState('consultant', { consultantHealth: health }));
}

function buttons(page: Page, command: string): readonly Control[] {
  return page.commands.filter((one) => one.dataset['command'] === command);
}

function check(over: Record<string, unknown>): CheckRecord {
  const parsed = parseCheckDocument(JSON.stringify({ callerKind: 'claude', state: 'checking', ...over }));
  assert.ok(parsed !== undefined, 'the fixture check does not parse');

  return parsed;
}

test('every caller row has exactly one Check, carrying its caller kind', () => {
  const page = run();

  assert.deepEqual(buttons(page, 'checkConsultant').map((one) => one.dataset['id']), ALL,
    'a row without its own Check cannot be checked, and two would run two paid turns');
});

test('pressing Check posts the command for THAT caller kind', () => {
  const page = run();

  click(page, 'checkConsultant', 'gemini');

  assert.deepEqual(work(page).at(-1), { type: 'command', command: 'checkConsultant', id: 'gemini' });
});

test('while a check runs its button is drawn disabled, and the others are not', () => {
  const page = run({ checking: ['codex'] });
  const by = (id: string): Control | undefined => buttons(page, 'checkConsultant').find((one) => one.dataset['id'] === id);

  assert.equal(by('codex')?.disabled, true, 'a second press while checking is a second paid turn');
  assert.equal(by('codex')?.textContent, 'Checking…');
  assert.equal(by('claude')?.disabled, false);
});

test('the server\'s settled checking disables the button after a reload, with no flag of this window\'s', () => {
  const running = check({ heartbeatUtc: '2026-10-03T10:00:45.0000000Z', startedUtc: '2026-10-03T10:00:00.0000000Z' });
  const page = run({ probe: answered(ALL.map((kind) => (kind === 'claude' ? row(kind, { check: running }) : row(kind)))) });

  assert.equal(buttons(page, 'checkConsultant').find((one) => one.dataset['id'] === 'claude')?.disabled, true);
});

test('a server too old for the mode offers no Check at all, and says to update it', () => {
  const page = run({ probe: { kind: 'too-old' } });

  assert.equal(buttons(page, 'checkConsultant').length, 0);
  assert.match(page.html, /update the MCP server/u);
});

const WSL = '\\\\wsl.localhost\\Ubuntu\\home\\u\\.local\\share\\coai-mcp';

function wslSide(): OtherSideHealth {
  const report = answered(ALL.map((kind) => row(kind)), 'wsl');

  return {
    label: 'WSL: Ubuntu',
    dir: WSL,
    files: { report: report.kind === 'answered' ? { kind: 'found', value: report.report } : undefined, checks: {} },
  };
}

test('another side\'s block is drawn read-only: no Check button in it, so only this side\'s four remain', () => {
  const page = run({}, [wslSide()]);

  assert.deepEqual(buttons(page, 'checkConsultant').map((one) => one.dataset['id']), ALL,
    'a Check drawn in the WSL block would run on THIS side while claiming to check that one');
  assert.match(page.html, /WSL: Ubuntu/u);
  assert.match(page.html, /Remote-WSL/u);
});

test('a failure the server reports for a consultant this row no longer names is not on the page', () => {
  // The server decides whether a failure is CURRENT, per the consultant its row names (ConsultHealth.Current); what the
  // panel still hides is a row whose facts are about another consultant than the one the person has since chosen.
  const failure = {
    utc: '2026-10-03T09:30:00.0000000Z', kind: 'command-denied', vendor: 'antigravity', model: 'gemini-3.1-pro-high',
    what: 'THE-OLD-VENDOR-FAILED', cure: 'x', evidence: '',
  };
  const stale = { vendor: 'antigravity', model: 'gemini-3.1-pro-high', runtime: 'antigravity', lastFailure: failure, failureCurrent: true };
  const page = run({ probe: answered(ALL.map((kind) => row(kind, kind === 'claude' ? stale : {}))) });

  assert.doesNotMatch(page.html, /THE-OLD-VENDOR-FAILED/u, 'the claude row names codex now; a failure of agy is not about it');
});

test('the same failure, of the consultant the row DOES name, is on the page — so the absence above is a filter, not a block that draws nothing', () => {
  const failure = {
    utc: '2026-10-03T09:30:00.0000000Z', kind: 'command-denied', vendor: 'codex', model: '',
    what: 'THE-CURRENT-VENDOR-FAILED', cure: 'THE-CURE', evidence: '',
  };
  const page = run({ probe: answered(ALL.map((kind) => row(kind, kind === 'claude' ? { lastFailure: failure, failureCurrent: true } : {}))) });

  assert.match(page.html, /THE-CURRENT-VENDOR-FAILED/u, 'a current failure of the row\'s own consultant never reached the page');
  assert.match(page.html, /THE-CURE/u);
});

test('the confinement line and the allow rule\'s text are on the page when the server sent them', () => {
  const limitation = { standing: 'unconfined', text: 'THE-LIMITATION-TEXT', evidence: 'source', source: 'documented upstream' };
  const page = run({
    probe: answered(ALL.map((kind) => row(kind, kind === 'claude'
      ? { limitation, runtime: 'antigravity', agy: { settingsPath: '/home/u/s.json', snippet: 'command(THE-RULE)', snippetWarning: 'not read-only' } }
      : {}))),
  });

  assert.match(page.html, /THE-LIMITATION-TEXT/u, 'the limitation the server reported was never drawn');
  assert.match(page.html, /<pre>command\(THE-RULE\)<\/pre>/u, 'the rule the Copy button copies was not shown to the person');
});

test('every value reaches the page escaped — a vendor\'s sentence cannot open a tag', () => {
  const page = run({ probe: answered(ALL.map((kind) => row(kind, kind === 'claude' ? { available: false, reason: '<img src=x onerror=alert(1)>' } : {}))) });

  assert.doesNotMatch(page.html, /<img src=x/u);
  assert.match(page.html, /&lt;img src=x/u);
});

function withSnippet(): Partial<ThisSideHealth> {
  return {
    probe: answered(ALL.map((kind) => row(kind, kind === 'claude'
      ? { runtime: 'antigravity', vendor: 'codex', agy: { settingsPath: '/home/u/.gemini/antigravity-cli/settings.json', snippet: 'command(git grep)', snippetWarning: 'a prefix rule is not read-only' } }
      : {}))),
  };
}

test('the allow-rule snippet has its own Copy, by caller kind, and the click posts it', () => {
  const page = run(withSnippet());

  assert.deepEqual(buttons(page, 'copyConsultantSnippet').map((one) => one.dataset['id']), ['claude']);
  click(page, 'copyConsultantSnippet', 'claude');
  assert.deepEqual(work(page).at(-1), { type: 'command', command: 'copyConsultantSnippet', id: 'claude' });
});

test('a copied message naming the snippet command relabels the snippet\'s Copy, and only that button', () => {
  const page = run(withSnippet());
  const copy = buttons(page, 'copyConsultantSnippet')[0];
  const checkClaude = buttons(page, 'checkConsultant').find((one) => one.dataset['id'] === 'claude');

  page.deliver({ type: 'copied', command: 'copyConsultantSnippet', id: 'claude' });

  assert.equal(copy?.textContent, 'Copied', 'the person was never told the rule reached the clipboard');
  assert.equal(checkClaude?.textContent, 'Check', 'a button of another command with the same id was relabelled');
  page.clock.advance(5000);
  assert.equal(copy?.textContent, 'Copy', 'the button says Copied for ever');
});

test('the old copied message — no command named — still acknowledges a phrase (the snippet is on the Settings page — the snippet test above keeps two commands with one id apart)', () => {
  const page = runPanel(panelState('phrases', { phrases: [{ id: 'claude', name: 'Ship it', text: 'make a pr' }] }));
  const phrase = buttons(page, 'copyPhrase')[0];

  page.deliver({ type: 'copied', id: 'claude' });

  assert.equal(phrase?.textContent, 'Copied', 'a host that predates the command field stopped confirming phrase copies');
});

test('a copied message naming a command that is not a copy changes nothing', () => {
  const page = run(withSnippet());

  page.deliver({ type: 'copied', command: 'checkConsultant', id: 'claude' });

  assert.deepEqual(buttons(page, 'checkConsultant').map((one) => one.textContent), ALL.map(() => 'Check'));
});

/** Every side's health block, as the page drew it. */
function healthSections(html: string): readonly string[] {
  return html.match(/<section class="consultant-health[\s\S]*?<\/section>/gu) ?? [];
}

test('every side block opens with a heading of its own — a screen reader can jump to it by its side (P)', () => {
  const page = run({}, [wslSide()]);
  const sections = healthSections(page.html);

  assert.ok(sections.length >= 8, 'the page drew no health blocks, so this test asserts nothing');
  for (const section of sections) {
    assert.match(section, /^<section[^>]*>\s*<h4 class="health-side">On (Windows|WSL: Ubuntu)/u, 'a side label that is not a heading cannot be navigated to');
  }
});

test('no health block claims to announce its check — a region rebuilt on every repaint never speaks (P)', () => {
  const page = run({ checking: ['codex'] });

  for (const section of healthSections(page.html)) {
    assert.doesNotMatch(section, /aria-live|role="status"/u, 'a live region replaced with the whole document is announced to nobody');
  }
});
