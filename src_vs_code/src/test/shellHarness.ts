import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import Module from 'node:module';
import { dirname, join } from 'node:path';
import { build, type Plugin } from 'esbuild';

/**
 * The shell as it ships, on a scratch disk, against a recording `vscode` (v2 E4, story s4-chat-carried).
 *
 * <p>`scripts/bundle-entries.json` lists the bundles; this builds the ones a test needs with the same options `npm run bundle` uses, writes them where
 * the entry's loader expects them (`<dir>/extension.js`, `<dir>/modules/<id>.js`) and answers the folder. A module a test does not want to run is a
 * TRIPWIRE: a file that records its id in {@link LOADED} and throws, so "the legacy module was never loaded" is something the test reads, not something
 * it hopes. Everything is run, not read: the entry's `activate` registers into {@link RecordingEditor}, the thunks it registered are called, and the
 * modules do what they do in the field.</p>
 */

export const ROOT = process.cwd();

/** Where the tripwire stubs write the id of the module that was required. */
export const LOADED = Symbol.for('coai.test.loaded');

/** The counters the chat build's spies write (`chatBuild`): drains of the usage queue, housekeeping starts and disposals. */
export const SPY = Symbol.for('coai.test.chat.spy');

export interface Spy {
  drained: number;
  started: number;
  disposed: number;
}

interface Config {
  readonly common: Record<string, unknown>;
  readonly bundles: readonly { readonly id: string; readonly entry: string }[];
}

const config = (): Config => JSON.parse(readFileSync(join(ROOT, 'scripts', 'bundle-entries.json'), 'utf8')) as Config;

/** One bundle of the list, built in memory with its production options (plus `plugins`), as text. */
export async function bundleText(id: string, plugins: readonly Plugin[] = []): Promise<string> {
  const all = config();
  const bundle = all.bundles.find((one) => one.id === id);
  if (bundle === undefined) {
    throw new Error(`bundle-entries.json has no ${id} bundle`);
  }
  const result = await build({ absWorkingDir: ROOT, entryPoints: [bundle.entry], outfile: 'unused.js', bundle: true, write: false, logLevel: 'silent', plugins: [...plugins], ...all.common });
  const file = result.outputFiles[0];
  if (file === undefined) {
    throw new Error('esbuild produced no output');
  }

  return file.text;
}

const put = (file: string, text: string): void => {
  mkdirSync(dirname(file), { recursive: true });
  writeFileSync(file, text);
};

/** A module that must not be loaded: requiring it records its id and throws. */
const tripwire = (id: string): string => `(globalThis[Symbol.for('coai.test.loaded')] ??= []).push(${JSON.stringify(id)}); throw new Error(${JSON.stringify(`the ${id} module was loaded`)});\n`;

/**
 * Spies on two things the chat module does at teardown, over the REAL modules: the usage-queue drain (`flushChatUsage`) and the housekeeping start with
 * its `dispose`. Each virtual module re-exports the real one, replacing one export; the real module is resolved by its own path, not by the plugin.
 */
export function chatSpies(): Plugin {
  const src = (name: string): string => join(ROOT, 'src', name).replaceAll('\\', '/');

  return {
    name: 'chat-spies',
    setup(b) {
      b.onResolve({ filter: /[/](chatUsageFile|chatStoreHousekeeping)$/ }, (args) => (args.namespace === 'chat-spy' ? undefined : { path: args.path.split('/').pop() ?? '', namespace: 'chat-spy' }));
      b.onLoad({ filter: /.*/, namespace: 'chat-spy' }, (args) => {
        const spy = 'globalThis[Symbol.for(\'coai.test.chat.spy\')]';
        const contents = args.path === 'chatUsageFile'
          ? `export * from './chatUsageFile';
import { flushChatUsage as real } from './chatUsageFile';
export async function flushChatUsage() { ${spy}.drained += 1; return real(); }`
          : `export * from './chatStoreHousekeeping';
import { startHousekeeping as real } from './chatStoreHousekeeping';
export function startHousekeeping(deps) {
  const housekeeping = real(deps);
  const dispose = housekeeping.dispose.bind(housekeeping);
  ${spy}.started += 1;
  housekeeping.dispose = () => { ${spy}.disposed += 1; dispose(); };
  return housekeeping;
}`;

        return { contents, loader: 'js', resolveDir: src('') };
      });
    },
  };
}

/** Writes the shell into `dir`: the real entry, runtime and chat modules, and tripwires for the legacy and sidebar modules. */
export async function writeShell(dir: string, options: { readonly spies?: boolean } = {}): Promise<void> {
  put(join(dir, 'extension.js'), await bundleText('extension'));
  put(join(dir, 'modules', 'runtime.js'), await bundleText('runtime'));
  put(join(dir, 'modules', 'chat.js'), await bundleText('chat', options.spies === true ? [chatSpies()] : []));
  put(join(dir, 'modules', 'legacy.js'), tripwire('legacy'));
  put(join(dir, 'modules', 'sidebar.js'), tripwire('sidebar'));
}

type Handler = (...args: unknown[]) => unknown;

/** What the fake editor was asked to register, and a handle to call what it was given. */
export interface RecordingEditor {
  readonly vscode: Record<string, unknown>;
  readonly commands: Map<string, Handler>;
  readonly serializers: Map<string, { deserializeWebviewPanel: (panel: unknown, state: unknown) => Thenable<void> | void }>;
  readonly viewProviders: Map<string, unknown>;
  readonly uriHandlers: unknown[];
  /** Every registration the editor was asked for, in order, as `command:<id>` / `serializer:<type>` / `view:<type>`: a second one for an id is a defect VS Code would refuse. */
  readonly registrations: string[];
  /** Every call made to the editor that is not a registration, as `namespace.member`: the cold start's footprint. */
  readonly calls: string[];
}

/** A `vscode` that records registrations and answers everything else with an inert stand-in; enough for the entry, the runtime and the chat module to run. */
export function recordingEditor(): RecordingEditor {
  const commands = new Map<string, Handler>();
  const serializers = new Map<string, { deserializeWebviewPanel: (panel: unknown, state: unknown) => Thenable<void> | void }>();
  const viewProviders = new Map<string, unknown>();
  const uriHandlers: unknown[] = [];
  const calls: string[] = [];
  const registrations: string[] = [];
  const disposable = { dispose: () => undefined };
  const inert = (name: string) => (): { dispose: () => undefined } => { calls.push(name); return disposable; };
  const uri = (fsPath: string) => ({ fsPath, path: fsPath, scheme: 'file', toString: () => `file://${fsPath}` });
  const section = (name: string, own: Record<string, unknown>): Record<string, unknown> => new Proxy(own, {
    get: (target, key) => (typeof key === 'string' && key in target ? target[key] : typeof key === 'string' ? inert(`${name}.${key}`) : undefined),
  });
  const vscode = {
    ThemeIcon: class { constructor(public readonly id: string) {} },
    ThemeColor: class { constructor(public readonly id: string) {} },
    EventEmitter: class { event = () => disposable; fire(): void { /* nobody listens */ } dispose(): void { /* nothing held */ } },
    Disposable: class { dispose(): void { /* nothing held */ } },
    MarkdownString: class { constructor(public readonly value?: string) {} },
    CancellationTokenSource: class { token = { isCancellationRequested: false }; },
    Position: class { constructor(public readonly line: number, public readonly character: number) {} },
    Range: class { constructor(public readonly start: unknown, public readonly end: unknown) {} },
    Uri: { file: uri, parse: (one: string) => uri(one), joinPath: (base: { fsPath: string }, ...rest: string[]) => uri(join(base.fsPath, ...rest)) },
    ViewColumn: { One: 1, Two: 2, Active: -1, Beside: -2 },
    StatusBarAlignment: { Left: 1, Right: 2 },
    ProgressLocation: { Notification: 15, Window: 10, SourceControl: 1 },
    ConfigurationTarget: { Global: 1, Workspace: 2, WorkspaceFolder: 3 },
    QuickPickItemKind: { Separator: -1, Default: 0 },
    ExtensionMode: { Production: 1, Development: 2, Test: 3 },
    UIKind: { Desktop: 1, Web: 2 },
    env: { clipboard: { readText: async () => '', writeText: async () => undefined }, uiKind: 1, remoteName: undefined, openExternal: async () => true },
    extensions: { getExtension: () => undefined, all: [] },
    languages: section('languages', {}),
    commands: section('commands', {
      registerCommand: (id: string, run: Handler) => { registrations.push(`command:${id}`); commands.set(id, run); return disposable; },
      executeCommand: async (id: string) => { calls.push(`commands.executeCommand:${id}`); },
      getCommands: async () => [],
    }),
    window: section('window', {
      state: { focused: true },
      terminals: [],
      activeTerminal: undefined,
      activeTextEditor: undefined,
      tabGroups: { all: [], activeTabGroup: { activeTab: undefined }, onDidChangeTabs: () => disposable },
      registerWebviewPanelSerializer: (type: string, serializer: { deserializeWebviewPanel: (panel: unknown, state: unknown) => Thenable<void> | void }) => { registrations.push(`serializer:${type}`); serializers.set(type, serializer); return disposable; },
      registerWebviewViewProvider: (type: string, provider: unknown) => { registrations.push(`view:${type}`); viewProviders.set(type, provider); return disposable; },
      registerUriHandler: (handler: unknown) => { uriHandlers.push(handler); return disposable; },
    }),
    workspace: section('workspace', {
      workspaceFolders: undefined,
      getConfiguration: () => ({ get: () => undefined, update: async () => undefined, has: () => false, inspect: () => undefined }),
      fs: { readFile: async () => new Uint8Array(), writeFile: async () => undefined, stat: async () => ({}) },
    }),
  };

  return { vscode, commands, serializers, viewProviders, uriHandlers, registrations, calls };
}

/** Routes `require('vscode')` to `editor` for every module loaded until the returned function is called. */
export function installEditor(editor: RecordingEditor): () => void {
  const host = Module as unknown as { _load: (request: string, ...rest: unknown[]) => unknown };
  const load = host._load;
  host._load = function loadWithEditor(this: unknown, request: string, ...rest: unknown[]): unknown {
    return request === 'vscode' ? editor.vscode : load.call(this, request, ...rest);
  };

  return () => { host._load = load; };
}

/** An `ExtensionContext` with the members the entry and the chat module use; mementos are in memory, storage is under `dir`. */
export function extensionContext(dir: string, mode: number): Record<string, unknown> {
  const memento = (): Record<string, unknown> => {
    const held = new Map<string, unknown>();

    return { get: (key: string, fallback?: unknown) => (held.has(key) ? held.get(key) : fallback), update: async (key: string, value: unknown) => { held.set(key, value); }, keys: () => [...held.keys()], setKeysForSync: () => undefined };
  };
  const uri = (fsPath: string) => ({ fsPath, path: fsPath, scheme: 'file', toString: () => `file://${fsPath}` });

  return {
    subscriptions: [],
    globalState: memento(),
    workspaceState: memento(),
    globalStorageUri: uri(join(dir, 'global-storage')),
    storageUri: uri(join(dir, 'workspace-storage')),
    extensionUri: uri(dir),
    extensionPath: dir,
    extensionMode: mode,
    extension: { packageJSON: { version: '0.0.0-test' } },
    secrets: { get: async () => undefined, store: async () => undefined, delete: async () => undefined },
  };
}

/** A module bundled from source text (a probe), with the production options, written to `<dir>/modules/<id>.js`; answers the source files that went into it. */
export async function writeProbe(dir: string, id: string, source: string): Promise<readonly string[]> {
  const all = config();
  const result = await build({
    absWorkingDir: ROOT, stdin: { contents: source, resolveDir: join(ROOT, 'shell'), loader: 'ts', sourcefile: `probe-${id}.ts` },
    outfile: 'unused.js', bundle: true, write: false, metafile: true, logLevel: 'silent', ...all.common,
  });
  const file = result.outputFiles[0];
  if (file === undefined) {
    throw new Error('esbuild produced no output');
  }
  put(join(dir, 'modules', `${id}.js`), file.text);

  return Object.keys(result.metafile.inputs);
}
