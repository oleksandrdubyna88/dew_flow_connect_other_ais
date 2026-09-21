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
//   node scripts/measure-append.mjs --sync      — the 2026-09-09 call, for reproducing that result
//   node scripts/measure-append.mjs 4 200 --dir=V:\coai   — somewhere else: a NAS, a UNC path
//   node scripts/measure-append.mjs 4 500 --dotnet=2      — half the writers are `coai-mcp`'s own
//                                                           append, through `NoticeTool append`
//
// WITH `--dotnet=N` IT IS FOUR LEGS, NOT ONE. A control (one .NET writer alone, which can only tear
// against itself, so anything it reports is the harness disagreeing with the writer rather than a
// tear); the torn-tail pair (the same file cut mid-line, written by each side, which is what shows
// what the repair BUYS rather than merely that it ran); and the mixed run. The verdict is all of
// them.
//
// RESULTS SO FAR, with their conditions, because a result without them is not one:
//   2026-09-09  appendFileSync   8 × 1000, 116 MB, local NTFS  → 8000 of 8000, 0 torn
//   2026-09-16  appendFile       8 × 1000, 116 MB, local NTFS  → 8000 of 8000, 0 torn
//   2026-09-16  appendFile       4 × 200, 11.6 MB, SMB share   → 800 of 800, 0 torn
//
// AND THE RUN THIS ARGUMENT WAS BUILT FOR — 2026-09-21, `coai-mcp`'s own append, local NTFS,
// Windows 11, .NET 10.0.112. The parent plan said "the .NET append is not the system call the
// harness measured", and it was right in the way nobody wanted:
//   2026-09-21  .NET FileMode.Append   8 × 1000, 108 MB  → 5512 of 8000, 1110 torn lines
//   2026-09-21  .NET FileMode.Append   4 .NET + 4 node   → 6719 of 8000,  623 torn lines
//   2026-09-21  node appendFile        8 × 1000          → 8000 of 8000,    0 torn  (reproduced)
//
// `FileMode.Append` is not an append: .NET writes at the offset it remembered when it opened, so
// two processes overwrite each other. `AppendOnlyFile` (FILE_APPEND_DATA / O_APPEND) replaced it,
// and the same runs answer:
//   2026-09-21  AppendOnlyFile   8 × 1000, 116 MB, local NTFS      → 8000 of 8000, 0 torn
//   2026-09-21  AppendOnlyFile   4 .NET + 4 node, 116 MB, NTFS     → 8000 of 8000, 0 torn
//   2026-09-21  AppendOnlyFile   6 × 400, 35 MB, ext4 under WSL    → 2400 of 2400, 0 torn
// The torn-tail pair, both 1000 records onto a file cut mid-line: the .NET writer keeps all 1000
// and leaves the fragment quarantined on its own line; node's writer keeps 999, because the record
// it fused onto the fragment is gone. That difference is what the repair buys.
//
// Exit code 0 means every line survived; 1 means the file tore, and the ledger needs a file per
// host after all.

import { appendFileSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { appendFile } from 'node:fs/promises';
import { fork, spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { tmpdir } from 'node:os';
import { noticeTool } from './noticeTool.mjs';

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

/**
 * The writer under test — and it must be the one PRODUCTION uses.
 *
 * <p>This harness measured `appendFileSync`, and the notifications plan cited the result as proof
 * that its ledger's appends do not tear. They are not the same call: `jsonlLedger.appendLine`
 * awaits `appendFile` from `node:fs/promises`. Both open with `'a'`, so both are `O_APPEND` and the
 * ARGUMENT for why they are safe is the same argument — but the same argument is not the same
 * measurement, and a plan may not treat one as the other. (Raised by the consultation on
 * `todo/PLAN_every_message_is_written_down.md`, then verified by reading both call sites.)</p>
 *
 * <p>So the promise-based writer is now the default, and `--sync` keeps the call that produced the
 * original 2026-09-09 result, so it stays reproducible.</p>
 */
async function writeMany(path, writer, count, mode) {
  for (let index = 0; index < count; index += 1) {
    const line = record(writer, index);
    if (mode === 'sync') {
      appendFileSync(path, line);
    } else {
      await appendFile(path, line, 'utf8');
    }
  }
}

// A forked child arrives here with its instructions in argv, and does nothing else.
if (process.argv[2] === 'write') {
  await writeMany(process.argv[3], Number(process.argv[4]), Number(process.argv[5]), process.argv[6]);
  process.exit(0);
}

const args = process.argv.slice(2);
const mode = args.includes('--sync') ? 'sync' : 'promises';
/**
 * Where to measure, because WHERE is a condition of the result rather than a detail.
 *
 * <p>The default is the temp directory, which on this machine is a local NTFS disk. The coai data
 * directory is relocatable and has been a NAS share here, and SMB does not guarantee atomic append
 * — so `--dir=\\\\server\\share` is how anybody finds out whether the guarantee survives the move,
 * rather than assuming a local result covers it.</p>
 */
const chosen = args.find((a) => a.startsWith('--dir='))?.slice('--dir='.length);
const counts = args.filter((a) => !a.startsWith('--'));
const writers = Number(counts[0] ?? 4);
const each = Number(counts[1] ?? 500);

/**
 * How many of the writers are the .NET one.
 *
 * <p><b>Why this exists.</b> The three results above measured NODE appending. `coai-mcp` writes
 * `server-notices.jsonl` into the same data directory from a different runtime, and the plan that
 * built that writer says so in as many words: *"The .NET append is not the system call the harness
 * measured."* An argument about `O_APPEND` that covers both is still not a measurement of both.</p>
 *
 * <p>So `--dotnet=N` makes N of the forked writers `NoticeTool append`, which calls
 * `JsonlLedger.AppendLine` — the product's own append, the same `FileStream` flags and the same
 * single write, never a lookalike with the same arguments. The default is 0, so every earlier
 * result stays reproducible exactly as it was recorded.</p>
 *
 * <p>The records are the HARNESS's shape on both sides, not notices: the check below compares each
 * line's padding LENGTH against the length it was written with, and the notice serialiser caps
 * `detail` at 4096, so every 60 KB record would read as torn. What this measures is therefore the
 * system call, and it says nothing about the serialiser — `npm run test:parity` says that.</p>
 */
const dotnet = Number(args.find((a) => a.startsWith('--dotnet='))?.slice('--dotnet='.length) ?? 0);

const home = join(chosen ?? tmpdir(), `coai-append-${process.pid}`);
mkdirSync(home, { recursive: true });

// Built, then driven — never found and trusted. Shared with `run-parity.mjs`, which needs the same
// companion for the same reason.
const tool = dotnet > 0 ? noticeTool((line) => console.log(`measure: ${line}`)) : '';

/** One writer process: the .NET append for the first `howManyDotnet` indices, node's for the rest. */
function writerProcess(path, writer, count, howManyDotnet) {
  return new Promise((resolve, reject) => {
    const child = writer < howManyDotnet
      ? spawn('dotnet', [tool, 'append', path, String(writer), String(count)], { stdio: 'inherit' })
      : fork(HERE, ['write', path, String(writer), String(count), mode], { stdio: 'inherit' });
    child.on('error', reject);
    child.on('exit', (code) => (code === 0 ? resolve() : reject(new Error(`writer ${writer} exited ${code}`))));
  });
}

/**
 * What a file holds: whole records, and lines that are not one.
 *
 * <p>Not merely "it parsed": the padding is what a torn write would cut, so its LENGTH is checked
 * against the length that record was written with. A line can be valid JSON and still be two halves
 * of two records if a writer's tail happened to close another's brace.</p>
 */
function inspect(path) {
  const text = readFileSync(path, 'utf8');
  const lines = text.split('\n').filter((line) => line.length > 0);
  const seen = new Set();
  let torn = 0;
  for (const line of lines) {
    try {
      const row = JSON.parse(line);
      if (row.pad.length !== PADDINGS[row.index % PADDINGS.length]) {
        torn += 1;
        continue;
      }
      seen.add(`${row.writer}:${row.index}`);
    } catch {
      torn += 1;
    }
  }

  return { lines: lines.length, whole: seen.size, torn, bytes: Buffer.byteLength(text) };
}

/**
 * One leg: some writers at one file, against what the leg expected of it.
 *
 * <p>`fixture` is written first when a leg is about a file that was already damaged. `expect` is
 * what that leg CLAIMS — a leg whose expectation is "whatever happened" measures nothing.</p>
 */
async function leg(title, { name, howMany, howManyDotnet, count, fixture = '', expect }) {
  const path = join(home, name);
  if (fixture !== '') {
    writeFileSync(path, fixture);
  }
  const startedMs = Date.now();
  await Promise.all(
    Array.from({ length: howMany }, (_unused, writer) => writerProcess(path, writer, count, howManyDotnet)),
  );
  const took = Date.now() - startedMs;
  const found = inspect(path);
  const ok = found.whole === expect.whole && found.torn === expect.torn;

  console.log(`\n${title}`);
  console.log(`  ${found.lines} lines, ${found.bytes} bytes, ${took} ms`);
  console.log(`  whole records: ${found.whole} of ${expect.whole}`);
  console.log(`  torn or unreadable lines: ${found.torn}, expected ${expect.torn}`);
  console.log(`  ${ok ? 'as expected' : 'NOT WHAT THIS LEG CLAIMS'}`);

  return ok;
}

/**
 * A line that was being written when its process was killed: no closing brace, no newline.
 *
 * <p>The state the torn-tail repair exists for, and the one a measurement cannot get by waiting for
 * a crash.</p>
 */
const TORN_TAIL = '{"utc":"2026-09-21T00:00:00.000Z","writer":9,"index":0,"pad":"aaa';

const legs = [];

if (dotnet > 0) {
  // THE CONTROL, and a mixed verdict means nothing until it is clean. A .NET writer whose records
  // differed from this harness's expectation by so much as a padding length would report every line
  // torn, and the harness cannot tell that from a real tear. One writer alone can only tear against
  // itself, so anything it reports is the harness disagreeing with the writer. (Raised on the plan
  // round; the same argument as the escape-counting control in `logging-serilog.md`.)
  legs.push(await leg(`control: 1 .NET writer alone × ${each} records`, {
    name: 'control.jsonl', howMany: 1, howManyDotnet: 1, count: each,
    expect: { whole: each, torn: 0 },
  }));

  // THE REPAIR, against the same fixture, twice — and the pair is what makes it a measurement
  // rather than an assertion. The .NET writer reads the last byte before every append and prepends
  // a newline when it is not one, so the fragment stays its own unreadable line and every record
  // survives. Node's `appendFile` does not, so its first record is fused onto the fragment and is
  // lost. One leg would show a number; the pair shows what the repair BUYS.
  legs.push(await leg(`torn tail, repaired: 1 .NET writer × ${each} onto a file cut mid-line`, {
    name: 'torn-dotnet.jsonl', howMany: 1, howManyDotnet: 1, count: each, fixture: TORN_TAIL,
    expect: { whole: each, torn: 1 },
  }));
  legs.push(await leg(`torn tail, unrepaired: 1 node writer × ${each} onto the same shape`, {
    name: 'torn-node.jsonl', howMany: 1, howManyDotnet: 0, count: each, fixture: TORN_TAIL,
    expect: { whole: each - 1, torn: 1 },
  }));
}

const mix = dotnet > 0 ? `${dotnet} .NET + ${writers - dotnet} node` : `${mode} writer`;
console.log(`\n${writers} processes × ${each} records, ${mix} → ${home}`);
legs.push(await leg(`mixed: ${writers} processes × ${each} records, ${mix}`, {
  name: 'chat-usage.jsonl', howMany: writers, howManyDotnet: dotnet, count: each,
  expect: { whole: writers * each, torn: 0 },
}));

const clean = legs.every(Boolean);
console.log(clean
  ? '\nEVERY record survived — one shared file is safe for appenders on this filesystem.'
  : '\nTHE FILE TORE — a shared ledger needs a file per host, or a lock.');

rmSync(home, { recursive: true, force: true });
process.exit(clean ? 0 : 1);
