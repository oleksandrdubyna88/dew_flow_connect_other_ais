/**
 * "Add a model" grouped by WHERE the model runs (todo/PLAN_one_model_catalog.md E3.2): a CLI on this machine, an API key,
 * this machine's GPU, a Team server — the four questions a person can answer before they know a vendor's name. Pure:
 * the host turns a `separator` into the picker's own separator item.
 */

export type AddGroup = 'cli' | 'api' | 'gpu' | 'remote';

/** The groups in the order the picker shows them, each with the line its separator says. */
export const ADD_GROUPS: readonly { readonly id: AddGroup; readonly label: string }[] = [
  { id: 'cli', label: 'A CLI on this machine' },
  { id: 'api', label: 'An API key' },
  { id: 'gpu', label: 'This machine’s GPU' },
  { id: 'remote', label: 'A Team server' },
];

const BY_RUNTIME: Readonly<Record<string, AddGroup>> = { api: 'api', local: 'gpu', remote: 'remote' };

/** Where a row of this runtime runs — every CLI runtime is `cli`. */
export function groupOf(runtime: string): AddGroup {
  return BY_RUNTIME[runtime] ?? 'cli';
}

/** A separator line in the picker. */
export interface Separator {
  readonly separator: string;
}

/**
 * The items in group order, each group under its separator; a group with no item has no separator. Within a group the
 * items keep the order they came in.
 */
export function grouped<T extends { readonly group: AddGroup }>(items: readonly T[]): readonly (T | Separator)[] {
  return ADD_GROUPS.flatMap(({ id, label }) => {
    const members = items.filter((item) => item.group === id);

    return members.length === 0 ? [] : [{ separator: label }, ...members];
  });
}

/** What the Team server group shows when this side holds a token for none — picking it says where to add one. */
export const NO_TEAM_SERVER = {
  label: 'A Team server',
  detail: 'None is signed in on this side — add one under Settings › Setup › Team servers first.',
} as const;
