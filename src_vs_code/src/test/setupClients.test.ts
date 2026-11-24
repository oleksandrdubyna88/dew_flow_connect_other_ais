import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { chmodSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { failuresNotice, failuresOf, setupClients, setupClientsArgs, spawnServer, type SpawnServer } from '../setupClients';
import { ownedTemp } from './ownedTemp';

/**
 * `coai-mcp --setup-clients` as the extension runs it (v2 E4, story s5-install-module).
 *
 * <p>The server's report is the contract (`v2/epoch-1/e3-server-cleanup-install/stories/s6-install/SPEC.md` § 1): one JSON object with `clients[{cli, result, reason}]`,
 * results `changed | unchanged | absent | refused | failed | unmeasured`, and exit codes 0, 65, 69, 73, 74, 75, 78 — never 64 for a request this binary understands.
 * Every case here runs the real `setupClients` over a fake spawn; the two cases at the end run the REAL spawn over a fake binary file, one that answers 64
 * (a binary from before the mode existed) and one that prints a report.</p>
 */

const BINARY = '/stable/path/coai-mcp';

const report = (...clients: readonly unknown[]): string => JSON.stringify({ mode: 'setup-clients', dryRun: false, clients, exitCode: 0 });

/** A spawn that records what it was asked and answers `code` and `output`. */
function fakeSpawn(code: number, output: string): { readonly run: SpawnServer; readonly asked: (readonly string[])[] } {
  const asked: (readonly string[])[] = [];

  return { asked, run: (args) => { asked.push(args); return Promise.resolve({ code, output }); } };
}

test('the mode is run with the extension\'s own binary named explicitly, and nothing else', async () => {
  const spawn = fakeSpawn(0, report({ cli: 'claude', result: 'unchanged' }));

  await setupClients(BINARY, spawn.run);

  assert.deepEqual(spawn.asked, [['--setup-clients', '--binary', BINARY]], 'one run, with --binary: the stable path in epoch 1 is the one the extension installed');
  assert.deepEqual(setupClientsArgs(BINARY), ['--setup-clients', '--binary', BINARY]);
});

test('changed, unchanged, absent and unmeasured clients are silent', async () => {
  const outcome = await setupClients(BINARY, fakeSpawn(0, report(
    { cli: 'claude', result: 'changed' },
    { cli: 'codex', result: 'unchanged' },
    { cli: 'agy', result: 'unmeasured' },
    { cli: 'gemini', result: 'absent' },
  )).run);

  assert.deepEqual(outcome, { kind: 'done', failures: [] });
});

test('a refused client is one failure with the server\'s reason, and the notice is ONE notification naming it', async () => {
  const outcome = await setupClients(BINARY, fakeSpawn(78, report(
    { cli: 'claude', result: 'changed' },
    { cli: 'codex', result: 'refused', reason: 'the entry coai points at a wrapper' },
  )).run);

  assert.equal(outcome.kind, 'done');
  assert.ok(outcome.kind === 'done');
  assert.deepEqual(outcome.failures, [{ cli: 'codex', result: 'refused', reason: 'the entry coai points at a wrapper' }]);
  const notice = failuresNotice(outcome.failures);
  assert.equal(notice.as, 'warning');
  assert.equal(notice.class, 'failure');
  assert.equal(notice.code, 'clients-not-set-up');
  assert.match(notice.title, /one AI client/u);
  assert.match(notice.title, /codex \(refused: the entry coai points at a wrapper\)/u);
  assert.equal(notice.action, undefined, 'never a question to the person: no button, no modal');
  assert.equal(notice.actions, undefined);
  assert.notEqual(notice.modal, true);
});

test('two clients that failed or were refused are ONE notice, not two', async () => {
  const outcome = await setupClients(BINARY, fakeSpawn(74, report(
    { cli: 'claude', result: 'failed', reason: 'the vendor command timed out' },
    { cli: 'codex', result: 'refused', reason: 'malformed config.toml' },
    { cli: 'agy', result: 'changed' },
  )).run);

  assert.ok(outcome.kind === 'done');
  assert.equal(outcome.failures.length, 2);
  const notice = failuresNotice(outcome.failures);
  assert.match(notice.title, /2 AI clients/u);
  assert.match(notice.title, /claude \(failed: the vendor command timed out\)/u);
  assert.match(notice.title, /codex \(refused: malformed config\.toml\)/u);
});

test('exit 64 is a binary from before the mode existed: no failure, no notice, whatever it printed', async () => {
  for (const printed of ['', 'unknown argument --setup-clients', report({ cli: 'claude', result: 'refused', reason: 'x' })]) {
    assert.deepEqual(await setupClients(BINARY, fakeSpawn(64, printed).run), { kind: 'old-binary' }, `printed: ${printed}`);
  }
});

test('exit 69 (no AI client found) and 75 (another run holds the lock) are silent', async () => {
  for (const code of [69, 75]) {
    assert.deepEqual(await setupClients(BINARY, fakeSpawn(code, report()).run), { kind: 'done', failures: [] }, `exit ${code}`);
  }
});

test('exit 73 (the backup was not written, nothing changed) is one failure even when the report names no client', async () => {
  const outcome = await setupClients(BINARY, fakeSpawn(73, report()).run);

  assert.ok(outcome.kind === 'done');
  assert.equal(outcome.failures.length, 1);
  assert.match(failuresNotice(outcome.failures).title, /nothing was changed/u);
});

test('what the extension cannot read is the console\'s, never a notification it invents: no output, not JSON, not a report, a code the contract does not name', async () => {
  const cases: readonly (readonly [string, number, string])[] = [
    ['the process did not run', -1, ''],
    ['no output', 0, ''],
    ['not JSON', 0, 'Setting up…'],
    ['JSON that is not a report', 0, '{"mode":"setup-clients"}'],
    ['an array', 0, '[1,2]'],
    ['a code the contract does not name', 65, report()],
  ];
  for (const [what, code, printed] of cases) {
    const outcome = await setupClients(BINARY, fakeSpawn(code, printed).run);
    assert.equal(outcome.kind, 'unread', what);
  }
});

test('a report is read defensively: a client without a name or a reason, a reason that is not text, and a very long reason', () => {
  const failures = failuresOf(report(
    { result: 'failed' },
    { cli: 'codex', result: 'refused', reason: 7 },
    { cli: 'claude', result: 'failed', reason: `  ${'x'.repeat(500)}  ` },
    null,
    'text',
    { cli: 'agy', result: 'changed', reason: 'ignored' },
  ));

  assert.ok(failures !== undefined);
  assert.deepEqual(failures.map((one) => one.cli), ['an AI client', 'codex', 'claude']);
  assert.equal(failures[1]?.reason, '', 'a reason that is not text is no reason');
  assert.equal(failures[2]?.reason.length, 200, 'a reason is a sentence: it is cut, so a server that wrote a page cannot fill a toast');
  assert.equal(failuresOf('not json'), undefined);
});

/* ---------------------------------------------------------------------------------------------- *
 * The real spawn over a fake binary FILE (POSIX: a script; a Windows runner has no script it can start without a shell, and the cases above cover the logic there).
 * ---------------------------------------------------------------------------------------------- */

const posix = process.platform === 'win32' ? { skip: 'a fake binary is a shell script' } : {};

/** A file that behaves as a `coai-mcp` would: it writes `output` and exits `code`, and logs its arguments beside itself. */
function fakeBinary(what: string, code: number, output: string): { readonly path: string; readonly log: string } {
  const dir = ownedTemp(what);
  const path = join(dir, 'coai-mcp');
  const log = join(dir, 'args.log');
  const quoted = (text: string): string => `'${text.replaceAll("'", "'\\''")}'`;
  writeFileSync(path, `#!/bin/sh\nprintf '%s\\n' "$*" >> ${quoted(log)}\nprintf '%s' ${quoted(output)}\nexit ${code}\n`);
  chmodSync(path, 0o755);

  return { path, log };
}

test('a fake binary that answers 64 gives no failure, and it was run once with the mode and its own path', posix, async () => {
  const fake = fakeBinary('setup-old', 64, 'unknown argument --setup-clients');

  const outcome = await setupClients(fake.path, spawnServer(fake.path));

  assert.deepEqual(outcome, { kind: 'old-binary' });
  assert.equal(readFileSync(fake.log, 'utf8'), `--setup-clients --binary ${fake.path}\n`);
});

test('a fake binary that refuses a client gives that failure from its real stdout', posix, async () => {
  const fake = fakeBinary('setup-refuses', 78, report({ cli: 'claude', result: 'changed' }, { cli: 'codex', result: 'refused', reason: 'foreign command' }));

  const outcome = await setupClients(fake.path, spawnServer(fake.path));

  assert.ok(outcome.kind === 'done');
  assert.deepEqual(outcome.failures, [{ cli: 'codex', result: 'refused', reason: 'foreign command' }]);
});
