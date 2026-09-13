import { randomUUID } from 'node:crypto';
import { mkdir, readFile, readdir, rename, rm, stat } from 'node:fs/promises';
import { join } from 'node:path';
import { abreast } from './abreast';
import { writeFileAtomically } from './atomicFile';
import {
  ConversationMeta,
  ConversationRecord,
  ConversationSource,
  besideMeta,
  expired,
  idOfMeta,
  isSafeId,
  isStale,
  metaFrom,
  metaOf,
  recordFrom,
  recordName,
  sameSource,
} from './chatStore';
import { claimConversation } from './chatStoreLock';

/**
 * The conversation store's world-facing half: the directory, the two files, and nothing else.
 *
 * <p>Every rule about what a record MEANS — what is written, what a damaged one does, when the
 * metadata is stale, what an id may be, how a picker row is derived — lives in `chatStore.ts` and is
 * imported. This file holds no judgement of its own; it does the things that need a disk and asks
 * `chatStore.ts` for every decision. The same split `chatUsageFile.ts` has against `chatUsage.ts`,
 * and for the same reason: a decision inside the host is a decision no test can reach.</p>
 *
 * <h2>One conversation, two files, one commit</h2>
 *
 * <p>A conversation is a transcript file `<id>.json` and a metadata file `<id>.meta.json` beside it.
 * The picker draws its list from the metadata alone (about three hundred bytes) and never opens a
 * transcript; opening one is what CHOOSING a row does. The reason both carry a `rev` is written down
 * in `chatStore.ts`: two atomic writes are not one atomic commit, so the record is written FIRST and
 * the metadata SECOND, both stamped with the same new number, and a metadata file whose `rev` is not
 * its record's is stale by construction rather than by a guess about clocks. On read the pair is
 * reconciled — the record is the truth, the metadata is regenerated from it whenever the two
 * disagree.</p>
 *
 * <h2>Every mutation holds the conversation's lock; the swap is checked under it</h2>
 *
 * <p>A save proceeds only when the disk holds EXACTLY the revision the caller last read — or nothing
 * at all, for a first write, and that is checked as absence in so many words rather than inferred
 * from a number. A rename cannot condition itself on what was read, so the check is made under a claim
 * of the whole conversation: `<id>.lock`, taken by an <b>exclusive create</b> (`chatStoreLock.ts`,
 * which names the operation, the fence, the wait and the residual). The same lock is taken by
 * {@link ChatStoreFile.forget} and by the metadata write {@link ChatStoreFile.read} performs when it
 * finds the index stale, so a delete cannot race a save into resurrecting a conversation or leaving a
 * row that opens onto nothing, and a reader cannot write a rev-1 index beside a rev-2 transcript. A
 * read that only reads takes nothing. Two windows that both read 5 both aim at 6; one owns the lock,
 * the other is refused. A window whose record was DELETED under it — a baseline above zero and nothing
 * on disk — is refused too, rather than quietly resurrecting what somebody else threw away.</p>
 *
 * <h2>The record is the commit — and a half-done save says so</h2>
 *
 * <p>Once the record's rename lands at its new `rev` the save has committed, because the record is
 * what {@link ChatStoreFile.read} regenerates the metadata from. A metadata write that fails
 * afterwards does not un-commit it, and the caller must advance its baseline as on a success — a
 * caller that treated it as failed would hold a stale baseline and see its OWN next write refused. But
 * it is not a success either: the index did not land, and {@link SaveOutcome} has a third answer,
 * `partial`, that carries the new rev AND the reason. Three vendors' reviewers refused the first
 * draft's `ok` here, independently, and were right.</p>
 *
 * <h2>A record this build cannot read is quarantined, never overwritten</h2>
 *
 * <p>A file at `<id>.json` that is torn, foreign, or of a version this build does not know is NOT an
 * absent record. Treating it as one would let an older build replace a newer build's conversation
 * after a downgrade, and a conversation minted under a fresh id — saved with baseline zero — meets
 * whatever is already at that id. So a save over it is `incompatible` and writes nothing; the file
 * stays where it is until an explicit {@link ChatStoreFile.forget} — the trash button of story B4,
 * which sets it aside rather than deleting it. The
 * sweep of story B1 deliberately leaves it: what cannot be read cannot be aged, and
 * {@link ChatStoreFile.retireIfExpired} answers `kept` for it. `read` returns nothing for it, saying
 * so on the console.</p>
 *
 * <h2>A person's forget SETS THE TRANSCRIPT ASIDE; only the ninety-day retirement deletes</h2>
 *
 * <p>{@link ChatStoreFile.forget} — the picker's trash and its keybinding — removes the metadata, so
 * the row goes at once and nothing lists it again, and then RENAMES the transcript into the quarantine
 * rather than unlinking it. The operator's rule for this feature was *archive, not destroy*, with no
 * confirmation dialog in the way, and those two are only compatible if the press is recoverable: a
 * mis-aimed `Alt+Delete` in a filtered list is one keystroke, and what it would otherwise destroy is
 * the only copy of somebody's words. The set-aside file is dated, so the sweep's own quarantine rule
 * retires it at the same ninety days everything else is kept for — recoverable by hand until then, and
 * nothing accumulates for ever. {@link ChatStoreFile.retireIfExpired} is the path that really deletes,
 * and it must stay that way: quarantining what has just aged out would keep it another ninety days.</p>
 *
 * <h2>What a crash between the two steps leaves, and who collects it</h2>
 *
 * <p>{@link ChatStoreFile.forget} removes the metadata first, then moves the record. A crash between
 * the two leaves a TRANSCRIPT WITH NO METADATA: invisible to {@link ChatStoreFile.listMeta}, which reads
 * metadata only, and — because a baseline-zero save meets it and is `refused` — an id that cannot be
 * reused for a new conversation. That is the intended shape of the failure (a file nobody sees beats a
 * row nobody can open), and it is the sweep of story B1 that collects such a file. This listing does
 * not hunt for it, deliberately: deletion by policy belongs in one place.</p>
 *
 * <h2>Missing is not unreadable</h2>
 *
 * <p>The `readLedger` rule of `jsonlLedger.ts`, applied to a directory of two-file pairs: a store
 * that is not there (`ENOENT`) is an ordinary empty state — nobody has chatted yet — while one that
 * EXISTS and cannot be read (`EACCES`, a file where the directory should be) is a store that has
 * history and is not answering. {@link ChatStoreFile.state} is the one place that distinction is
 * reported, because reporting "no conversations" for a store that could not be read is this module
 * lying about the world, and a later story's sweep must delete nothing while the store is
 * unavailable. <b>The same three-way honesty holds at every boundary</b>: {@link ChatStoreFile.read}
 * answers `absent`, `incompatible` or `unavailable` rather than one `undefined` for all three — the
 * reload serializer disposes a panel over "no record", and it must not do that over a permissions
 * error or a record a newer build wrote — and {@link ChatStoreFile.listMeta} answers `unavailable`
 * rather than an empty list for a directory it could not read, because the index cache of story B1
 * keeps its last good rows when a refresh fails and cannot tell "nothing here" from "could not look"
 * if both are `[]`. (codex, the third round.)</p>
 *
 * <p><b>Nothing here throws.</b> A filesystem failure becomes a typed outcome, an empty listing, or
 * `undefined`. Every such failure is also said on the console — never swallowed, per
 * `coding-style.md` — and the console line NAMES THE FILE OR DIRECTORY, because a person running
 * several profiles cannot otherwise tell which store refused. The `reason` returned to the caller is
 * the opposite: a short sentence a person reads, with no path and no stack. (gemini, the code
 * round.)</p>
 */

/**
 * What a save did, as one of five facts the caller reacts to differently.
 *
 * <p><b>`ok`</b> carries the new `rev`, the caller's next baseline. <b>`partial`</b> committed the
 * record at `rev` — advance the baseline exactly as for `ok` — but its index did not land, and
 * `reason` says why; the next read regenerates it. <b>`refused`</b> is not a failure: another window
 * is in this conversation, or deleted it (`diskRev` 0), and the caller keeps its transcript and
 * re-mints under a new id (story A3). <b>`incompatible`</b> means a file this build cannot read sits
 * at this id; nothing was written. <b>`failed`</b> is a disk that would not answer, or a call this
 * half could not act on — an invalid baseline included. Every `reason` is a short person-facing
 * sentence naming the fault and never the path.</p>
 */
export type SaveOutcome =
  | { readonly kind: 'ok'; readonly rev: number }
  | { readonly kind: 'partial'; readonly rev: number; readonly reason: string }
  | {
    readonly kind: 'refused';
    readonly diskRev: number;
    /**
     * What the record on disk SAYS, when it could be read — every message's text, in order.
     *
     * <p>A revision alone cannot tell a caller which refusal it has met. Two windows can both hold
     * a conversation at baseline 0 — both restored it, neither has written — and the second, refused
     * after the first saved, would take the number on faith and write over turns it has never seen.
     * What separates our own earlier session from somebody else's is not the number, it is whether
     * what is on disk is contained in what we hold. Absent when the record could not be read, and
     * then nothing may be assumed about it. (gemini and local, A3's plan round.)</p>
     */
    readonly said?: readonly string[];
    /**
     * When the record on disk BEGAN, when it could be read.
     *
     * <p>A window that adopts a record is taking over one it wrote in an earlier session, and the
     * conversation began when THAT did — not when the memento was last written, which is the best
     * this side can otherwise do. Without it, adopting a conversation started in January and
     * answered in March records it as having started in March, and the picker draws its "started"
     * from exactly that field. (codex, A3's code round.)</p>
     */
    readonly began?: number;
  }
  /**
   * Another mutation of THIS conversation holds its lock; nothing was probed, nothing was written.
   *
   * <p>Its own outcome rather than a refusal carrying no transcript, which is how it was first
   * written — and that overloading was wrong in a way that WEDGES. A genuine conflict where the
   * record is absent reports no transcript either, so a caller telling the two apart by that absence
   * reads a real conflict as a busy store: it waits, never forks, never advances, and every later
   * write waits behind it for ever. Two facts that need different answers need two names. (gemini,
   * A3's second code round.)</p>
   */
  | { readonly kind: 'busy' }
  | { readonly kind: 'incompatible'; readonly reason: string }
  | { readonly kind: 'failed'; readonly reason: string };

/**
 * What a delete did. `ok` is both files gone — or never there; `failed` is a conversation still on
 * disk, in whole or in part, which the migration and the sweep must be able to tell from a deletion.
 */
export type ForgetOutcome =
  | { readonly kind: 'ok' }
  | { readonly kind: 'failed'; readonly reason: string };

/** What retiring an expired conversation came to; `kept` names why, for the sweep to count and the next one to look at again. */
/** What following a moved file came to. `kept` is not a failure — the record was not this one to move. */
export type RefileOutcome =
  | { readonly kind: 'followed'; readonly rev: number }
  | { readonly kind: 'kept'; readonly why: 'busy' | 'absent' | 'incompatible' | 'unavailable' | 'moved on' }
  | { readonly kind: 'failed'; readonly reason: string };

export type RetireOutcome =
  | { readonly kind: 'retired' }
  | { readonly kind: 'kept'; readonly why: 'held' | 'absent' | 'changed' | 'not expired' | 'incompatible' | 'unavailable' }
  | { readonly kind: 'failed'; readonly reason: string };

/**
 * Whether the store can be used, and — the load-bearing part — WHY it cannot when it cannot.
 *
 * <p>`empty` is ordinary: the directory is not there because nobody has chatted yet, and the first
 * save materialises it. `unavailable` means history exists and could not be read; it carries a
 * `reason` so the picker can say so instead of showing an empty *Recent*, and it is what stops the
 * sweep from interpreting a directory that would not answer as a directory full of expired things.</p>
 */
export type StoreState =
  | { readonly kind: 'empty' }
  | { readonly kind: 'ready' }
  | { readonly kind: 'unavailable'; readonly reason: string };

/**
 * What is at `<id>.json`, as four facts — the save branches on them, and the read HANDS THEM ON.
 *
 * <p>`absent` is the only state a first write may create over, and the only one a caller may treat
 * as "no such conversation". `incompatible` is a file that parses to nothing this build trusts — torn,
 * foreign, or written by a newer build — and a caller that disposed a tab over it would be throwing
 * away a conversation a later build could read. `unavailable` is a file the disk would not hand over at
 * all. They are kept apart because they mean different things to a save (refuse, and fail) and to the
 * serializer (preserve, and preserve), and collapsing all three into `undefined` was the third round's
 * finding. Every `reason` is a short sentence for a person, with no path in it.</p>
 */
export type ReadOutcome =
  | { readonly kind: 'record'; readonly record: ConversationRecord }
  | { readonly kind: 'absent' }
  | { readonly kind: 'incompatible'; readonly reason: string }
  | { readonly kind: 'unavailable'; readonly reason: string };

/**
 * What a listing came to: the rows, or a directory that would not answer.
 *
 * <p>An empty `metas` means the directory was READ and held nothing — or is not there at all, which
 * is the ordinary "nobody has chatted yet" that {@link ChatStoreFile.state} calls `empty`, and which a
 * cache should reflect as empty rather than preserve stale rows over. `unavailable` is a directory that
 * exists and could not be read, and is the answer that lets a cache keep its last good rows.</p>
 */
export type Listing =
  | { readonly kind: 'listed'; readonly metas: readonly ConversationMeta[] }
  | { readonly kind: 'unavailable'; readonly reason: string };

/** A probe that came back with something a swap can be judged against. */
type Present = Extract<ReadOutcome, { kind: 'absent' | 'record' }>;

/**
 * How many metadata files are read at once when the directory is listed.
 *
 * <p>Serially, the store's own projected size — thousands of files at ninety days — is thousands of
 * awaits before the first row exists. All at once is a thundering herd of open descriptors against a
 * directory another window may be writing. Eight is the width at which a listing is bounded by the
 * disk rather than by the event loop, and it is a constant rather than a setting because nothing a
 * person can observe would tell them which value to pick. Paging and the in-memory index are stories
 * B1 and B2, not this.</p>
 */
const LIST_WIDTH = 8;

/** The `code` off a Node filesystem error, or empty when there is none. Never its message: that carries the path. */
const codeOf = (reason: unknown): string => {
  const code = (reason as { code?: unknown } | null)?.code;

  return typeof code === 'string' ? code : '';
};

/** A person-facing sentence with the error code appended when there is one — and never the path. */
const withCode = (sentence: string, code: string): string => (code.length > 0 ? `${sentence} (${code})` : sentence);

/**
 * JSON, or nothing.
 *
 * <p>A torn write — the JSON of a save nobody finished — is not a throw, it is `undefined`, which the
 * validators turn into a dropped record exactly as `recordFrom(undefined)` does. A file's contents
 * are not a promise about their own shape, whoever wrote them.</p>
 */
function parsed(text: string): unknown {
  try {
    return JSON.parse(text) as unknown;
  } catch {
    return undefined;
  }
}

/** The sentence a person reads when a file this build cannot make sense of sits where a conversation should. */
const INCOMPATIBLE = 'the conversation on disk is not one this build can read';

/** Whether a baseline is one a swap can be judged against: a whole number, zero or more. Nothing is floored. */
const isBaseline = (value: number): boolean => Number.isInteger(value) && value >= 0;

/**
 * Whether the disk is what the caller read — said explicitly for both kinds of baseline.
 *
 * <p>A baseline of zero is a CREATION and may meet only absence: anything present, whatever its
 * revision, is a conflict. A baseline above zero is an UPDATE and must meet exactly the revision it
 * read: nothing there means the conversation was deleted under it; another revision means it was
 * replaced under it, and writing over either would splice two conversations into one file. The
 * conflicting `diskRev` when there is one, `undefined` when the swap may proceed.</p>
 */
function conflictOf(seen: Present, base: number): number | undefined {
  if (base === 0) {
    return seen.kind === 'absent' ? undefined : seen.record.rev;
  }
  if (seen.kind === 'absent') {
    return 0;
  }

  return seen.record.rev === base ? undefined : seen.record.rev;
}

/**
 * The I/O half of the conversation store, bound to ONE directory it is handed.
 *
 * <p>It reads no configuration and calls no `coaiDataDir()`: the directory is the caller's to choose,
 * which is what lets a test point it at a temporary and the host point it under the coai data
 * directory. State is the directory and nothing more — every method is otherwise a pure function of
 * the disk.</p>
 */
/** What the conversation store is called, beside `chat-usage.jsonl` and `chat-doors.jsonl`. */
export const CONVERSATIONS_DIR = 'chat-conversations';

/** Where this installation keeps its conversations, given the coai data directory. */
export function conversationsDir(dataDir: string): string {
  return join(dataDir, CONVERSATIONS_DIR);
}

/**
 * Where what could not be filed as a conversation is kept, AS IT WAS — a subdirectory of the store,
 * so {@link ChatStoreFile.listMeta} never reads it (a directory name is not a `<id>.meta.json`) and so
 * the next story that moves the store moves the quarantine with it. The store owns its layout; the
 * migration asks for the operation rather than the path.
 */
export const QUARANTINE_DIR = 'quarantine';

/** What setting a value aside came to: where it is, or why it is not. */
export type QuarantineOutcome =
  | { readonly kind: 'ok'; readonly at: string }
  | { readonly kind: 'failed'; readonly reason: string };

/** What a quarantine file may be called: a label, no separators. The clock is appended by the store. */
const SAFE_LABEL = /^[A-Za-z0-9._-]{1,120}$/u;

export class ChatStoreFile {
  /** `dir` is readable so the keeper is bound to the SAME directory, never to a second copy of the path; the layout inside it stays here. */
  public constructor(public readonly dir: string) {}

  /** Where a record lives. Throws on an unsafe id, so every caller guards with {@link isSafeId} first. */
  private recordPath(id: string): string {
    return join(this.dir, recordName(id));
  }

  /** Where its metadata lives, on the same terms. */
  private metaPath(id: string): string {
    return join(this.dir, besideMeta(id));
  }

  /**
   * Save one conversation, as a compare-and-swap against what is on disk.
   *
   * <p><b>The new `rev` is `expectedRev + 1`.</b> `expectedRev` is the number the caller last read —
   * REQUIRED, and zero means "I have not read this record": a first write, which lands at rev 1. It is
   * required rather than defaulted because an update whose baseline the caller forgot to pass must not
   * silently become a creation attempt; and a baseline that is negative, fractional or not a number is
   * `failed` rather than floored, for the same reason. Counting from what was READ rather than from the
   * record's own `rev` field is deliberate: a first write lands at 1 without the caller
   * pre-incrementing, and the baseline the swap compares against and the number it writes are the same
   * quantity. Because the swap demands EQUALITY with the disk, the new rev is always the disk's plus one
   * — the count has no gaps.</p>
   *
   * <p><b>The order under the claim.</b> Claim `<id>.lock`; a claim still held after the lock's own
   * wait is another window mutating this conversation, and is `refused`. Then probe the disk and judge
   * it with {@link conflictOf}; a file this build cannot read is `incompatible`; a disk that will not
   * answer is `failed`. Then the record, then the metadata, then the lock is released in a `finally` —
   * a lock must never outlive a failed save.</p>
   *
   * <p>The store stamps ONE field — `rev`, its own protocol number. Everything else, `updatedAt` and
   * `closedAt` included, is the caller's to set before it calls here; a decision about what a record
   * means is not this half's to make.</p>
   *
   * @param now the clock, an argument so a test can pin it; it dates the lock and ages a rival's.
   */
  public async save(record: ConversationRecord, expectedRev: number, now = Date.now()): Promise<SaveOutcome> {
    if (!isSafeId(record.id)) {
      return { kind: 'failed', reason: 'a conversation id that cannot be a filename' };
    }
    if (!isBaseline(expectedRev)) {
      return { kind: 'failed', reason: 'a save was asked against a baseline that is not a whole number of zero or more' };
    }
    const claim = await claimConversation(this.dir, record.id, `save ${expectedRev + 1}`, now);
    if (claim.kind === 'failed') {
      return { kind: 'failed', reason: claim.reason };
    }
    if (claim.kind === 'held') {
      // NOT PROBED, deliberately, and this is the second half of the same finding. Reading the
      // record while another process is mid-mutation of it is exactly the unfenced read this
      // module's lock exists to forbid — on Windows it can also meet the sharing error that
      // mutation is holding, which would surface to a person as a failed save. There is nothing
      // worth reporting either: a revision read outside the claim describes a moment that has
      // already passed.
      return { kind: 'busy' };
    }
    try {
      return await this.saveClaimed({ ...record, rev: expectedRev + 1 }, expectedRev);
    } finally {
      await claim.release();
    }
  }

  /**
   * Follow a conversation whose file has MOVED: a new source and the root it now belongs to, written
   * as one revision.
   *
   * <p>Read and write INSIDE one claim, rather than a read followed by a save. A save-after-read
   * would be the check-then-act this module has been corrected for twice, and answering a refusal by
   * retrying is a loop whose exit condition is somebody else stopping — three reviewers asked for a
   * lock-ordered follow in this story's plan round, and this is it. Nothing is retried because
   * nothing races: the record is probed and replaced without the claim being let go.</p>
   *
   * <p><b>Both facts move together.</b> A file dragged into another workspace root changes where it
   * is AND which project it belongs to, and writing the uri alone would leave the conversation filed
   * under the root it left — invisible in exactly the folder the person is now looking at, which is
   * the misfiling this story exists to remove.</p>
   *
   * <p>It is `kept` rather than failed when the record has moved on: a window that closed the
   * conversation, or another window that followed the same rename first, has left a source that is no
   * longer the one being replaced, and overwriting it would undo their work.</p>
   *
   * @param was the source this caller believes is on disk; anything else is left alone
   */
  public async refile(
    id: string,
    was: ConversationSource,
    now: ConversationSource,
    workspace: string,
    at = Date.now(),
  ): Promise<RefileOutcome> {
    if (!isSafeId(id)) {
      return { kind: 'failed', reason: 'a conversation id that cannot be a filename' };
    }
    const claim = await claimConversation(this.dir, id, 'refile', at);
    if (claim.kind === 'failed') {
      return { kind: 'failed', reason: claim.reason };
    }
    if (claim.kind === 'held') {
      return { kind: 'kept', why: 'busy' };
    }
    try {
      const seen = await this.probe(id);
      if (seen.kind !== 'record') {
        return { kind: 'kept', why: seen.kind };
      }
      if (!sameSource(seen.record.source, was)) {
        return { kind: 'kept', why: 'moved on' };
      }
      const written = { ...seen.record, rev: seen.record.rev + 1, source: now, workspace };
      const done = await this.saveClaimed(written, seen.record.rev);

      if (done.kind === 'ok' || done.kind === 'partial') {
        return { kind: 'followed', rev: done.rev };
      }

      // EXHAUSTIVE BY NAME over what is left, like every other answer read in this feature. `busy`
      // cannot arrive — the claim is held, and `saveClaimed` never takes it — and `refused` cannot
      // either, because the baseline is the revision just probed inside that claim. Both are
      // reported rather than assumed away, so a change to the swap's answers is a sentence here and
      // not a silently wrong outcome.
      return {
        kind: 'failed',
        reason: done.kind === 'busy'
          ? 'the conversation was claimed by somebody while this window held its claim'
          : done.kind === 'refused' ? 'the conversation changed under the claim' : done.reason,
      };
    } finally {
      await claim.release();
    }
  }

  /** The swap itself, with the claim held: probe, compare, then the two writes in order. */
  private async saveClaimed(written: ConversationRecord, base: number): Promise<SaveOutcome> {
    // REFUSED BEFORE ANYTHING IS WRITTEN, if this build could not read it back. A record states its
    // origin twice — `fromSession` and `source` — and `agreeOnOrigin` rejects a pair that disagrees
    // ON READ, so a record that saves with a contradictory pair is a conversation that saves and is
    // then unopenable for ever: `read` answers `incompatible`, the picker refuses it, and the sweep
    // will not age it because what cannot be read cannot be dated. It was reachable only through the
    // new `refile` and only by a caller passing the wrong source, and it cost nothing to close the
    // whole class instead: the store does not write what it would refuse to read. (Three vendors, the
    // code round; it was also my own open question.)
    if (recordFrom(written) === undefined) {
      console.error(`ConnectOtherAIs: a conversation this build could not read back was not written: ${this.recordPath(written.id)}`);

      return { kind: 'failed', reason: 'the conversation is not one this build could read back, so it was not written' };
    }
    const seen = await this.probe(written.id);
    if (seen.kind === 'unavailable') {
      return { kind: 'failed', reason: seen.reason };
    }
    if (seen.kind === 'incompatible') {
      return { kind: 'incompatible', reason: seen.reason };
    }
    const conflict = conflictOf(seen, base);
    if (conflict !== undefined) {
      // The record was probed under the claim, so its words can be reported — which is what lets a
      // window tell its own earlier session from a rival before it decides to adopt or to fork.
      return {
        kind: 'refused',
        diskRev: conflict,
        ...(seen.kind === 'record'
          ? { said: seen.record.messages.map((message) => message.text), began: seen.record.createdAt }
          : {}),
      };
    }
    try {
      await writeFileAtomically(this.recordPath(written.id), JSON.stringify(written));
    } catch (reason) {
      console.error(`ConnectOtherAIs: a conversation could not be written: ${this.recordPath(written.id)}`, reason);

      return { kind: 'failed', reason: withCode('the conversation could not be saved to disk', codeOf(reason)) };
    }
    const indexFault = await this.writeMeta(written);

    return indexFault.length === 0
      ? { kind: 'ok', rev: written.rev }
      : { kind: 'partial', rev: written.rev, reason: indexFault };
  }

  /**
   * What is at `<id>.json`, without judging it — the validator does that.
   *
   * <p>Only `ENOENT` is `absent`. Anything else the disk refuses is `unavailable`, and a file the disk
   * hands over that {@link recordFrom} will not accept is `incompatible` — never mistaken for absence,
   * for the reasons in the header. Both faults are said on the console with the path.</p>
   */
  private async probe(id: string): Promise<ReadOutcome> {
    const path = this.recordPath(id);
    let text: string;
    try {
      text = await readFile(path, 'utf8');
    } catch (reason) {
      if (codeOf(reason) === 'ENOENT') {
        return { kind: 'absent' };
      }
      console.error(`ConnectOtherAIs: a conversation exists but could not be read: ${path}`, reason);

      return { kind: 'unavailable', reason: withCode('the conversation could not be read from disk', codeOf(reason)) };
    }
    const record = recordFrom(parsed(text));
    if (record === undefined) {
      console.error(`ConnectOtherAIs: a conversation file is not one this build can read: ${path}`);

      return { kind: 'incompatible', reason: INCOMPATIBLE };
    }

    return { kind: 'record', record };
  }

  /**
   * Write the metadata, from the record and nothing else. Returns the fault, or empty when it landed.
   *
   * <p>{@link metaOf} is the ONLY source of a metadata file, so the two can never describe different
   * things. A write that fails is said on the console WITH ITS PATH here, once, and handed back as a
   * sentence — the `partial` half of a save, or nothing more than the log for a reconciliation, whose
   * caller asked for the record and already has it. It is not thrown, because the record has already
   * landed and the reader regenerates the index from it whenever they disagree.</p>
   */
  private async writeMeta(record: ConversationRecord): Promise<string> {
    const path = this.metaPath(record.id);
    try {
      await writeFileAtomically(path, JSON.stringify(metaOf(record)));

      return '';
    } catch (reason) {
      console.error(`ConnectOtherAIs: a conversation was saved but its index entry could not be written: ${path}`, reason);

      return withCode('the conversation was saved but its index entry could not be written', codeOf(reason));
    }
  }

  /**
   * One conversation back — or WHICH of three reasons there is none — with the metadata reconciled on
   * the way.
   *
   * <p>`absent` is the ordinary "no such conversation", and the only answer a caller may act on as
   * one; an id that cannot be a filename is `absent` too, because nothing could ever have been written
   * under it. `unavailable` and `incompatible` are said on the console with the path, and are handed on
   * rather than folded into absence: the reload serializer DISPOSES a panel over a missing record, and
   * disposing over a permissions error or a record a newer build wrote would throw a person's tab away.
   * A present-but-torn record is never repaired — a transcript with a hole in it reads as a
   * conversation the person recognises with pieces missing — so it is reported, not rendered.</p>
   *
   * <p>The record returned is the one this read saw. If the reconciliation under the lock finds the
   * disk has moved on, the caller still gets what it read — its next save is then refused by the swap,
   * which is the honest answer and the one story A3 already handles.</p>
   *
   * @param now the clock, for the lock the reconciliation may take; a test pins it.
   */
  public async read(id: string, now = Date.now()): Promise<ReadOutcome> {
    if (!isSafeId(id)) {
      return { kind: 'absent' };
    }
    const seen = await this.probe(id);
    if (seen.kind === 'record') {
      await this.reconcileMeta(seen.record, now);
    }

    return seen;
  }

  /**
   * Bring the metadata back into step with its record — under the lock, and against the disk as it is
   * THEN, not as it was when the record was read.
   *
   * <p>Two races, both found on the second round. A `forget` that has just unlinked the metadata would
   * have its deletion undone by a reader regenerating the row; and a read that captured revision 1
   * while another window committed revision 2 would write a revision-1 index beside a revision-2
   * transcript. So the index is written only with the conversation's lock held, and only if a re-probe
   * under it finds the same revision this reconciliation started from — absent means forgotten under
   * us, another revision means that writer owns the index now, and a lock still held after the wait
   * means a mutation is in flight and will leave the index it means to. Best-effort throughout: a read
   * hands back the record whatever this comes to, and a write that fails is logged with its path by
   * {@link writeMeta}, which is why its returned sentence is not read here.</p>
   */
  private async reconcileMeta(record: ConversationRecord, now: number): Promise<void> {
    const meta = await this.readMeta(record.id);
    if (meta !== undefined && !isStale(meta, record)) {
      return;
    }
    const claim = await claimConversation(this.dir, record.id, `reconcile ${record.rev}`, now);
    if (claim.kind !== 'claimed') {
      return;
    }
    try {
      const again = await this.probe(record.id);
      if (again.kind === 'record' && again.record.rev === record.rev) {
        await this.writeMeta(again.record);
      }
    } finally {
      await claim.release();
    }
  }

  /**
   * One metadata file back, or nothing.
   *
   * <p>Missing is a silent nothing. Unreadable is said with its path. And a file that is THERE but torn
   * or not of a shape this build knows is said with its path too, before it reads as nothing: the
   * reconciliation will then write over it as if it had merely been absent, and a torn index is a fact
   * somebody debugging wants to have seen. (The second round, accepted.)</p>
   */
  private async readMeta(id: string): Promise<ConversationMeta | undefined> {
    const path = this.metaPath(id);
    let text: string;
    try {
      text = await readFile(path, 'utf8');
    } catch (reason) {
      if (codeOf(reason) !== 'ENOENT') {
        console.error(`ConnectOtherAIs: a conversation index entry could not be read: ${path}`, reason);
      }

      return undefined;
    }
    const meta = metaFrom(parsed(text));
    if (meta === undefined) {
      console.error(`ConnectOtherAIs: a conversation index entry is torn or not one this build can read: ${path}`);
    }

    return meta;
  }

  /**
   * Delete both files — metadata FIRST, then the record — under the conversation's lock.
   *
   * <p>The reverse of a save's order, and deliberate. A crash between the two deletes then leaves a
   * record with no metadata: a file nobody can SEE, because the picker lists metadata only, which the
   * sweep of story B1 collects later (see the header). The other order would leave metadata with no
   * record — a row that opens onto nothing, which is the one outcome a person actually meets. So if
   * the metadata delete FAILS, the record is left in place too and the outcome is `failed`: a row that
   * still opens is what remains, never a broken one. A conversation that is being written by another
   * window is `failed` as well, with a reason that says so, for the migration and the sweep to retry.
   * This is also the one built-in way to clear an `incompatible` file at an id.</p>
   */
  public async forget(id: string, now = Date.now()): Promise<ForgetOutcome> {
    if (!isSafeId(id)) {
      return { kind: 'failed', reason: 'a conversation id that cannot be a filename' };
    }
    const claim = await claimConversation(this.dir, id, 'forget', now);
    if (claim.kind === 'failed') {
      return { kind: 'failed', reason: claim.reason };
    }
    if (claim.kind === 'held') {
      return { kind: 'failed', reason: 'the conversation is being changed by another window' };
    }
    try {
      return await this.setAsideClaimed(id, now);
    } finally {
      await claim.release();
    }
  }

  /**
   * A person's forget, with the claim held: the row goes, the words are kept.
   *
   * <p>The metadata FIRST, because that is what makes the conversation stop existing as far as every
   * window is concerned, and a crash after it leaves a file the sweep collects rather than a row that
   * opens onto nothing. Then the transcript is renamed into the quarantine — a rename, so the bytes
   * are never rewritten and the move cannot half-happen. A transcript that is already gone is not a
   * failure: the row has gone, which is what was asked.</p>
   */
  private async setAsideClaimed(id: string, now: number): Promise<ForgetOutcome> {
    try {
      await rm(this.metaPath(id), { force: true });
    } catch (reason) {
      console.error(`ConnectOtherAIs: a conversation index entry could not be deleted: ${this.metaPath(id)}`, reason);

      return { kind: 'failed', reason: withCode('the conversation could not be deleted', codeOf(reason)) };
    }
    // A NONCE before the instant, and the instant stays last so the sweep's own dated rule still
    // reads it. A rename REPLACES its destination, so two archives that agreed on a name would lose
    // the older one — and the store is allowed to mint a conversation under an id whose record has
    // been forgotten, since a baseline-zero save meets an absent record. Vanishingly unlikely with
    // `randomUUID` ids, and free to make impossible, on the one operation whose whole purpose is not
    // losing what somebody said. (CodeRabbit, on the pull request.)
    const to = join(this.dir, QUARANTINE_DIR, `${id}-forgotten-${randomUUID().slice(0, 8)}-${now}.json`);
    try {
      await mkdir(join(this.dir, QUARANTINE_DIR), { recursive: true });
      await rename(this.recordPath(id), to);
      console.info(`ConnectOtherAIs: a conversation was forgotten; its transcript was set aside, unchanged, at ${to}`);

      return { kind: 'ok' };
    } catch (reason) {
      if (codeOf(reason) === 'ENOENT') {
        // NOT the same event as an archive, and it does not pass in silence. The row has gone, which
        // is what was asked, so this is not a failure and the person is not interrupted — but "the
        // transcript was set aside" and "there was no transcript to set aside" are different facts,
        // and a log that reported them identically would make the archive unfalsifiable. (Three
        // reviewers, the code round.)
        console.info(
          `ConnectOtherAIs: a conversation was forgotten, but there was no transcript to set aside: ${this.recordPath(id)}`
          + ' — it had already gone, so nothing was archived',
        );

        return { kind: 'ok' };
      }
      console.error(`ConnectOtherAIs: a forgotten conversation transcript could not be set aside: ${this.recordPath(id)}`, reason);

      return {
        kind: 'failed',
        reason: withCode('the conversation was removed from the list but its transcript could not be set aside', codeOf(reason)),
      };
    }
  }

  /** The two deletes, in their order, with the claim held — the retirement's path, and nothing else's. */
  private async forgetClaimed(id: string): Promise<ForgetOutcome> {
    try {
      await rm(this.metaPath(id), { force: true });
    } catch (reason) {
      console.error(`ConnectOtherAIs: a conversation index entry could not be deleted: ${this.metaPath(id)}`, reason);

      return { kind: 'failed', reason: withCode('the conversation could not be deleted', codeOf(reason)) };
    }
    try {
      await rm(this.recordPath(id), { force: true });
    } catch (reason) {
      console.error(`ConnectOtherAIs: a conversation transcript could not be deleted after its index entry: ${this.recordPath(id)}`, reason);

      return { kind: 'failed', reason: withCode('the conversation was removed from the list but its transcript could not be deleted', codeOf(reason)) };
    }

    return { kind: 'ok' };
  }

  /**
   * Retire one conversation the sweep observed as expired — but only if, UNDER ITS LOCK, it still is.
   *
   * <p>An index entry is a statement about a moment that has passed: a save can land between the
   * sweep's listing and its delete, and deleting on the listing's word would destroy it — the
   * check-then-act that cost epic A two rounds. So this is the ONE way the sweep deletes a conversation.
   * With the claim held the record is re-read; it goes only at exactly the revision the listing showed
   * (`seenRev`) and with its own `updatedAt` still past the window. Any other revision has been written
   * since and it is `kept`, UNTOUCHED — a stale index entry under it is the sweep's to repair through
   * {@link read}, not this method's to rewrite on the way out (two reviewers, the code round). A held
   * lock, an absent record, one this build cannot read and a disk that will not answer are `kept` too,
   * each named. The two deletes are {@link forgetClaimed}'s, in its order. `now` is the clock; a test pins it.</p>
   */
  public async retireIfExpired(
    id: string,
    seenRev: number,
    now = Date.now(),
    /**
     * Whether a live window holds this conversation — asked INSIDE the claim, because the sweep's own
     * answer was read before the lock was taken and a person can reopen a conversation in between.
     * The default is "nobody", for a caller with no heartbeats to consult.
     */
    heldNow: () => Promise<boolean> = async () => false,
  ): Promise<RetireOutcome> {
    if (!isSafeId(id)) {
      return { kind: 'failed', reason: 'a conversation id that cannot be a filename' };
    }
    const claim = await claimConversation(this.dir, id, 'retire', now);
    if (claim.kind === 'failed') {
      return { kind: 'failed', reason: claim.reason };
    }
    if (claim.kind === 'held') {
      return { kind: 'kept', why: 'held' };
    }
    try {
      return await this.retireClaimed(id, seenRev, now, heldNow);
    } finally {
      await claim.release();
    }
  }

  /** The re-check and the two deletes, with the claim held. Nothing is written on any `kept` path. */
  private async retireClaimed(
    id: string,
    seenRev: number,
    now: number,
    heldNow: () => Promise<boolean>,
  ): Promise<RetireOutcome> {
    const seen = await this.probe(id);
    if (seen.kind !== 'record') {
      return { kind: 'kept', why: seen.kind };
    }
    if (seen.record.rev !== seenRev) {
      return { kind: 'kept', why: 'changed' };
    }
    if (!expired(metaOf(seen.record), now)) {
      return { kind: 'kept', why: 'not expired' };
    }
    // THE LAST QUESTION, and the only one whose answer can have changed since the plan: reopening a
    // conversation moves neither its revision nor its age, so every check above still says delete —
    // and the announcement that protects it is written on another tick, under no lock.
    if (await heldNow()) {
      return { kind: 'kept', why: 'held' };
    }
    const gone = await this.forgetClaimed(id);

    return gone.kind === 'ok' ? { kind: 'retired' } : { kind: 'failed', reason: gone.reason };
  }

  /**
   * Every readable metadata file in the directory — and only the metadata files.
   *
   * <p>A transcript is NEVER opened to draw the list: {@link idOfMeta} returns empty for anything but
   * a `<id>.meta.json` with a safe id, so a `<id>.json`, an interrupted `.tmp` write, a `.lock` and a
   * stray `notes.txt` are all skipped before a byte is read. An unreadable or torn entry is dropped
   * rather than allowed to fail the whole listing — one corrupt index file must not empty a person's
   * picker. The files are read {@link LIST_WIDTH} at a time, in directory order, and come back in that
   * order.</p>
   *
   * <p><b>A directory that cannot be read is `unavailable`, not an empty list.</b> `state()` does not
   * cover this: a permission change between a state call and the listing is exactly the race the
   * cache's "keep the last good rows when a refresh fails" rule exists for, and it cannot keep them if
   * "could not look" and "nothing here" are both `[]`. A directory that is not there is `listed` with
   * nothing — the ordinary state of a machine nobody has chatted on, and one a cache should reflect.
   * A transcript with no metadata beside it is invisible here BY DESIGN and is the sweep's to reclaim,
   * not this listing's to hunt for.</p>
   */
  public async listMeta(): Promise<Listing> {
    let names: readonly string[];
    try {
      names = await readdir(this.dir);
    } catch (reason) {
      if (codeOf(reason) === 'ENOENT') {
        return { kind: 'listed', metas: [] };
      }
      console.error(`ConnectOtherAIs: the conversation store could not be listed: ${this.dir}`, reason);

      return { kind: 'unavailable', reason: withCode('the conversation store could not be listed', codeOf(reason)) };
    }
    const ids = names.map(idOfMeta).filter((id) => id.length > 0);
    const found = await abreast(ids.map((id) => () => this.entry(id)), LIST_WIDTH);

    return { kind: 'listed', metas: found.filter((meta): meta is ConversationMeta => meta !== undefined) };
  }

  /**
   * One index entry as the listing carries it — dropped when torn, unreadable, or naming an id its
   * filename does not. Public for B1's index, which re-reads entries one at a time as their stamps
   * change rather than listing thousands to learn that three moved. Never opens a transcript.
   */
  public async entry(id: string): Promise<ConversationMeta | undefined> {
    if (!isSafeId(id)) {
      return undefined;
    }
    const meta = await this.readMeta(id);
    if (meta === undefined) {
      return undefined;
    }
    if (meta.id !== id) {
      // The id INSIDE the file disagrees with the id its NAME claims — a hand-edited or mis-renamed
      // entry. Trusting either half would key an index by one id and open the record of another, so
      // the entry is dropped and said out loud. The "an id becomes a key" caution chatStore.ts is
      // built around, applied to the listing.
      console.error(`ConnectOtherAIs: a conversation index entry names ${meta.id} but is filed as ${this.metaPath(id)}; dropped`);

      return undefined;
    }

    return meta;
  }

  /**
   * Whether {@link listMeta} would carry this id — its index entry is there, readable, and names it.
   *
   * <p>For a caller that has to confirm a HANDFUL of ids: the migration files twenty records and must
   * prove each is findable before it empties the store it came from, and listing the whole directory
   * to answer that reads thousands of entries to confirm twenty. The same rule as the listing's, by
   * construction — it is the listing's own per-entry read.</p>
   */
  public async listed(id: string): Promise<boolean> {
    return isSafeId(id) && (await this.entry(id)) !== undefined;
  }

  /**
   * Set a value this build cannot file as a conversation aside, AS IT WAS, under the store.
   *
   * <p>Written by the migration for a memento entry that fails the validator, or a whole memento value
   * of a shape no build wrote: neither may be dropped (a conversation deleted with no trace the moment
   * the key is emptied) nor left in the key (a migration that re-runs for ever). The bytes are the
   * raw value's JSON; nothing is interpreted. Said on the console WITH THE PATH, at information level
   * — it is a record kept, not a failure.</p>
   *
   * @param label what the file is called, before the clock; letters, digits, dot, dash, underscore
   * @param at the clock, so two runs cannot overwrite each other's file; a test pins it
   */
  public async quarantine(raw: unknown, label: string, at: number): Promise<QuarantineOutcome> {
    if (!SAFE_LABEL.test(label)) {
      return { kind: 'failed', reason: 'a quarantine label that cannot be a filename' };
    }
    const path = join(this.dir, QUARANTINE_DIR, `${label}-${at}.json`);
    try {
      await writeFileAtomically(path, JSON.stringify(raw) ?? 'undefined');
      console.info(`ConnectOtherAIs: a stored chat record this build cannot read was set aside, unchanged, at ${path}`);

      return { kind: 'ok', at: path };
    } catch (reason) {
      console.error(`ConnectOtherAIs: a stored chat record this build cannot read could not be set aside at ${path}`, reason);

      return { kind: 'failed', reason: withCode('a damaged record could not be set aside', codeOf(reason)) };
    }
  }

  /**
   * Whether the store is usable, and the load-bearing distinction between "empty" and "unavailable".
   *
   * <p>`stat` answers all three cases without a listing: a missing directory (`ENOENT`) is `empty`, a
   * path that is a FILE where the directory should be is `unavailable` (which is also the portable way
   * to provoke the state on Windows, where `chmod` is a no-op), and any other `stat` failure is
   * `unavailable`. A directory that stats fine is then LISTED, because a directory can exist and still
   * refuse to be read (`EACCES`), and only trying tells the two apart.</p>
   */
  public async state(): Promise<StoreState> {
    let stats;
    try {
      stats = await stat(this.dir);
    } catch (reason) {
      return codeOf(reason) === 'ENOENT' ? { kind: 'empty' } : this.unavailable(reason);
    }
    if (!stats.isDirectory()) {
      console.error(`ConnectOtherAIs: a file is where the conversation store should be: ${this.dir}`);

      return { kind: 'unavailable', reason: 'a file is where the conversation store should be' };
    }
    try {
      await readdir(this.dir);

      return { kind: 'ready' };
    } catch (reason) {
      return this.unavailable(reason);
    }
  }

  /** Say the store is unavailable — the path on the console, and none of it in the sentence a person reads. */
  private unavailable(reason: unknown): StoreState {
    console.error(`ConnectOtherAIs: the conversation store is unavailable: ${this.dir}`, reason);

    return { kind: 'unavailable', reason: withCode('the conversation store could not be read', codeOf(reason)) };
  }
}
