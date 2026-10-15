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
  const summary = cutSession({ sessionFile, outDir, allowlist, salt: 'salt-one', repos: REPOS, otherRoot: '/fixture/other',
    quiet: true, ...overrides });
  return { outDir, summary };
}

function walk(dir) {
  return readdirSync(dir, { withFileTypes: true, recursive: true }).filter((e) => e.isFile())
    .map((e) => join(e.parentPath ?? e.path, e.name)).sort();
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

test('aNumberIsKeptOnlyAtAnIntPathAndEveryOtherNumberIsZero', () => {
  const out = sanitiser().sanitiseValue({ parentUuid: null, isSidechain: true, durationMs: 12, ratio: 0.5, nope: false, note: '', other: ['x', 3, { a: 'b' }],
    message: { usage: { input_tokens: 5, service_tier: 'MARKER_TIER', server_tool_use: { n: 9 } } }, spawnDepth: 2, effort: 3 });
  assert.deepEqual(out, { parentUuid: null, isSidechain: true, k1: 0, k2: 0, k3: false, k4: SENTINEL, k5: [SENTINEL, 0, { k1: SENTINEL }],
    message: { usage: { input_tokens: 5, k1: SENTINEL, k2: { k1: 0 } } }, spawnDepth: 2, effort: 0 });
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
  assert.deepEqual([...cwds].sort(), ['/fixture/repo-b', 'D:\\fixture\\repo-a']);
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
  assert.ok(withLate.some((l) => l.timestamp === T(55)));
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

test('clipAgentsKeepsOnlyTheWindowsLinesOfEveryAgentFileAndTheDefaultKeepsThemWhole', () => {
  const src = writeSession(scratch('in'), { lateAgentLine: true });
  const linesOf = (outDir) => readAll(outDir).filter((f) => f.file.includes('subagents') && f.file.endsWith('.jsonl')).map((f) => jsonLines(f.text));
  assert.ok(linesOf(cut(src, { from: T(0), to: T(10) }).outDir).some((ls) => ls.some((l) => l.timestamp === T(55))), 'whole by default');
  const clipped = linesOf(cut(src, { from: T(0), to: T(10), clipAgents: true }).outDir);
  assert.equal(clipped.length, 2);
  assert.ok(clipped.every((ls) => ls.length === 2 && ls.every((l) => l.timestamp !== T(55))), 'the minute-55 line is outside the window');
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

test('theCliRefusesAnOutputDirectoryOutsideTheFixtureFolderWithExit65', () => {
  const src = writeSession(scratch('in'));
  const out = join(scratch('refused'), 'case');
  const r = runCli(['--session-file', src, '--out', out, '--salt', 's', '--repo', 'D:/rsd/_wt/x=D:/fixture/repo-a']);
  assert.equal(r.status, 65, r.stderr);
  assert.deepEqual(readdirSync(dirname(out)), [], 'nothing was written');
});

test('theCliCutsWithOutAnywhereAndPrintsCountsOnlyAndRefusesANonEmptyOutput', () => {
  const src = writeSession(scratch('in'));
  const out = join(scratch('anywhere'), 'case');
  const args = ['--session-file', src, '--out', out, '--out-anywhere', '--salt', 's', '--repo', 'D:/rsd/_wt/x=D:/fixture/repo-a',
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
  const r = runCli(['--session-file', src, '--out', sneaky, '--salt', 's']);
  assert.equal(r.status, 65, r.stderr);
});
