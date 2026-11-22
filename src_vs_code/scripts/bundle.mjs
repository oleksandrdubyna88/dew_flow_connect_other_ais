#!/usr/bin/env node
/**
 * `npm run bundle`: every entry of `bundle-entries.json` as its own CommonJS esbuild bundle (dist/extension.js from
 * shell/entry.ts, dist/modules/<id>.js for each module), then the entry firewall over the result. A bundle that breaks
 * the firewall or its byte budget fails the build, so `vscode:prepublish` cannot package one.
 */
import { build } from 'esbuild';
import { isMain, optionsFor, readEntries } from './bundle-lib.mjs';
import { checkAll } from './entry-firewall.mjs';

export async function main(log = console.log, error = console.error) {
  const config = readEntries();
  const results = await Promise.all(config.bundles.map(async (bundle) => {
    const { metafile } = await build(optionsFor(bundle, { write: true }, config));

    return { bundle, bytes: Object.values(metafile.outputs)[0].bytes };
  }));
  for (const { bundle, bytes } of results) {
    log(`bundle: ${bundle.out} ${bytes} bytes`);
  }
  const problems = await checkAll(config);
  for (const problem of problems) {
    error(`entry-firewall: ${problem}`);
  }

  return problems.length === 0 ? 0 : 1;
}

if (isMain(import.meta.url)) {
  process.exit(await main());
}
