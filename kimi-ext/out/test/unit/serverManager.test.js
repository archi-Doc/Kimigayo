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
const node_test_1 = require("node:test");
const serverManager_1 = require("../../serverManager");
function deferred() {
    let resolve;
    const promise = new Promise(done => { resolve = done; });
    return { promise, resolve };
}
function fixture(overrides = {}, timeout = 1000) {
    const events = [];
    const errors = [];
    const logs = [];
    let sequence = 0;
    const services = {
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
    return { manager: new serverManager_1.ServerManager(services, timeout), events, errors, logs, services };
}
(0, node_test_1.test)('validation failure reports an error without constructing a client', async () => {
    const f = fixture({ resolvePath: async () => { throw new Error('missing file'); } });
    await f.manager.restart();
    assert.deepEqual(f.errors, ['missing file']);
    assert.deepEqual(f.events, []);
    await f.manager.dispose();
});
(0, node_test_1.test)('validation-only startup reports invalid configuration without constructing a client', async () => {
    const f = fixture({ resolvePath: async () => { throw new Error('missing file'); } });
    await f.manager.restart(false);
    assert.deepEqual(f.errors, ['missing file']);
    assert.deepEqual(f.events, []);
    await f.manager.dispose();
});
(0, node_test_1.test)('validation-only startup defers launching until requested', async () => {
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
(0, node_test_1.test)('a failed validation can recover after settings are corrected', async () => {
    const f = fixture({ resolvePath: async () => { throw new Error('missing'); } });
    await f.manager.restart();
    f.services.resolvePath = async () => 'fixed.exe';
    await f.manager.restart();
    assert.deepEqual(f.events, ['create:1:fixed.exe', 'start:1']);
    await f.manager.dispose();
});
(0, node_test_1.test)('stops the previous server before starting the replacement', async () => {
    const f = fixture();
    await f.manager.restart();
    await f.manager.restart();
    await f.manager.dispose();
    assert.deepEqual(f.events, ['create:1:Kimi.exe', 'start:1', 'dispose:1', 'create:2:Kimi.exe', 'start:2', 'dispose:2']);
});
(0, node_test_1.test)('coalesces queued settings changes', async () => {
    const f = fixture();
    await Promise.all([f.manager.restart(), f.manager.restart(), f.manager.restart()]);
    assert.deepEqual(f.events, ['create:1:Kimi.exe', 'start:1']);
    await f.manager.dispose();
});
(0, node_test_1.test)('does not start a stale path after asynchronous validation', async () => {
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
(0, node_test_1.test)('deactivation during validation prevents a process from starting', async () => {
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
(0, node_test_1.test)('deactivation during startup cleans up exactly once', async () => {
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
(0, node_test_1.test)('startup and cleanup failures preserve the useful startup error', async () => {
    const f = fixture({ createClient: () => ({ start: async () => { throw new Error('spawn failed'); }, dispose: async () => { throw new Error('already stopped'); } }) });
    await f.manager.restart();
    assert.match(f.errors[0], /Kimi.exe.*kimi.serverPath.*spawn failed/);
    assert.deepEqual(f.logs, ['Server cleanup: already stopped']);
    await f.manager.dispose();
});
(0, node_test_1.test)('constructor failures are caught and a later restart still works', async () => {
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
(0, node_test_1.test)('a server that never initializes times out and is disposed', async () => {
    let stops = 0;
    const client = { start: () => new Promise(() => { }), dispose: async () => { stops++; } };
    const f = fixture({ createClient: () => client }, 10);
    await f.manager.restart();
    assert.match(f.errors[0], /did not initialize/);
    assert.equal(stops, 1);
    await f.manager.dispose();
});
//# sourceMappingURL=serverManager.test.js.map