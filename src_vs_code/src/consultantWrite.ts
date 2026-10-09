/**
 * The checks a custom endpoint's two boxes run — its name and its base URL — before anything is written.
 *
 * <p>*Add a model* asks for both (`panelProvider.askCustomEndpoint`). The boxes are `vscode.window.showInputBox`, so
 * what they MEAN lives here, where a test can call it: a name that keys no vault entry, a name already spoken for at a
 * different endpoint, and a URL that is not one or carries a key.</p>
 *
 * <p>This module was the Consultant section's write path until the Settings page replaced each caller's own
 * definition with a pick of a catalog row (research/PLAN_one_model_catalog.md E5.3); a pick is written by
 * `consultantPicks.ts`.</p>
 */

import { Vendor, VENDOR_PRESETS, normaliseId } from './vendors';
import { namesACredential } from './credentialWords';
import { consultantChoiceFrom } from './consultSettings';

/**
 * What two boxes MEAN once they are closed — and what a dismissal of either means, which is nothing.
 *
 * <p>Its own function because it is the only part of the flow that can be tested: the boxes
 * themselves are `vscode.window.showInputBox`, and a host is not something this suite has. A
 * dismissed box is `undefined` and a name that normalises to nothing keys no vault entry, so both
 * answer "no endpoint" and the caller writes nothing at all. The second box is the one worth naming:
 * a person who typed a name and then changed their mind has given no more consent than one who
 * closed the first, and an endpoint minted from a name alone would have no URL to reach.
 * (local, C6's plan round.)</p>
 */
export function endpointAnswer(
  name: string | undefined,
  baseUrl: string | undefined,
): { readonly id: string; readonly baseUrl: string } | undefined {
  const id = normaliseId(name ?? '');
  const where = (baseUrl ?? '').trim();

  return name === undefined || baseUrl === undefined || id.length === 0 || where.length === 0
    ? undefined
    : { id, baseUrl: where };
}

/**
 * Whether a name is already spoken for at a DIFFERENT endpoint — said while it is being typed.
 *
 * <p>One id is one vault key and one credential, so two endpoints under one name would send a key to
 * whichever of them answered. The check spans the reviewer ROWS, the catalogue and the callers' consultants,
 * because all three key the vault the same way; the same URL under the same name is not a conflict at all,
 * it is the same service named once. Returns the sentence to show, or empty for "go ahead" — a
 * validator's shape, so `showInputBox` can refuse while the box is open rather than after it closes.
 * (gemini, C6's plan round.)</p>
 */
export function endpointConflict(
  name: string,
  baseUrl: string,
  vendors: readonly Vendor[],
  consultants: Readonly<Record<string, unknown>>,
): string {
  const id = normaliseId(name);
  const wanted = baseUrl.trim();
  const clash = holders(vendors, consultants)
    .find((one) => one.id.toLowerCase() === id.toLowerCase() && one.baseUrl !== wanted);

  if (clash === undefined) {
    return '';
  }

  // Both halves named, because a holder can have no endpoint of its own — `claude` is a catalogue
  // preset and a vault key with no URL — and the sentence then has neither a place to name nor an
  // endpoint to offer. Inline this was a ternary inside a template literal inside a ternary, which
  // SonarCloud flagged three times over and was right to.
  const where = clash.baseUrl.length > 0 ? ` at ${clash.baseUrl}` : '';
  const orUseIt = clash.baseUrl.length > 0 ? ' or use that endpoint' : '';

  return `'${id}' is already ${clash.what}${where}`
    + ` — one name is one key in the vault, so pick another name${orUseIt}`;
}

/**
 * Why a typed endpoint is not one — or `undefined` when it is fine to go on.
 *
 * <p>`startsWith('http')` was the whole check, and it accepts `http-not-a-url`: the box closes, the
 * setting is stored, and the person learns it is wrong the next time they are stuck and a
 * consultation fails. Parsing is the only way to know, and `URL` is the parser both halves of this
 * product already trust. (codex, C6's code round.)</p>
 *
 * <p><b>A credential in the URL is refused rather than stored.</b> `https://user:token@host/v1` and
 * `?api_key=…` both work against many gateways, and both end up in `settings.json` — which for a
 * WORKSPACE setting is a file people commit. This product keeps keys in one CredsForDevs entry
 * precisely so they are never in argv, a log line or a settings file, and an endpoint box is not the
 * place to make an exception. The vault entry under this name is where the key goes. (codex, C6's
 * code round; I took the refusal and not the redaction — the URL a conflict names is one already in
 * this person's own settings, shown back to that same person.)</p>
 */
export function badEndpoint(typed: string): string | undefined {
  const text = typed.trim();
  let parsed: URL;
  try {
    parsed = new URL(text);
  } catch {
    return 'A base URL is needed — something like https://api.example.com/v1';
  }

  if (!['http:', 'https:'].includes(parsed.protocol) || parsed.hostname.length === 0) {
    return 'The endpoint has to be an http or https address';
  }

  return parsed.username.length > 0 || parsed.password.length > 0 || carriesAKey(parsed)
    ? 'Leave the key out of the URL — it goes in the vault entry under this name, which is what keeps it out of settings.json'
    : undefined;
}

/**
 * Whether a URL carries a credential in its query or its fragment.
 *
 * <p>The fragment is read for the same reason the query is. A `#` never reaches the server, so it is
 * not a leak to the vendor — but this string is about to be written into `settings.json`, and a
 * workspace settings file is a file people commit, which is the whole point of refusing it.</p>
 */
function carriesAKey(parsed: URL): boolean {
  const named = [
    ...parsed.searchParams.keys(),
    ...new URLSearchParams(parsed.hash.replace(/^#/, '')).keys(),
  ];

  // `credentialWords.ts`, not a list here. This one WAS the list, private to this module, until the
  // notifications ledger needed the same judgement — and the first attempt at sharing it added a
  // second copy beside this one instead of moving it, which the code round caught. A marker added to
  // one list and not the other would make settings validation and ledger redaction disagree about
  // the same string: refused in one path, written to a file in the other.
  return named.some(namesACredential);
}

/**
 * Everything that already means something by a name.
 *
 * <p>Three holders, because all three key the vault the same way. The reviewer ROWS. The CATALOGUE,
 * which is the one no reviewer row need exist for: with no `deepseek` row configured a person could
 * name their own endpoint `deepseek`, and the next caller to pick DeepSeek out of the list would
 * share a vault key with a different service (codex, C6's code round). And the callers' consultants,
 * whose stored definitions name an endpoint under an id.</p>
 *
 * <p>A holder with no endpoint of its own still holds the NAME — `claude` is a catalogue preset and
 * a vault key — so it clashes, and the sentence then stops short of telling anyone to use an
 * endpoint there is none of.</p>
 *
 * <p>A consultant holds an endpoint only when its entry DEFINES one. A caller's pick is a bare reference to a catalog
 * row (`{ vendor: '<row id>' }`, E4.2) whose endpoint is the row's — already a holder above, at its real URL; read
 * as a holder of its own it had none, and the row's own URL was refused as a clash (E5.3's code round).</p>
 */
function holders(
  vendors: readonly Vendor[],
  consultants: Readonly<Record<string, unknown>>,
): readonly { readonly id: string; readonly baseUrl: string; readonly what: string }[] {
  return [
    ...vendors.map((one) => ({ id: one.id, baseUrl: one.baseUrl, what: 'a reviewer' })),
    ...VENDOR_PRESETS.filter((one) => one.id.length > 0)
      .map((one) => ({ id: one.id, baseUrl: one.baseUrl, what: `what this build calls ${one.label}` })),
    ...Object.values(consultants)
      .map((one) => consultantChoiceFrom(one))
      .filter((one) => one.baseUrl.length > 0)
      .map((one) => ({ id: one.vendor, baseUrl: one.baseUrl, what: "another caller's consultant" })),
  ];
}
