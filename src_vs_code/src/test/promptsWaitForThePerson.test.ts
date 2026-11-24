import '../../scripts/inRunFolder.cjs';
import * as fs from 'node:fs';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { blanked } from './blankedSource';

/**
 * Every VS Code prompt goes through `askPerson` (research/PLAN_busy_mark_pauses_while_you_type.md §3.5).
 *
 * <p>Structural, because a prompt is `vscode.window.*` and no unit test here has a VS Code. What `askPerson` DOES is run
 * in `personWait.test.ts` and `inFlight.test.ts`; what is read here is that nobody opens a box without it. One prompt
 * left out is one panel action whose bar runs over the person's typing again — the defect the operator asked to have
 * gone ("пока печатаю не считаем", 2026-10-02) — and nothing else would notice.</p>
 */

const SRC = path.join(__dirname, '..', '..', 'src');

/** Every source file of the extension, tests excluded, blanked so a comment naming a prompt is not a prompt. */
function sources(): readonly { readonly file: string; readonly text: string }[] {
  return fs.readdirSync(SRC, { recursive: true, encoding: 'utf8' })
    .filter((file) => file.endsWith('.ts') && !file.split(/[\\/]/u).includes('test'))
    .map((file) => ({ file, text: blanked(fs.readFileSync(path.join(SRC, file), 'utf8')) }));
}

const PROMPT = /\bwindow\.(?:showInputBox|showQuickPick|showOpenDialog|showSaveDialog)\(/gu;
/** What sits right before `window.` in a wrapped call. */
const WRAPPED = 'askPerson(() => vscode.';

/** Every prompt call in the given (blanked) sources, with whether the text right before it is the wrapper. */
function promptsIn(files: readonly { readonly file: string; readonly text: string }[]): readonly { readonly where: string; readonly wrapped: boolean }[] {
  return files.flatMap(({ file, text }) => [...text.matchAll(PROMPT)].map((match) => {
    const line = text.slice(0, match.index).split('\n').length;

    return { where: `${file}:${line}`, wrapped: text.slice(match.index - WRAPPED.length, match.index) === WRAPPED };
  }));
}

function prompts(): readonly { readonly where: string; readonly wrapped: boolean }[] {
  return promptsIn(sources());
}

test('the scan finds an unwrapped prompt, passes a wrapped one, and ignores one named in a comment', () => {
  // A structural test that matches nothing passes forever (plan round, gemini): this is the scan, shown a known instance.
  const fixture = blanked([
    'const a = await vscode.window.showInputBox({ prompt: \'x\' });',
    'const b = await askPerson(() => vscode.window.showQuickPick([\'y\']));',
    '// vscode.window.showOpenDialog( is only mentioned here',
  ].join('\n'));

  assert.deepEqual(promptsIn([{ file: 'fixture.ts', text: fixture }]), [
    { where: 'fixture.ts:1', wrapped: false },
    { where: 'fixture.ts:2', wrapped: true },
  ]);
});

test('every input box, quick pick and file dialog is opened through askPerson', () => {
  const found = prompts();
  assert.ok(found.length >= 30, `only ${found.length} prompts were found, so the scan is not reading the source`);

  assert.deepEqual(found.filter((one) => !one.wrapped).map((one) => one.where), [],
    'a prompt opened without askPerson counts the person’s typing as the panel working');
});

// 15 → 14 on 2026-10-04 (research/PLAN_bugz_keys_per_server.md): the contributor-key box moved to the keys host's
// askForContributorKey, still through askPerson, so the sidebar button and the command share one flow.
// 14 → 13 in v2 E3.S2: the split-order model picker's "another model…" box went with the command models.
test('the panel provider’s own thirteen prompts are among them', () => {
  assert.equal(prompts().filter((one) => one.where.startsWith('panelProvider.ts:')).length, 13,
    'a prompt was added or removed in the panel provider — check it goes through askPerson, then update this count');
});

test('nobody starts a prompt and walks away from it — a box nobody awaits would pause work that is still running', () => {
  const detached = sources().filter(({ text }) => /\bvoid\s+askPerson\(/u.test(text) || /\bvoid\s+notifyAndAsk\(/u.test(text));

  assert.deepEqual(detached.map((one) => one.file), []);
});

test('notifyAndAsk waits for the person through askPerson; the doors that do not wait are left alone', () => {
  const notify = blanked(fs.readFileSync(path.join(SRC, 'notify.ts'), 'utf8'));
  const asking = notify.slice(notify.indexOf('export async function notifyAndAsk('));
  const body = asking.slice(0, asking.indexOf('\n}'));

  assert.match(body, /await askPerson\(\(\) => show\(notice\)\)/u, 'a modal question would count the person reading it as work');
  assert.equal([...notify.matchAll(/askPerson\(/gu)].length, 1, 'notify, notifyOnce and notifyThen never wait, so never pause');
});

test('the two createQuickPick sites are the conversation picker and the accounts list, which nothing awaits', () => {
  // `switchConversations` shows the picker and returns: no operation waits on it, so there is nothing to pause. A third
  // createQuickPick is a new decision, not these two — this goes red so it is made.
  // The second was made on 2026-11-24 (E1.S4 of the accounts plan): `openList` of `remoteAccountsHost.ts` shows the list and returns, and its handlers
  // are detached; no operation waits on it, and what a press starts (a round, a question) is its own and goes through its own door.
  const sites = sources().filter(({ text }) => /\bwindow\.createQuickPick\b|\bwindow\.createInputBox\b/u.test(text));

  assert.deepEqual(sites.map((one) => one.file), ['conversationPickerCommand.ts', 'remoteAccountsHost.ts']);
});
