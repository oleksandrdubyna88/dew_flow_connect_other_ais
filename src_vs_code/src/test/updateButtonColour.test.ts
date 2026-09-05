import { SNIPPET_VERSION } from '../claudeSnippet';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { panelHtml } from '../panelView';
import { versionProbeCandidates } from '../cliVersions';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS } from '../vendors';

/**
 * Green means "there is something to update" and nothing else means green.
 *
 * <p>Reported against 0.20.0, and both halves were real.</p>
 *
 * <p><b>The button looked green while its own tooltip said the CLI was current.</b> Not the state
 * logic \u2014 the hover. `.upd` inherits `.run`, and `.vendor .head .run:hover` paints a green border;
 * my `.upd:hover` said grey but sat EARLIER in the same stylesheet at equal specificity, so it lost.
 * Hovering is how you read the tooltip, which made the wrong colour the only colour anybody saw.</p>
 *
 * <p><b>And codex reported that its version could not be read</b>, on a machine where
 * `codex --version` prints `codex-cli 0.152.0`. On Windows an npm global is a `codex.cmd` shim, and
 * `spawn` without a shell does no PATHEXT resolution \u2014 a trap this project has already met once,
 * driving vendor CLIs from code.</p>
 */

function css(): string {
  return panelHtml({
    settings: DEFAULTS,
    vendors: DEFAULT_VENDORS,
    codexModels: [], agyModels: [],
    localEngines: {},
    server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
    side: '',
    latestServerVersion: '',
    questions: [],
    sessions: [],
    openSections: [],
    usage: [],
    usageWindow: 'week',
    cliStatus: {},
    modelPrices: {},
    snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
  }, 'nonce');
}

test('the update button cannot be painted green by hovering it', () => {
  const page = css();
  const runHover = page.indexOf('.vendor .head .run:hover');
  const updHover = page.indexOf('.vendor .head .upd:hover');

  assert.notEqual(runHover, -1);
  assert.notEqual(updHover, -1);
  assert.ok(
    updHover > runHover,
    'the grey hover sits before the green one at equal specificity, so the green wins and every '
      + 'up-to-date button turns green the moment somebody reads its tooltip',
  );
});

test('only the has-update rule mentions green', () => {
  const page = css();
  const updRules = [...page.matchAll(/\.vendor \.head \.upd[^{]*\{[^}]*\}/g)].map((m) => m[0]);

  assert.ok(updRules.length >= 3, `expected the upd rules, found ${updRules.length}`);
  for (const rule of updRules) {
    if (rule.includes('charts-green')) {
      assert.match(rule, /has-update/, `a green that is not conditional on has-update: ${rule}`);
    }
  }
});

test('on Windows a CLI is looked for under its PATHEXT names too', () => {
  // `codex` on Windows is `codex.cmd`. A bare `spawn('codex')` with no shell finds nothing, and the
  // panel reported "could not be read" for a CLI that answers perfectly from a terminal.
  assert.deepEqual(versionProbeCandidates('codex', 'win32'), ['codex.cmd', 'codex.exe', 'codex']);
  assert.deepEqual(versionProbeCandidates('codex', 'linux'), ['codex']);
});

test('a path that already names its extension is not guessed at', () => {
  assert.deepEqual(versionProbeCandidates('C:/n/codex.cmd', 'win32'), ['C:/n/codex.cmd']);
  assert.deepEqual(versionProbeCandidates('C:/tools/agy.exe', 'win32'), ['C:/tools/agy.exe']);
});

test('a full path with no extension still gets the Windows treatment', () => {
  // The CLI-path field takes whatever somebody pastes, and on Windows that is often the shim's
  // directory plus the bare name.
  assert.deepEqual(
    versionProbeCandidates('C:/Users/x/AppData/Roaming/npm/codex', 'win32'),
    ['C:/Users/x/AppData/Roaming/npm/codex.cmd', 'C:/Users/x/AppData/Roaming/npm/codex.exe',
     'C:/Users/x/AppData/Roaming/npm/codex'],
  );
});
