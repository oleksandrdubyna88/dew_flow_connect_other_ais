import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { randomUUID } from 'node:crypto';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { projectDirNames, promptsFrom } from '../claudeSessions';
import { MOST_PER_PROMPT, MOST_PROMPTS, NOTHING_OF_YOURS } from '../sessionPrompts';
import { RemoteRead, UNREADABLE_SAID } from '../remoteFrame';
import {
  HostAnswer, PickRow, Picked, RemoteAnswer, RoadSeams,
  answerFrom, answersOf, consentOf, nextStep, onePerTerminal, promptsSaid, reachOf, readsKey, remoteRoad, withAnswer, withoutHost,
} from '../remoteRead';
import { RemoteCall, TAIL_MAX, operandsOf } from '../remoteScript';
import { RemoteRun } from '../remoteSsh';

/**
 * The one road from a terminal to its remote Claude session (E2.S3, contract C4): the consent, the registry path, the
 * fallback, the pick list, the picked id, the meaning of a cut, the re-check before each spawn and the latch.
 *
 * <p>Pure: `remoteRoad` takes every effect as a seam, so the whole road runs here with a fake `run` that answers with the
 * frames `REMOTE_SCRIPT` prints (typed out the way `remoteFrame.test.ts` types them — the parser is the REAL one, so a
 * road that misreads an arm is seen). The host module only supplies the `vscode` seams; `terminalHostsScenario.ts` runs it
 * in a real editor (S3-H1, CI only).</p>
 */

const H = 'ai1-dev2';
const S = 'small-tasks';
const NAME = `Creds: ${H} / ${S}`;
const ID = '0b2e6a52-1f6c-4d0e-8a43-1c9d1d5e7f10';
const ID2 = '7c1d44b0-2a9e-4c55-9b3e-5f0a8e2d6c21';
const ID3 = '5e3a9c10-8b2d-4f61-a7c4-0d9e1b2c3a44';
const KB = '/home/git/knowledge_base';
const KB_FOLDER = '-home-git-knowledge-base';
const NOW = '2026-11-11T10:00:00.000Z';

const answered = (stdout: string): RemoteRun => ({ kind: 'answered', code: 0, stdout });
const refusedRun = (refusal: string): RemoteRun => ({ kind: 'refused', refusal });
const frame = (...lines: readonly string[]): string => ['COAI-BEGIN 1', ...lines, 'COAI-END', ''].join('\n');
const data = (...lines: readonly string[]): string[] => lines.map((line) => `:${line}`);

const asks = (id: string): string => JSON.stringify({
  type: 'assistant',
  sessionId: 's',
  timestamp: '2026-11-11T09:00:00.000Z',
  message: { content: [{ type: 'tool_use', id, name: 'AskUserQuestion', input: { questions: [{
    header: 'Scope', question: 'How far should this go?', multiSelect: false, options: [{ label: 'All of it', description: 'every part' }, { label: 'Just the first', description: 'and stop' }],
  }] } }] },
});
const said = (text: string): string => JSON.stringify({ type: 'user', message: { content: text }, origin: { kind: 'human' } });
const ai = (title: string): string => JSON.stringify({ type: 'ai-title', aiTitle: title });
const reg = (over: Readonly<Record<string, unknown>>): string => JSON.stringify({ pid: 2000, sessionId: ID, cwd: KB, name: 'kb', status: 'busy', ...over });

const tailOf = (id: string, folder: string, lines: readonly string[]): string[] =>
  [`COAI-TRANSCRIPT ${id} 1`, 'COAI-FILE 50', ...data(folder), 'COAI-TAIL', ...data(...lines), 'COAI-FILE-END 50'];

/** The registry road's answer: one pane, one claude, its registry file, its transcript. */
const registryFrame = (lines: readonly string[] = [said('hello')]): string => frame(
  'COAI-PANE 0.0 %1 1000', 'COAI-CLAUDE 0.0 2000 5', 'COAI-REGISTRY 2000 ok', ...data(reg({ procStart: undefined })), ...tailOf(ID, KB_FOLDER, lines),
);
/** The answer to an `id` call about a pane with no `claude` of its own. */
const idFrame = (id: string, folder: string, lines: readonly string[] = [said('picked')]): string =>
  frame('COAI-PANE 0.0 %1 1000', ...tailOf(id, folder, lines));
/** The registry road over a tail that is over the limit: the first line is the cut part of a line. */
const cutFrame = (lines: readonly string[]): string => frame(
  'COAI-PANE 0.0 %1 1000', 'COAI-CLAUDE 0.0 2000 5', 'COAI-REGISTRY 2000 ok', ...data(reg({ procStart: undefined })),
  `COAI-TRANSCRIPT ${ID} 1`, `COAI-FILE ${TAIL_MAX + 1}`, ...data(KB_FOLDER), 'COAI-TAIL', ...data('part of a line', ...lines), `COAI-FILE-END ${TAIL_MAX + 1}`,
);
const goneFrame = (id: string): string => frame('COAI-PANE 0.0 %1 1000', `COAI-TRANSCRIPT ${id} 0`);
const fallbackFrame = (cwd: string, folders: readonly string[], skipped = 0, more = false): string =>
  frame('COAI-PANE 0.0 %1 1000', 'COAI-CWD', ...data(cwd), 'COAI-FOLDERS', ...data(...folders), `COAI-FOLDERS-END ${skipped} ${more ? 1 : 0}`);

interface TitleSession { readonly id: string; readonly folder: string; readonly title: string; readonly mtime?: number }
const titlesFrame = (sessions: readonly TitleSession[], scan = 'all', skipped = false): string => {
  const folders = [...new Set(sessions.map((one) => one.folder))];

  return frame(
    ...folders.flatMap((folder) => [
      `COAI-FOLDER ${folder} ok`,
      ...sessions.filter((one) => one.folder === folder).flatMap((one) => [`COAI-SESSION ${one.id} ${one.mtime ?? 100} 9 rows`, ...data(ai(one.title)), 'COAI-ROWS 0 0']),
    ]),
    ...(skipped ? [`COAI-SESSION ${ID3} - 70000000 skipped`] : []),
    `COAI-SCAN ${sessions.length} ${scan}`,
  );
};

const candidate = (pane: string, pid: number, id: string, cwd: string, name: string): string[] => [
  `COAI-PANE 0.${pane} %${Number(pane) + 1} ${pid - 1000}`, `COAI-CLAUDE 0.${pane} ${pid} 5`, `COAI-REGISTRY ${pid} ok`, ...data(reg({ pid, sessionId: id, cwd, name, procStart: undefined })),
];
const twoPanes = (...tail: readonly string[]): string => frame(...candidate('0', 2000, ID, '/home/git/a', 'A'), ...candidate('1', 2001, ID2, '/home/git/b', 'B'), ...tail);
const onePane = (...tail: readonly string[]): string => frame(...candidate('0', 2000, ID, '/home/git/a', 'A'), 'COAI-PANE 0.1 %2 1001', ...tail);

/** What a fake run is told: the operands it was called with, and how many calls came before. */
type Script = (operands: readonly string[], calls: number) => RemoteRun;

interface Options {
  readonly list?: unknown;
  readonly answer?: 'yes' | 'no' | undefined;
  readonly script?: Script;
  readonly choose?: (rows: readonly PickRow[]) => string | undefined;
  readonly live?: (reads: number) => string | undefined;
  readonly slowAsk?: boolean;
}

const yesFor = (host: string): readonly HostAnswer[] => [{ host, answer: 'yes', askedUtc: '2026-11-01T00:00:00.000Z' }];

function world(options: Options = {}) {
  const w = {
    list: options.list ?? yesFor(H),
    writes: [] as (readonly HostAnswer[])[],
    asks: [] as string[],
    runs: [] as (readonly string[])[],
    picks: [] as (readonly PickRow[])[],
    kept: [] as (Picked | undefined)[],
    reads: 0,
    progress: 0,
    releases: [] as (() => void)[],
  };
  const seams: RoadSeams = {
    store: { read: () => w.list, write: async (rows) => { w.list = rows; w.writes.push(rows); } },
    now: () => NOW,
    ask: async (host) => {
      w.asks.push(host);
      if (options.slowAsk === true) {
        await new Promise<void>((resolve) => { w.releases.push(resolve); });
      }

      return options.answer;
    },
    liveName: () => { const one = options.live?.(w.reads); w.reads += 1; return options.live === undefined ? NAME : one; },
    run: async (_host, _session, operands) => {
      const calls = w.runs.length;
      w.runs.push(operands);

      return (options.script ?? (() => answered(registryFrame())))(operands, calls);
    },
    pick: async (rows) => { w.picks.push(rows); return options.choose?.(rows); },
    progress: async (_host, work) => { w.progress += 1; return work(); },
    keep: (picked) => { w.kept.push(picked); },
  };

  return { w, seams };
}

type World = ReturnType<typeof world>;

const sequence = (...answers: readonly RemoteRun[]): Script => (_operands, calls) => answers[calls] ?? refusedRun('the script ran out of answers');
const refusal = (answer: { readonly kind: string; readonly refusal?: string }): string => (answer.kind === 'refused' ? (answer.refusal ?? '') : `a ${answer.kind} answer, not a refusal`);
const linesOf = (answer: RemoteAnswer): readonly string[] => (answer.kind === 'lines' ? answer.lines : []);
const id = (call: RemoteCall): readonly string[] => operandsOf(call);

test('S3-T1: an answer is kept per host, and a damaged list is not a "yes"', () => {
  assert.equal(consentOf([], 'h'), 'ask');
  const none = withAnswer([], 'h', 'no', NOW);
  assert.equal(consentOf(none, 'h'), 'no');
  assert.equal(consentOf(none, 'H'), 'ask', 'a host is compared exactly, not by case');
  assert.equal(consentOf(none, 'h2'), 'ask');
  assert.equal(consentOf(withAnswer(none, 'h', 'yes', NOW), 'h'), 'yes', 'the host\'s old row was not replaced');
  assert.equal(withAnswer(none, 'h', 'yes', NOW).length, 1, 'a changed answer made a second row');

  for (const damaged of ['junk', null, undefined, 7, {}, [{ host: 'h' }], [{ host: 'h', answer: 'yes' }], [{ host: 'h', answer: 'maybe', askedUtc: NOW }],
    [{ host: '', answer: 'yes', askedUtc: NOW }], [{ host: 'h', answer: 'yes', askedUtc: 5 }], [null], ['h'],
    [{ host: 'h', answer: 'yes', askedUtc: NOW }, 'junk']]) {
    assert.equal(consentOf(damaged, 'h'), 'ask', `a damaged answer list let ssh run: ${JSON.stringify(damaged)}`);
  }
  assert.deepEqual(withAnswer('junk', 'h', 'no', NOW), [{ host: 'h', answer: 'no', askedUtc: NOW }], 'a damaged list is not carried into the new one');
});

test('S3-T2: the list is capped at 100 and the oldest answer goes', () => {
  const rows: HostAnswer[] = Array.from({ length: 100 }, (_, n) => ({ host: `h${n}`, answer: 'yes', askedUtc: `2026-10-${String(10 + (n % 20)).padStart(2, '0')}T00:00:00.000Z` }));
  rows[37] = { host: 'oldest', answer: 'no', askedUtc: '2026-01-01T00:00:00.000Z' };
  const before = JSON.stringify(rows);
  const next = withAnswer(rows, 'new-host', 'yes', NOW);

  assert.equal(next.length, 100, `the list grew past 100 rows (${next.length})`);
  assert.ok(!next.some((row) => row.host === 'oldest'), 'the oldest answer was kept');
  assert.ok(next.some((row) => row.host === 'new-host' && row.answer === 'yes' && row.askedUtc === NOW), 'the new answer is not there');
  assert.equal(consentOf(next, 'oldest'), 'ask', 'the dropped host is not asked again');
  assert.equal(JSON.stringify(rows), before, 'the input array was changed');
  assert.equal(withAnswer(rows, 'h5', 'no', NOW).length, 100, 'a changed answer of a known host dropped a row');
  assert.ok(withAnswer(rows, 'h5', 'no', NOW).some((row) => row.host === 'oldest'), 'a changed answer of a known host dropped the oldest');
});

test('S3-T2b: the panel lists every remembered host, and Forget removes one row', () => {
  const list = withAnswer(withAnswer([], 'b-host', 'no', NOW), 'a-host', 'yes', NOW);
  assert.deepEqual(answersOf(list).map((row) => row.host), ['a-host', 'b-host'], 'the hosts are not listed by name');
  assert.deepEqual(answersOf('junk'), [], 'a damaged list was listed');

  const without = withoutHost(list, 'b-host');
  assert.equal(consentOf(without, 'b-host'), 'ask', 'a forgotten host was still remembered');
  assert.equal(consentOf(without, 'a-host'), 'yes', 'Forget removed another host\'s row');
  assert.equal(list.length, 2, 'the input array was changed');
  assert.equal(withoutHost(list, 'nobody').length, 2, 'Forget of an unknown host removed a row');
  assert.deepEqual(withoutHost('junk', 'a-host'), [], 'a damaged list was carried through Forget');
});

test('S3-T3: no ssh before a "yes"', async () => {
  const notThis = world({ list: [], answer: 'no' });
  const off = await remoteRoad(notThis.seams, NAME, 'question');
  assert.equal(refusal(off), `Reading the Claude session on “${H}” is turned off. Only the text you copied can be sent; turn it on in CoAI: hosts read over ssh.`);
  assert.equal(notThis.w.runs.length, 0, 'ssh was spawned for a host nobody agreed to');
  assert.equal(consentOf(notThis.w.list, H), 'no', 'the "no" was not written');
  assert.deepEqual(notThis.w.asks, [H]);

  const escaped = world({ list: [], answer: undefined });
  const nothing = await remoteRoad(escaped.seams, NAME, 'question');
  assert.equal(nothing.kind, 'refused');
  assert.equal(escaped.w.runs.length, 0, 'ssh was spawned for a host nobody agreed to');
  assert.equal(escaped.w.writes.length, 0, 'Escape wrote an answer');
  assert.equal(consentOf(escaped.w.list, H), 'ask');

  const remembered = world({ list: withAnswer([], H, 'no', NOW), answer: 'yes' });
  const again = await remoteRoad(remembered.seams, NAME, 'question');
  assert.equal(remembered.w.runs.length, 0, 'ssh was spawned for a host nobody agreed to');
  assert.equal(remembered.w.asks.length, 0, 'a remembered "no" asked again');
  assert.equal(refusal(again), refusal(off), 'a remembered "no" gave another sentence');

  const yes = world({ list: [], answer: 'yes' });
  const read = await remoteRoad(yes.seams, NAME, 'question');
  assert.equal(read.kind, 'lines');
  assert.equal(consentOf(yes.w.list, H), 'yes', 'the "yes" was not written');
  assert.deepEqual(yes.w.writes.map((rows) => rows.length), [1]);
  assert.equal(yes.w.runs.length, 1);
  assert.ok(yes.w.progress >= 1, 'the read was not under the progress line');

  const known = world({ list: yesFor(H), answer: 'no' });
  await remoteRoad(known.seams, NAME, 'question');
  assert.equal(known.w.asks.length, 0, 'a "yes" host was asked again');
});

test('S3-T3b: a name that is not a creds name is refused before any consent or ssh', async () => {
  const other = world({ list: [] });
  assert.equal(refusal(await remoteRoad(other.seams, 'bash', 'question')), 'This is not a creds terminal, so it has no Claude session to read.');
  const bad = world({ list: [] });
  const malformed = refusal(await remoteRoad(bad.seams, 'Creds: -oProxyCommand=x / s', 'question'));
  assert.match(malformed, /not a name ssh can be given safely/u);
  for (const one of [other, bad]) {
    assert.equal(one.w.asks.length + one.w.runs.length + one.w.writes.length, 0, 'a name that is not a session asked, wrote or spawned');
  }
});

test('S3-T4: the registry path is ONE call', async () => {
  const one = world({ script: () => answered(registryFrame([said('first'), said('second')])) });
  const got = await remoteRoad(one.seams, NAME, 'prompts');
  assert.equal(one.w.runs.length, 1, 'the registry path made more than one call');
  assert.deepEqual(one.w.runs[0], [S]);
  assert.deepEqual(linesOf(got), [said('first'), said('second')]);
});

const FALLBACK_FOLDERS = [KB_FOLDER, '-home-git-a', 'C--Users-x'];

test('S3-T5: the fallback is at most three calls and picks only one exact title', async () => {
  const titlesCall: RemoteCall = { kind: 'titles', session: S, folders: [KB_FOLDER] };
  const idCall: RemoteCall = { kind: 'id', session: S, id: ID2 };
  const one = world({ script: sequence(
    answered(fallbackFrame(KB, FALLBACK_FOLDERS)),
    answered(titlesFrame([{ id: ID, folder: KB_FOLDER, title: 'other title' }, { id: ID2, folder: KB_FOLDER, title: S }])),
    answered(idFrame(ID2, KB_FOLDER)),
  ) });
  const got = await remoteRoad(one.seams, NAME, 'prompts');
  assert.deepEqual(one.w.runs, [[S], id(titlesCall), id(idCall)], 'the three calls are not registry, titles of the folders in common, then the id');
  assert.deepEqual(linesOf(got), [said('picked')]);
  assert.equal(one.w.picks.length, 0, 'a single exact title was put to a vote');

  const two = world({ script: sequence(
    answered(fallbackFrame(KB, FALLBACK_FOLDERS)),
    answered(titlesFrame([{ id: ID, folder: KB_FOLDER, title: S, mtime: 100 }, { id: ID2, folder: KB_FOLDER, title: S, mtime: 200 }])),
    answered(idFrame(ID, KB_FOLDER)),
  ), choose: (rows) => rows.find((row) => row.sessionId === ID)?.sessionId });
  await remoteRoad(two.seams, NAME, 'prompts');
  assert.equal(two.w.picks.length, 1, 'two sessions of one name and one was taken');
  assert.equal(two.w.runs.length, 3);
  assert.deepEqual(two.w.runs[2], [S, 'id', ID], 'the call after the pick is not for the id that was chosen');

  const stuck = world({ script: sequence(
    answered(fallbackFrame(KB, FALLBACK_FOLDERS)),
    answered(titlesFrame([{ id: ID, folder: KB_FOLDER, title: S }, { id: ID2, folder: KB_FOLDER, title: S }])),
  ), choose: () => undefined });
  await remoteRoad(stuck.seams, NAME, 'prompts');
  assert.equal(stuck.w.runs.length, 2, 'a third call was made before the choice');

  const none = (scan: string, skipped = false): World => world({ script: sequence(
    answered(fallbackFrame(KB, FALLBACK_FOLDERS)),
    answered(titlesFrame([{ id: ID, folder: KB_FOLDER, title: 'not it' }], scan, skipped)),
  ) });
  const all = await remoteRoad(none('all').seams, NAME, 'prompts');
  assert.ok(refusal(all).includes(H) && refusal(all).includes(S), 'the refusal names neither the host nor the session');
  assert.ok(!/not every session/iu.test(refusal(all)), 'a scan that read everything said it did not');
  for (const [why, skipped] of [['count', false], ['time', false], ['all', true]] as const) {
    const cut = await remoteRoad(none(why, skipped).seams, NAME, 'prompts');
    assert.match(refusal(cut), /not every session/iu, `a cut title scan was reported as no session (${why}, skipped ${skipped})`);
  }
});

test('S3-T5b: the folders of the fallback are the pane\'s own, in order, at most three; none is a refusal that says what was not listed', async () => {
  const wanted = projectDirNames(KB);
  assert.ok(wanted.length >= 2, 'the fixture path has several spellings');
  const both = world({ script: sequence(answered(fallbackFrame(KB, ['-z-other', ...[...wanted].reverse(), '-home-git-a'])), refusedRun('stop')) });
  await remoteRoad(both.seams, NAME, 'prompts');
  assert.deepEqual(both.w.runs[1], [S, 'titles', ...wanted], 'the folders were not the pane\'s own, in projectDirNames order');

  const miss = (skipped: number, more: boolean): World => world({ script: sequence(answered(fallbackFrame(KB, ['-z-other'], skipped, more))) });
  const none = miss(0, false);
  const plain = refusal(await remoteRoad(none.seams, NAME, 'prompts'));
  assert.equal(none.w.runs.length, 1, 'a call was made for a folder the VM does not have');
  assert.ok(plain.includes(H) && plain.includes(S) && plain.includes(KB), 'the refusal does not name host, session and folder');
  assert.ok(!/could not be listed/u.test(plain));
  assert.match(refusal(await remoteRoad(miss(2, false).seams, NAME, 'prompts')), /could not be listed/u);
  assert.match(refusal(await remoteRoad(miss(0, true).seams, NAME, 'prompts')), /could not be listed/u);
});

test('S3-T6: an ambiguity is a list, even of one', async () => {
  const two = world({ script: sequence(answered(twoPanes()), answered(idFrame(ID2, '-home-git-b'))), choose: (rows) => rows[1]?.sessionId });
  const got = await remoteRoad(two.seams, NAME, 'prompts');
  assert.equal(two.w.picks.length, 1, 'a two-pane session was read without a choice');
  assert.deepEqual(two.w.picks[0]?.map((row) => row.sessionId).sort(), [ID, ID2].sort());
  assert.deepEqual(two.w.runs[1], [S, 'id', ID2]);
  assert.equal(got.kind, 'lines');

  const lone = world({ script: sequence(answered(onePane()), answered(idFrame(ID, '-home-git-a'))), choose: (rows) => rows[0]?.sessionId });
  await remoteRoad(lone.seams, NAME, 'prompts');
  assert.equal(lone.w.picks.length, 1, 'a two-pane session was read without a choice');
  assert.equal(lone.w.picks[0]?.length, 1);

  const nobody = world({ script: sequence(answered(frame('COAI-PANE 0.0 %1 1000', 'COAI-PANE 0.1 %2 1001'))) });
  const none = await remoteRoad(nobody.seams, NAME, 'prompts');
  assert.ok(refusal(none).includes(H) && refusal(none).includes(S));
  assert.equal(nobody.w.picks.length, 0, 'a list of nothing was shown');

  const cancelled = world({ script: sequence(answered(twoPanes())), choose: () => undefined });
  const no = await remoteRoad(cancelled.seams, NAME, 'prompts');
  assert.equal(refusal(no), 'No session was chosen, so nothing was read.');
  assert.equal(cancelled.w.runs.length, 1, 'a cancelled pick still spawned');
  assert.equal(cancelled.w.kept.length, 0, 'a cancelled pick kept or dropped an id');
});

test('S3-T6b: a pick list is newest first', async () => {
  const sessions = [{ id: ID, folder: KB_FOLDER, title: S, mtime: 100 }, { id: ID2, folder: KB_FOLDER, title: S, mtime: 300 }, { id: ID3, folder: KB_FOLDER, title: S, mtime: 200 }];
  const one = world({ script: sequence(answered(fallbackFrame(KB, FALLBACK_FOLDERS)), answered(titlesFrame(sessions))), choose: () => undefined });
  await remoteRoad(one.seams, NAME, 'prompts');
  assert.deepEqual(one.w.picks[0]?.map((row) => row.sessionId), [ID2, ID3, ID], 'the pick list is not newest first');
});

test('S3-T7: a picked id is sent, and kept or dropped by the parent\'s rule only', async () => {
  const picked: Picked = { id: ID2, folders: ['-home-git-b'], host: H, session: S };

  // A pick from an ambiguity sets it; the call that follows asks for it.
  const first = world({ script: sequence(answered(twoPanes()), refusedRun('“h” did not answer over ssh within 15 s.')), choose: (rows) => rows.find((row) => row.sessionId === ID2)?.sessionId });
  const slow = await remoteRoad(first.seams, NAME, 'prompts');
  assert.equal(slow.kind, 'refused');
  assert.deepEqual(first.w.kept, [{ id: ID2, folders: projectDirNames('/home/git/b'), host: H, session: S }], 'a slow host made the person choose again');
  assert.deepEqual(first.w.runs[1], [S, 'id', ID2]);

  // The next press sends it, on its FIRST call.
  const next = world({ script: () => answered(idFrame(ID2, '-home-git-b')) });
  const got = await remoteRoad(next.seams, NAME, 'prompts', picked);
  assert.deepEqual(next.w.runs, [[S, 'id', ID2]], 'the next press did not send the picked id');
  assert.equal(got.kind, 'lines');
  assert.deepEqual(next.w.kept, [], 'a good read by id dropped it');

  // Kept through every failure that is not the file's.
  const keepers: ReadonlyArray<readonly [string, Script, Options['choose']?]> = [
    ['an ssh refusal', () => refusedRun('ssh could not read from “h”: Permission denied.')],
    ['a timeout', () => refusedRun('“ai1-dev2” did not answer over ssh within 15 s.')],
    ['a cap', () => refusedRun('“ai1-dev2” sent more than 8 MiB, so the read was stopped.')],
    ['an unreadable answer', () => answered('Welcome\nnothing else\n')],
    ['a folder refusal', () => answered(idFrame(ID2, '-home-git-elsewhere'))],
    ['a cancel', () => answered(twoPanes()), () => undefined],
  ];
  for (const [what, script, choose] of keepers) {
    const one = world({ script, ...(choose === undefined ? {} : { choose }) });
    const answer = await remoteRoad(one.seams, NAME, 'prompts', picked);
    assert.equal(answer.kind, 'refused', what);
    assert.ok(!one.w.kept.includes(undefined), `a slow host made the person choose again: ${what} dropped the picked id`);
  }

  // Dropped by a registry success and by `gone`.
  const registry = world({ script: () => answered(registryFrame()) });
  await remoteRoad(registry.seams, NAME, 'prompts', picked);
  assert.deepEqual(registry.w.kept, [undefined], 'a registry success did not drop the picked id');
  const gone = world({ script: () => answered(goneFrame(ID2)) });
  await remoteRoad(gone.seams, NAME, 'prompts', picked);
  assert.deepEqual(gone.w.kept, [undefined], 'a vanished file did not drop the picked id');
});

test('S3-T16: a picked id is not sent to another host or session after a rename', async () => {
  const picked: Picked = { id: ID2, folders: ['-home-git-b'], host: H, session: S };
  for (const renamed of ['Creds: h2 / s', `Creds: ${H} / s2`]) {
    const one = world({ list: [...yesFor('h2'), ...yesFor(H)], live: () => renamed, script: () => answered(registryFrame()) });
    const got = await remoteRoad(one.seams, renamed, 'prompts', picked);
    assert.equal(got.kind, 'lines');
    assert.notDeepEqual(one.w.runs[0]?.slice(1), ['id', ID2], `a picked id of another host was sent to this one (${renamed})`);
    assert.equal(one.w.runs[0]?.length, 1, `the first call of a renamed terminal is not the registry path (${renamed})`);
    assert.ok(!one.w.kept.includes(undefined), 'a registry success of ANOTHER host dropped the id that was picked for this one');
  }
  const same = world({ script: () => answered(idFrame(ID2, '-home-git-b')) });
  await remoteRoad(same.seams, NAME, 'prompts', picked);
  assert.deepEqual(same.w.runs[0], [S, 'id', ID2], 'the id of the same host and session was not sent');
});

test('S3-T8: a picked file gone between the pick and the next press', async () => {
  let current: Picked | undefined;
  const press = async (script: Script, choose?: Options['choose']) => {
    const one = world({ script, ...(choose === undefined ? {} : { choose }) });
    const seams: RoadSeams = { ...one.seams, keep: (picked) => { current = picked; } };
    const answer = await remoteRoad(seams, NAME, 'prompts', current);

    return { answer, runs: one.w.runs };
  };

  const first = await press(sequence(answered(twoPanes()), refusedRun('“h” did not answer over ssh within 15 s.')), (rows) => rows.find((row) => row.sessionId === ID2)?.sessionId);
  assert.equal(first.answer.kind, 'refused');
  assert.equal(current?.id, ID2, 'press 1 did not keep the id it picked');

  const second = await press(() => answered(goneFrame(ID2)));
  const sentence = refusal(second.answer);
  assert.ok(sentence.includes(ID2) && sentence.includes(H), 'a vanished pick was reported as no session');
  assert.equal(sentence, `The session you chose for this terminal (${ID2}) is no longer on “${H}”. Press again to look for it.`);
  assert.equal(second.runs.length, 1, 'press 2 went on to the fallback');
  assert.equal(current, undefined, 'press 3 asked for a file that is gone');

  const third = await press(sequence(
    answered(fallbackFrame(KB, FALLBACK_FOLDERS)),
    answered(titlesFrame([{ id: ID, folder: KB_FOLDER, title: S }])),
    answered(idFrame(ID, KB_FOLDER)),
  ));
  assert.deepEqual(third.runs[0], [S], 'press 3 asked for a file that is gone');
  assert.equal(third.runs.length, 3);
  assert.equal(third.answer.kind, 'lines');
});

test('S3-T9: the reach takes the host from a LIVE terminal', () => {
  const key = {};
  const other = {};
  const another = {};
  const live = (...rows: readonly [object, string, boolean?][]) => rows.map(([one, name, exited]) => ({ key: one, name, exited: exited ?? false }));

  const restored = reachOf(live([other, NAME], [another, 'bash']), undefined, NAME);
  assert.deepEqual(restored, { kind: 'terminal', key: other, name: NAME });

  const closed = reachOf(live([another, 'bash']), undefined, NAME);
  assert.equal(closed.kind, 'refused', 'a closed terminal\'s VM was read');
  assert.equal(refusal(closed), `The terminal “${NAME}” is not open — open it to read its Claude session.`);

  const exited = reachOf(live([other, NAME, true]), undefined, NAME);
  assert.equal(exited.kind, 'refused', 'a closed terminal\'s VM was read');

  const several = reachOf(live([other, NAME], [another, NAME]), undefined, NAME);
  assert.equal(refusal(several), `2 terminals are called “${NAME}” — open the one you mean and press again.`);

  const renamed = reachOf(live([key, 'Creds: h2 / s2'], [other, NAME]), key, NAME);
  assert.deepEqual(renamed, { kind: 'terminal', key, name: 'Creds: h2 / s2' }, 'the recorded name was used instead of the live one');

  const keyedGone = reachOf(live([key, 'bash', true], [other, NAME]), key, NAME);
  assert.deepEqual(keyedGone, { kind: 'terminal', key: other, name: NAME }, 'an exited keyed terminal was kept');
});

function foundOf(lines: readonly string[], cut: boolean): Extract<RemoteRead, { kind: 'found' }> {
  return { kind: 'found', via: 'registry', sessionId: ID, cwd: KB, name: 'kb', folder: KB_FOLDER, lines, cut };
}

test('S3-T10: a cut is decided once, by the purpose', () => {
  const truncated = `No question was found in the last 4 MiB of the session on “${H}” that were read; an older one may be beyond the cut.`;
  const cutNo = answerFrom(foundOf([said('x'), ai('t')], true), 'question', H);
  assert.deepEqual(cutNo, { kind: 'refused', refusal: truncated }, 'a cut tail was reported as nothing asked');

  const cutYes = answerFrom(foundOf([said('x'), asks('q1')], true), 'question', H);
  assert.deepEqual(cutYes, { kind: 'lines', lines: [said('x'), asks('q1')], note: '' });

  const wholeNo = answerFrom(foundOf([said('x')], false), 'question', H);
  assert.deepEqual(wholeNo, { kind: 'lines', lines: [said('x')], note: '' }, 'a whole tail with no question was refused as cut');

  const strange = JSON.stringify({ type: 'assistant', message: { content: [{ type: 'tool_use', id: 'a', name: 'AskUserQuestion', input: { prompts: [] } }] } });
  assert.equal(answerFrom(foundOf([strange], true), 'question', H).kind, 'lines', 'a question this build could not read was called "cut"');

  const note = 'Only the last 4 MiB of this session were read, so its first prompts are not shown.';
  assert.deepEqual(answerFrom(foundOf([said('x')], true), 'prompts', H), { kind: 'lines', lines: [said('x')], note });
  assert.deepEqual(answerFrom(foundOf([said('x')], false), 'prompts', H), { kind: 'lines', lines: [said('x')], note: '' });
});

test('S3-T10b: the cut is read in remoteRead.ts only — no module that takes the road\'s answer reads it', () => {
  const dir = path.join(__dirname, '..', '..', 'src');
  const files = fs.readdirSync(dir).filter((name) => name.endsWith('.ts'));
  const talksToTheRoad = (text: string): boolean => /from '\.\/(remoteFrame|remoteRead|remoteSessionHost)'/u.test(text);
  // `waitingSaid.ts` is named, not found: the words of the doors must never read the cut, whatever they import (S4, the S3 review).
  const named = ['waitingSaid.ts'];
  const readers = files.filter((name) => named.includes(name) || talksToTheRoad(fs.readFileSync(path.join(dir, name), 'utf8')));
  for (const wanted of ['remoteRead.ts', 'remoteSessionHost.ts', ...named]) {
    assert.ok(readers.includes(wanted), `the scan did not find ${wanted}, so it proves nothing`);
  }
  assert.ok(/\.cut\b/u.test(fs.readFileSync(path.join(dir, 'remoteRead.ts'), 'utf8')), 'remoteRead.ts does not read the cut, so the one place is not the one place');
  const stray = readers.filter((name) => name !== 'remoteRead.ts')
    .filter((name) => /\bcut\b/u.test(fs.readFileSync(path.join(dir, name), 'utf8')));
  assert.deepEqual(stray, [], 'a cut tail was reported as nothing asked: a module other than remoteRead.ts reads `cut`');
});

test('S4-T9: the context key follows the name and the answer', () => {
  const ask: unknown = [];
  const no = withAnswer([], H, 'no', NOW);
  const yes = withAnswer([], H, 'yes', NOW);

  assert.equal(readsKey(NAME, ask), true, 'a Creds terminal with no answer yet hides the items');
  assert.equal(readsKey(NAME, yes), true, 'a host that said yes hides the items');
  assert.equal(readsKey(NAME, no), false, 'a host that said no still shows the items');
  assert.equal(readsKey('Creds: before x', ask), true, 'a bad creds name hides the items that explain it');
  assert.equal(readsKey('Creds: h / s.x', no), true, 'a bad creds name was judged by an answer of some host');
  for (const other of ['bash', 'pwsh', 'Creds run: x', 'Creds VPN: x', 'creds: h / s', '']) {
    assert.equal(readsKey(other, yes), false, `the terminal “${other}” shows the items`);
  }
  assert.equal(readsKey(NAME, 'junk'), true, 'a damaged answer list hid the items instead of asking again');
  assert.equal(readsKey(`Creds: other-host / ${S}`, no), true, 'the answer of one host was applied to another');
});

test('S4-T5b: what Asked shows for a terminal is the person\'s words of the tail, earliest first, with the same rules as the local file', async () => {
  const lines = [said('first'), ai('t'), asks('q1'), said('second'), '{broken half line', said('third')];
  assert.deepEqual(promptsSaid({ kind: 'lines', lines, note: '' }), { asked: ['first', 'second', 'third'], refusal: '' });

  const NOTE = 'Only the last 4 MiB of this session were read, so its first prompts are not shown.';
  assert.deepEqual(promptsSaid({ kind: 'lines', lines, note: NOTE }), { asked: ['first', 'second', 'third'], refusal: NOTE }, 'the cut note was not carried beside the prompts');
  assert.deepEqual(promptsSaid({ kind: 'lines', lines: [ai('t')], note: '' }), { asked: [], refusal: NOTHING_OF_YOURS }, 'a tail without a prompt was not the local sentence');
  assert.deepEqual(promptsSaid({ kind: 'lines', lines: [ai('t')], note: NOTE }), { asked: [], refusal: NOTE }, 'a part of a session without a prompt claimed nothing was ever written');
  assert.deepEqual(promptsSaid({ kind: 'refused', refusal: 'ssh said no' }), { asked: [], refusal: 'ssh said no' }, 'the road\'s refusal was changed');

  const long = said('x'.repeat(MOST_PER_PROMPT + 10));
  const cut = promptsSaid({ kind: 'lines', lines: [long], note: '' }).asked[0] ?? '';
  assert.ok(cut.length < MOST_PER_PROMPT + 100 && cut.includes('(cut here'), 'a long prompt was not cut as the local reader cuts it');
  const many = Array.from({ length: MOST_PROMPTS + 5 }, (_, index) => said(`p${index}`));
  const kept = promptsSaid({ kind: 'lines', lines: many, note: '' }).asked;
  assert.equal(kept.length, MOST_PROMPTS, 'more prompts than the local reader shows were handed over');
  assert.equal(kept[0], 'p0', 'the earliest prompts are not the ones kept');
});

/** The run folder this test run owns (`inRunFolder.cjs` has checked it is set): the one place a test file may be written. */
function runFolder(): string {
  const folder = process.env['COAI_TEST_RUN_FOLDER'];
  assert.ok(folder !== undefined && folder.length > 0, 'the run folder is not set');

  return folder;
}

test('S4-T5b: the same lines give the same prompts from a REAL file and from the road', async () => {
  const lines = [said('alpha'), ai('t'), said('b'.repeat(MOST_PER_PROMPT + 5)), asks('q1'), said('gamma')];
  const file = path.join(runFolder(), `prompts-${randomUUID()}.jsonl`);
  fs.writeFileSync(file, `${lines.join('\n')}\n`);
  try {
    const local = await promptsFrom(file);
    const road = promptsSaid({ kind: 'lines', lines, note: '' }).asked;

    assert.equal(local.kind, 'said', 'the file was not read as a session with prompts, so the comparison below proves nothing');
    assert.equal(road.length, 3, 'the fixture does not hold the three prompts this test is about');
    assert.deepEqual(road, local.kind === 'said' ? local.said : [], 'the road and the file read one session into different prompts');
  } finally {
    fs.rmSync(file, { force: true });
  }
});

test('S4-T5b: the local file reader and the road\'s lines make a prompt by ONE function, not two copies', () => {
  // No file here on purpose: a real file would need a temp folder, and the rule that makes the prompts the same is that both readers
  // call promptIn. A scan, with its companion that it still finds the local reader and the one over lines.
  const text = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'claudeSessions.ts'), 'utf8');
  const local = /async function promptsOf\([\s\S]*?\n\}\n/u.exec(text)?.[0] ?? '';
  const lines = /export function promptsIn\([\s\S]*?\n\}\n/u.exec(fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'sessionPrompts.ts'), 'utf8'))?.[0] ?? '';

  assert.ok(local.includes('eachLine(') && lines.includes('for ('), 'the scan no longer finds the two readers, so it proves nothing');
  assert.ok(local.includes('promptIn('), 'the file reader makes a prompt by its own code: the road and the file can disagree');
  assert.ok(lines.includes('promptIn') && !lines.includes('humanSaid('), 'the lines reader makes a prompt by its own code: the road and the file can disagree');
  assert.ok(!local.includes('MOST_PER_PROMPT'), 'the file reader cuts a prompt itself, beside the shared function');
});

test('S3-T11: the host road says five different things', async () => {
  const road = async (script: Script): Promise<string> => refusal(await remoteRoad(world({ script }).seams, NAME, 'question'));
  const absent = await road(() => answered(frame('COAI-NO-SESSION')));
  const unreadable = await road(() => answered('Welcome\nno frame here\n'));
  const ambiguous = await road(() => answered(frame('COAI-PANE 0.0 %1 1000', 'COAI-PANE 0.1 %2 1001')));
  const truncated = await road(() => answered(cutFrame([said('x')])));
  const ssh = await road(() => refusedRun(`ssh could not read from “${H}”: Permission denied (publickey).`));

  assert.notEqual(unreadable, absent, 'an unreadable session was reported as no session');
  const all = [absent, unreadable, ambiguous, truncated, ssh];
  assert.equal(new Set(all).size, 5, 'two different failures gave one sentence');
  assert.ok(all.every((one) => one.includes(H)), `a sentence does not name the host: ${all.join(' | ')}`);
  assert.match(truncated, /last 4 MiB/u, 'the cut sentence is not the truncated one');

  // The unreadable sentence carries the clause of ITS fault, and a missing session file is not a missing tmux session.
  assert.ok((await road(() => ({ kind: 'answered', code: 3, stdout: '' }))).includes(UNREADABLE_SAID.exit), 'the clause of an exit code is not in the sentence');
  assert.ok((await road(() => answered(frame('COAI-BAD tool')))).includes(UNREADABLE_SAID.tool), 'the clause of a missing tool is not in the sentence');
  const noFile = await road(() => answered(frame('COAI-PANE 0.0 %1 1000', 'COAI-CLAUDE 0.0 2000 5', 'COAI-REGISTRY 2000 ok', ...data(reg({ procStart: undefined })), `COAI-TRANSCRIPT ${ID} 0`)));
  assert.notEqual(noFile, absent, 'a missing session file was reported as no tmux session');
  assert.ok(noFile.includes(H) && noFile.includes(S));
});

test('S3-T12: a double press starts one child and one question', async () => {
  const latch = onePerTerminal<object>();
  const one = world({ list: [], answer: 'yes', slowAsk: true });
  const terminal = {};
  const road = (key: object): Promise<RemoteAnswer> => latch(key, () => remoteRoad(one.seams, NAME, 'question'));

  const first = road(terminal);
  const second = road(terminal);
  await Promise.resolve();
  one.w.releases.forEach((release) => { release(); });
  const [a, b] = await Promise.all([first, second]);

  assert.equal(one.w.asks.length, 1, 'a double press asked twice and spawned two ssh');
  assert.equal(one.w.runs.length, 1, 'a double press asked twice and spawned two ssh');
  assert.equal(a.kind, 'lines');
  assert.equal(refusal(b), 'The Claude session of this terminal is still being read — the second press was ignored.');

  const again = await road(terminal);
  assert.equal(again.kind, 'lines', 'the latch stayed shut after the first read ended');

  const both = world({ answer: 'yes' });
  const mine = onePerTerminal<object>();
  const answers = await Promise.all([{}, {}].map((key) => mine(key, () => remoteRoad(both.seams, NAME, 'question'))));
  assert.deepEqual(answers.map((one2) => one2.kind), ['lines', 'lines']);
  assert.equal(both.w.runs.length, 2, 'two terminals at once did not each read');

  const failing = onePerTerminal<object>();
  const key = {};
  await assert.rejects(() => failing(key, async () => { throw new Error('seam failed'); }), /seam failed/u);
  assert.equal((await failing(key, async () => ({ kind: 'lines', lines: [], note: '' }))).kind, 'lines', 'a failed read left the latch shut');
});

test('S3-T13: the terminal is re-checked just before EACH spawn', async () => {
  for (const [what, live] of [['a renamed host', 'Creds: h2 / s'], ['a renamed session', `Creds: ${H} / s2`], ['a closed terminal', undefined], ['a name that is not a creds name', 'bash']] as const) {
    const one = world({ list: [], answer: 'yes', live: () => live });
    const answer = await remoteRoad(one.seams, NAME, 'question');
    assert.equal(one.w.runs.length, 0, `ssh was called for a terminal whose name changed (${what})`);
    assert.equal(refusal(answer), 'The terminal’s name changed, or it was closed, so nothing was read. Press again.', what);
  }

  const between = world({ script: sequence(answered(fallbackFrame(KB, FALLBACK_FOLDERS))), live: (reads) => (reads < 1 ? NAME : 'Creds: h2 / s') });
  const stopped = await remoteRoad(between.seams, NAME, 'question');
  assert.equal(between.w.runs.length, 1, 'ssh was called for a terminal whose name changed');
  assert.equal(refusal(stopped), 'The terminal’s name changed, or it was closed, so nothing was read. Press again.');

  const same = world({ script: sequence(
    answered(fallbackFrame(KB, FALLBACK_FOLDERS)),
    answered(titlesFrame([{ id: ID, folder: KB_FOLDER, title: S }])),
    answered(idFrame(ID, KB_FOLDER)),
  ) });
  assert.equal((await remoteRoad(same.seams, NAME, 'prompts')).kind, 'lines');
  assert.equal(same.w.runs.length, 3);
  assert.equal(same.w.reads, 3, 'the seam was not asked once before each spawn');
});

test('S3-T14: a file found by a picked id is in a folder of that session, or it is refused', async () => {
  const folderSentence = `The session file chosen for this terminal is not in a folder of that session on “${H}”, so it was not read.`;

  // After a title pick of a session in F1: F2 refused with the id KEPT, F1 read.
  const titles = (folder: string): World => world({ script: sequence(
    answered(fallbackFrame(KB, FALLBACK_FOLDERS)),
    answered(titlesFrame([{ id: ID, folder: KB_FOLDER, title: S }])),
    answered(idFrame(ID, folder)),
  ) });
  const wrong = titles('-home-git-a');
  const refusedFolder = await remoteRoad(wrong.seams, NAME, 'prompts');
  assert.equal(refusal(refusedFolder), folderSentence, 'a file in a folder nobody asked for was read as this session');
  assert.deepEqual(wrong.w.kept, [{ id: ID, folders: [KB_FOLDER], host: H, session: S }], 'the folder refusal dropped or lost the id');
  assert.equal((await remoteRoad(titles(KB_FOLDER).seams, NAME, 'prompts')).kind, 'lines');

  // After a pick from an ambiguity list with cwd /home/git/kb: the folders are projectDirNames of that cwd.
  const kb = '/home/git/kb';
  const ambiguous = (folder: string): World => world({ script: sequence(
    answered(frame(...candidate('0', 2000, ID, kb, 'KB'), ...candidate('1', 2001, ID2, '/home/git/b', 'B'))),
    answered(idFrame(ID, folder)),
  ), choose: () => ID });
  const bad = ambiguous('-home-git-elsewhere');
  assert.equal(refusal(await remoteRoad(bad.seams, NAME, 'prompts')), folderSentence, 'a file in a folder nobody asked for was read as this session');
  assert.deepEqual(bad.w.kept, [{ id: ID, folders: projectDirNames(kb), host: H, session: S }]);
  for (const folder of projectDirNames(kb)) {
    assert.equal((await remoteRoad(ambiguous(folder).seams, NAME, 'prompts')).kind, 'lines', `the spelling ${folder} of the folder was refused`);
  }

  // The pure step: a `found` by id with nothing picked is not a session of this terminal either.
  const step = nextStep({ ...foundOf([], false), via: 'id', folder: '-home-git-a' }, undefined, { host: H, session: S });
  assert.equal(step.kind, 'refused', 'a found by id with no pick was read');
});

test('S3-T15: more than three calls are never made in one press', async () => {
  const endless = world({ script: (operands, calls) => (calls > 8 ? refusedRun('a runaway road') : operands.length === 1
    ? answered(fallbackFrame(KB, FALLBACK_FOLDERS))
    : operands[1] === 'titles'
      ? answered(titlesFrame([{ id: ID, folder: KB_FOLDER, title: S }]))
      : answered(frame(...candidate('0', 2000, ID2, '/home/git/a', 'A'), ...candidate('1', 2001, ID3, '/home/git/b', 'B')))), choose: (rows) => rows[0]?.sessionId });
  const answer = await remoteRoad(endless.seams, NAME, 'prompts');
  assert.equal(endless.w.runs.length, 3, 'a fourth call was made');
  assert.equal(answer.kind, 'refused');
  assert.match(refusal(answer), /could not be settled in three reads/u);
  assert.equal(endless.w.picks.length, 0, 'a pick was asked for that could not be followed');
});
