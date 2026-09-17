import assert from 'node:assert/strict';
import { test } from 'node:test';
import * as fs from 'node:fs';
import * as path from 'node:path';

/**
 * The live scripts are the only checks that touch a REAL vendor, and nothing compiles them.
 *
 * <p>`scripts/live-chat.mjs` and `scripts/live-fresh.mjs` drive this build's own adapters against a
 * signed-in CLI: one proves a conversation keeps its context across a turn, the other proves a reset
 * takes it away. They are scripts rather than tests because no vendor is real in CI and every run
 * spends money — and that is exactly why they rot silently. They are `.mjs`, they load the compiled
 * output through `await import()`, so every symbol they touch is `any`, and `tsc` never sees them.</p>
 *
 * <p><b>They had rotted.</b> `launchSpecFor`'s third parameter became a `ChatLaunch` — `{ resume,
 * model }` — when the model picker started deciding which model the CLI is told to use (72a6e80a,
 * 2026-09-11). Both scripts still passed the bare resume STRING they had passed since 2026-09-09.
 * A string is not `undefined`, so the parameter default never applied, and `modelRefusal` read
 * `.length` of `undefined` before any process started: every turn of every vendor failed with "the
 * model's process could not be started", spending nothing and proving nothing. Three days, and the
 * only thing that noticed was somebody running the script by hand.</p>
 *
 * <p><b>What this test does NOT do is enumerate the fields of `ChatLaunch`.</b> The first version
 * did, and two reviewers pointed out that it defeated the repair: the scripts spread
 * `NEW_CONVERSATION` precisely so a field added to that interface arrives with its own default and
 * NO script has to change — while a test pinning the field list would fail CI and demand exactly the
 * edit the spread removed. The spread is the guarantee; asserting the spread is the whole job.</p>
 */

const HERE = path.join(__dirname, '..', '..');
const SCRIPTS = ['live-chat.mjs', 'live-fresh.mjs'] as const;

const scriptText = (name: string): string => fs.readFileSync(path.join(HERE, 'scripts', name), 'utf8');

test('every live script builds its launch from the DEFAULT rather than by hand', () => {
  for (const name of SCRIPTS) {
    const text = scriptText(name);
    const call = /launchSpecFor\(vendor, (?:home|before\.home), (\{[^}]*\}), resolved\)/u.exec(text);
    assert.ok(
      call !== null,
      `${name} does not call launchSpecFor with an object in the launch position. A bare resume id `
      + 'was passed there for three days after the parameter became a ChatLaunch, and every run failed '
      + 'at process start with "Cannot read properties of undefined (reading \'length\')"',
    );
    // SPREAD, and that is what makes this class of rot impossible rather than merely detected: a
    // field added to `ChatLaunch` arrives with its own default instead of going missing, and nothing
    // — not this test, not the script — has to be edited in time. (codex and local, the plan round;
    // gemini and codex again on the code round, for pinning the field list on top of it.)
    assert.match(
      call[1] ?? '',
      /\.\.\.NEW_CONVERSATION/u,
      `${name} writes its launch object out by hand instead of spreading NEW_CONVERSATION, so the `
      + 'next field added to ChatLaunch goes missing there and throws before any process starts',
    );
    assert.match(text, /const \{ NEW_CONVERSATION \} = await import\(/u, `${name} spreads NEW_CONVERSATION without importing it`);
  }
  // And a spread of `undefined` is silently `{}`, which is the original bug wearing a new shape. The
  // script that does the spreading checks the import is really there. (local, the code round.)
  assert.match(
    scriptText('live-fresh.mjs'),
    /NEW_CONVERSATION === undefined \|\| typeof NEW_CONVERSATION !== 'object'/u,
    'nothing checks that NEW_CONVERSATION arrived, so a renamed export would spread to nothing',
  );
});

test('the two scripts measure the SAME vendors, so a fourth adapter cannot be half-added', () => {
  // Two hand-kept lists drift, and the drift is silent in the worst direction: `test:fresh` would
  // exit 0 having quietly not measured the new adapter at all. (codex, the code round.)
  // THE WHOLE DESCRIPTOR, not the label. Two scripts can both say `codex` while one of them builds a
  // different row or hands it a different adapter, and a parity check on names alone would call that
  // agreement — one script reporting a successful measurement for a configuration the other never
  // ran. (codex, the second code round.)
  const vendorsIn = (name: string): readonly string[] =>
    [...scriptText(name).matchAll(/^\s*\['\w+', row\([^)]*\), \w+Adapter\],$/gmu)]
      .map((one) => one[0].trim())
      .sort();

  const chat = vendorsIn('live-chat.mjs');
  assert.ok(chat.length >= 3, 'live-chat.mjs no longer declares its vendors in a shape this test can read');
  assert.deepEqual(
    vendorsIn('live-fresh.mjs'),
    chat,
    'the two live scripts do not check the same vendors in the same way: one of them would report '
    + 'success having never asked the adapter, or the row, the other one did',
  );
});

test('both live scripts are reachable as npm scripts, or nobody will ever run them', () => {
  const manifest = JSON.parse(fs.readFileSync(path.join(HERE, 'package.json'), 'utf8')) as {
    scripts: Record<string, string>;
  };
  const named = Object.values(manifest.scripts).join(' ');

  for (const name of SCRIPTS) {
    assert.ok(named.includes(name), `no npm script runs scripts/${name}`);
  }
});

test('a live script never reports a pass for a run that measured nothing', () => {
  const chat = scriptText('live-chat.mjs');
  assert.match(chat, /if \(all\.length === 0\)/u, 'live-chat.mjs no longer refuses an empty vendor list');

  const fresh = scriptText('live-fresh.mjs');
  assert.match(fresh, /if \(all\.length === 0\)/u, 'live-fresh.mjs does not refuse an empty vendor list');

  // A CONTROL ON BOTH SIDES. The first draft had one only before the reset, which is the same defect
  // it was written to prevent, mirrored: after the reset a crash, a timeout, a rate limit or a
  // refusal all produce an answer with no planted number in it, and reading that as "it forgot" is a
  // pass earned by the vendor being broken.
  assert.match(fresh, /const ALIVE = /u, 'nothing asks the session after the reset whether it is answering at all');
  assert.match(fresh, /const answering = answeredExactly\(alive, '4'\)/u, 'the liveness answer is not read');
  assert.match(fresh, /if \(!answering\) \{/u, 'a session that could not answer still yields a verdict about the number');
  // A turn that FAILED is not a conversation that remembered — it was reported as "still knows it",
  // naming the vendor as leaking context when the truth was a broken turn. (gemini, the code round.)
  assert.match(fresh, /if \(!recalled\.ok\) \{/u, 'a recall that never completed is read as the model still knowing');
  // The RETURN, not the phrase: the docblock says NO VERDICT too, and counting prose would make this
  // assertion pass on a comment.
  assert.equal((fresh.match(/return 'no verdict';/gu) ?? []).length, 3,
    'live-fresh.mjs has fewer than three no-verdict paths — the two controls and the failed recall');

  // Both sessions disposed whatever happens, or a control that throws leaks a process and a directory.
  assert.equal((fresh.match(/\} finally \{\s*\n\s*(?:\/\/[^\n]*\n\s*)*ended\(/gu) ?? []).length, 2,
    'a session is disposed on the happy path only');
  assert.match(fresh, /measured\.length > 0 && forgot\.length === measured\.length/u,
    'live-fresh.mjs can exit 0 for a run in which nothing was measured, or in which something still remembered');
});

test('an answer is matched as an ANSWER, not as a substring of prose', () => {
  // `includes('4')` is true of "retry in 4 minutes" and of a model called claude-sonnet-4-6;
  // `includes('7431')` is true of any refusal that quotes the question back. A control that a
  // rate-limit notice can satisfy is a control that awards a pass to a broken vendor. (codex and
  // gemini, the code round, four findings between them.)
  const fresh = scriptText('live-fresh.mjs');

  // TWO comparisons, each failing toward "no pass" — that asymmetry is what makes them controls.
  // Where a pass needs PROOF the value was answered, only the whole trimmed reply will do; where a
  // pass needs the value ABSENT, anything carrying it counts as present.
  assert.match(fresh, /const answeredExactly = \(turn, value\) => turn\.ok && bare\(turn\.answer\) === value;/u,
    'a pass can be earned by a reply that merely mentions the value — "retry in 4 minutes" satisfies '
    + 'both a substring test and a token test, which is how a rate-limited vendor passes a liveness control');
  assert.match(fresh, /const mentions = \(turn, value\) => turn\.ok && bare\(turn\.answer\)\.includes\(value\);/u,
    'the check for the value being GONE is exact, so a model that says "the number was 7431" reads as '
    + 'having forgotten it — the false pass this script exists to avoid');
  assert.match(fresh, /const kept = answeredExactly\(remembered, NUMBER\)/u, 'the first control is not the exact one');
  assert.match(fresh, /const answering = answeredExactly\(alive, '4'\)/u, 'the liveness control is not the exact one');
  assert.match(fresh, /return mentions\(recalled, NUMBER\) \? 'still knows it' : 'forgot'/u,
    'the verdict is not taken from the loose comparison, so a chatty answer reads as forgetting');
  // AND NO PATTERN IS BUILT FROM THE VALUE. It comes from `--number` on the command line; the
  // digits-only validation upstream makes it harmless in fact, but a regex assembled from an argument
  // is the shape of the defect, and CodeQL called it high-severity js/regex-injection — the one
  // finding on this change that three human reviewers all missed.
  assert.doesNotMatch(fresh, /new RegExp\(/u, 'a regular expression is built from a command-line argument again');
});

test('the command line is parsed, and a bad one costs nothing', () => {
  // `argv[2]` alone read `--number 1234 claude` as "no vendor named" and ran all three, spending
  // turns nobody asked for; and a malformed --number quietly measured the default, so the person
  // would be told their number passed when a different one was tested.
  const fresh = scriptText('live-fresh.mjs');

  assert.match(fresh, /function asked\(argv\)/u, 'the command line is read positionally rather than parsed');
  assert.match(fresh, /--number wants 1 to 9 digits/u, 'a malformed --number does not say so');
  assert.match(fresh, /\(at < 0 \|\| index !== at \+ 1\)/u,
    'with no --number present, argument zero is dropped as if it were the flag’s value — a vendor '
    + 'name on its own then reads as "all" and spends every vendor’s turns');
  assert.equal((fresh.match(/process\.exit\(2\)/gu) ?? []).length, 3,
    'the three usage errors — no NEW_CONVERSATION, a bad --number, an unknown vendor — do not all exit 2');
});

/**
 * The model probe's live check imports NAMED symbols, which is a second way to rot.
 *
 * <p>The two chat scripts pass an argument whose SHAPE can drift; this one names three functions in
 * an `import` from the compiled output. A rename reddens nothing — `.mjs` is never compiled, and the
 * script is only ever run by hand, so the first sign would be an ImportError weeks later, in the one
 * check that exists to tell us the CLI's contract still holds. (CodeRabbit, PR #335.)</p>
 */
test('the Claude probe live script imports symbols that still exist', () => {
  const text = scriptText('live-claude-models.mjs');
  const imported = /import \{([^}]*)\} from '\.\.\/out\/claudeModels\.js';/u.exec(text);
  assert.ok(imported, 'live-claude-models.mjs no longer imports from the compiled claudeModels');

  const named = (imported[1] ?? '').split(',').map((one) => one.trim()).filter((one) => one.length > 0);
  assert.ok(named.length >= 3, `it imports ${named.length} symbols, which is not the shape this guards`);

  // Against the DECLARATIONS, not against a list kept here: a list would be a third place to update.
  const source = fs.readFileSync(path.join(HERE, 'src', 'claudeModels.ts'), 'utf8');
  for (const one of named) {
    assert.match(
      source,
      new RegExp(`export (?:function|const) ${one}\\b`, 'u'),
      `live-claude-models.mjs imports '${one}', which claudeModels.ts no longer exports — the script `
      + 'would throw on import the next time somebody ran it to ask whether the CLI contract holds',
    );
  }
});

test('the Claude probe live script is reachable as an npm script', () => {
  const manifest = JSON.parse(fs.readFileSync(path.join(HERE, 'package.json'), 'utf8')) as {
    scripts: Record<string, string>;
  };
  const named = Object.values(manifest.scripts).some((one) => one.includes('live-claude-models.mjs'));

  assert.ok(named, 'nothing in package.json runs it, so nobody will');
});
