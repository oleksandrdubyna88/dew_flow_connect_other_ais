/**
 * The live check for *New chat*: one real conversation per vendor that is RESET in the middle, and
 * a model that must not remember what was said before it.
 *
 * <p>`live-chat.mjs` beside this one proves the opposite thing — that a second turn still has the
 * first. This one proves that a reset takes it away, which is the whole promise of the gesture and
 * the one part of story D1 no test in `npm test` can reach: the guarantee is about a vendor process
 * and its memory, and no vendor is real in CI.</p>
 *
 * <h2>What it does, and why it is these four turns</h2>
 *
 * <ol>
 *   <li><b>Plant</b> a number in session A.</li>
 *   <li><b>Ask for it back, still in A.</b> THE CONTROL, and the reason this script is worth
 *       running: without it a "forgot" result also happens when the model never remembered — a
 *       refusal, a truncated turn, a vendor having a bad afternoon — and the check would pass by
 *       testing nothing. A run whose control fails reports NO VERDICT rather than a pass.</li>
 *   <li><b>Reset</b>: dispose the session and remove its directory, which is what `ended()` does,
 *       then build a NEW session in a NEW directory with nothing carried, which is what `reopened()`
 *       does on the first question after a reset. This script performs those two steps directly
 *       because `freshStart` itself lives behind `vscode` and cannot be loaded here; what it checks
 *       is therefore the CLAIM — a new session object, a new process, an empty carry — rather than
 *       the host code that arranges it, which `chatFreshWiring.test.ts` pins instead.</li>
 *   <li><b>Ask again.</b> The number must be gone.</li>
 * </ol>
 *
 * <p><b>What it does NOT prove</b>, and the plan says so in the same words: that the old CLI's
 * process tree is gone on every platform. On Windows the launcher ends the tree; on WSL and Linux a
 * grandchild can outlive its parent, which `PLAN_closing_a_chat_ends_its_whole_tree.md` owns and the
 * orphan ledger collects. An orphan holds its own context and nothing speaks to it: a process leak,
 * not a leak of context BETWEEN conversations. This script measures the second thing only.</p>
 *
 * <p>Run it as `npm run test:fresh` after `npm run compile`, or with one vendor's name to spend
 * turns on only that one. Every run spends four real turns per vendor on a signed-in account. Exit
 * code 0 means every vendor asked both REMEMBERED before the reset and FORGOT after it.</p>
 */
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

import { pathToFileURL } from 'node:url';
const OUT = pathToFileURL(join(process.cwd(), 'out/')).href;
const { launch } = await import(`${OUT}processLauncher.js`);
const { CliChatSession } = await import(`${OUT}cliChatSession.js`);
const { agyAdapter } = await import(`${OUT}agyAdapter.js`);
const { claudeAdapter } = await import(`${OUT}claudeAdapter.js`);
const { codexAdapter } = await import(`${OUT}codexAdapter.js`);
const { launchSpecFor } = await import(`${OUT}cliChatLaunch.js`);
const { resolvedExecutable } = await import(`${OUT}versionProbe.js`);

const BUDGETS = { startupMs: 60_000, turnMs: 240_000 };

const row = (id, runtime) => ({
  id, runtime, model: '', enabled: true, plan: true, code: true,
  baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0,
});

/** The one variable, pinned and printed — the same rule and the same default as `live-chat.mjs`. */
const NUMBER = numberFrom(process.argv) ?? '7431';

function numberFrom(argv) {
  const at = argv.indexOf('--number');

  return at >= 0 && /^[0-9]{1,9}$/.test(argv[at + 1] ?? '') ? argv[at + 1] : undefined;
}

const PLANT = `Remember the number ${NUMBER}. Reply with the single word OK and nothing else.`;
const RECALL = 'What number did I ask you to remember? Reply with the number alone, or with the single word NONE if I never gave you one.';

/** A session in a directory of its own, built the way the extension builds one. */
function opened(vendor, adapter, resolved) {
  const home = mkdtempSync(join(tmpdir(), 'coai-fresh-'));
  const session = new CliChatSession(
    (resume) => {
      // A ChatLaunch, not a bare resume id - see the same note in live-chat.mjs, and the guard in
      // liveScripts.test.ts that now fails when this shape drifts from the interface again.
      const spec = launchSpecFor(vendor, home, { resume, model: '' }, resolved);

      return launch(spec.executable, spec.args, { cwd: spec.cwd, shell: spec.shell });
    },
    BUDGETS,
    undefined,
    adapter,
  );

  return { session, home };
}

/** End one, the way a reset ends one: dispose first, and only then take its directory away. */
function ended(open) {
  open.session.dispose();
  try {
    // AFTER the disposal, and best-effort: a killed child can hold its working directory for a
    // moment, and a temp directory that will not delete is not what this script measures.
    rmSync(open.home, { recursive: true, force: true });
  } catch (reason) {
    console.log(`  (the temp directory stayed: ${reason?.code ?? reason})`);
  }
}

const said = (turn) => (turn.ok ? turn.answer : `FAILED: ${turn.failure}`);

async function resetInTheMiddle(name, vendor, adapter) {
  process.stdout.write(`${name.padEnd(12)} asking…\n`);
  const resolved = await resolvedExecutable(
    vendor.executablePath || { antigravity: 'agy', claude: 'claude', codex: 'codex' }[vendor.runtime],
  );
  if (!resolved) {
    console.log(`${name.padEnd(12)} NOT INSTALLED`);

    return 'skipped';
  }

  // ---- before the reset
  const before = opened(vendor, adapter, resolved);
  const planted = await before.session.send(PLANT);
  const remembered = await before.session.send(RECALL);
  const kept = remembered.ok && remembered.answer.includes(NUMBER);

  // ---- the reset: this session ends, and the next question opens a new one with nothing carried
  ended(before);
  const after = opened(vendor, adapter, resolved);
  const asked = await after.session.send(RECALL);
  ended(after);
  const forgot = asked.ok && !asked.answer.includes(NUMBER);

  console.log(
    `${name.padEnd(12)} shape=${adapter.shape.padEnd(10)} before=${kept ? 'REMEMBERED' : 'did not remember'} `
    + `after=${asked.ok ? (forgot ? 'FORGOT' : 'STILL KNOWS IT') : 'FAILED'}`,
  );
  console.log(`  planted:      ${said(planted).slice(0, 60)}`);
  console.log(`  before reset: ${said(remembered).slice(0, 60)}   (planted ${NUMBER})`);
  console.log(`  after reset:  ${said(asked).slice(0, 60)}`);

  if (!kept) {
    // THE CONTROL FAILED, so the run says nothing either way. A model that never remembered the
    // number would "forget" it across a reset for a reason that has nothing to do with the reset,
    // and reporting that as a pass is the check testing nothing. (`live-chat.mjs` learned the same
    // lesson from a typo that filtered its vendor list to nothing and then reported success.)
    console.log('  NO VERDICT: it did not remember the number BEFORE the reset either, so this run cannot say what the reset did.');

    return 'no verdict';
  }

  return forgot ? 'forgot' : 'still knows it';
}

const VENDORS = [
  ['agy', row('antigravity', 'antigravity'), agyAdapter],
  ['claude', row('claude', 'claude'), claudeAdapter],
  ['codex', row('codex', 'codex'), codexAdapter],
];

const which = process.argv[2] !== undefined && !process.argv[2].startsWith('--') ? process.argv[2] : 'all';
const all = VENDORS.filter(([name]) => which === 'all' || which === name);
if (all.length === 0) {
  console.log(`no such vendor: ${which}. Try one of: all, ${VENDORS.map(([name]) => name).join(', ')}`);
  process.exit(2);
}

console.log(`conditions: number=${NUMBER} vendors=${all.map(([name]) => name).join(',')} `
  + `node=${process.version} platform=${process.platform}`);

const verdicts = [];
for (const [name, vendor, adapter] of all) {
  try {
    verdicts.push([name, await resetInTheMiddle(name, vendor, adapter)]);
  } catch (reason) {
    console.log(`${name.padEnd(12)} THREW: ${reason?.message ?? reason}`);
    verdicts.push([name, 'threw']);
  }
}

const asked = verdicts.filter(([, verdict]) => verdict !== 'skipped');
const forgot = asked.filter(([, verdict]) => verdict === 'forgot');
console.log(
  asked.length === 0
    ? 'NOTHING WAS ASKED: no vendor of those named is installed here'
    : `${forgot.length} of ${asked.length} forgot: ` + verdicts.map(([name, verdict]) => `${name}=${verdict}`).join(' '),
);
// A run that asked NOTHING is not a pass, and neither is one whose control failed: both are
// "this says nothing", and only a vendor that remembered and then forgot is evidence.
process.exit(asked.length > 0 && forgot.length === asked.length ? 0 : 1);
