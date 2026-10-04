/**
 * The seam's security-lane leg: the lane as the EXTENSION serializes it, read by the REAL server.
 *
 * <p>`COAI_SECURITY_LANE` is written by `securityEnv` and parsed by `SecurityLaneSetting.Parse`, and the
 * two hold separate copies of what a lane may say — trigger names, member names, stages. The extension
 * keeps an unknown trigger on purpose so the server can refuse it rather than run the prompt more
 * broadly than it was written for; only the two halves together can show that it does. This check
 * used to live in `securityLane.test.ts`, skipped whenever no Debug build sat beside the suite — which
 * in CI, where this script runs against the binary the job built, was always.</p>
 *
 * <p>Three settings, each through the extension's own reader and writer, none hand-written:</p>
 * <ul>
 *   <li>a well-formed lane — the positive control: the server must raise no complaint about it, or the
 *   refusal below could be the server refusing everything. It pairs the shipped "always" prompt
 *   `redteam-general` too: run against a 0.41/0.42 server (`COAI_MCP_DLL`), which knows general only as a
 *   custom prompt, this is the mixed-version check of research/PLAN_the_security_tab_reads_at_a_glance.md;</li>
 *   <li>a custom prompt carrying a trigger this build does not know — the server must refuse it, naming
 *   the prompt;</li>
 *   <li>a malformed setting — the extension sends it switched off with the stored value aside, and the
 *   server must say it refused it rather than look like a lane nobody configured.</li>
 * </ul>
 *
 * <p>Nothing is launched and nothing is billed: `--providers` reads settings and answers.</p>
 */
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const { serverSettingsJson } = await import('../out/serverSettingsFile.js');
const { DEFAULTS } = await import('../out/settingsShape.js');
const { vendorsFrom } = await import('../out/vendors.js');
const { DEFAULT_SECURITY, securityLaneFrom } = await import('../out/securityLane.js');

/** A reviewer row the shipped defaults do not hold, so the server can only know it from the file. */
const ROW = { id: 'seam-security', runtime: 'local', model: 'fixture', enabled: true, code: true };

/** Every `COAI_*` the parent carries, blanked: the file is what this leg is about, and an empty variable defers to it. */
const isolated = (dataDir) => ({
  ...Object.fromEntries(Object.keys(process.env).filter((key) => key.startsWith('COAI_')).map((key) => [key, ''])),
  COAI_DATA_DIR: dataDir,
});

/**
 * Run the leg. Answers what the server said about the refused lane when every claim held; ends the
 * process through `fail` when one did not.
 *
 * @param {{providersIn: Function, fail: Function}} runner the runner's own `--providers` call and failure road
 */
export async function securitySeam({ providersIn, fail }) {
  const dir = mkdtempSync(join(tmpdir(), 'coai-seam-security-'));
  const complaintsAbout = async (stored) => {
    const securityLane = securityLaneFrom(stored);
    writeFileSync(join(dir, 'settings.json'),
      serverSettingsJson({ ...DEFAULTS, securityLane }, vendorsFrom([ROW]), '9.9.9'), 'utf8');
    const answer = await providersIn(isolated(dir));
    return { securityLane, complaints: (answer.unrecognised ?? []).filter((s) => s.includes('COAI_SECURITY_LANE')) };
  };

  try {
    const clean = await complaintsAbout({ ...DEFAULT_SECURITY, enabled: true,
      runs: [{ vendor: ROW.id, prompt: 'redteam-authz', context: 'slice', contextTokens: 4096, stages: ['code'] },
        { vendor: ROW.id, prompt: 'redteam-general', context: 'slice', contextTokens: 4096, stages: ['code'] }] });
    if (clean.complaints.length > 0) {
      fail(`the server refused a well-formed lane the extension wrote: ${clean.complaints.join(' | ')}`);
    }

    // A CUSTOM prompt: general shipped (2026-10-04), and a shipped prompt is not what this leg is about.
    const unknown = await complaintsAbout({ ...DEFAULT_SECURITY, enabled: true,
      prompts: [{ id: 'redteam-fixture', triggers: ['future-detector'], focus: [] }],
      runs: [{ vendor: ROW.id, prompt: 'redteam-fixture' }] });
    if (!unknown.securityLane.prompts.some((p) => p.triggers.includes('future-detector'))) {
      fail('the extension dropped the unknown trigger before it reached the server, so nothing could refuse it');
    }
    const refusal = unknown.complaints.find((s) => s.includes('redteam-fixture') && s.includes('trigger'));
    if (refusal === undefined) {
      fail(`the server did not refuse a prompt whose trigger it does not know. It said: ${unknown.complaints.join(' | ') || '(nothing)'}`);
    }

    const malformed = await complaintsAbout({ ...DEFAULT_SECURITY, enabled: true,
      runs: [{ vendor: ROW.id, prompt: 'redteam-authz', stages: 1 }] });
    if (!('invalidConfiguration' in malformed.securityLane) || malformed.securityLane.enabled) {
      fail('the extension read a malformed lane as a usable one');
    }
    if (malformed.complaints.length === 0) {
      fail('the server said nothing about a malformed lane, so it reads as a lane nobody configured');
    }

    return { refusal, malformed: malformed.complaints[0] };
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}
