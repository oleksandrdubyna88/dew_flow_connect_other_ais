import assert from 'node:assert/strict';
import { test } from 'node:test';
import { sourceOfFile, sourceOfSession } from '../chatStore';
import { Moved, filedUnder, followable, movedTo, prepareMoves, rootOf, sessionIdOf } from '../chatSource';

/**
 * Which root a conversation is filed under, and what a rename moves — decided without a host.
 *
 * <p>Both rules were wrong in a way no test could have caught before this story, because nothing
 * wrote a real value into either field: every record said `source: none` and every record said the
 * FIRST root. The tests here are what make the two answers checkable.</p>
 */

const ROOTS = ['D:\\rsd\\one', 'D:\\rsd\\two'];

/** The host's two spellings, as this module takes them — a test's, so nothing here needs `vscode`. */
const toUri = (path: string): string => `file:///${path.replace(/\\/gu, '/')}`;
const fsPath = (uri: string): string => decodeURI(uri.replace(/^file:\/\/\//u, '')).replace(/\//gu, '\\');

test('a path is filed under the root that contains it, not the first one the window opened', () => {
  // The defect this story exists to remove, and it is A3's deferral coming due: every conversation
  // was filed under `whereToLook()[0]`, so in a multi-root window a chat opened in the second root
  // was invisible to a picker filtered on the second root — epic B's “this folder” scope silently
  // lying about a conversation that was right there.
  assert.equal(rootOf('D:\\rsd\\two\\src\\main.ts', ROOTS), 'D:\\rsd\\two');
  assert.equal(rootOf('D:\\rsd\\one\\README.md', ROOTS), 'D:\\rsd\\one');
  assert.equal(rootOf('D:\\somewhere\\else\\x.ts', ROOTS), '', 'a path under no root claimed one');
  assert.equal(rootOf('', ROOTS), '', 'a path that is not a path claimed a root');
  assert.equal(rootOf('D:\\rsd\\two\\src\\main.ts', []), '', 'a window with no folders open still filed under one');
});

test('the LONGEST root wins, because roots nest', () => {
  // A window open on both a folder and a project inside it is ordinary here. Taking the first match
  // would file every conversation in the inner project under the outer one — the same misfiling this
  // story removes, one level down.
  const nested = ['D:\\rsd', 'D:\\rsd\\coai'];

  assert.equal(rootOf('D:\\rsd\\coai\\src\\chat.ts', nested), 'D:\\rsd\\coai');
  assert.equal(rootOf('D:\\rsd\\other\\src\\chat.ts', nested), 'D:\\rsd');
  assert.equal(rootOf('D:\\rsd\\coai\\src\\chat.ts', [...nested].reverse()), 'D:\\rsd\\coai', 'the answer depends on the order the roots arrived in');
});

test('containment is on segment boundaries and blind to case and separator', () => {
  // `coai-old` is not inside `coai`, however much of the name it shares. And a path reaches this rule
  // from two programs — the editor's and Claude's — which do not agree on separators or on case.
  assert.equal(rootOf('D:\\rsd\\coai-old\\x.ts', ['D:\\rsd\\coai']), '', 'a sibling whose name starts the same was read as inside');
  assert.equal(rootOf('D:/rsd/two/src/main.ts', ROOTS), 'D:\\rsd\\two', 'a forward-slash path found no root');
  assert.equal(rootOf('d:\\RSD\\TWO\\src\\main.ts', ROOTS), 'D:\\rsd\\two', 'a differently-cased path found no root');
  assert.equal(rootOf('D:\\rsd\\two', ROOTS), 'D:\\rsd\\two', 'the root itself is not inside itself');
  assert.equal(rootOf('D:\\rsd\\two\\', ROOTS), 'D:\\rsd\\two', 'a trailing separator changed the answer');
});

test('a source that knows nothing keeps the first root, which is what it already had', () => {
  // Every record written before this story, and every window with no folder open at all. Filing them
  // under an empty workspace would hide them from the picker's “this folder” view, which is a change
  // for the worse for conversations that are doing no harm.
  assert.equal(filedUnder('D:\\rsd\\two\\src\\main.ts', ROOTS, 'D:\\rsd\\one'), 'D:\\rsd\\two');
  assert.equal(filedUnder('', ROOTS, 'D:\\rsd\\one'), 'D:\\rsd\\one', 'a sourceless conversation was filed nowhere');
  assert.equal(filedUnder('D:\\elsewhere\\x.ts', ROOTS, 'D:\\rsd\\one'), 'D:\\rsd\\one', 'a file outside every root was filed nowhere');
  assert.equal(filedUnder('', [], ''), '', 'a window with nothing open invented a folder');
});

test('a renamed file is followed, and a rename that says nothing about it changes nothing', () => {
  const renames: readonly Moved[] = [{ from: 'D:\\rsd\\one\\old.ts', to: 'D:\\rsd\\one\\new.ts' }];

  assert.equal(movedTo(toUri('D:\\rsd\\one\\old.ts'), prepareMoves(renames), toUri, fsPath), toUri('D:\\rsd\\one\\new.ts'));
  assert.equal(movedTo(toUri('D:\\rsd\\one\\other.ts'), prepareMoves(renames), toUri, fsPath), '', 'an unrelated file was rewritten');
  assert.equal(movedTo(toUri('D:\\rsd\\one\\old.ts'), prepareMoves([]), toUri, fsPath), '', 'a rename that did not happen moved something');
});

test('a renamed FOLDER moves everything under it, which the editor reports as one entry', () => {
  const renames: readonly Moved[] = [{ from: 'D:\\rsd\\one\\src', to: 'D:\\rsd\\one\\lib' }];

  assert.equal(movedTo(toUri('D:\\rsd\\one\\src\\deep\\file.ts'), prepareMoves(renames), toUri, fsPath), toUri('D:\\rsd\\one\\lib\\deep\\file.ts'));
  assert.equal(movedTo(toUri('D:\\rsd\\one\\srcs\\file.ts'), prepareMoves(renames), toUri, fsPath), '', 'a sibling folder whose name starts the same was moved');
});

test('the CLOSEST rename wins when a file is inside two of them', () => {
  // Moving `src` and `src/chat` in one gesture is two entries, and both are true of a file in the
  // second. Only the closer one has the right new path.
  const renames: readonly Moved[] = [
    { from: 'D:\\rsd\\one\\src', to: 'D:\\rsd\\one\\lib' },
    { from: 'D:\\rsd\\one\\src\\chat', to: 'D:\\rsd\\two\\chat' },
  ];

  assert.equal(movedTo(toUri('D:\\rsd\\one\\src\\chat\\page.ts'), prepareMoves(renames), toUri, fsPath), toUri('D:\\rsd\\two\\chat\\page.ts'));
  assert.equal(movedTo(toUri('D:\\rsd\\one\\src\\other.ts'), prepareMoves(renames), toUri, fsPath), toUri('D:\\rsd\\one\\lib\\other.ts'));
});

test('a move across roots is what makes the workspace part load-bearing', () => {
  // The follow rewrites the URI; the caller must recompute the root from it and write BOTH in one
  // swap. Writing the URI alone leaves a conversation whose source is under root two filed under root
  // one — invisible in exactly the scope the person is looking at. (Two vendors, the plan round.)
  const renames: readonly Moved[] = [{ from: 'D:\\rsd\\one\\moved.ts', to: 'D:\\rsd\\two\\moved.ts' }];
  const now = movedTo(toUri('D:\\rsd\\one\\moved.ts'), prepareMoves(renames), toUri, fsPath);

  assert.equal(now, toUri('D:\\rsd\\two\\moved.ts'));
  assert.equal(rootOf(fsPath(now), ROOTS), 'D:\\rsd\\two', 'the moved file still answers with the root it left');
});

test('a session id is the file name, on either platform’s separator, and never the path', () => {
  // A stored home-directory path names a machine and a person and is wrong the moment a profile
  // moves; the id names the conversation. The separator matters because this path arrives from
  // Claude rather than from the editor — a forward-slash spelling on Windows is ordinary, and a rule
  // that split on only one of them would have returned the WHOLE path as an id, silently.
  assert.equal(sessionIdOf('D:\\Users\\me\\.claude\\projects\\enc\\9f1c3a4e-1111.jsonl'), '9f1c3a4e-1111');
  assert.equal(sessionIdOf('/home/me/.claude/projects/enc/9f1c3a4e-1111.jsonl'), '9f1c3a4e-1111');
  assert.equal(sessionIdOf('9f1c3a4e-1111.jsonl'), '9f1c3a4e-1111', 'a bare file name is not an id');
  // Anything that is not one of those files writes NO source rather than a wrong one.
  assert.equal(sessionIdOf(''), '');
  assert.equal(sessionIdOf('D:\\Users\\me\\.claude\\projects\\enc'), '', 'a directory was read as a session');
  assert.equal(sessionIdOf('notes.txt'), '', 'a file of another kind was read as a session');
  assert.equal(sessionIdOf('.jsonl'), '', 'a file with no name at all was read as a session');
});

test('only a saved FILE conversation is followed by a rename', () => {
  // A session UUID names no path, so no file move can change it; an unsaved buffer has no path to
  // move; and a record with no source has nothing to follow.
  assert.equal(followable(sourceOfFile('file:///D:/rsd/one/main.ts')), true);
  assert.equal(followable(sourceOfFile('untitled:Untitled-1')), false, 'an unsaved buffer was treated as a file that can move');
  assert.equal(followable(sourceOfSession('9f1c3a4e')), false, 'a Claude session was treated as a file that can move');
  assert.equal(followable({ kind: 'none' }), false);
});
