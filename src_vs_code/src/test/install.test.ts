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
} from '../coaiInstall';
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

test('an unsupported platform is refused rather than guessed at', () => {
  assert.equal(ridFor('freebsd', 'x64'), undefined);
  assert.equal(ridFor('darwin', 'ppc'), undefined);
});
