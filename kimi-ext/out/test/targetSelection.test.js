"use strict";
var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", { value: true });
const assert = __importStar(require("node:assert/strict"));
const promises_1 = require("node:fs/promises");
const node_module_1 = require("node:module");
const node_os_1 = require("node:os");
const path = __importStar(require("node:path"));
const vscode = __importStar(require("vscode"));
suite('Kimi automatic target selection', () => {
    let api;
    let selectTarget;
    let originalPick;
    let originalDialog;
    let root;
    let otherRoot;
    let outside;
    let picked;
    suiteSetup(async () => {
        const extension = vscode.extensions.getExtension('local.kimi-ext');
        assert.ok(extension);
        await extension.activate();
        const require = (0, node_module_1.createRequire)(path.join(extension.extensionPath, 'package.json'));
        api = require('vscode');
        selectTarget = require('./out/targetSelection.js').selectTarget;
        originalPick = api.window.showQuickPick;
        originalDialog = api.window.showOpenDialog;
    });
    setup(async () => {
        await vscode.commands.executeCommand('workbench.action.closeAllEditors');
        root = await (0, promises_1.mkdtemp)(path.join(vscode.workspace.workspaceFolders[0].uri.fsPath, 'targets-'));
        otherRoot = await (0, promises_1.mkdtemp)(path.join(vscode.workspace.workspaceFolders[1].uri.fsPath, 'targets-'));
        outside = await (0, promises_1.mkdtemp)(path.join((0, node_os_1.tmpdir)(), 'kimi-ext-selection-'));
        picked = [];
        api.window.showQuickPick = (async (input) => {
            picked = [...await input];
            return undefined;
        });
        api.window.showOpenDialog = (async () => undefined);
    });
    teardown(async () => {
        await vscode.commands.executeCommand('workbench.action.closeAllEditors');
        api.window.showQuickPick = originalPick;
        api.window.showOpenDialog = originalDialog;
        assert.ok(path.resolve(root).startsWith(path.resolve(vscode.workspace.workspaceFolders[0].uri.fsPath) + path.sep));
        assert.ok(path.resolve(outside).startsWith(path.resolve((0, node_os_1.tmpdir)()) + path.sep));
        assert.ok(path.resolve(otherRoot).startsWith(path.resolve(vscode.workspace.workspaceFolders[1].uri.fsPath) + path.sep));
        await (0, promises_1.rm)(root, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 });
        await (0, promises_1.rm)(outside, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 });
        await (0, promises_1.rm)(otherRoot, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 });
    });
    async function file(name, active = false) {
        const uri = vscode.Uri.file(path.isAbsolute(name) ? name : path.join(root, name));
        await (0, promises_1.mkdir)(path.dirname(uri.fsPath), { recursive: true });
        await (0, promises_1.writeFile)(uri.fsPath, '');
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
        await (0, promises_1.rm)(project.fsPath);
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
        assert.deepEqual(picked.flatMap(item => item.uri ? [item.uri.fsPath] : []).sort(), [first.fsPath, second.fsPath, source.fsPath].sort());
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
        }
        finally {
            cancellation.dispose();
        }
    });
    test('cancellation while browsing ignores the chosen file', async () => {
        const source = await file('Main.kimi');
        const cancellation = new vscode.CancellationTokenSource();
        api.window.showOpenDialog = (async () => {
            cancellation.cancel();
            return [source];
        });
        try {
            assert.equal(await selectTarget(undefined, cancellation.token), undefined);
        }
        finally {
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
//# sourceMappingURL=targetSelection.test.js.map