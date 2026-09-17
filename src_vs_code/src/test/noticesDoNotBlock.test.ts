import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';

/**
 * Two orderings that decide whether a person can carry on, pinned where they can be checked.
 *
 * <p>Both were found on the code round, both predate this branch, and neither could be tested by
 * running anything: `bugzReviewPanel.ts` and `dataCommands.ts` import `vscode`, so no test in this
 * repository can import them. What is left is reading the source — and `testing.md` is clear about
 * the price of that: a structural assertion has to pin the WHOLE condition, because one matching a
 * fragment survives its own break. So each test below asserts the order of two specific calls, not
 * the presence of a word, and each has a companion proving the scan still finds what it reads.</p>
 *
 * <p><b>What these do not prove.</b> Not that the panel redraws, not that a modal blocks a toast —
 * only that the code is still written the way the fix wrote it. The behaviour itself is only
 * observable in a real VS Code host, which this suite does not have. Written down here rather than
 * left for a reader to discover, because a structural test that is mistaken for a behavioural one
 * is worse than none.</p>
 */

const SOURCE = join(__dirname, '..', '..', 'src');

function read(name: string): string {
  return readFileSync(join(SOURCE, name), 'utf8');
}

/** One function's body, from its signature to the closing brace in the first column. */
function bodyOf(text: string, signature: string): string {
  const from = text.indexOf(signature);
  assert.notEqual(from, -1, `${signature} is no longer in this file — the test is reading nothing`);
  const to = text.indexOf('\n}', from);
  assert.notEqual(to, -1, 'no closing brace found, so the slice would run to the end of the file');

  return text.slice(from, to);
}

test('the review panel never waits for a person before it redraws', () => {
  // `await notifyAndAsk(...)` with no button on it holds the promise chain until somebody dismisses
  // an error toast, and a VS Code error toast does not dismiss itself — so `await this.draw()`
  // below it did not run, and the panel sat frozen with its controls disabled and no terminal
  // state. It arrived that way from `await vscode.window.showErrorMessage`, which has the same
  // defect. (gemini, the code round.)
  const text = read('bugzReviewPanel.ts');

  // The IMPORT, not a search of the file. The first version of this test scanned for the word and
  // went red on the comment that EXPLAINS the fix - the same trap that had the site counter
  // reporting a call site because a docstring named the API. A door that is not imported cannot be
  // called, so this pins the whole condition and no amount of prose about it can move the result.
  const imports = /import \{([^}]{1,200})\} from '\.\/notify';/u.exec(text);
  assert.ok(imports !== null, "bugzReviewPanel no longer imports from './notify' at all");
  const doors = (imports?.[1] ?? '').split(',').map((name) => name.trim()).filter((name) => name.length > 0);

  assert.deepEqual(
    doors,
    ['notify'],
    'this panel may use only the door that waits for the DISK: every notice here is raised on a '
    + 'path that redraws immediately afterwards, and a door that waits for a person freezes it',
  );
  // The companion. Without it this file could stop notifying altogether and the assertion above
  // would go on passing for ever.
  //
  // 3 → 4 on 2026-09-17, story 1.1 of PLAN_the_review_page_can_be_read: the panel gained the ±
  // zoom and ± tone controls, and a settings write that fails now says so. It is on the same
  // footing as the other three — raised and not waited on — which is why it belongs behind the
  // same import assertion rather than reaching for `notifyAndAsk`.
  assert.equal(
    (text.match(/\bawait notify\(/gu) ?? []).length,
    4,
    'the four places this panel speaks: the refusal, the partial write, the catch, and a view '
    + 'setting that could not be saved',
  );
});

test('the data-directory modal is answered before the reload is offered', () => {
  // A modal disables the whole workbench, so a *Reload Window* button already on screen cannot be
  // pressed until the modal is dismissed — a dialog holding a notification hostage. They are two
  // steps of one instruction and they have to happen in that order. (gemini, the code round; the
  // overlap predates the funnel.)
  const body = bodyOf(read('dataCommands.ts'), 'async function tellClientsToCatchUp(');

  const asked = body.indexOf('await notifyAndAsk({');
  const offered = body.indexOf('void offerAReload(directory);');

  assert.notEqual(asked, -1, 'the modal is gone from this function — the ordering has no meaning');
  assert.notEqual(offered, -1, 'the reload offer is gone from this function');
  assert.ok(
    asked < offered,
    'the modal must be AWAITED first; offering a reload underneath one puts a button behind a '
    + 'dialog that has to be dismissed before it can be reached',
  );
  assert.ok(
    body.includes('await notifyAndAsk('),
    'and it must be awaited rather than fired: `void` here would put the two on screen at once '
    + 'again, which is the defect with an extra step',
  );
});
