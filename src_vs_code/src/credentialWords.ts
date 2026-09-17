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
 * Words that mean a credential wherever they appear inside a name.
 *
 * <p>Long enough that a substring match is not an accident. `client_secret`, `refresh_token` and a
 * run-together `myapikey` are all caught by one of these without anything having to know how the
 * name was spelled.</p>
 */
const ANYWHERE: readonly string[] = [
  'token', 'secret', 'password', 'passwd', 'authorization', 'credential', 'signature', 'apikey',
  'accesskey', 'privatekey',
];

/**
 * Words that mean a credential only when they are a WHOLE part of the name.
 *
 * <p>Three words short enough to appear inside ordinary ones, and matching them as substrings was a
 * real defect: `author`, `assignee`, `design`, `signal` and `monkey` all matched, so
 * `consultantWrite` REFUSED legitimate endpoint URLs and the ledger redacted diagnostic parameters
 * that carried nothing. A redaction that fires on `?author=octocat` teaches people that the
 * mechanism is noise, which is how a real one gets ignored. (gemini, the code round.)</p>
 *
 * <p>A "part" is what survives splitting on separators and camelCase, so `api_key`, `apiKey`,
 * `X-Api-Key` and a bare `key` all match while `monkey` does not. The cost is that a run-together
 * lowercase `apikey` is not reached by `key` alone — which is why it is spelled out in the list
 * above rather than left to a rule that cannot see the word boundary that is not there.</p>
 */
const WHOLE_PART: readonly string[] = ['key', 'auth', 'sig'];

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
