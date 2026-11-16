import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ChatEntry, ChatPanels } from '../chatPanels';
import { sourceOfFile, sourceOfTerminal, type ConversationSource } from '../chatStore';
import { Thread, threads } from '../chatThread';
import {
  type HeldTerminal,
  type Holds,
  type Marks,
  type Orphan,
  type Reattach,
  type ReattachActs,
  type ReattachView,
  type ReattachWindow,
  confirmBound,
  orphansOf,
  reasonOf,
  reattachIn,
  reattachOnce,
  reattachPlan,
  viewOf,
} from '../terminalReattach';
import { type LiveTerminal, terminalMatch } from '../terminalSource';

/**
 * Re-attach of a terminal's conversation after a reload (todo/PLAN_the_chat_opens_from_a_terminal_E3.md, S1): the rule, the second look
 * before the re-key, the provisional bind and its undo.
 *
 * <p>Pure, like the module: no `vscode`. A `Terminal` is any object here — the key is the object, never its name. The orchestration
 * tests (S1-T10 and S1-T13…T18) run over a REAL `ChatPanels`, the real marks and a window whose terminals the test opens and
 * closes; what stands in for the editor is only the list of terminals.</p>
 */

const NAME = 'Creds: h / s';

/** A terminal of the window and what it holds. The key is a fresh object: identity is the point. */
const held = (name: string, holds: Holds = 'nothing', exited = false): HeldTerminal => ({ key: { terminal: name }, name, exited, holds });

const orphan = (name: string): Orphan => ({ key: { orphan: name }, name });

/** The plan, with the answer a test names spelled out in its failure. */
const leaves = (answer: Reattach, why: string, message: string): void => {
  assert.deepEqual(answer, { kind: 'leave', why }, message);
};

test('S1-T1: one live terminal and one orphan of the name bind', () => {
  const one = orphan(NAME);
  const terminal = held(NAME);

  assert.deepEqual(reattachPlan([one], [terminal], NAME), { kind: 'bind', from: one.key, to: terminal.key }, 'a restored conversation did not find its one terminal');
});

test('S1-T2: two live terminals of the name never bind; one of them exited binds to the live one', () => {
  const one = orphan(NAME);
  const first = held(NAME);
  const second = held(NAME);

  leaves(reattachPlan([one], [first, second], NAME), 'several terminals', 'two terminals of one name and one was taken');
  const gone = held(NAME, 'nothing', true);

  assert.deepEqual(reattachPlan([one], [gone, second], NAME), { kind: 'bind', from: one.key, to: second.key }, 'an exited terminal was counted as a terminal of the name');
});

test('S1-T3: two orphans of the name never bind', () => {
  leaves(reattachPlan([orphan(NAME), orphan(NAME)], [held(NAME)], NAME), 'several orphans', 'two conversations of one name and one was bound');
});

test('S1-T4: a terminal that holds a conversation is never given another', () => {
  const full = held(NAME, 'final');

  leaves(reattachPlan([orphan(NAME)], [full], NAME), 'held', 'a terminal that has a conversation was given a second');
  leaves(reattachPlan([orphan(NAME), orphan(NAME)], [full], NAME), 'held', 'a terminal that has a conversation was given a second, with two orphans waiting');
});

test('S1-T5: only a strict Creds: name binds, and nothing is read for any other', () => {
  const names = ['bash', 'Creds run: x', 'Creds VPN: x', 'creds: h / s', 'Creds: h / s.x', 'Creds: -h / s', 'Creds: before x', 'Creds: h / s '];
  for (const name of names) {
    let reads = 0;
    const read = (): ReattachView => {
      reads += 1;

      return { orphans: [orphan(name)], liveNames: [held(name)] };
    };

    leaves(reattachPlan([orphan(name)], [held(name)], name), 'not creds', `a ${JSON.stringify(name)} terminal was re-attached by its name`);
    leaves(reattachOnce(read, { rekey: () => true, mark() {}, unmark() {} }, name), 'not creds', `a ${JSON.stringify(name)} terminal was re-attached by its name, through the one road`);
    assert.equal(reads, 0, `the terminals were read for ${JSON.stringify(name)}`);
  }
});

test('S1-T6: names are compared exactly', () => {
  for (const other of ['Creds: h / S', 'Creds: h / s2', 'Creds: h / ', 'Creds: h2 / s']) {
    leaves(reattachPlan([orphan(NAME)], [held(other)], NAME), 'no terminal', `a terminal of another name (${other}) took the conversation`);
    leaves(reattachPlan([orphan(other)], [held(NAME)], NAME), 'no orphan', `a conversation of another name (${other}) was bound`);
  }
});

test('S1-T7: an orphan is a terminal conversation with no terminal of the window', () => {
  const [orphanKey, liveKey, exitedKey, claude, file, none, noThread] = [{}, {}, {}, {}, {}, {}, {}];
  const sources = new Map<object, ConversationSource>([
    [orphanKey, sourceOfTerminal('Creds: h / s')],
    [liveKey, sourceOfTerminal('Creds: h / t')],
    [exitedKey, sourceOfTerminal('Creds: h / u')],
    [claude, { kind: 'claude', sessionId: 'abc' }],
    [file, sourceOfFile('file:///a/b.md')],
    [none, { kind: 'none' }],
  ]);
  const known = [orphanKey, liveKey, exitedKey, claude, file, none, noThread].map((key) => ({ key, label: 'h / s' }));

  assert.deepEqual(
    orphansOf(known, (key) => sources.get(key), [liveKey, exitedKey]),
    [{ key: orphanKey, name: 'Creds: h / s' }],
    'a Claude tab’s conversation was offered to a terminal, or a terminal of the window was taken for an orphan',
  );
});

/** A read seam that answers the views in order, and then the last one again. */
const reads = (...views: readonly ReattachView[]): (() => ReattachView) => {
  let at = 0;

  return () => views[Math.min(at++, views.length - 1)]!;
};

/** The acts, recording what was done. */
const recorder = (rekeyed = true): { readonly acts: ReattachActs; readonly done: string[]; readonly calls: unknown[][] } => {
  const done: string[] = [];
  const calls: unknown[][] = [];
  const acts: ReattachActs = {
    rekey: (from, to) => {
      done.push('rekey');
      calls.push([from, to]);

      return rekeyed;
    },
    mark: () => { done.push('mark'); },
    unmark: () => { done.push('unmark'); },
  };

  return { acts, done, calls };
};

test('S1-T8: the plan is checked again before the re-key', () => {
  const o = orphan(NAME);
  const t = held(NAME);
  const view = (orphans: readonly Orphan[], liveNames: readonly HeldTerminal[]): ReattachView => ({ orphans, liveNames });
  const A = view([o], [t]);
  const changed: readonly [string, ReattachView, string][] = [
    ['a second terminal of the name', view([o], [t, held(NAME)]), 'several terminals'],
    ['the orphan removed', view([], [t]), 'no orphan'],
    ['the terminal now holds a conversation', view([o], [{ ...t, holds: 'final' }]), 'held'],
    ['the terminal renamed', view([o], [{ ...t, name: 'Creds: h / other' }]), 'no terminal'],
  ];
  for (const [what, B, why] of changed) {
    const { acts, done } = recorder();

    leaves(reattachOnce(reads(A, B), acts, NAME), why, `the plan was acted on without a second look (${what})`);
    assert.deepEqual(done, [], `a re-key or a mark was made although the second look changed (${what})`);
  }
  // Another act on the second look is not acted on either.
  const other = recorder();
  const elsewhere = held(NAME);

  leaves(reattachOnce(reads(A, view([o], [elsewhere])), other.acts, NAME), 'held', 'the first plan was acted on although the second named another terminal');
  assert.deepEqual(other.done, []);

  // The same look twice: ONE re-key, ONE mark.
  const same = recorder();

  assert.deepEqual(reattachOnce(reads(A, A), same.acts, NAME), { kind: 'bind', from: o.key, to: t.key });
  assert.deepEqual(same.done, ['rekey', 'mark']);
  assert.deepEqual(same.calls, [[o.key, t.key]], 'the re-key moved something other than the orphan onto the terminal');
});

test('S1-T8: the same two looks govern an undo', () => {
  const provisional = held(NAME, 'provisional');
  const second = held(NAME);
  const lonely: ReattachView = { orphans: [], liveNames: [provisional, second] };
  const calm: ReattachView = { orphans: [], liveNames: [provisional] };

  // The name was ambiguous at the first look and one-to-one at the second: nothing is undone.
  const calmed = recorder();

  leaves(reattachOnce(reads(lonely, calm), calmed.acts, NAME), 'bound', 'a provisional bind was undone on a plan the second look no longer held');
  assert.deepEqual(calmed.done, [], 'an undo was made without a second look that agreed');
  // Ambiguous twice: the conversation goes to a key of its own, then the mark goes.
  const undone = recorder();
  const answer = reattachOnce(reads(lonely, lonely), undone.acts, NAME);

  assert.deepEqual(answer, { kind: 'undo', from: provisional.key });
  assert.deepEqual(undone.done, ['rekey', 'unmark']);
  assert.equal(undone.calls[0]![0], provisional.key);
  assert.notEqual(undone.calls[0]![1], provisional.key, 'the undo re-keyed the conversation onto the terminal again');
});

test('S1-T9: a refused re-key is not reported as a bind, and a real registry keeps both entries', () => {
  const world = newWorld();
  const a = world.orphan(NAME);
  const t = world.terminal(NAME);
  const b = world.put(t, sourceOfTerminal(NAME));
  // A view that lies: the terminal is said to hold nothing, though the registry holds B under it.
  const view: ReattachView = { orphans: [{ key: a.key, name: NAME }], liveNames: [{ key: t, name: NAME, exited: false, holds: 'nothing' }] };
  const { acts, done } = recorder();
  const real: ReattachActs = { ...acts, rekey: (from, to) => world.panels.rekey(from, to) };

  leaves(reattachOnce(reads(view), real, NAME), 'held', 'a refused re-key was reported as done');
  assert.equal(world.panels.get(a.key)?.id, a.id, 'the orphan left its key although the re-key was refused');
  assert.equal(world.panels.get(t)?.id, b.id, 'the conversation of the terminal was replaced by a refused re-key');
  assert.deepEqual(done, [], 'a refused re-key was marked as a bind');
});

/** A window the test opens and closes terminals in, over a REAL registry and the real marks. */
interface World {
  readonly panels: ChatPanels;
  readonly marks: Marks;
  readonly win: ReattachWindow;
  readonly list: LiveTerminal[];
  /** An open conversation keyed by a plain object, filed under this terminal name — what a reload leaves. */
  readonly orphan: (name: string) => { readonly key: object; readonly id: object };
  /** A terminal appears in the window. */
  readonly terminal: (name: string) => object;
  /** A conversation under this key. */
  readonly put: (key: object, source: ConversationSource) => { readonly id: object };
  readonly close: (terminal: object) => void;
  readonly disposed: object[];
}

function newWorld(): World {
  const panels = new ChatPanels();
  const marks: Marks = new WeakMap();
  const list: LiveTerminal[] = [];
  const disposed: object[] = [];
  const put = (key: object, source: ConversationSource): { readonly id: object } => {
    const id = {};
    // A thread with only the field the rules read (`source`): if they ever read another, it throws here.
    threads.set(id, { source } as unknown as Thread);
    const entry = (): ChatEntry => ({ id, panel: { reveal() {}, dispose() {}, post() {}, isActive: () => false }, session: { dispose() { disposed.push(id); } } });
    panels.open(key, 'label', entry);

    return { id };
  };

  return {
    panels,
    marks,
    list,
    disposed,
    win: { panels, marks, terminals: () => [...list] },
    orphan: (name) => {
      const key = {};

      return { key, id: put(key, sourceOfTerminal(name)).id };
    },
    terminal: (name) => {
      const key = { terminal: name };
      list.push({ key, name, exited: false });

      return key;
    },
    put,
    close: (terminal) => {
      list.splice(list.findIndex((one) => one.key === terminal), 1);
    },
  };
}

const holdsOn = (world: World, terminal: object): Holds | undefined => viewOf(world.win).liveNames.find((one) => one.key === terminal)?.holds;

/** The door, as the host does it: re-attach, the match, the confirmation on `existing`, a new conversation on `new`. */
function door(world: World, terminal: object, name: string): { readonly kind: string; readonly leave: Reattach } {
  const answer = reattachIn(world.win, name);
  const match = terminalMatch(world.panels.known(), terminal, name);
  if (match.kind === 'existing') {
    confirmBound(world.win, terminal, name);
  } else {
    world.put(terminal, sourceOfTerminal(name));
  }

  return { kind: match.kind, leave: answer };
}

test('S1-T10: the race, in both orders, over a real registry', () => {
  // (a) the orphan was registered first, then the terminal opens.
  const a = newWorld();
  const first = a.orphan(NAME);
  const terminalA = a.terminal(NAME);

  assert.equal(reattachIn(a.win, NAME).kind, 'bind', 'a terminal that opened after its chat was restored did not find it');
  assert.equal(a.panels.get(terminalA)?.id, first.id, 'a terminal that opened after its chat was restored did not get it');
  assert.equal(a.panels.get(first.key), undefined, 'the conversation sits under two keys');

  // (b) the terminal opened first (its trigger finds no orphan), then the restore trigger for that record's name.
  const b = newWorld();
  const terminalB = b.terminal(NAME);

  leaves(reattachIn(b.win, NAME), 'no orphan', 'a terminal bound a conversation that did not exist');
  const second = b.orphan(NAME);

  assert.equal(reattachIn(b.win, NAME).kind, 'bind', 'a terminal that opened before its chat was restored stayed apart');
  assert.equal(b.panels.get(terminalB)?.id, second.id, 'a terminal that opened before its chat was restored stayed apart');

  // (c) a second terminal of the name is already open: never bound, in either order of the two triggers.
  for (const order of ['restore first', 'terminal first'] as const) {
    const c = newWorld();
    const [x, y] = [c.terminal(NAME), c.terminal(NAME)];
    const third = c.orphan(NAME);
    const answers = order === 'restore first' ? [reattachIn(c.win, NAME), reattachIn(c.win, NAME)] : [reattachIn(c.win, NAME)];

    for (const answer of answers) {
      leaves(answer, 'several terminals', `a conversation was bound with two terminals of its name (${order})`);
    }
    assert.equal(c.panels.get(x), undefined);
    assert.equal(c.panels.get(y), undefined);
    assert.ok(c.panels.get(third.key) !== undefined, 'the conversation left its own key');
  }
});

test('S1-T11 companion: the console line is said for the ambiguous answers and the undo only', () => {
  const said = (answer: Reattach): string | undefined => reasonOf(answer);

  assert.match(said({ kind: 'leave', why: 'several terminals' }) ?? '', /several terminals/u);
  assert.match(said({ kind: 'leave', why: 'several orphans' }) ?? '', /several orphans/u);
  assert.match(said({ kind: 'undo', from: {} }) ?? '', /undone/u);
  for (const why of ['not creds', 'no terminal', 'held', 'no orphan', 'bound'] as const) {
    assert.equal(said({ kind: 'leave', why }), undefined, `“${why}” is housekeeping and was said`);
  }
  assert.equal(said({ kind: 'bind', from: {}, to: {} }), undefined);
});

test('S1-T13: case A — a second terminal after the bind undoes it, and arrival order never decides', () => {
  const world = newWorld();
  const a = world.orphan(NAME);
  const t1 = world.terminal(NAME);

  assert.equal(reattachIn(world.win, NAME).kind, 'bind');
  assert.equal(world.panels.get(t1)?.id, a.id);
  assert.equal(holdsOn(world, t1), 'provisional');
  const t2 = world.terminal(NAME);

  assert.equal(reattachIn(world.win, NAME).kind, 'undo', 'a conversation stayed on the first of two terminals of its name');
  assert.equal(world.panels.get(t1), undefined);
  assert.equal(world.panels.get(t2), undefined);
  assert.equal(holdsOn(world, t1), 'nothing');
  assert.equal(holdsOn(world, t2), 'nothing');
  assert.deepEqual(world.disposed, [], 'the undo ended the conversation: its panel must stay open');
  const own = world.panels.known().find((one) => world.panels.get(one.key)?.id === a.id);

  assert.ok(own !== undefined && own.key !== t1 && own.key !== t2, 'the conversation is not on a key of its own');
  // The door in T1 with T2 still open: several terminals, a NEW conversation for T1 (E1's behaviour).
  const pressed = door(world, t1, NAME);

  leaves(pressed.leave, 'several terminals', 'the door re-attached with two terminals of the name');
  assert.equal(pressed.kind, 'new');
  assert.notEqual(world.panels.get(t1)?.id, a.id);
  assert.ok(world.panels.get(own!.key) !== undefined, 'the restored conversation was lost');
});

test('S1-T13: case A — when T2 closes first, the door binds and confirms', () => {
  const world = newWorld();
  const a = world.orphan(NAME);
  const t1 = world.terminal(NAME);
  reattachIn(world.win, NAME);
  const t2 = world.terminal(NAME);
  reattachIn(world.win, NAME);
  world.close(t2);
  const pressed = door(world, t1, NAME);

  assert.equal(pressed.leave.kind, 'bind');
  assert.equal(pressed.kind, 'existing', 'the door started a second conversation instead of using the restored one');
  assert.equal(world.panels.get(t1)?.id, a.id);
  assert.equal(holdsOn(world, t1), 'final', 'the press inside the restore window did not confirm the conversation that arrived first');
});

test('S1-T14: case B — a second conversation after the bind undoes it', () => {
  const world = newWorld();
  const t = world.terminal(NAME);
  const a = world.orphan(NAME);

  assert.equal(reattachIn(world.win, NAME).kind, 'bind');
  assert.equal(world.panels.get(t)?.id, a.id);
  const b = world.orphan(NAME);

  assert.equal(reattachIn(world.win, NAME).kind, 'undo', 'the first of two restored conversations kept the terminal');
  assert.equal(world.panels.get(t), undefined);
  assert.equal(holdsOn(world, t), 'nothing');
  for (const id of [a.id, b.id]) {
    const own = world.panels.known().find((one) => world.panels.get(one.key)?.id === id);

    assert.ok(own !== undefined && own.key !== t, 'a conversation is not on a key of its own');
  }
  const pressed = door(world, t, NAME);

  leaves(pressed.leave, 'several orphans', 'the door bound one of two conversations of its name');
  assert.equal(pressed.kind, 'new');
});

test('S1-T15: a used bind is final — a namesake that opens later does not undo it, after the door and after take', () => {
  for (const use of ['the door', 'take'] as const) {
    const world = newWorld();
    const a = world.orphan(NAME);
    const t1 = world.terminal(NAME);
    reattachIn(world.win, NAME);
    if (use === 'the door') {
      assert.equal(door(world, t1, NAME).kind, 'existing');
    } else {
      // *take* and *add* call the same two functions on `existing`.
      assert.equal(terminalMatch(world.panels.known(), t1, NAME).kind, 'existing');
      assert.equal(confirmBound(world.win, t1, NAME), true);
    }
    const t2 = world.terminal(NAME);

    leaves(reattachIn(world.win, NAME), 'several terminals', `a conversation the person used was taken off its terminal (after ${use})`);
    assert.equal(world.panels.get(t1)?.id, a.id);
    assert.equal(world.panels.get(t2), undefined);
  }
});

test('S1-T16: the provisional state in the pure plan', () => {
  const o = orphan(NAME);
  const one = held(NAME, 'provisional');

  assert.deepEqual(reattachPlan([], [one, held(NAME)], NAME), { kind: 'undo', from: one.key }, 'a provisional bind survived a second terminal of its name');
  assert.deepEqual(reattachPlan([o], [one], NAME), { kind: 'undo', from: one.key }, 'a provisional bind survived a second conversation of its name');
  leaves(reattachPlan([], [one], NAME), 'bound', 'a lone provisional bind was not left alone');
  leaves(reattachPlan([o], [held(NAME, 'final')], NAME), 'held', 'a final bind was undone');
  leaves(reattachPlan([], [one, held(NAME, 'nothing', true)], NAME), 'bound', 'an exited terminal counted as a second terminal');
  // An orphan of ANOTHER name is not a second conversation of this one.
  leaves(reattachPlan([orphan('Creds: h / other')], [one], NAME), 'bound', 'a conversation of another name undid the bind');
});

test('S1-T17: a closed provisional conversation leaves no stale mark', () => {
  const world = newWorld();
  const a = world.orphan(NAME);
  const t = world.terminal(NAME);
  reattachIn(world.win, NAME);

  assert.equal(world.panels.close(t), true);
  assert.deepEqual(world.disposed, [a.id]);
  const pressed = door(world, t, NAME);

  assert.equal(pressed.kind, 'new', 'the door found the closed conversation');
  const n = world.panels.get(t);

  assert.ok(n !== undefined && n.id !== a.id);
  const t2 = world.terminal(NAME);

  leaves(reattachIn(world.win, NAME), 'several terminals', 'a new conversation on a terminal was undone as if re-attach had made it');
  assert.equal(world.panels.get(t)?.id, n.id);
  assert.equal(holdsOn(world, t), 'final');
  assert.equal(holdsOn(world, t2), 'nothing');
});

test('S1-T18: a use confirms only while the plan answers bound', () => {
  const world = newWorld();
  const a = world.orphan(NAME);
  const t = world.terminal(NAME);
  reattachIn(world.win, NAME);
  // A second conversation of the name reopened from the picker: keyed {}, and no trigger runs.
  const b = world.orphan(NAME);

  // Go-to from T shows A and confirms: the plan answers undo, not bound.
  assert.equal(confirmBound(world.win, t, NAME), false, 'an ambiguous bind was made final by a use');
  assert.equal(holdsOn(world, t), 'provisional');
  assert.equal(reattachIn(world.win, NAME).kind, 'undo', 'the next trigger did not undo the ambiguous bind');
  assert.equal(world.panels.get(t), undefined);
  assert.ok(world.panels.known().length === 2 && b.id !== a.id);

  // With no second conversation the same call removes the mark.
  const calm = newWorld();
  const only = calm.orphan(NAME);
  const terminal = calm.terminal(NAME);
  reattachIn(calm.win, NAME);

  assert.equal(confirmBound(calm.win, terminal, NAME), true);
  assert.equal(holdsOn(calm, terminal), 'final');
  assert.equal(calm.panels.get(terminal)?.id, only.id);
  // A terminal that is not the provisional one is never confirmed by the name's plan.
  assert.equal(confirmBound(calm.win, {}, NAME), false);
});
