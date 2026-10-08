import { FEATURE_STAGE, MAX_ACTIVE_PER_BUCKET, PLAN_STAGE, RESULT_CODE, RESULT_STAGE, activeCount, bucketOf, isActive, isBuiltIn, isProgramming, stageOf, whyNotAskable, type RoleRow } from './roles';
import { isShippedPrompt } from './rolesMessages';
import { STOOD_DOWN, type Tombstone } from './roleDeletion';
import { escapeHtml } from './webviewHtml';
import { roleTone } from './roleTone';
import { type BucketCounts, lastOn, lastOnBy, onCounts, switchedOn } from './rolesSwitch';

/**
 * The pieces a roles page is drawn from: one role's block, the switch rules it is drawn by, and the notes around it.
 *
 * <p><b>Why this is not in `rolesPage.ts` any more.</b> Two pages draw these: the Review roles tab, and Reviews › Roles
 * &amp; prompts on the new Settings page (`rolesEmbed.ts`, todo/PLAN_one_model_catalog.md E4.3), which draws the tab's
 * own blocks rather than a copy of them. Epic 5 deletes the tab (E5.1), so the blocks were moved out first —
 * prerequisite (b) of that epic. They are drawn byte for byte as before: the move compared the html of every shipped
 * role, in every switch state, from both builds. The functions the move put under the complexity rule — the block, a
 * prompt's block and the version comparison — were split into named parts to get there, and draw the same text.</p>
 *
 * <p><b>Everything here is text the person in front of it wrote</b>, which makes escaping a different question from
 * the rest of the panel. Elsewhere a label comes from a catalog this product shipped; here it is their own name for
 * their own role, and somebody will paste a `&lt;script&gt;` into it to see what happens. The answer must be that
 * they see a `&lt;script&gt;`.</p>
 *
 * <p><b>The edges point one way.</b> The tab, the new page's embed and `panelView.ts` (for {@link CUSTOM_ROLES_SINCE})
 * import this module, and it imports none of them: an edge back into `panelView.ts` would close a ring the
 * import-cycle ratchet (`importCycles.test.mjs`) refuses.</p>
 */

/**
 * How tall a prompt box starts.
 *
 * <p>A prompt is often a paragraph and sometimes several, and a box three lines high is reading one
 * three lines at a time. It is a starting HEIGHT and nothing else — the box scrolls, it is
 * `resize: vertical`, and what it holds is its value however much of it is on screen.</p>
 */
const PROMPT_ROWS = 10;

/**
 * Whether this role's switch may be turned ON.
 *
 * <p>Five active per BUCKET, and the sixth is refused HERE rather than dropped by the server. A page
 * that let somebody tick a sixth and then showed them five would be a page that lies about what it
 * saved — the server does cap, and it names what it capped, so nothing is at risk except the truth.</p>
 */
export function canActivate(rows: readonly RoleRow[], role: RoleRow): boolean {
  return isActive(role) || activeCount(rows, bucketOf(role)) < MAX_ACTIVE_PER_BUCKET;
}

/**
 * Whether this role's switch may be turned OFF.
 *
 * <p>The last one standing in a bucket cannot be: a bucket with no role in it produces a round with
 * no reviewer, which the session counts as unresolved and never lets a person retry. The server
 * refuses such a round with a sentence — `review_plan` and `review_code` both do — and refusing it
 * HERE, where the pointer is, beats refusing it at round time, after somebody has waited for a
 * review that was never going to happen.</p>
 *
 * <p>It is the rule the sidebar's code-role ticks have had since they shipped, applied to the plan
 * stage as well, which is what the operator asked for when they asked for plan-stage switches.</p>
 *
 * <p><b>ON is the one switch</b> (`rolesSwitch.lastOn`, todo/PLAN_one_model_catalog.md E5.1b): a role the panel's
 * `roleEnabled` switched off is not a reviewer, whatever the catalog says, so it does not keep a bucket populated. The
 * new page passes its `roleEnabled`; the Review roles tab, which draws the catalog's switch alone, passes none.</p>
 */
export function canDeactivate(rows: readonly RoleRow[], role: RoleRow, roleEnabled: Readonly<Record<string, boolean>> = {}): boolean {
  return !lastOn(rows, role, roleEnabled);
}

/**
 * What a role's KIND means for whether it runs — said only where the answer is surprising.
 *
 * <p>Until plan 4 every non-programming role ran in nothing and every one of them carried the same
 * sentence. Now one of the two buckets HAS a round, so repeating it there would be a page saying a
 * role does nothing while the round it takes part in is running — and dropping it from the other
 * would leave the one case that is still true unsaid. "Not built yet" and "does not exist" are
 * different states, and this is where a person meets the difference.</p>
 */
function kindHint(role: RoleRow): string {
  if (isProgramming(role)) {
    return '';
  }

  switch (stageOf(role)) {
    case PLAN_STAGE:
      return '<p class="hint">A plan-stage role that is not a programming task is kept and takes part in '
        + 'no round yet — there is no plan gate for non-programming work. Move it to the result stage and '
        + '<code>review_document</code> will run it.</p>';
    case FEATURE_STAGE:
      return '<p class="hint">A feature-stage role that is not a programming task is kept and takes part in '
        + 'no round — a feature review reads code, outlined. Mark it a programming task and '
        + '<code>review_feature</code> will run it.</p>';
    default:
      return '';
  }
}

/**
 * How a role block is drawn on the page that draws it. The new Settings page (todo/PLAN_one_model_catalog.md E4.3) marks
 * a prompt `data-role-prompt` — its shared script reads `data-prompt` as a round pick — and shows ONE switch per role,
 * the catalog's and the panel's read as one (`rolesSwitch.ts`).
 *
 * <p>It hands the panel's switches rather than a predicate of its own, so a block's tick and its last-role refusal are
 * both read by `rolesSwitch` — the tick through `switchedOn`, the refusal through `lastOn`, over the same switches
 * (E5.1b). A predicate for the tick beside a count of the catalog for the refusal is how the two disagreed.</p>
 */
export interface RoleBlockOptions {
  readonly promptAttr: string;
  /** The panel's `roleEnabled` — empty for a page that draws the catalog's switch alone. */
  readonly roleEnabled: Readonly<Record<string, boolean>>;
  /** Each bucket's roles ON by the one switch — what the last-ON refusal reads. Counted once per page. */
  readonly on: BucketCounts;
  /** Each bucket's roles active in the catalog — what the five-per-bucket room reads. Counted once per page. */
  readonly active: BucketCounts;
}

/**
 * The options every block of one page is drawn with — built ONCE per page, with both counts taken here (E5.1b's code
 * round, finding 1): a block asking its own count rescanned the whole role list, so a page of n roles read it n² times.
 *
 * @param rows every role, as the page composes them
 * @param promptAttr the attribute a prompt is marked with — `data-role-prompt` on the new page, `data-prompt` on the
 *   Review roles tab
 * @param roleEnabled the panel's switches — none on the Review roles tab, which draws the catalog's switch alone
 */
export function roleBlockOptions(rows: readonly RoleRow[], promptAttr: string, roleEnabled: Readonly<Record<string, boolean>>): RoleBlockOptions {
  return { promptAttr, roleEnabled, on: onCounts(rows, roleEnabled), active: onCounts(rows, {}) };
}

/** The prompt id, label and purpose a prompt block is drawn from — the fields of a role's prompt the page shows. */
interface PromptShown {
  readonly id: string;
  readonly label?: string;
  readonly purpose?: string;
}

function promptBlock(role: RoleRow, prompt: PromptShown, texts: Readonly<Record<string, string>>, promptAttr: string): string {
  const shipped = isShippedPrompt(role.id, prompt.id);
  const text = texts[prompt.id] ?? '';

  return `  <div class="prompt${shipped ? '' : ' mine'}" ${promptAttr}="${escapeHtml(prompt.id)}">
    <div class="head">
${promptHead(prompt, shipped)}
      ${promptButton(prompt.id, shipped, text)}
    </div>
    <textarea data-field="text" rows="${PROMPT_ROWS}" placeholder="${shipped ? 'The text this product ships. Write here to replace it.' : 'The question this prompt asks.'}">${escapeHtml(text)}</textarea>
  </div>`;
}

/** A prompt's label and purpose — readonly on a shipped one, whose picker entry is the product's. */
function promptHead(prompt: PromptShown, shipped: boolean): string {
  const readonly = shipped ? ' readonly' : '';

  return `      <input type="text" data-field="label" value="${escapeHtml(prompt.label ?? prompt.id)}" placeholder="What the picker shows"${readonly}>
      <input type="text" class="purpose" data-field="purpose" value="${escapeHtml(prompt.purpose ?? '')}" placeholder="The picker's tooltip"${readonly}>`;
}

/** Restore for a shipped prompt — nothing to restore while its text is the shipped one — and Remove for one of yours. */
function promptButton(promptId: string, shipped: boolean, text: string): string {
  return shipped
    ? `<button type="button" class="restore" data-restore="${escapeHtml(promptId)}"${text.length === 0 ? ' disabled' : ''}>Restore</button>`
    : `<button type="button" class="remove" data-remove-prompt="${escapeHtml(promptId)}">Remove</button>`;
}

/**
 * The mark on an ACTIVE role that will not be asked — issue #338, for the routes the refusal to switch
 * one on cannot see: a text erased after the switch, a settings file edited by hand, a prompt file
 * deleted. Until this the only signal was one line in a round reply, naming a generated id and a path.
 * A role that is off needs no mark: it is not asked anyway, and switching it on says why it cannot be.
 */
function unaskableHint(role: RoleRow, texts: Readonly<Record<string, string>>): string {
  const why = isActive(role) ? whyNotAskable(role, texts) : '';

  return why.length === 0
    ? ''
    // "A round that asks its first prompt", not "never": a per-round choice of a later prompt WITH text
    // is still asked. (CodeRabbit, PR #495.)
    : `<p class="hint warn">It is switched on, but a round that asks its first prompt will skip it — `
      + `every round, unless another prompt is chosen for it. ${escapeHtml(why)}</p>`;
}

/**
 * One role's block, from the page's options — whose counts were taken once for the whole page ({@link roleBlockOptions}),
 * so a block reads no other role.
 */
export function roleBlock(role: RoleRow, texts: Readonly<Record<string, string>>, options: RoleBlockOptions): string {
  const shipped = isBuiltIn(role.id);
  const on = switchedOn(role, options.roleEnabled);
  // Asked once per block, and answered for all three ways a role leaves its bucket: the switch, the stage picker and
  // Remove (E5.1b's code round, finding 0) — the host refuses each, and the page offers none of them.
  const last = lastOnBy(options.on, role, options.roleEnabled);

  return `${detailsTag(role, on)}
  <summary>
${roleSummary(role, shipped)}
  </summary>
  <div class="fields">
${roleFields(role, texts, options, last)}
  </div>
${promptBlocks(role, texts, options.promptAttr)}
  <button type="button" class="add" data-add-prompt="${escapeHtml(role.id)}">Add a prompt</button>
  ${removeRoleButton(role.id, shipped, last)}
</details>`;
}

/** The block's opening tag: its stage's colour, and open while the role is switched on. */
function detailsTag(role: RoleRow, on: boolean): string {
  return `<details class="role role-${roleTone(role.id, stageOf(role))}${on ? '' : ' off'}" data-id="${escapeHtml(role.id)}"${on ? ' open' : ''}>`;
}

function roleSummary(role: RoleRow, shipped: boolean): string {
  return `    <span class="title">${escapeHtml(role.name ?? role.id)}</span>
    <span class="id">${escapeHtml(role.id)}</span>
    ${shipped ? '<span class="badge">shipped</span>' : ''}`;
}

/** What a role's fields say and allow: its name, its stage, its kind, its switch, and why any of them is fixed. */
function roleFields(role: RoleRow, texts: Readonly<Record<string, string>>, options: RoleBlockOptions, last: boolean): string {
  const shipped = isBuiltIn(role.id);
  const on = switchedOn(role, options.roleEnabled);
  const may = on ? !last : mayActivate(options.active, role);

  return `${nameField(role, shipped)}
${stageField(stageOf(role), shipped, last)}
    ${programmingFlag(role, shipped)}
    ${activeFlag(on, may)}
    ${switchHint(may, last)}
    ${kindHint(role)}
    ${unaskableHint(role, texts)}
    ${shippedHint(shipped)}`;
}

/**
 * Whether an OFF switch may be turned on — {@link canActivate}, over the page's count. Off by the panel's switch alone, a
 * role the catalog holds active may always be switched on: that write moves no count of the catalog's. (ON, the switch
 * may be turned off unless it is the last role ON in its bucket — `roleFields` reads that from `last`.)
 */
function mayActivate(active: BucketCounts, role: RoleRow): boolean {
  return isActive(role) || (active.get(bucketOf(role)) ?? 0) < MAX_ACTIVE_PER_BUCKET;
}

function nameField(role: RoleRow, shipped: boolean): string {
  return `    <label>Name
      <input type="text" data-field="name" value="${escapeHtml(role.name ?? role.id)}" placeholder="What this role is called"${shipped ? ' readonly' : ''}>
    </label>`;
}

/** The three stages a role can be in, as its picker offers them. */
const STAGE_OPTIONS: readonly (readonly [string, string])[] = [
  [PLAN_STAGE, 'Plan review'],
  [RESULT_STAGE, 'Code review'],
  [FEATURE_STAGE, 'Feature review'],
];

/**
 * The stage picker. For the last role ON in its bucket every OTHER stage is drawn disabled: moving it there would leave
 * its bucket with no reviewer, which the host refuses (`rolesEdit.whyNotMoved`) — a choice the page offers only to have
 * it refused after the fact is a choice the page should not have offered. Its own stage stays chosen.
 */
function stageField(stage: string, shipped: boolean, last: boolean): string {
  const options = STAGE_OPTIONS.map(([value, label]) => stageOption(value, label, stage, last));

  return `    <label>Stage
      <select data-field="stage"${shipped ? ' disabled' : ''}>
        ${options.join('\n        ')}
      </select>
    </label>`;
}

/** One stage of the picker: chosen when it is the role's, and closed to the last role ON when it is not. */
function stageOption(value: string, label: string, stage: string, last: boolean): string {
  const own = stage === value;

  return `<option value="${value}"${own ? ' selected' : ''}${last && !own ? ' disabled' : ''}>${label}</option>`;
}

function programmingFlag(role: RoleRow, shipped: boolean): string {
  return `<label class="flag"><input type="checkbox" data-field="programmingTask"${role.programmingTask ?? true ? ' checked' : ''}${shipped ? ' disabled' : ''}> A programming task</label>`;
}

function activeFlag(on: boolean, may: boolean): string {
  return `<label class="flag"><input type="checkbox" data-field="active"${on ? ' checked' : ''}${may ? '' : ' disabled'}> Active</label>`;
}

/** Why the switch cannot move — the last one on in its stage, or a stage already full — or nothing when it can. */
function switchHint(may: boolean, last: boolean): string {
  if (may) {
    return '';
  }

  return last
    ? '<p class="hint">The only role still active in this stage — switch another one on before turning this one off, moving it to another stage or removing it, or the stage would have no reviewer in it at all.</p>'
    : `<p class="hint">Five roles are already active in this stage. Switch one off to make room.</p>`;
}

function shippedHint(shipped: boolean): string {
  return shipped
    ? '<p class="hint">A role this product ships. Its id, its name, its stage and its kind are fixed — they key your settings, your open sessions and every round already recorded, and the review server reads none of them from your configuration. Its switch and its prompt text are yours.</p>'
    : '';
}

function promptBlocks(role: RoleRow, texts: Readonly<Record<string, string>>, promptAttr: string): string {
  return (role.prompts ?? []).map((p) => promptBlock(role, p, texts, promptAttr)).join('\n');
}

/**
 * Remove, for a role of the person's own — drawn disabled for the last role ON in its bucket, which the host would refuse
 * to remove (`rolesEdit.removed`); the switch's hint above says why and what to do first.
 */
function removeRoleButton(roleId: string, shipped: boolean, last: boolean): string {
  return shipped
    ? ''
    : `<button type="button" class="remove role" data-remove="${escapeHtml(roleId)}"${last ? ' disabled' : ''}>Remove this role</button>`;
}

/**
 * The banner for a server too old to read any of this.
 *
 * <p>The third of its kind, and the same shape as the two in the panel: shown only when the server
 * is KNOWN and strictly older, naming the version that is there. Below 0.19.0 the key is never
 * read — the roles are on this page and in no round, and nothing anywhere fails. That is precisely
 * why it is said out loud.</p>
 */
export function tooOldFor(serverVersion: string, rows: readonly RoleRow[]): string {
  if (serverVersion.length === 0 || rows.length === 0 || !older(serverVersion, CUSTOM_ROLES_SINCE)) {
    return '';
  }

  return `<div class="stale">The coai-mcp you have installed (${escapeHtml(serverVersion)}) does not read `
    + `roles at all, so nothing on this page will run. Update it to ${escapeHtml(CUSTOM_ROLES_SINCE)} `
    + `or later — the <b>MCP server</b> tab of ConnectOtherAIs Settings.</div>`;
}

/**
 * The note for a server this window has not identified yet.
 *
 * <p>`coai.editRoles` is on the command palette, so this page can be the FIRST thing opened in a
 * window — before the panel has rendered, which is what detects the installed server. An unknown
 * server draws no banner, and no banner reads exactly like "checked, and fine". Saying the check has
 * not run is the honest shape, and it disappears the moment the version arrives, because the host
 * repaints when it learns one.</p>
 *
 * <p>Only when there is something that could fail to run: a machine with no roles of its own has
 * nothing at stake in the answer.</p>
 */
export function unknownServerNote(serverVersion: string, rows: readonly RoleRow[]): string {
  if (serverVersion.length > 0 || rows.length === 0) {
    return '';
  }

  return `<div class="stale">Whether the coai-mcp you have installed can read these roles has `
    + `<b>not been checked yet</b> — it needs ${escapeHtml(CUSTOM_ROLES_SINCE)} or later. Open the `
    + `ConnectOtherAIs panel and this line will answer itself.</div>`;
}

/** The first `coai-mcp` that reads `COAI_ROLES`. Below it, this page writes into a void. */
export const CUSTOM_ROLES_SINCE = '0.19.0';

/**
 * Whether `version` is strictly older than `since`, comparing numbers rather than text.
 *
 * <p>The first part where the two differ decides, a missing part counting as 0. A part that is not a number decides
 * "not older": `NaN` differs from everything, so it is where the comparison stops, and it is less than nothing.</p>
 */
function older(version: string, since: string): boolean {
  const mine = numbersOf(version);
  const theirs = numbersOf(since);
  const differing = Array.from({ length: Math.max(mine.length, theirs.length) }, (_, i) => [mine[i] ?? 0, theirs[i] ?? 0] as const)
    .find(([a, b]) => a !== b);

  return differing !== undefined && differing[0] < differing[1];
}

function numbersOf(version: string): readonly number[] {
  return version.split('.').map((n) => Number.parseInt(n, 10));
}

/**
 * Whether a role added now could not be switched on even once its question is written.
 *
 * <p>Every new role arrives switched off since issue #338 — it has no question yet — so what is worth
 * saying beside the button is the other reason it would stay off: the stage is full. A new role always
 * joins the code bucket of the result stage, so that is the only count to take.</p>
 */
export function stageIsFull(all: readonly RoleRow[]): boolean {
  return activeCount(all, RESULT_CODE) >= MAX_ACTIVE_PER_BUCKET;
}

/**
 * The deletions that are stuck, and the two things that can be done about them.
 *
 * <p><b>Reload Window comes first when the mirror STOOD DOWN</b>, because that is the thing which
 * actually ends one: a newer build owns the settings file, and reloading is how this window becomes
 * that build. Offering the destructive action first for a condition with a cure would be offering
 * the wrong one.</p>
 *
 * <p><b>And the other action costs something, which it says here rather than afterwards.</b> While
 * the server still carries the row, deleting the prompt text IS the incident: rounds run the role,
 * find no text, and complain each time. The softer wording this had first — <i>the server may still
 * carry the row</i> — describes the same fact without naming what it does to the person.
 * (antigravity, the plan round.)</p>
 */
export function strandedHtml(stranded: readonly Tombstone[]): string {
  if (stranded.length === 0) {
    return '';
  }
  const rows = stranded.map((one) => {
    const reload = one.reason === STOOD_DOWN
      ? '<button type="button" class="act" data-reload="1">Reload Window</button>'
      : '';

    return `<li><b>${escapeHtml(one.name)}</b> was removed here, and the server has not been told yet.`
      + ` <span class="why">${escapeHtml(one.reason)}</span>`
      + ` Its prompts are kept until the server has it.`
      + `${reload}`
      + `<button type="button" class="act" data-finish="${escapeHtml(one.roleId)}">Finish the deletion anyway</button>`
      + `<span class="cost">The server may go on running this role without its text until its settings`
      + ` catch up, and rounds against it will complain each time.</span></li>`;
  }).join('');

  return `<section class="stranded" aria-label="Deletions the server has not been told about"><ul>${rows}</ul></section>`;
}
