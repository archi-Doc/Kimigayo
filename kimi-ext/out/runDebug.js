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
exports.registerRunWithoutDebugging = registerRunWithoutDebugging;
const path = __importStar(require("node:path"));
const vscode_1 = require("vscode");
const commandRunner_1 = require("./commandRunner");
const defaults = { type: 'kimi', request: 'launch', name: 'Kimi: Build and Run' };
const noDebugger = 'Kimi supports Run Without Debugging (Ctrl+F5). Breakpoints and stepping are not supported.';
/** Register the built-in Run Without Debugging flow, without overriding its keybinding. */
function registerRunWithoutDebugging(context, run, reportError) {
    context.subscriptions.push(vscode_1.debug.registerDebugConfigurationProvider('kimi', {
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
                if (typeof config.program !== 'string' || !config.program.trim() || !(0, commandRunner_1.isTargetFile)(config.program)) {
                    reportError('Kimi launch program must be a .kimi or .kimiproj file path.');
                    return undefined;
                }
                const program = config.program;
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
    }), vscode_1.debug.registerDebugAdapterDescriptorFactory('kimi', {
        createDebugAdapterDescriptor: session => new vscode_1.DebugAdapterInlineImplementation(new RunAdapter(signal => run(session.configuration.program === undefined ? undefined : vscode_1.Uri.file(session.configuration.program), signal)))
    }));
}
/** A run-only session supplies lifecycle/Stop controls; Kimi still owns build and execution. */
class RunAdapter {
    run;
    messages = new vscode_1.EventEmitter();
    onDidSendMessage = this.messages.event;
    cancellation = new AbortController();
    sequence = 0;
    launched = false;
    started = false;
    ended = false;
    disposed = false;
    constructor(run) {
        this.run = run;
    }
    handleMessage(message) {
        const request = message;
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
    dispose() {
        this.disposed = true;
        this.cancellation.abort();
        this.messages.dispose();
    }
    respond(request, body, error) {
        this.messages.fire({ seq: ++this.sequence, type: 'response', request_seq: request.seq, command: request.command,
            success: error === undefined, body, message: error });
    }
    event(event, body) {
        if (!this.disposed) {
            this.messages.fire({ seq: ++this.sequence, type: 'event', event, body });
        }
    }
    finish() {
        if (!this.ended) {
            this.ended = true;
            this.event('terminated');
        }
    }
}
//# sourceMappingURL=runDebug.js.map