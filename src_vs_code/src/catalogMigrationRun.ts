import { CatalogLayer, LayerWrite, migrateLayer, MigrationOptions, MigrationOutcome } from './catalogMigration';

/**
 * One layer's run of the move into the catalog — read, plan, write in order — without `vscode`, so the host's half of
 * PLAN_one_model_catalog.md E1.3 is a value a test can drive (todo/PLAN_catalog_migration_waits_for_its_settings.md).
 * `catalogMigrationHost.ts` supplies the layers and the reports; nothing here decides what a person is told.
 */

/** What a layer is called in a sentence, how it is read, and how one write lands in it. */
export interface RunLayer {
  readonly name: string;
  readonly read: () => CatalogLayer;
  readonly write: (write: LayerWrite) => Promise<void>;
}

/** What a run says, through the host: a stop part way, and what the plan left where it was. */
export interface RunReports {
  readonly stopped: (layer: RunLayer, error: unknown) => Promise<void>;
  readonly left: (layer: RunLayer, outcome: MigrationOutcome) => Promise<void>;
}

/** Whether the layer was written to — a run that wrote anything is followed by the mirror of the server settings. */
export async function migrateOne(layer: RunLayer, options: MigrationOptions, reports: RunReports): Promise<boolean> {
  const outcome = readAndPlan(layer, options);
  if (outcome instanceof Error) {
    await reports.stopped(layer, outcome);

    return false;
  }
  await reports.left(layer, outcome);
  if (outcome.kind !== 'migrate') {
    return false;
  }

  return applyWrites(layer, outcome.writes, reports.stopped);
}

/**
 * The layer read and the plan — or the error a read threw (a corrupted overlay, a settings-store fault), which used to
 * escape every `void` caller unheard and repeat silently on each start (PR #681's code round).
 */
function readAndPlan(layer: RunLayer, options: MigrationOptions): MigrationOutcome | Error {
  try {
    return migrateLayer(layer.read(), options);
  } catch (error: unknown) {
    return error instanceof Error ? error : new Error(String(error));
  }
}

/** In order, stopping at the first refusal — the order is what makes a stopped run safe to finish later. */
export async function applyWrites(layer: RunLayer, writes: readonly LayerWrite[], stopped: (layer: RunLayer, error: unknown) => Promise<void>): Promise<boolean> {
  try {
    for (const write of writes) {
      await layer.write(write);
    }

    return true;
  } catch (error: unknown) {
    await stopped(layer, error);

    return writes.length > 0;
  }
}
