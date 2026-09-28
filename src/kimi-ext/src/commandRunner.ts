import { realpath, stat } from 'node:fs/promises';
import * as path from 'node:path';

export type KimiAction = 'build' | 'run' | 'buildAndRun' | 'check';
export type CliCommand = Exclude<KimiAction, 'buildAndRun'>;
export type CommandResult = 'succeeded' | 'failed' | 'cancelled' | 'busy';

export interface CommandTarget {
  file: string;
  directory: string;
}

export interface CommandServices {
  runBuilds(): boolean;
  save(target: CommandTarget): Promise<boolean>;
  resolveExecutable(): Promise<string>;
  execute(executable: string, command: CliCommand, target: CommandTarget, signal?: AbortSignal): Promise<number | undefined>;
  reportError(message: string): void;
}

export function isTargetFile(file: string): boolean {
  const extension = path.extname(file).toLowerCase();
  return extension === '.kimi' || extension === '.kimiproj';
}

/** A directory's projects and implicit sources can write the same bin directory. */
export class CommandRunner {
  private readonly busyDirectories = new Set<string>();
  private disposed = false;

  constructor(private readonly services: CommandServices) {}

  async run(action: KimiAction, input: string, signal?: AbortSignal): Promise<CommandResult> {
    let key: string | undefined;
    try {
      if (this.cancelled(signal)) {
        return 'cancelled';
      }
      if (!path.isAbsolute(input) || !isTargetFile(input)) {
        throw new Error('Select a .kimiproj project or a saved .kimi source file.');
      }
      const target = { file: path.normalize(input), directory: path.dirname(input) };
      const directory = await realpath(target.directory);
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
      if (!(await stat(target.file)).isFile()) {
        throw new Error(`The selected target is not a file: ${target.file}`);
      }
      const executable = await this.services.resolveExecutable();
      const steps: readonly CliCommand[] = action === 'buildAndRun'
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
    } catch (error) {
      if (!this.cancelled(signal)) {
        this.services.reportError(error instanceof Error ? error.message : String(error));
      }
      return this.cancelled(signal) ? 'cancelled' : 'failed';
    } finally {
      if (key !== undefined) {
        this.busyDirectories.delete(key);
      }
    }
  }

  dispose(): void {
    this.disposed = true;
  }

  private cancelled(signal?: AbortSignal): boolean {
    return this.disposed || signal?.aborted === true;
  }
}
