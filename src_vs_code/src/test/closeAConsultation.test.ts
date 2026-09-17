import assert from 'node:assert/strict';
import { test } from 'node:test';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import {
  CLOSE_CHOICES, NO_OUTCOME, OUTCOMES, outcomeBySaid, outcomeSaid, SERVER_TOO_OLD,
} from '../consultations';
import { consultationsHtml } from '../roundsLog';
import { roundsLogHtml } from '../roundsLog';
import { logCommandOf } from '../roundsLogMessages';
import { EMPTY_LOG } from '../roundsDb';
import type { DbConsultation, DbLog } from '../roundsDb';

/**
 * The `record…` control on a log row, from the markup to the message the host receives — RUN.
 *
 * <p><b>Why this file runs the page instead of reading it.</b> A page is assembled as a template
 * literal and handed to VS Code as text, so a substring assertion over that text cannot see a
 * control wired to nothing: the string contains everything it was supposed to contain.
 * `.agents/PROJECT.md` makes that an operator ruling, `forgetAChatRow.test.ts` is the file that
 * earned it — the chat ✕ had perfect markup and the page dropped half the message — and this
 * control shipped with markup assertions alone until the code round said the same thing again.</p>
 *
 * <p>The other half is the catalogue. These four words exist twice, once in each half of a product
 * whose halves ship separately, and a word in one and not the other is either a button whose only
 * outcome is a refusal or a refusal the panel cannot explain. Both halves therefore assert against
 * `shared/consultation-outcomes.json`, a file neither of them owns — the shape the C# suite's
 * `NothingReadsAnotherProgramsSourceTests` insists on, because a test that derives its expectation
 * from the other program's SOURCE goes quiet on a reformat rather than red.</p>
 */

interface SharedOutcome {
  readonly word: string;
  readonly verdict: boolean;
  readonly label: string;
  readonly detail: string;
  readonly said: string;
}

const CATALOGUE: readonly SharedOutcome[] = JSON.parse(
  readFileSync(join(__dirname, '..', '..', '..', 'shared', 'consultation-outcomes.json'), 'utf8'),
).outcomes;

const SOURCES: readonly { readonly word: string; readonly detail: string }[] = JSON.parse(
  readFileSync(join(__dirname, '..', '..', '..', 'shared', 'consultation-outcomes.json'), 'utf8'),
).sources;

function consultation(over: Partial<DbConsultation> = {}): DbConsultation {
  return {
    id: 'b8f1c2d3e4a5b6c7d8e9f0a1b2c3d4e5',
    callerKind: 'claude',
    repoPath: 'D:/repo',
    branch: 'feat/x',
    vendor: 'codex',
    model: 'gpt-5.6-luna',
    turns: 2,
    status: 'closed',
    reason: 'all 5 of its turns are used',
    startedUtc: '2026-09-17T09:00:00.000Z',
    endedUtc: '2026-09-17T09:30:00.000Z',
    seconds: 41,
    tokensIn: 40_000,
    tokensOut: 1_100,
    costUsd: 0.15,
    problem: 'the parser returns 3 where 4 is expected',
    advice: 'your loop stops one short',
    alert: '',
    ...over,
  };
}

function table(one: DbConsultation): string {
  const log: DbLog = { ...EMPTY_LOG, consultations: [one], read: true };

  return consultationsHtml(log, [], () => undefined);
}

// ---------- the shared catalogue ----------

test('the shared catalogue actually loaded', () => {
  // Without this, every assertion below would pass vacuously over an empty list.
  assert.ok(CATALOGUE.length > 3, `expected the shared outcomes, got ${CATALOGUE.length}`);
  assert.ok(SOURCES.length > 2, `expected the shared sources, got ${SOURCES.length}`);
});

test('every word the shared catalogue names has a sentence here, and no others do', () => {
  for (const one of CATALOGUE) {
    assert.equal(outcomeSaid(one.word), one.said, `${one.word} — ${one.detail}`);
  }

  // And back, which is the half a one-way check misses: a word this half learned and nobody wrote
  // down is a word the server will refuse, with no sentence able to explain why.
  assert.deepEqual(
    Object.keys(OUTCOMES).sort(),
    CATALOGUE.map((one) => one.word).sort(),
  );
});

test('the words a PERSON is offered are exactly the shared verdicts', () => {
  // Three, not four: `lapsed` is the server's own statement that the clock ran out, and offering it
  // would be asking somebody to state a fact that is not theirs. The server refuses it from this
  // door, so a fourth choice here would be a button whose only outcome is a refusal.
  assert.deepEqual(
    CLOSE_CHOICES.map((one) => one.outcome),
    CATALOGUE.filter((one) => one.verdict).map((one) => one.word),
  );

  for (const choice of CLOSE_CHOICES) {
    const shared = CATALOGUE.find((one) => one.word === choice.outcome);
    assert.ok(shared, choice.outcome);
    assert.equal(choice.label, shared.label, 'the label a person picks by');
    assert.equal(choice.detail, shared.detail, 'and the sentence they pick ON');
  }
});

test('every source the shared catalogue names is one this half can read back', () => {
  const said = SOURCES.map((one) => outcomeBySaid(one.word));

  assert.deepEqual(said, ['by the AI', 'by hand', ''],
    'the server`s own `lapsed` already says the clock did it; the other two are claims a person must tell apart');
});

test('an author this build has never heard of is shown as nothing, never guessed at', () => {
  // The same rule `outcomeSaid` follows for a fifth word. A newer server may write a fourth source.
  assert.equal(outcomeBySaid('some-future-door'), '');
  assert.equal(outcomeBySaid(undefined), '');
  assert.equal(outcomeSaid('some-future-word'), NO_OUTCOME);
});

// ---------- the author, in the row ----------

test('the row says who recorded the outcome, beside the word', () => {
  assert.match(table(consultation({ outcome: 'solved', outcomeBy: 'person' })), /solved<\/span>|solved/u);
  assert.match(table(consultation({ outcome: 'solved', outcomeBy: 'person' })), /by hand/u);
  assert.match(table(consultation({ outcome: 'not_solved', outcomeBy: 'caller' })), /by the AI/u);
});

test('the clock is not given an author, because the word already is one', () => {
  const row = table(consultation({ outcome: 'lapsed', outcomeBy: 'server' }));

  assert.match(row, /ran out/u);
  assert.doesNotMatch(row, /by the AI|by hand/u, '"ran out · by the server" is the same sentence twice');
});

test('a server too old to send an author still draws the verdict', () => {
  // Every row written before the column existed is in this state, and so is every row a server one
  // release behind still writes. A verdict with no author is a verdict.
  const row = table(consultation({ outcome: 'abandoned' }));

  assert.match(row, /abandoned/u);
  assert.doesNotMatch(row, /by the AI|by hand/u);
});

// ---------- the control, RUN ----------

/** What the page does when it is run: the listener it registered, and what it has posted so far. */
interface RunningPage {
  readonly click: (event: { target: unknown }) => void;
  readonly posted: readonly unknown[];
}

/**
 * The page's own click listener, captured out of its script exactly as the webview would run it.
 *
 * <p>The same shape `forgetAChatRow.test.ts` uses, deliberately: the context is five names, no
 * ambient environment, no filesystem and no network, and the script is synchronous.</p>
 */
function pageClickListener(): RunningPage {
  const html = roundsLogHtml([], [], 'n0nce');
  const script = html.slice(html.indexOf('<script'), html.lastIndexOf('</script>'));
  const body = script.slice(script.indexOf('>') + 1);

  let click: ((event: { target: unknown }) => void) | undefined;
  const element = (): Record<string, unknown> => ({
    innerHTML: '', textContent: '', hidden: false, value: '', className: '',
    addEventListener() {}, getAttribute: () => null, setAttribute() {}, querySelectorAll: () => [],
  });
  const document_ = {
    addEventListener(kind: string, fn: (event: { target: unknown }) => void) {
      if (kind === 'click') {
        click = fn;
      }
    },
    getElementById: () => element(),
    querySelectorAll: () => [],
    querySelector: () => element(),
    body: element(),
  };
  const posted: unknown[] = [];

  new Function('document', 'window', 'acquireVsCodeApi', body)(
    document_,
    { addEventListener() {}, matchMedia: () => ({ matches: false, addEventListener() {} }) },
    () => ({ postMessage: (message: unknown) => posted.push(message), getState: () => undefined, setState() {} }),
  );

  assert.ok(click, 'the page registers no click listener, so this test is driving nothing');

  return { click, posted };
}

test('pressing `record…` sends the consultation the person pointed at', () => {
  const { click, posted } = pageClickListener();
  const button = {
    getAttribute: (name: string) =>
      ({ 'data-command': 'closeConsultation', 'data-id': 'b8f1c2d3e4a5b6c7d8e9f0a1b2c3d4e5' })[name] ?? null,
  };

  const before = posted.length; // the page posts `ready` when it loads; this is about the CLICK.
  click({ target: { closest: (selector: string) => (selector === '[data-command]' ? button : null) } });

  assert.deepEqual(
    posted.slice(before),
    [{
      type: 'command',
      command: 'closeConsultation',
      id: 'b8f1c2d3e4a5b6c7d8e9f0a1b2c3d4e5',
      model: null,
    }],
    'the press did not reach the host as a close of that consultation',
  );
});

test('the message the control sends decodes to the close the host must perform', () => {
  assert.deepEqual(
    logCommandOf({ type: 'command', command: 'closeConsultation', id: 'b8f1c2d3', model: null }),
    { kind: 'closeConsultation', id: 'b8f1c2d3' },
  );
});

test('the control is offered exactly where nobody has decided yet', () => {
  // The two ways of saying nobody decided, and the three ways of saying somebody or nobody can.
  assert.match(table(consultation({ outcome: '' })), /data-command="closeConsultation"/u,
    'a consultation nobody spoke about has no way to be spoken about');
  assert.match(table(consultation({ outcome: 'lapsed', outcomeBy: 'server' })), /data-command="closeConsultation"/u,
    'the clock running out is not a verdict, and the issue`s own screenshot is in this state');
  assert.doesNotMatch(table(consultation({ outcome: 'solved', outcomeBy: 'caller' })), /closeConsultation/u,
    'a verdict is not rewritten, so a control promising to is a control that will be refused');
  assert.doesNotMatch(table(consultation({ outcome: '', status: 'failed' })), /closeConsultation/u,
    'a failed consultation produced no advice, so there is nothing to have a verdict about');
});

test('the id in the control is escaped rather than trusted', () => {
  // An id reaches the row out of a database this window did not necessarily write. One carrying a
  // quote would close `data-id` early and let the rest forge another attribute.
  const hostile = 'abc" data-command="deleteEverything';
  const row = table(consultation({ outcome: '', id: hostile }));

  assert.ok(!row.includes(`data-id="${hostile}"`), 'the id reached the attribute unescaped');
  assert.match(row, /data-id="abc&quot; data-command=&quot;deleteEverything"/u);
});

// ---------- the seam between the halves ----------

test('64 is the only code that means the server is too old', () => {
  // Reserved by `.agents/PROJECT.md` for exactly that, so a mode the binary HAS answers 65 however
  // wrong the request was. The panel's own branch depends on it: 64 sends a person to update a
  // server, and saying that about a malformed request would send them to fix something that works.
  assert.equal(SERVER_TOO_OLD, 64);
});

test('the close path tells a too-old server apart from a refusal', () => {
  // A source assertion, and the reason is the standing one: `panelProvider.ts` needs a VS Code host
  // this suite has none of, so the branch cannot be executed here. The idiom is
  // `forgetAChatRow.test.ts`'s last case.
  const host = readFileSync(join(__dirname, '..', '..', 'src', 'panelProvider.ts'), 'utf8');

  assert.match(host, /if \(code === SERVER_TOO_OLD\) \{/u,
    'the close path reads every non-zero code as a refusal, so an old server complains about arguments');
  assert.match(host, /too old to record how a consultation ended/u,
    'and the sentence does not name the cure, which is a version rather than a different click');
  assert.match(host, /vscode\.window\.withProgress\(/u,
    'the close runs with no progress, so a lock held by a vendor turn looks like a dead button');
});
