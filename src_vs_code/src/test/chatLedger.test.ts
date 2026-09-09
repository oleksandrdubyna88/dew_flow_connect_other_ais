import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  ChildRecord,
  FORGET_AFTER_MS,
  NEAR_ENOUGH_MS,
  forgotten,
  imageOf,
  killOutcome,
  ledgerName,
  ledgerText,
  ownerOf,
  parseLedger,
  recorded,
  safeImage,
  settled,
  tooOld,
  verifyAndKill,
  worthAsking,
} from '../chatLedger';

/**
 * The ledger that stops a force-killed editor from leaving an authenticated CLI running.
 *
 * <p>Every rule here is about the ONE dangerous thing this feature does: end a process that a file
 * says was ours. The launcher's own comment explains why that is dangerous — a pid is not an
 * identity and Windows hands used numbers out again — and these are the tests that make it safe.</p>
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

test('the ledger is named after the window that owns it, and reads back the same way', () => {
  // `globalStorageUri` is shared by every VS Code window. One file would mean one window's
  // activation reading another window's LIVE children — and killing them, because they really are
  // this extension's. A person with two windows would have watched a working conversation die.
  assert.strictEqual(ledgerName(9182), 'chat-children-9182.json');
  assert.strictEqual(ownerOf('chat-children-9182.json'), 9182);
});

test('a file that is not one of ours has no owner, so nothing reads it', () => {
  for (const stranger of ['settings.json', 'chat-children.json', 'chat-children-.json', 'chat-children-x.json', '']) {
    assert.strictEqual(ownerOf(stranger), 0, `it claimed ownership of: ${stranger}`);
  }
});

test('a record too old to mean anything is not asked about at all', () => {
  // Past a week the number means nothing on any machine that has rebooted, and a file nobody can
  // resolve would otherwise grow for ever.
  const now = ours.startedMs + FORGET_AFTER_MS + 1;

  assert.strictEqual(tooOld(ours, now), true);
  assert.strictEqual(tooOld(ours, ours.startedMs + FORGET_AFTER_MS - 1), false);
  assert.deepStrictEqual([...worthAsking([ours], now)], []);
  assert.deepStrictEqual([...worthAsking([ours], ours.startedMs + 1000)], [ours]);
});

test('an image name that could not appear in a command is never put in one', () => {
  // It comes from a reviewer's `executablePath`, which is a person's own text, and the
  // verify-and-kill command carries it. A file name has no business holding a quote.
  for (const bad of ["agy'.exe", 'agy" ; calc', 'a b.exe', 'agy$x', '', '..', "'"]) {
    assert.strictEqual(safeImage(bad), false, `it would have put this in a script: ${bad}`);
  }
  for (const fine of ['agy.exe', 'codex.cmd', 'claude', 'node-22.exe', 'a_b+c.exe']) {
    assert.strictEqual(safeImage(fine), true, `it refused an ordinary name: ${fine}`);
  }
  assert.deepStrictEqual([...worthAsking([{ ...ours, image: "a'b" }], ours.startedMs)], []);
});

test('an image name is compared as the operating system reports it, whatever path it came from', () => {
  assert.strictEqual(imageOf('C:\\Users\\somebody\\AppData\\Local\\agy\\bin\\AGY.EXE'), 'agy.exe');
  assert.strictEqual(imageOf('/usr/local/bin/claude'), 'claude');
  assert.strictEqual(imageOf('codex.cmd'), 'codex.cmd');
  assert.strictEqual(imageOf(''), '');
});

test('the command checks all three facts and ends a TREE, in one invocation', () => {
  // One invocation, because a query followed by a kill is a window in which the verified process can
  // exit and its number be handed on — and small windows around killing are the thing this module
  // exists to close. A tree, because a Windows shim is `cmd.exe` running the real program.
  const command = verifyAndKill(ours);

  assert.match(command, /ProcessId=4242/, 'the pid is not in the query');
  assert.match(command, /\$p\.Name -eq 'agy\.exe'/, 'the image is not compared');
  assert.match(command, new RegExp(`Abs\\(\\$started - ${ours.startedMs}\\)`), 'the start time is not compared');
  assert.match(command, new RegExp(`-le ${NEAR_ENOUGH_MS}`), 'the tolerance is not the documented one');
  assert.match(command, /taskkill \/pid 4242 \/t \/f/, 'only the top process would be ended');
  assert.match(command, /Get-CimInstance/, 'Get-Process reports a name with no extension');
  // The epoch conversion happens in PowerShell, so no date format, locale or time zone crosses over.
  assert.match(command, /TotalMilliseconds/);
});

test('the command says which of the three things it did', () => {
  assert.strictEqual(killOutcome(0, 'killed\r\n'), 'killed');
  assert.strictEqual(killOutcome(0, 'not ours'), 'not ours');
  assert.strictEqual(killOutcome(0, 'gone'), 'gone');
});

test('anything else is unknown — and unknown kills nothing and KEEPS the record', () => {
  // The direction this whole module errs in. An entry nobody could ask about is retried at the next
  // activation, because dropping it is how an orphan becomes permanent.
  for (const [code, said] of [[1, 'killed'], [0, ''], [0, 'Get-CimInstance : access denied'], [0, 'huh']] as const) {
    assert.strictEqual(killOutcome(code, said), 'unknown', `it read a verdict out of: ${code} / ${said}`);
  }
  assert.strictEqual(settled('unknown'), false);
  for (const outcome of ['killed', 'not ours', 'gone'] as const) {
    assert.strictEqual(settled(outcome), true);
  }
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
