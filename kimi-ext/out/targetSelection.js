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
exports.selectTarget = selectTarget;
exports.saveKimiDocuments = saveKimiDocuments;
const path = __importStar(require("node:path"));
const promises_1 = require("node:fs/promises");
const vscode_1 = require("vscode");
const commandRunner_1 = require("./commandRunner");
async function browse() {
    return (await vscode_1.window.showOpenDialog({
        title: 'Select a Kimi project or single source file',
        canSelectMany: false,
        canSelectFolders: false,
        filters: { 'Kimi project or source': ['kimiproj', 'kimi'] }
    }))?.[0];
}
function validateUri(uri) {
    if (uri.scheme !== 'file' || !(0, commandRunner_1.isTargetFile)(uri.fsPath)) {
        throw new Error('Select a .kimiproj project or a saved .kimi source file.');
    }
    return uri;
}
/** Explicit targets are exact. Prefer a nearby project, then an unambiguous workspace project. */
async function selectTarget(explicit, token, forceSelection = false) {
    if (token?.isCancellationRequested) {
        return undefined;
    }
    if (explicit) {
        return validateUri(explicit);
    }
    const editorUri = vscode_1.window.activeTextEditor?.document.uri;
    const active = editorUri?.scheme === 'file' && (0, commandRunner_1.isTargetFile)(editorUri.fsPath) ? editorUri : undefined;
    if (!forceSelection && active && path.extname(active.fsPath).toLowerCase() === '.kimiproj') {
        return active;
    }
    const projects = new Map();
    const add = (uri) => {
        if (uri.scheme === 'file' && path.extname(uri.fsPath).toLowerCase() === '.kimiproj') {
            const key = process.platform === 'win32' ? uri.fsPath.toLowerCase() : uri.fsPath;
            projects.set(key, uri);
        }
    };
    if (active) {
        add(active);
        const directory = path.dirname(active.fsPath);
        // Kimi projects include sibling sources. Check this directory before scanning a workspace.
        const entries = await (0, promises_1.readdir)(directory, { withFileTypes: true }).catch(() => []);
        for (const entry of entries) {
            if ((entry.isFile() || entry.isSymbolicLink()) && entry.name.toLowerCase().endsWith('.kimiproj')) {
                add(vscode_1.Uri.file(path.join(directory, entry.name)));
            }
        }
    }
    const folder = editorUri?.scheme === 'file' ? vscode_1.workspace.getWorkspaceFolder(editorUri) : undefined;
    // Outside-workspace sources must not silently select an unrelated workspace project.
    if (forceSelection || (projects.size === 0 && (!active || folder))) {
        // textDocuments can retain closed documents; only user-open project tabs are candidates.
        const folderKey = folder?.uri.toString();
        for (const group of vscode_1.window.tabGroups.all) {
            for (const tab of group.tabs) {
                if (tab.input instanceof vscode_1.TabInputText) {
                    const uri = tab.input.uri;
                    if (forceSelection || !folder || vscode_1.workspace.getWorkspaceFolder(uri)?.uri.toString() === folderKey) {
                        add(uri);
                    }
                }
            }
        }
        const pattern = '**/*.[kK][iI][mM][iI][pP][rR][oO][jJ]';
        const include = !forceSelection && folder ? new vscode_1.RelativePattern(folder, pattern) : pattern;
        for (const uri of await vscode_1.workspace.findFiles(include, '**/{node_modules,bin,obj,.git,.vscode-test}/**', undefined, token)) {
            add(uri);
        }
    }
    if (token?.isCancellationRequested) {
        return undefined;
    }
    if (!forceSelection) {
        if (projects.size === 1) {
            return projects.values().next().value;
        }
        if (projects.size === 0 && active) {
            return active;
        }
    }
    const candidates = [...projects.values()];
    if (active && path.extname(active.fsPath).toLowerCase() === '.kimi') {
        candidates.push(active);
    }
    if (candidates.length === 0) {
        const selected = await browse();
        return selected && !token?.isCancellationRequested ? validateUri(selected) : undefined;
    }
    const items = candidates.map(uri => ({
        label: path.basename(uri.fsPath),
        description: path.extname(uri.fsPath).toLowerCase() === '.kimiproj' ? 'Project' : 'Single source file only',
        detail: uri.fsPath,
        uri
    }));
    items.push({ label: 'Browse for a project or source file...' });
    const selected = await vscode_1.window.showQuickPick(items, {
        title: 'Kimi: Select Target',
        placeHolder: 'Select a project, or a single .kimi file (sibling files are not included)',
        matchOnDescription: true,
        matchOnDetail: true
    }, token);
    if (!selected || token?.isCancellationRequested) {
        return undefined;
    }
    const uri = selected.uri ?? await browse();
    return uri && !token?.isCancellationRequested ? validateUri(uri) : undefined;
}
/** Projects may reference other folders; implicit single-source projects read only their target. */
async function saveKimiDocuments(target) {
    const singleSource = path.extname(target.file).toLowerCase() === '.kimi';
    const targetKey = process.platform === 'win32' ? target.file.toLowerCase() : target.file;
    for (const document of vscode_1.workspace.textDocuments) {
        if (!document.isDirty || document.uri.scheme !== 'file' || !(0, commandRunner_1.isTargetFile)(document.uri.fsPath)) {
            continue;
        }
        const documentKey = process.platform === 'win32' ? document.uri.fsPath.toLowerCase() : document.uri.fsPath;
        if (singleSource && documentKey !== targetKey) {
            continue;
        }
        if (!await document.save()) {
            return false;
        }
    }
    return true;
}
//# sourceMappingURL=targetSelection.js.map