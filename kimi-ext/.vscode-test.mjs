import { mkdirSync, writeFileSync } from 'node:fs';
import * as path from 'node:path';
import { defineConfig } from '@vscode/test-cli';

const taskWorkspace = path.join(import.meta.dirname, '.vscode-test', 'task-workspace');
mkdirSync(taskWorkspace, { recursive: true });
const otherWorkspace = path.join(import.meta.dirname, '.vscode-test', 'selection-other');
const selectionWorkspace = path.join(import.meta.dirname, '.vscode-test', 'selection.code-workspace');
mkdirSync(otherWorkspace, { recursive: true });
writeFileSync(selectionWorkspace, JSON.stringify({ folders: [{ path: taskWorkspace }, { path: otherWorkspace }] }));
const common = {
  mocha: { ui: 'tdd', timeout: 30000 },
  useInstallation: process.env.VSCODE_EXECUTABLE_PATH
    ? { fromPath: process.env.VSCODE_EXECUTABLE_PATH }
    : undefined,
  extensionDevelopmentPath: process.env.KIMI_EXTENSION_UNDER_TEST,
  launchArgs: ['--disable-extensions', '--skip-welcome', '--skip-release-notes', '--new-window'],
};

export default defineConfig([
  { ...common, label: 'lsp', files: 'out/test/extension.test.js' },
  { ...common, label: 'commands', files: 'out/test/tasks.test.js', workspaceFolder: taskWorkspace },
  { ...common, label: 'selection', files: 'out/test/targetSelection.test.js', workspaceFolder: selectionWorkspace },
].map(config => ({
  ...config,
  // Keep empty-window, single-folder, and multi-root tests from restoring each other's windows.
  launchArgs: [...common.launchArgs, `--user-data-dir=${path.join(import.meta.dirname, '.vscode-test', 'profiles', config.label)}`],
})));
