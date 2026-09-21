import { randomBytes } from 'node:crypto';

import * as vscode from 'vscode';

import { asText } from './asText';
import { notify } from './notify';
import { AskedRevision, KeepWrite, PairsRead } from './roundsDbRead';
import { AskedTree } from './reviewTreeRead';
import { settingWritten } from './settingWrite';
import { applyToneDelta, currentTextTone, pushTextToneTo } from './textToneHost';
import { applyZoomDelta, currentUiScale, pushUiScaleTo } from './uiScaleHost';

import { FilterPress, reviewPageHtml } from './bugzReviewPage';
import { ReviewPair } from './reviewPair';
import { FileAtRead, RevisionDocument } from './openAtRevision';
import { TreeRead } from './reviewTree';
import { CallEnd, Calls } from './callHierarchy';
import { AskedAbout } from './callHierarchyAsk';
import { calledOut } from './callsBlock';
import { CallsPanel } from './callsPanel';
import { askedOnce, MarkReader, readGitMark } from './projectIdentity';
import { RealRead, realView } from './realMethodView';
import { RevisionPanel } from './revisionPanel';
import { ALL, HeldTabs, reviewTabs } from './reviewTabs';

/**
 * The window the review page lives in.
 *
 * <p>A panel of its own rather than a section of the sidebar, on `RoundsLogPanel`'s precedent: a
 * person reading two hundred before/after pairs is reading, and a sidebar column is not where
 * reading happens.</p>
 *
 * <p><b>It never paints a decision itself.</b> The page posts what a person pressed; the extension
 * writes it; the page is then redrawn from what the database SAYS. A page that congratulated itself
 * and was wrong is exactly the failure `keep` was made a column to prevent — and it is the failure
 * story 4's Collect button had, when it was drawn from a flag instead of from a row.</p>
 */
export interface ReviewHooks {
  /** The pairs as the server has them now, or why it could not say. */
  readonly read: () => Promise<PairsRead>;

  /** Writes a batch of decisions, or says why it could not. */
  readonly decide: (ids: readonly number[], keep: number) => Promise<KeepWrite>;

  /**
   * One pair's method as it really was, at both commits — `--real-method`, one process per call.
   *
   * <p>Called per OPENED row with the view on, and cached here for the panel's lifetime, so a
   * toggle flip with four rows open costs four calls at most, once. Never at paint: 200 pairs would
   * be 400 git reads on a path that already costs 468 ms.</p>
   */
  readonly readReal: (findingId: number) => Promise<RealRead>;

  /**
   * One pair's file as it was at the commit the reviewers read — `--file-at`, one process per call.
   *
   * <p>Called on a PRESS, never at paint, and what it answers is remembered for the panel's lifetime
   * per repository: a checkout that is gone answers once and every row of it stops offering.</p>
   */
  readonly readFileAt: (asked: AskedRevision) => Promise<FileAtRead>;

  /** Shows the text in a read-only document of this product's own scheme, named for its revision. */
  readonly showRevision: (document: RevisionDocument) => Promise<void>;

  /** Shows a file from the live filesystem — only ever a path `currentFileIn` has already judged. */
  readonly showCurrent: (file: string, line: number) => Promise<void>;

  /** The open workspace folders, as OS paths — what the current-file guard is judged against. */
  readonly folders: () => readonly string[];

  /** Who calls a row's method, and what it calls — the editor's own answer, about the CURRENT checkout. */
  readonly askCalls: (about: AskedAbout) => Promise<Calls>;

  /** Go to one end of a call. */
  readonly openCall: (end: CallEnd) => Promise<void>;

  /** One pair's repository checked out at its commit — the server, never git from here. */
  readonly readTreeAt: (asked: AskedTree) => Promise<TreeRead>;

  /** Opens a checkout as a folder, in a window of its own, leaving this page where it is. */
  readonly openFolder: (path: string) => Promise<void>;

  /**
   * Something was decided.
   *
   * <p>The sidebar shows how many pairs are still waiting, and a decision made HERE changed that.
   * Without this the count sat stale until something unrelated repainted the panel — the review
   * window and the section were reading one database and telling two stories. (Code round, gemini.)</p>
   */
  readonly changed: () => Promise<void>;
}

/**
 * What the page can post, as a UNION with required fields per kind rather than a bag of optionals.
 *
 * <p>A bag compiles whatever is missing. A malformed `zoom` became a zero-delta write, a `decide`
 * with no ids became a decision about nothing, and the page and this side could drift apart without
 * a type error anywhere — which is the argument a code reviewer made, and it only gets worse as
 * epics 2 to 4 add actions. The shapes below are what the page actually sends; {@link asReviewMessage}
 * is the one place raw webview data becomes one of them.</p>
 *
 * <p>`ready` is deliberately absent. The page announces itself and this side has nothing to do
 * about it, so it belongs with the messages that are DROPPED rather than with the ones that are
 * handled — a member here would be a case the switch has to answer for and never receives.</p>
 */
type ReviewMessage =
  | { readonly type: 'decide'; readonly ids: readonly number[]; readonly keep: number }
  | { readonly type: 'expand'; readonly ids: readonly number[]; readonly open: boolean }
  | { readonly type: 'expandAll'; readonly ids: readonly number[]; readonly open: boolean }
  | { readonly type: 'zoom' | 'tone'; readonly delta: number }
  /** A filter strip was pressed: which strip, and the key of the tab in it. */
  | { readonly type: 'tab'; readonly strip: string; readonly key: string }
  /** The un-anonymised view was switched; held so the next paint draws the same view. */
  | { readonly type: 'realText'; readonly on: boolean }
  /** An open row wants its real method; the generation is echoed back so a late answer can be told stale. */
  | { readonly type: 'fetchReal'; readonly id: number; readonly generation: string }
  /** A row's file was asked for — at the commit the reviewers read, or as it is now. */
  | { readonly type: 'openAt' | 'openCurrent' | 'openTree' | 'calls'; readonly id: number }
  | { readonly type: 'openCall'; readonly at: string };

/**
 * What an in-flight real-method read is keyed by: the row AND the two commits it is about.
 *
 * <p>The finding id alone is not enough. A recollection under the page keeps the id and replaces the
 * commits, so a read started for the old pair answers about commits nobody is looking at — and
 * `settle` stores it under the shas it began with, which both readers then refuse. Nothing wrong was
 * ever DISPLAYED; what was wasted was the fetch, and the new pair went unanswered for a cycle.
 * (Code round, codex, twice.)</p>
 */
function inFlightKey(pair: ReviewPair): string {
  return `${pair.findingId}@${pair.headSha}:${pair.fixSha}`;
}

/** Whole numbers only, junk dropped — an id is a row this database has or it is nothing. */
const numbers = (raw: unknown): readonly number[] =>
  (Array.isArray(raw) ? raw : []).map(Number).filter(Number.isFinite);

/**
 * Raw webview data, turned into one of the shapes above or into nothing at all.
 *
 * <p>Three things this has to survive, all of which a reviewer named and none of which the page
 * sends today — which is the point, because what arrives here is only the page's while nothing has
 * gone wrong. `null` and `undefined` throw on the first field read. `{"type":"__proto__"}` finds
 * `Object.prototype` on a plain lookup table, and calling it raises a `TypeError` rather than being
 * ignored. And a `delta` of `NaN` reaches `clampScale`, which answers 0 for anything non-finite —
 * so a junk press would have silently RESET somebody's zoom instead of doing nothing.</p>
 */
function asReviewMessage(raw: unknown): ReviewMessage | undefined {
  if (typeof raw !== 'object' || raw === null) {
    return undefined;
  }
  const m = raw as Record<string, unknown>;
  const open = m['open'] === true;

  switch (m['type']) {
    case 'decide':
      return { type: 'decide', ids: numbers(m['ids']), keep: Number(m['keep']) };
    case 'expand':
      return { type: 'expand', ids: numbers([m['id']]), open };
    case 'expandAll':
      return { type: 'expandAll', ids: numbers(m['ids']), open };
    case 'tab':
      return { type: 'tab', strip: String(m['strip']), key: String(m['key']) };
    case 'realText':
      return { type: 'realText', on: m['on'] === true };
    case 'fetchReal':
      // A generation that is not a string could never match what the page holds, and an id that
      // is not a WHOLE, non-negative number names no row: both are junk, and junk is dropped rather
      // than answered. `Number.isFinite` alone let `1.5` and `-1` through to a server call that
      // could only refuse them. (Code round, codex.)
      return Number.isInteger(Number(m['id'])) && Number(m['id']) >= 0 && typeof m['generation'] === 'string'
        ? { type: 'fetchReal', id: Number(m['id']), generation: m['generation'] }
        : undefined;
    case 'openCall':
      return typeof m['at'] === 'string' ? { type: 'openCall', at: m['at'] } : undefined;

    case 'openAt':
    case 'openCurrent':
    case 'openTree':
    case 'calls':
      // The same rule as `fetchReal`: an id that is not a whole, non-negative number names no row.
      return Number.isInteger(Number(m['id'])) && Number(m['id']) >= 0
        ? { type: m['type'], id: Number(m['id']) }
        : undefined;
    case 'zoom':
    case 'tone':
      return Number.isFinite(Number(m['delta']))
        ? { type: m['type'], delta: Number(m['delta']) }
        : undefined;
    default:
      // `ready` included: the page announces itself and this side has nothing to do about it.
      return undefined;
  }
}

export class BugzReviewPanel {
  private panel: vscode.WebviewPanel | undefined;

  /** Posts and redraws, in order — two decisions in flight would race the redraw between them. */
  private inFlight: Promise<void> = Promise.resolve();

  /**
   * Which pairs are showing their code.
   *
   * <p>Here rather than in the page, because {@link draw} replaces `webview.html` wholesale and the
   * new document remembers nothing — `rolesPanel.ts` holds its open tab for exactly this reason and
   * explains it at length. Every decision redraws, so without this a person who opened four rows,
   * ticked one and pressed Keep would be thrown back to a fully collapsed list.</p>
   *
   * <p><b>A Set of `findingId`, not of positions.</b> A redraw can reorder rows and can drop the one
   * that was open, and an index would then re-open somebody else's method — which is worse than
   * losing the state, because it is wrong rather than merely empty.</p>
   *
   * <p>Not a setting: which rows somebody had open is not worth a key in their synced settings, and
   * it is meaningless against a corpus that has been collected again. It is emptied when the window
   * is closed, too — this object outlives the webview, and a page reopened a day later is supposed
   * to start collapsed rather than re-open four rows whose contents may since have been recollected.
   * (A code reviewer found that; the first version kept it for the lifetime of the extension.)</p>
   */
  private expanded: ReadonlySet<number> = new Set<number>();

  /**
   * The last read, kept so a filter press does not cost a server process.
   *
   * <p>{@link draw} asks the server; pressing a tab does not have to. The reasoning is
   * {@link remember}'s, one step further along: a round trip per click would make narrowing a list
   * cost a process, and unlike a decision a filter changes nothing the server knows about. So the
   * strips repaint from what was already read, and only a real change — a decision, a poll, the
   * panel being reopened — goes back to the database.</p>
   *
   * <p>Cleared with the window, like {@link expanded}: this object outlives the webview, and pairs
   * read a day ago are not what the page should reopen with.</p>
   */
  private held: readonly ReviewPair[] = [];

  /** Why the last read failed, if it did — which is NOT the same page as an empty corpus. */
  private trouble = '';

  /**
   * Which project and which language the person chose.
   *
   * <p>Held here for {@link expanded}'s reason: `draw` replaces the document wholesale, so a
   * selection living in the page would die on the first decision anybody made. It is not a setting
   * — which project somebody was looking at is not worth a synced key, and it is meaningless
   * against a corpus that has been collected again.</p>
   *
   * <p>It is what was HELD, never what is shown: `reviewTabs` decides the latter, and drops a
   * choice whose pairs are gone rather than filtering the table to nothing.</p>
   */
  private chosen: HeldTabs = { project: ALL, language: ALL };

  /**
   * The filesystem, asked about each path once — for as long as the pairs it describes.
   *
   * <p><b>Its lifetime is the whole point, and two code reviewers found that out.</b> The cache
   * used to be built inside the grouping, so it memoised within one repaint and was discarded: a
   * language press then re-probed all 91 paths of the live corpus, synchronously, on the extension
   * host. 41 % of them do not exist, and one recorded UNC path on a sleeping server blocks the host
   * until the filesystem gives up — for a project that may not even be the one on screen. Held
   * here, a filter press touches no disk at all.</p>
   *
   * <p>Replaced on every {@link draw}, not kept forever: a checkout can be created or deleted
   * between reads, and the answer this cache holds is only as good as the moment the pairs came
   * from.</p>
   */
  private marks: MarkReader = askedOnce(readGitMark);

  /**
   * The tab just activated, so the repaint can give the keyboard back what it was on.
   *
   * <p>A person who tabs to a project, presses Enter and finds the focus at the top of a new
   * document has to navigate the whole page again to reach the language strip beside it — and again
   * for the next press. Found on the code round. Empty on a draw that nobody pressed.</p>
   */
  private pressed: FilterPress | undefined = undefined;

  /**
   * Whether the methods are shown un-anonymised — a view, held here for {@link expanded}'s reason.
   *
   * <p>Panel-held and not a setting, an assumption stated in the plan rather than silently made:
   * its two neighbours (`expanded`, `chosen`) are panel-held with a written reason, and if the
   * operator wants it remembered across windows it becomes a setting later. Off when the window
   * closes: a page reopened a day later shows what leaves the machine, and asks for the rest.</p>
   */
  private realText = false;

  /**
   * The real methods fetched so far, by `findingId` — for the panel's lifetime.
   *
   * <p>Each entry remembers the two commits it was fetched FOR, and is a miss when the pair now
   * names others: a recollection under the page changes the fix commit, and a cache keyed on the
   * id alone would show last week's method under this week's heading. Bounded by the pairs on the
   * page (at most `MAX_LIMIT`) and emptied with the window, which is its retention rule. Only a
   * read that REACHED the server is kept — a domain reason (no such pair, a pruned commit) is data
   * and is cached; a failed process is not, so the next flip or open tries again.</p>
   */
  private real: ReadonlyMap<number, HeldReal> = new Map<number, HeldReal>();

  /** The fetches in flight, so four rows asking twice in a row cost four processes and not eight. */
  private fetching: ReadonlyMap<string, Promise<RealRead>> = new Map<string, Promise<RealRead>>();

  /** Which paint this is — part of every generation the page asks with, so a redraw stales what came before. */
  private draws = 0;

  /**
   * Reaching the code, with the three pieces of state only it touches.
   *
   * <p>It came out of this class when the class reached 831 lines against the 800 the style rule
   * allows — a cluster that takes its own fields with it is one that has found its seam. It posts
   * through this panel rather than holding a webview, so there is still ONE place here that talks
   * to VS Code.</p>
   */
  private readonly revisions: RevisionPanel;

  /** Its own object, as the revisions are: its state is its own and the panel is at its ceiling. */
  private readonly calls: CallsPanel;

  constructor(private readonly hooks: ReviewHooks) {
    this.calls = new CallsPanel(
      { ask: hooks.askCalls, open: hooks.openCall },
      (items) => { void this.panel?.webview.postMessage({ type: 'calls', items }); });
    this.revisions = new RevisionPanel(hooks, (items) => {
      void this.panel?.webview.postMessage({ type: 'revisions', items });
    });
  }

  get isOpen(): boolean {
    return this.panel !== undefined;
  }

  /** Opens it, or brings back the one that is already open and refreshes it. */
  async show(): Promise<void> {
    if (this.panel === undefined) {
      this.panel = vscode.window.createWebviewPanel(
        'coai.bugzReview',
        'Review bugs',
        vscode.ViewColumn.Active,
        {
          enableScripts: true,
          retainContextWhenHidden: true,
          // `enableFindWidget` is the whole of Ctrl+F: the editor's find bar never reaches a
          // webview on its own, and a page of two hundred pairs that cannot be searched is a page
          // nobody can use. Fixed at creation — there is no API to turn it on afterwards.
          enableFindWidget: true,
        },
      );
      // Registered at CREATION and disposed with the panel: `pushUiScaleTo` hooks
      // `onDidChangeConfiguration`, and a listener that outlives its webview posts into a disposed
      // one on the next press of the control from any other page.
      const zoomHook = pushUiScaleTo(this.panel.webview);
      const toneHook = pushTextToneTo(this.panel.webview);
      this.panel.onDidDispose(() => {
        zoomHook.dispose();
        toneHook.dispose();
        // A closed window starts collapsed when it comes back. This object outlives the webview.
        this.expanded = new Set<number>();
        // And anonymised, with nothing remembered: the cache's lifetime IS the window's.
        this.realText = false;
        this.real = new Map<number, HeldReal>();
        this.fetching = new Map<string, Promise<RealRead>>();
        // And nothing remembered about reaching the code: a checkout can have come back.
        this.revisions.forget();
        this.calls.forget();
        this.panel = undefined;
      });
      this.panel.webview.onDidReceiveMessage((m: unknown) => this.received(m));
    }

    await this.draw();
    // `?.` because `draw` awaits the server and a person can close the window while it does; the
    // dispose handler then clears this. The command dispatcher starts `run()` with `void`, so the
    // rejection would surface as an unhandled one rather than as anything anybody could act on.
    this.panel?.reveal(vscode.ViewColumn.Active);
  }

  /**
   * What the page pressed, once it has been shown to be one of the things this panel understands.
   *
   * <p>A `switch` over the union rather than a lookup table: a plain object's prototype chain
   * answers `handlers['__proto__']` with something truthy and uncallable, and a `switch` has no
   * prototype to inherit from. `asReviewMessage` has already made every field the type it claims.</p>
   */
  private received(raw: unknown): void {
    const m = asReviewMessage(raw);
    if (m === undefined) {
      return;
    }

    switch (m.type) {
      case 'decide':
        this.queue(m.ids, m.keep);

        return;
      case 'expand':
      case 'expandAll':
        if (!m.open) {
          // A collapsed row does not want what it asked for. The provider cannot be told to stop —
          // none of the three commands takes a token — but the answer can be refused, and the one it
          // already has goes too so that reopening after a branch switch asks again rather than
          // showing the old checkout's count. (Code round, four findings.)
          this.calls.closed(m.ids, this.held);
        }
        this.remember(m.ids, m.open);

        return;
      case 'tab':
        this.narrow(m.strip, m.key);

        return;
      case 'realText':
        this.realText = m.on;

        return;
      case 'fetchReal':
        void this.answerReal(m.id, m.generation);

        return;
      case 'openAt':
        void this.opened(m.id, (pair) => this.revisions.openAt(pair, this.held));

        return;
      case 'openCurrent':
        void this.opened(m.id, (pair) => this.revisions.openCurrent(pair, this.held));
        return;

      case 'openTree':
        void this.opened(m.id, (pair) => this.revisions.openTree(pair, this.held));
        return;

      case 'calls':
        void this.opened(m.id, (pair) => this.calls.ask(pair, this.held));
        return;

      case 'openCall': {
        const named = calledOut(m.at);
        if (named !== undefined) {
          void this.calls.open(named.id, named.which, named.at);
        }
      }

        return;
      default:
        void settingWritten(
          m.type === 'zoom' ? applyZoomDelta(m.delta) : applyToneDelta(m.delta), 'bugzReview');
    }
  }

  /**
   * Write down which rows are showing their code. It does NOT redraw.
   *
   * <p>The page has already painted the row — it owns the gesture, because a redraw runs the server
   * and a round trip per click would make opening a row cost a process. This side only has to know
   * what to render the NEXT time something redraws it.</p>
   *
   * <p>A NEW set each time rather than `add`/`delete` in place: `coding-style.md` asks for it, and
   * a reviewer pointed out what it buys here beyond obedience — nothing downstream can be holding a
   * set that changes under it, which is what made the pruning below safe to stop mutating too.</p>
   */
  private remember(ids: readonly number[], open: boolean): void {
    const next = new Set(this.expanded);
    for (const id of ids) {
      if (open) {
        next.add(id);
      } else {
        next.delete(id);
      }
    }

    this.expanded = next;
  }

  /**
   * The open rows that still exist — WITHOUT deciding that the others never will again.
   *
   * <p>The first version deleted the absent ids from the panel's own set, and two reviewers arrived
   * at the same objection from opposite directions: one wanted it pruned on a failed read too, the
   * other wanted it not pruned at all. The second is right, and it dissolves the first. An id that
   * names no row on screen renders nothing, so keeping it costs one number; deleting it is
   * irreversible, and a corpus that comes back — a filter cleared, a tab switched, epic 2's
   * grouping — would come back collapsed for no reason a person could see.</p>
   *
   * <p>So this intersects per render and mutates nothing, which also means it no longer matters
   * whether the read succeeded: {@link draw} simply does not call it when there are no pairs to
   * intersect with.</p>
   */
  private keptOpen(pairs: readonly ReviewPair[]): ReadonlySet<number> {
    const alive = new Set(pairs.map((p) => p.findingId));

    return new Set([...this.expanded].filter((id) => alive.has(id)));
  }

  /**
   * Writes one batch, then redraws from storage.
   *
   * <p>Serialised through {@link inFlight} because a person can press Keep and then Drop faster than
   * a write completes, and two redraws racing would leave the page showing whichever finished last
   * rather than what is true.</p>
   */
  private queue(ids: readonly number[], keep: number): void {
    this.inFlight = this.inFlight.then(async () => {
      const written = await this.hooks.decide(ids, keep);
      if (!written.ok) {
        // A FAILURE, not "none of them existed". The two send a person to different places, and
        // saying the wrong one sends them to collect again for nothing. (Code round, codex.)
        // `notify`, which waits for the DISK and not for the person. It was `notifyAndAsk` here,
        // carried over from an `await vscode.window.showErrorMessage` in the code this replaced, and
        // both have the same defect: a VS Code error toast does not dismiss itself, so `this.draw()`
        // below did not run until somebody noticed the toast in the corner and closed it — leaving
        // this panel frozen with its controls disabled and no terminal state. The record is still on
        // disk before the toast appears; what stops is waiting for a human who has no decision to
        // make. (gemini, the code round.)
        await notify({
          as: 'error',
          class: 'failure',
          source: 'bugzReview',
          code: 'bugz-decision-not-saved',
          title: `The decision could not be saved: ${written.why}`,
          detail: written.why,
        });
      } else if (written.decided !== ids.length) {
        await notify({
          as: 'warning',
          class: 'failure',
          source: 'bugzReview',
          code: 'bugz-decisions-partly-written',
          title: `${written.decided} of ${ids.length} decisions were written. The rest name pairs this `
            + 'database does not have — collect again and they will come back.',
          detail: `${written.decided} of ${ids.length}`,
        });
      }

      await this.draw();
      await this.hooks.changed();
    }).catch(async (error_: unknown) => {
      // The same CODE as the refusal above, deliberately: one condition, reached two ways. The key
      // is what a repeat is counted on, and "the decision could not be saved" is one thing however
      // it arrived - and the same door, for the same reason: the redraw below must not wait for
      // somebody to close a toast that asks them nothing.
      await notify({
        as: 'error',
        class: 'failure',
        source: 'bugzReview',
        code: 'bugz-decision-not-saved',
        title: `The decision could not be saved: ${asText(error_)}`,
        detail: asText(error_),
      });
      await this.draw();
    });
  }

  /**
   * Answers one row's request for its real method — through the cache, and only ever by POSTING.
   *
   * <p>Nothing here touches `webview.html`. The page decides whether the answer is still wanted —
   * the generation it asked with goes back with it — and renders through the same path a toggle
   * flip takes, so what is on screen is always a function of the current toggle and the state the
   * row holds, never of when a process happened to finish. (Plan round, two reviewers, two angles.)</p>
   *
   * <p>A row this side no longer holds is not answered: the read that dropped it will repaint the
   * page without it, and an answer for a row that is not there is a message about nothing.</p>
   */
  private async answerReal(id: number, generation: string): Promise<void> {
    const pair = this.held.find((one) => one.findingId === id);
    if (pair === undefined) {
      return;
    }

    const read = await this.realOf(pair);
    const view = realView(pair, read);
    // `keep` is what this side already knows and the page could not: a read that REACHED the
    // server is remembered here whatever it said, and a process that failed is not. Without it the
    // page held the failure note anyway and never asked again, so a timeout became permanent while
    // the cache it was supposed to mirror was empty. (Code round, codex.)
    void this.panel?.webview.postMessage({
      type: 'real', id, generation, shown: view.shown, keep: read.ok, html: view.html,
    });
  }

  /**
   * The real method for one pair: from the cache when the cache is about the same two commits,
   * from the fetch already in flight when there is one, from the server otherwise.
   *
   * <p>A failed process is answered but not kept, so the next flip or open tries again; a read that
   * reached the server is kept whatever it said, because a pruned commit is a fact and not a
   * fault. A rejection is turned into a failed read rather than left to surface as an unhandled
   * one — the same shape `queue` gives a failed decision.</p>
   */
  private realOf(pair: ReviewPair): Promise<RealRead> {
    const held = this.real.get(pair.findingId);
    if (held !== undefined && held.headSha === pair.headSha && held.fixSha === pair.fixSha) {
      return Promise.resolve(held.read);
    }
    // Keyed by the id AND both commits, never by the id alone. A recollection under the page keeps
    // the finding id and changes the two shas, and an in-flight read started for the old pair
    // answers about the old commits: reusing it would spend the fetch and answer nothing usable,
    // because `settle` stores it under the shas it began with and both readers then refuse it.
    // (Code round, codex, twice.) The display was already safe; the wasted cycle was not.
    const key = inFlightKey(pair);
    const running = this.fetching.get(key);
    if (running !== undefined) {
      return running;
    }

    const started = this.hooks.readReal(pair.findingId).then(
      (read) => this.settle(pair, read),
      (error_: unknown) => this.settle(pair, { ok: false, tooOld: false, why: asText(error_) }),
    );
    this.fetching = new Map([...this.fetching, [key, started]]);

    return started;
  }

  /** A fetch has ended: it is no longer in flight, and it is remembered if it reached the server. */
  private settle(pair: ReviewPair, read: RealRead): RealRead {
    const key = inFlightKey(pair);
    this.fetching = new Map([...this.fetching].filter(([held]) => held !== key));
    if (read.ok) {
      this.real = new Map([...this.real, [pair.findingId, { headSha: pair.headSha, fixSha: pair.fixSha, read }]]);
    }

    return read;
  }

  /** The cached methods for the pairs being drawn — and only those whose two commits still match. */
  private realFor(pairs: readonly ReviewPair[]): ReadonlyMap<number, RealRead> {
    return new Map(pairs.flatMap((pair) => {
      const held = this.real.get(pair.findingId);

      return held !== undefined && held.headSha === pair.headSha && held.fixSha === pair.fixSha
        ? [[pair.findingId, held.read] as const]
        : [];
    }));
  }

  /** A press about one row, or nothing at all when that row is no longer on the page. */
  private async opened(id: number, act: (pair: ReviewPair) => Promise<void>): Promise<void> {
    const pair = this.held.find((one) => one.findingId === id);

    return pair === undefined ? undefined : act(pair);
  }

  private async draw(): Promise<void> {
    const open = this.panel;
    if (open === undefined) {
      return;
    }

    const answer = await this.hooks.read();
    if (this.panel === undefined) {
      // Disposed while the server was being asked. Painting into it now is the `Webview is
      // disposed` this repository has already been caught by once.
      return;
    }

    // A read that FAILED is not an empty corpus. Rendering the empty page for it would tell a
    // person their two hundred pairs are gone because a process timed out. (Code round, codex.)
    this.held = answer.ok ? answer.pairs : [];
    this.trouble = answer.ok ? '' : answer.why;
    // A new corpus, so a new cache: a checkout can have appeared or gone since the last read, and
    // nothing this side holds about the filesystem outlives the pairs it was learned for.
    this.marks = askedOnce(readGitMark);
    this.pressed = undefined;
    this.paint();
  }

  /**
   * Which project or language to show, and a repaint — without asking the server.
   *
   * <p><b>An unrecognised strip name is ignored, and that is the deliberate exception this
   * repository has already written down.</b> `coding-style.md` says an unknown name must fail
   * naming the legal values rather than fall back silently, and `chatMessages.ts` reconciles that
   * rule with this boundary at length — at a code reviewer's request on an earlier round: a
   * retained webview can be older OR newer than the extension talking to it, so a host that
   * refused a word it did not know would break the half that had done nothing wrong. A code
   * reviewer raised it again here; the answer is the same one, and `asReviewMessage` twenty lines
   * above already drops an unknown message TYPE the same way. What is silent is the ignoring, not
   * the vocabulary: both legal values are named in the condition below and both are tested,
   * including the unknown case.</p>
   */
  private narrow(strip: string, key: string): void {
    if (strip !== 'project' && strip !== 'language') {
      return;
    }

    // A project change abandons the language, rather than carrying a choice that belonged to
    // another project. `reviewTabs` would drop an impossible one anyway; this keeps a language that
    // happens to exist in BOTH projects from silently following the person across, which is a
    // filter they did not ask for.
    this.chosen = strip === 'project'
      ? { project: key, language: ALL }
      : { ...this.chosen, language: key };
    this.pressed = { strip, key };

    this.paint();
  }

  /**
   * The page, from what was last read — no server, no await.
   *
   * <p>`expanded` is pruned against everything that was read rather than against what is SHOWN: a
   * row hidden by a filter has not been closed, and re-opening the project it is in should find it
   * as it was left.</p>
   */
  private paint(): void {
    const open = this.panel;
    if (open === undefined) {
      return;
    }

    const view = { nonce: nonce(), uiScale: currentUiScale(), textTone: currentTextTone() };
    const found = reviewTabs(this.held, this.chosen, this.marks);
    this.draws += 1;

    open.webview.html = reviewPageHtml({
      ...view,
      pairs: found.shown,
      trouble: this.trouble,
      expanded: this.keptOpen(this.held),
      realText: this.realText,
      real: this.realFor(found.shown),
      revisions: this.revisions.stateFor(found.shown),
      calls: new Map(found.shown.map((pair) => [pair.findingId, this.calls.blockFor(pair)] as const)),
      draw: this.draws,
      projects: found.projects,
      languages: found.languages,
      project: found.project,
      language: found.language,
      // `exactOptionalPropertyTypes` is on, and on a draw nobody pressed the field is genuinely
      // ABSENT rather than present-and-undefined. `bugsKeysPanel` spreads the same way.
      ...(this.pressed === undefined ? {} : { focus: this.pressed }),
    });
  }
}


/** One cached real method, with the two commits it was fetched for. */
interface HeldReal {
  readonly headSha: string;
  readonly fixSha: string;
  readonly read: RealRead;
}

/**
 * One nonce per paint: the CSP admits our one script and nothing else.
 *
 * <p><b>Cryptographically random, not `Math.random()`.</b> The nonce is the whole of the content
 * security policy here — a predictable one is a policy an injected script can satisfy, which is
 * the point of having it. `Math.random()` is seeded per process and its sequence is recoverable
 * from a few outputs.</p>
 *
 * <p>The panel's own `nonce()` (`panelProvider.ts`) still uses `Math.random()`. I copied it here
 * and a scanner refused the copy; the reuse rule says to write the new one well and SAY what was
 * found rather than imitate it, so this is the correct one and that one is reported rather than
 * quietly rewritten in a change about something else.</p>
 */
function nonce(): string {
  return randomBytes(24).toString('base64url');
}
