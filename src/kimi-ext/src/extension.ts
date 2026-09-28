import { CancellationTokenSource, commands, ExtensionContext, TextDocument, Uri, window, workspace } from 'vscode';
import { CloseAction, ErrorAction, LanguageClient, LanguageClientOptions, RevealOutputChannelOn, ServerOptions } from 'vscode-languageclient/node';
import { resolveServerPath } from './serverPath';
import { ServerManager } from './serverManager';
import { toProtocolUri } from './documentUri';
import { CommandResult, CommandRunner, KimiAction } from './commandRunner';
import { selectTarget, saveKimiDocuments } from './targetSelection';
import { TaskExecutor } from './taskExecution';
import { registerRunWithoutDebugging } from './runDebug';

let manager: ServerManager | undefined;
let commandRunner: CommandRunner | undefined;
let taskExecutor: TaskExecutor | undefined;

/** Keep library diagnostics in the output; Kimi owns the single actionable notification. */
class KimiLanguageClient extends LanguageClient {
  override error(message: string, data?: unknown): void {
    super.error(message, data, false);
  }
}

function isKimiDocument(document: TextDocument): boolean {
  return document.uri.scheme === 'file' && (document.languageId === 'kimi' || document.languageId === 'kimiproj');
}

export async function activate(context: ExtensionContext): Promise<void> {
  const output = window.createOutputChannel('Kimi');
  let serverPath = workspace.getConfiguration('kimi').get<unknown>('serverPath');
  let serverErrorReported = false;
  const currentServerPath = (): unknown => {
    const value = workspace.getConfiguration('kimi').get<unknown>('serverPath');
    if (!Object.is(value, serverPath)) {
      serverPath = value;
      serverErrorReported = false;
    }
    return value;
  };
  const reportError = (message: string): void => {
    output.appendLine(message);
    currentServerPath();
    if (serverErrorReported) {
      return;
    }
    serverErrorReported = true;
    void window.showErrorMessage(`Kimi: ${message}`, 'Open Settings', 'Show Output').then(async action => {
      if (action === 'Open Settings') {
        await commands.executeCommand('workbench.action.openSettings', 'kimi.serverPath');
      } else if (action === 'Show Output') {
        output.show(true);
      }
    }).then(undefined, error => output.appendLine(String(error)));
  };

  const reportCommandError = (message: string): void => {
    if (message.includes('kimi.serverPath')) {
      reportError(message);
    } else {
      output.appendLine(message);
      void window.showErrorMessage(`Kimi: ${message}`);
    }
  };
  const executor = new TaskExecutor(message => output.appendLine(message));
  const runner = new CommandRunner({
    runBuilds: () => workspace.getConfiguration('kimi').get('runBuilds', false),
    save: saveKimiDocuments,
    resolveExecutable: () => resolveServerPath(currentServerPath()),
    execute: (executable, command, target, signal) => executor.execute(executable, command, target, signal),
    reportError: reportCommandError
  });
  taskExecutor = executor;
  commandRunner = runner;
  const runCommand = async (action: KimiAction, uri?: Uri, signal?: AbortSignal, forceSelection = false): Promise<CommandResult> => {
    if (signal?.aborted) {
      return 'cancelled';
    }
    const selectionCancellation = signal && !uri ? new CancellationTokenSource() : undefined;
    const cancelSelection = (): void => selectionCancellation?.cancel();
    signal?.addEventListener('abort', cancelSelection, { once: true });
    try {
      if (!workspace.isTrusted) {
        throw new Error('Trust this workspace before building or running Kimi code.');
      }
      if (!workspace.workspaceFolders?.length) {
        const message = 'Open a folder in VS Code before using Kimi build/run/check commands. A .kimiproj file is not required.';
        output.appendLine(message);
        void window.showErrorMessage(`Kimi: ${message}`, 'Open Folder').then(selection => {
          if (selection === 'Open Folder') {
            return commands.executeCommand('workbench.action.files.openFolder');
          }
        }).then(undefined, error => output.appendLine(String(error)));
        return 'failed';
      }
      const target = await selectTarget(uri, selectionCancellation?.token, forceSelection);
      return target && !signal?.aborted ? await runner.run(action, target.fsPath, signal) : 'cancelled';
    } catch (error) {
      if (signal?.aborted) {
        return 'cancelled';
      }
      reportCommandError(error instanceof Error ? error.message : String(error));
      return 'failed';
    } finally {
      signal?.removeEventListener('abort', cancelSelection);
      selectionCancellation?.dispose();
    }
  };
  for (const action of ['build', 'run', 'buildAndRun', 'check'] as const) {
    context.subscriptions.push(
      commands.registerCommand(`kimi.${action}`, (uri?: Uri) => runCommand(action, uri)),
      commands.registerCommand(`kimi.${action}WithTarget`, () => runCommand(action, undefined, undefined, true))
    );
  }
  registerRunWithoutDebugging(context, (uri, signal) => runCommand('buildAndRun', uri, signal), reportCommandError);

  const server = new ServerManager({
    resolvePath: () => resolveServerPath(currentServerPath()),
    createClient: executable => {
      const configuration = currentServerPath();
      const reportConnectionError = (message: string): void => {
        if (Object.is(configuration, currentServerPath())) {
          reportError(`Language server "${executable}" (kimi.serverPath): ${message}`);
        }
      };
      const serverOptions: ServerOptions = { command: executable, args: ['lsp'] };
      const clientOptions: LanguageClientOptions = {
        documentSelector: [
          { scheme: 'file', language: 'kimi' },
          { scheme: 'file', language: 'kimiproj' }
        ],
        initializationOptions: { checkQuietPeriodMs: 250 },
        uriConverters: { code2Protocol: toProtocolUri, protocol2Code: value => Uri.parse(value) },
        outputChannel: output,
        // ServerManager reports startup failures; avoid a second languageclient popup.
        revealOutputChannelOn: RevealOutputChannelOn.Never,
        initializationFailedHandler: () => false,
        errorHandler: {
          error: error => {
            reportConnectionError(error.message);
            return { action: ErrorAction.Shutdown, handled: true };
          },
          closed: () => {
            reportConnectionError('The connection closed. Check the Kimi output, then use Kimi: Restart Language Server to retry.');
            return { action: CloseAction.DoNotRestart, handled: true };
          }
        }
      };
      return new KimiLanguageClient('kimi', 'Kimi', serverOptions, clientOptions);
    },
    reportError,
    log: message => output.appendLine(message)
  });
  manager = server;
  let serverRequested = workspace.textDocuments.some(isKimiDocument);
  context.subscriptions.push(
    output,
    commands.registerCommand('kimi.restartServer', () => {
      serverRequested = true;
      return server.restart();
    }),
    workspace.onDidChangeConfiguration(event => {
      if (event.affectsConfiguration('kimi.serverPath')) {
        currentServerPath();
        void server.restart(serverRequested);
      }
    }),
    workspace.onDidOpenTextDocument(document => {
      if (!serverRequested && isKimiDocument(document)) {
        serverRequested = true;
        void server.restart();
      }
    })
  );
  // Validate an explicitly configured user path even in a settings-only window.
  // Leave an unconfigured default alone until the user actually uses Kimi.
  if (serverRequested || workspace.getConfiguration('kimi').inspect('serverPath')?.globalValue !== undefined) {
    await server.restart(serverRequested);
  }
}

export async function deactivate(): Promise<void> {
  commandRunner?.dispose();
  taskExecutor?.dispose();
  commandRunner = undefined;
  taskExecutor = undefined;
  const server = manager;
  manager = undefined;
  await server?.dispose();
}
