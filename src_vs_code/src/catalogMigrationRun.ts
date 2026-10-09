import { CatalogLayer, LayerWrite, migrateLayer, MigrationOptions, MigrationOutcome } from './catalogMigration';

/**
 * One layer's run of the move into the catalog — read, plan, write in order — without `vscode`, so the host's half of
 * PLAN_one_model_catalog.md E1.3 is a value a test can drive (research/PLAN_catalog_migration_waits_for_its_settings.md).
 * `catalogMigrationHost.ts` supplies the layers and the reports; nothing here decides what a person is told.
 */

/** What a layer is called in a sentence, how it is read, and how one write lands in it. */
export interface RunLayer {
  readonly name: string;
  readonly read: () => CatalogLayer;
  readonly write: (write: LayerWrite) => Promise<void>;
  /**
   * The keys among `keys` this window cannot write yet, because its settings registry does not hold them — a layer in
   * `settings.json` answers; one in the extension's own storage needs no registry and has no answer.
   */
  readonly unknownKeys?: (keys: readonly string[]) => readonly string[];
}

/** What a run says, through the host: a stop part way, and what the plan left where it was. */
export interface RunReports {
  readonly stopped: (layer: RunLayer, error: unknown) => Promise<void>;
  readonly left: (layer: RunLayer, outcome: MigrationOutcome) => Promise<void>;
  /** Nothing was written because the window does not know these keys yet; the host decides when to try again. */
  readonly waiting: (layer: RunLayer, keys: readonly string[]) => void;
}

/**
 * The keys a settings registry does not hold, read from what it says each key's default is: VS Code gives every
 * registered key a default — the manifest's, or one made from its type when the manifest gives none — and an unknown key
 * none. A `test:host` scenario holds the real editor to that (the plan round's finding).
 */
export function unknownKeysOf(keys: readonly string[], defaultOf: (key: string) => unknown): readonly string[] {
  return keys.filter((key) => defaultOf(key) === undefined);
}

/**
 * VS Code's refusal of a write to a key its settings registry does not hold — `Unable to write to User Settings because
 * coai.migratedFrom is not a registered configuration.` The key it names, without the section; empty for any other error.
 */
export function unregisteredKeyIn(error: unknown): string {
  const message = error instanceof Error ? error.message : String(error);
  const named = /\bcoai\.(\w+) is not a registered configuration\b/u.exec(message);

  return named?.[1] ?? '';
}

/** Whether the layer was written to — a run that wrote anything is followed by the mirror of the server settings. */
export async function migrateOne(layer: RunLayer, options: MigrationOptions, reports: RunReports): Promise<boolean> {
  const outcome = readAndPlan(layer, options);
  if (outcome instanceof Error) {
    await reports.stopped(layer, outcome);

    return false;
  }
  await reports.left(layer, outcome);

  return outcome.kind === 'migrate' ? writeUnlessWaiting(layer, outcome.writes, reports) : false;
}

/** The writes — unless the window does not know a key among them yet, which is asked BEFORE the first one is made. */
async function writeUnlessWaiting(layer: RunLayer, writes: readonly LayerWrite[], reports: RunReports): Promise<boolean> {
  const unknown = layer.unknownKeys?.(writes.map((write) => write.key)) ?? [];
  if (unknown.length > 0) {
    reports.waiting(layer, [...new Set(unknown)]);

    return false;
  }

  return applyWrites(layer, writes, (stoppedLayer, error, write) => stoppedOrWaiting(stoppedLayer, error, write, reports));
}

/**
 * The belt: the check and the write are two calls, and the registry is VS Code's to change between them. A refusal is a
 * wait when VS Code's words name the key, or — since those words are translated with the editor's language — when the
 * registry, asked again, does not know the refused key.
 */
async function stoppedOrWaiting(layer: RunLayer, error: unknown, write: LayerWrite, reports: RunReports): Promise<void> {
  const unknown = refusedAsUnknown(layer, error, write);
  if (unknown.length > 0) {
    reports.waiting(layer, unknown);

    return;
  }
  await reports.stopped(layer, error);
}

/** The key a refusal was about when it is one the window does not know — by VS Code's words, else by asking again. */
function refusedAsUnknown(layer: RunLayer, error: unknown, write: LayerWrite): readonly string[] {
  const named = unregisteredKeyIn(error);

  return named.length > 0 ? [named] : layer.unknownKeys?.([write.key]) ?? [];
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

/**
 * In order, stopping at the first refusal — the order is what makes a stopped run safe to finish later. Whether the
 * layer was written to: every write made, or a stop after at least one landed. A refusal of the FIRST write wrote
 * nothing, and says so, so no mirror or redraw follows an unchanged layer (the code round's finding).
 */
export async function applyWrites(
  layer: RunLayer,
  writes: readonly LayerWrite[],
  stopped: (layer: RunLayer, error: unknown, write: LayerWrite) => Promise<void>,
): Promise<boolean> {
  let landed = 0;
  for (const write of writes) {
    try {
      await layer.write(write);
    } catch (error: unknown) {
      await stopped(layer, error, write);

      return landed > 0;
    }
    landed += 1;
  }

  return true;
}
