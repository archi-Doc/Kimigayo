import * as assert from 'node:assert/strict';
import { test } from 'node:test';
import { ManagedClient, ServerManager, ServerServices } from '../../serverManager';

function deferred() {
  let resolve!: () => void;
  const promise = new Promise<void>(done => { resolve = done; });
  return { promise, resolve };
}

function fixture(overrides: Partial<ServerServices> = {}, timeout = 1000) {
  const events: string[] = [];
  const errors: string[] = [];
  const logs: string[] = [];
  let sequence = 0;
  const services: ServerServices = {
    resolvePath: async () => 'Kimi.exe',
    createClient: file => {
      const id = ++sequence;
      events.push(`create:${id}:${file}`);
      return {
        start: async () => { events.push(`start:${id}`); },
        dispose: async () => { events.push(`dispose:${id}`); }
      };
    },
    reportError: error => { errors.push(error); },
    log: log => { logs.push(log); },
    ...overrides
  };
  return { manager: new ServerManager(services, timeout), events, errors, logs, services };
}

test('validation failure reports an error without constructing a client', async () => {
  const f = fixture({ resolvePath: async () => { throw new Error('missing file'); } });
  await f.manager.restart();
  assert.deepEqual(f.errors, ['missing file']);
  assert.deepEqual(f.events, []);
  await f.manager.dispose();
});
test('validation-only startup reports invalid configuration without constructing a client', async () => {
  const f = fixture({ resolvePath: async () => { throw new Error('missing file'); } });
  await f.manager.restart(false);
  assert.deepEqual(f.errors, ['missing file']);
  assert.deepEqual(f.events, []);
  await f.manager.dispose();
});

test('validation-only startup defers launching until requested', async () => {
  let validations = 0;
  const f = fixture({ resolvePath: async () => { validations++; return 'Kimi.exe'; } });
  await f.manager.restart(false);
  assert.equal(validations, 1);
  assert.deepEqual(f.events, []);
  await f.manager.restart();
  assert.equal(validations, 2);
  assert.deepEqual(f.events, ['create:1:Kimi.exe', 'start:1']);
  await f.manager.dispose();
});

test('a failed validation can recover after settings are corrected', async () => {
  const f = fixture({ resolvePath: async () => { throw new Error('missing'); } });
  await f.manager.restart();
  f.services.resolvePath = async () => 'fixed.exe';
  await f.manager.restart();
  assert.deepEqual(f.events, ['create:1:fixed.exe', 'start:1']);
  await f.manager.dispose();
});
test('stops the previous server before starting the replacement', async () => {
  const f = fixture();
  await f.manager.restart();
  await f.manager.restart();
  await f.manager.dispose();
  assert.deepEqual(f.events, ['create:1:Kimi.exe', 'start:1', 'dispose:1', 'create:2:Kimi.exe', 'start:2', 'dispose:2']);
});
test('coalesces queued settings changes', async () => {
  const f = fixture();
  await Promise.all([f.manager.restart(), f.manager.restart(), f.manager.restart()]);
  assert.deepEqual(f.events, ['create:1:Kimi.exe', 'start:1']);
  await f.manager.dispose();
});
test('does not start a stale path after asynchronous validation', async () => {
  const gate = deferred();
  const entered = deferred();
  const f = fixture({ resolvePath: async () => { entered.resolve(); await gate.promise; return 'stale.exe'; } });
  const first = f.manager.restart();
  await entered.promise;
  f.services.resolvePath = async () => 'new.exe';
  const second = f.manager.restart();
  gate.resolve();
  await Promise.all([first, second]);
  assert.deepEqual(f.events, ['create:1:new.exe', 'start:1']);
  await f.manager.dispose();
});
test('deactivation during validation prevents a process from starting', async () => {
  const gate = deferred();
  const entered = deferred();
  const f = fixture({ resolvePath: async () => { entered.resolve(); await gate.promise; return 'Kimi.exe'; } });
  const start = f.manager.restart();
  await entered.promise;
  const stop = f.manager.dispose();
  gate.resolve();
  await Promise.all([start, stop]);
  assert.deepEqual(f.events, []);
  await f.manager.restart();
  assert.deepEqual(f.events, []);
});
test('deactivation during startup cleans up exactly once', async () => {
  const gate = deferred();
  const entered = deferred();
  let stops = 0;
  const f = fixture({ createClient: () => ({ start: async () => { entered.resolve(); await gate.promise; }, dispose: async () => { stops++; } }) });
  const start = f.manager.restart();
  await entered.promise;
  const stop = f.manager.dispose();
  gate.resolve();
  await Promise.all([start, stop]);
  assert.equal(stops, 1);
  assert.deepEqual(f.errors, []);
});
test('startup and cleanup failures preserve the useful startup error', async () => {
  const f = fixture({ createClient: () => ({ start: async () => { throw new Error('spawn failed'); }, dispose: async () => { throw new Error('already stopped'); } }) });
  await f.manager.restart();
  assert.match(f.errors[0], /Kimi.exe.*kimi.serverPath.*spawn failed/);
  assert.deepEqual(f.logs, ['Server cleanup: already stopped']);
  await f.manager.dispose();
});
test('constructor failures are caught and a later restart still works', async () => {
  const f = fixture();
  const create = f.services.createClient;
  f.services.createClient = () => { throw new Error('construction failed'); };
  await f.manager.restart();
  f.services.createClient = create;
  await f.manager.restart();
  assert.match(f.errors[0], /construction failed/);
  assert.equal(f.events.length, 2);
  await f.manager.dispose();
});
test('a server that never initializes times out and is disposed', async () => {
  let stops = 0;
  const client: ManagedClient = { start: () => new Promise<void>(() => {}), dispose: async () => { stops++; } };
  const f = fixture({ createClient: () => client }, 10);
  await f.manager.restart();
  assert.match(f.errors[0], /did not initialize/);
  assert.equal(stops, 1);
  await f.manager.dispose();
});
