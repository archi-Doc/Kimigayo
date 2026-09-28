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
exports.CommandRunner = void 0;
exports.isTargetFile = isTargetFile;
const promises_1 = require("node:fs/promises");
const path = __importStar(require("node:path"));
function isTargetFile(file) {
    const extension = path.extname(file).toLowerCase();
    return extension === '.kimi' || extension === '.kimiproj';
}
/** A directory's projects and implicit sources can write the same bin directory. */
class CommandRunner {
    services;
    busyDirectories = new Set();
    disposed = false;
    constructor(services) {
        this.services = services;
    }
    async run(action, input, signal) {
        let key;
        try {
            if (this.cancelled(signal)) {
                return 'cancelled';
            }
            if (!path.isAbsolute(input) || !isTargetFile(input)) {
                throw new Error('Select a .kimiproj project or a saved .kimi source file.');
            }
            const target = { file: path.normalize(input), directory: path.dirname(input) };
            const directory = await (0, promises_1.realpath)(target.directory);
            if (this.cancelled(signal)) {
                return 'cancelled';
            }
            const candidateKey = process.platform === 'win32' ? directory.toLowerCase() : directory;
            if (this.busyDirectories.has(candidateKey)) {
                this.services.reportError(`A Kimi task is already running in ${target.directory}. Finish or terminate it before retrying.`);
                return 'busy';
            }
            key = candidateKey;
            this.busyDirectories.add(key);
            // Snapshot once: a settings change while saving must not change the command plan.
            const runBuilds = this.services.runBuilds();
            if (action !== 'run' || runBuilds) {
                const saved = await this.services.save(target);
                if (this.cancelled(signal)) {
                    return 'cancelled';
                }
                if (!saved) {
                    this.services.reportError('The command was cancelled because a Kimi document could not be saved.');
                    return 'cancelled';
                }
            }
            if (!(await (0, promises_1.stat)(target.file)).isFile()) {
                throw new Error(`The selected target is not a file: ${target.file}`);
            }
            const executable = await this.services.resolveExecutable();
            const steps = action === 'buildAndRun'
                ? (runBuilds ? ['run'] : ['build', 'run']) : [action];
            for (const step of steps) {
                if (this.cancelled(signal)) {
                    return 'cancelled';
                }
                const exitCode = await this.services.execute(executable, step, target, signal);
                if (this.cancelled(signal) || exitCode === undefined) {
                    return 'cancelled';
                }
                if (exitCode !== 0) {
                    this.services.reportError(`Kimi ${step} failed (exit code ${exitCode}): ${target.file}. See the task terminal for details.`);
                    return 'failed';
                }
            }
            return 'succeeded';
        }
        catch (error) {
            if (!this.cancelled(signal)) {
                this.services.reportError(error instanceof Error ? error.message : String(error));
            }
            return this.cancelled(signal) ? 'cancelled' : 'failed';
        }
        finally {
            if (key !== undefined) {
                this.busyDirectories.delete(key);
            }
        }
    }
    dispose() {
        this.disposed = true;
    }
    cancelled(signal) {
        return this.disposed || signal?.aborted === true;
    }
}
exports.CommandRunner = CommandRunner;
//# sourceMappingURL=commandRunner.js.map