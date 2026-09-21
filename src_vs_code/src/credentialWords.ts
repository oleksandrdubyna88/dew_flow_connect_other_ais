/**
 * The words that mean a string is carrying a credential.
 *
 * <p>This is the ONE list. It was a private const inside `consultantWrite.ts` until the
 * notifications ledger needed the same judgement — and the first attempt at sharing it left the
 * original in place, so for one commit there were two lists and this file's own docstring claimed
 * there was one. The code round found it. Two redactors disagreeing about whether `sig` names a
 * credential is not a test failure anywhere; it is a secret in a file on one path and not the
 * other.</p>
 *
 * <p>The two callers ask DIFFERENT questions of it and that is deliberate. `consultantWrite` asks
 * "does this URL carry a key, so I can REFUSE to write it into settings.json"; the ledger asks
 * "does this parameter carry a key, so I can REDACT it before it reaches a file". Refusing is right
 * where a person can retype the URL without the key; redacting is right where the text is an
 * exception message nobody chose. Neither is the other's answer, and the list is what they share.</p>
 *
 * <p>It stays a list of WORDS rather than "anything in a query string": an endpoint that names an
 * API version or a deployment is ordinary — `?api-version=2024-02-01` is how Azure spells one — and
 * treating those as secrets teaches people to ignore the mechanism.</p>
 */

/**
 * The two lists, from `shared/credential-words.json` by way of the generator.
 *
 * <p><b>They moved out of this file on 2026-09-21, and not for tidiness.</b> The SERVER redacts the
 * same way before it writes `server-notices.jsonl`, and the words the two halves redact on have to
 * be the same words — two redactors disagreeing about whether `sig` names a credential is not a
 * test failure anywhere, it is a secret in a file on one path and not the other. `coai-mcp` embeds
 * that JSON as a manifest resource and this side generates a module from it, exactly as the role
 * catalog is already handled.</p>
 *
 * <p><b>Neither half reads `shared/` at run time</b>, which is the shape a plan round corrected: a
 * published Native-AOT binary and an installed VSIX both run where no such directory exists, and a
 * redactor falling back to an empty list there would write raw credentials while every test that
 * only checks the two sides AGREE stayed green.</p>
 *
 * <p>What each list means is unchanged. `ANYWHERE` is long enough that a substring match is not an
 * accident — `client_secret`, `refresh_token` and a run-together `myapikey` are all caught without
 * anything knowing how the name was spelled. `WHOLE_PART` holds three words short enough to appear
 * inside ordinary ones, and matching THEM as substrings was a real defect: `author`, `assignee`,
 * `design`, `signal` and `monkey` all matched, so `consultantWrite` refused legitimate endpoint
 * URLs and the ledger redacted diagnostic parameters that carried nothing. (gemini, the code
 * round.) A "part" is what survives splitting on separators and camelCase, so `api_key`, `apiKey`,
 * `X-Api-Key` and a bare `key` all match while `monkey` does not; the cost is that a run-together
 * `apikey` is spelled out in the long list rather than reached by a rule that cannot see a word
 * boundary that is not there.</p>
 */
import { ANYWHERE, WHOLE_PART } from './credentialWords.generated';

/** Every word, for a caller that wants to say what it looks for. */
export const CREDENTIAL_WORDS: readonly string[] = [...ANYWHERE, ...WHOLE_PART];

/**
 * A name split the way a person reads it: separators and camelCase, lowercased.
 *
 * <p>Every repetition is bounded. This runs over text nobody chose — a parameter name lifted out of
 * a vendor's stderr — and a pattern that can backtrack over attacker-shaped input is a frozen
 * extension host rather than a slow function.</p>
 */
function partsOf(name: string): readonly string[] {
  return name
    .replace(/([a-z0-9])([A-Z])/gu, '$1 $2')
    .toLowerCase()
    .split(/[^a-z0-9]{1,64}/u)
    .filter((part) => part.length > 0);
}

/** Whether a parameter name reads as a credential. Case-insensitive, and deliberately broad. */
export function namesACredential(name: string): boolean {
  if (ANYWHERE.some((word) => name.toLowerCase().includes(word))) {
    return true;
  }
  const parts = partsOf(name);

  return WHOLE_PART.some((word) => parts.includes(word));
}
