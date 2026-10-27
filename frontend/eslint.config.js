// ESLint's flat configuration (ESLint 9). Converted from upstream's .eslintrc.js, which ESLint 10 no longer reads:
// upstream's rules are unchanged (less valid-jsdoc and require-jsdoc, which ESLint 9 removed and upstream had off;
// no-unused-vars keeps ESLint 8's caughtErrors default), and Requestarr's own checks are in the "Requestarr additions" block at the end
// (docs/development.md#clean-code). ESLint 10 waits for eslint-plugin-react and eslint-plugin-filenames to support it.
// Violations that existed when a check was added are recorded in eslint-suppressions.json, which may only shrink.
const fs = require('fs');
const path = require('path');
const { fixupPluginRules } = require('@eslint/compat');
const babelParser = require('@babel/eslint-parser');
const typescriptEslint = require('@typescript-eslint/eslint-plugin');
const typescriptParser = require('@typescript-eslint/parser');
const prettierConfig = require('eslint-config-prettier');
const filenames = require('eslint-plugin-filenames');
const importPlugin = require('eslint-plugin-import');
const prettier = require('eslint-plugin-prettier');
const react = require('eslint-plugin-react');
const reactHooks = require('eslint-plugin-react-hooks');
const simpleImportSort = require('eslint-plugin-simple-import-sort');
const sonarjs = require('eslint-plugin-sonarjs');
const globals = require('globals');

const frontendFolder = __dirname;

const dirs = fs
  .readdirSync(path.join(frontendFolder, 'src'), { withFileTypes: true })
  .filter((dirent) => dirent.isDirectory())
  .map((dirent) => dirent.name)
  .join('|');

const importGroups = [
  'error',
  {
    groups: [
      // Packages
      // Absolute Paths
      // Relative Paths
      // Css
      ['^@?\\w', `^(${dirs})(/.*|$)`, '^\\.', '^\\..*css$']
    ]
  }
];

// The react-hooks plugin's recommended rules, all as errors: its own config makes some of them warnings, which don't
// fail the build.
const reactHooksRules = Object.fromEntries(
  Object.keys(reactHooks.configs['recommended-latest'].rules).map((rule) => [rule, 'error'])
);

module.exports = [
  {
    ignores: ['**/JsLibraries/**', '**/*.css.d.ts']
  },

  // ESLint 8 didn't report unused disable comments; ESLint 9 does unless told not to.
  {
    linterOptions: {
      reportUnusedDisableDirectives: 'off'
    }
  },

  {
    files: ['**/*.{js,jsx,ts,tsx}'],

    languageOptions: {
      parser: babelParser,
      ecmaVersion: 2015,
      sourceType: 'module',
      parserOptions: {
        babelOptions: {
          configFile: `${frontendFolder}/babel.config.js`
        },
        ecmaFeatures: {
          modules: true,
          impliedStrict: true
        }
      },
      globals: {
        ...globals.browser,
        ...globals.commonjs,
        ...globals.node,
        ...globals.es2015,
        expect: false,
        chai: false,
        sinon: false,
        JSX: true
      }
    },

    plugins: {
      // eslint-plugin-filenames still uses ESLint's legacy rule format.
      filenames: fixupPluginRules(filenames),
      react,
      'react-hooks': reactHooks,
      'simple-import-sort': simpleImportSort,
      import: importPlugin,
      '@typescript-eslint': typescriptEslint,
      prettier,
      sonarjs
    },

    settings: {
      react: {
        version: 'detect'
      }
    },

    rules: {
      'filenames/match-exported': ['error'],

      // ECMAScript 6

      'arrow-body-style': [0],
      'arrow-parens': ['error', 'always'],
      'arrow-spacing': ['error', { before: true, after: true }],
      'constructor-super': 'error',
      'generator-star-spacing': 'off',
      'no-class-assign': 'error',
      'no-confusing-arrow': 'error',
      'no-const-assign': 'error',
      'no-dupe-class-members': 'error',
      'no-duplicate-imports': 'error',
      'no-new-symbol': 'error',
      'no-this-before-super': 'error',
      'no-useless-escape': 'error',
      'no-useless-computed-key': 'error',
      'no-useless-constructor': 'error',
      'no-var': 'warn',
      'object-shorthand': ['error', 'properties'],
      'prefer-arrow-callback': 'error',
      'prefer-const': 'warn',
      'prefer-reflect': 'off',
      'prefer-rest-params': 'off',
      'prefer-spread': 'warn',
      'prefer-template': 'error',
      'require-yield': 'off',
      'template-curly-spacing': ['error', 'never'],
      'yield-star-spacing': 'off',

      // Possible Errors

      'comma-dangle': 'error',
      'no-cond-assign': 'error',
      'no-console': 'off',
      'no-constant-condition': 'warn',
      'no-control-regex': 'error',
      'no-debugger': 'off',
      'no-dupe-args': 'error',
      'no-dupe-keys': 'error',
      'no-duplicate-case': 'error',
      'no-empty': 'warn',
      'no-empty-character-class': 'error',
      'no-ex-assign': 'error',
      'no-extra-boolean-cast': 'error',
      'no-extra-parens': ['error', 'functions'],
      'no-extra-semi': 'error',
      'no-func-assign': 'error',
      'no-inner-declarations': 'error',
      'no-invalid-regexp': 'error',
      'no-irregular-whitespace': 'error',
      'no-negated-in-lhs': 'error',
      'no-obj-calls': 'error',
      'no-regex-spaces': 'error',
      'no-sparse-arrays': 'error',
      'no-unexpected-multiline': 'error',
      'no-unreachable': 'warn',
      'no-unsafe-finally': 'error',
      'use-isnan': 'error',
      'valid-typeof': 'error',

      // Best Practices

      'accessor-pairs': 'off',
      'array-callback-return': 'warn',
      'block-scoped-var': 'warn',
      'consistent-return': 'off',
      curly: 'error',
      'default-case': 'error',
      'dot-location': ['error', 'property'],
      'dot-notation': 'error',
      eqeqeq: ['error', 'smart'],
      'guard-for-in': 'error',
      'no-alert': 'warn',
      'no-caller': 'error',
      'no-case-declarations': 'error',
      'no-div-regex': 'error',
      'no-else-return': 'error',
      'no-empty-function': ['error', { allow: ['arrowFunctions'] }],
      'no-empty-pattern': 'error',
      'no-eval': 'error',
      'no-extend-native': 'error',
      'no-extra-bind': 'error',
      'no-fallthrough': 'error',
      'no-floating-decimal': 'error',
      'no-implicit-coercion': ['error', {
        boolean: false,
        number: true,
        string: true,
        allow: [/* "!!", "~", "*", "+" */]
      }],
      'no-implicit-globals': 'error',
      'no-implied-eval': 'error',
      'no-invalid-this': 'off',
      'no-iterator': 'error',
      'no-labels': 'error',
      'no-lone-blocks': 'error',
      'no-loop-func': 'error',
      'no-magic-numbers': ['off', { ignoreArrayIndexes: true, ignore: [0, 1] }],
      'no-multi-spaces': 'error',
      'no-multi-str': 'error',
      'no-native-reassign': ['error', { exceptions: ['console'] }],
      'no-new': 'off',
      'no-new-func': 'error',
      'no-new-wrappers': 'error',
      'no-octal': 'error',
      'no-octal-escape': 'error',
      'no-param-reassign': 'off',
      'no-process-env': 'off',
      'no-proto': 'error',
      'no-redeclare': 'error',
      'no-return-assign': 'warn',
      'no-script-url': 'error',
      'no-self-assign': 'error',
      'no-self-compare': 'error',
      'no-sequences': 'error',
      'no-throw-literal': 'error',
      'no-unmodified-loop-condition': 'error',
      'no-unused-expressions': 'error',
      'no-unused-labels': 'error',
      'no-useless-call': 'error',
      'no-useless-concat': 'error',
      'no-void': 'error',
      'no-warning-comments': 'off',
      'no-with': 'error',
      radix: ['error', 'as-needed'],
      'vars-on-top': 'off',
      'wrap-iife': ['error', 'inside'],
      yoda: 'error',

      // Strict Mode

      strict: ['error', 'never'],

      // Variables

      'init-declarations': ['error', 'always'],
      'no-catch-shadow': 'error',
      'no-delete-var': 'error',
      'no-label-var': 'error',
      'no-restricted-globals': 'off',
      'no-shadow': 'error',
      'no-shadow-restricted-names': 'error',
      'no-undef': 'error',
      'no-undef-init': 'off',
      'no-undefined': 'off',
      'no-unused-vars': ['error', { args: 'none', ignoreRestSiblings: true, caughtErrors: 'none' }],
      'no-use-before-define': 'error',

      // Node.js and CommonJS

      'callback-return': 'warn',
      'global-require': 'error',
      'handle-callback-err': 'warn',
      'no-mixed-requires': 'error',
      'no-new-require': 'error',
      'no-path-concat': 'error',
      'no-process-exit': 'error',

      // Stylistic Issues

      'array-bracket-spacing': ['error', 'never'],
      'block-spacing': ['error', 'always'],
      'brace-style': ['error', '1tbs', { allowSingleLine: false }],
      camelcase: 'off',
      'comma-spacing': ['error', { before: false, after: true }],
      'comma-style': ['error', 'last'],
      'computed-property-spacing': ['error', 'never'],
      'consistent-this': ['error', 'self'],
      'eol-last': 'error',
      'func-names': 'off',
      'func-style': ['error', 'declaration', { allowArrowFunctions: true }],
      indent: ['error', 2, { SwitchCase: 1 }],
      'key-spacing': ['error', { beforeColon: false, afterColon: true }],
      'keyword-spacing': ['error', { before: true, after: true }],
      'lines-around-comment': ['error', { beforeBlockComment: true, afterBlockComment: false }],
      'max-depth': ['error', { maximum: 5 }],
      'max-nested-callbacks': ['error', 4],
      'max-statements': 'off',
      'max-statements-per-line': ['error', { max: 1 }],
      'new-cap': ['error', { capIsNewExceptions: ['$.Deferred', 'DragDropContext', 'DragLayer', 'DragSource', 'DropTarget'] }],
      'new-parens': 'error',
      'newline-after-var': 'off',
      'newline-before-return': 'off',
      'newline-per-chained-call': 'off',
      'no-array-constructor': 'error',
      'no-bitwise': 'error',
      'no-continue': 'error',
      'no-inline-comments': 'off',
      'no-lonely-if': 'warn',
      'no-mixed-spaces-and-tabs': 'error',
      'no-multiple-empty-lines': ['error', { max: 1 }],
      'no-negated-condition': 'warn',
      'no-nested-ternary': 'error',
      'no-new-object': 'error',
      'no-plusplus': 'off',
      'no-restricted-syntax': 'off',
      'no-spaced-func': 'error',
      'no-ternary': 'off',
      'no-trailing-spaces': 'error',
      'no-underscore-dangle': ['error', { allowAfterThis: true }],
      'no-unneeded-ternary': 'error',
      'no-whitespace-before-property': 'error',
      'object-curly-spacing': ['error', 'always'],
      'one-var': ['error', 'never'],
      'one-var-declaration-per-line': ['error', 'always'],
      'operator-assignment': ['off', 'never'],
      'operator-linebreak': ['error', 'after'],
      'quote-props': ['error', 'as-needed'],
      quotes: ['error', 'single'],
      semi: 'error',
      'semi-spacing': ['error', { before: false, after: true }],
      'sort-vars': 'off',
      'space-before-blocks': ['error', 'always'],
      'space-before-function-paren': ['error', 'never'],
      'space-in-parens': 'off',
      'space-infix-ops': 'off',
      'space-unary-ops': 'off',
      'spaced-comment': 'error',
      'wrap-regex': 'error',

      // ImportSort

      'simple-import-sort/imports': 'error',
      'import/newline-after-import': 'error',

      // React

      'react/jsx-boolean-value': [2, 'always'],
      'react/jsx-uses-vars': 2,
      'react/jsx-closing-bracket-location': 2,
      'react/jsx-tag-spacing': ['error'],
      'react/jsx-curly-spacing': [2, 'never'],
      'react/jsx-equals-spacing': [2, 'never'],
      'react/jsx-indent-props': [2, 2],
      'react/jsx-indent': [2, 2, { indentLogicalExpressions: true }],
      'react/jsx-key': 2,
      'react/jsx-no-bind': [2, { allowArrowFunctions: true }],
      'react/jsx-no-duplicate-props': [2, { ignoreCase: true }],
      'react/jsx-max-props-per-line': [2, { maximum: 2 }],
      'react/jsx-handler-names': [2, { eventHandlerPrefix: '(on|dispatch)', eventHandlerPropPrefix: 'on' }],
      'react/jsx-no-undef': 2,
      'react/jsx-pascal-case': 2,
      'react/jsx-uses-react': 2,
      // Explicitly disabled in case we want to enable them again
      'react/no-did-mount-set-state': 0,
      'react/no-did-update-set-state': 0,
      'react/no-direct-mutation-state': 2,
      'react/no-multi-comp': [2, { ignoreStateless: true }],
      'react/no-unknown-property': 2,
      'react/prefer-es6-class': 2,
      'react/prop-types': 2,
      'react/react-in-jsx-scope': 2,
      'react/self-closing-comp': 2,
      'react/sort-comp': 2,
      'react/jsx-wrap-multilines': 2,
      'react-hooks/rules-of-hooks': 'error',
      'react-hooks/exhaustive-deps': 'error'
    }
  },

  {
    files: ['**/*.js'],
    rules: {
      'simple-import-sort/imports': importGroups
    }
  },

  {
    files: ['**/*.ts', '**/*.tsx'],

    languageOptions: {
      parser: typescriptParser,
      parserOptions: {
        project: path.join(frontendFolder, '..', 'tsconfig.json')
      }
    },

    rules: {
      ...prettierConfig.rules,
      ...typescriptEslint.configs.recommended.rules,
      '@typescript-eslint/no-unused-vars': [
        'error',
        {
          args: 'after-used',
          argsIgnorePattern: '^_',
          ignoreRestSiblings: true
        }
      ],
      '@typescript-eslint/explicit-function-return-type': 'off',
      'no-shadow': 'off',
      'prettier/prettier': 'error',
      'simple-import-sort/imports': importGroups,

      // React Hooks
      'react-hooks/rules-of-hooks': 'error',
      'react-hooks/exhaustive-deps': 'error',

      // React
      'react/function-component-definition': 'error',
      'react/hook-use-state': 'error',
      'react/jsx-boolean-value': ['error', 'always'],
      'react/jsx-curly-brace-presence': [
        'error',
        { props: 'never', children: 'never' }
      ],
      'react/jsx-fragments': 'error',
      'react/jsx-handler-names': [
        'error',
        {
          eventHandlerPrefix: 'on',
          eventHandlerPropPrefix: 'on'
        }
      ],
      'react/jsx-no-bind': ['error', { ignoreRefs: true }],
      'react/jsx-no-useless-fragment': ['error', { allowExpressions: true }],
      'react/jsx-pascal-case': ['error', { allowAllCaps: true }],
      'react/jsx-sort-props': [
        'error',
        {
          callbacksLast: true,
          noSortAlphabetically: true,
          reservedFirst: true
        }
      ],
      'react/prop-types': 'off',
      'react/self-closing-comp': 'error'
    }
  },

  // ---------------------------------------------------------------------------------------------------------------
  // Requestarr additions (docs/development.md#clean-code). Upstream's rules above stay as they are. These apply to
  // every file; violations in inherited code when they were added are in eslint-suppressions.json.
  // ---------------------------------------------------------------------------------------------------------------
  {
    files: ['**/*.{js,jsx,ts,tsx}'],
    rules: {
      ...reactHooksRules,
      complexity: ['error', { max: 15, variant: 'modified' }],
      'sonarjs/cognitive-complexity': ['error', 15],
      'max-depth': ['error', { max: 4 }],
      'max-params': ['error', { max: 4 }],
      'max-lines-per-function': ['error', { max: 80, skipBlankLines: true, skipComments: true }]
    }
  },
  // A .ts file holds no markup, so its functions get the tighter limit.
  {
    files: ['**/*.ts'],
    rules: {
      'max-lines-per-function': ['error', { max: 50, skipBlankLines: true, skipComments: true }]
    }
  }
];
