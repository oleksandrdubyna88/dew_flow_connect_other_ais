#!/usr/bin/env node
/**
 * The one-off clean-up of old `coai-*` test leftovers (E5 S2-A, D4) — a person's tool. Run it with no coai session and no VS Code window open.
 *
 *   node scripts/old-coai-leftovers.mjs list [--tmp <dir>] [--out <file>]
 *   node scripts/old-coai-leftovers.mjs remove --listing <file> [--tmp <dir>] <name>...
 *
 * `list` prints one line per entry and writes the listing (default `./old-coai-leftovers-listing.json`), which records the temp root it read.
 * `remove` takes the named picks — and only those, each only while it is still the object that was listed — and prints `removed <name>` or
 * `refused <name>: <check>`. Exit 0 when every pick was removed, 1 when any was refused, 2 on a usage error. There is no `--force`, no `--all`, no glob.
 * The logic and its checks, in order, are `scripts/lib/old-coai-leftovers.mjs`; the section of `research/module_tests.md` says what it never does.
 */
import * as fs from 'node:fs';
import * as path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const FORMS = [
  'list [--tmp <dir>] [--out <file>]',
  'remove --listing <file> [--tmp <dir>] <name>...',
];

function usage(reason) {
  console.error(`old-coai-leftovers: ${reason}\nlegal forms:\n${FORMS.map((form) => `  node scripts/old-coai-leftovers.mjs ${form}`).join('\n')}`);
  return 2;
}

/** `{ flags, names }` of `args`, or `{ error }`: only the flags named in `legal`, each with a value. */
function parse(args, legal) {
  const flags = {};
  const names = [];
  for (let i = 0; i < args.length; i += 1) {
    if (!args[i].startsWith('--')) {
      names.push(args[i]);
    } else if (legal.includes(args[i]) && i + 1 < args.length) {
      flags[args[i].slice(2)] = args[(i += 1)];
    } else {
      return { error: legal.includes(args[i]) ? `${args[i]} needs a value` : `unknown option ${args[i]}` };
    }
  }
  return { flags, names };
}

/** The product's own prefixes, from the shared file: never empty (a list that can be empty protects nothing). */
function neverSweptOf() {
  const neverSwept = JSON.parse(fs.readFileSync(path.join(here, '..', 'shared', 'temp-sweep.json'), 'utf8')).neverSwept;
  if (!Array.isArray(neverSwept) || neverSwept.length === 0) throw new Error('shared/temp-sweep.json names no product prefix; refusing to run with an empty list');
  return neverSwept;
}

function line(entry) {
  const what = entry.kind === 'quarantine' ? `interrupted clean-up of ${entry.of}${entry.empty ? ' (empty)' : ''}` : entry.claim === undefined ? '' : `claim ${entry.claim}`;
  return `${entry.kind}\t${entry.name}\t${entry.ageMinutes} min\t${what}`.trimEnd();
}

function list(lib, args, tempRoot) {
  const { flags, error } = parse(args, ['--tmp', '--out']);
  if (error !== undefined) return usage(error);
  const tmp = path.resolve(flags.tmp ?? tempRoot());
  console.log(`scanning ${tmp}`); // [S2-A code2 4/8] said before the scan: a root that cannot be read still shows which one it was
  const listing = lib.listOldLeftovers({ tmp, neverSwept: neverSweptOf() });
  const out = flags.out ?? 'old-coai-leftovers-listing.json';
  for (const entry of listing.entries) console.log(line(entry));
  for (const skipped of listing.refused) console.log(`refused\t${skipped.name}\t${skipped.check} (not one path segment on this system; remove it by hand)`); // shown FIRST: a file that cannot be written must not hide what was found
  console.log(`${listing.entries.length} entr${listing.entries.length === 1 ? 'y' : 'ies'} in ${listing.tmp}`);
  try {
    fs.writeFileSync(out, `${JSON.stringify(listing, null, 2)}\n`);
  } catch (error) {
    console.error(`old-coai-leftovers: the listing could not be written to ${out} (${error.code ?? error.message}); \`remove\` needs it, so run \`list\` again with --out <a writable file>`);
    return 2;
  }
  console.log(`listing written to ${out}`);
  return 0;
}

/** The listing of `--listing`, or the reason it cannot be used. */
function readListing(file) {
  let listing;
  try {
    listing = JSON.parse(fs.readFileSync(file, 'utf8'));
  } catch (error) {
    return { error: `cannot read the listing ${file}: ${error.code ?? error.message}` };
  }
  if (typeof listing?.tmp !== 'string' || !path.isAbsolute(listing.tmp) || !Array.isArray(listing.entries)) {
    return { error: `${file} is not a listing with an absolute temp root and its entries` };
  }
  return { listing };
}

function remove(lib, args) {
  const { flags, names, error } = parse(args, ['--listing', '--tmp']);
  if (error !== undefined) return usage(error);
  if (flags.listing === undefined || names.length === 0) return usage('remove needs --listing <file> and at least one name');
  const { listing, error: unusable } = readListing(flags.listing);
  if (unusable !== undefined) return usage(unusable);
  if (flags.tmp !== undefined && !lib.sameRoot(flags.tmp, listing.tmp)) {
    return usage(`--tmp ${flags.tmp} is not the temp root of the listing (${listing.tmp}); a listing is evidence about its own root only; omit --tmp to use the listing's root, or run \`list --tmp <dir>\` for that root`); // both as given
  }
  const onResult = (result) => console.log(lib.describeResult(result)); // as each is decided
  const results = lib.removePicked({ listing, picks: names, neverSwept: neverSweptOf(), onResult });
  return results.every((result) => result.outcome === 'removed') ? 0 : 1;
}

async function main(args) {
  let lib;
  let owned;
  try {
    lib = await import(pathToFileURL(path.join(here, 'lib', 'old-coai-leftovers.mjs')).href);
    owned = await import(pathToFileURL(path.join(here, '..', '.agents', 'conventions', 'tools', 'lib', 'owned.mjs')).href);
  } catch (error) {
    if (error?.code !== 'ERR_MODULE_NOT_FOUND') throw error;
    console.error('old-coai-leftovers: the shared conventions are not mounted; run: git submodule update --init .agents/conventions (then npm ci --ignore-scripts --prefix .agents/conventions)');
    return 2;
  }
  const [command, ...rest] = args;
  if (command === 'list') return list(lib, rest, owned.tempRoot);
  if (command === 'remove') return remove(lib, rest);
  return usage(command === undefined ? 'a command is needed' : `unknown command ${command}`);
}

try {
  process.exitCode = await main(process.argv.slice(2));
} catch (error) {
  console.error(`old-coai-leftovers: ${error.message}`);
  process.exitCode = 2;
}
