import { readFileSync, realpathSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

/** The package root (`src_vs_code`), from this file's own place: the scripts run from any working directory. */
export const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');

/** `scripts/bundle-entries.json`, read each time: it is the single list of what the build emits. */
export function readEntries() {
  return JSON.parse(readFileSync(join(ROOT, 'scripts', 'bundle-entries.json'), 'utf8'));
}

/**
 * The esbuild options of one bundle. `write: false` builds in memory (the firewall and the tests), `true` writes `out`.
 * `metafile` is always on: the firewall reads inputs and `outputs[*].imports` from it.
 */
export function optionsFor(bundle, { write }, config = readEntries()) {
  return {
    absWorkingDir: ROOT,
    entryPoints: [bundle.entry],
    outfile: bundle.out,
    bundle: true,
    write,
    metafile: true,
    logLevel: 'warning',
    ...config.common,
  };
}

/** True when node was pointed at the script whose `import.meta.url` is given, so the script is importable for its test. */
export function isMain(metaUrl, argv1 = process.argv[1]) {
  try {
    return argv1 !== undefined && realpathSync(argv1) === realpathSync(fileURLToPath(metaUrl));
  } catch {
    return false;
  }
}
