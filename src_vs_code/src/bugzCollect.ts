import { bugzCollectRefusal, bugzPickFrom, type BugzInputs } from './bugzPick';

/**
 * Starting a Bugz collect (todo/PLAN_one_model_catalog.md, E5.1; its code round, finding 6): the decision the provider's
 * `collectBugs` makes, with its reads and its two effects handed in as ports — so a stranded pick, no pick and a pick
 * that holds are each RUN by a test with in-memory ports, where the provider itself is only ever built by an extension
 * host. The provider's part is to build the ports: the inputs the sidebar's picker is drawn from, the `no-ranking-model`
 * notice, and the one spawn of `coai-mcp --collect-bugs` through the window's data directory.
 */

/** What a collect is started with — the reads and the two effects, handed in so the decision is runnable in a test. */
export interface CollectPorts {
  /** The facts the pick is read from — the same the sidebar draws its picker from. */
  readonly inputs: () => Promise<BugzInputs>;
  /** Tells the person why nothing was collected. */
  readonly refuse: (sentence: string) => Promise<void>;
  /** Runs the collector with these arguments — the only way a collect starts. */
  readonly start: (args: readonly string[]) => Promise<void>;
}

/**
 * Refuses a pick that does not hold — stranded, or none — by its sentence and starts nothing; else starts the collect
 * with the pick and, for a binary that ranks by it, the row's runtime.
 */
export async function collectWithPick(ports: CollectPorts): Promise<void> {
  const inputs = await ports.inputs();
  const refused = bugzCollectRefusal(bugzPickFrom(inputs));
  if (refused.length > 0) {
    await ports.refuse(refused);

    return;
  }
  await ports.start(collectArgs(inputs.saved, runtimeOf(inputs), inputs.byRuntime));
}

/** The runtime of the row the pick names — '' when no row is called that. */
function runtimeOf(inputs: BugzInputs): string {
  const rowId = inputs.saved.split('/')[0]?.toLowerCase();

  return inputs.rows.find((one) => one.id === rowId)?.runtime ?? '';
}

/**
 * The collector's arguments: the row's runtime is said only to a binary that ranks by it — an older one refuses a flag
 * it does not know — and only with a model to rank with.
 */
export function collectArgs(model: string, runtime: string, byRuntime: boolean): readonly string[] {
  return model.length === 0 ? ['--collect-bugs'] : ['--collect-bugs', '--model', model, ...runtimeArgs(runtime, byRuntime)];
}

function runtimeArgs(runtime: string, byRuntime: boolean): readonly string[] {
  return byRuntime && runtime.length > 0 ? ['--runtime', runtime] : [];
}
