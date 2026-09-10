// Can several extension hosts append to ONE ledger file without tearing each other's lines?
//
// The question is not academic and it is not answerable by reading. `globalStorageUri` and the coai
// data directory are SHARED by every VS Code window, so a chat ledger in one file has as many
// writers as the person has windows open. `chatOrphans.ts` answered the same question for the orphan
// ledger by putting the owner's pid in the file NAME — but that ledger is swept and deleted, and a
// usage ledger is kept for ever, so a file per host process is a directory that grows for the life
// of the installation.
//
// Both reviewers on the plan round said, independently, that two processes appending to one file
// with no cross-process lock tear their lines. That is TRUE of a read-modify-write, and of a
// positional write, and it is what happens when a writer seeks. It is not obviously true of an
// append-mode write, which is a different system call: `O_APPEND` on POSIX and `FILE_APPEND_DATA` on
// Windows are documented to place the write at the end of the file atomically with respect to other
// appenders. Node's `appendFileSync` opens with `'a'`, which is that mode.
//
// So this harness makes the claim falsifiable. Several real processes append many records of
// deliberately awkward sizes to one file at once, and the parent then checks that every line it
// reads back is one whole record and that none went missing.
//
//   node scripts/measure-append.mjs             — the default: 4 writers, 500 records each
//   node scripts/measure-append.mjs 8 1000      — heavier
//
// Exit code 0 means every line survived; 1 means the file tore, and the ledger needs a file per
// host after all.

import { appendFileSync, mkdirSync, readFileSync, rmSync } from 'node:fs';
import { fork } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { tmpdir } from 'node:os';

const HERE = fileURLToPath(import.meta.url);

/**
 * Line lengths that cross the boundaries a buffered writer would break on.
 *
 * <p>A measurement in which every record is fifty bytes proves only that fifty-byte records are
 * safe. The interesting sizes are the ones near a pipe buffer, a page and a filesystem block, and
 * the ones just over them — a real ledger line is a couple of hundred bytes, but a model name or a
 * failure sentence can make one much longer.</p>
 */
const PADDINGS = [0, 200, 4000, 8200, 60_000];

/** One record, padded to an awkward length. The padding is a single repeated character per writer. */
function record(writer, index) {
  const padding = PADDINGS[index % PADDINGS.length];

  return `${JSON.stringify({
    utc: new Date().toISOString(),
    writer,
    index,
    pad: String.fromCharCode(97 + (writer % 26)).repeat(padding),
  })}\n`;
}

function writeMany(path, writer, count) {
  for (let index = 0; index < count; index += 1) {
    appendFileSync(path, record(writer, index));
  }
}

// A forked child arrives here with its instructions in argv, and does nothing else.
if (process.argv[2] === 'write') {
  writeMany(process.argv[3], Number(process.argv[4]), Number(process.argv[5]));
  process.exit(0);
}

const writers = Number(process.argv[2] ?? 4);
const each = Number(process.argv[3] ?? 500);
const home = join(tmpdir(), `coai-append-${process.pid}`);
mkdirSync(home, { recursive: true });
const path = join(home, 'chat-usage.jsonl');

console.log(`${writers} processes × ${each} records → ${path}`);
const startedMs = Date.now();

await Promise.all(
  Array.from({ length: writers }, (_unused, writer) =>
    new Promise((resolve, reject) => {
      const child = fork(HERE, ['write', path, String(writer), String(each)], { stdio: 'inherit' });
      child.on('error', reject);
      child.on('exit', (code) => (code === 0 ? resolve() : reject(new Error(`writer ${writer} exited ${code}`))));
    })),
);

const took = Date.now() - startedMs;
const text = readFileSync(path, 'utf8');
const lines = text.split('\n').filter((line) => line.length > 0);
const seen = new Set();
let torn = 0;
for (const line of lines) {
  try {
    const row = JSON.parse(line);
    // Not merely "it parsed": the padding is what a torn write would cut, so its LENGTH is checked
    // against the length that record was written with. A line can be valid JSON and still be two
    // halves of two records if a writer's tail happened to close another's brace.
    if (row.pad.length !== PADDINGS[row.index % PADDINGS.length]) {
      torn += 1;
      continue;
    }
    seen.add(`${row.writer}:${row.index}`);
  } catch {
    torn += 1;
  }
}

const expected = writers * each;
const bytes = Buffer.byteLength(text);
console.log(`\n${lines.length} lines, ${bytes} bytes, ${took} ms`);
console.log(`whole records: ${seen.size} of ${expected}`);
console.log(`torn or unreadable lines: ${torn}`);

const clean = torn === 0 && seen.size === expected && lines.length === expected;
console.log(clean
  ? '\nEVERY record survived — one shared file is safe for appenders on this filesystem.'
  : '\nTHE FILE TORE — a shared ledger needs a file per host, or a lock.');

rmSync(home, { recursive: true, force: true });
process.exit(clean ? 0 : 1);
