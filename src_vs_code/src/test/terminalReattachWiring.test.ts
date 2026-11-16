import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { sourceFiles, sourceOf } from './sourceReading';

/**
 * The re-attach of a terminal's conversation, as it is wired (todo/PLAN_the_chat_opens_from_a_terminal_E3.md, S1).
 *
 * <p>Host code, where no seam reaches: `terminalHost.ts`, `extension.ts`, `chatRestorePanel.ts` and `chatCommand.ts` import
 * `vscode`, so their function bodies cannot be called here. These tests read them. The decisions themselves are
 * `terminalReattach.ts`'s and are tested by RUNNING them (`terminalReattach.test.ts`); what no unit test shows is the real
 * editor, which is `terminalReattachScenario.ts` in CI and the owner's R8.</p>
 */

/** The one place the window's terminals become `LiveTerminal`s: the `exited` flag is read from the process's `exitStatus`. */
const EXIT_MAP = /exitStatus\s*!==\s*undefined/u;

test('the window’s terminals are read as LiveTerminal in ONE place', () => {
  const holders = sourceFiles().filter((file) => EXIT_MAP.test(sourceOf(file)));

  // The scan's own proof: it still finds the one map, so a reformatted line cannot make it match nothing and pass for ever.
  assert.deepEqual(holders, ['terminalHost.ts'], `the exitStatus map of LiveTerminal is in ${holders.join(', ')}, not only in terminalHost.ts`);
  for (const file of ['chatReturnCommand.ts', 'remoteSessionHost.ts']) {
    assert.ok(sourceOf(file).includes('liveTerminals('), `${file} does not read the window's terminals through liveTerminals(`);
  }
});
