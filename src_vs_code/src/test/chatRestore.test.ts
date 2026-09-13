import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';
import {
  INCOMPATIBLE_NOTICE,
  RESTORE_RETRY,
  RETRY_LABEL,
  RestoreDecision,
  noticeHtml,
  restoreDecision,
  unavailableNotice,
} from '../chatRestore';
import { CONVERSATION_VERSION, ConversationRecord } from '../chatStore';
import { ReadOutcome } from '../chatStoreFile';
import { SavedTab } from '../chatTabs';

/**
 * What a reload does about each of the store's four answers — the decision as values, the notice
 * page as text, and the `vscode` half read as source.
 *
 * <p>Story A2 made the store's read typed for exactly one consumer: the serializer, which disposed a
 * panel over "no record" and must not do that over a permissions error or a record a newer build
 * wrote. This is where that consumer is held to it. The two answers that keep the tab get a DEFINED
 * tab — a sentence, the id preserved for the next reload, and a retry where one makes sense — not an
 * undisposed panel with no handlers, which is a blank tab with no explanation.</p>
 */

const AT = Date.UTC(2026, 8, 13, 12, 0, 0);

const record: ConversationRecord = {
  version: CONVERSATION_VERSION,
  rev: 4,
  id: 'a1',
  title: 'main',
  passage: '',
  modelId: 'gemini',
  messages: [{ role: 'you', text: 'why' }],
  fromSession: true,
  carryFrom: 0,
  source: { kind: 'none' },
  workspace: 'D:\\rsd\\coai',
  createdAt: AT - 10_000,
  updatedAt: AT,
};

const legacy: SavedTab = { id: 'a1', savedAt: AT - 500, title: 'main', passage: 'p', modelId: 'codex', messages: [{ role: 'you', text: 'hi' }] };

const notice = (decision: RestoreDecision): Extract<RestoreDecision, { kind: 'notice' }> => {
  assert.equal(decision.kind, 'notice');

  return decision as Extract<RestoreDecision, { kind: 'notice' }>;
};

// ---------------------------------------------------------------------------------------------
// The decision.
// ---------------------------------------------------------------------------------------------

test('a record restores, with its own revision and its own beginning', () => {
  const decision = restoreDecision({ kind: 'record', record }, legacy, 'D:\\rsd\\coai');

  assert.deepEqual(decision, { kind: 'restore', record });
});

test('absent in the store AND in the memento disposes; absent in the store alone restores the memento copy at baseline 0', () => {
  assert.deepEqual(restoreDecision({ kind: 'absent' }, undefined, 'D:\\rsd\\coai'), { kind: 'dispose' });

  const decision = restoreDecision({ kind: 'absent' }, legacy, 'D:\\rsd\\coai');

  assert.equal(decision.kind, 'restore');
  const restored = (decision as { record: ConversationRecord }).record;
  assert.equal(restored.id, 'a1');
  assert.equal(restored.rev, 0, 'a memento copy has no disk revision; anything else makes the first save a swap against a number nobody read');
  assert.deepEqual(restored.messages, legacy.messages);
  assert.equal(restored.modelId, 'codex');
  assert.equal(restored.workspace, 'D:\\rsd\\coai');
  assert.deepEqual(restored.source, { kind: 'none' });
  assert.equal(restored.createdAt, legacy.savedAt);
});

test('a record this build cannot read keeps the tab, names the cause, and offers no retry', () => {
  const seen: ReadOutcome = { kind: 'incompatible', reason: 'the conversation on disk is not one this build can read' };
  const decision = notice(restoreDecision(seen, legacy, ''));

  assert.equal(decision.sentence, INCOMPATIBLE_NOTICE);
  assert.equal(decision.retry, false, 'a retry was offered for a file that will read the same way every time');
  assert.match(decision.sentence, /newer/u, 'the sentence does not say a newer build most likely wrote it');
  assert.match(decision.sentence, /Nothing has been deleted/u);
  // Even with the memento holding a copy: what is on disk is not ours to replace, and restoring the
  // older memento copy would make the first save meet it and fork — a second conversation for the
  // price of a downgrade.
  assert.equal(restoreDecision(seen, legacy, '').kind, 'notice');
});

test('a disk that would not answer keeps the tab, repeats the store\'s sentence, and offers a retry', () => {
  const decision = notice(restoreDecision({ kind: 'unavailable', reason: 'the conversation could not be read from disk (EACCES)' }, undefined, ''));

  assert.equal(decision.retry, true);
  assert.equal(decision.sentence, unavailableNotice('the conversation could not be read from disk (EACCES)'));
  assert.match(decision.sentence, /EACCES/u, 'the reason the store gave was dropped');
  assert.doesNotMatch(decision.sentence, /[A-Z]:\\|\/home\//u, 'a path reached the page');
  assert.match(decision.sentence, /Nothing has been deleted/u);
});

// ---------------------------------------------------------------------------------------------
// The notice page.
// ---------------------------------------------------------------------------------------------

test('the notice page keeps the conversation id for the next reload, and escapes what it shows', () => {
  const html = noticeHtml('a1', { kind: 'notice', sentence: 'the disk said <no> & "stop"', retry: true }, 'n0nce');

  assert.match(html, /vscode\.setState\(\{"id":"a1"\}\)/u, 'the id is not handed back, so the next reload restores nothing');
  assert.match(html, /the disk said &lt;no&gt; &amp; &quot;stop&quot;/u, 'the sentence reached the page unescaped');
  assert.doesNotMatch(html, /<no>/u);
  assert.match(html, /script-src 'nonce-n0nce'/u, 'the page has no CSP, or one that lets any script run');
  assert.match(html, /<script nonce="n0nce">/u);
  assert.doesNotMatch(html, /onclick=/u, 'an inline handler is a dead button under this CSP');
});

test('the retry button is drawn only when a retry makes sense, and posts the message the host listens for', () => {
  const withRetry = noticeHtml('a1', { kind: 'notice', sentence: 's', retry: true }, 'n');
  const without = noticeHtml('a1', { kind: 'notice', sentence: 's', retry: false }, 'n');

  assert.match(withRetry, new RegExp(`<button id="retry" type="button">${RETRY_LABEL}</button>`, 'u'));
  assert.match(withRetry, new RegExp(`postMessage\\(\\{ type: "${RESTORE_RETRY}" \\}\\)`, 'u'),
    'the button posts something the host does not listen for');
  assert.doesNotMatch(without, /<button/u, 'a retry button was drawn for a file that reads the same way every time');
  assert.match(without, /vscode\.setState/u, 'a tab kept over an unreadable record does not keep its id');
});

test('a script-closing sequence in the id cannot end the page\'s own script', () => {
  const html = noticeHtml('</script><script>alert(1)//', { kind: 'notice', sentence: 's', retry: false }, 'n');

  assert.doesNotMatch(html, /<\/script><script>alert/u, 'the id closed the script element early');
  assert.match(html, /\\u003c\/script/u);
});

// ---------------------------------------------------------------------------------------------
// The `vscode` half, which can only be read.
// ---------------------------------------------------------------------------------------------

const source = (file: string): string => fs.readFileSync(path.join(__dirname, '..', '..', 'src', file), 'utf8');

test('the host disposes a panel in exactly one arm — the one where the conversation is nowhere', () => {
  const panel = source('chatRestorePanel.ts');
  const disposals = panel.split('panel.dispose()').length - 1;

  assert.equal(disposals, 1, `the panel is disposed in ${disposals} places; only "nowhere" may dispose`);
  const arm = panel.slice(panel.indexOf("decision.kind === 'dispose'"), panel.indexOf('panel.dispose()'));
  assert.ok(arm.length > 0 && arm.length < 400, 'the one disposal is not inside the dispose arm');
  assert.match(panel, /decision\.kind === 'restore'[\s\S]{0,120}restoreConversation\(deps\.panels, panel, decision\.record/u,
    'a record is not restored through the one path that builds a chat panel');
  assert.match(panel, /showNotice\(deps, panel, id, decision\)/u, 'the two answers that keep the tab draw nothing');
});

test('the host asks the memento as a fallback and hands both answers to the decision', () => {
  const panel = source('chatRestorePanel.ts');

  assert.match(panel, /restoreDecision\(await deps\.store\.read\(id\), deps\.memento\.saved\(id\), deps\.workspace\(\)\)/u,
    'the decision is not given the store\'s answer and the memento\'s copy together');
});

test('the retry runs the whole decision again, and disposes its listener before a restore can wire the page\'s own', () => {
  const panel = source('chatRestorePanel.ts');
  const retry = panel.slice(panel.indexOf('onDidReceiveMessage'));

  assert.match(retry.slice(0, 900), /listener\.dispose\(\);\s*\n\s*restoreChatTab\(deps, panel, id\)/u,
    'the retry leaves its listener on a panel the chat page is about to wire');
  assert.match(retry.slice(0, 1_400), /\.catch\(/u, 'a detached retry has no owner for its failure');
  assert.match(retry.slice(0, 1_400), /console\.error/u);
  assert.match(retry, /panel\.onDidDispose\(\(\) => listener\.dispose\(\)\)/u, 'the listener outlives the panel');
  assert.match(panel, /panel\.webview\.options = \{ enableScripts: true/u, 'the notice page\'s script is not allowed to run');
  assert.match(panel, /panel\.iconPath = chatTabIcon\(/u, 'a tab kept over a disk fault wears the generic glyph');
});
