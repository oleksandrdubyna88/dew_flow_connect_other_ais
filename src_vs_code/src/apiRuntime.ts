/**
 * The `api` runtime on the panel's side (PLAN_feature_review.md, story S1.2): a hosted
 * OpenAI-compatible endpoint reached directly by `coai-mcp --ask-api`, with a key from the vault
 * and no CLI in between.
 *
 * <p>Pure and `vscode`-free, so the version gate and the dialect list are unit tests rather than a
 * paint. `vendors.ts` imports from here; this file imports only a TYPE from it, so there is no cycle
 * at run time.</p>
 */

import { compareVersions } from './coaiInstall';

/**
 * The three things this file asks of a vendor row — a structural type rather than `Vendor`, because
 * `vendors.ts` imports from here and an import back would be a cycle (`importCycles.test.mjs` froze the
 * count this repository had, and a new one bundles and fails at run time).
 */
export interface ApiRow {
  readonly id: string;
  readonly runtime: string;
  readonly enabled: boolean;
}

/**
 * The first `coai-mcp` that knows `runtime: "api"`.
 *
 * <p><b>The unsafe direction is the SILENT one.</b> An older server's `RuntimeOf` turns a runtime it
 * does not know into `codex` and keeps the base URL — so a Grok row would ride the Codex CLI against
 * xAI's endpoint, under its own name, with codex's 21k-token system prompt in front of the review
 * (§4.13). Set too LOW this stays quiet on exactly that server. It is the release S1.2 ships in: the
 * feature after `mcp-v0.36.0`, which a sibling epic released on 2026-09-25 WITHOUT the runtime — this
 * constant first said 0.36.0 and would have trusted that server.</p>
 */
export const API_RUNTIME_SINCE = '0.37.0';

/**
 * Every dialect `shared/api-dialects.json` carries — the NAMES, mirrored; the values live on the
 * server side only, because the request is spelled there. `apiRuntime.test.ts` holds this list to
 * the file, as `CredentialWords` and `command-models.json` are held on both sides.
 */
export const API_DIALECTS = ['local', 'openai'] as const;

export type ApiDialect = (typeof API_DIALECTS)[number];

/** What an `api` row speaks when it names nothing: the generic hosted dialect. */
export const DEFAULT_API_DIALECT: ApiDialect = 'openai';

/**
 * The dialects an `api` row may pick. `local` is a row of the same file but it is the LOCAL
 * reviewer's body, measured against Ollama — offering it to a hosted endpoint would send a frequency
 * penalty and a bounded schema to a model documented to refuse both.
 */
export function dialectChoices(): readonly ApiDialect[] {
  return API_DIALECTS.filter((dialect) => dialect !== 'local');
}

/**
 * Whether the installed server reads `runtime: "api"` — true for an UNKNOWN version too.
 *
 * <p>Unknown is not old: every other skew gate here stays silent on a server it cannot version, and
 * suppressing rows on a guess would drop a configured reviewer from a server that is perfectly able
 * to run it. The one case that is gated is the one that is KNOWN to be older.</p>
 */
export function apiRuntimeOnServer(installedServerVersion: string): boolean {
  return installedServerVersion.length === 0 || compareVersions(API_RUNTIME_SINCE, installedServerVersion) <= 0;
}

/**
 * The sentence beside an `api` row while the installed server would run it as the wrong vendor — or
 * nothing when there is no such row, or the server is new enough, or unknown.
 */
export function apiRuntimeSkewNote(installedServerVersion: string, vendors: readonly ApiRow[]): string {
  const rows = vendors.filter((vendor) => vendor.runtime === 'api' && vendor.enabled);
  if (rows.length === 0 || apiRuntimeOnServer(installedServerVersion)) {
    return '';
  }

  const names = rows.map((vendor) => vendor.id).join(', ');
  const { its, kept } = grammar(rows.length);

  return `The coai-mcp you have installed (${installedServerVersion}) does not know the api runtime: it would run `
    + `${names} through the Codex CLI against ${its} endpoint. ${kept} kept out of the settings file until you `
    + `update it to ${API_RUNTIME_SINCE} or later — the MCP server section below.`;
}

/** The two words that change with the count, so the sentence above reads as one sentence. */
function grammar(rows: number): { its: string; kept: string } {
  return rows === 1 ? { its: 'its', kept: 'This row is' } : { its: 'their', kept: 'These rows are' };
}
