/**
 * The live contract check for the Claude model probe: does the installed CLI still answer in the
 * shape `claudeModels.ts` reads?
 *
 * <p>The ordinary suite cannot ask this. Each candidate is a BILLED request against whoever's
 * subscription is signed in here, so a test that ran it would fail on an account whose allowance is
 * spent and would spend one that is not — and `research/module_tests.md` says so as a limit. The
 * limit is real; the gap it leaves is that a change to `--output-format json` would leave the whole
 * suite green while every candidate quietly became unverified. This script is that gap's only
 * closer, and it is run by hand.</p>
 *
 * <h2>What it asserts, and why each one</h2>
 *
 * <ol>
 *   <li><b>The CLI answers at all</b>, with a version. Without this the rest is a test of nothing.</li>
 *   <li><b>A reply carries `modelUsage`, keyed by exactly one model.</b> This is the whole contract:
 *       the reply has no `model` field, so the KEY is the only place the answering model is named.
 *       Two keys or none and `modelThatAnswered` answers empty, which reads downstream as "not
 *       asked yet" — safe, and wrong.</li>
 *   <li><b>A known family answers as itself.</b> `sonnet` must come back as a `claude-sonnet-*`, or
 *       `answeredAsAsked` has stopped recognising the CLI's own naming.</li>
 *   <li><b>AN UNKNOWN NAME STILL EXITS 0.</b> The measurement the entire design rests on: an alias
 *       this CLI has never heard of is silently ignored and the DEFAULT model replies. If this ever
 *       becomes a non-zero exit, the probe could be far simpler — and if it stays true, a probe
 *       built on exit codes would report every candidate available. This assertion is the one that
 *       tells us which world we are in.</li>
 * </ol>
 *
 * <p><b>It is not run by `npm test`</b> and has no place in CI. Run it when the Claude CLI has been
 * updated, or when a dropdown starts disagreeing with reality:</p>
 *
 * <pre>cd src_vs_code &amp;&amp; npm run test:claude-models</pre>
 *
 * <p>It reports by exit code: 0 the contract holds, 1 it has changed (and says how), 2 the CLI could
 * not be reached at all — which is an environment answer, NOT a contract failure.</p>
 */
import { spawn } from 'node:child_process';

import { answeredAsAsked, familyOf, modelThatAnswered } from '../out/claudeModels.js';

const CAP_MS = 60_000;
const KNOWN = 'sonnet';
/** A name no CLI will ever resolve. If this one day fails, the fourth assertion has changed. */
const NONSENSE = 'no-such-family-4d9f';

function run(args) {
  return new Promise((resolve) => {
    const child = spawn(process.env['CLAUDE_CLI'] ?? 'claude', args, { shell: false });
    // The same EOF the probe now sends, and for the same measured reason: without it the CLI waits
    // three seconds for input on every call. Its warning goes to stderr, which is read separately
    // below - merging the two streams is what made the first run of this script fail to parse JSON.
    child.stdin.end();
    let output = '';
    let noise = '';
    let settled = false;
    const done = (code) => {
      if (!settled) {
        settled = true;
        resolve({ code, output, noise });
      }
    };
    const timer = setTimeout(() => { child.kill(); done(-1); }, CAP_MS);
    child.stdout.on('data', (chunk) => { output += String(chunk); });
    child.stderr.on('data', (chunk) => { noise += String(chunk); });
    child.on('error', () => { clearTimeout(timer); done(-1); });
    child.on('close', (code) => { clearTimeout(timer); done(code ?? -1); });
  });
}

const ask = (model) => run(['--model', model, '-p', 'hi', '--output-format', 'json']);

const broken = [];
const say = (ok, what, detail = '') => {
  console.log(`${ok ? 'ok  ' : 'FAIL'}  ${what}${detail === '' ? '' : ` — ${detail}`}`);
  if (!ok) {
    broken.push(what);
  }
};

const version = await run(['--version']);
if (version.code !== 0) {
  console.log(`The Claude CLI did not answer --version. Nothing here is a contract result.\n${version.output.trim()}`);
  process.exit(2);
}
console.log(`Claude CLI: ${version.output.trim()}\n`);

const known = await ask(KNOWN);
if (known.code !== 0) {
  console.log(`Asking for '${KNOWN}' failed (exit ${known.code}). An allowance that is spent answers like this.`);
  console.log(known.output.trim().slice(0, 400));
  process.exit(2);
}

let parsed;
try {
  parsed = JSON.parse(known.output);
} catch {
  say(false, 'the reply is JSON', known.output.trim().slice(0, 200));
  process.exit(1);
}

const keys = Object.keys(parsed?.modelUsage ?? {});
say(keys.length === 1, 'the reply carries modelUsage keyed by exactly one model', `keys: ${JSON.stringify(keys)}`);
say(parsed?.model === undefined, 'and still has no model field of its own, which is why the key is read');

const answered = modelThatAnswered(known.output);
say(answered.length > 0, 'modelThatAnswered reads that key', `read: '${answered}'`);
say(
  answeredAsAsked(KNOWN, answered),
  `a known family answers as itself`,
  `asked '${KNOWN}' (${familyOf(KNOWN)}), answered '${answered}' (${familyOf(answered)})`,
);

const nonsense = await ask(NONSENSE);
say(
  nonsense.code === 0,
  'AN UNKNOWN MODEL NAME STILL EXITS 0 — the measurement the whole design rests on',
  `exit ${nonsense.code}`,
);
if (nonsense.code === 0) {
  const other = modelThatAnswered(nonsense.output);
  say(
    !answeredAsAsked(NONSENSE, other),
    'and the default answered it, so an exit code proves nothing',
    `answered '${other}'`,
  );
}

console.log('');
if (broken.length === 0) {
  console.log('The contract holds. `claudeModels.ts` reads what this CLI writes.');
  process.exit(0);
}
console.log(`The contract has CHANGED: ${broken.length} assertion(s) failed.`);
console.log('Read `research/module_extension.md` (The Claude list is ASKED, not listed) before changing the parser.');
process.exit(1);
