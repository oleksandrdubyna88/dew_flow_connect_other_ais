import type { Vendor } from './vendors';

/**
 * The name a row's key is filed under in the vault: the one it names, else its id — this side's twin of
 * the server's `ProviderSettings.KeyName`.
 *
 * <p>Its own module rather than a private helper of the vault-key wizard (`apiKeyVendors.ts`), because every
 * runtime that reads a vault key asks it — an `api` row, a codex row on another endpoint, the card's ≡ — and a
 * general caller should not depend on the wizard to learn a row's key name (code round, gemini). Not in
 * `vendors.ts`, which sits at the repository's 800-line cap.</p>
 */
export function vaultKeyOf(vendor: Vendor): string {
  return vendor.vaultKeyName ?? vendor.id;
}
