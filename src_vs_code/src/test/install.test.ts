import * as child_process from 'node:child_process';
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

/** One job's block out of a workflow file: from its own key to the next job key. */
function jobBlock(workflow: string, job: string): string {
  const start = workflow.indexOf(`\n  ${job}:\n`);
  assert.notEqual(start, -1, `the workflow has no ${job} job`);
  const rest = workflow.slice(start + 1);
  const next = rest.search(/\n {2}[a-z][a-z0-9-]*:\n/);
  return next === -1 ? rest : rest.slice(0, next);
}

// Read with CR stripped. These files are checked out with native line endings on Windows, and a
// job header is matched as `\n  name:\n` — which is a line every one of these tests would have
// declared missing on the machine most of them are written on.
const workflowText = (file: string) =>
  fs
    .readFileSync(path.join(__dirname, '..', '..', '..', '.github', 'workflows', file), 'utf8')
    .split('\r\n')
    .join('\n');

const releaseWorkflow = () => workflowText('release.yml');

const deployWorkflow = () => workflowText('deploy-server.yml');

/** A script under `deploy/`, read as the host would read it — the deploy path is code too. */
const deployScript = (file: string) =>
  fs
    .readFileSync(path.join(__dirname, '..', '..', '..', 'deploy', file), 'utf8')
    .split('\r\n')
    .join('\n');

const ridsBuiltBy = (job: string) =>
  [...jobBlock(releaseWorkflow(), job).matchAll(/^\s+- rid:\s*(\S+)\s*$/gm)]
    .map((m) => m[1]!)
    .sort();

test('every RID the workflow builds is a RID the extension will install', () => {
  // The two lists live in different files and different languages; this is what holds them
  // together. A build added to the matrix that the extension does not know is a download nobody
  // can start; one the extension knows and the matrix does not build is a 404 at install time.
  //
  // Scoped to the mcp job rather than to the whole file. Scraping every `- rid:` line was the same
  // thing while one matrix existed and became wrong the moment the server got its own: the
  // extension downloads coai-mcp and nothing else, so a server RID in this list would demand it
  // learn to install a binary nobody ever asks it for.
  assert.deepEqual(ridsBuiltBy('mcp-binaries'), [...COAI_RIDS].sort());
});

test('the server matrix and the check that the release is complete name the same platforms', () => {
  // The Team server is DEPLOYED rather than downloaded, so no extension list holds this one
  // honest — which is why it needs a test of its own. The host runs a Native AOT binary under
  // systemd (deploy/README.md); the release line published container images only, so the one
  // artefact the deployment actually consumes was the one thing the tag did not produce.
  //
  // Held against the completeness job's OWN list rather than against COAI_RIDS. The two artefacts
  // have independent lifecycles: a server-only RID — an arm64 host, say — would otherwise turn
  // this red until the extension published a coai-mcp for it and added it to its install contract,
  // which is a coupling between two release lines that share nothing but a repository.
  // (codex, code round.)
  const complete = jobBlock(releaseWorkflow(), 'server-release-complete');
  const declared = /RIDS=\(([^)]*)\)/.exec(complete);
  assert.ok(declared, 'the completeness job declares the platforms it expects');

  assert.deepEqual(ridsBuiltBy('server-binaries'), declared[1]!.trim().split(/\s+/).sort());
});

test('the deploy pulls a RID the server release actually builds', () => {
  // The third place the same fact lives, and the one that fails LAST: a preflight looking for
  // `linux-x64` in a release that never built it stops with nothing to download, after somebody
  // has already been asked to approve a production deploy.
  const complete = jobBlock(releaseWorkflow(), 'server-release-complete');
  const hostRid = /HOST_RID:\s*(\S+)/.exec(deployWorkflow());
  assert.ok(hostRid, 'the deploy names the RID the host runs');

  assert.ok(
    ridsBuiltBy('server-binaries').includes(hostRid[1]!),
    `the release line builds ${hostRid[1]}`,
  );
  assert.match(complete, new RegExp(hostRid[1]!), 'and the completeness check expects it');
});

test('a server tag creates a GitHub RELEASE, because that is what the panel reads', () => {
  // The panel asks `api.github.com/repos/…/releases` and filters by tag prefix, so a bare tag is
  // invisible to it: `newestServerTag` had nothing to find and the update line could never fire.
  // This is the fact neither file can state alone — the reader is TypeScript, the writer is a
  // workflow step, and between them sat an assumption.
  const server = jobBlock(releaseWorkflow(), 'server-binaries');
  const installer = fs.readFileSync(
    path.join(__dirname, '..', '..', 'src', 'installer.ts'),
    'utf8',
  );

  assert.match(installer, /releases\?per_page=/, 'the panel reads RELEASES, not tags');
  assert.match(server, /gh release create "\$GITHUB_REF_NAME"/, 'so the server tag creates one');
  assert.match(server, /gh release upload "\$GITHUB_REF_NAME" "\$ASSET"/, 'and attaches its archive');
  assert.match(server, /sha256sum "\$ASSET"/, 'beside a checksum');
});

test('the server release is verified COMPLETE, not merely attempted', () => {
  // `gh release create … || true` swallows every failure, not only the "another matrix job got
  // there first" one it exists for — so five archives and a swallowed error look exactly like six.
  // The completeness check is its own job because no single matrix leg can see the others.
  const verify = jobBlock(releaseWorkflow(), 'server-release-complete');

  assert.match(verify, /needs:\s*\[\s*server-binaries/, 'it runs after every archive is uploaded');
  assert.match(verify, /gh release view/, 'and asks GitHub what the release actually carries');
  assert.match(verify, /EXPECTED=6/, 'against the number of platforms this line promises');
  // By NAME, not by tally: six wrongly-named files satisfy a count, and the one that matters is
  // the RID the live host installs — a release missing exactly that passes a count and fails the
  // next deploy at its preflight.
  assert.match(verify, /coai-server-\$VERSION-\$rid/, 'each expected asset name is looked for');
  assert.match(verify, /the release is incomplete — missing:/, 'and the missing ones are named');
});

test('the native release line does not wait on the container line', () => {
  // The two artefacts have independent lifecycles. Coupling the completeness check to the image
  // manifest means a registry outage leaves the shape that IS deployed with no signal at all —
  // and the shape that is deployed is the binary, not the image.
  const verify = jobBlock(releaseWorkflow(), 'server-release-complete');

  assert.doesNotMatch(verify, /needs:[^\n]*server-manifest/, 'the image job is not in its needs');
});

test('neither release nor deploy can hang past a stated bound', () => {
  // Every failure mode of a SERVER is a wait — one that never binds, one that ignores its
  // termination, an ssh session to a host that stopped answering. Without a job bound the ceiling
  // is the six-hour default: a release nobody watches finish, and an approval held all afternoon.
  assert.match(jobBlock(releaseWorkflow(), 'server-binaries'), /timeout-minutes:/, 'the release leg');
  assert.match(deployWorkflow(), /timeout-minutes:/, 'and the deploy job');

  // And the smoke's own deadline is absolute rather than a loop count: 60 iterations of a 2-second
  // curl plus a sleep is up to three minutes while the message says sixty seconds.
  assert.match(
    jobBlock(releaseWorkflow(), 'server-binaries'),
    /UNTIL=\$\(\( \$\(date \+%s\) \+ DEADLINE \)\)/,
    'the deadline is a wall-clock instant, not an iteration count',
  );
});

test('nothing crosses into a remote root shell, because there is no longer one to cross into', () => {
  // This used to be about VALIDATING what crossed — the version, and the canary token path that
  // arrived from a repository variable. Since the key became a forced command the property is
  // stronger and simpler: the workflow sends a fixed verb and one already-pinned version, and
  // everything it used to be trusted with — the token path, the staging directory, the script's own
  // location — is a fact about the host now.
  const deploy = deployWorkflow();

  // The COMMAND, not the word: the step that replaced it says in prose what it stopped doing, and
  // that sentence is worth more than the assertion would be if it forbade the noun.
  assert.doesNotMatch(deploy, /(^|\s)scp\s+-/m, 'a forced command has no scp, and nothing needs one');
  assert.doesNotMatch(deploy, /COAI_CANARY_TOKEN_FILE/, "the canary token path is the host's");
  assert.doesNotMatch(
    deploy,
    /ssh[^\n]*'[^'\n]*\/opt\//,
    'no ssh command may name a path on the host — that coupling is what broke the first real deploy',
  );

  // The three verbs, and nothing else, are what the workflow is allowed to say.
  assert.match(deploy, /"deploy \$VERSION"/, 'the deploy verb carries the version');
  assert.match(deploy, /'deploy --rollback'/, 'the rollback is a verb too');
  assert.match(deploy, /'health'/, 'and so is the loopback probe');
});

test('a deploy that never took effect is not "rolled back"', () => {
  // The rollback pops the release trail. Running it when the swap never happened takes a HEALTHY
  // server back a version to fix a deployment that never landed — so the verify step distinguishes
  // "still serving what it served before" from "serving something broken", and only the second
  // rolls back.
  const deploy = deployWorkflow();

  assert.match(deploy, /state=unchanged/, 'the deploy-did-not-land case is named');
  assert.match(deploy, /state=broken/, 'and is distinct from the broken case');
  assert.match(
    deploy,
    /steps\.verify\.outputs\.state == 'broken'/,
    'and only the broken one rolls back',
  );
  assert.match(deploy, /Nothing was rolled back, because nothing changed/, 'and it says so');
});

test('verification runs even when the deploy step itself failed', () => {
  // The window: an ssh session that dies AFTER the script swapped `bin` fails the job with the new
  // release live and unverified. A verification that only ran on success would never look.
  const deploy = deployWorkflow();

  assert.match(
    deploy,
    /if: always\(\) && steps\.install\.outcome != 'skipped'/,
    'verification runs whenever the install was attempted',
  );
  // The loopback probe survived the move to a forced command as a VERB: the workflow asks `health`
  // and the wrapper is what knows the unit's address. Both halves are asserted, because a probe
  // that exists in only one of them is a probe that silently stopped separating "the unit is wrong"
  // from "the edge is wrong".
  assert.match(deploy, /\$SSH "\$USER_NAME@\$HOST" 'health'/, 'loopback is asked before the edge');
  assert.match(
    deployScript('coai-deploy-cmd.sh'),
    /127\.0\.0\.1:8090\/api\/health/,
    'and the wrapper is what knows where the unit answers',
  );
});

test('the server smoke asks the binary its version the only way a server can be asked', () => {
  // coai-mcp answers `--version` on stdout; the server has no argument surface at all — it answers
  // /api/health with {ok, version} from the assembly's informational version. A smoke copied from
  // the mcp job without adapting it would grep stdout that never comes and pass on the empty
  // string, testing nothing on six runners at once.
  const server = jobBlock(releaseWorkflow(), 'server-binaries');
  const program = fs.readFileSync(
    path.join(__dirname, '..', '..', '..', 'src_server', 'src', 'Program.cs'),
    'utf8',
  );

  assert.match(program, /MapGet\("\/api\/health"/, 'the endpoint the smoke depends on exists');
  assert.match(server, /api\/health/, 'and the smoke asks it');
  assert.match(
    server,
    /the published binary reports/,
    'and FAILS when the answer is not the version this release stamps',
  );
});

test('the server smoke cannot hang: a deadline, the log printed, the process killed', () => {
  // The mcp smoke runs a command that exits. This one starts a SERVER, so every failure mode is a
  // wait: a binary that never binds, one that hangs inside Startup.Guard, one that ignores its
  // termination. A matrix job that hangs is worse than one that fails — it fails eventually, after
  // six hours, with no reason anywhere.
  const server = jobBlock(releaseWorkflow(), 'server-binaries');

  assert.match(server, /DEADLINE=/, 'the port is polled to a deadline');
  assert.match(server, /cat "\$LOG"/, 'and the captured output is printed when it is not reached');
  assert.match(server, /if: always\(\)/, 'and the process is killed even when the job failed');
});

test('the deploy workflow refuses to start without the secrets it needs, naming them', () => {
  // A deploy whose secrets are missing must not be a green job that did nothing: the entire point
  // of this workflow is that somebody believes the server was updated afterwards.
  const deploy = deployWorkflow();

  for (const secret of [
    'COAI_DEPLOY_HOST',
    'COAI_DEPLOY_USER',
    'COAI_DEPLOY_KEY',
    'COAI_DEPLOY_KNOWN_HOSTS',
  ]) {
    assert.match(deploy, new RegExp(secret), `${secret} is read`);
  }
  assert.match(deploy, /is not set/, 'and a missing one stops the run, by name');
});

test('the deploy workflow validates the version before it reaches a root shell', () => {
  // The version arrives as free text from a dispatch form and ends up in a command that runs as
  // root over ssh. `0.5.5; systemctl disable coai-server` is an ordinary thing to type into a
  // text box, and an approval gate does not read what it approves.
  const deploy = deployWorkflow();

  assert.match(deploy, /\^\[0-9\]\+\\\.\[0-9\]\+\\\.\[0-9\]\+/, 'a strict version pattern');
  assert.match(deploy, /is not a version/, 'and anything else stops the run');
});

test('the release script is still what runs, and it is the wrapper that runs it', () => {
  // The script on the host owns the release trail, the atomic symlink swap, the per-vendor canary
  // and the rollback. The day a workflow step starts doing its own systemctl restart, all four are
  // gone and nothing says so. Since the key became a forced command the CALL moved into the
  // wrapper — so the assertion moved with it rather than being dropped.
  const deploy = deployWorkflow();
  const wrapper = deployScript('coai-deploy-cmd.sh');

  assert.match(wrapper, /--from/, 'the wrapper hands the PUBLISHED artefact to the script');
  assert.match(wrapper, /--rollback/, 'and rolls back through the same script');
  assert.doesNotMatch(deploy, /systemctl restart/, 'the workflow never restarts the unit behind its back');
  assert.doesNotMatch(wrapper, /systemctl restart/, 'and neither does the wrapper');
});

test('the deploy key can press a button and nothing else', () => {
  // The model is CredsForDevs', running on this same host since it shipped: whatever the client
  // asked for arrives in SSH_ORIGINAL_COMMAND, and only the exact shapes below are honoured. A
  // leaked key is an update button, not root. ConnectOtherAIs deployed over an unrestricted root
  // key until 2026-09-09 and never had to.
  const wrapper = deployScript('coai-deploy-cmd.sh');

  assert.match(wrapper, /restrict,command=/, 'the authorized_keys line is documented in the file it names');
  assert.match(wrapper, /\^\[0-9A-Za-z\._-\]\{1,40\}\$/, 'the version is validated on the SERVER');
  assert.match(wrapper, /refused: this key accepts only/, 'and every other shape is refused');
  // `nopull` is stripped before the version is parsed, or `deploy 0.5.5 nopull` would either be
  // rejected outright or have its suffix pass through unvalidated. Raised on the plan round.
  assert.ok(
    wrapper.indexOf('nopull') < wrapper.indexOf('VERSION="${CMD#deploy }"'),
    'the flag must be stripped before the version token is taken',
  );
});

test('the wrapper refuses everything that is not one of its three verbs', () => {
  // RUN, not read. The `case` block is the whole security boundary of a key that reaches a root
  // account, and a regex asserted against its source proves the text is there, not that it decides
  // anything. Every input below returns before the wrapper touches the network, the checkout or the
  // release script, so this is safe to run anywhere `sh` exists.
  const script = path.join(__dirname, '..', '..', '..', 'deploy', 'coai-deploy-cmd.sh');
  const refused = [
    'rm -rf /',
    'deploy; rm -rf /',
    'deploy ../../etc',
    'deploy $(id)',
    'deploy 0.5.5 extra',
    `deploy ${'a'.repeat(41)}`,
    'deploy',
    '',
    'bash',
  ];

  for (const command of refused) {
    const run = child_process.spawnSync('sh', [script], {
      env: { ...process.env, SSH_ORIGINAL_COMMAND: command },
      encoding: 'utf8',
    });

    if (run.error !== undefined) {
      return; // no POSIX sh on this machine; the CI runner has one and this is where it matters
    }
    assert.equal(run.status, 90, `'${command}' must be refused, not run`);
    assert.match(run.stderr, /^refused: /, `'${command}' must say why it was refused`);
  }
});

test('the wrapper repairs a stale checkout instead of failing on it', () => {
  // The first real deploy failed because the host's release script was three commits behind the
  // workflow invoking it. The fix belongs on the side that holds the truth, and it has to survive
  // the OTHER thing that was true of that host: a working tree dirtied by a hand `chmod +x`, which
  // would abort a plain pull for ever.
  const wrapper = deployScript('coai-deploy-cmd.sh');

  assert.match(wrapper, /merge --ff-only origin\/main/, 'the checkout is fast-forwarded');
  assert.match(wrapper, /checkout -- \./, 'and local modifications to tracked files are discarded');
  assert.match(wrapper, /--untracked-files=no/, 'while untracked files are left alone');
  assert.match(wrapper, /timeout "\$NET_TIMEOUT"/, 'every network call is bounded');
});

test('both deploy scripts are committed executable', () => {
  // The workflow starts these over ssh. `deploy/systemd-release.sh` was committed 100644, so the
  // first deploy on any host with a fresh checkout died on `permission denied` — it worked here
  // only because somebody chmod-ed it by hand, twice, because `git checkout` put the mode back.
  // A comment cannot hold a file mode; this can.
  const modes = child_process
    .execSync('git ls-files -s deploy/coai-deploy-cmd.sh deploy/systemd-release.sh', {
      cwd: path.join(__dirname, '..', '..', '..'),
      encoding: 'utf8',
    })
    .trim()
    .split('\n');

  for (const line of modes) {
    assert.match(line, /^100755 /, `${line.split('\t')[1]} must be executable in git`);
  }
});

test('a deploy that swapped the binary and then failed its own check rolls back', () => {
  // The script canaries behind the swap; the workflow proves the version answering the internet is
  // the one that was asked for. A failure BETWEEN those two used to leave the new release serving,
  // with nothing but a red job to say otherwise.
  const deploy = deployWorkflow();

  assert.match(deploy, /api\/health/, 'the public endpoint is asserted');
  assert.match(deploy, /rolled back/, 'and the failure path says the rollback ran');
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
