import { readFile, readdir, rm, stat } from 'node:fs/promises';
import { join } from 'node:path';
import { writeFileAtomically } from './atomicFile';
import {
  ConversationMeta,
  ConversationRecord,
  besideMeta,
  idOfMeta,
  isSafeId,
  isStale,
  metaFrom,
  metaOf,
  recordFrom,
  recordName,
} from './chatStore';
import { claimRevision } from './chatStoreLock';

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
 * <h2>The compare-and-swap, and the atomic operation it rests on</h2>
 *
 * <p>A save proceeds only when the disk holds EXACTLY the revision the caller last read — or nothing
 * at all, for a first write. A rename cannot condition itself on what was read, so the check is made
 * under a claim of the transition: the writer of rev N must first own `<id>.N.lock`, taken by an
 * <b>exclusive create</b> (`chatStoreLock.ts`, which names the operation, the fence and the residual),
 * and the disk is probed only once the claim is held. Two windows that both read 5 both aim at 6; one
 * owns the lock, the other is refused. A window that reads 5 AFTER 6 has landed is refused by the
 * probe; a window whose record was DELETED under it — a baseline above zero and nothing on disk — is
 * refused too, rather than quietly resurrecting what somebody else threw away. Only baseline zero
 * over a genuinely absent record may create. (codex, story A2's code round.)</p>
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
 * stays where it is until an explicit {@link ChatStoreFile.forget} or the sweep of story B1 clears it,
 * neither of which is built here. `read` returns nothing for it, saying so on the console.</p>
 *
 * <h2>Missing is not unreadable</h2>
 *
 * <p>The `readLedger` rule of `jsonlLedger.ts`, applied to a directory of two-file pairs: a store
 * that is not there (`ENOENT`) is an ordinary empty state — nobody has chatted yet — while one that
 * EXISTS and cannot be read (`EACCES`, a file where the directory should be) is a store that has
 * history and is not answering. {@link ChatStoreFile.state} is the one place that distinction is
 * reported, because reporting "no conversations" for a store that could not be read is this module
 * lying about the world, and a later story's sweep must delete nothing while the store is
 * unavailable.</p>
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
 * at this id; nothing was written. <b>`failed`</b> is a disk that would not answer. Every `reason` is
 * a short person-facing sentence naming the fault and never the path.</p>
 */
export type SaveOutcome =
  | { readonly kind: 'ok'; readonly rev: number }
  | { readonly kind: 'partial'; readonly rev: number; readonly reason: string }
  | { readonly kind: 'refused'; readonly diskRev: number }
  | { readonly kind: 'incompatible'; readonly reason: string }
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
 * What is at `<id>.json`, as four facts the save and the read both branch on.
 *
 * <p>`absent` is the only state a first write may create over. `incompatible` is a file that parses
 * to nothing this build trusts; `unreadable` is a file the disk would not hand over at all. The two
 * are kept apart because they mean different things to a save — refuse and fail respectively — and
 * collapsing them was one of the first draft's defects.</p>
 */
type Probe =
  | { readonly kind: 'absent' }
  | { readonly kind: 'record'; readonly record: ConversationRecord }
  | { readonly kind: 'incompatible' }
  | { readonly kind: 'unreadable'; readonly reason: string };

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

/**
 * The I/O half of the conversation store, bound to ONE directory it is handed.
 *
 * <p>It reads no configuration and calls no `coaiDataDir()`: the directory is the caller's to choose,
 * which is what lets a test point it at a temporary and the host point it under the coai data
 * directory. State is the directory and nothing more — every method is otherwise a pure function of
 * the disk.</p>
 */
export class ChatStoreFile {
  public constructor(private readonly dir: string) {}

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
   * <p><b>The new `rev` is `expectedRev + 1`.</b> `expectedRev` is the number the caller last read
   * (undefined or 0 both mean "I have not read this record" — a first write, which lands at rev 1).
   * Counting from what was READ rather than from the record's own `rev` field is deliberate: a first
   * write then lands at 1 without the caller pre-incrementing, and the baseline the swap compares
   * against and the number it writes are the same quantity. Because the swap demands EQUALITY with the
   * disk, the new rev is always the disk's plus one — the count has no gaps.</p>
   *
   * <p><b>The order under the claim.</b> Claim `<id>.<newRev>.lock`; a held claim is another window
   * performing this exact transition, and is `refused`. Then probe the disk: a record at a different
   * rev, or none at all under a baseline above zero, is `refused`; a file this build cannot read is
   * `incompatible`; a disk that will not answer is `failed`. Then the record, then the metadata, then
   * the lock is released in a `finally` — a lock must never outlive a failed save.</p>
   *
   * <p>The store stamps ONE field — `rev`, its own protocol number. Everything else, `updatedAt` and
   * `closedAt` included, is the caller's to set before it calls here; a decision about what a record
   * means is not this half's to make.</p>
   *
   * @param now the clock, an argument so a test can pin it; it dates the lock and ages a rival's.
   */
  public async save(record: ConversationRecord, expectedRev = 0, now = Date.now()): Promise<SaveOutcome> {
    if (!isSafeId(record.id)) {
      return { kind: 'failed', reason: 'a conversation id that cannot be a filename' };
    }
    const base = expectedRev > 0 ? Math.floor(expectedRev) : 0;
    const claim = await claimRevision(this.dir, record.id, base + 1, now);
    if (claim.kind === 'failed') {
      return { kind: 'failed', reason: claim.reason };
    }
    if (claim.kind === 'held') {
      return { kind: 'refused', diskRev: await this.revSeen(record.id, base) };
    }
    try {
      return await this.saveClaimed({ ...record, rev: base + 1 }, base);
    } finally {
      await claim.release();
    }
  }

  /** The swap itself, with the claim held: probe, compare, then the two writes in order. */
  private async saveClaimed(written: ConversationRecord, base: number): Promise<SaveOutcome> {
    const seen = await this.probe(written.id);
    if (seen.kind === 'unreadable') {
      return { kind: 'failed', reason: seen.reason };
    }
    if (seen.kind === 'incompatible') {
      return { kind: 'incompatible', reason: INCOMPATIBLE };
    }
    const diskRev = seen.kind === 'record' ? seen.record.rev : 0;
    if (diskRev !== base) {
      // Higher: another window has taken turns since this one read. Lower, or absent under a baseline
      // above zero: the record this window read was deleted or replaced under it, and writing would
      // resurrect or overwrite what somebody else did on purpose. Either way, not this window's to write.
      return { kind: 'refused', diskRev };
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

  /** The rev to report when a claim is held: what the disk shows, or the baseline when it cannot say. */
  private async revSeen(id: string, base: number): Promise<number> {
    const seen = await this.probe(id);

    return seen.kind === 'record' ? seen.record.rev : seen.kind === 'absent' ? 0 : base;
  }

  /**
   * What is at `<id>.json`, without judging it — the validator does that.
   *
   * <p>Only `ENOENT` is `absent`. Anything else the disk refuses is `unreadable`, and a file the disk
   * hands over that {@link recordFrom} will not accept is `incompatible` — never mistaken for absence,
   * for the reasons in the header.</p>
   */
  private async probe(id: string): Promise<Probe> {
    const path = this.recordPath(id);
    let text: string;
    try {
      text = await readFile(path, 'utf8');
    } catch (reason) {
      if (codeOf(reason) === 'ENOENT') {
        return { kind: 'absent' };
      }
      console.error(`ConnectOtherAIs: a conversation exists but could not be read: ${path}`, reason);

      return { kind: 'unreadable', reason: withCode('the conversation could not be read from disk', codeOf(reason)) };
    }
    const record = recordFrom(parsed(text));
    if (record === undefined) {
      console.error(`ConnectOtherAIs: a conversation file is not one this build can read: ${path}`);

      return { kind: 'incompatible' };
    }

    return { kind: 'record', record };
  }

  /**
   * Write the metadata, from the record and nothing else. Returns the fault, or empty when it landed.
   *
   * <p>{@link metaOf} is the ONLY source of a metadata file, so the two can never describe different
   * things. A write that fails is reported to the caller as the `partial` half of a save, and to the
   * console with the path; it is not thrown, because the record has already landed and the reader
   * regenerates the index from it whenever they disagree.</p>
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
   * One conversation back, or nothing — and the metadata reconciled against it on the way.
   *
   * <p>Missing is nothing, an ordinary "no such conversation". Unreadable and incompatible are also
   * nothing to the caller — a read must not throw over one record — but each is SAID first, with the
   * path, because a permissions error and a file this build cannot parse are not the same fact as an
   * absence. A present-but-torn record is dropped, never repaired: a transcript with a hole in it
   * reads as a conversation the person recognises with pieces missing, which is worse than one that
   * is not there.</p>
   *
   * <p>The metadata is regenerated when it is missing, torn, or stale — the record is the truth, and
   * the index is brought back into step with it here so a picker built afterwards is not looking at
   * the turn before.</p>
   */
  public async read(id: string): Promise<ConversationRecord | undefined> {
    if (!isSafeId(id)) {
      return undefined;
    }
    const seen = await this.probe(id);
    if (seen.kind !== 'record') {
      return undefined;
    }
    await this.reconcileMeta(seen.record);

    return seen.record;
  }

  /** Bring the metadata back into step with its record, best-effort: a read still hands back the record. */
  private async reconcileMeta(record: ConversationRecord): Promise<void> {
    const meta = await this.readMeta(record.id);
    if (meta !== undefined && !isStale(meta, record)) {
      return;
    }
    await this.writeMeta(record);
  }

  /** One metadata file back, or nothing. Unreadable is said out loud with its path; missing and torn are silent nothings. */
  private async readMeta(id: string): Promise<ConversationMeta | undefined> {
    const path = this.metaPath(id);
    try {
      return metaFrom(parsed(await readFile(path, 'utf8')));
    } catch (reason) {
      if (codeOf(reason) !== 'ENOENT') {
        console.error(`ConnectOtherAIs: a conversation index entry could not be read: ${path}`, reason);
      }

      return undefined;
    }
  }

  /**
   * Delete both files — metadata FIRST, then the record.
   *
   * <p>The reverse of a save's order, and deliberate. A crash between the two deletes then leaves a
   * record with no metadata: a file nobody can SEE, because the picker lists metadata only, which the
   * sweep of story B1 collects later. The other order would leave metadata with no record — a row that
   * opens onto nothing, which is the one outcome a person actually meets. So if the metadata delete
   * FAILS, the record is left in place too: a row that still opens is what remains, never a broken
   * one. This is also the one built-in way to clear an `incompatible` file at an id.</p>
   */
  public async forget(id: string): Promise<void> {
    if (!isSafeId(id)) {
      return;
    }
    try {
      await rm(this.metaPath(id), { force: true });
    } catch (reason) {
      console.error(`ConnectOtherAIs: a conversation index entry could not be deleted: ${this.metaPath(id)}`, reason);

      return;
    }
    try {
      await rm(this.recordPath(id), { force: true });
    } catch (reason) {
      console.error(`ConnectOtherAIs: a conversation transcript could not be deleted after its index entry: ${this.recordPath(id)}`, reason);
    }
  }

  /**
   * Every readable metadata file in the directory — and only the metadata files.
   *
   * <p>A transcript is NEVER opened to draw the list: {@link idOfMeta} returns empty for anything but
   * a `<id>.meta.json` with a safe id, so a `<id>.json`, an interrupted `.tmp` write, a `.lock` and a
   * stray `notes.txt` are all skipped before a byte is read. An unreadable or torn entry is dropped
   * rather than allowed to fail the whole listing — one corrupt index file must not empty a person's
   * picker. A directory that cannot be read lists as empty here; {@link state} is what tells a caller
   * that empty means "unavailable" rather than "nothing", which is the distinction it must check
   * first. A transcript with no metadata beside it is invisible here BY DESIGN and is the sweep's to
   * reclaim, not this listing's to hunt for.</p>
   */
  public async listMeta(): Promise<readonly ConversationMeta[]> {
    let names: readonly string[];
    try {
      names = await readdir(this.dir);
    } catch (reason) {
      if (codeOf(reason) !== 'ENOENT') {
        console.error(`ConnectOtherAIs: the conversation store could not be listed: ${this.dir}`, reason);
      }

      return [];
    }
    const metas: ConversationMeta[] = [];
    for (const name of names) {
      const id = idOfMeta(name);
      if (id.length === 0) {
        continue;
      }
      const meta = await this.readListed(id);
      if (meta !== undefined) {
        metas.push(meta);
      }
    }

    return metas;
  }

  /** One listed entry, dropped when it is torn, unreadable, or names an id its filename does not. */
  private async readListed(id: string): Promise<ConversationMeta | undefined> {
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
