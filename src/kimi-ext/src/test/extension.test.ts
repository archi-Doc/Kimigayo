import * as assert from 'node:assert/strict';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { createRequire } from 'node:module';
import * as path from 'node:path';
import * as vscode from 'vscode';

suite('Kimi extension integration', () => {
  const errors: string[] = [];
  let errorWindow = vscode.window;
  let originalShowError = errorWindow.showErrorMessage;
  let root: string;
  let originalPath: unknown;
  let originalTrace: unknown;

  suiteSetup(async () => {
    root = await mkdtemp(path.join(tmpdir(), 'kimi-ext-integration-'));
    const extension = vscode.extensions.getExtension('archi-Doc.kimi-ext');
    assert.ok(extension, 'Kimi extension was not loaded');
    // VS Code scopes its API objects to the calling extension. Packaged smoke
    // tests live outside the extracted extension, so capture its API instance.
    errorWindow = (createRequire(path.join(extension.extensionPath, 'package.json'))('vscode') as typeof vscode).window;
    originalShowError = errorWindow.showErrorMessage;
    originalPath = vscode.workspace.getConfiguration('kimi').inspect('serverPath')?.globalValue;
    originalTrace = vscode.workspace.getConfiguration('kimi').inspect('trace.server')?.globalValue;
    await vscode.workspace.getConfiguration('kimi').update('trace.server', 'verbose', vscode.ConfigurationTarget.Global);
    errorWindow.showErrorMessage = ((text: string) => {
      errors.push(text);
      return Promise.resolve(undefined);
    }) as typeof vscode.window.showErrorMessage;
    await vscode.workspace.getConfiguration('kimi').update('serverPath', path.join(root, 'missing.exe'), vscode.ConfigurationTarget.Global);
    // Do not call activate(): that hid the missing startup activation event.
    await waitFor(() => extension.isActive && errors.some(error => error.includes('missing.exe')),
      'automatic activation and path validation without an open Kimi document');
  });

  suiteTeardown(async () => {
    try {
      await vscode.workspace.getConfiguration('kimi').update('serverPath', originalPath, vscode.ConfigurationTarget.Global);
      await vscode.workspace.getConfiguration('kimi').update('trace.server', originalTrace, vscode.ConfigurationTarget.Global);
      await vscode.commands.executeCommand('workbench.action.closeAllEditors');
      assert.ok(path.resolve(root).startsWith(path.resolve(tmpdir()) + path.sep));
      await rm(root, { recursive: true, force: true });
    } finally {
      errorWindow.showErrorMessage = originalShowError;
    }
  });

  async function configure(value: string): Promise<void> {
    errors.length = 0;
    await vscode.workspace.getConfiguration('kimi').update('serverPath', value, vscode.ConfigurationTarget.Global);
    await vscode.commands.executeCommand('kimi.restartServer');
  }

  test('activates automatically and reports a missing executable without a Kimi document', () => {
    assert.ok(vscode.extensions.getExtension('archi-Doc.kimi-ext')?.isActive);
    assert.ok(!vscode.workspace.textDocuments.some(document => ['kimi', 'kimiproj'].includes(document.languageId)));
    assert.ok(errors.some(error => error.includes('kimi.serverPath') && error.includes('missing.exe')));
  });
  test('validates a settings change before any Kimi document or restart command', async () => {
    errors.length = 0;
    await vscode.workspace.getConfiguration('kimi').update('serverPath', root, vscode.ConfigurationTarget.Global);
    await waitFor(() => errors.some(error => error.includes('expected an executable file') && error.includes(root)),
      'automatic validation in a settings-only window');
  });
  test('registers both language ids', async () => {
    const ids = await vscode.languages.getLanguages();
    assert.ok(ids.includes('kimi') && ids.includes('kimiproj'));
  });
  test('explains the task workspace requirement before launching a command', async () => {
    errors.length = 0;
    assert.equal(vscode.workspace.workspaceFolders?.length ?? 0, 0);
    assert.equal(await vscode.commands.executeCommand('kimi.build'), 'failed');
    assert.ok(errors.some(error => error.includes('Open a folder') && error.includes('.kimiproj file is not required')));
  });
  test('starts the server when the first Kimi file opens after startup validation', async function () {
    const server = process.env.KIMI_TEST_SERVER_PATH;
    if (!server) {
      this.skip();
    }
    errors.length = 0;
    await vscode.workspace.getConfiguration('kimi').update('serverPath', server!, vscode.ConfigurationTarget.Global);
    const file = path.join(root, 'Startup.kimi');
    await writeFile(file, '::Kimi.Console.writeLine("Hello" +');
    const uri = vscode.Uri.file(file);
    await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(uri));
    await waitForDiagnostics(uri, items => items.length > 0);
    assert.deepEqual(errors, []);
    await vscode.commands.executeCommand('workbench.action.closeAllEditors');
  });
  test('reports a directory and provides a usable restart command', async () => {
    await configure(root);
    assert.ok(errors.some(error => error.includes('expected an executable file')));
  });
  test('reports an empty setting', async () => {
    await configure('');
    assert.ok(errors.some(error => error.includes('kimi.serverPath must contain')));
  });
  test('reports an unchanged invalid path only once across repeated restarts', async () => {
    await configure(path.join(root, 'missing-once.exe'));
    await vscode.commands.executeCommand('kimi.restartServer');
    await vscode.commands.executeCommand('kimi.restartServer');
    assert.equal(errors.length, 1, errors.join('\n'));
    assert.ok(errors[0].includes('missing-once.exe'));
    await configure(path.join(root, 'another-missing.exe'));
    assert.equal(errors.length, 1, errors.join('\n'));
    assert.ok(errors[0].includes('another-missing.exe'));
  });
  test('reports failure to launch an existing invalid executable', async () => {
    const file = path.join(root, 'invalid.exe');
    await writeFile(file, 'This is not an executable.');
    await configure(file);
    await vscode.commands.executeCommand('kimi.restartServer');
    assert.equal(errors.length, 1, errors.join('\n'));
    assert.ok(errors.some(error => error.includes('Could not start') && error.includes('invalid.exe')));
  });
  test('recovers automatically after settings changes and publishes and clears diagnostics', async function () {
    const server = process.env.KIMI_TEST_SERVER_PATH;
    if (!server) {
      this.skip();
    }
    errors.length = 0;
    await vscode.workspace.getConfiguration('kimi').update('serverPath', server!, vscode.ConfigurationTarget.Global);
    const file = path.join(root, '日本語 Test #100%.kimi');
    await writeFile(file, '::Kimi.Console.writeLine(1 +');
    const uri = vscode.Uri.file(file);
    const document = await vscode.workspace.openTextDocument(uri);
    assert.equal(document.languageId, 'kimi');
    const editor = await vscode.window.showTextDocument(document);
    await waitForDiagnostics(uri, items => items.length > 0);
    await editor.edit(builder => builder.replace(new vscode.Range(document.positionAt(0), document.positionAt(document.getText().length)), '::Kimi.Console.writeLine("Hello")\n'));
    await waitForDiagnostics(uri, items => items.length === 0);
    assert.deepEqual(errors, []);
  });
});

async function waitFor(predicate: () => boolean, description: string): Promise<void> {
  const expires = Date.now() + 15000;
  while (Date.now() < expires) {
    if (predicate()) {
      return;
    }
    await new Promise(resolve => setTimeout(resolve, 50));
  }
  assert.fail(`Timed out waiting for ${description}`);
}

async function waitForDiagnostics(uri: vscode.Uri, predicate: (items: readonly vscode.Diagnostic[]) => boolean): Promise<void> {
  const expires = Date.now() + 15000;
  while (Date.now() < expires) {
    if (predicate(vscode.languages.getDiagnostics(uri))) {
      return;
    }
    await new Promise(resolve => setTimeout(resolve, 50));
  }
  assert.fail(`Timed out waiting for diagnostics for ${uri.fsPath}`);
}
