import type { HostStanding } from './remoteHosts';

/**
 * The timer of the accounts round, as decisions (E1.S3, E2.S4), moved out of `remoteAccountsRound.ts` (E3.S3: that file reached its line limit) with no change of a
 * rule. `remoteAccountsRound.ts` re-exports all of this, so no importer moves. Pure.
 */

/** From the END of a round to the start of the next (O4). */
export const ROUND_GAP_MS = 600_000;
/** After activation, or after the first eligible terminal opened: the window restores its terminals first. */
export const FIRST_DELAY_MS = 30_000;
/** *Refresh now* and opening the list start no round less than this after the last one ended. */
export const REFRESH_FLOOR_MS = 60_000;
export const HOURLY_MS = 3_600_000;

export type ArmDecision = 'arm-first' | 'clear' | 'keep';

const idle = (input: { readonly armed: boolean; readonly running: boolean }): boolean => !input.armed && !input.running;

/**
 * What the timer does when a terminal opens or closes or the window starts. `eligible`: a live `Creds:` terminal of a host that is not
 * refused and is called (never `wsl`). `permitted`: the permission is in force; with none, no timer is armed. Whether a host is CONTACTED is the
 * door's answer alone.
 */
export function armFor(input: { readonly eligible: boolean; readonly permitted: boolean; readonly armed: boolean; readonly stopped: boolean; readonly running: boolean }): ArmDecision {
  if (!armable(input)) {
    return 'clear';
  }

  return idle(input) ? 'arm-first' : 'keep';
}

/** A timer may run only for an eligible terminal, with the permission in force and the round not stopped. */
const armable = (input: { readonly eligible: boolean; readonly permitted: boolean; readonly stopped: boolean }): boolean => input.eligible && input.permitted && !input.stopped;

const refusedIn = (standings: readonly HostStanding[], host: string): boolean => standings.some((one) => one.host === host && one.answer === 'no');

/**
 * One round, and what follows it, in a window. A round that failed — a seam rejected — is logged once through `failed`, and the timer is still set
 * (`schedule` is given nothing): rounds never stop silently. A `schedule` that fails too falls back to `fallback`, which sets the plain gap.
 */
export async function settleRound<T>(work: () => Promise<T>, schedule: (done: T | undefined) => Promise<void>, fallback: () => void, failed: () => void): Promise<T | undefined> {
  let done: T | undefined;
  try {
    done = await work();
  } catch {
    failed();
  }
  try {
    await schedule(done);
  } catch {
    failed();
    fallback();
  }

  return done;
}

/** The hosts the list says "no" to. */
export const refusedHosts = (standings: readonly HostStanding[]): ReadonlySet<string> => new Set(standings.filter((one) => one.answer === 'no').map((one) => one.host));

/**
 * Whether the list's change signal shows a host that is NEWLY refused, or a standing that is gone (a Forget): a result in flight is then to be dropped,
 * so the host bumps its generation. A new row — the automatic "yes" this very round wrote — bumps nothing.
 */
export function standingsBump(known: ReadonlySet<string>, refused: ReadonlySet<string>, standings: readonly HostStanding[]): boolean {
  const forgotten = [...known].some((host) => !standings.some((one) => one.host === host));

  return forgotten || standings.some((one) => one.answer === 'no' && !refused.has(one.host));
}

/** The hosts of live terminals whose standing is not a "no". A host with no row is eligible: the round allows it by itself. */
export const eligibleOf = (hosts: readonly string[], standings: readonly HostStanding[]): readonly string[] => hosts.filter((host) => !refusedIn(standings, host));

export type NextRound = { readonly kind: 'at'; readonly atMs: number } | { readonly kind: 'clear' };

export type TimerAction = { readonly kind: 'none' } | { readonly kind: 'clear' } | { readonly kind: 'start'; readonly ms: number };

const afterRound = (next: NextRound, nowMs: number): TimerAction => (next.kind === 'at' ? { kind: 'start', ms: Math.max(0, next.atMs - nowMs) } : { kind: 'clear' });

const ARMED: Readonly<Record<ArmDecision, TimerAction>> = { 'arm-first': { kind: 'start', ms: FIRST_DELAY_MS }, clear: { kind: 'clear' }, keep: { kind: 'none' } };

/**
 * The first test-mode lock, and the ONE road from a decision to a timer: in a test run no timer is ever started, whatever `armFor` or
 * `nextAfterRound` answered. A decision after a round is an absolute time; the wait is counted from `nowMs`.
 */
export function timerFor(decision: ArmDecision | NextRound, test: boolean, nowMs = 0): TimerAction {
  return test ? { kind: 'none' } : actionOf(decision, nowMs);
}

/** What a read of the permission means for the window, given what it saw before. The first read of a window (`undefined`) is no edge. */
export type PermissionEdge = 'allowed' | 'withdrawn' | 'same';

export function permissionEdge(was: boolean | undefined, now: boolean): PermissionEdge {
  if (was === undefined || was === now) {
    return 'same';
  }

  return now ? 'allowed' : 'withdrawn';
}

/** An *Allow* starts a round at once — unless one runs, the round is stopped, or no terminal is eligible. */
export const startsAfterAllow = (input: { readonly running: boolean; readonly stopped: boolean; readonly eligible: boolean }): boolean =>
  !input.running && !input.stopped && input.eligible;

const actionOf = (decision: ArmDecision | NextRound, nowMs: number): TimerAction => (typeof decision === 'string' ? ARMED[decision] : afterRound(decision, nowMs));

/** After a round: the next one `ROUND_GAP_MS` from the END of this one, or no timer (a Stop, no eligible terminal, or no permission). */
export const nextAfterRound = (input: { readonly stopped: boolean; readonly eligible: boolean; readonly permitted: boolean; readonly endedMs: number }): NextRound =>
  (input.stopped || !input.eligible || !input.permitted ? { kind: 'clear' } : { kind: 'at', atMs: input.endedMs + ROUND_GAP_MS });

const insideFloor = (input: { readonly lastEndMs: number | undefined; readonly now: number }): boolean => input.lastEndMs !== undefined && input.now - input.lastEndMs < REFRESH_FLOOR_MS;

/** *Refresh now*: a round at once, unless one is running or the last one ended less than a minute ago. */
export function refreshAnswer(input: { readonly running: boolean; readonly lastEndMs: number | undefined; readonly now: number }): 'start' | 'busy' | 'floor' {
  if (input.running) {
    return 'busy';
  }

  return insideFloor(input) ? 'floor' : 'start';
}

/** Opening the list starts a round only when a host that is not refused has nothing yet, and not inside the floor. */
export const openStarts = (input: { readonly running: boolean; readonly lastEndMs: number | undefined; readonly now: number; readonly unreadHost: boolean }): boolean =>
  input.unreadHost && refreshAnswer(input) === 'start';
