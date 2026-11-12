import * as assert from 'node:assert/strict';
import * as vscode from 'vscode';

import type { CoaiExports } from '../../extension';
import { answersOf, consentOf, withAnswer } from '../../remoteRead';
import type { RemoteRun } from '../../remoteSsh';
import { until } from './hostHelpers';

/**
 * The per-host consent and the picked id of the BOUND road, in a real editor (S3-H1 of the E2 stories document, the chat reads the
 * remote Claude session).
 *
 * <p>What a value cannot answer: that `bindRemoteSessions` really binds the road of this window to the profile's own `globalState`
 * (a `Memento` that is not a map), that a press on a terminal named like a CredsForDevs one — really open in this window — stops at a
 * remembered "no" with its own sentence and spawns nothing, goes on to the ssh seam after a "yes", keeps the id of a title pick, and
 * loses that id when the terminal is really closed (`onDidCloseTerminal`). The unit tests (`remoteRead.test.ts`) fake the store.
 * The run seam here replaces the bound road's ssh call through the extension's TEST-ONLY door (present only in
 * `ExtensionMode.Test`): it answers with frames and counts; no ssh is started. The modal and the pick list are not driven (a test
 * cannot answer them): the consent is set through the store, as the road writes it, and the pick is a title pick of ONE session,
 * which asks nobody. It runs only in CI's `extension · a real editor` job: a real VS Code does not start on a machine without `libnss3`.</p>
 */

const HOST = 'h';
const SESSION = 's';
const ID = '0b2e6a52-1f6c-4d0e-8a43-1c9d1d5e7f10';
const REFUSAL = 'the run seam of S3-H1 answers nothing';

const frame = (...lines: readonly string[]): RemoteRun =>
  ({ kind: 'answered', code: 0, stdout: ['COAI-BEGIN 1', ...lines, 'COAI-END', ''].join('\n') });

/** The fallback's answers: the pane's folder, then ONE session whose title is the tmux session's name, then a refusal for its tail. */
function titleRoad(operands: readonly string[]): RemoteRun {
  if (operands.length === 1) {
    return frame('COAI-PANE 0.0 %1 1000', 'COAI-CWD', ':/home/git/kb', 'COAI-FOLDERS', ':-home-git-kb', 'COAI-FOLDERS-END 0 0');
  }

  return operands[1] === 'titles'
    ? frame('COAI-FOLDER -home-git-kb ok', `COAI-SESSION ${ID} 100 9 rows`, `:{"type":"ai-title","aiTitle":"${SESSION}"}`, 'COAI-ROWS 0 0', 'COAI-SCAN 1 all')
    : { kind: 'refused', refusal: REFUSAL };
}

export async function theConsentOfAHostIsKeptInTheRealGlobalState(): Promise<void> {
  const extension = vscode.extensions.getExtension<CoaiExports>('remsoftdev.connect-other-ais');
  assert.ok(extension !== undefined, 'the extension under test is not installed in this host');
  const door = (await extension.activate()).remoteSessions;
  assert.ok(door !== undefined, 'the extension did not hand out its test door in test mode, so nothing below is about the bound road');

  const calls: (readonly string[])[] = [];
  let answer: (operands: readonly string[]) => RemoteRun = () => ({ kind: 'refused', refusal: REFUSAL });
  door.replaceRun(async (_host, _session, operands) => {
    calls.push(operands);

    return answer(operands);
  });
  const before = answersOf(door.answers.read());
  const terminal = vscode.window.createTerminal({ name: `Creds: ${HOST} / ${SESSION}` });
  try {
    terminal.show();
    await door.answers.write(withAnswer(before, HOST, 'no', new Date().toISOString()));
    assert.equal(consentOf(door.answers.read(), HOST), 'no', 'a "no" written through globalState was not read back from it');

    const off = await door.read(terminal, 'question');
    assert.equal(calls.length, 0, 'ssh was spawned for a host nobody agreed to');
    assert.equal(off.kind, 'refused', 'a press on a host answered "no" did not refuse');
    assert.ok(off.kind === 'refused' && off.refusal.includes('is turned off'), `the refusal is not the "turned off" sentence: ${JSON.stringify(off)}`);

    await door.answers.write(withAnswer(door.answers.read(), HOST, 'yes', new Date().toISOString()));
    const fresh = answersOf(door.answers.read());
    assert.ok(fresh.some((row) => row.host === HOST && row.answer === 'yes'), `the "yes" is not in the list read back from globalState: ${JSON.stringify(fresh)}`);

    const on = await door.read(terminal, 'question');
    assert.equal(calls.length, 1, `a press on a host answered "yes" did not reach the run seam exactly once (${calls.length} calls)`);
    assert.deepEqual(calls[0], [SESSION], 'the first call of a press is not the registry path');
    assert.deepEqual(on, { kind: 'refused', refusal: REFUSAL }, 'the run seam\'s refusal did not come back as the answer');

    // A title pick of one session: three calls, the last refused — the id stays with the terminal, and goes when the terminal is closed.
    answer = titleRoad;
    calls.length = 0;
    await door.read(terminal, 'question');
    assert.equal(calls.length, 3, `the fallback made ${calls.length} calls, not three`);
    assert.equal(door.pickedOf(terminal)?.id, ID, 'the id of the title pick was not kept for the terminal after a refused tail');

    terminal.dispose();
    await until(() => door.pickedOf(terminal) === undefined, 'the picked id of a closed terminal is still kept');
  } finally {
    door.replaceRun(undefined);
    await door.answers.write(before);
    terminal.dispose();
  }
}
