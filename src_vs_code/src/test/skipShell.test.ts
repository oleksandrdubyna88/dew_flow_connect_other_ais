import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { Inspected } from '../modelKeys';
import {
  ADD,
  SKIP_SHELL_IDS,
  checkSkipShell,
  gatedByFocus,
  latchedCheck,
  offered,
  rememberedWith,
  skipShellState,
  toldAbout,
  withIds,
  type FocusSeams,
  type RepositoryLayer,
  type SkipShellSeams,
} from '../skipShell';

/**
 * The skip-shell check, over its seams (research/PLAN_the_chat_opens_from_a_terminal_E3.md, S3-T1…T8 and T12).
 *
 * <p>The road is pure: every effect — the layers of the setting, the offer, the notice, the write, the memory — is a fake that
 * records what it was asked and when. The write target (the user layer) belongs to the host and is held by `skipShellWiring.test.ts`.</p>
 */

const [ASK, GOTO] = SKIP_SHELL_IDS;
const BOTH = [...SKIP_SHELL_IDS];
const NOW = new Date('2026-11-16T08:00:00.000Z');

interface Fake {
  readonly seams: SkipShellSeams;
  readonly log: string[];
  readonly writes: (readonly string[])[];
  readonly asked: (readonly string[])[];
  readonly told: { readonly layer: RepositoryLayer; readonly ids: readonly string[] }[];
  readonly failures: string[];
  memory: unknown;
  /** The layers the next `inspect()` answers; a function is called per read, so the answer can change with the call number. */
  layers: (call: number) => Inspected | undefined;
}

/** A fake setting: `inspect` answers `layers(call)`, and `update` (unless it is made to reject) puts the value into the user layer. */
function fake(options: {
  readonly layers: (call: number) => Inspected | undefined;
  readonly answer?: string | undefined;
  readonly reject?: boolean;
  readonly applies?: boolean;
  readonly memory?: unknown;
}): Fake {
  const log: string[] = [];
  let reads = 0;
  let applied: readonly string[] | undefined;
  const state: Fake = {
    log,
    writes: [],
    asked: [],
    told: [],
    failures: [],
    memory: options.memory ?? [],
    layers: options.layers,
    seams: {
      inspect: () => {
        log.push('inspect');
        const read = state.layers(reads);
        reads += 1;

        return applied === undefined || options.applies === false ? read : { ...read, globalValue: applied };
      },
      ask: async (ids) => {
        log.push('ask');
        state.asked.push(ids);

        return options.answer;
      },
      tell: async (layer, ids) => {
        log.push('tell');
        state.told.push({ layer, ids });
      },
      update: async (next) => {
        log.push('update');
        state.writes.push(next);
        if (options.reject === true) {
          throw new Error('the settings file is read-only');
        }
        applied = next;
      },
      memory: {
        read: () => state.memory,
        write: async (next) => {
          log.push('remember');
          state.memory = next;
        },
      },
      failed: async (sentence) => {
        log.push('failed');
        state.failures.push(sentence);
      },
      now: () => NOW,
    },
  };

  return state;
}

const user = (...list: string[]): (() => Inspected) => () => ({ defaultValue: BOTH, globalValue: list });

test('S3-T1 the state over every inspect shape', () => {
  assert.deepEqual(skipShellState(undefined, BOTH), { kind: 'default', missing: [] });
  assert.deepEqual(skipShellState({ defaultValue: BOTH }, BOTH), { kind: 'default', missing: [] });
  assert.deepEqual(skipShellState({ defaultValue: ['other.x'] }, BOTH), { kind: 'default', missing: BOTH });

  assert.deepEqual(skipShellState({ defaultValue: BOTH, globalValue: ['a'] }, BOTH), { kind: 'user', list: ['a'], missing: BOTH });
  assert.deepEqual(skipShellState({ globalValue: ['a', ASK] }, BOTH), { kind: 'user', list: ['a', ASK], missing: [GOTO] });
  assert.deepEqual(skipShellState({ globalValue: BOTH }, BOTH), { kind: 'user', list: BOTH, missing: [] });
  // The person's own `-id` is their choice: kept, and never offered back.
  assert.deepEqual(skipShellState({ globalValue: [`-${ASK}`] }, BOTH), { kind: 'user', list: [`-${ASK}`], missing: [GOTO] });

  const workspace = skipShellState({ workspaceValue: ['w'], globalValue: BOTH }, BOTH);
  assert.deepEqual(workspace, { kind: 'repository', layer: 'workspace', missing: BOTH }, 'a repository\'s list was offered a write to the user settings');
  assert.deepEqual(skipShellState({ workspaceValue: ['w'] }, BOTH), { kind: 'repository', layer: 'workspace', missing: BOTH });
  assert.deepEqual(skipShellState({ workspaceFolderValue: ['f'], workspaceValue: ['w'] }, BOTH), { kind: 'repository', layer: 'folder', missing: BOTH });
  assert.deepEqual(skipShellState({ workspaceValue: BOTH, globalValue: ['a'] }, BOTH), { kind: 'repository', layer: 'workspace', missing: [] });
  assert.deepEqual(skipShellState({ workspaceValue: 'x' }, BOTH), { kind: 'repository', layer: 'workspace', missing: BOTH });

  for (const bad of ['x', [1], {}]) {
    assert.deepEqual(skipShellState({ globalValue: bad }, BOTH), { kind: 'unreadable' }, `a user value ${JSON.stringify(bad)} was read as a list`);
  }
});

test('S3-T2 the person\'s list is kept in order and the ids are added once', () => {
  const list = Object.freeze(['b', 'a', 'b']);
  const missing = Object.freeze([...BOTH]);

  assert.deepEqual(withIds(list, missing), ['b', 'a', 'b', ASK, GOTO], 'the person\'s own entries were reordered or removed');
  assert.deepEqual(withIds(['z', ASK], [ASK, GOTO]), ['z', ASK, GOTO], 'an id that was already there was added again');
  assert.deepEqual(withIds(['z'], [GOTO, ASK]), ['z', ASK, GOTO], 'the ids are not in SKIP_SHELL_IDS order');
  assert.deepEqual(withIds(['z'], [GOTO]), ['z', GOTO]);
  assert.deepEqual(list, ['b', 'a', 'b'], 'the input list was changed');
});

test('S3-T3 nothing is written before the press, and the write is after the answer', async () => {
  const closed = fake({ layers: user('x'), answer: undefined });
  assert.equal(await checkSkipShell(closed.seams), 'dismissed', 'a closed notice did not end as dismissed');
  assert.equal(closed.writes.length, 0, 'a closed notice wrote the setting');
  assert.deepEqual(offered(BOTH, closed.memory), [], 'the closed notice was not remembered as dismissed');

  const pressed = fake({ layers: user('x'), answer: ADD });
  assert.equal(await checkSkipShell(pressed.seams), 'added', 'a press of Add did not end as added');
  assert.equal(pressed.writes.length, 1, 'the press did not write exactly once');
  assert.deepEqual(pressed.writes[0], ['x', ASK, GOTO]);
  assert.ok(pressed.log.indexOf('update') > pressed.log.indexOf('ask'), 'the person\'s settings were changed before they agreed');
  assert.equal(pressed.log.indexOf('update'), pressed.log.lastIndexOf('update'));
});

test('S3-T4 the answer is remembered and the notice does not come back', async () => {
  const first = fake({ layers: user('x'), answer: undefined });
  await checkSkipShell(first.seams);
  const again = fake({ layers: user('x'), memory: first.memory, answer: ADD });
  assert.equal(await checkSkipShell(again.seams), 'nothing', 'the notice came back after the person answered');
  assert.equal(again.asked.length, 0, 'the notice came back after the person answered');

  const written = fake({ layers: user('x'), answer: ADD });
  await checkSkipShell(written.seams);
  const later = fake({ layers: user('y'), memory: written.memory, answer: ADD });
  await checkSkipShell(later.seams);
  assert.equal(later.asked.length, 0, 'the notice came back after the ids were added');

  for (const junk of ['junk', [{ id: 1 }], [{ id: ASK, answer: 'maybe', askedUtc: 'x' }], { id: ASK }]) {
    const damaged = fake({ layers: user('x'), memory: junk, answer: undefined });
    await checkSkipShell(damaged.seams);
    assert.deepEqual(damaged.asked, [BOTH], `a damaged memory ${JSON.stringify(junk)} was trusted, so nobody was asked again`);
    assert.equal(damaged.writes.length, 0);
  }

  const partial = fake({ layers: user('x'), memory: rememberedWith([], [ASK], 'dismissed', NOW), answer: undefined });
  await checkSkipShell(partial.seams);
  assert.deepEqual(partial.asked, [[GOTO]], 'a remembered answer for one id did not leave the other to be asked');
});

test('S3-T5 the write uses the list as it is at the press', async () => {
  const grew = fake({ layers: (call) => ({ globalValue: call === 0 ? ['a'] : ['a', 'c'] }), answer: ADD });
  assert.equal(await checkSkipShell(grew.seams), 'added', 'the write did not end as added');
  assert.deepEqual(grew.writes, [['a', 'c', ASK, GOTO]], 'an entry the person added while the notice was open was lost');

  const done = fake({ layers: (call) => ({ globalValue: call === 0 ? ['a'] : ['a', ...BOTH] }), answer: ADD });
  assert.equal(await checkSkipShell(done.seams), 'added');
  assert.equal(done.writes.length, 0, 'ids the person had added by hand were written again');
  assert.deepEqual(offered(BOTH, done.memory), [], 'the ids found already there were not remembered as added');

  const moved = fake({ layers: (call) => (call === 0 ? { globalValue: ['a'] } : { globalValue: ['a'], workspaceValue: ['w'] }), answer: ADD });
  assert.equal(await checkSkipShell(moved.seams), 'nothing');
  assert.equal(moved.writes.length, 0, 'a workspace value that appeared while the notice was open was written over');
});

test('S3-T6 a failed write is said and not remembered', async () => {
  const rejected = fake({ layers: user('a'), answer: ADD, reject: true });
  assert.equal(await checkSkipShell(rejected.seams), 'failed', 'a rejected write was reported as done');
  assert.equal(rejected.failures.length, 1);
  assert.ok(rejected.failures[0]?.includes('the settings file is read-only'), `the reason is not in the sentence: ${rejected.failures[0]}`);
  assert.ok(rejected.failures[0]?.includes('Nothing else in the setting was changed.'));
  assert.deepEqual(rejected.memory, [], 'a rejected write was remembered');

  const unseen = fake({ layers: user('a'), answer: ADD, applies: false });
  assert.equal(await checkSkipShell(unseen.seams), 'failed', 'a write that did not take was remembered as done');
  assert.equal(unseen.writes.length, 1);
  assert.ok(unseen.failures[0]?.includes('read back'), `the read-back sentence is missing: ${unseen.failures[0]}`);
  assert.deepEqual(unseen.memory, [], 'a write that did not take was remembered as done');
});

test('S3-T7 nothing is offered where Add cannot help or must not write', async () => {
  const shapes: readonly Inspected[] = [
    { globalValue: 'x' },
    { globalValue: [1] },
    { defaultValue: ['other.x'] },
    { defaultValue: ['other.x'], globalValue: undefined },
  ];
  for (const shape of shapes) {
    const run = fake({ layers: () => shape, answer: ADD });
    assert.equal(await checkSkipShell(run.seams), 'nothing', `${JSON.stringify(shape)} was acted on`);
    assert.deepEqual(run.log, ['inspect'], `a user list was created where the person had none: ${JSON.stringify(shape)} -> ${run.log.join(',')}`);
    assert.deepEqual(run.memory, []);
  }
  for (const layer of ['workspaceValue', 'workspaceFolderValue'] as const) {
    const run = fake({ layers: () => ({ [layer]: ['w'], globalValue: ['a'] }), answer: ADD });
    await checkSkipShell(run.seams);
    assert.equal(run.asked.length + run.writes.length, 0, `${layer}: an Add was offered or written`);
  }
});

test('S3-T8 one notification at a time', async () => {
  const base = fake({ layers: user('a'), answer: ADD });
  const seams: SkipShellSeams = {
    ...base.seams,
    // The offer stays open for a whole macrotask, so a second trigger arrives while it is shown.
    ask: async (ids) => {
      base.asked.push(ids);
      await new Promise<void>((resolve) => {
        setImmediate(resolve);
      });

      return ADD;
    },
  };
  const check = latchedCheck(seams);
  const outcomes = await Promise.all([check(), check()]);

  assert.equal(base.asked.length, 1, 'two notices were shown at once');
  assert.deepEqual([...outcomes].sort(), ['added', 'nothing']);
});

test('S3-T12 a workspace or folder list is named once, with no button, and never written', async () => {
  for (const layer of ['workspace', 'folder'] as const) {
    const value = layer === 'workspace' ? { workspaceValue: ['w'] } : { workspaceFolderValue: ['f'] };
    const run = fake({ layers: () => ({ ...value, globalValue: ['a'] }), answer: ADD });

    assert.equal(await checkSkipShell(run.seams), 'told', 'a workspace list was offered a write to the user settings');
    assert.deepEqual(run.told, [{ layer, ids: BOTH }], `the ${layer} notice was not shown once with both ids`);
    assert.equal(run.asked.length, 0, 'a workspace list was offered a write to the user settings');
    assert.equal(run.writes.length, 0, `the ${layer} list was written`);
    assert.deepEqual(toldAbout(BOTH, layer, run.memory), [], 'the told answer was not remembered with its layer');

    const second = fake({ layers: () => ({ ...value, globalValue: ['a'] }), memory: run.memory, answer: ADD });
    assert.equal(await checkSkipShell(second.seams), 'nothing');
    assert.equal(second.told.length, 0, 'the workspace notice came back');
  }

  // A `told` row of the folder does not stop a later workspace notice.
  const folder = fake({ layers: () => ({ workspaceFolderValue: ['f'] }) });
  await checkSkipShell(folder.seams);
  const workspace = fake({ layers: () => ({ workspaceValue: ['w'] }), memory: folder.memory });
  assert.equal(await checkSkipShell(workspace.seams), 'told');

  // A `told` row never stops the user offer when the workspace value is later removed and the user list lacks the ids.
  const removed = fake({ layers: user('a'), memory: workspace.memory, answer: ADD });
  assert.equal(await checkSkipShell(removed.seams), 'added');
  assert.equal(removed.asked.length, 1, 'a told row stopped the user offer');

  // A workspace list that holds both ids is not a problem.
  const fine = fake({ layers: () => ({ workspaceValue: BOTH }) });
  assert.equal(await checkSkipShell(fine.seams), 'nothing');
  assert.equal(fine.told.length, 0);
});

test('S3-T5b ids the person added by hand while the notice was open are remembered as added', async () => {
  // (a) both asked, one added by hand during the notice, the other written by the press.
  const some = fake({ layers: (call) => ({ globalValue: call === 0 ? ['a'] : ['a', ASK] }), answer: ADD });
  assert.equal(await checkSkipShell(some.seams), 'added');
  assert.deepEqual(some.writes, [['a', ASK, GOTO]], 'the id the person had added was written again, or the other was not');
  assert.deepEqual(offered(BOTH, some.memory), [], 'an id added by hand while the notice was open was not remembered, so it would be offered again');

  // (b) one id asked (the other was dismissed earlier), added by hand while the dismissed one is still missing.
  const one = fake({
    layers: (call) => ({ globalValue: call === 0 ? ['a'] : ['a', ASK] }),
    answer: ADD,
    memory: rememberedWith([], [GOTO], 'dismissed', NOW),
  });
  assert.equal(await checkSkipShell(one.seams), 'added');
  assert.deepEqual(one.asked, [[ASK]]);
  assert.equal(one.writes.length, 0, 'an id the person had added was written again');
  assert.deepEqual(offered([ASK], one.memory), [], 'an id added by hand while another was still missing was not remembered');
  assert.ok((one.memory as readonly { id: string; answer: string }[]).some((row) => row.id === ASK && row.answer === 'added'), 'the id added by hand has no added row');
});

test('S3-T4b the memory keeps one row per slot and drops the rows of ids that left the list', () => {
  const dismissed = rememberedWith([], [ASK], 'dismissed', NOW);
  const added = rememberedWith(dismissed, [ASK], 'added', NOW);
  assert.deepEqual(added.map((row) => `${row.id}:${row.answer}`), [`${ASK}:added`], 'an answer was appended beside the one it replaces');
  assert.equal(rememberedWith(added, [ASK], 'added', NOW).length, 1, 'the same answer twice made two rows');

  const old = [{ id: 'coai.gone', answer: 'dismissed', askedUtc: NOW.toISOString() }, ...added];
  assert.deepEqual(rememberedWith(old, [GOTO], 'dismissed', NOW).map((row) => row.id), [ASK, GOTO], 'a row of an id no longer in SKIP_SHELL_IDS was kept');

  // A `told` row's slot is its id AND its layer; it never replaces an added/dismissed row.
  const told = rememberedWith(rememberedWith(added, [ASK], 'told', NOW, 'workspace'), [ASK], 'told', NOW, 'folder');
  assert.equal(told.length, 3, 'a told row replaced a row of another layer or answer kind');
  assert.equal(rememberedWith(told, [ASK], 'told', NOW, 'workspace').length, 3, 'a told row for the same layer was appended instead of replacing its slot');
});

test('S3-T6b the failure sentence does not end the reason with two periods', async () => {
  const run = fake({ layers: user('a'), answer: ADD, reject: true });
  await checkSkipShell({ ...run.seams, update: async () => { throw new Error('Unable to write to User Settings because the file is dirty.'); } });
  assert.ok(run.failures[0]?.includes('the file is dirty. Nothing else'), `the sentence is not "reason. Nothing else": ${run.failures[0]}`);
  assert.ok(!run.failures[0]?.includes('..'), `the failure sentence has a double period: ${run.failures[0]}`);
});

interface FakeWindow {
  focused: boolean;
  readonly waits: (() => void)[];
  readonly seams: FocusSeams;
}

/** A window whose focus the test turns on, and whose `whenFocused` registrations it keeps so it can fire each as often as it likes. */
function fakeWindow(focused: boolean): FakeWindow {
  const win: FakeWindow = { focused, waits: [], seams: { focused: () => win.focused, whenFocused: (run) => { win.waits.push(run); } } };

  return win;
}

test('S3-T13 only a focused window asks about the skip list', async () => {
  let asks = 0;
  const gate = (win: FakeWindow) => gatedByFocus(async () => { asks += 1; return 'added' as const; }, win.seams, () => undefined);

  // Focused: it asks at once.
  const focused = fakeWindow(true);
  assert.equal(await gate(focused)(), 'added');
  assert.equal(asks, 1, 'a focused window did not check at once');

  // Unfocused: nothing until focus, then exactly one check, however many triggers and focus events there are.
  asks = 0;
  const background = fakeWindow(false);
  const run = gate(background);
  assert.equal(await run(), 'nothing', 'a window in the background asked about the skip list');
  await run();
  await run();
  assert.equal(asks, 0, 'a window in the background asked about the skip list');
  assert.equal(background.waits.length, 1, 'several triggers made several waits for the same focus');
  background.focused = true;
  background.waits[0]?.();
  background.waits[0]?.();
  await new Promise<void>((resolve) => setImmediate(resolve));
  assert.equal(asks, 1, 'two focus events made the window ask twice');

  // A later trigger while focused runs at once; a new loss of focus waits again.
  await run();
  assert.equal(asks, 2);
  background.focused = false;
  await run();
  assert.equal(asks, 2, 'a window that lost focus asked about the skip list');
  assert.equal(background.waits.length, 2);
});

test('S3-T13b the deferred check reports its failure and a later focus still runs once', async () => {
  const errors: unknown[] = [];
  const win = fakeWindow(false);
  const run = gatedByFocus(async () => { throw new Error('boom'); }, win.seams, (reason) => errors.push(reason));
  await run();
  win.focused = true;
  win.waits[0]?.();
  await new Promise<void>((resolve) => setImmediate(resolve));
  assert.equal(errors.length, 1, 'a failure of the deferred check was swallowed');
});

/** The offer, held open until the test presses a button (or closes it with `undefined`). */
function heldOffer(base: Fake): { readonly seams: SkipShellSeams; readonly press: (answer: string | undefined) => void } {
  let press: (answer: string | undefined) => void = () => undefined;
  const seams: SkipShellSeams = {
    ...base.seams,
    ask: (ids) => {
      base.asked.push(ids);

      return new Promise<string | undefined>((resolve) => {
        press = resolve;
      });
    },
  };

  return { seams, press: (answer) => press(answer) };
}

const tick = (): Promise<void> => new Promise<void>((resolve) => setImmediate(resolve));

test('S3-T14 a trigger that arrives while the offer is open is not dropped: one follow-up tells about the workspace list', async () => {
  const base = fake({ layers: user('a'), answer: ADD });
  const held = heldOffer(base);
  const check = latchedCheck(held.seams);
  const first = check();
  await tick();
  assert.equal(base.asked.length, 1, 'the offer was not open');

  // A workspace list appears while the offer is open, and its change reaches the check.
  base.layers = () => ({ globalValue: ['a'], workspaceValue: ['w'] });
  assert.equal(await check(), 'nothing', 'a trigger during a check did not return at once');
  assert.equal(base.told.length, 0, 'the follow-up ran while a notice was open');

  held.press(ADD);
  await first;
  assert.deepEqual(base.told, [{ layer: 'workspace', ids: BOTH }], 'a trigger that arrived during the check was dropped: the workspace list was never named');
  assert.equal(base.writes.length, 0, 'the workspace list was written');
  assert.equal(base.asked.length, 1, 'the follow-up asked again');
});

test('S3-T14b however many triggers arrive during one check, ONE follow-up runs', async () => {
  const base = fake({ layers: user('a'), answer: undefined });
  const held = heldOffer(base);
  const check = latchedCheck(held.seams);
  const first = check();
  await tick();
  await Promise.all([check(), check(), check()]);

  held.press(undefined);
  await first;
  await tick();
  // The check itself reads the layers once when it has nothing to ask; the first check read once, and the follow-up once.
  assert.equal(base.log.filter((step) => step === 'inspect').length, 2, 'several triggers during one check made more than one follow-up, or none');

  // A trigger after everything settled runs a check of its own.
  await check();
  assert.equal(base.log.filter((step) => step === 'inspect').length, 3);
});
