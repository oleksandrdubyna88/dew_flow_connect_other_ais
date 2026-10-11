import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';

import { codeOf, shippedSources } from './dataDirWriters';

/**
 * Every `coai-mcp` mode this extension launches, classified — and every one launched to READ is held by the server's own
 * test to leave a missing NAMED data directory absent (todo/PLAN_paths_per_side.md E5.7; the risk consultation of story
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
  /** Launched on a person's own act — a button, a check, a send: the person's consent, as the move flow is. */
  | 'a person acts'
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
  '--collect-bugs': 'a person acts',
  '--pairs-keep': 'a person acts',
  '--pairs-decide': 'a person acts',
  '--tree-remove': 'a person acts',
  '--upload-pairs': 'a person acts',
  '--close-consult': 'a person acts',
  '--check-consultant': 'a person acts',
  '--check-model': 'a person acts',
  '--check-security': 'a person acts',
  '--probe-api': 'a person acts',
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

  for (const [mode] of Object.entries(LAUNCHES).filter(([, launch]) => launch === 'reader')) {
    assert.ok(covered.has(mode), `${mode} is launched to read, and AReaderModeNeverMakesTheDataDirectoryTests.cs never runs it against a missing data directory`);
  }
});
