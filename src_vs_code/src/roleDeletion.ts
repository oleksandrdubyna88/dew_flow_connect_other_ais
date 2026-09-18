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
   * Half of step 3's condition: is the role absent from the configuration as it reads RIGHT NOW?
   *
   * <p>The other half is the mirror having carried a write, and it takes BOTH. Step 2 is several
   * `config.update` calls and each one fires VS Code's configuration listener, so a sync that landed
   * between the first and the last carried an incomplete removal. `sync()` writes what the settings
   * say at the moment it runs, so a landed sync whose configuration no longer mentions the role is
   * proof the server has the whole of it. Re-read every time rather than remembered, which is also
   * what makes this step idempotent.</p>
   */
  readonly gone: (roleId: string) => boolean;
  /** Step 4: the prompt override files. */
  readonly forget: (promptIds: readonly string[]) => Promise<void>;
  readonly now: () => Date;
  /** Said through the funnel when a deletion cannot finish. */
  readonly say: (tombstone: Tombstone, reason: string) => void;
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
  async settled(carried: boolean, reason: string): Promise<void> {
    for (const tombstone of await this.world.store.all()) {
      if (carried && this.world.gone(tombstone.roleId)) {
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

  /** What a page shows: failed at least once, and long enough ago to not be a write in flight. */
  async stranded(): Promise<readonly Tombstone[]> {
    const by = this.world.now().getTime() - STRANDED_AFTER_MS;

    return (await this.world.store.all()).filter(
      (one) => one.failedAt !== '' && Date.parse(one.failedAt) <= by,
    );
  }

  /** Steps 4 and 5, guarded by the nonce because the id can be alive again by now. */
  private async finish(tombstone: Tombstone): Promise<void> {
    const current = await this.world.store.read(tombstone.roleId);
    if (current === undefined || current.nonce !== tombstone.nonce) {
      return;
    }
    await this.world.forget(tombstone.promptIds);
    await this.world.store.drop(tombstone.roleId);
  }

  /** The reason, on the tombstone and through the funnel — once per condition, not per attempt. */
  private async failed(tombstone: Tombstone, reason: string): Promise<void> {
    if (tombstone.reason === reason) {
      return;
    }
    const noted = { ...tombstone, reason, failedAt: this.world.now().toISOString() };

    await this.world.store.put(noted);
    this.world.say(noted, reason);
  }
}

/** Ids that are taken although no row holds them — every deletion still on its way out. */
export function reserved(tombstones: readonly Tombstone[]): ReadonlySet<string> {
  return new Set(tombstones.map((one) => one.roleId.toLowerCase()));
}

function nonce(): string {
  return Math.random().toString(36).slice(2, 10) + Date.now().toString(36);
}
