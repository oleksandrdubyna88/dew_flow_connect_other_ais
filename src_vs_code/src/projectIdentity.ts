import { existsSync, readFileSync, statSync } from 'node:fs';

/**
 * Which sessions belong to the same project, decided from a path and one file read.
 *
 * <p><b>Why this is not the rule the plan asked for.</b> The plan said: the git remote when the
 * checkout is reachable, else the normalised root with worktree suffixes stripped, else `unknown`.
 * Both halves were measured against the live table on 2026-09-18 — 106 distinct
 * `sessions.repo_path` values — and both were wrong in a way the measurement made obvious
 * (`story22-identity-measured.md` has the arithmetic):</p>
 *
 * <ul>
 *   <li><b>Stripping a worktree-looking suffix merges unrelated products.</b> Not in theory: the
 *       live table has <b>22</b> directories under `d:/rsd/_wt` — `coai-*`, `creds-*` and
 *       `conv-gate`, three different repositories — and <b>20</b> under `d:/rsd`, which is every
 *       project on the machine. The rule would have drawn one tab called `_wt`. Two reviewers said
 *       so on the plan round and they were right. <b>So no suffix is ever stripped here.</b></li>
 *   <li><b>Spawning `git remote get-url` is not needed to beat it.</b> A linked worktree's `.git`
 *       is a FILE naming its parent, so one `readFileSync` answers what a process was going to —
 *       and the corpus's 54 live paths collapse to <b>10</b> identities that way, with the two
 *       `_wt` siblings above landing in their two different products.</li>
 * </ul>
 *
 * <p><b>The trap, which neither the plan nor any reviewer named and the measurement found.</b> A
 * submodule <i>inside</i> a worktree writes
 * `gitdir: …/repo/.git/worktrees/wt-rp/modules/.claude/rules/shared`. Cutting at
 * `/.git/worktrees/` would file `dew_flow_conventions` under `dew_flow_connect_other_ais` — two
 * real products merged, and conventions has its own sessions in that table. The parent is therefore
 * recovered <b>only</b> when exactly one segment follows `/worktrees/`; anything longer, and every
 * `/.git/modules/…` gitdir (which git writes relative), is its own project.</p>
 *
 * <p><b>Absence is an answer, not a failure.</b> 41 % of the corpus no longer exists on disk. An
 * identity that can only be learned from a filesystem is thus unavailable two times in five, so an
 * unreachable path becomes its own single-project bucket rather than being merged with a neighbour
 * or hidden. The 33 sessions whose `repo_path` is literally `.` get one explicit
 * {@link UNKNOWN_PROJECT} bucket, because a relative path identifies nothing at all.</p>
 */

/** The one bucket for paths that identify nothing — `.` and empty, 33 sessions of the corpus. */
export const UNKNOWN_PROJECT = 'unknown';

/**
 * What `.git` says about a directory.
 *
 * <p>A union rather than a kind plus optional fields, so `gitdir` exists exactly where it means
 * something and a `checkout` cannot carry one by accident.</p>
 */
export type GitMark =
  | { readonly kind: 'checkout' }
  | { readonly kind: 'gone' }
  | { readonly kind: 'linked'; readonly gitdir: string };

/** Reads one path's mark. Injected so the rule is testable without a filesystem. */
export type MarkReader = (path: string) => GitMark;

/** One project, as a tab needs it. */
export interface ProjectIdentity {
  /** The bucket every session of this project shares. */
  readonly key: string;

  /** What the tab is called — the repository's own directory name. */
  readonly label: string;

  /** The whole path, for the tab's tooltip; empty for {@link UNKNOWN_PROJECT}. */
  readonly full: string;

  /** Whether the identity was learned from disk, or merely assumed from the path. */
  readonly reachable: boolean;
}

/** `.` and empty say nothing about which project a session belonged to. */
const NO_IDENTITY: ReadonlySet<string> = new Set(['', '.']);

/** A gitdir names a worktree's parent only when the worktree's own name is the LAST segment. */
const LINKED = /^(.*)\/\.git\/worktrees\/[^/]+$/u;

const GITDIR = 'gitdir: ';

/** A UNC path's leading pair of separators is part of the path; every other run is one. */
function collapsed(path: string): string {
  return path.startsWith('//') ? `//${path.slice(2).replace(/\/{2,}/gu, '/')}` : path.replace(/\/{2,}/gu, '/');
}

/**
 * Separators, doubled separators, a trailing slash and case — and nothing else.
 *
 * <p>Case is folded because this runs on Windows and the corpus proves the need: 15 of 106 values
 * were a path already present, spelled differently. It is the one normalisation that would be wrong
 * on a case-sensitive filesystem, and it is accepted deliberately — on Linux two paths differing
 * only in case are two projects, and this product's store is per machine.</p>
 */
export function normalisePath(raw: string): string {
  const slashed = collapsed(raw.trim().split('\\').join('/')).toLowerCase();
  return slashed.length > 1 ? slashed.replace(/\/$/u, '') : slashed;
}

/** The repository a linked worktree's gitdir names, or empty when it names something else. */
function parentOf(mark: GitMark): string {
  if (mark.kind !== 'linked') return '';
  const found = LINKED.exec(normalisePath(mark.gitdir));
  return found === null ? '' : found[1];
}

function lastSegment(key: string): string {
  const at = key.lastIndexOf('/');
  return at < 0 || at === key.length - 1 ? key : key.slice(at + 1);
}

function unknownProject(): ProjectIdentity {
  return { key: UNKNOWN_PROJECT, label: 'Unknown project', full: '', reachable: false };
}

function identified(path: string, mark: GitMark): ProjectIdentity {
  const parent = parentOf(mark);
  const key = parent === '' ? path : parent;
  return { key, label: lastSegment(key), full: key, reachable: mark.kind !== 'gone' };
}

/** Which project one `sessions.repo_path` belonged to. */
export function identityOf(raw: string, read: MarkReader): ProjectIdentity {
  const path = normalisePath(raw);
  return NO_IDENTITY.has(path) ? unknownProject() : identified(path, read(path));
}

/** Wraps a reader so a path is asked about once however many sessions share it. */
export function askedOnce(read: MarkReader): MarkReader {
  const held = new Map<string, GitMark>();

  return (path) => {
    const known = held.get(path);
    if (known !== undefined) return known;

    const fresh = read(path);
    held.set(path, fresh);

    return fresh;
  };
}

/**
 * Every project a batch of paths belongs to, reading each distinct path at most once.
 *
 * <p>The draw this feeds already spends 468 ms tokenising 200 pairs (measured, story 1.2), and a
 * filesystem call per ROW would be the first thing to make that worse. The whole live corpus has 91
 * distinct paths and 10 identities, so in practice this is ten reads per panel.</p>
 */
export function identitiesOf(
  paths: readonly string[],
  read: MarkReader,
): ReadonlyMap<string, ProjectIdentity> {
  const once = askedOnce(read);
  const found = new Map<string, ProjectIdentity>();

  for (const raw of paths) {
    const one = identityOf(raw, once);
    found.set(one.key, one);
  }

  return found;
}

function gitdirIn(said: string): string {
  const at = said.indexOf(GITDIR);
  return at < 0 ? '' : said.slice(at + GITDIR.length).trim();
}

function markAt(dot: string): GitMark {
  if (!existsSync(dot)) return { kind: 'gone' };
  if (statSync(dot).isDirectory()) return { kind: 'checkout' };

  return { kind: 'linked', gitdir: gitdirIn(readFileSync(dot, 'utf8')) };
}

/**
 * The real filesystem reader — one `existsSync`, one `statSync`, and a `readFileSync` only for a
 * linked worktree.
 *
 * <p>A path that cannot be READ answers the same as one that is not there, because both mean the
 * identity could not be learned and the caller's behaviour is identical. That is not swallowing an
 * error: being gone is the ordinary state of 41 % of this corpus, and a panel draw that threw
 * because a scratch directory was deleted last week would be the defect.</p>
 */
export function readGitMark(path: string): GitMark {
  try {
    return markAt(`${path}/.git`);
  } catch {
    return { kind: 'gone' };
  }
}
