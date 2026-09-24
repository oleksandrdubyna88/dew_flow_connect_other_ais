import assert from 'node:assert/strict';
import test from 'node:test';

import { ChatEntry, ChatPanels } from '../chatPanels';
import { ReviewPair } from '../reviewPair';
import { bugChat, BugChats, fenced } from '../reviewChoose';

/**
 * What *CoAI: choose* on a bug hands a chat (issue #487): the seven things the row shows, in the
 * order the issue names them — Where, Why, Fix, Complexity, Before, After, and the person's comment.
 */

function pair(over: Partial<ReviewPair> = {}): ReviewPair {
  return {
    findingId: 7,
    symbolName: 'Settle',
    language: 'TypeScript',
    skeletonBefore: 'function settle(a) {\n  if (a) { return 1; }\n  return 0;\n}',
    skeletonAfter: 'function settle(a) {\n  return a ? 1 : 0;\n}',
    keep: -1,
    severity: 'Major',
    category: 'correctness',
    title: 'something',
    repoPath: 'D:/rsd/repo_a',
    headSha: 'aaaa1111bbbb',
    fixSha: 'cccc2222dddd',
    file: 'src/x.ts',
    line: 42,
    why: 'the branch is dead',
    fix: 'fold it',
    comment: 'saved words',
    sentUtc: '',
    commentLost: '',
    ...over,
  };
}

const NO_DRAFTS: ReadonlyMap<number, string> = new Map();

test('the passage carries the seven parts in the issue\'s order, each with its value', () => {
  const text = bugChat(pair(), NO_DRAFTS).text;

  const labels = ['Where', 'Why', 'Fix', 'Complexity', 'Before', 'After', 'Your comment on this pair'];
  const at = labels.map((label) => text.indexOf(`**${label}`));
  assert.ok(at.every((index) => index >= 0), `every label is present: ${JSON.stringify(at)}`);
  assert.deepEqual([...at].sort((a, b) => a - b), at, 'and in the issue\'s order');
  assert.match(text, /\*\*Where:\*\* src\/x\.ts:42 at aaaa111 in D:\/rsd\/repo_a/u);
  assert.match(text, /\*\*Why:\*\* the branch is dead/u);
  assert.match(text, /\*\*Fix:\*\* fold it/u);
  assert.match(text, /\*\*Complexity:\*\* \d+ at aaaa111 → \d+ at cccc222/u);
  assert.ok(text.includes('if (a) { return 1; }'), 'the before skeleton, verbatim');
  assert.ok(text.includes('return a ? 1 : 0;'), 'the after skeleton, verbatim');
  assert.match(text, /\*\*Your comment on this pair:\*\* saved words/u);
});

test('the words being typed beat the saved comment, and no words are said as none', () => {
  assert.match(bugChat(pair(), new Map([[7, 'typed just now']])).text, /this pair:\*\* typed just now/u);
  assert.match(bugChat(pair({ comment: '' }), NO_DRAFTS).text, /this pair:\*\* \(none\)/u);
  assert.match(bugChat(pair(), new Map([[8, 'another row']])).text, /this pair:\*\* saved words/u,
    'a draft of ANOTHER row is not this one\'s');
});

test('a skeleton holding a fence of its own stays inside one fence', () => {
  const code = 'const doc = `\n```\nnot the end\n```\n`;';
  const block = fenced(code, 'TypeScript');
  const fence = /^(`+)/u.exec(block)![1]!;

  assert.ok(fence.length >= 4, `longer than the three inside: ${fence}`);
  assert.ok(block.endsWith(`\n${fence}`), 'and closed by the same fence');
  assert.equal(block.split('\n').filter((line) => line === fence).length, 1, 'which closes it only once');
});

test('nothing recorded is said as such, never as a blank or a zero', () => {
  const text = bugChat(pair({ why: '  ', fix: '', file: '', line: 0, language: 'Klingon' }), NO_DRAFTS).text;

  assert.match(text, /\*\*Why:\*\* \(none recorded\)/u);
  assert.match(text, /\*\*Fix:\*\* \(none recorded\)/u);
  assert.match(text, /\*\*Where:\*\* \(no file recorded\)/u);
  assert.doesNotMatch(text, /\*\*Complexity:\*\* 0/u, 'a language the count does not read is not a zero');
});

test('the chat is named after the method and keyed by the bug, in its checkout', () => {
  const chat = bugChat(pair(), NO_DRAFTS);

  assert.equal(chat.label, 'Settle');
  assert.equal(chat.key, 'D:/rsd/repo_a#7');
  assert.equal(chat.repoPath, 'D:/rsd/repo_a');
  assert.equal(chat.file, 'src/x.ts');
});

test('one bug is one conversation key for the window, and two bugs are two', () => {
  const chats = new BugChats();

  assert.equal(chats.keyFor('D:/r#7'), chats.keyFor('D:/r#7'));
  assert.notEqual(chats.keyFor('D:/r#7'), chats.keyFor('D:/r#8'));
  assert.notEqual(chats.keyFor('D:/r#7'), chats.keyFor('D:/s#7'), 'the same finding id in another checkout is another bug');
});

/** A conversation the registry can hold — the shape `chatPanel.test.ts` fakes, reduced to what is counted. */
function conversation(): { entry: ChatEntry; revealed: () => number } {
  let reveals = 0;

  return {
    entry: {
      id: {},
      panel: { reveal: () => { reveals += 1; }, dispose: () => undefined, post: () => undefined, isActive: () => false },
      session: { dispose: () => undefined },
    },
    revealed: () => reveals,
  };
}

test('a second press finds the bug\'s conversation even after *go to* moved it onto a file tab', () => {
  const panels = new ChatPanels();
  const chats = new BugChats();
  const made = conversation();
  panels.open(chats.keyFor('D:/r#7'), 'Settle', () => made.entry);
  chats.remember('D:/r#7', panels);

  // What *go to conversation* and the picker do when they bind a conversation to the tab of its file.
  assert.ok(panels.rekey(chats.keyFor('D:/r#7'), { fileTab: true }));

  assert.equal(chats.openFor('D:/r#7', panels), made.entry,
    'found by the id that survives a re-key — by the key alone the next press opened a second conversation');
  assert.equal(chats.openFor('D:/r#8', panels), undefined, 'and another bug has none');
});

test('a bug whose conversation was closed has none, so the next press opens a new one', () => {
  const panels = new ChatPanels();
  const chats = new BugChats();
  panels.open(chats.keyFor('D:/r#7'), 'Settle', () => conversation().entry);
  chats.remember('D:/r#7', panels);

  panels.close(chats.keyFor('D:/r#7'));

  assert.equal(chats.openFor('D:/r#7', panels), undefined);
});

test('a first press that opened nothing remembers nothing', () => {
  const panels = new ChatPanels();
  const chats = new BugChats();

  chats.remember('D:/r#7', panels);

  assert.equal(chats.openFor('D:/r#7', panels), undefined, 'a refused press (no CLI, no model) leaves no conversation to find');
});

test('the fence names its language and nothing else — a stray backtick or line break cannot end it', () => {
  assert.match(fenced('x', 'C#'), /^```c#\n/u);
  assert.match(fenced('x', 'TypeScript'), /^```typescript\n/u);
  assert.match(fenced('x', 'ts`\nalert(1)'), /^```tsalert1\n/u, 'an info string holds no backtick and no line break');
});
