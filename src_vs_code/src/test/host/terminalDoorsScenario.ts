import * as assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import * as vscode from 'vscode';

import type { CoaiExports } from '../../extension';
import { answersOf, withAnswer } from '../../remoteRead';
import type { RemoteRun } from '../../remoteSsh';
import { until } from './hostHelpers';

/**
 * The two terminal doors of a `Creds:` terminal, in a real editor (S4-H1 of the E2 stories document): the context key that shows the
 * overflow items, and a real press of *CoAI: take the question*.
 *
 * <p>What a value cannot answer: that the BOUND road really sets `coai.terminalReadsSession` when the active terminal changes and when
 * an answer is written to the real `globalState` (the rule, `readsKey`, is a unit test), and that the registered command reaches the
 * bound road once. A context key cannot be read back from the host, so the scenario reads the last value the wiring set through
 * the extension's TEST-ONLY door (present only in `ExtensionMode.Test`); the `setContext` call itself is made as in production.
 * The ssh call is replaced through the same door by a fake that counts, and no ssh is started.</p>
 *
 * <p><b>What it does not reach.</b> The question INTO A CHAT needs a chat model and a CLI in the host, which CI's editor does not
 * have. So the press is driven with a run that REFUSES: the door shows the road's own sentence as a notice, which the notice ledger
 * (`notifications.jsonl`) holds — that is read. If this host has no chat model, the door stops first with `chat-not-ready` and the
 * scenario then asserts the road was NOT reached (0 runs): either way the count of runs says whether the press went through the
 * road. It runs only in CI's `extension · a real editor` job: a real VS Code does not start on a machine without `libnss3`.</p>
 */

const HOST = 'h';
const SESSION = 's';
const REFUSAL = 'the run seam of S4-H1 refuses every call';

/** How many rows of the notice ledger carry this code and this sentence (a row of an earlier press is counted too: compare before and after). */
function ledgerRows(code: string, sentence: string): number {
  const ledger = path.join(process.env['COAI_DATA_DIR'] ?? '', 'notifications.jsonl');
  const lines = fs.existsSync(ledger) ? fs.readFileSync(ledger, 'utf8').split('\n') : [];
  const quoted = JSON.stringify(sentence).slice(1, -1);

  return lines.filter((line) => line.includes(`"${code}"`) && (sentence === '' || line.includes(quoted))).length;
}

async function boundDoor(): Promise<NonNullable<CoaiExports['remoteSessions']>> {
  const extension = vscode.extensions.getExtension<CoaiExports>('remsoftdev.connect-other-ais');
  assert.ok(extension !== undefined, 'the extension under test is not installed in this host');
  const door = (await extension.activate()).remoteSessions;
  assert.ok(door !== undefined, 'the extension did not hand out its test door in test mode, so nothing below is about the bound road');

  return door;
}

/** Show a terminal and wait until the window says it is the active one. */
async function front(terminal: vscode.Terminal): Promise<void> {
  terminal.show();
  await until(() => vscode.window.activeTerminal === terminal, 'the terminal could not be brought to the front', 5000);
}

/** The context key of the terminal items follows the active terminal and the host's answer. */
export async function theTerminalItemsFollowTheActiveTerminalAndTheAnswer(): Promise<void> {
  const door = await boundDoor();
  const before = answersOf(door.answers.read());
  const creds = vscode.window.createTerminal({ name: `Creds: ${HOST} / ${SESSION}` });
  const plain = vscode.window.createTerminal({ name: 'plain shell' });
  try {
    await door.answers.write(withAnswer([], HOST, 'yes', new Date().toISOString()));

    await front(creds);
    await until(() => door.readsKey() === true, 'a Creds terminal whose host said yes does not show the items');
    await front(plain);
    await until(() => door.readsKey() === false, 'the items are still shown after the active terminal changed to a plain one (the onDidChangeActiveTerminal listener)');
    await front(creds);
    await until(() => door.readsKey() === true, 'the items did not come back with the Creds terminal');

    // An answer written to the real globalState changes the key without the active terminal changing.
    await door.answers.write(withAnswer(door.answers.read(), HOST, 'no', new Date().toISOString()));
    await until(() => door.readsKey() === false, 'a "no" for the host is written and the items are still shown (the key is not set after an answer is written)');
    await door.answers.write(withAnswer(door.answers.read(), HOST, 'yes', new Date().toISOString()));
    await until(() => door.readsKey() === true, 'a "yes" for the host is written and the items are still hidden');
  } finally {
    await door.answers.write(before);
    creds.dispose();
    plain.dispose();
  }
}

/** A real press of *CoAI: take the question* on a Creds terminal reaches the bound road once — or stops at the chat's own readiness, and reaches nothing. */
export async function aPressOnACredsTerminalReachesTheBoundRoadOnce(): Promise<void> {
  const door = await boundDoor();
  const before = answersOf(door.answers.read());
  const calls: (readonly string[])[] = [];
  door.replaceRun(async (_host, _session, operands) => {
    calls.push(operands);

    return { kind: 'refused', refusal: REFUSAL } satisfies RemoteRun;
  });
  const terminal = vscode.window.createTerminal({ name: `Creds: ${HOST} / ${SESSION}` });
  try {
    await door.answers.write(withAnswer([], HOST, 'yes', new Date().toISOString()));
    await front(terminal);
    const refusals = ledgerRows('no-question-from-terminal', REFUSAL);
    const notReady = ledgerRows('chat-not-ready', '');

    await vscode.commands.executeCommand('coai.takeFromTerminal');
    await until(() => ledgerRows('no-question-from-terminal', REFUSAL) > refusals || ledgerRows('chat-not-ready', '') > notReady, 'the press said nothing: neither the road’s refusal nor the chat’s own was noticed');

    if (ledgerRows('no-question-from-terminal', REFUSAL) > refusals) {
      assert.equal(calls.length, 1, `the press reached the run seam ${calls.length} times, not exactly once`);
      assert.deepEqual(calls[0], [SESSION], 'the first call of a press is not the registry path');
    } else {
      assert.equal(calls.length, 0, 'the chat was not ready, and the press still reached ssh');
    }
  } finally {
    door.replaceRun(undefined);
    await door.answers.write(before);
    terminal.dispose();
  }
}
