import * as vscode from 'vscode';
import { CatalogLayer, LayerWrite, migrateLayer, MigrationOutcome, restoreLayer, RestoreOutcome } from './catalogMigration';
import { overlayKey } from './coaiInstall';
import { thisSide } from './installer';
import { notify, notifyAndAsk } from './notify';
import { readOverlay } from './sideSettings';
import { WriteQueue } from './writeQueue';

/**
 * The host half of the move into the catalog (PLAN_one_model_catalog.md E1.3): read each settings layer, run
 * the pure `migrateLayer`, write what it says in the order it says, and tell the person only what it LEFT —
 * a migration that went through changes nothing they can see, so it says nothing.
 *
 * <p>Two layers per window: the user layer (`settings.json`, read through `inspect().globalValue` so a
 * workspace value is never migrated into it) and, when this side keeps its own settings, this side's overlay.
 * Another side's overlay is migrated by a window on that side. Runs are queued, never concurrent: activation,
 * a configuration change, and a save of a model key each enqueue one, and the writes of one run are what the
 * next run reads.</p>
 */

/** The settings whose change may leave a definition to move. */
export const MIGRATION_TRIGGERS: readonly string[] = ['vendors', 'consultants', 'qconsultRows'];

/** What a layer is called in a sentence, how it is read, and how one write lands in it. */
interface Layer {
  readonly name: string;
  readonly read: () => CatalogLayer;
  readonly write: (write: LayerWrite) => Promise<void>;
}

const queue = new WriteQueue();

/** What to do once a run has written anything — the activation's mirror of the server settings file. */
let afterMigration: () => void = () => undefined;

/** Called once on activation: remember what follows a migration, and run the first one. */
export function startCatalogMigration(context: vscode.ExtensionContext, after: () => void): Promise<void> {
  afterMigration = after;

  return scheduleCatalogMigration(context);
}

/** One more run, after any that is under way. Never throws: a failed layer is reported and the other goes on. */
export function scheduleCatalogMigration(context: vscode.ExtensionContext): Promise<void> {
  return queue.run(() => migrateEveryLayer(context));
}

async function migrateEveryLayer(context: vscode.ExtensionContext): Promise<void> {
  let wrote = false;
  for (const layer of layersOf(context, vscode.workspace.getConfiguration('coai'))) {
    wrote = (await migrateOne(layer)) || wrote;
  }
  if (wrote) {
    afterMigration();
  }
}

async function migrateOne(layer: Layer): Promise<boolean> {
  const outcome = migrateLayer(layer.read());
  await sayWhatWasLeft(layer, outcome);
  if (outcome.kind !== 'migrate') {
    return false;
  }

  return applyWrites(layer, outcome.writes);
}

/** In order, stopping at the first refusal — the order is what makes a stopped run safe to finish later. */
async function applyWrites(layer: Layer, writes: readonly LayerWrite[]): Promise<boolean> {
  try {
    for (const write of writes) {
      await layer.write(write);
    }

    return true;
  } catch (error: unknown) {
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

    return writes.length > 0;
  }
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
  catalogMigration: 'marker',
  migratedFrom: 'backup',
};

/** The user's own `settings.json` — never a workspace value, which is not theirs to have migrated. */
function userLayerOf(config: vscode.WorkspaceConfiguration): Layer {
  return {
    name: 'your settings',
    read: () => layerFrom((key) => config.inspect(key)?.globalValue),
    write: async ({ key, value }) => {
      await config.update(key, value, vscode.ConfigurationTarget.Global);
    },
  };
}

/** This side's overlay; a key it does not hold falls back to the user layer, which is what `sharedVendors` says. */
function sideLayer(context: vscode.ExtensionContext, config: vscode.WorkspaceConfiguration): Layer {
  const side = thisSide(context.globalStorageUri);
  const store = context.globalState;

  return {
    name: 'this side\'s own settings',
    read: () => ({
      ...layerFrom((key) => readOverlay(store, side)[key]),
      sharedVendors: config.inspect('vendors')?.globalValue,
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
  await queue.run(async () => {
    for (const { layer, outcome } of layers) {
      await applyWrites(layer, outcome.writes);
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
