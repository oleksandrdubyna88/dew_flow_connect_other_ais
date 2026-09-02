import assert from 'node:assert/strict';
import { test } from 'node:test';
import { RUNTIMES, Runtime } from '../models';
import { vendorsFrom, VENDOR_PRESETS } from '../vendors';

/**
 * A runtime this build knows must survive being read back from settings.
 *
 * <p><b>What went wrong.</b> `Runtime` gained `'local'` and the `RUNTIMES` array that
 * `vendorsFrom` validates against did not. An unknown runtime is deliberately rewritten to
 * `codex` — it is the one that takes a base URL, so it is the safest guess — which meant every
 * saved local reviewer came back as a CODEX reviewer. The row kept its name, so the panel showed
 * `local` while listing GPT-5.6 and offering codex's run and install buttons, and a round would
 * have been sent through the Codex CLI: the exact thing the local runtime exists to avoid, and the
 * thing measured as costing 21k tokens of someone else's system prompt before any review.</p>
 *
 * <p>The comment above that line already warned about this in so many words — "every runtime this
 * build KNOWS must be listed in RUNTIMES" — which is the argument against fixing it with a longer
 * comment. `Runtime` is now DERIVED from `RUNTIMES`, so the two cannot disagree; these tests cover
 * what the type cannot: that a saved vendor survives the round trip.</p>
 */

/**
 * The one runtime that deliberately does not survive, named here so the exception is a decision
 * rather than a gap: Google retired Code Assist for individual accounts, so a saved `gemini`
 * reviewer with no base URL is MIGRATED to `antigravity` — `migrateRetired`, which has its own
 * tests. Every other runtime must come back as itself.
 */
const MIGRATED = 'gemini';

test('every runtime this build knows survives being read back', () => {
  for (const runtime of RUNTIMES.filter((r) => r !== MIGRATED)) {
    const [vendor] = vendorsFrom([{ id: 'r', runtime, model: 'm', enabled: true }]);

    assert.equal(
      vendor.runtime,
      runtime,
      `a saved ${runtime} reviewer came back as ${vendor.runtime} — it would run the wrong vendor`,
    );
  }
});

test('the retired runtime is migrated, not silently rewritten to codex', () => {
  // The distinction that matters: `gemini` changes runtime for a stated reason and keeps its id,
  // while `local` was changing runtime for no reason at all. One is a migration, the other was a
  // defect, and they looked identical from outside until this test separated them.
  const [vendor] = vendorsFrom([{ id: 'gemini', runtime: 'gemini', enabled: true }]);

  assert.equal(vendor.runtime, 'antigravity');
  assert.equal(vendor.id, 'gemini', 'the id names the row, its history and its vault key');
});

test('every preset the panel offers survives being saved and read back', () => {
  // The path a person actually takes: pick a preset from "+ Add reviewer", it is written to
  // settings, and the next read is what the panel and the server both act on.
  for (const preset of VENDOR_PRESETS.filter((p) => p.runtime !== MIGRATED)) {
    const [vendor] = vendorsFrom([
      { id: preset.id, runtime: preset.runtime, model: preset.model, enabled: true },
    ]);

    assert.equal(vendor.runtime, preset.runtime, `the ${preset.id} preset does not survive a round trip`);
  }
});

test('a local reviewer keeps its runtime, which is what keeps it off the Codex CLI', () => {
  const [vendor] = vendorsFrom([
    { id: 'local', runtime: 'local', model: 'qwen3:32b', enabled: true, baseUrl: 'http://127.0.0.1:11434/v1' },
  ]);

  assert.equal(vendor.runtime, 'local');
  assert.equal(vendor.baseUrl, 'http://127.0.0.1:11434/v1', 'and its endpoint, which a codex row would reuse');
});

test('a runtime this build does not know is still rewritten to codex', () => {
  // The behaviour is right and stays: a name from a NEWER extension must not leave a row that
  // launches nothing. Only the list of known names was wrong.
  const [vendor] = vendorsFrom([{ id: 'x', runtime: 'something-from-the-future', enabled: true }]);

  assert.equal(vendor.runtime, 'codex');
});

test('the runtime list and the runtime type are one declaration', () => {
  // Not a style point. Two declarations of the same set is what produced the defect, and the only
  // fix that cannot regress is that there is nothing left to keep in step.
  const asType: readonly Runtime[] = RUNTIMES;

  assert.ok(asType.includes('local'));
  assert.deepEqual([...RUNTIMES].sort(), ['antigravity', 'claude', 'codex', 'gemini', 'local']);
});
