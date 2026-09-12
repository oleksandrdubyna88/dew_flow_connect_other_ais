import * as fs from 'node:fs/promises';
import { createReadStream } from 'node:fs';
import * as readline from 'node:readline';
import * as path from 'node:path';
import { AskedSet, humanSaid, lastAsked, titleFrom } from './claudeQuestion';

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
 * One answer from however many folders were looked in.
 *
 * <p>FIRST-MATCH-WINS was the bug. `onShowAsked` returned as soon as a folder produced prompts, so
 * in a workspace with two roots each holding a session called *Build*, the tab was shown whichever
 * folder VS Code happened to list first — the exact silent cross-session hand-over the title join
 * exists to prevent. Five reviewers across two vendors, on one round.</p>
 *
 * <p>So: every folder is looked in. An ambiguity in ANY of them wins first — a root that could not
 * name its own session does not become answerable because another root could. Then exactly one
 * match is the answer, two are a refusal saying how many, and none gives the FIRST reason, because
 * with several roots open the nearest miss is more use than whichever was checked last.</p>
 */
export function oneAnswerFrom(answers: readonly Asked[]): Asked {
  // AN AMBIGUITY ANYWHERE COUNTS. One root holding a single match and another holding two sessions
  // that share the tab's name is three candidates, not one — and answering with the single match
  // picks between three while looking like it picked between none. (codex, the second code round.)
  const several = answers.find((one) => one.kind === 'several');
  if (several !== undefined) {
    return several;
  }
  const said = answers.filter((one) => one.kind === 'said');
  if (said.length === 1) {
    return said[0]!;
  }
  if (said.length > 1) {
    return {
      kind: 'several',
      refusal: `${said.length} of the folders open here have a session by this tab's name, so it cannot say which one is its own.`,
    };
  }

  return answers[0] ?? {
    kind: 'none',
    refusal: 'This window has no folder open, so there is nowhere to look for a session.',
  };
}

/**
 * Whether these answers name ONE session, across every root, with nothing else in doubt.
 *
 * <p>It mirrors {@link oneAnswerFrom}, and it has to. Pinning is that decision made ONCE and kept,
 * so a pin the button would have refused makes the refusal permanent and invisible — one root
 * holding a single match while another holds two namesakes is three candidates, not one. The two
 * disagreed when they were written, which is what re-reading the diff against its own rules found.</p>
 */
export function pinnable(answers: readonly Found[]): boolean {
  return !answers.some((answer) => answer.kind === 'several')
    && answers.filter((answer) => answer.kind === 'one').length === 1;
}

/**
 * The most turns that cross to a webview at once, and the most of each.
 *
 * <p>A day-long session holds hundreds of turns and some of them are whole files pasted in. All of
 * it in one `postMessage` is megabytes over a bridge that has to stay responsive, rendered into a
 * region 40vh tall. The EARLIEST are kept, because the question this button exists to answer is
 * *what was this window for* — and a turn that is cut says so where it is cut. (gemini, the plan
 * round, on a payload nobody had bounded.)</p>
 */
export const MOST_PROMPTS = 200;
export const MOST_PER_PROMPT = 8_000;

/** What the person wrote in a session, or why there is nothing to show. */
export type Asked =
  | { readonly kind: 'said'; readonly said: readonly string[] }
  | { readonly kind: 'none'; readonly refusal: string }
  | { readonly kind: 'several'; readonly refusal: string };

/**
 * Everything the person wrote in the session THIS TAB is showing, oldest first.
 *
 * <p>Found by the same join the waiting question uses: Claude Code writes the conversation's title
 * into its own session file, and that title is what VS Code puts on the tab.</p>
 *
 * <p><b>Two sessions with one title is a refusal, never a pick.</b> It was a pick — the first file
 * the directory listed — until the plan round said so twice, from two vendors. The whole reason this
 * join exists is that delivering somebody else's conversation silently is the worst thing it can do,
 * and picking between namesakes is exactly that with extra steps. {@link waitingIn} has always
 * refused them; this now agrees with it.</p>
 *
 * <p><b>Every outcome is NAMED.</b> A session that cannot be read, a folder Claude has never run in,
 * a tab whose title matches nothing and a session where the person has said nothing yet are four
 * different situations, and a person looking at an empty box deserves to know which. (codex, on a
 * protocol with no failure result.)</p>
 */
export async function promptsInSession(
  home: string,
  cwd: string,
  caseBlind: boolean,
  looking: string,
): Promise<Asked> {
  const found = await sessionFileIn(home, cwd, caseBlind, looking);

  return found.kind === 'one' ? await promptsFrom(found.file) : found;
}

/** Which file a tab's name belongs to, or why it belongs to none. */
export type Found =
  | { readonly kind: 'one'; readonly file: string }
  | { readonly kind: 'none'; readonly refusal: string }
  | { readonly kind: 'several'; readonly refusal: string };

/**
 * The one session file this tab's name belongs to.
 *
 * <p>Separate from reading it, because a tab wants this ONCE and the prompts every time. Claude Code
 * refines a conversation's `ai-title` as it goes on and the tab follows it, so a name captured when
 * the chat was opened stops matching hours later — which is exactly the window this feature is for.
 * A FILE does not move. So the tab resolves its file while its name is still current and keeps it.
 * (codex, the second code round: *"persists only a mutable display title instead of stable session
 * identity"*.)</p>
 */
export async function sessionFileIn(
  home: string,
  cwd: string,
  caseBlind: boolean,
  looking: string,
): Promise<Found> {
  const root = projectsRoot(home);
  if (looking.length === 0) {
    // A tab with no name cannot be joined to anything. It is not an error — a chat opened from a
    // file is exactly this — so it is said plainly rather than dressed as a failure.
    return { kind: 'none', refusal: 'This conversation is not named after a Claude Code session.' };
  }
  let names: string[];
  try {
    names = await fs.readdir(root);
  } catch (reason) {
    return missing(reason)
      ? { kind: 'none', refusal: `Claude Code keeps its sessions in ${root}, and there is nothing there to read.` }
      : { kind: 'none', refusal: `Claude Code's sessions could not be listed in ${root}: ${where(reason)}` };
  }
  const dir = projectDirIn(root, cwd, names, caseBlind);
  if (dir.length === 0) {
    return {
      kind: 'none',
      refusal: `Claude Code has no sessions for this folder — nothing named ${projectDirName(cwd)} in ${root}.`,
    };
  }
  const files = await sessionFiles(dir);
  if (!Array.isArray(files)) {
    return { kind: 'none', refusal: (files as ReadFailure).refusal };
  }
  // TITLES FIRST, and nothing else. Which file this tab owns is one string per file; the prompts are
  // the whole conversation. Reading both in one pass meant a folder of fifty sessions was fifty
  // whole files in memory to answer a question about fifty titles. Two reviewers measured the same
  // shape at 10x and called it seconds of a blocked extension host.
  const matched: string[] = [];
  for (const file of files) {
    if (await titleOf(file) === looking) {
      matched.push(file);
    }
  }
  if (matched.length > 1) {
    return {
      kind: 'several',
      refusal: `${matched.length} sessions in this folder are called “${looking}”, so this tab cannot say which one is its own.`,
    };
  }
  const only = matched[0];
  if (only === undefined) {
    return {
      kind: 'none',
      refusal: `No session in this folder is called “${looking}” — Claude Code names a conversation once it has one.`,
    };
  }

  return { kind: 'one', file: only };
}

/**
 * What the person wrote in one known session file.
 *
 * <p>The file, not the name: a tab that resolved its session once reads straight from it afterwards,
 * so a conversation Claude Code has since renamed is still the tab's own.</p>
 */
export async function promptsFrom(file: string): Promise<Asked> {
  const said = await promptsOf(file);
  if (said.length === 0) {
    // Told apart from a file that is gone: an empty read of a file that IS there means the person
    // has not written in it yet, and a read of one that is not means the tab has outlived it.
    return await readable(file)
      ? { kind: 'none', refusal: 'Nothing of yours is in that session yet.' }
      : { kind: 'none', refusal: 'That session file is no longer on disk.' };
  }

  return { kind: 'said', said };
}

/** Whether a path is still there to be read. */
async function readable(file: string): Promise<boolean> {
  try {
    await fs.access(file);

    return true;
  } catch {
    return false;
  }
}

/**
 * Every line of a file, one at a time, stopping when the caller has what it needs.
 *
 * <p>`readline` over a stream rather than `readFile` + `split`: the second allocates the whole file
 * as a string AND again as an array of lines, and these files are tens of megabytes after a long
 * day. A file that cannot be opened or read is not an error here — another product owns this
 * directory and is writing in it — so it contributes nothing and the next one is tried.</p>
 */
async function eachLine(file: string, take: (line: string) => boolean): Promise<void> {
  let lines: readline.Interface | undefined;
  try {
    lines = readline.createInterface({ input: createReadStream(file, 'utf8'), crlfDelay: Infinity });
    for await (const line of lines) {
      if (!take(line)) {
        return;
      }
    }
  } catch {
    // Locked, gone, or not text. What has been taken so far stands.
  } finally {
    lines?.close();
  }
}

/** What a session calls itself — the LAST title, as the tab shows the newest. */
async function titleOf(file: string): Promise<string> {
  let title = '';
  await eachLine(file, (line) => {
    const named = titleFrom(line);
    if (named.length > 0) {
      title = named;
    }

    return true;
  });

  return title;
}

/**
 * What the person wrote in one session, EARLIEST first, and no more than will fit.
 *
 * <p>It stops at {@link MOST_PROMPTS}: the question this answers is what the window was FOR, so the
 * earliest turns are the ones worth carrying, and a day-long session need not be read past them.</p>
 */
async function promptsOf(file: string): Promise<readonly string[]> {
  const said: string[] = [];
  await eachLine(file, (line) => {
    const spoken = humanSaid(line);
    if (spoken.length > 0) {
      said.push(spoken.length <= MOST_PER_PROMPT
        ? spoken
        : `${spoken.slice(0, MOST_PER_PROMPT)}\n\n… (cut here — the rest is in the session file)`);
    }

    return said.length < MOST_PROMPTS;
  });

  return said;
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
