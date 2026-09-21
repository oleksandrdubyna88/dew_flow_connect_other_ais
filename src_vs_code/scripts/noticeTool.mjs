// Where the notice companion is, and building it before anything trusts it.
//
// `NoticeTool` is a test-only executable built by the solution and never published — the `FakeCli`
// pattern — and TWO scripts here drive it: `run-parity.mjs` runs both notice serialisers over one
// corpus, and `measure-append.mjs` runs the product's append from several real processes at one
// file. Finding it and building it is the same job in both, and the second copy is where the two
// would drift: the search order, the `COAI_NOTICE_TOOL` override and the reason the build is not
// optional are each a decision, not a detail.
//
// THE BUILD IS THE POINT, not the search. A reviewer named what finding it alone allows: edit
// `Redaction.cs`, run the check, and it compares against yesterday's DLL — green, about code that
// no longer exists. CI is safe by ordering; a developer's machine is not, and these checks are most
// useful exactly while somebody is changing the thing they check.
import { spawnSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

/** The repository root, from this file's own location. */
export const REPO = join(dirname(fileURLToPath(import.meta.url)), '..', '..');

/** How long the companion may take before a caller gives up rather than hanging a CI job. */
export const TIMEOUT_MS = 120_000;

/** The project, for the error message that tells a person what to run. */
export const PROJECT = join(REPO, 'src_mcp', 'tests_notices', 'NoticeTool.csproj');

/** The built companion, or an empty string. `COAI_NOTICE_TOOL` names one directly. */
export function findNoticeTool() {
  const named = process.env['COAI_NOTICE_TOOL'];
  if (named !== undefined && named.length > 0) {
    return named;
  }
  for (const configuration of ['Release', 'Debug']) {
    const candidate = join(REPO, 'src_mcp', 'tests_notices', 'bin', configuration, 'net10.0', 'NoticeTool.dll');
    if (existsSync(candidate)) {
      return candidate;
    }
  }

  return '';
}

/**
 * The companion, built from the current sources first.
 *
 * <p>It THROWS rather than answering an empty string: a check that quietly passes when it could not
 * find the thing it compares against is worse than no check — a green tick over an unasked
 * question. `announce` is called with the one line a caller wants prefixed in its own voice.</p>
 */
export function noticeTool(announce = () => {}) {
  // NAMED means named: `COAI_NOTICE_TOOL` points at one artefact on purpose — a published build, a
  // DLL carried to another machine, the Windows build run under WSL to ask a second kernel the same
  // question — and rebuilding it from whatever sources happen to be beside it would answer about a
  // different binary than the one the caller chose.
  const named = process.env['COAI_NOTICE_TOOL'];
  if (named !== undefined && named.length > 0) {
    announce(`using the NoticeTool named by COAI_NOTICE_TOOL: ${named}`);

    return named;
  }

  announce('building NoticeTool from the current sources...');
  const built = spawnSync('dotnet', ['build', PROJECT, '-v', 'q', '--nologo'], {
    encoding: 'utf8',
    timeout: TIMEOUT_MS * 4,
    shell: false,
  });
  if (built.status !== 0) {
    throw new Error(`NoticeTool would not build:\n${built.stdout ?? ''}${built.stderr ?? ''}`);
  }

  const tool = findNoticeTool();
  if (tool === '') {
    throw new Error(`no NoticeTool build found. Run: dotnet build ${PROJECT}`);
  }

  return tool;
}
