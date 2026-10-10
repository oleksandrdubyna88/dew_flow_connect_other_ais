/**
 * The released-half check for S3.3 of the feature-review plan (§4.13, the rows epic 3 introduces): the
 * vendor `feature` field and the snippet's feature half, each measured against the RELEASED artefact a
 * person may still be running on the other side.
 *
 * <p>No unit test can answer these. The suite holds one build of each half, both from this checkout, and
 * the question is about two DIFFERENT builds meeting on one data directory — the situation a person is
 * in for as long as they have updated one half and not the other. Four seams:</p>
 *
 * <ol>
 *   <li><b>This extension's tick → the released server.</b> Against a server it cannot version the panel
 *       writes `"feature": true` into `COAI_VENDORS` (unknown is not old). The released `coai-mcp` has
 *       no such field: it must still read the whole vendor list — through the settings file, through
 *       the client's env block and over MCP — rather than refuse it and fall back to its defaults.</li>
 *   <li><b>A rollback to the released extension.</b> It reads the vendor rows this build stored, keeps
 *       every row, and drops the tick it has never heard of — so what it writes carries none.</li>
 *   <li><b>This server over what the released extension wrote, then the released extension over what
 *       this server wrote.</b> No tick is no vendor: `review_feature` records a `skipped` round and does
 *       not block. The released extension then reads that session file, parses this server's `--log`,
 *       and its rounds-log page — bundled and RUN, as it ships — draws the round without throwing.</li>
 *   <li><b>The snippet.</b> This build reads a copy the released one handed out as behind on exactly
 *       the halves this build raised (the feature, consultant and question halves since artefact v15);
 *       the released build reads this one's copy as NEWER on the same halves, and says keep it.</li>
 * </ol>
 *
 * <h2>How to run it</h2>
 *
 * <pre>dotnet build dew_flow_connect_other_ais.slnx -c Debug &amp;&amp; cd src_vs_code &amp;&amp; npm run test:feature-vendor-compat</pre>
 *
 * <p>It downloads the newest published `mcp-v*` with `gh` and builds the newest published
 * `extension-v*` from its tag (`releasedHalves.mjs` says why built rather than downloaded); pass
 * `--server-tag=` / `--extension-tag=` for another release, or `COAI_OLD_SERVER=<path>`. NOT in CI: it
 * needs the network and released artefacts, and a check that silently skips in CI is a check that
 * reports success for having done nothing.</p>
 *
 * <p>Exit codes: <b>0</b> the boundary holds · <b>1</b> it does not, and says which assertion ·
 * <b>2</b> a released half or this checkout's build could not be obtained, which is an environment
 * answer rather than a contract failure.</p>
 */
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { flag, HERE, LOCAL_MS, newServer, releasedExtension, releasedServer, run } from './releasedHalves.mjs';
import { answerOf, sessionsFor } from './seam-session.mjs';

const broken = [];
const say = (ok, what, detail = '') => {
  console.log(`${ok ? 'ok  ' : 'FAIL'}  ${what}${detail === '' ? '' : ` — ${detail}`}`);
  if (!ok) {
    broken.push(what);
  }
};

/** The compiled modules of one extension tree — this checkout's, or a released one's. */
async function modulesOf(root) {
  const load = async (name) => await import(pathToFileURL(path.join(root, 'out', `${name}.js`)).href);

  return {
    vendors: await load('vendors'),
    settings: await load('settingsShape'),
    file: await load('serverSettingsFile'),
    snippet: await load('claudeSnippet'),
    rounds: await load('rounds'),
    roundsDb: await load('roundsDb'),
  };
}

/** The ids `--providers` or the `providers` tool answered with, in order. */
const idsOf = (answer) => (answer?.providers ?? []).map((one) => one.provider);

/** What a vendor list's providers answer says, minus the clock. */
const providersOf = (text) => JSON.stringify(JSON.parse(text || '{}').providers ?? null);

/** A throwaway repository with a base commit and a plan of three epics on top — the feature review's input. */
function fixtureRepo(into) {
  const dir = path.join(into, 'repo');
  mkdirSync(path.join(dir, 'todo'), { recursive: true });
  const git = (...args) => execFileSync('git', args, { cwd: dir, stdio: ['ignore', 'pipe', 'pipe'], encoding: 'utf8', timeout: LOCAL_MS });
  git('init', '--initial-branch=main');
  git('config', 'user.email', 'compat@example.invalid');
  git('config', 'user.name', 'compat');
  writeFileSync(path.join(dir, 'README.md'), '# fixture\n', 'utf8');
  git('add', '-A');
  git('commit', '-m', 'the base');
  const base = git('rev-parse', 'HEAD').trim();
  writeFileSync(path.join(dir, 'todo', 'PLAN_x.md'), '# PLAN — x\n\n## Epic 1\n## Epic 2\n## Epic 3\n', 'utf8');
  writeFileSync(path.join(dir, 'Cart.cs'), 'class Cart { int Count() => 3; }\n', 'utf8');
  git('add', '-A');
  git('commit', '-m', 'three epics');

  return { dir: git('rev-parse', '--show-toplevel').trim(), base };
}

const EPICS = JSON.stringify([1, 2, 3].map((n) => ({ title: `epic ${n}`, summary: `what epic ${n} built` })));
const LESSONS = JSON.stringify({
  pitfalls: ['none — a fixture for the released-half check, nothing was built by hand'],
  blockers: ['none — a fixture for the released-half check, nothing blocked it'],
  findings: ['the plan is three headings over one file, which is all this check needs'],
});

/**
 * The released rounds-log page, bundled from the released SOURCE the way its release job bundled it,
 * then RUN against a stub DOM — `bundledPage.test.ts`'s method, pointed at the older build.
 */
function releasedPage(extensionRoot, sessions, log) {
  const dir = mkdtempSync(path.join(tmpdir(), 'coai-old-page-'));
  const entry = path.join(dir, 'entry.mjs');
  const out = path.join(dir, 'bundle.cjs');
  writeFileSync(entry, `export { roundsLogHtml, rowsFrom } from ${JSON.stringify(path.join(extensionRoot, 'src', 'roundsLog.ts'))};\n`);
  const esbuild = createRequire(path.join(extensionRoot, 'package.json'))('esbuild');
  esbuild.buildSync({ entryPoints: [entry], outfile: out, bundle: true, format: 'cjs', platform: 'node', minify: true });
  const shim = { exports: {} };
  new Function('module', 'exports', readFileSync(out, 'utf8'))(shim, shim.exports);
  const html = shim.exports.roundsLogHtml(shim.exports.rowsFrom(sessions, Date.now(), () => undefined, [], log), [], 'n0nce');
  const script = html.slice(html.indexOf('<script'), html.lastIndexOf('</script>'));
  const body = script.slice(script.indexOf('>') + 1);
  const seen = {};
  const sent = [];
  const clicks = [];
  const stub = () => ({
    innerHTML: '', textContent: '', hidden: false, value: '', className: '', disabled: false, indeterminate: false,
    checked: false, addEventListener() {}, getAttribute: () => null, setAttribute() {}, querySelectorAll: () => [],
  });
  const document_ = {
    getElementById: (id) => (seen[id] ??= stub()),
    querySelectorAll: () => [],
    addEventListener(type, fn) {
      if (type === 'click') {
        clicks.push(fn);
      }
    },
  };
  new Function('document', 'window', 'acquireVsCodeApi', body)(
    document_, { addEventListener() {} }, () => ({ postMessage: (message) => sent.push(message), setState() {} }));
  const openRow = (key) => {
    const row = { getAttribute: (name) => (name === 'data-key' ? key : null), className: '' };
    for (const fn of clicks) {
      fn({ target: { closest: (asked) => (asked === 'tr[data-key]' ? row : null) } });
    }
  };

  return { seen, sent, openRow };
}

// ---------------------------------------------------------------------------------------------

const ours = path.join(HERE, 'src_vs_code');
if (!existsSync(path.join(ours, 'out', 'vendors.js'))) {
  console.log('this checkout\'s extension is not compiled — run `npm run compile` in src_vs_code first');
  process.exit(2);
}
const built = newServer();
if (built.length === 0) {
  console.log('this checkout has no built server — run `dotnet build dew_flow_connect_other_ais.slnx -c Debug` first');
  process.exit(2);
}
const work = mkdtempSync(path.join(tmpdir(), 'coai-feature-vendor-compat-'));
mkdirSync(path.join(work, 'server'));
mkdirSync(path.join(work, 'extension'));
const old = releasedServer(path.join(work, 'server'), flag('server-tag', ''));
const oldExtension = releasedExtension(path.join(work, 'extension'), flag('extension-tag', ''));
if (old.length === 0 || oldExtension === undefined) {
  console.log('a released half could not be obtained; nothing here is a contract result');
  process.exit(2);
}
console.log(`new: ${built}\n`);
const fresh = await modulesOf(ours);
const released = await modulesOf(oldExtension.root);
// The settings file is the only source the servers below may read: an inherited variable would win.
const noEnv = { COAI_VENDORS: '', COAI_PROVIDERS: '' };

// ---------- 1. THIS extension's tick → the RELEASED server ----------
const ticked = [...fresh.vendors.DEFAULT_VENDORS, { ...fresh.vendors.DEFAULT_VENDORS[0], id: 'codex-feature', feature: true }];
const stored = JSON.parse(JSON.stringify(fresh.vendors.vendorsFrom(ticked)));
say(stored.find((one) => one.id === 'codex-feature')?.feature === true, 'this build stores the feature tick on the row');
const newFile = fresh.file.serverSettingsJson(fresh.settings.DEFAULTS, stored, 'compat-new', '');
const wire = JSON.parse(JSON.parse(newFile).COAI_VENDORS ?? '[]');
say(wire.find((one) => one.id === 'codex-feature')?.feature === true,
  'against a server it cannot version, the tick crosses into COAI_VENDORS (unknown is not old)', JSON.stringify(wire.at(-1)));
const heldBack = JSON.parse(JSON.parse(fresh.file.serverSettingsJson(fresh.settings.DEFAULTS, stored, 'compat-new', '0.38.0')).COAI_VENDORS ?? '[]');
say(heldBack.length === 3 && heldBack.every((one) => !('feature' in one)),
  'against a known 0.38.0 the tick is held back and every row still crosses', `${heldBack.length} row(s)`);

const dataA = path.join(work, 'a');
mkdirSync(dataA);
writeFileSync(path.join(dataA, 'settings.json'), newFile);
const listed = await run(old, ['--providers'], dataA, noEnv);
say(listed.code === 0, 'the released server answers --providers over a settings file carrying the tick', `exit ${listed.code}`);
say(JSON.stringify(idsOf(JSON.parse(listed.out || '{}'))) === '["codex","antigravity","codex-feature"]',
  'and lists every vendor the file names — it did not refuse the list and fall back to its defaults',
  idsOf(JSON.parse(listed.out || '{}')).join(', '));

const dataA2 = path.join(work, 'a2');
mkdirSync(dataA2);
const untickedFile = fresh.file.serverSettingsJson(fresh.settings.DEFAULTS, stored.map(({ feature: _, ...rest }) => rest), 'compat-new', '');
say(untickedFile !== newFile, 'the control differs from the ticked file only by the tick');
writeFileSync(path.join(dataA2, 'settings.json'), untickedFile);
const control = await run(old, ['--providers'], dataA2, noEnv);
say(control.code === 0 && providersOf(control.out) === providersOf(listed.out),
  'the released server answers the same list with and without the tick — the field changes nothing there');

const dataA3 = path.join(work, 'a3');
mkdirSync(dataA3);
const block = fresh.settings.envBlock(fresh.settings.DEFAULTS, stored, '');
const fromEnv = await run(old, ['--providers'], dataA3, { COAI_VENDORS: block.COAI_VENDORS, COAI_PROVIDERS: '' });
say(fromEnv.code === 0 && JSON.stringify(idsOf(JSON.parse(fromEnv.out || '{}'))) === '["codex","antigravity","codex-feature"]',
  'the same list through the client\'s env block reads the same', idsOf(JSON.parse(fromEnv.out || '{}')).join(', '));

const served = sessionsFor({ command: old, args: [], dataDir: dataA, timeoutMs: LOCAL_MS })(noEnv);
await served.ready;
const toolAnswer = answerOf(await served.call('providers', {}));
const servedEnd = await served.close();
say(JSON.stringify(idsOf(toolAnswer)) === '["codex","antigravity","codex-feature"]',
  'and over MCP the released server\'s providers tool lists the same three', idsOf(toolAnswer).join(', '));
say(servedEnd.exited && servedEnd.code === 0, 'the released server ends on EOF', `exit ${servedEnd.code}`);

// ---------- 2. A rollback: the RELEASED extension reads what this build stored ----------
const rolledBack = released.vendors.vendorsFrom(stored);
say(JSON.stringify(rolledBack.map((one) => one.id)) === JSON.stringify(stored.map((one) => one.id)),
  `${oldExtension.tag} keeps every vendor row this build stored`, rolledBack.map((one) => one.id).join(', '));
say(rolledBack.every((one) => !('feature' in one)),
  'and drops the tick it has never heard of, so its next save forgets it — a release-note row, not a fix');
const oldFile = released.file.serverSettingsJson(released.settings.DEFAULTS, rolledBack, 'compat-old', '');
const oldWire = JSON.parse(oldFile).COAI_VENDORS;
say(oldWire === undefined || JSON.parse(oldWire).every((one) => !('feature' in one)), 'what it writes carries no tick',
  oldWire === undefined ? 'no COAI_VENDORS at all' : `${JSON.parse(oldWire).length} row(s)`);

// ---------- 3. THIS server over what the released extension wrote; the released extension reads the result ----------
const repo = fixtureRepo(work);
const dataB = path.join(work, 'b');
mkdirSync(dataB);
writeFileSync(path.join(dataB, 'settings.json'), oldFile);
const newSession = sessionsFor({ command: built, args: [], dataDir: dataB, timeoutMs: LOCAL_MS })(noEnv);
await newSession.ready;
const review = answerOf(await newSession.call('review_feature', {
  repoPath: repo.dir, planPath: 'todo/PLAN_x.md', baseRef: repo.base, epics: EPICS, lessons: LESSONS,
}));
const newEnd = await newSession.close();
say(review.verdict === 'skipped', 'this server records the feature review as skipped — no vendor carries a tick',
  `verdict: ${review.verdict ?? JSON.stringify(review).slice(0, 300)}`);
say(/no vendor is ticked/.test(JSON.stringify(review)), 'and says why', JSON.stringify(review).match(/no vendor is ticked[^"]*/)?.[0] ?? '');
say(newEnd.exited && newEnd.code === 0, 'this server ends on EOF', `exit ${newEnd.code}`);

const sessionDir = path.join(dataB, 'sessions');
const sessionTexts = existsSync(sessionDir)
  ? readdirSync(sessionDir).filter((one) => one.endsWith('.json')).map((one) => readFileSync(path.join(sessionDir, one), 'utf8'))
  : [];
const parsed = sessionTexts.map((text) => released.rounds.parseSession(text));
const featureRound = parsed.flatMap((one) => one?.rounds ?? []).find((round) => round.stage === 'FeatureReview');
say(sessionTexts.length > 0 && parsed.every((one) => one !== undefined),
  `${oldExtension.tag} parses every session file this server wrote`, `${sessionTexts.length} file(s)`);
say(featureRound?.verdict === 'skipped', 'including the feature session and its skipped round', `stage ${featureRound?.stage}, verdict ${featureRound?.verdict}`);
say(released.rounds.stageName('FeatureReview') === 'FeatureReview', 'it names the stage by its raw enum, having no phrase for it');

const newLog = await run(built, ['--log', '--paged'], dataB, noEnv);
const log = released.roundsDb.parseLog(newLog.out, true);
say(newLog.code === 0 && log.read === true && log.rounds.some((round) => round.stage === 'FeatureReview'),
  `${oldExtension.tag} parses this server's --log, the skipped feature round included`, `${log.rounds.length} round(s)`);

let page;
try {
  page = releasedPage(oldExtension.root, parsed.filter((one) => one !== undefined), log);
  say(true, `${oldExtension.tag}'s rounds-log page, bundled and run, renders without throwing`);
} catch (e) {
  say(false, `${oldExtension.tag}'s rounds-log page, bundled and run, renders without throwing`, String(e));
}
const rows = page?.seen['rows']?.innerHTML ?? '';
say((page?.seen['failed']?.textContent ?? '') === '', 'and reports no error to itself');
say(/<tr/.test(rows) && /FeatureReview/.test(rows), 'and draws the feature round, under the raw stage name');
const badge = /class="badge (\w+)"/.exec(rows)?.[1] ?? '(none)';
console.log(`      (${oldExtension.tag} draws it with the "${badge}" badge — it has no skipped state; this build's is "skipped — did not block")`);
const key = /data-key="([^"]+)"/.exec(rows)?.[1] ?? '';
if (page !== undefined && key.length > 0) {
  try {
    page.openRow(key);
    say(/class="detail"/.test(page.seen['rows']?.innerHTML ?? ''), 'opening the row works on the released page');
  } catch (e) {
    say(false, 'opening the row works on the released page', String(e));
  }
}

// ---------- 4. The snippet, both ways ----------
const oldSnippet = released.snippet.claudeSnippet();
const newSnippet = fresh.snippet.claudeSnippet();
const behind = fresh.snippet.snippetStatus(oldSnippet);
// The halves this build moved past the released one: since artefact v15 the feature (v3), the
// consultant (v4) and the new question half. Edit with every artefact change, as before.
const raised = JSON.stringify(['coai-consultant', 'coai-feature', 'coai-question']);
const sorted = (ids) => JSON.stringify([...ids].sort());
say(behind.kind === 'older' && sorted(behind.behind) === raised,
  `this build reads ${oldExtension.tag}'s snippet as behind on exactly the halves it raised`, JSON.stringify(behind));
say(fresh.snippet.snippetNote(behind).includes('the feature gate') && fresh.snippet.snippetNote(behind).includes(`v${fresh.snippet.ARTEFACT_VERSION}`),
  'and tells the person which half, and which version to copy', fresh.snippet.snippetNote(behind));
const inRepo = await fresh.snippet.readSnippetStatus(async (name) => (name === 'CLAUDE.md' ? `# repo\n\n${oldSnippet}` : ''));
say(JSON.stringify(inRepo) === JSON.stringify(behind), 'the same when it is read out of a CLAUDE.md');
const ahead = released.snippet.snippetStatus(newSnippet);
say(ahead.kind === 'ahead' && sorted(ahead.newer) === raised,
  `${oldExtension.tag} reads this build's snippet as NEWER on the same halves`, JSON.stringify(ahead));
say(/Keep what you have/.test(released.snippet.copiedMessage(ahead)), 'and tells the person to keep it rather than paste over it',
  released.snippet.copiedMessage(ahead));

console.log('');
if (broken.length === 0) {
  console.log('The boundary holds, both ways.');
  process.exit(0);
}
console.log(`The boundary is BROKEN: ${broken.length} assertion(s) failed.`);
console.log('Read §4.13 of todo/PLAN_feature_review.md before changing either half.');
process.exit(1);
