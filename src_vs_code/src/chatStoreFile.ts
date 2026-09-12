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
 * <h2>The record is the commit</h2>
 *
 * <p>Once the record's rename lands at its new `rev` the save has committed, because the record is
 * what {@link read} regenerates the metadata from. A metadata write that fails afterwards is
 * therefore logged and no more — it self-heals on the next read, and failing the whole save would be
 * worse than useless (see {@link ChatStoreFile.save}). What is never committed silently is a lost
 * update: a write whose baseline is behind the disk is REFUSED, not overwritten, and the caller
 * re-mints under a new id.</p>
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
 * <p><b>Nothing here throws.</b> A filesystem failure becomes a typed outcome ({@link SaveOutcome}),
 * an empty listing, or `undefined`, with the fault said out loud on the console — never swallowed,
 * per `coding-style.md`, and never carrying a stack or a full path into a sentence a person reads.</p>
 */

/**
 * What a save did, as one of three facts the caller reacts to differently.
 *
 * <p><b>`ok`</b> carries the new `rev`, which is the caller's next baseline — hold it, and the next
 * save's compare-and-swap has the number it needs. <b>`refused`</b> is not a failure: another window
 * advanced this conversation, and the caller keeps its transcript and re-mints under a new id (story
 * A3). <b>`failed`</b> is a disk that would not answer; the `reason` is a short sentence shown to a
 * person, so it names the fault and never the path.</p>
 */
export type SaveOutcome =
  | { readonly kind: 'ok'; readonly rev: number }
  | { readonly kind: 'refused'; readonly diskRev: number }
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
   * against and the number it writes are the same quantity, so the caller never has to keep the
   * record's `rev` in step with what it read.</p>
   *
   * <p><b>The swap.</b> A record on disk carrying a `rev` HIGHER than the baseline means another
   * window advanced this conversation since the caller read it — two windows in one record, which is
   * reachable. The write is then refused rather than performed, and nothing is written; the caller
   * re-mints under a new id and no turn is lost on either side. A disk that is behind the baseline (a
   * deleted-and-recreated file, a stale higher expectation) is not a conflict — the new `rev` is
   * still `expectedRev + 1`, which is strictly greater than what is on disk, so the record never
   * appears to move backwards.</p>
   *
   * <p><b>Record first, metadata second — and the record is the commit.</b> Once the record's rename
   * lands the save has committed; the metadata is a derived index the reader regenerates from the
   * record ({@link isStale}), so a metadata write that fails is logged and does NOT fail the save
   * (see {@link writeMeta}). A failure to write the RECORD is a real failure, and the conversation
   * stays on screen for the caller to act on.</p>
   *
   * <p>The store stamps ONE field — `rev`, its own protocol number. Everything else, `updatedAt` and
   * `closedAt` included, is the caller's to set before it calls here; a decision about what a record
   * means is not this half's to make.</p>
   */
  public async save(record: ConversationRecord, expectedRev = 0): Promise<SaveOutcome> {
    if (!isSafeId(record.id)) {
      return { kind: 'failed', reason: 'a conversation id that cannot be a filename' };
    }
    const probe = await this.diskRevOf(record.id);
    if (!probe.ok) {
      return { kind: 'failed', reason: probe.reason };
    }
    const base = expectedRev > 0 ? Math.floor(expectedRev) : 0;
    if (probe.rev > base) {
      return { kind: 'refused', diskRev: probe.rev };
    }
    const written: ConversationRecord = { ...record, rev: base + 1 };
    try {
      await writeFileAtomically(this.recordPath(record.id), JSON.stringify(written));
    } catch (reason) {
      return { kind: 'failed', reason: withCode('the conversation could not be saved to disk', codeOf(reason)) };
    }
    await this.writeMeta(written);

    return { kind: 'ok', rev: written.rev };
  }

  /**
   * The `rev` on disk, or a fault this half cannot swap against.
   *
   * <p>Three answers become `rev 0` and let the save proceed: nothing there (`ENOENT` — a first
   * write), a torn file, and a record of a version this build does not know. None of those is a
   * competing writer holding a higher rev, and overwriting the last two is the repair-by-replacement
   * the plan calls for — a damaged record is dropped, never patched. The one answer that is NOT rev 0
   * is a record that EXISTS and could not be READ: a compare-and-swap that cannot see the current
   * state must not guess at it, because a blind overwrite is exactly the lost update the rev exists to
   * prevent. That fails the save.</p>
   */
  private async diskRevOf(id: string): Promise<{ ok: true; rev: number } | { ok: false; reason: string }> {
    try {
      const record = recordFrom(parsed(await readFile(this.recordPath(id), 'utf8')));

      return { ok: true, rev: record === undefined ? 0 : record.rev };
    } catch (reason) {
      if (codeOf(reason) === 'ENOENT') {
        return { ok: true, rev: 0 };
      }

      return { ok: false, reason: withCode('the conversation could not be read to save it', codeOf(reason)) };
    }
  }

  /**
   * Write the metadata, from the record and nothing else, and never let its failure fail a save.
   *
   * <p>{@link metaOf} is the ONLY source of a metadata file, so the two can never describe different
   * things. A write that fails is logged and no more: the record has already landed at its new rev,
   * the reader regenerates the index from it whenever they disagree, and a save that reported itself
   * failed here would leave the caller holding a stale expected rev — so its own next write, against
   * a disk now one ahead, would be refused as a conflict with itself. Said out loud, never swallowed,
   * per `coding-style.md`.</p>
   */
  private async writeMeta(record: ConversationRecord): Promise<void> {
    try {
      await writeFileAtomically(this.metaPath(record.id), JSON.stringify(metaOf(record)));
    } catch (reason) {
      console.error('ConnectOtherAIs: a conversation was saved but its index entry could not be written', reason);
    }
  }

  /**
   * One conversation back, or nothing — and the metadata reconciled against it on the way.
   *
   * <p>Missing (`ENOENT`) is nothing, an ordinary "no such conversation". Unreadable is also nothing
   * to the caller — a read must not throw over one record — but it is SAID first, because a
   * permissions error is not the same fact as an absence. A present-but-torn record is dropped, never
   * repaired: a transcript with a hole in it reads as a conversation the person recognises with
   * pieces missing, which is worse than one that is not there.</p>
   *
   * <p>The metadata is regenerated when it is missing, torn, or stale — the record is the truth, and
   * the index is brought back into step with it here so a picker built afterwards is not looking at
   * the turn before.</p>
   */
  public async read(id: string): Promise<ConversationRecord | undefined> {
    if (!isSafeId(id)) {
      return undefined;
    }
    let record: ConversationRecord | undefined;
    try {
      record = recordFrom(parsed(await readFile(this.recordPath(id), 'utf8')));
    } catch (reason) {
      if (codeOf(reason) !== 'ENOENT') {
        console.error('ConnectOtherAIs: a conversation exists but could not be read', reason);
      }

      return undefined;
    }
    if (record === undefined) {
      return undefined;
    }
    await this.reconcileMeta(record);

    return record;
  }

  /** Bring the metadata back into step with its record, best-effort: a read still hands back the record. */
  private async reconcileMeta(record: ConversationRecord): Promise<void> {
    const meta = await this.readMeta(record.id);
    if (meta !== undefined && !isStale(meta, record)) {
      return;
    }
    await this.writeMeta(record);
  }

  /** One metadata file back, or nothing. Unreadable is said out loud; missing and torn are silent nothings. */
  private async readMeta(id: string): Promise<ConversationMeta | undefined> {
    try {
      return metaFrom(parsed(await readFile(this.metaPath(id), 'utf8')));
    } catch (reason) {
      if (codeOf(reason) !== 'ENOENT') {
        console.error('ConnectOtherAIs: a conversation index entry could not be read', reason);
      }

      return undefined;
    }
  }

  /**
   * Delete both files — metadata FIRST, then the record.
   *
   * <p>The reverse of a save's order, and deliberate. A crash between the two deletes then leaves a
   * record with no metadata: a file nobody can SEE, because the picker lists metadata only, which the
   * sweep collects later. The other order would leave metadata with no record — a row that opens onto
   * nothing, which is the one outcome a person actually meets. So if the metadata delete FAILS, the
   * record is left in place too: a row that still opens is what remains, never a broken one.</p>
   */
  public async forget(id: string): Promise<void> {
    if (!isSafeId(id)) {
      return;
    }
    try {
      await rm(this.metaPath(id), { force: true });
    } catch (reason) {
      console.error('ConnectOtherAIs: a conversation index entry could not be deleted', reason);

      return;
    }
    try {
      await rm(this.recordPath(id), { force: true });
    } catch (reason) {
      console.error('ConnectOtherAIs: a conversation transcript could not be deleted after its index entry', reason);
    }
  }

  /**
   * Every readable metadata file in the directory — and only the metadata files.
   *
   * <p>A transcript is NEVER opened to draw the list: {@link idOfMeta} returns empty for anything but
   * a `<id>.meta.json` with a safe id, so a `<id>.json`, an interrupted `.tmp` write and a stray
   * `notes.txt` are all skipped before a byte is read. An unreadable or torn entry is dropped rather
   * than allowed to fail the whole listing — one corrupt index file must not empty a person's picker.
   * A directory that cannot be read lists as empty here; {@link state} is what tells a caller that
   * empty means "unavailable" rather than "nothing", which is the distinction it must check first.</p>
   */
  public async listMeta(): Promise<readonly ConversationMeta[]> {
    let names: readonly string[];
    try {
      names = await readdir(this.dir);
    } catch (reason) {
      if (codeOf(reason) !== 'ENOENT') {
        console.error('ConnectOtherAIs: the conversation store could not be listed', reason);
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
      console.error(`ConnectOtherAIs: a conversation index entry names ${meta.id} but is filed as ${id}; dropped`);

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
      return { kind: 'unavailable', reason: 'a file is where the conversation store should be' };
    }
    try {
      await readdir(this.dir);

      return { kind: 'ready' };
    } catch (reason) {
      return this.unavailable(reason);
    }
  }

  /** Say the store is unavailable, out loud and without a path in the sentence a person reads. */
  private unavailable(reason: unknown): StoreState {
    console.error('ConnectOtherAIs: the conversation store is unavailable', reason);

    return { kind: 'unavailable', reason: withCode('the conversation store could not be read', codeOf(reason)) };
  }
}
