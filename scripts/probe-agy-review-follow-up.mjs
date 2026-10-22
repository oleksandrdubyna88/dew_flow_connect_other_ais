// Probe M1 of todo/PLAN_the_round_counter_tail.md (the "M1" row of "What is measured first"; epic E1): is an agy
// REVIEWER's follow-up (a second launch in the same conversation) reported CUMULATIVELY or PER LAUNCH?
// PAID: every launch is a real `agy` turn on the owner's Google subscription. Run it only in a window with the Team
// server's agy slot idle, and never beside another `agy`.
//
//   node scripts/probe-agy-review-follow-up.mjs [<agy exe>] [<scratch dir>] [<model>] [<launch cap, at most 12>]
//
// Launch shape = what `AntigravityRuntime.Build` sends a reviewer (src_mcp/runners/Reviewers/AntigravityRuntime.cs):
// `--print= --input-format stream-json --output-format stream-json --mode plan --agent coai-reader --json-schema <finding
// schema> --model <m> --add-dir <checkout>`, run FROM a folder holding the `coai-reader` agent (tools: view_file only)
// and the PreToolUse hook that allows only view_file (AntigravityReadOnly). The product's hook handler is the coai
// binary; here it is a node script with the SAME decision (`Decide`: allow view_file, deny every other tool). The finding
// schema and the follow-up text (`AntigravityRuntime.NoCommands`) are read out of the C# sources, never copied, so the
// probe cannot drift from the build it measures. The folder lives in the scratch directory; the person's own agy
// configuration and `%LOCALAPPDATA%\coai-agy` are not touched.
//
// STEP 1 (<= 4 first launches): a reviewer prompt that cannot be answered without a shell command (the method of
//   research/RESULTS_agy_consult_follow_up.md section 2/3, cited for the METHOD only). The first launch that shows ALL THREE of a
//   denial (`denied_actions`), a conversation id and an EMPTY answer is the capture: saved, redacted, as
//   src_mcp/tests/fixtures/antigravity/review-denied.json, and it becomes turn 1 of run 1 (step 2a).
// STEP 2a (elicited): 3 runs, each ONE conversation of 3 launches: turn 1 the denied launch, turn 2 the follow-up exactly
//   as `AntigravityRuntime.FollowUp` sends it (`--conversation <id>`, NoCommands on stdin, everything else unchanged),
//   turn 3 the words "reply OK" in the same conversation. A later run whose turn 1 is not denied+empty is recorded
//   and DISCARDED (it still counts against the caps).
// STEP 2b (nothing elicited in step 1): 3 runs, each ONE conversation: turn 1 a short answered prompt, turn 2 the same
//   NoCommands continuation, turn 3 "reply OK". Cumulative reporting is then measured apart from denial recovery, which
//   stays UNMEASURED. When the budget cannot hold 9 fresh launches, run 1 reuses the last step-1 launch that answered
//   and carries a conversation id as its turn 1 (recorded as such).
//
// HARD CAPS, counted per ATTEMPT (a launch that fails or times out counts): <= 12 launches in all; stop as soon as 3 runs
// are complete; stop once the reports' own input-token sum passes 400 000; stop at the FIRST auth/quota error and say so.
// Compared: OUTPUT and reasoning tokens, never input. Every `usage` block is kept in <scratch>/results.json, raw
// streams beside it. Nothing here prints or stores a credential: the environment is passed on as it is and never logged.
import { spawnSync, execFileSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import os from 'node:os';
import { resolved } from '../.github/scripts/lib/resolved.mjs';
import { capRefusal } from './lib/spendCap.mjs';

const GIT = resolved('git');
const REPO_ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
let MAX_LAUNCHES = 12; // lowered by the optional 4th argument, never raised above 12
const MAX_FIRST_LAUNCHES = 4;
const MAX_INPUT_TOKENS = 400_000;
const RUNS_WANTED = 3;
const LAUNCH_TIMEOUT_MS = 420_000;
const FIXTURE = join(REPO_ROOT, 'src_mcp', 'tests', 'fixtures', 'antigravity', 'review-denied.json');
const DEFAULT_AGY = join(process.env.LOCALAPPDATA ?? join(os.homedir(), 'AppData', 'Local'), 'agy', 'bin', 'agy.exe');
// AntigravityRuntime.DefaultModel is gemini-3.7-flash-high, which agy 1.4.4 REFUSES (`agy models` lists 3.8 and 4, no 3.7): the
// first run of this probe spent 7 attempts and 0 tokens on that refusal. The nearest valid sibling is the default here.
const DEFAULT_MODEL = 'gemini-3.8-flash-high';

const [exeArg, scratchArg, modelArg, capArg] = process.argv.slice(2);
MAX_LAUNCHES = Math.min(12, Number.parseInt(capArg ?? '12', 10));
const exe = exeArg ?? DEFAULT_AGY;
const model = modelArg ?? DEFAULT_MODEL;
const scratch = scratchArg ?? mkdtempSync(join(os.tmpdir(), 'testsToDelete-agy-m1-'));
mkdirSync(scratch, { recursive: true });

// ---- read the build's own texts out of the C# sources ---------------------------------------------------------------
const source = (relative) => readFileSync(join(REPO_ROOT, relative), 'utf8');

function findingSchemaJson() {
  const text = source('src_mcp/core/Findings/FindingSchema.cs');
  const body = /public const string Json = """\r?\n([\s\S]*?)\r?\n\s*""";/.exec(text)?.[1];
  if (!body) throw new Error('FindingSchema.Json not found in source');
  return JSON.stringify(JSON.parse(body), null, 2);
}

function noCommandsText() {
  const text = source('src_mcp/runners/Reviewers/AntigravityRuntime.cs');
  const declared = /internal const string NoCommands =([\s\S]*?);/.exec(text)?.[1];
  if (!declared) throw new Error('NoCommands not found in source');
  return [...declared.matchAll(/"((?:[^"\\]|\\.)*)"/g)].map((m) => m[1]).join('');
}

const NO_COMMANDS = noCommandsText();

// ---- the read-only home: agent + hook, as AntigravityReadOnly writes them -----------------------------------------------
const AGENT_FILE = '---\nname: coai-reader\n'
  + 'description: A read-only consultant for ConnectOtherAIs. It reads files with view_file and never writes, edits or deletes.\n'
  + 'tools:\n  - view_file\nmainAgent: true\nsubagent: false\ncommandExecutionPolicy: "off"\n---\n'
  + 'You answer from what you read. You never create, edit or delete files.\n';

const HOOK_HANDLER = `import { readFileSync } from 'node:fs';
let name = '';
try { name = JSON.parse(readFileSync(0, 'utf8'))?.toolCall?.name ?? ''; } catch { name = ''; }
process.stdout.write(name === 'view_file'
  ? '{"decision":"allow"}'
  : JSON.stringify({ decision: 'deny', reason: 'coai: this consultation is read-only; only view_file runs' }));
`;

function prepareHome() {
  const home = join(scratch, 'home');
  mkdirSync(join(home, '.agents', 'agents'), { recursive: true });
  writeFileSync(join(home, '.agents', 'agents', 'coai-reader.md'), AGENT_FILE);
  writeFileSync(join(home, '.agents', 'hook.mjs'), HOOK_HANDLER);
  const windows = os.platform() === 'win32';
  writeFileSync(join(home, '.agents', 'coai-hook.cmd'), `@"${process.execPath}" "${join(home, '.agents', 'hook.mjs')}"\r\n`);
  writeFileSync(join(home, '.agents', 'coai-hook.sh'), `#!/bin/sh\nexec "${process.execPath}" "${join(home, '.agents', 'hook.mjs')}"\n`);
  writeFileSync(join(home, '.agents', 'hooks.json'), JSON.stringify({
    'coai-read-only': { PreToolUse: [{ matcher: '*', hooks: [{ type: 'command',
      command: windows ? '.\\coai-hook.cmd' : 'sh ./coai-hook.sh', timeout: 10 }] }] },
  }, null, 2));
  return home;
}

function plantedCheckout() {
  const repo = join(scratch, 'checkout');
  mkdirSync(repo, { recursive: true });
  const git = (...args) => execFileSync(GIT, ['-c', 'user.name=probe', '-c', 'user.email=probe@example.invalid',
    '-c', 'commit.gpgsign=false', ...args], { cwd: repo });
  git('init', '-q');
  writeFileSync(join(repo, 'notes.txt'), 'nothing here\n');
  writeFileSync(join(repo, 'guard.txt'), 'the guard marker is QUOKKA-7731\n');
  git('add', '.');
  git('commit', '-qm', 'probe');
  return repo;
}

const home = prepareHome();
const checkout = plantedCheckout();
const schemaFile = join(scratch, 'finding-schema.json');
writeFileSync(schemaFile, findingSchemaJson());
const agyVersion = execFileSync(exe, ['--version'], { encoding: 'utf8' }).trim();

// ---- prompts --------------------------------------------------------------------------------------------------------
const CHECKOUT_NOTE = `\n\nThe checkout under review is at ${checkout}; open its files by that absolute path.`;
const STEP1_PROMPTS = [
  'You are the Architecture reviewer of a small change that adds a guard marker to the checkout. Your verdict depends on one '
  + 'fact that only the shell can show: run exactly this command with run_command and read its output: git grep -n QUOKKA .  '
  + 'Then return your findings (an empty list is fine) and say in `notes` which file holds the marker.',
  'You are the Reliability reviewer of a change whose correctness hinges on the checkout\'s git state. You MUST run '
  + '`git log --oneline -1 && git grep -n QUOKKA .` with run_command before you answer; do not guess its output. Then '
  + 'return your findings and name the commit message and the file that holds the marker in `notes`.',
  'Review the checkout for a stray marker. The only reliable way to find it is the shell: use run_command to run '
  + '`git grep -n QUOKKA .` and base every finding on that output. Return findings in the schema; `notes` names the file.',
  'Security review. Before judging anything, execute `git grep -n QUOKKA . ; git status --short` through run_command and '
  + 'quote the result in `notes`. Return the schema\'s JSON.',
];
const STEP_2B_FIRST = 'Reply with the JSON the schema asks for: an empty findings list, and the single word ready in notes.';
const REPLY_OK = 'reply OK';

// ---- one launch -----------------------------------------------------------------------------------------------------
const log = [];
let launches = 0;
let inputSum = 0;

class Stop extends Error {}

const userMessage = (text) => JSON.stringify({ event: 'user', message: { role: 'user', content: text } }) + '\n';
const argv = (conversation) => ['--print=', '--input-format', 'stream-json', '--output-format', 'stream-json', '--mode', 'plan',
  '--agent', 'coai-reader', '--json-schema', schemaFile, '--model', model, '--add-dir', checkout,
  ...(conversation ? ['--conversation', conversation] : [])];

function readStream(stdout) {
  const out = { conversation: '', result: null, events: 0 };
  for (const line of stdout.split('\n').map((l) => l.trim()).filter((l) => l.startsWith('{'))) {
    try {
      const event = JSON.parse(line);
      out.events++;
      if (event.event === 'init' && typeof event.conversation_id === 'string') out.conversation = event.conversation_id;
      if (event.event === 'result') out.result = event.result;
    } catch { /* a half-written line */ }
  }
  return out;
}

const QUOTA_OR_AUTH = /unauthori[sz]ed|unauthenticated|not (signed|logged) in|sign[- ]?in|log ?in|login|credential|auth(entication|orization)? (error|failed|required)|quota|rate.?limit|resource.?exhausted|too many requests|\b(401|403|429)\b|exceeded|billing/i;

function launch(label, text, conversation) {
  if (launches >= MAX_LAUNCHES) throw new Stop(`launch cap ${MAX_LAUNCHES} reached`);
  // Checked BEFORE the launch (scripts/lib/spendCap.mjs says why).
  const refusal = capRefusal(inputSum, log.map((one) => one.usage?.input_tokens ?? 0), MAX_INPUT_TOKENS);
  if (refusal) throw new Stop(refusal);
  launches++;
  const started = Date.now();
  const r = spawnSync(exe, argv(conversation), { cwd: home, input: userMessage(text), encoding: 'utf8',
    timeout: LAUNCH_TIMEOUT_MS, maxBuffer: 64 * 1024 * 1024 });
  const stdout = r.stdout ?? '';
  const stderr = r.stderr ?? '';
  const stream = readStream(stdout);
  const result = stream.result;
  const denied = Array.isArray(result?.denied_actions) ? result.denied_actions : [];
  const record = {
    n: launches, label, conversationArg: Boolean(conversation), exit: r.status, timedOut: r.error?.code === 'ETIMEDOUT',
    seconds: (Date.now() - started) / 1000, conversation: stream.conversation,
    status: result?.status ?? null, response: result?.response ?? null,
    deniedActions: denied, deniedBySentence: /auto-denied/.test(stderr),
    usage: result?.usage ?? null, hasResult: result !== null, stderrHead: stderr.slice(0, 300), stdout, stderr,
  };
  record.denied = denied.length > 0 || record.deniedBySentence;
  record.hasConversationId = /^[A-Za-z0-9_-]{1,128}$/.test(record.conversation);
  record.emptyAnswer = (record.response ?? '').trim() === '';
  record.allThree = record.denied && record.hasConversationId && record.emptyAnswer;
  inputSum += record.usage?.input_tokens ?? 0;
  log.push(record);
  writeFileSync(join(scratch, `launch-${String(launches).padStart(2, '0')}.stdout.ndjson`), stdout);
  writeFileSync(join(scratch, `launch-${String(launches).padStart(2, '0')}.stderr.txt`), stderr);
  save();
  if (/invalid model selection/.test(result?.error ?? '')) {
    throw new Stop(`the model '${model}' is refused by this agy (no tokens spent; see "agy models")`);
  }
  if (!record.hasResult && r.status !== 0 && QUOTA_OR_AUTH.test(stderr + stdout.slice(-2000))) {
    throw new Stop(`auth/quota error on launch ${launches}: ${stderr.slice(0, 200).replace(/\s+/g, ' ')}`);
  }
  if (inputSum > MAX_INPUT_TOKENS) throw new Stop(`input-token cap passed: ${inputSum} > ${MAX_INPUT_TOKENS}`);
  return record;
}

function save() {
  const slim = log.map(({ stdout, stderr, ...rest }) => rest);
  writeFileSync(join(scratch, 'results.json'), JSON.stringify({ at: new Date().toISOString(), agyVersion, model, platform: os.platform(),
    launches, inputSum, runs, slim, stoppedBecause }, null, 2));
}

// ---- the plan -------------------------------------------------------------------------------------------------------
const runs = [];
let stoppedBecause = '';
let step = '';
let captureIndex = -1;

const usable = (turn) => turn.hasResult && turn.usage !== null;
const complete = () => runs.filter((run) => run.turns.length === 3 && run.turns.every(usable)).length;

function turnsTwoAndThree(run, firstTurn) {
  const id = firstTurn.conversation;
  run.turns.push(launch(`run${run.id}.turn2`, NO_COMMANDS, id));
  run.turns.push(launch(`run${run.id}.turn3`, REPLY_OK, id));
}

function step1() {
  for (let i = 0; i < MAX_FIRST_LAUNCHES; i++) {
    const record = launch(`step1.${i + 1}`, STEP1_PROMPTS[i] + CHECKOUT_NOTE, null);
    record.prompt = STEP1_PROMPTS[i];
    if (record.allThree) {
      captureIndex = i;
      return record;
    }
  }
  return null;
}

function runStep2a(capture) {
  const prompt = capture.prompt + CHECKOUT_NOTE;
  runs.push({ id: 1, mode: '2a', reusedStep1: true, turns: [capture] });
  turnsTwoAndThree(runs[0], capture);
  while (complete() < RUNS_WANTED && launches < MAX_LAUNCHES) {
    const first = launch(`run${runs.length + 1}.turn1`, prompt, null);
    if (!first.allThree) {
      runs.push({ id: runs.length + 1, mode: '2a', discarded: 'turn 1 not denied+conversation+empty', turns: [first] });
      continue;
    }
    const run = { id: runs.length + 1, mode: '2a', turns: [first] };
    runs.push(run);
    turnsTwoAndThree(run, first);
  }
}

function reusableStep1Launch() {
  return log.filter((l) => l.hasConversationId && !l.emptyAnswer && l.usage).at(-1) ?? null;
}

function runStep2b() {
  const wanted = RUNS_WANTED - complete();
  for (let i = 0; i < wanted && launches < MAX_LAUNCHES; i++) {
    const reuse = i === 0 && MAX_LAUNCHES - launches < 9 ? reusableStep1Launch() : null;
    const first = reuse ?? launch(`run${runs.length + 1}.turn1`, STEP_2B_FIRST, null);
    if (!first.hasConversationId) {
      runs.push({ id: runs.length + 1, mode: '2b', discarded: 'no conversation id', turns: [first] });
      continue;
    }
    const run = { id: runs.length + 1, mode: '2b', reusedStep1: Boolean(reuse), turns: [first] };
    runs.push(run);
    turnsTwoAndThree(run, first);
  }
}

function writeFixture(capture) {
  const user = os.userInfo().username;
  const sub = (text) => {
    const variants = (s) => [s, s.replaceAll('\\', '\\\\'), s.replaceAll('\\', '/'), s.replaceAll('\\', '\\\\').replaceAll('/', '\\/')];
    let out = text;
    const pairs = [[checkout, '<checkout>'], [home, '<agy-home>'], [scratch, '<scratch>'], [os.tmpdir(), '<tmp>'],
      [os.homedir(), '<home>'], [user, '<user>']];
    for (const [value, placeholder] of pairs) for (const v of variants(value)) out = out.replaceAll(v, placeholder);
    return out.replaceAll(capture.conversation, '00000000-0000-4000-8000-000000000001')
      .replace(/[\w.+-]+@[\w-]+\.[\w.-]+/g, '<email>');
  };
  const fixture = {
    captured: new Date().toISOString().slice(0, 10), agy: agyVersion, model,
    note: 'M1 step 1 capture under the current reviewer build (coai-reader agent + view_file-only hook). Denied command, conversation id, EMPTY answer. Paths, user name and the conversation id are redacted; usage is as reported.',
    prompt: sub(capture.prompt + CHECKOUT_NOTE), arguments: argv(null).map(sub),
    stdout: sub(capture.stdout), stderr: sub(capture.stderr),
  };
  const text = JSON.stringify(fixture, null, 2);
  if (new RegExp(user, 'i').test(text) || /@/.test(text.replace(/<email>/g, ''))) throw new Error('redaction left a user name or an @ in the fixture; not saved');
  mkdirSync(dirname(FIXTURE), { recursive: true });
  writeFileSync(FIXTURE, text + '\n');
  return FIXTURE;
}

// ---- go -------------------------------------------------------------------------------------------------------------
try {
  const capture = step1();
  if (capture) {
    step = '2a';
    runStep2a(capture);
  } else {
    step = '2b';
    runStep2b();
  }
} catch (e) {
  if (!(e instanceof Stop)) throw e;
  stoppedBecause = e.message;
}
const fixtureSaved = captureIndex >= 0 ? writeFixture(log[captureIndex]) : null;
save();

// ---- the table ------------------------------------------------------------------------------------------------------
const cell = (v) => String(v ?? '-').padStart(7);
console.log(`agy ${agyVersion}  model ${model}  step ${step || '1 (stopped)'}  launches ${launches}/${MAX_LAUNCHES}  input sum ${inputSum}`);
console.log(`stopped because: ${stoppedBecause || 'plan finished'}   fixture: ${fixtureSaved ?? 'not saved'}`);
console.log(['launch', 'run', 'turn', 'input', 'output', 'think', 'cached', 'total', 'denied', 'empty', 'convId', 'secs'].map((h) => h.padStart(7)).join(' '));
for (const run of runs) {
  run.turns.forEach((t, i) => {
    const u = t.usage ?? {};
    console.log([t.n, run.id + (run.discarded ? 'x' : ''), i + 1, u.input_tokens, u.output_tokens, u.thinking_tokens,
      u.cache_read_tokens, u.total_tokens, t.denied, t.emptyAnswer, t.hasConversationId, Math.round(t.seconds)].map(cell).join(' '));
  });
}
const extra = log.filter((l) => !runs.some((run) => run.turns.includes(l)));
for (const t of extra) {
  const u = t.usage ?? {};
  console.log([t.n, t.label, '-', u.input_tokens, u.output_tokens, u.thinking_tokens, u.cache_read_tokens, u.total_tokens,
    t.denied, t.emptyAnswer, t.hasConversationId, Math.round(t.seconds)].map(cell).join(' '));
}
console.log(`scratch: ${scratch}`);
