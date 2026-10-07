import assert from 'node:assert/strict';
import { test } from 'node:test';
import { migrateLayer, MIGRATED, restoreLayer, RESTORED, type CatalogLayer, type LayerWrite } from '../catalogMigration';
import { DEFAULT_VENDORS } from '../vendors';

/**
 * E4.6a of todo/PLAN_one_model_catalog.md, wired into the epic 1 migration: the chat presets move in the same run, in
 * the same safe order (backup, rows, the record, the chat's own keys, the marker), per layer — and never twice.
 */

const preset = (id: string, extra: Record<string, unknown> = {}): Record<string, unknown> => ({ id, name: `Name of ${id}`, runtime: 'claude', model: 'opus', ...extra });

function writes(layer: CatalogLayer): readonly LayerWrite[] {
  const outcome = migrateLayer(layer);
  assert.equal(outcome.kind, 'migrate', `the run did not migrate: ${JSON.stringify(outcome)}`);

  return outcome.kind === 'migrate' ? outcome.writes : [];
}

function written(all: readonly LayerWrite[], key: LayerWrite['key']): unknown {
  return all.find((one) => one.key === key)?.value;
}

test('the user layer moves its presets: rows, the record, the main model, in the safe order', () => {
  const all = writes({ chatPresets: [preset('a'), preset('b', { main: true, model: 'sonnet' })], chatModel: 'a', chatModelName: 'haiku' });

  assert.deepEqual(all.map((one) => one.key), ['migratedFrom', 'vendors', 'chatPresetsMoved', 'chatModel', 'chatModelName', 'catalogMigration']);
  const rows = written(all, 'vendors') as readonly Record<string, unknown>[];
  assert.deepEqual(rows.slice(-2).map((row) => row['id']), ['chat-a', 'chat-b']);
  assert.equal(written(all, 'chatModel'), 'chat-b');
  assert.equal(written(all, 'chatModelName'), 'sonnet', 'a stale model name would open another model on the main row');
  const backup = written(all, 'migratedFrom') as { keys: readonly string[]; values: Record<string, unknown> };
  assert.ok(['chatPresetsMoved', 'chatModel', 'chatModelName'].every((key) => backup.keys.includes(key)), `not backed up: ${backup.keys.join(', ')}`);
  assert.deepEqual([backup.values['chatModel'], backup.values['chatModelName']], ['a', 'haiku']);
  assert.equal(written(all, 'catalogMigration'), MIGRATED);
});

test('with no main preset the chat model is remapped to its row and the model name left alone', () => {
  const all = writes({ chatPresets: [preset('a'), preset('b')], chatModel: 'b', chatModelName: 'opus' });

  assert.equal(written(all, 'chatModel'), 'chat-b');
  assert.ok(!all.some((one) => one.key === 'chatModelName'));
});

test('a second run changes nothing — the record holds every preset', () => {
  const first = writes({ chatPresets: [preset('a')] });
  const again = migrateLayer({
    chatPresets: [preset('a')],
    vendors: written(first, 'vendors'),
    chatPresetsMoved: written(first, 'chatPresetsMoved'),
    marker: MIGRATED,
    backup: written(first, 'migratedFrom'),
  });

  assert.equal(again.kind, 'unchanged');
});

test('a run interrupted after its record was written still points the chat model at the row', () => {
  // The record and `coai.chatModel` are two settings writes. A window closed between them left the chat model naming the
  // old preset, and the next run, finding nothing to move, never remapped it. (CodeRabbit's architecture note, PR #688.)
  const first = writes({ chatPresets: [preset('a')], chatModel: 'a' });
  const resumed = writes({
    chatPresets: [preset('a')],
    vendors: written(first, 'vendors'),
    chatPresetsMoved: written(first, 'chatPresetsMoved'),
    chatModel: 'a',
    marker: MIGRATED,
    backup: written(first, 'migratedFrom'),
  });

  assert.equal(written(resumed, 'chatModel'), 'chat-a');
});

test('a preset an older build edited after the move, its run interrupted, ends on the EDITED row as an uninterrupted run does', () => {
  // The own review of epic 4's code round: the record then holds two entries of id `a`, and the resume took the FIRST —
  // the revision the person had edited away — while the uninterrupted run points the chat at the newest.
  const first = writes({ chatPresets: [preset('a')], chatModel: 'a' });
  const edited = [preset('a', { model: 'sonnet' })];
  const second = writes({
    chatPresets: edited, vendors: written(first, 'vendors'), chatPresetsMoved: written(first, 'chatPresetsMoved'),
    chatModel: 'a', marker: MIGRATED, backup: written(first, 'migratedFrom'),
  });
  const uninterrupted = written(second, 'chatModel');
  const resumed = migrateLayer({
    chatPresets: edited, vendors: written(second, 'vendors'), chatPresetsMoved: written(second, 'chatPresetsMoved'),
    chatModel: 'a', marker: MIGRATED, backup: written(second, 'migratedFrom') ?? written(first, 'migratedFrom'),
  });
  const resumedWrites = resumed.kind === 'migrate' ? resumed.writes : [];

  assert.equal(uninterrupted, 'chat-a-2', 'the fixture: the edited revision did not get its own row');
  assert.equal(written(resumedWrites, 'chatModel'), uninterrupted, `the resume did not end where the uninterrupted run ends: ${JSON.stringify(resumed)}`);
});

test('a run interrupted before the chat keys of a MAIN move ends where an uninterrupted run ends — row AND model', () => {
  // The uninterrupted run writes chatModel = the main row AND chatModelName = its model; a resume that repaired only the
  // chat model left a stale model name, which opens another model on the right row (the risk consultation, R4).
  const presets = [preset('a', { main: true, model: 'sonnet' })];
  const first = writes({ chatPresets: presets, chatModel: 'a', chatModelName: 'haiku' });
  const resumed = writes({
    chatPresets: presets,
    vendors: written(first, 'vendors'),
    chatPresetsMoved: written(first, 'chatPresetsMoved'),
    chatModel: 'a',
    chatModelName: 'haiku',
    marker: MIGRATED,
    backup: written(first, 'migratedFrom'),
  });

  assert.deepEqual([written(resumed, 'chatModel'), written(resumed, 'chatModelName')], [written(first, 'chatModel'), written(first, 'chatModelName')]);
});

test('a chat model that names a row is the person\'s choice, and no recorded preset of that id takes it over', () => {
  const first = writes({ chatPresets: [preset('a')] });
  const rows = written(first, 'vendors') as readonly Record<string, unknown>[];
  const again = migrateLayer({
    chatPresets: [preset('a')],
    // The record says preset "codex" moved to chat-a; the person has since picked the reviewer row called codex.
    vendors: rows,
    chatPresetsMoved: [{ presetId: 'codex', runtime: 'claude', model: 'opus', name: 'Name of a', rowId: 'chat-a' }, ...(written(first, 'chatPresetsMoved') as readonly unknown[])],
    chatModel: 'codex',
    marker: MIGRATED,
    backup: written(first, 'migratedFrom'),
  });

  assert.ok(rows.some((row) => row['id'] === 'codex'), 'the fixture has no row called codex');
  assert.equal(again.kind, 'unchanged', 'the person\'s pick of a row was rewritten');
});

test('a side that keeps no rows of its own is left alone; one that does gets the user layer\'s row ids', () => {
  assert.equal(migrateLayer({ side: true, chatPresets: [preset('a')], sharedVendors: DEFAULT_VENDORS }).kind, 'unchanged');

  const userRecord = [{ presetId: 'a', runtime: 'claude', model: 'opus', name: 'Name of a', rowId: 'chat-a' }];
  const taken = [...DEFAULT_VENDORS.map((row) => ({ ...row })), { id: 'chat-a', runtime: 'codex', model: '', enabled: true, plan: false, code: true }];
  const side = writes({ side: true, chatPresets: [preset('a')], vendors: DEFAULT_VENDORS.map((row) => ({ ...row })), userChatRecord: userRecord });
  assert.equal((written(side, 'vendors') as readonly Record<string, unknown>[]).at(-1)!['id'], 'chat-a');
  assert.ok(!side.some((one) => one.key === 'chatModel'), 'the chat model is the user layer\'s, written once there');

  const clash = writes({ side: true, chatPresets: [preset('a')], vendors: taken, userChatRecord: userRecord });
  assert.equal((written(clash, 'vendors') as readonly Record<string, unknown>[]).at(-1)!['id'], 'chat-a-2', 'a taken id is never reused');
});

test('a side whose own row for the chat model got another id opens on ITS row, not the unrelated one the inherited id names', () => {
  // The user layer's chat model is `chat-a`; the side already owns an unrelated `chat-a`, so its move put the preset at
  // `chat-a-2` — and the side, inheriting `chat-a`, opened on the unrelated row (the risk consultation of epic 4, R2).
  const userRecord = [{ presetId: 'a', runtime: 'claude', model: 'opus', name: 'Name of a', rowId: 'chat-a' }];
  const taken = [...DEFAULT_VENDORS.map((row) => ({ ...row })), { id: 'chat-a', runtime: 'codex', model: '', enabled: true, plan: false, code: true }];

  const clash = writes({ side: true, chatPresets: [preset('a')], vendors: taken, userChatRecord: userRecord, userChatModel: 'chat-a' });
  assert.equal(written(clash, 'chatModel'), 'chat-a-2');

  const same = writes({ side: true, chatPresets: [preset('a')], vendors: DEFAULT_VENDORS.map((row) => ({ ...row })), userChatRecord: userRecord, userChatModel: 'chat-a' });
  assert.ok(!same.some((one) => one.key === 'chatModel'), 'a side whose row kept the id still inherits the chat model');
});

test('a restored layer moves nothing, and a restore puts the chat\'s keys back before the rows', () => {
  assert.equal(migrateLayer({ chatPresets: [preset('a')], marker: RESTORED }).kind, 'restored');

  const all = writes({ chatPresets: [preset('a', { main: true })], chatModel: 'a' });
  const restore = restoreLayer({ backup: written(all, 'migratedFrom') });
  assert.equal(restore.kind, 'restore');
  const keys = restore.kind === 'restore' ? restore.writes.map((one) => one.key) : [];
  assert.ok(keys.indexOf('chatModel') < keys.indexOf('vendors') && keys.indexOf('chatPresetsMoved') < keys.indexOf('vendors'), keys.join(', '));
  const values = restore.kind === 'restore' ? restore.writes : [];
  assert.equal(values.find((one) => one.key === 'chatModel')?.value, 'a');
  assert.equal(values.find((one) => one.key === 'chatPresetsMoved')?.value, undefined, 'the record did not exist before the move');
});

test('presets past the 64-row cap refuse the run, said', () => {
  const many = Array.from({ length: 70 }, (_, index) => preset(`p-${index}`));

  assert.equal(migrateLayer({ chatPresets: many }).kind, 'refused');
});

test('a chat row an older build wrote back without its uses gets its chat tick back', () => {
  const lost = { id: 'chat-a', runtime: 'claude', model: 'opus', enabled: true, plan: false, code: false, document: false, baseUrl: '', executablePath: '', vaultKeyName: 'a' };
  const all = writes({
    chatPresets: [preset('a')],
    vendors: [...DEFAULT_VENDORS.map((row) => ({ ...row })), lost],
    chatPresetsMoved: [{ presetId: 'a', runtime: 'claude', model: 'opus', name: 'Name of a', rowId: 'chat-a' }],
    marker: MIGRATED,
    backup: { keys: ['vendors', 'consultants', 'qconsultRows'], values: {} },
  });

  assert.deepEqual((written(all, 'vendors') as readonly Record<string, unknown>[]).at(-1)!['uses'], ['chat']);
});

test('a chat row whose preset id kept no key name gets its tick back too — the record proves the move made it', () => {
  // A mixed-case id is not normaliseId-clean, so its row carries no vaultKeyName; the repair looked for a "foreign" key
  // name to know a migration made the row, and so never repaired this one — after a downgrade and an upgrade the model
  // was gone from the chat (the risk consultation, R6).
  const lost = { id: 'chat-glm-fast', runtime: 'claude', model: 'opus', enabled: true, plan: false, code: false, document: false, baseUrl: '', executablePath: '' };
  const all = writes({
    chatPresets: [preset('GLM Fast')],
    vendors: [...DEFAULT_VENDORS.map((row) => ({ ...row })), lost],
    chatPresetsMoved: [{ presetId: 'GLM Fast', runtime: 'claude', model: 'opus', name: 'Name of GLM Fast', rowId: 'chat-glm-fast' }],
    marker: MIGRATED,
    backup: { keys: ['vendors', 'consultants', 'qconsultRows'], values: {} },
  });

  assert.deepEqual((written(all, 'vendors') as readonly Record<string, unknown>[]).at(-1)!['uses'], ['chat']);
});

test('a chat tick the person took off on Models stays off — only a MISSING uses is an older build\'s', () => {
  // Unticking Chat on a moved chat-only row writes `uses: []`; an older build drops the key. Reading both as "lost" ticked
  // Chat back on the very next run, which the person's own write triggers. (our own reviewer, E4.6.)
  const unticked = { id: 'chat-a', runtime: 'claude', model: 'opus', enabled: true, plan: false, code: false, document: false, baseUrl: '', executablePath: '', vaultKeyName: 'a', uses: [] };
  const outcome = migrateLayer({
    chatPresets: [preset('a')],
    vendors: [...DEFAULT_VENDORS.map((row) => ({ ...row })), unticked],
    chatPresetsMoved: [{ presetId: 'a', runtime: 'claude', model: 'opus', name: 'Name of a', rowId: 'chat-a' }],
    marker: MIGRATED,
    backup: { keys: ['vendors', 'consultants', 'qconsultRows'], values: {} },
  });
  const rows = outcome.kind === 'migrate' ? (written(outcome.writes, 'vendors') as readonly Record<string, unknown>[] | undefined) : undefined;

  assert.deepEqual(rows?.find((row) => row['id'] === 'chat-a')?.['uses'] ?? [], [], 'the chat tick the person removed came back');
});

test('a side that keeps its own chat model has it remapped too — the user layer\'s is never written from a side', () => {
  const ownRows = DEFAULT_VENDORS.map((row) => ({ ...row }));
  const side = writes({ side: true, chatPresets: [preset('a'), preset('b')], vendors: ownRows, chatModel: 'b' });
  assert.equal(written(side, 'chatModel'), 'chat-b', 'the side would open on the first model, silently');

  const noModel = writes({ side: true, chatPresets: [preset('a', { main: true })], vendors: ownRows });
  assert.ok(!noModel.some((one) => one.key === 'chatModel' || one.key === 'chatModelName'), 'a side that holds no chat model of its own was given one');
});
