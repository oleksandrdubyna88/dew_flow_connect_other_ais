import { closeSync, fstatSync, openSync, readSync } from 'node:fs';
import { join } from 'node:path';

/**
 * The sidebar's LAST SNAPSHOT (v2 epoch 1, E4 story s3-sidebar): the page the old code last painted, kept on disk so the next window can
 * show it before the old code has loaded.
 *
 * <p><b>State since 2026-11-23:</b> the sidebar module no longer replays this page. The owner's cold-start rule is that the start ends when the REAL menu is
 * shown and clickable (`sidebarMenu.ts`), and a replayed copy of an old page is `inert` by construction. The format, the reader and the writer
 * (`sidebarSnapshotStore.ts`) stay, tested on their own, until the owner decides whether the file goes. This file imports only `node:fs` and `node:path`.</p>
 *
 * <p><b>What is stored.</b> The sidebar's html, built with a PLACEHOLDER where its nonce goes, as one small JSON file in the extension's global
 * storage: `{ v, version, html }`. The nonce is the whole of a webview's content security policy, so a stored page never holds a real one: every
 * replay is given a fresh nonce ({@link snapshotPage}), and a file whose placeholder is not in exactly the two places the page writes its nonce
 * is refused as damaged. The extension's version is part of the file, so a page written by one build is never replayed by another.</p>
 *
 * <p><b>What may be stored.</b> The sidebar draws notifications, the active gates and consultations, the cadence lines, the phrases and the
 * Bugz card (checked 2026-11-22: no key, token or password is drawn — the contributor and admin keys live in the editor's secret store and the
 * Bugz card draws the ingest server's URL only). Text a reviewer wrote is still text, so {@link snapshotText} also REFUSES a page that looks
 * like it holds a credential, and the sidebar then opens on the skeleton instead. A page over {@link SNAPSHOT_MAX_BYTES} is refused the same way.</p>
 */

/** The file, inside the extension's global storage directory. */
export const SNAPSHOT_FILE = 'sidebar-snapshot.json';

/** The most html a snapshot may hold: a bigger page is not stored, and the sidebar opens on the skeleton. */
export const SNAPSHOT_MAX_BYTES = 256 * 1024;

/** What stands for the nonce in a stored page. It carries no character a CSP or an attribute treats specially. */
export const NONCE_PLACEHOLDER = '__COAI_SNAPSHOT_NONCE__';

const FORMAT = 1;

/** The extension's version, or `''` when the host does not say: with no version there is no snapshot, written or read. */
export function extensionVersionOf(context: { readonly extension?: { readonly packageJSON?: { readonly version?: unknown } } }): string {
  const version = context.extension?.packageJSON?.version;

  return typeof version === 'string' ? version : '';
}

/**
 * Patterns of text that must never be written into a file this feature makes. Deliberately wide: a refused page costs one skeleton, a stored
 * credential costs more. Applied to the page's BODY (the part a person's data can reach), not to the page's own script and stylesheet.
 */
const SECRET_LIKE: readonly RegExp[] = [
  /-----BEGIN [A-Z ]*PRIVATE KEY-----/u,
  /\b(?:sk|pk|rk)-[A-Za-z0-9_-]{16,}/u,
  /\bAKIA[0-9A-Z]{16}\b/u,
  /\bgh[pousr]_[A-Za-z0-9]{20,}/u,
  /\bxox[abprs]-[A-Za-z0-9-]{10,}/u,
  /\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\./u,
  /:\/\/[^/\s:@]+:[^/\s@]+@/u,
  /\b(?:password|passwd|secret|api[_-]?key|access[_-]?token|auth[_-]?token|authorization)\b\s*[:=]\s*[^\s<]{6,}/iu,
  /\bBearer\s+[A-Za-z0-9._~+/=-]{16,}/u,
];

/** The part of a page a person's data can reach: everything before the page's own script. */
function bodyOf(html: string): string {
  const script = html.indexOf('<script nonce=');

  return script < 0 ? html : html.slice(0, script);
}

/** True when the page's body looks like it holds a credential. */
export function looksSecret(html: string): boolean {
  const body = bodyOf(html);

  return SECRET_LIKE.some((pattern) => pattern.test(body));
}

/** How often the placeholder appears: a stored page has it exactly twice, in its policy and on its script tag. */
function placeholders(html: string): number {
  return html.split(NONCE_PLACEHOLDER).length - 1;
}

/** What the file holds, as parsed: nothing is trusted until {@link currentHtml} has looked. */
interface Stored { readonly v?: unknown; readonly version?: unknown; readonly html?: unknown }

/** A page this feature can replay: the placeholder in exactly two places and a `<body>` to mark. */
function replayable(html: string): boolean {
  return placeholders(html) === 2 && html.includes('<body>') && html.includes(`nonce-${NONCE_PLACEHOLDER}`) && html.includes(`<script nonce="${NONCE_PLACEHOLDER}">`);
}

/** A page that may be kept: a version to stamp it with, under the cap, the shape of a replayable page, and nothing credential-like in it. */
function keepable(version: string, html: string): boolean {
  return version !== '' && html.length <= SNAPSHOT_MAX_BYTES && replayable(html) && !looksSecret(html);
}

/**
 * The file's text for a page built with {@link NONCE_PLACEHOLDER}, or `undefined` when the page may not be kept: too big, credential-like, or
 * not the shape of a page this feature can replay.
 */
export function snapshotText(version: string, html: string): string | undefined {
  const text = keepable(version, html) ? JSON.stringify({ v: FORMAT, version, html }) : undefined;

  return text !== undefined && Buffer.byteLength(text, 'utf8') <= SNAPSHOT_MAX_BYTES + 1024 ? text : undefined;
}

const isStored = (value: unknown): value is Stored => typeof value === 'object' && value !== null;
const sameBuild = (stored: Stored, version: string): boolean => stored.v === FORMAT && stored.version === version;
const htmlOf = (stored: Stored): string | undefined => (typeof stored.html === 'string' ? stored.html : undefined);

/** The page a file's parsed content holds, when this build wrote it. */
function currentHtml(parsed: unknown, version: string): string | undefined {
  return isStored(parsed) && sameBuild(parsed, version) ? htmlOf(parsed) : undefined;
}

function parsedOf(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return undefined;
  }
}

/** The stored page in a file's text, or `undefined` for anything that is not one this build wrote and may replay. */
export function snapshotFrom(text: string, version: string): string | undefined {
  const html = currentHtml(parsedOf(text), version);

  return html !== undefined && keepable(version, html) ? html : undefined;
}

function closeQuietly(handle: number | undefined): void {
  if (handle === undefined) {
    return;
  }
  try {
    closeSync(handle);
  } catch {
    // A handle that will not close changes nothing for the person.
  }
}

/** A file's text, unless it is empty, over the cap, or unreadable. Synchronous, because it runs while the view is being resolved and nothing may wait. */
function readCapped(path: string): string | undefined {
  let handle: number | undefined;
  try {
    handle = openSync(path, 'r');
    const size = fstatSync(handle).size;
    if (size === 0 || size > SNAPSHOT_MAX_BYTES + 1024) {
      return undefined;
    }
    const buffer = Buffer.alloc(size);
    readSync(handle, buffer, 0, size, 0);

    return buffer.toString('utf8');
  } catch {
    return undefined;
  } finally {
    closeQuietly(handle);
  }
}

/** Reads the snapshot in `directory`. A missing, oversized, unreadable or damaged file is no snapshot, never an error. */
export function readSnapshot(directory: string, version: string): string | undefined {
  const text = version === '' || directory === '' ? undefined : readCapped(join(directory, SNAPSHOT_FILE));

  return text === undefined ? undefined : snapshotFrom(text, version);
}

/** Marks the replayed page as old: said once at the top, and the page is inert until the live one replaces it. */
const REFRESHING = '<div id="coai-refreshing" class="hint" role="status">Refreshing…</div>';

/**
 * The stored page, ready for the webview: a FRESH nonce in its place, `<body inert>` so a press on a page that is about to be replaced is
 * not a press nobody hears, and a small "Refreshing…" line above it.
 *
 * @param stored a page {@link readSnapshot} answered
 * @param nonce a nonce made for THIS paint (`webviewNonce()`); never one that was used before
 */
export function snapshotPage(stored: string, nonce: string): string {
  return stored.split(NONCE_PLACEHOLDER).join(nonce).replace('<body>', `<body inert>\n${REFRESHING}`);
}

/**
 * The page when there is no snapshot: a heading and "Loading…", and nothing else. Its one script acknowledges the document, as the sidebar's
 * own does, so the first paint is known to have happened.
 */
export function skeletonPage(nonce: string): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<style>body { color: var(--vscode-foreground); font-family: var(--vscode-font-family); font-size: var(--vscode-font-size); padding: 8px 12px; }
.hint { opacity: .65; margin: 3px 0 0; }</style>
</head>
<body inert>
<div id="coai-refreshing" class="hint" role="status">Loading ConnectOtherAIs…</div>
<script nonce="${nonce}">
  const vscode = acquireVsCodeApi();
  vscode.postMessage({ type: 'ready' });
</script>
</body>
</html>`;
}
