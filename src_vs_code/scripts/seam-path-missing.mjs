/**
 * A missing question-consultant root of THIS OS, across the seam (todo/PLAN_paths_per_side.md E5.6).
 *
 * <p>Which roots are missing is decided TWICE: the Settings page asks the disk (`pathFindings` over the bounded probes)
 * and coai-mcp refuses the root and names it in `--providers`' `settingProblems` (E5.2) — and the page then prefers the
 * server's answer. Two suites each agree with themselves; this leg is the live check that the two implementations agree
 * with EACH OTHER over the real binary: the same `(key, value, kind)` set from both halves, for a settings file the
 * extension's own writer produced with one existing root and one that is not there.</p>
 *
 * <p>And the toast's half: the sentence the server carries in `unrecognised` for that root is the one its structured
 * problem names, so `withoutCovered` drops it — one missing folder is one notice, never two (risk 6).</p>
 */
import { promises as fsp } from 'node:fs';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const { serverSettingsJsonWith } = await import('../out/serverSettingsFile.js');
const { settingsFrom } = await import('../out/settingsShape.js');
const { vendorsFrom } = await import('../out/vendors.js');
const { parseProviderNotes, withoutCovered } = await import('../out/providers.js');
const { kindOfState, pathFindings } = await import('../out/pathFindings.js');
const { PathProbes, statProbe } = await import('../out/pathProbes.js');
const { QCONSULT_ROOTS } = await import('../out/pathSettings.js');
const { hostFamily } = await import('../out/hostSide.js');

/**
 * @returns {Promise<{ missing: string, agreed: string[] }>} what both halves called missing
 */
export async function pathMissingSeam({ providersIn, fail }) {
  const dataDir = mkdtempSync(join(tmpdir(), 'coai-seam-missing-data-'));
  const roots = mkdtempSync(join(tmpdir(), 'coai-seam-missing-roots-'));
  const leave = (why) => {
    rmSync(dataDir, { recursive: true, force: true });
    rmSync(roots, { recursive: true, force: true });
    fail(`a missing path of this OS: ${why}`);
  };
  const here = join(roots, 'here');
  mkdirSync(here);
  const gone = join(roots, 'gone');

  // The settings file, by the extension's own writer: one root that is there, one that is not.
  const stored = { qconsultRoots: [here, gone] };
  const write = { writtenBy: '9.9.9', installedServerVersion: '', priceOf: () => undefined, features: [] };
  writeFileSync(join(dataDir, 'settings.json'), serverSettingsJsonWith(settingsFrom((key) => stored[key]), vendorsFrom([]), write), 'utf8');

  // The server's half, through the extension's own parser.
  const notes = parseProviderNotes(JSON.stringify(await providersIn({ COAI_DATA_DIR: dataDir, COAI_VENDORS: '', COAI_PROVIDERS: '' })));
  const server = notes.settingProblems.map((one) => `${one.key}|${one.value}|${one.kind}`).sort();

  // The page's half: the same two roots, the real disk, through the bounded probes — waited for, as the host never does.
  const probes = new PathProbes(statProbe((path) => fsp.stat(path)));
  const values = [here, gone].map((root) => ({ setting: QCONSULT_ROOTS, value: root, path: root }));
  await Promise.all(values.map((one) => probes.settled(one.path, 'folder')));
  const page = pathFindings(values, hostFamily(), (path, kind) => probes.presence(path, kind))
    .map((one) => `${QCONSULT_ROOTS.wireKey}|${one.value}|${kindOfState(one.state)}`).sort();

  const expected = [`${QCONSULT_ROOTS.wireKey}|${gone}|missing-folder`];
  if (JSON.stringify(server) !== JSON.stringify(expected)) {
    leave(`the server did not name exactly the missing root by key, value and kind: it said ${JSON.stringify(notes.settingProblems)}`);
  }
  if (JSON.stringify(page) !== JSON.stringify(server)) {
    leave(`the page and the server disagree about which roots are missing: the page ${JSON.stringify(page)}, the server ${JSON.stringify(server)}`);
  }

  // The toast's half: the sentence the server carries for that root is covered by its structured problem.
  const covered = notes.unrecognised.filter((sentence) => sentence.includes(gone));
  if (covered.length !== 1) {
    leave(`the control failed: the missing root should be one sentence in unrecognised, it is ${JSON.stringify(notes.unrecognised)}`);
  }
  const kept = withoutCovered(notes.unrecognised, notes.settingProblems);
  if (kept.some((sentence) => sentence.includes(gone))) {
    leave(`the generic toast would still carry the missing root's sentence beside its own notice: ${JSON.stringify(kept)}`);
  }

  rmSync(dataDir, { recursive: true, force: true });
  rmSync(roots, { recursive: true, force: true });

  return { missing: gone, agreed: server };
}
