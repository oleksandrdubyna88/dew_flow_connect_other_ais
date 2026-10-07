import assert from 'node:assert/strict';
import { test } from 'node:test';
import { catalogHtml } from '../catalogPage';
import { type PanelState } from '../panelView';
import { cliGroups, clientRegistration, movedFromHtml } from '../setupTab';
import { DEFAULT_VENDORS, type Vendor } from '../vendors';
import { click, panelState, runPanel } from './panelPageHarness';
import { pageTree } from './pageTree';

/**
 * E4.5 of todo/PLAN_one_model_catalog.md: the new page's Setup — the CLIs the models run on, whether each MCP client
 * registers coai (READ from its config file, never written, and nothing else in it shown), and the data folder's
 * "moved from" record surviving a reload with the way to delete the old folder.
 */

const row = (id: string, runtime: Vendor['runtime'], extra: Partial<Vendor> = {}): Vendor => ({ ...DEFAULT_VENDORS[0]!, id, runtime, baseUrl: '', executablePath: '', ...extra });

test('the CLI table groups the models by the CLI they run on, and leaves out what runs on no CLI', () => {
  const groups = cliGroups([
    row('codex', 'codex'), row('deepseek', 'codex', { baseUrl: 'https://api.deepseek.com' }), row('claude', 'claude'),
    row('claude-2', 'claude', { executablePath: 'C:\\tools\\claude.exe' }), row('ollama', 'local'), row('grok', 'api'), row('team', 'remote'),
  ]);

  assert.deepEqual(groups.map((one) => [one.runtime, one.executablePath, one.rows.map((r) => r.id)]), [
    ['codex', '', ['codex', 'deepseek']],
    ['claude', '', ['claude']],
    ['claude', 'C:\\tools\\claude.exe', ['claude-2']],
  ]);
});

/** A config file holding coai and another server whose env carries a secret. */
const CLAUDE_JSON = JSON.stringify({
  mcpServers: { coai: { command: 'coai-mcp' }, other: { command: 'x', env: { TOKEN: 'sekret-123' } } },
  projects: { 'D:/work/app': { mcpServers: { coai: { command: 'old' } } } },
});

test('a client registers coai where its own config says — and nothing else of the file comes back', () => {
  const user = clientRegistration('claudeUser', CLAUDE_JSON, 'D:/elsewhere');
  assert.equal(user.state, 'registered');
  assert.equal(user.note, '');
  assert.doesNotMatch(JSON.stringify(user), /sekret|other/, 'the reader returned more than its own entry');

  assert.equal(clientRegistration('claudeProject', JSON.stringify({ mcpServers: { coai: {} } }), '').state, 'registered');
  assert.equal(clientRegistration('vscode', JSON.stringify({ servers: { coai: {} } }), '').state, 'registered');
  assert.equal(clientRegistration('vscode', JSON.stringify({ mcpServers: { coai: {} } }), '').state, 'not registered', 'VS Code reads "servers"');
});

test('a project entry in ~/.claude.json outranks the top level, and the reader says so', () => {
  assert.match(clientRegistration('claudeUser', CLAUDE_JSON, 'D:/work/app').note, /project entry .* takes precedence/);
});

test('a missing, empty or unreadable config is said as it is', () => {
  assert.equal(clientRegistration('claudeProject', '', '').state, 'no file');
  assert.equal(clientRegistration('claudeProject', '{ not json', '').state, 'unreadable');
  assert.equal(clientRegistration('claudeProject', '[]', '').state, 'not registered');
});

test('the moved-from record offers the delete only after a verified move', () => {
  const verified = movedFromHtml({ from: 'C:\\old\\coai', to: 'Z:\\coai', verified: true });
  assert.match(verified, /C:\\old\\coai/);
  assert.match(verified, /data-command="deleteOldDataFolder"/);
  assert.doesNotMatch(movedFromHtml({ from: 'a', to: 'b', verified: false }), /deleteOldDataFolder/);
  assert.equal(movedFromHtml(undefined), '');
});

test('the Setup places draw the CLI table, the clients and the move record', () => {
  const state: PanelState = {
    ...panelState('keys'),
    catalogRows: [row('codex', 'codex')],
    mcpClients: [{ label: 'VS Code', path: '.vscode/mcp.json', state: 'not registered', note: '' }],
    lastDataMove: { from: 'C:\\old\\coai', to: 'Z:\\coai', verified: true },
  };
  const html = catalogHtml(state, 'test-nonce', 'setup/mcp');
  const tree = pageTree(html);
  const clients = tree.one((node) => node.dataset.pane === 'setup/mcp', 'MCP server pane').find((node) => node.tagName === 'TR').map((row) => row.text());
  assert.ok(clients.some((row) => row.includes('VS Code') && row.includes('not registered')), `no client row says so: ${clients.join(' | ')}`);

  // The buttons, pressed through the page's own script.
  const page = runPanel(state, { html });
  click(page, 'updateVendorCli', 'codex');
  click(page, 'deleteOldDataFolder', '');
  const commands = page.posted.filter((one) => one['type'] === 'command').map((one) => [one['command'], one['id']]);
  assert.deepEqual(commands, [['updateVendorCli', 'codex'], ['deleteOldDataFolder', '']]);
});
