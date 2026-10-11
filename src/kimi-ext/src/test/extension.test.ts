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
  test('colors Kimi sources and Kimi blocks in Markdown with the contributed grammars', async () => {
    const source = path.join(root, 'Colors.kimi');
    await writeFile(source, 'func main()\n    let text = "Hello, \\(name)"\n');
    const sourceTokens = await captureSyntaxTokens(vscode.Uri.file(source));
    assert.ok(sourceTokens.some(token => token.c === 'func' && token.t.includes('storage.type.function.kimi')), JSON.stringify(sourceTokens));
    assert.ok(sourceTokens.some(token => token.c === 'name' && token.t.includes('meta.embedded.line.kimi')), JSON.stringify(sourceTokens));
    const markdown = path.join(root, 'Colors.md');
    await writeFile(markdown, '# Title\n\n```kimi\nlet total = 1\n```\n\nlet\n');
    const markdownTokens = await captureSyntaxTokens(vscode.Uri.file(markdown));
    const lets = markdownTokens.filter(token => token.c.trim() === 'let');
    assert.equal(lets.length, 2, JSON.stringify(markdownTokens));
    assert.ok(lets[0].t.includes('meta.embedded.block.kimi') && lets[0].t.includes('storage.type.binding.kimi'), JSON.stringify(lets));
    assert.ok(!lets[1].t.includes('storage.type.binding.kimi'), JSON.stringify(lets));
  });
  test('indents Kimi sources with four spaces', () => {
    const editor = vscode.workspace.getConfiguration('editor', { languageId: 'kimi' });
    assert.equal(editor.get('insertSpaces'), true);
    assert.equal(editor.get('tabSize'), 4);
    assert.equal(editor.get('detectIndentation'), false);
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
  test('shows server Hover Markdown and opens its mapped source-relative link without trusting commands', async function () {
    const server = process.env.KIMI_TEST_SERVER_PATH;
    if (!server) {
      this.skip();
    }
    await configure(server!);
    const file = path.join(root, 'Hover.kimi');
    const guide = path.join(root, 'guide #%.md');
    await writeFile(guide, '# Intro\n\nGuide body.\n');
    await writeFile(file, '/// See [guide](guide%20%23%25.md#intro) and [blocked](command:run).\nstruct Sample\npublic func main() => ()\n');
    const uri = vscode.Uri.file(file);
    await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(uri));
    let hover: vscode.Hover | undefined;
    const expires = Date.now() + 15000;
    while (Date.now() < expires) {
      const values = await vscode.commands.executeCommand<vscode.Hover[]>('vscode.executeHoverProvider', uri, new vscode.Position(1, 8));
      hover = values?.[0];
      if (hover) {
        break;
      }
      await new Promise(resolve => setTimeout(resolve, 50));
    }
    assert.ok(hover, 'No compiler Hover reached VS Code');
    assert.ok(hover.range?.isEqual(new vscode.Range(1, 7, 1, 13)), JSON.stringify(hover.range));
    const contents = hover.contents[0];
    assert.ok(contents instanceof vscode.MarkdownString);
    assert.ok(contents.value.includes('```kimi\nstruct Sample\n```'), contents.value);
    assert.ok(contents.value.includes('Copy: No'), contents.value);
    assert.ok(!contents.isTrusted, 'Compiler documentation must not enable trusted Markdown execution');
    assert.ok(!contents.value.includes('command:run'), contents.value);
    const match = contents.value.match(/\[guide\]\(<([^>]+)>\)/);
    assert.ok(match, contents.value);
    const destination = vscode.Uri.parse(match[1]);
    assert.equal(destination.scheme, 'file');
    assert.equal(destination.fsPath, vscode.Uri.file(guide).fsPath);
    assert.equal(destination.fragment, 'intro');
    await vscode.commands.executeCommand('vscode.open', destination);
    assert.equal(vscode.window.activeTextEditor?.document.uri.fsPath, vscode.Uri.file(guide).fsPath);
    await vscode.commands.executeCommand('workbench.action.closeAllEditors');
    assert.deepEqual(errors, []);
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

/** The tokens VS Code's TextMate tokenizer produces for a file; the command is the one VS Code's colorization tests use. */
async function captureSyntaxTokens(uri: vscode.Uri): Promise<{ c: string; t: string }[]> {
  return await vscode.commands.executeCommand('_workbench.captureSyntaxTokens', uri);
}

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
