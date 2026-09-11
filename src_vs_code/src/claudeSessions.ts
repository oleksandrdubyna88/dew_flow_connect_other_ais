import * as fs from 'node:fs/promises';
import * as path from 'node:path';
import { AskedSet, humanPrompts, lastAsked } from './claudeQuestion';

/**
 * Where Claude Code keeps its sessions, and which of them is the one being looked at.
 *
 * <p>`node:` only — no `vscode` — so every decision in it is reachable from a test with a real
 * temporary directory. The uncertainty this module carries is named rather than hidden: the
 * extension host cannot see into Claude Code's webview, so it cannot ask which session a tab is.
 * What it can do is refuse to guess when more than one session is waiting for an answer.</p>
 *
 * <p><b>Asynchronous throughout.</b> A session file is megabytes after a long day, and reading eight
 * of them synchronously holds the extension host — every webview with it. Three reviewers said so
 * on one round, and the fix is not a cap on how much is read but not blocking while it is.</p>
 */

/** The directory listing failed for a reason that is not "it is not there". */
export interface ReadFailure {
  readonly refusal: string;
}

/**
 * The directory name Claude Code gives a working folder.
 *
 * <p>Every separator becomes a dash, the drive colon included: `D:\\rsd\\ClaudeRag` is
 * `D--rsd-ClaudeRag`. Verified against a real `~/.claude/projects` on this machine rather than
 * assumed — the shape is Anthropic's and undocumented, which is exactly why it was measured.</p>
 */
export function projectDirName(cwd: string): string {
  return cwd.replace(/[\\/:]/g, '-');
}

/** Where the projects live for a given home directory. */
export function projectsRoot(home: string): string {
  return path.join(home, '.claude', 'projects');
}

/**
 * The project directory for this folder, or empty when there is none.
 *
 * <p>Exact first. A case-INSENSITIVE match answers after it, because the case of the name is
 * whatever the cwd string was when the session started and `d:\\rsd` and `D:\\rsd` are the same
 * folder to Windows — but only where the filesystem agrees that they are. On a case-SENSITIVE host
 * `/work/App` and `/work/app` are two different projects, and a loose match there would hand one
 * project's question to the other. (codex, the code round, as a security finding.)</p>
 */
export function projectDirIn(
  root: string,
  cwd: string,
  names: readonly string[],
  caseBlind: boolean,
): string {
  const wanted = projectDirName(cwd);
  const exact = names.find((name) => name === wanted);
  if (exact !== undefined) {
    return path.join(root, exact);
  }
  if (!caseBlind) {
    return '';
  }
  const loose = names.filter((name) => name.toLowerCase() === wanted.toLowerCase());

  // Two names that differ only in case, on a host that cannot tell them apart, is not a thing that
  // happens — and if it does, picking one of them is the guess this module exists to refuse.
  return loose.length === 1 ? path.join(root, loose[0]!) : '';
}

/** One session file, and the question waiting in it. */
export interface WaitingSession {
  readonly file: string;
  readonly asked: AskedSet;
}

/** What was found, in the shape the caller acts on — never an optional field to interpret. */
export type Waiting =
  | { readonly kind: 'one'; readonly session: WaitingSession }
  | { readonly kind: 'none' }
  | { readonly kind: 'answered'; readonly session: WaitingSession }
  | { readonly kind: 'several'; readonly sessions: readonly WaitingSession[] }
  | { readonly kind: 'failed'; readonly refusal: string };

/**
 * Which session is waiting for an answer, over the sessions already read.
 *
 * <p>Pure, so the rule can be tested without a filesystem: ONE waiting session is the answer; two or
 * more is a refusal rather than a pick, because the host cannot tell which tab is being looked at
 * and a wrong-but-plausible guess is the silent failure this codebase spends whole rounds
 * preventing. When nothing is waiting but something was asked, the newest ANSWERED question is
 * reported as answered — that is a different sentence from "nothing was asked at all".</p>
 */
export function waitingIn(sessions: readonly WaitingSession[], looking = ''): Waiting {
  const waiting = sessions.filter((one) => !one.asked.answered);
  if (waiting.length === 1) {
    return { kind: 'one', session: waiting[0]! };
  }
  if (waiting.length > 1) {
    // TWO WAITING, AND THE TAB SAYS WHICH. There is no window id to ask for — but Claude Code writes
    // the conversation's title into the session, and that title is what the tab shows. Measured
    // against a live session before this was built. A title that names exactly one of them is not a
    // guess; anything else still refuses.
    const named = looking.length === 0
      ? []
      : waiting.filter((one) => one.asked.title === looking);

    return named.length === 1
      ? { kind: 'one', session: named[0]! }
      : { kind: 'several', sessions: waiting };
  }
  if (sessions.length === 0) {
    return { kind: 'none' };
  }
  const newest = [...sessions].sort((a, b) => a.asked.at.localeCompare(b.asked.at)).pop();

  return newest === undefined ? { kind: 'none' } : { kind: 'answered', session: newest };
}

/**
 * Every session file in a directory, newest first — or the reason the directory could not be read.
 *
 * <p>A directory that is NOT THERE is an empty list: a folder Claude Code has never run in is an
 * ordinary state of the world. Anything else — a permission, a disconnected drive — is a failure
 * that says which operation failed, because reporting it as "nothing was asked here" would be this
 * module lying about the world. (codex, twice, and the coding-style rule it cites.)</p>
 */
export async function sessionFiles(dir: string): Promise<readonly string[] | ReadFailure> {
  if (dir.length === 0) {
    return [];
  }
  let names: string[];
  try {
    names = (await fs.readdir(dir)).filter((name) => name.endsWith('.jsonl'));
  } catch (reason) {
    return missing(reason) ? [] : { refusal: `Claude Code's session folder could not be listed: ${where(reason)}` };
  }
  const dated = await Promise.all(names.map(async (name) => {
    const full = path.join(dir, name);
    try {
      return { full, at: (await fs.stat(full)).mtimeMs };
    } catch {
      // Gone between the listing and the stat: another product owns this directory and is writing
      // in it. It sorts last rather than failing the whole command.
      return { full, at: 0 };
    }
  }));

  return dated.sort((a, b) => b.at - a.at).map((one) => one.full);
}

/** Whether a filesystem error means "it is not there" rather than "it would not answer". */
function missing(reason: unknown): boolean {
  return typeof reason === 'object' && reason !== null && (reason as { code?: unknown }).code === 'ENOENT';
}

/** A filesystem error as one short sentence, without a stack nobody reads. */
function where(reason: unknown): string {
  const code = typeof reason === 'object' && reason !== null ? (reason as { code?: unknown }).code : undefined;

  return typeof code === 'string' ? code : String(reason);
}

/**
 * Everything the person wrote in the session THIS TAB is showing, oldest first.
 *
 * <p>Found by the same join the waiting question uses: Claude Code writes the conversation's title
 * into its own session file, and that title is what VS Code puts on the tab. No title, no match,
 * and an empty list — which the caller reports rather than guessing at another session.</p>
 */
export async function promptsInSession(
  home: string,
  cwd: string,
  caseBlind: boolean,
  looking: string,
): Promise<readonly string[]> {
  if (looking.length === 0) {
    return [];
  }
  let names: string[];
  try {
    names = await fs.readdir(projectsRoot(home));
  } catch {
    return [];
  }
  const dir = projectDirIn(projectsRoot(home), cwd, names, caseBlind);
  const files = await sessionFiles(dir);
  if (!Array.isArray(files)) {
    return [];
  }
  for (const file of files) {
    let body: string;
    try {
      body = await fs.readFile(file, 'utf8');
    } catch {
      continue;
    }
    const lines = body.split('\n');
    if (titleIn(lines) === looking) {
      return humanPrompts(lines);
    }
  }

  return [];
}

/** What a session calls itself, or empty — the last title wins, as the tab shows the newest. */
function titleIn(lines: readonly string[]): string {
  let title = '';
  for (const line of lines) {
    if (!line.includes('"ai-title"')) {
      continue;
    }
    try {
      const row = JSON.parse(line) as { type?: unknown; aiTitle?: unknown };
      if (row.type === 'ai-title' && typeof row.aiTitle === 'string' && row.aiTitle.length > 0) {
        title = row.aiTitle;
      }
    } catch {
      // A half-written line names nothing.
    }
  }

  return title;
}

/**
 * The question waiting in this folder's sessions, or the reason there is none.
 *
 * <p>EVERY session file is read, not the newest few: a folder with nine of them can hold its only
 * waiting question in the ninth, and reporting "nothing is waiting" then is telling the person the
 * wrong thing. (codex, twice.)</p>
 *
 * @param home the extension host's own home. In a WSL or Remote window the host and Claude Code run
 *   on the same side, so this is the right one — and when they do not, nothing is found and the
 *   refusal names the directory that was looked in, so the difference is visible.
 * @param caseBlind whether this filesystem treats two names differing only in case as one
 */
export async function waitingQuestion(
  home: string,
  cwd: string,
  caseBlind: boolean,
  looking = '',
): Promise<Waiting> {
  const root = projectsRoot(home);
  let names: string[];
  try {
    names = await fs.readdir(root);
  } catch (reason) {
    return missing(reason)
      ? { kind: 'failed', refusal: `Claude Code keeps its sessions in ${root}, and there is nothing there to read.` }
      : { kind: 'failed', refusal: `Claude Code's sessions could not be listed in ${root}: ${where(reason)}` };
  }
  const dir = projectDirIn(root, cwd, names, caseBlind);
  if (dir.length === 0) {
    return {
      kind: 'failed',
      refusal: `Claude Code has no sessions for this folder — nothing named ${projectDirName(cwd)} in ${root}.`,
    };
  }
  const files = await sessionFiles(dir);
  if (!Array.isArray(files)) {
    return { kind: 'failed', refusal: (files as ReadFailure).refusal };
  }
  const sessions: WaitingSession[] = [];
  for (const file of files) {
    let body: string;
    try {
      body = await fs.readFile(file, 'utf8');
    } catch (reason) {
      if (missing(reason)) {
        // Deleted while this was running. Another product owns the directory; the rest still counts.
        continue;
      }

      return { kind: 'failed', refusal: `Claude Code's session file could not be read: ${where(reason)}` };
    }
    const asked = lastAsked(body.split('\n'));
    if (asked.kind === 'asked') {
      sessions.push({ file, asked: asked.set });
    } else if (asked.kind === 'unreadable') {
      // A question this build could not read is NOT silence. If Anthropic moves a field, the command
      // must say that rather than report that Claude is asking nothing. (codex, the code round.)
      return {
        kind: 'failed',
        refusal: `Claude Code asked something this build does not recognise, in ${file}.`
          + ' The session format has changed — this needs a new release.',
      };
    }
  }

  return waitingIn(sessions, looking);
}
