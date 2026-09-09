/**
 * A `Response` for a stubbed `fetch` — a REAL one, built by the runtime's own constructor.
 *
 * <p><b>There is no cast here, and that is the point.</b> Eight fixtures across three files used to
 * hand-build an object and assert it was a `Response`, which `typescript/doctrine.md` allows only
 * with a note saying why. Writing that note was the first plan; a reviewer asked the better question
 * — why is a fixture pretending, when Node has shipped the real thing since 18? `new Response(...)`
 * gives `ok` derived from the status, real case-insensitive `Headers`, a real `text()`, and every
 * member nobody has needed yet (`json()`, `clone()`, `bodyUsed`) already correct.</p>
 *
 * <p><b>What the pretending cost.</b> All eight omitted `headers`, because nothing read one. The day
 * the client started reading a response header they all threw `Cannot read properties of undefined`,
 * in tests with no connection to that change — and one of them was missed twice while they were
 * repaired by hand. A hand-built stand-in can silently lack whatever the production code has not
 * needed yet; the real constructor cannot.</p>
 *
 * <p>Test-only, and it lives here rather than in `src/` for that reason: nothing that ships may
 * import it.</p>
 */
export function response(parts: {
  readonly status: number;
  readonly body?: string;
  readonly headers?: Record<string, string>;
}): Response {
  // A 204 or 304 legitimately carries no body, and the constructor REFUSES a non-null body for
  // those — which is the real rule, and one a hand-built object could break without noticing.
  const empty = parts.status === 204 || parts.status === 304;

  return new Response(empty ? null : (parts.body ?? ''), {
    status: parts.status,
    // A fresh `Headers` per call. Sharing one would let a test that adds a header change what a
    // later test sees, which is the kind of order-dependent failure nobody debugs twice.
    headers: parts.headers ?? {},
  });
}
