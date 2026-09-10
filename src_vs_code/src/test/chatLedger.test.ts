import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  ChildRecord,
  FORGET_AFTER_MS,
  afterSweep,
  bootSecondsIn,
  filesToSweep,
  NEAR_ENOUGH_MS,
  USER_HZ,
  forgotten,
  imageOf,
  killOutcome,
  namesOf,
  procStat,
  startedAtMs,
  stillOurs,
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

test('a taskkill that was REFUSED is not a kill, and keeps the record', () => {
  // It used to print "killed" whatever taskkill did, so an access-denied refusal struck out the
  // record and left the process running — the exact outcome this module exists to prevent.
  assert.match(verifyAndKill(ours), /LASTEXITCODE/, 'the kill reports success without checking it');
  assert.strictEqual(killOutcome(0, 'refused'), 'unknown');
  assert.strictEqual(settled(killOutcome(0, 'refused')), false, 'a refused kill struck out its record');
});

test('an image this side would not put in a command produces NO command', () => {
  // `worthAsking` filters these earlier. Saying it here too means the guarantee does not have to be
  // reconstructed from another function by whoever reads this one.
  assert.strictEqual(verifyAndKill({ ...ours, image: "agy'; calc; '.exe" }), '');
  assert.notStrictEqual(verifyAndKill(ours), '');
});

test('a file whose owner is still running is not opened at all', () => {
  // The whole reason the owner's pid is in the name. Opening it would mean verifying processes that
  // really ARE this extension's — every check would pass — and killing a conversation somebody is
  // reading in another VS Code window.
  const alive = (pid: number): boolean => pid === 200;
  const files = filesToSweep(
    ['chat-children-100.json', 'chat-children-200.json', 'chat-children-300.json', 'settings.json'],
    300,
    alive,
  );

  assert.deepStrictEqual([...files], [{ name: 'chat-children-100.json', owner: 100 }]);
});

test('this window never sweeps its own ledger, whatever the operating system says about it', () => {
  assert.deepStrictEqual([...filesToSweep(['chat-children-42.json'], 42, () => false)], []);
});

test('a ledger is removed only when everything in it was asked AND answered', () => {
  const record = { pid: 1, image: 'agy.exe', startedMs: 5 };

  assert.strictEqual(afterSweep([], true), 'remove');
  // Something nobody could resolve, or a sweep that ran out of time: the file stays, because one
  // kept too long costs a few hundred bytes and one removed too early costs a process nobody can find.
  assert.strictEqual(afterSweep([record], true), 'rewrite');
  assert.strictEqual(afterSweep([], false), 'rewrite');
  assert.strictEqual(afterSweep([record], false), 'rewrite');
});

/**
 * The POSIX identity check, against lines a real kernel wrote.
 *
 * <p>Both fixtures were taken from a live WSL distro on 2026-09-10 rather than composed, and the
 * second one exists because `/proc/<pid>/stat` is the file everybody parses wrongly: field 2 is the
 * executable's name in parentheses and it may contain spaces AND brackets of its own. A process
 * really can be called `we (are) here`, and splitting the line on whitespace then reads a different
 * field — silently, and only for that process.</p>
 */
const REAL_STAT = '84361 (sleep) S 84357 84357 84357 34825 84357 4194304 129 0 0 0 0 0 0 0 20 0 1 0 430830 '
  + '3207168 447 18446744073709551615 108365529612288 108365529626289 140731617153408 0 0 0 0 6 0 1 0 0 17 19 0 0 0 0 0';

const TRICKY_STAT = '84369 (we (are) here) S 84357 84357 84357 34825 84357 4194304 133 0 0 0 0 0 0 0 20 0 1 0 430852 '
  + '3207168 447 18446744073709551615 109356870762496 109356870776497 140728320219584 0 0 0 0 6 0 1 0 0 17 2 0 0 0 0 0';

test('a real /proc stat line yields the name and the start ticks', () => {
  assert.deepStrictEqual(procStat(REAL_STAT), { comm: 'sleep', startTicks: 430830 });
});

test('a process whose own name contains spaces and brackets is still read correctly', () => {
  assert.deepStrictEqual(procStat(TRICKY_STAT), { comm: 'we (are) here', startTicks: 430852 });
});

test('a stat line nobody could parse says so rather than guessing a number', () => {
  assert.deepStrictEqual(procStat(''), { comm: '', startTicks: -1 });
  assert.deepStrictEqual(procStat('84361 (sleep'), { comm: '', startTicks: -1 });
  assert.strictEqual(procStat('84361 (sleep) S 1 2 3').startTicks, -1, 'a truncated line produced a start time');
});

test('the boot time comes from btime and from nothing else', () => {
  assert.strictEqual(bootSecondsIn('cpu 1 2 3\nbtime 1789025380\nprocesses 9\n'), 1789025380);
  assert.strictEqual(bootSecondsIn('cpu 1 2 3\nprocesses 9\n'), -1, 'a file with no btime invented one');
});

test('the start time computed from a real pair lands where the process really started', () => {
  // Taken together on the same machine in the same second: btime 1789025380, and the process was
  // observed alive at 1789029688673. USER_HZ is 100, so 430830 ticks is 4308.3 s after boot.
  const started = startedAtMs(procStat(REAL_STAT).startTicks, bootSecondsIn('btime 1789025380\n'));

  assert.strictEqual(started, 1_789_029_688_300);
  assert.ok(Math.abs(started - 1_789_029_688_673) < NEAR_ENOUGH_MS, 'the computed start is outside the slack');
  assert.strictEqual(USER_HZ, 100);
});

test('a start time is unknown rather than zero when either half is missing', () => {
  assert.strictEqual(startedAtMs(-1, 1789025380), -1);
  assert.strictEqual(startedAtMs(430830, -1), -1);
});

test('a process answers to its comm and to the first two words of its command line, and to nothing else', () => {
  // A native binary: argv[0] is its own path. A shebang script: the kernel runs the interpreter, so
  // argv[0] is the interpreter and the name we recorded is at argv[1] — measured, and the reason
  // this takes two positions rather than one.
  assert.deepStrictEqual(namesOf('claude', ['/usr/bin/claude', '--print']), ['claude']);
  assert.deepStrictEqual(
    namesOf('sh', ['/bin/sh', '/mnt/c/Users/x/AppData/Roaming/npm/codex', 'exec']),
    ['sh', 'codex'],
  );
  // And it stops at two: an argument that happens to be a vendor's name is an argument.
  assert.deepStrictEqual(namesOf('python3', ['/usr/bin/python3', 'job.py', 'codex']), ['python3', 'job.py']);
});

test('a comm the kernel truncated at fifteen characters is not the only thing asked', () => {
  // Linux caps comm at 15 characters, so a longer name never matches there — which is exactly why
  // the command line is consulted as well.
  assert.deepStrictEqual(
    namesOf('a-very-long-nam', ['/opt/bin/a-very-long-named-cli']),
    ['a-very-long-nam', 'a-very-long-named-cli'],
  );
});

test('a process is ours only when the name AND the start time both agree', () => {
  const record: ChildRecord = { pid: 4242, image: 'codex', startedMs: 1_789_000_000_000 };

  assert.strictEqual(stillOurs(record, { names: ['sh', 'codex'], startedMs: record.startedMs + 500 }), true);
  assert.strictEqual(stillOurs(record, { names: ['node'], startedMs: record.startedMs }), false);
  assert.strictEqual(
    stillOurs(record, { names: ['codex'], startedMs: record.startedMs + NEAR_ENOUGH_MS + 1 }),
    false,
    'a namesake started outside the slack was taken for ours',
  );
  assert.strictEqual(stillOurs(record, { names: ['codex'], startedMs: -1 }), false, 'an unknown start matched');
});
