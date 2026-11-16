import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { Inspected } from '../modelKeys';
import {
  ADD,
  SKIP_SHELL_IDS,
  checkSkipShell,
  latchedCheck,
  offered,
  rememberedWith,
  skipShellState,
  toldAbout,
  withIds,
  type RepositoryLayer,
  type SkipShellSeams,
} from '../skipShell';

/**
 * The skip-shell check, over its seams (todo/PLAN_the_chat_opens_from_a_terminal_E3.md, S3-T1…T8 and T12).
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
