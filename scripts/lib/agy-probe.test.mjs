// node --test scripts/lib/agy-probe.test.mjs — the pure parts of scripts/probe-agy-review-follow-up.mjs (a PAID probe: the probe
// itself is never run by a test). Argument validation, the stream reader, the launch classification, the auth/quota stop
// sentence, the console progress lines and the fixture's path redaction.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { absoluteInvocation, authQuotaStop, classifyLaunch, constStringOf, failedLaunch, failureText, fixtureLeak, launchDoneLine, launchStartLine, parseArgs, readStream, redactPaths, treeStop, usableTurn } from './agy-probe.mjs';

const deps = { defaultExe: '/opt/agy', defaultModel: 'model-default', isFile: () => true };

test('no arguments: the defaults, 12 launches, a temp scratch folder (empty = make one)', () => {
  assert.deepEqual(parseArgs([], deps), { error: '', exe: '/opt/agy', scratch: '', model: 'model-default', maxLaunches: 12 });
});

test('every argument given is taken as given', () => {
  assert.deepEqual(parseArgs(['/x/agy', '/x/scratch', 'm1', '4'], deps), { error: '', exe: '/x/agy', scratch: '/x/scratch', model: 'm1', maxLaunches: 4 });
});

test('the launch cap is a positive integer of at most 12 — anything else fails fast, and NaN cannot disable the cap', () => {
  for (const ok of ['1', '4', '12']) assert.equal(parseArgs(['/x/agy', '/s', 'm', ok], deps).maxLaunches, Number(ok));
  for (const bad of ['0', '13', '-1', 'abc', 'NaN', '3.5', '4x', ' 4', '', '1e1', '999999']) {
    const got = parseArgs(['/x/agy', '/s', 'm', bad], deps);
    assert.match(got.error, /launch cap/, `cap '${bad}'`);
    assert.match(got.error, /12/, `cap '${bad}'`);
  }
});

test('the model must be a non-empty word when given', () => {
  for (const bad of ['', '   ']) assert.match(parseArgs(['/x/agy', '/s', bad], deps).error, /model/);
});

test('the executable must be an existing file', () => {
  assert.match(parseArgs(['/missing/agy'], { ...deps, isFile: () => false }).error, /agy executable/);
  assert.match(parseArgs([], { ...deps, isFile: () => false }).error, /agy executable/);
  assert.match(parseArgs([''], deps).error, /agy executable/);
  const seen = [];
  parseArgs(['/x/agy'], { ...deps, isFile: (p) => { seen.push(p); return true; } });
  assert.deepEqual(seen, ['/x/agy']);
});

test('a scratch folder given as an empty string, or a fifth argument, is refused', () => {
  assert.match(parseArgs(['/x/agy', ''], deps).error, /scratch/);
  assert.match(parseArgs(['/x/agy', '/s', 'm', '4', 'extra'], deps).error, /at most 4 arguments/);
});

test('the stream reader keeps the conversation id and the result, and COUNTS the lines it cannot parse', () => {
  const stdout = [
    '{"event":"init","conversation_id":"abc-123"}',
    '',
    '{"half": ',
    'plain log text',
    '{"event":"result","result":{"status":"SUCCESS","usage":{"input_tokens":5}}}\r',
    '{"event":"noise"}',
  ].join('\n');
  const got = readStream(stdout);
  assert.equal(got.conversation, 'abc-123');
  assert.equal(got.result.status, 'SUCCESS');
  assert.equal(got.events, 3);
  assert.equal(got.ignoredLines, 2);
});

test('an empty stream has nothing and ignores nothing', () => {
  assert.deepEqual(readStream(''), { conversation: '', result: null, events: 0, ignoredLines: 0 });
});

test('a launch is classified without touching its inputs: denial, conversation id, empty answer and all three', () => {
  const result = Object.freeze({ response: '  ', denied_actions: Object.freeze(['run_command']) });
  const got = classifyLaunch(result, '', 'abc_DEF-123');
  assert.deepEqual(got, { deniedActions: ['run_command'], deniedBySentence: false, denied: true, hasConversationId: true, emptyAnswer: true, malformedResponse: false, allThree: true });
});

test('denial can come from agy\'s own sentence on stderr; a bad conversation id or an answer defeats allThree', () => {
  assert.equal(classifyLaunch({ response: '' }, 'tool was auto-denied', 'abc').denied, true);
  assert.equal(classifyLaunch({ response: '' }, 'tool was auto-denied', 'abc').allThree, true);
  assert.equal(classifyLaunch({ response: '' }, 'tool was auto-denied', 'has space').allThree, false);
  assert.equal(classifyLaunch({ response: '' }, 'tool was auto-denied', '').hasConversationId, false);
  assert.equal(classifyLaunch({ response: 'an answer', denied_actions: ['x'] }, '', 'abc').allThree, false);
  assert.equal(classifyLaunch(null, '', 'abc').emptyAnswer, true);
  assert.equal(classifyLaunch(null, '', 'abc').denied, false);
});

test('the auth/quota stop names the launch and a CATEGORY word — never the text that matched, which could carry a token', () => {
  assert.equal(authQuotaStop(3, 'all good'), '');
  const auth = authQuotaStop(3, 'HTTP 401 unauthorized: bearer sk-SECRET-TOKEN-123 rejected');
  assert.equal(auth, 'auth/quota error on launch 3: auth');
  const quota = authQuotaStop(5, 'Resource exhausted for key AIzaSECRETKEY');
  assert.equal(quota, 'auth/quota error on launch 5: quota');
  assert.doesNotMatch(auth + quota, /SECRET/);
});

test('the console progress lines carry the launch number, label, exit code, seconds and input tokens', () => {
  assert.equal(launchStartLine(2, 12, 'run1.turn2'), '[launch 2/12] run1.turn2 starting…');
  assert.equal(launchDoneLine({ n: 2, exit: 0, seconds: 65.34, inputTokens: 94_883 }), '[launch 2] done: exit 0, 65.3s, input 94883');
  assert.equal(launchDoneLine({ n: 3, exit: null, seconds: 420, inputTokens: null }), '[launch 3] done: exit none, 420.0s, input unknown');
});

test('redaction replaces a path whatever its drive-letter case when asked to, in every escaped spelling', () => {
  const pairs = [['D:\\Temp\\run', '<tmp>']];
  const text = 'a d:\\temp\\run\\x b D:\\\\TEMP\\\\RUN\\\\y c d:/temp/run/z d "D:\\\\\\\\Temp"';
  const got = redactPaths(text, pairs, true);
  assert.equal(got, 'a <tmp>\\x b <tmp>\\\\y c <tmp>/z d "D:\\\\\\\\Temp"');
  assert.doesNotMatch(got, /temp\\run/i);
});

test('redaction stays case-sensitive when it is not asked for, skips empty values and takes a placeholder literally', () => {
  assert.equal(redactPaths('d:\\temp\\x D:\\Temp\\x', [['D:\\Temp', '<tmp>']], false), 'd:\\temp\\x <tmp>\\x');
  assert.equal(redactPaths('keep', [['', '<x>']], true), 'keep');
  assert.equal(redactPaths('/home/u/x', [['/home/u', '$&-$1']], false), '$&-$1/x');
});

const REPO_ROOT = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const good = { hasResult: true, usage: { input_tokens: 1 }, exit: 0, status: 'SUCCESS', timedOut: false, launchError: '' };

test('a result that is not an object is no result: counted as an ignored line, and nothing downstream reads a field of it', () => {
  for (const result of ['"text"', '7', 'null', '[1]']) {
    const got = readStream(`{"event":"result","result":${result}}\n`);
    assert.equal(got.result, null, result);
    assert.equal(got.ignoredLines, 1, result);
  }
});

test('a response that is not a string does not throw: it is marked malformed, and is neither an answer nor an empty one', () => {
  for (const response of [7, { a: 1 }, ['x'], true]) {
    const got = classifyLaunch({ response }, '', 'conv');
    assert.equal(got.malformedResponse, true);
    assert.equal(got.emptyAnswer, false);
    assert.equal(got.allThree, false);
  }
  assert.equal(classifyLaunch({ response: 'an answer' }, '', 'conv').malformedResponse, false);
  assert.equal(classifyLaunch({ response: '  ' }, '', 'conv').emptyAnswer, true);
  assert.equal(classifyLaunch({}, '', 'conv').emptyAnswer, true);
  assert.equal(classifyLaunch(null, '', 'conv').emptyAnswer, true);
});

test('a launch failed when it exited nonzero, timed out, could not run, or its result does not say SUCCESS', () => {
  assert.equal(failedLaunch(good), false);
  for (const change of [{ exit: 1 }, { exit: null }, { timedOut: true }, { launchError: 'heldPipes' }, { status: 'ERROR' }, { status: null }]) {
    assert.equal(failedLaunch({ ...good, ...change }), true, JSON.stringify(change));
  }
});

test('a failed launch is read for auth and quota in its result error too, whatever shape that error has', () => {
  const text = failureText({ error: 'RESOURCE_EXHAUSTED 429' }, 'noise', 'stream');
  assert.match(authQuotaStop(3, text), /quota/);
  assert.match(authQuotaStop(3, failureText({ error: { code: 401 } }, '', '')), /auth/);
  for (const result of [null, undefined, {}, { error: null }]) assert.equal(authQuotaStop(3, failureText(result, '', '')), '');
  assert.equal(failureText(null, 'e', 'x'.repeat(5000)).length, 'e'.length + 2000, 'only the tail of stdout is read');
});

test('a turn that failed is not a completed measurement, even when it reports usage', () => {
  assert.equal(usableTurn(good), true);
  assert.equal(usableTurn({ ...good, exit: 2 }), false);
  assert.equal(usableTurn({ ...good, status: 'ERROR' }), false);
  assert.equal(usableTurn({ ...good, usage: null }), false);
  assert.equal(usableTurn({ ...good, hasResult: false }), false);
});

test('a tree that did not end stops the probe, and the sentence says a helper may still run; any other result does not', () => {
  assert.match(treeStop(4, 'heldPipes'), /launch 4.*helper.*still/);
  assert.match(treeStop(4, 'notReaped'), /launch 4.*still/);
  for (const other of ['', 'maxBuffer', 'ENOENT']) assert.equal(treeStop(4, other), '');
});

test('the executable and the scratch folder are made absolute against the invocation folder, because a launch runs elsewhere', () => {
  const base = resolve('/invoked/here');
  assert.deepEqual(absoluteInvocation({ exe: 'bin/agy', scratch: 'run' }, base), { exe: join(base, 'bin', 'agy'), scratch: join(base, 'run') });
  assert.deepEqual(absoluteInvocation({ exe: resolve('/opt/agy'), scratch: '' }, base), { exe: resolve('/opt/agy'), scratch: '' }, 'no scratch means make one');
});

test('a C# constant is read whole: a semicolon inside a literal does not end it', () => {
  const source = 'x; internal const string T =\n  "one; two "\n  + "three";\nconst string U = "u";';
  assert.equal(constStringOf(source, 'T'), 'one; two three');
  assert.equal(constStringOf(source, 'U'), 'u');
  assert.equal(constStringOf(source, 'Absent'), null);
});

test('a constant this reader cannot read exactly is refused, never guessed: an escape, or anything but literals', () => {
  assert.equal(constStringOf('const string T = "a\\"b";', 'T'), null);
  assert.equal(constStringOf('const string T = "a" + Other;', 'T'), null);
});

test("the follow-up text read from the build's own source is whole", () => {
  const text = constStringOf(readFileSync(join(REPO_ROOT, 'src_mcp/runners/Reviewers/AntigravityRuntime.cs'), 'utf8'), 'NoCommands');
  assert.match(text, /^Shell commands are not available/);
  assert.match(text, /the schema asks for; if a check needed a command/);
  assert.match(text, /could not be run\.$/);
});

test('the leak check reads what was captured, not the fixture\'s own words or placeholders', () => {
  assert.equal(fixtureLeak(['<user> at <home> in <checkout>, mail <email>'], 'User'), '');
  assert.equal(fixtureLeak(['clean text'], 'User'), '');
  assert.match(fixtureLeak(['left by Alice here'], 'alice'), /user name/);
  assert.match(fixtureLeak(['someone@example.com'], 'alice'), /@/);
});
