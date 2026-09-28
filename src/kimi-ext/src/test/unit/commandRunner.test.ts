import * as assert from 'node:assert/strict';
import { mkdtemp, mkdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import * as path from 'node:path';
import { after, before, test } from 'node:test';
import { CommandRunner, CommandServices } from '../../commandRunner';

let directory: string;
let file: string;
before(async () => {
  directory = await mkdtemp(path.join(tmpdir(), 'kimi-ext-runner-'));
  file = path.join(directory, '日本語 space & #100%.kimi');
  await writeFile(file, '::Kimi.Console.writeLine("Hello")');
});
after(async () => {
  assert.ok(path.resolve(directory).startsWith(path.resolve(tmpdir()) + path.sep));
  await rm(directory, { recursive: true, force: true });
});

function fixture(overrides: Partial<CommandServices> = {}) {
  const events: string[] = [];
  const errors: string[] = [];
  const services: CommandServices = {
    runBuilds: () => false,
    save: async () => { events.push('save'); return true; },
    resolveExecutable: async () => { events.push('resolve'); return 'Kimi.exe'; },
    execute: async (executable, command, target) => {
      assert.equal(executable, 'Kimi.exe');
      assert.equal(target.file, file);
      assert.equal(target.directory, directory);
      events.push(command);
      return 0;
    },
    reportError: error => errors.push(error),
    ...overrides
  };
  return { runner: new CommandRunner(services), services, events, errors };
}

test('build and run saves inputs and uses one validated executable for both steps', async () => {
  const f = fixture();
  const save = f.services.save;
  f.services.save = async target => {
    assert.deepEqual(target, { file, directory });
    return save(target);
  };
  assert.equal(await f.runner.run('buildAndRun', file), 'succeeded');
  assert.deepEqual(f.events, ['save', 'resolve', 'build', 'run']);
});

test('Run executes the existing build without saving or building', async () => {
  const f = fixture();
  assert.equal(await f.runner.run('run', file), 'succeeded');
  assert.deepEqual(f.events, ['resolve', 'run']);
});

test('a build-capable run saves inputs and executes run exactly once for either action', async () => {
  for (const action of ['run', 'buildAndRun'] as const) {
    const f = fixture({ runBuilds: () => true });
    assert.equal(await f.runner.run(action, file), 'succeeded');
    assert.deepEqual(f.events, ['save', 'resolve', 'run']);
  }
});

test('build-capable run failures are not retried or followed by another build or run', async () => {
  for (const exitCode of [1, undefined]) {
    const f = fixture({ runBuilds: () => true });
    f.services.execute = async (_, command) => { f.events.push(command); return exitCode; };
    assert.equal(await f.runner.run('buildAndRun', file), exitCode === 1 ? 'failed' : 'cancelled');
    assert.deepEqual(f.events, ['save', 'resolve', 'run']);
  }
});

test('a build-capable run does not execute if saving fails', async () => {
  const f = fixture({ runBuilds: () => true, save: async () => false });
  assert.equal(await f.runner.run('run', file), 'cancelled');
  assert.deepEqual(f.events, []);
});

test('changing run semantics while saving does not alter the current command plan', async () => {
  let runBuilds = false;
  const f = fixture({ runBuilds: () => runBuilds });
  f.services.save = async () => { runBuilds = true; return true; };
  assert.equal(await f.runner.run('buildAndRun', file), 'succeeded');
  assert.deepEqual(f.events, ['resolve', 'build', 'run']);
  f.events.length = 0;
  assert.equal(await f.runner.run('buildAndRun', file), 'succeeded');
  assert.deepEqual(f.events, ['resolve', 'run']);
});

test('run semantics do not change standalone Build or Check', async () => {
  for (const action of ['build', 'check'] as const) {
    const f = fixture({ runBuilds: () => true });
    assert.equal(await f.runner.run(action, file), 'succeeded');
    assert.deepEqual(f.events, ['save', 'resolve', action]);
  }
});

test('Check saves inputs then checks without building', async () => {
  const f = fixture();
  assert.equal(await f.runner.run('check', file), 'succeeded');
  assert.deepEqual(f.events, ['save', 'resolve', 'check']);
});

test('a failed build never runs an old executable', async () => {
  const steps: string[] = [];
  const f = fixture({ execute: async (_, command) => { steps.push(command); return 1; } });
  assert.equal(await f.runner.run('buildAndRun', file), 'failed');
  assert.deepEqual(steps, ['build']);
  assert.match(f.errors[0], /build failed.*exit code 1/);
});

test('a cancelled build never runs an old executable', async () => {
  const steps: string[] = [];
  const f = fixture({ execute: async (_, command) => { steps.push(command); return undefined; } });
  assert.equal(await f.runner.run('buildAndRun', file), 'cancelled');
  assert.deepEqual(steps, ['build']);
});

test('a failed save prevents validation and execution', async () => {
  const f = fixture({ save: async () => false });
  assert.equal(await f.runner.run('buildAndRun', file), 'cancelled');
  assert.deepEqual(f.events, []);
});

test('a validation failure prevents task creation and releases the directory', async () => {
  const f = fixture({ resolveExecutable: async () => { throw new Error('kimi.serverPath: missing'); } });
  assert.equal(await f.runner.run('build', file), 'failed');
  assert.deepEqual(f.events, ['save']);
  f.services.resolveExecutable = async () => 'Kimi.exe';
  assert.equal(await f.runner.run('build', file), 'succeeded');
});

test('task launch failures are reported and the next invocation can recover', async () => {
  const f = fixture({ execute: async () => { throw new Error('Cannot create terminal'); } });
  assert.equal(await f.runner.run('build', file), 'failed');
  assert.match(f.errors[0], /Cannot create terminal/);
  f.services.execute = async () => 0;
  assert.equal(await f.runner.run('build', file), 'succeeded');
});

test('missing inputs and directories do not start a task', async () => {
  const f = fixture();
  assert.equal(await f.runner.run('build', path.join(directory, 'missing.kimi')), 'failed');
  const folder = path.join(directory, 'folder.kimiproj');
  await mkdir(folder);
  assert.equal(await f.runner.run('build', folder), 'failed');
  assert.ok(f.events.every(event => event === 'save'));
});

test('unsupported and relative inputs are rejected before saving', async () => {
  const f = fixture();
  assert.equal(await f.runner.run('build', 'test.kimi'), 'failed');
  assert.equal(await f.runner.run('build', path.join(directory, 'program.exe')), 'failed');
  assert.deepEqual(f.events, []);
});

test('a new source file can be created by saving before it is checked', async () => {
  const target = path.join(directory, 'new.kimi');
  const f = fixture({ save: async () => { await writeFile(target, ''); return true; }, execute: async () => 0 });
  assert.equal(await f.runner.run('check', target), 'succeeded');
});

test('overlapping commands cannot write the same output directory', async () => {
  let release!: () => void;
  let started!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; });
  const entered = new Promise<void>(resolve => { started = resolve; });
  const f = fixture({ execute: async () => { started(); await gate; return 0; } });
  const first = f.runner.run('build', file);
  await entered;
  const second = path.join(directory, 'another.kimiproj');
  assert.equal(await f.runner.run('build', second), 'busy');
  assert.equal(await f.runner.run('run', file), 'busy');
  release();
  assert.equal(await first, 'succeeded');
  assert.equal(await f.runner.run('check', file), 'succeeded');
});

test('deactivation during a build prevents the run step', async () => {
  const steps: string[] = [];
  const f = fixture({ execute: async (_, command) => { steps.push(command); f.runner.dispose(); return 0; } });
  assert.equal(await f.runner.run('buildAndRun', file), 'cancelled');
  assert.equal(await f.runner.run('build', file), 'cancelled');
  assert.deepEqual(steps, ['build']);
});

test('deactivation while saving cancels without path validation or error notifications', async () => {
  const f = fixture({ save: async () => { f.runner.dispose(); return false; } });
  assert.equal(await f.runner.run('buildAndRun', file), 'cancelled');
  assert.deepEqual(f.events, []);
  assert.deepEqual(f.errors, []);
});

test('a cancelled run never saves or starts a task', async () => {
  const f = fixture();
  assert.equal(await f.runner.run('buildAndRun', file, AbortSignal.abort()), 'cancelled');
  assert.deepEqual(f.events, []);
});

test('Stop during a build forwards cancellation, prevents Run, and releases the directory', async () => {
  const controller = new AbortController();
  const steps: string[] = [];
  const f = fixture({ execute: async (_, command, _target, signal) => {
    assert.equal(signal, controller.signal);
    steps.push(command);
    controller.abort();
    return 0;
  } });
  assert.equal(await f.runner.run('buildAndRun', file, controller.signal), 'cancelled');
  assert.deepEqual(steps, ['build']);
  assert.deepEqual(f.errors, []);
  f.services.execute = async () => 0;
  assert.equal(await f.runner.run('build', file), 'succeeded');
});
