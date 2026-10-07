import type { CommandStageName } from './commands';
import type { RowCommand } from './commandsEdit';

/**
 * What a message about the gate commands MEANS, decided without a host or a page.
 *
 * <p><b>Why this is not in `commandsPage.ts` any more.</b> Two pages post these messages: the Gate commands tab, and
 * Reviews › Commands on the new Settings page (todo/PLAN_one_model_catalog.md E4.4), whose `commands` messages reach the
 * same editing core (`commandsHost.ts`). Epic 5 deletes the tab (E5.1); the core's command type and the panel's parser
 * cannot be deleted with it, so they were moved out first — prerequisite (b) of that epic — exactly as they were.</p>
 *
 * <p><b>The edges point one way.</b> This module imports the commands' own vocabulary and nothing that draws: the tab,
 * the blocks (`commandsBlocks.ts`), the core and the panel import it, and it imports none of them, `panelView.ts` least
 * of all — the import-cycle ratchet (`importCycles.test.mjs`) would name the ring.</p>
 */

/** What a commands page can ask the host for: an edit of the rows, or a text written or restored. */
export type PageCommand =
  | RowCommand
  | { readonly kind: 'text'; readonly fileId: string; readonly value: string }
  | { readonly kind: 'restore'; readonly fileId: string }
  | { readonly kind: 'ignore' };

const IGNORE: PageCommand = { kind: 'ignore' };

/** The rounds a command can be given in, in the order a picker offers them — and the only stages a message may name. */
export const STAGE_NAMES: readonly CommandStageName[] = ['any', 'plan', 'code'];

/** A message from the page, read as one of the few things it may ask — anything else is ignored. */
export function commandEdit(message: unknown): PageCommand {
  const m = recordOf(message);
  const type = text(m['type']);

  return Object.hasOwn(READERS, type) ? (READERS[type] ?? ignored)(m) : IGNORE;
}

const ignored = (): PageCommand => IGNORE;

function recordOf(message: unknown): Record<string, unknown> {
  return typeof message === 'object' && message !== null ? message as Record<string, unknown> : {};
}

const text = (value: unknown): string => (typeof value === 'string' ? value : '');

const READERS: Readonly<Record<string, (m: Record<string, unknown>) => PageCommand>> = {
  // No token from the page: the host draws a random one (`commandsPanel.store`).
  add: () => ({ kind: 'add', token: '' }),
  remove: (m) => ({ kind: 'remove', id: text(m['id']) }),
  retitle: (m) => ({ kind: 'retitle', id: text(m['id']), value: text(m['value']) }),
  restage: (m) => stageCommand(text(m['id']), m['value']),
  switch: (m) => (typeof m['value'] === 'boolean' ? { kind: 'switch', id: text(m['id']), value: m['value'] } : IGNORE),
  text: (m) => ({ kind: 'text', fileId: text(m['fileId']), value: text(m['value']) }),
  restore: (m) => ({ kind: 'restore', fileId: text(m['fileId']) }),
};

function stageCommand(id: string, value: unknown): PageCommand {
  const stage = STAGE_NAMES.find((one) => one === value);

  return stage === undefined ? IGNORE : { kind: 'restage', id, value: stage };
}
