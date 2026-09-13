/**
 * Run jobs a few at a time — `width` abreast — and hand back every result in the jobs' own order.
 *
 * <p>The one worker pool this feature has. Serially, thousands of small reads are thousands of awaits
 * one after another; all at once is a thundering herd of open descriptors against a directory another
 * window may be writing. It was written first inside the migration (`chatStoreImport.ts`), and the
 * store's listing carried the same loop inline; when the sweep and the index of story B1 needed it too
 * it was moved here rather than copied a third and a fourth time — the reuse rule's second move.</p>
 *
 * <p>A job that rejects rejects the whole run, as `Promise.all` would: every caller here hands in jobs
 * that answer in outcomes and never throw, and a throw is then a defect worth surfacing rather than a
 * result worth skipping.</p>
 */
export async function abreast<T>(jobs: readonly (() => Promise<T>)[], width: number): Promise<readonly T[]> {
  const out = new Array<T>(jobs.length);
  let next = 0;
  const worker = async (): Promise<void> => {
    while (next < jobs.length) {
      const here = next;
      next += 1;
      out[here] = await (jobs[here] as () => Promise<T>)();
    }
  };
  await Promise.all(Array.from({ length: Math.min(width, jobs.length) }, worker));

  return out;
}
