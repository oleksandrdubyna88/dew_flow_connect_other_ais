import { namesACredential } from './credentialWords';

/**
 * One thing coai told a person, as it is written down.
 *
 * <p>The extension has 109 `window.show*Message` call sites and, until this ledger, no durable
 * record of any of them: no output channel, no log file, no telemetry. A message a person misses
 * is a message that never happened, and on 2026-09-16 that cost ninety minutes — a settings-mirror
 * stand-down warned once, nobody saw it, and eleven code rounds ran against a role deleted
 * thirty-eight minutes earlier. Design record:
 * `todo/PLAN_every_message_is_written_down.md`.</p>
 *
 * <p>This module holds what a RECORD means: its shape, the one line it becomes, and the line read
 * back. It knows nothing about a disk — that is `notificationsFile.ts`, the same split
 * `chatDoors.ts` / `chatDoorsFile.ts` already uses.</p>
 */

/**
 * What the person is supposed to DO about it, which is what a log has to sort on.
 *
 * <p>Not severity. 68 of the 109 call sites are `showWarningMessage`, and that bucket mixes a
 * refusal, a failure and a modal question — three different things to do. Six classes, derived from
 * the call sites rather than invented.</p>
 *
 * <p><b>There is no `progress`.</b> The inventory counted `withProgress` as a seventh class and the
 * plan round was right that it cannot be one: it is a different API, this ledger's funnel wraps
 * `show*Message`, so a `progress` member could never receive a record while a completeness test went
 * on reporting full coverage. A taxonomy with an unreachable member is worse than a smaller
 * taxonomy. The 7 `withProgress` sites are out of scope, and their failures arrive here afterwards
 * as `refusal` or `failure` anyway. (Operator ruling, 2026-09-16.)</p>
 */
export type NotificationClass =
  | 'refusal'
  | 'failure'
  | 'confirmation'
  | 'outcome'
  | 'offer'
  | 'stand-down'
  | 'storm';

/**
 * Every class, in the order a reader meets them: what needs acting on first, what is merely news.
 *
 * <p><b>The one catalog.</b> The page's tab strip is derived from this rather than listing the
 * classes again — a second list is a defect from the moment it compiles, and this one would drift
 * silently in the worst direction: a class added to the parser would be 'known', so it would not go
 * to the `other` tab, and it would have no tab of its own either. Its rows would simply not appear.
 * (codex, the S5 code round, from two roles.)</p>
 */
export const CLASS_ORDER: readonly NotificationClass[] = [
  'failure', 'refusal', 'stand-down', 'storm', 'confirmation', 'offer', 'outcome',
];

const CLASSES: ReadonlySet<string> = new Set<NotificationClass>(CLASS_ORDER);

/** Everything a record can carry. Four fields are required; a later field is added optional. */
export interface NotificationRecord {
  readonly utc: string;
  readonly class: NotificationClass;
  /** The module that raised it — `serverSettingsSync`, `rolesPanel`. Free text, grouped on. */
  readonly source: string;
  /**
   * The KEY, and it is a string literal at the call site — never prose.
   *
   * <p>The second draft of the plan keyed repeats on the rendered `title`, and the plan round found
   * what that costs: a title carries versions, paths, role names and round numbers, so the Role2
   * complaint — which names its round — would have minted eleven keys across eleven rounds, counted
   * one each, and never reached any threshold. <b>The mechanism built for that incident would not
   * have caught that incident.</b> A literal `code` is also what bounds the key space to the number
   * of call sites, which is what bounds every counter built on it.</p>
   */
  readonly code: string;
  /** The resource this one is about, when a `code` can be about more than one. */
  readonly subject?: string;
  readonly title?: string;
  readonly detail?: string;
  readonly cure?: string;
  /** The button offered, when one was. */
  readonly action?: string;
  /**
   * Every button offered, in order, when there was more than one.
   *
   * <p>An audit that can see what somebody chose but not what they were choosing between is half an
   * audit. The call site this matters most for is the `.wslconfig` modal, which offers *Copy `wsl
   * --shutdown`* and *Put it back to nat* — the second of which changes a machine's networking, and
   * neither of which the record mentioned. (codex, the code round.)</p>
   */
  readonly offered?: string;
  /** What the person pressed — recorded for a confirmation, whose ANSWER is the event. */
  readonly answer?: string;
  /**
   * An id minted once per host start.
   *
   * <p>Not the pid: pids are reused, so a pid cannot identify a process lifetime and two unrelated
   * incidents months apart would group into one row.</p>
   */
  readonly run?: string;
  readonly pid?: number;
  /**
   * This occurrence's ordinal for `(code, subject)` within this run — DIAGNOSTIC ONLY.
   *
   * <p>Counting is counting rows. Nothing a reader sees is derived from this, because any in-memory
   * counter can be evicted or lost and a count computed from one would then be wrong on screen. A
   * count computed from rows cannot be.</p>
   */
  readonly seq?: number;
  /**
   * Which bound a `storm` record marks, and nothing else carries it.
   *
   * <p>The once-only promise for a storm is a READ-time invariant, not a write-time one: there is no
   * test-and-set on an append-only file, so two windows crossing the same threshold for the same
   * fault each write one. The page groups on `(code, subject, bound)` and shows one row, which is
   * why the threshold has to be a field rather than a sentence inside `detail`.</p>
   */
  readonly bound?: number;
  readonly repo?: string;
  readonly branch?: string;
  readonly session?: string;
  readonly provider?: string;
  readonly role?: string;
  /**
   * Fields this build has never heard of, kept rather than dropped.
   *
   * <p>The same bargain the class field already makes: *"a newer build writing a class this one has
   * never heard of must not make its records vanish from an older reader"*. It applies harder to the
   * server's file than to this side's, because the two halves ship SEPARATELY — the Team server is
   * deployed by hand — so an extension one release behind reads a `server-notices.jsonl` written by
   * a newer server as a matter of routine. Without this, a field S8 adds is dropped in silence while
   * both halves appear to have accepted the row. (codex, the code round.)</p>
   *
   * <p>It is flattened back on the way out, so a record that is read and written again is the record
   * that arrived. Strings and finite numbers only, and bounded: an unknown value that is an object
   * or an array is not kept, because the one thing a line in this file must stay is flat.</p>
   */
  readonly more?: Readonly<Record<string, string | number>>;
}

/**
 * What a title may be before it is cut.
 *
 * <p><b>1000, where the plan said 200 — a deliberate deviation.</b> `title` is the text the person
 * was actually shown, and several messages here are longer than two hundred characters (the
 * data-directory ones run to a paragraph). A ledger that truncates the message it claims to have
 * recorded is not more honest for having a smaller number in it; a toast past a thousand characters
 * is already unreadable, so nothing real is lost at this bound.</p>
 */
export const TITLE_LIMIT = 1000;

/**
 * What a detail may be before it is cut.
 *
 * <p>`detail` carries exception text, and exception text here can be an HTTP response body or a
 * megabyte of stack. Unbounded, one record then exceeds any file bound on its own, is re-parsed on
 * every read, and — the part that actually bites — makes the redaction patterns run over megabytes,
 * which is a hung extension host rather than a slow function.</p>
 */
export const DETAIL_LIMIT = 4096;

/** What replaces a secret. Visible, so a reader knows something was taken out rather than missing. */
const REDACTED = '[redacted]';

/** Said when a value was cut, for the same reason. */
const TRUNCATED = '…(truncated)';

/**
 * Every pattern that takes a secret out of a string.
 *
 * <p><b>Bounded quantifiers, no nesting, no backreferences.</b> These run over text nobody chose —
 * a vendor's stderr, a server's response body — and a pattern that can backtrack over attacker-shaped
 * input is a frozen extension host, not a slow function. Every repetition below has an upper
 * bound.</p>
 */
const SECRETS: ReadonlyArray<readonly [RegExp, string]> = [
  // A URL carrying its own credentials: https://someone:password@host
  [/\b([a-z][a-z0-9+.-]{0,15}:\/\/)[^\s/@:]{1,256}:[^\s/@]{1,256}@/gi, `$1${REDACTED}@`],
  // An Authorization header, or anything that spells one out.
  // Lower case only because the flag is `i`: spelling `A-Za-z` beside `a-z` under a
  // case-insensitive match is a duplicated range, which is what Sonar reports and what makes a
  // pattern harder to read than it needs to be.
  [/\b(bearer|basic|token)[ \t]+[a-z0-9._~+/=-]{8,4096}/gi, `$1 ${REDACTED}`],
  // Vendor key shapes that are unmistakable on sight.
  [/\b(sk-|ghp_|gho_|ghu_|ghs_|github_pat_|xox[baprs]-)[A-Za-z0-9._-]{8,512}/g, `$1${REDACTED}`],
];

/** A query or fragment parameter, so its NAME can be judged before its value is kept. */
const PARAMETER = /([?&#])([^\s=&#]{1,64})=([^\s&#]{1,4096})/g;

/**
 * A credential NAMED and then given, anywhere in ordinary text.
 *
 * <p>`password=letmein`, `api_key: abc123`, `"client_secret": "..."` — none of which is a URL
 * parameter, an Authorization header or a vendor key shape, so none of which the three patterns
 * above touch. They reach this ledger constantly: these records are built from text nobody chose,
 * and a server that refuses a request tends to quote what it was given back at you. (codex, the
 * code round.)</p>
 *
 * <p>The optional quotes are matched without a backreference on purpose — every pattern that runs
 * over arbitrary text here has bounded repetition and no backtracking trap, and a backreference is
 * how that promise gets quietly broken. A JSON body redacts as `"password": "[redacted]"`, with the
 * quotes kept, because the line still has to read like what it was.</p>
 */
const LABELLED = /([A-Za-z][A-Za-z0-9_.-]{0,63})("?[ \t]{0,4}[:=][ \t]{0,4}"?)([^\s"',;)}]{1,4096})/g;

/**
 * Whether a character may be written down at all.
 *
 * <p><b>Code points, not a literal and not a character class</b> — and that is not a style
 * preference. `serverSettingsSync.displayable` carries the same comparison with a comment saying
 * why: *"the class has to be SPELLED, and the first attempt at spelling it put a real NUL byte in
 * this file."* This module then did it again — a DEL escape here and a NUL escape in its test reached
 * disk as RAW bytes, and git stopped treating both files as text, which is how it was noticed. No
 * escape, no class, no literal: the numbers.</p>
 */
function isPrintable(character: string): boolean {
  const code = character.codePointAt(0) ?? 0;

  return code >= 32 && code !== 127;
}

/**
 * One string, made fit to be written down.
 *
 * <p>Applied to EVERY string field of a record rather than to the one somebody remembered.
 * `security.md` names "a measure applied at SOME of its sites" as the defect this family keeps
 * writing, and the plan round found this plan committing it: the first draft redacted `detail` and
 * left `title`, `cure` and six context fields composed from the same exception text.</p>
 *
 * <p>Control characters go first, for the reason `serverSettingsSync.displayable` gives about a
 * different arbitrary string: they are not an attack anybody is expecting, they are how a corrupted
 * value produces a line nobody can read — and here, one that no `JSON.parse` can read back.</p>
 */
export function safeText(value: string, limit: number): string {
  const printable = [...value].filter(isPrintable).join('');

  const withoutParameters = printable.replace(
    PARAMETER,
    (whole: string, lead: string, name: string) =>
      (namesACredential(name) ? `${lead}${name}=${REDACTED}` : whole),
  );

  const redacted = SECRETS.reduce(
    (text, [pattern, replacement]) => text.replace(pattern, replacement),
    withoutParameters,
  );

  // LAST, so the shapes that can be recognised on sight have already been taken out: an
  // `Authorization: Bearer ...` is redacted by its own pattern above before this one sees the word
  // `Authorization` at all, which keeps the sentence readable instead of redacting it twice.
  const named = redacted.replace(
    LABELLED,
    (whole: string, name: string, joiner: string) =>
      (namesACredential(name) ? `${name}${joiner}${REDACTED}` : whole),
  );

  return named.length <= limit ? named : named.slice(0, limit) + TRUNCATED;
}

/** Which limit a field gets. Everything that is not the long one is held to the short one. */
function limitFor(field: string): number {
  return field === 'detail' ? DETAIL_LIMIT : TITLE_LIMIT;
}

/**
 * <b>Every string field is redacted, identity fields included — and that was re-decided.</b>
 *
 * <p>The code round asked for `class`, `source`, `code` and `run` to be exempt: they are literals
 * chosen in the source rather than text anybody typed, so there is nothing in them to redact, and
 * rewriting one risks a persisted key that no longer matches the key the suppressor admitted. The
 * exemption was written, and an existing test rejected it — the one that puts a secret-bearing
 * string into EVERY field and asserts none of them reaches the line. It is right and the exemption
 * was wrong: `security.md` names "a measure applied at SOME of its sites" as the defect this family
 * keeps writing, and trading a tested invariant for a guarantee about a case that does not occur is
 * how that defect gets written again.</p>
 *
 * <p>The concern was real even though the example was not, so it is answered by a GUARD instead of
 * an exemption: `notification-sites.json` carries every `code` literal the product can emit, and a
 * test asserts that redaction is a no-op on each one. A code that the redactor would rewrite now
 * fails a test on the day it is added, which is strictly better than an exemption — the invariant
 * stays whole and the key stays stable, and neither rests on anybody remembering.</p>
 */

/**
 * The record as one line of JSONL, with the newline — redacted on the way out.
 *
 * <p>The redaction happens HERE, at the one road onto the disk, and iterates the record's own
 * entries rather than a list of field names. That is the whole point: a field added to
 * `NotificationRecord` next year is redacted by this function without anybody remembering to add
 * it to a list, and a test that named the fields would have repeated a list the code also holds —
 * which `testing.md` says will not notice the third entry.</p>
 */
export function notificationLine(record: NotificationRecord): string {
  const { more, ...named } = record;
  // Both halves are redacted the same way — a forward-compatibility bag that skipped the redactor
  // would be a hole in a security measure, dressed as tolerance — but PRESENCE is decided
  // separately, which is why they are no longer flattened first.
  const safeNamed = Object.entries(named)
    .map(([field, value]) => [field, clean(field, value)] as const)
    // An optional string that redacts to NOTHING is ABSENT rather than empty. `given()` drops a
    // string that was empty when the record was built; this is the same rule one step later, for a
    // string that HAD characters and lost all of them — `title: '\u0001'` is the shape. Without it
    // the line carries `"title":""`, `parseNotificationLine` drops the empty optional on the way
    // back in, and the line is no longer a fixed point of its own parser: a record read and written
    // again is not the record that arrived. Measured on the parity harness, which said so in those
    // words. (CodeRabbit, on the notice-line pull request.)
    .filter(([field, value]) => value !== '' || REQUIRED_FIELDS.includes(field as string));
  const safeMore = Object.entries(more ?? {}).map(([field, value]) => [field, clean(field, value)] as const);

  return `${JSON.stringify(Object.fromEntries([...safeNamed, ...safeMore]))}\n`;
}

/** The four a line means nothing without. They are never dropped, whatever redaction leaves. */
const REQUIRED_FIELDS: readonly string[] = ['utc', 'class', 'source', 'code'];

/** One value, redacted at its field's limit; anything that is not a string is passed through. */
function clean(field: string, value: unknown): unknown {
  return typeof value === 'string' ? safeText(value, limitFor(field)) : value;
}

/** A string, made safe: anything that is not one is empty rather than `undefined` downstream. */
function asText(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

/** A number, or nothing — `0` is a real ordinal and must not be confused with an absent one. */
function asNumber(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isFinite(value) ? value : undefined;
}

/**
 * Fields this build does not know, kept so a newer writer's record survives an older reader.
 *
 * <p>Bounded in three ways, because this reads a file another process appends to: at most
 * `MOST_UNKNOWN_FIELDS` of them, strings and finite numbers only — an object or an array is dropped,
 * since a line here must stay flat — and each one goes through the serialiser's own cleaning on the
 * way out like every other field.</p>
 */
function strangers(row: Record<string, unknown>, known: ReadonlySet<string>): Record<string, string | number> {
  const kept: Record<string, string | number> = {};
  for (const [field, value] of Object.entries(row)) {
    if (known.has(field) || Object.keys(kept).length >= MOST_UNKNOWN_FIELDS) {
      continue;
    }
    if (typeof value === 'string' || (typeof value === 'number' && Number.isFinite(value))) {
      kept[field] = value;
    }
  }

  return kept;
}

/** How many unknown fields one record may carry. A bound, because the file is written by others. */
export const MOST_UNKNOWN_FIELDS = 32;

/** Only the optional fields that are present, so a record round-trips as what it was. */
function optional(row: Record<string, unknown>): Partial<NotificationRecord> {
  const text: Array<keyof NotificationRecord> = [
    'subject', 'title', 'detail', 'cure', 'action', 'offered', 'answer', 'run', 'repo', 'branch',
    'session', 'provider', 'role',
  ];
  const kept: Record<string, unknown> = {};
  for (const field of text) {
    const value = asText(row[field]);
    if (value.length > 0) {
      kept[field] = value;
    }
  }
  for (const field of ['pid', 'seq', 'bound']) {
    const value = asNumber(row[field]);
    if (value !== undefined) {
      kept[field] = value;
    }
  }
  const known = new Set<string>([...text, 'class', 'source', 'code', 'utc', 'pid', 'seq', 'bound']);
  const unknown = strangers(row, known);
  if (Object.keys(unknown).length > 0) {
    kept['more'] = unknown;
  }

  return kept as Partial<NotificationRecord>;
}

/**
 * One line back, or nothing when it is torn, foreign, or names no class.
 *
 * <p>`utc`, `class`, `source` and `code` are what make a record mean anything — a line missing any
 * of them can be neither placed in time nor grouped — so those are required and everything else is
 * coerced. A line this cannot read is dropped rather than thrown: the file is appended to by
 * processes that can be killed mid-write, and one torn line must not take a year of history with
 * it.</p>
 *
 * <p><b>An unknown class is kept, not dropped.</b> A newer build writing a class this one has never
 * heard of must not make its records vanish from an older reader — that is the forward-compatibility
 * bargain `chatDoors` already takes for `door`. The page groups unknown classes under one tab rather
 * than pretending they did not happen.</p>
 */
export function parseNotificationLine(line: string): NotificationRecord | undefined {
  let value: unknown;
  try {
    value = JSON.parse(line);
  } catch {
    return undefined;
  }
  if (value === null || typeof value !== 'object') {
    return undefined;
  }
  const row = value as Record<string, unknown>;
  const utc = asText(row['utc']);
  const kind = asText(row['class']);
  const source = asText(row['source']);
  const code = asText(row['code']);
  if (utc.length === 0 || kind.length === 0 || source.length === 0 || code.length === 0) {
    return undefined;
  }

  return { utc, class: kind as NotificationClass, source, code, ...optional(row) };
}

/** Whether a class is one this build knows. The page needs it; the parser deliberately does not. */
export function isKnownClass(kind: string): kind is NotificationClass {
  return CLASSES.has(kind);
}

/**
 * Every record in one file's text. A torn last line costs itself and nothing else.
 *
 * <p>The same bargain `usage.ts` already makes for the server's ledger: a file being appended to
 * while it is read can end in half a line, and the newest record then waits for the next tick.</p>
 */
export function parseNotifications(text: string): NotificationRecord[] {
  const kept: NotificationRecord[] = [];
  for (const line of text.split('\n')) {
    const trimmed = line.trim();
    if (trimmed.length === 0) {
      continue;
    }
    const record = parseNotificationLine(trimmed);
    if (record !== undefined) {
      kept.push(record);
    }
  }

  return kept;
}
