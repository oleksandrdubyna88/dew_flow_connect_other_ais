import { rmSync } from 'node:fs';
import { rm } from 'node:fs/promises';
import { join } from 'node:path';
import { writeFileAtomically, writeFileAtomicallySync } from './atomicFile';
import { SNAPSHOT_FILE, snapshotText } from './sidebarSnapshot';

/**
 * The old code's half of the sidebar snapshot (v2 E4 s3-sidebar): after the sidebar has been painted with its data, the page is kept for
 * the next window. The format, the limits and the reader are in `sidebarSnapshot.ts` (they run before this code is loaded).
 *
 * <p>Never on the render's path: a render OFFERS a page (a function, not html — building it is not free) and the file is written later, at
 * most once in {@link MIN_GAP_MS}, from the newest offer. A page that cannot be kept (too big, credential-like) REMOVES the file rather than
 * leaving an older, different page behind. A write that fails is logged once and the next offer tries again: a snapshot is a convenience,
 * and nothing waits for it. On {@link dispose} an offer still waiting is written at once, synchronously, because the editor may be closing.</p>
 */

/** The wait before the first write of a window, and the least time between two writes after it. */
const FIRST_WRITE_MS = 1000;
export const MIN_GAP_MS = 10_000;

/** What the writer needs from the world; a test replaces each of these. */
export interface SnapshotSeams {
  readonly directory: () => string;
  readonly version: string;
  readonly now: () => number;
  readonly setTimer: (run: () => void, ms: number) => { readonly stop: () => void };
  readonly write: (path: string, text: string) => Promise<void>;
  readonly writeSync: (path: string, text: string) => void;
  readonly remove: (path: string) => Promise<void>;
  readonly removeSync: (path: string) => void;
  readonly log: (message: string, error: unknown) => void;
}

const REAL: Omit<SnapshotSeams, 'directory' | 'version'> = {
  now: () => Date.now(),
  setTimer: (run, ms) => {
    const timer = setTimeout(run, ms);
    timer.unref();

    return { stop: () => { clearTimeout(timer); } };
  },
  write: writeFileAtomically,
  writeSync: writeFileAtomicallySync,
  remove: (path) => rm(path, { force: true }),
  removeSync: (path) => { rmSync(path, { force: true }); },
  log: (message, error) => { console.error(`ConnectOtherAIs: ${message}`, error); },
};

export class SidebarSnapshotWriter {
  private readonly seams: SnapshotSeams;
  private offered: (() => string) | undefined;
  private timer: { readonly stop: () => void } | undefined;
  private lastWriteAt = Number.NEGATIVE_INFINITY;
  /** The text last written (or `''` when the file was removed on purpose), so an unchanged page costs no write. */
  private last: string | undefined;
  private failedOnce = false;
  private disposed = false;
  private writing: Promise<void> = Promise.resolve();

  constructor(seams: Pick<SnapshotSeams, 'directory' | 'version'> & Partial<SnapshotSeams>) {
    this.seams = { ...REAL, ...seams };
  }

  /** Offers the sidebar's page as of now — built with `NONCE_PLACEHOLDER` where the nonce goes; the newest offer is the one written. Never throws, never waits. */
  offer(page: () => string): void {
    if (this.disposed || this.seams.version === '') {
      return;
    }
    this.offered = page;
    if (this.timer === undefined) {
      const wait = Math.max(FIRST_WRITE_MS, this.lastWriteAt + MIN_GAP_MS - this.seams.now());
      this.timer = this.seams.setTimer(() => { this.timer = undefined; this.flush(false); }, wait);
    }
  }

  /** What the file must become next, or nothing: no offer, a page that could not be built, or a page the file already holds. `text` undefined means remove it. */
  private take(): { readonly text: string | undefined } | undefined {
    const page = this.offered;
    this.offered = undefined;

    return page === undefined ? undefined : this.changeFor(page);
  }

  private changeFor(page: () => string): { readonly text: string | undefined } | undefined {
    let text: string | undefined;
    try {
      text = snapshotText(this.seams.version, page());
    } catch (error) {
      this.seams.log('the sidebar snapshot could not be built', error);

      return undefined;
    }
    this.lastWriteAt = this.seams.now();

    return (text ?? '') === this.last ? undefined : { text };
  }

  /** Writes the newest offer now (asynchronously, or synchronously when the editor is closing). */
  private flush(closing: boolean): void {
    const change = this.take();
    if (change === undefined) {
      return;
    }
    this.last = change.text ?? '';
    const path = join(this.seams.directory(), SNAPSHOT_FILE);
    if (closing) {
      this.applyNow(path, change.text);
    } else {
      this.applyLater(path, change.text);
    }
  }

  private applyNow(path: string, text: string | undefined): void {
    try {
      if (text === undefined) {
        this.seams.removeSync(path);
      } else {
        this.seams.writeSync(path, text);
      }
    } catch (error) {
      this.seams.log('the sidebar snapshot could not be written', error);
    }
  }

  /** One write at a time, in order: a slow disk never has two renames racing for one name. */
  private applyLater(path: string, text: string | undefined): void {
    this.writing = this.writing.then(() => (text === undefined ? this.seams.remove(path) : this.seams.write(path, text))).catch((error: unknown) => {
      this.last = undefined;
      if (!this.failedOnce) {
        this.failedOnce = true;
        this.seams.log('the sidebar snapshot could not be written', error);
      }
    });
  }

  /** Settles what is in flight; for a test, and for nothing else. */
  settled(): Promise<void> {
    return this.writing;
  }

  /** The window is going: an offer still waiting is written now, and nothing is written after. */
  dispose(): void {
    this.timer?.stop();
    this.timer = undefined;
    this.flush(true);
    this.disposed = true;
  }
}
