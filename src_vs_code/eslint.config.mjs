// The linter this package could not have until today.
//
// WHY IT COULD NOT. `typescript-eslint` declares peer `typescript: ">=4.8.4 <6.1.0"` — true of
// `latest` (8.70.0) and of the alpha canary alike, checked against the registry. This package was on
// `typescript ^7.0.2`, so the parser refused to install; forced past that with `--legacy-peer-deps`
// it refuses at RUN time too, and by name: "typescript-eslint does not support TS 7.0", pointing at
// issue #10940. So the largest TypeScript package in the family had no linter at all.
//
// WHY 6.0.3, AND NOT THE 5.9.3 THE FIRST MEASUREMENT CHOSE. TypeScript 7 arrived here as an ordinary
// `chore(deps)` bump (7c08d6a8), and on the morning of 2026-09-18 this source typechecked clean on
// 5.9.3 — measured, and the suite passed 3583 to 0. It stopped being true the same day:
// `codeHighlight.ts` landed on main with `shiki@^4.4.3`, which is ESM-only (`"type": "module"`) and
// is imported by subpath from a CommonJS module. `require()` of an ESM package is legal under TS 7's
// semantics and not under 5.9.3, which reports TS1479 five times — and the page depends on the
// SYNCHRONOUS `createHighlighterCoreSync`, so `await import()` would be a redesign of that feature
// rather than a fix belonging in a CI change.
//
// `typescript@6.0.3` is the one version that does BOTH: it compiles this source with ZERO errors,
// shiki included, and it satisfies the parser's `<6.1.0`. Measured: 3756 tests to 0.
//
// THE TYPE-AWARE RULE IS THE POINT. `no-floating-promises` cannot be had from a syntax-only config,
// and it is the rule the hardening plan names. A plain `eslint` would have passed a gate having
// dropped the one rule the gate was for, which is why this waited for the compiler to move instead
// of shipping a weaker check.
//
// THE RULE SET IS READ OFF THE SOURCE, not chosen. This code already carried `eslint-disable`
// comments naming `no-control-regex`, `no-new-func` and `no-await-in-loop`, written against a config
// that never existed. Only the first is enabled by a preset. `no-new-func` is switched on for
// production only, where it costs nothing and means something — all twenty-four `new Function` sites
// are in tests, which is how a page script is executed at all. `no-await-in-loop` is NOT switched
// on: it reports 73 times here, and five decorative markers are not evidence that a codebase wants a
// rule it violates seventy-three times; those five directives were removed instead.
// `reportUnusedDisableDirectives` makes any marker that has stopped applying an error rather than a
// silent exemption.
//
// TWO OF THE THREE ARE NOW HERE, and the third still is not. The sibling extensions carry a boundary
// set — `complexity: 4`, `max-lines-per-function: 50`, `no-console`. When this file was written they
// reported 451, 49 and 109 here, and the note reasoned that 451 is not a boundary but a wholesale
// rejection of how the package is written. That reasoning was right and its conclusion has expired:
// the way to hold new code to a rule the old code breaks is not an exemption, it is a SUPPRESSIONS
// FILE, and ESLint 10 has one.
//
// So `complexity` and `max-lines-per-function` are on for `src/**`, off for `src/test/**` (tests
// narrate, the same reason `max-lines` is off there), and `eslint-suppressions.json` records the
// violations that already existed — measured 2026-09-21 at 770 across 236 files, of which 615 in
// production. `npm run lint` needs NO flag: ESLint reads the file from its default location, which
// was measured rather than assumed. New code is held to both rules with nothing for anyone to
// remember, and `suppressionsOnlyShrink.test.ts` refuses a suppression that was not there before,
// so the file cannot become a place to hide a new violation.
//
// | purpose | command |
// |---|---|
// | regenerate the baseline | `npx eslint src --suppress-rule complexity --suppress-rule max-lines-per-function` |
// | drop entries whose violation was fixed | `npx eslint src --prune-suppressions` |
//
// `no-console` is STILL not here: 109 violations, and no equivalent argument has been made for it.
import js from '@eslint/js';
import globals from 'globals';
import tseslint from 'typescript-eslint';

export default tseslint.config(
  {
    // `out/` is compiled output, the `.mjs` scripts are not in the TypeScript project, and
    // `src/generated/` is written by `prepare-gate.mjs` during the build.
    ignores: ['out/**', 'node_modules/**', 'scripts/**', 'src/generated/**'],
  },
  {
    // A disable that has stopped being needed is an exemption nobody granted: once a refactor makes
    // it unnecessary, the stale marker keeps exempting the code silently. Making that an error turns
    // "remove it when it no longer applies" from prose into a check.
    linterOptions: { reportUnusedDisableDirectives: 'error' },
  },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    files: ['src/**/*.ts'],
    languageOptions: {
      parserOptions: { projectService: true, tsconfigRootDir: import.meta.dirname },
    },
    rules: {
      // `node:test`'s `test`/`it`/`describe` return a promise BY DESIGN and are not meant to be
      // awaited — without this exemption the rule reports 3,544 times in `src/test` and says nothing
      // about any of them. Naming the package rather than the identifier means a local function
      // called `test` is still checked.
      '@typescript-eslint/no-floating-promises': ['error', {
        allowForKnownSafeCalls: [
          {
            from: 'package',
            package: 'node:test',
            name: ['test', 'it', 'describe', 'suite', 'before', 'after', 'beforeEach', 'afterEach'],
          },
        ],
      }],
      // A leading underscore is this codebase's existing way of saying "bound and deliberately
      // unused" — `_`, `_done`, `_gone`. Encoding that here is reading the convention off the source
      // rather than asking four call sites to change to suit a default.
      '@typescript-eslint/no-unused-vars': ['error', {
        argsIgnorePattern: '^_',
        varsIgnorePattern: '^_',
        caughtErrorsIgnorePattern: '^_',
        // `const { run, ...rest } = record` binds `run` for the sole purpose of leaving it OUT of
        // `rest`. That is the idiom, not an oversight — and it is the one thing spelling these
        // options out turned off, because the base rule's default for it is `false`.
        ignoreRestSiblings: true,
      }],

      // Free where it counts: the extension does not build code at runtime and never should, and
      // measured across `src`, every one of the twenty-four `new Function` sites is in a TEST, where
      // it is how a page script gets executed at all. So the guard is real in production and off in
      // the tests, rather than nine decorative `eslint-disable` comments for a rule nobody enabled.
      'no-new-func': 'error',

      'max-lines': ['error', { max: 800, skipBlankLines: false, skipComments: false }],
      complexity: ['error', 4],
      'max-lines-per-function': ['error', 50],
    },
  },
  {
    // Tests narrate: one file holds one scenario end to end, and splitting it to satisfy a line count
    // hides the story the test exists to tell. And a test page script is executed by `new Function`,
    // which is the only way to run one outside a webview.
    files: ['src/test/**/*.ts'],
    rules: {
      'max-lines': 'off',
      'no-new-func': 'off',
      // One scenario told end to end is one function, and the assertions are the story. Splitting a
      // test to satisfy a line count hides what it exists to say — the same argument as `max-lines`
      // above, one level down.
      complexity: 'off',
      'max-lines-per-function': 'off',
    },
  },
  {
    // The `.test.mjs` files are node scripts, not part of the TypeScript program: they get node's
    // globals and none of the type-aware rules, which have no program to consult for them.
    files: ['**/*.mjs'],
    languageOptions: { globals: globals.node },
  },
);
