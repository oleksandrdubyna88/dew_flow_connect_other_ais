/**
 * The words that mean a string is carrying a credential.
 *
 * <p>Extracted from `consultantWrite.ts`, where it was a private const, when the notifications
 * ledger needed the same judgement. Copying it would have been ten lines and two places for the
 * same list to drift — which is what the reuse rule is about, and the drift would have been
 * invisible: two redactors disagreeing about whether `sig` names a credential is not a test
 * failure anywhere, it is a secret in a file on one path and not the other.</p>
 *
 * <p>The two callers ask DIFFERENT questions of it and that is deliberate. `consultantWrite`
 * asks "does this URL carry a key, so I can REFUSE to write it into settings.json"; the ledger
 * asks "does this parameter carry a key, so I can REDACT it before it reaches a file". Refusing
 * is right where a person can retype the URL without the key; redacting is right where the text
 * is an exception message nobody chose. Neither is the other's answer, and the list is what they
 * share.</p>
 *
 * <p>It stays a list of WORDS rather than "anything in a query string": an endpoint that names an
 * API version or a deployment is ordinary — `?api-version=2024-02-01` is how Azure spells one —
 * and treating those as secrets teaches people to ignore the mechanism.</p>
 */
export const CREDENTIAL_WORDS: readonly string[] = [
  'key', 'token', 'secret', 'password', 'passwd', 'authorization', 'auth', 'credential', 'sig', 'signature',
];

/** Whether a parameter name reads as a credential. Case-insensitive, substring, deliberately broad. */
export function namesACredential(name: string): boolean {
  const lower = name.toLowerCase();

  return CREDENTIAL_WORDS.some((word) => lower.includes(word));
}
