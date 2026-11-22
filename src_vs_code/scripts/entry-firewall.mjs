#!/usr/bin/env node
/**
 * The entry firewall (v2 extension.md § 2, § 10 row 7): what the extension's entry bundle may contain, and how big it may be.
 *
 * <p>`activate()` must do nothing but register, and a bundle that quietly pulls in `fs`, a child process or the old
 * `src/` tree would load all of that at start-up whatever the source of `activate` says. So the entry is built in memory
 * with a metafile and checked twice:</p>
 * <ul>
 *   <li>`inputs` — every source file that went into the bundle must be on the bundle's allow-list (`bundle-entries.json`:
 *       shell/entry.ts, shell/lazy.ts, shell/ids.ts), and none may be under `src/`;</li>
 *   <li>`outputs[*].imports` — every module the bundle `require`s at run time. This is the second check on purpose: a
 *       Node builtin is external, so it is NOT among the inputs and shows only here (consultant review 1, finding 7).
 *       Only `vscode` and `path` may be there: no `child_process`, `fs`, `http`, `https`, `net`, nothing under `src/`.</li>
 * </ul>
 * <p>And the minified output must fit its `budgetBytes` (16 KiB for dist/extension.js).</p>
 *
 * <p>`contents` (a test seam) replaces the text of the entry file inside the build, so a test can plant a forbidden
 * `require` and watch this fail without touching the tree.</p>
 */
import { build } from 'esbuild';
import { isMain, optionsFor, readEntries, ROOT } from './bundle-lib.mjs';
import { join } from 'node:path';

const BANNED = ['child_process', 'fs', 'https', 'http', 'net'];
const bareName = (name) => name.replace(/^node:/u, '').split('/')[0];

/** Builds one bundle in memory, with its entry's text replaced when `contents` is given. */
async function buildInMemory(bundle, config, contents) {
  const options = optionsFor(bundle, { write: false }, config);
  if (contents !== undefined) {
    const entry = join(ROOT, bundle.entry);
    options.plugins = [{
      name: 'planted-entry',
      setup(b) {
        b.onLoad({ filter: /\.ts$/ }, (args) => (args.path === entry ? { contents, loader: 'ts', resolveDir: join(ROOT, 'shell') } : undefined));
      },
    }];
  }

  return build(options);
}

/** The problems of the firewall bundle `bundle` (empty when it is clean), and its size in bytes. */
export function firewallProblems(bundle, metafile) {
  const problems = [];
  const allowed = new Set(bundle.firewall.inputs);
  // A planted import of the old tree drags hundreds of files in: list a few and count the rest.
  const sample = (list) => (list.length > 5 ? `${list.slice(0, 5).join(', ')} and ${list.length - 5} more` : list.join(', '));
  const inputs = Object.keys(metafile.inputs);
  const old = inputs.filter((input) => input.startsWith('src/'));
  const others = inputs.filter((input) => !input.startsWith('src/') && !allowed.has(input));
  if (old.length > 0) {
    problems.push(`${bundle.out}: ${old.length} input(s) under src/ — the entry must not reach the old code: ${sample(old)}`);
  }
  if (others.length > 0) {
    problems.push(`${bundle.out}: input(s) not on the allow-list (${bundle.firewall.inputs.join(', ')}): ${sample(others)}`);
  }
  const imports = new Set(bundle.firewall.imports);
  for (const output of Object.values(metafile.outputs)) {
    for (const required of output.imports) {
      if (imports.has(required.path)) {
        continue;
      }
      const why = BANNED.includes(bareName(required.path)) ? 'a module the entry must never load' : 'not an allowed import';
      problems.push(`${bundle.out}: the bundle imports ${required.path} (${required.kind}) — ${why}; allowed: ${bundle.firewall.imports.join(', ')}`);
    }
  }

  return problems;
}

/** All problems of one bundle: the firewall, if the bundle has one, and its byte budget, if it has one. */
export async function checkBundle(bundle, config = readEntries(), contents = undefined) {
  const { metafile } = await buildInMemory(bundle, config, contents);
  const problems = bundle.firewall === undefined ? [] : firewallProblems(bundle, metafile);
  const bytes = Object.values(metafile.outputs)[0].bytes;
  if (bundle.budgetBytes !== undefined && bytes > bundle.budgetBytes) {
    problems.push(`${bundle.out}: ${bytes} bytes is over its budget of ${bundle.budgetBytes} bytes`);
  }

  return problems;
}

/** Every bundle that has a firewall or a budget. */
export async function checkAll(config = readEntries()) {
  const watched = config.bundles.filter((bundle) => bundle.firewall !== undefined || bundle.budgetBytes !== undefined);
  const lists = await Promise.all(watched.map((bundle) => checkBundle(bundle, config)));

  return lists.flat();
}

async function main() {
  const problems = await checkAll();
  for (const problem of problems) {
    console.error(`entry-firewall: ${problem}`);
  }
  if (problems.length === 0) {
    console.log('entry-firewall: ok');
  }

  return problems.length === 0 ? 0 : 1;
}

if (isMain(import.meta.url)) {
  process.exit(await main());
}
