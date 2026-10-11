import assert from 'node:assert/strict';
import { test } from 'node:test';

import { MAKES_A_DIRECTORY, WRITERS, codeOf } from './dataDirWriters';

/**
 * Where the data lives is CHOSEN without making anything, and only the MOVE makes a folder — the folder a person just
 * picked (todo/PLAN_paths_per_side.md E5.0/E5.7; the gate's plan-round item A, verified 2026-10-10).
 *
 * <p><b>What the gate said, and what the code does.</b> The plan round took the choice flow (`askWhereDataLives`) to
 * create the folder a person chooses, and asked for that to be recorded as the intended exception to "nothing creates a
 * missing named root". Read and pinned here, it does not: the system dialog only returns a folder that exists, the flow
 * reads it (`whatIsIn`) and saves the setting (`saveChoice`), and `<root>/<side>` is then made lazily by the ordinary
 * writers through the one maker — a side folder inside an existing root. The one flow that DOES make a folder is the
 * move (`moveDataDirectory`): it makes `<destination>/<side>` inside the folder the person just picked and copies into
 * it — the pick is the consent, so it is the recorded exception. A person whose named folder is gone recovers by making
 * it again or choosing it (or another) again; the next probe lets every writer through.</p>
 *
 * <p>A scan, not a run: the flows open VS Code's dialogs and cannot run outside an editor. What it pins is WHERE the
 * folder-making calls of `dataCommands.ts` are — only in the move's two functions — so a choice that starts making
 * folders is red here.</p>
 */

/** The file's top-level functions, each with its own text up to the next one — enough to say which holds a call. */
function topLevelFunctions(file: string): ReadonlyMap<string, string> {
  const chunks = codeOf(file).split(/\n(?=(?:export )?(?:async )?function )/u);

  return new Map(chunks.flatMap((chunk) => {
    const name = /^(?:export )?(?:async )?function (\w+)/u.exec(chunk)?.[1];

    return name === undefined ? [] : [[name, chunk] as const];
  }));
}

function sites(text: string): number {
  return (text.match(MAKES_A_DIRECTORY) ?? []).length;
}

test('the choice flow makes no folder: askWhereDataLives, saveChoice and the reads of the chosen folder hold no directory-making call', () => {
  const functions = topLevelFunctions('dataCommands.ts');
  for (const name of ['askWhereDataLives', 'saveChoice', 'whatIsIn', 'inspect', 'askForSideName']) {
    assert.ok(functions.has(name), `dataCommands.ts no longer has ${name} — this pin reads the wrong file`);
    assert.equal(sites(functions.get(name) ?? ''), 0, `${name} makes a folder: choosing where the data lives must not create it`);
  }
});

test('the only folder-making calls of dataCommands.ts are the move\'s — the folder a person just picked, the recorded exception', () => {
  const functions = topLevelFunctions('dataCommands.ts');
  const holding = [...functions].filter(([, text]) => sites(text) > 0).map(([name]) => name).sort();

  assert.deepEqual(holding, ['copyInventory', 'moveDataDirectory'], 'a folder-making call appeared outside the move flow');
  assert.equal(WRITERS['dataCommands.ts']?.guard, 'the folder a person chose', 'the inventory no longer records the move as the person\'s own act');
});
