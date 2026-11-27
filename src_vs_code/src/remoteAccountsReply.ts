import { VENDOR_KEY_SHAPE } from './notifications';
import {
  ACCOUNTS_LINES_MAX, ACCOUNTS_LINE_BYTES_MAX, ACCOUNTS_PROTOCOL, AGY_WINDOWS_MAX, AccountsMode, MANAGER_WORDS, ManagerWord, USAGE_LINES_MAX,
} from './remoteAccountsScript';
import {
  AgyUsage, ClaudeUsage, CodexUsage, agyUsageOf, claudeUsageOf, codexUsageOf, emailOf, orgOf, planOf, versionOf,
} from './remoteAccountsUsage';
import { Framed, framed } from './remoteFraming';

export type { AgyGroup, AgyUsage, AgyWindow, ClaudeUsage, ClaudeWindow, CodexUsage, CodexWindow } from './remoteAccountsUsage';
export {
  ACCOUNTS_LINES_MAX, ACCOUNTS_LINE_BYTES_MAX, ACCOUNTS_PROTOCOL, ACCOUNTS_REPLY_BYTES_MAX, ACCOUNTS_SECONDS, MANAGER_WORDS,
} from './remoteAccountsScript';
export type { ManagerWord } from './remoteAccountsScript';

/**
 * The parser of what a VM answers to the accounts script (E1.S2, spec § A2.5 and § A2.7): the frame of marker lines and data lines
 * becomes ONE typed result. Pure — no process, no file, no `vscode` — so every row of the decision table is a test over a string.
 *
 * <p><b>The script reports facts and this module decides.</b> Only a closed vocabulary is read: an unknown marker, an unknown key, a
 * line that is neither, a marker in the wrong place or twice makes the WHOLE answer `unreadable`, with a word from a closed list, so
 * no hostile line can be skipped and the rest believed. It is bounded (64 lines of 512 bytes; a value is cut at 200 characters),
 * and it never throws: every rule below answers with a value. No arm holds an error's message or a piece of the reply that the rules
 * did not pass, and nothing of a reply is run, used as a path, or written to a log.</p>
 *
 * <p>The decision runs in the order of the spec's table: the exit code, the frame, the caps, the line forms, the markers and the keys,
 * the one `COAI-BAD` record, the arrangement, the closed values, and only then the vendor's own values, each through `plainValue` and
 * the credential net. The first rule that fails names the fault.</p>
 */

/** Why a whole answer cannot be used. A closed list; `ACCOUNTS_UNREADABLE_SAID` has one clause for each. */
export type AccountsUnreadable =
  | 'exit' | 'no-sentinel' | 'version' | 'cut' | 'too-many' | 'too-long'
  | 'line' | 'marker' | 'key' | 'value' | 'shape'
  | 'operands' | 'home' | 'folder' | 'tool' | 'unsafe';

/** A program that another user can write, refused before its start as UID 0 (§ R2.6): the path that failed the check, or `null` when it is not shown. */
export interface UnsafeSide { readonly state: 'unsafe'; readonly path: string | null }

export type ClaudeSide =
  | { readonly state: 'absent' }
  | UnsafeSide
  | { readonly state: 'signed-out' }
  | { readonly state: 'unreadable'; readonly why: 'no-jq' | 'auth' | 'timeout' | 'credential-shape' }
  | {
    readonly state: 'found'; readonly version: string | null;
    /** `''` = the account is not known. */
    readonly email: string;
    readonly org: string | null; readonly plan: string | null; readonly usage: ClaudeUsage;
  };

export type CodexSide =
  | { readonly state: 'absent' }
  | UnsafeSide
  | { readonly state: 'unreadable'; readonly why: 'no-jq' | 'credential-shape' }
  | {
    readonly state: 'found'; readonly signInFile: boolean;
    /** `''` = the account is not known. */
    readonly email: string;
    readonly plan: string | null; readonly usage: CodexUsage;
  };

export type AgySide =
  | { readonly state: 'absent' }
  | UnsafeSide
  | { readonly state: 'unreadable'; readonly why: 'credential-shape' }
  | {
    readonly state: 'found'; readonly version: string | null; readonly signInFile: boolean;
    /** `''` = the account is not known: agy's log had no such line. */
    readonly email: string;
    readonly usage: AgyUsage;
  };

export type AccountsRead =
  | { readonly kind: 'unreadable'; readonly why: Exclude<AccountsUnreadable, 'unsafe'> }
  /** `COAI-BAD unsafe`: a tool of the place is writable by another user, as UID 0 (§ R2.6). */
  | { readonly kind: 'unreadable'; readonly why: 'unsafe'; readonly path: string | null }
  /** The reply to the usage mode: the Claude part and nothing else (§ R2.7). Never `read`. */
  | { readonly kind: 'claude-only'; readonly claude: ClaudeSide; readonly jqMissing: ManagerWord | undefined }
  | {
    readonly kind: 'read'; readonly claude: ClaudeSide; readonly codex: CodexSide; readonly agy: AgySide;
    /** The manager word whenever the VM said `jq` is missing, also on a host where no vendor needs it. */
    readonly jqMissing: ManagerWord | undefined;
  };

/** One clause for each fault, all different, none with data from the answer, for ONE sentence of the list. */
export const ACCOUNTS_UNREADABLE_SAID: Readonly<Record<AccountsUnreadable, string>> = {
  exit: 'the remote command ended with an error',
  'no-sentinel': 'the answer did not start as expected',
  version: 'the answer is in a form this version does not know',
  cut: 'the answer stopped before its end',
  'too-many': 'the answer has more lines than a reply may have',
  'too-long': 'the answer has a line longer than a reply may have',
  line: 'the answer holds a line that is neither a marker nor data',
  marker: 'the answer holds a marker this version does not know',
  key: 'the answer holds a key this version does not know',
  value: 'the answer holds a value that is not in the form of its key',
  shape: 'the parts of the answer do not fit together',
  operands: 'the remote script refused what it was asked',
  home: 'the remote account has no home folder',
  folder: 'the remote script could not go to the folder /',
  tool: 'a tool the read needs (id, timeout, tail, sleep, sed, grep or sha256sum) is not in a system folder there',
  unsafe: 'a program the read needs is writable by another user',
};

// ───────────────── the value rules that need no vendor: plain text and the credential net ─────────────────

/** The ONE place of `src/` that holds the rule "control and format characters out": bell, escape, bidi marks, a zero-width space. */
export const withoutControls = (text: string): string => text.replace(/[\p{Cc}\p{Cf}]/gu, '');

/**
 * THE road by which text from a VM becomes a value: control and format characters out, every `$(` written `$ (` so nothing a VM
 * sent can be drawn as an icon by a list, and the text cut at 200 characters. Doing it twice changes nothing.
 */
export function plainValue(text: string): string {
  return [...withoutControls(text).replaceAll('$(', '$ (')].slice(0, 200).join('');
}

/**
 * The net of a value that looks like a credential: a run of 40 or more characters of letters, digits, `_` and `-`; the product's own
 * vendor key shapes (a pattern built here, ONCE, from the shared one's source — the shared object has the flag `g` and keeps state
 * between two `test` calls, so it is never tested); a JWT, a Google access token, a PEM block. A net, not the measure: the measure
 * is that the script has no line that opens a credential file. A false positive makes a real account unreadable, the safe direction.
 */
const CREDENTIAL_SHAPES: readonly RegExp[] = [
  /[A-Za-z0-9_-]{40,}/u,
  new RegExp(VENDOR_KEY_SHAPE.source),
  /eyJ[A-Za-z0-9._-]{16,}/u,
  /ya29\.[A-Za-z0-9._-]{16,}/u,
  /-----BEGIN/u,
];

export const credentialShaped = (value: string): boolean => CREDENTIAL_SHAPES.some((shape) => shape.test(value));

// ───────────────── the grammar: ONE table that the checks and the tests walk ─────────────────

/** How many times a key may stand under its marker: at least, at most. */
type Count = readonly [min: number, max: number];
const ONCE: Count = [1, 1];
const MAYBE: Count = [0, 1];
const NONE: Readonly<Record<string, Count>> = {};

const BAD_WORDS = ['operands', 'home', 'folder', 'tool'] as const;
const ONE_PATH: Readonly<Record<string, Count>> = { path: ONCE };

/** A row for each word of a marker: the marker's whole line, and the keys its data lines may hold. */
const wordRows = (head: string, words: readonly string[], keys: Readonly<Record<string, Count>> = NONE): readonly (readonly [string, Readonly<Record<string, Count>>])[] => words.map((word) => [`${head} ${word}`, keys]);

/**
 * Every marker line the script can print, and for each the keys of its data lines with how often each may stand. A line that is not
 * a key of this table is the fault `marker`; a key that is not in its row, or stands too often, is `key`; one that stands too
 * seldom makes the parts not fit (`shape`). The slots of Codex's windows (`primary`, `secondary`) are not in it: a window is named by its length; an agy window by its group and its length.
 */
export const ACCOUNTS_GRAMMAR: Readonly<Record<string, Readonly<Record<string, Count>>>> = Object.fromEntries([
  ...wordRows('COAI-BAD', BAD_WORDS),
  ['COAI-BAD unsafe', ONE_PATH],
  ['COAI-JQ found', NONE],
  ...wordRows('COAI-JQ missing', MANAGER_WORDS),
  ['COAI-VENDOR claude found', { version: MAYBE, email: MAYBE, org: MAYBE, plan: MAYBE }],
  ...wordRows('COAI-VENDOR claude', ['absent', 'signed-out', 'unreadable no-jq', 'unreadable auth', 'unreadable timeout']),
  ['COAI-VENDOR claude unsafe', ONE_PATH],
  ['COAI-USAGE claude read', { turns: ONCE, line: [0, USAGE_LINES_MAX], unmatched: ONCE }],
  ...wordRows('COAI-USAGE claude', ['skipped', 'skipped account-changed', 'failed timeout', 'failed error']),
  ['COAI-VENDOR codex found', { 'sign-in-file': ONCE, account: MAYBE, plan: MAYBE }],
  ...wordRows('COAI-VENDOR codex', ['absent', 'unreadable no-jq']),
  ['COAI-VENDOR codex unsafe', ONE_PATH],
  ['COAI-USAGE codex read', { window: [0, 2] }],
  ...wordRows('COAI-USAGE codex', ['failed none', 'failed error']),
  ['COAI-VENDOR agy found', { version: MAYBE, 'sign-in-file': ONCE, account: MAYBE }],
  ['COAI-VENDOR agy absent', NONE],
  ['COAI-VENDOR agy unsafe', ONE_PATH],
  ['COAI-USAGE agy read', { quota: [0, AGY_WINDOWS_MAX] }],
  ...wordRows('COAI-USAGE agy', ['skipped', 'failed timeout', 'failed error', 'failed model-call', 'failed no-jq']),
]);

/** The keys whose values are in a closed form (§ A2.5): the others are text, and `plainValue` and the credential net judge them. */
const DIGITS = /^\d{1,6}$/u;
/** A path the script printed: the dash, or an absolute path of at most 200 characters of `[A-Za-z0-9._/@+-]` (§ R2.6). */
const PATH_WORD = /^(?:-|\/[A-Za-z0-9._/@+-]{0,199})$/u;
const CLOSED: Readonly<Record<string, (value: string) => boolean>> = {
  turns: (value) => DIGITS.test(value),
  unmatched: (value) => DIGITS.test(value),
  'sign-in-file': (value) => value === 'present' || value === 'absent',
  path: (value) => PATH_WORD.test(value),
  window: (value) => value.split(' ').length === 3,
  quota: (value) => value.split(' ').length === 4,
};

// ───────────────── the records ─────────────────

interface Datum { readonly key: string; readonly value: string }
/** A marker line and the data lines after it. */
interface Rec { readonly head: string; readonly data: readonly Datum[] }

function datumOf(line: string): Datum {
  const rest = line.slice(1);
  const at = rest.indexOf(' ');

  return at < 0 ? { key: rest, value: '' } : { key: rest.slice(0, at), value: rest.slice(at + 1) };
}

/** The frame as records. The data of the LAST record is appended to in place: a local accumulator that never leaves this function. */
function recordsOf(lines: readonly string[]): readonly Rec[] {
  const recs: { head: string; data: Datum[] }[] = [];
  for (const line of lines) {
    if (line.startsWith(':')) {
      (recs[recs.length - 1] as { data: Datum[] }).data.push(datumOf(line));
    } else {
      recs.push({ head: line, data: [] });
    }
  }

  return recs;
}

const valuesOf = (rec: Rec, key: string): readonly string[] => rec.data.filter((one) => one.key === key).map((one) => one.value);
/** The value of a key that the arrangement rule has shown to be there. */
const valueOf = (rec: Rec, key: string): string => valuesOf(rec, key)[0] as string;
const rowOf = (rec: Rec): Readonly<Record<string, Count>> => ACCOUNTS_GRAMMAR[rec.head] ?? NONE;

// ───────────────── the rules, in the order of the table ─────────────────

type Fault = AccountsUnreadable | undefined;
type Lines = { readonly lines: readonly string[] } | { readonly why: AccountsUnreadable };

const isMarkerOrData = (line: string): boolean => line.startsWith('COAI-') || line.startsWith(':');

/** Every line a marker or data, and the first a marker: data belongs to the marker before it. */
const formFault = (lines: readonly string[]): Fault => (!lines.every(isMarkerOrData) || (lines[0] ?? '').startsWith(':') ? 'line' : undefined);

/** Rule 3 and 4: at most 64 lines, each at most 512 bytes (bytes, not characters); then the form of each line. */
function lineFault(lines: readonly string[]): Fault {
  if (lines.length > ACCOUNTS_LINES_MAX) {
    return 'too-many';
  }
  if (lines.some((line) => Buffer.byteLength(line) > ACCOUNTS_LINE_BYTES_MAX)) {
    return 'too-long';
  }

  return formFault(lines);
}

const SENTINELS = { begin: `COAI-ACCOUNTS ${ACCOUNTS_PROTOCOL}`, family: 'COAI-ACCOUNTS ', end: 'COAI-ACCOUNTS-END' };

const missOf = (found: Exclude<Framed, { kind: 'framed' }>): AccountsUnreadable => {
  if (found.kind === 'no-end') {
    return 'cut';
  }

  return found.otherVersion ? 'version' : 'no-sentinel';
};

function checkedLines(lines: readonly string[]): Lines {
  const why = lineFault(lines);

  return why === undefined ? { lines } : { why };
}

/** Rule 1 and 2: the exit code, and the frame between the LAST begin line and the first end line after it. */
function linesOf(run: { readonly code: number; readonly stdout: string }): Lines {
  if (run.code !== 0) {
    return { why: 'exit' };
  }
  const found = framed(run.stdout, SENTINELS);

  return found.kind === 'framed' ? checkedLines(found.lines) : { why: missOf(found) };
}

/** Rule 5: every marker is in the table (all of them first), then every key is one its marker takes, no more often than the row says. */
function grammarFault(recs: readonly Rec[]): Fault {
  if (recs.some((rec) => !Object.hasOwn(ACCOUNTS_GRAMMAR, rec.head))) {
    return 'marker';
  }

  return recs.some((rec) => rec.data.some((one) => !Object.hasOwn(rowOf(rec), one.key) || valuesOf(rec, one.key).length > (rowOf(rec)[one.key] as Count)[1])) ? 'key' : undefined;
}

/** `COAI-BAD unsafe` holds its `path` once, and in the closed form: no path is `shape`, one that is not closed is `value`. */
function unsafeFault(bad: Rec): Fault {
  const path = valuesOf(bad, 'path')[0];
  if (path === undefined) {
    return 'shape';
  }

  return CLOSED['path']?.(path) === true ? 'unsafe' : 'value';
}

/** Rule 6: a `COAI-BAD <word>` is the only record, and then the answer is unreadable with that word; beside any other record it is `shape`. */
function badFault(recs: readonly Rec[]): Fault {
  const bad = recs.find((rec) => rec.head.startsWith('COAI-BAD '));
  if (bad === undefined) {
    return undefined;
  }
  if (recs.length !== 1) {
    return 'shape';
  }

  return bad.head === 'COAI-BAD unsafe' ? unsafeFault(bad) : bad.head.slice('COAI-BAD '.length) as AccountsUnreadable;
}

/** One vendor's records: `found` and the `COAI-USAGE` line that follows it, or any other state alone. */
const vendorPart = (name: string): string => `(?:COAI-VENDOR ${name} found\\nCOAI-USAGE ${name} [^\\n]+|COAI-VENDOR ${name} (?!found)[^\\n]+)`;

/** The arrangement by the mode that was SENT (§ R2.7): the first mode prints the three parts, the usage mode the Claude part and NOTHING else. */
const ARRANGEMENT: Readonly<Record<AccountsMode, RegExp>> = {
  'no-claude-usage': new RegExp(['^COAI-JQ [^\\n]+', ...['claude', 'codex', 'agy'].map(vendorPart)].join('\\n') + '$', 'u'),
  'claude-usage': new RegExp(['^COAI-JQ [^\\n]+', vendorPart('claude')].join('\\n') + '$', 'u'),
};

/** The state words of a vendor's `COAI-VENDOR` line: `found`, `absent`, `unreadable no-jq`, … */
function stateOf(recs: readonly Rec[], name: string): string {
  const prefix = `COAI-VENDOR ${name} `;

  return (recs.find((rec) => rec.head.startsWith(prefix))?.head ?? prefix).slice(prefix.length);
}

const keysMissing = (recs: readonly Rec[]): boolean => recs.some((rec) => Object.entries(rowOf(rec)).some(([key, [min]]) => valuesOf(rec, key).length < min));

/**
 * `COAI-JQ missing` goes only with `absent`, `unsafe` (refused before `jq` is asked for) or `unreadable no-jq` for Claude and for Codex, and `COAI-JQ found` never with
 * `no-jq`. A found agy says `failed no-jq` exactly when `jq` is missing. A vendor that the reply does not hold (the usage mode has no Codex) says nothing.
 */
function jqMismatch(recs: readonly Rec[]): boolean {
  const missing = (recs[0] as Rec).head.startsWith('COAI-JQ missing ');
  const states = [stateOf(recs, 'claude'), stateOf(recs, 'codex')].filter((state) => state !== '');
  const agyAsked = stateOf(recs, 'agy') === 'found' && recs.some((rec) => rec.head === 'COAI-USAGE agy failed no-jq') !== missing;

  return agyAsked || (missing ? states.some((state) => state !== 'absent' && state !== 'unsafe' && state !== 'unreadable no-jq') : states.some((state) => state === 'unreadable no-jq'));
}

/** For `no-claude-usage` a found Claude has `skipped`; for `claude-usage` it never has. */
function modeMismatch(recs: readonly Rec[], mode: AccountsMode): boolean {
  const skipped = recs.some((rec) => rec.head === 'COAI-USAGE claude skipped');

  return stateOf(recs, 'claude') === 'found' && skipped !== (mode === 'no-claude-usage');
}

/** The arrangement by the mode that was sent: what comes after the `COAI-JQ` line is the first mode's three parts or the usage mode's one. The fault is `shape`. */
const modeFault = (recs: readonly Rec[], mode: AccountsMode): Fault => (ARRANGEMENT[mode].test(recs.map((rec) => rec.head).join('\n')) ? undefined : 'shape');

/** What breaks the arrangement, in order: a later check may rely on an earlier one (it reads the first record and the vendors' lines). */
const SHAPE_BREAKS: readonly ((recs: readonly Rec[], mode: AccountsMode) => boolean)[] = [keysMissing, jqMismatch, modeMismatch];

/** Rule 7: the arrangement, the keys that must be there, `COAI-JQ` against the vendors, the mode. */
const shapeFault = (recs: readonly Rec[], mode: AccountsMode): Fault => (SHAPE_BREAKS.some((breaks) => breaks(recs, mode)) ? 'shape' : undefined);

/** Rule 8: the values of the closed keys are in their form. The raw value is judged, so a control character cannot make a bad value good. */
function valueFault(recs: readonly Rec[]): Fault {
  return recs.some((rec) => rec.data.some((one) => !(CLOSED[one.key]?.(one.value) ?? true))) ? 'value' : undefined;
}

const CHECKS: readonly ((recs: readonly Rec[], mode: AccountsMode) => Fault)[] = [grammarFault, badFault, modeFault, shapeFault, valueFault];

function firstFault(recs: readonly Rec[], mode: AccountsMode): Fault {
  for (const check of CHECKS) {
    const why = check(recs, mode);
    if (why !== undefined) {
      return why;
    }
  }

  return undefined;
}

// ───────────────── the typed result ─────────────────

/** A vendor's own records: its `COAI-VENDOR` line and, when it is found, the `COAI-USAGE` line after it. */
interface Part { readonly vendor: Rec; readonly usage: Rec | undefined }

function partOf(recs: readonly Rec[], name: string): Part {
  const at = recs.findIndex((rec) => rec.head.startsWith(`COAI-VENDOR ${name} `));
  const vendor = recs[at] as Rec;

  return { vendor, usage: vendor.head.endsWith(' found') ? recs[at + 1] : undefined };
}

const wordsAfter = (rec: Rec, prefix: string): string => rec.head.slice(prefix.length);

/** Rule 9: one value of the vendor that looks like a credential, and the vendor is unreadable with every other value of it. */
const credentialIn = (part: Part): boolean => [part.vendor, part.usage].some((rec) => rec?.data.some((one) => credentialShaped(plainValue(one.value))) === true);

/** The value of a key through `plainValue` and the text rule of the field; `absent` when the key is not there. */
function ruled<T>(rec: Rec, key: string, rule: (plain: string) => T, absent: T): T {
  const value = valuesOf(rec, key)[0];

  return value === undefined ? absent : rule(plainValue(value));
}

const plainValues = (rec: Rec, key: string): readonly string[] => valuesOf(rec, key).map((value) => plainValue(value));

const CLAUDE_USAGE_FLAT: Readonly<Record<string, ClaudeUsage>> = {
  skipped: { state: 'skipped' },
  'skipped account-changed': { state: 'skipped', why: 'account-changed' },
  'failed timeout': { state: 'failed', why: 'timeout' },
  'failed error': { state: 'failed', why: 'error' },
};

function claudeUsageSide(rec: Rec, nowMs: number): ClaudeUsage {
  const flat = CLAUDE_USAGE_FLAT[wordsAfter(rec, 'COAI-USAGE claude ')];

  return flat ?? claudeUsageOf(Number(valueOf(rec, 'turns')), plainValues(rec, 'line'), Number(valueOf(rec, 'unmatched')), nowMs);
}

const CLAUDE_FLAT: Readonly<Record<string, ClaudeSide>> = {
  absent: { state: 'absent' },
  'signed-out': { state: 'signed-out' },
  'unreadable no-jq': { state: 'unreadable', why: 'no-jq' },
  'unreadable auth': { state: 'unreadable', why: 'auth' },
  'unreadable timeout': { state: 'unreadable', why: 'timeout' },
};

/** A path the script printed, as text: `-` and a path in the shape of a credential are not shown. */
function pathOf(value: string): string | null {
  const plain = plainValue(value);

  return plain === '-' || credentialShaped(plain) ? null : plain;
}

const unsafeOf = (rec: Rec): UnsafeSide => ({ state: 'unsafe', path: pathOf(valueOf(rec, 'path')) });

function claudeOf(part: Part, nowMs: number): ClaudeSide {
  if (part.vendor.head.endsWith(' unsafe')) {
    return unsafeOf(part.vendor);
  }
  const flat = CLAUDE_FLAT[wordsAfter(part.vendor, 'COAI-VENDOR claude ')];
  if (flat !== undefined) {
    return flat;
  }

  return credentialIn(part) ? { state: 'unreadable', why: 'credential-shape' } : {
    state: 'found',
    version: ruled(part.vendor, 'version', versionOf, null),
    email: ruled(part.vendor, 'email', emailOf, ''),
    org: ruled(part.vendor, 'org', orgOf, null),
    plan: ruled(part.vendor, 'plan', planOf, null),
    usage: claudeUsageSide(part.usage as Rec, nowMs),
  };
}

const CODEX_USAGE_FLAT: Readonly<Record<string, CodexUsage>> = {
  'failed none': { state: 'failed', why: 'none' },
  'failed error': { state: 'failed', why: 'error' },
};

function codexUsageSide(rec: Rec): CodexUsage {
  const flat = CODEX_USAGE_FLAT[wordsAfter(rec, 'COAI-USAGE codex ')];

  return flat ?? codexUsageOf(plainValues(rec, 'window'));
}

const CODEX_FLAT: Readonly<Record<string, CodexSide>> = {
  absent: { state: 'absent' },
  'unreadable no-jq': { state: 'unreadable', why: 'no-jq' },
};

function codexOf(part: Part): CodexSide {
  if (part.vendor.head.endsWith(' unsafe')) {
    return unsafeOf(part.vendor);
  }
  const flat = CODEX_FLAT[wordsAfter(part.vendor, 'COAI-VENDOR codex ')];
  if (flat !== undefined) {
    return flat;
  }

  return credentialIn(part) ? { state: 'unreadable', why: 'credential-shape' } : {
    state: 'found',
    signInFile: valueOf(part.vendor, 'sign-in-file') === 'present',
    email: ruled(part.vendor, 'account', emailOf, ''),
    plan: ruled(part.vendor, 'plan', planOf, null),
    usage: codexUsageSide(part.usage as Rec),
  };
}

const AGY_USAGE_FLAT: Readonly<Record<string, AgyUsage>> = {
  skipped: { state: 'skipped' },
  'failed timeout': { state: 'failed', why: 'timeout' },
  'failed error': { state: 'failed', why: 'error' },
  'failed model-call': { state: 'failed', why: 'model-call' },
  'failed no-jq': { state: 'failed', why: 'no-jq' },
};

const agyUsageSide = (rec: Rec): AgyUsage => AGY_USAGE_FLAT[wordsAfter(rec, 'COAI-USAGE agy ')] ?? agyUsageOf(plainValues(rec, 'quota'));

function agyOf(part: Part): AgySide {
  if (part.vendor.head.endsWith(' absent')) {
    return { state: 'absent' };
  }
  if (part.vendor.head.endsWith(' unsafe')) {
    return unsafeOf(part.vendor);
  }

  return credentialIn(part) ? { state: 'unreadable', why: 'credential-shape' } : {
    state: 'found',
    version: ruled(part.vendor, 'version', versionOf, null),
    signInFile: valueOf(part.vendor, 'sign-in-file') === 'present',
    email: ruled(part.vendor, 'account', emailOf, ''),
    usage: agyUsageSide(part.usage as Rec),
  };
}

/** The manager word of `COAI-JQ missing <word>`: the grammar has already shown it is one of the five. */
const jqMissingOf = (recs: readonly Rec[]): ManagerWord | undefined => {
  const head = (recs[0] as Rec).head;

  return head.startsWith('COAI-JQ missing ') ? head.slice('COAI-JQ missing '.length) as ManagerWord : undefined;
};

/**
 * What the VM answered, as one value. The caller passes the `run` it got, the SAME `mode` it sent, and the clock of its round
 * (`nowMs`: the reset text has no year, and a clock read inside a pure parser would make its tests expire). Never throws.
 */
export function readAccounts(run: { readonly code: number; readonly stdout: string }, mode: AccountsMode, nowMs: number): AccountsRead {
  const got = linesOf(run);
  if ('why' in got) {
    return { kind: 'unreadable', why: got.why as Exclude<AccountsUnreadable, 'unsafe'> };
  }
  const recs = recordsOf(got.lines);
  const why = firstFault(recs, mode);

  return why === undefined ? readOfRecords(recs, mode, nowMs) : unreadableOf(why, recs);
}

/** `unsafe` is the one fault that carries data: the path of the first record, which the checks have shown to be there and closed. */
function unreadableOf(why: AccountsUnreadable, recs: readonly Rec[]): AccountsRead {
  return why === 'unsafe' ? { kind: 'unreadable', why, path: pathOf(valueOf(recs[0] as Rec, 'path')) } : { kind: 'unreadable', why };
}

/** The typed result of a reply the rules passed: the usage mode has the Claude part only (`claude-only`), the first mode the three parts (`read`). */
function readOfRecords(recs: readonly Rec[], mode: AccountsMode, nowMs: number): AccountsRead {
  const claude = claudeOf(partOf(recs, 'claude'), nowMs);
  if (mode === 'claude-usage') {
    return { kind: 'claude-only', claude, jqMissing: jqMissingOf(recs) };
  }

  return { kind: 'read', claude, codex: codexOf(partOf(recs, 'codex')), agy: agyOf(partOf(recs, 'agy')), jqMissing: jqMissingOf(recs) };
}
