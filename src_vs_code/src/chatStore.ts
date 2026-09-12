import { ChatMessage } from './chatPage';
import { SavedTab } from './chatTabs';

/**
 * What a stored conversation IS, and every rule over one that needs no disk.
 *
 * <p>No `vscode` and no `node:fs`, on purpose — the same reason `chatTabs.ts` had none before it.
 * What is written, what a damaged record does, when one has expired and what a picker row says are
 * DECISIONS, and a decision inside the host is a decision no test can reach. The half that needs a
 * directory is `chatStoreFile.ts`, which holds no judgement of its own.</p>
 *
 * <h2>Why a conversation is two files</h2>
 *
 * <p>The transcript is tens of kilobytes and the picker shows a title, a model and one line. Reading
 * every transcript to draw a list of ninety days of conversations is seconds of a blocked extension
 * host, so the list is drawn from a metadata file of about three hundred bytes that lives beside the
 * record. The picker never opens a transcript; opening one is what CHOOSING a row does.</p>
 *
 * <h2>And why both of them carry a `rev`</h2>
 *
 * <p>Two atomic writes are not one atomic commit. The record is renamed into place, the host dies,
 * and the metadata still describes the turn before — a row then shows a last line that is no longer
 * last, or points at a transcript that has moved on. Three vendors' reviewers found that hole in the
 * plan independently, which is why the number exists: every save increments it, the record is written
 * first and the metadata second, and a metadata file whose `rev` is not its record's is stale BY
 * CONSTRUCTION rather than by a guess about clocks. {@link isStale} is that test, and the reader
 * regenerates rather than believes.</p>
 *
 * <p>The same number makes a lost update detectable. Two windows can both hold one conversation —
 * two windows on one workspace, *go to* pressed in each — and the last rename would otherwise
 * silently replace the other's turns. A writer holds the `rev` it last read; a higher one on disk
 * means somebody else is in this conversation, and the write is refused rather than performed.</p>
 */

/**
 * The shape this module writes. Bumping it discards every older record rather than guessing at it.
 *
 * <p>A stored object is read back by a build that may be newer or older than the one that wrote it,
 * and a half-understood transcript is worse than none: it would be rendered as a conversation the
 * person recognises, with pieces missing. The same ruling `chatTabs.ts` made for the memento.</p>
 */
export const CONVERSATION_VERSION = 1;

/**
 * How long a conversation nobody has touched is kept: ninety days.
 *
 * <p>The memento's window was seven days and twenty records, which is a store nothing read. This one
 * is what a person opens two days — or two months — later, so the count cap goes and the window
 * lengthens. The projected size is in the plan's growth table; the sweep that applies this lives in
 * `chatStoreSweep.ts` and does not run while the store cannot be read.</p>
 */
export const KEEP_FOR_MS = 90 * 24 * 60 * 60 * 1000;

/** How much of the last thing said a picker row carries. A row is one line; a transcript is not. */
export const LAST_LINE_MAX = 120;

/**
 * What a conversation was opened FROM, durably — the identity that outlives the window.
 *
 * <p>The LIVE key is the host's own `vscode.Tab` object (`chatPanels.ts`), and it dies with the
 * window. This is the other half: what a tab has to be, tomorrow, for its conversation to be found
 * again.</p>
 *
 * <p><b>Never a title.</b> Two Claude Code sessions can both be called `main` and two folders can
 * both hold a `README.md`; a store keyed by name hands the second one the first one's conversation,
 * and the failure is silent — the transcript arrives, it is simply somebody else's. `chatPanels.ts`
 * exists because of that, and this type is the same ruling applied to disk.</p>
 */
export type ConversationSource =
  /** Claude Code's own session, by the UUID its session file is named for. A file does not move. */
  | { readonly kind: 'claude'; readonly sessionId: string }
  /** A document, by its URI. `file:` or `untitled:`, and an untitled one is followed when it is saved. */
  | { readonly kind: 'file'; readonly uri: string }
  /** Nothing this build can match a tab to: a migrated record, or a tab whose identity was ambiguous. */
  | { readonly kind: 'none' };

/**
 * A source, built — and an EMPTY identity is not one.
 *
 * <p>`sourceOfSession('')` used to produce a `claude` source carrying nothing, and two of those
 * compared equal: every tab whose session could not be resolved would have joined every other. That
 * is precisely the rule below — nothing matches nothing — defeated by the value it exists to
 * exclude. A caller with no identity gets `none`, which is the honest answer and the one that
 * matches nothing. (codex, this story's code round.)</p>
 */
export const sourceOfSession = (sessionId: string): ConversationSource =>
  sessionId.length === 0 ? { kind: 'none' } : { kind: 'claude', sessionId };

export const sourceOfFile = (uri: string): ConversationSource =>
  uri.length === 0 ? { kind: 'none' } : { kind: 'file', uri };

/** The whole of one conversation, as it is written to disk. */
export interface ConversationRecord {
  readonly version: number;
  /** Which save this is, counted from 1. See the module note — it is a commit marker and a guard. */
  readonly rev: number;
  /**
   * The conversation's own id, minted where its tab is made and echoed back by the page.
   *
   * <p>Not the title, for the reason the source type gives. It is also what `vscode.setState` hands
   * the serializer after a reload, so it must survive the migration unchanged.</p>
   */
  readonly id: string;
  readonly title: string;
  readonly passage: string;
  readonly modelId: string;
  readonly messages: readonly ChatMessage[];
  /** Whether this conversation came from a Claude Code session rather than from a file. */
  readonly fromSession: boolean;
  /** Where a handed-over conversation begins — the index of the first message carried. */
  readonly carryFrom: number;
  readonly source: ConversationSource;
  /** The workspace folder this conversation belongs to, or empty for a window that had none. */
  readonly workspace: string;
  readonly createdAt: number;
  readonly updatedAt: number;
  /** When the person started a new chat here, if they did. A closed conversation is still readable. */
  readonly closedAt?: number;
}

/** What a picker row is drawn from, and the whole of what the index holds in memory. */
export interface ConversationMeta {
  /**
   * The version of the RECORD this was derived from, so the pair evolves together.
   *
   * <p>Without it, a future {@link CONVERSATION_VERSION} makes `recordFrom` reject an old record
   * while `metaFrom` goes on accepting its metadata — and the picker then offers a row whose
   * transcript this build has already discarded, which opens onto nothing. (codex, this story's code
   * round.)</p>
   */
  readonly version: number;
  readonly id: string;
  readonly rev: number;
  readonly title: string;
  readonly modelId: string;
  readonly turns: number;
  readonly lastLine: string;
  readonly source: ConversationSource;
  readonly workspace: string;
  readonly updatedAt: number;
  readonly closedAt?: number;
}

/**
 * What an id may be, and it is deliberately narrow: a filename component and nothing else.
 *
 * <p><b>An id becomes a PATH.</b> {@link recordName} is joined to the store's directory, so a record
 * on disk carrying `../../somewhere` would have the store read and write outside itself — and what
 * is read comes from a file, which is not a promise about its own contents. `randomUUID()` is what
 * mints one today; this is what makes that true of the ones that come back. Found by two vendors'
 * reviewers on this story's code round, from two roles.</p>
 *
 * <p>Letters, digits, dash, underscore and dot, with `.` and `..` excluded by the length-2 cases
 * below — which covers a uuid, and refuses every separator, drive letter, wildcard and space.</p>
 */
const SAFE_ID = /^[A-Za-z0-9_-]+(?:\.[A-Za-z0-9_-]+)*$/u;

/** Whether this id may be turned into a filename. */
export const isSafeId = (id: string): boolean =>
  id.length > 0 && id.length <= 200 && SAFE_ID.test(id) && id !== '.' && id !== '..';

/**
 * The file a record lives in, named for its id and for nothing else.
 *
 * <p>It THROWS on an id that is not safe rather than building the path. The validator on the way in
 * is the boundary and this is the last place before a path reaches the filesystem; a measure applied
 * at one of its sites is the defect this family keeps writing down.</p>
 */
export function recordName(id: string): string {
  if (!isSafeId(id)) {
    throw new Error(`a conversation id that is not a safe filename: ${id}`);
  }

  return `${id}.json`;
}

/** And the metadata beside it, on the same terms. */
export function besideMeta(id: string): string {
  if (!isSafeId(id)) {
    throw new Error(`a conversation id that is not a safe filename: ${id}`);
  }

  return `${id}.meta.json`;
}

/** Whether a filename is one this module wrote, rather than something else in the directory. */
export function isRecordName(name: string): boolean {
  if (!name.endsWith('.json') || name.endsWith('.meta.json')) {
    return false;
  }

  return isSafeId(name.slice(0, -'.json'.length));
}

/** The id a metadata file belongs to, or empty when the name is not one of ours. */
export function idOfMeta(name: string): string {
  if (!name.endsWith('.meta.json')) {
    return '';
  }
  const id = name.slice(0, -'.meta.json'.length);

  return isSafeId(id) ? id : '';
}

const isText = (value: unknown): value is string => typeof value === 'string';

const isCount = (value: unknown): value is number =>
  typeof value === 'number' && Number.isInteger(value) && value >= 0;

const isRev = (value: unknown): value is number =>
  typeof value === 'number' && Number.isInteger(value) && value >= 1;

const isInstant = (value: unknown): value is number =>
  typeof value === 'number' && Number.isFinite(value);

const isMessage = (value: unknown): value is ChatMessage => {
  const row = value as { role?: unknown; text?: unknown } | null;

  return row !== null && typeof row === 'object'
    && (row.role === 'you' || row.role === 'model') && typeof row.text === 'string';
};

/**
 * A source, validated — or nothing, which a reader turns into `none`.
 *
 * <p>A `claude` without its uuid and a `file` without its uri are records this build cannot use:
 * they name a kind of identity and then fail to carry one, and matching a tab against them would be
 * matching against an empty string, which every other empty string would equal.</p>
 */
function sourceFrom(value: unknown): ConversationSource | undefined {
  const row = value as { kind?: unknown; sessionId?: unknown; uri?: unknown } | null;
  if (row === null || typeof row !== 'object') {
    return undefined;
  }
  if (row.kind === 'none') {
    return { kind: 'none' };
  }
  if (row.kind === 'claude') {
    return isText(row.sessionId) && row.sessionId.length > 0 ? sourceOfSession(row.sessionId) : undefined;
  }
  if (row.kind === 'file') {
    return isText(row.uri) && row.uri.length > 0 ? sourceOfFile(row.uri) : undefined;
  }

  return undefined;
}

/**
 * One record back, or nothing when it is torn, foreign, or of a version this build does not know.
 *
 * <p>Every field is checked because every field is rendered, and what is stored can be edited by
 * hand, written by another version, or half-written by a host that was killed. A record that does not
 * survive this is DROPPED rather than repaired: a transcript with a hole in it reads as if the model
 * said nothing.</p>
 *
 * <p><b>Four fields are tolerated as ABSENT, and that is not the same as tolerating a wrong one.</b>
 * `source`, `workspace`, `fromSession` and `carryFrom` did not exist before this plan, and a record
 * written without them is a person's real conversation. Absent reads as *this record names no tab* —
 * never as *this one* — because a migrated record matched to a tab would hand over somebody else's
 * conversation, which is the whole failure this join exists to prevent. Present and wrong is a
 * record this build cannot trust, and it is dropped with the rest.</p>
 */
export function recordFrom(value: unknown): ConversationRecord | undefined {
  const row = value as Partial<Record<keyof ConversationRecord, unknown>> | null;
  if (row === null || typeof row !== 'object' || row.version !== CONVERSATION_VERSION) {
    return undefined;
  }
  if (!isRev(row.rev) || !isText(row.id) || !isSafeId(row.id)) {
    return undefined;
  }
  if (!isText(row.title) || !isText(row.passage) || !isText(row.modelId)) {
    return undefined;
  }
  if (!Array.isArray(row.messages) || !row.messages.every(isMessage)) {
    return undefined;
  }
  if (!isInstant(row.createdAt) || !isInstant(row.updatedAt)) {
    return undefined;
  }
  if (row.closedAt !== undefined && !isInstant(row.closedAt)) {
    return undefined;
  }
  if (row.fromSession !== undefined && typeof row.fromSession !== 'boolean') {
    return undefined;
  }
  if (row.carryFrom !== undefined && !isCount(row.carryFrom)) {
    return undefined;
  }
  if (row.workspace !== undefined && !isText(row.workspace)) {
    return undefined;
  }
  const source = row.source === undefined ? { kind: 'none' as const } : sourceFrom(row.source);
  if (source === undefined) {
    return undefined;
  }

  return {
    version: CONVERSATION_VERSION,
    rev: row.rev,
    id: row.id,
    title: row.title,
    passage: row.passage,
    modelId: row.modelId,
    messages: row.messages,
    fromSession: row.fromSession === true,
    carryFrom: row.carryFrom ?? 0,
    source,
    workspace: row.workspace ?? '',
    createdAt: row.createdAt,
    updatedAt: row.updatedAt,
    ...(row.closedAt === undefined ? {} : { closedAt: row.closedAt }),
  };
}

/** One metadata file back, on the same terms. */
export function metaFrom(value: unknown): ConversationMeta | undefined {
  const row = value as Partial<Record<keyof ConversationMeta, unknown>> | null;
  if (row === null || typeof row !== 'object' || row.version !== CONVERSATION_VERSION) {
    return undefined;
  }
  if (!isRev(row.rev) || !isText(row.id) || !isSafeId(row.id)) {
    return undefined;
  }
  if (!isText(row.title) || !isText(row.modelId) || !isText(row.lastLine)) {
    return undefined;
  }
  if (!isCount(row.turns) || !isInstant(row.updatedAt)) {
    return undefined;
  }
  if (row.closedAt !== undefined && !isInstant(row.closedAt)) {
    return undefined;
  }
  if (row.workspace !== undefined && !isText(row.workspace)) {
    return undefined;
  }
  const source = row.source === undefined ? { kind: 'none' as const } : sourceFrom(row.source);
  if (source === undefined) {
    return undefined;
  }

  return {
    version: CONVERSATION_VERSION,
    id: row.id,
    rev: row.rev,
    title: row.title,
    modelId: row.modelId,
    turns: row.turns,
    lastLine: row.lastLine,
    source,
    workspace: row.workspace ?? '',
    updatedAt: row.updatedAt,
    ...(row.closedAt === undefined ? {} : { closedAt: row.closedAt }),
  };
}

/**
 * The last thing said, as ONE line a row can hold.
 *
 * <p>Newlines become spaces before anything is measured: a transcript's first line can be three
 * words and its meaning in the fourth, and a row that stopped at the newline would say nothing while
 * looking complete.</p>
 */
function oneLine(text: string): string {
  const flat = text.replace(/\s+/gu, ' ').trim();

  return flat.length <= LAST_LINE_MAX ? flat : `${flat.slice(0, LAST_LINE_MAX)}…`;
}

/** What the picker reads, derived from the record so the two can never describe different things. */
export function metaOf(record: ConversationRecord): ConversationMeta {
  const last = record.messages[record.messages.length - 1];

  return {
    version: record.version,
    id: record.id,
    rev: record.rev,
    title: record.title,
    modelId: record.modelId,
    turns: record.messages.length,
    lastLine: last === undefined ? '' : oneLine(last.text),
    source: record.source,
    workspace: record.workspace,
    updatedAt: record.updatedAt,
    ...(record.closedAt === undefined ? {} : { closedAt: record.closedAt }),
  };
}

/**
 * Whether a metadata file describes a DIFFERENT save from its record.
 *
 * <p>Lower means the pair was interrupted between the two renames. Higher means the same, the other
 * way round — a record that failed to be replaced after its metadata was. Either way the two are not
 * one commit and the metadata is not evidence about anything; the record is, and the reader
 * regenerates from it.</p>
 */
export const isStale = (meta: ConversationMeta, record: ConversationRecord): boolean => meta.rev !== record.rev;

/**
 * Whether two sources are the same source.
 *
 * <p><b>`none` matches nothing, including another `none`.</b> It is not an identity; it is the
 * absence of one, and treating two absences as equal would join every migrated conversation to
 * every other. A record that names no source is reachable only by a person choosing it.</p>
 */
export function sameSource(left: ConversationSource, right: ConversationSource): boolean {
  if (left.kind === 'claude' && right.kind === 'claude') {
    return left.sessionId === right.sessionId;
  }
  if (left.kind === 'file' && right.kind === 'file') {
    return left.uri === right.uri;
  }

  return false;
}

/** Whether this conversation is past the window, measured from when it was last USED. */
export const expired = (meta: ConversationMeta, now: number): boolean => now - meta.updatedAt > KEEP_FOR_MS;

/**
 * A memento record as a store record — the migration's one decision.
 *
 * <p>The id survives, because it is what the page hands the serializer back after a reload. The
 * instant survives too: ageing a migrated conversation from the moment it was MIGRATED would keep a
 * three-month-old conversation for another ninety days, and the window is about when a person last
 * used something. Nothing else is invented — the source is `none`, which is the honest answer for a
 * record written before there were sources.</p>
 */
export function fromLegacy(tab: SavedTab, workspace: string): ConversationRecord {
  return {
    version: CONVERSATION_VERSION,
    rev: 1,
    id: tab.id,
    title: tab.title,
    passage: tab.passage,
    modelId: tab.modelId,
    messages: tab.messages,
    fromSession: tab.fromSession === true,
    carryFrom: tab.carryFrom ?? 0,
    source: { kind: 'none' },
    workspace,
    createdAt: tab.savedAt,
    updatedAt: tab.savedAt,
  };
}
