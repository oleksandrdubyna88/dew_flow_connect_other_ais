/**
 * The `COAI_CONSULTANTS` key: which consultant answers each kind of caller, as the server's `ConsultantDto` reads it.
 *
 * <p>Split out of `settingsShape.ts`, whose `envBlock` writes every other key: this is one cohesive rule with its own reasons —
 * what crosses, per caller, and in which shape — and it kept the file past the lint's line limit.</p>
 */

import { FEATURES } from './binaryFeatures';
import { type CatalogRowOnTheWire } from './qconsultSettings';
import { CALLER_KINDS, ConsultantChoice, ConsultSettings, DEFAULT_CONSULT, ResolvedConsultant, sameChoice } from './consultSettings';
import { hostExecutableSide } from './hostSide';
import { executableHere } from './pathFamily';
import { Vendor } from './vendors';
import { RowPriceLookup, rowOnTheWire } from './vendorsWire';

/**
 * `COAI_CONSULTANTS` for the callers whose entry differs from the shipped pair — nothing at all for a pristine panel.
 *
 * The consultant's four, each only when it differs — so a pristine panel sends NOTHING and the
 * server's own fallback is what runs. That is what makes the two default sets one contract rather
 * than two numbers that happen to agree today; the gate's defaults diverged for a day once, and a
 * new install read one number off the screen while another one ran.
 *
 * Decided on the STORED map, never the resolved one. `consultSettingsFrom` resolves a legacy entry
 * on read, so a pristine map comes back as four definitions carrying the reviewer rows' values —
 * compared with the shipped pairs those are "different", and every untouched install would write
 * this key while the server, with no key, resolves the same absence to the same consultant.
 *
 * What CROSSES is the resolved entry — since story B4 of PLAN_the_consultant_has_its_own_vendors
 * measured a definition against the last released server (mcp-v0.22.0, 2026-09-15). That server's
 * DTO is `(Vendor, Model)`: `System.Text.Json` skips the three members it does not declare, and it
 * consulted through the reviewer row with the same id — that row's runtime, endpoint and CLI path,
 * with the entry's model — exactly as it did for the legacy pair, and refused a definition whose id
 * had no row as "not configured". The definition's own three fields are dropped without a word, so
 * `consultantSkewNote` says so in the section while such a server is installed, and
 * `research/module_server.md` holds the replies verbatim.
 *
 * PER CALLER: only an entry that differs from its shipped pair travels. Every caller used to
 * travel as a legacy pair whenever one differed, which cost nothing — the server resolved a shipped
 * pair to what it would have chosen with no key. A resolved DEFINITION is not that: it would
 * freeze this panel's reading of a caller nobody configured, so an untouched caller stays off the
 * wire and the server resolves its own absence, which `ConsultantRouting.For` does for a kind the
 * map lacks. A map with nothing to say emits no key, as before.
 */
export function consultantsEnv(
  consult: ConsultSettings,
  vendors: readonly Vendor[],
  installedServerVersion: string,
  priceOf: RowPriceLookup,
  features: readonly string[],
): Record<string, string> {
  const crossing = callersOnTheWire(consult);
  if (crossing.length === 0) {
    return {};
  }
  // One JSON key rather than four scalars, for the reason `COAI_VENDORS` already carries: a compound value needs a structured
  // encoding, and four key spellings is four chances for the two halves to disagree about one of them.
  const rowOf = consultantRowOf(catalogRowOnTheWire(vendors, installedServerVersion, priceOf, features));

  return {
    COAI_CONSULTANTS: JSON.stringify(Object.fromEntries(
      crossing.map((id) => [id, wireEntry(consult.byCaller[id]!, rowOf(consult.stored[id]))]))),
  };
}

/**
 * The callers whose STORED entry differs from the shipped pair — the ones `COAI_CONSULTANTS` carries.
 *
 * <p>Per caller through {@link sameChoice}, the same comparison `sameCallers` makes for the whole map,
 * so "differs" means one thing on both sides of the key. A stored DEFINITION always differs — its
 * runtime is set and a shipped pair's is not — so a caller a person has edited always travels.</p>
 */
function callersOnTheWire(consult: ConsultSettings): readonly string[] {
  return CALLER_KINDS
    .filter(({ id }) => !sameChoice(consult.stored[id], DEFAULT_CONSULT.stored[id]))
    .map(({ id }) => id);
}

/**
 * One caller's entry as the server's `ConsultantDto` reads it — a definition whole, an unavailable
 * entry raw.
 *
 * <p>In the DTO's DECLARED order, legacy pair first: `panelServerDefaultsAgreement` reads that order
 * off the C# and holds this level with it, and a reader older than the three new fields meets the two
 * it knows where it always met them. All five are written for a definition, empty strings included —
 * this is the wire, not the file a person edits (`consultantWrite.ts` drops empties there), and `vendorsEnv`
 * writes `COAI_VENDORS` the same way. An UNAVAILABLE entry travels as `{vendor, model}` with NO
 * runtime, so the server's rule (c) refuses it by name: a runtime invented here would be a definition
 * the server builds a provider for, sending a working tree to a vendor nobody chose.</p>
 */
function wireEntry(one: ResolvedConsultant, row: Readonly<Record<string, unknown>> | undefined): Record<string, unknown> {
  return one.kind === 'unavailable'
    ? { vendor: one.vendor, model: one.model }
    // The CLI path this SIDE runs: a legacy definition's path spelled for the other OS crosses empty (paths per side, E1.2).
    : { vendor: one.vendor, model: one.model, runtime: one.runtime, baseUrl: one.baseUrl, executablePath: executableHere(one.executablePath, hostExecutableSide()), ...(row === undefined ? {} : { row }) };
}

/**
 * The catalog row a stored reference names, as `COAI_VENDORS` would write it — the consultant's effort, system prompt,
 * timeout and key travel with it (research/PLAN_one_model_catalog.md, C2). Only to a binary that lists `consultantRow`: an
 * older one skips an unknown member without a word, so the person would believe settings applied that were not. Found
 * by the rule `resolveConsultant` uses for a reference — the row with that id, switched on or off — and written switched
 * on, as `--check-model` is handed it, because a consultant-only row reviews nothing.
 */
function consultantRowOf(rowOf: CatalogRowOnTheWire): (choice: ConsultantChoice | undefined) => Readonly<Record<string, unknown>> | undefined {
  return (choice) => (choice?.runtime === '' ? rowOf(choice.vendor) : undefined);
}

/**
 * A catalog row by its id (any case), as `COAI_VENDORS` writes it — switched on, as `--check-model` is handed it,
 * because a consultant-only row reviews nothing. Nothing at all for a binary that does not list `consultantRow`.
 */
export function catalogRowOnTheWire(
  vendors: readonly Vendor[], installedServerVersion: string, priceOf: RowPriceLookup, features: readonly string[],
): CatalogRowOnTheWire {
  if (!features.includes(FEATURES.consultantRow)) {
    return () => undefined;
  }

  return (catalogId) => {
    const row = vendors.find((one) => one.id.toLowerCase() === catalogId.toLowerCase());

    return row === undefined ? undefined : rowOnTheWire({ ...row, enabled: true }, installedServerVersion, priceOf, features);
  };
}
