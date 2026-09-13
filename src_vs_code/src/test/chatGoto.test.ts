import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ConversationMeta, sourceOfFile, sourceOfSession } from '../chatStore';
import { IndexState } from '../chatStoreCache';
import { Goto, GotoAsked, UNNAMED_TAB, bindable, goto } from '../chatGoto';

/**
 * Which conversation belongs to the tab somebody is looking at — decided without a host.
 *
 * <p>The matrix is the test: tab kind × what this window already holds × how many saved
 * conversations carry this tab's source × whether its own session is in doubt × what state the index
 * is in. Every answer is a value, so all of it is reachable here, which is the reason the decision is
 * a module of its own rather than a branch inside a command.</p>
 */

const ROOTS = ['D:\\rsd\\one', 'D:\\rsd\\two'];
const HERE = 'file:///D:/rsd/one/main.ts';
const HERE_PATH = 'D:\\rsd\\one\\main.ts';
const READY: IndexState = { kind: 'ready', at: 1 };

function meta(over: Partial<ConversationMeta> = {}): ConversationMeta {
  return {
    version: 1,
    id: 'a1',
    rev: 3,
    title: 'Why the lock is fenced',
    modelId: 'gpt-5.4',
    turns: 4,
    lastLine: 'because a read and a delete are two operations',
    source: sourceOfFile(HERE),
    workspace: 'D:\\rsd\\one',
    updatedAt: 10,
    ...over,
  };
}

const asked = (over: Partial<GotoAsked> = {}): GotoAsked => ({
  tab: { kind: 'document', label: 'main.ts', path: HERE_PATH },
  live: '',
  source: sourceOfFile(HERE),
  ambiguous: false,
  candidates: [],
  roots: ROOTS,
  fallback: 'D:\\rsd\\one',
  caseBlind: true,
  index: READY,
  ...over,
});

const kindOf = (answer: Goto): string => answer.kind;

test('what this window already holds is revealed, and nothing is read to find out', () => {
  // The ten-tabs case, and the reason the whole feature was asked for. It is decided on the TAB
  // rather than on a source, so it is right even for a conversation that has none.
  assert.deepEqual(goto(asked({ live: 'open-1' })), { kind: 'reveal', id: 'open-1' });
  // Even when the store would have answered something else — a live conversation wins over every
  // saved one, because it is the one in front of the person.
  assert.deepEqual(goto(asked({ live: 'open-1', candidates: [meta({ id: 'saved' })] })), { kind: 'reveal', id: 'open-1' });
  // And even when the tab is one no conversation could belong to: if this window holds a conversation
  // for it, it holds one.
  assert.deepEqual(goto(asked({ live: 'open-1', tab: { kind: 'other', label: 'Terminal', path: '' } })), { kind: 'reveal', id: 'open-1' });
});

test('a tab nothing can belong to gets the whole list — never an error, never silence', () => {
  // The operator's decision, in as many words: a graceful fall back to the global list, because an
  // error or silence from a terminal is the mark of unfinished software.
  assert.deepEqual(goto(asked({ tab: { kind: 'other', label: 'Terminal', path: '' } })), { kind: 'everything' });
});

test('exactly one saved conversation for this tab is reopened', () => {
  const answer = goto(asked({ candidates: [meta()] }));

  assert.equal(answer.kind, 'reopen');
  assert.equal(answer.kind === 'reopen' ? answer.meta.id : '', 'a1');
});

test('nothing saved for an eligible tab offers to start one, named after it', () => {
  const answer = goto(asked());

  assert.deepEqual(answer, { kind: 'start', offer: 'main.ts' });
});

test('an untitled document with no name still has something to call the offer', () => {
  // Eligible by kind, and nameless — the offer would otherwise have ended in nothing. (codex.)
  const answer = goto(asked({ tab: { kind: 'document', label: '   ', path: '' }, source: sourceOfFile('untitled:Untitled-1') }));

  assert.deepEqual(answer, { kind: 'start', offer: UNNAMED_TAB });
});

// ---------------------------------------------------------------------------------------------
// The workspace: a tab's OWN root, not membership in the window's list.
// ---------------------------------------------------------------------------------------------

test('a conversation filed under ANOTHER root of the same window is not this tab’s', () => {
  // The hole three reviewers found. With two roots open, membership in the window's list would let a
  // record filed under the first attach to a tab under the second — silently handing somebody the
  // wrong project's conversation.
  const there = meta({ workspace: 'D:\\rsd\\two' });

  assert.deepEqual(goto(asked({ candidates: [there] })), { kind: 'start', offer: 'main.ts' });
  // And the same record IS this tab's when the tab is the one under that root.
  const tabThere = { kind: 'document' as const, label: 'main.ts', path: 'D:\\rsd\\two\\main.ts' };
  const uriThere = 'file:///D:/rsd/two/main.ts';
  const answer = goto(asked({
    tab: tabThere,
    source: sourceOfFile(uriThere),
    candidates: [meta({ source: sourceOfFile(uriThere), workspace: 'D:\\rsd\\two' })],
  }));
  assert.equal(answer.kind, 'reopen');
});

test('a tab under NO root falls back to the same answer story C1 files such a conversation under', () => {
  // The two halves must agree by construction. C1 files a conversation whose file is outside every
  // root under the first root; so this tab belongs there too, and they meet.
  const outside = { kind: 'document' as const, label: 'notes.md', path: 'D:\\elsewhere\\notes.md' };
  const outsideUri = 'file:///D:/elsewhere/notes.md';
  const answer = goto(asked({
    tab: outside,
    source: sourceOfFile(outsideUri),
    candidates: [meta({ source: sourceOfFile(outsideUri), workspace: 'D:\\rsd\\one' })],
  }));

  assert.equal(answer.kind, 'reopen', 'a conversation about a file outside every root could never be found again');
});

// ---------------------------------------------------------------------------------------------
// A source of `none` matches nothing — the line this whole story rests on.
// ---------------------------------------------------------------------------------------------

test('a tab with no resolvable source matches NOTHING, however full the store is of sourceless records', () => {
  // Every conversation written before story C1 carries `source: none`, and `sameSource` answers false
  // for `none` against `none`. That one line is what keeps this feature from handing a person
  // somebody else's conversation — a title is never a key, and neither is an absence.
  const old = [meta({ id: 'old-1', source: { kind: 'none' } }), meta({ id: 'old-2', source: { kind: 'none' } })];

  assert.deepEqual(goto(asked({ source: { kind: 'none' }, candidates: old })), { kind: 'start', offer: 'main.ts' });
  // Nor does a real source match a sourceless record.
  assert.deepEqual(goto(asked({ candidates: old })), { kind: 'start', offer: 'main.ts' });
});

// ---------------------------------------------------------------------------------------------
// Everything ambiguous is a picker. Nothing is ever guessed.
// ---------------------------------------------------------------------------------------------

test('two conversations with one source is a picker, never the newer of the two', () => {
  // Impossible by construction, and answered honestly anyway: choosing for somebody and telling them
  // afterwards is what the operator ruled out.
  const answer = goto(asked({ candidates: [meta({ id: 'a1', updatedAt: 10 }), meta({ id: 'b2', updatedAt: 99 })] }));

  assert.equal(answer.kind, 'pick');
  assert.deepEqual(answer.kind === 'pick' ? answer.among.map((one) => one.id) : [], ['a1', 'b2']);
  assert.deepEqual(answer.kind === 'pick' ? answer.why : undefined, { kind: 'several' });
  assert.equal(answer.kind === 'pick' ? answer.offer : '', 'main.ts', 'the picker cannot offer to start one instead');
});

test('a Claude tab whose name answers to more than one session is a picker that says so', () => {
  const answer = goto(asked({
    tab: { kind: 'claude', label: 'Fixing the lock', path: '' },
    source: { kind: 'none' },
    ambiguous: true,
  }));

  assert.equal(answer.kind, 'pick');
  assert.deepEqual(answer.kind === 'pick' ? answer.why : undefined, { kind: 'ambiguous session' });
});

test('an ambiguous session outranks having exactly one candidate, because the TAB is what is in doubt', () => {
  // The candidates were gathered for a source this tab may not actually own. Reopening on that would
  // bind a conversation to a tab that merely shares a name with its session.
  const answer = goto(asked({
    tab: { kind: 'claude', label: 'Fixing the lock', path: '' },
    source: sourceOfSession('9f1c3a4e-1111-2222-3333-444455556666'),
    ambiguous: true,
    candidates: [meta({ source: sourceOfSession('9f1c3a4e-1111-2222-3333-444455556666') })],
  }));

  assert.equal(answer.kind, 'pick');
  assert.deepEqual(answer.kind === 'pick' ? answer.why : undefined, { kind: 'ambiguous session' });
});

// ---------------------------------------------------------------------------------------------
// What the store's own state means.
// ---------------------------------------------------------------------------------------------

test('an index still being BUILT is not an empty store', () => {
  // It is empty for a window's first seconds. Answering `start` there offers to create a second
  // conversation for a tab that already has one — the duplicate this feature exists to prevent.
  // (Three vendors, the plan round.)
  assert.deepEqual(goto(asked({ index: { kind: 'building' } })), { kind: 'building' });
  // And it says so even for a tab whose conversation happens to be in the rows already.
  assert.deepEqual(goto(asked({ index: { kind: 'building' }, candidates: [meta()] })), { kind: 'building' });
  // But a conversation THIS WINDOW HOLDS needs no index at all.
  assert.deepEqual(goto(asked({ index: { kind: 'building' }, live: 'open-1' })), { kind: 'reveal', id: 'open-1' });
});

test('an index that could not be READ never reopens — it offers what it last knew, and says why', () => {
  // Binding a tab to a row that may since have been forgotten is a guess wearing a disk. The person
  // sees the candidates and chooses; the press then re-reads the record.
  const why = { kind: 'unavailable' as const, reason: 'the store could not be read (EACCES)', lastGoodAt: 5 };
  const answer = goto(asked({ index: why, candidates: [meta()] }));

  assert.equal(answer.kind, 'pick', 'a conversation was bound to a tab on the strength of rows that could not be refreshed');
  assert.deepEqual(answer.kind === 'pick' ? answer.why : undefined, { kind: 'unreadable', reason: why.reason });
  assert.deepEqual(answer.kind === 'pick' ? answer.among.map((one) => one.id) : [], ['a1'], 'the rows it did have were thrown away');
});

test('an unreadable index still reveals what this window holds, and still lists for an ineligible tab', () => {
  const why = { kind: 'unavailable' as const, reason: 'nope', lastGoodAt: 5 };

  assert.deepEqual(goto(asked({ index: why, live: 'open-1' })), { kind: 'reveal', id: 'open-1' });
  assert.deepEqual(goto(asked({ index: why, tab: { kind: 'other', label: 'Terminal', path: '' } })), { kind: 'everything' });
});

// ---------------------------------------------------------------------------------------------
// The matrix, as a whole: every combination lands on exactly one of the six answers.
// ---------------------------------------------------------------------------------------------

test('every combination of the inputs answers, and only with answers this union names', () => {
  const kinds: readonly Goto['kind'][] = ['reveal', 'reopen', 'pick', 'start', 'everything', 'building'];
  const states: readonly IndexState[] = [READY, { kind: 'building' }, { kind: 'unavailable', reason: 'x', lastGoodAt: 1 }];
  const seen = new Set<string>();
  for (const kind of ['claude', 'document', 'other'] as const) {
    for (const live of ['', 'open-1']) {
      for (const ambiguous of [false, true]) {
        for (const index of states) {
          for (const candidates of [[], [meta()], [meta({ id: 'a1' }), meta({ id: 'b2' })]]) {
            const answer = goto(asked({ tab: { kind, label: 'main.ts', path: kind === 'document' ? HERE_PATH : '' }, live, ambiguous, index, candidates }));
            assert.ok(kinds.includes(answer.kind), `an answer this union does not name: ${kindOf(answer)}`);
            seen.add(answer.kind);
          }
        }
      }
    }
  }

  assert.deepEqual([...seen].sort(), [...kinds].sort(), `the matrix never produced every answer: ${[...seen].join(',')}`);
});

// ---------------------------------------------------------------------------------------------
// Decide on what is known; verify at the act.
// ---------------------------------------------------------------------------------------------

test('a record re-read at the press is bindable only if it is still this tab’s', () => {
  // Between the decision and the press another window can forget the conversation or follow it
  // somewhere else. The same division story B3 made for the picker.
  const record = { source: sourceOfFile(HERE) } as Parameters<typeof bindable>[0];

  assert.equal(bindable(record, sourceOfFile(HERE)), true);
  assert.equal(bindable(undefined, sourceOfFile(HERE)), false, 'a conversation that has gone was bound to a tab');
  assert.equal(bindable(record, sourceOfFile('file:///D:/rsd/one/other.ts')), false, 'a record was bound to a tab it does not belong to');
  assert.equal(bindable(record, { kind: 'none' }), false, 'a sourceless tab was bound to a record');
});
