#!/usr/bin/env node
/**
 * Runs the compiled test files by LISTING them, not by glob.
 *
 * <p>The glob was the bug, twice over: quoted, Linux's shell passed it verbatim and node 20
 * treated it as a literal path ("Could not find out/test/*.test.js" — the release job's first
 * failure); unquoted, Windows' cmd would pass it verbatim instead. And `node --test <dir>` turned
 * out to execute the DIRECTORY as a module on node 24. A readdir has no version, no shell and no
 * platform.</p>
 */
import { readdirSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { join } from 'node:path';

const dir = join('out', 'test');
const files = readdirSync(dir)
  .filter((f) => f.endsWith('.test.js'))
  .map((f) => join(dir, f));

if (files.length === 0) {
  console.error(`no compiled test files in ${dir} — did the compile step run?`);
  process.exit(1);
}

const result = spawnSync(process.execPath, ['--test', ...files], { stdio: 'inherit' });
process.exit(result.status ?? 1);
