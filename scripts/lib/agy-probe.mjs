// lib/agy-probe.mjs — the pure parts of scripts/probe-agy-review-follow-up.mjs (a PAID probe that is never run by a test): the
// command line, the stream reader, how a launch is classified, the sentence an auth/quota stop prints, the console progress
// lines and the fixture's path redaction. The input-token budget is lib/spendCap.mjs; the launch itself is lib/launch-tree.mjs.
import { statSync } from 'node:fs';
import { resolve } from 'node:path';

const MAX_LAUNCHES_ALLOWED = 12;
const MAX_ARGUMENTS = 4;

const isExistingFile = (path) => {
  try {
    return statSync(path).isFile();
  } catch {
    return false;
  }
};

/**
 * The command line `[<agy exe>] [<scratch dir>] [<model>] [<launch cap>]`, checked at the boundary. Returns
 * `{ error, exe, scratch, model, maxLaunches }`: `error` is '' or ONE sentence for the person, and `scratch` is '' when the
 * probe should make its own temp folder. An argument that is given must be usable — an empty string is an error, never a silent
 * default — and the cap is digits only, 1 to 12, so a typo can no longer turn the launch cap into NaN.
 */
export function parseArgs(argv, { defaultExe, defaultModel, isFile = isExistingFile }) {
  const [exeArg, scratchArg, modelArg, capArg] = argv;
  const exe = exeArg ?? defaultExe;
  const model = modelArg ?? defaultModel;
  const fail = (error) => ({ error, exe, scratch: scratchArg ?? '', model, maxLaunches: 0 });
  if (argv.length > MAX_ARGUMENTS) return fail(`at most ${MAX_ARGUMENTS} arguments: [<agy exe>] [<scratch dir>] [<model>] [<launch cap, 1 to ${MAX_LAUNCHES_ALLOWED}>]`);
  if (!exe?.trim() || !isFile(exe)) return fail('the agy executable is not an existing file: pass its path as the first argument');
  if (scratchArg !== undefined && !scratchArg.trim()) return fail('the scratch folder argument is empty: pass a folder or leave it out');
  if (!model?.trim()) return fail('the model argument is empty: pass a model name or leave it out');
  const cap = capArg === undefined ? MAX_LAUNCHES_ALLOWED : parseCap(capArg);
  if (cap === 0) return fail(`the launch cap must be a whole number from 1 to ${MAX_LAUNCHES_ALLOWED}`);
  return { error: '', exe, scratch: scratchArg ?? '', model, maxLaunches: cap };
}

/**
 * The executable and the scratch folder as absolute paths, against the folder the probe was invoked from: a launch runs with
 * another working directory, so a relative one would mean something else there. An empty scratch still means "make one".
 */
export const absoluteInvocation = ({ exe, scratch }, base = process.cwd()) => ({ exe: resolve(base, exe), scratch: scratch === '' ? '' : resolve(base, scratch) });

/**
 * The value of a C# `const string <name> = "..." + "...";`, or null when it is absent or is anything this reader could not read
 * EXACTLY (an escape, a reference to another constant). Read as a sequence of literals, so a `;` inside one does not end it: the
 * follow-up text has one, and a search for the first `;` sent a turn-two prompt cut off in the middle of its last sentence.
 */
export function constStringOf(source, name) {
  const declaration = `const string ${name} =`;
  const start = source.indexOf(declaration);
  if (start < 0) return null;
  const literals = /^\s*("[^"\\\r\n]*"(?:\s*\+\s*"[^"\\\r\n]*")*)\s*;/.exec(source.slice(start + declaration.length))?.[1];
  return literals === undefined ? null : [...literals.matchAll(/"([^"]*)"/g)].map((m) => m[1]).join('');
}

/** The launch cap as a number 1..12, or 0 for anything else (digits only: `4x`, `3.5`, ` 4`, `NaN` and `1e1` are all refused). */
function parseCap(text) {
  const n = /^[1-9]\d{0,2}$/.test(text) ? Number(text) : 0;
  return n <= MAX_LAUNCHES_ALLOWED ? n : 0;
}

/**
 * What agy's stream-json stdout says: the conversation id of the `init` event, the `result` event's result, how many events
 * parsed, and `ignoredLines` — every non-blank line that was NOT a JSON event (a half-written line, log noise): counted, never
 * silently dropped, so a stream that lost its result line is visible in the record.
 */
export function readStream(stdout) {
  const out = { conversation: '', result: null, events: 0, ignoredLines: 0 };
  for (const line of stdout.split('\n').map((l) => l.trim()).filter((l) => l !== '')) {
    const event = parseEvent(line);
    if (event === null) {
      out.ignoredLines++;
      continue;
    }
    out.events++;
    if (event.event === 'init' && typeof event.conversation_id === 'string') out.conversation = event.conversation_id;
    if (event.event === 'result') {
      // A result that is not an object is no result: nothing downstream may read a field of it, and the line stays counted.
      if (isRecord(event.result)) out.result = event.result;
      else out.ignoredLines++;
    }
  }
  return out;
}

const isRecord = (value) => value !== null && typeof value === 'object' && !Array.isArray(value);

function parseEvent(line) {
  if (!line.startsWith('{')) return null;
  try {
    return JSON.parse(line);
  } catch {
    return null;
  }
}

/**
 * The three signs step 1 looks for, derived from one launch and built as a NEW object (nothing is assigned onto a record):
 * a denial (agy's `denied_actions`, or its own auto-denied sentence on stderr), a usable conversation id, an empty answer —
 * and all three together. A `response` that is neither a string nor absent is `malformedResponse`: not an answer and not an empty
 * one, and it never throws here, because the launch it came from was paid for and is saved first; the probe stops on it after.
 */
export function classifyLaunch(result, stderr, conversation) {
  const deniedActions = Array.isArray(result?.denied_actions) ? result.denied_actions : [];
  const deniedBySentence = /auto-denied/.test(stderr);
  const denied = deniedActions.length > 0 || deniedBySentence;
  const hasConversationId = /^[A-Za-z0-9_-]{1,128}$/.test(conversation);
  const response = result?.response ?? '';
  const malformedResponse = typeof response !== 'string';
  const emptyAnswer = !malformedResponse && response.trim() === '';
  return { deniedActions, deniedBySentence, denied, hasConversationId, emptyAnswer, malformedResponse, allThree: denied && hasConversationId && emptyAnswer };
}

/** A launch that exited nonzero, timed out, could not run or end cleanly, or whose result does not say SUCCESS. A launch with no result has status null. */
export const failedLaunch = ({ exit, timedOut, launchError, status }) => exit !== 0 || timedOut || Boolean(launchError) || status !== 'SUCCESS';

/** A completed measurement: a result with a usage block from a launch that did not fail. A failed turn's usage still counts in the spend, which is summed apart. */
export const usableTurn = (turn) => turn.hasResult && turn.usage !== null && !failedLaunch(turn);

/** What a failed launch is searched in for auth and quota trouble: its stderr, the tail of its stdout, and the result's own `error`, whatever shape that has. */
export function failureText(result, stderr, stdout) {
  const error = result?.error ?? '';
  return (typeof error === 'string' ? error : JSON.stringify(error)) + stderr + stdout.slice(-2000);
}

/**
 * '' unless the launch's process tree may not have ended (lib/launch-tree.mjs): the sentence the probe then stops with, before
 * another paid launch could run beside it. On Windows a helper that outlives its parent cannot be reached by a tree kill.
 */
export function treeStop(launchNumber, launchError) {
  if (launchError === 'heldPipes') return `launch ${launchNumber}: a helper held the output open after the CLI ended, and it may still be running; check before another launch`;
  if (launchError === 'notReaped') return `launch ${launchNumber}: the CLI had not ended after the tree kill and may still be running; check before another launch`;
  return '';
}

/**
 * '' when the captured fields of a fixture carry no user name and no @, otherwise what they carry. Only what was captured is
 * read, with the placeholders taken out: the fixture's own fixed words would match an account called `user`.
 */
export function fixtureLeak(captured, user) {
  const text = captured.join('\n').replace(/<(?:checkout|agy-home|scratch|tmp|home|user|email)>/g, '');
  if (user !== '' && text.toLowerCase().includes(user.toLowerCase())) return 'a user name';
  return text.includes('@') ? 'an @' : '';
}

const AUTH = /unauthori[sz]ed|unauthenticated|not (signed|logged) in|sign[- ]?in|log ?in|login|credential|auth(entication|orization)? (error|failed|required)|\b(401|403)\b/i;
const QUOTA = /quota|rate.?limit|resource.?exhausted|too many requests|\b429\b|exceeded|billing/i;

/**
 * '' when `text` (a failed launch's stderr and stdout tail) shows no auth/quota trouble; otherwise the Stop sentence. It names
 * the launch and a CATEGORY word only — the matched text can carry a token, so it never reaches a message; it stays in the
 * launch's saved files.
 */
export function authQuotaStop(launchNumber, text) {
  const category = AUTH.test(text) ? 'auth' : (QUOTA.test(text) ? 'quota' : '');
  return category === '' ? '' : `auth/quota error on launch ${launchNumber}: ${category}`;
}

/** The line printed to stderr before a launch starts. */
export const launchStartLine = (n, max, label) => `[launch ${n}/${max}] ${label} starting…`;

/** The line printed to stderr after a launch ends. */
export const launchDoneLine = ({ n, exit, seconds, inputTokens }) =>
  `[launch ${n}] done: exit ${exit ?? 'none'}, ${seconds.toFixed(1)}s, input ${inputTokens ?? 'unknown'}`;

/** A path as it can be spelled in a JSON or shell text: plain, JSON-escaped, forward slashes, and JSON-escaped with escaped slashes. */
export const pathVariants = (s) => [s, s.replaceAll('\\', '\\\\'), s.replaceAll('\\', '/'), s.replaceAll('\\', '\\\\').replaceAll('/', '\\/')];

const escapeRegExp = (s) => s.replace(/[.*+?^${}()|[\]\\]/g, String.raw`\$&`);

/**
 * `text` with every `[value, placeholder]` pair replaced in every spelling of the value. With `caseInsensitive` (Windows: the
 * drive letter and folder casing differ between the places a path was read from) the match ignores case. An empty value is
 * skipped (it would match everywhere) and a placeholder is taken literally.
 */
export function redactPaths(text, pairs, caseInsensitive) {
  let out = text;
  for (const [value, placeholder] of pairs) {
    if (value === '') continue;
    for (const variant of pathVariants(value)) out = out.replace(new RegExp(escapeRegExp(variant), caseInsensitive ? 'gi' : 'g'), () => placeholder);
  }
  return out;
}
