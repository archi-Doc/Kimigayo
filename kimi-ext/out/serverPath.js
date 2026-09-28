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
exports.resolveServerPath = resolveServerPath;
const node_fs_1 = require("node:fs");
const promises_1 = require("node:fs/promises");
const path = __importStar(require("node:path"));
function unquote(value) {
    return value.startsWith('"') && value.endsWith('"') ? value.slice(1, -1) : value;
}
function isFullyQualified(value) {
    // On Windows, isAbsolute also accepts "\\Tools\\Kimi.exe", relative to the current drive.
    return path.isAbsolute(value) && (process.platform !== 'win32' || path.parse(value).root.length > 1);
}
async function checkFile(file) {
    let info;
    try {
        info = await (0, promises_1.stat)(file);
    }
    catch (error) {
        const code = error.code;
        if (code === 'ENOENT' || code === 'ENOTDIR') {
            throw new Error(`kimi.serverPath: file not found: ${file}`);
        }
        throw new Error(`kimi.serverPath: cannot access ${file} (${code ?? String(error)}).`);
    }
    if (!info.isFile()) {
        throw new Error(`kimi.serverPath: expected an executable file, but found a directory or another file type: ${file}`);
    }
    try {
        await (0, promises_1.access)(file, node_fs_1.constants.R_OK | node_fs_1.constants.X_OK);
    }
    catch {
        throw new Error(`kimi.serverPath: the file cannot be read or executed: ${file}`);
    }
}
/** Resolve once so validation and process creation use the same absolute path. */
async function resolveServerPath(setting, searchPath = process.env.PATH ?? '') {
    if (typeof setting !== 'string' || setting.trim().length === 0) {
        throw new Error('kimi.serverPath must contain the absolute path to Kimi.exe or an executable name on PATH.');
    }
    const value = unquote(setting.trim());
    if (!value || /[\0\r\n"]/.test(value)) {
        throw new Error('kimi.serverPath contains an empty or invalid executable path.');
    }
    if (isFullyQualified(value)) {
        const absolute = path.resolve(value);
        await checkFile(absolute);
        return absolute;
    }
    if (/[\\/:]/.test(value)) {
        throw new Error(`kimi.serverPath: use an absolute path instead of a relative path: ${value}`);
    }
    const names = process.platform === 'win32' && !path.extname(value) ? [value, `${value}.exe`] : [value];
    for (const entry of searchPath.split(path.delimiter)) {
        const directory = unquote(entry.trim());
        // Empty/relative PATH entries would depend on the extension host's working directory.
        if (!isFullyQualified(directory)) {
            continue;
        }
        for (const name of names) {
            const candidate = path.join(directory, name);
            try {
                await checkFile(candidate);
                return candidate;
            }
            catch {
                // Keep searching: an earlier PATH entry can be missing or inaccessible.
            }
        }
    }
    throw new Error(`kimi.serverPath: no accessible executable named "${value}" was found on PATH. Set the absolute path to Kimi.exe.`);
}
//# sourceMappingURL=serverPath.js.map