import assert from 'node:assert/strict';
import { test } from 'node:test';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import { CHECK_STATES_WORDED } from '../consultantHealthState';
import {
  CANARY_READINGS,
  type CheckRecord,
  HEALTH_FILE_CAP_BYTES,
  LIVENESS_UNKNOWN,
  canaryWording,
  checkStateOf,
  checkStateOnThisDisk,
  failureLabel,
  parseCheckDocument,
  parseCheckFile,
  parseConsultantsAnswer,
  parseConsultantsFile,
  utcShort,
} from '../consultantHealth';

/**
 * The Consultant tab's source of truth, READ — epic 5 (E5.1) of PLAN_the_consultant_works_on_every_vendor.md.
 *
 * <p>What the server answers (`--consultants`, `--check-consultant`) and what it leaves on disk for the other side
 * (`consultations/health/consultants.json`, `&lt;kind&gt;.check.json`) are parsed here, and the two halves ship
 * separately: an extension newer than its server meets an answer without the fields it knows, and an older one meets
 * fields it does not. Every fallback is therefore a test. The failure words are asserted against
 * `shared/consult-failure-kinds.json`, a file neither half owns, never against the server's source.</p>
 */

interface SharedKind {
  readonly word: string;
  readonly label: string;
}

const KINDS: readonly SharedKind[] = JSON.parse(
  readFileSync(join(__dirname, '..', '..', '..', 'shared', 'consult-failure-kinds.json'), 'utf8'),
).kinds;

interface SharedWord {
  readonly word: string;
  readonly observedConfinement?: boolean;
}

/** The check's states and canary readings, as both halves are held to them (the whole-branch review, J). */
const CHECK_WORDS: { readonly states: readonly SharedWord[]; readonly canaryReadings: readonly SharedWord[] } = JSON.parse(
  readFileSync(join(__dirname, '..', '..', '..', 'shared', 'consult-check-words.json'), 'utf8'),
);

const NOW = Date.parse('2026-10-03T10:01:00.0000000Z');

function bytes(text: string): Uint8Array {
  return new TextEncoder().encode(text);
}

/** A check record as the server writes it — every field, so a parse that drops one is seen. */
function checkJson(over: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    callerKind: 'claude',
    state: 'checking',
    startedUtc: '2026-10-03T10:00:00.0000000Z',
    deadlineUtc: '2026-10-03T10:07:00.0000000Z',
    heartbeatUtc: '2026-10-03T10:00:30.0000000Z',
    finishedUtc: '',
    heartbeatStaleAfterSeconds: 60,
    side: 'wsl',
    vendor: 'antigravity',
    runtime: 'antigravity',
    model: 'gemini-3.1-pro-high',
    confinement: '',
    answered: false,
    markerRead: false,
    canary: '',
    deniedActions: [],
    seconds: 0,
    tokensIn: 0,
    tokensOut: 0,
    failureKind: '',
    failureWhat: '',
    failureCure: '',
    evidence: '',
    reason: '',
    ...over,
  };
}

function record(over: Record<string, unknown> = {}): CheckRecord {
  const parsed = parseCheckDocument(JSON.stringify(checkJson(over)));
  assert.ok(parsed !== undefined, 'the fixture record did not parse, so this test asserts nothing');

  return parsed;
}

/** One `--consultants` row, as the server writes it. */
function row(over: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    callerKind: 'claude',
    available: true,
    reason: '',
    vendor: 'antigravity',
    runtime: 'antigravity',
    model: 'gemini-3.1-pro-high',
    executablePath: '',
    cli: { probed: true, found: true, version: '1.2.15', authSource: 'own auth', note: 'fine' },
    capability: '',
    limitation: {
      standing: 'default-deny', text: 'agy denies a shell command headless', capability: '', evidence: 'measured',
      measuredDate: '2026-10-02', measuredCliVersion: '1.2.15', measuredCells: '6/6',
      measuredDocument: 'research/RESULTS_agy_consult_follow_up.md', source: '',
    },
    agy: { settingsPath: '/home/u/.gemini/antigravity-cli/settings.json', snippet: 'command(git grep)', snippetWarning: 'a prefix rule is not read-only' },
    lastAnswer: null,
    lastFailure: {
      utc: '2026-10-03T09:00:00.0000000Z', consultationId: 'c1', callerKind: 'claude', vendor: 'antigravity',
      runtime: 'antigravity', model: 'gemini-3.1-pro-high', kind: 'command-denied', what: 'agy denied a shell command',
      cure: 'add an allow rule', evidence: '/home/u/x.txt', side: 'wsl', confinement: '',
    },
    failureCurrent: true,
    check: checkJson({ state: 'answered', answered: true, markerRead: true, canary: 'denied-by-cli' }),
    ...over,
  };
}

test('a server too old for the mode exits 64, and that is "update the server", never an empty tab', () => {
  assert.deepEqual(parseConsultantsAnswer(64, ''), { kind: 'too-old' });
});

test('an answer with no consultants field at all is a server from before the field — too old, not "nobody configured"', () => {
  assert.deepEqual(parseConsultantsAnswer(0, JSON.stringify({ utc: '2026-10-03T10:00:00Z', side: 'windows' })), { kind: 'too-old' });
});

test('an EMPTY consultants list is answered — a server that reports no rows is not a server that could not report', () => {
  const answer = parseConsultantsAnswer(0, JSON.stringify({ utc: '2026-10-03T10:00:00Z', side: 'windows', consultants: [] }));

  assert.equal(answer.kind, 'answered');
  assert.deepEqual(answer.kind === 'answered' ? answer.report.consultants : undefined, []);
});

test('every field of a row is read, the nested check and the published staleness included', () => {
  const answer = parseConsultantsAnswer(0, JSON.stringify({
    utc: '2026-10-03T10:00:00Z', side: 'wsl', distro: 'Ubuntu', heartbeatStaleAfterSeconds: 60, consultants: [row()],
  }));
  assert.equal(answer.kind, 'answered');
  const report = answer.kind === 'answered' ? answer.report : undefined;
  const one = report?.consultants[0];

  assert.equal(report?.side, 'wsl');
  assert.equal(report?.distro, 'Ubuntu');
  assert.equal(report?.heartbeatStaleAfterSeconds, 60);
  assert.equal(one?.callerKind, 'claude');
  assert.deepEqual(one?.cli, { probed: true, found: true, version: '1.2.15', authSource: 'own auth', note: 'fine' });
  assert.equal(one?.limitation?.standing, 'default-deny');
  assert.equal(one?.limitation?.measuredDocument, 'research/RESULTS_agy_consult_follow_up.md');
  assert.equal(one?.agy?.snippet, 'command(git grep)');
  assert.equal(one?.agy?.snippetWarning, 'a prefix rule is not read-only');
  assert.equal(one?.lastAnswer, undefined, 'a null answer is no answer');
  assert.equal(one?.lastFailure?.kind, 'command-denied');
  assert.equal(one?.failureCurrent, true);
  assert.equal(one?.check?.state, 'answered');
  assert.equal(one?.check?.canary, 'denied-by-cli');
});

test('a body that is not JSON, or an exit that is neither 0 nor 64, is unanswered — and says which', () => {
  assert.equal(parseConsultantsAnswer(0, 'not json').kind, 'unanswered');
  const crashed = parseConsultantsAnswer(74, '');
  assert.equal(crashed.kind, 'unanswered');
  assert.match(crashed.kind === 'unanswered' ? crashed.why : '', /74/u, 'the exit code is the one fact a person can report');
});

test('a row with junk in its fields reads defaults, never a throw and never a junk value', () => {
  const answer = parseConsultantsAnswer(0, JSON.stringify({
    consultants: [{ callerKind: 'codex', available: 'yes', cli: 'found', limitation: 7, agy: [], check: { state: 3 } }, 'not a row'],
  }));
  const one = answer.kind === 'answered' ? answer.report.consultants : [];

  assert.equal(one.length, 1, 'a row that is not an object is not a row');
  assert.equal(one[0]?.available, false);
  assert.equal(one[0]?.cli.probed, false);
  assert.equal(one[0]?.limitation, undefined);
  assert.equal(one[0]?.agy, undefined);
  assert.equal(one[0]?.check, undefined, 'a check with no state word is no check');
});

test('a health file over the cap is refused BEFORE it is parsed, and says why', () => {
  const huge = JSON.stringify(checkJson({ reason: 'x'.repeat(HEALTH_FILE_CAP_BYTES) }));
  const read = parseCheckFile(bytes(huge));

  assert.equal(read.kind, 'unreadable');
  assert.match(read.kind === 'unreadable' ? read.why : '', /larger than/u);
  assert.equal(parseConsultantsFile(bytes(huge)).kind, 'unreadable');
});

test('a check file that is not JSON, or holds no state word, is unreadable — never a check nobody ran', () => {
  assert.equal(parseCheckFile(bytes('{')).kind, 'unreadable');
  assert.equal(parseCheckFile(bytes(JSON.stringify({ callerKind: 'claude' }))).kind, 'unreadable');
  const found = parseCheckFile(bytes(JSON.stringify(checkJson())));
  assert.equal(found.kind, 'found');
  assert.equal(found.kind === 'found' ? found.value.heartbeatStaleAfterSeconds : 0, 60);
});

test('a check file from a server before the staleness field reads it as absent, never as zero', () => {
  const read = parseCheckFile(bytes(JSON.stringify(checkJson({ heartbeatStaleAfterSeconds: undefined }))));

  assert.equal(read.kind === 'found' ? read.value.heartbeatStaleAfterSeconds : 'not found', undefined);
});

test('a consultants file without the consultants field is unreadable, and one with it is found', () => {
  assert.equal(parseConsultantsFile(bytes(JSON.stringify({ utc: 'x' }))).kind, 'unreadable');
  assert.equal(parseConsultantsFile(bytes(JSON.stringify({ utc: 'x', consultants: [] }))).kind, 'found');
});

test('THIS side: the server settled the state, so it is shown as given — even a heartbeat long silent', () => {
  const quiet = record({ heartbeatUtc: '2026-10-03T09:00:00.0000000Z' });

  assert.equal(checkStateOf(quiet, 'this', NOW).state, 'checking',
    'on its own side the lock decides, and the server asked it — a TypeScript reading of the heartbeat would overrule it');
});

test('the OTHER side: a heartbeat this window saw move recently is still checking, one it has seen stand still past the published staleness is abandoned (O)', () => {
  // The stamp is the OTHER side's clock and is never subtracted from this one; only this window's own sighting counts.
  const beat = record({ heartbeatUtc: '2026-10-03T09:30:00.0000000Z' });
  assert.equal(checkStateOf(beat, 'other', NOW, NOW - 30_000).state, 'checking', 'a stamp half an hour behind this clock read as a dead check');
  const stale = checkStateOf(beat, 'other', NOW, NOW - 61_000);
  assert.equal(stale.state, 'abandoned');
  assert.match(stale.reason, /heartbeat/u);
  assert.match(stale.reason, /this window's clock/u);
});

test('the OTHER side: the staleness is the FILE\'s — a shorter published margin calls the same sighting abandoned', () => {
  const beat = { heartbeatUtc: '2026-10-03T10:00:30.0000000Z' };

  assert.equal(checkStateOf(record({ ...beat, heartbeatStaleAfterSeconds: 60 }), 'other', NOW, NOW - 30_000).state, 'checking');
  assert.equal(checkStateOf(record({ ...beat, heartbeatStaleAfterSeconds: 10 }), 'other', NOW, NOW - 30_000).state, 'abandoned',
    'a margin of the panel\'s own would ignore what the server published');
});

test('THIS side\'s own disk: a checking no server has settled is judged by its own stamp — one clock, the server\'s and this window\'s', () => {
  assert.equal(checkStateOnThisDisk(record({ heartbeatUtc: '2026-10-03T10:00:30.0000000Z' }), NOW).state, 'checking');
  assert.equal(checkStateOnThisDisk(record({ heartbeatUtc: '2026-10-03T09:59:00.0000000Z' }), NOW).state, 'abandoned');
  assert.equal(checkStateOnThisDisk(record({ state: 'answered' }), NOW).state, 'answered');
});

test('the OTHER side, a file with no staleness field: whether it still runs cannot be told — never a spinner, never a guess', () => {
  // `undefined` is dropped by JSON.stringify, so the record is parsed from a document WITHOUT the field.
  const old = record({ heartbeatStaleAfterSeconds: undefined, heartbeatUtc: '2026-10-03T09:00:00.0000000Z' });
  assert.equal(old.heartbeatStaleAfterSeconds, undefined, 'the fixture still carries the field, so this asserts nothing');
  const judged = checkStateOf(old, 'other', NOW);

  assert.equal(judged.state, LIVENESS_UNKNOWN);
});

test('the OTHER side, a heartbeat nobody can parse is judged like any other value — by how long this window has seen it unchanged (O)', () => {
  assert.equal(checkStateOf(record({ heartbeatUtc: '' }), 'other', NOW, NOW).state, 'checking', 'first seen now');
  assert.equal(checkStateOf(record({ heartbeatUtc: '' }), 'other', NOW, NOW - 61_000).state, 'abandoned');
});

test('a finished check is what it says on either side', () => {
  for (const state of ['answered', 'failed', 'unavailable', 'already-checking', 'abandoned', 'unreadable']) {
    assert.equal(checkStateOf(record({ state, heartbeatUtc: '' }), 'other', NOW).state, state);
    assert.equal(checkStateOf(record({ state }), 'this', NOW).state, state);
  }
});

test('every failure kind of the shared catalogue has the catalogue\'s own label here', () => {
  assert.ok(KINDS.length >= 14, 'the shared catalogue was not read, and this test now asserts nothing');
  for (const kind of KINDS) {
    assert.equal(failureLabel(kind.word), kind.label, `${kind.word} is worded differently from the catalogue both halves share`);
  }
});

test('a failure word this build does not know is named, never dropped or guessed at', () => {
  assert.match(failureLabel('a-newer-word'), /a-newer-word/u);
});

test('only a refusal the CLI recorded against the canary is called observed confinement', () => {
  for (const reading of CANARY_READINGS) {
    const said = canaryWording(reading);
    assert.equal(/observed confinement/u.test(said), reading === 'denied-by-cli',
      `"${reading}" is worded as if it proved more, or less, than it does: ${said}`);
  }
  assert.match(canaryWording('read'), /LEAK/u);
});

test('the four canary readings the server writes are all worded', () => {
  for (const reading of ['read', 'denied-by-cli', 'denied-by-cli-unattributed', 'not-attempted']) {
    assert.ok(CANARY_READINGS.includes(reading), `${reading} has no wording here`);
  }
  assert.match(canaryWording('a-fifth-reading'), /a-fifth-reading/u);
  assert.equal(canaryWording(''), '', 'a consultant that answered nothing has no canary reading to word');
});

test('a time is shown absolute and in UTC — a relative one would repaint the page on every tick', () => {
  assert.equal(utcShort('2026-10-03T10:00:30.1234567Z'), '2026-10-03 10:00 UTC');
  assert.equal(utcShort(''), 'an unknown time');
  assert.equal(utcShort('soon'), 'soon');
});

test('every check state of the shared catalogue is put into words here — none reaches a person as a bare identifier (J)', () => {
  assert.ok(CHECK_WORDS.states.length >= 7, 'the shared catalogue was not read, and this test now asserts nothing');
  for (const state of CHECK_WORDS.states) {
    assert.ok(CHECK_STATES_WORDED.includes(state.word), `${state.word} is a state the server writes and this panel has no words for`);
  }
});

test('the canary is worded for exactly the readings the shared catalogue lists, and only its observed one claims confinement (J)', () => {
  assert.deepEqual([...CANARY_READINGS].sort(), CHECK_WORDS.canaryReadings.map((one) => one.word).sort());
  for (const reading of CHECK_WORDS.canaryReadings) {
    assert.equal(/observed confinement/u.test(canaryWording(reading.word)), reading.observedConfinement === true, reading.word);
  }
});
