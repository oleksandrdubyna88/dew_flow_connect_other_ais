import { BugCorpus, CollectRun, EMPTY_CORPUS, hasRun, isRunning, sending } from './roundsDb';
import { sendLabel, waiting } from './bugsSend';
import { ModelChoice } from './models';
import { allowedBy, RANKING_VENDORS, type RankingChoice } from './bugzPick';

/**
 * The Bugz section: what the corpus holds, and what the last run made of it.
 *
 * <p>Markup only — a pure function of state, like every other section body here, so it is testable
 * without a webview and cannot reach a process by accident.</p>
 *
 * <p><b>There is no free-text control in this section, and that is a constraint rather than a
 * style.</b> The section must be in `staticKey` or the Collect button can never repaint to show a
 * run happening; and a section that IS in `staticKey` must hold no free-text control, because the
 * page is then rebuilt under a focused box on every keystroke — which is why the chat section's
 * prompt textarea became a picker. The ingest server's address is therefore collected through
 * `vscode.window.showInputBox` behind a button, exactly as `addTeamServer` and `customConsultant`
 * already collect theirs.</p>
 */

/** What the section needs to draw itself. */
export interface BugzViewState {
  readonly corpus: BugCorpus;
  readonly models: readonly RankingChoice[];
  readonly model: string;
  readonly server: string;
  /**
   * Whether the installed coai-mcp ranks by the row's RUNTIME (`--features` lists `bugzRuntime`, E2.1). Absent or
   * false: it matches the row id, so the picker offers only what that binary will accept.
   */
  readonly byRuntime?: boolean;
  /**
   * The saved pick when it no longer holds — its row unticked Bugz or gone (`bugzPick.ts`); absent or '' when it holds.
   * Drawn as what it is, chosen and disabled, so what is configured is what is shown (E5.1 step 2, the Chat tab's rule).
   */
  readonly stranded?: string;
}

/**
 * The collector's arguments: the row's runtime is said only to a binary that ranks by it — an older one refuses a flag
 * it does not know — and only with a model to rank with.
 */
export function collectArgs(model: string, runtime: string, byRuntime: boolean): readonly string[] {
  return model.length === 0 ? ['--collect-bugs'] : ['--collect-bugs', '--model', model, ...runtimeArgs(runtime, byRuntime)];
}

function runtimeArgs(runtime: string, byRuntime: boolean): readonly string[] {
  return byRuntime && runtime.length > 0 ? ['--runtime', runtime] : [];
}

/**
 * What the Collect button says right now.
 *
 * <p>Read from the PERSISTED run rather than from a flag in the page: a flag dies on reload, and the
 * durable-status rule exists because "clicked → reloaded → state lost" is precisely the failure it
 * forbids. A run that stopped reporting comes back as `interrupted`, so this never sticks.</p>
 */
export function collectLabel(run: CollectRun): string {
  if (isRunning(run)) {
    const done = run.collected + run.skipped + run.failed;

    return run.picked > 0 ? `Collecting… ${done}/${run.picked}` : 'Collecting…';
  }

  return hasRun(run) ? 'Collect again' : 'Collect';
}

/** One line saying what the last run did, or that none has happened. */
export function lastRunLine(corpus: BugCorpus): string {
  if (!corpus.read) {
    // Deliberately not the words the Team-server section uses for its own unknown state: that
    // section's test asserts the phrase is absent from the WHOLE page, to prove which of two
    // states it is showing, and a second section saying it would break that proof.
    return 'The corpus has not been read from the server yet.';
  }

  const run = corpus.lastRun;
  if (!hasRun(run)) {
    return `${corpus.funnel.unprocessed} candidate(s) waiting. Nothing has been collected yet.`;
  }

  if (isRunning(run)) {
    return `Run ${run.id} is going: ${run.collected} collected, ${run.skipped} skipped so far.`;
  }

  const ended = run.state === 'interrupted'
    // Named plainly, because it is the one ending a person may want to do something about — and
    // what it means is that nothing is known about the rest, not that the rest went badly.
    ? 'was interrupted'
    : run.state === 'failed' ? 'failed' : 'finished';

  return `Last run ${ended}: ${run.collected} collected, ${run.skipped} skipped, ${run.failed} failed`
    + ` of ${run.picked}.`;
}

const escape = (text: string): string =>
  text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

const option = (choice: ModelChoice, selected: string): string =>
  `<option value="${escape(choice.id)}"${choice.id === selected ? ' selected' : ''}>`
  + `${escape(choice.label)}</option>`;

/**
 * What the last send came to, or what is waiting to go.
 *
 * <p>Four states, and the order is the one a person reads: a send happening now, a send that ended
 * badly, a send that ended, and nothing sent from this machine yet. "Nothing yet" is deliberately
 * not silence — a section that says nothing about sending is the section this story was written
 * about.</p>
 */
export function lastSendLine(corpus: BugCorpus): string {
  const send = corpus.lastSend;
  if (sending(send)) {
    return send.offered > 0
      ? `Sending to ${send.server} — ${send.sent} of ${send.offered} so far.`
      : `Sending to ${send.server}…`;
  }

  if (send.id.length === 0) {
    return waiting(corpus) > 0
      ? 'Nothing has been sent from this machine yet.'
      : 'Nothing has been sent from this machine yet, and nothing is waiting.';
  }

  const went = `${send.sent} sent, ${send.duplicate} already held, ${send.refused} refused`;
  if (send.state === 'interrupted') {
    // NOT an ending. Nothing is known about what it would have done, and reading its counts as a
    // result would say "0 sent" for a send whose window was simply closed. (Code round, codex.)
    return `The last send stopped without finishing — ${went} before it did. Press Send to offer `
      + 'what is left.';
  }

  return send.trouble.length > 0
    ? `The last send could not finish: ${send.trouble}. ${went} before it stopped.`
    : `Last send: ${went}.`;
}

/** What the section says while no row is ticked Bugz on Models — and why the pick is a model on this machine. */
const NONE_TICKED = '<div class="hint">No model is ticked for Bugz. Tick one under Settings › Models — the ranking pass'
  + ' reads findings that are not anonymised, so it runs on a model on this machine or not at all.</div>';

/**
 * The ranking picker (todo/PLAN_one_model_catalog.md, E5.1 step 2): the rows ticked Bugz on Models (`bugzPick.ts`), a
 * stranded pick drawn as what it is, and — while nothing is ticked — the sentence that says so and where to tick one.
 * No picker at all only when there is neither a row to offer nor a pick to show.
 */
function pickerHtml(offered: readonly RankingChoice[], model: string, stranded: string): string {
  const select = offered.length === 0 && stranded.length === 0
    ? ''
    : `<select id="bugz-model" data-setting="bugzModel">${firstOption(offered, model, stranded)}${
      offered.map((m) => option(m, model)).join('')}</select>`;

  return `${select}${offered.length === 0 ? NONE_TICKED : ''}${strandedLine(stranded)}`;
}

/**
 * What the picker shows when the setting is none of its rows: the stranded pick, chosen and disabled — never the first
 * row, which a browser would show for a select with nothing selected, and which is not what is configured — or, for
 * no pick at all, a placeholder saying one is wanted.
 */
function firstOption(offered: readonly RankingChoice[], model: string, stranded: string): string {
  if (stranded.length > 0) {
    return `<option value="${escape(stranded)}" selected disabled>${escape(stranded)} — no longer ticked Bugz</option>`;
  }

  return offered.some((m) => m.id === model) ? '' : '<option value="" selected disabled>Pick a model</option>';
}

/** Why a stranded pick is shown, and what Collect does with it — refuses it by name (the collect's own sentence). */
function strandedLine(stranded: string): string {
  return stranded.length === 0
    ? ''
    : `<div class="stale">${escape(stranded)} is no longer ticked Bugz on Models, or was removed. Until you tick a model`
      + ' for Bugz under Settings › Models, or pick one here, Collect is refused by that name — it never ranks with a'
      + ' model you did not choose.</div>';
}

/** The section's body. */
export function bugzBody(state: BugzViewState = {
  corpus: EMPTY_CORPUS, models: [], model: '', server: '',
}): string {
  const run = state.corpus.lastRun;
  const running = isRunning(run);
  // THE SEND'S OWN STATE, read from the database rather than from a flag on this panel. A pair is
  // marked sent only on the server's acknowledgement, so the funnel says the same thing during a
  // send as before one — and a reloaded window that trusted it would show an idle button over a
  // live upload and let a second start. (Plan round, all three reviewers.)
  const send = state.corpus.lastSend;
  const busy = sending(send);
  // THIS server's list when it said, the panel's own when it is too old to. An installed
  // extension and an installed server can be of different ages, and only the server can say what
  // it will actually accept this minute.
  const vendors = state.corpus.rankingVendors.length > 0
    ? state.corpus.rankingVendors
    : RANKING_VENDORS;
  const offered = state.models.filter((m) => allowedBy(m, vendors, state.byRuntime === true));

  const picker = pickerHtml(offered, state.model, state.stranded ?? '');

  return `<div class="field">
  <div class="hint">${escape(lastRunLine(state.corpus))}</div>
  <label for="bugz-model">Ranking model</label>
  ${picker}
  <div class="row">
    <button type="button" class="run" data-command="collectBugs"${running ? ' disabled' : ''}>${
  escape(collectLabel(run))}</button>
    <button type="button" class="run" data-command="reviewBugs"${
  state.corpus.funnel.collected > 0 ? '' : ' disabled'}>Review bugs</button>
    <button type="button" class="run" data-command="bugsKeys">Who holds a key</button>
  </div>
  <div class="row">
    <button type="button" class="run" data-command="sendBugs"${
  busy || waiting(state.corpus) === 0 ? ' disabled' : ''}>${escape(sendLabel(send, state.corpus))}</button>
    <button type="button" class="run quiet" data-command="setBugsKey">Set the contributor key…</button>
  </div>
  <div class="hint" id="bugz-send">${escape(lastSendLine(state.corpus))}</div>
  <button type="button" class="run" data-command="setBugsServer">${
  state.server.length > 0 ? `Ingest server: ${escape(state.server)}` : 'Set the ingest server…'}</button>
  <div class="hint">Collect reads this machine's own gate findings and writes what it learned to the
  local database. Nothing leaves the machine until a pair is reviewed and sent.</div>
</div>`;
}
