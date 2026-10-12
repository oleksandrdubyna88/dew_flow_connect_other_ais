import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';

import { codeOf, shippedSources } from './dataDirWriters';

/**
 * Every `coai-mcp` mode this extension launches, classified — and every one launched to READ is held by the server's own
 * test to leave a missing NAMED data directory absent (research/PLAN_paths_per_side.md E5.7; the risk consultation of story
 * 5.7, item C, measured: `--bugs-json`, `--pairs-json`, the three pair reads and `--providers` made the folder the
 * extension refuses to make).
 *
 * <p>The modes are the server's own list (`Program.Classify`), read from `src_mcp/src/Program.cs`; the launches are the
 * mode names this extension's shipped code spells. A mode the extension starts using is red here until it is classified;
 * a READER is red until `AReaderModeNeverMakesTheDataDirectoryTests.cs` runs it against a missing data directory.</p>
 */

type Launch =
  /** Launched to read — on a render, a probe, a page — with nobody acting: must never make the data directory. */
  | 'reader'
  /**
   * Launched on a person's own act, and it WRITES under the data directory: the server refuses it while the named root is
   * missing (exit 74, `Program.WriterModes`) and its test runs it against one (research/PLAN_names_and_a_missing_root.md).
   */
  | 'a person acts, refused by the server'
  /**
   * Launched on a person's own act, and measured to make no missing named root: `--close-consult` finds no record there and
   * refuses (65); `--tree-remove` works under the review root. Held by the server's reader test.
   */
  | 'a person acts, makes no missing root'
  /**
   * A person's paid check, which writes its state under the data directory: not started while the named root is missing
   * (`consultantCheckRun.runConsultantCheck`), and refused by the server too (research/PLAN_names_and_a_missing_root.md 3.3).
   */
  | 'a person acts, held by the extension'
  /** Launched on a person's act, but reads stdin and answers stdout only: never touches the data directory (`CheckSecurityMode`). */
  | 'touches no data directory'
  /** The consultants survey: writes its health file, and is not started while the named root is missing (`consultantsProbe`). */
  | 'survey, held by the extension'
  /** The literal names a mode but launches no `coai-mcp` (another program's flag). */
  | 'not a coai-mcp launch';

const LAUNCHES: Readonly<Record<string, Launch>> = {
  '--log': 'reader',
  '--findings': 'reader',
  '--findings-many': 'reader',
  '--bugs-json': 'reader',
  '--pairs-json': 'reader',
  '--real-method': 'reader',
  '--file-at': 'reader',
  '--tree-at': 'reader',
  '--trees': 'reader',
  '--round-counts': 'reader',
  '--providers': 'reader',
  '--features': 'reader',
  '--version': 'reader',
  '--cadence': 'reader',
  '--collect-bugs': 'a person acts, refused by the server',
  '--pairs-keep': 'a person acts, refused by the server',
  '--pairs-decide': 'a person acts, refused by the server',
  '--tree-remove': 'a person acts, makes no missing root',
  '--upload-pairs': 'a person acts, refused by the server',
  '--close-consult': 'a person acts, makes no missing root',
  '--check-consultant': 'a person acts, held by the extension',
  '--check-model': 'a person acts, held by the extension',
  '--check-security': 'touches no data directory',
  '--probe-api': 'a person acts, refused by the server',
  '--consultants': 'survey, held by the extension',
  '--help': 'not a coai-mcp launch',
};

const REPO = path.join(__dirname, '..', '..', '..');

/** The server's modes, as `Program.Classify` maps them: `"--bugs-json" => Startup.Bugs`. */
function serverModes(): ReadonlySet<string> {
  // reads-another-program: the server's mode list is the subject of this inventory, and the read fails LOUDLY — the
  // first test asserts the parse found --bugs-json, --providers and more than twenty modes, so a changed shape is red.
  const program = fs.readFileSync(path.join(REPO, 'src_mcp', 'src', 'Program.cs'), 'utf8');

  return new Set([...program.matchAll(/"(--[a-z-]+)"(?: or "[^"]+")* => Startup\./gu)].map((match) => match[1]!));
}

/** The modes the extension's shipped code spells as a literal. */
function launchedModes(modes: ReadonlySet<string>): ReadonlySet<string> {
  const spelled = shippedSources().flatMap((file) => [...codeOf(file).matchAll(/'(--[a-z-]+)'/gu)].map((match) => match[1]!));

  return new Set(spelled.filter((flag) => modes.has(flag)));
}

test('the scan reads the server\'s modes — a parse that found nothing would pass forever', () => {
  const modes = serverModes();

  assert.ok(modes.has('--bugs-json') && modes.has('--providers') && modes.size > 20, `Program.Classify was not read: ${[...modes].join(' ')}`);
});

test('every coai-mcp mode the extension launches is classified — a new launch is red until it says whether it reads', () => {
  const launched = [...launchedModes(serverModes())].sort();

  assert.deepEqual(launched, Object.keys(LAUNCHES).sort(), 'a mode the extension launches appeared or went: classify it in LAUNCHES');
});

test('every READER is run by the server\'s test against a missing data directory', () => {
  // reads-another-program: a positive assertion per reader — each named mode must appear in the server's own test, so a
  // renamed or emptied file turns this red, never quiet.
  const held = fs.readFileSync(path.join(REPO, 'src_mcp', 'tests', 'AReaderModeNeverMakesTheDataDirectoryTests.cs'), 'utf8');
  const covered = new Set([...held.matchAll(/"(--[a-z-]+)"/gu)].map((match) => match[1]!));

  for (const [mode] of Object.entries(LAUNCHES).filter(([, launch]) => HELD_BY_THE_READER_TEST.has(launch))) {
    assert.ok(covered.has(mode), `${mode} must make no missing named root, and AReaderModeNeverMakesTheDataDirectoryTests.cs never runs it against a missing data directory`);
  }
});

/** The kinds the server's reader test must run: none of them may make a missing named root. */
const HELD_BY_THE_READER_TEST: ReadonlySet<Launch> = new Set<Launch>(['reader', 'touches no data directory', 'a person acts, makes no missing root']);

/** The kinds that write, which the server refuses under a missing named root and its writer test runs. */
const HELD_BY_THE_WRITER_TEST: ReadonlySet<Launch> = new Set<Launch>(['a person acts, held by the extension', 'a person acts, refused by the server']);

test('every writer a person starts is refused by the server under a missing root — the two halves agree', () => {
  // reads-another-program: research/PLAN_names_and_a_missing_root.md — the extension does not start the checks while the
  // named root is missing, and the server's own test runs every writer against one (exit 74, nothing made), so a mode
  // started another way — a terminal, an older extension — still makes nothing.
  const held = fs.readFileSync(path.join(REPO, 'src_mcp', 'tests', 'AWriterModeRefusesAMissingNamedRootTests.cs'), 'utf8');
  const covered = new Set([...held.matchAll(/"(--[a-z-]+)"/gu)].map((match) => match[1]!));

  for (const [mode] of Object.entries(LAUNCHES).filter(([, launch]) => HELD_BY_THE_WRITER_TEST.has(launch))) {
    assert.ok(covered.has(mode), `${mode} writes on a person's act, and AWriterModeRefusesAMissingNamedRootTests.cs never runs it against a missing data directory`);
  }
});

test('no mode a person starts is left unmeasured — each is held by the reader test or the writer test', () => {
  // The coordinator's follow-up of 2026-10-11: "a person acts" used to be a kind no test held. Every launch now names
  // which server test holds it, so a new person's mode is red here until it is measured.
  const unheld = Object.entries(LAUNCHES)
    .filter(([, launch]) => !HELD_BY_THE_READER_TEST.has(launch) && !HELD_BY_THE_WRITER_TEST.has(launch))
    .map(([mode]) => mode);

  assert.deepEqual(unheld.sort(), ['--consultants', '--help'], 'a mode is held by neither server test — measure it against a missing root');
});
