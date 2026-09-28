import { randomUUID } from 'node:crypto';
import * as path from 'node:path';
import { Disposable, ProcessExecution, Task, TaskExecution, TaskGroup, TaskPanelKind, TaskRevealKind, TaskScope, Uri, tasks, workspace } from 'vscode';
import { CliCommand, CommandTarget } from './commandRunner';

interface PendingTask {
  execution?: TaskExecution;
  finish(exitCode: number | undefined): void;
}

/** Track process exits (not executeTask's launch acknowledgement) before advancing. */
export class TaskExecutor {
  private readonly pending = new Set<PendingTask>();
  private disposed = false;

  constructor(private readonly log: (message: string) => void) {}

  async execute(executable: string, command: CliCommand, target: CommandTarget, signal?: AbortSignal): Promise<number | undefined> {
    if (this.disposed || signal?.aborted) {
      return undefined;
    }
    const id = randomUUID();
    const task = new Task(
      { type: 'kimi', command, file: target.file, executionId: id },
      workspace.getWorkspaceFolder(Uri.file(target.file)) ?? TaskScope.Global,
      `${command}: ${path.basename(target.file)}`,
      'Kimi',
      new ProcessExecution(executable, [command, target.file], { cwd: target.directory }),
      []
    );
    if (command === 'build') {
      task.group = TaskGroup.Build;
    }
    task.presentationOptions = {
      reveal: TaskRevealKind.Always,
      focus: command === 'run',
      panel: TaskPanelKind.Shared,
      clear: false,
      showReuseMessage: true
    };
    const subscriptions: Disposable[] = [];
    let resolve!: (exitCode: number | undefined) => void;
    const result = new Promise<number | undefined>(done => { resolve = done; });
    const pending: PendingTask = {
      finish: code => {
        for (const subscription of subscriptions) {
          subscription.dispose();
        }
        this.pending.delete(pending);
        resolve(code);
      }
    };
    // Keep the runner's directory lock until VS Code confirms that the task has ended.
    const cancel = (): void => pending.execution?.terminate();
    signal?.addEventListener('abort', cancel, { once: true });
    // Subscribe before launch so even a very short-lived process is observed.
    subscriptions.push(
      tasks.onDidEndTaskProcess(event => {
        if (event.execution.task.definition.executionId === id) {
          this.log(`Kimi ${command} process exited: ${event.exitCode ?? 'terminated'}`);
          pending.finish(event.exitCode);
        }
      }),
      tasks.onDidEndTask(event => {
        if (event.execution.task.definition.executionId === id) {
          this.log(`Kimi ${command} task ended without a process exit code.`);
          pending.finish(undefined);
        }
      })
    );
    this.pending.add(pending);
    try {
      if (signal?.aborted) {
        return undefined;
      }
      this.log(`Kimi ${command}: ${target.file}`);
      pending.execution = await tasks.executeTask(task);
      if (this.disposed) {
        pending.execution.terminate();
        pending.finish(undefined);
      } else if (signal?.aborted) {
        pending.execution.terminate();
      }
      return await result;
    } finally {
      signal?.removeEventListener('abort', cancel);
      pending.finish(undefined);
    }
  }

  dispose(): void {
    this.disposed = true;
    for (const pending of this.pending) {
      pending.execution?.terminate();
      pending.finish(undefined);
    }
  }
}
