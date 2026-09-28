import * as assert from 'node:assert/strict';
import { mkdtemp, mkdir, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { createRequire } from 'node:module';
import * as path from 'node:path';
import * as vscode from 'vscode';

suite('Kimi build and run integration', () => {
  let api: typeof vscode;
  let root: string;
  let file: string;
  let project: string;
  let buildFile: string;
  let originalPath: unknown;
  let originalTaskSave: unknown;
  let originalRunBuilds: unknown;
  let originalError: typeof vscode.window.showErrorMessage;
  let originalPick: typeof vscode.window.showQuickPick;
  let originalDialog: typeof vscode.window.showOpenDialog;
  let taskEvents: vscode.Disposable;
  let debugEvents: vscode.Disposable;
  const errors: string[] = [];
  const started: vscode.Task[] = [];
  const sessions: vscode.DebugSession[] = [];
  const endedSessions: string[] = [];

  suiteSetup(async function () {
    if (!process.env.KIMI_TEST_SERVER_PATH) {
      this.skip();
    }
    root = await mkdtemp(path.join(tmpdir(), 'kimi-ext-tasks-'));
    const directory = path.join(root, '日本語 space & #100%');
    await mkdir(directory);
    file = vscode.Uri.file(path.join(directory, 'Hello.kimi')).fsPath;
    project = vscode.Uri.file(path.join(directory, 'Hello.kimiproj')).fsPath;
    await writeFile(file, '::Kimi.Console.writeLine("Hello from Kimi Extension")\n');
    await writeFile(project, 'Targets=\n  "x86_64-pc-windows-msvc"\nOutputKind="Application"\nOptimization="O2"\n');
    const buildDirectory = path.join(root, 'build 日本語 space & #100');
    await mkdir(buildDirectory);
    buildFile = vscode.Uri.file(path.join(buildDirectory, 'Hello.kimi')).fsPath;
    await writeFile(buildFile, '::Kimi.Console.writeLine("Hello from Build and Run")\n');
    const extension = vscode.extensions.getExtension('local.kimi-ext');
    const expires = Date.now() + 15000;
    while (!extension?.isActive && Date.now() < expires) {
      await new Promise(resolve => setTimeout(resolve, 50));
    }
    assert.ok(extension?.isActive);
    api = createRequire(path.join(extension.extensionPath, 'package.json'))('vscode') as typeof vscode;
    originalError = api.window.showErrorMessage;
    originalPick = api.window.showQuickPick;
    originalDialog = api.window.showOpenDialog;
    originalPath = vscode.workspace.getConfiguration('kimi').inspect('serverPath')?.globalValue;
    originalTaskSave = vscode.workspace.getConfiguration('task').inspect('saveBeforeRun')?.globalValue;
    originalRunBuilds = vscode.workspace.getConfiguration('kimi').inspect('runBuilds')?.globalValue;
    await vscode.workspace.getConfiguration('kimi').update('runBuilds', false, vscode.ConfigurationTarget.Global);
    // VS Code otherwise saves every dirty editor independently of the extension.
    // Disable that in this isolated profile to verify the extension's own save scope.
    await vscode.workspace.getConfiguration('task').update('saveBeforeRun', 'never', vscode.ConfigurationTarget.Global);
    api.window.showErrorMessage = ((text: string) => {
      errors.push(text);
      return Promise.resolve(undefined);
    }) as typeof vscode.window.showErrorMessage;
    taskEvents = vscode.tasks.onDidStartTask(event => {
      if (event.execution.task.definition.type === 'kimi') {
        started.push(event.execution.task);
      }
    });
    debugEvents = vscode.Disposable.from(
      vscode.debug.onDidStartDebugSession(session => { if (session.type === 'kimi') { sessions.push(session); } }),
      vscode.debug.onDidTerminateDebugSession(session => { if (session.type === 'kimi') { endedSessions.push(session.id); } })
    );
    await vscode.workspace.getConfiguration('kimi').update('serverPath', process.env.KIMI_TEST_SERVER_PATH, vscode.ConfigurationTarget.Global);
    await vscode.commands.executeCommand('workbench.action.closeAllEditors');
  });

  setup(() => {
    errors.length = 0;
    started.length = 0;
    sessions.length = 0;
    endedSessions.length = 0;
  });

  teardown(() => {
    if (api) {
      api.window.showQuickPick = originalPick;
      api.window.showOpenDialog = originalDialog;
    }
  });

  suiteTeardown(async () => {
    if (!api) {
      return;
    }
    try {
      await vscode.debug.stopDebugging();
      await vscode.commands.executeCommand('workbench.action.closeAllEditors');
      await vscode.workspace.getConfiguration('kimi').update('serverPath', originalPath, vscode.ConfigurationTarget.Global);
      await vscode.workspace.getConfiguration('task').update('saveBeforeRun', originalTaskSave, vscode.ConfigurationTarget.Global);
      await vscode.workspace.getConfiguration('kimi').update('runBuilds', originalRunBuilds, vscode.ConfigurationTarget.Global);
      // Task terminals can briefly retain their working directory after Stop on Windows.
      // This host/profile is isolated; all terminals here were created by these tests.
      for (const terminal of vscode.window.terminals) {
        terminal.dispose();
      }
      assert.ok(path.resolve(root).startsWith(path.resolve(tmpdir()) + path.sep));
      await rm(root, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 });
    } finally {
      taskEvents.dispose();
      debugEvents.dispose();
      api.window.showErrorMessage = originalError;
    }
  });

  test('registers automatic and manual commands under the renamed extension identity', async () => {
    const commands = await vscode.commands.getCommands(true);
    for (const action of ['build', 'run', 'buildAndRun', 'check']) {
      assert.ok(commands.includes(`kimi.${action}`));
      assert.ok(commands.includes(`kimi.${action}WithTarget`));
    }
    assert.equal(vscode.extensions.getExtension('local.kimi-ext')?.packageJSON.name, 'kimi-ext');
  });

  test('Check saves dirty input and launches a process task with intact arguments', async () => {
    const document = await vscode.workspace.openTextDocument(file);
    const editor = await vscode.window.showTextDocument(document);
    const source = '::Kimi.Console.writeLine("Saved by Check")\n';
    assert.ok(await editor.edit(builder => builder.replace(new vscode.Range(document.positionAt(0), document.positionAt(document.getText().length)), source)));
    assert.ok(document.isDirty);
    assert.equal(await vscode.commands.executeCommand('kimi.check', vscode.Uri.file(file)), 'succeeded');
    assert.equal(await readFile(file, 'utf8'), source);
    assert.equal(document.isDirty, false);
    assert.equal(started.length, 1);
    const execution = started[0].execution as vscode.ProcessExecution;
    assert.ok(execution instanceof vscode.ProcessExecution);
    assert.deepEqual(execution.args, ['check', file]);
    assert.equal(execution.options?.cwd, path.dirname(file));
  });

  test('single-source Check leaves unrelated dirty Kimi files unsaved', async () => {
    const unrelated = path.join(root, 'Unrelated.kimi');
    const original = '::Kimi.Console.writeLine("Original")\n';
    await writeFile(unrelated, original);
    const document = await vscode.workspace.openTextDocument(unrelated);
    const editor = await vscode.window.showTextDocument(document);
    await editor.edit(builder => builder.insert(new vscode.Position(0, 0), '// Unsaved work\n'));
    try {
      assert.equal(await vscode.commands.executeCommand('kimi.check', vscode.Uri.file(file)), 'succeeded');
      assert.equal(await readFile(unrelated, 'utf8'), original);
      assert.ok(document.isDirty);
    } finally {
      await editor.edit(builder => builder.replace(new vscode.Range(document.positionAt(0), document.positionAt(document.getText().length)), original));
      await document.save();
      await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(file));
    }
  });

  test('an active untitled Kimi document does not block project selection', async () => {
    const projectDocument = await vscode.workspace.openTextDocument(project);
    const projectEditor = await vscode.window.showTextDocument(projectDocument);
    await projectEditor.edit(builder => builder.insert(new vscode.Position(0, 0), '\n'));
    const source = await vscode.workspace.openTextDocument(file);
    const sourceEditor = await vscode.window.showTextDocument(source);
    await sourceEditor.edit(builder => builder.insert(new vscode.Position(0, 0), '// Saved with project\n'));
    const document = await vscode.workspace.openTextDocument({ language: 'kimi', content: '// Unnamed draft\n' });
    await vscode.window.showTextDocument(document);
    let picked = false;
    api.window.showQuickPick = (async (input: unknown) => {
      const items = await (input as Promise<readonly (vscode.QuickPickItem & { uri?: vscode.Uri })[]>);
      picked = true;
      return items.find(item => item.uri?.fsPath === project);
    }) as unknown as typeof vscode.window.showQuickPick;
    try {
      assert.equal(await vscode.commands.executeCommand('kimi.check'), 'succeeded');
      assert.equal(picked, false);
      assert.equal(started[0].definition.file, project);
      assert.ok(document.isUntitled && document.isDirty);
      assert.equal(source.isDirty, false);
      assert.equal(projectDocument.isDirty, false);
    } finally {
      await vscode.commands.executeCommand('workbench.action.revertAndCloseActiveEditor');
      await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(file));
    }
  });

  test('target picker distinguishes the current source from a nearby project', async () => {
    await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(file));
    let picked = false;
    api.window.showQuickPick = (async (input: unknown) => {
      const items = await (input as Promise<readonly (vscode.QuickPickItem & { uri?: vscode.Uri })[]>);
      assert.ok(items.some(item => item.uri?.fsPath === file && item.description === 'Single source file only'));
      const selected = items.find(item => item.uri?.fsPath === project);
      assert.ok(selected);
      assert.equal(selected.description, 'Project');
      picked = true;
      return selected;
    }) as unknown as typeof vscode.window.showQuickPick;
    assert.equal(await vscode.commands.executeCommand('kimi.checkWithTarget'), 'succeeded');
    assert.ok(picked);
    assert.equal(started[0].definition.file, project);
  });

  test('cancelling target selection does not launch a process', async () => {
    api.window.showQuickPick = (async () => undefined) as typeof vscode.window.showQuickPick;
    assert.equal(await vscode.commands.executeCommand('kimi.buildWithTarget'), 'cancelled');
    assert.deepEqual(started, []);
  });

  test('Build and Run waits for a real successful build then runs the result', async function () {
    this.timeout(60000);
    assert.equal(await vscode.commands.executeCommand('kimi.buildAndRun', vscode.Uri.file(buildFile)), 'succeeded');
    assert.deepEqual(started.map(task => task.definition.command), ['build', 'run']);
    const output = await readdir(path.join(path.dirname(buildFile), 'bin', 'x86_64-pc-windows-msvc'));
    assert.ok(output.some(name => name.endsWith('.exe')));
  });

  test('Run forwards to the CLI without a separate extension build task', async () => {
    assert.equal(await vscode.commands.executeCommand('kimi.run', vscode.Uri.file(buildFile)), 'succeeded');
    assert.deepEqual(started.map(task => task.definition.command), ['run']);
  });

  test('an automatic palette command prefers a unique project beside the active source', async () => {
    await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(file));
    api.window.showQuickPick = (async () => { assert.fail('An unambiguous target must not prompt'); }) as typeof vscode.window.showQuickPick;
    assert.equal(await vscode.commands.executeCommand('kimi.check'), 'succeeded');
    assert.equal(started[0].definition.file, project);
  });

  test('runBuilds builds a fresh source through one run task for palette and Ctrl+F5', async function () {
    this.timeout(60000);
    const directory = path.join(root, 'run-builds');
    await mkdir(directory);
    const source = path.join(directory, 'Fresh.kimi');
    await writeFile(source, '::Kimi.Console.writeLine("Built by kimi run")\n');
    await vscode.workspace.getConfiguration('kimi').update('runBuilds', true, vscode.ConfigurationTarget.Global);
    try {
      assert.equal(await vscode.commands.executeCommand('kimi.buildAndRun', vscode.Uri.file(source)), 'succeeded');
      assert.ok((await readdir(path.join(directory, 'bin', 'x86_64-pc-windows-msvc'))).some(name => name.endsWith('.exe')));
      assert.ok(await vscode.debug.startDebugging(undefined, {
        type: 'kimi', request: 'launch', name: 'Build-capable run', program: source
      }, { noDebug: true }));
      await waitFor(() => endedSessions.length === 1, 'single-run session completion');
      assert.deepEqual(started.map(task => task.definition.command), ['run', 'run']);
      assert.deepEqual(errors, []);
    } finally {
      await vscode.workspace.getConfiguration('kimi').update('runBuilds', false, vscode.ConfigurationTarget.Global);
    }
  });

  test('the built-in Run Without Debugging command builds and runs without launch.json', async function () {
    this.timeout(60000);
    await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(buildFile));
    api.window.showQuickPick = (async () => { assert.fail('A standalone source must not prompt'); }) as typeof vscode.window.showQuickPick;
    await vscode.commands.executeCommand('workbench.action.debug.run');
    await waitFor(() => endedSessions.length === 1, 'Ctrl+F5 session completion');
    assert.equal(sessions.length, 1);
    assert.equal(sessions[0].configuration.noDebug, true);
    assert.deepEqual(started.map(task => task.definition.command), ['build', 'run']);
    assert.deepEqual(errors, []);
  });

  test('a launch program supports VS Code variables and workspace-relative paths', async function () {
    this.timeout(60000);
    await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(buildFile));
    const folder = vscode.workspace.workspaceFolders![0];
    const buildProject = path.join(path.dirname(buildFile), 'Hello.kimiproj');
    await writeFile(buildProject, await readFile(project, 'utf8'));
    for (const program of ['${file}', path.relative(folder.uri.fsPath, buildFile), buildProject]) {
      const endCount = endedSessions.length;
      assert.ok(await vscode.debug.startDebugging(folder, {
        type: 'kimi', request: 'launch', name: 'Kimi configured run', program
      }, { noDebug: true }));
      await waitFor(() => endedSessions.length === endCount + 1, 'configured run completion');
      assert.equal(sessions[endCount].configuration.program.toLowerCase(),
        (program === buildProject ? buildProject : buildFile).toLowerCase());
    }
    assert.deepEqual(started.map(task => task.definition.command), ['build', 'run', 'build', 'run', 'build', 'run']);
    assert.deepEqual(errors, []);
  });

  test('cancelling the run target picker ends the session without starting a task', async () => {
    await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(buildFile));
    const cleanUp = await makeAmbiguousProjects();
    api.window.showQuickPick = (async () => undefined) as typeof vscode.window.showQuickPick;
    try {
      assert.ok(await vscode.debug.startDebugging(undefined, {
        type: 'kimi', request: 'launch', name: 'Cancelled Kimi run'
      }, { noDebug: true }));
      await waitFor(() => endedSessions.length === 1, 'cancelled run completion');
      assert.deepEqual(started, []);
    } finally {
      await cleanUp();
    }
  });

  test('a debugging request is rejected with Ctrl+F5 guidance', async () => {
    assert.equal(await vscode.debug.startDebugging(undefined, {
      type: 'kimi', request: 'launch', name: 'Unsupported debugging', program: buildFile
    }), false);
    assert.ok(errors.some(error => error.includes('Ctrl+F5')));
    assert.deepEqual(started, []);
  });

  test('Stop cancels target selection before any task can start', async () => {
    await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(buildFile));
    const cleanUp = await makeAmbiguousProjects();
    let picking = false;
    api.window.showQuickPick = ((_input: unknown, _options: unknown, token: vscode.CancellationToken) => {
      picking = true;
      return new Promise(resolve => {
        const cancellation = token.onCancellationRequested(() => { cancellation.dispose(); resolve(undefined); });
      });
    }) as typeof vscode.window.showQuickPick;
    try {
      assert.ok(await vscode.debug.startDebugging(undefined, {
        type: 'kimi', request: 'launch', name: 'Kimi target selection'
      }, { noDebug: true }));
      await waitFor(() => picking, 'target picker');
      await vscode.debug.stopDebugging(sessions[0]);
      await waitFor(() => endedSessions.length === 1, 'cancelled target selection');
      assert.deepEqual(started, []);
    } finally {
      await cleanUp();
    }
  });

  test('invalid launch programs are rejected before any task starts', async () => {
    const programs: unknown[] = [42, 'Notes.txt'];
    if (process.platform === 'win32') {
      programs.push('C:Hello.kimi', '\\Hello.kimi');
    }
    for (const program of programs) {
      assert.equal(await vscode.debug.startDebugging(vscode.workspace.workspaceFolders![0], {
        type: 'kimi', request: 'launch', name: 'Invalid Kimi target', program
      }, { noDebug: true }), false);
    }
    assert.equal(errors.length, programs.length);
    assert.deepEqual(started, []);
  });

  test('Stop terminates the running task and ends the run session', async function () {
    this.timeout(60000);
    const directory = path.join(root, 'stoppable');
    await mkdir(directory);
    const loop = path.join(directory, 'Loop.kimi');
    await writeFile(loop, '::Kimi.Console.writeLine("Running until Stop")\nloop\n    if false => exit\n');
    let runExecution: vscode.TaskExecution | undefined;
    const observe = vscode.tasks.onDidStartTaskProcess(event => {
      if (event.execution.task.definition.type === 'kimi' && event.execution.task.definition.command === 'run') {
        runExecution = event.execution;
      }
    });
    try {
      assert.ok(await vscode.debug.startDebugging(undefined, {
        type: 'kimi', request: 'launch', name: 'Stoppable Kimi run', program: loop
      }, { noDebug: true }));
      await waitFor(() => runExecution !== undefined || endedSessions.length > 0, 'running application');
      assert.ok(runExecution, `Application did not start: ${errors.join('; ')}`);
      await vscode.debug.stopDebugging(sessions[0]);
      await waitFor(() => endedSessions.length === 1 && !vscode.tasks.taskExecutions.some(task => task === runExecution), 'Stop cleanup');
      assert.deepEqual(started.map(task => task.definition.command), ['build', 'run']);
    } finally {
      if (runExecution && vscode.tasks.taskExecutions.includes(runExecution)) {
        runExecution.terminate();
      }
      observe.dispose();
    }
  });

  test('Stop during Build prevents the Run step', async () => {
    let buildExecution: vscode.TaskExecution | undefined;
    const observe = vscode.tasks.onDidStartTaskProcess(event => {
      if (event.execution.task.definition.type === 'kimi' && event.execution.task.definition.command === 'build') {
        buildExecution = event.execution;
      }
    });
    try {
      assert.ok(await vscode.debug.startDebugging(undefined, {
        type: 'kimi', request: 'launch', name: 'Stopped Kimi build', program: buildFile
      }, { noDebug: true }));
      await waitFor(() => buildExecution !== undefined, 'build start');
      await vscode.debug.stopDebugging(sessions[0]);
      await waitFor(() => endedSessions.length === 1 && !vscode.tasks.taskExecutions.includes(buildExecution!), 'build stop');
      assert.deepEqual(started.map(task => task.definition.command), ['build']);
    } finally {
      if (buildExecution && vscode.tasks.taskExecutions.includes(buildExecution)) {
        buildExecution.terminate();
      }
      observe.dispose();
    }
  });

  test('failed Build and Run never launches the old executable', async () => {
    const document = await vscode.workspace.openTextDocument(buildFile);
    const editor = await vscode.window.showTextDocument(document);
    await editor.edit(builder => builder.replace(new vscode.Range(document.positionAt(0), document.positionAt(document.getText().length)), '::Kimi.Console.writeLine("broken" +'));
    assert.equal(await vscode.commands.executeCommand('kimi.buildAndRun', vscode.Uri.file(buildFile)), 'failed');
    assert.deepEqual(started.map(task => task.definition.command), ['build']);
    assert.ok(errors.some(error => error.includes('build failed')));
  });

  test('Run Without Debugging does not run an old executable when the build fails', async () => {
    assert.ok(await vscode.debug.startDebugging(undefined, {
      type: 'kimi', request: 'launch', name: 'Failing Kimi run', program: buildFile
    }, { noDebug: true }));
    await waitFor(() => endedSessions.length === 1, 'failed run completion');
    assert.deepEqual(started.map(task => task.definition.command), ['build']);
    assert.ok(errors.some(error => error.includes('build failed')));
  });

  test('invalid server paths prevent task creation and offer configuration errors', async () => {
    await vscode.workspace.getConfiguration('kimi').update('serverPath', path.join(root, 'missing.exe'), vscode.ConfigurationTarget.Global);
    assert.equal(await vscode.commands.executeCommand('kimi.check', vscode.Uri.file(file)), 'failed');
    assert.deepEqual(started, []);
    assert.ok(errors.some(error => error.includes('kimi.serverPath') && error.includes('missing.exe')));
    await vscode.workspace.getConfiguration('kimi').update('serverPath', process.env.KIMI_TEST_SERVER_PATH, vscode.ConfigurationTarget.Global);
  });

  async function makeAmbiguousProjects(): Promise<() => Promise<void>> {
    const files = ['First.kimiproj', 'Second.kimiproj'].map(name => path.join(path.dirname(buildFile), name));
    for (const candidate of files) {
      await writeFile(candidate, await readFile(project, 'utf8'));
    }
    return async () => { for (const candidate of files) { await rm(candidate); } };
  }
});

async function waitFor(predicate: () => boolean, description: string): Promise<void> {
  const expires = Date.now() + 30000;
  while (Date.now() < expires) {
    if (predicate()) {
      return;
    }
    await new Promise(resolve => setTimeout(resolve, 25));
  }
  assert.fail(`Timed out waiting for ${description}`);
}
