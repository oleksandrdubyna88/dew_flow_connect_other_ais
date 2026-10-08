import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CatalogLayer, LayerWrite, migrateLayer, MIGRATED, restoreLayer, RESTORED } from '../catalogMigration';
import { envBlock, settingsFrom } from '../settingsShape';
import { DEFAULT_VENDORS, vendorsFrom } from '../vendors';

/**
 * The one-time move of every model DEFINITION into the catalog (PLAN_one_model_catalog.md D3, E1.3).
 *
 * <p>A consultant or question-consultant entry that carries its own runtime becomes a catalog row, and the
 * entry becomes a reference to it. Judged where it matters: the env block coai-mcp reads must be the
 * same, byte for byte, before and after — that is the whole promise of an epic in which nothing changes
 * for the person.</p>
 */

/** A layer as a reader sees it: its own values, falling back to the shared vendors. */
function reader(layer: CatalogLayer): (key: string) => unknown {
  const own: Record<string, unknown> = { ...layer };

  return (key) => (key === 'vendors' ? own['vendors'] ?? layer.sharedVendors : own[key]);
}

function env(layer: CatalogLayer): Record<string, string> {
  const read = reader(layer);

  return envBlock(settingsFrom(read), vendorsFrom(read('vendors')));
}

/** Apply the writes the way the host does: in order, `undefined` removing the key. */
function apply(layer: CatalogLayer, writes: readonly LayerWrite[]): CatalogLayer {
  const next: Record<string, unknown> = { ...layer };
  for (const write of writes) {
    const field = write.key === 'migratedFrom' ? 'backup' : write.key === 'catalogMigration' ? 'marker' : write.key;
    if (write.value === undefined) {
      delete next[field];
    } else {
      next[field] = write.value;
    }
  }

  return next as CatalogLayer;
}

function migrated(layer: CatalogLayer): CatalogLayer {
  const outcome = migrateLayer(layer);
  assert.equal(outcome.kind, 'migrate', `expected a migration, got ${JSON.stringify(outcome)}`);

  return apply(layer, outcome.kind === 'migrate' ? outcome.writes : []);
}

const REVIEWERS = [
  { id: 'codex', runtime: 'codex', model: 'gpt-x', enabled: true },
  { id: 'claude', runtime: 'claude', model: '', enabled: true },
];

/** The operator's shape: overlapping reviewer, consultant and question entries. */
const OPERATOR: CatalogLayer = {
  vendors: REVIEWERS,
  consultants: {
    claude: { vendor: 'codex', runtime: 'codex', model: 'gpt-x', baseUrl: '', executablePath: '' },
    codex: { vendor: 'glm', runtime: 'codex', model: 'glm-5.3', baseUrl: 'https://glm.example/v1', executablePath: '' },
  },
  qconsultRows: [
    { id: 'q1', vendor: 'claude', runtime: 'claude', model: 'opus', prompt: 'p1', enabled: true, key: '' },
    { id: 'q2', vendor: 'codex', runtime: '', model: '', prompt: 'p2', enabled: true },
  ],
};

test('a pristine layer writes nothing at all', () => {
  assert.deepEqual(migrateLayer({}), { kind: 'unchanged' });
  assert.deepEqual(migrateLayer({ vendors: REVIEWERS, qconsultRows: [] }), { kind: 'unchanged' });
});

test('the env block coai-mcp reads is byte-identical after the migration', () => {
  assert.deepEqual(env(migrated(OPERATOR)), env(OPERATOR));
});

test('a consultant definition becomes a row that reviews nothing, and the caller refers to it', () => {
  const after = migrated({ vendors: REVIEWERS, consultants: OPERATOR.consultants });
  const rows = vendorsFrom(after.vendors);
  const glm = rows.find((row) => row.id === 'consult-codex');

  assert.deepEqual((after.consultants as Record<string, unknown>)['codex'], { vendor: 'consult-codex' });
  assert.equal(glm?.runtime, 'codex');
  assert.equal(glm?.model, 'glm-5.3');
  assert.equal(glm?.baseUrl, 'https://glm.example/v1');
  assert.equal(glm?.vaultKeyName, 'glm', 'the key stays filed under the name it was stored under');
  assert.deepEqual(glm?.uses, ['consultant']);
  assert.deepEqual([glm?.plan, glm?.code, glm?.document], [false, false, false]);
});

test('a definition that equals an existing row exactly joins it rather than duplicating it', () => {
  const exact = { id: 'q1', vendor: 'claude', runtime: 'claude', model: '', prompt: 'p1', enabled: true, key: '' };
  const before: CatalogLayer = { vendors: REVIEWERS, qconsultRows: [exact] };
  const after = migrated(before);
  const rows = vendorsFrom(after.vendors);

  assert.equal(rows.length, 2, 'no new row');
  assert.deepEqual(rows.find((row) => row.id === 'claude')?.uses, ['qconsult']);
  assert.deepEqual((after.qconsultRows as Record<string, unknown>[])[0], { id: 'q1', vendor: 'claude', prompt: 'p1', enabled: true, key: '' });
  assert.deepEqual(env(after), env(before));
});

test('a consultant never becomes a reference equal to its shipped pair, which would drop it off the wire', () => {
  // claude's definition equals the `codex` reviewer row exactly — but `{ vendor: 'codex' }` for the claude
  // caller IS the shipped pair, and a caller equal to its shipped pair is not sent at all.
  const after = migrated({ vendors: REVIEWERS, consultants: { claude: (OPERATOR.consultants as Record<string, unknown>)['claude'] } });

  assert.deepEqual((after.consultants as Record<string, unknown>)['claude'], { vendor: 'consult-claude' });
  assert.deepEqual(env(after), env({ vendors: REVIEWERS, consultants: { claude: (OPERATOR.consultants as Record<string, unknown>)['claude'] } }));
});

test('two callers with one definition share one row', () => {
  const same = { vendor: 'glm', runtime: 'codex', model: 'glm-5.3', baseUrl: 'https://glm.example/v1', executablePath: '' };
  const after = migrated({ vendors: REVIEWERS, consultants: { codex: same, gemini: same } });
  const added = vendorsFrom(after.vendors).filter((row) => row.uses !== undefined);

  assert.deepEqual(added.map((row) => row.id), ['consult-codex']);
  assert.deepEqual((after.consultants as Record<string, unknown>)['gemini'], { vendor: 'consult-codex' });
});

test('an id already taken by a different row takes the next free one', () => {
  const taken = [...REVIEWERS, { id: 'consult-codex', runtime: 'claude', model: 'other', enabled: true }];
  const after = migrated({ vendors: taken, consultants: { codex: (OPERATOR.consultants as Record<string, unknown>)['codex'] } });

  assert.deepEqual((after.consultants as Record<string, unknown>)['codex'], { vendor: 'consult-codex-2' });
});

test('a question row keeps its id, prompt, switch and key, and refers to an ask- row', () => {
  const row = { id: 'q9', vendor: 'qwen', runtime: 'codex', model: 'qwen-max', baseUrl: 'https://q.example', prompt: 'p', enabled: false, key: 'qwen-key' };
  const before: CatalogLayer = { vendors: REVIEWERS, qconsultRows: [row] };
  const after = migrated(before);

  assert.deepEqual((after.qconsultRows as unknown[])[0], { id: 'q9', vendor: 'ask-q9', prompt: 'p', enabled: false, key: 'qwen-key' });
  assert.equal(vendorsFrom(after.vendors).find((one) => one.id === 'ask-q9')?.vaultKeyName, 'qwen');
  assert.deepEqual(env(after), env(before));
});

test('running it twice changes nothing the second time', () => {
  assert.deepEqual(migrateLayer(migrated(OPERATOR)), { kind: 'unchanged' });
});

test('a run stopped after any write is finished by the next run, never duplicated', () => {
  const outcome = migrateLayer(OPERATOR);
  assert.equal(outcome.kind, 'migrate');
  const writes = outcome.kind === 'migrate' ? outcome.writes : [];
  const whole = apply(OPERATOR, writes);

  for (let stop = 1; stop < writes.length; stop += 1) {
    const half = apply(OPERATOR, writes.slice(0, stop));
    const rerun = migrateLayer(half);
    const finished = rerun.kind === 'migrate' ? apply(half, rerun.writes) : half;

    assert.deepEqual(vendorsFrom(finished.vendors), vendorsFrom(whole.vendors), `stopped after write ${stop}`);
    assert.deepEqual(env(finished), env(OPERATOR), `stopped after write ${stop}`);
  }
});

test('the backup holds every key it rewrites, absence included, and is written once', () => {
  const layer: CatalogLayer = { consultants: OPERATOR.consultants, sharedVendors: REVIEWERS };
  const outcome = migrateLayer(layer);
  const first = outcome.kind === 'migrate' ? outcome.writes[0] : undefined;

  assert.equal(first?.key, 'migratedFrom', 'the backup is the first write');
  assert.deepEqual(first?.value, { keys: ['vendors', 'consultants', 'qconsultRows'], values: { consultants: OPERATOR.consultants } });
  const again = migrateLayer({ ...layer, backup: { keys: ['vendors'], values: {} } });
  assert.ok(again.kind === 'migrate' && again.writes.every((write) => write.key !== 'migratedFrom'), 'never overwritten');
});

test('the marker is the last write', () => {
  const outcome = migrateLayer(OPERATOR);

  assert.ok(outcome.kind === 'migrate');
  assert.deepEqual(outcome.writes.at(-1), { key: 'catalogMigration', value: MIGRATED });
});

test('restore puts every key back exactly, removes what was absent, and stops the migration', () => {
  const before: CatalogLayer = { consultants: OPERATOR.consultants, qconsultRows: OPERATOR.qconsultRows, sharedVendors: REVIEWERS };
  const after = migrated(before);
  const restore = restoreLayer(after);

  assert.equal(restore.kind, 'restore');
  const back = apply(after, restore.kind === 'restore' ? restore.writes : []);
  assert.equal(back.vendors, undefined, 'the layer had no vendors of its own, and has none again');
  assert.deepEqual(back.consultants, before.consultants);
  assert.deepEqual(back.qconsultRows, before.qconsultRows);
  assert.equal(back.marker, RESTORED);
  assert.deepEqual(migrateLayer(back), { kind: 'restored' }, 'reloading does not migrate it again');
});

test('a restore stopped after any write leaves every consultant and question row resolvable', () => {
  // The migration writes rows before references; a restore must write in the REVERSE order (CodeRabbit, PR #681):
  // put the definitions back first, then drop the rows. Stopped part way, the layer then holds definitions and
  // perhaps rows nobody refers to — never a reference to a row that is gone.
  const after = migrated(OPERATOR);
  const restore = restoreLayer(after);
  const writes = restore.kind === 'restore' ? restore.writes : [];

  assert.deepEqual(writes.map((write) => write.key), ['consultants', 'qconsultRows', 'vendors', 'catalogMigration']);
  for (let stop = 1; stop < writes.length; stop += 1) {
    const half = apply(after, writes.slice(0, stop));
    const rows = vendorsFrom(half.vendors ?? half.sharedVendors);
    const consultants = settingsFrom((key) => (half as Record<string, unknown>)[key] ?? (key === 'vendors' ? half.sharedVendors : undefined)).consult.byCaller;

    assert.ok(Object.values(consultants).every((one) => one.kind === 'definition'), `stopped after write ${stop}: a consultant became unavailable`);
    assert.ok(rows.length > 0, `stopped after write ${stop}`);
  }
});

test('restore with no backup says so and writes nothing', () => {
  assert.equal(restoreLayer({ consultants: {} }).kind, 'nothing-to-restore');
});

test('a migration that would pass 64 rows writes nothing and says how many it needed', () => {
  const many = Array.from({ length: 63 }, (_, i) => ({ id: `r${i}`, runtime: 'codex', model: `m${i}` }));
  const outcome = migrateLayer({ vendors: many, consultants: OPERATOR.consultants });

  assert.equal(outcome.kind, 'refused');
  assert.match(outcome.kind === 'refused' ? outcome.why : '', /65 models.*at most 64/u);
});

test('a definition whose name is not a valid id is left as it is, and said', () => {
  const outcome = migrateLayer({ vendors: REVIEWERS, consultants: { codex: { vendor: 'My GLM', runtime: 'codex', model: 'glm' } } });

  assert.equal(outcome.kind, 'unchanged-with-skips');
  assert.match(outcome.kind === 'unchanged-with-skips' ? outcome.skipped.join() : '', /My GLM/u);
});

test('a layer with no rows of its own starts from the shared ones', () => {
  const after = migrated({ consultants: OPERATOR.consultants, sharedVendors: REVIEWERS });

  assert.deepEqual(vendorsFrom(after.vendors).map((row) => row.id), ['codex', 'claude', 'consult-claude', 'consult-codex']);
});

test('a layer with no rows anywhere starts from the shipped ones', () => {
  const after = migrated({ consultants: OPERATOR.consultants });

  assert.deepEqual(vendorsFrom(after.vendors).slice(0, DEFAULT_VENDORS.length).map((row) => row.id), DEFAULT_VENDORS.map((row) => row.id));
});

test('after an older build strips the catalog fields, the next run gives the uses back and adds nothing', () => {
  const after = migrated(OPERATOR);
  // What an older build writes back: its own parse of the rows, which knows no `uses` and no `name`.
  const stripped = (after.vendors as Record<string, unknown>[]).map(({ uses: _uses, ...rest }) => rest);
  const downgraded = { ...after, vendors: stripped };
  const rerun = migrateLayer(downgraded);
  const repaired = rerun.kind === 'migrate' ? apply(downgraded, rerun.writes) : downgraded;

  assert.deepEqual(vendorsFrom(repaired.vendors), vendorsFrom(after.vendors));
  assert.deepEqual(env(repaired), env(OPERATOR));
});

test('a layer that was restored is never migrated again by itself', () => {
  assert.deepEqual(migrateLayer({ ...OPERATOR, marker: RESTORED }), { kind: 'restored' });
});

// ---------------------------------------------------------------- the Bugz model (PLAN_one_model_catalog.md E2.1)

const LOCAL = { id: 'local', runtime: 'local', model: 'qwen3.5:35b', baseUrl: 'http://127.0.0.1:11434/v1', enabled: true };
const BY_RUNTIME = { bugzByRuntime: true };

test('a Bugz model that differs from its row\'s model becomes a bugz- row, and the setting names it', () => {
  const layer: CatalogLayer = { vendors: [...REVIEWERS, LOCAL], bugzModel: 'local/gemma4:27b' };
  const outcome = migrateLayer(layer, BY_RUNTIME);
  assert.equal(outcome.kind, 'migrate');
  const after = apply(layer, outcome.kind === 'migrate' ? outcome.writes : []);
  const row = vendorsFrom(after.vendors).find((one) => one.id === 'bugz-local');

  assert.equal(after.bugzModel, 'bugz-local/gemma4:27b');
  assert.deepEqual([row?.runtime, row?.model, row?.baseUrl], ['local', 'gemma4:27b', LOCAL.baseUrl], 'the row\'s launch, the Bugz model');
  assert.deepEqual(row?.uses, ['bugz']);
  assert.equal(row?.plan === false && row.code === false && row.document === false, true, 'it reviews nothing');
  assert.equal(vendorsFrom(after.vendors).find((one) => one.id === 'local')?.model, 'qwen3.5:35b', 'the reviewer row is not touched');
  assert.equal(env(after)['COAI_VENDORS'], env(layer)['COAI_VENDORS'], 'the reviewers coai-mcp is handed are the same: a Bugz-only row never crosses');
  // The Bugz entry in the env block went in E5.1 (T7: nothing in coai-mcp read it, the collect takes `--model`), so
  // the setting above — which the collect passes — is what names the row it moved to.
  assert.equal(env(after)['COAI_BUGZ_MODEL'], undefined, 'no environment key for the Bugz model: nothing reads one');
});

test('the Bugz model moves only when the binary ranks by runtime — an older one refuses bugz-local', () => {
  const layer: CatalogLayer = { vendors: [...REVIEWERS, LOCAL], bugzModel: 'local/gemma4:27b' };

  assert.equal(migrateLayer(layer).kind, 'unchanged');
});

test('a Bugz model that IS its row\'s model, or names no row, stays as it is', () => {
  for (const bugzModel of ['local/qwen3.5:35b', 'nowhere/gemma4:27b', '', 'local']) {
    assert.equal(migrateLayer({ vendors: [...REVIEWERS, LOCAL], bugzModel }, BY_RUNTIME).kind, 'unchanged', bugzModel);
  }
});

test('moving the Bugz model twice is moving it once', () => {
  const layer: CatalogLayer = { vendors: [...REVIEWERS, LOCAL], bugzModel: 'local/gemma4:27b' };
  const once = migrateLayer(layer, BY_RUNTIME);
  const after = apply(layer, once.kind === 'migrate' ? once.writes : []);

  assert.equal(migrateLayer(after, BY_RUNTIME).kind, 'unchanged');
});

test('a layer epic 1 already migrated gains the Bugz model in its backup before it is rewritten, and restore puts it back', () => {
  const e1 = migrated({ ...OPERATOR, vendors: [...REVIEWERS, LOCAL] });
  const layer: CatalogLayer = { ...e1, bugzModel: 'local/gemma4:27b' };
  const outcome = migrateLayer(layer, BY_RUNTIME);
  assert.equal(outcome.kind, 'migrate');
  const writes = outcome.kind === 'migrate' ? outcome.writes : [];
  const after = apply(layer, writes);
  const backup = after.backup as { keys: string[]; values: Record<string, unknown> };
  const before = e1.backup as { keys: string[]; values: Record<string, unknown> };

  assert.ok(writes.findIndex((w) => w.key === 'migratedFrom') < writes.findIndex((w) => w.key === 'bugzModel'), 'the backup first');
  assert.ok(backup.keys.includes('bugzModel'));
  assert.equal(backup.values['bugzModel'], 'local/gemma4:27b', 'the value from before the move');
  assert.deepEqual(backup.values['vendors'], before.values['vendors'], 'what epic 1 saved is never overwritten');

  const restore = restoreLayer(after);
  assert.equal(restore.kind, 'restore');
  const restored = apply(after, restore.kind === 'restore' ? restore.writes : []);
  assert.equal(restored.bugzModel, 'local/gemma4:27b');
  assert.ok(!vendorsFrom(restored.vendors).some((one) => one.id === 'bugz-local'));
});

test('picking another model after the move rewrites the Bugz row in place — no second row, no orphan', () => {
  const layer: CatalogLayer = { vendors: [...REVIEWERS, LOCAL], bugzModel: 'local/gemma4:27b' };
  const first = migrateLayer(layer, BY_RUNTIME);
  const moved = apply(layer, first.kind === 'migrate' ? first.writes : []);
  const picked: CatalogLayer = { ...moved, bugzModel: 'local/llama4:17b' };
  const second = migrateLayer(picked, BY_RUNTIME);
  const after = apply(picked, second.kind === 'migrate' ? second.writes : []);
  const bugzRows = vendorsFrom(after.vendors).filter((row) => row.uses?.includes('bugz') === true);

  assert.deepEqual(bugzRows.map((row) => [row.id, row.model]), [['bugz-local', 'llama4:17b']]);
  assert.equal(after.bugzModel, 'bugz-local/llama4:17b');
});

test('a pick through the Bugz row itself is the Bugz model already — nothing moves', () => {
  const layer: CatalogLayer = { vendors: [...REVIEWERS, LOCAL], bugzModel: 'local/gemma4:27b' };
  const first = migrateLayer(layer, BY_RUNTIME);
  const moved = apply(layer, first.kind === 'migrate' ? first.writes : []);

  assert.equal(migrateLayer({ ...moved, bugzModel: 'bugz-local/llama4:17b' }, BY_RUNTIME).kind, 'unchanged',
    'bugz-local is Bugz\'s own row; a bugz-bugz-local row would be one per pick');
});
