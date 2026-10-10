import { PersonListing, ServerResult, Usage } from './teamServerApi';

/**
 * What each Team server says has been spent on it — asked, remembered and refreshed, without `vscode`.
 *
 * <p>Extracted from `PanelProvider` (todo/PLAN_team_usage_by_person.md, story 1.1) BEFORE anything about it changed,
 * because that class imports `vscode` and no test can construct it: the defects the story fixes could not be shown
 * failing until the code they live in could be run. The clock, the request and the admin flag are handed in.</p>
 *
 * <p><b>Keyed by server, scope and window.</b> It held one answer per server, so a personal answer and a company
 * answer — or Today's and a Year's — overwrote each other, and one freshness stamp for everything let a sidebar refresh
 * make the page's question look answered. Each key now has its own answer, its own stamp and its own sequence number,
 * and the number is checked BEFORE an answer is written, so the older of two requests landing last is dropped.</p>
 *
 * <p><b>Privacy is an eviction, never a filter.</b> A refusal of `company` (`401`/`403`), a catalog that says the caller
 * is no longer an admin, a different account on the token, a sign-out or a removal each FORGET what was held — a failed
 * request used to keep the last good answer, which would have shown a demoted admin every colleague's email.</p>
 *
 * <p>A stateful service, so a class that changes its own maps; the cells it hands out are never mutated.</p>
 */

/** Whose spending: this account's, or — for an admin — everybody's. */
export type UsageScope = 'me' | 'company';

/**
 * What a surface can want of a server: a spending scope, or — for an admin — the list of who is signed in
 * (`GET /api/people`, todo/PLAN_team_usage_by_person.md 3.1), which has no window and is kept under `''`.
 */
export type WantScope = UsageScope | 'people';

/** The scopes only an admin is answered, and a refusal of which forgets every one of them. */
const ADMIN_ONLY: ReadonlySet<WantScope> = new Set<WantScope>(['company', 'people']);

/** How long an answer stands before it is asked again. */
export const USAGE_FRESH_MS = 60 * 1000;

/** The server a request goes to. */
export interface UsageServer {
  readonly id: string;
  readonly url: string;
}

/** One thing a surface wants shown: a scope over a window, in the server's own window names. */
export interface UsageWant {
  readonly scope: WantScope;
  readonly window: string;
}

/** What one refresh learned about a server before its usage is asked. */
export interface Prepared {
  /** The bearer this side holds for it, or empty when it holds none — then nothing is asked. */
  readonly token: string;
  /** Whether THIS refresh's catalog says the caller is an admin there. */
  readonly admin: boolean;
  /** Whose token it is, so an answer for one account is never shown under another. */
  readonly account: string;
}

/** One server to refresh: what is wanted of it, and how to bring it up to date first. */
export interface UsageTarget {
  readonly server: UsageServer;
  readonly wants: readonly UsageWant[];
  readonly prepare: () => Promise<Prepared>;
}

/** What is known for one server, scope and window. */
export interface UsageCell {
  /** The last answer, or undefined when there has never been one — or it was evicted. */
  readonly usage?: Usage | undefined;
  /** The last people listing, for the `people` key — undefined like `usage` when there is none. */
  readonly people?: readonly PersonListing[] | undefined;
  /** Why the last request failed, or empty. */
  readonly problem: string;
  /** The server refused this scope (`401`/`403`): for `company`, this account is no admin there now. */
  readonly refused?: boolean | undefined;
  /** The HTTP contract the answering server said it speaks, when a response arrived. */
  readonly contract?: number | undefined;
  /** When the answer landed — what the page prints as "Read". */
  readonly answeredAt: number;
  /**
   * When the request it answers was SENT — what freshness is measured from. From the landing instead, a one-minute
   * clock found every answer that took a moment still fresh and re-asked only every second minute.
   */
  readonly askedAt?: number;
}

export interface TeamUsageDeps {
  readonly now: () => number;
  readonly fetchUsage: (url: string, token: string, window: string, scope: UsageScope) => Promise<ServerResult<Usage>>;
  /** `GET /api/people`. Absent: nothing asks for the listing (a caller with no use for it). */
  readonly fetchPeople?: (url: string, token: string) => Promise<ServerResult<readonly PersonListing[]>>;
  /** Told after every answer that lands, so whatever shows these figures — the sidebar AND the page — repaints. */
  readonly changed: () => void;
}

/** A refusal that means "not this caller", which for `company` means "not an admin here any more". */
const REFUSED = new Set([401, 403]);

/** The key one answer is kept under. NUL cannot occur in a server id, a scope or a window name. */
function keyOf(serverId: string, scope: WantScope, window: string): string {
  return `${serverId}\u0000${scope}\u0000${window}`;
}

/** Whether a failure means "not this caller" for a scope that needs an admin — then nothing held may stay. */
function refuses(scope: WantScope, status: number): boolean {
  return ADMIN_ONLY.has(scope) && REFUSED.has(status);
}

function belongsTo(key: string, serverId: string, scope?: WantScope): boolean {
  return key.startsWith(scope === undefined ? `${serverId}\u0000` : `${serverId}\u0000${scope}\u0000`);
}

/** What a failure keeps of the cell it replaces: nothing after a refusal, everything after an outage. */
function keptThrough(refused: boolean, known: UsageCell | undefined): UsageCell | undefined {
  return refused ? undefined : known;
}

/** What one key holds of an answer: the spending, or the listing. */
interface Answer {
  readonly usage?: Usage;
  readonly people?: readonly PersonListing[];
}

/** A typed answer, as the one shape a cell is written from — a failure carried across unchanged. */
function asAnswer<T>(result: ServerResult<T>, held: (value: T) => Answer): ServerResult<Answer> {
  return result.ok ? { ...result, value: held(result.value) } : result;
}

export class TeamUsageCache {
  private cells: ReadonlyMap<string, UsageCell> = new Map();

  /** The number of the newest request per key, and of the newest one that has settled. */
  private readonly newest = new Map<string, number>();
  private readonly settled = new Map<string, number>();
  private requests = 0;

  /** Whose token each server's answers were fetched with. */
  private readonly accounts = new Map<string, string>();

  constructor(private readonly deps: TeamUsageDeps) {}

  cell(serverId: string, scope: WantScope, window: string): UsageCell | undefined {
    return this.cells.get(keyOf(serverId, scope, window));
  }

  /** Whether this key's answer is recent enough to stand. */
  isFresh(serverId: string, scope: WantScope, window: string): boolean {
    const held = this.cell(serverId, scope, window);

    return held !== undefined && this.deps.now() - (held.askedAt ?? held.answeredAt) < USAGE_FRESH_MS;
  }

  /** Whether a request for this key is on its way. */
  isAsking(serverId: string, scope: WantScope, window: string): boolean {
    const key = keyOf(serverId, scope, window);

    return this.newest.has(key) && this.newest.get(key) !== this.settled.get(key);
  }

  /** Forget everything about one server — signed out of, removed, or now someone else's. */
  evictServer(serverId: string): void {
    this.forget((key) => belongsTo(key, serverId));
    this.accounts.delete(serverId);
  }

  /**
   * Forget one server's admin views — its company figures AND its people listing: the caller is no admin there, as far
   * as anyone can now tell.
   */
  evictCompany(serverId: string): void {
    this.forget((key) => [...ADMIN_ONLY].some((scope) => belongsTo(key, serverId, scope)));
  }

  /** Whether anything a target wants is neither fresh nor already being asked. */
  isDue(target: UsageTarget): boolean {
    return target.wants.some((want) => this.needs(target.server.id, want));
  }

  /**
   * Bring every target up to date, concurrently — each key on its own, so a changed selection is asked even while
   * another request is running. `force` asks again whatever is fresh or in flight (the Refresh button, a sign-in).
   */
  async refresh(targets: readonly UsageTarget[], force = false): Promise<void> {
    await Promise.allSettled(targets.map((target) => this.refreshOne(target, force)));
  }

  private async refreshOne(target: UsageTarget, force: boolean): Promise<void> {
    if (!force && !this.isDue(target)) {
      return;
    }
    const prepared = await target.prepare();
    if (prepared.token.length === 0) {
      return;
    }
    this.heldFor(target.server.id, prepared);
    const asked = target.wants.filter((want) => (want.scope === 'me' || prepared.admin)
      && (force || this.needs(target.server.id, want)));
    await Promise.allSettled(asked.map((want) => this.ask(target.server, prepared.token, want)));
  }

  /** Evict what this refresh says may no longer be shown: another account's answers, or company figures for a non-admin. */
  private heldFor(serverId: string, prepared: Prepared): void {
    const before = this.accounts.get(serverId);
    if (before !== undefined && before !== prepared.account) {
      this.evictServer(serverId);
    }
    this.accounts.set(serverId, prepared.account);
    if (!prepared.admin) {
      this.evictCompany(serverId);
    }
  }

  private needs(serverId: string, want: UsageWant): boolean {
    return !this.isFresh(serverId, want.scope, want.window) && !this.isAsking(serverId, want.scope, want.window);
  }

  /** One request, numbered; its answer is written only if nothing newer was asked and nothing was evicted meanwhile. */
  private async ask(server: UsageServer, token: string, want: UsageWant): Promise<void> {
    const key = keyOf(server.id, want.scope, want.window);
    this.requests += 1;
    const number = this.requests;
    this.newest.set(key, number);
    const askedAt = this.deps.now();
    const spent = await this.fetch(server, token, want);
    if (this.newest.get(key) !== number) {
      return;
    }
    // A refusal of an admin view is about the CALLER, not the window: every window of this server's company figures
    // and its people listing go, and any still in flight with them, before the refusal is written down.
    if (!spent.ok && refuses(want.scope, spent.status)) {
      this.evictCompany(server.id);
    }
    this.settled.set(key, number);
    this.write(key, want.scope, spent, askedAt);
    this.deps.changed();
  }

  /** The one request a want stands for: the listing, or the spending over its window. */
  private async fetch(server: UsageServer, token: string, want: UsageWant): Promise<ServerResult<Answer>> {
    if (want.scope === 'people') {
      return asAnswer(await this.peopleOf(server, token), (people) => ({ people }));
    }

    return asAnswer(await this.deps.fetchUsage(server.url, token, want.window, want.scope), (usage) => ({ usage }));
  }

  private peopleOf(server: UsageServer, token: string): Promise<ServerResult<readonly PersonListing[]>> {
    return this.deps.fetchPeople === undefined
      ? Promise.resolve({ ok: false, status: 0, contract: undefined, message: 'nothing here asks for the people listing' })
      : this.deps.fetchPeople(server.url, token);
  }

  private write(key: string, scope: WantScope, spent: ServerResult<Answer>, askedAt: number): void {
    const known = this.cells.get(key);
    const answeredAt = this.deps.now();
    const cell: UsageCell = spent.ok
      ? { ...spent.value, problem: '', contract: spent.contract, answeredAt, askedAt }
      : { ...this.failed(scope, spent, known, answeredAt), askedAt };
    this.cells = new Map([...this.cells, [key, cell]]);
  }

  /**
   * A failure: an outage keeps the last good answer and says what went wrong; a refusal of `company` keeps NOTHING.
   */
  private failed(
    scope: WantScope,
    spent: Extract<ServerResult<Answer>, { ok: false }>,
    known: UsageCell | undefined,
    answeredAt: number,
  ): UsageCell {
    const refused = refuses(scope, spent.status);
    const kept = keptThrough(refused, known) ?? { problem: '', answeredAt };
    const contract = spent.contract ?? known?.contract;

    return { usage: kept.usage, people: kept.people, problem: spent.message, refused, contract, answeredAt };
  }

  /**
   * Drop every key a predicate names — the cells, and the request numbers, so an answer still in flight is dropped —
   * and tell the page when anything was held: what it shows must go with what was forgotten, or a signed-out server's
   * last company push stays on screen with nothing left to replace it.
   */
  private forget(named: (key: string) => boolean): void {
    const before = this.cells.size;
    this.cells = new Map([...this.cells].filter(([key]) => !named(key)));
    for (const key of [...this.newest.keys()].filter(named)) {
      this.newest.delete(key);
      this.settled.delete(key);
    }
    if (this.cells.size < before) {
      this.deps.changed();
    }
  }
}
