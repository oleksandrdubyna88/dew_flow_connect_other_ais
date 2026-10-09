import { readFile, stat } from 'node:fs/promises';
import path from 'node:path';
import { CLIENT_FILES, clientRegistration, type ClientKind, type ClientRegistration } from './setupTab';

/**
 * The host half of the Settings page's MCP clients table (todo/PLAN_one_model_catalog.md E4.5): each client's config file is
 * READ from disk — never written, no process launched — and handed to `clientRegistration`, which keeps only whether
 * coai is registered there.
 *
 * <p>A file is read again only when it changed (its modification time and size): `~/.claude.json` holds every project's
 * history and can run to megabytes, and the panel renders every few seconds.</p>
 */

/** One client file, where it is on this machine. */
export interface ClientFile {
  readonly kind: ClientKind;
  readonly file: string;
}

/** The biggest file read — past it the file is "unreadable" rather than a megabytes-long read on every change. */
const MAX_BYTES = 32 * 1024 * 1024;

/** The files the clients keep their servers in: the user's own, and this folder's when the window has one. */
export function clientFilesFor(home: string, workspace: string): readonly ClientFile[] {
  const user: ClientFile = { kind: 'claudeUser', file: path.join(home, '.claude.json') };

  return workspace.length === 0 ? [user] : [
    user,
    { kind: 'claudeProject', file: path.join(workspace, '.mcp.json') },
    { kind: 'vscode', file: path.join(workspace, '.vscode', 'mcp.json') },
  ];
}

function unreadable(kind: ClientKind): ClientRegistration {
  const { label, path: where } = CLIENT_FILES[kind];

  return { label, path: where, state: 'unreadable', note: '' };
}

function isMissing(error: unknown): boolean {
  return (error as { code?: string } | undefined)?.code === 'ENOENT';
}

/** Reads the clients' files, each again only when it changed. */
export class ClientReader {
  private readonly seen = new Map<string, { readonly stamp: string; readonly answer: ClientRegistration }>();
  /** How many files were actually read — what the cache saves is measured, not assumed. */
  reads = 0;

  constructor(private readonly maxBytes = MAX_BYTES) {}

  /** Each client's answer, in the order given. `workspace` is how `~/.claude.json` keys this folder's project entry. */
  read(files: readonly ClientFile[], workspace: string): Promise<readonly ClientRegistration[]> {
    return Promise.all(files.map((one) => this.one(one, workspace.replace(/\\/g, '/'))));
  }

  private async one(client: ClientFile, workspace: string): Promise<ClientRegistration> {
    try {
      return await this.current(client, workspace);
    } catch (error: unknown) {
      return isMissing(error) ? clientRegistration(client.kind, '', workspace) : unreadable(client.kind);
    }
  }

  /** The kept answer while the file is unchanged; otherwise the file read again. */
  private async current({ kind, file }: ClientFile, workspace: string): Promise<ClientRegistration> {
    const facts = await stat(file);
    const stamp = `${facts.mtimeMs}:${facts.size}:${workspace}`;
    const kept = this.seen.get(file);
    if (kept?.stamp === stamp) {
      return kept.answer;
    }
    const answer = facts.size > this.maxBytes ? unreadable(kind) : clientRegistration(kind, await this.text(file), workspace);
    this.seen.set(file, { stamp, answer });

    return answer;
  }

  private text(file: string): Promise<string> {
    this.reads += 1;

    return readFile(file, 'utf8');
  }
}
