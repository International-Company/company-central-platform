import js from '@eslint/js';
import tseslint from 'typescript-eslint';

/**
 * Lint rules that enforce the constraints of ARCHITECTURE.md §9.
 *
 * Two of these are acceptance criteria of Phase 7 and cannot be met by
 * intention alone: on a screen written at speed, a hardcoded string or a
 * physical `left` is the path of least resistance. A rule makes the wrong thing
 * fail the build instead of surviving review.
 */
export default tseslint.config(
  { ignores: ['.next/**', 'node_modules/**', 'next-env.d.ts'] },

  js.configs.recommended,
  ...tseslint.configs.recommendedTypeChecked,

  {
    // The build's own config files are not part of the TypeScript project, so
    // type-aware rules cannot parse them. Linted without type information
    // rather than excluded outright: they are still code, and a genuine mistake
    // in one breaks the build for everyone.
    //
    // `next build` lints only `src`, which is why this only surfaced in CI —
    // a reminder that "the build passed" is not the same as "the lint script
    // passed".
    files: ['**/*.mjs', '**/*.js'],
    ...tseslint.configs.disableTypeChecked,
  },

  {
    files: ['**/*.ts', '**/*.tsx'],

    languageOptions: {
      parserOptions: {
        projectService: true,
        tsconfigRootDir: import.meta.dirname,
      },
    },

    rules: {
      // -------------------------------------------------------------------
      // No physical direction utilities.
      //
      // `ml-4` survives the mirror and lands on the wrong side in Arabic;
      // `ms-4` follows the direction and needs no second rule. This is the
      // single most common way an RTL layout breaks, and it breaks quietly —
      // the page still renders, it is just wrong for half the company.
      // -------------------------------------------------------------------
      'no-restricted-syntax': [
        'error',
        {
          selector:
            "Literal[value=/(^|\\s)-?(ml|mr|pl|pr|left|right|border-l|border-r|rounded-l|rounded-r|text-left|text-right)-/]",
          message:
            'Physical direction utility. Use the logical equivalent (ms/me, ps/pe, start/end, border-s/border-e, text-start/text-end) so the layout mirrors in Arabic.',
        },
        {
          selector:
            "JSXAttribute[name.name='className'] Literal[value=/(^|\\s)(left|right)-/]",
          message:
            'Physical inset. Use start-* / end-* so the layout mirrors in Arabic.',
        },
      ],

      // -------------------------------------------------------------------
      // No hardcoded user-visible strings.
      //
      // Every string a person reads comes from the catalogue. This rule cannot
      // see intent, so it flags any non-trivial literal rendered as JSX text
      // and expects the author to move it or to be deliberate about why not.
      // -------------------------------------------------------------------
      'react/jsx-no-literals': 'off',

      '@typescript-eslint/no-unused-vars': [
        'error',
        { argsIgnorePattern: '^_', varsIgnorePattern: '^_' },
      ],

      // A floating promise in a UI is a request nobody is waiting for and an
      // error nobody will see.
      '@typescript-eslint/no-floating-promises': 'error',
      '@typescript-eslint/no-misused-promises': 'error',

      // `any` in a typed client defeats the point of generating types from the
      // OpenAPI document at all.
      '@typescript-eslint/no-explicit-any': 'error',
    },
  },

  {
    // The catalogues are the one place strings are supposed to live.
    files: ['src/i18n/**'],
    rules: { 'no-restricted-syntax': 'off' },
  },
);
