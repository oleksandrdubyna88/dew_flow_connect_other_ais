import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  CONVERSATION_VERSION,
  ConversationRecord,
  KEEP_FOR_MS,
  besideMeta,
  expired,
  fromLegacy,
  idOfMeta,
  isRecordName,
  isStale,
  metaOf,
  recordFrom,
  recordName,
  metaFrom,
  sameSource,
  sourceOfFile,
  sourceOfSession,
} from '../chatStore';
import { ChatMessage } from '../chatPage';
import { SavedTab } from '../chatTabs';

/**
 * What a stored conversation IS, and every rule over one that needs no disk.
 *
 * <p>Pure, for the reason `chatTabs.ts` was pure before it: what is written, what a damaged record
 * does, what is pruned and when are DECISIONS, and a decision inside the host is a decision no test
 * can reach. The half that needs a directory is `chatStoreFile.ts`.</p>
 */

const AT = Date.UTC(2026, 8, 12, 12, 0, 0);

const said = (role: 'you' | 'model', text: string): ChatMessage => ({ role, text });

function record(over: Partial<ConversationRecord> = {}): ConversationRecord {
  return {
    version: CONVERSATION_VERSION,
    rev: 1,
    id: 'a1',
    title: 'main',
    passage: 'the passage',
    modelId: 'gpt-5.4',
    messages: [said('you', 'why'), said('model', 'because')],
    fromSession: true,
    carryFrom: 0,
    source: sourceOfSession('9f1c-uuid'),
    workspace: 'D:\\rsd\\coai',
    createdAt: AT - 1_000,
    updatedAt: AT,
    ...over,
  };
}

// ---------------------------------------------------------------------------------------------
// The record, and what a reader may believe about one.
// ---------------------------------------------------------------------------------------------

test('a record round trips through its own writer and reader unchanged', () => {
  const mine = record();

  assert.deepEqual(recordFrom(JSON.parse(JSON.stringify(mine))), mine);
});

test('a record written before this plan reads as belonging to no tab, never to the wrong one', () => {
  // The legacy shape: everything `SavedTab` had, and none of the four fields this plan adds. It must
  // still READ — a person's transcripts are not thrown away by an upgrade — and it must never be
  // matched to a tab, because nothing in it says which tab it came from. Absent is not "this one".
  const legacy = {
    version: CONVERSATION_VERSION,
    rev: 1,
    id: 'old',
    title: 'README.md',
    passage: '',
    modelId: 'gemini-3',
    messages: [said('you', 'hello')],
    createdAt: AT,
    updatedAt: AT,
  };

  const read = recordFrom(legacy);

  assert.ok(read !== undefined, 'an older record was thrown away rather than read');
  assert.deepEqual(read!.source, { kind: 'none' }, 'a record with no source claimed one');
  assert.equal(read!.workspace, '', 'a record with no workspace claimed this one');
  assert.equal(read!.fromSession, false);
  assert.equal(read!.carryFrom, 0);
});

test('a record of a different version is discarded, never half-read', () => {
  assert.equal(recordFrom({ ...record(), version: CONVERSATION_VERSION + 1 }), undefined);
  assert.equal(recordFrom({ ...record(), version: 'one' }), undefined);
});

test('a damaged record is dropped rather than repaired', () => {
  // A transcript with a hole in it reads as a conversation the person recognises with pieces
  // missing, which is worse than one that is not there. Each of these is a record this build cannot
  // trust, and none of them is rendered.
  const bad: unknown[] = [
    { ...record(), id: '' },
    { ...record(), id: 42 },
    { ...record(), messages: 'a conversation' },
    { ...record(), messages: [{ role: 'nobody', text: 'x' }] },
    { ...record(), messages: [{ role: 'you' }] },
    { ...record(), rev: 0 },
    { ...record(), rev: -1 },
    { ...record(), rev: 1.5 },
    { ...record(), updatedAt: 'now' },
    { ...record(), carryFrom: -1 },
    { ...record(), carryFrom: 2.5 },
    { ...record(), source: { kind: 'claude' } },
    { ...record(), source: { kind: 'file', uri: '' } },
    { ...record(), source: { kind: 'elsewhere' } },
    null,
    'a record',
  ];

  for (const one of bad) {
    assert.equal(recordFrom(one), undefined, `a record this build cannot trust was read: ${JSON.stringify(one)}`);
  }
});

test('a torn file — the JSON of a write nobody finished — is nothing, not a throw', () => {
  assert.equal(recordFrom(undefined), undefined);
});

// ---------------------------------------------------------------------------------------------
// The metadata: what the picker reads, and what it must never have to open a transcript for.
// ---------------------------------------------------------------------------------------------

test('the metadata says what a row shows, so the picker never opens a transcript', () => {
  const meta = metaOf(record({ messages: [said('you', 'why'), said('model', 'because'), said('you', 'and')] }));

  assert.equal(meta.id, 'a1');
  assert.equal(meta.rev, 1);
  assert.equal(meta.title, 'main');
  assert.equal(meta.modelId, 'gpt-5.4');
  assert.equal(meta.turns, 3, 'the row would say the wrong number of turns');
  assert.equal(meta.lastLine, 'and', 'the row shows the last thing said');
  assert.deepEqual(meta.source, { kind: 'claude', sessionId: '9f1c-uuid' });
  assert.equal(meta.workspace, 'D:\\rsd\\coai');
  assert.equal(meta.updatedAt, AT);
});

test('a long last line is cut, because a picker row is one line and the transcript is not', () => {
  const meta = metaOf(record({ messages: [said('model', 'x'.repeat(400))] }));

  assert.ok(meta.lastLine.length <= 121, `a row was handed ${meta.lastLine.length} characters`);
  assert.ok(meta.lastLine.endsWith('…'), 'a cut line does not say it was cut');
});

test('a last line is ONE line — a transcript is not a row', () => {
  const meta = metaOf(record({ messages: [said('model', 'first\nsecond\nthird')] }));

  assert.equal(meta.lastLine, 'first second third');
});

test('a conversation nobody has spoken in yet has a row, and an empty last line', () => {
  const meta = metaOf(record({ messages: [] }));

  assert.equal(meta.turns, 0);
  assert.equal(meta.lastLine, '');
});

test('metadata round trips, and a damaged one is dropped like a record', () => {
  const meta = metaOf(record());

  assert.deepEqual(metaFrom(JSON.parse(JSON.stringify(meta))), meta);
  assert.equal(metaFrom({ ...meta, id: '' }), undefined);
  assert.equal(metaFrom({ ...meta, turns: -1 }), undefined);
  assert.equal(metaFrom(null), undefined);
});

// ---------------------------------------------------------------------------------------------
// `rev` — what makes two atomic writes one commit, and a lost update detectable.
// ---------------------------------------------------------------------------------------------

test('a metadata file older than its record is stale, and is regenerated rather than believed', () => {
  // The hole three vendors found independently: the record is renamed, the host dies, the metadata
  // still describes the previous turn. A picker row would show a last line that is no longer last.
  const mine = record({ rev: 4 });

  assert.equal(isStale({ ...metaOf(mine), rev: 3 }, mine), true);
  assert.equal(isStale(metaOf(mine), mine), false);
  assert.equal(
    isStale({ ...metaOf(mine), rev: 5 }, mine),
    true,
    'metadata from a LATER write than the record is just as untrustworthy — the pair is not one commit',
  );
});

test('a record and its metadata live under names derived from the id, and only from the id', () => {
  assert.equal(recordName('a1'), 'a1.json');
  assert.equal(besideMeta('a1'), 'a1.meta.json');
  assert.notEqual(recordName('a1'), besideMeta('a1'));
});

// ---------------------------------------------------------------------------------------------
// The source: what a conversation was opened FROM, and the one question the store is asked about it.
// ---------------------------------------------------------------------------------------------

test('two conversations match a tab only when their source is the same source', () => {
  assert.equal(sameSource(sourceOfSession('uuid-1'), sourceOfSession('uuid-1')), true);
  assert.equal(sameSource(sourceOfSession('uuid-1'), sourceOfSession('uuid-2')), false);
  assert.equal(sameSource(sourceOfFile('file:///a/README.md'), sourceOfFile('file:///a/README.md')), true);
  // The defect the URI exists to prevent: two READMEs in two folders are not one document.
  assert.equal(sameSource(sourceOfFile('file:///a/README.md'), sourceOfFile('file:///b/README.md')), false);
  assert.equal(sameSource(sourceOfSession('x'), sourceOfFile('x')), false);
});

test('a conversation that names no source matches NOTHING — not even another that names none', () => {
  // `none` is everything written before this plan, and everything opened from a tab whose identity
  // could not be resolved. Matching two of them to each other would hand a person somebody else's
  // conversation, which is the one failure this whole join exists to prevent.
  assert.equal(sameSource({ kind: 'none' }, { kind: 'none' }), false);
  assert.equal(sameSource({ kind: 'none' }, sourceOfSession('uuid-1')), false);
});

// ---------------------------------------------------------------------------------------------
// Retention.
// ---------------------------------------------------------------------------------------------

test('a conversation older than the window has expired; one inside it has not', () => {
  const meta = metaOf(record());

  assert.equal(expired(meta, AT), false);
  assert.equal(expired(meta, AT + KEEP_FOR_MS - 1), false);
  assert.equal(expired(meta, AT + KEEP_FOR_MS + 1), true);
});

test('the window is ninety days, and it is read from the constant rather than retyped', () => {
  assert.equal(KEEP_FOR_MS, 90 * 24 * 60 * 60 * 1000);
});

test('age is measured from the LAST update, so a conversation you came back to is not expired', () => {
  // A two-day-old conversation reopened today is a conversation in use. Measuring from `createdAt`
  // would delete exactly the ones this feature exists to bring back.
  const old = metaOf(record({ createdAt: AT - KEEP_FOR_MS * 2, updatedAt: AT }));

  assert.equal(expired(old, AT + 1_000), false);
});

// ---------------------------------------------------------------------------------------------
// The migration's mapping, which is a decision and therefore lives here.
// ---------------------------------------------------------------------------------------------

test('a memento record becomes a store record that claims no source and this workspace', () => {
  const tab: SavedTab = {
    id: 'legacy-1',
    savedAt: AT,
    title: 'main',
    passage: 'p',
    modelId: 'gemini-3',
    messages: [said('you', 'hi')],
    fromSession: true,
    carryFrom: 2,
  };

  const mine = fromLegacy(tab, 'D:\\rsd\\coai');

  assert.equal(mine.id, 'legacy-1', 'the id must survive: it is what the page hands the serializer back');
  assert.equal(mine.rev, 1);
  assert.equal(mine.version, CONVERSATION_VERSION);
  assert.deepEqual(mine.source, { kind: 'none' }, 'a migrated record invented a source it never had');
  assert.equal(mine.workspace, 'D:\\rsd\\coai');
  assert.equal(mine.updatedAt, AT, 'the record would age from the migration rather than from its own last use');
  assert.equal(mine.createdAt, AT);
  assert.deepEqual(mine.messages, tab.messages);
  assert.equal(mine.carryFrom, 2, 'the mark a person drew did not survive the move');
  assert.equal(mine.fromSession, true);
});

test('a migrated record is a record this build can read back', () => {
  const tab: SavedTab = { id: 'l2', savedAt: AT, title: 't', passage: '', modelId: 'm', messages: [] };

  assert.notEqual(recordFrom(JSON.parse(JSON.stringify(fromLegacy(tab, '')))), undefined);
});

// ---------------------------------------------------------------------------------------------
// What the code round of story A1 found. Each of these is a test before it was a fix.
// ---------------------------------------------------------------------------------------------

test('an id that is not a safe filename is not a record, however well-formed the rest is', () => {
  // codex and gemini, independently. An id becomes a PATH — `recordName(id)` is joined to the store's
  // directory — so a record on disk carrying an escaping id would have the store read and write
  // outside itself. `randomUUID()` is what mints one today, but what is READ comes from a file, and a
  // file is not a promise.
  const escapes = ['../outside', '..', '.', 'a/b', 'a:b', 'a b', 'a*b'];

  for (const id of escapes) {
    assert.equal(recordFrom({ ...record(), id }), undefined, `an id that escapes the store was accepted: ${id}`);
    assert.equal(metaFrom({ ...metaOf(record()), id }), undefined, `a metadata id that escapes was accepted: ${id}`);
  }
});

test('the names a record lives under refuse an unsafe id rather than building a path', () => {
  // Belt and braces, deliberately: the validator above is the boundary, and these two are the last
  // place before a path is handed to the filesystem. A measure applied at one of its sites is the
  // defect this family keeps writing.
  assert.throws(() => recordName('../outside'), /id/u);
  assert.throws(() => besideMeta('../outside'), /id/u);
  assert.equal(recordName('a1'), 'a1.json');
});

test('a uuid is a safe id, because a uuid is what mints one', () => {
  const uuid = '3d301f15-17c6-4788-bf94-ccd35367adc5';

  assert.equal(recordName(uuid), `${uuid}.json`);
  assert.notEqual(recordFrom({ ...record(), id: uuid }), undefined);
});

test('only this module own filenames are read as this module files', () => {
  assert.equal(isRecordName('a1.json'), true);
  assert.equal(isRecordName('a1.meta.json'), false, 'a metadata file would be read as a record');
  assert.equal(isRecordName('a1.json.4321.tmp'), false, 'an interrupted write would be read as a record');
  assert.equal(isRecordName('notes.txt'), false);

  assert.equal(idOfMeta('a1.meta.json'), 'a1');
  assert.equal(idOfMeta('a1.json'), '', 'a record was read as metadata');
});

test('metadata carries the version its record was written at, so a bump cannot leave a ghost row', () => {
  // codex: a future `CONVERSATION_VERSION` makes `recordFrom` reject an old record while `metaFrom`
  // goes on accepting its metadata — and the picker then offers a row whose transcript this build has
  // already discarded. The two evolve together or they do not evolve.
  const meta = metaOf(record());

  assert.equal(meta.version, CONVERSATION_VERSION);
  assert.equal(metaFrom({ ...meta, version: CONVERSATION_VERSION + 1 }), undefined);
  assert.equal(metaFrom({ ...meta, version: undefined }), undefined, 'a metadata file of no version was believed');
});

test('an identity that is empty is no identity, and the constructors say so', () => {
  // codex: `sourceOfSession` with an empty id produced a `claude` source carrying nothing, and two of
  // those compared EQUAL. A session that could not be resolved would then have joined every other
  // unresolved tab — the rule that nothing matches nothing, defeated by the value it excludes.
  assert.deepEqual(sourceOfSession(''), { kind: 'none' });
  assert.deepEqual(sourceOfFile(''), { kind: 'none' });
  assert.equal(sameSource(sourceOfSession(''), sourceOfSession('')), false);
  assert.equal(sameSource(sourceOfFile(''), sourceOfFile('')), false);
});
