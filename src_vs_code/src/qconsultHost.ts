import * as fs from 'node:fs';
import * as os from 'node:os';
import * as vscode from 'vscode';
import { ConsultPromptFile } from './consultPromptFile';
import { notify } from './notify';
import { RootExistence, directoryAt, systemDriveOf } from './pathFamily';
import { keepsItsOwnRefusal } from './pathSides';
import { askPerson } from './personWait';
import { type QconsultSettings, qconsultSettingsFrom } from './qconsultSettings';
import { SHIPPED_QUESTION_PROMPTS } from './questionPrompts.generated';
import {
  type QconsultCommand,
  type RootPlaces,
  customPromptEdited,
  isRowKey,
  isShippedPrompt,
  promptAdded,
  promptOverridePath,
  promptRemoved,
  questionPrompts,
  rootAdded,
  rootRemoved,
  rowAdded,
  rowEdited,
  rowRemoved,
  shippedPromptWrite,
} from './qconsultWrite';
import type { Vendor } from './vendors';
import { TYPED_ROOT_KEY, typedRootRefusal } from './typedRoots';
import { rootBoxKey, type TypedRefusal } from './typedRefusals';

/** Whether a routed `caller`-kind write belongs to this section: a row's control, a prompt's box, or the typed folder box. */
export function isQconsultCallerKey(key: string): boolean {
  return isRowKey(key) || BOX_KEYS.has(key);
}

/** The boxes whose text is handled whole rather than as a row's field: a prompt's words, and a folder of the other side. */
const BOX_KEYS: ReadonlySet<string> = new Set(['qconsultPromptText', TYPED_ROOT_KEY]);

/** The other side's typed folder box (E4.2) — the key its refusal is kept under. */
const TYPED_BOX = rootBoxKey('qconsultRoots', 'otherSide');

/** What a press of Enter in the typed box decided: the roots to store, or the refusal to draw beside the box — or neither (empty). */
interface TypedVerdict {
  readonly roots?: readonly string[];
  readonly refusal?: TypedRefusal;
}

const NOTHING_TYPED: TypedVerdict = {};

/** A typed root while this side keeps its own folders: refused, whatever it says — an empty box saves nothing either. */
function keptHere(root: string): TypedVerdict {
  return root.length === 0 ? NOTHING_TYPED : { refusal: { typed: root, reason: keepsItsOwnRefusal(root) } };
}

/** What a write is handed by the panel: the side-aware reader, the vendor rows, and how to store or refuse. */
export interface QconsultWriteHooks {
  readonly read: (section: string) => unknown;
  readonly vendors: readonly Vendor[];
  /** Stores one setting through the panel's own write path, which snaps the control back if the save is refused. */
  readonly save: (key: string, value: unknown) => Promise<void>;
  /** Repaints from what is stored — an edit refused here leaves the control where it was. */
  readonly snapBack: () => Promise<void>;
  /** Keeps (or forgets, with undefined) what was typed into a box and refused, so the next paint draws it beside the box. */
  readonly besideTheBox: (box: string, refusal: TypedRefusal | undefined) => void;
  /** Whether a key is saved into THIS side's own settings (`sideConfig.writesTheOverlay`) — which the other side never reads. */
  readonly keptHere: (key: string) => boolean;
}

/**
 * The host half of the Question consultant section (todo/PLAN_question_consultant.md, S4): the buttons, the
 * routed writes, the shipped prompts' override files and this machine's places a disk root may not be.
 *
 * <p>Its own module so `panelProvider.ts` — far past the 800 lines `coding-style.md` allows, with a plan to
 * shrink it — gains a field and three hand-offs rather than a feature. Every DECISION is `qconsultWrite.ts`'s
 * and pure; what is here is the VS Code around it.</p>
 */
export class QconsultHost {
  private readonly prompts: ReadonlyMap<string, ConsultPromptFile>;

  constructor(private readonly dataDir: vscode.Uri) {
    this.prompts = new Map(SHIPPED_QUESTION_PROMPTS.map((p) => [
      p.id, new ConsultPromptFile(dataDir, promptOverridePath(p.id), `The question prompt '${p.title}'`),
    ]));
  }

  /** Each shipped prompt's override as it is on disk — read at paint, like the consultant's own prompt. */
  async overrides(): Promise<Record<string, string>> {
    const read = await Promise.all([...this.prompts].map(async ([id, file]) => [id, await file.readConsultPrompt()] as const));

    return Object.fromEntries(read);
  }

  /**
   * The places a disk root may not be on THIS machine — the server's `SystemPlaces.Current` and the data folder — and
   * which of the stored `roots` spelled for the other OS are folders here all the same (`/work` on the system drive),
   * asked of the disk only for those and only when the roots, the platform or the drive changed (`RootExistence`), so
   * the page never calls an existing folder the other side's and a repaint stats nothing.
   */
  async places(roots: readonly string[] = []): Promise<RootPlaces> {
    const windows = process.platform === 'win32';
    const system = windows
      ? [process.env['SystemRoot'], process.env['ProgramFiles'], process.env['ProgramFiles(x86)'], process.env['ProgramData']]
      : ['/usr/share'];
    const systemDrive = systemDriveOf(process.env['SystemDrive']);

    return {
      dataDir: this.dataDir.fsPath,
      profile: os.homedir(),
      systemDirs: system.filter((one): one is string => typeof one === 'string' && one.length > 0),
      caseless: windows,
      windows,
      ...await this.existence(roots, windows, systemDrive),
      systemDrive,
    };
  }

  private async existence(roots: readonly string[], windows: boolean, systemDrive: string): Promise<Pick<RootPlaces, 'existingHere' | 'unknownHere'>> {
    const { existing, unknown } = await this.rootAnswers.of(roots, windows, systemDrive);

    return { existingHere: existing, unknownHere: unknown };
  }

  /** The roots setting changed: the next paint asks the disk again (a folder created since is then found). */
  forgetRootAnswers(): void {
    this.rootAnswers.forget();
  }

  /** What the disk said about the stored roots spelled for the other OS — asked one root at a time, and kept. */
  private readonly rootAnswers = new RootExistence(directoryAt((root) => fs.promises.stat(root)));

  /** One control of a row, or one prompt's box, changed. A refused edit snaps back rather than storing what the server would refuse. */
  async write(key: string, id: string, value: unknown, hooks: QconsultWriteHooks): Promise<void> {
    const settings = qconsultSettingsFrom(hooks.read);
    if (BOX_KEYS.has(key)) {
      await this.boxWrite(key, id, value, settings, hooks);
      return;
    }
    const rows = isRowKey(key)
      ? rowEdited(settings.rows, id, key, value, { prompts: questionPrompts(settings.prompts), vendors: hooks.vendors })
      : undefined;
    await (rows === undefined ? hooks.snapBack() : hooks.save('qconsultRows', rows));
  }

  private boxWrite(key: string, id: string, value: unknown, settings: QconsultSettings, hooks: QconsultWriteHooks): Promise<void> {
    return key === TYPED_ROOT_KEY ? this.typedRoot(value, settings, hooks) : this.promptText(id, value, settings, hooks);
  }

  /**
   * A folder typed into the OTHER side's box (todo/PLAN_paths_per_side.md E4.2): admitted only when this window would
   * group it as the other side's (`typedRootRefusal`, the disk asked only at this machine's own spelling of it) and then
   * appended to the one shared list; refused BESIDE the box — the text kept, the reason under it — never as a toast.
   *
   * <p>While this side keeps its own folders (`coai.perSideSettings`) every typed root is refused before the disk is asked:
   * it would be saved into THIS side's own settings, which the other side never reads (E4's own review, item 1). The page
   * draws no typed box then; only a page drawn before the switch moved can post one.</p>
   */
  private async typedRoot(value: unknown, settings: QconsultSettings, hooks: QconsultWriteHooks): Promise<void> {
    const root = typeof value === 'string' ? value.trim() : '';
    const verdict = hooks.keptHere('qconsultRoots') ? keptHere(root) : await this.typedVerdict(root, settings);
    hooks.besideTheBox(TYPED_BOX, verdict.refusal);
    await (verdict.roots === undefined ? hooks.snapBack() : this.addTyped(verdict.roots, hooks));
  }

  private async typedVerdict(root: string, settings: QconsultSettings): Promise<TypedVerdict> {
    return root.length === 0 ? NOTHING_TYPED : this.admitted(root, settings);
  }

  private async admitted(root: string, settings: QconsultSettings): Promise<TypedVerdict> {
    const places = await this.places([...settings.roots, root]);
    const reason = typedRootRefusal(root, places);

    return reason.length > 0 ? { refusal: { typed: root, reason } } : { roots: rootAdded(settings.roots, root, places).roots };
  }

  private addTyped(roots: readonly string[], hooks: QconsultWriteHooks): Promise<void> {
    this.forgetRootAnswers();

    return hooks.save('qconsultRoots', roots);
  }

  private async promptText(id: string, value: unknown, settings: QconsultSettings, hooks: QconsultWriteHooks): Promise<void> {
    const file = this.prompts.get(id);
    if (file !== undefined) {
      await file.savePromptWrite(shippedPromptWrite(id, value));
      return;
    }
    const prompts = customPromptEdited(settings.prompts, id, value);
    await (prompts === undefined ? hooks.snapBack() : hooks.save('qconsultPrompts', prompts));
  }

  /** A button. */
  async run(command: QconsultCommand, id: string, hooks: QconsultWriteHooks): Promise<void> {
    const settings = qconsultSettingsFrom(hooks.read);
    await COMMANDS[command](this, id, settings, hooks);
  }

  /** Restore default: the override file goes, and the shipped words answer again. */
  async restore(id: string): Promise<void> {
    if (isShippedPrompt(id)) {
      await this.prompts.get(id)?.savePromptWrite({ kind: 'remove' });
    }
  }
}

type Command = (host: QconsultHost, id: string, settings: QconsultSettings, hooks: QconsultWriteHooks) => Promise<void>;

/** One handler per button — a table, so a command added to the list without a handler is a compile error. */
const COMMANDS: Readonly<Record<QconsultCommand, Command>> = {
  qconsultAddRow: (_host, _id, settings, hooks) => hooks.save('qconsultRows', rowAdded(settings.rows, questionPrompts(settings.prompts))),
  qconsultRemoveRow: (_host, id, settings, hooks) => hooks.save('qconsultRows', rowRemoved(settings.rows, id)),
  qconsultAddPrompt: (_host, _id, settings, hooks) => addPrompt(settings, hooks),
  qconsultRemovePrompt: (_host, id, settings, hooks) => settled(promptRemoved(settings.prompts, settings.rows, id), 'qconsultPrompts', (r) => r.prompts, hooks),
  qconsultRestorePrompt: (host, id) => host.restore(id),
  qconsultAddRoot: (host, _id, settings, hooks) => { host.forgetRootAnswers(); return addRoot(host, settings, hooks); },
  qconsultRemoveRoot: (host, id, settings, hooks) => { host.forgetRootAnswers(); return hooks.save('qconsultRoots', rootRemoved(settings.roots, id)); },
};

/** A change that may be refused: stored, or its sentence said — never both, never silence. */
async function settled<T extends { readonly refusal: string }>(
  result: T,
  key: string,
  value: (result: T) => unknown,
  hooks: QconsultWriteHooks,
): Promise<void> {
  if (result.refusal.length === 0) {
    await hooks.save(key, value(result));
    return;
  }
  void notify({
    as: 'warning',
    class: 'refusal',
    source: 'questionConsultant',
    code: 'question-consultant-change-refused',
    title: `The question consultant was not changed: ${result.refusal}.`,
  });
}

/** A prompt of your own: its title, then the capability it needs. Dismissing either box adds nothing. */
async function addPrompt(settings: QconsultSettings, hooks: QconsultWriteHooks): Promise<void> {
  const title = await askPerson(() => vscode.window.showInputBox({
    title: 'ConnectOtherAIs — a base prompt of your own',
    prompt: 'Its title — the id is made from it, so it is also the name its rows carry',
    ignoreFocusOut: true,
  }));
  const capability = title === undefined ? undefined : await askPerson(() => vscode.window.showQuickPick(
    [
      { label: 'none', detail: 'Answers from the question and its context alone — any runtime.' },
      { label: 'disk', detail: 'Reads the folders below, read-only — Claude Code, Codex (flagged), Antigravity (flagged).' },
      { label: 'web', detail: 'Searches the internet with the question alone — Claude Code, Codex (flagged).' },
    ],
    { title: 'What this prompt needs of its runtime', ignoreFocusOut: true },
  ));
  if (title !== undefined && capability !== undefined) {
    await settled(promptAdded(settings.prompts, title, capability.label), 'qconsultPrompts', (r) => r.prompts, hooks);
  }
}

/**
 * A folder from the system's own picker — so it exists — and refused by name when it is one of D14 (c)'s places,
 * judged at what its junctions and symlinks resolve to as well (S4b item 2: a link to the profile IS the profile).
 */
async function addRoot(host: QconsultHost, settings: QconsultSettings, hooks: QconsultWriteHooks): Promise<void> {
  const picked = await askPerson(() => vscode.window.showOpenDialog({
    canSelectFiles: false, canSelectFolders: true, canSelectMany: false,
    title: 'A folder a disk row may read, read-only',
  }));
  const folder = picked?.[0];
  if (folder !== undefined) {
    // A folder whose links cannot be followed is judged as it was picked — the server walks them again, and refuses there.
    const real = await fs.promises.realpath(folder.fsPath).catch(() => folder.fsPath);
    await settled(rootAdded(settings.roots, folder.fsPath, await host.places(), real), 'qconsultRoots', (r) => r.roots, hooks);
  }
}
