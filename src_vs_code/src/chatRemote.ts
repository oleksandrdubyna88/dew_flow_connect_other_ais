import { RemoteChatSession, RemoteTransport } from './remoteChatSession';
import { DEFAULT_BUDGETS } from './chatSession';
import { REAL_TIMERS } from './cliChatSession';
import { ask } from './teamServerApi';
import { Vendor } from './vendors';

/**
 * A chat session over a Team server, assembled from what the extension already has.
 *
 * <p>Its own module for the reason `chatProcess.ts` is: the session itself must stay testable
 * without a host, and this is the file that knows about tokens, URLs and `fetch`. Nothing here
 * decides anything — every rule about the wire is in `remoteAsk.ts` and every rule about the
 * conversation is in `remoteChatSession.ts`.</p>
 *
 * <p><b>The vendor id the SERVER knows is not the row's id.</b> A row is named `<server>-<vendor>`
 * so that two Team servers each offering `codex` do not collide, and sending the row id would be
 * refused by every server as a vendor it "does not offer" — which reads exactly like a typo. This
 * repository has already shipped that defect once, in three releases; `remoteVendor` is the field
 * that fixes it and this is one more place that must use it.</p>
 */

/** One request to a Team server, as `ask` performs it. */
function requestOf(url: string, token: string): RemoteTransport {
  return {
    submit: async (body) => {
      const answered = await ask<unknown>(url, 'api/reviews', { method: 'POST', body, token });

      return answered.ok
        ? { body: answered.value, failure: '', status: 200 }
        : { body: undefined, failure: answered.message, status: answered.status };
    },
    poll: async (id, waitSeconds) => {
      const answered = await ask<unknown>(url, `api/reviews/${id}?wait=${waitSeconds}`, { token });

      return answered.ok
        ? { body: answered.value, failure: '', status: 200 }
        : { body: undefined, failure: answered.message, status: answered.status };
    },
    cancel: async (id) => {
      // Nothing waits for this and nothing reads its answer: the tab is already closing. What
      // matters is that the server stops holding a vendor slot for a question nobody will read.
      await ask<unknown>(url, `api/reviews/${id}`, { method: 'DELETE', token });
    },
  };
}

/**
 * The conversation for a remote row, or nothing when this side cannot reach that server.
 *
 * @param url the server's address, canonical
 * @param token the bearer this side holds for it, or empty when it is not signed in
 */
export function remoteChatFor(vendor: Vendor, url: string, token: string): RemoteChatSession | undefined {
  if (url.length === 0 || token.length === 0) {
    return undefined;
  }

  return new RemoteChatSession(
    requestOf(url, token),
    // The vendor the SERVER knows, falling back to the row's own id for a row written before that
    // field existed — which is what every other caller of it does.
    { vendor: vendor.remoteVendor ?? vendor.id, model: vendor.model },
    DEFAULT_BUDGETS,
    REAL_TIMERS,
  );
}
