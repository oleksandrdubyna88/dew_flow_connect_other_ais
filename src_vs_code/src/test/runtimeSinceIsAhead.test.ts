import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { after, test } from 'node:test';

import { compareVersions } from '../coaiInstall';
import { RUNTIME_SINCE } from '../runtimeSince';

/**
 * A runtime's "first server that knows it" is a release that must NOT already exist without it (the release-guards family: `releaseAnchors`,
 * `changelogNamesTheRelease`).
 *
 * <p><b>The drift this holds.</b> `KIMI_RUNTIME_SINCE` was written as 0.56.0 while `main` had already moved past the tag it was computed
 * from: two `feat` commits had landed after `mcp-v0.55.0`, so release-please's NEXT release was 0.56.0 — and it ships WITHOUT Kimi, which
 * lands in the one after. Set too low, the gate calls a server that refuses the row by name "new enough" and stays silent on exactly it
 * (`runtimeSince.ts`). The constant is a prediction about a release that does not exist yet, so the only things that can be checked are the
 * two facts around it: the newest RELEASED `src_mcp` (`.release-please-manifest.json`), and the changelog's own account of the release.</p>
 *
 * <p>For every runtime in the table one of these must hold: SINCE is AHEAD of the released version (the prediction is still about the
 * future), or the changelog's `## Server <SINCE>` section — read by `changelog-section.mjs`, the script that writes the release body — NAMES the runtime (it
 * has shipped, and says so), or the runtime is listed below with the reason its release is documented elsewhere.</p>
 */

const repoRoot = path.resolve(__dirname, '..', '..', '..');
const sectionScript = path.join(repoRoot, '.github', 'scripts', 'changelog-section.mjs');
const realChangelog = path.join(repoRoot, 'src_vs_code', 'CHANGELOG.md');
const manifestPath = path.join(repoRoot, '.release-please-manifest.json');

/**
 * Runtimes whose release the changelog's section does not name, with the reason — an entry with no reason is refused. `api` shipped in 0.37.0
 * (E2.3), whose changelog section is about the period switch; its gate was measured live instead (`scripts/live-api-settings-compat.mjs`).
 */
const DOCUMENTED_ELSEWHERE: Readonly<Record<string, string>> = {
  api: 'shipped in 0.37.0 under a changelog section about something else; its gate was measured live by scripts/live-api-settings-compat.mjs',
};

const NONE = '(no section)';

/** The section of the changelog at `changelog` for `mcp-v<version>`, or NONE — the very script the release body is written by. */
function sectionOf(version: string, changelog: string): string {
  const ran = spawnSync(process.execPath, [sectionScript, `mcp-v${version}`, '--changelog', changelog, '--fallback', NONE],
    { encoding: 'utf8', timeout: 30_000 });
  assert.equal(ran.status, 0, `changelog-section.mjs failed: ${ran.stderr}`);

  return ran.stdout.trim();
}

/** Why a runtime's SINCE is not trustworthy, or the empty string. Pure: the three facts are arguments. */
function refusal(runtime: string, since: string, released: string, section: string): string {
  if (compareVersions(since, released) > 0) {
    return '';
  }
  if (section !== NONE && new RegExp(String.raw`\b${runtime}\b`, 'iu').test(section)) {
    return '';
  }
  if ((DOCUMENTED_ELSEWHERE[runtime] ?? '').length > 0) {
    return '';
  }

  return `${runtime}: SINCE ${since} is not ahead of the newest released coai-mcp (${released}) and the changelog's "## Server ${since}" section `
    + `${section === NONE ? 'does not exist' : `does not name ${runtime}`} — a server released without the runtime would be called new enough, `
    + 'and the version gate stays silent on exactly the server that refuses the row. Set it to the next minor AFTER the newest release main will cut.';
}

// reads-another-program: a positive claim about the release manifest — the newest RELEASED src_mcp is a fact written by the release PR, and a
// manifest that stops holding it fails the assertion below loudly (nothing is derived from it).
const releasedMcp = (JSON.parse(fs.readFileSync(manifestPath, 'utf8')) as Record<string, string>)['src_mcp']!;

test('every runtime in RUNTIME_SINCE is either ahead of the newest released coai-mcp or named in its release', () => {
  assert.match(releasedMcp, /^\d+\.\d+\.\d+$/u, 'the manifest holds no src_mcp version — the check below would compare with nothing');
  const rows = Object.entries(RUNTIME_SINCE);
  assert.ok(rows.length >= 3, 'the table shrank — the loop would check less than it says');

  const refusals = rows.map(([runtime, since]) => refusal(runtime, since, releasedMcp, sectionOf(since, realChangelog))).filter((said) => said.length > 0);

  assert.deepEqual(refusals, []);
});

test('every exemption states its reason, and only a runtime that has actually shipped may have one', () => {
  for (const [runtime, reason] of Object.entries(DOCUMENTED_ELSEWHERE)) {
    assert.ok(reason.length > 20, `${runtime}: an exemption with no reason is a way of turning the guard off`);
    assert.ok(compareVersions(RUNTIME_SINCE[runtime] ?? '999', releasedMcp) <= 0, `${runtime}: not released yet — it has no business being exempt`);
  }
});

// ---------------------------------------------------------------- the guard's own teeth

const scratch = fs.mkdtempSync(path.join(os.tmpdir(), 'testsToDelete-runtimeSince-'));
after(() => fs.rmSync(scratch, { recursive: true, force: true }));

function changelogWith(...sections: string[]): string {
  const file = path.join(scratch, `changelog-${fs.readdirSync(scratch).length}.md`);
  fs.writeFileSync(file, `# Changelog\n\n${sections.join('\n\n')}\n`, 'utf8');

  return file;
}

test('a SINCE equal to the newest released version, whose section does not name the runtime, is refused', () => {
  const log = changelogWith('## Server 0.55.0 — 2026-10-15\n\n**Added: something else entirely.**');
  const said = refusal('kimi', '0.55.0', '0.55.0', sectionOf('0.55.0', log));

  assert.match(said, /kimi: SINCE 0\.55\.0 is not ahead of the newest released coai-mcp \(0\.55\.0\)/u);
  assert.match(said, /does not name kimi/u);
});

test('a SINCE below the released version with no section at all is refused too', () => {
  const said = refusal('kimi', '0.54.0', '0.55.0', sectionOf('0.54.0', changelogWith('## Server 0.55.0 — 2026-10-15\n\nx')));

  assert.match(said, /does not exist/u);
});

test('a SINCE ahead of the released version passes with no section, and so does a released one whose section names the runtime', () => {
  assert.equal(refusal('kimi', '0.57.0', '0.55.0', sectionOf('0.57.0', changelogWith('## Server 0.55.0 — 2026-10-15\n\nx'))), '');

  const log = changelogWith('## Server 0.57.0 — 2026-10-20\n\n**Added: Kimi Code as a reviewer.**\n\nmore', '## Server 0.55.0 — 2026-10-15\n\nx');
  assert.equal(refusal('kimi', '0.57.0', '0.57.0', sectionOf('0.57.0', log)), '');
  assert.equal(refusal('kimi', '0.57.0', '0.58.0', sectionOf('0.57.0', log)), '', 'the heading is found by the release script, not by a fixed position');
});

test('the section is the release\'s own: a runtime named only in the NEXT release does not make this one honest', () => {
  const log = changelogWith('## Server 0.58.0 — 2026-10-21\n\n**Added: Kimi Code.**', '## Server 0.57.0 — 2026-10-20\n\n**Something else.**');

  assert.match(refusal('kimi', '0.57.0', '0.58.0', sectionOf('0.57.0', log)), /does not name kimi/u);
});
