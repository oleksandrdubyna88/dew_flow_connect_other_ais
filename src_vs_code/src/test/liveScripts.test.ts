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
 * <p>So this test reads what the scripts CALL and checks it against the interface they call it with.
 * It cannot run a vendor and does not try; what it can do is fail on the commit that changes the
 * shape, which is the moment the fix is cheap.</p>
 */

const HERE = path.join(__dirname, '..', '..');
const SCRIPTS = ['live-chat.mjs', 'live-fresh.mjs'] as const;

const scriptText = (name: string): string => fs.readFileSync(path.join(HERE, 'scripts', name), 'utf8');

/** The field names of `ChatLaunch`, read from the interface rather than copied out of it. */
function chatLaunchFields(): readonly string[] {
  const source = fs.readFileSync(path.join(HERE, 'src', 'chatAdapter.ts'), 'utf8');
  const start = source.indexOf('export interface ChatLaunch {');
  assert.notEqual(start, -1, 'ChatLaunch has been renamed or moved, and this test can no longer find it');
  const body = source.slice(start, source.indexOf('\n}', start));

  return [...body.matchAll(/^\s{2}readonly (\w+):/gmu)].map((one) => one[1] ?? '').sort();
}

test('every live script hands launchSpecFor a ChatLaunch, with every field the interface has', () => {
  const fields = chatLaunchFields();
  assert.deepEqual(fields, ['model', 'resume'], 'ChatLaunch has changed shape — the scripts below must change with it');

  for (const name of SCRIPTS) {
    const text = scriptText(name);
    const call = /launchSpecFor\(vendor, home, (\{[^}]*\}), resolved\)/u.exec(text);
    assert.ok(
      call !== null,
      `${name} does not call launchSpecFor with an object in the launch position. A bare resume id `
      + 'was passed there for three days after the parameter became a ChatLaunch, and every run failed '
      + 'at process start with "Cannot read properties of undefined (reading \'length\')"',
    );
    for (const field of fields) {
      assert.match(
        call[1] ?? '',
        new RegExp(`\\b${field}\\b`, 'u'),
        `${name} passes a launch object with no ${field}: the parameter default only applies to undefined, `
        + 'so a partial object reaches the vendor argv builder and throws before any process starts',
      );
    }
  }
});

test('both live scripts are reachable as npm scripts, or nobody will ever run them', () => {
  // A script nobody can name is a script nobody runs, and this pair is the only thing in the
  // repository that touches a real vendor.
  const manifest = JSON.parse(fs.readFileSync(path.join(HERE, 'package.json'), 'utf8')) as {
    scripts: Record<string, string>;
  };
  const named = Object.values(manifest.scripts).join(' ');

  for (const name of SCRIPTS) {
    assert.ok(named.includes(name), `no npm script runs scripts/${name}`);
  }
});

test('a live script never reports a pass for a run that asked nothing', () => {
  // `live-chat.mjs` learned this from a typo that filtered its vendor list to nothing and then
  // reported that everything had kept its context, with exit code 0. `live-fresh.mjs` has the same
  // hazard twice over: a vendor that is not installed, and a control turn that failed — a model
  // which never remembered the number would "forget" it across a reset for a reason that has
  // nothing to do with the reset.
  const chat = scriptText('live-chat.mjs');
  assert.match(chat, /if \(all\.length === 0\)/u, 'live-chat.mjs no longer refuses an empty vendor list');

  const fresh = scriptText('live-fresh.mjs');
  assert.match(fresh, /if \(all\.length === 0\)/u, 'live-fresh.mjs does not refuse an empty vendor list');
  assert.match(fresh, /NO VERDICT/u, 'live-fresh.mjs reports a verdict even when its control turn failed');
  assert.match(
    fresh,
    /asked\.length > 0 && forgot\.length === asked\.length/u,
    'live-fresh.mjs can exit 0 for a run in which nothing was asked, or in which something still remembered',
  );
});
