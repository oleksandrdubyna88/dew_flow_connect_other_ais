import { withoutControls } from './remoteAccountsReply';
import type { AccountsMode } from './remoteAccountsScript';
import type { DoorAnswer } from './remotePermission';
import { MAX_DISTROS, MAX_PLACES, WslPlace, wslNames } from './remotePlaces';
import { USERS_UNREADABLE_SAID, WslUser, usersOf as readUsers } from './remoteUsersScript';
import { WslRun, isStrike } from './remoteWslRun';

/**
 * The WSL part of a round, as a pure function over seams (E3.S1, spec § W5 and § W11; contract K2). The order, each step awaited:
 * the list, then the running list; for each distro of the first 16, in the list's order: not running → no call of any kind; the users call; for each
 * user, while fewer than 32 WSL places were taken, the accounts call — and before EVERY `-d` call the running list is read again.
 *
 * <p><b>The door is not asked here</b>: it is asked inside the edge's one spawn function (`remoteWsl.ts`), so no order of this file can spawn without
 * it. A call that the door refused comes back as a `refused` run with its answer, and ends the WSL part with no further call.</p>
 *
 * <p>The round's own checks (the generation, the Stop, the terminal) are `hooks.stillOn`, asked before each call. What a place's result is — its
 * door again, its publication, its memory — is the caller's: `visit` gets each place with its `call`, which makes the re-read and the spawn.</p>
 */

/** What the round needs of the edge: the three calls, each never rejecting. */
export interface WslPort {
  readonly list: (running: boolean) => Promise<WslRun>;
  readonly users: (distro: string) => Promise<WslRun>;
  readonly accounts: (place: WslPlace, mode: AccountsMode, digest: string | undefined) => Promise<WslRun>;
}

/** What a distro is, in the words of § W13. `read`: its users were read; the others say why not. */
export type DistroState = 'read' | 'not-running' | 'left' | 'users-failed' | 'strikes';

export interface DistroFact {
  readonly distro: string;
  readonly state: DistroState;
  /** The sentence of a state that has one (S6 holds the call's own sentence); `''` for `read`. */
  readonly said: string;
  readonly users: number;
}

/** The scope host `wsl`'s own facts of the last round: the distros, the counts past a cap, a failure, the strikes. */
export interface WslFacts {
  readonly distros: readonly DistroFact[];
  /** Distros of the list past the cap of 16. */
  readonly moreDistros: number;
  /** Users not read: past the 32 places of the round, and the users the script itself left out past its 16. */
  readonly moreUsers: number;
  /** Lines of the lists that were not understood. */
  readonly ignored: number;
  /** A failed list call: S3; `''` when the list was read. */
  readonly failed: string;
  readonly strikes: number;
  /** How the WSL part ended: all done, a list that failed, two strikes, the door's refusal, or the round's own checks. */
  readonly ended: 'complete' | 'strikes' | 'failed' | 'door' | 'stopped';
}

export const EMPTY_FACTS: WslFacts = { distros: [], moreDistros: 0, moreUsers: 0, ignored: 0, failed: '', strikes: 0, ended: 'complete' };

// ───────────────── the words (§ W13) ─────────────────

export const NOT_RUNNING_SAID = 'not running, so not read';
export const LEFT_SAID = 'stopped since the round began, so not read';
export const STRIKES_SAID = 'not read in this round: WSL did not answer twice.';

const listFailedSaid = (how: string, running: boolean): string => `The list of ${running ? 'running ' : ''}WSL distros could not be read: ${how}.`;

const refusedHow = (run: Extract<WslRun, { readonly kind: 'refused' }>): string =>
  (run.end === 'timeout' ? 'WSL did not answer within 30 s' : run.end === 'not-started' ? 'wsl.exe could not be started' : run.refusal);

const exitHow = (code: number): string => (code === 0 ? 'its output could not be decoded' : `wsl.exe ended with exit ${code}`);

/** The words of S3 for each way a list call did not give a list. */
const listHow = (run: WslRun): string => (run.kind === 'refused' ? refusedHow(run) : exitHow(run.code));

export const usersFailedSaid = (distro: string, how: string): string => `The Linux users of “${withoutControls(distro)}” could not be read: ${how}.`;

// ───────────────── what a list call came to ─────────────────

type NamesListing = { readonly kind: 'names'; readonly names: readonly string[]; readonly ignored: number };
/** `said` is the whole sentence (S3, S2 or S1) the scope host shows. */
type Listing = NamesListing | { readonly kind: 'failed'; readonly said: string } | { readonly kind: 'door'; readonly door: DoorAnswer };

const failed = (how: string, running: boolean): Listing => ({ kind: 'failed', said: listFailedSaid(how, running) });

/** The refusals whose own sentence is the whole story: `wsl.exe` is not there (S2), or this is not a Windows window (S1). */
const WHOLE: ReadonlySet<string> = new Set(['missing', 'platform']);

/** The door's answer of a refused run that was the door's, else nothing. */
export const doorOf = (run: WslRun): DoorAnswer | undefined => (run.kind === 'refused' && run.end === 'door' ? run.door : undefined);

const refusedListing = (run: Extract<WslRun, { readonly kind: 'refused' }>, running: boolean): Listing => {
  const door = doorOf(run);

  if (WHOLE.has(run.end)) {
    return { kind: 'failed', said: run.refusal };
  }

  return door === undefined ? failed(listHow(run), running) : { kind: 'door', door };
};

/** An answered list call: exit 0 and a decodable text. Another exit, or a text that does not decode, is a list that could not be read — for the running list too: it is NEVER "none running". */
function answeredListing(run: Extract<WslRun, { readonly kind: 'answered' }>, running: boolean): Listing {
  const got = run.code === 0 ? wslNames(run.stdout) : 'undecodable';

  return got === 'undecodable' ? failed(exitHow(run.code), running) : { kind: 'names', names: got.names, ignored: got.ignored };
}

const listingOf = (run: WslRun, running: boolean): Listing => (run.kind === 'refused' ? refusedListing(run, running) : answeredListing(run, running));

// ───────────────── what a place's call came to ─────────────────

/** What `visit` is given to make a place's call. `left`: the distro was not in the running list when it was read again, so no call was made. */
export type PlaceRun = WslRun | { readonly kind: 'left' };

export type PlaceCall = (mode: AccountsMode, digest: string | undefined) => Promise<PlaceRun>;

/** What a visit tells the walk: go on, or end the WSL part (the round's own checks said so, or the door did). */
export interface Visited<S> {
  readonly state: S;
  readonly end?: 'stopped' | { readonly door: DoorAnswer } | undefined;
}

export type Visit<S> = (state: S, place: WslPlace, call: PlaceCall) => Promise<Visited<S>>;

export interface WslHooks {
  /** The generation, the Stop and the terminal, from the round: false ends the WSL part with no further call. */
  readonly stillOn: () => Promise<boolean>;
  /** The scope host's facts changed. */
  readonly facts: (facts: WslFacts) => void;
}

type SeenDistro = { readonly running: boolean; readonly users: readonly string[] | undefined };

/** What the walk knows of the world at its end, for the memory's retirement: the list, and which distros and users were seen. */
export interface WslSeen {
  readonly listed: boolean;
  readonly distros: ReadonlyMap<string, SeenDistro>;
}

type Ended = 'complete' | 'stopped' | { readonly door: DoorAnswer };

export interface WslWalk<S> {
  readonly state: S;
  readonly facts: WslFacts;
  readonly seen: WslSeen;
  readonly end: Ended;
}

type UsersResult =
  | { readonly kind: 'users'; readonly users: readonly WslUser[]; readonly more: number }
  | { readonly kind: 'fault'; readonly how: string }
  | { readonly kind: 'door'; readonly door: DoorAnswer };

function usersResult(run: WslRun): UsersResult {
  const door = doorOf(run);
  if (door !== undefined) {
    return { kind: 'door', door };
  }
  if (run.kind === 'refused') {
    return { kind: 'fault', how: run.refusal };
  }
  const read = readUsers(run);

  return read.kind === 'unreadable' ? { kind: 'fault', how: USERS_UNREADABLE_SAID[read.why] } : { kind: 'users', users: read.users, more: read.more };
}

/** What the running list said again: the distro is in it, it is gone, or the WSL part cannot go on (the door, or a list that failed). */
type Reread = 'in' | 'left' | 'stop';

const strikesRun = (said: string): WslRun => ({ kind: 'refused', refusal: said, end: 'exit' });

/** One walk. Mutable inside, because it is one sequence of awaited steps; what it returns is a value. */
class Walk<S> {
  private facts: WslFacts = EMPTY_FACTS;
  private readonly distros = new Map<string, SeenDistro>();
  private strikes = 0;
  private listFailed = '';
  private taken = 0;
  private names: readonly string[] = [];
  private listed = false;
  private last: Ended | undefined;
  private readonly left = new Set<string>();

  constructor(private readonly port: WslPort, private readonly hooks: WslHooks) {}

  private set(change: Partial<WslFacts>): void {
    this.facts = { ...this.facts, ...change };
    this.hooks.facts(this.facts);
  }

  private setDistro(fact: DistroFact): void {
    const rest = this.facts.distros.filter((one) => one.distro !== fact.distro);
    this.set({ distros: [...rest, fact].sort((a, b) => this.names.indexOf(a.distro) - this.names.indexOf(b.distro)) });
  }

  /** Whether the round's own checks still allow a call. */
  private async on(): Promise<boolean> {
    if (this.last === undefined && (await this.hooks.stillOn())) {
      return true;
    }
    this.last = this.last ?? 'stopped';

    return false;
  }

  /** The names of a list call, or nothing — with the door's answer or the failure kept for the end. */
  private accept(listing: Listing): NamesListing | undefined {
    if (listing.kind === 'door') {
      this.last = { door: listing.door };
    } else if (listing.kind === 'failed') {
      this.listFailed = listing.said;
      this.set({ failed: this.listFailed, ended: 'failed' });
    }

    return listing.kind === 'names' ? listing : undefined;
  }

  private take(listed: NamesListing): void {
    this.names = listed.names.slice(0, MAX_DISTROS);
    this.set({ ignored: listed.ignored, moreDistros: Math.max(0, listed.names.length - MAX_DISTROS) });
  }

  private known(running: NamesListing): ReadonlySet<string> {
    this.listed = true;
    const set = new Set(running.names.filter((name) => this.names.includes(name)));
    this.names.forEach((name) => this.distros.set(name, { running: set.has(name), users: undefined }));

    return set;
  }

  /** The list, then the running list. `undefined` when the WSL part cannot go on. */
  private async enumerate(): Promise<ReadonlySet<string> | undefined> {
    const listed = this.accept(listingOf(await this.port.list(false), false));
    if (listed === undefined) {
      return undefined;
    }
    this.take(listed);
    const running = (await this.on()) ? this.accept(listingOf(await this.port.list(true), true)) : undefined;

    return running === undefined ? undefined : this.known(running);
  }

  private gone(distro: string): Reread {
    this.left.add(distro);
    this.distros.set(distro, { running: false, users: undefined });
    this.setDistro({ distro, state: 'left', said: LEFT_SAID, users: 0 });

    return 'left';
  }

  /** Before every `-d` call: the running list again. A distro that is not in it is `left`, with no further call or re-read for it. */
  private async reread(distro: string): Promise<Reread> {
    if (this.left.has(distro)) {
      return 'left';
    }
    const running = this.accept(listingOf(await this.port.list(true), true));
    if (running === undefined) {
      return 'stop';
    }

    return running.names.includes(distro) ? 'in' : this.gone(distro);
  }

  /** A refused run for a call that was not made: the door's own answer, else the failed list's sentence. */
  private stopRun(): WslRun {
    const last = this.last;

    return typeof last === 'object' ? { kind: 'refused', refusal: 'door', end: 'door', door: last.door } : strikesRun(this.listFailed);
  }

  private blocked(): WslRun | undefined {
    if (this.strikes >= 2) {
      return strikesRun(STRIKES_SAID);
    }

    return this.listFailed === '' ? undefined : strikesRun(this.listFailed);
  }

  private count(run: WslRun): void {
    if (isStrike(run)) {
      this.strikes += 1;
      this.set({ strikes: this.strikes });
    }
  }

  private noted(run: WslRun): WslRun {
    this.count(run);
    const door = doorOf(run);
    this.last = door === undefined ? this.last : { door };

    return run;
  }

  private async placeCall(place: WslPlace, mode: AccountsMode, digest: string | undefined): Promise<PlaceRun> {
    const blocked = this.blocked();
    if (blocked !== undefined) {
      return blocked;
    }
    const again = await this.reread(place.distro);
    if (again !== 'in') {
      return again === 'left' ? { kind: 'left' } : this.stopRun();
    }

    return this.noted(await this.port.accounts(place, mode, digest));
  }

  private callOf(place: WslPlace): PlaceCall {
    return (mode, digest) => this.placeCall(place, mode, digest);
  }

  private applyUsers(distro: string, got: UsersResult): readonly WslUser[] | undefined {
    if (got.kind === 'door') {
      this.last = { door: got.door };
    } else if (got.kind === 'fault') {
      this.setDistro({ distro, state: 'users-failed', said: usersFailedSaid(distro, got.how), users: 0 });
    } else {
      this.set({ moreUsers: this.facts.moreUsers + got.more });
    }

    return got.kind === 'users' ? got.users : undefined;
  }

  /** The users of one running distro; `undefined` when the call or its reply failed (the distro's state says so, or the part ended). */
  private async usersOf(distro: string): Promise<readonly WslUser[] | undefined> {
    if ((await this.reread(distro)) !== 'in') {
      return undefined;
    }
    const run = await this.port.users(distro);
    this.count(run);

    return this.applyUsers(distro, usersResult(run));
  }

  /** A distro that is not read at all: not running, or two strikes already. */
  private skipFact(distro: string, running: ReadonlySet<string>): DistroFact | undefined {
    if (!running.has(distro)) {
      return { distro, state: 'not-running', said: NOT_RUNNING_SAID, users: 0 };
    }

    return this.strikes >= 2 ? { distro, state: 'strikes', said: STRIKES_SAID, users: 0 } : undefined;
  }

  private async distro(state: S, distro: string, running: ReadonlySet<string>, visit: Visit<S>): Promise<S> {
    const skip = this.skipFact(distro, running);
    if (skip !== undefined) {
      this.setDistro(skip);

      return state;
    }
    const users = (await this.on()) ? await this.usersOf(distro) : undefined;
    if (users === undefined) {
      return state;
    }
    this.distros.set(distro, { running: true, users: users.map((one) => one.name) });
    this.setDistro({ distro, state: 'read', said: '', users: users.length });

    return this.visitUsers(state, distro, users, visit);
  }

  private async visitOne(state: S, distro: string, user: WslUser, visit: Visit<S>): Promise<{ readonly state: S; readonly stop: boolean }> {
    if (this.left.has(distro) || !(await this.on())) {
      return { state, stop: true };
    }
    this.taken += 1;
    const place: WslPlace = { kind: 'wsl', distro, user: user.name, uid: user.uid };
    const visited = await visit(state, place, this.callOf(place));
    this.last = visited.end ?? this.last;

    return { state: visited.state, stop: this.last !== undefined };
  }

  private async visitUsers(state: S, distro: string, users: readonly WslUser[], visit: Visit<S>): Promise<S> {
    const used = users.slice(0, Math.max(0, MAX_PLACES - this.taken));
    this.set({ moreUsers: this.facts.moreUsers + users.length - used.length });
    let now = state;
    for (const user of used) {
      const step = await this.visitOne(now, distro, user, visit);
      now = step.state;
      if (step.stop) {
        break;
      }
    }

    return now;
  }

  private async eachDistro(state: S, running: ReadonlySet<string>, visit: Visit<S>): Promise<S> {
    let now = state;
    for (const distro of this.names) {
      if (this.last !== undefined) {
        break;
      }
      now = await this.distro(now, distro, running, visit);
    }

    return now;
  }

  private endedWord(end: Ended): WslFacts['ended'] {
    if (typeof end !== 'string') {
      return 'door';
    }

    return end === 'stopped' ? 'stopped' : this.softEnd();
  }

  private softEnd(): WslFacts['ended'] {
    return this.listFailed !== '' ? 'failed' : this.strikes >= 2 ? 'strikes' : 'complete';
  }

  private finish(state: S): WslWalk<S> {
    const end = this.last ?? 'complete';
    this.set({ ended: this.endedWord(end) });

    return { state, facts: this.facts, seen: { listed: this.listed, distros: new Map(this.distros) }, end };
  }

  async run(state: S, visit: Visit<S>): Promise<WslWalk<S>> {
    const running = (await this.on()) ? await this.enumerate() : undefined;

    return this.finish(running === undefined ? state : await this.eachDistro(state, running, visit));
  }
}

/**
 * The WSL part of one round. `visit` is called for each place that is to be read, in order, with the `call` that makes its re-read and its spawn; it
 * publishes the place's result and returns the state it made. `state` is threaded through, as the round threads its own.
 */
export function wslRound<S>(port: WslPort, hooks: WslHooks, state: S, visit: Visit<S>): Promise<WslWalk<S>> {
  return new Walk<S>(port, hooks).run(state, visit);
}

const userIn = (users: readonly string[] | undefined, user: string): boolean => users === undefined || users.includes(user);

const keptDistro = (one: SeenDistro | undefined, user: string): boolean => one !== undefined && one.running && userIn(one.users, user);

/**
 * Whether a memory entry of a WSL place is kept after a walk: a list that was not read keeps everything; a distro that the list no longer names, or that
 * is not running, keeps nothing; a distro whose users were read keeps only the users the reply named. (Retirement, E3.S3.)
 */
export const retainedAfter = (seen: WslSeen, distro: string, user: string): boolean => !seen.listed || keptDistro(seen.distros.get(distro), user);
