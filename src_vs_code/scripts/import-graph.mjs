import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * The import graph of this extension's source, and the cycles in it.
 *
 * <p><b>Why this exists.</b> A cycle between two ES modules bundles without complaint: esbuild
 * resolves the symbols, the build is green, and the failure arrives at RUNTIME as
 * <code>Cannot access 'X' before initialization</code> or a call on <code>undefined</code> — when
 * the module that is still initialising reaches for a binding the other one has not reached yet.
 * Nothing in this repository starts an extension host, so nothing else here would see it.</p>
 *
 * <p>Splitting one 4 183-line file into fifteen is exactly the change that invents cycles, so the
 * graph is checked rather than hoped about. The check is a RATCHET, not a ban: cycles that predate
 * the check are listed and frozen, and the count may only fall. A ban would have to be argued with
 * every author of every existing one before this could land at all, and a rule nobody can satisfy
 * is a rule that gets deleted.</p>
 */

/** Only the extension's own modules: a cycle through `node:` or `vscode` is not a thing. */
const LOCAL = /^\.\.?\//u;

/** The import specifiers one source file names, relative paths only. */
export function importsOf(text) {
  const found = [];
  // `import … from 'x'`, `export … from 'x'`, and the dynamic `import('x')`.
  for (const match of text.matchAll(/(?:from|import)\s*\(?\s*'([^']+)'/gu)) {
    const [, specifier] = match;
    if (LOCAL.test(specifier)) {
      found.push(specifier.replace(/^\.\//u, '').replace(/\.js$/u, ''));
    }
  }

  return [...new Set(found)];
}

/** Every `*.ts` in a directory, by module name, with what it imports. */
export function graphOf(dir) {
  const graph = new Map();
  for (const name of readdirSync(dir)) {
    if (!name.endsWith('.ts') || name.endsWith('.d.ts')) {
      continue;
    }
    graph.set(name.replace(/\.ts$/u, ''), importsOf(readFileSync(join(dir, name), 'utf8')));
  }

  return graph;
}

/**
 * Every cycle in the graph, each as the sorted set of modules in it, de-duplicated.
 *
 * <p>Reported as a SET rather than a path because one cycle has as many paths as it has members, and
 * a reader wants to know which modules are tangled, not which arbitrary rotation was found first.</p>
 */
export function cyclesIn(graph) {
  const found = new Map();
  const onStack = [];
  const seen = new Set();

  const walk = (node) => {
    const at = onStack.indexOf(node);
    if (at >= 0) {
      const ring = [...onStack.slice(at)].sort();
      found.set(ring.join(' ↔ '), ring);

      return;
    }
    if (seen.has(node)) {
      return;
    }
    seen.add(node);
    onStack.push(node);
    for (const next of graph.get(node) ?? []) {
      if (graph.has(next)) {
        walk(next);
      }
    }
    onStack.pop();
  };

  for (const node of [...graph.keys()].sort()) {
    seen.clear();
    walk(node);
  }

  return [...found.values()].sort((one, two) => one.join().localeCompare(two.join()));
}

if (process.argv[1]?.endsWith('import-graph.mjs')) {
  const dir = join(import.meta.dirname, '..', 'src');
  const cycles = cyclesIn(graphOf(dir));
  console.log(`${cycles.length} import cycle(s) among the extension's own modules`);
  for (const ring of cycles) {
    console.log('  ' + ring.join(' ↔ '));
  }
}
