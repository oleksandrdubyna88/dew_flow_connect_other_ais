/**
 * The question consultant's roots across the seam — a root of the OTHER operating system (research/PLAN_qconsult_roots_of_the_other_os.md).
 *
 * <p>VS Code shares its settings between a WSL window and a Windows window, so the roots list carries both sides' folders.
 * Which roots are the other side's is decided TWICE: the server skips them (`QuestionRoots.OtherSideHere`, after
 * `Qualified`), the Settings page says so beside them (`pathFamily.ts`). Two suites answer one vector file; this leg is
 * the live check that the two implementations agree against each other, over the real binary.</p>
 *
 * <p>What the server REPORTS, through what it already exposes — no flag was added for this leg:</p>
 * <ul>
 *   <li>`--providers`' `unrecognised`, read through the extension's own `parseProviderNotes` — the very list the panel
 *   turns into the `server-did-not-understand-a-setting` toast. A missing root of THIS OS must appear there (the
 *   control that the probe can see a root at all); the other side's root must not.</li>
 *   <li>`ask_consultants` over a live MCP session, one root at a time, with one `question-disk` row: a root the server
 *   skipped leaves the row `disabled`, "inactive on this side"; a root it kept is launched, and the row's note names
 *   the path it kept ("root … is not a git checkout") — qualified with the system drive on Windows. The row's CLI is
 *   the server's own fake CLI: nothing reaches a model, nothing is billed.</li>
 * </ul>
 *
 * <p>OS-independent: the other side's spelling is chosen by `process.platform`, and on Windows this machine's root is
 * written ROOT-RELATIVE when the temp folder is on the system drive, so the qualification is exercised too.</p>
 */
import { existsSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { promises as fsp } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const { serverSettingsJsonWith } = await import('../out/serverSettingsFile.js');
const { settingsFrom } = await import('../out/settingsShape.js');
const { vendorsFrom } = await import('../out/vendors.js');
const { parseProviderNotes } = await import('../out/providers.js');
const { directoryAt, existingHere, otherSideHere, qualified, systemDriveOf } = await import('../out/pathFamily.js');

/** The words each decision is recognised by — the server's own sentences, never retyped as a whole. */
const INACTIVE = 'inactive on this side';
const KEPT = (path) => `root ${path} is not a git checkout`;

/**
 * @returns {Promise<{ kept: string, skipped: string }>} what the leg saw, for its line
 */
export async function qconsultRootsSeam({ providersIn, sessionsFor, answerOf, binary, fakeCli, repoPath, fail, timeoutMs }) {
  const windows = process.platform === 'win32';
  const systemDrive = systemDriveOf(process.env['SystemDrive']);
  const dataDir = mkdtempSync(join(tmpdir(), 'coai-seam-qroots-'));
  const here = mkdtempSync(join(tmpdir(), 'coai-seam-qroot-here-'));
  const leave = async (why, session) => {
    await session?.killAndWait();
    rmSync(dataDir, { recursive: true, force: true });
    rmSync(here, { recursive: true, force: true });
    fail(`question-consultant roots: ${why}`);
  };

  const thisRoot = thisSidesSpelling(here, windows, systemDrive);
  const otherRoot = windows ? '/home/coai-seam-nobody/git' : 'C:\\Users\\coai-seam-nobody\\git';
  const missingHere = join(here, 'gone');
  if (existsSync(otherRoot)) {
    await leave(`${otherRoot} exists on this machine, so it cannot stand for the other side's folder`);
  }

  // (a) No complaint for the other side's root — and the control: a missing root of THIS OS is complained about.
  write(dataDir, [thisRoot, otherRoot, missingHere], fakeCli);
  const notes = parseProviderNotes(JSON.stringify(await providersIn({ COAI_DATA_DIR: dataDir, COAI_VENDORS: '', COAI_PROVIDERS: '' })));
  if (!notes.unrecognised.some((sentence) => sentence.includes(missingHere))) {
    await leave(`the control failed: a missing root of this OS (${missingHere}) was not complained about, so --providers cannot be read for roots at all. It said: ${JSON.stringify(notes.unrecognised)}`);
  }
  // In EITHER spelling: a server that judged the root would name it qualified (`C:\home\…`), as a break-it showed —
  // a check for the written spelling alone passed against a server that complained about it.
  const spellings = [otherRoot, qualified(otherRoot, windows, systemDrive)];
  const complained = notes.unrecognised.filter((sentence) => spellings.some((one) => sentence.toLowerCase().includes(one.toLowerCase())));
  if (complained.length > 0) {
    await leave(`the other side's root reached the "server did not understand a setting" list: ${JSON.stringify(complained)}`);
  }

  // (b) + (c) One live server; each root alone, so the row's fate is that root's.
  const session = sessionsFor({ command: 'dotnet', args: [binary], dataDir, timeoutMs })({ COAI_VENDORS: '', COAI_PROVIDERS: '' });
  try {
    await session.ready;
  } catch (e) {
    await leave(`the binary never finished the MCP handshake: ${e.message}`, session);
  }
  const isDirectory = directoryAt((path) => fsp.stat(path));
  const seen = {};
  for (const root of [thisRoot, otherRoot]) {
    write(dataDir, [root], fakeCli);
    const server = await serversVerdict(session, answerOf, repoPath, root).catch(async (e) => leave(`ask_consultants failed for ${root}: ${e.message}`, session));
    const existence = await existingHere([root], windows, systemDrive, isDirectory);
    const page = otherSideHere(root, windows, existence.existing.includes(root), existence.unknown.includes(root)) ? 'other' : 'this';
    if (server.side !== page) {
      await leave(`the page and the server disagree about ${root}: the page calls it ${page} side's, the server ${server.side} side's (${server.said})`, session);
    }
    seen[root] = server;
  }
  await session.close();

  // (b) This machine's root is KEPT, and kept qualified (on Windows, on the system drive).
  const keptAs = canonical(qualified(thisRoot, windows, systemDrive));
  if (!sameText(seen[thisRoot].said, KEPT(keptAs), windows)) {
    await leave(`this machine's root ${thisRoot} was not kept as ${keptAs}: the row said ${seen[thisRoot].said}`);
  }
  rmSync(dataDir, { recursive: true, force: true });
  rmSync(here, { recursive: true, force: true });

  return { kept: `${thisRoot} → ${keptAs}`, skipped: otherRoot };
}

/** On Windows, this machine's folder written from the root of the drive when it is on the system drive — `/Users/…`. */
function thisSidesSpelling(dir, windows, systemDrive) {
  return windows && dir.toLowerCase().startsWith(systemDrive.toLowerCase())
    ? dir.slice(systemDrive.length).replace(/\\/g, '/')
    : dir;
}

/** The settings file, written by the extension's own writer: one `question-disk` row, these roots. */
function write(dataDir, roots, fakeCli) {
  const stored = {
    qconsultRows: [{
      id: 'seam-disk', vendor: 'claude', runtime: 'claude', model: 'seam', baseUrl: '', executablePath: fakeCli, key: '',
      prompt: 'question-disk', enabled: true,
    }],
    qconsultRoots: roots,
  };
  const write = { writtenBy: '9.9.9', installedServerVersion: '', priceOf: () => undefined, features: [] };
  writeFileSync(join(dataDir, 'settings.json'), serverSettingsJsonWith(settingsFrom((key) => stored[key]), vendorsFrom([]), write), 'utf8');
}

/** What the server did with ONE root: `other` when the row is inactive here naming it, `this` when it launched reading it. */
async function serversVerdict(session, answerOf, repoPath, root) {
  const reply = answerOf(await session.call('ask_consultants', {
    repoPath,
    question: 'Which of the two parsers should own the count?',
    context: 'Two classes both count; the seam asks only to see which folders the disk row is given.',
  }));
  const row = (reply.answers ?? [])[0];
  if (row === undefined) {
    throw new Error(`no row answered: ${JSON.stringify(reply).slice(0, 400)}`);
  }
  if (row.status === 'disabled' && String(row.reason).includes(INACTIVE)) {
    return { side: 'other', said: String(row.reason) };
  }
  if (row.status !== 'disabled' && row.status !== 'blocked' && String(row.note).length > 0) {
    return { side: 'this', said: String(row.note) };
  }

  throw new Error(`the row says neither: ${JSON.stringify(row).slice(0, 400)}`);
}

/** A path as the server spells a kept root: its own full path, no trailing separator. */
function canonical(path) {
  return path.replace(/[\\/]+$/, '');
}

function sameText(note, expected, windows) {
  return windows ? note.toLowerCase().includes(expected.toLowerCase()) : note.includes(expected);
}

