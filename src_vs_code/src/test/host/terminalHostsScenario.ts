import * as assert from 'node:assert/strict';
import * as vscode from 'vscode';

import type { CoaiExports } from '../../extension';
import { answersOf, consentOf, withAnswer } from '../../remoteRead';

/**
 * The per-host consent through the REAL `globalState`, in a real editor (S3-H1 of the E2 stories document, the chat reads the remote
 * Claude session).
 *
 * <p>What a value cannot answer: that the answers are written to, and read back from, the profile's own `globalState` (a
 * `Memento` that is not a map), and that a press on a terminal named like a CredsForDevs one — really open in this window —
 * stops at a remembered "no" with its own sentence and spawns nothing, and goes on to the run seam after a "yes". The unit tests
 * (`remoteRead.test.ts`) fake the store. The run seam here is a fake that only counts: no ssh is started. The modal and the pick
 * list are not driven (they cannot be answered by a test): the consent is set through the store, as the road writes it. It runs
 * only in CI's `extension · a real editor` job: a real VS Code does not start on a machine without `libnss3`.</p>
 */

const HOST = 'h';
const SESSION = 's';

export async function theConsentOfAHostIsKeptInTheRealGlobalState(): Promise<void> {
  const extension = vscode.extensions.getExtension<CoaiExports>('remsoftdev.connect-other-ais');
  assert.ok(extension !== undefined, 'the extension under test is not installed in this host');
  const exported = await extension.activate();

  const calls: (readonly string[])[] = [];
  const sessions = exported.remoteSessions(async (_host, _session, operands) => {
    calls.push(operands);

    return { kind: 'refused', refusal: 'the run seam of S3-H1 answers nothing' };
  });
  const before = answersOf(sessions.answers.read());
  const terminal = vscode.window.createTerminal({ name: `Creds: ${HOST} / ${SESSION}` });
  try {
    terminal.show();
    await sessions.answers.write(withAnswer(before, HOST, 'no', new Date().toISOString()));
    assert.equal(consentOf(exported.remoteSessions().answers.read(), HOST), 'no', 'a "no" written through globalState was not read back from it');

    const off = await sessions.read(terminal, 'question');
    assert.equal(calls.length, 0, 'ssh was spawned for a host nobody agreed to');
    assert.equal(off.kind, 'refused', 'a press on a host answered "no" did not refuse');
    assert.ok(off.kind === 'refused' && off.refusal.includes('is turned off'), `the refusal is not the "turned off" sentence: ${JSON.stringify(off)}`);

    await sessions.answers.write(withAnswer(sessions.answers.read(), HOST, 'yes', new Date().toISOString()));
    const fresh = answersOf(exported.remoteSessions().answers.read());
    assert.ok(fresh.some((row) => row.host === HOST && row.answer === 'yes'), `the "yes" is not in the list read back from globalState: ${JSON.stringify(fresh)}`);

    const on = await sessions.read(terminal, 'question');
    assert.equal(calls.length, 1, `a press on a host answered "yes" did not reach the run seam exactly once (${calls.length} calls)`);
    assert.deepEqual(calls[0], [SESSION], 'the first call of a press is not the registry path');
    assert.deepEqual(on, { kind: 'refused', refusal: 'the run seam of S3-H1 answers nothing' }, 'the run seam\'s refusal did not come back as the answer');
  } finally {
    await sessions.answers.write(before);
    terminal.dispose();
  }
}
