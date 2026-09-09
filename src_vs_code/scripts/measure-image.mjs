/**
 * Phase 0 of `todo/PLAN_a_picture_in_the_question.md`: can a vendor CLI be handed a picture at all,
 * in the mode this extension runs it, and how?
 *
 * <p>`ChatSession.send` takes `text: string` and nothing else. Whether each CLI accepts an image in a
 * non-interactive turn is not documented anywhere this repository can reach, so it is measured. Run
 * it as `npm run measure:image`, optionally `-- <vendor>`.</p>
 *
 * <h2>Why a run that exits 0 is NOT a yes</h2>
 *
 * <p>The sharpest finding on this probe's plan round, and the one the whole script is shaped by: a
 * CLI answering cheerfully does not prove the model RECEIVED the image. An unknown field in a JSON
 * turn can be ignored silently; an unsupported payload can be dropped on the floor; either way the
 * process exits 0 and a naive probe records a "yes" that commits the project to building a paste
 * feature, a CSP change and a temp-file discipline for a vendor that throws pictures away.</p>
 *
 * <p>So the image SAYS something. `tokenPng` draws a four-digit number, the prompt asks for that
 * number back and nothing else, and only the token appearing in the answer counts as receipt.
 * Everything else is a different outcome with a different name — see {@link OUTCOMES}.</p>
 *
 * <h2>Why a "no" here is not allowed to be vague</h2>
 *
 * <p>A probe that searches for a mechanism can fail to find one, and that is not the same fact as a
 * vendor refusing. If those two arrive in the table as one word, a feature gets killed by a bad
 * afternoon. Every attempt therefore records which of them happened, and the plan's decision line is
 * only allowed to say "cannot" where a vendor actually REFUSED or accepted-and-ignored.</p>
 */
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { environment, linesOf, runCli } from './cliHarness.mjs';
import { tokenPng } from './tokenPng.mjs';

const HERE = fileURLToPath(new URL('.', import.meta.url));
const OUT = new URL('../out/', import.meta.url).href;
const MEASUREMENTS = join(HERE, '..', 'measurements');

const { launchSpecFor } = await import(`${OUT}cliChatLaunch.js`);
const { resolvedExecutable } = await import(`${OUT}versionProbe.js`);
const { agyAdapter } = await import(`${OUT}agyAdapter.js`);
const { claudeAdapter } = await import(`${OUT}claudeAdapter.js`);
const { codexAdapter } = await import(`${OUT}codexAdapter.js`);

/**
 * What one attempt PROVED. The vocabulary matters more than the code that produces it.
 *
 * <p>`received` is the only "yes". `answered-without-token` is the dangerous one — the CLI took the
 * turn and said something, and the picture went nowhere; a probe without a readable token would have
 * called it a success. `refused` is a real no, in the vendor's own words. The last three are facts
 * about this machine and are NOT evidence about the vendor.</p>
 */
export const OUTCOMES = {
  received: 'the answer contained the number drawn in the image',
  'answered-without-token': 'the CLI answered, and the image was not read',
  refused: 'the CLI refused the turn on its merits, and said why',
  unavailable: 'the CLI could not run the turn for a reason about THIS ACCOUNT — not about images',
  'no-answer': 'the CLI produced no answer at all',
  unspawnable: 'node refused to spawn this file in this mode',
  failed: 'the run did not finish — hung, killed, or exited non-zero',
};

/** The token the image carries, pinned and printed. Digits only — `tokenPng` draws digits. */
const TOKEN = tokenFrom(process.argv) ?? '7431';

function tokenFrom(argv) {
  const at = argv.indexOf('--token');

  return at >= 0 && /^[0-9]{2,6}$/.test(argv[at + 1] ?? '') ? argv[at + 1] : undefined;
}

const ASK = `The image contains a number. Reply with ONLY that number and nothing else.`;

const TIMEOUT_MS = 180_000;
const MAX_BYTES = 32 * 1024 * 1024;

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
 * The two ways a picture could possibly travel, per vendor, built from each adapter's REAL turn.
 *
 * <p>A reviewer asked for both to be tried before Phase 1 designs an attachment around `{ path, mime }`
 * — testing only file paths against a CLI that takes inline base64, or the reverse, produces a false
 * negative and then the wrong interface. Which vectors are even structurally possible differs, and
 * that is a finding in itself:</p>
 *
 * <ul>
 *   <li><b>claude</b> — `message.content` is an ARRAY of blocks and this extension sends exactly one
 *       text block into it, so an `image` block beside it is the obvious candidate. Both vectors.</li>
 *   <li><b>agy</b> — `message.content` is a STRING. There is nowhere to put a block, so inline is not
 *       merely unsupported, it is unrepresentable in the turn this extension sends. Path only.</li>
 *   <li><b>codex</b> — the turn is the prompt text itself. Path only.</li>
 * </ul>
 */
function attempts(name, png, pngPath) {
  const base64 = png.toString('base64');
  const askAboutPath = `${ASK}\nThe image is the file at ${pngPath}`;

  if (name === 'claude') {
    return [
      {
        vector: 'inline base64 block',
        stdin: JSON.stringify({
          type: 'user',
          message: {
            role: 'user',
            content: [
              { type: 'image', source: { type: 'base64', media_type: 'image/png', data: base64 } },
              { type: 'text', text: ASK },
            ],
          },
        }),
      },
      { vector: 'a path in the prompt', stdin: claudeAdapter.encode(askAboutPath) },
    ];
  }
  if (name === 'agy') {
    return [
      {
        vector: 'inline base64 block',
        stdin: '',
        impossible: 'the turn’s `message.content` is a STRING — there is no block to put an image in',
      },
      { vector: 'a path in the prompt', stdin: agyAdapter.encode(askAboutPath) },
    ];
  }

  return [
    {
      vector: 'inline base64 block',
      stdin: '',
      impossible: 'the turn IS the prompt text — there is no structure to carry a block',
    },
    { vector: 'a path in the prompt', stdin: codexAdapter.encode(askAboutPath) },
  ];
}

/**
 * Whether a refusal is about THIS ACCOUNT rather than about pictures.
 *
 * <p>A usage limit, an expired sign-in or a rate limit is a fact about the afternoon, and recording
 * it as "this vendor cannot take an image" is exactly the silent feature-kill the plan round asked
 * to be held to. It happened on the first real run: `codex` answered
 * "You've hit your usage limit", which says nothing whatever about images.</p>
 */
/** The vendor's OWN sentence, dug out of the raw stream when the adapter has flattened it away. */
function reasonIn(raw) {
  for (const line of raw.split(/\r?\n/)) {
    try {
      const event = JSON.parse(line);
      const message = event?.message ?? event?.error?.message ?? '';
      if (typeof message === 'string' && isAboutTheAccount(message)) {
        return message;
      }
    } catch {
      // Not JSON, or not this line. The next one may be.
    }
  }

  return '';
}

function isAboutTheAccount(message) {
  return /usage limit|rate.?limit|quota|too many requests|sign in|signed in|unauthor|forbidden|credit/i
    .test(message);
}

/** What the CLI said, and what that proves. */
function outcomeOf(run, adapter) {
  if (run.ending === 'unspawnable') {
    return { outcome: 'unspawnable', answer: '', note: run.failure };
  }

  // Parsed BEFORE the exit code is judged. A CLI that exits non-zero and explains itself in its own
  // NDJSON has told us more than its status did, and the first version of this function threw that
  // away — it reported `codex` as a bare "failed, code 1" when the stream said, in words, that the
  // account was out of credit.
  let answer = '';
  let refusal = '';
  for (const line of linesOf(run.chunks)) {
    const seen = adapter.classify(line.text);
    if (seen.kind === 'answer') {
      answer = seen.text;
    }
    if (seen.kind === 'failure') {
      refusal = seen.failure;
    }
  }
  if (refusal.length > 0) {
    // The RAW output is searched as well as the adapter's sentence, because an adapter is allowed to
    // flatten a vendor's failure into one phrase for the person reading a chat — `codexAdapter` turns
    // every `turn.failed` into "the model did not finish the turn" — and that phrase is exactly the
    // information this probe needs and cannot get from it. The raw stream still carries the reason.
    const raw = run.chunks.map((one) => one.text).join('');
    const said = isAboutTheAccount(refusal) || isAboutTheAccount(raw);

    return {
      outcome: said ? 'unavailable' : 'refused',
      answer: '',
      note: (said ? reasonIn(raw) || refusal : refusal).replace(/\s+/g, ' ').slice(0, 300),
    };
  }
  if (run.ending !== 'closed' || run.code !== 0) {
    return { outcome: 'failed', answer: '', note: `${run.ending}, code ${run.code}. ${run.stderr.trim().slice(-300)}` };
  }
  if (answer.length === 0) {
    return { outcome: 'no-answer', answer: '', note: run.stderr.trim().slice(-300) };
  }

  return {
    outcome: answer.includes(TOKEN) ? 'received' : 'answered-without-token',
    answer: answer.trim().slice(0, 200),
    note: '',
  };
}

async function probe(name, png, raw) {
  const vendor = VENDORS[name];
  const resolved = await resolvedExecutable(vendor.executable);
  if (!resolved) {
    console.log(`${name.padEnd(8)} NOT INSTALLED`);

    return [];
  }
  const home = mkdtempSync(join(tmpdir(), 'coai-image-'));
  const pngPath = join(home, `token-${TOKEN}.png`);
  writeFileSync(pngPath, png);
  const spec = launchSpecFor(row(vendor.runtime), home, '', resolved);
  if (spec.refusal.length > 0) {
    console.log(`${name.padEnd(8)} REFUSED: ${spec.refusal}`);
    rmSync(home, { recursive: true, force: true });

    return [];
  }

  const results = [];
  try {
    for (const attempt of attempts(name, png, pngPath)) {
      if (attempt.impossible !== undefined) {
        console.log(`${name.padEnd(8)} ${attempt.vector.padEnd(22)} not representable — ${attempt.impossible}`);
        results.push({ vendor: name, ...attempt, outcome: 'not-representable', answer: '', note: attempt.impossible });
        continue;
      }
      process.stdout.write(`${name.padEnd(8)} ${attempt.vector.padEnd(22)} asking… `);
      const run = await runCli(spec, {
        useShell: spec.shell,
        stdin: attempt.stdin,
        timeoutMs: TIMEOUT_MS,
        maxBytes: MAX_BYTES,
      });
      const seen = outcomeOf(run, vendor.adapter);
      console.log(`${seen.outcome}${seen.answer.length > 0 ? ` — answered "${seen.answer}"` : ''}`);
      results.push({
        vendor: name,
        vector: attempt.vector,
        stdinBytes: attempt.stdin.length,
        ms: run.ms,
        ...seen,
      });
    }
  } finally {
    rmSync(home, { recursive: true, force: true });
  }
  raw.push(...results);

  return results;
}

const asked = process.argv.slice(2).find((arg) => !arg.startsWith('-'));
const names = asked === undefined || asked === 'all' ? Object.keys(VENDORS) : [asked];
if (names.some((name) => VENDORS[name] === undefined)) {
  console.error(`unknown vendor: ${asked}. Known: ${Object.keys(VENDORS).join(', ')}, all`);
  process.exit(2);
}

const png = tokenPng(TOKEN);
console.log(`token: ${TOKEN} · image ${png.length} bytes, base64 ${Buffer.from(png).toString('base64').length} chars`);
console.log(`prompt: ${ASK}`);
console.log(`environment: ${JSON.stringify(environment())}\n`);

const raw = [];
const all = [];
for (const name of names) {
  all.push(...(await probe(name, png, raw)));
}

writeFileSync(
  join(MEASUREMENTS, `image-${new Date().toISOString().replace(/[:.]/g, '-')}.json`),
  JSON.stringify({ token: TOKEN, ask: ASK, pngBytes: png.length, environment: environment(), attempts: raw }, null, 2),
);

console.log('\n=== the table ===\n');
console.log('| vendor | vector | outcome | what it said | bytes on stdin |');
console.log('|---|---|---|---|---|');
for (const one of all) {
  const said = one.outcome === 'received' || one.outcome === 'answered-without-token'
    ? `answered \`${one.answer}\``
    : (one.note ?? '').replace(/\s+/g, ' ').slice(0, 120);
  console.log(`| \`${one.vendor}\` | ${one.vector} | **${one.outcome}** | ${said} | ${one.stdinBytes ?? '—'} |`);
}

const anyReceived = all.some((one) => one.outcome === 'received');
const anyInconclusive = all.some((one) =>
  ['failed', 'unspawnable', 'unavailable', 'no-answer'].includes(one.outcome));
console.log(`\n${Object.entries(OUTCOMES).map(([key, meaning]) => `  ${key.padEnd(24)} ${meaning}`).join('\n')}`);
console.log(
  anyInconclusive
    ? '\nSOME ATTEMPTS WERE INCONCLUSIVE — those rows are facts about this machine, not about the vendor.'
    : '\nEvery attempt reached a verdict about the vendor rather than about the environment.',
);
process.exitCode = anyReceived || !anyInconclusive ? 0 : 1;
