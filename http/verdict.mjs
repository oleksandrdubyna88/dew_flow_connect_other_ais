// What httpyac's exit status means for the contract suite — the one mapping `run-contracts.mjs` reports by, kept pure
// so a test can hold it to the pinned httpyac's own codes.

/** Exit codes, so a caller can tell a broken contract from a broken machine. */
export const PASS = 0;
export const CONTRACT = 1;
export const ENVIRONMENT = 3;
export const CONFIG = 4;

/** httpyac's own exit status for a run with a FAILED assertion (6.16.7, `testExitCode`). */
export const ASSERTION_FAILED = 20;

/**
 * The suite's verdict for one httpyac run: `{ code, message }`, the message empty on a pass.
 *
 * <p>The rule reserves exit 1 for a CONTRACT regression — an API that answered something other than what its file says.
 * Everything else is the machine: a run killed before it finished, a bad argument, a crash. Reporting those as 1 tells
 * whoever reads the exit code that the server is broken when it is the harness that is.</p>
 *
 * @param {number | null} status httpyac's exit status — `null` when it was killed.
 */
export function verdictOf(status) {
  if (status === null) {
    return { code: ENVIRONMENT, message: 'httpyac was killed before it finished — no verdict was produced.' };
  }

  if (status === PASS) {
    return { code: PASS, message: '' };
  }

  // 20 is what the pinned httpyac (6.16.7) sets when a test() assertion FAILED (its `testExitCode` hook); 1 is kept
  // for a version that answered failures with 1. Treating only 1 as a failed assertion reported every real regression
  // as a misconfigured suite.
  if (status === ASSERTION_FAILED || status === 1) {
    return { code: CONTRACT, message: 'a contract request failed — the API did not answer what its .http file says it does.' };
  }

  return {
    code: CONFIG,
    message: `httpyac exited ${status}, which is neither pass nor a failed assertion — the suite is misconfigured, not the API.`,
  };
}
