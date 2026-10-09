import { escapeHtml } from './escapeHtml';

/**
 * The new Settings page's shared pieces (todo/PLAN_one_model_catalog.md, E3.1): what a control says when the installed
 * coai-mcp cannot use it, the "new" tag, the one confirm, and what a tab not built yet shows. Pure — every tab of the
 * page draws through these, so a sentence or a rule lives in one place.
 */

/**
 * What the page knows about the installed coai-mcp: whether there is one on this side, and what its `--features`
 * lists — `undefined` until that list has SETTLED (a cold start, a timeout), which is not the same as an empty list.
 */
export interface BinarySays {
  readonly installed: boolean;
  readonly features: readonly string[] | undefined;
}

/**
 * Why a control does nothing yet on this side — ONE road for every gate, by CAPABILITY rather than by a version number
 * (the plan's epic 2 revision: the binary's own `--features` decides, and no constant guesses a release).
 *
 * <p>Nothing while the binary's list has not settled: a cold start must not paint "ignores" on every card of a fully
 * able binary (epic 3's plan round). "Not installed" only when there is no binary at all.</p>
 *
 * @param feature the `--features` name the control needs
 * @param what the control, as a person names it — "its system prompt"
 */
export function skew(feature: string, what: string, binary: BinarySays): string {
  if (!binary.installed) {
    return `coai-mcp is not installed on this side, so ${what} does nothing yet — install it under Setup › MCP server.`;
  }

  return binary.features === undefined || binary.features.includes(feature)
    ? ''
    : `this side's coai-mcp does not take ${what} yet, so it is not applied — update it under Setup › MCP server.`;
}

/** How long a control is marked new after this window first saw it. */
export const NEW_FOR_MS = 7 * 24 * 60 * 60 * 1000;

/** When each control was first seen in this window's profile (`newTags.ts` keeps it), by control id. */
export type FirstSeen = Readonly<Record<string, number>>;

/**
 * The "new" mark on one control — for {@link NEW_FOR_MS} after it was first seen, then never again.
 *
 * <p>By CONTROL, not by a global "days since the update": a control that arrives in a later release is new on its own
 * clock (the mockup had one counter for the whole page).</p>
 */
export function newTag(controlId: string, seen: FirstSeen, now: number): string {
  const at = seen[controlId];

  return at !== undefined && at > 0 && now - at < NEW_FOR_MS
    ? ` <span class="tag-new" title="New in this version — the mark goes a week after you first saw it.">new</span>`
    : '';
}

/** What one confirmed action is: the button, and what the dialog asks before the command is sent. */
export interface Confirmed {
  readonly label: string;
  readonly command: string;
  readonly id: string;
  readonly title: string;
  /** What the person is told, as plain text — escaped here. */
  readonly body: string;
  /** The dialog's action button — "Remove", "Run the check". */
  readonly action: string;
  /** Whether the action loses something; the action button is then drawn as a danger. */
  readonly danger: boolean;
  /** Set when the action may not be taken now; the button is drawn disabled with this as its title. */
  readonly refused?: string;
}

/**
 * A button that asks first — through the page's ONE confirm dialog, which every confirmed action shares (Remove
 * included; the mockup drew a second dialog for it). The page's script opens the dialog from these attributes and sends
 * the command only on the action button; `data-asks` rather than `data-command`, so the shared wiring never sends it
 * on the first click.
 */
export function confirmButton(confirmed: Confirmed): string {
  const refused = confirmed.refused ?? '';

  return `<button type="button" class="ask" data-asks="${escapeHtml(confirmed.command)}" data-id="${escapeHtml(confirmed.id)}"`
    + ` data-ask-title="${escapeHtml(confirmed.title)}" data-ask-body="${escapeHtml(confirmed.body)}"`
    + ` data-ask-action="${escapeHtml(confirmed.action)}" data-ask-danger="${confirmed.danger ? 'true' : 'false'}"`
    + `${refused.length > 0 ? ` disabled title="${escapeHtml(refused)}"` : ''}>${escapeHtml(confirmed.label)}</button>`;
}

/** The one dialog every confirmed action opens — filled by the page's script from the button that asked. */
export const CONFIRM_DIALOG = `<dialog id="confirm-dialog" aria-labelledby="confirm-title">
<h2 id="confirm-title"></h2>
<p id="confirm-body"></p>
<div class="dialog-buttons"><button type="button" id="confirm-keep" autofocus>Keep it</button><button type="button" id="confirm-go"></button></div>
</dialog>`;
