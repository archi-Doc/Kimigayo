import * as path from 'node:path';
import { debug, DebugAdapter, DebugAdapterInlineImplementation, DebugConfiguration, DebugProtocolMessage, EventEmitter, ExtensionContext, Uri } from 'vscode';
import { CommandResult, isTargetFile } from './commandRunner';

type Run = (target: Uri | undefined, signal: AbortSignal) => Promise<CommandResult>;

interface Request {
  type: string;
  seq: number;
  command: string;
  arguments?: { noDebug?: boolean };
}

const defaults: DebugConfiguration = { type: 'kimi', request: 'launch', name: 'Kimi: Build and Run' };
const noDebugger = 'Kimi supports Run Without Debugging (Ctrl+F5). Breakpoints and stepping are not supported.';

/** Register the built-in Run Without Debugging flow, without overriding its keybinding. */
export function registerRunWithoutDebugging(context: ExtensionContext, run: Run, reportError: (message: string) => void): void {
  context.subscriptions.push(
    debug.registerDebugConfigurationProvider('kimi', {
      provideDebugConfigurations: () => [{ ...defaults, noDebug: true }],
      resolveDebugConfiguration: (_folder, config) => {
        if (config.noDebug !== true) {
          reportError(noDebugger);
          return undefined;
        }
        if (config.request && config.request !== 'launch') {
          reportError('Kimi supports launch configurations only.');
          return undefined;
        }
        return { ...defaults, ...config };
      },
      resolveDebugConfigurationWithSubstitutedVariables: (folder, config) => {
        if (config.program !== undefined) {
          if (typeof config.program !== 'string' || !config.program.trim() || !isTargetFile(config.program)) {
            reportError('Kimi launch program must be a .kimi or .kimiproj file path.');
            return undefined;
          }
          const program: string = config.program;
          if (process.platform === 'win32' && ((path.isAbsolute(program) && path.parse(program).root.length === 1) ||
              (!path.isAbsolute(program) && /^[a-z]:/i.test(program)))) {
            reportError('Kimi launch program must use a fully qualified path or a path relative to the workspace folder.');
            return undefined;
          }
          if (!path.isAbsolute(program) && !folder) {
            reportError('A relative Kimi launch program requires a workspace folder.');
            return undefined;
          }
          config.program = path.resolve(folder?.uri.fsPath ?? '', program);
        }
        return config;
      }
    }),
    debug.registerDebugAdapterDescriptorFactory('kimi', {
      createDebugAdapterDescriptor: session => new DebugAdapterInlineImplementation(new RunAdapter(
        signal => run(session.configuration.program === undefined ? undefined : Uri.file(session.configuration.program), signal)
      ))
    })
  );
}

/** A run-only session supplies lifecycle/Stop controls; Kimi still owns build and execution. */
class RunAdapter implements DebugAdapter {
  private readonly messages = new EventEmitter<DebugProtocolMessage>();
  readonly onDidSendMessage = this.messages.event;
  private readonly cancellation = new AbortController();
  private sequence = 0;
  private launched = false;
  private started = false;
  private ended = false;
  private disposed = false;

  constructor(private readonly run: (signal: AbortSignal) => Promise<CommandResult>) {}

  handleMessage(message: DebugProtocolMessage): void {
    const request = message as Request;
    if (this.disposed || request.type !== 'request') {
      return;
    }
    switch (request.command) {
      case 'initialize':
        this.respond(request, { supportsConfigurationDoneRequest: true, supportsTerminateRequest: true });
        break;
      case 'launch':
        if (this.launched || this.ended || request.arguments?.noDebug !== true) {
          this.respond(request, undefined, noDebugger);
          break;
        }
        this.launched = true;
        this.respond(request);
        this.event('initialized');
        break;
      case 'configurationDone':
        this.respond(request);
        if (this.launched && !this.started && !this.ended) {
          this.started = true;
          void this.run(this.cancellation.signal).catch(error => {
            this.event('output', { category: 'stderr', output: `${String(error)}\n` });
          }).finally(() => this.finish());
        }
        break;
      case 'terminate':
      case 'disconnect':
        this.cancellation.abort();
        this.respond(request);
        this.finish();
        break;
      case 'threads':
        this.respond(request, { threads: [] });
        break;
      case 'setExceptionBreakpoints':
        this.respond(request, { breakpoints: [] });
        break;
      default:
        this.respond(request, undefined, `Kimi does not support ${request.command} in Run Without Debugging.`);
    }
  }

  dispose(): void {
    this.disposed = true;
    this.cancellation.abort();
    this.messages.dispose();
  }

  private respond(request: Request, body?: unknown, error?: string): void {
    this.messages.fire({ seq: ++this.sequence, type: 'response', request_seq: request.seq, command: request.command,
      success: error === undefined, body, message: error });
  }

  private event(event: string, body?: unknown): void {
    if (!this.disposed) {
      this.messages.fire({ seq: ++this.sequence, type: 'event', event, body });
    }
  }

  private finish(): void {
    if (!this.ended) {
      this.ended = true;
      this.event('terminated');
    }
  }
}
