// node --test scripts/lib/caller-transcripts.test.mjs — the caller-transcript harness (E1.S2): what it counts, and that nothing
// it prints can carry text. Every tree is synthetic and lives under the OS temp folder (`testsToDelete-` named, removed in
// `after`); nothing here reads the operator's ~/.claude or any real store. CI runs it on Linux (ci.yml, "Probe script tests"),
// WSL's Node 20 runs it too, so no API newer than Node 20 appears in the .mjs files.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, linkSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, realpathSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { after, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { busiestDay, classifyPath, classifyRound, isSessionId, measure, percentile, safeModel, safeVersion } from './caller-transcripts.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const CLI = join(HERE, '..', 'measure-caller-transcripts.mjs');
const EXPORTER = join(HERE, '..', 'export-code-rounds.py');
const base = mkdtempSync(join(realpathSync.native(tmpdir()), 'testsToDelete-caller-transcripts-'));
after(() => rmSync(base, { recursive: true, force: true }));

const SESSION = '11111111-1111-4111-8111-111111111111';
const OTHER_SESSION = '22222222-2222-4222-8222-222222222222';
const MARK = (n) => `ZZ-MARK-${n}`;
let counter = 0;

/** A fresh empty root with a `projects` folder inside. */
function fresh() {
  counter += 1;
  const root = join(base, `case-${counter}`);
  mkdirSync(join(root, 'projects'), { recursive: true });
  return { root, projects: join(root, 'projects') };
}
function put(path, text) {
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, text);
}
const jsonl = (...lines) => `${lines.map((entry) => (typeof entry === 'string' ? entry : JSON.stringify(entry))).join('\n')}\n`;
const usage = (output, input = 10) => ({ input_tokens: input, output_tokens: output, cache_creation_input_tokens: 3, cache_read_input_tokens: 4 });
const assistant = ({ id, at, out = 5, content = [{ type: 'text', text: 'hello' }], ...over }) => ({
  type: 'assistant', timestamp: at, version: '2.1.289', cwd: '/work/repo', effort: 'medium', requestedModel: 'claude-sonnet-5-5',
  message: { id, model: 'claude-sonnet-5-5', role: 'assistant', content, usage: usage(out) }, ...over,
});
const round = (caller, startedUtc, vendor = 'claude-code') => ({ session_id: 's', number: 1, started_utc: startedUtc, caller, caller_vendor: vendor });
const run = (rounds, projects, options = {}) => measure({ rounds, projectsDir: projects, largest: 1, runs: 2, ...options });

test('aMessageWrittenAsThreeLinesIsOneId', () => {
  const { projects } = fresh();
  put(join(projects, 'slug', `${SESSION}.jsonl`), jsonl(
    assistant({ id: 'msg_1', at: '2026-10-01T10:00:00Z' }), assistant({ id: 'msg_1', at: '2026-10-01T10:00:01Z' }), assistant({ id: 'msg_1', at: '2026-10-01T10:00:02Z' })));
  const { messages } = run([round(SESSION, '2026-10-01T09:00:00Z')], projects);
  assert.equal(messages.ids, 1);
  assert.equal(messages.lines, 3);
  assert.equal(messages.idsWithSeveralLines, 1);
  assert.equal(messages.differing, 0);
});

test('aMessageWhoseUsageGrowsReportsWhichLineHoldsTheMaximum', () => {
  const { projects } = fresh();
  put(join(projects, 'slug', `${SESSION}.jsonl`), jsonl(
    assistant({ id: 'msg_1', at: '2026-10-01T10:00:00Z', out: 5 }), assistant({ id: 'msg_1', at: '2026-10-01T10:00:01Z', out: 9 }), assistant({ id: 'msg_1', at: '2026-10-01T10:00:02Z', out: 20 }),
    // a second message whose LAST line is not the largest
    assistant({ id: 'msg_2', at: '2026-10-01T10:00:03Z', out: 30 }), assistant({ id: 'msg_2', at: '2026-10-01T10:00:04Z', out: 7 })));
  const { messages } = run([round(SESSION, '2026-10-01T09:00:00Z')], projects);
  assert.equal(messages.differing, 2);
  assert.equal(messages.lastHoldsMax, 1);
  assert.equal(messages.lastLacksMax, 1);
  assert.deepEqual(messages.differingByTerm, { output_tokens: 2 });
});

test('theSpanOfAMessageIsTheTimeBetweenItsFirstAndLastLine', () => {
  const { projects } = fresh();
  put(join(projects, 'slug', `${SESSION}.jsonl`), jsonl(
    assistant({ id: 'msg_1', at: '2026-10-01T10:00:00Z' }), assistant({ id: 'msg_1', at: '2026-10-01T10:00:02Z' }),
    assistant({ id: 'msg_2', at: '2026-10-01T10:00:03Z' }), assistant({ id: 'msg_2', at: '2026-10-01T12:00:03Z' }),
    assistant({ id: 'msg_3', at: '2026-10-01T10:00:04Z' })));
  const { spanMs } = run([round(SESSION, '2026-10-01T09:00:00Z')], projects).messages;
  assert.deepEqual(spanMs, { ids: 2, p50: 7200000, p99: 7200000, max: 7200000, over1s: 2, over10s: 1, over60s: 1, over1h: 1 }, 'a single-line id has no span and is not counted');
});

test('whenTwoLinesShareATimestampFileOrderDecidesWhichIsLast', () => {
  const { projects } = fresh();
  put(join(projects, 'slug', `${SESSION}.jsonl`), jsonl(
    assistant({ id: 'msg_1', at: '2026-10-01T10:00:00Z', out: 5 }), assistant({ id: 'msg_1', at: '2026-10-01T10:00:00Z', out: 9 })));
  assert.equal(run([round(SESSION, '2026-10-01T09:00:00Z')], projects).messages.lastHoldsMax, 1);
});

test('aRoundWhoseTranscriptEndsBeforeItStartedIsNotCovered', () => {
  const { projects } = fresh();
  put(join(projects, 'slug', `${SESSION}.jsonl`), jsonl(assistant({ id: 'msg_1', at: '2026-10-01T10:00:00Z' })));
  const { coverage } = run([round(SESSION, '2026-10-02T00:00:00Z'), round(SESSION, '2026-10-01T09:30:00Z')], projects);
  assert.equal(coverage.rounds.foundButEndsBefore, 1);
  assert.equal(coverage.rounds.found, 1);
  assert.equal(coverage.rounds.notFound, 0);
  assert.deepEqual(coverage.callers, { distinct: 1, notASessionId: 0, withTranscript: 1, withoutTranscript: 0 });
  assert.equal(coverage.byVendor['claude-code'].rounds, 2);
});

test('aCallerThatIsNotAUuidIsCountedApart', () => {
  const { projects } = fresh();
  put(join(projects, 'slug', `${SESSION}.jsonl`), jsonl(assistant({ id: 'msg_1', at: '2026-10-01T10:00:00Z' })));
  const { coverage } = run([round('codex-abc', '2026-10-01T09:00:00Z', 'codex'), round('', '2026-10-01T09:00:00Z', ''), round(OTHER_SESSION, '2026-10-01T09:00:00Z')], projects);
  assert.equal(coverage.rounds.notASessionId, 2);
  assert.equal(coverage.rounds.notFound, 1, 'only the real UUID with no file is notFound');
  assert.equal(coverage.byVendor['(blank)'].notASessionId, 1);
  assert.equal(coverage.byVendor.codex.notASessionId, 1);
  assert.equal(coverage.callers.notASessionId, 2);
});

test('aUpperCaseCallerFindsItsLowerCaseFile', () => {
  const { projects } = fresh();
  put(join(projects, 'slug', `${SESSION}.jsonl`), jsonl(assistant({ id: 'msg_1', at: '2026-10-01T10:00:00Z' })));
  assert.equal(run([round(SESSION.toUpperCase(), '2026-10-01T09:00:00Z')], projects).coverage.rounds.found, 1);
});

test('aMetaWithoutItsTranscriptAndAnAgentCallWithoutAMetaAreBothCounted', () => {
  const { projects } = fresh();
  const dir = join(projects, 'slug', SESSION);
  const agentCall = (id) => ({ type: 'tool_use', id, name: 'Agent', input: { subagent_type: 'x' } });
  put(join(projects, 'slug', `${SESSION}.jsonl`), jsonl(assistant({ id: 'msg_1', at: '2026-10-01T10:00:00Z', content: [agentCall('toolu_1'), agentCall('toolu_2')] })));
  // agent a1 has a meta and launches a nested agent (toolu_3), so the nested meta matches nothing in the MAIN file
  put(join(dir, 'subagents', 'agent-a1.jsonl'), jsonl(assistant({ id: 'msg_a1', at: '2026-10-01T10:01:00Z', content: [agentCall('toolu_3')], isSidechain: true })));
  put(join(dir, 'subagents', 'agent-a1.meta.json'), JSON.stringify({ toolUseId: 'toolu_1', spawnDepth: 1, requestShape: 'background', model: 'sonnet' }));
  put(join(dir, 'subagents', 'agent-a3.meta.json'), JSON.stringify({ toolUseId: 'toolu_3', spawnDepth: 2, parentAgentId: 'a1' })); // no agent-a3.jsonl
  put(join(dir, 'subagents', 'agent-a2.jsonl'), jsonl(assistant({ id: 'msg_a2', at: '2026-10-01T10:02:00Z', isSidechain: true }))); // no meta
  const { agents, layout, versions } = run([round(SESSION, '2026-10-01T09:00:00Z')], projects);
  assert.equal(agents.agentToolUses, 2);
  assert.equal(agents.metas, 2);
  assert.equal(agents.metasWithoutFile, 1);
  assert.equal(agents.filesWithoutMeta, 1);
  assert.equal(agents.toolUsesWithoutMeta, 1, 'toolu_2 launched nothing that left a meta');
  assert.equal(agents.metasUnmatchedInMain, 1);
  assert.equal(agents.metasUnmatchedAnywhere, 0, 'the nested launch is in an agent file');
  assert.equal(agents.metasWithParent, 1);
  assert.deepEqual([layout.agentFiles, layout.metas, layout.withSubagentsDir], [2, 2, 1]);
  assert.equal(versions['2.1.289'].metasWithModel, 1);
  assert.equal(versions['2.1.289'].metasWithoutModel, 0);
  assert.equal(versions['(none)'].metasWithoutModel, 1, 'a meta whose agent file is missing has no version to be counted under');
});

test('aTornLastLineIsCountedAndSkipped', () => {
  const { projects } = fresh();
  put(join(projects, 'slug', `${SESSION}.jsonl`), `${jsonl(assistant({ id: 'msg_1', at: '2026-10-01T10:00:00Z' }))}{"type":"assistant","timestamp":"2026-10-01T10:0`);
  const report = run([round(SESSION, '2026-10-01T09:00:00Z')], projects);
  assert.equal(report.files.unparseable, 1);
  assert.equal(report.files.unparseableAtEnd, 1);
  assert.equal(report.versions['2.1.289'].assistantLines, 1);
  assert.equal(report.coverage.rounds.found, 1, 'the last PARSEABLE stamp is what coverage reads');
});

test('theVersionsAreCountedApartWithTheirEffortAndKeySets', () => {
  const { projects } = fresh();
  put(join(projects, 'slug', `${SESSION}.jsonl`), jsonl(
    assistant({ id: 'msg_1', at: '2026-10-01T10:00:00Z', version: '2.1.197', effort: undefined }),
    assistant({ id: 'msg_2', at: '2026-10-01T10:00:01Z', effort: 'high', extraField: 'v' }),
    assistant({ id: 'msg_3', at: '2026-10-01T10:00:02Z', effort: 'medium' })));
  const { versions } = run([round(SESSION, '2026-10-01T09:00:00Z')], projects);
  assert.equal(versions['2.1.197'].assistantLines, 1);
  assert.equal(versions['2.1.197'].withEffort, 0);
  assert.equal(versions['2.1.289'].withEffort, 2);
  assert.deepEqual(versions['2.1.289'].effort, { high: 1, medium: 1 });
  assert.ok(versions['2.1.289'].topKeys.includes('extraField'));
  assert.deepEqual(versions['2.1.289'].contentTypes, ['text']);
});

test('writePathsAreClassifiedAsBooleansNeverKept', () => {
  assert.deepEqual(classifyPath('D:\\rsd\\_wt\\x\\src\\A.cs', 'd:/rsd/_wt/x'), { missing: false, absolute: true, separators: 'backslash', drive: 'upper', underCwd: 'yes' });
  assert.deepEqual(classifyPath('d:/rsd/other/B.cs', 'D:\\rsd\\_wt\\x'), { missing: false, absolute: true, separators: 'slash', drive: 'lower', underCwd: 'no' });
  assert.equal(classifyPath('src/a.cs', '/work').absolute, false);
  assert.equal(classifyPath('src/a.cs', '/work').underCwd, 'unknown');
  assert.equal(classifyPath('/work/repository-2/a.cs', '/work/repo').underCwd, 'no', 'a longer sibling name is not under the folder');
  assert.equal(classifyPath('/work/repo/a\\b.cs', '/work/repo').separators, 'mixed');
  assert.equal(classifyPath(undefined, '/work').missing, true);
});

test('theWriteToolsAreCountedByPathFormMainAndAgentApart', () => {
  const { projects } = fresh();
  const dir = join(projects, 'slug', SESSION);
  const write = (name, path) => ({ type: 'tool_use', id: `toolu_${name}`, name, input: { file_path: path } });
  put(join(projects, 'slug', `${SESSION}.jsonl`), jsonl(assistant({ id: 'msg_1', at: '2026-10-01T10:00:00Z', cwd: 'D:\\repo', content: [write('Edit', 'D:\\repo\\a.cs'), write('Write', 'd:/elsewhere/b.cs'), { type: 'tool_use', id: 't', name: 'Bash', input: { command: 'ls' } }] })));
  put(join(dir, 'subagents', 'agent-a1.jsonl'), jsonl(assistant({ id: 'msg_a1', at: '2026-10-01T10:01:00Z', cwd: 'D:\\repo', content: [write('NotebookEdit', 'rel/n.ipynb')] })));
  const { writes } = run([round(SESSION, '2026-10-01T09:00:00Z')], projects);
  assert.deepEqual([writes.main.total, writes.main.absolute, writes.main.underCwd, writes.main.drive], [2, 2, { no: 1, yes: 1 }, { lower: 1, upper: 1 }]);
  assert.deepEqual([writes.agent.total, writes.agent.relative, writes.agent.underCwd], [1, 1, { unknown: 1 }]);
});

test('theFirstLineOfAMainFileIsDescribedByTypeAndPresenceOnly', () => {
  const { projects } = fresh();
  put(join(projects, 'slug', `${SESSION}.jsonl`), jsonl({ type: 'queue-operation', operation: 'enqueue', timestamp: '2026-10-01T09:59:00Z' }, assistant({ id: 'msg_1', at: '2026-10-01T10:00:00Z' })));
  const { firstLine } = run([round(SESSION, '2026-10-01T09:00:00Z')], projects);
  assert.deepEqual(firstLine, { files: 1, withTimestamp: 1, withMessage: 0, types: { 'queue-operation': 1 } });
});

test('aToolResultWithoutIsErrorIsCountedAsAbsent', () => {
  const { projects } = fresh();
  const result = (extra) => ({ type: 'user', timestamp: '2026-10-01T10:00:00Z', version: '2.1.289', message: { role: 'user', content: [{ type: 'tool_result', tool_use_id: 't', content: 'x', ...extra }] } });
  put(join(projects, 'slug', `${SESSION}.jsonl`), jsonl(result({}), result({ is_error: true }), result({ is_error: false })));
  assert.deepEqual(run([round(SESSION, '2026-10-01T09:00:00Z')], projects).toolResults, { total: 3, isErrorTrue: 1, isErrorFalse: 1, isErrorAbsent: 1 });
});

test('aSessionIsMeasuredWhole_AMainFilePlusItsSubagents', () => {
  const { projects } = fresh();
  const main = jsonl(assistant({ id: 'msg_1', at: '2026-10-01T10:00:00Z' }));
  const agent = jsonl(assistant({ id: 'msg_a', at: '2026-10-01T10:01:00Z' }), assistant({ id: 'msg_b', at: '2026-10-02T10:01:00Z' }));
  put(join(projects, 'slug', `${SESSION}.jsonl`), main);
  put(join(projects, 'slug', SESSION, 'subagents', 'agent-a1.jsonl'), agent);
  put(join(projects, 'slug', OTHER_SESSION, 'subagents', 'agent-zz.jsonl'), 'x'); // a folder with no main file is not a session
  put(join(projects, 'slug', 'not-a-session.jsonl'), 'x');
  const report = run([], projects);
  assert.equal(report.sizes.sessions, 1);
  assert.equal(report.sizes.maxBytes, Buffer.byteLength(main) + Buffer.byteLength(agent));
  assert.equal(report.sizes.nonSessionJsonl, 1);
  assert.equal(report.largest[0].bytes, report.sizes.maxBytes);
  assert.equal(report.largest[0].lines, 3);
  assert.equal(report.largest[0].files, 2);
});

test('theGatedPassKeepsTheBusiestDayUnlessADayIsGiven', () => {
  const { projects } = fresh();
  put(join(projects, 'slug', `${SESSION}.jsonl`), jsonl(
    assistant({ id: 'm1', at: '2026-10-01T10:00:00Z' }), assistant({ id: 'm2', at: '2026-10-02T10:00:00Z' }),
    assistant({ id: 'm3', at: '2026-10-02T11:00:00Z' }), assistant({ id: 'm4', at: '2026-10-03T11:00:00Z' })));
  const defaulted = run([], projects).largest[0];
  assert.equal(defaulted.gateDay, '2026-10-02');
  assert.equal(defaulted.gateDayFromOption, false);
  assert.equal(defaulted.linesOnGateDay, 2);
  assert.equal(defaulted.dayGatedParse.linesParsed, 2);
  assert.equal(defaulted.fullParse.linesParsed, 4);
  assert.equal(defaulted.shareOfLinesKept, 0.5);
  const given = run([], projects, { day: '2026-10-03' }).largest[0];
  assert.deepEqual([given.gateDay, given.gateDayFromOption, given.dayGatedParse.linesParsed], ['2026-10-03', true, 1]);
  assert.equal(given.fullParse.runs, 2);
  assert.equal(typeof given.fullParse.p50OfRestMs, 'number');
});

test('aTieBetweenDaysGoesToTheEarliestAndNoDaysIsEmpty', () => {
  assert.equal(busiestDay({ '2026-10-03': 2, '2026-10-01': 2, '2026-10-02': 1 }), '2026-10-01');
  assert.equal(busiestDay({}), '');
});

test('helpersAnswerUnknownAsNullAndRefuseTheWrongShapes', () => {
  assert.equal(percentile([], 0.5), null);
  assert.equal(percentile([5, 1, 3], 0.5), 3);
  assert.equal(isSessionId('11111111-1111-4111-8111-11111111111g'), false);
  assert.equal(isSessionId('..\\..\\secret'), false);
  assert.equal(safeVersion('2.1.289'), '2.1.289');
  assert.equal(safeVersion('see /etc/passwd'), '(other)');
  assert.equal(safeModel('ZZ-MARK-1'), '(other)');
  assert.equal(classifyRound({ caller: SESSION, started_utc: 'not a time' }, [1]), 'startUnreadable');
});

// ---- the guarantee: no field value reaches the report ------------------------------------------------------------------

/** A tree where EVERY free-text slot carries a marker, plus values in the printed slots that fail their shape. */
function markedTree() {
  const { root, projects } = fresh();
  const dir = join(projects, 'slug', SESSION);
  const cwd = `D:\\${MARK(1)}`;
  const bash = { type: 'tool_use', id: 'toolu_b', name: 'Bash', input: { command: `echo ${MARK(2)}`, description: MARK(3) } };
  const edit = { type: 'tool_use', id: 'toolu_e', name: 'Edit', input: { file_path: `${cwd}\\${MARK(4)}\\a.cs`, old_string: MARK(5), new_string: MARK(6) } };
  const agent = { type: 'tool_use', id: 'toolu_a', name: 'Agent', input: { description: MARK(7), prompt: MARK(8), subagent_type: 'general-purpose' } };
  put(join(projects, 'slug', `${SESSION}.jsonl`), jsonl(
    { type: 'queue-operation', operation: 'enqueue', timestamp: '2026-10-01T09:59:00Z', content: MARK(9) },
    { type: 'user', timestamp: '2026-10-01T10:00:00Z', version: '2.1.289', cwd, gitBranch: MARK(10), message: { role: 'user', content: MARK(11) } },
    assistant({ id: 'msg_1', at: '2026-10-01T10:00:01Z', cwd, gitBranch: MARK(12), content: [{ type: 'thinking', thinking: MARK(13), signature: MARK(14) }, bash, edit, agent] }),
    assistant({ id: 'msg_1', at: '2026-10-01T10:00:02Z', out: 9, cwd, content: [{ type: 'text', text: MARK(15) }] }),
    { type: 'user', timestamp: '2026-10-01T10:00:03Z', version: '2.1.289', cwd, toolUseResult: { stdout: MARK(16) }, message: { role: 'user', content: [{ type: 'tool_result', tool_use_id: 'toolu_b', content: MARK(17) }] } },
    // printed slots holding something that is not the shape they promise
    { ...assistant({ id: 'msg_h', at: '2026-10-01T10:00:04Z', version: MARK(18), effort: MARK(19), requestedModel: MARK(20) }), message: { id: 'msg_h', model: MARK(21), content: [], usage: usage(1) } },
    { type: 'summary', timestamp: '2026-10-01T10:00:05Z', summary: MARK(22) }));
  put(join(dir, 'subagents', 'agent-a1.jsonl'), jsonl(
    assistant({ id: 'msg_a1', at: '2026-10-01T10:01:00Z', cwd, isSidechain: true, content: [{ type: 'tool_use', id: 'toolu_w', name: 'Write', input: { file_path: `${cwd}\\${MARK(23)}.cs`, content: MARK(24) } }] }),
    '{"type":"assistant","message":{"id":"torn'));
  put(join(dir, 'subagents', 'agent-a1.meta.json'), JSON.stringify({ agentType: MARK(25), description: MARK(26), toolUseId: 'toolu_a', spawnDepth: 1, requestShape: MARK(27), model: MARK(28) }));
  const rounds = join(root, 'rounds.json');
  put(rounds, JSON.stringify([round(SESSION, '2026-10-01T09:00:00Z', MARK(29)), round(`repo:${MARK(30)}`, '2026-10-01T09:00:00Z', 'stated'), round('', '2026-10-01T09:00:00Z', '')]));
  return { projects, rounds };
}

test('noFieldValueReachesTheReport', () => {
  const { projects, rounds } = markedTree();
  const ran = spawnSync(process.execPath, [CLI, '--rounds', rounds, '--projects', projects, '--runs', '2'], { encoding: 'utf8' });
  assert.equal(ran.status, 0, ran.stderr);
  assert.doesNotMatch(ran.stdout, /ZZ-MARK/u, 'a marker planted in a free-text slot reached stdout');
  assert.doesNotMatch(ran.stdout, /slug|rounds\.json|repo:|testsToDelete/u, 'a path or a folder name reached stdout');
  assert.equal(ran.stdout.trim().split('\n').length, 1, 'one JSON line');
});

test('progressGoesToStderrAsCountsOnlyWhileStdoutStaysOneJsonLine', () => {
  const { projects, rounds } = markedTree();
  const ran = spawnSync(process.execPath, [CLI, '--rounds', rounds, '--projects', projects, '--runs', '2'], { encoding: 'utf8' });
  assert.equal(ran.status, 0, ran.stderr);
  assert.match(ran.stderr, /reading caller sessions 1\/1/);
  assert.match(ran.stderr, /timing session 1\/1: full parse run 2\/2/);
  assert.match(ran.stderr, /timing session 1\/1: day-gated parse run 2\/2/);
  assert.doesNotMatch(ran.stderr, /ZZ-MARK|slug|rounds\.json|testsToDelete/u, 'progress names no value and no path');
  assert.equal(ran.stdout.trim().split('\n').length, 1, 'stdout is still the single JSON result');
  JSON.parse(ran.stdout);
});

test('theSameTreeProducesNonZeroCountsInEverySection', () => {
  const { projects, rounds } = markedTree();
  const report = run(JSON.parse(readFileSync(rounds, 'utf8')), projects);
  assert.equal(report.coverage.rounds.found, 1);
  assert.equal(report.coverage.rounds.notASessionId, 2);
  assert.equal(report.layout.sessions, 1);
  assert.ok(report.versions['2.1.289'].assistantLines > 0);
  assert.equal(report.versions['(other)'].assistantLines, 1, 'a version failing its shape is bucketed, not printed');
  assert.deepEqual(report.versions['(other)'].effort, { '(other)': 1 });
  assert.ok(report.messages.ids > 0 && report.messages.differing === 1);
  assert.ok(report.agents.metas === 1 && report.agents.agentToolUses === 1);
  assert.deepEqual(report.agents.requestShape, { '(other)': 1 });
  assert.ok(report.writes.main.total === 1 && report.writes.agent.total === 1);
  assert.equal(report.toolResults.total, 1);
  assert.ok(report.files.unparseableAtEnd === 1);
  assert.ok(report.sizes.sessions === 1 && report.sizes.maxBytes > 0);
  assert.ok(report.largest[0].lines > 0 && report.largest[0].fullParse.linesParsed > 0);
  assert.ok(report.models['claude-sonnet-5-5'] > 0 && report.models['(other)'] === 1);
});

test('theHarnessWritesNothingUnderTheProjectsFolder', () => {
  const { projects, rounds } = markedTree();
  const snapshot = () => readdirSync(projects, { recursive: true }).sort().map((name) => `${name}:${statSync(join(projects, name)).mtimeMs}:${statSync(join(projects, name)).size}`);
  const before = snapshot();
  assert.equal(spawnSync(process.execPath, [CLI, '--rounds', rounds, '--projects', projects, '--runs', '1'], { encoding: 'utf8' }).status, 0);
  assert.deepEqual(snapshot(), before);
});

// ---- the CLI -------------------------------------------------------------------------------------------------------------

test('aMissingRoundsArgumentExitsSixtyFiveWithAUsageLine', () => {
  const ran = spawnSync(process.execPath, [CLI, '--projects', base], { encoding: 'utf8' });
  assert.equal(ran.status, 65);
  assert.match(ran.stderr, /usage: measure-caller-transcripts/u);
  assert.equal(ran.stdout, '');
});

test('badArgumentsAreRefusedAsSixtyFiveNeverSixtyFour', () => {
  const { root } = fresh();
  const rounds = join(root, 'rounds.json');
  put(rounds, '[]');
  for (const extra of [['--day', '2026-13-1'], ['--runs', '0'], ['--largest', '-1'], ['--bogus', 'x'], ['--runs']]) {
    const ran = spawnSync(process.execPath, [CLI, '--rounds', rounds, '--projects', root, ...extra], { encoding: 'utf8' });
    assert.equal(ran.status, 65, `${extra.join(' ')}: ${ran.stderr}`);
  }
});

test('anUnreadableRoundsFileIsRefusedWithoutQuotingIt', () => {
  const { root } = fresh();
  const rounds = join(root, 'rounds.json');
  put(rounds, `{"caller": "${MARK(1)}" this is not json`);
  const ran = spawnSync(process.execPath, [CLI, '--rounds', rounds, '--projects', root], { encoding: 'utf8' });
  assert.equal(ran.status, 65);
  assert.doesNotMatch(ran.stderr, /ZZ-MARK/u);
  assert.equal(spawnSync(process.execPath, [CLI, '--rounds', join(root, 'absent.json'), '--projects', root], { encoding: 'utf8' }).status, 65);
});

test('aValidRunExitsZeroWithOneJsonLine', () => {
  const { projects, rounds } = markedTree();
  const ran = spawnSync(process.execPath, [CLI, '--rounds', rounds, '--projects', projects, '--largest', '1', '--runs', '3', '--day', '2026-10-01'], { encoding: 'utf8' });
  assert.equal(ran.status, 0, ran.stderr);
  const report = JSON.parse(ran.stdout);
  assert.equal(report.largest[0].gateDay, '2026-10-01');
  assert.equal(report.timedBy, 'node');
});

// ---- the exporter ----------------------------------------------------------------------------------------------------------

/** A Python 3 launcher that exists here: `py -3` on Windows (python3 is the Store stub), `python3` elsewhere. */
function findPython() {
  const candidates = process.platform === 'win32' ? [['py', ['-3']]] : [['python3', []], ['python', []]];
  return candidates.find(([command, args]) => spawnSync(command, [...args, '-I', '--version'], { encoding: 'utf8' }).status === 0);
}
const python = findPython();

function pythonRun(code, ...args) {
  const [command, lead] = python;
  return spawnSync(command, [...lead, '-I', '-c', code, ...args], { encoding: 'utf8' });
}

function seedStore(path) {
  const code = [
    'import sqlite3, sys',
    'c = sqlite3.connect(sys.argv[1])',
    'c.execute("CREATE TABLE sessions (id TEXT PRIMARY KEY, repo_path TEXT NOT NULL)")',
    'c.execute("CREATE TABLE rounds (id INTEGER PRIMARY KEY AUTOINCREMENT, session_id TEXT, stage TEXT, number INTEGER, status TEXT, started_utc TEXT, completed_utc TEXT, caller TEXT, caller_vendor TEXT)")',
    'c.execute("INSERT INTO sessions VALUES (\'s1\', \'/home/\\u00fcn\\u00ef/repo\')")',
    'rows = [("s1","CodeReview",1,"Done","2026-10-01T10:00:00Z","2026-10-01T10:05:00Z","c1","claude-code"),',
    '        ("s1","CodeReview",2,"Done","2026-09-01T10:00:00Z","2026-09-01T10:05:00Z","c1","claude-code"),',
    '        ("s1","PlanReview",1,"Done","2026-10-02T10:00:00Z","2026-10-02T10:05:00Z","c2","codex"),',
    '        ("s1","CodeReview",3,"Done","2026-10-03T10:00:00Z","2026-10-03T10:05:00Z","","")]',
    'c.executemany("INSERT INTO rounds (session_id,stage,number,status,started_utc,completed_utc,caller,caller_vendor) VALUES (?,?,?,?,?,?,?,?)", rows)',
    'c.commit()',
  ].join('\n');
  const made = pythonRun(code, path);
  assert.equal(made.status, 0, made.stderr);
}

const exporter = (...args) => spawnSync(python[0], [...python[1], '-I', EXPORTER, ...args], { encoding: 'utf8' });

test('theExporterWritesOnlyCodeRoundsSinceTheDayAsLfUtf8', { skip: !python }, () => {
  const { root } = fresh();
  const db = join(root, 'coai.db');
  const out = join(root, 'rounds.json');
  seedStore(db);
  const before = readFileSync(db);
  const ran = exporter(db, out, '2026-09-30');
  assert.equal(ran.status, 0, ran.stderr);
  const bytes = readFileSync(out);
  assert.ok(!bytes.includes(0x0d), 'LF only');
  assert.notEqual(bytes[0], 0xef, 'no BOM');
  const rows = JSON.parse(bytes.toString('utf8'));
  assert.deepEqual(rows.map((row) => row.number), [1, 3], 'one stage, one window, oldest first');
  assert.deepEqual(Object.keys(rows[0]), ['session_id', 'number', 'started_utc', 'completed_utc', 'status', 'caller', 'caller_vendor', 'repo_path']);
  assert.equal(rows[0].repo_path, '/home/\u00fcn\u00ef/repo');
  assert.ok(bytes.toString('utf8').includes('\u00fcn\u00ef'), 'non-ASCII survives unescaped');
  assert.deepEqual(readFileSync(db), before, 'the store is untouched');
});

test('theExporterRefusesAnOutputThatIsTheStoreItselfAndLeavesTheStoreIntact', { skip: !python }, () => {
  const { root } = fresh();
  const db = join(root, 'coai.db');
  seedStore(db);
  const before = readFileSync(db);
  const swapped = exporter(db, db, '2026-09-30'); // the arguments swapped: the "output" would truncate the live store
  assert.equal(swapped.status, 65, swapped.stderr);
  assert.match(swapped.stderr, /same file/);
  assert.deepEqual(readFileSync(db), before, 'the store is byte-for-byte untouched');
  const link = join(root, 'looks-like-output.json');
  linkSync(db, link); // a second name for the same file
  const linked = exporter(db, link, '2026-09-30');
  assert.equal(linked.status, 65, linked.stderr);
  assert.deepEqual(readFileSync(db), before, 'still untouched through a hard link');
});

test('theExporterRefusesAnOutputNamedLikeAStoreFile', { skip: !python }, () => {
  const { root } = fresh();
  const db = join(root, 'coai.db');
  seedStore(db);
  for (const name of ['copy.db', 'copy.sqlite', 'coai.db-wal', 'coai.db-shm', 'COPY.DB']) {
    const ran = exporter(db, join(root, name), '2026-09-30');
    assert.equal(ran.status, 65, name + ' ' + ran.stderr);
    assert.equal(existsSync(join(root, name)), false, name + ' was not created');
  }
});

test('theExporterWritesThroughATempFileAndLeavesNothingBehind', { skip: !python }, () => {
  const { root } = fresh();
  const db = join(root, 'coai.db');
  const out = join(root, 'rounds.json');
  seedStore(db);
  writeFileSync(out, 'previous');
  assert.equal(exporter(db, out, '2026-09-30').status, 0);
  assert.deepEqual(readdirSync(root).sort(), ['coai.db', 'projects', 'rounds.json'], 'no temp file is left in the output folder');
  assert.ok(JSON.parse(readFileSync(out, 'utf8')).length > 0);
  const empty = join(root, 'empty.db');
  assert.equal(pythonRun('import sqlite3, sys\nsqlite3.connect(sys.argv[1]).close()', empty).status, 0);
  writeFileSync(out, 'previous');
  const failed = exporter(empty, out, '2026-09-30'); // no tables: the query fails
  assert.equal(failed.status, 74);
  assert.equal(readFileSync(out, 'utf8'), 'previous', 'a failed export leaves an existing output as it was');
  assert.deepEqual(readdirSync(root).sort(), ['coai.db', 'empty.db', 'projects', 'rounds.json']);
});

test('theExporterRefusesAMissingStoreWithoutCreatingOne', { skip: !python }, () => {
  const { root } = fresh();
  const db = join(root, 'absent.db');
  const ran = exporter(db, join(root, 'out.json'), '2026-09-30');
  assert.equal(ran.status, 74);
  assert.equal(existsSync(db), false, 'mode=ro must not create the database');
  assert.equal(existsSync(join(root, 'out.json')), false);
});

test('theExporterRefusesBadArgumentsAsSixtyFive', { skip: !python }, () => {
  const { root } = fresh();
  assert.equal(exporter(join(root, 'x.db'), join(root, 'o.json')).status, 65);
  assert.equal(exporter(join(root, 'x.db'), join(root, 'o.json'), 'yesterday').status, 65);
});
