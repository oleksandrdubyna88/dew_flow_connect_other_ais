import { ConfigReader } from './settingsShape';

/**
 * The settings that name a model — and with it a program to run — read from the person's OWN layer.
 *
 * <p><b>Why.</b> None of `coai.*` declares a manifest `scope`, so VS Code merges a workspace or folder
 * value over the user one. For these keys that is a cloned repository choosing what runs: a
 * `.vscode/settings.json` carrying `coai.vendors` with an `executablePath` of its own would be launched
 * by the next review. PLAN_one_model_catalog.md, E1.1 and its plan round (findings 3 and 10).</p>
 *
 * <p><b>Why not a manifest `scope`.</b> `machine` or `application` would stop VS Code applying a
 * workspace value, but they also change which layer a REMOTE window reads — a change for every WSL side
 * that nobody has measured. Reading `inspect()` keeps the user layer exactly as every side reads it
 * today and drops only the two layers a repository can write.</p>
 *
 * <p>vscode-free, so the rule is a unit test; `sideConfig.readerFor` is the one place that hands it the
 * host's `get` and `inspect`, and `modelKeysAreReadOnce.test.ts` refuses any other read of these keys.</p>
 */
export const MODEL_KEYS: readonly string[] = [
  'vendors', 'consultants', 'qconsultRows', 'chatModel', 'chatModelPresets', 'bugzModel', 'securityLane',
];

/** What `WorkspaceConfiguration.inspect` answers, narrowed to the layers this rule talks about. */
export interface Inspected {
  readonly defaultValue?: unknown;
  readonly globalValue?: unknown;
  readonly workspaceValue?: unknown;
  readonly workspaceFolderValue?: unknown;
}

/** A workspace or folder value of a model key that was NOT applied — what the notice says. */
export interface IgnoredValue {
  readonly key: string;
  readonly layer: 'workspace' | 'folder';
  readonly value: unknown;
  /** Whether copying it would REPLACE something the person already set in their own layer. */
  readonly userHasOne: boolean;
}

type Inspect = (key: string) => Inspected | undefined;

/**
 * A reader that answers a model key from the user layer (then the manifest default), and every other
 * key exactly as `merged` does.
 */
export function userLayerReader(merged: ConfigReader, inspect: Inspect): ConfigReader {
  return (section) => (MODEL_KEYS.includes(section) ? userLayerValue(inspect(section)) : merged(section));
}

/** The user layer's value, or the manifest default when the person set none. */
function userLayerValue(inspected: Inspected | undefined): unknown {
  return inspected?.globalValue ?? inspected?.defaultValue;
}

/** Every model key a workspace or folder tried to set, in {@link MODEL_KEYS} order. */
export function ignoredWorkspaceValues(inspect: Inspect): readonly IgnoredValue[] {
  return MODEL_KEYS.flatMap((key) => ignoredIn(key, inspect(key)));
}

/** The layers a repository can write, folder first — the one VS Code would have applied. */
const REPOSITORY_LAYERS = [
  ['folder', 'workspaceFolderValue'],
  ['workspace', 'workspaceValue'],
] as const;

/** The one repository layer that held a value for `key`, or nothing. */
function ignoredIn(key: string, inspected: Inspected = {}): readonly IgnoredValue[] {
  const held = REPOSITORY_LAYERS.find(([, field]) => inspected[field] !== undefined);

  return held === undefined
    ? []
    : [{ key, layer: held[0], value: inspected[held[1]], userHasOne: inspected.globalValue !== undefined }];
}

/**
 * Whether copying an ignored value would REPLACE something the person set — judged where the copy LANDS: this side's
 * own settings when it keeps them and the key is one of theirs (`overlay` given), else the person's user layer. It
 * was judged on the user layer alone, so on a side with its own settings Copy overwrote that side's models
 * (PR #681's review).
 */
export function copyWouldReplace(ignored: IgnoredValue, overlay: Readonly<Record<string, unknown>> | undefined): boolean {
  return overlay === undefined ? ignored.userHasOne : overlay[ignored.key] !== undefined;
}
