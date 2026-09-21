import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  CallEnd, Calls, columnOf, distinct, preparedSentence, sideSentence, stillWanted, theRightSymbol,
} from '../callHierarchy';
import { askCalls, AskedAbout, Editor, Preparation } from '../callHierarchyAsk';

/**
 * Story 3.3's decisions, as values.
 *
 * <p>Every one of these encodes something the plan round found or a measurement established, and the
 * two that matter most are the two ways to be CONFIDENTLY WRONG: counting the enclosing class
 * because the column was not found, and counting an overload because a name was taken for an
 * identity.</p>
 */

const END = (name: string, file = 'src/A.ts', detail = ''): CallEnd =>
  ({ name, file, line: 3, character: 4, detail });

// --------------------------------------------------------------------------------------------
// The column, which the measurement made a guard rather than a convenience.
// --------------------------------------------------------------------------------------------

test('the column is found on the line, and column 0 is never assumed', () => {
  // Measured: asking at 0 on this line prepares `Totals`, the CLASS, and would have counted its
  // callers under the method's name.
  assert.equal(columnOf('    public counted(): number { return 1; }', 'counted'), 11);
  assert.equal(columnOf('export function called(): number { return 1; }', 'called'), 16);
});

test('a name that is only PART of another word is not the symbol', () => {
  assert.equal(columnOf('    public recounted(): number { return 1; }', 'counted'), -1,
    'finding `counted` inside `recounted` would ask about a method that is not there');
  assert.equal(columnOf('    public counted_x(): number { return 1; }', 'counted'), -1);
  assert.equal(columnOf('  const x = precounted + counted;', 'counted'), 25,
    'and the SECOND occurrence is taken when the first is inside a word');
});

test('a line that does not hold the symbol at all means the method moved', () => {
  assert.equal(columnOf('    public somethingElse(): number { return 1; }', 'counted'), -1);
  assert.equal(columnOf('', 'counted'), -1);
  assert.equal(columnOf('counted', ''), -1, 'a row with no symbol name can ask nothing');
});

// --------------------------------------------------------------------------------------------
// The identity, which a name alone cannot establish.
// --------------------------------------------------------------------------------------------

test('exactly one match is the symbol; an overload pair is refused rather than guessed at', () => {
  assert.equal(theRightSymbol([{ name: 'counted', detail: '(): number' }], 'counted'), 0);
  assert.equal(
    theRightSymbol([{ name: 'counted', detail: '(): number' }, { name: 'counted', detail: '(x: number): number' }], 'counted'),
    -1,
    'nothing in a review row can tell two overloads apart, so neither may be counted');
  assert.equal(theRightSymbol([{ name: 'Totals', detail: '' }], 'counted'), -1,
    'the enclosing class is exactly what column 0 prepares, and this is the guard that catches it');
  assert.equal(theRightSymbol([], 'counted'), -1);
});

test('WHICH of the prepared items matched is the answer, not merely that one did', () => {
  // The worst finding of the code round, found independently by three reviewers: the first draft
  // returned a boolean and then asked about `items[0]`. A provider that answers
  // [the enclosing class, the method] would have counted the CLASS's callers under the METHOD's
  // name — a number that is wrong and looks right, which is the one outcome this story forbids.
  assert.equal(
    theRightSymbol([{ name: 'Totals', detail: '' }, { name: 'counted', detail: '(): number' }], 'counted'),
    1,
    'the handle must be the one beside the item that matched');
});

// --------------------------------------------------------------------------------------------
// What a person is told — and zero is never confused with silence.
// --------------------------------------------------------------------------------------------

test('every reason becomes a sentence, and none of them prints a state TOKEN', () => {
  // The rule is about our vocabulary, not about English: "the method moved or was renamed" is a
  // sentence a person reads, while `no-provider` is a label only this code understands. The first
  // version of this test conflated them and went red on its own good sentence.
  for (const why of ['moved', 'gone', 'no-provider', 'failed'] as const) {
    const said = preparedSentence(why);

    assert.ok(said.length > 20, `${why} must say something a person can act on`);
    assert.doesNotMatch(said, /[a-z]+-[a-z]+|[a-z]+_[a-z]+/u, `${why} printed a token rather than a sentence`);
  }
  assert.equal(preparedSentence('ok'), '', 'a good answer says nothing; the NUMBER is the answer');
});

test('no provider says nobody could be ASKED, which is not nobody calling it', () => {
  assert.match(preparedSentence('no-provider'), /not the same as nobody calling it/u);
});

test('zero is a real answer and reads as one; a failure reads as a failure', () => {
  assert.match(sideSentence({ asked: true, failed: false, ends: [] }, 'calls this'),
    /nothing calls this, in the current checkout/u);
  assert.match(sideSentence({ asked: true, failed: false, ends: [END('a')] }, 'calls this'),
    /1 method calls this, in the current checkout/u);
  assert.match(sideSentence({ asked: true, failed: false, ends: [END('a'), END('b')] }, 'calls this'),
    /2 methods calls this/u);
  assert.match(sideSentence({ asked: true, failed: true, ends: [] }, 'calls this'),
    /could not say/u);
  assert.equal(sideSentence({ asked: false, failed: false, ends: [] }, 'calls this'), '');
});

test('every count says WHICH checkout it is about, because the row is about another commit', () => {
  for (const ends of [[], [END('a')]]) {
    assert.match(sideSentence({ asked: true, failed: false, ends }, 'calls this'),
      /in the current checkout/u,
      'a call hierarchy answers over the folder this window has open, never over head_sha');
  }
});

// --------------------------------------------------------------------------------------------
// Distinct methods, not call sites.
// --------------------------------------------------------------------------------------------

test('one method calling twice is ONE caller; two methods of one name in two files are two', () => {
  assert.equal(distinct([END('uses'), END('uses'), END('other')]).length, 2);
  assert.equal(distinct([END('uses', 'src/A.ts'), END('uses', 'src/B.ts')]).length, 2);
  assert.equal(distinct([END('uses', 'src/A.ts', '(): void'), END('uses', 'src/A.ts', '(x: number): void')]).length, 2,
    'two overloads of one name in one file are two methods');
});

// --------------------------------------------------------------------------------------------
// Which answer may be applied — the half a generation alone cannot do.
// --------------------------------------------------------------------------------------------

const answer = (over: Partial<Calls> = {}): Calls => ({
  findingId: 7, attempt: 'a1', prepared: 'ok',
  incoming: { asked: true, failed: false, ends: [] },
  outgoing: { asked: true, failed: false, ends: [] },
  ...over,
});

test('an answer is applied only to the row that asked, and only for the attempt it awaits', () => {
  const waiting = new Map([[7, 'a2']]);

  assert.equal(stillWanted(answer({ attempt: 'a2' }), waiting), true);
  // The defect a reviewer found: a timed-out request resolving after the person pressed again.
  assert.equal(stillWanted(answer({ attempt: 'a1' }), waiting), false,
    'a superseded attempt must not overwrite the retry that replaced it');
  assert.equal(stillWanted(answer({ findingId: 8, attempt: 'a2' }), waiting), false,
    'and never a row that did not ask');
  assert.equal(stillWanted(answer(), new Map()), false, 'a row waiting for nothing takes nothing');
});

// --------------------------------------------------------------------------------------------
// The flow, with the editor as a parameter.
// --------------------------------------------------------------------------------------------

const ABOUT: AskedAbout = { findingId: 7, attempt: 'a1', file: 'src/A.ts', line: 2, symbolName: 'counted' };

const editorThat = (over: Partial<Editor> = {}): Editor => ({
  lineText: async () => '    public counted(): number { return 1; }',
  prepare: async (): Promise<Preparation> => ({ items: [{ name: 'counted', detail: '' }], handles: ['H'] }),
  incoming: async () => [END('uses')],
  outgoing: async () => [END('helper')],
  ...over,
});

test('a good press counts both directions, independently', async () => {
  const calls = await askCalls(editorThat(), ABOUT);

  assert.equal(calls.prepared, 'ok');
  assert.equal(calls.incoming.ends.length, 1);
  assert.equal(calls.outgoing.ends.length, 1);
  assert.equal(calls.attempt, 'a1', 'the answer carries the attempt it belongs to');
});

test('a direction that fails does not take the other down with it', async () => {
  const calls = await askCalls(editorThat({ incoming: async () => { throw new Error('provider fell over'); } }), ABOUT);

  assert.equal(calls.prepared, 'ok');
  assert.equal(calls.incoming.failed, true);
  assert.equal(calls.outgoing.failed, false, 'one provider failure must not lose the other answer');
  assert.equal(calls.outgoing.ends.length, 1);
});

test('the provider is asked about the item that MATCHED, whatever its position', async () => {
  let askedAbout: unknown;
  await askCalls(
    editorThat({
      prepare: async () => ({
        items: [{ name: 'Totals', detail: '' }, { name: 'counted', detail: '' }],
        handles: ['the class', 'the method'],
      }),
      incoming: async (handle) => { askedAbout = handle; return []; },
    }),
    ABOUT);

  assert.equal(askedAbout, 'the method',
    'asking about handles[0] would count the enclosing class under the method’s name');
});

test('the symbol is proved BEFORE either direction is asked', async () => {
  let asked = 0;
  const calls = await askCalls(
    editorThat({
      prepare: async () => ({ items: [{ name: 'Totals', detail: '' }], handles: ['H'] }),
      incoming: async () => { asked += 1; return []; },
      outgoing: async () => { asked += 1; return []; },
    }),
    ABOUT);

  assert.equal(calls.prepared, 'moved');
  assert.equal(asked, 0, 'a count about the wrong symbol is the failure this story is most afraid of');
});

test('a file the checkout does not have is GONE, not a missing provider', async () => {
  const calls = await askCalls(editorThat({ lineText: async () => undefined }), ABOUT);

  assert.equal(calls.prepared, 'gone');
});

test('an empty preparation is NO PROVIDER, which is not zero callers', async () => {
  const calls = await askCalls(editorThat({ prepare: async () => ({ items: [], handles: [] }) }), ABOUT);

  assert.equal(calls.prepared, 'no-provider');
  assert.equal(calls.incoming.asked, false, 'nothing was asked, so nothing may be shown as a count');
});

test('a preparation that never answers is FAILED, and bounded', async () => {
  const started = Date.now();
  const calls = await askCalls(
    editorThat({ prepare: () => new Promise(() => { /* never */ }) }), ABOUT, 50);

  assert.equal(calls.prepared, 'failed');
  assert.ok(Date.now() - started < 2_000, 'the wait is bounded, so a row is never stuck');
});

test('a direction that never answers is a failed DIRECTION, not a failed press', async () => {
  const calls = await askCalls(
    editorThat({ incoming: () => new Promise(() => { /* never */ }) }), ABOUT, 50);

  assert.equal(calls.prepared, 'ok');
  assert.equal(calls.incoming.failed, true);
  assert.equal(calls.outgoing.failed, false);
});

test('a row with no recorded line, or no symbol name, asks nothing at all', async () => {
  let touched = 0;
  const watching = editorThat({ lineText: async () => { touched += 1; return 'x'; } });

  assert.equal((await askCalls(watching, { ...ABOUT, line: 0 })).prepared, 'moved');
  assert.equal((await askCalls(watching, { ...ABOUT, symbolName: '' })).prepared, 'moved');
  assert.equal(touched, 0, 'a row that cannot be asked about must not cost a provider call');
});
