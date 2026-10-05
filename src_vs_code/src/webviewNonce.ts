import { randomBytes } from 'node:crypto';

/**
 * The nonce a webview's content security policy admits its one script by — for every panel this extension opens
 * (todo/PLAN_one_model_catalog.md, E3.1).
 *
 * <p><b>128 bits from the operating system's generator</b>, never `Math.random()`: the nonce is the whole of the policy,
 * and a predictable one is a policy an injected script can satisfy. <b>base64url</b>, so it is exactly what CSP's
 * nonce-source admits and carries no quote, space or semicolon that could end the policy string it is written into.</p>
 *
 * <p>One helper rather than one per panel: ten copies had grown in four encodings, and the one that was wrong — the
 * Settings panel's — stayed wrong while a copy of it was refused by a scanner. A test fails when a file makes its own.</p>
 */
export function webviewNonce(): string {
  return randomBytes(16).toString('base64url');
}
