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
const serverPath_1 = require("../../serverPath");
let root;
let executable;
(0, node_test_1.before)(async () => {
    root = await (0, promises_1.mkdtemp)(path.join((0, node_os_1.tmpdir)(), 'kimi-lsp-path-'));
    executable = path.join(root, 'Kimi server.exe');
    await (0, promises_1.writeFile)(executable, 'test fixture');
    await (0, promises_1.chmod)(executable, 0o700);
});
(0, node_test_1.after)(async () => {
    assert.ok(path.resolve(root).startsWith(path.resolve((0, node_os_1.tmpdir)()) + path.sep));
    await (0, promises_1.rm)(root, { recursive: true, force: true });
});
(0, node_test_1.test)('accepts a file path containing spaces', async () => {
    assert.equal(await (0, serverPath_1.resolveServerPath)(executable), executable);
});
(0, node_test_1.test)('accepts the quoted path produced by Windows Copy as path', async () => {
    assert.equal(await (0, serverPath_1.resolveServerPath)(` "${executable}" `), executable);
});
(0, node_test_1.test)('reports the missing file and setting name', async () => {
    const missing = path.join(root, 'missing.exe');
    await assert.rejects((0, serverPath_1.resolveServerPath)(missing), error => error instanceof Error && error.message.includes(missing) && error.message.includes('kimi.serverPath'));
});
(0, node_test_1.test)('rejects directories even when their name ends with exe', async () => {
    const directory = path.join(root, 'directory.exe');
    await (0, promises_1.mkdir)(directory);
    await assert.rejects((0, serverPath_1.resolveServerPath)(directory), /expected an executable file/);
});
(0, node_test_1.test)('rejects empty, non-string and malformed values', async () => {
    for (const value of ['', '  ', undefined, null, 42, '""', 'bad\0path', 'bad\npath', '"unterminated']) {
        await assert.rejects((0, serverPath_1.resolveServerPath)(value), /kimi.serverPath/);
    }
});
(0, node_test_1.test)('rejects relative paths instead of depending on the host working directory', async () => {
    await assert.rejects((0, serverPath_1.resolveServerPath)('./Kimi.exe'), /absolute path/);
    await assert.rejects((0, serverPath_1.resolveServerPath)('folder\\Kimi.exe'), /absolute path/);
});
(0, node_test_1.test)('rejects Windows root-relative paths that depend on the current drive', { skip: process.platform !== 'win32' }, async () => {
    await assert.rejects((0, serverPath_1.resolveServerPath)(executable.slice(2)), /absolute path/);
});
(0, node_test_1.test)('ignores Windows root-relative PATH directories', { skip: process.platform !== 'win32' }, async () => {
    await assert.rejects((0, serverPath_1.resolveServerPath)(path.basename(executable), root.slice(2)), /found on PATH/);
});
(0, node_test_1.test)('resolves a command through PATH and skips missing directories', async () => {
    const search = [path.join(root, 'missing'), root].join(path.delimiter);
    assert.equal(await (0, serverPath_1.resolveServerPath)(path.basename(executable), search), executable);
});
(0, node_test_1.test)('handles quoted PATH entries', async () => {
    assert.equal(await (0, serverPath_1.resolveServerPath)(path.basename(executable), `"${root}"`), executable);
});
(0, node_test_1.test)('reports a command missing from PATH', async () => {
    await assert.rejects((0, serverPath_1.resolveServerPath)('missing.exe', root), /found on PATH/);
});
(0, node_test_1.test)('does not search the current directory for empty or relative PATH entries', async () => {
    await assert.rejects((0, serverPath_1.resolveServerPath)('Kimi.exe', ['', '.', 'relative'].join(path.delimiter)), /found on PATH/);
});
(0, node_test_1.test)('adds exe to an extensionless Windows command', { skip: process.platform !== 'win32' }, async () => {
    const file = path.join(root, 'Kimi.exe');
    await (0, promises_1.writeFile)(file, 'test fixture');
    assert.equal(await (0, serverPath_1.resolveServerPath)('Kimi', root), file);
});
(0, node_test_1.test)('rejects non-executable files on POSIX', { skip: process.platform === 'win32' }, async () => {
    const file = path.join(root, 'not-executable');
    await (0, promises_1.writeFile)(file, 'test fixture', { mode: 0o600 });
    await assert.rejects((0, serverPath_1.resolveServerPath)(file), /cannot be read or executed/);
});
//# sourceMappingURL=serverPath.test.js.map