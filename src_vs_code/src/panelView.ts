import { CoaiSettings } from './settingsShape';
import { Escalation } from './escalations';
import { HELP, HelpKey } from './help';
import { ModelChoice, modelsFor, modelsProvenance } from './models';
import { ROLES, promptsFor, selectedFor } from './prompts';
import { barWidth, estimated, money, shortDuration, shortNumber, totalsByVendor, UsageEntry, Window, WINDOWS, within } from './usage';
import { costPhrase, elapsed, isRunning, reviewerLines, RoundRecord, SessionFile, stageName } from './rounds';
import { CliStatus, cliStatusNote, updateAvailable, UNKNOWN_CLI } from './cliVersions';
import { Vendor } from './vendors';

/**
 * The panel's HTML, as a pure function of what it shows.
 *
 * <p>Pure and `vscode`-free so the markup is a test rather than something checked by opening a
 * sidebar. Three rules the tests hold: every colour comes from a theme variable, every value that
 * came from a file or a person is escaped — a question is DATA, never markup — and nothing can
 * make the view scroll sideways.</p>
 *
 * <p><b>Labels sit above their controls, never beside them.</b> A sidebar is narrow and its width
 * is the person's choice; a label-and-field row collides the moment they drag it in, and then the
 * whole view scrolls sideways. Stacked fields cannot do that.</p>
 *
 * <p><b>One section per job, and most of them closed.</b> Almost everything here is configured
 * once and never touched again; a wall of it hides the two things that do change — who reviews,
 * and what came back.</p>
 */

export interface PanelState {
  readonly settings: CoaiSettings;
  readonly vendors: readonly Vendor[];
  readonly codexModels: readonly ModelChoice[];
  readonly serverInstalled: boolean;
  readonly serverVersion: string;
  readonly questions: readonly Escalation[];
  readonly sessions: readonly SessionFile[];
  /** Which collapsible sections are open. Empty falls back to {@link OPEN_BY_DEFAULT}. */
  readonly openSections: readonly string[];
  /** Every reviewer run the server has recorded, newest last. */
  readonly usage: readonly UsageEntry[];
  /** Which window the spending chart is showing. */
  readonly usageWindow: Window;
  /** The newest published server version, or empty while it is unknown or unreachable. */
  readonly latestServerVersion: string;
  /**
   * Each vendor's installed and published CLI version, by vendor id.
   *
   * <p>Absent, or both fields empty, is a legitimate answer — an offline machine, a CLI that is not
   * installed, a vendor this build has no official version source for — and it renders as a grey
   * button rather than as an error.</p>
   */
  readonly cliStatus: Readonly<Record<string, CliStatus>>;
}

/**
 * Nothing is open before anyone touches anything.
 *
 * <p>The panel opens as a list of headings that fits in one glance, and you expand what you came
 * for. Two sections open was still a wall on a narrow sidebar — and this is a panel you configure
 * once and afterwards only visit when something is waiting on you.</p>
 */
export const OPEN_BY_DEFAULT: readonly string[] = [];

export function panelHtml(state: PanelState, nonce: string): string {
  const open = state.openSections.length === 0 ? OPEN_BY_DEFAULT : state.openSections;
  const body = [
    `<div id="live-questions">${questionsSection(state.questions)}</div>`,
    section('reviewers', 'Reviewers', open, reviewersBody(state)),
    section('prompts', 'Prompts per round', open, promptsBody(state)),
    section('gate', 'The gate', open, gateBody(state.settings)),
    section('limits', 'Limits', open, limitsBody(state.settings)),
    section('keys', 'Vendor keys', open, keysBody(state)),
    section('server', 'Server', open, serverBody(state)),
    section('usage', 'What each AI has used', open, usageBody(state)),
    section('rounds', 'Recent rounds', open, `<div id="live-rounds">${roundsBody(state.sessions)}</div>`),
  ].join('\n');

  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<style>${CSS}</style>
</head>
<body>
${body}
<script nonce="${nonce}">
  const vscode = acquireVsCodeApi();
  for (const el of document.querySelectorAll('[data-setting]')) {
    el.addEventListener('change', () => {
      const value = el.type === 'checkbox' ? el.checked : el.type === 'number' ? Number(el.value) : el.value;
      if (value === '__other__') {
        // Not a model — a request to type one; the input box comes from the provider side.
        vscode.postMessage({ type: 'command', command: 'customModel', id: el.dataset.vendor });
        return;
      }
      vscode.postMessage({ type: 'setting', key: el.dataset.setting, value,
                           vendor: el.dataset.vendor, role: el.dataset.role });
    });
  }
  for (const el of document.querySelectorAll('[data-prompt]')) {
    el.addEventListener('change', () =>
      vscode.postMessage({ type: 'prompt', role: el.dataset.prompt, round: Number(el.dataset.round), value: el.value }));
  }
  for (const el of document.querySelectorAll('.section')) {
    el.addEventListener('toggle', () =>
      vscode.postMessage({ type: 'section', id: el.dataset.section, open: el.open }));
  }
  for (const el of document.querySelectorAll('[data-command]')) {
    el.addEventListener('click', () =>
      vscode.postMessage({ type: 'command', command: el.dataset.command, id: el.dataset.id }));
  }

  // Live updates arrive as HTML for the two regions that change on their own — the round in
  // flight, and any open escalation. Patching them leaves every other control ALONE,
  // which is the whole point: assigning the panel's html reloads the webview, and a reload
  // closes any open dropdown. This is what stopped the pickers snapping shut mid-choice.
  window.addEventListener('message', (event) => {
    const message = event.data;
    if (message?.type !== 'live') {
      return;
    }
    const questions = document.getElementById('live-questions');
    const rounds = document.getElementById('live-rounds');
    const usage = document.getElementById('live-usage');
    if (questions !== null) {
      questions.innerHTML = message.questions;
    }
    if (rounds !== null) {
      rounds.innerHTML = message.rounds;
    }
    if (usage !== null) {
      usage.innerHTML = message.usage;
    }
    // The answer buttons live inside the patched HTML, so they are re-bound here.
    for (const el of document.querySelectorAll('#live-questions [data-command]')) {
      el.addEventListener('click', () =>
        vscode.postMessage({ type: 'command', command: el.dataset.command, id: el.dataset.id }));
    }
  });
</script>
</body>
</html>`;
}

/**
 * One collapsible section.
 *
 * <p><code>&lt;details&gt;</code> rather than a scripted accordion: keyboard-operable and
 * screen-reader-correct for free, and its arrow cannot get out of step with its own state. The
 * open set is carried in {@link PanelState} because the panel repaints on every change — a
 * section that snapped shut while somebody was typing in it would be worse than no collapsing.</p>
 */
function section(id: string, title: string, open: readonly string[], body: string): string {
  return `<details class="section sec-${id}" data-section="${id}"${open.includes(id) ? ' open' : ''}>
  <summary>${escapeHtml(title)}</summary>
${body}
</details>`;
}

/**
 * What a screen reader says, which cannot be a colour.
 *
 * <p>The green is the fast signal and it is never the only one: the label and the tooltip both
 * carry the same fact in words.</p>
 */
function updateLabel(id: string, cli: CliStatus): string {
  return updateAvailable(cli.installed, cli.latest)
    ? `Update the ${id} CLI to ${cli.latest}`
    : `The ${id} CLI is up to date`;
}

function reviewersBody(state: PanelState): string {
  return `${state.vendors.map((v) => vendorCard(v, state.codexModels, state.cliStatus[v.id] ?? UNKNOWN_CLI)).join('\n')}
<button class="add" data-command="addVendor" title="${escapeHtml(HELP.addVendor)}">＋&nbsp; Add a reviewer</button>`;
}

function vendorCard(vendor: Vendor, codexModels: readonly ModelChoice[], cli: CliStatus): string {
  const id = escapeHtml(vendor.id);
  const models = modelsFor(vendor.runtime, codexModels, vendor.model);
  const endpoint =
    vendor.baseUrl.length === 0
      ? ''
      : `
  <div class="field">
    <input type="url" data-setting="baseUrl" data-vendor="${id}" title="${escapeHtml(HELP.vendorBaseUrl)}"
           value="${escapeHtml(vendor.baseUrl)}">
    <div class="hint">Its OpenAI-compatible endpoint. The key for it lives in the vault entry under <code>${id}</code>.</div>
  </div>`;

  // Shown for EVERY vendor, not only a custom endpoint. PATH is not always able to answer: in WSL
  // `codex` and `gemini` resolve to the WINDOWS npm shims through the interop PATH and die on a
  // missing Linux binary, and until this field existed nothing could point at the native one.
  const executable = `
  <div class="field">
    <input type="text" data-setting="executablePath" data-vendor="${id}" title="${escapeHtml(HELP.vendorExecutablePath)}"
           placeholder="CLI path — empty means look it up on PATH" value="${escapeHtml(vendor.executablePath)}">
  </div>
  <div class="field inline">
    ${labelled(`price-in-${id}`, '$ / 1M in', 'vendorPrice')}
    <input type="number" id="price-in-${id}" min="0" step="0.01" data-setting="pricePerMillionIn" data-vendor="${id}"
           value="${vendor.pricePerMillionIn === 0 ? '' : vendor.pricePerMillionIn}" placeholder="—">
  </div>
  <div class="field inline">
    ${labelled(`price-out-${id}`, '$ / 1M out', 'vendorPrice')}
    <input type="number" id="price-out-${id}" min="0" step="0.01" data-setting="pricePerMillionOut" data-vendor="${id}"
           value="${vendor.pricePerMillionOut === 0 ? '' : vendor.pricePerMillionOut}" placeholder="—">
  </div>`;

  return `<div class="vendor">
  <div class="head">
    <input type="checkbox" id="v-${id}" data-setting="enabled" data-vendor="${id}"${vendor.enabled ? ' checked' : ''}
           title="${escapeHtml(HELP.vendorEnabled)}">
    <label class="name" for="v-${id}">${id}</label>
    <button class="run" data-command="runVendor" data-id="${id}" title="${escapeHtml(HELP.runVendor)}"
            aria-label="Open ${id} in a terminal">▶</button>
    <button class="run get" data-command="installVendorCli" data-id="${id}" title="${escapeHtml(HELP.installVendorCli)}"
            aria-label="Install the ${id} CLI">⤓</button>
    <button class="run upd${updateAvailable(cli.installed, cli.latest) ? ' has-update' : ''}"
            data-command="updateVendorCli" data-id="${id}" title="${escapeHtml(cliStatusNote(vendor.id, cli))}"
            aria-label="${escapeHtml(updateLabel(vendor.id, cli))}">⟳</button>
    <button class="link" data-command="removeVendor" data-id="${id}">remove</button>
  </div>
  <div class="field">
    <select data-setting="model" data-vendor="${id}" title="${escapeHtml(HELP.vendorModel)}">
      ${modelOptions(models, vendor.model)}
    </select>
    <div class="hint">${escapeHtml(vendor.runtime)} · ${escapeHtml(modelsProvenance(vendor.runtime, codexModels))}</div>
  </div>${endpoint}${executable}
</div>`;
}


function gateBody(s: CoaiSettings): string {
  // Rounds and threshold moved INTO each role's box, beside that role's prompts: they were two
  // sections describing one thing. What is left here is the one decision that belongs to neither
  // role nor stage \u2014 what to do when the rounds run out.
  return `<div class="field">
  ${labelled('onExhausted', 'When the rounds run out', 'onExhausted')}
  <select id="onExhausted" data-setting="onExhausted">
    <option value="human"${s.onExhausted === 'human' ? ' selected' : ''}>Ask a human</option>
    <option value="continue"${s.onExhausted === 'continue' ? ' selected' : ''}>Continue, and say so</option>
    <option value="good_enough"${s.onExhausted === 'good_enough' ? ' selected' : ''}>Good enough \u2014 take what\u2019s true and move on</option>
    <option value="escalate"${s.onExhausted === 'escalate' ? ' selected' : ''}>Climb the ladder</option>
  </select>
</div>`;
}

function limitsBody(s: CoaiSettings): string {
  return `<div class="field inline">
  ${labelled('maxConcurrency', 'Reviewers at once', 'maxConcurrency')}
  <input type="number" id="maxConcurrency" min="1" data-setting="maxConcurrency" value="${s.maxConcurrency}">
</div>
<div class="field inline">
  ${labelled('maxPerProvider', 'Per vendor', 'maxPerProvider')}
  <input type="number" id="maxPerProvider" min="1" data-setting="maxPerProvider" value="${s.maxPerProvider}">
</div>
<div class="field inline">
  ${labelled('reviewerTimeoutMinutes', 'Reviewer timeout, minutes', 'reviewerTimeout')}
  <input type="number" id="reviewerTimeoutMinutes" min="1" data-setting="reviewerTimeoutMinutes" value="${s.reviewerTimeoutMinutes}">
</div>
<div class="field inline">
  ${labelled('escalationMinutes', 'Wait for you, minutes', 'escalationMinutes')}
  <input type="number" id="escalationMinutes" min="1" data-setting="escalationMinutes" value="${s.escalationMinutes}">
</div>`;
}

/**
 * The keys, and — first — whether they are needed at all.
 *
 * <p>Somebody reading "CredsForDevs config key" with codex and gemini configured has no way to
 * know the answer is "not yet". A field that cannot say whether it applies to you is a field that
 * gets filled in wrongly, so this one says it.</p>
 */
function keysBody(state: PanelState): string {
  const needy = state.vendors.filter((v) => v.enabled && v.baseUrl.length > 0);
  const field = `<div class="field">
  ${labelled('credsKey', 'CredsForDevs config key', 'credsKey')}
  <input type="text" id="credsKey" data-setting="credsKey" value="${escapeHtml(state.settings.credsKey)}"
         placeholder="${needy.length === 0 ? 'not needed yet' : 'the key from Enable Code Access…'}">
</div>`;

  if (needy.length === 0) {
    return `<div class="hint"><b>Nothing to fill in yet.</b> Every reviewer you have signs in through its own CLI, so none of them needs an API key. This becomes necessary when you add a vendor that has no CLI of its own — DeepSeek, OpenRouter, any endpoint you give a URL to.</div>
${field}`;
  }

  const names = needy.map((v) => escapeHtml(v.id)).join(', ');
  const one = needy.length === 1;
  return `<div class="hint">${names} ${one ? 'reaches an endpoint of its own, so it needs' : 'reach endpoints of their own, so they need'} an API key. Put the keys in ONE CredsForDevs entry of kind <code>config</code> — a JSON object keyed by vendor name — turn on <i>Enable Code Access…</i> for it, and paste the key it mints here.</div>
${field}`;
}

/**
 * What is installed, what is published, and a button when those differ.
 *
 * <p>The published version is shown even when it MATCHES, because "you are up to date" and "the
 * check never ran" look identical when only a mismatch is displayed — and this check silently
 * never ran at all for weeks, asking GitHub for the newest release of any kind and being handed
 * an extension tag.</p>
 */
function serverBody(state: PanelState): string {
  const installed = state.serverInstalled
    ? `coai-mcp ${escapeHtml(state.serverVersion)} is installed.`
    : 'coai-mcp is not installed yet.';

  const published = state.latestServerVersion.length === 0
    ? '<div class="hint">The published version could not be read from GitHub just now.</div>'
    : state.serverInstalled && state.latestServerVersion === state.serverVersion
      ? `<div class="hint">${escapeHtml(state.latestServerVersion)} is the newest published — you are up to date.</div>`
      : `<div class="hint">${escapeHtml(state.latestServerVersion)} is published.</div>
<button class="add" data-command="installServer">⬇&nbsp; ${state.serverInstalled ? 'Update' : 'Install'} coai-mcp ${escapeHtml(state.latestServerVersion)}</button>`;

  return `<div class="status">${installed}</div>
${published}
<div class="hint">Changes here are saved for the server straight away; it reads them when your MCP client next starts it. The config block in the ⋯ menu is pasted once, when you first set it up.</div>
<button class="link" data-command="checkForUpdate">Check again</button>`;
}

function questionsSection(questions: readonly Escalation[]): string {
  if (questions.length === 0) {
    return '';
  }
  const cards = questions
    .map((q) => {
      const findings = q.openFindings
        .map(
          (f) =>
            `<div class="finding">• ${escapeHtml(f.severity)} ${escapeHtml(f.category)} — ${escapeHtml(f.title)}</div>`,
        )
        .join('\n      ');
      return `<div class="question">
      <div>${escapeHtml(q.question)}</div>
      ${findings}
      <div class="meta">${escapeHtml(q.branch)}${q.translationNote ? ` · shown untranslated: ${escapeHtml(q.translationNote)}` : ''}</div>
      <button data-command="answer" data-id="${escapeHtml(q.id)}">Answer…</button>
    </div>`;
    })
    .join('\n');
  // Never collapsible: a blocked round is the one thing that must not be tidied away.
  return `<h2>A review is waiting on you</h2>\n${cards}`;
}

/**
 * What has run — and what is running RIGHT NOW, which is the question people actually have
 * during a ten-minute code gate.
 *
 * <p>A round in flight sorts first and lists its reviewers one by one, because "four of six
 * answered, two running" is the difference between waiting and being stuck. Until the server
 * began writing the round at its START, this panel could not tell those apart at all.</p>
 */
export 
/**
 * Which prompt each role uses on each round, beside that role’s rounds and threshold.
 *
 * <p>One row per round because that is the unit a person actually reasons about — "the second
 * round should look for something else" — and a single prompt per role could not express it.
 * The universal prompt is the default everywhere; a narrow lens is always a deliberate pick.</p>
 */
/** Which tone wraps each role. The colour is never the only signal — the name is always written. */
const ROLE_TONE: Record<string, string> = {
  PlanCritique: 'plan',
  Architecture: 'arch',
  SecurityReliability: 'sec',
  UxDxPerformance: 'uxdx',
};

/**
 * The fan-out, multiplied out loud.
 *
 * <p>"three reviewers per vendor, every round" made a reader ask whether each reviewer runs six
 * times. It does not: six is the number of REVIEWERS in a round — vendors times roles — each run
 * once. What looks like six runs is the panel itself: three roles, each with a picker per round.
 * So the sentence does the arithmetic in the numbers actually configured, and a disabled vendor is
 * not counted, because a reviewer that will not run is not one.</p>
 */
function fanOut(state: PanelState): string {
  const vendors = state.vendors.filter((v) => v.enabled).length;
  const reviewers = vendors * 3;
  // Derived, not stored: it is the widest CODE role's budget, and a stored copy would be a
  // second source of truth for a number that already exists.
  const rounds = Math.max(
    1,
    ...['Architecture', 'SecurityReliability', 'UxDxPerformance'].map((r) => state.settings.rounds[r] ?? 2),
  );
  return (
    `${vendors} vendor${vendors === 1 ? '' : 's'} × 3 roles = ${reviewers} reviewer${reviewers === 1 ? '' : 's'} ` +
    `per round, each runs once per round, up to ${rounds} round${rounds === 1 ? '' : 's'}`
  );
}

function promptsBody(state: PanelState): string {
  const s = state.settings;
  const roleRow = (role: (typeof ROLES)[number]): string => {
    // A role's own budget decides how many rounds it shows: a picker for a round that role will
    // never reach is a control that cannot do anything, which the spending tabs already taught.
    const budget = Math.max(1, Math.min(s.rounds[role.id] ?? 2, 6));
    const pickers = Array.from({ length: budget }, (_, i) => {
      const round = i + 1;
      const current = selectedFor(role.id, round, s.promptsPerRound);
      const options = promptsFor(role.id)
        .map(
          (p) =>
            `<option value="${escapeHtml(p.id)}"${p.id === current ? ' selected' : ''} title="${escapeHtml(p.purpose)}">${escapeHtml(p.label)}</option>`,
        )
        .join('');
      return `  <div class="field">
    <label for="pr-${escapeHtml(role.id)}-${round}">Round ${round}</label>
    <select id="pr-${escapeHtml(role.id)}-${round}" data-prompt="${escapeHtml(role.id)}" data-round="${round}">${options}</select>
  </div>`;
    }).join('\n');

    // The gate and the prompts were two sections describing one thing: how many times this role
    // asks, how much it may still find, and what it asks each time. One box now.
    return `<div class="role role-${ROLE_TONE[role.id] ?? 'plan'}">
  <div class="head"><span class="name">${escapeHtml(role.label)}</span></div>
  <div class="field inline">
    ${labelled(`rounds-${role.id}`, 'Rounds', 'maxRounds')}
    <input type="number" id="rounds-${role.id}" min="1" max="6" data-setting="rounds" data-role="${role.id}"
           value="${s.rounds[role.id] ?? 2}">
  </div>
  <div class="field inline">
    ${labelled(`threshold-${role.id}`, 'Passes at or under', 'gateThreshold')}
    <input type="number" id="threshold-${role.id}" min="0" data-setting="thresholds" data-role="${role.id}"
           value="${s.thresholds[role.id] ?? 3}">
  </div>
${pickers}
</div>`;
  };

  const plan = ROLES.filter((r) => r.stage === 'plan').map(roleRow).join('\n');
  const code = ROLES.filter((r) => r.stage !== 'plan').map(roleRow).join('\n');

  return `<div class="role-group">
  <div class="group-head">Plan stage</div>
  <div class="field">
    <label class="check"><input type="checkbox" data-setting="dealPlanLenses"${s.dealPlanLenses ? ' checked' : ''}> Deal the lenses across vendors</label>
    <div class="hint">Off: every vendor answers the same question, and two vendors agreeing on a finding is a fact the gate can use. On: every lens gets asked once instead, at half the launches \u2014 and that agreement is gone. Anything you pick below wins either way.</div>
  </div>
${plan}
</div>
<div class="role-group">
  <div class="group-head">Code stage \u2014 ${fanOut(state)}</div>
  <div class="field">
    <label class="check"><input type="checkbox" data-setting="dealCodeLenses"${s.dealCodeLenses ? ' checked' : ''}> Deal the roles across vendors</label>
    <div class="hint">Off: each of the three roles is asked of every vendor. On: the three roles are dealt out, one vendor each.</div>
  </div>
  <div class="hint">Round 1 defaults to <b>Conventions</b>: it judges the diff against the rules this project has written down \u2014 <code>CLAUDE.md</code>, <code>AGENTS.md</code>, <code>GEMINI.md</code>, <code>.claude/rules</code> \u2014 and nothing else. Pick something else for round 1 and that wins.</div>
${code}
</div>`;
}

/**
 * What each vendor has consumed, over a window a person picks.
 *
 * <p>Bars rather than a plotted chart: the comparison that matters is BETWEEN vendors in one
 * window, not a shape over time, and a bar made of a div needs no library, no canvas and no
 * network — three things a webview under a strict content policy cannot have anyway.</p>
 *
 * <p>Money is a dash where a vendor does not price its own runs. Rendering that as $0.00 would
 * read as "free", and free and unreported are different facts.</p>
 */
function usageBody(state: PanelState): string {
  const tabs = WINDOWS.map(
    (w) =>
      `<button class="tab${w.id === state.usageWindow ? ' on' : ''}" data-command="usageWindow" data-id="${w.id}">${escapeHtml(w.label)}</button>`,
  ).join('');

  // The tabs sit OUTSIDE the patched region on purpose: a button inside one loses its click
  // listener the next time the region is replaced, and the listener IS the button.
  return `<div class="tabs">${tabs}</div>
<div id="live-usage">${usageRows(state)}</div>`;
}

/**
 * The spending itself, which advances while a round runs, so it travels as a patch rather than a
 * repaint: a repaint reloads the webview, and a reload closes whatever dropdown was open.
 */
export /**
 * What one vendor cost: what it billed, or what its rates say, or nothing.
 *
 * <p>Never both. A reported cost is the fact and an estimate beside it is noise; an estimate with
 * no rate behind it is a guess dressed as a number, so that stays a dash. The tilde is load-bearing
 * — it is the difference between what somebody charged and what we worked out.</p>
 */
function spend(row: { costUsd: number | null; estimatedUsd: number | null }): string {
  return row.costUsd !== null
    ? money(row.costUsd)
    : row.estimatedUsd !== null
      ? estimated(row.estimatedUsd)
      : '—';
}

/** The total, saying which half is billed and which is worked out. */
function total(billed: number | null, guessed: number | null): string {
  if (billed === null && guessed === null) {
    return '—';
  }
  if (guessed === null) {
    return money(billed);
  }

  return billed === null ? estimated(guessed) : `${money(billed)} + ${estimated(guessed)}`;
}

function usageRows(state: PanelState): string {
  const rows = totalsByVendor(within(state.usage, state.usageWindow, new Date()), state.vendors);
  if (rows.length === 0) {
    return '<div class="empty">Nothing recorded in this window yet.</div>';
  }

  const busiest = Math.max(...rows.map((r) => r.tokensIn + r.tokensOut));
  const cards = rows
    .map((r) => {
      const total = r.tokensIn + r.tokensOut;
      const failed = r.failed === 0 ? '' : ` · <span class="warn">${r.failed} failed</span>`;
      return `<div class="spend">
  <div class="head"><span class="name">${escapeHtml(r.provider)}</span><span class="cost">${spend(r)}</span></div>
  <div class="bar"><span style="width:${barWidth(total, busiest)}%"></span></div>
  <div class="figures">${shortNumber(r.tokensIn)} in · ${shortNumber(r.tokensOut)} out · ${r.runs} run(s)${failed}</div>
  <div class="hint">${shortDuration(r.seconds)} total · ${shortDuration(r.averageSeconds)} average</div>
</div>`;
    })
    .join('\n');

  const all = rows.reduce(
    (t, r) => ({
      tokens: t.tokens + r.tokensIn + r.tokensOut,
      cost: r.costUsd === null ? t.cost : (t.cost ?? 0) + r.costUsd,
      // Kept apart from `cost` all the way to the end: a total that mixes what a vendor billed with
      // what we worked out from a rate somebody typed is a number nobody can check.
      guess: r.estimatedUsd === null ? t.guess : (t.guess ?? 0) + r.estimatedUsd,
      seconds: t.seconds + r.seconds,
    }),
    { tokens: 0, cost: null as number | null, guess: null as number | null, seconds: 0 },
  );

  return `${cards}
<div class="hint total">All vendors: ${shortNumber(all.tokens)} tokens · ${total(all.cost, all.guess)} · ${shortDuration(all.seconds)}</div>`;
}

function roundsBody(sessions: readonly SessionFile[], nowMs: number = Date.now()): string {
  const rounds = sessions
    .flatMap((s) => s.rounds.map((r) => ({ branch: s.state.branch, ...r })))
    .sort((a, b) => Number(isRunning(b)) - Number(isRunning(a)) || b.completedUtc.localeCompare(a.completedUtc))
    .slice(0, 6);
  if (rounds.length === 0) {
    return '<div class="empty">No rounds yet.</div>';
  }
  return rounds.map((r) => roundCard(r, nowMs)).join('\n');
}

function roundCard(round: RoundRecord & { branch: string }, nowMs: number): string {
  const verdict = isRunning(round)
    ? '<span class="badge running">running</span>'
    : round.status === 'interrupted'
      ? '<span class="badge stopped">interrupted</span>'
      : `<b>${escapeHtml(round.verdict)}</b>`;
  const reviewers = isRunning(round)
    ? reviewerLines(round)
        .map((line) => `<div class="reviewer">${escapeHtml(line)}</div>`)
        .join('\n')
    : '';
  const took = elapsed(round, nowMs);
  // WHAT was reviewed leads the line; the branch and the round number follow it. A list of rounds
  // identified only by stage and number is a column of numbers — a person scanning it is looking
  // for the plan they remember, not for round four.
  const subject = (round.subject ?? '').length > 0
    ? `<div class="subject">${escapeHtml(round.subject!)}</div>`
    : '';
  return `${subject}<div class="verdict">${escapeHtml(stageName(round.stage))} ${round.number} · ${escapeHtml(round.branch)} · ${verdict} · ${round.gatingCount} gating</div>
<div class="usage">${took.length > 0 ? `${escapeHtml(took)} · ` : ''}${escapeHtml(costPhrase(round))}</div>
${reviewers}`;
}

/**
 * The two regions the provider may patch without reloading the webview — the round in flight and
 * a waiting question. Everything else on the panel is a control, and a control only changes when
 * the person changes it.
 */
export function liveRegions(state: PanelState, nowMs: number = Date.now()): { questions: string; rounds: string; usage: string } {
  return {
    usage: usageRows(state), questions: questionsSection(state.questions), rounds: roundsBody(state.sessions, nowMs) };
}

/**
 * A model list as SELECT options: the CLI's default first, every known model, the saved value
 * kept even when unknown, and "another model…" as the way out of the list.
 *
 * <p>A select rather than a datalist, learned the hard way: a datalist FILTERS its options by the
 * field's current value, so the moment a model was chosen every other option vanished and the
 * picker read as broken.</p>
 */
function modelOptions(models: readonly ModelChoice[], current: string): string {
  const known = models
    .map((m) => `<option value="${escapeHtml(m.id)}"${m.id === current ? ' selected' : ''}>${escapeHtml(m.label)}</option>`)
    .join('\n      ');
  return `<option value=""${current === '' ? ' selected' : ''}>the CLI's default</option>
      ${known}
      <option value="__other__">another model…</option>`;
}

/**
 * The little "?" that explains a setting on hover.
 *
 * <p>A native `title` rather than a scripted popup: it works with the keyboard, it cannot escape
 * the webview, and it needs no state of its own.</p>
 */
export function help(key: HelpKey): string {
  return `<span class="help" title="${escapeHtml(HELP[key])}" role="img" aria-label="What this means">?</span>`;
}

/** A label with its explanation beside it. */
function labelled(forId: string, text: string, key: HelpKey): string {
  return `<label for="${forId}">${help(key)}${escapeHtml(text)}</label>`;
}

/** Every value here came from a file or a person; none of it may become markup. */
export function escapeHtml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

const CSS = `
  /* Everything measures its own border and padding: without this a 100% field plus padding is
     wider than its parent, and the whole view gains a horizontal scrollbar. */
  *, *::before, *::after { box-sizing: border-box; }
  :root { color-scheme: light dark; }
  body {
    font-family: var(--vscode-font-family); font-size: var(--vscode-font-size);
    color: var(--vscode-foreground); background: transparent;
    /* Air down both sides: nothing should touch the edge of the view, and the right-hand gap is
       a shade wider so a field never sits under the scrollbar's track. */
    margin: 0; padding: 4px 14px 20px 12px; overflow-x: hidden;
  }
  h2 {
    font-size: 11px; text-transform: uppercase; letter-spacing: .06em; opacity: .75;
    margin: 10px 0 6px; font-weight: 600;
  }
  .section { border-top: 1px solid var(--vscode-panel-border); padding: 0 0 8px; }
  .section > summary {
    /* .9 rather than .75: coloured text at .75 on a dark ground is muddy, and the colour is
       doing the separating now. */
    font-size: 11px; text-transform: uppercase; letter-spacing: .06em; opacity: .9;
    font-weight: 600; padding: 10px 0 6px; cursor: pointer; list-style: none;
    display: flex; align-items: center; gap: 6px; user-select: none;
  }
  .section > summary:hover { opacity: 1; }
  /* Each header that opens carries its own tone, from the same palette the role boxes use, so a
     column of eight identical grey words becomes something you can aim at. The chevron follows for
     free: it is drawn from currentColor.

     Colour is never the only signal — every heading is also its own word — and each tone is a
     --vscode-charts-* token with a hex fallback, so a theme that redefines the charts palette
     moves these with it. */
  .sec-reviewers > summary { color: var(--tone-arch); }
  .sec-prompts   > summary { color: var(--tone-plan); }
  .sec-gate      > summary { color: var(--tone-sec); }
  .sec-limits    > summary { color: var(--tone-limits); }
  .sec-keys      > summary { color: var(--tone-keys); }
  .sec-server    > summary { color: var(--tone-uxdx); }
  .sec-usage     > summary { color: var(--tone-arch); }
  .sec-rounds    > summary { color: var(--tone-plan); }
  .section > summary::-webkit-details-marker { display: none; }
  /* A real chevron, drawn from two borders rather than borrowed from punctuation: it matches the
     Explorer's weight, scales with the text, and points the right way in both states with no
     script. (A '›' glyph rendered a third of this size — a disclosure arrow nobody can hit.) */
  .section > summary::before {
    content: ''; flex: 0 0 auto; width: 6px; height: 6px; margin: 0 4px 0 2px;
    border-right: 1.5px solid currentColor; border-bottom: 1.5px solid currentColor;
    transform: rotate(-45deg); transition: transform .12s ease;
  }
  .section[open] > summary::before { transform: rotate(45deg); margin-top: -3px; }
  .field { margin: 8px 0; }
  .field > label { display: block; margin-bottom: 3px; }
  .inline { display: flex; align-items: center; justify-content: space-between; gap: 10px; }
  .inline > label { flex: 1 1 auto; min-width: 0; margin-bottom: 0; }
  .inline > input[type="number"] { flex: 0 0 64px; width: 64px; }
  .hint { opacity: .65; font-size: 11px; margin: 3px 0 0; line-height: 1.45; }
  input[type="text"], input[type="url"], input[type="number"], select {
    background: var(--vscode-input-background); color: var(--vscode-input-foreground);
    border: 1px solid var(--vscode-input-border, transparent); border-radius: 2px;
    padding: 3px 6px; font-family: inherit; font-size: inherit;
    width: 100%; max-width: 100%; min-width: 0;
  }
  button {
    background: var(--vscode-button-background); color: var(--vscode-button-foreground);
    border: none; border-radius: 2px; padding: 5px 10px; cursor: pointer;
    font-family: inherit; font-size: inherit; width: 100%; margin: 6px 0 0;
  }
  button:hover { background: var(--vscode-button-hoverBackground); }
  button.add { margin-top: 10px; padding: 7px 10px; font-weight: 600; }
  button.link {
    background: none; color: var(--vscode-textLink-foreground); padding: 0; width: auto;
    margin: 0; text-decoration: underline; font-size: 11px;
  }
  button.link:hover { background: none; color: var(--vscode-textLink-activeForeground); }
  .help {
    display: inline-block; width: 14px; height: 14px; line-height: 14px; margin-right: 6px;
    text-align: center; border-radius: 50%; font-size: 10px; font-weight: 700; cursor: help;
    background: var(--vscode-badge-background); color: var(--vscode-badge-foreground);
    opacity: .8; flex: 0 0 auto;
  }
  .help:hover { opacity: 1; }
  .vendor {
    border: 1px solid var(--vscode-panel-border);
    border-radius: 3px; padding: 8px; margin: 8px 0;
  }
  .vendor .head { display: flex; align-items: center; gap: 6px; }
  .vendor .name { font-weight: 600; flex: 0 1 auto; min-width: 0; overflow: hidden; text-overflow: ellipsis; }
  .vendor input[type="checkbox"] { flex: 0 0 auto; margin: 0; }
  /* The play button sits between the name and remove, centred in the gap they leave. */
  /* The install button sits beside ▶ and is deliberately quieter: it is the thing you press
     once, on a machine that does not have the CLI yet. */
  .vendor .head .get { font-size: 11px; }
  /* The update button says, by its colour, whether there is anything to do — which is the question
     somebody actually has, and the one they used to answer by leaving the panel. Grey is the
     resting state AND the "could not tell" state: a button that lights up because a fetch failed
     would be worse than one that never lights up. */
  .vendor .head .upd { font-size: 12px; color: var(--vscode-descriptionForeground); }
  .vendor .head .upd.has-update { color: var(--vscode-charts-green); font-weight: 600; }
  .vendor .head .upd:hover { border-color: var(--vscode-descriptionForeground); }
  .vendor .head .upd.has-update:hover { border-color: var(--vscode-charts-green); }
  .vendor .head .run {
    flex: 0 0 auto; width: auto; margin: 0 auto; padding: 1px 8px; line-height: 1.2;
    background: none; color: var(--vscode-charts-green); font-size: 13px;
    border: 1px solid transparent; border-radius: 3px;
  }
  .vendor .head .run:hover {
    background: var(--vscode-toolbar-hoverBackground); border-color: var(--vscode-charts-green);
  }
  .question {
    border-left: 3px solid var(--vscode-inputValidation-warningBorder);
    background: var(--vscode-inputValidation-warningBackground);
    padding: 8px 10px; margin: 8px 0; border-radius: 0 3px 3px 0;
  }
  .question .meta { opacity: .7; font-size: 11px; margin-top: 4px; }
  .finding { font-size: 11px; opacity: .85; margin: 3px 0 0 8px; }
  .verdict {
    font-family: var(--vscode-editor-font-family); font-size: 11px; margin: 2px 0;
    overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
  }
  .usage { font-size: 11px; opacity: .7; margin: 0 0 4px; }
  .reviewer { font-size: 11px; opacity: .85; margin: 1px 0 1px 8px; }
  .badge { padding: 0 5px; border-radius: 8px; font-size: 10px; font-weight: 600; }
  .badge.running { background: var(--vscode-charts-green); color: var(--vscode-editor-background); }
  .badge.stopped {
    background: var(--vscode-inputValidation-warningBorder);
    color: var(--vscode-editor-background);
  }
  /* The role palette, taken from the sibling product's own token set
     (creds/src_vs_code/src/entityFormStyles.ts): a charts token with the hex it falls back to, so a
     theme that defines them wins and one that does not still gets the intended colour. */
  :root {
    --tone-plan: var(--vscode-charts-purple, #c586c0);
    --tone-arch: var(--vscode-charts-blue, #569cd6);
    --tone-sec: var(--vscode-charts-orange, #ce9178);
    --tone-uxdx: var(--vscode-charts-green, #b5cea8);
    --tone-limits: var(--vscode-charts-yellow, #d7ba7d);
    --tone-keys: var(--vscode-charts-red, #f14c4c);
    --tone-code: var(--vscode-widget-border, #454545);
  }
  .role-group { border: 1px solid var(--vscode-widget-border); border-radius: 4px; padding: 6px 8px 2px; margin: 0 0 10px; }
  .group-head { font-size: 11px; font-weight: 600; opacity: .8; margin: 0 0 6px; }
  /* A left edge rather than a filled box: it marks the role at a glance without turning the
     settings panel into four coloured slabs, and it survives a light theme unchanged. */
  .role { border: 1px solid var(--vscode-widget-border); border-left: 3px solid var(--tone-plan);
          border-radius: 3px; padding: 6px 8px 2px; margin: 0 0 8px; }
  .role .head { margin: 0 0 4px; }
  .role .name { font-weight: 600; }
  .role-plan { border-left-color: var(--tone-plan); }
  .role-arch { border-left-color: var(--tone-arch); }
  .role-sec { border-left-color: var(--tone-sec); }
  .role-uxdx { border-left-color: var(--tone-uxdx); }
  .role-code { border-left-color: var(--tone-code); }
  .tabs { display: flex; gap: 4px; margin: 0 0 8px; }
  .tab { flex: 1; padding: 3px 6px; font: inherit; color: var(--vscode-foreground);
         background: var(--vscode-editorWidget-background); border: 1px solid var(--vscode-widget-border);
         border-radius: 3px; cursor: pointer; }
  .tab:hover { background: var(--vscode-toolbar-hoverBackground); }
  .tab.on { background: var(--vscode-button-background); color: var(--vscode-button-foreground);
            border-color: var(--vscode-button-background); }
  .tab.on:hover { background: var(--vscode-button-hoverBackground); }
  /* The spending card. It was called .usage, which the rounds section had already claimed for its
     own line - so opacity .7 from a rule written for something else dimmed every card, and the
     .hint inside it to .7 x .65. Nothing was broken; the whole section just read as disabled.
     A name each. */
  .spend { margin: 0 0 12px; }
  .spend .head { display: flex; align-items: baseline; justify-content: space-between; gap: 8px; }
  .spend .name { font-weight: 600; overflow: hidden; text-overflow: ellipsis; }
  /* The money is the quiet half of the row: a dash where a vendor does not price its own runs. */
  .spend .cost { font-size: 11px; opacity: .75; flex: 0 0 auto; }
  /* The tokens are what the section is FOR, so they are read at full strength; the durations
     underneath stay a .hint, because they are context rather than the answer. */
  .spend .figures { font-size: 11px; margin: 3px 0 0; line-height: 1.45; }
  .bar { height: 6px; background: var(--vscode-editorWidget-background); border-radius: 3px;
         overflow: hidden; margin: 4px 0 2px; }
  .bar span { display: block; height: 100%; background: var(--vscode-button-background); }
  .warn { color: var(--vscode-editorWarning-foreground); }
  .total { margin-top: 8px; border-top: 1px solid var(--vscode-widget-border); padding-top: 6px; }
  .subject { font-weight: 600; margin: 6px 0 1px; }
  .empty { opacity: .6; font-style: italic; margin: 6px 0; }
  .status { margin: 2px 0 0; }
`;

/**
 * What the panel paints on: a key over the state that a REPAINT would change.
 *
 * <p>A repaint reloads the webview, which closes any dropdown that was open, so it is reserved
 * for the person's own doing. Everything that moves by itself travels through
 * {@link liveRegions} instead.</p>
 *
 * <p><b>Anything left out of this key is a control that can never change.</b> The spending
 * window was: clicking Today, Month or Year recorded the choice, produced an identical key, and
 * repainted nothing — so the section sat on Week for good, and the buttons read as broken
 * because they were.</p>
 */
/**
 * Every command a control in this panel can post.
 *
 * <p>It exists because the Update button did nothing for a day. The markup emitted
 * `data-command="installServer"`, the provider's switch had no case for it, and the click fell
 * into `default: return` — no error, no notification, no log. A button wired to nothing looks
 * exactly like a button whose work failed silently.</p>
 *
 * <p>The list is declared HERE, beside the markup that emits it, and the provider switches over
 * this type with an exhaustiveness check: a command added here without a case is a COMPILE error,
 * not a dead button. The test below adds the other half — a `data-command` in the markup that is
 * not in this list.</p>
 */
export const PANEL_COMMANDS = [
  'answer',
  'addVendor',
  'removeVendor',
  'runVendor',
  'installServer',
  'checkForUpdate',
  'usageWindow',
  'installVendorCli',
  'updateVendorCli',
  // Posted by the model picker rather than by a button: "another model…" is a request to type one.
  'customModel',
] as const;

export type PanelCommand = (typeof PANEL_COMMANDS)[number];

/**
 * Panel commands that are only a request to run a command the extension registered, by id.
 *
 * <p>Named here rather than typed at the call site because the exhaustiveness check proves a
 * `case` EXISTS, not that it invokes the right thing: a typo in the id would compile, pass every
 * guard, and reproduce the original silence exactly. A test holds these ids against the manifest's
 * own contributed commands, which is the only check that can catch that.</p>
 */
export const VSCODE_COMMAND_FOR = {
  installServer: 'coai.installServer',
} as const satisfies Partial<Record<PanelCommand, string>>;

export function isPanelCommand(value: string | undefined): value is PanelCommand {
  return value !== undefined && (PANEL_COMMANDS as readonly string[]).includes(value);
}

export function staticKey(state: PanelState): string {
  return JSON.stringify([
    state.settings,
    state.vendors,
    state.codexModels,
    state.serverInstalled,
    state.serverVersion,
    // Rare, and both are a person's doing or an answer they asked for.
    state.latestServerVersion,
    state.usageWindow,
    state.openSections,
  ]);
}
