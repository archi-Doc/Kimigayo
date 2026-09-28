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
const documentUri_1 = require("../../documentUri");
(0, node_test_1.test)('Windows drive colon is preserved while filename characters remain escaped', { skip: process.platform !== 'win32' }, () => {
    const result = (0, documentUri_1.toProtocolUri)({ scheme: 'file', fsPath: 'C:\\Project Files\\日本語 #100%.kimi', toString: () => 'unused' });
    assert.equal(result, 'file:///C:/Project%20Files/%E6%97%A5%E6%9C%AC%E8%AA%9E%20%23100%25.kimi');
    const parsed = new URL(result);
    assert.equal(parsed.hash, '');
    assert.equal(parsed.search, '');
});
(0, node_test_1.test)('preserves UNC server and share', { skip: process.platform !== 'win32' }, () => {
    assert.equal((0, documentUri_1.toProtocolUri)({ scheme: 'file', fsPath: '\\\\server\\share\\Hello World.kimi', toString: () => 'unused' }), 'file://server/share/Hello%20World.kimi');
});
(0, node_test_1.test)('POSIX file URI remains escaped', { skip: process.platform === 'win32' }, () => {
    assert.equal((0, documentUri_1.toProtocolUri)({ scheme: 'file', fsPath: '/tmp/Test #1.kimi', toString: () => 'unused' }), 'file:///tmp/Test%20%231.kimi');
});
(0, node_test_1.test)('preserves non-file URIs', () => {
    assert.equal((0, documentUri_1.toProtocolUri)({ scheme: 'untitled', fsPath: '', toString: () => 'untitled:Untitled-1' }), 'untitled:Untitled-1');
});
//# sourceMappingURL=documentUri.test.js.map