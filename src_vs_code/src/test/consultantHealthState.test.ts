import assert from 'node:assert/strict';
import { test } from 'node:test';

import { type CheckRecord, type ConsultantRow, type ConsultantsReport, type HealthFile, parseCheckDocument, parseConsultantsAnswer } from '../consultantHealth';
import type { SideFiles } from '../consultantHealthRead';
import {
  type ConsultantHealthState,
  type OtherSideHealth,
  type ProbeShown,
  type ThisSideHealth,
  reportedAbout,
  reportedSnippet,
  rowHealth,
} from '../consultantHealthState';

/**
 * What each Consultant row's health block SHOWS, decided — before any of it is markup (E5.2).
 *
 * <p>The split `consultantView.ts` already lives by: which notices a row carries, whether its last failure is still
 * about the consultant the row now names, which check result is the newest truth, whether the Check button exists
 * and whether it is disabled — all of it is a VALUE these tests assert. `.agents/PROJECT.md` refuses a new behavioural
 * assertion over page source text, and the honest alternative is a decision that was never markup in the first place.
 * The page's own behaviour (the button posts, it is disabled, it is absent on the other side) is RUN in
 * `consultantHealthPage.test.ts`.</p>
 */

const NOW = Date.parse('2026-10-03T10:01:00.0000000Z');

function check(over: Record<string, unknown> = {}): CheckRecord {
  const parsed = parseCheckDocument(JSON.stringify({
    callerKind: 'claude', state: 'answered', startedUtc: '2026-10-03T09:00:00.0000000Z',
    deadlineUtc: '2026-10-03T09:07:00.0000000Z', heartbeatUtc: '2026-10-03T09:00:00.0000000Z',
    finishedUtc: '2026-10-03T09:01:00.0000000Z', heartbeatStaleAfterSeconds: 60, side: 'windows', vendor: 'antigravity',
    runtime: 'antigravity', model: 'gemini-3.1-pro-high', answered: true, markerRead: true, canary: 'denied-by-cli-unattributed',
    seconds: 42, ...over,
  }));
  assert.ok(parsed !== undefined, 'the fixture check does not parse');

  return parsed;
}

function report(rows: readonly Record<string, unknown>[], over: Record<string, unknown> = {}): ConsultantsReport {
  const answer = parseConsultantsAnswer(0, JSON.stringify({ utc: '2026-10-03T10:00:00.0000000Z', side: 'windows', consultants: rows, ...over }));
  assert.ok(answer.kind === 'answered', 'the fixture report does not parse');

  return answer.report;
}

function row(over: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    callerKind: 'claude', available: true, vendor: 'antigravity', runtime: 'antigravity', model: 'gemini-3.1-pro-high',
    cli: { probed: true, found: true, version: '1.2.15', authSource: 'own auth', note: '' },
    limitation: { standing: 'default-deny', text: 'agy denies a shell command headless', evidence: 'source', source: 'documented upstream' },
    agy: { settingsPath: 'C:\\Users\\u\\.gemini\\antigravity-cli\\settings.json', snippet: '', snippetWarning: '' },
    lastFailure: {
      utc: '2026-10-03T09:30:00.0000000Z', kind: 'command-denied', vendor: 'antigravity', model: 'gemini-3.1-pro-high',
      what: 'agy denied a shell command', cure: 'ask again; the follow-up answers', evidence: 'C:\\coai\\unparseable\\c.txt',
    },
    failureCurrent: true,
    ...over,
  };
}

function answered(rows: readonly Record<string, unknown>[] = [row()], over: Record<string, unknown> = {}): ProbeShown {
  return { kind: 'answered', report: report(rows, over) };
}

function thisSide(over: Partial<ThisSideHealth> = {}): ThisSideHealth {
  return { label: 'Windows', probe: answered(), files: { report: undefined, checks: {} }, checking: [], runs: {}, ...over };
}

function state(over: Partial<ThisSideHealth> = {}, otherSides: readonly OtherSideHealth[] = []): ConsultantHealthState {
  return { thisSide: thisSide(over), otherSides, nowMs: NOW };
}

const ROW = { vendor: 'antigravity', model: 'gemini-3.1-pro-high' };

function files(checks: Readonly<Record<string, CheckRecord>>, rows?: readonly Record<string, unknown>[], over: Record<string, unknown> = {}): SideFiles {
  return {
    report: rows === undefined ? undefined : { kind: 'found', value: report(rows, over) },
    checks: Object.fromEntries(Object.entries(checks).map(([kind, one]): [string, HealthFile<CheckRecord>] => [kind, { kind: 'found', value: one }])),
  };
}

test('the last failure is shown with its cure while the row still names the consultant that failed', () => {
  const failure = rowHealth('claude', ROW, state()).thisSide.failure;

  assert.equal(failure?.label, 'Shell command denied');
  assert.equal(failure?.cure, 'ask again; the follow-up answers');
  assert.equal(failure?.utc, '2026-10-03T09:30:00.0000000Z');
});

test('facts about ANOTHER consultant than this row names are not shown as its failure — the row has moved on since the server was asked', () => {
  assert.equal(rowHealth('claude', { vendor: 'codex', model: '' }, state()).thisSide.failure, undefined);
  assert.equal(rowHealth('claude', { ...ROW, model: 'gemini-3.1-flash' }, state()).thisSide.failure, undefined);
});

test("whether a failure is the row's current one is the SERVER's verdict — the panel does not compare the failure's own vendor a second time (A3)", () => {
  // The server decides per consultant (ConsultHealth.Current) and says so in failureCurrent; a TypeScript copy of the
  // rule was a second source of truth, and this row is one it would have hidden.
  const failure = { utc: '2026-10-03T09:30:00.0000000Z', kind: 'quota', vendor: 'antigravity', model: '', what: 'quota spent', cure: 'wait', evidence: '' };
  const block = rowHealth('claude', ROW, state({ probe: answered([row({ lastFailure: failure, failureCurrent: true })]) })).thisSide;

  assert.equal(block.failure?.label, 'Quota spent', 'the server said current, and the panel overruled it with a rule of its own');
});

test('a health file the server could not read is said on the row — never shown as a consultant that is fine (A4)', () => {
  const unreadable = 'C:/coai/consultations/health/claude.failure.json does not parse';
  const block = rowHealth('claude', ROW, state({ probe: answered([row({ lastFailure: null, failureCurrent: false, healthUnreadable: unreadable })]) })).thisSide;

  assert.match(block.notices.join(' '), /cannot be read/u);
  assert.match(block.notices.join(' '), /claude\.failure\.json does not parse/u);
});

test("another side's unreadable health file is said in its block too (A4)", () => {
  const block = rowHealth('claude', ROW, state({}, [other(files({}, [row({ healthUnreadable: 'its failure file does not parse' })], { side: 'wsl' }))])).otherSides[0];

  assert.match(block?.notices.join(' ') ?? '', /its failure file does not parse/u);
});

test('a failure a later answer superseded is not shown — the server\'s own rule, as it answered it', () => {
  const recovered = state({ probe: answered([row({ failureCurrent: false })]) });

  assert.equal(rowHealth('claude', ROW, recovered).thisSide.failure, undefined);
});

test('a server too old for the mode says "update the MCP server", offers no Check, and is never an empty block', () => {
  const block = rowHealth('claude', ROW, state({ probe: { kind: 'too-old' } })).thisSide;

  assert.match(block.notices.join(' '), /update the MCP server/u);
  assert.equal(block.checkButton, undefined);
});

test('an available row has one Check, by its caller kind, enabled while nothing runs', () => {
  assert.deepEqual(rowHealth('claude', ROW, state()).thisSide.checkButton, { id: 'claude', disabled: false, label: 'Check' });
});

test('while this window runs a check the button is disabled and says Checking', () => {
  const button = rowHealth('claude', ROW, state({ checking: ['claude'] })).thisSide.checkButton;

  assert.deepEqual(button, { id: 'claude', disabled: true, label: 'Checking…' });
});

test('after a reload — no local flag at all — the server\'s settled checking still disables the button', () => {
  const running = check({ state: 'checking', finishedUtc: '', heartbeatUtc: '2026-10-03T10:00:45.0000000Z', startedUtc: '2026-10-03T10:00:00.0000000Z' });
  const block = rowHealth('claude', ROW, state({ probe: answered([row({ check: running })]) })).thisSide;

  assert.equal(block.checkButton?.disabled, true, 'durable status: the persisted state, not a flag that died with the page');
  assert.equal(block.check.state, 'checking');
});

test('a row the server would refuse names the refusal and offers no paid Check', () => {
  const block = rowHealth('claude', ROW, state({ probe: answered([row({ available: false, reason: 'consult is switched off' })]) })).thisSide;

  assert.match(block.notices.join(' '), /consult is switched off/u);
  assert.equal(block.checkButton, undefined);
});

test('the CLI is reported as its auth source — never as a sign-in', () => {
  const cli = rowHealth('claude', ROW, state()).thisSide.cli;

  assert.match(cli, /1\.2\.15/u);
  assert.match(cli, /own auth/u);
  assert.match(cli, /not a sign-in/u);
});

test('the limitation says how it is backed — measured, or sourced and not measured here', () => {
  const limitation = rowHealth('claude', ROW, state()).thisSide.limitation;

  assert.equal(limitation?.standing, 'default-deny');
  assert.match(limitation?.backing ?? '', /not measured here/u);
});

test('a final state on disk newer than the server\'s last answer wins — the check finished after it was asked', () => {
  const asked = check({ state: 'checking', finishedUtc: '', heartbeatUtc: '2026-10-03T10:00:15.0000000Z', startedUtc: '2026-10-03T10:00:00.0000000Z' });
  const done = check({ state: 'failed', startedUtc: '2026-10-03T10:00:00.0000000Z', finishedUtc: '2026-10-03T10:00:40.0000000Z', failureKind: 'quota', failureWhat: 'quota reached' });
  const block = rowHealth('claude', ROW, state({ probe: answered([row({ check: asked })]), files: files({ claude: done }) })).thisSide;

  assert.equal(block.check.state, 'failed');
  assert.equal(block.checkButton?.disabled, false);
  assert.match(block.check.lines.join(' '), /Quota spent/u);
});

test('a checking on disk the server already settled as abandoned stays abandoned — the server asked the lock', () => {
  const stamps = { startedUtc: '2026-10-03T10:00:00.0000000Z', heartbeatUtc: '2026-10-03T10:00:15.0000000Z', finishedUtc: '' };
  const settled = check({ ...stamps, state: 'abandoned', reason: 'its lock is free' });
  const raw = check({ ...stamps, state: 'checking' });
  const block = rowHealth('claude', ROW, state({ probe: answered([row({ check: settled })]), files: files({ claude: raw }) })).thisSide;

  assert.equal(block.check.state, 'abandoned');
});

test('a Check the binary did not know names the cure — update the MCP server', () => {
  const block = rowHealth('claude', ROW, state({ runs: { claude: { result: { kind: 'too-old' }, landedUtc: '2026-10-03T10:00:50.000Z' } } })).thisSide;

  assert.match(block.check.lines.join(' '), /update the MCP server/u);
});

test('an answered check says whether the marker was read and words the canary', () => {
  const block = rowHealth('claude', ROW, state({ probe: answered([row({ check: check() })]) })).thisSide;
  const said = block.check.lines.join(' ');

  assert.match(said, /CHECK\.md/u);
  assert.match(said, /not proof that reads are confined/u);
});

test('the allow-rule snippet appears exactly where the server sent one, with its warning', () => {
  const linux = answered([row({ agy: { settingsPath: '/home/u/.gemini/antigravity-cli/settings.json', snippet: 'command(git grep)', snippetWarning: 'a prefix rule is not read-only' } })], { side: 'wsl' });

  assert.deepEqual(rowHealth('claude', ROW, state({ probe: linux })).thisSide.snippet, {
    id: 'claude', text: 'command(git grep)', warning: 'a prefix rule is not read-only', settingsPath: '/home/u/.gemini/antigravity-cli/settings.json',
  });
  assert.equal(rowHealth('claude', ROW, state()).thisSide.snippet, undefined);
});

test('a Windows agy row says agy allows only exact commands there, and names the settings file', () => {
  const fact = rowHealth('claude', ROW, state()).thisSide.agyFact;

  assert.match(fact, /exact/u);
  assert.match(fact, /settings\.json/u);
  assert.equal(rowHealth('claude', ROW, state({ probe: answered([row({ runtime: 'claude', vendor: 'claude' })]) })).thisSide.agyFact, '');
});

const WSL = '\\\\wsl.localhost\\Ubuntu\\home\\u\\.local\\share\\coai-mcp';

function other(sideFiles: SideFiles | undefined): OtherSideHealth {
  return { label: 'WSL: Ubuntu', dir: WSL, files: sideFiles };
}

test('another side is read-only: labelled with the side and the time taken, no Check, and a Remote-WSL pointer', () => {
  const block = rowHealth('claude', ROW, state({}, [other(files({}, [row()], { side: 'wsl', utc: '2026-10-03T09:58:00.0000000Z' }))])).otherSides[0];

  assert.equal(block?.readOnly, true);
  assert.equal(block?.side, 'WSL: Ubuntu');
  assert.equal(block?.asOf, '2026-10-03 09:58 UTC');
  assert.equal(block?.checkButton, undefined);
  assert.equal(block?.snippet, undefined);
  assert.match(block?.pointer ?? '', /Remote-WSL/u);
});

test('another side\'s failure is judged against THAT side\'s consultant, and its evidence is named as that side\'s path', () => {
  const block = rowHealth('claude', { vendor: 'codex', model: '' }, state({}, [other(files({}, [row({ lastFailure: {
    utc: '2026-10-03T09:30:00.0000000Z', kind: 'empty', vendor: 'antigravity', model: 'gemini-3.1-pro-high',
    what: 'answered nothing', cure: 'try again', evidence: '/home/u/.local/share/coai-mcp/unparseable/x.txt',
  } })], { side: 'wsl' }))])).otherSides[0];

  assert.equal(block?.failure?.label, 'Answered nothing', 'this window\'s row names codex; that side\'s consultant is still agy');
  assert.match(block?.failure?.evidence ?? '', /a path on WSL: Ubuntu/u);
});

test('another side\'s check is judged by how long THIS window has seen its heartbeat stand still, against its published staleness (O)', () => {
  // The stamps are deliberately far from NOW: that side's clock is not this one, and only the sighting counts.
  const running = check({ state: 'checking', finishedUtc: '', startedUtc: '2026-10-03T09:50:00.0000000Z', heartbeatUtc: '2026-10-03T09:51:00.0000000Z', side: 'wsl' });
  const sighted = (seenMs: number): SideFiles => ({ ...files({ claude: running }, [row()]), beatsSeenMs: { 'claude|2026-10-03T09:51:00.0000000Z': seenMs } });

  assert.equal(rowHealth('claude', ROW, state({}, [other(sighted(NOW - 30_000))])).otherSides[0]?.check.state, 'checking',
    'seen moving thirty seconds ago — the stamp ten minutes behind this clock says nothing about that side');
  assert.equal(rowHealth('claude', ROW, state({}, [other(sighted(NOW - 61_000))])).otherSides[0]?.check.state, 'abandoned');
  assert.equal(rowHealth('claude', ROW, state({}, [other(files({ claude: running }, [row()]))])).otherSides[0]?.check.state, 'checking',
    'a heartbeat this window has not seen yet counts as seen now — never this clock minus that side\'s stamp');
});

test('another side\'s check from a server without the staleness field: "cannot be told from here", never a spinner', () => {
  const old = check({ state: 'checking', finishedUtc: '', heartbeatStaleAfterSeconds: undefined, heartbeatUtc: '2026-10-03T10:00:45.0000000Z', side: 'wsl' });
  const block = rowHealth('claude', ROW, state({}, [other(files({ claude: old }, [row()]))])).otherSides[0];

  assert.equal(block?.check.busy, false);
  assert.match(block?.check.lines.join(' ') ?? '', /cannot be told from here/u);
});

test('another side that cannot be read right now says so, rather than showing nothing', () => {
  const block = rowHealth('claude', ROW, state({}, [other(undefined)])).otherSides[0];

  assert.match(block?.notices.join(' ') ?? '', /cannot be read/u);
});

test('the row a server reported is found by caller kind — another kind\'s facts never land on this row', () => {
  const codexOnly: ConsultantRow['callerKind'] = 'codex';
  const block = rowHealth('claude', ROW, state({ probe: answered([row({ callerKind: codexOnly })]) })).thisSide;

  assert.equal(block.cli, '');
  assert.match(block.notices.join(' '), /did not report/u);
});

test('a Check is confirmed naming the consultant the server reported for that caller kind — nothing when it reported none', () => {
  assert.deepEqual(reportedAbout(answered(), 'claude'), { vendor: 'antigravity', model: 'gemini-3.1-pro-high' });
  assert.deepEqual(reportedAbout(answered(), 'codex'), { vendor: '', model: '' });
  assert.deepEqual(reportedAbout({ kind: 'too-old' }, 'claude'), { vendor: '', model: '' });
});

test('the snippet a Copy press copies is THIS side\'s server\'s, for that caller kind — none where it sent none', () => {
  const linux = answered([row({ agy: { settingsPath: '/home/u/s.json', snippet: 'command(git grep)', snippetWarning: 'w' } })], { side: 'wsl' });

  assert.deepEqual(reportedSnippet(linux, 'claude'), { text: 'command(git grep)', settingsPath: '/home/u/s.json' });
  assert.equal(reportedSnippet(answered(), 'claude'), undefined, 'a Windows row with no rule offered one to copy');
  assert.equal(reportedSnippet(linux, 'gemini'), undefined);
});

test('another side that was never read claims nothing about its checks — not even "never checked"', () => {
  const block = rowHealth('claude', ROW, state({}, [other(undefined)])).otherSides[0];

  assert.deepEqual(block?.check.lines, [], 'a share that could not be read was reported as a side nobody ever checked');
});

test('a check of the consultant a row USED to name is labelled as such — never read as a verdict on the one it names now', () => {
  const failed = check({ state: 'failed', failureKind: 'quota', failureWhat: 'quota reached', answered: false, canary: '' });
  const block = rowHealth('claude', { vendor: 'codex', model: '' }, state({ probe: answered([row({ check: failed })]) })).thisSide;
  const said = block.check.lines.join(' ');

  assert.match(said, /antigravity · gemini-3\.1-pro-high, which this row no longer names/u);
  assert.equal(rowHealth('claude', ROW, state({ probe: answered([row({ check: failed })]) })).thisSide.check.lines.some((one) => /no longer names/u.test(one)), false,
    'a check of the very consultant the row names was labelled as somebody else\'s');
});

test('a failed check names the consultant it ran on', () => {
  const failed = check({ state: 'failed', failureKind: 'quota', failureWhat: 'quota reached', answered: false, canary: '' });

  assert.match(rowHealth('claude', ROW, state({ probe: answered([row({ check: failed })]) })).thisSide.check.lines.join(' '), /antigravity · gemini-3\.1-pro-high/u);
});

const STARTED = '2026-10-03T10:00:00.0000000Z';

function onDisk(record: CheckRecord): SideFiles {
  return files({ claude: record });
}

test('the panel killed the check at its cap: with this window\'s run over, a checking left on disk does not keep the row on Checking…', () => {
  const previous = check({ state: 'answered' });
  const left = check({ state: 'checking', finishedUtc: '', startedUtc: STARTED, heartbeatUtc: '2026-10-03T10:00:45.0000000Z' });
  const block = rowHealth('claude', ROW, state({
    probe: answered([row({ check: previous })], { utc: '2026-10-03T09:59:00.0000000Z' }),
    files: onDisk(left),
    runs: { claude: { result: { kind: 'crashed', why: 'it was stopped' }, landedUtc: '2026-10-03T10:00:50.000Z' } },
  })).thisSide;

  assert.equal(block.check.busy, false, 'a check this window already killed was shown running until somebody asked the server again');
  assert.match(block.check.lines.join(' '), /did not finish/u);
});

test('a server answer taken AFTER the check started settles it — a checking on disk does not outrank what the lock said', () => {
  const settled = check({ state: 'abandoned', startedUtc: STARTED, heartbeatUtc: '2026-10-03T10:00:30.0000000Z', finishedUtc: '', reason: 'its lock is free' });
  const left = check({ state: 'checking', finishedUtc: '', startedUtc: STARTED, heartbeatUtc: '2026-10-03T10:00:45.0000000Z' });
  const block = rowHealth('claude', ROW, state({
    probe: answered([row({ check: settled })], { utc: '2026-10-03T10:00:55.0000000Z' }),
    files: onDisk(left),
  })).thisSide;

  assert.equal(block.check.state, 'abandoned');
});

test('with no server answer at all, a checking on disk is judged by its own heartbeat — never Checking… for ever', () => {
  const dead = check({ state: 'checking', finishedUtc: '', startedUtc: '2026-10-03T09:50:00.0000000Z', heartbeatUtc: '2026-10-03T09:51:00.0000000Z' });
  const live = check({ state: 'checking', finishedUtc: '', startedUtc: STARTED, heartbeatUtc: '2026-10-03T10:00:45.0000000Z' });

  assert.equal(rowHealth('claude', ROW, state({ probe: { kind: 'asking' }, files: onDisk(dead) })).thisSide.check.busy, false,
    'a check whose heartbeat stopped long ago held the row on Checking…');
  assert.equal(rowHealth('claude', ROW, state({ probe: { kind: 'asking' }, files: onDisk(live) })).thisSide.check.state, 'checking',
    'a live check was not shown running after a reload');
});

test('an already-checking answer with no times of its own does not hold the row busy over the next state', () => {
  // The running check it found then died: its last word is a heartbeat from BEFORE the press landed.
  const after = check({ state: 'abandoned', startedUtc: '2026-10-03T09:59:00.0000000Z', heartbeatUtc: '2026-10-03T10:00:00.0000000Z', finishedUtc: '', reason: 'its lock is free' });
  const block = rowHealth('claude', ROW, state({
    probe: answered([row({ check: after })]),
    runs: { claude: { result: { kind: 'reported', record: check({ state: 'already-checking', startedUtc: '', heartbeatUtc: '', finishedUtc: '' }) }, landedUtc: '2026-10-03T10:00:05.000Z' } },
  })).thisSide;

  assert.equal(block.check.busy, false, 'a press that found another check running outranked that check\'s own result');
  assert.equal(block.check.state, 'abandoned');
  assert.match(block.check.lines.join(' '), /already running/u, 'the press that launched nothing was not said at all');
});
