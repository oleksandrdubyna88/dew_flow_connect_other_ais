import { RemoteChatSession, RemoteTransport } from './remoteChatSession';
import { DEFAULT_BUDGETS } from './chatSession';
import { REAL_TIMERS } from './cliChatSession';
import { TeamServer, serverVendorOf } from './teamServers';
import { ask } from './teamServerApi';
import { Vendor } from './vendors';

/**
 * A chat session over a Team server, assembled from what the extension already has.
 *
 * <p>Its own module for the reason `chatProcess.ts` is: the session itself must stay testable
 * without a host, and this is the file that knows about tokens, URLs and `fetch`. Nothing here
 * decides anything — every rule about the wire is in `remoteAsk.ts`, every rule about the
 * conversation is in `remoteChatSession.ts`, and the rule about which name the server knows is in
 * `teamServers.ts` beside the function that builds the other direction.</p>
 */

/** One request to a Team server, as `ask` performs it. Injected, so the wiring itself is testable. */
export type AskServer = typeof ask;

/** What `ask` answered, in the shape the session takes — the STATUS included, whichever arm it is. */
export function transportFor(url: string, token: string, asked: AskServer = ask): RemoteTransport {
  return {
    submit: async (body) => {
      const answered = await asked<unknown>(url, 'api/reviews', { method: 'POST', body, token });

      // `answered.status`, not a literal 200. A success arm that reports a status it did not receive
      // is a fact invented at the one place the real one was available, and the poll loop next to it
      // decides on exactly this field. (gemini, the code round.)
      return answered.ok
        ? { body: answered.value, failure: '', status: answered.status }
        : { body: undefined, failure: answered.message, status: answered.status };
    },
    poll: async (id, waitSeconds) => {
      const answered = await asked<unknown>(url, `api/reviews/${id}?wait=${waitSeconds}`, { token });

      return answered.ok
        ? { body: answered.value, failure: '', status: answered.status }
        : { body: undefined, failure: answered.message, status: answered.status };
    },
    cancel: async (id) => {
      // Nothing waits for this and nothing reads its answer: the tab is already closing. What
      // matters is that the server stops holding a vendor slot for a question nobody will read.
      await asked<unknown>(url, `api/reviews/${id}`, { method: 'DELETE', token });
    },
  };
}

/**
 * The conversation for a remote row, or nothing when this side cannot reach that server.
 *
 * @param server the Team server the row belongs to — its id names the row, so it is what tells the
 *   row's own name apart from the vendor name the server knows
 * @param token the bearer this side holds for it, or empty when it is not signed in
 */
export function remoteChatFor(
  vendor: Vendor,
  server: TeamServer,
  token: string,
  asked: AskServer = ask,
): RemoteChatSession | undefined {
  if (server.url.length === 0 || token.length === 0) {
    return undefined;
  }

  return new RemoteChatSession(
    transportFor(server.url, token, asked),
    { vendor: serverVendorOf(vendor, server.id), model: vendor.model },
    DEFAULT_BUDGETS,
    REAL_TIMERS,
  );
}
