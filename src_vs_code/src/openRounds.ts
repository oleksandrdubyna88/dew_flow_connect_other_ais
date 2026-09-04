import { isRunning, RoundRecord } from './rounds';
import { roundKey } from './panelView';

/**
 * Which round cards are expanded, and who decided.
 *
 * <p><b>Running is open, finished is closed, and what the person opened is never touched.</b> Three
 * sets rather than one, because "open" alone cannot answer the question that decides everything
 * here — WHO opened it.</p>
 *
 * <p>Pure, and in its own file, because the policy used to live inside the provider next to
 * `vscode` and was therefore untestable: the previous rule shipped on nothing but its own comment
 * saying it was right. It was not.</p>
 */
export interface OpenRounds {
  /** What renders as `<details open>`. */
  readonly open: readonly string[];
  /** Every round this panel has ever opened by itself — so closing one is not undone next tick. */
  readonly autoOpened: readonly string[];
  /** Still open on the panel's initiative ALONE. Emptied for a card the person touches. */
  readonly panelOpened: readonly string[];
}

export const NOTHING_OPEN: OpenRounds = { open: [], autoOpened: [], panelOpened: [] };

/** A round card, as the rounds list knows it: the record plus the branch it belongs to. */
export type Card = RoundRecord & { readonly branch: string };

/**
 * The next state, given what the sessions say right now.
 *
 * <p>Order matters and is the whole of the behaviour: a round that has STARTED is opened once; a
 * round the panel opened and that has now STOPPED is closed again; everything else is left exactly
 * as it was. A round the person opened is not in <c>panelOpened</c>, so the second step cannot
 * reach it.</p>
 */
export function nextOpenRounds(previous: OpenRounds, cards: readonly Card[]): OpenRounds {
  const running = new Set(cards.filter(isRunning).map(roundKey));
  const alive = new Set(cards.map(roundKey));
  const autoOpened = new Set(previous.autoOpened);
  const panelOpened = new Set(previous.panelOpened);
  const open = new Set(previous.open);

  // Started: opened once. "Once" is what stops the next five-second tick from re-opening a round
  // the person has just closed while it runs.
  for (const key of running) {
    if (!autoOpened.has(key)) {
      autoOpened.add(key);
      panelOpened.add(key);
      open.add(key);
    }
  }

  // Stopped, and still ours: closed again. The previous rule kept it open "because that is when its
  // reviewers are worth reading" — true of one round and wrong of a list, which became a wall of
  // expanded cards. Overruled by the person who uses it.
  for (const key of [...panelOpened]) {
    if (!running.has(key)) {
      panelOpened.delete(key);
      open.delete(key);
    }
  }

  // All three are pruned to rounds that still EXIST: none is large in a day's work, but each would
  // otherwise grow for the life of the extension host and be scanned on every tick. (codex.)
  return {
    open: [...open].filter((key) => alive.has(key)),
    autoOpened: [...autoOpened].filter((key) => alive.has(key)),
    panelOpened: [...panelOpened].filter((key) => alive.has(key)),
  };
}

/**
 * The person clicked a card. It is theirs now — open or closed alike.
 *
 * <p>Dropping the key from <c>panelOpened</c> is the whole of "do not touch what I opened": the
 * auto-collapse only ever closes cards the panel still owns.</p>
 */
export function afterToggle(previous: OpenRounds, key: string, open: boolean): OpenRounds {
  return {
    open: open ? [...new Set([...previous.open, key])] : previous.open.filter((k) => k !== key),
    // It stays in `autoOpened`: that set answers "has this ever been opened for them", and a person
    // closing a running round must not have it opened again on the next tick.
    autoOpened: previous.autoOpened,
    panelOpened: previous.panelOpened.filter((k) => k !== key),
  };
}
