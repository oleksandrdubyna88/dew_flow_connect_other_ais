/**
 * Deleting a role, in an order that survives the mirror not writing.
 *
 * <p><b>The defect this replaces.</b> `rolesPanel.ts` wrote the rows and then deleted the prompt
 * files, with nothing in between asking whether the row reached the file the server reads. When it
 * did not, the result was a role that still exists on the server with a prompt that has no text,
 * complaining once per round for ever — the 2026-09-16 incident's own shape, since `Role2` was
 * deleted while the mirror was stood down and eleven code rounds ran against it.</p>
 *
 * <p><b>The asymmetry that decides the order.</b> The row and its four keyed records can be typed
 * again in a minute; the paragraphs of prose in a prompt cannot. So the row and the records go
 * together and go FIRST — they are one payload and the mirror carries them in one write — and the
 * TEXT waits on the far side of the mirror having carried it.</p>
 *
 * <p>Everything is a parameter: the store, the settings, the clock, the reporter. A deletion tested
 * by killing a process is a deletion nobody tests; a deletion whose steps are functions is one where
 * "the host died between step two and step three" is "call step two, throw the object away, call the
 * sweep".</p>
 *
 * <p>Plan: `todo/PLAN_a_deleted_role_stays_deleted.md`.</p>
 */

/** A deletion that has begun and has not finished. */
export interface Tombstone {
  readonly roleId: string;
  /** What the role was called, so a page can name it without the row that is already gone. */
  readonly name: string;
  readonly promptIds: readonly string[];
  readonly askedAt: string;
  /**
   * Minted when the tombstone is written, checked before anything is destroyed.
   *
   * <p>Two windows can work the same deletion. One finishes and clears the tombstone; the person
   * creates a role that takes the freed id; the second resumes and prunes the NEW role's settings
   * and text. Idempotence protects a repeated operation on unchanged state — it does not protect a
   * new thing that reuses an identity, and nothing else here would have noticed. (codex, the plan
   * round.)</p>
   */
  readonly nonce: string;
  /** Why the last attempt could not finish, or empty while it has not failed. */
  readonly reason: string;
  /** When that was, or empty. What makes a tombstone STRANDED rather than merely young. */
  readonly failedAt: string;
}

export interface TombstoneStore {
  readonly put: (tombstone: Tombstone) => Promise<void>;
  readonly all: () => Promise<readonly Tombstone[]>;
  readonly read: (roleId: string) => Promise<Tombstone | undefined>;
  readonly drop: (roleId: string) => Promise<void>;
  /**
   * Take exclusive ownership of a deletion, or answer that somebody else has it.
   *
   * <p>A rename, because it is the one thing a filesystem does atomically: two windows both reach
   * it and exactly one succeeds. The nonce is checked inside the claim, so an id that has come back
   * to life under a new tombstone is refused rather than deleted.</p>
   */
  readonly claim: (roleId: string, nonce: string) => Promise<boolean>;
  /**
   * Every id a deletion is outstanding for, taken from the FILENAMES.
   *
   * <p>Separate from {@link all} on purpose. A tombstone whose contents cannot be read is still a
   * deletion that has not finished, and dropping it from the reservations would release the id to a
   * new role that inherits the old one's prompt files — and the next successful read would have the
   * startup sweep prune the replacement. A name is readable when a body is not. (codex, the code
   * round.)</p>
   */
  readonly reservedIds: () => Promise<ReadonlySet<string>>;
}

export interface DeletionWorld {
  readonly store: TombstoneStore;
  /**
   * Step 2: the row AND the four role-keyed settings, in that one act.
   *
   * <p>They are pruned BEFORE the mirror runs rather than after. They are part of the payload it
   * writes, so pruning them on the far side leaves the server holding orphaned keys until some
   * unrelated setting changes — possibly for ever. (antigravity, the plan round, blocking.)</p>
   */
  readonly prune: (roleId: string) => Promise<void>;
  /**
   * Step 3's condition: does the payload the mirror actually CARRIED still mention this role?
   *
   * <p>It asks about the payload, not about the configuration as it reads now, and the difference
   * is a defect this had for one round. A mirror can finish writing a payload that still contains
   * the role and then call back; while the callback awaits its first read, `begin` finishes pruning
   * the role from the live configuration; a condition asking "is it absent now" answers yes, the
   * text is deleted, and if the NEXT write fails the server holds the role without its prompts —
   * the incident this whole change exists to prevent. Current configuration is not evidence of what
   * was acknowledged. (codex, the code round, twice from two roles.)</p>
   *
   * <p>It settles the half-written case for free, too: step 2 is several `config.update` calls and
   * each one fires the listener, and a payload written between the first and the last still
   * mentions the role.</p>
   */
  readonly mentions: (payload: string, roleId: string) => boolean;
  /** Step 4: the prompt override files. */
  readonly forget: (promptIds: readonly string[]) => Promise<void>;
  readonly now: () => Date;
  /** Said through the funnel when a deletion cannot finish. */
  readonly say: (tombstone: Tombstone, reason: string) => void;
  /**
   * Told whenever what a page would draw has changed — and when it will change NEXT.
   *
   * <p>Without it the recovery controls appear only by accident. `remove` redraws the page before
   * anything can be stranded: that needs a terminal failure and then ten more seconds, and
   * recording the failure writes a file. Nothing in that sequence redraws anything, so a person who
   * leaves the Roles tab open is told to go there and finds no controls. `inMs` is how long until
   * the tombstone crosses the stranding boundary, so the page can come back exactly then instead of
   * polling. (codex, the code round.)</p>
   */
  readonly changed: (inMs: number) => void;
}

/**
 * The reason a stand-down gives, as an exact string.
 *
 * <p>Exact, and exported, because the page decides whether to offer *Reload Window* on it — and a
 * page deciding that by reading prose would offer the cure for one condition to another the day
 * somebody rewords a sentence.</p>
 */
export const STOOD_DOWN = 'a newer build of this extension owns the settings file';

/** How long a tombstone that has failed is given before a page calls it stranded. */
export const STRANDED_AFTER_MS = 10_000;

export class RoleDeletions {
  constructor(
    private readonly world: DeletionWorld,
    private readonly mint: () => string = nonce,
  ) {}

  /**
   * The person pressed Remove: the tombstone first, then the row and the four records.
   *
   * <p>The tombstone is durable BEFORE the row is touched, and the order is the whole of it. Written
   * after, it buys nothing: a process that dies between the row write and the tombstone leaves the
   * next start with no evidence that anything is half-done, the prompt files orphaned and the id
   * free again — which is the state this defect is about.</p>
   *
   * <p>And it STOPS there. Writing the row is what starts the mirror, so the answer cannot be known
   * yet; {@link settled} is where the rest happens.</p>
   */
  async begin(role: {
    readonly id: string;
    readonly name: string;
    readonly promptIds: readonly string[];
  }): Promise<void> {
    await this.world.store.put({
      roleId: role.id,
      name: role.name,
      promptIds: role.promptIds,
      askedAt: this.world.now().toISOString(),
      nonce: this.mint(),
      reason: '',
      failedAt: '',
    });
    await this.world.prune(role.id);
  }

  /**
   * Activation: step 2 again for everything outstanding, then wait like every other deletion.
   *
   * <p>A host that died before the row write is a host that never wrote it, and `prune` is
   * idempotent, so redoing it costs nothing and closes the gap. What it does NOT do is finish
   * anything: at activation nothing has been carried yet, and {@link settled} is what says so.</p>
   */
  async sweep(): Promise<void> {
    for (const tombstone of await this.world.store.all()) {
      await this.world.prune(tombstone.roleId);
    }
  }

  /**
   * The mirror reached a terminal outcome — the ONLY place a deletion finishes.
   *
   * <p>The first draft of this had the deletion call `sync()` itself, and the way that is wrong is
   * the normal path rather than an edge case: writing `coai.roles` fires the configuration listener,
   * which starts the mirror's own schedule, so a direct call moments later answers `busy`. The
   * listener's sync then succeeds, the row really does reach the server, and nothing resumes the
   * cleanup — a completed deletion that looks stranded. Calling `busy` "another attempt" does not
   * schedule that attempt. (codex, the plan round.)</p>
   */
  async settled(carried: boolean, payload: string, reason: string): Promise<void> {
    for (const tombstone of await this.world.store.all()) {
      if (carried && !this.world.mentions(payload, tombstone.roleId)) {
        await this.finish(tombstone);
        continue;
      }
      if (!carried) {
        await this.failed(tombstone, reason);
      }
    }
  }

  /**
   * *Finish the deletion anyway*: the text goes, the id comes back, and the cost has been stated.
   *
   * <p>A mirror that stays stood down until somebody reloads a window can outlive every reload, and
   * a tombstone that cannot clear would bar the person from ever recreating a role of that name. So
   * there has to be a way out — but it is not free, and the page says so before this runs: while the
   * server still carries the row, deleting the text IS the incident, and rounds against that role
   * will complain each time. (antigravity, the plan round.)</p>
   *
   * <p>Keeping the files instead was refused: it releases the id while leaving prose on disk, so the
   * next role of that name opens with a stranger's writing. One of the two costs has to be paid, and
   * the person choosing is the one who should choose which.</p>
   */
  async finishAnyway(roleId: string): Promise<void> {
    const tombstone = await this.world.store.read(roleId);
    if (tombstone !== undefined) {
      await this.finish(tombstone);
    }
  }

  /** Ids no new role may take: every deletion on its way out, whether or not its file parses. */
  async reserved(): Promise<ReadonlySet<string>> {
    return await this.world.store.reservedIds();
  }

  /** What a page shows: failed at least once, and long enough ago to not be a write in flight. */
  async stranded(): Promise<readonly Tombstone[]> {
    const by = this.world.now().getTime() - STRANDED_AFTER_MS;

    return (await this.world.store.all()).filter(
      (one) => one.failedAt !== '' && Date.parse(one.failedAt) <= by,
    );
  }

  /** Steps 4 and 5, guarded by the nonce because the id can be alive again by now. */
  private async finish(tombstone: Tombstone): Promise<void> {
    // CLAIMED, not merely checked. A read followed by a delete is two operations with an await
    // between them, and two windows can both pass the read: one deletes the prompts and drops the
    // tombstone, the person creates a role that takes the released id and writes its prompts, and
    // the other resumes at `forget` and deletes the new text. The claim is a rename, which exactly
    // one of them wins. (codex, the code round, Blocking.)
    if (!(await this.world.store.claim(tombstone.roleId, tombstone.nonce))) {
      return;
    }
    await this.world.forget(tombstone.promptIds);
    await this.world.store.drop(tombstone.roleId);
    // A resolved deletion has to LEAVE the page, not wait there until something unrelated redraws.
    this.world.changed(0);
  }

  /** The reason, on the tombstone and through the funnel — once per condition, not per attempt. */
  private async failed(tombstone: Tombstone, reason: string): Promise<void> {
    if (tombstone.reason === reason) {
      return;
    }
    // Re-read, because this can arrive after the deletion has finished: a notification in flight
    // while another window completed the same deletion would otherwise RESURRECT the tombstone with
    // `put`, stranding the id for ever against a role nobody is deleting any more. (antigravity,
    // the code round.)
    const current = await this.world.store.read(tombstone.roleId);
    if (current === undefined || current.nonce !== tombstone.nonce) {
      return;
    }
    const noted = { ...tombstone, reason, failedAt: this.world.now().toISOString() };

    await this.world.store.put(noted);
    this.world.say(noted, reason);
    // It is not stranded YET — a write that failed a second ago is a write in flight. The page is
    // told when it will be.
    this.world.changed(STRANDED_AFTER_MS);
  }
}

/** Ids that are taken although no row holds them — every deletion still on its way out. */
export function reserved(tombstones: readonly Tombstone[]): ReadonlySet<string> {
  return new Set(tombstones.map((one) => one.roleId.toLowerCase()));
}

function nonce(): string {
  return Math.random().toString(36).slice(2, 10) + Date.now().toString(36);
}
