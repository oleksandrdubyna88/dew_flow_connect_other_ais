import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import { NO_NAMES } from '../claudeQuestion';
import { namedAmong, projectDirNames } from '../claudeSessions';
import { RemoteRead, UNREADABLE_SAID, Unreadable, readRemote } from '../remoteFrame';
import { REGISTRY_MAX, RemoteCall, TAIL_MAX } from '../remoteScript';

/**
 * The parser of what a VM answers (E2.S2, C2): `readRemote` turns the frame of marker lines and data lines into ONE arm of
 * `RemoteRead`. Pure — the frames are typed out here; `remoteScriptRuns.test.ts` feeds it what the REAL script prints.
 *
 * <p>Each negative row changes ONE thing of a fixture the parser accepts (asserted first: `REG()` gives a `found`), so a
 * red names the thing that was changed and not an accident of the fixture.</p>
 */

const S = 'small-tasks';
const ID = '0b2e6a52-1f6c-4d0e-8a43-1c9d1d5e7f10';
const ID2 = '7c1d44b0-2a9e-4c55-9b3e-5f0a8e2d6c21';
const CALL: RemoteCall = { kind: 'session', session: S };
const KB = '/home/git/knowledge_base';
const KB_FOLDER = '-home-git-knowledge-base';

const ok = (stdout: string): { code: number; stdout: string } => ({ code: 0, stdout });
const frame = (...lines: readonly string[]): string => ['COAI-BEGIN 1', ...lines, 'COAI-END', ''].join('\n');
/** Data lines: each line of the text with the `:` the script puts before it. */
const data = (...lines: readonly string[]): string[] => lines.map((line) => `:${line}`);

/** The `why` values this file has reached — compared with every key of `UNREADABLE_SAID` by the last test. */
const reached = new Set<string>();

function unreadable(read: RemoteRead, why: Unreadable, message = ''): void {
  assert.deepEqual(read, { kind: 'unreadable', why }, message || `expected unreadable ${why}`);
  reached.add(why);
}

/** A registry file that IS valid; each test changes one field of it. */
function REG(over: Readonly<Record<string, unknown>> = {}): string {
  return JSON.stringify({ pid: 2000, sessionId: ID, cwd: KB, name: 'kb', procStart: '41288', status: 'busy', ...over });
}

interface OneOptions {
  readonly registry?: string;
  readonly state?: string;
  readonly start?: string;
  readonly transcript?: string;
  readonly files?: number;
  readonly folder?: string;
  readonly tail?: readonly string[];
  readonly sizes?: readonly [number, number];
}

/** One pane, one `claude`, its registry file and its transcript: the registry road. */
function one(options: OneOptions = {}): string {
  const { registry = REG(), state = 'ok', start = '41288', transcript = ID, files = 1, folder = KB_FOLDER,
    tail = ['{"type":"user"}', '{"type":"assistant"}'], sizes = [100, 100] } = options;

  return frame(
    'COAI-PANE 0.0 %1 1000',
    `COAI-CLAUDE 0.0 2000 ${start}`,
    `COAI-REGISTRY 2000 ${state}`,
    ...(state === 'ok' ? data(registry) : []),
    `COAI-TRANSCRIPT ${transcript} ${files}`,
    ...(files === 1
      ? [`COAI-FILE ${sizes[0]}`, ...data(folder), 'COAI-TAIL', ...data(...tail), `COAI-FILE-END ${sizes[1]}`]
      : []),
  );
}

const readOne = (options: OneOptions = {}, call: RemoteCall = CALL): RemoteRead => readRemote(ok(one(options)), call);

test('the fixture itself is a found session', () => {
  const read = readOne();
  assert.equal(read.kind, 'found');
  if (read.kind === 'found') {
    assert.deepEqual(
      { via: read.via, sessionId: read.sessionId, cwd: read.cwd, name: read.name, folder: read.folder, lines: read.lines, cut: read.cut },
      { via: 'registry', sessionId: ID, cwd: KB, name: 'kb', folder: KB_FOLDER, lines: ['{"type":"user"}', '{"type":"assistant"}'], cut: false },
    );
  }
});

test('S2-T24: a banner is not data, and no sentinel is not "absent"', () => {
  const absent = frame('COAI-NO-SESSION');
  const want: RemoteRead = { kind: 'absent', what: 'session' };
  assert.deepEqual(readRemote(ok(absent), CALL), want);
  assert.deepEqual(readRemote(ok(`Welcome to the VM\nmotd line\n${absent}`), CALL), want, 'a motd before the frame changed the answer');
  assert.deepEqual(readRemote(ok(`COAI-BEGIN 1\nbanner text\n${absent}`), CALL), want, 'a banner that holds the sentinel started the frame (the LAST one starts it)');
  assert.deepEqual(readRemote(ok(`${absent}later text\nCOAI-END\nmore`), CALL), want, 'text after COAI-END was read');

  unreadable(readRemote(ok('Welcome\nnothing else\n'), CALL), 'no-sentinel', 'a broken answer was read as no session');
  unreadable(readRemote(ok(''), CALL), 'no-sentinel');
  unreadable(readRemote(ok('COAI-BEGIN 2\nCOAI-NO-SESSION\nCOAI-END\n'), CALL), 'version');
  unreadable(readRemote(ok('COAI-BEGIN 1\nCOAI-NO-SESSION\n'), CALL), 'cut');
  unreadable(readRemote(ok(frame('hello')), CALL), 'frame');
  unreadable(readRemote(ok(frame('COAI-NO-SESSION', 'hello')), CALL), 'frame');
  unreadable(readRemote(ok(frame('COAI-WHAT 1')), CALL), 'frame', 'an unknown marker');
  unreadable(readRemote(ok(frame('COAI-NO-SESSION extra')), CALL), 'frame', 'a marker with a word it does not take');
  unreadable(readRemote(ok(frame('COAI-OVER lots')), CALL), 'frame');
  unreadable(readRemote(ok(frame(':data before any marker')), CALL), 'frame');
  unreadable(readRemote(ok(frame('COAI-NO-SESSION', ':data after a marker that holds none')), CALL), 'frame');
  unreadable(readRemote({ code: 3, stdout: absent }, CALL), 'exit', 'a whole good frame with a failing exit code was read');
  unreadable(readRemote({ code: 255, stdout: absent }, CALL), 'exit');
});

test('S2-T25: a transcript line cannot end the transcript', () => {
  const read = readOne({ tail: ['COAI-END', 'COAI-BEGIN 1', ':x', 'COAI-FILE-END 9'] });
  assert.equal(read.kind, 'found');
  if (read.kind === 'found') {
    assert.deepEqual(read.lines, ['COAI-END', 'COAI-BEGIN 1', ':x', 'COAI-FILE-END 9']);
  }
});

test('S2-T26: a cut tail drops its first part, and a tail cut inside a character is clean', () => {
  const part = '��vet"}';
  const cut = readOne({ sizes: [TAIL_MAX + 1, TAIL_MAX + 1], tail: [part, '{"a":1}', '{"b":2}'] });
  assert.ok(cut.kind === 'found' && cut.cut, 'a tail over the limit is not cut');
  assert.deepEqual(cut.kind === 'found' ? cut.lines : [], ['{"a":1}', '{"b":2}'], 'the first part of a cut tail was kept');

  const whole = readOne({ sizes: [10, 10], tail: [part, '{"a":1}'] });
  assert.ok(whole.kind === 'found' && !whole.cut);
  assert.deepEqual(whole.kind === 'found' ? whole.lines : [], [part, '{"a":1}'], 'a tail that was not cut lost its first line');

  const grew = readOne({ sizes: [TAIL_MAX, TAIL_MAX + 5], tail: [part, '{"a":1}'] });
  assert.ok(grew.kind === 'found' && grew.cut, 'a file that grew past the limit during the read is still cut');

  const empties = readOne({ tail: ['', '{"a":1}', '', '', '{"b":2}', ''] });
  assert.deepEqual(empties.kind === 'found' ? empties.lines : [], ['{"a":1}', '{"b":2}'], 'empty lines are left out');
});

test('S2-T27: a registry file that is not right is unreadable, each fault by its name', () => {
  const bigText = JSON.stringify({ note: 'é'.repeat(33_000) });
  assert.ok(bigText.length < REGISTRY_MAX && Buffer.byteLength(bigText) > REGISTRY_MAX, 'the fixture is under the limit in characters and over it in bytes');
  unreadable(readOne({ registry: bigText }), 'registry-big');

  const parsed = readOne({ registry: '{not json' });
  unreadable(parsed, 'registry-bad');
  assert.ok(!JSON.stringify(parsed).includes('not json'), 'the result quotes the text that did not parse');
  for (const [what, over] of [
    ['a session id that is not one', { sessionId: 'x' }],
    ['a pid that is not the candidate\'s', { pid: 2001 }],
    ['no cwd', { cwd: undefined }],
    ['a cwd with a line break', { cwd: '/home/git/a\nb' }],
    ['a cwd that is not absolute', { cwd: 'home/git' }],
    ['a cwd over 500 units', { cwd: `/${'a'.repeat(500)}` }],
  ] as const) {
    unreadable(readOne({ registry: REG(over) }), 'registry-bad', what);
  }
  unreadable(readOne({ registry: '[1,2]' }), 'registry-bad', 'an array is not a registry file');
  unreadable(readOne({ registry: 'null' }), 'registry-bad');

  unreadable(readOne({ start: '41289' }), 'registry-stale', 'a registry file of an older process was read as the session');
  assert.equal(readOne({ start: '41288' }).kind, 'found');
  assert.equal(readOne({ registry: REG({ procStart: undefined }) }).kind, 'found', 'no procStart: not checked');
  assert.equal(readOne({ start: '-' }).kind, 'found', 'no start time in the frame: not checked');
  assert.equal(readOne({ registry: REG({ procStart: 41288 }), start: '41289' }).kind, 'found', 'a procStart that is not a string of digits is not compared');

  unreadable(readOne({ state: 'denied', files: 0 }), 'registry-denied');
  const named = readOne({ registry: REG({ name: 'bad\u0007name' }) });
  assert.ok(named.kind === 'found' && named.name === '', 'a name with a control character is kept');
});

test('S2-T28: the transcript is found by its id, once, in the session\'s folder', () => {
  assert.deepEqual(readOne({ files: 0 }), { kind: 'absent', what: 'transcript' });
  assert.deepEqual(readOne({ files: 2 }), { kind: 'ambiguous', why: 'transcripts', candidates: [], unnamed: 0 });
  unreadable(readOne({ folder: '-home-git-other' }), 'folder', 'a file in another project\'s folder was read as this session');
  const names = projectDirNames(KB);
  assert.ok(names.length >= 2, 'the fixture path has several spellings');
  for (const folder of names) {
    const read = readOne({ folder });
    assert.ok(read.kind === 'found' && read.folder === folder, `the spelling ${folder} of the folder was refused`);
  }
  unreadable(readOne({ transcript: ID2 }), 'id-mismatch');
  const none = frame('COAI-PANE 0.0 %1 1000', 'COAI-CLAUDE 0.0 2000 41288', 'COAI-REGISTRY 2000 ok', ...data(REG()));
  unreadable(readRemote(ok(none), CALL), 'frame', 'a valid registry with no COAI-TRANSCRIPT');
  const noFile = frame('COAI-PANE 0.0 %1 1000', 'COAI-CLAUDE 0.0 2000 41288', 'COAI-REGISTRY 2000 ok', ...data(REG()), `COAI-TRANSCRIPT ${ID} 1`);
  unreadable(readRemote(ok(noFile), CALL), 'frame', 'one file and no COAI-FILE');
});

const idCall: RemoteCall = { kind: 'id', session: S, id: ID2 };

/** Two panes, each with one `claude`; `names` says each registry (`undefined` = `none`). */
function twoPanes(first: string | undefined, second: string | undefined, tail: readonly string[]): string {
  const part = (pane: string, pid: number, registry: string | undefined): string[] => [
    `COAI-PANE 0.${pane} %${Number(pane) + 1} ${pid - 1000}`,
    `COAI-CLAUDE 0.${pane} ${pid} 5`,
    ...(registry === undefined ? [`COAI-REGISTRY ${pid} none`] : [`COAI-REGISTRY ${pid} ok`, ...data(registry)]),
  ];

  return frame(...part('0', 2000, first), ...part('1', 2001, second), ...tail);
}

const foundTail = (id: string): string[] => [`COAI-TRANSCRIPT ${id} 1`, 'COAI-FILE 50', ...data('-home-git-b'), 'COAI-TAIL', ...data('{"x":1}'), 'COAI-FILE-END 50'];
const regA = (): string => REG({ pid: 2000, sessionId: ID, cwd: '/home/git/a', name: 'A', procStart: undefined });
const regB = (): string => REG({ pid: 2001, sessionId: ID2, cwd: '/home/git/b', name: 'B', procStart: undefined });

test('S2-T29: a picked id — the six cases', () => {
  // (a) one pane, one claude, a valid registry of ID: the registry wins, the pick is not read.
  const a = readOne({}, idCall);
  assert.ok(a.kind === 'found' && a.via === 'registry' && a.sessionId === ID, '(a) the registry road must win when it is exact');

  // (b) two panes, both named, the transcript of ID2: found by id with THAT candidate's cwd and name.
  const b = readRemote(ok(twoPanes(regA(), regB(), foundTail(ID2))), idCall);
  assert.ok(b.kind === 'found' && b.via === 'id' && b.sessionId === ID2 && b.cwd === '/home/git/b' && b.name === 'B', '(b)');

  // (c) one pane, no claude: found by id with no cwd.
  const noClaude = (tail: readonly string[]): string => frame('COAI-PANE 0.0 %1 1000', ...tail);
  const c = readRemote(ok(noClaude(foundTail(ID2))), idCall);
  assert.ok(c.kind === 'found' && c.via === 'id' && c.cwd === '' && c.name === '' && c.sessionId === ID2, '(c)');

  // (d) the file count 0 in (b) and in (c): gone — never absent, never the fallback.
  const gone: RemoteRead = { kind: 'gone', sessionId: ID2 };
  assert.deepEqual(readRemote(ok(twoPanes(regA(), regB(), [`COAI-TRANSCRIPT ${ID2} 0`])), idCall), gone, 'a vanished pick was reported as no session (b)');
  assert.deepEqual(readRemote(ok(noClaude([`COAI-TRANSCRIPT ${ID2} 0`])), idCall), gone, 'a vanished pick was reported as no session (c)');
  assert.deepEqual(readRemote(ok(noClaude([`COAI-TRANSCRIPT ${ID2} 2`])), idCall), { kind: 'ambiguous', why: 'transcripts', candidates: [], unnamed: 0 });

  // (e) two named candidates, none is ID2: the pick is not one of the live sessions.
  const other = '5e3a9c10-8b2d-4f61-a7c4-0d9e1b2c3a44';
  const regC = REG({ pid: 2001, sessionId: other, cwd: '/home/git/c', name: 'C', procStart: undefined });
  const e = readRemote(ok(twoPanes(regA(), regC, [`COAI-TRANSCRIPT ${ID2} 0`])), idCall);
  assert.ok(e.kind === 'ambiguous' && e.why === 'panes' && e.candidates.length === 2, '(e) a pick that is no live session was read');

  // (f) one named candidate that is not ID2, and one claude with registry none: the unnamed one may be the pick.
  const f = readRemote(ok(twoPanes(regA(), undefined, foundTail(ID2))), idCall);
  assert.ok(f.kind === 'found' && f.via === 'id' && f.sessionId === ID2, '(f)');

  // A COAI-TRANSCRIPT of another id is not an answer to this call.
  unreadable(readRemote(ok(noClaude(foundTail(ID))), idCall), 'frame');
});

test('S2-T30: the fallback\'s first call', () => {
  const noClaude = (...tail: readonly string[]): string => frame('COAI-PANE 0.0 %1 1000', 'COAI-CWD', ...data('/home/git/kb'), 'COAI-FOLDERS', ...tail);
  const folders = data('-home-git-kb', '-home-git-a', 'C--Users-x');
  const read = readRemote(ok(noClaude(...folders, 'COAI-FOLDERS-END 2 0')), CALL);
  assert.deepEqual(read, { kind: 'fallback', paneCwd: '/home/git/kb', folders: ['-home-git-kb', '-home-git-a', 'C--Users-x'], skipped: 2, more: false });
  const more = readRemote(ok(noClaude(...folders, 'COAI-FOLDERS-END 0 1')), CALL);
  assert.ok(more.kind === 'fallback' && more.more && more.skipped === 0);
  unreadable(readRemote(ok(noClaude(...data('a b'), 'COAI-FOLDERS-END 0 0')), CALL), 'frame', 'a folder name with a space');
  unreadable(readRemote(ok(noClaude(...data('.hidden'), 'COAI-FOLDERS-END 0 0')), CALL), 'frame');
  unreadable(readRemote(ok(frame('COAI-PANE 0.0 %1 1000', 'COAI-CWD', ...data('home/git'), 'COAI-FOLDERS', 'COAI-FOLDERS-END 0 0')), CALL), 'frame', 'a pane folder that is not absolute');
  unreadable(readRemote(ok(frame('COAI-PANE 0.0 %1 1000', 'COAI-CWD', ...data('/home/git/kb'))), CALL), 'frame', 'a fallback with no folder listing');

  const registryNone = frame('COAI-PANE 0.0 %1 1000', 'COAI-CLAUDE 0.0 2000 5', 'COAI-REGISTRY 2000 none', 'COAI-CWD', ...data('/home/git/kb'), 'COAI-FOLDERS', 'COAI-FOLDERS-END 0 0');
  assert.equal(readRemote(ok(registryNone), CALL).kind, 'fallback', 'one pane and one claude with no registry file is the fallback');
});

const F1 = '-home-git-a';
const F2 = '-home-git-b';
const titlesCall: RemoteCall = { kind: 'titles', session: S, folders: [F1, F2] };
const ai = (title: string): string => JSON.stringify({ type: 'ai-title', aiTitle: title });
const custom = (title: string): string => JSON.stringify({ type: 'custom-title', customTitle: title });

test('S2-T31: the title rows are read by the one rule', () => {
  const stdout = frame(
    `COAI-FOLDER ${F1} ok`,
    `COAI-SESSION ${ID} 1794411443 201 rows`, ...data(ai('A'), custom(S), ai('A')), 'COAI-ROWS 0 0',
    `COAI-FOLDER ${F2} ok`,
    `COAI-SESSION ${ID2} - 70000000 skipped`,
    'COAI-SCAN 2 all',
  );
  const read = readRemote(ok(stdout), titlesCall);
  assert.equal(read.kind, 'titles');
  if (read.kind !== 'titles') {
    return;
  }
  assert.deepEqual(read.sessions[0], { sessionId: ID, folder: F1, mtime: 1794411443, names: { latest: 'A', former: [S] }, skipped: false },
    'the names were not built by collectNames over the rows in order (the latest is the LAST row, the former hold the others)');
  assert.deepEqual(read.sessions[1], { sessionId: ID2, folder: F2, mtime: 0, names: NO_NAMES, skipped: true });
  assert.deepEqual(read.scan, { why: 'all', tooLong: 0, more: false });

  const counted = readRemote(ok(frame(
    `COAI-FOLDER ${F1} ok`, `COAI-SESSION ${ID} 5 9 rows`, ...data(ai('A')), 'COAI-ROWS 3 1', `COAI-FOLDER ${F2} none`, 'COAI-SCAN 250 count')), titlesCall);
  assert.ok(counted.kind === 'titles' && counted.scan.why === 'count' && counted.scan.tooLong === 3 && counted.scan.more, 'the scan\'s own words');
  assert.ok(counted.kind === 'titles' && counted.sessions.length === 1);

  unreadable(readRemote(ok(frame('COAI-FOLDER -home-git-elsewhere ok', 'COAI-SCAN 0 all')), titlesCall), 'frame', 'a COAI-FOLDER that is not in the call');
  unreadable(readRemote(ok(frame(`COAI-FOLDER ${F1} ok`, `COAI-SESSION ${ID} 5 9 rows`, 'COAI-SCAN 1 all')), titlesCall), 'frame', 'a rows session with no COAI-ROWS');
  unreadable(readRemote(ok(frame(`COAI-FOLDER ${F1} ok`)), titlesCall), 'frame', 'no COAI-SCAN');
  unreadable(readRemote(ok(frame(`COAI-SESSION ${ID} 5 9 skipped`, 'COAI-SCAN 1 all')), titlesCall), 'frame', 'a session before any folder');
  unreadable(readRemote(ok(frame('COAI-NO-SESSION')), titlesCall), 'frame', 'a marker of the other forms in a titles answer');
  unreadable(readRemote(ok(frame('COAI-BAD tool')), titlesCall), 'tool');

  // End to end through the rule that reads these names: the session that answers to the name is the one.
  const three = readRemote(ok(frame(
    `COAI-FOLDER ${F1} ok`,
    `COAI-SESSION ${ID} 1 1 rows`, ...data(ai('A'), custom(S), ai('A')), 'COAI-ROWS 0 0',
    `COAI-SESSION ${ID2} 2 1 rows`, ...data(ai('other')), 'COAI-ROWS 0 0',
    `COAI-SESSION 5e3a9c10-8b2d-4f61-a7c4-0d9e1b2c3a44 3 1 rows`, ...data(custom('third')), 'COAI-ROWS 0 0',
    'COAI-SCAN 3 all')), titlesCall);
  assert.ok(three.kind === 'titles');
  const named = namedAmong(three.kind === 'titles' ? three.sessions : [], (session) => session.names, S);
  assert.deepEqual(named.map((session) => session.sessionId), [ID], 'the session that once carried the name was not the one found');
});

test('S2-T32: several panes or processes are a list', () => {
  const two = readRemote(ok(twoPanes(regA(), regB(), [])), CALL);
  assert.deepEqual(two, {
    kind: 'ambiguous',
    why: 'panes',
    candidates: [
      { pane: '0.0', pid: 2000, sessionId: ID, name: 'A', cwd: '/home/git/a' },
      { pane: '0.1', pid: 2001, sessionId: ID2, name: 'B', cwd: '/home/git/b' },
    ],
    unnamed: 0,
  }, 'a two-pane session was read without a choice');

  const twoProcesses = frame(
    'COAI-PANE 0.0 %1 1000',
    'COAI-CLAUDE 0.0 2000 5', 'COAI-REGISTRY 2000 ok', ...data(regA()),
    'COAI-CLAUDE 0.0 2001 6', 'COAI-REGISTRY 2001 none',
  );
  assert.deepEqual(readRemote(ok(twoProcesses), CALL), {
    kind: 'ambiguous', why: 'processes', candidates: [{ pane: '0.0', pid: 2000, sessionId: ID, name: 'A', cwd: '/home/git/a' }], unnamed: 1,
  });

  const none = frame('COAI-PANE 0.0 %1 1000', 'COAI-PANE 0.1 %2 1001');
  assert.deepEqual(readRemote(ok(none), CALL), { kind: 'ambiguous', why: 'panes', candidates: [], unnamed: 0 });
  // A candidate whose registry file is not right is unnamed, not dropped and not a fault of the whole answer.
  const bad = readRemote(ok(twoPanes(regA(), '{not json', [])), CALL);
  assert.ok(bad.kind === 'ambiguous' && bad.candidates.length === 1 && bad.unnamed === 1);
  assert.deepEqual(readRemote(ok(frame('COAI-PANE 0.0 %1 1000', 'COAI-CLAUDE 0.1 2000 5', 'COAI-REGISTRY 2000 none')), CALL), { kind: 'unreadable', why: 'frame' }, 'a claude on a pane that was never listed');
  reached.add('frame');
});

test('S2-T33: every fault has its own words', () => {
  assert.deepEqual(readRemote(ok(frame('COAI-NO-SESSION')), CALL), { kind: 'absent', what: 'session' });
  for (const what of ['operands', 'home', 'tool', 'panes', 'cwd', 'transcript'] as const) {
    unreadable(readRemote(ok(frame(`COAI-BAD ${what}`)), CALL), what);
  }
  unreadable(readRemote(ok(frame('COAI-BAD nonsense')), CALL), 'frame');
  unreadable(readRemote(ok(frame('COAI-OVER panes')), CALL), 'too-many');
  unreadable(readRemote(ok(frame('COAI-PANE 0.0 %1 1000', 'COAI-OVER walk')), CALL), 'too-many');

  const clauses = Object.entries(UNREADABLE_SAID);
  assert.ok(clauses.length >= 18);
  for (const [why, clause] of clauses) {
    assert.ok(clause.length > 0, `the clause of ${why} is empty`);
  }
  assert.equal(new Set(clauses.map(([, clause]) => clause)).size, clauses.length, 'two faults share one clause');
});

test('S2-T33: every fault of the list was reached by some test of this file', () => {
  assert.deepEqual([...reached].sort(), Object.keys(UNREADABLE_SAID).sort(), 'a fault in UNREADABLE_SAID is never produced, or one is produced that is not listed');
});

test('S2-T33: readRemote never throws, whatever it is given', () => {
  const stuff = ['', '\n', 'COAI-BEGIN 1\nCOAI-END', frame(':'), frame('COAI-'), frame('COAI-PANE'), frame(' '), 'COAI-BEGIN 1\n\n\nCOAI-END', '\0\0\0'];
  for (const stdout of stuff) {
    for (const call of [CALL, idCall, titlesCall]) {
      assert.doesNotThrow(() => readRemote(ok(stdout), call));
    }
  }
});
