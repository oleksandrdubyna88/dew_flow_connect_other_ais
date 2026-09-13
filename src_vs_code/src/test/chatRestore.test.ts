import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';
import {
  INCOMPATIBLE_NOTICE,
  MIGRATION_WAIT_MS,
  RESTORE_RETRY,
  RESTORING_NOTICE,
  RETRYING_LABEL,
  RETRY_LABEL,
  RestoreDecision,
  noticeHtml,
  persistedId,
  restoreDecision,
  restoringHtml,
  unavailableNotice,
} from '../chatRestore';
import { CONVERSATION_VERSION, ConversationRecord } from '../chatStore';
import { ReadOutcome } from '../chatStoreFile';
import { SavedTab } from '../chatTabs';

/**
 * What a reload does about each of the store's four answers — the decision as values, the two pages
 * as text, and the `vscode` half read as source.
 *
 * <p>Story A2 made the store's read typed for exactly one consumer: the serializer, which disposed a
 * panel over "no record" and must not do that over a permissions error or a record a newer build
 * wrote. This is where that consumer is held to it. The two answers that keep the tab get a DEFINED
 * tab — a sentence, the id preserved for the next reload, and a retry where one makes sense — not an
 * undisposed panel with no handlers, which is a blank tab with no explanation. And a tab is never
 * blank while the migration runs: it says it is restoring, and it waits only so long.</p>
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
// The boundary: what the reload handed over.
// ---------------------------------------------------------------------------------------------

test('the persisted id is taken only when it is one this build could have minted and can file', () => {
  assert.equal(persistedId({ id: 'a1' }), 'a1');
  assert.equal(persistedId({ id: '0f1c9d2e-7b3a-4c5d-8e9f-0a1b2c3d4e5f' }), '0f1c9d2e-7b3a-4c5d-8e9f-0a1b2c3d4e5f');
  for (const state of [undefined, null, 'a1', {}, { id: 7 }, { id: '' }, { id: '../x' }, { id: 'a1.meta' }, { id: 'a b' }]) {
    assert.equal(persistedId(state), '', `a state nothing can be filed under was taken as an id: ${JSON.stringify(state)}`);
  }
});

test('the ceiling on the wait is seconds, not minutes — long enough for twenty records behind locks, short enough to wait for', () => {
  assert.ok(MIGRATION_WAIT_MS >= 3_000 && MIGRATION_WAIT_MS <= 10_000, `${MIGRATION_WAIT_MS} ms is not a wait a person tolerates on a tab`);
});

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
// The two pages.
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

test('the retry button is drawn only when a retry makes sense, posts the message the host listens for, and says it is retrying', () => {
  const withRetry = noticeHtml('a1', { kind: 'notice', sentence: 's', retry: true }, 'n');
  const without = noticeHtml('a1', { kind: 'notice', sentence: 's', retry: false }, 'n');

  assert.match(withRetry, new RegExp(`<button id="retry" type="button">${RETRY_LABEL}</button>`, 'u'));
  assert.match(withRetry, new RegExp(`postMessage\\(\\{ type: "${RESTORE_RETRY}" \\}\\)`, 'u'),
    'the button posts something the host does not listen for');
  assert.match(withRetry, new RegExp(`retry\\.textContent = "${RETRYING_LABEL}"`, 'u'),
    'a disabled button that says nothing while the retry runs reads as broken');
  assert.ok(withRetry.indexOf('retry.disabled = true') < withRetry.indexOf('postMessage'), 'the button is disabled after the message, so a second press can send a second retry');
  assert.doesNotMatch(without, /<button/u, 'a retry button was drawn for a file that reads the same way every time');
  assert.match(without, /vscode\.setState/u, 'a tab kept over an unreadable record does not keep its id');
});

test('the restoring page says what the tab is doing, keeps the id, and has no button', () => {
  const html = restoringHtml('a1', 'n');

  assert.match(html, /Restoring this conversation/u);
  assert.ok(html.includes(RESTORING_NOTICE));
  assert.match(html, /vscode\.setState\(\{"id":"a1"\}\)/u, 'a tab reloaded again while restoring would come back with no id');
  assert.doesNotMatch(html, /<button/u);
  assert.match(html, /script-src 'nonce-n'/u);
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

test('the host disposes a panel in exactly two arms, and both mean "nowhere"', () => {
  const panel = source('chatRestorePanel.ts');
  const disposals = panel.split('panel.dispose()').length - 1;

  assert.equal(disposals, 2, `the panel is disposed in ${disposals} places; only an id nothing can be filed under and a conversation that is nowhere may dispose`);
  const noId = panel.slice(panel.indexOf('if (id.length === 0)'), panel.indexOf('panel.dispose()'));
  assert.ok(noId.length > 0 && noId.length < 500, 'the first disposal is not inside the no-id arm');
  const nowhere = panel.slice(panel.indexOf("decision.kind === 'dispose'"), panel.lastIndexOf('panel.dispose()'));
  assert.ok(nowhere.length > 0 && nowhere.length < 400, 'the second disposal is not inside the dispose arm');
  assert.match(panel, /decision\.kind === 'restore'[\s\S]{0,120}restoreConversation\(deps\.panels, panel, decision\.record/u,
    'a record is not restored through the one path that builds a chat panel');
  assert.match(panel, /showNotice\(deps, panel, id, decision\)/u, 'the two answers that keep the tab draw nothing');
});

test('the host draws the tab BEFORE it waits, waits under the ceiling, and only then reads the store', () => {
  // Four findings from three reviewers, one defect: a person reloading with ten tabs must never see
  // ten blank panels for as long as a migration takes.
  const panel = source('chatRestorePanel.ts');
  const body = panel.slice(panel.indexOf('export async function restoreAfterReload'), panel.indexOf('export async function restoreChatTab'));

  assert.match(body, /const id = persistedId\(state\);/u, 'the id is not validated at the boundary');
  const drawn = body.indexOf('draw(deps, panel, restoringHtml(id, nonce()))');
  const waited = body.indexOf('await withinCeiling(migration, MIGRATION_WAIT_MS)');
  const read = body.indexOf('await restoreChatTab(deps, panel, id');
  assert.ok(drawn !== -1, 'nothing is drawn while the migration runs — a blank tab');
  assert.ok(waited !== -1, 'the migration is waited for without a ceiling, or not at all');
  assert.ok(read !== -1);
  assert.ok(drawn < waited && waited < read, 'the tab is drawn after the wait, or the store is read before the migration has had its say');
  assert.match(panel, /clearTimeout\(timer\)/u, 'a fast migration leaves a timer ticking');
});

test('a tab closed while the host waits is left alone: nothing is decided for a panel that has been disposed', () => {
  // CodeRabbit, PR #223. The wait is up to five seconds and a person can close the tab inside it. The
  // restore then went on into a panel that no longer existed — and both arms of it assign
  // `panel.webview.html`, which throws on a disposed panel, into the serializer's handler. Nothing
  // leaks on this path (`restoreConversation` registers the panel only after `createChatPanel`
  // returns); it is the throw. So disposal is recorded BEFORE anything is awaited, and the decision is
  // applied only to a panel that is still open — after the wait, and after the read that follows it.
  const panel = source('chatRestorePanel.ts');
  const body = panel.slice(panel.indexOf('export async function restoreAfterReload'), panel.indexOf('export async function restoreChatTab'));
  const watched = body.indexOf('panel.onDidDispose(');
  const waited = body.indexOf('await withinCeiling(migration, MIGRATION_WAIT_MS)');

  assert.ok(watched !== -1, 'the host does not notice the panel being closed while it waits');
  assert.ok(watched < waited, 'disposal is watched for only after the wait, so a close during the wait is missed');
  assert.match(body.slice(waited), /if \(!open\(\)\)\s*\{\s*\n\s*return;/u, 'the restore goes on into a panel that has been disposed');
  const decided = panel.slice(panel.indexOf('export async function restoreChatTab'), panel.indexOf('function showNotice'));
  assert.match(decided, /restoreDecision\(await deps\.store\.read\(id\)[\s\S]{0,160}if \(!open\(\)\)\s*\{\s*\n\s*return;/u,
    'the decision is applied to a panel that was closed while the store was being read');
});

test('a store answer this module has no arm for is a defect it names, not a notice it invents', () => {
  // The switch is exhaustive: `unavailable` has its arm by name, and what remains is `never`, so a
  // variant added to ReadOutcome without an arm here is a compile error. A value that reaches the
  // default at runtime — which the types forbid — throws with the value in the message, rather than
  // drawing a retry notice whose reason is the word undefined. (CodeRabbit, PR #223.)
  assert.throws(() => restoreDecision({ kind: 'lost' } as unknown as ReadOutcome, undefined, ''), /lost/u);
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
