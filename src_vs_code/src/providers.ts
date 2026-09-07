/**
 * What the SERVER makes of the reviewers this panel has configured.
 *
 * <p>The pure half. `providersProbe.ts` spawns the binary; this holds the types and the reading of
 * its answer, because `panelView` needs those and a spawn in the same module would drag
 * `node:child_process` into the bundle the webview page is built from — which is what the bundled-page
 * test caught, with `require is not defined`, the first time `roundsDb` was written as one file.</p>
 *
 * <p>Read from `coai-mcp --providers`, the same way the rounds log is read from `--log`, and for the
 * same reason: the panel owns none of these decisions. Whether a vendor can review is answered by
 * `RuntimeResolution.AuthOf`, once, for both binaries — this repository has twice shipped a defect
 * that was a second copy of that decision disagreeing with the first, and the second copy is always
 * the one that looks harmless.</p>
 *
 * <p>So the panel asks and displays. It does not decide.</p>
 */

/** One vendor, as the server reports it. Only the fields a badge needs. */
export interface ProviderHealth {
  readonly provider: string;
  readonly auth: string;
  readonly note: string;
}

/**
 * The three states a card can be in, and the third is the one worth having.
 *
 * <p>`unavailable` badges. `fine` does not. **`unknown` does not either** — a probe that failed,
 * timed out or found no binary tells you nothing about a reviewer, and a badge that lights up
 * because a probe failed is a badge that lies. The ⤓ buttons already refuse to guess for exactly
 * this reason.</p>
 *
 * <p>There is a second reason for `unknown` here specifically. An MCP client's `env` block outranks
 * the settings file key by key, so a standalone invocation cannot see environment a scripted or
 * containerised client passed to the running server — and would then report an availability the live
 * server does not have. Anything this cannot establish stays silent.</p>
 */
export type Availability = 'fine' | 'unavailable' | 'unknown';

/**
 * What one probe produced, and whether it produced anything.
 *
 * <p>`answered: false` is not "everything is fine" and it is not "this reviewer is broken" — it is
 * that the installed binary was asked and could not say. The CARD stays silent about a reviewer it
 * knows nothing about, and the Server section says the check itself failed. Four reviewers on this
 * epic's plan round raised the same point: silence about a failed CHECK is the class of defect this
 * whole plan is about.</p>
 */
export interface ProvidersAnswer {
  readonly reported: Record<string, ProviderHealth>;
  /** A binary existed and was run. False means nothing was asked — which the Server section says. */
  readonly asked: boolean;
  readonly answered: boolean;
}

/** The shape the server answers with, parsed defensively. */
export function parseProviders(output: string): Record<string, ProviderHealth> {
  try {
    const parsed: unknown = JSON.parse(output);
    const rows = (parsed as { providers?: unknown })?.providers;
    if (!Array.isArray(rows)) {
      return {};
    }

    const found: Record<string, ProviderHealth> = {};
    for (const row of rows) {
      const health = oneProvider(row);
      if (health !== undefined) {
        found[health.provider] = health;
      }
    }

    return found;
  } catch {
    return {};
  }
}

function oneProvider(row: unknown): ProviderHealth | undefined {
  if (typeof row !== 'object' || row === null) {
    return undefined;
  }
  const { provider, auth, note } = row as Record<string, unknown>;

  return typeof provider === 'string' && provider.length > 0
    ? {
      provider,
      auth: typeof auth === 'string' ? auth : '',
      note: typeof note === 'string' ? note : '',
    }
    : undefined;
}

/**
 * What to show for one vendor.
 *
 * <p>A row the server did not mention is `unknown`, not `fine`: it means this panel and that binary
 * disagree about what is configured, which is a thing to stay quiet about rather than to reassure
 * somebody over.</p>
 */
export function availabilityOf(
  id: string,
  reported: Readonly<Record<string, ProviderHealth>>,
): Availability {
  const health = reported[id];
  if (health === undefined || health.auth.length === 0) {
    return 'unknown';
  }

  return health.auth === 'unavailable' ? 'unavailable' : 'fine';
}
