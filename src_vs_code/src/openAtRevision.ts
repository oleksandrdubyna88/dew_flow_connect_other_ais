import * as fs from 'node:fs/promises';
import * as path from 'node:path';

import { insideReally } from './claudeSessions';
import { RevisionRow, RevisionState, UNPROBED } from './revisionActions';

/**
 * Reaching the code a row names, honestly about which revision — the host's half.
 *
 * <p>Two actions with two different guards, and getting them the same way round is the defect to
 * avoid. *Open at &lt;sha&gt;* reads an OBJECT out of git through the server (`coai-mcp --file-at`),
 * so its path is validated lexically, server-side, against the string the row stores — a file
 * deleted since the recorded revision has no current path, and that is 99.6 % of what the action
 * exists for. *Open CURRENT* opens a file from the live filesystem, so it is guarded by containment:
 * the recorded checkout must be inside an open workspace folder, and the file inside the checkout,
 * both as written and as they really lead ({@link insideReally}). The extension spawns no git of its
 * own on either path.</p>
 *
 * <p>Free of `vscode`, so every decision here is a unit test; the two calls that touch the editor
 * live in `revisionOpen.ts` and are pinned structurally.</p>
 */

/** What `--file-at` answers: the file at the commit the reviewers read, or why not. */
export interface FileAtRevision {
  readonly findingId: number;
  /** The commit read, as the pair stores it. */
  readonly sha: string;
  /** The path read, repository-relative, as the pair stores it. */
  readonly path: string;
  /**
   * Empty when `text` is the file; otherwise `pair_not_found`, `repo_path_missing`, `git_failed`,
   * `commit_unreachable`, `file_not_in_commit` or `path_refused` — each a different fact.
   */
  readonly reason: string;
  readonly text: string;
}

/** What one read came to. `tooOld` is the 64 branch: a server that has never heard of the mode. */
export type FileAtRead =
  | { readonly ok: true; readonly file: FileAtRevision }
  | { readonly ok: false; readonly why: string; readonly tooOld: boolean };

/** What the editor is handed to show: the text, and where it came from, so the tab can say which revision. */
export interface RevisionDocument {
  readonly sha: string;
  readonly file: string;
  readonly text: string;
  /** The finding's line at that commit — exact there, so the cursor lands on it. 0 for none. */
  readonly line: number;
}

/**
 * The sentence a person reads when the server has never heard of the mode.
 *
 * <p>Exported so the reader and the state say the same thing: `readFileAt` fills it in for exit code
 * 64, and {@link stateOf} renders it. 64 means "this binary does not have that mode" and nothing
 * else (`.agents/PROJECT.md`), so the sentence sends somebody to update.</p>
 */
export const TOO_OLD_FOR_THE_REVISION =
  'this machine’s coai-mcp is older than opening a file at its revision; update it from the panel';

/** One row's last answer, with the coordinates it was about — a miss when the pair now names others. */
/**
 * One row's answer, with the three coordinates that make it THAT row's.
 *
 * <p><b>The checkout is one of them.</b> A finding id, a commit and a path are the same three in two
 * checkouts of one repository — which story 2.2 measured as the ordinary case here, 44 of 54 live
 * paths being linked worktrees — so an identity without `repoPath` hands the second row the first
 * one's code. (Code round, codex.)</p>
 */
interface HeldRevision {
  readonly repoPath: string;
  readonly headSha: string;
  readonly file: string;
  readonly read: FileAtRead;
}

/**
 * What the panel has learned about reaching code, for its lifetime.
 *
 * <p><b>Probe once per REPOSITORY, on the first press, and remember the answer.</b> `probed` holds the
 * repositories that have answered at all; `gone` those the server said are not git repositories any
 * more — after which every row of that repository says so instead of offering, and no further
 * process is spent on it (41 % of recorded checkouts no longer exist). `rows` holds each row's own
 * answer: the text, or the reason that is about THAT row (the commit pruned, the file not at that
 * path then). `current` holds why the last *Open CURRENT* press could not open a row's file.</p>
 *
 * <p>Bounded by the pairs on the page (at most `MAX_LIMIT`) and emptied with the window — a checkout
 * can come back, and a page reopened a day later should probe again. Immutable: every change is a
 * new memory.</p>
 */
export interface RevisionMemory {
  readonly tooOld: boolean;
  readonly probed: ReadonlySet<string>;
  readonly gone: ReadonlySet<string>;
  readonly rows: ReadonlyMap<number, HeldRevision>;
  readonly current: ReadonlyMap<number, string>;
}

export function emptyMemory(): RevisionMemory {
  return {
    tooOld: false,
    probed: new Set<string>(),
    gone: new Set<string>(),
    rows: new Map<number, HeldRevision>(),
    current: new Map<number, string>(),
  };
}

/** The reasons that are a fact about the REPOSITORY having answered, rather than about the row alone. */
const ABOUT_THE_REPOSITORY: ReadonlySet<string> = new Set(['', 'repo_path_missing', 'commit_unreachable', 'file_not_in_commit']);

/**
 * What one answer teaches.
 *
 * <p>A failed process is remembered on the row so the row can say so, and is retried on the next
 * press — {@link heldRevision} answers only text. `git_failed` is the same: the server ran and git
 * did not answer, which is a fact about nothing. A server too old for the mode is a fact about every
 * row at once.</p>
 */
export function remember(memory: RevisionMemory, row: RevisionRow, read: FileAtRead): RevisionMemory {
  const rows = new Map([
    ...memory.rows,
    [row.findingId, { repoPath: row.repoPath, headSha: row.headSha, file: row.file, read }],
  ]);
  if (!read.ok) {
    return { ...memory, rows, tooOld: memory.tooOld || read.tooOld };
  }
  const probed = ABOUT_THE_REPOSITORY.has(read.file.reason)
    ? new Set([...memory.probed, row.repoPath])
    : memory.probed;
  const gone = read.file.reason === 'repo_path_missing'
    ? new Set([...memory.gone, row.repoPath])
    : memory.gone;

  return { ...memory, rows, probed, gone };
}

/** Why the last *Open CURRENT* press could not open this row's file — or empty, which clears it. */
export function rememberCurrent(memory: RevisionMemory, row: RevisionRow, why: string): RevisionMemory {
  return { ...memory, current: new Map([...memory.current, [row.findingId, why]]) };
}

/** The row's held answer, when it still names the same commit and path. */
function heldOf(memory: RevisionMemory, row: RevisionRow): FileAtRead | undefined {
  const held = memory.rows.get(row.findingId);

  return held !== undefined
    && held.repoPath === row.repoPath
    && held.headSha === row.headSha
    && held.file === row.file
      ? held.read
      : undefined;
}

/**
 * The text already fetched for this row, so a second press opens it again without a process.
 *
 * <p>Only TEXT. A domain reason is remembered by {@link stateOf} as the sentence that replaces the
 * button, and a failed process must stay retryable — so neither is answered from here.</p>
 */
export function heldRevision(memory: RevisionMemory, row: RevisionRow): FileAtRead | undefined {
  const held = heldOf(memory, row);

  return held !== undefined && held.ok && held.file.reason.length === 0 ? held : undefined;
}

/**
 * The rows whose actions may have changed after this row's answer: every row of the same repository
 * once the repository has answered, else the row alone.
 *
 * <p>This is what makes one process per repository true for the case that matters: a checkout that
 * is gone answers once, and every row of it stops offering. It is also what turns the other rows'
 * "not checked yet" into an offer after the first successful read in that repository.</p>
 */
export function affectedBy(memory: RevisionMemory, row: RevisionRow, held: readonly RevisionRow[]): readonly number[] {
  // A server too old for the mode is a fact about the SERVER, not about one repository: every drawn
  // row is affected at once. Told to the pressed row alone, the other 199 kept offering the action
  // and each launched another doomed request. (Code round, codex.)
  if (memory.tooOld) {
    return held.map((one) => one.findingId);
  }

  if (!memory.probed.has(row.repoPath)) {
    return [row.findingId];
  }

  return held.filter((one) => one.repoPath === row.repoPath).map((one) => one.findingId);
}

/** One sentence per fact — each sends a person somewhere different, and one word for all would send them to the wrong place. */
function sentence(read: FileAtRead, row: RevisionRow): string {
  if (!read.ok) {
    return read.tooOld ? TOO_OLD_FOR_THE_REVISION : `the file could not be read: ${read.why}; press again to retry`;
  }
  const at = row.headSha.slice(0, 7);
  switch (read.file.reason) {
    case '':
      return '';
    case 'pair_not_found':
      return 'this pair is not in the database any more';
    case 'repo_path_missing':
      return `${row.repoPath} is not a git repository any more, so nothing can be opened at ${at}`;
    case 'git_failed':
      return `git did not answer for ${row.repoPath}; press again to retry`;
    case 'commit_unreachable':
      return `commit ${at} is not in the repository any more`;
    case 'file_not_in_commit':
      return `${row.file} was not at this path at ${at}; open the current file instead`;
    case 'path_refused':
      return 'the recorded path is not a repository-relative path, so it cannot be opened';
    default:
      return `not opened: ${read.file.reason}`;
  }
}

/** Whether an answer still leaves the action offered — a retryable failure does, a fact about the row does not. */
function stillOffered(read: FileAtRead): boolean {
  return read.ok ? read.file.reason.length === 0 || read.file.reason === 'git_failed' : !read.tooOld;
}

/** What one row offers, from what the panel remembers. */
export function stateOf(memory: RevisionMemory, row: RevisionRow): RevisionState {
  const currentNote = memory.current.get(row.findingId) ?? '';
  if (memory.tooOld) {
    return { offered: false, note: TOO_OLD_FOR_THE_REVISION, currentNote };
  }
  if (memory.gone.has(row.repoPath)) {
    return { offered: false, note: sentence({ ok: true, file: { findingId: row.findingId, sha: row.headSha, path: row.file, reason: 'repo_path_missing', text: '' } }, row), currentNote };
  }
  const held = heldOf(memory, row);
  if (held !== undefined) {
    return { offered: stillOffered(held), note: sentence(held, row), currentNote };
  }

  return { ...(memory.probed.has(row.repoPath) ? { offered: true, note: '' } : UNPROBED), currentNote };
}

/**
 * Where the tab for a revision document lives: the file's own name carrying the short sha, under
 * the file's own directory, with the full coordinates in the query.
 *
 * <p>The editor labels a tab with the last path segment, so `Totals@aaaa111.cs` says which revision
 * it is at a glance and keeps the extension for language detection; the query carries the full sha
 * and the path as stored, so the URI is self-describing.</p>
 */
export function revisionDocumentPath(sha: string, file: string): { readonly path: string; readonly query: string } {
  const posix = path.posix;
  const name = posix.basename(file);
  const dir = posix.dirname(file);
  const ext = posix.extname(name);
  const stem = name.slice(0, name.length - ext.length);

  return {
    path: `/${dir === '.' ? '' : `${dir}/`}${stem}@${sha.slice(0, 7)}${ext}`,
    query: `sha=${encodeURIComponent(sha)}&path=${encodeURIComponent(file)}`,
  };
}

/** Where the current file really is, or why it is not opened. */
export type CurrentFile =
  | { readonly ok: true; readonly path: string }
  | { readonly ok: false; readonly why: string };

/** Whether `repoPath` IS `folder` — the common case, where the checkout is the workspace folder. */
async function sameDirectory(folder: string, repoPath: string): Promise<boolean> {
  try {
    return path.relative(await fs.realpath(folder), await fs.realpath(repoPath)).length === 0;
  } catch {
    return false;
  }
}

/**
 * Whether the recorded checkout is one of this workspace's folders, or inside one — as written AND
 * as it really leads.
 *
 * <p>`insideReally` is strict containment, and a workspace folder that IS the checkout is the
 * ordinary case, so equality is asked separately: two canonical paths with nothing between them.
 * That is not a second containment check — it is the one case containment cannot express.</p>
 */
async function withinWorkspace(folders: readonly string[], repoPath: string): Promise<boolean> {
  for (const folder of folders) {
    if ((await insideReally(folder, repoPath)) !== undefined || (await sameDirectory(folder, repoPath))) {
      return true;
    }
  }

  return false;
}

/** Whether a path is still there to be opened. */
async function readable(file: string): Promise<boolean> {
  try {
    await fs.access(file);

    return true;
  } catch {
    return false;
  }
}

/**
 * The current file, guarded twice: the checkout inside an open workspace folder, and the file inside
 * the checkout — both through {@link insideReally}, which requires containment as written AND as it
 * really leads and fails closed when either cannot be canonicalised.
 *
 * <p>The first guard is the one the plan round added (gemini): `insideReally(repoPath, file)` alone
 * passes for anything under `repoPath`, so a pair naming `/etc` would have opened a system file.
 * A stored pair is data a model wrote about, and this is the one action that touches the live
 * filesystem.</p>
 */
export async function currentFileIn(folders: readonly string[], repoPath: string, file: string): Promise<CurrentFile> {
  if (repoPath.trim().length === 0) {
    return { ok: false, why: 'no checkout is recorded for this pair' };
  }
  if (file.trim().length === 0) {
    return { ok: false, why: 'no file is recorded for this pair' };
  }
  if (!(await withinWorkspace(folders, repoPath))) {
    return { ok: false, why: `${repoPath} is not a folder of this workspace, so nothing is opened from it` };
  }
  const target = path.join(repoPath, file);
  if (!(await readable(target))) {
    return { ok: false, why: `${file} is not in ${repoPath} any more; open it at its revision instead` };
  }
  const real = await insideReally(repoPath, target);

  return real === undefined
    ? { ok: false, why: `${file} leads outside ${repoPath}, so it is not opened` }
    : { ok: true, path: real };
}
