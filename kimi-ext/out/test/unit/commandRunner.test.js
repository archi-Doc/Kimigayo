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
const assert = __importStar(require("node:assert/strict"));
const promises_1 = require("node:fs/promises");
const node_os_1 = require("node:os");
const path = __importStar(require("node:path"));
const node_test_1 = require("node:test");
const commandRunner_1 = require("../../commandRunner");
let directory;
let file;
(0, node_test_1.before)(async () => {
    directory = await (0, promises_1.mkdtemp)(path.join((0, node_os_1.tmpdir)(), 'kimi-ext-runner-'));
    file = path.join(directory, '日本語 space & #100%.kimi');
    await (0, promises_1.writeFile)(file, '::Kimi.Console.writeLine("Hello")');
});
(0, node_test_1.after)(async () => {
    assert.ok(path.resolve(directory).startsWith(path.resolve((0, node_os_1.tmpdir)()) + path.sep));
    await (0, promises_1.rm)(directory, { recursive: true, force: true });
});
function fixture(overrides = {}) {
    const events = [];
    const errors = [];
    const services = {
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
    return { runner: new commandRunner_1.CommandRunner(services), services, events, errors };
}
(0, node_test_1.test)('build and run saves inputs and uses one validated executable for both steps', async () => {
    const f = fixture();
    const save = f.services.save;
    f.services.save = async (target) => {
        assert.deepEqual(target, { file, directory });
        return save(target);
    };
    assert.equal(await f.runner.run('buildAndRun', file), 'succeeded');
    assert.deepEqual(f.events, ['save', 'resolve', 'build', 'run']);
});
(0, node_test_1.test)('Run executes the existing build without saving or building', async () => {
    const f = fixture();
    assert.equal(await f.runner.run('run', file), 'succeeded');
    assert.deepEqual(f.events, ['resolve', 'run']);
});
(0, node_test_1.test)('a build-capable run saves inputs and executes run exactly once for either action', async () => {
    for (const action of ['run', 'buildAndRun']) {
        const f = fixture({ runBuilds: () => true });
        assert.equal(await f.runner.run(action, file), 'succeeded');
        assert.deepEqual(f.events, ['save', 'resolve', 'run']);
    }
});
(0, node_test_1.test)('build-capable run failures are not retried or followed by another build or run', async () => {
    for (const exitCode of [1, undefined]) {
        const f = fixture({ runBuilds: () => true });
        f.services.execute = async (_, command) => { f.events.push(command); return exitCode; };
        assert.equal(await f.runner.run('buildAndRun', file), exitCode === 1 ? 'failed' : 'cancelled');
        assert.deepEqual(f.events, ['save', 'resolve', 'run']);
    }
});
(0, node_test_1.test)('a build-capable run does not execute if saving fails', async () => {
    const f = fixture({ runBuilds: () => true, save: async () => false });
    assert.equal(await f.runner.run('run', file), 'cancelled');
    assert.deepEqual(f.events, []);
});
(0, node_test_1.test)('changing run semantics while saving does not alter the current command plan', async () => {
    let runBuilds = false;
    const f = fixture({ runBuilds: () => runBuilds });
    f.services.save = async () => { runBuilds = true; return true; };
    assert.equal(await f.runner.run('buildAndRun', file), 'succeeded');
    assert.deepEqual(f.events, ['resolve', 'build', 'run']);
    f.events.length = 0;
    assert.equal(await f.runner.run('buildAndRun', file), 'succeeded');
    assert.deepEqual(f.events, ['resolve', 'run']);
});
(0, node_test_1.test)('run semantics do not change standalone Build or Check', async () => {
    for (const action of ['build', 'check']) {
        const f = fixture({ runBuilds: () => true });
        assert.equal(await f.runner.run(action, file), 'succeeded');
        assert.deepEqual(f.events, ['save', 'resolve', action]);
    }
});
(0, node_test_1.test)('Check saves inputs then checks without building', async () => {
    const f = fixture();
    assert.equal(await f.runner.run('check', file), 'succeeded');
    assert.deepEqual(f.events, ['save', 'resolve', 'check']);
});
(0, node_test_1.test)('a failed build never runs an old executable', async () => {
    const steps = [];
    const f = fixture({ execute: async (_, command) => { steps.push(command); return 1; } });
    assert.equal(await f.runner.run('buildAndRun', file), 'failed');
    assert.deepEqual(steps, ['build']);
    assert.match(f.errors[0], /build failed.*exit code 1/);
});
(0, node_test_1.test)('a cancelled build never runs an old executable', async () => {
    const steps = [];
    const f = fixture({ execute: async (_, command) => { steps.push(command); return undefined; } });
    assert.equal(await f.runner.run('buildAndRun', file), 'cancelled');
    assert.deepEqual(steps, ['build']);
});
(0, node_test_1.test)('a failed save prevents validation and execution', async () => {
    const f = fixture({ save: async () => false });
    assert.equal(await f.runner.run('buildAndRun', file), 'cancelled');
    assert.deepEqual(f.events, []);
});
(0, node_test_1.test)('a validation failure prevents task creation and releases the directory', async () => {
    const f = fixture({ resolveExecutable: async () => { throw new Error('kimi.serverPath: missing'); } });
    assert.equal(await f.runner.run('build', file), 'failed');
    assert.deepEqual(f.events, ['save']);
    f.services.resolveExecutable = async () => 'Kimi.exe';
    assert.equal(await f.runner.run('build', file), 'succeeded');
});
(0, node_test_1.test)('task launch failures are reported and the next invocation can recover', async () => {
    const f = fixture({ execute: async () => { throw new Error('Cannot create terminal'); } });
    assert.equal(await f.runner.run('build', file), 'failed');
    assert.match(f.errors[0], /Cannot create terminal/);
    f.services.execute = async () => 0;
    assert.equal(await f.runner.run('build', file), 'succeeded');
});
(0, node_test_1.test)('missing inputs and directories do not start a task', async () => {
    const f = fixture();
    assert.equal(await f.runner.run('build', path.join(directory, 'missing.kimi')), 'failed');
    const folder = path.join(directory, 'folder.kimiproj');
    await (0, promises_1.mkdir)(folder);
    assert.equal(await f.runner.run('build', folder), 'failed');
    assert.ok(f.events.every(event => event === 'save'));
});
(0, node_test_1.test)('unsupported and relative inputs are rejected before saving', async () => {
    const f = fixture();
    assert.equal(await f.runner.run('build', 'test.kimi'), 'failed');
    assert.equal(await f.runner.run('build', path.join(directory, 'program.exe')), 'failed');
    assert.deepEqual(f.events, []);
});
(0, node_test_1.test)('a new source file can be created by saving before it is checked', async () => {
    const target = path.join(directory, 'new.kimi');
    const f = fixture({ save: async () => { await (0, promises_1.writeFile)(target, ''); return true; }, execute: async () => 0 });
    assert.equal(await f.runner.run('check', target), 'succeeded');
});
(0, node_test_1.test)('overlapping commands cannot write the same output directory', async () => {
    let release;
    let started;
    const gate = new Promise(resolve => { release = resolve; });
    const entered = new Promise(resolve => { started = resolve; });
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
(0, node_test_1.test)('deactivation during a build prevents the run step', async () => {
    const steps = [];
    const f = fixture({ execute: async (_, command) => { steps.push(command); f.runner.dispose(); return 0; } });
    assert.equal(await f.runner.run('buildAndRun', file), 'cancelled');
    assert.equal(await f.runner.run('build', file), 'cancelled');
    assert.deepEqual(steps, ['build']);
});
(0, node_test_1.test)('deactivation while saving cancels without path validation or error notifications', async () => {
    const f = fixture({ save: async () => { f.runner.dispose(); return false; } });
    assert.equal(await f.runner.run('buildAndRun', file), 'cancelled');
    assert.deepEqual(f.events, []);
    assert.deepEqual(f.errors, []);
});
(0, node_test_1.test)('a cancelled run never saves or starts a task', async () => {
    const f = fixture();
    assert.equal(await f.runner.run('buildAndRun', file, AbortSignal.abort()), 'cancelled');
    assert.deepEqual(f.events, []);
});
(0, node_test_1.test)('Stop during a build forwards cancellation, prevents Run, and releases the directory', async () => {
    const controller = new AbortController();
    const steps = [];
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
//# sourceMappingURL=commandRunner.test.js.map