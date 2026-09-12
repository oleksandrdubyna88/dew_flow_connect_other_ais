import { BUILTIN_ROLES } from './builtinRoles.generated';
import type { RoleDefinition } from './prompts';

/**
 * The review roles a person configured — the rows `COAI_ROLES` carries, unchanged.
 *
 * <p><b>This IS the wire format.</b> `coai.roles` holds exactly what the server's `RoleEntry` reads,
 * so `envBlock` is a `JSON.stringify` and nothing translates on the way. A panel-shaped model
 * normalised at the boundary would give the two halves two schemas to keep level, which is the
 * defect `PLAN_review_roles_become_data.md` spent eight stories removing.</p>
 *
 * <p><b>Every field but the id is optional, and that is the whole design.</b> A row naming a
 * built-in is an OVERRIDE, and an override must be able to say "leave that as it is" about every
 * field it does not mention — a row that carried `active: false` merely because the person edited a
 * prompt would switch Architecture off for them. The server's `RoleEntry` is nullable field for
 * field for the same reason; this type is its twin.</p>
 */
export interface RoleRow {
  readonly id: string;
  readonly name?: string;
  readonly stage?: string;
  readonly programmingTask?: boolean;
  readonly active?: boolean;
  readonly prompts?: readonly PromptRow[];
}

/** One prompt of a role: what the picker shows. The TEXT is a file, never this. */
export interface PromptRow {
  readonly id: string;
  readonly label?: string;
  readonly purpose?: string;
}

/** The seed's word for the stage a role reviews in. The panel says `code` for `result`. */
export const PLAN_STAGE = 'plan';
export const RESULT_STAGE = 'result';

/**
 * At most five ACTIVE roles per stage.
 *
 * <p>The server caps at the same number and names what it capped, so nothing here is load-bearing
 * for correctness — it is load-bearing for honesty. A page that let somebody tick a sixth and then
 * showed them five would be a page that lies about what it saved.</p>
 */
export const MAX_ACTIVE_PER_STAGE = 5;

/** A prompt id is a FILE NAME under `<dataDir>/prompts/`, so it is a slug and nothing else. */
const PROMPT_ID = /^[a-z0-9][a-z0-9-]*$/;

/** A role id becomes `COAI_ROUNDS_<ID>`, so it is latin, starts with a letter, and has no hyphen. */
const ROLE_ID = /^[A-Za-z][A-Za-z0-9_]*$/;

/**
 * Names Windows will not give a file, whatever the extension.
 *
 * <p>Mirrors `RoleComposition.ReservedNames` on the server. A prompt called `con` would compose, be
 * accepted, and then fail to have its text written on one operating system out of three.</p>
 */
const RESERVED = new Set([
  'con', 'prn', 'aux', 'nul',
  'com1', 'com2', 'com3', 'com4', 'com5', 'com6', 'com7', 'com8', 'com9',
  'lpt1', 'lpt2', 'lpt3', 'lpt4', 'lpt5', 'lpt6', 'lpt7', 'lpt8', 'lpt9',
]);

/** The ids this product ships, lower-cased once — a person may never take one for a new role. */
const SHIPPED_IDS = new Set(BUILTIN_ROLES.map((r) => r.id.toLowerCase()));

/** Whether this id names a role this product ships. Matched without case, as the server matches it. */
export function isBuiltIn(id: string): boolean {
  return SHIPPED_IDS.has(id.toLowerCase());
}

/** The shipped definition behind an id, or undefined for a role a person added. */
export function builtInFor(id: string): RoleDefinition | undefined {
  return BUILTIN_ROLES.find((r) => r.id.toLowerCase() === id.toLowerCase());
}

/**
 * The rows a person configured, with anything unusable dropped.
 *
 * <p>Dropped rather than thrown on, which is the house rule for everything read back out of a
 * setting: a person who hand-edits one row of JSON must not lose the other five, and a panel that
 * throws while rendering shows nothing at all. The SERVER refuses a bad row with a sentence the
 * panel displays; this only has to avoid drawing one.</p>
 */
export function rolesFrom(raw: unknown): readonly RoleRow[] {
  if (!Array.isArray(raw)) {
    return [];
  }

  const rows: RoleRow[] = [];
  const taken = new Set<string>();
  for (const row of raw) {
    const one = roleRow(row, taken);
    if (one !== undefined) {
      taken.add(one.id.toLowerCase());
      rows.push(one);
    }
  }

  return rows;
}

function roleRow(raw: unknown, taken: ReadonlySet<string>): RoleRow | undefined {
  if (typeof raw !== 'object' || raw === null) {
    return undefined;
  }

  const row = raw as Record<string, unknown>;
  const id = typeof row['id'] === 'string' ? row['id'] : '';
  if (!ROLE_ID.test(id) || taken.has(id.toLowerCase())) {
    return undefined;
  }

  return {
    id,
    ...(typeof row['name'] === 'string' ? { name: row['name'] } : {}),
    ...(typeof row['stage'] === 'string' ? { stage: row['stage'] } : {}),
    ...(typeof row['programmingTask'] === 'boolean' ? { programmingTask: row['programmingTask'] } : {}),
    ...(typeof row['active'] === 'boolean' ? { active: row['active'] } : {}),
    ...(Array.isArray(row['prompts']) ? { prompts: promptRows(row['prompts']) } : {}),
  };
}

function promptRows(raw: readonly unknown[]): readonly PromptRow[] {
  const rows: PromptRow[] = [];
  for (const one of raw) {
    if (typeof one !== 'object' || one === null) {
      continue;
    }

    const row = one as Record<string, unknown>;
    const id = typeof row['id'] === 'string' ? row['id'] : '';
    if (!PROMPT_ID.test(id) || RESERVED.has(id.toLowerCase())) {
      continue;
    }

    rows.push({
      id,
      ...(typeof row['label'] === 'string' ? { label: row['label'] } : {}),
      ...(typeof row['purpose'] === 'string' ? { purpose: row['purpose'] } : {}),
    });
  }

  return rows;
}

/**
 * A latin id for a name a person wrote, unique against everything already taken.
 *
 * <p><b>Generated, never asked for.</b> An id is a settings key, a session-file field and a column in
 * every rounds-database row — and it becomes `COAI_ROUNDS_&lt;ID&gt;`, which is why it carries no
 * hyphen. None of that is a person's problem, and a name in Cyrillic or Chinese is a name, not a
 * mistake: where the letters cannot make a latin id, `Role2` does, and the NAME still says what the
 * person meant.</p>
 *
 * <p>It is fixed at creation. Renaming a role never moves it, because by then it may key a stored
 * value, an open session and rows already written.</p>
 */
export function idFor(name: string, taken: ReadonlySet<string>): string {
  const letters = [...name].filter((c) => /[A-Za-z0-9 ]/.test(c)).join('');
  const camel = letters
    .split(' ')
    .filter((word) => word.length > 0)
    .map((word) => word[0]!.toUpperCase() + word.slice(1))
    .join('');
  const base = ROLE_ID.test(camel) ? camel : '';

  return unique(base, taken);
}

function unique(base: string, taken: ReadonlySet<string>): string {
  const spoken = (id: string): boolean => taken.has(id.toLowerCase()) || isBuiltIn(id);
  if (base.length > 0 && !spoken(base)) {
    return base;
  }

  const stem = base.length > 0 ? base : 'Role';
  for (let n = 2; n < 1000; n += 1) {
    if (!spoken(`${stem}${n}`)) {
      return `${stem}${n}`;
    }
  }

  return `${stem}${Date.now()}`;
}

/** A prompt id for a prompt a person added to a role, unique across the WHOLE catalog. */
export function promptIdFor(roleId: string, label: string, taken: ReadonlySet<string>): string {
  const slug = [...label.toLowerCase()]
    .map((c) => (/[a-z0-9]/.test(c) ? c : '-'))
    .join('')
    .replace(/-+/g, '-')
    .replace(/^-|-$/g, '');
  const stem = PROMPT_ID.test(slug) && !RESERVED.has(slug) ? `${roleId.toLowerCase()}-${slug}` : `${roleId.toLowerCase()}-prompt`;
  const base = PROMPT_ID.test(stem) ? stem : 'prompt';
  if (!taken.has(base)) {
    return base;
  }

  for (let n = 2; n < 1000; n += 1) {
    if (!taken.has(`${base}-${n}`)) {
      return `${base}-${n}`;
    }
  }

  return `${base}-${Date.now()}`;
}

/** Every prompt id in use — the shipped ones and the ones a person added. */
export function promptIdsInUse(rows: readonly RoleRow[]): ReadonlySet<string> {
  const ids = new Set<string>();
  for (const role of BUILTIN_ROLES) {
    for (const prompt of role.prompts) {
      ids.add(prompt.id);
    }
  }
  for (const row of rows) {
    for (const prompt of row.prompts ?? []) {
      ids.add(prompt.id);
    }
  }

  return ids;
}

/** Which stage a row reviews in, falling back to the built-in's or to the result stage. */
export function stageOf(row: RoleRow): string {
  return row.stage ?? builtInFor(row.id)?.stage ?? RESULT_STAGE;
}

/** Whether a row takes part at all, defaulting to yes as the server defaults it. */
export function isActive(row: RoleRow): boolean {
  return row.active ?? true;
}

/**
 * How many roles are active in one stage — the shipped ones, as the configured rows leave them.
 *
 * <p>Counted over the composed picture rather than over the rows, because a row that says nothing
 * about a built-in leaves it active and a row that says `active: false` switches it off. The
 * server's cap is over the same picture, so a count taken any other way would disagree with it.</p>
 */
export function activeCount(rows: readonly RoleRow[], stage: string): number {
  return composed(rows).filter((r) => stageOf(r) === stage && isActive(r)).length;
}

/**
 * Every role that exists: the shipped five with any row's edits applied, then the rows a person
 * added, in that order — which is the order the server composes and the panel draws.
 */
export function composed(rows: readonly RoleRow[]): readonly RoleRow[] {
  const byId = new Map(rows.map((r) => [r.id.toLowerCase(), r]));
  const shipped = BUILTIN_ROLES.map((role) => {
    const row = byId.get(role.id.toLowerCase());

    return {
      id: role.id,
      name: row?.name ?? role.name,
      stage: row?.stage ?? role.stage,
      programmingTask: row?.programmingTask ?? role.programmingTask,
      active: row?.active ?? true,
      prompts: [...role.prompts, ...(row?.prompts ?? []).filter((p) => !shippedPrompt(role, p.id))],
    } satisfies RoleRow;
  });

  return [...shipped, ...rows.filter((r) => !isBuiltIn(r.id))];
}

function shippedPrompt(role: RoleDefinition, promptId: string): boolean {
  return role.prompts.some((p) => p.id === promptId);
}
