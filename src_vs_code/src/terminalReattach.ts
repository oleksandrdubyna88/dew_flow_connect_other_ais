import type { ChatPanels } from './chatPanels';
import type { ConversationSource } from './chatStore';
import { threadUnder } from './chatThread';
import { credsSession } from './credsSession';
import type { KnownPanel } from './sessionKey';
import type { LiveTerminal } from './terminalSource';

/**
 * Re-attach a conversation to its terminal after a reload — the decision, with no `vscode` in it
 * (todo/PLAN_the_chat_opens_from_a_terminal_E3.md, S1, contract C1).
 *
 * <p>A reload gives a restored chat tab a key of its own, and creds brings the terminal back as a NEW object. Nothing joins the
 * two, so the next press of the door would start a second conversation beside the restored one. This module joins them — but
 * only when nothing is in doubt: a name that passes `credsSession`, exactly ONE live terminal and exactly ONE orphaned
 * conversation of that exact name, a terminal that holds no conversation yet, and the same answer twice in a row.</p>
 *
 * <p><b>It never picks, and it never orders by arrival.</b> The triggers see the window one event at a time, so a name can be
 * one-to-one at one event and not at the next. A bind this module makes is therefore PROVISIONAL until the person first uses
 * the conversation from its terminal; while it is, its conversation counts among the conversations of the name, and when the
 * name stops being one terminal and one conversation the answer is `undo`. A bind that was used is final.</p>
 *
 * <p>The marks that say which terminal holds a provisional bind, and the registry that is re-keyed, are the host's
 * (`terminalHost.ts`); every look at them arrives here as a value, and every act leaves as a seam.</p>
 */

/** An open panel whose durable source is `terminal` and whose key is not a terminal of the window; `name` is the source's full name. */
export interface Orphan {
  readonly key: object;
  readonly name: string;
}

/**
 * What the registry holds under a terminal's key: nothing; the conversation re-attach bound there that the person has not used from this
 * terminal yet (`provisional`); or any other conversation (`final` — the door or go-to made it, a provisional bind was used,
 * or the marked conversation was closed and another took the terminal).
 */
export type Holds = 'nothing' | 'provisional' | 'final';

/** A terminal of the window and what it holds. */
export interface HeldTerminal extends LiveTerminal {
  readonly holds: Holds;
}

/** Why the answer is "leave it as it is". */
export type Leave = 'not creds' | 'no terminal' | 'several terminals' | 'held' | 'no orphan' | 'several orphans' | 'bound';

/** What to do for a name: bind an orphan to its terminal, undo a provisional bind, or leave things. */
export type Reattach =
  | { readonly kind: 'bind'; readonly from: object; readonly to: object }
  | { readonly kind: 'undo'; readonly from: object }
  | { readonly kind: 'leave'; readonly why: Leave };

const leave = (why: Leave): Reattach => ({ kind: 'leave', why });

/** The live (not exited) terminals of exactly this name. Names are compared exactly, as `oneLiveNamed` and `sameSource` do. */
const liveNamed = (liveNames: readonly HeldTerminal[], name: string): readonly HeldTerminal[] =>
  liveNames.filter((one) => !one.exited && one.name === name);

/**
 * A provisional bind that the name no longer supports: its terminal is not the only one of the name, or another conversation
 * of the name is open. A provisional bind is made only when both are one, so there is at most one — and its own conversation is
 * the one conversation, which is why a second one is any orphan of the name.
 */
function undone(terminals: readonly HeldTerminal[], orphans: readonly Orphan[]): Reattach | undefined {
  const marked = terminals.find((one) => one.holds === 'provisional');
  if (marked === undefined) {
    return undefined;
  }

  return terminals.length === 1 && orphans.length === 0 ? undefined : { kind: 'undo', from: marked.key };
}

/** The one terminal already holds a conversation: a final one is `held`, a provisional one (its own conversation is the only one) is `bound`. */
function heldOrBound(only: HeldTerminal): Reattach | undefined {
  if (only.holds === 'nothing') {
    return undefined;
  }

  return leave(only.holds === 'final' ? 'held' : 'bound');
}

/** The one terminal holds nothing: bind the ONE orphan of the name to it. */
function choose(only: HeldTerminal, orphans: readonly Orphan[]): Reattach {
  if (orphans.length !== 1) {
    return leave(orphans.length === 0 ? 'no orphan' : 'several orphans');
  }

  return { kind: 'bind', from: orphans[0]!.key, to: only.key };
}

function decide(terminals: readonly HeldTerminal[], orphans: readonly Orphan[]): Reattach {
  if (terminals.length !== 1) {
    return leave(terminals.length === 0 ? 'no terminal' : 'several terminals');
  }
  const only = terminals[0]!;

  return heldOrBound(only) ?? choose(only, orphans);
}

/**
 * What to do for this terminal name.
 *
 * <p>Decided in this order: a name that does not pass `credsSession` is `not creds`; a provisional bind that the name no longer
 * supports is `undo`; then no terminal, several terminals, a terminal that already holds a conversation (`held`, or `bound` for
 * the provisional one), no orphan, several orphans, and only then `bind`.</p>
 *
 * @param orphans the open terminal conversations whose terminal is not in the window
 * @param liveNames every terminal of the window, with what the registry holds under it
 * @param name the terminal's full name
 */
export function reattachPlan(orphans: readonly Orphan[], liveNames: readonly HeldTerminal[], name: string): Reattach {
  if (credsSession(name).kind !== 'session') {
    return leave('not creds');
  }
  const terminals = liveNamed(liveNames, name);
  const mine = orphans.filter((one) => one.name === name);

  return undone(terminals, mine) ?? decide(terminals, mine);
}

/**
 * The open panels that are terminal conversations with no terminal in the window.
 *
 * <p>Judged by the conversation's DURABLE source, as `tabSourced` is — never by the key's type and never by the label: a Claude
 * tab's or a file's conversation is never an orphan, whatever it is called. A terminal that has exited but is still in the window keeps
 * its conversation, so `windowKeys` is every terminal of the window, live or not.</p>
 */
export function orphansOf(
  known: readonly KnownPanel[],
  sourceOf: (key: object) => ConversationSource | undefined,
  windowKeys: readonly object[],
): readonly Orphan[] {
  const sourced = known.map((panel) => ({ key: panel.key, source: sourceOf(panel.key) }));

  return sourced.flatMap((panel) =>
    (panel.source?.kind === 'terminal' && !windowKeys.includes(panel.key) ? [{ key: panel.key, name: panel.source.name }] : []));
}

/** What a look at the window shows: the orphans and every terminal with what it holds. */
export interface ReattachView {
  readonly orphans: readonly Orphan[];
  readonly liveNames: readonly HeldTerminal[];
}

/** What acting on a plan does to the registry and the marks. */
export interface ReattachActs {
  /** Move the conversation under `from` onto `to`; false when it did not (the destination is taken or the source is gone). */
  readonly rekey: (from: object, to: object) => boolean;
  /** The conversation now under this terminal is a provisional bind. */
  readonly mark: (terminal: object) => void;
  /** This terminal's provisional mark goes. */
  readonly unmark: (terminal: object) => void;
}

/** Said when a re-key was refused or the second look disagreed. */
const HELD: Reattach = leave('held');

/** One look and one plan. A name that is not a strict `Creds:` name reads nothing at all. */
function look(read: () => ReattachView, name: string): Reattach {
  if (credsSession(name).kind !== 'session') {
    return leave('not creds');
  }
  const view = read();

  return reattachPlan(view.orphans, view.liveNames, name);
}

/** What an act is about, to compare two plans: its kind and its keys. */
const keysOf = (plan: Reattach): readonly (object | string)[] =>
  (plan.kind === 'bind' ? [plan.kind, plan.from, plan.to] : plan.kind === 'undo' ? [plan.kind, plan.from] : [plan.kind]);

const sameAct = (first: Reattach, second: Reattach): boolean => {
  const one = keysOf(first);
  const other = keysOf(second);

  return one.length === other.length && one.every((key, at) => key === other[at]);
};

/** Do the act: re-key (a bind onto the terminal, an undo onto a fresh key of its own), then the mark. A refused re-key is `leave` `held`. */
function actOn(plan: Exclude<Reattach, { kind: 'leave' }>, act: ReattachActs): Reattach {
  if (!act.rekey(plan.from, plan.kind === 'bind' ? plan.to : {})) {
    return HELD;
  }
  if (plan.kind === 'bind') {
    act.mark(plan.to);
  } else {
    act.unmark(plan.from);
  }

  return plan;
}

/**
 * The one road: read, plan; on `bind` or `undo` read AGAIN and plan again, and act only when the second plan is the same kind
 * with the same keys. The reads happen with no `await` between the second one and the act, so nothing can change in between.
 *
 * <p>A second look that disagrees changes nothing and answers its own `leave` — or, when it is another act, `held`: the
 * next trigger decides, and this one never acts on a plan that has already moved. It answers what was done, `bind` or `undo`
 * only when the re-key answered true.</p>
 */
export function reattachOnce(read: () => ReattachView, act: ReattachActs, name: string): Reattach {
  const first = look(read, name);
  if (first.kind === 'leave') {
    return first;
  }
  const second = look(read, name);
  if (!sameAct(first, second)) {
    return second.kind === 'leave' ? second : HELD;
  }

  return actOn(first, act);
}

/**
 * The provisional marks (C2): terminal key → the entry id of the conversation re-attach bound there. In memory, one per bound
 * terminal at most, dropped with the terminal. A terminal is `provisional` only while the registry's conversation under it still
 * has that id, so closing the conversation needs no hook, and a conversation that took its place reads as `final`.
 */
export type Marks = WeakMap<object, object>;

/** The registry, the marks and the window's terminals: everything a look and an act need, with the window as a seam. */
export interface ReattachWindow {
  readonly panels: Pick<ChatPanels, 'known' | 'get' | 'rekey'>;
  readonly marks: Marks;
  /** The window's terminals, read afresh on every call (the host reads `window.terminals` each time). */
  readonly terminals: () => readonly LiveTerminal[];
}

/** What the registry holds under this terminal's key. */
function holdsOf(win: ReattachWindow, terminal: object): Holds {
  const held = win.panels.get(terminal);
  if (held === undefined) {
    return 'nothing';
  }

  return win.marks.get(terminal) === held.id ? 'provisional' : 'final';
}

/** One look at the window: the orphans, and every terminal of the window with what the registry holds under it. */
export function viewOf(win: ReattachWindow): ReattachView {
  const terminals = win.terminals();

  return {
    orphans: orphansOf(win.panels.known(), (key) => threadUnder(win.panels, key)?.source, terminals.map((one) => one.key)),
    liveNames: terminals.map((one) => ({ ...one, holds: holdsOf(win, one.key) })),
  };
}

/** What re-attach does to the registry and the marks. `mark` reads the id of the conversation `rekey` just moved there. */
export const actsOf = (win: ReattachWindow): ReattachActs => ({
  rekey: (from, to) => win.panels.rekey(from, to),
  mark: (terminal) => {
    const held = win.panels.get(terminal);
    if (held !== undefined) {
      win.marks.set(terminal, held.id);
    }
  },
  unmark: (terminal) => {
    win.marks.delete(terminal);
  },
});

/** Re-attach the conversation of the terminal called `name`, when nothing is in doubt: the window is read twice and the re-key follows the second read. */
export const reattachIn = (win: ReattachWindow, name: string): Reattach => reattachOnce(() => viewOf(win), actsOf(win), name);

const answersBound = (plan: Reattach): boolean => plan.kind === 'leave' && plan.why === 'bound';

/**
 * The person used a conversation from its terminal: its bind is final — but ONLY when the plan for the terminal's name, from a fresh
 * view, answers `bound` and THIS terminal is the provisional one. Any other answer changes nothing and the next trigger decides:
 * a use of an ambiguous bind is not a confirmation. True when the mark was removed.
 */
export function confirmBound(win: ReattachWindow, terminal: object, name: string): boolean {
  const view = viewOf(win);
  const mine = view.liveNames.find((one) => one.key === terminal);
  const confirmed = answersBound(reattachPlan(view.orphans, view.liveNames, name)) && mine?.holds === 'provisional';
  if (confirmed) {
    win.marks.delete(terminal);
  }

  return confirmed;
}

/** The answers that are about ambiguity, and so worth a line for a person who asks why a chat did not re-attach. */
const AMBIGUOUS: readonly Leave[] = ['several terminals', 'several orphans'];

/** The reason to say for an answer — an undo, or an ambiguity — or nothing for the quiet answers. */
export function reasonOf(answer: Reattach): string | undefined {
  if (answer.kind === 'undo') {
    return 'its provisional bind was undone: the name is no longer one terminal and one conversation';
  }

  return answer.kind === 'leave' && AMBIGUOUS.includes(answer.why) ? `not re-attached: ${answer.why} carry that name` : undefined;
}
