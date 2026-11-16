import * as assert from 'node:assert/strict';
import * as vscode from 'vscode';

import type { CoaiExports } from '../../extension';
import { until } from './hostHelpers';

/**
 * The skip-shell check writes the person's USER setting, once, after the press, in a real editor (S3-H1 of the E3 stories document).
 *
 * <p>What a value cannot answer: that `ConfigurationTarget.Global` really lands in the user layer a real host reports through
 * `inspect().globalValue`, that the entries the person had stay first and in order, and that a second check after the answer is
 * remembered in the profile's real `globalState` asks nobody. The offer is replaced through the extension's TEST-ONLY door (present
 * only in `ExtensionMode.Test`) by a fake that answers **Add** and counts: a test cannot press a button. The editor's user setting and
 * the memory key are saved first and put back in a `finally`, also when an assertion fails. It runs only in CI's
 * `extension · a real editor` job: a real VS Code does not start on a machine without `libnss3`.</p>
 *
 * <p>The write of the setting fires the bound check through its own change listener, so the first answer may come from that run and
 * not from the explicit one below; both go through the same latch and the same fake, so the count is the same either way.</p>
 */

const SECTION = 'terminal.integrated';
const SETTING = 'commandsToSkipShell';
const THEIRS = 'workbench.action.quickOpen';
const WANTED = [THEIRS, 'coai.askFromTerminal', 'coai.goToFromTerminal'];

const userValue = (): unknown => vscode.workspace.getConfiguration(SECTION).inspect<unknown>(SETTING)?.globalValue;

export async function theSkipListAddAppendsToTheUserSettingOnce(): Promise<void> {
  const extension = vscode.extensions.getExtension<CoaiExports>('remsoftdev.connect-other-ais');
  assert.ok(extension !== undefined, 'the extension under test is not installed in this host');
  const door = (await extension.activate()).skipShell;
  assert.ok(door !== undefined, 'the extension did not hand out its skip-shell test door in test mode, so nothing below is about the bound check');

  const savedValue = userValue();
  const savedMemory = door.memory.read();
  let asks = 0;
  door.replaceAsk(async () => {
    asks += 1;

    return 'Add';
  });
  try {
    await door.memory.write(undefined);
    await vscode.workspace.getConfiguration(SECTION).update(SETTING, [THEIRS], vscode.ConfigurationTarget.Global);
    await door.check();
    await until(() => JSON.stringify(userValue()) === JSON.stringify(WANTED), `the user value of ${SECTION}.${SETTING} never became the person's entry followed by both ids`);
    assert.deepEqual(userValue(), WANTED, 'the real inspect().globalValue is not the person\'s entry first and the two ids after it, once each');
    assert.equal(asks, 1, `the offer was shown ${asks} times for one list, not once`);

    await door.check();
    assert.equal(asks, 1, 'the offer came back after the ids were added');
    assert.deepEqual(userValue(), WANTED, 'a second check changed the user value');
  } finally {
    door.replaceAsk(undefined);
    await vscode.workspace.getConfiguration(SECTION).update(SETTING, savedValue, vscode.ConfigurationTarget.Global);
    await door.memory.write(savedMemory);
  }
}
