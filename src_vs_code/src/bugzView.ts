import { BugCorpus, CollectRun, EMPTY_CORPUS, hasRun, isRunning } from './roundsDb';
import { ModelChoice } from './models';

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
  readonly models: readonly ModelChoice[];
  readonly model: string;
  readonly server: string;
}

/**
 * The vendors that may be shown a finding's own words.
 *
 * <p><b>It is a copy, and a test is what keeps it honest.</b> The list that DECIDES lives in
 * `CoaiMcp.Core.Collecting.RankingModels` and is enforced there, before a single finding field is
 * read. TypeScript cannot import a C# constant, so this cannot literally be derived from it — the
 * plan said 'derived' and that was not achievable; what is achievable is that the two can never
 * drift silently. `theAllowlistsAgree` reads the C# file and fails if this list differs, so adding
 * a vendor on one side without the other is a red test rather than a feature that half works.
 * (Code round, codex: 'the picker keeps a second independent copy'.)</p>
 *
 * <p>If they ever DO disagree at runtime the collector wins and the person sees its refusal, which
 * is the right way round — but a picker offering a model that always fails is a bug in this file.</p>
 *
 * <p>Why so narrow: a finding's `title`, `why` and `fix` are the reviewers' prose about somebody's
 * code and are <b>not</b> anonymised. The normaliser runs later and only on source, so the ranking
 * pass is the one step here that handles un-anonymised text.</p>
 */
export const RANKING_VENDORS: readonly string[] = ['local'];

/** Whether this model may be offered, against a given list of vendors. */
const allowedBy = (model: string, vendors: readonly string[]): boolean =>
  model.length === 0 || vendors.includes(model.split('/')[0]?.toLowerCase() ?? '');

/** Whether this model may be offered at all, by the panel's own fallback list. */
export const mayRank = (model: string): boolean => allowedBy(model, RANKING_VENDORS);

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

/** The section's body. */
export function bugzBody(state: BugzViewState = {
  corpus: EMPTY_CORPUS, models: [], model: '', server: '',
}): string {
  const run = state.corpus.lastRun;
  const running = isRunning(run);
  // THIS server's list when it said, the panel's own when it is too old to. An installed
  // extension and an installed server can be of different ages, and only the server can say what
  // it will actually accept this minute.
  const vendors = state.corpus.rankingVendors.length > 0
    ? state.corpus.rankingVendors
    : RANKING_VENDORS;
  const offered = state.models.filter((m) => allowedBy(m.id, vendors));

  const picker = offered.length === 0
    ? '<div class="hint">No local engine was found. The ranking pass reads findings that are not'
      + ' anonymised, so it runs on this machine or not at all.</div>'
    : `<select id="bugz-model" data-setting="bugzModel">${
      offered.map((m) => option(m, state.model)).join('')}</select>`;

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
  <button type="button" class="run" data-command="setBugsServer">${
  state.server.length > 0 ? `Ingest server: ${escape(state.server)}` : 'Set the ingest server…'}</button>
  <div class="hint">Collect reads this machine's own gate findings and writes what it learned to the
  local database. Nothing leaves the machine until a pair is reviewed and sent.</div>
</div>`;
}
