import * as assert from 'node:assert/strict';
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { tmpdir } from 'node:os';
import * as path from 'node:path';
import * as vscode from 'vscode';
import type { selectTarget as selectTargetFunction } from '../targetSelection';

type Item = vscode.QuickPickItem & { uri?: vscode.Uri };

suite('Kimi automatic target selection', () => {
  let api: typeof vscode;
  let selectTarget: typeof selectTargetFunction;
  let originalPick: typeof vscode.window.showQuickPick;
  let originalDialog: typeof vscode.window.showOpenDialog;
  let root: string;
  let otherRoot: string;
  let outside: string;
  let picked: Item[];

  suiteSetup(async () => {
    const extension = vscode.extensions.getExtension('local.kimi-ext');
    assert.ok(extension);
    await extension.activate();
    const require = createRequire(path.join(extension.extensionPath, 'package.json'));
    api = require('vscode') as typeof vscode;
    selectTarget = (require('./out/targetSelection.js') as { selectTarget: typeof selectTargetFunction }).selectTarget;
    originalPick = api.window.showQuickPick;
    originalDialog = api.window.showOpenDialog;
  });

  setup(async () => {
    await vscode.commands.executeCommand('workbench.action.closeAllEditors');
    root = await mkdtemp(path.join(vscode.workspace.workspaceFolders![0].uri.fsPath, 'targets-'));
    otherRoot = await mkdtemp(path.join(vscode.workspace.workspaceFolders![1].uri.fsPath, 'targets-'));
    outside = await mkdtemp(path.join(tmpdir(), 'kimi-ext-selection-'));
    picked = [];
    api.window.showQuickPick = (async (input: unknown) => {
      picked = [...await (input as Promise<Item[]>)];
      return undefined;
    }) as typeof vscode.window.showQuickPick;
    api.window.showOpenDialog = (async () => undefined) as typeof vscode.window.showOpenDialog;
  });

  teardown(async () => {
    await vscode.commands.executeCommand('workbench.action.closeAllEditors');
    api.window.showQuickPick = originalPick;
    api.window.showOpenDialog = originalDialog;
    assert.ok(path.resolve(root).startsWith(path.resolve(vscode.workspace.workspaceFolders![0].uri.fsPath) + path.sep));
    assert.ok(path.resolve(outside).startsWith(path.resolve(tmpdir()) + path.sep));
    assert.ok(path.resolve(otherRoot).startsWith(path.resolve(vscode.workspace.workspaceFolders![1].uri.fsPath) + path.sep));
    await rm(root, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 });
    await rm(outside, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 });
    await rm(otherRoot, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 });
  });

  async function file(name: string, active = false): Promise<vscode.Uri> {
    const uri = vscode.Uri.file(path.isAbsolute(name) ? name : path.join(root, name));
    await mkdir(path.dirname(uri.fsPath), { recursive: true });
    await writeFile(uri.fsPath, '');
    if (active) {
      await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(uri));
    }
    return uri;
  }

  test('an active project wins even with other projects present', async () => {
    await file('Other.kimiproj');
    const active = await file('App.kimiproj', true);
    assert.equal((await selectTarget())?.fsPath, active.fsPath);
    assert.deepEqual(picked, []);
  });

  test('a nearby project wins over other workspace projects and the source', async () => {
    await file('Other/Other.kimiproj');
    const project = await file('App/App.kimiproj');
    await file('App/Main.kimi', true);
    assert.equal((await selectTarget())?.fsPath, project.fsPath);
    assert.deepEqual(picked, []);
  });

  test('a unique workspace project is found without an active Kimi editor', async () => {
    const project = await file('App/App.KIMIPROJ');
    await file('Notes.txt', true);
    assert.equal((await selectTarget())?.fsPath, project.fsPath);
    assert.deepEqual(picked, []);
  });

  test('a saved source is selected when its workspace has no projects', async () => {
    const source = await file('Main.kimi', true);
    assert.equal((await selectTarget())?.fsPath, source.fsPath);
    assert.deepEqual(picked, []);
  });

  test('closed and deleted project documents cannot override a standalone source', async () => {
    const project = await file('Closed.kimiproj', true);
    await vscode.commands.executeCommand('workbench.action.closeAllEditors');
    await rm(project.fsPath);
    const source = await file('Main.kimi', true);
    assert.equal((await selectTarget())?.fsPath, source.fsPath);
    assert.deepEqual(picked, []);
  });

  test('outside-workspace sources do not select unrelated workspace or open projects', async () => {
    await file('Workspace.kimiproj', true);
    const source = await file(path.join(outside, 'Main.kimi'), true);
    assert.equal((await selectTarget())?.fsPath, source.fsPath);
    assert.deepEqual(picked, []);
  });

  test('multiple nearby projects prompt and cancellation returns no target', async () => {
    const first = await file('First.kimiproj');
    const second = await file('Second.kimiproj');
    const source = await file('Main.kimi', true);
    assert.equal(await selectTarget(), undefined);
    assert.deepEqual(picked.flatMap(item => item.uri ? [item.uri.fsPath] : []).sort(),
      [first.fsPath, second.fsPath, source.fsPath].sort());
  });

  test('multiple workspace projects also require selection', async () => {
    await file('One/App.kimiproj');
    await file('Two/App.kimiproj');
    await file('Main.kimi', true);
    assert.equal(await selectTarget(), undefined);
    assert.equal(picked.filter(item => item.description === 'Project').length, 2);
  });

  test('manual selection prompts even for the active unique project', async () => {
    const project = await file('App.kimiproj', true);
    assert.equal(await selectTarget(undefined, undefined, true), undefined);
    assert.equal(picked[0].uri?.fsPath, project.fsPath);
  });

  test('explicit source targets bypass nearby projects and pickers', async () => {
    await file('App.kimiproj', true);
    const source = await file('Main.kimi');
    assert.equal((await selectTarget(source))?.fsPath, source.fsPath);
    assert.deepEqual(picked, []);
  });

  test('excluded output directories do not make a workspace target ambiguous', async () => {
    const project = await file('App.kimiproj');
    await file('bin/Generated.kimiproj');
    await file('Notes.txt', true);
    assert.equal((await selectTarget())?.fsPath, project.fsPath);
    assert.deepEqual(picked, []);
  });

  test('cancellation prevents both automatic and explicit selection', async () => {
    const source = await file('Main.kimi', true);
    const cancellation = new vscode.CancellationTokenSource();
    try {
      cancellation.cancel();
      assert.equal(await selectTarget(undefined, cancellation.token), undefined);
      assert.equal(await selectTarget(source, cancellation.token), undefined);
      assert.deepEqual(picked, []);
    } finally {
      cancellation.dispose();
    }
  });

  test('cancellation while browsing ignores the chosen file', async () => {
    const source = await file('Main.kimi');
    const cancellation = new vscode.CancellationTokenSource();
    api.window.showOpenDialog = (async () => {
      cancellation.cancel();
      return [source];
    }) as typeof vscode.window.showOpenDialog;
    try {
      assert.equal(await selectTarget(undefined, cancellation.token), undefined);
    } finally {
      cancellation.dispose();
    }
  });

  test('the active workspace folder takes precedence in a multi-root workspace', async () => {
    const project = await file('App.kimiproj');
    await file(path.join(otherRoot, 'Other.kimiproj'), true);
    await file('Notes.txt', true);
    assert.equal((await selectTarget())?.fsPath, project.fsPath);
    assert.equal(picked.length, 0);
    await vscode.commands.executeCommand('workbench.action.closeAllEditors');
    assert.equal(await selectTarget(), undefined);
    assert.equal(picked.filter(item => item.description === 'Project').length, 2);
  });
});
