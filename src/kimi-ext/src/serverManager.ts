export interface ManagedClient {
  start(): Promise<void>;
  dispose(): Promise<void>;
}

export interface ServerServices {
  resolvePath(): Promise<string>;
  createClient(executable: string): ManagedClient;
  reportError(message: string): void;
  log(message: string): void;
}

function message(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

/** Serialize restarts and discard settings that changed while a start was pending. */
export class ServerManager {
  private client: ManagedClient | undefined;
  private pending = Promise.resolve();
  private revision = 0;
  private disposed = false;

  constructor(private readonly services: ServerServices, private readonly startTimeoutMs = 10000) {}

  /** Validate configuration; defer launching until a document or command needs it. */
  restart(startServer = true): Promise<void> {
    if (this.disposed) {
      return this.pending;
    }
    const revision = ++this.revision;
    this.pending = this.pending.then(() => this.start(revision, startServer));
    return this.pending;
  }

  dispose(): Promise<void> {
    this.disposed = true;
    ++this.revision;
    this.pending = this.pending.then(() => this.stop());
    return this.pending;
  }

  private current(revision: number): boolean {
    return !this.disposed && revision === this.revision;
  }

  private async start(revision: number, startServer: boolean): Promise<void> {
    if (!this.current(revision)) {
      return;
    }
    await this.stop();
    let executable: string | undefined;
    try {
      executable = await this.services.resolvePath();
      if (!this.current(revision) || !startServer) {
        return;
      }
      const client = this.services.createClient(executable);
      this.client = client;
      let timer: ReturnType<typeof setTimeout> | undefined;
      try {
        await Promise.race([
          client.start(),
          new Promise<never>((_, reject) => {
            timer = setTimeout(() => reject(new Error('The language server did not initialize within 10 seconds.')), this.startTimeoutMs);
          })
        ]);
      } finally {
        clearTimeout(timer);
      }
      if (!this.current(revision)) {
        await this.stop();
      }
    } catch (error) {
      await this.stop();
      if (this.current(revision)) {
        this.services.reportError(executable
          ? `Could not start "${executable}" (kimi.serverPath): ${message(error)}`
          : message(error));
      }
    }
  }

  private async stop(): Promise<void> {
    const client = this.client;
    this.client = undefined;
    if (client) {
      try {
        await client.dispose();
      } catch (error) {
        // A failed languageclient start can also reject dispose; retain the original error.
        this.services.log(`Server cleanup: ${message(error)}`);
      }
    }
  }
}
