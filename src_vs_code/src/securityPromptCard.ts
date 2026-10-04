import { escapeHtml as esc } from './escapeHtml';
import { promptFileIn } from './rolesPrompts';
import { SECURITY_SEED } from './securityLane.generated';
import { securityAlways, type SecurityPrompt } from './securityLane';
import { conditionsSummary, holdsContent, type PromptState } from './securityLaneState';
import type { SecurityTextState } from './securityPromptFiles';
import type { Vendor } from './vendors';

/**
 * One prompt card on the Security lane tab (research/PLAN_the_security_tab_reads_at_a_glance.md, epic 3, sections B–D):
 * its name and state, why the server would not send it, the reviewers it is paired with, its buttons, and its
 * conditions folded away — conditions are fine-tuning, needed rarely.
 */
export interface CardInput {
  readonly prompt: SecurityPrompt;
  readonly state: PromptState;
  readonly text: SecurityTextState;
  readonly vendors: readonly Vendor[];
  readonly paired: (vendor: string) => boolean;
  /** The server is too old for the lane: the reviewer boxes, which pair a prompt, are drawn disabled. */
  readonly old: boolean;
  /** The folder the server reads overrides from, for naming the file a person writes in. */
  readonly promptsDir: string;
}

const at = (field: string): string => `data-setting="securityLane" data-security-field="${esc(field)}"`;
const check = (field: string, on: boolean, label: string, disabled = false): string =>
  `<label><input type="checkbox" ${at(field)}${on ? ' checked' : ''}${disabled ? ' disabled' : ''}> ${esc(label)}</label>`;
/** A button whose action may remove it: `then` names the button that takes the focus after the repaint. */
const button = (command: string, id: string, label: string, then: readonly [string, string] = ['', '']): string =>
  `<button type="button" data-command="${esc(command)}" data-id="${esc(id)}"`
  + (then[0] === '' ? '' : ` data-seclane-then="${esc(then[0])}" data-seclane-then-id="${esc(then[1])}"`) + `>${esc(label)}</button>`;

const BADGE: Readonly<Record<PromptState, string>> = { shipped: 'default', edited: 'edited', custom: 'custom' };

export function promptCard(card: CardInput): string {
  const { prompt } = card;
  // A space between the name and its badge: the legend is read as one accessible name, and "redteam-authzdefault" is not one.
  return `<fieldset class="seclane-prompt seclane-prompt--${card.state}">`
    + `<legend><span class="seclane-name">${esc(prompt.id)}</span> <span class="seclane-badge">${BADGE[card.state]}</span></legend>`
    + triggerWarning(prompt, card.state)
    + textProblem(card)
    + `<p>${card.vendors.filter(v => v.enabled).map(v =>
      check('pair:' + v.id + ':' + prompt.id, card.paired(v.id), v.id, card.old)).join(' ')}</p>`
    + whereTheTextLives(card)
    + actions(card)
    + conditionsBody(prompt)
    + '</fieldset>';
}

/** A shipped preset with no trigger never runs — said OUTSIDE the fold, so a collapsed card cannot hide it. */
function triggerWarning(p: SecurityPrompt, state: PromptState): string {
  return state !== 'custom' && !securityAlways(p.id) && p.triggers.length === 0
    ? `<p class="stale">${esc(p.id)} has no triggers; select at least one condition to run this preset.</p>` : '';
}

/** The prompt's override file, by the one rule that names it — nothing for an id that may not become a path. */
const fileOf = (card: CardInput): string | undefined => promptFileIn(card.promptsDir, card.prompt.id);
const TEXT_PROBLEMS: Readonly<Partial<Record<SecurityTextState, string>>> = {
  placeholder: 'holds only the operator placeholder',
  oversized: 'is over 64 KiB',
  unreadable: 'cannot be read',
};

/**
 * Why the server would not send this prompt's text. A custom prompt with no usable text of its own refuses its pairs;
 * a shipped one whose override is unusable is excluded rather than falling back to the shipped text; and an id that
 * cannot name a file — a hand-edited setting — has no text anywhere.
 */
function textProblem(card: CardInput): string {
  const file = fileOf(card);
  if (file === undefined) {
    return `<p class="stale">${esc(card.prompt.id)} cannot name a prompt file, so it has no text and the lane reports its pairs as unable to run. Rename it in coai.securityLane in your settings JSON.</p>`;
  }
  const why = TEXT_PROBLEMS[card.text];
  return why === undefined ? missingText(card, file) : `<p class="stale">${esc(file)} ${why}, so the lane reports its pairs as unable to run.</p>`;
}
/** A custom prompt has no shipped text to fall back to: until its own file holds some, its pairs cannot run. */
function missingText(card: CardInput, file: string): string {
  return card.state === 'custom' && card.text !== 'written'
    ? `<p class="stale">No usable prompt text — write it in ${esc(file)}. Until then the lane reports its pairs as unable to run.</p>` : '';
}

function whereTheTextLives(card: CardInput): string {
  const file = fileOf(card);
  if (card.state !== 'custom') return `<p>Repository prompt: <code>src_mcp/src/prompts/${esc(card.prompt.id)}.md</code></p>`;
  return file === undefined ? '' : `<p>Prompt text: <code>${esc(file)}</code></p>`;
}

/**
 * Restore default shows wherever it would do something: a shipped prompt whose conditions changed (except general, whose
 * leftover conditions have their own Clear button), and one whose override file holds anything at all — an unusable
 * override is still the person's text, and restoring is how they get rid of it (`restoreSteps` asks first).
 */
const restorable = (card: CardInput): boolean =>
  card.state !== 'custom' && (holdsContent(card.text) || (card.state === 'edited' && !securityAlways(card.prompt.id)));

/** Edit opens the prompt's file — so an id that cannot name one gets no button that would silently do nothing. */
function editButton(card: CardInput): string {
  if (fileOf(card) === undefined) return '';
  return button('editSecurityPrompt', card.prompt.id, card.state === 'custom' ? 'Edit prompt text' : 'Edit local prompt override');
}

function actions(card: CardInput): string {
  const id = card.prompt.id;
  const own = card.state === 'custom';
  return '<p class="seclane-actions">'
    + editButton(card)
    + (restorable(card) ? button('restoreSecurityPrompt', id, 'Restore default', ['editSecurityPrompt', id]) : '')
    + (own ? button('removeSecurityPrompt', id, 'Remove custom prompt', ['newSecurityPrompt', '']) : '')
    + '</p>';
}

/**
 * What decides whether a prompt runs, folded: the summary line says it, the fold holds the two columns of boxes. The
 * fold is drawn CLOSED — the page reopens what the person opened (`securityLaneScript.ts`) — so a toggle never moves
 * the paint key. An "always" prompt has no conditions and draws none.
 */
function conditionsBody(p: SecurityPrompt): string {
  if (securityAlways(p.id)) return alwaysBody(p);
  return `<details class="seclane-conditions" data-seclane-open="${esc(p.id)}"><summary>${esc(conditionsSummary(p))}</summary>
    <div class="seclane-conditions-grid">
      <div><h4>Run when code matches</h4>${SECURITY_SEED.signals.filter(s => s.trigger).map(s =>
        check('trigger:' + p.id + ':' + s.id, p.triggers.includes(s.id), s.label)).join('')}
        <label>All trigger tags <input type="text" ${at('prompt:' + p.id + ':triggers')} value="${esc(p.triggers.join(', '))}"></label></div>
      <div><h4>Prioritize source</h4>${SECURITY_SEED.signals.map(s =>
        check('focus:' + p.id + ':' + s.id, p.focus.includes(s.id), s.label)).join('')}
        <label>All focus tags <input type="text" ${at('prompt:' + p.id + ':focus')} value="${esc(p.focus.join(', '))}"></label></div>
    </div></details>`;
}

/**
 * An "always" prompt's whole condition story. A general registered by hand before it shipped may still carry
 * triggers: a server from before then runs it only when they match, so the card must not claim "every change"
 * until they are cleared — and clearing them is one button (the plan's D6; the coai code round on epic 1).
 */
function alwaysBody(p: SecurityPrompt): string {
  if (p.triggers.length === 0) return '<p>Runs on every code change while a reviewer is ticked; it has no conditions.</p>';
  return `<p class="stale">${esc(p.id)} has stored conditions (${esc(p.triggers.join(', '))}) from before it shipped. This version ignores them, but an older MCP server still runs it only when they match. Clear them so it runs on every code change.</p>
    <p class="seclane-actions">${button('clearSecurityConditions', p.id, 'Clear stored conditions', ['editSecurityPrompt', p.id])}</p>`;
}
