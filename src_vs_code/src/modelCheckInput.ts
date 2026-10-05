import { PRICE_BOOK } from './priceBook';
import type { Vendor } from './vendors';
import { vendorsEnv } from './vendorsWire';

/**
 * One catalog row as `coai-mcp --check-model` reads it on stdin — `{"row": …}` — through the SAME wire the settings
 * file is written with (`vendorsEnv`), so the row checked is the row a round would run (PLAN_one_model_catalog.md D10).
 * Switched on for the check: a switched-off row is checked as it would run once switched on. `{}` when this server
 * would not be handed the row at all (an api row for a server too old for it) — which the binary refuses by name.
 */
export function checkInputOf(row: Vendor, serverVersion: string, features: readonly string[]): string {
  const wire = JSON.parse(vendorsEnv([{ ...row, enabled: true }], serverVersion, (one) => PRICE_BOOK.priceOf(one.model, one.baseUrl), features)) as unknown[];

  return wire.length === 0 ? '{}' : JSON.stringify({ row: wire[0] });
}
