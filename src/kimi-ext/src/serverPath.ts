import { constants } from 'node:fs';
import { access, stat } from 'node:fs/promises';
import * as path from 'node:path';

function unquote(value: string): string {
  return value.startsWith('"') && value.endsWith('"') ? value.slice(1, -1) : value;
}

function isFullyQualified(value: string): boolean {
  // On Windows, isAbsolute also accepts "\\Tools\\Kimi.exe", relative to the current drive.
  return path.isAbsolute(value) && (process.platform !== 'win32' || path.parse(value).root.length > 1);
}

async function checkFile(file: string): Promise<void> {
  let info;
  try {
    info = await stat(file);
  } catch (error) {
    const code = (error as NodeJS.ErrnoException).code;
    if (code === 'ENOENT' || code === 'ENOTDIR') {
      throw new Error(`kimi.serverPath: file not found: ${file}`);
    }
    throw new Error(`kimi.serverPath: cannot access ${file} (${code ?? String(error)}).`);
  }
  if (!info.isFile()) {
    throw new Error(`kimi.serverPath: expected an executable file, but found a directory or another file type: ${file}`);
  }
  try {
    await access(file, constants.R_OK | constants.X_OK);
  } catch {
    throw new Error(`kimi.serverPath: the file cannot be read or executed: ${file}`);
  }
}

/** Resolve once so validation and process creation use the same absolute path. */
export async function resolveServerPath(setting: unknown, searchPath = process.env.PATH ?? ''): Promise<string> {
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
      } catch {
        // Keep searching: an earlier PATH entry can be missing or inaccessible.
      }
    }
  }
  throw new Error(`kimi.serverPath: no accessible executable named "${value}" was found on PATH. Set the absolute path to Kimi.exe.`);
}
