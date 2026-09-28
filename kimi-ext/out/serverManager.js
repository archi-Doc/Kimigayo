"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.ServerManager = void 0;
function message(error) {
    return error instanceof Error ? error.message : String(error);
}
/** Serialize restarts and discard settings that changed while a start was pending. */
class ServerManager {
    services;
    startTimeoutMs;
    client;
    pending = Promise.resolve();
    revision = 0;
    disposed = false;
    constructor(services, startTimeoutMs = 10000) {
        this.services = services;
        this.startTimeoutMs = startTimeoutMs;
    }
    /** Validate configuration; defer launching until a document or command needs it. */
    restart(startServer = true) {
        if (this.disposed) {
            return this.pending;
        }
        const revision = ++this.revision;
        this.pending = this.pending.then(() => this.start(revision, startServer));
        return this.pending;
    }
    dispose() {
        this.disposed = true;
        ++this.revision;
        this.pending = this.pending.then(() => this.stop());
        return this.pending;
    }
    current(revision) {
        return !this.disposed && revision === this.revision;
    }
    async start(revision, startServer) {
        if (!this.current(revision)) {
            return;
        }
        await this.stop();
        let executable;
        try {
            executable = await this.services.resolvePath();
            if (!this.current(revision) || !startServer) {
                return;
            }
            const client = this.services.createClient(executable);
            this.client = client;
            let timer;
            try {
                await Promise.race([
                    client.start(),
                    new Promise((_, reject) => {
                        timer = setTimeout(() => reject(new Error('The language server did not initialize within 10 seconds.')), this.startTimeoutMs);
                    })
                ]);
            }
            finally {
                clearTimeout(timer);
            }
            if (!this.current(revision)) {
                await this.stop();
            }
        }
        catch (error) {
            await this.stop();
            if (this.current(revision)) {
                this.services.reportError(executable
                    ? `Could not start "${executable}" (kimi.serverPath): ${message(error)}`
                    : message(error));
            }
        }
    }
    async stop() {
        const client = this.client;
        this.client = undefined;
        if (client) {
            try {
                await client.dispose();
            }
            catch (error) {
                // A failed languageclient start can also reject dispose; retain the original error.
                this.services.log(`Server cleanup: ${message(error)}`);
            }
        }
    }
}
exports.ServerManager = ServerManager;
//# sourceMappingURL=serverManager.js.map