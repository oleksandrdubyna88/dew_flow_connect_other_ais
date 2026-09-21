// The two notice serialisers, run over one corpus, compared as BYTES.
//
//   npm run test:parity
//
// WHY THIS IS NOT A UNIT TEST. `server-notices.jsonl` is written by C# and read by TypeScript, and
// both halves redact before anything reaches disk. Each half has a suite that compares it to what
// its author expected — and two suites can agree with their own expectations while disagreeing with
// each other, which is a secret written by one half that the other would have taken out. The only
// honest check runs BOTH implementations over the same inputs. This is that check.
//
// WHY IT LIVES IN THE EXTENSION JOB. `.github/workflows/ci.yml` orders the work `dotnet build` ->
// the .NET suites -> `npm ci` -> `test:contract` -> `test:seam`. A C# test cannot spawn node,
// because node is not installed when the .NET tests run. The extension job is the only place both
// runtimes exist, so the script lives here and drives `NoticeTool` — a test-only executable built by
// the solution, never published, the same shape as `FakeCli`.
//
// WHAT IT DOES NOT PROVE: that either half is RIGHT. It proves they are the same. `notifications.ts`
// is the contract, and its own suite is what says the contract is what it should be.
import { execFileSync, spawnSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = join(HERE, '..');
const REPO = join(ROOT, '..');

/** How long the companion may take before this gives up rather than hanging a CI job. */
const TIMEOUT_MS = 120_000;

function fail(why) {
  console.error(`parity: ${why}`);
  process.exit(1);
}

/**
 * The built companion, or a refusal that says how to build it.
 *
 * <p>It REFUSES rather than skipping. A parity check that quietly passes when it could not find the
 * thing it compares against is worse than no check: it is a green tick over an unasked question.</p>
 */
function companion() {
  const named = process.env['COAI_NOTICE_TOOL'];
  if (named !== undefined && named.length > 0) {
    return named;
  }
  for (const configuration of ['Release', 'Debug']) {
    const candidate = join(REPO, 'src_mcp', 'tests_notices', 'bin', configuration, 'net10.0', 'NoticeTool.dll');
    if (existsSync(candidate)) {
      return candidate;
    }
  }

  return '';
}

/** One run of the companion: a JSON array in, a JSON array out. */
function ask(tool, verb, args, payload) {
  const run = spawnSync('dotnet', [tool, verb, ...args], {
    input: JSON.stringify(payload),
    encoding: 'utf8',
    timeout: TIMEOUT_MS,
    maxBuffer: 64 * 1024 * 1024,
  });

  if (run.error !== undefined) {
    fail(`could not run the companion: ${run.error.message}`);
  }
  if (run.status !== 0) {
    fail(`the companion exited ${run.status}\n${run.stderr}`);
  }

  try {
    return JSON.parse(run.stdout);
  } catch (reason) {
    fail(`the companion did not answer JSON (${reason.message}): ${run.stdout.slice(0, 400)}`);
  }

  return [];
}

/**
 * A string as its UTF-16 CODE UNITS, which is how both halves' answers cross.
 *
 * <p>Nothing on this path encodes text, and the harness is what taught me why. Its first version
 * compared JSON strings, and the first run reported a difference at code unit 999 of a 1000-unit
 * cut — JavaScript keeps the lone HIGH SURROGATE `slice` leaves behind when it cuts through an
 * emoji, and .NET's JSON encoder had replaced it with U+FFFD on the way out. The port was right and
 * the transport was lying about it. A lone surrogate, a NUL, U+2028 and a non-breaking space all
 * survive as numbers and none of them survives a round trip through an encoder.</p>
 */
function units(text) {
  const out = [];
  for (let at = 0; at < text.length; at += 1) {
    out.push(text.charCodeAt(at));
  }

  return out;
}

/** Where two code-unit arrays first differ. */
function firstDifference(left, right) {
  const shortest = Math.min(left.length, right.length);
  for (let at = 0; at < shortest; at += 1) {
    if (left[at] !== right[at]) {
      return { at, left: left[at], right: right[at] };
    }
  }

  return { at: shortest, left: left.length, right: right.length };
}

function compare(what, inputs, ours, theirs) {
  if (ours.length !== theirs.length) {
    fail(`${what}: ${ours.length} answers from TypeScript and ${theirs.length} from C#`);
  }
  let differed = 0;
  for (let at = 0; at < ours.length; at += 1) {
    if (ours[at].length === theirs[at].length
      && ours[at].every((unit, where) => unit === theirs[at][where])) {
      continue;
    }
    differed += 1;
    if (differed <= 3) {
      const where = firstDifference(ours[at], theirs[at]);
      console.error(`\n${what} case ${at} differs at code unit ${where.at}`);
      console.error(`  input      ${JSON.stringify(inputs[at]).slice(0, 200)}`);
      console.error(`  TypeScript ${JSON.stringify(String.fromCharCode(...ours[at])).slice(0, 200)}`);
      console.error(`  C#         ${JSON.stringify(String.fromCharCode(...theirs[at])).slice(0, 200)}`);
      console.error(`  unit there TypeScript ${where.left}, C# ${where.right}`);
    }
  }
  if (differed > 0) {
    fail(`${what}: ${differed} of ${ours.length} cases differ — one half of this product would `
      + 'write a secret the other would have taken out');
  }
  console.log(`  ok  ${what}: ${ours.length} cases, byte for byte`);
}

// BUILT HERE, not found here. A reviewer pointed out what `companion()` alone allows: edit
// `Redaction.cs`, run `npm run test:parity`, and the script recompiles the TypeScript and compares
// it against yesterday's DLL — green, about code that no longer exists. CI is safe by ordering (the
// solution build is three steps above this one and the job is fail-fast); a developer's machine is
// not, and this check is most useful exactly when somebody is changing the redactor.
console.log('parity: building NoticeTool from the current sources...');
const built = spawnSync(
  'dotnet',
  ['build', join(REPO, 'src_mcp', 'tests_notices', 'NoticeTool.csproj'), '-v', 'q', '--nologo'],
  { encoding: 'utf8', timeout: TIMEOUT_MS * 4, shell: false },
);
if (built.status !== 0) {
  fail(`NoticeTool would not build:\n${built.stdout}${built.stderr}`);
}

const tool = companion();
if (tool === '') {
  fail('no NoticeTool build found. Run: dotnet build src_mcp/tests_notices/NoticeTool.csproj');
}

// Compiled, then imported — the same road `run-seam.mjs` takes. The TypeScript half under test is
// the PRODUCT's module, never a copy of it. Announced, because a silent step that takes ten seconds
// on a fresh checkout reads as a hang.
console.log('parity: compiling the extension...');
execFileSync('npm', ['run', 'compile'], { cwd: ROOT, stdio: 'ignore', shell: process.platform === 'win32' });
const { safeText, notificationLine, parseNotificationLine, TITLE_LIMIT, DETAIL_LIMIT }
  = await import('../out/notifications.js');
const { noticeShapes, noticeRecords } = await import('../out/test/noticeShapes.js');

const shapes = noticeShapes();
console.log(`parity: ${shapes.length} shapes, ${noticeRecords().length} records, against ${tool}`);

for (const limit of [TITLE_LIMIT, DETAIL_LIMIT]) {
  compare(
    `safeText at ${limit}`,
    shapes,
    shapes.map((one) => units(safeText(one, limit))),
    ask(tool, 'safe-text', [String(limit)], shapes),
  );
}

// THE LINE IS COMPARED THROUGH THE PARSER, NOT BYTE FOR BYTE, and finding out why is the most
// useful thing this harness has done. The first version demanded byte equality and reported a
// difference: both halves wrote 4408 code units of identical content, ordered differently — `pid`
// and `seq` before `provider` in TypeScript and after it in C#.
//
// Neither is wrong. `JSON.stringify` writes an object's properties in the order the CALLER inserted
// them, so the TypeScript line's order is a property of the call site rather than of the serialiser;
// C# has a fixed field list and cannot reproduce an arbitrary caller's order. Demanding byte
// equality here is demanding something the two languages cannot deliver, and a check that cannot
// pass is a check that gets deleted.
//
// What the product actually needs is narrower and stronger:
//   1. the REDACTION is identical — that is the `safeText` comparison above, byte for byte, and it
//      is the half where a difference means a secret on disk;
//   2. the server's line PARSES into the record the server meant, using the extension's own parser,
//      which is the only reader this file will ever have;
//   3. the server's line is a FIXED POINT of that parser — `notificationLine(parse(line)) === line` —
//      so a record read and written again is the record that arrived. Story 2.4 reads the real
//      file with this parser, and this is the property that makes that check mean something.
const records = noticeRecords();
const theirLines = ask(tool, 'serialise', [], records).map((one) => String.fromCharCode(...one));

for (let at = 0; at < records.length; at += 1) {
  const line = theirLines[at];
  // `trimEnd` rather than a regex: the only thing to remove is the trailing
  // newline the serialiser appends, and a literal newline inside a regex literal
  // is how this line was broken in the first place.
  const parsed = parseNotificationLine(line.trimEnd());
  if (parsed === undefined) {
    fail(`notificationLine case ${at}: the extension's own parser REFUSED the server's line.
`
      + `  ${JSON.stringify(line).slice(0, 300)}`);
  }

  // (2) every field the server was given came back, redacted the way the extension would have
  // redacted it. Compared against the TypeScript serialiser's own parse of its own line, so the
  // expectation is the contract rather than this script's idea of it.
  // Both sides rendered with the SAME key order, so the comparison is about values rather than
  // about the property order neither language controls.
  const ours = parseNotificationLine(notificationLine(records[at]).trimEnd());
  const mine = JSON.stringify(ours, Object.keys(ours).sort());
  const theirs = JSON.stringify(parsed, Object.keys(parsed).sort());
  if (mine !== theirs) {
    const where = firstDifference(units(mine), units(theirs));
    fail(`notificationLine case ${at}: the two halves parse to different records, first at `
      + `${where.at}`
      + `\n  TypeScript ${mine.slice(Math.max(0, where.at - 80), where.at + 80)}`
      + `\n  C#         ${theirs.slice(Math.max(0, where.at - 80), where.at + 80)}`);
  }

  // (3) the fixed point.
  const again = notificationLine(parsed);
  if (again !== line) {
    const where = firstDifference(units(line), units(again));
    fail(`notificationLine case ${at}: the server's line is not a fixed point of the extension's `
      + `parser, first differing at code unit ${where.at} (${where.left} against ${where.right}). A `
      + 'record read and written again would not be the record that arrived.');
  }
}
console.log(`  ok  notificationLine: ${records.length} records parse, agree and round-trip`);

// And the codes, because the redactor must be a no-op on every one of them: a code it rewrote would
// be a persisted key that no longer matches the key the suppressor admitted.
const codes = ask(tool, 'codes', [], []).map((one) => String.fromCharCode(...one));
if (codes.length === 0) {
  fail('the companion listed no codes, so the check below asserts nothing');
}
compare(
  'codes are untouched by the redactor',
  codes,
  codes.map(units),
  ask(tool, 'safe-text', ['1000'], codes),
);

console.log('parity: ok — the two serialisers answer identically');
