import assert from 'node:assert/strict';
import { test } from 'node:test';

import { catalogHtml } from '../catalogPage';
import { KIMI_ON_LINUX_SINCE } from '../runtimeSince';
import { clearMarkerSaid, markerLine, pressClearMarker, type MarkerPorts } from '../kimiMarker';
import { PANEL_COMMANDS, type PanelState } from '../panelView';
import { parseProviders, type ProviderHealth, type ProvidersAnswer } from '../providers';
import { DEFAULT_VENDORS, type Vendor } from '../vendors';
import { pageTree } from './pageTree';
import { panelState, runPanel } from './panelPageHarness';

/**
 * The Kimi card's *Clear the stuck Kimi marker* button — E6 of todo/PLAN_kimi_and_vibe_on_linux.md, DL9, S-K18.
 *
 * <p>The field relied on is `providers[].survivorMarker` = `{ "path": "<marker file>", "pids": [<pid>, …] }` on the Kimi row of
 * `coai-mcp --providers` (A3's spec does not name a providers field; this is the proposal, flagged in E6's report). The exit codes
 * are A3-spec §7's: 0 cleared or no marker, 65 not a marker under the lock root, 74 a member could not be verified, 75 a member
 * still lives; stdout is `{"path","outcome":"cleared|kept|absent","exit","members":[{"pid","startedUtc","liveness","detail"}]}`.</p>
 */

const KIMI: Vendor = {
  id: 'kimi', runtime: 'kimi', model: 'kimi-code/k3-256k', enabled: true, plan: true, code: true,
  baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0,
};
const MARKER = '/home/p/.kimi-locks/0123456789abcdef.survivors.json';
const KNOWN = { kind: 'known' as const, version: KIMI_ON_LINUX_SINCE, remembered: false, updateOffered: false };

function answer(reported: Record<string, ProviderHealth>, answered = true): ProvidersAnswer {
  return { reported, asked: true, answered, notes: { unrecognised: [], vaultNote: '', vaultKeys: [], vault: 'not-reported', settingProblems: [] } };
}

const row = (extra: Partial<ProviderHealth> = {}): ProviderHealth => ({ provider: 'kimi', auth: 'cli', note: '', ...extra });

function stateWith(reported: Record<string, ProviderHealth>): PanelState {
  return { ...panelState('reviewers'), vendors: [DEFAULT_VENDORS[0]!, KIMI], server: KNOWN, providers: answer(reported) };
}

const cardOf = (state: PanelState) =>
  pageTree(runPanel(state, { html: catalogHtml(state, 'test-nonce', 'models') }).html).one((node) => node.dataset['modelCard'] === 'kimi', 'the Kimi card');

// ---------------------------------------------------------------- the providers field

test('the server’s survivorMarker is read whole: a path and its positive pids, and nothing from a half', () => {
  const body = (marker: unknown) => JSON.stringify({ providers: [{ provider: 'kimi', auth: 'cli', note: '', survivorMarker: marker }] });

  assert.deepEqual(parseProviders(body({ path: MARKER, pids: [4242, 4243] }))?.['kimi']?.survivorMarker, { path: MARKER, pids: [4242, 4243] });
  assert.deepEqual(parseProviders(body({ path: MARKER, pids: [] }))?.['kimi']?.survivorMarker, { path: MARKER, pids: [] }, 'a marker that names no process still stands');
  assert.deepEqual(parseProviders(body({ path: MARKER, pids: [7, 'x', -1, 1.5, null] }))?.['kimi']?.survivorMarker?.pids, [7]);
  for (const bad of [undefined, null, 'x', 7, [], {}, { path: '' }, { path: 7 }, { pids: [1] }]) {
    assert.equal(parseProviders(body(bad))?.['kimi']?.survivorMarker, undefined, `${JSON.stringify(bad)} drew a marker`);
  }
});

// ---------------------------------------------------------------- the button

test('the button is on the Kimi card only while the server reports a standing marker for it', () => {
  const withMarker = cardOf(stateWith({ kimi: row({ survivorMarker: { path: MARKER, pids: [4242] } }) }));
  const buttons = withMarker.find((node) => node.dataset['command'] === 'clearKimiMarker');

  assert.equal(buttons.length, 1, 'exactly one button while a marker stands');
  assert.equal(buttons[0]!.dataset['id'], 'kimi', 'the id is the row, never a path from the page');
  assert.match(buttons[0]!.text(), /Clear the stuck Kimi marker/u);
  assert.match(withMarker.text(), /pid 4242/u, 'the card names the pid');
  assert.ok(withMarker.text().includes(MARKER), 'and the file, so the way out by hand needs no button');

  for (const [why, reported] of [
    ['no marker reported', { kimi: row() }],
    ['the row not reported at all', {}],
    ['a marker on another runtime’s row', { codex: row({ provider: 'codex', survivorMarker: { path: MARKER, pids: [1] } }) }],
  ] as const) {
    assert.equal(cardOf(stateWith(reported)).find((node) => node.dataset['command'] === 'clearKimiMarker').length, 0, why);
  }
});

test('a marker reported on a row that is not Kimi draws no button there', () => {
  const state = { ...stateWith({ codex: row({ provider: 'codex', survivorMarker: { path: MARKER, pids: [1] } }) }), vendors: [DEFAULT_VENDORS[0]!, KIMI] };
  const codex = pageTree(runPanel(state, { html: catalogHtml(state, 'test-nonce', 'models') }).html).one((node) => node.dataset['modelCard'] === DEFAULT_VENDORS[0]!.id, 'the first card');

  assert.equal(codex.find((node) => node.dataset['command'] === 'clearKimiMarker').length, 0);
});

test('the command is one the panel host handles: it is in the exhaustive list the provider’s switch is checked against', () => {
  assert.ok((PANEL_COMMANDS as readonly string[]).includes('clearKimiMarker'));
});

test('the card’s line carries the same way out by hand as the Busy sentence: the file, the pids, and that deleting it is safe once they are gone', () => {
  const said = markerLine({ path: MARKER, pids: [4242, 4243] });

  assert.ok(said.includes(MARKER) && said.includes('4242, 4243'));
  assert.match(said, /delete the file yourself once they are/u);
  assert.match(markerLine({ path: MARKER, pids: [] }), /names no process/u);
});

// ---------------------------------------------------------------- the exit codes, in plain words

const members = (liveness: string, detail = '') => JSON.stringify({ path: MARKER, outcome: 'kept', exit: 0, members: [{ pid: 4242, startedUtc: '2026-10-16T10:00:00Z', liveness, detail }] });

test('exit 0 says the marker is cleared; a 0 for no marker says there was none', () => {
  assert.match(clearMarkerSaid(0, JSON.stringify({ outcome: 'cleared', members: [] })).title, /cleared: every process it named is gone/u);
  assert.equal(clearMarkerSaid(0, JSON.stringify({ outcome: 'cleared', members: [] })).as, 'information');
  assert.match(clearMarkerSaid(0, JSON.stringify({ outcome: 'absent', members: [] })).title, /no marker to clear/u);
  // N1: 0 also means "no marker there", so an unreadable report may not claim the processes are gone.
  for (const unreadable of ['', 'not json', '{}', JSON.stringify({ outcome: 'kept', members: [] })]) {
    const said = clearMarkerSaid(0, unreadable);

    assert.doesNotMatch(said.title, /every process it named is gone|is cleared/u, `exit 0 with ${JSON.stringify(unreadable)} claimed a clear`);
    assert.match(said.title, /report could not be read/u);
  }
});

test('exit 75 names the living pid and that coai ends nothing; the marker stays', () => {
  const said = clearMarkerSaid(75, members('alive'));

  assert.equal(said.as, 'warning');
  assert.match(said.title, /marker stays: pid 4242 is still running/u);
  assert.match(said.title, /never ends a process/u);
});

test('exit 74 names the process that could not be checked, with the reason, and that the marker stays', () => {
  const said = clearMarkerSaid(74, members('unknown', 'errno 13 (EACCES) reading /proc/4242/stat'));

  assert.equal(said.as, 'warning');
  assert.match(said.title, /marker stays: coai-mcp could not check pid 4242 \(errno 13 \(EACCES\) reading \/proc\/4242\/stat\)/u);
  assert.match(said.title, /not known to be gone/u);
});

test('exit 65 says nothing was deleted; an unknown code (64 from an old coai-mcp) says the manual way out still works', () => {
  assert.match(clearMarkerSaid(65, '').title, /Nothing was deleted/u);
  for (const code of [64, 1]) {
    const said = clearMarkerSaid(code, '');

    assert.equal(said.as, 'warning');
    assert.ok(said.title.includes(`exit ${code}`), said.title);
    assert.match(said.title, /safe to delete once the processes it names are gone/u);
  }
});

// ---------------------------------------------------------------- a press, against a fake server

function ports(providers: ProvidersAnswer, run: { code: number; output: string }): { readonly ports: MarkerPorts; readonly ran: string[] } {
  const ran: string[] = [];

  return { ran, ports: { providers: async () => providers, clear: async (path) => { ran.push(path); return run; } } };
}

test('a press runs the mode on the path the SERVER reports for the row, and says the exit code in words', async () => {
  const fake = ports(answer({ kimi: row({ survivorMarker: { path: MARKER, pids: [4242] } }) }), { code: 75, output: members('alive') });
  const said = await pressClearMarker('kimi', fake.ports);

  assert.deepEqual(fake.ran, [MARKER]);
  assert.match(said.title, /pid 4242 is still running/u);
});

test('a press when the server no longer reports a marker runs nothing and says so', async () => {
  const fake = ports(answer({ kimi: row() }), { code: 0, output: '' });
  const said = await pressClearMarker('kimi', fake.ports);

  assert.deepEqual(fake.ran, [], 'a stale button ran the mode');
  assert.match(said.title, /no marker to clear/u);
});

test('a press when the server did not answer runs nothing: a path nobody reported is never invented', async () => {
  const fake = ports(answer({ kimi: row({ survivorMarker: { path: MARKER, pids: [1] } }) }, false), { code: 0, output: '' });

  await pressClearMarker('kimi', fake.ports);

  assert.deepEqual(fake.ran, []);
});

test('the press hands the server’s path for THIS row, not another row’s', async () => {
  const other = '/home/p/.kimi-locks/ffffffffffffffff.survivors.json';
  const fake = ports(answer({ kimi: row({ survivorMarker: { path: MARKER, pids: [1] } }), 'kimi-2': row({ provider: 'kimi-2', survivorMarker: { path: other, pids: [2] } }) }), { code: 0, output: JSON.stringify({ outcome: 'cleared', members: [] }) });

  await pressClearMarker('kimi-2', fake.ports);

  assert.deepEqual(fake.ran, [other]);
});

test('a press when the providers question was NOT answered says so — never "no marker" — and runs nothing (S4)', async () => {
  const fake = ports(answer({ kimi: row({ survivorMarker: { path: MARKER, pids: [1] } }) }, false), { code: 0, output: '' });
  const said = await pressClearMarker('kimi', fake.ports);

  assert.deepEqual(fake.ran, []);
  assert.equal(said.as, 'warning');
  assert.match(said.title, /did not answer the providers question, so nothing was run/u);
  assert.match(said.title, /If a marker stands it still stands — the Busy sentence names the file/u);
  assert.doesNotMatch(said.title, /no marker|no longer/u, 'an unanswered question is not an absent marker');
  assert.match((await pressClearMarker('kimi', ports(answer({}), { code: 0, output: '' }).ports)).title, /no marker to clear/u, 'while an ANSWERED question with no marker still says so');
});

test('a clear mode that never answered (code -1: a timeout or a spawn that failed) says nothing is known and the card shows what stands (N1)', () => {
  const said = clearMarkerSaid(-1, '');

  assert.equal(said.as, 'warning');
  assert.match(said.title, /did not answer within 20 seconds/u);
  assert.match(said.title, /the card shows what stands/u);
});
