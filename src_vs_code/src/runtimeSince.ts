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
 * table, one arm per runtime, so the next runtime (Kimi, S4, joined 2026-10-15) is a line here rather than a second gate beside the
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

/**
 * The first `coai-mcp` that knows `runtime: "kimi"`: the release that carries E4.1–E4.5 of
 * todo/PLAN_vibe_and_kimi_runtimes_E4.md (the junction-safe home, the shim's flags, `KimiRuntime`, this story).
 *
 * <p><b>Which version, and why.</b> release-please bumps the minor for a `feat` below 1.0. The newest RELEASED `src_mcp` when this was
 * written (2026-10-15) is 0.55.0 (tag `mcp-v0.55.0`; `.release-please-manifest.json`), but `main` already holds two `feat` commits after it
 * (#828 and #829), so the release it will cut next — 0.56.0 — ships WITHOUT Kimi, and the stories land in the one after: 0.57.0. A server
 * released without the runtime refuses a kimi row by name ("not one this build of coai-mcp runs"), as every server since 0.44.0 does. An
 * earlier draft said 0.56.0, computed from the tag alone; set too LOW this stays quiet on exactly the server that would refuse the row, set
 * too high it only holds a row back from a server that could run it. <b>Whoever cuts the release keeps it level with the tag</b> — if another
 * minor ships before Kimi, raise it. `runtimeSinceIsAhead.test.ts` holds the part a test can: every entry of the table is ahead of the newest
 * released `src_mcp` or named in its changelog section (it cannot see unreleased commits on `main`, which is how 0.56.0 got through).</p>
 */
export const KIMI_RUNTIME_SINCE = '0.57.0';

/**
 * The first `coai-mcp` that RUNS a Vibe row on a Linux host — on WSL2 only, in that release (DL1 of
 * todo/PLAN_kimi_and_vibe_on_linux.md). `RUNTIME_SINCE` answers "does this server know the runtime"; this answers "does it
 * run it on THIS host", and they are different questions: a 0.57.0 server on WSL knows `vibe` and still refuses the row at
 * launch with the D18 sentence (DL14). The release that carries E2 and E3 is the next `src_mcp` minor after the newest
 * released one (0.57.0 when this was written, `.release-please-manifest.json`), so 0.58.0. <b>Whoever cuts the release keeps
 * it level with the tag</b>; `runtimeSinceIsAhead.test.ts` holds this table to the same guard as `RUNTIME_SINCE`.
 */
export const VIBE_ON_LINUX_SINCE = '0.58.0';

/**
 * Which `coai-mcp` first runs each runtime on a Linux or WSL host. A runtime with no entry runs on every host its server
 * knows it on. Kimi's entry (`KIMI_ON_LINUX_SINCE`) comes with the release that lifts Kimi's own host refusal (E6), not
 * before: Kimi on Linux is a later release, and an entry for it now would call a refusal a feature.
 */
export const ON_LINUX_SINCE: Readonly<Record<string, string>> = {
  vibe: VIBE_ON_LINUX_SINCE,
};

/** Every runtime some released server does not know, with the release that first knows it. */
export const RUNTIME_SINCE: Readonly<Record<string, string>> = {
  api: API_RUNTIME_SINCE,
  vibe: VIBE_RUNTIME_SINCE,
  kimi: KIMI_RUNTIME_SINCE,
};

/**
 * Whether the installed server reads this runtime — true for a runtime every server knows, and for an UNKNOWN server.
 *
 * <p>Unknown is not old: every other skew gate here stays silent on a server it cannot version, and withholding rows
 * on a guess would drop a configured reviewer from a server perfectly able to run it.</p>
 */
export function runtimeOnServer(runtime: string, installedServerVersion: string): boolean {
  return sinceMet(RUNTIME_SINCE[runtime], installedServerVersion);
}

/**
 * Whether the installed server runs this runtime on a Linux or WSL host — true for a runtime with no Linux entry, and for
 * an UNKNOWN server (the rule of {@link runtimeOnServer}: unknown is not old).
 */
export function runtimeOnLinuxSide(runtime: string, installedServerVersion: string): boolean {
  return sinceMet(ON_LINUX_SINCE[runtime], installedServerVersion);
}

/**
 * The one rule both tables share: a runtime with no entry is met on every server, an UNKNOWN server is not called old, and
 * otherwise the installed version must have reached the release.
 */
function sinceMet(since: string | undefined, installedServerVersion: string): boolean {
  return since === undefined || installedServerVersion.length === 0 || compareVersions(since, installedServerVersion) <= 0;
}

/**
 * The sentence on a card whose row the installed server would run as the wrong vendor — or nothing when there is no
 * such row, the server is new enough, or its version is unknown. An `api` row keeps its own sentence, which names the
 * endpoint it would be sent to.
 *
 * <p>On a Linux or WSL side (`onLinux`) there is one more case (DL14): a server that KNOWS the runtime but is older than
 * `ON_LINUX_SINCE` refuses the row at launch, and the card says to update instead of showing a row that cannot run. A
 * server that does not know the runtime at all has already said so, once — never two notes for one row.</p>
 */
export function runtimeSkewNote(installedServerVersion: string, vendors: readonly ApiRow[], onLinux = false): string {
  const api = apiRuntimeSkewNote(installedServerVersion, vendors);
  const switchedOn = vendors.filter((vendor) => vendor.enabled && vendor.runtime !== 'api');
  const held = switchedOn.filter((vendor) => !runtimeOnServer(vendor.runtime, installedServerVersion));
  const refusedHere = onLinux
    ? switchedOn.filter((vendor) => !held.includes(vendor) && !runtimeOnLinuxSide(vendor.runtime, installedServerVersion))
    : [];

  return [
    api,
    ...held.map((vendor) => heldNote(installedServerVersion, vendor)),
    ...refusedHere.map((vendor) => linuxNote(installedServerVersion, vendor)),
  ].filter((note) => note.length > 0).join(' ');
}

/**
 * What a Linux or WSL card says when its server knows the runtime and still refuses the row here. It claims only what is
 * measured: `ON_LINUX_SINCE` is the first release that runs the row, and it runs it on WSL2 alone — a native Linux host is
 * refused at any version, with a sentence of its own, so this note does not promise it.
 */
function linuxNote(installedServerVersion: string, vendor: ApiRow): string {
  return `Update coai-mcp to run ${RUNTIME_LABEL[vendor.runtime] ?? vendor.runtime} here: the coai-mcp you have installed (${installedServerVersion}) `
    + `refuses a ${vendor.id} row on Linux and WSL, and ${ON_LINUX_SINCE[vendor.runtime]} is the first release that runs it (on WSL2) — the MCP server tab.`;
}

/** The name a person knows a runtime by, where the runtime's own word reads wrong in a sentence. */
const RUNTIME_LABEL: Readonly<Record<string, string>> = { vibe: 'Vibe' };

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
