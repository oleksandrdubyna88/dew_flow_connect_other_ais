import * as fs from 'node:fs';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  COAI_RIDS,
  assetNameFor,
  binaryNameFor,
  companionsOf,
  copyCompanions,
  requiredCompanionMissing,
  sqliteNameFor,
  installedKey,
  overlayKey,
  sideKey,
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
  assert.match(
    workflow,
    /the archive does not carry the SQLite library beside the binary/,
    'and the ARCHIVE is inspected after it is made: the publish output proves the library was built, '
    + 'not that the file being shipped carries it');
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

test('every file the archive brought travels beside the binary', () => {
  // 0.18.1 shipped the executable alone. Native AOT compiles managed code; the call into SQLite
  // still resolves at run time through the OS loader, which searches the directory the executable
  // sits in - so the installed server threw DllNotFoundException the first time anything touched
  // the rounds database, and because that write is best-effort it failed in silence.
  const archive = [
    ['coai-mcp.exe', true],
    ['e_sqlite3.dll', true],
  ] as const;

  assert.deepEqual(companionsOf(archive, 'win-x64'), ['e_sqlite3.dll']);
});

test('a companion is named by what the archive holds, not by a list kept here', () => {
  // The point of copying the archive's contents rather than a file we know by name: the next
  // native dependency needs no change in the installer at all.
  const entries = [
    ['coai-mcp', true],
    ['libe_sqlite3.so', true],
    ['libsomething-not-yet-invented.so', true],
  ] as const;

  assert.deepEqual(companionsOf(entries, 'linux-x64'), [
    'libe_sqlite3.so',
    'libsomething-not-yet-invented.so',
  ]);
});

test('the binary itself is not a companion, and neither is a directory', () => {
  // The binary is copied by name, because that name is what the panel launches; copying it twice
  // would be harmless but a directory would not be - vscode.workspace.fs.copy would recurse.
  const entries = [
    ['coai-mcp', true],
    ['libe_sqlite3.dylib', true],
    ['runtimes', false],
  ] as const;

  assert.deepEqual(companionsOf(entries, 'osx-arm64'), ['libe_sqlite3.dylib']);
});

test('an archive of nothing but the binary leaves nothing behind to copy', () => {
  assert.deepEqual(companionsOf([['coai-mcp.exe', true]], 'win-arm64'), []);
});

test('a companion that cannot be copied does not stop the ones after it, and is named', () => {
  // The policy, and the only part of this that no manual check performs: the binary is already in
  // place by the time companions are copied, so a failure here degrades a feature rather than
  // stopping the server from starting. The gate was right that swallowing it silently costs the
  // caller its best sentence - the reason is usually "another process holds this file open".
  const tried: string[] = [];
  const copy = (name: string) => {
    tried.push(name);

    return name === 'libe_sqlite3.so' ? Promise.reject(new Error('EBUSY: resource busy')) : Promise.resolve();
  };

  return copyCompanions(
    [['coai-mcp', true], ['libe_sqlite3.so', true], ['README.md', true]] as const,
    'linux-x64', copy, noPause)
    .then(({ placed, failed }) => {
      assert.deepEqual(tried, ['libe_sqlite3.so', 'libe_sqlite3.so', 'README.md'], 'one retry, then on');
      assert.deepEqual(placed, ['README.md'], 'only what landed is reported as placed');
      assert.deepEqual(failed, [{ name: 'libe_sqlite3.so', why: 'EBUSY: resource busy' }]);
    });
});

test('a copy that fails once and then works is not a failure', async () => {
  // The common case is transient by nature: a server that is exiting still holds its files for a
  // moment. Retrying once turns the usual failure into a successful install.
  let attempts = 0;
  const { placed, failed } = await copyCompanions(
    [['coai-mcp.exe', true], ['e_sqlite3.dll', true]] as const, 'win-x64',
    () => (++attempts === 1 ? Promise.reject(new Error('EBUSY')) : Promise.resolve()),
    noPause);

  assert.equal(attempts, 2);
  assert.deepEqual(placed, ['e_sqlite3.dll']);
  assert.deepEqual(failed, []);
});

/** The retry's pause, taken out of the test's way. */
const noPause = () => Promise.resolve();

test('a SQLite library that did not land fails the install', () => {
  // Best-effort is correct for a companion in general, and wrong for THIS companion on an upgrade:
  // the running server holds the old e_sqlite3 open on Windows, the copy fails, and the new binary
  // is left beside the old library - the original incident again, and just as silent.
  assert.equal(requiredCompanionMissing([], 'win-x64'), 'e_sqlite3.dll');
  assert.equal(requiredCompanionMissing(['e_sqlite3.dll'], 'win-x64'), '', 'it landed');
});

test('a companion that is not the SQLite library still fails nothing', () => {
  assert.equal(
    requiredCompanionMissing(['libe_sqlite3.so'], 'linux-x64'), '',
    'a README that did not land is a feature degrading, which is what best-effort is for');
});

test('an archive that carries no SQLite library at all is REFUSED, not installed', () => {
  // This test asserted the opposite until the review gate took the reasoning apart. The first
  // version required only what the archive HAPPENED to carry, on the grounds that a library missing
  // from the archive is the release job's failure and checked there. It is - for archives not yet
  // built. The 0.18.1 archive was already published without it, so requiring only what was given
  // installs that archive successfully and leaves a server that throws DllNotFoundException the
  // first time anything touches the database, silently. The requirement comes from the PLATFORM now.
  assert.equal(requiredCompanionMissing([], 'osx-arm64'), 'libe_sqlite3.dylib');
  assert.equal(requiredCompanionMissing([], 'linux-arm64'), 'libe_sqlite3.so');
  assert.equal(requiredCompanionMissing([], 'win-arm64'), 'e_sqlite3.dll');
});

test('the name the installer requires is the name the loader looks for', () => {
  // Three platforms, three names, and no third opinion: the OS loader accepts exactly one of them.
  for (const rid of COAI_RIDS) {
    const needed = sqliteNameFor(rid);
    const expected = rid.startsWith('win-')
      ? 'e_sqlite3.dll'
      : rid.startsWith('osx-') ? 'libe_sqlite3.dylib' : 'libe_sqlite3.so';

    assert.equal(needed, expected, `${rid} would look for the wrong file`);
  }
});

test('the archive check accepts what tar and 7z actually print, and refuses a nested library', () => {
  // The pattern is written once, in bash, and only runs while a release is being cut - so its first
  // version shipped a check that passed on `tar tzf` and failed on BOTH windows RIDs, because 7z
  // prints a TABLE whose last column is `NAME\\e_sqlite3.dll` after whitespace while tar prints
  // `NAME/lib...` at the start of a line. The review gate caught it; this test is what holds it,
  // by extracting the real pattern from the workflow and running it against both listings.
  const workflow = fs.readFileSync(
    path.join(__dirname, '..', '..', '..', '.github', 'workflows', 'release.yml'),
    'utf8',
  );
  const written = /grep -Eq "([^"]*e_sqlite3)"/.exec(workflow);
  assert.ok(written, 'the archive check is not in the workflow at all');
  const name = 'coai-mcp-0.18.2-win-x64';
  // POSIX ERE to JavaScript: the one class this pattern uses, and $NAME as bash would expand it.
  const pattern = new RegExp(written[1]!.replace('[[:space:]/]', String.raw`[\s/]`).replace('$NAME', name), 'm');
  const normalise = (listing: string) => listing.split('\\').join('/');

  assert.ok(
    pattern.test(normalise(`   Date      Time    Attr         Size   Compressed  Name
2026-09-06 09:00:00 ....A       1234567      500000  ${name}\\e_sqlite3.dll`)),
    'a 7z listing of an archive that DOES carry the library must pass - it did not, and that failed every windows release');
  assert.ok(
    pattern.test(normalise(`${name}/
${name}/e_sqlite3.dll
${name}/coai-mcp.exe`)),
    'and so must a tar listing');
  assert.ok(
    !pattern.test(normalise(`${name}/native/e_sqlite3.dll`)),
    'a library in a subdirectory is not beside the binary, which is the only place the loader looks');
});

test('the release smoke fails on the message the server actually prints', () => {
  // The gate caught this on the very change that caused it: `--log` now answers a MISSING library
  // with an empty log, a note on stderr and exit 0, so the graceful failure defeated the smoke that
  // was added to catch it. The smoke greps for a SENTENCE, so the sentence and the grep are one
  // fact in two files - this is what keeps them the same fact.
  const workflow = fs.readFileSync(
    path.join(__dirname, '..', '..', '..', '.github', 'workflows', 'release.yml'),
    'utf8',
  );
  const program = fs.readFileSync(
    path.join(__dirname, '..', '..', '..', 'src_mcp', 'src', 'Program.cs'),
    'utf8',
  );

  // Every sentence the smoke greps OUR stderr for, minus the one macOS prints about its own CPU.
  const ours = [...workflow.matchAll(/grep -q '([^']+)' "\$DB\/err"/g)]
    .map((match) => match[1]!)
    .filter((sentence) => !sentence.includes('Bad CPU type'));

  assert.ok(ours.length > 0, 'the smoke no longer inspects stderr after a successful run');
  for (const sentence of ours) {
    assert.ok(
      program.includes(sentence),
      `the smoke greps for "${sentence}" and the server does not print it — the check is dead`);
  }
});

test('two WSL distros with the same storage path are two different sides', () => {
  // The reason this identity is not just the storage path, argued in the install record's own
  // review: every distro mounts /home/<user>/.vscode-server/… at the same place, so one company's
  // settings would land on another's.
  const one = { remoteName: 'wsl', distro: 'Ubuntu-24.04', storagePath: '/home/x/.vscode-server/data/User/globalStorage/coai' };
  const two = { ...one, distro: 'Ubuntu-Work' };

  assert.notEqual(sideKey(one), sideKey(two));
  assert.notEqual(overlayKey(one), overlayKey(two));
});

test('a local window and a WSL window are two different sides', () => {
  const local = { remoteName: undefined, hostname: 'DESKTOP', storagePath: 'C:/Users/x/AppData/Roaming/Code/User/globalStorage/coai' };
  const wsl = { remoteName: 'wsl', distro: 'Ubuntu-24.04', storagePath: '/home/x/.vscode-server/data/User/globalStorage/coai' };

  assert.notEqual(overlayKey(local), overlayKey(wsl));
  assert.match(overlayKey(local), /^coai\.settingsOverlay@/);
});

test('the install record and the settings overlay never share a key', () => {
  // They are kept in the SAME globalState database, and one overwriting the other would be a
  // remembered version replacing a company's settings.
  const side = { remoteName: 'wsl', distro: 'Ubuntu-24.04', storagePath: '/home/x/.vscode-server' };

  assert.notEqual(overlayKey(side), installedKey(side));
  assert.ok(overlayKey(side).endsWith(sideKey(side)) && installedKey(side).endsWith(sideKey(side)));
});

test('the block the panel copies is a path and nothing else, whatever the settings say', () => {
  // Reported on 2026-09-06 as "we fixed the settings applying immediately several times" — and that
  // was true, and true only for the keys nobody had pasted. The block used to carry every setting
  // that differed from the defaults, sixteen keys at full stretch, and a variable in the client's
  // config beats the settings file KEY BY KEY and on purpose. So each pasted key was frozen at its
  // pasted value: change it in the panel, the panel saves it and shows the new number, and the
  // server keeps using the old one without a word.
  //
  // The env block predates the settings file. Nobody trimmed it when the file landed.
  const parsed = JSON.parse(mcpServerBlock('/home/ada/.local/coai-mcp')) as {
    mcpServers: { coai: Record<string, unknown> };
  };

  assert.deepEqual(Object.keys(parsed.mcpServers.coai), ['command'],
    'anything else in here freezes that setting for this client, silently');
});

test('a scripted run can still be handed environment variables', () => {
  // The channel is not removed, only unused by the panel: a containerised or CI run has no panel
  // and no shared data directory, and env is the only way to configure it.
  const parsed = JSON.parse(mcpServerBlock('/bin/coai-mcp', { COAI_ON_EXHAUSTED: 'good_enough' })) as {
    mcpServers: { coai: { env?: Record<string, string> } };
  };

  assert.equal(parsed.mcpServers.coai.env?.['COAI_ON_EXHAUSTED'], 'good_enough');
});

test('and the extension passes it nothing — the call site, not just the default', () => {
  // The first version of this guard asserted the FUNCTION's default and let the call site do as it
  // pleased: putting the env back in extension.ts left every test green. The call site lives behind
  // `vscode`, which this suite cannot import, so the source is what it reads — and it asserts it
  // FOUND the calls, because a structural test that matches nothing passes for ever.
  const source = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'extension.ts'), 'utf8');
  const calls = [...source.matchAll(/mcpServerBlock\(([^)]*)\)/g)].map((m) => m[1]!.trim());

  assert.ok(calls.length >= 2, `expected the two clipboard writes, found ${calls.length}`);
  for (const args of calls) {
    assert.doesNotMatch(args, /,/,
      `mcpServerBlock(${args}) passes an env: every key in it freezes that setting for the client`);
  }
});
