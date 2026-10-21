import type { RoundsRow } from './roundCounts';
import { isLaunchLine, type LedgerLine } from './roundMoney';

/**
 * Launches per round (E6.S3 of todo/PLAN_the_round_counter_tail_E6.md): the launch lines that carry a round key, divided by
 * the keyed rounds. Pure.
 *
 * <p>A launch is whatever {@link isLaunchLine} says — never a second allow-list here — of THIS row's vendor (one round key is
 * in every vendor row of a multi-vendor round, and each row counts its own vendor's launches), of a round in the row's
 * `roundKeys`, and from the store the answer's `keyStore` attributes that key to. Membership in the keys IS the window's cut
 * (the round started inside it), so there is no second time comparison.</p>
 *
 * <p>The denominator is the keyed rounds. A keyed round none of whose lines is a launch is reported apart; a round with no key
 * (written before coai-mcp 0.60.2, or skipped) cannot be tied to a line at all and is reported apart, differently — only
 * THOSE are blamed on the version.</p>
 */

/** What a row says about its rounds: whose, which keys, how many have none, and who owns each key. */
export type LaunchRow = Pick<RoundsRow, 'provider' | 'roundKeys' | 'roundsWithoutKey' | 'keyStore'>;

/** The fields of a ledger line a launch is judged by, with the round and the store it was read from. */
export type LaunchLine = Pick<LedgerLine, 'attempt' | 'cli' | 'provider' | 'round' | 'store'>;

export interface LaunchFigure {
  /** Launch lines of this vendor's keyed rounds. */
  readonly launches: number;
  /** The keyed rounds — the denominator. */
  readonly rounds: number;
  /** Launches over rounds; nothing when there is no keyed round to divide by. */
  readonly perRound: number | undefined;
  /** Keyed rounds that no launch line names. */
  readonly keyedWithoutLaunch: number;
  /** Rounds counted whose key is empty. */
  readonly withoutKey: number;
}

export function launchesPerRound(row: LaunchRow, lines: readonly LaunchLine[]): LaunchFigure {
  const keys = new Set(row.roundKeys);
  const launches = lines.filter((line) => isLaunchOf(row, keys, line));
  const withLaunch = new Set(launches.map((line) => line.round));

  return {
    launches: launches.length,
    rounds: keys.size,
    perRound: keys.size === 0 ? undefined : launches.length / keys.size,
    keyedWithoutLaunch: row.roundKeys.filter((key) => !withLaunch.has(key)).length,
    withoutKey: row.roundsWithoutKey,
  };
}

function isLaunchOf(row: LaunchRow, keys: ReadonlySet<string>, line: LaunchLine): boolean {
  return launchOf(row, line) && keys.has(line.round ?? '') && fromOwner(row, line);
}

/** One launch of this row's vendor — `isLaunchLine`'s answer, never a second allow-list. */
function launchOf(row: LaunchRow, line: LaunchLine): boolean {
  return isLaunchLine(line) && line.provider === row.provider;
}

/** A key the answer attributes to a store counts only from that store's ledger; a key with no owner counts wherever it was read. */
function fromOwner(row: LaunchRow, line: LaunchLine): boolean {
  const owner = row.keyStore?.[line.round ?? ''];

  return owner === undefined || owner === line.store;
}

/**
 * The sentence that says which rounds the figure does not cover — empty when it covers them all. Only a round with NO key is
 * blamed on coai-mcp before 0.60.2; a keyed round without a launch line is a round whose lines were not found.
 */
export function launchFact(figure: LaunchFigure): string {
  const parts = [
    figure.keyedWithoutLaunch > 0 ? `${roundsWord(figure.keyedWithoutLaunch)} ${carries(figure.keyedWithoutLaunch)} no launch count` : '',
    figure.withoutKey > 0 ? `${roundsWord(figure.withoutKey)} written before coai-mcp 0.60.2 ${carries(figure.withoutKey)} no round key` : '',
  ];

  return parts.filter((part) => part.length > 0).join('; ');
}

/** *1 round* / *2 rounds*. */
function roundsWord(count: number): string {
  return `${count} round${count === 1 ? '' : 's'}`;
}

function carries(count: number): string {
  return count === 1 ? 'carries' : 'carry';
}
