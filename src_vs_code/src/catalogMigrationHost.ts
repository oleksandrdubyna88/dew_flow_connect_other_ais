import * as vscode from 'vscode';
import { CatalogLayer, LayerWrite, MigrationOptions, MigrationOutcome, restoreLayer, RestoreOutcome } from './catalogMigration';
import { applyWrites, migrateOne, RunLayer, unknownKeysOf } from './catalogMigrationRun';
import { MigrationWait } from './migrationWait';
import { overlayKey } from './coaiInstall';
import { thisSide } from './installer';
import { notify, notifyAndAsk } from './notify';
import { readOverlay } from './sideSettings';
import { CatalogTurns } from './catalogTurns';
import { userChatPresets } from './modelKeys';

/**
 * The host half of the move into the catalog (PLAN_one_model_catalog.md E1.3): read each settings layer, run
 * the pure `migrateLayer`, write what it says in the order it says, and tell the person only what it LEFT —
 * a migration that went through changes nothing they can see, so it says nothing.
 *
 * <p>Two layers per window: the user layer (`settings.json`, read through `inspect().globalValue` so a
 * workspace value is never migrated into it) and, when this side keeps its own settings, this side's overlay.
 * Another side's overlay is migrated by a window on that side. Runs are queued, never concurrent: activation,
 * a configuration change, and a save of a model key each ask for one, and the writes of one run are what the
 * next run reads. They share the catalog's turns with the panel's save ({@link inCatalogTurn}), so neither
 * reads the other half-written; a request before a run starts joins it ({@link CatalogTurns}).</p>
 */

/**
 * The settings whose change may leave a definition to move — the chat presets too (E4.6a): a config import or a hand edit
 * can bring presets back. Not `chatModel`: the move remaps it only in the run that moves the preset it names.
 */
export const MIGRATION_TRIGGERS: readonly string[] = ['vendors', 'consultants', 'qconsultRows', 'bugzModel', 'chatModelPresets'];

/** A layer of this window — the run's shape (`catalogMigrationRun.ts`). */
type Layer = RunLayer;

const turns = new CatalogTurns();

/** What to do once a run has written anything — the activation's mirror of the server settings file. */
let afterMigration: () => void = () => undefined;

/**
 * Whether the installed coai-mcp ranks Bugz by runtime — asked per run, because the binary can be updated while the
 * window is open, and the Bugz model moves only for one that does (PLAN_one_model_catalog.md E2.1).
 */
let bugzByRuntime: () => Promise<boolean> = () => Promise.resolve(false);

/**
 * The one retry, then the one warning, of a move that met a key this window does not know yet
 * (research/PLAN_catalog_migration_waits_for_its_settings.md). Made on activation, disposed with the extension.
 */
let wait: MigrationWait | undefined;

/** Called once on activation: remember what follows a migration and how to ask the binary, and run the first one. */
export function startCatalogMigration(context: vscode.ExtensionContext, after: () => void, ranksByRuntime: () => Promise<boolean>): Promise<void> {
  afterMigration = after;
  bugzByRuntime = ranksByRuntime;
  wait = waitFor(context);
  context.subscriptions.push(wait);

  return scheduleCatalogMigration(context);
}

/** One more run, after any that is under way. Never throws: a failed layer is reported and the other goes on. */
export function scheduleCatalogMigration(context: vscode.ExtensionContext): Promise<void> {
  return turns.migrate(() => migrateEveryLayer(context));
}

/**
 * A write of the catalog's keys that reads them first — the panel's save — taken as one turn, so no migration
 * reads between its read and its last write. Its work must not await a migration (the triggers it fires are `void`).
 */
export function inCatalogTurn<T>(work: () => Promise<T>): Promise<T> {
  return turns.edit(work);
}

async function migrateEveryLayer(context: vscode.ExtensionContext): Promise<void> {
  let wrote = false;
  let waited = false;
  const waiting = (): void => {
    waited = true;
    wait?.wait();
  };
  const options: MigrationOptions = { bugzByRuntime: await bugzByRuntime().catch(() => false) };
  for (const layer of layersOf(context, vscode.workspace.getConfiguration('coai'))) {
    wrote = (await migrateOne(layer, options, { stopped: migrationStopped, left: sayWhatWasLeft, waiting })) || wrote;
  }
  afterThePass(wrote, waited);
}

/**
 * Settled only by a pass in which no layer waited: the side overlay landing beside a waiting user layer is not the move
 * going through, and must not take that layer's retry down. The mirror follows only a pass that wrote.
 */
function afterThePass(wrote: boolean, waited: boolean): void {
  if (!waited) {
    wait?.settled();
  }
  if (wrote) {
    afterMigration();
  }
}

/**
 * The retry runs the move again through the queue, on the next `coai` settings change or after the timer; the warning
 * is said once, and only a press of its button reloads.
 */
function waitFor(context: vscode.ExtensionContext): MigrationWait {
  return new MigrationWait({
    onSettingsChange: (fire) => vscode.workspace.onDidChangeConfiguration((event) => {
      if (event.affectsConfiguration('coai')) {
        fire();
      }
    }),
    after: (ms, fire) => {
      const timer = setTimeout(fire, ms);

      return { dispose: () => clearTimeout(timer) };
    },
    retry: () => {
      void scheduleCatalogMigration(context);
    },
    warn: () => {
      void reloadToMove();
    },
  });
}

const RELOAD = 'Reload Window';

/** A window that will not learn the keys: nothing was moved, nothing is lost, and a reload is the cure. */
async function reloadToMove(): Promise<void> {
  const answer = await notifyAndAsk({
    as: 'warning',
    class: 'refusal',
    source: 'catalogMigrationHost',
    code: 'catalog-migration-waits-for-reload',
    title: 'This window has not loaded the settings of the updated ConnectOtherAIs yet, so your models are moved into '
      + 'the catalog after a reload. Nothing was changed in the meantime.',
    action: RELOAD,
  });
  if (answer === RELOAD) {
    await vscode.commands.executeCommand('workbench.action.reloadWindow');
  }
}

/** A migration stopped part way is finished by the next run, which is what it says. */
async function migrationStopped(layer: Layer, error: unknown): Promise<void> {
  await notify({
    as: 'error',
    class: 'failure',
    source: 'catalogMigrationHost',
    code: 'catalog-migration-failed',
    subject: layer.name,
    title: `Moving the models in ${layer.name} into the catalog stopped part way; nothing that was written is lost, `
      + 'and the next start finishes it.',
    detail: error instanceof Error ? error.message : String(error),
  });
}

/**
 * A restore stopped part way is NOT finished by anything: the layer is marked neither restored nor left as it was.
 * It wrote the definitions first, so every consultant still resolves; running the command again finishes it.
 */
async function restoreStopped(layer: Layer, error: unknown): Promise<void> {
  await notify({
    as: 'error',
    class: 'failure',
    source: 'catalogMigrationHost',
    code: 'catalog-restore-failed',
    subject: layer.name,
    title: `Restoring ${layer.name} from before the catalog stopped part way. Every consultant still works; run `
      + '"Restore settings from before the catalog" again to finish it.',
    detail: error instanceof Error ? error.message : String(error),
  });
}

/** A refusal or a skipped definition, said once per run; a migration that went through says nothing. */
async function sayWhatWasLeft(layer: Layer, outcome: MigrationOutcome): Promise<void> {
  const left = leftSentence(outcome);
  if (left.length === 0) {
    return;
  }
  await notify({
    as: 'warning',
    class: 'refusal',
    source: 'catalogMigrationHost',
    code: 'catalog-migration-left',
    subject: layer.name,
    title: `Some models in ${layer.name} were left where they are: ${left}`,
  });
}

function leftSentence(outcome: MigrationOutcome): string {
  if (outcome.kind === 'refused') {
    return outcome.why;
  }

  return outcome.kind === 'migrate' || outcome.kind === 'unchanged-with-skips' ? outcome.skipped.join('; ') : '';
}

// ---------------------------------------------------------------- the layers

function layersOf(context: vscode.ExtensionContext, config: vscode.WorkspaceConfiguration): readonly Layer[] {
  const side = config.get<boolean>('perSideSettings') === true ? [sideLayer(context, config)] : [];

  return [userLayerOf(config), ...side];
}

const LAYER_KEYS: Readonly<Record<LayerWrite['key'], keyof CatalogLayer>> = {
  vendors: 'vendors',
  consultants: 'consultants',
  qconsultRows: 'qconsultRows',
  bugzModel: 'bugzModel',
  catalogMigration: 'marker',
  migratedFrom: 'backup',
  chatPresetsMoved: 'chatPresetsMoved',
  chatModel: 'chatModel',
  chatModelName: 'chatModelName',
};

/** Every key the move may write — what the `test:host` scenario asks the real registry about. */
export const MIGRATION_KEYS: readonly string[] = Object.keys(LAYER_KEYS);

/**
 * The chat model presets AS THE CHAT READS THEM (E4.6a): the user layer's, the shipped ones included when the person
 * never changed them — `modelKeys.userChatPresets`, the ONE reader `savedModels`, Chat's conflicts and a choice on Chat
 * take too, so the move moves exactly what the chat offered and Chat raises only what the move recorded (R7's code
 * round, finding 6).
 */
function chatPresetsOf(config: vscode.WorkspaceConfiguration): unknown {
  return userChatPresets(config);
}

/** The user's own `settings.json` — never a workspace value, which is not theirs to have migrated. */
function userLayerOf(config: vscode.WorkspaceConfiguration): Layer {
  return {
    name: 'your settings',
    read: () => ({ ...layerFrom((key) => config.inspect(key)?.globalValue), chatPresets: chatPresetsOf(config) }),
    write: async ({ key, value }) => {
      await config.update(key, value, vscode.ConfigurationTarget.Global);
    },
    // settings.json is written through VS Code's registry, which may not hold an updated version's keys yet.
    unknownKeys: (keys) => unknownKeysOf(keys, (key) => config.inspect(key)?.defaultValue),
  };
}

/** This side's overlay; a key it does not hold falls back to the user layer, which is what `sharedVendors` says. */
/**
 * What a side reads of the user layer, and why: the rows a key it does not hold falls back to; the consultants and question
 * rows it inherits, so the rows they refer to join its own list (C1); the user layer's chat record, whose row ids it keeps
 * where free (E4.6a); and the chat model it inherits, which it replaces when its own move gave that preset another id (R2).
 */
function inheritedFrom(config: vscode.WorkspaceConfiguration): Partial<CatalogLayer> {
  // Each read by NAME, so the scan that keeps every read around a side visible (thePanelReadsThisSide) sees all five.
  return {
    sharedVendors: userValue(config.inspect('vendors')),
    sharedConsultants: userValue(config.inspect('consultants')),
    sharedQconsultRows: userValue(config.inspect('qconsultRows')),
    userChatRecord: userValue(config.inspect('chatPresetsMoved')),
    userChatModel: userValue(config.inspect('chatModel')),
  };
}

function userValue(inspected: { readonly globalValue?: unknown } | undefined): unknown {
  return inspected?.globalValue;
}

function sideLayer(context: vscode.ExtensionContext, config: vscode.WorkspaceConfiguration): Layer {
  const side = thisSide(context.globalStorageUri);
  const store = context.globalState;

  return {
    name: 'this side\'s own settings',
    read: () => ({
      ...layerFrom((key) => readOverlay(store, side)[key]),
      ...inheritedFrom(config),
      // The chat's presets are the user layer's; a side that keeps its own rows gets chat rows of its own, under the
      // ids the user layer gave them where they are free (E4.6a).
      chatPresets: chatPresetsOf(config),
      side: true,
    }),
    write: async ({ key, value }) => {
      const { [key]: _old, ...rest } = readOverlay(store, side);
      await store.update(overlayKey(side), value === undefined ? rest : { ...rest, [key]: value });
    },
  };
}

function layerFrom(read: (key: string) => unknown): CatalogLayer {
  return Object.fromEntries(Object.entries(LAYER_KEYS).map(([key, field]) => [field, read(key)])) as CatalogLayer;
}

// ---------------------------------------------------------------- restore

const RESTORE = 'Restore';

/**
 * *ConnectOtherAIs: Restore settings from before the catalog* — every layer this window can see that holds a
 * backup, put back exactly, after a confirmation naming the layers. A restored layer is not migrated again by
 * itself.
 */
export async function restoreFromBeforeTheCatalog(context: vscode.ExtensionContext): Promise<void> {
  const layers = layersOf(context, vscode.workspace.getConfiguration('coai'))
    .map((layer) => ({ layer, outcome: restoreLayer(layer.read()) }))
    .filter((one): one is { layer: Layer; outcome: Extract<RestoreOutcome, { kind: 'restore' }> } => one.outcome.kind === 'restore');
  if (layers.length === 0) {
    await notify({
      as: 'information',
      class: 'outcome',
      source: 'catalogMigrationHost',
      code: 'catalog-restore-none',
      title: 'There is nothing to restore: none of the settings this window can see holds a backup from before the '
        + 'catalog.',
    });

    return;
  }
  if (!(await confirmed(layers.map(({ layer }) => layer.name)))) {
    return;
  }
  await turns.edit(async () => {
    for (const { layer, outcome } of layers) {
      await applyWrites(layer, outcome.writes, restoreStopped);
    }
  });
  afterMigration();
}

async function confirmed(names: readonly string[]): Promise<boolean> {
  const answer = await notifyAndAsk({
    as: 'warning',
    class: 'confirmation',
    source: 'catalogMigrationHost',
    code: 'catalog-restore-confirm',
    title: `Put the reviewers, consultants and question-consultant rows in ${names.join(' and ')} back exactly as they `
      + 'were before the catalog? Anything changed in them since is replaced, and they are not moved into the catalog '
      + 'again until you ask.',
    action: RESTORE,
    modal: true,
  });

  return answer === RESTORE;
}
