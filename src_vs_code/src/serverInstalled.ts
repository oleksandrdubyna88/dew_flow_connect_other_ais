import { hostRuntime } from './hostRuntime';

/**
 * "The server on this side was (re)installed" — said by the install module, heard by the legacy panel (v2 E4, story s5-install-module).
 *
 * <p>The old `coai.installServer` handler installed, then told the panel to forget what it knew about the server's roots and to repaint. The install now lives in
 * its own bundle and the panel in another, so the two meet through the host's one runtime (`hostRuntime().once`): a listener list that exists once per host, not once per
 * bundle. A listener that throws does not stop the others.</p>
 */
const listeners = (): Set<() => void> => hostRuntime().once('serverInstalledListeners', () => new Set<() => void>());

/** Registers a listener; the returned function removes it. */
export function onServerInstalled(listener: () => void): () => void {
  listeners().add(listener);

  return () => { listeners().delete(listener); };
}

/** Tells every listener. */
export function announceServerInstalled(): void {
  for (const listener of [...listeners()]) {
    try {
      listener();
    } catch (error) {
      console.error('ConnectOtherAIs: a listener of "the server was installed" failed', error);
    }
  }
}
