import * as path from 'node:path';
import { readdir } from 'node:fs/promises';
import { CancellationToken, QuickPickItem, RelativePattern, TabInputText, Uri, window, workspace } from 'vscode';
import { CommandTarget, isTargetFile } from './commandRunner';

interface TargetItem extends QuickPickItem {
  uri?: Uri;
}

async function browse(): Promise<Uri | undefined> {
  return (await window.showOpenDialog({
    title: 'Select a Kimi project or single source file',
    canSelectMany: false,
    canSelectFolders: false,
    filters: { 'Kimi project or source': ['kimiproj', 'kimi'] }
  }))?.[0];
}

function validateUri(uri: Uri): Uri {
  if (uri.scheme !== 'file' || !isTargetFile(uri.fsPath)) {
    throw new Error('Select a .kimiproj project or a saved .kimi source file.');
  }
  return uri;
}

/** Explicit targets are exact. Prefer a nearby project, then an unambiguous workspace project. */
export async function selectTarget(explicit?: Uri, token?: CancellationToken, forceSelection = false): Promise<Uri | undefined> {
  if (token?.isCancellationRequested) {
    return undefined;
  }
  if (explicit) {
    return validateUri(explicit);
  }
  const editorUri = window.activeTextEditor?.document.uri;
  const active = editorUri?.scheme === 'file' && isTargetFile(editorUri.fsPath) ? editorUri : undefined;
  if (!forceSelection && active && path.extname(active.fsPath).toLowerCase() === '.kimiproj') {
    return active;
  }

  const projects = new Map<string, Uri>();
  const add = (uri: Uri): void => {
    if (uri.scheme === 'file' && path.extname(uri.fsPath).toLowerCase() === '.kimiproj') {
      const key = process.platform === 'win32' ? uri.fsPath.toLowerCase() : uri.fsPath;
      projects.set(key, uri);
    }
  };
  if (active) {
    add(active);
    const directory = path.dirname(active.fsPath);
    // Kimi projects include sibling sources. Check this directory before scanning a workspace.
    const entries = await readdir(directory, { withFileTypes: true }).catch(() => []);
    for (const entry of entries) {
      if ((entry.isFile() || entry.isSymbolicLink()) && entry.name.toLowerCase().endsWith('.kimiproj')) {
        add(Uri.file(path.join(directory, entry.name)));
      }
    }
  }
  const folder = editorUri?.scheme === 'file' ? workspace.getWorkspaceFolder(editorUri) : undefined;
  // Outside-workspace sources must not silently select an unrelated workspace project.
  if (forceSelection || (projects.size === 0 && (!active || folder))) {
    // textDocuments can retain closed documents; only user-open project tabs are candidates.
    const folderKey = folder?.uri.toString();
    for (const group of window.tabGroups.all) {
      for (const tab of group.tabs) {
        if (tab.input instanceof TabInputText) {
          const uri = tab.input.uri;
          if (forceSelection || !folder || workspace.getWorkspaceFolder(uri)?.uri.toString() === folderKey) {
            add(uri);
          }
        }
      }
    }
    const pattern = '**/*.[kK][iI][mM][iI][pP][rR][oO][jJ]';
    const include = !forceSelection && folder ? new RelativePattern(folder, pattern) : pattern;
    for (const uri of await workspace.findFiles(include, '**/{node_modules,bin,obj,.git,.vscode-test}/**', undefined, token)) {
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
  const items: TargetItem[] = candidates.map(uri => ({
    label: path.basename(uri.fsPath),
    description: path.extname(uri.fsPath).toLowerCase() === '.kimiproj' ? 'Project' : 'Single source file only',
    detail: uri.fsPath,
    uri
  }));
  items.push({ label: 'Browse for a project or source file...' });
  const selected = await window.showQuickPick(items, {
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
export async function saveKimiDocuments(target: CommandTarget): Promise<boolean> {
  const singleSource = path.extname(target.file).toLowerCase() === '.kimi';
  const targetKey = process.platform === 'win32' ? target.file.toLowerCase() : target.file;
  for (const document of workspace.textDocuments) {
    if (!document.isDirty || document.uri.scheme !== 'file' || !isTargetFile(document.uri.fsPath)) {
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
