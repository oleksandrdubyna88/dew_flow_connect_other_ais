import * as fs from 'node:fs';
import * as path from 'node:path';
import { AskedSet, lastAsked } from './claudeQuestion';

/**
 * Where Claude Code keeps its sessions, and which of them is the one being looked at.
 *
 * <p>`node:` only — no `vscode` — so every decision in it is reachable from a test with a real
 * temporary directory. The uncertainty this module carries is named rather than hidden: the
 * extension host cannot see into Claude Code's webview, so it cannot ask which session a tab is.
 * What it can do is refuse to guess when more than one session is waiting for an answer.</p>
 */

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
 * The project directory for this folder, matched case-INSENSITIVELY.
 *
 * <p>The case of the name is the one part that is not ours to predict — it is whatever the cwd
 * string was when the session started, and `d:\\rsd` and `D:\\rsd` are the same folder to Windows
 * and two different names here. So the exact name is tried first and a case-insensitive match
 * answers when it misses. Nothing at all is a legitimate answer: this folder may never have had a
 * session in it.</p>
 */
export function projectDirIn(root: string, cwd: string, names: readonly string[]): string {
  const wanted = projectDirName(cwd);
  const exact = names.find((name) => name === wanted);
  if (exact !== undefined) {
    return path.join(root, exact);
  }
  const loose = names.find((name) => name.toLowerCase() === wanted.toLowerCase());

  return loose === undefined ? '' : path.join(root, loose);
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
export function waitingIn(sessions: readonly WaitingSession[]): Waiting {
  const waiting = sessions.filter((one) => !one.asked.answered);
  if (waiting.length === 1) {
    return { kind: 'one', session: waiting[0]! };
  }
  if (waiting.length > 1) {
    return { kind: 'several', sessions: waiting };
  }
  const newest = [...sessions].sort((a, b) => a.asked.at.localeCompare(b.asked.at)).pop();

  return newest === undefined ? { kind: 'none' } : { kind: 'answered', session: newest };
}

/**
 * Every session file in a directory, newest first.
 *
 * <p>A directory that is not there is an empty list, not an exception: a folder Claude Code has
 * never run in is an ordinary state of the world.</p>
 */
export function sessionFiles(dir: string): readonly string[] {
  if (dir.length === 0) {
    return [];
  }
  let names: string[];
  try {
    names = fs.readdirSync(dir).filter((name) => name.endsWith('.jsonl'));
  } catch {
    return [];
  }

  return names
    .map((name) => {
      const full = path.join(dir, name);
      try {
        return { full, at: fs.statSync(full).mtimeMs };
      } catch {
        // Gone between the listing and the stat. Somebody else owns this directory.
        return { full, at: 0 };
      }
    })
    .sort((a, b) => b.at - a.at)
    .map((one) => one.full);
}

/**
 * The question waiting in this folder's sessions, or the reason there is none.
 *
 * <p>Every filesystem failure is CAUGHT and named. A command that throws into the void looks
 * exactly like a command that does nothing, and this one runs against a directory another product
 * owns and rewrites while it is being read.</p>
 *
 * @param home the extension host's own home. In a WSL or Remote window the host and Claude Code run
 *   on the same side, so this is the right one — and when they do not, nothing is found and the
 *   refusal names the directory that was looked in, so the difference is visible.
 */
export function waitingQuestion(home: string, cwd: string, howMany = 8): Waiting {
  const root = projectsRoot(home);
  let names: string[];
  try {
    names = fs.readdirSync(root);
  } catch {
    return { kind: 'failed', refusal: `Claude Code keeps its sessions in ${root}, and there is nothing there to read.` };
  }
  const dir = projectDirIn(root, cwd, names);
  if (dir.length === 0) {
    return { kind: 'failed', refusal: `Claude Code has no sessions for this folder — nothing named ${projectDirName(cwd)} in ${root}.` };
  }
  const sessions: WaitingSession[] = [];
  for (const file of sessionFiles(dir).slice(0, howMany)) {
    let lines: string[];
    try {
      lines = fs.readFileSync(file, 'utf8').split('\n');
    } catch (reason) {
      return { kind: 'failed', refusal: `Claude Code's session file could not be read: ${String(reason)}` };
    }
    const asked = lastAsked(lines);
    if (asked !== undefined) {
      sessions.push({ file, asked });
    }
  }

  return waitingIn(sessions);
}
