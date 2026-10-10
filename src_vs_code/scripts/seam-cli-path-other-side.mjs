/**
 * A reviewer's CLI path of the OTHER operating system, across the seam (todo/PLAN_paths_per_side.md E1.2).
 *
 * <p>VS Code shares its settings between a WSL window and a Windows window, so a reviewer row's `executablePath` written on
 * one side is read on the other. Which value this side runs is decided TWICE: the extension writes THIS side's value into
 * `COAI_VENDORS` (`pathForThisSide`), and coai-mcp skips an other-OS path itself (`ExecutablePaths.Here`) for a settings
 * file an older extension wrote. Both suites answer one vector file; this leg checks the two against each other over the
 * real binary, through `--providers` — the row's `cliFound`, `version` and `note`, the probe's own answer.</p>
 *
 * <p>The row is `seam-cli`, and `COAI_EXE_SEAM_CLI` names the server's own fake CLI — the server's fallback for a row with
 * no path of its own. So a server that SKIPS the other side's path probes the fake CLI and finds it (its version is the
 * fake's); one that probes the path finds nothing and says the path "was not found on this machine". The control: a path of
 * THIS OS that does not exist is still probed as written and is not found — proof the probe reads the row's path at all.
 * Nothing reaches a model.</p>
 */
import { cpSync, mkdtempSync, rmSync, statSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';

const { vendorsEnv } = await import('../out/vendorsWire.js');
const { vendorsFrom } = await import('../out/vendors.js');
const { pathForThisSide } = await import('../out/pathFamily.js');
const { hostExecutableSide, hostFamily } = await import('../out/hostSide.js');
const { fileAt, systemDriveOf, useFileProbe } = await import('../out/pathFamily.js');

// The host's disk, as the extension's activation installs it: the page's half asks it about other-OS spellings.
useFileProbe(fileAt(statSync));

const ID = 'seam-cli';
const FAKE_VERSION = 'fake-cli 9.9.9';

/**
 * @returns {Promise<{ skipped: string }>} the other side's path the leg saw skipped by both halves
 */
export async function cliPathOtherSideSeam({ providersIn, fakeCli, fail }) {
  const dataDir = mkdtempSync(join(tmpdir(), 'coai-seam-clipath-'));
  const leave = (why) => {
    rmSync(dataDir, { recursive: true, force: true });
    fail(`a CLI path of the other side: ${why}`);
  };
  const otherSides = hostFamily() === 'windows' ? '/usr/local/bin/coai-seam-nobody/codex' : 'C:\\coai-seam-nobody\\codex.cmd';
  const missingHere = join(dataDir, 'gone', 'codex');
  const asked = (executablePath) => vendorsFrom([{ id: ID, runtime: 'codex', model: '', enabled: true, plan: true, code: true, executablePath }]);
  const probed = async (vendorsJson, extra = {}) => (await providersIn({
    COAI_DATA_DIR: dataDir, COAI_VENDORS: vendorsJson, COAI_PROVIDERS: '', COAI_EXE_SEAM_CLI: fakeCli, FAKECLI_MODE: 'vendor', FAKECLI_STDOUT: FAKE_VERSION, ...extra,
  })).providers?.find((one) => one.provider === ID) ?? {};

  // (a) The page's half: the extension's own writer sends nothing for the other side's path, and names what it skipped.
  const page = pathForThisSide({ executablePath: otherSides }, hostExecutableSide());
  const written = JSON.parse(vendorsEnv(asked(otherSides)))[0]?.executablePath;
  if (page.otherSide !== otherSides || written !== '') {
    leave(`the extension did not skip ${otherSides}: the page said ${JSON.stringify(page)}, COAI_VENDORS carried ${JSON.stringify(written)}`);
  }

  // The control: an explicit path of THIS OS that does not exist is probed as written — and is unavailable.
  const control = await probed(JSON.stringify([{ id: ID, runtime: 'codex', executablePath: missingHere }]));
  if (control.cliFound !== false || !String(control.note).includes(missingHere)) {
    leave(`the control failed: a missing path of this OS (${missingHere}) was not probed as written (${JSON.stringify(control)}), so --providers cannot be read for a row's CLI path at all`);
  }

  // (b) The server's half: a file an OLDER extension wrote, carrying the other side's path as it is stored.
  const older = await probed(JSON.stringify([{ id: ID, runtime: 'codex', executablePath: otherSides }]));
  // (c) And the file this extension writes.
  const newer = await probed(vendorsEnv(asked(otherSides)));
  if (!ranTheFake(older) || !ranTheFake(newer)) {
    leave(`the server did not look the CLI up for a row whose path is the other side's: an older extension's file probed ${JSON.stringify(older)}, this one's ${JSON.stringify(newer)}`);
  }
  const kept = await rootRelativeKept({ probed, fakeCli, dataDir, leave });
  rmSync(dataDir, { recursive: true, force: true });

  return { skipped: otherSides, kept };
}

/**
 * (d) The cadence consultant's case (E1): on Windows `/Users/…/FakeCli.exe` is a legal root-relative path. A COPY of the fake
 * CLI on the system drive, spelled without its drive letter, must run — and the row's PATH-side fallback
 * (`COAI_EXE_SEAM_CLI`) is pointed at a file that does not exist, so which one ran is visible: the copy answers with the
 * fake's version, the fallback would be "not found". The page's half must write the same path, qualified. Only a Windows
 * host can spell a file of its own that way; elsewhere the leg says it did not run, and why.
 */
async function rootRelativeKept({ probed, fakeCli, dataDir, leave }) {
  const systemDrive = systemDriveOf(process.env['SystemDrive']);
  const home = mkdtempSync(join(tmpdir(), 'coai-seam-clicopy-'));
  if (hostFamily() !== 'windows' || !home.toLowerCase().startsWith(systemDrive.toLowerCase())) {
    rmSync(home, { recursive: true, force: true });

    return 'not run — only a Windows host with its temp folder on the system drive can spell its own file without a drive letter';
  }
  cpSync(dirname(fakeCli), home, { recursive: true });
  const copy = join(home, 'FakeCli.exe');
  const spelled = copy.slice(systemDrive.length).replace(/\\/g, '/');
  const fallback = join(dataDir, 'not-the-fallback', 'codex.exe');
  const probedWith = (vendorsJson) => probed(vendorsJson, { COAI_EXE_SEAM_CLI: fallback });
  const asked = vendorsFrom([{ id: ID, runtime: 'codex', model: '', enabled: true, plan: true, code: true, executablePath: spelled }]);
  const page = pathForThisSide({ executablePath: spelled }, hostExecutableSide());
  const written = JSON.parse(vendorsEnv(asked))[0]?.executablePath;
  const older = await probedWith(JSON.stringify([{ id: ID, runtime: 'codex', executablePath: spelled }]));
  const newer = await probedWith(vendorsEnv(asked));
  rmSync(home, { recursive: true, force: true });
  if (page.path.toLowerCase() !== copy.toLowerCase() || String(written).toLowerCase() !== copy.toLowerCase()) {
    leave(`the extension did not keep ${spelled} qualified as ${copy}: the page said ${JSON.stringify(page)}, COAI_VENDORS carried ${JSON.stringify(written)}`);
  }
  if (!ranTheFake(older) || !ranTheFake(newer)) {
    leave(`the server did not run the existing ${spelled} (a different installation, or none, would have answered): an older extension's file probed ${JSON.stringify(older)}, this one's ${JSON.stringify(newer)}`);
  }

  return `${spelled} → ${copy}`;
}

const ranTheFake = (row) => row.cliFound === true && row.version === FAKE_VERSION;
