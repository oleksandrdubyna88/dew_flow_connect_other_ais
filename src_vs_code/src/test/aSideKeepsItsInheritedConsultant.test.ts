import assert from 'node:assert/strict';
import { test } from 'node:test';
import { migrateLayer, MIGRATED, restoreLayer, type CatalogLayer, type LayerWrite } from '../catalogMigration';
import { DEFAULT_VENDORS } from '../vendors';

/**
 * A side that keeps its own model list but inherits the shared consultants keeps its consultant through the migration
 * (the cadence consultation for epics 1–3 of research/PLAN_one_model_catalog.md, finding C1).
 *
 * <p>The user layer's migration turns a consultant definition into a row in ITS list and the definition into a reference
 * to that row. A side whose overlay holds `vendors` but not `consultants` inherits the reference — and its own list had no
 * such row, so on that side the consultant named nothing and was unavailable. The side now takes the referenced row from
 * the shared list into its own; a restore puts its list back as it was.</p>
 */

type Raw = Record<string, unknown>;

const written = (writes: readonly LayerWrite[], key: LayerWrite['key']): unknown => writes.find((one) => one.key === key)?.value;

function migrated(layer: CatalogLayer): readonly LayerWrite[] {
  const outcome = migrateLayer(layer);
  assert.equal(outcome.kind, 'migrate', `the run did not migrate: ${JSON.stringify(outcome)}`);

  return outcome.kind === 'migrate' ? outcome.writes : [];
}

test('a side with its own models keeps the consultant it inherits, and a restore gives its list back', () => {
  const sideRows = DEFAULT_VENDORS.map((row) => ({ ...row }));
  // The user layer, as it was: a consultant DEFINITION for the "other" caller.
  const user = migrated({ consultants: { other: { vendor: 'claude', runtime: 'claude', model: 'opus' } } });
  const userRows = written(user, 'vendors') as readonly Raw[];
  const userConsultants = written(user, 'consultants') as Record<string, Raw>;
  const reference = String(userConsultants['other']?.['vendor'] ?? '');
  assert.ok(userRows.some((row) => row['id'] === reference), 'the fixture: the user layer did not make the consultant a row');

  // The side: its own list, no consultants of its own — it inherits the user layer's, now a reference.
  const side = migrated({ side: true, vendors: sideRows, sharedVendors: userRows, sharedConsultants: userConsultants });
  const sideAfter = written(side, 'vendors') as readonly Raw[];

  assert.ok(sideAfter.some((row) => row['id'] === reference), `the side's list has no ${reference}, so its inherited consultant names nothing`);
  assert.equal(written(side, 'consultants'), undefined, 'the side was given consultants of its own — it inherits them');

  const restore = restoreLayer({ side: true, vendors: sideAfter, backup: written(side, 'migratedFrom'), marker: MIGRATED });
  assert.ok(restore.kind === 'restore');
  assert.deepEqual(restore.writes.find((one) => one.key === 'vendors')?.value, sideRows, 'a restore did not give the side its list back');
});

test('a side that holds its own consultants, or no list of its own, is not given rows it does not refer to', () => {
  const userRows = [...DEFAULT_VENDORS.map((row) => ({ ...row })), { ...DEFAULT_VENDORS[0]!, id: 'consult-other', runtime: 'claude', uses: ['consultant'] }];
  const shared = { other: { vendor: 'consult-other', model: '' } };

  const own = migrateLayer({ side: true, vendors: DEFAULT_VENDORS.map((row) => ({ ...row })), consultants: {}, sharedVendors: userRows, sharedConsultants: shared });
  assert.equal(own.kind, 'unchanged', 'a side with consultants of its own was handed the shared consultant\'s row');

  assert.equal(migrateLayer({ side: true, sharedVendors: userRows, sharedConsultants: shared }).kind, 'unchanged', 'a side that reads the shared list needs nothing');
});
