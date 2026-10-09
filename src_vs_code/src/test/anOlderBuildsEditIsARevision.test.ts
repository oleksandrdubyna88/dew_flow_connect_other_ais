import assert from 'node:assert/strict';
import { test } from 'node:test';
import { migrateLayer, MIGRATED, restoreLayer, type CatalogLayer, type LayerWrite } from '../catalogMigration';
import { chatModelsOf, movedTo } from '../chatCatalogModels';
import type { ModelPreset } from '../chatPresets';
import { chatMove, COPIED_FIELDS, entryOf, movedRecordFrom, snapshotted, type MovedPreset } from '../chatPresetMove';
import {
  applyRevisionChoice, presetConflicts, revisionStoresReading, revisionWrites, type RevisionPorts, type RevisionStores,
} from '../chatPresetRevision';
import { chatSettingsFrom } from '../chatSettings';
import { userChatPresets } from '../modelKeys';
import { DEFAULT_VENDORS, vendorsFrom } from '../vendors';
import { afterWrites, readerOf, type SettingsFile } from './settingsFileFixture';
import { sourceOf } from './sourceReading';

/**
 * Epic 5 prerequisite (a) of research/PLAN_one_model_catalog.md, R7: a chat preset an OLDER build edits after the move is a
 * conflicting revision of the same preset — never a second row, never silently lost. The record keeps a snapshot of
 * everything the move copied; a preset that differs from it is raised on Chat with two choices, and the choice is
 * written to the record, so it holds across a reload.
 */

type RawRow = Readonly<Record<string, unknown>>;

const BASE: readonly RawRow[] = DEFAULT_VENDORS.map((row) => ({ ...row }));

/** A Team-server preset with every field the move copies set, so an edit of any one of them has something to change. */
const preset = (extra: Partial<ModelPreset> = {}): ModelPreset => ({
  id: 'p-1', name: 'Deep', runtime: 'remote', model: 'gpt-5', main: false, executablePath: 'C:\\coai\\codex.exe',
  baseUrl: 'https://llm.example.test/v1', startingPrompt: 'You review APIs.', teamServerId: 'acme', remoteVendor: 'codex', ...extra,
});

/** One edit per field a person can change on a preset in an older build. */
const EDITS: readonly Partial<ModelPreset>[] = [
  { name: 'Deeper' }, { runtime: 'codex' }, { model: 'gpt-5-mini' }, { baseUrl: 'https://other.example.test/v1' },
  { executablePath: 'D:\\tools\\codex.exe' }, { startingPrompt: 'You review schemas.' }, { teamServerId: 'beta' }, { remoteVendor: 'gemini' },
];

/** The row the move writes for one preset — what `rowOf` copies, observed rather than restated. */
const rowFor = (one: ModelPreset): RawRow => chatMove({ presets: [one], rows: BASE, record: [] }).rows.at(-1)!;

/** The settings once this build has moved `moved`. */
function afterMove(...moved: readonly ModelPreset[]): SettingsFile {
  const run = chatMove({ presets: moved, rows: BASE, record: [] });

  return { vendors: run.rows, chatPresetsMoved: run.record };
}

/** What a side reads, given the presets as the chat reads them now. */
const storesOf = (file: SettingsFile, presets: readonly ModelPreset[]): RevisionStores => revisionStoresReading(presets, (key) => file[key]);

/** Each write landed, then the file read back as a window that reloads reads it. */
const applied = afterWrites;

const rowIn = (file: SettingsFile, id: string): RawRow | undefined => storesOf(file, []).rows.find((row) => row['id'] === id);

/** The same record with every snapshot taken out — what a build before R7 wrote. */
function withoutSnapshots(file: SettingsFile): SettingsFile {
  return { ...file, chatPresetsMoved: movedRecordFrom(file['chatPresetsMoved']).map(({ copied: _copied, ...five }) => five) };
}

test('the snapshot holds exactly what the move copies — every row field a preset\'s own fields reach, the row id aside', () => {
  const base = rowFor(preset());
  const variants = [preset({ id: 'q-2' }), ...EDITS.map((edit) => preset(edit))];
  const reached = new Set(variants.flatMap((one) => {
    const row = rowFor(one);

    return Object.keys({ ...base, ...row }).filter((key) => key !== 'id' && JSON.stringify(row[key]) !== JSON.stringify(base[key]));
  }));

  assert.deepEqual([...reached].sort(), [...COPIED_FIELDS].sort());
});

test('a new entry carries a snapshot of everything the move copied', () => {
  const run = chatMove({ presets: [preset()], rows: BASE, record: [] });
  const row = run.rows.at(-1)!;

  assert.deepEqual(run.record[0]?.copied, Object.fromEntries(COPIED_FIELDS.map((field) => [field, row[field]])));
});

test('an older build\'s edit of EACH copied field is a revision: no second row, the row untouched, the conflict names the field', () => {
  const moved = afterMove(preset());
  for (const field of COPIED_FIELDS) {
    if (field === 'vaultKeyName') {
      // The key name IS the preset's id, and the id is what matches a preset to its entry: no edit of one preset reaches it.
      continue;
    }
    const edit = EDITS.find((one) => rowFor(preset(one))[field] !== rowFor(preset())[field]);
    assert.ok(edit !== undefined, `no edit in the fixture changes ${field}`);
    const edited = preset(edit);
    const run = chatMove({ presets: [edited], rows: storesOf(moved, []).rows, record: movedRecordFrom(moved['chatPresetsMoved']) });

    assert.deepEqual(run.rows, moved['vendors'], `${field}: the edit made a second row, or touched the row`);
    assert.equal(run.changed, false, `${field}: the edit was moved`);
    const conflicts = presetConflicts(storesOf(moved, [edited]));
    assert.deepEqual(conflicts.map((one) => [one.presetId, one.rowId]), [['p-1', 'chat-p-1']], `${field}: no conflict raised`);
    assert.ok(conflicts[0]!.fields.some((one) => one.field === field), `${field}: the conflict does not name it`);
  }
});

test('the conflict shows the row as it is and the preset\'s edited value, field by field', () => {
  const [conflict] = presetConflicts(storesOf(afterMove(preset()), [preset({ executablePath: 'D:\\tools\\codex.exe' })]));

  assert.deepEqual(conflict, {
    presetId: 'p-1', rowId: 'chat-p-1', rowName: 'Deep',
    fields: [{ field: 'executablePath', row: 'C:\\coai\\codex.exe', edited: 'D:\\tools\\codex.exe' }],
  });
});

test('Use the edited values: the row takes them, written before the record, and the conflict stays gone after a reload', () => {
  const moved = afterMove(preset());
  const edited = preset({ executablePath: 'D:\\tools\\codex.exe', startingPrompt: undefined });
  const writes = revisionWrites('use', 'p-1', storesOf(moved, [edited]));
  const saved = applied(moved, writes);

  assert.deepEqual(writes.map((one) => one.key), ['vendors', 'chatPresetsMoved'], 'a choice stopped between its writes must be asked again, never lost');
  assert.equal(rowIn(saved, 'chat-p-1')?.['executablePath'], 'D:\\tools\\codex.exe');
  assert.ok(!('chatStartingPrompt' in rowIn(saved, 'chat-p-1')!), 'a starting text the edit cleared is still on the row');
  assert.equal(rowIn(saved, 'chat-p-1')?.['name'], 'Deep', 'a field the edit did not touch was rewritten');
  assert.deepEqual(presetConflicts(storesOf(saved, [edited])), []);
  assert.equal(chatMove({ presets: [edited], rows: storesOf(saved, []).rows, record: storesOf(saved, []).record }).changed, false, 'the next run moved it');
});

test('Keep the row: the row unchanged, and the conflict stays gone after a reload', () => {
  const moved = afterMove(preset());
  const edited = preset({ name: 'Deeper', model: 'gpt-5-mini' });
  const writes = revisionWrites('keep', 'p-1', storesOf(moved, [edited]));
  const saved = applied(moved, writes);

  assert.deepEqual(writes.map((one) => one.key), ['chatPresetsMoved']);
  assert.deepEqual(rowIn(saved, 'chat-p-1'), rowIn(moved, 'chat-p-1'));
  assert.deepEqual(presetConflicts(storesOf(saved, [edited])), []);
  // The entry's own fingerprint follows, so an older build reading the record does not move the preset a second time.
  assert.deepEqual(storesOf(saved, []).record.map(({ presetId, runtime, model, name, rowId }) => ({ presetId, runtime, model, name, rowId })),
    [{ presetId: 'p-1', runtime: 'remote', model: 'gpt-5-mini', name: 'Deeper', rowId: 'chat-p-1' }]);
});

test('a later, DIFFERENT edit raises the conflict again; the same edit does not', () => {
  const first = preset({ model: 'gpt-5-mini' });
  const kept = applied(afterMove(preset()), revisionWrites('keep', 'p-1', storesOf(afterMove(preset()), [first])));

  assert.deepEqual(presetConflicts(storesOf(kept, [first])), []);
  const again = presetConflicts(storesOf(kept, [preset({ model: 'gpt-5-mini', executablePath: 'D:\\tools\\codex.exe' })]));
  assert.deepEqual(again.flatMap((one) => one.fields.map((field) => field.field)), ['executablePath']);
});

test('a choice for a preset with no conflict writes nothing — a stale page, or one answered in another window', () => {
  const moved = afterMove(preset());

  assert.deepEqual(revisionWrites('use', 'p-1', storesOf(moved, [preset()])), []);
  assert.deepEqual(revisionWrites('keep', 'nobody', storesOf(moved, [preset({ name: 'Deeper' })])), []);
});

test('an entry written before the snapshot takes it from its row: an older-build edit on disk is raised, an unedited preset is not', () => {
  const legacy = withoutSnapshots(afterMove(preset()));

  assert.deepEqual(presetConflicts(storesOf(legacy, [preset()])), []);
  const raised = presetConflicts(storesOf(legacy, [preset({ startingPrompt: 'You review schemas.' })]));
  assert.deepEqual(raised.flatMap((one) => one.fields), [{ field: 'chatStartingPrompt', row: 'You review APIs.', edited: 'You review schemas.' }]);
});

test('the run writes that snapshot into the record — taken from the row — and adds no row', () => {
  const legacy = withoutSnapshots(afterMove(preset()));
  const run = chatMove({ presets: [preset({ model: 'gpt-5-mini' })], rows: storesOf(legacy, []).rows, record: storesOf(legacy, []).record });
  const row = rowIn(legacy, 'chat-p-1')!;

  assert.equal(run.changed, true, 'the snapshot was not written');
  assert.deepEqual(run.rows, legacy['vendors']);
  assert.deepEqual(run.record[0]?.copied, Object.fromEntries(COPIED_FIELDS.map((field) => [field, row[field]])));
});

test('a deleted row or a deleted preset raises nothing, throws nothing, and leaves the entry as it is', () => {
  const legacy = withoutSnapshots(afterMove(preset()));
  const record = storesOf(legacy, []).record;
  const noRow: SettingsFile = { ...legacy, vendors: BASE };

  assert.deepEqual(presetConflicts(storesOf(noRow, [preset({ name: 'Deeper' })])), []);
  assert.deepEqual(chatMove({ presets: [preset({ name: 'Deeper' })], rows: BASE, record }), { rows: BASE, record, changed: false }, 'a deleted row came back, or the entry changed');
  assert.deepEqual(presetConflicts(storesOf(legacy, [])), []);
  assert.deepEqual(chatMove({ presets: [], rows: storesOf(legacy, []).rows, record }).record, record, 'an entry whose preset is gone was rewritten');
});

test('a record epic 4 wrote with two entries for one id: the move, the conflict and the legacy conversation id all take the NEWEST', () => {
  const rows = [...BASE, rowFor(preset()), { ...rowFor(preset({ model: 'gpt-5-mini' })), id: 'chat-p-1-2' }];
  const record: readonly MovedPreset[] = [
    { presetId: 'p-1', runtime: 'remote', model: 'gpt-5', name: 'Deep', rowId: 'chat-p-1' },
    { presetId: 'p-1', runtime: 'remote', model: 'gpt-5-mini', name: 'Deep', rowId: 'chat-p-1-2' },
  ];
  const file: SettingsFile = { vendors: rows, chatPresetsMoved: record };

  assert.equal(movedTo('p-1', record), 'chat-p-1-2', 'a legacy conversation id resumed on the revision the person edited away');
  assert.deepEqual(presetConflicts(storesOf(file, [preset({ model: 'gpt-5-mini' })])), []);
  assert.deepEqual(presetConflicts(storesOf(file, [preset({ model: 'o4' })])).map((one) => one.rowId), ['chat-p-1-2']);
  assert.equal(chatMove({ presets: [preset({ model: 'o4' })], rows, record }).rows.length, rows.length, 'a third row');
});

test('the chat lists an edited preset\'s ROW, never the edited preset beside it', () => {
  const moved = afterMove(preset());
  const listed = chatModelsOf([preset({ model: 'gpt-5-mini' })], vendorsFrom([...storesOf(moved, []).rows]), storesOf(moved, []).record);

  assert.deepEqual(listed.map((one) => one.id).filter((id) => id === 'p-1' || id.startsWith('chat-')), ['chat-p-1']);
});

/** One migration of the user layer, which must write. */
function writesOf(layer: CatalogLayer): readonly LayerWrite[] {
  const outcome = migrateLayer(layer);
  assert.equal(outcome.kind, 'migrate', `the run did not migrate: ${JSON.stringify(outcome)}`);

  return outcome.kind === 'migrate' ? outcome.writes : [];
}

const writtenIn = (all: readonly LayerWrite[], key: LayerWrite['key']): unknown => all.find((one) => one.key === key)?.value;

test('a record written before the snapshot, with a remap still owed, gets both in one run', () => {
  const raw = { id: 'a', name: 'A', runtime: 'claude', model: 'opus' };
  const first = writesOf({ chatPresets: [raw], chatModel: 'a' });
  const legacy = withoutSnapshots({ chatPresetsMoved: writtenIn(first, 'chatPresetsMoved') });
  const resumed = writesOf({
    chatPresets: [raw], vendors: writtenIn(first, 'vendors'), chatPresetsMoved: legacy['chatPresetsMoved'], chatModel: 'a',
    marker: MIGRATED, backup: writtenIn(first, 'migratedFrom'),
  });

  assert.equal(writtenIn(resumed, 'chatModel'), 'chat-a', 'the interrupted run\'s chat model was left on the old preset id');
  assert.deepEqual(movedRecordFrom(writtenIn(resumed, 'chatPresetsMoved')).map((one) => one.copied?.name), ['A'], 'the snapshot was not written');
});

test('a revision changes nothing in the migration, and the restore still clears the record', () => {
  const raw = { id: 'a', name: 'A', runtime: 'claude', model: 'opus' };
  const first = writesOf({ chatPresets: [raw] });
  const layer: CatalogLayer = {
    chatPresets: [{ ...raw, executablePath: 'D:\\tools\\claude.exe' }], vendors: writtenIn(first, 'vendors'),
    chatPresetsMoved: writtenIn(first, 'chatPresetsMoved'), marker: MIGRATED, backup: writtenIn(first, 'migratedFrom'),
  };

  assert.equal(migrateLayer(layer).kind, 'unchanged');
  const restore = restoreLayer(layer);
  const record = restore.kind === 'restore' ? restore.writes.find((one) => one.key === 'chatPresetsMoved') : undefined;
  assert.ok(record !== undefined, 'the restore did not touch the record');
  assert.equal(record.value, undefined, 'the record survived the restore');
});

// ---------------------------------------------------------------- R7's code round (2026-10-07), findings 2–8

/** A record whose index reads are counted — what "copies nothing" and "one pass" are measured by. */
function counted(entries: readonly MovedPreset[]): { readonly record: readonly MovedPreset[]; readonly reads: () => number } {
  let reads = 0;
  const record = new Proxy([...entries], {
    get(target, key, receiver): unknown {
      if (typeof key === 'string' && /^\d+$/u.test(key)) {
        reads += 1;
      }

      return Reflect.get(target, key, receiver);
    },
  });

  return { record, reads: () => reads };
}

/** `n` presets, each moved into its own row, with the snapshot the move writes — or, `legacy`, without one. */
function moved(n: number, legacy = false): RevisionStores {
  const presets = Array.from({ length: n }, (_, index) => preset({ id: `p-${index}`, name: `P ${index}` }));
  const run = chatMove({ presets, rows: BASE, record: [] });

  return { presets, rows: run.rows, record: legacy ? run.record.map(({ copied: _copied, ...five }) => five) : run.record, chatModel: '' };
}

test('the newest entry is read from the record\'s end, copying nothing (finding 3)', () => {
  const { record, reads } = counted(moved(1000).record);

  assert.equal(entryOf('p-999', record)?.rowId, 'chat-p-999');
  assert.ok(reads() <= 2, `one lookup read ${reads()} entries — the record was copied`);
});

test('the conflicts and the snapshot pass each read the record once, not once per preset (finding 8)', () => {
  const settled = moved(300);
  const conflicts = counted(settled.record);
  presetConflicts({ ...settled, record: conflicts.record });
  assert.ok(conflicts.reads() <= 3000, `300 presets read the record ${conflicts.reads()} times`);

  const legacy = moved(300, true);
  const snapshots = counted(legacy.record);
  snapshotted(snapshots.record, legacy.presets, legacy.rows);
  assert.ok(snapshots.reads() <= 3000, `300 entries without a snapshot read the record ${snapshots.reads()} times`);
});

test('ONE implementation of "the newest entry": every reader calls entryOf, none reverses the record (finding 2)', () => {
  for (const file of ['catalogChatStep.ts', 'chatCatalogModels.ts', 'chatPresetRevision.ts', 'chatPresetMove.ts']) {
    assert.doesNotMatch(sourceOf(file), /\.reverse\(\)/u, `${file} reverses the record — a second "newest"`);
  }
  assert.match(sourceOf('catalogChatStep.ts'), /entryOf\(saved, /u, 'recordedEntryOf does not read through entryOf');
});

test('Use the edited values keeps coai.chatModelName right when the chat opens on that row and its model changed (finding 5)', () => {
  const file: SettingsFile = { ...afterMove(preset()), chatModel: 'chat-p-1', chatModelName: 'gpt-5' };
  const edited = preset({ model: 'gpt-5-mini' });
  const writes = revisionWrites('use', 'p-1', storesOf(file, [edited]));

  assert.deepEqual(writes.map((one) => one.key), ['vendors', 'chatModelName', 'chatPresetsMoved']);
  assert.equal(writes.find((one) => one.key === 'chatModelName')?.value, 'gpt-5-mini');
  assert.ok(!revisionWrites('keep', 'p-1', storesOf(file, [edited])).some((one) => one.key === 'chatModelName'), 'Keep the row changed the model');
  assert.ok(!revisionWrites('use', 'p-1', storesOf({ ...file, chatModel: 'codex' }, [edited])).some((one) => one.key === 'chatModelName'),
    'the model name of a chat that opens on another row was written');
  assert.ok(!revisionWrites('use', 'p-1', storesOf(file, [preset({ executablePath: 'D:\\x.exe' })])).some((one) => one.key === 'chatModelName'),
    'the model name was written though the model did not change');
});

/** The window a choice is made in, held in memory: what it saved, and what it was refused. */
function windowOver(file: SettingsFile, presets: readonly ModelPreset[], refuse = ''): { ports: RevisionPorts; saved: string[]; refused: string[] } {
  const saved: string[] = [];
  const refused: string[] = [];
  const ports: RevisionPorts = {
    presets: () => presets,
    reader: () => (key) => file[key],
    save: (key) => (key === refuse ? Promise.reject(new Error(`refused ${key}`)) : Promise.resolve().then(() => { saved.push(key); })),
    refused: (key) => { refused.push(key); },
    turn: (work) => work(),
  };

  return { ports, saved, refused };
}

test('a choice always redraws — one on a conflict already settled ends its busy mark and drops the stale card (finding 7)', async () => {
  const file = afterMove(preset());
  const stale = windowOver(file, [preset()]);

  assert.equal(await applyRevisionChoice({ presetId: 'p-1', choice: 'use' }, stale.ports), true, 'a settled conflict\'s card stays on Chat');
  assert.deepEqual(stale.saved, []);

  const refusing = windowOver(file, [preset({ name: 'Deeper' })], 'vendors');
  assert.equal(await applyRevisionChoice({ presetId: 'p-1', choice: 'use' }, refusing.ports), true);
  assert.deepEqual([refusing.saved, refusing.refused], [[], ['vendors']], 'a refused row went on to write the record');
});

test('Chat reads the presets the MOVE reads — a workspace value raises nothing the choice cannot settle (finding 6)', () => {
  const file = afterMove(preset());
  const workspace = { ...file, chatModelPresets: [preset({ name: 'A workspace\'s name' })] };

  assert.deepEqual(chatSettingsFrom(readerOf(workspace), [preset()]).conflicts, [], 'Chat raised the workspace layer\'s preset');
  assert.deepEqual(chatSettingsFrom(readerOf(workspace), [preset({ model: 'o4' })]).conflicts.map((one) => one.presetId), ['p-1']);
  const layered = { get: (): unknown => [preset({ name: 'ws' })], inspect: () => ({ globalValue: [preset()], workspaceValue: [preset({ name: 'ws' })] }) };
  assert.deepEqual(userChatPresets(layered), [preset()], 'the one reader took the workspace layer');
});
