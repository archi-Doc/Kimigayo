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
exports.TaskExecutor = void 0;
const node_crypto_1 = require("node:crypto");
const path = __importStar(require("node:path"));
const vscode_1 = require("vscode");
/** Track process exits (not executeTask's launch acknowledgement) before advancing. */
class TaskExecutor {
    log;
    pending = new Set();
    disposed = false;
    constructor(log) {
        this.log = log;
    }
    async execute(executable, command, target, signal) {
        if (this.disposed || signal?.aborted) {
            return undefined;
        }
        const id = (0, node_crypto_1.randomUUID)();
        const task = new vscode_1.Task({ type: 'kimi', command, file: target.file, executionId: id }, vscode_1.workspace.getWorkspaceFolder(vscode_1.Uri.file(target.file)) ?? vscode_1.TaskScope.Global, `${command}: ${path.basename(target.file)}`, 'Kimi', new vscode_1.ProcessExecution(executable, [command, target.file], { cwd: target.directory }), []);
        if (command === 'build') {
            task.group = vscode_1.TaskGroup.Build;
        }
        task.presentationOptions = {
            reveal: vscode_1.TaskRevealKind.Always,
            focus: command === 'run',
            panel: vscode_1.TaskPanelKind.Shared,
            clear: false,
            showReuseMessage: true
        };
        const subscriptions = [];
        let resolve;
        const result = new Promise(done => { resolve = done; });
        const pending = {
            finish: code => {
                for (const subscription of subscriptions) {
                    subscription.dispose();
                }
                this.pending.delete(pending);
                resolve(code);
            }
        };
        // Keep the runner's directory lock until VS Code confirms that the task has ended.
        const cancel = () => pending.execution?.terminate();
        signal?.addEventListener('abort', cancel, { once: true });
        // Subscribe before launch so even a very short-lived process is observed.
        subscriptions.push(vscode_1.tasks.onDidEndTaskProcess(event => {
            if (event.execution.task.definition.executionId === id) {
                this.log(`Kimi ${command} process exited: ${event.exitCode ?? 'terminated'}`);
                pending.finish(event.exitCode);
            }
        }), vscode_1.tasks.onDidEndTask(event => {
            if (event.execution.task.definition.executionId === id) {
                this.log(`Kimi ${command} task ended without a process exit code.`);
                pending.finish(undefined);
            }
        }));
        this.pending.add(pending);
        try {
            if (signal?.aborted) {
                return undefined;
            }
            this.log(`Kimi ${command}: ${target.file}`);
            pending.execution = await vscode_1.tasks.executeTask(task);
            if (this.disposed) {
                pending.execution.terminate();
                pending.finish(undefined);
            }
            else if (signal?.aborted) {
                pending.execution.terminate();
            }
            return await result;
        }
        finally {
            signal?.removeEventListener('abort', cancel);
            pending.finish(undefined);
        }
    }
    dispose() {
        this.disposed = true;
        for (const pending of this.pending) {
            pending.execution?.terminate();
            pending.finish(undefined);
        }
    }
}
exports.TaskExecutor = TaskExecutor;
//# sourceMappingURL=taskExecution.js.map