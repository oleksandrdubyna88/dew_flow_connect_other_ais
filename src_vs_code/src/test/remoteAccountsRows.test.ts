import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import { ACCOUNTS_UNREADABLE_SAID, ACCOUNTS_SECONDS, MANAGER_WORDS, readAccounts } from '../remoteAccountsReply';
import type { AccountsRead, ClaudeUsage, CodexUsage } from '../remoteAccountsReply';
import type { AccountsState, HostMemory, Memory } from '../remoteAccountsRound';
import { INSTALL_JQ, NO_MANAGER_SAID, USAGE_OFF_SAID, accountsRows, forList, noticesDue } from '../remoteAccountsRows';
import type { AccountsView, Row } from '../remoteAccountsRows';
import { HOSTS_SAID } from '../remoteHostsSaid';
import { dayOf } from '../remoteHosts';
import type { HostStanding } from '../remoteHosts';
import { frame, ok } from './accountsFrames';
import { sourceOf } from './sourceReading';

/**
 * The rows of the accounts list (E1.S4, A4-T4 … A4-T14 and the row half of A4-T20): pure, over a view built by the test and a clock the test
 * hands in. Every email, organization and host here is made up.
 */

const NOW = Date.UTC(2026, 10, 24, 12, 0, 0);
const min = (n: number): number => n * 60_000;
const hour = (n: number): number => n * 3_600_000;

type Reading = Extract<AccountsRead, { kind: 'read' }>;
type Claude = Reading['claude'];

const ANN = 'ann@example.test';
const ORG = 'Example Org';

const claude = (over: Partial<Extract<Claude, { state: 'found' }>> = {}): Claude =>
  ({ state: 'found', version: '2.1.5', email: ANN, org: ORG, plan: null, usage: { state: 'skipped' }, ...over });

const reading = (over: Partial<Reading> = {}): Reading => ({ kind: 'read', claude: { state: 'absent' }, codex: { state: 'absent' }, agy: { state: 'absent' }, jqMissing: undefined, ...over });

const usageOf = (five: number | null, week: number | null, fable: number | null, nowMs = NOW): ClaudeUsage => ({
  state: 'read',
  unmatched: 0,
  windows: [
    { name: '5h', usedPercent: five, resetsAtMs: nowMs + min(130) },
    { name: 'week', usedPercent: week, resetsAtMs: nowMs + min(3_800) },
    { name: { model: 'Fable' }, usedPercent: fable, resetsAtMs: null },
  ],
});

const seen = (read: Reading, ageMin: number, over: Partial<HostMemory> = {}): HostMemory => ({ reading: { read, atMs: NOW - min(ageMin) }, ...over });

const view = (over: Partial<AccountsView> = {}): AccountsView =>
  ({ live: true, stopped: false, busy: false, memory: new Map(), standings: [], hosts: [], ignored: undefined, allowNew: true, ...over });

const memoryOf = (...entries: readonly (readonly [string, HostMemory])[]): Memory => new Map(entries);

const person = (host: string, answer: 'yes' | 'no' = 'yes'): HostStanding => ({ host, answer, how: 'person', askedUtc: '2026-11-20T10:00:00.000Z' });
const auto = (host: string, askedUtc = '2026-11-21T09:00:00.000Z'): HostStanding => ({ host, answer: 'yes', how: 'auto', askedUtc });

const rowsOf = (over: Partial<AccountsView>, nowMs = NOW): readonly Row[] => accountsRows(view(over), nowMs);
const labelsOf = (rows: readonly Row[]): readonly string[] => rows.map((row) => row.label);
const separators = (rows: readonly Row[]): readonly string[] => rows.filter((row) => row.kind === 'separator').map((row) => row.label);
const usageRows = (rows: readonly Row[]): readonly Extract<Row, { kind: 'usage' }>[] => rows.filter((row): row is Extract<Row, { kind: 'usage' }> => row.kind === 'usage');
const hostRow = (rows: readonly Row[], host: string): Extract<Row, { kind: 'host' }> => {
  const found = rows.find((row): row is Extract<Row, { kind: 'host' }> => row.kind === 'host' && row.host === host);
  assert.ok(found !== undefined, `no row for ${host}`);

  return found;
};
/** Everything a person can read in the rows, joined. */
const allText = (rows: readonly Row[]): string => rows.map((row) => [row.label, 'description' in row ? row.description : '', 'detail' in row ? row.detail : ''].join(' ')).join('\n');

// ───────────────── A4-T4: the Claude row is the mod's line ─────────────────

test('A4-T4: the Claude row is the mod\'s line, from "used" to "left", with the week\'s reset in the description', () => {
  const memory = memoryOf(['hA', seen(reading({ claude: claude({ usage: usageOf(56, 77, 49) }) }), 4)]);
  const rows = rowsOf({ memory, hosts: ['hA'], standings: [person('hA')] });
  const [row] = usageRows(rows);

  assert.ok(row !== undefined, 'no usage row was drawn');
  assert.equal(row.label, '5h 44% left · resets in 2h 10m · week 23% left · Fable 51% left', 'the list says 56% left where 44% is left');
  assert.equal(row.description, 'week resets in 2d 15h');
  assert.deepEqual(separators(rows).slice(0, 1), [`Claude Code · ${ANN} · ${ORG}`]);
});

test('A4-T4: with no session window the row says so with dashes, and the reset text is made at DRAW time', () => {
  const noSession: ClaudeUsage = { state: 'read', unmatched: 0, windows: [{ name: 'week', usedPercent: 10, resetsAtMs: NOW + min(120) }] };
  const lone = memoryOf(['hA', seen(reading({ claude: claude({ usage: noSession }) }), 1)]);
  assert.equal(usageRows(rowsOf({ memory: lone, hosts: ['hA'] }))[0]?.label, '5h — · resets in — · week 90% left · Fable —');

  const memory = memoryOf(['hA', seen(reading({ claude: claude({ usage: usageOf(56, 77, 49) }) }), 1)]);
  const first = usageRows(rowsOf({ memory, hosts: ['hA'] }, NOW))[0]?.label;
  const later = usageRows(rowsOf({ memory, hosts: ['hA'] }, NOW + min(70)))[0]?.label;
  assert.notEqual(first, later, 'the reset text was made when the reading arrived, not when the list was drawn');
  assert.ok(later?.includes('resets in 1h 0m'), `the later draw says ${String(later)}`);
});

test('A4-T4: one model part for each model window that /usage named', () => {
  const usage: ClaudeUsage = {
    state: 'read',
    unmatched: 0,
    windows: [
      { name: '5h', usedPercent: 10, resetsAtMs: null }, { name: 'week', usedPercent: 20, resetsAtMs: null },
      { name: { model: 'Fable' }, usedPercent: 30, resetsAtMs: null }, { name: { model: 'Opal' }, usedPercent: 40, resetsAtMs: null },
    ],
  };
  const memory = memoryOf(['hA', seen(reading({ claude: claude({ usage }) }), 1)]);

  assert.equal(usageRows(rowsOf({ memory, hosts: ['hA'] }))[0]?.label, '5h 90% left · resets in — · week 80% left · Fable 70% left · Opal 60% left');
});

// ───────────────── A4-T5: absent is never zero ─────────────────

/** A small generator with a fixed seed, so a red is the same red tomorrow. */
function numbers(seed: number): () => number {
  let state = seed;

  return () => { state = (state * 1_103_515_245 + 12_345) % 2_147_483_648; return state / 2_147_483_648; };
}

test('A4-T5: a number that is not there is a dash and never a 0% — 200 generated readings', () => {
  const next = numbers(7);
  const pick = (): number | null => (next() < 0.5 ? null : [12, 37, 55.5, 88][Math.floor(next() * 4)] as number);
  for (let one = 0; one < 200; one += 1) {
    const claudeUsage: ClaudeUsage = { state: 'read', unmatched: 0, windows: [
      { name: '5h', usedPercent: pick(), resetsAtMs: next() < 0.5 ? null : NOW + min(30) }, { name: 'week', usedPercent: pick(), resetsAtMs: null }, { name: { model: 'Fable' }, usedPercent: pick(), resetsAtMs: null },
    ] };
    const codexUsage: CodexUsage = { state: 'read', statMissing: false, asOf: null, windows: [{ minutes: 300, usedPercent: pick(), resetsAtMs: null }, { minutes: 10_080, usedPercent: pick(), resetsAtMs: NOW + hour(30) }] };
    const memory = memoryOf(['hA', seen(reading({ claude: claude({ usage: claudeUsage }), codex: { state: 'found', signInFile: true, plan: null, usage: codexUsage } }), 3)]);
    const text = allText(rowsOf({ memory, hosts: ['hA'] }));

    assert.ok(!/(?<![0-9])0% left/u.test(text), `a 0% that was not a 0 used was drawn: ${text}`);
    assert.ok(!/—\s*(?:\d+)?% left/u.test(text), `a dash carries "% left": ${text}`);
  }
});

test('A4-T5: a real 100% used is a real 0% left', () => {
  const memory = memoryOf(['hA', seen(reading({ claude: claude({ usage: usageOf(100, 100, 100) }) }), 3)]);

  assert.equal(usageRows(rowsOf({ memory, hosts: ['hA'] }))[0]?.label.startsWith('5h 0% left'), true);
});

// ───────────────── A4-T6: the groups ─────────────────

const usageFound = (over: Partial<Extract<Claude, { state: 'found' }>> = {}): Claude => claude({ usage: usageOf(56, 77, 49), ...over });

test('A4-T6: one Claude account on two hosts is ONE group that names both', () => {
  const memory = memoryOf(['hA', seen(reading({ claude: usageFound() }), 4)], ['hB', seen(reading({ claude: usageFound() }), 4)]);
  const rows = rowsOf({ memory, hosts: ['hA', 'hB'] });

  assert.equal(separators(rows).filter((one) => one.startsWith('Claude Code')).length, 1);
  assert.ok(usageRows(rows)[0]?.detail.startsWith('hA, hB · read '), `the detail is ${String(usageRows(rows)[0]?.detail)}`);
});

test('A4-T6: two accounts with equal numbers are two groups', () => {
  const memory = memoryOf(['hA', seen(reading({ claude: usageFound({ email: 'one@example.test' }) }), 4)], ['hB', seen(reading({ claude: usageFound({ email: 'two@example.test' }) }), 4)]);

  assert.equal(separators(rowsOf({ memory, hosts: ['hA', 'hB'] })).filter((one) => one.startsWith('Claude Code')).length, 2, 'two accounts were merged');
});

test('A4-T6: two hosts whose email is empty are two groups, also when their organization is equal; the separator names the host', () => {
  const memory = memoryOf(['hA', seen(reading({ claude: usageFound({ email: '' }) }), 4)], ['hB', seen(reading({ claude: usageFound({ email: '' }) }), 4)]);
  const named = separators(rowsOf({ memory, hosts: ['hA', 'hB'] })).filter((one) => one.startsWith('Claude Code'));

  assert.equal(named.length, 2, 'two accounts that are not known were merged');
  assert.ok(named[0]?.includes('hA') && !named[0]?.includes(ORG) && named[1]?.includes('hB'), `the separators are ${named.join(' | ')}`);
});

test('A4-T6: a Codex group is one host, and a host\'s alias never groups', () => {
  const codex = (plan: string | null): Reading['codex'] => ({ state: 'found', signInFile: true, plan, usage: { state: 'read', statMissing: false, asOf: { ms: NOW - hour(48), from: 'file' }, windows: [{ minutes: 10_080, usedPercent: 14, resetsAtMs: NOW + min(3_800) }] } });
  const memory = memoryOf(['hA', seen(reading({ codex: codex('prolite') }), 4)], ['hB', seen(reading({ codex: codex('prolite') }), 4)]);
  const rows = rowsOf({ memory, hosts: ['hA', 'hB'] });

  assert.deepEqual(separators(rows).filter((one) => one.startsWith('Codex')), ['Codex · account name not shown by Codex · plan prolite · hA', 'Codex · account name not shown by Codex · plan prolite · hB']);
  assert.equal(usageRows(rows)[0]?.label, 'week 86% left · resets in 2d 15h');
  assert.equal(usageRows(rows)[0]?.detail, 'from a Codex session 2 d ago · read 4 min ago');
});

// ───────────────── A4-T7: the newest reading, and a later failure ─────────────────

test('A4-T7: the group shows the newest reading and its age', () => {
  const memory = memoryOf(['hA', seen(reading({ claude: usageFound({ usage: usageOf(10, 10, 10) }) }), 4)], ['hB', seen(reading({ claude: usageFound({ usage: usageOf(20, 20, 20) }) }), 1)]);
  const [row] = usageRows(rowsOf({ memory, hosts: ['hA', 'hB'] }));

  assert.ok(row?.label.startsWith('5h 80% left'), `the oldest reading was drawn: ${String(row?.label)}`);
  assert.ok(row?.detail.endsWith('read 1 min ago'), `the detail is ${String(row?.detail)}`);
});

test('A4-T7: a later failed read keeps the older numbers and says so', () => {
  const memory = memoryOf(
    ['hA', seen(reading({ claude: usageFound({ usage: usageOf(10, 10, 10) }) }), 4)],
    ['hB', seen(reading({ claude: usageFound({ usage: usageOf(20, 20, 20) }) }), 2, { failure: { sentence: 'did not answer', atMs: NOW } })],
  );
  const [row] = usageRows(rowsOf({ memory, hosts: ['hA', 'hB'] }));

  assert.ok(row?.label.startsWith('5h 80% left'), 'hB\'s older numbers were dropped');
  assert.ok(row?.detail.includes('the later read of hB failed'), `the detail is ${String(row?.detail)}`);
});

test('A4-T7 (F2): a later answer that could not be read keeps the older numbers and says the later read failed', () => {
  const memory = memoryOf(['hA', seen(reading({ claude: usageFound({ usage: usageOf(10, 10, 10) }) }), 2, { unreadable: { why: 'cut', atMs: NOW } })]);
  const [row] = usageRows(rowsOf({ memory, hosts: ['hA'] }));

  assert.ok(row?.label.startsWith('5h 90% left'), 'the older numbers were dropped');
  assert.ok(row?.detail.includes('the later read of hA failed'), `the detail is ${String(row?.detail)}`);
});

test('A4-T7: a newer reading that read no usage does not hide the numbers of an older one', () => {
  const memory = memoryOf(['hA', seen(reading({ claude: usageFound({ usage: usageOf(10, 10, 10) }) }), 4)], ['hB', seen(reading({ claude: usageFound({ usage: { state: 'skipped' } }) }), 1)]);
  const [row] = usageRows(rowsOf({ memory, hosts: ['hA', 'hB'] }));

  assert.ok(row?.label.startsWith('5h 90% left'), `the numbers are gone: ${String(row?.label)}`);
  assert.ok(row?.detail.startsWith('hA, hB · read 4 min ago'), `the numbers are said to be fresher than they are: ${String(row?.detail)}`);
  assert.ok(!allText(rowsOf({ memory, hosts: ['hA', 'hB'] })).includes('usage not read'), 'a skipped usage drew a sentence although another host read it');
});

// ───────────────── A4-T8: every host says how it stands ─────────────────

const HOSTS = ['hAsk', 'hAuto', 'hDamaged', 'hFail', 'hFull', 'hJq', 'hNever', 'hNo', 'hOk', 'hUnreadable', 'hWait'] as const;

function standingsWorld(over: Partial<AccountsView> = {}): readonly Row[] {
  const ok = reading({ claude: claude() });
  const memory = memoryOf(
    ['hAsk', { admission: 'not-asked' }],
    ['hAuto', seen(ok, 2)],
    ['hDamaged', { admission: 'damaged' }],
    ['hFail', seen(ok, 9, { failure: { sentence: '“hFail” did not answer over ssh within 30 s.', atMs: NOW - min(3) } })],
    ['hFull', { admission: 'full' }],
    ['hJq', seen(reading({ jqMissing: 'apt-get' }), 2)],
    ['hOk', seen(ok, 2)],
    ['hUnreadable', seen(reading(), 2, { unreadable: { why: 'cut', atMs: NOW - min(2) }, reading: undefined })],
    ['hWait', { admission: 'unwritten' }],
  );

  return rowsOf({ memory, hosts: HOSTS, standings: [auto('hAuto'), person('hFail'), person('hJq'), person('hNever'), person('hNo', 'no'), person('hOk'), person('hUnreadable'), person('hWait')], ...over });
}

test('A4-T8: every host has one row, and each standing is its own sentence', () => {
  const rows = standingsWorld();
  const said = HOSTS.map((host) => hostRow(rows, host).description);

  assert.equal(new Set(said).size, HOSTS.length, `two standings share a sentence: ${said.join(' | ')}`);
  assert.equal(hostRow(rows, 'hNo').description, 'turned off: your answer for this host is “Not this host”');
  assert.equal(hostRow(rows, 'hAsk').description, 'not asked yet — choose this row to be asked');
  assert.equal(hostRow(rows, 'hNever').description, 'never read yet');
  assert.equal(hostRow(rows, 'hFull').description, 'the list of hosts is full, so this host is not read');
  assert.equal(hostRow(rows, 'hFail').description, 'the last read failed 3 min ago: “hFail” did not answer over ssh within 30 s.');
  assert.equal(hostRow(rows, 'hUnreadable').description, `The AI accounts on “hUnreadable” could not be read: ${ACCOUNTS_UNREADABLE_SAID.cut}.`);
  assert.equal(hostRow(rows, 'hOk').description, 'read 2 min ago');
});

test('A4-T8: an automatic host says when it was allowed, also with a reading', () => {
  const row = hostRow(standingsWorld(), 'hAuto');

  assert.ok(row.description.includes(`allowed automatically on ${dayOf('2026-11-21T09:00:00.000Z')}. Choose this row to change that.`), row.description);
  assert.ok(row.description.includes('read 2 min ago'), 'the reading is no longer said');
});

test('A4-T8: the damaged, full and not-asked rows come from the memory of the door, and the damaged one is S1\'s W11 word for word', () => {
  const rows = standingsWorld();

  assert.equal(hostRow(rows, 'hDamaged').description, HOSTS_SAID.rowDamagedList, 'a host that is not read over a damaged list says never read yet');
  assert.equal(hostRow(standingsWorld({ damagedFile: true }), 'hDamaged').description, HOSTS_SAID.rowDamagedFile);
  assert.equal(hostRow(rows, 'hWait').description, HOSTS_SAID.rowWaiting);
});

test('A4-T8 (F1): "turned off" comes from a "no" among the standings, not from a remembered refusal, so a Read given after it shows at once', () => {
  const memory = memoryOf(['hX', { admission: 'refused' }]);

  assert.equal(hostRow(rowsOf({ memory, hosts: ['hX'], standings: [person('hX')] }), 'hX').description, 'never read yet', 'a host that was given a Read still says it is turned off');
  assert.equal(hostRow(rowsOf({ memory, hosts: ['hX'], standings: [person('hX', 'no')] }), 'hX').description, 'turned off: your answer for this host is “Not this host”');
});

test('A4-T8: with no standing and the setting off a host that was never admitted is not asked yet; with it on it is never read yet', () => {
  assert.equal(hostRow(rowsOf({ hosts: ['hA'], allowNew: false }), 'hA').description, 'not asked yet — choose this row to be asked');
  assert.equal(hostRow(rowsOf({ hosts: ['hA'], allowNew: true }), 'hA').description, 'never read yet');
});

test('A4-T8: what choosing a host row means is carried by the row', () => {
  const rows = standingsWorld();

  assert.equal(hostRow(rows, 'hAsk').press, 'ask');
  assert.equal(hostRow(rows, 'hWait').press, 'save');
  for (const host of ['hAuto', 'hDamaged', 'hFail', 'hFull', 'hJq', 'hNever', 'hNo', 'hOk', 'hUnreadable']) {
    assert.equal(hostRow(rows, host).press, 'change', host);
  }
});

test('A4-T8: the host rows follow the separator "── Hosts ──", after the groups', () => {
  const memory = memoryOf(['hA', seen(reading({ claude: usageFound() }), 2)]);
  const rows = rowsOf({ memory, hosts: ['hA'], standings: [person('hA')] });
  const at = rows.findIndex((row) => row.kind === 'separator' && row.label === '── Hosts ──');

  assert.ok(at > 0, 'no Hosts separator');
  assert.deepEqual(rows.slice(at + 1).map((row) => row.kind), ['host']);
  assert.ok(rows.slice(0, at).some((row) => row.kind === 'usage'));
});

// ───────────────── A4-T9, A4-T10: the fixed command ─────────────────

test('A4-T9: INSTALL_JQ is exactly the plan\'s table', () => {
  assert.deepEqual({ ...INSTALL_JQ }, {
    'apt-get': 'sudo apt-get install -y jq', dnf: 'sudo dnf install -y jq', yum: 'sudo yum install -y jq', apk: 'apk add jq', none: undefined,
  });
  assert.deepEqual(Object.keys(INSTALL_JQ).sort(), [...MANAGER_WORDS].sort(), 'a manager word has no row in the table');
});

test('A4-T9: for each manager word the host row shows exactly the table\'s command and its copy is that string', () => {
  for (const word of MANAGER_WORDS) {
    const memory = memoryOf(['hA', seen(reading({ jqMissing: word }), 1)]);
    const row = hostRow(rowsOf({ memory, hosts: ['hA'], standings: [person('hA')] }), 'hA');
    const command = INSTALL_JQ[word];

    if (command === undefined) {
      assert.equal(row.copy, undefined, 'a host without a package manager offers a command');
      assert.equal(row.description, NO_MANAGER_SAID);
      assert.equal(NO_MANAGER_SAID, 'Install `jq` on this host. CoAI found none of apt-get, dnf, yum and apk there.');
    } else {
      assert.equal(row.copy, command, `${word}: the copy is not the table's string`);
      assert.ok(row.description.startsWith(`jq is missing on this host. Run there: ${command}`), row.description);
    }
  }
  const apk = memoryOf(['hA', seen(reading({ jqMissing: 'apk' }), 1)]);
  assert.ok(hostRow(rowsOf({ memory: apk, hosts: ['hA'] }), 'hA').description.includes('to be run as root'), 'the apk row does not say it is to be run as root');
});

const MARKERS = ['; rm -rf ~', '$(id)', 'line one\nline two', 'apt-get remove', '`id`'];

test('A4-T10: no text from a reply is part of a shown command — 50 generated readings with a marker in every text field', () => {
  const next = numbers(11);
  for (let one = 0; one < 50; one += 1) {
    const marker = MARKERS[Math.floor(next() * MARKERS.length)] as string;
    const word = MANAGER_WORDS[Math.floor(next() * MANAGER_WORDS.length)] as (typeof MANAGER_WORDS)[number];
    const found = claude({ email: `a${marker}@example.test`, org: `o${marker}`, version: `1${marker}`, plan: `p${marker}`, usage: { state: 'read', unmatched: 0, windows: [{ name: { model: `m${marker}` }, usedPercent: 5, resetsAtMs: null }] } });
    const memory = memoryOf(['hA', seen(reading({ claude: found, jqMissing: word, codex: { state: 'found', signInFile: true, plan: `x${marker}`, usage: { state: 'failed', why: 'none' } } }), 1)]);
    const rows = rowsOf({ memory, hosts: ['hA'], standings: [person('hA')] });
    const row = hostRow(rows, 'hA');
    const allowed = Object.values(INSTALL_JQ).filter((value): value is string => value !== undefined);

    for (const each of rows) {
      if (each.kind === 'host' && each.copy !== undefined) {
        assert.ok(allowed.includes(each.copy), 'a copy that is not a value of INSTALL_JQ');
      }
    }
    const command = INSTALL_JQ[word];
    const said = command === undefined ? NO_MANAGER_SAID : `jq is missing on this host. Run there: ${command}`;
    assert.ok(row.description.startsWith(said), `text from the VM reached the command: ${row.description}`);
    assert.ok(!(row.copy ?? '').includes(marker) && !said.includes(marker), 'text from the VM reached the command');
  }
});

// ───────────────── A4-T11: usage not read says why ─────────────────

const textFor = (side: Partial<Reading>): string => allText(rowsOf({ memory: memoryOf(['hA', seen(reading(side), 1)]), hosts: ['hA'], standings: [person('hA')] }));
const found = (usage: ClaudeUsage, over: Partial<Extract<Claude, { state: 'found' }>> = {}): Partial<Reading> => ({ claude: claude({ usage, ...over }) });

test('A4-T11: each way Claude usage was not read is its own sentence, with the version', () => {
  const cases: readonly (readonly [ClaudeUsage, string])[] = [
    [{ state: 'failed', why: 'format' }, 'usage not read: Claude Code 2.1.5 on this host printed no usage line this version understands'],
    [{ state: 'failed', why: 'model-call' }, 'usage not read: the usage command of Claude Code 2.1.5 asked the model, so CoAI stopped asking this host'],
    [{ state: 'failed', why: 'timeout' }, `usage not read: Claude Code did not answer within ${ACCOUNTS_SECONDS.usage} s`],
    [{ state: 'failed', why: 'error' }, 'usage not read: the usage command of Claude Code 2.1.5 ended with an error'],
  ];
  const all = new Set<string>();
  for (const [usage, said] of cases) {
    const text = textFor(found(usage));
    assert.ok(text.includes(said), `${said}\n---\n${text}`);
    all.add(said);
  }
  assert.equal(all.size, cases.length, 'one sentence for format and model-call');
  assert.ok(textFor(found({ state: 'failed', why: 'format' }, { version: null })).includes('Claude Code (version not known) on this host'), 'a version that is not known');
});

test('A4-T11: the account\'s own failures, signed out, and the credential net — each vendor, its own sentence', () => {
  const sentences: readonly (readonly [Partial<Reading>, string])[] = [
    [{ claude: { state: 'signed-out' } }, 'signed out'],
    [{ claude: { state: 'unreadable', why: 'auth' } }, 'the account command of Claude Code printed nothing this version understands'],
    [{ claude: { state: 'unreadable', why: 'timeout' } }, `the account command of Claude Code did not answer within ${ACCOUNTS_SECONDS.account} s`],
    [{ claude: { state: 'unreadable', why: 'credential-shape' } }, 'not shown: a value in the answer looked like a credential'],
    [{ codex: { state: 'unreadable', why: 'credential-shape' } }, 'not shown: a value in the answer looked like a credential'],
    [{ agy: { state: 'unreadable', why: 'credential-shape' } }, 'not shown: a value in the answer looked like a credential'],
    [{ codex: { state: 'found', signInFile: true, plan: null, usage: { state: 'failed', why: 'no-sessions' } } }, 'no Codex session file is on this host'],
    [{ codex: { state: 'found', signInFile: true, plan: null, usage: { state: 'failed', why: 'none' } } }, 'no usage record is near the end of the newest Codex sessions'],
    [{ codex: { state: 'found', signInFile: true, plan: null, usage: { state: 'failed', why: 'error' } } }, 'the Codex session files could not be read'],
    [{ agy: { state: 'found', version: '1.5.12', signInFile: true, usage: { state: 'no-source' } } }, 'agy states no usage, and its account is not read'],
  ];
  for (const [side, said] of sentences) {
    assert.ok(textFor(side).includes(said), `${said}\n---\n${textFor(side)}`);
  }
  const auth = textFor({ claude: { state: 'unreadable', why: 'auth' } });
  assert.ok(!auth.includes('signed out'), 'two failures share a sentence');
});

test('A4-T11: agy is drawn with its version, or says it is not known', () => {
  const agy = (version: string | null): Partial<Reading> => ({ agy: { state: 'found', version, signInFile: true, usage: { state: 'no-source' } } });

  assert.ok(separators(rowsOf({ memory: memoryOf(['hA', seen(reading(agy('1.5.12')), 1)]), hosts: ['hA'] })).includes('Antigravity 1.5.12 · hA'));
  assert.ok(separators(rowsOf({ memory: memoryOf(['hA', seen(reading(agy(null)), 1)]), hosts: ['hA'] })).includes('Antigravity (version not known) · hA'));
});

test('A4-T11: a whole answer that is unreadable has one sentence with the clause of ACCOUNTS_UNREADABLE_SAID, for every fault', () => {
  const clauses = new Set<string>();
  for (const [why, clause] of Object.entries(ACCOUNTS_UNREADABLE_SAID)) {
    const memory = memoryOf(['hA', { unreadable: { why: why as keyof typeof ACCOUNTS_UNREADABLE_SAID, atMs: NOW - min(1) } }]);
    const row = hostRow(rowsOf({ memory, hosts: ['hA'], standings: [person('hA')] }), 'hA');

    assert.equal(row.description, `The AI accounts on “hA” could not be read: ${clause}.`);
    clauses.add(row.description);
  }
  assert.equal(clauses.size, Object.keys(ACCOUNTS_UNREADABLE_SAID).length);
});

test('A4-T11: Codex with no time says a dash for its age, and a host without stat says so', () => {
  const codex = (asOf: { ms: number; from: 'record' | 'file' } | null, statMissing: boolean): Partial<Reading> =>
    ({ codex: { state: 'found', signInFile: true, plan: null, usage: { state: 'read', statMissing, asOf, windows: [{ minutes: 300, usedPercent: 20, resetsAtMs: NOW + min(60) }] } } });
  const none = usageRows(rowsOf({ memory: memoryOf(['hA', seen(reading(codex(null, false)), 1)]), hosts: ['hA'] }))[0];
  const noStat = usageRows(rowsOf({ memory: memoryOf(['hA', seen(reading(codex(null, true)), 1)]), hosts: ['hA'] }))[0];

  assert.equal(none?.detail, 'from a Codex session — · read 1 min ago');
  assert.ok(noStat?.detail.includes('this host has no stat, so the time of the Codex session file was not read'), noStat?.detail);
  assert.equal(none?.label, '5h 80% left · resets in 1h 0m');
});

test('A4-T11: a Codex window whose reset has passed says there is no reading since it was reset', () => {
  const memory = memoryOf(['hA', seen(reading({ codex: { state: 'found', signInFile: true, plan: null, usage: { state: 'read', statMissing: false, asOf: null, windows: [{ minutes: 300, usedPercent: 20, resetsAtMs: NOW - min(5) }] } } }), 1)]);

  assert.ok(usageRows(rowsOf({ memory, hosts: ['hA'] }))[0]?.label.includes('no reading since this window was reset'));
});

test('A4-T11: a group whose usage nobody read says why — never a row of dashes alone', () => {
  const memory = memoryOf(['hA', seen(reading({ claude: claude() }), 1)]);
  const [row] = usageRows(rowsOf({ memory, hosts: ['hA'] }));

  assert.equal(row?.label, USAGE_OFF_SAID);
  assert.ok(/usage not read/u.test(USAGE_OFF_SAID) && !/^[\s—·%\d]*$/u.test(USAGE_OFF_SAID), 'the sentence of a group nobody read is not words');
  for (const each of usageRows(rowsOf({ memory: memoryOf(['hA', seen(reading({ codex: { state: 'unreadable', why: 'credential-shape' } }), 1)]), hosts: ['hA'] }))) {
    assert.ok(/[a-z]{6}/u.test(each.label), `a row of dashes with no reason: ${each.label}`);
  }
});

// ───────────────── A4-T12: VM text is made plain at one road ─────────────────

test('A4-T12 (F7): a 200-character email is shown whole in the separator', () => {
  const email = `${'a'.repeat(187)}@example.test`;
  const memory = memoryOf(['hA', seen(reading({ claude: usageFound({ email }) }), 1)]);

  assert.equal(email.length, 200);
  assert.ok(separators(rowsOf({ memory, hosts: ['hA'] })).some((one) => one.includes(email)), 'a long email was cut');
});

test('A4-T12: forList takes out the bidi override, the zero-width space and the bell, breaks $( and cuts at max with an ellipsis', () => {
  const bidi = String.fromCodePoint(0x202e);
  const zero = String.fromCodePoint(0x200b);
  const bell = String.fromCodePoint(7);
  const text = `a${bidi}b${zero}c${bell}d $(zap) ${'x'.repeat(10_000)}`;
  const out = forList(text, 40);

  assert.ok(![bidi, zero, bell].some((one) => out.includes(one)), 'a control or format character survived');
  assert.ok(out.endsWith('…') && [...out].length <= 40, `the cut: ${out}`);
  assert.equal(forList('$(zap)', 40), '$ (zap)');
  assert.equal(forList(forList(text, 40), 40), out, 'doing it twice changed it');
  assert.equal(forList('short', 40), 'short', 'a short text was changed');
});

test('A4-T12: an email that holds $( reaches a separator as $ (, and a failure sentence that ends with $(zap) and a bidi override too', () => {
  const bidi = String.fromCodePoint(0x202e);
  const memory = memoryOf(
    ['hA', seen(reading({ claude: usageFound({ email: 'x$(zap)@example.test' }) }), 1)],
    ['hB', seen(reading(), 1, { failure: { sentence: `ssh said ${bidi}$(zap)`, atMs: NOW - min(1) } })],
  );
  const rows = rowsOf({ memory, hosts: ['hA', 'hB'], standings: [person('hA'), person('hB')] });
  const text = allText(rows);

  assert.ok(separators(rows).some((one) => one.includes('x$ (zap)@example.test')), 'an icon a VM chose was drawn');
  assert.ok(!text.includes('$(') && !text.includes(bidi), 'an icon from ssh\'s stderr was drawn');
  assert.ok(hostRow(rows, 'hB').description.endsWith('$ (zap)'), hostRow(rows, 'hB').description);
});

/**
 * The unit twin of the host scenario A4-H1: the SAME reply the scenario's fake call answers (a Claude account, the usage skipped, no other vendor),
 * through the real parser and into the rows. The scenario cannot run here; this is what it stands on. Its reply holds a "$(" in the organization,
 * which reaches the separator written "$ (" — and one in an email, which the parser does not keep: it judges an email after `plainValue`, and the
 * space that `plainValue` writes makes it "not known", so the group is named by its host. That is the S2 value table, not a defect of the list.
 */
const twin = (email: string, org: string): readonly Row[] => {
  const read = readAccounts(ok(frame('COAI-JQ found', 'COAI-VENDOR claude found', `:email ${email}`, `:org ${org}`, ':version 2.1.326', 'COAI-USAGE claude skipped', 'COAI-VENDOR codex absent', 'COAI-VENDOR agy absent')), 'no-claude-usage', NOW);
  assert.equal(read.kind, 'read', 'the reply of the host scenario is not a reading');

  return rowsOf({ memory: memoryOf(['hA', seen(read as Reading, 1)]), hosts: ['hA'], standings: [auto('hA')] });
};

test('A4-H1 twin: a parsed reading with an account and no usage numbers draws its group, then the Hosts separator and one row for the host', () => {
  const rows = twin('someone@example.test', 'Example$(zap) Org');

  assert.deepEqual(separators(rows), ['Claude Code · someone@example.test · Example$ (zap) Org', '── Hosts ──']);
  assert.equal(usageRows(rows).length, 1, 'the group has no row');
  assert.deepEqual(rows.filter((row) => row.kind === 'host').map((row) => row.label), [hostRow(rows, 'hA').label]);
});

test('A4-H1 twin: an email that holds $( is not an account the parser keeps, and its group is named by its host', () => {
  const rows = twin('x$(zap)@example.test', 'Example Org');

  assert.deepEqual(separators(rows), ['Claude Code · hA', '── Hosts ──']);
  assert.ok(!allText(rows).includes('$('), 'an icon a VM chose was drawn');
});

/** The spans of the argument lists of every `forList(` in a text, as [start, end) indices. */
function forListSpans(text: string): readonly (readonly [number, number])[] {
  return [...text.matchAll(/\bforList\(/gu)].map((call): readonly [number, number] => {
    let depth = 0;
    for (let at = (call.index ?? 0) + call[0].length - 1; at < text.length; at += 1) {
      depth += text[at] === '(' ? 1 : text[at] === ')' ? -1 : 0;
      if (depth === 0) {
        return [(call.index ?? 0), at];
      }
    }

    return [(call.index ?? 0), text.length];
  });
}

/** Every access of a text field of a reply or of the runner's sentence that is not inside a forList( call. */
const FIELDS = 'email|org|plan|version|model|sentence';
const FIELD = new RegExp(`\\??\\.(?:${FIELDS})\\b|\\{[^{}]*\\b(?:${FIELDS})\\b[^{}]*\\}\\s*=(?!=)`, 'gu');
function barefields(text: string): readonly string[] {
  const spans = forListSpans(text);

  return [...text.matchAll(FIELD)].filter((one) => !spans.some(([from, to]) => (one.index ?? 0) > from && (one.index ?? 0) < to)).map((one) => text.slice(Math.max(0, (one.index ?? 0) - 20), (one.index ?? 0) + 20));
}

test('A4-T12 (scan): every text field of a reply and the failure sentence reach a row only inside forList(, and forList calls plainValue( with no character class of its own', () => {
  const source = sourceOf('remoteAccountsRows.ts');

  assert.deepEqual(barefields(source), [], 'a text field of a reply reaches a row outside forList(');
  const body = /export function forList\([\s\S]*?\n\}\n/u.exec(source)?.[0] ?? '';
  assert.ok(body.includes('plainValue('), 'forList does not call plainValue(');
  assert.ok(!/\\p\{|\\u[0-9a-f]{4}|\\x[0-9a-f]{2}|\[\^?[\\\w-]+\]/iu.test(body), 'forList holds a character class of its own');
});

test('A4-T12 (scan companions): the scan finds a bare field, a field inside forList(, and the sentence only inside forList(', () => {
  assert.equal(barefields('const a = found.email;').length, 1, 'the scan misses a bare field');
  assert.equal(barefields('const a = forList(found.email, 9);').length, 0, 'the scan flags a field inside forList(');
  assert.equal(barefields('const { email } = found;').length, 1, 'the scan misses a destructured field');
  assert.equal(barefields('const { email: shown, org } = found.read;').length, 1, 'the scan misses a renamed destructured field');
  assert.equal(barefields('const { host, found } = one;').length, 0, 'the scan flags a destructure of other names');
  assert.equal(barefields('const a = forList(memory.failure?.sentence, 9) + b.sentence;').length, 1, 'the scan misses a bare sentence beside a wrapped one');
  const source = sourceOf('remoteAccountsRows.ts');
  assert.ok(forListSpans(source).length >= 6, 'the scan found no forList( call in the module, so it looked at nothing');
  assert.ok(/forList\([^)]*\.sentence/u.test(source), 'the failure sentence does not pass forList(');
  assert.ok(/forList\([^)]*\.email/u.test(source), 'the email does not pass forList(');
});

// ───────────────── A4-T13: the notices ─────────────────

const state = (over: Partial<AccountsState> = {}): AccountsState => over;
const iso = (ms: number): string => new Date(ms).toISOString();

test('A4-T13: the first automatic host is told at once; two hours later nobody is; after 25 hours only the hosts allowed since', () => {
  const standings = [auto('hA', iso(NOW - min(5)))];
  const first = noticesDue(state(), standings, true, NOW);
  assert.deepEqual(first.autoHosts, ['hA']);

  const told = state({ lastAutoNoticeUtc: iso(NOW) });
  assert.deepEqual(noticesDue(told, standings, true, NOW + hour(2)).autoHosts, [], 'the person was told 6 times an hour');
  const more = [...standings, auto('hB', iso(NOW + hour(1)))];
  assert.deepEqual(noticesDue(told, more, true, NOW + hour(2)).autoHosts, [], 'a new host inside the day was told at once');
  assert.deepEqual(noticesDue(told, more, true, NOW + hour(25)).autoHosts, ['hB'], 'a host that was already told about was told again');
});

test('A4-T13: no host allowed by itself means no notice, and a person\'s own "yes" is not an automatic one', () => {
  assert.deepEqual(noticesDue(state(), [], true, NOW).autoHosts, []);
  assert.deepEqual(noticesDue(state(), [person('hA'), person('hB', 'no')], true, NOW).autoHosts, []);
});

test('A4-T13 (F3): the first-round notice names only the "yes" rows older than the round that just ended', () => {
  const start = NOW;
  const old = { host: 'hOld', answer: 'yes', how: 'person', askedUtc: iso(start - hour(5)) } as const;
  const during = { host: 'hNew', answer: 'yes', how: 'person', askedUtc: iso(start + min(2)) } as const;

  assert.deepEqual(noticesDue(state(), [old, during], true, start + min(10), start).first, ['hOld'], 'a "yes" given during the round was told as one from before');
});

test('A4-T13: the first-round notice names the person\'s "yes" rows once, while it was never told', () => {
  const standings = [person('hA'), person('hB'), person('hC', 'no'), auto('hD')];

  assert.deepEqual(noticesDue(state(), standings, true, NOW).first, ['hA', 'hB']);
  assert.deepEqual(noticesDue(state(), standings, false, NOW).first, [], 'told before the first round ended');
  assert.deepEqual(noticesDue(state({ toldUtc: iso(NOW - hour(1)) }), standings, true, NOW).first, [], 'told again after a told');
});

// ───────────────── A4-T14: the stopped list and the empty list ─────────────────

test('A4-T14: while stopped the list is exactly the start row', () => {
  const memory = memoryOf(['hA', seen(reading({ claude: usageFound() }), 1)]);
  const rows = rowsOf({ stopped: true, memory, hosts: ['hA'], standings: [person('hA')] });

  assert.deepEqual(rows, [{ kind: 'info', label: 'The accounts round is stopped. Choose this row to start it.', action: 'start' }], 'the groups were drawn while stopped');
});

test('A4-T14: the stopped row has the two other wordings of W13', () => {
  assert.equal(rowsOf({ stopped: true, damagedFile: true })[0]?.label, HOSTS_SAID.startDamaged);
  assert.equal(rowsOf({ stopped: true, startUnwritten: true })[0]?.label, HOSTS_SAID.startUnwritten);
  assert.equal(rowsOf({ stopped: true })[0]?.label, HOSTS_SAID.stoppedElsewhere);
  assert.equal((rowsOf({ stopped: true, damagedFile: true })[0] as { action?: string }).action, undefined, 'a row that cannot start offers to start');
});

test('A4-T14: with no Creds terminal the list is exactly one sentence', () => {
  const rows = rowsOf({ live: false });

  assert.deepEqual(rows, [{ kind: 'info', label: 'No `Creds: <host> / <session>` terminal is open, so there is no host to read.' }]);
});

test('A4-T14: a workspace or a folder that sets the key is said in the first row', () => {
  assert.equal(rowsOf({ ignored: 'workspace', hosts: ['hA'] })[0]?.label, HOSTS_SAID.ignoredWorkspace);
  assert.equal(rowsOf({ ignored: 'folder', hosts: ['hA'] })[0]?.label, HOSTS_SAID.ignoredFolder);
  assert.deepEqual(labelsOf(rowsOf({ ignored: 'workspace', stopped: true })), [HOSTS_SAID.ignoredWorkspace, HOSTS_SAID.stoppedElsewhere]);
  assert.deepEqual(labelsOf(rowsOf({ ignored: 'folder', live: false })).length, 2);
  assert.ok(rowsOf({ ignored: undefined, hosts: ['hA'] }).every((row) => row.label !== HOSTS_SAID.ignoredWorkspace));
});

test('A4-T14: a press of Refresh now inside a minute adds one row that says so', () => {
  const rows = rowsOf({ hosts: ['hA'], floor: true });

  assert.equal(rows[0]?.label, 'A round ran less than a minute ago.');
  assert.ok(rowsOf({ hosts: ['hA'] }).every((row) => row.label !== 'A round ran less than a minute ago.'));
});
