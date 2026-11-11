import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { sourceOf } from './sourceReading';

/**
 * The test door of the bound remote road is not an export of the shipped extension (E2.S3 review).
 *
 * <p>`CoaiExports` is what ANY other extension gets from `activate()`. A door that writes a host's consent into this profile's
 * `globalState` or replaces the ssh call must therefore exist only when the extension runs in test mode. `extension.ts` imports
 * `vscode`, so no test can run it: this is a source check over the one place the field is built, with its controls.</p>
 */

const text = sourceOf('extension.ts');

test('the remote test door is built only behind the test-mode check', () => {
  const built = [...text.matchAll(/remoteSessions\s*:\s*testDoorOf\(/gu)];
  assert.equal(built.length, 1, `the scan expected ONE place that builds the door, found ${built.length}`);
  const before = text.slice(Math.max(0, (built[0]?.index ?? 0) - 160), built[0]?.index ?? 0);
  assert.match(before, /extensionMode\s*===\s*vscode\.ExtensionMode\.Test\s*\?\s*\{\s*$/u, 'the remote test door is reachable outside test mode: it is not built behind ExtensionMode.Test');
  assert.match(text, /readonly remoteSessions\?:\s*RemoteSessionsTestDoor/u, 'the field of CoaiExports is not optional, so production would have to supply it');
});

test('nothing else in the extension builds a remote road for a caller', () => {
  assert.ok(!/remoteSessionsOf\(/u.test(text), 'extension.ts builds a second road beside the bound one');
  assert.equal([...text.matchAll(/bindRemoteSessions\(/gu)].length, 1, 'the road is not bound exactly once');
});
