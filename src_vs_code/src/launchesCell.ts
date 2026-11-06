import { escapeHtml } from './escapeHtml';
import { launchesPerRound, launchFact, type LaunchFigure, type LaunchLine, type LaunchRow } from './launchesPerRound';
import { type RoundCountsAnswer, rowIn, WINDOW_LABEL, WINDOW_NAMES, type WindowName } from './roundCounts';
import { LAUNCHES_PER_ROUND } from './roundMoney';

/**
 * The Rounds block's *launches per round* cell (E6.S3, D9): the 7 d figure in the cell, the other windows and the fact about the
 * rounds it does not cover in its tooltip. Pure — {@link launchesPerRound} does the counting, this words it.
 *
 * <p>A row with no round that has a launch line in a window has no figure there: a dash, never a division by nothing and never a
 * 0 drawn from rounds nobody could count (the tooltip's fact says how many keyed rounds carry no launch count).</p>
 */

/** The cell for a row that runs on a Team server: its launch lines are `cli: remote` and never launches, so a number would always be 0. */
export function remoteLaunchesCell(server: string): string {
  return `<span title="launches run on ${escapeHtml(server)}, not recorded here">—</span>`;
}

/** The cell for one local row, from the counted answer and the lines that count (store-resolved, owner-filtered). */
export function launchesCell(answer: RoundCountsAnswer, lines: readonly LaunchLine[], provider: string): string {
  const figures = WINDOW_NAMES.map((name) => [name, launchesPerRound(rowOf(answer, name, provider), lines)] as const);
  const seven = figures.find(([name]) => name === '7d')![1];
  const title = `${figures.map(([name, figure]) => windowWords(name, figure)).join(' · ')} ${LAUNCHES_PER_ROUND}${factWords(seven)}`;

  return `<span title="${escapeHtml(title)}">${figureText(seven)}</span>`;
}

/** The row's keys in a window — a row the answer does not carry ran no round there. */
function rowOf(answer: RoundCountsAnswer, name: WindowName, provider: string): LaunchRow {
  return rowIn(answer, name, provider) ?? { provider, roundKeys: [], roundsWithoutKey: 0 };
}

function figureText(figure: LaunchFigure): string {
  return figure.perRound === undefined ? '—' : figure.perRound.toFixed(1);
}

/** `5 h: 2.0 (2 launches over 1 round)`. */
function windowWords(name: WindowName, figure: LaunchFigure): string {
  return `${WINDOW_LABEL[name]}: ${figureText(figure)}${figure.rounds === 0 ? '' : ` (${count(figure.launches, 'launch', 'launches')} over ${count(figure.rounds, 'round', 'rounds')})`}`;
}

function count(n: number, one: string, many: string): string {
  return `${n} ${n === 1 ? one : many}`;
}

function factWords(figure: LaunchFigure): string {
  const fact = launchFact(figure);

  return fact.length === 0 ? '' : `. ${fact}`;
}
