import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import { clientFilesFor, ClientReader } from '../mcpClientsRead';

/**
 * E4.5 of todo/PLAN_one_model_catalog.md: whether each MCP client registers coai is READ from its real config file —
 * never written — and only that answer comes back. Over real files in a temporary folder.
 */

test('the files a client keeps its servers in, for this machine and this folder', () => {
  const files = clientFilesFor('/home/me', '/work/app');

  assert.deepEqual(files.map((one) => [one.kind, one.file]), [
    ['claudeUser', path.join('/home/me', '.claude.json')],
    ['claudeProject', path.join('/work/app', '.mcp.json')],
    ['vscode', path.join('/work/app', '.vscode', 'mcp.json')],
  ]);
  assert.deepEqual(clientFilesFor('/home/me', '').map((one) => one.kind), ['claudeUser'], 'a window with no folder has no project files');
});

test('each file is read for coai alone; a missing file and an oversized one are said as they are', async () => {
  const dir = mkdtempSync(path.join(tmpdir(), 'coai-clients-'));
  try {
    writeFileSync(path.join(dir, '.claude.json'), JSON.stringify({ mcpServers: { coai: {}, other: { env: { KEY: 'sekret' } } } }));
    mkdirSync(path.join(dir, '.vscode'));
    writeFileSync(path.join(dir, '.vscode', 'mcp.json'), 'x'.repeat(400));
    const reader = new ClientReader(200);

    const read = await reader.read(clientFilesFor(dir, dir), dir);

    assert.deepEqual(read.map((one) => one.state), ['registered', 'no file', 'unreadable']);
    assert.doesNotMatch(JSON.stringify(read), /sekret|other/);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a file that has not changed is not read again', async () => {
  const dir = mkdtempSync(path.join(tmpdir(), 'coai-clients-'));
  try {
    const file = path.join(dir, '.mcp.json');
    writeFileSync(file, JSON.stringify({ mcpServers: { coai: {} } }));
    const reader = new ClientReader();
    const files = [{ kind: 'claudeProject' as const, file }];

    await reader.read(files, dir);
    await reader.read(files, dir);

    assert.equal(reader.reads, 1);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
