/**
 * What the admin API of `coai-bugs` puts on the wire.
 *
 * <p><b>A module of its own, importing nothing.</b> The reader that CHECKS these shapes
 * (`bugsAdminWire.ts`) and the client that FETCHES them (`bugsAdminApi.ts`) both need them, and
 * with the shapes living in the client the two imported each other — a cycle that this
 * repository's `importCycles` guard caught on the first run after it appeared, with the sentence
 * that says why it is not a style question: *it will bundle and fail at runtime*.</p>
 *
 * <p>So the shapes sit underneath both. `bugsAdminApi` re-exports them, because a caller asking for
 * "the admin API's types" should not have to know which of two files they were written in.</p>
 */

/** One key, as the listing gives it. No key value and no hash: the wire has no field for either. */
export interface KeyRow {
  readonly id: string;
  readonly note: string;
  readonly createdUtc: string;
  /** Absent while the key is in force. */
  readonly revokedUtc?: string;
  /** `yyyy-MM`, or absent for never used — never a date and never a clock time. */
  readonly lastSeenMonth?: string;
  readonly sent: number;
  readonly waiting: number;
}

/** A page of keys. `nextBefore` is present only while another page may exist. */
export interface KeysPage {
  readonly items: readonly KeyRow[];
  readonly limit: number;
  readonly total: number;
  readonly nextBefore?: string;
}

/** The one response in this product that carries a key, and it carries it once. */
export interface IssuedKey {
  readonly id: string;
  readonly key: string;
  readonly note: string;
  readonly createdUtc: string;
}

/** What revoking came to. `changed` false means somebody else had already done it. */
export interface Revocation {
  readonly id: string;
  readonly revokedUtc: string;
  readonly changed: boolean;
}
