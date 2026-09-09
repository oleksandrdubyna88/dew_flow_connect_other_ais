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
 * <p><b>Run it as `node scripts/measure-stream.mjs` after `npm run compile`</b>, optionally with one
 * vendor's name. It spends real turns on real signed-in accounts, which is why it is a script and
 * not a test.</p>
 *
 * <h2>What the code round insisted on, and why each one matters</h2>
 *
 * <ul>
 *   <li><b>The launch spec is IMPORTED, never retyped.</b> `launchSpecFor` is the same function the
 *       extension uses, so this cannot measure a mode the product does not run. A hand-copied flag
 *       list would drift the first time an adapter changed one, and the table would then describe a
 *       CLI nobody launches. (the local reviewer, the plan round.)</li>
 *   <li><b>A failure is never counted as "no streaming".</b> A CLI that hangs, times out, is rate
 *       limited or has an expired token produces no partial text — and reading that as evidence
 *       would kill a feature that works. Every run must reach a terminal event AND exit 0, or it is
 *       reported as FAILED and excluded. (codex and the local reviewer, both Blocking.)</li>
 *   <li><b>The prompt forces a LONG answer.</b> "2+2?" comes back in one chunk from a CLI that
 *       streams perfectly well, which would be a false negative. It is also synthetic — no repository
 *       content, no paths — because raw vendor output is written to disk. (codex, gemini.)</li>
 *   <li><b>Chunks, not lines.</b> Buffering is the question, and a line-reader hides it: a stream
 *       held back and released in one 8 KB write looks identical to one that trickled, once you only
 *       count lines. Arrival is stamped per `data` event, before any line splitting. (gemini.)</li>
 *   <li><b>Direct spawn AND `cmd.exe`, same command.</b> Otherwise a late chunk cannot be attributed
 *       to a layer: the CLI, the pipe, or the shell the product puts in front of it on Windows.
 *       (codex.)</li>
 *   <li><b>Answer text is distinguished from reasoning, tool and status text.</b> Counting any
 *       text-bearing event as an answer partial would select an event for Phase 1 that shows a
 *       person the model's scratchpad. Each event is classified, and only text that is a PREFIX of
 *       the final answer counts as a partial. (codex.)</li>
 *   <li><b>Repeats with a warm-up</b>, because one cold run measures a cold start. (codex, gemini.)</li>
 * </ul>
 *
 * <p>Raw transcripts go to `measurements/` (git-ignored). Only the table belongs in the plan.</p>
 */
import { spawn } from 'node:child_process';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

const OUT = pathToFileURL(join(process.cwd(), 'out/')).href;
const { launchSpecFor } = await import(`${OUT}cliChatLaunch.js`);
const { resolvedExecutable } = await import(`${OUT}versionProbe.js`);
const { agyAdapter } = await import(`${OUT}agyAdapter.js`);
const { claudeAdapter } = await import(`${OUT}claudeAdapter.js`);
const { codexAdapter } = await import(`${OUT}codexAdapter.js`);

/**
 * The one prompt, pinned and printed.
 *
 * <p>Long enough that a streaming CLI cannot finish it in one chunk, mechanical enough that the
 * answer is checkable without judging prose, and synthetic so that nothing from this machine ends up
 * in a file. Forty lines was chosen because it is comfortably past any plausible chunk boundary.</p>
 */
const LINES = linesFrom(process.argv) ?? 40;
const PROMPT =
  `Write the numbers from 1 to ${LINES}, one per line, as plain text. `
  + `No commentary, no code block, no explanation — just the ${LINES} lines.`;

/**
 * How long an answer to ask for, because the answer's LENGTH is the second variable here.
 *
 * <p>A streaming CLI whose deltas all arrive in the last fraction of a second spares a person
 * nothing, and whether that fraction grows with the answer is the question the decision turns on:
 * if the window scales with length, a real chat answer streams usefully; if it is constant, the
 * silence was never buffering and streaming buys 4 % of a wait. One variable, pinned and printed.</p>
 */
function linesFrom(argv) {
  const at = argv.indexOf('--lines');

  return at >= 0 && /^[0-9]{1,4}$/.test(argv[at + 1] ?? '') ? Number(argv[at + 1]) : undefined;
}

/** How many measured runs per arm, after one warm-up that is recorded but not counted. */
const RUNS = 3;

/** A run that has said nothing terminal by now is a failure, not a silent CLI. */
const TIMEOUT_MS = 180_000;

const VENDORS = {
  claude: { runtime: 'claude', adapter: claudeAdapter, executable: 'claude' },
  codex: { runtime: 'codex', adapter: codexAdapter, executable: 'codex' },
  agy: { runtime: 'antigravity', adapter: agyAdapter, executable: 'agy' },
};

const row = (runtime) => ({
  id: runtime, runtime, model: '', enabled: true, plan: true, code: true,
  baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0,
});

/**
 * One run of one CLI, stamped.
 *
 * <p>Returns every stdout chunk with the millisecond it arrived, the exit code, and whether the
 * process had to be killed. Nothing here interprets the bytes — that is `classify`'s job, and
 * keeping them apart is what lets the raw transcript be re-read after the fact.</p>
 */
function runOnce(spec, useShell) {
  return new Promise((resolve) => {
    const started = Date.now();
    const chunks = [];
    let stderr = '';
    let killed = false;

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
      resolve({
        chunks: [], stderr: '', code: -1, killed: false,
        failure: String(reason), ms: Date.now() - started, unspawnable: true,
      });

      return;
    }

    const timer = setTimeout(() => {
      killed = true;
      child.kill();
    }, TIMEOUT_MS);

    child.stdout.on('data', (data) => {
      // Stamped HERE, on the raw chunk, before any line splitting. A stream held back and released
      // in one write is indistinguishable from a trickling one once you only count lines.
      chunks.push({ atMs: Date.now() - started, text: data.toString('utf8') });
    });
    child.stderr.on('data', (data) => {
      stderr += data.toString('utf8');
    });
    child.on('error', (reason) => {
      clearTimeout(timer);
      resolve({ chunks, stderr, code: -1, killed, failure: String(reason), ms: Date.now() - started });
    });
    child.on('close', (code) => {
      clearTimeout(timer);
      resolve({ chunks, stderr, code: code ?? -1, killed, failure: '', ms: Date.now() - started });
    });

    child.stdin.write(spec.encode(PROMPT));
    child.stdin.end();
  });
}

/** Every complete NDJSON line in arrival order, each carrying the chunk time it completed in. */
function linesOf(chunks) {
  const lines = [];
  let held = '';
  for (const chunk of chunks) {
    held += chunk.text;
    const parts = held.split(/\r?\n/);
    held = parts.pop() ?? '';
    for (const part of parts) {
      if (part.trim().length > 0) {
        lines.push({ atMs: chunk.atMs, text: part });
      }
    }
  }
  if (held.trim().length > 0) {
    lines.push({ atMs: chunks.at(-1)?.atMs ?? 0, text: held });
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

/** Every string buried in an event, so text can be looked for without knowing where a vendor put it. */
function stringsIn(value, depth = 0) {
  if (typeof value === 'string') {
    return [value];
  }
  if (depth > 6 || value === null || typeof value !== 'object') {
    return [];
  }

  return Object.values(value).flatMap((inner) => stringsIn(inner, depth + 1));
}

/** Whether an event mentions token usage at all, and under which key. */
function usageKeyOf(event) {
  const found = [];
  const walk = (value, path, depth) => {
    if (depth > 5 || value === null || typeof value !== 'object') {
      return;
    }
    for (const [key, inner] of Object.entries(value)) {
      const here = path.length > 0 ? `${path}.${key}` : key;
      if (/^(usage|total_cost_usd|tokens?_?(in|out|used)?|input_tokens|output_tokens)$/i.test(key)) {
        found.push(here);
      }
      walk(inner, here, depth + 1);
    }
  };
  walk(event, '', 0);

  return found;
}

/**
 * What this run proved.
 *
 * <p>The rule that matters: text counts as an ANSWER PARTIAL only when it is a PREFIX of the final
 * answer. A CLI that narrates its reasoning, echoes a tool argument or prints a status line is
 * emitting text that must never be shown as the answer, and the difference is invisible if you only
 * ask "did any event carry a string". (codex, the plan round.)</p>
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
      events.push({ atMs: line.atMs, kind: 'not-json', strings: 0, usage: [] });
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
      strings: stringsIn(event).filter((text) => text.trim().length > 0),
      usage: usageKeyOf(event),
    });
  }

  // A DELTA is a piece of the final answer that arrived before the answer did. Testing for a PREFIX
  // is not enough and undercounts badly: only the first delta is a prefix, and every one after it is
  // a fragment from the middle. The honest test is containment plus reconstruction — the pieces, in
  // arrival order, must join up into what the answer turned out to be. Anything that fails that is
  // reasoning, a tool argument or a status line, and must never be shown to a person as the answer.
  // (codex, the plan round.)
  const squash = (text) => text.replace(/\s+/g, '');
  const flat = squash(answer);
  const deltas = answer.length === 0
    ? []
    : events
      .filter((event) => event.adapterKind !== 'answer')
      .flatMap((event) =>
        event.strings
          .filter((text) => {
            const piece = squash(text);

            return piece.length > 1 && piece.length < flat.length && flat.includes(piece);
          })
          .map((text) => ({ atMs: event.atMs, kind: event.kind, text })),
      );
  const rebuilt = squash(deltas.map((delta) => delta.text).join(''));

  return {
    unspawnable: run.unspawnable === true,
    failure: run.failure,
    ok: !run.killed && run.code === 0 && terminalAtMs >= 0 && answer.length > 0,
    ms: run.ms,
    code: run.code,
    killed: run.killed,
    stderr: run.stderr.trim().slice(-300),
    answerChars: answer.length,
    terminalAtMs,
    firstDeltaAtMs: deltas.length > 0 ? deltas[0].atMs : -1,
    deltaCount: deltas.length,
    /** Whether the pieces actually rebuild the answer — the difference between streaming and noise. */
    deltasRebuildAnswer: deltas.length > 0 && rebuilt === flat,
    /** The whole answer arriving once, early, is NOT a stream — it is an early final. */
    wholeAnswerEarly: deltas.length === 0
      && events.some(
        (event) => event.adapterKind !== 'answer' && event.strings.some((text) => squash(text) === flat),
      ),
    /** How much of the wait a person would actually be spared. The number the decision turns on. */
    streamWindowMs: deltas.length > 0 && terminalAtMs >= 0 ? terminalAtMs - deltas[0].atMs : -1,
    deltaKinds: [...new Set(deltas.map((delta) => delta.kind))],
    chunkCount: run.chunks.length,
    firstChunkAtMs: run.chunks[0]?.atMs ?? -1,
    lastChunkAtMs: run.chunks.at(-1)?.atMs ?? -1,
    kinds: [...new Set(events.map((event) => event.kind))],
    usageKinds: [...new Set(events.filter((event) => event.usage.length > 0).map((event) => event.kind))],
    usageKeys: [...new Set(events.flatMap((event) => event.usage))],
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

  // `shell` is not a knob this measurement gets to choose freely: `launchSpecFor` decides it from the
  // resolved file, and for a `.cmd` shim there is no alternative — so the arm the product runs is
  // labelled as such, and the other one is attempted only to answer the buffering question.
  const arm = useShell ? 'cmd.exe' : 'direct';
  const shipped = spec.shell === useShell;
  const results = [];
  let unspawnable = false;
  try {
    for (let attempt = 0; attempt <= RUNS; attempt += 1) {
      const warm = attempt === 0;
      process.stdout.write(
        `${name.padEnd(8)} ${arm.padEnd(8)}${shipped ? '*' : ' '} ${warm ? 'warm-up' : `run ${attempt}`}… `,
      );
      const seen = classify(await runOnce(spec, useShell), vendor.adapter);
      console.log(
        seen.unspawnable
          ? 'IMPOSSIBLE — node refuses to spawn this file without a shell'
          : seen.ok
            ? `${seen.ms} ms · ${seen.deltaCount} deltas${seen.deltasRebuildAnswer ? ' (rebuild the answer)' : ''}`
              + `${seen.wholeAnswerEarly ? ' · whole answer early' : ''} · window ${seen.streamWindowMs} ms`
            : `FAILED (code ${seen.code}${seen.killed ? ', killed on timeout' : ''}) ${seen.stderr.slice(0, 120)}`,
      );
      raw.push({ vendor: name, arm, shipped, attempt, warm, ...seen });
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

const asked = process.argv[2];
const names = asked && asked !== 'all' ? [asked] : Object.keys(VENDORS);
if (names.some((name) => VENDORS[name] === undefined)) {
  console.error(`unknown vendor: ${asked}. Known: ${Object.keys(VENDORS).join(', ')}, all`);
  process.exit(2);
}

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

mkdirSync('measurements', { recursive: true });
const stamp = new Date().toISOString().replace(/[:.]/g, '-');
writeFileSync(join('measurements', `stream-${stamp}.json`), JSON.stringify(raw, null, 2));

console.log('\n=== the table ===\n');
console.log('| vendor | arm | ok | turn ms | deltas | rebuild the answer | stream window ms | usage event | usage at ms |');
console.log('|---|---|---|---|---|---|---|---|---|');
const median = (values) => {
  const sorted = [...values].sort((a, b) => a - b);

  return sorted.length === 0 ? -1 : sorted[Math.floor(sorted.length / 2)];
};
for (const arm of arms) {
  const good = arm.results.filter((seen) => seen.ok);
  const label = `${arm.arm}${arm.shipped ? ' (shipped)' : ''}`;
  if (arm.unspawnable) {
    console.log(`| ${arm.vendor} | ${label} | n/a | — | — | — | — | — | node refuses this file without a shell |`);
    continue;
  }
  const one = good[0];
  console.log(
    `| ${arm.vendor} | ${label} | ${good.length}/${arm.results.length} | ${median(good.map((s) => s.ms))} `
    + `| ${median(good.map((s) => s.deltaCount))} `
    + `| ${good.every((s) => s.deltasRebuildAnswer) ? 'yes' : one?.wholeAnswerEarly ? 'whole answer, once, early' : 'no'} `
    + `| ${median(good.map((s) => s.streamWindowMs))} `
    + `| ${(one?.usageKinds ?? []).join(' · ') || '—'} | ${median(good.map((s) => s.usageAtMs))} |`,
  );
}

console.log('\n=== what each vendor emitted, in order (first good run of the shipped arm) ===\n');
for (const arm of arms.filter((one) => one.shipped)) {
  const one = arm.results.find((seen) => seen.ok);
  if (one === undefined) {
    console.log(`${arm.vendor}: no good run`);
    continue;
  }
  console.log(`${arm.vendor} — ${one.events.length} events, answer ${one.answerChars} chars:`);
  for (const event of one.events) {
    const text = event.strings?.join(' ').trim().slice(0, 60).replace(/\s+/g, ' ') ?? '';
    console.log(
      `  ${String(event.atMs).padStart(7)} ms  ${(event.kind ?? '').padEnd(28)} `
      + `${(event.adapterKind ?? '').padEnd(8)} ${event.usage?.length > 0 ? 'USAGE ' : '      '} ${text}`,
    );
  }
  console.log('');
}

const anyFailed = arms.some((arm) => arm.results.some((seen) => !seen.ok && !seen.unspawnable));
console.log(`\nraw transcripts: measurements/stream-${stamp}.json`);
console.log(anyFailed ? 'SOME RUNS FAILED — a failed run is NOT evidence that a CLI does not stream.' : 'every run reached a terminal event and exited 0.');
process.exit(anyFailed ? 1 : 0);
