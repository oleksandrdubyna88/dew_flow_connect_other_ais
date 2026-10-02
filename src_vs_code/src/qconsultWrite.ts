import { type Admission, admit } from './capabilityAdmission';
import { SHIPPED_QUESTION_PROMPTS } from './questionPrompts.generated';
import { MAX_ACTIVE_ROWS, type QuestionPromptSetting, type QuestionRowSetting } from './qconsultSettings';
import { VENDOR_PRESETS, type Vendor, normaliseId } from './vendors';

/**
 * The Question consultant section's DECISIONS and its WRITE path (todo/PLAN_question_consultant.md, S4) —
 * which pair a row may run, when its switch may be turned on, and what an edit stores.
 *
 * <p>Pure and `vscode`-free, like `consultantWrite.ts` beside it: the section draws what these answer and the
 * host stores what they return, so the rule is a unit test rather than markup. Every refusal here is ALSO a
 * refusal the server makes (`QuestionRows.Parse`, `CapabilityMatrix.Admit`, `QuestionAdmission`, D14 c), so a
 * hand-edited `settings.json` meets the same answer — the panel only says it first.</p>
 */

/** A base prompt as the section lists it: the shipped three, then a person's own. */
export interface QuestionPromptView {
  readonly id: string;
  readonly title: string;
  readonly capability: string;
  /** The shipped words, or a custom prompt's own. A shipped prompt's OVERRIDE is a file the host reads beside this. */
  readonly text: string;
  readonly shipped: boolean;
}

/** Every prompt a row may name — the server's `QuestionPromptSet.With`: a custom id never shadows a shipped one. */
export function questionPrompts(custom: readonly QuestionPromptSetting[]): readonly QuestionPromptView[] {
  const shipped = SHIPPED_QUESTION_PROMPTS.map((p) => ({ ...p, shipped: true }));

  return [...shipped, ...custom.filter((c) => !shipped.some((s) => s.id === c.id)).map((c) => ({ ...c, shipped: false }))];
}

/**
 * The runtime a row runs on: its own, or — empty, the server's rule — the reviewer row its vendor names, or the
 * catalogue entry of that id. Empty when nothing says.
 */
export function runtimeOfRow(row: QuestionRowSetting, vendors: readonly Vendor[]): string {
  return row.runtime.length > 0 ? row.runtime : borrowedRuntime(row.vendor, vendors);
}

function borrowedRuntime(vendor: string, vendors: readonly Vendor[]): string {
  const named = vendors.find((v) => v.id === vendor) ?? VENDOR_PRESETS.find((p) => p.id === vendor);

  return named?.runtime ?? '';
}

/** What the capability table says of a row's pair, or why the row has no pair at all. */
export function rowAdmission(row: QuestionRowSetting, prompts: readonly QuestionPromptView[], vendors: readonly Vendor[]): Admission {
  const prompt = prompts.find((p) => p.id === row.prompt);

  return prompt === undefined
    ? { admitted: false, standing: '', flag: '', caveat: '', reason: noPrompt(row) }
    : admit(runtimeOfRow(row, vendors), prompt.capability);
}

function noPrompt(row: QuestionRowSetting): string {
  return row.prompt.length === 0
    ? 'this row has no prompt — a row is a model and exactly one base prompt; pick one'
    : `this row names the prompt '${row.prompt}', which is not one of the prompts below — pick one that is`;
}

/**
 * Why a row that is OFF may not be switched on — or empty when it may. In the order a person would fix it:
 * the prompt, the pair, the acknowledgement (D13), the cap of six.
 */
export function enableBlocker(
  row: QuestionRowSetting,
  rows: readonly QuestionRowSetting[],
  prompts: readonly QuestionPromptView[],
  vendors: readonly Vendor[],
): string {
  const admission = rowAdmission(row, prompts, vendors);
  if (!admission.admitted) {
    return admission.reason;
  }
  return admission.flag.length > 0 && !row.acknowledged
    ? 'this pair can read this machine — tick the acknowledgement below before switching it on'
    : capBlocker(row, rows);
}

function capBlocker(row: QuestionRowSetting, rows: readonly QuestionRowSetting[]): string {
  return rows.filter((r) => r.enabled && r.id !== row.id).length >= MAX_ACTIVE_ROWS
    ? `${MAX_ACTIVE_ROWS} rows are on, the most that may run at once — switch another off first`
    : '';
}

/**
 * A new row, appended OFF: a model of the first catalogue entry that can answer, paired with the first prompt
 * that pair admits — so a row is never stored without a prompt, and adding one never breaks the cap.
 */
export function rowAdded(rows: readonly QuestionRowSetting[], prompts: readonly QuestionPromptView[]): readonly QuestionRowSetting[] {
  const preset = VENDOR_PRESETS.find((p) => p.id === 'claude') ?? VENDOR_PRESETS[0]!;

  return [...rows, {
    id: freeId(rows, preset.id),
    vendor: preset.id, runtime: preset.runtime, model: preset.model, baseUrl: preset.baseUrl, executablePath: '', key: '',
    prompt: firstAdmitted(preset.runtime, prompts), enabled: false, acknowledged: false,
  }];
}

function firstAdmitted(runtime: string, prompts: readonly QuestionPromptView[]): string {
  return (prompts.find((p) => admit(runtime, p.capability).admitted) ?? prompts[0])?.id ?? '';
}

/** `claude-1`, `claude-2`… — the first id no row holds. Row ids name records and ledger lines, so two rows never share one. */
function freeId(rows: readonly QuestionRowSetting[], base: string): string {
  let n = 1;
  while (rows.some((r) => r.id === `${base}-${n}`)) {
    n += 1;
  }

  return `${base}-${n}`;
}

export function rowRemoved(rows: readonly QuestionRowSetting[], id: string): readonly QuestionRowSetting[] {
  return rows.filter((r) => r.id !== id);
}

/** The commands the section's buttons post — spread into `PANEL_COMMANDS`, whose switch is checked for exhaustiveness. */
export const QCONSULT_COMMANDS = [
  'qconsultAddRow', 'qconsultRemoveRow', 'qconsultAddPrompt', 'qconsultRemovePrompt', 'qconsultRestorePrompt',
  'qconsultAddRoot', 'qconsultRemoveRoot',
] as const;

export type QconsultCommand = (typeof QCONSULT_COMMANDS)[number];

export function isQconsultCommand(value: string): value is QconsultCommand {
  return (QCONSULT_COMMANDS as readonly string[]).includes(value);
}

/** The keys a row's controls carry, and which field each one edits. */
export const ROW_KEYS = [
  'qconsultRowVendor', 'qconsultRowModel', 'qconsultRowBaseUrl', 'qconsultRowExecutablePath', 'qconsultRowKey',
  'qconsultRowPrompt', 'qconsultRowEnabled', 'qconsultRowAcknowledged',
] as const;

export type RowKey = (typeof ROW_KEYS)[number];

export function isRowKey(key: string): key is RowKey {
  return (ROW_KEYS as readonly string[]).includes(key);
}

/** What one edit is given to decide with. */
export interface RowEditContext {
  readonly prompts: readonly QuestionPromptView[];
  readonly vendors: readonly Vendor[];
}

/**
 * One control of one row changed: the rows after it, or `undefined` when the edit is REFUSED — the host then
 * snaps the control back rather than storing a row the server would refuse.
 */
export function rowEdited(
  rows: readonly QuestionRowSetting[],
  id: string,
  key: RowKey,
  value: unknown,
  context: RowEditContext,
): readonly QuestionRowSetting[] | undefined {
  const row = rows.find((r) => r.id === id);
  if (row === undefined) {
    return undefined;
  }
  const next = EDITS[key](row, value, rows, context);

  return next === undefined ? undefined : rows.map((r) => (r.id === id ? next : r));
}

type Edit = (row: QuestionRowSetting, value: unknown, rows: readonly QuestionRowSetting[], context: RowEditContext) => QuestionRowSetting | undefined;

const text = (value: unknown): string => (typeof value === 'string' ? value.trim() : '');

/** One rule per key — a table rather than a switch, so a ninth key is a compile error here and not a silent no-op. */
const EDITS: Readonly<Record<RowKey, Edit>> = {
  qconsultRowVendor: (row, value) => vendorChosen(row, text(value)),
  qconsultRowModel: (row, value) => ({ ...row, model: text(value) }),
  qconsultRowBaseUrl: (row, value) => ({ ...row, baseUrl: text(value) }),
  qconsultRowExecutablePath: (row, value) => ({ ...row, executablePath: text(value) }),
  qconsultRowKey: (row, value) => ({ ...row, key: text(value).toLowerCase() }),
  qconsultRowPrompt: (row, value, _rows, context) => promptChosen(row, text(value), context),
  qconsultRowEnabled: (row, value, rows, context) => switched(row, value === true, rows, context),
  qconsultRowAcknowledged: (row, value) => acknowledged(row, value === true),
};

/**
 * A catalogue entry chosen: the row becomes that entry — runtime, model, endpoint — and stays OFF until it is
 * switched on again, unacknowledged, because the pair it was acknowledged for is gone.
 */
function vendorChosen(row: QuestionRowSetting, id: string): QuestionRowSetting | undefined {
  const preset = VENDOR_PRESETS.find((p) => p.id === id && p.id.length > 0);
  if (preset === undefined) {
    return undefined;
  }

  return id === row.vendor ? row : {
    ...row, vendor: preset.id, runtime: preset.runtime, model: preset.model, baseUrl: preset.baseUrl, executablePath: '', key: '',
    enabled: false, acknowledged: false,
  };
}

/** A prompt chosen — never none, never one the pair cannot run (A3) — and the acknowledgement goes with the old pair. */
function promptChosen(row: QuestionRowSetting, id: string, context: RowEditContext): QuestionRowSetting | undefined {
  const candidate = { ...row, prompt: id };
  if (id.length === 0 || !rowAdmission(candidate, context.prompts, context.vendors).admitted) {
    return undefined;
  }

  return id === row.prompt ? row : { ...candidate, enabled: false, acknowledged: false };
}

function switched(row: QuestionRowSetting, on: boolean, rows: readonly QuestionRowSetting[], context: RowEditContext): QuestionRowSetting | undefined {
  if (!on) {
    return { ...row, enabled: false };
  }

  return enableBlocker(row, rows, context.prompts, context.vendors).length === 0 ? { ...row, enabled: true } : undefined;
}

/** The tick taken away switches the row off too: the server refuses a flagged row without it (S3), so it could not run. */
function acknowledged(row: QuestionRowSetting, on: boolean): QuestionRowSetting {
  return on ? { ...row, acknowledged: true } : { ...row, acknowledged: false, enabled: false };
}

// ---------- the prompts ----------

/** What an edit of a SHIPPED prompt's box means for its override file: words to keep, or the override taken away. */
export type PromptFileWrite = { readonly kind: 'write'; readonly text: string } | { readonly kind: 'remove' };

/**
 * An emptied box, or one holding exactly the shipped words, REMOVES the override — so the prompt goes on
 * following the shipped text in the next release. Anything else is written verbatim.
 */
export function shippedPromptWrite(id: string, raw: unknown): PromptFileWrite {
  const typed = typeof raw === 'string' ? raw : '';

  return isTheDefault(id, typed) ? { kind: 'remove' } : { kind: 'write', text: typed };
}

function isTheDefault(id: string, typed: string): boolean {
  return typed.trim().length === 0 || typed.trim() === shippedText(id);
}

/** The words a shipped prompt comes with, or empty for an id that is not one. */
export function shippedText(id: string): string {
  return SHIPPED_QUESTION_PROMPTS.find((p) => p.id === id)?.text ?? '';
}

/** Where a shipped prompt's override lives, relative to the data directory — `RolePrompts.OverrideDir` + the id. */
export function promptOverridePath(id: string): readonly string[] {
  return ['prompts', `${id}.md`];
}

/** Whether an id names a SHIPPED prompt, the only kind with an override file and a default to restore. */
export function isShippedPrompt(id: string): boolean {
  return SHIPPED_QUESTION_PROMPTS.some((p) => p.id === id);
}

/** A custom prompt's text edited — empty is refused, because a row paired with it would be launched with nothing to say. */
export function customPromptEdited(custom: readonly QuestionPromptSetting[], id: string, raw: unknown): readonly QuestionPromptSetting[] | undefined {
  const typed = text(raw);

  return typed.length === 0 || !custom.some((p) => p.id === id)
    ? undefined
    : custom.map((p) => (p.id === id ? { ...p, text: typed } : p));
}

/**
 * A prompt a person adds: an override-file id from its title, one of the three capabilities, and a first text to
 * edit. Refused — with the sentence — for a title that makes no id, an id already taken, or a capability the table
 * does not know.
 */
export function promptAdded(
  custom: readonly QuestionPromptSetting[],
  title: string,
  capability: string,
): { readonly prompts: readonly QuestionPromptSetting[]; readonly refusal: string } {
  const id = normaliseId(title);
  const refusal = newPromptRefusal(id, capability, custom);

  return refusal.length > 0
    ? { prompts: custom, refusal }
    : { prompts: [...custom, { id, title: title.trim(), capability, text: `Answer the question as ${title.trim()}.` }], refusal: '' };
}

function newPromptRefusal(id: string, capability: string, custom: readonly QuestionPromptSetting[]): string {
  const checks: readonly (readonly [boolean, string])[] = [
    [id.length === 0, 'a prompt needs a title that makes an id — letters or digits'],
    [questionPrompts(custom).some((p) => p.id === id), `'${id}' is already a prompt — one id is one prompt`],
    [!['none', 'disk', 'web'].includes(capability), `'${capability}' is not a capability — none, disk or web`],
  ];

  return checks.find(([refused]) => refused)?.[1] ?? '';
}

/** A custom prompt removed — refused while a row still runs it, because that row would be dropped by the server with it. */
export function promptRemoved(
  custom: readonly QuestionPromptSetting[],
  rows: readonly QuestionRowSetting[],
  id: string,
): { readonly prompts: readonly QuestionPromptSetting[]; readonly refusal: string } {
  const users = rows.filter((r) => r.prompt === id).map((r) => r.id);

  return users.length > 0
    ? { prompts: custom, refusal: `the prompt '${id}' is the prompt of ${users.join(', ')} — give ${users.length === 1 ? 'that row' : 'those rows'} another prompt first` }
    : { prompts: custom.filter((p) => p.id !== id), refusal: '' };
}

// ---------- the roots (D14 c) ----------

/** The places a disk root may not be, on THIS machine — handed in, so a test can name its own. */
export interface RootPlaces {
  readonly dataDir: string;
  readonly profile: string;
  readonly systemDirs: readonly string[];
  /** Windows compares paths without case; elsewhere case is part of the name. */
  readonly caseless: boolean;
}

/**
 * Why a folder may not be a disk root, or empty — the server's `QuestionRoots.WhyNot`, checked before it is
 * stored: not absolute, a drive root, the profile itself, inside the data directory, a system directory — and
 * since S4b item 2 a folder that CONTAINS the profile, the data folder or a system folder, and a credential
 * folder, anything inside one, or a folder holding one. Existence and links are the host's to resolve
 * (`rootAdded`'s `real`): this is pure.
 */
export function rootRefusal(root: string, places: RootPlaces): string {
  const full = trimmed(root);
  const credential = credentialIn(full, places);
  const checks: readonly (readonly [boolean, string])[] = [
    [!isAbsolute(full), `'${root}' is not an absolute path — a disk root is spelled from a drive or from /`],
    [isDriveRoot(full), `'${full}' is a drive root — a disk row would read the whole disk; name the project folders instead`],
    [same(full, places.profile, places), `'${full}' is your profile folder itself — every file of yours; name the project folders under it instead`],
    [above(full, places.profile, places), `'${full}' contains your profile folder — every file of yours; name the project folders instead`],
    [within(full, places.dataDir, places), `'${full}' is inside the data folder — the records, the ledger and the sessions are not another project to read`],
    [above(full, places.dataDir, places), `'${full}' contains the data folder — the records, the ledger and the sessions are not another project to read`],
    [places.systemDirs.some((dir) => within(full, dir, places)), `'${full}' is a system folder — not a project, and not a disk row's to read`],
    [places.systemDirs.some((dir) => above(full, dir, places)), `'${full}' contains a system folder — not a project, and not a disk row's to read`],
    [credential.length > 0, `'${full}' is a credential folder (${credential}) or inside one — keys and vendor sign-ins are not a project to read`],
    [credentialsUnder(places).some((dir) => above(full, dir, places)), `'${full}' contains a credential folder — keys and vendor sign-ins are not a project to read`],
  ];

  return checks.find(([refused]) => refused)?.[1] ?? '';
}

/** The credential folder a path is, or is inside, by its segments — `.ssh` anywhere, `.config/gcloud` as a pair. */
function credentialIn(path: string, places: RootPlaces): string {
  const segments = `/${comparable(path, places).split('/').filter((part) => part.length > 0).join('/')}/`;

  return CREDENTIAL_DIRECTORIES.find((dir) => segments.includes(`/${places.caseless ? dir.toLowerCase() : dir}/`)) ?? '';
}

/** The credential folders under the profile — a folder holding one (the profile's `.config`) is refused too. */
function credentialsUnder(places: RootPlaces): readonly string[] {
  return places.profile.trim().length === 0 ? [] : CREDENTIAL_DIRECTORIES.map((dir) => `${trimmed(places.profile)}/${dir}`);
}

/** The folders a root may never be, be inside, or contain — the server's `QuestionRoots.CredentialDirectories`, held level by a test. */
export const CREDENTIAL_DIRECTORIES: readonly string[] = ['.ssh', '.aws', '.gnupg', '.config/gcloud', '.claude', '.codex', '.azure'];

/**
 * A root added — or the sentence refusing it. Two spellings of one folder are one root.
 *
 * <p>`real` is what the picked folder resolves to through its junctions and symlinks (the host asks `realpath`):
 * a link to the profile IS the profile, so the root is refused when either spelling is (S4b item 2). What is stored
 * is the folder as it was picked.</p>
 */
export function rootAdded(
  roots: readonly string[],
  root: string,
  places: RootPlaces,
  real: string = root,
): { readonly roots: readonly string[]; readonly refusal: string } {
  const atTarget = same(real, root, places) ? '' : rootRefusal(real, places);
  const refusal = rootRefusal(root, places) || (atTarget.length > 0 ? `${atTarget} ('${trimmed(root)}' resolves to it)` : '');
  if (refusal.length > 0) {
    return { roots, refusal };
  }

  return roots.some((r) => same(r, root, places)) ? { roots, refusal: '' } : { roots: [...roots, trimmed(root)], refusal: '' };
}

export function rootRemoved(roots: readonly string[], root: string): readonly string[] {
  return roots.filter((r) => r !== root);
}

/** No trailing separator — but a bare drive keeps its slash, because `C:` alone means "the current folder on C". */
function trimmed(path: string): string {
  const t = path.trim().replace(/[\\/]+$/, '');

  return /^[A-Za-z]:$/.test(t) || t.length === 0 ? `${t}/` : t;
}

function isAbsolute(path: string): boolean {
  return /^[A-Za-z]:[\\/]/.test(path) || /^[\\/]/.test(path);
}

function isDriveRoot(path: string): boolean {
  return /^[A-Za-z]:[\\/]?$/.test(path) || /^[\\/]+$/.test(path) || /^[\\/]{2}[^\\/]+[\\/][^\\/]+$/.test(path);
}

function comparable(path: string, places: RootPlaces): string {
  const slashed = trimmed(path).replace(/\\/g, '/');

  return places.caseless ? slashed.toLowerCase() : slashed;
}

function same(one: string, other: string, places: RootPlaces): boolean {
  return other.trim().length > 0 && comparable(one, places) === comparable(other, places);
}

/** Whether `path` is an ANCESTOR of `place` — reading it reads the place too (S4b item 2). */
function above(path: string, place: string, places: RootPlaces): boolean {
  return place.trim().length > 0 && !same(path, place, places) && within(place, path, places);
}

function within(path: string, parent: string, places: RootPlaces): boolean {
  if (parent.trim().length === 0) {
    return false;
  }
  const p = comparable(parent, places);

  return comparable(path, places) === p || comparable(path, places).startsWith(`${p.replace(/\/$/, '')}/`);
}
