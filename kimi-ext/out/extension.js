"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.activate = activate;
exports.deactivate = deactivate;
const vscode_1 = require("vscode");
const node_1 = require("vscode-languageclient/node");
const serverPath_1 = require("./serverPath");
const serverManager_1 = require("./serverManager");
const documentUri_1 = require("./documentUri");
const commandRunner_1 = require("./commandRunner");
const targetSelection_1 = require("./targetSelection");
const taskExecution_1 = require("./taskExecution");
const runDebug_1 = require("./runDebug");
let manager;
let commandRunner;
let taskExecutor;
function isKimiDocument(document) {
    return document.uri.scheme === 'file' && (document.languageId === 'kimi' || document.languageId === 'kimiproj');
}
async function activate(context) {
    const output = vscode_1.window.createOutputChannel('Kimi');
    const reportError = (message) => {
        output.appendLine(message);
        void vscode_1.window.showErrorMessage(`Kimi: ${message}`, 'Open Settings', 'Show Output').then(async (action) => {
            if (action === 'Open Settings') {
                await vscode_1.commands.executeCommand('workbench.action.openSettings', 'kimi.serverPath');
            }
            else if (action === 'Show Output') {
                output.show(true);
            }
        }).then(undefined, error => output.appendLine(String(error)));
    };
    const reportCommandError = (message) => {
        if (message.includes('kimi.serverPath')) {
            reportError(message);
        }
        else {
            output.appendLine(message);
            void vscode_1.window.showErrorMessage(`Kimi: ${message}`);
        }
    };
    const executor = new taskExecution_1.TaskExecutor(message => output.appendLine(message));
    const runner = new commandRunner_1.CommandRunner({
        runBuilds: () => vscode_1.workspace.getConfiguration('kimi').get('runBuilds', false),
        save: targetSelection_1.saveKimiDocuments,
        resolveExecutable: () => (0, serverPath_1.resolveServerPath)(vscode_1.workspace.getConfiguration('kimi').get('serverPath')),
        execute: (executable, command, target, signal) => executor.execute(executable, command, target, signal),
        reportError: reportCommandError
    });
    taskExecutor = executor;
    commandRunner = runner;
    const runCommand = async (action, uri, signal, forceSelection = false) => {
        if (signal?.aborted) {
            return 'cancelled';
        }
        const selectionCancellation = signal && !uri ? new vscode_1.CancellationTokenSource() : undefined;
        const cancelSelection = () => selectionCancellation?.cancel();
        signal?.addEventListener('abort', cancelSelection, { once: true });
        try {
            if (!vscode_1.workspace.isTrusted) {
                throw new Error('Trust this workspace before building or running Kimi code.');
            }
            if (!vscode_1.workspace.workspaceFolders?.length) {
                const message = 'Open a folder in VS Code before using Kimi build/run/check commands. A .kimiproj file is not required.';
                output.appendLine(message);
                void vscode_1.window.showErrorMessage(`Kimi: ${message}`, 'Open Folder').then(selection => {
                    if (selection === 'Open Folder') {
                        return vscode_1.commands.executeCommand('workbench.action.files.openFolder');
                    }
                }).then(undefined, error => output.appendLine(String(error)));
                return 'failed';
            }
            const target = await (0, targetSelection_1.selectTarget)(uri, selectionCancellation?.token, forceSelection);
            return target && !signal?.aborted ? await runner.run(action, target.fsPath, signal) : 'cancelled';
        }
        catch (error) {
            if (signal?.aborted) {
                return 'cancelled';
            }
            reportCommandError(error instanceof Error ? error.message : String(error));
            return 'failed';
        }
        finally {
            signal?.removeEventListener('abort', cancelSelection);
            selectionCancellation?.dispose();
        }
    };
    for (const action of ['build', 'run', 'buildAndRun', 'check']) {
        context.subscriptions.push(vscode_1.commands.registerCommand(`kimi.${action}`, (uri) => runCommand(action, uri)), vscode_1.commands.registerCommand(`kimi.${action}WithTarget`, () => runCommand(action, undefined, undefined, true)));
    }
    (0, runDebug_1.registerRunWithoutDebugging)(context, (uri, signal) => runCommand('buildAndRun', uri, signal), reportCommandError);
    const server = new serverManager_1.ServerManager({
        resolvePath: () => (0, serverPath_1.resolveServerPath)(vscode_1.workspace.getConfiguration('kimi').get('serverPath')),
        createClient: executable => {
            const serverOptions = { command: executable, args: ['lsp'] };
            const clientOptions = {
                documentSelector: [
                    { scheme: 'file', language: 'kimi' },
                    { scheme: 'file', language: 'kimiproj' }
                ],
                initializationOptions: { checkQuietPeriodMs: 250 },
                uriConverters: { code2Protocol: documentUri_1.toProtocolUri, protocol2Code: value => vscode_1.Uri.parse(value) },
                outputChannel: output
            };
            return new node_1.LanguageClient('kimi', 'Kimi', serverOptions, clientOptions);
        },
        reportError,
        log: message => output.appendLine(message)
    });
    manager = server;
    let serverRequested = vscode_1.workspace.textDocuments.some(isKimiDocument);
    context.subscriptions.push(output, vscode_1.commands.registerCommand('kimi.restartServer', () => {
        serverRequested = true;
        return server.restart();
    }), vscode_1.workspace.onDidChangeConfiguration(event => {
        if (event.affectsConfiguration('kimi.serverPath')) {
            void server.restart(serverRequested);
        }
    }), vscode_1.workspace.onDidOpenTextDocument(document => {
        if (!serverRequested && isKimiDocument(document)) {
            serverRequested = true;
            void server.restart();
        }
    }));
    // Validate an explicitly configured user path even in a settings-only window.
    // Leave an unconfigured default alone until the user actually uses Kimi.
    if (serverRequested || vscode_1.workspace.getConfiguration('kimi').inspect('serverPath')?.globalValue !== undefined) {
        await server.restart(serverRequested);
    }
}
async function deactivate() {
    commandRunner?.dispose();
    taskExecutor?.dispose();
    commandRunner = undefined;
    taskExecutor = undefined;
    const server = manager;
    manager = undefined;
    await server?.dispose();
}
//# sourceMappingURL=extension.js.map