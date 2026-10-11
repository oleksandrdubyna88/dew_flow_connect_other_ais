/**
 * The released-half check for E3.5 of todo/PLAN_paths_per_side.md: a catalog row's CLI path PER SIDE
 * (`coai.vendors[].executablePathBySide`) measured against the RELEASED extension and the RELEASED coai-mcp a person may
 * still be running on the other side.
 *
 * <p>VS Code shares `coai.vendors` between a Windows window and a WSL window, and a WSL remote installs its OWN copy of
 * the extension, which can lag a version — so one row is read and written by two extension versions at once, and each
 * side's server reads the file its own extension wrote. No unit test can answer that: the suite holds one build of each
 * half (`pathsPerSideCompat.test.ts` asks the same questions of this build alone). Three seams:</p>
 *
 * <ol>
 *   <li><b>(a) A row an older extension wrote</b> — the legacy `executablePath` only — is read by this build exactly as
 *       the released one reads it, gains no per-side member (so a save writes it back byte for byte), and goes into
 *       `COAI_VENDORS` identically.</li>
 *   <li><b>(c) The server settings file this build writes</b> for a row WITH per-side values carries ONE string per row —
 *       this side's value, no per-side member — and the released server reads it and runs that value (it is the fake
 *       CLI; the server's own fallback for the row points at nothing, so only the row's path can answer).</li>
 *   <li><b>(b) Mixed versions</b> (the cadence consultation for epics 4-6, finding b). A row a newer extension on the
 *       OTHER side saved last holds the other side's path as its legacy value: the released extension on THIS side
 *       never runs this side's per-side value (the named limitation — it sends the legacy value, or nothing once it
 *       knows the other-side rule), and a server ≥ 0.46.0 skips the other side's path it is sent. Then the released
 *       extension's next save, through its own vendor write (`panelProvider.ts`, `vendorsFrom(...).map(...)`), is read
 *       back: a newer reader on this side resolves it; the per-side member is measured, and with it what a newer reader
 *       on the other side is left with.</li>
 * </ol>
 *
 * <h2>How to run it</h2>
 *
 * <pre>dotnet build src_mcp/src/CoaiMcp.csproj &amp;&amp; dotnet build src_mcp/tests_fakecli/FakeCli.csproj &amp;&amp; cd src_vs_code &amp;&amp; npm run test:paths-per-side-compat</pre>
 *
 * <p>It downloads the newest published `mcp-v*` with `gh` and builds the newest published `extension-v*` from its tag
 * (`releasedHalves.mjs`); pass `--server-tag=` / `--extension-tag=` for another release (`extension-v0.65.0` is the last
 * one that sends the legacy value unchanged, `mcp-v0.45.1` the last server that probes the other side's path), or
 * `COAI_OLD_SERVER=<path>`. NOT in CI: it needs the network and released artefacts.</p>
 *
 * <p>Exit codes: <b>0</b> the boundary holds · <b>1</b> it does not, and says which assertion · <b>2</b> a released half,
 * this checkout's build or the fake CLI could not be obtained — an environment answer, not a contract one.</p>
 */
import { existsSync, mkdirSync, mkdtempSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { flag, HERE, newServer, releasedExtension, releasedServer, run } from './releasedHalves.mjs';

let broken = [];
const say = (ok, what, detail = '') => {
  console.log(`${ok ? 'ok  ' : 'FAIL'}  ${what}${detail === '' ? '' : ` — ${detail}`}`);
  if (!ok) {
    broken = [...broken, what];
  }
};
const note = (what) => console.log(`info  ${what}`);

/**
 * The compiled modules of one extension tree — this checkout's, or a released one's. `pathFamily` arrived with the
 * other-side rule (extension-v0.67.0); an older tree has none, and reads as an empty module.
 */
async function modulesOf(root) {
  const file = (name) => path.join(root, 'out', `${name}.js`);
  const load = async (name) => await import(pathToFileURL(file(name)).href);

  return {
    vendors: await load('vendors'),
    wire: await load('vendorsWire'),
    family: existsSync(file('pathFamily')) ? await load('pathFamily') : {},
    settings: await load('settingsShape'),
    file: await load('serverSettingsFile'),
  };
}

/** The stand-in vendor CLI the server's own tests use (`run-seam.mjs`), or an empty string. */
function fakeCli() {
  const exe = process.platform === 'win32' ? 'FakeCli.exe' : 'FakeCli';
  const found = ['Release', 'Debug'].map((configuration) => path.join(HERE, 'src_mcp', 'tests_fakecli', 'bin', configuration, 'net10.0', exe));

  return found.find((one) => existsSync(one)) ?? '';
}

/** `a >= b` for two `x.y.z` versions; an unreadable one is not newer. */
function atLeast(version, floor) {
  const parts = (text) => (/(\d+)\.(\d+)\.(\d+)/u.exec(text) ?? [0, -1, 0, 0]).slice(1).map(Number);
  const [a, b] = [parts(version), parts(floor)];
  const first = a.findIndex((n, i) => n !== b[i]);

  return first === -1 || a[first] > b[first];
}

/** A data directory holding one settings file, the only source the servers below may read. */
function dataDir(work, name, settingsJson) {
  const dir = path.join(work, name);
  mkdirSync(dir);
  writeFileSync(path.join(dir, 'settings.json'), settingsJson);

  return dir;
}

// ---------------------------------------------------------------------------------------------

const ours = path.join(HERE, 'src_vs_code');
if (!existsSync(path.join(ours, 'out', 'cliPathSave.js'))) {
  console.log('this checkout\'s extension is not compiled — run `npm run compile` in src_vs_code first');
  process.exit(2);
}
const built = newServer();
const cli = fakeCli();
if (built.length === 0 || cli.length === 0) {
  console.log('this checkout has no built server or fake CLI — run `dotnet build src_mcp/src/CoaiMcp.csproj` and `dotnet build src_mcp/tests_fakecli/FakeCli.csproj` first');
  process.exit(2);
}
const work = mkdtempSync(path.join(tmpdir(), 'coai-paths-per-side-compat-'));
mkdirSync(path.join(work, 'server'));
mkdirSync(path.join(work, 'extension'));
const old = releasedServer(path.join(work, 'server'), flag('server-tag', ''));
const oldExtension = releasedExtension(path.join(work, 'extension'), flag('extension-tag', ''));
if (old.length === 0 || oldExtension === undefined) {
  console.log('a released half could not be obtained; nothing here is a contract result');
  process.exit(2);
}
console.log(`new: ${built}\nold extension: ${oldExtension.tag}\n`);
const fresh = await modulesOf(ours);
const released = await modulesOf(oldExtension.root);
// The host's disk, as each build's activation installs it — only a build that knows the other-side rule has the probe.
for (const tree of [fresh, released]) {
  if (typeof tree.family.useFileProbe === 'function') {
    tree.family.useFileProbe(tree.family.fileAt(statSync));
  }
}
const oldVersion = (await run(old, ['--version'], work)).out.trim().split(/\s+/u).pop() ?? '';
const skipsOtherSide = atLeast(oldVersion, '0.46.0');

const H = fresh.family.familyOf(process.platform);
const O = H === 'windows' ? 'posix' : 'windows';
const ID = 'compat-cli';
const FAKE_VERSION = 'fake-cli 9.9.9';
const thisSides = H === 'windows' ? 'C:\\coai-compat-nobody\\codex.cmd' : '/opt/coai-compat-nobody/codex';
const otherSides = H === 'windows' ? '/usr/local/bin/coai-compat-nobody/codex' : 'C:\\coai-compat-nobody\\codex.cmd';
const nowhere = path.join(work, 'no-fallback', 'codex');
const BASE = { runtime: 'codex', model: '', enabled: true, plan: true, code: true };
// The settings file is the only source the servers below may read: an inherited variable would win.
const noEnv = { COAI_VENDORS: '', COAI_PROVIDERS: '', FAKECLI_MODE: 'vendor', FAKECLI_STDOUT: FAKE_VERSION };
const rowIn = (text) => (JSON.parse(text || '{}').providers ?? []).find((one) => one.provider === ID) ?? {};
const ranTheFake = (row) => row.cliFound === true && row.version === FAKE_VERSION;
/** What a newer reader of `family` runs for a stored row — its disk saying "absent" to every other-OS spelling. */
const newerReader = (rows, family) =>
  fresh.family.pathForThisSide(fresh.vendors.vendorsFrom(rows)[0], { family, systemDrive: 'C:', fileAt: () => 'absent' }).path;

// ---------- (a) A row an OLDER extension wrote: the legacy value only ----------
const LEGACY = ['', thisSides, otherSides, '//server/share/codex', 'codex-beta', '/Program Files/coai-compat-nobody/codex.exe', `  ${thisSides}  `];
const legacyRows = JSON.parse(JSON.stringify(LEGACY.map((executablePath, at) => ({ ...BASE, id: `legacy-${at}`, executablePath }))));
const newRead = fresh.vendors.vendorsFrom(legacyRows);
const oldRead = released.vendors.vendorsFrom(legacyRows);
say(JSON.stringify(newRead) === JSON.stringify(oldRead),
  `(a) a legacy-only row reads the same in this build and in ${oldExtension.tag} — no per-side member appears, so a save writes it back byte for byte`);
const newLegacyWire = JSON.parse(fresh.wire.vendorsEnv(newRead));
const oldLegacyWire = JSON.parse(released.wire.vendorsEnv(oldRead));
// A release before the other-side rule (E1, extension-v0.67.0) sent an other-OS path as it was stored; that difference
// is E1's, and is allowed only for a row this build skips as the other side's. Against a release that knows the rule,
// nothing may differ.
const hereSide = { family: H, systemDrive: fresh.family.systemDriveOf(process.env['SystemDrive']), fileAt: fresh.family.fileAt(statSync) };
const skippedByE1 = typeof released.family.pathForThisSide === 'function' ? [] : fresh.family.otherSideClis(newRead, hereSide);
const differing = newLegacyWire.filter((row, at) => JSON.stringify(row) !== JSON.stringify(oldLegacyWire[at])).map((row) => row.id);
say(newLegacyWire.length === oldLegacyWire.length && differing.every((id) => skippedByE1.includes(id)),
  `(a) and goes into COAI_VENDORS as ${oldExtension.tag} sends it${skippedByE1.length === 0 ? ', identically' : ' — except the other side\'s paths the other-side rule (E1) skips'}`,
  `${newLegacyWire.map((one) => JSON.stringify(one.executablePath)).join(', ')}; differing: ${JSON.stringify(differing)}`);

// ---------- (c) The server settings file THIS build writes → the RELEASED server ----------
// A row the newer extension on the OTHER side saved last: its own value mirrored into the legacy field, THIS side's value
// in its member — which is the fake CLI, so a server that runs the row's path finds it.
const perSide = { ...BASE, id: ID, executablePath: otherSides, executablePathBySide: { [H]: cli, [O]: otherSides, mirrored: otherSides } };
const newFile = fresh.file.serverSettingsJson(fresh.settings.DEFAULTS, fresh.vendors.vendorsFrom(JSON.parse(JSON.stringify([perSide]))), 'compat-new', '');
const wire = JSON.parse(JSON.parse(newFile).COAI_VENDORS ?? '[]');
say(!newFile.includes('executablePathBySide') && !newFile.includes('mirrored'), '(c) the settings file this build writes carries no per-side member');
say(wire.length === 1 && wire[0].executablePath === cli, '(c) and carries THIS side\'s value as the row\'s one string', JSON.stringify(wire[0]?.executablePath));
for (const [name, server] of [[`the released server (${oldVersion})`, old], ['this checkout\'s server', built]]) {
  const answer = await run(server, ['--providers'], dataDir(work, `c-${name.length}-${server.length}`, newFile), { ...noEnv, COAI_EXE_COMPAT_CLI: nowhere });
  const row = rowIn(answer.out);
  say(answer.code === 0 && ranTheFake(row), `(c) ${name} reads that file and runs this side's value`, `exit ${answer.code}, ${JSON.stringify({ cliFound: row.cliFound, version: row.version, note: row.note })}`);
}

// ---------- (b) Mixed versions ----------
// 1. The RELEASED extension on THIS side reads the row the newer other side saved, and writes its server's file.
const oldReadPerSide = released.vendors.vendorsFrom(JSON.parse(JSON.stringify([perSide])));
const oldSent = JSON.parse(released.wire.vendorsEnv(oldReadPerSide))[0]?.executablePath;
say(oldSent !== cli && (oldSent === otherSides || oldSent === ''),
  `(b) the limitation, pinned: ${oldExtension.tag} never runs this side's per-side value — it sends the one legacy value, the other side's path, or nothing once it knows the other-side rule`,
  JSON.stringify(oldSent));
const fallback = { ...noEnv, COAI_EXE_COMPAT_CLI: cli };
const sentRaw = JSON.stringify([{ id: ID, runtime: 'codex', executablePath: otherSides }]);
for (const [name, server, skips] of [[`the released server (${oldVersion})`, old, skipsOtherSide], ['this checkout\'s server', built, true]]) {
  const answer = await run(server, ['--providers'], dataDir(work, `b-${name.length}-${server.length}`, JSON.stringify({ COAI_VENDORS: sentRaw })), fallback);
  const row = rowIn(answer.out);
  if (skips) {
    say(answer.code === 0 && ranTheFake(row), `(b) ${name} skips the other side's path it is sent and looks the CLI up instead`,
      JSON.stringify({ cliFound: row.cliFound, version: row.version }));
  } else {
    note(`(b) ${name} is older than 0.46.0 and probes the other side's path: ${JSON.stringify({ cliFound: row.cliFound, note: row.note })} — the cure is updating it`);
  }
}

// 2. The RELEASED extension's next save of that row, through its own vendor write: `vendorsFrom(read('vendors')).map(...)`.
const thisSides2 = H === 'windows' ? 'D:\\coai-compat-nobody\\codex2.exe' : '/usr/local/coai-compat-nobody/codex2';
const olderSaved = JSON.parse(JSON.stringify(oldReadPerSide.map((v) => (v.id === ID ? { ...v, executablePath: thisSides2 } : v))));
const memberKept = 'executablePathBySide' in olderSaved[0];
note(`(b) ${oldExtension.tag}'s save ${memberKept ? 'KEPT' : 'dropped'} the per-side member`);
say(newerReader(olderSaved, H) === thisSides2, '(b) a newer reader on THIS side resolves the older extension\'s save', JSON.stringify(newerReader(olderSaved, H)));
say(newerReader(olderSaved, O) === (memberKept ? otherSides : ''),
  `(b) and a newer reader on the other side is left with ${memberKept ? 'its own value' : 'the PATH lookup — its value went with the member the older save dropped (update that side)'}`,
  JSON.stringify(newerReader(olderSaved, O)));
// 3. A save that leaves the member as it was (a hand edit of the legacy value) is detected through `mirrored`.
const handEdit = JSON.parse(JSON.stringify([{ ...perSide, executablePath: thisSides2 }]));
say(newerReader(handEdit, H) === thisSides2 && newerReader(handEdit, O) === otherSides,
  '(b) a legacy edit that leaves the member alone wins for this side only — legacy ≠ mirrored — and the other side keeps its value',
  JSON.stringify([newerReader(handEdit, H), newerReader(handEdit, O)]));

console.log('');
if (broken.length === 0) {
  console.log('The boundary holds, both ways.');
  process.exit(0);
}
console.log(`The boundary is BROKEN: ${broken.length} assertion(s) failed.`);
process.exit(1);
