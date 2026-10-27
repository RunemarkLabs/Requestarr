// Claude Code hook (after every file edit): lints an edited web app file with the project's ESLint configuration and
// its suppressions baseline, and hands any problem straight back (exit 2), so it is fixed before the build check sees
// it (docs/development.md#clean-code). C# needs a build to analyze, so it is left to `dotnet build`.
const { spawnSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const LINTED = /\.(js|jsx|ts|tsx)$/;

function readStdin() {
  try {
    return fs.readFileSync(0, 'utf8');
  } catch {
    return '';
  }
}

const input = JSON.parse(readStdin() || '{}');
const file = input.tool_input && input.tool_input.file_path;
const root = process.env.CLAUDE_PROJECT_DIR || process.cwd();
const relative = file ? path.relative(root, path.resolve(root, file)).split(path.sep).join('/') : '';

if (!relative.startsWith('frontend/') || !LINTED.test(relative) || relative.endsWith('.css.d.ts')) {
  process.exit(0);
}

const eslint = path.join(root, 'node_modules', '.bin', process.platform === 'win32' ? 'eslint.cmd' : 'eslint');
const result = spawnSync(
  eslint,
  [
    '--config', 'frontend/eslint.config.js',
    '--suppressions-location', 'frontend/eslint-suppressions.json',
    '--pass-on-unpruned-suppressions',
    relative
  ],
  { cwd: root, encoding: 'utf8', shell: process.platform === 'win32' }
);

if (result.status === 0) {
  process.exit(0);
}

process.stderr.write(
  `ESLint found problems in ${relative} (docs/development.md#clean-code):\n${result.stdout}${result.stderr}`
);
process.exit(2);
