import assert from 'node:assert/strict';
import { test } from 'node:test';

import { catalogHtml } from '../catalogPage';
import { type CheckRecord, parseCheckDocument, parseConsultantsAnswer } from '../consultantHealth';
import type { ConsultantHealthState, OtherSideHealth, ProbeShown, ThisSideHealth } from '../consultantHealthState';
import { type PanelState } from '../panelView';
import { settingsFrom } from '../settingsShape';
import { DEFAULT_VENDORS, type Vendor } from '../vendors';
import { type Control, type Page, click, panelState, runPanel, work } from './panelPageHarness';
import { pageTree, type PageNode } from './pageTree';

/**
 * The Consultant tab's health block, RUN — the Settings page's own script over the markup the panel really renders
 * (E5.2/E5.3 of PLAN_the_consultant_works_on_every_vendor.md) — on BOTH pages that draw it, from one table.
 *
 * <p>`.agents/PROJECT.md` refuses a new behavioural assertion over page source text: a template literal contains every
 * button it was meant to contain, wired or not. So the buttons below are PARSED out of the page and clicked through the
 * page's own `bindCommands`, and the `copied` acknowledgement is delivered to the page's own message listener. Ask of
 * each assertion what it would see if the behaviour were deleted: no Check button for that id, no post, a button that
 * is not disabled, a button where there must be none.</p>
 *
 * <p><b>Why every case runs twice.</b> The current page draws the block under each caller's own definition; the new
 * page's Consultants › Consultant tab (todo/PLAN_one_model_catalog.md E4.2) drew picks only, so a person on the new page
 * had no Check, no health and no allow rule — and E5.1 step 5, which deletes the current page, would have deleted the
 * feature with it. E5.1b put the same block under each pick. Run against one page only, a case is a promise about that
 * page; run from {@link PAGES}, it is a promise about the block, wherever a person meets it.</p>
 */

/** A page that draws the Consultant tab, and how a test draws it from a panel state. */
interface ConsultantPage {
  readonly name: string;
  readonly draw: (state: PanelState) => string;
}

/**
 * The pages a person meets the consultant health block on — every case below runs on each. One since E5.1 step 5 removed
 * the current page; kept a table, so a second surface that draws the block is one row, not a copy of every case.
 */
const PAGES: readonly ConsultantPage[] = [
  { name: 'the Settings page', draw: (state) => catalogHtml(state, 'test-nonce', 'consultants/consultant') },
];

/** One case, registered once per page — so a page that stops drawing the block fails by its own name. */
function onBothPages(name: string, body: (page: ConsultantPage) => void): void {
  for (const page of PAGES) {
    test(`${page.name}: ${name}`, () => { body(page); });
  }
}

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

function healthOf(over: Partial<ThisSideHealth>, otherSides: readonly OtherSideHealth[]): ConsultantHealthState {
  return { thisSide: thisSide(over), otherSides, nowMs: Date.parse('2026-10-03T10:01:00.0000000Z') };
}

/** The page, drawn on its Consultant tab with this health, and running. */
function run(on: ConsultantPage, over: Partial<ThisSideHealth> = {}, otherSides: readonly OtherSideHealth[] = [], base: Partial<PanelState> = {}): Page {
  const state = panelState('consultant', { ...base, consultantHealth: healthOf(over, otherSides) });

  return runPanel(state, { html: on.draw(state) });
}

function buttons(page: Page, command: string): readonly Control[] {
  return page.commands.filter((one) => one.dataset['command'] === command);
}

function check(over: Record<string, unknown>): CheckRecord {
  const parsed = parseCheckDocument(JSON.stringify({ callerKind: 'claude', state: 'checking', ...over }));
  assert.ok(parsed !== undefined, 'the fixture check does not parse');

  return parsed;
}

onBothPages('every caller row has exactly one Check, carrying its caller kind', (on) => {
  const page = run(on);

  assert.deepEqual(buttons(page, 'checkConsultant').map((one) => one.dataset['id']), ALL,
    'a row without its own Check cannot be checked, and two would run two paid turns');
});

onBothPages('pressing Check posts the command for THAT caller kind', (on) => {
  const page = run(on);

  click(page, 'checkConsultant', 'gemini');

  assert.deepEqual(work(page).at(-1), { type: 'command', command: 'checkConsultant', id: 'gemini' });
});

onBothPages('while a check runs its button is drawn disabled, and the others are not', (on) => {
  const page = run(on, { checking: ['codex'] });
  const by = (id: string): Control | undefined => buttons(page, 'checkConsultant').find((one) => one.dataset['id'] === id);

  assert.equal(by('codex')?.disabled, true, 'a second press while checking is a second paid turn');
  assert.equal(by('codex')?.textContent, 'Checking…');
  assert.equal(by('claude')?.disabled, false);
});

onBothPages('the server\'s settled checking disables the button after a reload, with no flag of this window\'s', (on) => {
  const running = check({ heartbeatUtc: '2026-10-03T10:00:45.0000000Z', startedUtc: '2026-10-03T10:00:00.0000000Z' });
  const page = run(on, { probe: answered(ALL.map((kind) => (kind === 'claude' ? row(kind, { check: running }) : row(kind)))) });

  assert.equal(buttons(page, 'checkConsultant').find((one) => one.dataset['id'] === 'claude')?.disabled, true);
});

onBothPages('a server too old for the mode offers no Check at all, and says to update it', (on) => {
  const page = run(on, { probe: { kind: 'too-old' } });

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

onBothPages('another side\'s block is drawn read-only: no Check button in it, so only this side\'s four remain', (on) => {
  const page = run(on, {}, [wslSide()]);

  assert.deepEqual(buttons(page, 'checkConsultant').map((one) => one.dataset['id']), ALL,
    'a Check drawn in the WSL block would run on THIS side while claiming to check that one');
  assert.match(page.html, /WSL: Ubuntu/u);
  assert.match(page.html, /Remote-WSL/u);
});

onBothPages('a failure the server reports for a consultant this row no longer names is not on the page', (on) => {
  // The server decides whether a failure is CURRENT, per the consultant its row names (ConsultHealth.Current); what the
  // panel still hides is a row whose facts are about another consultant than the one the person has since chosen.
  const failure = {
    utc: '2026-10-03T09:30:00.0000000Z', kind: 'command-denied', vendor: 'antigravity', model: 'gemini-3.1-pro-high',
    what: 'THE-OLD-VENDOR-FAILED', cure: 'x', evidence: '',
  };
  const stale = { vendor: 'antigravity', model: 'gemini-3.1-pro-high', runtime: 'antigravity', lastFailure: failure, failureCurrent: true };
  const page = run(on, { probe: answered(ALL.map((kind) => row(kind, kind === 'claude' ? stale : {}))) });

  assert.match(page.html, /class="consultant-health/u, 'the page drew no health block, so the absence below proves nothing');
  assert.doesNotMatch(page.html, /THE-OLD-VENDOR-FAILED/u, 'the claude row names codex now; a failure of agy is not about it');
});

onBothPages('the same failure, of the consultant the row DOES name, is on the page — so the absence above is a filter, not a block that draws nothing', (on) => {
  const failure = {
    utc: '2026-10-03T09:30:00.0000000Z', kind: 'command-denied', vendor: 'codex', model: '',
    what: 'THE-CURRENT-VENDOR-FAILED', cure: 'THE-CURE', evidence: '',
  };
  const page = run(on, { probe: answered(ALL.map((kind) => row(kind, kind === 'claude' ? { lastFailure: failure, failureCurrent: true } : {}))) });

  assert.match(page.html, /THE-CURRENT-VENDOR-FAILED/u, 'a current failure of the row\'s own consultant never reached the page');
  assert.match(page.html, /THE-CURE/u);
});

/** A catalog row ticked Consultant, with a model of its own — what the new page's picker offers a caller (E4.2). */
const DEEP: Vendor = { ...DEFAULT_VENDORS[0]!, id: 'deep-high', runtime: 'codex', model: 'gpt-deep-1', plan: false, code: false, uses: ['consultant'] };

/** The panel's state with claude's consultant picked as {@link DEEP}: stored as a bare reference, the model the row's. */
function pickedDeep(): Partial<PanelState> {
  const rows = [...DEFAULT_VENDORS, DEEP];
  const stored: Readonly<Record<string, unknown>> = { vendors: rows, consultants: { claude: { vendor: 'deep-high' } } };

  return { settings: settingsFrom((key) => stored[key]), vendors: rows, catalogRows: rows };
}

onBothPages('a pick that is a catalog row shows the failure the server reports for THAT row — matched by the pair the pick resolves to', (on) => {
  // The identity risk of E5.1b: the pick is stored as `{ vendor: 'deep-high' }` with no model, and the server reports the
  // consultant it RESOLVED — the row's id and the row's model. Matched against the stored entry (model ''), the block
  // would hide its own row's failure as "about another consultant". Matched against `consult.byCaller`, it is shown.
  const failure = {
    utc: '2026-10-03T09:30:00.0000000Z', kind: 'command-denied', vendor: 'deep-high', model: 'gpt-deep-1',
    what: 'THE-PICKED-ROW-FAILED', cure: 'THE-ROW-CURE', evidence: '',
  };
  const deep = { vendor: 'deep-high', model: 'gpt-deep-1', runtime: 'codex', lastFailure: failure, failureCurrent: true };
  const page = run(on, { probe: answered(ALL.map((kind) => row(kind, kind === 'claude' ? deep : {}))) }, [], pickedDeep());

  const claude = healthUnder(page, 'claude');
  assert.match(claude, /THE-PICKED-ROW-FAILED/u, 'the picked row\'s own failure was filtered away as another consultant\'s');
  assert.match(claude, /THE-ROW-CURE/u);
  assert.doesNotMatch(claude, /which is not what this row names now/u, 'the picked row\'s own facts were called another consultant\'s');
});

/**
 * The text of one caller's health block as the page shows it: the block under THAT caller's own pick — on either page the
 * pick's select is a direct child of the caller's row — so a failure drawn under another caller is not read as this one's.
 *
 * <p>The drawn tree IS what the running page shows here (PR #711, CodeRabbit): the page's script changes content only in
 * its live regions (`live-*`, read through `page.region`) and the nodes it inserts beside a control, and the block is
 * asserted to sit in no live region, so nothing the script does can replace it after load.</p>
 */
function healthUnder(page: Page, caller: string): string {
  const tree = pageTree(page.html);
  const row = tree.one((node) => node.tagName === 'DIV' && node.children.some((child) => child.tagName === 'SELECT' && child.dataset.caller === caller), `${caller}'s row`);
  const sections = row.find((node) => node.tagName === 'SECTION' && node.className.split(' ').includes('consultant-health'));
  assert.ok(sections.length > 0, `${caller}'s row draws no health block`);
  const live: readonly PageNode[] = tree.find((node) => node.id.startsWith('live-')).flatMap((region) => region.all());
  assert.ok(sections.every((section) => !live.includes(section)), `${caller}'s health block sits in a live region, so the script can replace it after load`);

  return sections.map((section) => section.text()).join('\n');
}

onBothPages('the confinement line and the allow rule\'s text are on the page when the server sent them', (on) => {
  const limitation = { standing: 'unconfined', text: 'THE-LIMITATION-TEXT', evidence: 'source', source: 'documented upstream' };
  const page = run(on, {
    probe: answered(ALL.map((kind) => row(kind, kind === 'claude'
      ? { limitation, runtime: 'antigravity', agy: { settingsPath: '/home/u/s.json', snippet: 'command(THE-RULE)', snippetWarning: 'not read-only' } }
      : {}))),
  });

  assert.match(page.html, /THE-LIMITATION-TEXT/u, 'the limitation the server reported was never drawn');
  assert.match(page.html, /<pre>command\(THE-RULE\)<\/pre>/u, 'the rule the Copy button copies was not shown to the person');
});

onBothPages('every value reaches the page escaped — a vendor\'s sentence cannot open a tag', (on) => {
  const page = run(on, { probe: answered(ALL.map((kind) => row(kind, kind === 'claude' ? { available: false, reason: '<img src=x onerror=alert(1)>' } : {}))) });

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

onBothPages('the allow-rule snippet has its own Copy, by caller kind, and the click posts it', (on) => {
  const page = run(on, withSnippet());

  assert.deepEqual(buttons(page, 'copyConsultantSnippet').map((one) => one.dataset['id']), ['claude']);
  click(page, 'copyConsultantSnippet', 'claude');
  assert.deepEqual(work(page).at(-1), { type: 'command', command: 'copyConsultantSnippet', id: 'claude' });
});

onBothPages('a copied message naming the snippet command relabels the snippet\'s Copy, and only that button', (on) => {
  const page = run(on, withSnippet());
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

onBothPages('a copied message naming a command that is not a copy changes nothing', (on) => {
  const page = run(on, withSnippet());

  page.deliver({ type: 'copied', command: 'checkConsultant', id: 'claude' });

  const labels = buttons(page, 'checkConsultant').map((one) => one.textContent);
  assert.equal(labels.length, ALL.length, 'the page drew no Check, so "nothing changed" was never asked of one');
  assert.deepEqual(labels, ALL.map(() => 'Check'));
});

/** Every side's health block, as the page drew it. */
function healthSections(html: string): readonly string[] {
  return html.match(/<section class="consultant-health[\s\S]*?<\/section>/gu) ?? [];
}

onBothPages('every side block opens with a heading of its own — a screen reader can jump to it by its side (P)', (on) => {
  const page = run(on, {}, [wslSide()]);
  const sections = healthSections(page.html);

  assert.ok(sections.length >= 8, 'the page drew no health blocks, so this test asserts nothing');
  for (const section of sections) {
    assert.match(section, /^<section[^>]*>\s*<h4 class="health-side">On (Windows|WSL: Ubuntu)/u, 'a side label that is not a heading cannot be navigated to');
  }
});

onBothPages('no health block claims to announce its check — a region rebuilt on every repaint never speaks (P)', (on) => {
  const page = run(on, { checking: ['codex'] });
  const sections = healthSections(page.html);

  assert.ok(sections.length > 0, 'the page drew no health blocks, so this test asserts nothing');
  for (const section of sections) {
    assert.doesNotMatch(section, /aria-live|role="status"/u, 'a live region replaced with the whole document is announced to nobody');
  }
});
