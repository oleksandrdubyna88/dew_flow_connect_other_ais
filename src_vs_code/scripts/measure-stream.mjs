/**
 * Phase 0 of `todo/PLAN_an_answer_as_it_arrives.md`: does a vendor CLI say anything BEFORE it is
 * finished, and if so, what carries it?
 *
 * <p>A turn is 9.4 s measured and eight of those seconds are silent. Whether that silence is
 * unavoidable is not a thing to reason about — the three-adapter plan already recorded one
 * written-down assumption about these CLIs that measurement found half wrong. So this drives the
 * real binaries and reports what actually arrived, and the plan's decision follows the table rather
 * than the other way round.</p>
 *
 * <p><b>Run it as `npm run measure:stream`</b>, optionally `-- <vendor>` and `-- --lines N`. It spends
 * real turns on real signed-in accounts, which is why it is a script and not a test.</p>
 *
 * <p><b>Exit code:</b> `0` when every run reached a terminal event and exited cleanly; `1` when any
 * run FAILED, because a failed run is not evidence about streaming and a table built on one would be
 * a lie; `2` for a bad argument. The code is the report — nothing downstream should have to read the
 * prose to learn whether the numbers are trustworthy.</p>
 *
 * <h2>How a DELTA is identified, which is the whole credibility of this measurement</h2>
 *
 * <p>The first version scraped every nested string out of every event and accepted any that appeared
 * somewhere in the final answer, then asked whether they joined up. All three vendors' reviewers
 * found the same hole independently: for a numeric answer, unrelated metadata like `12` and `345`
 * can pass a containment test and "reconstruct" an answer nobody streamed. A measurement that can
 * say YES when the truth is NO is worse than no measurement.</p>
 *
 * <p>So a delta must now satisfy three conditions at once, and they are deliberately strict:</p>
 * <ol>
 *   <li><b>It matches at an ADVANCING OFFSET.</b> Each accepted fragment must be exactly what comes
 *       next in the answer, from a cursor that only moves forward. `12` cannot match unless the
 *       answer's next unconsumed characters are `12`.</li>
 *   <li><b>The fragments TILE the answer.</b> The cursor must reach the end — the pieces, in arrival
 *       order, must account for the whole answer with nothing left over.</li>
 *   <li><b>They all come from ONE JSON path.</b> A real stream comes from one field of one event
 *       type. Fragments scattered across different paths are metadata that happened to line up, and
 *       the path is also the thing Phase 1 needs to know in order to parse it.</li>
 * </ol>
 *
 * <h2>What else the round insisted on</h2>
 *
 * <ul>
 *   <li><b>The launch spec is IMPORTED, never retyped</b>, so this cannot measure a mode the product
 *       does not run.</li>
 *   <li><b>A failure is never counted as "no streaming"</b> — hung, rate-limited, non-zero exit and
 *       unspawnable are each their own outcome.</li>
 *   <li><b>Chunks, not lines.</b> Arrival is stamped on the raw stdout `data` event, because a
 *       line reader hides the difference between a trickle and one 8 KB release.</li>
 *   <li><b>Direct spawn AND `cmd.exe`</b>, wherever the resolved file permits both.</li>
 *   <li><b>The child is killed as a TREE.</b> With `shell: true` the direct child is `cmd.exe`, and
 *       killing it would orphan the vendor CLI — still signed in, still spending a turn.</li>
 *   <li><b>Raw stdout chunks are persisted</b>, not just the classification, so the decision can be
 *       re-derived by somebody who doubts it.</li>
 *   <li><b>Paths are anchored to this file</b>, not to `process.cwd()`, so transcripts cannot land
 *       outside the git-ignored directory when it is run from the repository root.</li>
 * </ul>
 *
 * <p><b>Retention.</b> `src_vs_code/measurements/` is git-ignored and owned by whoever runs this
 * script: one file per invocation, a few hundred kB each, deleted by hand. It is a scratch directory
 * for evidence behind a table, not a store anything reads — nothing in the product opens it, and
 * losing it costs a re-run.</p>
 */
import { spawn } from 'node:child_process';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

/** Anchored to THIS file, never to the working directory. See the header. */
const HERE = fileURLToPath(new URL('.', import.meta.url));
const OUT = new URL('../out/', import.meta.url).href;
const MEASUREMENTS = join(HERE, '..', 'measurements');

const { launchSpecFor } = await import(`${OUT}cliChatLaunch.js`);
const { resolvedExecutable } = await import(`${OUT}versionProbe.js`);
const { agyAdapter } = await import(`${OUT}agyAdapter.js`);
const { claudeAdapter } = await import(`${OUT}claudeAdapter.js`);
const { codexAdapter } = await import(`${OUT}codexAdapter.js`);

const VENDORS = {
  claude: { runtime: 'claude', adapter: claudeAdapter, executable: 'claude' },
  codex: { runtime: 'codex', adapter: codexAdapter, executable: 'codex' },
  agy: { runtime: 'antigravity', adapter: agyAdapter, executable: 'agy' },
};

const USAGE = `usage: npm run measure:stream -- [vendor|all] [--lines N]

  vendor    one of ${Object.keys(VENDORS).join(', ')}, or "all" (the default)
  --lines   how many lines to ask the answer to be, 1..2000 (default 40)

The answer's LENGTH is the second variable: a stream whose deltas all arrive in the last
fraction of a second spares nobody, and whether that fraction grows with the answer is what
the decision turns on.`;

/** Options parsed independently of the optional positional vendor, so either order works. */
function optionsFrom(argv) {
  const args = argv.slice(2);
  if (args.includes('--help') || args.includes('-h')) {
    return { help: true };
  }
  const at = args.indexOf('--lines');
  let lines = 40;
  if (at >= 0) {
    const given = args[at + 1] ?? '';
    if (!/^[0-9]{1,4}$/.test(given) || Number(given) < 1 || Number(given) > 2000) {
      return { error: `--lines needs a whole number from 1 to 2000, not "${given}"` };
    }
    lines = Number(given);
  }
  const positional = args.filter((arg, index) => !arg.startsWith('--') && index !== at + 1);
  const vendor = positional[0] ?? 'all';
  if (vendor !== 'all' && VENDORS[vendor] === undefined) {
    return { error: `unknown vendor: ${vendor}` };
  }

  return { lines, vendor };
}

const options = optionsFrom(process.argv);
if (options.help === true) {
  console.log(USAGE);
  process.exit(0);
}
if (options.error !== undefined) {
  console.error(`${options.error}\n\n${USAGE}`);
  process.exit(2);
}

const LINES = options.lines;

/**
 * The one prompt, pinned and printed.
 *
 * <p>Mechanical, so the answer can be checked without judging prose; synthetic, so nothing from this
 * machine reaches a file; and long enough that one chunk cannot hold it, because "2+2?" comes back
 * whole from a CLI that streams perfectly well and that would be a false negative.</p>
 */
const PROMPT =
  `Write the numbers from 1 to ${LINES}, one per line, as plain text. `
  + `No commentary, no code block, no explanation — just the ${LINES} lines.`;

/** How many measured runs per arm, after one warm-up that is recorded but not counted. */
const RUNS = 3;

/** A run that has said nothing terminal by now is a failure, not a silent CLI. */
const TIMEOUT_MS = 180_000;

/** Past this, a runaway CLI is killed rather than allowed to exhaust the heap and take the run with it. */
const MAX_BYTES = 32 * 1024 * 1024;

const row = (runtime) => ({
  id: runtime, runtime, model: '', enabled: true, plan: true, code: true,
  baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0,
});

/**
 * Kill a whole process TREE.
 *
 * <p>With `shell: true` the direct child is `cmd.exe` and the vendor CLI is its child, so
 * `child.kill()` reaps the shell and orphans the thing that is actually spending a signed-in turn.
 * Windows has no process groups to signal, so `taskkill /T /F` is the way. (Three reviewers.)</p>
 */
function killTree(child) {
  if (child.pid === undefined) {
    return;
  }
  if (process.platform === 'win32') {
    try {
      spawn('taskkill.exe', ['/pid', String(child.pid), '/t', '/f'], { windowsHide: true, stdio: 'ignore' });
    } catch {
      child.kill();
    }

    return;
  }
  child.kill();
}

/**
 * One run of one CLI, stamped.
 *
 * <p>Returns every stdout chunk with the millisecond it arrived, the exit code, and how it ended.
 * Nothing here interprets the bytes — that is `classify`'s job, and keeping them apart is what lets
 * the raw transcript be re-read by somebody who doubts the classification.</p>
 */
function runOnce(spec, useShell, onHeartbeat) {
  return new Promise((resolve) => {
    const started = Date.now();
    const chunks = [];
    let stderr = '';
    let ending = '';
    let bytes = 0;
    let settled = false;
    const finish = (result) => {
      if (settled) {
        return;
      }
      settled = true;
      resolve({ chunks, stderr, ms: Date.now() - started, ...result });
    };

    let child;
    try {
      child = spawn(spec.executable, spec.args, {
        cwd: spec.cwd,
        shell: useShell,
        stdio: ['pipe', 'pipe', 'pipe'],
        windowsHide: true,
      });
    } catch (reason) {
      // Node ≥ 20 REFUSES to spawn a `.cmd` without a shell — `EINVAL`, thrown synchronously, since
      // the fix for CVE-2024-27980. That is not a measurement failure, it is a measurement RESULT:
      // for a CLI installed as a shim there is no direct arm to compare against, because the product
      // cannot have one either.
      finish({ code: -1, ending: 'unspawnable', failure: String(reason) });

      return;
    }

    const timer = setTimeout(() => {
      ending = 'timeout';
      killTree(child);
      // Resolved here rather than waiting for `close`: a killed tree on Windows does not always
      // deliver one, and a run that never settles hangs the whole harness.
      finish({ code: -1, ending: 'timeout', failure: `no terminal event within ${TIMEOUT_MS} ms` });
    }, TIMEOUT_MS);

    child.stdout.on('data', (data) => {
      // Stamped HERE, on the raw chunk, before any line splitting: a stream held back and released
      // in one write is indistinguishable from a trickling one once you only count lines.
      const text = data.toString('utf8');
      bytes += text.length;
      if (bytes > MAX_BYTES) {
        ending = 'too much output';
        killTree(child);
        clearTimeout(timer);
        finish({ code: -1, ending: 'overflow', failure: `more than ${MAX_BYTES} bytes of stdout` });

        return;
      }
      chunks.push({ atMs: Date.now() - started, text });
      onHeartbeat?.(chunks.length);
    });
    child.stderr.on('data', (data) => {
      stderr += data.toString('utf8');
    });
    // Without this, a CLI that exits at once — a refused sign-in, a rate limit — turns the write
    // below into an unhandled EPIPE that takes the whole harness down instead of recording a
    // FAILED run. (codex and gemini, the code round.)
    child.stdin.on('error', () => undefined);
    child.on('error', (reason) => {
      clearTimeout(timer);
      finish({ code: -1, ending: 'error', failure: String(reason) });
    });
    child.on('close', (code) => {
      clearTimeout(timer);
      finish({ code: code ?? -1, ending: ending || 'closed', failure: '' });
    });

    try {
      child.stdin.write(spec.encode(PROMPT));
      child.stdin.end();
    } catch (reason) {
      clearTimeout(timer);
      finish({ code: -1, ending: 'stdin', failure: String(reason) });
    }
  });
}

/** Every complete NDJSON line in arrival order, each carrying the chunk time it completed in. */
function linesOf(chunks) {
  const lines = [];
  let held = '';
  for (const chunk of chunks) {
    held += chunk.text;
    const parts = held.split(/\r?\n/);
    held = parts[parts.length - 1] ?? '';
    for (const part of parts.slice(0, -1)) {
      if (part.trim().length > 0) {
        lines.push({ atMs: chunk.atMs, text: part });
      }
    }
  }
  if (held.trim().length > 0) {
    lines.push({ atMs: chunks[chunks.length - 1]?.atMs ?? 0, text: held });
  }

  return lines;
}

/** What one NDJSON line IS, in the vendor's own vocabulary — never guessed from its shape. */
function kindOf(event) {
  const type = typeof event['type'] === 'string' ? event['type'] : '';
  const named = typeof event['event'] === 'string' ? event['event'] : '';
  const subtype = typeof event['subtype'] === 'string' ? `.${event['subtype']}` : '';
  const item = event['item'];
  const itemType =
    item !== null && typeof item === 'object' && typeof item['type'] === 'string' ? `.${item['type']}` : '';

  return `${type || named || 'unknown'}${subtype}${itemType}`;
}

/**
 * Every string in an event, WITH the JSON path it was found at.
 *
 * <p>The path is what makes a delta attributable: a real stream comes out of one field of one event
 * type, and fragments scattered across several paths are metadata that happened to line up. It is
 * also the thing Phase 1 needs, since it has to parse that exact field.</p>
 */
function stringsWithPaths(value, path = '', depth = 0, found = []) {
  if (typeof value === 'string') {
    found.push({ path, text: value });

    return found;
  }
  if (depth > 6 || value === null || typeof value !== 'object') {
    return found;
  }
  for (const [key, inner] of Object.entries(value)) {
    // Array indices are collapsed, so `content[0].text` and `content[1].text` are ONE path — a
    // stream that arrives as successive array entries is still one field.
    const step = Array.isArray(value) ? '[]' : key;
    stringsWithPaths(inner, path.length > 0 ? `${path}.${step}` : step, depth + 1, found);
  }

  return found;
}

/** Whether an event mentions token usage at all, and under which key. */
function usageKeysOf(event) {
  return stringsWithPaths(event)
    .map((one) => one.path)
    .concat(pathsOfNumbers(event))
    .filter((path) => /(^|\.)(usage|total_cost_usd|input_tokens|output_tokens|tokens_in|tokens_out)(\.|$)/i.test(path));
}

/** Numeric leaves, by path — token counts are numbers, and `stringsWithPaths` only sees strings. */
function pathsOfNumbers(value, path = '', depth = 0, found = []) {
  if (typeof value === 'number') {
    found.push(path);

    return found;
  }
  if (depth > 6 || value === null || typeof value !== 'object') {
    return found;
  }
  for (const [key, inner] of Object.entries(value)) {
    pathsOfNumbers(inner, path.length > 0 ? `${path}.${key}` : key, depth + 1, found);
  }

  return found;
}

const squash = (text) => text.replace(/\s+/g, '');

/**
 * What this run proved.
 *
 * <p>See the header for the three conditions a delta has to meet. The short version: it must be the
 * NEXT thing in the answer, the pieces must tile the whole answer in order, and they must all come
 * from one JSON path. Anything weaker lets a numeric answer be "reconstructed" out of unrelated
 * metadata, which is the false positive all three vendors' reviewers found in the first version.</p>
 */
function classify(run, adapter) {
  const lines = linesOf(run.chunks);
  const events = [];
  let answer = '';
  let terminalAtMs = -1;

  for (const line of lines) {
    let event;
    try {
      event = JSON.parse(line.text);
    } catch {
      events.push({ atMs: line.atMs, kind: 'not-json', strings: [], usage: [] });
      continue;
    }
    const seen = adapter.classify(line.text);
    if (seen.kind === 'answer') {
      answer = seen.text;
      terminalAtMs = line.atMs;
    }
    if (seen.kind === 'failure') {
      terminalAtMs = line.atMs;
    }
    events.push({
      atMs: line.atMs,
      kind: kindOf(event),
      adapterKind: seen.kind,
      strings: stringsWithPaths(event).filter((one) => one.text.trim().length > 0),
      usage: [...new Set(usageKeysOf(event))],
    });
  }

  const flat = squash(answer);
  const deltas = [];
  let cursor = 0;
  for (const event of events) {
    if (event.adapterKind === 'answer' || flat.length === 0) {
      continue;
    }
    for (const one of event.strings) {
      const piece = squash(one.text);
      // The advancing offset IS the strictness. A fragment is accepted only when it is exactly what
      // comes next, from a cursor that never goes backwards, so a stray `12` cannot match unless the
      // answer's next unconsumed characters are `12`.
      // One character is enough to be a delta, and excluding them was a false negative of my own
      // making: `agy` really does emit a single-character fragment when a token straddles a boundary
      // ("…16\n1" then "7\n18\n"), and dropping it stalled the cursor so that every later fragment
      // failed to match and a streaming vendor was reported as not streaming. Safety does not come
      // from the minimum length — it comes from the three conditions below the loop: the pieces must
      // tile the WHOLE answer, in order, from ONE path.
      if (piece.length >= 1 && cursor < flat.length && flat.startsWith(piece, cursor)) {
        deltas.push({ atMs: event.atMs, kind: event.kind, path: one.path, chars: piece.length });
        cursor += piece.length;
      }
    }
  }
  const paths = [...new Set(deltas.map((delta) => delta.path))];
  // Tiled the WHOLE answer, in order, from ONE field, in more than one piece. Anything less is not a
  // stream: one piece covering everything is an early final, and several paths is coincidence.
  const streams = deltas.length > 1 && cursor === flat.length && paths.length === 1;

  const wholeAnswerEarly = events.some(
    (event) => event.adapterKind !== 'answer' && event.strings.some((one) => squash(one.text) === flat),
  );

  return {
    ending: run.ending,
    failure: run.failure,
    unspawnable: run.ending === 'unspawnable',
    ok: run.ending === 'closed' && run.code === 0 && terminalAtMs >= 0 && answer.length > 0,
    ms: run.ms,
    code: run.code,
    stderr: run.stderr.trim().slice(-300),
    answerChars: answer.length,
    terminalAtMs,
    streams,
    deltaCount: deltas.length,
    coveredChars: cursor,
    answerFlatChars: flat.length,
    deltaPaths: paths,
    deltaKinds: [...new Set(deltas.map((delta) => delta.kind))],
    firstDeltaAtMs: deltas.length > 0 ? deltas[0].atMs : -1,
    /** How much of the wait a person would actually be spared. The number the decision turns on. */
    streamWindowMs: streams && terminalAtMs >= 0 ? terminalAtMs - deltas[0].atMs : -1,
    wholeAnswerEarly: !streams && wholeAnswerEarly,
    chunkCount: run.chunks.length,
    kinds: [...new Set(events.map((event) => event.kind))],
    usageKinds: [...new Set(events.filter((event) => event.usage.length > 0).map((event) => event.kind))],
    usageKeys: [...new Set(events.flatMap((event) => event.usage))].slice(0, 8),
    usageAtMs: events.find((event) => event.usage.length > 0)?.atMs ?? -1,
    events,
  };
}

async function measure(name, useShell, raw) {
  const vendor = VENDORS[name];
  const resolved = await resolvedExecutable(vendor.executable);
  if (!resolved) {
    console.log(`${name.padEnd(8)} NOT INSTALLED`);

    return undefined;
  }
  const home = mkdtempSync(join(tmpdir(), 'coai-measure-'));
  const spec = launchSpecFor(row(vendor.runtime), home, '', resolved);
  if (spec.refusal.length > 0) {
    console.log(`${name.padEnd(8)} REFUSED: ${spec.refusal}`);
    rmSync(home, { recursive: true, force: true });

    return undefined;
  }
  spec.encode = (text) => vendor.adapter.encode(text);

  const arm = useShell ? 'cmd.exe' : 'direct';
  const shipped = spec.shell === useShell;
  const results = [];
  let unspawnable = false;
  try {
    for (let attempt = 0; attempt <= RUNS; attempt += 1) {
      const warm = attempt === 0;
      const label = `${name.padEnd(8)} ${arm.padEnd(8)}${shipped ? '*' : ' '} ${warm ? 'warm-up' : `run ${attempt}`}`;
      // A heartbeat, because a run can sit for three minutes and a frozen line says nothing about
      // whether bytes are arriving or the CLI is wedged. (gemini and the local reviewer.)
      // Only on a terminal: `\r` redraws a line for a person watching, and appends noise to a log
      // when the output is piped or captured.
      const live = process.stdout.isTTY === true;
      const beat = (count) => {
        if (live) {
          process.stdout.write(`\r${label} … ${count} chunks`);
        }
      };
      if (live) {
        process.stdout.write(`${label} … waiting`);
      }
      const run = await runOnce(spec, useShell, beat);
      const seen = classify(run, vendor.adapter);
      process.stdout.write('\r');
      console.log(
        `${label} … `
        + (seen.unspawnable
          ? 'IMPOSSIBLE — node refuses to spawn this file without a shell'
          : seen.ok
            ? `${seen.ms} ms · ${seen.deltaCount} deltas · ${seen.streams ? `STREAMS, window ${seen.streamWindowMs} ms` : seen.wholeAnswerEarly ? 'whole answer once, early' : 'no stream'}`
            : `FAILED (${seen.ending}, code ${seen.code}) ${seen.stderr.slice(0, 100)}`),
      );
      // The RAW stdout chunks are persisted BESIDE the classification, with their arrival times and
      // the stderr tail, so a reader who doubts the delta rule can re-derive the whole answer from
      // the bytes rather than having to trust this script's summary of them. (codex, twice.)
      raw.push({
        vendor: name, arm, shipped, attempt, warm,
        ...seen,
        events: undefined,
        stdoutChunks: run.chunks,
        stderrTail: run.stderr.slice(-4000),
      });
      if (!warm) {
        results.push(seen);
      }
      if (seen.unspawnable) {
        unspawnable = true;
        break;
      }
    }
  } finally {
    rmSync(home, { recursive: true, force: true });
  }

  return { vendor: name, arm, shipped, unspawnable, results, executable: spec.executable };
}

const names = options.vendor === 'all' ? Object.keys(VENDORS) : [options.vendor];

console.log(`prompt: ${PROMPT}`);
console.log(`answer length asked for: ${LINES} lines`);
console.log(`runs: ${RUNS} measured + 1 warm-up per arm · timeout ${TIMEOUT_MS} ms · node ${process.version}\n`);

const raw = [];
const arms = [];
for (const name of names) {
  for (const useShell of [false, true]) {
    const seen = await measure(name, useShell, raw);
    if (seen !== undefined) {
      arms.push(seen);
    }
  }
}

mkdirSync(MEASUREMENTS, { recursive: true });
const stamp = new Date().toISOString().replace(/[:.]/g, '-');
const file = join(MEASUREMENTS, `stream-${stamp}.json`);
writeFileSync(file, JSON.stringify({ prompt: PROMPT, lines: LINES, node: process.version, runs: raw }, null, 2));

const median = (values) => {
  const sorted = [...values].sort((a, b) => a - b);

  return sorted.length === 0 ? -1 : sorted[Math.floor(sorted.length / 2)];
};

console.log('\n=== the table ===\n');
console.log('| vendor | arm | ok | turn ms | deltas | streams? | stream window ms | delta path | usage event | usage at ms |');
console.log('|---|---|---|---|---|---|---|---|---|---|');
for (const arm of arms) {
  const good = arm.results.filter((seen) => seen.ok);
  const label = `${arm.arm}${arm.shipped ? ' (shipped)' : ''}`;
  if (arm.unspawnable) {
    console.log(`| ${arm.vendor} | ${label} | n/a | — | — | — | — | — | — | node refuses this file without a shell |`);
    continue;
  }
  const one = good[0];
  const verdict = good.length === 0
    ? 'no good run'
    : good.every((seen) => seen.streams)
      ? '**yes**'
      : one?.wholeAnswerEarly ? 'no — whole answer, once, early' : 'no';
  console.log(
    `| ${arm.vendor} | ${label} | ${good.length}/${arm.results.length} | ${median(good.map((s) => s.ms))} `
    + `| ${median(good.map((s) => s.deltaCount))} | ${verdict} | ${median(good.map((s) => s.streamWindowMs))} `
    + `| ${(one?.deltaPaths ?? []).join(' · ') || '—'} `
    + `| ${(one?.usageKinds ?? []).join(' · ') || '—'} | ${median(good.map((s) => s.usageAtMs))} |`,
  );
}

console.log('\n=== what each vendor emitted, in order (first good run of the shipped arm) ===\n');
for (const arm of arms.filter((one) => one.shipped)) {
  const one = arm.results.find((seen) => seen.ok);
  if (one === undefined) {
    console.log(`${arm.vendor}: no good run\n`);
    continue;
  }
  console.log(`${arm.vendor} — ${one.events.length} events, answer ${one.answerChars} chars, covered ${one.coveredChars}/${one.answerFlatChars}:`);
  for (const event of one.events) {
    console.log(
      `  ${String(event.atMs).padStart(7)} ms  ${(event.kind ?? '').padEnd(28)} `
      + `${(event.adapterKind ?? '').padEnd(8)} ${event.usage?.length > 0 ? 'USAGE' : '     '}`,
    );
  }
  console.log('');
}

const anyFailed = arms.some((arm) => arm.results.some((seen) => !seen.ok && !seen.unspawnable));
console.log(`raw transcripts: ${file}`);
console.log(
  anyFailed
    ? 'SOME RUNS FAILED — a failed run is NOT evidence that a CLI does not stream. Exit 1.'
    : 'every run reached a terminal event and exited 0.',
);
process.exit(anyFailed ? 1 : 0);
