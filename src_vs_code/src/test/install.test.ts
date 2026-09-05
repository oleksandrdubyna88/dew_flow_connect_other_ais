import * as fs from 'node:fs';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  COAI_RIDS,
  assetNameFor,
  binaryNameFor,
  compareVersions,
  entryPathIn,
  ridFor,
  updateAvailable,
  versionFromTag,
  installFailureHint,
  SingleFlight,
} from '../coaiInstall';
import { VSCODE_COMMAND_FOR } from '../panelView';
import { CLIENT_TARGETS, installedMessage, mcpServerBlock } from '../mcpBlock';
import { claudeSnippet } from '../claudeSnippet';

test('ridFor matches the release matrix', () => {
  assert.equal(ridFor('win32', 'x64'), 'win-x64');
  assert.equal(ridFor('win32', 'arm64'), 'win-arm64');
  assert.equal(ridFor('linux', 'x64'), 'linux-x64');
  assert.equal(ridFor('linux', 'arm64'), 'linux-arm64');
  // macOS used to be refused here, correctly, because the matrix did not build it. It does now.
  assert.equal(ridFor('darwin', 'arm64'), 'osx-arm64');
  assert.equal(ridFor('linux', 'ia32'), undefined, 'a guessed RID downloads a binary that cannot run');
});

test('asset and entry names match what the release workflow packages', () => {
  assert.equal(assetNameFor('win-x64', '0.1.0'), 'coai-mcp-0.1.0-win-x64.zip');
  assert.equal(assetNameFor('linux-arm64', '0.1.0'), 'coai-mcp-0.1.0-linux-arm64.tar.gz');
  assert.equal(entryPathIn('win-x64', '0.1.0'), 'coai-mcp-0.1.0-win-x64/coai-mcp.exe');
  assert.equal(entryPathIn('linux-x64', '0.1.0'), 'coai-mcp-0.1.0-linux-x64/coai-mcp');
  assert.equal(binaryNameFor('win-arm64'), 'coai-mcp.exe');
  assert.equal(binaryNameFor('linux-x64'), 'coai-mcp');
});

test('a tag from another line yields nothing rather than a wrong version', () => {
  assert.equal(versionFromTag('mcp-v0.1.0'), '0.1.0');
  assert.equal(versionFromTag('extension-v0.1.0'), undefined);
  assert.equal(versionFromTag('mcp-v'), undefined);
});

test('updates are offered only when the published tag is actually newer', () => {
  assert.equal(updateAvailable(undefined, 'mcp-v0.1.0'), true, 'nothing installed = install');
  assert.equal(updateAvailable('0.1.0', 'mcp-v0.2.0'), true);
  assert.equal(updateAvailable('0.2.0', 'mcp-v0.2.0'), false);
  assert.equal(updateAvailable('0.3.0', 'mcp-v0.2.0'), false, 'never offer a downgrade');
  assert.equal(updateAvailable('0.1.0', 'extension-v9.9.9'), false, 'the wrong tag line is not an update');
  assert.equal(compareVersions('0.10.0', '0.9.0') > 0, true, 'numeric, not lexicographic');
});

test('the config block uses the full path and survives JSON parsing on Windows', () => {
  const block = mcpServerBlock('C:\\Users\\ada\\AppData\\coai-mcp.exe', {});
  const parsed = JSON.parse(block) as { mcpServers: { coai: { command: string; env?: unknown } } };
  assert.equal(parsed.mcpServers.coai.command, 'C:\\Users\\ada\\AppData\\coai-mcp.exe');
  assert.equal('env' in parsed.mcpServers.coai, false, 'a field that does nothing invites a question');
});

test('the server id is coai — the namespace every tool inherits', () => {
  const parsed = JSON.parse(mcpServerBlock('/home/ada/.local/coai-mcp', {})) as {
    mcpServers: Record<string, unknown>;
  };
  assert.deepEqual(Object.keys(parsed.mcpServers), ['coai']);
});

test('settings travel in the env block when there are any', () => {
  const block = mcpServerBlock('/bin/coai-mcp', { COAI_MAX_ROUNDS: '5' });
  const parsed = JSON.parse(block) as { mcpServers: { coai: { env: Record<string, string> } } };
  assert.equal(parsed.mcpServers.coai.env['COAI_MAX_ROUNDS'], '5');
});

test('the install message names the path and the paste, not a PATH change', () => {
  const message = installedMessage('/home/ada/.local/coai-mcp');
  assert.ok(message.includes('/home/ada/.local/coai-mcp'));
  assert.ok(message.includes('clipboard'));
  assert.ok(!message.includes('PATH'));
});

test('the known client targets are the three files a person would edit', () => {
  assert.deepEqual(
    CLIENT_TARGETS.map((t) => t.path),
    ['~/.claude.json', '<project>/.mcp.json', '.vscode/mcp.json'],
  );
});

test('the CLAUDE.md snippet names all seven tools under the coai namespace', () => {
  const snippet = claudeSnippet();
  for (const tool of ['providers', 'open', 'review_plan', 'review_code', 'resolve', 'status', 'ask_human']) {
    assert.ok(snippet.includes(`mcp__coai__${tool}`), `names ${tool}`);
  }
});

test('the snippet names no repository — it tells the AI to read its own checkout', () => {
  const snippet = claudeSnippet();
  assert.ok(snippet.includes('git rev-parse --show-toplevel'), 'repoPath comes from where it runs');
  assert.ok(snippet.includes('Never a path from this file'));
});

test('the snippet states the ordering contract and the human stop', () => {
  const snippet = claudeSnippet();
  assert.ok(snippet.includes('REFUSES until a plan round'));
  assert.ok(snippet.includes('Do not proceed on your own judgement'));
  assert.ok(snippet.includes('a rejection'), 'the reason duty is stated');
  assert.ok(snippet.includes('needs a reason'));
});

test('a Mac gets its own build, because .NET calls that platform osx and node calls it darwin', () => {
  // The mapping is the whole reason a Mac was told "there is no published build" while the
  // runtime had supported one all along.
  assert.equal(ridFor('darwin', 'arm64'), 'osx-arm64');
  assert.equal(ridFor('darwin', 'x64'), 'osx-x64');
});

test('every RID the workflow builds is a RID the extension will install', () => {
  // The two lists live in different files and different languages; this is what holds them
  // together. A build added to the matrix that the extension does not know is a download nobody
  // can start; one the extension knows and the matrix does not build is a 404 at install time.
  const workflow = fs.readFileSync(
    path.join(__dirname, '..', '..', '..', '.github', 'workflows', 'release.yml'),
    'utf8',
  );
  const built = [...workflow.matchAll(/^\s+- rid:\s*(\S+)\s*$/gm)].map((m) => m[1]!);

  assert.deepEqual([...built].sort(), [...COAI_RIDS].sort());
});

test('the release carries the native library the binary opens its database through', () => {
  // 0.18.1 shipped the executable alone. Native AOT compiles managed code; the P/Invoke into
  // SQLite still resolves at run time through the OS loader, which searches the directory the
  // executable sits in — so the installed server threw DllNotFoundException the first time
  // anything touched the rounds database, and the write is best-effort, so it did it in silence.
  // It answered --version, --help and a full tools/list exchange throughout.
  const workflow = fs.readFileSync(
    path.join(__dirname, '..', '..', '..', '.github', 'workflows', 'release.yml'),
    'utf8',
  );

  assert.match(workflow, /e_sqlite3/, 'the archive carries the native library');
  assert.match(
    workflow,
    /is not in the publish output/,
    'and the job FAILS when it is missing, rather than shipping a server that cannot open its own database');
  assert.match(
    workflow,
    /COAI_DATA_DIR="\$DB" "\$EXE" --log/,
    'the smoke makes the published binary actually open one — the check that would have caught it');
});

test('an unsupported platform is refused rather than guessed at', () => {
  assert.equal(ridFor('freebsd', 'x64'), undefined);
  assert.equal(ridFor('darwin', 'ppc'), undefined);
});

/**
 * Overwriting a binary that is RUNNING is refused by Windows, and an MCP client holding
 * `coai-mcp.exe` open is the normal case at the exact moment somebody presses Update. The raw
 * error is an errno; what a person needs is the sentence that says which program to quit.
 */
test('an update blocked by the running server says what to close', () => {
  const hint = installFailureHint('EPERM: operation not permitted, copyfile ... coai-mcp.exe', 'EPERM');
  assert.match(hint, /MCP client/);
  assert.match(hint, /coai-mcp\.exe/);
});


test('an ordinary failure is passed through untouched, never dressed up as a lock', () => {
  assert.equal(installFailureHint('GitHub answered 503 for the release list'), '');
});

/**
 * The exhaustiveness check proves a `case` exists, not that it invokes the right thing — a typo in
 * the command id compiles, passes every guard, and reproduces the original silence exactly. The
 * manifest is the only place that can settle it.
 */
test('every command the panel delegates to is one the manifest actually registers', () => {
  const registered = new Set(
    (JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8')) as {
      contributes: { commands: { command: string }[] };
    }).contributes.commands.map((c) => c.command),
  );

  for (const [panelCommand, id] of Object.entries(VSCODE_COMMAND_FOR)) {
    assert.ok(registered.has(id), `${panelCommand} delegates to "${id}", which nothing registers`);
  }
});



test('a sharing violation is named plainly, whichever layer reported it', () => {
  // The CODE decides. Windows' own sentence is the exception: it arrives inside the message of a
  // wrapped error and carries no machine-readable code at all.
  for (const [raw, code] of [
    ['EBUSY: resource busy or locked, copyfile coai-mcp.exe', 'EBUSY'],
    ['ETXTBSY: text file busy, copyfile coai-mcp.exe', 'ETXTBSY'],
    ['cannot write coai-mcp.exe', 'Unavailable'],
    // As VS Code wraps it: the path is in the message, which is what keeps this branch about
    // the binary rather than about anything else the install touched.
    ['Unable to write file coai-mcp.exe: The process cannot access the file because it is being used by another process.', ''],
  ] as const) {
    assert.match(installFailureHint(raw, code), /MCP client/, raw);
  }
});

test('a failure on something that is not the binary keeps its own message', () => {
  // A read-only attribute or an ACL on the scratch directory is EPERM too, and neither sentence
  // this function can produce would be about the right file.
  assert.equal(installFailureHint('EPERM: operation not permitted, mkdir .download-1', 'EPERM'), '');
  assert.equal(installFailureHint('GitHub answered 503 for the release list'), '');
});

test('a second Update while one is running joins it instead of racing it', async () => {
  const flight = new SingleFlight<number>();
  let started = 0;
  const work = async (): Promise<number> => {
    started += 1;
    await new Promise((r) => setTimeout(r, 20));
    return started;
  };

  const [a, b] = await Promise.all([flight.run(work), flight.run(work)]);
  assert.equal(started, 1, 'two installs racing on one destination is a corrupt binary');
  assert.equal(a, b, 'the second caller wanted the same thing, so it gets the same answer');
  assert.equal(flight.isRunning, false, 'and the gate reopens once it is done');
});

test('a failed install does not wedge the button until the window is reloaded', async () => {
  // Both reviewers went looking for this half: a download dropped by a flaky connection must not
  // leave every later click joining that same rejected promise. A retry is the most likely next
  // thing a person does after a failure.
  const flight = new SingleFlight<string>();
  await assert.rejects(flight.run(() => Promise.reject(new Error('connection reset'))), /connection reset/);
  assert.equal(flight.isRunning, false);
  assert.equal(await flight.run(() => Promise.resolve('second attempt ran')), 'second attempt ran');
});

test('an access denial on the binary names both causes instead of asserting one', () => {
  // EPERM covers a running exe AND a read-only attribute with nothing holding it. Naming only the
  // first sends somebody to close a program that was never the problem, and hides the real cause.
  const denied = installFailureHint('EPERM: operation not permitted, copyfile coai-mcp.exe', 'EPERM');
  assert.match(denied, /quit that client/);
  assert.match(denied, /read-only|permissions/);
  assert.doesNotMatch(denied, /is in use —/, 'that sentence is for an unambiguous sharing violation');
});

test('a sharing violation still says plainly that something has the file open', () => {
  assert.match(installFailureHint('EBUSY: resource busy or locked, copyfile coai-mcp.exe', 'EBUSY'), /is in use/);
});
