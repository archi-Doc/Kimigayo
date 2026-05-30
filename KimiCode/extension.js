const vscode = require("vscode");
const path = require("path");

const {
    LanguageClient,
    TransportKind
} = require("vscode-languageclient/node");

let client;

/**
 * @param {vscode.ExtensionContext} context
 */
function activate(context) {
    const serverDll = path.join(
        context.extensionPath,
        "../",
        "Kimi",
        "bin",
        "Debug",
        "net10.0",
        "Kimi.dll"
    );

    const serverOptions = {
        command: "dotnet",
        args: [
            serverDll,
            "lsp",
            "-DebugWait",
            "true"
        ],
        transport: TransportKind.stdio
    };

    const clientOptions = {
        documentSelector: [
            {
                scheme: "file",
                language: "kimi"
            },
            {
                scheme: "untitled",
                language: "kimi"
            }
        ],
        synchronize: {
            fileEvents: vscode.workspace.createFileSystemWatcher("**/*.kimi")
        }
    };

    client = new LanguageClient(
        "KimiCode",
        "Kimi Language Server",
        serverOptions,
        clientOptions
    );

    context.subscriptions.push(client.start());
}

async function deactivate() {
    if (client) {
        await client.stop();
        client = undefined;
    }
}

module.exports = {
    activate,
    deactivate
};
