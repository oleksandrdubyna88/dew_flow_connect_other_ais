// lib/agy-probe.mjs — the pure parts of scripts/probe-agy-review-follow-up.mjs (a PAID probe that is never run by a test): the
// command line, the stream reader, how a launch is classified, the sentence an auth/quota stop prints, the console progress
// lines and the fixture's path redaction. The input-token budget is lib/spendCap.mjs; the launch itself is lib/launch-tree.mjs.
import { statSync } from 'node:fs';

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
    if (event.event === 'result') out.result = event.result;
  }
  return out;
}

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
 * and all three together.
 */
export function classifyLaunch(result, stderr, conversation) {
  const deniedActions = Array.isArray(result?.denied_actions) ? result.denied_actions : [];
  const deniedBySentence = /auto-denied/.test(stderr);
  const denied = deniedActions.length > 0 || deniedBySentence;
  const hasConversationId = /^[A-Za-z0-9_-]{1,128}$/.test(conversation);
  const emptyAnswer = (result?.response ?? '').trim() === '';
  return { deniedActions, deniedBySentence, denied, hasConversationId, emptyAnswer, allThree: denied && hasConversationId && emptyAnswer };
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
