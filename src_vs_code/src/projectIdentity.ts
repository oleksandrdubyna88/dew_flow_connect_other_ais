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
 * <p><b>What this rule CANNOT do, named here because it is not obvious from the code.</b> The key
 * is a checkout LOCATION resolved against today's filesystem, not a recorded project identity — two
 * code reviewers on the code round said so and they are right:</p>
 *
 * <ol>
 *   <li><b>Two clones of one project are two tabs.</b> Both `.git` entries are directories, the
 *       paths differ, and nothing about a location says what it is a copy of. Measured: reading
 *       `.git/config`'s origin url would merge NOTHING on the live corpus — 10 identities by origin
 *       against the same 10 by root, with 9 of 10 having an origin at all — so the cheap-looking
 *       answer buys a file read per project and changes no grouping that exists.</li>
 *   <li><b>A directory reused by another project misfiles history</b>, and this one is wrong rather
 *       than untidy: pairs collected in `_wt/review` for project A, the worktree deleted, a new one
 *       for project B made in its place, and A's sessions now read B's `.git`. No read-time rule can
 *       fix it, because the evidence of which checkout produced the session is gone.</li>
 * </ol>
 *
 * <p>Both follow from reconstructing an identity instead of recording one, so the fix is server-side
 * and is planned as `todo/PLAN_a_session_records_which_project_it_was.md`: a `project_id` on
 * `sessions`, written where git already runs. This rule then stays as the fallback for legacy rows,
 * which is 100 % of the 106 sessions that exist today.</p>
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

  /** What the tab is called — the repository's own directory name, in its own case. */
  readonly label: string;

  /**
   * The whole path as it was RECORDED, for the tab's tooltip; empty for {@link UNKNOWN_PROJECT}.
   *
   * <p>Not folded: this is the one field a person copies out of the page, and a path pasted into a
   * terminal should be the path that was written down. {@link ProjectIdentity.key} is the folded
   * one, and it is the only thing that needs to be.</p>
   */
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
  return shaped(raw).toLowerCase();
}

/**
 * The same path, WITHOUT the case folding — which is the form the filesystem must be asked about.
 *
 * <p><b>CI found why these have to be two functions, and the docblock above had already named the
 * hazard before walking into it.</b> The suite was green on Windows and red on the Ubuntu runner:
 * folding the path before `existsSync` sees it asks about a directory that exists nowhere case
 * matters, so on Linux every project with a capital letter in its path reported as *not on disk*.
 * Separators, doubled separators and a trailing slash are normalised here because those are
 * spelling in every filesystem; case is not.</p>
 *
 * <p>{@link askedOnce} therefore memoises by the unfolded path, and two spellings of one Windows
 * directory cost two `existsSync` calls. That is the right trade: one extra stat per draw against
 * an identity rule that answers wrongly on half the platforms this extension runs on.</p>
 */
export function shaped(raw: string): string {
  const slashed = collapsed(raw.trim().split('\\').join('/'));

  return slashed.length > 1 ? slashed.replace(/\/$/u, '') : slashed;
}

/** A drive-qualified path, a POSIX root, or a UNC share — anything else is relative. */
const ABSOLUTE = /^([a-z]:\/|\/)/iu;

/** Which root a resolved path keeps: a UNC share's two separators, a POSIX root's one, or neither. */
function leadOf(base: string): string {
  if (base.startsWith('//')) return '//';

  return base.startsWith('/') ? '/' : '';
}

function stepped(walked: string[], part: string): void {
  if (part === '..') {
    walked.pop();

    return;
  }

  if (part !== '.' && part !== '') {
    walked.push(part);
  }
}

/**
 * A relative gitdir, resolved against the directory whose `.git` file carried it.
 *
 * <p><b>A code reviewer found why this is not optional.</b> Git writes the gitdir relative when
 * `worktree.useRelativePaths` is set (2.48 and later) or `--relative-paths` was passed, so two
 * worktrees in two unrelated products can carry the IDENTICAL line
 * `gitdir: ../repo/.git/worktrees/wt`. Read as text both answer `../repo`, and the two products land
 * in one tab — the exact merge this module exists to prevent, arriving through the door built to
 * prevent it. Resolved against the worktree that holds the file, they answer
 * `d:/products/a/repo` and `d:/products/b/repo`.</p>
 *
 * <p>Pure string work, with no `node:path` and no current directory: `path.resolve` would fold in
 * the process's cwd for a relative base and would answer with backslashes on Windows, and the keys
 * here are normalised forward-slash text by definition.</p>
 */
function resolvedAgainst(base: string, said: string): string {
  const walked: string[] = [];

  for (const part of `${base}/${said}`.split('/')) {
    stepped(walked, part);
  }

  return `${leadOf(base)}${walked.join('/')}`;
}

/**
 * The repository a linked worktree's gitdir names, or empty when it names something else.
 *
 * <p>`path` is the worktree itself, which is what a relative gitdir is relative TO.</p>
 */
function parentOf(path: string, mark: GitMark): string {
  if (mark.kind !== 'linked') return '';

  // Shaped, not folded: a relative gitdir is resolved against the real path, and the ABSOLUTE test
  // wants the drive letter in either case, which `[a-z]:\/` with the `i`-free pattern would miss.
  // Folding happens once, on the way out, because the KEY is the only folded thing here.
  const said = shaped(mark.gitdir);
  const where = ABSOLUTE.test(said) ? said : resolvedAgainst(path, said);
  const found = LINKED.exec(where);

  return found === null ? '' : found[1];
}

function lastSegment(key: string): string {
  const at = key.lastIndexOf('/');
  return at < 0 || at === key.length - 1 ? key : key.slice(at + 1);
}

function unknownProject(): ProjectIdentity {
  return { key: UNKNOWN_PROJECT, label: 'Unknown project', full: '', reachable: false };
}

/**
 * One project, with the fold applied to the KEY and to nothing a person reads.
 *
 * <p>`full` is the tab's tooltip and it is there to be COPIED — a path somebody pastes into a
 * terminal should be the path that was recorded, not a lower-cased version of it. `label` is the
 * directory's own name for the same reason. Only the key is folded, because only the key has to
 * make two spellings one bucket.</p>
 *
 * <p>Two recorded spellings of one project therefore share a key and the tooltip shows whichever
 * was drawn last. That is one of the real spellings rather than a manufactured one, which is the
 * right answer when the question has two truthful ones.</p>
 */
function identified(path: string, mark: GitMark): ProjectIdentity {
  const parent = parentOf(path, mark);
  const where = parent === '' ? path : parent;

  return {
    key: where.toLowerCase(),
    label: lastSegment(where),
    full: where,
    reachable: mark.kind !== 'gone',
  };
}

/** Which project one `sessions.repo_path` belonged to. */
export function identityOf(raw: string, read: MarkReader): ProjectIdentity {
  const path = shaped(raw);

  return NO_IDENTITY.has(path) ? unknownProject() : identified(path, read(path));
}

/**
 * Wraps a reader so a path is asked about once however many sessions share it.
 *
 * <p><b>Its LIFETIME is the point, and two code reviewers found that out the hard way.</b> Created
 * inside the grouping, it memoised within one draw and threw the answers away — so every press of
 * a language tab probed all 91 paths of the live corpus again, synchronously, on the extension
 * host. 41 % of them do not exist, and one recorded UNC path on a sleeping server blocks the host
 * until the filesystem answers. So the panel holds one of these for as long as the pairs it
 * describes, and a filter press touches no disk at all.</p>
 *
 * <p>Which is also why there is no `identitiesOf` here any more: a map of identities keyed by the
 * RESOLVED key cannot be looked up by the path a caller holds — a reviewer pointed out that this
 * is exactly why the grouping had to iterate for itself — and a memoised reader is the honest
 * shape of the same saving, because it answers the question the caller actually asks.</p>
 */
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
