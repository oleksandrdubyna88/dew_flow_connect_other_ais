import * as os from 'node:os';
import * as vscode from 'vscode';
import { ChatEntry, ChatPanels } from './chatPanels';
import { Thread, threads } from './chatThread';
import { keepQueued } from './chatPersist';
import { ConversationSource, sourceOfFile, sourceOfSession } from './chatStore';
import { ConversationIndex } from './chatStoreCache';
import { GotoAsked, sessionSourceOf, tabKindOf } from './chatGoto';
import { NAMES_ARE_CASE_BLIND, conversationWorkspace, everyFolder, fsPathOf, whereToLook } from './chatRoots';
import { filedUnder, sameRoot, sessionIdOf } from './chatSource';
import { snapshots } from './chatCapture';
import { isClaudeSessionTab, isOrdinaryEditorTab, sourceSession } from './sessionKey';
import { Asked, Found, oneAnswerFrom, pinnable, promptsFrom, sessionFileIn, sessionFileOf, severalMatch } from './claudeSessions';

/**
 * How a tab is joined to the Claude Code session behind it.
 *
 * <p>Extracted from `chatCommand.ts` unchanged. Everything here answers one question — WHICH session
 * is this conversation's — asked in the three places it comes up: as a tab opens, in the background;
 * when the Asked button is pressed and nothing was pinned; and when *go to* wants to know what this
 * window already holds.</p>
 *
 * <p>The decisions are not here. Which answers count as one session, what a refusal says, and what
 * *go to* does with any of it are `claudeSessions.ts`, `chatSource.ts` and `chatGoto.ts` — pure, and
 * tested as values. This is the half that walks a real folder and reads a real tab.</p>
 */

/**
 * Where this tab's session is, however the window was opened.
 *
 * <p>A window with NO FOLDER still runs Claude Code — and it runs it in the HOME directory, which is
 * where a VS Code terminal starts when there is no folder to start in. So that is where this looks,
 * as though home were the workspace: the operator's own session, which the button could not see, is
 * filed under `C--Users-strug`.</p>
 *
 * <p><b>Not every project on the machine.</b> That was the first attempt and it was worse than the
 * bug: 77 project directories and a gigabyte of transcript on this machine, read to compare titles,
 * while the region sat on *Reading the session…*. The refusal names the directory it looked in, so a
 * window whose Claude Code was started somewhere else says where it did look rather than hunting.</p>
 */
export async function findSession(title: string): Promise<readonly Found[]> {
  return (await findSessionIn(title)).map((one) => one.found);
}

/** One folder's answer, WITH the folder — the provenance a pinned session is filed under. */
export interface FoundIn {
  readonly folder: string;
  readonly found: Found;
}

/**
 * The same search, keeping the folder each answer came from.
 *
 * <p>`findSession` flattens this away, and for its callers that is right: they are deciding whether
 * a tab has one session or several, and the folder is not part of that question. It IS part of
 * story C1's: a session's id names no directory, so the only honest way to know which root a Claude
 * conversation belongs to is to remember where it was found. Three reviewers refused the alternative
 * — decoding the root out of Claude's own encoded directory name — and so would I: it is another
 * program's encoding, undocumented, and a decoder that drifts misfiles conversations silently.</p>
 */
export async function findSessionIn(title: string): Promise<readonly FoundIn[]> {
  return await everyFolder(async (folder, caseBlind) => ({
    folder,
    found: await sessionFileIn(os.homedir(), folder, caseBlind, title),
  }));
}

/**
 * Find this conversation's session by name, keep the file, and read it.
 *
 * <p>The path a tab takes when it has no pin — because it was restored from a reload, or because the
 * background walk at open time found nothing yet. What it resolves it keeps, on the same terms
 * `pinSession` uses: exactly one session, across every root, with nothing else in doubt.</p>
 */
export async function resolveAndPin(entry: ChatEntry, mine: Thread): Promise<Asked> {
  // THE ID FIRST. A conversation that was pinned once keeps `source: {kind:'claude', sessionId}`,
  // and that id is what the file is CALLED — so the answer is a directory entry rather than every
  // transcript in the folder read for its title. It is also the only path that survives a rename:
  // the name walk below is hunting a string another program rewrites underneath it, which is the
  // defect this whole plan is about. Measured on the operator's folder: 5.2 s warm against one stat.
  const byId = await findSessionById(mine.source);
  // ONE root, on the same terms the name walk uses. A UUID naming a session in two roots at once is
  // not a shape this data has — which is exactly why answering with whichever root was listed first
  // would be a pick nobody would ever see fail. The conversation's OWN filed root answers first when
  // it is one of them, since that is where this session was found the day it was pinned. (gemini,
  // the plan round.)
  const own = byId.find((one) => one.found.kind === 'one' && sameRoot(one.folder, mine.workspace, NAMES_ARE_CASE_BLIND));
  // Two questions, two statements. They were one nested expression — `own ?? (pinnable ? find :
  // undefined)` — which reads as though the fallback were a default rather than a second search
  // with its own precondition. Behaviour is unchanged; what the reader has to hold at once is not.
  const anywhere = pinnable(byId.map((one) => one.found))
    ? byId.find((one) => one.found.kind === 'one')
    : undefined;
  const kept = own ?? anywhere;
  if (kept !== undefined) {
    // Nothing is written: the id is already on the record, and the path is deliberately not.
    mine.sessionFile = (kept.found as Extract<Found, { kind: 'one' }>).file;

    return await promptsFrom(mine.sessionFile);
  }
  // The id missed — this conversation never had one, or the session it names has been deleted — so
  // the tab's name is asked, and what it finds is ADOPTED: source, workspace and a queued write, on
  // the terms `pinSession` uses. Until this, a resolution found here died with the window and the
  // same walk ran again after every reload. (codex, the plan round.)
  const found = await findSessionIn(mine.title);
  if (!pinnable(found.map((one) => one.found))) {
    // Nothing to keep, and the reason is the one the refusal would give anyway.
    return oneAnswerFrom(found.map((one) => asAsked(one.found)));
  }
  adoptFound(entry, mine, found.find((one) => one.found.kind === 'one')!);

  return await promptsFrom(mine.sessionFile);
}

/**
 * Each folder's answer for the session a conversation's own source NAMES, or nothing to ask.
 *
 * <p>A source that is not a Claude session — a file-opened chat, or one that was never pinned — has
 * no id to look for, and an empty list is what says so. Every root is asked, as the name walk asks
 * them: the id names no folder, and a workspace can hold two.</p>
 */
export async function findSessionById(source: ConversationSource): Promise<readonly FoundIn[]> {
  if (source.kind !== 'claude') {
    return [];
  }

  return await everyFolder(async (folder, caseBlind) => ({
    folder,
    found: await sessionFileOf(os.homedir(), folder, caseBlind, source.sessionId),
  }));
}

/**
 * Keep the session this answer found: the file in memory, the identity on the record.
 *
 * <p>ONE road in, because there are two ways a session is discovered — the walk as a tab opens, and
 * a press that finds the tab unpinned — and they wrote different amounts of it. The press wrote the
 * file alone, so what it learned was lost at the next reload while the walk's version lasted for
 * ever. The difference was invisible: both produce prompts on screen.</p>
 *
 * <p><b>The FOLDER the session was found in</b>, never this window's first root: a session's id names
 * no directory, so the only honest answer is where it was found. That is why this cannot go through
 * `reorigin`, which computes the root from the source and gets `''` for a Claude one.</p>
 */
export function adoptFound(entry: ChatEntry, mine: Thread, one: FoundIn): void {
  const found = one.found as Extract<Found, { kind: 'one' }>;
  mine.sessionFile = found.file;
  if (!found.complete) {
    // PINNED FOR THIS WINDOW, NEVER WRITTEN DOWN. The walk was cut short by its budget, so this is
    // the only session of that name among the ones that were READ — a fine answer to "show me this
    // conversation" and no proof at all that no namesake sits beyond the cut. Writing it would make
    // a guess permanent, which is the exact failure the namesake refusal exists to prevent. (codex,
    // the code round, twice.)
    return;
  }
  const sessionId = sessionIdOf(mine.sessionFile);
  if (sessionId.length === 0) {
    // A file of a shape this build does not recognise. The tab keeps its pin — the Asked button
    // reads that file happily — and the conversation keeps no source, which is the honest answer:
    // an id invented here would match a tab that is not this one.
    return;
  }
  mine.source = sourceOfSession(sessionId);
  mine.workspace = filedUnder(one.folder, whereToLook(), conversationWorkspace(), NAMES_ARE_CASE_BLIND);
  // WRITTEN EXPLICITLY. `show`'s guard compares messages, model and mark, so a source arriving on
  // its own — which is exactly what this is, minutes after the last thing anybody said — would
  // never reach disk through that path. Queued behind the conversation's other writes, so it
  // cannot carry a stale revision.
  keepQueued(entry, mine);
}

/** A lookup answer as an answer about prompts — the refusals are word for word the same ones. */
export function asAsked(found: Found): Asked {
  return found.kind === 'one' ? { kind: 'said', said: [] } : found;
}

/**
 * Find and keep this conversation's session file, in the background, as its tab opens.
 *
 * <p>Not awaited: a tab must appear at once, and this is a walk over a folder of session files whose
 * answer is not needed until somebody presses Asked. Not on a FILE-opened chat at all — there is no
 * session behind one. If it finds nothing, the button falls back to searching by name, which is
 * where it started.</p>
 */
export function pinSession(entry: ChatEntry, title: string, fromSession: boolean): void {
  if (!fromSession || title.length === 0) {
    return;
  }
  void (async () => {
    let found: readonly FoundIn[];
    try {
      found = await findSessionIn(title);
    } catch {
      // Nothing is pinned and nothing is said: the button still works by name, and a tab must not
      // take down the extension host for a walk it started on its own.
      return;
    }
    const mine = threads.get(entry.id);
    if (mine === undefined || !pinnable(found.map((one) => one.found))) {
      return;
    }
    adoptFound(entry, mine, found.find((answer) => answer.found.kind === 'one')!);
  })().catch((reason: unknown) => {
    // The outer edge of a detached call. The `try` above covers only the walk; everything after it —
    // the id, the file, the queue — used to escape unobserved, which `reliability.md` forbids of any
    // edge nothing is above. (gemini, the code round.)
    console.error('ConnectOtherAIs: pinning a conversation to its Claude session threw', reason);
  });
}

/**
 * Whether this answer means the folder did not SAY, as against saying there is nothing of that name.
 *
 * <p>Exhaustive over `Found.none`'s reason rather than a comparison against one string: a third
 * reason added to that union — a cancelled walk, a second kind of partial read — must be a compile
 * error here rather than a new failure silently counted as a completed search. (codex, the code
 * round.)</p>
 */
export function didNotAnswer(one: Found): boolean {
  if (one.kind !== 'none') {
    return false;
  }
  switch (one.why) {
    case 'unreadable':
      return true;
    case 'unmatched':
      return false;
    default: {
      const unhandled: never = one.why;

      throw new Error(`a walk outcome this build has no reading for: ${JSON.stringify(unhandled)}`);
    }
  }
}

/**
 * Everything *go to* needs to know, gathered from the host — the other half of `chatGoto.ts`.
 *
 * <p>It lives here because every piece of it is private to this file: the thread registry, the tab
 * snapshot, the session walk, the roots. What it hands back is a value, so the decision itself stays
 * where a test can reach it. One snapshot answers for the tab and for what this window already
 * holds, so the two cannot disagree about which tab is active.</p>
 *
 * <p>The Claude walk is the only slow part and it is only done for a Claude tab that has no live
 * conversation — which is the case *go to* exists for, and the one where a person is already waiting
 * to be taken somewhere.</p>
 */
export async function askedForGoto(
  panels: ChatPanels,
  index: ConversationIndex,
): Promise<{ readonly asked: GotoAsked; readonly key: object | undefined }> {
  const { active, all } = snapshots();
  const known = panels.known();
  const claude = sourceSession(active, all, known);
  const matched = claude ?? sourceSession(active, all, known, isOrdinaryEditorTab);
  const tab = all.find((one) => one.key === matched?.key) ?? active;
  const kind = tabKindOf({ claude: tab !== undefined && isClaudeSessionTab(tab), document: tab !== undefined && isOrdinaryEditorTab(tab) });
  const path = kind === 'document' ? fsPathOf(tab?.uri ?? '') : '';
  const here = filedUnder(path, whereToLook(), conversationWorkspace(), NAMES_ARE_CASE_BLIND);
  const live = matched === undefined ? undefined : threads.get(panels.get(matched.key)?.id ?? {});
  // THE WALK, with progress: it reads a directory of session files and can take seconds, and a
  // person who has just pressed a chord and sees nothing presses it again. The same notification the
  // Asked button already shows for the same walk. (Four findings, the code round.)
  const walked = kind === 'claude' && live === undefined
    ? await vscode.window.withProgress(
      { location: vscode.ProgressLocation.Window, title: 'Finding this conversation…' },
      async () => sessionsNamed(tab?.label ?? ''),
    )
    : { found: [] as readonly Found[], unsure: false };
  const one = walked.found.find((answer) => answer.kind === 'one');
  // THREE outcomes, asked in order rather than as a ternary inside a ternary. The nested form said
  // `kind === 'claude'` twice and made the middle case — a Claude tab whose walk matched nothing —
  // the hardest of the three to find, which is the one a reader is usually looking for.
  const sourceFor = (): ConversationSource => {
    if (kind !== 'claude') {
      return sourceOfFile(tab?.uri ?? '');
    }
    if (!pinnable(walked.found)) {
      return { kind: 'none' as const };
    }

    return sessionSourceOf(sessionIdOf(one?.kind === 'one' ? one.file : ''));
  };
  const source = sourceFor();

  const asked: GotoAsked = {
    tab: { kind, label: tab?.label ?? '', path },
    live: live?.saveId ?? '',
    source,
    // Only a Claude tab can be ambiguous in this sense, and `pinnable` is the same rule the pin uses.
    // A walk that could not be done counts as ambiguous too: not knowing which session a tab belongs
    // to is a reason to ASK, never a reason to offer to start a second conversation.
    ambiguous: kind === 'claude' && (walked.unsure || (walked.found.length > 0 && !pinnable(walked.found))),
    // THE POSITIVE FACT, and the only thing that licenses the name fallback in `goto`: more than one
    // session really does answer to this name. A walk that could not be done arrives as `false` and
    // asks, and so will any future reason to be ambiguous. Asked of the ANSWERS rather than of the
    // array — `findSession` returns one outcome per FOLDER and a single folder can answer `several`,
    // so a length test was false in a one-root workspace however many sessions shared the name, and
    // the fallback was dead in the commonest case there is. (CodeRabbit, on the pull request.)
    severalSessions: severalMatch(walked.found),
    // THE OTHER HALF of what `ambiguous` folds together: a walk that could not be DONE, as against
    // one that was done and matched nothing. Two situations, and the person needs a different thing
    // from each — "try again" against "none of them is called that, pick one".
    //
    // A THROWN walk is not the only way it fails. A directory that would not list, and a folder too
    // big to finish reading, both come back as an ordinary `none` — so asking only `unsure` would
    // have called them "no session is called that", which is the very mistake this fixes. The reason
    // is a field on the answer rather than the wording of its sentence. (codex, the plan round.)
    walkFailed: walked.unsure || walked.found.some(didNotAnswer),
    // How many tabs are called what this one is called — this tab included, so never below 1. It is
    // what lets a NAME be evidence: with two tabs of one name it identifies neither.
    namesakes: all.filter((one) => one.label === (tab?.label ?? '')).length,
    candidates: index.bySource(source),
    inRoot: index.entries({ kind: 'workspace', workspace: here }),
    roots: whereToLook(),
    fallback: conversationWorkspace(),
    caseBlind: NAMES_ARE_CASE_BLIND,
    index: index.state(),
  };

  // The KEY beside the decision, not inside it: `chatGoto.ts` is pure and a tab object is a handle
  // only this side can do anything with. It is what a conversation is bound to, and it is re-checked
  // before that happens.
  return { asked, key: tab?.key };
}

/**
 * Every session this name answers to — and whether the walk could be done at all.
 *
 * <p>A failed walk is NOT an empty result, and conflating them was a real defect: "no session is
 * called that" leads to offering a new conversation, so an unreadable session directory would have
 * produced a duplicate of a conversation that already existed. It is said on the console and
 * reported as `unsure`, which the caller turns into a question rather than an answer. (Two vendors,
 * the code round; the constraint — never swallow — was my own.)</p>
 */
export async function sessionsNamed(title: string): Promise<{ readonly found: readonly Found[]; readonly unsure: boolean }> {
  if (title.length === 0) {
    return { found: [], unsure: false };
  }
  try {
    return { found: await findSession(title), unsure: false };
  } catch (reason) {
    console.warn(`ConnectOtherAIs: this tab's Claude sessions could not be read, so which conversation is its own is unknown: ${title}`, reason);

    return { found: [], unsure: true };
  }
}
