import { API_RUNTIME_SINCE, apiRuntimeSkewNote, type ApiRow } from './apiRuntime';
import { compareVersions } from './coaiInstall';

/**
 * Which `coai-mcp` first knows each runtime that NOT every released server knows — and the gate that keeps a row of
 * such a runtime out of an older server's settings file (todo/PLAN_vibe_and_kimi_runtimes.md D13, path 1).
 *
 * <p><b>What an older server does with such a row.</b> Before mcp 0.44.0 its `RuntimeOf` turned a runtime it did not
 * know into `codex` — a Grok row rode the Codex CLI against xAI's endpoint (§4.13 of the feature-review plan). Since
 * 0.44.0 it refuses the row by name; measured 2026-10-13 against the released 0.52.0, a vibe row comes back from
 * `--providers` as `unavailable`, "not one this build of coai-mcp runs". Neither reviews with it, so the row is withheld
 * from the file — not merely greyed on the card — while every other row still crosses, and the card says why. One
 * table, one arm per runtime, so the next runtime (Kimi, S4) is a line here rather than a second gate beside the
 * first.</p>
 *
 * <p>Pure and `vscode`-free. It imports only from `apiRuntime.ts` and `coaiInstall.ts`, never from `vendors.ts`, which
 * imports from here (`importCycles.test.mjs` froze the count of cycles).</p>
 */

/**
 * The first `coai-mcp` that knows `runtime: "vibe"`. mcp 0.51.0 shipped the Vibe ground (the billing refusal, the
 * snapshot, the Kimi shim — #785) without the runtime, and mcp 0.52.0 was RELEASED from main on 2026-10-13 while the
 * runtime was still on its branch — so the earliest release that can carry it is the next minor, 0.53.0 (release-please
 * bumps the minor for a `feat` below 1.0). Set too LOW this stays quiet on exactly the server that would run the row as
 * codex; set too high it only holds a row back from a server that could run it. Whoever cuts the release keeps it level
 * with the tag.
 */
export const VIBE_RUNTIME_SINCE = '0.53.0';

/** Every runtime some released server does not know, with the release that first knows it. */
const RUNTIME_SINCE: Readonly<Record<string, string>> = {
  api: API_RUNTIME_SINCE,
  vibe: VIBE_RUNTIME_SINCE,
};

/**
 * Whether the installed server reads this runtime — true for a runtime every server knows, and for an UNKNOWN server.
 *
 * <p>Unknown is not old: every other skew gate here stays silent on a server it cannot version, and withholding rows
 * on a guess would drop a configured reviewer from a server perfectly able to run it.</p>
 */
export function runtimeOnServer(runtime: string, installedServerVersion: string): boolean {
  const since = RUNTIME_SINCE[runtime];

  return since === undefined || installedServerVersion.length === 0 || compareVersions(since, installedServerVersion) <= 0;
}

/**
 * The sentence on a card whose row the installed server would run as the wrong vendor — or nothing when there is no
 * such row, the server is new enough, or its version is unknown. An `api` row keeps its own sentence, which names the
 * endpoint it would be sent to.
 */
export function runtimeSkewNote(installedServerVersion: string, vendors: readonly ApiRow[]): string {
  const api = apiRuntimeSkewNote(installedServerVersion, vendors);
  const held = vendors.filter((vendor) => vendor.enabled && vendor.runtime !== 'api' && !runtimeOnServer(vendor.runtime, installedServerVersion));

  return [api, ...held.map((vendor) => heldNote(installedServerVersion, vendor))].filter((note) => note.length > 0).join(' ');
}

/**
 * What the card says, and deliberately not what the server would do instead: measured 2026-10-13, the released mcp
 * 0.52.0 REFUSES a vibe row by name ("not one this build of coai-mcp runs"), as every server since 0.44.0 does, while
 * one older than that runs it as codex. Either way it does not review with the row, which is the one claim made here.
 */
function heldNote(installedServerVersion: string, vendor: ApiRow): string {
  return `The coai-mcp you have installed (${installedServerVersion}) does not know the ${vendor.runtime} runtime, so it `
    + `cannot review with ${vendor.id}. This row is kept out of the settings file until you update it to `
    + `${RUNTIME_SINCE[vendor.runtime]} or later — the MCP server tab.`;
}
