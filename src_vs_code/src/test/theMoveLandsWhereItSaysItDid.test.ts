import assert from 'node:assert/strict';
import { test } from 'node:test';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * The move copies into, verifies, and points at ONE directory.
 *
 * <p><b>The defect this exists for was introduced by a fix for another one</b>, which is the whole
 * argument for a second review round. Keeping the side through a move meant computing
 * `landing = <destination>/<side>` — and the destination check, the record and the sentence a person
 * reads all took it, while the COPY and the VERIFICATION still took the picked root. On a
 * partitioned installation the history therefore landed in the root, the settings pointed at an
 * empty side directory beside it, and deleting the source left the active installation with nothing.
 * Three reviewers found it independently, one of them rating it Blocking.</p>
 *
 * <p>Structural, because `dataCommands.ts` imports `vscode` and cannot be instantiated under this
 * runner — the sanctioned substitute for a command module, and not the refused one: what is asserted
 * is the wiring of a flow with no page to run. Each assertion pins the WHOLE call rather than the
 * function's name, because `copyInventory(` matched perfectly well while the argument was wrong.</p>
 */

const FLOW = readFileSync(join(__dirname, '..', '..', 'src', 'dataCommands.ts'), 'utf8');

test('the copy is made into the landing directory, not the folder that was picked', () => {
  assert.match(
    FLOW,
    /copyInventory\(vscode\.Uri\.file\(from\), landing, progress\)/u,
    'the copy takes something other than `landing` — on a partitioned installation that writes the '
    + 'history into the destination ROOT while the settings point at the side directory inside it',
  );
});

test('the verification reads the landing directory back', () => {
  assert.match(
    FLOW,
    /verificationFailure\(before, await countAt\(landing\.fsPath\)\)/u,
    'the verification counts a directory other than the one copied into, so it can pass over a move '
    + 'that put the data somewhere else entirely',
  );
});

test('and the placement check is made against the landing directory too', () => {
  // Otherwise moving from `C:\coai\sideA` to `C:\coai` with side `sideB` is refused as "the
  // destination contains the source", although `C:\coai\sideB` is disjoint from it.
  assert.match(
    FLOW,
    /destinationPlaceRefusal\(from, landing\.fsPath, isInside\)/u,
    'the placement check runs against the picked root, which refuses valid moves between two sides '
    + 'of one shared parent',
  );
});

test('the landing directory is created before anything is copied into it', () => {
  assert.match(
    FLOW,
    /createDirectory\(landing\)/u,
    'nothing creates `<destination>/<side>`, so the first copy into it has nowhere to go',
  );
});

test('every one of them names the same variable, so they cannot drift apart', () => {
  // The defect was three call sites agreeing and two not. This is the assertion that would have
  // caught it: one directory is computed, and the copy, the check, the verification, the record and
  // the sentence all take THAT one.
  const move = FLOW.slice(FLOW.indexOf('export async function moveDataDirectory'));
  const body = move.slice(0, move.indexOf('\n}\n'));

  assert.doesNotMatch(
    body,
    /copyInventory\([^)]*destination[,)]|countAt\(destination\.fsPath\)/u,
    'the move still reaches for the picked folder somewhere, which is how the copy and the settings '
    + 'came to disagree in the first place',
  );
});
