import { type CliStatus, UNKNOWN_CLI, updateAvailable } from './cliVersions';
import { mayDeleteTheOldCopy, type MoveRecord } from './dataMove';
import { escapeHtml } from './escapeHtml';
import type { Runtime } from './models';
import type { Vendor } from './vendors';

/**
 * The new Settings page's Setup additions (todo/PLAN_one_model_catalog.md E4.5): the CLIs the models run on, whether
 * each MCP client registers coai, and the data folder's "moved from" record. Pure — the host reads the files and the
 * versions, these decide and draw.
 */

// ---------------------------------------------------------------- the CLIs

/** The runtimes that run a CLI on this machine. `local` and `api` call an endpoint; `remote` runs on a Team server. */
const CLI_RUNTIMES: readonly Runtime[] = ['claude', 'codex', 'antigravity', 'gemini'];

/** One CLI — a runtime at one executable — and the models that run on it. */
export interface CliGroup {
  readonly runtime: Runtime;
  readonly executablePath: string;
  readonly rows: readonly Vendor[];
}

/** The models grouped by the CLI they run on: a row with its own CLI path is a CLI of its own. */
export function cliGroups(rows: readonly Vendor[]): readonly CliGroup[] {
  const keyOf = (row: Vendor): string => `${row.runtime}\u0000${row.executablePath}`;
  const onCli = rows.filter((row) => CLI_RUNTIMES.includes(row.runtime));
  const keys = [...new Set(onCli.map(keyOf))];

  return keys.map((key) => {
    const members = onCli.filter((row) => keyOf(row) === key);

    return { runtime: members[0]!.runtime, executablePath: members[0]!.executablePath, rows: members };
  });
}

/** The version cell: what is installed, and whether a newer one is published. */
function versionCell(cli: CliStatus): string {
  if (cli.installed.length === 0) {
    return '<span class="hint">version not read</span>';
  }
  const newer = updateAvailable(cli.installed, cli.latest) ? `<br><span class="stale">${escapeHtml(cli.latest)} available</span>` : '';

  return `<b>${escapeHtml(cli.installed)}</b>${newer}`;
}

function cliRow(group: CliGroup, status: Readonly<Record<string, CliStatus>>, buttons: (vendor: Vendor, id: string, cli: CliStatus) => string): string {
  const first = group.rows[0]!;
  const cli = status[first.id] ?? UNKNOWN_CLI;
  const path = group.executablePath.length === 0 ? '' : `<br><code>${escapeHtml(group.executablePath)}</code>`;
  const models = group.rows.map((row) => `${escapeHtml(row.id)}${row.enabled ? '' : ' <i>(off)</i>'}`).join(', ');

  return `<tr><td>${escapeHtml(group.runtime)}${path}</td><td>${models}</td><td>${versionCell(cli)}</td><td>${buttons(first, escapeHtml(first.id), cli)}</td></tr>`;
}

/**
 * The CLI table — the current page's own ▶ ⤓ ⟳ buttons, for the first model on each CLI.
 *
 * @param buttons the current card's builder (`panelView.cliButtons`), passed in so this module stays pure
 */
export function cliTableHtml(rows: readonly Vendor[], status: Readonly<Record<string, CliStatus>>, buttons: (vendor: Vendor, id: string, cli: CliStatus) => string): string {
  const groups = cliGroups(rows);
  const body = groups.length === 0 ? '<tr><td colspan="4"><i>No model runs on a CLI.</i></td></tr>' : groups.map((group) => cliRow(group, status, buttons)).join('');

  return `<h3>CLIs your models run on</h3>
<p class="hint">These need no key here: each CLI keeps its own sign-in, and you sign in inside the CLI itself.</p>
<table class="map"><thead><tr><th scope="col">CLI</th><th scope="col">Models</th><th scope="col">Version</th><th scope="col">Actions</th></tr></thead>
<tbody>${body}</tbody></table>`;
}

// ---------------------------------------------------------------- the MCP clients

/** Which client config a file is. */
export type ClientKind = 'claudeUser' | 'claudeProject' | 'vscode';

/** One client's answer: whether coai is registered there — the only thing read out of its file. */
export interface ClientRegistration {
  readonly label: string;
  readonly path: string;
  readonly state: 'registered' | 'not registered' | 'no file' | 'unreadable';
  /** What can quietly outrank it, or ''. */
  readonly note: string;
}

/** Where each client keeps its servers, and what it is called. */
export const CLIENT_FILES: Readonly<Record<ClientKind, { readonly label: string; readonly path: string; readonly member: string }>> = {
  claudeUser: { label: 'Claude Code (this machine)', path: '~/.claude.json', member: 'mcpServers' },
  claudeProject: { label: 'Claude Code (this project)', path: '.mcp.json', member: 'mcpServers' },
  vscode: { label: 'VS Code (this project)', path: '.vscode/mcp.json', member: 'servers' },
};

function recordOf(value: unknown): Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? value as Record<string, unknown> : {};
}

/** Whether `coai` is a key of the object at `member` — nothing else of the file is looked at, let alone kept. */
function holdsCoai(parsed: unknown, member: string): boolean {
  return Object.hasOwn(recordOf(recordOf(parsed)[member]), 'coai');
}

/** A `projects` entry for this workspace that registers coai outranks the top level of `~/.claude.json`. */
function projectNote(kind: ClientKind, parsed: unknown, workspace: string): string {
  const project = kind === 'claudeUser' && workspace.length > 0 ? recordOf(recordOf(parsed)['projects'])[workspace] : undefined;

  return holdsCoai(project, 'mcpServers') ? 'A project entry for this folder registers coai too, and it takes precedence over this one.' : '';
}

function parsedOf(text: string): unknown {
  try {
    return JSON.parse(text) as unknown;
  } catch {
    return undefined;
  }
}

/**
 * Whether a client registers coai, from its config file's text — READ only: no other entry, value or secret of the file
 * is returned.
 *
 * @param text the file as read — '' when there is none
 * @param workspace this window's folder, as `~/.claude.json` keys its projects
 */
export function clientRegistration(kind: ClientKind, text: string, workspace: string): ClientRegistration {
  const read = readOf(text);
  const { label, path } = CLIENT_FILES[kind];

  return typeof read === 'string' ? { label, path, state: read, note: '' } : registered(kind, read.json, workspace);
}

/** The file's JSON — or, as words, that there is none or it cannot be read. */
function readOf(text: string): 'no file' | 'unreadable' | { readonly json: unknown } {
  if (text.trim().length === 0) {
    return 'no file';
  }
  const parsed = parsedOf(text);

  return parsed === undefined ? 'unreadable' : { json: parsed };
}

function registered(kind: ClientKind, parsed: unknown, workspace: string): ClientRegistration {
  const { label, path, member } = CLIENT_FILES[kind];

  return { label, path, state: holdsCoai(parsed, member) ? 'registered' : 'not registered', note: projectNote(kind, parsed, workspace) };
}

function clientRow(client: ClientRegistration): string {
  const note = client.note.length === 0 ? '' : `<br><span class="hint">${escapeHtml(client.note)}</span>`;
  const tone = client.state === 'registered' ? '' : ' class="stale"';

  return `<tr><td>${escapeHtml(client.label)}</td><td><code>${escapeHtml(client.path)}</code></td><td><span${tone}>${client.state}</span>${note}</td></tr>`;
}

/** The clients table: read from each client's own file, never written — the block to paste is the Install button's. */
export function mcpClientsHtml(clients: readonly ClientRegistration[]): string {
  return clients.length === 0 ? '' : `<h3>MCP clients</h3>
<p class="hint">Read from each client's own config file and never written there. To register coai, use Install the MCP server and paste its block.</p>
<table class="map"><thead><tr><th scope="col">Client</th><th scope="col">File</th><th scope="col">coai</th></tr></thead>
<tbody>${clients.map(clientRow).join('')}</tbody></table>`;
}

// ---------------------------------------------------------------- the data folder

/** The last move of the data folder, as this profile remembers it across a reload — and the delete, when it is safe. */
export function movedFromHtml(record: MoveRecord | undefined): string {
  if (record === undefined) {
    return '';
  }
  const action = mayDeleteTheOldCopy(record)
    ? '<button type="button" data-command="deleteOldDataFolder" data-id="">Delete the old folder…</button>'
    : '<span class="hint">The copy did not verify, so the old folder is kept: it may hold what the copy lost.</span>';

  return `<div class="field moved-from"><div>Your data was moved from <code>${escapeHtml(record.from)}</code> to <code>${escapeHtml(record.to)}</code>.</div>${action}</div>`;
}
