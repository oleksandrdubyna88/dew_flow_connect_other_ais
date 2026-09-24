import assert from 'node:assert/strict';
import test from 'node:test';

import { ReviewPair } from '../reviewPair';
import { bugChat, BugChatKeys, fenced } from '../reviewChoose';

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
  const keys = new BugChatKeys();

  assert.equal(keys.keyFor('D:/r#7'), keys.keyFor('D:/r#7'), 'a second press finds the first conversation');
  assert.notEqual(keys.keyFor('D:/r#7'), keys.keyFor('D:/r#8'));
  assert.notEqual(keys.keyFor('D:/r#7'), keys.keyFor('D:/s#7'), 'the same finding id in another checkout is another bug');
});
