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
// are complete; stop once the reports' own input-token sum passes 400 000 (checked BEFORE a launch and again after it;
// lib/spendCap.mjs); stop at the FIRST auth/quota error and say so; stop on a launch whose report carries no usable
// input-token count (the spend is unknown, so the probe does not go on). Every external process has a timeout, and a launch
// that times out ends its whole process tree (lib/launch-tree.mjs). Compared: OUTPUT and reasoning tokens, never input. Every
// `usage` block is kept in <scratch>/results.json, raw streams beside it. Nothing here prints or stores a credential: the
// environment is passed on as it is and never logged, and a Stop message never carries launch output.
import { execFileSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import os from 'node:os';
import { resolved } from '../.github/scripts/lib/resolved.mjs';
import { authQuotaStop, classifyLaunch, launchDoneLine, launchStartLine, parseArgs, readStream, redactPaths } from './lib/agy-probe.mjs';
import { runTree } from './lib/launch-tree.mjs';
import { capPassed, capRefusal, inputTokensOf } from './lib/spendCap.mjs';

const GIT = resolved('git');
const REPO_ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const MAX_FIRST_LAUNCHES = 4;
const MAX_INPUT_TOKENS = 400_000;
const RUNS_WANTED = 3;
const LAUNCH_TIMEOUT_MS = 420_000;
const SHORT_TIMEOUT_MS = 30_000; // `agy --version` and every git call
const FIXTURE = join(REPO_ROOT, 'src_mcp', 'tests', 'fixtures', 'antigravity', 'review-denied.json');
const DEFAULT_AGY = join(process.env.LOCALAPPDATA ?? join(os.homedir(), 'AppData', 'Local'), 'agy', 'bin', 'agy.exe');
// AntigravityRuntime.DefaultModel is gemini-3.7-flash-high, which agy 1.4.4 REFUSES (`agy models` lists 3.8 and 4, no 3.7): the
// first run of this probe spent 7 attempts and 0 tokens on that refusal. The nearest valid sibling is the default here.
const DEFAULT_MODEL = 'gemini-3.8-flash-high';

// Validated before anything is created or launched: a bad argument ends the run with one sentence.
const args = parseArgs(process.argv.slice(2), { defaultExe: DEFAULT_AGY, defaultModel: DEFAULT_MODEL });
if (args.error) {
  process.stderr.write(`${args.error}\n`);
  process.exit(2);
}
const { exe, model, maxLaunches: MAX_LAUNCHES } = args;
const scratch = args.scratch || mkdtempSync(join(os.tmpdir(), 'testsToDelete-agy-m1-'));
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
  const git = (...gitArgs) => execFileSync(GIT, ['-c', 'user.name=probe', '-c', 'user.email=probe@example.invalid',
    '-c', 'commit.gpgsign=false', ...gitArgs], { cwd: repo, timeout: SHORT_TIMEOUT_MS, windowsHide: true });
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
const versionRun = await runTree(exe, ['--version'], { cwd: home, timeoutMs: SHORT_TIMEOUT_MS, maxBuffer: 1024 * 1024 });
if (versionRun.status !== 0) {
  process.stderr.write(`agy --version failed (exit ${versionRun.status ?? 'none'}${versionRun.timedOut ? ', timed out' : ''}${versionRun.error ? `, ${versionRun.error}` : ''}); nothing was launched\n`);
  process.exit(2);
}
const agyVersion = versionRun.stdout.trim();

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

/** The Stop a finished launch calls for, or '' — in the order: a refused model, an auth/quota failure, unknown spend, the cap. */
function stopAfter(record, result, stderr, stdout) {
  if (/invalid model selection/.test(result?.error ?? '')) {
    return `the model '${model}' is refused by this agy (no tokens spent; see "agy models")`;
  }
  const authStop = record.hasResult || record.exit === 0 ? '' : authQuotaStop(record.n, stderr + stdout.slice(-2000));
  if (authStop) return authStop;
  if (record.inputTokens === null) return `launch ${record.n}: ${inputTokensOf(record.usage).refusal}${record.timedOut ? ' (it timed out)' : ''}; the spend is unknown, so the probe stops`;
  return capPassed(inputSum, MAX_INPUT_TOKENS) ? `input-token cap passed: ${inputSum} > ${MAX_INPUT_TOKENS}` : '';
}

async function launch(label, text, conversation, { prompt = '' } = {}) {
  if (launches >= MAX_LAUNCHES) throw new Stop(`launch cap ${MAX_LAUNCHES} reached`);
  // Checked BEFORE the launch (scripts/lib/spendCap.mjs says why).
  const refusal = capRefusal(inputSum, log.map((one) => one.inputTokens), MAX_INPUT_TOKENS);
  if (refusal) throw new Stop(refusal);
  launches++;
  process.stderr.write(`${launchStartLine(launches, MAX_LAUNCHES, label)}\n`);
  const started = Date.now();
  const r = await runTree(exe, argv(conversation), { cwd: home, input: userMessage(text), timeoutMs: LAUNCH_TIMEOUT_MS, maxBuffer: 64 * 1024 * 1024 });
  const { stdout, stderr } = r;
  const stream = readStream(stdout);
  const result = stream.result;
  const spend = inputTokensOf(result?.usage);
  const record = {
    n: launches, label, prompt, conversationArg: Boolean(conversation), exit: r.status, timedOut: r.timedOut, launchError: r.error,
    seconds: (Date.now() - started) / 1000, conversation: stream.conversation, ignoredLines: stream.ignoredLines,
    status: result?.status ?? null, response: result?.response ?? null,
    ...classifyLaunch(result, stderr, stream.conversation),
    usage: result?.usage ?? null, inputTokens: spend.refusal ? null : spend.tokens, hasResult: result !== null,
    stderrHead: stderr.slice(0, 300), stdout, stderr,
  };
  inputSum += record.inputTokens ?? 0;
  log.push(record);
  writeFileSync(join(scratch, `launch-${String(launches).padStart(2, '0')}.stdout.ndjson`), stdout);
  writeFileSync(join(scratch, `launch-${String(launches).padStart(2, '0')}.stderr.txt`), stderr);
  save();
  process.stderr.write(`${launchDoneLine(record)}\n`);
  const stop = stopAfter(record, result, stderr, stdout);
  if (stop) throw new Stop(stop);
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

async function turnsTwoAndThree(run, firstTurn) {
  const id = firstTurn.conversation;
  run.turns.push(await launch(`run${run.id}.turn2`, NO_COMMANDS, id));
  run.turns.push(await launch(`run${run.id}.turn3`, REPLY_OK, id));
}

async function step1() {
  for (let i = 0; i < MAX_FIRST_LAUNCHES; i++) {
    const record = await launch(`step1.${i + 1}`, STEP1_PROMPTS[i] + CHECKOUT_NOTE, null, { prompt: STEP1_PROMPTS[i] });
    if (record.allThree) {
      captureIndex = i;
      return record;
    }
  }
  return null;
}

async function runStep2a(capture) {
  const prompt = capture.prompt + CHECKOUT_NOTE;
  runs.push({ id: 1, mode: '2a', reusedStep1: true, turns: [capture] });
  await turnsTwoAndThree(runs[0], capture);
  while (complete() < RUNS_WANTED && launches < MAX_LAUNCHES) {
    const first = await launch(`run${runs.length + 1}.turn1`, prompt, null);
    if (!first.allThree) {
      runs.push({ id: runs.length + 1, mode: '2a', discarded: 'turn 1 not denied+conversation+empty', turns: [first] });
      continue;
    }
    const run = { id: runs.length + 1, mode: '2a', turns: [first] };
    runs.push(run);
    await turnsTwoAndThree(run, first);
  }
}

function reusableStep1Launch() {
  return log.filter((l) => l.hasConversationId && !l.emptyAnswer && l.usage).at(-1) ?? null;
}

async function runStep2b() {
  const wanted = RUNS_WANTED - complete();
  for (let i = 0; i < wanted && launches < MAX_LAUNCHES; i++) {
    const reuse = i === 0 && MAX_LAUNCHES - launches < 9 ? reusableStep1Launch() : null;
    const first = reuse ?? await launch(`run${runs.length + 1}.turn1`, STEP_2B_FIRST, null);
    if (!first.hasConversationId) {
      runs.push({ id: runs.length + 1, mode: '2b', discarded: 'no conversation id', turns: [first] });
      continue;
    }
    const run = { id: runs.length + 1, mode: '2b', reusedStep1: Boolean(reuse), turns: [first] };
    runs.push(run);
    await turnsTwoAndThree(run, first);
  }
}

function writeFixture(capture) {
  const user = os.userInfo().username;
  // Most specific first. Windows paths differ in drive-letter and folder casing between the places they were read from, so
  // there the match ignores case (lib/agy-probe.mjs `redactPaths`).
  const pairs = [[checkout, '<checkout>'], [home, '<agy-home>'], [scratch, '<scratch>'], [os.tmpdir(), '<tmp>'],
    [os.homedir(), '<home>'], [user, '<user>']];
  const sub = (text) => redactPaths(text, pairs, os.platform() === 'win32')
    .replaceAll(capture.conversation, '00000000-0000-4000-8000-000000000001')
    .replace(/[\w.+-]+@[\w-]+\.[\w.-]+/g, '<email>');
  const fixture = {
    captured: new Date().toISOString().slice(0, 10), agy: agyVersion, model,
    note: 'M1 step 1 capture under the current reviewer build (coai-reader agent + view_file-only hook). Denied command, conversation id, EMPTY answer. Paths, user name and the conversation id are redacted; usage is as reported.',
    prompt: sub(capture.prompt + CHECKOUT_NOTE), arguments: argv(null).map(sub),
    stdout: sub(capture.stdout), stderr: sub(capture.stderr),
  };
  const text = JSON.stringify(fixture, null, 2);
  if (text.toLowerCase().includes(user.toLowerCase()) || /@/.test(text.replace(/<email>/g, ''))) throw new Error('redaction left a user name or an @ in the fixture; not saved');
  mkdirSync(dirname(FIXTURE), { recursive: true });
  writeFileSync(FIXTURE, text + '\n');
  return FIXTURE;
}

// ---- go -------------------------------------------------------------------------------------------------------------
try {
  const capture = await step1();
  if (capture) {
    step = '2a';
    await runStep2a(capture);
  } else {
    step = '2b';
    await runStep2b();
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
