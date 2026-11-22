import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import * as nodePath from 'node:path';
import { join } from 'node:path';
import { test } from 'node:test';
import { build, type Plugin } from 'esbuild';

/**
 * `activate()` of the shell's entry only REGISTERS (v2 extension.md § 2, epic E2, story S1, task t4).
 *
 * <p>The entry bundle is built in memory with the options `scripts/bundle-entries.json` gives it (the same list
 * `npm run bundle` builds from), loaded against a RECORDING `vscode`, and `activate` is called. What it did is a
 * list of calls and reads, and the list may hold only `register*` calls and `context.subscriptions.push`. A file
 * read, a spawn, a request, a memento write, a configuration read or a timer is a call this recorder sees — the
 * bundle's own `require` is limited to `vscode` and `node:path`, and the globals a start-up could use are replaced
 * by recorders — and the bundle's text is not what is asserted on, so a forbidden call hidden behind a helper still
 * shows.</p>
 *
 * <p>The last case plants forbidden calls into the entry and requires this test's own check to report each: a guard
 * that cannot fail is the thing the version before it was not allowed to be.</p>
 */

const ROOT = process.cwd();

interface Manifest {
  readonly contributes: { readonly commands: readonly { readonly command: string }[]; readonly views: Record<string, readonly { readonly id: string }[]> };
  readonly activationEvents: readonly string[];
}

const manifest = JSON.parse(readFileSync(join(ROOT, 'package.json'), 'utf8')) as Manifest;

interface Bundles {
  readonly common: Record<string, unknown>;
  readonly bundles: readonly { readonly id: string; readonly entry: string }[];
}

/** The entry bundle's code, built in memory; `planted` replaces the text of `shell/entry.ts` inside the build. */
async function entryCode(planted?: string): Promise<string> {
  const config = JSON.parse(readFileSync(join(ROOT, 'scripts', 'bundle-entries.json'), 'utf8')) as Bundles;
  const bundle = config.bundles.find((one) => one.id === 'extension');
  assert.ok(bundle !== undefined, 'bundle-entries.json has no `extension` bundle');
  const entry = join(ROOT, bundle.entry);
  const plugins: Plugin[] = planted === undefined ? [] : [{
    name: 'planted-entry',
    setup(b) {
      b.onLoad({ filter: /\.ts$/ }, (args) => (args.path === entry ? { contents: planted, loader: 'ts', resolveDir: join(ROOT, 'shell') } : undefined));
    },
  }];
  const result = await build({
    absWorkingDir: ROOT, entryPoints: [bundle.entry], outfile: 'unused.js', bundle: true, write: false, logLevel: 'silent', plugins,
    ...config.common,
  });
  const file = result.outputFiles[0];
  assert.ok(file !== undefined, 'esbuild produced no output');

  return file.text;
}

interface Run {
  readonly calls: readonly string[];
  readonly reads: readonly string[];
  readonly forbidden: readonly string[];
  readonly commandIds: readonly string[];
  readonly pushed: number;
  readonly returned: unknown;
}

/**
 * Runs `activate` of `code` against recorders and answers what it did. `env` is what `process.env` holds: the entry may read
 * exactly one variable from it, COAI_MEASURE (the cold-start measurement's switch); every other read of `process` is forbidden.
 */
function runActivate(code: string, env: Readonly<Record<string, string>> = {}): Run {
  const accessed = new Set<string>();
  const calls: string[] = [];
  const forbidden: string[] = [];
  const commandIds: string[] = [];
  let pushed = 0;

  // A `vscode` whose every member is a recording stand-in: reading one is noted, calling one is noted and answers a disposable.
  const recording = (path: readonly string[]): unknown => new Proxy(function recorder() { /* the target of a call */ }, {
    get: (_target, key) => {
      if (typeof key === 'symbol') {
        return undefined;
      }
      accessed.add([...path, key].join('.'));

      return recording([...path, key]);
    },
    apply: (_target, _this, args: unknown[]) => {
      calls.push(path.join('.'));
      if (path.join('.') === 'commands.registerCommand') {
        commandIds.push(String(args[0]));
      }

      return { dispose: () => undefined };
    },
  });
  const vscodeFake = {
    commands: recording(['commands']), window: recording(['window']), workspace: recording(['workspace']),
    env: recording(['env']), extensions: recording(['extensions']), languages: recording(['languages']),
    ExtensionMode: { Production: 1, Development: 2, Test: 3 },
  };
  const subscriptions = new Proxy([] as unknown[], {
    get: (target, key) => (key === 'push'
      ? (...items: unknown[]) => { pushed += 1; calls.push('subscriptions.push'); return target.push(...items); }
      : Reflect.get(target, key)),
  });
  const context = new Proxy({ subscriptions }, {
    get: (target, key) => {
      if (key !== 'subscriptions') {
        forbidden.push(`context.${String(key)} was read`);
      }

      return Reflect.get(target, key);
    },
  });
  const shout = (name: string) => () => { forbidden.push(`${name} was called`); throw new Error(`${name} is not allowed in activate()`); };
  const fakeRequire = (name: string): unknown => {
    if (name === 'vscode') {
      return vscodeFake;
    }
    if (name === 'node:path' || name === 'path') {
      return nodePath;
    }
    forbidden.push(`require('${name}')`);
    throw new Error(`the entry may not load ${name}`);
  };
  const module = { exports: {} as { activate?: (c: unknown) => unknown } };
  const wrapper = new Function('module', 'exports', 'require', '__dirname', 'setTimeout', 'setInterval', 'setImmediate', 'fetch', 'process', code);
  wrapper(module, module.exports, fakeRequire, join(ROOT, 'dist'), shout('setTimeout'), shout('setInterval'), shout('setImmediate'), shout('fetch'), new Proxy({}, {
    get: (_t, key) => {
      if (key === 'env') {
        return new Proxy({}, { get: (_e, name) => { if (name !== 'COAI_MEASURE') { forbidden.push(`process.env.${String(name)} was read`); } return env[String(name)]; } });
      }
      forbidden.push(`process.${String(key)} was read`);

      return undefined;
    },
  }));
  const activate = module.exports.activate;
  assert.equal(typeof activate, 'function', 'the entry exports no activate');
  const returned = activate?.(context);

  // A member that was read and never called, and is not a step on the way to a call, is a READ of the editor's state.
  const steps = new Set(calls.flatMap((call) => call.split('.').map((_part, at, all) => all.slice(0, at + 1).join('.'))));
  const reads = [...accessed].filter((one) => !steps.has(one));

  return { calls, reads, forbidden, commandIds, pushed, returned };
}

/** What `activate` may do and did not: every call must be `register*` or `subscriptions.push`, and nothing else may be read. */
function violations(run: Run): readonly string[] {
  const wrongCalls = run.calls.filter((call) => call !== 'subscriptions.push' && !/(?:^|\.)register[A-Za-z]*$/u.test(call));

  return [
    ...wrongCalls.map((call) => `call ${call}`),
    ...run.reads.map((read) => `read of ${read}`),
    ...run.forbidden,
  ];
}

test('activate() makes only register* calls and subscriptions.push', async () => {
  const run = runActivate(await entryCode());

  assert.deepEqual(violations(run), []);
  assert.equal(run.pushed, 1, 'activate pushes its registrations in one place, so a later push stands out');
});

test('what activate registers is what the manifest declares: every command, the sidebar view, the chat serializer, a uri handler', async () => {
  const run = runActivate(await entryCode());
  const declared = manifest.contributes.commands.map((one) => one.command);
  const registerCommands = run.calls.filter((call) => call === 'commands.registerCommand');

  assert.equal(registerCommands.length, declared.length, 'the entry registers a different number of commands than package.json declares');
  assert.deepEqual([...run.commandIds].sort(), [...declared].sort(), 'the entry registers other command ids than package.json declares (shell/ids.ts is out of date)');
  assert.deepEqual(run.calls.filter((call) => call !== 'commands.registerCommand' && call !== 'subscriptions.push').sort(),
    ['window.registerUriHandler', 'window.registerWebviewPanelSerializer', 'window.registerWebviewViewProvider']);
  const returned = run.returned as { readonly legacy?: unknown; readonly chat?: unknown };
  assert.equal(typeof returned.legacy, 'function', 'activate returns no door for the test host to start the old code');
  assert.equal(typeof returned.chat, 'function', 'activate returns no door for the test host to start the chat module');
});

test('with COAI_MEASURE=1 activate registers one more command, coai.__measure, and still only registers; with it unset the command is absent', async () => {
  const code = await entryCode();
  const off = runActivate(code);
  const on = runActivate(code, { COAI_MEASURE: '1' });
  const declared = manifest.contributes.commands.map((one) => one.command);

  assert.deepEqual(violations(on), []);
  assert.equal(on.pushed, 1, 'the measure command is pushed with the other registrations, not in a second push');
  assert.equal(off.commandIds.includes('coai.__measure'), false, 'the measure command is registered without COAI_MEASURE=1');
  assert.deepEqual([...on.commandIds].sort(), [...declared, 'coai.__measure'].sort());
  assert.equal(runActivate(code, { COAI_MEASURE: '0' }).commandIds.includes('coai.__measure'), false, 'only the value 1 switches it on');
});

test('the activation events are the views, panels and uri links this shell registers for, and never onStartupFinished', () => {
  assert.deepEqual([...manifest.activationEvents].sort(), ['onUri', 'onView:coai.panel', 'onWebviewPanel:coaiChat']);
  assert.ok(Object.values(manifest.contributes.views).flat().some((view) => view.id === 'coai.panel'), 'onView:coai.panel names a view the manifest does not contribute');
});

test('the check is not vacuous: each planted forbidden call is reported', async () => {
  const entry = readFileSync(join(ROOT, 'shell', 'entry.ts'), 'utf8');
  const plants: readonly (readonly [string, string])[] = [
    ['a configuration read', "vscode.workspace.getConfiguration('coai');"],
    ['a memento write', "void context.globalState.update('k', 1);"],
    ['a timer', 'setTimeout(() => undefined, 1);'],
    ['a command run', "void vscode.commands.executeCommand('coai.help');"],
    ['a file read', "require('node:fs').readFileSync('x');"],
    ['a state read', 'void vscode.workspace.workspaceFolders;'],
  ];
  for (const [what, statement] of plants) {
    const planted = entry.replace('  const legacy = () => lazy(MODULE_LEGACY);', `  const legacy = () => lazy(MODULE_LEGACY);\n  ${statement}`);
    assert.notEqual(planted, entry, 'the plant found no place in shell/entry.ts: update this test with the file');
    let reported: readonly string[];
    try {
      reported = violations(runActivate(await entryCode(planted)));
    } catch (error) {
      reported = [`threw: ${error instanceof Error ? error.message : String(error)}`];
    }
    assert.notDeepEqual(reported, [], `${what} in activate() was not reported`);
  }
});
