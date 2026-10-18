// node --test scripts/lib/transcript-sanitise.test.mjs — the privacy boundary of the caller-transcript fixtures
// (todo/PLAN_a_round_records_who_wrote_the_code_and_how_much_E1.md, E1.S3). Everything here is a SYNTHETIC session built in
// a temp folder: the real ~/.claude is never named, listed or read. CI runs it on Linux ("Probe script tests").
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readdirSync, readFileSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { after, test } from 'node:test';
import { cutSession, createSanitiser, loadAllowlist } from './transcript-sanitise.mjs';
import { compareOrdinal } from './ordinal.mjs';
import * as sanitise from './transcript-sanitise.mjs'; // names the fixes add are reached through here: a missing one fails ITS test, not the whole file

const here = dirname(fileURLToPath(import.meta.url));
const CLI = join(here, '..', 'cut-transcript-fixtures.mjs');
const ALLOWLIST_FILE = join(here, '..', '..', 'src_mcp', 'tests', 'fixtures', 'caller-transcripts', 'allowlist.json');
const allowlist = loadAllowlist(JSON.parse(readFileSync(ALLOWLIST_FILE, 'utf8')));
const SENTINEL = allowlist.sentinel;

const base = mkdtempSync(join(realpathSync.native(tmpdir()), 'transcript-sanitise-test-'));
after(() => rmSync(base, { recursive: true, force: true }));
let counter = 0;
const scratch = (name) => { const d = join(base, `${name}-${++counter}`); mkdirSync(d, { recursive: true }); return d; };

// ---- a synthetic session ---------------------------------------------------------------------------------------------
const SESSION = '11111111-2222-4333-8444-555555555555';
const OTHER_SESSION = '66666666-2222-4333-8444-555555555555';
const AGENT_A = 'aaaaaaaaaaaaaaaa1';
const AGENT_B = 'bbbbbbbbbbbbbbbb2';
const AGENT_NESTED = 'cccccccccccccccc3';
const TOOL_AGENT_A = 'toolu_01AgentLaunchAAAAAAAA';
const TOOL_AGENT_B = 'toolu_01AgentLaunchBBBBBBBB';
const TOOL_NESTED = 'toolu_01AgentLaunchNESTED00';
const TOOL_BASH = 'toolu_01BashSecretCommand000';
const MSG_1 = 'msg_01OriginalMessageOne';
const REPO = 'D:/rsd/_wt/x';
const MARKERS = ['MARKER_PROMPT', 'MARKER_THINK', 'MARKER_SIG', 'MARKER_RESULT', 'MARKER_STDOUT', 'MARKER_CMD', 'MARKER_DESC',
  'MARKER_BRANCH', 'MARKER_TIER', 'MARKER_BACKUP', 'secret.txt', 'MARKER_META_DESC', 'MARKER_SLUG',
  'MARKER_PRIVATE_FILE', 'MARKER_PRIVATE_DIR', 'MARKER_SERVER', 'MARKER_TOOL', 'MARKER_CUSTOM_AGENT'];

const uid = (n) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const T = (min, sec = 0) => `2026-10-01T10:${String(min).padStart(2, '0')}:${String(sec).padStart(2, '0')}.000Z`;

function mainLines() {
  return [
    { type: 'queue-operation', operation: 'enqueue', timestamp: T(0), sessionId: SESSION, content: 'MARKER_PROMPT queued' },
    { parentUuid: null, isSidechain: false, userType: 'external', cwd: 'D:\\rsd\\_wt\\x', sessionId: SESSION, version: '2.1.0',
      gitBranch: 'MARKER_BRANCH', type: 'user', message: { role: 'user', content: 'MARKER_PROMPT hello' },
      uuid: '00000000-0000-4000-8000-000000000001', timestamp: T(1) },
    { parentUuid: '00000000-0000-4000-8000-000000000001', isSidechain: false, cwd: 'D:\\rsd\\_wt\\x', sessionId: SESSION,
      version: '2.1.0', gitBranch: 'MARKER_BRANCH', type: 'assistant', requestedModel: 'opus', effort: 'high',
      message: { id: MSG_1, model: 'claude-opus-4-1', role: 'assistant', stop_reason: 'MARKER_TIER',
        content: [
          { type: 'thinking', thinking: 'MARKER_THINK', signature: 'MARKER_SIG' },
          { type: 'tool_use', id: TOOL_AGENT_A, name: 'Agent', input: { description: 'MARKER_DESC', prompt: 'MARKER_PROMPT launch',
            subagent_type: 'general-purpose', model: 'sonnet', run_in_background: true } },
          { type: 'tool_use', id: TOOL_BASH, name: 'Bash', input: { command: 'MARKER_CMD cat secret.txt', description: 'MARKER_DESC' } },
          { type: 'tool_use', id: 'toolu_01EditFile00000000001', name: 'Edit', input: { file_path: 'D:\\rsd\\_wt\\x\\MARKER_PRIVATE_DIR\\MARKER_PRIVATE_FILE.cs', old_string: 'MARKER_CMD' } },
          { type: 'tool_use', id: 'toolu_01McpTool0000000000001', name: 'mcp__MARKER_SERVER__MARKER_TOOL', input: { q: 'MARKER_CMD' } }],
        usage: { input_tokens: 3, output_tokens: 7, cache_creation_input_tokens: 11, cache_read_input_tokens: 13, service_tier: 'MARKER_TIER' } },
      uuid: '00000000-0000-4000-8000-000000000002', timestamp: T(2) },
    { parentUuid: '00000000-0000-4000-8000-000000000002', isSidechain: false, cwd: 'D:\\rsd\\_wt\\x', sessionId: SESSION, version: '2.1.0',
      type: 'user', message: { role: 'user', content: [{ type: 'tool_result', tool_use_id: TOOL_BASH, content: 'MARKER_RESULT', is_error: true },
        { type: 'tool_result', tool_use_id: TOOL_AGENT_A, content: [{ type: 'text', text: 'MARKER_RESULT' }] }] },
      toolUseResult: { stdout: 'MARKER_STDOUT', type: 'user', message: { role: 'user' } },
      uuid: '00000000-0000-4000-8000-000000000003', timestamp: T(3) },
    { type: 'file-history-snapshot', messageId: 'MARKER_BRANCH', isSnapshotUpdate: false,
      snapshot: { messageId: 'x', trackedFileBackups: { 'secret.txt': { backupFileName: 'MARKER_BACKUP', version: 2 } }, timestamp: T(4) } },
    { parentUuid: null, isSidechain: false, cwd: '/home/someone/src/y', sessionId: SESSION, version: '2.1.0', type: 'assistant',
      message: { id: 'msg_01OriginalMessageTwo', model: 'claude-sonnet-5', role: 'assistant',
        content: [{ type: 'tool_use', id: TOOL_AGENT_B, name: 'Agent', input: { prompt: 'MARKER_PROMPT', subagent_type: 'MARKER_CUSTOM_AGENT' } }],
        usage: { input_tokens: 1, output_tokens: 2, cache_creation_input_tokens: 0, cache_read_input_tokens: 5 } },
      uuid: '00000000-0000-4000-8000-000000000004', timestamp: T(30) }
  ];
}

function agentLines(agentId, tsMinute, extra = []) {
  return [
    { parentUuid: null, isSidechain: true, agentId, cwd: 'D:\\rsd\\_wt\\x', sessionId: SESSION, version: '2.1.0', type: 'user',
      message: { role: 'user', content: 'MARKER_PROMPT sub' }, uuid: uid(tsMinute * 10), timestamp: T(tsMinute) },
    { parentUuid: null, isSidechain: true, agentId, cwd: 'D:\\rsd\\_wt\\x', sessionId: SESSION, version: '2.1.0', type: 'assistant',
      requestedModel: 'sonnet', message: { id: `msg_01Sub${agentId}`, model: 'claude-sonnet-5', role: 'assistant',
        content: [{ type: 'text', text: 'MARKER_RESULT' }, ...extra],
        usage: { input_tokens: 4, output_tokens: 4, cache_creation_input_tokens: 0, cache_read_input_tokens: 0 } },
      uuid: uid(tsMinute * 10 + 1), timestamp: T(tsMinute + 1) }
  ];
}

const nested = { type: 'tool_use', id: TOOL_NESTED, name: 'Agent', input: { prompt: 'MARKER_PROMPT', subagent_type: 'general-purpose' } };

const jl = (lines) => lines.map((l) => JSON.stringify(l)).join('\n') + '\n';

/** Writes the synthetic session into <root>/projects/-slug/ and returns the main file. */
function writeSession(root, { crlf = false, lateAgentLine = false } = {}) {
  const dir = join(root, 'projects', 'D--rsd--wt-MARKER_SLUG');
  const sub = join(dir, SESSION, 'subagents');
  mkdirSync(sub, { recursive: true });
  const put = (file, text) => writeFileSync(file, crlf ? text.replaceAll('\n', '\r\n') : text);
  put(join(dir, `${SESSION}.jsonl`), jl(mainLines()));
  const a = agentLines(AGENT_A, 5, [nested]);
  if (lateAgentLine) a.push({ ...a[0], timestamp: T(55) });
  put(join(sub, `agent-${AGENT_A}.jsonl`), jl(a));
  put(join(sub, `agent-${AGENT_A}.meta.json`), JSON.stringify({ agentType: 'general-purpose', description: 'MARKER_META_DESC', toolUseId: TOOL_AGENT_A, spawnDepth: 1, requestShape: 'background', model: 'sonnet' }));
  put(join(sub, `agent-${AGENT_NESTED}.jsonl`), jl(agentLines(AGENT_NESTED, 6)));
  put(join(sub, `agent-${AGENT_NESTED}.meta.json`), JSON.stringify({ agentType: 'general-purpose', toolUseId: TOOL_NESTED, spawnDepth: 2, parentAgentId: AGENT_A, requestShape: 'background', model: 'haiku' }));
  put(join(sub, `agent-${AGENT_B}.jsonl`), jl(agentLines(AGENT_B, 31)));
  put(join(sub, `agent-${AGENT_B}.meta.json`), JSON.stringify({ agentType: 'MARKER_CUSTOM_AGENT', toolUseId: TOOL_AGENT_B, spawnDepth: 1, requestShape: 'background', model: 'opus' }));
  // a file the cutter was never named for: it must not be copied
  mkdirSync(join(dir, SESSION, 'tool-results'), { recursive: true });
  writeFileSync(join(dir, SESSION, 'tool-results', 'MARKER_RESULT.txt'), 'MARKER_RESULT output');
  return join(dir, `${SESSION}.jsonl`);
}

const REPOS = [{ from: 'D:/rsd/_wt/x', to: 'D:/fixture/repo-a' }, { from: '/home/someone/src/y', to: '/fixture/repo-b' }];

function cut(sessionFile, overrides = {}) {
  const outDir = overrides.outDir ?? scratch('out');
  const result = cutSession({ sessionFile, outDir, allowlist, salt: 'salt-one', repos: REPOS, otherRoot: '/fixture/other',
    quiet: true, ...overrides });
  assert.equal(result.ok, true, result.refusal);
  return { outDir, summary: result.value };
}

function walk(dir) {
  return readdirSync(dir, { withFileTypes: true, recursive: true }).filter((e) => e.isFile())
    .map((e) => join(e.parentPath ?? e.path, e.name)).sort(compareOrdinal);
}
const readAll = (dir) => walk(dir).map((f) => ({ file: relative(dir, f).replaceAll('\\', '/'), text: readFileSync(f, 'utf8') }));
const jsonLines = (text) => text.split('\n').filter(Boolean).map((l) => JSON.parse(l));
const sanitiser = (repos = REPOS) => createSanitiser({ allowlist, salt: 'salt-one', repos, otherRoot: '/fixture/other' });

// ---- the allowlist itself --------------------------------------------------------------------------------------------
test('theKeptKeySetIsEverySegmentOfEveryAllowlistedPathAndNothingMatchesTheRenameShape', () => {
  for (const k of ['message', 'content', 'input', 'usage', 'file_path', 'is_error', 'tool_use_id', 'parentAgentId'])
    assert.ok(allowlist.keptKeys.has(k), `${k} must be a structural key`);
  assert.ok(!allowlist.keptKeys.has('[]'));
  assert.ok(![...allowlist.keptKeys].some((k) => /^k\d+$/.test(k)));
  assert.throws(() => loadAllowlist({ sentinel: 'S', shapes: {}, fields: [{ path: 'message.k1', shape: 'int' }] }), /k\d+/);
  assert.throws(() => loadAllowlist({ sentinel: 'S', shapes: {}, fields: [{ path: 'a', shape: 'no-such-shape' }] }), /shape/);
});

// ---- the sanitiser ---------------------------------------------------------------------------------------------------
test('everyStringOffTheAllowlistBecomesTheSentinel', () => {
  const src = writeSession(scratch('in'));
  const { outDir } = cut(src);
  const files = readAll(outDir);
  assert.ok(files.length >= 7, 'main + 3 agents + 3 metas');
  for (const { file, text } of files) {
    for (const m of MARKERS) assert.ok(!text.includes(m) && !file.includes(m), `${m} survived in ${file}`);
    if (file.endsWith('.jsonl')) assert.ok(text.includes(SENTINEL), `${file} carries no sentinel at all`);
  }
  const main = files.find((f) => f.file.endsWith(`.jsonl`) && !f.file.includes('subagents'));
  const out = jsonLines(main.text);
  const inp = mainLines();
  assert.equal(out.length, inp.length, 'line count unchanged');
  inp.forEach((line, i) => assert.deepEqual(Object.keys(out[i]).map((k) => (/^k\d+$/.test(k) ? 'k' : k)),
    Object.keys(line).map((k) => (allowlist.keptKeys.has(k) ? k : 'k')), `key order of line ${i}`));
  assert.ok(!files.some((f) => f.file.includes('tool-results')), 'a file the cutter was not named for was copied');
});

test('anAllowlistedFieldKeepsItsShapeAndOnlyItsShape', () => {
  const s = sanitiser();
  s.prepare([{ message: { usage: { input_tokens: 5 } } }]);
  const out = s.sanitiseValue({ message: { model: 'a sentence about my secret project', role: 'assistant', id: 'not-a-message-id' }, version: 'banana',
    timestamp: 'yesterday', effort: 'high', agentId: 'ZZZZ', isSidechain: 'true' });
  assert.equal(out.message.model, SENTINEL);
  assert.equal(out.message.role, 'assistant');
  assert.equal(out.message.id, SENTINEL);
  assert.equal(out.version, SENTINEL);
  assert.equal(out.timestamp, SENTINEL);
  assert.equal(out.effort, 'high');
  assert.equal(out.agentId, SENTINEL);
  assert.equal(out.isSidechain, SENTINEL, 'a string where a bool belongs is text');
  assert.equal(s.sanitiseValue({ message: { model: 'claude-opus-4-1', usage: { input_tokens: 5 } } }).message.model, 'claude-opus-4-1');
});

test('aModelIdOrVersionOutsideTheExactListsIsTheSentinelWhateverItsPrefixOrSuffix', () => {
  const s = sanitiser();
  const out = s.sanitiseValue({ message: { model: 'claude-private-client-canary' }, version: '2.1.0+private-client-canary', requestedModel: 'opus-private-canary',
    model: 'sonnet[private]', agentType: 'general-purpose' });
  assert.equal(out.message.model, SENTINEL);
  assert.equal(out.version, SENTINEL);
  assert.equal(out.requestedModel, SENTINEL);
  assert.equal(out.model, SENTINEL);
  assert.equal(s.sanitiseValue({ version: '2.1.289', requestedModel: 'opus[1m]', model: 'fable' }).version, '2.1.289');
  for (const unmeasured of ['2.1.999', '2.1.282', '2.1.0', '3.0.0', '2.1.289 ']) assert.equal(s.sanitiseValue({ version: unmeasured }).version, SENTINEL, `${unmeasured} was never measured`);
  for (const measured of ['2.1.283', '2.1.288', '2.1.300']) assert.equal(s.sanitiseValue({ version: measured }).version, measured);
  assert.equal(s.sanitiseValue({ requestedModel: 'claude-fable-5-1' }).requestedModel, 'claude-fable-5-1');
});

test('everyInstantIsRedrawnOnASyntheticClockByRankAndOrderTiesAndWindowsSurvive', () => {
  const s = sanitiser();
  const real = [T(0), T(1), T(1), T(7), T(55)];
  s.prepare(real.map((timestamp) => ({ timestamp })), [T(7, 30)]);
  const mapped = real.map((timestamp) => s.sanitiseValue({ timestamp }).timestamp);
  assert.deepEqual(mapped, ['2000-01-01T00:00:00.000Z', '2000-01-01T00:00:01.000Z', '2000-01-01T00:00:01.000Z', '2000-01-01T00:00:02.000Z', '2000-01-01T00:00:04.000Z'],
    'rank, not offset: ties stay ties, the 54-minute gap is one step, and the extra instant took rank 3');
  assert.ok(!mapped.some((m) => m.startsWith('2026')), 'no real date survives');
  assert.equal(s.sanitiseRounds({ callerSessionId: SESSION, gateSessions: [{ id: SESSION, repoPath: 'D:/rsd/_wt/x', openedUtc: T(0),
    rounds: [{ number: 1, startedUtc: T(7, 30), completedUtc: T(55), status: 'done', headSha: 'abc' }] }] }).gateSessions[0].rounds[0].startedUtc, '2000-01-01T00:00:03.000Z');
});

test('anInstantNobodyGavePrepareIsAnErrorNotAPassThrough', () => {
  assert.throws(() => sanitiser().sanitiseValue({ timestamp: T(3) }), /prepare/);
  assert.throws(() => sanitiser().sanitiseValue({ message: { usage: { output_tokens: 8 } } }), /prepare/);
});

test('usageCountsAreRedrawnAsRanksKeepingEqualityOrderAndZero', () => {
  const s = sanitiser();
  const line = (output, cacheRead) => ({ message: { usage: { input_tokens: 4, output_tokens: output, cache_read_input_tokens: cacheRead } } });
  s.prepare([line(7, 0), line(7, 0), line(9100, 0), line(250000, 3)]);
  const usage = (l) => s.sanitiseValue(l).message.usage;
  assert.equal(usage(line(7, 0)).output_tokens, usage(line(7, 0)).output_tokens, 'a message\'s lines keep equal usage equal');
  assert.ok(usage(line(7, 0)).output_tokens < usage(line(9100, 0)).output_tokens && usage(line(9100, 0)).output_tokens < usage(line(250000, 3)).output_tokens, 'growth survives');
  assert.deepEqual([usage(line(7, 0)).output_tokens, usage(line(9100, 0)).output_tokens, usage(line(250000, 3)).output_tokens], [1, 2, 3], 'no real magnitude survives');
  assert.equal(usage(line(7, 0)).cache_read_input_tokens, 0, 'zero stays zero');
  assert.equal(usage(line(250000, 3)).cache_read_input_tokens, 1);
});

test('aFractionalOrNegativeNumberAtAnIntPathIsZero', () => {
  const s = sanitiser();
  s.prepare([{ message: { usage: { input_tokens: 3 } } }]);
  const usage = (v) => s.sanitiseValue({ message: { usage: { input_tokens: v, output_tokens: v } }, spawnDepth: v }).message.usage;
  assert.deepEqual(usage(2.5), { input_tokens: 0, output_tokens: 0 });
  assert.deepEqual(usage(-4), { input_tokens: 0, output_tokens: 0 });
  assert.equal(s.sanitiseValue({ spawnDepth: 1.5 }).spawnDepth, 0);
  assert.equal(s.sanitiseValue({ spawnDepth: 2 }).spawnDepth, 2, 'a depth is structural, kept');
});

test('aNumberIsKeptOnlyAtAnIntPathAndEveryOtherNumberIsZero', () => {
  const s = sanitiser();
  const input = { parentUuid: null, isSidechain: true, durationMs: 12, ratio: 0.5, nope: false, note: '', other: ['x', 3, { a: 'b' }],
    message: { usage: { input_tokens: 5, service_tier: 'MARKER_TIER', server_tool_use: { n: 9 } } }, spawnDepth: 2, effort: 3 };
  s.prepare([input]);
  const out = s.sanitiseValue(input);
  assert.deepEqual(out, { parentUuid: null, isSidechain: true, k1: 0, k2: 0, k3: false, k4: SENTINEL, k5: [SENTINEL, 0, { k1: SENTINEL }],
    message: { usage: { input_tokens: 1, k1: SENTINEL, k2: { k1: 0 } } }, spawnDepth: 2, effort: 0 });
});

test('structuralContainersKeepTheirNamesAndIsErrorIsKeptAsABool', () => {
  const out = sanitiser().sanitiseValue({ message: { role: 'user', content: [
    { type: 'tool_result', tool_use_id: TOOL_BASH, content: 'MARKER_RESULT', is_error: true },
    { type: 'tool_use', id: TOOL_AGENT_A, name: 'Write', input: { file_path: 'D:/rsd/_wt/x/a.cs', content: 'MARKER_PROMPT', command: 'MARKER_CMD' } }] } });
  const [res, use] = out.message.content;
  assert.equal(res.is_error, true);
  assert.equal(res.content, SENTINEL, 'content as a string is text');
  assert.deepEqual(Object.keys(use.input), ['file_path', 'content', 'k1']);
  assert.equal(use.input.content, SENTINEL);
  assert.equal(use.input.k1, SENTINEL);
  assert.match(use.input.file_path, /^D:\/fixture\/repo-a\/p\d+\.cs$/);
  assert.ok(!JSON.stringify(out).includes('MARKER'));
});

test('aKeyThatLooksLikeAPathOrAnIdentifierIsRenamedAndNumberedPerObject', () => {
  const s = sanitiser();
  const out = s.sanitiseValue({ snapshot: { trackedFileBackups: { 'secret.txt': { a: 1 }, 'D:\\x\\y.cs': 'v', alice: 'v', 'user@example.com': 'v' } },
    k1: 'x', gitBranch: 'b', type: 'user' });
  assert.deepEqual(Object.keys(out), ['k1', 'k2', 'k3', 'type'], 'top level: snapshot, k1, gitBranch renamed in order; type kept');
  const backups = out.k1.k1;
  assert.deepEqual(Object.keys(backups), ['k1', 'k2', 'k3', 'k4']);
  assert.deepEqual(backups.k1, { k1: 0 }, 'numbers outside an int path are 0, keys are renamed in turn');
  const text = JSON.stringify(out);
  for (const leak of ['secret.txt', 'y.cs', 'alice', 'example.com', 'snapshot', 'gitBranch']) assert.ok(!text.includes(leak), leak);
  assert.equal(out.type, 'user', 'a top-level type is the allowlisted enum');
});

test('nothingBelowARenamedKeyIsAllowlistedAnyMoreAndItsKeysAreRenamedInTurn', () => {
  const out = sanitiser().sanitiseValue({ toolUseResult: { type: 'user', sessionId: SESSION, message: { role: 'user', model: 'claude-opus-4-1' }, timestamp: T(1), n: 4 } });
  assert.deepEqual(out, { k1: { k1: SENTINEL, k2: SENTINEL, k3: { k1: SENTINEL, k2: SENTINEL }, k4: SENTINEL, k5: 0 } });
});

// ---- ids ------------------------------------------------------------------------------------------------------------
test('idsAreRemappedConsistentlyAndStillJoin', () => {
  const src = writeSession(scratch('in'));
  const { outDir } = cut(src);
  const files = readAll(outDir);
  const originals = [SESSION, AGENT_A, AGENT_B, AGENT_NESTED, TOOL_AGENT_A, TOOL_AGENT_B, TOOL_NESTED, TOOL_BASH, MSG_1,
    ...[1, 2, 3, 4].map((n) => `00000000-0000-4000-8000-00000000000${n}`)];
  for (const { file, text } of files) for (const id of originals) assert.ok(!text.includes(id) && !file.includes(id), `${id} appears in ${file}`);

  const mainFile = files.find((f) => /^projects\/[^/]+\/[^/]+\.jsonl$/.test(f.file));
  assert.ok(mainFile, 'main file under projects/<slug>/<sessionId>.jsonl');
  const agentUses = new Set(jsonLines(mainFile.text).flatMap((l) => l.message?.content ?? []).filter((c) => c.type === 'tool_use' && c.name === 'Agent').map((c) => c.id));
  const agentFiles = new Set(files.map((f) => /subagents\/agent-([0-9a-f]+)\.jsonl$/.exec(f.file)?.[1]).filter(Boolean));
  const nestedUses = new Set(files.filter((f) => /subagents\/.*\.jsonl$/.test(f.file)).flatMap((f) => jsonLines(f.text)).flatMap((l) => l.message?.content ?? [])
    .filter((c) => c.type === 'tool_use' && c.name === 'Agent').map((c) => c.id));
  const metas = files.filter((f) => f.file.endsWith('.meta.json')).map((f) => JSON.parse(f.text));
  assert.equal(metas.length, 3);
  for (const m of metas) {
    assert.ok(agentUses.has(m.toolUseId) || nestedUses.has(m.toolUseId), `meta toolUseId ${m.toolUseId} joins no Agent tool_use`);
    if (m.parentAgentId) assert.ok(agentFiles.has(m.parentAgentId), 'parentAgentId names an agent file');
  }
  const sessionDir = /^projects\/[^/]+\/([^/]+)\.jsonl$/.exec(mainFile.file)[1];
  assert.ok(files.every((f) => !f.file.includes('subagents') || f.file.includes(`/${sessionDir}/subagents/`)), 'agent files sit under the remapped session id');
  // the remapped ids carry the SHAPE of the original
  assert.match(sessionDir, /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/);
  for (const id of agentFiles) assert.equal(id.length, AGENT_A.length);
  for (const { text } of files.filter((f) => f.file.endsWith('.jsonl'))) {
    for (const line of jsonLines(text)) if (line.sessionId) assert.equal(line.sessionId, sessionDir);
  }
});

test('theSameOriginalAlwaysMapsToTheSameIdAndDifferentOnesDoNot', () => {
  const s = sanitiser();
  const a1 = s.sanitiseValue({ agentId: AGENT_A, parentAgentId: AGENT_A, toolUseId: TOOL_BASH });
  const a2 = s.sanitiseValue({ agentId: AGENT_A, parentAgentId: AGENT_B, toolUseId: TOOL_BASH });
  assert.equal(a1.agentId, a1.parentAgentId);
  assert.equal(a1.agentId, a2.agentId);
  assert.notEqual(a1.agentId, a2.parentAgentId);
  assert.equal(a1.toolUseId, a2.toolUseId);
  assert.notEqual(a1.agentId, AGENT_A);
  assert.match(a1.toolUseId, /^toolu_[0-9A-Za-z_]{8,80}$/);
  assert.equal(a1.toolUseId.length, TOOL_BASH.length);
  const other = createSanitiser({ allowlist, salt: 'salt-two', repos: REPOS, otherRoot: '/fixture/other' }).sanitiseValue({ agentId: AGENT_A });
  assert.notEqual(other.agentId, a1.agentId, 'the salt is per fixture');
});

// ---- paths ----------------------------------------------------------------------------------------------------------
test('everySegmentBelowARepositoryRootIsMappedAndOnlyTheRootAndTheSeparatorsSurvive', () => {
  const s = sanitiser();
  const input = (p) => s.sanitiseValue({ message: { content: [{ type: 'tool_use', input: { file_path: p } }] } }).message.content[0].input.file_path;
  const a = input('D:\\rsd\\_wt\\x\\todo\\PLAN_private_name.md');
  assert.match(a, /^D:\\fixture\\repo-a\\p\d+\\p\d+\.md$/, 'root rewritten, backslash style kept, the file name is gone');
  const b = input('d:/rsd/_wt/x/todo/PLAN_private_name.md');
  assert.equal(b.split('/').slice(-2).join('/'), a.split(String.fromCharCode(92)).slice(-2).join('/'), 'same names, same p<n>, case-insensitively on a Windows prefix');
  const shaped = input('D:/rsd/_wt/x/src/Alice.Secret.cs');
  assert.match(shaped, /^D:\/fixture\/repo-a\/p\d+\/p\d+\.cs$/, 'the extension is kept, the stem is not');
  assert.match(input('D:/rsd/_wt/x/notes.verylongextension'), /^D:\/fixture\/repo-a\/p\d+$/, 'a long extension is dropped with the name');
  assert.equal(input('D:/rsd/_wt/x/src/B.CS').split('/').pop().split('.').pop(), 'cs', 'extensions are folded to lower case');
  assert.match(input('/home/someone/src/y/lib/z.rs'), /^\/fixture\/repo-b\/p\d+\/p\d+\.rs$/);
  assert.equal(input('D:/rsd/_wt/x'), 'D:/fixture/repo-a', 'the root itself');
  assert.equal(input('/HOME/someone/src/y/lib/z.rs').startsWith('/fixture/other/p'), true, 'a POSIX prefix is case-sensitive');
  assert.equal(s.sanitiseValue({ cwd: 'D:\\rsd\\_wt\\x' }).cwd, 'D:\\fixture\\repo-a');
  for (const p of [a, shaped, input('/home/someone/src/y/MARKER_PRIVATE_FILE.txt')]) assert.ok(!p.includes('MARKER') && !/private|Alice|Secret|PLAN/.test(p), p);
});

test('theSameSegmentIsTheSameNumberEverywhereInTheFixtureAndDifferentSegmentsDiffer', () => {
  const s = sanitiser();
  const f = (p) => s.sanitiseValue({ cwd: p }).cwd;
  const one = f('D:/rsd/_wt/x/src/Alpha.cs');
  const two = f('d:/RSD/_WT/X/SRC/alpha.cs');
  assert.equal(one.split('/').slice(-2).join('/'), two.split('/').slice(-2).join('/'), 'src/Alpha.cs maps the same whatever its case, under a Windows prefix');
  assert.notEqual(f('D:/rsd/_wt/x/src/Beta.cs'), one);
  for (const part of one.replace('D:/fixture/repo-a/', '').split('/')) assert.match(part, /^p\d+(\.[a-z0-9]{1,5})?$/);
});

test('anUnmappedOrEscapingPathLandsUnderTheOtherRootAndNeverKeepsTheOriginal', () => {
  const s = sanitiser();
  const f = (p) => s.sanitiseValue({ cwd: p }).cwd;
  const first = f('C:\\Users\\alice\\secret\\x.cs');
  assert.match(first, /^\/fixture\/other\/p\d+$/);
  assert.equal(f('C:\\Users\\alice\\secret\\x.cs'), first, 'the same original maps to the same replacement');
  assert.notEqual(f('C:\\Users\\alice\\secret\\y.cs'), first);
  assert.match(f('D:/rsd/_wt/x/../../etc/passwd'), /^\/fixture\/other\/p\d+$/, 'a ".." in the remainder is not trusted');
  assert.match(f('D:/rsd/_wt/xylophone/a.cs'), /^\/fixture\/other\/p\d+$/, 'a prefix must end at a separator');
  const rel = f('src/Alice/secret.txt');
  assert.match(rel, /^p\d+\/p\d+\/p\d+\.txt$/);
  assert.equal(f('src/Alice/other.txt').split('/')[0], rel.split('/')[0], 'segments are replaced consistently');
  assert.ok(!/alice|secret|Users/i.test(JSON.stringify([first, rel])));
  assert.equal(f(''), SENTINEL);
});

test('aToolNameSurvivesOnlyFromTheClosedBuiltinListAndMcpNamesAreRenumbered', () => {
  const s = sanitiser();
  const name = (n) => s.sanitiseValue({ message: { content: [{ type: 'tool_use', name: n }] } }).message.content[0].name;
  assert.equal(name('Bash'), 'Bash');
  assert.equal(name('Agent'), 'Agent');
  assert.equal(name('mcp__MARKER_SERVER__MARKER_TOOL'), 'mcp__s1__t1');
  assert.equal(name('mcp__MARKER_SERVER__other_tool'), 'mcp__s1__t2');
  assert.equal(name('mcp__second-server__MARKER_TOOL'), 'mcp__s2__t1');
  assert.equal(name('mcp__MARKER_SERVER__MARKER_TOOL'), 'mcp__s1__t1', 'consistent');
  assert.equal(name('MARKER_PrivatePluginTool'), SENTINEL, 'not builtin, not mcp: text');
  assert.ok(s.stats().shapeFailures['message.content[].name'] >= 1);
});

test('anAgentTypeSurvivesOnlyWhenBuiltinAndAnyOtherIsCustomNumbered', () => {
  const s = sanitiser();
  const type = (n) => s.sanitiseValue({ agentType: n }).agentType;
  assert.equal(type('general-purpose'), 'general-purpose');
  assert.equal(type('Explore'), 'Explore');
  assert.equal(type('MARKER_CUSTOM_AGENT'), 'custom-1');
  assert.equal(type('feature-dev:code-architect'), 'custom-2');
  assert.equal(type('MARKER_CUSTOM_AGENT'), 'custom-1');
  assert.equal(s.sanitiseValue({ message: { content: [{ input: { subagent_type: 'MARKER_CUSTOM_AGENT' } }] } }).message.content[0].input.subagent_type, 'custom-1', 'one map for the meta and the Agent call');
});

// ---- the cutter -----------------------------------------------------------------------------------------------------
test('aPathInAFileRewritesUnderTheRepositoryRootsAndTheOutputSlugIsFixtureDerived', () => {
  const { outDir } = cut(writeSession(scratch('in')));
  const files = readAll(outDir);
  const main = files.find((f) => /^projects\/[^/]+\/[^/]+\.jsonl$/.test(f.file));
  assert.match(main.file, /^projects\/D--fixture-repo-a\//, 'the slug is derived from the rewritten cwd');
  const cwds = new Set(files.filter((f) => f.file.endsWith('.jsonl')).flatMap((f) => jsonLines(f.text)).map((l) => l.cwd).filter(Boolean));
  assert.deepEqual([...cwds].sort(compareOrdinal), ['/fixture/repo-b', 'D:\\fixture\\repo-a']);
  const edit = jsonLines(main.text).flatMap((l) => l.message?.content ?? []).find((c) => c.name === 'Edit');
  assert.match(edit.input.file_path, /^D:\\fixture\\repo-a\\p\d+\\p\d+\.cs$/);
});

test('theSameInputCutsToTheSameBytes', () => {
  const src = writeSession(scratch('in'));
  const one = readAll(cut(src).outDir);
  const two = readAll(cut(src).outDir);
  assert.deepEqual(one, two);
  const other = readAll(cut(src, { salt: 'a-different-salt' }).outDir);
  assert.notDeepEqual(other.map((f) => f.file), one.map((f) => f.file), 'a different salt yields different ids');
});

test('theOutputIsLfOnlyEvenFromACrlfInputAndOneLinePerInputLine', () => {
  const { outDir } = cut(writeSession(scratch('in'), { crlf: true }));
  for (const { file, text } of readAll(outDir)) {
    assert.ok(!text.includes('\r'), `${file} carries a CR`);
    assert.ok(text.endsWith('\n'), `${file} does not end with LF`);
    if (file.endsWith('.jsonl')) assert.ok(!/\n\n/.test(text));
  }
  const main = readAll(outDir).find((f) => /^projects\/[^/]+\/[^/]+\.jsonl$/.test(f.file));
  assert.equal(main.text.split('\n').length - 1, mainLines().length);
  assert.ok(!main.text.includes(': ') && !main.text.includes(', '), 'JSON.stringify without spaces');
});

test('aWindowKeepsMainLinesInsideItAndTheAgentFilesItLaunchedWhole', () => {
  const src = writeSession(scratch('in'), { lateAgentLine: true });
  const { outDir, summary } = cut(src, { from: T(0), to: T(10) });
  const files = readAll(outDir);
  const main = files.find((f) => /^projects\/[^/]+\/[^/]+\.jsonl$/.test(f.file));
  assert.equal(jsonLines(main.text).length, 5, 'the minute-30 line is outside the window');
  const agentFiles = files.filter((f) => f.file.endsWith('.jsonl') && f.file.includes('subagents'));
  assert.equal(agentFiles.length, 2, 'A and its nested child; B launched at minute 30 is out');
  assert.equal(files.filter((f) => f.file.endsWith('.meta.json')).length, 2);
  assert.equal(summary.joinBreaks, 0);
});

test('aResumedAgentKeepsItsLaterLines', () => {
  const src = writeSession(scratch('in'), { lateAgentLine: true });
  const { outDir } = cut(src, { from: T(0), to: T(10) });
  const withLate = readAll(outDir).filter((f) => f.file.includes('subagents') && f.file.endsWith('.jsonl'))
    .map((f) => jsonLines(f.text)).find((ls) => ls.length === 3);
  assert.ok(withLate, 'the agent launched in the window keeps the line it wrote at minute 55');
  const stamps = withLate.map((l) => l.timestamp);
  assert.equal(stamps.at(-1), [...stamps].sort(compareOrdinal).at(-1), 'the resumed line is the last, on the synthetic clock');
  assert.ok(stamps.every((t) => t.startsWith('2000-01-01')));
});

test('anAgentWhoseFirstLineIsInTheWindowIsKeptEvenWhenItsLaunchIsNot', () => {
  const src = writeSession(scratch('in'));
  const { outDir, summary } = cut(src, { from: T(5), to: T(7) });
  const files = readAll(outDir);
  assert.equal(files.filter((f) => f.file.includes('subagents') && f.file.endsWith('.jsonl')).length, 2);
  assert.ok(summary.joinBreaks >= 1, 'their Agent tool_use lines are outside the window: the break is reported, not hidden');
});

test('aDroppedAgentFileLeavesItsMetaAndADroppedMetaLeavesItsFile', () => {
  const src = writeSession(scratch('in'));
  const { outDir, summary } = cut(src, { dropAgentFiles: [AGENT_B], dropMetas: [AGENT_NESTED] });
  const names = readAll(outDir).map((f) => f.file).filter((f) => f.includes('subagents'));
  assert.equal(names.filter((n) => n.endsWith('.jsonl')).length, 2);
  assert.equal(names.filter((n) => n.endsWith('.meta.json')).length, 2);
  const mapped = (id) => sanitiser().sanitiseValue({ agentId: id }).agentId;
  assert.ok(!names.some((n) => n.includes(mapped(AGENT_B)) && n.endsWith('.jsonl')));
  assert.ok(names.some((n) => n.includes(mapped(AGENT_B)) && n.endsWith('.meta.json')));
  assert.ok(names.some((n) => n.includes(mapped(AGENT_NESTED)) && n.endsWith('.jsonl')));
  assert.ok(!names.some((n) => n.includes(mapped(AGENT_NESTED)) && n.endsWith('.meta.json')));
  assert.equal(summary.joinBreaks, 0, 'the meta still joins its launching tool_use');
});

test('agentFilesAreDiscoveredInOrdinalOrderSoTheirPathNumbersDoNotDependOnTheDirectory', () => {
  // two agents created in the REVERSE of their id order: the one whose id sorts first must meet its path segments first (lower p<n>)
  const src = writeSession(scratch('in'));
  const sub = join(dirname(src), SESSION, 'subagents');
  const write = (id, file) => writeFileSync(join(sub, `agent-${id}.jsonl`), jl([{ type: 'assistant', timestamp: T(5), agentId: id,
    message: { id: `msg_${id}`, role: 'assistant', content: [{ type: 'tool_use', id: `toolu_${id}`, name: 'Write', input: { file_path: `D:/rsd/_wt/x/${file}.md` } }] } }]));
  write(AGENT_B, 'second-file');
  write(AGENT_A, 'first-file');
  const { outDir } = cut(src);
  const numberIn = (id) => Number(/\/p(\d+)\.md/.exec(readAll(outDir).find((f) => f.file.endsWith(`agent-${sanitiser().sanitiseValue({ agentId: id }).agentId}.jsonl`)).text)?.[1]);
  assert.ok(numberIn(AGENT_A) < numberIn(AGENT_B), 'aaaa… sorts before bbbb…, so its file is met first');
});

test('clipAgentsKeepsOnlyTheWindowsLinesOfEveryAgentFileAndTheDefaultKeepsThemWhole', () => {
  const src = writeSession(scratch('in'), { lateAgentLine: true });
  const linesOf = (outDir) => readAll(outDir).filter((f) => f.file.includes('subagents') && f.file.endsWith('.jsonl')).map((f) => jsonLines(f.text));
  assert.ok(linesOf(cut(src, { from: T(0), to: T(10) }).outDir).some((ls) => ls.length === 3), 'whole by default: the minute-55 line is kept');
  const clipped = linesOf(cut(src, { from: T(0), to: T(10), clipAgents: true }).outDir);
  assert.equal(clipped.length, 2);
  assert.ok(clipped.every((ls) => ls.length === 2), 'the minute-55 line is outside the window');
});

// ---- a kept write call keeps its answer -------------------------------------------------------------------------------------
const answerSession = () => {
  const dir = join(scratch('ans'), 'projects', 'D--rsd--wt-MARKER_SLUG');
  mkdirSync(join(dir, SESSION, 'subagents'), { recursive: true });
  const call = (id, name, input) => ({ type: 'tool_use', id, name, input });
  const assistant = (at, id, content) => ({ type: 'assistant', timestamp: T(at), sessionId: SESSION, cwd: 'D:/rsd/_wt/x', isSidechain: false, uuid: uid(at * 10),
    message: { id: 'msg_01Answer' + id, model: 'claude-opus-4-1', role: 'assistant', content, usage: { input_tokens: 1, output_tokens: 1, cache_creation_input_tokens: 0, cache_read_input_tokens: 0 } } });
  const answer = (at, ids) => ({ type: 'user', timestamp: T(at), sessionId: SESSION, cwd: 'D:/rsd/_wt/x', isSidechain: false, uuid: uid(at * 10 + 1),
    message: { role: 'user', content: ids.map((id) => ({ type: 'tool_result', tool_use_id: id, content: 'MARKER_RESULT' })) } });
  const main = [
    assistant(1, 'a', [call('toolu_01WriteCallAAAAAAAAAA', 'Write', { file_path: 'D:/rsd/_wt/x/a.md', content: 'MARKER_PROMPT' }), call('toolu_01BashCallBBBBBBBBBBB', 'Bash', { command: 'MARKER_CMD' }),
      call('toolu_01EditCallCCCCCCCCCCC', 'Edit', { file_path: 'D:/rsd/_wt/x/c.md', old_string: 'MARKER_CMD' })]),
    answer(5, ['toolu_01WriteCallAAAAAAAAAA', 'toolu_01BashCallBBBBBBBBBBB']),
    assistant(6, 'b', [{ type: 'text', text: 'MARKER_PROMPT later' }]),
    answer(7, ['toolu_01EditCallCCCCCCCCCCC'])];
  writeFileSync(join(dir, SESSION + '.jsonl'), jl(main));
  return join(dir, SESSION + '.jsonl');
};
const toolResultIds = (text) => jsonLines(text).flatMap((l) => (Array.isArray(l.message?.content) ? l.message.content : [])).filter((c) => c.type === 'tool_result').map((c) => c.tool_use_id);

test('aKeptWriteCallKeepsTheLineThatAnswersItEvenWhenTheWindowEndsFirst', () => {
  const src = answerSession();
  const { outDir } = cut(src, { from: T(0), to: T(2) }); // the window holds only the calls
  const lines = jsonLines(readAll(outDir).find((f) => /^projects\/[^/]+\/[^/]+\.jsonl$/.test(f.file)).text);
  assert.equal(lines.length, 3, 'the call line plus the two lines that answer its Write and its Edit; the later text line is not kept');
  const ids = toolResultIds(readAll(outDir).find((f) => /^projects\/[^/]+\/[^/]+\.jsonl$/.test(f.file)).text);
  assert.equal(ids.length, 3, 'the Write, the Bash (it shares the answering line) and the Edit are all answered in the cut');
  assert.ok(lines.every((l) => l.type !== 'assistant' || l.message.content.every((c) => c.type !== 'text')), 'the assistant text line outside the window is dropped');
});

test('aWriteCallOutsideTheWindowIsNotKeptSoItsAnswerIsNotPulledIn', () => {
  const src = answerSession();
  const { outDir } = cut(src, { from: T(5), to: T(8) });
  const text = readAll(outDir).find((f) => /^projects\/[^/]+\/[^/]+\.jsonl$/.test(f.file)).text;
  assert.equal(jsonLines(text).length, 3, 'only the lines inside the window; the call line at minute 1 stays out, and with it nothing is pinned');
});

test('anAnswerThatIsAlreadyInsideTheWindowIsKeptOnceAndNothingElseMoves', () => {
  const src = answerSession();
  const { outDir } = cut(src, { from: T(0), to: T(9) });
  assert.equal(jsonLines(readAll(outDir).find((f) => /^projects\/[^/]+\/[^/]+\.jsonl$/.test(f.file)).text).length, 4);
});

test('aClippedAgentFileKeepsTheAnswerToItsWriteToo', () => {
  const src = writeSession(scratch('in'));
  const agent = join(dirname(src), SESSION, 'subagents', 'agent-' + AGENT_A + '.jsonl');
  const lines = readFileSync(agent, 'utf8').split('\n').filter(Boolean).map((l) => JSON.parse(l));
  const write = { ...lines[1], timestamp: T(5, 10), uuid: uid(9001), message: { ...lines[1].message, id: 'msg_01AgentWriteLine', content: [{ type: 'tool_use', id: 'toolu_01AgentWriteAAAAAAAAA', name: 'Write', input: { file_path: 'D:/rsd/_wt/x/w.md', content: 'MARKER_PROMPT' } }] } };
  const result = { ...lines[0], timestamp: T(8), uuid: uid(9002), message: { role: 'user', content: [{ type: 'tool_result', tool_use_id: 'toolu_01AgentWriteAAAAAAAAA', content: 'MARKER_RESULT' }] } };
  writeFileSync(agent, jl([...lines, write, result]));
  const { outDir } = cut(src, { from: T(0), to: T(7), clipAgents: true });
  const out = readAll(outDir).filter((f) => f.file.includes('subagents') && f.file.endsWith('.jsonl')).map((f) => jsonLines(f.text));
  const withWrite = out.find((ls) => ls.some((l) => (Array.isArray(l.message?.content) ? l.message.content : []).some((c) => c.type === 'tool_use' && c.name === 'Write')));
  assert.ok(withWrite, 'the agent file with the write is cut');
  assert.ok(withWrite.some((l) => (Array.isArray(l.message?.content) ? l.message.content : []).some((c) => c.type === 'tool_result')), 'its answer at minute 8, outside the window, is kept');
});

test('aClippedCutKeepsTheLaunchLineOfAnAgentWhoseCallIsOutsideTheWindow', () => {
  const src = writeSession(scratch('in'));
  const plain = cut(src, { from: T(5), to: T(7) });
  assert.ok(plain.summary.joinBreaks >= 1, 'without the option the break is reported');
  const { outDir, summary } = cut(src, { from: T(5), to: T(7), clipAgents: true });
  const main = readAll(outDir).find((f) => /^projects\/[^/]+\/[^/]+\.jsonl$/.test(f.file));
  assert.equal(jsonLines(main.text).length, 1, 'only the line that launched agent A, from minute 2');
  assert.equal(summary.joinBreaks, 0);
});

test('includeAgentPullsInItsWholeChainOfParentsAndTheirLaunchLines', () => {
  const src = writeSession(scratch('in'));
  const agentFiles = (outDir) => readAll(outDir).filter((f) => f.file.includes('subagents') && f.file.endsWith('.jsonl'));
  assert.equal(agentFiles(cut(src, { from: T(6), to: T(8), clipAgents: true }).outDir).length, 1, 'only the nested child starts in the window');
  const { outDir, summary } = cut(src, { from: T(6), to: T(8), clipAgents: true, includeAgents: [AGENT_NESTED] });
  assert.equal(agentFiles(outDir).length, 2, 'the nested child and its parent A');
  assert.equal(readAll(outDir).filter((f) => f.file.endsWith('.meta.json')).length, 2);
  assert.equal(summary.joinBreaks, 0, 'A is launched from the main file (kept as a pinned line), the child from A');
});

test('aTornLineIsDroppedNeverCopiedAsText', () => {
  const root = scratch('in');
  const src = writeSession(root);
  writeFileSync(src, readFileSync(src, 'utf8') + '{"type":"assistant","message":{"content":"MARKER_PROMPT torn');
  const { outDir, summary } = cut(src);
  assert.equal(summary.tornLines, 1);
  for (const { text } of readAll(outDir)) assert.ok(!text.includes('MARKER') && !text.includes('torn'));
});

test('unknownKeysAreSentinelledAndCountedByTheirParentPath', () => {
  const { summary } = cut(writeSession(scratch('in')));
  assert.ok(summary.unknownKeys['gitBranch'] >= 2, JSON.stringify(summary.unknownKeys));
  assert.ok(summary.unknownKeys['message.stop_reason'] >= 1);
  assert.ok(!Object.keys(summary.unknownKeys).some((k) => k.includes('secret.txt')), 'a non-identifier key name is never reported');
});

// ---- the CLI --------------------------------------------------------------------------------------------------------
const runCli = (args) => spawnSync(process.execPath, [CLI, ...args], { encoding: 'utf8' });

// ---- expected failures are VALUES, not exceptions --------------------------------------------------------------------------
const cutArgs = (over = {}) => ({ outDir: scratch('o'), allowlist, salt: 'salt-one', repos: REPOS, otherRoot: '/fixture/other', ...over });

test('aRefusedCutIsAReturnedValueNotAThrow', () => {
  const src = writeSession(scratch('in'));
  const notASession = join(scratch('x'), 'not-a-session.jsonl');
  assert.doesNotThrow(() => cutSession({ sessionFile: notASession, ...cutArgs() }));
  const bad = cutSession({ sessionFile: notASession, ...cutArgs() });
  assert.equal(bad.ok, false);
  assert.match(bad.refusal, /<sessionId>\.jsonl/);
  const full = scratch('full');
  writeFileSync(join(full, 'f'), 'x');
  const notEmpty = cutSession({ sessionFile: src, ...cutArgs({ outDir: full }) });
  assert.equal(notEmpty.ok, false);
  assert.match(notEmpty.refusal, /already holds files/);
  const mapping = sanitise.parseRepoMapping('nonsense');
  assert.equal(mapping.ok, false);
  assert.match(mapping.refusal, /<real-prefix>=<fixture-root>/);
  assert.equal(sanitise.parseRepoMapping('D:/rsd/_wt/x=D:/fixture/repo-a').ok, true);
  assert.equal(sanitise.checkOutputAllowed(scratch('elsewhere'), join(scratch('allowed'), 'root')).ok, false);
  assert.equal(cutSession({ sessionFile: src, ...cutArgs() }).ok, true);
});

// ---- the destination roots are the synthetic policy, nothing the caller types ----------------------------------------------
test('aDestinationRootOutsideTheSyntheticPolicyIsRefusedBeforeAnyInputIsRead', () => {
  const absent = join(scratch('empty'), `${SESSION}.jsonl`); // reading it would throw ENOENT: a refusal proves nothing was read
  for (const to of ['C:\\Users\\alice\\project', '/home/alice', 'D:/fixture/repo-a/../../x', 'D:/fixture/repo-ab', 'D:/fixture/Other', 'D:/fixture/repo-a/p1', 'D:/fixture']) {
    const r = cutSession({ sessionFile: absent, ...cutArgs({ repos: [{ from: 'D:/rsd/_wt/x', to }] }) });
    assert.equal(r.ok, false, to);
    assert.match(r.refusal, /fixture root/);
    assert.ok(!r.refusal.includes('alice'), 'the refusal does not echo what was typed');
  }
  const other = cutSession({ sessionFile: absent, ...cutArgs({ otherRoot: 'C:\\Users\\alice\\other' }) });
  assert.equal(other.ok, false);
  assert.match(other.refusal, /fixture root/);
  for (const to of ['D:/fixture/repo-a', 'D:\\fixture\\repo-z', '/fixture/other', '/fixture/scratch', 'D:/fixture/scratch/'])
    assert.equal(sanitise.checkFixtureRoot?.(to).ok, true, to);
});

test('theCliRefusesAForeignDestinationRootWithExit65BeforeReadingTheSession', () => {
  const absent = join(scratch('empty'), `${SESSION}.jsonl`);
  for (const flags of [['--repo', 'D:/rsd/_wt/x=C:\\Users\\alice\\project'], ['--other-root', 'C:\\Users\\alice\\other']]) {
    const r = runCli(['--session-file', absent, '--out', scratch('o'), '--out-anywhere', '--salt', '0123456789abcdef0123456789abcdef-test-salt', ...flags]);
    assert.equal(r.status, 65, r.stderr);
    assert.match(r.stderr, /fixture root/);
    assert.ok(!r.stderr.includes('alice'));
  }
});

// ---- the input size limit ---------------------------------------------------------------------------------------------------
const bigSession = () => {
  const dir = scratch('big');
  const file = join(dir, `${SESSION}.jsonl`);
  writeFileSync(file, `${JSON.stringify({ type: 'user', timestamp: T(1), pad: 'x'.repeat(1_500_000) })}\n`);
  return file;
};

test('anInputOverTheSizeLimitIsRefusedBeforeItIsReadAndTheLimitIsDocumented', () => {
  const src = bigSession();
  const out = scratch('o');
  const r = cutSession({ sessionFile: src, ...cutArgs({ outDir: out, maxInputBytes: 1024 * 1024 }) });
  assert.equal(r.ok, false);
  assert.match(r.refusal, /1\.\d MiB .*limit of 1 MiB/);
  assert.deepEqual(readdirSync(out), [], 'nothing was written');
  assert.equal(sanitise.DEFAULT_MAX_INPUT_BYTES, 256 * 1024 * 1024, 'the documented default: above the 229 MB session cut so far, below what Node can parse in its default heap');
  assert.equal(cutSession({ sessionFile: src, ...cutArgs({ outDir: scratch('o2') }) }).ok, true, 'the default admits it');
});

test('theAgentFilesCountTowardTheSizeLimitAndTheCliTakesItInMiB', () => {
  const src = writeSession(scratch('in'));
  const agent = join(dirname(src), SESSION, 'subagents', `agent-${AGENT_A}.jsonl`);
  writeFileSync(agent, readFileSync(agent, 'utf8') + `${JSON.stringify({ type: 'user', timestamp: T(5), pad: 'x'.repeat(1_500_000) })}\n`);
  const base = ['--session-file', src, '--out-anywhere', '--salt', '0123456789abcdef0123456789abcdef-test-salt', '--repo', 'D:/rsd/_wt/x=D:/fixture/repo-a'];
  const refused = runCli([...base, '--out', scratch('o'), '--max-input-mb', '1']);
  assert.equal(refused.status, 65, refused.stderr);
  assert.match(refused.stderr, /MiB/);
  assert.equal(runCli([...base, '--out', scratch('o')]).status, 0);
  assert.equal(runCli([...base, '--out', scratch('o'), '--max-input-mb', 'lots']).status, 2, 'a usage error');
});

// ---- the catch-all names the failure without a path ------------------------------------------------------------------------
test('aKnownFailureIsExplainedInASafeSentenceWithAnExitCode', () => {
  const explain = sanitise.explainFailure;
  assert.equal(explain({ code: 'ENOENT' }).exit, 74);
  assert.match(explain({ code: 'ENOENT' }).sentence, /does not exist/);
  assert.match(explain({ code: 'EACCES' }).sentence, /permission denied/);
  assert.match(explain({ code: 'EPERM' }).sentence, /permission denied/);
  assert.match(explain({ code: 'EISDIR' }).sentence, /directory/);
  assert.match(explain({ code: 'ENOSPC' }).sentence, /disk is full/);
  assert.equal(explain(new SyntaxError('Unexpected token \'x\' in C:\\Users\\alice\\f.json')).exit, 65);
  assert.match(explain(new SyntaxError('boom')).sentence, /not valid JSON/);
  const unknown = explain(Object.assign(new RangeError('secret C:\\Users\\alice'), { code: undefined }));
  assert.match(unknown.sentence, /unexpected RangeError/);
  assert.ok(!unknown.sentence.includes('alice'), 'an error message never reaches the operator\'s terminal');
});

test('theCliExplainsAMissingSessionAnInvalidSpecAndADirectoryWithoutAPath', () => {
  const dir = scratch('cli');
  const flags = ['--out', scratch('o'), '--out-anywhere', '--salt', '0123456789abcdef0123456789abcdef-test-salt'];
  const missing = runCli(['--session-file', join(dir, `${SESSION}.jsonl`), ...flags]);
  assert.equal(missing.status, 74);
  assert.match(missing.stderr, /does not exist/);
  assert.ok(!missing.stderr.includes('testsToDelete') && !missing.stderr.includes(dir), 'no path');
  const spec = join(dir, 'spec.json');
  writeFileSync(spec, '{ not json at all');
  const invalid = runCli(['--case-spec', spec, ...flags]);
  assert.equal(invalid.status, 65, invalid.stderr);
  assert.match(invalid.stderr, /not valid JSON/);
  assert.ok(!invalid.stderr.includes(dir) && !invalid.stderr.includes('not json at all'));
  mkdirSync(join(dir, `${OTHER_SESSION}.jsonl`));
  const isDir = runCli(['--session-file', join(dir, `${OTHER_SESSION}.jsonl`), ...flags]);
  assert.equal(isDir.status, 74);
  assert.match(isDir.stderr, /directory/);
});

test('theCliRefusesAShortSaltBecauseAGuessableSaltLetsAnOriginalIdBeConfirmed', () => {
  const src = writeSession(scratch('in'));
  for (const salt of ['s', 'x'.repeat(31)]) {
    const r = runCli(['--session-file', src, '--out', scratch('out'), '--out-anywhere', '--salt', salt]);
    assert.equal(r.status, 2, 'a usage error');
    assert.match(r.stderr, /at least 32 characters/);
  }
  assert.equal(runCli(['--session-file', src, '--out', scratch('out'), '--out-anywhere', '--salt', 'x'.repeat(32)]).status, 0);
});

test('theCliRefusesAnOutputDirectoryOutsideTheFixtureFolderWithExit65', () => {
  const src = writeSession(scratch('in'));
  const out = join(scratch('refused'), 'case');
  const r = runCli(['--session-file', src, '--out', out, '--salt', '0123456789abcdef0123456789abcdef-test-salt', '--repo', 'D:/rsd/_wt/x=D:/fixture/repo-a']);
  assert.equal(r.status, 65, r.stderr);
  assert.deepEqual(readdirSync(dirname(out)), [], 'nothing was written');
});

test('theCliCutsWithOutAnywhereAndPrintsCountsOnlyAndRefusesANonEmptyOutput', () => {
  const src = writeSession(scratch('in'));
  const out = join(scratch('anywhere'), 'case');
  const args = ['--session-file', src, '--out', out, '--out-anywhere', '--salt', '0123456789abcdef0123456789abcdef-test-salt', '--repo', 'D:/rsd/_wt/x=D:/fixture/repo-a',
    '--repo', '/home/someone/src/y=/fixture/repo-b', '--from', T(0), '--to', T(10), '--drop-meta', AGENT_NESTED];
  const r = runCli(args);
  assert.equal(r.status, 0, r.stderr);
  const summary = JSON.parse(r.stdout);
  assert.equal(summary.agentFiles, 2);
  assert.equal(summary.metas, 1);
  assert.ok(!r.stdout.includes('MARKER') && !r.stderr.includes('MARKER') && !r.stderr.includes('secret'));
  assert.equal(runCli(args).status, 65, 'cutting into a folder that already holds files');
  assert.equal(runCli(['--out-anywhere']).status, 2, 'usage');
});

test('theCliRefusesAnOutputDirectoryThatEscapesThroughDotDot', () => {
  const src = writeSession(scratch('in'));
  const sneaky = `${join(here, '..', '..', 'src_mcp', 'tests', 'fixtures', 'caller-transcripts')}/../escape-case`;
  const r = runCli(['--session-file', src, '--out', sneaky, '--salt', '0123456789abcdef0123456789abcdef-test-salt']);
  assert.equal(r.status, 65, r.stderr);
});

// ---- case spec: rounds.json and the registry entry go through the same sanitiser -----------------------------------------
const CASE_SPEC = () => ({
  rounds: { callerSessionId: SESSION, gateSessions: [{ id: '99999999-2222-4333-8444-555555555555', repoPath: 'D:/rsd/_wt/x', openedUtc: T(0, 5),
    rounds: [{ number: 1, startedUtc: T(2, 30), completedUtc: T(40), status: 'done', headSha: 'f'.repeat(40) }] }] },
  registry: { pid: 4242, sessionId: SESSION, cwd: 'D:/rsd/_wt/x', version: '2.1.289' }
});

test('aCaseSpecWritesRoundsAndRegistryOnTheSameClockAndUnderTheSameIdMap', () => {
  const src = writeSession(scratch('in'));
  const { outDir } = cut(src, { caseSpec: CASE_SPEC() });
  const files = readAll(outDir);
  const rounds = JSON.parse(files.find((f) => f.file === 'rounds.json').text);
  const registry = JSON.parse(files.find((f) => f.file === 'sessions/4242.json').text);
  const mainName = /^projects\/[^/]+\/([^/]+)\.jsonl$/.exec(files.find((f) => /^projects\/[^/]+\/[^/]+\.jsonl$/.test(f.file)).file)[1];
  assert.equal(rounds.callerSessionId, mainName, 'the caller id is the transcript\'s mapped id');
  assert.equal(registry.sessionId, mainName);
  assert.equal(registry.cwd, 'D:/fixture/repo-a');
  const main = jsonLines(files.find((f) => /^projects\/[^/]+\/[^/]+\.jsonl$/.test(f.file)).text);
  const stamps = main.map((l) => l.timestamp).filter((t) => t !== undefined && t !== SENTINEL);
  const round = rounds.gateSessions[0].rounds[0];
  assert.ok(round.startedUtc < round.completedUtc && rounds.gateSessions[0].openedUtc < round.startedUtc);
  assert.ok([round.startedUtc, round.completedUtc, rounds.gateSessions[0].openedUtc, ...stamps].every((t) => t.startsWith('2000-01-01T00:')), 'one synthetic clock for both');
  assert.ok(stamps.some((t) => t > round.startedUtc) && stamps.some((t) => t < round.startedUtc), 'the round still sits between transcript lines');
  assert.ok(!files.some((f) => /2026-10|ffffffff/.test(f.text)), 'no real instant and no real sha survives');
  assert.match(round.headSha, /^[0-9a-f]{40}$/);
});

test('aCaseWithoutATranscriptIsJustItsRoundsAndRegistryThroughTheSanitiser', () => {
  const out = scratch('nt');
  const cli = spawnSync(process.execPath, [CLI, '--case-spec', writeSpec(CASE_SPEC()), '--out', out, '--salt', '0123456789abcdef0123456789abcdef-test-salt', '--repo', 'D:/rsd/_wt/x=D:/fixture/repo-a', '--out-anywhere'], { encoding: 'utf8' });
  assert.equal(cli.status, 0, cli.stderr);
  assert.deepEqual(readAll(out).map((f) => f.file), ['rounds.json', 'sessions/4242.json']);
  assert.ok(!readAll(out).some((f) => f.text.includes('2026')));
});

function writeSpec(spec) {
  const file = join(scratch('spec'), 'spec.json');
  writeFileSync(file, JSON.stringify(spec));
  return file;
}
