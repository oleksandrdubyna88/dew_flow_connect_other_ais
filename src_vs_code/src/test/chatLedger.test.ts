import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  ChildRecord,
  NEAR_ENOUGH_MS,
  forgotten,
  imageOf,
  isOurs,
  ledgerText,
  livingQuery,
  parseLedger,
  parseLiving,
  recorded,
} from '../chatLedger';

/**
 * The ledger that stops a force-killed editor from leaving an authenticated CLI running.
 *
 * <p>Every rule here is about the ONE dangerous thing this feature does: kill a process by a number
 * written down earlier. The launcher's own comment says why that is dangerous — Windows reuses pids
 * — and these are the tests that make it safe.</p>
 */

const ours: ChildRecord = { pid: 4242, image: 'agy.exe', startedMs: 1_700_000_000_000 };

test('a child is written down, and struck out when it ends', () => {
  const one = recorded([], ours);
  assert.deepStrictEqual([...one], [ours]);
  assert.deepStrictEqual([...forgotten(one, 4242)], []);
});

test('a pid that comes round again replaces its old row rather than doubling it', () => {
  const later: ChildRecord = { pid: 4242, image: 'codex.cmd', startedMs: 1_700_000_900_000 };

  assert.deepStrictEqual([...recorded([ours], later)], [later]);
});

test('forgetting a pid nobody recorded changes nothing', () => {
  assert.deepStrictEqual([...forgotten([ours], 999)], [ours]);
});

test('a pid nothing holds is already gone, and is not killed', () => {
  assert.strictEqual(isOurs(ours, undefined), false);
});

test('a pid held by ANOTHER program is a recycled number, and is left alone', () => {
  // The whole reason this module exists. Killing it would be worse than the orphan it was meant to
  // clean up: the number belongs to whatever the operating system handed it to next.
  assert.strictEqual(isOurs(ours, { image: 'chrome.exe', createdMs: ours.startedMs }), false);
});

test('the same program started at ANOTHER time is somebody else’s run of it', () => {
  // This is the case that would hurt most: a person's own `agy`, running their own work, killed by
  // an extension tidying up after a crash it had nothing to do with.
  assert.strictEqual(
    isOurs(ours, { image: 'agy.exe', createdMs: ours.startedMs + NEAR_ENOUGH_MS + 1 }),
    false,
  );
  assert.strictEqual(
    isOurs(ours, { image: 'agy.exe', createdMs: ours.startedMs - NEAR_ENOUGH_MS - 1 }),
    false,
  );
});

test('the same program at the same moment is ours, within a tolerance for two clocks', () => {
  assert.strictEqual(isOurs(ours, { image: 'agy.exe', createdMs: ours.startedMs }), true);
  assert.strictEqual(isOurs(ours, { image: 'agy.exe', createdMs: ours.startedMs + 900 }), true);
  assert.strictEqual(isOurs(ours, { image: 'agy.exe', createdMs: ours.startedMs - 900 }), true);
});

test('an image name is compared as the operating system reports it, whatever path it came from', () => {
  assert.strictEqual(imageOf('C:\\Users\\somebody\\AppData\\Local\\agy\\bin\\AGY.EXE'), 'agy.exe');
  assert.strictEqual(imageOf('/usr/local/bin/claude'), 'claude');
  assert.strictEqual(imageOf('codex.cmd'), 'codex.cmd');
  assert.strictEqual(imageOf(''), '');
});

test('a ledger a force-kill left half-written is no ledger, not a crash at activation', () => {
  // This file is read while the extension is starting. A half-written line is exactly the shape the
  // failure it exists for leaves behind, and it must cost at most one missed tidy-up.
  for (const junk of ['', '[{"pid":42', 'null', '"a string"', '{}', '[1,2,3]']) {
    assert.deepStrictEqual([...parseLedger(junk)], [], `it read something out of: ${junk}`);
  }
});

test('a row missing any of the three facts is not a row worth killing on', () => {
  const text = JSON.stringify([
    { pid: 0, image: 'agy.exe', startedMs: 1 },
    { pid: -3, image: 'agy.exe', startedMs: 1 },
    { pid: 1.5, image: 'agy.exe', startedMs: 1 },
    { pid: 7, image: '', startedMs: 1 },
    { pid: 8, image: 'agy.exe' },
    { pid: 9, image: 'agy.exe', startedMs: 'soon' },
    ours,
  ]);

  assert.deepStrictEqual([...parseLedger(text)], [ours]);
});

test('what is written is what comes back', () => {
  const entries = recorded(recorded([], ours), { pid: 77, image: 'CLAUDE.EXE', startedMs: 5 });

  assert.deepStrictEqual(
    [...parseLedger(ledgerText(entries))],
    [ours, { pid: 77, image: 'claude.exe', startedMs: 5 }],
  );
});

test('the question asked of Windows names the pid and returns two facts', () => {
  const query = livingQuery(4242);

  assert.match(query, /ProcessId=4242/);
  assert.match(query, /Get-CimInstance/, 'Get-Process reports a name with no extension');
  assert.match(query, /CreationDate/, 'without the start time a recycled pid is indistinguishable');
});

test('the answer is read as two facts, and anything else is no answer at all', () => {
  // Measured on this machine, from the real query: `pwsh.exe|1788944157495`.
  assert.deepStrictEqual(parseLiving('pwsh.exe|1788944157495'), { image: 'pwsh.exe', createdMs: 1788944157495 });
  assert.deepStrictEqual(parseLiving('  AGY.EXE|1788944157495  \n'), { image: 'agy.exe', createdMs: 1788944157495 });

  // Every unhappy shape means the same thing: this side cannot prove the pid is ours, so nothing is
  // killed. A guard that failed OPEN would kill strangers.
  for (const nothing of ['', 'agy.exe', '|123', 'agy.exe|', 'agy.exe|soon', 'agy.exe|0', 'Get-CimInstance : failed']) {
    assert.strictEqual(parseLiving(nothing), undefined, `it read a process out of: ${nothing}`);
  }
});
