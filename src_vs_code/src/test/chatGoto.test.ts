import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ConversationMeta, sourceOfFile, sourceOfSession } from '../chatStore';
import { IndexState } from '../chatStoreCache';
import { Goto, GotoAsked, UNNAMED_TAB, bindable, goto, rootOfTab } from '../chatGoto';

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
  severalSessions: true,
  walkFailed: false,
  namesakes: 1,
  candidates: [],
  inRoot: [],
  roots: ROOTS,
  fallback: 'D:\\rsd\\one',
  caseBlind: true,
  index: READY,
  ...over,
});

const kindOf = (answer: Goto): string => answer.kind;

test('the root a tab belongs to is one the host can ask for, and it is the root the decision files by', () => {
  // The host must re-check the record against THE TAB'S root at the press, and the only way the two
  // halves can agree is by asking the same function. Working it out a second time in the command was
  // the defect that made the workspace half of the guard a tautology: it compared the record's own
  // workspace against itself and passed for every record in the store. (gemini, the code round.)
  assert.equal(rootOfTab(asked()), 'D:\\rsd\\one');
  // The tab's own root, not the window's first: a file under the second root belongs to the second.
  assert.equal(rootOfTab(asked({ tab: { kind: 'document', label: 'other.ts', path: 'D:\\rsd\\two\\other.ts' } })), 'D:\\rsd\\two');
  // A tab under NO root falls back to the same answer story C1 files such a conversation by.
  assert.equal(rootOfTab(asked({ tab: { kind: 'document', label: 'away.ts', path: 'C:\\elsewhere\\away.ts' } })), 'D:\\rsd\\one');
  // And it is the root the decision itself used: a record filed under it reopens, one filed under
  // the other root does not — so a guard built on this value agrees with the answer it is guarding.
  const here = rootOfTab(asked({ tab: { kind: 'document', label: 'other.ts', path: 'D:\\rsd\\two\\other.ts' } }));
  const mine = meta({ source: sourceOfFile('file:///D:/rsd/two/other.ts'), workspace: here });
  assert.equal(kindOf(goto(asked({
    tab: { kind: 'document', label: 'other.ts', path: 'D:\\rsd\\two\\other.ts' },
    source: sourceOfFile('file:///D:/rsd/two/other.ts'),
    candidates: [mine],
  }))), 'reopen');
  // The same record filed under the OTHER root is offered rather than reopened — which is exactly
  // what a tautological workspace check would have destroyed.
  assert.equal(kindOf(goto(asked({
    tab: { kind: 'document', label: 'other.ts', path: 'D:\\rsd\\two\\other.ts' },
    source: sourceOfFile('file:///D:/rsd/two/other.ts'),
    candidates: [meta({ source: sourceOfFile('file:///D:/rsd/two/other.ts'), workspace: 'D:\\rsd\\one' })],
  }))), 'pick');
});

test('a unique name match is refused when TWO tabs carry that name', () => {
  // Three reviewers refused the auto-open with one case between them: two tabs called the same
  // thing, one conversation of that name, and pressing go-to on the tab that does not own it opens
  // the one that does. They were right, and this is the fact that answers them — with two tabs of a
  // name, the name identifies neither, so the question goes back to the person.
  const tab = { kind: 'claude' as const, label: 'speak with Astra', path: '' };
  const mine = meta({ id: 'astra', title: 'speak with Astra' });

  assert.equal(
    kindOf(goto(asked({ tab, ambiguous: true, source: { kind: 'none' }, inRoot: [mine], namesakes: 2 }))),
    'pick',
    'a conversation was opened on the strength of a name two open tabs share',
  );
  // And it is still narrowed to the match rather than thrown back to everything.
  const answer = goto(asked({ tab, ambiguous: true, source: { kind: 'none' }, inRoot: [mine, meta({ id: 'x', title: 'other' })], namesakes: 2 }));
  assert.deepEqual(answer.kind === 'pick' ? answer.among : [], [mine]);
});

test('a walk that FAILED never becomes a conversation opened on a name', () => {
  // `ambiguous` carries two different facts: two sessions answer to this name, and the folder could
  // not be read. Both should ask rather than guess, and only the first is evidence a fallback may
  // lean on — a temporarily unreadable directory says nothing about names either. (codex.)
  const tab = { kind: 'claude' as const, label: 'speak with Astra', path: '' };
  const mine = meta({ id: 'astra', title: 'speak with Astra' });

  // `walkFailed` is what says the directory would not open. Until it existed this test passed with
  // `severalSessions: false` alone, because every non-namesake case was called unreadable — which is
  // exactly the defect the test below this one now covers.
  const answer = goto(asked({ tab, ambiguous: true, severalSessions: false, walkFailed: true, source: { kind: 'none' }, inRoot: [mine] }));
  assert.equal(kindOf(answer), 'pick', 'a session directory that could not be read opened a conversation anyway');
  // AND IT SAYS WHICH AMBIGUITY IT MET. Telling somebody their sessions share a name, when the truth
  // is that the folder would not open, sends them looking for a duplicate that is not there.
  // (gemini, the code round.)
  assert.equal(
    answer.kind === 'pick' ? answer.why.kind : '',
    'unreadable',
    'a walk that could not be done is reported as two sessions of one name',
  );
});

test('an ambiguous tab whose NAME matches exactly one conversation opens it', () => {
  // The name is the last thing left, and only here: everywhere else this module matches a source and
  // never a title. On this path there is no source to be wrong about — the tab's own session could
  // not be resolved — so the alternative is not "match by source instead", it is "show everything and
  // ask". Which is what it did: a title reading «more than one Claude session is called X» above
  // forty rows mostly not called X. (Found by the operator, testing 0.40.0.)
  const tab = { kind: 'claude' as const, label: 'speak with Astra', path: '' };
  const mine = meta({ id: 'astra', title: 'speak with Astra', source: sourceOfSession('11111111-2222-3333-4444-555555555555') });
  const other = meta({ id: 'other', title: 'Logging analysis' });

  assert.deepEqual(
    goto(asked({ tab, ambiguous: true, source: { kind: 'none' }, inRoot: [other, mine] })),
    { kind: 'reopen', meta: mine },
    'a tab whose name matches one conversation still opens a list of everything',
  );
});

test('several of that name narrow the question to THOSE, not to everything', () => {
  const tab = { kind: 'claude' as const, label: 'speak with Astra', path: '' };
  const one = meta({ id: 'a1', title: 'speak with Astra' });
  const two = meta({ id: 'a2', title: 'speak with Astra' });
  const other = meta({ id: 'x', title: 'Logging analysis' });
  const answer = goto(asked({ tab, ambiguous: true, source: { kind: 'none' }, inRoot: [other, one, two] }));

  assert.equal(kindOf(answer), 'pick');
  assert.deepEqual(
    answer.kind === 'pick' ? answer.among : [],
    [one, two],
    'the picker for an ambiguous tab still offers conversations that are not called what the tab is called',
  );
});

test('and a name that matches NOTHING falls back to the whole root, as it always did', () => {
  // A list of everything is still better than nothing when the name says nothing either.
  const tab = { kind: 'claude' as const, label: 'speak with Astra', path: '' };
  const other = meta({ id: 'x', title: 'Logging analysis' });
  const answer = goto(asked({ tab, ambiguous: true, source: { kind: 'none' }, inRoot: [other] }));

  assert.equal(kindOf(answer), 'pick');
  assert.deepEqual(answer.kind === 'pick' ? answer.among : [], [other]);
});

test('an EMPTY tab name matches nothing rather than everything', () => {
  // The difference between a fallback and a bug: an unnamed tab must not adopt the one conversation
  // that happens to have been saved with an empty title.
  const tab = { kind: 'claude' as const, label: '', path: '' };
  const nameless = meta({ id: 'n', title: '' });
  const answer = goto(asked({ tab, ambiguous: true, source: { kind: 'none' }, inRoot: [nameless] }));

  assert.equal(kindOf(answer), 'pick', 'a nameless tab reopened a conversation on the strength of a shared emptiness');
});

test('the name is a fallback ONLY where there is no source — never beside a good one', () => {
  // The rule the whole module rests on: a tab whose source resolved and matched nothing HAS no
  // conversation, and a conversation of the same name belongs to some other tab. Matching it here
  // would be the silent wrong-conversation this feature exists to prevent, arriving through the
  // fallback added to fix something else.
  const tab = { kind: 'document' as const, label: 'main.ts', path: HERE_PATH };
  const namesake = meta({ id: 'ns', title: 'main.ts', source: sourceOfFile('file:///D:/rsd/one/OTHER.ts') });

  assert.equal(
    kindOf(goto(asked({ tab, ambiguous: false, inRoot: [namesake], candidates: [] }))),
    'start',
    'a resolved tab with no conversation of its own adopted one that merely shares its name',
  );

  // AND THE SAME FOR A CLAUDE TAB WHOSE SESSION RESOLVED. This is the case the guard is actually
  // made of — the branch the name fallback lives on is a Claude branch, so a test that only proves
  // it for a document proves nothing about the condition that keeps it there. (Found by breaking
  // `asked.ambiguous &&` out of the guard and watching the suite stay green.)
  const resolved = { kind: 'claude' as const, label: 'speak with Astra', path: '' };
  assert.equal(
    kindOf(goto(asked({
      tab: resolved,
      ambiguous: false,
      source: sourceOfSession('99999999-8888-7777-6666-555555555555'),
      inRoot: [meta({ id: 'ns2', title: 'speak with Astra' })],
      candidates: [],
    }))),
    'start',
    'a Claude tab whose own session resolved still adopted a conversation that merely shares its name',
  );
});

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

  // NOT reopened — and not silently dropped either. Treating it as nothing would offer to START a
  // conversation while the one about this very file sits a folder away: the duplicate the workspace
  // rule exists to prevent, produced by the rule itself. It is shown, and the person decides.
  // (Two vendors, the code round.)
  const answer = goto(asked({ candidates: [there] }));
  assert.equal(answer.kind, 'pick', 'a conversation about this file in another project was silently ignored');
  assert.deepEqual(answer.kind === 'pick' ? answer.why : undefined, { kind: 'cross root' });
  assert.deepEqual(answer.kind === 'pick' ? answer.among.map((one) => one.id) : [], ['a1']);
  // And the same record IS this tab's when the tab is the one under that root.
  const tabThere = { kind: 'document' as const, label: 'main.ts', path: 'D:\\rsd\\two\\main.ts' };
  const uriThere = 'file:///D:/rsd/two/main.ts';
  const same = goto(asked({
    tab: tabThere,
    source: sourceOfFile(uriThere),
    candidates: [meta({ source: sourceOfFile(uriThere), workspace: 'D:\\rsd\\two' })],
  }));
  assert.equal(same.kind, 'reopen');
});

test('a root spelled two ways is one root, not two', () => {
  // The record's workspace was written by a window that may have spelled its root differently — a
  // forward slash, another case on a case-blind filesystem. Comparing them as raw strings meant the
  // person was offered a new conversation while theirs sat right there. (codex, the code round.)
  const spelled = meta({ workspace: 'D:/RSD/ONE' });

  assert.equal(goto(asked({ candidates: [spelled] })).kind, 'reopen', 'one root spelled two ways read as two roots');
  // And on a case-SENSITIVE filesystem those really are two folders.
  assert.equal(goto(asked({ candidates: [spelled], caseBlind: false })).kind, 'pick',
    'a case-sensitive filesystem treated two differently-cased roots as one');
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

test('a Claude tab whose name answers to more than one session is a picker that says so — WITH the candidates in it', () => {
  // The one defect in this story that would have shipped looking like a working feature, and four
  // reviewers found it. An ambiguous tab has no resolvable source, so filtering the candidates BY
  // source left none — a picker asking which conversation somebody meant, containing no
  // conversations. The source is the very thing in doubt there; the candidates are narrowed by their
  // root and by nothing else.
  const answer = goto(asked({
    tab: { kind: 'claude', label: 'Fixing the lock', path: '' },
    source: { kind: 'none' },
    ambiguous: true,
    // Through inRoot, not candidates: an ambiguous tab has no source to filter by, which is the whole
    // point, and the two lists exist so a caller cannot get that wrong. (codex, the code round.)
    inRoot: [
      meta({ id: 'one', source: sourceOfSession('9f1c3a4e-1111-2222-3333-444455556666') }),
      meta({ id: 'two', source: sourceOfSession('aaaabbbb-1111-2222-3333-444455556666') }),
    ],
  }));

  assert.equal(answer.kind, 'pick');
  assert.deepEqual(answer.kind === 'pick' ? answer.why : undefined, { kind: 'ambiguous session' });
  assert.deepEqual(answer.kind === 'pick' ? answer.among.map((one) => one.id) : [], ['one', 'two'],
    'the picker asks which conversation was meant and offers none of them');
});

test('an ambiguous tab is offered its own root’s conversations, and the caller supplies them', () => {
  // `inRoot` IS the root's rows — the host narrows by root when it reads the index, because the
  // index is the thing that can do it cheaply. What this asserts is that the decision shows them
  // rather than filtering them away by a source that does not exist.
  const answer = goto(asked({
    tab: { kind: 'claude', label: 'Fixing the lock', path: '' },
    source: { kind: 'none' },
    ambiguous: true,
    inRoot: [meta({ id: 'here', source: sourceOfSession('9f1c3a4e-1111-2222-3333-444455556666') })],
    // And the source-matched list is not consulted on this path at all: there is no source.
    candidates: [meta({ id: 'unrelated' })],
  }));

  assert.deepEqual(answer.kind === 'pick' ? answer.among.map((one) => one.id) : [], ['here'],
    'the ambiguous picker read the source-matched list, which is empty exactly when it is needed');
});

test('a DOCUMENT tab is never treated as an ambiguous session, whatever the flag says', () => {
  // Only a Claude tab can be ambiguous in this sense — a document is named by its uri, which answers
  // to one thing or to nothing. Reading the flag wherever it was set let an invalid combination
  // produce a picker telling somebody that two Claude sessions share their file's name. (codex.)
  const answer = goto(asked({ ambiguous: true, candidates: [meta()] }));

  assert.equal(answer.kind, 'reopen', 'a document tab produced a Claude-session picker');
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
  // The ROOT's rows, not this tab's: a store that could not be read cannot say which are this tab's,
  // and the root's are the honest superset.
  const answer = goto(asked({ index: why, inRoot: [meta()], candidates: [meta()] }));

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
  const mine = 'D:\\rsd\\one';
  const record = { source: sourceOfFile(HERE), workspace: mine } as Parameters<typeof bindable>[0];

  assert.equal(bindable(record, sourceOfFile(HERE), mine, true), true);
  assert.equal(bindable(undefined, sourceOfFile(HERE), mine, true), false, 'a conversation that has gone was bound to a tab');
  assert.equal(bindable(record, sourceOfFile('file:///D:/rsd/one/other.ts'), mine, true), false, 'a record was bound to a tab it does not belong to');
  assert.equal(bindable(record, { kind: 'none' }, mine, true), false, 'a sourceless tab was bound to a record');
  // BOTH halves of the key. A record another window re-filed into a different project between the
  // decision and the press still has this tab's source — checking only that would bind the wrong
  // project's conversation, undoing the workspace rule at the last step. (Three findings.)
  assert.equal(bindable(record, sourceOfFile(HERE), 'D:\\rsd\\two', true), false, 'a conversation from another project was bound to this tab');
  // And the two roots are compared the way paths are, not as raw strings.
  assert.equal(bindable(record, sourceOfFile(HERE), 'D:/RSD/ONE', true), true, 'one root spelled two ways read as two roots');
});

test('a folder that ANSWERED and matched nothing says so, rather than that it could not be read', () => {
  // `ambiguous` folds two different facts into one flag — the folder would not open, and the folder
  // opened and nothing in it is called this — and `goto` had nothing to tell them apart with, so the
  // commonest case of all was reported as a failure that had not happened. The person was then sent
  // looking for a directory problem while the truth was that no session wears that name.
  const tab = { kind: 'claude' as const, label: 'speak with Astra', path: '' };
  const mine = meta({ id: 'astra', title: 'speak with Astra' });

  const answered = goto(asked({
    tab,
    ambiguous: true,
    severalSessions: false,
    walkFailed: false,
    source: { kind: 'none' },
    inRoot: [mine],
  }));
  assert.equal(kindOf(answered), 'pick');
  assert.equal(
    answered.kind === 'pick' ? answered.why.kind : '',
    'unmatched',
    'a folder that answered perfectly well was reported as one that could not be read',
  );

  // And a walk that really did fail still says that, because the two ask different things of the person.
  const failed = goto(asked({
    tab,
    ambiguous: true,
    severalSessions: false,
    walkFailed: true,
    source: { kind: 'none' },
    inRoot: [mine],
  }));
  assert.equal(
    failed.kind === 'pick' ? failed.why.kind : '',
    'unreadable',
    'a walk that could not be done lost its own sentence',
  );
});
