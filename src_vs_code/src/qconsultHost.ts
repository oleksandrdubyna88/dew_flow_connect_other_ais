import * as fs from 'node:fs';
import * as os from 'node:os';
import * as vscode from 'vscode';
import { ConsultPromptFile } from './consultPromptFile';
import { notify } from './notify';
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

/** Whether a routed `caller`-kind write belongs to this section: a row's control, or a prompt's box. */
export function isQconsultCallerKey(key: string): boolean {
  return isRowKey(key) || key === 'qconsultPromptText';
}

/** What a write is handed by the panel: the side-aware reader, the vendor rows, and how to store or refuse. */
export interface QconsultWriteHooks {
  readonly read: (section: string) => unknown;
  readonly vendors: readonly Vendor[];
  /** Stores one setting through the panel's own write path, which snaps the control back if the save is refused. */
  readonly save: (key: string, value: unknown) => Promise<void>;
  /** Repaints from what is stored — an edit refused here leaves the control where it was. */
  readonly snapBack: () => Promise<void>;
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

  /** The places a disk root may not be on THIS machine — the server's `SystemPlaces.Current` and the data folder. */
  places(): RootPlaces {
    const windows = process.platform === 'win32';
    const system = windows
      ? [process.env['SystemRoot'], process.env['ProgramFiles'], process.env['ProgramFiles(x86)'], process.env['ProgramData']]
      : ['/usr/share'];

    return {
      dataDir: this.dataDir.fsPath,
      profile: os.homedir(),
      systemDirs: system.filter((one): one is string => typeof one === 'string' && one.length > 0),
      caseless: windows,
      windows,
    };
  }

  /** One control of a row, or one prompt's box, changed. A refused edit snaps back rather than storing what the server would refuse. */
  async write(key: string, id: string, value: unknown, hooks: QconsultWriteHooks): Promise<void> {
    const settings = qconsultSettingsFrom(hooks.read);
    if (key === 'qconsultPromptText') {
      await this.promptText(id, value, settings, hooks);
      return;
    }
    const rows = isRowKey(key)
      ? rowEdited(settings.rows, id, key, value, { prompts: questionPrompts(settings.prompts), vendors: hooks.vendors })
      : undefined;
    await (rows === undefined ? hooks.snapBack() : hooks.save('qconsultRows', rows));
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
  qconsultAddRoot: (host, _id, settings, hooks) => addRoot(host, settings, hooks),
  qconsultRemoveRoot: (_host, id, settings, hooks) => hooks.save('qconsultRoots', rootRemoved(settings.roots, id)),
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
    await settled(rootAdded(settings.roots, folder.fsPath, host.places(), real), 'qconsultRoots', (r) => r.roots, hooks);
  }
}
