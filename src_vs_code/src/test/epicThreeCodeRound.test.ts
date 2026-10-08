import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { removedRow } from '../catalogCommands';
import { type CheckRunPorts, runConsultantCheck } from '../consultantCheckRun';
import { NEW_CONTROLS } from '../newTags';
import { DEFAULT_VENDORS, type Vendor } from '../vendors';

/**
 * Epic 3's code round (coai session ebf28ac3): what was wrong in the built epic, each pinned by the guarantee a person
 * expects.
 */

const row = (id: string, runtime: Vendor['runtime'], extra: Partial<Vendor> = {}): Vendor => ({
  ...DEFAULT_VENDORS[0]!, id, runtime, model: `${id}-model`, enabled: true, plan: true, code: true, ...extra,
});

test('removing the Bugz row leaves Bugz with no model — never naming a row that is gone', () => {
  const rows = [row('codex', 'codex'), row('local', 'local', { uses: ['bugz'], plan: false, code: false })];

  assert.equal(removedRow(rows, 'local', 'local/local-model').bugzModel, '');
  assert.equal(removedRow(rows, 'local', 'other/x').bugzModel, undefined, 'a Bugz model held elsewhere is left as it is');
});

test('a model check whose row is gone by the time it runs spawns nothing, and says why', async () => {
  let spawned = 0;
  const ports: CheckRunPorts = {
    run: () => { spawned += 1; return Promise.resolve({ code: 0, output: '' }); },
    readState: () => Promise.resolve(undefined),
    nowMs: () => 0,
    every: () => () => undefined,
  };

  const result = await runConsultantCheck('model-gone', ports, '');

  assert.equal(spawned, 0, 'a paid turn was started for a row that is no longer there');
  assert.equal(result.kind, 'crashed');
  assert.match(result.kind === 'crashed' ? result.why : '', /removed/);
});

const SRC = join(__dirname, '..', '..', 'src');

test('every control marked "new" is in the first-seen list, and every listed control is drawn with its mark', () => {
  const drawn = new Set(readdirSync(SRC).filter((name) => name.endsWith('.ts'))
    .flatMap((name) => [...readFileSync(join(SRC, name), 'utf8').matchAll(/newTag\('([a-zA-Z.]+)'/g)].map((found) => found[1]!)));

  assert.deepEqual([...drawn].filter((id) => !NEW_CONTROLS.includes(id)), [], 'a control is marked but never stamped — its mark never shows');
  assert.deepEqual(NEW_CONTROLS.filter((id) => !drawn.has(id)), [], 'a listed control is never drawn — a stale entry');
});
