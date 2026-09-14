/**
 * The live check for *New chat*: one real conversation per vendor that is RESET in the middle, and
 * a model that must not remember what was said before it.
 *
 * <p>`live-chat.mjs` beside this one proves the opposite thing — that a second turn still has the
 * first. This one proves that a reset takes it away, which is the whole promise of the gesture and
 * the one part of story D1 no test in `npm test` can reach: the guarantee is about a vendor process
 * and its memory, and no vendor is real in CI.</p>
 *
 * <h2>What it does, and why it is these five turns</h2>
 *
 * <ol>
 *   <li><b>Plant</b> a number in session A.</li>
 *   <li><b>Recall it, still in A.</b> The FIRST control: a model that never remembered would
 *       "forget" across a reset for a reason that has nothing to do with the reset.</li>
 *   <li><b>Reset</b>: dispose the session and remove its directory, which is what `ended()` does,
 *       then open a NEW session in a NEW directory with nothing carried, which is what `reopened()`
 *       does on the first question after a reset.</li>
 *   <li><b>Ask something it cannot fail to know.</b> The SECOND control, and the half the first
 *       draft was missing: a crash on startup, a timeout, a rate limit or a refusal all produce an
 *       answer with no planted number in it, and reading that as "it forgot" is a pass earned by the
 *       vendor being broken.</li>
 *   <li><b>Recall again.</b> The number must be gone.</li>
 * </ol>
 *
 * <p><b>Every answer is matched STRICTLY</b>, and that is not fussiness. `includes('4')` is true of
 * "retry in 4 minutes" and of a model called `claude-sonnet-4-6`; `includes('7431')` is true of any
 * prose that happens to quote it back. A refusal that satisfies the control and then omits the
 * number is precisely a false pass, so the liveness answer must BE four and the recall must carry
 * the number as a token of its own. (codex and gemini, the code round.)</p>
 *
 * <h2>What it proves, and what it does NOT</h2>
 *
 * <p>It proves the RECIPE: dispose the session, a new directory, an empty carry, and the vendor
 * forgets. <b>It does not prove that `freshStart` follows that recipe</b> — that function is behind
 * `vscode` and cannot be loaded outside an extension host, so a reset which failed to call `ended()`,
 * leaked a thread id or reused the directory would pass this script every time. `chatFreshWiring.test.ts`
 * pins that half by reading the source, and closing the gap properly is the open tail of
 * `research/PLAN_go_to_conversation.md`. Nor does it prove the old CLI's process tree is gone on
 * every platform: on WSL and Linux a grandchild can outlive its parent, which
 * `PLAN_closing_a_chat_ends_its_whole_tree.md` owns and the orphan ledger collects. An orphan holds
 * its own context and nothing speaks to it — a process leak, not a leak of context BETWEEN
 * conversations.</p>
 *
 * <h2>Running it</h2>
 *
 * <p>`npm run test:fresh` after `npm run compile`, or with one vendor's name. Every run spends five
 * real turns per vendor on a signed-in account, serially — never in parallel, because three CLIs at
 * once on shared accounts is the saturation the load campaign measured, and it would make the
 * measurement noisy as well as expensive.</p>
 *
 * <h2>Exit codes</h2>
 *
 * <ul>
 *   <li><b>0</b> — every vendor asked remembered before the reset and forgot after it. The only pass.</li>
 *   <li><b>1</b> — something still knew the number, or a control failed, or nothing was asked at all.
 *       A "no verdict" is NOT a pass: a run that could not measure is reported as a failure to
 *       measure, because a green tick for a check that tested nothing is worse than no check.</li>
 *   <li><b>2</b> — the command line was wrong: an unknown vendor, or a `--number` that is not one.</li>
 * </ul>
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
const { NEW_CONVERSATION } = await import(`${OUT}chatAdapter.js`);
const { resolvedExecutable } = await import(`${OUT}versionProbe.js`);

// The spread below is the whole defence against this script rotting again, and a spread of
// `undefined` is silently `{}` — which would put us back where we started, with a launch object
// missing every field but `resume`. So it is checked once, here, rather than discovered as a
// process that would not start. (local, the code round.)
if (NEW_CONVERSATION === undefined || typeof NEW_CONVERSATION !== 'object') {
  console.log('chatAdapter.js exports no NEW_CONVERSATION — run npm run compile, and check the export has not been renamed');
  process.exit(2);
}

const BUDGETS = { startupMs: 60_000, turnMs: 240_000 };

const row = (id, runtime) => ({
  id, runtime, model: '', enabled: true, plan: true, code: true,
  baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0,
});

const VENDORS = [
  ['agy', row('antigravity', 'antigravity'), agyAdapter],
  ['claude', row('claude', 'claude'), claudeAdapter],
  ['codex', row('codex', 'codex'), codexAdapter],
];

/**
 * The command line, parsed rather than positional.
 *
 * <p>`argv[2]` alone read `--number 1234 claude` as "no vendor named", ran all three, and spent
 * three vendors' worth of turns the person had not asked for. (gemini, the code round.)</p>
 */
function asked(argv) {
  const rest = argv.slice(2);
  const at = rest.indexOf('--number');
  const value = at >= 0 ? rest[at + 1] : undefined;
  if (at >= 0 && (value === undefined || !/^[0-9]{1,9}$/u.test(value))) {
    // A MALFORMED --number is a usage error, not a reason to quietly measure the default: the person
    // would be told their number passed when a different one was tested. (codex, three findings.)
    return { bad: `--number wants 1 to 9 digits; got ${value === undefined ? 'nothing' : JSON.stringify(value)}` };
  }
  // `at + 1` is only the value's index when there IS a --number: with none, `at` is -1 and this
  // dropped argument ZERO, so a vendor name on its own read as 'all' and spent three vendors' worth
  // of turns. Found by running it.
  const named = rest.filter((one, index) => !one.startsWith('--') && (at < 0 || index !== at + 1));

  return { number: value ?? '7431', which: named[0] ?? 'all' };
}

const cli = asked(process.argv);
if (cli.bad !== undefined) {
  console.log(cli.bad);
  process.exit(2);
}
const NUMBER = cli.number;

const PLANT = `Remember the number ${NUMBER}. Reply with the single word OK and nothing else.`;
const RECALL = 'What number did I ask you to remember? Reply with the number alone, or with the single word NONE if I never gave you one.';
/**
 * Something the model cannot fail to know, asked of the session AFTER the reset.
 *
 * <p>Its answer is not interesting; that there IS one is. Arithmetic rather than a greeting, because
 * a rate-limit notice or a refusal rendered as prose can contain the word hello.</p>
 */
const ALIVE = 'What is 2+2? Reply with the number alone.';

/** An answer with nothing round it — what a model told to reply with one token actually sent. */
const bare = (answer) => answer.trim().replace(/^[^0-9A-Za-z]+|[^0-9A-Za-z]+$/gu, '');

/**
 * Did the model ANSWER with this value — the whole reply, trimmed?
 *
 * <p>Used wherever a PASS needs proof, so it is exact and fails closed: a chatty reply is NO VERDICT
 * rather than a control satisfied. Substring matching is what makes a control hollow — `includes('4')`
 * is true of "retry in 4 minutes" — and so, it turns out, is a token match: a bare `4` stands as its
 * own token in that sentence too. Only the whole answer will do.</p>
 *
 * <p><b>And no regular expression is built from it.</b> The first version of this composed one from
 * the value, which comes from `--number` on the command line; the digits-only validation upstream
 * made it harmless in fact, but a regex assembled from an argument is the shape of the defect rather
 * than an instance of it, and it earned a high-severity `js/regex-injection` from CodeQL — the one
 * finding on this change that three human reviewers all missed. String equality needs no pattern.</p>
 */
const answeredExactly = (turn, value) => turn.ok && bare(turn.answer) === value;

/**
 * Does this reply carry the value ANYWHERE in it?
 *
 * <p>The mirror of the above, and used only where a pass needs the value to be ABSENT. It errs
 * toward finding it: a model that says "the number was 7431" has plainly not forgotten, and reading
 * that as forgetting would be the false pass this whole script exists to avoid. Each of the two
 * therefore fails toward "no pass", which is what makes them controls rather than decoration.</p>
 */
const mentions = (turn, value) => turn.ok && bare(turn.answer).includes(value);

/** A session in a directory of its own, built the way the extension builds one. */
function opened(vendor, adapter, resolved) {
  const home = mkdtempSync(join(tmpdir(), 'coai-fresh-'));
  const session = new CliChatSession(
    (resume) => {
      // Spread from the default, never written out by hand - see the note in live-chat.mjs. A field
      // added to ChatLaunch arrives with its own default rather than missing.
      const spec = launchSpecFor(vendor, home, { ...NEW_CONVERSATION, resume }, resolved);

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
    // moment, and a temp directory that will not delete is not what this script measures. Nothing
    // rests on it either — the next session mkdtemps a directory of its own.
    rmSync(open.home, { recursive: true, force: true });
  } catch (reason) {
    console.log(`  (the temp directory stayed: ${reason?.code ?? reason})`);
  }
}

const said = (turn) => (turn.ok ? turn.answer : `FAILED: ${turn.failure}`);

/** Each turn announced BEFORE it is waited on: five turns at up to four minutes each is a long silence. */
async function turn(what, session, text) {
  process.stdout.write(`  ${what}…`);
  const at = Date.now();
  const result = await session.send(text);
  process.stdout.write(` ${((Date.now() - at) / 1000).toFixed(1)}s\n`);

  return result;
}

async function resetInTheMiddle(name, vendor, adapter) {
  const resolved = await resolvedExecutable(
    vendor.executablePath || { antigravity: 'agy', claude: 'claude', codex: 'codex' }[vendor.runtime],
  );
  if (!resolved) {
    console.log(`${name.padEnd(12)} NOT INSTALLED`);

    return 'skipped';
  }
  console.log(`${name.padEnd(12)} shape=${adapter.shape} exe=${resolved}`);

  // ---- before the reset. Disposed in a `finally`, or a control that throws leaves a CLI running
  // and its directory on disk.
  let planted;
  let remembered;
  const before = opened(vendor, adapter, resolved);
  try {
    // READ BACK WHAT IS ACTUALLY SENT, once per vendor: the measurements rule asks for the composed
    // inputs of at least one cell per arm, and an argv nobody printed is an input nobody pinned.
    const spec = launchSpecFor(vendor, before.home, { ...NEW_CONVERSATION, resume: '' }, resolved);
    console.log(`  argv: ${JSON.stringify(spec.args)}`);
    planted = await turn('planting', before.session, PLANT);
    remembered = await turn('recalling (control 1)', before.session, RECALL);
  } finally {
    // ---- and this IS the reset: the session ends and its directory goes, the way `ended()` does.
    ended(before);
  }
  const kept = answeredExactly(remembered, NUMBER);
  if (!kept) {
    // THE FIRST CONTROL FAILED. Stop here rather than spending two more turns on a measurement that
    // is already unusable. (gemini, the code round.)
    console.log(`  before reset: ${said(remembered).slice(0, 60)}   (planted ${NUMBER})`);
    console.log('  NO VERDICT: it did not remember the number BEFORE the reset either, so this run cannot say what the reset did.');

    return 'no verdict';
  }

  // ---- after it: a new session in a new directory, carrying nothing, the way `reopened()` does
  let alive;
  let recalled;
  const after = opened(vendor, adapter, resolved);
  try {
    alive = await turn('checking it answers (control 2)', after.session, ALIVE);
    recalled = await turn('recalling after the reset', after.session, RECALL);
  } finally {
    ended(after);
  }
  const answering = answeredExactly(alive, '4');

  console.log(`  planted:      ${said(planted).slice(0, 60)}`);
  console.log(`  before reset: ${said(remembered).slice(0, 60)}   (planted ${NUMBER})`);
  console.log(`  still alive:  ${said(alive).slice(0, 60)}`);
  console.log(`  after reset:  ${said(recalled).slice(0, 60)}`);

  if (!answering) {
    // THE SECOND CONTROL FAILED, and this is the half the first draft was missing. A session that
    // crashed on startup, timed out, or was rate-limited says nothing about the number either — and
    // silence about the number is exactly what a pass looks like here.
    console.log('  NO VERDICT: the session after the reset could not answer a question it cannot fail to know, so its silence about the number means nothing.');

    return 'no verdict';
  }
  if (!recalled.ok) {
    // A TURN THAT FAILED IS NOT A CONVERSATION THAT REMEMBERED. Reported as "still knows it" in the
    // first draft, which named the vendor as leaking context when the truth was a broken turn.
    // (gemini, the code round.)
    console.log('  NO VERDICT: the recall after the reset did not complete, so nothing was measured.');

    return 'no verdict';
  }

  return mentions(recalled, NUMBER) ? 'still knows it' : 'forgot';
}

const all = VENDORS.filter(([name]) => cli.which === 'all' || cli.which === name);
// A typo used to filter the list to nothing and then report that everything kept its context, with
// exit code 0 — a check that passes by testing nothing is worse than no check. (codex.)
if (all.length === 0) {
  console.log(`no such vendor: ${cli.which}. Try one of: all, ${VENDORS.map(([name]) => name).join(', ')}`);
  process.exit(2);
}

console.log(`conditions: number=${NUMBER} vendors=${all.map(([name]) => name).join(',')} `
  + `node=${process.version} platform=${process.platform} budgets=${JSON.stringify(BUDGETS)}`);
// THE PREDICTION, written before the run rather than after it: a result with nothing to be measured
// against is an observation, and the measurements rule asks for both side by side.
console.log('predicted:  every vendor REMEMBERS before the reset and FORGETS after it — a disposed session '
  + 'takes its process with it, and a new one in a new directory with an empty carry has nothing to resume from. '
  + 'The per-turn shape is the one to watch: it resumes by an id rather than a pipe.');

const verdicts = [];
for (const [name, vendor, adapter] of all) {
  try {
    verdicts.push([name, await resetInTheMiddle(name, vendor, adapter)]);
  } catch (reason) {
    console.log(`${name.padEnd(12)} THREW: ${reason?.message ?? reason}`);
    verdicts.push([name, 'threw']);
  }
}

const measured = verdicts.filter(([, verdict]) => verdict !== 'skipped');
const forgot = measured.filter(([, verdict]) => verdict === 'forgot');
console.log(
  measured.length === 0
    ? 'NOTHING WAS ASKED: no vendor of those named is installed here'
    : `observed:   ${forgot.length} of ${measured.length} forgot — ` + verdicts.map(([name, verdict]) => `${name}=${verdict}`).join(' '),
);
// A run that asked NOTHING is not a pass, and neither is one whose control failed: both are
// "this says nothing", and only a vendor that remembered and then forgot is evidence.
process.exit(measured.length > 0 && forgot.length === measured.length ? 0 : 1);
