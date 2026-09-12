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

/**
 * The longest id this will generate.
 *
 * <p>An id becomes `COAI_ROUNDS_&lt;ID&gt;`, `COAI_THRESHOLD_&lt;ID&gt;` and `COAI_ENABLED_&lt;ID&gt;`. A
 * name pasted out of a document is a name a person can write, and the id made from it used to be as
 * long as they made it — a variable no shell would carry, so the role's budget would read from the
 * settings file and never from the block they paste into an MCP client. Working in one of the two
 * places a setting can come from is worse than working in neither, because only one of those is
 * noticed.</p>
 *
 * <p><b>This is a POLICY cap, not a measured platform number, and the difference matters.</b>
 * `common/platform-limits.md` requires a kernel limit to come from a probe that was run — there is no
 * single such number here: POSIX sets no maximum on a variable NAME, Windows allows far more than
 * this, and the shells and MCP clients in between disagree. So this is a bound chosen to be
 * comfortably under all of them and still readable in a settings file, held in one constant that
 * every check and every message derives from. If a platform is ever found that refuses something
 * shorter, THAT number is probed and replaces this one.</p>
 */
export const MAX_ROLE_ID_LENGTH = 48;

/** Room left under the cap for what {@link unique} appends when an id is already spoken for. */
const ID_SUFFIX_ROOM = 14;

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
    ...unknownFields(row, ROW_FIELDS),
    id,
    ...(typeof row['name'] === 'string' ? { name: row['name'] } : {}),
    ...(typeof row['stage'] === 'string' ? { stage: row['stage'] } : {}),
    ...(typeof row['programmingTask'] === 'boolean' ? { programmingTask: row['programmingTask'] } : {}),
    ...(typeof row['active'] === 'boolean' ? { active: row['active'] } : {}),
    ...(Array.isArray(row['prompts']) ? { prompts: promptRows(row['prompts']) } : {}),
  };
}

/** The fields of a row THIS build knows about. Everything else is carried, not understood. */
const ROW_FIELDS: ReadonlySet<string> = new Set(['id', 'name', 'stage', 'programmingTask', 'active', 'prompts']);

/** The fields of a prompt row this build knows about. */
const PROMPT_FIELDS: ReadonlySet<string> = new Set(['id', 'label', 'purpose']);

/** Keys that are not data, whatever a hand-written settings file or a JSON payload calls them. */
const NOT_A_FIELD: ReadonlySet<string> = new Set(['__proto__', 'constructor', 'prototype']);

/**
 * Whatever else the row was carrying.
 *
 * <p><b>The setting IS the wire format, and this is what makes that true in both directions.</b> The
 * panel rewrites the whole array on every edit, so a field the SERVER gains before this extension
 * does would be deleted by the first keystroke on the roles page — the person's configuration
 * quietly losing a feature they had set up, with nothing anywhere saying so. That is not a
 * hypothetical: `remoteVendor` was dropped by three releases of this extension exactly that way, one
 * unexplained 400 at a time.</p>
 *
 * <p>Carried, never interpreted. A misspelt field survives too, which is the price — and the cheaper
 * of the two mistakes, because the server names what it refused and the panel shows that sentence.
 * `__proto__` and its family are not carried at all: `JSON.parse` makes one an OWN property, so
 * spreading a parsed row is a prototype write nobody wrote.</p>
 */
function unknownFields(row: Record<string, unknown>, known: ReadonlySet<string>): Record<string, unknown> {
  const kept: Record<string, unknown> = {};
  for (const key of Object.getOwnPropertyNames(row)) {
    if (!known.has(key) && !NOT_A_FIELD.has(key)) {
      kept[key] = row[key];
    }
  }

  return kept;
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
      ...unknownFields(row, PROMPT_FIELDS),
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
  const short = camel.slice(0, MAX_ROLE_ID_LENGTH - ID_SUFFIX_ROOM);
  const base = ROLE_ID.test(short) ? short : '';

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
 *
 * <p><b>A row naming a shipped role may say exactly two things, and this takes exactly those two.</b>
 * Its switch, and the prompts it adds. `RoleComposition.Overridden` on the server is the boundary
 * that decides, and its own comment says why about the rest: <i>"Id, name, stage and kind are NEVER
 * taken from the row. A built-in cannot be renamed — its id keys settings, session files and every
 * row of the rounds database."</i></p>
 *
 * <p>This used to take the name, the stage and the kind as well. Nothing failed, which is the
 * interesting part: the page would draw the person's word for the role, the round would use the
 * shipped one, and the only place the two met was a log nobody reads next to a panel nobody
 * doubted. A panel that shows what the server will NOT do is worse than one that shows nothing —
 * found on this plan's code round by three reviewers at once.</p>
 */
export function composed(rows: readonly RoleRow[]): readonly RoleRow[] {
  const byId = new Map(rows.map((r) => [r.id.toLowerCase(), r]));
  const shipped = BUILTIN_ROLES.map((role) => {
    const row = byId.get(role.id.toLowerCase());

    return {
      id: role.id,
      name: role.name,
      stage: role.stage,
      programmingTask: role.programmingTask,
      active: row?.active ?? true,
      prompts: [...role.prompts, ...(row?.prompts ?? []).filter((p) => !shippedPrompt(role, p.id))],
    } satisfies RoleRow;
  });

  return [...shipped, ...rows.filter((r) => !isBuiltIn(r.id)).map(materialised)];
}

/**
 * A row with every field the catalog promises, filled in as the server would read it.
 *
 * <p>A built-in came out of this function complete and a person's own role came out exactly as it
 * was STORED — so `role.active` was a boolean on one and `undefined` on the other, and anything
 * reading it directly would read a live custom role as switched off. Every caller was carrying its
 * own `?? true` to compensate, which is what a leaky shape looks like from the outside. One shape
 * now, whoever wrote the role. (gemini, the second code round.)</p>
 *
 * <p>The spread comes FIRST, so a field this build does not know is carried through the catalog the
 * same way `rolesFrom` carries it through the parser.</p>
 */
function materialised(row: RoleRow): RoleRow {
  return {
    ...row,
    name: row.name ?? row.id,
    stage: stageOf(row),
    programmingTask: row.programmingTask ?? true,
    active: isActive(row),
    prompts: row.prompts ?? [],
  };
}

function shippedPrompt(role: RoleDefinition, promptId: string): boolean {
  return role.prompts.some((p) => p.id === promptId);
}
