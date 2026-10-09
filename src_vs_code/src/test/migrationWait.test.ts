import assert from 'node:assert/strict';
import { test } from 'node:test';
import { Armed, MigrationWait, RETRY_AFTER_MS } from '../migrationWait';

/**
 * One retry, then one warning (todo/PLAN_catalog_migration_waits_for_its_settings.md): a window that has not
 * registered the keys the move writes gets one more try — on the next settings change or after a short timer — and is
 * told to reload when that try waits too.
 */

/** The host's three hooks, recorded: what is armed, what is live, and what was told. */
function hooks() {
  const live = new Set<string>();
  const fires: Record<string, () => void> = {};
  const told = { retries: 0, warnings: 0, timerMs: [] as number[] };
  const armed = (name: string, fire: () => void): Armed => {
    live.add(name);
    fires[name] = fire;

    return { dispose: () => live.delete(name) };
  };

  return {
    live,
    fire: (name: string) => fires[name]!(),
    told,
    hooks: {
      onSettingsChange: (fire: () => void) => armed('change', fire),
      after: (ms: number, fire: () => void) => {
        told.timerMs.push(ms);

        return armed('timer', fire);
      },
      retry: () => {
        told.retries += 1;
      },
      warn: () => {
        told.warnings += 1;
      },
    },
  };
}

test('the first wait arms one retry on a settings change and on a short timer, and warns nobody', () => {
  const h = hooks();
  new MigrationWait(h.hooks).wait();

  assert.deepEqual([...h.live].sort(), ['change', 'timer']);
  assert.deepEqual(h.told.timerMs, [RETRY_AFTER_MS]);
  assert.equal(h.told.warnings, 0);
});

for (const trigger of ['change', 'timer']) {
  test(`the ${trigger} runs the move again once and takes the other trigger down`, () => {
    const h = hooks();
    new MigrationWait(h.hooks).wait();

    h.fire(trigger);
    h.fire(trigger === 'change' ? 'timer' : 'change');

    assert.equal(h.told.retries, 1, 'the retry ran more than once, or never');
    assert.equal(h.live.size, 0, 'a trigger was left armed after the retry');
  });
}

test('a second wait in the same window warns once, retries nothing more, and leaves nothing armed', () => {
  const h = hooks();
  const wait = new MigrationWait(h.hooks);
  wait.wait();
  h.fire('change');

  wait.wait();
  wait.wait();

  assert.equal(h.told.warnings, 1, 'the person was told more than once, or never');
  assert.equal(h.told.retries, 1);
  assert.equal(h.live.size, 0);
});

test('a second wait before the retry fired still warns, and takes the armed retry down', () => {
  const h = hooks();
  const wait = new MigrationWait(h.hooks);
  wait.wait();
  wait.wait();

  assert.equal(h.told.warnings, 1);
  assert.equal(h.live.size, 0);
});

test('disposing the wait leaves nothing armed', () => {
  const h = hooks();
  const wait = new MigrationWait(h.hooks);
  wait.wait();
  wait.dispose();

  assert.equal(h.live.size, 0);
});
